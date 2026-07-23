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
    ''' <summary>FormID → candidate index built once whenever <see cref="_itemCandidates"/> is assigned,
    ''' so per-item lookups (AddItemFidAsPiece / PrefillPiecesFromOutfit / Add-to-lvl name) are O(1)
    ''' instead of an O(n) FirstOrDefault per item. First occurrence wins, mirroring FirstOrDefault.</summary>
    Private _itemCandidatesByFid As Dictionary(Of UInteger, (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
    Private _filteredItems As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))

    ''' <summary>Assign <see cref="_itemCandidates"/> and rebuild the FormID index in lockstep so the two
    ''' never drift. Call this instead of assigning the field directly.</summary>
    Private Sub SetItemCandidates(items As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String)))
        _itemCandidates = items
        _itemCandidatesByFid = New Dictionary(Of UInteger, (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(If(items Is Nothing, 0, items.Count))
        If items IsNot Nothing Then
            For Each it In items
                If Not _itemCandidatesByFid.ContainsKey(it.FormID) Then _itemCandidatesByFid(it.FormID) = it
            Next
        End If
    End Sub
    ''' <summary>Pieces the user added to the outfit under construction (working set). Order = add
    ''' sequence; the slot-conflict resolver uses it for "last-added wins".</summary>
    Private ReadOnly _pieces As New List(Of PieceEntry)
    Private _pieceOrderCounter As Integer = 0

    ''' <summary>Leveled-list DRILL-DOWN navigation stack (in-situ recursive editing). EMPTY = TOP level: the bottom
    ''' list shows the outfit's own pieces (<see cref="_pieces"/>) with slot-conflict resolution + preview, exactly
    ''' as before. NON-EMPTY = drilled into a chain of LVLI drafts; the last FormID is the list currently being
    ''' edited, and the bottom list shows THAT list's LVLO entries (<see cref="_levelView"/>) instead. Double-click
    ''' a leveled row pushes; the "▲ Back" button (or a leveled parent row) pops. Every FormID here is an OWN
    ''' <see cref="LeveledListDraft"/> (a vanilla LVLI is auto-promoted to an OVERRIDE draft on first drill so it
    ''' becomes editable). The outfit's <see cref="_pieces"/> are NEVER mutated while nested — commit/preview/save
    ''' always read <see cref="_pieces"/>, so drilling can't corrupt the outfit being authored.</summary>
    Private ReadOnly _lvlNavStack As New List(Of UInteger)
    ''' <summary>The current nested level's rows (rebuilt on each drill/refresh) — one <see cref="PieceEntry"/> per
    ''' LVLO entry of <see cref="CurrentLevelDraft"/>, each carrying its <see cref="PieceEntry.SourceEntry"/>. Only
    ''' populated while <see cref="_lvlNavStack"/> is non-empty.</summary>
    Private ReadOnly _levelView As New List(Of PieceEntry)
    ''' <summary>Set when a nested LVLI edit (add/edit/remove entry) changed a list — so the next TOP-level render
    ''' re-samples the leveled pieces' realizations to reflect it. Cleared after that one re-sample.</summary>
    Private _lvlDirtyResample As Boolean = False
    ''' <summary>Which of the two Create lists the user last worked in — drives the "selected piece"
    ''' preview so it follows the FOCUSED list (False = top candidate-items list, True = bottom
    ''' chosen-pieces list). Set on each list's Enter. Defaults to the top list (the first populated).</summary>
    Private _pieceListHasPreviewFocus As Boolean = False
    ''' <summary>Tooltip host for the piece-reorder (▲/▼) buttons — explains that piece Order is the equip
    ''' sequence (last-equipped wins slot conflicts), which the two glyph buttons aren't self-evident about.</summary>
    Private ReadOnly _pieceReorderTip As New ToolTip()
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
        ''' <summary>NESTED-LEVEL rows only (the leveled-list drill-down): the backing LVLO entry inside the LVLI
        ''' draft currently open, so Edit/Remove at that level mutate the real draft. Nothing for TOP-LEVEL outfit
        ''' pieces (which are the outfit's own items, not entries of a parent list). Its Level/Count/ChanceNone are
        ''' shown in the row and edited in place.</summary>
        Public SourceEntry As LeveledListDraft.LeveledEntry
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
        RefreshOutfitEdidField(True, OutfitDraft.EditorIdPrefix)   ' opens in "New outfit" mode (empty name)
        SetItemCandidates(_mainForm.GetArmoItemCandidatesWithDrafts(_raceFormID, _isFemale))
        _filteredItems = New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(_itemCandidates)
        RefreshItemList()
        RefreshPieces()
        AddHandler TextBoxItemFilter.TextChanged, AddressOf OnItemFilterChanged
        AddHandler ListViewItems.DoubleClick, AddressOf OnEditItemInArmorEditor
        AddHandler ButtonAddItem.Click, AddressOf OnAddItem
        AddHandler ButtonRemovePiece.Click, AddressOf OnRemovePiece
        ' Reorder the selected piece in equip sequence (Order). INAM ascending = equip order; last-equipped
        ' wins slot conflicts, so ▼ (later) promotes a piece over an overlapping one. Top-level only.
        AddHandler ButtonMovePieceUp.Click, AddressOf OnMovePieceUp
        AddHandler ButtonMovePieceDown.Click, AddressOf OnMovePieceDown
        _pieceReorderTip.SetToolTip(ButtonMovePieceUp, "Move piece earlier in equip order")
        _pieceReorderTip.SetToolTip(ButtonMovePieceDown, "Move piece later (equipped last → wins slot conflicts)")
        AddHandler ButtonReroll.Click, AddressOf OnReroll
        AddHandler ButtonNewLvl.Click, AddressOf OnNewLvl
        AddHandler ButtonAddToLvl.Click, AddressOf OnAddToLvl
        ' Explicit New-record vs Override intent for the OUTFIT (xEdit model), replacing the old New/Override radio.
        AddHandler ButtonNewOutfit.Click, AddressOf OnActionNewOutfit
        AddHandler ButtonOverrideOutfit.Click, AddressOf OnActionOverrideOutfit
        AddHandler TextBoxEdid.TextChanged, AddressOf OnCreateEdidChanged
        AddHandler TabsMain.SelectedIndexChanged, AddressOf OnTabChanged
        ' Preview-mode toggle (whole outfit vs selected piece) + selection-driven piece preview.
        AddHandler RadioButtonRenderPiece.CheckedChanged, AddressOf OnCreatePreviewModeChanged
        AddHandler ListViewItems.SelectedIndexChanged, AddressOf OnCreateItemSelectionChanged
        AddHandler ListViewPieces.SelectedIndexChanged, AddressOf OnCreatePieceSelectionChanged
        ' Double-click an ARMO (candidate OR selected piece) → open the ARMO editor with it as template (LVLs excluded).
        AddHandler ListViewPieces.DoubleClick, AddressOf OnEditPieceInArmorEditor
        ' The "selected piece" preview follows whichever Create list has focus — track the last one entered.
        AddHandler ListViewItems.Enter, AddressOf OnItemsListEnter
        AddHandler ListViewPieces.Enter, AddressOf OnPiecesListEnter
        ' "Edit armor…" edits the focused concrete ARMO (disabled when nothing concrete is focused); "New armor…"
        ' always authors a brand-new ARMO from scratch.
        AddHandler ButtonEditArmor.Click, AddressOf OnEditArmor
        AddHandler ButtonNewArmor.Click, AddressOf OnNewArmor
        ' Recursive leveled-list drill-down: "Override LVL…" picks a real LVLI to edit as an override; "▲ Back"
        ' pops one nested level. Double-click on a leveled row drills IN (wired via the existing pieces double-click).
        AddHandler ButtonOverrideLvl.Click, AddressOf OnOverrideLvl
        AddHandler ButtonBackLevel.Click, AddressOf OnBackLevel
        ' Nested-only "Edit entry…" edits the selected LVLO entry's Level/Count/ChanceNone (distinct from
        ' "Edit armor…", which stays and opens the ARMO editor when the entry references a concrete armor).
        AddHandler ButtonEditEntry.Click, Sub() EditSelectedEntry()
        ' ARMA/ARMO record authoring now works on BOTH games — the Skyrim serializers are implemented and proven
        ' byte-exact (Tools\ArmoArmaSseRoundtripProbe: ARMA 766/766, ARMO 2762/2762). "New armor…" / "Edit armor…"
        ' are enabled for Skyrim too; "Edit armor…" state is driven purely by focus (UpdateEditArmorEnabled).
        UpdateEditArmorEnabled()   ' initial state: disabled until a concrete ARMO is focused
        ' "My outfit drafts" panel: double-click loads a draft back into Create for editing; the button deletes/reverts.
        AddHandler ListViewMyOutfits.DoubleClick, AddressOf OnMyOutfitDoubleClick
        AddHandler ListViewMyOutfits.SelectedIndexChanged, Sub() UpdateDeleteOutfitEnabled()
        AddHandler ButtonDeleteOutfit.Click, AddressOf OnDeleteOrRevertOutfit

        ' Click-to-sort on every column of all three lists (same helper EditFace uses). The sorter
        ' persists across the lists' re-populations (filter/refresh), so a user-chosen sort survives.
        SortableListView.Attach(ListViewParts)
        SortableListView.Attach(ListViewItems)
        SortableListView.Attach(ListViewPieces)
        SortableListView.Attach(ListViewMyOutfits)

        ' First open: default to OVERRIDE of the NPC's current outfit (your plugin replaces it) — the usual
        ' intent when editing what the NPC already wears. Pre-fill its pieces and keep its EDID (read-only). The
        ' user can switch to "New outfit" explicitly. With no current outfit → stay in the New mode set above.
        If _currentEffectiveOutfitFID <> 0UI Then
            _overrideTargetFormID = _currentEffectiveOutfitFID
            _overrideTargetEditorID = _mainForm.GetOutfitDisplayName(_currentEffectiveOutfitFID)
            RefreshOutfitEdidField(False, _overrideTargetEditorID)   ' kept EDID, read-only
            PrefillPiecesFromOutfit(_currentEffectiveOutfitFID)
        End If
        UpdateCreateBanner()
        RefreshMyOutfitDrafts()
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
            ' On success CommitCreate closes; on a validation veto it doesn't — keep the My-outfits list current
            ' (it also reflects a freshly committed draft if the dialog is kept open by a later flow).
            RefreshMyOutfitDrafts()
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

    ''' <summary>Double-click a CANDIDATE ARMO → open the ARMO editor to OVERRIDE it (edit that record; your plugin
    ''' replaces it). LVLI candidates are excluded. Adding to the outfit stays on the Add button; to make a NEW
    ''' record from a copy, use the editor's "New from template…" once open.</summary>
    Private Sub OnEditItemInArmorEditor(sender As Object, e As EventArgs)
        If ListViewItems.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListViewItems.SelectedItems(0).Tag)
        If _mainForm.IsLeveledItem(fid) Then Return   ' exclude LVLs
        OpenArmorEditorForTemplate(fid, asOverride:=True)
    End Sub

    ''' <summary>Double-click a SELECTED PIECE → open the ARMO editor to OVERRIDE it. LVLI pieces excluded.</summary>
    Private Sub OnEditPieceInArmorEditor(sender As Object, e As EventArgs)
        Dim p = SelectedPieceEntry()
        If p Is Nothing Then Return
        ' A LEVELED row (top piece OR nested entry) → DRILL IN (recursive): its own draft, or a vanilla LVLI
        ' auto-promoted to an override draft, becomes the current level.
        If p.IsLeveled Then
            DrillIntoLeveled(p.FormID)
            Return
        End If
        ' A CONCRETE (ARMO) row → open the ARMO editor as override, at BOTH levels (double-click a nested armor
        ' entry edits the armor itself; its Level/Count/Chance are edited via the separate "Edit entry…" button).
        OpenArmorEditorForTemplate(p.FormID, asOverride:=True)
    End Sub

    ' ============================ Leveled-list recursive drill-down (in-situ) ============================

    ''' <summary>True at the outfit ROOT (nav stack empty): the bottom list shows the outfit's pieces. False while
    ''' drilled into a chain of leveled lists.</summary>
    Private Function IsAtTopLevel() As Boolean
        Return _lvlNavStack.Count = 0
    End Function

    ''' <summary>The LVLI draft currently drilled into (top of the nav stack), or Nothing at the root / if the draft
    ''' was removed. Every stack FormID is an own draft (a vanilla list is promoted to an override on first drill).</summary>
    Private Function CurrentLevelDraft() As LeveledListDraft
        If _lvlNavStack.Count = 0 Then Return Nothing
        Return _mainForm.TryGetLeveledListDraft(_lvlNavStack(_lvlNavStack.Count - 1))
    End Function

    ''' <summary>Breadcrumb for <see cref="LabelPieces"/>: "Outfit ▸ LVL_A ▸ LVL_B:" over the nav chain.</summary>
    Private Function BuildBreadcrumb() As String
        Dim sb As New System.Text.StringBuilder("Outfit")
        For Each fid In _lvlNavStack
            Dim d = _mainForm.TryGetLeveledListDraft(fid)
            sb.Append("  ▸  ").Append(If(d IsNot Nothing, StripLvlPrefix(d.EditorID), fid.ToString("X8")))
        Next
        sb.Append(":")
        Return sb.ToString()
    End Function

    ''' <summary>Drop the "npcm_LVLI_" type prefix for a compact breadcrumb/label name.</summary>
    Private Shared Function StripLvlPrefix(edid As String) As String
        If String.IsNullOrEmpty(edid) Then Return ""
        If edid.StartsWith(LeveledListDraft.EditorIdPrefix, StringComparison.Ordinal) Then Return edid.Substring(LeveledListDraft.EditorIdPrefix.Length)
        Return edid
    End Function

    ''' <summary>Show TOP (outfit) vs NESTED (leveled-entry) button chrome. Nested REUSES Remove/Edit/Add-to-lvl for
    ''' entry ops (relabeled) and shows "▲ Back"; the outfit-authoring buttons are hidden. Called by both renders.</summary>
    Private Sub UpdateLevelChrome()
        Dim nested = Not IsAtTopLevel()
        ButtonBackLevel.Visible = nested
        ButtonEditEntry.Visible = nested        ' nested-only: edits the LVLO entry's Level/Count/Chance
        ButtonOverrideLvl.Visible = Not nested
        ButtonNewLvl.Visible = Not nested
        ButtonNewArmor.Visible = Not nested
        ButtonAddItem.Visible = Not nested
        ButtonReroll.Visible = Not nested
        ' Piece reorder is a top-level (outfit) action — hidden while drilled into a leveled list, where the
        ' rows are LVLO entries (their order isn't the equip sequence). Enable state set by UpdateMovePieceEnabled.
        ButtonMovePieceUp.Visible = Not nested
        ButtonMovePieceDown.Visible = Not nested
        ButtonRemovePiece.Text = If(nested, "Remove entry", "Remove piece")
        ButtonAddToLvl.Text = If(nested, "Add item ▼", "Add to lvl ▼")
        ' ButtonEditArmor stays visible + labelled "Edit armor…" at BOTH levels: it edits the focused concrete
        ' ARMO (a top piece OR a nested armor entry). Enable state is set by UpdateEditArmorEnabled.
    End Sub

    ''' <summary>Resolve a reference (ARMO or LVLI) FormID to a display name from the draft-aware candidate universe,
    ''' falling back to a leveled draft's (prefix-stripped) EditorID or the record's editor display name.</summary>
    Private Function ResolveRefDisplay(fid As UInteger) As String
        Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
        If _itemCandidatesByFid.TryGetValue(fid, it) Then Return it.DisplayName
        Dim d = _mainForm.TryGetLeveledListDraft(fid)
        If d IsNot Nothing Then Return StripLvlPrefix(d.EditorID) & "  [LVL]"
        Return _mainForm.GetRecordDisplayNameForEditor(fid)
    End Function

    ''' <summary>Drill INTO a leveled reference: resolve it to an editable own draft (promoting a vanilla/loaded LVLI
    ''' to an OVERRIDE draft on first entry), push it on the nav stack, and render its entries. No-op with a message
    ''' when the FormID isn't an editable leveled list; guarded against re-entering a list already in the chain.</summary>
    Private Sub DrillIntoLeveled(fid As UInteger)
        If fid = 0UI Then Return
        Dim d = _mainForm.TryGetLeveledListDraft(fid)
        If d Is Nothing Then d = _mainForm.BuildLeveledOverrideDraftFromReal(fid)
        If d Is Nothing Then
            MessageBox.Show(Me, "That item isn't an editable leveled list.", "Open leveled list",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If _lvlNavStack.Contains(d.FormID) Then Return   ' already open in this chain — avoid a navigation cycle
        _lvlNavStack.Add(d.FormID)
        RefreshItemCandidates()   ' a just-promoted override draft now appears as an addable candidate
        RefreshPieces()
    End Sub

    ''' <summary>"▲ Back": pop one nested level (RefreshPieces restores the outfit view at the root).</summary>
    Private Sub OnBackLevel(sender As Object, e As EventArgs)
        If _lvlNavStack.Count > 0 Then _lvlNavStack.RemoveAt(_lvlNavStack.Count - 1)
        RefreshPieces()
    End Sub

    ''' <summary>Render the current nested LVLI draft's LVLO entries into the pieces list (one row per entry, with
    ''' the resolved ref name + 🎲 for a leveled ref, sampled slots, and a "Lvl N · ×C · cn X" column). Slot-conflict
    ''' resolution + outfit preview are TOP-LEVEL only, so they're skipped here. Updates breadcrumb + chrome.</summary>
    Private Sub RenderLeveledLevel()
        Dim d = CurrentLevelDraft()
        If d Is Nothing Then   ' the draft vanished (reverted elsewhere) — bail to the outfit root
            _lvlNavStack.Clear()
            RefreshPieces()
            Return
        End If
        Dim keep As UInteger = If(SelectedPieceEntry()?.FormID, 0UI)
        _levelView.Clear()
        Dim ord As Integer = 0
        For Each en In d.Entries
            ord += 1
            Dim isLvl = _mainForm.IsLeveledItem(en.RefFormID)
            ' Robust slot footprint for ANY reference (in or out of the candidate universe) so the column never
            ' spuriously shows "(none)" for an item that has slots. LVLI → union of terminals; ARMO → effective mask.
            Dim slot = _mainForm.GetReferenceSlotMask(en.RefFormID, _raceFormID, _isFemale)
            _levelView.Add(New PieceEntry With {
                .FormID = en.RefFormID, .Display = ResolveRefDisplay(en.RefFormID), .SlotMask = slot,
                .Order = ord, .IsLeveled = isLvl, .SourceEntry = en})
        Next
        ListViewPieces.BeginUpdate()
        Try
            ListViewPieces.Items.Clear()
            For Each p In _levelView
                Dim en = p.SourceEntry
                Dim row As New ListViewItem(If(p.IsLeveled, "🎲 " & p.Display, p.Display))
                row.SubItems.Add(DescribeSlotMask(p.SlotMask))
                row.SubItems.Add($"Lvl {en.Level} · ×{en.Count}" & If(en.ChanceNone > 0, $" · cn {en.ChanceNone}", ""))
                row.SubItems.Add("")
                row.Tag = p
                If keep <> 0UI AndAlso p.FormID = keep Then row.Selected = True
                ListViewPieces.Items.Add(row)
            Next
        Finally
            ListViewPieces.EndUpdate()
        End Try
        LabelPieces.Text = BuildBreadcrumb()
        LabelCreateStatus.Text = $"{d.Entries.Count} entry(ies) in '{StripLvlPrefix(d.EditorID)}'" & If(d.IsOverride, "  ·  override", "  ·  new")
        ButtonReroll.Enabled = False
        UpdateLevelChrome()
        UpdateAddToLvlEnabled()
        UpdateEditArmorEnabled()
    End Sub

    ''' <summary>Nested "Add item": put the top candidate list's selected item into the CURRENT leveled draft
    ''' (Level/Count/ChanceNone dialog), guarding self-insert + cycles. Mirror of the top-level "Add to lvl".</summary>
    Private Sub AddEntryToCurrentLevel()
        Dim d = CurrentLevelDraft()
        If d Is Nothing Then Return
        Dim itemFid = SelectedItemFormID()
        If itemFid = 0UI Then
            MessageBox.Show(Me, "Select an item in the top list to add into this leveled list.", "Add item",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If itemFid = d.FormID OrElse _mainForm.WouldCreateLeveledCycle(d.FormID, itemFid) Then
            MessageBox.Show(Me, "That would create a leveled-list cycle (the list would end up containing itself).",
                            "Add item", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        Using dlg As New LeveledEntryDialog_Form($"Add '{ResolveRefDisplay(itemFid)}'  →  '{StripLvlPrefix(d.EditorID)}'")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            d.Entries.Add(New LeveledListDraft.LeveledEntry With {
                .RefFormID = itemFid, .Level = dlg.LevelValue, .Count = dlg.CountValue, .ChanceNone = dlg.ChanceNoneValue})
            d.IsModified = True
        End Using
        _lvlDirtyResample = True
        RefreshPieces()
    End Sub

    ''' <summary>Edit the selected nested entry's Level/Count/ChanceNone in place (the LVLO fields).</summary>
    Private Sub EditSelectedEntry()
        Dim d = CurrentLevelDraft()
        If d Is Nothing Then Return
        Dim p = SelectedPieceEntry()
        If p Is Nothing OrElse p.SourceEntry Is Nothing Then Return
        Dim en = p.SourceEntry
        Using dlg As New LeveledEntryDialog_Form($"Edit '{ResolveRefDisplay(en.RefFormID)}'", en.Level, en.Count, en.ChanceNone)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            en.Level = dlg.LevelValue
            en.Count = dlg.CountValue
            en.ChanceNone = dlg.ChanceNoneValue
            d.IsModified = True
        End Using
        _lvlDirtyResample = True
        RefreshPieces()
    End Sub

    ''' <summary>Remove the selected nested entry from the current leveled draft.</summary>
    Private Sub RemoveSelectedEntry()
        Dim d = CurrentLevelDraft()
        If d Is Nothing Then Return
        Dim p = SelectedPieceEntry()
        If p Is Nothing OrElse p.SourceEntry Is Nothing Then Return
        d.Entries.Remove(p.SourceEntry)
        d.IsModified = True
        _lvlDirtyResample = True
        RefreshPieces()
    End Sub

    ''' <summary>"Override LVL…" (top level): pick a real LVLI, promote it to an editable OVERRIDE draft, add it as an
    ''' outfit piece, then drill straight in so the user edits its entries. The saver writes it as an override
    ''' (Phase 2d), replacing the vanilla/preserved record and keeping its non-owned subrecords.</summary>
    Private Sub OnOverrideLvl(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, New String() {"LVLI"},
                                           "Override a leveled list", 0UI, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            Dim d = _mainForm.BuildLeveledOverrideDraftFromReal(dlg.SelectedFormID)
            If d Is Nothing Then
                MessageBox.Show(Me, "Could not open that leveled list.", "Override LVL",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
            RefreshItemCandidates()
            AddItemFidAsPiece(d.FormID)
            DrillIntoLeveled(d.FormID)
        End Using
    End Sub

    ''' <summary>Open the ARMO editor with <paramref name="armoFid"/> pre-loaded as its template in the requested
    ''' mode (<paramref name="asOverride"/> → xEdit "copy as override" vs "copy as new record"), then FULLY
    ''' refresh the Create tab ON RETURN (regardless of OK/Cancel): an edit may have added/changed a draft, and
    ''' the editor's own GL preview host teardown leaves ours needing a re-render. RefreshItemCandidates re-fetches
    ''' the candidate universe + rebuilds the item list; ResyncPiecesFromCandidates pulls any overridden ARMO's
    ''' updated slots/name into its piece; clearing _lastPreviewKey forces a re-render (an override can change the
    ''' render without changing the piece FormIDs); RefreshPieces rebuilds the pieces list + re-renders.</summary>
    Private Sub OpenArmorEditorForTemplate(armoFid As UInteger, asOverride As Boolean)
        Dim outfitCtx = RegisterOutfitContextDraft()
        Try
            Using dlg As New ArmoEditor_Form(_mainForm, _npcFormID, _raceFormID, _isFemale,
                                             initialTemplateArmoFormID:=armoFid, templateAsOverride:=asOverride,
                                             outfitContextFormID:=outfitCtx)
                dlg.ShowDialog(Me)
            End Using
        Finally
            UnregisterOutfitContextDraft()
        End Try
        RefreshCreateAfterArmorEdit()
    End Sub

    ''' <summary>Stable throwaway OTFT sentinel for the "outfit context" threaded into the ARMO/ARMA editors'
    ''' "Full Outfit" preview mode. DISTINCT from <see cref="OutfitDraft.PreviewDraftFormID"/> (which those
    ''' editors overwrite for their OWN single-item preview) and from the ARMA editor's ARMO wrapper sentinel.</summary>
    Private Const OutfitContextFormID As UInteger = &HFF0007FDUI
    Private _outfitContextRegistered As Boolean

    ''' <summary>Register a STABLE OTFT holding the Create tab's currently-assembled winners (slot-conflict
    ''' resolved + flattened, same set the Create preview draws), under a dedicated sentinel FormID so it
    ''' survives the ARMO/ARMA editor modals (which register their own preview draft at the shared sentinel).
    ''' Threaded to those editors as the "Full Outfit" preview context. Returns 0 when the Create tab has no
    ''' pieces (⇒ the editors fall back to their single-item throwaway).</summary>
    ''' <summary>BOD2 CRUDO del ARMO de la pieza — la máscara con la que el engine decide el conflicto de
    ''' EQUIP en Skyrim (0x1403BD39E castea el ítem con AsBipedObjectForm y lo compara contra lo ya equipado
    ''' con SlotsOverlap 0x1401CCA90, any-bit). <see cref="PieceEntry.SlotMask"/> NO sirve: es la unión de los
    ''' BOD2 de los ARMA más los bits headwear del ARMO, y esos bits extra (34 Forearms, 38 Calves, 41, 43…)
    ''' gobiernan particiones, no el equip — usarlos hacía chocar las botas [37,38] con la túnica [32,34,38]
    ''' y las botas desaparecían. Piezas LVLI: se queda la máscara que ya traen (sus ARMO terminales se
    ''' muestrean aparte). Cacheado por FormID. El resolver lo ignora en FO4.</summary>
    Private ReadOnly _armoConflictMaskCache As New Dictionary(Of UInteger, UInteger)
    Private Function ArmoConflictMask(p As PieceEntry) As UInteger
        If p Is Nothing Then Return 0UI
        If p.IsLeveled Then Return p.SlotMask
        Dim cached As UInteger
        If _armoConflictMaskCache.TryGetValue(p.FormID, cached) Then Return cached
        Dim mask As UInteger = p.SlotMask
        Dim pm = _mainForm.PluginManagerForEditor
        If pm IsNot Nothing Then
            Dim rec = pm.GetRecord(p.FormID)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "ARMO" Then
                Dim armo = RecordParsers.ParseARMO(rec, pm)
                If armo IsNot Nothing AndAlso armo.SlotMask <> 0UI Then mask = armo.SlotMask
            End If
        End If
        _armoConflictMaskCache(p.FormID) = mask
        Return mask
    End Function

    Private Function RegisterOutfitContextDraft() As UInteger
        Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order, AddressOf ArmoConflictMask)
        Dim winners = FlattenPieces(res.Winners)
        If winners.Count = 0 Then Return 0UI
        Dim d As New OutfitDraft With {.FormID = OutfitContextFormID,
                                       .EditorID = OutfitDraft.EditorIdPrefix & "(outfitcontext)"}
        d.ItemFormIDs.AddRange(winners)
        _mainForm.RegisterOutfitDraft(d)
        _outfitContextRegistered = True
        Return OutfitContextFormID
    End Function

    ''' <summary>Drop the outfit-context OTFT registered by <see cref="RegisterOutfitContextDraft"/> (after the
    ''' editor modal returns) so it never leaks into Browse / the save set.</summary>
    Private Sub UnregisterOutfitContextDraft()
        If Not _outfitContextRegistered Then Return
        Try
            _mainForm.UnregisterOutfitDraft(OutfitContextFormID)
        Catch
        End Try
        _outfitContextRegistered = False
    End Sub

    ''' <summary>Post-return refresh shared by <see cref="OpenArmorEditorForTemplate"/> (template edit) and
    ''' <see cref="OnEditArmor"/> (new-ARMO authoring): re-fetch the candidate universe + rebuild the item list,
    ''' pull any overridden ARMO's updated slots/name into its piece, force a re-render (an override can change
    ''' the render without changing piece FormIDs), and rebuild the pieces list + re-render.</summary>
    Private Sub RefreshCreateAfterArmorEdit()
        RefreshItemCandidates()
        ResyncPiecesFromCandidates()
        _lastPreviewKey = Nothing
        RefreshPieces()
    End Sub

    ''' <summary>The FOCUSED concrete (non-leveled) ARMO FormID: the selected piece when the pieces list has
    ''' preview focus, else the selected candidate item. 0 when nothing concrete is focused (empty selection or a
    ''' leveled list). Drives both <see cref="OnEditArmor"/> and <see cref="UpdateEditArmorEnabled"/>.</summary>
    Private Function FocusedConcreteArmoFid() As UInteger
        Dim fid As UInteger = If(_pieceListHasPreviewFocus, If(SelectedPieceEntry()?.FormID, 0UI), SelectedItemFormID())
        If fid = 0UI OrElse _mainForm.IsLeveledItem(fid) Then Return 0UI
        Return fid
    End Function

    ''' <summary>"Edit armor…" — open the ARMO editor (as an override) on the FOCUSED concrete ARMO. Disabled by
    ''' <see cref="UpdateEditArmorEnabled"/> when nothing concrete is focused, so this is a no-op then. Authoring a
    ''' brand-new ARMO is the separate "New armor…" button.</summary>
    Private Sub OnEditArmor(sender As Object, e As EventArgs)
        ' Works at BOTH levels: FocusedConcreteArmoFid resolves the selected concrete ARMO — a top-level piece OR
        ' a nested LVLO entry whose reference is an armor — so "Edit armor…" opens the ARMO editor either way.
        Dim fid = FocusedConcreteArmoFid()
        If fid = 0UI Then Return
        OpenArmorEditorForTemplate(fid, asOverride:=True)
    End Sub

    ''' <summary>"New armor…" — always open the ARMO editor in NEW (blank) mode to author a brand-new ARMO from
    ''' scratch, then run the same post-return Create refresh as an edit.</summary>
    Private Sub OnNewArmor(sender As Object, e As EventArgs)
        Dim outfitCtx = RegisterOutfitContextDraft()
        Try
            Using dlg As New ArmoEditor_Form(_mainForm, _npcFormID, _raceFormID, _isFemale,
                                             outfitContextFormID:=outfitCtx)
                dlg.ShowDialog(Me)
            End Using
        Finally
            UnregisterOutfitContextDraft()
        End Try
        RefreshCreateAfterArmorEdit()
    End Sub

    ''' <summary>"Edit armor…" is enabled only when a concrete (non-leveled) ARMO is focused in the Create lists.
    ''' Mirror of <see cref="UpdateAddToLvlEnabled"/>; called from the selection/focus handlers + after refresh.</summary>
    Private Sub UpdateEditArmorEnabled()
        ' "Edit armor…" is enabled whenever a concrete ARMO is focused — a top-level piece OR a nested armor entry.
        ' Enabled on BOTH games now that the Skyrim ARMA/ARMO serializers are implemented (proven byte-exact).
        ButtonEditArmor.Enabled = (FocusedConcreteArmoFid() <> 0UI)
        ' "Edit entry…" (nested only) is enabled whenever any LVLO entry row is selected.
        ButtonEditEntry.Enabled = (Not IsAtTopLevel() AndAlso SelectedPieceEntry() IsNot Nothing)
    End Sub

    ''' <summary>Pull each concrete (non-LVLI) piece's slots/name/plugin from the refreshed candidate universe
    ''' (an overridden ARMO's slots/name may have changed since the piece was added — the piece holds a snapshot).
    ''' Call AFTER RefreshItemCandidates (which rebuilds _itemCandidatesByFid). LVLI pieces keep their sample.</summary>
    Private Sub ResyncPiecesFromCandidates()
        For Each p In _pieces
            If p.IsLeveled Then Continue For
            Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
            If _itemCandidatesByFid.TryGetValue(p.FormID, it) Then
                p.SlotMask = it.SlotMask
                p.Display = it.DisplayName
                p.Plugin = it.Plugin
            End If
        Next
    End Sub

    ''' <summary>Add an item (ARMO or LVLI, including an own draft LVL) to the pieces list by FormID: dedup,
    ''' build the <see cref="PieceEntry"/>, sample a realization for leveled items, then refresh. Shared by
    ''' "Add to outfit" and by "New LVL…" (which auto-adds the freshly-created list as a piece).</summary>
    Private Sub AddItemFidAsPiece(fid As UInteger)
        If fid = 0UI Then Return
        If _pieces.Any(Function(p) p.FormID = fid) Then Return  ' no exact-duplicate item (ARMO or LVLI)
        Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
        If Not _itemCandidatesByFid.TryGetValue(fid, it) Then Return
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
        If Not IsAtTopLevel() Then   ' nested: "Remove entry" drops the selected LVLO entry from the current list
            RemoveSelectedEntry()
            Return
        End If
        If ListViewPieces.SelectedItems.Count = 0 Then Return
        Dim p = TryCast(ListViewPieces.SelectedItems(0).Tag, PieceEntry)
        If p Is Nothing Then Return
        _pieces.Remove(p)
        RefreshPieces()
    End Sub

    Private Sub OnMovePieceUp(sender As Object, e As EventArgs)
        MoveSelectedPiece(-1)
    End Sub

    Private Sub OnMovePieceDown(sender As Object, e As EventArgs)
        MoveSelectedPiece(1)
    End Sub

    ''' <summary>Reorder the selected top-level piece one slot up/down in equip sequence by SWAPPING its Order
    ''' with the adjacent piece (Order is the INAM/equip sequence — later = equipped last = wins slot conflicts,
    ''' so ▼ promotes a piece over an overlapping one). No-op when nested (LVLO rows aren't equip-ordered),
    ''' nothing selected, or already at the edge. RefreshPieces re-sorts by Order, repaints ✓/✗, preserves the
    ''' selection by FormID, and re-previews.</summary>
    Private Sub MoveSelectedPiece(delta As Integer)
        If Not IsAtTopLevel() Then Return
        Dim sel = SelectedPieceEntry()
        If sel Is Nothing Then Return
        Dim ordered = _pieces.OrderBy(Function(p) p.Order).ToList()
        Dim i = ordered.IndexOf(sel)
        Dim j = i + delta
        If i < 0 OrElse j < 0 OrElse j >= ordered.Count Then Return
        Dim neighbor = ordered(j)
        Dim tmp = sel.Order
        sel.Order = neighbor.Order
        neighbor.Order = tmp
        RefreshPieces()
    End Sub

    ''' <summary>Enable ▲/▼ only when a top-level piece is selected and has a neighbor in that direction
    ''' (disabled at the edges / when nested / with no selection). Mirror of UpdateEditArmorEnabled; called
    ''' from the piece selection + focus handlers and after every RefreshPieces.</summary>
    Private Sub UpdateMovePieceEnabled()
        Dim sel = If(IsAtTopLevel(), SelectedPieceEntry(), Nothing)
        If sel Is Nothing Then
            ButtonMovePieceUp.Enabled = False
            ButtonMovePieceDown.Enabled = False
            Return
        End If
        Dim ordered = _pieces.OrderBy(Function(p) p.Order).ToList()
        Dim i = ordered.IndexOf(sel)
        ButtonMovePieceUp.Enabled = (i > 0)
        ButtonMovePieceDown.Enabled = (i >= 0 AndAlso i < ordered.Count - 1)
    End Sub

    ''' <summary>Reroll: re-sample every LVLI piece's realization (new terminals + slot), then refresh the
    ''' list + preview. Only meaningful when a leveled piece is present (the button is enabled accordingly).</summary>
    Private Sub OnReroll(sender As Object, e As EventArgs)
        If Not IsAtTopLevel() Then Return   ' Reroll is a top-level (outfit) action; hidden while nested
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
        SetItemCandidates(_mainForm.GetArmoItemCandidatesWithDrafts(_raceFormID, _isFemale))
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
        If Not IsAtTopLevel() Then   ' nested: "Add item" puts the top-selected candidate into the CURRENT list
            AddEntryToCurrentLevel()
            Return
        End If
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
        Dim itemCand As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
        Dim itemName As String = If(_itemCandidatesByFid.TryGetValue(itemFid, itemCand), itemCand.DisplayName, Nothing)
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
        If Not IsAtTopLevel() Then
            ButtonAddToLvl.Enabled = True   ' nested: always addable into the current list (validated on click)
            Return
        End If
        Dim p = SelectedPieceEntry()
        ButtonAddToLvl.Enabled = (p IsNot Nothing AndAlso _mainForm.IsOwnLeveledDraft(p.FormID))
    End Sub

    ''' <summary>Run the shared slot-conflict resolver over the assembled pieces, repaint the list
    ''' (✓ winners / ✗ eliminated, losers greyed), preview the resolved (winner) set, and update the
    ''' status line. Losers stay visible so the user sees what got eliminated and can remove a winner
    ''' to promote a loser; only winners are saved into the outfit (the resolved, conflict-free set).</summary>
    Private Async Sub RefreshPieces()
        ' Drilled into a leveled list → render THAT list's entries instead of the outfit pieces (the outfit's own
        ' _pieces are untouched; slot-conflict + preview are top-level only). Early-return keeps the top-level path
        ' below byte-identical when at the outfit root.
        If Not IsAtTopLevel() Then
            RenderLeveledLevel()
            Return
        End If
        ' Returning to the outfit root after editing a nested list: re-sample every leveled piece's realization once
        ' so the top view reflects the edited list contents (added/removed/edited entries). Cleared after one pass.
        If _lvlDirtyResample Then
            _lvlDirtyResample = False
            For Each p In _pieces
                If p.IsLeveled Then
                    Dim rr = _mainForm.SampleLeveledRealization(p.FormID, _raceFormID, _isFemale)
                    p.Realization = rr.Terminals
                    p.SlotMask = rr.SlotMask
                End If
            Next
            _lastPreviewKey = Nothing
        End If
        LabelPieces.Text = "Outfit pieces:"
        UpdateLevelChrome()
        ' Remember the selected piece so a rebuild (add item, add-to-lvl, reroll) doesn't drop the
        ' selection — the user can keep acting on the same piece (e.g. add several items into the same
        ' leveled list) without re-selecting it every time. No-op if that piece is gone (e.g. Remove).
        Dim keepSelectedFid As UInteger = If(SelectedPieceEntry()?.FormID, 0UI)

        Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order, AddressOf ArmoConflictMask)
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
        UpdateEditArmorEnabled()  ' reflects the (preserved) selection
        UpdateMovePieceEnabled()  ' ▲/▼ enable per selection + edges

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
            Dim res = SlotConflictResolver.ResolveSlotWinners(_pieces, Function(p) p.SlotMask, Function(p) p.Order, AddressOf ArmoConflictMask)
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
        UpdateEditArmorEnabled()   ' item focus changed → refresh Edit-armor enable (independent of preview mode)
        If TabsMain.SelectedTab IsNot TabPageCreate OrElse Not RadioButtonRenderPiece.Checked Then Return
        Await RefreshCreatePreview()
    End Sub

    Private Async Sub OnCreatePieceSelectionChanged(sender As Object, e As EventArgs)
        UpdateAddToLvlEnabled()   ' applies regardless of preview mode
        UpdateEditArmorEnabled()  ' piece focus changed → refresh Edit-armor enable
        UpdateMovePieceEnabled()  ' ▲/▼ enable follows the piece selection
        If TabsMain.SelectedTab IsNot TabPageCreate OrElse Not RadioButtonRenderPiece.Checked Then Return
        Await RefreshCreatePreview()
    End Sub

    ' Focus-gain must (re)fire the piece preview for THIS list's selection: switching lists doesn't always
    ' change a selection (the target row may already be selected), so SelectedIndexChanged wouldn't fire and
    ' the render would stay on the other list's item. Only relevant in "selected piece only" mode on Create.
    Private Async Sub OnItemsListEnter(sender As Object, e As EventArgs)
        _pieceListHasPreviewFocus = False   ' top list (candidate items) now drives the piece preview
        UpdateEditArmorEnabled()            ' focus moved to the candidate list → its selection drives Edit-armor
        If RadioButtonRenderPiece.Checked AndAlso TabsMain.SelectedTab Is TabPageCreate Then Await RefreshCreatePreview()
    End Sub

    Private Async Sub OnPiecesListEnter(sender As Object, e As EventArgs)
        _pieceListHasPreviewFocus = True    ' bottom list (chosen pieces) now drives the piece preview
        UpdateEditArmorEnabled()            ' focus moved to the pieces list → its selection drives Edit-armor
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

    ''' <summary>"New outfit" action → author a brand-new OTFT (xEdit "new record"): clear any override target,
    ''' re-enable + clear the EDID (a user-typed name, prefixed + uniqueness-checked on commit). The assembled
    ''' pieces are kept (the user builds the new outfit from whatever is in the list). Refreshes the banner.</summary>
    Private Sub OnActionNewOutfit(sender As Object, e As EventArgs)
        _overrideTargetFormID = 0UI
        _overrideTargetEditorID = ""
        RefreshOutfitEdidField(True, OutfitDraft.EditorIdPrefix)   ' editable name, empty
        UpdateCreateBanner()
    End Sub

    ''' <summary>Drive the shared EditorID field (prefix label + name box + live "Saves as:" preview) uniformly for
    ''' the Create tab: a NEW/owned record edits only the &lt;name&gt; after a fixed prefix with a live preview;
    ''' a real OVERRIDE shows its kept EditorID read-only. <paramref name="baseOrKeptEdid"/> = the base EDID whose
    ''' name seeds the box (New) or the verbatim EDID to keep (Override).</summary>
    Private Sub RefreshOutfitEdidField(isNew As Boolean, baseOrKeptEdid As String)
        If isNew Then
            EditorIdField.ConfigureNew(LabelEdidPrefix, TextBoxEdid, LabelEdidPreview, OutfitDraft.EditorIdPrefix, baseOrKeptEdid)
        Else
            EditorIdField.ConfigureOverride(LabelEdidPrefix, TextBoxEdid, LabelEdidPreview, baseOrKeptEdid)
        End If
    End Sub

    ''' <summary>"Override selected/loaded outfit…" action → edit the Browse-selected (or currently-loaded) OTFT
    ''' as an OVERRIDE (xEdit "copy as override"): keep its FormID + EditorID (EDID locked), pre-fill its pieces.
    ''' No-op with a message when there's no concrete outfit to override. Refreshes the banner.</summary>
    Private Sub OnActionOverrideOutfit(sender As Object, e As EventArgs)
        Dim target = ResolveOverrideTarget()
        If target = 0UI Then
            MessageBox.Show(Me, "Select an outfit in the Browse tab (or have one loaded) to override. Use 'New outfit' otherwise.",
                            "Edit Outfit", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _overrideTargetFormID = target
        _overrideTargetEditorID = _mainForm.GetOutfitDisplayName(target)
        RefreshOutfitEdidField(False, _overrideTargetEditorID)   ' kept EDID, read-only
        PrefillPiecesFromOutfit(target)
        UpdateCreateBanner()
    End Sub

    ''' <summary>EditorID textbox edit → keep the Create banner in sync (only meaningful in New mode; the box is
    ''' locked in Override).</summary>
    Private Sub OnCreateEdidChanged(sender As Object, e As EventArgs)
        ' Keep the live "Saves as:" preview in sync while the name is editable (New/owned draft). The box is
        ' disabled for a real override, where the preview is hidden.
        If TextBoxEdid.Enabled Then EditorIdField.UpdatePreview(LabelEdidPreview, OutfitDraft.EditorIdPrefix, TextBoxEdid.Text)
        UpdateCreateBanner()
    End Sub

    ''' <summary>Persistent Create-tab status banner: states EXACTLY the outfit target + what Save will do.
    ''' OVERRIDE (real OTFT replaced, with source plugin) / Editing draft (a draft target) / NEW outfit (a new
    ''' FormID). Called after every target/EditorID change.</summary>
    Private Sub UpdateCreateBanner()
        If LabelCreateBanner Is Nothing Then Return
        If _overrideTargetFormID <> 0UI Then
            Dim edid = If(String.IsNullOrEmpty(_overrideTargetEditorID), TextBoxEdid.Text.Trim(), _overrideTargetEditorID)
            If OutfitDraft.IsDraftFormID(_overrideTargetFormID) Then
                LabelCreateBanner.Text = $"Editing draft — {edid} (new)"
            Else
                Dim plug = If(_mainForm.GetOutfitPluginName(_overrideTargetFormID), "")
                Dim tail = If(String.IsNullOrEmpty(plug), "", $" · {plug} → your plugin replaces it")
                LabelCreateBanner.Text = $"OVERRIDE — {edid} [0x{_overrideTargetFormID:X8}]{tail}"
            End If
        Else
            Dim suffix = TextBoxEdid.Text.Trim()
            Dim edid = If(suffix.Length = 0, "(unnamed)", OutfitDraft.EditorIdPrefix & suffix)
            LabelCreateBanner.Text = $"NEW outfit — {edid} (new FormID)"
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
            Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
            If Not _itemCandidatesByFid.TryGetValue(itemFID, it) Then Continue For
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
        ElseIf TabsMain.SelectedTab Is TabPageMyOutfits Then
            ' Drafts tab just became active — repopulate the list so it reflects any commits/deletes.
            RefreshMyOutfitDrafts()
        Else
            OnListSelectionChanged(Me, EventArgs.Empty)
        End If
    End Sub

    ''' <summary>Commit the Create tab: build the OutfitDraft (winners only = resolved set), register
    ''' it on MainForm, and auto-select it as the NPC's outfit (SelectedOutfitOverride = draft FormID).
    ''' New → prefix+name EDID (uniqueness-checked) + provisional FormID. Override → keep the existing
    ''' OTFT's FormID + EditorID. Vetoes the close (DialogResult.None) on validation failure.</summary>
    Private Sub CommitCreate()
        ' Persist ALL assembled pieces, NOT just the slot-conflict winners. The "✗ eliminated" tag in the
        ' pieces list is a LIVE PREVIEW of what the render won't show right now (slot overlap, last-equipped-
        ' wins) — never a reason to drop the item from the outfit. A vanilla OTFT lists every item and the
        ' engine resolves overlaps at EQUIP time; our render (CollectMeshCandidates → SelectWinningCandidates,
        ' per-ARMO) re-resolves from the full INAM set and shows the same winner, so keeping the loser is
        ' render- AND game-faithful and non-destructive: remove the winner (or edit a slot) later and the
        ' previously-eliminated piece re-appears without having to re-add it. Safe only because the render
        ' now eliminates the whole losing ARMO (per-ARMO grouping fix) — a per-ARMA render would leave a
        ' cut-hand orphan ARMA. Order = authored piece Order (INAM ascending = equip sequence).
        Dim allPieces = _pieces.OrderBy(Function(p) p.Order).ToList()
        If allPieces.Count = 0 Then
            MessageBox.Show(Me, "Add at least one item to the outfit.", "Create Outfit",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        Dim draft As New OutfitDraft()
        If _overrideTargetFormID <> 0UI Then
            ' A draft target (provisional 0xFF FormID) is a NEW owned record being re-edited — RENAMEABLE: keep
            ' its FormID but rebuild the EDID from the editable name box. A real OTFT FormID is an OVERRIDE — keep
            ' its FormID + EditorID verbatim.
            Dim isDraftTarget = OutfitDraft.IsDraftFormID(_overrideTargetFormID)
            draft.FormID = _overrideTargetFormID
            If isDraftTarget Then
                Dim suffix = TextBoxEdid.Text.Trim()
                If suffix.Length = 0 Then
                    MessageBox.Show(Me, "Enter a name for the new outfit.", "Create Outfit",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information)
                    DialogResult = DialogResult.None
                    Return
                End If
                Dim fullEdid = EditorIdField.Compose(OutfitDraft.EditorIdPrefix, suffix)
                ' Uniqueness EXCLUDING self: keeping the same name must be allowed (the draft is still registered
                ' under this FormID, so IsOutfitEditorIdAvailable would report its own EDID as taken).
                Dim current = _mainForm.TryGetOutfitDraft(_overrideTargetFormID)
                Dim currentEdid = If(current IsNot Nothing, current.EditorID, _overrideTargetEditorID)
                If Not String.Equals(fullEdid, currentEdid, StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not _mainForm.IsOutfitEditorIdAvailable(fullEdid) Then
                    MessageBox.Show(Me, $"EditorID '{fullEdid}' is already in use. Choose another name.",
                                    "Create Outfit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    DialogResult = DialogResult.None
                    Return
                End If
                draft.EditorID = fullEdid
                draft.IsOverride = False
            Else
                draft.EditorID = _overrideTargetEditorID
                draft.IsOverride = True
            End If
        Else
            Dim suffix = TextBoxEdid.Text.Trim()
            If suffix.Length = 0 Then
                MessageBox.Show(Me, "Enter a name for the new outfit.", "Create Outfit",
                                MessageBoxButtons.OK, MessageBoxIcon.Information)
                DialogResult = DialogResult.None
                Return
            End If
            Dim fullEdid = EditorIdField.Compose(OutfitDraft.EditorIdPrefix, suffix)
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
        ' INAM = EVERY assembled piece FormID as authored — ARMO or LVLI (LVLIs persist as leveled entries).
        ' Slot conflicts are resolved at render/equip time, never dropped at save (see the header comment).
        draft.ItemFormIDs.AddRange(allPieces.Select(Function(p) p.FormID))
        ' Carry each LVLI piece's current realization to the draft so the committed render matches the
        ' picker's preview (until rerolled later). The draft still saves the LVLI ref in INAM.
        For Each p In allPieces
            If p.IsLeveled AndAlso p.Realization IsNot Nothing Then
                draft.LvliRealization(p.FormID) = New List(Of UInteger)(p.Realization)
            End If
        Next
        ' Brand-new and re-edited owned drafts are NEW owned records; a real OVERRIDE is not. An override edits an
        ' existing record → mark it modified so Save (IsDirty) emits it (matches the ARMO/ARMA editor convention).
        draft.IsNew = Not draft.IsOverride
        draft.IsModified = draft.IsOverride
        _mainForm.RegisterOutfitDraft(draft)

        SelectedOutfitOverride = draft.FormID   ' auto-select the just-created outfit as the NPC's DOFT
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' =====================================================================
    ' "My outfit drafts" panel — the user's authored outfit drafts (new + override), with
    ' load-for-edit (double-click) and delete/revert (button). Mutations go through MainForm's
    ' Register/Unregister API; the list is display-only and rebuilt by ONE refresh call.
    ' =====================================================================

    ''' <summary>Clear + repopulate the My-outfits list from MainForm's outfit drafts (new + override), excluding
    ''' the throwaway Create-tab preview draft. Row text = EditorID; Kind = Override/New; row.Tag = the draft.</summary>
    Private Sub RefreshMyOutfitDrafts()
        ListViewMyOutfits.BeginUpdate()
        Try
            ListViewMyOutfits.Items.Clear()
            Dim draftFids As New HashSet(Of UInteger)
            ' 1) Unsaved drafts (New / Override) — editable + deletable here. Tag = the OutfitDraft.
            For Each d In _mainForm.OutfitDrafts()
                If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
                draftFids.Add(d.FormID)
                Dim row As New ListViewItem(d.EditorID)
                row.SubItems.Add(If(d.IsOverride OrElse Not d.IsNew, "Override (unsaved)", "New (unsaved)"))
                row.Tag = d
                ListViewMyOutfits.Items.Add(row)
            Next
            ' 2) Already-SAVED outfits this app authored (real OTFT, npcm_ EDID), minus any a draft is overriding.
            ' Tag = a UInteger FormID (marks a real record); double-click re-opens it as an override; not deletable here.
            For Each fid In _mainForm.GetAuthoredOutfitFormIDs()
                If draftFids.Contains(fid) Then Continue For
                Dim row As New ListViewItem(_mainForm.GetOutfitDisplayName(fid))
                row.SubItems.Add("Saved")
                row.Tag = fid
                ListViewMyOutfits.Items.Add(row)
            Next
        Finally
            ListViewMyOutfits.EndUpdate()
        End Try
        UpdateDeleteOutfitEnabled()
    End Sub

    ''' <summary>The selected My-outfits row's Tag: an <see cref="OutfitDraft"/> (unsaved) or a UInteger FormID
    ''' (a saved authored OTFT). Nothing when no row is selected.</summary>
    Private Function SelectedMyOutfitTag() As Object
        If ListViewMyOutfits.SelectedItems.Count = 0 Then Return Nothing
        Return ListViewMyOutfits.SelectedItems(0).Tag
    End Function

    ''' <summary>Delete / Revert applies to any selected row — an unsaved DRAFT (delete/revert the draft) or a
    ''' SAVED record (mark it for removal on the next Save). Disabled only when nothing is selected.</summary>
    Private Sub UpdateDeleteOutfitEnabled()
        ButtonDeleteOutfit.Enabled = (SelectedMyOutfitTag() IsNot Nothing)
    End Sub

    ''' <summary>Re-open an already-saved authored outfit (real OTFT FormID) as an OVERRIDE in the Create tab —
    ''' same as Browse→Override (<see cref="OnActionOverrideOutfit"/>), targeted at the given FormID.</summary>
    Private Sub BeginOverrideOfSavedOutfit(fid As UInteger)
        If fid = 0UI Then Return
        _overrideTargetFormID = fid
        _overrideTargetEditorID = _mainForm.GetOutfitDisplayName(fid)
        RefreshOutfitEdidField(False, _overrideTargetEditorID)   ' kept EDID, read-only
        PrefillPiecesFromOutfit(fid)
        TabsMain.SelectedTab = TabPageCreate
        UpdateCreateBanner()
        RefreshPieces()
    End Sub

    ''' <summary>Double-click a My-outfits row → load that draft back into the Create tab for editing.</summary>
    Private Sub OnMyOutfitDoubleClick(sender As Object, e As EventArgs)
        Dim tag = SelectedMyOutfitTag()
        If tag Is Nothing Then Return
        Dim d = TryCast(tag, OutfitDraft)
        If d IsNot Nothing Then
            LoadOutfitDraftForEdit(d)               ' unsaved draft → keep editing it
        ElseIf TypeOf tag Is UInteger Then
            BeginOverrideOfSavedOutfit(CUInt(tag))  ' saved authored OTFT → re-open as an override
        End If
    End Sub

    ''' <summary>Load an existing outfit draft into the Create tab so it can be edited and re-committed AS THE SAME
    ''' draft. Mirrors the Browse→Override flow (<see cref="OnActionOverrideOutfit"/>): re-target CommitCreate at this
    ''' draft's FormID+EditorID (so it re-saves under the same identity — see <see cref="CommitCreate"/>), lock the
    ''' EDID box, then rebuild the working pieces from the draft's authored items the same way
    ''' <see cref="AddItemFidAsPiece"/> does (candidate index refreshed first so the per-item lookup resolves).</summary>
    Private Sub LoadOutfitDraftForEdit(d As OutfitDraft)
        If d Is Nothing Then Return
        _overrideTargetFormID = d.FormID
        _overrideTargetEditorID = d.EditorID
        ' A NEW owned draft is renameable (editable name, live preview, keeps its FormID on re-commit); a real
        ' OVERRIDE draft keeps its EDID read-only. Identity for CommitCreate is carried by _overrideTargetFormID.
        RefreshOutfitEdidField(d.IsNew, d.EditorID)
        ' Populate _itemCandidatesByFid before the per-item lookups in AddItemFidAsPiece.
        RefreshItemCandidates()
        _pieces.Clear()
        _pieceOrderCounter = 0
        For Each fid In d.ItemFormIDs
            AddItemFidAsPiece(fid)   ' builds the PieceEntry (samples LVLI realizations) exactly like "Add to outfit"
        Next
        TabsMain.SelectedTab = TabPageCreate
        UpdateCreateBanner()
        RefreshPieces()
    End Sub

    ''' <summary>Delete (a NEW draft) or Revert (an OVERRIDE draft) the selected My-outfits row. Override → confirm +
    ''' unregister (the NPC falls back to the original OTFT). New → block with a referrer list if anything still
    ''' references it, else confirm + unregister. After a successful drop, if that draft was loaded for edit, clear the
    ''' override target so a later Commit doesn't resurrect it; then refresh the list + candidates + pieces.</summary>
    Private Sub OnDeleteOrRevertOutfit(sender As Object, e As EventArgs)
        Dim tag = SelectedMyOutfitTag()
        If tag Is Nothing Then Return

        ' SAVED authored outfit (UInteger FormID) → mark for removal on the next Save (a new outfit is deleted;
        ' an override reverts to the original). Applied when the user next Saves.
        If TypeOf tag Is UInteger Then
            Dim fid = CUInt(tag)
            Dim referrers = _mainForm.GetDraftReferrers(fid)
            Dim refWarn = If(referrers.Count > 0, vbCrLf & vbCrLf & "Still referenced by:" & vbCrLf & String.Join(vbCrLf, referrers), "")
            If MessageBox.Show(Me, $"Remove saved outfit '{_mainForm.GetOutfitDisplayName(fid)}' from your plugin on the next Save?" & refWarn,
                               "Remove saved outfit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then Return
            _mainForm.MarkRecordForRemoval(fid)
            _mainForm.RevertAppOverrideInMemory(fid)   ' in-memory: restore the mod's winning OTFT (override) / drop it (new)
            If _overrideTargetFormID = fid Then
                _overrideTargetFormID = 0UI
                _overrideTargetEditorID = ""
            End If
            RefreshMyOutfitDrafts()
            RefreshItemCandidates()
            RefreshPieces()
            Return
        End If

        Dim d = TryCast(tag, OutfitDraft)
        If d Is Nothing Then Return

        If d.IsOverride OrElse Not d.IsNew Then
            If MessageBox.Show(Me, $"Revert outfit '{d.EditorID}' to the original? Your changes will be discarded.",
                               "Revert outfit", MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            _mainForm.UnregisterOutfitDraft(d.FormID)
            ' Dropping the in-memory draft is NOT enough: if this override was ALREADY SAVED into the plugin, the
            ' saver's Phase 2a re-preserves it (re-emits every target-plugin OTFT as an OVERRIDE entry unless it's in
            ' RecordsToRemove), so the reverted outfit would keep getting written. Mark it for removal so Phase 2a drops
            ' it and the original wins. No-op when no saved copy exists (removal only drops target-plugin records). The
            ' revert branch (IsOverride OrElse Not IsNew) always carries a real FormID, never a 0xFF draft sentinel.
            _mainForm.MarkRecordForRemoval(d.FormID)
            _mainForm.RevertAppOverrideInMemory(d.FormID)   ' in-memory: restore the mod's winning OTFT so it shows immediately
        Else
            Dim referrers = _mainForm.GetDraftReferrers(d.FormID)
            If referrers.Count > 0 Then
                MessageBox.Show(Me, "Can't delete — still referenced by:" & vbCrLf & String.Join(vbCrLf, referrers),
                                "Delete outfit draft", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If MessageBox.Show(Me, $"Delete outfit draft '{d.EditorID}'?", "Delete outfit draft",
                               MessageBoxButtons.YesNo, MessageBoxIcon.Question) <> DialogResult.Yes Then Return
            _mainForm.UnregisterOutfitDraft(d.FormID)
        End If

        ' Was this draft the one loaded into Create for edit? Drop the override target so a later CommitCreate
        ' doesn't re-register it under the same FormID.
        If _overrideTargetFormID = d.FormID Then
            _overrideTargetFormID = 0UI
            _overrideTargetEditorID = ""
        End If
        RefreshMyOutfitDrafts()
        RefreshItemCandidates()
        RefreshPieces()
    End Sub

    ''' <summary>Human-readable list of the biped slots a mask occupies, GAME-AWARE: names come from the
    ''' shared <see cref="BipedSlotCheckboxes.SlotName"/> table (FO4/SSE per current game). No per-game
    ''' slot-name table is duplicated here — single source of truth.</summary>
    Private Shared Function DescribeSlotMask(mask As UInteger) As String
        If mask = 0UI Then Return "(none)"
        Dim names As New List(Of String)
        For bit = 0 To 31
            If (mask And (1UI << bit)) <> 0UI Then names.Add(BipedSlotCheckboxes.SlotName(30 + bit))
        Next
        Return String.Join(", ", names)
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
            ' Camera GPU/CPU toggle debe re-aplicar el tint de ESTE preview (no sólo la geometría). Ver MainForm.
            _mainForm?.HookSkinningToggleRefresh(_preview, _host)
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

        ' Defensive: the outfit-context draft is normally dropped in the editor-open Finally; unregister here
        ' too in case the form closes while one is still registered.
        UnregisterOutfitContextDraft()

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
