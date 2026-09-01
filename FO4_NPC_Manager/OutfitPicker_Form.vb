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
    ''' <summary>El estado con el que el render dibujó a este NPC (<c>NpcRenderHost.LastRenderedState</c>).
    ''' <para>⛔ Se recibe ENTERO en vez de tres escalares sueltos porque el selector necesita preguntarle al
    ''' COLECTOR qué emite cada prenda, y el colector pide el estado. Antes el sitio de construcción lo
    ''' rompía en raza, género y atuendo y tiraba el resto, así que la única respuesta a mano era una
    ''' derivada de los armatures — un SUBCONJUNTO de lo que se dibuja.</para>
    ''' <para>Raza y género salen de acá y no por su cuenta: dos fuentes para el mismo dato es un carril
    ''' paralelo que se desincroniza.</para></summary>
    Private ReadOnly _visualState As MainForm.NPCVisualState
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
    Private _pendingDraft As OutfitDraft

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
        ''' <summary>⚠️ SÓLO lo lee la vista del drill-down (`_levelView`). Para una pieza de nivel
        ''' superior la columna de slots sale de `mutexMaskByPiece`, así que acá es informativo.</summary>
        Public SlotMask As UInteger
        Public Order As Integer
        Public Plugin As String = ""
        ''' <summary>True if <see cref="FormID"/> is an LVLI (leveled list) rather than a concrete ARMO.</summary>
        Public IsLeveled As Boolean
        ''' <summary>For an LVLI piece: the currently-sampled terminal ARMO FormIDs (the realization shown +
        ''' rendered + conflict-checked). Re-sampled by Reroll. Nothing for a plain ARMO piece. The draft
        ''' persists the LVLI FormID, not this — so the saved outfit stays leveled.</summary>
        Public Realization As List(Of OutfitArmorPick)
        ''' <summary>NESTED-LEVEL rows only (the leveled-list drill-down): the backing LVLO entry inside the LVLI
        ''' draft currently open, so Edit/Remove at that level mutate the real draft. Nothing for TOP-LEVEL outfit
        ''' pieces (which are the outfit's own items, not entries of a parent list). Its Level/Count/ChanceNone are
        ''' shown in the row and edited in place.</summary>
        Public SourceEntry As Canon.ILvli_LeveledListEntries
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
    ''' <remarks>Friend y no Public: recibe el <c>NPCVisualState</c>, que es Friend. El dialogo se
    ''' construye unicamente desde MainForm, dentro del mismo proyecto.</remarks>
    Friend Sub New(mainForm As MainForm,
                   npcFormID As UInteger,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   visualState As MainForm.NPCVisualState,
                   raceEditorID As String,
                   currentEffectiveOutfitFID As UInteger,
                   rawRecordOutfitFID As UInteger)
        InitializeComponent()
        _mainForm = mainForm
        _npcFormID = npcFormID
        _appliedPresets = appliedPresets
        ' ⛔ Sin estado no hay selector: la lista entera se construye preguntando qué emite cada prenda
        ' sobre ESTE NPC, y sin él no hay a quién preguntarle. Tiraría igual una línea más abajo al leer
        ' la raza, pero callado y sin decir qué faltó.
        If visualState Is Nothing Then Throw New ArgumentNullException(NameOf(visualState),
            "El selector de atuendos necesita el estado con el que el render dibujó al NPC: sin él no puede " &
            "preguntarle al colector qué prenda se ve y cuál pelea el slot.")
        _visualState = visualState
        _raceFormID = visualState.RaceFormID
        _isFemale = visualState.IsFemale
        _currentEffectiveOutfitFID = currentEffectiveOutfitFID
        Text = "Change Outfit"
        LabelHeader.Text = $"Outfits for race '{raceEditorID}' ({If(_isFemale, "Female", "Male")}). Choose one:"

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
        ' Explicit New-record vs Override intent for the OUTFIT.
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
    ''' being drawn even under rapid re-selection. <paramref name="draft"/> = Nothing for Browse (a
    ''' real outfit / clear / naked override).</summary>
    Private Async Function RequestPreviewAsync(overrideValue As UInteger?, key As String, pieceOnly As Boolean,
                                               Optional draft As OutfitDraft = Nothing) As Task
        _pendingHasValue = True
        _pendingOverride = overrideValue
        _pendingKey = key
        _pendingPieceOnly = pieceOnly
        _pendingDraft = draft
        If _previewInProgress Then Return   ' the in-flight loop will consume the latest pending request
        _previewInProgress = True
        Try
            While _pendingHasValue
                Dim reqOverride = _pendingOverride
                Dim reqKey = _pendingKey
                Dim reqPieceOnly = _pendingPieceOnly
                Dim reqDraft = _pendingDraft
                _pendingHasValue = False
                If reqKey = _lastPreviewKey Then Continue While   ' nothing changed since the last render
                If _host Is Nothing OrElse _preview Is Nothing OrElse _preview.IsDisposed Then Return
                ' Create previews: register the throwaway draft with THIS request's contents so the render
                ' reads exactly what we're about to mark as rendered (no stale-content race).
                If reqDraft IsNot Nothing Then
                    ' ⛔ El borrador viene ARMADO desde el pedido (`VolcarPiezasEn`), con las MISMAS piezas y
                    ' realizaciones que el confirmado. Se registra acá, con el contenido de ESTE pedido, para
                    ' que el render lea exactamente lo que estamos por marcar como dibujado.
                    _mainForm.RegisterOutfitDraft(reqDraft)
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
    Private Async Function PreviewCreateAssemblyAsync() As Task
        ' Draft registration + toggles happen inside RequestPreviewAsync (coalesced with the render) so the
        ' rendered contents match the request even under rapid re-selection. Whole outfit = full body.
        Dim d = ArmarBorradorDeAtuendo(OutfitDraft.PreviewDraftFormID, "(preview)")
        Await RequestPreviewAsync(OutfitDraft.PreviewDraftFormID, "create:" & LlaveDelBorrador(d),
                                  pieceOnly:=False, draft:=d)
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
            sb.Append("  ▸  ").Append(If(d IsNot Nothing, StripLvlPrefix(d.Record.EditorID),
                      fid.ToString("X8")))
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
        If d IsNot Nothing Then Return StripLvlPrefix(d.Record.EditorID) & "  [LVL]"
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
    ''' the resolved ref name + for a leveled ref, sampled slots, and a "Lvl N · ×C · cn X" column). Slot-conflict
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
        For Each en In d.Record.LeveledListEntries
            ord += 1
            Dim isLvl = _mainForm.IsLeveledItem(en.LeveledListEntryItem)
            ' Robust slot footprint for ANY reference (in or out of the candidate universe) so the column never
            ' spuriously shows "(none)" for an item that has slots. LVLI → union of terminals; ARMO → effective mask.
            Dim slot = _mainForm.GetReferenceSlotMask(en.LeveledListEntryItem, _raceFormID,
                                                      _isFemale)
            _levelView.Add(New PieceEntry With {
                .FormID = en.LeveledListEntryItem,
                .Display = ResolveRefDisplay(en.LeveledListEntryItem),
                .SlotMask = slot, .Order = ord, .IsLeveled = isLvl, .SourceEntry = en})
        Next
        ListViewPieces.BeginUpdate()
        Try
            ListViewPieces.Items.Clear()
            For Each p In _levelView
                Dim en = p.SourceEntry
                Dim row As New ListViewItem(If(p.IsLeveled, "🎲 " & p.Display, p.Display))
                row.SubItems.Add(DescribeSlotMask(p.SlotMask))
                Dim cn = EntryChanceNone(en)
                Dim cnSuffix = If(cn > 0, $" · cn {cn}", "")
                Dim lvlText = $"Lvl {en.LeveledListEntryLevel} · ×{en.LeveledListEntryCount}"
                row.SubItems.Add(lvlText & cnSuffix)
                row.SubItems.Add("")
                row.Tag = p
                If keep <> 0UI AndAlso p.FormID = keep Then row.Selected = True
                ListViewPieces.Items.Add(row)
            Next
        Finally
            ListViewPieces.EndUpdate()
        End Try
        LabelPieces.Text = BuildBreadcrumb()
        Dim entryCount = d.Record.LeveledListEntries.Count
        Dim overrideSuffix = If(d.IsOverride, "  ·  override", "  ·  new")
        Dim lvlName = StripLvlPrefix(d.Record.EditorID)
        LabelCreateStatus.Text = $"{entryCount} entry(ies) in '{lvlName}'" & overrideSuffix
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
        Dim addLvlName = StripLvlPrefix(d.Record.EditorID)
        Dim addTitle = $"Add '{ResolveRefDisplay(itemFid)}'  →  '{addLvlName}'"
        Using dlg As New LeveledEntryDialog_Form(addTitle)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim en = d.Record.AgregarLeveledListEntries()
            If en Is Nothing Then Return
            en.LeveledListEntryItem = itemFid
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
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
        Dim editTitle = $"Edit '{ResolveRefDisplay(en.LeveledListEntryItem)}'"
        Using dlg As New LeveledEntryDialog_Form(editTitle,
                                                  en.LeveledListEntryLevel,
                                                  en.LeveledListEntryCount, EntryChanceNone(en))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
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
        RemoveEntry(d.Record, p.SourceEntry)
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
    ''' mode (<paramref name="asOverride"/> → cargarlo como override vs. como nuevo record), then FULLY
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
    Private Const OutfitContextFormID As UInteger = Borradores.FormIdAltoDeBorrador Or &H7FDUI
    Private _outfitContextRegistered As Boolean

    ''' <summary>Las unidades de equip de las piezas armadas, para la LEY ÚNICA
    ''' (<see cref="EquipResolver"/>). La unidad del motor es UN ARMO, así que una pieza leveled aporta una
    ''' unidad POR TERMINAL de su realización — igual que el render, que compite por ARMO terminal. El
    ''' <c>Tag</c> de cada unidad es su <see cref="PieceEntry"/>, para mapear el veredicto de vuelta a la
    ''' fila. Antes esta pestaña calculaba su propia máscara de conflicto (la unión de las ARMA de la
    ''' realización), que es lo que tachaba piezas que el render sí dibuja.</summary>
    Private Function BuildEquipUnits(Optional memo As Dictionary(Of UInteger, (Dibuja As Boolean, Compite As Boolean)) = Nothing) As List(Of EquipResolver.EquipItem)
        Dim units As New List(Of EquipResolver.EquipItem)
        Dim order As Integer = 0
        For Each p In _pieces.OrderBy(Function(x) x.Order)
            For Each fid In PieceTerminals(p)
                ' ⛔ COMPITE EL QUE EL RENDER DEJA COMPETIR, y la ley es del render, no mía:
                ' `ApplyEquipSlotResolution` arma su torneo con `slottedCandidates` — los candidates que el
                ' colector EMITIÓ y que traen `SlotMask <> 0` — y agrupa por ARMO dueño. Un ARMO que no
                ' aporta ninguno no genera grupo, no genera `EquipItem` y NO COMPITE: no ocupa slot y no
                ' elimina a nadie. Los chunk-mounts de OMOD salen con `SlotMask = 0` y se dibujan por la
                ' pasada slotless, así que DIBUJAN SIN PELEAR EL SLOT.
                ' ⛔ NO se filtra por `DibujaAlgunArmature`: eso mira sólo los armatures, es un
                ' SUBCONJUNTO de lo que se dibuja, y un subconjunto sirve para AFIRMAR y nunca para
                ' DESCARTAR — usado de filtro se comía prendas que la ventana principal sí dibuja. La
                ' respuesta completa se le PREGUNTA al colector (`EmisionDe`), que es quien la tiene.
                order += 1
                ' El footprint se arma sobre el FormID CRUDO: la herencia la resuelve la vista
                ' EFECTIVA adentro de `BuildFootprint`, en un solo lugar. Antes se resolvia aca el
                ' terminal y la fila corria el torneo con identidades de TERMINAL mientras el render
                ' agrupa por identidad de HIJO: los veredictos coincidian y la identidad no.
                ' ⛔ Este filtro es SÓLO para la FILA (qué se muestra como «✗ eliminated»). El conjunto que
                ' se manda a dibujar NO sale de acá: sale de `VolcarPiezasEn`, y el torneo que vale lo corre
                ' el render. Cuando este filtro decidía las dos cosas, la prenda que no compite desaparecía
                ' del preview aunque dibujara.
                ' ⚠️ LÍMITE MEDIDO, y son DOS canales, no uno. La fila arma el footprint con
                ' `BuildFootprint(addonFormIDs:=Nothing)` —TODOS los Models— y el render sólo con el grupo
                ' INDX que resolvió OBTS y además pasado por el dedup intra-ARMO `coveredSlots`, que
                ' `BuildFootprint` NO hace (lo declara ahí mismo). Medido sobre FO4:
                '   · grupo INDX con BOD2 distinto de la unión total ....: 0 de 39     ⇒ canal MUERTO
                '   · ARMO donde `coveredSlots` pierde bits de la unión .: 10 de 1.067 ⇒ canal VIVO
                '     (Armor_Raider_Suit_02B_GlovesC 000732B9: unión 0x40200038, render 0x40000008)
                ' O sea que la fila PUEDE decir «✗ eliminated» sobre una prenda que el render dibuja, si el
                ' bit perdido cae en el `shielded`/`reservedA` de un extended-underarmor aceptado antes.
                ' Eso último NO está medido: queda declarado, no dado por imposible.
                If Not EmisionDe(fid, memo).Compite Then Continue For
                units.Add(EquipResolver.EquipItem.FromFootprint(
                    _mainForm.ArmoFootprintFor(fid, _raceFormID, _isFemale), order, p))
            Next
        Next
        Return units
    End Function

    ''' <summary>Los ARMO que una pieza pone sobre el actor: una pieza concreta es ella misma; una leveled,
    ''' los terminales de su realización actual (vacía si el sorteo no dio nada — ChanceNone).</summary>
    Private Shared Function PieceTerminals(p As PieceEntry) As IEnumerable(Of UInteger)
        If p Is Nothing Then Return Array.Empty(Of UInteger)()
        If p.IsLeveled Then Return If(p.Realization, New List(Of OutfitArmorPick)()).Select(Function(pk) pk.ArmoFormID)
        Return New UInteger() {p.FormID}
    End Function

    ''' <summary>Veredicto por pieza a partir del veredicto por unidad: cuántas de sus unidades ganaron y
    ''' cuántas cayeron. Sin unidades = la lista no sorteó nada.</summary>
    Private Shared Function PieceVerdicts(res As EquipResolver.EquipResolution) As Dictionary(Of PieceEntry, (Won As Integer, Lost As Integer))
        Dim d As New Dictionary(Of PieceEntry, (Won As Integer, Lost As Integer))
        For Each it In res.Winners
            Dim pe = TryCast(it.Tag, PieceEntry)
            If pe Is Nothing Then Continue For
            Dim cur = If(d.ContainsKey(pe), d(pe), (Won:=0, Lost:=0))
            d(pe) = (Won:=cur.Won + 1, Lost:=cur.Lost)
        Next
        For Each it In res.Losers
            Dim pe = TryCast(it.Tag, PieceEntry)
            If pe Is Nothing Then Continue For
            Dim cur = If(d.ContainsKey(pe), d(pe), (Won:=0, Lost:=0))
            d(pe) = (Won:=cur.Won, Lost:=cur.Lost + 1)
        Next
        Return d
    End Function

    ''' <summary>EL volcado de piezas a un borrador de atuendo: lo deja con EXACTAMENTE estas prendas, en
    ''' este orden, y con la realización de cada leveled. Es el ÚNICO sitio donde se escribe el contenido
    ''' de un borrador de atuendo — lo comparten la vista previa, la de una pieza, el contexto del editor
    ''' de ARMO y el commit.
    ''' <para>⛔ `Friend Shared` y PURA, y recibe TUPLAS en vez de <c>PieceEntry</c> (que es `Private` de
    ''' este formulario) POR ESO: para que el gate la pueda CORRER. Con el volcado repetido en cada
    ''' consumidor, el caso sólo puede comparar textos, y mutar `p.FormID` por `TerminalDe(p.FormID)` —que
    ''' cambia el conjunto que se manda a dibujar— lo deja verde. Misma razón por la que `PlanDeSembrado`
    ''' era `Friend Shared`.</para>
    ''' <para>⛔ `LvliRealization` se limpia primero: `ReemplazarPrendas` reemplaza la lista ENTERA, así
    ''' que dejar la realización de una prenda que ya no está es un carril paralelo que sobrevive a su
    ''' dueño.</para></summary>
    Friend Shared Sub VolcarPiezasEn(d As OutfitDraft,
                                     piezas As IEnumerable(Of (Fid As UInteger, EsLeveled As Boolean,
                                                               Picks As List(Of OutfitArmorPick))))
        If d Is Nothing Then Return
        ' ⛔ Sin ternario: `If(piezas, New List(...))` mezcla `IEnumerable(Of T)` con `List(Of T)` y el
        ' resultado no tiene tipo común. Con Option Strict apagado eso compila y revienta en ejecución.
        Dim lista As New List(Of (Fid As UInteger, EsLeveled As Boolean, Picks As List(Of OutfitArmorPick)))
        If piezas IsNot Nothing Then lista.AddRange(piezas)
        d.ReemplazarPrendas(lista.Select(Function(p) p.Fid))
        d.LvliRealization.Clear()
        For Each p In lista
            If p.EsLeveled AndAlso p.Picks IsNot Nothing Then
                d.LvliRealization(p.Fid) = New List(Of OutfitArmorPick)(p.Picks)
            End If
        Next
    End Sub

    ''' <summary>Adapta las <see cref="PieceEntry"/> del formulario a la forma que consume
    ''' <see cref="VolcarPiezasEn"/>. Único puente entre el tipo privado y la ley compartida.</summary>
    Private Shared Function ComoTuplas(piezas As IEnumerable(Of PieceEntry)) _
            As List(Of (Fid As UInteger, EsLeveled As Boolean, Picks As List(Of OutfitArmorPick)))
        Dim salida As New List(Of (Fid As UInteger, EsLeveled As Boolean, Picks As List(Of OutfitArmorPick)))
        If piezas Is Nothing Then Return salida
        For Each p In piezas
            salida.Add((p.FormID, p.IsLeveled, p.Realization))
        Next
        Return salida
    End Function

    ''' <summary>UN borrador de atuendo con las piezas del editor. Misma forma para los cuatro
    ''' consumidores — vista previa, vista previa de UNA pieza, contexto del editor de ARMO, y el commit.
    ''' <para>⛔ NO prefiltra por el torneo: `ApplyEquipSlotResolution` corre su PROPIO torneo sobre los
    ''' candidates emitidos y ADEMÁS dibuja la pasada slotless sobre `visibleCandidates` — todos los
    ''' emitidos, incluidos los de un ARMO perdedor. Prefiltrar acá es opinar distinto que el motor.</para>
    ''' <para>⛔ Y NO aplana: el aplanado existía para que la LVLI no re-sorteara en cada repintado, y eso
    ''' ya lo impide `LvliRealization` — `ResolveDraftPicks` sólo muestrea con la caché vacía, y el re-sorteo
    ''' tiene su propio botón que la borra. Aplanar además tiraba las keywords del LLKC.</para></summary>
    Private Function ArmarBorradorDeAtuendo(formID As UInteger, sufijoEdid As String) As OutfitDraft
        Dim d = OutfitDraft.Nuevo(formID, Canon.CanonBridge.SessionGame())
        d.Record.EditorID = OutfitDraft.EditorIdPrefix & sufijoEdid
        VolcarPiezasEn(d, ComoTuplas(_pieces.OrderBy(Function(x) x.Order)))
        Return d
    End Function

    ''' <summary>La llave anti-repetición del preview, DERIVADA del borrador ya armado.
    ''' <para>⛔ Sale del CONTENIDO del borrador y no de `_pieces`, para que no se pueda desincronizar de
    ''' lo que se va a dibujar. Incluye las `ContextKeywords` de cada pick: dos sorteos que caen en los
    ''' mismos terminales por ramas distintas del LLKC resuelven combinaciones OBTS distintas, y con la
    ''' llave vieja el render se salteaba y dejaba la variante anterior en pantalla.</para>
    ''' <para>Las keywords se ORDENAN: el multiset viene en el orden del recorrido del LLKC, así que sin
    ''' ordenar dos caminos equivalentes dan llaves distintas y disparan un render de más.</para>
    ''' <para>⛔ `Friend` y no `Private`, por lo mismo que `VolcarPiezasEn`: para que el gate la pueda
    ''' CORRER (`InternalsVisibleTo OutfitDraftSaveGate`).</para>
    ''' <para>Lo dibujado es función del contenido del borrador Y de la cadena de plantilla más el
    ''' registro de borradores de ARMO, que la llave no ve. La cadena es estática en la sesión, y los tres
    ''' caminos que registran un borrador de ARMO desde el selector pasan por `RefreshCreateAfterArmorEdit`,
    ''' que anula `_lastPreviewKey`. Cerrado por argumento, no por suerte.</para></summary>
    Friend Shared Function LlaveDelBorrador(d As OutfitDraft) As String
        If d Is Nothing Then Return ""
        Dim partes As New List(Of String)
        For Each fid In d.Prendas()
            Dim picks As List(Of OutfitArmorPick) = Nothing
            If Not d.LvliRealization.TryGetValue(fid, picks) Then picks = Nothing
            Dim tok = fid.ToString("X")
            If picks IsNot Nothing Then
                tok &= "=" & String.Join(",", picks.Select(Function(pk) pk.ArmoFormID.ToString("X") & "+" &
                            String.Join("|", pk.ContextKeywords.OrderBy(Function(k) k).Select(Function(k) k.ToString("X")))))
            End If
            partes.Add(tok)
        Next
        Return String.Join(";", partes)
    End Function
    Private Function RegisterOutfitContextDraft() As UInteger
        ' ⛔ La guarda EQUIVALENTE a la vieja («había ≥1 ganador»), no `_pieces.Count = 0`: con ésa, un
        ' atuendo cuyas piezas no dibujan nada pasaba de ctx=0 a ctx≠0 y los editores de ARMA/ARMO caían de
        ' «Full Outfit con contexto» a un atuendo que no dibuja. Y NO se pregunta `EmisionDe(p.FormID)` sobre
        ' una LVLI: eso recursa por TODOS sus terminales posibles, no por los sorteados.
        Dim memoCtx As New Dictionary(Of UInteger, (Dibuja As Boolean, Compite As Boolean))
        If Not _pieces.Any(Function(p) PieceTerminals(p).Any(Function(f) EmisionDe(f, memoCtx).Dibuja)) Then Return 0UI
        Dim d = ArmarBorradorDeAtuendo(OutfitContextFormID, "(outfitcontext)")
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
            Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
            If _itemCandidatesByFid.TryGetValue(p.FormID, it) Then
                p.Display = it.DisplayName
                p.Plugin = it.Plugin
                ' La máscara de una lista por nivel es la de SU realización, no la unión de todos los
                ' terminales del candidato: pisarla con la unión desalinea la fila de lo que se sorteó.
                If Not p.IsLeveled Then p.SlotMask = it.SlotMask
            End If
        Next
    End Sub

    ''' <summary>El PLAN de sembrado del editor: qué prendas del INAM entran a la grilla, en orden, y
    ''' cuáles entran MARCADAS por no aplicar a esta raza/género.
    ''' <para>⛔ Acá vive la decisión que tenía el defecto, y por eso recibe el conjunto de candidatos
    ''' como PARÁMETRO: una versión anterior extrajo sólo el filtro de ceros y duplicados —un pedazo que
    ''' nunca había fallado— y el caso del gate quedaba VERDE aunque se repusiera el filtro por
    ''' candidatos, que es lo que borraba prendas del archivo del usuario.</para>
    ''' <para>Dos respuestas correctas: el 0 no entra —el formato no declara NULL para INAM, ver
    ''' <see cref="OutfitDraft.ReemplazarPrendas"/>— y TODO LO DEMÁS entra, marcado si no es candidato.</para>
    ''' <para>⛔ NO deduplica. Hubo una versión que descartaba la repetición exacta «porque no agrega
    ''' nada al torneo de equip»: eso es una regla de la APP, sin cita del formato, y además cambiaba el
    ''' comportamiento — el sembrador viejo no deduplicaba, así que descartar era PERDER una entrada del
    ''' archivo del usuario. Medido sobre el corpus: 0 duplicados en 1.241 OTFT (750 de Skyrim, 491 de
    ''' FO4), o sea que la guarda habría sido inerte — pero inerte no es lo mismo que correcta.</para>
    ''' <para>⚠️ El ORDEN de la lista es la secuencia de equip y el último gana el slot. Eso es premisa
    ''' NUESTRA, declarada en <c>EquipResolver</c>, no una ley citada: la línea de xEdit que sostiene lo
    ''' del NULL usa <c>wbArrayS</c>, que es el constructor de arreglo ORDENADO, así que no se puede
    ''' invocar para hablar del orden autorado.</para></summary>
    Friend Shared Function PlanDeSembrado(inam As IEnumerable(Of UInteger),
                                          aplica As Func(Of UInteger, Boolean),
                                          esLeveled As Func(Of UInteger, Boolean)) _
                                          As List(Of (Fid As UInteger, NoAplica As Boolean,
                                                      EsLeveled As Boolean, Orden As Integer))
        Dim salida As New List(Of (Fid As UInteger, NoAplica As Boolean,
                                   EsLeveled As Boolean, Orden As Integer))
        If inam Is Nothing Then Return salida
        Dim orden = 0
        For Each fid In inam
            If fid = 0UI Then Continue For
            orden += 1
            salida.Add((fid,
                        Not (aplica IsNot Nothing AndAlso aplica(fid)),
                        esLeveled IsNot Nothing AndAlso esLeveled(fid),
                        orden))
        Next
        Return salida
    End Function

    ''' <summary>Qué emite una referencia del atuendo (ARMO o LVLI) sobre ESTE NPC. Es LA consulta del
    ''' formulario: las dos preguntas salen de acá y de ningún otro lado.
    ''' <para>⛔ No la contesta el formulario: se la pregunta al COLECTOR
    ''' (<see cref="MainForm.EmisionDeArmo"/>), que es quien arma la geometría de verdad. Antes se derivaba
    ''' de <c>DibujaAlgunArmature</c>, que sólo mira los armatures y por eso es un SUBCONJUNTO: una prenda
    ''' que monta por chunk-mount de OMOD se dibuja perfecto y no tiene un solo armature.</para>
    ''' <para>Una LVLI no emite nada: emiten sus TERMINALES, así que se agrega — dibuja si alguno dibuja,
    ''' compite si alguno compite. Los terminales salen de <c>EnumerateLeveledTerminalsAll</c>, que es
    ''' draft-aware, así que con un borrador de override de la lista se ve lo editado y no el disco.</para>
    ''' <para>Sobre el TERMINAL y no sobre el FormID crudo del INAM: un ARMO hijo de plantilla trae sus
    ''' armatures en el terminal (<c>TemplateArmor</c>), y preguntando por el crudo la fila decía «no
    ''' aplica» sobre una prenda que el render dibuja perfecto.</para>
    ''' <para>El memo es POR PASADA y lo trae el llamador: como campo sería una foto que se desincroniza,
    ''' que es el defecto que ya se cazó dos veces acá — con el signo para un lado y para el otro.</para></summary>

    Private Function EmisionDe(fid As UInteger,
                               Optional memo As Dictionary(Of UInteger, (Dibuja As Boolean, Compite As Boolean)) = Nothing) _
                               As (Dibuja As Boolean, Compite As Boolean)
        If fid = 0UI Then Return (False, False)
        Dim cacheada As (Dibuja As Boolean, Compite As Boolean)
        If memo IsNot Nothing AndAlso memo.TryGetValue(fid, cacheada) Then Return cacheada
        Dim r As (Dibuja As Boolean, Compite As Boolean) = (False, False)
        If _mainForm.IsLeveledItem(fid) Then
            For Each term In _mainForm.EnumerateLeveledTerminalsAll(fid)
                Dim e = EmisionDe(term, memo)
                r = (r.Dibuja OrElse e.Dibuja, r.Compite OrElse e.Compite)
                If r.Dibuja AndAlso r.Compite Then Exit For
            Next
        Else
            ' Sobre el FormID CRUDO: el colector pide la vista EFECTIVA y resuelve la herencia el
            ' mismo. Resolverla tambien aca era la segunda copia de la ley.
            r = _mainForm.EmisionDeArmo(fid, _visualState)
        End If
        If memo IsNot Nothing Then memo(fid) = r
        Return r
    End Function

    ''' <summary>¿Esta prenda SE VE en este NPC? Es la mitad <c>Dibuja</c> de <see cref="EmisionDe"/>.
    ''' <para>⛔ «Se ve» y «pelea el slot» son DOS preguntas, y acá se contestaba una sola para las
    ''' dos. Se ve = emitió algún candidate, chunk-mounts incluidos. Pelear el slot es la otra mitad, y la
    ''' usa <see cref="BuildEquipUnits"/>.</para>
    ''' <para>⛔ No se pregunta «está entre los candidatos»: ese universo incluye SIEMPRE a los
    ''' borradores ARMO propios, tengan o no armature, así que uno recién creado entraba sin marcar,
    ''' competía con el BOD2 crudo y eliminaba a la armadura real de la vista previa — que el render sí
    ''' dibujaba, porque ahí el borrador sin ARMA no emite candidato. Con <c>EmisionDe</c> los dos lados dan
    ''' la MISMA respuesta por el mismo código, en vez de dos aproximaciones que coincidían de casualidad.</para></summary>
    Private Function AplicaAEsteNpc(fid As UInteger) As Boolean
        Return EmisionDe(fid).Dibuja
    End Function

    ''' <summary>Siembra en la grilla lo que el plan diga. NO decide nada: la decisión entera vive en
    ''' <see cref="PlanDeSembrado"/>, que es `Friend Shared` y por eso el gate la puede recorrer.
    ''' <para>⛔ Antes la decisión estaba acá, en un `Private Sub` de un formulario, y el caso del gate
    ''' medía una función auxiliar que nunca había fallado: poner un `Continue For` sobre las marcadas
    ''' reproducía EL defecto original — borrarle prendas al usuario — y la suite seguía en verde.</para>
    ''' <para>Lo que queda acá es sólo lo que necesita la UI y el resolvedor: el nombre, el plugin y el
    ''' muestreo de la realización.</para></summary>
    Private Sub SembrarInam(inam As IEnumerable(Of UInteger))
        For Each e In PlanDeSembrado(inam, AddressOf AplicaAEsteNpc, AddressOf _mainForm.IsLeveledItem)
            ' ⛔ El orden sale del PLAN. Con un contador aparte, el `Orden` que el plan calcula quedaba
            ' sin consumir y el aserto del gate medía un campo muerto: el orden real se podía romper con
            ' el caso en verde. `_pieceOrderCounter` se sincroniza para que lo que se AGREGUE después
            ' («Add to outfit») siga la numeración.
            _pieceOrderCounter = e.Orden
            Dim it As (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String) = Nothing
            Dim pieza As PieceEntry
            ' ⛔ La decisión es la del PLAN y nada más. Acá hubo una segunda compuerta
            ' (`AndAlso _itemCandidatesByFid.TryGetValue(…)`), o sea que la marca efectiva era
            ' `plan.NoAplica OR NO-pertenencia` — y con eso se podía reponer EL defecto original mutando
            ' esta línea, con el caso del gate en verde, porque el gate mide el plan y no llega hasta acá.
            ' El índice se consulta SÓLO para el texto y el plugin, que es dato de presentación.
            If Not e.NoAplica Then
                If Not _itemCandidatesByFid.TryGetValue(e.Fid, it) Then
                    it = (e.Fid, _mainForm.GetRecordDisplayNameForEditor(e.Fid), 0UI,
                          _mainForm.GetOutfitPluginName(e.Fid))
                End If
                pieza = New PieceEntry With {.FormID = it.FormID, .Display = it.DisplayName,
                                             .SlotMask = it.SlotMask, .Order = _pieceOrderCounter,
                                             .Plugin = it.Plugin}
            Else
                ' La marcada también lleva su plugin: es la fila donde el usuario decide si la saca.
                pieza = New PieceEntry With {.FormID = e.Fid,
                                             .Display = _mainForm.GetRecordDisplayNameForEditor(e.Fid),
                                             .SlotMask = 0UI, .Order = _pieceOrderCounter,
                                             .Plugin = _mainForm.GetOutfitPluginName(e.Fid)}
            End If
            ' ⛔ La clasificación sale del PLAN, no de otra consulta: sin esto una LVLI cuyos terminales
            ' no aplican quedaba como ARMO concreto y se rompían tres cosas — Reroll apagado aunque el
            ' atuendo SÍ tiene lista, el doble clic abriendo un editor de ARMO EN BLANCO porque el editor
            ' rechaza la firma, y el FormID de la lista viajando al draft de vista previa como terminal
            ' plano, contra el contrato declarado de ese draft.
            If e.EsLeveled Then
                pieza.IsLeveled = True
                Dim r = _mainForm.SampleLeveledRealization(e.Fid, _raceFormID, _isFemale)
                pieza.Realization = r.Picks
                pieza.SlotMask = r.SlotMask
            End If
            _pieces.Add(pieza)
        Next
    End Sub

    ''' <summary>Agrega un item (ARMO o LVLI, incluido un borrador propio) a la lista por FormID:
    ''' deduplica, arma la <see cref="PieceEntry"/>, muestrea la realización si es leveled y refresca.
    ''' Lo comparten «Add to outfit» y «New LVL…».
    ''' <para>⚠️ Es el camino de AUTORAR, no el de LEER: su deduplicación es correcta ací —el usuario no
    ''' agrega dos veces lo mismo— y sería incorrecta al sembrar desde el archivo, donde manda el INAM.
    ''' Por eso <see cref="SembrarInam"/> NO pasa por acá.</para></summary>
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
            piece.Realization = r.Picks
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
    ''' nothing selected, or already at the edge. RefreshPieces re-sorts by Order, repaints/, preserves the
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
                p.Realization = r.Picks
                p.SlotMask = r.SlotMask
                any = True
            End If
        Next
        If Not any Then Return
        _lastPreviewKey = Nothing   ' the piece FormIDs are unchanged; force the re-render of the new sample
        RefreshPieces()
    End Sub

    ''' <summary>Rebuild the Create item-candidate list (after a new LVL draft is created) so own LVL drafts
    ''' () appear/update, then re-apply the current filter.</summary>
    Private Sub RefreshItemCandidates()
        SetItemCandidates(_mainForm.GetArmoItemCandidatesWithDrafts(_raceFormID, _isFemale))
        OnItemFilterChanged(Me, EventArgs.Empty)   ' re-filters + RefreshItemList
    End Sub

    ''' <summary>"New LVL…" → modal (name + 3 LVLF flags + Chance None + Max Count) → register an empty own
    ''' LeveledListDraft, which then shows in the item list () ready to be filled via "Add to lvl".</summary>
    Private Sub OnNewLvl(sender As Object, e As EventArgs)
        Using dlg As New LeveledListEditor_Form(_mainForm)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim d = LeveledListDraft.Nuevo(_mainForm.AllocateDraftFormID(),
                                           Canon.CanonBridge.SessionGame())
            d.Record.EditorID = dlg.FullEditorID
            d.Record.FlagsCalculateFromAllLevelsPlayerSLevel = dlg.CalcAllLevels
            d.Record.FlagsCalculateForEachItemInCount = dlg.CalcEachInCount
            d.Record.FlagsUseAll = dlg.UseAll
            d.Record.ChanceNone = dlg.ChanceNoneValue
            ' Max Count (LVLM) sólo existe en Fallout 4 — en Skyrim ese subrecord no está en el
            ' formato.
            Dim fo4Rec = TryCast(d.Record, Canon.LvliFO4)
            If fo4Rec IsNot Nothing Then fo4Rec.MaxCount = dlg.MaxCountValue
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
        Dim itemLabel = If(itemName, itemFid.ToString("X8"))
        Dim addToLvlTitle = $"Add '{itemLabel}'  →  '{lvl.Record.EditorID}'"
        Using dlg As New LeveledEntryDialog_Form(addToLvlTitle)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim en = lvl.Record.AgregarLeveledListEntries()
            If en Is Nothing Then Return
            en.LeveledListEntryItem = itemFid
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
            lvl.IsModified = True
        End Using
        ' The LVL's contents changed → re-sample the affected piece's realization + refresh list/candidates.
        Dim r = _mainForm.SampleLeveledRealization(target.FormID, _raceFormID, _isFemale)
        target.Realization = r.Picks
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
    ''' (winners / eliminated, losers greyed), preview the resolved (winner) set, and update the
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
                    p.Realization = rr.Picks
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

        ' La MISMA ley que el render: una unidad por ARMO terminal, veredicto por unidad, agregado por fila.
        ' ⛔ UN memo por pasada, compartido: el torneo y las filas tienen que contestar con la MISMA
        ' emisión. Con un memo por lado, una prenda podía quedar fuera del torneo y marcada como que se ve,
        ' o al revés, y la barra volvía a mentir por otro camino.
        Dim emisionMemo As New Dictionary(Of UInteger, (Dibuja As Boolean, Compite As Boolean))
        Dim units = BuildEquipUnits(emisionMemo)
        Dim res = EquipResolver.Resolve(units)
        Dim verdicts = PieceVerdicts(res)
        ' La máscara que se pinta es EXACTAMENTE la que decidió el/(EquipResolver.MutexMaskOf), no otra:
        ' hoy en FO4 esa no es el BOD2 del ARMO, y pintar el BOD2 daba dos listas de slots distintas para el
        ' mismo ítem en la misma ventana. Cuando FO4 pase a EquipMask, esta columna lo sigue sola.
        Dim mutexMaskByPiece As New Dictionary(Of PieceEntry, UInteger)
        For Each u In units
            Dim pe = TryCast(u.Tag, PieceEntry)
            If pe Is Nothing Then Continue For
            mutexMaskByPiece(pe) = If(mutexMaskByPiece.ContainsKey(pe), mutexMaskByPiece(pe), 0UI) Or EquipResolver.MutexMaskOf(u)
        Next
        Dim renderingCount As Integer = 0, eliminatedCount As Integer = 0, rolledNothingCount As Integer = 0, noAplicanCount As Integer = 0
        ListViewPieces.BeginUpdate()
        Try
            ListViewPieces.Items.Clear()
            ' ⛔ La marca se DERIVA acá, no se guarda. Como campo era una FOTO que sólo se refrescaba al
            ' volver del editor de ARMO: agregar un ARMO válido a una lista por nivel recién creada dejaba
            ' la fila diciendo «no aplica» y la barra «0 of 1 rendering» mientras la vista previa SÍ la
            ' dibujaba — el mismo defecto de antes con el signo dado vuelta. Derivada no se puede
            ' desincronizar; y sale del MISMO memo que alimentó al torneo, así que las dos mitades de la
            ' respuesta no se pueden separar.
            Dim marcada = Function(fid As UInteger) As Boolean
                              Return Not EmisionDe(fid, emisionMemo).Dibuja
                          End Function
            For Each p In _pieces.OrderBy(Function(x) x.Order)
                Dim noAplica = marcada(p.FormID)
                Dim v = If(verdicts.ContainsKey(p), verdicts(p), (Won:=0, Lost:=0))
                Dim hasUnits = (v.Won + v.Lost) > 0
                ' ⛔ La prenda que no aplica se VE, marcada: si no se ve no se puede sacar, y borrarla
                ' en silencio era el defecto original.
                Dim etiqueta = If(p.IsLeveled, "🎲 " & p.Display, p.Display)
                ' ⛔ «no aplica a este NPC» y no «a esta raza/género»: el predicado (`Valid` del
                ' footprint) junta TRES motivos — sin ARMA de la raza o sin malla del género, el gate de
                ' power armor, y un FormID que no resuelve a nada. Afirmar el primero en los tres casos le
                ' muestra al usuario un problema de raza donde hay una referencia colgada.
                If noAplica Then etiqueta = "⚠ " & etiqueta & "  (no aplica a este NPC)"
                Dim row As New ListViewItem(etiqueta)
                Dim status As String
                Dim slotsText As String
                If noAplica Then
                    ' ⛔ Estado PROPIO, no un veredicto del torneo: no corrió en él. Contarla como
                    ' «renderizando» hacía que la barra dijera «3 of 3» mientras la vista previa dibujaba 2.
                    ' Ahora la marcada TAMPOCO compite (`BuildEquipUnits` la filtra por `Compite`), así que
                    ' ya no puede ganarle el slot a la que sí se ve — que era el «0 of 2 rendering» con una
                    ' prenda dibujada en pantalla.
                    noAplicanCount += 1
                    status = "— no aplica"
                    slotsText = "(no aplica a este NPC)"
                    row.ForeColor = Color.Gray
                ElseIf Not hasUnits AndAlso PieceTerminals(p).Any(Function(f) EmisionDe(f, emisionMemo).Dibuja) Then
                    ' ⛔ SE VE PERO NO PELEA EL SLOT, y SE RENDERIZA: va al borrador como cualquier otra
                    ' ganadores. No es «rolled nothing» —eso es una lista que no sorteó, y se distingue porque
                    ' ahi NINGUN terminal dibuja—: emite geometría por chunk-mount de OMOD, que sale
                    ' con `SlotMask = 0` y se dibuja por la pasada slotless del render sin entrar al torneo.
                    ' Cuenta como RENDERIZANDO porque se ve: mandarla al balde de «no aplica» o al de «rolled
                    ' nothing» es la misma mentira de antes movida de lugar.
                    renderingCount += 1
                    status = "✓"
                    slotsText = "(sin slot · montada)"
                ElseIf Not hasUnits Then
                    ' La lista no sorteó nada (ChanceNone). La fila NO puede mostrar "(none)": lo que la
                    ' pieza ES sigue siendo la lista, así que se muestra su footprint, etiquetado.
                    rolledNothingCount += 1
                    status = "— rolled nothing"
                    Dim listMask = _mainForm.GetReferenceSlotMask(p.FormID, _raceFormID, _isFemale)
                    slotsText = If(listMask = 0UI, "(none)", "list: " & DescribeSlotMask(listMask))
                    row.ForeColor = Color.Gray
                ElseIf v.Lost = 0 Then
                    renderingCount += 1
                    status = "✓"
                    slotsText = DescribeSlotMask(mutexMaskByPiece(p))
                ElseIf v.Won = 0 Then
                    eliminatedCount += 1
                    status = "✗ eliminated"
                    slotsText = DescribeSlotMask(mutexMaskByPiece(p))
                    row.ForeColor = Color.Gray
                Else
                    ' Una lista UseAll puede realizar en varios ARMO: unos ganan y otros caen. Pintarla o
                    ' sería mentira en los dos sentidos.
                    renderingCount += 1
                    status = $"◐ {v.Won}/{v.Won + v.Lost}  ·  {v.Lost} eliminated"
                    slotsText = DescribeSlotMask(mutexMaskByPiece(p))
                End If
                row.SubItems.Add(slotsText)
                row.SubItems.Add(status)
                row.SubItems.Add(p.Plugin)
                row.Tag = p
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

        Dim statusText = $"{renderingCount} of {_pieces.Count} piece(s) rendering"
        If eliminatedCount > 0 Then statusText &= $"  ·  {eliminatedCount} eliminated by slot conflict"
        If rolledNothingCount > 0 Then statusText &= $"  ·  {rolledNothingCount} rolled nothing"
        If noAplicanCount > 0 Then statusText &= $"  ·  {noAplicanCount} no aplica(n) a este NPC"
        LabelCreateStatus.Text = statusText

        ' Preview only when the Create tab is active and the host exists (skipped during construction,
        ' where the active tab is Browse and _host has not been created yet). The list repaint above ran
        ' synchronously before this Await, so the/feedback is immediate. RefreshCreatePreview honors
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
            ' Los ARMO terminales ganadores del sorteo actual — el mismo veredicto y el mismo sorteo que
            ' pinta la lista (Reroll cambia la realización y con ella esto).
            Await PreviewCreateAssemblyAsync()
        End If
    End Function

    ''' <summary>Terminal ARMO FormIDs to preview for a single selected FormID: a chosen LVLI piece → its
    ''' cached realization; a candidate LVLI item (not yet added) → a fresh sample; an ARMO → itself.</summary>

    ''' <summary>Preview a single ARMO piece via the throwaway draft (one-item set) — same WYSIWYG host
    ''' path the assembly preview uses, just with one FormID.</summary>
    ''' <summary>Vista previa de UNA pieza por el borrador desechable — el mismo camino WYSIWYG del atuendo
    ''' entero, con una sola prenda.
    ''' <para>⛔ Va la pieza COMO LA AUTORÓ EL USUARIO (LVLI o ARMO) más su realización, y NO los terminales
    ''' pelados: aplanar tiraba las keywords del LLKC de cada `OutfitArmorPick`, y con el contexto vacío
    ''' `ObjectTemplateResolver.ResolveCombinationList` ni entra a la rama de keywords — o sea la MISMA S3
    ''' que arregla `ArmarBorradorDeAtuendo`, por el otro carril. Este carril existe y v1 no lo veía.</para></summary>
    Private Async Function PreviewCreatePieceAsync(fid As UInteger) As Task
        Dim p = _pieces.FirstOrDefault(Function(x) x.FormID = fid)
        Dim esLeveled As Boolean
        Dim picks As List(Of OutfitArmorPick) = Nothing
        If p IsNot Nothing Then
            esLeveled = p.IsLeveled
            If esLeveled Then picks = p.Realization
        Else
            esLeveled = _mainForm.IsLeveledItem(fid)
            If esLeveled Then picks = _mainForm.SampleLeveledRealization(fid, _raceFormID, _isFemale).Picks
        End If
        Dim una As New List(Of (Fid As UInteger, EsLeveled As Boolean, Picks As List(Of OutfitArmorPick)))
        una.Add((fid, esLeveled, picks))
        Dim d = OutfitDraft.Nuevo(OutfitDraft.PreviewDraftFormID, Canon.CanonBridge.SessionGame())
        d.Record.EditorID = OutfitDraft.EditorIdPrefix & "(preview)"
        VolcarPiezasEn(d, una)
        Await RequestPreviewAsync(OutfitDraft.PreviewDraftFormID,
                                  "piece:" & fid.ToString("X") & ":" & LlaveDelBorrador(d),
                                  pieceOnly:=True, draft:=d)
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

    ''' <summary>"New outfit" action → author a brand-new OTFT record: clear any override target,
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
    ''' as an OVERRIDE: keep its FormID + EditorID (EDID locked), pre-fill its pieces.
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
            If Borradores.EsFormIdDeBorrador(_overrideTargetFormID) Then
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
        ' ⛔ TODAS entran, también las que no aplican a esta raza/género: ver `SembrarInam`.
        SembrarInam(_mainForm.ResolveOutfitItemList(fid))
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
        ' Persist ALL assembled pieces, NOT just the slot-conflict winners. The "eliminated" tag in the
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

        Dim draft As OutfitDraft
        If _overrideTargetFormID <> 0UI Then
            ' A draft target (provisional 0xFF FormID) is a NEW owned record being re-edited — RENAMEABLE: keep
            ' its FormID but rebuild the EDID from the editable name box. A real OTFT FormID is an OVERRIDE — keep
            ' its FormID + EditorID verbatim.
            Dim isDraftTarget = Borradores.EsFormIdDeBorrador(_overrideTargetFormID)
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
                Dim currentEdid = If(current IsNot Nothing, current.Record.EditorID,
                                     _overrideTargetEditorID)
                If Not String.Equals(fullEdid, currentEdid, StringComparison.OrdinalIgnoreCase) _
                   AndAlso Not _mainForm.IsOutfitEditorIdAvailable(fullEdid) Then
                    MessageBox.Show(Me, $"EditorID '{fullEdid}' is already in use. Choose another name.",
                                    "Create Outfit", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                    DialogResult = DialogResult.None
                    Return
                End If
                draft = OutfitDraft.Nuevo(_overrideTargetFormID, Canon.CanonBridge.SessionGame())
                draft.Record.EditorID = fullEdid
                draft.IsOverride = False
            Else
                draft = OutfitDraft.Nuevo(_overrideTargetFormID, Canon.CanonBridge.SessionGame())
                draft.Record.EditorID = _overrideTargetEditorID
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
            draft = OutfitDraft.Nuevo(_mainForm.AllocateDraftFormID(),
                                      Canon.CanonBridge.SessionGame())
            draft.Record.EditorID = fullEdid
            draft.IsOverride = False
        End If
        ' INAM = EVERY assembled piece FormID as authored — ARMO or LVLI (LVLIs persist as leveled entries).
        ' Slot conflicts are resolved at render/equip time, never dropped at save (see the header comment).
        ' ⛔ `allPieces` YA trae las que no aplican a esta raza/género, marcadas: son parte del atuendo
        ' y viajan en la MISMA lista y el mismo orden. Hubo una versión con un carril paralelo que se
        ' reintercalaba acá; rompía de cuatro formas y se sacó — ver `SembrarInam`.
        ' ⛔ EL MISMO volcado que la vista previa y que el contexto, no una copia con el mismo texto: si
        ' esto se separa otra vez, el usuario guarda algo distinto de lo que vio. `CommitCreate` escribe
        ' sobre un `draft` que YA existe (override o nuevo), así que no puede usar `ArmarBorradorDeAtuendo`
        ' —que CREA uno—: comparte el volcado, que es la parte que era la ley.
        VolcarPiezasEn(draft, ComoTuplas(allPieces))
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
                Dim row As New ListViewItem(d.Record.EditorID)
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
        _overrideTargetEditorID = d.Record.EditorID
        ' A NEW owned draft is renameable (editable name, live preview, keeps its FormID on re-commit); a real
        ' OVERRIDE draft keeps its EDID read-only. Identity for CommitCreate is carried by _overrideTargetFormID.
        RefreshOutfitEdidField(d.IsNew, d.Record.EditorID)
        ' Populate _itemCandidatesByFid before the per-item lookups in AddItemFidAsPiece.
        RefreshItemCandidates()
        _pieces.Clear()
        _pieceOrderCounter = 0
        ' ⛔ El MISMO sembrador que `PrefillPiecesFromOutfit`. Con `AddItemFidAsPiece` acá, reabrir el
        ' borrador volvía a descartar lo que no es candidato: el arreglo no sobrevivía su propio
        ' round-trip — override, aceptar, reabrir para retocar el nombre, y las prendas se caían igual.
        SembrarInam(d.Prendas())
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
            If MessageBox.Show(Me, $"Revert outfit '{d.Record.EditorID}' to the original? " &
                               "Your changes will be discarded.",
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
            If MessageBox.Show(Me, $"Delete outfit draft '{d.Record.EditorID}'?",
                               "Delete outfit draft",
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

    ''' <summary>El "chance none" POR ENTRADA (LVLO\Chance None) sólo existe en Fallout 4 — en
    ''' Skyrim ese
    ''' byte no está en el formato. 0 para una entrada de Skyrim, nunca un dato inventado.</summary>
    Friend Shared Function EntryChanceNone(en As Canon.ILvli_LeveledListEntries) As Byte
        Dim fo4 = TryCast(en, Canon.LvliFO4_LeveledListEntries)
        Return If(fo4 IsNot Nothing, fo4.LeveledListEntryChanceNone, CByte(0))
    End Function

    ''' <summary>Contraparte de <see cref="EntryChanceNone"/>: no-op en Skyrim, donde el campo no
    ''' existe.</summary>
    Friend Shared Sub SetEntryChanceNone(en As Canon.ILvli_LeveledListEntries, value As Byte)
        Dim fo4 = TryCast(en, Canon.LvliFO4_LeveledListEntries)
        If fo4 IsNot Nothing Then fo4.LeveledListEntryChanceNone = value
    End Sub

    ''' <summary>Saca <paramref name="en"/> de <paramref name="rec"/>. La baja por referencia la
    ''' resuelve la vista generada: quien tiene el elemento en la mano no tiene por que saber en que
    ''' posicion quedo.</summary>
    Private Shared Sub RemoveEntry(rec As Canon.ILvli, en As Canon.ILvli_LeveledListEntries)
        rec.QuitarLeveledListEntries(en)
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
