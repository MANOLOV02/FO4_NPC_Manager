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
    ''' <summary>Ya se avisó que el registro del borrador de vista previa falló. UNA sola vez por diálogo:
    ''' el repintado se dispara con cada selección, y un box por pedido convierte un aviso en una trampa
    ''' de la que el usuario no puede salir. Mismo criterio que <c>_previewCommitFallado</c>.</summary>
    Private _previewRegistroFallado As Boolean
    ''' <summary>Las listas por nivel que ESTE diálogo registró en esta sesión. Se bajan al cerrar SIN OK.
    ''' <para>⛔ «Cancelar el editor tiene que dejar el original como estaba» es el contrato escrito de los
    ''' otros borradores — y el docstring de <see cref="MainForm.BuildLeveledOverrideDraftFromReal"/> ya lo
    ''' prometía: «On Cancel the caller unregisters this draft». Las listas eran la excepción: se
    ''' registraban al abrir el drill-down y quedaban para siempre, así que una lista sucia se guardaba
    ''' aunque nada la referenciara.</para>
    ''' <para>⛔ Sólo las que registró ESTE diálogo: si el borrador YA existía (de otra sesión de edición o
    ''' de un New LVL anterior que el usuario aceptó) no es nuestro y no se toca. Los ARMO/ARMA editados
    ''' desde acá tampoco entran: tienen su propio modal con su propio OK/Cancel.</para></summary>
    Private ReadOnly _lvliRegistradasPorMi As New HashSet(Of UInteger)

    ''' <summary>Estado de APERTURA de cada lista por nivel PREEXISTENTE que esta sesión mutó. Se restaura al
    ''' cerrar SIN OK.
    ''' <para>⛔ La otra mitad de «cancelar deja el original como estaba»: desregistrar sólo lo que creamos
    ''' NO alcanza. Si el diálogo crea la lista A, la mete como entrada de la lista PREEXISTENTE B y el
    ''' usuario cancela, A se va y B se queda apuntando al 0xFF de A — una referencia a un record que ya no
    ''' existe. El guardado la levanta (el remapper del writer tira al no poder darle FormID real), o sea
    ''' que cancelar dejaba al usuario con un guardado ROTO. Restaurando B desaparece de raíz.</para>
    ''' <para>⛔ Sólo PREEXISTENTES: las que registró este diálogo se bajan enteras
    ''' (<see cref="_lvliRegistradasPorMi"/>), y no hay nada que revertir. Se toma UNA vez, justo ANTES de
    ''' la primera mutación — el mismo gesto que publica la foto, en los mismos cuatro mutadores.</para></summary>
    Private ReadOnly _lvliSnapshotDeApertura As New Dictionary(Of UInteger, LeveledListDraft)
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

    ''' <summary>Generación del pedido de vista previa. La incrementa <see cref="ClearPreview"/>: un
    ''' repintado que vuelve de su <c>Await</c> con la generación vieja es de un pedido que ya no vale.</summary>
    Private _previewGeneration As Integer = 0

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
    ''' re-samples the leveled pieces' realizations to reflect it. Cleared after that one re-sample.
    ''' <para>⛔ <b>ES SÓLO DEL DRILL-DOWN, y ahí SÍ es global a propósito.</b> Metido en una lista por nivel el
    ''' usuario puede haber editado CUALQUIERA de la cadena que abrió —y puede haber entrado y salido de
    ''' varias antes de volver—, así que la bandera no sabe qué lista cambió: la única respuesta correcta con
    ''' ese dato es «todas». Es una BANDERA y no un re-muestreo inmediato porque mientras hay nivel abierto
    ''' <c>RefreshPieces</c> se va por el carril anidado y las piezas del atuendo no se repintan; se consume al
    ''' volver a la raíz, o en el commit si el usuario aprieta OK sin volver.</para>
    ''' <para>⛔ Y por eso la edición de RAÍZ («Add to lvl») NO la usa: ahí se sabe exactamente qué lista se
    ''' tocó, así que re-muestrea dirigido — ver <see cref="PlanDeReMuestreo"/>. Volver acá desde la raíz le
    ''' pisa al usuario el Reroll que eligió en otra lista.</para></summary>
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
        ''' <summary>⚠️ SÓLO lo lee la vista del drill-down (<c>_levelView</c>). Para una pieza de nivel
        ''' superior la columna de slots sale del veredicto del torneo (<c>VeredictosPorFila</c>, cuyas
        ''' máscaras da el COLECTOR), así que acá es informativo. `mutexMaskByPiece` —el diccionario que
        ''' esta línea citaba— ya no existe: se fusionó con el veredicto para que la marca y la máscara no
        ''' pudieran salir de dos recorridos distintos.</summary>
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
        RefreshOutfitEdidField(0UI, OutfitDraft.EditorIdPrefix)   ' sin objetivo = "New outfit" (nombre vacío)
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
        ' ⛔ `ListViewPieces` NO se ordena por columna, y es la unica de las cuatro. Su orden ES LA
        ' SECUENCIA DE EQUIP: es lo que `ArmarBorradorDeAtuendo` vuelca al INAM con
        ' `_pieces.OrderBy(Order)`, y lo que decide quien gana el slot (last-equipped-wins). Un clic en
        ' el encabezado reordenaba la VISTA y dejaba al usuario mirando un orden que no es el que se
        ' guarda — y si despues tocaba las flechas, movia respecto de un orden que no existia.
        ' Las otras tres son listas de CONSULTA: ahi ordenar es util y no significa nada.
        SortableListView.Attach(ListViewMyOutfits)

        ' First open: default to OVERRIDE of the NPC's current outfit (your plugin replaces it) — the usual
        ' intent when editing what the NPC already wears. Pre-fill its pieces and keep its EDID (read-only). The
        ' user can switch to "New outfit" explicitly. With no current outfit → stay in the New mode set above.
        If _currentEffectiveOutfitFID <> 0UI Then
            _overrideTargetFormID = _currentEffectiveOutfitFID
            _overrideTargetEditorID = _mainForm.GetOutfitDisplayName(_currentEffectiveOutfitFID)
            ' ⛔ EL OBJETIVO DECIDE, no un `False` literal: el atuendo cargado del NPC puede ser un BORRADOR
            ' (su DOFT apunta a un 0xFF propio) y ése es renombrable. Ver `ElNombreEsSufijo`.
            RefreshOutfitEdidField(_overrideTargetFormID, _overrideTargetEditorID)
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
                    ' ⛔ CON Try PROPIO, igual que `ArmoEditor_Form.CommitProtegido` y por lo mismo:
                    ' `RegisterOutfitDraft` ahora PUBLICA LA FOTO, la foto de atuendo clona con
                    ' `OutfitDraft.Clone` y ése tiene una precondición que TIRA. El Try de más arriba sólo
                    ' tiene `Finally`, y el `Catch` que salva el repintado empieza recién después del
                    ' `ApplyPreviewToggles`: un throw en esta línea sale del `Async Function`, y con
                    ' `UnhandledExceptionMode.ThrowException` eso CIERRA la app con el trabajo del usuario
                    ' adentro. La vista previa que no se puede armar se avisa y se sigue.
                    Try
                        _mainForm.RegisterOutfitDraft(reqDraft)
                        _previewDraftRegistered = True
                    Catch ex As Exception
                        Logger.Log("OutfitPicker.RequestPreviewAsync: " & ex.ToString())
                        If Not _previewRegistroFallado Then
                            _previewRegistroFallado = True
                            MessageBox.Show(Me,
                                "No se pudo armar la vista previa de este atuendo:" & vbCrLf & vbCrLf &
                                ex.Message & vbCrLf & vbCrLf &
                                "El detalle quedó en el log. Podés seguir editando la lista de prendas.",
                                "Vista previa", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                        End If
                        ' ⛔ `Continue While`, NO `Return`, por lo MISMO que el descarte de abajo: lo que
                        ' fracasó es ESTE pedido, no la cola. Con el `Return`, un registro que falla mata el
                        ' bucle entero y se lleva puesto el pedido PENDIENTE — el usuario cambia de fila
                        ' mientras tanto y esa fila se queda sin dibujar, sin nada que lo explique.
                        Continue While
                    End Try
                End If
                ApplyPreviewToggles(reqPieceOnly)
                Dim gen = _previewGeneration
                Try
                    Await _mainForm.PreviewOutfitInHostAsync(_host, _npcFormID, reqOverride)
                    ' ⛔ EL REPINTADO QUE VUELVE TARDE SE DESCARTA. Si mientras se resolvía el `Await`
                    ' alguien llamó a `ClearPreview` —deseleccionar en Browse (`OnListSelectionChanged`),
                    ' o quedarse sin pieza seleccionada en modo «una prenda» (`RefreshCreatePreview`):
                    ' esos son sus DOS llamadores, cerrar el diálogo NO es uno—, este resultado es de un
                    ' pedido que ya no vale: no se marca como dibujado y se vuelve a limpiar, porque el
                    ' render ya pintó encima de lo que `ClearPreview` había borrado.
                    If gen <> _previewGeneration Then
                        Try
                            _preview?.RenderShapes(New List(Of IRenderableShape))
                        Catch
                        End Try
                        ' ⛔ `Continue While`, NO `Return`: salir mataba el bucle ENTERO y con él el pedido
                        ' PENDIENTE. Secuencia real: se selecciona A, se selecciona B mientras A está en
                        ' vuelo, se deselecciona; A vuelve tarde, y con el `Return` B se perdía — la fila
                        ' seleccionada quedaba sin dibujar. Lo que se descarta es ESTE repintado, no la cola.
                        Continue While
                    End If
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
        ' ⛔ INVALIDA LO QUE ESTE EN VUELO. Antes sólo bajaba las banderas: el bucle seguía, resolvía su
        ' `Await` y REPINTABA encima —y encima re-fijaba `_lastPreviewKey`—, así que deseleccionar
        ' mientras un render estaba a mitad de camino dejaba el atuendo dibujado SIN fila seleccionada.
        ' Con la generación, el repintado que vuelve tarde se descarta y se limpia de nuevo.
        _previewGeneration += 1
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
        ' ⛔ Estos dos tambien: adentro de una lista por nivel, "New outfit" y "Override outfit" actuan
        ' sobre el ATUENDO, no sobre la lista que se esta editando. Visibles ahi, el clic tiraba al
        ' usuario a otra pantalla sin avisar que perdia el nivel en el que estaba.
        ButtonNewOutfit.Visible = Not nested
        ButtonOverrideOutfit.Visible = Not nested
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
        If d Is Nothing Then
            d = _mainForm.BuildLeveledOverrideDraftFromReal(fid)
            ' La promoción a override la hicimos NOSOTROS al entrar: se baja si el diálogo cierra sin OK.
            If d IsNot Nothing Then _lvliRegistradasPorMi.Add(d.FormID)
        End If
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
        ' ⛔ POR IDENTIDAD DE FILA, igual que en la raíz: las entradas de una lista por nivel pueden
        ' repetir el mismo item, y con la llave puesta en el FormID «Edit entry» y «Remove entry»
        ' actuaban sobre la PRIMERA que coincidiera — o sea sobre la entrada equivocada.
        ' ⛔ Y la identidad que se conserva es la de la ENTRADA (`SourceEntry`), no la del `PieceEntry`:
        ' `_levelView` se RECONSTRUYE entero acá abajo, así que las filas viejas son objetos muertos y
        ' `p Is keep` no puede dar True NUNCA — la selección se perdía en cada edición. Los
        ' `LeveledListEntries` del record sí sobreviven al rebuild (es el mismo árbol), y son justamente
        ' la identidad que este comentario decía querer.
        Dim keep As PieceEntry = SelectedPieceEntry()
        Dim keepEntry = If(keep Is Nothing, Nothing, keep.SourceEntry)
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
                If keepEntry IsNot Nothing AndAlso p.SourceEntry Is keepEntry Then row.Selected = True
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

    ''' <summary>QUÉ hacer con las listas por nivel al cerrar el diálogo: cuáles se REVIERTEN a su estado de
    ''' apertura y cuáles se BAJAN. No toca nada — devuelve el plan, y el llamador lo ejecuta.
    ''' <para>⛔ La decisión vive acá, <c>Friend Shared</c> y pura, por lo mismo que
    ''' <see cref="PlanDeSembrado"/>: dentro de <c>FormClosing</c> el gate no la puede correr, y un caso que
    ''' mira el TEXTO del manejador mide la letra en vez de la conducta.</para>
    ''' <para>⛔ SIN OK van las DOS mitades —revertir lo preexistente y bajar TODO lo creado—, porque hacer
    ''' una sola deja a una lista preexistente apuntando al 0xFF de la que se acaba de ir.</para>
    '''
    ''' <para>⛔ <b>CON OK SE BAJA LO CREADO QUE NADIE RECLAMA</b>, y esto es un arreglo: antes con OK no se
    ''' tocaba nada, con el argumento de que «el atuendo que el diálogo devuelve referencia esas listas». Eso
    ''' vale para el camino Create y NO para el camino Browse — «OK» es del DIÁLOGO, no del atuendo—: elegir
    ''' un OTFT existente y aceptar también sale por OK, y ahí <c>CommitCreate</c> ni corre, así que la lista
    ''' que la sesión creó para componer ESTE atuendo queda sin que nadie la nombre. <b>MEDIDO</b> (gate
    ''' <c>OutfitDraftSaveGate</c>, C52-M1, los dos juegos): una LVLI propia sucia que no referencia nadie
    ''' <b>llega igual al .esp</b> — la fase 2d siembra todo borrador sucio «even if unreferenced»
    ''' (<c>NpcOverrideSaver</c>) — y no hay ninguna interfaz para sacarla después:
    ''' <c>UnregisterLeveledListDraft</c> se llama SÓLO desde este cierre. Se creó para componer este
    ''' atuendo; aceptar otro la abandona.</para>
    '''
    ''' <para>⛔ <b>Y ES UN BARRIDO, no una pasada.</b> Se arranca dando por bajadas TODAS las creadas y se
    ''' SALVA la que tenga un referrer <b>fuera del conjunto que se va</b>, hasta que no se salve ninguna
    ''' más. Con una sola pasada, dos listas creadas y abandonadas donde A está adentro de B se salvan
    ''' mutuamente —A la referencia B, que también se va— y queda el huérfano de segundo nivel, que es el
    ''' mismo defecto una capa más abajo. Termina porque el conjunto sólo se achica.</para>
    '''
    ''' <para>Lo que NO se toca con OK: las PREEXISTENTES que la sesión mutó. Su ley es la de antes —se
    ''' revierten sólo al cancelar—: no las creó este diálogo y el usuario aceptó esas ediciones.</para>
    ''' <para><paramref name="tieneReferrerFuera"/> es <c>MainForm.TieneReferrerFueraDe</c>, o sea el MISMO
    ''' censo que decide si un borrador se puede borrar (<c>Borradores.CensarReferrers</c>): la fuente única,
    ''' no una segunda idea de qué cuenta como referencia. Se inyecta para que esta decisión quede pura y el
    ''' gate la corra.</para></summary>
    Friend Shared Function PlanDeCierreDeListas(esOk As Boolean,
                                                snapshots As IEnumerable(Of LeveledListDraft),
                                                creadas As IEnumerable(Of UInteger),
                                                tieneReferrerFuera As Func(Of UInteger, ICollection(Of UInteger), Boolean)) _
            As (Restaurar As List(Of LeveledListDraft), Bajar As List(Of UInteger))
        Dim restaurar As New List(Of LeveledListDraft)
        Dim bajar As New List(Of UInteger)
        Dim creadasList As New List(Of UInteger)
        If creadas IsNot Nothing Then creadasList.AddRange(creadas)

        If esOk Then
            If creadasList.Count = 0 Then Return (restaurar, bajar)
            ' ⛔ Sin el censo NO se adivina: dar todo por abandonado le borra al usuario listas que SÍ usa, y
            ' dar todo por vivo repone el defecto. Las dos direcciones son destructivas, así que se tira.
            If tieneReferrerFuera Is Nothing Then
                Throw New ArgumentNullException(NameOf(tieneReferrerFuera),
                    "PlanDeCierreDeListas necesita el censo de referrers para decidir qué lista creada quedó abandonada.")
            End If
            ' BARRIDO: todas candidatas, y se salva la que algo de AFUERA reclame.
            Dim aBajar As New HashSet(Of UInteger)(creadasList)
            Dim huboRescate = True
            While huboRescate
                huboRescate = False
                For Each fid In aBajar.ToList()
                    If tieneReferrerFuera(fid, aBajar) Then
                        aBajar.Remove(fid)      ' algo que NO se va la nombra: se queda
                        huboRescate = True
                    End If
                Next
            End While
            ' En el orden en que se crearon, para que la baja sea reproducible.
            For Each fid In creadasList
                If aBajar.Contains(fid) Then bajar.Add(fid)
            Next
            Return (restaurar, bajar)
        End If

        If snapshots IsNot Nothing Then
            For Each s In snapshots
                If s IsNot Nothing Then restaurar.Add(s)
            Next
        End If
        bajar.AddRange(creadasList)
        Return (restaurar, bajar)
    End Function

    ''' <summary>Guarda el estado de APERTURA de <paramref name="d"/> la PRIMERA vez que esta sesión lo va a
    ''' mutar, si es una lista PREEXISTENTE. Se llama JUSTO ANTES de la mutación, en los cuatro mutadores.
    ''' <para>⛔ Con <c>Try</c>, por lo mismo que <c>ArmaEditor_Form.SnapshotCurrentDraft</c>: <c>Clone()</c>
    ''' tiene una precondición que puede tirar, esto corre desde manejadores sin <c>Try</c> y la app usa
    ''' <c>UnhandledExceptionMode.ThrowException</c> — un throw acá CIERRA la app. La consecuencia de no
    ''' tener snapshot se declara: esa lista no se revierte al cancelar. Es peor que revertirla y mejor que
    ''' cerrar la aplicación con el trabajo del usuario adentro.</para></summary>
    Private Sub SnapshotAntesDeMutar(d As LeveledListDraft)
        If d Is Nothing Then Return
        ' La registramos nosotros ⇒ en Cancel se baja ENTERA; no hay estado previo al que volver.
        If _lvliRegistradasPorMi.Contains(d.FormID) Then Return
        If _lvliSnapshotDeApertura.ContainsKey(d.FormID) Then Return
        Try
            _lvliSnapshotDeApertura(d.FormID) = d.Clone()
        Catch ex As Exception
            Logger.Log(ex.ToString())
        End Try
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
            ' Estado de apertura ANTES de tocar el árbol: si el diálogo se cancela, vuelve a éste.
            SnapshotAntesDeMutar(d)
            Dim en = d.Record.AgregarLeveledListEntries()
            If en Is Nothing Then Return
            en.LeveledListEntryItem = itemFid
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
            d.IsModified = True
        End Using
        ' ⛔ QUIEN MUTA, PUBLICA: las lecturas del sorteo van a la FOTO, no al árbol vivo.
        _mainForm.PublicarBorradorDeLista(d)
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
            ' Estado de apertura ANTES de tocar el árbol: si el diálogo se cancela, vuelve a éste.
            SnapshotAntesDeMutar(d)
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
            d.IsModified = True
        End Using
        ' ⛔ QUIEN MUTA, PUBLICA: las lecturas del sorteo van a la FOTO, no al árbol vivo.
        _mainForm.PublicarBorradorDeLista(d)
        _lvlDirtyResample = True
        RefreshPieces()
    End Sub

    ''' <summary>Remove the selected nested entry from the current leveled draft.</summary>
    Private Sub RemoveSelectedEntry()
        Dim d = CurrentLevelDraft()
        If d Is Nothing Then Return
        Dim p = SelectedPieceEntry()
        If p Is Nothing OrElse p.SourceEntry Is Nothing Then Return
        ' Estado de apertura ANTES de tocar el árbol: si el diálogo se cancela, vuelve a éste.
        SnapshotAntesDeMutar(d)
        RemoveEntry(d.Record, p.SourceEntry)
        d.IsModified = True
        ' ⛔ QUIEN MUTA, PUBLICA: las lecturas del sorteo van a la FOTO, no al árbol vivo.
        _mainForm.PublicarBorradorDeLista(d)
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
            ' Se pregunta ANTES de construir: BuildLeveledOverrideDraftFromReal devuelve el que ya estaba
            ' cuando existe, y ése no lo registramos nosotros — no nos toca bajarlo al cancelar.
            Dim yaEstaba = _mainForm.TryGetLeveledListDraft(dlg.SelectedFormID) IsNot Nothing
            Dim d = _mainForm.BuildLeveledOverrideDraftFromReal(dlg.SelectedFormID)
            If d IsNot Nothing AndAlso Not yaEstaba Then _lvliRegistradasPorMi.Add(d.FormID)
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
    ''' editors overwrite for their OWN single-item preview) and from the ARMA editor's ARMO wrapper sentinel.
    ''' <para><c>Shared ReadOnly</c> y no <c>Const</c>: se compone desde
    ''' <see cref="Borradores.FormIdAltoDeBorrador"/>, que es un campo — ver la nota de allá.</para></summary>
    Private Shared ReadOnly OutfitContextFormID As UInteger = Borradores.FormIdAltoDeBorrador Or &H7FDUI
    Private _outfitContextRegistered As Boolean

    ''' <summary>Las unidades de equip de las piezas armadas, para la LEY ÚNICA
    ''' (<see cref="EquipResolver"/>). La unidad del motor es UN ARMO, así que una pieza leveled aporta una
    ''' unidad POR TERMINAL de su realización — igual que el render, que compite por ARMO terminal. El
    ''' <c>Tag</c> de cada unidad es su <see cref="PieceEntry"/>, para mapear el veredicto de vuelta a la
    ''' fila. Antes esta pestaña calculaba su propia máscara de conflicto (la unión de las ARMA de la
    ''' realización), que es lo que tachaba piezas que el render sí dibuja.</summary>
    Private Function BuildEquipUnits(Optional memo As Dictionary(Of String, EmisionArmo) = Nothing,
                                     Optional ctxDeLaPasada As Func(Of UInteger, List(Of UInteger)) = Nothing) As List(Of EquipResolver.EquipItem)
        ' ⛔ UN EquipItem POR ARMO, no por fila - es EXACTAMENTE lo que hace el render: agrupa sus
        ' candidates por `SourceFormID` (el ARMO dueno), arma un `EquipItem` por grupo con el `Order`
        ' MINIMO del grupo, y recien despues expande el ganador. Aca habia uno por fila-terminal, asi que
        ' dos filas que resuelven al MISMO ARMO -una pieza directa y una LVLI que cae en ella, o dos LVLI
        ' que caen en la misma- metian dos unidades con la misma identidad: el torneo las hacia competir
        ' ENTRE SI, la ultima ganaba y la primera salia "eliminated" mientras el render la dibujaba.
        ' Esa segunda copia no existe en el motor: el ARMO se equipa UNA vez.
        Dim conTerminales As New List(Of (Fid As UInteger, Ctx As List(Of UInteger), Fila As Object))
        ' ⛔ El mapa de la pasada lo trae el llamador cuando ya lo armó: el repintado lo construía y
        ' `BuildEquipUnits` lo volvía a construir, así que se recorrían las piezas DOS veces por
        ' repintado para obtener exactamente lo mismo. Sin llamador que lo pase, se arma acá.
        Dim ctxPasada = If(ctxDeLaPasada, ContextoDePasada())
        For Each p In _pieces.OrderBy(Function(x) x.Order)
            For Each t In PieceTerminals(p)
                Dim fid = t.Fid
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
                ' NO SE FILTRA ACA. El filtro `Compite` corria POR APARICION y el agrupamiento venia
                ' despues, asi que "gana la primera" se evaluaba sobre una lista YA PODADA: si la primera
                ' aparicion no competia, la unidad se armaba con el contexto de la SEGUNDA y la fila
                ' marcaba conflictos que el render no tiene. PRIMERO LA IDENTIDAD, DESPUES LA CONSULTA:
                ' el gate de competencia vive adentro de `AgruparPorArmo`, que es donde vive la
                ' identidad. Separables es justo como se rompio.
                conTerminales.Add((fid, ctxPasada(fid), CType(p, Object)))
            Next
        Next
        Return AgruparPorArmo(conTerminales,
                              Function(f, ctx)
                                  Dim e = EmisionDe(f, ctx, memo, ctxPasada)
                                  Return (e.Compite, e.EquipMask, e.GeometryMask, e.OcclusionMask)
                              End Function)
    End Function

    ''' <summary>El contexto OBTS EFECTIVO de cada ARMO en esta pasada: el de su PRIMERA aparicion
    ''' recorriendo las piezas en orden de equip.
    '''
    ''' <para>⛔ Es el espejo exacto de <c>MainForm.ProyeccionesDelBorrador</c>, que recorre
    ''' <c>draft.Prendas()</c> y se queda con la primera (<c>If mapa.ContainsKey(...) Then Continue For</c>)
    ''' SIN preguntar si esa aparicion compite ni si dibuja. El render resuelve cada ARMO UNA vez con ese
    ''' contexto —<c>NpcMeshCollector</c> recorre <c>state.LoadoutArmorFormIDs</c>, ya deduplicado con la
    ''' misma regla— asi que preguntar con el contexto de OTRA aparicion es preguntar por algo que el
    ''' motor no va a dibujar.</para>
    '''
    ''' <para>⛔ Las CLAVES son TERMINALES (y las piezas concretas, que son su propio terminal): el
    ''' FormID de una lista por nivel NO es una clave, porque una lista no tiene contexto propio — lo
    ''' tienen los ARMO que sortea. Por eso quien pregunta por una lista recorre sus terminales y
    ''' consulta cada uno por separado.</para>
    ''' <para>⛔ Es GLOBAL a la pasada, no por fila: si dos filas comparten el ARMO, las DOS se marcan
    ''' con la emision del contexto de la primera aparicion. Derivado UNA vez y consumido por los dos
    ''' carriles —el de <c>Compite</c> (el torneo) y el de <c>Dibuja</c> (la marca "no aplica")— para que
    ''' no puedan volver a divergir entre si.</para></summary>
    Private Function ContextoDePasada() As Func(Of UInteger, List(Of UInteger))
        Dim mapa As New Dictionary(Of UInteger, List(Of UInteger))
        For Each p In _pieces.OrderBy(Function(x) x.Order)
            For Each t In PieceTerminals(p)
                If t.Fid = 0UI OrElse mapa.ContainsKey(t.Fid) Then Continue For
                mapa(t.Fid) = t.Ctx
            Next
        Next
        ' ⛔ NUNCA devuelve Nothing. Adentro del selector el camino `ctx = Nothing` esta PROHIBIDO: un ARMO
        ' que no aparece en el mapa es una pieza que no paso por ningun LLKC, y su contexto real es la
        ' LISTA VACIA -que es exactamente lo que el borrador le publica en `ResolveDraftPicks`-. Con
        ' Nothing la consulta caia al contexto del ESTADO, o sea al del ultimo render del NPC, que es de
        ' otro atuendo. `Nothing` sobrevive solo para llamadores de AFUERA (el gate de la cache), donde
        ' preguntar "lo que diga el estado" si es la pregunta.
        Return Function(f)
                   Dim ctx As List(Of UInteger) = Nothing
                   If mapa.TryGetValue(f, ctx) AndAlso ctx IsNot Nothing Then Return ctx
                   Return VACIO
               End Function
    End Function

    ''' <summary>El contexto de una prenda que no paso por ningun LLKC. Compartido y de solo lectura por
    ''' contrato: nadie le escribe. Existe para que "sin keywords" sea UN objeto y no una asignacion
    ''' nueva por consulta.</summary>
    Private Shared ReadOnly VACIO As New List(Of UInteger)

    ''' <summary>EL agrupamiento del torneo del selector: UN <see cref="EquipResolver.EquipItem"/> por
    ''' ARMO, con el orden MÍNIMO de las filas que lo aportan y las máscaras que da el COLECTOR.
    '''
    ''' <para>⛔ Es EXACTAMENTE lo que hace el render: agrupa sus candidates por <c>SourceFormID</c> (el
    ''' ARMO dueño), arma un <c>EquipItem</c> por grupo con el <c>Order</c> mínimo, y recién después
    ''' expande el ganador. Acá había una unidad POR FILA, así que dos filas que resuelven al MISMO ARMO
    ''' —una pieza directa y una LVLI que cae en ella, o dos LVLI que caen en la misma— metían dos
    ''' unidades con la MISMA identidad: el torneo las hacía competir ENTRE SÍ, la última ganaba y la
    ''' primera salía «✗ eliminated» mientras el render la dibujaba. Esa segunda copia no existe en el
    ''' motor: el ARMO se equipa UNA vez.</para>
    '''
    ''' <para>⛔ Las máscaras las da <paramref name="emision"/> —el colector— y no
    ''' <c>EquipResolver.BuildFootprint</c>. <c>BuildFootprint(addonFormIDs:=Nothing)</c> une TODOS los
    ''' Models; el render compite con lo que EMITIÓ: el grupo INDX que resolvió OBTS, ya pasado por el
    ''' dedup intra-ARMO <c>coveredSlots</c>. Medido sobre FO4: 10 de 1.067 ARMO donde <c>coveredSlots</c>
    ''' pierde bits de la unión, o sea que la fila podía tachar una prenda que el render dibuja.</para>
    '''
    ''' <para>⛔ <c>Friend Shared</c> y PURA, y recibe el tag como <c>Object</c>, por lo mismo que
    ''' <see cref="VolcarPiezasEn"/>: para que el gate la pueda CORRER. Con el agrupamiento adentro de un
    ''' <c>Private Function</c> de un formulario el caso sólo puede comparar textos, y un caso de texto
    ''' deja reponer el defecto cambiando la forma de escribirlo.</para>
    ''' <para>El <c>Tag</c> de cada unidad es la LISTA de filas que la aportan, para que el veredicto se
    ''' pueda mapear de vuelta a TODAS.</para></summary>
    Friend Shared Function AgruparPorArmo(
            conTerminales As IEnumerable(Of (Fid As UInteger, Ctx As List(Of UInteger), Fila As Object)),
            emision As Func(Of UInteger, List(Of UInteger), (Compite As Boolean, EquipMask As UInteger, GeometryMask As UInteger, OcclusionMask As UInteger))) _
            As List(Of EquipResolver.EquipItem)
        Dim units As New List(Of EquipResolver.EquipItem)
        If conTerminales Is Nothing Then Return units
        ' ⛔ `porArmo` guarda la unidad o Nothing: el ARMO se registra APENAS SE LO VE, compita o no. Si
        ' solo se registraran los que compiten, una aparicion posterior del MISMO ARMO volveria a
        ' consultar y crearia la unidad — que es el defecto de orden por la puerta de atras.
        ' ⛔ El `orden` cuenta apariciones CRUDAS, asi que sus valores absolutos cambiaron al sacar el
        ' filtro de afuera. No importa: `EquipResolver.Resolve` usa el orden RELATIVO (last-equipped-
        ' wins) y las no-competidoras no producen unidad, asi que el orden relativo de los
        ' supervivientes es identico. Queda escrito porque es la parte que se rompe sin ruido.
        Dim porArmo As New Dictionary(Of UInteger, EquipResolver.EquipItem)
        Dim orden As Integer = 0
        For Each t In conTerminales
            If t.Fid = 0UI Then Continue For
            orden += 1
            Dim ya As EquipResolver.EquipItem = Nothing
            If porArmo.TryGetValue(t.Fid, ya) Then
                ' Ya está: sólo se suma la fila. El orden se queda con el del PRIMERO, que es el mínimo
                ' porque las filas vienen en orden — igual que el `g.Min(c.Order)` del render.
                ' ⛔ Y EL CONTEXTO TAMBIÉN es el de la primera: GANA LA PRIMERA APARICIÓN. No es una regla
                ' nueva — es la del borrador: `MainForm.ProyeccionesDelBorrador` hace
                ' `If mapa.ContainsKey(pick.ArmoFormID) Then Continue For`. Si acá se eligiera otra, la
                ' fila calcularía el veredicto con una variante OBTS distinta de la que se dibuja.
                ' `ya` en Nothing = ARMO ya visto que NO compite: se lo saltea sin volver a consultar.
                If ya IsNot Nothing Then DirectCast(ya.Tag, List(Of Object)).Add(t.Fila)
                Continue For
            End If
            Dim m = If(emision Is Nothing, (Compite:=False, EquipMask:=0UI, GeometryMask:=0UI, OcclusionMask:=0UI), emision(t.Fid, t.Ctx))
            ' ⛔ EL GATE DE COMPETENCIA VA ACA, DESPUES de decidir la identidad. Un ARMO cuya PRIMERA
            ' aparicion no emite candidates slotted no genera grupo en el render, asi que no genera
            ' unidad ni ocupa slot -aunque una aparicion POSTERIOR si hubiera competido: el motor
            ' resuelve el ARMO una sola vez, con el contexto de la primera-. Filtrar antes de agrupar
            ' dejaba que la segunda armara la unidad.
            If Not m.Compite Then
                porArmo(t.Fid) = Nothing
                Continue For
            End If
            Dim u As New EquipResolver.EquipItem With {
                .ArmoFormID = t.Fid,
                .Order = orden,
                .EquipMask = m.EquipMask,
                .GeometryMask = m.GeometryMask,
                .OcclusionMask = m.OcclusionMask,
                .Tag = New List(Of Object) From {t.Fila}}
            porArmo(t.Fid) = u
            units.Add(u)
        Next
        Return units
    End Function

    ''' <summary>Los ARMO que una pieza pone sobre el actor: una pieza concreta es ella misma; una leveled,
    ''' los terminales de su realización actual (vacía si el sorteo no dio nada — ChanceNone).</summary>
    Private Function PieceTerminals(p As PieceEntry) As IEnumerable(Of (Fid As UInteger, Ctx As List(Of UInteger)))
        If p Is Nothing Then Return Array.Empty(Of (Fid As UInteger, Ctx As List(Of UInteger)))()
        If p.IsLeveled Then
            ' ⛔ NO SE APLANA. El pick trae las keywords que el terminal heredo del encadenado de LLKC, y
            ' ⛔ son las que deciden que combinacion OBTS aplica. Aplanarlo a `pk.ArmoFormID` las tiraba, asi
            ' ⛔ que la fila calculaba OTRA variante -y OTRA mascara- que el preview, que SI las publica por
            ' ⛔ `ProyeccionesDelBorrador`. No son especulativas: las trajo el sorteo REAL.
            Return If(p.Realization, New List(Of OutfitArmorPick)()).
                   Select(Function(pk) (pk.ArmoFormID, New List(Of UInteger)(pk.ContextKeywords)))
        End If
        ' ⛔ Una prenda concreta NO paso por ningun LLKC: su contexto es vacio DE VERDAD, no por omision.
        ' Es la misma lista vacia que le da el borrador en `ResolveDraftPicks`.
        ' ⛔ Y su identidad es el TERMINAL, igual que la de un pick. La ley estaba aplicada A MEDIAS: la
        ' pieza por nivel salia resuelta y la DIRECTA salia cruda, asi que el atuendo trataba al hijo y a
        ' su terminal como DOS armaduras -torneo, contador, marcas, contexto OBTS y preview- y al guardar
        ' colapsaban en una. Lo alcanza un atuendo con una ARMO-con-plantilla directa MAS una lista que
        ' termina en la misma cadena. El INAM sigue guardando el hijo tal como se autoro; lo que resuelve
        ' el terminal es la CONSULTA. Ver `OutfitDraft.PicksSellados`.
        Return New(Fid As UInteger, Ctx As List(Of UInteger))() {(TerminalMemo(p.FormID), New List(Of UInteger))}
    End Function

    ''' <summary>El terminal de un ARMO, memoizado POR DIÁLOGO. Devuelve el propio FormID cuando la cadena
    ''' no resuelve.
    ''' <para>⛔ NO es un caché nuevo de la app: es la memoización de una función PURA del orden de carga
    ''' —<c>OutfitResolver.ResolveTerminalArmorFormID</c> recorre TNAM— cuya respuesta no depende de nada
    ''' que este diálogo pueda mover. <see cref="PieceTerminals"/> se llama hasta cuatro veces por
    ''' repintado (el contexto de la pasada, el torneo, la marca y el conteo) y ahora cada una resolvía la
    ''' cadena entera de cada pieza directa.</para>
    ''' <para>⛔ Se VACÍA cuando cambian los borradores de ARMO, porque la cadena puede pasar por uno: lo
    ''' hace <see cref="RefreshCreateAfterArmorEdit"/>, que es el único camino por el que este diálogo ve
    ''' un ARMO editado o creado. Sin eso sería una foto vieja de la herencia.</para></summary>
    Private ReadOnly _terminalMemo As New Dictionary(Of UInteger, UInteger)

    Private Function TerminalMemo(armoFid As UInteger) As UInteger
        If armoFid = 0UI Then Return 0UI
        Dim ya As UInteger
        If _terminalMemo.TryGetValue(armoFid, ya) Then Return ya
        Dim t = _mainForm.TerminalDeArmoConBorradores(armoFid)
        If t = 0UI Then t = armoFid
        _terminalMemo(armoFid) = t
        Return t
    End Function

    ''' <summary>Veredicto por pieza a partir del veredicto por unidad: cuántas de sus unidades ganaron y
    ''' cuántas cayeron. Sin unidades = la lista no sorteó nada.</summary>
    Friend Shared Function VeredictosPorFila(res As EquipResolver.EquipResolution) _
            As Dictionary(Of Object, (Won As Integer, Lost As Integer, MutexMask As UInteger))
        ' ⛔ El `Tag` es la LISTA de filas que aportan ese ARMO, y el veredicto se propaga a TODAS: el
        ' ARMO se dibuja o no se dibuja, y dos filas que lo aportan son dos CAMINOS a la misma prenda, no
        ' dos prendas. Marcar "eliminated" en una porque gano por la otra afirma sobre una copia que el
        ' motor no tiene - que es el defecto que este agrupamiento vino a cerrar.
        Dim d As New Dictionary(Of Object, (Won As Integer, Lost As Integer, MutexMask As UInteger))
        Dim sumar =
            Sub(it As EquipResolver.EquipItem, gano As Boolean)
                ' ⛔ TIRA, no vuelve mudo. El `Tag` lo escribe `AgruparPorArmo` y SIEMPRE es la lista de
                ' filas: un tipo distinto es un cambio de contrato que el compilador no ve —exactamente
                ' la trampa del `TryCast` re-tipado que dejó el mapa de máscaras vacío—. Volver callado
                ' devolvía un mapa incompleto y el consumidor pintaba mal sin que nada avisara.
                Dim filas = TryCast(it.Tag, List(Of Object))
                If filas Is Nothing Then
                    Throw New InvalidOperationException(
                        "El Tag de la unidad de equip no es la lista de filas que `AgruparPorArmo` escribe. " &
                        "Es un cambio de contrato: el veredicto y la máscara saldrían incompletos y nadie " &
                        "se enteraría.")
                End If
                Dim mascara = EquipResolver.MutexMaskOf(it)
                For Each o In filas
                    If o Is Nothing Then Continue For
                    Dim cur = If(d.ContainsKey(o), d(o), (Won:=0, Lost:=0, MutexMask:=0UI))
                    d(o) = (Won:=cur.Won + If(gano, 1, 0),
                            Lost:=cur.Lost + If(gano, 0, 1),
                            MutexMask:=cur.MutexMask Or mascara)
                Next
            End Sub
        For Each it In res.Winners
            sumar(it, True)
        Next
        For Each it In res.Losers
            sumar(it, False)
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
    ''' <para>⛔ Las realizaciones se escriben en la MISMA pasada que el INAM, no en un carril aparte:
    ''' <see cref="OutfitDraft.ReemplazarPiezas"/> recorre una vez y aplica la regla del cero una vez.
    ''' Así una realización no puede sobrevivir a la prenda que ya no está —el carril paralelo que se
    ''' desincroniza— ni quedarse en el índice de otra.</para></summary>
    Friend Shared Sub VolcarPiezasEn(d As OutfitDraft,
                                     piezas As IEnumerable(Of (Fid As UInteger, EsLeveled As Boolean,
                                                               Picks As List(Of OutfitArmorPick))))
        If d Is Nothing Then Return
        ' ⛔ Sin ternario: `If(piezas, New List(...))` mezcla `IEnumerable(Of T)` con `List(Of T)` y el
        ' resultado no tiene tipo común. Con Option Strict apagado eso compila y revienta en ejecución.
        Dim lista As New List(Of (Fid As UInteger, EsLeveled As Boolean, Picks As List(Of OutfitArmorPick)))
        If piezas IsNot Nothing Then lista.AddRange(piezas)
        ' ⛔ UNA sola llamada, y por eso una sola pasada: `ReemplazarPiezas` escribe el INAM y las
        ' realizaciones juntos, aplicando la regla del cero una vez. Antes eran dos recorridos -el INAM
        ' por un lado y un diccionario por FormID por el otro- y ahi vivian DOS defectos: el 0 que
        ' desalinea, y la lista por nivel repetida que colapsaba en una sola realizacion.
        d.ReemplazarPiezas(lista.Select(Function(p) (p.Fid, If(p.EsLeveled, p.Picks, Nothing))))
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
    ''' ya lo impide <see cref="OutfitDraft.Realizaciones"/> — `ResolveDraftPicks` sólo muestrea donde
    ''' está en Nothing, y el re-sorteo
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
        Dim prendas = d.Prendas()
        For i = 0 To prendas.Count - 1
            Dim fid = prendas(i)
            ' Por INDICE: la misma lista por nivel dos veces son dos sorteos, y la llave tiene que
            ' distinguirlos o el preview se saltea el repintado cuando cambia solo el segundo.
            Dim picks = d.RealizacionEn(i)
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
        ' ⛔ ES UN CAMBIO DE CONDUCTA, NO UNA EQUIVALENCIA — el comentario decía «equivalente a la vieja» y
        ' era falso. La vieja preguntaba «¿hay al menos UN GANADOR del torneo?»; ésta pregunta «¿algún
        ' terminal DIBUJA?», y es estrictamente MÁS PERMISIVA: un atuendo entero de chunk-mounts dibuja y
        ' no compite —salen con `SlotMask = 0` y el render los pinta en su pasada slotless, sin entrar al
        ' torneo (`NpcMeshCollector.ApplyEquipSlotResolution`, que arma el torneo sólo con
        ' `slottedCandidates`)—, así que antes daba ctx=0 y ahora da ctx≠0.
        ' ⛔ Y LA NUEVA ES LA CORRECTA, con el criterio del render: el contexto existe para que los
        ' editores de ARMA/ARMO muestren «Full Outfit» — o sea LO QUE SE DIBUJA. Con la guarda vieja, un
        ' atuendo que el render SÍ pinta les llegaba como «no hay atuendo». «Gana el slot» es otra
        ' pregunta y no es ésta.
        ' Y NO se pregunta `EmisionDe(p.FormID)` sobre una LVLI: eso recursa por TODOS sus terminales
        ' posibles, no por los sorteados.
        Dim memoCtx As New Dictionary(Of String, EmisionArmo)
        ' ⛔ Con el contexto de la PRIMERA aparicion global, no con el de cada aparicion: el render dibuja
        ' el ARMO una sola vez y con aquel. Ver `ContextoDePasada`.
        Dim ctxP = ContextoDePasada()
        If Not _pieces.Any(Function(p) PieceTerminals(p).Any(Function(t) EmisionDe(t.Fid, ctxP(t.Fid), memoCtx, ctxP).Dibuja)) Then Return 0UI
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
        ' ⛔ La memoización del terminal se VACÍA acá: un ARMO editado o creado puede haber cambiado su
        ' TNAM, y con la memo viva la identidad de la pieza se quedaría con la cadena vieja. Éste es el
        ' único camino por el que este diálogo ve un ARMO tocado — lo llaman los tres retornos del editor.
        _terminalMemo.Clear()
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
            Dim lvl = esLeveled IsNot Nothing AndAlso esLeveled(fid)
            ' ⛔ UNA SOLA LEY PARA LA MARCA, y por eso la lista por nivel NO se marca acá. La marca
            ' contesta «¿lo que esta pieza le pone al actor se VE?», y lo que una lista le pone es su
            ' realización SORTEADA — que es lo que el render dibuja (`ProyeccionesDelBorrador` publica los
            ' picks sellados) y lo que el repintado consulta. En este punto ese sorteo TODAVÍA NO EXISTE:
            ' `SembrarInam` lo hace unas líneas más abajo. Preguntar por la lista ENTERA
            ' (`EnumerateLeveledTerminalsAll`, todos sus terminales POSIBLES) es OTRA pregunta, y contestarla
            ' acá hacía que la misma fila entrara sin marca y quedara marcada al primer repintado, o que
            ' cambiara de marca con cada Reroll: la UI parpadeaba porque había dos leyes.
            ' Sin sorteo la respuesta correcta es NO SÉ, y «no sé» se pinta sin marca: el repintado que
            ' sigue —siempre hay uno, `SembrarInam` termina en `RefreshPieces`— pone la definitiva. La
            ' pieza CONCRETA sí se contesta acá, y con el mismo sujeto que el repintado (su terminal).
            salida.Add((fid,
                        Not lvl AndAlso Not (aplica IsNot Nothing AndAlso aplica(fid)),
                        lvl,
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

    Private Function EmisionDe(fid As UInteger, Optional ctx As List(Of UInteger) = Nothing,
                               Optional memo As Dictionary(Of String, EmisionArmo) = Nothing,
                               Optional ctxDe As Func(Of UInteger, List(Of UInteger)) = Nothing) As EmisionArmo
        ' Nunca Nothing: los llamadores encadenan `.Dibuja` / `.Compite` sobre el resultado. Un cero
        ' contesta las dos preguntas con la verdad —no dibuja, no compite— sin obligar a cada llamador a
        ' su propia guarda de nulo.
        If fid = 0UI Then Return New EmisionArmo()
        ' ⛔ La clave del memo LLEVA EL CONTEXTO. Con la clave puesta solo en el FormID, la primera fila
        ' ⛔ que preguntara por un ARMO le fijaba la respuesta a TODAS -incluida una que lo aporta con
        ' ⛔ otras keywords y por lo tanto con otra combinacion OBTS-, y el memo mentia entre filas.
        ' ⛔ Las keywords van ORDENADAS por la misma razon que en `LlaveDelBorrador`: el contexto es un
        ' ⛔ CONJUNTO, y sin ordenar dos caminos equivalentes darian dos claves y dos consultas.
        Dim clave = ClaveDeEmision(fid, ctx)
        Dim cacheada As EmisionArmo = Nothing
        If memo IsNot Nothing AndAlso memo.TryGetValue(clave, cacheada) Then Return cacheada
        Dim r As EmisionArmo
        If _mainForm.IsLeveledItem(fid) Then
            ' ⛔ Una LVLI agrega SOLO los dos booleanos. Las máscaras quedan en 0 a propósito: la unidad
            ' del torneo es UN ARMO, así que la unión de las máscaras de varios terminales no es la
            ' máscara de nada — `BuildEquipUnits` pregunta por TERMINAL, nunca por la lista.
            ' ⛔ CADA TERMINAL CON **SU** CONTEXTO, no con el del padre. `ContextoDePasada` indexa
            ' TERMINALES —el fid de la lista NO es una clave—, así que preguntar por la lista con "su"
            ' contexto daba VACÍO y encima ese vacío se propagaba a todos sus terminales: la marca de una
            ' pieza por nivel se calculaba con contexto vacío mientras el torneo usaba el del pick. Con el
            ' mapa de la pasada, cada terminal contesta con lo MISMO que usa el torneo.
            Dim dib = False, comp = False
            For Each term In _mainForm.EnumerateLeveledTerminalsAll(fid)
                Dim ctxTerm = If(ctxDe Is Nothing, ctx, ctxDe(term))
                Dim e = EmisionDe(term, ctxTerm, memo, ctxDe)
                dib = dib OrElse e.Dibuja
                comp = comp OrElse e.Compite
                If dib AndAlso comp Then Exit For
            Next
            r = New EmisionArmo With {.Dibuja = dib, .Compite = comp}
        Else
            ' ⛔ SOBRE EL FormID QUE LE DAN, y no resuelve herencia: son DOS cosas distintas y confundirlas
            ' fue el nudo. Los DATOS heredados los resuelve el colector con la vista EFECTIVA, en un solo
            ' lugar, y repetirlo aca seria la segunda copia de la ley. La IDENTIDAD —quien es este ARMO
            ' para el torneo, el conteo y las marcas— la decide el LLAMADOR, y desde F1 todos los
            ' llamadores mandan el TERMINAL por la misma puerta (`PieceTerminals` →
            ' `MainForm.TerminalDeArmoConBorradores`). Esta funcion no opina sobre ninguna de las dos.
            Dim c = _mainForm.EmisionDeArmo(fid, _visualState, ctx)
            r = New EmisionArmo With {.Dibuja = c.Dibuja, .Compite = c.Compite, .EquipMask = c.EquipMask,
                                      .GeometryMask = c.GeometryMask, .OcclusionMask = c.OcclusionMask}
        End If
        If memo IsNot Nothing Then memo(clave) = r
        Return r
    End Function

    ''' <summary>La clave del memo de <see cref="EmisionDe"/>: el ARMO MAS su contexto OBTS, con las
    ''' keywords ORDENADAS. Misma razon que en <see cref="LlaveDelBorrador"/>: el contexto es un CONJUNTO,
    ''' asi que dos recorridos del LLKC que traen las mismas keywords en otro orden son el MISMO
    ''' contexto y tienen que compartir la respuesta.
    ''' <para>⛔ Friend y no Private, por lo mismo que VolcarPiezasEn: para que el gate la pueda CORRER.
    ''' La clave del memo es una LEY -que dos contextos distintos no compartan respuesta- y medirla por
    ''' el texto deja reponer el defecto cambiando como se escribe.</para></summary>
    Friend Shared Function ClaveDeEmision(fid As UInteger, ctx As List(Of UInteger)) As String
        ' ⛔ Nothing y lista VACIA son DOS consultas distintas y no pueden compartir clave: con Nothing el
        ' colector resuelve con el contexto que traiga el ESTADO, y con la lista vacia clona y resuelve
        ' con contexto vacio. Pueden dar respuestas distintas, asi que colapsarlas hacia que la primera
        ' le fijara la respuesta a la segunda.
        If ctx Is Nothing Then Return fid.ToString("X8") & "|estado"
        If ctx.Count = 0 Then Return fid.ToString("X8") & "|vacio"
        Return fid.ToString("X8") & "|" & String.Join(",", ctx.OrderBy(Function(k) k).Select(Function(k) k.ToString("X8")))
    End Function

    ''' <summary>La respuesta del colector para UN ARMO: las dos preguntas y las TRES máscaras del
    ''' torneo. Clase y no tupla porque viaja por el memo y por el torneo, y una tupla de cinco campos
    ''' repetida en cada firma es la clase de declaración que después queda desalineada.</summary>
    Private Class EmisionArmo
        Public Dibuja As Boolean
        Public Compite As Boolean
        ''' <summary>BOD2 crudo del ARMO — con la que el motor decide el mutex.</summary>
        Public EquipMask As UInteger
        ''' <summary>Unión de los BOD2 de las ARMA emitidas (particiones).</summary>
        Public GeometryMask As UInteger
        ''' <summary>Unión de los SlotMask emitidos (ARMA ∪ headwear del ARMO).</summary>
        Public OcclusionMask As UInteger
    End Class

    ''' <summary>¿Esta prenda SE VE en este NPC? Es la mitad <c>Dibuja</c> de <see cref="EmisionDe"/>.
    ''' <para>⛔ «Se ve» y «pelea el slot» son DOS preguntas, y acá se contestaba una sola para las
    ''' dos. Se ve = emitió algún candidate, chunk-mounts incluidos. Pelear el slot es la otra mitad, y la
    ''' usa <see cref="BuildEquipUnits"/>.</para>
    ''' <para>⛔ No se pregunta «está entre los candidatos»: ese universo incluye SIEMPRE a los
    ''' borradores ARMO propios, tengan o no armature, así que uno recién creado entraba sin marcar,
    ''' competía con el BOD2 crudo y eliminaba a la armadura real de la vista previa — que el render sí
    ''' dibujaba, porque ahí el borrador sin ARMA no emite candidato. Con <c>EmisionDe</c> los dos lados dan
    ''' la MISMA respuesta por el mismo código, en vez de dos aproximaciones que coincidían de casualidad.</para>
    ''' <para>⛔ Recibe el contexto y el memo DE LA PASADA. Antes preguntaba sin ninguno de los dos: sin
    ''' contexto resolvía las combinaciones OBTS con las del último render —otro atuendo— y sin memo
    ''' repetía la consulta al colector por cada prenda del INAM. El sembrado y el pintado tienen que
    ''' mirar el MISMO mapa y el MISMO memo o marcan distinto la misma prenda en la misma ventana.</para>
    ''' <para>⛔ Y por la MISMA IDENTIDAD que el repintado: el terminal. Es el carril del SEMBRADO, donde
    ''' todavía no hay <c>PieceEntry</c> ni realización sorteada —se recorre el INAM crudo—, así que la
    ''' puerta se llama directo. Una lista por nivel se consulta COMO LISTA (<see cref="EmisionDe"/>
    ''' recorre sus entradas; antes de sembrar no hay sorteo del que sacar terminales) y una prenda
    ''' concreta por su terminal. Sin esto, la misma prenda directa con plantilla se marcaba con el HIJO al
    ''' sembrar y con el TERMINAL al repintar: dos respuestas en la misma ventana, que es exactamente lo
    ''' que el párrafo de arriba prohíbe.</para></summary>
    Private Function AplicaAEsteNpc(fid As UInteger, ctx As Func(Of UInteger, List(Of UInteger)),
                                    memo As Dictionary(Of String, EmisionArmo)) As Boolean
        Dim sujeto = fid
        If Not _mainForm.IsLeveledItem(fid) Then
            Dim t = _mainForm.TerminalDeArmoConBorradores(fid)
            If t <> 0UI Then sujeto = t
        End If
        Return EmisionDe(sujeto, ctx(sujeto), memo, ctx).Dibuja
    End Function

    ''' <summary>PRECONDICIÓN del sembrado: <c>_pieces</c> tiene que estar LIMPIA.
    ''' <para>⛔ No es un detalle de implementación, es de lo que depende que el contexto salga bien.
    ''' <see cref="SembrarInam"/> asume la grilla vacía — sus dos llamadores hacen <c>_pieces.Clear()</c>
    ''' en la línea anterior—, y sobre esa premisa <c>ContextoDePasada</c> sale vacío y contesta LISTA
    ''' VACIA para toda prenda. Eso es correcto porque al sembrar todavía no existe ninguna realización
    ''' sorteada, así que ninguna prenda del INAM tiene keywords de LLKC y «gana la primera aparición»
    ''' se cumple trivialmente.</para>
    ''' <para>⛔ <b>Un TERCER llamador que siembre con piezas VIVAS cambia la semántica</b>: el mapa
    ''' traería los contextos de las filas viejas y las nuevas se marcarían con ellos. Que lo diga esta
    ''' línea y no un comentario: la premisa que nadie verifica es la que se rompe en silencio.</para></summary>
    Private Sub ExigirGrillaLimpiaParaSembrar()
        If _pieces.Count = 0 Then Return
        Throw New InvalidOperationException(
            $"SembrarInam corrió con {_pieces.Count} pieza(s) ya en la grilla. Asume `_pieces` limpio: " &
            "sobre esa premisa el contexto de toda prenda es la lista vacía (al sembrar no hay ninguna " &
            "realización sorteada). Con piezas vivas, el mapa trae los contextos de las filas VIEJAS y " &
            "las nuevas se marcan con ellos — hay que decidir la ley del contexto para ese caso, no " &
            "dejar que la herede por accidente.")
    End Sub

    ''' <summary>Siembra en la grilla lo que el plan diga. NO decide nada: la decisión entera vive en
    ''' <see cref="PlanDeSembrado"/>, que es <c>Friend Shared</c> y por eso el gate la puede recorrer.
    ''' <para>⛔ Antes la decisión estaba acá, en un <c>Private Sub</c> de un formulario, y el caso del gate
    ''' medía una función auxiliar que nunca había fallado: poner un <c>Continue For</c> sobre las marcadas
    ''' reproducía EL defecto original — borrarle prendas al usuario — y la suite seguía en verde.</para>
    ''' <para>Lo que queda acá es sólo lo que necesita la UI y el resolvedor: el nombre, el plugin y el
    ''' muestreo de la realización. Exige la grilla LIMPIA con
    ''' <see cref="ExigirGrillaLimpiaParaSembrar"/>.</para></summary>
    Private Sub SembrarInam(inam As IEnumerable(Of UInteger),
                            Optional selladas As IReadOnlyList(Of List(Of OutfitArmorPick)) = Nothing)
        ' ⛔ EL BOOTSTRAP DEL SEMBRADO, y es trivial pero hay que decirlo. `ContextoDePasada` recorre
        ' `_pieces`, que en este momento esta VACIA: las piezas son justo lo que se esta por sembrar. El
        ' mapa sale vacio y toda consulta contesta con la LISTA VACIA — y eso es CORRECTO, no un atajo:
        ' al sembrar todavia no hay ninguna realizacion sorteada, asi que ninguna prenda del INAM tiene
        ' keywords de LLKC. La ley "gana la primera aparicion" sobre el INAM crudo se cumple de manera
        ' trivial porque todas las apariciones traen el mismo contexto vacio. Cuando el sorteo ocurre
        ' -abajo, en `SampleLeveledRealization`- el mapa de las pasadas SIGUIENTES ya lo refleja.
        ExigirGrillaLimpiaParaSembrar()
        Dim ctxSiembra = ContextoDePasada()
        ' ⛔ MEMO PROPIO DEL SEMBRADO, Y NO SE COMPARTE CON `RefreshPieces`. DECIDIDO Y MEDIDO — no lo
        ' "optimices" al ver los dos memos, que es justo lo que parece de arriba.
        ' Se evaluó pasarle a la primera pasada de repintado este mismo memo. Medición (`Tools\EmisionCostProbe`,
        ' FO4, 20 prendas, control A/A previo con 4,31 ms de ruido):
        '     EmisionDeArmo FRIO ... 14,372 ms c/u  <- con las caches VACIADAS antes de cada ARMO: cota pesimista
        '     EmisionDeArmo CALIENTE  0,236 ms c/u
        '     pasada completa x3 ... 22,91 / 12,20 / 9,51 ms
        ' O sea que la SEGUNDA pasada sobre las mismas prendas cuesta ~12 ms y no ~287: lo que domina no es
        ' el memo sino las caches de `NpcRenderContext`, que SI persisten entre pasadas. Compartir el memo
        ' bajaria esa pasada a ~20 x 0,236 = ~4,7 ms: un ahorro de ~7,5 ms UNA VEZ por apertura de atuendo,
        ' que es 1,7x el ruido de la medicion.
        ' ⛔ Y se paga caro: entre el sembrado y el primer repintado ocurre el SORTEO
        ' (`SampleLeveledRealization`, mas abajo), que es exactamente lo que cambia el contexto OBTS de las
        ' pasadas siguientes. Un memo que sobreviva al sorteo puede contestar con el contexto VIEJO, y que
        ' el sembrado y el repintado opinen distinto sobre la misma prenda es el defecto que estos memos
        ' vinieron a cerrar. 7,5 ms no compran ese riesgo.
        Dim memoSiembra As New Dictionary(Of String, EmisionArmo)
        ' ⛔ El indice del PLAN es el mismo que el de `selladas`. El plan sólo saltea las prendas en 0, y un
        ' borrador NO puede tener ninguna —ni `ReemplazarPiezas` ni `ReemplazarPrendas` las agregan, por
        ' la cita del formato—, así que sobre `d.Prendas()` el recorrido es 1:1. Se lleva la cuenta acá y
        ' no se confía en `e.Orden`, que es el número que ve el usuario.
        Dim iSellada As Integer = -1
        For Each e In PlanDeSembrado(inam,
                                     Function(f) AplicaAEsteNpc(f, ctxSiembra, memoSiembra),
                                     AddressOf _mainForm.IsLeveledItem)
            iSellada += 1
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
                ' ⛔ SI VIENEN SELLADAS, NO SE RE-SORTEA. Reabrir un borrador para retocarle el nombre le
                ' cambiaba las prendas: el sembrado volvía a muestrear cada lista por nivel y el atuendo
                ' que el usuario había armado salía distinto del que había aceptado. El sorteo es del
                ' PRODUCTOR y ya está sellado en el borrador; acá se LEE.
                Dim yaSellada As List(Of OutfitArmorPick) = Nothing
                If selladas IsNot Nothing AndAlso iSellada >= 0 AndAlso iSellada < selladas.Count Then
                    yaSellada = selladas(iSellada)
                End If
                If yaSellada IsNot Nothing Then
                    ' ⛔ Copia PROFUNDA, como las otras dos puertas: `RemapearPicks` reescribe
                    ' `pk.ArmoFormID` IN SITU, así que compartir el objeto con el borrador hace que
                    ' promover un ARMO propio mueva también la fila —y al revés—.
                    pieza.Realization = OutfitDraft.ClonarPicks(yaSellada)
                    Dim m As UInteger = 0UI
                    For Each pk In yaSellada
                        m = m Or _mainForm.ArmoFootprintFor(pk.ArmoFormID, _raceFormID, _isFemale).OcclusionMask
                    Next
                    pieza.SlotMask = m
                Else
                    Dim r = _mainForm.SampleLeveledRealization(e.Fid, _raceFormID, _isFemale)
                    pieza.Realization = r.Picks
                    pieza.SlotMask = r.SlotMask
                End If
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
            ' La creamos NOSOTROS: se baja si el diálogo cierra sin OK.
            _lvliRegistradasPorMi.Add(d.FormID)
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
            ' Estado de apertura ANTES de tocar el árbol: si el diálogo se cancela, vuelve a éste.
            SnapshotAntesDeMutar(lvl)
            Dim en = lvl.Record.AgregarLeveledListEntries()
            If en Is Nothing Then Return
            en.LeveledListEntryItem = itemFid
            en.LeveledListEntryLevel = dlg.LevelValue
            en.LeveledListEntryCount = dlg.CountValue
            SetEntryChanceNone(en, dlg.ChanceNoneValue)
            lvl.IsModified = True
            ' ⛔ QUIEN MUTA, PUBLICA: ver `MainForm.PublicarBorradorDeLista`.
            _mainForm.PublicarBorradorDeLista(lvl)
        End Using
        ' ⛔ CAMBIÓ EL CONTENIDO DE `target` ⇒ SE RE-SORTEA LO QUE DEPENDE DE `target`, Y NADA MÁS.
        ' Las dos mitades, y las dos fueron defectos (ver `PlanDeReMuestreo`): re-muestrear sólo la FILA
        ' seleccionada dejaba con el sorteo viejo a las otras filas que apuntan a la MISMA lista —el INAM
        ' puede traerla dos veces, el sembrado no deduplica a propósito— y a las listas propias que la
        ' CONTIENEN; y marcar la bandera global re-sorteaba TODAS las piezas por nivel, o sea que agregar una
        ' entrada en A le tiraba de nuevo los dados a una lista B que el usuario ya había rerolleado a gusto.
        ' Acá se sabe QUÉ lista se tocó —estamos en la raíz y `target` es la fila—, así que va dirigido; la
        ' bandera queda para el drill-down, que es donde ese dato no existe.
        ReMuestrearListas(target.FormID)
        RefreshItemCandidates()   ' the LVL's slot footprint in the candidate list may have changed
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

    ''' <summary>El centinela de <see cref="PlanDeReMuestreo"/> para «no sé qué lista cambió»: re-sortea TODAS
    ''' las piezas por nivel. Es 0 porque 0 NO es un FormID —el formato lo usa para «ninguna referencia»— así
    ''' que no puede chocar con una lista real.</summary>
    Friend Const TodasLasListas As UInteger = 0UI

    ''' <summary>QUÉ piezas del atuendo hay que volver a sortear cuando cambió el CONTENIDO de una lista por
    ''' nivel. Devuelve los ÍNDICES dentro de <paramref name="piezas"/>. No toca nada: decide.
    '''
    ''' <para>⛔ La ley es <b>«se re-sortea lo que DEPENDE de la lista editada, y nada más»</b>, y las dos
    ''' mitades son defectos distintos que ya ocurrieron:</para>
    ''' <list type="bullet">
    ''' <item><b>De menos</b>: el INAM puede traer la MISMA lista en dos filas —el sembrado no deduplica, a
    ''' propósito— y una lista propia puede CONTENER a la editada. Re-sortear sólo la fila seleccionada dejaba
    ''' a las otras con el sorteo VIEJO, sin la entrada recién agregada, y <c>CommitCreate</c> las volcaba así:
    ''' se guardaba un atuendo distinto del que el usuario acababa de armar.</item>
    ''' <item><b>De más</b>: re-sortear TODAS las piezas por nivel le pisa al usuario el Reroll que eligió en
    ''' una lista B que no tiene nada que ver con la que editó. El sorteo es una decisión suya; agregar una
    ''' entrada en A no es permiso para volver a tirar los dados de B.</item></list>
    '''
    ''' <para>La dependencia la contesta <paramref name="dependeDe"/> —en la app,
    ''' <c>MainForm.ElSorteoDependeDe</c>, la misma alcanzabilidad que usa el bloqueo de ciclos— y se INYECTA
    ''' para que esta decisión quede pura y el gate la pueda correr. <c>Friend Shared</c> por lo mismo que
    ''' <see cref="PlanDeSembrado"/>: adentro de un manejador de ratón no hay testigo que la ejercite.</para>
    '''
    ''' <para><paramref name="listaEditada"/> = <see cref="TodasLasListas"/> es el caso del DRILL-DOWN, donde
    ''' no se sabe qué lista de la cadena tocó el usuario y «todas» es la única respuesta correcta con ese
    ''' dato — ver <see cref="_lvlDirtyResample"/>. Ahí el predicado NO se consulta.</para></summary>
    Friend Shared Iterator Function PlanDeReMuestreo(
            piezas As IEnumerable(Of (Fid As UInteger, EsLeveled As Boolean)),
            listaEditada As UInteger,
            dependeDe As Func(Of UInteger, UInteger, Boolean)) As IEnumerable(Of Integer)
        ' ⛔ Sin predicado NO se adivina. Un `If dependeDe Is Nothing Then` que cayera a «todas» convierte un
        ' cableado roto en el defecto de re-sortear de más, callado y con el gate en verde.
        If listaEditada <> TodasLasListas AndAlso dependeDe Is Nothing Then
            Throw New ArgumentNullException(NameOf(dependeDe),
                "PlanDeReMuestreo necesita el predicado de dependencia para decidir sobre una lista concreta.")
        End If
        If piezas Is Nothing Then Return
        Dim i As Integer = -1
        For Each p In piezas
            i += 1
            If Not p.EsLeveled Then Continue For          ' un ARMO concreto no tiene sorteo que rehacer
            If listaEditada = TodasLasListas OrElse dependeDe(p.Fid, listaEditada) Then Yield i
        Next
    End Function

    ''' <summary>Aplica <see cref="PlanDeReMuestreo"/> sobre <c>_pieces</c>: vuelve a sortear las realizaciones
    ''' que dependen de <paramref name="listaEditada"/> y deja el preview pidiendo redibujo.
    ''' <para>⛔ UNA sola puerta para los tres gestos que invalidan un sorteo (la edición de raíz, la vuelta del
    ''' drill-down y el commit): con tres bucles copiados, el día que la ley cambie se arregla en uno.</para></summary>
    Private Sub ReMuestrearListas(listaEditada As UInteger)
        Dim tuplas = _pieces.Select(Function(p) (Fid:=p.FormID, EsLeveled:=p.IsLeveled)).ToList()
        For Each i In PlanDeReMuestreo(tuplas, listaEditada, AddressOf _mainForm.ElSorteoDependeDe)
            Dim p = _pieces(i)
            Dim rr = _mainForm.SampleLeveledRealization(p.FormID, _raceFormID, _isFemale)
            p.Realization = rr.Picks
            p.SlotMask = rr.SlotMask
        Next
        _lastPreviewKey = Nothing
    End Sub

    ''' <summary>Run the shared slot-conflict resolver over the assembled pieces, repaint the list
    ''' (winners / eliminated, losers greyed), preview the resolved (winner) set, and update the
    ''' status line. Losers stay visible so the user sees what got eliminated and can remove a winner
    ''' to promote a loser; only winners are saved into the outfit (the resolved, conflict-free set).
    ''' <para>Repinta la lista de piezas: marca, torneo, veredictos, máscaras y conteo.</para>
    ''' <para>COSTO MEDIDO, no argumentado — <c>Tools\EmisionCostProbe</c>, FO4, 71 plugins, Debug x64,
    ''' raza 000E8D09 (205 de 400 ARMO de la muestra le emiten): un repintado de <b>20 prendas cuesta
    ''' 7,7-15,6 ms</b> (media 10,3) en el hilo de UI con las cachés calientes, que es el estado de la
    ''' ventana abierta. Control A/A previo: ruido 0,62 ms, o sea que el número se puede afirmar. Es
    ''' BARATO para un gesto de clic y por eso no se optimizó nada acá.</para>
    ''' <para>Lo que NO es barato es la PRIMERA consulta de cada ARMO: 11,1 ms de media y 18,7 ms la
    ''' peor, una sola vez por ARMO y por sesión. Ese tramo es el que las cachés de
    ''' <c>NpcRenderContext</c> ya atacaron (ver <c>Tools\SocketsCacheGate</c>); el probe lo mide con las
    ''' cachés vaciadas antes de CADA ARMO, así que 11,1 ms es una COTA PESIMISTA — en la app las cachés
    ''' persisten entre prendas.</para>
    ''' <para>El memo por pasada es lo que separa los dos números: la misma consulta caliente sale 0,172
    ''' ms. Un repintado toca cada terminal cuatro veces (contexto, torneo, marca, conteo) y las cuatro
    ''' comparten respuesta.</para></summary>
    Private Async Sub RefreshPieces()
        ' Drilled into a leveled list → render THAT list's entries instead of the outfit pieces (the outfit's own
        ' _pieces are untouched; slot-conflict + preview are top-level only). Early-return keeps the top-level path
        ' below byte-identical when at the outfit root.
        If Not IsAtTopLevel() Then
            RenderLeveledLevel()
            ' ⛔ El preview TAMBIÉN se despacha acá: ver `DespacharPreviewSiCorresponde`.
            Await DespacharPreviewSiCorresponde()
            Return
        End If
        ' Returning to the outfit root after editing a nested list: re-sample every leveled piece's realization once
        ' so the top view reflects the edited list contents (added/removed/edited entries). Cleared after one pass.
        ' ⛔ TODAS, y acá sí corresponde: la bandera viene del DRILL-DOWN, donde el usuario pudo editar
        ' cualquiera de la cadena que abrió y el dato «qué lista» no existe. Ver `_lvlDirtyResample`.
        If _lvlDirtyResample Then
            _lvlDirtyResample = False
            ReMuestrearListas(TodasLasListas)
        End If
        LabelPieces.Text = "Outfit pieces:"
        UpdateLevelChrome()
        ' Remember the selected piece so a rebuild (add item, add-to-lvl, reroll) doesn't drop the
        ' selection — the user can keep acting on the same piece (e.g. add several items into the same
        ' leveled list) without re-selecting it every time. No-op if that piece is gone (e.g. Remove).
        ' ⛔ POR IDENTIDAD DE FILA, no por FormID. Dos filas pueden compartir el FormID —el INAM puede traer
        ' la MISMA lista por nivel dos veces, que es el caso que abrio esta tanda— y con la llave puesta
        ' en el numero, el rebuild le devolvia la seleccion a la PRIMERA que coincidiera: el usuario
        ' seguia editando otra fila que la que tenia elegida.
        Dim keepSelected As PieceEntry = SelectedPieceEntry()

        ' La MISMA ley que el render: una unidad por ARMO terminal, veredicto por unidad, agregado por fila.
        ' ⛔ UN memo por pasada, compartido: el torneo y las filas tienen que contestar con la MISMA
        ' emisión. Con un memo por lado, una prenda podía quedar fuera del torneo y marcada como que se ve,
        ' o al revés, y la barra volvía a mentir por otro camino.
        Dim emisionMemo As New Dictionary(Of String, EmisionArmo)
        ' El MISMO mapa que consume el torneo: `Compite` y `Dibuja` no pueden divergir entre carriles.
        Dim ctxRefresh = ContextoDePasada()
        Dim units = BuildEquipUnits(emisionMemo, ctxRefresh)
        Dim res = EquipResolver.Resolve(units)
        Dim verdicts = VeredictosPorFila(res)
        ' La máscara que se pinta es EXACTAMENTE la que decidió el/(EquipResolver.MutexMaskOf), no otra:
        ' hoy en FO4 esa no es el BOD2 del ARMO, y pintar el BOD2 daba dos listas de slots distintas para el
        ' mismo ítem en la misma ventana. Cuando FO4 pase a EquipMask, esta columna lo sigue sola.
        ' ⛔ Sin `renderingCount`: era una variable de SOLO ESCRITURA. La barra cuenta ARMO equipados
        ' (`armosQueRenderizan`), no filas, y ese contador quedó de la versión por fila — leerlo hacía
        ' creer que la rama de cada fila era la que contaba.
        Dim eliminatedCount As Integer = 0, rolledNothingCount As Integer = 0, noAplicanCount As Integer = 0
        ' ⛔ LA BARRA CUENTA LO QUE EL MOTOR EQUIPA, no filas. Contaba `+= 1` por FILA, así que dos
        ' filas que resuelven al MISMO ARMO —una pieza directa y una lista que cae en ella— decían «2 of 2
        ' rendering» sobre UNA sola prenda dibujada. Las FILAS conservan su ✓ (las dos son caminos
        ' válidos a algo que se ve); lo que cambia es el CONTEO, que ahora es por ARMO equipado.
        Dim armosQueRenderizan As New HashSet(Of UInteger)
        Dim armosDelAtuendo As New HashSet(Of UInteger)
        For Each u In units
            armosDelAtuendo.Add(u.ArmoFormID)
        Next
        For Each w In res.Winners
            armosQueRenderizan.Add(w.ArmoFormID)
        Next
        ListViewPieces.BeginUpdate()
        Try
            ListViewPieces.Items.Clear()
            ' ⛔ La marca se DERIVA acá, no se guarda. Como campo era una FOTO que sólo se refrescaba al
            ' volver del editor de ARMO: agregar un ARMO válido a una lista por nivel recién creada dejaba
            ' la fila diciendo «no aplica» y la barra «0 of 1 rendering» mientras la vista previa SÍ la
            ' dibujaba — el mismo defecto de antes con el signo dado vuelta. Derivada no se puede
            ' desincronizar; y sale del MISMO memo que alimentó al torneo, así que las dos mitades de la
            ' respuesta no se pueden separar.
            ' ⛔ Con el contexto DE LA PASADA, no con Nothing: es la misma consulta que hizo el sembrado y
            ' la que hace el torneo. Con Nothing resolvia las combinaciones OBTS con las del ultimo
            ' render del NPC, asi que la misma prenda se podia marcar distinto en el sembrado y en el
            ' repintado siguiente.
            ' ⛔ Y POR LOS TERMINALES DE LA PIEZA, no por su FormID crudo. Era el ULTIMO consumidor que
            ' preguntaba por el hijo: las claves del mapa de contexto son TERMINALES (lo dice
            ' `ContextoDePasada`), asi que una pieza DIRECTA con plantilla preguntaba por un FormID que no
            ' esta en el mapa y se marcaba con contexto VACIO, mientras el torneo la resolvia con el del
            ' terminal. Dos respuestas para la misma prenda en la misma ventana. `PieceTerminals` es la
            ' misma puerta que usan el torneo, el sembrado y el volcado.
            Dim dibujaTerminal = Function(t As (Fid As UInteger, Ctx As List(Of UInteger))) As Boolean
                                     Return EmisionDe(t.Fid, ctxRefresh(t.Fid), emisionMemo, ctxRefresh).Dibuja
                                 End Function
            For Each p In _pieces.OrderBy(Function(x) x.Order)
                ' Una vez por fila: la marca, el conteo slotless y el estado salen del MISMO recorrido.
                Dim terms = PieceTerminals(p).ToList()
                ' ⛔ `terms.Count > 0 AndAlso ...`: una lista que NO sorteo nada no tiene terminales, y eso
                ' NO es «no aplica a este NPC» —es «rolled nothing», que tiene su propia rama—. Marcarla
                ' acá seria mover la mentira de lugar, que es el defecto que esta fila ya tuvo dos veces.
                Dim noAplica = terms.Count > 0 AndAlso Not terms.Any(dibujaTerminal)
                Dim v = If(verdicts.ContainsKey(p), verdicts(p), (Won:=0, Lost:=0, MutexMask:=0UI))
                Dim hasUnits = (v.Won + v.Lost) > 0
                ' ⛔ EL CONTEO SLOTLESS ES POR TERMINAL, no por `hasUnits` de la pieza entera. Una pieza
                ' MIXTA —una lista UseAll cuyo T1 compite y cuyo T2 sólo monta por chunk-mount de OMOD—
                ' tiene `hasUnits = True`, asi que la rama de abajo no corria y T2 no entraba NUNCA al
                ' conteo: la barra decia «1 of 1» con dos prendas dibujadas. Un terminal que YA esta en
                ' `armosDelAtuendo` produjo unidad y compite: su suerte la decide el torneo y acá no se
                ' toca —forzarlo a «renderiza» le taparia una derrota—.
                For Each t In terms
                    If t.Fid <> 0UI AndAlso Not armosDelAtuendo.Contains(t.Fid) AndAlso dibujaTerminal(t) Then
                        armosDelAtuendo.Add(t.Fid)
                        armosQueRenderizan.Add(t.Fid)
                    End If
                Next
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
                ElseIf Not hasUnits AndAlso terms.Any(dibujaTerminal) Then
                    ' ⛔ SE VE PERO NO PELEA EL SLOT: emite geometría por chunk-mount de OMOD, que sale con
                    ' `SlotMask = 0` y se dibuja por la pasada slotless del render sin entrar al torneo. No
                    ' es «rolled nothing» —eso es una lista que no sorteó, y se distingue porque ahí no hay
                    ' ningún terminal—. Esta rama decide sólo el ESTADO DE LA FILA; el conteo ya lo hizo el
                    ' recorrido por terminal de arriba, que también cubre la pieza MIXTA que ésta no ve.
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
                    status = "✓"
                    slotsText = DescribeSlotMask(v.MutexMask)
                ElseIf v.Won = 0 Then
                    eliminatedCount += 1
                    status = "✗ eliminated"
                    slotsText = DescribeSlotMask(v.MutexMask)
                    row.ForeColor = Color.Gray
                Else
                    ' Una lista UseAll puede realizar en varios ARMO: unos ganan y otros caen. Pintarla o
                    ' sería mentira en los dos sentidos.
                    status = $"◐ {v.Won}/{v.Won + v.Lost}  ·  {v.Lost} eliminated"
                    slotsText = DescribeSlotMask(v.MutexMask)
                End If
                row.SubItems.Add(slotsText)
                row.SubItems.Add(status)
                row.SubItems.Add(p.Plugin)
                row.Tag = p
                If keepSelected IsNot Nothing AndAlso p Is keepSelected Then row.Selected = True
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

        ' «0 of 0 armor piece(s) rendering · 2 no aplica(n)» con dos filas a la vista se lee como un error de
        ' la barra. Cuando el atuendo NO APORTA NINGÚN ARMO a este NPC, el conteo no dice nada que la frase no
        ' diga mejor: las filas están, y ninguna aplica. Es sólo el texto — los conteos no cambian.
        ' ⛔ Y LA MISMA FRASE QUE LA FILA, palabra por palabra: «no aplican a este NPC», nunca «por
        ' raza/género». Es la regla de arriba (ver la marca de la etiqueta) y acá la había roto: el predicado
        ' junta TRES motivos —sin ARMA de la raza o sin malla del género, el gate de power armor, y un FormID
        ' que no resuelve—, así que nombrar el primero manda a revisar la raza a quien tiene un ARMO
        ' desinstalado. La barra y la fila no pueden decir causas distintas del mismo hecho.
        Dim statusText As String
        If armosDelAtuendo.Count = 0 AndAlso noAplicanCount > 0 Then
            statusText = $"ninguna pieza aplica a este NPC ({noAplicanCount} de {_pieces.Count} no aplican)"
        Else
            statusText = $"{armosQueRenderizan.Count} of {armosDelAtuendo.Count} armor piece(s) rendering"
            If noAplicanCount > 0 Then statusText &= $"  ·  {noAplicanCount} no aplica(n) a este NPC"
        End If
        If eliminatedCount > 0 Then statusText &= $"  ·  {eliminatedCount} eliminated by slot conflict"
        If rolledNothingCount > 0 Then statusText &= $"  ·  {rolledNothingCount} rolled nothing"
        LabelCreateStatus.Text = statusText

        ' Preview only when the Create tab is active and the host exists (skipped during construction,
        ' where the active tab is Browse and _host has not been created yet). The list repaint above ran
        ' synchronously before this Await, so the/feedback is immediate. RefreshCreatePreview honors
        ' the whole-outfit vs selected-piece toggle.
        Await DespacharPreviewSiCorresponde()
    End Sub

    ''' <summary>El despacho del preview de la pestaña Create, en UN solo lugar.
    ''' <para>⛔ Lo llaman LOS DOS caminos de <see cref="RefreshPieces"/> — el de la raíz y el del
    ''' drill-down—. El anidado salía por <c>Return</c> ANTES de la cola, así que volver a Create desde
    ''' Browse estando metido en una lista por nivel dejaba dibujado el atuendo que se había mirado en
    ''' Browse. (En modo «pieza seleccionada» no se notaba porque el cambio de selección dispara por su
    ''' cuenta.) La ELECCIÓN de qué dibujar sigue estando una sola vez, adentro de
    ''' <see cref="RefreshCreatePreview"/>: acá sólo está la guarda de cuándo.</para></summary>
    Private Async Function DespacharPreviewSiCorresponde() As Task
        If TabsMain.SelectedTab Is TabPageCreate AndAlso _host IsNot Nothing Then
            Await RefreshCreatePreview()
        End If
    End Function

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
        LimpiarObjetivoDeCreate()
    End Sub

    ''' <summary>SACARLE EL OBJETIVO A LA PESTAÑA CREATE ES UN SOLO GESTO, y son TRES cosas: el objetivo, la
    ''' caja del EditorID y el cartel. Nunca una sola.
    '''
    ''' <para>⛔ <b>QUINTA PUERTA, y era un defecto.</b> <see cref="OnDeleteOrRevertOutfit"/> limpiaba el
    ''' objetivo a mano —las dos ramas— y NO tocaba la caja: quedaba como la había dejado
    ''' <c>EditorIdField.ConfigureOverride</c>, o sea <b>deshabilitada y con el EditorID COMPLETO adentro</b>,
    ''' mientras <see cref="CommitCreate"/>, ya sin objetivo, la lee como SUFIJO. Repro: «My outfits» → doble
    ''' clic en una guardada → Delete/Revert → Sí → OK ⇒ <c>npcm_Outfit_npcm_Outfit_Alpha</c>. Y lo peor no es
    ''' el nombre: la caja está DESHABILITADA, así que el usuario ve el error y no lo puede arreglar. El
    ''' cartel también mentía — seguía diciendo OVERRIDE de un record que se acaba de ir.</para>
    '''
    ''' <para>Por eso las tres líneas viven acá y no en cada llamador: el objetivo y la caja son <b>un solo
    ''' estado</b> y quien mueva uno sin el otro reabre exactamente esto.</para></summary>
    Private Sub LimpiarObjetivoDeCreate()
        _overrideTargetFormID = 0UI
        _overrideTargetEditorID = ""
        RefreshOutfitEdidField(0UI, OutfitDraft.EditorIdPrefix)   ' sin objetivo: nombre editable, vacío
        UpdateCreateBanner()
    End Sub

    ''' <summary>¿La caja del EditorID es el &lt;nombre&gt; (un SUFIJO que se compone con el prefijo) o el EDID
    ''' COMPLETO que se conserva tal cual? Lo decide el OBJETIVO del commit y nada más.
    '''
    ''' <para>⛔ <b>ES EL MISMO PREDICADO QUE <see cref="CommitCreate"/>, Y POR ESO ES UNA SOLA FUNCIÓN.</b> El
    ''' que llena la caja y el que la lee tienen que partir del mismo lugar: quien escribe el EDID completo en
    ''' una caja que el commit va a leer como sufijo produce <c>npcm_Outfit_npcm_Outfit_MiAtuendo</c>, y cada OK
    ''' vuelve a duplicar el prefijo. Eso pasaba: las puertas que re-apuntan el objetivo pasaban un
    ''' <c>False</c> LITERAL —«esto es un override»— sin mirar si el objetivo era un BORRADOR, y un borrador
    ''' 0xFF SÍ es renombrable, así que el commit componía sobre lo ya compuesto.</para>
    '''
    ''' <para>Las dos ramas que dan SUFIJO son exactamente las dos que <c>CommitCreate</c> resuelve leyendo
    ''' <c>TextBoxEdid</c> con <c>EditorIdField.Compose</c>: SIN objetivo (atuendo nuevo, FormID por asignar) y
    ''' con un objetivo BORRADOR (un record propio que todavía no se escribió, renombrable). La otra —un OTFT
    ''' REAL— conserva su EditorID verbatim porque el guardado no le pone namespace a un override.</para>
    '''
    ''' <para><c>Friend Shared</c> y PURA para que el gate la corra: la ley entera es este renglón, y medida por
    ''' el TEXTO de los llamadores se podía romper con el caso en verde.</para></summary>
    Friend Shared Function ElNombreEsSufijo(objetivo As UInteger) As Boolean
        Return objetivo = 0UI OrElse Borradores.EsFormIdDeBorrador(objetivo)
    End Function

    ''' <summary>Drive the shared EditorID field (prefix label + name box + live "Saves as:" preview) uniformly for
    ''' the Create tab: a NEW/owned record edits only the &lt;name&gt; after a fixed prefix with a live preview;
    ''' a real OVERRIDE shows its kept EditorID read-only. <paramref name="baseOrKeptEdid"/> = the base EDID whose
    ''' name seeds the box (New) or the verbatim EDID to keep (Override).
    ''' <para>⛔ RECIBE EL OBJETIVO, NO UN BOOLEANO. La decisión es de <see cref="ElNombreEsSufijo"/> — el mismo
    ''' predicado que consume el commit—, así que ningún llamador puede volver a declarar «esto es un override»
    ''' por su cuenta sobre un objetivo que es un borrador.</para></summary>
    Private Sub RefreshOutfitEdidField(objetivo As UInteger, baseOrKeptEdid As String)
        If ElNombreEsSufijo(objetivo) Then
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
        ' ⛔ EL OBJETIVO DECIDE, no un `False` literal: `ResolveOverrideTarget` devuelve lo que esté elegido en
        ' Browse, y ahí también aparecen los BORRADORES propios, que son renombrables. Ver `ElNombreEsSufijo`.
        RefreshOutfitEdidField(_overrideTargetFormID, _overrideTargetEditorID)
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
        ' ⛔ Se vuelve a la RAIZ. Sembrar con el usuario metido en una lista por nivel dejaba la pila de
        ' navegacion apuntando a un borrador de OTRO atuendo: la grilla mostraba las entradas de esa
        ' lista mientras las piezas sembradas eran las del atuendo nuevo.
        _lvlNavStack.Clear()
        _pieces.Clear()
        _pieceOrderCounter = 0
        ' INAM items AS AUTHORED (ARMO or LVLI) — a leveled entry stays a leveled piece (not flattened).
        ' ⛔ TODAS entran, también las que no aplican a esta raza/género: ver `SembrarInam`.
        ' ⛔ Y SI EL OBJETIVO ES UN BORRADOR, CON SUS REALIZACIONES SELLADAS: «reabrir NO re-sortea» es la
        ' MISMA ley que aplica `LoadOutfitDraftForEdit`, y acá faltaba. Ésta es la puerta que recorre el
        ' usuario que abre "Edit outfit" sobre un NPC cuyo atuendo por defecto YA es un borrador propio: sin
        ' las selladas, el sembrado volvía a muestrear TODAS las listas por nivel, así que la grilla mostraba
        ' una realización distinta de la que el render está dibujando y un OK sin tocar nada le cambiaba la
        ' prenda al usuario. El índice es 1:1 con el plan por la misma cita que allá: `ResolveOutfitItemList`
        ' de un borrador ES `d.Prendas()`, y el plan sólo saltea las prendas en 0, que un borrador no tiene.
        ' Un OTFT REAL no tiene nada sellado —su sorteo es de esta sesión— y entra con `Nothing`, o sea
        ' muestreando, que es lo correcto para él.
        Dim selladas As IReadOnlyList(Of List(Of OutfitArmorPick)) = Nothing
        Dim d = _mainForm.TryGetOutfitDraft(fid)
        If d IsNot Nothing Then selladas = d.Realizaciones
        SembrarInam(_mainForm.ResolveOutfitItemList(fid), selladas)
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
        ' ⛔ SE CONSUME EL RE-MUESTREO PENDIENTE ANTES DE VOLCAR. `_lvlDirtyResample` lo ponen las
        ' ediciones del nivel anidado -Remove entry, Edit entry, Add item-, y se consumía SÓLO en el
        ' repintado de la RAÍZ. Apretando OK sin volver atrás, el commit volcaba las realizaciones
        ' ANTERIORES a la edición: el borrador quedaba con picks apuntando a entradas que el usuario
        ' acababa de borrar. El re-muestreo corre ACÁ, en el hilo de UI, coherente con el sellado.
        ' ⛔ Y es la bandera del DRILL-DOWN, o sea TODAS: la edición de raíz ya re-sorteó dirigido en el gesto
        ' y no deja nada pendiente. Ver `_lvlDirtyResample` y `PlanDeReMuestreo`.
        If _lvlDirtyResample Then
            _lvlDirtyResample = False
            ReMuestrearListas(TodasLasListas)
        End If
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
            ' ⛔ EL MISMO PREDICADO QUE LLENÓ LA CAJA. Acá adentro el objetivo ya no puede ser 0 —lo excluye el
            ' `If` de arriba—, así que `ElNombreEsSufijo` es exactamente «es un borrador». Escrito con la otra
            ' mitad de la ley, el que llena y el que lee podían divergir, y divergieron.
            Dim isDraftTarget = ElNombreEsSufijo(_overrideTargetFormID)
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
        ' ⛔ EL OBJETIVO DECIDE. Hoy `GetAuthoredOutfitFormIDs` sólo trae records REALES, así que acá el
        ' predicado da False igual que el literal que había; va por el objetivo porque la ley es UNA para las
        ' cuatro puertas y no se sostiene con tres que la aplican y una que la asume. Ver `ElNombreEsSufijo`.
        RefreshOutfitEdidField(_overrideTargetFormID, _overrideTargetEditorID)
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
        ' ⛔ Por el OBJETIVO y no por `d.IsNew`: es el mismo veredicto —un borrador nuevo tiene FormID 0xFF y uno
        ' de override conserva el REAL— y así las cuatro puertas preguntan lo mismo. Ver `ElNombreEsSufijo`.
        RefreshOutfitEdidField(_overrideTargetFormID, d.Record.EditorID)
        ' Populate _itemCandidatesByFid before the per-item lookups in AddItemFidAsPiece.
        RefreshItemCandidates()
        ' ⛔ Se vuelve a la RAIZ: reabrir un borrador con el usuario metido en una lista por nivel
        ' dejaba la pila apuntando a OTRO borrador. Ver `PrefillPiecesFromOutfit`.
        _lvlNavStack.Clear()
        _pieces.Clear()
        _pieceOrderCounter = 0
        ' ⛔ El MISMO sembrador que `PrefillPiecesFromOutfit`. Con `AddItemFidAsPiece` acá, reabrir el
        ' borrador volvía a descartar lo que no es candidato: el arreglo no sobrevivía su propio
        ' round-trip — override, aceptar, reabrir para retocar el nombre, y las prendas se caían igual.
        ' ⛔ Con las realizaciones YA SELLADAS del borrador: reabrir no re-sortea. Ver `SembrarInam`.
        SembrarInam(d.Prendas(), d.Realizaciones)
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
            If _overrideTargetFormID = fid Then LimpiarObjetivoDeCreate()
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
        If _overrideTargetFormID = d.FormID Then LimpiarObjetivoDeCreate()
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

        ' ⛔ CANCELAR DEJA EL ORIGINAL COMO ESTABA, también para las listas por nivel, y son DOS mitades:
        ' las que creó este diálogo se BAJAN con su foto (UnregisterLeveledListDraft, el par simétrico de
        ' Publicar) y las PREEXISTENTES que la sesión mutó se REVIERTEN a su estado de apertura.
        ' ⛔ Las DOS mitades o ninguna: bajar sólo lo creado deja a una lista preexistente apuntando al
        ' 0xFF de la que se acaba de ir, y esa referencia colgada revienta el guardado siguiente.
        ' Primero se revierte y después se baja, para que ningún paso vea un estado a medias.
        ' ⛔ Y CON OK TAMBIÉN SE BAJA lo que quedó sin dueño: ver `PlanDeCierreDeListas`. El censo va DESPUÉS
        ' de dar de baja el borrador de vista previa (arriba): ése referencia todo lo que el usuario armó en
        ' Create, así que consultarlo antes salvaría hasta la lista abandonada.
        Dim planCierre = PlanDeCierreDeListas(DialogResult = DialogResult.OK,
                                              _lvliSnapshotDeApertura.Values, _lvliRegistradasPorMi,
                                              AddressOf _mainForm.TieneReferrerFueraDe)
        For Each snap In planCierre.Restaurar
            Try
                _mainForm.RestaurarBorradorDeLista(snap)
            Catch ex As Exception
                Logger.Log(ex.ToString())
            End Try
        Next
        For Each fidLvl In planCierre.Bajar
            Try
                _mainForm.UnregisterLeveledListDraft(fidLvl)
            Catch ex As Exception
                Logger.Log(ex.ToString())
            End Try
        Next
        _lvliSnapshotDeApertura.Clear()
        _lvliRegistradasPorMi.Clear()

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
