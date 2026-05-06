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

    ' Phase D wiring — see EditFace_Form for the full rationale. _editorHost owns this form's
    ' embedded preview; _mainForm is referenced only to invoke pipeline methods that still live
    ' on MainForm but accept an arbitrary host.
    Private _editorHost As NpcRenderHost = Nothing
    Private ReadOnly _mainForm As MainForm = Nothing
    Private ReadOnly _mainGore As Boolean = False
    ''' <summary>Set to True by OnOk; MainForm reads this after ShowDialog to decide whether to
    ''' re-render its main preview from the (now-mutated) overlay. Cancel rolls back the overlay
    ''' so the MainForm's preview is already correct without a reload.</summary>
    Public Property HasUncommittedChanges As Boolean = False

    ' Snapshot for Cancel rollback. Cloned at construction; if the user cancels we restore.
    Private ReadOnly _hadPriorOverlay As Boolean
    Private ReadOnly _priorPreset As LooksmenuLoader.LooksmenuPreset

    ' Per-MRSV slot labels + UI references. Populated in CreateMrsvRows.
    Private _mrsvBars(4) As FO4_Base_Library.TinySliderTextBox
    Private _suspendEvents As Boolean

    ' Set to True while we seed the render-toggle checkboxes from MainForm at Shown so their
    ' CheckedChanged handlers don't each rebuild Toggles + run ApplyRenderToggleVisibility per
    ' assignment (would be 4 redundant passes before the first render).
    Private _seedingToggles As Boolean

    ' Per-BodySlide-slider UI references. Key = sliderName (case-insensitive).
    Private ReadOnly _bodySlideBars As New Dictionary(Of String, FO4_Base_Library.TinySliderTextBox)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _bodySlideRows As New Dictionary(Of String, Control)(StringComparer.OrdinalIgnoreCase)

    ' Slider drag throttle: model writes happen synchronously inside On...Changed (so Save/OK
    ' captures fresh state) but the costly _refresh callback is deferred. Same pattern as
    ' Editor_Form.vb (WM): timer fires after the user pauses; DragEnded forces an immediate flush
    ' so releasing the mouse always shows the final preview without waiting for the timer tick.
    Private WithEvents _refreshTimer As New Timer() With {.Interval = 500, .Enabled = False}
    Private _pendingRefresh As Boolean = False

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

    Public Sub New(rootNpcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   hasMwgt As Boolean,
                   hasMrsv As Boolean,
                   availableSliders As List(Of String),
                   initial As InitialValues,
                   mainForm As MainForm,
                   mainGore As Boolean)
        InitializeComponent()
        _rootNpcFormID = rootNpcFormID
        _appliedPresets = appliedPresets
        _availableSliders = If(availableSliders, New List(Of String))
        _refresh = AddressOf OnLocalBodyRefresh
        _mainForm = mainForm
        _mainGore = mainGore

        ' Snapshot the existing overlay so Cancel can restore it byte-for-byte.
        Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
        _hadPriorOverlay = _appliedPresets.TryGetValue(rootNpcFormID, existing)
        _priorPreset = If(_hadPriorOverlay, ClonePreset(existing), Nothing)

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
        ' Reset section is the BodySlide-only reset (zero out preset.BodyMorphSliders). When there
        ' is no BodySlide section to reset there is nothing to do, so hide the button entirely
        ' rather than leave a no-op control. The Enable/Disable inside the BodySlide flow is
        ' separate (depends on whether the user actually moved any slider).
        ButtonResetSection.Visible = hasBodySlide
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
        ' Carry editor-only overrides through the snapshot so that an EditFace edit followed by
        ' an EditBody Cancel doesn't reset face-side overrides the user kept.
        c.IsCharGenFacePreset = p.IsCharGenFacePreset
        c.SkinFormIDOverride = p.SkinFormIDOverride
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
            Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
                .Minimum = -1R,
                .Maximum = 1R,
                .DisplayFormat = "0.00%",
                .InputScale = 0.01R,
                .SmallChange = 0.01R,
                .LargeChange = 0.1R,
                .FillMode = FO4_Base_Library.TinySliderFillMode.Center,
                .Height = 28,
                .Value = 0R,
                .Dock = DockStyle.Fill,
                .Margin = New Padding(2)
            }
            AddHandler bar.ValueChanged, Sub(s, e) OnMrsvChanged(idx)
            AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
            MrsvLayout.Controls.Add(lblText, 0, idx)
            MrsvLayout.Controls.Add(bar, 1, idx)
            _mrsvBars(idx) = bar
        Next
    End Sub

    ''' <summary>Build the dynamic BodySlide slider rows from the union of morph names
    ''' present in the loaded body shapes' PIRT .tri files.</summary>
    Private Sub CreateBodySlideRows()
        BodySlidePanel.SuspendLayout()
        Try
            BodySlidePanel.Controls.Clear()
            _bodySlideBars.Clear()
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
                    .ColumnCount = 2,
                    .RowCount = 1,
                    .AutoSize = True,
                    .AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    .Width = BodySlidePanel.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4,
                    .Margin = New Padding(0, 0, 0, 2)
                }
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 180))
                row.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
                row.RowStyles.Add(New RowStyle(SizeType.AutoSize))

                Dim lbl As New Label() With {
                    .Text = sliderName,
                    .AutoSize = False,
                    .Width = 180,
                    .TextAlign = ContentAlignment.MiddleLeft,
                    .Anchor = AnchorStyles.Left Or AnchorStyles.Right
                }
                Dim bar As New FO4_Base_Library.TinySliderTextBox() With {
                    .Minimum = 0R,
                    .Maximum = 100R,
                    .AllowExtremeValues = True,
                    .DisplayFormat = "0\%",
                    .SmallChange = 1R,
                    .LargeChange = 10R,
                    .Height = 28,
                    .Value = 0R,
                    .Dock = DockStyle.Fill,
                    .Margin = New Padding(2)
                }
                Dim capturedName = sliderName
                AddHandler bar.ValueChanged, Sub(s, e) OnBodySlideChanged(capturedName)
                AddHandler bar.DragEnded, AddressOf OnSliderDragEnded
                row.Controls.Add(lbl, 0, 0)
                row.Controls.Add(bar, 1, 0)

                BodySlidePanel.Controls.Add(row)
                _bodySlideBars(sliderName) = bar
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
            ' Echo the (already-normalized) values back into the linked sliders so the on-screen
            ' numbers match what the engine will see post-render. SyncMwgtSliders runs under
            ' _suspendEvents so the slider ValueChanged handlers don't recurse back into
            ' OnMwgtSliderChanged.
            SyncMwgtSliders(WeightTriangle.Thin, WeightTriangle.Muscular, WeightTriangle.Fat)
            ' MRSV — preset.BodyMorphValues already mirrors NPC.MRSV (5 floats in [-1..+1]).
            For i = 0 To 4
                Dim v As Single = If(i < p.BodyMorphValues.Count, p.BodyMorphValues(i), 0.0F)
                _mrsvBars(i).Value = v
            Next
            ' BodySlide — model stores 0..1 fractional; slider works in 0..100 scale (BodySlide canon).
            For Each kv In p.BodyMorphSliders
                Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
                If _bodySlideBars.TryGetValue(kv.Key, bar) Then
                    bar.Value = kv.Value * 100.0R
                End If
            Next
        Finally
            _suspendEvents = False
        End Try
    End Sub

    ''' <summary>Push (t, m, f) into the three linked TinySliderTextBox controls without
    ''' re-triggering their ValueChanged handlers. Caller mutates state — this only updates
    ''' the on-screen numbers.</summary>
    Private Sub SyncMwgtSliders(t As Single, m As Single, f As Single)
        Dim wasSuspended = _suspendEvents
        _suspendEvents = True
        Try
            SliderThin.Value = CDec(t)
            SliderMuscular.Value = CDec(m)
            SliderFat.Value = CDec(f)
        Finally
            _suspendEvents = wasSuspended
        End Try
    End Sub

    ''' <summary>Single mutation path for MWGT. Both the WeightTriangle drag handler and the
    ''' three linked sliders converge here. Writes the overlay preset, syncs the editor host's
    ''' dual cache (LastRenderedState + CurrentBaseState — required because BuildBodyWeightPose
    ''' reads from state.WeightX, NOT from the overlay; the overlay→state sync only runs on
    ''' full reload via ResolveNPCBaseState — see arch_npc_state_dual_cache.md), refreshes the
    ''' UI mirrors, and schedules a throttled refresh via the existing 500ms timer.</summary>
    Private Sub ApplyMwgt(t As Single, m As Single, f As Single)
        Dim p = Preset
        p.WeightThin = t
        p.WeightMuscular = m
        p.WeightFat = f
        ' Dual-cache sync per arch_npc_state_dual_cache.md: BuildBodyWeightPose reads
        ' state.WeightX (sentinel-substituted by ApplyRaceFallbacks) — not the overlay. During
        ' a live slider edit there is no full reload to re-run that sync, so we mutate both
        ' caches in place. Without this, the editor's preview would not reflect MWGT changes
        ' until the user closes the editor with OK and triggers a full reload.
        If _editorHost IsNot Nothing AndAlso _editorHost.LastRenderedState IsNot Nothing Then
            _editorHost.LastRenderedState.WeightThin = t
            _editorHost.LastRenderedState.WeightMuscular = m
            _editorHost.LastRenderedState.WeightFat = f
        End If
        If _editorHost IsNot Nothing AndAlso _editorHost.CurrentBaseState IsNot Nothing Then
            _editorHost.CurrentBaseState.WeightThin = t
            _editorHost.CurrentBaseState.WeightMuscular = m
            _editorHost.CurrentBaseState.WeightFat = f
        End If
        ' Throttled refresh — same path the BodySlide / MRSV sliders use. Drag many values
        ' through the slider without slamming the render pipeline; FlushRefresh on DragEnded
        ' guarantees the final value renders immediately.
        ScheduleRefresh()
    End Sub

    ''' <summary>Triangle drag handler. The triangle control already enforces sum=1 internally
    ''' (barycentric coordinates), so we just read its three values, mirror them into the linked
    ''' sliders, and route through ApplyMwgt.</summary>
    Private Sub OnWeightTriangleChanged(sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim t As Single = WeightTriangle.Thin
        Dim m As Single = WeightTriangle.Muscular
        Dim f As Single = WeightTriangle.Fat
        SyncMwgtSliders(t, m, f)
        ApplyMwgt(t, m, f)
    End Sub

    ''' <summary>Distribute the new value of one MWGT axis across the constrained simplex
    ''' (t + m + f = 1). Strategy: clamp the changed axis to [0..1], then split the remaining
    ''' (1 - changed) across the other two axes proportionally to their CURRENT relative ratio
    ''' so the user perceives the existing distribution being preserved as much as possible.
    '''
    ''' Edge cases:
    '''   • The other two are both 0 (corner of the simplex) → split (1 - changed) 50/50 so we
    '''     never divide by zero AND the user can see two non-zero handles to drag from.
    '''   • changed = 1 → others = 0 (vertex of the simplex).
    '''
    ''' axisIdx: 0=Thin, 1=Muscular, 2=Fat. Returns the new (t, m, f) triple.</summary>
    Private Function RedistributeMwgt(axisIdx As Integer, newValue As Single,
                                       currT As Single, currM As Single, currF As Single) _
                                       As (T As Single, M As Single, F As Single)
        Dim v As Single = Math.Max(0.0F, Math.Min(1.0F, newValue))
        Dim remaining As Single = 1.0F - v
        ' Pick the two "other" current values for proportional split.
        Dim aIdx As Integer, bIdx As Integer
        Select Case axisIdx
            Case 0 : aIdx = 1 : bIdx = 2  ' changed Thin → split between M and F
            Case 1 : aIdx = 0 : bIdx = 2  ' changed Muscular → split between T and F
            Case Else : aIdx = 0 : bIdx = 1  ' changed Fat → split between T and M
        End Select
        Dim curr() As Single = {currT, currM, currF}
        Dim a As Single = curr(aIdx)
        Dim b As Single = curr(bIdx)
        Dim sum As Single = a + b
        Dim na As Single, nb As Single
        If sum < 0.0001F Then
            ' Both other axes were at 0 — split remaining equally so the user has handles to grab.
            na = remaining * 0.5F
            nb = remaining * 0.5F
        Else
            na = remaining * (a / sum)
            nb = remaining * (b / sum)
        End If
        Dim res() As Single = {0.0F, 0.0F, 0.0F}
        res(axisIdx) = v
        res(aIdx) = na
        res(bIdx) = nb
        Return (res(0), res(1), res(2))
    End Function

    ''' <summary>One slider moved → redistribute, push the new triple into the WeightTriangle
    ''' (which absorbs them as its new barycentric position) and the other two sliders, then
    ''' apply.</summary>
    Private Sub OnMwgtSliderChanged(axisIdx As Integer, sender As Object, e As EventArgs)
        If _suspendEvents Then Return
        Dim newVal As Single
        Select Case axisIdx
            Case 0 : newVal = CSng(SliderThin.Value)
            Case 1 : newVal = CSng(SliderMuscular.Value)
            Case Else : newVal = CSng(SliderFat.Value)
        End Select
        Dim currT As Single = WeightTriangle.Thin
        Dim currM As Single = WeightTriangle.Muscular
        Dim currF As Single = WeightTriangle.Fat
        Dim r = RedistributeMwgt(axisIdx, newVal, currT, currM, currF)
        ' Push into triangle + sliders under suspend so we don't recurse.
        Dim wasSuspended = _suspendEvents
        _suspendEvents = True
        Try
            WeightTriangle.SetWeights(r.T, r.M, r.F)
        Finally
            _suspendEvents = wasSuspended
        End Try
        SyncMwgtSliders(r.T, r.M, r.F)
        ApplyMwgt(r.T, r.M, r.F)
    End Sub

    Private Sub OnSliderThinChanged(sender As Object, e As EventArgs) Handles SliderThin.ValueChanged
        OnMwgtSliderChanged(0, sender, e)
    End Sub

    Private Sub OnSliderMuscularChanged(sender As Object, e As EventArgs) Handles SliderMuscular.ValueChanged
        OnMwgtSliderChanged(1, sender, e)
    End Sub

    Private Sub OnSliderFatChanged(sender As Object, e As EventArgs) Handles SliderFat.ValueChanged
        OnMwgtSliderChanged(2, sender, e)
    End Sub

    ''' <summary>Force-flush the throttle on slider DragEnded so releasing the mouse always
    ''' renders the final value without waiting for the timer tick. Mirrors the same wiring
    ''' the MRSV / BodySlide sliders already use.</summary>
    Private Sub OnMwgtSliderDragEnded(sender As Object, e As EventArgs) _
        Handles SliderThin.DragEnded, SliderMuscular.DragEnded, SliderFat.DragEnded
        FlushRefresh()
    End Sub

    Private Sub OnMrsvChanged(idx As Integer)
        If _suspendEvents Then Return
        Dim v As Single = CSng(_mrsvBars(idx).Value)
        Dim p = Preset
        ' Ensure BodyMorphValues has 5 slots — overlay-apply expects positional MRSV.
        While p.BodyMorphValues.Count < 5
            p.BodyMorphValues.Add(0.0F)
        End While
        p.BodyMorphValues(idx) = v
        ScheduleRefresh()
    End Sub

    Private Sub OnBodySlideChanged(sliderName As String)
        If _suspendEvents Then Return
        Dim bar As FO4_Base_Library.TinySliderTextBox = Nothing
        If Not _bodySlideBars.TryGetValue(sliderName, bar) Then Return
        Dim v As Single = CSng(bar.Value / 100.0R)
        Dim p = Preset
        If Math.Abs(v) < 0.001F Then
            p.BodyMorphSliders.Remove(sliderName)
        Else
            p.BodyMorphSliders(sliderName) = v
        End If
        ScheduleRefresh()
    End Sub

    ''' <summary>Mark a refresh as pending and start the throttle timer if it isn't already
    ''' running. The model is already written; this only defers the costly _refresh callback.</summary>
    Private Sub ScheduleRefresh()
        _pendingRefresh = True
        If Not _refreshTimer.Enabled Then _refreshTimer.Start()
    End Sub

    ''' <summary>Force-flush any pending refresh immediately. Bound to every slider's DragEnded
    ''' so releasing the mouse shows the final preview without waiting for the timer tick.</summary>
    Private Sub FlushRefresh()
        If _pendingRefresh Then
            _pendingRefresh = False
            _refresh?.Invoke()
        End If
        _refreshTimer.Stop()
    End Sub

    Private Sub RefreshTimer_Tick(sender As Object, e As EventArgs) Handles _refreshTimer.Tick
        FlushRefresh()
    End Sub

    Private Sub OnSliderDragEnded(sender As Object, e As EventArgs)
        FlushRefresh()
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
                kv.Value.Value = 0R
            Next
        Finally
            _suspendEvents = False
        End Try
        _refresh?.Invoke()
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Live edits already applied to the overlay; flag MainForm so it reloads its preview.
        HasUncommittedChanges = True
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
        HasUncommittedChanges = False
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>Refresh dispatcher targeting the editor's embedded NpcRenderHost. MWGT and
    ''' MRSV affect the bone-scale pose (BuildBodyWeightPose), so we need a Pose dirty pass;
    ''' BodySlide sliders affect the vertex morph plan, so we also rebuild the MorphResolver
    ''' and mark Morphs dirty. Both flags can be set on the same intent.</summary>
    Private Sub OnLocalBodyRefresh()
        If _editorHost Is Nothing OrElse _mainForm Is Nothing Then Return
        If _editorHost.LastRenderedState Is Nothing OrElse _editorHost.LastRenderData Is Nothing Then Return
        Dim intent = _editorHost.PreviewCtl.Intent
        intent.MorphResolver = _mainForm.BuildCompositeMorphResolver(_editorHost.LastRenderedState, _editorHost.LastRenderData, _editorHost)
        intent.MarkDirty(RenderDirtyFlags.Morphs Or RenderDirtyFlags.Pose, _editorHost.LastRenderData.Shapes)
        ' Body-weight pose depends on overlay weights — rebuild it on the editor's host.
        _mainForm.RebuildAndApplyMergedPose(_editorHost)
        _editorHost.PreviewCtl.InvalidateRender()
    End Sub

    Protected Overrides Sub OnFormClosed(e As FormClosedEventArgs)
        ' Flush any in-flight throttled refresh so OK doesn't leave a deferred render hanging,
        ' then stop the timer so its tick doesn't fire on a disposed form.
        FlushRefresh()
        _refreshTimer.Stop()
        _refreshTimer.Dispose()
        MyBase.OnFormClosed(e)
    End Sub

    ' =====================================================================
    ' Embedded preview lifecycle (Shown / FormClosing)
    '
    ' Pattern adopted from Wardrobe_Manager Editor_Form.vb:1046 and
    ' CreatefromNif_Form.vb:36 — the PreviewControl is created in Shown (NOT in
    ' .ctor / Designer) so its OpenGL context is created when the form is actually
    ' visible. FormClosing tears it down explicitly so the GL resources are released
    ' before the form's own Dispose runs.
    ' =====================================================================
    Private WithEvents EditPreviewControl As PreviewControl = Nothing

    Private Async Sub EditBodyForm_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        EditPreviewControl = New PreviewControl() With {.Dock = System.Windows.Forms.DockStyle.Fill}
        PreviewHostPanel.Controls.Add(EditPreviewControl)

        ' Seed the per-editor visibility checkboxes from MainForm so the embedded preview opens
        ' with whatever the user had in the main viewer. Done BEFORE the Toggles snapshot below
        ' so the snapshot reads the editor's own checkboxes (single source of truth from now on).
        ' _seedingToggles guards against the CheckedChanged handlers firing N times during seed —
        ' each .Checked = ... assignment would otherwise trigger a full visibility pass per box.
        _seedingToggles = True
        Try
            CheckBoxRenderUnderarmor.Checked = _mainForm.CheckBoxRenderUnderarmor.Checked
            CheckBoxRenderArmor.Checked = _mainForm.CheckBoxRenderArmor.Checked
            CheckBoxRenderHeadwear.Checked = _mainForm.CheckBoxRenderHeadwear.Checked
            CheckBoxRenderGore.Checked = _mainForm.CheckBoxRenderGore.Checked
        Finally
            _seedingToggles = False
        End Try

        _editorHost = New NpcRenderHost(EditPreviewControl)
        _editorHost.AppliedPresets = _appliedPresets
        ' Toggle preset uses FullBody as the morph/sculpt baseline (everything ON), then
        ' OVERWRITES the 4 visibility flags from the editor's own checkboxes. The editor
        ' checkboxes own the truth post-seed; CheckedChanged handlers below mutate them and
        ' rebuild the same way. _mainGore is no longer special — the editor's gore checkbox
        ' replaces it as the visibility input.
        _editorHost.Toggles = BuildTogglesFromEditorCheckboxes()
        ' Face tint deferral is now handled by the library's PostTextureUploadAction hook on
        ' RenderIntent — wired by RenderCurrentStateAsync inside the render dispatch path so
        ' editor hosts get the same generic post-texture sequencing the MainForm uses.

        If _mainForm IsNot Nothing Then
            Try
                Await _mainForm.RenderInHostAsync(_editorHost, _rootNpcFormID)
            Catch ex As Exception
                NpcPreviewLog.LogLazy(Function() $"  [EDITBODY-PREVIEW] initial render failed: {ex.Message}")
            End Try
        End If
    End Sub

    Private Sub EditBodyForm_FormClosing(sender As Object, e As System.Windows.Forms.FormClosingEventArgs) Handles Me.FormClosing
        ' Tear down host BEFORE the preview control — same ordering rationale as EditFace_Form.
        If _editorHost IsNot Nothing Then
            Try
                _editorHost.Dispose()
            Catch
            End Try
            _editorHost = Nothing
        End If
        If EditPreviewControl IsNot Nothing AndAlso Not EditPreviewControl.IsDisposed Then
            Try
                EditPreviewControl.Clean()
            Catch
            End Try
            Try
                EditPreviewControl.Dispose()
            Catch
            End Try
        End If
        EditPreviewControl = Nothing
    End Sub

    ''' <summary>Snapshot the editor's 4 visibility checkboxes into a fresh RenderToggles. The
    ''' morph/sculpt/body-weight/body-tri baseline is taken from FullBody (everything ON — body
    ''' editing wants the full pipeline running so the user can judge MWGT/MRSV/BodySlide
    ''' against the outfit). Only the 4 visibility flags come from the editor checkboxes;
    ''' RenderBody stays True (the editor never exposes the master-gate-of-3 toggle).</summary>
    Private Function BuildTogglesFromEditorCheckboxes() As RenderToggles
        Dim t = RenderToggles.FullBody(False) ' mainGore ignored — overwritten below.
        t.RenderUnderarmor = CheckBoxRenderUnderarmor.Checked
        t.RenderArmor = CheckBoxRenderArmor.Checked
        t.RenderHeadwear = CheckBoxRenderHeadwear.Checked
        t.RenderGore = CheckBoxRenderGore.Checked
        Return t
    End Function

    ''' <summary>Single CheckedChanged handler for all 4 render-visibility checkboxes. Same
    ''' shape MainForm uses (rebuild Toggles → ApplyRenderToggleVisibility) but pointed at
    ''' the editor's host instead of _renderHost. _seedingToggles short-circuits during the
    ''' Shown seed so we don't run 4 redundant visibility passes.</summary>
    Private Sub OnRenderToggleChanged(sender As Object, e As EventArgs) _
        Handles CheckBoxRenderUnderarmor.CheckedChanged,
                CheckBoxRenderArmor.CheckedChanged,
                CheckBoxRenderHeadwear.CheckedChanged,
                CheckBoxRenderGore.CheckedChanged
        If _seedingToggles Then Return
        If _editorHost Is Nothing Then Return
        _editorHost.Toggles = BuildTogglesFromEditorCheckboxes()
        _editorHost.ApplyRenderToggleVisibility()
    End Sub
End Class
