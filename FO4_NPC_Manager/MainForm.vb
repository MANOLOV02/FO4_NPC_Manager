Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics
Imports FO4_Base_Library.Canon.CanonInterpretacion

Public Class MainForm

    Private ReadOnly _pluginManager As PluginManager
    ''' <summary>Shared record-parse services. Single owner of the ARMO/ARMA/RACE/
    ''' HDPT/NPC_ parse caches; created once in the ctor and injected into extracted render
    ''' subsystems. Replaces the per-MainForm parse caches + GetParsed* helpers.</summary>
    Private ReadOnly _ctx As NpcRenderContext
    ''' <summary>Material / texture-set / hair-palette / color-form resolution.
    ''' Receives the shared <see cref="_ctx"/>; ApplyShapeMaterialOverrides + skin-tone
    ''' resolvers still live here.</summary>
    Private ReadOnly _materialResolver As NpcMaterialResolver
    ''' <summary>NPC visual-state resolution (template-chain traits/inventory/model, race fallbacks,
    ''' skeleton key, leveled-NPC pick). Receives the shared context + collaborators +
    ''' IoC delegates for the MainForm-resident state it can't own (gender filter, LM-skin resolver).</summary>
    Private ReadOnly _stateResolver As NpcStateResolver
    ''' <summary>Morph + pose resolution (face/body morph resolvers, FMRS face-bone transforms,
    ''' body-weight data, race height, merged pose, facial-bone regions). Skeleton
    ''' LOADING (PrepareSkeleton) + caches stay in MainForm. IoC: overlay + host-provider delegates.</summary>
    Private ReadOnly _morphPoseResolver As NpcMorphPoseResolver
    ''' <summary>FaceTint compositor EXECUTION (face-tint compose + skin SoftLight/subsurface passes,
    ''' pristine diffuse snapshot/rollback, live face-tint refresh). Owns the per-process
    ''' _tintBytesCache. The skin-override live-preview fast-path stays in MainForm (coupled to
    ''' CollectArmoCandidates) and calls back into this resolver. IoC: ctx + materialResolver +
    ''' host-provider delegate + shared _appliedPresets.</summary>
    Private ReadOnly _faceTintResolver As NpcFaceTintResolver
    ''' <summary>Mounting math (mount-delta transforms for robot
    ''' chunks/sockets onto a live SkeletonInstance, host-scoped socket resolution, synthetic-skin Pipboy).
    ''' Pure data + NiflySharp, no GL/controls. The render orchestrator (RenderCurrentStateAsync) stays in
    ''' MainForm and calls this. IoC: ctx + stateResolver.</summary>
    Private ReadOnly _mountingResolver As NpcMountingResolver
    ''' <summary>The candidate pipeline (ResolvePreviewVariant →
    ''' collect ARMO/OTFT/headpart/robot-chunk candidates → slot-conflict selection + headwear occlusion →
    ''' LoadNifShapes). Pure data + NiflySharp, no GL/controls. The render orchestrator stays in MainForm
    ''' and calls _meshCollector.ResolvePreviewVariant. IoC: ctx + materialResolver + stateResolver +
    ''' mountingResolver + Func delegates (ArmoIsPowerArmor, RaceIsPowerArmor —
    ''' shared power-armor predicates kept in MainForm because the outfit/armo-universe also uses them).</summary>
    Private ReadOnly _meshCollector As NpcMeshCollector
    ''' <summary>Skin-override live-preview fast-path (EditBody skin swap →
    ''' re-resolve TXST/MSWP in place + re-bake face tint/softlight, no VBO regen). Orchestrates
    ''' meshCollector + materialResolver + faceTintResolver over host.PreviewCtl. IoC: the three
    ''' resolvers + host-provider + shared _appliedPresets + Func delegates for ResolveLmSkinTemplate
    ''' and the live _previewRequestVersion token.</summary>
    Private ReadOnly _skinLivePreview As NpcSkinLivePreview
    ''' <summary>Todos los NPC del orden de carga. ⛔ SIEMPRE ORDENADO por <see cref="ClaveDeOrden"/>:
    ''' es una INVARIANTE de la que depende <see cref="PopulateNPCTree"/>, que agrupa por plugin y NO
    ''' reordena. Todo lo que agregue, reemplace o le cambie la clave a un elemento tiene que terminar
    ''' en <see cref="OrdenarNpcs"/>.</summary>
    Private _allNPCs As New List(Of NPC_Data)
    Private _previewControl As PreviewControl
    Private _dataPath As String = ""
    Private _assetDictionaryLoadTask As Task = Nothing
    Private ReadOnly _assetDictionaryLock As New Object()
    Private _previewRequestVersion As Integer = 0
    ''' <summary>Serializes the CPU render compute (BuildRenderPlan) across overlapping renders so two
    ''' never run concurrently on the ThreadPool. Without it, rapid NPC/outfit switches could run two
    ''' BuildRenderPlan on background threads at once, racing on shared memoization caches
    ''' (e.g. NpcMeshCollector._candidateHairSlotMaskCache). Order-dependent data (outfit pieces, slot
    ''' conflict, tints, region swaps) is computed in per-call LOCAL state so the order itself is never
    ''' interleaved; the gate additionally removes any shared-cache contention. The GL submission tail
    ''' runs on the (single) UI thread, so it needs no gate. Held only for the duration of the Task.Run.</summary>
    Private ReadOnly _renderGate As New System.Threading.SemaphoreSlim(1, 1)
    Private Shared ReadOnly _rng As New Random()
    Private _templateDependencyMapCache As New Dictionary(Of UInteger, List(Of TemplateDependencyEdge))()
    Private _templateRootSourceIdsCache As New List(Of UInteger)()
    ''' <summary>Universe of ARMO FormIDs referenced as skin by any RACE.WNAM or NPC_.WNAM in the
    ''' load order. Populated once after ParseAllNPCs and consumed by EditBody's NPC.WNAM combo
    ''' filter. Excludes pure outfit ARMOs because no record in the load order points to them as
    ''' skin — a cheap and engine-faithful way to narrow the candidate pool.</summary>
    Private _skinArmoUniverse As New HashSet(Of UInteger)()
    ''' <summary>Universe of OTFT FormIDs in the load order. Populated once in RebuildTreeModelCache;
    ''' consumed by the Edit Outfit picker (NPC.DOFT override). The per-race/gender filter
    ''' (<see cref="GetOutfitCandidates"/>) runs over this set, expanding each OTFT deterministically
    ''' via OutfitResolver.EnumerateAllTerminalArmos and checking per-ARMA race+gender validity.</summary>
    Private _outfitUniverse As New HashSet(Of UInteger)()

    ' --- Manual multi-select for the NPC tree (WinForms TreeView has no native multi-select) ---
    ''' <summary>NPC FormIDs currently multi-selected in the tree. Keyed by FormID so an NPC that
    ''' appears under several nodes (its plugin group + every LVLN that lists it) highlights
    ''' everywhere at once and batch ops dedup naturally. Drives the highlight in
    ''' <see cref="TreeViewNPCs_PintarFila"/> and the random render pick.</summary>
    Private ReadOnly _selectedNpcFormIDs As New HashSet(Of UInteger)()
    ''' <summary>FormID of the NPC actually being rendered out of the selection (the random pick, or
    ''' the single selected one). Painted with the full highlight; the rest of the set gets a paler
    ''' one so the user can see which member was rolled.</summary>
    Private _currentRandomPickFormID As UInteger = 0UI
    ''' <summary>FormID whose detail tree was just built by <see cref="TreeViewNPCs_FilaEnfocada"/>.
    ''' Lets the debounced render skip the redundant rebuild for the single-select case (AfterSelect
    ''' already populated it). 0 = none pending; consumed-and-cleared in
    ''' <see cref="RenderFromCurrentSelection"/> so it suppresses ONLY the one render that follows
    ''' a selection — any later re-render of the same NPC still repopulates fresh.</summary>
    Private _detailsAfterSelectFormID As UInteger = 0UI
    ' ⛔ ACA VIVIA `_multiSelectBrush`, "pale highlight brush for non-picked members of a
    ' multi-selection". Se DECLARABA y se LIBERABA en MainForm_FormClosing, pero no se le asignaba nada
    ' en todo el archivo: resto del owner-draw del TreeView viejo. El tono suave de la multi-seleccion
    ' lo da `ColoresDelArbol.SeleccionSuave` via `EstiloDeFila.Fondo`.
    ''' <summary>FormIDs the tree context menu acts on (the multi-selection when the right-click
    ''' lands inside it, else just the clicked NPC). Set in <see cref="TreeViewNPCs_FilaClickeada"/>.</summary>
    Private ReadOnly _contextMenuTargets As New List(Of UInteger)()
    ''' <summary>Debounce timer: coalesces rapid selection changes so the heavy render fires once
    ''' after the selection settles (same pattern as Wardrobe Manager's source/target lists).</summary>
    Private WithEvents _selectionDebounceTimer As New System.Windows.Forms.Timer With {.Interval = 180}
    ''' <summary>Cache of GetOutfitCandidates results keyed by (race, isFemale). The deterministic
    ''' OTFT→ARMO expansion + ARMA parse is the costly part, so the first picker-open per race/gender
    ''' pays it and subsequent opens are instant. Cleared whenever the universe is rebuilt.</summary>
    Private _outfitCandidateCache As New Dictionary(Of (Race As UInteger, Female As Boolean), List(Of (FormID As UInteger, DisplayName As String)))
    ''' <summary>Cache of "does (race, gender) have ANY valid outfit?" — drives the Edit Outfit button
    ''' enable so it isn't lit for races with zero compatible outfits (creatures, robots, some
    ''' children). Cheaper than the full list (early-exits on the first match) and cached so the
    ''' render-complete gate doesn't re-scan. Cleared whenever the universe is rebuilt.</summary>
    Private _outfitAvailabilityCache As New Dictionary(Of (Race As UInteger, Female As Boolean), Boolean)
    ''' <summary>Cache of selectable ARMO ITEMS (armor/clothing pieces) for the Edit Outfit "Create"
    ''' tab, keyed by (race, gender). Each entry is (FormID, DisplayName, SlotMask). The full ARMO
    ''' sweep + per-ARMA race/gender resolution is the costly part, so the first Create-tab-open per
    ''' race/gender pays it and the rest are instant. Cleared on plugin reload.</summary>
    Private _armoItemCandidateCache As New Dictionary(Of (Race As UInteger, Female As Boolean), List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String)))
    ''' <summary>Cache of the RECORD-derived skin-ARMO candidates for the WNAM combo, keyed by (race, gender).
    ''' Only the <see cref="_skinArmoUniverse"/> sweep (parse + SkinArmoQualifies per ARMO) is cached — the
    ''' dirty ARMO drafts are appended FRESH on every call (they change as the user authors them). The ARMO/ARMA
    ''' parses it relies on are globally cached (<see cref="NpcRenderContext.GetParsedArmo"/>/GetParsedArma), so
    ''' the record portion is stable between reloads. Cleared beside <see cref="_armoItemCandidateCache"/>.</summary>
    Private _skinArmoCandidateCache As New Dictionary(Of (UInteger, Boolean), List(Of (FormID As UInteger, DisplayName As String)))
    ''' <summary>Outfits authored in the Edit Outfit "Create" tab — drafts that live here (process
    ''' scope, survive NPC selection changes) until the Save dialog's "Save new outfits" persists
    ''' them. New drafts get a provisional FormID (<see cref="OutfitDraft.DraftFormIdHighByte"/>)
    ''' allocated from <see cref="_nextDraftObjIndex"/>; the render/Browse/writer resolve them via
    ''' <see cref="TryGetOutfitDraft"/>. Cleared on plugin reload (RebuildTreeModelCache).</summary>
    Private ReadOnly _outfitDrafts As New List(Of OutfitDraft)
    ''' <summary>Author-built leveled lists (LVLI drafts) — same lifetime/scope as <see cref="_outfitDrafts"/>;
    ''' provisional FormIDs from the SAME <see cref="AllocateDraftFormID"/> counter so OTFT and LVLI drafts
    ''' never collide. Resolved via <see cref="TryGetLeveledListDraft"/>. Cleared on plugin reload.</summary>
    Private ReadOnly _leveledListDrafts As New List(Of LeveledListDraft)
    ''' <summary>Armor (ARMO) drafts authored in the (future) ARMA/ARMO/MSWP editor — same lifetime/scope as
    ''' <see cref="_outfitDrafts"/>; provisional FormIDs from the SAME <see cref="AllocateDraftFormID"/> counter
    ''' so OTFT/LVLI/ARMA/ARMO/MSWP drafts NEVER collide (cross-refs between draft kinds must resolve). The
    ''' saver (Phase 2e) emits the transitive closure of ARMO drafts reachable from emitted outfits/leveled
    ''' lists (plus dirty standalone / WNAM-skin-referenced drafts), pulling their ARMA + MSWP draft refs.</summary>
    Private ReadOnly _armoDrafts As New List(Of ArmoDraft)
    ''' <summary>Armor Addon (ARMA) drafts — same lifetime/scope and shared FormID counter as
    ''' <see cref="_outfitDrafts"/>. Pulled into a save (saver Phase 2f) when a needed ARMO draft references
    ''' them via ArmorAddons; their material-swap refs pull in MSWP drafts.</summary>
    Private ReadOnly _armaDrafts As New List(Of ArmaDraft)
    ''' <summary>Material Swap (MSWP) drafts — same lifetime/scope and shared FormID counter as
    ''' <see cref="_outfitDrafts"/>. Pulled into a save (saver Phase 2g) when a needed ARMO/ARMA draft
    ''' references them via a material-swap FormID.</summary>
    Private ReadOnly _mswpDrafts As New List(Of MswpDraft)
    ''' <summary>Next object index (low 3 bytes, ≥0x800 by the FO4 new-record convention) for
    ''' a provisional draft FormID. SHARED across ALL draft kinds (OTFT/LVLI/ARMA/ARMO/MSWP) so the
    ''' provisional sentinels are globally unique and cross-references between drafts never collide.</summary>
    Private _nextDraftObjIndex As UInteger = &H800UI
    ''' <summary>Parsed F4SE LooksMenu skin templates loaded from
    ''' Data\F4SE\Plugins\F4EE\Skin\&lt;mod&gt;\skin.json and Data\F4SE\Plugins\F4EE\Skin\Loose\*.json.
    ''' Mirrors the bundle structure of f4ee/SkinInterface.cpp:490-621 (id+name+gender+sort + per-gender
    ''' face TXST / head HDPT / rear HDPT + skin ARMO). Populated once after plugin load.</summary>
    Private _lmSkinTemplates As New List(Of LmSkinTemplate)()
    ''' <summary>Parsed F4SE LooksMenu body-overlay ("tattoo") templates loaded from
    ''' Data\F4SE\Plugins\F4EE\Overlays\&lt;mod&gt;\overlays.json + Overlays\Loose\*.json (the disk
    ''' layout LoadOverlayMods scans — OverlayInterface.cpp:1025-1052). Gender-separated exactly like
    ''' the engine's <c>m_overlayTemplates[isFemale?1:0]</c> (OverlayInterface.cpp:1084): index 0 = male,
    ''' 1 = female. First-loaded-wins on a duplicate id within a gender (engine keeps the first via
    ''' <c>emplace</c>, :1084-1090). Populated once after plugin load by <see cref="BuildOverlayTemplateCache"/>.</summary>
    Private ReadOnly _overlayTemplates() As List(Of OverlayTemplate) = {New List(Of OverlayTemplate)(), New List(Of OverlayTemplate)()}
    ''' <summary>NPCs directly placed in the world via ACHR records (unique characters).</summary>
    Private _directlyPlacedNPCFormIDs As New HashSet(Of UInteger)()
    ''' <summary>NPCs that appear in the game world: placed in CELLs (ACHR) or in LVLN encounter lists.</summary>
    Private _npcsInGameWorld As New HashSet(Of UInteger)()
    ''' <summary>NPCs that are referenced as template source (TPLT/TPTA) by other NPCs.</summary>
    Private _npcsUsedAsTemplates As New HashSet(Of UInteger)()
    ''' <summary>Final LVLNs: leveled NPC lists that are NOT nested inside another LVLN.</summary>
    Private _finalLVLNFormIDs As New List(Of UInteger)()
    ''' <summary>Parsed LVLN data cache keyed by FormID.</summary>
    Private _lvlnDataCache As New Dictionary(Of UInteger, Canon.ILvln)()
    ''' <summary>Pre-computed flattened leaf NPC FormID list per LVLN. Recursive descent into
    ''' nested LVLNs is resolved during cache warmup (BuildNPCClassification) so the tree
    ''' rebuild path doesn't do per-keystroke recursion + per-entry _pluginManager.GetRecord
    ''' lookups. Invalidation: BuildNPCClassification clears + repopulates. Save ESP doesn't
    ''' mutate LVLN data → cache stays valid across saves.</summary>
    Private _lvlnLeavesCache As New Dictionary(Of UInteger, List(Of UInteger))()
    ''' <summary>Pre-computed lowercase searchable text per NPC FormID. Concatenates the 6 fields
    ''' MatchesNpcFilter compares against (ToString, EditorID, FullName, PluginName, FormID hex)
    ''' so per-keystroke filter becomes ONE IndexOf instead of 6 + a String() allocation.
    ''' Invalidation: rebuilt in BuildNPCClassification. Save ESP changes PluginName for a single
    ''' NPC → entry gets rebuilt via InvalidateNpcSearchCache.</summary>
    Private _npcSearchableCache As New Dictionary(Of UInteger, String)()
    ''' <summary>Etiqueta del nodo por FormID ("FullName (EditorID, FormID)", con alternativas).
    ''' <para>⛔ SÍ cambia después de la carga: el readback del Save y el editor de NPC le cambian el
    ''' nombre a un record ya cargado, y los dos tienen que refrescar este cache vía
    ''' <see cref="RefrescarCachesDerivados"/>, junto con el texto buscable y la clave de orden (salen
    ''' de los mismos campos).</para></summary>
    Private _npcDisplayLabelCache As New Dictionary(Of UInteger, String)()

    ''' <summary>Clave con la que se ORDENA la lista, por FormID. Es exactamente
    ''' <c>NPC_Data.ToString()</c>, precalculada.
    ''' <para>Existe porque la clave se leia VIVA en los dos sitios que ordenan, y leerla no es
    ''' gratis: <c>ToString()</c> lee <c>Record.Name</c>, o sea una resolucion de ruta sobre el arbol
    ''' del record. El orden de <c>_allNPCs</c> hace ~2 lecturas por comparacion (unas 107 mil para
    ''' 4.473 NPC), y <c>PopulateNPCTree</c> vuelve a ordenar EN CADA repoblado: cada tecla del
    ''' buscador, cada checkbox de categoria y cada Save.</para>
    ''' <para>Se llena y se invalida en los MISMOS tres sitios que
    ''' <see cref="_npcDisplayLabelCache"/>, que tiene el mismo contrato de frescura: el barrido de
    ''' <c>RebuildTreeModelCache</c>, el <c>Clear</c> de <c>BuildNPCClassification</c> y el refresco
    ''' por NPC del readback del Save. Quien no este en el cache cae a leer el valor vivo, asi que
    ''' una ausencia da el mismo orden, no uno distinto.</para></summary>
    Private _npcSortKeyCache As New Dictionary(Of UInteger, String)()

    ''' <summary>La clave de orden de un NPC: la del cache, o la viva si no esta.</summary>
    Private Function ClaveDeOrden(n As NPC_Data) As String
        If n Is Nothing Then Return ""
        Dim k As String = Nothing
        If _npcSortKeyCache.TryGetValue(n.FormID, k) Then Return k
        Return n.ToString()
    End Function

    ''' <summary>QUE es la clave de orden. Un solo sitio: el plan contempla —y por ahora
    ''' descarta— un desempate por FormID; el día que se agregue, hay que actualizarlo en los tres
    ''' call-sites que llenan <see cref="_npcSortKeyCache"/> o la lista queda ordenada con dos leyes
    ''' distintas sin ningún aviso.</summary>
    Private Sub SembrarClaveDeOrden(npc As NPC_Data)
        If npc Is Nothing Then Return
        _npcSortKeyCache(npc.FormID) = npc.ToString()
        ' Va JUNTO con la clave, no aparte: los dos salen del record y el filtro los pide en la misma
        ' pasada. Sembrarlos en sitios distintos es la forma de que uno quede fresco y el otro no.
        _npcHeredaAparienciaCache(npc.FormID) = NpcTemplateHelpers.NpcInheritsVisualAppearance(npc)
    End Sub

    ''' <summary>Si el NPC HEREDA su apariencia de una plantilla, por FormID.
    ''' <para>Sale de leer las banderas de plantilla del ACBS, que es una resolucion de ruta sobre el
    ''' arbol del record — y el filtro de categorias la pedia para los 4.473 NPC en cada repoblado, o sea
    ''' en cada tecla del buscador. Cambia con las mismas ediciones que la clave de orden, asi que se
    ''' siembra y se invalida en el MISMO sitio: separarlos es la forma de que uno quede fresco y el otro
    ''' no.</para></summary>
    Private _npcHeredaAparienciaCache As New Dictionary(Of UInteger, Boolean)()

    ''' <summary>Si el NPC hereda apariencia: el del cache, o el vivo si no esta.</summary>
    Private Function HeredaApariencia(n As NPC_Data) As Boolean
        If n Is Nothing Then Return False
        Dim v As Boolean
        If _npcHeredaAparienciaCache.TryGetValue(n.FormID, v) Then Return v
        Return NpcTemplateHelpers.NpcInheritsVisualAppearance(n)
    End Function


    ''' <summary>Deja `_allNPCs` en el orden en que el arbol lo muestra. Es una INVARIANTE, no una
    ''' conveniencia: `PopulateNPCTree` agrupa por plugin y `GroupBy` conserva el orden de la fuente, asi
    ''' que mientras esto valga el arbol no tiene que reordenar nada al repoblar — y reordenaba los 4.473
    ''' NPC en CADA tecla del buscador, grupo por grupo.
    ''' <para>Todo lo que mueve la clave de un NPC —renombrarlo, revertir un override, borrar un record
    ''' nuevo— tiene que pasar por aca. Son acciones del usuario, una por vez; el buscador no.</para></summary>
    Private Sub OrdenarNpcs()
        _allNPCs.Sort(Function(a, b) String.Compare(ClaveDeOrden(a), ClaveDeOrden(b), StringComparison.OrdinalIgnoreCase))
    End Sub

    ''' <summary>Rehace los TRES caches que se derivan del record de un NPC: el texto que busca el
    ''' filtro, la etiqueta del nodo y la clave de orden.
    ''' <para>Van juntos porque salen de los mismos campos —FullName y EditorID— y porque el que se
    ''' olvide deja la lista mostrando una cosa y ordenando por otra. Lo llaman los dos caminos que
    ''' mutan un record ya cargado: el readback del Save y el editor de NPC.</para>
    ''' <para>⛔ Ese "los dos caminos" era MENTIRA hasta 2026-08-22: el readback RE-IMPLEMENTABA los tres
    ''' refrescos inline en vez de llamar acá, o sea la misma ley escrita dos veces. Fue por esa puerta
    ''' que entró el defecto del label viejo. Ahora sí son dos call sites de esta función.</para>
    ''' <para><paramref name="ordenarAhora"/> existe sólo para el readback, que procesa N NPC en un bucle:
    ''' ordenar por cada uno sería O(n log n) N veces. Pasa False y ordena UNA vez al salir. El default
    ''' es True para que el camino de a uno no se pueda olvidar de ordenar.</para></summary>
    ''' <para>El identificador sale SIEMPRE de `npc.FormID`: recibirlo aparte permitia que la etiqueta y
    ''' el texto buscable cayeran en una entrada y la clave de orden en OTRA, y entonces la lista se
    ''' ordenaria con una clave que no es la de ese NPC.</para>
    Private Sub RefrescarCachesDerivados(npc As NPC_Data, Optional ordenarAhora As Boolean = True)
        If npc Is Nothing Then Return
        Dim fid = npc.FormID
        _npcSearchableCache(fid) = NpcDisplayHelpers.BuildNpcSearchableText(npc)
        _npcDisplayLabelCache(fid) = NpcDisplayHelpers.BuildNpcDisplayLabel(npc)
        SembrarClaveDeOrden(npc)
        If ordenarAhora Then OrdenarNpcs()   ' la clave de este NPC acaba de cambiar
    End Sub

    Private _pendingTreeFilter As String = ""
    Private WithEvents SearchDebounceTimer As New System.Windows.Forms.Timer()

    ''' <summary>Advanced-filter facet resolver. Created the FIRST time a query actually carries a
    ''' facet token and never before: a session that only ever uses the plain search box pays nothing
    ''' for this feature — no memory, no record reads, no startup work. See NpcFilterIndex for the
    ''' rest of the cost model.</summary>
    Private _filterIndex As NpcFilterIndex = Nothing

    ''' <summary>Cache of NPC_Manager auto-generated plugins on disk (TES4.CNAM matches the
    ''' canonical author string). Populated lazily the first time the user opens the Save ESP
    ''' dialog, then invalidated/updated by the Save handler when a new plugin is written or
    ''' an existing one is updated. Avoids re-scanning Data\ (which can take 1-2 seconds with
    ''' many plugins) every time the dialog opens.
    ''' Nothing = not yet scanned. Empty list = scanned, no auto-gen plugins found.</summary>
    Private _autoGenPluginsCache As List(Of SaveEsp_Form.ExistingPlugin) = Nothing

    ' Deferred face tint application — the texture cache is async (Render.vb queues uploads
    ' and processes them per-frame), so when ApplyFaceTintOverlay runs right after RenderShapes
    ' the face diffuse texture may not be in Textures_Dictionary yet. The polling timer plus
    ' its state and attempts counter live on _renderHost so the editor previews (future phase)
    ' can run their own deferral without stomping the main preview.


    ''' <summary>Memoized chargen-TRI morph-name sets keyed by the normalized mesh-prefixed TRI path.
    ''' RenderCurrentStateAsync parses the face chargen TRI after each render to publish the morph
    ''' names EditFace uses for slider filtering + the Edit Face button gate. Most actors of a race
    ''' share the same TRI (BaseFemale/MaleHeadChargen.tri), so without this the same BA2 decompress +
    ''' TriHeadParser ran on the UI thread on every render. A miss (empty set) is cached too so a
    ''' missing TRI isn't re-probed. Same lifetime as the parse caches; cleared by
    ''' <see cref="InvalidateParseCaches"/>.</summary>
    Private ReadOnly _faceTriMorphNamesCache As New System.Collections.Concurrent.ConcurrentDictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Base-skeleton BYTE caches keyed by (race, gender), shared across the per-ARMA
    ''' PrepareSkeleton calls of a single render. PrepareSkeleton re-loads the HKX / BPTD / face
    ''' skeleton bytes for every distinct ARMA — but those bytes depend only on (race, gender), so
    ''' the loads are identical across ARMAs. Memoizing collapses N loads to 1. Same lifetime as the
    ''' parse caches above (dies with the load order; cleared by <see cref="InvalidateParseCaches"/>).
    ''' A Nothing value ("no such skeleton for this race/gender") is memoized natively by
    ''' ConcurrentDictionary.GetOrAdd, so a miss isn't re-loaded on the next ARMA.</summary>
    Private ReadOnly _skelHkxBytesCache As New System.Collections.Concurrent.ConcurrentDictionary(Of (Race As UInteger, Female As Boolean), Byte())()
    Private ReadOnly _skelBptdBytesCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Byte())()
    Private ReadOnly _skelFaceBytesCache As New System.Collections.Concurrent.ConcurrentDictionary(Of (Race As UInteger, Female As Boolean), Byte())()

    ''' <summary>Clear the FormID-keyed parse caches (RACE / HDPT) and the (race,gender) skeleton-byte
    ''' caches, plus the bulk-parsed NPC universe. Call whenever the PluginManager / FilesDictionary is
    ''' (re)built or the plugin set changes — otherwise a stale parse from the previous load order would
    ''' survive. The NPC cache is cleared too so a re-parse repopulates it from the current record set.</summary>
    Private Sub InvalidateParseCaches()
        ' Record-parse caches (ARMO/ARMA/RACE/HDPT/NPC_) live in the shared NpcRenderContext now;
        ' it clears all of them together. The skeleton-byte + chargen-TRI caches are render-specific
        ' and still owned here.
        _ctx.InvalidateParseCaches()
        _skelHkxBytesCache.Clear()
        _skelBptdBytesCache.Clear()
        _skelFaceBytesCache.Clear()
        _faceTriMorphNamesCache.Clear()
        ' Anim-clip cache is keyed by race FormID (+gender) — those can collide across plugin sets, so a
        ' reload must drop it too (consistent with the FormID-keyed caches above). This does NOT force a
        ' synchronous re-enumeration: within a fixed load order InvalidateParseCaches never runs again, and
        ' on an actual reload PreloadAnimRacesInBackground re-warms the distinct races OFF the UI thread
        ' right after ParseAllNPCs — the expensive ~16 s behavior walk never blocks the user.
        _animRaceCache.Clear()
        ' El memo por ARCHIVO de animacion vive en la libreria y es compartido entre razas; muere
        ' aca por la misma razon que el de arriba: la misma ruta puede resolver a otros bytes.
        BehaviorClipEnumerator.LimpiarMemoDeArchivos()
        ' Per-process TRI parse caches (Shared in the resolvers): same lifetime contract — kept across a
        ' session (no re-parse while browsing) but dropped on a load-order change so a stale parse from a
        ' path that now resolves to different bytes is discarded and the parsed geometry (MBs each) is freed.
        NpcMorphResolver.ClearCaches()
        BodySlideTriResolver.ClearCaches()
        ' Caches del compositor de facetint SSE (capas por raza, máscaras decodificadas por (path, tamaño) y
        ' fuentes por (path, target), CLFM): su propio comentario dice "call on FilesDictionary rebuild" pero
        ' NADIE lo llamaba — un reload del load order dejaba máscaras/capas stale de la sesión anterior. Mismo
        ' contrato que los ClearCaches de arriba.
        ' ACÁ se sueltan los CUATRO, incluidos los de RECORD (capas por raza, CLFM), porque un cambio de
        ' load order puede cambiar los récords mismos. Los dos de TEXTURA además tienen una vida MÁS CORTA
        ' encima de ésta: ClearFaceTintCaches los suelta en cada cambio de NPC raíz (ver
        ' SseFaceTintComposer.ClearTextureCaches) para que la app no acumule memoria navegando. Las dos vidas
        ' conviven: ésta es el techo, aquélla el piso.
        SseFaceTintComposer.ClearCaches()
    End Sub

    ' _renderHost.TintGpuCache, _renderHost.PristineDiffusePixels and the PristinePixels nested class moved to
    ' NpcRenderHost so each preview surface owns its own caches.

    ' HDPT.DATA mapea sus flags POR POSICIÓN dentro del array de bits, no por el valor
    ' literal. Posiciones reales:
    '   bit 0 (0x01) Playable, bit 1 (0x02) Male, bit 2 (0x04) Female,
    '   bit 3 (0x08) IsExtraPart, bit 4 (0x10) UseSolidTint, bit 5 (0x20) UsesBodyTexture.
    ''' <summary>NO USAR como gate del solid tint. Se conserva sólo por documentar el mapeo posicional
    ''' de los flags. El gate real, MEDIDO, es `HDPT.CNAM &lt;&gt; 0`: ninguna de las 5 HDPT con CNAM en vanilla+DLC
    ''' tiene este flag seteado y el CK usó el CNAM igual. Ver MainForm.MeshCandidate.UseSolidTint.</summary>
    Friend Const HeadPartFlagUseSolidTint As Byte = &H10
    ''' <summary>Flag bit at position 3 of HDPT.DATA — "Is Extra Part" (entry 4 of the flags'
    ''' positional array). Set on HDPTs that are
    ''' addons referenced via another HDPT's HNAM (eyelashes, hairlines, etc.) rather than
    ''' standalone parts. CharGenInterface.cpp:96 filters these out when serializing a preset.</summary>
    Private Const HeadPartFlagIsExtra As Byte = &H8
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeEyes As Integer = 2
    Friend Const HeadPartTypeHair As Integer = 3
    Friend Const HeadPartTypeFacialHair As Integer = 4
    Private Const HeadPartTypeHeadRear As Integer = 9

    ' Head-part occlusion is per-NPC and RACE-driven, NOT a fixed slot set: ResolvePreviewVariant computes
    ' the slot mask from the NPC's RACE.DATA biped objects via RaceUtil.RaceHeadOcclusionMask (engine-faithful
    ' — verified vs Fallout4.exe + .esm) and carries it on PreviewResolutionResult.HeadOcclusionMask;
    ' NpcRenderHost.ApplyRenderToggleVisibility reads that.

    Private Enum PreviewMode
        FullCharacter = 0
        OnlyFace = 1
    End Enum

    Friend Enum GenderFilterMode
        Random = 0
        Male = 1
        Female = 2
    End Enum

    Private ReadOnly Property CurrentGenderFilter As GenderFilterMode
        Get
            If ComboBoxGender.InvokeRequired Then
                Return CType(ComboBoxGender.Invoke(Function() ComboBoxGender.SelectedIndex), GenderFilterMode)
            End If
            Return CType(Math.Max(0, ComboBoxGender.SelectedIndex), GenderFilterMode)
        End Get
    End Property

    Friend Enum MeshCandidateKind
        Skin = 0
        Outfit = 1
        HeadPart = 2
        ''' <summary>Robot/animal chunk montado por AttachPoint socket (NPC_.OBTE path,
        ''' ChunkOmodFormID>0, SlotMask=0). Bypasea slot conflict resolution (mount via
        ''' socket, no via armor slot) y NO debe forzar SkinTint (ShouldForceSkinTint).</summary>
        Attachment = 3
    End Enum

    ''' <summary>Info por socket que publica un chunk host via BSConnectPoint::Parents, cacheada durante el
    ''' indexado. Incluye el global del socket EN EL ESPACIO DEL NIF DEL HOST (no en el del actor) para que el
    ''' consumidor componga <c>M_mesh_consumer = host.ChunkToActor x <see cref="HostSocketGlobalT"/></c> sin
    ''' mezclar sistemas de coordenadas.
    ''' <para>Con <see cref="ParentFoundInHostNif"/>=False (el parent no existe en el NIF del host) el consumidor
    ''' cae al fallback por skeleton (actor.parentBone x socket.local), que es lo apropiado para sockets que
    ''' referencian bones del skeleton del actor.</para></summary>
    Friend Class PublisherSocketInfo
        Public Socket As BSConnectPointReader.ConnectPointInfo
        ''' <summary>Transform global del socket dentro del NIF del host, computado como
        ''' <c>parentNiNode.GlobalTransform.Compose(socket.LocalAsTransform)</c>. Cuando el
        ''' parent name del socket está vacío, equivale al socket.Local respecto al root del
        ''' NIF (semántica engine de connect points sin parent explícito).</summary>
        Public HostSocketGlobalT As Transform_Class
        ''' <summary>True si el ParentBoneName del socket fue encontrado como NiNode en el
        ''' NIF del host (o si ParentBoneName="" → parent implícito = root del host NIF).
        ''' False = parent name no aparece en el NIF; consumer debe caer al path skeleton
        ''' fallback (actor.parentBone × socket.local), apropiado para sockets que referencian
        ''' bones del actor que NO existen en el host como NiNodes internos.</summary>
        Public ParentFoundInHostNif As Boolean
    End Class

    Friend Class MeshCandidate
        Public DictKey As String = ""
        Public SlotMask As UInteger
        ''' <summary>The ARMA's OWN biped-slot footprint (EquipResolver.ArmaGeometryMask), WITHOUT the owning ARMO's
        ''' head-occlusion gate bits that <see cref="SlotMask"/> also carries. The SSE skin-ARMA BOD2-ownership
        ''' de-dup keys on THIS, so a Feet(37) skin candidate isn't credited slot 30 just because its ARMO
        ''' (SkinNaked) declares head-occlusion bits — otherwise childfeet's EyesChild (partition 30) survives the
        ''' drop. 0 for non-ARMA candidates (heads/chunks/attachments).</summary>
        Public ArmaOwnSlotMask As UInteger
        ''' <summary>BOD2 del ARMO dueño, crudo. SSE: es la máscara que el engine usa para el conflicto de
        ''' EQUIP entre ítems del outfit — `0x1403BD39E` castea el ítem con `AsBipedObjectForm` (ARMO+0x1B0)
        ''' y compara con `SlotsOverlap 0x1401CCA90` (any-bit) contra lo ya equipado; si solapa, no lo equipa.
        ''' También es la que alimenta `GetWornMask 0x140225CB0` (OR de `[ARMO+0x1B8]`). NO confundir con
        ''' <see cref="ArmaOwnSlotMask"/> (los bits del armature, que gobiernan particiones) ni con
        ''' <see cref="SlotMask"/> (la mezcla de ambos). 0 para candidates sin ARMO.</summary>
        Public ArmoOwnSlotMask As UInteger
        Public Priority As Integer
        Public Kind As MeshCandidateKind
        Public SourceFormID As UInteger
        Public ArmorAddonFormID As UInteger
        Public MaterialSwapFormID As UInteger
        Public ColorRemapIndex As Nullable(Of Single)
        ''' <summary>OBTS/OMOD resolution for this candidate. Applied AFTER the ARMA-direct
        ''' MaterialSwapFormID/ColorRemapIndex base, so DirectProperties and Properties of
        ''' IncludedOmods can stack/override. Populated by:
        '''   - CollectArmoCandidates (humanoid path): formType=ARMO, shared across addon shapes.
        '''   - CollectRobotCandidates (NPC_.OBTE path): formType=NPC_, shared across chunks of
        '''     the chosen combination. Each chunk gets its own MeshCandidate but they all link
        '''     back to the same OmodResolution so Properties apply once at the actor level.</summary>
        Public OmodResolution As ObjectTemplateResolver.CombinationResolution = Nothing
        ''' <summary>For chunk MeshCandidates emitted by the NPC robot path: the FormType context
        ''' the OmodResolutionApplier should use ("ARMO" for humanoid, "NPC_" for robot). Drives
        ''' which PropertyIndex enum interprets each Property idx. Defaults to "ARMO" because
        ''' the humanoid path is the legacy default.</summary>
        Public OmodResolutionFormType As String = "ARMO"
        ''' <summary>OMOD chunk metadata for the robot path. Empty/zero for humanoid candidates.
        ''' AttachPointKywdEditorId is the resolved EditorID of OMOD.AttachPointFormID (matches
        ''' BSConnectPoint::Parents.Name in the actor skeleton). MountSocket carries the socket's
        ''' transform once resolved (Nothing if no socket matches; chunk falls back to origin).</summary>
        Public ChunkOmodFormID As UInteger
        Public AttachPointKywdEditorId As String = ""
        ''' <summary>AttachPointIndex carried from the OBTS Include that referenced this OMOD.
        ''' For multi-instance sockets (P-X|0/|1/|2 like Mr Handy arms or eyes) this picks which
        ''' indexed socket to mount on. For single-socket APs (P-X) it's typically 0 and the
        ''' resolver falls back to the unindexed name.</summary>
        Public MountApIdx As Byte = 0
        Public MountSocket As BSConnectPointReader.ConnectPointInfo = Nothing
        ''' <summary>Socket con alcance de ESQUELETO, para el Path B (chunks sin nodo C-X interno). Se
        ''' resuelve por nombre exacto contra los sockets que publica el esqueleto del actor.
        ''' <para>Existe separado del MountSocket porque los dos usan NOMENCLATURAS DISTINTAS: éste habla
        ''' en nombres del esqueleto del actor (con sufijo de instancia) y el publisher habla en nombres
        ''' internos del chunk, sin sufijo. Mezclarlos rompía los attachments multi-instancia, donde el chunk
        ''' dice un nombre de hueso que el actor sólo tiene indexado.</para>
        ''' <para><c>Nothing</c> cuando el esqueleto no publica ese socket; ahí el Path B cae al publisher
        ''' como último recurso.</para></summary>
        Public SkeletonFallbackSocket As BSConnectPointReader.ConnectPointInfo = Nothing
        ''' <summary>InstanceOrdinal único de ESTE candidate — identidad runtime real asignada
        ''' en expand-time por ObjectTemplateResolver (CollectOmodCandidate). Inmune a colisión
        ''' por reúso de OMOD asset bajo hosts distintos: cada expansión exitosa recibe ordinal
        ''' fresco del counter monotónico, antes de cualquier dedup. 0 = sentinel "skeleton root"
        ''' (no asignado a ningún candidate real; usado como hostOrdinal para chunks que mountean
        ''' en el actor root via initialApPool).</summary>
        Public ChunkInstanceOrdinal As Integer = 0
        ''' <summary>OMOD FormID del chunk publisher cuyo AP introduce este chunk al pool —
        ''' el "host inmediato" en el árbol de mounting OBTE. 0UI = host es el actor/skeleton
        ''' root (AP venía del initialApPool / NPC.APPR seed).
        '''
        ''' SOLO para logging legible. La identidad runtime del host vive en
        ''' <see cref="MountHostInstanceOrdinal"/>. La tuple (FormID, ApIdx) puede colisionar
        ''' bajo reúso teórico de asset; el ordinal no.</summary>
        Public MountHostOmodFormID As UInteger = 0UI
        ''' <summary>ApIdx del host instance (solo logging). Identidad runtime real:
        ''' <see cref="MountHostInstanceOrdinal"/>.</summary>
        Public MountHostApIdx As Byte = 0
        ''' <summary>InstanceOrdinal del host inmediato. 0 = skeleton root. Identidad runtime
        ''' real para el host chain walk — usado como key en hostChainMap / _candByOrdinal.</summary>
        Public MountHostInstanceOrdinal As Integer = 0
        ''' <summary>Transform chunk-to-actor de este chunk montado (= A computado por V2:
        ''' A = M_mesh × inv(G_CX)). Mapea coordenadas del NIF interno del chunk al actor
        ''' world space. Los consumers de este chunk (chunks montados via sockets que ESTE
        ''' publica) usan: M_mesh_consumer = host.ChunkToActor × HostSocketGlobalT, evitando
        ''' la mezcla de coord systems entre actor.parentBone y socket.local-en-chunk-frame.
        '''
        ''' Nothing = aún no calculado (pre-pass no corrió, o este candidate no es robot
        ''' mount). Los lookups de consumers que ven Nothing caen al path skeleton fallback.
        ''' Se popula en el pre-pass <c>PopulateRobotChunkChunkToActor</c> apenas A es derivable
        ''' (M_mesh + G_CX disponibles), ANTES del split actor-rig/module-rig de V2.</summary>
        Public ChunkToActor As Transform_Class = Nothing
        ''' <summary>FormID del host donde el resolver host-scoped efectivamente encontró el
        ''' socket de este consumer (puede ser distinto al MountHostOmodFormID inmediato si
        ''' el resolver walkeó la cadena hacia arriba). 0UI = socket vino del skeleton root
        ''' o no se encontró. SOLO para logging — identidad runtime real:
        ''' <see cref="MatchedHostInstanceOrdinal"/>.</summary>
        Public MatchedHostOmodFormID As UInteger = 0UI
        ''' <summary>ApIdx del matched host (solo logging). Identidad runtime real:
        ''' <see cref="MatchedHostInstanceOrdinal"/>.</summary>
        Public MatchedHostApIdx As Byte = 0
        ''' <summary>InstanceOrdinal del host donde el resolver host-scoped efectivamente encontró
        ''' el socket. Path A usa ESTE ordinal para buscar host.ChunkToActor via _candByOrdinal.
        ''' 0 = skeleton root (Path A no aplica).</summary>
        Public MatchedHostInstanceOrdinal As Integer = 0
        ''' <summary>HostSocketGlobalT del PublisherSocketInfo resuelto — global del socket
        ''' dentro del NIF del matched host. Path A: M_mesh = matchedHost.ChunkToActor × ESTE.
        ''' Nothing si el socket vino del skeleton (no aplica Path A).</summary>
        Public ResolvedHostSocketGlobalT As Transform_Class = Nothing
        ''' <summary>True si el ParentBoneName del socket fue encontrado como NiNode en el NIF
        ''' del matched host (o si el parent era vacío y se interpretó como root del host NIF).
        ''' False = consumer debe caer al path skeleton fallback en lugar de Path A.</summary>
        Public ParentFoundInMatchedHostNif As Boolean = False
        ''' <summary>Effective PartType: HDPT.PartType, falling back to the parent HNAM-chain
        ''' part type for sub-parts whose own PartType=0 (Misc). Used by skinning / FBNS
        ''' resolution / FaceTint candidacy where a Misc child of Face must behave Face-like.</summary>
        Public HeadPartType As Integer = -1
        ''' <summary>Raw PartType straight off the HDPT record (no HNAM-parent inheritance).
        ''' Used by ResolveTextureSet to gate the NPC.FTST fallback only to HDPTs that ARE
        ''' originally Face — Misc children that chain to Face via HNAM (MouthShadowFemale,
        ''' eye lashes/AO/wet) keep their authored material and don't inherit the head's
        ''' FTST diffuse (verified against Alijo vanilla CK bake).</summary>
        Public HeadPartTypeRaw As Integer = -1
        Public HeadPartColorFormID As UInteger
        Public TextureSetFormID As UInteger
        ''' <summary>HeadPart only: the HDPT record FormID this candidate was collected from.
        ''' Needed so the per-shape material pass can re-derive the ghoul head-rear bare-id gate
        ''' (FemaleHeadHumanRearTEMP 0x0004D0E9) without a separate lookup. 0 for non-HeadPart.</summary>
        Public HeadPartHdptFormID As UInteger
        ''' <summary>Gate del solid-tint por head part. CALCULADO sobre <see cref="HeadPartColorFormID"/>
        ''' (HDPT.CNAM), NO asignable: render y bake construyen el candidate en sitios distintos
        ''' (NpcMeshCollector.CollectHeadPartCandidate y FaceGenBuilder) y cada uno tenía su propia
        ''' definición — el render usaba el flag DATA 0x10 y el bake el CNAM. RENDER ≠ BAKE.
        ''' El gate MEDIDO es `CNAM &lt;&gt; 0`, NO el flag DATA 0x10 "Use Solid Tint": de las 5 HDPT con
        ''' CNAM&lt;&gt;0 en todo vanilla+DLC (pelo y hairline de Serana/Valerica) NINGUNA tiene el flag seteado
        ''' y el CK usó el CNAM igual — el corpus REFUTÓ el gate por flag. Ver la ley completa en
        ''' NpcMaterialResolver.ResolveHairTintColor y ResolveHeadPartSolidTintColor.
        ''' Al ser propiedad calculada la ley existe UNA SOLA VEZ y no puede volver a driftear.</summary>
        Public ReadOnly Property UseSolidTint As Boolean
            Get
                Return HeadPartColorFormID <> 0UI
            End Get
        End Property
        Public UsesBodyTexture As Boolean
        Public FaceGenTexturePrefix As String = ""
        Public Order As Integer
        ' From HDPT NAM0/NAM1 pairs (face head parts only, normally empty otherwise)
        Public RaceMorphTriPath As String = ""
        Public ChargenMorphTriPath As String = ""
        ' HDPT NAM0=1 "Tri" — the mesh's own morph tri (SkinnyMorph weight source). Its basename does NOT
        ' always match the mesh NIF (Hair08.nif → Elf\Male\ElfHair08.tri), so the record path is authoritative.
        Public MeshMorphTriPath As String = ""
        ''' <summary>Per-bone scale deltas from the ARMA's BSMP/BSMB/BSMS block (matching this
        ''' NPC's gender). Engine-side these are added on top of RACE.BSMS to shape the outfit
        ''' (cinched waist, wider hips, etc.). Nothing when the ARMA has no BSMS or gender mismatch.</summary>
        Public ArmaBoneScaleDeltas As List(Of ARMA_BoneScaleDelta) = Nothing
        ''' <summary>Head-bake: dictKey del hermano <c>_faceBones.nif</c>. <c>DictKey</c> es el NIF PLANO —que es
        ''' el que se dibuja, igual que el motor— y esto queda como INSUMO para <see cref="HeadBakeService"/>.
        ''' Vacío fuera de FO4 / cuando no hay <c>_faceBones</c>.</summary>
        Public FaceBonesDictKey As String = ""
        ''' <summary>True = collect into candidates for logging/inspection but exclude from render.
        ''' Set for HDPT type=7 Meatcaps (inner-mouth geometry occluded by teeth; vanilla CK declares
        ''' them but normally not visible in static pose). Filtered out in SelectWinningCandidates.</summary>
        Public Hide As Boolean = False
        ''' <summary>Skin candidates only (NPC_/RACE.WNAM body geometry): True cuando algún outfit
        ''' aceptado declara bits que solapan con este Skin (BODY/hands). Usado para RenderHide=True
        ''' por default — el outfit cubre visualmente al Skin, evita z-fighting. Se destapa cuando
        ''' "Render underarmor" se apaga (ver ApplyRenderToggleVisibility).</summary>
        Public IsCoveredByOutfit As Boolean = False
        ''' <summary>HeadPart candidates only: True cuando un headwear aceptado oculta este head
        ''' part por la occlusion matrix vanilla (Hair ocluido por HairTop/HairLong/FaceGenHead;
        ''' FacialHair ocluido por FaceGenHead/Beard/Mouth; Eyebrows por FaceGenHead; HeadRear por
        ''' HairTop AND (HairLong OR FaceGenHead)). Se acepta con flag (no se descarta) para que
        ''' "Render headwear OFF" pueda destaparlo runtime.</summary>
        Public IsOccludedByHeadwear As Boolean = False
        ''' <summary>HeadPart candidates only: True cuando el candidato fue colectado via la
        ''' recursión HNAM de un parent (parentPartType≥0 en CollectHeadPartCandidate), no como
        ''' entry top-level de NPC.PNAM/RACE defaults. Vanilla engine renderiza HNAM-extras aunque
        ''' un headwear oculte al parent — sólo FaceGenHead full-face los tapa. Independiente del
        ''' raw type del extra: el flag de "entró via HNAM" es el que determina la regla de
        ''' addon, no su PartType. Contraejemplo: un hair cuya HNAM declara una hairline raw=3
        ''' (no Misc) — sin este flag cae en la rama Hair de occlusion y la gorra lo oculta.</summary>
        Public IsHnamExtra As Boolean = False
        ''' <summary>Hair candidates {30,31} only: qué particiones (Top=v30−v31, Long=v31−v30) se ZAPEAN
        ''' este render, según el modelo complementario main/hairline (ver <see cref="HairZapParts"/>).
        ''' MAIN bajo gorra [30] → Top; MAIN bajo casco [30,31] cae en IsOccludedByHeadwear (hide entero).
        ''' HAIRLINE bajo gorra [30] → Long (su top forehead queda); HAIRLINE bajo casco [30,31] → None
        ''' (under-helmet, entera visible). Mutuamente excluyente con IsOccludedByHeadwear (full-mask 32
        ''' u cobertura total del main). Plumbed a result.ShapeZapHairParts y consumido por
        ''' HairTopZapResolver (render) + ButtonSaveSceneNif_Click (export compacta los zapeados).</summary>
        Public ZapParts As HairZapParts = HairZapParts.None
    End Class

    Friend Class PreviewVariantDefinition
        Public RootNpcFormID As UInteger
        Public VariantId As Integer
        Public DisplayName As String = ""
        Public State As NPCVisualState
        Public UseFaceGen As Boolean
        ''' <summary>When True, <see cref="CollectMeshCandidates"/> skips Skin and Outfit
        ''' (only HeadParts enter the pipeline). Same mechanism the MainForm "Only Face"
        ''' PreviewMode triggers; the editor host sets this True for its renders.</summary>
        Public OnlyFaceCollect As Boolean = False
        ''' <summary>When True, <see cref="CollectMeshCandidates"/> collects ONLY the outfit (skips Skin,
        ''' HeadParts and robot chunks) — used by the Edit Outfit "selected piece only" preview.</summary>
        Public OnlyOutfitCollect As Boolean = False
        ''' <summary>Preview-only: collect THIS ARMA even if its race doesn't match the actor's
        ''' (<see cref="NpcRenderHost.RaceFilterBypassArmaFormID"/>). 0 = the engine rule applies to every ARMA.</summary>
        Public RaceFilterBypassArmaFormID As UInteger = 0UI
        Public ReadOnly Warnings As New List(Of String)
    End Class

    Friend Class TemplateDependencyEdge
        Public SourceFormID As UInteger
        Public DependentNpc As NPC_Data
        Public Categories As New List(Of String)
    End Class
    ''' <summary>Clasifica un BSSubIndexTriShape sub-segment según su userSlotID para determinar
    ''' si es geometría de "meatcap" — la cara interna del corte que sólo debe verse cuando la
    ''' parte del cuerpo fue severed. Vanilla FO4 oculta estas shapes hasta que el dismemberment
    ''' system las activa; en preview estático las ocultamos siempre, igual que HDPT type=7.
    ''' Rangos: 101..113 / 201..213 vienen del enum BSDismemberBodyPartType (nif.xml, certeza
    ''' estructural); 100/102/103 solo aparecen etiquetados "Gore" en el .xrc de BS-OS, por eso van
    ''' como Tentative y son removibles si aparece evidencia contraria.</summary>
    Public Enum MeatcapClassification
        ''' <summary>userSlotID = 0 (no slot) o cualquier valor fuera de los rangos de gore/cap.
        ''' Geometría visible normal del cuerpo/outfit/etc.</summary>
        Normal = 0
        ''' <summary>userSlotID ∈ {101..113, 201..213}. SECTIONCAP/TORSOCAP del enum oficial
        ''' BSDismemberBodyPartType. Identificación 100% spec del NIF.</summary>
        Confirmed = 1
        ''' <summary>userSlotID ∈ {100, 102, 103}. "Gore" según .xrc de BS-OS pero NO en el enum
        ''' NIF ni en docs de Bethesda. A confirmar con más NPCs / reverseo del motor.</summary>
        Tentative = 2
    End Enum

    ''' <summary>Categoría de un shape para los toggles diagnósticos de visibilidad. Calculada
    ''' al cargar el shape a partir del MeshCandidate.SlotMask + Kind. Los handlers de los
    ''' CheckBoxes setean RenderHide según esta categoría sin re-resolver candidates.</summary>
    Public Enum ShapeRenderCategory
        ''' <summary>Sin clasificar (accessories sin slot, etc). Siempre visible.</summary>
        Other = 0
        ''' <summary>Over-armor [A] puro: declara algún bit [A] (11-15) y NO toca BODY/[U].</summary>
        ArmorOver = 1
        ''' <summary>Underarmor outfit: Kind=Outfit con BODY (bit 3) o [U] (bits 6-10). Ropa que
        ''' va debajo del armor (AAClothesCait, fatigues, etc.). Controlado por "Render underarmor".</summary>
        Underarmor = 2
        ''' <summary>Naked hands: Kind=Skin con bits hand (4/5) sin BODY. Piel de manos desnudas.
        ''' Se oculta junto con el resto del body skin via "Render body".</summary>
        NakedHands = 3
        ''' <summary>Glove de outfit: Kind=Outfit con bits hand (4/5) y sin BODY/[U]. Independiente
        ''' del cuerpo desnudo — sigue las reglas de outfit, no se toggle como body.</summary>
        GloveOutfit = 4
        ''' <summary>Head part (Kind=HeadPart). Forma parte del NPC desnudo — controlado por "Render body".</summary>
        HeadPart = 5
        ''' <summary>Body skin: Kind=Skin con bit BODY (3). Cuerpo desnudo del NPC (torso+piernas+pies
        ''' en FO4 vanilla — no hay slot feet). Controlado por "Render body".</summary>
        BodySkin = 6
        ''' <summary>Headwear: Kind=Outfit con bits exclusivos de cabeza/cara (HairTop/HairLong/
        ''' FaceGenHead/Headband/Eyes/Beard/Mouth) y SIN tocar BODY/[U]/[A]/hand. Helmets, caps,
        ''' glasses, bandanas, masks. Controlado por "Render headwear".</summary>
        Headwear = 7
    End Enum

    Friend Class PreviewResolutionResult
        Public ReadOnly Shapes As New List(Of IRenderableShape)
        Public SkeletonKey As String = ""
        Public ReadOnly Warnings As New List(Of String)
        ''' <summary>Shape reference -> mesh dictionary key path (for TRI file lookup).</summary>
        Public ReadOnly MeshDictKeys As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Camino head-bake (gate ON): shape del NIF PLANO -> dictKey de su hermano
        ''' <c>_faceBones.nif</c>, que es el INSUMO del cálculo (nunca se dibuja, igual que en el motor).
        ''' Vacío con el gate OFF. Lo consume <c>BuildRenderPlan</c> para armar el <see cref="HeadBakeService"/>.</summary>
        Public ReadOnly ShapeFaceBonesKeys As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> chargen morph TRI path (from HDPT NAM0=2/NAM1).</summary>
        Public ReadOnly ShapeChargenTriPaths As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> race morph TRI path (from HDPT NAM0=0/NAM1, expression file).</summary>
        Public ReadOnly ShapeRaceMorphTriPaths As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> mesh morph TRI path (from HDPT NAM0=1/NAM1). SkinnyMorph weight source;
        ''' basename may differ from the mesh NIF, so this is the authoritative path (vs ChangeExtension guess).</summary>
        Public ReadOnly ShapeMeshMorphTriPaths As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Per-shape ARMA sculpt data lookup. Key = shape reference, Value = bone-name → Vec3 delta
        ''' (delta = sclp_absolute - 1.0). Each shape carries the sculpt of ITS ARMA owner only — there is
        ''' no cross-ARMA aggregation. Shapes whose ARMA has no sculpt data are absent from this dictionary.
        ''' Render-time: each shape gets a SkeletonInstance with its own sculpt applied (or none if absent),
        ''' generic for any ARMA — no special-casing for body/outfit/gloves/etc.</summary>
        Public ReadOnly ShapeArmaSculpt As New Dictionary(Of IRenderableShape, Dictionary(Of String, System.Numerics.Vector3))
        ''' <summary>Per-shape ARMA owner FormID (0 if shape has no ARMA, e.g. head parts). Used to
        ''' group shapes by sculpt source so we build one SkeletonInstance per distinct ARMA with sculpt.</summary>
        Public ReadOnly ShapeArmaFormID As New Dictionary(Of IRenderableShape, UInteger)
        ''' <summary>Per-shape categoría para toggles diagnósticos de visibilidad. Ver
        ''' ApplyRenderToggleVisibility.</summary>
        Public ReadOnly ShapeCategory As New Dictionary(Of IRenderableShape, ShapeRenderCategory)
        ''' <summary>Per-shape: True cuando el shape proviene de un Skin candidate cubierto por
        ''' algún outfit aceptado (sus bits BODY/hand chocan con bits de outfits que ganaron).
        ''' Usado por ApplyRenderToggleVisibility para decidir RenderHide inicial: con
        ''' "Render underarmor" ON el Skin cubierto se oculta (el outfit lo tapa visualmente);
        ''' con "Render underarmor" OFF el Skin cubierto se destapa.</summary>
        Public ReadOnly ShapeCoveredByOutfit As New Dictionary(Of IRenderableShape, Boolean)
        ''' <summary>Per-shape: True cuando el shape proviene de un HeadPart ocluido por algún
        ''' headwear aceptado (occlusion matrix vanilla — pelo bajo casco, etc.). Usado por
        ''' ApplyRenderToggleVisibility: con "Render headwear" ON el head part ocluido se oculta;
        ''' OFF lo destapa para mostrar el pelo/barba/etc bajo el headwear oculto.</summary>
        Public ReadOnly ShapeOccludedByHeadwear As New Dictionary(Of IRenderableShape, Boolean)
        ''' <summary>Per-shape: the OWN worn biped-slot mask of the candidate this shape came from
        ''' (bit N-30 = biped slot N, same convention as <see cref="EquipResolver.EquipResolution.OccupiedSlots"/>).
        ''' Stored only for worn-item (Kind=Outfit) shapes; head parts / skin have no entry (own slots = 0).
        ''' ApplyRenderToggleVisibility rebuilds the per-segment occlusion mask (IRenderableShape.CoveredSlotsMask)
        ''' from these every apply, scoped to the items CURRENTLY rendered — so a render toggle that hides an
        ''' item (e.g. Pipboy under "Render armor" OFF) drops its slots from the occluding set and the segments
        ''' it covered re-appear. NOT a static covered mask: covered-by-OTHERS is recomputed at toggle time
        ''' (ORDER / other-items rule), excluding the shape's own group via <see cref="ShapeSlotGroup"/>.</summary>
        Public ReadOnly ShapeOwnSlots As New Dictionary(Of IRenderableShape, UInteger)
        ''' <summary>Per-shape BOD2 del ARMATURE (ARMA) dueño, sin los bits que sólo declara el ARMO. SSE:
        ''' es la máscara que gobierna qué particiones de cabeza oculta el ítem del slot de pelo — el writer
        ''' de la tabla del biped (0x1402134E0) guarda el ARMA en `entry+0x18` recorriendo los bits del ARMA,
        ''' y la fase 2 de 0x140218200 agrupa por ese puntero. Ver NpcMeshCollector.HeadPartHideMask.</summary>
        Public ReadOnly ShapeArmaOwnSlots As New Dictionary(Of IRenderableShape, UInteger)
        ''' <summary>Per-shape DNAM priority (Male/Female, gender-resuelto) del ARMA dueño. SSE: en empate de
        ''' biped-slot decide qué ítem lo POSEE para la oclusión per-partición (fase 1 de 0x140218200 — el
        ''' owner de cada slot en `entry+0x18`). Seteado sólo para worn items (Kind=Outfit), igual que
        ''' ShapeArmaOwnSlots. Ver NpcRenderHost.ApplyRenderToggleVisibility (mapa de dueños por-slot SSE).</summary>
        Public ReadOnly ShapePriority As New Dictionary(Of IRenderableShape, Integer)
        ''' <summary>Per-shape occlusion group id (one per candidate; all shapes of a candidate share it).
        ''' Used by ApplyRenderToggleVisibility so an item never occludes its OWN segments: covered-by-others
        ''' ORs the own-slot masks of rendered groups whose id differs (engine owner-slot branch 0x14035E22B).
        ''' This is what keeps a slot SHARED by two items (Pipboy + a Pipboy-aware outfit both declaring 60)
        ''' working — occupied&amp;~own would strip the shared bit; OR-of-other-groups keeps it.</summary>
        Public ReadOnly ShapeSlotGroup As New Dictionary(Of IRenderableShape, Integer)
        ''' <summary>Per-shape: True when the shape's candidate IS the Pipboy DEVICE, by ENGINE IDENTITY —
        ''' its ARMO FormID matches one of the Pipboy default objects (see
        ''' <see cref="NpcRenderContext.PipboyDeviceArmoFormIDs"/>, resolved from the PipboyCleanObject_DO /
        ''' PipboyDustyObject_DO DFOBs at VA 0x1400F18B0 / 0x1400F18F0). Replaces the old slot heuristic
        ''' ("its only worn slot is 60"), which wrongly flagged AssaultronShield (0022BC24), MirelurkShield
        ''' (000986CA) and babybundled (000F468E) — 3 of the 7 vanilla slot-60-only ARMOs are NOT Pipboys.
        ''' Absent entry = not a Pipboy device.</summary>
        Public ReadOnly ShapeIsPipboyDevice As New Dictionary(Of IRenderableShape, Boolean)
        ''' <summary>The biped slot THIS NPC's race reserves for the Pipboy, as a slot-30-relative bit mask,
        ''' from RACE.DATA 'Pipboy Biped Object' via
        ''' <see cref="RaceUtil.RacePipboyMask"/>. The Pipboy slot is PER-RACE data, so the coexist-by-design
        ''' strip in NpcRenderHost.ApplyRenderToggleVisibility must strip THIS bit, not the constant slot 60.
        ''' 0 when the race declares None, and always 0 on Skyrim (no such field; slot 60 is generic there).</summary>
        Public PipboySlotMask As UInteger = 0UI
        ''' <summary>Monotonic seed for <see cref="ShapeSlotGroup"/> ids. LoadNifShapes runs once per
        ''' candidate; it claims one id per call via the post-increment below.</summary>
        Public OcclusionGroupSeq As Integer = 0
        ''' <summary>Per-NPC, RACE-driven head-part occlusion slot mask (slot-30-relative): the union of the
        ''' face-cull (A), hair (B), and facial-hair (C) biped objects declared in this NPC's RACE.DATA, via
        ''' <see cref="RaceUtil.RaceHeadOcclusionMask"/>. Computed once in ResolvePreviewVariant where the race
        ''' is in scope, and consumed by NpcRenderHost.ApplyRenderToggleVisibility to slice the rendered
        ''' worn-slot set down to the head region (replaces the old fixed HeadwearOcclusionSlots const, which
        ''' was wrong for non-human races). For HumanRace this resolves to {30,31,32,48}.</summary>
        Public HeadOcclusionMask As UInteger = 0UI
        ''' <summary>A: face-cull biped object [race+0x12C] (RACE DATA+0x44, engine master 0x1403BB880). Si el
        ''' worn set cubre este slot ⇒ whole-node cull de la cabeza (`or [headNode+0xF4],1`) — cascadea a TODO
        ''' head-part. NO es per-partición, así que NO forma parte de la máscara CoveredSlotsMask del render.</summary>
        Public HeadFaceCullMask As UInteger = 0UI
        ''' <summary>B: hair biped object [race+0x130] (RACE DATA+0x48, engine 0x1403BB880 / attach 0x140218200
        ''' fase 2). Es el slot de pelo (bit único en SSE). El ítem que ocupa este slot oculta, en el subárbol
        ''' de la cabeza, las particiones de TODO su BOD2 (ver NpcMeshCollector.HeadPartHideMask). Se combina
        ''' render-time con los BOD2 de los ítems renderizados para producir la máscara per-partición.</summary>
        Public HeadHairSlotMask As UInteger = 0UI
        ''' <summary>Per-shape: qué particiones de un Hair {30,31} se ZAPEAN este render (Top=v30−v31,
        ''' Long=v31−v30), según el modelo complementario main/hairline (ver <see cref="HairZapParts"/>).
        ''' A diferencia de ShapeOccludedByHeadwear (oculta la mesh entera), esto zapea SÓLO los vértices
        ''' de la(s) partición(es) indicada(s). Consumido por HairTopZapResolver (emite un canal de zap
        ''' con la unión de los vertex-sets) y por ButtonSaveSceneNif_Click (compacta los vértices
        ''' zapeados al exportar, agnóstico a la partición). Gated por "Render headwear": OFF descarta el
        ''' zap (BuildCompositeMorphResolver no engancha el resolver) y la mesh se destapa entera, igual
        ''' que el resto de la oclusión. Shapes ausentes / con valor None no tienen zap.</summary>
        Public ReadOnly ShapeZapHairParts As New Dictionary(Of IRenderableShape, HairZapParts)
        ''' <summary>Per-shape: clasificación de meatcap. Confirmed (enum NIF SECTIONCAP/TORSOCAP)
        ''' o Tentative (BS-OS-only, userSlotID 100/102/103) → la shape es geometría interna del
        ''' corte que sólo se ve post-dismemberment. ApplyRenderToggleVisibility la oculta por
        ''' default (igual que HDPT type=7 Meatcaps, que ya se filtran en SelectWinningCandidates).
        ''' Shapes ausentes del dict o con valor Normal son geometría visible regular.</summary>
        Public ReadOnly ShapeMeatcap As New Dictionary(Of IRenderableShape, MeatcapClassification)
        ''' <summary>Per-shape: True iff the shape's owning candidate had UsesBodyTexture=True
        ''' (HDPT.DATA flag 0x40, post CBBE-style override fix). Lets the fast-path
        ''' (RefreshBodySkinLivePreview) know which HeadPart shapes pull their diffuse from the
        ''' actor's body skin TXST and therefore need a re-resolve when state.SkinFormID changes.
        ''' Without this, ghoul/synth/etc. NPCs whose CBBE override forced UsesBodyTexture=True
        ''' would render the OLD body diffuse on the headRear after a WNAM combo change.</summary>
        Public ReadOnly ShapeUsesBodyTexture As New Dictionary(Of IRenderableShape, Boolean)
        ''' <summary>DEPRECATED. Used to be the cross-ARMA aggregated sculpt; now superseded by the
        ''' per-shape mapping above. Kept as always-empty for back-compat with consumers that read it.</summary>
        Public ReadOnly ArmaBoneScaleDeltas As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
        ''' <summary>Per-shape mount socket info para robot chunks (chunks emitidos por
        ''' CollectRobotChunkCandidates con MountSocket resuelto via ConnectPointMountResolver).
        ''' Ausente para shapes humanoides / chunks sin socket. Consumido por PrepareSkeleton
        ''' para inyectar bones internos del chunk anchored al socket bone (ver
        ''' BSConnectPointBoneInjector_Class.InjectChunkBonesIntoLiveSkeleton).</summary>
        Public ReadOnly ShapeMountSocket As New Dictionary(Of IRenderableShape, BSConnectPointReader.ConnectPointInfo)
        ''' <summary>Per-shape NIF del chunk de origen (mismo NIF para todas las shapes del mismo
        ''' chunk). Necesario para el injector porque la jerarquía de bones internos vive en el
        ''' chunk NIF — sin él no hay forma de recurrir el parent chain.</summary>
        Public ReadOnly ShapeChunkNif As New Dictionary(Of IRenderableShape, Nifcontent_Class_Manolo)
        ''' <summary>Per-candidate NIF — populated by LoadNifShapes. Used by the mount pass to
        ''' map a candidate to ITS shapes (multi-instance robot chunks: 3 arm candidates share
        ''' DictKey but each parses its own fresh NIF, so identity comparison on the NIF reaches
        ''' only the shapes of THAT candidate). Reference equality is intentional.</summary>
        Public ReadOnly CandidateNif As New Dictionary(Of MeshCandidate, Nifcontent_Class_Manolo)
        ''' <summary>Per-shape → owning MeshCandidate. Populated by LoadNifShapes at NIF-load
        ''' time. Used by paths that mutate material state per-shape but only have access to the
        ''' shape (e.g. RefreshFaceTintLivePreview, where the user-facing edit refreshes color
        ''' uniforms on every mesh and needs candidate.HeadPartType to gate hair color
        ''' resolution correctly per shape). Without this dict the live-edit path was forced to
        ''' iterate model.meshes blindly and apply hair color to any palette-enabled material —
        ''' which leaked hair color into robot armor / face / body shapes with palette opt-in.</summary>
        Public ReadOnly ShapeCandidate As New Dictionary(Of IRenderableShape, MeshCandidate)
        ''' <summary>Plan de mount: los (boneName, desiredWorld W_B) que V2 computó durante el shape
        ''' loop. El aplicador canónico (<see cref="ApplyMountPlanForActor"/>) escribe la capa
        ''' <c>MountDeltaTransform</c> de cada hueso (<see cref="OverrideActorBoneWorld"/>) en orden
        ''' topológico. La animación HKX no pelea con el mount: el Δ se computa contra el clipBase
        ''' medido del clip (HkxPoseImportSession).</summary>
        Public ReadOnly MountDesiredWorlds As New List(Of MountDesiredWorldEntry)
    End Class

    ''' <summary>Entry del plan de mount: el desiredWorld (W_B) que V2 computó por bone.
    ''' <para><c>TargetSkel</c> ata el entry a la SkeletonInstance contra la que se computó (per-ARMA
    ''' clones no reciben entries del base por bone-name collision).</para></summary>
    Public Class MountDesiredWorldEntry
        Public BoneName As String
        Public DesiredWorld As Transform_Class
        Public ContextLabel As String
        Public TargetSkel As SkeletonInstance
    End Class
    Friend Class TraitsState
        ''' <summary>FormID of the NPC_ record this Traits bucket was resolved FROM — own FormID for a
        ''' non-inheriting NPC, or the terminal template source when Use Traits walked the chain. Lets
        ''' the face appearance reads (tint/morphs) pull from the inherited source, mirroring how
        ''' HeadPartFormIDs already travel in this state. Propagated to NPCVisualState.TraitsSourceFormID.</summary>
        Public SourceFormID As UInteger
        Public IsFemale As Boolean
        Public RaceFormID As UInteger
        Public SkinFormID As UInteger
        ''' <summary>Raw NPC.MWGT slots — Nothing means the slot was the engine "Default" sentinel
        ''' (Single.MaxValue). Only the body-weight resolver should consume these; everywhere else
        ''' should read NPCVisualState.WeightX after ApplyRaceFallbacks materializes them.</summary>
        Public WeightThin As Single?
        Public WeightMuscular As Single?
        Public WeightFat As Single?
        ' [TEST: TPLT-traits-bucket] Face-appearance fields live in the Traits bucket: the template
        ' flags total 15 bits but don't pin each NPC_ subrecord to a specific bit, so these ride
        ' "Use Traits" — the CK Traits tab covers the actor's visual identity (race, skin, head, hair),
        ' same conceptual bucket as RNAM/WNAM/MWGT.
        Public HeadTextureFormID As UInteger
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        Public TextureLightingColor As Color = Color.Empty
        Public HeadPartFormIDs As New List(Of UInteger)
        ' [TEST: TPLT-traits-bucket] NPC ObjectTemplate (OBTE/OBTS) rides "Use Traits" (bit0), NOT
        ' "Use Model/Animation" (bit6): GutsyTemplateProbe over all 4365 load-order NPC_ shows ZERO
        ' inherit OBTS via bit6; the rank variants (encMrGutsy02/03/04, SentryBot/Assaultron/Synth
        ' encounters) reach the OBTS holder ONLY via bit0 — 225 NPCs go from empty→rendered, 0
        ' regressions. Matches the CK, where "Object Template" lives on the Traits tab.
        ''' <summary>Legacy flat list of OMOD FormIDs from combo #0 (kept for back-compat).</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
        ''' <summary>Full OBTE/OBTS combinations — used by the robot-chunk path resolver.</summary>
        Public ObjectTemplateCombinations As New List(Of FO4_Base_Library.Canon.IBloque_Combinations)
        ''' <summary>True when source NPC_ had an OBTE present.</summary>
        Public HasObjectTemplate As Boolean = False
        ''' <summary>NPC.APPR (Attach Parent Slots) — list of AP keywords the actor exposes
        ''' as the initial pool for OBTE OMOD AP-filter. Brahmin: [ap_HornsL, ap_HornsR, ap_PackBase];
        ''' Codsworth: presumed empty/different — AP filter only applies when NPC.APPR != empty.</summary>
        Public AttachParentSlotFormIDs As New List(Of UInteger)
    End Class

    Friend Class InventoryState
        Public DefaultOutfitFormID As UInteger
        Public SleepOutfitFormID As UInteger
    End Class

    Friend Class NPCVisualState
        Public FormID As UInteger
        Public RootNpcFormID As UInteger
        Public TraitsSourceFormID As UInteger
        Public InventorySourceFormID As UInteger
        Public ModelSourceFormID As UInteger
        Public VariantLabel As String = ""
        Public IsFemale As Boolean
        Public RaceFormID As UInteger
        Public SkinFormID As UInteger
        Public DefaultOutfitFormID As UInteger
        Public SleepOutfitFormID As UInteger
        Public HeadTextureFormID As UInteger
        ' FTST PROPIO del NPC (NPC_.FTST), capturado ANTES del fallback DFTM que pisa HeadTextureFormID en
        ' BuildNPCVisualState. Distingue "el NPC declara su cara" (FTST) del default de raza (DFTM), para la
        ' precedencia FTST > HDPT.TNAM > DFTM en ResolveTextureSet. 0 = el NPC no tiene FTST propio.
        Public ExplicitHeadTextureFormID As UInteger
        ''' <summary>FO4 SÓLO. ACBS\Flags bit 0x01000000 "Diffuse Alpha Test" del
        ''' NPC. Es el ÁRBITRO record-driven del alpha de la cabeza: el CK fabrica el NiAlphaProperty (0x2EC) sii
        ''' este flag (RE CreationKit 0x140ED41F6 → gate [npc+0x9b]&1 = bit 24 de ACBS). Reemplaza el proxy
        ''' isNpcExplicitFaceTextureSet, que fallaba para Valentine (su material con alpha viene por MSWP, no por
        ''' el TXST del FTST, así que el FormID no coincidía) y daba falso-positivo si el RACE.DFTM apuntaba a un
        ''' material con alpha (DiMA: DFTM=SkinHeadValentine con alpha, PERO sin este flag ⇒ sólido, como el CK).
        ''' Gobierna render (HasAlphaTest), bake (WriteAlphaPropertyToShape) y el compositor del _d. Ver
        ''' 40-bake-leyes-fo4. SSE: bit 24 = "Unknown 24" (sin uso) ⇒ siempre False.</summary>
        Public HeadDiffuseAlphaTest As Boolean
        Public HairColorFormID As UInteger
        ''' <summary>SSE-ONLY RaceMenu absolute hair tint (packed 0xRRGGBB) from an applied .jslot's actor.hairColor.
        ''' When set it takes precedence over <see cref="HairColorFormID"/> (the CLFM) at hair-material resolution
        ''' (ResolveHairTintColor), matching skee's ApplyMappedPreset. Nothing = fall back to the CLFM. SSE-only.</summary>
        Public SseHairColorRgb As Integer?
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        ''' <summary>QNAM RGBA. Alpha is the body SoftLight intensity (vanilla = 1.0 by convention,
        ''' synced with the slot-12 SkinTone tint layer's Value). When the editor mutates slot-12
        ''' Value or Color, ResolveNpcSkinToneColor packs the new (Color, Value/100) back here so
        ''' face and body stay symmetric — the engine itself reads QNAM.A as the body softlight
        ''' opacity (QNAM 'Texture lighting' is an RGBA float struct), so this
        ''' preserves engine-fidelity while reflecting the user's edit.</summary>
        Public TextureLightingColor As Color = Color.Empty
        ''' <summary>Ajuste manual del skin tone del CUERPO autorado en Edit Body (overlay -> state). YA ESTA
        ''' APLICADO dentro de <see cref="TextureLightingColor"/>; se guarda ademas crudo porque el refresh en
        ''' vivo re-resuelve el tono desde el record y necesita volver a sumarlo. NUNCA se aplica a la resolucion
        ''' que consume la CARA (ResolveNpcSkinToneColor): el punto de la feature es que el origen del match no
        ''' se mueva. Nothing = sin ajuste.</summary>
        Public SkinToneOffset As SkinToneQnamOffset = Nothing
        Public HeadPartFormIDs As New List(Of UInteger)
        Public LoadoutArmorFormIDs As New List(Of UInteger)
        ''' <summary>Per-ARMO contextual keywords inherited from the LVLI.LLKC chain at outfit
        ''' sample time. Used by CollectArmoCandidates to match OBTS combinations and apply
        ''' OMOD AddonIndex Property swaps (Lite/Mid/Heavy). Empty for ARMOs that didn't pass
        ''' through any LLKC during sampling.</summary>
        Public LoadoutArmorContextKeywords As New Dictionary(Of UInteger, List(Of UInteger))
        Public WeightThin As Single
        Public WeightMuscular As Single
        Public WeightFat As Single
        ''' <summary>OMOD FormIDs from NPC_.ObjectTemplate combination #0 (robot body parts).
        ''' Legacy flat list — kept for back-compat with old robot-path code that hasn't
        ''' migrated yet. New code uses ObjectTemplateCombinations + ObjectTemplateResolver.</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
        ''' <summary>Full NPC_.OBTE/OBTS combinations (every header + payload) — fed into
        ''' ObjectTemplateResolver.ResolveNpcCombinations to pick the engine-applied
        ''' combination and walk its Includes/Properties recursively.</summary>
        Public ObjectTemplateCombinations As New List(Of FO4_Base_Library.Canon.IBloque_Combinations)
        ''' <summary>True when the source NPC_ had OBTE present. Robot path activates only if so.</summary>
        Public HasObjectTemplate As Boolean = False
        ''' <summary>NPC.APPR — Attach Parent Slots declared at the actor level. Seeds the
        ''' AP-pool filter in ObjectTemplateResolver. Brahmin: [ap_HornsL, ap_HornsR, ap_PackBase].</summary>
        Public AttachParentSlotFormIDs As New List(Of UInteger)
    End Class

    Private ReadOnly Property CurrentPreviewMode As PreviewMode
        Get
            If ComboBoxPreviewMode.InvokeRequired Then
                Return CType(ComboBoxPreviewMode.Invoke(Function() ComboBoxPreviewMode.SelectedIndex), PreviewMode)
            End If
            Return CType(Math.Max(0, ComboBoxPreviewMode.SelectedIndex), PreviewMode)
        End Get
    End Property

    Private Sub ComboBoxPreviewMode_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxPreviewMode.SelectedIndexChanged
        If _renderHost Is Nothing Then Return
        ' Re-render the currently selected node with new preview mode
        If _renderHost.CurrentBaseState Is Nothing Then Return
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    ''' <summary>Toggle the FMRS face bone pose in-place without re-extracting geometry.
    ''' WM-style granular pipeline: mutate the existing RenderIntent, flip Intent.Pose, mark
    ''' RenderDirtyFlags.Pose, InvalidateRender. The pipeline's needsPoseUpdate path re-runs
    ''' the skeleton step and recomputes bone matrices — it does NOT reload shapes.
    '''
    ''' When toggling OFF we pass an EMPTY Poses_class (not Nothing) so that
    ''' FaceBoneSkeletonResolver actually calls AppplyPoseToSkeleton — which runs Reset()
    ''' first (clearing all DeltaTransforms) and then iterates an empty dict. Passing
    ''' Nothing would make the resolver's "If pose IsNot Nothing" guard skip the reset
    ''' and leave the previous deltas pegged on the SkeletonDictionary.</summary>
    Private Sub CheckBoxApplyBoneMorphs_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBoneMorphs.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        RebuildAndApplyMergedPose()
    End Sub

    ''' <summary>Toggle face FRTRI003 vertex morphs only. Body PIRT morphs are toggled
    ''' independently by CheckBoxBodyTri. The composite is rebuilt every time so the granular
    ''' gates inside (face=this checkbox, body=CheckBoxBodyTri) reflect the latest state.</summary>
    Private Sub CheckBoxApplyVertexMorphs_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyVertexMorphs.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        If _renderHost.LastRenderedState Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then
            Return
        End If
        Dim newResolver = BuildCompositeMorphResolver(_renderHost.LastRenderedState, _renderHost.LastRenderData)
        Dim intent = _previewControl.Intent
        intent.MorphResolver = newResolver
        intent.MarkDirty(RenderDirtyFlags.Morphs, _renderHost.LastRenderData.Shapes)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>Toggle body-weight pose (MWGT × BSMS + MRSV + ARMA sculpt aditivo). Triggers granular
    ''' MarkDirty(Pose) — no full reload.</summary>
    Private Sub CheckBoxApplyBodyWeight_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBodyWeight.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        RebuildAndApplyMergedPose()
        ' SSE: el peso (NAM7) NO es bone-scale sino morph de VÉRTICE — LERP _0/_1 del cuerpo +
        ' SkinnyMorph de cabeza/pelo. Además del pase de pose hay que rearmar el composite de morphs.
        RebuildMorphResolverIfSse()
    End Sub

    ''' <summary>Rearma el composite de morphs + MarkDirty(Morphs) SÓLO bajo Skyrim. Lo usan los toggles
    ''' "Body weight" y "Sculpt", que en FO4 son pura pose (bone-scale) pero en SSE son canales de vértice
    ''' dentro del plan de cara / del resolver _0/_1. En FO4 no se llama: sería recomputar morphs al pedo.</summary>
    Private Sub RebuildMorphResolverIfSse()
        If Not RenderToggleLabels.IsSse() Then Return
        If _renderHost.LastRenderedState Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return
        Dim newResolver = BuildCompositeMorphResolver(_renderHost.LastRenderedState, _renderHost.LastRenderData)
        Dim intent = _previewControl.Intent
        intent.MorphResolver = newResolver
        intent.MarkDirty(RenderDirtyFlags.Morphs, _renderHost.LastRenderData.Shapes)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>Toggle BodySlide vertex morphs (BODYTRI .tri + slider dict). Same granular path
    ''' as CheckBoxApplyVertexMorphs: rebuild MorphResolver via BuildCompositeMorphResolver
    ''' (which now sees the toggle via BuildBodyMorphResolver) + MarkDirty(Morphs) + Invalidate.
    ''' Off = engine reads NifLocalVertices unmorphed for the body shape; face FRTRI003 morphs
    ''' still apply (separate resolver in the composite).</summary>
    Private Sub CheckBoxBodyTri_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxBodyTri.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        If _renderHost.LastRenderedState Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return
        ' Independent of CheckBoxApplyVertexMorphs (face). Composite always rebuilt; the gates
        ' inside (face=CheckBoxApplyVertexMorphs, body=this) decide what each subsection emits.
        Dim newResolver = BuildCompositeMorphResolver(_renderHost.LastRenderedState, _renderHost.LastRenderData)
        Dim intent = _previewControl.Intent
        intent.MorphResolver = newResolver
        intent.MarkDirty(RenderDirtyFlags.Morphs, _renderHost.LastRenderData.Shapes)
        _previewControl.InvalidateRender()
    End Sub

    ''' <summary>Toggle sculpt. FO4 = ARMA SCLP (per-bone scaling): OFF hace que toda shape — incluidos los
    ''' consumidores [A] over-armor que heredarían el SCLP del source — caiga al skeleton base sin amplificador.
    ''' SSE = sculpt per-vértice de RaceMenu (.jslot): OFF deja la cara con los NAM9/NAMA vanilla, sin los
    ''' deltas libres del preset. Toggle diagnóstico para comparar A/B sobre el mismo NPC en los dos juegos.</summary>
    Private Sub CheckBoxApplySculpt_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplySculpt.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        RebuildAndApplyMergedPose()
        ' SSE: el sculpt de RaceMenu es un canal de VÉRTICE del plan de cara, no una capa de pose.
        RebuildMorphResolverIfSse()
    End Sub

    ''' <summary>Toggle "Render armor". OFF excluye los candidates con bits [A] (41-45) del
    ''' render — útil para ver al NPC con underarmor + body skin sin las piezas combat encima
    ''' y poder detectar visualmente bugs del SCLP. Requiere full re-render porque cambia el
    ''' set de shapes cargados, no sólo poses.</summary>
    Private Sub CheckBoxRenderArmor_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderArmor.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        _renderHost.ApplyRenderToggleVisibility()
    End Sub

    ''' <summary>Toggle "Render underarmor". OFF oculta la ropa underarmor (Outfit con BODY/[U])
    ''' Y los gloves de outfit (Outfit con hand bits). Al ocultar la ropa, destapa automáticamente
    ''' el body skin / naked hands subyacentes — replica el efecto in-game `unequipall`.
    ''' Independiente de "Render armor [A]".</summary>
    Private Sub CheckBoxRenderUnderarmor_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderUnderarmor.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        _renderHost.ApplyRenderToggleVisibility()
    End Sub

    ''' <summary>Toggle "Render body". OFF oculta el NPC desnudo: body skin (Kind=Skin con BODY,
    ''' que en FO4 cubre torso+piernas+pies), naked hands (Skin con bits hand) y head parts.
    ''' Deja sólo outfits/armor visibles — útil para revisar la silueta de la ropa sola.</summary>
    Private Sub CheckBoxRenderBody_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderBody.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        _renderHost.ApplyRenderToggleVisibility()
    End Sub

    ''' <summary>Toggle "Render headwear". OFF oculta cualquier prenda de cabeza/cara (helmets,
    ''' caps, glasses, bandanas, masks — Outfit con bits 30-32/46-49 puros) Y destapa los head parts
    ''' que estaban ocluidos por la occlusion matrix vanilla (pelo bajo casco, barba bajo gas mask,
    ''' etc.). Replica el efecto in-game de quitar el headgear.</summary>
    Private Sub CheckBoxRenderHeadwear_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderHeadwear.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        ' Rebuild + re-apply morphs so the hair-top zap follows the toggle: headwear ON re-injects the
        ' zap channel (corona zapeada), OFF drops the HairTopZapResolver and ApplyMorphPlan clears the
        ' mask next pass (corona destapada). Same granular Intent.MarkDirty(Morphs) path the vertex-
        ' morph checkboxes use — no full reload. ApplyRenderToggleVisibility still runs afterwards to
        ' hide/show the headwear meshes themselves and the fully-occluded head parts.
        If _renderHost.LastRenderedState IsNot Nothing AndAlso _renderHost.LastRenderData IsNot Nothing Then
            Dim newResolver = BuildCompositeMorphResolver(_renderHost.LastRenderedState, _renderHost.LastRenderData)
            Dim intent = _previewControl.Intent
            intent.MorphResolver = newResolver
            intent.MarkDirty(RenderDirtyFlags.Morphs, _renderHost.LastRenderData.Shapes)
            _previewControl.InvalidateRender()
        End If
        _renderHost.ApplyRenderToggleVisibility()
    End Sub

    ''' <summary>Toggle "Render gore". OFF oculta meatcap shapes (BSSubIndexTriShape sub-segments
    ''' con userSlotID en SECTIONCAP/TORSOCAP del enum NIF, o en el rango Gore 100/102/103 del
    ''' .xrc de BS-OS). Mismo destino visual que las HDPT type=7 Meatcaps que ya se filtran en
    ''' SelectWinningCandidates. ON las muestra para inspección.</summary>
    Private Sub CheckBoxRenderGore_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxRenderGore.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        _renderHost.ApplyRenderToggleVisibility()
    End Sub


    Private Async Sub RenderCurrentStateAsyncWrapper()
        Try
            Await RenderCurrentStateAsync(System.Threading.Interlocked.Increment(_previewRequestVersion))
        Catch ex As Exception
            Logger.LogLazy(Function() $"[RENDER] main render failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Shared path for FMRS / body-weight toggles: rebuild the merged NPC pose from
    ''' current checkbox state, apply it to the per-NPC SkeletonInstance, and MarkDirty(Pose,
    ''' shapes) so only this NPC's meshes recompute. SkeletonDictionary is already populated
    ''' from the initial render, so BuildMergedNpcPose can parent-walk.</summary>
    Friend Sub RebuildAndApplyMergedPose(Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If host.LastRenderedState Is Nothing OrElse host.LastRenderData Is Nothing OrElse host.LastSkeletonInstance Is Nothing Then
            Return
        End If
        Dim fmrsEnabled = host.Toggles.ApplyBoneMorphs
        Dim bwEnabled = host.Toggles.ApplyBodyWeight
        Dim sculptEnabled = host.Toggles.ApplySculpt
        ' Mismo checkbox que fmrsEnabled: bajo Skyrim ese canal gatea los node transforms de RaceMenu
        ' (no hay FMRS). Acá coincide con fmrsEnabled; en BuildRenderPlan no, porque allá fmrs trae
        ' AND'eado el gender-override.
        Dim nodeXfEnabled = host.Toggles.ApplyBoneMorphs
        ' Base pose (sin sculpt) → skeleton base.
        Dim basePose = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, host.LastSkeletonInstance, Nothing,
                                                             nodeTransformsEnabled:=nodeXfEnabled)
        ' Los bone-morphs van a la capa MorphDeltaTransform (no a la pose). Así la capa Delta
        ' (pose/animación) queda libre y el morph sobrevive a un futuro ApplyPose por frame.
        host.LastSkeletonInstance.ApplyBoneMorphPose(basePose)
        ' NNAM comp anti-propagación (post-pase tras ApplyBoneMorphPose) — ver ApplyNeckNnamCompensation.
        _morphPoseResolver.ApplyNeckNnamCompensation(host.LastSkeletonInstance)
        ' [MOUNTDELTA-PREPASS] Repopular MountDelta desde la cache del render inicial (re-write
        ' idempotente; ApplyBoneMorphPose no borra el mount).
        _mountingResolver.ApplyMountPlanForActor(host.LastSkeletonInstance, host.LastRenderData)

        ' Head skeleton: re-pose CON body weight Y NNAM neck-fat. BuildBodyWeightPose mete la
        ' compensación anti-propagación (S⁻¹ a los hijos directos de "Neck") para que la escala del
        ' hueso ancestro "Neck" quede solo en sus propios verts y no infle (balloon) el resto de la
        ' cara. fmrsEnabled sigue honrado (la cabeza conserva sus morphs FMRS).
        If host.LastHeadSkeletonInstance IsNot Nothing AndAlso Not ReferenceEquals(host.LastHeadSkeletonInstance, host.LastSkeletonInstance) Then
            Dim headPose = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, host.LastHeadSkeletonInstance, Nothing,
                                                                 nodeTransformsEnabled:=nodeXfEnabled)
            host.LastHeadSkeletonInstance.ApplyBoneMorphPose(headPose)
            _morphPoseResolver.ApplyNeckNnamCompensation(host.LastHeadSkeletonInstance)
        End If

        ' Lazy build / refresh of per-ARMA skeletons. Necesario cuando Sclpt arranca OFF en el
        ' render inicial (entonces host.LastSkelByArma quedó vacío) y el usuario lo enciende después,
        ' o cuando aparecen shapes con sculpt cuyo per-ARMA aún no existía. El MultiInstanceSkeletonResolver
        ' tiene host.LastShapeToSkel por referencia → mutar el dict aquí lo refleja en el siguiente Pose dirty.
        Dim lazyBuilt As Integer = 0
        If sculptEnabled AndAlso host.LastShapeToSkel IsNot Nothing Then
            For Each shape In host.LastRenderData.Shapes
                Dim sculpt As Dictionary(Of String, System.Numerics.Vector3) = Nothing
                If Not host.LastRenderData.ShapeArmaSculpt.TryGetValue(shape, sculpt) Then Continue For
                If sculpt Is Nothing OrElse sculpt.Count = 0 Then Continue For
                Dim armaFormID As UInteger = 0
                host.LastRenderData.ShapeArmaFormID.TryGetValue(shape, armaFormID)
                If host.LastSkelByArma.ContainsKey(armaFormID) Then Continue For
                ' Build the missing per-ARMA skel.
                Dim armaSkel = PrepareSkeleton(host.LastRenderedState, host.LastRenderData)
                host.LastSkelByArma(armaFormID) = armaSkel
                host.LastSculptByArma(armaFormID) = sculpt
                lazyBuilt += 1
            Next
        End If

        ' Per-ARMA skeleton clones: cada uno recibe SU propio sculpt aplicado vía H3 multiplicative.
        ' Si sculpt OFF: rebuild las per-ARMA con Nothing (idéntico al base) — el shape sigue
        ' apuntando al per-ARMA skel pero éste pierde el SCLP, equivalente a base.
        For Each kv In host.LastSkelByArma
            Dim armaSkel = kv.Value
            Dim sculpt As Dictionary(Of String, System.Numerics.Vector3) = Nothing
            If sculptEnabled Then host.LastSculptByArma.TryGetValue(kv.Key, sculpt)
            Dim poseForArma = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, armaSkel, sculpt,
                                                                    nodeTransformsEnabled:=nodeXfEnabled)
            armaSkel.ApplyBoneMorphPose(poseForArma)
            _morphPoseResolver.ApplyNeckNnamCompensation(armaSkel)
            ' [MOUNTDELTA-PREPASS] Per-instance MountDelta para este clone sculpt — repopula desde cache.
            _mountingResolver.ApplyMountPlanForActor(armaSkel, host.LastRenderData)
        Next

        ' Re-route shape→skel mappings según el toggle actual. Sclpt=ON → shapes con sculpt apuntan
        ' a su per-ARMA skel; Sclpt=OFF → todos apuntan al base. La mutación es visible al resolver
        ' porque éste tiene el dict por referencia.
        If host.LastShapeToSkel IsNot Nothing Then
            For Each shape In host.LastRenderData.Shapes
                Dim catR As ShapeRenderCategory = ShapeRenderCategory.Other
                host.LastRenderData.ShapeCategory.TryGetValue(shape, catR)
                If catR = ShapeRenderCategory.HeadPart AndAlso host.LastHeadSkeletonInstance IsNot Nothing Then
                    ' Head parts stay on the body-weight-free head skeleton (never the body skel).
                    host.LastShapeToSkel(shape) = host.LastHeadSkeletonInstance
                    Continue For
                End If
                Dim sculpt As Dictionary(Of String, System.Numerics.Vector3) = Nothing
                Dim armaFormID As UInteger = 0
                host.LastRenderData.ShapeArmaSculpt.TryGetValue(shape, sculpt)
                host.LastRenderData.ShapeArmaFormID.TryGetValue(shape, armaFormID)
                Dim armaSkel As SkeletonInstance = Nothing
                If sculptEnabled AndAlso sculpt IsNot Nothing AndAlso sculpt.Count > 0 _
                   AndAlso host.LastSkelByArma.TryGetValue(armaFormID, armaSkel) Then
                    host.LastShapeToSkel(shape) = armaSkel
                Else
                    host.LastShapeToSkel(shape) = host.LastSkeletonInstance
                End If
            Next
        End If

        Dim intent = host.PreviewCtl.Intent
        ' Camino head-bake: la deformación FMRS y el body-weight de la cabeza ya no viven en los huesos
        ' sino en las POSICIONES horneadas ⇒ hay que refrescar los insumos del servicio (si no, conserva la
        ' firma con la que nació y NO re-hornea) y además marcar Morphs, que es donde corre el provider.
        ' Este es el chokepoint de los DOS caminos que cambian esos insumos sin rearmar el composite:
        ' este mismo Sub (toggles de body-weight / sculpt / FMRS de la main form) y el slider de FMRS del
        ' Edit Face, que llama acá justo antes de su propio MarkDirty (EditFace_Form.FaceRefreshScope.Pose).
        ' headBakeOn = hay un servicio vivo con shapes gateadas en ESTE host (preciso: fuera de FO4 o sin
        ' cabeza horneable el servicio es Nothing y esto queda como un MarkDirty(Pose) normal).
        Dim headBakeOn = host.LastHeadBakeService IsNot Nothing AndAlso host.LastHeadBakeService.RegisteredCount > 0
        If headBakeOn Then RefreshHeadBakeInputs(host)
        intent.MarkDirty(If(headBakeOn, RenderDirtyFlags.Pose Or RenderDirtyFlags.Morphs, RenderDirtyFlags.Pose), host.LastRenderData.Shapes)
        host.PreviewCtl.InvalidateRender()
    End Sub

#Region "Animation bar (combo + Select Animation + play/frames) — live preview on the main render"
    ' El behavior es de la RAZA (ver [[24-anim-behavior-por-raza]]); el clip se reproduce con
    ' HkxPoseImportSession (skeleton del rigName + clip + skeleton vivo del render) y se aplica a la
    ' capa Delta vía SkeletonInstance.ApplyPose. La capa pose/Delta está en IDENTIDAD en el render
    ' normal (los morphs viven en MorphDeltaTransform vía ApplyBoneMorphPose) → la animación tiene esa
    ' capa libre y ResetPose vuelve al estático morph-only. Cache POR RAZA (no por NPC): el behavior
    ' es de la raza, así dos NPCs de la misma raza comparten clips+skeleton (solo se enumera una vez).
    Private Class AnimRaceModel
        Public Clips As List(Of ResolvedAnimationClip)
        Public SkeletonBytes As Byte()
        Public AdditiveDetected As Boolean = False
    End Class
    ' Per race+gender clip-model cache. ConcurrentDictionary because it is filled from BOTH the startup
    ' background preload AND the per-selection background enumeration (and read on the UI thread). Holds
    ' clip METADATA (paths/names) + one ~43 KB Havok skeleton per race — NOT raw animation bytes (those
    ' load on demand when a clip is actually played), so the footprint stays bounded by distinct races.
    Private ReadOnly _animRaceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of String, AnimRaceModel)(StringComparer.OrdinalIgnoreCase)
    ' Race+gender key the anim bar SHOULD show right now (set on the UI thread at selection). A background
    ' enumeration that finishes after the user moved to another race compares against this and skips the
    ' stale combo populate. UI-thread-only.
    Private _animDesiredKey As String = ""
    ' Keys whose background enumeration is in flight — prevents launching a duplicate enumeration when the
    ' user re-selects the same race before its ~16 s walk finishes. UI-thread-only (add at launch, remove
    ' in the BeginInvoke continuation).
    Private ReadOnly _animInFlight As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
    Private _animCacheKey As String = ""                ' "raceFormID|F|M" actualmente en el combo
    Private _animClips As List(Of ResolvedAnimationClip) = Nothing
    Private _animComboClips As List(Of ResolvedAnimationClip) = Nothing  ' subconjunto del combo (gender + sin 1ª persona); el combo indexa ESTA lista
    Private _animSkeletonBytes As Byte() = Nothing
    Private _animSession As HkxPoseImportSession = Nothing
    ' Playback compartido con WM (reloj + selección de frame por FPS + caché de poses + loop
    ' Application.Idle). El player ES el driver: BeginIdlePlayback/EndIdlePlayback reemplazan al
    ' WinForms Timer y, en cada frame elegido por reloj, llaman OnAnimPlaybackFrame en el hilo UI.
    Private _animPlayer As HkxAnimationPlayer = Nothing
    Private _animSuppress As Boolean = False
    Private _animSuppressMs As Boolean = False          ' al setear NumericAnimFrameMs programáticamente
    ''' <summary>hkbClipGenerator::m_playbackSpeed del clip seleccionado. Se RESUELVE al cargar el
    ''' clip (multiplica el FPS nativo) y desde ahí el usuario puede editar el numeric libremente.
    ''' El SIGNO se guarda aparte porque el numeric sólo admite FPS positivos, y se reinyecta en
    ''' <see cref="SignedFpsFromNumeric"/>. ⛔ Desde el fix de la reversa, un TargetFps negativo SÍ
    ''' reproduce al revés: HkxAnimationPlayer.FrameForNow conserva el signo y sólo coacciona el 0 y el
    ''' no-finito. NO reponer un Math.Abs acá ni en ButtonAnimPlay_Click. Medido: 17,1% de los clips SSE y 5,3% de los FO4 tienen
    ''' playbackSpeed &lt;&gt; 1.0 (0,1x, 10x, 2x, -1x…): no es caso raro, hay que resolverlo siempre.</summary>
    Private _animClipSpeed As Double = 1.0
    Private _animOverBudget As Boolean = False          ' FPS en rojo = render no llega al target

    ' Editor-bar gate durante playback: al pulsar Play se deshabilitan los 10 botones de acción
    ' por-NPC (los mismos de DisableNpcActionControls) y se restauran en el ÚNICO path de stop
    ' (StopAnimPlayback). Se captura el .Enabled de cada botón ANTES de apagarlo para restaurar el
    ' estado exacto que tenían (algunos pueden estar deshabilitados por gating per-NPC). El guard
    ' _editorBarCapturedDuringPlay hace que un Stop sin Play previo (ComboAnim_SelectedIndexChanged /
    ' RefreshAnimBarForCurrentNpc llaman StopAnimPlayback incondicionalmente) sea un no-op.
    Private _editorBarCapturedDuringPlay As Boolean = False
    Private _savedEnabledEditFace As Boolean
    Private _savedEnabledEditBody As Boolean
    Private _savedEnabledEditOutfit As Boolean
    Private _savedEnabledEditNpc As Boolean
    Private _savedEnabledLoadLooksmenu As Boolean
    Private _savedEnabledSaveLooksmenu As Boolean
    Private _savedEnabledCopyLook As Boolean
    Private _savedEnabledPasteLook As Boolean
    Private _savedEnabledSavePlugin As Boolean
    Private _savedEnabledBuildCharGen As Boolean
    Private _savedEnabledSaveSceneNif As Boolean
    Private _savedEnabledExportFomod As Boolean

    ''' <summary>Deshabilita/restaura la barra de botones de acción del EDITOR mientras se reproduce
    ''' una animación. NO toca los controles de la barra de animación (Combo/Select/Play/Slider/FPS)
    ''' para que el usuario pueda parar/scrubear durante el playback. Al activar captura el .Enabled
    ''' actual de los 10 botones y los apaga; al desactivar (solo si hubo captura previa) los restaura
    ''' a su valor capturado. Idempotente: el gating per-NPC posterior (LoadNPCOnDemandAsync /
    ''' UpdateEditBodyEnabled / UpdateEditFaceEnabled / DisableNpcActionControls) recalcula el estado
    ''' correcto tras un cambio de selección, sobrescribiendo lo restaurado.</summary>
    Private Sub SetEditorBarEnabledForPlayback(playing As Boolean)
        If playing Then
            _savedEnabledEditFace = ButtonEditFace.Enabled
            _savedEnabledEditBody = ButtonEditBody.Enabled
            _savedEnabledEditOutfit = ButtonEditOutfit.Enabled
            _savedEnabledEditNpc = ButtonEditNpc.Enabled
            _savedEnabledLoadLooksmenu = ButtonLoadLooksmenu.Enabled
            _savedEnabledSaveLooksmenu = ButtonSaveLooksmenu.Enabled
            _savedEnabledCopyLook = ButtonCopyLook.Enabled
            _savedEnabledPasteLook = ButtonPasteLook.Enabled
            _savedEnabledSavePlugin = ButtonSavePlugin.Enabled
            _savedEnabledBuildCharGen = ButtonBuildCharGen.Enabled
            _savedEnabledSaveSceneNif = ButtonSaveSceneNif.Enabled
            _savedEnabledExportFomod = ButtonExportFomod.Enabled
            _editorBarCapturedDuringPlay = True
            ButtonEditFace.Enabled = False
            ButtonEditBody.Enabled = False
            ButtonEditOutfit.Enabled = False
            ButtonEditNpc.Enabled = False
            ButtonLoadLooksmenu.Enabled = False
            ButtonSaveLooksmenu.Enabled = False
            ButtonCopyLook.Enabled = False
            ButtonPasteLook.Enabled = False
            ButtonSavePlugin.Enabled = False
            ButtonBuildCharGen.Enabled = False
            ButtonSaveSceneNif.Enabled = False
            ButtonExportFomod.Enabled = False
        ElseIf _editorBarCapturedDuringPlay Then
            ButtonEditFace.Enabled = _savedEnabledEditFace
            ButtonEditBody.Enabled = _savedEnabledEditBody
            ButtonEditOutfit.Enabled = _savedEnabledEditOutfit
            ButtonEditNpc.Enabled = _savedEnabledEditNpc
            ButtonLoadLooksmenu.Enabled = _savedEnabledLoadLooksmenu
            ButtonSaveLooksmenu.Enabled = _savedEnabledSaveLooksmenu
            ButtonCopyLook.Enabled = _savedEnabledCopyLook
            ButtonPasteLook.Enabled = _savedEnabledPasteLook
            ButtonSavePlugin.Enabled = _savedEnabledSavePlugin
            ButtonBuildCharGen.Enabled = _savedEnabledBuildCharGen
            ButtonSaveSceneNif.Enabled = _savedEnabledSaveSceneNif
            ButtonExportFomod.Enabled = _savedEnabledExportFomod
            _editorBarCapturedDuringPlay = False
        End If
    End Sub

    ''' <summary>Refresca la barra de animación al NPC actual. Se llama al INICIO de RenderCurrentStateAsync
    ''' (cubre TODOS los paths de render: wrapper, RenderFromCurrentSelection, etc. — usa CurrentBaseState,
    ''' el NPC que se va a renderizar). Fuerza stop + estático + repuebla el combo (cache por raza) y lo
    ''' deja en "(None)". Imprescindible: al cambiar de NPC el combo NO debe quedar con clips de otra raza.</summary>
    Private Sub RefreshAnimBarForCurrentNpc()
        StopAnimPlayback()
        SetPlayingAnimation(False)
        _animSession = Nothing
        EnsureAnimModelForCurrentNpc()        ' repuebla el combo si cambió la raza (cache por raza)
        _animSuppress = True
        If ComboAnim.Items.Count > 0 Then ComboAnim.SelectedIndex = 0
        _animSuppress = False
        SliderAnimFrame.Enabled = False : NumericAnimFrameMs.Enabled = False : ButtonAnimPlay.Enabled = False
        ' El render que viene reconstruye los SkeletonInstance ⇒ la pose estática aplicada a la capa
        ' Delta se pierde. El combo vuelve a "None" para no MENTIR sobre lo que se está viendo (mismo
        ' criterio que el combo de clips, que también vuelve a "(None - static)" en cada refresh).
        EnsurePoseCatalog(False)
        ResetPoseComboToNone()
        UpdatePoseComboEnabled()
        UpdateExportPoseEnabled()
    End Sub

    ' Resuelve las animaciones de la RAZA del NPC actual y puebla el combo. Cache por raza+gender:
    ' enumerar los behaviors (cargar decenas de .hkx + parsear cientos de clips) se hace UNA vez por
    ' raza; el resto de NPCs de esa raza es un AddRange instantáneo. La resolución NPC→raza es barata.
    ' Lee de CurrentBaseState (NPC seleccionado/a renderizar) — disponible antes que LastRenderedState.
    Private Function EnsureAnimModelForCurrentNpc() As Boolean
        If _renderHost Is Nothing OrElse _pluginManager Is Nothing Then Return False
        Dim st = If(_renderHost.CurrentBaseState, _renderHost.LastRenderedState)
        If st Is Nothing Then Return False
        Dim fid = st.RootNpcFormID
        If fid = 0UI Then Return False
        Try
            Dim npcRec = _pluginManager.GetRecord(fid)
            If npcRec Is Nothing Then Return False
            Dim npc = RecordParsers.ParseNPC(npcRec, _pluginManager)
            Dim rb = RaceBehaviorResolver.ResolveNpcBehavior(npc, _pluginManager)
            If rb Is Nothing Then Return False
            Dim isFemale = st.IsFemale
            rb.IsFemale = isFemale
            Dim key = $"{rb.RaceFormID:X8}|{If(isFemale, "F", "M")}"
            _animDesiredKey = key
            If key = _animCacheKey AndAlso _animClips IsNot Nothing Then Return _animClips.Count > 0  ' el combo ya muestra esta raza

            ' Cache HIT (preload de arranque, o enumeración de un NPC previo de esta raza) -> poblar combo instantáneo.
            Dim cached As AnimRaceModel = Nothing
            If _animRaceCache.TryGetValue(key, cached) Then
                ApplyAnimModelToCombo(key, cached)
                Logger.LogLazy(Function() $"[ANIM-BAR] NPC 0x{fid:X8} race={rb.RaceEditorID}({key}) cache HIT: {cached.Clips.Count} clips")
                Return cached.Clips.Count > 0
            End If

            ' Cache MISS -> enumerar en un hilo de fondo para que NI el render NI la UI se bloqueen en el walk de
            ' ~16 s. Se muestra un item "enumerando..." en el combo; cuando el walk termina, se marshalea de vuelta y
            ' se puebla SOLO si el usuario sigue en esta raza (_animDesiredKey). Re-seleccionar la misma raza mientras
            ' su walk esta en vuelo es no-op (_animInFlight); el poblado ocurre cuando esa unica enumeracion termina.
            SetAnimComboLoading()
            If _animInFlight.Contains(key) Then Return False
            _animInFlight.Add(key)
            Dim capturedKey = key
            Dim capturedRb = rb
            Dim capturedFid = fid
            System.Threading.Tasks.Task.Run(Sub()
                                                Dim model As AnimRaceModel = Nothing
                                                Try
                                                    model = EnumerateAnimRaceModel(capturedRb)
                                                    _animRaceCache(capturedKey) = model
                                                Catch ex As Exception
                                                    Logger.LogLazy(Function() $"[ANIM-BAR] background enumerate failed key={capturedKey}: {ex.GetType().Name}: {ex.Message}")
                                                End Try
                                                If IsDisposed Then Return
                                                Try
                                                    BeginInvoke(Sub()
                                                                    _animInFlight.Remove(capturedKey)
                                                                    If model IsNot Nothing AndAlso _animDesiredKey = capturedKey Then
                                                                        ApplyAnimModelToCombo(capturedKey, model)
                                                                        Logger.LogLazy(Function() $"[ANIM-BAR] NPC 0x{capturedFid:X8} ({capturedKey}) async populate: {model.Clips.Count} clips")
                                                                    End If
                                                                End Sub)
                                                Catch
                                                End Try
                                            End Sub)
            Return False
        Catch ex As Exception
            Logger.LogLazy(Function() $"[ANIM-BAR] resolve failed: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
    End Function

    ''' <summary>Construye el modelo de clips de la raza: el walk de behaviors (~16 s) + enumeracion de clips +
    ''' resolucion del esqueleto Havok. Computo PURO sobre PluginManager + FilesDictionary (read-only), asi que
    ''' es seguro en un hilo de fondo (lo llaman el preload de arranque y el path de miss por seleccion, ambos
    ''' off-UI). La deteccion de flags aditivos se difiere a su propio Task (muta los clips in-place; no hace
    ''' falta para las etiquetas del combo).</summary>
    Private Function EnumerateAnimRaceModel(rb As ResolvedRaceBehavior) As AnimRaceModel
        Dim loader As Func(Of String, Byte()) = AddressOf LoadAnimHkxBytes
        Dim model As New AnimRaceModel With {
            .Clips = BehaviorClipEnumerator.EnumerateClips(rb, loader).
                        OrderBy(Function(c) NpcManagerFormat.AnimClipLabel(c), StringComparer.OrdinalIgnoreCase).ToList(),
            .SkeletonBytes = LoadAnimHkxBytes(BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader))
        }
        Logger.LogLazy(Function() $"[ANIM-BAR] enumerated race ({rb.RaceEditorID}): {model.Clips.Count} clips, skeletonBytes={If(model.SkeletonBytes Is Nothing, 0, model.SkeletonBytes.Length)}")
        If Not model.AdditiveDetected Then
            model.AdditiveDetected = True
            Dim clipsRef = model.Clips
            System.Threading.Tasks.Task.Run(Sub()
                                                Try
                                                    BehaviorClipEnumerator.DetectHkxFlags(clipsRef, AddressOf LoadAnimHkxBytes)
                                                Catch
                                                End Try
                                            End Sub)
        End If
        Return model
    End Function

    ''' <summary>Puebla el combo de animaciones desde un modelo de raza (cacheado). SOLO hilo UI. Setea las listas
    ''' paralelas _animClips/_animComboClips que el picker y el indice de seleccion de clip usan.</summary>
    Private Sub ApplyAnimModelToCombo(key As String, model As AnimRaceModel)
        _animClips = model.Clips
        _animSkeletonBytes = model.SkeletonBytes
        _animCacheKey = key
        ' Combo plano = TODO (sin filtros de genero/1a-persona; el filtrado vive solo en el picker). Lista paralela
        ' propia para que el remap de indices y el add-if-missing del picker sigan andando uniformemente.
        _animComboClips = _animClips.ToList()
        Dim _swC As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)
        _animSuppress = True
        ComboAnim.BeginUpdate()
        ComboAnim.Items.Clear()
        ComboAnim.Items.Add("(None - static)")
        ComboAnim.Items.AddRange(_animComboClips.Select(Function(c) CObj(NpcManagerFormat.AnimClipLabel(c))).ToArray())
        ComboAnim.SelectedIndex = 0
        ComboAnim.EndUpdate()
        ComboAnim.Enabled = True   ' re-enable after a background load finished (no-op on the instant cache-hit path)
        _animSuppress = False
        UpdatePoseComboEnabled()   ' el combo quedó en "(None - static)" ⇒ la pose estática vuelve a mandar
        Dim cnt = _animComboClips.Count
        Logger.LogLazy(Function() $"[PERF-AC] ApplyAnimModelToCombo populate {cnt} items = {_swC.ElapsedMilliseconds}ms")
    End Sub

    ''' <summary>Muestra un item transitorio "enumerando..." en el combo mientras corre la enumeracion de fondo de
    ''' la raza. Limpia las listas paralelas de clips para que una seleccion de clip durante la carga sea no-op.
    ''' SOLO hilo UI.</summary>
    Private Sub SetAnimComboLoading()
        _animClips = Nothing
        _animComboClips = Nothing
        _animCacheKey = ""
        _animSuppress = True
        ComboAnim.BeginUpdate()
        ComboAnim.Items.Clear()
        ComboAnim.Items.Add("(enumerating animations...)")
        ComboAnim.SelectedIndex = 0
        ComboAnim.EndUpdate()
        ComboAnim.Enabled = False   ' no interaction while the background behavior walk runs (only on a genuine cache miss)
        _animSuppress = False
    End Sub

    ''' <summary>Background warm-up of the anim-clip cache for the DISTINCT race+gender combos present in the
    ''' loaded NPCs, started once after the NPC tree is built. Each distinct race is enumerated at most once
    ''' (the per-selection miss path shares the same ConcurrentDictionary, so it never double-enumerates a race
    ''' already warmed here). Runs entirely off the UI thread; failures per-race are swallowed (that race just
    ''' falls back to the lazy miss path). Bounded memory: clip metadata for the races actually in the load
    ''' order, no raw animation bytes.</summary>
    Private Sub PreloadAnimRacesInBackground()
        If _pluginManager Is Nothing Then Return
        Dim npcs = _allNPCs.ToList()   ' snapshot on the UI thread
        If npcs.Count = 0 Then Return
        System.Threading.Tasks.Task.Run(Sub()
                                            Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                                            Dim done As Integer = 0
                                            For Each npc In npcs
                                                Try
                                                    Dim rb = RaceBehaviorResolver.ResolveNpcBehavior(npc, _pluginManager)
                                                    If rb Is Nothing Then Continue For
                                                    Dim key = $"{rb.RaceFormID:X8}|{If(npc.Record.ConfigurationFlagsFemale, "F", "M")}"
                                                    If Not seen.Add(key) Then Continue For         ' distinct race+gender once
                                                    If _animRaceCache.ContainsKey(key) Then Continue For
                                                    rb.IsFemale = npc.Record.ConfigurationFlagsFemale
                                                    _animRaceCache(key) = EnumerateAnimRaceModel(rb)
                                                    done += 1
                                                Catch
                                                End Try
                                            Next
                                            Dim doneCopy = done, distinctCopy = seen.Count
                                            Logger.LogLazy(Function() $"[ANIM-BAR] preload done: {doneCopy} enumerated, {distinctCopy} distinct race+gender, {_animRaceCache.Count} cached")
                                        End Sub)
    End Sub

    ' Activa/desactiva el modo "reproduciendo animación" del control de render (como WM): cuando está
    ' activo, los frames se aplican como cambio de POSE solamente — sin reset de cámara ni recompute de
    ' bounds (Render.vb) → updates eficientes. Se apaga al volver a estático o al cambiar de NPC.
    Private Sub SetPlayingAnimation(value As Boolean)
        If _renderHost IsNot Nothing AndAlso _renderHost.PreviewCtl IsNot Nothing Then _renderHost.PreviewCtl.PlayingAnimation = value
    End Sub

    ' Combo plano = TODO (incluye los clips de 1ª persona, que el picker oculta por defecto). Como acá NO hay
    ' filtro, marcamos "1st-person" en el texto para distinguir los clips de cámara/viewmodel (inútiles para
    ' preview de NPC). Sufijo, no prefijo, para no alterar el orden alfabético por nombre del combo.
    ' Carga un .hkx por path lógico (Data-relativo): prueba con/sin "Meshes\" y .hkx/.hkt vía FilesDictionary.
    Private Function LoadAnimHkxBytes(path As String) As Byte()
        Return BehaviorClipEnumerator.LoadFirstHkxCandidate(AddressOf LoadHkxKeyBytes, path)
    End Function

    ' Loader de UNA key contra FilesDictionary (Nothing si falta/vacía/error).
    Private Function LoadHkxKeyBytes(key As String) As Byte()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If FilesDictionary_class.Dictionary.TryGetValue(key, loc) AndAlso loc IsNot Nothing Then
            Try
                Dim b = loc.GetBytes()
                If b IsNot Nothing AndAlso b.Length > 0 Then Return b
            Catch
            End Try
        End If
        Return Nothing
    End Function

    Private Sub ButtonSelectAnim_Click(sender As Object, e As EventArgs) Handles ButtonSelectAnim.Click
        If Not EnsureAnimModelForCurrentNpc() Then
            MsgBox("No animations resolved for the current NPC (load/render an NPC first).", vbInformation Or vbOKOnly, "Animations")
            Return
        End If
        Dim current = TryCast(If(ComboAnim.SelectedIndex > 0, _animComboClips(ComboAnim.SelectedIndex - 1), Nothing), ResolvedAnimationClip)
        Dim isFemale = _animCacheKey.EndsWith("|F", StringComparison.OrdinalIgnoreCase)   ' género del NPC actual (clave "raceFID|F/M")
        Using dlg As New AnimationPicker_Form(_animClips, isFemale, current)
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.SelectedClip IsNot Nothing Then
                Dim picked = dlg.SelectedClip
                Dim pos = _animComboClips.IndexOf(picked)
                If pos < 0 Then
                    ' el clip elegido está filtrado fuera del combo (p.ej. el usuario destildó género en el picker):
                    ' lo agregamos al combo para poder seleccionarlo/representarlo.
                    _animComboClips.Add(picked)
                    ComboAnim.Items.Add(NpcManagerFormat.AnimClipLabel(picked))
                    pos = _animComboClips.Count - 1
                End If
                ComboAnim.SelectedIndex = pos + 1
            End If
        End Using
    End Sub

    Private Sub ComboAnim_DropDown(sender As Object, e As EventArgs) Handles ComboAnim.DropDown
        EnsureAnimModelForCurrentNpc()
    End Sub

    Private Sub ComboAnim_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboAnim.SelectedIndexChanged
        If _animSuppress Then Return
        StopAnimPlayback()
        If ComboAnim.SelectedIndex <= 0 OrElse _animComboClips Is Nothing Then
            ResetAnimToTPose()
            Return
        End If
        ' Hay clip ⇒ la capa Delta pasa a ser del player: la pose estática se apaga (combo a "None" +
        ' deshabilitado, igual que WM). Se limpia ACÁ y no dentro de SelectAnimationClip para que un
        ' clip que falla al cargar tampoco deje puesta una pose que el combo ya no está mostrando.
        ClearStaticPoseForAnimation()
        SelectAnimationClip(_animComboClips(ComboAnim.SelectedIndex - 1))
        UpdatePoseComboEnabled()
        UpdateExportPoseEnabled()   ' cubre el clip cargado OK y también los aborts de SelectAnimationClip
    End Sub

    ''' <summary>TODO ABORTO DE ESTE METODO TIENE QUE PASAR POR ACA. Si un aborto deja
    ''' <c>_animSession</c> con la sesion del clip ANTERIOR, el resultado no es "no pasa nada": es que
    ''' "Export pose…" queda HABILITADO —su gate mira <c>_animSession IsNot Nothing</c>— y exporta los
    ''' huesos del clip viejo con el NOMBRE del clip nuevo, porque <c>SuggestedExportPoseName</c> y el log
    ''' salen de <c>ComboAnim.SelectedIndex</c>, no de la sesion. Eso escribe una pose mal etiquetada en el
    ''' XML COMPARTIDO con Wardrobe Manager, y el preview sigue mostrando la animacion vieja con el combo
    ''' diciendo otra cosa.
    ''' <para>Pasa de verdad: un clip que el behavior graph declara pero cuyo .hkx no esta instalado
    ''' (DLC o mod ausente) hace que <c>LoadAnimHkxBytes</c> devuelva Nothing.</para>
    ''' <para>Los tres caminos (el <c>Catch</c> y los dos early-returns) tienen que limpiar TANTO
    ''' <c>_animSession</c> COMO <c>_animPlayer</c> — dejar alguno afuera deja controles de la barra
    ''' de transporte vivos que no hacen nada.</para></summary>
    Private Sub AbortarSeleccionDeClip(motivo As String)
        _animSession = Nothing
        _animPlayer = Nothing
        ' HAY QUE APAGAR LA BARRA DE TRANSPORTE (slider/FPS/▶): dejarlos con el rango del clip ANTERIOR
        ' los deja "vivos" pero muertos (arrastrar el slider entra en `ApplyAnimFrame` y sale en seco
        ' por `_animPlayer Is Nothing`; ▶ no encuentra rama), y con `ComboPose` deshabilitado tampoco
        ' hay forma de recuperarlos salvo volver a "(None - static)" a mano.
        ' No se puede llamar `ResetAnimToTPose` entera: esa además resetea la pose del esqueleto y
        ' re-renderiza, y un clip que no cargó no tiene por qué tirar abajo lo que se está viendo.
        SliderAnimFrame.Enabled = False : NumericAnimFrameMs.Enabled = False : ButtonAnimPlay.Enabled = False
        Logger.LogLazy(Function() $"[ANIM-BAR] SelectAnimationClip abort: {motivo}")
    End Sub

    Private Sub SelectAnimationClip(clip As ResolvedAnimationClip)
        If clip Is Nothing OrElse _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing Then
            AbortarSeleccionDeClip($"clip={(clip IsNot Nothing)} host={(_renderHost IsNot Nothing)} liveSkel={(_renderHost?.LastSkeletonInstance IsNot Nothing)}")
            Return
        End If
        Dim liveBones = _renderHost.LastSkeletonInstance.SkeletonDictionary.Count
        ' Skeleton para interpretar ESTE clip = el del actor de origen de la anim (clip.SourceSkeletonPath,
        ' resuelto del path de animationNames). Una anim humana reusada por SuperMutant se interpreta con el
        ' rig humano → nombres de hueso → se mapean por NOMBRE al skeleton vivo (no por índice → no deforma).
        Dim clipSkelBytes = LoadAnimHkxBytes(clip.SourceSkeletonPath)
        If clipSkelBytes Is Nothing Then clipSkelBytes = _animSkeletonBytes   ' fallback: rigName del NPC
        Logger.LogLazy(Function() $"[ANIM-BAR] select clip='{clip.ClipName}' file='{clip.AnimationFile}' srcSkel='{clip.SourceSkeletonPath}'({If(clipSkelBytes Is Nothing, 0, clipSkelBytes.Length)}b) roles=[{String.Join(",", clip.Roles)}] race={_animCacheKey} liveBones={liveBones}")
        Dim clipBytes = LoadAnimHkxBytes(clip.AnimationFile)
        If clipBytes Is Nothing Then
            ' El caso real: el behavior graph declara el clip pero el .hkx no esta instalado.
            AbortarSeleccionDeClip($"clip not found: {clip.AnimationFile}")
            Return
        End If
        Try
            _animSession = HkxPoseImportSession.Create(clipSkelBytes, clipBytes, _renderHost.LastSkeletonInstance, clip.AnimationFile, clip.SourceSkeletonPath, additiveHint:=clip.IsAdditive)
            _animPlayer = New HkxAnimationPlayer(_animSession) With {.PoseName = "Animation"}
            Logger.LogLazy(Function() $"[ANIM-BAR] session OK frames={_animSession.FrameCount} tracks={_animSession.TrackCount} frameDur={_animSession.FrameDuration:0.####} skelSrc={_animSession.SkeletonSource}")
        Catch ex As Exception
            AbortarSeleccionDeClip($"session create FAILED clip='{clip.AnimationFile}': {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        ' Clip seleccionado = PAUSADO en frame 0 (no es "playing"). PlayingAnimation sigue la lógica
        ' del botón Play (True solo al reproducir), igual que WM — si no, el RenderTimer queda parado
        ' en pausa y no se puede rotar/zoom. Acá NO se setea True.
        ' El motor NO reproduce el tramo que el hkbClipGenerator recorta (cropStart/cropEnd), ni loopea un
        ' clip PING_PONG: rebota. Las dos cosas las resuelve el player; acá solo se le pasan los SEGUNDOS
        ' declarados y se lee el rango que salga. ⛔ Este metodo NO divide: toda la aritmetica vive en
        ' SetPlayableRange, para que el snap y el clamp esten en un solo lugar.
        _animPlayer.SetPlayableRange(clip.CropStartLocalTime, clip.CropEndLocalTime)
        _animPlayer.PingPong = clip.IsPingPong
        Dim primerFrame = _animPlayer.FirstPlayableFrame
        Dim ultimoFrame = _animPlayer.LastPlayableFrame
        _animSuppress = True
        ' ⛔ Maximum PRIMERO: el setter de Minimum hace `If _maximum < _minimum Then _maximum = _minimum`
        ' (TinySliderTextBox.vb:104), asi que subir el minimo con el maximo viejo abajo lo ARRASTRA. Y el
        ' setter de Value levanta ValueChanged (:146): por eso las tres van dentro del mismo _animSuppress.
        SliderAnimFrame.Maximum = ultimoFrame
        SliderAnimFrame.Minimum = primerFrame
        SliderAnimFrame.Value = primerFrame
        _animSuppress = False
        ' ⛔ > Minimum, no > 0: con crop el minimo deja de ser 0, y un clip cuyo rango honrado sea de UN
        ' solo frame en un indice > 0 habilitaria Play sobre algo congelado (OnAppIdle girando en Sleep(1)).
        ' Medido: 155 de 212 clips con crop dejan Minimum > 0, y hay 3 clips vanilla con rango de 1 frame
        ' (Actors\LibertyPrime\Animations\Idle.hkx, clips Equip y Unequip). Que esos queden sin transporte
        ' es engine-faithful: el motor tampoco tiene nada que reproducir ahi.
        SliderAnimFrame.Enabled = ultimoFrame > primerFrame
        NumericAnimFrameMs.Enabled = ultimoFrame > primerFrame
        ButtonAnimPlay.Enabled = ultimoFrame > primerFrame
        ' Velocidad AUTORADA del clip. ⛔ La ley de "que velocidad se reproduce de verdad" (0/NaN/Inf => x1,
        ' el resto tal cual CON SIGNO) vive en UN solo lugar: BehaviorClipEnumerator.VelocidadEfectiva, que
        ' deja el resultado en clip.VelocidadReproduccion. Es la MISMA que usa la clave del dedup. Repetirla
        ' aca las acoplaba por convencion: el dia que una cambiara, el dedup separaria variantes que se
        ' reproducen igual (o al reves) y ningun gate lo veria.
        _animClipSpeed = CDbl(clip.VelocidadReproduccion)
        ApplyAnimPlaybackInterval()   ' FPS por defecto = FPS nativo × playbackSpeed (editable después)
        ApplyAnimFrame(primerFrame)
        ' Al cambiar de clip, re-encuadra la cámara RESPETANDO los flags de cámara (Settings_Camara:
        ' FreezeCamera / ResetZoom / ResetAngles) — igual que WM/el pipeline en selección. ResetCamera()
        ' sin Force honra esos flags (si FreezeCamera está on, no toca la cámara).
        If _renderHost?.PreviewCtl IsNot Nothing Then
            _renderHost.PreviewCtl.ResetCamera()
            _renderHost.PreviewCtl.UpdateRequired = True
            _renderHost.PreviewCtl.RefreshRender()
        End If
    End Sub

    ' Default del FPS desde el FrameDuration del clip (editable por el usuario), igual que WM
    ' (ApplyHkxPlaybackInterval). El timer corre a ≤16ms y el player consulta el reloj de pared.
    Private Sub ApplyAnimPlaybackInterval()
        Dim fps As Double = 30.0
        If _animPlayer IsNot Nothing AndAlso _animPlayer.NativeFps > 0.0 Then fps = _animPlayer.NativeFps
        ' El playbackSpeed del clip se RESUELVE acá dentro del FPS. El numeric queda con el valor ya
        ' resuelto para que el usuario lo pueda cambiar a mano desde ahí.
        fps = fps * Math.Abs(_animClipSpeed)
        fps = Math.Min(CDbl(NumericAnimFrameMs.Maximum), Math.Max(CDbl(NumericAnimFrameMs.Minimum), fps))
        _animSuppressMs = True
        NumericAnimFrameMs.Value = CDec(Math.Round(fps, MidpointRounding.AwayFromZero))
        _animSuppressMs = False
        If _animPlayer IsNot Nothing Then _animPlayer.TargetFps = SignedFpsFromNumeric()
    End Sub

    ''' <summary>FPS del numeric con el SIGNO del playbackSpeed autorado: negativo = el clip va al revés.</summary>
    Private Function SignedFpsFromNumeric() As Double
        Dim fps = Math.Max(1.0, CDbl(NumericAnimFrameMs.Value))
        Return If(_animClipSpeed < 0.0, -fps, fps)
    End Function

    Private Sub NumericAnimFrameMs_ValueChanged(sender As Object, e As EventArgs) Handles NumericAnimFrameMs.ValueChanged
        If _animSuppressMs Then Return
        Dim fps = SignedFpsFromNumeric()   ' el numeric es FPS; el signo sale del playbackSpeed del clip
        If _animPlayer IsNot Nothing Then
            _animPlayer.TargetFps = fps
            ' Reanclar el reloj al frame actual para que el cambio de FPS no pegue un salto.
            If _animPlayer.IsPlaying Then _animPlayer.Rebase(CInt(Math.Round(SliderAnimFrame.Value)))
        End If
    End Sub

    ''' <summary>True si el player está reproduciendo (reemplaza el viejo _animPlayTimer.Enabled).</summary>
    Private Function IsAnimPlayingNow() As Boolean
        Return _animPlayer IsNot Nothing AndAlso _animPlayer.IsPlaying
    End Function

    ''' <summary>Aplica UNA pose a los tres juegos de esqueletos vivos del render: el del cuerpo, el de
    ''' la cabeza (body-weight-free — si no, la cabeza FaceGen y los head parts quedan flotando) y los
    ''' clones per-ARMA del sculpt. La pose es por NOMBRE de hueso, así que el mismo objeto sirve para
    ''' los tres. <c>ApplyPose</c> toca SOLO la capa DeltaTransform ⇒ el morph (MorphDelta) y el mount
    ''' sobreviven. Compartido por el frame de animación y por el combo de pose estática — que por eso
    ''' mismo son EXCLUYENTES: escriben la misma capa.</summary>
    Private Sub ApplyPoseToLiveSkeletons(pose As Poses_class)
        If _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing Then Return
        _renderHost.LastSkeletonInstance.ApplyPose(pose)
        If _renderHost.LastHeadSkeletonInstance IsNot Nothing AndAlso Not ReferenceEquals(_renderHost.LastHeadSkeletonInstance, _renderHost.LastSkeletonInstance) Then
            _renderHost.LastHeadSkeletonInstance.ApplyPose(pose)
        End If
        If _renderHost.LastSkelByArma IsNot Nothing Then
            For Each kv In _renderHost.LastSkelByArma
                If kv.Value IsNot Nothing Then
                    kv.Value.ApplyPose(pose)
                End If
            Next
        End If
    End Sub

    Private Sub ApplyAnimFrame(frame As Integer)
        If _animPlayer Is Nothing OrElse _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return
        Try
            ' La pose la memoiza `HkxPoseImportSession.BuildPose`, por (frame, nombre) → scrub/play
            ' barato. ⛔ El player NO tiene cache propia: tenia una SEGUNDA sobre la misma llamada, con
            ' otra clave, y por eso una devolvia la pose renombrada y la otra la vieja.
            ' La pose es por nombre de bone
            ' → se aplica igual al skeleton base y a los clones per-ARMA (sculpt). ApplyPose toca SOLO
            ' la capa DeltaTransform → el morph (MorphDelta) y el mount sobreviven.
            Dim pose = _animPlayer.PoseForFrame(frame)
            ApplyPoseToLiveSkeletons(pose)
            ' [ANIM-BONE-DIAG] Diagnóstico (no fix): para resolver chunks (cabeza/brazos Assaultron) dumpea
            ' por frame los bones de chunk — world final + mount (con rotación) + delta de la animación.
            ' Así se ve si el mount tiene rotación (eje de la anim mal) o cómo la pose mueve el chunk.
            ' Scrub a un frame da 1 dump limpio; en play se samplea 1 de cada 30 para no spamear.
            If Logger.Enabled AndAlso (Not IsAnimPlayingNow() OrElse frame Mod 30 = 0) Then
                Dim instD = _renderHost.LastSkeletonInstance
                ' Formatea un Transform COMPLETO (R 3×3 + T + Scale) para hacer la math offline con las
                ' convenciones reales. Nothing = "I".
                Dim fmt = Function(t As Transform_Class) As String
                              If t Is Nothing Then Return "I"
                              Dim r = t.Rotation, tt = t.Translation
                              Return $"R[{r.M11:F4},{r.M12:F4},{r.M13:F4}|{r.M21:F4},{r.M22:F4},{r.M23:F4}|{r.M31:F4},{r.M32:F4},{r.M33:F4}] T({tt.X:F3},{tt.Y:F3},{tt.Z:F3}) S={t.Scale:F4}"
                          End Function
                ' TODOS los huesos del esqueleto (no un subset): ver todo para no diagnosticar a ciegas.
                For Each kvpBone In instD.SkeletonDictionary
                    Dim bn = kvpBone.Key
                    Dim hb = kvpBone.Value
                    If hb Is Nothing Then Continue For
                    Dim bnL = bn, frL = frame
                    Dim oS = fmt(hb.OriginalLocaLTransform), mS = fmt(hb.MountDeltaTransform), dS = fmt(hb.DeltaTransform), phS = fmt(hb.MorphDeltaTransform)
                    Dim bw = hb.OriginalGetGlobalTransform.Translation, pw = hb.GetGlobalTransform.Translation
                    ' Padre: nombre + world POSE (para reconstruir la jerarquía/cascada real del app).
                    Dim parName = "<root>"
                    Dim parPwS = "n/a"
                    If hb.Parent IsNot Nothing Then
                        parName = hb.Parent.BoneName
                        Dim ppw = hb.Parent.GetGlobalTransform.Translation
                        parPwS = $"({ppw.X:F3},{ppw.Y:F3},{ppw.Z:F3})"
                    End If
                    Dim parNameL = parName, parPwSL = parPwS
                    Logger.LogLazy(Function() $"[ANIM-BONE] f={frL} '{bnL}' parent='{parNameL}'{Environment.NewLine}    O    ={oS}{Environment.NewLine}    Mount={mS}{Environment.NewLine}    Morph={phS}{Environment.NewLine}    Delta={dS}{Environment.NewLine}    parentPoseW.T={parPwSL}  bindWorld.T=({bw.X:F3},{bw.Y:F3},{bw.Z:F3})  poseWorld.T=({pw.X:F3},{pw.Y:F3},{pw.Z:F3})")
                Next
            End If
            ' Solo POSE dirty (no recarga geometría/materiales). Con PlayingAnimation=True el control
            ' omite reset de cámara y recompute de bounds → update eficiente como WM.
            _renderHost.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Pose, _renderHost.LastRenderData.Shapes)
            _renderHost.PreviewCtl.InvalidateRender()
        Catch ex As Exception
            Logger.LogLazy(Function() $"[ANIM-BAR] ApplyAnimFrame({frame}) FAILED: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}")


        End Try
    End Sub

    ' "(None)" = estático: limpia SOLO la capa pose/Delta (ResetPose) → queda el morph (MorphDelta) +
    ' mount. Vuelve a modo normal del control (PlayingAnimation=False).
    Private Sub ResetAnimToTPose()
        StopAnimPlayback()
        _animSession = Nothing
        _animPlayer = Nothing
        _animClipSpeed = 1.0
        SliderAnimFrame.Enabled = False : NumericAnimFrameMs.Enabled = False : ButtonAnimPlay.Enabled = False
        If _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then
            SetPlayingAnimation(False)
            Return
        End If
        _renderHost.LastSkeletonInstance.ResetPose()
        If _renderHost.LastHeadSkeletonInstance IsNot Nothing AndAlso Not ReferenceEquals(_renderHost.LastHeadSkeletonInstance, _renderHost.LastSkeletonInstance) Then
            _renderHost.LastHeadSkeletonInstance.ResetPose()
        End If
        If _renderHost.LastSkelByArma IsNot Nothing Then
            For Each kv In _renderHost.LastSkelByArma
                If kv.Value IsNot Nothing Then kv.Value.ResetPose()
            Next
        End If
        _renderHost.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Pose, _renderHost.LastRenderData.Shapes)
        _renderHost.PreviewCtl.InvalidateRender()
        SetPlayingAnimation(False)
        ' Sin clip la capa Delta vuelve a estar libre ⇒ el combo de pose estática se re-habilita.
        UpdatePoseComboEnabled()
        UpdateExportPoseEnabled()   ' sin sesión no hay frame que exportar
    End Sub

    ' ── Static pose combo ────────────────────────────────────────────────────────────────────────
    ' Puerto de la lógica de Wardrobe Manager (ComboBoxPoses + SliderPresetCollection.LoadPoses*):
    ' mismo catálogo (identidad + SAM JSON + BodySlide/WM XML), mismas etiquetas (Poses_class.ToString)
    ' y misma resolución de rutas (ver PoseCatalog).
    ' La pose y la animación escriben la MISMA capa (DeltaTransform vía ApplyPose), así que son
    ' excluyentes: el combo sólo vive mientras la barra está en "(None - static)". Al elegir un clip
    ' vuelve a "None" y se deshabilita; al volver a estático se re-habilita.
    Private _poseCatalog As PoseCatalog = Nothing
    ' Clave de las RUTAS con las que se construyó el catálogo ("<juego>|<samDir>|<bsPoseDir>"): si el
    ' usuario cambia el exe de BodySlide (Edit Body → BodySlide) o el juego, cambia la clave y se relee.
    Private _poseCatalogKey As String = ""
    Private _poseSuppress As Boolean = False
    ' True cuando hay una pose NO identidad puesta en la capa Delta. Evita el render de más al limpiar
    ' (elegir un clip con el combo ya en "None" no tiene nada que limpiar).
    Private _staticPoseApplied As Boolean = False

    ''' <summary>True cuando la capa Delta es del combo de poses: sin sesión de clip y con el combo de
    ''' animación en el índice 0 ("(None - static)", o el item transitorio de "enumerando…", que también
    ''' es estático). Hacen falta las dos: la sesión sola no alcanza porque un clip que falló al cargar
    ''' deja el combo mostrando su nombre con la sesión en Nothing.</summary>
    Private Function IsAnimStaticNow() As Boolean
        Return _animSession Is Nothing AndAlso ComboAnim.SelectedIndex <= 0
    End Function

    ''' <summary>Habilita el combo de pose sólo en estático y con poses reales para elegir (con el
    ''' catálogo vacío queda sólo "None", que no es una elección).</summary>
    Private Sub UpdatePoseComboEnabled()
        Dim usable = IsAnimStaticNow() AndAlso ComboPose.Items.Count > 1
        ComboPose.Enabled = usable
        LabelPose.Enabled = usable
        UpdateDeletePoseEnabled()
    End Sub

    Private Sub UpdateDeletePoseEnabled()
        Dim pose As Poses_class = Nothing
        Dim key = TryCast(ComboPose.SelectedItem, String)
        ButtonDeletePose.Enabled = IsAnimStaticNow() AndAlso key IsNot Nothing AndAlso
                                   key <> PoseCatalog.NoneKey AndAlso _poseCatalog IsNot Nothing AndAlso
                                   _poseCatalog.Poses.TryGetValue(key, pose) AndAlso
                                   pose.Source <> Poses_class.Pose_Source_Enum.None
    End Sub

    ''' <summary>(Re)lee el catálogo de poses si cambiaron las rutas (o si <paramref name="force"/>),
    ''' y repuebla el combo conservando la selección. Barato: son unas pocas decenas de XML/JSON
    ''' chicos, así que corre sincrónico (a diferencia del walk de behaviors de la animación).
    ''' <para>LA CLAVE TIENE QUE INCLUIR LOS DIRECTORIOS YA RESUELTOS, no sólo el juego: cortar antes
    ''' de resolverlos (para ahorrar el I/O de <c>ResolveBsExeFromSiblingWm</c> por render) deja, en una
    ''' instalación sin BodySlide configurado, el catálogo pegado en sólo "None" — y como los ÚNICOS
    ''' call sites con <c>force:=True</c> son <c>DropDown</c> y los dos del botón de export (ninguno
    ''' dispara en "cambió la config"), las poses no vuelven a aparecer hasta reiniciar la app.</para>
    ''' <para>El costo del escaneo de carpetas hermanas se ataca memoizándolo en <c>PoseCatalog</c>,
    ''' no dejando de mirar si las rutas cambiaron.</para></summary>
    Private Sub EnsurePoseCatalog(force As Boolean)
        Dim isSse = Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
        If force Then PoseCatalog.OlvidarEscaneoDeHermanos()
        Dim samDir = PoseCatalog.ResolveSamPosesDir()
        Dim bsDir = PoseCatalog.ResolveBsPosesDir(isSse)
        Dim key = $"{If(isSse, "SSE", "FO4")}|{samDir}|{bsDir}"
        If Not force AndAlso _poseCatalog IsNot Nothing AndAlso key = _poseCatalogKey Then Return
        Try
            Dim cat As New PoseCatalog()
            cat.Load(samDir, bsDir)
            _poseCatalog = cat
            _poseCatalogKey = key
            Logger.LogLazy(Function() $"[POSE-BAR] catalog loaded: {cat.Poses.Count - 1} poses (+None) sam='{samDir}' bs='{bsDir}'")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[POSE-BAR] catalog load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        ' Si la pose que estaba puesta desapareció del disco, el combo cae a "None": hay que limpiar
        ' también la capa Delta, o el render seguiría mostrando una pose que el combo ya no nombra.
        If Not PopulatePoseCombo() AndAlso _staticPoseApplied Then ApplyStaticPose(Nothing)
        UpdateDeletePoseEnabled()
    End Sub

    ''' <summary>Puebla el combo: "None" primero y el resto alfabético. WM ordena TODO alfabéticamente
    ''' (Relee_Poses) y la identidad cae donde caiga; acá va primera para que el índice 0 sea siempre
    ''' "sin pose", igual que "(None - static)" en el combo de clips. Conserva la selección por clave;
    ''' devuelve False si esa clave ya no existe (la selección quedó en "None").</summary>
    Private Function PopulatePoseCombo() As Boolean
        Dim wanted As New List(Of String) From {PoseCatalog.NoneKey}
        If _poseCatalog IsNot Nothing Then
            wanted.AddRange(_poseCatalog.Poses.Keys.Where(Function(k) k <> PoseCatalog.NoneKey))
        End If
        ' Sin cambios en las claves no se toca el combo: el relee del DropDown ocurre con la lista YA
        ' desplegada, y un Clear/Add ahí adentro la parpadea sin necesidad.
        If ComboPose.Items.Count = wanted.Count Then
            Dim same = True
            For i = 0 To wanted.Count - 1
                If Not String.Equals(TryCast(ComboPose.Items(i), String), wanted(i), StringComparison.Ordinal) Then same = False : Exit For
            Next
            If same Then Return True
        End If

        Dim previous = TryCast(ComboPose.SelectedItem, String)
        _poseSuppress = True
        ComboPose.BeginUpdate()
        ComboPose.Items.Clear()
        ComboPose.Items.AddRange(wanted.Cast(Of Object).ToArray())
        Dim idx = If(previous Is Nothing, 0, ComboPose.Items.IndexOf(previous))
        ComboPose.SelectedIndex = If(idx >= 0, idx, 0)
        ComboPose.EndUpdate()
        _poseSuppress = False
        Return idx >= 0
    End Function

    ''' <summary>Lleva el combo a "None" SIN tocar el render (el caller decide qué pasa con la capa
    ''' Delta: un refresh de NPC la reconstruye, un clip la sobreescribe).</summary>
    Private Sub ResetPoseComboToNone()
        _poseSuppress = True
        If ComboPose.Items.Count > 0 Then ComboPose.SelectedIndex = 0
        _poseSuppress = False
        _staticPoseApplied = False
    End Sub

    ''' <summary>Apaga la pose estática porque entra una animación: combo a "None" y, si había una
    ''' pose realmente puesta, se limpia la capa Delta. Si el clip carga bien su frame 0 la pisa
    ''' igual; la limpieza es para el caso en que NO cargue.</summary>
    Private Sub ClearStaticPoseForAnimation()
        Dim hadPose = _staticPoseApplied
        ResetPoseComboToNone()
        If hadPose Then ApplyStaticPose(Nothing)
    End Sub

    ''' <summary>Refresca el catálogo al abrir el combo, así una pose recién guardada por Wardrobe
    ''' Manager (o un exe de BodySlide recién elegido en Edit Body) aparece sin reiniciar la app.
    ''' Mismo gesto que <c>ComboAnim_DropDown</c>.</summary>
    Private Sub ComboPose_DropDown(sender As Object, e As EventArgs) Handles ComboPose.DropDown
        EnsurePoseCatalog(True)
    End Sub

    Private Sub ComboPose_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboPose.SelectedIndexChanged
        UpdateDeletePoseEnabled()
        If _poseSuppress Then Return
        ' Con un clip activo la capa Delta no es del combo. No debería poder pasar (el combo está
        ' deshabilitado), pero un cambio por código sin suppress no puede pisar la animación.
        If Not IsAnimStaticNow() Then Return
        Dim pose As Poses_class = Nothing
        Dim key = TryCast(ComboPose.SelectedItem, String)
        If key IsNot Nothing AndAlso key <> PoseCatalog.NoneKey AndAlso _poseCatalog IsNot Nothing Then
            _poseCatalog.Poses.TryGetValue(key, pose)
        End If
        ApplyStaticPose(pose)
    End Sub

    Private Sub ButtonDeletePose_Click(sender As Object, e As EventArgs) Handles ButtonDeletePose.Click
        Dim key = TryCast(ComboPose.SelectedItem, String)
        Dim pose As Poses_class = Nothing
        If key Is Nothing OrElse key = PoseCatalog.NoneKey OrElse _poseCatalog Is Nothing OrElse
           Not _poseCatalog.Poses.TryGetValue(key, pose) OrElse pose Is Nothing Then Return

        If MessageBox.Show(Me, $"Are you sure you want to delete pose {pose.Name}?", "Delete pose",
                           MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                           MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        Try
            _poseCatalog.DeletePose(pose)
            EnsurePoseCatalog(True)
            UpdatePoseComboEnabled()
            Logger.LogLazy(Function() $"[POSE-BAR] deleted pose='{pose.Name}' source={pose.Source} file='{pose.Filename}'")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[POSE-BAR] delete FAILED pose='{pose.Name}': {ex}")
            MessageBox.Show(Me, "Error deleting the pose: " & ex.Message, "Delete pose",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ' ── Export pose (frame de la animación → XML de poses de Wardrobe Manager) ────────────────────
    ' Puerto del path de guardado de WM (HkxPoseImport_Form.OkClicked + Wardrobe_Manager_Form.
    ' SaveImportedHkxPoseXml), SIN el export SAM. Reusa la MISMA sesión que ya está reproduciendo:
    ' HkxPoseImportSession.BuildPose devuelve exactamente el Poses_class que WM guarda, así que el
    ' archivo que sale es indistinguible del que escribe WM.
    ' La pose es un DELTA contra el rig VIVO del NPC renderizado. Para un NPC humano ese rig es el
    ' mismo que usa WM (nombres de hueso de FO4/SSE) ⇒ el archivo es intercambiable. Para un rig no
    ' humano (SuperMutant, robot, Behemoth) los nombres son otros y la pose no significará nada en el
    ' preview de WM: se exporta igual (es dato honesto del clip), pero queda dicho en el log.

    ''' <summary>El botón vive con la animación PAUSADA: hace falta una sesión de clip cargada (hay
    ''' frames que exportar) y que el player NO esté reproduciendo (el frame del slider es el que se
    ''' va a exportar, y durante el play cambia solo).</summary>
    Private Sub UpdateExportPoseEnabled()
        ButtonExportPose.Enabled = _animSession IsNot Nothing AndAlso Not IsAnimPlayingNow() AndAlso
                                   _renderHost IsNot Nothing AndAlso _renderHost.LastSkeletonInstance IsNot Nothing
    End Sub

    ''' <summary>Clip seleccionado en el combo (Nothing en "(None - static)"). Mismo cálculo que
    ''' ButtonSelectAnim_Click.</summary>
    Private Function CurrentAnimClip() As ResolvedAnimationClip
        If _animComboClips Is Nothing OrElse ComboAnim.SelectedIndex <= 0 Then Return Nothing
        Dim i = ComboAnim.SelectedIndex - 1
        If i < 0 OrElse i >= _animComboClips.Count Then Return Nothing
        Return _animComboClips(i)
    End Function

    ''' <summary>Nombre sugerido: el del clip (sin los adornos del combo: roles, "1st-person") más el
    ''' frame, que es lo que distingue una exportación de otra del mismo clip.</summary>
    Private Function SuggestedExportPoseName(frame As Integer) As String
        Dim clip = CurrentAnimClip()
        Dim nm = If(clip Is Nothing, "", If(String.IsNullOrWhiteSpace(clip.ClipName),
                                            IO.Path.GetFileNameWithoutExtension(clip.AnimationFile), clip.ClipName))
        If String.IsNullOrWhiteSpace(nm) Then nm = "Imported HKX Pose"
        ' ⛔ El sufijo de variante entra en el nombre: dos variantes del MISMO .hkx exportadas al mismo
        ' frame proponian el MISMO nombre, y el XML de poses es COMPARTIDO con Wardrobe Manager ⇒ el prompt
        ' de conflicto ofrecia pisar la anterior y las dos quedaban indistinguibles en el archivo.
        ' Se limpian los caracteres que no sobreviven a un nombre de pose (la flecha y el punto medio).
        Dim v = If(clip Is Nothing, "", clip.VarianteSufijo).Replace(" · ", " ").Replace("◀", "rev").Replace("↔", "pp").Trim()
        Return If(v = "", $"{nm}_f{frame}", $"{nm} ({v})_f{frame}")
    End Function

    Private Sub ButtonExportPose_Click(sender As Object, e As EventArgs) Handles ButtonExportPose.Click
        If _animSession Is Nothing OrElse IsAnimPlayingNow() Then Return

        ' Destino: el MISMO archivo de WM, <BodySlide>\PoseData\WardrobeManagerPoses.xml.
        Dim isSse = Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim
        Dim poseDir = PoseCatalog.ResolveBsPosesDir(isSse)
        If String.IsNullOrEmpty(poseDir) Then
            MsgBox("No BodySlide installation is configured for this game, so there is nowhere to write the pose." & Environment.NewLine &
                   "Pick the BodySlide/OutfitStudio executable in Edit Body → BodySlide (""Set BS exe…"") and try again.",
                   MsgBoxStyle.Exclamation, "Export pose")
            Return
        End If
        Dim outPath = IO.Path.Combine(poseDir, PoseCatalog.WmPosesFileName)

        Dim frame = Math.Max(0, CInt(Math.Round(SliderAnimFrame.Value)))
        Dim poseName = InputBox("Pose name", "Export pose", SuggestedExportPoseName(frame))
        If poseName Is Nothing Then Return
        poseName = poseName.Trim()
        If poseName = "" Then Return   ' Cancel, o nombre vacío = no exportar (WM rechaza el vacío igual)

        Try
            ' Mismo gesto que el OK del importador de WM: BuildPose del frame + los huesos del HKX que
            ' el esqueleto vivo NO tiene (BuildUnboundBoneWmData), que sólo se agregan al GUARDAR.
            Dim result = _animSession.BuildPose(frame, poseName, collectDiagnostics:=True)
            If result Is Nothing OrElse result.Pose Is Nothing OrElse result.ImportedBoneCount = 0 Then
                MsgBox("The animation frame did not match any bone of the rendered skeleton — nothing to export.",
                       MsgBoxStyle.Critical, "Export pose")
                Return
            End If
            Dim extra = _animSession.BuildUnboundBoneWmData(frame)
            If extra IsNot Nothing Then
                For Each kv In extra
                    If Not result.Pose.Transforms.ContainsKey(kv.Key) Then result.Pose.Transforms.Add(kv.Key, kv.Value)
                Next
            End If

            ' Conflicto de nombre contra el catálogo FRESCO (WM pudo haber escrito mientras tanto),
            ' con las mismas dos reglas de WM: otro archivo ⇒ error; mismo archivo ⇒ confirmar pisada.
            EnsurePoseCatalog(True)
            If _poseCatalog IsNot Nothing Then
                Dim existingKey As String = Nothing
                Dim existing = _poseCatalog.FindByName(poseName, existingKey)
                If existing IsNot Nothing Then
                    If Not String.Equals(If(existing.Filename, ""), outPath, StringComparison.OrdinalIgnoreCase) Then
                        MsgBox($"Pose {poseName} already exists in another file:" & Environment.NewLine & If(existing.Filename, "<unknown>"),
                               MsgBoxStyle.Critical, "Export pose")
                        Return
                    End If
                    If MsgBox($"Pose {poseName} already exists. Do you want to overwrite it?",
                              MsgBoxStyle.YesNo, "Export pose") = MsgBoxResult.No Then Return
                End If
            End If

            ' El catálogo puede seguir en Nothing si su lectura falló; escribir no depende de él (sólo
            ' registra la pose recién escrita), así que un catálogo vacío sirve igual.
            If _poseCatalog Is Nothing Then _poseCatalog = New PoseCatalog()
            _poseCatalog.WriteWmPoseXml(outPath, result.Pose)
            ' .Where(...).Count() y no .Count(lambda): Dictionary.Count es una PROPIEDAD que tapa la
            ' extensión de LINQ y el compilador la lee como indexación.
            Dim written = result.Pose.Transforms.Where(Function(t) Not t.Value.Isidentity).Count()
            Dim clipFile = If(CurrentAnimClip()?.AnimationFile, "")
            Logger.LogLazy(Function() $"[POSE-BAR] export pose='{poseName}' frame={frame} clip='{clipFile}' bones={written} (live={result.ImportedBoneCount}, unbound={If(extra Is Nothing, 0, extra.Count)}) skel='{result.SkeletonName}' -> '{outPath}'")

            ' Recargar el catálogo del disco para que la pose recién escrita quede en el combo (que
            ' está deshabilitado ahora — hay un clip activo — pero la va a mostrar al volver a estático).
            EnsurePoseCatalog(True)
            MsgBox($"Pose ""{poseName}"" exported ({written} bones, frame {frame})." & Environment.NewLine & outPath,
                   MsgBoxStyle.Information, "Export pose")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[POSE-BAR] export FAILED pose='{poseName}' frame={frame}: {ex}")
            MsgBox("Error exporting the pose: " & ex.Message, MsgBoxStyle.Critical, "Export pose")
        End Try
    End Sub

    ''' <summary>Aplica (o limpia, con <c>Nothing</c>) la pose estática y re-renderiza sólo la POSE.
    ''' <c>PlayingAnimation</c> queda en False: esto es un cambio puntual, no un playback — el control
    ''' re-encuadra como en cualquier otro cambio de pose (toggle de body weight, FMRS, etc.).</summary>
    Private Sub ApplyStaticPose(pose As Poses_class)
        If _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing OrElse
           _renderHost.LastRenderData Is Nothing OrElse _renderHost.PreviewCtl Is Nothing Then
            _staticPoseApplied = False
            Return
        End If
        Try
            ApplyPoseToLiveSkeletons(pose)
            _renderHost.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Pose, _renderHost.LastRenderData.Shapes)
            _renderHost.PreviewCtl.InvalidateRender()
            _staticPoseApplied = pose IsNot Nothing AndAlso pose.Transforms IsNot Nothing AndAlso pose.Transforms.Count > 0
            Logger.LogLazy(Function() $"[POSE-BAR] apply pose='{If(pose Is Nothing, "(none)", pose.ToString())}' bones={If(pose?.Transforms Is Nothing, 0, pose.Transforms.Count)}")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[POSE-BAR] ApplyStaticPose FAILED: {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}")
        End Try
    End Sub

    ' El slider tiny (TinySliderTextBox) muestra el frame inline y es el único control de frame (scrub).
    Private Sub SliderAnimFrame_ValueChanged(sender As Object, e As EventArgs) Handles SliderAnimFrame.ValueChanged
        If _animSuppress Then Return
        ApplyAnimFrame(CInt(Math.Round(SliderAnimFrame.Value)))
    End Sub

    Private Sub ButtonAnimPlay_Click(sender As Object, e As EventArgs) Handles ButtonAnimPlay.Click
        If IsAnimPlayingNow() Then
            StopAnimPlayback()
        ElseIf _animPlayer IsNot Nothing AndAlso SliderAnimFrame.Maximum > SliderAnimFrame.Minimum Then
            SetPlayingAnimation(True)
            SetEditorBarEnabledForPlayback(True)   ' deshabilita la barra de botones del editor hasta el Stop
            ' Durante el playback el slider es solo indicador de progreso (OnAnimPlaybackFrame le sigue
            ' seteando .Value por código): se bloquea el scrub manual y se re-habilita en StopAnimPlayback.
            SliderAnimFrame.Enabled = False
            ' ⛔ SignedFpsFromNumeric, NO Math.Max: el Max tiraba el SIGNO que ApplyAnimPlaybackInterval y
            ' NumericAnimFrameMs_ValueChanged si conservaban, asi que apretar Play reproducia hacia ADELANTE
            ' un clip autorado para ir al reves. Medido: 108 clips con playbackSpeed negativo en los dos
            ' juegos, y Bethesda lo usa como idioma (RifleIdle...ShuffleBackward apunta a ...ShuffleForward
            ' con speed -1: no autoran la animacion hacia atras, reproducen la de adelante en reversa).
            Dim fps = SignedFpsFromNumeric()
            _animPlayer.TargetFps = fps
            _animPlayer.Start(CInt(Math.Round(SliderAnimFrame.Value)))
            _animPlayer.BeginIdlePlayback(AddressOf OnAnimPlaybackFrame)
            ButtonAnimPlay.Text = "⏸"
            UpdateExportPoseEnabled()   ' reproduciendo NO se exporta: el frame del slider cambia solo
        End If
    End Sub

    Private Sub StopAnimPlayback()
        _animPlayer?.EndIdlePlayback()
        _animPlayer?.Stop()
        ' Stop/pausa → PlayingAnimation=False (igual que WM): reactiva el RenderTimer para rotar/zoom.
        SetPlayingAnimation(False)
        ' Restaura la barra de botones del editor (no-op si no hubo Play previo, vía el guard).
        SetEditorBarEnabledForPlayback(False)
        ' Re-habilita el scrub del slider si hay clip cargado (lo deshabilitó el Play). Los callers que
        ' descartan el clip (RefreshAnimBarForCurrentNpc / ResetAnimToTPose) lo re-deshabilitan luego.
        If SliderAnimFrame IsNot Nothing Then SliderAnimFrame.Enabled = SliderAnimFrame.Maximum > SliderAnimFrame.Minimum
        If ButtonAnimPlay IsNot Nothing Then ButtonAnimPlay.Text = "▶"
        If _animOverBudget Then NumericAnimFrameMs.ForeColor = SystemColors.ControlText : _animOverBudget = False
        ' Pausa con clip cargado = el estado en el que SÍ se exporta (los callers que además descartan
        ' el clip lo vuelven a apagar después, vía ResetAnimToTPose / RefreshAnimBarForCurrentNpc).
        UpdateExportPoseEnabled()
    End Sub

    ''' <summary>Callback del loop Application.Idle del player (hilo UI, igual que el viejo Tick).
    ''' Recibe el frame ya elegido por reloj real (el player ya dedup-ea por _lastShownFrame);
    ''' actualiza el slider, mide el render y aplica. Reemplaza al viejo AnimPlayTimer_Tick.</summary>
    Private Sub OnAnimPlaybackFrame(frame As Integer)
        If _animPlayer Is Nothing OrElse SliderAnimFrame.Maximum <= SliderAnimFrame.Minimum Then StopAnimPlayback() : Return
        _animSuppress = True : SliderAnimFrame.Value = frame : _animSuppress = False

        ' Medir el render del frame (InvalidateRender es síncrono) y, si excede el target (ms/frame =
        ' 1000/FPS), pintar el numeric en rojo: el playback no llega al target y salta frames.
        Dim budgetMs = Math.Max(1, CInt(Math.Round(1000.0 / Math.Max(1.0, CDbl(NumericAnimFrameMs.Value)))))
        Dim sw = System.Diagnostics.Stopwatch.StartNew()
        ApplyAnimFrame(frame)
        sw.Stop()
        If sw.ElapsedMilliseconds > budgetMs Then
            If Not _animOverBudget Then NumericAnimFrameMs.ForeColor = Color.Red : _animOverBudget = True
        Else
            If _animOverBudget Then NumericAnimFrameMs.ForeColor = SystemColors.ControlText : _animOverBudget = False
        End If
    End Sub

    ' (los nombres de los tipos de HDPT PNAM que la region de arriba declara)
#End Region


    Public Sub New(pluginManager As PluginManager,
                   dataPath As String,
                   Optional autoGenPluginsCache As List(Of SaveEsp_Form.ExistingPlugin) = Nothing,
                   Optional sidecars As Dictionary(Of String, BssliderSidecar.SidecarFile) = Nothing)
        ' Context + resolvers created BEFORE InitializeComponent: the Designer sets control state
        ' during InitializeComponent (e.g. CheckBox.Checked), firing CheckedChanged handlers —
        ' CategoryFilter_CheckedChanged → PopulateNPCTree reads _ctx.NpcCache, so the context must
        ' already exist. The Func delegates below are lazy (not invoked here); _appliedPresets and
        ' _lvlnDataCache are field initializers (already set before the ctor body runs).
        _pluginManager = pluginManager
        _ctx = New NpcRenderContext(pluginManager)
        ' Draft-aware resolution: let the parse path (GetParsedArmo/GetParsedArma) "see" unsaved in-memory
        ' ARMO/ARMA drafts so the preview renders them and the candidate lists can resolve their ARMA children.
        ' Same injection contract as the OutfitResolver leveled-list resolver — the resolver returns Nothing for
        ' a non-draft FormID (real-record path runs) and a freshly-synthesized *_Data for a draft (never cached).
        ' El borrador YA ES la vista canónica (ArmoDraft.Record As Canon.IArmo): no hace falta
        ' sintetizar nada, se devuelve directo.
        _ctx.ArmoDraftResolver = Function(fid) TryGetArmoDraft(fid)?.Record
        _ctx.ArmoIsPowerArmor = AddressOf ArmoIsPowerArmor
        _ctx.RaceIsPowerArmor = AddressOf RaceIsPowerArmor
        _ctx.ArmaDraftResolver = Function(fid) TryGetArmaDraft(fid)?.Record
        _ctx.MswpDraftResolver = Function(fid) BuildMswpDataFromDraft(fid)
        _materialResolver = New NpcMaterialResolver(_ctx, AddressOf ApplyPresetOverlayToNpcData, _appliedPresets)
        _stateResolver = New NpcStateResolver(_ctx, _materialResolver, _appliedPresets, _lvlnDataCache,
                                              Function() CurrentGenderFilter, AddressOf ResolveLmSkinTemplate)
        _morphPoseResolver = New NpcMorphPoseResolver(_ctx, AddressOf ApplyPresetOverlayToNpcData, Function() _renderHost, _appliedPresets,
                                                      AddressOf ResolveOverlayTemplate)
        _faceTintResolver = New NpcFaceTintResolver(_ctx, _materialResolver, Function() _renderHost, _appliedPresets)
        _mountingResolver = New NpcMountingResolver(_ctx, _stateResolver)
        _meshCollector = New NpcMeshCollector(_ctx, _materialResolver, _stateResolver, _mountingResolver,
                                              AddressOf ArmoIsPowerArmor, AddressOf RaceIsPowerArmor)
        _skinLivePreview = New NpcSkinLivePreview(_ctx, _materialResolver, _meshCollector, _faceTintResolver,
                                                 Function() _renderHost, _appliedPresets,
                                                 AddressOf ResolveLmSkinTemplate, Function() _previewRequestVersion)
        InitializeComponent()
        _dataPath = If(dataPath, "")
        ' Preflight_Form already filled FilesDictionary. Mark the gate as completed so the two
        ' EnsureAssetDictionaryAsync call sites don't re-trigger Fill_DictionaryAsync (which clears
        ' the Dictionary as its first step, so a re-trigger would wipe the work the preflight did).
        _assetDictionaryLoadTask = Task.CompletedTask
        ' Cache of NPC_Manager auto-generated plugins, optionally pre-populated by Preflight.
        ' If passed, the first Save ESP dialog opens instantly without re-scanning Data\.
        ' If Nothing, the cache lazy-fills on first ScanForAutoGeneratedPlugins call.
        _autoGenPluginsCache = autoGenPluginsCache
        ' Sidecar hydration: seed _appliedPresets with BodyMorphs + SkinTemplate entries from
        ' the user's plugins' .bssliders files. NPCs without a sidecar entry are unaffected;
        ' the Has* flags on the synthesized presets stay False so vanilla fields (HeadParts,
        ' tints, weights, MRSV, FMRI/FMRS, MSDK/MSDV) are preserved from the raw record.
        HydrateAppliedPresetsFromSidecars(sidecars)
        ' Snapshot WHICH NPCs woke up with F4SE data persisted on disk (sidecar row → BodyGen .ini →
        ' VMAD apply-script): before this point _appliedPresets is empty, so after hydration its keys
        ' are exactly the sidecar-backed set. "Reset (discard changes)" consults it to keep such NPCs
        ' dirty so the next Save prunes their disk state (WYSIWYG routing);
        ' ApplyPostSaveReadback keeps it current as saves add/remove sidecar rows.
        For Each hydratedFid In _appliedPresets.Keys
            _sidecarBackedNpcs.Add(hydratedFid)
        Next
        ComboBoxPreviewMode.SelectedIndex = 0
        ComboBoxGender.SelectedIndex = 0
    End Sub

    ''' <summary>NPCs whose F4SE-only edits (BodyMorphs / Skin template / Overlays / SSE co-save fields)
    ''' are persisted on disk — a sidecar row, and with it the BodyGen .ini row and (overlays/skin/
    ''' transforms) the VMAD apply-script in the saved plugin. Seeded in the ctor from the sidecar
    ''' hydration; updated by <see cref="ApplyPostSaveReadback"/> when a Save writes or prunes rows.
    ''' Consumer: <see cref="MenuItemResetOverlay_Click"/> — a reset NPC in this set stays dirty so the
    ''' next Save propagates the revert to disk/game instead of stranding it in the preview.</summary>
    Private ReadOnly _sidecarBackedNpcs As New HashSet(Of UInteger)

    ''' <summary>Seed <see cref="_appliedPresets"/> from the preflight's <c>.bssliders</c> sidecars.
    ''' The merge itself lives in <see cref="BssliderSidecar.HydratePresets"/> so the headless bake
    ''' (<c>--bake-all</c>) starts from the identical overlay state; Edit Body / Load LM / Paste all
    ''' mutate or replace the synthesized preset in-place afterwards, so this is just the start state.</summary>
    Private Sub HydrateAppliedPresetsFromSidecars(sidecars As Dictionary(Of String, BssliderSidecar.SidecarFile))
        BssliderSidecar.HydratePresets(sidecars, _pluginManager, _appliedPresets)
    End Sub

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        SearchDebounceTimer.Interval = 250
        ' Raza EFECTIVA para el BAKE: los caminos de bake resuelven el NPC vía NpcRecordOverlay.
        ' ResolveOverlaidNpcData (crudo + preset LM), que NO ve el NpcRecordOverride del editor. Este hook
        ' le da la raza pisada (Edit NPC → Race) para que FaceTint/FaceGen se horneen con el MISMO catálogo
        ' que el render (state.RaceFormID) — render == bake. CLI/probes no lo setean → no-op.
        NpcRecordOverlay.EffectiveRaceResolver =
            Function(fid As UInteger) As UInteger
                Dim ov = TryGetNpcRecordOverride(fid)
                Return If(ov IsNot Nothing AndAlso ov.RaceFormID.HasValue, ov.RaceFormID.Value, 0UI)
            End Function
        ' Game is NOT re-pinned here anymore — Preflight_Form's selector already set Config_App.Current.Game
        ' (and re-initialized the plugin encoding for it) before this form was constructed. Forcing FO4 here
        ' would silently override an SSE session picked in the preflight.
        ' NOTE: plugin text encoding (InitializeForGame + SetLanguage + ApplyOverrideIni) AND the
        ' Logger init both live in Program.Main, BEFORE the preflight loads any plugin — this
        ' follows a configure → load → edit order. Do NOT re-init either here; that would run
        ' AFTER the preflight already loaded plugins, and would lose every startup-time log.
        ' Rótulos + tooltips de los 10 toggles según el juego pineado en el Preflight: los canales son los
        ' mismos, pero lo que cuelga de cada uno no (FMRS vs node transforms, MWGT vs NAM7, ARMA SCLP vs
        ' sculpt RaceMenu, [U]/[A] vs outfit/accesorios). Ver RenderToggleLabels.
        ApplyRenderToggleLabelsForGame()
        RestoreMainWindowBounds()
        ' Restore persisted UI toggles BEFORE InitializePreview (Shown handler snapshots
        ' checkbox state into _renderHost.Toggles via RenderToggles.FromMainCheckBoxes).
        CheckBoxRenderGore.Checked = NPC_Config.Current.RenderGore
        ' "Show:" category filters (Section 1 of the tree) are likewise persisted. The CategoryFilter
        ' CheckedChanged handler is wired via Handles, so these assignments may fire PopulateNPCTree before
        ' LoadDataAsync has any data — harmless (it guards on _allNPCs.Count and just rebuilds an empty tree,
        ' which LoadDataAsync repopulates with the now-seeded filter).
        CheckBoxCatUnique.Checked = NPC_Config.Current.ShowCatUnique
        CheckBoxCatGeneric.Checked = NPC_Config.Current.ShowCatGeneric
        CheckBoxCatTemplate.Checked = NPC_Config.Current.ShowCatTemplate
        CheckBoxCatUnused.Checked = NPC_Config.Current.ShowCatUnused
#If DEBUG Then
        AddSseFoldedRenderDebugToggle()   ' PROVISORIO (diagnóstico) — DEBUG ONLY: en Release ni siquiera se crea
        AddHavokPhysicsDebugCombo()       ' DEBUG ONLY (misma ley que el de arriba): en Release ni se crea
#End If
        LoadDataAsync()
    End Sub

    ''' <summary>Restaura posición/tamaño/maximizado guardados en el cierre anterior (NPC_Config
    ''' MainWindow*). Sin config previa (Width = 0) no toca nada: quedan los defaults del Designer
    ''' (CenterScreen + Maximized). El rectángulo guardado se valida contra las pantallas ACTUALES —
    ''' si no intersecta ninguna (monitor desconectado, resolución cambiada) se descarta y se centra,
    ''' porque una ventana en coordenadas de un escritorio inexistente es irrecuperable con el ratón.</summary>
    Private Sub RestoreMainWindowBounds()
        Dim cfg = NPC_Config.Current
        If cfg.MainWindowWidth <= 0 OrElse cfg.MainWindowHeight <= 0 Then
            ' Nunca se guardó geometría: sólo se honra el estado maximizado.
            WindowState = If(cfg.MainWindowMaximized, FormWindowState.Maximized, FormWindowState.Normal)
            Return
        End If

        ' MinimumSize (1024x720) manda: un tamaño guardado menor lo pondría el propio WinForms igual,
        ' pero clampeamos aquí para que el test de visibilidad use el rectángulo REAL.
        Dim w = Math.Max(cfg.MainWindowWidth, MinimumSize.Width)
        Dim h = Math.Max(cfg.MainWindowHeight, MinimumSize.Height)
        Dim rect As New Rectangle(cfg.MainWindowLeft, cfg.MainWindowTop, w, h)

        Dim visible = Screen.AllScreens.Any(Function(s) s.WorkingArea.IntersectsWith(rect))
        If visible Then
            StartPosition = FormStartPosition.Manual
            Bounds = rect
        Else
            ' Rectángulo huérfano: conservamos el TAMAÑO (es preferencia del usuario) y centramos.
            StartPosition = FormStartPosition.CenterScreen
            Size = New Size(w, h)
        End If

        WindowState = If(cfg.MainWindowMaximized, FormWindowState.Maximized, FormWindowState.Normal)
    End Sub

    ''' <summary>Vuelca la geometría de la ventana a NPC_Config. Usa RestoreBounds (el rectángulo
    ''' "normal") y NO Bounds: cerrando maximizada o minimizada, Bounds sería el del monitor entero o
    ''' el de la barra de tareas, y al reabrir + des-maximizar la ventana quedaría con ese tamaño.
    ''' El flush a npc_config.json lo hace el SaveConfig() del propio FormClosing.</summary>
    Private Sub CaptureMainWindowBounds()
        Dim r = If(WindowState = FormWindowState.Normal, Bounds, RestoreBounds)
        ' RestoreBounds puede venir vacío si el form se maximizó sin haber estado nunca en Normal.
        If r.Width > 0 AndAlso r.Height > 0 Then
            NPC_Config.Current.MainWindowLeft = r.Left
            NPC_Config.Current.MainWindowTop = r.Top
            NPC_Config.Current.MainWindowWidth = r.Width
            NPC_Config.Current.MainWindowHeight = r.Height
        End If
        ' Minimizada no es un estado que valga la pena restaurar: se trata como Normal.
        NPC_Config.Current.MainWindowMaximized = (WindowState = FormWindowState.Maximized)
    End Sub

    ''' <summary>PROVISORIO — herramienta de diagnóstico, a ELIMINAR junto con
    ''' <see cref="NPC_Config.SseRenderFoldedPath"/> y <c>NpcFaceTintResolver.ApplySseFacetintFolded</c>.
    ''' Agrega (SOLO en SSE) un checkbox a la toolbar que conmuta el render entre:
    '''   OFF = camino normal (slot 0 complexion + slot 3 detail + slot 6 facetint; el shader hace
    '''         <c>softlight(complexion, facetint) × amplify(detail)</c>, igual que el engine), y
    '''   ON  = camino PLEGADO (lo que el bake escribe: el fold en el slot 0 y los slots 3/6 neutralizados,
    '''         de modo que el shader haga la identidad y muestre el diffuse plegado).
    ''' Si el pliegue es correcto, el TONO DE PIEL debe ser IDÉNTICO en ambos. Re-renderiza al conmutar.
    ''' Se crea por código a propósito (no en el Designer) para que borrarlo sea trivial.</summary>
    Private Sub AddSseFoldedRenderDebugToggle()
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return
        Dim cb As New CheckBox With {
            .Name = "CheckBoxSseRenderFolded",
            .Text = "SSE: folded render (debug)",
            .AutoSize = True,
            .Checked = NPC_Config.Current.SseRenderFoldedPath,
            .Margin = New Padding(12, 8, 3, 3)
        }
        AddHandler cb.CheckedChanged,
            Sub()
                ' El propio render marca/desmarca el checkbox (UpdateSseFoldedToggleAvailability) cuando el NPC
                ' pliega a la fuerza. Sin este guard, ese Checked=True re-entraría acá y dispararía OTRA recarga
                ' completa ⇒ bucle infinito de renders. Sólo el click del usuario debe recargar.
                If _suppressFoldToggleEvent Then Return
                NPC_Config.Current.SseRenderFoldedPath = cb.Checked
                ' RECARGA COMPLETA (no RenderFromCurrentSelection): el camino plegado MUTA la textura del complexion
                ' en el diccionario del modelo. Un simple re-render dejaría el plegado "pegado" al destildar. Esto
                ' reconstruye geometría + materiales + tints desde los records ⇒ el toggle es reversible de verdad.
                ReloadCurrentNpcFull()
            End Sub
        _checkBoxSseRenderFolded = cb
        PanelActionsToolbar.Controls.Add(cb)


        ' PROVISORIO (mismo contrato que el checkbox de arriba): SseMeasureFoldParity es <JsonIgnore> (no
        ' persiste — persistirlo fue el bug del compose duplicado para siempre) y NO tenía NINGUNA UI: sólo se
        ' podía encender desde el debugger. Este checkbox lo enciende para la corrida en la que se quiere MEDIR
        ' (duplica el compose: +3,6 s por render a 1024², medido). Al tildar recarga el NPC ⇒ el fold re-corre
        ' y, si el NPC pliega en modo GPU, loguea "[SSE-FOLD] PARITY (sandbox): rmsCPUvsGPU=..." en fo4lib.log.
        Dim cbParity As New CheckBox With {
            .Name = "CheckBoxSseMeasureFoldParity",
            .Text = "SSE: measure fold parity (debug)",
            .AutoSize = True,
            .Checked = NPC_Config.Current.SseMeasureFoldParity,
            .Margin = New Padding(12, 8, 3, 3)
        }
        AddHandler cbParity.CheckedChanged,
            Sub()
                NPC_Config.Current.SseMeasureFoldParity = cbParity.Checked
                ' Recarga completa por la misma razón que el toggle de arriba: el fold mutó texturas del dict,
                ' y además queremos que la medición corra YA (no en algún render futuro).
                ReloadCurrentNpcFull()
            End Sub
        PanelActionsToolbar.Controls.Add(cbParity)
    End Sub

    ''' <summary>
    ''' DEBUG ONLY — combo para conmutar la física Havok de cloth EN VIVO y ver el A/B con y sin ella.
    ''' <para>Se construye EN RUNTIME (no está en el Designer) por la misma razón que los dos toggles de
    ''' fold de arriba: es diagnóstico. La llamada vive dentro de un <c>#If DEBUG</c>, así que en Release
    ''' el control NO EXISTE — no hay que acordarse de ocultarlo.</para>
    ''' <para>⛔ FO4 ÚNICAMENTE, misma lógica que <c>GroupConvFold.Visible = isSse</c> en CharGenOptionsForm:
    ''' Havok Cloth es exclusivo de Fallout 4 (SkyrimSE.exe no declara ni una sola clase <c>hcl</c>, medido
    ''' sobre la reflexión del exe), y un control visible que no mueve nada es un defecto. El gate DURO de
    ''' verdad sigue estando en <c>Config_App.ApplyHavokPhysicsSettings</c> — esto sólo evita ofrecer una
    ''' perilla muerta.</para>
    ''' </summary>
    Private Sub AddHavokPhysicsDebugCombo()
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Fallout4 Then Return

        Dim lbl As New Label With {
            .Name = "LabelHavokPhysicsDebug",
            .Text = "Cloth physics (debug):",
            .AutoSize = True,
            .Margin = New Padding(12, 12, 3, 3)
        }

        Dim cmb As New ComboBox With {
            .Name = "ComboBoxHavokPhysicsDebug",
            .DropDownStyle = ComboBoxStyle.DropDownList,
            .Width = 150,
            .Margin = New Padding(3, 8, 3, 3)
        }
        ' El orden de los ítems ES el valor de `HavokPhysicsMode` (0/1/2). No es coincidencia: el índice se
        ' escribe tal cual en `Setting_HavokPhysicsMode`, que `ApplyHavokPhysicsSettings` castea al enum.
        cmb.Items.AddRange(New Object() {"Off", "Follow only", "Full simulation"})

        ' ⛔ El ÍNDICE INICIAL SE SIEMBRA ANTES DE ENGANCHAR EL HANDLER. Al revés, `SelectedIndex = ...`
        ' dispararía SelectedIndexChanged durante el Load: guardaría la config y forzaría un render con
        ' `_renderHost` todavía en Nothing.
        Dim cfg = Config_App.Current
        cmb.SelectedIndex = If(cfg.Setting_HavokPhysics, Math.Max(0, Math.Min(2, cfg.Setting_HavokPhysicsMode)), 0)

        AddHandler cmb.SelectedIndexChanged,
            Sub()
                Dim idx = cmb.SelectedIndex
                If idx < 0 Then Return
                Dim c = Config_App.Current
                If c Is Nothing Then Return

                ' "Off" = apagar el interruptor. Los otros dos encienden y eligen hasta dónde llega.
                ' Se guarda el MODO aunque sea Off para no perder la elección del usuario al volver a prender.
                c.Setting_HavokPhysics = (idx > 0)
                If idx > 0 Then c.Setting_HavokPhysicsMode = idx
                ' La config es la FUENTE DE VERDAD (el pase de render la vuelca en cada frame). Volcarla
                ' acá también hace que apagar limpie la capa YA — el setter de `Enabled` llama a
                ' ClearAllTouchedSkeletons en la transición True→False — sin esperar a un frame de pose.
                c.ApplyHavokPhysicsSettings()
                Config_App.SaveConfig()

                ' Tirar el estado vivo: al cambiar de modo la próxima pasada RESIEMBRA desde la piel posada
                ' y corre los `SettleSteps` (10, el `uNumSimSettleSteps` del motor). Sin esto el combo
                ' mostraría la tela a medio caer del modo anterior, que no es el A/B que se quiere ver.
                FO4_Base_Library.Havok.Physics.HavokClothSimulation.ResetAll()

                ' POSE dirty: el paso de física vive en la rama `needsPoseUpdate` del pipeline (Render.vb).
                ' Con Morphs o Textures dirty el combo no movería NADA — medido al escribir esto.
                If _renderHost Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return
                _renderHost.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Pose, _renderHost.LastRenderData.Shapes)
                _renderHost.PreviewCtl.InvalidateRender()
            End Sub

        PanelActionsToolbar.Controls.Add(lbl)
        PanelActionsToolbar.Controls.Add(cmb)
    End Sub

    ''' <summary>PROVISORIO (con <see cref="AddSseFoldedRenderDebugToggle"/>). Referencia al checkbox para poder
    ''' DESHABILITARLO cuando el NPC actual pliega SÍ O SÍ.</summary>
    Private _checkBoxSseRenderFolded As CheckBox
    Private _suppressFoldToggleEvent As Boolean

    ''' <summary>PROVISORIO. Refleja en el checkbox si el NPC actual PUEDE mostrarse sin plegar. Cuando el NPC
    ''' tiene skee MASKT u overlays de cara, el render pliega OBLIGATORIAMENTE (igual que el bake) — no existe un
    ''' "sin plegar" fiel que mostrar —, así que el checkbox se deshabilita y se marca, para que quede claro que ahí
    ''' el fold no es una elección. En vanilla queda habilitado: ahí sí se puede comparar con y sin pliegue.</summary>
    Friend Sub UpdateSseFoldedToggleAvailability(foldIsMandatory As Boolean)
        Dim cb = _checkBoxSseRenderFolded
        If cb Is Nothing Then Return
        If cb.InvokeRequired Then
            cb.BeginInvoke(Sub() UpdateSseFoldedToggleAvailability(foldIsMandatory))
            Return
        End If
        _suppressFoldToggleEvent = True
        Try
            If foldIsMandatory Then
                cb.Enabled = False
                cb.Checked = True
                cb.Text = "SSE: folded render (forced: this NPC has MASKT/overlays)"
            Else
                cb.Enabled = True
                cb.Checked = NPC_Config.Current.SseRenderFoldedPath
                cb.Text = "SSE: folded render (debug)"
            End If
        Finally
            _suppressFoldToggleEvent = False
        End Try
    End Sub

    ''' <summary>PreviewControl initialization happens in Shown (not Load) — same pattern as
    ''' WM (Wardrobe_Manager_Form.OSPManager_Form_Shown / CreatefromNif_Form.Create_from_Nif_2_Shown).
    ''' This guarantees the Panel container has its final layout dimensions BEFORE the GL control
    ''' is added, so the first ApplyResize calls GL.Viewport with the correct width/height and
    ''' the projection matrix uses the right aspect ratio. Doing this in Load gives the control
    ''' the designer-time size and the first ResetCamera computes distH/distW from a wrong aspect.</summary>
    Private Sub MainForm_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        InitializePreview()
    End Sub

    Private Sub InitializePreview()
        ' Remove the LabelStatus "Loading..." placeholder from the preview host (sin afectar la toolbar)
        If LabelStatus IsNot Nothing AndAlso LabelStatus.Parent IsNot Nothing Then
            LabelStatus.Parent.Controls.Remove(LabelStatus)
        End If
        ' GLControl en su panel exclusivo (PanelPreviewHost), fila 3 (Percent 100) del
        ' PanelPreviewLayout (TableLayoutPanel). Las dos toolbars van en las filas 1 y 2 (AutoSize):
        ' cuando PanelActionsToolbar (FlowLayoutPanel, WrapContents) envuelve a una 2da fila, el TLP
        ' fija el ancho de columna antes de medir, así que la fila crece y el render baja sin solapar.
        _previewControl = New PreviewControl() With {.Dock = DockStyle.Fill}
        PanelPreviewHost.Controls.Add(_previewControl)
        _previewControl.ApplyResize(True)

        ' Render-pipeline state lives here, not on MainForm. Constructed once the preview
        ' control exists. The Tick handler stays on MainForm during this phase — the editor
        ' previews (future phase) will create their own NpcRenderHost and own their own Tick
        ' handler local to the editor form.
        _renderHost = New NpcRenderHost(_previewControl) With {
            .AppliedPresets = _appliedPresets,
            .Toggles = RenderToggles.FromMainCheckBoxes(Me)
        }

        ' Ver HookSkinningToggleRefresh: el toggle GPU/CPU de la cámara re-corre la geometría pero NO el face-tint.
        HookSkinningToggleRefresh(_previewControl, _renderHost)
    End Sub

    ''' <summary>Cablea el refresh del face-tint para el toggle GPU/CPU skinning del menu de camara de CUALQUIER
    ''' preview (main, editores y pickers).
    ''' <para>Problema: al togglear, la libreria solo re-corre la GEOMETRIA (skin + morphs, por su MarkDirty
    ''' interno) y NO re-aplica el face-tint ni el fold, asi que el diffuse plegado queda pegado en el
    ''' diccionario de texturas mientras el MaterialData nuevo pierde su estado per-mesh (SkinToneBaked,
    ''' FaceTintOverlay_ID) y la cara sale oscura. La libreria levanta SkinningModeToggled justo para que la app
    ''' re-corra SU pipeline, y no lo escuchaba nadie.</para>
    ''' <para>Fix: se re-arma el hook post-upload; cuando el re-render de geometria termina con las texturas
    ''' listas, la libreria lo dispara SYNC y ahi se restaura el pristine y se RE-COMPONE el face-tint sobre el
    ''' MaterialData nuevo, in-place y sin recargar el NIF. Si el refresh liviano no puede (sin pristine), cae a
    ''' una recarga completa de ESE host. Un handler por (control, host).</para></summary>
    Friend Sub HookSkinningToggleRefresh(ctl As PreviewControl, host As NpcRenderHost)
        If ctl Is Nothing OrElse host Is Nothing Then Return
        AddHandler ctl.SkinningModeToggled,
            Sub(sender As PreviewControl)
                If host.LastRenderedState Is Nothing OrElse ctl.Intent Is Nothing Then Return
                Dim capturedVersion = _previewRequestVersion   ' in-place: NO se bumpea (no es una request nueva)
                ctl.Intent.PostTextureUploadAction =
                    Sub(model)
                        If host.IsDisposed Then Return
                        ' Si se disparó una request nueva (cambio de NPC en algún host) entretanto, no tocar nada.
                        If capturedVersion <> _previewRequestVersion Then Return
                        If RefreshFaceTintLivePreview(host) Then Return
                        ' No se pudo refrescar in-place (sin estado/pristine) ⇒ recarga completa de ESTE host.
                        Dim st = host.LastRenderedState
                        If st Is Nothing Then Return
                        Dim fid = If(st.ModelSourceFormID <> 0UI, st.ModelSourceFormID, st.FormID)
                        Dim reloadTask = RenderInHostAsync(host, fid)
                    End Sub
            End Sub
    End Sub

    Private Async Sub LoadDataAsync()
        ' Plugins + BA2/BSA archives were loaded by Preflight_Form before MainForm was even
        ' constructed. _pluginManager and FilesDictionary_class are already populated. Here we
        ' just parse the NPC records out of the loaded plugins and populate the tree.
        Try
            ToolStripProgressBar1.Visible = False

            SetStatus("Parsing NPC records...")
            _pendingLoadWarnings.Clear()
            Await Task.Run(Sub() ParseAllNPCs())

            ' Los avisos de records que no parsean se acumulan en el worker y se muestran ACA: ya volvimos al
            ' hilo de UI y hay owner, asi que el box es modal de verdad. Ver _pendingLoadWarnings.
            FlushPendingLoadWarnings()

            PopulateNPCTree()

    
            SetStatus($"Loaded {_directlyPlacedNPCFormIDs.Count} placed NPCs + {_finalLVLNFormIDs.Count} leveled lists from {_pluginManager.Plugins.Count} plugins" &
                      If(_tiemposDeCarga = "", "", " — " & _tiemposDeCarga))

            ' Warm the anim-clip cache for the races present in the load order on a background thread, so the
            ' first NPC selection of each race does NOT pay the ~16 s behavior walk on the UI thread. Bounded:
            ' only the DISTINCT race+gender combos actually present, clip METADATA only (no raw anim bytes).
            PreloadAnimRacesInBackground()

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
            MessageBox.Show(ex.ToString(), "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub


    ''' <summary>Avisos de la carga (records que no parsean) juntados por el WORKER y mostrados por el hilo
    ''' de UI despues del Await, con owner.
    ''' <para>NO se muestran donde se producen: `ParseAllNPCs` corre siempre dentro de `Await Task.Run`, y
    ''' `LoadDataAsync` se lanza SIN await desde el ctor, asi que MainForm ya esta visible y bombeando
    ''' mensajes. Un `MessageBox` creado en un hilo del pool y sin owner no es modal respecto de la ventana
    ''' principal: se va atras al primer click y deja la carga colgada, con el worker bloqueado y la UI libre
    ''' para re-entrar a `PopulateNPCTree` -> `RebuildTreeModelCache` sobre los mismos diccionarios.</para></summary>
    ''' <summary>Muestra y vacía los avisos acumulados por el worker. Corre en el hilo de UI y con <c>Me</c>
    ''' como owner, así que el box es modal de verdad.
    ''' <para>Es un MÉTODO y no código pegado dentro de <c>LoadDataAsync</c> porque ese camino corre UNA sola
    ''' vez, en el arranque. Los avisos también los produce <c>BuildNPCClassification</c>, al que se vuelve
    ''' desde el rebuild post-Save y desde el camino frío de <c>PopulateNPCTree</c>. Sin drenar ahí, el caso con
    ''' daño real es concreto: el readback post-save remonta el plugin RECIÉN ESCRITO y, si trae un LVLN que la
    ''' app no puede releer, el aviso nace y muere sin que nadie lo vea — o sea que escribimos algo que no
    ''' podemos volver a leer y nos callamos.</para>
    ''' <para>Sin avisos pendientes no hace nada, así que es seguro llamarlo desde cualquier lado. Si lo llama
    ''' un hilo de fondo se difiere al de UI en vez de abrir un box sin owner (que no sería modal y colgaría
    ''' la carga detrás de la ventana principal).</para></summary>
    Private Sub FlushPendingLoadWarnings()
        If _pendingLoadWarnings.Count = 0 Then Return
        ' Sin handle todavia no se puede ni diferir; los avisos quedan pendientes y los muestra el proximo
        ' drenaje. Perder el aviso seria peor, pero reventar en el arranque tambien.
        If IsDisposed OrElse Not IsHandleCreated Then Return
        If InvokeRequired Then
            BeginInvoke(Sub() FlushPendingLoadWarnings())
            Return
        End If
        Dim texto = String.Join(vbCrLf & vbCrLf, _pendingLoadWarnings)
        _pendingLoadWarnings.Clear()
        MessageBox.Show(Me,
            texto & vbCrLf & vbCrLf & "The rest of the load order loaded normally.",
            "Records skipped", MessageBoxButtons.OK, MessageBoxIcon.Warning)
    End Sub

    Private ReadOnly _pendingLoadWarnings As New List(Of String)

    Private Sub ParseAllNPCs()
        _allNPCs.Clear()
        ' Drop any parse/skeleton-byte caches from a prior load order before re-establishing the NPC
        ' universe — they're keyed by FormID/(race,gender) which could collide across plugin sets.
        InvalidateParseCaches()

        ' El parse en bloque llena la cache (_allNPCs / _ctx.NpcCache) que consume el render. Cada NPC_Data
        ' lleva el ARBOL del record, asi que no hay una segunda copia de los campos ni un parser liviano
        ' aparte: lo que se lee una vez es lo que despues se muestra, se hornea y se guarda.
        Dim sw = System.Diagnostics.Stopwatch.StartNew()
        Dim npcRecords = _pluginManager.GetNPCs()
        Dim getNpcsMs = sw.ElapsedMilliseconds
        sw.Restart()
        ' RecordParsers lanza deliberadamente en varios sitios ("fail loud rather than silently corrupt").
        ' Este camino —el que carga TODOS los NPC— tiene que CAPTURAR y REPORTAR cada fallo (ver abajo):
        ' un `Catch` que sólo trague la excepción deja al NPC ausente del árbol sin un solo mensaje.
        Dim parseFailures As New List(Of String)
        Dim parseFailureTotal As Integer = 0
        ' EL ÁRBOL DE CADA NPC SE ARMA EN PARALELO. Cada uno sale de SU record y de nada más: no hay
        ' estado compartido que dependa del orden.
        ' <para>Que armar árboles concurrentemente sea seguro NO es una suposición nueva: es lo que hace
        ' cada bake. `FaceGenBuilder.BuildCharGen` corre dentro del `Parallel.ForEach` por NPC de
        ' `BakeAllRunner`, y ahí adentro llama a `NpcRecordOverlay.GetParsedNpc` (⇒ `RecordParsers.ParseNPC`)
        ' y a `Canon.CanonRecords.Race/Otft/Arma/Armo`. (El bucle que arma la LISTA de objetivos de
        ' `BakeAllRunner` sí es secuencial; el paralelismo relevante es el `Parallel.ForEach` por NPC.)</para>
        ' <para>EL RESULTADO SE DEPOSITA POR ÍNDICE Y SE VUELCA EN ORDEN. `_allNPCs` queda exactamente en
        ' el orden en que vino `GetNPCs()` —el mismo que daba el bucle secuencial—, y los fallos se
        ' reportan también por índice: si se acumularan según termina cada hilo, la lista de las primeras
        ' 20 fallas cambiaría de una corrida a otra.</para>
        Dim cuantos = npcRecords.Count
        If cuantos > 0 Then
            Dim parseados(cuantos - 1) As NPC_Data
            Dim fallos(cuantos - 1) As String
            System.Threading.Tasks.Parallel.For(0, cuantos,
                Sub(i)
                    Dim rec = npcRecords(i)
                    Try
                        parseados(i) = RecordParsers.ParseNPC(rec, _pluginManager)
                    Catch ex As Exception
                        fallos(i) = $"{rec.SourcePluginName}:{rec.Header.FormID:X8} — {ex.GetType().Name}: {ex.Message}"
                    End Try
                End Sub)
            For i = 0 To cuantos - 1
                If fallos(i) IsNot Nothing Then
                    ' Se sigue cargando el resto (un record roto no puede costar la sesión entera), pero se
                    ' cuenta y se nombra. El primer puñado va al log con detalle; el total se reporta al terminar.
                    parseFailureTotal += 1
                    If parseFailures.Count < 20 Then parseFailures.Add(fallos(i))
                Else
                    _allNPCs.Add(parseados(i))
                End If
            Next
        End If
        If parseFailureTotal > 0 Then
            ' EL AVISO NO PUEDE VIVIR SOLO EN EL LOGGER: en Release `Logger.Enabled` está clavado en False
            ' (sólo el CLI prende `Logger.AllowInReleaseBuilds`), así que `LogLazy` sale en su primera línea
            ' y el usuario final no ve nada — el NPC queda ausente del árbol sin un solo mensaje. Un
            ' "fail loud" que sólo grita en Debug no es un fail loud.
            ' El MessageBox espeja el que ya usa el Preflight para los plugins excluidos por master faltante.
            Dim n = parseFailureTotal
            Dim detail = String.Join(vbLf & "    ", parseFailures)
            Logger.LogLazy(Function() $"[LOAD] {n} NPC_ record(s) could not be parsed and are ABSENT from the " &
                                      "list (they would also be absent from any bake):" & vbLf & "    " & detail)
            ' NO se muestra acá: ParseAllNPCs corre SIEMPRE dentro del `Await Task.Run` de `LoadDataAsync`,
            ' o sea en un hilo del pool. Un MessageBox desde ahí no es modal respecto de MainForm (que ya está visible y
            ' bombeando mensajes, porque LoadDataAsync se lanza sin await desde el ctor): el usuario clickea la
            ' ventana principal, el box se va atrás y la carga queda colgada. Se ACUMULA y lo muestra el hilo de
            ' UI después del Await, con owner — igual que el aviso del Save.
            _pendingLoadWarnings.Add(
                $"{n} NPC record(s) could not be parsed and are NOT in the list — they would also be missing " &
                "from any bake." & vbCrLf & "First failures:" & vbCrLf & "  " &
                String.Join(vbCrLf & "  ", parseFailures))
        End If
        Dim parseMs = sw.ElapsedMilliseconds
        sw.Restart()
        ' Resolve inherited FullName for NPCs that inherit BaseData from a template
        ResolveInheritedFullNames()
        Dim resolveMs = sw.ElapsedMilliseconds
        sw.Restart()
        ' La clave se calcula UNA vez por NPC. Tiene que ser DESPUES de ResolveInheritedFullNames,
        ' que le ESCRIBE el nombre heredado al record: con la clave tomada antes, todo NPC que hereda
        ' el nombre de su plantilla ordenaria por su EditorID en vez de por el nombre.
        _npcSortKeyCache.Clear()
        _npcHeredaAparienciaCache.Clear()
        For Each npc In _allNPCs
            SembrarClaveDeOrden(npc)
        Next
        OrdenarNpcs()
        Dim sortMs = sw.ElapsedMilliseconds
        sw.Restart()
        RebuildTreeModelCache()
        Dim cacheMs = sw.ElapsedMilliseconds
        sw.Stop()
        ' Los cinco tiempos se medían y se TIRABAN: cinco variables asignadas y nunca leídas. Ahora
        ' quedan en el texto de estado, que es lo único que el usuario final ve — el log está
        ' clavado en apagado en Release, así que dejarlos ahí sería no medirlos.
        _tiemposDeCarga = $"{getNpcsMs + parseMs + resolveMs + sortMs + cacheMs} ms " &
                          $"(records {getNpcsMs} · árbol {parseMs} · nombres {resolveMs} · " &
                          $"orden {sortMs} · caches {cacheMs})"
    End Sub

    ''' <summary>Desglose del último <see cref="ParseAllNPCs"/>, para el texto de estado.</summary>
    Private _tiemposDeCarga As String = ""




    ''' <summary>Cuanto tardo el ultimo <see cref="PopulateNPCTree"/>. Va al texto de estado por el mismo
    ''' motivo que los tiempos de carga: en Release el log esta apagado, asi que lo unico que se puede
    ''' medir es lo que se muestra.</summary>
    Private _msUltimoRepoblado As Long = 0

    ''' <summary>For NPCs with no FullName that inherit BaseData from a template, resolve the name from the chain.</summary>
    Private Sub ResolveInheritedFullNames()
        ' Un ANCESTRO se parsea UNA vez. Sin esto, cada eslabon de cada cadena de plantillas volvia a
        ' construir el arbol canonico entero del record —y a traducir todas sus referencias— cada vez
        ' que alguien pasaba por el, y en un orden de carga real hay miles de NPC colgando de un
        ' punado de plantillas.
        '
        ' ⛔ La cache es de PARSEOS, no de nombres resueltos, y guarda instancias PROPIAS: no se
        ' reusan las de `_allNPCs`. Este mismo Sub le ESCRIBE el nombre heredado a las de `_allNPCs`,
        ' asi que leer de ahi haria que un eslabon devolviera un nombre que otra vuelta del bucle
        ' acababa de asignarle, en vez del que trae el record. Con instancias propias se lee siempre
        ' lo que dice el archivo, que es lo que hacia el re-parseo.
        Dim parseados As New Dictionary(Of UInteger, NPC_Data)()
        For Each npc In _allNPCs
            If npc.Record.Name <> "" Then Continue For
            If Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.BaseData) Then Continue For

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData)
            Dim resolved = ResolveInheritedFullName(sourceFormID, New HashSet(Of UInteger)(), parseados)
            If resolved <> "" Then npc.Record.Name = resolved
        Next
    End Sub

    Private Function ResolveInheritedFullName(formID As UInteger, visited As HashSet(Of UInteger),
                                              parseados As Dictionary(Of UInteger, NPC_Data)) As String
        If formID = 0UI OrElse visited.Contains(formID) Then Return ""
        visited.Add(formID)

        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return ""

        Select Case rec.Header.Signature
            Case "NPC_"
                Dim npc As NPC_Data = Nothing
                If Not parseados.TryGetValue(formID, npc) Then
                    npc = RecordParsers.ParseNPC(rec, _pluginManager)
                    parseados(formID) = npc
                End If
                If npc Is Nothing Then Return ""
                If npc.Record.Name <> "" Then Return npc.Record.Name
                ' Follow BaseData chain if this NPC also inherits
                If NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.BaseData) Then
                    Return ResolveInheritedFullName(NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData), visited, parseados)
                End If
            Case "LVLN"
                ' Pick first NPC entry from the LVLN to get a representative name
                ' Tolerante: este camino corre ANTES de que exista _lvlnDataCache, asi que un LVLN roto
                ' tumbaba la carga entera. Ver NpcTemplateHelpers.TryAbrirLvlnTolerante.
                Dim lvln = NpcTemplateHelpers.TryAbrirLvlnTolerante(rec, _pluginManager)
                If lvln Is Nothing Then Return ""
                For Each entry In lvln.LeveledListEntries
                    If entry.LeveledListEntryNPC = 0UI Then Continue For
                    Dim resolved = ResolveInheritedFullName(entry.LeveledListEntryNPC, visited, parseados)
                    If resolved <> "" Then Return resolved
                Next
        End Select

        Return ""
    End Function

    Private Sub RebuildTreeModelCache()
        _ctx.NpcCache = New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, NPC_Data)(
            _allNPCs.GroupBy(Function(n) n.FormID).Select(Function(g) g.First()).ToDictionary(Function(n) n.FormID))
        _templateDependencyMapCache = BuildTemplateDependencyMap(_ctx.NpcCache)
        _templateRootSourceIdsCache = BuildTemplateTreeRootSourceIds(_ctx.NpcCache, _templateDependencyMapCache)
        ' Pre-build per-NPC caches (searchable text + display label) en bulk una sola vez. La
        ' lectura per-keystroke pasa a Dictionary.TryGetValue O(1) sin string formatting.
        ' BuildNPCClassification() llamado a continuación pisa los caches y los rellena de nuevo
        ' — orden importa: classifications limpia ambos primero, así no acumulamos entries
        ' obsoletos.
        BuildNPCClassification()
        BuildSkinArmoUniverse()
        BuildOutfitUniverse()
        BuildLmSkinTemplateCache()
        BuildOverlayTemplateCache()
        ' Drop the LM custom-tint disk scan so it re-reads F4SE\Plugins\F4EE\Tints\ against the new load
        ' order; the RACE cache was just invalidated too, so they re-merge lazily on next use.
        LmCustomTintLoader.Invalidate()
        ' Mismo motivo para el registro de LUTs de pelo (LUTs\<plugin>\haircolors.json): se resuelve contra
        ' el load order (FormID local -> global) y contra el catálogo de RACE, los dos recién invalidados.
        ' Y se recarga EN SERIE acá mismo, no perezosamente: el bake lo consume desde un Parallel.ForEach
        ' por NPC, y despertarlo desde el fan-out hace que el lote dependa del scheduling.
        LmHairColorLutLoader.Invalidate()
        LmHairColorLutLoader.EnsureLoaded(_pluginManager)
        For Each npc In _ctx.NpcCache.Values
            _npcSearchableCache(npc.FormID) = NpcDisplayHelpers.BuildNpcSearchableText(npc)
            _npcDisplayLabelCache(npc.FormID) = NpcDisplayHelpers.BuildNpcDisplayLabel(npc)
            SembrarClaveDeOrden(npc)
        Next

        ' RESIEMBRA LA CLAVE DE ORDEN DE TODOS ⇒ TIENE QUE REORDENAR. `PopulateNPCTree` dejó de reordenar
        ' cada grupo por repoblado (ver OrdenarNpcs), o sea que ya no hay quien sane esto después: si la
        ' clave de alguno cambió acá, la lista queda mostrando el nombre nuevo en la posición vieja, sin
        ' un solo aviso. Es idempotente cuando nada cambió, así que se llama siempre y la invariante se
        ' restablece sola en vez de depender de que cada call site se acuerde.
        OrdenarNpcs()
        ' Load order changed: the advanced-filter caches are keyed by FormID (both the NPC results and
        ' the referenced HDPT/ARMO/RACE labels), so they cannot survive a different plugin set. Not
        ' rebuilt here — the index goes back to being lazy and re-fills only if a facet is used again.
        _filterIndex?.InvalidateAll()

        ' Drenar ACA y no solo en LoadDataAsync: los tres productores de avisos cuelgan de
        ' BuildNPCClassification, al que se vuelve desde el rebuild post-Save y desde el camino frio de
        ' PopulateNPCTree. LoadDataAsync corre UNA vez, en el arranque, asi que todo lo que se acumulara
        ' despues moria en silencio — incluido el caso feo: el readback post-save no puede releer un LVLN
        ' del plugin que la app acaba de escribir. No hace nada si no hay nada pendiente.
        ' DIFERIDO: un MessageBox modal bombea mensajes, y abrirlo en medio del rebuild deja reentrar a
        ' PopulateNPCTree -> RebuildTreeModelCache sobre los mismos diccionarios (la carrera que documenta
        ' FlushPendingLoadWarnings). Se muestra cuando el rebuild ya termino.
        If Not IsDisposed AndAlso IsHandleCreated Then BeginInvoke(Sub() FlushPendingLoadWarnings())
    End Sub

    ''' <summary>Filter the skin ARMO universe (built once at plugin load) by the race+gender of
    ''' the NPC currently being edited. An ARMO qualifies iff (a) at least one ARMA child has the
    ''' gender's skin TXST set (so the candidate is actually a body skin, not a placeholder) AND
    ''' (b) ARMO.RNAM matches OR at least one ARMA's RNAM/AdditionalRaces matches the NPC's race.
    ''' Returned tuples are (FormID, DisplayName) ready for direct assignment to a ComboBox.
    ''' DisplayName falls back to EditorID then to FormID-hex.</summary>
    Friend Function GetSkinArmoCandidates(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String))
        Dim outList As New List(Of (FormID As UInteger, DisplayName As String))

        ' RECORD-derived portion (the _skinArmoUniverse sweep) is cached per (race, gender): parses are
        ' globally cached so the qualifying set is stable between reloads. The dirty ARMO drafts below are
        ' appended FRESH each call (they change as the user authors them) — mirror of GetArmoItemCandidatesWithDrafts.
        Dim cacheKey = (npcRaceFID, isFemale)
        Dim recordPortion As List(Of (FormID As UInteger, DisplayName As String)) = Nothing
        If Not _skinArmoCandidateCache.TryGetValue(cacheKey, recordPortion) Then
            recordPortion = New List(Of (FormID As UInteger, DisplayName As String))
            For Each armoFID In _skinArmoUniverse
                Dim armo As Canon.IArmo
                Try
                    armo = _ctx.GetParsedArmo(armoFID)
                Catch
                    Continue For
                End Try
                If armo Is Nothing Then Continue For
                If SkinArmoQualifies(armoFID, armo, npcRaceFID, isFemale) Then
                    Dim display As String = If(Not String.IsNullOrEmpty(armo.Name), armo.Name,
                                                If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID,
                                                   armoFID.ToString("X8")))
                    recordPortion.Add((armoFID, display))
                End If
            Next
            _skinArmoCandidateCache(cacheKey) = recordPortion
        End If
        outList.AddRange(recordPortion)

        ' Dirty in-memory ARMO drafts — appended fresh (NOT cached; they change as the user authors them).
        ' Each must pass the SAME race/gender skin rule as a real candidate (SkinArmoQualifies): a draft
        ' qualifies as a skin iff one of its ARMA children (resolved via the now-draft-aware GetParsedArma)
        ' has the gender's skin TXST set and is race-valid. Drafts are NOT in _skinArmoUniverse, so a draft
        ' FormID can't double-list. Marker "(new)" mirrors how OTFT/LVLI drafts are surfaced.
        For Each d In _armoDrafts
            If Not d.IsDirty Then Continue For
            ' El borrador ES la vista canónica: nada que sintetizar.
            Dim armo As Canon.IArmo = d.Record
            If armo Is Nothing Then Continue For
            If SkinArmoQualifies(d.FormID, armo, npcRaceFID, isFemale) Then
                Dim display As String = If(Not String.IsNullOrEmpty(armo.Name), armo.Name,
                                            If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID,
                                               d.FormID.ToString("X8")))
                ' An OVERRIDE draft shares a real skin ARMO's FormID (already in recordPortion) — REPLACE that
                ' entry in place so the combo lists the FormID ONCE with the draft's edited name, not a stale
                ' duplicate. A NEW draft (provisional FormID) appends.
                Dim entry = (d.FormID, display & "  (new)")
                Dim existingIdx = outList.FindIndex(Function(x) x.FormID = d.FormID)
                If existingIdx >= 0 Then outList(existingIdx) = entry Else outList.Add(entry)
            End If
        Next

        Return outList.OrderBy(Function(x) x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    ''' <summary>Predicado del PICKER de Skin Armor (el botón "…" de Edit Body): el ARMO ocupa el slot
    ''' BODY del juego activo. Deliberadamente MUCHO más laxo que <see cref="SkinArmoQualifies"/>.
    '''
    ''' <para>QUÉ ES ESTE BOTÓN: el combo ya es la lista curada (raza + género + TXST de piel); el "…" es
    ''' la PUERTA DEL CASO EXTREMO, para lo que esa lista deja afuera. Por eso acá no se cura: se muestra
    ''' todo lo que tenga body y **elige el usuario**. Requisito textual suyo. No agregar filtros
    ''' "inteligentes" acá: se evaluó filtrar por "referenciado como WNAM" (177 filas en SSE / 173 en FO4,
    ''' semánticamente más lindo) y el usuario lo RECHAZÓ por curar.</para>
    '''
    ''' <para>NO exige raza, ni género, ni TXST de piel: el caso UBE probó que un ARMO de piel legítimo
    ''' puede no declarar TXST (NAM0=NAM1=0), así que exigirlo escondería justo lo que se quiere elegir.</para>
    '''
    ''' <para>Y NO lleva gate de POWER ARMOR, aunque el render/bake/<c>SkinArmoQualifies</c> sí lo tengan.
    ''' Se propuso y el usuario lo rechazó con la regla correcta: ese gate NO es data-driven —
    ''' <see cref="FindArmorTypePowerKeywordFid"/> barre los KYWD comparando el EditorID contra el string
    ''' literal "ArmorTypePower"— y un match por nombre no decide qué puede elegir el usuario. Consecuencia
    ''' asumida: en FO4 se listan los 8 ARMO de PA; elegir uno para un NPC no-PA lo deja sin cuerpo, y ése
    ''' es exactamente el caso extremo que este botón pone en manos del usuario. El gate sigue intacto
    ''' donde ya vivía.</para>
    '''
    ''' <para>Slot BODY **estricto** vía <see cref="BipedSlots.BodySlotBit"/> (SSE 32 / FO4 33), NO
    ''' <c>RegionMask(Body)</c>: esa unión agrupa Feet/Calves/Tail y Scalp, y MEDIDO deja entrar
    ''' <c>DremoraBoots</c> y <c>cc_Armor_Power_X01_Helm</c>.
    ''' <para>Volumen: <b>1169 de 3715 ARMO en SSE, 498 de 1045 en FO4</b> — medido con un probe .NET
    ''' contra la lib compilada y el load order REAL del usuario (Plugins.txt, post-merge de overrides), que
    ''' es lo que el picker efectivamente lista. No confundir con los conteos del parser Python del
    ''' scratchpad (4372/1025), que son PRE-merge y por eso difieren en SSE.</para>
    '''
    ''' <para>SIN MEMO, por MEDICIÓN: el gate en frío cuesta 17,5 ms (FO4) / 29,4 ms (SSE) una vez por
    ''' carga, y con los parses calientes 0,4/1,8 ms — un memo ahorraba 0 ms en FO4 y obligaba a dos reglas
    ''' extra (no cachear drafts, invalidar al recargar). Lo caro ya lo cachean GetParsedArmo/GetParsedArma.</para>
    '''
    ''' <para>Drafts propios DIRTY: siempre True, y detectados por PERTENENCIA a <c>_armoDrafts</c>, NO
    ''' por <c>OutfitDraft.IsDraftFormID</c> — un draft OVERRIDE conserva su FormID REAL, así que el test de
    ''' forma cubriría sólo la mitad de los casos. Misma regla que "Own ARMO drafts are the user's OWN
    ''' creations — ALWAYS list them" (GetArmoItemCandidatesWithDrafts).</para></summary>
    Friend Function ArmoHasBodyArmature(armoFID As UInteger) As Boolean
        If armoFID = 0UI Then Return False

        Dim armo As Canon.IArmo = Nothing
        Try
            armo = _ctx.GetParsedArmo(armoFID)
        Catch
            Return False
        End Try
        If armo Is Nothing Then Return False

        ' Draft propio con cambios sin guardar: siempre listable, aunque todavía no tenga armatures.
        For Each d In _armoDrafts
            If d.IsDirty AndAlso d.FormID = armoFID Then Return True
        Next

        Dim bodyBit As UInteger = BipedSlots.BodySlotBit()
        If bodyBit = 0UI Then Return False

        ' Footprint del REGISTRO por la ley única (unión de los armatures que resuelven, sin filtro de
        ' raza/género, con el fallback al BOD2 propio del ARMO cuando ninguno resuelve — el render cae ahí
        ' al mesh de ARMO.MOD2, p.ej. robots). La pregunta acá es del registro, no del actor.
        ' SIN gate de power-armor, a propósito (ver EditBody_Form: el picker de Skin Armor lista las pieles
        ' de PA, que por definición son ARMO con ArmorTypePower). Por eso el contexto se arma acá y no con
        ' EquipCtx, que sí trae el gate.
        ' Los resolvers de EquipContext siguen pidiendo el modelo *_Data legado (Records\, no se toca):
        Dim recCtx As New EquipResolver.EquipContext With {
            .PluginManager = _pluginManager,
            .ArmoResolver = AddressOf _ctx.GetParsedArmo,
            .ArmaResolver = AddressOf _ctx.GetParsedArma}
        Return (EquipResolver.BuildFootprint(armoFID, recCtx).RecordGeometryMask And bodyBit) <> 0UI
    End Function

    ''' <summary>The per-ARMO skin-candidate rule, shared by real records and ARMO drafts so both qualify
    ''' identically. An ARMO is a valid skin for (race, gender) iff: it survives the power-armor gate (no PA
    ''' skin for a non-PA race) AND (ARMO.RNAM matches the race OR some race-valid ARMA child does) AND at least
    ''' one race-valid ARMA child has the gender's skin TXST set (so it's a real body skin, not a placeholder).
    ''' ARMA children are resolved through <see cref="NpcRenderContext.GetParsedArma"/>, which now also resolves
    ''' draft ARMAs — so a draft ARMO whose children are draft ARMAs qualifies end-to-end.</summary>
    Private Function SkinArmoQualifies(armoFID As UInteger, armo As Canon.IArmo, npcRaceFID As UInteger, isFemale As Boolean) As Boolean
        ' Power-armor gate (same rule as the render): don't offer a power-armor skin for a non-PA race.
        If ArmoIsPowerArmor(armoFID) AndAlso Not RaceIsPowerArmor(npcRaceFID) Then Return False
        Dim raceMatch = (armo.Race = npcRaceFID)
        Dim genderMatch As Boolean = False
        For Each addon In ArmoEditor_Form.ReadAddons(armo)
            Dim arma As Canon.IArma
            Try
                arma = _ctx.GetParsedArma(addon.ArmaFormID)
            Catch
                Continue For
            End Try
            If arma Is Nothing Then Continue For
            Dim armaRaceOk = EquipResolver.ArmaMatchesRace(arma, npcRaceFID, _ctx.GetEffectiveArmorRaces(npcRaceFID))
            If armaRaceOk Then raceMatch = True
            Dim txst = If(isFemale, arma.FemaleSkinTexture, arma.MaleSkinTexture)
            If armaRaceOk AndAlso txst <> 0UI Then genderMatch = True
        Next
        Return raceMatch AndAlso genderMatch
    End Function

    ''' <summary>Walks the same chain the render uses (raw NPC.HCLF -> Traits template chain
    ''' -> RACE.{Male,Female}DefaultHairColorFormID with own-gender-first fallback) and returns
    ''' the HCLF FormID the engine would actually paint with for this NPC. Used by Edit Face to
    ''' pre-select the combo at form-open WITHOUT mutating the overlay (preserve semantic stays
    ''' intact). Returns 0 if the NPC has no resolvable HCLF anywhere up the chain.</summary>
    Friend Function ResolveEffectiveHairColorFormID(npcFormID As UInteger) As UInteger
        If npcFormID = 0UI Then Return 0UI
        Dim warnings As New List(Of String)
        ' Lo llama EditFace_Form para pre-seleccionar el combo de color. Sin el ancla hacia su PROPIO
        ' sorteo de la LVLN y el swatch mostraba el pelo de una hoja distinta a la del preview.
        Dim traits = _stateResolver.ResolveTraitsStateFromNPC(npcFormID, New HashSet(Of UInteger)(), warnings,
                                                              ResolveShownTraitsLeaf(npcFormID, Nothing))
        If traits Is Nothing Then Return 0UI
        If traits.HairColorFormID <> 0UI Then Return traits.HairColorFormID

        Dim raceRec = _pluginManager.GetRecord(traits.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 0UI
        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        If race Is Nothing Then Return 0UI

        ' HCLF\Default Hair Colors: array fijo de 2 (slot 0 = Male, slot 1 = Female) en la interfaz común.
        Dim hclf = race.DefaultHairColors
        Dim maleHcl As UInteger = If(hclf.Count > 0, hclf(0).DefaultHairColor, 0UI)
        Dim femaleHcl As UInteger = If(hclf.Count > 1, hclf(1).DefaultHairColor, 0UI)
        Dim ownGender = If(traits.IsFemale, femaleHcl, maleHcl)
        Dim otherGender = If(traits.IsFemale, maleHcl, femaleHcl)
        Return If(ownGender <> 0UI, ownGender, otherGender)
    End Function

    ''' <summary>Filter the LM skin templates cache by gender (gender=2 means unisex per
    ''' SkinInterface.h:38). Sorted by template.Sort then DisplayName, mirroring SkinInterface.cpp:610-611.</summary>
    Friend Function GetLmSkinTemplateCandidates(isFemale As Boolean) As List(Of LmSkinTemplate)
        Dim genderByte As Byte = If(isFemale, CByte(1), CByte(0))
        Dim filtered = _lmSkinTemplates.
            Where(Function(t) t.Gender = 2 OrElse t.Gender = genderByte).
            OrderBy(Function(t) t.Sort).
            ThenBy(Function(t) t.DisplayName, StringComparer.OrdinalIgnoreCase).
            ToList()
        Return filtered
    End Function

    ''' <summary>Best-effort display string for an ARMO FormID — used by Edit Body to add the
    ''' NPC's currently-effective WNAM at the top of the combo even when it falls outside the
    ''' filtered universe (e.g. an esoteric vanilla skin). Empty string if the FormID isn't an
    ''' ARMO record.</summary>
    Friend Function GetSkinArmoDisplayName(armoFID As UInteger) As String
        If armoFID = 0UI Then Return ""
        Dim armo As Canon.IArmo

        Try
            armo = _ctx.GetParsedArmo(armoFID)
        Catch
            Return armoFID.ToString("X8")
        End Try
        If armo Is Nothing Then Return ""
        If Not String.IsNullOrEmpty(armo.Name) Then Return armo.Name
        If Not String.IsNullOrEmpty(armo.EditorID) Then Return armo.EditorID
        Return armoFID.ToString("X8")
    End Function

    ''' <summary>Builds the universe of ARMO FormIDs referenced as skin by any RACE or NPC_ in
    ''' the load order. Sweep is one-shot per plugin reload — runs inside RebuildTreeModelCache.
    ''' Cheaper than enumerating every ARMO record because most ARMOs are outfits/armor, never
    ''' skin; only the ones some record actually marks as skin are interesting. The race+gender
    ''' filter applied at combo-populate time runs over this set, not over the raw ARMO pool.</summary>
    Private Sub BuildSkinArmoUniverse()
        _skinArmoUniverse.Clear()
        ' NPC.WNAM contributions — _allNPCs is already parsed by this point.
        For Each npc In _allNPCs
            If npc.Record.Skin <> 0UI Then _skinArmoUniverse.Add(npc.Record.Skin)
        Next
        ' RACE.WNAM contributions. Se piden por TIPO: el índice por signature ya existe y lo mantiene
        ' el propio gestor. Antes se recorrían las 127.850 entradas de AllRecords comparando la
        ' signature cadena a cadena para quedarse con ~150 RACE — el mismo índice que la línea de al
        ' lado (BuildOutfitUniverse) ya usaba para los OTFT.
        Dim raceRecs = _pluginManager.GetRecordsOfType("RACE")
        If raceRecs IsNot Nothing Then
            For Each rec In raceRecs
                If rec Is Nothing Then Continue For
                Try
                    Dim race = _ctx.ParseRaceCanonCached(rec)
                    If race.Skin <> 0UI Then _skinArmoUniverse.Add(race.Skin)
                Catch
                End Try
            Next
        End If
    End Sub

    ''' <summary>Sweep all OTFT FormIDs in the load order into <see cref="_outfitUniverse"/>. Cheap —
    ''' no expansion here; the per-race/gender filter expands lazily in <see cref="GetOutfitCandidates"/>.
    ''' Runs once per plugin reload inside RebuildTreeModelCache, right after BuildSkinArmoUniverse.
    ''' Also clears the candidate cache so a reload doesn't serve stale lists.</summary>
    Private Sub BuildOutfitUniverse()
        _outfitUniverse.Clear()
        _outfitCandidateCache.Clear()
        _outfitAvailabilityCache.Clear()
        _armoItemCandidateCache.Clear()
        _skinArmoCandidateCache.Clear()
        ' Picker rows are derived from the same record universe — drop the cross-open per-signature cache
        ' too. This choke point runs on plugin (re)load (RebuildTreeModelCache) AND after a Save promotes
        ' drafts to real records (PromoteSavedDrafts → BuildOutfitUniverse), so both cases are covered here.
        FormIdPicker_Form.InvalidateSignatureCache()
        _outfitLeveledListCache = Nothing   ' OTFT-referenced LVLI set is derived from records → re-derive
        Dim otftRecs = _pluginManager.GetRecordsOfType("OTFT")
        If otftRecs Is Nothing Then Return
        For Each rec In otftRecs
            If rec Is Nothing Then Continue For
            _outfitUniverse.Add(rec.Header.FormID)
        Next
    End Sub


    ''' <summary>Outfits selectable for (race, gender). For each OTFT in the universe, deterministically
    ''' enumerate every possible terminal ARMO (<see cref="OutfitResolver.EnumerateAllTerminalArmos"/>)
    ''' and accept the OTFT if ANY of its ARMAs is valid for the race (RaceFormID ∪ AdditionalRaces)
    ''' AND carries a world mesh (matching the renderer's own gender-fallback rule for model paths).
    ''' <para>Filter is PER-ARMA, never by ARMO.RaceFormID: most vanilla clothing has RNAM=HumanRace,
    ''' so filtering by the ARMO would drop outfits valid for ghouls/other races (the closed
    ''' ghoul-outfit bug). Known deferred edge case: ghouls wearing human outfits whose ARMA doesn't
    ''' list GhoulRace won't pass this filter (23-armor-outfit-resolution).</para>
    ''' Cached per (race, gender) — the costly OTFT expansion + ARMA parse runs once per pair.</summary>
    Friend Function GetOutfitCandidates(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String))
        Dim cacheKey = (npcRaceFID, isFemale)
        ' Cached OTFT sweep (the expensive part).
        Dim otftList As List(Of (FormID As UInteger, DisplayName As String)) = Nothing
        If Not _outfitCandidateCache.TryGetValue(cacheKey, otftList) Then
            otftList = New List(Of (FormID As UInteger, DisplayName As String))
            For Each otftFID In _outfitUniverse
                If OutfitHasValidArma(otftFID, npcRaceFID, isFemale) Then
                    otftList.Add((otftFID, GetOutfitDisplayName(otftFID)))
                End If
            Next
            otftList = otftList.OrderBy(Function(x) x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
            _outfitCandidateCache(cacheKey) = otftList
        End If

        ' In-memory drafts authored in the Create tab — appended fresh (NOT cached, they change as the
        ' user authors them). Shown for any NPC (they're deliberate user creations); the render's
        ' per-ARMA race check drops any piece that doesn't fit the actual NPC. Marked "[draft]".
        ' Dedupe: an Override draft keeps the base OTFT's FormID, so it would otherwise show TWICE (the
        ' real OTFT row + a "[draft]" row, same FormID). Skip the draft row when its FormID is already
        ' listed — the existing row already resolves to the draft at render time (TryGetOutfitDraft wins),
        ' so one row is enough and it applies the modified outfit. (Operates on _outfitDrafts, which only
        ' changes on OK via RegisterOutfitDraft — the in-dialog Override↔New toggling never touches it, and
        ' the preview throwaway draft is excluded below.)
        If _outfitDrafts.Count = 0 Then Return otftList
        Dim result As New List(Of (FormID As UInteger, DisplayName As String))(otftList)
        For Each d In _outfitDrafts
            If d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For   ' throwaway picker-preview draft
            ' An OVERRIDE draft keeps the base OTFT's FormID (already listed). REPLACE that row in place so the
            ' FormID shows ONCE with the draft's EDITED name (the old code kept the stale real name). A NEW draft
            ' (provisional FormID) appends. Render already resolves the draft (TryGetOutfitDraft wins).
            Dim entry = (d.FormID, d.Record.EditorID & "  [draft]")
            Dim idx = result.FindIndex(Function(x) x.FormID = d.FormID)
            If idx >= 0 Then result(idx) = entry Else result.Add(entry)
        Next
        Return result
    End Function

    ''' <summary>True if (race, gender) has at least one valid outfit. Early-exits on the first match
    ''' (cheaper than <see cref="GetOutfitCandidates"/>, which builds the whole sorted list), and
    ''' answers from the full-list cache when it's already populated. Cached per (race, gender) so the
    ''' render-complete button gate doesn't re-scan. Drives <see cref="UpdateEditOutfitEnabled"/> so
    ''' the button reflects real availability instead of being lit for every NPC.</summary>
    Private Function HasAnyOutfitCandidate(npcRaceFID As UInteger, isFemale As Boolean) As Boolean
        Dim cacheKey = (npcRaceFID, isFemale)
        Dim cachedBool As Boolean
        If _outfitAvailabilityCache.TryGetValue(cacheKey, cachedBool) Then Return cachedBool
        ' If the full candidate list was already built (picker opened before), answer from it.
        Dim cachedList As List(Of (FormID As UInteger, DisplayName As String)) = Nothing
        If _outfitCandidateCache.TryGetValue(cacheKey, cachedList) Then
            Dim fromList = cachedList.Count > 0
            _outfitAvailabilityCache(cacheKey) = fromList
            Return fromList
        End If
        ' Otherwise scan the universe, early-exiting on the first valid outfit.
        Dim result As Boolean = False
        For Each otftFID In _outfitUniverse
            If OutfitHasValidArma(otftFID, npcRaceFID, isFemale) Then
                result = True
                Exit For
            End If
        Next
        _outfitAvailabilityCache(cacheKey) = result
        Return result
    End Function

    ''' <summary>True if the OTFT resolves (over all possible realizations) to at least one ARMA valid
    ''' for the race + gender. Used by <see cref="GetOutfitCandidates"/>. Per-ARMA race check + world
    ''' mesh presence with the same male/female fallback the renderer applies.</summary>
    Private Function OutfitHasValidArma(otftFID As UInteger, npcRaceFID As UInteger, isFemale As Boolean) As Boolean
        For Each armoFID In OutfitResolver.EnumerateAllTerminalArmos(otftFID, _pluginManager)
            Dim armo As Canon.IArmo
            Try
                armo = _ctx.GetParsedArmo(armoFID)
            Catch
                Continue For
            End Try
            If armo Is Nothing Then Continue For
            ' Power-armor gate (same rule as the render): a PA ARMO is not a valid piece for a non-PA race
            ' (it'd be dropped at render). A mixed outfit still validates via its non-PA pieces; a purely-PA
            ' outfit won't list for a non-PA NPC.
            If ArmoIsPowerArmor(armoFID) AndAlso Not RaceIsPowerArmor(npcRaceFID) Then Continue For
            For Each addon In ArmoEditor_Form.ReadAddons(armo)
                Dim arma As Canon.IArma
                Try
                    arma = _ctx.GetParsedArma(addon.ArmaFormID)
                Catch
                    Continue For
                End Try
                If arma Is Nothing Then Continue For
                    Dim armaRaceOk = EquipResolver.ArmaMatchesRace(arma, npcRaceFID, _ctx.GetEffectiveArmorRaces(npcRaceFID))
                If Not armaRaceOk Then Continue For
                If arma.FemaleModelFilename <> "" OrElse arma.MaleModelFilename <> "" Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>Display label for an OTFT FormID: EditorID if any, else hex. OTFTs carry no FULL name
    ''' (Canon.IOtft is FormID + EditorID + INAM array).</summary>
    Friend Function GetOutfitDisplayName(otftFID As UInteger) As String
        If otftFID = 0UI Then Return ""
        Dim draft = TryGetOutfitDraft(otftFID)
        If draft IsNot Nothing Then Return draft.Record.EditorID
        Dim rec = _pluginManager.GetRecord(otftFID)
        If rec Is Nothing OrElse rec.Header.Signature <> "OTFT" Then Return otftFID.ToString("X8")
        If Not String.IsNullOrEmpty(rec.EditorID) Then Return rec.EditorID
        Return otftFID.ToString("X8")
    End Function

    ''' <summary>Return the in-memory outfit draft for <paramref name="formID"/>, or Nothing. Matches
    ''' both provisional (new, 0xFF sentinel) and override (existing FormID kept) drafts.</summary>
    Friend Function TryGetOutfitDraft(formID As UInteger) As OutfitDraft
        If formID = 0UI Then Return Nothing
        For Each d In _outfitDrafts
            If d.FormID = formID Then Return d
        Next
        Return Nothing
    End Function

    ''' <summary>The in-memory ARMO draft for <paramref name="formID"/>, or Nothing. Matches both provisional
    ''' (new, 0xFF sentinel) and override (existing FormID kept) drafts — mirror of <see cref="TryGetOutfitDraft"/>.</summary>
    Friend Function TryGetArmoDraft(formID As UInteger) As ArmoDraft
        If formID = 0UI Then Return Nothing
        For Each d In _armoDrafts
            If d.FormID = formID Then Return d
        Next
        Return Nothing
    End Function

    ''' <summary>The in-memory ARMA draft for <paramref name="formID"/>, or Nothing. Mirror of <see cref="TryGetArmoDraft"/>.</summary>
    Friend Function TryGetArmaDraft(formID As UInteger) As ArmaDraft
        If formID = 0UI Then Return Nothing
        For Each d In _armaDrafts
            If d.FormID = formID Then Return d
        Next
        Return Nothing
    End Function

    ''' <summary>Current preview context for the standalone ARMA/ARMO editors — the rendered NPC (0 = none) and its
    ''' gender, sourced from the render host's LastRenderedState. Used by the addon-entry modal's "Edit ARMA…"
    ''' button (and the outfit editor's "Edit armor") so the deep ARMA/ARMO editor previews on the right body.</summary>
    Friend Sub GetEditorPreviewContext(ByRef previewNpcFormID As UInteger, ByRef isFemale As Boolean)
        Dim st = _renderHost.LastRenderedState
        previewNpcFormID = If(st IsNot Nothing, st.RootNpcFormID, 0UI)
        isFemale = (st IsNot Nothing AndAlso st.IsFemale)
    End Sub

    ''' <summary>The current preview NPC's resolved skin ARMO FormID (already NPC.WNAM ?? RACE.WNAM), or 0 when no
    ''' NPC is loaded. The naked-body ARMO is gender-neutral (its ARMAs carry both MOD2/MOD3), so the ARMA editor's
    ''' Estimate reads whichever gender it needs from it, per gender, and falls back to the race skin only when this
    ''' skin has no mesh for a gender.</summary>
    Friend Function GetCurrentPreviewSkinFormID() As UInteger
        Dim st = If(_renderHost.CurrentBaseState, _renderHost.LastRenderedState)
        Return If(st IsNot Nothing, st.SkinFormID, 0UI)
    End Function

    ''' <summary>The race the preview actor is ACTUALLY rendered as — the same <c>state.RaceFormID</c> the mesh
    ''' collector gates every ARMA against. NOT the same as the race an editor was opened with (the ARMA/ARMO
    ''' editors are handed the owning ARMO's RNAM, which may differ), so any UI that mirrors the render's race
    ''' filter must read it from here. 0 when no NPC is loaded.</summary>
    Friend Function GetCurrentPreviewRaceFormID() As UInteger
        Dim st = If(_renderHost.CurrentBaseState, _renderHost.LastRenderedState)
        Return If(st IsNot Nothing, st.RaceFormID, 0UI)
    End Function

    ''' <summary>The in-memory MSWP draft for <paramref name="formID"/>, or Nothing. Mirror of <see cref="TryGetArmoDraft"/>.</summary>
    Friend Function TryGetMswpDraft(formID As UInteger) As MswpDraft
        If formID = 0UI Then Return Nothing
        For Each d In _mswpDrafts
            If d.FormID = formID Then Return d
        Next
        Return Nothing
    End Function

    ' =====================================================================
    ' ARMA Editor (ArmaEditor_Form) — draft registrars + read access.
    ' Mirror of RegisterOutfitDraft/TryGetOutfitDraft (same replace-by-FormID
    ' semantics, same shared AllocateDraftFormID counter). The form mutates
    ' these lists; the existing Save ESP flow persists the transitive closure.
    ' =====================================================================

    ''' <summary>MainForm's canonical per-NPC overlay dict (shared BY REFERENCE) — the Armor Editor's
    ''' NpcRenderHost reads overlays from here, exactly like OutfitPicker_Form does, so the preview reflects
    ''' the NPC's committed look. Read-only handle; the editor never writes the outfit override into it (it
    ''' uses the host-scoped OutfitPreviewOverride via PreviewOutfitInHostAsync instead).</summary>
    Friend ReadOnly Property AppliedPresetsForEditor As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
        Get
            Return _appliedPresets
        End Get
    End Property

    ''' <summary>Snapshot (copy) of the current ARMO drafts — the editor's draft list reads from this.</summary>
    Friend Function ArmoDrafts() As List(Of ArmoDraft)
        Return New List(Of ArmoDraft)(_armoDrafts)
    End Function

    ''' <summary>Snapshot (copy) of the current ARMA drafts.</summary>
    Friend Function ArmaDrafts() As List(Of ArmaDraft)
        Return New List(Of ArmaDraft)(_armaDrafts)
    End Function

    ''' <summary>Snapshot (copy) of the current MSWP drafts.</summary>
    Friend Function MswpDrafts() As List(Of MswpDraft)
        Return New List(Of MswpDraft)(_mswpDrafts)
    End Function

    ''' <summary>Snapshot (copy) of the current outfit (OTFT) drafts. Used by the Edit-Outfit dialog's
    ''' "My outfit drafts" panel (edit / delete / revert). Callers filter out the transient
    ''' <see cref="OutfitDraft.PreviewDraftFormID"/> sentinel themselves.</summary>
    Friend Function OutfitDrafts() As List(Of OutfitDraft)
        Return New List(Of OutfitDraft)(_outfitDrafts)
    End Function

    ''' <summary>Real records of signature <paramref name="sig"/> this app AUTHORED — identified by their WINNING
    ''' version living in a plugin THIS app wrote (TES4.CNAM = NPC Manager marker, via
    ''' <see cref="PluginManager.IsNpcManagerPlugin"/>). Catches both NEW records (npcm_ EDID) and OVERRIDES
    ''' (which keep the original EDID) uniformly, and survives app restarts. Returned as (FormID, EditorID,
    ''' DisplayName) so the "Edit mine…" picker lists them alongside unsaved drafts and re-opens one as an override.</summary>
    Friend Function GetAuthoredRecords(sig As String) As List(Of (FormID As UInteger, EditorID As String, DisplayName As String))
        Dim result As New List(Of (FormID As UInteger, EditorID As String, DisplayName As String))
        Dim recs = _pluginManager.GetRecordsOfType(sig)
        If recs Is Nothing Then Return result
        For Each rec In recs
            If rec Is Nothing Then Continue For
            If _pluginManager.IsNpcManagerPlugin(rec.SourcePluginName) AndAlso Not _recordsToRemove.Contains(rec.Header.FormID) Then
                result.Add((rec.Header.FormID, If(rec.EditorID, ""), GetRecordDisplayNameForEditor(rec.Header.FormID)))
            End If
        Next
        Return result
    End Function

    ''' <summary>GLOBAL FormIDs the user marked for REMOVAL from their plugin (Delete of a saved NEW record /
    ''' Revert of a saved OVERRIDE). Hidden from the "my records" lists immediately; the real removal happens on the
    ''' next Save (the saver's Phase 2a skips them, so a new record vanishes and an override reverts to the original),
    ''' after which this is cleared.</summary>
    Private ReadOnly _recordsToRemove As New HashSet(Of UInteger)

    ''' <summary>True if the KYWD <paramref name="fid"/> is an ATTACH-POINT keyword — the AUTHORITATIVE test
    ''' (NOT a name heuristic): its KYWD.TNAM 'Type' == 2 ('Attach Point'). Filters the ARMO
    ''' APPR (Attach Parent Slots) picker to real attach-point
    ''' keywords; the picker's "Show all" checkbox escapes the filter.</summary>
    Friend Function IsAttachPointKeyword(fid As UInteger) As Boolean
        If fid = 0UI Then Return False
        Dim rec = _pluginManager.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "KYWD" Then Return False
        Dim tnam = rec.GetSubrecord("TNAM")
        If Not tnam.HasValue OrElse tnam.Value.Data Is Nothing OrElse tnam.Value.Data.Length < 4 Then Return False
        Return BitConverter.ToUInt32(tnam.Value.Data, 0) = 2UI
    End Function

    ''' <summary>Mark a SAVED authored record for removal on the next Save.</summary>
    Friend Sub MarkRecordForRemoval(formID As UInteger)
        If formID <> 0UI Then _recordsToRemove.Add(formID)
    End Sub

    ''' <summary>Revert an app override IN MEMORY so the editor/render/pickers immediately resolve the FormID to the
    ''' mod's WINNING record again (the last non-app override) instead of the just-reverted app override — or drop it
    ''' when the app created it new. Pairs with <see cref="MarkRecordForRemoval"/> (which drops it from the FILE on the
    ''' next Save): together, revert is consistent in memory AND on disk without a full reload. Clears the render
    ''' context's parse caches so the next parse/render reflects the restored record. No-op when nothing changed.</summary>
    Friend Sub RevertAppOverrideInMemory(formID As UInteger)
        If formID = 0UI Then Return
        ' TARGETED invalidation (only the reverted record) — NOT InvalidateParseCaches(): a full Clear() mid-session
        ' races in-flight background renders and blanks the whole scene intermittently.
        If _pluginManager.RevertAppOverride(formID) Then _ctx.InvalidateRecord(formID)
    End Sub

    ''' <summary>Snapshot of the FormIDs marked for removal — passed to the save via SaveContext.RecordsToRemove.</summary>
    Friend Function RecordsToRemove() As HashSet(Of UInteger)
        Return New HashSet(Of UInteger)(_recordsToRemove)
    End Function

    ''' <summary>FormIDs of the real OTFT records this app AUTHORED — winning version in a plugin this app wrote
    ''' (<see cref="PluginManager.IsNpcManagerPlugin"/>), so both NEW and OVERRIDE outfits count. The Edit-Outfit
    ''' "My outfit drafts" tab lists these alongside the unsaved drafts; double-click re-opens as override.</summary>
    Friend Function GetAuthoredOutfitFormIDs() As List(Of UInteger)
        Dim result As New List(Of UInteger)
        Dim recs = _pluginManager.GetRecordsOfType("OTFT")
        If recs Is Nothing Then Return result
        For Each rec In recs
            If rec Is Nothing Then Continue For
            If _pluginManager.IsNpcManagerPlugin(rec.SourcePluginName) AndAlso Not _recordsToRemove.Contains(rec.Header.FormID) Then result.Add(rec.Header.FormID)
        Next
        Return result
    End Function

    ''' <summary>Register/replace an ARMO draft (by FormID). Seen by the draft-aware render resolver
    ''' (<see cref="NpcRenderContext.ArmoDraftResolver"/> → <c>TryGetArmoDraft(fid)?.Record</c>) and the Save
    ''' flow. Mirror of <see cref="RegisterOutfitDraft"/>.</summary>
    Friend Sub RegisterArmoDraft(d As ArmoDraft)
        If d Is Nothing Then Return
        Dim existing = _armoDrafts.FirstOrDefault(Function(x) x.FormID = d.FormID)
        If existing IsNot Nothing Then _armoDrafts.Remove(existing)
        _armoDrafts.Add(d)
    End Sub

    ''' <summary>Register/replace an ARMA draft (by FormID). Mirror of <see cref="RegisterArmoDraft"/>.</summary>
    Friend Sub RegisterArmaDraft(d As ArmaDraft)
        If d Is Nothing Then Return
        Dim existing = _armaDrafts.FirstOrDefault(Function(x) x.FormID = d.FormID)
        If existing IsNot Nothing Then _armaDrafts.Remove(existing)
        _armaDrafts.Add(d)
    End Sub

    ''' <summary>Register/replace an MSWP draft (by FormID). Mirror of <see cref="RegisterArmoDraft"/>.</summary>
    Friend Sub RegisterMswpDraft(d As MswpDraft)
        If d Is Nothing Then Return
        Dim existing = _mswpDrafts.FirstOrDefault(Function(x) x.FormID = d.FormID)
        If existing IsNot Nothing Then _mswpDrafts.Remove(existing)
        _mswpDrafts.Add(d)
    End Sub

    ''' <summary>Drop an ARMO draft (by FormID). Used by "Delete draft".</summary>
    Friend Sub UnregisterArmoDraft(formID As UInteger)
        Dim existing = _armoDrafts.FirstOrDefault(Function(x) x.FormID = formID)
        If existing IsNot Nothing Then _armoDrafts.Remove(existing)
    End Sub

    ''' <summary>Drop an ARMA draft (by FormID).</summary>
    Friend Sub UnregisterArmaDraft(formID As UInteger)
        Dim existing = _armaDrafts.FirstOrDefault(Function(x) x.FormID = formID)
        If existing IsNot Nothing Then _armaDrafts.Remove(existing)
    End Sub

    ''' <summary>Drop an MSWP draft (by FormID).</summary>
    Friend Sub UnregisterMswpDraft(formID As UInteger)
        Dim existing = _mswpDrafts.FirstOrDefault(Function(x) x.FormID = formID)
        If existing IsNot Nothing Then _mswpDrafts.Remove(existing)
    End Sub

    ''' <summary>Human-readable list of everything that currently REFERENCES the draft with
    ''' <paramref name="formID"/>: other in-memory drafts (an ARMO draft's addon ARMA or material swap, an ARMA
    ''' draft's material swap, an outfit draft's item, a leveled-list draft's entry) AND per-NPC assignments
    ''' (a WNAM skin or default-outfit override in an applied preset). EMPTY ⇒ nothing points at it, so a NEW
    ''' draft (provisional FormID) can be deleted without dangling any reference. Callers only need this for NEW
    ''' drafts — an OVERRIDE draft is REVERTED (unregistered), which is always safe because every reference
    ''' resolves by FormID to the still-existing real record. Pure read-only scan.</summary>
    Friend Function GetDraftReferrers(formID As UInteger) As List(Of String)
        Dim refs As New List(Of String)
        If formID = 0UI Then Return refs

        For Each d In _armoDrafts
            If d Is Nothing Then Continue For
            Dim addons = ArmoEditor_Form.ReadAddons(d.Record)
            If addons.Any(Function(a) a IsNot Nothing AndAlso a.ArmaFormID = formID) Then
                refs.Add($"ARMO draft '{d.Record.EditorID}' (addon)")
            End If
            ' El material swap a nivel ARMO (MOD2S/MOD4S) sólo existe en Fallout 4.
            Dim armoFo4 = TryCast(d.Record, Canon.ArmoFO4)
            Dim armoSwapMatch = armoFo4 IsNot Nothing AndAlso
                (armoFo4.WorldModelMaterialSwap = formID OrElse
                 armoFo4.WorldModelMaterialSwap2 = formID)
            If armoSwapMatch Then
                refs.Add($"ARMO draft '{d.Record.EditorID}' (material swap)")
            End If
        Next
        For Each d In _armaDrafts
            If d Is Nothing Then Continue For
            ' El material swap del ARMA (MO2S/MO3S) sólo existe en Fallout 4.
            Dim armaFo4 = TryCast(d.Record, Canon.ArmaFO4)
            Dim armaSwapMatch = armaFo4 IsNot Nothing AndAlso
                (armaFo4.MaleMaterialSwap = formID OrElse armaFo4.FemaleMaterialSwap = formID)
            If armaSwapMatch Then
                refs.Add($"ARMA draft '{d.Record.EditorID}' (material swap)")
            End If
        Next
        For Each d In _outfitDrafts
            If d Is Nothing OrElse d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For
            If d.Prendas().Contains(formID) Then refs.Add($"Outfit draft '{d.Record.EditorID}'")
        Next
        For Each d In _leveledListDrafts
            If d Is Nothing Then Continue For
            Dim hasRef = d.Record.LeveledListEntries.Any(
                Function(en) en IsNot Nothing AndAlso en.LeveledListEntryItem = formID)
            If hasRef Then
                refs.Add($"Leveled-list draft '{d.Record.EditorID}'")
            End If
        Next
        For Each kv In _appliedPresets
            Dim p = kv.Value
            If p Is Nothing Then Continue For
            If p.SkinFormIDOverride.HasValue AndAlso p.SkinFormIDOverride.Value = formID Then
                refs.Add($"NPC skin — {GetRecordDisplayNameForEditor(kv.Key)}")
            End If
            If p.DefaultOutfitFormIDOverride.HasValue AndAlso p.DefaultOutfitFormIDOverride.Value = formID Then
                refs.Add($"NPC outfit — {GetRecordDisplayNameForEditor(kv.Key)}")
            End If
        Next
        Return refs
    End Function

    ''' <summary>Draft-aware parsed ARMO view (real record OR draft) — exposes the render context's
    ''' resolver so the Armor Editor's override-load converters and preview can read the same data the
    ''' render reads. Nothing when the FormID is neither a real ARMO nor a draft.</summary>
    Friend Function GetParsedArmoForEditor(formID As UInteger) As Canon.IArmo
        Return _ctx.GetParsedArmo(formID)
    End Function

    ''' <summary>Draft-aware parsed ARMA view (real record OR draft). See <see cref="GetParsedArmoForEditor"/>.</summary>
    Friend Function GetParsedArmaForEditor(formID As UInteger) As Canon.IArma
        Return _ctx.GetParsedArma(formID)
    End Function

    ''' <summary>If <paramref name="fid"/> is a REAL (non-draft) MSWP record, build + register an OVERRIDE
    ''' <see cref="MswpDraft"/> seeded with its parsed substitutions (EditorID/TreeFolder + BNAM/SNAM/CNAM pairs)
    ''' so the "New / Edit MSWP…" button edits the EXISTING swap in place (same FormID) instead of opening a blank
    ''' one — a swap already saved into the plugin (or any load-order MSWP the field points at) shows up for
    ''' editing. Returns Nothing when <paramref name="fid"/> is 0, a draft sentinel, or does not resolve to an
    ''' MSWP; the caller then falls back to a fresh blank NEW draft. On Cancel the caller unregisters this draft,
    ''' reverting the field to referencing the real record.</summary>
    Friend Function BuildMswpOverrideDraftFromReal(fid As UInteger) As MswpDraft
        If fid = 0UI OrElse OutfitDraft.IsDraftFormID(fid) Then Return Nothing
        Dim rec = _pluginManager.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "MSWP" Then Return Nothing
        ' El borrador trabaja sobre una COPIA del record: cancelar el editor tiene que dejar
        ' el original como estaba.
        Dim d = MswpDraft.Edicion(rec, _pluginManager)
        If d Is Nothing Then Return Nothing
        RegisterMswpDraft(d)
        Return d
    End Function

    ''' <summary>The master plugin manager (the record source) exposed for the ARMA Editor's
    ''' <see cref="FormIdPicker_Form"/> instances — it enumerates records of the allowed signatures
    ''' itself. Read-only handle; the editor never mutates loaded records.</summary>
    Friend ReadOnly Property PluginManagerForEditor As PluginManager
        Get
            Return _pluginManager
        End Get
    End Property

    ''' <summary>True if <paramref name="edid"/> is free for a new owned ARMO/ARMA/MSWP record: not used by
    ''' another draft (any kind) or any loaded record. EditorIDs are globally unique. Reuses the outfit-draft
    ''' check (which already covers OTFT/LVLI drafts + AllRecords) and adds the armor draft kinds.</summary>
    Friend Function IsRecordEditorIdAvailable(edid As String) As Boolean
        If String.IsNullOrWhiteSpace(edid) Then Return False
        For Each d In _armoDrafts
            If String.Equals(d.Record.EditorID, edid,
                             StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        For Each d In _armaDrafts
            If String.Equals(d.Record.EditorID, edid,
                             StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        For Each d In _mswpDrafts
            If String.Equals(d.Record.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Return IsOutfitEditorIdAvailable(edid)
    End Function

    ''' <summary>Display name for any record/draft FormID used in the Armor Editor combos (MSWP/TXST/RACE/ARMO).
    ''' Drafts → their EditorID; real records → EditorID/FullName; 0 → "(none)".</summary>
    Friend Function GetRecordDisplayNameForEditor(formID As UInteger) As String
        If formID = 0UI Then Return "(none)"
        Dim md = TryGetMswpDraft(formID)
        If md IsNot Nothing Then Return md.Record.EditorID & "  (new)"
        Dim ad = TryGetArmoDraft(formID)
        If ad IsNot Nothing Then Return If(Not String.IsNullOrEmpty(ad.Record.Name), ad.Record.Name,
                                           ad.Record.EditorID) & "  (new)"
        Dim aad = TryGetArmaDraft(formID)
        If aad IsNot Nothing Then Return aad.Record.EditorID & "  (new)"
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return formID.ToString("X8")
        Return If(Not String.IsNullOrEmpty(rec.EditorID), rec.EditorID, formID.ToString("X8"))
    End Function

    ''' <summary>Synthesize an <see cref="Canon.IMswp"/> from an in-memory MSWP draft, or Nothing if
    ''' <paramref name="fid"/> is not an MSWP draft. Deep-copies the substitution pairs so the render path
    ''' never mutates the draft. Synthesized FRESH on each call (no caching) so a live edit to the draft's
    ''' swaps shows on the next render.
    ''' <para>ARMO/ARMA no necesitan su espejo de este método: el borrador YA ES la vista canónica
    ''' (<see cref="ArmoDraft.Record"/> / <see cref="ArmaDraft.Record"/> son <c>Canon.IArmo</c>/<c>Canon.IArma</c>
    ''' directo), así que <see cref="NpcRenderContext.ArmoDraftResolver"/>/<c>ArmaDraftResolver</c> devuelven
    ''' <c>TryGetArmoDraft(fid)?.Record</c> sin sintetizar nada.</para></summary>
    Private Function BuildMswpDataFromDraft(fid As UInteger) As Canon.IMswp
        Dim d = TryGetMswpDraft(fid)
        If d Is Nothing Then Return Nothing
        ' El borrador ES el record: no hay nada que sintetizar.
        Return d.Record
    End Function

    ''' <summary>Content signature of an MSWP DRAFT (EditorID + tree folder + every substitution) so an ARMA/ARMO
    ''' editor's preview KEY changes when the user EDITS a referenced material-swap draft. The draft's FormID
    ''' stays the same across an edit, so a FormID-only key would skip the re-render and the swap wouldn't show
    ''' until reopening. "" for a real record or a missing draft (a real MSWP can't be edited in-session).</summary>
    Friend Function GetMswpDraftSignature(fid As UInteger) As String
        Dim d = TryGetMswpDraft(fid)
        If d Is Nothing Then Return ""
        Dim sb As New System.Text.StringBuilder()
        sb.Append(d.Record.EditorID).Append("~"c).Append(If(d.Record.TreeFolder, ""))
        For Each s In d.Record.MaterialSubstitutions
            sb.Append("|"c).Append(If(s.SubstitutionOriginalMaterial, "")).Append(">"c).Append(If(s.SubstitutionReplacementMaterial, "")).
               Append("~"c).Append(If(s.SubstitutionTreeFolderObsolete, "")).
               Append(If(s.TieneIndiceDeColor(), "#" & s.SubstitutionColorRemappingIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), ""))
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Allocate a fresh provisional FormID for a NEW outfit draft (0xFF high byte +
    ''' object index ≥0x800, the FO4 new-record convention). The writer rewrites it to the real
    ''' plugin self-index FormID at save time.</summary>
    Friend Function AllocateDraftFormID() As UInteger
        Dim fid As UInteger = OutfitDraft.DraftFormIdHighByte Or _nextDraftObjIndex
        _nextDraftObjIndex += 1UI
        Return fid
    End Function

    ''' <summary>Short source-plugin (esp/esm) name for a FormID, shown next to the ID in the Edit Outfit
    ''' lists and used by their filters. Not-yet-saved drafts → "(new)"; otherwise the originating plugin
    ''' via <see cref="PluginManager.GetOriginatingPluginName"/> (ESL-aware high-byte scheme).</summary>
    Friend Function GetOutfitPluginName(formID As UInteger) As String
        If OutfitDraft.IsDraftFormID(formID) Then Return "(new)"
        Return If(_pluginManager.GetOriginatingPluginName(formID), "")
    End Function

    ''' <summary>Selectable ARMO items (armor/clothing pieces) for the Edit Outfit "Create" tab,
    ''' filtered by (race, gender): every ARMO that has a race-valid ARMA (<see cref="EquipResolver.ArmaMatchesRace"/>)
    ''' carrying a world mesh for the gender (male/female with the renderer's fallback). Returns
    ''' (FormID, DisplayName, SlotMask, Plugin). SlotMask is the effective slot footprint — the union of
    ''' <see cref="EquipResolver.ArmaGeometryMask"/> across the ARMO's race-valid addons (same per-addon choice the
    ''' render makes), so the conflict resolver sees exactly what the render does. Cached per (race, gender)
    ''' — the full ARMO+ARMA sweep is the costly part; ARMO/ARMA parses are globally cached so each record
    ''' is parsed once.</summary>
    Friend Function GetArmoItemCandidates(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        Dim cacheKey = (npcRaceFID, isFemale)
        Dim cached As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String)) = Nothing
        If _armoItemCandidateCache.TryGetValue(cacheKey, cached) Then Return cached

        Dim outList As New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        ' Un solo contexto para todo el barrido: la cadena de razas del redirect RNAM se resuelve una vez.
        Dim eqCtx = EquipCtx(npcRaceFID, isFemale)
        Dim armoRecs = _pluginManager.GetRecordsOfType("ARMO")
        If armoRecs IsNot Nothing Then
            For Each rec In armoRecs
                If rec Is Nothing Then Continue For
                Dim armoFID = rec.Header.FormID
                Dim armo As Canon.IArmo
                Try
                    armo = _ctx.GetParsedArmo(armoFID)
                Catch
                    Continue For
                End Try
                If armo Is Nothing Then Continue For
                ' El gate de power-armor (no ofrecer piezas de PA a una raza que no es de PA: montarían mal
                ' sin armazón) lo aplica la ley única y se reporta en PowerArmorRejected.
                ' Effective slot footprint, matching the render (CollectArmoCandidates): per addon take
                ' the ARMA's own BOD2 mask, falling back to the ARMO's only when the ARMA declares none, and
                ' UNION across every race-valid addon that has a mesh. The render builds one candidate per
                ' ARMA and feeds them all to EquipResolver; for the Create tab — one piece per ARMO —
                ' the union is the equivalent footprint, so two pieces overlapping on ANY slot conflict the
                ' same way they do in-game. (The old "ARMO BOD2 first, first ARMA only" path used a declared
                ' mask that can diverge from the ARMA's real slots, so same-slot pieces weren't eliminated.)
                Dim armoSlot = EquipResolver.BuildFootprint(armoFID, eqCtx)
                If armoSlot.Valid Then
                    Dim disp As String = If(Not String.IsNullOrEmpty(armo.Name), armo.Name,
                                            If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID, armoFID.ToString("X8")))
                    outList.Add((armoFID, disp, armoSlot.OcclusionMask, GetOutfitPluginName(armoFID)))
                End If
            Next
        End If

        ' LVLI items — let the user add a leveled list as an outfit piece (it persists AS a leveled entry;
        ' the engine rolls at runtime, the editor previews a rerollable realization). Offered only for the
        ' leveled lists that outfits actually use (GetOutfitLeveledListFormIDs), and only when the list can
        ' produce >=1 race/gender-valid terminal. SlotMask = UNION of those terminals' effective masks (the
        ' footprint it could cover). The Type is derived at use time via IsLeveledItem (record signature).
        For Each lvliFID In GetOutfitLeveledListFormIDs()
            Dim lvliRec = _pluginManager.GetRecord(lvliFID)
            If lvliRec Is Nothing Then Continue For
            Dim unionMask As UInteger = 0UI
            Dim anyValid As Boolean = False
            For Each terminalFID In OutfitResolver.EnumerateItemTerminalArmos(lvliFID, _pluginManager)
                Dim tr = EquipResolver.BuildFootprint(terminalFID, eqCtx)
                If tr.Valid Then
                    anyValid = True
                    unionMask = unionMask Or tr.OcclusionMask
                End If
            Next
            If anyValid Then
                Dim disp As String = If(lvliRec.EditorID <> "", lvliRec.EditorID, lvliFID.ToString("X8"))
                outList.Add((lvliFID, disp, unionMask, GetOutfitPluginName(lvliFID)))
            End If
        Next

        outList = outList.OrderBy(Function(x) x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
        _armoItemCandidateCache(cacheKey) = outList
        Return outList
    End Function

    ''' <summary>Like <see cref="GetArmoItemCandidates"/> but ALSO appends the author-built drafts — own LVLI
    ''' drafts ("[LVL]") AND dirty in-memory ARMO drafts ("(new)") — fresh on every call (not cached: they
    ''' change as the user builds them). The Edit Outfit picker uses this so own leveled lists are addable /
    ''' nestable and an unsaved armor piece can be dropped into an outfit before Save. Each draft's slot
    ''' footprint = UNION of <see cref="EquipResolver.BuildFootprint"/> over the terminals/addons it resolves to
    ''' (draft-aware: <see cref="EnumerateLeveledTerminalsAll"/> for LVLI, the draft ARMA children for ARMO).</summary>
    Friend Function GetArmoItemCandidatesWithDrafts(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        Dim baseList = GetArmoItemCandidates(npcRaceFID, isFemale)
        If _leveledListDrafts.Count = 0 AndAlso _armoDrafts.Count = 0 Then Return baseList
        Dim result As New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(baseList)

        ' Own leveled-list drafts (addable as a leveled outfit piece / nestable into other LVLs).
        For Each d In _leveledListDrafts
            Dim unionMask As UInteger = 0UI
            For Each t In EnumerateLeveledTerminalsAll(d.FormID)
                unionMask = unionMask Or ArmoFootprintFor(t, npcRaceFID, isFemale).OcclusionMask
            Next
            result.Add((d.FormID, d.Record.EditorID & "  [LVL]", unionMask, "(new)"))
        Next

        ' Dirty ARMO drafts — an unsaved armor/clothing piece selectable as an outfit item. Filtered by the
        ' SAME race/gender rule the real candidates use: EquipResolver.BuildFootprint walks the draft's ARMA
        ' children (resolved via the now-draft-aware GetParsedArma) applying EquipResolver.ArmaMatchesRace + gender
        ' mesh presence, returning Valid only when at least one addon fits. Same power-armor gate too. The
        ' slot footprint is the union of those addons' geometry mask — exactly what the render resolves.
        For Each d In _armoDrafts
            If Not d.IsDirty Then Continue For
            ' El borrador ES la vista canónica: nada que sintetizar.
            Dim armo As Canon.IArmo = d.Record
            If armo Is Nothing Then Continue For
            Dim armoSlot = ArmoFootprintFor(d.FormID, npcRaceFID, isFemale)
            ' Mismo gate PA que los candidatos reales: la ley lo marca en el footprint.
            If armoSlot.PowerArmorRejected Then Continue For
            ' Own ARMO drafts are the user's OWN creations (few, hand-authored) — ALWAYS list them, even when no addon
            ' matches this NPC's race/gender (armoSlot.Valid=False). Hiding a just-created armor was the reported bug
            ' ("New armor doesn't show up"): a brand-new ARMO has no race-matching addon yet, so the old
            ' `If Not armoSlot.Valid Then Continue For` gate dropped it silently. Real vanilla candidates KEEP the race
            ' filter (thousands of records) — that gate lives in GetArmoItemCandidates and is untouched. For a not-valid
            ' draft the mask falls back to the ARMO's own BOD2 (EquipResolver.BuildFootprint) so it still occupies slots
            ' in the conflict resolver; the label flags WHY it may not render on this NPC.
            Dim newTag As String = If(armoSlot.Valid, "  (new)", "  (new · no addon for this race/gender)")
            Dim disp As String = If(Not String.IsNullOrEmpty(armo.Name), armo.Name,
                                    If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID, d.FormID.ToString("X8")))
            ' An OVERRIDE draft shares the real record's FormID, which is already in baseList — REPLACE that
            ' entry in place so the FormID appears ONCE with the draft's (edited) name/slots. Appending instead
            ' would leave the stale real entry first, and the FormID→candidate index (first-wins) would shadow
            ' the draft — the outfit's selected piece would keep resolving to the OLD data. A NEW draft (its
            ' provisional FormID isn't in baseList) simply appends.
            Dim entry = (d.FormID, disp & newTag, armoSlot.OcclusionMask, "(new)")
            Dim existingIdx = result.FindIndex(Function(x) x.FormID = d.FormID)
            If existingIdx >= 0 Then result(existingIdx) = entry Else result.Add(entry)
        Next

        Return result
    End Function

    ''' <summary>El contexto con el que la app llama a la LEY ÚNICA de equip
    ''' (<see cref="EquipResolver"/>, FO4_Base_Library): resolvedores draft-aware, la cadena de razas del
    ''' redirect RNAM y el gate de power-armor, que son lo único que la librería no puede saber sola.
    ''' TODO cálculo de slots de armadura sale de acá — el render, el bake y los editores no vuelven a
    ''' recorrer armatures por su cuenta.</summary>
    Friend Function EquipCtx(npcRaceFID As UInteger, isFemale As Boolean) As EquipResolver.EquipContext
        Return _ctx.EquipCtx(npcRaceFID, isFemale)
    End Function

    ''' <summary>Footprint de un ARMO para (raza, género) por la ley única. Atajo sobre
    ''' <see cref="EquipResolver.BuildFootprint"/> con el contexto de <see cref="EquipCtx"/>.</summary>
    Friend Function ArmoFootprintFor(armoFid As UInteger, npcRaceFID As UInteger, isFemale As Boolean) As EquipResolver.ArmoFootprint
        Return EquipResolver.BuildFootprint(armoFid, EquipCtx(npcRaceFID, isFemale))
    End Function


    ''' <summary>Slot footprint of ANY outfit reference (ARMO or LVLI) for a race/gender — robust for references
    ''' OUTSIDE the (race-filtered, bounded) candidate universe, so the leveled-list drill-down can show real slots
    ''' for every LVLO entry instead of "(none)". LVLI → UNION of its terminals' effective masks (stable, not a
    ''' random sample); ARMO → its effective mask (addon union race-valid, falling back to BOD2). Draft-aware via
    ''' <see cref="_ctx"/>. Mirror of the slot computation in <see cref="GetArmoItemCandidatesWithDrafts"/>.</summary>
    Friend Function GetReferenceSlotMask(fid As UInteger, npcRaceFID As UInteger, isFemale As Boolean) As UInteger
        If fid = 0UI Then Return 0UI
        If IsLeveledItem(fid) Then
            Dim unionMask As UInteger = 0UI
            For Each t In EnumerateLeveledTerminalsAll(fid)
                unionMask = unionMask Or ArmoFootprintFor(t, npcRaceFID, isFemale).OcclusionMask
            Next
            Return unionMask
        End If
        Return ArmoFootprintFor(fid, npcRaceFID, isFemale).OcclusionMask
    End Function

    ''' <summary>The set of LVLI FormIDs referenced directly by any OTFT's INAM — i.e. the leveled lists that
    ''' outfits actually use. These are what the Create tab offers as addable leveled pieces (a bounded,
    ''' relevant set, instead of sweeping every LVLI in the load order). Cached once per load order.</summary>
    Private _outfitLeveledListCache As List(Of UInteger) = Nothing
    Private Function GetOutfitLeveledListFormIDs() As List(Of UInteger)
        If _outfitLeveledListCache IsNot Nothing Then Return _outfitLeveledListCache
        Dim seen As New HashSet(Of UInteger)
        Dim result As New List(Of UInteger)
        For Each otftFID In _outfitUniverse
            Dim rec = _pluginManager.GetRecord(otftFID)
            If rec Is Nothing OrElse rec.Header.Signature <> "OTFT" Then Continue For
            For Each itemFID In Canon.CanonRecords.Otft(rec, _pluginManager).Prendas()
                If seen.Add(itemFID) AndAlso IsLeveledItem(itemFID) Then result.Add(itemFID)
            Next
        Next
        _outfitLeveledListCache = result
        Return result
    End Function

    ''' <summary>Preview WYSIWYG del outfit: renderiza el NPC vestido con <paramref name="overrideValue"/>
    ''' en el host propio del picker usando EXACTAMENTE el pipeline del preview principal
    ''' (<see cref="RenderInHostAsync"/>); no hay resolver "liviano" aparte, así que lo que muestra el
    ''' picker es lo que produce el render.
    ''' <paramref name="overrideValue"/>: Nothing → respeta el DOFT crudo · Some(0) → desnudo · Some(fid) → OTFT.
    ''' El override es HOST-SCOPED: no toca <see cref="_appliedPresets"/>, así que navegar outfits no ensucia
    ''' el estado commiteado y Cancel no necesita restaurar nada.</summary>
    Friend Async Function PreviewOutfitInHostAsync(host As NpcRenderHost, npcFormID As UInteger, overrideValue As UInteger?) As Task
        If host Is Nothing Then Return
        host.OutfitPreviewActive = True
        host.OutfitPreviewOverride = overrideValue
        Await RenderInHostAsync(host, npcFormID)
    End Function

    ''' <summary>Render toggles for the Edit Outfit picker preview: FullBody baseline (every
    ''' morph/sculpt/body-weight stage ON so the outfit is judged against the real body), with gore read
    ''' from the user's global checkbox. Same baseline EditBody_Form uses for its embedded preview.</summary>
    Friend Function BuildOutfitPickerToggles() As RenderToggles
        Return RenderToggles.FullBody(CheckBoxRenderGore.Checked)
    End Function

    ''' <summary>Remove an in-memory outfit draft (by FormID). Used by the Edit Outfit picker to drop the
    ''' throwaway preview draft (<see cref="OutfitDraft.PreviewDraftFormID"/>) it registers while the
    ''' user assembles a Create-tab outfit, so it never leaks into Browse / the save set.</summary>
    Friend Sub UnregisterOutfitDraft(formID As UInteger)
        Dim existing = _outfitDrafts.FirstOrDefault(Function(x) x.FormID = formID)
        If existing IsNot Nothing Then _outfitDrafts.Remove(existing)
    End Sub

    ''' <summary>Resolve an outfit FormID to its INAM item list — the entries AS AUTHORED (ARMO **or LVLI**
    ''' FormIDs), NOT sampled. A draft → its ItemFormIDs; a real OTFT → its parsed INAM. Used by the Create
    ''' tab's Override pre-fill so a leveled entry stays a leveled piece (not flattened to one realization).</summary>
    Friend Function ResolveOutfitItemList(fid As UInteger) As List(Of UInteger)
        If fid = 0UI Then Return New List(Of UInteger)
        Dim draft = TryGetOutfitDraft(fid)
        If draft IsNot Nothing Then Return New List(Of UInteger)(draft.Prendas())
        Dim rec = _pluginManager.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "OTFT" Then Return New List(Of UInteger)
        Return New List(Of UInteger)(Canon.CanonRecords.Otft(rec, _pluginManager).Prendas())
    End Function

    ''' <summary>True if <paramref name="fid"/> is a leveled item list — a real LVLI record OR an author-built
    ''' LVLI draft (which lives outside the PluginManager, so GetRecord wouldn't see it).</summary>
    Friend Function IsLeveledItem(fid As UInteger) As Boolean
        If fid = 0UI Then Return False
        If IsOwnLeveledDraft(fid) Then Return True
        Dim rec = _pluginManager.GetRecord(fid)
        Return rec IsNot Nothing AndAlso rec.Header.Signature = "LVLI"
    End Function

    ''' <summary>Deja que el sorteador de la librería vea también las listas por nivel que
    ''' todavía son borradores y no están en ningún archivo.
    ''' <para>El borrador YA ES su record, así que no hay nada que adaptar: se devuelve tal cual — una
    ''' copia campo por campo arriesga que un campo olvidado haga que el sorteo de un borrador se
    ''' comporte distinto al de una lista real.</para></summary>
    Private Function ResolveLeveledDraftView(formID As UInteger) As Canon.ILvli
        Dim d = TryGetLeveledListDraft(formID)
        If d Is Nothing Then Return Nothing
        Return d.Record
    End Function

    ''' <summary>Sample ONE realization of a leveled item (real LVLI or own draft) to terminal ARMO FormIDs,
    ''' through the single lib sampler with the draft view injected. Handles UseAll / ChanceNone / Count /
    ''' CalcEach / nesting uniformly for real and draft lists.</summary>
    Friend Function SampleLeveledTerminals(fid As UInteger) As List(Of UInteger)
        Return OutfitResolver.SampleItemWithKeywords(fid, _pluginManager, leveledResolver:=AddressOf ResolveLeveledDraftView) _
            .Select(Function(p) p.ArmoFormID).ToList()
    End Function

    ''' <summary>Deterministic: ALL terminal ARMOs a leveled item can produce (real or own draft), for the
    ''' candidate-list slot footprint — through the single lib enumerator with the draft view injected.</summary>
    Friend Function EnumerateLeveledTerminalsAll(fid As UInteger) As List(Of UInteger)
        Return OutfitResolver.EnumerateItemTerminalArmos(fid, _pluginManager, leveledResolver:=AddressOf ResolveLeveledDraftView)
    End Function

    ''' <summary>True if adding <paramref name="itemFid"/> into the own leveled list <paramref name="lvlFid"/>
    ''' would create a cycle — the item IS the list, or the item is an own LVL that already contains the list
    ''' (directly or transitively, e.g. lvl1→lvl2→lvl1). Blocks "Add to lvl" cycles. (Runtime resolution is
    ''' already cycle-safe via visited sets; this stops the user authoring one in the first place.)</summary>
    Friend Function WouldCreateLeveledCycle(lvlFid As UInteger, itemFid As UInteger) As Boolean
        If lvlFid = itemFid Then Return True
        If Not IsOwnLeveledDraft(itemFid) Then Return False   ' real LVLIs can't reference our drafts → no cycle
        Return LeveledDraftReaches(itemFid, lvlFid, New HashSet(Of UInteger))
    End Function

    Private Function LeveledDraftReaches(fromFid As UInteger, targetFid As UInteger, visited As HashSet(Of UInteger)) As Boolean
        Dim d = TryGetLeveledListDraft(fromFid)
        If d Is Nothing OrElse Not visited.Add(fromFid) Then Return False
        For Each e In d.Record.LeveledListEntries
            If e.LeveledListEntryItem = targetFid Then Return True
            If IsOwnLeveledDraft(e.LeveledListEntryItem) AndAlso
               LeveledDraftReaches(e.LeveledListEntryItem, targetFid, visited) Then Return True
        Next
        Return False
    End Function

    ''' <summary>Flatten a draft to its render-ready terminal ARMO list: ARMO items pass through; LVLI items
    ''' use their cached realization (sampled once via <see cref="OutfitResolver.SampleItemWithKeywords"/>,
    ''' stored on the draft so the preview is STABLE between renders — Reroll clears the cache to re-sample).
    ''' The draft keeps the LVLI FormIDs (persists as leveled); this is only the editor's current sample.</summary>
    Friend Function ResolveDraftArmoList(draft As OutfitDraft) As List(Of UInteger)
        Dim outList As New List(Of UInteger)
        If draft Is Nothing Then Return outList
        For Each itemFid In draft.Prendas()
            If IsLeveledItem(itemFid) Then
                Dim realized As List(Of UInteger) = Nothing
                If Not draft.LvliRealization.TryGetValue(itemFid, realized) Then
                    realized = SampleLeveledTerminals(itemFid)
                    draft.LvliRealization(itemFid) = realized
                End If
                outList.AddRange(realized)
            Else
                outList.Add(itemFid)
            End If
        Next
        Return outList
    End Function

    ''' <summary>Reroll an LVLI item's cached realization (clear it so the next resolve re-samples). Pass 0
    ''' to reroll ALL leveled items in the draft.</summary>
    Friend Sub RerollDraftLeveled(draft As OutfitDraft, Optional lvliFid As UInteger = 0UI)
        If draft Is Nothing Then Return
        If lvliFid = 0UI Then
            draft.LvliRealization.Clear()
        Else
            draft.LvliRealization.Remove(lvliFid)
        End If
    End Sub


    ''' <summary>Sample ONE realization of an LVLI for the Edit Outfit picker: the terminal ARMO FormIDs +
    ''' their UNION effective slot mask (for the piece's display/conflict, approach A — the LVLI behaves as
    ''' its current sample). Called on Add and Reroll; the result is cached on the piece/draft so the preview
    ''' is stable between renders. The draft persists the LVLI FormID, not this realization.</summary>
    Friend Function SampleLeveledRealization(lvliFid As UInteger, npcRaceFID As UInteger, isFemale As Boolean) As (Terminals As List(Of UInteger), SlotMask As UInteger)
        Dim terminals = SampleLeveledTerminals(lvliFid)
        Dim mask As UInteger = 0UI
        For Each t In terminals
            mask = mask Or ArmoFootprintFor(t, npcRaceFID, isFemale).OcclusionMask
        Next
        Return (terminals, mask)
    End Function

    ''' <summary>Register a newly created/edited outfit draft so the render (TryGetOutfitDraft), the
    ''' Browse list (GetOutfitCandidates) and the Save flow can see it. Replaces any existing draft
    ''' with the same FormID (re-edit).</summary>
    Friend Sub RegisterOutfitDraft(d As OutfitDraft)
        If d Is Nothing Then Return
        Dim existing = _outfitDrafts.FirstOrDefault(Function(x) x.FormID = d.FormID)
        If existing IsNot Nothing Then _outfitDrafts.Remove(existing)
        _outfitDrafts.Add(d)
    End Sub

    ''' <summary>Register/replace an author-built leveled list (LVLI draft). Seen by the candidate list,
    ''' the draft-aware resolver and the Save flow.</summary>
    Friend Sub RegisterLeveledListDraft(d As LeveledListDraft)
        If d Is Nothing Then Return
        Dim existing = _leveledListDrafts.FirstOrDefault(Function(x) x.FormID = d.FormID)
        If existing IsNot Nothing Then _leveledListDrafts.Remove(existing)
        _leveledListDrafts.Add(d)
    End Sub

    ''' <summary>If <paramref name="fid"/> is a REAL (non-draft) LVLI record, build + register an OVERRIDE
    ''' <see cref="LeveledListDraft"/> seeded with its parsed header (EditorID/flags/ChanceNone/MaxCount) and its
    ''' LVLO entries (each RefFormID is the parser's already-resolved GLOBAL FormID), so "Override LVL…" edits the
    ''' EXISTING list in place (same FormID) instead of authoring a blank one. Mirrors
    ''' <see cref="BuildMswpOverrideDraftFromReal"/>. Returns Nothing when <paramref name="fid"/> is 0, a draft
    ''' sentinel, or does not resolve to an LVLI; the caller then falls back to a fresh NEW draft. If an override
    ''' draft for this FormID already exists it is returned as-is (don't clobber in-progress edits). On Cancel the
    ''' caller unregisters this draft, reverting the field to referencing the real record.</summary>
    Friend Function BuildLeveledOverrideDraftFromReal(fid As UInteger) As LeveledListDraft
        If fid = 0UI OrElse OutfitDraft.IsDraftFormID(fid) Then Return Nothing
        Dim already = TryGetLeveledListDraft(fid)
        If already IsNot Nothing Then Return already
        Dim rec = _pluginManager.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "LVLI" Then Return Nothing
        ' El borrador trabaja sobre una COPIA del record: cancelar el editor tiene que dejar el
        ' original
        ' como estaba. Edicion() ya trae TODOS los campos (no sólo el subconjunto que copiaba a mano
        ' antes).
        Dim d = LeveledListDraft.Edicion(rec, _pluginManager)
        If d Is Nothing Then Return Nothing
        RegisterLeveledListDraft(d)
        Return d
    End Function

    ''' <summary>The in-memory LVLI draft for <paramref name="formID"/>, or Nothing.</summary>
    Friend Function TryGetLeveledListDraft(formID As UInteger) As LeveledListDraft
        If formID = 0UI Then Return Nothing
        For Each d In _leveledListDrafts
            If d.FormID = formID Then Return d
        Next
        Return Nothing
    End Function

    ''' <summary>True if <paramref name="formID"/> is an author-built LVLI draft (own, not a vanilla/loaded
    ''' record). Drives the "Add to lvl" enable (only own leveled lists can be edited).</summary>
    Friend Function IsOwnLeveledDraft(formID As UInteger) As Boolean
        Return TryGetLeveledListDraft(formID) IsNot Nothing
    End Function

    ''' <summary>True if <paramref name="edid"/> is NOT already used by any loaded record or existing
    ''' draft (case-insensitive). Used by the Create tab to validate a new outfit's EditorID before
    ''' committing. O(N) over AllRecords — called on commit, not per keystroke.</summary>
    Friend Function IsOutfitEditorIdAvailable(edid As String) As Boolean
        If String.IsNullOrWhiteSpace(edid) Then Return False
        For Each d In _outfitDrafts
            If d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For   ' throwaway picker-preview draft
            If String.Equals(d.Record.EditorID, edid,
                             StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        For Each kvp In _pluginManager.AllRecords
            Dim rec = kvp.Value
            If rec Is Nothing Then Continue For
            If String.Equals(rec.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Return True
    End Function

    ''' <summary>True if <paramref name="edid"/> is free for a new author-built LVLI: not used by another
    ''' leveled draft, an outfit draft, or any loaded record (EditorIDs are globally unique).</summary>
    Friend Function IsLeveledEditorIdAvailable(edid As String) As Boolean
        If String.IsNullOrWhiteSpace(edid) Then Return False
        For Each d In _leveledListDrafts
            If String.Equals(d.Record.EditorID, edid,
                             StringComparison.OrdinalIgnoreCase) Then Return False
        Next
        Return IsOutfitEditorIdAvailable(edid)
    End Function

    ''' <summary>Discover and parse F4SE LooksMenu skin templates from on-disk JSONs. Mirrors
    ''' f4ee/SkinInterface.cpp:461-488 (LoadSkinMods) — iterates every plugin's
    ''' Data\F4SE\Plugins\F4EE\Skin\&lt;pluginName&gt;\skin.json plus Data\F4SE\Plugins\F4EE\Skin\Loose\*.json.
    ''' Form identifiers in the JSON ("PluginFile|FORMID") are resolved against the plugin manager
    ''' so unresolved templates are skipped silently (LM does the same).</summary>
    Private Sub BuildLmSkinTemplateCache()
        _lmSkinTemplates.Clear()
        _lmSkinTemplates.AddRange(LmSkinTemplateLoader.BuildCache(_dataPath, _pluginManager))
    End Sub

    ''' <summary>Build the LM body-overlay ("tattoo") template cache. Mirrors
    ''' <see cref="BuildLmSkinTemplateCache"/> structurally, but the on-disk layout is
    ''' <c>Data\F4SE\Plugins\F4EE\Overlays\&lt;pluginFileName&gt;\overlays.json</c> + an
    ''' <c>Overlays\Loose\*.json</c> folder (OverlayInterface.cpp:1025-1052 LoadOverlayMods). Each file
    ''' is parsed by <see cref="OverlayTemplateLoader.LoadFromFile"/>; the templates are then bucketed by
    ''' gender (the engine keeps two maps, <c>m_overlayTemplates[isFemale?1:0]</c>, OverlayInterface.cpp:1084)
    ''' with FIRST-LOADED-WINS on a duplicate id WITHIN a gender (the engine's <c>find</c>-then-<c>emplace</c>
    ''' only inserts when the id is absent, :1084-1090; later files with the same id keep the existing
    ''' template). The disk scan order is load-order plugins then the Loose folder, identical to the engine.</summary>
    Private Sub BuildOverlayTemplateCache()
        _overlayTemplates(0).Clear()
        _overlayTemplates(1).Clear()
        If String.IsNullOrEmpty(_dataPath) Then Return
        Dim baseOverlayDir = Path.Combine(_dataPath, "F4SE", "Plugins", "F4EE", "Overlays")
        If Not Directory.Exists(baseOverlayDir) Then Return

        ' Per-plugin templates: Overlays\<pluginName>\overlays.json (load order = priority order).
        For Each plugin In _pluginManager.Plugins
            Dim p = Path.Combine(baseOverlayDir, plugin.FileName, "overlays.json")
            If File.Exists(p) Then AddOverlayTemplatesFromFile(p)
        Next
        ' Loose templates: Overlays\Loose\*.json
        Dim looseDir = Path.Combine(baseOverlayDir, "Loose")
        If Directory.Exists(looseDir) Then
            For Each p In Directory.EnumerateFiles(looseDir, "*.json", SearchOption.TopDirectoryOnly)
                AddOverlayTemplatesFromFile(p)
            Next
        End If
    End Sub

    ''' <summary>Parse one overlays.json and append its templates into the gendered cache, keeping the
    ''' first-loaded template on a duplicate id within the same gender bucket (engine parity —
    ''' OverlayInterface.cpp:1084-1090). <see cref="OverlayTemplate.Gender"/> is already clamped to 0..1
    ''' by the loader, so it indexes the two buckets directly.</summary>
    Private Sub AddOverlayTemplatesFromFile(filePath As String)
        For Each tpl In OverlayTemplateLoader.LoadFromFile(filePath)
            If tpl Is Nothing OrElse String.IsNullOrEmpty(tpl.Id) Then Continue For
            Dim bucket = _overlayTemplates(If(tpl.Gender = 1, 1, 0))
            Dim duplicate As Boolean = False
            For Each existing In bucket
                If String.Equals(existing.Id, tpl.Id, StringComparison.Ordinal) Then
                    duplicate = True
                    Exit For
                End If
            Next
            If Not duplicate Then bucket.Add(tpl)
        Next
    End Sub

    ''' <summary>Templates for one gender, sorted by Sort then DisplayName (for the Phase 4 editor combo).
    ''' Female NPC → gender bucket 1, male → 0 — matching the engine's per-gender map split.</summary>
    Friend Function GetOverlayTemplateCandidates(isFemale As Boolean) As List(Of OverlayTemplate)
        Dim bucket = _overlayTemplates(If(isFemale, 1, 0))
        Return bucket.
            OrderBy(Function(t) t.Sort).
            ThenBy(Function(t) t.DisplayName, StringComparer.OrdinalIgnoreCase).
            ToList()
    End Function

    ''' <summary>Resolve an overlay template by id within the matching gender bucket. Nothing if the id
    ''' isn't loaded for that gender — caller treats that as "this overlay contributes no layer" (engine
    ''' parity: <c>GetTemplateByName</c> returns null and <c>ForEachOverlayBySlot</c> simply skips it,
    ''' OverlayInterface.cpp:443-448). Mirrors <see cref="ResolveLmSkinTemplate"/>.</summary>
    Private Function ResolveOverlayTemplate(id As String, isFemale As Boolean) As OverlayTemplate
        If String.IsNullOrEmpty(id) Then Return Nothing
        For Each tpl In _overlayTemplates(If(isFemale, 1, 0))
            If String.Equals(tpl.Id, id, StringComparison.Ordinal) Then Return tpl
        Next
        Return Nothing
    End Function

    ''' <summary>Friend wrapper exposing <see cref="ResolveOverlayTemplate"/> at Friend scope, mirroring
    ''' <see cref="ResolveLmSkinTemplate_Friend"/> — lets the Phase 4 editor reuse the same resolver.</summary>
    Friend Function ResolveOverlayTemplate_Friend(id As String, isFemale As Boolean) As OverlayTemplate
        Return ResolveOverlayTemplate(id, isFemale)
    End Function

    ''' <summary>Classify NPCs:
    ''' - _directlyPlacedNPCFormIDs: NPCs placed in CELLs via ACHR records (unique characters).
    ''' - _npcsInGameWorld: All NPCs in game (placed + LVLN encounters).
    ''' - _npcsUsedAsTemplates: NPCs referenced as TPLT/TPTA source by other NPCs.
    ''' - _finalLVLNFormIDs: Top-level LVLNs not nested inside another LVLN.
    ''' A "template only" NPC is one used as template but never placed or in any LVLN.</summary>
    Private Sub BuildNPCClassification()
        _directlyPlacedNPCFormIDs.Clear()
        _npcsInGameWorld.Clear()
        _npcsUsedAsTemplates.Clear()
        _finalLVLNFormIDs.Clear()
        _lvlnDataCache.Clear()
        _lvlnLeavesCache.Clear()
        _npcSearchableCache.Clear()
        _npcDisplayLabelCache.Clear()
        _npcSortKeyCache.Clear()
        _npcHeredaAparienciaCache.Clear()

        ' Collect NPCs placed in the world (ACHR records from CELL/WRLD groups)
        Dim placedNPCs = _pluginManager.GetPlacedNPCFormIDs()
        _directlyPlacedNPCFormIDs.UnionWith(placedNPCs)
        _npcsInGameWorld.UnionWith(placedNPCs)

        ' Parse and cache all LVLN records; track which LVLNs are nested inside others
        Dim nestedLVLNFormIDs As New HashSet(Of UInteger)()
        Dim allLVLNRecords = _pluginManager.GetRecordsOfType("LVLN")

        ' ParseLVLN pasó a LANZAR ante un MODS malformado (el gate game-aware de Material Swap vs Alternate
        ' Textures). Sin este Catch, UN solo LVLN roto —un plugin mal mergeado, uno editado a mano— impedía
        ' construir la lista de NPC entera. Un record roto no puede costar la sesión: se saltea, se cuenta y se
        ' nombra, igual que el bulk parse de NPC_ de más arriba.
        Dim lvlnFailures As Integer = 0
        Dim lvlnFirstFailure As String = ""
        For Each rec In allLVLNRecords
            Dim lvln = NpcTemplateHelpers.TryAbrirLvlnTolerante(rec, _pluginManager)
            If lvln Is Nothing Then
                lvlnFailures += 1
                If lvlnFirstFailure = "" Then _
                    lvlnFirstFailure = $"{rec.SourcePluginName}:{rec.Header.FormID:X8}"
                Continue For
            End If
            _lvlnDataCache(lvln.FormID) = lvln

            For Each entry In lvln.LeveledListEntries
                If entry.LeveledListEntryNPC = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.LeveledListEntryNPC)
                If entryRec IsNot Nothing AndAlso entryRec.Header.Signature = "LVLN" Then
                    nestedLVLNFormIDs.Add(entry.LeveledListEntryNPC)
                End If
            Next
        Next

        If lvlnFailures > 0 Then
            Dim nL = lvlnFailures, firstL = lvlnFirstFailure
            Logger.LogLazy(Function() $"[LOAD] {nL} LVLN record(s) could not be parsed and were skipped " &
                                      $"(their leveled lists are absent from the NPC classification). First: {firstL}")
            ' Mismo motivo que el aviso de NPC_ (logger apagado en Release) y misma mecánica (hilo de fondo).
            _pendingLoadWarnings.Add(
                $"{nL} leveled NPC list(s) could not be parsed and were skipped — the NPCs they spawn are not " &
                "classified as encounter spawns." & vbCrLf & "First: " & firstL)
        End If

        ' Final LVLNs = all LVLNs that are NOT referenced as entries inside another LVLN
        For Each lvlnFormID In _lvlnDataCache.Keys
            If Not nestedLVLNFormIDs.Contains(lvlnFormID) Then
                _finalLVLNFormIDs.Add(lvlnFormID)
            End If
        Next
        _finalLVLNFormIDs.Sort(Function(a, b)
                                   Dim lvlnA = _lvlnDataCache(a)
                                   Dim lvlnB = _lvlnDataCache(b)
                                   Return String.Compare(lvlnA.EditorID, lvlnB.EditorID, StringComparison.OrdinalIgnoreCase)
                               End Function)

        ' Collect NPCs in leveled lists (encounter spawns)
        ' Se recorre `_lvlnDataCache`, NO `allLVLNRecords`: la caché ya excluye los LVLN que no parsearon.
        ' Recorrer la lista cruda volvía a pasar el MISMO record roto por ParseLVLN (GetRecord devuelve el mismo
        ' objeto, PluginManager.BuildTypeIndex sale de AllRecords), así que la excepción salía igual y el
        ' try/catch de arriba quedaba inerte: la carga entera seguía muriendo por un solo record.
        For Each lvlnFid In _lvlnDataCache.Keys.ToList()
            CollectNPCsFromLVLNRecursive(lvlnFid, _npcsInGameWorld, New HashSet(Of UInteger)())
        Next

        ' Warm _lvlnLeavesCache: pre-compute flattened NPC FormID list for every LVLN. Recursion
        ' memoizada via ComputeAndCacheLVLNLeaves — sub-LVLNs ya cacheadas se leen del cache, no
        ' se re-walkean. Costo total: O(total entries across all LVLNs). Una sola vez al startup;
        ' PopulateNPCTree luego sólo hace dictionary lookups O(1) por LVLN.
        For Each lvlnFid In _lvlnDataCache.Keys
            ComputeAndCacheLVLNLeaves(lvlnFid, New HashSet(Of UInteger)())
        Next

        ' Scan all NPCs to find which are used as template sources.
        ' El TPLT se lee UNA vez por NPC: leerlo tres veces son tres resoluciones de ruta para el
        ' mismo campo, por cada NPC del orden de carga.
        For Each npc In _allNPCs
            Dim tplt = npc.Record.Plantilla()
            If tplt <> 0UI Then
                Dim rec = _pluginManager.GetRecord(tplt)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(tplt)
                End If
            End If
            For Each actor In npc.Record.ActoresDePlantilla()
                Dim rec = _pluginManager.GetRecord(actor)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(actor)
                End If
            Next
        Next

        ' DIAGNOSTIC (LogLazy, one-shot per load): the NPCs Section 1 hides from the ESP-madre list
        ' because they inherit their appearance via a template (NpcInheritsVisualAppearance). Pick one
        ' of the "placed+inheriting" entries to open in CK and study how to "un-inherit" it — the basis
        ' for the deferred promote-inherited→own path. The summary also counts the whole in-game
        ' inheriting population (mostly LVLN encounters, which section 2 still shows). Guarded because
        ' the scan exists only to feed the log.
        If Logger.Enabled Then
            Dim inheritingInWorld = 0
            Dim placedInheriting = 0
            For Each kv In _ctx.NpcCache
                Dim npc = kv.Value
                If npc Is Nothing OrElse Not _npcsInGameWorld.Contains(npc.FormID) Then Continue For
                If Not NpcTemplateHelpers.NpcInheritsVisualAppearance(npc) Then Continue For
                inheritingInWorld += 1
                If _directlyPlacedNPCFormIDs.Contains(npc.FormID) Then
                    placedInheriting += 1
                    Dim n = npc
                    Logger.LogLazy(Function() $"[SECTION1-DISCARD] placed+inheriting (hidden from ESP-madre) " &
                                              $"FormID=0x{n.FormID:X8} '{NpcManagerFormat.DescribeNpc(n)}' plugin='{n.PluginName}' " &
                                              $"templateFlags=0x{n.Record.ConfigurationTemplateFlags:X4} " &
                                              $"useTraits={NpcTemplateHelpers.HasTemplateFlag(n.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits)} " &
                                              $"useModelAnim={NpcTemplateHelpers.HasTemplateFlag(n.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.ModelAnimation)} " &
                                              $"TPLT=0x{n.Record.Plantilla():X8}")
                End If
            Next
            Dim totalInWorld = inheritingInWorld
            Dim totalPlaced = placedInheriting
            Logger.LogLazy(Function() $"[SECTION1-DISCARD] summary: {totalPlaced} placed+inheriting (detailed above), " &
                                      $"{totalInWorld} in-game NPCs inherit appearance total (the rest are LVLN encounters, still listed under section 2)")
        End If

    End Sub

    Private Sub CollectNPCsFromLVLNRecursive(lvlnFormID As UInteger, result As HashSet(Of UInteger), visited As HashSet(Of UInteger))
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return
        Dim rec = _pluginManager.GetRecord(lvlnFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "LVLN" Then Return

        visited.Add(lvlnFormID)
        ' La caché es la fuente: la llenó el barrido de arranque, que ya saltea (y reporta) los LVLN que no
        ' parsean. Re-parsear acá reintroducía la excepción por la puerta de al lado — un LVLN anidado roto
        ' alcanzaba para tumbar la carga aunque el bucle de arriba lo hubiera salteado.
        Dim lvln As Canon.ILvln = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) OrElse lvln Is Nothing Then Return

        For Each entry In lvln.LeveledListEntries
            If entry.LeveledListEntryNPC = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.LeveledListEntryNPC)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    result.Add(entry.LeveledListEntryNPC)
                Case "LVLN"
                    CollectNPCsFromLVLNRecursive(entry.LeveledListEntryNPC, result, visited)
            End Select
        Next
    End Sub

    ''' <summary>True if this NPC is only used as a template source and never placed in the world or in any LVLN.</summary>
    Private Function IsTemplateOnly(npc As NPC_Data) As Boolean
        If npc Is Nothing Then Return False
        Return _npcsUsedAsTemplates.Contains(npc.FormID) AndAlso Not _npcsInGameWorld.Contains(npc.FormID)
    End Function

    ''' <summary>True if the NPC matches the currently-enabled Section 1 category filters.
    ''' Additive union: the NPC shows if it matches ANY ticked category. The four flags are read
    ''' once by the caller (PopulateNPCTree), not per-NPC. Categories:
    ''' Unique faces = in-world AND own appearance; Generic = in-world AND inherits appearance;
    ''' Template bases = used as a TPLT/TPTA source; Unused = not in-world and not a template source.
    ''' CharGen face presets (ACBS bit 0x04) land under Unused too — NOT excluded: excluding them hides
    ''' vanilla's preset sets (30 FO4 / 200 SSE, measured) but also any mod whose plugin only ships
    ''' chargen presets, which would then list zero editable NPCs.</summary>
    Private Function NpcMatchesCategoryFilter(n As NPC_Data, showUnique As Boolean, showGeneric As Boolean,
                                              showTemplate As Boolean, showUnused As Boolean) As Boolean
        Dim inWorld = _npcsInGameWorld.Contains(n.FormID)
        Dim ownFace = Not HeredaApariencia(n)
        If showUnique AndAlso inWorld AndAlso ownFace Then Return True
        If showGeneric AndAlso inWorld AndAlso Not ownFace Then Return True
        If showTemplate AndAlso _npcsUsedAsTemplates.Contains(n.FormID) Then Return True
        If showUnused AndAlso Not inWorld AndAlso Not _npcsUsedAsTemplates.Contains(n.FormID) Then Return True
        Return False
    End Function

    ''' <summary>Check if this NPC has any LVLN in its direct TPLT or TPTA references.
    ''' These NPCs produce different results each time they're resolved (different face, gender, etc).</summary>
    Private Function NpcHasLeveledTemplates(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.Record.ConfigurationTemplateFlags = 0US Then Return False

        ' Check TPLT
        If npc.Record.Plantilla() <> 0UI Then
            Dim rec = _pluginManager.GetRecord(npc.Record.Plantilla())
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        End If

        ' Check TPTA entries
        For Each actor In npc.Record.ActoresDePlantilla()
            Dim rec = _pluginManager.GetRecord(actor)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        Next

        Return False
    End Function

    ''' <summary>Ancho (en dígitos) del prefijo de orden de carga de los nodos raíz: el que pida la posición
    ''' más alta del load order cargado, con piso 3. Con 300 plugins da [000]..[299]; pasando los 1001 pasa a
    ''' [0000] (los ESL cuentan en el mismo cupo, así que es alcanzable), y si después bajan, vuelve a 3.
    ''' <para>Que sea variable NO puede mezclar anchos en pantalla, y está VERIFICADO, no asumido: dentro de
    ''' UNA sola pasada de <see cref="PopulateNPCTree"/> hay exactamente un <c>modelo.Limpiar()</c>, dos
    ''' <c>modelo.AgregarRaiz</c> (Sección 1 y Sección 2) y dos asignaciones a <c>.Texto</c> de un nodo raíz —
    ''' los cinco dentro de esa misma pasada, que arranca borrando el árbol entero. No hay camino incremental
    ''' que agregue o reetiquete un nodo de plugin sin repoblar todo, así que el árbol se repinta ENTERO con
    ''' el ancho de esta lectura: o todo en 3 dígitos o todo en 4, nunca mezclado.</para>
    ''' <para>Por eso se lee UNA vez acá y viaja como parámetro hasta <see cref="LoadOrderPrefix"/>: si cada
    ''' nodo lo recalculara, un save que monta un plugin nuevo y cruza el umbral EN MEDIO del repoblado sí
    ''' dejaría media lista con un ancho y media con otro.</para></summary>
    Private Function LoadOrderTagWidth() As Integer
        Dim n = If(_pluginManager Is Nothing, 0, _pluginManager.LoadedPluginCount)
        Return Math.Max(3, Math.Max(0, n - 1).ToString().Length)
    End Function

    ''' <summary>Clave de orden del árbol: la posición EFECTIVA del plugin (la del merge real, no la línea del
    ''' Plugins.txt). Los que no están cargados —el grupo "Unknown", o uno excluido por masters faltantes—
    ''' devuelven <see cref="Integer.MaxValue"/> para caer al final en vez de encabezar la lista.</summary>
    Private Function LoadOrderSortKey(pluginName As String) As Integer
        Dim pos = If(_pluginManager Is Nothing, -1, _pluginManager.GetLoadOrderPosition(pluginName))
        Return If(pos < 0, Integer.MaxValue, pos)
    End Function


    ''' <summary>Prefijo "[00042] " de una fila de plugin, con <paramref name="width"/> dígitos y cero a
    ''' la izquierda. Plugin no cargado → "[?????] " del MISMO ancho, así la alineación no se rompe.
    ''' <para>Es SÓLO texto de la fila. El nombre del plugin se sigue leyendo de la CLAVE de la fila
    ''' ("PLUGIN_&lt;nombre&gt;"), nunca del texto — ver <see cref="SelectedPluginForFomodExport"/>.</para></summary>
    Private Function LoadOrderPrefix(pluginName As String, width As Integer) As String
        Dim pos = If(_pluginManager Is Nothing, -1, _pluginManager.GetLoadOrderPosition(pluginName))
        If pos < 0 Then Return "[" & New String("?"c, width) & "] "
        Return "[" & pos.ToString().PadLeft(width, "0"c) & "] "
    End Function

    ''' <summary>Rehace el árbol de NPC según el filtro y las casillas de categoría.
    '''
    ''' <para>Arma un MODELO (<see cref="ModeloDeArbol"/>) y no nodos de Win32. Es todo el cambio de costo:
    ''' un <c>TreeNode</c> es un ítem del control con su handle —7.000 de ellos costaban ~1.960 ms entre
    ''' alta y baja, o sea eso en CADA tecla del buscador— mientras que una <see cref="FilaDeArbol"/> es un
    ''' objeto chico. El control materializa sólo las ~30 filas que se ven.</para>
    '''
    ''' <para>Lo que se muestra NO cambia: mismas dos secciones, mismo orden, mismas cuentas en las
    ''' cabeceras y la misma regla de expansión —los grupos se abren sólo cuando hay filtro o "sólo
    ''' cambiados", así que al arrancar se ve el nivel de plugin y nada más—. Con una lista virtual
    ''' expandir es gratis, así que esa regla se conserva por fidelidad y no por costo.</para></summary>
    Private Sub PopulateNPCTree(Optional filter As String = "")
        If InvokeRequired Then
            Invoke(Sub() PopulateNPCTree(filter))
            Return
        End If
        If IsNothing(_ctx) Then Return

        If (_ctx.NpcCache Is Nothing OrElse _ctx.NpcCache.Count = 0) AndAlso _allNPCs.Count > 0 Then
            RebuildTreeModelCache()
        End If

        ' NO-OP PATH. NpcFilterQuery.Parse hands back the input VERBATIM as FreeText when the text
        ' carries no `facet:value` token, so `normalizedFilter` here is byte-identical to the old
        ' `If(filter, "").Trim()` and every downstream comparison is the one that always ran. Only a
        ' query with facets builds `advIndex` — and only then does anything read a referenced record.
        ' El gate de esa afirmación es Tools/NpcFilterGate; tiene que seguir en verde.
        Dim query = NpcFilterQuery.Parse(filter)
        Dim normalizedFilter = query.FreeText.Trim()
        Dim advTerms = query.Terms
        Dim advIndex As NpcFilterIndex = If(advTerms.Length > 0, EnsureFilterIndex(), Nothing)
        If advIndex IsNot Nothing Then advIndex.FollowTemplates = query.FollowTemplates

        Dim filterActive As Boolean = normalizedFilter.Length > 0 OrElse advTerms.Length > 0
        Dim onlyChanged As Boolean = CheckBoxOnlyChanged IsNot Nothing AndAlso CheckBoxOnlyChanged.Checked
        Dim showUnique As Boolean = CheckBoxCatUnique Is Nothing OrElse CheckBoxCatUnique.Checked
        Dim showGeneric As Boolean = CheckBoxCatGeneric IsNot Nothing AndAlso CheckBoxCatGeneric.Checked
        Dim showTemplate As Boolean = CheckBoxCatTemplate IsNot Nothing AndAlso CheckBoxCatTemplate.Checked
        Dim showUnused As Boolean = CheckBoxCatUnused IsNot Nothing AndAlso CheckBoxCatUnused.Checked

        Dim loadOrderWidth As Integer = LoadOrderTagWidth()
        ' Los grupos se abren solos SÓLO con filtro o con "sólo cambiados", igual que antes: al arrancar
        ' se ve el nivel de plugin y los NPC quedan adentro.
        Dim abrirGrupos As Boolean = filterActive OrElse onlyChanged

        Dim swArbol = System.Diagnostics.Stopwatch.StartNew()
        Dim modelo = TreeViewNPCs.Modelo
        ' ⛔ QUÉ GRUPOS ESTABAN ABIERTOS, ANTES DE DESTRUIRLOS. `Limpiar()` se lleva las filas y con
        ' ellas su `Expandida`; sin esto, repoblar CIERRA el árbol entero. Antes casi no se notaba
        ' porque repoblar era raro; ahora el editor y el Save repueblan siempre, y el usuario perdía
        ' toda su navegación en cada OK.
        ' ⛔ SE RECORRE EL ÁRBOL, NO LO VISIBLE. `AplanarDesde` (NpcTreeModel.vb:148-154) hace
        ' `If Not fila.Expandida Then Return` ANTES de bajar, así que un nodo expandido DENTRO de un
        ' ancestro colapsado no está en `Visibles` y su estado se perdía en cada repoblado sin filtro
        ' (con filtro no se nota: `abrirGrupos` los abre igual). MEDIDO: un LVLN abierto dentro de un
        ' grupo de plugin cerrado salía VACÍO de acá. Es el mismo motivo por el que `ModeloDeArbol.Indexar`
        ' recorre el árbol entero: el índice describe el ÁRBOL, la expansión sólo decide qué se dibuja.
        ' Costo medido del recorrido completo: 0,137 ms sobre 7.260 filas.
        Dim abiertosAntes As New HashSet(Of String)(StringComparer.Ordinal)
        RecolectarExpandidas(modelo.Raices, abiertosAntes)
        modelo.Limpiar()

        ' === Sección 1: NPC agrupados por plugin ===
        ' Qué NPC entran lo deciden las casillas de categoría (Unique / Generic / Template bases /
        ' Unused), en unión aditiva: un NPC aparece si cae en ALGUNA de las tildadas. El default —sólo
        ' "Unique faces"— reproduce el comportamiento previo. Un NPC con apariencia propia alcanzable
        ' desde un LVLN aparece EN LAS DOS secciones, que es la regla del producto.
        Dim pluginSectionNpcs = _allNPCs.
            Where(Function(n) NpcMatchesCategoryFilter(n, showUnique, showGeneric, showTemplate, showUnused) AndAlso
                               (Not onlyChanged OrElse _dirtyNpcs.Contains(n.FormID)) AndAlso
                               (normalizedFilter.Length = 0 OrElse MatchesNpcFilter(n, Nothing, normalizedFilter)) AndAlso
                               (advIndex Is Nothing OrElse advIndex.MatchesAll(n, advTerms))).
            GroupBy(Function(n) If(n.PluginName, "Unknown")).
            OrderBy(Function(g) LoadOrderSortKey(g.Key)).
            ThenBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

        For Each pluginGroup In pluginSectionNpcs
            Dim grupo As FilaDeArbol = Nothing
            Dim matchCount = 0

            ' SIN OrderBy: `_allNPCs` ya viene ordenado por `ClaveDeOrden` (ver OrdenarNpcs) y `GroupBy`
            ' conserva el orden de la fuente dentro de cada grupo.
            For Each npc In pluginGroup
                If grupo Is Nothing Then
                    grupo = modelo.AgregarRaiz(New FilaDeArbol(TipoDeFila.GrupoDePlugin,
                                                               $"PLUGIN_{pluginGroup.Key}",
                                                               pluginGroup.Key, 0, Nothing))
                End If
                grupo.Agregar(New FilaDeArbol(TipoDeFila.Npc, $"NPC_{npc.FormID:X8}",
                                              EtiquetaDeNpc(npc), 1, npc))
                matchCount += 1
            Next

            If grupo IsNot Nothing Then
                grupo.Texto = $"{LoadOrderPrefix(pluginGroup.Key, loadOrderWidth)}{pluginGroup.Key} ({matchCount})"
                grupo.Expandida = abrirGrupos OrElse abiertosAntes.Contains(grupo.Clave)
            End If
        Next

        ' === Sección 2: leveled lists finales (encuentros) ===
        ' Cada LVLN cuelga con sus NPC hoja como hijos (recursión aplanada vía CollectLVLNLeafNpcIds).
        ' SIN dedup: un NPC puede aparecer bajo CADA LVLN que lo lista — regla del usuario, sirve para
        ' ver qué LVLNs enrolan al mismo NPC.
        If _finalLVLNFormIDs.Count > 0 Then
            Dim value As Canon.ILvln = Nothing
            Dim visibleLvlns As New List(Of (FormID As UInteger,
                                             Record As PluginRecord,
                                             Data As Canon.ILvln,
                                             VisibleLeaves As List(Of NPC_Data)))

            For Each fid In _finalLVLNFormIDs
                Dim rec = _pluginManager.GetRecord(fid)
                Dim lvln = If(_lvlnDataCache.TryGetValue(fid, value), value, Nothing)
                If rec Is Nothing OrElse lvln Is Nothing Then Continue For

                Dim visibleLeaves As List(Of NPC_Data) = Nothing
                If onlyChanged OrElse filterActive Then
                    visibleLeaves = New List(Of NPC_Data)
                    Dim leaves As List(Of UInteger) = Nothing
                    If Not _lvlnLeavesCache.TryGetValue(fid, leaves) Then leaves = New List(Of UInteger)

                    For Each leafFid In leaves
                        Dim leafNpc As NPC_Data = Nothing
                        If Not _ctx.NpcCache.TryGetValue(leafFid, leafNpc) Then Continue For
                        If onlyChanged AndAlso Not _dirtyNpcs.Contains(leafFid) Then Continue For
                        If normalizedFilter.Length > 0 AndAlso Not MatchesNpcFilter(leafNpc, Nothing, normalizedFilter) Then Continue For
                        If advIndex IsNot Nothing AndAlso Not advIndex.MatchesAll(leafNpc, advTerms) Then Continue For
                        visibleLeaves.Add(leafNpc)
                    Next

                    If onlyChanged Then
                        If visibleLeaves.Count = 0 Then Continue For
                    ElseIf filterActive AndAlso visibleLeaves.Count = 0 Then
                        ' Un LVLN no tiene head parts / skin / outfit propios, así que NUNCA puede
                        ' satisfacer un término de faceta: con el filtro avanzado sobrevive sólo por un
                        ' hijo que matchee. El texto libre solo conserva el comportamiento viejo (la
                        ' lista misma puede matchear por EditorID / FormID / plugin).
                        If advTerms.Length > 0 Then Continue For
                        If Not NpcDisplayHelpers.MatchesRecordFilter(rec, normalizedFilter) Then Continue For
                    End If
                End If

                visibleLvlns.Add((fid, rec, lvln, visibleLeaves))
            Next

            Dim lvlnsByPlugin = visibleLvlns.
                GroupBy(Function(x) If(x.Record.SourcePluginName, "Unknown")).
                OrderBy(Function(g) LoadOrderSortKey(g.Key)).
                ThenBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

            For Each pluginGroup In lvlnsByPlugin
                Dim grupo As FilaDeArbol = Nothing
                Dim matchCount = 0

                For Each item In pluginGroup.OrderBy(Function(x) x.Data.EditorID, StringComparer.OrdinalIgnoreCase)
                    If grupo Is Nothing Then
                        grupo = modelo.AgregarRaiz(New FilaDeArbol(TipoDeFila.GrupoDeLvlnPorPlugin,
                                                                   $"LVLN_PLUGIN_{pluginGroup.Key}",
                                                                   pluginGroup.Key, 0, Nothing))
                    End If

                    Dim label = If(item.Data.EditorID <> "", item.Data.EditorID, item.FormID.ToString("X8"))
                    Dim filaLvln = grupo.Agregar(New FilaDeArbol(TipoDeFila.Lvln, $"LVLN_{item.FormID:X8}",
                                                                 label, 1, item.Data))

                    Dim childMatchCount = 0
                    If item.VisibleLeaves IsNot Nothing Then
                        For Each leafNpc In item.VisibleLeaves
                            filaLvln.Agregar(New FilaDeArbol(TipoDeFila.NpcDeLvln, $"NPC_{leafNpc.FormID:X8}",
                                                             EtiquetaDeNpc(leafNpc), 2, leafNpc))
                            childMatchCount += 1
                        Next
                    Else
                        Dim leaves As List(Of UInteger) = Nothing
                        If Not _lvlnLeavesCache.TryGetValue(item.FormID, leaves) Then leaves = New List(Of UInteger)
                        For Each leafFid In leaves
                            Dim leafNpc As NPC_Data = Nothing
                            If Not _ctx.NpcCache.TryGetValue(leafFid, leafNpc) Then Continue For
                            filaLvln.Agregar(New FilaDeArbol(TipoDeFila.NpcDeLvln, $"NPC_{leafNpc.FormID:X8}",
                                                             EtiquetaDeNpc(leafNpc), 2, leafNpc))
                            childMatchCount += 1
                        Next
                    End If

                    filaLvln.Expandida = (childMatchCount > 0 AndAlso abrirGrupos) OrElse abiertosAntes.Contains(filaLvln.Clave)
                    matchCount += 1
                Next

                If grupo IsNot Nothing AndAlso grupo.Hijos.Count > 0 Then
                    grupo.Texto = $"{LoadOrderPrefix(pluginGroup.Key, loadOrderWidth)}[LVLN] {pluginGroup.Key} ({matchCount})"
                    grupo.Expandida = abrirGrupos OrElse abiertosAntes.Contains(grupo.Clave)
                End If
            Next
        End If

        TreeViewNPCs.Refrescar()
        swArbol.Stop()
        _msUltimoRepoblado = swArbol.ElapsedMilliseconds

        ' Sólo cuando el repoblado viene de un FILTRO: en la carga inicial el que manda el estado es
        ' LoadDataAsync, con el desglose completo, y pisarlo acá lo borraría.
        If abrirGrupos Then
            SetStatus($"Filter — {modelo.Raices.Count} plugin group(s), {modelo.Visibles.Count} row(s) in {_msUltimoRepoblado} ms")
        End If
    End Sub

    ''' <summary>Las claves de las filas EXPANDIDAS de TODO el árbol, mire o no la expansión de sus
    ''' ancestros. Espejo de <c>ModeloDeArbol.Indexar</c>, que ya lo recorre entero por el mismo motivo:
    ''' el índice describe el ÁRBOL, la expansión sólo decide qué se dibuja.
    ''' <para>Las filas de NPC son hojas (<c>Alternar</c> exige <c>TieneHijos</c>), así que sólo entran
    ''' claves de grupo y de LVLN, que son únicas. El mismo NPC cuelga de su plugin y de cada LVLN como
    ''' objetos DISTINTOS, pero lo que se repite es la clave y el destino es un HashSet: idempotente.
    ''' Tres niveles como máximo y sin ciclos.</para></summary>
    Private Shared Sub RecolectarExpandidas(filas As IEnumerable(Of FilaDeArbol), destino As HashSet(Of String))
        If filas Is Nothing Then Return
        For Each f In filas
            If f Is Nothing Then Continue For
            If f.Expandida AndAlso f.Clave IsNot Nothing Then destino.Add(f.Clave)
            If f.Hijos.Count > 0 Then RecolectarExpandidas(f.Hijos, destino)
        Next
    End Sub

    ''' <summary>La etiqueta de un NPC, del caché; si no está, se arma Y SE GUARDA. Sin guardarla, un NPC
    ''' que no quedó en el caché se re-arma la etiqueta en cada repoblado —o sea en cada tecla— para
    ''' siempre. Un solo sitio: los tres lugares que crean filas de NPC llaman acá en vez de armar la
    ''' etiqueta cada uno por su cuenta.</summary>
    Private Function EtiquetaDeNpc(npc As NPC_Data) As String
        Dim etiqueta As String = Nothing
        If _npcDisplayLabelCache.TryGetValue(npc.FormID, etiqueta) Then Return etiqueta
        etiqueta = NpcDisplayHelpers.BuildNpcDisplayLabel(npc)
        _npcDisplayLabelCache(npc.FormID) = etiqueta
        Return etiqueta
    End Function


    ''' <summary>Compute (memoized) la lista flattened de NPC FormIDs alcanzables desde un LVLN.
    ''' Recurse en sub-LVLNs vía la misma función (memoización mutua). El cache global
    ''' <see cref="_lvlnLeavesCache"/> guarda el resultado por FormID, así una sola pasada en
    ''' BuildNPCClassification cubre todos los LVLNs y los rebuilds del tree (filter / save)
    ''' luego sólo hacen lookup O(1) sin volver a tocar el plugin manager.
    '''
    ''' Cycle detection: `inProgress` se pasa por la cadena de recursión activa. Si A→B→A, la
    ''' segunda visita a A retorna lista vacía y el cache de B captura la parte de B sin
    ''' contribución cíclica. Vanilla FO4 no tiene ciclos LVLN; el guard es defensivo.</summary>
    Private Function ComputeAndCacheLVLNLeaves(lvlnFormID As UInteger, inProgress As HashSet(Of UInteger)) As List(Of UInteger)
        Dim cached As List(Of UInteger) = Nothing
        If _lvlnLeavesCache.TryGetValue(lvlnFormID, cached) Then Return cached
        If lvlnFormID = 0UI OrElse inProgress.Contains(lvlnFormID) Then Return New List(Of UInteger)()
        inProgress.Add(lvlnFormID)
        Dim result As New List(Of UInteger)
        Dim lvln As Canon.ILvln = Nothing
        If _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            For Each entry In lvln.LeveledListEntries
                If entry.LeveledListEntryNPC = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.LeveledListEntryNPC)
                If entryRec Is Nothing Then Continue For
                Select Case entryRec.Header.Signature
                    Case "NPC_"
                        result.Add(entry.LeveledListEntryNPC)
                    Case "LVLN"
                        result.AddRange(ComputeAndCacheLVLNLeaves(entry.LeveledListEntryNPC, inProgress))
                End Select
            Next
        End If
        inProgress.Remove(lvlnFormID)
        _lvlnLeavesCache(lvlnFormID) = result
        Return result
    End Function

    Private Function BuildTemplateDependencyMap(npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As Dictionary(Of UInteger, List(Of TemplateDependencyEdge))
        Dim dependencyMap As New Dictionary(Of UInteger, List(Of TemplateDependencyEdge))

        For Each npc In npcById.Values
            Dim groupedEdges As New Dictionary(Of UInteger, TemplateDependencyEdge)

            For Each dependency In GetTemplateDependencies(npc)
                If Not IsSupportedTemplateSource(dependency.Key, npcById) Then Continue For

                Dim edge As TemplateDependencyEdge = Nothing
                If Not groupedEdges.TryGetValue(dependency.Key, edge) Then
                    edge = New TemplateDependencyEdge With {
                        .SourceFormID = dependency.Key,
                        .DependentNpc = npc
                    }
                    groupedEdges(dependency.Key) = edge
                End If

                If Not edge.Categories.Contains(dependency.Value) Then
                    edge.Categories.Add(dependency.Value)
                End If
            Next

            For Each edge In groupedEdges.Values
                Dim edges As List(Of TemplateDependencyEdge) = Nothing
                If Not dependencyMap.TryGetValue(edge.SourceFormID, edges) Then
                    edges = New List(Of TemplateDependencyEdge)()
                    dependencyMap(edge.SourceFormID) = edges
                End If
                edges.Add(edge)
            Next
        Next

        For Each edges In dependencyMap.Values
            edges.Sort(Function(left, right)
                           Dim compare = StringComparer.OrdinalIgnoreCase.Compare(NpcDisplayHelpers.GetNpcNodeDisplayText(left.DependentNpc, left), NpcDisplayHelpers.GetNpcNodeDisplayText(right.DependentNpc, right))
                           If compare <> 0 Then Return compare
                           Return left.DependentNpc.FormID.CompareTo(right.DependentNpc.FormID)
                       End Function)
        Next

        Return dependencyMap
    End Function

    Private Function BuildTemplateTreeRootSourceIds(npcById As IReadOnlyDictionary(Of UInteger, NPC_Data), dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge))) As List(Of UInteger)
        Dim rootIds As New HashSet(Of UInteger)()
        Dim dependentNpcIds As New HashSet(Of UInteger)()

        For Each edges In dependencyMap.Values
            For Each edge In edges
                dependentNpcIds.Add(edge.DependentNpc.FormID)
            Next
        Next

        For Each sourceId In dependencyMap.Keys
            If Not dependentNpcIds.Contains(sourceId) AndAlso IsSupportedTemplateSource(sourceId, npcById) Then
                rootIds.Add(sourceId)
            End If
        Next

        For Each npc In npcById.Values
            If Not GetTemplateDependencies(npc).Any(Function(dep) IsSupportedTemplateSource(dep.Key, npcById)) Then
                rootIds.Add(npc.FormID)
            End If
        Next

        Dim reachableNpcIds = CollectReachableNpcIds(rootIds, dependencyMap, npcById)
        For Each npcId In npcById.Keys
            If Not reachableNpcIds.Contains(npcId) Then
                rootIds.Add(npcId)
            End If
        Next

        Return rootIds.ToList()
    End Function

    Private Function CollectReachableNpcIds(rootSourceIds As IEnumerable(Of UInteger), dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)), npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As HashSet(Of UInteger)
        Dim reachableNpcIds As New HashSet(Of UInteger)()
        Dim visitedSources As New HashSet(Of UInteger)()

        For Each sourceId In rootSourceIds
            CollectReachableNpcIds(sourceId, dependencyMap, npcById, reachableNpcIds, visitedSources)
        Next

        Return reachableNpcIds
    End Function

    Private Sub CollectReachableNpcIds(sourceId As UInteger,
                                       dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)),
                                       npcById As IReadOnlyDictionary(Of UInteger, NPC_Data),
                                       reachableNpcIds As HashSet(Of UInteger),
                                       visitedSources As HashSet(Of UInteger))
        If visitedSources.Contains(sourceId) Then Return
        visitedSources.Add(sourceId)

        If npcById.ContainsKey(sourceId) Then
            reachableNpcIds.Add(sourceId)
        End If

        Dim edges As List(Of TemplateDependencyEdge) = Nothing
        If Not dependencyMap.TryGetValue(sourceId, edges) Then Return

        For Each edge In edges
            reachableNpcIds.Add(edge.DependentNpc.FormID)
            CollectReachableNpcIds(edge.DependentNpc.FormID, dependencyMap, npcById, reachableNpcIds, visitedSources)
        Next
    End Sub

    Private Function GetTemplateDependencies(npc As NPC_Data) As List(Of KeyValuePair(Of UInteger, String))
        Dim dependencies As New List(Of KeyValuePair(Of UInteger, String))
        If npc Is Nothing Then Return dependencies

        ' Las banderas se leen UNA vez. Leerlas dentro del bucle son trece resoluciones de ruta por
        ' NPC para el mismo campo, y esto corre por cada NPC del orden de carga.
        Dim flags = npc.Record.ConfigurationTemplateFlags
        For Each category As NPC_TemplateCategory In Canon.CanonInterpretacion.CategoriasDePlantilla
            If Not NpcTemplateHelpers.HasTemplateFlag(flags, category) Then Continue For

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, category)
            If sourceFormID = 0UI Then Continue For

            dependencies.Add(New KeyValuePair(Of UInteger, String)(sourceFormID, NpcManagerFormat.GetTemplateCategoryLabel(category)))
        Next

        Return dependencies
    End Function

    ' ⛔ Acá vivía `BuildTemplateTreeNode`: un árbol de dependencias de template que NADIE llamaba
    ' —su único llamador era ella misma— y que además estaba roto de tres formas a la vez: armaba un
    ' `TreeNode` que nunca usaba, terminaba en un `Return Nothing` incondicional (así que la recursión
    ' siempre recibía Nothing y la lista de hijos quedaba vacía SIEMPRE), y remataba con
    ' `CType(Nothing, TreeNode).Nodes.Add(...)`, que es una NRE segura y sólo era inalcanzable por esa
    ' casualidad. Encima usaba la API de `TreeNode`, que el árbol virtual (`551df68`) reemplazó: revivirla
    ' habría sido reintroducir el control viejo. Borrada el 2026-08-22.


    Private Function IsSupportedTemplateSource(sourceFormID As UInteger, npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As Boolean
        If sourceFormID = 0UI Then Return False
        If npcById.ContainsKey(sourceFormID) Then Return True

        Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
        Return sourceRec IsNot Nothing AndAlso sourceRec.Header.Signature = "LVLN"
    End Function

    Private Function MatchesNpcFilter(npc As NPC_Data, dependencyEdge As TemplateDependencyEdge, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) Then Return True
        If npc Is Nothing Then Return False

        ' Fast path: searchable text pre-built en BuildNPCClassification (lowercase concat de los
        ' 5 campos base separados por '|'). Match con un solo IndexOf. dependencyEdge sólo
        ' aplicaba al árbol de dependencias de template, que ya no existe; para la lista plana de NPC
        ' no cambia nada.
        Dim cached As String = Nothing
        If _npcSearchableCache.TryGetValue(npc.FormID, cached) Then
            If cached.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        Else
            ' Fallback para NPCs no incluidos en el cache (raro — debería estar todo)
            Dim fallback = NpcDisplayHelpers.BuildNpcSearchableText(npc)
            If fallback.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        End If
        ' Categorías del template dependency edge no entran al cache (depende del contexto del
        ' template tree, no del NPC). Si hay edge, evaluamos esa sola string adicional.
        If dependencyEdge IsNot Nothing AndAlso dependencyEdge.Categories.Count > 0 Then
            Dim cats = String.Join(" ", dependencyEdge.Categories)
            If cats.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        End If
        Return False
    End Function


    ' ⛔ `GetTemplateSourceDisplayText` se fue con `BuildTemplateTreeNode`, que era su único llamador.
    ' Lo que sí sigue vivo es `NpcManagerFormat.DescribeRecord`, que es donde vive esa ley.

    ''' <summary>Friend so EditFace_Form / EditBody_Form can read the resolved render state
    ''' (e.g. <see cref="NpcRenderHost.LastFaceTriMorphNames"/>) directly when constructing the
    ''' editor UI from MainForm's last completed render — avoids the order-of-operations issue
    ''' where the editor's own _editorHost isn't populated until its Shown handler runs.</summary>
    Friend _renderHost As NpcRenderHost = Nothing
    ' Friend (not Private) because OutfitComboEntry.SlotKind is exposed via NpcRenderHost.OutfitEntries.
    Friend Enum OutfitSlotKind
        DefaultOutfit
        SleepOutfit
        NoOutfit
    End Enum

    ''' <summary>One entry of the outfit combo. With the new canonical model, entries enumerate
    ''' <c>(branch, slot_kind)</c> — today one per (DOFT?, SOFT?) of the current base state. A
    ''' sampled realization of ARMO FormIDs is cached per entry; Reroll re-samples via the library.
    ''' Friend (not Private) so <see cref="NpcRenderHost.OutfitEntries"/> can hold the per-host set.</summary>
    Friend Class OutfitComboEntry
        Public Label As String
        Public SlotKind As OutfitSlotKind
        Public OutfitFormID As UInteger
        Public SampledArmorFormIDs As New List(Of UInteger)
        ''' <summary>Per-ARMO contextual keywords inherited from the LLKC chain at sample time.
        ''' Key = ARMO FormID, value = list of KYWD FormIDs that the LVLI sequence accumulated
        ''' along the path. Used by CollectArmoCandidates to match OBTS combinations and apply
        ''' OMOD AddonIndex Property swaps (Lite/Mid/Heavy).</summary>
        Public SampledArmorContextKeywords As New Dictionary(Of UInteger, List(Of UInteger))
    End Class

    Private _currentOutfitEntries As New List(Of OutfitComboEntry)
    Private _suppressOutfitComboEvent As Boolean = False

    ''' <summary>Estilo de una fila: la MISMA ley de colores y fuentes de siempre —gris si el NPC sólo
    ''' sirve de plantilla, azul si es una leveled list, negrita si tiene cambios sin guardar, tachado y
    ''' rojo si está marcado para borrar, y el resaltado suave de la multi-selección—.
    '''
    ''' <para>Los colores salen de <see cref="ColoresDelArbol"/>, o sea de <c>SystemColors</c>: un color
    ''' escrito a mano pierde el contraste apenas el usuario cambia a tema oscuro o a alto contraste.</para>
    '''
    ''' <para>El FONDO de la selección del sistema lo pinta el control (con el par
    ''' Highlight/HighlightText); acá sólo se pide el resaltado PROPIO de la multi-selección, que es un
    ''' concepto distinto —los NPC elegidos para actuar en lote— y por eso tiene su tono.</para></summary>
    Private Sub TreeViewNPCs_PintarFila(sender As Object, e As PintarFilaEventArgs) Handles TreeViewNPCs.PintarFila
        Dim npc = e.Fila.Npc
        Dim esLvln = (e.Fila.Tipo = TipoDeFila.Lvln)

        e.Estilo.Texto = ColoresDelArbol.Texto
        If npc IsNot Nothing AndAlso IsTemplateOnly(npc) Then
            e.Estilo.Texto = ColoresDelArbol.Apagado
        ElseIf esLvln Then
            e.Estilo.Texto = ColoresDelArbol.Acento
        End If

        ' Con cambios sin guardar va en negrita. La fuente se deriva una vez de la del control.
        Dim fuente = TreeViewNPCs.Font
        If npc IsNot Nothing AndAlso _dirtyNpcs.Contains(npc.FormID) Then
            If _dirtyNodeFont Is Nothing Then _dirtyNodeFont = New Font(TreeViewNPCs.Font, FontStyle.Bold)
            fuente = _dirtyNodeFont
        End If

        ' Marcado para borrar: tachado y rojo, y le gana a la negrita — un record que está por
        ' desaparecer del plugin no puede leerse además como "edición pendiente".
        If npc IsNot Nothing AndAlso _recordsToRemove.Contains(npc.FormID) Then
            If _deleteNodeFont Is Nothing Then _deleteNodeFont = New Font(TreeViewNPCs.Font, FontStyle.Strikeout)
            fuente = _deleteNodeFont
            e.Estilo.Texto = ColoresDelArbol.Peligro
        End If
        e.Estilo.Fuente = fuente

        ' ⛔ EL RESALTADO FUERTE ES DEL QUE SE ESTÁ VIENDO EN EL VISOR, no del enfocado. Con una
        ' multi-selección el visor muestra un sorteado (`_currentRandomPickFormID`) que NO es la fila
        ' enfocada; con selección simple los dos son el mismo. Es la ley que declara la doc de ese campo
        ' —"painted with the full highlight; the rest of the set gets a paler one so the user can see
        ' which member was rolled"— y que hasta ahora no se cumplía.
        ' Se pide el PAR del sistema, no un color: los colores del sistema vienen de a dos y el control
        ' los aplica juntos. Ver `EstiloDeFila.ResaltadoDelSistema`.
        ' Como el resaltado va por FormID, las DOS copias del mismo NPC —la de su plugin y la de cada
        ' LVLN que lo lista— se marcan juntas, que es la misma semántica que ya tiene `_selectedNpcFormIDs`
        ' ("an NPC that appears under several nodes highlights everywhere at once").
        If npc IsNot Nothing AndAlso _currentRandomPickFormID <> 0UI Then
            e.Estilo.ResaltadoDelSistema = (npc.FormID = _currentRandomPickFormID)
        End If
        ' Y el resto del lote, el tono suave.
        ' ⛔ ANTES ESTA RAMA ERA INALCANZABLE. La condición era
        '   `enElConjunto AndAlso Not esElRenderizado AndAlso Not e.SeleccionadaPorElSistema`,
        ' y `SeleccionadaPorElSistema` traía el LOTE y no el foco —el mismo conjunto del que sale
        ' `_selectedNpcFormIDs`, MainForm.vb:5036-5039—, así que el primer término implicaba el tercero y
        ' el `And` daba False SIEMPRE. `SeleccionSuave` no se pintó nunca y los N NPC de un lote salían
        ' los N en azul pleno, sin forma de ver cuál estaba en el visor.
        If e.EnElLote AndAlso Not e.Estilo.ResaltadoDelSistema Then
            e.Estilo.Fondo = ColoresDelArbol.SeleccionSuave
        End If
    End Sub

    ''' <summary>El foco del árbol se movió: refresca el panel de detalles. NO toca el conjunto
    ''' seleccionado — de eso se ocupa <see cref="TreeViewNPCs_SeleccionCambiada"/>, y separarlos es lo que
    ''' evita que se peleen (el orden entre "cambió el foco" y "cambió la selección" no está garantizado).</summary>
    Private Sub TreeViewNPCs_FilaEnfocada(sender As Object, e As FilaEventArgs) Handles TreeViewNPCs.FilaEnfocada
        Dim npc = e.Fila?.Npc
        PopulateRecordDetails(npc)
        ' Se anota qué NPC muestra el panel para que el render con retardo (RenderFromCurrentSelection,
        ' ~180 ms después) pueda saltearse rearmar el mismo árbol de detalles en el caso común de
        ' selección simple — hoy se construye dos veces. Ver _detailsAfterSelectFormID.
        _detailsAfterSelectFormID = If(npc IsNot Nothing, npc.FormID, 0UI)
        ' El gate de Export FOMOD es por PLUGIN (nodo raíz o NPC): respuesta inmediata acá, y otra vez en
        ' el render con retardo (idempotente y barato).
        UpdateExportFomodEnabled()
    End Sub

    ''' <summary>Cambió el CONJUNTO seleccionado del árbol: se traduce a los FormID de NPC con los que
    ''' trabaja el resto de la app.
    '''
    ''' <para>⛔ LA MECÁNICA DE SELECCIÓN VIVE EN EL CONTROL, NO ACÁ: no reintroducir un ancla propia, un
    ''' rango con Shift o un alternado con Ctrl acá (duplicaría lo que el control de lista ya sabe hacer,
    ''' con la semántica escrita en dos lugares). Acá sólo se DERIVA: las filas que no son NPC —cabeceras
    ''' de plugin, leveled lists— no aportan FormID, así que seleccionar una vacía el conjunto.</para></summary>
    Private Sub TreeViewNPCs_SeleccionCambiada(sender As Object, e As EventArgs) Handles TreeViewNPCs.SeleccionCambiada
        _selectedNpcFormIDs.Clear()
        For Each fila In TreeViewNPCs.Seleccionadas
            Dim npc = fila.Npc
            If npc IsNot Nothing Then _selectedNpcFormIDs.Add(npc.FormID)
        Next
        TreeViewNPCs.Invalidate()
        RestartSelectionDebounce()
    End Sub

    ''' <summary>Click en una fila. El botón derecho abre el menú contextual, y SÓLO sobre NPC —nunca
    ''' sobre una cabecera de plugin, un LVLN o el vacío—. Los objetivos son toda la multi-selección si el
    ''' click cayó adentro, o ese NPC solo si cayó afuera. No arranca ningún render: un click de menú no
    ''' puede costar trabajo pesado.</summary>
    Private Sub TreeViewNPCs_FilaClickeada(sender As Object, e As FilaEventArgs) Handles TreeViewNPCs.FilaClickeada
        If e.Boton <> MouseButtons.Right Then Return
        Dim npc = e.Fila?.Npc
        If npc Is Nothing Then Return

        _contextMenuTargets.Clear()
        If _selectedNpcFormIDs.Count > 1 AndAlso _selectedNpcFormIDs.Contains(npc.FormID) Then
            _contextMenuTargets.AddRange(_selectedNpcFormIDs)
        Else
            ' El control ya dejó seleccionada la fila clickeada (ver OnMouseDown), así que acá sólo se
            ' fija cuál es el que se va a renderizar y se arma el objetivo.
            _currentRandomPickFormID = npc.FormID
            _contextMenuTargets.Add(npc.FormID)
            TreeViewNPCs.Invalidate()
        End If
        _contextMenuNpcFormID = npc.FormID

        ' Reset sólo tiene sentido si algún objetivo tiene algo que descartar.
        MenuItemResetOverlay.Enabled = _contextMenuTargets.Any(
            Function(fid) _appliedPresets.ContainsKey(fid) OrElse _dirtyNpcs.Contains(fid))

        ' El texto de marcar-para-borrar pasa a "Unmark delete" sólo cuando TODOS los objetivos ya están
        ' marcados, así una selección mixta re-marca los que faltan (idempotente) en vez de desmarcar todo.
        Dim allMarked = _contextMenuTargets.Count > 0 AndAlso _contextMenuTargets.All(AddressOf _recordsToRemove.Contains)
        MenuItemMarkToDelete.Text = If(allMarked, "Unmark delete", "Mark to delete (on Save)")

        TreeViewNpcsContextMenu.Show(TreeViewNPCs, e.Punto)
    End Sub

    ''' <summary>Right-click "Mark to delete (on Save)" — toggle every context target in
    ''' <see cref="_recordsToRemove"/>. On the next Save the writer's Phase 2a drops these records from the
    ''' plugin (a saved NEW record vanishes, an OVERRIDE reverts to the base) — and if the FormID is NOT in
    ''' the target ESP it is simply never written, so this is a safe no-op for base records with no override.
    ''' Marked nodes render struck-through (see <see cref="TreeViewNPCs_PintarFila"/>). The label flipped to
    ''' "Unmark delete" when all targets were already marked, so this un-marks in that case.</summary>
    Private Sub MenuItemMarkToDelete_Click(sender As Object, e As EventArgs) Handles MenuItemMarkToDelete.Click
        If _contextMenuTargets.Count = 0 Then Return
        Dim allMarked = _contextMenuTargets.All(AddressOf _recordsToRemove.Contains)
        For Each fid In _contextMenuTargets
            If allMarked Then
                _recordsToRemove.Remove(fid)
            Else
                _recordsToRemove.Add(fid)
            End If
        Next
        TreeViewNPCs.Invalidate()
        ' A pending deletion is savable work → enable Save even when no NPC is selected.
        If _recordsToRemove.Count > 0 Then ButtonSavePlugin.Enabled = True
    End Sub






    ''' <summary>(Re)start the selection debounce so the render fires once the selection settles.
    ''' Also refreshes the Re-roll enable state + the selection-count readout immediately (not only
    ''' after the debounced render) so the user gets instant feedback on the true set size.</summary>
    Private Sub RestartSelectionDebounce()
        RefreshMultiSelectControls()
        _selectionDebounceTimer.Stop()
        _selectionDebounceTimer.Start()
    End Sub

    Private Sub SelectionDebounceTimer_Tick(sender As Object, e As EventArgs) Handles _selectionDebounceTimer.Tick
        _selectionDebounceTimer.Stop()
        RenderFromCurrentSelection()
    End Sub

    ''' <summary>Render the current selection: a single NPC renders directly; a multi-selection
    ''' renders ONE random member (ad-hoc leveled list); an LVLN node renders its own random roll;
    ''' anything else clears the per-NPC action controls.</summary>
    Private Sub RenderFromCurrentSelection()
        ' Per-plugin Export FOMOD gate — covers every debounced path, including the plugin-root
        ' branch below that ends in DisableNpcActionControls (which deliberately skips this button).
        UpdateExportFomodEnabled()
        ' Clear the viewport before the (possibly async) load so the previous NPC doesn't linger.
        ClearPreviewImmediate()
        If _selectedNpcFormIDs.Count >= 1 Then
            Dim targetFid = PickRenderTargetFromSelection()
            Dim npc As NPC_Data = Nothing
            If targetFid <> 0UI AndAlso _ctx.NpcCache.TryGetValue(targetFid, npc) AndAlso npc IsNot Nothing Then
                _currentRandomPickFormID = targetFid
                RefreshMultiSelectControls()
                TreeViewNPCs.Invalidate()
                ' AfterSelect already built the detail tree synchronously for the focused node, so
                ' skip the identical rebuild here when this render targets that same NPC (single
                ' select). Consume the flag so a later re-render of the same NPC still refreshes
                ' (overlay preview, CharGen options re-render, reload).
                Dim detailsAlreadyShown = (_detailsAfterSelectFormID = targetFid)
                _detailsAfterSelectFormID = 0UI
                If Not detailsAlreadyShown Then PopulateRecordDetails(npc)
                Dim reqVersion = Interlocked.Increment(_previewRequestVersion)
                LoadNPCOnDemandAsync(npc, reqVersion)
                Return
            End If
        End If

        _currentRandomPickFormID = 0UI
        RefreshMultiSelectControls()
        TreeViewNPCs.Invalidate()
        Dim lvln = TryCast(TreeViewNPCs.FilaEnfocadaActual()?.Tag, Canon.ILvln)
        If lvln IsNot Nothing Then
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvln, requestVersion)
        Else
            DisableNpcActionControls()
        End If
    End Sub

    ''' <summary>Pick the FormID to render from the current selection (treated as an ad-hoc leveled
    ''' list): the only member when one is selected, else a random member honoring the gender combo
    ''' filter (same rule as PickWeightedRandomFromLVLN). <paramref name="avoid"/>, when non-zero, is
    ''' not re-picked as long as another candidate exists (used by the NPC Re-roll to show a
    ''' different one).</summary>
    Private Function PickRenderTargetFromSelection(Optional avoid As UInteger = 0UI) As UInteger
        If _selectedNpcFormIDs.Count = 0 Then Return 0UI

        Dim candidates = _selectedNpcFormIDs.ToList()

        ' Gender filter (Random = no filter). Falls back to the unfiltered set if the filter would
        ' leave nothing (e.g. an all-male selection with the Female filter).
        Dim genderFilter = CurrentGenderFilter
        If genderFilter <> GenderFilterMode.Random Then
            Dim filtered = candidates.Where(
                Function(fid)
                    Dim n As NPC_Data = Nothing
                    If _ctx.NpcCache.TryGetValue(fid, n) AndAlso n IsNot Nothing Then
                        Return If(genderFilter = GenderFilterMode.Female, n.Record.ConfigurationFlagsFemale, Not n.Record.ConfigurationFlagsFemale)
                    End If
                    Return True
                End Function).ToList()
            If filtered.Count > 0 Then candidates = filtered
        End If

        ' Prefer not to re-pick the one already shown when there's an alternative.
        If avoid <> 0UI AndAlso candidates.Count > 1 Then
            candidates = candidates.Where(Function(fid) fid <> avoid).ToList()
        End If

        If candidates.Count = 1 Then Return candidates(0)
        Return candidates(_rng.Next(candidates.Count))
    End Function

    ''' <summary>NPC Re-roll over the multi-selection: render a different (gender-filtered) random
    ''' member without changing the selection. Driven by the existing <see cref="ButtonRandomNPC"/>
    ''' and the gender combo when 2+ NPCs are selected.</summary>
    Private Sub RerollFromSelection()
        If _selectedNpcFormIDs.Count < 2 Then Return
        Dim pick = PickRenderTargetFromSelection(avoid:=_currentRandomPickFormID)
        Dim npc As NPC_Data = Nothing
        If pick = 0UI OrElse Not _ctx.NpcCache.TryGetValue(pick, npc) OrElse npc Is Nothing Then Return
        _currentRandomPickFormID = pick
        TreeViewNPCs.Invalidate()
        PopulateRecordDetails(npc)
        Dim reqVersion = Interlocked.Increment(_previewRequestVersion)
        ' Gesto de azar: re-sortea tambien la LVLN de su cadena. Ver el param `rerollLeveled`.
        LoadNPCOnDemandAsync(npc, reqVersion, rerollLeveled:=True)
    End Sub

    ''' <summary>Refresh controls that depend on the multi-selection: when 2+ NPCs are selected the
    ''' selection behaves as an ad-hoc leveled list, so enable the existing NPC Re-roll button
    ''' (<see cref="ButtonRandomNPC"/>) and the gender filter, and show the true selection count in
    ''' the title. The single/none state is set authoritatively by the render
    ''' (LoadNPCOnDemandAsync / LoadLVLNOnDemandAsync), so this only force-enables for the multi case.</summary>
    Private Sub RefreshMultiSelectControls()
        If _selectedNpcFormIDs.Count >= 2 Then
            If ButtonRandomNPC IsNot Nothing Then ButtonRandomNPC.Enabled = True
            If ComboBoxGender IsNot Nothing Then ComboBoxGender.Enabled = True
        End If
        Dim n = _selectedNpcFormIDs.Count
        Me.Text = If(n = 0, "FO4 NPC Manager", $"FO4 NPC Manager  —  {n} NPC(s) selected")
    End Sub

    ''' <summary>Context-menu "Mark as changed": flags the NPC dirty (bold) even with no overlay, so
    ''' a subsequent Save emits a forwarded (identity) override for it.</summary>
    Private Sub MenuItemMarkChanged_Click(sender As Object, e As EventArgs) Handles MenuItemMarkChanged.Click
        Dim targets = If(_contextMenuTargets.Count > 0, _contextMenuTargets, New List(Of UInteger) From {_contextMenuNpcFormID})
        Dim changed As Boolean = False
        For Each fid In targets
            If fid <> 0UI AndAlso _dirtyNpcs.Add(fid) Then changed = True
        Next
        If changed Then RefreshTreeAfterDirtyChange()
    End Sub

    ''' <summary>Context-menu "Save Selected": opens the Save ESP/ESM dialog defaulting to the
    ''' "Selected" scope (the NPC multi-selection). The toolbar Save button defaults to "All changed".</summary>
    Private Async Sub MenuItemSaveSelected_Click(sender As Object, e As EventArgs) Handles MenuItemSaveSelected.Click
        Await LaunchSaveDialogAsync(defaultToSelected:=True)
    End Sub

    ''' <summary>Context-menu "Build CharGen (loose) — Selected": bake CharGen loose for every selected
    ''' NPC. One NPC → blocking msgbox (same as the toolbar single path); many → a determinate progress
    ''' dialog (see <see cref="BuildCharGenForSelectionAsync"/>).</summary>
    Private Async Sub MenuItemBuildChargen_Click(sender As Object, e As EventArgs) Handles MenuItemBuildChargen.Click
        ' Game-aware bake (no blocking gate).
        Dim targets = If(_contextMenuTargets.Count > 0, _contextMenuTargets.Distinct().ToList(), New List(Of UInteger))
        If targets.Count = 0 Then Return
        If targets.Count = 1 Then
            Await BuildCharGenSingle(targets(0))
        Else
            Await BuildCharGenForSelectionAsync(targets)
        End If
    End Sub

    ''' <summary>Context-menu "Reset": discard the in-memory overlay for the NPC. The NPC reverts to its
    ''' current baseline record: the saved override if it was saved this session (MergeOverridePlugin put
    ''' it in GetRecord), else vanilla. Does NOT touch disk itself — but an NPC with F4SE data persisted
    ''' on disk (sidecar row → BodyGen .ini → VMAD apply-script) STAYS dirty so the next Save prunes that
    ''' state and the game ends up matching the pristine preview (WYSIWYG routing): leaving it clean here
    ''' would make "Reset → Save" a silent no-op, with the game / next session still showing the discarded
    ''' edits. NPCs never saved just drop their dirty flag.
    ''' Destructive (drops BodyMorphs/Skin edits too) → confirmation first.</summary>
    Private Async Sub MenuItemResetOverlay_Click(sender As Object, e As EventArgs) Handles MenuItemResetOverlay.Click
        Dim sourceTargets = If(_contextMenuTargets.Count > 0, _contextMenuTargets, New List(Of UInteger) From {_contextMenuNpcFormID})
        ' Only NPCs that actually have something to discard (LM overlay, NPC-record override, or dirty mark).
        Dim targets = sourceTargets.
            Where(Function(fid) fid <> 0UI AndAlso (_appliedPresets.ContainsKey(fid) OrElse _npcRecordOverrides.ContainsKey(fid) OrElse _dirtyNpcs.Contains(fid))).
            Distinct().ToList()
        If targets.Count = 0 Then Return

        Dim prompt = If(targets.Count = 1,
            "Discard all in-memory changes for this NPC (including BodyMorphs / Skin / Overlays edits) and revert to its current record?",
            $"Discard all in-memory changes for the {targets.Count} selected NPCs (including BodyMorphs / Skin / Overlays edits) and revert each to its current record?")
        If MessageBox.Show(Me,
                           prompt & vbCrLf & vbCrLf &
                           "Nothing on disk changes now. NPCs whose body edits were already saved stay marked " &
                           "as changed: the next Save removes them from the .bssliders sidecar, re-emits the " &
                           "BodyGen .ini and writes a cleanup helper script, so the game matches this preview.",
                           "Reset NPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Dim shownFormID As UInteger = If(_renderHost IsNot Nothing AndAlso _renderHost.LastRenderedState IsNot Nothing,
                                         _renderHost.LastRenderedState.RootNpcFormID, 0UI)
        Dim mustReRender As Boolean = False
        For Each fid In targets
            _appliedPresets.Remove(fid)
            ' NPC-record edits (name/flags/race/keywords/factions/… from NpcEditor_Form) are an authored overlay
            ' too — clear them here so "revert to record" actually reverts them like every other overlay does.
            _npcRecordOverrides.Remove(fid)
            ' NpcEditor_Form applies its edit by mutating the LIVE parse-cache instance for immediate preview, so
            ' clearing the override bag alone would NOT undo the visible edit. Drop the mutated instance from the
            ' cache; the re-render below re-parses the base record fresh (GetParsedNpc), giving the pristine NPC.
            Dim discardedNpc As NPC_Data = Nothing
            _ctx.NpcCache.TryRemove(fid, discardedNpc)
            ' WYSIWYG routing: when this NPC has F4SE data already persisted
            ' (sidecar row → BodyGen .ini → VMAD apply-script), the revert only reaches the game after a
            ' Save prunes it — so KEEP the NPC dirty and let the normal "Save (all changed)" route there:
            ' MergeOneNpcIntoSidecar rebuilds an EMPTY row (Write() drops it), the BodyGen .ini is
            ' re-emitted without the NPC, and NpcApplyScriptEmitter.ApplyToNpc emits the CLEANUP
            ' apply-script that undoes the co-save state in-game. Without this the post-reset preview
            ' showed a state that existed nowhere: not on disk, not in-game, and not on the next app
            ' launch (sidecar hydration resurrected the edits).
            If _sidecarBackedNpcs.Contains(fid) Then
                _dirtyNpcs.Add(fid)
            Else
                _dirtyNpcs.Remove(fid)
            End If
            If fid = shownFormID Then mustReRender = True
        Next
        RefreshTreeAfterDirtyChange()

        ' Re-render from the baseline only if the currently-shown NPC was among those reset. GetParsedNpc re-parses
        ' on the miss we just created above, so the preview shows the pristine record, not the mutated cache instance.
        If mustReRender AndAlso shownFormID <> 0UI Then
            Dim npc As NPC_Data = _ctx.GetParsedNpc(shownFormID)
            If npc IsNot Nothing Then
                Try
                    Dim version = Interlocked.Increment(_previewRequestVersion)
                    Await LoadNPCOnDemandAsyncFromExisting(npc, version)
                Catch ex As Exception
                    MessageBox.Show($"Failed to re-render after reset: {ex.Message}", "Reset NPC",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error)
                End Try
            End If
        End If
    End Sub

    ''' <summary>Limpia el viewport inmediatamente al cambiar de selección.
    ''' El pipeline en Render.vb trata Shapes vacío como reset: Clean(False) + CleanTextures()
    ''' + LoadedShapes.Clear() — sin mensaje al usuario. Si la carga async del nuevo NPC produce
    ''' 0 shapes (caso robot con OBTE pool vacío), el preview queda vacío en lugar de mostrar
    ''' el render del NPC anterior.</summary>
    Private Sub ClearPreviewImmediate()
        If _renderHost Is Nothing OrElse _renderHost.PreviewCtl Is Nothing Then Return
        _renderHost.PreviewCtl.RenderShapes(Array.Empty(Of IRenderableShape)())
    End Sub

    ''' <summary>Restaura el estado "sin NPC seleccionado" de los controles de acción por-NPC
    ''' (el mismo baseline que fija el Designer al arrancar, en MainForm.Designer.vb
    ''' InitializeComponent). ⛔ El único llamador es <see cref="RenderFromCurrentSelection"/>, NO el
    ''' handler del foco: el doc decía otra cosa y eso hacía creer que suprimir un aviso de foco apagaba
    ''' estos botones. Corre cuando la selección del árbol cae en un nodo NO accionable: un root de plugin / grupo "[LVLN]" (Tag = Nothing) o
    ''' ausencia de nodo. Sin esto los botones quedaban habilitados apuntando vía
    ''' <c>_renderHost.LastRenderedState</c> / <c>CurrentBaseState</c> al NPC cargado previamente
    ''' (ButtonEditBody/EditFace/CopyLook/SavePlugin/… leen ese estado), de modo que la acción
    ''' operaba sobre la selección vieja. Los handlers de NPC (LoadNPCOnDemandAsync) y de LVLN
    ''' (LoadLVLNOnDemandAsync) re-habilitan lo que corresponde al cargar una selección válida.
    ''' Corre siempre en el UI thread (AfterSelect es un event handler de WinForms).</summary>
    Private Sub DisableNpcActionControls()
        ButtonEditFace.Enabled = False
        ButtonEditBody.Enabled = False
        ButtonEditOutfit.Enabled = False
        ButtonEditNpc.Enabled = False
        ButtonLoadLooksmenu.Enabled = False
        ButtonSaveLooksmenu.Enabled = False
        ButtonCopyLook.Enabled = False
        ButtonPasteLook.Enabled = False
        ' Save stays available when there is pending work to write even with no NPC selected — dirty NPCs
        ' OR records marked for deletion (a mark-to-delete alone must still let the user Save to drop them).
        ButtonSavePlugin.Enabled = (_dirtyNpcs.Count > 0 OrElse _recordsToRemove.Count > 0)
        ButtonBuildCharGen.Enabled = False
        ButtonSaveSceneNif.Enabled = False
        ' ButtonExportFomod deliberately NOT touched here: its gating is per-PLUGIN, not per-NPC —
        ' a plugin-root selection (Tag=Nothing) lands in this method and must KEEP the button
        ' enabled. UpdateExportFomodEnabled() runs in the same selection flows and fixes it up.
    End Sub

    ''' <summary>Plugin (file name with extension) the current tree selection points at, for the
    ''' Export FOMOD gate: an NPC leaf resolves to its winning plugin, a top-level plugin-root
    ''' node ("PLUGIN_&lt;name&gt;", Tag=Nothing) resolves to its own name. Anything else (LVLN
    ''' roots, group nodes, no selection) → Nothing.</summary>
    Private Function SelectedPluginForFomodExport() As String
        Dim fila = TreeViewNPCs.FilaEnfocadaActual()
        If fila Is Nothing Then Return Nothing
        Dim npc = fila.Npc
        If npc IsNot Nothing Then Return npc.PluginName
        ' Cabecera de plugin de la sección 1: el nombre sale de la CLAVE, nunca del texto —el texto
        ' lleva el prefijo "[00042] " y la cuenta "(23)".
        If fila.Tipo = TipoDeFila.GrupoDePlugin Then
            Return fila.Clave.Substring("PLUGIN_".Length)
        End If
        Return Nothing
    End Function

    ''' <summary>Export FOMOD gate: enabled ONLY when the selection resolves to a plugin AUTHORED
    ''' by this app (TES4.CNAM marker, <see cref="PluginManager.IsNpcManagerPlugin"/>) whose file
    ''' exists on disk. Cheap (dict lookup + File.Exists) — safe to call on every selection
    ''' change. Called from AfterSelect + RenderFromCurrentSelection + after a Save ESP.</summary>
    Private Sub UpdateExportFomodEnabled()
        Dim pluginName = SelectedPluginForFomodExport()
        ButtonExportFomod.Enabled =
            Not String.IsNullOrEmpty(pluginName) AndAlso
            _pluginManager IsNot Nothing AndAlso
            _pluginManager.IsNpcManagerPlugin(pluginName) AndAlso
            IO.File.Exists(IO.Path.Combine(_dataPath, pluginName))
    End Sub

    ''' <summary>Open the Export FOMOD dialog for the selected app-authored plugin. The dialog
    ''' owns metadata editing (persisted to &lt;plugin&gt;.fomodmeta.json), manifest validation and
    ''' the ZIP write; this handler only resolves the plugin, its NPC FormIDs (needed for the
    ''' loose-only FaceGen enumeration) and whether the plugin has unsaved changes (surfaced as a
    ''' "Save ESP first" warning inside the dialog — the ZIP always reflects DISK state).</summary>
    Private Sub ButtonExportFomod_Click(sender As Object, e As EventArgs) Handles ButtonExportFomod.Click
        Dim pluginName = SelectedPluginForFomodExport()
        If String.IsNullOrEmpty(pluginName) OrElse Not _pluginManager.IsNpcManagerPlugin(pluginName) Then Return
        Dim espFullPath = IO.Path.Combine(_dataPath, pluginName)
        If Not IO.File.Exists(espFullPath) Then
            MessageBox.Show(Me, $"Plugin file not found on disk: {espFullPath}", "Export FOMOD",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Global FormIDs of the NPC_ records whose winning version lives in this plugin (same
        ' criterion as GetAuthoredRecords) — the exporter needs them to enumerate per-NPC FaceGen
        ' loose files when the app is configured loose-only.
        Dim npcFormIDs As New List(Of UInteger)
        Dim recs = _pluginManager.GetRecordsOfType("NPC_")
        If recs IsNot Nothing Then
            For Each rec In recs
                If rec IsNot Nothing AndAlso String.Equals(rec.SourcePluginName, pluginName, StringComparison.OrdinalIgnoreCase) Then
                    npcFormIDs.Add(rec.Header.FormID)
                End If
            Next
        End If

        ' Unsaved-work check SCOPED to this plugin: a dirty NPC (or a mark-to-delete) whose winning
        ' record lives here means the on-disk plugin lags the editor → the dialog shows the
        ' "Save ESP first" warning.
        Dim hasUnsaved As Boolean = False
        For Each fid In _dirtyNpcs.Concat(_recordsToRemove)
            Dim rec = _pluginManager.GetRecord(fid)
            If rec IsNot Nothing AndAlso String.Equals(rec.SourcePluginName, pluginName, StringComparison.OrdinalIgnoreCase) Then
                hasUnsaved = True
                Exit For
            End If
        Next

        ' Preview capture for the optional wizard screenshot: whatever the main viewport shows
        ' right now (same front-buffer read the WM editor uses). Best-effort — Nothing (no GL
        ' frame / capture failure) just disables the "Include screenshot" checkbox in the dialog.
        Dim previewShot As Bitmap = Nothing
        Try
            previewShot = _renderHost?.PreviewCtl?.CaptureBitmap()
        Catch
            previewShot = Nothing
        End Try

        Try
            Using dlg As New FomodExport_Form(espFullPath, pluginName, Config_App.Current.Game,
                                              _dataPath, _pluginManager, npcFormIDs, hasUnsaved, previewShot)
                If dlg.ShowDialog(Me) = DialogResult.OK Then
                    SetStatus($"FOMOD exported: {dlg.ExportedZipPath}")
                End If
            End Using
        Finally
            ' The dialog only borrows the bitmap (PictureBox + PNG encode happen while it's open).
            previewShot?.Dispose()
        End Try
    End Sub

    ''' <param name="rerollLeveled">True SOLO para los gestos que el usuario entiende como "sortea de
    ''' nuevo": <c>ButtonRandomNPC</c> y <see cref="RerollFromSelection"/>. El resto ANCLA a la hoja que
    ''' esta en pantalla. NO se implementa borrando <c>LastRenderedState</c>: ese campo lo leen el
    ''' fast-path de tints, el swatch de pelo, ComputeBodyEditAvailability, ReloadCurrentNpcFull y
    ''' ButtonEditFace_Click.</param>
    Private Async Sub LoadNPCOnDemandAsync(npc As NPC_Data, requestVersion As Integer,
                                           Optional rerollLeveled As Boolean = False)
        Try
            Dim _swL As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)
            SetStatus($"Loading assets for {npc}...")
            Await EnsureAssetDictionaryAsync()
            Logger.LogLazy(Function() $"[PERF-L] EnsureAssetDictionary @ {_swL.ElapsedMilliseconds}ms")
            If requestVersion <> _previewRequestVersion Then Return

            SetStatus($"Resolving {npc}...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
            ' Nothing = el resolver deduce el ancla del propio host. `Reroll` sólo en los dos gestos de azar.
            Dim pin As NpcStateResolver.LeveledLeafPin = If(rerollLeveled, NpcStateResolver.LeveledLeafPin.Reroll, Nothing)
            Await Task.Run(Sub()
                               baseState = _stateResolver.ResolveNPCBaseState(npc, _renderHost, pin)
                               outfitEntries = BuildOutfitComboEntries(baseState)
                           End Sub)
            Logger.LogLazy(Function() $"[PERF-L] ResolveNPCBaseState+outfit @ {_swL.ElapsedMilliseconds}ms")
            If requestVersion <> _previewRequestVersion Then Return

            _renderHost.CurrentBaseState = baseState
            _currentOutfitEntries = If(outfitEntries, New List(Of OutfitComboEntry))

            ' Now that an NPC is selected and resolved, the editor actions can target it.
            ' Paste enable is recomputed against the new state — only stays enabled if the
            ' clipboard's source NPC matched this one's race + gender.
            If InvokeRequired Then
                Invoke(Sub()
                           ButtonLoadLooksmenu.Enabled = True
                           ButtonSaveLooksmenu.Enabled = True
                           ButtonCopyLook.Enabled = True
                           ButtonSavePlugin.Enabled = True
                           ButtonSaveSceneNif.Enabled = True
                       End Sub)
            Else
                ButtonLoadLooksmenu.Enabled = True
                ButtonSaveLooksmenu.Enabled = True
                ButtonCopyLook.Enabled = True
                ButtonSavePlugin.Enabled = True
                ButtonSaveSceneNif.Enabled = True
            End If
            UpdatePasteLookEnabled()
            ' ButtonEditBody.Enabled is decided after render in UpdateEditBodyEnabled when we
            ' know whether the race's RACE.BSMS / body .tri actually carry editable channels.

            ' Re-roll button gates on whether the NPC has LVLN in its template chain.
            ' Gender combo is LVLN-node-only (the gender filter applies to picking a leaf from a
            ' leveled list, not to re-rolling templates of an already-selected NPC), so it stays
            ' disabled here regardless of templates. Enabled in LoadLVLNOnDemandAsync.
            ' Re-roll + gender enable when the NPC has leveled templates OR when this render came from
            ' an NPC multi-selection (the conjunto acts as an ad-hoc leveled list). _selectedNpcFormIDs
            ' is read on the UI thread inside each branch.
            Dim hasLeveledTemplates = NpcHasLeveledTemplates(npc)
            If InvokeRequired Then
                Invoke(Sub()
                           Dim multiSel = _selectedNpcFormIDs.Count >= 2
                           ButtonRandomNPC.Enabled = hasLeveledTemplates OrElse multiSel
                           ComboBoxGender.Enabled = multiSel
                       End Sub)
            Else
                Dim multiSel = _selectedNpcFormIDs.Count >= 2
                ButtonRandomNPC.Enabled = hasLeveledTemplates OrElse multiSel
                ComboBoxGender.Enabled = multiSel
            End If

            ' Populate outfit combo
            PopulateOutfitCombo()
            Logger.LogLazy(Function() $"[PERF-L] pre-render UI work (record details + combos) @ {_swL.ElapsedMilliseconds}ms")

            ' Render with first outfit option
            Await RenderCurrentStateAsync(requestVersion)

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    ''' <summary>When a LVLN node is selected, pick a random NPC from the list and render it.</summary>
    Private Async Sub LoadLVLNOnDemandAsync(lvlnData As Canon.ILvln, requestVersion As Integer)
        Try
            SetStatus($"Picking random NPC from {lvlnData.EditorID}...")
            Await EnsureAssetDictionaryAsync()
            If requestVersion <> _previewRequestVersion Then Return

            ' Pick a random leaf NPC from the LVLN (weighted by Count, recursing into nested LVLNs)
            Dim pickedFormID = _stateResolver.PickWeightedRandomFromLVLN(lvlnData.FormID, New HashSet(Of UInteger)())
            If pickedFormID = 0UI Then
                SetStatus($"No NPCs found in {lvlnData.EditorID}")
                Return
            End If

            Dim npc As NPC_Data = Nothing
            _ctx.NpcCache.TryGetValue(pickedFormID, npc)
            If npc Is Nothing Then
                ' NPC not in cache — parse it on-the-fly
                Dim npcRec = _pluginManager.GetRecord(pickedFormID)
                If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then
                    SetStatus($"Picked FormID {pickedFormID:X8} is not a valid NPC")
                    Return
                End If
                npc = RecordParsers.ParseNPC(npcRec, _pluginManager)
            End If

            PopulateRecordDetails(npc)

            SetStatus($"Resolving {npc} (from {lvlnData.EditorID})...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
            Await Task.Run(Sub()
                               ' Reroll: llegar aca YA fue un sorteo (PickWeightedRandomFromLVLN, mas
                               ' arriba) y los tres gestos que lo alcanzan —elegir el nodo LVLN, el boton
                               ' de azar y el combo de genero— son gestos de azar.
                               baseState = _stateResolver.ResolveNPCBaseState(npc, _renderHost,
                                                                              NpcStateResolver.LeveledLeafPin.Reroll)
                               outfitEntries = BuildOutfitComboEntries(baseState)
                           End Sub)
            If requestVersion <> _previewRequestVersion Then Return

            _renderHost.CurrentBaseState = baseState
            _currentOutfitEntries = If(outfitEntries, New List(Of OutfitComboEntry))

            ' Now that an NPC is selected and resolved, the editor actions can target it.
            ' Paste enable is recomputed against the new state — only stays enabled if the
            ' clipboard's source NPC matched this one's race + gender.
            If InvokeRequired Then
                Invoke(Sub()
                           ButtonLoadLooksmenu.Enabled = True
                           ButtonSaveLooksmenu.Enabled = True
                           ButtonCopyLook.Enabled = True
                           ButtonSavePlugin.Enabled = True
                           ButtonSaveSceneNif.Enabled = True
                       End Sub)
            Else
                ButtonLoadLooksmenu.Enabled = True
                ButtonSaveLooksmenu.Enabled = True
                ButtonCopyLook.Enabled = True
                ButtonSavePlugin.Enabled = True
                ButtonSaveSceneNif.Enabled = True
            End If
            UpdatePasteLookEnabled()
            ' ButtonEditBody.Enabled is decided after render in UpdateEditBodyEnabled when we
            ' know whether the race's RACE.BSMS / body .tri actually carry editable channels.

            ' LVLN selections always allow re-randomization
            If InvokeRequired Then
                Invoke(Sub()
                           ButtonRandomNPC.Enabled = True
                           ComboBoxGender.Enabled = True
                       End Sub)
            Else
                ButtonRandomNPC.Enabled = True
                ComboBoxGender.Enabled = True
            End If

            PopulateOutfitCombo()
            Await RenderCurrentStateAsync(requestVersion)

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    Private Sub ComboBoxOutfit_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxOutfit.SelectedIndexChanged
        If _suppressOutfitComboEvent Then Return
        If _renderHost Is Nothing Then Return
        If _renderHost.CurrentBaseState Is Nothing Then Return
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    Private Sub ButtonReroll_Click(sender As Object, e As EventArgs) Handles ButtonReroll.Click
        If _renderHost.CurrentBaseState Is Nothing Then Return

        Dim idx = If(ComboBoxOutfit.InvokeRequired,
                     CInt(ComboBoxOutfit.Invoke(Function() ComboBoxOutfit.SelectedIndex)),
                     ComboBoxOutfit.SelectedIndex)
        Dim entry As OutfitComboEntry = Nothing
        If idx >= 0 AndAlso idx < _currentOutfitEntries.Count Then
            entry = _currentOutfitEntries(idx)
        End If

        Dim hasOutfit = entry IsNot Nothing AndAlso entry.SlotKind <> OutfitSlotKind.NoOutfit AndAlso entry.OutfitFormID <> 0UI
        Dim hasObte = _renderHost.CurrentBaseState.HasObjectTemplate
        ' Reroll requires SOMETHING to re-randomize: an outfit (LVLI sample) or an OBTE
        ' (OMOD modcol_* random pick happens inside ObjectTemplateResolver each render).
        ' Without either, the button is a no-op.
        If Not hasOutfit AndAlso Not hasObte Then Return

        If hasOutfit Then
            Dim draft = TryGetOutfitDraft(entry.OutfitFormID)
            If draft IsNot Nothing Then
                ' Draft outfit: the lib sampler can't see drafts (GetRecord(0xFF…)=Nothing → empty → naked).
                ' Re-roll the draft's own leveled items (clear the cached realization) and re-resolve through
                ' the draft path. Context keywords stay empty (drafts carry no OMOD/LLKC context), matching
                ' AddOutfitEntryIfPresent.
                RerollDraftLeveled(draft)
                entry.SampledArmorFormIDs = ResolveDraftArmoList(draft)
                entry.SampledArmorContextKeywords = New Dictionary(Of UInteger, List(Of UInteger))
            Else
                ' Real OTFT: re-sample outfit ARMOs (LVLI random pick + LLKC propagation).
                Dim warnings As New List(Of String)
                Dim picks = OutfitResolver.SampleOutfitWithKeywords(entry.OutfitFormID, _pluginManager, warnings)
                entry.SampledArmorFormIDs = picks.Select(Function(p) p.ArmoFormID).ToList()
                entry.SampledArmorContextKeywords = picks.ToDictionary(Function(p) p.ArmoFormID, Function(p) p.ContextKeywords)
                For Each w In warnings
                Next
            End If
        End If

        ' Disparar render. OJO — esto YA NO re-rolea los modcol_*: ResolveNpcCombinations es DETERMINISTA
        ' por defecto (first-wins en los includes DontUseAll) — NO usar Random.Shared ahí: el mismo NPC
        ' produciría NIFs distintos en dos corridas e invalidaría toda comparación por hash aguas abajo.
        ' La variación sigue disponible, pero EXPLÍCITA y reproducible: pasar `rngSeed` a
        ' ResolveNpcCombinations / ResolveArmoCombinations (hoy los call sites de NpcMeshCollector
        ' no lo pasan). Recuperar el re-roll de chunks de robot en el preview requiere cablear un seed
        ' variable hasta esos dos call sites — cambio de app, no de lib.
        ' Si solo hay OBTE sin outfit (robots, brahmin), hoy no aplica ningún re-roll.
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    Private Sub ButtonRandomNPC_Click(sender As Object, e As EventArgs) Handles ButtonRandomNPC.Click
        ' Multi-selection acts as an ad-hoc leveled list: re-roll a (gender-filtered) random member.
        If _selectedNpcFormIDs.Count >= 2 Then
            RerollFromSelection()
            Return
        End If

        Dim selectedNode = TreeViewNPCs.FilaEnfocadaActual()
        If selectedNode Is Nothing Then Return

        ' If selected node is a LVLN, re-pick a random NPC from it.
        ' ⛔ SÓLO CON EL LOTE VACÍO. Antes alcanzaba con que la fila enfocada fuera un LVLN, y eso era
        ' inalcanzable con un lote no vacío porque elegir una fila de LVLN vacía `_selectedNpcFormIDs`.
        ' Ahora el foco puede llegar a un LVLN por un COLAPSO (ver `VirtualTreeList.AlternarFila`) con el
        ' lote intacto, y sin esta guarda el botón sortearía otro NPC de la lista y lo renderizaría
        ' mientras el lote sigue apuntando al que el usuario eligió.
        Dim lvlnData = TryCast(selectedNode.Tag, Canon.ILvln)
        If lvlnData IsNot Nothing AndAlso _selectedNpcFormIDs.Count = 0 Then
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvlnData, requestVersion)
            Return
        End If

        ' Re-resolve the SAME NPC — the LVLN in its template chain will produce
        ' different random picks (different face/gender) each time.
        Dim npc = TryCast(selectedNode.Tag, NPC_Data)
        If npc Is Nothing Then Return

        Dim requestVersion2 = Interlocked.Increment(_previewRequestVersion)
        ' El boton de azar SI re-sortea: es su unico trabajo. Sin este True el ancla lo dejaria mudo.
        LoadNPCOnDemandAsync(npc, requestVersion2, rerollLeveled:=True)
    End Sub

    ''' <summary>Changing the gender filter re-rolls the pick: from the multi-selection (ad-hoc
    ''' leveled list) when 2+ NPCs are selected, otherwise from the selected LVLN node. No-op for a
    ''' single plain NPC (the filter only governs random picks).</summary>
    Private Sub ComboBoxGender_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxGender.SelectedIndexChanged
        If _selectedNpcFormIDs.Count >= 2 Then
            RerollFromSelection()
            Return
        End If
        ' Misma guarda que ButtonRandomNPC_Click: el foco puede quedar en un LVLN por un colapso con el
        ' lote intacto, y ahí el que manda es el lote.
        Dim lvln = TryCast(TreeViewNPCs.FilaEnfocadaActual()?.Tag, Canon.ILvln)
        If lvln IsNot Nothing AndAlso _selectedNpcFormIDs.Count = 0 Then
            Dim v = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvln, v)
        End If
    End Sub

    Private Sub ButtonLightRig_Click(sender As Object, e As EventArgs) Handles ButtonLightRig.Click
        ' AllowHiddenSegments = False: el render de NPC Manager DEPENDE de la oclusion por segmento
        ' (swap de Pip-Boy 60/160, ocultado de head parts) y por eso Program.vb fuerza
        ' Setting_DrawHiddenSegments = False al arrancar. El dialogo compartido no puede dejar tocarlo:
        ' con False la casilla no se muestra y el valor no se escribe. Es el UNICO ajuste app-aware.
        ' Using: ShowDialog NO dispone el form. Sin esto, cada apertura del dialogo compartido filtra
        ' los handles de una pestana entera de NUD, sliders y swatches; el Finally solo saca handlers.
        Using form As New LightRigForm With {.AllowHiddenSegments = False}
            AddHandler form.LightsChanged, AddressOf OnLightRigChanged
            AddHandler form.RenderSettingsChanged, AddressOf OnRenderSettingsChanged
            Try
                form.ShowDialog(Me)
            Finally
                RemoveHandler form.LightsChanged, AddressOf OnLightRigChanged
                RemoveHandler form.RenderSettingsChanged, AddressOf OnRenderSettingsChanged
            End Try
        End Using
    End Sub

    Private Sub OnLightRigChanged()
        If _previewControl IsNot Nothing AndAlso Not _previewControl.IsDisposed Then
            _previewControl.UpdateRequired = True
            _previewControl.Update()
        End If
    End Sub

    ''' <summary>La pestana Rendering toca cosas que NO se arreglan repintando (recalculo de normales,
    ''' welding, skinning): hay que re-correr el pipeline con la geometria marcada sucia.</summary>
    Private Sub OnRenderSettingsChanged()
        If _previewControl Is Nothing OrElse _previewControl.IsDisposed Then Return
        ' Toda la logica vive en la libreria: los ajustes de render estan duplicados como estado del
        ' PreviewModel y del Floor, y sin empujarlos la casilla no hace nada visible.
        _previewControl.ApplyRenderSettingsFromConfig()
    End Sub

    Private Async Sub RenderOnDemandAsync(requestVersion As Integer)
        Try
            Await RenderCurrentStateAsync(requestVersion)
        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
        End Try
    End Sub

    Private Sub PopulateOutfitCombo()
        If InvokeRequired Then
            Invoke(Sub() PopulateOutfitCombo())
            Return
        End If

        _suppressOutfitComboEvent = True
        ComboBoxOutfit.Items.Clear()
        If _currentOutfitEntries.Count = 0 Then
            ComboBoxOutfit.Items.Add("(no outfit)")
            ComboBoxOutfit.SelectedIndex = 0
        Else
            For Each entry In _currentOutfitEntries
                ComboBoxOutfit.Items.Add(entry.Label)
            Next
            ComboBoxOutfit.SelectedIndex = 0
        End If
        _suppressOutfitComboEvent = False
    End Sub

    Private Function GetSelectedOutfitArmorIDs(host As NpcRenderHost) As List(Of UInteger)
        Dim entry = SelectedOutfitEntryForHost(host)
        Return If(entry Is Nothing, New List(Of UInteger), entry.SampledArmorFormIDs)
    End Function

    Private Function GetSelectedOutfitContextKeywords(host As NpcRenderHost) As Dictionary(Of UInteger, List(Of UInteger))
        Dim entry = SelectedOutfitEntryForHost(host)
        Return If(entry Is Nothing, New Dictionary(Of UInteger, List(Of UInteger)), entry.SampledArmorContextKeywords)
    End Function

    ''' <summary>The outfit entry to render for a host. The MAIN host reads the entry selected in
    ''' <c>ComboBoxOutfit</c> (backed by <c>_currentOutfitEntries</c>). Editor / outfit-picker hosts read
    ''' the Default entry (index 0) of their own <see cref="NpcRenderHost.OutfitEntries"/>, so they never
    ''' depend on — or are perturbed by — the main form's outfit combo selection.</summary>
    Private Function SelectedOutfitEntryForHost(host As NpcRenderHost) As OutfitComboEntry
        If host Is _renderHost Then
            Dim idx = If(ComboBoxOutfit.InvokeRequired,
                         CInt(ComboBoxOutfit.Invoke(Function() ComboBoxOutfit.SelectedIndex)),
                         ComboBoxOutfit.SelectedIndex)
            If idx < 0 OrElse idx >= _currentOutfitEntries.Count Then Return Nothing
            Return _currentOutfitEntries(idx)
        End If
        Dim entries = host.OutfitEntries
        If entries Is Nothing OrElse entries.Count = 0 Then Return Nothing
        Return entries(0)
    End Function

    Private Async Function RenderCurrentStateAsync(requestVersion As Integer, Optional host As NpcRenderHost = Nothing) As Task
        If host Is Nothing Then host = _renderHost
        If host.CurrentBaseState Is Nothing Then Return
        Dim _swR As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)

        ' Cualquier re-render del preview principal (cualquier path: wrapper, RenderFromCurrentSelection,
        ' etc.) invalida la animación en curso y refresca la barra al NPC actual (CurrentBaseState ya es
        ' el nuevo). Imprescindible para que el combo NO quede con clips de la raza anterior.
        If host Is _renderHost Then RefreshAnimBarForCurrentNpc()
        Logger.LogLazy(Function() $"[PERF-R] RefreshAnimBar @ {_swR.ElapsedMilliseconds}ms")

        ' Build final state with selected outfit
        Dim state = _stateResolver.CloneVisualState(host.CurrentBaseState)
        state.LoadoutArmorFormIDs.AddRange(GetSelectedOutfitArmorIDs(host))
        For Each kvCtx In GetSelectedOutfitContextKeywords(host)
            state.LoadoutArmorContextKeywords(kvCtx.Key) = kvCtx.Value
        Next

        ' Gate de FaceGen = RACE.DATA bit 0x2 "FaceGen Head", el discriminador canónico de 0 excepciones
        ' (RaceUtil.RaceSupportsFaceGen) — el MISMO que habilita el botón Edit Face (UpdateEditFaceEnabled),
        ' que gatea el bake (FaceGenBuilder) y que deja entrar las head parts (NpcMeshCollector.
        ' RaceBuildsFaceGenHead). NO usar "¿existe el FaceGeom horneado?" (HasFaceGenAssets) como
        ' heurística: el motor tiene DOS ramas aguas abajo del bit (cargar el NIF horneado o armar la
        ' cabeza desde head parts), así que la falta del NIF elige rama, no apaga el FaceGen.
        ' "Show other gender" (editores ARMA/ARMO): se dibuja una cabeza race-default del género destino, NO
        ' la del NPC fuente (que es la del género ORIGINAL) — ahí sí se suprime el FaceGen a mano.
        Dim useFaceGen = RaceUtil.RaceSupportsFaceGen(state.RaceFormID, _pluginManager) AndAlso Not host.PreviewGenderOverride.HasValue

        ' OnlyFace collect-time filter: editor hosts set host.OnlyFaceCollect=True so their
        ' embedded preview matches MainForm's "Only Face" PreviewMode (skin + outfit skipped at
        ' CollectMeshCandidates, only HeadParts enter the pipeline). For the MainForm host the
        ' value comes from its ComboBoxPreviewMode. Either path funnels into the same flag on
        ' the variant — no parallel rendering paths.
        Dim onlyFaceCollect = host.OnlyFaceCollect OrElse (host Is _renderHost AndAlso CurrentPreviewMode = PreviewMode.OnlyFace)
        Dim onlyOutfitCollect = host.OnlyOutfitCollect

        Dim previewVariant As New PreviewVariantDefinition With {
            .RootNpcFormID = state.FormID,
            .VariantId = 1,
            .DisplayName = $"{NpcManagerFormat.DescribeNpc(_ctx.GetParsedNpc(state.FormID))} | {ComboBoxOutfit.Text}",
            .State = state,
            .UseFaceGen = useFaceGen,
            .OnlyFaceCollect = onlyFaceCollect,
            .OnlyOutfitCollect = onlyOutfitCollect,
            .RaceFilterBypassArmaFormID = host.RaceFilterBypassArmaFormID
        }

        SetStatus($"Rendering {previewVariant.DisplayName}...")
        ' Serialize the CPU compute (no two BuildRenderPlan concurrently) and freeze the render toggles
        ' for the main host while it runs, so a checkbox toggle can't reassign host.Toggles mid-compute
        ' (BuildRenderPlan + the morph resolvers read host.Toggles on the background thread). The gate is
        ' released and the controls re-enabled right after the compute — the GL tail runs on the UI thread.
        Dim plan As RenderPlanResult = Nothing
        Await _renderGate.WaitAsync()
        Logger.LogLazy(Function() $"[PERF-R] render gate acquired @ {_swR.ElapsedMilliseconds}ms")
        Try
            If host Is _renderHost Then SetRenderTogglesEnabled(False)
            plan = Await Task.Run(Function() BuildRenderPlan(previewVariant, host, state, requestVersion))
        Finally
            If host Is _renderHost Then SetRenderTogglesEnabled(True)
            _renderGate.Release()
        End Try
        Logger.LogLazy(Function() $"[PERF-R] BuildRenderPlan (Task.Run) returned @ {_swR.ElapsedMilliseconds}ms")
        If requestVersion <> _previewRequestVersion Then Return
        If plan Is Nothing OrElse plan.RenderData Is Nothing OrElse plan.RenderData.Shapes.Count = 0 Then
            SetStatus($"No meshes found{NpcManagerFormat.BuildWarningSuffix(plan?.RenderData?.Warnings)}")
            Return
        End If
        Dim renderData = plan.RenderData
        Dim inst = plan.Inst
        Dim headInst = plan.HeadInst
        Dim skelByArma = plan.SkelByArma
        Dim sculptByArma = plan.SculptByArma
        Dim shapeToSkel = plan.ShapeToSkel
        Dim request = plan.Request
        ' Hide all shapes (RenderHide=True) during load + face tint composition so the user
        ' doesn't see a flash of untinted face. The control stays Visible so GL keeps processing
        ' texture uploads + FBO compositing (tint pipeline NEEDS the control rendering to work).
        ' Cleared by the post-texture-upload hook below (success path → ApplyFaceTintOverlay +
        ' RevealAllShapes) or by the watchdog timeout fallback (textures never finished loading).
        For Each sh In renderData.Shapes
            sh.RenderHide = True
        Next

        ' Wire the post-texture-upload hook BEFORE invoking the render pipeline so the watchdog
        ' deadline armed inside LoadTexturesAsync sees the action. The library guarantees the
        ' callback fires exactly once, on the GL thread, when:
        '   • all background diffuse uploads finished (success branch — bake passes run with
        '     every diffuse already in Textures_Dictionary, then the shapes are revealed),
        '   • OR the deadline elapsed without completion (timeout branch — reveal anyway so
        '     the user sees an untinted preview rather than a permanently blank canvas).
        ' Replaces the legacy PendingTintTimer polling: same observable behaviour, single
        ' source of truth in the render pipeline, no per-app timer machinery.
        Dim capturedState = state
        Dim capturedRenderData = renderData
        Dim capturedHost = host
        ' [TEST: fastpath-skin-softlight] Token check — if the user switches NPCs while the
        ' async upload is pending, _previewRequestVersion advances and this hook would otherwise
        ' bake tints/softlight onto the new NPC's textures using this NPC's state.
        Dim capturedRequestVersion = requestVersion
        host.PreviewCtl.Intent.PostTextureUploadAction = Sub(model)
                                                             If capturedHost Is Nothing OrElse capturedHost.IsDisposed Then Return
                                                             If capturedRequestVersion <> _previewRequestVersion Then Return
                                                             _faceTintResolver.ApplyFaceTintOverlay(capturedState, capturedRenderData, capturedHost)
                                                             UpdateSseFoldedToggleAvailability(_faceTintResolver.LastSseFoldWasMandatory)
                                                             RevealAllShapes(capturedHost)
                                                             FinalizeRenderCamera(capturedHost)
                                                         End Sub
        host.PreviewCtl.Intent.PostTextureUploadTimeoutAction = Sub(model)
                                                                    If capturedHost Is Nothing OrElse capturedHost.IsDisposed Then Return
                                                                    If capturedRequestVersion <> _previewRequestVersion Then Return
                                                                    RevealAllShapes(capturedHost)
                                                                    FinalizeRenderCamera(capturedHost)
                                                                End Sub

        host.PreviewCtl.RenderShapes(request)
        Logger.LogLazy(Function() $"[PERF-R] RenderShapes (GL submit) done @ {_swR.ElapsedMilliseconds}ms")

        ' Cache the resolved state + render data + skeleton instance so the morph/pose checkbox
        ' handlers can rebuild the merged pose on demand without re-running the full preview
        ' resolution pipeline. See CheckBoxApplyBoneMorphs_CheckedChanged /
        ' CheckBoxApplyVertexMorphs_CheckedChanged below — they follow the WM granular
        ' Intent.MarkDirty(Pose)/MarkDirty(Morphs) pattern, not a full reload.
        host.LastRenderedState = state
        host.LastRenderData = renderData
        host.LastSkeletonInstance = inst
        host.LastHeadSkeletonInstance = headInst
        host.LastSkelByArma = skelByArma
        host.LastSculptByArma = sculptByArma
        host.LastShapeToSkel = shapeToSkel

        ' Publish the morph-name set of the face chargen TRI so EditFace_Form can filter sliders
        ' to only entries the engine could actually apply. Vanilla data is inconsistent for some
        ' races (e.g. HumanChildRace.MorphValues declares Brow/Chin sliders but the HDPT points
        ' at BaseFemaleHeadChargen.tri which has none of those names). Showing those sliders in
        ' the editor would lie to the user. Find the face shape via the same NifShaderType filter
        ' the tint compositor uses, get its chargen TRI path from renderData, parse the TRI for
        ' its morph names. Empty set on miss/error → editor falls back to "show all".
        host.LastFaceTriMorphNames.Clear()
        Try
            Dim faceTriPath As String = ""
            For Each mesh In host.PreviewCtl.Model.meshes
                If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
                Dim shape = mesh.MeshData.Shape
                If shape Is Nothing Then Continue For
                Dim mb = mesh.MeshData.Material.MaterialBase
                If mb Is Nothing Then Continue For
                If mb.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
                If renderData.ShapeChargenTriPaths.TryGetValue(shape, faceTriPath) Then Exit For
            Next
            If Not String.IsNullOrEmpty(faceTriPath) Then
                Dim normTri = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(faceTriPath)
                ' Parse the chargen TRI's morph names once per unique path (deterministic per file);
                ' most actors of a race share BaseFemale/MaleHeadChargen.tri, so this collapses a
                ' per-render BA2 decompress + TriHeadParser to a single cache lookup.
                Dim cachedNames As HashSet(Of String) = _faceTriMorphNamesCache.GetOrAdd(
                    normTri,
                    Function(key)
                        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                        Dim triLoc As FilesDictionary_class.File_Location = Nothing
                        If FilesDictionary_class.Dictionary.TryGetValue(key, triLoc) Then
                            Dim triBytes = triLoc.GetBytes()
                            If triBytes IsNot Nothing AndAlso triBytes.Length > 0 Then
                                Dim triHead = TriHeadParser.ParseTriHeadFromBytes(triBytes)
                                If triHead IsNot Nothing AndAlso triHead.Morphs IsNot Nothing Then
                                    For Each m In triHead.Morphs
                                        If Not String.IsNullOrEmpty(m.Name) Then names.Add(m.Name)
                                    Next
                                End If
                            End If
                        End If
                        Return names
                    End Function)
                For Each n In cachedNames
                    host.LastFaceTriMorphNames.Add(n)
                Next
            End If
        Catch ex As Exception
        End Try

        ' Gate the Edit Body button on whether this race + body actually has any editable channels.
        ' Some races (Ghoul, PowerArmorRace, custom robots) declare no BSMS WeightScale or Range
        ' Modifier and have no body PIRT .tri — opening the editor would show three empty panels.
        UpdateEditBodyEnabled()
        ' Gate the Edit Face button by the same shape as Edit Body: section availability per race
        ' + gender. Skipped entirely when no head parts, no hair colors, no morph presets in the
        ' loaded TRI, no tint groups, and no FacialBoneRegions JSON — opening the editor would
        ' show only empty pickers. La regla vive en UpdateEditFaceEnabled (RaceUtil.RaceSupportsFaceGen).
        UpdateEditFaceEnabled()
        ' Gate the Edit Outfit button: enabled when an NPC with a race is loaded and the load order
        ' has any outfit. The per-race candidate filter is deferred to picker-open (GetOutfitCandidates).
        UpdateEditOutfitEnabled()
        ' Gate the NPC (Traits) editor: enabled whenever a real NPC with a FormID is loaded.
        UpdateEditNpcEnabled()

        ' Face tint compositing + RevealAllShapes are sequenced by the PostTextureUploadAction
        ' wired before RenderShapes (above). The library invokes them on the GL thread once the
        ' background diffuse uploads complete, so the bake passes always see populated Textures_Dictionary
        ' entries. RevealAllShapes is called inside the hook — shapes stay RenderHide=True until then.

        SetStatus($"Rendered {previewVariant.DisplayName} ({renderData.Shapes.Count} shapes)")
        Logger.LogLazy(Function() $"[PERF-R] ========== RenderCurrentStateAsync TOTAL = {_swR.ElapsedMilliseconds}ms ==========")
    End Function

    ''' <summary>Output of <see cref="BuildRenderPlan"/>: the CPU-computed render data + skeleton/mount
    ''' state that the UI-thread tail of RenderCurrentStateAsync submits to GL.</summary>
    Private NotInheritable Class RenderPlanResult
        Public RenderData As PreviewResolutionResult
        Public Inst As SkeletonInstance
        Public HeadInst As SkeletonInstance
        Public SkelByArma As Dictionary(Of UInteger, SkeletonInstance)
        Public SculptByArma As Dictionary(Of UInteger, Dictionary(Of String, System.Numerics.Vector3))
        Public ShapeToSkel As Dictionary(Of IRenderableShape, SkeletonInstance)
        Public Request As RenderRequest
    End Class

    ''' <summary>CPU compute half of RenderCurrentStateAsync (Finding 2-core): resolves the preview
    ''' variant, builds the merged pose + per-ARMA skeletons, runs the robot-chunk/socket mounting, and
    ''' assembles the RenderRequest. PURE CPU — no WinForms controls, no GL/host.PreviewCtl — so the caller
    ''' runs it on Task.Run, keeping only the GL submission (RenderShapes) + UI gating on the UI thread.
    ''' Returns Nothing if the request was superseded; a result whose RenderData has no shapes signals 'no
    ''' meshes' to the caller (which shows the status + returns).</summary>
    Private Function BuildRenderPlan(previewVariant As PreviewVariantDefinition, host As NpcRenderHost, state As NPCVisualState, requestVersion As Integer) As RenderPlanResult
        Dim _swBrp As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)
        Dim renderData = _meshCollector.ResolvePreviewVariant(previewVariant)
        Logger.LogLazy(Function() $"[PERF-BRP] ResolvePreviewVariant @ {_swBrp.ElapsedMilliseconds}ms ({renderData?.Shapes?.Count} shapes)")
        If requestVersion <> _previewRequestVersion Then Return Nothing

        If renderData Is Nothing OrElse renderData.Shapes.Count = 0 Then
            Return New RenderPlanResult With {.RenderData = renderData}
        End If

        ' LM body overlays ("tattoos"): resolve the applied preset's Overlays into per-shape
        ' IRenderableShape.OverlayLayers on the SKIN shapes. Runs HERE — on this Task.Run background
        ' thread, after renderData is settled and before the RenderRequest captures renderData.Shapes
        ' (line below: .Shapes = renderData.Shapes) — so the layers travel with the exact shapes that get
        ' rendered. It loads materials via FilesDictionary, the same off-UI-thread material load the base
        ' shapes already did during ResolvePreviewVariant, so it's thread-safe in this context. The method
        ' clears OverlayLayers on every shape when the NPC has no overlays, so switching/clearing an NPC
        ' never leaks a previous NPC's tattoos (and each plan rebuilds fresh shape instances anyway).
        ' este mismo NPC se resuelve para el preview principal (no los dibuja) y para el del editor (sí). Pasar
        ' `_hostProvider()` acá haría que el editor viera lo del principal.
        _morphPoseResolver.ResolveOverlayLayers(state, renderData, host)

        ' Dos checkboxes independientes controlan la pose osea (FMRS) y los morphs de vertice (TRI de chargen).
        ' Los dos se honran en el render completo inicial; los toggles posteriores los manejan sus
        ' CheckedChanged por el flujo granular de MarkDirty, no por un reload completo. El gating fino vive en
        ' BuildCompositeMorphResolver (cara y cuerpo por separado) y no hace falta un AND maestro aca: el
        ' composite devuelve Nothing solo cuando las dos subsecciones estan destildadas.
        ' boneMorphsEnabled alimenta UNICAMENTE la pose FMRS de los huesos de la cara; el escalado por peso
        ' corporal es el toggle aparte bodyWeightEnabled. Con "Show other gender" los deltas FMRS son los del
        ' genero propio del NPC de origen y no corresponden sobre una cabeza del genero destino, asi que se
        ' suprimen (el peso corporal sigue).
        Dim boneMorphsEnabled = host.Toggles.ApplyBoneMorphs AndAlso Not host.PreviewGenderOverride.HasValue
        ' Mismo checkbox, sin el AND del gender-override: bajo Skyrim ese canal gatea los node transforms de
        ' RaceMenu (escala/pos/rot por nodo del cuerpo), que no son gender-específicos como los FMRS.
        Dim nodeXfEnabled = host.Toggles.ApplyBoneMorphs
        ' "FaceGeom en memoria": con el gate ON el collector NO redirigió, así que se dibuja la malla PLANA
        ' y hay que entregarle las posiciones horneadas como geometría base. Nothing con el gate OFF.
        ' ANTES del composite: BuildCompositeMorphResolver lo lee de host.LastHeadBakeService para
        ' filtrar los canales de posición de las shapes gateadas (si no, doble aplicación del chargen).
        Dim headBake = BuildHeadBakeService(state, renderData, host)
        host.LastHeadBakeService = headBake
        Logger.LogLazy(Function() $"[PERF-BRP] headBakeService @ {_swBrp.ElapsedMilliseconds}ms ({If(headBake Is Nothing, 0, headBake.RegisteredCount)} shapes)")
        Dim morphResolver = BuildCompositeMorphResolver(state, renderData, host)
        Logger.LogLazy(Function() $"[PERF-BRP] morphResolver @ {_swBrp.ElapsedMilliseconds}ms")

        ' Build a pose carrying the FMRI/FMRS face bone deltas (each region's bones become
        ' PoseTransformData entries). This pose is applied via SkeletonInstance.ApplyPose which
        ' sets DeltaTransform on each bone — the same mechanism body poses use. The checkbox
        ' toggle lets the user compare "raw face" (no pose, no morphs) vs "with FMRS applied" live.
        Dim bodyWeightEnabled = host.Toggles.ApplyBodyWeight
        Dim sculptEnabled = host.Toggles.ApplySculpt

        ' Per-ARMA skeleton flow: each shape goes to a SkeletonInstance with its own ARMA's sculpt applied
        ' (if any), or to the base instance (no sculpt) if its ARMA has none. Generic for ANY ARMA — body /
        ' outfit / gloves / underarmor / etc. all follow the same rule. Multiple shapes from the same NIF
        ' share the same skeleton (cached by ArmorAddonFormID).
        ' Sculpt formula: H3 multiplicative (s = race_s · (1 + arma_d)) hardcoded — A REVISAR: la fórmula
        ' correcta del engine no está confirmada vs CK (H3 es la candidata conceptual más limpia, cumple
        ' invariantes naturales, pero sin verificación pixel-match). NO reintroducir un dropdown
        ' experimental de fórmulas como "arreglo": un caso que lo motivó eran OMODs/add-ons no
        ' renderizados, no la fórmula.
        Dim inst = PrepareSkeleton(state, renderData)

        ' [PIPBOY-DIAG] dump del skeleton actor para ver si trae el socket donde el Pipboy NIF
        ' espera mounting. Convención vanilla: el host (actor skeleton) expone "P-X" como NiNode,
        ' el cliente (Pipboy NIF) declara "C-X" en BSConnectPoint::Children y el mount-resolver
        ' los matchea. Loguemos cualquier key que contenga "Pip" (case-insensitive) o que arranque
        ' con "P-" (cualquier P- socket). Si no hay nada → no hay punto de mounting → el render
        ' debe inventarlo (anclar a LArm_skin, etc.) o dejar el Pipboy en el origen.
        If Logger.Enabled Then
            Dim skelKeys = inst.SkeletonDictionary.Keys.ToList()
            Dim pipMatches = skelKeys.Where(Function(k) k.Contains("pip", StringComparison.OrdinalIgnoreCase)).OrderBy(Function(k) k).ToList()
            Dim pSocketMatches = skelKeys.Where(Function(k) k.StartsWith("P-", StringComparison.OrdinalIgnoreCase)).OrderBy(Function(k) k).ToList()
            Logger.LogLazy(Function() $"[PIPBOY-DIAG] skeleton total-bones={skelKeys.Count} pip-related-keys=[{String.Join(",", pipMatches)}] P-prefix-keys=[{String.Join(",", pSocketMatches)}]")
        End If

        ' Pipboy synthetic-skin: corre ahora que `inst` está construido. Descubre el bone target
        ' dinámicamente del SkeletonDictionary (case-insensitive match contra "pipboy"). Sin
        ' hardcoding: razas distintas (Ghoul, Child, Synth) pueden traer otro nombre o ninguno.
        _mountingResolver.ApplyPipboySyntheticSkin(renderData, inst)
        ' SSE: rigid biped items (shield slot 39, etc.) anchored to their Prn-named skeleton node.
        _mountingResolver.ApplyPrnRigidAttach(renderData, inst)

        Dim basePose = _morphPoseResolver.BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled,
                                          inst, Nothing, nodeTransformsEnabled:=nodeXfEnabled)  ' Nothing = no sculpt → base pose
        ' Bone-morphs → capa MorphDeltaTransform (deja libre la capa pose/animación).
        inst.ApplyBoneMorphPose(basePose)
        _morphPoseResolver.ApplyNeckNnamCompensation(inst)

        ' Head skeleton: SAME morph/FMRS pose as `inst` WITH body weight AND NNAM neck-fat. Body weight
        ' scales the _skin LEAF bones (Head_skin/Face_skin/Neck1_skin + shared neck/chest), which do NOT
        ' propagate. NNAM escala el hueso ANCESTRO "Neck": sin compensar, propaga [1+nnam,1+nnam,1] a toda
        ' la cara (balloon). ApplyNeckNnamCompensation (post-pase) compensa a TODOS los hijos directos de
        ' "Neck" (comp = L_C⁻¹∘S⁻¹∘L_C ∘ FMRS, con shear), dejando la escala solo en los verts del "Neck"
        ' → la cara NO se infla y los FMRS del cuello quedan.
        ' Head-part shapes are routed here (loop below); animation frames applied too (ApplyAnimFrame). Built
        ' unconditionally + separate so it stays in sync with the BODY skeleton. No chunk/pipboy injection.
        Dim headInst = PrepareSkeleton(state, renderData)
        Dim headPose = _morphPoseResolver.BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled, headInst, Nothing,
                                                             nodeTransformsEnabled:=nodeXfEnabled)
        headInst.ApplyBoneMorphPose(headPose)
        _morphPoseResolver.ApplyNeckNnamCompensation(headInst)
        Logger.LogLazy(Function() $"[PERF-BRP] initial PrepareSkeleton+BuildMergedNpcPose (+head) @ {_swBrp.ElapsedMilliseconds}ms")

        ' DIAG POST-PASE: dump del estado de los bones inyectados de chunks robot DESPUÉS de ApplyPose.
        Dim shapeToSkel As New Dictionary(Of IRenderableShape, SkeletonInstance)
        Dim skelByArma As New Dictionary(Of UInteger, SkeletonInstance)
        Dim sculptByArma As New Dictionary(Of UInteger, Dictionary(Of String, System.Numerics.Vector3))
        For Each shape In renderData.Shapes
            Dim cat As ShapeRenderCategory = ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat = ShapeRenderCategory.HeadPart Then
                ' Head parts → head skeleton (lleva body-weight en hojas _skin + NNAM compensado:
                ' la escala del "Neck" queda en sus verts, no se propaga a la cara).
                shapeToSkel(shape) = headInst
                Continue For
            End If
            Dim armaFormID As UInteger = 0
            renderData.ShapeArmaFormID.TryGetValue(shape, armaFormID)
            Dim sculpt As Dictionary(Of String, System.Numerics.Vector3) = Nothing
            renderData.ShapeArmaSculpt.TryGetValue(shape, sculpt)
            If sculpt Is Nothing OrElse sculpt.Count = 0 OrElse Not sculptEnabled Then
                ' Sin sculpt o sculpt-toggle OFF → skeleton base compartido. BW es independiente:
                ' Sclpt=ON + BW=OFF construye igual el per-ARMA skel pero con sólo capa 4 ARMA.
                shapeToSkel(shape) = inst
                Continue For
            End If
            Dim armaSkel As SkeletonInstance = Nothing
            If Not skelByArma.TryGetValue(armaFormID, armaSkel) Then
                armaSkel = PrepareSkeleton(state, renderData)
                Dim poseForArma = _morphPoseResolver.BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled,
                                                     armaSkel, sculpt, nodeTransformsEnabled:=nodeXfEnabled)
                armaSkel.ApplyBoneMorphPose(poseForArma)
                _morphPoseResolver.ApplyNeckNnamCompensation(armaSkel)
                skelByArma(armaFormID) = armaSkel
                sculptByArma(armaFormID) = sculpt
            End If
            shapeToSkel(shape) = armaSkel
        Next
        Logger.LogLazy(Function() $"[PERF-BRP] per-ARMA skeletons ({skelByArma.Count}) @ {_swBrp.ElapsedMilliseconds}ms")

        ' Per-shape meatcap classification by geometry: read BSSubIndexTriShape segmentation,
        ' classify each shape via ClassifyShapeMeatcap (Confirmed = NIF enum SECTIONCAP/TORSOCAP,
        ' Tentative = BS-OS-only "Gore" range 100/102/103). Complementary to the candidate-based
        ' marking in ApplyShapeGeometry which sets ShapeMeatcap=Confirmed for HDPT type=7.
        ' This loop only writes when classification != Normal so the candidate-side mark for
        ' headpart meatcaps is preserved if the geometry doesn't also flag itself.
        ' Per-shape Try/Catch: one shape with bad geometry must NOT abort classification for the
        ' rest (a single throw used to silently skip every remaining shape). Isolate per shape and
        ' log the offender so the bad geometry is diagnosable instead of swallowed.
        For Each sh In renderData.Shapes
            Try
                Dim cls = ShapeMeatcapClassifier.ClassifyShapeMeatcap(sh.Geometry)
                If cls <> MeatcapClassification.Normal Then
                    renderData.ShapeMeatcap(sh) = cls
                End If
            Catch ex As Exception
                Dim shapeNameCopy = sh.ShapeName
                Logger.LogLazy(Function() $"[MEATCAP-CLASSIFY] shape='{shapeNameCopy}' classification failed (skipped): {ex.GetType().Name}: {ex.Message}")
            End Try
        Next

        Dim skelResolver As ISkeletonResolver = New MultiInstanceSkeletonResolver(shapeToSkel, inst)

        ' Inyecta al esqueleto del actor los huesos internos de los chunks montados por BSConnectPoint
        ' (robot, weapon mods, piezas de power armor): los que no existen en el actor se agregan anclados al
        ' hueso padre del socket, para que el skinning los encuentre por el diccionario y produzca el mundo
        ' correcto en actor-space.
        ' ORDEN TOPOLÓGICO, no arbitrario: el host materializa sus sockets PRIMERO (los chunks pueden
        ' depender de ellos), y por cada chunk va primero INJECT y después MATERIALIZE, porque sus sockets
        ' pueden anclarse a los huesos internos recién creados. A va antes que B si B consume un sub-socket
        ' que expone A; si son independientes, el orden da igual.
        Dim shapesWithSocket As New List(Of IRenderableShape)
        For Each sh In renderData.Shapes
            If sh.NifContent Is Nothing Then Continue For
            Dim socket As BSConnectPointReader.ConnectPointInfo = Nothing
            If renderData.ShapeMountSocket.TryGetValue(sh, socket) Then shapesWithSocket.Add(sh)
        Next

        ' Build per-shape sets de tails: childTails (consumidos) y parentTails (expuestos).
        Dim childTailsByShape As New Dictionary(Of IRenderableShape, HashSet(Of String))
        Dim parentTailsByShape As New Dictionary(Of IRenderableShape, HashSet(Of String))
        For Each sh In shapesWithSocket
            Dim ct As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each cn In BSConnectPointReader.ReadChildrenNames(sh.NifContent)
                Dim tail = cn
                If cn.Length >= 2 AndAlso (cn.StartsWith("C-", StringComparison.OrdinalIgnoreCase) OrElse cn.StartsWith("C_", StringComparison.OrdinalIgnoreCase)) Then
                    tail = cn.Substring(2)
                End If
                ct.Add(tail)
            Next
            childTailsByShape(sh) = ct
            Dim pt As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each p In BSConnectPointReader.ReadParents(sh.NifContent)
                Dim n = If(p?.Name, "")
                Dim tail = n
                If n.Length >= 2 AndAlso (n.StartsWith("P-", StringComparison.OrdinalIgnoreCase) OrElse n.StartsWith("P_", StringComparison.OrdinalIgnoreCase)) Then
                    tail = n.Substring(2)
                End If
                If Not String.IsNullOrEmpty(tail) Then pt.Add(tail)
            Next
            parentTailsByShape(sh) = pt
        Next

        ' Kahn topological sort. inDegree[B] = #shapes A tales que B consume P-X expuesto por A.
        Dim inDegree As New Dictionary(Of IRenderableShape, Integer)
        Dim edgesFrom As New Dictionary(Of IRenderableShape, List(Of IRenderableShape))
        For Each sh In shapesWithSocket
            inDegree(sh) = 0
            edgesFrom(sh) = New List(Of IRenderableShape)
        Next
        For Each a In shapesWithSocket
            For Each b In shapesWithSocket
                If a Is b Then Continue For
                ' a expone tails que b consume?
                Dim aPar As HashSet(Of String) = parentTailsByShape(a)
                Dim bCh As HashSet(Of String) = childTailsByShape(b)
                Dim dep As Boolean = False
                For Each t In aPar
                    If bCh.Contains(t) Then dep = True : Exit For
                Next
                If dep Then
                    edgesFrom(a).Add(b)
                    inDegree(b) = inDegree(b) + 1
                End If
            Next
        Next
        Dim ordered As New List(Of IRenderableShape)
        Dim queue As New Queue(Of IRenderableShape)
        For Each sh In shapesWithSocket
            If inDegree(sh) = 0 Then queue.Enqueue(sh)
        Next
        While queue.Count > 0
            Dim cur = queue.Dequeue()
            ordered.Add(cur)
            For Each nxt In edgesFrom(cur)
                inDegree(nxt) -= 1
                If inDegree(nxt) = 0 Then queue.Enqueue(nxt)
            Next
        End While
        ' Si quedan shapes (ciclo en grafo), agregarlas al final igual (orden best-effort).
        If ordered.Count < shapesWithSocket.Count Then
            For Each sh In shapesWithSocket
                If Not ordered.Contains(sh) Then ordered.Add(sh)
            Next
        End If

        ' DIAG (multi-socket Mr Handy): dumpear estado del skeleton antes/después de inject
        ' para entender qué bones ya están + qué nombres aporta cada chunk + si hay colisión
        ' por idempotencia. Filtrado por chunks robot solo (state.HasObjectTemplate) para no
        ' contaminar logs de humanoides normales.
        ' NOTA: isRobotMount es SOLO filtro de verbosidad de diagnósticos — la aplicación V2
        ' SKEL-OVERRIDE NO depende de este flag: V2 aplica a robot Y biped por igual (biped corre
        ' V2 sin emitir estos logs verbosos).
        Dim isRobotMount = state.HasObjectTemplate AndAlso shapesWithSocket.Count > 0
        If isRobotMount AndAlso Logger.Enabled Then
            ' [DIAG-PRE-INJECT] Dump bones del actor ANTES de cualquier inject de chunk.
            Dim preKeys = inst.SkeletonDictionary.Keys.OrderBy(Function(k) k).ToList()
            Dim preCountAll = preKeys.Count
            Logger.LogLazy(Function() $"[PRE-INJECT-SKEL] actor skeleton bones count={preCountAll}")
            For Each k In preKeys
                Dim kn = k
                Logger.LogLazy(Function() $"[PRE-INJECT-SKEL]   '{kn}'")
            Next

            ' Per-shape: clasificar bones del chunk en in-dict (idempotent skip) vs needs-inject.
            For Each sh In shapesWithSocket
                Dim shapeNameLog = sh.ShapeName
                Dim sharedBones As New List(Of String)
                Dim newBones As New List(Of String)
                If sh.ShapeBones IsNot Nothing Then
                    For Each bn In sh.ShapeBones
                        Dim niNode = TryCast(bn, NiflySharp.Blocks.NiNode)
                        If niNode Is Nothing Then Continue For
                        Dim boneName = If(niNode.Name?.String, "<null>")
                        If inst.SkeletonDictionary.ContainsKey(boneName) Then
                            sharedBones.Add(boneName)
                        Else
                            newBones.Add(boneName)
                        End If
                    Next
                End If
                Dim sharedStr = String.Join(", ", sharedBones)
                Dim newStr = String.Join(", ", newBones)
                Dim sharedCount = sharedBones.Count
                Dim newCount = newBones.Count
                Logger.LogLazy(Function() $"[PRE-INJECT-CLASSIFY] shape='{shapeNameLog}' shared(idempotent-skip)={sharedCount} needsInject={newCount}")
                Logger.LogLazy(Function() $"[PRE-INJECT-CLASSIFY]   shared=[{sharedStr}]")
                Logger.LogLazy(Function() $"[PRE-INJECT-CLASSIFY]   needsInject=[{newStr}]")
            Next
        End If

        ' Host NIF: materializa sockets nivel-1 (C-X cuyo P-X vive en el host).
        If inst.Skeleton IsNot Nothing Then
            Dim preHostKeys = New HashSet(Of String)(inst.SkeletonDictionary.Keys, StringComparer.OrdinalIgnoreCase)
            Dim addedHost = BSConnectPointBoneInjector_Class.MaterializeSocketsAsConnectPointBones(inst.Skeleton, inst)
            If isRobotMount AndAlso Logger.Enabled Then
                Dim postNew = inst.SkeletonDictionary.Keys.Where(Function(k) Not preHostKeys.Contains(k)).OrderBy(Function(k) k).ToList()
                Dim addedHostLog = addedHost
                Logger.LogLazy(Function() $"[MATERIALIZE-HOST] from inst.Skeleton (host NIF): added={addedHostLog} newBones=[{String.Join(", ", postNew)}]")
                For Each nbName In postNew
                    Dim n = nbName
                    Dim hb As HierarchiBone_class = Nothing
                    inst.SkeletonDictionary.TryGetValue(n, hb)
                    If hb IsNot Nothing Then
                        Dim localT = hb.OriginalLocaLTransform
                        Dim worldT = hb.OriginalGetGlobalTransform
                        Dim lr = localT.Rotation, wr = worldT.Rotation
                        Logger.LogLazy(Function() $"[MATERIALIZE-HOST]   '{n}' parent='{hb.Parent?.BoneName}' local: T=({localT.Translation.X:F3},{localT.Translation.Y:F3},{localT.Translation.Z:F3}) S={localT.Scale:F3} R=[{lr.M11:F3},{lr.M12:F3},{lr.M13:F3}|{lr.M21:F3},{lr.M22:F3},{lr.M23:F3}|{lr.M31:F3},{lr.M32:F3},{lr.M33:F3}]")
                        Logger.LogLazy(Function() $"[MATERIALIZE-HOST]   '{n}' world: T=({worldT.Translation.X:F3},{worldT.Translation.Y:F3},{worldT.Translation.Z:F3}) S={worldT.Scale:F3} R=[{wr.M11:F3},{wr.M12:F3},{wr.M13:F3}|{wr.M21:F3},{wr.M22:F3},{wr.M23:F3}|{wr.M31:F3},{wr.M32:F3},{wr.M33:F3}]")
                    End If
                Next
            End If
        End If

        ' Inject + materialize por shape en orden topológico. processedNifs idempotencia para
        ' chunks que aparecen en múltiples shapes (mismo NIF, varios shapes adentro).
        ' [PER-SKEL] La clave es (targetSkel, NIF): un mismo NIF puede necesitar materializarse
        ' en el base inst Y en un clone per-ARMA si shapes del mismo NIF rutean a skels distintos
        ' (mount shape sculpted → clone). Sin la clave per-skel, el clone quedaría sin sockets.
        Dim processedNifs As New HashSet(Of (SkeletonInstance, Nifcontent_Class_Manolo))
        Dim totalInjected As Integer = 0
        ' [DIAG-ACCUMULATION] Tracking de W_B por bone name a través de chunks. Si un sub-chunk
        ' reskinea un bone cuyo nombre ya fue reskin-eado por un chunk previo, loggear el delta
        ' entre actor.B.world (que usamos) y prev_W_B (donde el geometry previo renderiza). Test
        ' de la hipótesis del usuario: armor de pierna se reskinea contra actor.LThigh.world del
        ' skeleton.nif, no contra W_B(LThigh) que produjo LegsAssaultron. Solo log, no afecta render.
        ' [PER-SKEL] chunkWBHistory es per-target-skel: la cascade cross-shape (un chunk montando
        ' sobre un bone que otro chunk corrigió) solo tiene sentido dentro del MISMO skeleton.
        ' Mount shapes que rutean a clones sculpt acumulan su W_B en el dict del clone, no del base.
        ' Para shapes base-targeted (la mayoría), targetSkel = inst → un solo dict como antes.
        Dim chunkWBHistoryBySkel As New Dictionary(Of SkeletonInstance, Dictionary(Of String, Transform_Class))
        ' [PER-SKEL HOST-MATERIALIZE] El host materialize (sockets P-X/C-X del skeleton.nif del actor)
        ' corre arriba SOLO sobre `inst` base. Un clone per-ARMA (PrepareSkeleton) NO trae esos host
        ' sockets. Si una mount shape rutea a un clone, ese clone necesita el mismo contexto
        ' estructural host-level. Materializamos host sockets lazily por skel la primera vez que una
        ' mount shape lo toca. `inst` ya está materializado (bloque MATERIALIZE-HOST de arriba).
        Dim hostMaterializedSkels As New HashSet(Of SkeletonInstance) From {inst}

        ' [HOST-SCOPED A_HOST] Cache de candidates por ORDINAL runtime. Construido desde
        ' renderData.CandidateNif.Keys (todos los candidates con NIF cargado, NO solo los
        ' con shapes renderizables). Eso desacopla la identidad de instancia host de la
        ' shape materialization — un host que publica sockets pero no emite shapes propias
        ' igual aparece en este cache y puede recibir ChunkToActor lazy via EnsureChunkToActor.
        Dim _candByOrdinal As New Dictionary(Of Integer, MeshCandidate)
        For Each _cv In renderData.CandidateNif.Keys
            If _cv Is Nothing OrElse _cv.ChunkInstanceOrdinal = 0 Then Continue For
            Dim _key = _cv.ChunkInstanceOrdinal
            Dim _existing As MeshCandidate = Nothing
            If _candByOrdinal.TryGetValue(_key, _existing) Then
                If Not ReferenceEquals(_existing, _cv) Then
                    Dim kOrdL = _key, oldNmL = If(_existing.MountSocket?.Name, "?"), newNmL = If(_cv.MountSocket?.Name, "?"), oldFidL = _existing.ChunkOmodFormID, newFidL = _cv.ChunkOmodFormID
                    Logger.LogLazy(Function() $"[CAND-BY-ORDINAL-OVERWRITE] ordinal={kOrdL} existing.socket='{oldNmL}' (0x{oldFidL:X8}) new.socket='{newNmL}' (0x{newFidL:X8}) — bug serio: ordinal debería ser único por construcción")
                End If
            End If
            _candByOrdinal(_key) = _cv
        Next

        ' [VISITING-SET] DFS cycle detection para EnsureChunkToActor recursivo. Ordinals
        ' actualmente en stack de cómputo se pushean acá; si recursión llega a uno ya
        ' presente, es ciclo real (loggeado, fallback Path B). Standard DFS coloring
        ' (gray = visiting). Push/pop en EnsureChunkToActor via Try/Finally.
        Dim ensureVisiting As New HashSet(Of Integer)

        For Each shape In ordered
            Dim socket As BSConnectPointReader.ConnectPointInfo = Nothing
            If Not renderData.ShapeMountSocket.TryGetValue(shape, socket) Then Continue For

            ' [PER-SKEL] Target skel real de esta shape: clone per-ARMA si es sculpted mount, sino
            ' base inst. TODO el procesamiento de chunk-mount (inject, materialize, JIT, V2 collect)
            ' opera sobre targetSkel — no sobre inst hardcoded — para que mount shapes ruteadas a
            ' clones reciban sus bones + MountDelta. Para la mayoría (base-targeted) targetSkel = inst.
            Dim targetSkel As SkeletonInstance = Nothing
            If Not shapeToSkel.TryGetValue(shape, targetSkel) OrElse targetSkel Is Nothing Then targetSkel = inst
            Dim targetWB As Dictionary(Of String, Transform_Class) = Nothing
            If Not chunkWBHistoryBySkel.TryGetValue(targetSkel, targetWB) Then
                targetWB = New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
                chunkWBHistoryBySkel(targetSkel) = targetWB
            End If
            ' [PER-SKEL HOST-MATERIALIZE] Si esta shape rutea a un clone que aún no recibió el host
            ' materialize (sockets del skeleton.nif del actor), hacerlo ahora — para que el clone
            ' tenga el mismo contexto estructural host-level que el base inst antes de inject/V2.
            If Not hostMaterializedSkels.Contains(targetSkel) AndAlso targetSkel.Skeleton IsNot Nothing Then
                BSConnectPointBoneInjector_Class.MaterializeSocketsAsConnectPointBones(targetSkel.Skeleton, targetSkel)
                hostMaterializedSkels.Add(targetSkel)
                Dim shHM = shape.ShapeName
                Logger.LogLazy(Function() $"[MATERIALIZE-HOST-CLONE] host sockets materializados sobre clone per-ARMA para mount shape '{shHM}'")
            End If

            ' [SOCKET-EFFECTIVE-OVERRIDE] Para chunks downstream del
            ' shape loop (INJECT, FAKE-SKIN, V2 reskin path B), usar el SkeletonFallbackSocket
            ' del cand cuando existe — su parent bone está en nomenclatura actor.skel (indexed:
            ' Arm1|0/1/2) en lugar del publisher chunk socket cuyo parent es chunk-internal
            ' (Arm1 sin suffix). Plus apIdx-substitution del suffix '|N' del parent para
            ' matchear el consumer apIdx (caso vivo Codsworth Mr Handy ModArmsHandyAR1A
            ' apIdx=1 que necesita Arm1|1, no el Arm1|0 default del skeleton publish).
            '
            ' Cuando NO hay SkeletonFallbackSocket (cand fue creado fuera del robot path o
            ' skeleton no publica el socket), mantenemos el socket original como antes.
            Dim _candForSocket As MeshCandidate = Nothing
            If renderData.ShapeCandidate.TryGetValue(shape, _candForSocket) AndAlso _candForSocket IsNot Nothing AndAlso _candForSocket.SkeletonFallbackSocket IsNot Nothing Then
                Dim skelFb = _candForSocket.SkeletonFallbackSocket
                Dim effectiveParent = If(skelFb.ParentBoneName, "")
                ' apIdx substitution: si parent termina en '|N' numérico y consumer apIdx != 0,
                ' sustituir N por consumer apIdx.
                If _candForSocket.MountApIdx <> 0 AndAlso Not String.IsNullOrEmpty(effectiveParent) Then
                    Dim pipe = effectiveParent.LastIndexOf("|"c)
                    If pipe > 0 AndAlso pipe < effectiveParent.Length - 1 Then
                        Dim sfx = effectiveParent.Substring(pipe + 1)
                        Dim allDigits As Boolean = True
                        For Each c In sfx
                            If Not Char.IsDigit(c) Then allDigits = False : Exit For
                        Next
                        If allDigits Then
                            effectiveParent = String.Concat(effectiveParent.AsSpan(0, pipe + 1), _candForSocket.MountApIdx.ToString())
                        End If
                    End If
                End If
                Dim effectiveSocket As New BSConnectPointReader.ConnectPointInfo With {
                    .Name = skelFb.Name,
                    .ParentBoneName = effectiveParent,
                    .Translation = skelFb.Translation,
                    .Rotation = skelFb.Rotation,
                    .Scale = skelFb.Scale
                }
                If isRobotMount AndAlso Not String.Equals(effectiveSocket.ParentBoneName, socket.ParentBoneName, StringComparison.Ordinal) Then
                    Dim shL = shape.ShapeName, origParL = If(socket.ParentBoneName, ""), newParL = effectiveParent, apL = _candForSocket.MountApIdx
                    Logger.LogLazy(Function() $"[SOCKET-EFFECTIVE-OVERRIDE] shape='{shL}' apIdx={apL} parent '{origParL}' (publisher) → '{newParL}' (skeleton+apIdx-sub)")
                End If
                socket = effectiveSocket
            End If

            ' [A_HOST-JIT] Thin caller a EnsureChunkToActor. Extrae el compute de
            ' ChunkToActor del shape loop. Si _cand es robot mount y ChunkToActor no está
            ' set, EnsureChunkToActor lo computa lazy + resuelve recursivamente la cadena
            ' de hosts (cada host es ensured antes que sus consumers via recursión).
            ' Cubre el caso "host publisher sin shapes" — ese host nunca pasa por este JIT
            ' pero recibe ChunkToActor cuando algún descendant con shapes lo requiere.
            Try
                Dim _cand As MeshCandidate = Nothing
                renderData.ShapeCandidate.TryGetValue(shape, _cand)
                ' Criterio estructural: el chunk-mount path es 'soy attachment con MountSocket
                ' resuelto', no 'soy de FormType X'. Capas 1+2 (coord fix + socket disambig) y capa 3
                ' (V2) son compartidas entre robot y biped: cualquier shape con MountSocket recibe el
                ' mount vía MountDeltaTransform en los huesos (ApplyMountPlanForActor →
                ' OverrideActorBoneWorld). isRobotMount quedó sólo como filtro de verbosidad.
                If _cand IsNot Nothing AndAlso _cand.Kind = MeshCandidateKind.Attachment AndAlso _cand.MountSocket IsNot Nothing Then
                    _mountingResolver.EnsureChunkToActor(_cand, _candByOrdinal, renderData, targetSkel, targetWB, ensureVisiting)
                End If
            Catch exJit As Exception
                Dim shL = shape.ShapeName, msgL = exJit.Message
                Logger.LogLazy(Function() $"[A_HOST-JIT] shape='{shL}' EXCEPTION: {msgL}")
            End Try

            ' DIAG-PRE-INJECT: dump TODO lo necesario para hacer la matemática del attachment
            ' offline. Para cada chunk con socket loggeamos:
            '   - Skinned flag del BSConnectPoint::Children del chunk
            '   - chunkRoot.local (T/R/S del root NiNode del NIF)
            '   - socket.LocalTransform (T/R quat/S del P-X)
            '   - parent_bone (socket.ParentBone) world transform en actor
            '   - Para cada ShapeBone del chunk: NiNode raw local + cadena hasta chunkRoot +
            '     bind matrix (chunk-frame inverse-bind) + authoring world derivado del bind
            ' Filtra solo chunks de robot OBTE para no contaminar.
            If isRobotMount AndAlso Logger.Enabled Then
                Try
                    Dim shName = shape.ShapeName
                    Dim chunkNif = shape.NifContent
                    Dim childrenInfo = BSConnectPointReader.ReadChildren(chunkNif)
                    Dim skinnedFlag = childrenInfo.Skinned

                    ' [DIAG-CHILDREN-TARGETS] Dump TODOS los PointNames del BSConnectPoint::Children
                    ' del chunk. V2 actualmente solo usa PointNames[0] como cxName. Si hay
                    ' múltiples targets, alguno podría ser el anchor real (= bone weighted root)
                    ' en vez del marker mount point. Diagnostic para validar hipótesis NIF-derived.
                    Try
                        Dim allTargets = If(childrenInfo.PointNames, New List(Of String))
                        Dim targetCount = allTargets.Count
                        Dim targetList = String.Join(", ", allTargets)
                        Dim shNL = shape.ShapeName, tcL = targetCount, tlL = targetList
                        Logger.LogLazy(Function() $"[CHUNK-DIAG] Children targets count={tcL} all=[{tlL}]")
                    Catch
                    End Try
                    Dim chunkRootDiag = chunkNif.GetRootNode()
                    Dim chunkRootLocal As Transform_Class = If(chunkRootDiag IsNot Nothing, New Transform_Class(chunkRootDiag), New Transform_Class())
                    Dim crT = chunkRootLocal.Translation
                    Dim crR = chunkRootLocal.Rotation
                    Dim crS = chunkRootLocal.Scale
                    Dim socketLocal As New Transform_Class With {
                        .Translation = socket.Translation,
                        .Rotation = BSConnectPointReader.QuatToMatrix33(socket.Rotation),
                        .Scale = If(socket.Scale > 0.0F, socket.Scale, 1.0F)
                    }
                    Dim sT = socketLocal.Translation
                    Dim sR = socketLocal.Rotation
                    Dim parentBoneCache As HierarchiBone_class = Nothing
                    Dim parentWorldT As New System.Numerics.Vector3()
                    Dim parentWorldR As NiflySharp.Structs.Matrix33 = Nothing
                    Dim parentWorldS As Single = 1.0F
                    Dim parentBoneFound = False
                    If Not String.IsNullOrEmpty(socket.ParentBoneName) AndAlso inst.SkeletonDictionary.TryGetValue(socket.ParentBoneName, parentBoneCache) Then
                        Dim parentWorld = parentBoneCache.OriginalGetGlobalTransform
                        parentWorldT = parentWorld.Translation
                        parentWorldR = parentWorld.Rotation
                        parentWorldS = parentWorld.Scale
                        parentBoneFound = True
                    End If

                    Logger.LogLazy(Function() $"[CHUNK-DIAG] === shape='{shName}' BEGIN ===")
                    Logger.LogLazy(Function() $"[CHUNK-DIAG] Skinned flag = {skinnedFlag}")

                    ' [DIAG-SKIN-TYPE] Tipo del NifSkin block del chunk + metadata distinctiva.
                    ' Hipótesis a validar: BSSkin_Instance → module-rig (V2). BSDismemberSkinInstance
                    ' → actor-rig humanoid body part skin (skip V2). NiSkinInstance → legacy.
                    ' Si el tipo correlaciona limpio con los 4 casos (Codsworth arms/eyes vs
                    ' Protectron/Assaultron arms), tenemos discriminador NIF-autoritativo.
                    Try
                        Dim nifSkin = shape.NifSkin
                        If nifSkin Is Nothing Then
                            Logger.LogLazy(Function() $"[CHUNK-DIAG] NifSkin: <null>")
                        Else
                            Dim skinTypeName = nifSkin.GetType().Name
                            Dim skelRootName As String = "<unknown>"
                            Dim partitionInfo As String = ""

                            If TypeOf nifSkin Is NiflySharp.Blocks.BSSkin_Instance Then
                                Dim bsSkin = DirectCast(nifSkin, NiflySharp.Blocks.BSSkin_Instance)
                                If bsSkin.SkeletonRoot IsNot Nothing AndAlso bsSkin.SkeletonRoot.Index >= 0 AndAlso bsSkin.SkeletonRoot.Index < shape.NifContent.Blocks.Count Then
                                    Dim rootBlock = shape.NifContent.Blocks(bsSkin.SkeletonRoot.Index)
                                    Dim rootNode = TryCast(rootBlock, NiflySharp.Blocks.NiNode)
                                    If rootNode IsNot Nothing Then
                                        skelRootName = If(rootNode.Name?.String, "<null-name>")
                                    Else
                                        skelRootName = $"<not-NiNode: {rootBlock.GetType().Name}>"
                                    End If
                                End If
                            ElseIf TypeOf nifSkin Is NiflySharp.Blocks.BSDismemberSkinInstance Then
                                Dim dismember = DirectCast(nifSkin, NiflySharp.Blocks.BSDismemberSkinInstance)
                                If dismember.SkeletonRoot IsNot Nothing AndAlso dismember.SkeletonRoot.Index >= 0 AndAlso dismember.SkeletonRoot.Index < shape.NifContent.Blocks.Count Then
                                    Dim rootNode = TryCast(shape.NifContent.Blocks(dismember.SkeletonRoot.Index), NiflySharp.Blocks.NiNode)
                                    If rootNode IsNot Nothing Then skelRootName = If(rootNode.Name?.String, "<null-name>")
                                End If
                                If dismember.Partitions IsNot Nothing Then
                                    Dim numParts = dismember.Partitions.Count
                                    partitionInfo = $" partitions={numParts}"
                                End If
                            ElseIf TypeOf nifSkin Is NiflySharp.Blocks.NiSkinInstance Then
                                Dim niSkin = DirectCast(nifSkin, NiflySharp.Blocks.NiSkinInstance)
                                If niSkin.SkeletonRoot IsNot Nothing AndAlso niSkin.SkeletonRoot.Index >= 0 AndAlso niSkin.SkeletonRoot.Index < shape.NifContent.Blocks.Count Then
                                    Dim rootNode = TryCast(shape.NifContent.Blocks(niSkin.SkeletonRoot.Index), NiflySharp.Blocks.NiNode)
                                    If rootNode IsNot Nothing Then skelRootName = If(rootNode.Name?.String, "<null-name>")
                                End If
                            End If

                            Dim stnL = skinTypeName, srL = skelRootName, piL = partitionInfo
                            Logger.LogLazy(Function() $"[CHUNK-DIAG] NifSkin: type={stnL} SkeletonRoot='{srL}'{piL}")
                        End If
                    Catch exSkin As Exception
                        Logger.LogLazy(Function() $"[CHUNK-DIAG] NifSkin: EXCEPTION {exSkin.GetType().Name}: {exSkin.Message}")
                    End Try

                    Logger.LogLazy(Function() $"[CHUNK-DIAG] chunkRoot.local: T=({crT.X:F3},{crT.Y:F3},{crT.Z:F3}) S={crS:F3} R=[{crR.M11:F3},{crR.M12:F3},{crR.M13:F3}|{crR.M21:F3},{crR.M22:F3},{crR.M23:F3}|{crR.M31:F3},{crR.M32:F3},{crR.M33:F3}]")
                    Logger.LogLazy(Function() $"[CHUNK-DIAG] socket.local: name='{socket.Name}' parentBone='{socket.ParentBoneName}' T=({sT.X:F3},{sT.Y:F3},{sT.Z:F3}) R=[{sR.M11:F3},{sR.M12:F3},{sR.M13:F3}|{sR.M21:F3},{sR.M22:F3},{sR.M23:F3}|{sR.M31:F3},{sR.M32:F3},{sR.M33:F3}]")
                    If parentBoneFound Then
                        Dim pT = parentWorldT, pR = parentWorldR, pS = parentWorldS
                        Logger.LogLazy(Function() $"[CHUNK-DIAG] parent_bone.world: T=({pT.X:F3},{pT.Y:F3},{pT.Z:F3}) S={pS:F3} R=[{pR.M11:F3},{pR.M12:F3},{pR.M13:F3}|{pR.M21:F3},{pR.M22:F3},{pR.M23:F3}|{pR.M31:F3},{pR.M32:F3},{pR.M33:F3}]")
                    Else
                        Logger.LogLazy(Function() $"[CHUNK-DIAG] parent_bone.world: NOT-FOUND ({socket.ParentBoneName})")
                    End If

                    ' Per ShapeBone: dump NiNode chain + bind
                    If shape.ShapeBones IsNot Nothing AndAlso shape.ShapeBoneTransforms IsNot Nothing Then
                        For sbi = 0 To shape.ShapeBones.Count - 1
                            Dim sb = shape.ShapeBones(sbi)
                            Dim sbNode = TryCast(sb, NiflySharp.Blocks.NiNode)
                            If sbNode Is Nothing Then Continue For
                            Dim sbName = If(sbNode.Name?.String, "<null>")
                            Dim bindT As Transform_Class = shape.ShapeBoneTransforms(sbi)
                            Dim bT = bindT.Translation
                            Dim bR = bindT.Rotation
                            Dim bS = bindT.Scale
                            Dim sbiCap = sbi, sbNameCap = sbName
                            Logger.LogLazy(Function() $"[CHUNK-DIAG]   shapeBone[{sbiCap}] '{sbNameCap}'")
                            Logger.LogLazy(Function() $"[CHUNK-DIAG]     bind: T=({bT.X:F3},{bT.Y:F3},{bT.Z:F3}) S={bS:F3} R=[{bR.M11:F3},{bR.M12:F3},{bR.M13:F3}|{bR.M21:F3},{bR.M22:F3},{bR.M23:F3}|{bR.M31:F3},{bR.M32:F3},{bR.M33:F3}]")
                            ' NiNode chain: walk up from this NiNode to chunkRoot, dumping each
                            Dim curNode As NiflySharp.Blocks.NiNode = sbNode
                            Dim depth As Integer = 0
                            While curNode IsNot Nothing
                                Dim curLocal As New Transform_Class(curNode)
                                Dim cT = curLocal.Translation
                                Dim cR = curLocal.Rotation
                                Dim curName = If(curNode.Name?.String, "<null>")
                                Dim isRoot = (chunkRootDiag IsNot Nothing AndAlso ReferenceEquals(curNode, chunkRootDiag))
                                Dim depthCap = depth, curNameCap = curName, isRootCap = isRoot
                                Dim cTcap = cT, cRcap = cR, cScap = curLocal.Scale
                                Logger.LogLazy(Function() $"[CHUNK-DIAG]     chain[{depthCap}] '{curNameCap}'{If(isRootCap, " (ROOT)", "")} local: T=({cTcap.X:F3},{cTcap.Y:F3},{cTcap.Z:F3}) S={cScap:F3} R=[{cRcap.M11:F3},{cRcap.M12:F3},{cRcap.M13:F3}|{cRcap.M21:F3},{cRcap.M22:F3},{cRcap.M23:F3}|{cRcap.M31:F3},{cRcap.M32:F3},{cRcap.M33:F3}]")
                                If isRoot Then Exit While
                                Dim parentNode = TryCast(chunkNif.GetParentNode(curNode), NiflySharp.Blocks.NiNode)
                                If parentNode Is Nothing Then Exit While
                                curNode = parentNode
                                depth += 1
                                If depth > 20 Then Exit While ' safety
                            End While
                        Next
                    End If
                    Logger.LogLazy(Function() $"[CHUNK-DIAG] === shape='{shName}' END ===")
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[CHUNK-DIAG] EXCEPTION: {ex.GetType().Name}: {ex.Message}")
                End Try
            End If

            ' [DIAG-INJECT] Pre/post snapshots para inferir qué bones se crearon (sobre targetSkel).
            Dim preKeysSet = New HashSet(Of String)(targetSkel.SkeletonDictionary.Keys, StringComparer.OrdinalIgnoreCase)
            Dim preCount = targetSkel.SkeletonDictionary.Count
            Dim n = BSConnectPointBoneInjector_Class.InjectChunkBonesIntoLiveSkeleton(shape.NifContent, shape, socket, targetSkel)
            totalInjected += n
            Dim postCount = targetSkel.SkeletonDictionary.Count

            ' FAKE-SKIN: a una shape UNSKINNED dentro de un chunk BSConnectPoint se le aplica un skin
            ' sintético que ata todos sus vértices al ancla del chunk con peso 1.0. Sin esto, el path
            ' unskinned computa su transform en el frame LOCAL del chunk y el shader lo aplica como si fuera
            ' actor-world ⇒ la geometría cae al origen. Con el ancla entra al path skinned nativo y sigue la
            ' pose gratis.
            ' La bind matrix se camina hasta el PADRE del chunkRoot, sin componer chunkRoot.local: esa
            ' rotación es del visor del modelador, no parte del attachment, y el mundo del ancla ya trae la
            ' rotación del hueso padre del actor. Componer las dos mete un flip espurio de 180°.
            ' Aplica a robot Y biped: gatearlo sólo por robot dejaba caer al origen los chunks unskinned de
            ' bipeds. Criterio: unskinned + Attachment + MountSocket resuelto.
            Dim fakeSkinCand As MeshCandidate = Nothing
            renderData.ShapeCandidate.TryGetValue(shape, fakeSkinCand)
            Dim isAttachmentMount As Boolean = fakeSkinCand IsNot Nothing AndAlso fakeSkinCand.Kind = MeshCandidateKind.Attachment AndAlso fakeSkinCand.MountSocket IsNot Nothing
            If Not shape.IsSkinned AndAlso isAttachmentMount Then
                ' [FAKE-SKIN-DIAG] Confirmaciones discriminantes: estado de la shape
                ' antes de aplicar el anchor. Permite verificar la hipótesis del bug en el log.
                If Logger.Enabled Then
                    Dim shD = shape.ShapeName, isSk = shape.IsSkinned, sbC = If(shape.ShapeBones IsNot Nothing, shape.ShapeBones.Count, -1), sockD = If(socket?.Name, "?")
                    Logger.LogLazy(Function() $"[FAKE-SKIN-DIAG] shape='{shD}' IsSkinned={isSk} ShapeBones.Count={sbC} socket='{sockD}' isRobotMount={isRobotMount}")
                End If
                Try
                    Dim asOverride = TryCast(shape, IRuntimeSkinOverride)
                    If asOverride IsNot Nothing Then
                        ' [V2-AWARE ANCHOR] Preferir el C-X counterpart (materializado por
                        ' MaterializeSocketsAsConnectPointBones desde inst.Skeleton.nif) sobre
                        ' el synthetic '__chunkAnchor__'. El C-X bone está en la misma posición
                        ' world que el synthetic (ambos derivados de parent×socket.local), pero
                        ' además cascadea correctamente cuando V2 SKEL-OVERRIDE lo modifica
                        ' (caso vivo: LightPlane Protectron — V2 mueve C-Head 5.47 unidades,
                        ' synthetic anchor parented a Chest no seguía, light renderizaba en
                        ' posición vieja). Plus cubre chunks 100% unskinned (shishkebab DLC
                        ' Mechanist Assaultron) donde InjectChunkBonesIntoLiveSkeleton
                        ' early-exits y nunca crea el synthetic anchor.
                        Dim cxBoneName As String = BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(socket.Name)
                        Dim cxInDict As Boolean = Not String.IsNullOrEmpty(cxBoneName) AndAlso targetSkel.SkeletonDictionary.ContainsKey(cxBoneName)
                        ' [CAMINO C — materialización lazy on-demand del C-X]
                        ' Si el C-X counterpart no está en el skel (chunk unskinned puro: el injector
                        ' early-exit con ShapeBones=0 y el bulk materialize pudo no correr/correr tarde),
                        ' materializarlo AHORA desde el socket EFECTIVO que el shape loop ya tiene. Mantiene
                        ' el modelo (socket vive como bone C-X), no depende del orden helmet-antes-de-child
                        ' ni de ShapeBones>0. Reusa la semántica de MaterializeSocketsAsConnectPointBones.
                        If Not cxInDict Then
                            Dim ensuredName = BSConnectPointBoneInjector_Class.EnsureSocketCounterpartBone(socket, targetSkel)
                            If Not String.IsNullOrEmpty(ensuredName) Then
                                cxBoneName = ensuredName
                                cxInDict = True
                            End If
                        End If
                        Dim anchorName As String
                        If cxInDict Then
                            anchorName = cxBoneName
                        Else
                            Dim chunkRootName = If(shape.NifContent.GetRootNode()?.Name?.String, "chunk")
                            anchorName = "__chunkAnchor__" & socket.Name & "__" & chunkRootName
                        End If
                        ' [FAKE-SKIN-DIAG] Confirmación #2: ¿el C-X counterpart
                        ' está materializado en el skel? Si cxInDict=False, el anchor cae al synthetic
                        ' __chunkAnchor__ que el injector puede no haber creado para shapes sin bones →
                        ' el anchor apuntaría a un bone inexistente → sin ancla real (origen).
                        If Logger.Enabled Then
                            Dim shD2 = shape.ShapeName, cxN = cxBoneName, inD = cxInDict, anN = anchorName
                            Dim anchorExists = targetSkel.SkeletonDictionary.ContainsKey(anN)
                            Logger.LogLazy(Function() $"[FAKE-SKIN-DIAG] shape='{shD2}' cxBone='{cxN}' cxInDict={inD} → anchor='{anN}' anchorExistsInSkel={anchorExists}")
                        End If

                        ' Computar bind manualmente: walk desde backing hacia arriba pero
                        ' STOP antes de componer chunkRoot.local.
                        Dim backing = shape.Geometry.BackingShape
                        Dim chunkRootNode = shape.NifContent.GetRootNode()
                        Dim bindMatrix As New Transform_Class(backing)
                        Dim curNode As NiflySharp.Blocks.NiNode = TryCast(shape.NifContent.GetParentNode(backing), NiflySharp.Blocks.NiNode)
                        While curNode IsNot Nothing AndAlso Not ReferenceEquals(curNode, chunkRootNode)
                            bindMatrix = New Transform_Class(curNode).ComposeTransforms(bindMatrix)
                            curNode = TryCast(shape.NifContent.GetParentNode(curNode), NiflySharp.Blocks.NiNode)
                        End While

                        ' Placeholder NiNode en memoria — su .Name debe matchear el anchor que
                        ' BSConnectPointBoneInjector creó en SkeletonInstance.SkeletonDictionary.
                        ' No se agrega a chunkNif.Blocks, vive solo como referencia de bone name.
                        Dim placeholder As New NiflySharp.Blocks.NiNode With {
                            .Name = New NiflySharp.NiStringRef(anchorName)
                        }

                        asOverride.ApplySyntheticAnchorSkin(placeholder, bindMatrix)

                        If Logger.Enabled Then
                            Dim shL = shape.ShapeName, anL = anchorName
                            Dim bT = bindMatrix.Translation
                            Logger.LogLazy(Function() $"[FAKE-SKIN] shape='{shL}' anchor='{anL}' bind.T=({bT.X:F3},{bT.Y:F3},{bT.Z:F3}) (excluye chunkRoot.local)")
                        End If
                    End If
                Catch ex As Exception
                    Dim shL = shape.ShapeName, exL = ex
                    Logger.LogLazy(Function() $"[FAKE-SKIN] shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
                End Try
            End If

            ' Re-skin de los bone transforms del chunk. Cada hueso B recibe `W_B = G_B × A`, donde `G_B` es su
            ' global en el árbol del NIF del chunk, y `A = inv(G_CX) × P_world` es el ÚNICO transform de
            ' attachment chunk→actor. Como el render usa el mundo del hueso del actor, se corrige el bind:
            '   bind' = inv(actor.B.world) × W_B × bind    ⇒   v · bind' × actor.B.world = v · bind × W_B
            ' Para el hueso C-X eso colapsa a P_world; para los demás, `A` transporta su posición relativa y
            ' así se conserva la ARTICULACIÓN del chunk en vez de colapsar todos los huesos en un punto.
            ' NO aplicarlo a shapes fake-skinned: su bind ya es el Mtot chunk-local y el shader lo compone
            ' con el mundo del ancla; el re-skin les metería un factor espurio y rompe el render.
            Dim _syntheticOverride = TryCast(shape, IRuntimeSkinOverride)
            If _syntheticOverride IsNot Nothing AndAlso _syntheticOverride.HasSyntheticSkin Then
                If isRobotMount AndAlso Logger.Enabled Then
                    Dim shL = shape.ShapeName
                    Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' SKIP: HasSyntheticSkin=True (fake-skinned)")
                End If
                Continue For
            End If

            _mountingResolver.CollectV2PlanForShape(shape, socket, targetSkel, renderData, targetWB, isRobotMount)
            If isRobotMount AndAlso Logger.Enabled Then
                Dim newKeys = targetSkel.SkeletonDictionary.Keys.Where(Function(k) Not preKeysSet.Contains(k)).OrderBy(Function(k) k).ToList()
                Dim shNameLog = shape.ShapeName, nLog = n, preLog = preCount, postLog = postCount
                Logger.LogLazy(Function() $"[INJECT] shape='{shNameLog}' returned={nLog} dictCount {preLog}→{postLog} added={newKeys.Count}: [{String.Join(", ", newKeys)}]")
                ' Para cada bone nuevo, dumpear su world resuelto (esperado = inverseBind × M_mesh para shape bones, M_mesh para anchor).
                For Each nbName In newKeys
                    Dim nb = nbName
                    Dim hb As HierarchiBone_class = Nothing
                    targetSkel.SkeletonDictionary.TryGetValue(nb, hb)
                    If hb IsNot Nothing Then
                        Dim worldT = hb.OriginalGetGlobalTransform
                        Dim wr = worldT.Rotation
                        Dim parentName = If(hb.Parent IsNot Nothing, hb.Parent.BoneName, "<root>")
                        Logger.LogLazy(Function() $"[INJECT]   '{nb}' parent='{parentName}' world: T=({worldT.Translation.X:F3},{worldT.Translation.Y:F3},{worldT.Translation.Z:F3}) S={worldT.Scale:F3} R=[{wr.M11:F3},{wr.M12:F3},{wr.M13:F3}|{wr.M21:F3},{wr.M22:F3},{wr.M23:F3}|{wr.M31:F3},{wr.M32:F3},{wr.M33:F3}]")
                    End If
                Next
            End If

            ' [CHUNK-PARENTS-DUMP] Antes de materializar el chunk's BSConnectPoint::Parents, leerlos
            ' del NIF y comparar con lo que ya está en el dict. Si el chunk redeclara un socket name
            ' con T/R/S distintos a los del skeleton.nif del actor (o de chunks previos), la idempotencia
            ' de MaterializeSocketsAsConnectPointBones los skipea silenciosamente — pero el engine de
            ' FO4 podría usar la versión del chunk. Log diagnóstico para detectar discrepancias.
            If isRobotMount AndAlso Not processedNifs.Contains((targetSkel, shape.NifContent)) Then
                Try
                    Dim chunkParents = BSConnectPointReader.ReadParents(shape.NifContent)
                    If chunkParents IsNot Nothing Then
                        For Each cp In chunkParents
                            If cp Is Nothing OrElse String.IsNullOrEmpty(cp.Name) Then Continue For
                            Dim cName = BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(cp.Name)
                            If String.IsNullOrEmpty(cName) Then Continue For
                            Dim chunkRotMat = BSConnectPointReader.QuatToMatrix33(cp.Rotation)
                            Dim chunkT = cp.Translation, chunkS = If(cp.Scale > 0.0F, cp.Scale, 1.0F)
                            Dim existing As HierarchiBone_class = Nothing
                            Dim shLogP = shape.ShapeName, cpName = cp.Name, cnL = cName, cpParent = cp.ParentBoneName
                            Dim cTL = chunkT, cSL = chunkS, cRL = chunkRotMat
                            If targetSkel.SkeletonDictionary.TryGetValue(cName, existing) Then
                                Dim existLocal = existing.OriginalLocaLTransform
                                Dim eT = existLocal.Translation, eS = existLocal.Scale, eR = existLocal.Rotation
                                Dim existParent = If(existing.Parent IsNot Nothing, existing.Parent.BoneName, "<root>")
                                Const eps As Single = 0.001F
                                Dim tDiff = Math.Abs(eT.X - cTL.X) + Math.Abs(eT.Y - cTL.Y) + Math.Abs(eT.Z - cTL.Z)
                                Dim sDiff = Math.Abs(eS - cSL)
                                Dim rDiff = Math.Abs(eR.M11 - cRL.M11) + Math.Abs(eR.M12 - cRL.M12) + Math.Abs(eR.M13 - cRL.M13) +
                                            Math.Abs(eR.M21 - cRL.M21) + Math.Abs(eR.M22 - cRL.M22) + Math.Abs(eR.M23 - cRL.M23) +
                                            Math.Abs(eR.M31 - cRL.M31) + Math.Abs(eR.M32 - cRL.M32) + Math.Abs(eR.M33 - cRL.M33)
                                Dim parentDiff = Not String.Equals(existParent, cpParent, StringComparison.OrdinalIgnoreCase)
                                Dim diffTag = If(tDiff > eps OrElse sDiff > eps OrElse rDiff > eps OrElse parentDiff, "DIFFERS", "MATCH")
                                Dim tDiffL = tDiff, sDiffL = sDiff, rDiffL = rDiff, parDiffL = parentDiff, dtL = diffTag
                                Dim eTL = eT, eSL = eS, eRL = eR, exParL = existParent
                                Logger.LogLazy(Function() $"[CHUNK-PARENTS-DUMP] shape='{shLogP}' socket='{cpName}' → cName='{cnL}' EXISTS-IN-DICT diff={dtL} (tDiff={tDiffL:F3} sDiff={sDiffL:F3} rDiff={rDiffL:F3} parentDiff={parDiffL})")
                                Logger.LogLazy(Function() $"[CHUNK-PARENTS-DUMP]   chunk-source:  parent='{cpParent}' T=({cTL.X:F3},{cTL.Y:F3},{cTL.Z:F3}) S={cSL:F3} R=[{cRL.M11:F3},{cRL.M12:F3},{cRL.M13:F3}|{cRL.M21:F3},{cRL.M22:F3},{cRL.M23:F3}|{cRL.M31:F3},{cRL.M32:F3},{cRL.M33:F3}]")
                                Logger.LogLazy(Function() $"[CHUNK-PARENTS-DUMP]   dict-existing: parent='{exParL}' T=({eTL.X:F3},{eTL.Y:F3},{eTL.Z:F3}) S={eSL:F3} R=[{eRL.M11:F3},{eRL.M12:F3},{eRL.M13:F3}|{eRL.M21:F3},{eRL.M22:F3},{eRL.M23:F3}|{eRL.M31:F3},{eRL.M32:F3},{eRL.M33:F3}]")
                            Else
                                Logger.LogLazy(Function() $"[CHUNK-PARENTS-DUMP] shape='{shLogP}' socket='{cpName}' → cName='{cnL}' NOT-IN-DICT (will be materialized fresh from chunk: parent='{cpParent}' T=({cTL.X:F3},{cTL.Y:F3},{cTL.Z:F3}) S={cSL:F3} R=[{cRL.M11:F3},{cRL.M12:F3},{cRL.M13:F3}|{cRL.M21:F3},{cRL.M22:F3},{cRL.M23:F3}|{cRL.M31:F3},{cRL.M32:F3},{cRL.M33:F3}])")
                            End If
                        Next
                    End If
                Catch ex As Exception
                    Dim shL = shape.ShapeName, exL = ex
                    Logger.LogLazy(Function() $"[CHUNK-PARENTS-DUMP] shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
                End Try
            End If

            If processedNifs.Add((targetSkel, shape.NifContent)) Then
                Dim preMatKeysSet = New HashSet(Of String)(targetSkel.SkeletonDictionary.Keys, StringComparer.OrdinalIgnoreCase)
                Dim preMatCount = targetSkel.SkeletonDictionary.Count
                Dim m = BSConnectPointBoneInjector_Class.MaterializeSocketsAsConnectPointBones(shape.NifContent, targetSkel)
                If isRobotMount AndAlso Logger.Enabled Then
                    Dim newMatKeys = targetSkel.SkeletonDictionary.Keys.Where(Function(k) Not preMatKeysSet.Contains(k)).OrderBy(Function(k) k).ToList()
                    Dim shNameMat = shape.ShapeName, mLog = m
                    Logger.LogLazy(Function() $"[MATERIALIZE-CHUNK] shape='{shNameMat}' returned={mLog} addedSubSockets={newMatKeys.Count}: [{String.Join(", ", newMatKeys)}]")
                End If
            End If
        Next

        ' [MOUNTDELTA-APPLY] Fuente única de verdad: el shape loop SOLO colectó el plan
        ' (renderData.MountDesiredWorlds) — acá lo aplicamos en orden topológico. El initial render
        ' usa el mismo ApplyMountPlanForActor que el pose-dirty refresh. Base inst primero.
        _mountingResolver.ApplyMountPlanForActor(inst, renderData)
        ' Per-instance scope: cada clone sculpt aplica su propio subset (filtrado por TargetSkel).
        For Each kv In skelByArma
            Dim cloneInst = kv.Value
            If cloneInst Is Nothing OrElse ReferenceEquals(cloneInst, inst) Then Continue For
            _mountingResolver.ApplyMountPlanForActor(cloneInst, renderData)
        Next

        ' DIAG: dump post-inject — solo bones nuevos (los inyectados).
        If isRobotMount AndAlso Logger.Enabled Then
            Dim injected = inst.InjectedBones.OrderBy(Function(k) k).ToList()
            Dim injCount = injected.Count
            Logger.LogLazy(Function() $"[POST-INJECT-SUMMARY] inst.InjectedBones count={injCount} total={inst.SkeletonDictionary.Count}")
            For Each k In injected
                Dim kn = k
                Logger.LogLazy(Function() $"[POST-INJECT-SUMMARY]   '{kn}'")
            Next
        End If

        ' DIAG socket-math: para cada chunk shape con socket, dumpear el
        ' bone.world (OriginalGetGlobalTransform) de cada bone que el shape referencia.
        ' Con esto + bind log + socket log podemos hacer la matemática completa offline.
        If isRobotMount AndAlso Logger.Enabled Then
            For Each sh In ordered
                Dim sock As BSConnectPointReader.ConnectPointInfo = Nothing
                If Not renderData.ShapeMountSocket.TryGetValue(sh, sock) Then Continue For
                Dim shName = sh.ShapeName
                Logger.LogLazy(Function() $"[BONE-WORLD] shape='{shName}' socket='{sock.Name}' parentBone='{sock.ParentBoneName}'")
                If sh.ShapeBones Is Nothing Then Continue For
                For Each bn In sh.ShapeBones
                    Dim niN = TryCast(bn, NiflySharp.Blocks.NiNode)
                    Dim boneName = If(niN?.Name?.String, "<null>")
                    Dim hb As HierarchiBone_class = Nothing
                    If Not inst.SkeletonDictionary.TryGetValue(boneName, hb) Then
                        Dim bnName = boneName
                        Logger.LogLazy(Function() $"[BONE-WORLD]   bone='{bnName}' NOT IN DICT")
                        Continue For
                    End If
                    Dim originalGlobal = hb.OriginalGetGlobalTransform
                    Dim poseGlobal = hb.GetGlobalTransform()
                    Dim bnNameLog = boneName
                    Dim ogR = originalGlobal.Rotation
                    Dim pgR = poseGlobal.Rotation
                    Logger.LogLazy(Function() $"[BONE-WORLD]   bone='{bnNameLog}' originalGlobal: T=({originalGlobal.Translation.X:F2},{originalGlobal.Translation.Y:F2},{originalGlobal.Translation.Z:F2}) S={originalGlobal.Scale:F3} R=[{ogR.M11:F3},{ogR.M12:F3},{ogR.M13:F3}|{ogR.M21:F3},{ogR.M22:F3},{ogR.M23:F3}|{ogR.M31:F3},{ogR.M32:F3},{ogR.M33:F3}]")
                    Logger.LogLazy(Function() $"[BONE-WORLD]   bone='{bnNameLog}' poseGlobal:    T=({poseGlobal.Translation.X:F2},{poseGlobal.Translation.Y:F2},{poseGlobal.Translation.Z:F2}) S={poseGlobal.Scale:F3} R=[{pgR.M11:F3},{pgR.M12:F3},{pgR.M13:F3}|{pgR.M21:F3},{pgR.M22:F3},{pgR.M23:F3}|{pgR.M31:F3},{pgR.M32:F3},{pgR.M33:F3}]")
                Next
            Next

            ' DIAG VERTEX-TRACE: per chunk shape, replicar la fórmula de skinning
            ' app-side y loggear la posición predicta del primeros vertices. Muestra:
            '   v_local      → posición cruda en NIF chunk-local
            '   v_after_LT_i → v_local · localT_i (después del primer paso de skinning)
            '   v_world_i    → v_after_LT_i · boneWorld_i (después del 2do paso, sin weights ni global)
            '   v_world_blend → suma ponderada por weights
            ' Sirve para verificar empíricamente DÓNDE termina el chunk en world space, sin tocar
            ' la lib del render. Si v_world_blend está donde debería (lomo del brahmin), el render
            ' está bien y el problema es visual de cámara o algo más. Si está mal, vemos qué offset
            ' falta meter (ej. socket transform).
            Try
                For Each sh In ordered
                    Dim sock As BSConnectPointReader.ConnectPointInfo = Nothing
                    If Not renderData.ShapeMountSocket.TryGetValue(sh, sock) Then Continue For
                    If sh.NifContent Is Nothing OrElse sh.Geometry Is Nothing Then Continue For

                    Dim shName = sh.ShapeName
                    Dim positions = sh.Geometry.GetVertexPositions()
                    Dim skinning = sh.Geometry.GetSkinning()
                    Dim shapeBones = sh.ShapeBones
                    Dim shapeBoneT = sh.ShapeBoneTransforms

                    If positions Is Nothing OrElse positions.Count = 0 Then Continue For
                    If skinning.BoneIndices Is Nothing OrElse skinning.WeightsPerVertex <= 0 Then Continue For
                    If shapeBones Is Nothing OrElse shapeBoneT Is Nothing Then Continue For

                    Dim wpv = skinning.WeightsPerVertex
                    Dim sampleCount = Math.Min(3, positions.Count)
                    Logger.LogLazy(Function() $"[VERTEX-TRACE] shape='{shName}' verts={positions.Count} wpv={wpv} sampling first {sampleCount}")

                    ' [DIAG-EXPECTED] Posición esperada para el origen (0,0,0) del chunk: socket × parent_bone.world.
                    ' En row-vec: M_mesh = socketLocal × parent_world. v_origin = (0,0,0) → cae en M_mesh.Translation.
                    Try
                        Dim socketLocalExp As New Transform_Class With {
                            .Translation = sock.Translation,
                            .Rotation = BSConnectPointReader.QuatToMatrix33(sock.Rotation),
                            .Scale = If(sock.Scale > 0.0F, sock.Scale, 1.0F)
                        }
                        Dim parentWorldExp As New Transform_Class()
                        Dim parentFound As Boolean = False
                        Dim parentHb As HierarchiBone_class = Nothing
                        If Not String.IsNullOrEmpty(sock.ParentBoneName) AndAlso inst.SkeletonDictionary.TryGetValue(sock.ParentBoneName, parentHb) Then
                            parentWorldExp = parentHb.OriginalGetGlobalTransform
                            parentFound = True
                        End If
                        Dim mMesh = parentWorldExp.ComposeTransforms(socketLocalExp)
                        Dim mr = mMesh.Rotation
                        Dim parentFoundCap = parentFound
                        Dim sockName = sock.Name
                        Dim sockParent = sock.ParentBoneName
                        Logger.LogLazy(Function() $"[VERTEX-TRACE]   EXPECTED M_mesh (socket='{sockName}' × parent='{sockParent}'.world) parentFound={parentFoundCap}")
                        Logger.LogLazy(Function() $"[VERTEX-TRACE]     M_mesh.T={mMesh.Translation.X:F2},{mMesh.Translation.Y:F2},{mMesh.Translation.Z:F2}  M_mesh.S={mMesh.Scale:F3}  R=[{mr.M11:F3},{mr.M12:F3},{mr.M13:F3}|{mr.M21:F3},{mr.M22:F3},{mr.M23:F3}|{mr.M31:F3},{mr.M32:F3},{mr.M33:F3}]")
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[VERTEX-TRACE]   EXPECTED M_mesh EXCEPTION: {ex.Message}")
                    End Try

                    For vi = 0 To sampleCount - 1
                        Dim vp = positions(vi)
                        Dim vLocal As New System.Numerics.Vector3(vp.X, vp.Y, vp.Z)
                        Dim vBlendX As Single = 0, vBlendY As Single = 0, vBlendZ As Single = 0
                        Dim sumW As Single = 0
                        Dim viCap = vi
                        Dim vL = vLocal
                        Logger.LogLazy(Function() $"[VERTEX-TRACE]   v[{viCap}] v_local=({vL.X:F2},{vL.Y:F2},{vL.Z:F2})")
                        For j = 0 To wpv - 1
                            Dim baseSkin = vi * wpv + j
                            If baseSkin >= skinning.BoneIndices.Length Then Exit For
                            Dim boneIdx = CInt(skinning.BoneIndices(baseSkin))
                            Dim w = CSng(skinning.BoneWeights(baseSkin))
                            If w <= 0 OrElse boneIdx >= shapeBones.Count OrElse boneIdx >= shapeBoneT.Count Then Continue For
                            sumW += w

                            Dim boneNode = TryCast(shapeBones(boneIdx), NiflySharp.Blocks.NiNode)
                            Dim boneName = If(boneNode?.Name?.String, "<null>")
                            Dim localT = shapeBoneT(boneIdx)

                            Dim hb As HierarchiBone_class = Nothing
                            inst.SkeletonDictionary.TryGetValue(boneName, hb)
                            Dim boneWorld As Transform_Class = If(hb IsNot Nothing, hb.OriginalGetGlobalTransform, New Transform_Class())
                            Dim originTag As String
                            If hb Is Nothing Then
                                originTag = "NOT-IN-DICT"
                            ElseIf inst.InjectedBones.Contains(boneName) Then
                                originTag = "INJECTED"
                            Else
                                originTag = "SHARED-WITH-ACTOR"
                            End If

                            ' v_after_localT = v · R_localT + T_localT
                            Dim rL = localT.Rotation
                            Dim aX As Single = vLocal.X * rL.M11 + vLocal.Y * rL.M21 + vLocal.Z * rL.M31 + localT.Translation.X
                            Dim aY As Single = vLocal.X * rL.M12 + vLocal.Y * rL.M22 + vLocal.Z * rL.M32 + localT.Translation.Y
                            Dim aZ As Single = vLocal.X * rL.M13 + vLocal.Y * rL.M23 + vLocal.Z * rL.M33 + localT.Translation.Z

                            ' v_world_i = (aX,aY,aZ) · R_boneWorld + T_boneWorld
                            Dim rB = boneWorld.Rotation
                            Dim wX As Single = aX * rB.M11 + aY * rB.M21 + aZ * rB.M31 + boneWorld.Translation.X
                            Dim wY As Single = aX * rB.M12 + aY * rB.M22 + aZ * rB.M32 + boneWorld.Translation.Y
                            Dim wZ As Single = aX * rB.M13 + aY * rB.M23 + aZ * rB.M33 + boneWorld.Translation.Z

                            vBlendX += w * wX
                            vBlendY += w * wY
                            vBlendZ += w * wZ

                            Dim viCap2 = vi, jCap = j
                            Dim bnLog = boneName, wLog = w
                            Dim aXL = aX, aYL = aY, aZL = aZ
                            Dim wXL = wX, wYL = wY, wZL = wZ
                            Dim originLog = originTag
                            Logger.LogLazy(Function() $"[VERTEX-TRACE]     v[{viCap2}] bone[{jCap}]='{bnLog}' {originLog} w={wLog:F3} v_after_localT=({aXL:F2},{aYL:F2},{aZL:F2}) v_world_i=({wXL:F2},{wYL:F2},{wZL:F2})")
                        Next

                        Dim vbX = vBlendX, vbY = vBlendY, vbZ = vBlendZ, swCap = sumW
                        Dim viCap3 = vi
                        Logger.LogLazy(Function() $"[VERTEX-TRACE]   v[{viCap3}] v_world_blend=({vbX:F2},{vbY:F2},{vbZ:F2}) sumW={swCap:F3}")
                    Next
                Next
            Catch ex As Exception
                Logger.LogLazy(Function() $"[VERTEX-TRACE] EXCEPTION: {ex.GetType().Name}: {ex.Message}")
            End Try

            ' DIAGNÓSTICO puro (sólo loguea, no toca el render): para el vértice 0 de cada chunk con socket
            ' predice v_world bajo cuatro hipótesis de cómo derivar el mundo del hueso del actor — la actual,
            ' identidad, sólo-socket, y alineando el C-X del chunk al P-X del socket — para poder comparar
            ' contra lo que se ve. Se conserva porque discrimina en dos reads un problema de montaje que si
            ' no se diagnostica a ciegas.
            Try
                For Each sh In ordered
                    Dim sock As BSConnectPointReader.ConnectPointInfo = Nothing
                    If Not renderData.ShapeMountSocket.TryGetValue(sh, sock) Then Continue For
                    If sh.NifContent Is Nothing OrElse sh.Geometry Is Nothing Then Continue For
                    Dim shapeBones2 = sh.ShapeBones
                    Dim shapeBoneT2 = sh.ShapeBoneTransforms
                    If shapeBones2 Is Nothing OrElse shapeBoneT2 Is Nothing Then Continue For
                    Dim positions2 = sh.Geometry.GetVertexPositions()
                    Dim skinning2 = sh.Geometry.GetSkinning()
                    If positions2.Count = 0 OrElse skinning2.WeightsPerVertex <= 0 Then Continue For
                    If skinning2.BoneIndices Is Nothing Then Continue For

                    Dim shNameSim = sh.ShapeName

                    ' Compute socketLocal (T+R from quat) and P-X.world once per chunk.
                    Dim socketLocalSim As New Transform_Class With {
                        .Translation = sock.Translation,
                        .Rotation = BSConnectPointReader.QuatToMatrix33(sock.Rotation),
                        .Scale = If(sock.Scale > 0.0F, sock.Scale, 1.0F)
                    }
                    Dim parentWorldSim As New Transform_Class()
                    Dim parentHbSim As HierarchiBone_class = Nothing
                    If Not String.IsNullOrEmpty(sock.ParentBoneName) AndAlso inst.SkeletonDictionary.TryGetValue(sock.ParentBoneName, parentHbSim) Then
                        parentWorldSim = parentHbSim.OriginalGetGlobalTransform
                    End If
                    Dim pxWorldSim = parentWorldSim.ComposeTransforms(socketLocalSim)

                    ' Find C-X bind for S4: scan shape bones for a name that matches one of the
                    ' chunk's BSConnectPoint::Children declarations. RenameShapeBoneIndices renames
                    ' shape bones |0→|N for multi-instance but NOT the Children block — so to match
                    ' both base (|0) and renamed (|1, |2) instances we strip any trailing |<digits>
                    ' from BOTH sides antes de comparar (via el helper Private Shared StripInstanceSuffix).
                    Dim childrenInfoSim = BSConnectPointReader.ReadChildren(sh.NifContent)
                    Dim cxBind As Transform_Class = Nothing
                    Dim cxBoneName As String = ""
                    For Each childName In childrenInfoSim.PointNames
                        Dim childNorm = NameUtils.StripInstanceSuffix(childName)
                        For idx As Integer = 0 To shapeBones2.Count - 1
                            Dim niN = TryCast(shapeBones2(idx), NiflySharp.Blocks.NiNode)
                            Dim bn = If(niN?.Name?.String, "")
                            Dim bnNorm = NameUtils.StripInstanceSuffix(bn)
                            If String.Equals(bnNorm, childNorm, StringComparison.OrdinalIgnoreCase) Then
                                cxBind = shapeBoneT2(idx)
                                cxBoneName = bn
                                Exit For
                            End If
                        Next
                        If cxBind IsNot Nothing Then Exit For
                    Next

                    ' M_chunk_to_actor under S4. NA if no C-X skinning bone found.
                    Dim mS4 As Transform_Class = Nothing
                    If cxBind IsNot Nothing Then
                        mS4 = pxWorldSim.ComposeTransforms(cxBind)
                    End If

                    ' [S5] General CX-align via NIF tree walk. The chunk's C-X NiNode may NOT be a
                    ' skinning bone (most non-arm chunks); we look it up in the full block tree and
                    ' compose its chunk-NIF-global transform. Math:
                    '   T_chunk_to_actor = pxWorld · inv(C-X.chunkGlobalT)
                    '   bone.actorWorld(B) = inv(bind(B)) · T_chunk_to_actor   (∀ B in skinning)
                    '   v_world = v · bind(B) · bone.actorWorld
                    ' When C-X IS a skinning bone, C-X.chunkGlobalT = inv(bind(C-X)) so S5 collapses
                    ' to S4 (verified analytically; reported both for sanity).
                    Dim cxChunkGlobalT As Transform_Class = Nothing
                    Dim cxNiNodeName As String = ""
                    For Each childName In childrenInfoSim.PointNames
                        Dim cxNode = sh.NifContent.FindBlockByName(Of NiflySharp.Blocks.NiNode)(childName)
                        If cxNode IsNot Nothing Then
                            cxChunkGlobalT = Transform_Class.GetGlobalTransform(cxNode, sh.NifContent)
                            cxNiNodeName = childName
                            Exit For
                        End If
                        Dim childNorm5 = NameUtils.StripInstanceSuffix(childName)
                        For Each candidateBlock In sh.NifContent.Blocks
                            Dim niNodeCand = TryCast(candidateBlock, NiflySharp.Blocks.NiNode)
                            If niNodeCand Is Nothing Then Continue For
                            Dim candName = If(niNodeCand.Name?.String, "")
                            Dim candNorm = NameUtils.StripInstanceSuffix(candName)
                            If String.Equals(candNorm, childNorm5, StringComparison.OrdinalIgnoreCase) Then
                                cxChunkGlobalT = Transform_Class.GetGlobalTransform(niNodeCand, sh.NifContent)
                                cxNiNodeName = candName
                                Exit For
                            End If
                        Next
                        If cxChunkGlobalT IsNot Nothing Then Exit For
                    Next
                    Dim tChunkToActor As Transform_Class = Nothing
                    If cxChunkGlobalT IsNot Nothing Then
                        tChunkToActor = pxWorldSim.ComposeTransforms(cxChunkGlobalT.Inverse())
                    End If

                    ' Vertex 0:
                    Dim vp0Sim = positions2(0)
                    Dim vLSim As New System.Numerics.Vector3(vp0Sim.X, vp0Sim.Y, vp0Sim.Z)

                    Dim wpvSim = skinning2.WeightsPerVertex
                    Dim s1X As Single = 0, s1Y As Single = 0, s1Z As Single = 0
                    Dim s2X As Single = 0, s2Y As Single = 0, s2Z As Single = 0
                    Dim s3X As Single = 0, s3Y As Single = 0, s3Z As Single = 0
                    Dim s4X As Single = 0, s4Y As Single = 0, s4Z As Single = 0
                    Dim s5X As Single = 0, s5Y As Single = 0, s5Z As Single = 0
                    Dim s4Available As Boolean = (mS4 IsNot Nothing)
                    Dim s5Available As Boolean = (tChunkToActor IsNot Nothing)
                    Dim sumWSim As Single = 0

                    For j = 0 To wpvSim - 1
                        Dim baseSkin = j
                        If baseSkin >= skinning2.BoneIndices.Length Then Exit For
                        Dim boneIdx = CInt(skinning2.BoneIndices(baseSkin))
                        Dim w = CSng(skinning2.BoneWeights(baseSkin))
                        If w <= 0 OrElse boneIdx >= shapeBones2.Count OrElse boneIdx >= shapeBoneT2.Count Then Continue For
                        sumWSim += w

                        Dim boneNode2 = TryCast(shapeBones2(boneIdx), NiflySharp.Blocks.NiNode)
                        Dim boneNameSim2 = If(boneNode2?.Name?.String, "<null>")
                        Dim bindB = shapeBoneT2(boneIdx)
                        Dim invBindB = bindB.Inverse()

                        ' Helper: compute v_world = v_local · bind · boneWorld (row-vec).
                        Dim ComputeVWorld = Function(boneWorld As Transform_Class) As System.Numerics.Vector3
                                                Dim rB = bindB.Rotation, tB = bindB.Translation
                                                Dim aX = vLSim.X * rB.M11 + vLSim.Y * rB.M21 + vLSim.Z * rB.M31 + tB.X
                                                Dim aY = vLSim.X * rB.M12 + vLSim.Y * rB.M22 + vLSim.Z * rB.M32 + tB.Y
                                                Dim aZ = vLSim.X * rB.M13 + vLSim.Y * rB.M23 + vLSim.Z * rB.M33 + tB.Z
                                                Dim rW = boneWorld.Rotation, tW = boneWorld.Translation
                                                Dim wxL = aX * rW.M11 + aY * rW.M21 + aZ * rW.M31 + tW.X
                                                Dim wyL = aX * rW.M12 + aY * rW.M22 + aZ * rW.M32 + tW.Y
                                                Dim wzL = aX * rW.M13 + aY * rW.M23 + aZ * rW.M33 + tW.Z
                                                Return New System.Numerics.Vector3(wxL, wyL, wzL)
                                            End Function

                        ' S1: actor's current bone.world.
                        Dim hb1 As HierarchiBone_class = Nothing
                        inst.SkeletonDictionary.TryGetValue(boneNameSim2, hb1)
                        Dim bw1Sim As Transform_Class = If(hb1 IsNot Nothing, hb1.OriginalGetGlobalTransform, New Transform_Class())
                        Dim v1 = ComputeVWorld(bw1Sim)
                        s1X += w * v1.X : s1Y += w * v1.Y : s1Z += w * v1.Z

                        ' S2: bone.world = inv(bind).
                        Dim v2 = ComputeVWorld(invBindB)
                        s2X += w * v2.X : s2Y += w * v2.Y : s2Z += w * v2.Z

                        ' S3: bone.world = socketLocal·inv(bind) = socketLocal.Compose(invBind).
                        Dim bw3Sim = socketLocalSim.ComposeTransforms(invBindB)
                        Dim v3 = ComputeVWorld(bw3Sim)
                        s3X += w * v3.X : s3Y += w * v3.Y : s3Z += w * v3.Z

                        ' S4: bone.world = M·inv(bind) where M = pxWorld·bind(C-X). NA if no C-X.
                        If s4Available Then
                            Dim bw4Sim = mS4.ComposeTransforms(invBindB)
                            Dim v4 = ComputeVWorld(bw4Sim)
                            s4X += w * v4.X : s4Y += w * v4.Y : s4Z += w * v4.Z
                        End If

                        ' S5: bone.world = inv(bind) · T_chunk_to_actor (general C-X via NIF tree).
                        If s5Available Then
                            Dim bw5Sim = tChunkToActor.ComposeTransforms(invBindB)
                            Dim v5 = ComputeVWorld(bw5Sim)
                            s5X += w * v5.X : s5Y += w * v5.Y : s5Z += w * v5.Z
                        End If
                    Next

                    Dim vLLog = vLSim, sumWLog = sumWSim, cxLog = cxBoneName
                    Logger.LogLazy(Function() $"[STRATEGY-SIMULATE] shape='{shNameSim}' v[0]_local=({vLLog.X:F2},{vLLog.Y:F2},{vLLog.Z:F2}) sumW={sumWLog:F3} cxBone={If(String.IsNullOrEmpty(cxLog), "<none>", "'" & cxLog & "'")}")
                    Dim s1XL = s1X, s1YL = s1Y, s1ZL = s1Z
                    Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S1 (current/actor-bone):                 ({s1XL:F2},{s1YL:F2},{s1ZL:F2})")
                    Dim s2XL = s2X, s2YL = s2Y, s2ZL = s2Z
                    Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S2 (chunkRoot=identity, bone=inv(bind)): ({s2XL:F2},{s2YL:F2},{s2ZL:F2})")
                    Dim s3XL = s3X, s3YL = s3Y, s3ZL = s3Z
                    Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S3 (chunkRoot=socketLocal, no parent):   ({s3XL:F2},{s3YL:F2},{s3ZL:F2})")
                    If s4Available Then
                        Dim s4XL = s4X, s4YL = s4Y, s4ZL = s4Z
                        Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S4 (CX-align: M=P-X.world·bind(C-X)):    ({s4XL:F2},{s4YL:F2},{s4ZL:F2})")
                    Else
                        Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S4 (CX-align): NA (no C-X skinning bone in shape)")
                    End If
                    If s5Available Then
                        Dim s5XL = s5X, s5YL = s5Y, s5ZL = s5Z
                        Dim cxNiL = cxNiNodeName
                        Dim cxgT = cxChunkGlobalT.Translation
                        Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S5 (CX-NIF-tree: cx='{cxNiL}' chunkT=({cxgT.X:F2},{cxgT.Y:F2},{cxgT.Z:F2})): ({s5XL:F2},{s5YL:F2},{s5ZL:F2})")
                    Else
                        Logger.LogLazy(Function() $"[STRATEGY-SIMULATE]   S5 (CX-NIF-tree): NA (C-X NiNode not found in chunk NIF)")
                    End If
                Next
            Catch ex As Exception
                Logger.LogLazy(Function() $"[STRATEGY-SIMULATE] EXCEPTION: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If

        Dim shapesPreCount = renderData.Shapes.Count
        Dim mountsPreCount = renderData.ShapeMountSocket.Count
        Logger.LogLazy(Function() $"[RENDER-DISPATCH] shapes={shapesPreCount} ShapeMountSocket={mountsPreCount} (pre-render)")
        For Each sh In renderData.Shapes
            ' The per-shape RENDER-DISPATCH dump below is pure diagnostics, but it eagerly builds
            ' ~16 material/texture .ToString() strings per shape BEFORE the lazy log. Skip it
            ' entirely when logging is off so a normal render doesn't pay it on the UI thread.
            If Not Logger.Enabled Then Continue For
            Dim shapeName = sh.ShapeName
            Dim hide = sh.RenderHide
            Dim hasMount = renderData.ShapeMountSocket.ContainsKey(sh)
            Dim socketName As String = ""
            If hasMount Then socketName = renderData.ShapeMountSocket(sh).Name
            Logger.LogLazy(Function() $"[RENDER-DISPATCH-SHAPE] '{shapeName}' hide={hide} socket='{socketName}'")

            Dim sm = sh.ShapeMaterial
            Dim mPath As String = "<no-mat>"
            Dim mAT As String = "?"
            Dim mATRef As String = "?"
            Dim mABM As String = "?"
            Dim mDiff As String = ""
            Dim mShader As String = "?"
            Dim mFacegen As String = "?"
            Dim mSkinTint As String = "?"
            Dim mHair As String = "?"
            Dim mDecal As String = "?"
            Dim mTwoSided As String = "?"
            Dim mAlphaBlendFlag As String = "?"
            Dim mTintR As String = "?"
            Dim mTintG As String = "?"
            Dim mTintB As String = "?"
            If sm IsNot Nothing Then
                mPath = If(sm.path, "")
                If sm.material IsNot Nothing Then
                    mAT = sm.material.AlphaTest.ToString()
                    mATRef = sm.material.AlphaTestRef.ToString()
                    mABM = sm.material.AlphaBlendMode.ToString()
                    mDiff = If(sm.material.Diffuse_or_Base_Texture, "")
                    mShader = sm.material.NifShaderType.ToString()
                    mFacegen = sm.material.Facegen.ToString()
                    mSkinTint = sm.material.SkinTint.ToString()
                    mHair = sm.material.Hair.ToString()
                    mDecal = sm.material.Decal.ToString()
                    mTwoSided = sm.material.TwoSided.ToString()
                    ' Render shader reads SkinTintColor (alias of BGSM HairTintColor) if SkinTint=True,
                    ' else HairTintColor if Hair=True. Both back the same BGSM byte.
                    Dim activeTint = If(sm.material.SkinTint, sm.material.SkinTintColor, sm.material.HairTintColor)
                    mTintR = activeTint.R.ToString()
                    mTintG = activeTint.G.ToString()
                    mTintB = activeTint.B.ToString()
                End If
            End If
            Dim hasVC As String = "?"
            Dim hasVA As String = "?"
            If sh.Geometry IsNot Nothing Then
                hasVC = sh.Geometry.HasVertexColors.ToString()
            End If
            If sh.NifShader IsNot Nothing Then
                hasVA = sh.NifShader.HasVertexAlpha.ToString()
            End If
            Dim showVC = sh.ShowVertexColor.ToString()
            Dim mPathLog = mPath
            Dim mAtLog = mAT
            Dim mAtRefLog = mATRef
            Dim mAbmLog = mABM
            Dim mDiffLog = mDiff
            Dim mShaderLog = mShader
            Dim mFacegenLog = mFacegen
            Dim mSkinTintLog = mSkinTint
            Dim mHairLog = mHair
            Dim mDecalLog = mDecal
            Dim mTwoSidedLog = mTwoSided
            Dim mTintRLog = mTintR
            Dim mTintGLog = mTintG
            Dim mTintBLog = mTintB
            Dim hasVcLog = hasVC
            Dim hasVaLog = hasVA
            Dim showVcLog = showVC
            Dim shapeNameDispatch = shapeName
            Logger.LogLazy(Function() $"[RENDER-DISPATCH-MAT] '{shapeNameDispatch}' path='{mPathLog}' AT={mAtLog} ATRef={mAtRefLog} ABM={mAbmLog} shader={mShaderLog} facegen={mFacegenLog} skinTint={mSkinTintLog} hair={mHairLog} decal={mDecalLog} twoSided={mTwoSidedLog} tintRGB=({mTintRLog},{mTintGLog},{mTintBLog})")
            Logger.LogLazy(Function() $"[RENDER-DISPATCH-TEX] '{shapeNameDispatch}' diffuse='{mDiffLog}' hasVC={hasVcLog} hasVA={hasVaLog} showVC={showVcLog}")
        Next

        Logger.LogLazy(Function() $"[PERF-BRP] meatcap + chunk-mount injection @ {_swBrp.ElapsedMilliseconds}ms")
        Dim request As New RenderRequest With {
            .Shapes = renderData.Shapes,
            .SkeletonResolver = skelResolver,
            .MorphResolver = morphResolver,
            .BaseGeometryProvider = headBake,
            .RecalculateNormals = Config_App.Current.Setting_RecalculateNormals,
            .ResetCamera = True
        }
        Logger.LogLazy(Function() $"[PERF-BRP] ===== BuildRenderPlan TOTAL = {_swBrp.ElapsedMilliseconds}ms =====")
        Return New RenderPlanResult With {.RenderData = renderData, .Inst = inst, .HeadInst = headInst, .SkelByArma = skelByArma, .SculptByArma = sculptByArma, .ShapeToSkel = shapeToSkel, .Request = request}
    End Function

    ''' <summary>Enable/disable the render-toggle checkboxes. Called around the background render compute
    ''' (BuildRenderPlan on Task.Run): disabling them while the compute runs prevents a CheckedChanged
    ''' handler from reassigning <c>_renderHost.Toggles</c> mid-compute, which would otherwise let
    ''' BuildRenderPlan + the morph resolvers read an inconsistent toggle snapshot off the UI thread.
    ''' Only the toggles are frozen; the NPC tree / outfit combo stay live (their selection is captured
    ''' into the request BEFORE the await, and overlapping renders are serialized by _renderGate).</summary>
    Private Sub SetRenderTogglesEnabled(enabled As Boolean)
        CheckBoxApplyBoneMorphs.Enabled = enabled
        CheckBoxApplyVertexMorphs.Enabled = enabled
        CheckBoxApplyBodyWeight.Enabled = enabled
        CheckBoxApplySculpt.Enabled = enabled
        CheckBoxBodyTri.Enabled = enabled
        CheckBoxRenderArmor.Enabled = enabled
        CheckBoxRenderUnderarmor.Enabled = enabled
        CheckBoxRenderBody.Enabled = enabled
        CheckBoxRenderHeadwear.Enabled = enabled
        ' Gore: en Skyrim queda deshabilitado SIEMPRE (no hay meatcaps que ocultar), así que el re-enable
        ' post-render no puede resucitarlo. El valor persistido se conserva igual.
        CheckBoxRenderGore.Enabled = enabled AndAlso RenderToggleLabels.GoreEnabledForGame()
    End Sub

    ''' <summary>Rótulos + tooltips game-aware de los 10 checkboxes de la toolbar de preview, y deshabilitado
    ''' del gore bajo Skyrim. Se llama en Load (el juego ya viene pineado del Preflight).</summary>
    Private Sub ApplyRenderToggleLabelsForGame()
        RenderToggleLabels.Apply(CheckBoxApplyBoneMorphs, CheckBoxApplyVertexMorphs, CheckBoxApplyBodyWeight,
                                 CheckBoxApplySculpt, CheckBoxBodyTri,
                                 CheckBoxRenderBody, CheckBoxRenderUnderarmor, CheckBoxRenderArmor,
                                 CheckBoxRenderHeadwear, CheckBoxRenderGore)
        ' El grupo Load/Save de presets es LooksMenu (F4SE) en FO4 y RaceMenu (SKSE) en Skyrim.
        LabelLooksMenu.Text = If(RenderToggleLabels.IsSse(), "RaceMenu:", "LooksMenu:")
    End Sub

    ''' <summary>Last step of a render — invoked by the post-texture-upload hook AFTER face tint
    ''' bake passes have completed and the shapes have been revealed. Resets the camera RESPECTING
    ''' the Settings_Camara flags (FreezeCamera/ResetZoom/ResetAngles — same as WM/the render pipeline
    ''' on selection; no Force) now that mesh bounds reflect the final visible pose, then triggers a
    ''' repaint. Deferred into the hook so it sees populated mesh bounds rather than the pre-reveal state.</summary>
    Private Sub FinalizeRenderCamera(host As NpcRenderHost)
        If host Is Nothing OrElse host.PreviewCtl Is Nothing Then Return
        Try
            host.PreviewCtl.ResetCamera()
            host.PreviewCtl.UpdateRequired = True
            host.PreviewCtl.RefreshRender()
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>Render the given NPC into the supplied host's preview. Used by editor forms
    ''' to drive their embedded PreviewControl independently of the main form's preview.
    ''' The targetHost must already have its Toggles and AppliedPresets configured before
    ''' this call (typically Toggles via OnlyFace/FullBody preset and AppliedPresets shared
    ''' by reference with the MainForm so live overlay edits inside the modal write through
    ''' to the same dict the MainForm will re-resolve from on close).
    ''' Friend (not Public) because <see cref="NpcRenderHost"/> is itself Friend; promoting this to
    ''' Public would leak the type out of the assembly.</summary>
    Friend Async Function RenderInHostAsync(targetHost As NpcRenderHost, npcFormID As UInteger) As Task
        ArgumentNullException.ThrowIfNull(targetHost)
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            Throw New InvalidOperationException($"NPC 0x{npcFormID:X8} not in cache.")
        End If
        Dim requestVersion = Threading.Interlocked.Increment(_previewRequestVersion)
        Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion, targetHost)
    End Function

    ''' <summary>Clear the blanket RenderHide=True set during the load+tint window, then apply
    ''' the diagnostic toggles on top. Idempotent — safe to call repeatedly.</summary>
    Private Sub RevealAllShapes(Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If host.LastRenderData Is Nothing OrElse host.LastRenderData.Shapes Is Nothing Then Return
        For Each sh In host.LastRenderData.Shapes
            sh.RenderHide = False
        Next
        host.ApplyRenderToggleVisibility()  ' Includes RefreshRender at the end.
    End Sub

    ''' <summary>Facade for the editors: delegates to the FaceTint resolver's live tint refresh
    ''' (rollback to pristine → re-compose face tints + skin SoftLight → refresh uniforms). Kept on
    ''' MainForm so EditFace_Form's <c>_mainForm.RefreshFaceTintLivePreview</c> call site stays
    ''' unchanged.</summary>
    Friend Function RefreshFaceTintLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Return _faceTintResolver.RefreshFaceTintLivePreview(host)
    End Function

    ''' <summary>Facade del editor de cuerpo: repinta SOLO el tono de piel del cuerpo con el ajuste manual
    ''' (re-resuelve el QNAM efectivo y reescribe los uniforms del soft-light). No recompone la cara ni toca
    ''' texturas -- el ajuste no entra por ahi-, asi que sirve para arrastrar un slider y para las iteraciones
    ''' del auto-calc.</summary>
    Friend Function RefreshBodySkinToneLive(offset As SkinToneQnamOffset, Optional host As NpcRenderHost = Nothing) As Boolean
        Return _faceTintResolver.RefreshBodySkinToneLive(offset, host)
    End Function

    ''' <summary>Tono de piel BASE del host (sin el ajuste manual). Nothing = este NPC no tiene tono derivable
    ''' -- la misma condicion con la que el render se saltea el soft-light del cuerpo-, y es lo que el editor
    ''' usa para deshabilitar el tab en vez de ofrecer sliders inertes.</summary>
    Friend Function ResolveBaseSkinToneForHost(host As NpcRenderHost) As Nullable(Of Color)
        If host Is Nothing OrElse host.LastRenderedState Is Nothing Then Return Nothing
        Return _materialResolver.ResolveNpcSkinToneColor(host.LastRenderedState)
    End Function

    ''' <summary>Tono de piel EFECTIVO del cuerpo (base + ajuste manual) = exactamente el QNAM que se va a
    ''' escribir en el ESP y a hornear. Solo para mostrarlo en el editor.</summary>
    Friend Function ResolveBodySkinToneForHost(host As NpcRenderHost) As Nullable(Of Color)
        If host Is Nothing OrElse host.LastRenderedState Is Nothing Then Return Nothing
        Return _materialResolver.ResolveNpcBodySkinToneColor(host.LastRenderedState)
    End Function

    ''' <summary>Facade for the editors: delegates to the skin-override live-preview fast-path. Kept on
    ''' MainForm so EditBody_Form's <c>_mainForm.RefreshBodySkinLivePreview</c> call site stays
    ''' unchanged.</summary>
    Friend Function RefreshBodySkinLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Return _skinLivePreview.RefreshBodySkinLivePreview(host)
    End Function

    ''' <summary>Re-resuelve las capas de overlay sobre el render data EXISTENTE del host (re-hornea el nuevo
    ''' UV/tint en los materiales de capa) y repinta, SIN un BuildRenderPlan completo: sin re-colectar mallas ni
    ''' recargar el NIF. Es para ediciones de propiedades de overlay (offset/escala/tint). False si el host
    ''' todavia no tiene render data, y el caller cae al reload completo.
    ''' <para>Es seguro para offset/escala/tint porque el conjunto de overlays y sus materiales de slot no
    ''' cambian: solo cambian los parametros del material de capa. Esas texturas ya estan en el diccionario de
    ''' la GPU desde el render inicial y la libreria bindea los overlays por path al dibujar, asi que un repaint
    ''' muestra los parametros nuevos sin recargar texturas ni reconstruir mallas.</para></summary>
    Friend Function RefreshOverlayLayersLive(host As NpcRenderHost) As Boolean
        If host Is Nothing OrElse host.LastRenderedState Is Nothing OrElse host.LastRenderData Is Nothing Then Return False
        ' EL HOST VA EXPLÍCITO, IGUAL QUE EN BuildRenderPlan. Omitirlo caía al `_hostProvider()` = el host
        ' overlays del pool magic: se veían al abrir (reload completo, que sí pasa el host) y desaparecían al
        ' primer arrastre del slider de Opacity, sin volver hasta el próximo reload completo. Es exactamente el
        ' modo de falla contra el que existe el parámetro.
        _morphPoseResolver.ResolveOverlayLayers(host.LastRenderedState, host.LastRenderData, host)
        If host.PreviewCtl IsNot Nothing Then host.PreviewCtl.InvalidateRender()
        Return True
    End Function













    ' Naming helpers moved to FaceTintInputBuilder. Wrappers preserve the existing
    ' private signatures used by other MainForm members (TintPickerDialog, EditFace_Form,
    ' diagnostic logs, etc).
    Private Shared Function TintSlotName(slot As UShort) As String
        Return FaceTintInputBuilder.TintSlotName(slot)
    End Function

    Private Shared Function BlendOpName(op As UInteger) As String
        Return FaceTintInputBuilder.BlendOpName(op)
    End Function

    Private Shared Function FormatTintFlagsName(flags As UShort) As String
        Return FaceTintInputBuilder.FormatTintFlagsName(flags)
    End Function

    Private Shared Function NormalizeDictionaryKeyWithTexturesPrefix(rawPath As String) As String
        Return FaceTintInputBuilder.NormalizeDictionaryKeyWithTexturesPrefix(rawPath)
    End Function



    ''' <summary>Build the outfit combo entries for the current base state: up to two entries
    ''' (Default + Sleep), each holding one sampled realization of ARMO FormIDs. Reroll re-samples
    ''' a single entry via <see cref="OutfitResolver.SampleOutfitRealization"/>.</summary>
    Private Function BuildOutfitComboEntries(state As NPCVisualState) As List(Of OutfitComboEntry)
        Dim entries As New List(Of OutfitComboEntry)
        If state Is Nothing Then Return entries

        AddOutfitEntryIfPresent(entries, state.DefaultOutfitFormID, OutfitSlotKind.DefaultOutfit, "Default")
        AddOutfitEntryIfPresent(entries, state.SleepOutfitFormID, OutfitSlotKind.SleepOutfit, "Sleep")

        Return entries
    End Function

    Private Sub AddOutfitEntryIfPresent(entries As List(Of OutfitComboEntry), otftFormID As UInteger, kind As OutfitSlotKind, slotName As String)
        If otftFormID = 0UI Then Return

        ' Outfit draft (Create tab): ARMO items render directly; LVLI items are sampled to their cached
        ' realization (stable until Reroll). ResolveDraftArmoList does that flattening so the render shows
        ' the current realization. Slot conflicts handled downstream by SelectWinningCandidates.
        Dim draft = TryGetOutfitDraft(otftFormID)
        If draft IsNot Nothing Then
            Dim realized = ResolveDraftArmoList(draft)
            entries.Add(New OutfitComboEntry With {
                .Label = $"{slotName} — {draft.Record.EditorID} ({realized.Count} pcs) [draft]",
                .SlotKind = kind,
                .OutfitFormID = otftFormID,
                .SampledArmorFormIDs = realized,
                .SampledArmorContextKeywords = New Dictionary(Of UInteger, List(Of UInteger))
            })
            Return
        End If

        Dim otftRec = _pluginManager.GetRecord(otftFormID)
        If otftRec Is Nothing OrElse otftRec.Header.Signature <> "OTFT" Then
            Return
        End If

        Dim warnings As New List(Of String)
        Dim picks = OutfitResolver.SampleOutfitWithKeywords(otftFormID, _pluginManager, warnings)
        For Each w In warnings
        Next

        Dim sampled = picks.Select(Function(p) p.ArmoFormID).ToList()
        Dim ctxKeywords = picks.ToDictionary(Function(p) p.ArmoFormID, Function(p) p.ContextKeywords)
        Dim otftLabel = If(otftRec.EditorID <> "", otftRec.EditorID, otftFormID.ToString("X8"))
        entries.Add(New OutfitComboEntry With {
            .Label = $"{slotName} — {otftLabel} ({sampled.Count} pcs)",
            .SlotKind = kind,
            .OutfitFormID = otftFormID,
            .SampledArmorFormIDs = sampled,
            .SampledArmorContextKeywords = ctxKeywords
        })
    End Sub

    ' RenderVariantAsync and PopulateVariantNodes removed — replaced by on-demand RenderCurrentStateAsync + outfit combo.

    Private Async Function EnsureAssetDictionaryAsync() As Task
        Dim loadTask As Task = Nothing

        SyncLock _assetDictionaryLock
            If _assetDictionaryLoadTask Is Nothing Then
                ToolStripProgressBar1.Visible = True
                ToolStripProgressBar1.Minimum = 0
                ToolStripProgressBar1.Maximum = 1
                ToolStripProgressBar1.Value = 0

                Dim progress = New Progress(Of (Stepn As String, Value As Integer, Max As Integer))(
                    Sub(info) UpdateAssetLoadProgress(info)
                )

                Dim cacheDir = IO.Path.Combine(Application.StartupPath, "Caches")
                IO.Directory.CreateDirectory(cacheDir)
                FilesDictionary_class.CacheDirectory = cacheDir
                ' .ssf (Segment Sub-File) maps BSSubIndexTriShape SubSegment BoneIDs to symbolic
                ' gore-zone names for FO4 actor meshes. .sclp (ARMA Sculpt) carries per-bone
                ' translation/scale deltas referenced by ARMA records. Both are NPC-rendering
                ' specific so they're registered here, not in the shared library default set.
                ' (RaceMenu/LooksMenu config — .ini/.jslot/.slot, and Papyrus .pex/.psc — lives in the library's
                ' DEFAULT set: both plugins read it through the game's archive layer, so it is not
                ' NPC-Manager-specific. Only the SSE editors PARSE the .pex, via RaceMenuPaintCatalog below.)
                _assetDictionaryLoadTask = FilesDictionary_class.Fill_DictionaryAsync(_dataPath, progress)
            End If

            loadTask = _assetDictionaryLoadTask
        End SyncLock

        Await loadTask

        ' Drenar SIEMPRE (la cola de FilesDictionary crece un item por archivo escaneado si nadie la vacia);
        ' el desglose es diagnostico y va gateado — o se loguea, o no se calcula (no dejar un
        ' `For Each ... Next` de cuerpo vacio que arme datos que nadie usa).
        Dim scanReport = FilesDictionary_class.DrainScanReport()
        If Logger.Enabled AndAlso scanReport.Count > 0 Then
            Dim hits As Integer = 0
            For Each r In scanReport
                If r.CacheHit Then hits += 1
            Next
            Dim total = scanReport.Count
            Dim misses = total - hits
            Logger.LogLazy(Function() $"[ASSET-SCAN] archives={total} indexCacheHits={hits} rescans={misses}")
            For Each r In scanReport
                Dim nm = r.ArchiveName, hit = r.CacheHit
                Logger.LogLazy(Function() $"[ASSET-SCAN]   '{nm}' {If(hit, "cache", "RESCAN")}")
            Next
        End If


        ' LOS CATÁLOGOS SE POBLAN EN UN SITIO COMPARTIDO. Estaban acá adentro, o sea que sólo
        ' existían si el usuario abría la GUI: BakeAllRunner y FO4_FaceTint_CLI nunca ejecutan MainForm,
        ' así que el bake headless de Skyrim corría con RaceCompatCatalog = Nothing y horneaba
        ' head-parts DISTINTOS de los que hornea la GUI para el mismo NPC. Ver NpcSessionCatalogs.
        NpcSessionCatalogs.EnsureLoaded(_pluginManager)

        ' Asset dictionary ready; hide the progress bar.
        If InvokeRequired Then
            BeginInvoke(Sub() ToolStripProgressBar1.Visible = False)
        Else
            ToolStripProgressBar1.Visible = False
        End If
    End Function




    ''' <summary>Refresca los insumos del <see cref="HeadBakeService"/> vivo (si hay). Devuelve True si la
    ''' firma cambió, o sea si el próximo paso de morphs va a re-hornear.
    ''' <para>Hace falta en TODO camino que cambie FMRS / morphs de chargen / body-weight sin rearmar el
    ''' composite de morphs: sin esto el servicio conserva la firma con la que nació y devuelve el horneado
    ''' cacheado, con lo que el cambio no se ve. Los caminos que SÍ rearman el composite ya se refrescan
    ''' dentro de <see cref="BuildCompositeMorphResolver"/>.</para></summary>
    Friend Function RefreshHeadBakeInputs(host As NpcRenderHost) As Boolean
        If host Is Nothing Then Return False
        Dim hb = host.LastHeadBakeService
        If hb Is Nothing OrElse hb.RegisteredCount = 0 Then Return False
        Dim bs As FaceGenBuildPipeline.BakeState = Nothing
        Dim sg As String = ""
        Dim ac As Boolean = True
        If Not TryBuildHeadBakeInputs(host.LastRenderedState, host, bs, sg, ac) Then Return False
        Return hb.UpdateInputs(bs, sg, ac)
    End Function

    ''' <summary>Arma el <c>BakeState</c> + la firma del head-bake HONRANDO LOS TOGGLES. Extraído para que
    ''' <see cref="BuildHeadBakeService"/> (render completo) y <see cref="BuildCompositeMorphResolver"/>
    ''' (los SEIS handlers de toggle) usen exactamente los mismos insumos — si divergieran, un toggle
    ''' cambiaría la firma pero no el estado con el que se hornea, o al revés.</summary>
    Private Function TryBuildHeadBakeInputs(state As NPCVisualState, host As NpcRenderHost,
                                             ByRef bakeState As FaceGenBuildPipeline.BakeState,
                                             ByRef signature As String, ByRef applyChargen As Boolean) As Boolean
        bakeState = Nothing : signature = "" : applyChargen = True
        If state Is Nothing OrElse host Is Nothing Then Return False
        applyChargen = host.Toggles Is Nothing OrElse host.Toggles.ApplyVertexMorphs
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(state.FormID, _ctx.PluginManager, _appliedPresets)
        If npcData Is Nothing Then Return False

        ' FMRS OFF ⇒ BakeState sin regiones faciales ⇒ FmrsPose = Nothing ⇒ la base sale en bind pose,
        ' que es exactamente lo que el checkbox significa ("Off = bind pose de esos huesos").
        Dim boneMorphsOn = host.Toggles IsNot Nothing AndAlso host.Toggles.ApplyBoneMorphs AndAlso
                           Not host.PreviewGenderOverride.HasValue
        Dim regionsFile As FacialBoneRegionsFile = Nothing
        If boneMorphsOn Then
            Dim raceRec = _ctx.PluginManager.GetRecord(npcData.Record.Race)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                regionsFile = NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution(
                    Canon.CanonRecords.Race(raceRec, _ctx.PluginManager), npcData.Record.ConfigurationFlagsFemale)
            End If
        End If

        bakeState = FaceGenBuildPipeline.BuildBakeState(state.FormID, _ctx.PluginManager, _appliedPresets, regionsFile)
        If bakeState Is Nothing Then Return False
        ' Body-weight OFF ⇒ sin MWGT/MRSV en el bake, igual que el render deja la pose sin peso.
        ' Mutar acá es seguro: ResolveOverlaidNpcData devuelve un parse FRESCO (GetParsedNpc no cachea).
        If host.Toggles IsNot Nothing AndAlso Not host.Toggles.ApplyBodyWeight AndAlso bakeState.NpcData IsNot Nothing Then
            ' Solo si el record TRAE MWGT: escribir el centinela donde no habia nada le crearia el
            ' subrecord a un NPC que no lo declara.
            If bakeState.NpcData.Record.PesoDelCuerpo(0).HasValue OrElse
               bakeState.NpcData.Record.PesoDelCuerpo(1).HasValue OrElse
               bakeState.NpcData.Record.PesoDelCuerpo(2).HasValue Then
                bakeState.NpcData.Record.PonerPesoDelCuerpo(0, Nothing)
                bakeState.NpcData.Record.PonerPesoDelCuerpo(1, Nothing)
                bakeState.NpcData.Record.PonerPesoDelCuerpo(2, Nothing)
            End If
        End If
        signature = HeadBakeService.BuildSignature(bakeState.NpcData, npcData.Record.Race, host.Toggles)
        Return True
    End Function

    ''' <summary>Arma el <see cref="HeadBakeService"/> del NPC actual, o <c>Nothing</c> si el gate está
    ''' apagado o no hay shapes gateadas. Cada shape del NIF PLANO que el collector dejó sin redirigir trae
    ''' en <c>renderData.ShapeFaceBonesKeys</c> el dictKey de su hermano <c>_faceBones</c>: se carga ese NIF
    ''' (uno por key, cacheado) y se aparea la shape por nombre normalizado + VertexCount, que es la MISMA
    ''' regla que usa el driver del motor.
    ''' <para><b>Guarda por candidato</b>: si alguna shape del NIF no aparea, no se registra NINGUNA de ese
    ''' NIF y todas quedan como estaban — degrada, no rompe.</para>
    ''' <para><b>Los toggles se honran acá</b>, construyendo el <c>BakeState</c> con o sin FMRS: es lo que
    ''' mantiene vivo el checkbox de bone-morphs ahora que la deformación vive en las posiciones horneadas
    ''' y no en los huesos.</para></summary>
    Private Function BuildHeadBakeService(state As NPCVisualState, renderData As PreviewResolutionResult,
                                           host As NpcRenderHost) As HeadBakeService
        If Not NPC_Config.IsHeadBakeActive() Then Return Nothing
        If renderData Is Nothing OrElse renderData.ShapeFaceBonesKeys.Count = 0 Then Return Nothing
        If state Is Nothing OrElse host Is Nothing Then Return Nothing

        Try
            Dim bakeState As FaceGenBuildPipeline.BakeState = Nothing
            Dim sig As String = ""
            Dim applyChargen As Boolean = True
            If Not TryBuildHeadBakeInputs(state, host, bakeState, sig, applyChargen) Then Return Nothing
            Dim svc As New HeadBakeService(bakeState, sig, applyChargen)

            ' Un NIF `_faceBones` por dictKey (varias shapes lo comparten).
            Dim fbnsByKey As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)
            ' Agrupar por key para poder aplicar la guarda "todas o ninguna".
            For Each grp In renderData.ShapeFaceBonesKeys.GroupBy(Function(kv) kv.Value, StringComparer.OrdinalIgnoreCase)
                Dim fbnsNif As Nifcontent_Class_Manolo = Nothing
                If Not fbnsByKey.TryGetValue(grp.Key, fbnsNif) Then
                    Dim bytes = MeshPathHelpers.TryLoadMeshBytes(grp.Key)
                    If bytes IsNot Nothing Then
                        Try
                            fbnsNif = New Nifcontent_Class_Manolo()
                            fbnsNif.Load_Manolo(bytes)
                        Catch ex As Exception
                            fbnsNif = Nothing
                        End Try
                    End If
                    fbnsByKey(grp.Key) = fbnsNif
                End If
                If fbnsNif Is Nothing Then Continue For

                ' Índice de las shapes del `_faceBones` por (nombre sin sufijo, VertexCount).
                Dim fbnsIdx As New Dictionary(Of String, INiShape)(StringComparer.OrdinalIgnoreCase)
                For Each fs In fbnsNif.GetShapes()
                    Dim nm = NameUtils.StripFaceBonesSuffix(If(fs.Name?.String, ""))
                    If nm = "" Then Continue For
                    fbnsIdx($"{nm}|{ShapeGeometryFactory.[For](fs, fbnsNif).VertexCount}") = fs
                Next

                ' Pasada 1: aparear TODAS antes de registrar ninguna (guarda por candidato).
                Dim pend As New List(Of (Flat As IRenderableShape, Fbns As INiShape))
                Dim allPaired As Boolean = True
                For Each kv In grp
                    Dim flat = kv.Key
                    If flat.NifShape Is Nothing OrElse flat.NifContent Is Nothing Then allPaired = False : Exit For
                    Dim key = $"{NameUtils.StripFaceBonesSuffix(If(flat.NifShape.Name?.String, ""))}|{ShapeGeometryFactory.[For](flat.NifShape, flat.NifContent).VertexCount}"
                    Dim fbShape As INiShape = Nothing
                    If Not fbnsIdx.TryGetValue(key, fbShape) Then allPaired = False : Exit For
                    pend.Add((flat, fbShape))
                Next
                If Not allPaired Then
                    Dim keyLog = grp.Key
                    Logger.LogLazy(Function() $"[HEAD-BAKE] '{keyLog}': alguna shape no aparea -> se deja como antes (sin hornear)")
                    Continue For
                End If

                ' Pasada 2: registrar.
                For Each pr In pend
                    ' cg va CRUDO (el path real siempre): la decisión "aplicar o no" es VIVA en el servicio
                    ' (_applyChargen), porque el toggle vertex-morphs se conmuta sin re-registrar las Entry.
                    Dim cg As String = "" : renderData.ShapeChargenTriPaths.TryGetValue(pr.Flat, cg)
                    Dim rm As String = "" : renderData.ShapeRaceMorphTriPaths.TryGetValue(pr.Flat, rm)
                    svc.Register(pr.Flat.NifContent, pr.Flat.NifShape, fbnsNif, pr.Fbns, cg, rm)
                Next
            Next

            If svc.RegisteredCount = 0 Then Return Nothing
            Return svc
        Catch ex As Exception
            Logger.LogLazy(Function() $"[HEAD-BAKE] fallo armando el servicio: {ex.GetType().Name}: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>Compone los resolvers de morph de cara y de cuerpo. Los morphs de cara (FRTRI003) y los de
    ''' BodySlide (PIRT) viajan por el mismo MorphPlan pero nunca chocan: el lookup de cada shape se hace
    ''' contra su PROPIO <c>.tri</c>. MultiMorphResolver junta las listas de canales y ApplyMorphPlan las
    ''' recorre todas por shape, de forma uniforme.
    ''' <para>Los toggles son granulares por pipeline: uno gatea los canales de FORMA de la cara (en SSE, vía
    ''' applyChargenMorphs; el SkinnyMorph del peso sigue al toggle de body-weight) y otro gatea únicamente
    ''' el resolver PIRT del cuerpo.</para></summary>
    Friend Function BuildCompositeMorphResolver(state As NPCVisualState, renderData As PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _renderHost
        Dim face As IMorphResolver = Nothing
        ' SSE: el SkinnyMorph (weight de cabeza/pelo) vive DENTRO del plan de cara, así que el face
        ' resolver se construye también con "Vertex morphs" OFF mientras "Body weight" esté ON — el
        ' resolver recibe applyChargenMorphs:=Toggles.ApplyVertexMorphs y suprime la FORMA pero no el
        ' weight (antes: OFF mataba el resolver entero ⇒ cabeza en peso neutro + cuerpo _0/_1 lerpeado
        ' ⇒ costura de cuello). FO4: sin SkinnyMorph en el plan ⇒ gate por ApplyVertexMorphs como siempre.
        Dim sseNeedsFaceForWeight = Config_App.Current IsNot Nothing AndAlso
            Config_App.Current.Game = Config_App.Game_Enum.Skyrim AndAlso host.Toggles.ApplyBodyWeight
        If host.Toggles.ApplyVertexMorphs OrElse sseNeedsFaceForWeight Then
            face = _morphPoseResolver.BuildFaceMorphResolver(state, renderData, host)
        End If
        Dim body = _morphPoseResolver.BuildBodyMorphResolver(state, renderData, host)
        ' SSE vanilla body-weight (_0/_1) vertex LERP. SSE-only (Nothing on FO4), so this is inert for
        ' FO4 and MultiMorphResolver filters the null. Ordered after face, before BodySlide.
        Dim sseBodyWeight = _morphPoseResolver.BuildSseBodyWeightResolver(state, renderData, host)
        ' SSE HEAD/HAIR weight morph is now folded into the FACE resolver's plan (SkinnyMorph channel added by
        ' BuildFaceMorphPlanFromNam9 from each shape's own mesh tri), so head+hair track the body neck across
        ' weight per-part and race-aware — no separate head-weight resolver / hardcoded delta table.
        ' Hair zap resolver: emite el/los canal(es) de zap para las shapes Hair {30,31} marcadas con
        ' ZapParts (Top/Long/Both) según el modelo complementario main/hairline. Gated por "Render
        ' headwear": OFF → no se engancha → la mesh se destapa en el próximo pase de morphs (igual que la
        ' oclusión de la mesh entera). Se incluye INDEPENDIENTE de los morphs face/body (un NPC con ambos
        ' morphs OFF igual debe zapear bajo gorra), y debe ser el ÚLTIMO delegate del composite así su
        ' canal de zap se agrega después de los canales de posición — el orden no afecta el resultado
        ' (zap = mask, position = vertex) pero mantiene el zap visible al final del plan.
        Dim hairTopZap = _morphPoseResolver.BuildHairTopZapResolver(renderData, host)

        ' Junta los delegates no-nulos. MultiMorphResolver filtra nulls, así que paso los tres.
        ' Camino head-bake — REFRESCAR LOS INSUMOS PRIMERO, ANTES de cualquier early-return.
        ' Esto va arriba del `delegates.Length = 0` a propósito: los seis handlers de toggle rearman el
        ' composite y marcan Morphs pero NO pasan por BuildRenderPlan, así que este es el único punto donde
        ' el servicio se entera de que cambió un toggle. Si vive DESPUÉS del early-return, entonces cuando el
        ' composite queda vacío (p.ej. vertex-morphs OFF y ningún otro canal — depende del estado de los
        ' OTROS checkboxes) el refresh se saltea, el servicio conserva la firma vieja y el provider NO
        ' re-hornea ⇒ el toggle "a veces anda a veces no". El provider (IBaseGeometryProvider) es
        ' independiente del MorphResolver: re-hornea con sólo cambiar la firma, aunque el composite sea Nothing.
        Dim hb = host?.LastHeadBakeService
        Dim headBakeActive = hb IsNot Nothing AndAlso hb.RegisteredCount > 0
        If headBakeActive Then
            Dim bs As FaceGenBuildPipeline.BakeState = Nothing
            Dim sg As String = ""
            Dim ac As Boolean = True
            If TryBuildHeadBakeInputs(state, host, bs, sg, ac) Then hb.UpdateInputs(bs, sg, ac)
        End If

        Dim delegates = New IMorphResolver() {face, sseBodyWeight, body, hairTopZap}.Where(Function(r) r IsNot Nothing).ToArray()
        ' Composite vacío ⇒ no hay canales que emitir. El provider igual re-horneó (refresh de arriba), así
        ' que las shapes gateadas ya tienen su base correcta; devolver Nothing para el MorphResolver es sano.
        If delegates.Length = 0 Then Return Nothing
        Dim composite As IMorphResolver = If(delegates.Length = 1, delegates(0), New MultiMorphResolver(delegates))

        ' En las shapes gateadas la base YA trae los morphs de chargen (el bake los aplica, igual que el CK).
        ' Emitirlos otra vez como canal los aplicaría DOS VECES ⇒ se filtran los canales de POSICIÓN y sólo
        ' pasan los de ZAP.
        If headBakeActive Then Return New HeadBakeZapOnlyResolver(composite, hb)
        Return composite
    End Function

    ''' <summary>Envoltorio del composite para el camino head-bake: en las shapes gateadas devuelve SÓLO
    ''' los canales <c>IsZap</c>; en las demás delega tal cual. Ver el porqué en
    ''' <see cref="HeadBakeService.IsGated"/>.
    ''' <para>PROVISORIO junto con el resto del gate: cuando se borre el toggle, esto se queda (el
    ''' filtro es parte del camino definitivo), pero el <c>If hb IsNot Nothing</c> deja de hacer falta.</para></summary>
    Private NotInheritable Class HeadBakeZapOnlyResolver
        Implements IMorphResolver
        Private ReadOnly _inner As IMorphResolver
        Private ReadOnly _svc As HeadBakeService
        Public Sub New(inner As IMorphResolver, svc As HeadBakeService)
            _inner = inner : _svc = svc
        End Sub
        Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan _
            Implements IMorphResolver.ResolveMorphPlan
            Dim plan = _inner?.ResolveMorphPlan(shape, geom)
            If plan Is Nothing OrElse Not _svc.IsGated(shape) Then Return plan
            If plan.Channels Is Nothing OrElse plan.Channels.Count = 0 Then Return plan
            Dim zaps = plan.Channels.Where(Function(c) c IsNot Nothing AndAlso c.IsZap).ToList()
            If zaps.Count = plan.Channels.Count Then Return plan
            Dim outPlan As New MorphPlan()
            For Each z In zaps : outPlan.Channels.Add(z) : Next
            Return outPlan
        End Function
    End Class


    ''' <summary>SkeletonInstance por NPC con las tres fuentes del engine, en orden:
    ''' RACE.ANAM (base) → BPTD.MODL (skeleton real vía RACE.GNAM; aporta el SkeletonRef de robots)
    ''' → face bones (convención de filesystem `_[gender_]faceBones.nif`, solo chargen).
    ''' El inject de cloth bones (PrepareForShapes) va AL FINAL: antes del merge esos bones
    ''' figuran como missing y el inject falla en silencio. Detalle: memoria
    ''' 25-cloth-inyeccion-de-huesos / 24-robots-huesos-inyectados. El caller aplica ApplyPose después.</summary>
    Private Function PrepareSkeleton(state As NPCVisualState, renderData As PreviewResolutionResult) As SkeletonInstance
        Dim s As New SkeletonInstance()
        ' Source 1: RACE.ANAM
        s.LoadFromKey(renderData.SkeletonKey)
        ' Source 1b: HKX de animación (behavior rigName) como base AUTORITATIVA — huesos compartidos al world
        ' del HKX + agrega solo-HKX (Weapon/IK, chunk-bones de robot + C-). HKX Nothing → no-op (fallback NIF).
        ' Reusa la misma resolución que la anim-bar (project→character→rigName, ver MainForm ~1029).
        ' HKX/BPTD/face bytes depend only on (race, gender) — identical across the per-ARMA calls of
        ' one render. Memoize the loads (caches die with the load order, see InvalidateParseCaches) so a
        ' render with N distinct ARMAs loads each skeleton source once, not N times. The SkeletonInstance
        ' assembly (LoadFromKey / MergeHkxSkeleton / MergeAdditionalSkeleton / PrepareForShapes) stays
        ' per-call — only the expensive byte loads are shared.
        Dim raceGenderKey = (state.RaceFormID, state.IsFemale)
        Try
            Dim hkxBytes = _skelHkxBytesCache.GetOrAdd(raceGenderKey,
                Function(k)
                    Dim rbSkel = RaceBehaviorResolver.ResolveRaceBehavior(k.Race, _pluginManager)
                    If rbSkel Is Nothing Then Return Nothing
                    rbSkel.IsFemale = k.Female
                    Dim hkxLoader As Func(Of String, Byte()) = AddressOf LoadAnimHkxBytes
                    Return LoadAnimHkxBytes(BehaviorClipEnumerator.ResolveHavokSkeleton(rbSkel, hkxLoader))
                End Function)
            If hkxBytes IsNot Nothing Then
                Dim merged = s.MergeHkxSkeleton(hkxBytes)
                Logger.LogLazy(Function() $"[PREP-SKEL] HKX base merge: {merged} bones (hkxBytes={hkxBytes.Length})")
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[PREP-SKEL] HKX base merge failed (fallback NIF): {ex.GetType().Name}: {ex.Message}")
        End Try
        ' Source 2: BPTD.MODL (vía RACE.GNAM)
        Dim bptdBytes = _skelBptdBytesCache.GetOrAdd(state.RaceFormID,
            Function(raceFid) BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(raceFid, _pluginManager))
        If bptdBytes IsNot Nothing Then s.MergeAdditionalSkeleton(bptdBytes)
        ' Source 3: Face bones convention (chargen-only; convención filesystem, no engine record)
        Dim faceBytes = _skelFaceBytesCache.GetOrAdd(raceGenderKey,
            Function(k) FaceSkeletonResolver.TryLoadFaceSkeletonBytes(k.Race, k.Female, _pluginManager))
        If faceBytes IsNot Nothing Then s.MergeAdditionalSkeleton(faceBytes)
        s.PrepareForShapes(renderData.Shapes)
        Return s
    End Function

    Private Sub UpdateAssetLoadProgress(info As (Stepn As String, Value As Integer, Max As Integer))
        ToolStripProgressBar1.Visible = True
        ToolStripProgressBar1.Minimum = 0
        ToolStripProgressBar1.Maximum = Math.Max(1, info.Max)
        ToolStripProgressBar1.Value = Math.Max(0, Math.Min(info.Value, ToolStripProgressBar1.Maximum))
        SetStatus(info.Stepn)
    End Sub


    ' === Power-armor gating (registry-derived, no hardcoded race/FormID list) ===
    ' FO4 carries armor type in KWDA, not BOD2 (BOD2 = slot flags only). A piece is power armor iff its
    ' ARMO has the ArmorTypePower keyword; a race is a power-armor race iff its RACE.WNAM (Skin) ARMO is
    ' itself power armor (vanilla PowerArmorRace.WNAM = SkinPowerArmor [ArmorTypePower]). PA armatures also
    ' list HumanRace (for the inventory model), so the per-ARMA race check alone leaks PA pieces onto humans
    ' mounted wrong — hence this gate. The ArmorTypePower KYWD is resolved by EditorID (canonical schema
    ' name; one vanilla keyword mods reference but never redefine), so no FormID is hardcoded.
    Private _armorTypePowerKywdResolved As Boolean = False
    Private _armorTypePowerKywdFid As UInteger = 0UI
    Private ReadOnly _isPowerArmorArmoCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Boolean)
    Private ReadOnly _isPowerArmorRaceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, Boolean)

    ''' <summary>FormID of the vanilla <c>ArmorTypePower</c> KYWD, found once by EditorID. 0 if the load
    ''' order has no such keyword (then the gate is inert).</summary>
    Private Function ArmorTypePowerKeywordFid() As UInteger
        If Not _armorTypePowerKywdResolved Then
            _armorTypePowerKywdResolved = True
            _armorTypePowerKywdFid = FindArmorTypePowerKeywordFid(_pluginManager)
        End If
        Return _armorTypePowerKywdFid
    End Function

    ''' <summary>Shared core of <see cref="ArmorTypePowerKeywordFid"/>: EditorID scan for the vanilla
    ''' ArmorTypePower KYWD. 0 when the load order has no such keyword (gate inert — e.g. Skyrim, which
    ''' has no PA concept). Used by the cached instance resolver above AND by the bake path
    ''' (FaceGenBuilder.ResolveOutfitHeadwearSlots) so both apply the SAME keyword rule (RENDER == BAKE).</summary>
    Friend Shared Function FindArmorTypePowerKeywordFid(pluginManager As PluginManager) As UInteger
        Dim kywds = pluginManager.GetRecordsOfType("KYWD")
        If kywds IsNot Nothing Then
            For Each kw In kywds
                If kw IsNot Nothing AndAlso String.Equals(kw.EditorID, "ArmorTypePower", StringComparison.OrdinalIgnoreCase) Then
                    Return kw.Header.FormID
                End If
            Next
        End If
        Return 0UI
    End Function

    ''' <summary>Shared core of the PA piece rule (see gate comment above): a piece is power armor iff
    ''' its ARMO carries the ArmorTypePower keyword. Takes parsed data so draft-aware callers pass ctx
    ''' parses and the bake passes RecordParsers-direct parses — single source (RENDER == BAKE).</summary>
    Friend Shared Function IsPowerArmorArmoData(armo As Canon.IArmo, armorTypePowerKywdFid As UInteger) As Boolean
        Return armorTypePowerKywdFid <> 0UI AndAlso armo IsNot Nothing AndAlso
               armo.Keywords.Any(Function(k) k.Keyword = armorTypePowerKywdFid)
    End Function

    ''' <summary>Shared core of the PA race rule: a race is a power-armor race iff its RACE.WNAM (Skin)
    ''' ARMO is itself power armor. <paramref name="getParsedArmo"/> resolves the skin ARMO (ctx-cached
    ''' in the app, RecordParsers-direct in the bake) — single source (RENDER == BAKE).</summary>
    Friend Shared Function IsPowerArmorRaceData(race As Canon.IRace, armorTypePowerKywdFid As UInteger,
                                                getParsedArmo As Func(Of UInteger, Canon.IArmo)) As Boolean
        If race Is Nothing OrElse armorTypePowerKywdFid = 0UI OrElse race.Skin = 0UI Then Return False
        Return IsPowerArmorArmoData(getParsedArmo(race.Skin), armorTypePowerKywdFid)
    End Function

    ''' <summary>True if the ARMO is power armor — carries the ArmorTypePower keyword. Cached per ARMO.</summary>
    Private Function ArmoIsPowerArmor(armoFID As UInteger) As Boolean
        If armoFID = 0UI Then Return False
        Dim kFid = ArmorTypePowerKeywordFid()
        If kFid = 0UI Then Return False
        ' Draft FormIDs are evaluated FRESH (no PA-boolean cache) so a live keyword edit to the draft is
        ' reflected immediately — same "drafts mutate live, never cache" rule the parse resolver follows.
        If OutfitDraft.IsDraftFormID(armoFID) Then
            Return IsPowerArmorArmoData(_ctx.GetParsedArmo(armoFID), kFid)
        End If
        Return _isPowerArmorArmoCache.GetOrAdd(armoFID,
            Function(fid) IsPowerArmorArmoData(_ctx.GetParsedArmo(fid), kFid))
    End Function

    ''' <summary>True if the race is a power-armor race — its RACE.WNAM (Skin) ARMO is power armor. Covers
    ''' vanilla PowerArmorRace + DLC/mod PA races without a hardcoded race list. Cached per race.</summary>
    Private Function RaceIsPowerArmor(raceFID As UInteger) As Boolean
        If raceFID = 0UI Then Return False
        Return _isPowerArmorRaceCache.GetOrAdd(raceFID,
            Function(fid)
                Dim rRec = _pluginManager.GetRecord(fid)
                If rRec Is Nothing OrElse rRec.Header.Signature <> "RACE" Then Return False
                ' Same shared core the bake uses; the draft-aware nuance of ArmoIsPowerArmor doesn't
                ' apply here (a RACE.WNAM skin is never an in-memory ARMO draft).
                Return IsPowerArmorRaceData(_ctx.ParseRaceCanonCached(rRec), ArmorTypePowerKeywordFid(), AddressOf _ctx.GetParsedArmo)
            End Function)
    End Function








    ' Biped object slot bits moved to the shared module BipedSlots (NPC\BipedSlots.vb) so the
    ' render path, the resolvers and this form all share ONE definition. Reference them as
    ' BipedSlots.SlotBit* / BipedSlots.HEADWEAR_MASK / BipedSlots.SLOT_PIPBOY.







    ''' <summary>True if an ARMA is wearable by <paramref name="raceFid"/> for the FormIdPicker race filter — the
    ''' SAME per-ARMA race match the render/candidate paths use (<see cref="EquipResolver.ArmaMatchesRace"/> with the
    ''' RNAM "Armor Race" redirect + AdditionalRaces, via <see cref="NpcRenderContext.GetEffectiveArmorRaces"/>).
    ''' <paramref name="raceFid"/> = 0 (editor opened without a preview NPC) → True, i.e. no filtering. The ARMA
    ''' is resolved through the draft-aware <see cref="NpcRenderContext.GetParsedArma"/>, so draft ARMAs are
    ''' filtered consistently. A FormID that resolves to no ARMA returns False.</summary>
    Friend Function IsArmaRaceCompatible(armaFid As UInteger, raceFid As UInteger) As Boolean
        If raceFid = 0UI Then Return True
        Dim arma As Canon.IArma
        Try
            arma = _ctx.GetParsedArma(armaFid)
        Catch
            Return False
        End Try
        If arma Is Nothing Then Return False
        Return EquipResolver.ArmaMatchesRace(arma, raceFid, _ctx.GetEffectiveArmorRaces(raceFid))
    End Function

    ''' <summary>Same per-ARMA race rule as <see cref="IsArmaRaceCompatible"/> (RNAM + AdditionalRaces + the
    ''' RACE.RNAM Armor-Race redirect chain, identical in FO4 and Skyrim), but evaluated over LOOSE race fields
    ''' instead of a parsed record — for an ARMA draft whose panels are not committed yet, where the FormID would
    ''' still resolve to the pre-edit values. <paramref name="raceFid"/> = 0 → True (no filtering).</summary>
    Friend Function IsArmaRaceCompatible(armaRaceFormID As UInteger, additionalRaces As IEnumerable(Of UInteger),
                                         raceFid As UInteger) As Boolean
        If raceFid = 0UI Then Return True
        Return EquipResolver.ArmaMatchesRace(armaRaceFormID, additionalRaces, raceFid,
                                             _ctx.GetEffectiveArmorRaces(raceFid))
    End Function

    ''' <summary>True if an ARMO is wearable by <paramref name="raceFid"/> for the FormIdPicker race filter. An
    ''' ARMO qualifies iff at least one of its ArmorAddons ARMAs is race-compatible (<see cref="IsArmaRaceCompatible"/>)
    ''' — the PER-ARMA match, NOT <c>ARMO.RaceFormID</c>: a clothing ARMO commonly carries RNAM=HumanRace but is
    ''' worn by ghouls via a sub-ARMA's AdditionalRaces, so filtering by the ARMO's own race would wrongly hide it.
    ''' This mirrors the per-ARMA OR that <see cref="EquipResolver.BuildFootprint"/> / <see cref="GetArmoItemCandidates"/>
    ''' use to decide an ARMO is valid for a race (without their extra gender-mesh/skin-TXST constraint, which would
    ''' over-filter a generic Template picker). <paramref name="raceFid"/> = 0 → True (no filtering). The ARMO is
    ''' resolved through the draft-aware <see cref="NpcRenderContext.GetParsedArmo"/>. <paramref name="isFemale"/> is
    ''' accepted for parity with the candidate helpers; v1 gates on race only (per-ARMA race match is sufficient).</summary>
    Friend Function IsArmoRaceCompatible(armoFid As UInteger, raceFid As UInteger, isFemale As Boolean) As Boolean
        If raceFid = 0UI Then Return True
        Dim armo As Canon.IArmo
        Try
            armo = _ctx.GetParsedArmo(armoFid)
        Catch
            Return False
        End Try
        If armo Is Nothing Then Return False
        For Each addon In ArmoEditor_Form.ReadAddons(armo)
            If IsArmaRaceCompatible(addon.ArmaFormID, raceFid) Then Return True
        Next
        Return False
    End Function










    Friend Enum SkinRegion
        Body = 0
        Hand = 1
    End Enum











    ' ApplyMaterialSwap lives in the shared ShapeMaterialOverrides module (in FO4_Base_Library)
    ' so it can be reused by the NPC ObjectTemplate / OMOD path. The generic material-resolution
    ' helpers (EnsureShapeMaterialResolved / TryLoadMaterialFromDictionary) now live in the lib's
    ' MaterialResolver module and are called directly from there, e.g.
    ' `ShapeMaterialOverrides.ApplyMaterialSwap(formID, func, shapes, _pluginManager)`.










    Private Sub TextBoxSearch_TextChanged(sender As Object, e As EventArgs) Handles TextBoxSearch.TextChanged
        _pendingTreeFilter = TextBoxSearch.Text
        SearchDebounceTimer.Stop()
        SearchDebounceTimer.Start()
    End Sub

    Private Sub SearchDebounceTimer_Tick(sender As Object, e As EventArgs) Handles SearchDebounceTimer.Tick
        SearchDebounceTimer.Stop()
        PopulateNPCTree(_pendingTreeFilter)
    End Sub

    ' ============================================================================================
    ' Advanced filter (facet terms live IN the search box; a modal dialog edits them)
    ' ============================================================================================

    ''' <summary>The facet resolver, created on first actual use. Deliberately NOT built at load: a
    ''' session that never types a facet token must not pay for it. Cleared by InvalidateAll on a
    ''' load-order change and by InvalidateNpcState after an NPC save.</summary>
    Private Function EnsureFilterIndex() As NpcFilterIndex
        If _filterIndex Is Nothing Then
            _filterIndex = New NpcFilterIndex(_pluginManager,
                                              Function(fid As UInteger) As NPC_Data
                                                  Dim npc As NPC_Data = Nothing
                                                  If _ctx IsNot Nothing AndAlso _ctx.NpcCache IsNot Nothing AndAlso
                                                     _ctx.NpcCache.TryGetValue(fid, npc) Then Return npc
                                                  Return Nothing
                                              End Function)
        End If
        Return _filterIndex
    End Function

    ''' <summary>Open the advanced editor on whatever is in the box, and write back what it composes.
    ''' The dialog holds no state of its own: it parses the box on open and returns a query string, so
    ''' the box stays the single, visible source of truth for the whole filter. Opening it reads
    ''' NOTHING from the load order — it is pure text editing.</summary>
    Private Sub ButtonAdvanced_Click(sender As Object, e As EventArgs) Handles ButtonAdvanced.Click
        Using dlg As New NpcFilterAdvanced_Form()
            dlg.QueryText = TextBoxSearch.Text
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If Not String.Equals(dlg.QueryText, TextBoxSearch.Text, StringComparison.Ordinal) Then
                TextBoxSearch.Text = dlg.QueryText   ' fires TextChanged -> debounce -> repopulate
            End If
        End Using
    End Sub

    ''' <summary>Drop every facet token and the `templates:` mode, KEEPING the free text: clearing the
    ''' advanced part must never wipe what the user typed. A no-op when there is nothing advanced on,
    ''' which is why the button can just stay enabled instead of appearing and disappearing.</summary>
    Private Sub ButtonClearAdvanced_Click(sender As Object, e As EventArgs) Handles ButtonClearAdvanced.Click
        Dim cleared = NpcFilterQuery.Parse(TextBoxSearch.Text).WithoutFacets()
        If Not String.Equals(cleared, TextBoxSearch.Text, StringComparison.Ordinal) Then TextBoxSearch.Text = cleared
    End Sub

    ''' <summary>"Only changed" tree filter toggle: rebuild the tree restricted to dirty NPCs (when
    ''' ticked) or back to the full set, in both cases honoring the current text filter.</summary>
    Private Sub CheckBoxOnlyChanged_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxOnlyChanged.CheckedChanged
        PopulateNPCTree(_pendingTreeFilter)
    End Sub

    ''' <summary>Section 1 category filter toggles (Unique faces / Generic / Template bases / Unused):
    ''' rebuild the tree honoring the current text filter. Additive union — see NpcMatchesCategoryFilter.</summary>
    Private Sub CategoryFilter_CheckedChanged(sender As Object, e As EventArgs) _
            Handles CheckBoxCatUnique.CheckedChanged, CheckBoxCatGeneric.CheckedChanged,
                    CheckBoxCatTemplate.CheckedChanged, CheckBoxCatUnused.CheckedChanged
        PopulateNPCTree(_pendingTreeFilter)
    End Sub

    Private Sub SetStatus(text As String)
        If InvokeRequired Then
            BeginInvoke(Sub() SetStatus(text))
            Return
        End If
        ToolStripStatusLabel1.Text = text
    End Sub

#Region "Record Details Panel"

    ''' <summary>LVLN parses made while building ONE details tree. The panel resolves ten template
    ''' categories independently, and a chain hop through the same leveled list would otherwise re-parse
    ''' it once per category. Scoped to a single PopulateRecordDetails call — cleared on entry — so it
    ''' cannot go stale against a plugin reload.</summary>
    Private ReadOnly _detailsLvlnCache As New Dictionary(Of UInteger, Canon.ILvln)

    ''' <summary>
    ''' Populates the record details TreeView for a selected NPC, showing all fields
    ''' with full inheritance resolution (which template provides each category).
    ''' </summary>
    Private Sub PopulateRecordDetails(npc As NPC_Data)
        If InvokeRequired Then
            Invoke(Sub() PopulateRecordDetails(npc))
            Return
        End If

        TreeViewRecordDetails.SuspendLayout()
        TreeViewRecordDetails.BeginUpdate()
        TreeViewRecordDetails.Nodes.Clear()
        _detailsLvlnCache.Clear()

        If npc Is Nothing Then
            ' ⛔ Y EL TÍTULO TAMBIÉN. Se escribe unas líneas más abajo, DESPUÉS de este guard, así que al
            ' entrar acá quedaba el encabezado del NPC anterior sobre un árbol de detalles VACÍO:
            ' "Fulano [Mod.esp] FormID:0001A2B3" y cero nodos debajo. Se repone el baseline que fija el
            ' Designer, no la cadena vacía: el label es una banda de 24 px con `BackColor = ControlDark`
            ' y dejarla muda es una franja oscura sin explicación.
            LabelRecordTitle.Text = "  Record Details"
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
            Return
        End If

        Try
            LabelRecordTitle.Text = $"  {npc} [{npc.PluginName}] FormID:{npc.FormID:X8}"

            ' The NPC_ record is NOT one schema across the two games. FO4 and Skyrim disagree on the ACBS
            ' layout, on body size (MWGT thin/muscular/fat vs a single NAM7 weight float), on the stats
            ' block (DNAM = 8-byte calculated stats vs 52-byte player skills), on face data (MSDK/TETI/FMRI
            ' vs NAM9/NAMA/TINI) and on template resolution (FO4 caches the resolved actor per category in
            ' TPTA; Skyrim has no TPTA and walks the TPLT chain). Gate on the record's OWN game pin, not the
            ' session's, so a record always renders under the schema it was parsed with.
            Dim isSse As Boolean = (npc.Game = Config_App.Game_Enum.Skyrim)

            ' --- Header ---
            Dim headerNode = AddNode(Nothing, $"NPC_ {npc.EditorID}  [{npc.FormID:X8}]  {npc.PluginName}")
            AddNode(headerNode, $"Full Name: {If(npc.Record.Name <> "", npc.Record.Name, "(none)")}")
            If npc.Record.ShortNamePresente AndAlso npc.Record.ShortName <> "" Then AddNode(headerNode, $"Short Name: {npc.Record.ShortName}")
            AddNode(headerNode, $"Editor ID: {npc.EditorID}")
            AddNode(headerNode, $"Form ID: {npc.FormID:X8}")
            AddNode(headerNode, $"Plugin: {npc.PluginName}")
            AddNode(headerNode, $"Gender: {If(npc.Record.ConfigurationFlagsFemale, "Female", "Male")}")
            headerNode.Expand()

            ' --- Template Info ---
            If npc.Record.Plantilla() <> 0UI OrElse npc.Record.ActoresDePlantilla().Count > 0 Then
                Dim tplNode = AddNode(Nothing, $"Template Configuration  (flags: {npc.Record.ConfigurationTemplateFlags:X4})")
                If npc.Record.Plantilla() <> 0UI Then
                    AddNode(tplNode, $"Base Template (TPLT): {DescribeFormID(npc.Record.Plantilla())}")
                End If
                If Not isSse Then
                    ' TPTA + the legendary template pair are Fallout-only subrecords.
                    For Each cat As NPC_TemplateCategory In Canon.CanonInterpretacion.CategoriasDePlantilla
                        Dim actor = npc.Record.ActorDePlantilla(cat)
                        If actor = 0UI Then Continue For
                        AddNode(tplNode, $"TPTA[{cat}] ({NpcManagerFormat.GetTemplateCategoryLabel(cat)}): {DescribeFormID(actor)}")
                    Next
                    Dim npcFo4 = TryCast(npc.Record, Canon.NpcFO4)
                    If npcFo4 IsNot Nothing Then
                        If npcFo4.LegendaryTemplatePresente Then AddNode(tplNode, $"Legendary Template (LTPT): {DescribeFormID(npcFo4.LegendaryTemplate)}")
                        If npcFo4.LegendaryChancePresente Then AddNode(tplNode, $"Legendary Chance (LTPC): {DescribeFormID(npcFo4.LegendaryChance)}")
                    End If
                End If
                ' The 13 template-flag bits are identical in both engines.
                Dim flagList As New List(Of String)
                For Each cat As NPC_TemplateCategory In Canon.CanonInterpretacion.CategoriasDePlantilla
                    If NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, cat) Then flagList.Add(NpcManagerFormat.GetTemplateCategoryLabel(cat))
                Next
                If flagList.Count > 0 Then AddNode(tplNode, $"Active flags: {String.Join(", ", flagList)}")
                tplNode.Expand()
            End If

            ' --- Configuration (ACBS) ---
            ' The ACBS bytes always live on THIS record (required subrecord) — only the category VALUES
            ' below are resolved through the template chain, so this section is never "inherited".
            Dim cfgNode = AddNode(Nothing, "Configuration (ACBS)")
            AddNode(cfgNode, $"Flags: {NpcManagerFormat.DescribeAcbsFlags(npc.Record.ConfigurationFlags, npc.Game)}")
            AddNode(cfgNode, NpcManagerFormat.FormatAcbsLevel(npc.Record))
            Dim cfgSse = TryCast(npc.Record, Canon.NpcSSE)
            Dim cfgFo4 = TryCast(npc.Record, Canon.NpcFO4)
            If cfgSse IsNot Nothing Then
                AddNode(cfgNode, $"Offsets: Magicka={cfgSse.ConfigurationMagickaOffset}  Stamina={cfgSse.ConfigurationStaminaOffset}  Health={cfgSse.ConfigurationHealthOffset}")
                AddNode(cfgNode, $"Speed Multiplier: {cfgSse.ConfigurationSpeedMultiplier}%")
            ElseIf cfgFo4 IsNot Nothing Then
                AddNode(cfgNode, $"XP Value Offset: {cfgFo4.ConfigurationXPValueOffset}")
                AddNode(cfgNode, $"Disposition Base: {npc.Record.BaseDeDisposicion()}")
            End If
            AddNode(cfgNode, $"Bleedout Override: {npc.Record.ConfigurationBleedoutOverride}")

            ' --- Traits (with inheritance) ---
            Dim traitsNpc = ResolveSectionSource(npc, NPC_TemplateCategory.Traits)
            Dim traitsNode = AddNode(Nothing, SectionLabel(npc, traitsNpc, "Traits"))
            AddNode(traitsNode, $"Race: {DescribeFormID(traitsNpc.Record.Race)}")
            ExpandRaceDetails(traitsNode, traitsNpc.Record.Race, traitsNpc.Record.ConfigurationFlagsFemale)
            If traitsNpc.Record.Skin <> 0UI Then AddNode(traitsNode, $"Skin Armor: {DescribeFormID(traitsNpc.Record.Skin)}")
            If traitsNpc.Record.VoicePresente Then AddNode(traitsNode, $"Voice: {DescribeFormID(traitsNpc.Record.Voice)}")
            If isSse Then
                ' Skyrim body size = NAM6 Height + NAM7 Weight, both single floats. It has neither the MWGT
                ' thin/muscular/fat triple nor MRSV body-morph regions. NAM7 is parsed as an opaque payload
                ' because in FO4 the same signature is an unused field — here it carries the weight.
                If traitsNpc.Record.TieneAltura() Then AddNode(traitsNode, $"Height: {traitsNpc.Record.Altura():F2}")
                If traitsNpc.Record.TienePesoDeSkyrim() Then AddNode(traitsNode, $"Weight: {traitsNpc.Record.PesoDeSkyrim():F2}")
            Else
                If traitsNpc.Record.TieneAltura() OrElse traitsNpc.Record.TieneAlturaMaxima() Then
                    ' Each half is reported only when its subrecord is actually present: NPC_Data.HeightMax
                    ' defaults to 0.0, so printing it unconditionally showed "max=0.00" for a record that
                    ' simply has no NAM4 — indistinguishable from one that really stores zero.
                    Dim hMin = If(traitsNpc.Record.TieneAltura(), $"{traitsNpc.Record.Altura():F2}", "(absent)")
                    Dim hMax = If(traitsNpc.Record.TieneAlturaMaxima(), $"{traitsNpc.Record.AlturaMaxima():F2}", "(absent)")
                    AddNode(traitsNode, $"Height: min={hMin}  max={hMax}")
                End If
                Dim fmtMwgt = Function(v As Single?) If(v.HasValue, v.Value.ToString("F2"), "Default")
                AddNode(traitsNode, $"Weight: Thin={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(0))}  Muscular={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(1))}  Fat={fmtMwgt(traitsNpc.Record.PesoDelCuerpo(2))}")
                Dim regiones = traitsNpc.Record.ValoresDeRegionCorporal()
                If regiones.Count > 0 Then
                    Dim morphNode = AddNode(traitsNode, $"Body Morph Regions ({regiones.Count} values)")
                    For i = 0 To regiones.Count - 1
                        AddNode(morphNode, $"[{i}] = {regiones(i):F4}")
                    Next
                End If
            End If
            traitsNode.Expand()

            ' --- Stats (with inheritance) ---
            Dim statsNpc = ResolveSectionSource(npc, NPC_TemplateCategory.Stats)
            Dim statsNode = AddNode(Nothing, SectionLabel(npc, statsNpc, "Stats"))
            If statsNpc.Record.ClassPresente Then AddNode(statsNode, $"Class: {DescribeFormID(statsNpc.Record.[Class])}")
            If isSse Then
                ' DNAM = 52-byte Player Skills. Nothing when the payload was too short to model.
                Dim skillsSse = TryCast(statsNpc.Record, Canon.NpcSSE)
                If skillsSse IsNot Nothing AndAlso skillsSse.PlayerSkillsHealthPresente Then
                    AddNode(statsNode, $"Health {skillsSse.PlayerSkillsHealth}   Magicka {skillsSse.PlayerSkillsMagicka}   Stamina {skillsSse.PlayerSkillsStamina}")
                    Dim skillsNode = AddNode(statsNode, $"Player Skills ({skillsSse.SkillValues.Count})")
                    For i = 0 To skillsSse.SkillValues.Count - 1
                        Dim off = If(i < skillsSse.SkillOffsets.Count, skillsSse.SkillOffsets(i).Skill, CByte(0))
                        ' El nombre de la skill sale del esquema, que es donde vive el orden del arreglo.
                        Dim nombre = skillsSse.SkillValues(i).Node?.Name
                        AddNode(skillsNode, $"{If(String.IsNullOrEmpty(nombre), $"[{i}]", nombre)}: {skillsSse.SkillValues(i).Skill}  (offset +{off})")
                    Next
                End If
            Else
                ' DNAM = 8-byte Calculated Stats. Note far-away-model distance is a u16 here, a float on SSE.
                Dim calcFo4 = TryCast(statsNpc.Record, Canon.NpcFO4)
                If calcFo4 IsNot Nothing AndAlso calcFo4.CalculatedHealthPresente Then
                    AddNode(statsNode, $"Calculated Health: {calcFo4.CalculatedHealth}")
                    AddNode(statsNode, $"Calculated Action Points: {calcFo4.CalculatedActionPoints}")
                End If
            End If

            ' --- Factions (with inheritance) ---
            Dim facNpc = ResolveSectionSource(npc, NPC_TemplateCategory.Factions)
            Dim facciones = facNpc.Record.Factions
            If facciones.Count > 0 Then
                Dim facNode = AddNode(Nothing, SectionLabel(npc, facNpc, $"Factions ({facciones.Count})"))
                For Each fac In facciones
                    AddNode(facNode, $"{DescribeFormID(fac.Faction)}  rank {fac.FactionRank}")
                Next
            End If

            ' --- AI Data (with inheritance) ---
            Dim aiNpc = ResolveSectionSource(npc, NPC_TemplateCategory.AIData)
            If aiNpc.Record.AIDataAggressionPresente Then
                Dim aiNode = AddNode(Nothing, SectionLabel(npc, aiNpc, "AI Data"))
                AddNode(aiNode, $"Aggression: {aiNpc.Record.AIDataAggressionNombre}")
                AddNode(aiNode, $"Confidence: {aiNpc.Record.AIDataConfidenceNombre}")
                AddNode(aiNode, $"Morality: {aiNpc.Record.AIDataMoralityNombre}")
                AddNode(aiNode, $"Mood: {aiNpc.Record.AIDataMoodNombre}")
                AddNode(aiNode, $"Assistance: {aiNpc.Record.AIDataAssistanceNombre}")
                AddNode(aiNode, $"Energy Level: {aiNpc.Record.AIDataEnergyLevel}")
                AddNode(aiNode, $"Aggro Radius Behavior: {If(aiNpc.Record.AIDataAggroRadiusBehavior, "Yes", "No")}")
                AddNode(aiNode, $"Radius: warn={aiNpc.Record.AggroWarn}  warn/attack={aiNpc.Record.AggroWarnAttack}  attack={aiNpc.Record.AggroAttack}")
            End If

            ' --- AI Packages (with inheritance) ---
            Dim pkgNpc = ResolveSectionSource(npc, NPC_TemplateCategory.AIPackages)
            Dim dpltNpc = ResolveSectionSource(npc, NPC_TemplateCategory.DefaultPackageList)
            If pkgNpc.Record.PaquetesDeIA().Count > 0 OrElse dpltNpc.Record.DefaultPackageListPresente Then
                Dim pkgNode = AddNode(Nothing, SectionLabel(npc, pkgNpc, $"AI Packages ({pkgNpc.Record.PaquetesDeIA().Count})"))
                For Each pkgID In pkgNpc.Record.PaquetesDeIA()
                    AddNode(pkgNode, DescribeFormID(pkgID))
                Next
                If dpltNpc.Record.DefaultPackageListPresente Then AddNode(pkgNode, $"Default Package List (DPLT): {DescribeFormID(dpltNpc.Record.DefaultPackageList)}")
            End If

            ' --- Spell List (with inheritance) ---
            Dim spellNpc = ResolveSectionSource(npc, NPC_TemplateCategory.SpellList)
            If spellNpc.Record.EfectosDeActor().Count > 0 Then
                Dim spellNode = AddNode(Nothing, SectionLabel(npc, spellNpc, $"Actor Effects ({spellNpc.Record.EfectosDeActor().Count})"))
                For Each spellID In spellNpc.Record.EfectosDeActor()
                    AddNode(spellNode, DescribeFormID(spellID))
                Next
            End If

            ' --- Keywords (with inheritance) ---
            Dim kwNpc = ResolveSectionSource(npc, NPC_TemplateCategory.Keywords)
            If kwNpc.Record.PalabrasClave().Count > 0 Then
                Dim kwNode = AddNode(Nothing, SectionLabel(npc, kwNpc, $"Keywords ({kwNpc.Record.PalabrasClave().Count})"))
                For Each kwID In kwNpc.Record.PalabrasClave()
                    AddNode(kwNode, DescribeFormID(kwID))
                Next
            End If

            ' --- Perks (no template category — always the record's own) ---
            Dim ventajas = npc.Record.Perks
            If ventajas.Count > 0 Then
                Dim perkNode = AddNode(Nothing, $"Perks ({ventajas.Count})")
                For Each perk In ventajas
                    AddNode(perkNode, $"{DescribeFormID(perk.Perk)}  rank {perk.PerkRank}")
                Next
            End If

            ' --- Inventory (with inheritance) ---
            Dim invNpc = ResolveSectionSource(npc, NPC_TemplateCategory.Inventory)
            Dim invNode = AddNode(Nothing, SectionLabel(npc, invNpc, "Inventory"))
            If invNpc.Record.DefaultOutfit <> 0UI Then
                Dim outfitNode = AddNode(invNode, $"Default Outfit: {DescribeFormID(invNpc.Record.DefaultOutfit)}")
                ExpandOutfitDetails(outfitNode, invNpc.Record.DefaultOutfit)
            Else
                AddNode(invNode, "Default Outfit: (none)")
            End If
            If invNpc.Record.SleepingOutfit <> 0UI Then
                Dim sleepNode = AddNode(invNode, $"Sleep Outfit: {DescribeFormID(invNpc.Record.SleepingOutfit)}")
                ExpandOutfitDetails(sleepNode, invNpc.Record.SleepingOutfit)
            End If
            ' CNTO items are listed by name only — deliberately NOT expanded into their ARMO/ARMA graph
            ' the way the outfit is, so a 40-item merchant doesn't pay for it on every selection.
            Dim inventario = invNpc.Record.Items
            If inventario.Count > 0 Then
                Dim itemsNode = AddNode(invNode, $"Items ({inventario.Count})")
                For Each item In inventario
                    AddNode(itemsNode, $"{DescribeFormID(item.Item)}  x{item.ItemCount}")
                Next
            End If
            invNode.Expand()

            ' --- Model / Appearance (with inheritance) ---
            Dim modelNpc = ResolveSectionSource(npc, NPC_TemplateCategory.ModelAnimation)
            Dim modelNode = AddNode(Nothing, SectionLabel(npc, modelNpc, "Appearance"))
            If modelNpc.Record.HeadTexture <> 0UI Then AddNode(modelNode, $"Head Texture: {DescribeFormID(modelNpc.Record.HeadTexture)}")
            If modelNpc.Record.HairColor <> 0UI Then AddNode(modelNode, $"Hair Color: {DescribeFormID(modelNpc.Record.HairColor)}")
            ' BCLF (facial hair colour) is a Fallout-only subrecord — Skyrim tints the beard off HCLF.
            If Not isSse AndAlso modelNpc.Record.ColorDeBarba() <> 0UI Then AddNode(modelNode, $"Facial Hair Color: {DescribeFormID(modelNpc.Record.ColorDeBarba())}")
            ' QNAM exists in both engines (float RGB(A)).
            If modelNpc.Record.TextureLightingRedPresente Then AddNode(modelNode, $"Texture Lighting: R={modelNpc.Record.ColorDeIluminacionDeTextura().R} G={modelNpc.Record.ColorDeIluminacionDeTextura().G} B={modelNpc.Record.ColorDeIluminacionDeTextura().B}")

            ' Head Parts (PNAM — both engines)
            If modelNpc.Record.PartesDeCabeza().Count > 0 Then
                Dim hpNode = AddNode(modelNode, $"Head Parts ({modelNpc.Record.PartesDeCabeza().Count})")
                For Each hpFormID In modelNpc.Record.PartesDeCabeza()
                    Dim hpRec = _pluginManager.GetRecord(hpFormID)
                    If hpRec IsNot Nothing Then
                        Dim hdpt = _ctx.ParseHdptCached(hpRec)
                        Dim typeName = NpcManagerFormat.GetHeadPartTypeName(hdpt.TipoDeParte())
                        Dim hpChildNode = AddNode(hpNode, $"[{typeName}] {hdpt.EditorID}  [{hpFormID:X8}]")
                        If hdpt.ModelFileName <> "" Then AddNode(hpChildNode,
                                                                 $"Mesh: {hdpt.ModelFileName}")
                        If hdpt.TextureSet <> 0UI Then AddNode(hpChildNode, $"TextureSet: {DescribeFormID(hdpt.TextureSet)}")
                        If hdpt.Color <> 0UI Then AddNode(hpChildNode, $"Color: {DescribeFormID(hdpt.Color)}")
                        If hdpt.PartesExtra().Count > 0 Then
                            For Each epId In hdpt.PartesExtra()
                                AddNode(hpChildNode, $"Extra Part: {DescribeFormID(epId)}")
                            Next
                        End If
                    Else
                        AddNode(hpNode, $"HDPT [{hpFormID:X8}] (record not found)")
                    End If
                Next
                hpNode.Expand()
            End If

            If isSse Then
                AddSseFaceMorphNodes(modelNode, modelNpc.Record.DeslizadoresDeCara())
                AddSseFacePartNodes(modelNode, modelNpc.Record.PartesDeCara())
                AddSseTintLayerNodes(modelNode, TryCast(modelNpc.Record, Canon.NpcSSE))
            Else
                ' Face Morph Presets (MSDK/MSDV)
                If modelNpc.Record.MorfosDeCara().Count > 0 Then
                    Dim morphNode = AddNode(modelNode, $"Face Morph Presets ({modelNpc.Record.MorfosDeCara().Count})")
                    For Each kvp In modelNpc.Record.MorfosDeCara()
                        AddNode(morphNode, $"Key {kvp.Key:X8} = {kvp.Value:F4}")
                    Next
                End If

                ' Face Morph Sculpting (FMRI/FMRS)
                Dim modelFo4 = TryCast(modelNpc.Record, Canon.NpcFO4)
                If modelFo4 IsNot Nothing AndAlso modelFo4.FaceMorphs.Count > 0 Then
                    Dim fmNode = AddNode(modelNode, $"Face Morph Sculpting ({modelFo4.FaceMorphs.Count} morphs)")
                    For Each fm In modelFo4.FaceMorphs
                        AddNode(fmNode, $"Morph {fm.FaceMorphIndex:X8}: posicion, rotacion y escala")
                    Next
                End If
                If modelNpc.Record.TieneIntensidadDeMorfoFacial() Then AddNode(modelNode, $"Facial Morph Intensity (FMIN): {modelNpc.Record.IntensidadDeMorfoFacial():F2}")

                ' Face Tint Layers (TETI/TEND)
                Dim capasDeTinte = FaceTintInputBuilder.CapasAutoradasDelRecord(modelNpc.Record)
                If capasDeTinte.Count > 0 Then
                    Dim tintNode = AddNode(modelNode, $"Face Tint Layers ({capasDeTinte.Count})")
                    For Each tl In capasDeTinte
                        Dim colorStr = If(tl.Color <> Color.Empty, $" Color:({tl.Color.R},{tl.Color.G},{tl.Color.B},{tl.Color.A})", "")
                        AddNode(tintNode, $"Discr:{tl.Discriminator} Index:{tl.Index} Value:{tl.Value}{colorStr}")
                    Next
                End If
            End If
            modelNode.Expand()

            ' --- Other ---
            Dim otherNode = AddNode(Nothing, "Other")
            If npc.Record.DeathItemPresente Then AddNode(otherNode, $"Death Item (INAM): {DescribeFormID(npc.Record.DeathItem)}")
            If npc.Record.CombatStylePresente Then AddNode(otherNode, $"Combat Style (ZNAM): {DescribeFormID(npc.Record.CombatStyle)}")
            If npc.Record.CrimeFactionPresente Then AddNode(otherNode, $"Crime Faction (CRIF): {DescribeFormID(npc.Record.CrimeFaction)}")
            If npc.Record.GiftFilterPresente Then AddNode(otherNode, $"Gift Filter (GNAM): {DescribeFormID(npc.Record.GiftFilter)}")
            If npc.Record.FarAwayModelPresente Then AddNode(otherNode, $"Far Away Model (ANAM): {DescribeFormID(npc.Record.FarAwayModel)}")
            If npc.Record.AttackRacePresente Then AddNode(otherNode, $"Attack Race (ATKR): {DescribeFormID(npc.Record.AttackRace)}")
            If npc.Record.SoundLevelPresente Then AddNode(otherNode, $"Sound Level (NAM8): {NpcManagerFormat.SoundLevelName(npc.Record.SoundLevel, npc.Game)}")
            If npc.Record.InheritsSoundsFromPresente Then AddNode(otherNode, $"Inherits Sounds From (CSCR): {DescribeFormID(npc.Record.InheritsSoundsFrom)}")
            If Not isSse Then
                ' PFRN (power-armor stand) and NTRM (native terminal) have no Skyrim counterpart.
                Dim otherFo4 = TryCast(npc.Record, Canon.NpcFO4)
                If otherFo4 IsNot Nothing Then
                    If otherFo4.PowerArmorStandPresente Then AddNode(otherNode, $"Power Armor Stand (PFRN): {DescribeFormID(otherFo4.PowerArmorStand)}")
                    If otherFo4.NativeTerminalPresente Then AddNode(otherNode, $"Native Terminal (NTRM): {DescribeFormID(otherFo4.NativeTerminal)}")
                End If
            End If
            If otherNode.Nodes.Count = 0 Then otherNode.Remove()

        Finally
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
        End Try
    End Sub

    ''' <summary>The NPC that actually provides a template category — the terminal of the chain, or the
    ''' record itself when the category is not inherited. Never Nothing (falls back to <paramref name="npc"/>).</summary>
    Private Function ResolveSectionSource(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        Return If(ResolveInheritedSourceNpc(npc, category), npc)
    End Function

    ''' <summary>Section header text, tagged "(own)" or "(inherited from X)".</summary>
    Private Shared Function SectionLabel(npc As NPC_Data, source As NPC_Data, title As String) As String
        If source IsNot Nothing AndAlso source.FormID <> npc.FormID Then
            Return $"{title}  (inherited from {NpcManagerFormat.DescribeNpc(source)} [{source.FormID:X8}])"
        End If
        Return $"{title}  (own)"
    End Function

    ''' <summary>Reads one f32 out of a verbatim-preserved subrecord payload. Nothing when the subrecord
    ''' was absent or too short to hold one, so a malformed record degrades to a missing row, not a crash.</summary>
    Private Shared Function ReadSingleAt(raw As Byte(), offset As Integer) As Single?
        If raw Is Nothing OrElse raw.Length < offset + 4 Then Return Nothing
        Return BitConverter.ToSingle(raw, offset)
    End Function

    ''' <summary>NAM9 (SSE) — 19 chargen face sliders, kept verbatim by the parser. Slider i is the f32
    ''' at +4i; that order IS the byte layout, so the names come from the schema, not from presentation.</summary>
    Private Sub AddSseFaceMorphNodes(parentNode As TreeNode, nam9 As Single())
        If nam9 Is Nothing OrElse nam9.Length = 0 Then Return
        Dim count = Math.Min(NpcManagerFormat.SseFaceMorphSliderNames.Length, nam9.Length)
        Dim node = AddNode(parentNode, $"Face Morph (NAM9, {count} sliders)")
        For i = 0 To count - 1
            AddNode(node, $"{NpcManagerFormat.SseFaceMorphSliderNames(i)}: {nam9(i):F3}")
        Next
    End Sub

    ''' <summary>NAMA (SSE) — 4×u32 face parts (Nose / Unknown / Eyes / Mouth).</summary>
    Private Sub AddSseFacePartNodes(parentNode As TreeNode, nama As UInteger())
        If nama Is Nothing OrElse nama.Length < 4 Then Return
        Dim node = AddNode(parentNode, "Face Parts (NAMA)")
        For i = 0 To 3
            AddNode(node, $"{NpcManagerFormat.SseFacePartNames(i)}: {nama(i)}")
        Next
    End Sub

    ''' <summary>TINI/TINC/TINV/TIAS (SSE): las capas de tinte de cara, el equivalente de Skyrim a los
    ''' pares TETI/TEND de Fallout. Cada campo se muestra sólo si el record lo declara.</summary>
    Private Sub AddSseTintLayerNodes(parentNode As TreeNode, npcSse As Canon.NpcSSE)
        If npcSse Is Nothing OrElse npcSse.TintLayers.Count = 0 Then Return
        Dim tintNode = AddNode(parentNode, "Face Tint Layers")
        Dim layerCount = 0
        For Each tl In npcSse.TintLayers
            If Not tl.LayerTintIndexPresente Then Continue For
            layerCount += 1
            Dim layerNode = AddNode(tintNode, $"Layer index {tl.LayerTintIndex}")
            If tl.TintColorAlphaPresente Then
                AddNode(layerNode, $"Color: R={tl.TintColorRed} G={tl.TintColorGreen} B={tl.TintColorBlue} A={tl.TintColorAlpha}")
            End If
            If tl.LayerInterpolationValuePresente Then
                AddNode(layerNode, $"Interpolation: {tl.LayerInterpolationValue / 100.0F:F2}")
            End If
            If tl.LayerPresetPresente Then
                AddNode(layerNode, If(tl.LayerPreset < 0, "Preset: custom (-1)", $"Preset: {tl.LayerPreset}"))
            End If
        Next
        tintNode.Text = $"Face Tint Layers ({layerCount})"
    End Sub

    ''' <summary>Follow template chain for a category and return the terminal NPC that provides the value.</summary>
    Private Function ResolveInheritedSourceNpc(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, category) Then Return npc

        Dim visited As New HashSet(Of UInteger)
        Dim current = npc

        While current IsNot Nothing
            If visited.Contains(current.FormID) Then Exit While
            visited.Add(current.FormID)

            If Not NpcTemplateHelpers.HasTemplateFlag(current.Record.ConfigurationTemplateFlags, category) Then Return current

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(current, category)
            If sourceFormID = 0UI Then Return current

            ' If source is a leveled NPC, try to get the first entry
            Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
            If sourceRec Is Nothing Then Return current

            If sourceRec.Header.Signature = "NPC_" Then
                current = _ctx.GetParsedNpc(sourceFormID)
                If current Is Nothing Then Return npc
            ElseIf sourceRec.Header.Signature = "LVLN" Then
                ' Leveled NPC - get first NPC_ entry
                Dim lvln As Canon.ILvln = Nothing
                If Not _detailsLvlnCache.TryGetValue(sourceFormID, lvln) Then
                    lvln = NpcTemplateHelpers.TryAbrirLvlnTolerante(sourceRec, _pluginManager)
                    ' El fallo también se cachea: `_detailsLvlnCache` se vacía en CADA repoblado del árbol
                    ' de detalle (ver PopulateRecordDetails), o sea en cada selección de NPC, así que no
                    ' puede quedar pegado.
                    _detailsLvlnCache(sourceFormID) = lvln
                End If
                ' TryAbrirLvlnTolerante devuelve Nothing en el LVLN malformado que existe para
                ' tolerar. Sin este guard el .LeveledListEntries de abajo tira NRE dentro de un handler
                ' Handles (TreeViewNPCs_FilaEnfocada -> PopulateRecordDetails, que tiene Finally pero NO
                ' Catch). Mismo patron que los otros call sites de TryAbrirLvlnTolerante en este archivo
                ' y en NpcTemplateHelpers/NpcStateResolver.
                If lvln Is Nothing Then Return current
                Dim firstNpcId = lvln.LeveledListEntries.Select(Function(e) e.LeveledListEntryNPC).
                    FirstOrDefault(Function(fid)
                                       Dim r = _pluginManager.GetRecord(fid)
                                       Return r IsNot Nothing AndAlso r.Header.Signature = "NPC_"
                                   End Function)
                If firstNpcId = 0UI Then Return current
                current = _ctx.GetParsedNpc(firstNpcId)
                If current Is Nothing Then Return npc
            Else
                Return current
            End If
        End While

        Return npc
    End Function

    Private Sub ExpandRaceDetails(parentNode As TreeNode, raceFormID As UInteger, isFemale As Boolean)
        If raceFormID = 0UI Then Return
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return

        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        Dim raceNodeName = If(race.NamePresente, race.Name, "")
        Dim raceNode = AddNode(parentNode, $"Race: {raceNodeName} [{race.EditorID}]")
        ' Default Face Texture: DFTM/DFTF, declarado por juego con su propia colección — TryCast al que
        ' corresponda (nsse.MaleHeadDataDefaultFaceTextureMale/FemaleHeadDataDefaultFaceTextureFemale en
        ' Skyrim, nf.MaleDefaultFaceTexture/FemaleDefaultFaceTexture en Fallout 4).
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim raceSse = TryCast(race, Canon.RaceSSE)
        If isFemale Then
            If race.FemaleSkeletalModelPresente Then AddNode(raceNode, $"Skeleton: {race.FemaleSkeletalModel}")
            ' El filtro por ".nif" es del consumidor viejo (sólo mallas): se replica acá para no listar,
            ' p.ej., un ".egt" de morph de cuerpo como si fuera malla del cuerpo.
            Dim femaleMeshes = race.Parts2.Select(Function(p) p.PartModelFileName).
                Where(Function(m) m.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).ToList()
            For Each mesh In femaleMeshes
                AddNode(raceNode, $"Body Mesh: {mesh}")
            Next
            Dim femaleFaceTex As UInteger = If(raceFo4 IsNot Nothing, raceFo4.FemaleDefaultFaceTexture,
                                               If(raceSse IsNot Nothing, raceSse.FemaleHeadDataDefaultFaceTextureFemale, 0UI))
            If femaleFaceTex <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(femaleFaceTex)}")
        Else
            If race.MaleSkeletalModelPresente Then AddNode(raceNode, $"Skeleton: {race.MaleSkeletalModel}")
            Dim maleMeshes = race.Parts.Select(Function(p) p.PartModelFileName).
                Where(Function(m) m.EndsWith(".nif", StringComparison.OrdinalIgnoreCase)).ToList()
            For Each mesh In maleMeshes
                AddNode(raceNode, $"Body Mesh: {mesh}")
            Next
            Dim maleFaceTex As UInteger = If(raceFo4 IsNot Nothing, raceFo4.MaleDefaultFaceTexture,
                                             If(raceSse IsNot Nothing, raceSse.MaleHeadDataDefaultFaceTextureMale, 0UI))
            If maleFaceTex <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(maleFaceTex)}")
        End If
        If race.Skin <> 0UI Then AddNode(raceNode, $"Race Skin: {DescribeFormID(race.Skin)}")
    End Sub

    Private Sub ExpandOutfitDetails(parentNode As TreeNode, outfitFormID As UInteger)
        If outfitFormID = 0UI Then Return
        Dim outfitRec = _pluginManager.GetRecord(outfitFormID)
        If outfitRec Is Nothing Then Return

        If outfitRec.Header.Signature = "OTFT" Then
            Dim otft = Canon.CanonRecords.Otft(outfitRec, _pluginManager)
            For Each itemFormID In otft.Prendas()
                ExpandOutfitItem(parentNode, itemFormID)
            Next
        End If
    End Sub

    Private Sub ExpandOutfitItem(parentNode As TreeNode, itemFormID As UInteger)
        If itemFormID = 0UI Then Return
        Dim itemRec = _pluginManager.GetRecord(itemFormID)
        If itemRec Is Nothing Then
            AddNode(parentNode, $"[{itemFormID:X8}] (missing record)")
            Return
        End If

        Select Case itemRec.Header.Signature
            Case "ARMO"
                Dim armo = _ctx.GetParsedArmo(itemFormID)
                Dim slotStr = NpcManagerFormat.FormatSlotMask(armo.SlotMaskDe())
                Dim armoNode = AddNode(parentNode, $"ARMO {armo.EditorID}  ""{armo.Name}""  [{armo.FormID:X8}]  Slots:{slotStr}")

                ' Follow template armor
                If armo.TemplateArmor <> 0UI Then
                    AddNode(armoNode, $"Template Armor: {DescribeFormID(armo.TemplateArmor)}")
                End If

                ' Armor Addons
                For Each addon In ArmoEditor_Form.ReadAddons(armo)
                    Dim aaFormID = addon.ArmaFormID
                    Dim aaRec = _pluginManager.GetRecord(aaFormID)
                    If aaRec Is Nothing OrElse aaRec.Header.Signature <> "ARMA" Then
                        AddNode(armoNode, $"ARMA [{aaFormID:X8}] (missing)")
                        Continue For
                    End If
                    Dim arma = _ctx.GetParsedArma(aaFormID)
                    Dim armaFo4 = TryCast(arma, Canon.ArmaFO4)
                    Dim aaNode = AddNode(armoNode, $"ARMA {arma.EditorID}  [{arma.FormID:X8}]  Slots:{NpcManagerFormat.FormatSlotMask(arma.SlotMaskDe())}")
                    If arma.MaleModelFilename <> "" Then AddNode(aaNode, $"Male Mesh: {arma.MaleModelFilename}")
                    If arma.FemaleModelFilename <> "" Then AddNode(aaNode, $"Female Mesh: {arma.FemaleModelFilename}")
                    If arma.MaleModelFilename2 <> "" Then AddNode(aaNode, $"Male 1P Mesh: {arma.MaleModelFilename2}")
                    If arma.FemaleModelFilename2 <> "" Then AddNode(aaNode, $"Female 1P Mesh: {arma.FemaleModelFilename2}")
                    If arma.MaleSkinTexture <> 0UI Then AddNode(aaNode, $"Male Skin Texture: {DescribeFormID(arma.MaleSkinTexture)}")
                    If arma.FemaleSkinTexture <> 0UI Then AddNode(aaNode, $"Female Skin Texture: {DescribeFormID(arma.FemaleSkinTexture)}")
                    ' MO2S/MO3S (material swap) sólo existen en Fallout 4.
                    If armaFo4 IsNot Nothing AndAlso armaFo4.MaleMaterialSwap <> 0UI Then
                        AddNode(aaNode, $"Male Material Swap: {DescribeFormID(armaFo4.MaleMaterialSwap)}")
                    End If
                    If armaFo4 IsNot Nothing AndAlso armaFo4.FemaleMaterialSwap <> 0UI Then
                        AddNode(aaNode, $"Female Material Swap: {DescribeFormID(armaFo4.FemaleMaterialSwap)}")
                    End If
                    If arma.AdditionalRaces.Count > 0 Then
                        For Each raceId In arma.AdditionalRaces
                            AddNode(aaNode, $"Additional Race: {DescribeFormID(raceId.Race)}")
                        Next
                    End If
                Next

            Case "LVLI"
                Dim lvli = Canon.CanonRecords.Lvli(itemRec, _pluginManager)
                If lvli Is Nothing Then
                    AddNode(parentNode, $"LVLI [{itemFormID:X8}] (no parsea)")
                    Return
                End If
                Dim lvliNode = AddNode(parentNode, $"LVLI {lvli.EditorID}  [{lvli.FormID:X8}]  ({lvli.LeveledListEntries.Count} entries)")
                For Each entry In lvli.LeveledListEntries
                    ExpandOutfitItem(lvliNode, entry.LeveledListEntryItem)
                Next

            Case Else
                AddNode(parentNode, $"{itemRec.Header.Signature} {itemRec.EditorID}  [{itemFormID:X8}]")
        End Select
    End Sub

    Private Function AddNode(parent As TreeNode, text As String) As TreeNode
        Dim node As New TreeNode(text)
        If parent Is Nothing Then
            TreeViewRecordDetails.Nodes.Add(node)
        Else
            parent.Nodes.Add(node)
        End If
        Return node
    End Function

    Private Function DescribeFormID(formID As UInteger) As String
        If formID = 0UI Then Return "(none)"
        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return $"[{formID:X8}]"
        Dim edid = If(rec.EditorID <> "", rec.EditorID, rec.Header.Signature)
        Dim pluginSuffix = If(String.IsNullOrWhiteSpace(rec.SourcePluginName), "", $" @{rec.SourcePluginName}")
        Return $"{edid}  [{formID:X8}]{pluginSuffix}"
    End Function

#End Region

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' Persist UI-level config BEFORE teardown. Los rigs de luces (uno por juego) viven en el
        ' Config_App compartido (los escribe en memoria LightRigForm); RenderGore es NPC-only y vive
        ' en NPC_Config.
        NPC_Config.Current.RenderGore = CheckBoxRenderGore.Checked
        NPC_Config.Current.ShowCatUnique = CheckBoxCatUnique.Checked
        NPC_Config.Current.ShowCatGeneric = CheckBoxCatGeneric.Checked
        NPC_Config.Current.ShowCatTemplate = CheckBoxCatTemplate.Checked
        NPC_Config.Current.ShowCatUnused = CheckBoxCatUnused.Checked
        CaptureMainWindowBounds()
        Config_App.SaveConfig()
        NPC_Config.SaveConfig()

        ' Quiesce the render loop FIRST so the safety-repaint heartbeat cannot drain
        ' a paint while the host disposes its GL caches (TintGpuCache, PristineDiffusePixels).
        ' Same rationale as EditFace_Form / EditBody_Form.
        If _previewControl IsNot Nothing AndAlso Not _previewControl.IsDisposed Then
            Try
                _previewControl.BeginTeardown()
            Catch
            End Try
        End If

        If _renderHost IsNot Nothing Then
            Try
                _renderHost.Dispose()
            Catch
            End Try
            _renderHost = Nothing
        End If

        If _previewControl IsNot Nothing AndAlso Not _previewControl.IsDisposed Then
            Try
                _previewControl.Clean()
            Catch
            End Try
            Try
                _previewControl.Dispose()
            Catch
            End Try
        End If

        If _dirtyNodeFont IsNot Nothing Then
            _dirtyNodeFont.Dispose()
            _dirtyNodeFont = Nothing
        End If

        If _deleteNodeFont IsNot Nothing Then
            _deleteNodeFont.Dispose()
            _deleteNodeFont = Nothing
        End If

        Try
            _selectionDebounceTimer.Stop()
            _selectionDebounceTimer.Dispose()
        Catch
        End Try

    End Sub

    Private Sub PanelNpcList_Paint(sender As Object, e As PaintEventArgs) Handles PanelNpcList.Paint

    End Sub

#Region "Editor actions — Load LooksMenu"

    ''' <summary>Per-NPC overlay applied on top of the resolver chain. Keyed by the FormID of the
    ''' NPC the user selected (NOT the model/traits template source — we do NOT mutate templates,
    ''' so other NPCs that share the same template stay untouched). The resolver consults this
    ''' dict at the points where it would otherwise read from the NPC_Data and prefers preset
    ''' values when present.</summary>
    Private ReadOnly _appliedPresets As New Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)

    ''' <summary>NPC-record scalar/list overrides authored in the NPC Editor (Name/ACBS/identity FormIDs/
    ''' keywords/factions/inventory/OBTS), keyed by NPC global FormID. Mirror of <see cref="_appliedPresets"/>
    ''' for the record fields the LooksMenu overlay does NOT carry. Consulted at Save time via the
    ''' <see cref="NpcOverrideSaver.SaveContext.ApplyNpcRecordOverride"/> delegate (applied AFTER the round-trip
    ''' copy so the edit wins). Cleared per-NPC after a successful Save (the values are now in the saved plugin).</summary>
    Private ReadOnly _npcRecordOverrides As New Dictionary(Of UInteger, NpcRecordOverride)

    ''' <summary>NPCs the user has changed this session — drives the bold rendering in
    ''' <see cref="TreeViewNPCs_PintarFila"/>. Set on each editor commit (Load LM / Edit Face /
    ''' Edit Body / Edit Outfit / Paste) and on the context-menu "Mark as changed"; cleared on a
    ''' successful Save of that NPC and on context-menu "Reset". Deliberately decoupled from
    ''' <see cref="_appliedPresets"/> emptiness: after Save the overlay keeps non-ESP fields
    ''' (BodyMorphs/Skin) but the NPC must stop being bold, and "Mark as changed" can flag an NPC
    ''' with no overlay at all. Sidecar hydration (<see cref="HydrateAppliedPresetsFromSidecars"/>)
    ''' does NOT mark dirty — those BodyMorphs were already persisted in a prior Save.</summary>
    Private ReadOnly _dirtyNpcs As New HashSet(Of UInteger)

    ''' <summary>Bold font for dirty NPC nodes, lazily derived from the tree's font on first dirty
    ''' paint and reused (one allocation, not per-paint). Disposed in <see cref="MainForm_FormClosing"/>.</summary>
    Private _dirtyNodeFont As Font
    ''' <summary>Strikeout font for NPC nodes marked-to-delete (in <see cref="_recordsToRemove"/>), lazily
    ''' derived once from the tree's font. Disposed in <see cref="MainForm_FormClosing"/>.</summary>
    Private _deleteNodeFont As Font

    ''' <summary>FormID of the NPC node the tree context menu was opened on. Set in
    ''' <see cref="TreeViewNPCs_FilaClickeada"/>, consumed by the Mark/Reset menu handlers.</summary>
    Private _contextMenuNpcFormID As UInteger

    ''' <summary>Flag an NPC as having unsaved changes (bold in the tree). Idempotent.</summary>
    Private Sub MarkNpcDirty(npcFormID As UInteger)
        If npcFormID = 0UI Then Return
        If _dirtyNpcs.Add(npcFormID) Then RefreshTreeAfterDirtyChange()
    End Sub

    ''' <summary>Clear an NPC's dirty flag (no longer bold). Idempotent.</summary>
    Private Sub ClearNpcDirty(npcFormID As UInteger)
        If _dirtyNpcs.Remove(npcFormID) Then RefreshTreeAfterDirtyChange()
    End Sub

    ''' <summary>Reflect a dirty-set change in the tree. When "Only changed" is active the dirty set
    ''' defines tree membership, so the tree is rebuilt; otherwise a cheap repaint updates the bold
    ''' styling without disturbing the node structure or selection.</summary>
    Private Sub RefreshTreeAfterDirtyChange()
        If CheckBoxOnlyChanged IsNot Nothing AndAlso CheckBoxOnlyChanged.Checked Then
            PopulateNPCTree(_pendingTreeFilter)
        Else
            TreeViewNPCs.Invalidate()
        End If
    End Sub

    ''' <summary>Open the LooksMenu preset picker for the currently selected NPC. On OK records
    ''' the preset as a per-NPC overlay and re-renders. The underlying NPC_Data records are NOT
    ''' mutated — see <see cref="_appliedPresets"/>.</summary>
    Private Async Sub ButtonLoadLooksmenu_Click(sender As Object, e As EventArgs) Handles ButtonLoadLooksmenu.Click
        ' LooksMenu presets are the FO4 f4ee format (F4SE\Plugins\F4EE). SSE uses RaceMenu (.jslot): map the
        ' loaded .jslot onto a per-NPC overlay preset (RaceMenuPresetMapper.ApplyJslotToPreset) and re-render
        ' through the SAME overlay funnel the FO4 path uses (_appliedPresets + LoadNPCOnDemandAsyncFromExisting).
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            Await LoadRaceMenuPresetForSseAsync()
            Return
        End If
        If _renderHost.CurrentBaseState Is Nothing Then Return

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            MessageBox.Show("Could not find NPC record in cache.", "Load LooksMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Resolve race for both the header label AND the optional race-compatibility filter.
        ' The filter checks each preset's HeadPartFormIDs against HDPT.RNAM/FLST + RACE defaults
        ' and each FaceTintLayer.Index against RACE.Male/FemaleTintTemplateGroups, hiding presets
        ' that would partially-apply to this NPC.
        Dim raceFormID As UInteger = _renderHost.CurrentBaseState.RaceFormID
        Dim raceDisplay As String = $"0x{raceFormID:X8}"
        Dim race As Canon.IRace = Nothing
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec IsNot Nothing Then
            race = _ctx.ParseRaceCanonCached(raceRec)
            If race IsNot Nothing AndAlso Not String.IsNullOrEmpty(race.EditorID) Then
                raceDisplay = race.EditorID
            End If
        End If
        Dim gender As Byte = If(_renderHost.CurrentBaseState.IsFemale, CByte(1), CByte(0))
        Dim raceDefaultsForLm As IEnumerable(Of UInteger) =
            race.HeadPartsDe(_renderHost.CurrentBaseState.IsFemale)

        ' Snapshot the overlay state *before* the dialog opens so we can roll back on Cancel.
        ' The dialog drives a live preview via PreviewRequested on every selection change; if the
        ' user picks Cancel we must restore whatever was applied (or unapplied) prior to opening.
        Dim hadPriorOverlay As Boolean = _appliedPresets.TryGetValue(npcFormID, Nothing)
        Dim priorOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
        _appliedPresets.TryGetValue(npcFormID, priorOverlay)

        ' Determine whether the NPC's body NIF has BODYTRI extra-data on its root, so the dialog
        ' can default the "Apply BodySlide sliders" checkbox sensibly. If no shape carries
        ' BODYTRI, the engine wouldn't apply BodyMorphs in-game either — so default unchecked.
        ' User can override.
        Dim npcHasBodyTri = NpcHasAnyBodyTri()

        Dim selected As LooksmenuLoader.LooksmenuPreset = Nothing
        Dim dialogResult As DialogResult
        ' The two F4SE catalogs feed the dialog's "Show incompatible" audit: an overlay/skin-template id the
        ' preset names but no installed mod registers applies NOTHING in-game (engine parity: GetTemplateByName
        ' → null → skipped). Passing the ids lets the report say "not installed" instead of "not checked".
        Using dlg As New LooksmenuLoad_Form(_pluginManager, _dataPath, gender, raceDisplay, npcHasBodyTri,
                                            raceFormID, race, raceDefaultsForLm,
                                            knownOverlayTemplateIds:=GetOverlayTemplateCandidates(gender = 1).Select(Function(t) t.Id),
                                            knownLmSkinTemplateIds:=GetLmSkinTemplateCandidates(gender = 1).Select(Function(t) t.Id))
            ' priorOverlay is the preserve BASELINE for the unticked categories: the live preview keeps
            ' rewriting _appliedPresets as the user clicks around, so reading the current overlay would
            ' preserve the previously PREVIEWED preset instead of the NPC's own look.
            AddHandler dlg.PreviewRequested, Sub(s, args) PreviewLooksmenuOverlay(npcFormID, npc, args.Preset, args.Options, priorOverlay)
            dialogResult = dlg.ShowDialog(Me)
            selected = dlg.SelectedPreset
        End Using

        If dialogResult <> DialogResult.OK Then
            ' Cancel / [X] / Esc → restore pre-dialog overlay state and re-render.
            If hadPriorOverlay Then
                _appliedPresets(npcFormID) = priorOverlay
            Else
                _appliedPresets.Remove(npcFormID)
            End If
            Try
                Dim restoreVersion = Interlocked.Increment(_previewRequestVersion)
                Await LoadNPCOnDemandAsyncFromExisting(npc, restoreVersion)
            Catch ex As Exception
                MessageBox.Show($"Failed to restore preview after cancel: {ex.Message}",
                                "Load LooksMenu", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
            Return
        End If

        If selected Is Nothing Then Return

        ' OK path: the live preview already left `selected` applied in _appliedPresets and rendered.
        ' Nothing to re-apply or re-render — just mark the NPC changed (bold) and log the commit.
        MarkNpcDirty(npcFormID)

        ' ⛔ ACÁ VIVÍAN DOS BUCLES CON EL CUERPO VACÍO: uno recorría HeadPartFormIDs resolviendo el HDPT y
        ' calculando un `typeLabel` que no usaba, y el otro recorría UnresolvedHeadParts sin hacer nada.
        ' Eran un log al que alguien le sacó el contenido y le dejó el esqueleto. Además de costar un
        ' GetRecord + ParseHdptCached por parte en cada carga de preset, hacían PARECER que existía un
        ' camino que trataba lo no resuelto — me mandó a buscarlo a mí. Lo que un lector necesita saber
        ' está en la ListView del editor (ver EditFace_Form.BuildUnresolvedHeadPartRow) y en el auditor
        ' de compatibilidad (PresetCompatibilityReport.AuditMissingMasters), que sí lo muestran.
    End Sub

    ''' <summary>SSE counterpart of <see cref="ButtonLoadLooksmenu_Click"/>: pick a RaceMenu <c>.jslot</c>,
    ''' map it onto the current NPC's overlay preset via <see cref="RaceMenuPresetMapper.ApplyJslotToPreset"/>,
    ''' and re-render through the same overlay funnel the FO4 path uses (_appliedPresets +
    ''' LoadNPCOnDemandAsyncFromExisting). The applied .jslot is mapped onto a CLONE of any pre-existing
    ''' overlay so we can restore the prior state on failure.</summary>
    Private Async Function LoadRaceMenuPresetForSseAsync() As Task
        If _renderHost.CurrentBaseState Is Nothing Then Return

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            MessageBox.Show("Could not find NPC record in cache.", "Load RaceMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Resolve race for the header AND the race-compatibility filter (same as the FO4 Load path). RaceMenu is
        ' race-aware too: HeadPartResolver.IsPresetCompatibleWithRace validates the preset's headParts against the
        ' NPC's RACE the same way. (SSE armor uses a flat per-race ARMA list; that governs skin/armor RESOLUTION,
        ' not preset compatibility — the headPart/RACE check here is the right race gate for the preset browser.)
        Dim raceFormID As UInteger = _renderHost.CurrentBaseState.RaceFormID
        Dim raceDisplay As String = $"0x{raceFormID:X8}"
        Dim race As Canon.IRace = Nothing
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec IsNot Nothing Then
            race = _ctx.ParseRaceCanonCached(raceRec)
            If race IsNot Nothing AndAlso Not String.IsNullOrEmpty(race.EditorID) Then raceDisplay = race.EditorID
        End If
        Dim gender As Byte = If(_renderHost.CurrentBaseState.IsFemale, CByte(1), CByte(0))
        Dim raceDefaultsForLm As IEnumerable(Of UInteger) =
            race.HeadPartsDe(_renderHost.CurrentBaseState.IsFemale)

        ' RaceMenu preset directory (skee64 PapyrusCharGen.cpp): Data\SKSE\Plugins\CharGen\Presets. Same
        ' _dataPath Data-root the FO4 Save path composes its F4SE\Plugins\F4EE\Presets path from.
        Dim presetsDir = IO.Path.Combine(_dataPath, "SKSE", "Plugins", "CharGen", "Presets")

        ' Snapshot the overlay state *before* the dialog opens so we can roll back on Cancel. The browser drives a
        ' live preview via PreviewRequested on every selection change (same funnel as FO4); Cancel restores this.
        Dim hadPriorOverlay As Boolean = _appliedPresets.TryGetValue(npcFormID, Nothing)
        Dim priorOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
        _appliedPresets.TryGetValue(npcFormID, priorOverlay)

        ' UN read + UN parse del .jslot, DOS mapeos (el formato de RaceMenu no responde ambas con un objeto):
        '   • APPLY, sobre un CLONE del overlay previo -> con qué queda el NPC. El clone es obligatorio: varios
        '     campos no pueden expresar "ausente" (NAM9 es un vector fijo de 18 donde 0 es valor legítimo), así
        '     que se siembra del valor anterior y solo se pisa lo que el archivo declara. Alimenta el preview y OK.
        '   • DISPLAY, sobre un preset VACÍO -> qué trae el ARCHIVO. Alimenta conteos por categoría, filtro de
        '     compatibilidad de raza y el reporte, para no atribuirle al preset contenido propio del NPC.
        ' Nothing si el archivo no se puede leer (el browser lo saltea). SourcePath/Gender se estampan para el label.
        Dim mapper As Func(Of String, LooksmenuLoad_Form.SsePresetMapping) =
            Function(fp As String) As LooksmenuLoad_Form.SsePresetMapping
                Try
                    Dim j = RaceMenuJslot.Load(IO.File.ReadAllBytes(fp))
                    If j Is Nothing Then Return Nothing
                    ' raceFormID + gender → translate the .jslot POSITIONAL tint index to the record TINI value
                    ' (RaceMenuPresetMapper.JslotIndexToTini). Without it the skin tone / per-layer texture bind to the
                    ' wrong RACE layer on races whose TINI != position (incl. the body-QNAM skin tone at position 0).
                    Dim applied = If(priorOverlay IsNot Nothing, LooksmenuLoader.ClonePreset(priorOverlay),
                                                                New LooksmenuLoader.LooksmenuPreset() With {.Gender = gender})
                    RaceMenuPresetMapper.ApplyJslotToPreset(j, applied, _pluginManager, raceFormID, gender = CByte(1))
                    applied.SourcePath = fp
                    applied.Gender = gender

                    Dim fileOnly As New LooksmenuLoader.LooksmenuPreset() With {.Gender = gender}
                    RaceMenuPresetMapper.ApplyJslotToPreset(j, fileOnly, _pluginManager, raceFormID, gender = CByte(1))
                    fileOnly.SourcePath = fp

                    Return New LooksmenuLoad_Form.SsePresetMapping(applied, fileOnly)
                Catch
                    Return Nothing
                End Try
            End Function

        Dim npcHasBodyTri = NpcHasAnyBodyTri()
        Dim selected As LooksmenuLoader.LooksmenuPreset = Nothing
        Dim dialogResult As DialogResult
        Using dlg As New LooksmenuLoad_Form(_pluginManager, _dataPath, gender, raceDisplay, npcHasBodyTri,
                                            raceFormID, race, raceDefaultsForLm,
                                            isSse:=True, ssePresetsDir:=presetsDir, sseMapper:=mapper)
            ' Same baseline rule as the FO4 path: preserve from the PRE-DIALOG overlay, not from the one
            ' the live preview is currently rewriting.
            AddHandler dlg.PreviewRequested, Sub(s, args) PreviewLooksmenuOverlay(npcFormID, npc, args.Preset, args.Options, priorOverlay)
            dialogResult = dlg.ShowDialog(Me)
            selected = dlg.SelectedPreset
        End Using

        If dialogResult <> DialogResult.OK Then
            ' Cancel / [X] / Esc → restore pre-dialog overlay state and re-render.
            If hadPriorOverlay Then _appliedPresets(npcFormID) = priorOverlay Else _appliedPresets.Remove(npcFormID)
            Try
                Dim restoreVersion = Interlocked.Increment(_previewRequestVersion)
                Await LoadNPCOnDemandAsyncFromExisting(npc, restoreVersion)
            Catch ex As Exception
                MessageBox.Show($"Failed to restore preview after cancel: {ex.Message}",
                                "Load RaceMenu", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
            Return
        End If

        If selected Is Nothing Then Return
        ' OK: the live preview already left `selected` applied in _appliedPresets and rendered. Just mark dirty.
        MarkNpcDirty(npcFormID)
    End Function

    ''' <summary>Live-preview handler invoked by <see cref="LooksmenuLoad_Form.PreviewRequested"/>
    ''' on every selection change AND on every category toggle. Applies (or removes) the overlay and
    ''' triggers a non-blocking re-render. Concurrency-safe via _previewRequestVersion: rapid clicks
    ''' supersede each other, only the latest survives.
    ''' <para><paramref name="baseline"/> is the PRE-DIALOG overlay: the value the unticked categories
    ''' preserve. It must NOT be read from _appliedPresets here — this very method keeps overwriting that
    ''' entry, so the previously previewed preset would become the "NPC's own look".</para></summary>
    Private Sub PreviewLooksmenuOverlay(npcFormID As UInteger, npc As NPC_Data,
                                        preset As LooksmenuLoader.LooksmenuPreset,
                                        options As PresetCategories.PresetCategoryOptions,
                                        baseline As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then
            _appliedPresets.Remove(npcFormID)
        Else
            ' Same per-category merge Paste Look uses: ticked categories come from the preset, unticked
            ' ones from the baseline overlay (else the raw record). BuildFiltered clones, so the dialog's
            ' parsed object stays intact when the user toggles categories without re-selecting.
            Dim isSseGame = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
            Dim toApply = PresetCategoryFilter.BuildFiltered(preset, npc, baseline, options, isSseGame,
                                                            ResolveHdptForOrphanCascade(), AddressOf ResolveLmSkinTemplate)
            ' WYSIWYG: if the loaded JSON references an LM SkinTemplate, materialize its head/
            ' headRear HDPT swaps into preset.HeadPartFormIDs so Save ESP / Edit Face / Copy see
            ' the same picture the live render shows via ApplyPresetOverlayToNpcData. The template
            ' bundle is otherwise applied only to the runtime shadow; without this call the JSON
            ' could be loaded, the preview would render the template HDPTs, but exporting to ESP
            ' would emit raw NPC PNAM (no headRear swap).
            NpcRecordOverlay.MaterializeLmTemplateBundleToPreset(toApply, npc.Record.ConfigurationFlagsFemale, AddressOf ResolveLmSkinTemplate)
            ' Normalizar el TemplateColorIndex de TODOS los layers re-derivándolo desde el Color — misma
            ' resolución que Copy/Paste hace en BuildPresetFromState. El load de LooksMenu toma el "ColorID"
            ' crudo del JSON, que no siempre coincide con el TemplateIndex del RACE; sin esto el resolver del
            ' render (match por TemplateIndex) no encuentra la entrada y el layer cae a su color crudo
            ' (skin-tone slot-12 -> pálido/blanco). Idempotente; no-op en no-Palette.
            ' Raza EFECTIVA para normalizar los TemplateColorIndex: `npc` es el raw cacheado del ctx (raza
            ' vieja tras un cambio de raza en el editor); el catálogo de tints correcto es el de la raza
            ' pisada por el NpcRecordOverride, igual que el render/bake.
            Dim ovForTintNorm = TryGetNpcRecordOverride(npcFormID)
            Dim raceFidForTintNorm As UInteger = If(ovForTintNorm IsNot Nothing AndAlso ovForTintNorm.RaceFormID.HasValue AndAlso ovForTintNorm.RaceFormID.Value <> 0UI,
                                                    ovForTintNorm.RaceFormID.Value, npc.Record.Race)
            Dim raceForTintNorm As Canon.IRace = Nothing
            Dim raceRecForTintNorm = _pluginManager.GetRecord(raceFidForTintNorm)
            If raceRecForTintNorm IsNot Nothing AndAlso raceRecForTintNorm.Header.Signature = "RACE" Then
                raceForTintNorm = _ctx.ParseRaceCanonCached(raceRecForTintNorm)
            End If
            NormalizePresetTintTemplateColorIds(toApply, raceForTintNorm, npc.Record.ConfigurationFlagsFemale)

            ' Record which raw Misc (hairlines) this preset orphans by REPLACING a main-type parent
            ' (e.g. a hair swap): compute it HERE, at the apply point, so Save drops them the same way
            ' Edit Face does — the decision lives where the swap happens, not lazily at bake. No-op
            ' (empty set) when no parent was replaced, so lashes/AO/wet on untouched parents are safe.
            ' Recomputed AFTER MaterializeLmTemplateBundleToPreset (which can inject head/headRear HDPTs),
            ' so this supersedes the set BuildFiltered computed on the pre-materialize list.
            toApply.SuppressedRawHeadPartFormIDs = HeadPartResolver.ComputeReplacedParentOrphanMisc(
                npc.Record.PartesDeCabeza(), toApply.HeadPartFormIDs, ResolveHdptForOrphanCascade())

            _appliedPresets(npcFormID) = toApply
        End If
        Dim previewVersion = Interlocked.Increment(_previewRequestVersion)
        ' Fire-and-forget: the Async lambda runs on the UI sync context (LoadNPCOnDemandAsyncFromExisting
        ' already marshals back to the UI thread for the render). Errors are swallowed silently here —
        ' the user is mid-selection and a popup would be more disruptive than a stale preview.
        Dim _unused = PreviewLooksmenuOverlayAsync(npc, previewVersion)
    End Sub

    ''' <summary>FormID → parsed HDPT resolver (cached via <c>_ctx.ParseHdptCached</c>) for the
    ''' head-part orphan-cascade helpers in <see cref="HeadPartResolver"/>. Returns Nothing for
    ''' non-HDPT or unresolved FormIDs.</summary>
    Private Function ResolveHdptForOrphanCascade() As Func(Of UInteger, Canon.IHdpt)
        Return Function(fid As UInteger) As Canon.IHdpt
                   If fid = 0UI Then Return Nothing
                   Dim r = _pluginManager.GetRecord(fid)
                   If r IsNot Nothing AndAlso r.Header.Signature = "HDPT" Then Return _ctx.ParseHdptCached(r)
                   Return Nothing
               End Function
    End Function

    ''' <summary>True if any shape of the currently rendered NPC carries BODYTRI extra-data on
    ''' its NIF root. Used to default the "Apply BodySlide sliders" checkbox in the Load
    ''' LooksMenu dialog.</summary>
    Private Function NpcHasAnyBodyTri() As Boolean
        If _renderHost.LastRenderData Is Nothing OrElse _renderHost.LastRenderData.Shapes Is Nothing Then Return False
        For Each shape In _renderHost.LastRenderData.Shapes
            Dim meshKey As String = Nothing
            If _renderHost.LastRenderData.MeshDictKeys IsNot Nothing Then _renderHost.LastRenderData.MeshDictKeys.TryGetValue(shape, meshKey)
            If BodySlideTriResolver.ResolveAndLoad(shape, meshKey) IsNot Nothing Then Return True
        Next
        Return False
    End Function

    Private Async Function PreviewLooksmenuOverlayAsync(npc As NPC_Data, requestVersion As Integer) As Task
        Try
            Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[OVERLAY-PREVIEW] fire-and-forget overlay preview failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Function

    ''' <summary>Same flow as <see cref="LoadNPCOnDemandAsync"/> but skipping EnsureAssetDictionary
    ''' (already mounted) — used after applying / removing an overlay to re-resolve from scratch
    ''' so the resolver picks up the updated overlay state.
    ''' <para>Optional <paramref name="host"/> overrides the default <c>_renderHost</c> so an
    ''' editor form can drive its own embedded preview via <see cref="RenderInHostAsync"/>. When
    ''' <c>Nothing</c> the MainForm's host is used, which is the legacy behaviour.</para></summary>
    Private Async Function LoadNPCOnDemandAsyncFromExisting(npc As NPC_Data, requestVersion As Integer, Optional host As NpcRenderHost = Nothing) As Task
        If host Is Nothing Then host = _renderHost
        Dim baseState As NPCVisualState = Nothing
        Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
        ' NINGUNO de los llamadores de este camino es un gesto de azar (Reset NPC, RenderInHostAsync de
        ' los dos editores y del picker de outfit, cancel de Load LooksMenu/RaceMenu, preview de overlay,
        ' NPC Editor OK, Edit Outfit, ReloadCurrentNpcFull, Paste Look, readback del Save): todos ANCLAN.
        ' El ancla se calcula ACA y no dentro del resolver porque un host de EDITOR nace con
        ' LastRenderedState = Nothing y hay que caer al del MainForm — que es lo que el usuario tenia en
        ' pantalla al apretar Edit.
        Dim pin As New NpcStateResolver.LeveledLeafPin(ResolveShownTraitsLeaf(npc.FormID, host))
        Await Task.Run(Sub()
                           baseState = _stateResolver.ResolveNPCBaseState(npc, host, pin)
                           outfitEntries = BuildOutfitComboEntries(baseState)
                       End Sub)
        If requestVersion <> _previewRequestVersion Then Return

        ' Tint caches are per-NPC: paths/keys tied to one actor's race/skin TXST are not
        ' valid for another, and a poisoned entry (negative cache, GL upload that came back
        ' silently corrupt) would otherwise leak into every subsequent NPC. ClearFaceTintCaches
        ' drops _tintBytesCache + TintGpuCache + PristineDiffusePixels in one shot — the three
        ' caches share the same per-NPC invariant. Keep them across same-NPC reloads
        ' (overlay edits, paste look) so live tint refreshes stay fast on the second click.
        If host.CurrentBaseState IsNot Nothing AndAlso baseState IsNot Nothing _
           AndAlso host.CurrentBaseState.RootNpcFormID <> baseState.RootNpcFormID Then
            ' EL HOST VA EXPLÍCITO — el mismo con el que se decidió dos líneas arriba. Sin pasarlo,
            ' ClearFaceTintCaches caía al `_hostProvider()` (siempre el del MainForm) y limpiaba el host
            ' equivocado cuando esto se llama desde un formulario editor.
            _faceTintResolver.ClearFaceTintCaches(host)
        ElseIf host.CurrentBaseState Is Nothing Then
            ' Defensive: very first load of any NPC after process start — nothing to clear.
        End If

        host.CurrentBaseState = baseState

        ' Main-form UI is updated ONLY when rendering into the main host. Editor / outfit-picker hosts
        ' keep their sampled outfit on the host (host.OutfitEntries) and DON'T touch the MainForm-global
        ' outfit combo / record details / paste-enable — so an editor or picker render is fully isolated
        ' from the main viewer's state (the user's "no live preview en el render principal" rule).
        If host Is _renderHost Then
            _currentOutfitEntries = If(outfitEntries, New List(Of OutfitComboEntry))

            ' Recompute Paste enable now that the target NPC may have changed race/gender.
            UpdatePasteLookEnabled()

            ' Refresh the right-side record details panel so weights / morphs / tints reflect the
            ' overlay-applied state instead of the raw record. ApplyPresetOverlayToNpcData returns
            ' the raw NPC_Data when there's no overlay registered, so this is also a no-op for the
            ' non-overlay path. Header fields (FormID/EditorID/Plugin) are preserved by the shallow
            ' copy so the panel still identifies the record correctly.
            Dim modelFormID = NpcStateFactory.FaceAppearanceSourceFormID(baseState)
            Dim effective = ApplyPresetOverlayToNpcData(_ctx.GetParsedNpc(modelFormID), baseState.RootNpcFormID)
            PopulateRecordDetails(If(effective, npc))

            PopulateOutfitCombo()
        Else
            host.OutfitEntries = If(outfitEntries, New List(Of OutfitComboEntry))
        End If

        Await RenderCurrentStateAsync(requestVersion, host)
    End Function

    ''' <summary>Thin instance wrapper over <see cref="NpcRecordOverlay.ApplyPresetOverlayToNpcData"/>;
    ''' threads <see cref="_pluginManager"/> + <see cref="_appliedPresets"/> through. Real impl
    ''' lives in the helper module so offline bake (FaceGenBuilder) can reuse without coupling
    ''' to MainForm instance state.</summary>
    Private Function ApplyPresetOverlayToNpcData(raw As NPC_Data, selectedNpcFormID As UInteger) As NPC_Data
        Return NpcRecordOverlay.ApplyPresetOverlayToNpcData(raw, selectedNpcFormID, _appliedPresets,
                                                            _pluginManager, AddressOf ResolveLmSkinTemplate,
                                                            AddressOf _ctx.ParseRaceCanonCached)
    End Function

    ''' <summary>Resolver passed to the overlay helper so it can map an LM SkinTemplate id to
    ''' its full bundle (skin ARMO + face TXST + head/headRear HDPT). Nothing if the id isn't
    ''' in the loaded template cache — caller treats that as "no override".</summary>
    Private Function ResolveLmSkinTemplate(templateId As String) As LmSkinTemplate
        If String.IsNullOrEmpty(templateId) Then Return Nothing
        For Each tpl In _lmSkinTemplates
            If String.Equals(tpl.Id, templateId, StringComparison.Ordinal) Then Return tpl
        Next
        Return Nothing
    End Function

    ''' <summary>Friend wrapper so EditBody / EditFace can invoke
    ''' <see cref="NpcRecordOverlay.MaterializeLmTemplateBundleToPreset"/> with a delegate to this
    ''' MainForm's resolver. Same Function reference — just exposed at Friend scope.</summary>
    Friend Function ResolveLmSkinTemplate_Friend(templateId As String) As LmSkinTemplate
        Return ResolveLmSkinTemplate(templateId)
    End Function

    ''' <summary>Pick the leaf NPC_ to PIN an actor to when its Traits chain runs through a leveled list.
    ''' Handed to <see cref="NpcTemplateMaterializer.MakeCategoryOwn"/> by the NPC Editor and the Save apply,
    ''' so neither needs a PluginManager of its own.
    '''
    ''' <para>WYSIWYG: the leaf currently ON SCREEN wins. The preview already rolled this LVLN
    ''' (ResolveNPCBaseState → ResolveTraitsStateFromNPC, recorded in state.TraitsSourceFormID); rolling AGAIN
    ''' here would pin the actor to a DIFFERENT leaf than the one the user was looking at when they hit Edit —
    ''' they would edit face A and get face B. When there is no live pick to honour, the first stable leaf is
    ''' used. This delegate can be called independently for several template categories and again at save time;
    ''' a fresh random roll on each call would combine unrelated leaves in one NPC.</para></summary>
    ''' <summary>La hoja de LVLN que esta EN PANTALLA para <paramref name="rootNpcFormID"/>, o 0 si no
    ''' hay ninguna. UNICA implementacion de la ley "gana la hoja que esta en pantalla": la consumen
    ''' <see cref="ResolveLvlnPick_Friend"/> (editor de NPC + apply del Save) y el RENDER
    ''' (LoadNPCOnDemandAsyncFromExisting -> NpcStateResolver.ResolveNPCBaseState). Antes la ley existia
    ''' solo en el primero y el render volvia a sortear.
    ''' <para>Prefiere el host que se esta renderizando; cae al del MainForm porque un host de EDITOR
    ''' nace con <c>LastRenderedState = Nothing</c> (EditFace_Form lo crea en Shown y renderiza justo
    ''' despues), y lo que el usuario tenia en pantalla al apretar Edit es el preview principal.</para></summary>
    Friend Function ResolveShownTraitsLeaf(rootNpcFormID As UInteger, host As NpcRenderHost) As UInteger
        If rootNpcFormID = 0UI Then Return 0UI
        Dim hoja = HojaMostradaEn(host, rootNpcFormID)
        If hoja = 0UI Then hoja = HojaMostradaEn(_renderHost, rootNpcFormID)
        Return hoja
    End Function

    Private Shared Function HojaMostradaEn(h As NpcRenderHost, rootNpcFormID As UInteger) As UInteger
        If h Is Nothing OrElse h.IsDisposed Then Return 0UI
        Dim st = h.LastRenderedState
        If st Is Nothing OrElse st.RootNpcFormID <> rootNpcFormID Then Return 0UI
        Return st.TraitsSourceFormID
    End Function

    Friend Function ResolveLvlnPick_Friend(lvlnFormID As UInteger) As UInteger
        If lvlnFormID = 0UI Then Return 0UI

        Dim leaves = NpcTemplateHelpers.CollectLvlnLeafNpcFormIDs(lvlnFormID, _pluginManager)
        If leaves Is Nothing OrElse leaves.Count = 0 Then Return 0UI
        If leaves.Count = 1 Then Return leaves(0)

        Dim raiz As UInteger = 0UI
        If _renderHost IsNot Nothing AndAlso _renderHost.LastRenderedState IsNot Nothing Then raiz = _renderHost.LastRenderedState.RootNpcFormID
        Dim shown = ResolveShownTraitsLeaf(raiz, _renderHost)
        If shown <> 0UI AndAlso leaves.Contains(shown) Then
            Return shown
        End If

        ' ⛔ SEGUNDA MITAD DEL ANCLA — MUEVE BYTES DEL ESP (aplicada por decision expresa del usuario,
        ' 25-ago-2026). La hoja en pantalla puede NO ser entrada directa de esta lista
        ' (LVLN -> hoja B -> Use Traits -> C, con C lo que el render muestra): MEDIDO 69 NPC en FO4 y 235
        ' en SSE. Para esos, el `leaves(0)` de abajo materializa un actor que el usuario NUNCA vio — 47
        ' (FO4) y 189 (SSE) cambian de actor con esta rama, y en 18 y 185 el actor difiere en PNAM,
        ' genero, raza o DOFT. Es EXACTO porque en el corpus ninguna cadena de Traits encadena dos LVLN
        ' (hops max = 1 en los dos juegos), asi que el tramo posterior a la lista es DETERMINISTA.
        ' Misma ley que el paso (2b) de NpcStateResolver.ResolveSingleLeveledTemplate — se CONSUME de
        ' ahi, no se re-escribe: sin esta rama, RENDER y SAVE discrepan para esos 304 NPC.
        If shown <> 0UI AndAlso _stateResolver IsNot Nothing Then
            For Each hoja In leaves
                If _stateResolver.TerminalDeTraitsPublico(hoja) = shown Then Return hoja
            Next
        End If

        Return leaves(0)
    End Function

    ''' <summary>Per-layer clone — delegates to the canonical helper.</summary>
    Private Function CloneFaceTint(tl As LooksmenuLoader.CapaDeTintePreset) As LooksmenuLoader.CapaDeTintePreset
        Return LooksmenuLoader.CloneFaceTintLayer(tl)
    End Function


    ' NPC-record scalar/list override (NPC Editor) — storage + save apply
    ' =====================================================================

    ''' <summary>Return the NPC's authored record override, or Nothing when none exists. Used by the NPC Editor
    ''' to MERGE a new edit into the accumulated override (so successive edits latch, e.g. TraitsChanged).</summary>
    Friend Function TryGetNpcRecordOverride(npcFormID As UInteger) As NpcRecordOverride
        Dim ov As NpcRecordOverride = Nothing
        _npcRecordOverrides.TryGetValue(npcFormID, ov)
        Return ov
    End Function

    ''' <summary>True when a LooksMenu overlay (face/body/skin) is applied for this NPC. Lets the NPC Editor's
    ''' in-memory Traits materialization use the SAME skip-overlay-owned rule as the save apply, so the preview
    ''' matches the written record exactly (overlay-owned fields come from the overlay, not the template).</summary>
    Friend Function NpcHasOverlay(npcFormID As UInteger) As Boolean
        Return _appliedPresets.ContainsKey(npcFormID)
    End Function

    ''' <summary>Store (or replace) the NPC's authored record override. An empty override is dropped so it never
    ''' reaches the save apply. Called by the NPC Editor on OK.</summary>
    Friend Sub SetNpcRecordOverride(npcFormID As UInteger, ov As NpcRecordOverride)
        If ov Is Nothing OrElse ov.IsEmpty Then
            _npcRecordOverrides.Remove(npcFormID)
        Else
            _npcRecordOverrides(npcFormID) = ov
        End If
    End Sub

    ''' <summary>Seed the NPC Editor's Inventory-tab outfit fields with the EFFECTIVE Default/Sleep outfit
    ''' FormIDs — the LooksMenu overlay override when one is set (e.g. a prior Edit Outfit pick), else the raw
    ''' record value the caller passes in. Outfit edits live in the same overlay as the Edit Outfit picker, so
    ''' the editor must show the overlaid value, not the stale raw DOFT/SOFT. Out params default to the raws.</summary>
    Friend Sub GetEffectiveNpcOutfitsForEditor(npcFormID As UInteger, rawDefault As UInteger, rawSleep As UInteger,
                                               ByRef effectiveDefault As UInteger, ByRef effectiveSleep As UInteger)
        effectiveDefault = rawDefault
        effectiveSleep = rawSleep
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(npcFormID, p) AndAlso p IsNot Nothing Then
            If p.DefaultOutfitFormIDOverride.HasValue Then effectiveDefault = p.DefaultOutfitFormIDOverride.Value
            If p.SleepOutfitFormIDOverride.HasValue Then effectiveSleep = p.SleepOutfitFormIDOverride.Value
        End If
    End Sub

    ''' <summary>Commit a Default (DOFT) outfit override from the NPC Editor onto the LooksMenu overlay — the SAME
    ''' path the Edit Outfit picker uses, so the preview, outfit combo and Save all resolve it once.
    ''' <paramref name="value"/>: Nothing = clear the override (preserve raw NPC.DOFT); 0 = no outfit; other =
    ''' OTFT FormID. Creates the overlay preset on demand when setting a value; clearing with no preset is a
    ''' no-op. Only call when the field actually changed, so an untouched outfit never clobbers a prior pick.</summary>
    Friend Sub SetNpcDefaultOutfitOverrideFromEditor(npcFormID As UInteger, value As UInteger?)
        Dim p = EnsureOverlayPresetForOutfit(npcFormID, value)
        If p IsNot Nothing Then p.DefaultOutfitFormIDOverride = value
    End Sub

    ''' <summary>Commit a Sleep (SOFT) outfit override from the NPC Editor onto the LooksMenu overlay. Same shape
    ''' and semantics as <see cref="SetNpcDefaultOutfitOverrideFromEditor"/> but for NPC.SOFT.</summary>
    Friend Sub SetNpcSleepOutfitOverrideFromEditor(npcFormID As UInteger, value As UInteger?)
        Dim p = EnsureOverlayPresetForOutfit(npcFormID, value)
        If p IsNot Nothing Then p.SleepOutfitFormIDOverride = value
    End Sub

    ''' <summary>Return the overlay preset for this NPC, creating one when we're about to STORE an override value
    ''' (HasValue). When clearing (value = Nothing) and no preset exists, return Nothing — there's nothing to
    ''' clear, and we must not conjure an empty overlay that would spuriously flag the NPC as changed.</summary>
    Private Function EnsureOverlayPresetForOutfit(npcFormID As UInteger, value As UInteger?) As LooksmenuLoader.LooksmenuPreset
        Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(npcFormID, p) AndAlso p IsNot Nothing Then Return p
        If Not value.HasValue Then Return Nothing
        p = New LooksmenuLoader.LooksmenuPreset()
        _appliedPresets(npcFormID) = p
        Return p
    End Function

    ''' <summary>Enhebra el estado de la app en <see cref="NpcRecordOverrideApplier.Aplicar"/>. Es el delegado
    ''' <see cref="NpcOverrideSaver.SaveContext.ApplyNpcRecordOverride"/>, y por eso lleva <c>strict</c> hasta el
    ''' fondo en vez de fijarlo: con <c>strict:=True</c> (el guardado) una categoría que no se puede materializar
    ''' ABORTA, igual que antes de la mudanza — si el bit Use-X se bajara igual, la plantilla dejaría de llenar el
    ''' campo y el NPC se quedaría con su valor propio vacío. Con <c>strict:=False</c> (el diálogo, que compone lo
    ''' mismo sólo para LEER un bit) devuelve el motivo en vez de matar el proceso al abrirse.
    ''' <para>El cuerpo se mudó a <see cref="NpcRecordOverrideApplier"/> para que un arnés pueda medir la
    ''' PRECEDENCIA overlay-vs-override sin replicarla: media ley del guardado era inalcanzable desde
    ''' <c>Tools/</c> mientras vivía en un <c>Private</c> de este formulario. Wrapper fino, el mismo patrón que
    ''' <see cref="ApplyPresetOverlayToNpcData"/> sobre <see cref="NpcRecordOverlay"/>.</para></summary>
    Private Function ApplyNpcRecordOverrideToSpec(npcSpec As NPC_Data, npcFormID As UInteger, strict As Boolean) As String
        Return NpcRecordOverrideApplier.Aplicar(npcSpec, npcFormID, _npcRecordOverrides,
                                                AddressOf _ctx.GetParsedNpc,
                                                Function(f As UInteger) _appliedPresets.ContainsKey(f),
                                                AddressOf ResolveLvlnPick_Friend,
                                                strict)
    End Function


    ''' <summary>In-memory clipboard for Copy Look / Paste Look. Lives at process scope so the
    ''' user can copy from one NPC and paste onto another (which is the whole point of testing
    ''' the overlay path round-trip with no JSON file involved).</summary>
    Private _clipboardPreset As LooksmenuLoader.LooksmenuPreset = Nothing

    ''' <summary>Source race FormID of the NPC the clipboard was copied from. Stored separately
    ''' from <see cref="_clipboardPreset"/> because the LooksMenu schema doesn't carry race
    ''' (CharGenInterface.cpp:90 only writes Gender). Used by <see cref="IsClipboardCompatibleWithCurrentNpc"/>
    ''' to gate Paste so the user can only paste between NPCs of the same race + gender — outside
    ''' that boundary the HDPTs (gender-specific) and morph hashes (race-specific) don't translate
    ''' and the result is visually broken.</summary>
    Private _clipboardSourceRaceFormID As UInteger = 0UI

    ''' <summary>Currently-selected NPC matches the clipboard's source NPC by race AND gender.
    ''' Returns False when there's no clipboard yet, no NPC selected, or either dimension differs.</summary>
    Private Function IsClipboardCompatibleWithCurrentNpc() As Boolean
        If _clipboardPreset Is Nothing Then Return False
        If _renderHost.CurrentBaseState Is Nothing Then Return False
        If _renderHost.CurrentBaseState.RaceFormID <> _clipboardSourceRaceFormID Then Return False
        Dim targetGender As Byte = If(_renderHost.CurrentBaseState.IsFemale, CByte(1), CByte(0))
        If _clipboardPreset.Gender <> targetGender Then Return False
        Return True
    End Function

    ''' <summary>Re-evaluate the Paste button's enable state. Call after Copy completes (clipboard
    ''' just changed) and after every NPC selection change (target just changed). Marshals to UI
    ''' thread when needed.</summary>
    Private Sub UpdatePasteLookEnabled()
        Dim shouldEnable = IsClipboardCompatibleWithCurrentNpc()
        If InvokeRequired Then
            Invoke(Sub() ButtonPasteLook.Enabled = shouldEnable)
        Else
            ButtonPasteLook.Enabled = shouldEnable
        End If
    End Sub

    ''' <summary>LooksmenuPreset con lo que se está renderizando: lee el mismo NPC_Data efectivo que
    ''' consume el render y emite el esquema Save de LooksMenu, para que Paste use exactamente el
    ''' camino ApplyPresetOverlayToNpcData de Load (sin codepath paralelo ni drift de esquema).
    ''' Fidelidad a CharGenInterface.cpp SavePreset: HeadParts descarta IsExtraPart (0x08) — los
    ''' extras vuelven solos porque CollectHeadPartCandidate expande el HNAM en render; Tints saltea
    ''' Value=0. Divergencia deliberada: Morphs.Intensity se escribe aunque sea 1.0, porque es lo que
    ''' LoadPreset interpreta.</summary>
    Private Function BuildPresetFromState(state As NPCVisualState) As LooksmenuLoader.LooksmenuPreset
        If state Is Nothing Then Return Nothing
        Dim modelFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim raw = _ctx.GetParsedNpc(modelFormID)
        If raw Is Nothing Then Return Nothing
        ' Capture rendered state — overlay-on-top-of-template, just like the renderer reads it.
        Dim effective = ApplyPresetOverlayToNpcData(raw, state.RootNpcFormID)

        Dim preset As New LooksmenuLoader.LooksmenuPreset With {
            .SourcePath = $"<clipboard from {raw.EditorID}>",
            .Gender = If(state.IsFemale, CByte(1), CByte(0))
        }

        ' HeadParts. state.HeadPartFormIDs only carries explicit NPC.PNAM entries — slots that
        ' the NPC didn't override (Meatcaps for most humans, Teeth/HeadRear sometimes) get filled
        ' by MergeHeadPartsWithRaceDefaults at render time, NOT before. LooksMenu's SavePreset
        ' (CharGenInterface.cpp:79-103) reads npc->headParts which is the post-merge runtime list,
        ' so for the JSON to round-trip we have to call the same merger here. Then filter the
        ' IsExtraPart (flag 0x08) entries — those are addons (lashes, hairlines, AO meshes) that
        ' the engine regenerates from each main HDPT's HNAM extras and shouldn't be in the JSON.
        Dim merged = _meshCollector.MergeHeadPartsWithRaceDefaults(state)
        For Each fid In merged
            If fid = 0UI Then Continue For
            Dim rec = _pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue For
            Dim hd = _ctx.ParseHdptCached(rec)
            If (hd.Flags And HeadPartFlagIsExtra) <> 0 Then Continue For
            preset.HeadPartFormIDs.Add(fid)
        Next

        preset.HairColorFormID = state.HairColorFormID
        preset.WeightThin = state.WeightThin
        preset.WeightMuscular = state.WeightMuscular
        preset.WeightFat = state.WeightFat

        ' Morphs (chargen face vertex via MSDK/MSDV, body region via MRSV, face bones via FMRI/FMRS,
        ' intensity via FMIN). All come from the effective record so overlay values win.
        For Each kv In effective.Record.MorfosDeCara()
            preset.ChargenFaceMorphs(kv.Key) = kv.Value
        Next
        preset.BodyMorphValues.AddRange(effective.Record.ValoresDeRegionCorporal())
        Dim effectiveFo4 = TryCast(effective.Record, Canon.NpcFO4)
        If effectiveFo4 IsNot Nothing Then
            For Each fm In effectiveFo4.FaceMorphs
                preset.FaceBoneRegions(fm.FaceMorphIndex) = New Single() {
                    fm.ValuesPositionX, fm.ValuesPositionY, fm.ValuesPositionZ,
                    fm.ValuesRotationX, fm.ValuesRotationY, fm.ValuesRotationZ, fm.ValuesScale}
            Next
        End If
        preset.FacialMorphIntensity = effective.Record.IntensidadDeMorfoFacial()

        ' BodySlide vertex sliders: F4SE-only, no record-level source. Pulled directly from the
        ' overlay preset for this NPC because ApplyPresetOverlayToNpcData doesn't touch them
        ' (NPC_Data has no BodyMorphs field). Without this copy Save LooksMenu would drop every
        ' BodySlide slider the user dialed in via the Edit Body form.
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(state.RootNpcFormID, overlay) AndAlso overlay IsNot Nothing Then
            For Each kv In overlay.BodyMorphSliders
                preset.BodyMorphSliders(kv.Key) = kv.Value
            Next
            ' Body overlays (tattoos / body paint): F4SE-only, no record-level source, same as
            ' BodyMorphSliders above. Deep-clone each entry (cloning the float arrays so the
            ' snapshot is independent) so Copy Look captures them and Save Looksmenu emits them.
            For Each ov In overlay.Overlays
                preset.Overlays.Add(New LooksmenuLoader.OverlayEntry With {
                    .TemplateId = ov.TemplateId,
                    .Priority = ov.Priority,
                    .Tint = CType(ov.Tint?.Clone(), Single()),
                    .OffsetUV = CType(ov.OffsetUV?.Clone(), Single()),
                    .ScaleUV = CType(ov.ScaleUV?.Clone(), Single())
                })
            Next
            ' LM SkinTemplate id is overlay-only (no record source). Carry through so Copy Look
            ' captures it and Save Looksmenu emits it.
            preset.SkinTemplateId = If(overlay.SkinTemplateId, "")
            ' Ajuste manual del tono del cuerpo (QNAM): overlay-only igual que los de arriba, y PARTE DEL LOOK
            ' -es la correccion que hace que ese cuerpo y esa cara se lean como la misma piel-, asi que viaja
            ' con Copy Look. Se filtra junto con los TINTS (PresetCategoryFilter, categoria FaceTints): es un
            ' ajuste de tinte, no una categoria nueva.
            preset.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(overlay.SkinToneOffset)
        End If

        ' NPC.WNAM skin override: capturamos la skin EFECTIVA que se está renderizando ahora —
        ' que ya considera overlay.SkinFormIDOverride si existe, raw NPC.WNAM como fallback, y
        ' RACE.WNAM si ambos son 0 (ver ApplyRaceFallbacks / RecomputeEffectiveSkinFormID).
        ' Capturar state.SkinFormID directamente vs overlay-only garantiza que Copy → Paste
        ' transfiera el skin AUNQUE el NPC source no tenga overlay explícito (caso típico:
        ' vanilla NPC con WNAM autoreado).
        ' SerializePreset SÍ emite este campo (`_npcm_SkinFormID`, LooksmenuLoader.vb). Consecuencia REAL,
        ' deliberada y compartida con DOFT/SOFT: como se captura la piel EFECTIVA (incluye el fallback
        ' RACE.WNAM), todo preset guardado PINNEA esa ARMO, y cargarlo sobre un NPC de otra raza se la
        ' impone. Es la misma ley que los dos campos de abajo; si algún día se quiere cambiar, se cambian
        ' los TRES juntos.
        preset.SkinFormIDOverride = state.SkinFormID

        ' NPC.DOFT default outfit: capturamos el outfit EFECTIVO (post-override) igual que skin.
        ' Se arrastra Copy → Paste (gated por options.Outfit) y SerializePreset lo emite como
        ' _npcm_DefaultOutfit. state.DefaultOutfitFormID ya considera el override aplicado en
        ' ResolveNPCBaseState.
        preset.DefaultOutfitFormIDOverride = state.DefaultOutfitFormID
        ' NPC.SOFT sleep outfit: MISMO par que DOFT — la categoría Outfit del filtro de Paste revierte
        ' LOS DOS (ver PresetCategoryFilter), así que los dos tienen que capturarse acá simétricamente:
        ' si sólo viajara el DOFT, con Outfit tildado el sleep outfit del NPC origen nunca llegaría al
        ' destino (quedaría en Nothing = "preservar el del destino").
        preset.SleepOutfitFormIDOverride = state.SleepOutfitFormID

        ' NPC.ACBS bit 0x04 "Is CharGen Face Preset": se captura el valor EFECTIVO (overlay si existe,
        ' si no el bit crudo); sin esto Copy->Paste perdía la flag aunque el checkbox estuviera activo.

        If overlay IsNot Nothing AndAlso overlay.IsCharGenFacePreset.HasValue Then
            preset.IsCharGenFacePreset = overlay.IsCharGenFacePreset.Value
        Else
            preset.IsCharGenFacePreset = raw.Record.ConfigurationFlagsIsCharGenFacePreset
        End If

        ' WYSIWYG: with the SkinTemplateId carried over, materialize the template's HDPT bundle
        ' into preset.HeadPartFormIDs so a paste at the destination NPC still emits the correct
        ' PNAM at Save ESP time. Without this, the clipboard would carry SkinTemplateId but its
        ' headRear swap would only ever exist in the runtime shadow, dropping out of any ESP
        ' the destination NPC writes after a paste.
        NpcRecordOverlay.MaterializeLmTemplateBundleToPreset(preset, state.IsFemale, AddressOf ResolveLmSkinTemplate)

        ' Tints: se saltean las entradas Value=0. El ORDEN importa, porque determina el orden de composicion de
        ' capas en render. El orden natural del record es el del ESP (TETI/TEND), pero el motor in-game reordena
        ' los tints al de las Options de los TintTemplateGroups de la RACE, y eso es lo que hace que un blend no
        ' conmutativo como SoftLight de un resultado estable entre Save y Load de LooksMenu. Como LM in-game
        ' escribe en orden de grupo de RACE, para round-trippear hay que igualarlo.
        ' Ademas se resuelve el TemplateColorIndex posicional de cada capa (el TEND vanilla guarda la POSICION en
        ' el array TTEC de la RACE) al TemplateIndex absoluto de ese color, que es lo que emite LooksMenu: sin
        ' esa conversion el ColorID round-trippea como 0, que es la posicion que suele usar vanilla.
        Dim raceRec = If(state.RaceFormID <> 0UI, _pluginManager.GetRecord(state.RaceFormID), Nothing)
        Dim race As Canon.IRace = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            race = _ctx.ParseRaceCanonCached(raceRec)
        End If

        ' Build a TETI.Index → RACE-order rank dict by walking the gender-appropriate TintGroups
        ' Options in order of appearance. Layers whose Index isn't found in the RACE (custom mods?)
        ' get rank Integer.MaxValue → appended at the end.
        Dim raceTintRank As New Dictionary(Of UShort, Integer)
        If race IsNot Nothing Then
            Dim tintGroups = LmCustomTintLoader.Fusionar(race, state.IsFemale, _pluginManager, _ctx.DataPath)
            Dim rank As Integer = 0
            For Each grp In tintGroups
                For Each opt In grp.Options
                    If Not raceTintRank.ContainsKey(opt.Index) Then
                        raceTintRank(opt.Index) = rank
                        rank += 1
                    End If
                Next
            Next
        End If

        Dim layersWithRank = LooksmenuLoader.CapasDeTinteDelRecord(effective.Record).
            Where(Function(tl) tl.Value > 0).
            Select(Function(tl, originalIdx)
                       Dim r As Integer = Integer.MaxValue
                       raceTintRank.TryGetValue(tl.Index, r)
                       Return New With {.Layer = tl, .Rank = r, originalIdx}
                   End Function).
            OrderBy(Function(x) x.Rank).
            ThenBy(Function(x) x.originalIdx).
            ToList()

        For Each entry In layersWithRank
            Dim cloned = CloneFaceTint(entry.Layer)
            ResolveTemplateColorIdToAbsolute(cloned, race, state.IsFemale)
            preset.FaceTintLayers.Add(cloned)
        Next

        ' SSE (Skyrim) capture. All FO4 capture above is game-agnostic and untouched; this branch ONLY runs
        ' under Skyrim so FO4 Copy/Save snapshots stay byte-identical. It populates the preset's SSE carriers
        ' (weight / body morphs / body overlays / NAM9-NAMA head morphs / sculpt / custom morphs / tints) so
        ' Copy and Save capture the FULL effective SSE state, exactly as the render+bake read it.
        '
        ' Source priority per field: the existing overlay `_appliedPresets(RootNpcFormID)` (fetched as `overlay`
        ' earlier in this function) wins because it holds live edits; record-backed fields fall back to the parsed NPC (`raw`)
        ' when the overlay is absent or hasn't set them. Body morphs / sculpt / custom morphs / body overlays
        ' have NO record source (F4SE/RaceMenu-only) → left empty when there is no overlay.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ' --- Vanilla body weight (NAM7): overlay SseWeight else record NAM7 (default 100). ---
            If overlay IsNot Nothing AndAlso overlay.SseWeight.HasValue Then
                preset.SseWeight = overlay.SseWeight.Value
            ElseIf raw.Record.TienePesoDeSkyrim() Then
                preset.SseWeight = raw.Record.PesoDeSkyrim()
            Else
                preset.SseWeight = 100.0F
            End If

            ' --- Head morphs (NAM9 18 floats + NAMA 4 type uints): overlay if set, else parse the record. ---
            If overlay IsNot Nothing AndAlso overlay.HasSseMorphs AndAlso overlay.SseNam9 IsNot Nothing Then
                preset.SseNam9 = DirectCast(overlay.SseNam9.Clone(), Single())
                ' Sin SseNama el vector va a CENTINELAS, no a ceros: 0 es un tipo REAL y esta rama estaba
                ' diciendo lo contrario que la de abajo sobre el mismo campo. Ver DefaultNamaVector.
                preset.SseNama = If(overlay.SseNama Is Nothing, SseNam9MorphMap.DefaultNamaVector(), DirectCast(overlay.SseNama.Clone(), UInteger()))
                preset.HasSseMorphs = True
                ' EL SLOT 18 TAMBIÉN VIAJA POR ACÁ. Estaba sólo en la rama del `Else`, así que el arreglo era
                ' INERTE justo en el camino normal: basta con haber cargado un .jslot o tocado UN slider en Edit
                ' Face para que exista overlay, y entonces "Save RaceMenu Preset" volvía a emitir la constante
                ' centinela y pisaba el VampireMorph real del NPC. El overlay lo trae si vino de un .jslot; si no,
                ' se cae al record, que es la fuente de verdad cuando el editor no lo tocó.
                preset.SseVampireMorph = If(overlay.SseVampireMorph.HasValue,
                                            overlay.SseVampireMorph,
                                            VampireMorphFromNam9(raw))
            Else
                Dim rawNam9 = raw.Record.DeslizadoresDeCara()
                Dim rawNama = raw.Record.PartesDeCara()
                Dim nam9(SseNam9MorphMap.Nam9SliderCount - 1) As Single
                For i = 0 To SseNam9MorphMap.Nam9SliderCount - 1
                    If rawNam9 IsNot Nothing AndAlso i < rawNam9.Length Then
                        Dim v = rawNam9(i)
                        If Single.IsNaN(v) OrElse Single.IsInfinity(v) Then v = 0.0F
                        nam9(i) = v
                    End If
                Next
                Dim nama(SseNam9MorphMap.NamaFamilyCount - 1) As UInteger
                For f = 0 To SseNam9MorphMap.NamaFamilyCount - 1
                    ' El centinela 0xFFFFFFFF ("esta familia NO tiene tipo asignado") viaja INTACTO — NO
                    ' colapsarlo a 0, que es un tipo REAL: el lector del .jslot (RaceMenuPresetMapper,
                    ' "0xFFFFFFFF = unset/default, preserved (never forced to a real type 0)") y el motor
                    ' lo distinguen igual (skee asigna el tipo tal cual, `presets[i] = value`,
                    ' PresetInterface.cpp:1052-1058; NpcMorphResolver no hace nada con el centinela y
                    ' aplica el morph "Default" con peso 1.0 con el 0). Colapsarlo le CAMBIA LA CARA a un
                    ' NPC sin NAMA al guardar y recargar su preset.
                    ' Medido: en los 48 presets reales conviven 43 centinelas y 27 ceros, o sea que RaceMenu
                    ' escribe los dos y el centinela es un valor legítimo del formato.
                    ' El sitio gemelo es PresetCategoryFilter (misma ley, mismo motivo).
                    nama(f) = If(rawNama IsNot Nothing AndAlso f < rawNama.Length,
                                 rawNama(f), SseNam9MorphMap.NamaUnset)
                Next
                ' Slot 18 del NAM9 (VampireMorph): fuera de los 18 sliders editables, pero parte del record. Se
                ' captura para que ToJslot no lo reemplace por su constante. Ver LooksmenuPreset.SseVampireMorph.
                preset.SseVampireMorph = VampireMorphFromNam9(raw)
                preset.SseNam9 = nam9
                preset.SseNama = nama
                preset.HasSseMorphs = (rawNam9 IsNot Nothing OrElse rawNama IsNot Nothing)
            End If

            ' --- Face tints (TINI/TINC/TINV/TIAS): overlay if set, else the record's authored list. ---
            If overlay IsNot Nothing AndAlso overlay.HasSseTints AndAlso overlay.SseTintLayers IsNot Nothing Then
                preset.SseTintLayers = PresetCategoryFilter.CloneSseTintLayers(overlay.SseTintLayers)
                preset.HasSseTints = True
            Else
                Dim delRecord = LooksmenuLoader.CapasDeTinteSseDelRecord(raw.Record)
                If delRecord.Count > 0 Then
                    preset.SseTintLayers = delRecord
                    preset.HasSseTints = True
                End If
            End If

            ' Se captura `ExplicitHeadTextureFormID`, NO el efectivo: el explícito vale 0 justamente cuando
            ' el TXST salió del default de la RAZA. Copiar el efectivo convertiría ese default en un override
            ' explícito y, al pegarlo sobre un NPC de otra raza, le clavaría la cara de la raza de origen.
            ' Va sólo en la rama SSE por ORIGEN DEL DATO, no por olvido: en FO4 el override de cara viaja
            ' por la plantilla de LooksMenu, que esta misma función ya captura en su propio carrier; poblar
            ' además éste duplicaría el dato y le daría precedencia al de menor rango. En SSE no existen esas
            ' plantillas, así que éste es el ÚNICO carrier.
            ' La traducción al carrier tri-estado es EXPLÍCITA, no una asignación directa: `UInteger` ensancha a
            ' `UInteger?` en silencio (Option Strict está Off) y un Explicit=0 —que es "este NPC no tiene FTST
            ' propio", el caso de arriba— se volvería Some(0) = CLEAR EXPLÍCITO. Copiar la cara de un NPC sin FTST
            ' le borraría el FTST al target al pegarla. 0 ⇒ Nothing preserva la semántica que este bloque ya tenía.
            ' El OVERLAY manda cuando existe, y NO se puede derivar esto del `state`: el state colapsa dos
            ' casos distintos en Explicit=0 — "el NPC no tiene FTST propio" y "el usuario apretó Clear (no FTST)".
            ' Leyendo sólo el state, un Copy Look de una cara con el FTST borrado viajaba como Nothing
            ' (= "preservar") y al pegarla el target se quedaba con SU PROPIO FTST: la cara pegada no se parecía
            ' a la copiada, en silencio. El overlay sí distingue los tres estados, así que se prefiere.
            ' Mismo criterio que los demás carriers SSE de este bloque (SseWeight, SseNam9, SseTintLayers).
            If overlay IsNot Nothing AndAlso overlay.SseHeadTextureFormIDOverride.HasValue Then
                preset.SseHeadTextureFormIDOverride = overlay.SseHeadTextureFormIDOverride
            ElseIf state.ExplicitHeadTextureFormID <> 0UI Then
                ' If/Then, NO el ternario `If(cond, valor, Nothing)`: con un nullable ese ternario resuelve el
                ' tipo dominante a UInteger y convierte el `Nothing` en 0 ⇒ HasValue=True con valor 0 = CLEAR,
                ' justo lo contrario de lo que se quiere. Es la trampa de VB que este proyecto ya se comió antes.
                preset.SseHeadTextureFormIDOverride = state.ExplicitHeadTextureFormID
            Else
                preset.SseHeadTextureFormIDOverride = Nothing
            End If

            ' --- F4SE/RaceMenu-only carriers (no record source): overlay only, else leave empty. ---
            If overlay IsNot Nothing Then
                If overlay.BodyMorphsKeyed IsNot Nothing Then
                    Dim bmk As New Dictionary(Of String, Dictionary(Of String, Single))(StringComparer.OrdinalIgnoreCase)
                    For Each kv In overlay.BodyMorphsKeyed
                        Dim inner As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
                        If kv.Value IsNot Nothing Then
                            For Each ik In kv.Value : inner(ik.Key) = ik.Value : Next
                        End If
                        bmk(kv.Key) = inner
                    Next
                    preset.BodyMorphsKeyed = bmk
                End If
                preset.SseBodyOverlays = LooksmenuLoader.CloneSseBodyOverlays(overlay.SseBodyOverlays)
                preset.SseNodeTransforms = LooksmenuLoader.CloneSseNodeTransforms(overlay.SseNodeTransforms)
                ' ⛔ SIN ESTA LÍNEA LA RED DE `ToJslot` NO SE ALCANZA. `RaceMenuPresetMapper.vb:90-95` ya
                ' re-emite las partes de cabeza que no resolvieron, y su propio comentario dice que "HOY
                ' ESTA RAMA NO SE ALCANZA… ese constructor NO copia SseUnresolvedHeadParts". Éste es ese
                ' constructor. El clon de `LooksmenuLoader:806` y `PresetCategoryFilter:205` sí lo copian;
                ' sólo faltaba acá, que es justo el camino de "Save RaceMenu Preset".
                ' MEDIDO sobre los 48 .jslot del usuario: 49 entradas de 14 mods no instalados, en 33 de 48
                ' archivos (SGEyebrows.esp 9, MikanEyes 5, Brows.esp 5, …). Sin esto, abrir uno de esos
                ' presets y guardarlo lo deja sin esas cejas/ojos para siempre.
                ' ⚠️ DIVERGENCIA DELIBERADA, decidida por el usuario (24-ago-2026): el canónico NO conserva
                ' esto. `PresetInterface.cpp:355-365` arma el array recorriendo `npc->headparts` —lo que el
                ' ACTOR tiene— y `:978-987` nunca mete en `presetData->headParts` la que no resuelve, así
                ' que un cargar→aplicar→guardar dentro del propio RaceMenu la pierde igual. Acá se
                ' preserva porque esto es un EDITOR, con el mismo criterio que la app ya aplica al color
                ' de pelo ("PRESERVACIÓN, no invención", LooksmenuLoader.vb:1138-1150).
                ' (La copia de SseUnresolvedHeadParts se mudo a CopyUnresolvedHeadPartsToSnapshot, que
                ' corre FUERA de este gate: el agujero era de los DOS juegos y la ley vive en un solo lado.)
                ' Los elementos de primera persona del .jslot: no se modelan ni se editan, pero sin esta línea el
                ' "Save RaceMenu preset" de una sesión posterior los emitía perdidos.
                preset.SseFirstPersonTransformsRaw = If(overlay.SseFirstPersonTransformsRaw Is Nothing, Nothing,
                                                        New List(Of String)(overlay.SseFirstPersonTransformsRaw))
                preset.SseHairColorRgb = overlay.SseHairColorRgb
                preset.SseSkinOverrides = LooksmenuLoader.CloneSseSkinOverrides(overlay.SseSkinOverrides)
                If overlay.SseSculptHead IsNot Nothing Then
                    Dim sc As New List(Of NPC_SculptVert)(overlay.SseSculptHead.Count)
                    For Each sv In overlay.SseSculptHead
                        sc.Add(New NPC_SculptVert With {.Index = sv.Index, .Dx = sv.Dx, .Dy = sv.Dy, .Dz = sv.Dz})
                    Next
                    preset.SseSculptHead = sc
                End If
                preset.SseSculptParts = LooksmenuLoader.CloneSseSculptParts(overlay.SseSculptParts)
                If overlay.SseCustomMorphs IsNot Nothing Then
                    Dim cms As New List(Of NPC_CustomMorph)(overlay.SseCustomMorphs.Count)
                    For Each cm In overlay.SseCustomMorphs
                        cms.Add(New NPC_CustomMorph With {.Name = cm.Name, .Value = cm.Value})
                    Next
                    preset.SseCustomMorphs = cms
                End If
                If overlay.SseTintTexOverride IsNot Nothing AndAlso overlay.SseTintTexOverride.Count > 0 Then
                    preset.SseTintTexOverride = New Dictionary(Of Integer, String)(overlay.SseTintTexOverride)
                End If
            End If
        End If

        ' ⭐ LO QUE NO SE PUDO RESOLVER, SE PRESERVA — FUERA del gate de juego a proposito: FO4 no
        ' poblaba NINGUNO de los campos (236 identificadores en 201 de 368 presets se perdian al guardar)
        ' y SSE solo poblaba el suyo dentro del bloque de arriba. Ver el doc del helper.
        LooksmenuLoader.CopyUnresolvedHeadPartsToSnapshot(overlay, preset)

        ' BuildPresetFromState produces a complete snapshot of the rendered NPC. By definition
        ' every overlay-replaceable field is "present" in this snapshot, so all Has* flags are
        ' True — the resulting preset, when applied as overlay, fully replaces those fields on
        ' any NPC (including wiping when one of the lists ended up empty after edits).
        preset.HasFaceTintLayers = True
        preset.HasChargenFaceMorphs = True
        preset.HasBodyMorphValues = True
        preset.HasFaceBoneRegions = True
        preset.HasHeadPartFormIDs = True
        preset.HasOverlays = True
        ' Origin reset: the line above asserts authority based on "snapshot is complete",
        ' independent of the LM template. So the writer-trackable origin flag stays False —
        ' otherwise a Paste followed by an LM template change in EditBody would erroneously
        ' Retract the HDPTs the snapshot put in (the user didn't ask for that).
        ' LmTemplateInjectedHdptFormIDs likewise stays empty: the HDPTs in HeadPartFormIDs at
        ' this point describe the rendered state, not the template's contribution per se.
        preset.HasHeadPartFormIDsSetByTemplate = False
        preset.LmTemplateInjectedHdptFormIDs.Clear()

        Return preset
    End Function

    ''' <summary>Resuelve layer.TemplateColorIndex (el ColorID del TEND) SOLO por color: gana el
    ''' preset TTEC cuyo CLFM RGB coincide; entre los de igual color desempata el Alpha más cercano
    ''' a la opacidad; sin coincidencia de color → -1 (RGB custom, se usa el TEND directo).
    ''' Delegación a FaceTintInputBuilder.ResolveTemplateColorIndex, única fuente compartida con el
    ''' editor. Formato y comportamiento de LooksMenu ante -1: memoria 60-feature-looksmenu-tints.</summary>
    Private Sub ResolveTemplateColorIdToAbsolute(layer As LooksmenuLoader.CapaDeTintePreset, race As Canon.IRace, isFemale As Boolean)
        If race Is Nothing OrElse layer Is Nothing OrElse layer.Discriminator <> 1US Then Return
        Dim tintGroups = LmCustomTintLoader.Fusionar(race, isFemale, _pluginManager, _ctx.DataPath)
        Dim opt = tintGroups.BuscarOpcion(layer.Index)
        If opt Is Nothing OrElse opt.TemplateColors Is Nothing OrElse opt.TemplateColors.Count = 0 Then Return

        layer.TemplateColorIndex = FaceTintInputBuilder.ResolveTemplateColorIndex(layer.Color, layer.Value / 100.0F, opt, _pluginManager)
    End Sub

    ''' <summary>Normaliza el TemplateColorIndex de CADA Palette layer del preset re-derivándolo desde su
    ''' Color (vía <see cref="ResolveTemplateColorIdToAbsolute"/>), idéntico a lo que Copy/Paste hace en
    ''' BuildPresetFromState. Necesario en el load de LooksMenu: LooksmenuLoader toma el "ColorID" crudo del
    ''' JSON, que NO siempre coincide con el TemplateIndex del RACE — y el resolver del render
    ''' (FaceTintInputBuilder, match por TemplateIndex) entonces no matchea y el layer cae a su color crudo
    ''' (el skin-tone slot-12 -> pálido/blanco). Idempotente (re-deriva del Color, que no toca); no-op en
    ''' layers no-Palette (Discriminator&lt;&gt;1) y si falta race.</summary>
    Private Sub NormalizePresetTintTemplateColorIds(preset As LooksmenuLoader.LooksmenuPreset, race As Canon.IRace, isFemale As Boolean)
        If preset Is Nothing OrElse race Is Nothing OrElse preset.FaceTintLayers Is Nothing Then Return
        For Each tl In preset.FaceTintLayers
            ResolveTemplateColorIdToAbsolute(tl, race, isFemale)
        Next
    End Sub

    ''' <summary>Compute body-edit availability against the currently rendered NPC and update
    ''' the toolbar button accordingly. Disables the button entirely when no section has any
    ''' editable channel; otherwise the form opens with only the applicable sections visible.</summary>
    Private Sub UpdateEditBodyEnabled()
        Dim avail = ComputeBodyEditAvailability(_renderHost.LastRenderedState, _renderHost.LastRenderData)
        Dim shouldEnable = avail.AnythingAvailable
        If InvokeRequired Then
            Invoke(Sub() ButtonEditBody.Enabled = shouldEnable)
        Else
            ButtonEditBody.Enabled = shouldEnable
        End If
    End Sub

    ''' <summary>Gate ButtonEditOutfit on REAL availability: enabled only when the NPC's race+gender
    ''' has at least one compatible outfit (<see cref="HasAnyOutfitCandidate"/>, early-exit + cached).
    ''' Previously this just checked "the load order has any OTFT", which lit the button for every NPC
    ''' even creatures/robots with zero compatible outfits — the picker then opened empty. The check
    ''' is cached per (race, gender) so it costs a one-time scan per race on the render-complete path
    ''' (background continuation), not every render.</summary>
    Private Sub UpdateEditOutfitEnabled()
        Dim st = _renderHost.LastRenderedState
        Dim shouldEnable As Boolean = st IsNot Nothing AndAlso st.RaceFormID <> 0UI AndAlso
                                      HasAnyOutfitCandidate(st.RaceFormID, st.IsFemale)
        If InvokeRequired Then
            Invoke(Sub() ButtonEditOutfit.Enabled = shouldEnable)
        Else
            ButtonEditOutfit.Enabled = shouldEnable
        End If
    End Sub

    ''' <summary>Gate the NPC (Traits) editor button: enabled whenever a real NPC with a FormID is the currently
    ''' rendered subject (LVLN sub-actors and empty selections leave it off). Unlike the outfit/face/body gates
    ''' there is no per-race content requirement — every NPC record has editable Name/flags/factions/etc.</summary>
    Private Sub UpdateEditNpcEnabled()
        Dim st = _renderHost.LastRenderedState
        Dim shouldEnable As Boolean = st IsNot Nothing AndAlso st.RootNpcFormID <> 0UI
        If InvokeRequired Then
            Invoke(Sub() ButtonEditNpc.Enabled = shouldEnable)
        Else
            ButtonEditNpc.Enabled = shouldEnable
        End If
    End Sub

    ''' <summary>Open the multi-tab NPC (Traits) editor over the currently rendered NPC. The editor mutates the
    ''' LIVE in-memory NPC_Data (the render cache instance) only on OK, then we re-render + mark it dirty so the
    ''' preview reflects the edit. See <see cref="NpcEditor_Form"/> for the persistence caveat (ESP write-back is
    ''' a scoped follow-up — the current Save path re-parses the record fresh and would drop these scalar edits).</summary>
    Private Async Sub ButtonEditNpc_Click(sender As Object, e As EventArgs) Handles ButtonEditNpc.Click
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim st = _renderHost.LastRenderedState
        Dim npcFormID = st.RootNpcFormID
        If npcFormID = 0UI Then Return
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then Return

        Using dlg As New NpcEditor_Form(Me, npc, npcFormID, st.RaceFormID, st.IsFemale, AddressOf _ctx.GetParsedNpc)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse Not dlg.HasChanges Then Return
            ' OK with real changes: the live NPC_Data was mutated in place — re-render + mark dirty.
            ' Y con ella, los TRES caches derivados del record: el editor le puede haber cambiado el
            ' nombre visible, y de ese nombre salen la etiqueta del nodo, el texto que busca el
            ' filtro y la CLAVE CON LA QUE SE ORDENA LA LISTA. Sin esto, renombrar "Aaa" a "Zzz"
            ' repinta el árbol con el nodo todavía en la posición de "Aaa". Es el mismo refresco por
            ' FormID que hace el readback del Save.
            RefrescarCachesDerivados(npc)
            ' El editor puede haber cambiado RNAM/head parts/outfit, o sea todo lo que el filtro
            ' avanzado cachea por NPC. El readback del Save ya lo tiraba; este camino no, y ahora que
            ' repuebla consumiria el cache sucio: con `race:ghoul` activo, un NPC que acaba de pasar a
            ' ghoul no aparecería.
            _filterIndex?.InvalidateNpcState()
            ' ⛔ Y SE REPUEBLA EL ARBOL. Refrescar los caches NO alcanza: `FilaDeArbol.Texto` es un
            ' String que `PopulateNPCTree` COPIÓ al construir la fila, no algo que se derive del `Tag`
            ' al dibujar, así que un `Invalidate` redibuja el MISMO texto viejo. Y además el renombre
            ' cambia la clave de orden ⇒ la fila tiene que MOVERSE de lugar, cosa que reescribir el
            ' texto tampoco haría. Este camino no tenía NINGÚN disparador de repoblado: el usuario
            ' editaba el nombre, aceptaba, y el árbol seguía mostrando el viejo.
            PopulateNPCTree(_pendingTreeFilter)
            ' Y se vuelve a enfocar el NPC editado: repoblar lo saca de la vista si su grupo quedo
            ' cerrado, y el usuario perderia de vista justamente lo que acaba de editar.
            TreeViewNPCs.EnfocarClave($"NPC_{npcFormID:X8}")
            Try
                Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
                Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion)
                MarkNpcDirty(npcFormID)
            Catch ex As Exception
                MessageBox.Show($"Failed to render NPC edit: {ex.Message}", "NPC Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>Que puede ofrecer el editor de cuerpo para este NPC, segun el RACE y las shapes de cuerpo
    ''' cargadas. Cada seccion gatea por separado: una raza como Ghoul o PowerArmorRace puede no declarar BSMS
    ''' WeightScale ni RangeModifier en ningun hueso, y entonces esa seccion no tiene efecto en el motor y se
    ''' oculta.
    ''' <para>HasMwgt = hay al menos una entrada WeightScale en un hueso del genero. HasMrsv = idem
    ''' RangeModifier, que es solo Y/Z, asi que su ausencia significa que el MRSV no hace nada para esa raza.
    ''' BodySlideSliders = union de los nombres de morph de los .tri PIRT de todas las shapes de cuerpo, sin los
    ''' nombres reservados de peso; vacio = no hay .tri de cuerpo cargado.</para></summary>
    Private Structure BodyEditAvailability
        Public HasMwgt As Boolean
        Public HasMrsv As Boolean
        ' SSE-only: the vanilla body weight (NAM7 → _0/_1 LERP) is always editable on a Skyrim NPC even when
        ' the FO4 MWGT/MRSV BSMS channels are absent. Lets the Edit Body button enable so the SSE weight
        ' editor is reachable. Always False on FO4 (the FO4 gate is unchanged).
        Public HasSseWeight As Boolean
        ' Height (NAM6 / NAM4) is a plain NPC_ subrecord every actor carries, in BOTH games — unlike the
        ' MWGT/MRSV channels, which depend on the RACE declaring BSMS bones. So the Height section alone is
        ' reason enough to open the editor: without this the field would be unreachable for creatures, robots
        ' and any race without weight-scale bones. Same rationale as HasSseWeight above.
        Public HasHeight As Boolean
        Public BodySlideSliders As List(Of String)
        Public ReadOnly Property AnythingAvailable As Boolean
            Get
                Return HasMwgt OrElse HasMrsv OrElse HasSseWeight OrElse HasHeight OrElse (BodySlideSliders IsNot Nothing AndAlso BodySlideSliders.Count > 0)
            End Get
        End Property
    End Structure

    ''' <summary>Compute body-edit availability for the currently rendered NPC. RACE BSMS flags
    ''' come from the gender-matching BoneData block (or any block, falling back if the gendered
    ''' one is empty). BodySlide sliders come from BodySlideTriResolver enumerating across the
    ''' loaded shapes' PIRT .tri files.</summary>
    Private Function ComputeBodyEditAvailability(state As NPCVisualState, renderData As PreviewResolutionResult) As BodyEditAvailability
        Dim avail As New BodyEditAvailability With {.BodySlideSliders = New List(Of String)}
        If state Is Nothing Then Return avail

        ' RACE BSMS scan — match by gender first, fall back to any block (some races only declare
        ' one block shared between genders).
        Dim raceRec = If(state.RaceFormID <> 0UI, _pluginManager.GetRecord(state.RaceFormID), Nothing)
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            ' Bone Data (BSMP/BMMP/BSMB/BSMS) es exclusivo de Fallout 4 — Skyrim no lo declara en RACE.
            Dim raceFo4 = TryCast(_ctx.ParseRaceCanonCached(raceRec), Canon.RaceFO4)
            If raceFo4 IsNot Nothing Then
                Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
                Dim nb = raceFo4.BoneScaleData
                Dim chosen = nb.FirstOrDefault(Function(bd) bd.BoneWeightScaleDataWeightScaleTargetGender = targetGender)
                If chosen Is Nothing OrElse (chosen.BoneWeightScales.Count = 0 AndAlso chosen.BoneRangeModifiers.Count = 0) Then
                    chosen = nb.FirstOrDefault(Function(bd) bd.BoneWeightScales.Count > 0 OrElse bd.BoneRangeModifiers.Count > 0)
                End If
                If chosen IsNot Nothing Then
                    avail.HasMwgt = chosen.BoneWeightScales.Count > 0
                    avail.HasMrsv = chosen.BoneRangeModifiers.Count > 0
                End If
            End If
        End If

        If renderData IsNot Nothing AndAlso renderData.Shapes IsNot Nothing Then
            avail.BodySlideSliders = BodySlideTriResolver.EnumerateSliderNames(
                renderData.Shapes, renderData.MeshDictKeys)
        End If

        ' SSE: body weight (NAM7) is always an editable channel — surface the editor even for races
        ' with no BSMS MWGT/MRSV and no BodySlide .tri. Gated on the session game (mirror the _isSSE idiom).
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            avail.HasSseWeight = True
        End If

        ' NAM6/NAM4 exist on every NPC_ in both games — nothing to probe, the Height section is always live.
        avail.HasHeight = True

        Return avail
    End Function

    ''' <summary>Open the body editor for the currently selected NPC. Modal — live edits flow
    ''' through the LooksMenu overlay (_appliedPresets) and a granular repaint callback. OK
    ''' commits the live state; Cancel restores the snapshot the form took at open time.
    ''' Sections are pre-gated against RACE BSMS and the loaded body .tri.</summary>
    Private Async Sub ButtonEditBody_Click(sender As Object, e As EventArgs) Handles ButtonEditBody.Click
        If _renderHost.LastRenderedState Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return

        Dim avail = ComputeBodyEditAvailability(_renderHost.LastRenderedState, _renderHost.LastRenderData)
        If Not avail.AnythingAvailable Then
            ' Defensive only, and now unreachable in practice: HasHeight is unconditionally True, so every
            ' NPC has at least the Height section. Kept so a future channel-gating change can't silently
            ' open an editor with nothing in it.
            MessageBox.Show("This NPC has no editable body channels available.",
                            "Edit Body", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If

        ' Capture the NPC's current effective values so the editor can seed its sliders rather
        ' than showing zeros for a freshly-loaded NPC. We pull from state.WeightX (already
        ' resolved through ApplyRaceFallbacks + overlay) and from the post-overlay NPC_Data
        ' for MRSV.
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(_renderHost.LastRenderedState)
        Dim effectiveNpc = ApplyPresetOverlayToNpcData(_ctx.GetParsedNpc(modelNpcFormID), _renderHost.LastRenderedState.RootNpcFormID)
        Dim initial As New EditBody_Form.InitialValues With {
            .Thin = _renderHost.LastRenderedState.WeightThin,
            .Muscular = _renderHost.LastRenderedState.WeightMuscular,
            .Fat = _renderHost.LastRenderedState.WeightFat
        }
        If effectiveNpc IsNot Nothing Then
            Dim regiones = effectiveNpc.Record.ValoresDeRegionCorporal()
            For i = 0 To Math.Min(4, regiones.Count - 1)
                initial.Mrsv(i) = regiones(i)
            Next
        End If
        ' SSE body weight (NAM7). Read the overlay-applied effective weight so the editor's SSE weight
        ' slider opens at the NPC's current value (default 100 when unset / not SSE). Harmless on FO4
        ' (NAM7 Unused there; the editor's SSE section is never built).
        If effectiveNpc IsNot Nothing AndAlso effectiveNpc.Record.TienePesoDeSkyrim() Then
            initial.SseWeight = effectiveNpc.Record.PesoDeSkyrim()
        End If
        ' Height (NAM6 / NAM4). Seed from the effective TRAITS source — the same resolution the record
        ' tree uses — so an inheriting NPC opens at the height it actually shows instead of its own empty
        ' slot. Absent subrecord ⇒ 1.0 (engine default). Then let an already-authored override win, so
        ' reopening the editor shows the pending edit instead of re-seeding the stale record value.
        Dim rootFidForHeight = _renderHost.LastRenderedState.RootNpcFormID
        Dim rootNpcForHeight = _ctx.GetParsedNpc(rootFidForHeight)
        If rootNpcForHeight IsNot Nothing Then
            Dim traitsSrc = ResolveSectionSource(rootNpcForHeight, NPC_TemplateCategory.Traits)
            If traitsSrc IsNot Nothing Then
                If traitsSrc.Record.TieneAltura() Then
                    initial.HeightMin = traitsSrc.Record.Altura()
                    initial.HasHeightMin = True
                End If
                If traitsSrc.Record.TieneAlturaMaxima() Then
                    initial.HeightMax = traitsSrc.Record.AlturaMaxima()
                    initial.HasHeightMax = True
                End If
            End If
        End If
        Dim ovForHeight = TryGetNpcRecordOverride(rootFidForHeight)
        If ovForHeight IsNot Nothing Then
            ' An authored override makes the subrecord present-to-be, so it counts as "carried" for the
            ' editor's cross-clamp / write gating.
            If ovForHeight.HeightMin.HasValue Then
                initial.HeightMin = ovForHeight.HeightMin.Value
                initial.HasHeightMin = True
            End If
            If ovForHeight.HeightMax.HasValue Then
                initial.HeightMax = ovForHeight.HeightMax.Value
                initial.HasHeightMax = True
            End If
        End If

        ' BodySlide sliders that the overlay (or a previously loaded preset) already carries —
        ' open at those values; otherwise zero. There is no record-level source for these.
        Dim existingPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(_renderHost.LastRenderedState.RootNpcFormID, existingPreset) AndAlso existingPreset IsNot Nothing Then
            For Each kv In existingPreset.BodyMorphSliders
                initial.BodySlide(kv.Key) = kv.Value
            Next
        End If

        Dim mainGore As Boolean = CheckBoxRenderGore.Checked
        Using dlg As New EditBody_Form(_renderHost.LastRenderedState.RootNpcFormID,
                                       _appliedPresets,
                                       avail.HasMwgt, avail.HasMrsv,
                                       avail.BodySlideSliders,
                                       initial,
                                       Me,
                                       mainGore,
                                       _renderHost.LastRenderedState.RaceFormID,
                                       _renderHost.LastRenderedState.IsFemale,
                                       _renderHost.LastRenderedState.SkinFormID)
            dlg.ShowDialog(Me)
            ' Phase D: reload MainForm preview only when the user committed via OK. Cancel
            ' already rolled back the overlay; without an explicit MainForm render during the
            ' modal session, our preview is still in the pre-edit state — no reload needed.
            If dlg.DialogResult = DialogResult.OK AndAlso dlg.HasUncommittedChanges Then
                MarkNpcDirty(_renderHost.LastRenderedState.RootNpcFormID)
                Try
                    Await RenderInHostAsync(_renderHost, _renderHost.LastRenderedState.RootNpcFormID)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[EDITOR] post-edit MainForm re-render failed: {ex.GetType().Name}: {ex.Message}")
                End Try
            End If
        End Using
    End Sub

    ' ⛔ NOTA HISTORICA, no la doc del handler de abajo. El boton "ARMA Editor" se RETIRO (lo dice
    ' el propio texto): el miembro que esto describia no existe.
    ' Open the standalone ARMA Editor (Armor Addon authoring). The editor mutates the MainForm
    ' ARMA/MSWP draft lists only; the existing Save ESP flow persists them. Passes the currently-rendered
    ' NPC as the preview context (or 0 = no preview) plus its race/gender so a new ARMA pre-fills the right
    ' race and the WYSIWYG preview equips on the right body. After close, the drafts are already registered;
    ' the OutfitPicker's item list reads ARMO drafts LIVE so nothing to rebuild here.

    ' Open the Edit Outfit picker (NPC.DOFT override) for the current NPC. The picker renders
    ' its WYSIWYG preview through the SAME pipeline as this viewer (<see cref="PreviewOutfitInHostAsync"/>)
    ' but into ITS OWN host with a host-scoped override — it never touches the shared overlay
    ' (_appliedPresets) nor the main preview, so cancel is inherently non-destructive (nothing to undo).
    ' On OK the chosen value is committed to the overlay and the MAIN preview reloads. Picker return:
    '   Nothing → "(record default)" (clear override, preserve raw NPC.DOFT)
    '   Some(0) → "(no outfit)"
    '   Some(fid) → OTFT / draft override.
    Private Async Sub ButtonEditOutfit_Click(sender As Object, e As EventArgs) Handles ButtonEditOutfit.Click
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim st = _renderHost.LastRenderedState
        Dim npcFormID = st.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then Return

        ' Raw record DOFT drives the "(record default)" pinned entry → Nothing semantic.
        Dim modelFormID = If(st.ModelSourceFormID <> 0UI, st.ModelSourceFormID, npcFormID)
        Dim rawNpc = _ctx.GetParsedNpc(modelFormID)
        Dim rawOutfit As UInteger = If(rawNpc IsNot Nothing, rawNpc.Record.DefaultOutfit, 0UI)

        Dim raceRec = If(st.RaceFormID <> 0UI, _pluginManager.GetRecord(st.RaceFormID), Nothing)
        Dim raceEditorID = If(raceRec IsNot Nothing, raceRec.EditorID, "?")

        Using dlg As New OutfitPicker_Form(Me, npcFormID, _appliedPresets, st.RaceFormID, raceEditorID, st.IsFemale, st.DefaultOutfitFormID, rawOutfit)
            ' Cancel: the picker rendered only into its own host; the main preview + overlay were never
            ' touched, so there is nothing to undo.
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            ' OK: reload the MAIN preview to reflect whatever changed in the picker, then mark the NPC
            ' dirty ONLY when its effective outfit actually changed.
            Dim result As UInteger? = dlg.SelectedOutfitOverride
            Dim previousOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
            Dim hadOverlay = _appliedPresets.TryGetValue(npcFormID, previousOverlay) AndAlso previousOverlay IsNot Nothing
            Dim priorOutfitOverride As UInteger? = If(hadOverlay, previousOverlay.DefaultOutfitFormIDOverride, Nothing)

            ' The NPC_ record only stores the DOFT FormID, so "did the NPC change?" = "did the effective outfit
            ' change?". Effective outfit = override value when set, else the raw record DOFT — so Nothing↔Some(
            ' rawOutfit) counts as no change. The picker's "Edit armor…" authors ARMA/ARMO DRAFTS without changing
            ' which outfit is worn; those drafts persist via their own IsDirty, so an unchanged pick must NOT mark
            ' the NPC dirty (it would write a no-op NPC_ override on Save) — but it DOES still need a re-render,
            ' because the drafts are read live and a draft edit changes how the SAME outfit looks. So the dirty
            ' mark is gated on outfitChanged while the re-render below is unconditional.
            Dim effectiveBefore As UInteger = If(priorOutfitOverride.HasValue, priorOutfitOverride.Value, rawOutfit)
            Dim effectiveAfter As UInteger = If(result.HasValue, result.Value, rawOutfit)
            Dim outfitChanged As Boolean = (effectiveBefore <> effectiveAfter)

            ' Commit the outfit override to the overlay only when it actually changed, so an unchanged pick
            ' doesn't leave a spurious empty overlay behind (which would flag the NPC as changed via
            ' _appliedPresets.ContainsKey). When unchanged, any pre-existing overlay is left exactly as-is and the
            ' re-render below re-resolves its (possibly edited) draft outfit live.
            Dim p As LooksmenuLoader.LooksmenuPreset = Nothing
            If outfitChanged Then
                If hadOverlay Then
                    p = previousOverlay
                Else
                    p = New LooksmenuLoader.LooksmenuPreset()
                    _appliedPresets(npcFormID) = p
                End If
                p.DefaultOutfitFormIDOverride = result
            End If

            Try
                Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
                Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion)
                If outfitChanged Then MarkNpcDirty(npcFormID)
            Catch ex As Exception
                ' Revert just the outfit field; don't clobber other overlay edits. Only meaningful when we
                ' committed a change above (p is Nothing on the unchanged path).
                If p IsNot Nothing Then
                    p.DefaultOutfitFormIDOverride = priorOutfitOverride
                    If Not hadOverlay Then _appliedPresets.Remove(npcFormID)
                End If
                MessageBox.Show($"Failed to render outfit: {ex.Message}{vbCrLf}Outfit reverted.",
                                "Edit Outfit", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ' (The standalone "ARMA Editor" / "ARMO Editor" toolbar buttons were removed — armor authoring is now reached
    ' from the Edit Outfit dialog's "Edit armor" button, and ARMA via the ARMO editor's addon "Edit ARMA…".)

    ' =====================================================================
    ' Edit Face — toolbar enable + dialog launch
    ' =====================================================================


    ''' <summary>Gate de ButtonEditFace: EXACTAMENTE el mismo que impide bakear, en los dos juegos —
    ''' <see cref="RaceUtil.RaceSupportsFaceGen"/> (RACE.DATA bit 0x2). Si la raza bakea, se edita; si no,
    ''' no. Una sola regla, sin rama por juego y sin condiciones extra: cualquier gate MÁS estricto que el
    ''' del bake bloquea ediciones legítimas, y cualquiera MÁS laxo deja producir datos que el motor
    ''' ignora.</summary>
    Private Sub UpdateEditFaceEnabled()
        ' UNA sola regla, SIN rama por juego: si la raza bakea, se puede editar. Es el MISMO gate que usan el
        ' bake y la recolección de head parts (RACE.DATA bit 0x2), y la fuente del FormID también coincide
        ' porque las dos pasan por la raza EFECTIVA del editor.
        ' NO volver a los dos gates viejos: uno abría el editor sobre razas sin facegen (y como OnOk marca
        ' dirty, esos datos que el motor ignora acababan en el ESP); el otro exigía además que la RACE
        ' tuviera contenido autorado, y era MÁS estricto que el bake — el editor deja añadir head parts de
        ' todo el load order, así que "la raza no autora nada" no implica "no hay nada que editar".
        Dim shouldEnable As Boolean = False
        If _renderHost.LastRenderedState IsNot Nothing AndAlso _renderHost.LastRenderData IsNot Nothing Then
            shouldEnable = RaceUtil.RaceSupportsFaceGen(_renderHost.LastRenderedState.RaceFormID, _pluginManager)
        End If
        ' Build CharGen also enables for a multi-selection: the batch resolves + skips per NPC, so it
        ' must NOT be gated on the single rendered NPC's face-edit availability (the random pick could
        ' be an NPC with no facegen while others in the set have it). Edit Face stays single-NPC.
        Dim multiSel = _selectedNpcFormIDs.Count >= 2
        If InvokeRequired Then
            Invoke(Sub()
                       ButtonEditFace.Enabled = shouldEnable
                       ButtonBuildCharGen.Enabled = shouldEnable OrElse multiSel
                   End Sub)
        Else
            ButtonEditFace.Enabled = shouldEnable
            ButtonBuildCharGen.Enabled = shouldEnable OrElse multiSel
        End If
    End Sub

    Private Async Sub ButtonEditFace_Click(sender As Object, e As EventArgs) Handles ButtonEditFace.Click
        ' Game-gated inside EditFace_Form (_isSSE): SSE drives the NAM9/NAMA + sculpt + tint + .jslot path,
        ' FO4 the LooksMenu path. No blocking gate — the editor is game-aware.
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim raceFormID = _renderHost.LastRenderedState.RaceFormID
        If raceFormID = 0UI Then
            MessageBox.Show(Me, "This NPC has no resolved RACE — Edit Face needs the RACE record to populate" &
                            " palette / morph / region pickers.", "Edit Face", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return
        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        If race Is Nothing Then Return

        ' Capture the raw NPC's AcbsFlags so the Edit Face form can compute the original bit and
        ' the form's Cancel rollback can restore it (the overlay only stores Boolean? — the raw
        ' value lives on the NPC record).
        Dim modelNpcFormID = If(_renderHost.LastRenderedState.ModelSourceFormID <> 0UI, _renderHost.LastRenderedState.ModelSourceFormID, _renderHost.LastRenderedState.FormID)
        Dim rawNpc = _ctx.GetParsedNpc(modelNpcFormID)
        Dim rawAcbsFlags As UInteger = If(rawNpc IsNot Nothing, rawNpc.Record.ConfigurationFlags, 0UI)

        Dim formatRef As Func(Of UInteger, String) = AddressOf DescribeFormID
        Dim mainGore As Boolean = CheckBoxRenderGore.Checked

        Using dlg As New EditFace_Form(_renderHost.LastRenderedState.RootNpcFormID,
                                       _appliedPresets,
                                       _pluginManager,
                                       race,
                                       raceFormID,
                                       _renderHost.LastRenderedState.IsFemale,
                                       formatRef,
                                       rawAcbsFlags,
                                       Me,
                                       mainGore)
            dlg.ShowDialog(Me)
            ' Phase D: reload MainForm preview only when the user committed via OK. Cancel
            ' already rolled back the overlay; the MainForm preview was untouched during the
            ' modal so it's already correct.
            If dlg.DialogResult = DialogResult.OK AndAlso dlg.HasUncommittedChanges Then
                MarkNpcDirty(_renderHost.LastRenderedState.RootNpcFormID)
                Try
                    Await RenderInHostAsync(_renderHost, _renderHost.LastRenderedState.RootNpcFormID)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[EDITOR] post-edit MainForm re-render failed: {ex.GetType().Name}: {ex.Message}")
                End Try
            End If
        End Using

        UpdatePasteLookEnabled()
    End Sub

    ''' <summary>Abre el diálogo CharGen Options (tamaño de textura por canal + formato del diffuse,
    ''' persistido en Config_App). El bake lee esos settings via FaceGenBuilder.OutputSettings.</summary>
    Private Sub ButtonCharGenOptions_Click(sender As Object, e As EventArgs) Handles ButtonCharGenOptions.Click
        Using f As New CharGenOptionsForm()
            If f.ShowDialog(Me) = DialogResult.OK Then
                ' Las convenciones FaceTint (tab "FaceTint Conventions") afectan el composite del render
                ' EN VIVO, no sólo el bake. Re-render el NPC actual para reflejarlo.
                ' RenderFromCurrentSelection re-corre la pipeline facetint completa.
                RenderFromCurrentSelection()
            End If
        End Using
    End Sub

    Private Async Sub ButtonBuildCharGen_Click(sender As Object, e As EventArgs) Handles ButtonBuildCharGen.Click
        ' Game-aware: geometry morph is game-aware (FaceGenBuildPipeline); SSE texture/facetint is handled by
        ' the SSE bake path. No blocking gate.
        ' Multi-selection → batch build with a determinate progress dialog. Otherwise the single
        ' currently-rendered NPC, reported via a blocking message box (unchanged behaviour).
        If _selectedNpcFormIDs.Count >= 2 Then
            Await BuildCharGenForSelectionAsync(_selectedNpcFormIDs.ToList())
            Return
        End If
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim modelNpcFormID = If(_renderHost.LastRenderedState.ModelSourceFormID <> 0UI,
                                _renderHost.LastRenderedState.ModelSourceFormID,
                                _renderHost.LastRenderedState.FormID)
        Await BuildCharGenSingle(modelNpcFormID)
    End Sub

    ''' <summary>Build CharGen (loose) for a single NPC, reporting via a blocking message box.</summary>
    Private Async Function BuildCharGenSingle(npcFormID As UInteger) As Task
        If npcFormID = 0UI Then Return
        Dim result As FaceGenBuilder.BuildResult
        Try
            ' willBePacked:=False — loose output stays standalone (no BA2 repack/rename), so the NIF
            ' must embed the actual on-disk texture suffix (carries _2 in DebugMode). See BuildCharGen.
            ' WriteGPUSandboxOutput corre el GL (para el _2b) -> sync en el hilo UI (contexto GL),
            ' INDEPENDIENTE de DebugMode. Sin ese flag (output CPU-only, sin GL) -> bake en thread de fondo.
            Dim fidL = npcFormID
            If FaceGenBuilder.WriteGPUSandboxOutput Then
                result = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
            Else
                result = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[BUILDCHARGEN] EXCEPTION {ex.GetType().Name}: {ex.Message}{vbCrLf}{ex.StackTrace}")
            result = New FaceGenBuilder.BuildResult With {.Success = False, .Summary = $"Build CharGen failed: {ex.GetType().Name}: {ex.Message}"}
        End Try

        Dim icon As MessageBoxIcon
        Dim message As String
        If result.Skipped Then
            icon = MessageBoxIcon.Information
            message = result.Summary   ' "No FaceGen head parts for this NPC — skipped."
        ElseIf result.Success AndAlso result.TextureSlotsFailed > 0 Then
            ' Success=True NO significa "salio bien": el NIF se escribio, pero un slot de TEXTURA pudo
            ' fallar y eso viaja aparte, en TextureSlotsFailed. Decir "Generated OK" con texturas caidas es
            ' el mismo agujero de observabilidad que ya documenta BakeAllRunner (un barrido reporto
            ' "4460 baked / 0 failed" habiendo escrito CERO facetint). El camino de Save ya lo mira; este no.
            icon = MessageBoxIcon.Warning
            message = $"Generated, BUT {result.TextureSlotsFailed} face texture(s) FAILED:" & vbCrLf & vbCrLf &
                      result.TextureFailureDetail & vbCrLf & vbCrLf &
                      "The NIF was written; those texture slot(s) were NOT."
        ElseIf result.Success Then
            icon = MessageBoxIcon.Information
            message = "Generated OK"
        Else
            icon = MessageBoxIcon.Error
            message = $"Error: {result.Summary}"
        End If
        MessageBox.Show(Me, message, "Build CharGen", MessageBoxButtons.OK, icon)
    End Function

    ''' <summary>Build CharGen (loose) for many NPCs with a determinate, MODAL progress dialog. The
    ''' GL-bound bake (FaceGenBuilder.BuildCharGen) MUST run on the UI thread (it owns the OpenGL
    ''' context), so it runs synchronously per NPC; <c>Await Task.Yield()</c> between NPCs returns to
    ''' the dialog's modal message loop so the bar repaints and Cancel stays responsive — the same
    ''' GL-sync / rest-async split the Save pipeline uses. The loop runs inside the dialog's Shown
    ''' event (ShowDialog), so modality blocks the main window automatically (no manual Enable toggle,
    ''' which left the owned non-modal dialog unable to repaint its Cancel button). No BA2 pack.</summary>
    Private Function BuildCharGenForSelectionAsync(formIDs As List(Of UInteger)) As Task
        If formIDs Is Nothing OrElse formIDs.Count = 0 Then Return Task.CompletedTask
        Dim total = formIDs.Count
        Dim ok As Integer = 0
        Dim skipped As Integer = 0
        Dim failed As New List(Of String)
        Dim texWarn As New List(Of String)   ' NIF escrito pero algun slot de TEXTURA fallo (Success sigue True)

        Using prog As New BuildProgress_Form()
            prog.Text = $"Build CharGen (loose) — {total} NPCs"
            prog.WorkAsync =
                Async Function(p As BuildProgress_Form) As Task
                    ' Let the dialog fully paint (Cancel button included) before the first blocking
                    ' bake. Task.Delay (NOT Task.Yield) is deliberate: a Yield continuation outranks
                    ' WM_PAINT, so the form never repaints between the synchronous GL bakes (the
                    ' "white Cancel button / frozen UI" symptom). A 1 ms delay gives the message loop
                    ' idle time to process WM_PAINT + the Cancel click before resuming.
                    Await Task.Delay(1)
                    ' Cache de decode a nivel BATCH: las texturas source (face d/_n/_s) + tint + swap se
                    ' repiten entre clones -> decode UNA vez por DDS en todo el batch (no por clon). Se limpia
                    ' en el Finally (incluso si se cancela). Equivalente CPU del TintGpuCache persistente del GL.
                    ' Techo del cache de decode: se resuelve al ABRIR el lote (ver
                    ' FaceTintCpuCompositor.BeginBatchDecodeCacheConMotivo). Antes este camino —el batch
                    ' de la GUI, por donde pasa casi todo el mundo— abria el lote sin leer
                    ' FGBAKE_DECODE_CACHE_MB, asi que la variable con la que un usuario acota la memoria
                    ' no la miraba nadie.
                    ' Y SE DICE CUAL APLICO. Descartar el motivo dejaba al usuario de la GUI —que es por
                    ' donde pasa casi todo el mundo— sin forma de saber si su FGBAKE_DECODE_CACHE_MB se leyó
                    ' o si corrió sin techo. Un techo que no se ve no se puede diagnosticar.
                    ' LA LLAMADA VA AFUERA DEL LogLazy. `LogLazy` NO evalúa el lambda si el logger está
                    ' apagado ⇒ meterla adentro dejaba el lote SIN ABRIR en el caso normal. Nunca poner un
                    ' efecto adentro de un log perezoso.
                    ' EL Begin VA PEGADO AL Try. Dejar el LogLazy en el medio es el mismo defecto que
                    ' BakeAllRunner documenta y arregla: si el log tira, el Finally con EndBatchDecodeCache
                    ' no corre y los DOS niveles del cache (DecodedTex + Single() de 4K) quedan retenidos
                    ' toda la sesion de la GUI.
                    Dim motivoCache = FaceTintCpuCompositor.BeginBatchDecodeCacheConMotivo()
                    Try
                        Logger.LogLazy(Function() $"[CHARGEN] decode cache: {motivoCache}")
                        For i = 0 To total - 1
                            If p.Cancelled Then Exit For
                            Dim fid = formIDs(i)
                            Dim npc As NPC_Data = Nothing
                            Dim name = If(_ctx.NpcCache.TryGetValue(fid, npc) AndAlso npc IsNot Nothing, npc.ToString(), fid.ToString("X8"))
                            p.SetProgress(i, total, $"Building {i + 1}/{total}: {name}")
                            Await Task.Delay(1)   ' idle window so the bar/button repaint + Cancel processes
                            If p.Cancelled Then Exit For
                            Try
                                ' WriteGPUSandboxOutput corre el GL (para el _2b) -> sync en el hilo UI (contexto
                                ' GL), INDEPENDIENTE de DebugMode. Sin ese flag (output CPU-only, sin GL) -> bake
                                ' en thread de fondo (Await Task.Run): la UI repinta y Cancel responde DURANTE el
                                ' bake. Secuencial (await uno a la vez) -> sin race.
                                Dim fidL = fid
                                Dim r As FaceGenBuilder.BuildResult
                                If FaceGenBuilder.WriteGPUSandboxOutput Then
                                    r = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
                                Else
                                    r = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
                                End If
                                If r.Skipped Then
                                    skipped += 1
                                ElseIf r.Success Then
                                    ok += 1
                                    ' Success=True con texturas caidas NO es exito: el NIF salio, el slot de
                                    ' textura no. Va a la lista de avisos (no a `failed`, que cuenta NPCs sin
                                    ' NIF) para que el resumen no diga "Built N/N" tapando el fallo.
                                    If r.TextureSlotsFailed > 0 Then
                                        texWarn.Add($"{name}: {r.TextureSlotsFailed} texture(s) FAILED — {r.TextureFailureDetail}")
                                    End If
                                Else
                                    failed.Add($"{name}: {r.Summary}")
                                End If
                            Catch ex As Exception
                                Logger.LogLazy(Function() $"[BUILDCHARGEN-BATCH] EXCEPTION 0x{fid:X8} {ex.GetType().Name}: {ex.Message}")
                                failed.Add($"{name}: {ex.Message}")
                            End Try
                        Next
                    Finally
                        FaceTintCpuCompositor.EndBatchDecodeCache()
                    End Try
                    p.SetProgress(total, total, "Done.")
                End Function
            prog.ShowDialog(Me)   ' modal: runs WorkAsync on Shown, closes itself when the loop ends
        End Using

        Dim doneCount = ok + skipped + failed.Count
        Dim summary = $"Built CharGen (loose) for {ok}/{total} NPC(s)."
        If skipped > 0 Then summary &= $"{vbCrLf}Skipped {skipped} (no FaceGen head parts)."
        If doneCount < total Then summary &= $"{vbCrLf}Cancelled — {total - doneCount} not processed."
        If failed.Count > 0 Then
            Dim shown = failed.Take(15).ToList()
            summary &= $"{vbCrLf}{vbCrLf}Failed ({failed.Count}):{vbCrLf}" & String.Join(vbCrLf, shown)
            If failed.Count > shown.Count Then summary &= $"{vbCrLf}… (+{failed.Count - shown.Count} more)"
        End If
        If texWarn.Count > 0 Then
            Dim shownT = texWarn.Take(15).ToList()
            summary &= $"{vbCrLf}{vbCrLf}⚠ {texWarn.Count} NPC(s) got their NIF but FAILED a face texture:{vbCrLf}" & String.Join(vbCrLf, shownT)
            If texWarn.Count > shownT.Count Then summary &= $"{vbCrLf}… (+{texWarn.Count - shownT.Count} more)"
        End If
        MessageBox.Show(Me, summary, "Build CharGen", MessageBoxButtons.OK,
                        If(failed.Count = 0 AndAlso texWarn.Count = 0, MessageBoxIcon.Information, MessageBoxIcon.Warning))
        Return Task.CompletedTask
    End Function

    ''' <summary>Re-trigger the on-demand NPC load for the currently-rendered NPC. Same path
    ''' LooksMenu Paste uses; reconstructs every render-side cache (geometry, materials,
    ''' tints) from records + overlay. Used when geometry-affecting edits happen, or as a
    ''' fallback when an in-place refresh can't guarantee correctness.</summary>
    Private Sub ReloadCurrentNpcFull()
        If _renderHost.LastRenderedState Is Nothing Then
            Return
        End If
        Dim modelFormID = If(_renderHost.LastRenderedState.ModelSourceFormID <> 0UI, _renderHost.LastRenderedState.ModelSourceFormID, _renderHost.LastRenderedState.FormID)
        Dim raw = _ctx.GetParsedNpc(modelFormID)
        If raw Is Nothing Then
            Return
        End If
        Dim version = Threading.Interlocked.Increment(_previewRequestVersion)
        ' Fire-and-forget but with explicit exception trap. A silently-swallowed exception in
        ' the async chain is the most likely culprit when the main render goes black after a
        ' HeadParts edit (the renderer ran Model.Clean and then never finished Setup_GL because
        ' something downstream threw). Logging here surfaces the cause.
        Dim t = LoadNPCOnDemandAsyncFromExisting(raw, version)
        t.ContinueWith(Sub(failedTask)
                           Try
                               Dim ex = failedTask.Exception
                               Dim st = ex?.GetBaseException()?.StackTrace
                               Logger.LogLazy(Function() $"[RELOAD] faulted: {ex?.GetBaseException()?.GetType().Name}: {ex?.GetBaseException()?.Message}{Environment.NewLine}{st}")
                           Catch
                           End Try
                       End Sub, Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted)
    End Sub

    Private Sub ButtonCopyLook_Click(sender As Object, e As EventArgs) Handles ButtonCopyLook.Click
        If _renderHost.CurrentBaseState Is Nothing Then Return
        Dim built = BuildPresetFromState(_renderHost.CurrentBaseState)
        If built Is Nothing Then
            MessageBox.Show("Could not capture the current NPC state.", "Copy Look",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        _clipboardPreset = built
        _clipboardSourceRaceFormID = _renderHost.CurrentBaseState.RaceFormID
        UpdatePasteLookEnabled()

        Dim consoleCmd = $"player.placeatme {_renderHost.CurrentBaseState.RootNpcFormID:X8} 1"
        Try
            Clipboard.SetText(consoleCmd)
        Catch ex As Exception
        End Try

    End Sub

    Private Async Sub ButtonPasteLook_Click(sender As Object, e As EventArgs) Handles ButtonPasteLook.Click
        ' Double-check — the button should already be disabled when this isn't true (the enable
        ' state is recomputed on every NPC selection and on Copy), but the click handler must
        ' refuse anyway in case anything bypasses the gating.
        If Not IsClipboardCompatibleWithCurrentNpc() Then Return

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            MessageBox.Show("Could not find NPC record in cache.", "Paste Look",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Ask the user which categories of the clipboard preset to actually apply. Cancel = no-op,
        ' nothing changes on the target NPC. The dialog defaults all checkboxes to True so the
        ' "select OK without thinking" path matches the legacy "paste everything" behavior; the copied
        ' preset is handed in so each row can show how much it actually carries.
        Dim isSseGame = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim options As PresetCategories.PresetCategoryOptions
        Using dlg As New PasteOptionsDialog(isSseGame, _clipboardPreset)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            options = dlg.BuildOptions()
        End Using

        ' The target's live overlay serves twice: as the preserve BASELINE for the unticked categories
        ' (what the NPC shows RIGHT NOW, editor work included, falling back to the raw record where the
        ' overlay has nothing), and as the rollback value if the render throws.
        Dim previousOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
        _appliedPresets.TryGetValue(npcFormID, previousOverlay)
        Dim filtered = PresetCategoryFilter.BuildFiltered(_clipboardPreset, npc, previousOverlay, options, isSseGame,
                                                          ResolveHdptForOrphanCascade(), AddressOf ResolveLmSkinTemplate)
        _appliedPresets(npcFormID) = filtered

        Try
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion)
            MarkNpcDirty(npcFormID)
        Catch ex As Exception
            If previousOverlay Is Nothing Then
                _appliedPresets.Remove(npcFormID)
            Else
                _appliedPresets(npcFormID) = previousOverlay
            End If
            MessageBox.Show($"Failed to render pasted look: {ex.Message}{vbCrLf}Overlay reverted.",
                            "Paste Look", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub

    ''' <summary>Capture the current rendered state into a LooksMenu preset and save it to a JSON
    ''' file. Default location is Data\F4SE\Plugins\F4EE\Presets\ — the same folder
    ''' Load LooksMenu reads from. Default filename is the NPC's EditorID.</summary>
    Private Sub ButtonSaveLooksmenu_Click(sender As Object, e As EventArgs) Handles ButtonSaveLooksmenu.Click
        ' LooksMenu presets are the FO4 f4ee format (F4SE\Plugins\F4EE). SSE writes a RaceMenu (.jslot):
        ' capture the same preset the FO4 path does (BuildPresetFromState now carries the SSE fields), map
        ' it to a jslot via RaceMenuPresetMapper.ToJslot, and write it under Data\SKSE\Plugins\CharGen\Presets.
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            SaveRaceMenuPresetForSse()
            Return
        End If
        If _renderHost.CurrentBaseState Is Nothing Then Return

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            MessageBox.Show("Could not find NPC record in cache.", "Save LooksMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim preset = BuildPresetFromState(_renderHost.CurrentBaseState)
        If preset Is Nothing Then
            MessageBox.Show("Could not capture the current NPC state.", "Save LooksMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim defaultDir = IO.Path.Combine(_dataPath, "F4SE", "Plugins", "F4EE", "Presets")
        Try
            If Not IO.Directory.Exists(defaultDir) Then IO.Directory.CreateDirectory(defaultDir)
        Catch
            ' Fall back to the data root if we can't create the default folder.
            defaultDir = _dataPath
        End Try

        Dim defaultName = If(String.IsNullOrEmpty(npc.EditorID), $"NPC_{npc.FormID:X8}", npc.EditorID) & ".json"

        Using dlg As New SaveFileDialog()
            dlg.Title = "Save LooksMenu Preset"
            dlg.Filter = "LooksMenu preset (*.json)|*.json"
            dlg.InitialDirectory = defaultDir
            dlg.FileName = defaultName
            dlg.OverwritePrompt = True
            dlg.AddExtension = True
            dlg.DefaultExt = "json"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Try
                ' Los campos que no se pudieron nombrar quedan FUERA del preset a propósito (preservar es más
                ' seguro que borrar), pero eso tiene que decirse: si no, el usuario guarda creyendo que el
                ' outfit/piel viajó. El caso típico es un record recién creado que todavía no se guardó al ESP.
                Dim omitted As List(Of String) = Nothing
                Dim json = LooksmenuLoader.SerializePreset(preset, _pluginManager, omitted)
                IO.File.WriteAllText(dlg.FileName, json, New System.Text.UTF8Encoding(False))
                If omitted IsNot Nothing AndAlso omitted.Count > 0 Then
                    MessageBox.Show(
                        "The preset was saved, but these fields could NOT be included because the record they " &
                        "point at does not belong to any loaded plugin yet:" & vbCrLf & vbCrLf &
                        "  • " & String.Join(vbCrLf & "  • ", omitted) & vbCrLf & vbCrLf &
                        "Save the plugin (Save ESP) first and then re-save the preset if you want them in it. " &
                        "They were left out rather than written as ""none"", so loading this preset will keep " &
                        "whatever the target NPC already has instead of clearing it.",
                        "Save LooksMenu", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                End If
            Catch ex As Exception
                MessageBox.Show($"Failed to write preset: {ex.Message}", "Save LooksMenu",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>SSE counterpart of <see cref="ButtonSaveLooksmenu_Click"/>: capture the current rendered
    ''' state into a preset (the FO4 <see cref="BuildPresetFromState"/> now carries SSE fields), map it to a
    ''' RaceMenu <c>.jslot</c> via <see cref="RaceMenuPresetMapper.ToJslot"/>, and write it to
    ''' Data\SKSE\Plugins\CharGen\Presets (skee64 PapyrusCharGen.cpp). Default filename is the NPC EditorID.</summary>
    Private Sub SaveRaceMenuPresetForSse()
        If _renderHost.CurrentBaseState Is Nothing Then Return

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            MessageBox.Show("Could not find NPC record in cache.", "Save RaceMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        Dim preset = BuildPresetFromState(_renderHost.CurrentBaseState)
        If preset Is Nothing Then
            MessageBox.Show("Could not capture the current NPC state.", "Save RaceMenu",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        ' race + gender → translate each record TINI back to the .jslot POSITIONAL index (inverse of load).
        Dim jRaceFid As UInteger = If(_renderHost.CurrentBaseState IsNot Nothing, _renderHost.CurrentBaseState.RaceFormID, 0UI)
        Dim jFemale As Boolean = _renderHost.CurrentBaseState IsNot Nothing AndAlso _renderHost.CurrentBaseState.IsFemale
        Dim j = RaceMenuPresetMapper.ToJslot(preset, _pluginManager, jRaceFid, jFemale)

        ' Same _dataPath Data-root the FO4 Save uses; RaceMenu presets live under SKSE\Plugins\CharGen\Presets.
        Dim defaultDir = IO.Path.Combine(_dataPath, "SKSE", "Plugins", "CharGen", "Presets")
        Try
            If Not IO.Directory.Exists(defaultDir) Then IO.Directory.CreateDirectory(defaultDir)
        Catch
            ' Fall back to the data root if we can't create the default folder.
            defaultDir = _dataPath
        End Try

        Dim defaultName = If(String.IsNullOrEmpty(npc.EditorID), $"NPC_{npc.FormID:X8}", npc.EditorID) & ".jslot"

        Using dlg As New SaveFileDialog()
            dlg.Title = "Save RaceMenu Preset"
            dlg.Filter = "RaceMenu preset (*.jslot)|*.jslot"
            dlg.InitialDirectory = defaultDir
            dlg.FileName = defaultName
            dlg.OverwritePrompt = True
            dlg.AddExtension = True
            dlg.DefaultExt = "jslot"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            Try
                IO.File.WriteAllBytes(dlg.FileName, j.Save())
            Catch ex As Exception
                MessageBox.Show($"Failed to write preset: {ex.Message}", "Save RaceMenu",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>Save the current NPC override into an auto-generated plugin (.esp/.esm).
    ''' Workflow:
    '''   1. Scan Data\ for plugins whose TES4.CNAM matches NPC_MANAGER_AUTHOR_CNAM.
    '''   2. Show SaveEsp_Form: pick existing plugin to update, or new with auto-suffix name.
    '''   3. Build NpcOverrideEntry from the current NPC's full type-safe parse.
    '''   4. Pass to SaveNpcEspWriter.SaveOverridePlugin which handles MAST cleanup
    '''      (drop unused masters, re-map FormIDs to new MAST list).
    ''' Light master flag default is taken from the source NPC's master plugin (IsESM).</summary>
    Private Async Sub ButtonSavePlugin_Click(sender As Object, e As EventArgs) Handles ButtonSavePlugin.Click
        ' Toolbar Save → defaults to the "All changed" scope. The tree context-menu "Save Selected"
        ' calls LaunchSaveDialogAsync(defaultToSelected:=True) instead.
        Await LaunchSaveDialogAsync(defaultToSelected:=False)
    End Sub

    ''' <summary>Open the Save ESP/ESM dialog. <paramref name="defaultToSelected"/> picks the default
    ''' scope radio: False (toolbar) → "All changed"; True (context-menu "Save Selected") → "Selected"
    ''' (the NPC multi-selection / conjunto). Both scope labels always show their NPC count.</summary>
    Private Async Function LaunchSaveDialogAsync(defaultToSelected As Boolean) As Task
        If _renderHost.CurrentBaseState Is Nothing Then Return

        ' "Selected" scope = the NPC multi-selection (the conjunto), or the currently-rendered NPC
        ' when nothing is multi-selected. Always savable, even if not dirty (forwarded/identity
        ' override). The first entry is the anchor for the existing-plugin scan + source plugin.
        Dim selectedFormIDs As New List(Of UInteger)
        If _selectedNpcFormIDs.Count >= 1 Then
            selectedFormIDs.AddRange(_selectedNpcFormIDs)
        Else
            selectedFormIDs.Add(_renderHost.CurrentBaseState.RootNpcFormID)
        End If
        Dim selectedInputs As New List(Of NpcOverrideSaver.NpcSaveInput)
        Dim seenSel As New HashSet(Of UInteger)
        For Each fid In selectedFormIDs
            Dim inp = TryBuildNpcSaveInput(fid)
            If inp IsNot Nothing AndAlso seenSel.Add(fid) Then selectedInputs.Add(inp)
        Next
        If selectedInputs.Count = 0 Then
            MessageBox.Show("Could not find or parse the selected NPC record(s).", "Save ESP/ESM",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        Dim selectedInput = selectedInputs(0)
        Dim selectedFormID = selectedInput.NpcFormID

        ' The "Apply to all changed NPCs" scope = every dirty NPC (parsed; unparseable ones skipped).
        Dim allDirtyInputs As New List(Of NpcOverrideSaver.NpcSaveInput)
        Dim seen As New HashSet(Of UInteger)
        For Each fid In _dirtyNpcs
            Dim inp = TryBuildNpcSaveInput(fid)
            If inp IsNot Nothing AndAlso seen.Add(fid) Then allDirtyInputs.Add(inp)
        Next
        ' If nothing is dirty, the "all" scope collapses to the selected NPC so it is never empty.
        If allDirtyInputs.Count = 0 Then allDirtyInputs.Add(selectedInput)

        ' Source plugin's IsESM (default light-master flag) — from the selected NPC's source.
        Dim sourceMasterIsEsm As Boolean = False
        For Each plugin In _pluginManager.Plugins
            If plugin Is Nothing Then Continue For
            If String.Equals(plugin.FileName, selectedInput.SourcePluginName, StringComparison.OrdinalIgnoreCase) Then
                sourceMasterIsEsm = plugin.IsESM
                Exit For
            End If
        Next

        ' Scan Data\ for existing auto-generated plugins (ContainsTargetNpc keyed to the selected NPC).
        Dim existing = ScanForAutoGeneratedPlugins(selectedFormID)

        ' Build SaveContext — bundles the dependencies the orchestrator (NpcOverrideSaver) needs
        ' to call back into the host. All MainForm helpers it consumes (overlay merge, round-trip
        ' field copy, parallel-collection sync, CharGen bake) are forwarded as delegates so the
        ' orchestrator stays UI-free.
        Dim ctx As New NpcOverrideSaver.SaveContext With {
            .PluginManager = _pluginManager,
            .AppliedPresets = _appliedPresets,
            .RenderHost = _renderHost,
            .DataPath = _dataPath,
            .ApplyPresetOverlayToNpcData = AddressOf ApplyPresetOverlayToNpcData,
            .ApplyNpcRecordOverride = AddressOf ApplyNpcRecordOverrideToSpec,
            .RunChargenBake = Function(npcFid As UInteger, anchor As String, srcPlugin As String,
                                        prog As IProgress(Of NpcOverrideSaver.SaveProgress)) _
                                   As Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String, TexWarning As String))
                                  Return RunChargenBake(npcFid, anchor, srcPlugin, prog)
                              End Function,
            .RunChargenPackBatch = Function(anchor As String,
                                             bundles As IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle),
                                             excludeEntries As IReadOnlyList(Of String),
                                             prog As IProgress(Of NpcOverrideSaver.SaveProgress)) _
                                        As Task(Of (Summary As String, Success As Boolean))
                                       Return RunChargenPackBatch(anchor, bundles, excludeEntries, prog)
                                   End Function,
            .OutfitDrafts = New List(Of OutfitDraft)(_outfitDrafts),
            .LeveledListDrafts = New List(Of LeveledListDraft)(_leveledListDrafts),
            .ArmoDrafts = New List(Of ArmoDraft)(_armoDrafts),
            .ArmaDrafts = New List(Of ArmaDraft)(_armaDrafts),
            .MswpDrafts = New List(Of MswpDraft)(_mswpDrafts),
            .RecordsToRemove = New HashSet(Of UInteger)(_recordsToRemove),
            .AllocateDraftFormID = AddressOf AllocateDraftFormID
        }

        ' Show dialog. The form runs the orchestrator internally (async, with progress in an
        ' embedded panel), and exposes the result via dlg.ExecutionResult. ShowDialog returns
        ' DialogResult.OK only after the save finished successfully.
        Dim execResult As NpcOverrideSaver.SaveExecutionResult = Nothing
        Dim target As SaveEsp_Form.SaveTarget = Nothing
        Using dlg As New SaveEsp_Form(_dataPath, existing, selectedInputs, allDirtyInputs, sourceMasterIsEsm, ctx, defaultToSelected, selectedInput.SourcePluginName)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            target = dlg.Result
            execResult = dlg.ExecutionResult
        End Using
        If target Is Nothing OrElse execResult Is Nothing Then Return
        ' "EL ESP SE ESCRIBIÓ" NO ES LO MISMO QUE "EL GUARDADO SALIÓ BIEN", y pegarlos duplicaba records.
        ' El ESP es el punto de no retorno, pero DESPUÉS van el sidecar, los .ini de BodyGen y InstallPex (que
        ' lanza a propósito). Con `Not execResult.Success Then Return` cualquier excepción de esas fases se
        ' llevaba puesto el readback y, con él, PromoteSavedDrafts. Al reintentar, el OTFT/ARMO ya escrito se
        ' preserva con su FormID REAL mientras el draft en memoria sigue en 0xFF…, y `alreadyEmitted`
        ' (en NpcOverrideSaver) sólo contiene FormID reales ⇒ se emite OTRA VEZ como record nuevo, con `_2`
        ' pegado al EditorID y sin un aviso. Repetible por cada reintento, y viaja al plugin que se distribuye.
        ' Disparadores reales: Data\Scripts de sólo lectura, carpeta virtual del mod manager, juego abierto.
        ' Con `WriterResult` presente el archivo YA está en disco, así que el estado en memoria tiene que
        ' alinearse con él igual; la falla de la fase posterior se reporta aparte, no se esconde.
        Dim espWasWritten = execResult.WriterResult IsNot Nothing
        If Not execResult.Success AndAlso Not espWasWritten Then Return
        ' Falla PARCIAL (el ESP salió, una fase posterior no): el diálogo ya se lo dijo al usuario con el
        ' detalle. El box de "Saved N NPCs" del final se suprime — dos diálogos seguidos, el segundo diciendo
        ' que salió todo bien, es peor que no decir nada.
        Dim savePartiallyFailed = Not execResult.Success

        ' Drafts written this save (OTFT outfits + LVLI leveled lists) are PROMOTED to real records inside
        ' ApplyPostSaveReadback: once the saved plugin is re-mounted, every reference still pointing at a
        ' provisional FormID is remapped to the real record and the draft is dropped from the in-memory set,
        ' so re-using the outfit on another NPC reuses the real record instead of re-emitting a duplicate.

        ' Cache update (add/refresh the auto-gen plugin entry without re-scanning disk).
        If target.IsNewPlugin Then
            RegisterSavedPluginInCache(target.TargetPath, execResult.SavedFormIDs, target.MarkAsMaster, target.LightMaster)
        Else
            RefreshSavedPluginInCache(target.TargetPath, execResult.SavedFormIDs, target.MarkAsMaster, target.LightMaster)
        End If
        ' A just-saved plugin may enable Export FOMOD for the current selection without reselecting.
        UpdateExportFomodEnabled()

        ' Mark-to-delete post-save: BEFORE the readback re-mounts anything, snapshot which marked FormIDs were
        ' actually dropped from THIS save's target (their winning record is currently sourced from the target
        ' plugin) plus their CharGen bake loose paths (origin/local FormID resolve cleanly here, before Part 2
        ' reverts them). Marks pointing at a record in a DIFFERENT plugin were no-ops on disk (saver Phase 2a) and
        ' are left to the in-memory model untouched — the user re-marks before saving that plugin.
        Dim targetBaseName = IO.Path.GetFileName(target.TargetPath)
        Dim droppedFromTarget As New List(Of UInteger)
        Dim faceGenLooseToDelete As New List(Of String)
        For Each fid In _recordsToRemove
            Dim rec = _pluginManager.GetRecord(fid)
            If rec IsNot Nothing AndAlso String.Equals(rec.SourcePluginName, targetBaseName, StringComparison.OrdinalIgnoreCase) Then
                droppedFromTarget.Add(fid)
                faceGenLooseToDelete.AddRange(ResolveNpcFaceGenLoosePaths(fid))
            End If
        Next

        ' Step 6: re-read the just-saved records as the new baseline (mount the written plugin last
        ' in load order), strip the now-persisted ESP fields from each overlay (keeping non-ESP
        ' BodyMorphs/Skin), clear the dirty marks, regroup in the tree, and re-render the loaded NPC.
        Await ApplyPostSaveReadback(execResult.WrittenNpcFormIDs, target.TargetPath, execResult.DraftFormIdMap,
                                    sidecarWritten:=execResult.SidecarWritten)

        ' Part 3 — delete the removed NPCs' CharGen bake LOOSE files (NIF + _d/_msn/_s DDS, incl. debug _2 variants).
        ' Safe (disk-only). The BA2-packed bakes are left as a TODO (see DeleteFaceGenLooseFiles remarks).
        DeleteFaceGenLooseFiles(faceGenLooseToDelete)

        ' Part 2 — make the dropped records DISAPPEAR from the in-memory model (they were preserved by an earlier
        ' in-session mount, so the readback's re-mount can't remove them). Revert each (NEW app record → drop;
        ' OVERRIDE → restore the base), fix _allNPCs, then rebuild the tree model + repopulate so the node vanishes
        ' (NEW) or reappears under the base plugin group (OVERRIDE).
        Dim removedAny As Boolean = False
        For Each fid In droppedFromTarget
            RevertAppOverrideInMemory(fid)   ' PluginManager: drop NEW / revert OVERRIDE + targeted parse-cache invalidate
            _dirtyNpcs.Remove(fid)
            _npcRecordOverrides.Remove(fid)
            ' Drop the LooksMenu overlay too. The saver's Phase 3b already PRUNED this NPC from the .bssliders
            ' sidecar + BodyGen .ini on disk (via _recordsToRemove), but the overlay is the in-memory SOURCE the
            ' sidecar is built from (MergeOneNpcIntoSidecar reads _appliedPresets). Leaving it behind desyncs
            ' memory from disk and would RESURRECT the pruned sidecar entry on the next save. A deleted NEW record
            ' / reverted OVERRIDE discards all authored appearance, so the whole overlay goes.
            _appliedPresets.Remove(fid)
            Dim baseRec = _pluginManager.GetRecord(fid)
            If baseRec Is Nothing OrElse baseRec.Header.Signature <> "NPC_" Then
                ' NEW authored record → gone from the load order. Drop every model entry for it.
                _allNPCs.RemoveAll(Function(n) n IsNot Nothing AndAlso n.FormID = fid)
            Else
                ' OVERRIDE reverted → re-parse the now-winning base record and replace it in the model.
                Dim baseNpc = NpcRecordOverlay.GetParsedNpc(fid, _pluginManager)
                If baseNpc IsNot Nothing Then
                    Dim replaced = False
                    For i = 0 To _allNPCs.Count - 1
                        If _allNPCs(i) IsNot Nothing AndAlso _allNPCs(i).FormID = fid Then
                            _allNPCs(i) = baseNpc
                            replaced = True
                            Exit For
                        End If
                    Next
                    If Not replaced Then _allNPCs.Add(baseNpc)
                End If
            End If
            removedAny = True
        Next
        If removedAny Then
            ' NO se ordena aca: en este punto `_npcSortKeyCache` todavia tiene la clave del override que
            ' se acaba de tirar, asi que ordenar ahora usaria el nombre VIEJO. Quien re-siembra las claves
            ' es `RebuildTreeModelCache`, y es el que ordena al terminar.
            ' Rebuild the model caches from the mutated _allNPCs (drops the NEW record from NpcCache/searchable/
            ' display too), then repopulate the tree so the phantom node is gone.
            RebuildTreeModelCache()
            PopulateNPCTree(_pendingTreeFilter)
        End If

        ' Removal intents were applied by the saver's Phase 2a (it dropped every target-plugin record whose GLOBAL
        ' FormID is in this set). Clear it now so a stale mark can't re-drop a record the user later re-authors: e.g.
        ' revert-then-re-edit-then-save re-emits the override in Phase 2e/2f this save, but an uncleared mark would
        ' make the NEXT save's Phase 2a delete the freshly-written record again. Fids that pointed at a record in a
        ' DIFFERENT plugin were no-ops here and are dropped too — the user re-marks before saving that plugin.
        _recordsToRemove.Clear()

        Dim savedCount = execResult.WrittenNpcFormIDs.Count
        Dim what = If(savedCount = 1, $"{If(selectedInput.Npc?.EditorID, selectedFormID.ToString("X8"))}", $"{savedCount} NPCs")
        ' Title reflects the icon: a texture/pack warning (VerifierIcon = Warning) says so up front instead of
        ' a bare "Save ESP/ESM" over a body that quietly reports missing textures.
        Dim boxTitle = If(execResult.VerifierIcon = MessageBoxIcon.Warning,
                          "Save ESP/ESM — completed with warnings", "Save ESP/ESM")
        ' En una falla PARCIAL el diálogo de Save ya mostró el detalle de qué fase reventó. Sacar acá un
        ' "Saved N NPCs" liso sería contradecirlo con el último diálogo que ve el usuario, que es el que se
        ' recuerda.
        ' Lo que el EMISOR encontró y el usuario tiene que ver: hoy, los textos localizados que no se
        ' pudieron resolver contra las tablas de idioma y salieron VACÍOS. El archivo sale bien formado y
        ' el NPC sin nombre, así que sin esto la pérdida es invisible — y como el aviso cambia el
        ' veredicto, también cambia el ícono y el título.
        Dim avisosDelEmisor = If(execResult.WriterResult?.Advertencias, New List(Of String)())
        Dim resumenAvisos As String = ""
        If avisosDelEmisor.Count > 0 Then
            Const TOPE = 10
            resumenAvisos = Environment.NewLine & Environment.NewLine &
                            $"{avisosDelEmisor.Count} campo(s) de texto no se pudieron resolver contra las tablas de " &
                            "idioma y se grabaron VACÍOS:" & Environment.NewLine &
                            String.Join(Environment.NewLine, avisosDelEmisor.Take(TOPE).Select(Function(a) "  · " & a))
            If avisosDelEmisor.Count > TOPE Then
                resumenAvisos &= Environment.NewLine & $"  … y {avisosDelEmisor.Count - TOPE} más."
            End If
            boxTitle = "Save ESP/ESM — completed with warnings"
        End If
        Dim iconoFinal = If(avisosDelEmisor.Count > 0 AndAlso execResult.VerifierIcon = MessageBoxIcon.None,
                            MessageBoxIcon.Warning, execResult.VerifierIcon)

        If Not savePartiallyFailed Then
            MessageBox.Show($"Saved {what} to {IO.Path.GetFileName(execResult.WriterResult.OutputPath)}.{execResult.ChargenSummary}{execResult.VerifierSummary}{resumenAvisos}",
                            boxTitle, MessageBoxButtons.OK, iconoFinal)
        End If
    End Function

    ''' <summary>Loose FaceGen bake files the app could have written for <paramref name="npcFormID"/>, under
    ''' Data\ keyed by the record's ORIGIN plugin + FaceGen-local FormID. GAME-AWARE via
    ''' <see cref="NpcFaceGenPacker.FaceGenFileSpecs"/> (FO4: FaceGeom NIF + FaceCustomization _d/_msn/_s;
    ''' SSE: FaceGeom NIF + FaceGenData\FaceTint DDS), covering both the release and the <c>_2</c> debug
    ''' variant of every file that has one. Best-effort: returns every candidate; the caller deletes those
    ''' that exist. Empty when origin plugin or DataPath can't be resolved.</summary>
    Private Function ResolveNpcFaceGenLoosePaths(npcFormID As UInteger) As List(Of String)
        Dim paths As New List(Of String)
        If String.IsNullOrEmpty(_dataPath) Then Return paths
        Dim originPlugin = _pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then Return paths
        Dim idLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Dim game = Config_App.Current.Game

        ' Release names (debugSandbox:=False) + the debug _2 names — the bake may have written either.
        For Each debugSandbox In {False, True}
            For Each spec In NpcFaceGenPacker.FaceGenFileSpecs(game, originPlugin, idLow, debugSandbox)
                Dim full = IO.Path.Combine(_dataPath, spec.Source)
                If Not paths.Contains(full, StringComparer.OrdinalIgnoreCase) Then paths.Add(full)
            Next
        Next
        Return paths
    End Function

    ''' <summary>Delete the given loose FaceGen bake files, each best-effort: skip if absent, swallow IO errors
    ''' so a single locked/missing file can't abort the batch. BA2-PACKED bakes are NOT touched here — removing
    ''' an entry from a shared FaceGen BA2 needs a full repack, out of scope for the delete path (TODO): a
    ''' deleted NPC whose bake lives only inside a BA2 keeps that entry until the archive is rebuilt.</summary>
    Private Sub DeleteFaceGenLooseFiles(paths As IEnumerable(Of String))
        If paths Is Nothing Then Return
        For Each p In paths
            If String.IsNullOrEmpty(p) Then Continue For
            Try
                If IO.File.Exists(p) Then IO.File.Delete(p)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[MARK-DELETE] could not delete FaceGen loose '{p}': {ex.GetType().Name}: {ex.Message}")
            End Try
        Next
    End Sub

    ''' <summary>Build the per-NPC save input from a FormID: fetch + type-safe parse the raw record,
    ''' y resolve its source plugin. Returns Nothing when the
    ''' record is missing or not an NPC_. Note: after a prior Save this session mounted an auto-gen
    ''' override (MergeOverridePlugin), GetRecord returns that override — re-saving then uses the
    ''' saved state as its base, which is the intended "saved override is the new baseline" behaviour.</summary>
    Private Function TryBuildNpcSaveInput(npcFormID As UInteger) As NpcOverrideSaver.NpcSaveInput
        If npcFormID = 0UI Then Return Nothing
        Dim rawRecord = _pluginManager.GetRecord(npcFormID)
        If rawRecord Is Nothing OrElse rawRecord.Header.Signature <> "NPC_" Then Return Nothing
        Dim sourcePluginName = If(rawRecord.SourcePluginName, "")
        Dim rawNpcSpec = RecordParsers.ParseNPC(rawRecord, _pluginManager)
        If rawNpcSpec Is Nothing Then Return Nothing
        Dim npc As NPC_Data = Nothing
        _ctx.NpcCache.TryGetValue(npcFormID, npc)

        Return New NpcOverrideSaver.NpcSaveInput With {
            .NpcFormID = npcFormID,
            .Npc = If(npc, rawNpcSpec),
            .RawRecord = rawRecord,
            .RawNpcSpec = rawNpcSpec,
            .SourcePluginName = sourcePluginName
        }
    End Function

    ''' <summary>Saca del overlay de un NPC los campos que ya persistio el ESP tras un Save exitoso, dejando
    ''' solo los que son F4SE-only y no tienen equivalente en el record - el MISMO conjunto que persiste el
    ''' sidecar .bssliders (sliders de BodyMorphs, template de piel y overlays/tatuajes de LM). Los campos del
    ''' ESP ya estan en el override guardado, asi que soltarlos evita que un overlay redundante re-aplique los
    ''' mismos valores.
    ''' <para>Es el espejo de <see cref="HydrateAppliedPresetsFromSidecars"/>: el overlay residual tiene que ser
    ''' estructuralmente identico al de una hidratacion fresca desde el sidecar, o el re-render post-save
    ''' muestra un estado distinto del que se veria reabriendo la app (de ahi el bug de "los tatuajes
    ''' desaparecen tras Save": el sidecar en disco los tenia y el overlay en memoria se rearmaba sin
    ''' ellos).</para>
    ''' <para>Si no queda nada no-ESP, el overlay se elimina entero. True si queda overlay residual, o sea si el
    ''' sidecar en disco conserva una fila para este NPC.</para></summary>
    Private Function StripEspFieldsFromOverlay(npcFormID As UInteger) As Boolean
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(npcFormID, overlay) OrElse overlay Is Nothing Then Return False

        ' Round-trip through the sidecar's own entry type: EntryFromPreset keeps exactly the fields
        ' MergeOneNpcIntoSidecar persists, and ApplyEntryToPreset rebuilds them exactly like a fresh
        ' hydration — the residual is "structurally identical to a sidecar hydration" BY CONSTRUCTION,
        ' so the mirrors can no longer drift. NOT a hand-rolled field-by-field copy: one here once missed
        ' BodyMorphsKeyed + SseCustomMorphs + SseSculptHead/Parts + SseTintTexOverride, which a second
        ' Save then silently wiped from the sidecar on disk.
        Dim entry = BssliderSidecar.EntryFromPreset(overlay, "", "")
        If entry.HasAnything Then
            Dim residual As New LooksmenuLoader.LooksmenuPreset()
            BssliderSidecar.ApplyEntryToPreset(entry, residual)
            _appliedPresets(npcFormID) = residual
            Return True
        End If
        _appliedPresets.Remove(npcFormID)
        Return False
    End Function

    ''' <summary>Post-save re-read (Step 6). Mounts the just-written plugin as the top override so
    ''' GetRecord returns the saved state, then RE-PARSES each written NPC into _ctx.NpcCache / _allNPCs.
    ''' That re-parse is load-bearing: GetParsedNpc memoizes per load order (only cleared on a load-order
    ''' change, InvalidateParseCaches), so without it the render keeps resolving the STALE pre-save parse
    ''' and the saved outfit (DOFT) / tints / morphs / headparts silently revert in the preview until the
    ''' app is reopened — even though the ESP on disk is correct. For each written NPC: strip the ESP fields
    ''' from its overlay, clear its dirty mark (no longer bold), refresh its cached parse, and move it to the
    ''' saved plugin's tree group. Re-renders the currently-loaded NPC when it was among those saved so the
    ''' preview drops the overlay and shows the clean saved record.</summary>
    Private Async Function ApplyPostSaveReadback(writtenFormIDs As List(Of UInteger), savedPluginPath As String,
                                                 draftFormIdMap As Dictionary(Of UInteger, UInteger),
                                                 sidecarWritten As Boolean) As Task
        ' NOTE: the pending-removal marks (_recordsToRemove) are deliberately NOT cleared here. They're kept for
        ' the session so (a) a removed record never reappears in the "my records" lists even if the in-memory
        ' re-mount below leaves a stale override, and (b) every subsequent Save re-applies the removal (Phase 2a
        ' skip is idempotent). On the next app launch the rewritten plugin no longer contains them, so they're
        ' genuinely gone. The marks are excluded from GetAuthoredRecords/GetAuthoredOutfitFormIDs meanwhile.

        Dim mergeOk As Boolean = True
        Try
            _pluginManager.MergeOverridePlugin(savedPluginPath)
        Catch ex As Exception
            mergeOk = False
            Logger.LogLazy(Function() $"[SAVE-READBACK] MergeOverridePlugin failed for {savedPluginPath}: {ex.Message}")
        End Try

        Dim savedPluginName = IO.Path.GetFileName(savedPluginPath)

        ' The merge just changed the record universe the FormID picker enumerates (new/override ARMA/ARMO/
        ' MSWP/etc. from the saved plugin). Invalidate here too: the draft-promote path reaches
        ' BuildOutfitUniverse (which also invalidates), but a Save with no drafts to promote returns early
        ' from PromoteSavedDrafts before that — so this guarantees no stale picker rows after any save.
        If mergeOk Then FormIdPicker_Form.InvalidateSignatureCache()

        ' Promote the just-written OTFT/LVLI drafts to real records BEFORE re-rendering: remap any overlay /
        ' remaining-draft reference that still points at a provisional FormID to the real record, drop the
        ' persisted drafts, and refresh the outfit universe so they reappear in the editor as real records.
        ' Only when the re-mount succeeded — otherwise the file-local→global resolution would be wrong and
        ' we keep the drafts so a retry still works.
        If mergeOk Then PromoteSavedDrafts(draftFormIdMap, savedPluginName)
        Dim reloadFid As UInteger = If(_renderHost?.LastRenderedState IsNot Nothing, _renderHost.LastRenderedState.RootNpcFormID, 0UI)
        Dim treeChanged = False
        Dim ordenSucio = False

        For Each fid In writtenFormIDs
            Dim keptF4seResidual = StripEspFieldsFromOverlay(fid)
            ' Keep the sidecar-backed set in sync with what the save just did to disk: with WriteBssliders
            ' on, MergeOneNpcIntoSidecar rebuilt this NPC's row from the SAME overlay the residual came
            ' from, so "residual kept" ⟺ "row on disk". With it off, the sidecar was not touched — leave
            ' the membership alone. Consumer: MenuItemResetOverlay_Click's WYSIWYG dirty routing.
            If sidecarWritten Then
                If keptF4seResidual Then _sidecarBackedNpcs.Add(fid) Else _sidecarBackedNpcs.Remove(fid)
            End If
            ' The authored NPC-record override is now in the saved plugin (re-read as this NPC's new baseline
            ' below), so drop it — leaving it would just re-apply identical values on the next save. Mirror of
            ' StripEspFieldsFromOverlay for the record-field overrides.
            _npcRecordOverrides.Remove(fid)
            ClearNpcDirty(fid)

            ' Re-parse the saved override as this NPC's new baseline. MergeOverridePlugin mounted the
            ' written plugin on top, so GetRecord(fid) now returns the override — but _ctx.NpcCache still
            ' memoizes the PRE-save parse (it is only cleared on a load-order change, InvalidateParseCaches).
            ' Without refreshing it here, every later GetParsedNpc — and therefore the re-render's outfit
            ' (DOFT) / tints / morphs / headparts resolution — reads the stale pre-edit record, so the saved
            ' changes silently revert in the preview until the app is reopened (the reported "outfit not
            ' saved" bug: the ESP on disk is correct, only the in-session readback was stale). Replace the
            ' instance in BOTH _ctx.NpcCache and _allNPCs (they share NPC_Data instances) so the render and
            ' the tree/display stay in sync, and re-derive the tree group from the freshly-parsed plugin name.
            Dim staleNpc As NPC_Data = Nothing
            _ctx.NpcCache.TryGetValue(fid, staleNpc)
            Dim oldPluginName As String = If(staleNpc IsNot Nothing, staleNpc.PluginName, Nothing)
            Dim freshNpc = NpcRecordOverlay.GetParsedNpc(fid, _pluginManager)
            If freshNpc IsNot Nothing Then
                _ctx.NpcCache(fid) = freshNpc
                For i = 0 To _allNPCs.Count - 1
                    If _allNPCs(i) IsNot Nothing AndAlso _allNPCs(i).FormID = fid Then
                        _allNPCs(i) = freshNpc
                        Exit For
                    End If
                Next
                ' Un Save puede cambiar FullName o EditorID, y de esos dos salen los TRES caches
                ' derivados. Se refrescan con la MISMA funcion que usa el editor de NPC, no con una
                ' copia: tener las dos implementaciones es como se llegó a que el filtro buscara por el
                ' nombre nuevo mientras el nodo seguía etiquetado con el viejo.
                RefrescarCachesDerivados(freshNpc, ordenarAhora:=False)
                ' Un save puede cambiar head parts / skin / outfit, asi que todo resultado del filtro
                ' avanzado calculado desde este record esta viejo. Se tira ENTERO (no por FormID) porque
                ' este NPC puede ser el template de cualquier cantidad de otros.
                _filterIndex?.InvalidateNpcState()
                ' La lista se REORDENA una sola vez al salir del bucle: refrescar la clave sin reacomodar
                ' dejaria el arbol mostrando el nombre nuevo en la posicion vieja.
                ordenSucio = True
                ' ⛔ Y SE REPUEBLA SIEMPRE. Acá hubo un detector que decidía si repoblar comparando la
                ' etiqueta "vieja" contra la nueva, y estaba ENVENENADO: la etiqueta "vieja" salía de
                ' `_npcDisplayLabelCache`, que el editor de NPC YA HABÍA PISADO antes de llegar acá. Las
                ' dos eran iguales siempre, así que por el label no repoblaba nunca; lo tapaba el chequeo
                ' de plugin de más abajo, que es True la PRIMERA vez que se guarda a un plugin nuevo y
                ' False de ahí en más. Escenario medido: renombrar → Save → renombrar → Save al MISMO
                ' plugin ⇒ el árbol quedaba con el nombre viejo. Es el defecto que reportó el usuario.
                '
                ' El arreglo NO es un detector mejor. Un detector ya falló dos veces en este mismo bloque
                ' (antes por la guarda `oldLabel IsNot Nothing`, después por el envenenamiento) y además
                ' no puede ser confiable: la MISMA clave tiene N filas en el árbol —el NPC cuelga de su
                ' plugin y de cada LVLN— así que comparar contra una no dice nada de las otras. Repoblar
                ' siempre saca la condición que se podía equivocar. `FilaDeArbol.Texto` es una COPIA que
                ' hace `PopulateNPCTree`, y el renombre además MUEVE la fila de lugar, así que reescribir
                ' el texto tampoco alcanzaría.
                treeChanged = True
            End If
        Next

        ' Una sola pasada de orden para todos los NPC guardados, no una por NPC.
        If ordenSucio Then OrdenarNpcs()

        ' When the tree grouping OR any saved NPC's display label changed, rebuild it and re-select the
        ' loaded NPC — the AfterSelect handler re-renders it from the clean record. Otherwise re-render
        ' explicitly if the loaded NPC was saved (its overlay was just stripped and needs to drop off
        ' the preview).
        If treeChanged Then
            PopulateNPCTree(_pendingTreeFilter)
            If reloadFid <> 0UI Then
                ' Una sola llamada: busca por clave en TODO el árbol —también dentro de grupos
                ' cerrados—, abre lo que haga falta, lo trae a la vista y lo enfoca. Antes eran cuatro
                ' pasos y el `Find` no veía lo que no estuviera expandido.
                If TreeViewNPCs.EnfocarClave($"NPC_{reloadFid:X8}") Then Return
            End If
        End If

        If reloadFid <> 0UI AndAlso writtenFormIDs.Contains(reloadFid) Then
            Dim npc As NPC_Data = Nothing
            If _ctx.NpcCache.TryGetValue(reloadFid, npc) AndAlso npc IsNot Nothing Then
                Try
                    Dim version = Interlocked.Increment(_previewRequestVersion)
                    Await LoadNPCOnDemandAsyncFromExisting(npc, version)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[SAVE-READBACK] re-render failed: {ex.Message}")
                End Try
            End If
        End If
    End Function

    ''' <summary>Promote the OTFT/LVLI drafts just written into the saved plugin to real records. For each
    ''' (provisional → file-local real) the writer reported, resolve the GLOBAL FormID of the now-mounted
    ''' record, then: (1) remap any overlay outfit override still pointing at the provisional; (2) remap any
    ''' SURVIVING draft's internal reference (OTFT items / LVLI entries) to a promoted one; (3) drop the
    ''' promoted drafts from the in-memory sets (they are real records now, enumerated from the load order);
    ''' (4) rebuild the outfit universe so the real records surface in the editor. No-op when the map is
    ''' empty. MUST run after <see cref="PluginManager.MergeOverridePlugin"/> so the file-local→global
    ''' resolution sees the mounted plugin (handles full and ESL slots like the record re-read).</summary>
    Private Sub PromoteSavedDrafts(draftFormIdMap As Dictionary(Of UInteger, UInteger), savedPluginName As String)
        If draftFormIdMap Is Nothing OrElse draftFormIdMap.Count = 0 Then Return

        Dim realGlobal As New Dictionary(Of UInteger, UInteger)
        For Each kv In draftFormIdMap
            Dim g = _pluginManager.ResolveReferencedFormID(savedPluginName, kv.Value)
            ' A still-provisional result means the plugin didn't resolve (e.g. not mounted) — skip it so we
            ' never rewrite a reference to a bogus FormID or drop a draft that wasn't actually persisted.
            If g <> 0UI AndAlso Not OutfitDraft.IsDraftFormID(g) Then realGlobal(kv.Key) = g
        Next
        If realGlobal.Count = 0 Then Return

        ' (1) Overlays: an outfit override OR a skin (WNAM) override still aimed at a promoted draft → its real
        ' record. (For the NPCs just saved this is redundant — their overlay is reconciled right after — but it's
        ' what keeps a DIFFERENT NPC that shares the same draft pointing at the real record, not a dead provisional.)
        For Each ov In _appliedPresets.Values
            If ov Is Nothing Then Continue For
            Dim mapped As UInteger
            If ov.DefaultOutfitFormIDOverride.HasValue AndAlso realGlobal.TryGetValue(ov.DefaultOutfitFormIDOverride.Value, mapped) Then
                ov.DefaultOutfitFormIDOverride = mapped
            End If
            If ov.SkinFormIDOverride.HasValue AndAlso realGlobal.TryGetValue(ov.SkinFormIDOverride.Value, mapped) Then
                ov.SkinFormIDOverride = mapped
            End If
        Next

        ' (2) Surviving drafts that reference a promoted one: rewrite every provisional cross-reference to the
        ' real FormID. Mirrors the OTFT/LVLI handling but across all five draft kinds and their FormID-bearing
        ' fields: OutfitDraft.ItemFormIDs→OTFT/ARMO/LVLI; LeveledListDraft.Entries[].RefFormID→ARMO/LVLI;
        ' ArmoDraft.ArmorAddons[].ArmaFormID→ARMA + ArmoDraft material-swap FormIDs→MSWP; ArmaDraft material-swap
        ' FormIDs→MSWP. (A promoted ARMO can be referenced by a surviving OTFT/LVLI item; a promoted ARMA by a
        ' surviving ARMO's addon; a promoted MSWP by a surviving ARMO/ARMA material swap.)
        For Each d In _outfitDrafts
            If d Is Nothing Then Continue For
            For Each it In d.Record.Items
                Dim mapped As UInteger
                If realGlobal.TryGetValue(it.Item, mapped) Then it.Item = mapped
            Next
        Next
        For Each d In _leveledListDrafts
            If d Is Nothing Then Continue For
            For Each e In d.Record.LeveledListEntries
                Dim mapped As UInteger
                If realGlobal.TryGetValue(e.LeveledListEntryItem,
                                          mapped) Then e.LeveledListEntryItem = mapped
            Next
        Next
        For Each d In _armoDrafts
            If d Is Nothing Then Continue For
            ' El modelo de addons (INDX+referencia vs. array de referencias) es distinto por juego;
            ' el
            ' material swap a nivel ARMO sólo existe en Fallout 4.
            Dim armoFo4 = TryCast(d.Record, Canon.ArmoFO4)
            Dim armoSse = TryCast(d.Record, Canon.ArmoSSE)
            If armoFo4 IsNot Nothing Then
                For Each mdl In armoFo4.Models
                    Dim mapped As UInteger
                    If realGlobal.TryGetValue(mdl.ModelArmorAddon,
                                              mapped) Then mdl.ModelArmorAddon = mapped
                Next
                Dim m As UInteger
                If realGlobal.TryGetValue(armoFo4.WorldModelMaterialSwap,
                                          m) Then armoFo4.WorldModelMaterialSwap = m
                If realGlobal.TryGetValue(armoFo4.WorldModelMaterialSwap2,
                                          m) Then armoFo4.WorldModelMaterialSwap2 = m
            ElseIf armoSse IsNot Nothing Then
                For Each mdl In armoSse.Armature
                    Dim mapped As UInteger
                    If realGlobal.TryGetValue(mdl.ModelFilename,
                                              mapped) Then mdl.ModelFilename = mapped
                Next
            End If
        Next
        For Each d In _armaDrafts
            If d Is Nothing Then Continue For
            ' El material swap del ARMA (MO2S/MO3S) sólo existe en Fallout 4.
            Dim armaFo4 = TryCast(d.Record, Canon.ArmaFO4)
            If armaFo4 IsNot Nothing Then
                Dim m As UInteger
                If realGlobal.TryGetValue(armaFo4.MaleMaterialSwap,
                                          m) Then armaFo4.MaleMaterialSwap = m
                If realGlobal.TryGetValue(armaFo4.FemaleMaterialSwap,
                                          m) Then armaFo4.FemaleMaterialSwap = m
            End If
        Next

        ' (3) Drop the promoted drafts. The throwaway preview sentinel is never in the map, so it survives.
        _outfitDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        _leveledListDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        _armoDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        _armaDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        _mswpDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))

        ' (4) Refresh the affected universes so the newly-real records surface in the editor: the outfit
        ' universe (OTFT/LVLI/ARMO items) and the skin-ARMO universe (so a promoted skin ARMO appears in the
        ' WNAM combo). BuildSkinArmoUniverse runs first (it scans the load order incl. the just-mounted plugin),
        ' then BuildOutfitUniverse.
        BuildSkinArmoUniverse()
        BuildOutfitUniverse()
    End Sub

    ''' <summary>Guarda como PNG el frame actual del mismo PreviewControl que usa el render principal.</summary>
    Private Sub ButtonScreenshot_Click(sender As Object, e As EventArgs) Handles ButtonScreenshot.Click
        Dim preview = _renderHost?.PreviewCtl
        If preview Is Nothing OrElse preview.IsDisposed Then
            MessageBox.Show("The render preview is not available.", "Screenshot",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim state = If(_renderHost.LastRenderedState, _renderHost.CurrentBaseState)
        Dim npcFormID As UInteger = If(state IsNot Nothing, state.RootNpcFormID, 0UI)
        Dim npc As NPC_Data = Nothing
        If npcFormID <> 0UI Then _ctx.NpcCache.TryGetValue(npcFormID, npc)
        Dim baseName = If(npc IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(npc.EditorID),
                          npc.EditorID, If(npcFormID <> 0UI, $"NPC_{npcFormID:X8}", "render"))
        For Each ch In IO.Path.GetInvalidFileNameChars()
            baseName = baseName.Replace(ch, "_"c)
        Next

        Using dlg As New SaveFileDialog With {
            .Title = "Save render screenshot",
            .Filter = "PNG image (*.png)|*.png",
            .InitialDirectory = If(String.IsNullOrEmpty(_dataPath), IO.Directory.GetCurrentDirectory(), _dataPath),
            .FileName = baseName & "_" & Date.Now.ToString("yyyyMMdd_HHmmss") & ".png",
            .OverwritePrompt = True,
            .AddExtension = True,
            .DefaultExt = "png"
        }
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Try
                Using bmp = preview.CaptureBitmap()
                    If bmp Is Nothing Then
                        MessageBox.Show("Could not capture render preview.", "Screenshot",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Return
                    End If
                    bmp.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Png)
                End Using
            Catch ex As Exception
                MessageBox.Show("Error saving render screenshot: " & ex.Message, "Screenshot",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ''' <summary>Exporta la escena renderizada a un NIF multi-shape, cada shape visible ya con vértices en
    ''' world-pose. Filtra por el mismo flag que los toggles del preview: lo que se ve es lo que se exporta.
    ''' Usa <c>PerVertexSkinMatrix</c> directo (la matriz del render) y su traspuesta-inversa para normales.
    ''' <para>NO usar el bake de <c>SkinningHelper</c>: produce coordenadas de rebind (dan world-pose al
    ''' RE-skinearse), correcto para re-emitir con skinning intacto e INCORRECTO acá, que exporta sin skin.</para>
    ''' <para>Tras clonar, resetear el T/R/S local a identidad y quitar el skin: si no, el parent-baking
    ''' del camino unskinned vuelve a transformar vértices que ya son absolutos.</para></summary>
    Private Sub ButtonSaveSceneNif_Click(sender As Object, e As EventArgs) Handles ButtonSaveSceneNif.Click
        If _renderHost Is Nothing OrElse _renderHost.CurrentBaseState Is Nothing Then Return
        If _previewControl Is Nothing OrElse _previewControl.Model Is Nothing OrElse
           _previewControl.Model.meshes Is Nothing OrElse _previewControl.Model.meshes.Count = 0 Then
            MessageBox.Show("No rendered scene to export.", "NPC Model to NIF",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        Dim npcFormID = _renderHost.CurrentBaseState.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        _ctx.NpcCache.TryGetValue(npcFormID, npc)

        ' Opciones ANTES del SaveFileDialog: si el usuario cancela acá no tiene sentido haberle pedido
        ' un nombre de archivo primero.
        Dim isSse As Boolean = (Config_App.Current IsNot Nothing AndAlso
                                Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim exportOptions As SceneExportOptions
        Using dlgOpts As New ExportModelOptions_Form()
            ' El dato de "este NPC pliega" lo tiene el resolver del render: lo calculó por shape al
            ' componer los tints de la escena que está en pantalla. Recalcularlo acá sería una segunda
            ' copia del predicado.
            Dim npcFoldsOverlays As Boolean = (_faceTintResolver IsNot Nothing AndAlso
                                               _faceTintResolver.LastSseFoldWasMandatory)
            ' Bbox del modelo ya horneado, para que el diálogo pueda ofrecer un default editable para el
            ' locator del menú de carga. Sale de la MISMA función que usaría el export, así el número que
            ' el usuario ve es el que se escribe.
            ' Se manda ADEMÁS cómo remedirlo: un shape que todavía no pasó por el cómputo de oclusión no
            ' aporta vértices, y con el bbox vacío el diálogo se queda sin el slider de altura. Que lo
            ' pida cuando lo necesita en vez de arrastrar una sola foto.
            Dim measure As Func(Of SceneNifExporter.BakedBounds) =
                Function() SceneNifExporter.MeasureBakedBounds(_previewControl.Model.meshes)
            dlgOpts.Prepare(isSse, npcFoldsOverlays, measure(), measure)
            If dlgOpts.ShowDialog(Me) <> DialogResult.OK Then Return
            exportOptions = dlgOpts.Options
        End Using

        ' Plan de paths de la cara. Sin plugin de origen no se puede armar ningún path del bake, así
        ' que en ese caso el repunte se apaga solo en vez de escribir basura.
        Dim facePlan As FaceTexturePlan = Nothing
        If exportOptions.RepointFaceTextures Then
            Dim originPlugin = _ctx.PluginManager?.GetOriginatingPluginName(npcFormID)
            If Not String.IsNullOrEmpty(originPlugin) Then
                ' Igual que el pliegue: el dato lo tiene el resolver del render, que ya corrió el
                ' predicado del bake (HasFaceOverlayNormals) por shape sobre la escena en pantalla.
                facePlan = New FaceTexturePlan With {
                    .OriginPlugin = originPlugin,
                    .FaceGenLocalFormID = PluginManager.ToFaceGenLocalFormID(npcFormID),
                    .BakeEmitsFoldedNormal = (_faceTintResolver IsNot Nothing AndAlso
                                              _faceTintResolver.LastSseBakeEmitsFoldedNormal)
                }
            End If
        End If

        Dim defaultName = If(npc IsNot Nothing AndAlso Not String.IsNullOrEmpty(npc.EditorID),
                             npc.EditorID, $"NPC_{npcFormID:X8}") & ".nif"
        Dim defaultDir = _dataPath
        If String.IsNullOrEmpty(defaultDir) Then defaultDir = IO.Directory.GetCurrentDirectory()

        Dim outPath As String = Nothing
        Using dlg As New SaveFileDialog()
            dlg.Title = "Save NPC Model to NIF"
            dlg.Filter = "NIF (*.nif)|*.nif"
            dlg.InitialDirectory = defaultDir
            dlg.FileName = defaultName
            dlg.OverwritePrompt = True
            dlg.AddExtension = True
            dlg.DefaultExt = "nif"
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            outPath = dlg.FileName
        End Using

        ' Build/bake/serialize is pure export domain logic — see SceneNifExporter. The handler keeps
        ' only the SaveFileDialog + default-name plumbing (above) and the result MessageBoxes (below).
        Dim result = SceneNifExporter.Export(_previewControl.Model.meshes, outPath, exportOptions, facePlan)

        If result.ShapesWritten = 0 Then
            MessageBox.Show("No visible shapes were exported." & vbCrLf & result.FailureDetails,
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        If result.SaveError IsNot Nothing Then
            MessageBox.Show($"Failed to write {outPath}: {result.SaveError}",
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Sólo se informa lo que el usuario tiene que resolver: shapes que no se pudieron escribir.
        ' Un export limpio no da ninguna decisión, y un resumen largo para decir "no pasó nada"
        ' enseña a cerrar el cuadro sin leerlo — incluido el que sí traía un error.
        If result.ShapesFailed > 0 Then
            MessageBox.Show($"{result.ShapesFailed} shape{If(result.ShapesFailed = 1, "", "s")} failed:" & vbCrLf & result.FailureDetails,
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If

        ' Residuo del skin tone: el export salió bien igual (los shapes se escribieron), pero en esos el
        ' modelo NO va a matchear al preview, que es justo lo que la opción prometía. Silenciarlo sería
        ' venderle un "Export OK" a un archivo que no cumple.
        If result.SkinToneSkipped > 0 Then
            MessageBox.Show($"Export OK, but the skin tone was not written on {result.SkinToneSkipped} shape{If(result.SkinToneSkipped = 1, "", "s")}:" &
                            vbCrLf & result.SkinToneSkippedDetails,
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Mismo criterio que el residuo del skin tone: el NIF se escribió, pero el usuario pidió el
        ' locator del menú de carga y no está. Sin este cuadro se lo lleva creyendo que sí.
        If result.LoadScreenNodeError IsNot Nothing Then
            MessageBox.Show("Export OK, but " & result.LoadScreenNodeError,
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        MessageBox.Show("Export OK", "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    ''' <summary>Phase 4a delegate: bake one NPC's FaceGen NIF + FaceCustomization textures to
    ''' loose files. UI-thread / GL-bound. Returns a <see cref="NpcFaceGenPacker.BakedNpcBundle"/>
    ''' identifying the loose so the orchestrator can batch them into one PackBatch call after the
    ''' whole bake loop. Never throws — failures surface via Success=False + FailureMessage.</summary>
    Private Async Function RunChargenBake(npcFormID As UInteger,
                                          anchorPluginPath As String,
                                          sourcePluginName As String,
                                          progress As IProgress(Of NpcOverrideSaver.SaveProgress)) As Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String, TexWarning As String))
        ReportSaveProgress(progress, "Baking CharGen NIF + textures…", "", False, 0, 0)

        ' LA IDENTIDAD DEL BAKE ES EL NPC QUE SE GUARDA, PUNTO — NO derivarla de
        ' `_renderHost.LastRenderedState.ModelSourceFormID`: ese campo es inerte en el render hoy
        ' (NpcStateFactory nunca lo cablea, siempre cae al root), pero si algún día se cablea dejaría una
        ' lectura del RENDER decidiendo dos cosas críticas: qué NPC se hornea y, vía
        ' GetOriginatingPluginName + ToFaceGenLocalFormID, LA RUTA DE SALIDA del FaceGeom y sus texturas —
        ' el bake de un NPC empezaría a escribir en la ruta de OTRO, en silencio.
        ' BuildCharGen es headless: resuelve la apariencia del record + overlay, sin estado del host.
        Dim bakeFormID As UInteger = npcFormID

        ' GL-bound bake (FaceTintCompositor GPU pipeline + GL.GetTexImage readback) — MUST stay on
        ' the UI thread, which owns the OpenGL context. Runs synchronously: no await has happened
        ' yet, so we are still on the UI thread the orchestrator called us from. Single Await
        ' Task.Yield at entry would already have yielded; placing it here keeps the function async.
        Await Task.Yield()
        Dim bakeResult As FaceGenBuilder.BuildResult
        Try
            ' `willBePacked` sigue lo que de verdad va a pasar con los archivos: True solo si el packer va a
            ' repackear los sueltos `_2` al BA2 con nombres canónicos (el NIF debe embeber esos canónicos).
            ' En LOOSE-ONLY no hay packer, nadie renombra: con True el NIF apuntaría a texturas que no existen.
            ' Solo muerde en DebugMode, que es justo el modo de una sesión de diagnóstico. Ver 40-bake-reglas-comunes.
            Dim willPack As Boolean = Not NPC_Config.IsLooseOnly(Config_App.Current.Game)
            ' WriteGPUSandboxOutput corre el GL (para el _2b) -> sync en el hilo UI (contexto GL; ya estamos
            ' en él tras el Yield), INDEPENDIENTE de DebugMode. Sin ese flag (output CPU-only, sin GL) -> bake
            ' en thread de fondo (Await Task.Run). Secuencial -> sin race entre NPCs.
            Dim fidL = bakeFormID
            If FaceGenBuilder.WriteGPUSandboxOutput Then
                bakeResult = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets,
                                                         _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides,
                                                         willBePacked:=willPack, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
            Else
                bakeResult = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets,
                                                         _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides,
                                                         willBePacked:=willPack, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
            End If
        Catch ex As Exception
            Return (False, False, Nothing, $"CharGen bake failed: {ex.Message}", "")
        End Try

        If bakeResult.Skipped Then
            ' No FaceGen head parts (non-human race, etc.) → nothing to bake/pack. SKIP, not failure.
            Return (True, True, Nothing, "", "")
        End If
        If Not bakeResult.Success Then
            Return (False, False, Nothing, "CharGen bake failed", "")
        End If

        Dim originPlugin = _pluginManager.GetOriginatingPluginName(bakeFormID)
        ' ESL-aware local id (PluginManager.ToFaceGenLocalFormID): the packed FaceGen file name MUST match
        ' the engine's lookup name (FaceGenBuilder.ResolveFaceGenPath usa el mismo helper),
        ' so an ESL NPC bakes to "00000800", not "00032800" — otherwise the lookup misses the packed file.
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(bakeFormID)
        Logger.LogLazy(Function() $"[CHARGEN-ID] save npcFormID=0x{npcFormID:X8} bakeFormID=0x{bakeFormID:X8} → originPlugin='{originPlugin}' formIdLow=0x{formIdLow:X8}")

        Dim bundle As New NpcFaceGenPacker.BakedNpcBundle With {
            .OriginPlugin = originPlugin,
            .FormIdLow = formIdLow,
            .DebugSandbox = FaceGenBuilder.DebugMode,
            .ExtraLooseFiles = bakeResult.ExtraLooseFiles
        }
        ' The NIF wrote (Success=True), but face-texture slots may have failed to encode/write. Surface the
        ' cause upward: the orchestrator adds it to the save summary + flips the icon to Warning, so the user
        ' sees WHY textures are missing instead of a silent "1 OK" followed by "0/1 packed, N unaccounted".
        Dim texWarn As String = ""
        If bakeResult.TextureSlotsFailed > 0 Then
            texWarn = $"{bakeResult.TextureSlotsFailed} texture output(s) failed — {bakeResult.TextureFailureDetail}"
        End If
        Return (True, False, bundle, "", texWarn)
    End Function

    ''' <summary>Phase 4b delegate: take the bundles collected from successful per-NPC bakes and
    ''' pack them into the BA2 set anchored to <paramref name="anchorPluginPath"/> in ONE batched
    ''' <see cref="NpcFaceGenPacker.PackBatch"/> call. Runs on a worker thread (Task.Run).
    '''
    ''' Honors the loose-only sentinel: when <see cref="NPC_Config.Ba2Version_FO4"/>=0, returns
    ''' a summary saying the bake outputs were left as loose and skips the pack entirely.
    ''' Never throws — pack failures surface via Success=False + Summary.</summary>
    Private Async Function RunChargenPackBatch(anchorPluginPath As String,
                                                bundles As IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle),
                                                excludeEntries As IReadOnlyList(Of String),
                                                progress As IProgress(Of NpcOverrideSaver.SaveProgress)) As Task(Of (Summary As String, Success As Boolean))
        Dim hasBundles = bundles IsNot Nothing AndAlso bundles.Count > 0
        Dim hasExcludes = excludeEntries IsNot Nothing AndAlso excludeEntries.Count > 0
        ' Nothing to pack AND nothing to strip → no-op.
        If Not hasBundles AndAlso Not hasExcludes Then
            Return ("", True)
        End If
        Dim bundlesToPack As IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle) =
            If(bundles, CType(New List(Of NpcFaceGenPacker.BakedNpcBundle)(), IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle)))

        ' Capture config on the UI thread before going to the worker — same pattern the original
        ' RunChargenBakeAndPack used.
        Dim dataPath = _dataPath
        Dim game = Config_App.Current.Game
        Dim ba2Version = NPC_Config.Current.Ba2Version_FO4
        Dim excludeList As List(Of String) = If(hasExcludes, New List(Of String)(excludeEntries), Nothing)

        ' Loose-only sentinel: skip the pack. The 4 loose files per NPC stay on disk where the
        ' engine auto-discovers them at runtime. Matches the user's intent for the
        ' "None - Loose files" option in the SaveEsp BA2 version combo. (Mark-to-delete: there is no BA2 to
        ' strip in loose mode — the removed NPC's loose were already deleted by DeleteFaceGenLooseFiles.)
        If NPC_Config.IsLooseOnly(game) Then
            If Not hasBundles Then Return ("", True)
            Dim n = bundles.Count
            Return ($"Archive pack skipped — {n} NPC{If(n = 1, "", "s")} left as loose files (None - Loose mode).", True)
        End If

        Try
            Dim packResult = Await Task.Run(
                Function()
                    Return NpcFaceGenPacker.PackBatch(
                        anchorPluginPath, dataPath, game, ba2Version, bundlesToPack,
                        Sub(p As NpcFaceGenPacker.PackProgress)
                            Select Case p.Phase
                                Case NpcFaceGenPacker.PackPhase.BuildingBundle
                                    ReportSaveProgress(progress, "Compressing FaceGen bundles…", p.Detail, p.Max > 0, p.Max, p.Current)
                                Case NpcFaceGenPacker.PackPhase.WritingArchive
                                    ReportSaveProgress(progress, "Writing BA2 archive(s)…", p.Detail, False, 0, 0)
                                Case NpcFaceGenPacker.PackPhase.DeletingLoose
                                    ReportSaveProgress(progress, "Removing loose files…", p.Detail, p.Max > 0, p.Max, p.Current)
                                Case NpcFaceGenPacker.PackPhase.Done
                                    ReportSaveProgress(progress, "Done.", "", False, 0, 0)
                            End Select
                        End Sub,
                        excludeEntries:=excludeList)
                End Function)

            If Not packResult.Success Then
                Return ($"(CharGen baked OK but BA2 pack failed: {packResult.ErrorMessage})", False)
            End If

            ' Dedup across flushes: same archive path appears once per flush it was rewritten in.
            ' For the user-facing count we want the DISTINCT archive files touched, not the total
            ' write operations.
            Dim distinctWritten = packResult.WrittenArchives.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
            Dim distinctSkipped = packResult.SkippedArchives.Distinct(StringComparer.OrdinalIgnoreCase).
                                      Where(Function(p) Not distinctWritten.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList()
            Dim wrote = distinctWritten.Count
            Dim skipped = distinctSkipped.Count
            Dim committed = packResult.BundlesCommitted
            Dim total = bundlesToPack.Count
            Dim flushes = packResult.FlushesCommitted
            Dim missingSources = packResult.MissingSources.Count
            Dim missingBundles = total - committed
            Dim summary As String

            ' Gate the wording on COMMITTED bundles, not on wrote/skipped archive counts. When 0
            ' bundles committed (all failed) but partial entries reached Pack, ArchivePackager can
            ' still report the existing archive as Skipped (byte-identical for whatever fragment
            ' made it through) — saying "BA2 unchanged" in that case is a half-truth that hides
            ' the failure. Branch order matters: success/unchanged use committed; failure uses
            ' missingBundles.
            If total = 0 Then
                ' Delete-only pack (mark-to-delete): no new bakes — only stripped removed NPCs' stale entries.
                summary = If(wrote > 0,
                             $"BA2 updated — stripped deleted NPC bake(s) from {wrote} archive{If(wrote = 1, "", "s")}.",
                             "BA2 unchanged — no deleted NPC bakes were present.")
            ElseIf committed = 0 Then
                summary = $"BA2 pack: 0/{total} NPC{If(total = 1, "", "s")} packed."
            ElseIf wrote = 0 AndAlso skipped > 0 Then
                ' Byte-identical: every entry of every committed bundle matched existing CRC32 →
                ' no rewrite, ArchivePackager reported the archives as Skipped.
                summary = $"BA2 unchanged ({committed}/{total} NPC{If(total = 1, "", "s")} already present byte-identical in {skipped} archive{If(skipped = 1, "", "s")})."
            Else
                ' At least one entry differed → archive(s) rewritten with the bundle entries
                ' replacing the prior CRC-mismatched ones, plus stream-copy of preserved entries.
                summary = $"Packed {committed}/{total} NPC{If(total = 1, "", "s")} into {wrote} BA2 archive{If(wrote = 1, "", "s")}" &
                          If(flushes > 1, $" ({flushes} flushes).", ".")
            End If
            If missingBundles > 0 Then
                ' Hard discrepancy: we baked N OK and only N - missingBundles fully landed in BA2.
                ' Además del conteo, se NOMBRAN los NPC afectados y el primer archivo que le falta a cada uno:
                ' el log por sí solo no alcanza (en Release no existe, Logger.Enabled=False), así que sin esto
                ' el usuario no tiene forma de saber a qué NPC volver. Se muestran los primeros 10, como el
                ' batch loose muestra 15.
                summary &= $" ⚠ {missingBundles} NPC{If(missingBundles = 1, "", "s")} failed to pack ({missingSources} file{If(missingSources = 1, "", "s")} unaccounted for)."
                If packResult.FailedBundles.Count > 0 Then
                    Dim shownFb = packResult.FailedBundles.Take(10).ToList()
                    summary &= vbCrLf & "      " & String.Join(vbCrLf & "      ", shownFb)
                    If packResult.FailedBundles.Count > shownFb.Count Then
                        summary &= vbCrLf & $"      … and {packResult.FailedBundles.Count - shownFb.Count} more."
                    End If
                End If
                ' Sus archivos sueltos NO se borraron (el bundle se descarta entero antes de empaquetar nada),
                ' así que volver a guardar reintenta sin tener que re-hornear.
                summary &= vbCrLf & "      (Those NPCs' loose files were kept, so the save can be retried.)"
            End If
            Return (summary, True)
        Catch ex As Exception
            ' Preserve the "never throws" contract — pack failures surface in the summary, not as a
            ' thrown exception that would mark the whole (already-written) save as failed.
            Return ($"(CharGen baked OK but BA2 pack failed: {ex.Message})", False)
        End Try
    End Function

    ''' <summary>Forward a save-pipeline progress update to the orchestrator's IProgress sink.
    ''' Wrapper exists so RunChargenBakeAndPack stays terse — every `progress.Report(New ...)`
    ''' call would otherwise need a 5-arg builder inline.</summary>
    Private Sub ReportSaveProgress(progress As IProgress(Of NpcOverrideSaver.SaveProgress),
                                   phase As String, detail As String,
                                   determinate As Boolean, max As Integer, current As Integer)
        If progress Is Nothing Then Return
        progress.Report(New NpcOverrideSaver.SaveProgress With {
            .Phase = phase,
            .Detail = detail,
            .Determinate = determinate,
            .Max = max,
            .Current = current
        })
    End Sub

    ''' <summary>Get the list of NPC_Manager auto-generated plugins. Uses a process-lifetime
    ''' in-memory cache (_autoGenPluginsCache) to avoid re-scanning Data\ on every Save dialog
    ''' open. The cache is populated on first call (lazy) and updated by the Save handler
    ''' (RegisterSavedPluginInCache / RefreshSavedPluginInCache) when a write succeeds, so
    ''' subsequent Save dialogs see the freshly-saved plugin without re-scanning.
    '''
    ''' Per-call work:
    '''   - target_npc_check: rebuild the ContainsTargetNpc flag for each cached plugin
    '''     (varies per call because the user might be saving a different NPC). Cheap: it's
    '''     just a Dictionary lookup against the records we already loaded once.</summary>
    Private Function ScanForAutoGeneratedPlugins(targetNpcFormID As UInteger) As List(Of SaveEsp_Form.ExistingPlugin)
        If _autoGenPluginsCache Is Nothing Then
            ' Fallback: cache wasn't seeded by Preflight (defensive — shouldn't normally happen).
            _autoGenPluginsCache = SaveEsp_Form.ScanAutoGeneratedPlugins(_dataPath, _pluginManager)
        End If

        ' Refresh the per-target-NPC flag without re-loading anything from disk. The cached
        ' entries store NpcFormIDs (built once at scan time); flipping the flag is a cheap
        ' lookup.
        For Each ep In _autoGenPluginsCache
            ep.ContainsTargetNpc = ep.NpcFormIDs IsNot Nothing AndAlso ep.NpcFormIDs.Contains(targetNpcFormID)
        Next
        Return _autoGenPluginsCache
    End Function

    ''' <summary>Slot 18 del NAM9 (VampireMorph) de un record, o Nothing si el record no lo trae.
    ''' UNA sola lectura para las DOS ramas de BuildPresetFromState: tenerla en una sola era el defecto —
    ''' el arreglo quedaba inerte justo en el camino con overlay, que es el normal.</summary>
    Private Shared Function VampireMorphFromNam9(raw As NPC_Data) As Single?
        Return SseNam9MorphMap.VampireMorphDe(raw?.Record.DeslizadoresDeCara())
    End Function


    ''' <summary>Add a newly-written plugin to the cache without re-scanning disk. Called by
    ''' the Save handler after a successful write of a NEW plugin.</summary>
    Private Sub RegisterSavedPluginInCache(savedPath As String, savedNpcFormIDs As IEnumerable(Of UInteger), isEsm As Boolean, isLight As Boolean)
        If _autoGenPluginsCache Is Nothing Then Return  ' Will be picked up at first scan.
        ' Avoid duplicates if the cache somehow already has it (defensive).
        For Each cached In _autoGenPluginsCache
            If String.Equals(cached.FullPath, savedPath, StringComparison.OrdinalIgnoreCase) Then
                Exit Sub
            End If
        Next
        ' `ids` YA son todos los NPC_ del archivo: SavedFormIDs = preservados + escritos, y el saver los
        ' resuelve a global uno por uno, asi que no hay filtrado que pueda achicar la cuenta. Coincide con el
        ' `npcTotal` del barrido de Data. Ver ExistingPlugin.NpcCount.
        Dim ids As New HashSet(Of UInteger)(savedNpcFormIDs)
        _autoGenPluginsCache.Add(New SaveEsp_Form.ExistingPlugin With {
            .FullPath = savedPath,
            .FileName = IO.Path.GetFileName(savedPath),
            .NpcCount = ids.Count,
            .NpcFormIDs = ids,
            .ContainsTargetNpc = False,
            .IsEsm = isEsm,
            .IsLight = isLight,
            .TranslatableEncoding = ComputeSavedTranslatableEncoding()
        })
    End Sub

    ''' <summary>The encoding a fresh disk re-scan would report for the plugin we just wrote.
    ''' Mirrors the writer's SNAM-tag rule: UTF-8 emits NO tag, so a re-scan sees Nothing;
    ''' any other code page emits &lt;cp:XXXX&gt;, so a re-scan sees that encoding. Without keeping
    ''' the cache in sync with this, the next Save dialog would auto-recommend the OLD encoding
    ''' (the value captured at the initial preflight scan), not the one just saved.</summary>
    Private Function ComputeSavedTranslatableEncoding() As System.Text.Encoding
        Dim enc = PluginEncodingSettings.Translatable
        If enc IsNot Nothing AndAlso enc.CodePage <> 65001 Then Return enc
        Return Nothing
    End Function

    ''' <summary>Update an existing cache entry after a successful write that targeted that
    ''' plugin (the user picked "Update existing"). Replaces NpcFormIDs/NpcCount + ESM/Light
    ''' flags with the freshly-written state so the next Save ESP dialog auto-populates the
    ''' Mark-as-master / Light checkboxes from this updated value, not the stale pre-save
    ''' snapshot.</summary>
    Private Sub RefreshSavedPluginInCache(savedPath As String, savedNpcFormIDs As IEnumerable(Of UInteger), isEsm As Boolean, isLight As Boolean)
        If _autoGenPluginsCache Is Nothing Then Return
        For Each cached In _autoGenPluginsCache
            If String.Equals(cached.FullPath, savedPath, StringComparison.OrdinalIgnoreCase) Then
                cached.NpcFormIDs = New HashSet(Of UInteger)(savedNpcFormIDs)
                ' Idem: NpcFormIDs post-save son todos los del archivo (preservados + escritos), no un
                ' subconjunto resuelto. Ver ExistingPlugin.NpcCount.
                cached.NpcCount = cached.NpcFormIDs.Count
                cached.IsEsm = isEsm
                cached.IsLight = isLight
                ' Sync the encoding to what we just wrote, so the next Save dialog auto-recommends
                ' the NEW encoding, not the stale pre-save value captured at preflight.
                cached.TranslatableEncoding = ComputeSavedTranslatableEncoding()
                Return
            End If
        Next
        ' Wasn't in cache (race condition / external edit). Treat as new.
        RegisterSavedPluginInCache(savedPath, savedNpcFormIDs, isEsm, isLight)
    End Sub

    ' Note: the scan implementation now lives in SaveEsp_Form.ScanAutoGeneratedPlugins
    ' (Shared). It is called once at startup from Preflight_Form to populate the cache,
    ' and only invoked here as a fallback if the cache wasn't seeded for some reason.

#End Region

End Class




























