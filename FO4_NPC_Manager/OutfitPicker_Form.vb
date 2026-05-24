Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal dialog to change an NPC's default outfit (NPC.DOFT override). Modeled on
''' <see cref="HeadPartPicker_Form"/>: a race/gender-filtered list on the left, a lightweight raw
''' geometry preview on the right (no skinning / morphs / pose — just the outfit's world meshes for
''' the selected gender, same fidelity as the head-part picker).
'''
''' The candidate list comes from <see cref="MainForm.GetOutfitCandidates"/> (existing OTFT records
''' filtered per-ARMA by race+gender — never a synthesized cross-product). Two pinned entries sit on
''' top, mirroring EditBody's skin combo:
'''   • "(record default: …)" → <see cref="SelectedOutfitOverride"/> = Nothing (clear override).
'''   • "(no outfit)"          → Some(0) (render naked).
''' Every other row → Some(OTFT FormID).
'''
''' Preview samples ONE realization via MainForm.ResolveOutfitPreviewMeshPaths (LVLI random roll is
''' resolved there); the deterministic full expansion is only used for the list filter.</summary>
Public Class OutfitPicker_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean
    Private ReadOnly _candidates As New List(Of Candidate)
    Private _filtered As List(Of Candidate)

    ''' <summary>Result of the picker. Nothing = "(record default)" (clear the overlay override and
    ''' preserve the raw NPC.DOFT); Some(0) = "(no outfit)"; Some(fid) = OTFT override. The caller
    ''' writes this straight to <c>preset.DefaultOutfitFormIDOverride</c>.</summary>
    Public Property SelectedOutfitOverride As UInteger?

    ''' <summary>GLControl previewing the selected outfit's world meshes. Created in code-behind —
    ''' GLControl needs a live OpenGL context the Designer can't provide.</summary>
    Private _preview As PreviewControl
    Private _previewLoadInProgress As Boolean
    Private _lastPreviewKey As String = Nothing

    Private Class Candidate
        Public Display As String
        Public FormIDText As String = ""
        ''' <summary>Value returned on OK (Nothing / Some(0) / Some(fid)).</summary>
        Public OverrideValue As UInteger?
        ''' <summary>OTFT FormID to render in the preview (0 = render nothing).</summary>
        Public PreviewOutfitFID As UInteger
        ''' <summary>False for the pinned entries — always shown regardless of the filter text.</summary>
        Public Filterable As Boolean = True
    End Class

    ''' <param name="currentEffectiveOutfitFID">The outfit the NPC is rendering right now (post any
    ''' existing override) — used to pre-select the matching row.</param>
    ''' <param name="rawRecordOutfitFID">The NPC's raw record DOFT — backs the "(record default)"
    ''' pinned entry and its preview.</param>
    Public Sub New(mainForm As MainForm,
                   raceFormID As UInteger,
                   raceEditorID As String,
                   isFemale As Boolean,
                   currentEffectiveOutfitFID As UInteger,
                   rawRecordOutfitFID As UInteger)
        InitializeComponent()
        _mainForm = mainForm
        _raceFormID = raceFormID
        _isFemale = isFemale
        Text = "Change Outfit"
        LabelHeader.Text = $"Outfits for race '{raceEditorID}' ({If(isFemale, "Female", "Male")}). Choose one:"

        BuildCandidates(rawRecordOutfitFID)
        _filtered = New List(Of Candidate)(_candidates)
        RefreshList()
        PreselectCurrent(currentEffectiveOutfitFID)

        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        AddHandler ListViewParts.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ListViewParts.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub BuildCandidates(rawRecordOutfitFID As UInteger)
        ' Pinned entries (mirror EditBody's skin combo).
        Dim recordName = If(rawRecordOutfitFID <> 0UI, _mainForm.GetOutfitDisplayName(rawRecordOutfitFID), "none")
        _candidates.Add(New Candidate With {
            .Display = $"(record default: {recordName})",
            .OverrideValue = Nothing,
            .PreviewOutfitFID = rawRecordOutfitFID,
            .Filterable = False
        })
        _candidates.Add(New Candidate With {
            .Display = "(no outfit)",
            .OverrideValue = 0UI,
            .PreviewOutfitFID = 0UI,
            .Filterable = False
        })

        ' Race/gender-filtered OTFTs (cached in MainForm per race+gender).
        For Each cand In _mainForm.GetOutfitCandidates(_raceFormID, _isFemale)
            _candidates.Add(New Candidate With {
                .Display = cand.DisplayName,
                .FormIDText = cand.FormID.ToString("X8"),
                .OverrideValue = cand.FormID,
                .PreviewOutfitFID = cand.FormID,
                .Filterable = True
            })
        Next
    End Sub

    Private Sub RefreshList()
        ListViewParts.BeginUpdate()
        Try
            ListViewParts.Items.Clear()
            For Each c In _filtered
                Dim row As New ListViewItem(c.Display)
                row.SubItems.Add(c.FormIDText)
                row.Tag = c
                ListViewParts.Items.Add(row)
            Next
        Finally
            ListViewParts.EndUpdate()
        End Try
    End Sub

    ''' <summary>Select the row matching the NPC's current effective outfit: a real candidate by
    ''' FormID when one matches, else "(no outfit)" when the NPC is currently bare, else "(record
    ''' default)". Runs against the freshly-populated (unfiltered) list so indices align.</summary>
    Private Sub PreselectCurrent(currentEffectiveOutfitFID As UInteger)
        Dim idx As Integer = -1
        If currentEffectiveOutfitFID <> 0UI Then
            For i = 0 To _filtered.Count - 1
                If _filtered(i).OverrideValue.HasValue AndAlso _filtered(i).OverrideValue.Value = currentEffectiveOutfitFID Then
                    idx = i : Exit For
                End If
            Next
        End If
        If idx < 0 Then idx = If(currentEffectiveOutfitFID = 0UI, 1, 0) ' (no outfit) : (record default)
        If idx >= 0 AndAlso idx < ListViewParts.Items.Count Then
            ListViewParts.Items(idx).Selected = True
            ListViewParts.Items(idx).EnsureVisible()
        End If
    End Sub

    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        Dim text = TextBoxFilter.Text.Trim()
        If text.Length = 0 Then
            _filtered = New List(Of Candidate)(_candidates)
        Else
            ' Pinned entries (Filterable=False) always survive the filter.
            _filtered = _candidates.Where(Function(c) _
                Not c.Filterable OrElse c.Display.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0).ToList()
        End If
        RefreshList()
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Re-load the selected outfit's world meshes into the preview. Cheapest pipeline:
    ''' MainForm resolves one realization's mesh paths, we read each NIF from FilesDictionary and
    ''' hand the raw shapes to PreviewControl (no skinning resolver / morphs / pose) — same approach
    ''' HeadPartPicker_Form uses for HDPTs.</summary>
    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewLoadInProgress Then Return
        If ListViewParts.SelectedItems.Count = 0 Then
            ClearPreview()
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        Dim key = c.PreviewOutfitFID.ToString("X8")
        If key = _lastPreviewKey Then Return

        _previewLoadInProgress = True
        Try
            If c.PreviewOutfitFID = 0UI Then
                ' "(no outfit)" or a record default of none → empty preview.
                _preview.RenderShapes(New List(Of IRenderableShape))
                _lastPreviewKey = key
                Return
            End If

            Dim allShapes As New List(Of IRenderableShape)
            For Each meshPath In _mainForm.ResolveOutfitPreviewMeshPaths(c.PreviewOutfitFID, _raceFormID, _isFemale)
                Dim dictKey = NormalizeMeshKey(meshPath)
                Dim loc As FilesDictionary_class.File_Location = Nothing
                If Not FilesDictionary_class.Dictionary.TryGetValue(dictKey, loc) Then Continue For
                Dim bytes As Byte() = Nothing
                Try
                    bytes = loc.GetBytes()
                Catch
                End Try
                If bytes Is Nothing OrElse bytes.Length = 0 Then Continue For

                Dim nif As New Nifcontent_Class_Manolo()
                Try
                    nif.Load_Manolo(bytes)
                Catch
                    Continue For
                End Try

                Dim shapes = NifRenderableShape.FromNif(nif)
                If shapes Is Nothing OrElse Not shapes.Any() Then Continue For
                allShapes.AddRange(shapes)
            Next

            _preview.RenderShapes(allShapes)
            _lastPreviewKey = key
        Finally
            _previewLoadInProgress = False
        End Try
    End Sub

    Private Sub ClearPreview()
        _lastPreviewKey = Nothing
        Try
            _preview?.RenderShapes(New List(Of IRenderableShape))
        Catch
        End Try
    End Sub

    ''' <summary>FilesDictionary keys are lowercase paths starting with "meshes\". ARMA mesh paths
    ''' may or may not include the prefix — normalize both shapes. Same logic as HeadPartPicker_Form.</summary>
    Private Shared Function NormalizeMeshKey(rawPath As String) As String
        If String.IsNullOrEmpty(rawPath) Then Return ""
        Dim p = rawPath.Replace("/"c, "\"c).Trim().ToLowerInvariant()
        If Not p.StartsWith("meshes\") Then p = "meshes\" & p
        Return p
    End Function

    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        SelectedOutfitOverride = c.OverrideValue
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub OutfitPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        End If

        If ListViewParts.SelectedItems.Count > 0 Then
            OnListSelectionChanged(Me, EventArgs.Empty)
        Else
            ClearPreview()
        End If
    End Sub

    Private Sub OutfitPicker_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            _preview.Clean()
            _preview.Dispose()
        End If
    End Sub
End Class
