Imports System.Globalization
Imports FO4_Base_Library

''' <summary>Editor for an NPC's body weight (MWGT — 3 sliders), MRSV body morph regions
''' (5 vanilla regions per wbDefinitionsFO4.pas:10793) and BodySlide vertex sliders (PIRT
''' .tri morphs from the loaded body NIF, F4SE-only field).
'''
''' Live edit: every slider drag mutates the LooksMenu preset overlay (_appliedPresets) on
''' the host MainForm and triggers a granular repaint via the supplied refresh callback.
''' OK confirms (commits the live edit). Cancel restores the snapshot taken when the form
''' opened, then refreshes one last time so the preview reverts.
'''
''' Pipeline reminder (vanilla bones first, BodySlide vertex on top):
'''   1. BuildBodyWeightPose applies MWGT (Layer 1) + NNAM + MRSV (Layer 3) + ARMA (Layer 4)
'''      to the skeleton — bone scaling. No .tri.
'''   2. MorphEngine.ApplyMorphPlan applies face FRTRI003 morphs + BodySlide PIRT morphs to
'''      NifLocalVertices pre-skin. Skinning then transforms the morphed verts with the
'''      already-scaled bones.
''' </summary>
Public Class EditBody_Form

    Private ReadOnly _rootNpcFormID As UInteger
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _refresh As Action
    Private ReadOnly _availableSliders As List(Of String)

    ' Snapshot for Cancel rollback. Cloned at construction; if the user cancels we restore.
    Private ReadOnly _hadPriorOverlay As Boolean
    Private ReadOnly _priorPreset As LooksmenuLoader.LooksmenuPreset
    Private ReadOnly _priorMwgt As (Thin As Single, Muscular As Single, Fat As Single)

    ' Per-MRSV slot labels + UI references. Populated in CreateMrsvRows.
    Private _mrsvBars(4) As TrackBar
    Private _mrsvLabels(4) As Label
    Private _suspendEvents As Boolean

    ' Per-BodySlide-slider UI references. Key = sliderName (case-insensitive).
    Private ReadOnly _bodySlideBars As New Dictionary(Of String, TrackBar)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _bodySlideLabels As New Dictionary(Of String, Label)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _bodySlideRows As New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Initial values seeded from the live NPC (post-overlay-applied). Used to
    ''' populate sliders the very first time the editor opens against an NPC that has no
    ''' overlay yet — without this we'd show all zeros even when the record carries values.</summary>
    Public Class InitialValues
        Public Thin As Single
        Public Muscular As Single
        Public Fat As Single
        Public Mrsv As Single() = New Single() {0, 0, 0, 0, 0}
        Public BodySlide As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
    End Class

    ' Hook invoked when MWGT changes, so the host can sync state.WeightX (which is what
    ' BuildBodyWeightPose actually reads — the overlay preset alone is insufficient).
    Private ReadOnly _onMwgtChanged As Action(Of Single, Single, Single)

    Public Sub New(rootNpcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   hasMwgt As Boolean,
                   hasMrsv As Boolean,
                   availableSliders As List(Of String),
                   initial As InitialValues,
                   refresh As Action,
                   onMwgtChanged As Action(Of Single, Single, Single))
        InitializeComponent()
        _rootNpcFormID = rootNpcFormID
        _appliedPresets = appliedPresets
        _availableSliders = If(availableSliders, New List(Of String))
        _refresh = refresh
        _onMwgtChanged = onMwgtChanged

        ' Snapshot the existing overlay so Cancel can restore it byte-for-byte.
        Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
        _hadPriorOverlay = _appliedPresets.TryGetValue(rootNpcFormID, existing)
        _priorPreset = If(_hadPriorOverlay, ClonePreset(existing), Nothing)
        _priorMwgt = (initial.Thin, initial.Muscular, initial.Fat)

        ' Ensure an overlay preset exists for live editing — even if the NPC currently has none.
        ' We'll roll it back in Cancel if the user bails out.
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(rootNpcFormID, p) OrElse p Is Nothing Then
            p = New LooksmenuLoader.LooksmenuPreset()
            _appliedPresets(rootNpcFormID) = p
        End If

        ' Seed missing slots in the overlay from the NPC's current effective values, so the
        ' sliders open at the NPC's real state instead of all zeros. Only fills slots the
        ' overlay didn't already define (preserves any prior preset/edit).
        SeedOverlayFromInitial(p, initial)

        ApplyAvailability(hasMwgt, hasMrsv, _availableSliders.Count > 0)

        If hasMrsv Then CreateMrsvRows()
        CreateBodySlideRows()

        AddHandler WeightTriangle.WeightChanged, AddressOf OnWeightTriangleChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
        AddHandler ButtonResetSection.Click, AddressOf OnResetBodySlide
        AddHandler TextBoxBodySlideFilter.TextChanged, AddressOf OnBodySlideFilterChanged

        LoadValuesFromOverlay()
    End Sub

    ''' <summary>Seed the overlay preset's editable channels from the NPC's current effective
    ''' values, but only when the overlay hasn't already taken ownership of that channel. This
    ''' lets a user open Edit Body on a fresh NPC and see its real Weight/MRSV/BodySlide state
    ''' rather than zeros, without trampling a preset they previously loaded.</summary>
    Private Shared Sub SeedOverlayFromInitial(p As LooksmenuLoader.LooksmenuPreset, initial As InitialValues)
        If initial Is Nothing Then Return
        If Not p.WeightThin.HasValue Then p.WeightThin = initial.Thin
        If Not p.WeightMuscular.HasValue Then p.WeightMuscular = initial.Muscular
        If Not p.WeightFat.HasValue Then p.WeightFat = initial.Fat
        If p.BodyMorphValues.Count = 0 AndAlso initial.Mrsv IsNot Nothing Then
            ' Always carry exactly 5 slots (vanilla MRSV layout), zero-padding if needed.
            For i = 0 To 4
                p.BodyMorphValues.Add(If(i < initial.Mrsv.Length, initial.Mrsv(i), 0.0F))
            Next
        End If
        If p.BodyMorphSliders.Count = 0 AndAlso initial.BodySlide IsNot Nothing Then
            For Each kv In initial.BodySlide
                p.BodyMorphSliders(kv.Key) = kv.Value
            Next
        End If
    End Sub

    ''' <summary>Hide / disable sections that don't apply to this race + body. Each section is
    ''' independent; we hide rather than gray out so the form stays compact when only one or two
    ''' apply (e.g. Ghoul race with no BSMS at all → only BodySlide section visible).</summary>
    Private Sub ApplyAvailability(hasMwgt As Boolean, hasMrsv As Boolean, hasBodySlide As Boolean)
        GroupBoxWeight.Visible = hasMwgt
        GroupBoxMrsv.Visible = hasMrsv
        ' BodySlide section only shows when at least one shape's NIF root has BODYTRI extra-data
        ' (LM-strict resolution per BodySlideTriResolver). Without that the engine wouldn't apply
        ' any BodyMorphs in-game either, so the section has no semantic meaning to expose.
        GroupBoxBodySlide.Visible = hasBodySlide
    End Sub

    ''' <summary>Deep-clone a LooksmenuPreset for snapshot/restore.</summary>
    Private Shared Function ClonePreset(p As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        If p Is Nothing Then Return Nothing
        Dim c As New LooksmenuLoader.LooksmenuPreset()
        c.SourcePath = p.SourcePath
        c.Gender = p.Gender
        c.HeadPartFormIDs.AddRange(p.HeadPartFormIDs)
        c.UnresolvedHeadParts.AddRange(p.UnresolvedHeadParts)
        c.HairColorFormID = p.HairColorFormID
        c.WeightThin = p.WeightThin
        c.WeightMuscular = p.WeightMuscular
        c.WeightFat = p.WeightFat
        For Each kv In p.ChargenFaceMorphs : c.ChargenFaceMorphs(kv.Key) = kv.Value : Next
        c.BodyMorphValues.AddRange(p.BodyMorphValues)
        For Each kv In p.FaceBoneRegions
            c.FaceBoneRegions(kv.Key) = If(kv.Value Is Nothing, Nothing, CType(kv.Value.Clone(), Single()))
        Next
        c.FacialMorphIntensity = p.FacialMorphIntensity
        c.FaceTintLayers.AddRange(p.FaceTintLayers)
        For Each kv In p.BodyMorphSliders : c.BodyMorphSliders(kv.Key) = kv.Value : Next
        c.UnsupportedCounts.Overlays = p.UnsupportedCounts.Overlays
        c.UnsupportedCounts.BodyMorphSliders = p.UnsupportedCounts.BodyMorphSliders
        c.UnsupportedCounts.HasSkinOverride = p.UnsupportedCounts.HasSkinOverride
        Return c
    End Function

    Private ReadOnly Property Preset As LooksmenuLoader.LooksmenuPreset
        Get
            Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
            _appliedPresets.TryGetValue(_rootNpcFormID, p)
            Return p
        End Get
    End Property

    ''' <summary>Build the 5 MRSV slider rows (Head/UpperTorso/Arms/LowerTorso/Legs).</summary>
    Private Sub CreateMrsvRows()
        For i = 0 To 4
            Dim idx = i  ' capture for closures
            Dim lblText As New Label() With {
                .Text = NpcMorphResolver.BodyRegionLabels(idx),
                .AutoSize = True,
                .MinimumSize = New Size(80, 0),
                .TextAlign = ContentAlignment.MiddleLeft,
                .Anchor = AnchorStyles.Left Or AnchorStyles.Right
            }
            Dim bar As New TrackBar() With {
                .Minimum = -100,
                .Maximum = 100,
                .TickFrequency = 25,
                .TickStyle = TickStyle.None,
                .AutoSize = False,
                .Height = 22,
                .Value = 0,
                .Dock = DockStyle.Fill,
                .Margin = New Padding(2)
            }
            Dim lblValue As New Label() With {
                .Text = "0.00",
                .AutoSize = True,
                .MinimumSize = New Size(50, 0),
                .TextAlign = ContentAlignment.MiddleRight
            }
            AddHandler bar.ValueChanged, Sub(s, e) OnMrsvChanged(idx)
            MrsvLayout.Controls.Add(lblText, 0, idx)
            MrsvLayout.Controls.Add(bar, 1, idx)
            MrsvLayout.Controls.Add(lblValue, 2, idx)
            _mrsvBars(idx) = bar
            _mrsvLabels(idx) = lblValue
        Next
    End Sub

    ''' <summary>Build the dynamic BodySlide slider rows from the union of morph names
    ''' present in the loaded body shapes' PIRT .tri files.</summary>
    Private Sub CreateBodySlideRows()
        BodySlidePanel.SuspendLayout()
        Try
            BodySlidePanel.Controls.Clear()
            _bodySlideBars.Clear()
            _bodySlideLabels.Clear()
            _bodySlideRows.Clear()
            If _availableSliders.Count = 0 Then
                Dim empty As New Label() With {
                    .Text = "No BodySlide PIRT .tri found for any body shape on this NPC.",
                    .AutoSize = True,
                    .ForeColor = Color.Gray,
                    .Padding = New Padding(8)
                }
                BodySlidePanel.Controls.Add(empty)
                ButtonResetSection.Enabled = False
                Return
            End If
            ButtonResetSection.Enabled = True
            For Each sliderName In _availableSliders
                Dim row As New TableLayoutPanel() With {
                    .ColumnCount = 3,
                    .RowCount = 1,
                    .AutoSize = True,
                    .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .Width = BodySlidePanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4,
                    .Margin = New Padding(0, 0, 0, 2)
                }
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 180))
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 60))
                row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

                Dim lbl As New Label() With {
                    .Text = sliderName,
                    .AutoSize = False,
                    .Width = 180,
                    .TextAlign = ContentAlignment.MiddleLeft,
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Right
                }
                Dim bar As New TrackBar() With {
                    .Minimum = 0,
                    .Maximum = 100,
                    .TickFrequency = 10,
                    .TickStyle = TickStyle.None,
                    .AutoSize = False,
                    .Height = 22,
                    .Value = 0,
                    .Dock = DockStyle.Fill,
                    .Margin = New Padding(2)
                }
                Dim val As New Label() With {
                    .Text = "0.00",
                    .AutoSize = False,
                    .Width = 60,
                    .TextAlign = ContentAlignment.MiddleRight,
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Right
                }
                Dim capturedName = sliderName
                AddHandler bar.ValueChanged, Sub(s, e) OnBodySlideChanged(capturedName)
                row.Controls.Add(lbl, 0, 0)
                row.Controls.Add(bar, 1, 0)
                row.Controls.Add(val, 2, 0)

                BodySlidePanel.Controls.Add(row)
                _bodySlideBars(sliderName) = bar
                _bodySlideLabels(sliderName) = val
                _bodySlideRows(sliderName) = row
            Next
        Finally
            BodySlidePanel.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Initialize all slider positions from the current overlay preset state.</summary>
    Private Sub LoadValuesFromOverlay()
        _suspendEvents = True
        Try
            Dim p = Preset
            ' Weight (MWGT) — barycentric triangle. Values are normalized to sum=1 by the control.
            Dim t = p.WeightThin.GetValueOrDefault(0.0F)
            Dim m = p.WeightMuscular.GetValueOrDefault(0.0F)
            Dim f = p.WeightFat.GetValueOrDefault(0.0F)
            WeightTriangle.SetWeights(t, m, f)
            ' Echo the (already-normalized) values back into labels and the overlay so the
            ' on-screen numbers match exactly what the engine will see post-render.
            UpdateLabel(LabelThinValue, WeightTriangle.Thin)
            UpdateLabel(LabelMuscularValue, WeightTriangle.Muscular)
            UpdateLabel(LabelFatValue, WeightTriangle.Fat)
            ' MRSV — preset.BodyMorphValues already mirrors NPC.MRSV (5 floats).
            For i = 0 To 4
                Dim v As Single = If(i < p.BodyMorphValues.Count, p.BodyMorphValues(i), 0.0F)
                _mrsvBars(i).Value = ToTrackInt(v, -100, 100)
                UpdateLabel(_mrsvLabels(i), v)
            Next
            ' BodySlide
            For Each kv In p.BodyMorphSliders
                Dim bar As TrackBar = Nothing
                Dim lbl As Label = Nothing
                If _bodySlideBars.TryGetValue(kv.Key, bar) AndAlso _bodySlideLabels.TryGetValue(kv.Key, lbl) Then
                    bar.Value = ToTrackInt(kv.Value, 0, 100)
                    UpdateLabel(lbl, kv.Value)
                End If
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    Private Shared Function ToTrackInt(value As Single, lo As Integer, hi As Integer) As Integer
        Dim scaled = CInt(Math.Round(value * 100.0F))
        If scaled < lo Then Return lo
        If scaled > hi Then Return hi
        Return scaled
    End Function

    Private Shared Sub UpdateLabel(lbl As Label, value As Single)
        lbl.Text = value.ToString("F2", CultureInfo.InvariantCulture)
    End Sub

    Private Sub OnWeightTriangleChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim t As Single = WeightTriangle.Thin
        Dim m As Single = WeightTriangle.Muscular
        Dim f As Single = WeightTriangle.Fat
        Dim p = Preset
        p.WeightThin = t
        p.WeightMuscular = m
        p.WeightFat = f
        UpdateLabel(LabelThinValue, t)
        UpdateLabel(LabelMuscularValue, m)
        UpdateLabel(LabelFatValue, f)
        ' BuildBodyWeightPose reads state.WeightX, not the overlay preset, so the host has to
        ' sync them. Without this hook drags only update the JSON, not the preview.
        _onMwgtChanged?.Invoke(t, m, f)
        _refresh?.Invoke()
    End Sub

    Private Sub OnMrsvChanged(idx As Integer)
        If _suspendEvents Then Return
        Dim v As Single = _mrsvBars(idx).Value / 100.0F
        Dim p = Preset
        ' Ensure BodyMorphValues has 5 slots — overlay-apply expects positional MRSV.
        While p.BodyMorphValues.Count < 5
            p.BodyMorphValues.Add(0.0F)
        End While
        p.BodyMorphValues(idx) = v
        UpdateLabel(_mrsvLabels(idx), v)
        _refresh?.Invoke()
    End Sub

    Private Sub OnBodySlideChanged(sliderName As String)
        If _suspendEvents Then Return
        Dim bar As TrackBar = Nothing
        If Not _bodySlideBars.TryGetValue(sliderName, bar) Then Return
        Dim v As Single = bar.Value / 100.0F
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.BodyMorphSliders.Remove(sliderName)
        Else
            p.BodyMorphSliders(sliderName) = v
        End If
        Dim lbl As Label = Nothing
        If _bodySlideLabels.TryGetValue(sliderName, lbl) Then UpdateLabel(lbl, v)
        _refresh?.Invoke()
    End Sub

    Private Sub OnBodySlideFilterChanged(sender As Object, e As EventArgs)
        Dim filter = TextBoxBodySlideFilter.Text.Trim()
        BodySlidePanel.SuspendLayout()
        Try
            For Each kv In _bodySlideRows
                Dim visible = (filter.Length = 0) OrElse
                              (kv.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                kv.Value.Visible = visible
            Next
        Finally
            BodySlidePanel.ResumeLayout()
        End Try
    End Sub

    Private Sub OnResetBodySlide(sender As Object, e As EventArgs)
        Dim p = Preset
        p.BodyMorphSliders.Clear()
        _suspendEvents = True
        Try
            For Each kv In _bodySlideBars
                kv.Value.Value = 0
            Next
            For Each kv In _bodySlideLabels
                UpdateLabel(kv.Value, 0.0F)
            Next
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Live edits already applied; nothing to do but close.
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub OnCancel(sender As Object, e As EventArgs)
        ' Restore the snapshot taken when the form opened.
        If _hadPriorOverlay Then
            _appliedPresets(_rootNpcFormID) = _priorPreset
        Else
            _appliedPresets.Remove(_rootNpcFormID)
        End If
        ' MWGT lives on _lastRenderedState (read by BuildBodyWeightPose), not on the preset alone.
        ' Re-sync the host's state with the snapshot before triggering the final refresh.
        _onMwgtChanged?.Invoke(_priorMwgt.Thin, _priorMwgt.Muscular, _priorMwgt.Fat)
        _refresh?.Invoke()
        DialogResult = DialogResult.Cancel
        Close()
    End Sub
End Class
