Imports System.Drawing
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal dialog to change an NPC's default outfit (NPC.DOFT override). A race/gender-filtered
''' list on the left, a WYSIWYG preview on the right: the NPC is rendered wearing the selected/assembled
''' outfit through the EXACT same pipeline as the main viewer (<see cref="MainForm.PreviewOutfitInHostAsync"/>
''' → RenderInHostAsync → CollectMeshCandidates → SelectWinningCandidates → skinning/morphs/pose/tints).
''' There is no separate "lightweight" outfit resolver — what the picker shows is what the main render
''' produces (OMOD addon-index resolution, ARMO WorldModel fallback, slot-conflict elimination, chunk
''' mounting, body weight, all included).
'''
''' The candidate list comes from <see cref="MainForm.GetOutfitCandidates"/> (existing OTFT records
''' filtered per-ARMA by race+gender — never a synthesized cross-product). Two pinned entries sit on
''' top, mirroring EditBody's skin combo:
'''   • "(record default: …)" → <see cref="SelectedOutfitOverride"/> = Nothing (clear override).
'''   • "(no outfit)"          → Some(0) (render naked).
''' Every other row → Some(OTFT FormID).
'''
''' Preview mechanics: each selection writes <c>DefaultOutfitFormIDOverride</c> into the SHARED overlay
''' preset and full-renders into this form's own <see cref="NpcRenderHost"/>. The caller
''' (<see cref="MainForm.ButtonEditOutfit_Click"/>) captures the prior override before opening and
''' restores it on cancel, so browsing is non-destructive. The Create tab previews its assembled set via
''' a throwaway draft (<see cref="OutfitDraft.PreviewDraftFormID"/>) dropped on close.</summary>
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

    ''' <summary>GLControl previewing the NPC wearing the selected outfit. Created in code-behind —
    ''' GLControl needs a live OpenGL context the Designer can't provide.</summary>
    Private _preview As PreviewControl
    ''' <summary>Per-preview render-pipeline state, wrapping <see cref="_preview"/>. Drives the full
    ''' WYSIWYG render via <see cref="MainForm.PreviewOutfitInHostAsync"/> — same mechanism EditBody /
    ''' EditFace use for their embedded previews.</summary>
    Private _host As NpcRenderHost
    ''' <summary>The NPC whose outfit is being changed. Needed to drive RenderInHostAsync and to write
    ''' the outfit override into the shared overlay.</summary>
    Private ReadOnly _npcFormID As UInteger
    ''' <summary>The MainForm's canonical per-NPC overlay dict, shared by reference so preview writes go
    ''' to the same place the render reads from (and the caller restores from on cancel).</summary>
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    ''' <summary>True once the throwaway Create-tab preview draft has been registered on MainForm, so
    ''' FormClosing knows to drop it.</summary>
    Private _previewDraftRegistered As Boolean
    ''' <summary>Dedup key of the last preview render, so re-selecting the same row / re-running an
    ''' unchanged Create assembly doesn't kick off a redundant (expensive) full render.</summary>
    Private _lastPreviewKey As String = Nothing
    ''' <summary>Guards against overlapping async previews stepping on each other.</summary>
    Private _previewInProgress As Boolean
    ' Pending (coalesced) preview request — see RequestPreviewAsync. While a render is in flight the newest
    ' request is parked here and consumed when that render finishes, so a fast sequence of selections never
    ' leaves the preview stuck on a stale one (the "selecting a chosen piece doesn't render it" bug).
    Private _pendingHasValue As Boolean
    Private _pendingOverride As UInteger?
    Private _pendingKey As String
    Private _pendingPieceOnly As Boolean
    Private _pendingDraftItems As List(Of UInteger)

    ' === Create tab state ===
    ''' <summary>The NPC's current effective outfit FormID — used to pre-fill the Create tab when the
    ''' user picks "Override the loaded outfit".</summary>
    Private ReadOnly _currentEffectiveOutfitFID As UInteger
    ''' <summary>All ARMO items selectable for this race/gender (MainForm.GetArmoItemCandidates).</summary>
    Private _itemCandidates As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
    Private _filteredItems As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
    ''' <summary>Pieces the user added to the outfit under construction (working set). Order = add
    ''' sequence; the slot-conflict resolver uses it for "last-added wins".</summary>
    Private ReadOnly _pieces As New List(Of PieceEntry)
    Private _pieceOrderCounter As Integer = 0
    ''' <summary>Which of the two Create lists the user last worked in — drives the "selected piece"
    ''' preview so it follows the FOCUSED list (False = top candidate-items list, True = bottom
    ''' chosen-pieces list). Set on each list's Enter. Defaults to the top list (the first populated).</summary>
    Private _pieceListHasPreviewFocus As Boolean = False
    ''' <summary>When the user committed an Override, the FormID + EditorID to keep.</summary>
    Private _overrideTargetFormID As UInteger = 0UI
    Private _overrideTargetEditorID As String = ""

    Private Class PieceEntry
        Public FormID As UInteger
        Public Display As String
        Public SlotMask As UInteger
        Public Order As Integer
        Public Plugin As String = ""
        ''' <summary>True if <see cref="FormID"/> is an LVLI (leveled list) rather than a concrete ARMO.</summary>
        Public IsLeveled As Boolean
        ''' <summary>For an LVLI piece: the currently-sampled terminal ARMO FormIDs (the realization shown +
        ''' rendered + conflict-checked). Re-sampled by Reroll. Nothing for a plain ARMO piece. The draft
        ''' persists the LVLI FormID, not this — so the saved outfit stays leveled.</summary>
        Public Realization As List(Of UInteger)
    End Class

    Private Class Candidate
        Public Display As String
        Public FormIDText As String = ""
        ''' <summary>Source plugin (esp/esm) name — shown in the Plugin column + matched by the filter.</summary>
        Public PluginText As String = ""
        ''' <summary>Value returned on OK (Nothing / Some(0) / Some(fid)).</summary>
        Public OverrideValue As UInteger?
        ''' <summary>OTFT FormID to render in the preview (0 = render nothing).</summary>
        Public PreviewOutfitFID As UInteger
        ''' <summary>False for the pinned entries — always shown regardless of the filter text.</summary>
        Public Filterable As Boolean = True
    End Class

    ''' <param name="npcFormID">The NPC being edited — drives the WYSIWYG render and the overlay write.</param>
    ''' <param name="appliedPresets">MainForm's canonical per-NPC overlay dict (shared by reference).</param>
    ''' <param name="currentEffectiveOutfitFID">The outfit the NPC is rendering right now (post any
    ''' existing override) — used to pre-select the matching row.</param>
    ''' <param name="rawRecordOutfitFID">The NPC's raw record DOFT — backs the "(record default)"
    ''' pinned entry and its preview.</param>
    Public Sub New(mainForm As MainForm,
                   npcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   raceFormID As UInteger,
                   raceEditorID As String,
                   isFemale As Boolean,
                   currentEffectiveOutfitFID As UInteger,
                   rawRecordOutfitFID As UInteger)
        InitializeComponent()
        _mainForm = mainForm
        _npcFormID = npcFormID
        _appliedPresets = appliedPresets
        _raceFormID = raceFormID
        _isFemale = isFemale
        _currentEffectiveOutfitFID = currentEffectiveOutfitFID
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

        ' --- Create tab ---
        LabelEdidPrefix.Text = "EDID: " & OutfitDraft.EditorIdPrefix
        _itemCandidates = _mainForm.GetArmoItemCandidatesWithDrafts(_raceFormID, _isFemale)
        _filteredItems = New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(_itemCandidates)
        RefreshItemList()
        RefreshPieces()
        AddHandler TextBoxItemFilter.TextChanged, AddressOf OnItemFilterChanged
        AddHandler ListViewItems.DoubleClick, AddressOf OnAddItem
        AddHandler ButtonAddItem.Click, AddressOf OnAddItem
        AddHandler ButtonRemovePiece.Click, AddressOf OnRemovePiece
        AddHandler ButtonReroll.Click, AddressOf OnReroll
        AddHandler ButtonNewLvl.Click, AddressOf OnNewLvl
        AddHandler ButtonAddToLvl.Click, AddressOf OnAddToLvl
        AddHandler RadioButtonOverride.CheckedChanged, AddressOf OnModeChanged
        AddHandler TabsMain.SelectedIndexChanged, AddressOf OnTabChanged
        ' Preview-mode toggle (whole outfit vs selected piece) + selection-driven piece preview.
        AddHandler RadioButtonRenderPiece.CheckedChanged, AddressOf OnCreatePreviewModeChanged
        AddHandler ListViewItems.SelectedIndexChanged, AddressOf OnCreateItemSelectionChanged
        AddHandler ListViewPieces.SelectedIndexChanged, AddressOf OnCreatePieceSelectionChanged
        ' The "selected piece" preview follows whichever Create list has focus — track the last one entered.
        AddHandler ListViewItems.Enter, AddressOf OnItemsListEnter
        AddHandler ListViewPieces.Enter, AddressOf OnPiecesListEnter

        ' Click-to-sort on every column of all three lists (same helper EditFace uses). The sorter
        ' persists across the lists' re-populations (filter/refresh), so a user-chosen sort survives.
        SortableListView.Attach(ListViewParts)
        SortableListView.Attach(ListViewItems)
        SortableListView.Attach(ListViewPieces)

        ' First open: stay in "New outfit" (the Designer default) but pre-load a COPY of the NPC's current
        ' outfit, so the user edits a fresh, isolated record and never risks changing a shared OTFT by
        ' accident. They can still switch to Override explicitly. With no current outfit there's nothing to
        ' copy → New starts empty. (New requires typing a name before OK, by design.)
        If _currentEffectiveOutfitFID <> 0UI Then PrefillPiecesFromOutfit(_currentEffectiveOutfitFID)
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
                .PluginText = _mainForm.GetOutfitPluginName(cand.FormID),
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
                row.SubItems.Add(c.PluginText)
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
            ' Pinned entries (Filterable=False) always survive the filter. Real rows match by name,
            ' plugin (esp/esm) or record ID — same fields the NPC list filter offers.
            _filtered = _candidates.Where(Function(c) _
                Not c.Filterable OrElse
                c.Display.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                c.PluginText.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                c.FormIDText.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()
        End If
        RefreshList()
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Browse selection changed → render the NPC wearing that outfit (WYSIWYG). Pinned
    ''' "(record default)" → Nothing; "(no outfit)" → Some(0); any other row → Some(fid). No selection
    ''' clears the preview.</summary>
    Private Async Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        If TabsMain.SelectedTab IsNot TabPageBrowse Then Return  ' the shared preview follows the active tab
        If ListViewParts.SelectedItems.Count = 0 Then
            ClearPreview()
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        ' Browse always shows the whole outfit on the full body.
        Await RequestPreviewAsync(c.OverrideValue, "browse:" & OverrideKey(c.OverrideValue), pieceOnly:=False)
    End Sub

    ''' <summary>Set the host render toggles for the next preview. <paramref name="pieceOnly"/>=True hides
    ''' the body skin / naked hands / head parts (RenderBody off) so ONLY the previewed piece shows;
    ''' False = full body (whole-outfit / Browse). Takes effect on the next render (callers set this right
    ''' before PreviewOverrideAsync, which re-renders because the dedup key changed with the mode).</summary>
    Private Sub ApplyPreviewToggles(pieceOnly As Boolean)
        If _host Is Nothing Then Return
        _host.Toggles = _mainForm.BuildOutfitPickerToggles()   ' FullBody + global gore
        ' Piece-only: collect ONLY the selected piece (the 1-item draft) — skip body/head at COLLECT time
        ' instead of skinning them and hiding via RenderBody=False. Renders just the piece on the posed,
        ' body-weighted skeleton (WYSIWYG, ~half the work). See NpcRenderHost.OnlyOutfitCollect.
        _host.OnlyOutfitCollect = pieceOnly
    End Sub

    ''' <summary>Coalescing preview request. Records the LATEST desired preview (override value + dedup
    ''' <paramref name="key"/> + whole-outfit/piece toggle + optional Create draft contents) and renders it
    ''' through the full main-viewer pipeline (<see cref="MainForm.PreviewOutfitInHostAsync"/>) — one
    ''' resolver, WYSIWYG, no lightweight raw-NIF path. If a render is already in flight the request is
    ''' parked as pending and the running loop picks up the most recent one when it finishes, so a fast
    ''' sequence of selections never DROPS the last one (the bug where selecting a chosen piece left the
    ''' preview stuck on the previously rendered candidate). For Create previews the throwaway draft is
    ''' (re)registered HERE, right before the render, so the rendered contents always match the request
    ''' being drawn even under rapid re-selection. <paramref name="draftItems"/> = Nothing for Browse (a
    ''' real outfit / clear / naked override).</summary>
    Private Async Function RequestPreviewAsync(overrideValue As UInteger?, key As String, pieceOnly As Boolean,
                                               Optional draftItems As List(Of UInteger) = Nothing) As Task
        _pendingHasValue = True
        _pendingOverride = overrideValue
        _pendingKey = key
        _pendingPieceOnly = pieceOnly
        _pendingDraftItems = draftItems
        If _previewInProgress Then Return   ' the in-flight loop will consume the latest pending request
        _previewInProgress = True
        Try
            While _pendingHasValue
                Dim reqOverride = _pendingOverride
                Dim reqKey = _pendingKey
                Dim reqPieceOnly = _pendingPieceOnly
                Dim reqDraftItems = _pendingDraftItems
                _pendingHasValue = False
                If reqKey = _lastPreviewKey Then Continue While   ' nothing changed since the last render
                If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
                ' Create previews: register the throwaway draft with THIS request's contents so the render
                ' reads exactly what we're about to mark as rendered (no stale-content race).
                If reqDraftItems IsNot Nothing Then
                    ' The preview draft is FLAT terminal ARMOs: ARMO pieces + each LVLI piece's current
                    ' cached realization (see FlattenPieces). So it renders exactly the sample shown in the
                    ' pieces list, with no fresh re-roll on every render.
                    Dim d As New OutfitDraft With {.FormID = OutfitDraft.PreviewDraftFormID,
                                                   .EditorID = OutfitDraft.EditorIdPrefix & "(preview)"}
                    d.ItemFormIDs.AddRange(reqDraftItems)
                    _mainForm.RegisterOutfitDraft(d)
                    _previewDraftRegistered = True
                End If
                ApplyPreviewToggles(reqPieceOnly)
                Try
                    Await _mainForm.PreviewOutfitInHostAsync(_host, _npcFormID, reqOverride)
                    _lastPreviewKey = reqKey
                Catch
                    ' A failed preview render must not break the dialog; the user can pick another outfit.
                End Try
            End While
        Finally
            _previewInProgress = False
        End Try
    End Function

    ''' <summary>Render the Create-tab assembled set (winners only) by (re)registering the throwaway
    ''' preview draft with its items and previewing that draft FormID — same code path Browse uses for a
    ''' real outfit. The draft is dropped on close (FormClosing).</summary>
    Private Async Function PreviewCreateAssemblyAsync(winnerFIDs As List(Of UInteger)) As Task
        ' Draft registration + toggles happen inside RequestPreviewAsync (coalesced with the render) so the
        ' rendered contents match the request even under rapid re-selection. Whole outfit = full body.
        Await RequestPreviewAsync(OutfitDraft.PreviewDraftFormID,
                                  "create:" & String.Join(",", winnerFIDs.Select(Function(f) f.ToString("X"))),
                                  pieceOnly:=False, draftItems:=winnerFIDs)
    End Function

    ''' <summary>Stable dedup token for an override value (Nothing / 0 / fid).</summary>
    Private Shared Function OverrideKey(v As UInteger?) As String
        If Not v.HasValue Then Return "raw"
        Return v.Value.ToString("X8")
    End Function

    ''' <summary>Render nothing (no Browse selection). Resets the dedup key so the next real selection
    ''' always re-renders.</summary>
    Private Sub ClearPreview()
        _lastPreviewKey = Nothing
        _pendingHasValue = False
        Try
            _preview?.RenderShapes(New List(Of IRenderableShape))
        Catch
        End Try
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' OK dispatches by the active tab (Browse picks an existing/draft outfit; Create commits the
        ' assembled draft). ButtonOk has DialogResult.OK in the Designer, so setting DialogResult.None
        ' inside a handler is how we VETO the auto-close (validation failure).
        If TabsMain.SelectedTab Is TabPageCreate Then
            CommitCreate()
            Return
        End If
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

    ' =====================================================================
    ' Create tab — assemble an outfit from individual ARMO items
    ' =====================================================================

    Private Sub OnItemFilterChanged(sender As Object, e As EventArgs)
        Dim text = TextBoxItemFilter.Text.Trim()
        If text.Length = 0 Then
            _filteredItems = New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(_itemCandidates)
        Else
            ' Match by name, record ID, plugin (esp/esm) or slot name.
            _filteredItems = _itemCandidates.Where(Function(it) _
                it.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                it.FormID.ToString("X8").Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                it.Plugin.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                DescribeSlotMask(it.SlotMask).Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()
        End If
        RefreshItemList()
    End Sub

    Private Sub RefreshItemList()
        ' Preserve the top-list selection across the rebuild (candidate refresh / filter change), same as the
        ' pieces list — so "Add to lvl" doesn't drop the item you just picked. No-op if it's filtered out.
        Dim keepFid As UInteger = SelectedItemFormID()
        ListViewItems.BeginUpdate()
        Try
            ListViewItems.Items.Clear()
            For Each it In _filteredItems
                Dim isLvl = _mainForm.IsLeveledItem(it.FormID)
                Dim row As New ListViewItem(If(isLvl, "🎲 " & it.DisplayName, it.DisplayName))
                row.SubItems.Add(DescribeSlotMask(it.SlotMask))
                row.SubItems.Add(it.FormID.ToString("X8"))
                row.SubItems.Add(it.Plugin)
                row.Tag = it.FormID
                If keepFid <> 0UI AndAlso it.FormID = keepFid Then row.Selected = True
                ListViewItems.Items.Add(row)
            Next
        Finally
            ListViewItems.EndUpdate()
        End Try
        If ListViewItems.SelectedItems.Count > 0 Then ListViewItems.SelectedItems(0).EnsureVisible()
    End Sub

    Private Sub OnAddItem(sender As Object, e As EventArgs)
        If ListViewItems.SelectedItems.Count = 0 Then Return
        AddItemFidAsPiece(CUInt(ListViewItems.SelectedItems(0).Tag))
    End Sub

    ''' <summary>Add an item (ARMO or LVLI, including an own draft LVL) to the pieces list by FormID: dedup,
    ''' build the <see cref="PieceEntry"/>, sample a realization for leveled items, then refresh. Shared by
    ''' "Add to outfit" and by "New LVL…" (which auto-adds the freshly-created list as a piece).</summary>
    Private Sub AddItemFidAsPiece(fid As UInteger)
        If fid = 0UI Then Return
        If _pieces.Any(Function(p) p.FormID = fid) Then Return  ' no exact-duplicate item (ARMO or LVLI)
        Dim it = _itemCandidates.FirstOrDefault(Function(x) x.FormID = fid)
        If it.FormID = 0UI Then Return
        _pieceOrderCounter += 1
        Dim piece As New PieceEntry With {.FormID = it.FormID, .Display = it.DisplayName, .SlotMask = it.SlotMask,
                                          .Order = _pieceOrderCounter, .Plugin = it.Plugin}
        ' LVLI piece: sample a realization now (approach A — its terminal(s) drive display/conflict/render).
        ' The draft keeps the LVLI FormID; Reroll re-samples this. A freshly-created empty LVL realizes to
        ' nothing (slot 0) until the user fills it via "Add to lvl".
        If _mainForm.IsLeveledItem(fid) Then
            piece.IsLeveled = True
            Dim r = _mainForm.SampleLeveledRealization(fid, _raceFormID, _isFemale)
            piece.Realization = r.Terminals
            piece.SlotMask = r.SlotMask
        End If
        _pieces.Add(piece)
        RefreshPieces()
    End Sub

    Private Sub OnRemovePiece(sender As Object, e As EventArgs)
        If ListViewPieces.SelectedItems.Count = 0 Then Return
        Dim p = TryCast(ListViewPieces.SelectedItems(0).Tag, PieceEntry)
        If p Is Nothing Then Return
        _pieces.Remove(p)
        RefreshPieces()
    End Sub

    ''' <summary>Reroll: re-sample every LVLI piece's realization (new terminals + slot), then refresh the
    ''' list + preview. Only meaningful when a leveled piece is present (the button is enabled accordingly).</summary>
    Private Sub OnReroll(sender As Object, e As EventArgs)
        Dim any As Boolean = False
        For Each p In _pieces
            If p.IsLeveled Then
                Dim r = _mainForm.SampleLeveledRealization(p.FormID, _raceFormID, _isFemale)
                p.Realization = r.Terminals
                p.SlotMask = r.SlotMask
                any = True
            End If
        Next
        If Not any Then Return
        _lastPreviewKey = Nothing   ' the piece FormIDs are unchanged; force the re-render of the new sample
        RefreshPieces()
    End Sub

    ''' <summary>Rebuild the Create item-candidate list (after a new LVL draft is created) so own LVL drafts
    ''' (🎲) appear/update, then re-apply the current filter.</summary>
    Private Sub RefreshItemCandidates()
        _itemCandidates = _mainForm.GetArmoItemCandidatesWithDrafts(_raceFormID, _isFemale)
        OnItemFilterChanged(Me, EventArgs.Empty)   ' re-filters + RefreshItemList
    End Sub

    ''' <summary>"New LVL…" → modal (name + 3 LVLF flags + Chance None + Max Count) → register an empty own
    ''' LeveledListDraft, which then shows in the item list (🎲) ready to be filled via "Add to lvl".</summary>
    Private Sub OnNewLvl(sender As Object, e As EventArgs)
        Using dlg As New LeveledListEditor_Form(_mainForm)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim d As New LeveledListDraft With {
                .FormID = _mainForm.AllocateDraftFormID(),
                .EditorID = dlg.FullEditorID,
                .CalcAllLevels = dlg.CalcAllLevels,
                .CalcEachInCount = dlg.CalcEachInCount,
                .UseAll = dlg.UseAll,
                .ChanceNone = dlg.ChanceNoneValue,
                .MaxCount = dlg.MaxCountValue,
                .IsNew = True
            }
            _mainForm.RegisterLeveledListDraft(d)
            RefreshItemCandidates()
            ' Auto-add the new (empty) leveled list as a piece, then select it so "Add to lvl" is enabled
            ' immediately — the user creates the list and starts filling it without an extra "Add" step.
            AddItemFidAsPiece(d.FormID)
            SelectPieceByFormID(d.FormID)
        End Using
    End Sub

    ''' <summary>"Add to lvl": put the item selected in the candidate list (top) into the OWN leveled-list
    ''' piece selected in the pieces list (bottom), prompting for the entry Level/Count/ChanceNone. Blocks
    ''' self-insert and cycles (lvl1→lvl2→lvl1).</summary>
    Private Sub OnAddToLvl(sender As Object, e As EventArgs)
        Dim target = SelectedPieceEntry()
        If target Is Nothing OrElse Not _mainForm.IsOwnLeveledDraft(target.FormID) Then Return
        Dim itemFid = SelectedItemFormID()
        If itemFid = 0UI Then
            MessageBox.Show(Me, "Select an item in the top list to add into the leveled list.", "Add to lvl",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If _mainForm.WouldCreateLeveledCycle(target.FormID, itemFid) Then
            MessageBox.Show(Me, "That would create a leveled-list cycle (the list would end up containing itself).",
                            "Add to lvl", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Dim lvl = _mainForm.TryGetLeveledListDraft(target.FormID)
        If lvl Is Nothing Then Return
        Dim itemName = _itemCandidates.Where(Function(x) x.FormID = itemFid).Select(Function(x) x.DisplayName).FirstOrDefault()
        Using dlg As New LeveledEntryDialog_Form($"Add '{If(itemName, itemFid.ToString("X8"))}'  →  '{lvl.EditorID}'")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            lvl.Entries.Add(New LeveledListDraft.LeveledEntry With {
                .RefFormID = itemFid, .Level = dlg.LevelValue, .Count = dlg.CountValue, .ChanceNone = dlg.ChanceNoneValue})
            lvl.IsModified = True
        End Using
        ' The LVL's contents changed → re-sample the affected piece's realization + refresh list/candidates.
        Dim r = _mainForm.SampleLeveledRealization(target.FormID, _raceFormID, _isFemale)
        target.Realization = r.Terminals
        target.SlotMask = r.SlotMask
        RefreshItemCandidates()   ' the LVL's slot footprint in the candidate list may have changed
        _lastPreviewKey = Nothing
        RefreshPieces()
    End Sub

    ''' <summary>"Add to lvl" is enabled only when the selected piece (bottom) is an OWN leveled-list draft
    ''' (not a vanilla/loaded LVLI, not an ARMO).</summary>
    Private Sub UpdateAddToLvlEnabled()
        Dim p = SelectedPieceEntry()
        ButtonAddToLvl.Enabled = (p IsNot Nothing AndAlso _mainForm.IsOwnLeveledDraft(p.FormID))
    End Sub

    ''' <summary>Run the shared slot-conflict resolver over the assembled pieces, repaint the list
    ''' (✓ winners / ✗ eliminated, losers greyed), preview the resolved (winner) set, and update the
    ''' status line. Losers stay visible so the user sees what got eliminated and can remove a winner
    ''' to promote a loser; only winners are saved into the outfit (the resolved, conflict-free set).</summary>
    Private Async Sub RefreshPieces()
        ' Remember the selected piece so a rebuild (add item, add-to-lvl, reroll) doesn't drop the
        ' selection — the user can keep acting on the same piece (e.g. add several items into the same
        ' leveled list) without re-selecting it every time. No-op if that piece is gone (e.g. Remove).
        Dim keepSelectedFid As UInteger = If(SelectedPieceEntry()?.FormID, 0UI)

        Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order)
        Dim winners As New HashSet(Of PieceEntry)(res.Winners)
        ListViewPieces.BeginUpdate()
        Try
            ListViewPieces.Items.Clear()
            For Each p In _pieces.OrderBy(Function(x) x.Order)
                Dim isWin = winners.Contains(p)
                Dim row As New ListViewItem(If(p.IsLeveled, "🎲 " & p.Display, p.Display))
                row.SubItems.Add(DescribeSlotMask(p.SlotMask))
                row.SubItems.Add(If(isWin, "✓", "✗ eliminated"))
                row.SubItems.Add(p.Plugin)
                row.Tag = p
                If Not isWin Then row.ForeColor = Color.Gray
                If keepSelectedFid <> 0UI AndAlso p.FormID = keepSelectedFid Then row.Selected = True
                ListViewPieces.Items.Add(row)
            Next
        Finally
            ListViewPieces.EndUpdate()
        End Try
        Dim restored = ListViewPieces.SelectedItems.Count > 0
        If restored Then ListViewPieces.SelectedItems(0).EnsureVisible()

        ' Reroll only makes sense (and is only enabled) when there's a leveled list among the pieces.
        ButtonReroll.Enabled = _pieces.Any(Function(x) x.IsLeveled)
        UpdateAddToLvlEnabled()   ' reflects the (preserved) selection

        Dim losers = res.Losers.Count
        LabelCreateStatus.Text = $"{res.Winners.Count} piece(s) in outfit" & If(losers > 0, $"  ·  {losers} eliminated by slot conflict", "")

        ' Preview only when the Create tab is active and the host exists (skipped during construction,
        ' where the active tab is Browse and _host has not been created yet). The list repaint above ran
        ' synchronously before this Await, so the ✓/✗ feedback is immediate. RefreshCreatePreview honors
        ' the whole-outfit vs selected-piece toggle.
        If TabsMain.SelectedTab Is TabPageCreate AndAlso _host IsNot Nothing Then
            Await RefreshCreatePreview()
        End If
    End Sub

    ''' <summary>Render the Create-tab preview per the mode toggle: "whole outfit" → the assembled
    ''' (conflict-resolved) set; "selected piece only" → the single piece selected in whichever list has
    ''' focus (top items vs bottom pieces, via <see cref="CurrentSelectedPieceOrItemFormID"/>). Single
    ''' decision point — both list selections and the mode toggle route through here.</summary>
    Private Async Function RefreshCreatePreview() As Task
        If _host Is Nothing Then Return
        If RadioButtonRenderPiece.Checked Then
            Dim fid = CurrentSelectedPieceOrItemFormID()
            If fid <> 0UI Then
                Await PreviewCreatePieceAsync(fid)
            Else
                ClearPreview()
            End If
        Else
            Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order)
            ' Flatten to terminal ARMOs (LVLI winners → their cached realization) so the preview draft is
            ' the concrete sample currently shown in the list (Reroll changes it).
            Await PreviewCreateAssemblyAsync(FlattenPieces(res.Winners))
        End If
    End Function

    ''' <summary>Flatten pieces to render-ready terminal ARMO FormIDs: ARMO pieces pass through; LVLI pieces
    ''' expand to their cached realization. Used to build the FLAT preview draft (the committed draft keeps
    ''' the LVLI FormIDs).</summary>
    Private Function FlattenPieces(pieces As IEnumerable(Of PieceEntry)) As List(Of UInteger)
        Dim flat As New List(Of UInteger)
        For Each p In pieces
            If p.IsLeveled AndAlso p.Realization IsNot Nothing Then
                flat.AddRange(p.Realization)
            Else
                flat.Add(p.FormID)
            End If
        Next
        Return flat
    End Function

    ''' <summary>Terminal ARMO FormIDs to preview for a single selected FormID: a chosen LVLI piece → its
    ''' cached realization; a candidate LVLI item (not yet added) → a fresh sample; an ARMO → itself.</summary>
    Private Function RealizedTerminalsFor(fid As UInteger) As List(Of UInteger)
        Dim p = _pieces.FirstOrDefault(Function(x) x.FormID = fid)
        If p IsNot Nothing Then
            If p.IsLeveled AndAlso p.Realization IsNot Nothing Then Return New List(Of UInteger)(p.Realization)
            Return New List(Of UInteger) From {p.FormID}
        End If
        If _mainForm.IsLeveledItem(fid) Then Return _mainForm.SampleLeveledRealization(fid, _raceFormID, _isFemale).Terminals
        Return New List(Of UInteger) From {fid}
    End Function

    ''' <summary>Preview a single ARMO piece via the throwaway draft (one-item set) — same WYSIWYG host
    ''' path the assembly preview uses, just with one FormID.</summary>
    Private Async Function PreviewCreatePieceAsync(fid As UInteger) As Task
        ' Single-item preview; pieceOnly hides body/head so ONLY the piece shows. For an LVLI the realized
        ' terminal(s) are previewed (the key includes them so a Reroll re-renders). Draft register + toggles
        ' are coalesced with the render inside RequestPreviewAsync.
        Dim flat = RealizedTerminalsFor(fid)
        Await RequestPreviewAsync(OutfitDraft.PreviewDraftFormID,
                                  "piece:" & fid.ToString("X") & ":" & String.Join(",", flat.Select(Function(f) f.ToString("X"))),
                                  pieceOnly:=True, draftItems:=flat)
    End Function

    ''' <summary>Toggle whole-outfit ⇄ selected-piece. Re-renders per the new mode.</summary>
    Private Async Sub OnCreatePreviewModeChanged(sender As Object, e As EventArgs)
        If TabsMain.SelectedTab IsNot TabPageCreate Then Return
        Await RefreshCreatePreview()
    End Sub

    ' Both list selections route through the single RefreshCreatePreview decision point, which previews the
    ' focused list's selection (Enter sets _pieceListHasPreviewFocus first, before SelectedIndexChanged).
    Private Async Sub OnCreateItemSelectionChanged(sender As Object, e As EventArgs)
        If TabsMain.SelectedTab IsNot TabPageCreate OrElse Not RadioButtonRenderPiece.Checked Then Return
        Await RefreshCreatePreview()
    End Sub

    Private Async Sub OnCreatePieceSelectionChanged(sender As Object, e As EventArgs)
        UpdateAddToLvlEnabled()   ' applies regardless of preview mode
        If TabsMain.SelectedTab IsNot TabPageCreate OrElse Not RadioButtonRenderPiece.Checked Then Return
        Await RefreshCreatePreview()
    End Sub

    ' Focus-gain must (re)fire the piece preview for THIS list's selection: switching lists doesn't always
    ' change a selection (the target row may already be selected), so SelectedIndexChanged wouldn't fire and
    ' the render would stay on the other list's item. Only relevant in "selected piece only" mode on Create.
    Private Async Sub OnItemsListEnter(sender As Object, e As EventArgs)
        _pieceListHasPreviewFocus = False   ' top list (candidate items) now drives the piece preview
        If RadioButtonRenderPiece.Checked AndAlso TabsMain.SelectedTab Is TabPageCreate Then Await RefreshCreatePreview()
    End Sub

    Private Async Sub OnPiecesListEnter(sender As Object, e As EventArgs)
        _pieceListHasPreviewFocus = True    ' bottom list (chosen pieces) now drives the piece preview
        If RadioButtonRenderPiece.Checked AndAlso TabsMain.SelectedTab Is TabPageCreate Then Await RefreshCreatePreview()
    End Sub

    ''' <summary>FormID of the selected row in the items-to-choose list (0 if none).</summary>
    Private Function SelectedItemFormID() As UInteger
        If ListViewItems.SelectedItems.Count = 0 Then Return 0UI
        Return CUInt(ListViewItems.SelectedItems(0).Tag)
    End Function

    ''' <summary>The selected entry in the chosen-pieces list (Nothing if none).</summary>
    Private Function SelectedPieceEntry() As PieceEntry
        If ListViewPieces.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(ListViewPieces.SelectedItems(0).Tag, PieceEntry)
    End Function

    ''' <summary>Select the chosen-pieces row whose entry has <paramref name="fid"/> (used after "New LVL…"
    ''' auto-adds the new list, so it's the active selection and "Add to lvl" enables right away). No-op when
    ''' the FormID isn't in the list.</summary>
    Private Sub SelectPieceByFormID(fid As UInteger)
        For Each row As ListViewItem In ListViewPieces.Items
            Dim p = TryCast(row.Tag, PieceEntry)
            If p IsNot Nothing AndAlso p.FormID = fid Then
                ListViewPieces.SelectedItems.Clear()
                row.Selected = True
                row.EnsureVisible()
                UpdateAddToLvlEnabled()
                Return
            End If
        Next
    End Sub

    ''' <summary>FormID to preview in "selected piece" mode: the selection of whichever Create list the
    ''' user last had focus in (top = candidate items, bottom = chosen pieces, per
    ''' <see cref="_pieceListHasPreviewFocus"/>). Falls back to the other list's selection when the focused
    ''' one has none, and to 0 when neither has a selection.</summary>
    Private Function CurrentSelectedPieceOrItemFormID() As UInteger
        If _pieceListHasPreviewFocus Then
            Dim p = SelectedPieceEntry()
            If p IsNot Nothing Then Return p.FormID
            Return SelectedItemFormID()
        Else
            Dim fid = SelectedItemFormID()
            If fid <> 0UI Then Return fid
            Dim p = SelectedPieceEntry()
            Return If(p IsNot Nothing, p.FormID, 0UI)
        End If
    End Function

    ''' <summary>"New outfit" vs "Override the loaded outfit". Override keeps the current outfit's
    ''' EditorID + FormID and pre-fills its pieces; the EDID box is locked. New uses the
    ''' <c>npcm_Outfit_</c> prefix + a user-typed name (uniqueness-checked on commit).</summary>
    Private Sub OnModeChanged(sender As Object, e As EventArgs)
        If RadioButtonOverride.Checked Then
            Dim target = ResolveOverrideTarget()
            If target = 0UI Then
                MessageBox.Show(Me, "Select an outfit in the Browse tab (or have one loaded) to override. Use 'New outfit' otherwise.",
                                "Edit Outfit", MessageBoxButtons.OK, MessageBoxIcon.Information)
                RadioButtonNew.Checked = True
                Return
            End If
            _overrideTargetFormID = target
            _overrideTargetEditorID = _mainForm.GetOutfitDisplayName(target)
            LabelEdidPrefix.Text = "EDID (kept): "
            TextBoxEdid.Text = _overrideTargetEditorID
            TextBoxEdid.Enabled = False
            PrefillPiecesFromOutfit(target)
        Else
            _overrideTargetFormID = 0UI
            _overrideTargetEditorID = ""
            LabelEdidPrefix.Text = "EDID: " & OutfitDraft.EditorIdPrefix
            TextBoxEdid.Enabled = True
            TextBoxEdid.Text = ""
        End If
    End Sub

    ''' <summary>The outfit that Override edits: the one selected in the Browse list (a real outfit or
    ''' draft — not "(no outfit)"/"(record default)"), falling back to the NPC's currently-loaded
    ''' outfit when Browse has no concrete selection.</summary>
    Private Function ResolveOverrideTarget() As UInteger
        If ListViewParts.SelectedItems.Count > 0 Then
            Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
            If c IsNot Nothing AndAlso c.OverrideValue.HasValue AndAlso c.OverrideValue.Value <> 0UI Then
                Return c.OverrideValue.Value
            End If
        End If
        Return _currentEffectiveOutfitFID
    End Function

    ''' <summary>Seed the pieces list from an existing outfit's resolved ARMOs (only the ones that are
    ''' valid items for this race/gender — others wouldn't render anyway). Used by Override mode and by the
    ''' initial open to pre-load a fresh copy of the NPC's current outfit under "New".</summary>
    Private Sub PrefillPiecesFromOutfit(fid As UInteger)
        _pieces.Clear()
        _pieceOrderCounter = 0
        ' INAM items AS AUTHORED (ARMO or LVLI) — a leveled entry stays a leveled piece (not flattened).
        For Each itemFID In _mainForm.ResolveOutfitItemList(fid)
            Dim it = _itemCandidates.FirstOrDefault(Function(x) x.FormID = itemFID)
            If it.FormID = 0UI Then Continue For
            _pieceOrderCounter += 1
            Dim piece As New PieceEntry With {.FormID = it.FormID, .Display = it.DisplayName, .SlotMask = it.SlotMask,
                                              .Order = _pieceOrderCounter, .Plugin = it.Plugin}
            If _mainForm.IsLeveledItem(itemFID) Then
                piece.IsLeveled = True
                Dim r = _mainForm.SampleLeveledRealization(itemFID, _raceFormID, _isFemale)
                piece.Realization = r.Terminals
                piece.SlotMask = r.SlotMask
            End If
            _pieces.Add(piece)
        Next
        RefreshPieces()
    End Sub

    Private Sub OnTabChanged(sender As Object, e As EventArgs)
        _lastPreviewKey = Nothing  ' force a preview reload for the newly-active tab
        ' The piece/outfit radios only apply to Create — disabled on Browse (which always shows the
        ' whole outfit on the full body).
        PreviewModeRow.Enabled = (TabsMain.SelectedTab Is TabPageCreate)
        If TabsMain.SelectedTab Is TabPageCreate Then
            RefreshPieces()
        Else
            OnListSelectionChanged(Me, EventArgs.Empty)
        End If
    End Sub

    ''' <summary>Commit the Create tab: build the OutfitDraft (winners only = resolved set), register
    ''' it on MainForm, and auto-select it as the NPC's outfit (SelectedOutfitOverride = draft FormID).
    ''' New → prefix+name EDID (uniqueness-checked) + provisional FormID. Override → keep the existing
    ''' OTFT's FormID + EditorID. Vetoes the close (DialogResult.None) on validation failure.</summary>
    Private Sub CommitCreate()
        Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order)
        Dim winnerFIDs = res.Winners.Select(Function(p) p.FormID).ToList()
        If winnerFIDs.Count = 0 Then
            MessageBox.Show(Me, "Add at least one item to the outfit.", "Create Outfit",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        Dim draft As New OutfitDraft()
        If RadioButtonOverride.Checked AndAlso _overrideTargetFormID <> 0UI Then
            draft.FormID = _overrideTargetFormID
            draft.EditorID = _overrideTargetEditorID
            ' Real OTFT → write an override record (keep FormID+EDID). A draft target → re-edit that
            ' draft (still a new owned record, keep its provisional FormID+EDID).
            draft.IsOverride = Not OutfitDraft.IsDraftFormID(_overrideTargetFormID)
        Else
            Dim suffix = TextBoxEdid.Text.Trim()
            If suffix.Length = 0 Then
                MessageBox.Show(Me, "Enter a name for the new outfit.", "Create Outfit",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                DialogResult = DialogResult.None
                Return
            End If
            Dim fullEdid = OutfitDraft.EditorIdPrefix & suffix
            If Not _mainForm.IsOutfitEditorIdAvailable(fullEdid) Then
                MessageBox.Show(Me, $"EditorID '{fullEdid}' is already in use. Choose another name.",
                                "Create Outfit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                DialogResult = DialogResult.None
                Return
            End If
            draft.FormID = _mainForm.AllocateDraftFormID()
            draft.EditorID = fullEdid
            draft.IsOverride = False
        End If
        ' INAM = the winning piece FormIDs as authored — ARMO or LVLI (LVLIs persist as leveled entries).
        draft.ItemFormIDs.AddRange(winnerFIDs)
        ' Carry each LVLI piece's current realization to the draft so the committed render matches the
        ' picker's preview (until rerolled later). The draft still saves the LVLI ref in INAM.
        For Each p In res.Winners
            If p.IsLeveled AndAlso p.Realization IsNot Nothing Then
                draft.LvliRealization(p.FormID) = New List(Of UInteger)(p.Realization)
            End If
        Next
        draft.IsNew = True
        draft.IsModified = False
        _mainForm.RegisterOutfitDraft(draft)

        SelectedOutfitOverride = draft.FormID   ' auto-select the just-created outfit as the NPC's DOFT
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Human-readable list of the biped slots a mask occupies (FO4 slots 30-61).</summary>
    Private Shared Function DescribeSlotMask(mask As UInteger) As String
        If mask = 0UI Then Return "(none)"
        Dim names As New List(Of String)
        For bit = 0 To 31
            If (mask And (1UI << bit)) <> 0UI Then names.Add(SlotName(30 + bit))
        Next
        Return String.Join(", ", names)
    End Function

    Private Shared Function SlotName(slot As Integer) As String
        Select Case slot
            Case 30 : Return "HairTop"
            Case 31 : Return "HairLong"
            Case 32 : Return "FaceHead"
            Case 33 : Return "BODY"
            Case 34 : Return "LHand"
            Case 35 : Return "RHand"
            Case 36 : Return "[U]Torso"
            Case 37 : Return "[U]LArm"
            Case 38 : Return "[U]RArm"
            Case 39 : Return "[U]LLeg"
            Case 40 : Return "[U]RLeg"
            Case 41 : Return "[A]Torso"
            Case 42 : Return "[A]LArm"
            Case 43 : Return "[A]RArm"
            Case 44 : Return "[A]LLeg"
            Case 45 : Return "[A]RLeg"
            Case 46 : Return "Headband"
            Case 47 : Return "Eyes"
            Case 48 : Return "Beard"
            Case 49 : Return "Mouth"
            Case 50 : Return "Neck"
            Case 51 : Return "Ring"
            Case 52 : Return "Scalp"
            Case 53 : Return "Decap"
            ' 54-58 are "Unnamed" in vanilla FO4 (reserved/unused biped slots; mods repurpose them
            ' for custom gear to avoid clashing with vanilla equipment). wbDefinitionsFO4.pas:3770-3774.
            Case 54 : Return "Unnamed54"
            Case 55 : Return "Unnamed55"
            Case 56 : Return "Unnamed56"
            Case 57 : Return "Unnamed57"
            Case 58 : Return "Unnamed58"
            Case 59 : Return "Shield"
            Case 60 : Return "Pipboy"
            Case 61 : Return "FX"
            Case Else : Return "s" & slot.ToString()
        End Select
    End Function

    Private Sub OutfitPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        End If

        ' Own render host over our PreviewControl — same setup EditBody/EditFace use. AppliedPresets is
        ' the SHARED dict the render reads from; Toggles = FullBody so the outfit is judged against the
        ' real body. The host owns no record state; PreviewOutfitInHostAsync writes the override + renders.
        If _host Is Nothing Then
            _host = New NpcRenderHost(_preview) With {
                .AppliedPresets = _appliedPresets,
                .Toggles = _mainForm.BuildOutfitPickerToggles()
            }
        End If

        ' Form opens on Browse → the piece/outfit radios start disabled (Create-only).
        PreviewModeRow.Enabled = (TabsMain.SelectedTab Is TabPageCreate)

        ' First WYSIWYG render of the pre-selected row.
        OnListSelectionChanged(Me, EventArgs.Empty)
    End Sub

    Private Sub OutfitPicker_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Drop the throwaway Create-tab preview draft so it never leaks into Browse / the save set.
        If _previewDraftRegistered Then
            Try
                _mainForm.UnregisterOutfitDraft(OutfitDraft.PreviewDraftFormID)
            Catch
            End Try
            _previewDraftRegistered = False
        End If

        ' Quiesce the render loop before tearing down GL state — same ordering rationale as EditBody:
        ' control teardown → host Dispose → control Clean/Dispose (host must release its GL compositor
        ' handles while the context is still current, before the control is destroyed).
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.BeginTeardown()
            Catch
            End Try
        End If
        If _host IsNot Nothing Then
            Try
                _host.Dispose()
            Catch
            End Try
            _host = Nothing
        End If
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.Clean()
            Catch
            End Try
            Try
                _preview.Dispose()
            Catch
            End Try
        End If
    End Sub

End Class
