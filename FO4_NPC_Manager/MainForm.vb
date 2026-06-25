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

Public Class MainForm

    Private ReadOnly _pluginManager As PluginManager
    ''' <summary>Shared record-parse services (Phase 2 split). Single owner of the ARMO/ARMA/RACE/
    ''' HDPT/NPC_ parse caches; created once in the ctor and injected into extracted render
    ''' subsystems. Replaces the per-MainForm parse caches + GetParsed* helpers.</summary>
    Private ReadOnly _ctx As NpcRenderContext
    ''' <summary>Material / texture-set / hair-palette / color-form resolution (Phase 2 split,
    ''' increment 1). Receives the shared <see cref="_ctx"/>; ApplyShapeMaterialOverrides + skin-tone
    ''' resolvers still live here pending later increments.</summary>
    Private ReadOnly _materialResolver As NpcMaterialResolver
    ''' <summary>NPC visual-state resolution (template-chain traits/inventory/model, race fallbacks,
    ''' skeleton key, leveled-NPC pick). Phase 2 split. Receives the shared context + collaborators +
    ''' IoC delegates for the MainForm-resident state it can't own (gender filter, LM-skin resolver).</summary>
    Private ReadOnly _stateResolver As NpcStateResolver
    ''' <summary>Morph + pose resolution (face/body morph resolvers, FMRS face-bone transforms,
    ''' body-weight data, race height, merged pose, facial-bone regions). Phase 2 split; skeleton
    ''' LOADING (PrepareSkeleton) + caches stay in MainForm. IoC: overlay + host-provider delegates.</summary>
    Private ReadOnly _morphPoseResolver As NpcMorphPoseResolver
    ''' <summary>FaceTint compositor EXECUTION (face-tint compose + skin SoftLight/subsurface passes,
    ''' pristine diffuse snapshot/rollback, live face-tint refresh). Phase 2 split; owns the per-process
    ''' _tintBytesCache. The skin-override live-preview fast-path stays in MainForm (coupled to
    ''' CollectArmoCandidates) and calls back into this resolver. IoC: ctx + materialResolver +
    ''' host-provider delegate + shared _appliedPresets.</summary>
    Private ReadOnly _faceTintResolver As NpcFaceTintResolver
    ''' <summary>MeshCollection/Mounting Increment 1: mounting math (mount-delta transforms for robot
    ''' chunks/sockets onto a live SkeletonInstance, host-scoped socket resolution, synthetic-skin Pipboy).
    ''' Pure data + NiflySharp, no GL/controls. The render orchestrator (RenderCurrentStateAsync) stays in
    ''' MainForm and calls this. IoC: ctx + stateResolver.</summary>
    Private ReadOnly _mountingResolver As NpcMountingResolver
    ''' <summary>MeshCollection/Mounting Increment 2: the candidate pipeline (ResolvePreviewVariant →
    ''' collect ARMO/OTFT/headpart/robot-chunk candidates → slot-conflict selection + headwear occlusion →
    ''' LoadNifShapes). Pure data + NiflySharp, no GL/controls. The render orchestrator stays in MainForm
    ''' and calls _meshCollector.ResolvePreviewVariant. IoC: ctx + materialResolver + stateResolver +
    ''' mountingResolver + Func delegates (HasFaceGenAssets, ArmoIsPowerArmor, RaceIsPowerArmor —
    ''' shared power-armor predicates kept in MainForm because the outfit/armo-universe also uses them).</summary>
    Private ReadOnly _meshCollector As NpcMeshCollector
    ''' <summary>MeshCollection Increment 3: skin-override live-preview fast-path (EditBody skin swap →
    ''' re-resolve TXST/MSWP in place + re-bake face tint/softlight, no VBO regen). Orchestrates
    ''' meshCollector + materialResolver + faceTintResolver over host.PreviewCtl. IoC: the three
    ''' resolvers + host-provider + shared _appliedPresets + Func delegates for ResolveLmSkinTemplate
    ''' and the live _previewRequestVersion token.</summary>
    Private ReadOnly _skinLivePreview As NpcSkinLivePreview
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
    ''' <see cref="TreeViewNPCs_DrawNode"/> and the random render pick.</summary>
    Private ReadOnly _selectedNpcFormIDs As New HashSet(Of UInteger)()
    ''' <summary>Anchor node for Shift-range selection (set on the last plain/Ctrl click).</summary>
    Private _multiSelectAnchorNode As TreeNode = Nothing
    ''' <summary>FormID of the NPC actually being rendered out of the selection (the random pick, or
    ''' the single selected one). Painted with the full highlight; the rest of the set gets a paler
    ''' one so the user can see which member was rolled.</summary>
    Private _currentRandomPickFormID As UInteger = 0UI
    ''' <summary>FormID whose detail tree was just built by <see cref="TreeViewNPCs_AfterSelect"/>.
    ''' Lets the debounced render skip the redundant rebuild for the single-select case (AfterSelect
    ''' already populated it). 0 = none pending; consumed-and-cleared in
    ''' <see cref="RenderFromCurrentSelection"/> so it suppresses ONLY the one render that follows
    ''' a selection — any later re-render of the same NPC still repopulates fresh.</summary>
    Private _detailsAfterSelectFormID As UInteger = 0UI
    ''' <summary>Pale highlight brush for non-picked members of a multi-selection. Lazily created,
    ''' disposed in <see cref="MainForm_FormClosing"/>.</summary>
    Private _multiSelectBrush As System.Drawing.SolidBrush = Nothing
    ''' <summary>FormIDs the tree context menu acts on (the multi-selection when the right-click
    ''' lands inside it, else just the clicked NPC). Set in <see cref="TreeViewNPCs_NodeMouseClick"/>.</summary>
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
    ''' <summary>Next object index (low 3 bytes, ≥0x800 per the FO4/xEdit new-record convention) for
    ''' a provisional draft FormID.</summary>
    Private _nextDraftObjIndex As UInteger = &H800UI
    ''' <summary>Parsed F4SE LooksMenu skin templates loaded from
    ''' Data\F4SE\Plugins\F4EE\Skin\&lt;mod&gt;\skin.json and Data\F4SE\Plugins\F4EE\Skin\Loose\*.json.
    ''' Mirrors the bundle structure of f4ee/SkinInterface.cpp:490-621 (id+name+gender+sort + per-gender
    ''' face TXST / head HDPT / rear HDPT + skin ARMO). Populated once after plugin load.</summary>
    Private _lmSkinTemplates As New List(Of LmSkinTemplate)()
    ''' <summary>NPCs directly placed in the world via ACHR records (unique characters).</summary>
    Private _directlyPlacedNPCFormIDs As New HashSet(Of UInteger)()
    ''' <summary>NPCs that appear in the game world: placed in CELLs (ACHR) or in LVLN encounter lists.</summary>
    Private _npcsInGameWorld As New HashSet(Of UInteger)()
    ''' <summary>NPCs that are referenced as template source (TPLT/TPTA) by other NPCs.</summary>
    Private _npcsUsedAsTemplates As New HashSet(Of UInteger)()
    ''' <summary>Final LVLNs: leveled NPC lists that are NOT nested inside another LVLN.</summary>
    Private _finalLVLNFormIDs As New List(Of UInteger)()
    ''' <summary>Parsed LVLN data cache keyed by FormID.</summary>
    Private _lvlnDataCache As New Dictionary(Of UInteger, LVLN_Data)()
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
    ''' <summary>Pre-computed display label per NPC FormID ("FullName (EditorID, FormID)" with
    ''' fallbacks). All inputs are FormID-stable (FullName/EditorID don't change post-load), so
    ''' no invalidation needed after initial warmup.</summary>
    Private _npcDisplayLabelCache As New Dictionary(Of UInteger, String)()
    Private _pendingTreeFilter As String = ""
    Private WithEvents SearchDebounceTimer As New System.Windows.Forms.Timer()

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
    End Sub

    ' _renderHost.TintGpuCache, _renderHost.PristineDiffusePixels and the PristinePixels nested class moved to
    ' NpcRenderHost so each preview surface owns its own caches.

    ' xEdit wbDefinitionsFO4.pas:7365-7372 mapea HDPT.DATA flags POR POSICIÓN del array
    ' wbFlags, no por el valor en los comentarios `{0x...}`. Posiciones reales:
    '   bit 0 (0x01) Playable, bit 1 (0x02) Male, bit 2 (0x04) Female,
    '   bit 3 (0x08) IsExtraPart, bit 4 (0x10) UseSolidTint, bit 5 (0x20) UsesBodyTexture.
    Friend Const HeadPartFlagUseSolidTint As Byte = &H10
    ''' <summary>Flag bit at position 3 of HDPT.DATA — "Is Extra Part". Verified against
    ''' wbDefinitionsFO4.pas:7369 (entry 4 in the wbFlags positional array). Set on HDPTs that are
    ''' addons referenced via another HDPT's HNAM (eyelashes, hairlines, etc.) rather than
    ''' standalone parts. CharGenInterface.cpp:96 filters these out when serializing a preset.</summary>
    Private Const HeadPartFlagIsExtra As Byte = &H8
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeEyes As Integer = 2
    Private Const HeadPartTypeHair As Integer = 3
    Friend Const HeadPartTypeFacialHair As Integer = 4
    Private Const HeadPartTypeHeadRear As Integer = 9

    ' The old fixed HeadwearOcclusionSlots const ({30,31,32,46,48,49} for every NPC) has been REMOVED.
    ' Head-part occlusion is now per-NPC and RACE-driven: ResolvePreviewVariant computes the slot mask from
    ' the NPC's RACE.DATA biped objects via RaceUtil.RaceHeadOcclusionMask (engine-faithful — verified vs
    ' Fallout4.exe + .esm, see [[project_re_occlusion_engine]]) and carries it on
    ' PreviewResolutionResult.HeadOcclusionMask; NpcRenderHost.ApplyRenderToggleVisibility reads that instead.

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

    ''' <summary>Per-socket info published by a host chunk via BSConnectPoint::Parents.
    ''' Cacheada por <c>publisherSockets</c> durante SRC3 indexing. Incluye el global del
    ''' socket EN EL ESPACIO DEL NIF DEL HOST (no en actor) para que el consumer pueda
    ''' componer correctamente sin mezclar coord systems:
    '''
    '''   M_mesh_consumer = host.ChunkToActor × <see cref="HostSocketGlobalT"/>
    '''
    ''' donde <c>host.ChunkToActor</c> mapea chunk-internal space del host a actor world.
    ''' Para casos donde <see cref="ParentFoundInHostNif"/>=False (parent name no existe en
    ''' el NIF del host), el consumer cae al path skeleton fallback (actor.parentBone ×
    ''' socket.local) — apropiado para sockets que referencian bones del actor skel directo.</summary>
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
        ''' <summary>Skeleton-scoped socket fallback para Path B (cuando Path A no aplica —
        ''' chunks sin C-X NiNode interno). Resuelto desde el flat <c>skeletonSockets</c>
        ''' (SRC1 RACE.ANAM + SRC2 BPTD.MODL) por nombre exacto en CollectRobotChunkCandidates.
        '''
        ''' Su <c>ParentBoneName</c> usa nomenclatura actor skel (con suffixes indexed como
        ''' <c>Arm1|0</c>), distinto al publisher chunk socket que usa chunk-internal naming
        ''' sin suffix. Path B usa ESTE para evitar mezclar chunk-internal-parent-naming con
        ''' actor-skel-bone-lookup, que rompía multi-instance Mr Handy attachments donde el
        ''' chunk dice <c>parent='Arm1'</c> pero actor.skel solo tiene <c>Arm1|0/1/2</c>.
        '''
        ''' Nothing cuando el skeleton no publica el socket name (raro — típicamente solo
        ''' chunks root que son los primeros publishers de su AP). En ese caso Path B cae al
        ''' MountSocket publisher como último recurso.
        '''
        ''' Separación estructural (per OpenAI Vuelta 17): el MountSocket original hacía 2
        ''' trabajos distintos (publisher coord system para Path A, skeleton bone naming para
        ''' Path B). Persistir dos representaciones distintas en el candidate cierra la
        ''' sobrecarga conceptual.</summary>
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
        Public UseSolidTint As Boolean
        Public UsesBodyTexture As Boolean
        Public FaceGenTexturePrefix As String = ""
        Public Order As Integer
        ' From HDPT NAM0/NAM1 pairs (face head parts only, normally empty otherwise)
        Public RaceMorphTriPath As String = ""
        Public ChargenMorphTriPath As String = ""
        ''' <summary>Per-bone scale deltas from the ARMA's BSMP/BSMB/BSMS block (matching this
        ''' NPC's gender). Engine-side these are added on top of RACE.BSMS to shape the outfit
        ''' (cinched waist, wider hips, etc.). Nothing when the ARMA has no BSMS or gender mismatch.</summary>
        Public ArmaBoneScaleDeltas As List(Of ARMA_BoneScaleDelta) = Nothing
        ''' <summary>HeadRear only (effectivePartType=9): cuando DictKey fue redirigido al variant
        ''' *_faceBones.nif, el _faceBones vanilla trae material genérico (basehumanfemaleskin) en
        ''' lugar del material part-específico (basehumanfemalerear). LoadNifShapes copia el material
        ''' del .nif base a los shapes del _faceBones (matching por nombre con sufijo "_faceBones"
        ''' removido). Sólo se popula para HeadRear; otros HeadParts mantienen sus materiales originales.</summary>
        Public BaseDictKeyForFaceBones As String = ""
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
        ''' HairTop AND (HairLong OR FaceGenHead)). Antes esto descartaba el head part entero;
        ''' ahora se acepta con flag para que "Render headwear OFF" pueda destaparlo runtime.</summary>
        Public IsOccludedByHeadwear As Boolean = False
        ''' <summary>HeadPart candidates only: True cuando el candidato fue colectado via la
        ''' recursión HNAM de un parent (parentPartType≥0 en CollectHeadPartCandidate), no como
        ''' entry top-level de NPC.PNAM/RACE defaults. Vanilla engine renderiza HNAM-extras aunque
        ''' un headwear oculte al parent — sólo FaceGenHead full-face los tapa. Independiente del
        ''' raw type del extra: el flag de "entró via HNAM" es el que determina la regla de
        ''' addon, no su PartType. Caso 2026-05-17: hair nuevo cuya HNAM declara una hairline raw=3
        ''' (no Misc) — sin este flag caía en la rama Hair de occlusion y la gorra la ocultaba.</summary>
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
    '''
    ''' Sources del rango (auditadas, ver discusión de sesión 2026-05-03):
    '''   - 101..113 / 201..213: enum oficial BSDismemberBodyPartType del NIF (BP_SECTIONCAP_*,
    '''     BP_TORSOCAP_*). Documentado en niftools/nif.xml. Certeza estructural.
    '''   - 100, 102, 103: NO documentados por Bethesda ni en el enum NIF. Aparecen sólo en el
    '''     .xrc de BS-OS etiquetados "Gore". Confianza alta (BS-OS es la herramienta de
    '''     autoría de Bethesda) pero NO certeza spec. Marcados como Tentative para que sea
    '''     auditable y removible si aparece evidencia contraria.</summary>
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
        ''' <summary>Shape reference -> chargen morph TRI path (from HDPT NAM0=2/NAM1).</summary>
        Public ReadOnly ShapeChargenTriPaths As New Dictionary(Of IRenderableShape, String)
        ''' <summary>Shape reference -> race morph TRI path (from HDPT NAM0=0/NAM1, expression file).</summary>
        Public ReadOnly ShapeRaceMorphTriPaths As New Dictionary(Of IRenderableShape, String)
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
        ''' <summary>Per-shape (Fase 2): the OWN worn biped-slot mask of the candidate this shape came from
        ''' (bit N-30 = biped slot N, same convention as <see cref="SlotConflictResolver.OccupiedSlots"/>).
        ''' Stored only for worn-item (Kind=Outfit) shapes; head parts / skin have no entry (own slots = 0).
        ''' ApplyRenderToggleVisibility rebuilds the per-segment occlusion mask (IRenderableShape.CoveredSlotsMask)
        ''' from these every apply, scoped to the items CURRENTLY rendered — so a render toggle that hides an
        ''' item (e.g. Pipboy under "Render armor" OFF) drops its slots from the occluding set and the segments
        ''' it covered re-appear. NOT a static covered mask: covered-by-OTHERS is recomputed at toggle time
        ''' (ORDER / other-items rule), excluding the shape's own group via <see cref="ShapeSlotGroup"/>.</summary>
        Public ReadOnly ShapeOwnSlots As New Dictionary(Of IRenderableShape, UInteger)
        ''' <summary>Per-shape occlusion group id (one per candidate; all shapes of a candidate share it).
        ''' Used by ApplyRenderToggleVisibility so an item never occludes its OWN segments: covered-by-others
        ''' ORs the own-slot masks of rendered groups whose id differs (engine owner-slot branch 0x14035E22B).
        ''' This is what keeps a slot SHARED by two items (Pipboy + a Pipboy-aware outfit both declaring 60)
        ''' working — occupied&amp;~own would strip the shared bit; OR-of-other-groups keeps it.</summary>
        Public ReadOnly ShapeSlotGroup As New Dictionary(Of IRenderableShape, Integer)
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
        ' [TEST: TPLT-traits-bucket] Face-appearance fields live in the Traits bucket (moved here from
        ' the former ModelAnimationState). xEdit's wbTemplateFlags lists 15 bits but doesn't pin each
        ' NPC_ subrecord to a specific bit. These ride "Use Traits" since the CK Traits tab covers the
        ' actor's visual identity (race, skin, head, hair) — same conceptual bucket as RNAM/WNAM/MWGT.
        ' The OBTS fields below joined this bucket after measurement (see their note); the now-empty
        ' Model/Animation bucket was removed, so a revert means re-introducing it.
        Public HeadTextureFormID As UInteger
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        Public TextureLightingColor As Color = Color.Empty
        Public HeadPartFormIDs As New List(Of UInteger)
        ' [TEST: TPLT-traits-bucket] NPC ObjectTemplate (OBTE/OBTS) MOVED here from ModelAnimationState.
        ' The old comment claimed OBTS was "model assembly, closer to Model/Animation". MEASURED wrong:
        ' GutsyTemplateProbe over all 4365 load-order NPC_ shows ZERO inherit OBTS via Use Model/Animation
        ' (bit6); the rank variants (encMrGutsy02/03/04, SentryBot/Assaultron/Synth encounters) reach the
        ' OBTS holder ONLY via Use Traits (bit0) — 225 NPCs go from empty→rendered, 0 regressions. Matches
        ' the CK, where the "Object Template" section lives on the Traits tab. So OBTS rides the Traits walk.
        ''' <summary>Legacy flat list of OMOD FormIDs from combo #0 (kept for back-compat).</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
        ''' <summary>Full OBTE/OBTS combinations — used by the robot-chunk path resolver.</summary>
        Public ObjectTemplateCombinations As New List(Of FO4_Base_Library.NPC_ObjectTemplateCombination)
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
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        ''' <summary>QNAM RGBA. Alpha is the body SoftLight intensity (vanilla = 1.0 by convention,
        ''' synced with the slot-12 SkinTone tint layer's Value). When the editor mutates slot-12
        ''' Value or Color, ResolveNpcSkinToneColor packs the new (Color, Value/100) back here so
        ''' face and body stay symmetric — the engine itself reads QNAM.A as the body softlight
        ''' opacity (wbDefinitionsFO4.pas:10776 wbFloatRGBA QNAM 'Texture lighting'), so this
        ''' preserves engine-fidelity while reflecting the user's edit.</summary>
        Public TextureLightingColor As Color = Color.Empty
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
        Public ObjectTemplateCombinations As New List(Of FO4_Base_Library.NPC_ObjectTemplateCombination)
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

    ''' <summary>Toggle the chargen vertex morph resolver in-place without re-extracting
    ''' geometry. WM-style granular: mutate Intent.MorphResolver, mark RenderDirtyFlags.Morphs,
    ''' InvalidateRender. The pipeline's needsMorphUpdate path re-runs PipelineStep_Morphs
    ''' which restarts from NifLocalVertices (raw pre-skinning) and applies the plan fresh.
    '''
    ''' Toggling OFF sets MorphResolver=Nothing: per the PipelineStep_Morphs / ApplyMorphPlan
    ''' contract, a null resolver resets geom.Vertices to NifLocalVertices (no stale deltas).</summary>
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

    ''' <summary>ARMA Sculpt Data (Bone Scale Delta, xEdit "Bone Scale Delta") application formula.
    ''' ADITIVO en los 3 ejes desde 2026-06-19: s = race_s + arma_d (componente a componente, X incluido).
    ''' Base RE del builder del engine FUN_140652230 (combina aditivamente weight_base + sculpt_delta);
    ''' X se aplica (no se descarta) porque el render skin usa matrices de nodo (X-capaz) y la data de
    ''' Fallout4.esm trae DeltaX deliberado en antebrazos (BoS X=+0.20; Raider X=-0.19). Antes era H3
    ''' multiplicativo (s = race_s·(1+arma_d), hardcoded 2026-04-27). ⚠ El test diferencial CK que
    ''' confirme aditivo-vs-multiplicativo a nivel byte sigue PENDIENTE (consumidor del +0x50 GPU/oculto).</summary>
    ''' <summary>Toggle body-weight pose (MWGT × BSMS + MRSV + ARMA sculpt aditivo). Triggers granular
    ''' MarkDirty(Pose) — no full reload.</summary>
    Private Sub CheckBoxApplyBodyWeight_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBodyWeight.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        RebuildAndApplyMergedPose()
    End Sub

    ''' <summary>Range-Modifier clamp model for BuildBodyWeightPose. The RACE.BSMS Range Modifier
    ''' (Min/Max Y/Z) bounds the bone-scale delta; this enum selects HOW it's applied. Only Y/Z
    ''' (Range has no X). ARMA sculpt (Layer 4) is always applied AFTER the clamp.
    '''   Off           = no clamp (legacy behavior).
    '''   ClampWeightL1 = clamp the weight delta to [Min,Max] BEFORE MRSV.
    '''   ClampFinal    = clamp the total weight+MRSV delta.
    '''   ClampBoth     = clamp the weight delta AND the total — keeps the bone always inside the band.</summary>

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

    ''' <summary>Toggle ARMA sculpt (SCLP per-bone scaling). When OFF, every shape — including
    ''' [A] over-armor consumers that would normally receive the source's SCLP — falls back to
    ''' the base skeleton (no SCLP amplifier). Diagnostic toggle to compare A/B with vs without
    ''' sculpt on the same NPC.</summary>
    Private Sub CheckBoxApplySculpt_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplySculpt.CheckedChanged
        If _renderHost Is Nothing Then Return
        _renderHost.Toggles = RenderToggles.FromMainCheckBoxes(Me)
        RebuildAndApplyMergedPose()
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


    Private Sub TriggerFullRender()
        If _renderHost.LastRenderedState Is Nothing Then Return
        ' Reuse the existing flow used by other CheckedChanged handlers that need a full reload.
        ' We piggyback on the outfit selection refresh path to force the render pipeline.
        RenderCurrentStateAsyncWrapper()
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
        ' Base pose (sin sculpt) → skeleton base.
        Dim basePose = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, host.LastSkeletonInstance, Nothing)
        ' Los bone-morphs van a la capa MorphDeltaTransform (no a la pose). Así la capa Delta
        ' (pose/animación) queda libre y el morph sobrevive a un futuro ApplyPose por frame.
        host.LastSkeletonInstance.ApplyBoneMorphPose(basePose)
        ' NNAM comp anti-propagación (post-pase tras ApplyBoneMorphPose) — ver ApplyNeckNnamCompensation.
        _morphPoseResolver.ApplyNeckNnamCompensation(host.LastSkeletonInstance)
        ' [MOUNTDELTA-PREPASS] Repopular MountDelta desde la cache del render inicial (re-write
        ' idempotente; ApplyBoneMorphPose no borra el mount).
        _mountingResolver.ApplyMountPlanForActor(host.LastSkeletonInstance, host.LastRenderData)

        ' Head skeleton: re-pose WITH body weight AND NNAM neck-fat (FASE 1 2026-06-24). Antes el NNAM se
        ' suprimía acá porque escala el hueso ANCESTRO "Neck" y propagaba la escala a toda la cara (balloon).
        ' Ahora BuildBodyWeightPose mete la COMPENSACIÓN anti-propagación (S⁻¹ a los hijos directos de
        ' "Neck"), así la escala queda solo en los verts del "Neck" y la cara NO se infla. fmrsEnabled
        ' sigue honrado (la cabeza conserva sus morphs FMRS).
        If host.LastHeadSkeletonInstance IsNot Nothing AndAlso Not ReferenceEquals(host.LastHeadSkeletonInstance, host.LastSkeletonInstance) Then
            Dim headPose = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, host.LastHeadSkeletonInstance, Nothing)
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
            Dim poseForArma = _morphPoseResolver.BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, armaSkel, sculpt)
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
        intent.MarkDirty(RenderDirtyFlags.Pose, host.LastRenderData.Shapes)
        host.PreviewCtl.InvalidateRender()
    End Sub

#Region "Animation bar (combo + Select Animation + play/frames) — live preview on the main render"
    ' El behavior es de la RAZA (ver [[arch_race_behavior_resolution]]); el clip se reproduce con
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
    Private _savedEnabledLoadLooksmenu As Boolean
    Private _savedEnabledSaveLooksmenu As Boolean
    Private _savedEnabledCopyLook As Boolean
    Private _savedEnabledPasteLook As Boolean
    Private _savedEnabledSavePlugin As Boolean
    Private _savedEnabledBuildCharGen As Boolean
    Private _savedEnabledSaveSceneNif As Boolean

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
            _savedEnabledLoadLooksmenu = ButtonLoadLooksmenu.Enabled
            _savedEnabledSaveLooksmenu = ButtonSaveLooksmenu.Enabled
            _savedEnabledCopyLook = ButtonCopyLook.Enabled
            _savedEnabledPasteLook = ButtonPasteLook.Enabled
            _savedEnabledSavePlugin = ButtonSavePlugin.Enabled
            _savedEnabledBuildCharGen = ButtonBuildCharGen.Enabled
            _savedEnabledSaveSceneNif = ButtonSaveSceneNif.Enabled
            _editorBarCapturedDuringPlay = True
            ButtonEditFace.Enabled = False
            ButtonEditBody.Enabled = False
            ButtonEditOutfit.Enabled = False
            ButtonLoadLooksmenu.Enabled = False
            ButtonSaveLooksmenu.Enabled = False
            ButtonCopyLook.Enabled = False
            ButtonPasteLook.Enabled = False
            ButtonSavePlugin.Enabled = False
            ButtonBuildCharGen.Enabled = False
            ButtonSaveSceneNif.Enabled = False
        ElseIf _editorBarCapturedDuringPlay Then
            ButtonEditFace.Enabled = _savedEnabledEditFace
            ButtonEditBody.Enabled = _savedEnabledEditBody
            ButtonEditOutfit.Enabled = _savedEnabledEditOutfit
            ButtonLoadLooksmenu.Enabled = _savedEnabledLoadLooksmenu
            ButtonSaveLooksmenu.Enabled = _savedEnabledSaveLooksmenu
            ButtonCopyLook.Enabled = _savedEnabledCopyLook
            ButtonPasteLook.Enabled = _savedEnabledPasteLook
            ButtonSavePlugin.Enabled = _savedEnabledSavePlugin
            ButtonBuildCharGen.Enabled = _savedEnabledBuildCharGen
            ButtonSaveSceneNif.Enabled = _savedEnabledSaveSceneNif
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
            Dim npc = RecordParsers.ParseNPCLight(npcRec, npcRec.SourcePluginName, _pluginManager)
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
                                                    BehaviorClipEnumerator.DetectAdditiveFlags(clipsRef, AddressOf LoadAnimHkxBytes)
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
        ComboAnim.Items.Add("(enumerando animaciones...)")
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
                                                    Dim key = $"{rb.RaceFormID:X8}|{If(npc.IsFemale, "F", "M")}"
                                                    If Not seen.Add(key) Then Continue For         ' distinct race+gender once
                                                    If _animRaceCache.ContainsKey(key) Then Continue For
                                                    rb.IsFemale = npc.IsFemale
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
        Using dlg As New AnimationPicker_Form(_animClips, isFemale, If(current?.AnimationFile, ""))
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
        SelectAnimationClip(_animComboClips(ComboAnim.SelectedIndex - 1))
    End Sub

    Private Sub SelectAnimationClip(clip As ResolvedAnimationClip)
        If clip Is Nothing OrElse _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing Then
            Logger.LogLazy(Function() $"[ANIM-BAR] SelectAnimationClip abort: clip={(clip IsNot Nothing)} host={(_renderHost IsNot Nothing)} liveSkel={(_renderHost?.LastSkeletonInstance IsNot Nothing)}")
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
            Logger.LogLazy(Function() $"Clip not found: {clip.AnimationFile}")
            Return
        End If
        Try
            _animSession = HkxPoseImportSession.Create(clipSkelBytes, clipBytes, _renderHost.LastSkeletonInstance, clip.AnimationFile, clip.SourceSkeletonPath, additiveHint:=clip.IsAdditive)
            _animPlayer = New HkxAnimationPlayer(_animSession) With {.PoseName = "Animation"}
            Logger.LogLazy(Function() $"[ANIM-BAR] session OK frames={_animSession.FrameCount} tracks={_animSession.TrackCount} frameDur={_animSession.FrameDuration:0.####} skelSrc={_animSession.SkeletonSource}")
        Catch ex As Exception
            _animSession = Nothing
            Logger.LogLazy(Function() $"[ANIM-BAR] session create FAILED clip='{clip.AnimationFile}': {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        ' Clip seleccionado = PAUSADO en frame 0 (no es "playing"). PlayingAnimation sigue la lógica
        ' del botón Play (True solo al reproducir), igual que WM — si no, el RenderTimer queda parado
        ' en pausa y no se puede rotar/zoom. Acá NO se setea True.
        Dim maxFrame = Math.Max(0, _animSession.FrameCount - 1)
        _animSuppress = True
        SliderAnimFrame.Minimum = 0 : SliderAnimFrame.Maximum = maxFrame : SliderAnimFrame.Value = 0
        _animSuppress = False
        SliderAnimFrame.Enabled = maxFrame > 0
        NumericAnimFrameMs.Enabled = maxFrame > 0
        ButtonAnimPlay.Enabled = maxFrame > 0
        ApplyAnimPlaybackInterval()   ' setea el ms/frame por defecto desde el FrameDuration del clip (editable)
        ApplyAnimFrame(0)
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
        fps = Math.Min(CDbl(NumericAnimFrameMs.Maximum), Math.Max(CDbl(NumericAnimFrameMs.Minimum), fps))
        _animSuppressMs = True
        NumericAnimFrameMs.Value = CDec(Math.Round(fps, MidpointRounding.AwayFromZero))
        _animSuppressMs = False
        Dim appliedFps = Math.Max(1.0, CDbl(NumericAnimFrameMs.Value))
        If _animPlayer IsNot Nothing Then _animPlayer.TargetFps = appliedFps
    End Sub

    Private Sub NumericAnimFrameMs_ValueChanged(sender As Object, e As EventArgs) Handles NumericAnimFrameMs.ValueChanged
        If _animSuppressMs Then Return
        Dim fps = Math.Max(1.0, CDbl(NumericAnimFrameMs.Value))   ' el numeric ahora es FPS
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

    Private Sub ApplyAnimFrame(frame As Integer)
        If _animPlayer Is Nothing OrElse _renderHost Is Nothing OrElse _renderHost.LastSkeletonInstance Is Nothing OrElse _renderHost.LastRenderData Is Nothing Then Return
        Try
            ' El player cachea la pose por frame → scrub/play barato. La pose es por nombre de bone
            ' → se aplica igual al skeleton base y a los clones per-ARMA (sculpt). ApplyPose toca SOLO
            ' la capa DeltaTransform → el morph (MorphDelta) y el mount sobreviven.
            Dim pose = _animPlayer.PoseForFrame(frame)
            _renderHost.LastSkeletonInstance.ApplyPose(pose)
            ' Head skeleton (body-weight-free) also gets the clip pose, so the FaceGen head + head parts
            ' follow the animation instead of floating. Separate instance from the body skel.
            If _renderHost.LastHeadSkeletonInstance IsNot Nothing AndAlso Not ReferenceEquals(_renderHost.LastHeadSkeletonInstance, _renderHost.LastSkeletonInstance) Then
                _renderHost.LastHeadSkeletonInstance.ApplyPose(pose)
            End If
            If _renderHost.LastSkelByArma IsNot Nothing Then
                For Each kv In _renderHost.LastSkelByArma
                    If kv.Value IsNot Nothing Then kv.Value.ApplyPose(pose)
                Next
            End If
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
                For Each bn In {"Neck", "HeadTwist", "HeadNod", "LUPPERARM", "Chest"}
                    Dim hb As HierarchiBone_class = Nothing
                    If Not instD.SkeletonDictionary.TryGetValue(bn, hb) OrElse hb Is Nothing Then Continue For
                    Dim bnL = bn, frL = frame
                    Dim oS = fmt(hb.OriginalLocaLTransform), mS = fmt(hb.MountDeltaTransform), dS = fmt(hb.DeltaTransform), phS = fmt(hb.MorphDeltaTransform)
                    Dim bw = hb.OriginalGetGlobalTransform.Translation, pw = hb.GetGlobalTransform.Translation
                    Logger.LogLazy(Function() $"[ANIM-BONE] f={frL} '{bnL}'{Environment.NewLine}    O    ={oS}{Environment.NewLine}    Mount={mS}{Environment.NewLine}    Morph={phS}{Environment.NewLine}    Delta={dS}{Environment.NewLine}    bindWorld.T=({bw.X:F3},{bw.Y:F3},{bw.Z:F3})  poseWorld.T=({pw.X:F3},{pw.Y:F3},{pw.Z:F3})")
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
    End Sub

    ' El slider tiny (TinySliderTextBox) muestra el frame inline y es el único control de frame (scrub).
    Private Sub SliderAnimFrame_ValueChanged(sender As Object, e As EventArgs) Handles SliderAnimFrame.ValueChanged
        If _animSuppress Then Return
        ApplyAnimFrame(CInt(Math.Round(SliderAnimFrame.Value)))
    End Sub

    Private Sub ButtonAnimPlay_Click(sender As Object, e As EventArgs) Handles ButtonAnimPlay.Click
        If IsAnimPlayingNow() Then
            StopAnimPlayback()
        ElseIf _animPlayer IsNot Nothing AndAlso SliderAnimFrame.Maximum > 0 Then
            SetPlayingAnimation(True)
            SetEditorBarEnabledForPlayback(True)   ' deshabilita la barra de botones del editor hasta el Stop
            ' Durante el playback el slider es solo indicador de progreso (OnAnimPlaybackFrame le sigue
            ' seteando .Value por código): se bloquea el scrub manual y se re-habilita en StopAnimPlayback.
            SliderAnimFrame.Enabled = False
            Dim fps = Math.Max(1.0, CDbl(NumericAnimFrameMs.Value))
            _animPlayer.TargetFps = fps
            _animPlayer.Start(CInt(Math.Round(SliderAnimFrame.Value)))
            _animPlayer.BeginIdlePlayback(AddressOf OnAnimPlaybackFrame)
            ButtonAnimPlay.Text = "⏸"
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
        If SliderAnimFrame IsNot Nothing Then SliderAnimFrame.Enabled = SliderAnimFrame.Maximum > 0
        If ButtonAnimPlay IsNot Nothing Then ButtonAnimPlay.Text = "▶"
        If _animOverBudget Then NumericAnimFrameMs.ForeColor = SystemColors.ControlText : _animOverBudget = False
    End Sub

    ''' <summary>Callback del loop Application.Idle del player (hilo UI, igual que el viejo Tick).
    ''' Recibe el frame ya elegido por reloj real (el player ya dedup-ea por _lastShownFrame);
    ''' actualiza el slider, mide el render y aplica. Reemplaza al viejo AnimPlayTimer_Tick.</summary>
    Private Sub OnAnimPlaybackFrame(frame As Integer)
        If _animPlayer Is Nothing OrElse SliderAnimFrame.Maximum <= 0 Then StopAnimPlayback() : Return
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
        _materialResolver = New NpcMaterialResolver(_ctx, AddressOf ApplyPresetOverlayToNpcData)
        _stateResolver = New NpcStateResolver(_ctx, _materialResolver, _appliedPresets, _lvlnDataCache,
                                              Function() CurrentGenderFilter, AddressOf ResolveLmSkinTemplate)
        _morphPoseResolver = New NpcMorphPoseResolver(_ctx, AddressOf ApplyPresetOverlayToNpcData, Function() _renderHost, _appliedPresets)
        _faceTintResolver = New NpcFaceTintResolver(_ctx, _materialResolver, Function() _renderHost, _appliedPresets)
        _mountingResolver = New NpcMountingResolver(_ctx, _stateResolver)
        _meshCollector = New NpcMeshCollector(_ctx, _materialResolver, _stateResolver, _mountingResolver,
                                              AddressOf HasFaceGenAssets, AddressOf ArmoIsPowerArmor, AddressOf RaceIsPowerArmor)
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
        ComboBoxPreviewMode.SelectedIndex = 0
        ComboBoxGender.SelectedIndex = 0
    End Sub

    ''' <summary>Translate each sidecar entry's "Master.esp|HEX6" key into a global FormID via
    ''' the active load order and seed <see cref="_appliedPresets"/> with a minimal
    ''' <see cref="LooksmenuLoader.LooksmenuPreset"/> carrying just the BodyMorphs + SkinTemplate.
    '''
    ''' <para>Entries whose master isn't in the load order resolve to FormID 0 and are skipped —
    ''' same semantics as the LM JSON loader's UnresolvedHeadParts handling. Last-loaded-wins
    ''' across sidecars (iteration order = dict order = insertion order from preflight). Edit
    ''' Body / Load LM / Paste all mutate or replace the synthesized preset in-place after
    ''' hydration, so this is just the starting state.</para></summary>
    Private Sub HydrateAppliedPresetsFromSidecars(sidecars As Dictionary(Of String, BssliderSidecar.SidecarFile))
        If sidecars Is Nothing OrElse sidecars.Count = 0 Then Return
        For Each pluginKv In sidecars
            Dim sidecar = pluginKv.Value
            If sidecar Is Nothing OrElse sidecar.Npcs Is Nothing Then Continue For
            For Each entryKv In sidecar.Npcs
                Dim entry = entryKv.Value
                If entry Is Nothing OrElse Not entry.HasAnything Then Continue For

                Dim globalFid = LooksmenuLoader.ResolveFormIdentifier(entryKv.Key, _pluginManager)
                If globalFid = 0UI Then Continue For  ' Master not in load order; nothing to apply.

                ' If a later sidecar (or some other code path) already hydrated this FormID,
                ' merge in the slider dict + SkinTemplate without clobbering whatever may
                ' already be on the existing overlay (e.g. Has* flags from a prior hydration).
                Dim existing As LooksmenuLoader.LooksmenuPreset = Nothing
                If Not _appliedPresets.TryGetValue(globalFid, existing) OrElse existing Is Nothing Then
                    existing = New LooksmenuLoader.LooksmenuPreset()
                    _appliedPresets(globalFid) = existing
                End If
                If entry.BodyMorphs IsNot Nothing Then
                    For Each bm In entry.BodyMorphs
                        existing.BodyMorphSliders(bm.Key) = bm.Value
                    Next
                End If
                If Not String.IsNullOrEmpty(entry.SkinTemplateId) Then
                    existing.SkinTemplateId = entry.SkinTemplateId
                End If
            Next
        Next
    End Sub

    Private Sub MainForm_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        SearchDebounceTimer.Interval = 250
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4
        ' NOTE: plugin text encoding (InitializeForGame + SetLanguage + ApplyOverrideIni) AND the
        ' Logger init both live in Program.Main, BEFORE the preflight loads any plugin — mirror
        ' of xEdit's "configure → load → edit" order. Do NOT re-init either here; that would run
        ' AFTER the preflight already loaded plugins, and would lose every startup-time log.
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
        LoadDataAsync()
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
    End Sub

    Private Async Sub LoadDataAsync()
        ' Plugins + BA2/BSA archives were loaded by Preflight_Form before MainForm was even
        ' constructed. _pluginManager and FilesDictionary_class are already populated. Here we
        ' just parse the NPC records out of the loaded plugins and populate the tree.
        Try
            ToolStripProgressBar1.Visible = False

            SetStatus("Parsing NPC records...")
            Await Task.Run(Sub() ParseAllNPCs())

            PopulateNPCTree()

            SetStatus($"Loaded {_directlyPlacedNPCFormIDs.Count} placed NPCs + {_finalLVLNFormIDs.Count} leveled lists from {_pluginManager.Plugins.Count} plugins")

            ' Warm the anim-clip cache for the races present in the load order on a background thread, so the
            ' first NPC selection of each race does NOT pay the ~16 s behavior walk on the UI thread. Bounded:
            ' only the DISTINCT race+gender combos actually present, clip METADATA only (no raw anim bytes).
            PreloadAnimRacesInBackground()

        Catch ex As Exception
            SetStatus($"Error: {ex.Message}")
            MessageBox.Show(ex.ToString(), "Load Error", MessageBoxButtons.OK, MessageBoxIcon.Error)
        End Try
    End Sub


    Private Sub ParseAllNPCs()
        _allNPCs.Clear()
        ' Drop any parse/skeleton-byte caches from a prior load order before re-establishing the NPC
        ' universe — they're keyed by FormID/(race,gender) which could collide across plugin sets.
        InvalidateParseCaches()

        ' Bulk-parse uses the full ParseNPC. The cache (_allNPCs / _ctx.NpcCache) is consumed
        ' by the render path (CreateOwnTraitsState / CreateOwnInventoryState /
        ' CreateOwnModelAnimationState) which reads SkinFormID, DefaultOutfitFormID,
        ' HeadTextureFormID, HairColorFormID, HeadPartFormIDs, ObjectTemplateOMODFormIDs,
        ' weights, etc. — fields that ParseNPCLight skips. Switching this loop to Light caused
        ' silent render regressions (NPCs with no headparts/skin/outfit).
        '
        ' GC pressure from bulk allocation is now mitigated by lazy properties on NPC_Data:
        ' the 21 collection fields don't allocate until their setter/getter is touched. A
        ' parse run only allocates the lists that the source NPC actually populates.
        Dim sw = System.Diagnostics.Stopwatch.StartNew()
        Dim npcRecords = _pluginManager.GetNPCs()
        Dim getNpcsMs = sw.ElapsedMilliseconds
        sw.Restart()
        For Each rec In npcRecords
            Try
                Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
                Dim npc = RecordParsers.ParseNPC(rec, pluginName, _pluginManager)
                _allNPCs.Add(npc)
            Catch
            End Try
        Next
        Dim parseMs = sw.ElapsedMilliseconds
        sw.Restart()
        ' Resolve inherited FullName for NPCs that inherit BaseData from a template
        ResolveInheritedFullNames()
        Dim resolveMs = sw.ElapsedMilliseconds
        sw.Restart()
        _allNPCs.Sort(Function(a, b) String.Compare(a.ToString(), b.ToString(), StringComparison.OrdinalIgnoreCase))
        Dim sortMs = sw.ElapsedMilliseconds
        sw.Restart()
        RebuildTreeModelCache()
        Dim cacheMs = sw.ElapsedMilliseconds
        sw.Stop()
    End Sub

    ''' <summary>For NPCs with no FullName that inherit BaseData from a template, resolve the name from the chain.</summary>
    Private Sub ResolveInheritedFullNames()
        For Each npc In _allNPCs
            If npc.FullName <> "" Then Continue For
            If Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then Continue For

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData)
            Dim resolved = ResolveInheritedFullName(sourceFormID, New HashSet(Of UInteger)())
            If resolved <> "" Then npc.FullName = resolved
        Next
    End Sub

    Private Function ResolveInheritedFullName(formID As UInteger, visited As HashSet(Of UInteger)) As String
        If formID = 0UI OrElse visited.Contains(formID) Then Return ""
        visited.Add(formID)

        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing Then Return ""

        Select Case rec.Header.Signature
            Case "NPC_"
                ' Light parse: we only need FullName + TemplateFlags + TPTA for chain walk.
                Dim npc = RecordParsers.ParseNPCLight(rec, "", _pluginManager)
                If npc.FullName <> "" Then Return npc.FullName
                ' Follow BaseData chain if this NPC also inherits
                If NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then
                    Return ResolveInheritedFullName(NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData), visited)
                End If
            Case "LVLN"
                ' Pick first NPC entry from the LVLN to get a representative name
                Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)
                For Each entry In lvln.Entries
                    If entry.FormID = 0UI Then Continue For
                    Dim resolved = ResolveInheritedFullName(entry.FormID, visited)
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
        For Each npc In _ctx.NpcCache.Values
            _npcSearchableCache(npc.FormID) = NpcDisplayHelpers.BuildNpcSearchableText(npc)
            _npcDisplayLabelCache(npc.FormID) = NpcDisplayHelpers.BuildNpcDisplayLabel(npc)
        Next
    End Sub

    ''' <summary>Filter the skin ARMO universe (built once at plugin load) by the race+gender of
    ''' the NPC currently being edited. An ARMO qualifies iff (a) at least one ARMA child has the
    ''' gender's skin TXST set (so the candidate is actually a body skin, not a placeholder) AND
    ''' (b) ARMO.RNAM matches OR at least one ARMA's RNAM/AdditionalRaces matches the NPC's race.
    ''' Returned tuples are (FormID, DisplayName) ready for direct assignment to a ComboBox.
    ''' DisplayName falls back to EditorID then to FormID-hex.</summary>
    Friend Function GetSkinArmoCandidates(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String))
        Dim outList As New List(Of (FormID As UInteger, DisplayName As String))
        For Each armoFID In _skinArmoUniverse
            Dim armo As ARMO_Data
            Try
                armo = _ctx.GetParsedArmo(armoFID)
            Catch
                Continue For
            End Try
            If armo Is Nothing Then Continue For
            ' Power-armor gate (same rule as the render): don't offer a power-armor skin for a non-PA race.
            If ArmoIsPowerArmor(armoFID) AndAlso Not RaceIsPowerArmor(npcRaceFID) Then Continue For
            Dim raceMatch = (armo.RaceFormID = npcRaceFID)
            Dim genderMatch As Boolean = False
            For Each addon In armo.ArmorAddons
                Dim arma As ARMA_Data
                Try
                    arma = _ctx.GetParsedArma(addon.ArmaFormID)
                Catch
                    Continue For
                End Try
                If arma Is Nothing Then Continue For
                Dim armaRaceOk = ArmorAddonMatchesRace(arma, npcRaceFID)
                If armaRaceOk Then raceMatch = True
                Dim txst = If(isFemale, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID)
                If armaRaceOk AndAlso txst <> 0UI Then
                    genderMatch = True
                End If
            Next
            If raceMatch AndAlso genderMatch Then
                Dim display As String = If(Not String.IsNullOrEmpty(armo.FullName), armo.FullName,
                                            If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID,
                                               armoFID.ToString("X8")))
                outList.Add((armoFID, display))
            End If
        Next
        Return outList.OrderBy(Function(x) x.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()
    End Function

    ''' <summary>Walks the same chain the render uses (raw NPC.HCLF -> Traits template chain
    ''' -> RACE.{Male,Female}DefaultHairColorFormID with own-gender-first fallback) and returns
    ''' the HCLF FormID the engine would actually paint with for this NPC. Used by Edit Face to
    ''' pre-select the combo at form-open WITHOUT mutating the overlay (preserve semantic stays
    ''' intact). Returns 0 if the NPC has no resolvable HCLF anywhere up the chain.</summary>
    Friend Function ResolveEffectiveHairColorFormID(npcFormID As UInteger) As UInteger
        If npcFormID = 0UI Then Return 0UI
        Dim warnings As New List(Of String)
        Dim traits = _stateResolver.ResolveTraitsStateFromNPC(npcFormID, New HashSet(Of UInteger)(), warnings)
        If traits Is Nothing Then Return 0UI
        If traits.HairColorFormID <> 0UI Then Return traits.HairColorFormID

        Dim raceRec = _pluginManager.GetRecord(traits.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 0UI
        Dim race = _ctx.ParseRaceCached(raceRec)
        If race Is Nothing Then Return 0UI

        Dim ownGender = If(traits.IsFemale, race.FemaleDefaultHairColorFormID, race.MaleDefaultHairColorFormID)
        Dim otherGender = If(traits.IsFemale, race.MaleDefaultHairColorFormID, race.FemaleDefaultHairColorFormID)
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
        Dim armo As ARMO_Data

        Try
            armo = _ctx.GetParsedArmo(armoFID)
        Catch
            Return armoFID.ToString("X8")
        End Try
        If armo Is Nothing Then Return ""
        If Not String.IsNullOrEmpty(armo.FullName) Then Return armo.FullName
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
            If npc.SkinFormID <> 0UI Then _skinArmoUniverse.Add(npc.SkinFormID)
        Next
        ' RACE.WNAM contributions — iterate AllRecords filtering by signature; ParseRACE only on
        ' matches. Vanilla FO4 has ~150 races so the cost is negligible.
        For Each kvp In _pluginManager.AllRecords
            Dim rec = kvp.Value
            If rec Is Nothing OrElse rec.Header.Signature <> "RACE" Then Continue For
            Try
                Dim race = _ctx.ParseRaceCached(rec)
                If race.SkinFormID <> 0UI Then _skinArmoUniverse.Add(race.SkinFormID)
            Catch
            End Try
        Next
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
    ''' AND carries a world mesh (with the renderer's male/female fallback, MainForm.vb:6914-6915).
    ''' <para>Filter is PER-ARMA, never by ARMO.RaceFormID: most vanilla clothing has RNAM=HumanRace,
    ''' so filtering by the ARMO would drop outfits valid for ghouls/other races (the closed
    ''' ghoul-outfit bug). Known deferred edge case: ghouls wearing human outfits whose ARMA doesn't
    ''' list GhoulRace won't pass this filter (project_ghoul_armor_race_filter_deferred).</para>
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
        Dim listedIds As New HashSet(Of UInteger)(otftList.Select(Function(x) x.FormID))
        For Each d In _outfitDrafts
            If d.FormID = OutfitDraft.PreviewDraftFormID Then Continue For   ' throwaway picker-preview draft
            If listedIds.Contains(d.FormID) Then Continue For                 ' override of an already-listed OTFT — dedupe
            result.Add((d.FormID, d.EditorID & "  [draft]"))
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
            Dim armo As ARMO_Data
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
            For Each addon In armo.ArmorAddons
                Dim arma As ARMA_Data
                Try
                    arma = _ctx.GetParsedArma(addon.ArmaFormID)
                Catch
                    Continue For
                End Try
                If arma Is Nothing Then Continue For
                Dim armaRaceOk = ArmorAddonMatchesRace(arma, npcRaceFID)
                If Not armaRaceOk Then Continue For
                If arma.FemaleMeshPath <> "" OrElse arma.MaleMeshPath <> "" Then Return True
            Next
        Next
        Return False
    End Function

    ''' <summary>Display label for an OTFT FormID: EditorID if any, else hex. OTFTs carry no FULL name
    ''' (OTFT_Data is FormID + EditorID + INAM array).</summary>
    Friend Function GetOutfitDisplayName(otftFID As UInteger) As String
        If otftFID = 0UI Then Return ""
        Dim draft = TryGetOutfitDraft(otftFID)
        If draft IsNot Nothing Then Return draft.EditorID
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

    ''' <summary>Allocate a fresh provisional FormID for a NEW outfit draft (0xFF high byte +
    ''' object index ≥0x800, FO4/xEdit new-record convention). The writer rewrites it to the real
    ''' plugin self-index FormID at save time.</summary>
    Friend Function AllocateDraftFormID() As UInteger
        Dim fid As UInteger = OutfitDraft.DraftFormIdHighByte Or _nextDraftObjIndex
        _nextDraftObjIndex += 1UI
        Return fid
    End Function

    ''' <summary>The biped slot mask an armor addon effectively occupies: the ARMA's own BOD2 mask, or the
    ''' owning ARMO's BOD2 when the ARMA declares none. SINGLE source for armor slot-footprint logic — used
    ''' by both the render (<see cref="CollectArmoCandidates"/>, once per ARMA candidate) and the Edit Outfit
    ''' item enumeration (<see cref="GetArmoItemCandidates"/>, OR-ed across an ARMO's race-valid addons). Both
    ''' MUST go through here so the Create tab's slot-conflict marking always matches what the render resolves
    ''' (do not re-inline the ARMA-vs-ARMO choice anywhere else).</summary>
    Friend Shared Function EffectiveArmaSlotMask(arma As ARMA_Data, armo As ARMO_Data) As UInteger
        Return If(arma.SlotMask <> 0UI, arma.SlotMask, armo.SlotMask)
    End Function

    ''' <summary>Short source-plugin (esp/esm) name for a FormID, shown next to the ID in the Edit Outfit
    ''' lists and used by their filters. Not-yet-saved drafts → "(new)"; otherwise the originating plugin
    ''' via <see cref="PluginManager.GetOriginatingPluginName"/> (ESL-aware high-byte scheme).</summary>
    Friend Function GetOutfitPluginName(formID As UInteger) As String
        If OutfitDraft.IsDraftFormID(formID) Then Return "(new)"
        Return If(_pluginManager.GetOriginatingPluginName(formID), "")
    End Function

    ''' <summary>Selectable ARMO items (armor/clothing pieces) for the Edit Outfit "Create" tab,
    ''' filtered by (race, gender): every ARMO that has a race-valid ARMA (<see cref="ArmorAddonMatchesRace"/>)
    ''' carrying a world mesh for the gender (male/female with the renderer's fallback). Returns
    ''' (FormID, DisplayName, SlotMask, Plugin). SlotMask is the effective slot footprint — the union of
    ''' <see cref="EffectiveArmaSlotMask"/> across the ARMO's race-valid addons (same per-addon choice the
    ''' render makes), so the conflict resolver sees exactly what the render does. Cached per (race, gender)
    ''' — the full ARMO+ARMA sweep is the costly part; ARMO/ARMA parses are globally cached so each record
    ''' is parsed once.</summary>
    Friend Function GetArmoItemCandidates(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        Dim cacheKey = (npcRaceFID, isFemale)
        Dim cached As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String)) = Nothing
        If _armoItemCandidateCache.TryGetValue(cacheKey, cached) Then Return cached

        Dim outList As New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        Dim armoRecs = _pluginManager.GetRecordsOfType("ARMO")
        If armoRecs IsNot Nothing Then
            For Each rec In armoRecs
                If rec Is Nothing Then Continue For
                Dim armoFID = rec.Header.FormID
                Dim armo As ARMO_Data
                Try
                    armo = _ctx.GetParsedArmo(armoFID)
                Catch
                    Continue For
                End Try
                If armo Is Nothing Then Continue For
                ' Power-armor gate (same rule as the render): don't offer ArmorTypePower pieces for a
                ' non-power-armor race — they'd mount wrong without a frame.
                If ArmoIsPowerArmor(armoFID) AndAlso Not RaceIsPowerArmor(npcRaceFID) Then Continue For
                ' Effective slot footprint, matching the render (CollectArmoCandidates:7319): per addon take
                ' the ARMA's own BOD2 mask, falling back to the ARMO's only when the ARMA declares none, and
                ' UNION across every race-valid addon that has a mesh. The render builds one candidate per
                ' ARMA and feeds them all to SlotConflictResolver; for the Create tab — one piece per ARMO —
                ' the union is the equivalent footprint, so two pieces overlapping on ANY slot conflict the
                ' same way they do in-game. (The old "ARMO BOD2 first, first ARMA only" path used a declared
                ' mask that can diverge from the ARMA's real slots, so same-slot pieces weren't eliminated.)
                Dim armoSlot = ComputeArmoEffectiveSlotMask(armo, npcRaceFID, isFemale)
                If armoSlot.Valid Then
                    Dim disp As String = If(Not String.IsNullOrEmpty(armo.FullName), armo.FullName,
                                            If(Not String.IsNullOrEmpty(armo.EditorID), armo.EditorID, armoFID.ToString("X8")))
                    outList.Add((armoFID, disp, armoSlot.Mask, GetOutfitPluginName(armoFID)))
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
                Dim tArmo As ARMO_Data
                Try
                    tArmo = _ctx.GetParsedArmo(terminalFID)
                Catch
                    Continue For
                End Try
                Dim tr = ComputeArmoEffectiveSlotMask(tArmo, npcRaceFID, isFemale)
                If tr.Valid Then
                    anyValid = True
                    unionMask = unionMask Or tr.Mask
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

    ''' <summary>Like <see cref="GetArmoItemCandidates"/> but ALSO appends the author-built LVLI drafts
    ''' (🎲, own), fresh on every call (not cached — they change as the user builds them). The Edit Outfit
    ''' picker uses this so own leveled lists are addable as outfit pieces / nestable into other LVLs. Each
    ''' draft's slot footprint = UNION of <see cref="ComputeArmoEffectiveSlotMask"/> over the terminals its
    ''' entries can produce (<see cref="EnumerateLeveledTerminalsAll"/>, draft-aware/recursive).</summary>
    Friend Function GetArmoItemCandidatesWithDrafts(npcRaceFID As UInteger, isFemale As Boolean) As List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))
        Dim baseList = GetArmoItemCandidates(npcRaceFID, isFemale)
        If _leveledListDrafts.Count = 0 Then Return baseList
        Dim result As New List(Of (FormID As UInteger, DisplayName As String, SlotMask As UInteger, Plugin As String))(baseList)
        For Each d In _leveledListDrafts
            Dim unionMask As UInteger = 0UI
            For Each t In EnumerateLeveledTerminalsAll(d.FormID)
                unionMask = unionMask Or ComputeArmoEffectiveSlotMask(_ctx.GetParsedArmo(t), npcRaceFID, isFemale).Mask
            Next
            result.Add((d.FormID, d.EditorID & "  [LVL]", unionMask, "(new)"))
        Next
        Return result
    End Function

    ''' <summary>Effective slot footprint of an ARMO for a race/gender: UNION of <see cref="EffectiveArmaSlotMask"/>
    ''' over its race-valid addons that carry a gender mesh, falling back to the ARMO's own BOD2 when no addon
    ''' contributes. Valid = at least one addon had a mesh for this race/gender. Shared by the ARMO and the
    ''' LVLI-terminal paths of <see cref="GetArmoItemCandidates"/> so both compute the slot identically.</summary>
    Private Function ComputeArmoEffectiveSlotMask(armo As ARMO_Data, npcRaceFID As UInteger, isFemale As Boolean) As (Mask As UInteger, Valid As Boolean)
        If armo Is Nothing Then Return (0UI, False)
        Dim slotMask As UInteger = 0UI
        Dim valid As Boolean = False
        For Each addon In armo.ArmorAddons
            Dim arma As ARMA_Data
            Try
                arma = _ctx.GetParsedArma(addon.ArmaFormID)
            Catch
                Continue For
            End Try
            If arma Is Nothing Then Continue For
            If Not ArmorAddonMatchesRace(arma, npcRaceFID) Then Continue For
            Dim genderMesh = If(isFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If genderMesh = "" Then genderMesh = If(arma.MaleMeshPath <> "", arma.MaleMeshPath, arma.FemaleMeshPath)
            If genderMesh <> "" Then
                valid = True
                slotMask = slotMask Or EffectiveArmaSlotMask(arma, armo)
            End If
        Next
        If slotMask = 0UI Then slotMask = armo.SlotMask
        Return (slotMask, valid)
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
            For Each itemFID In RecordParsers.ParseOTFT(rec, _pluginManager).ItemFormIDs
                If seen.Add(itemFID) AndAlso IsLeveledItem(itemFID) Then result.Add(itemFID)
            Next
        Next
        _outfitLeveledListCache = result
        Return result
    End Function

    ''' <summary>WYSIWYG outfit preview: render the NPC wearing <paramref name="overrideValue"/> into the
    ''' Edit Outfit picker's own <see cref="NpcRenderHost"/> using the EXACT same pipeline as the main
    ''' preview (<see cref="RenderInHostAsync"/> → CollectMeshCandidates → SelectWinningCandidates →
    ''' skinning/morphs/pose/tints). There is NO separate "lightweight" outfit resolver anymore — the
    ''' picker and the main viewer resolve outfits through one path, so what the picker shows is what the
    ''' main render produces (OMOD addon-index resolution, ARMO WorldModel fallback, slot-conflict
    ''' elimination, chunk mounting, body weight, all included).
    '''
    ''' Semantics of <paramref name="overrideValue"/> (mirrors <c>DefaultOutfitFormIDOverride</c>):
    '''   Nothing → preserve the raw NPC.DOFT · Some(0) → no outfit (naked) · Some(fid) → OTFT / draft.
    ''' The override is HOST-SCOPED (set on <paramref name="host"/>, applied in ResolveNPCBaseState): it
    ''' does NOT touch the shared <see cref="_appliedPresets"/>, so browsing outfits in the picker never
    ''' disturbs the main render's committed state. Cancel needs no restore; on OK the caller
    ''' (<see cref="ButtonEditOutfit_Click"/>) commits the chosen value to the overlay and re-renders main.</summary>
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
        If draft IsNot Nothing Then Return New List(Of UInteger)(draft.ItemFormIDs)
        Dim rec = _pluginManager.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "OTFT" Then Return New List(Of UInteger)
        Return New List(Of UInteger)(RecordParsers.ParseOTFT(rec, _pluginManager).ItemFormIDs)
    End Function

    ''' <summary>True if <paramref name="fid"/> is a leveled item list — a real LVLI record OR an author-built
    ''' LVLI draft (which lives outside the PluginManager, so GetRecord wouldn't see it).</summary>
    Friend Function IsLeveledItem(fid As UInteger) As Boolean
        If fid = 0UI Then Return False
        If IsOwnLeveledDraft(fid) Then Return True
        Dim rec = _pluginManager.GetRecord(fid)
        Return rec IsNot Nothing AndAlso rec.Header.Signature = "LVLI"
    End Function

    ''' <summary>Leveled-list resolver injected into <see cref="OutfitResolver"/> so the lib's ONE sampler/
    ''' enumerator also sees our in-memory LVLI drafts (which aren't in the PluginManager). Returns the draft
    ''' as an <see cref="LVLI_Data"/> view when <paramref name="formID"/> is an own draft; Nothing otherwise
    ''' (the lib then resolves it as a real record). This is what removed the app-side duplicate of the
    ''' leveled sampling algorithm — the app supplies only the draft DATA, never the logic.</summary>
    Private Function ResolveLeveledDraftView(formID As UInteger) As LVLI_Data
        Dim d = TryGetLeveledListDraft(formID)
        If d Is Nothing Then Return Nothing
        Dim data As New LVLI_Data With {.FormID = d.FormID, .EditorID = d.EditorID, .ChanceNone = d.ChanceNone, .Flags = d.FlagsByte()}
        For Each e In d.Entries
            data.Entries.Add(New LVLI_Entry With {.FormID = e.RefFormID, .Level = e.Level, .Count = e.Count, .ChanceNone = e.ChanceNone})
        Next
        ' Drafts carry no LLKC (FilterKeywords stays empty) — same as the old app-side sampler.
        Return data
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
        For Each e In d.Entries
            If e.RefFormID = targetFid Then Return True
            If IsOwnLeveledDraft(e.RefFormID) AndAlso LeveledDraftReaches(e.RefFormID, targetFid, visited) Then Return True
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
        For Each itemFid In draft.ItemFormIDs
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

    ''' <summary>True if the draft contains at least one LVLI item.</summary>
    Friend Function DraftHasLeveled(draft As OutfitDraft) As Boolean
        If draft Is Nothing Then Return False
        Return draft.ItemFormIDs.Any(Function(f) IsLeveledItem(f))
    End Function

    ''' <summary>Sample ONE realization of an LVLI for the Edit Outfit picker: the terminal ARMO FormIDs +
    ''' their UNION effective slot mask (for the piece's display/conflict, approach A — the LVLI behaves as
    ''' its current sample). Called on Add and Reroll; the result is cached on the piece/draft so the preview
    ''' is stable between renders. The draft persists the LVLI FormID, not this realization.</summary>
    Friend Function SampleLeveledRealization(lvliFid As UInteger, npcRaceFID As UInteger, isFemale As Boolean) As (Terminals As List(Of UInteger), SlotMask As UInteger)
        Dim terminals = SampleLeveledTerminals(lvliFid)
        Dim mask As UInteger = 0UI
        For Each t In terminals
            mask = mask Or ComputeArmoEffectiveSlotMask(_ctx.GetParsedArmo(t), npcRaceFID, isFemale).Mask
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
            If String.Equals(d.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Return False
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
            If String.Equals(d.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Return False
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
        If String.IsNullOrEmpty(_dataPath) Then Return
        Dim baseSkinDir = Path.Combine(_dataPath, "F4SE", "Plugins", "F4EE", "Skin")
        If Not Directory.Exists(baseSkinDir) Then Return
        ' Per-plugin templates: Skin\<pluginName>\skin.json
        For Each plugin In _pluginManager.Plugins
            Dim p = Path.Combine(baseSkinDir, plugin.FileName, "skin.json")
            If File.Exists(p) Then LmSkinTemplateLoader.LoadFromFile(p, _pluginManager, _lmSkinTemplates)
        Next
        ' Loose templates: Skin\Loose\*.json
        Dim looseDir = Path.Combine(baseSkinDir, "Loose")
        If Directory.Exists(looseDir) Then
            For Each p In Directory.EnumerateFiles(looseDir, "*.json", SearchOption.TopDirectoryOnly)
                LmSkinTemplateLoader.LoadFromFile(p, _pluginManager, _lmSkinTemplates)
            Next
        End If
    End Sub

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

        ' Collect NPCs placed in the world (ACHR records from CELL/WRLD groups)
        Dim placedNPCs = _pluginManager.GetPlacedNPCFormIDs()
        _directlyPlacedNPCFormIDs.UnionWith(placedNPCs)
        _npcsInGameWorld.UnionWith(placedNPCs)

        ' Parse and cache all LVLN records; track which LVLNs are nested inside others
        Dim nestedLVLNFormIDs As New HashSet(Of UInteger)()
        Dim allLVLNRecords = _pluginManager.GetRecordsOfType("LVLN")

        For Each rec In allLVLNRecords
            Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)
            _lvlnDataCache(lvln.FormID) = lvln

            For Each entry In lvln.Entries
                If entry.FormID = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.FormID)
                If entryRec IsNot Nothing AndAlso entryRec.Header.Signature = "LVLN" Then
                    nestedLVLNFormIDs.Add(entry.FormID)
                End If
            Next
        Next

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
        For Each rec In allLVLNRecords
            CollectNPCsFromLVLNRecursive(rec.Header.FormID, _npcsInGameWorld, New HashSet(Of UInteger)())
        Next

        ' Warm _lvlnLeavesCache: pre-compute flattened NPC FormID list for every LVLN. Recursion
        ' memoizada via ComputeAndCacheLVLNLeaves — sub-LVLNs ya cacheadas se leen del cache, no
        ' se re-walkean. Costo total: O(total entries across all LVLNs). Una sola vez al startup;
        ' PopulateNPCTree luego sólo hace dictionary lookups O(1) por LVLN.
        For Each lvlnFid In _lvlnDataCache.Keys
            ComputeAndCacheLVLNLeaves(lvlnFid, New HashSet(Of UInteger)())
        Next

        ' Scan all NPCs to find which are used as template sources
        For Each npc In _allNPCs
            If npc.TemplateFormID <> 0UI Then
                Dim rec = _pluginManager.GetRecord(npc.TemplateFormID)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(npc.TemplateFormID)
                End If
            End If
            For Each kvp In npc.TemplateActorFormIDs
                If kvp.Value = 0UI Then Continue For
                Dim rec = _pluginManager.GetRecord(kvp.Value)
                If rec IsNot Nothing AndAlso rec.Header.Signature = "NPC_" Then
                    _npcsUsedAsTemplates.Add(kvp.Value)
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
                                              $"templateFlags=0x{n.TemplateFlags:X4} " &
                                              $"useTraits={NpcTemplateHelpers.HasTemplateFlag(n.TemplateFlags, NPC_TemplateCategory.Traits)} " &
                                              $"useModelAnim={NpcTemplateHelpers.HasTemplateFlag(n.TemplateFlags, NPC_TemplateCategory.ModelAnimation)} " &
                                              $"TPLT=0x{n.TemplateFormID:X8}")
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
        Dim lvln = RecordParsers.ParseLVLN(rec, _pluginManager)

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    result.Add(entry.FormID)
                Case "LVLN"
                    CollectNPCsFromLVLNRecursive(entry.FormID, result, visited)
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
    ''' Template bases = used as a TPLT/TPTA source; Unused = not in-world, not a template source,
    ''' and not a CharGen face preset (ACBS bit 0x04).</summary>
    Private Function NpcMatchesCategoryFilter(n As NPC_Data, showUnique As Boolean, showGeneric As Boolean,
                                              showTemplate As Boolean, showUnused As Boolean) As Boolean
        ' ACBS bit 0x04 = "Is CharGen Face Preset" (xEdit wbDefinitionsFO4); same named constant the
        ' Save-ESP / preset paths use elsewhere in this file (e.g. ~13483, ~14360, ~14586).
        Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
        Dim inWorld = _npcsInGameWorld.Contains(n.FormID)
        Dim ownFace = Not NpcTemplateHelpers.NpcInheritsVisualAppearance(n)
        If showUnique AndAlso inWorld AndAlso ownFace Then Return True
        If showGeneric AndAlso inWorld AndAlso Not ownFace Then Return True
        If showTemplate AndAlso _npcsUsedAsTemplates.Contains(n.FormID) Then Return True
        If showUnused AndAlso Not inWorld AndAlso Not _npcsUsedAsTemplates.Contains(n.FormID) _
           AndAlso (n.AcbsFlags And AcbsBitIsCharGenFacePreset) = 0UI Then Return True
        Return False
    End Function

    ''' <summary>Check if this NPC has any LVLN in its direct TPLT or TPTA references.
    ''' These NPCs produce different results each time they're resolved (different face, gender, etc).</summary>
    Private Function NpcHasLeveledTemplates(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False

        ' Check TPLT
        If npc.TemplateFormID <> 0UI Then
            Dim rec = _pluginManager.GetRecord(npc.TemplateFormID)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        End If

        ' Check TPTA entries
        For Each kvp In npc.TemplateActorFormIDs
            If kvp.Value = 0UI Then Continue For
            Dim rec = _pluginManager.GetRecord(kvp.Value)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "LVLN" Then Return True
        Next

        Return False
    End Function

    Private Sub PopulateNPCTree(Optional filter As String = "")
        If InvokeRequired Then
            Invoke(Sub() PopulateNPCTree(filter))
            Return
        End If
        If IsNothing(_ctx) Then Return

        If (_ctx.NpcCache Is Nothing OrElse _ctx.NpcCache.Count = 0) AndAlso _allNPCs.Count > 0 Then
            RebuildTreeModelCache()
        End If

        Dim normalizedFilter = If(filter, "").Trim()
        ' "Only changed" filter: when ticked, restrict the tree to NPCs in the dirty set (bold ones),
        ' combined with the text filter. Applies to both placed NPCs and LVLN leaf NPCs.
        Dim onlyChanged As Boolean = CheckBoxOnlyChanged IsNot Nothing AndAlso CheckBoxOnlyChanged.Checked
        ' Section 1 category filters (additive union). Read once here, never inside the lambda.
        ' Null-guarded because PopulateNPCTree can run before Designer init: "Unique faces" defaults
        ' TRUE when its checkbox is null so very-early calls reproduce today's behavior; the others
        ' default FALSE.
        Dim showUnique As Boolean = CheckBoxCatUnique Is Nothing OrElse CheckBoxCatUnique.Checked
        Dim showGeneric As Boolean = CheckBoxCatGeneric IsNot Nothing AndAlso CheckBoxCatGeneric.Checked
        Dim showTemplate As Boolean = CheckBoxCatTemplate IsNot Nothing AndAlso CheckBoxCatTemplate.Checked
        Dim showUnused As Boolean = CheckBoxCatUnused IsNot Nothing AndAlso CheckBoxCatUnused.Checked

        TreeViewNPCs.SuspendLayout()
        TreeViewNPCs.BeginUpdate()
        TreeViewNPCs.Nodes.Clear()

        Try
            ' === Section 1: NPCs grouped by plugin ===
            ' Which NPCs appear here is now driven by the category checkboxes in the filter row
            ' (Unique faces / Generic / Template bases / Unused), additive union — an NPC shows if it
            ' matches ANY ticked category. Default = "Unique faces" only, which reproduces the prior
            ' behavior: in-world NPCs that define their own visual appearance. (NPCs that inherit
            ' Traits or ModelAnimation from a template are "Generic"; those used only as TPLT/TPTA
            ' sources are "Template bases"; the rest are "Unused".) An own-appearance NPC reached via
            ' an LVLN appears in BOTH this plugin group and under its LVLN node in section 2.
            Dim pluginSectionNpcs = _allNPCs.
                Where(Function(n) NpcMatchesCategoryFilter(n, showUnique, showGeneric, showTemplate, showUnused) AndAlso
                                   (Not onlyChanged OrElse _dirtyNpcs.Contains(n.FormID)) AndAlso
                                   (normalizedFilter.Length = 0 OrElse MatchesNpcFilter(n, Nothing, normalizedFilter))).
                GroupBy(Function(n) If(n.PluginName, "Unknown")).
                OrderBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

            For Each pluginGroup In pluginSectionNpcs
                Dim pluginNode As TreeNode = Nothing
                Dim matchCount = 0

                For Each npc In pluginGroup.OrderBy(Function(n) n.ToString(), StringComparer.OrdinalIgnoreCase)
                    If pluginNode Is Nothing Then
                        pluginNode = New TreeNode(pluginGroup.Key) With {
                            .Name = $"PLUGIN_{pluginGroup.Key}",
                            .Tag = Nothing
                        }
                    End If

                    Dim displayLabel As String = Nothing
                    If Not _npcDisplayLabelCache.TryGetValue(npc.FormID, displayLabel) Then
                        displayLabel = NpcDisplayHelpers.BuildNpcDisplayLabel(npc)
                    End If
                    Dim npcNode = New TreeNode(displayLabel) With {
                        .Name = $"NPC_{npc.FormID:X8}",
                        .Tag = npc
                    }
                    pluginNode.Nodes.Add(npcNode)
                    matchCount += 1
                Next

                If pluginNode IsNot Nothing Then
                    pluginNode.Text = $"{pluginGroup.Key} ({matchCount})"
                    TreeViewNPCs.Nodes.Add(pluginNode)
                    If normalizedFilter.Length > 0 OrElse onlyChanged Then pluginNode.Expand()
                End If
            Next

            Dim value As LVLN_Data = Nothing
            ' === Section 2: Final Leveled NPC Lists (encounter spawns) ===
            ' Cada LVLN se cuelga con sus NPC entries como hijos (recursión flatten via
            ' CollectLVLNLeafNpcIds). El usuario puede expandir el LVLN y elegir un NPC específico,
            ' o clickear el LVLN para random roll (handler diferencia por Tag type). Sin dedup:
            ' un NPC puede aparecer bajo CADA LVLN que lo lista (regla del usuario 2026-05-18 —
            ' útil para ver qué LVLNs enrolan a un mismo NPC).
            If _finalLVLNFormIDs.Count > 0 Then
                Dim visibleLvlns As New List(Of (FormID As UInteger,
                                                 Record As PluginRecord,
                                                 Data As LVLN_Data,
                                                 VisibleLeaves As List(Of NPC_Data)))

                For Each fid In _finalLVLNFormIDs
                    Dim rec = _pluginManager.GetRecord(fid)
                    Dim lvln = If(_lvlnDataCache.TryGetValue(fid, value), value, Nothing)
                    If rec Is Nothing OrElse lvln Is Nothing Then Continue For

                    Dim visibleLeaves As List(Of NPC_Data) = Nothing
                    If onlyChanged OrElse normalizedFilter.Length > 0 Then
                        visibleLeaves = New List(Of NPC_Data)
                        Dim leaves As List(Of UInteger) = Nothing
                        If Not _lvlnLeavesCache.TryGetValue(fid, leaves) Then leaves = New List(Of UInteger)

                        For Each leafFid In leaves
                            Dim leafNpc As NPC_Data = Nothing
                            If Not _ctx.NpcCache.TryGetValue(leafFid, leafNpc) Then Continue For
                            If onlyChanged AndAlso Not _dirtyNpcs.Contains(leafFid) Then Continue For
                            If normalizedFilter.Length > 0 AndAlso Not MatchesNpcFilter(leafNpc, Nothing, normalizedFilter) Then Continue For
                            visibleLeaves.Add(leafNpc)
                        Next

                        If onlyChanged Then
                            If visibleLeaves.Count = 0 Then Continue For
                        ElseIf normalizedFilter.Length > 0 AndAlso visibleLeaves.Count = 0 AndAlso Not NpcDisplayHelpers.MatchesRecordFilter(rec, normalizedFilter) Then
                            Continue For
                        End If
                    End If

                    visibleLvlns.Add((fid, rec, lvln, visibleLeaves))
                Next

                ' Group final LVLNs by source plugin
                Dim lvlnsByPlugin = visibleLvlns.
                    GroupBy(Function(x) If(x.Record.SourcePluginName, "Unknown")).
                    OrderBy(Function(g) g.Key, StringComparer.OrdinalIgnoreCase)

                For Each pluginGroup In lvlnsByPlugin
                    Dim pluginNode As TreeNode = Nothing
                    Dim matchCount = 0

                    For Each item In pluginGroup.OrderBy(Function(x) x.Data.EditorID, StringComparer.OrdinalIgnoreCase)
                        If pluginNode Is Nothing Then
                            pluginNode = New TreeNode($"[LVLN] {pluginGroup.Key}") With {
                                .Name = $"LVLN_PLUGIN_{pluginGroup.Key}",
                                .Tag = Nothing
                            }
                        End If

                        Dim label = If(item.Data.EditorID <> "", item.Data.EditorID, item.FormID.ToString("X8"))

                        Dim lvlnNode = New TreeNode(label) With {
                            .Name = $"LVLN_{item.FormID:X8}",
                            .Tag = item.Data
                        }

                        ' Colgar cada NPC leaf del LVLN como hijo seleccionable. Recurse en nested
                        ' LVLNs para aplanar el árbol — el usuario ve los NPCs concretos, no
                        ' sub-LVLNs intermedios. SIN dedup contra otros LVLNs: si el NPC está en
                        ' múltiples lvl lists, aparece bajo cada una. Leaves vienen del cache
                        ' precomputado (_lvlnLeavesCache) — O(1) lookup en lugar de recursión.
                        Dim childMatchCount = 0
                        If item.VisibleLeaves IsNot Nothing Then
                            For Each leafNpc In item.VisibleLeaves
                                Dim childLabel As String = Nothing
                                If Not _npcDisplayLabelCache.TryGetValue(leafNpc.FormID, childLabel) Then
                                    childLabel = NpcDisplayHelpers.BuildNpcDisplayLabel(leafNpc)
                                End If
                                Dim childNode = New TreeNode(childLabel) With {
                                    .Name = $"NPC_{leafNpc.FormID:X8}",
                                    .Tag = leafNpc
                                }
                                lvlnNode.Nodes.Add(childNode)
                                childMatchCount += 1
                            Next
                        Else
                            Dim leaves As List(Of UInteger) = Nothing
                            If Not _lvlnLeavesCache.TryGetValue(item.FormID, leaves) Then leaves = New List(Of UInteger)
                            For Each leafFid In leaves
                                Dim leafNpc As NPC_Data = Nothing
                                If Not _ctx.NpcCache.TryGetValue(leafFid, leafNpc) Then Continue For
                                Dim childLabel As String = Nothing
                                If Not _npcDisplayLabelCache.TryGetValue(leafFid, childLabel) Then
                                    childLabel = NpcDisplayHelpers.BuildNpcDisplayLabel(leafNpc)
                                End If
                                Dim childNode = New TreeNode(childLabel) With {
                                    .Name = $"NPC_{leafNpc.FormID:X8}",
                                    .Tag = leafNpc
                                }
                                lvlnNode.Nodes.Add(childNode)
                                childMatchCount += 1
                            Next
                        End If

                        pluginNode.Nodes.Add(lvlnNode)
                        matchCount += 1
                        ' Auto-expand to reveal the surviving children when filtering by text or by
                        ' "Only changed" (so the dirty NPCs show without a manual expand).
                        If childMatchCount > 0 AndAlso (normalizedFilter.Length > 0 OrElse onlyChanged) Then lvlnNode.Expand()
                    Next

                    If pluginNode IsNot Nothing AndAlso pluginNode.Nodes.Count > 0 Then
                        pluginNode.Text = $"[LVLN] {pluginGroup.Key} ({matchCount})"
                        TreeViewNPCs.Nodes.Add(pluginNode)
                        If normalizedFilter.Length > 0 OrElse onlyChanged Then pluginNode.Expand()
                    End If
                Next
            End If
        Finally
            TreeViewNPCs.EndUpdate()
            TreeViewNPCs.ResumeLayout()
        End Try
    End Sub


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
        Dim lvln As LVLN_Data = Nothing
        If _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            For Each entry In lvln.Entries
                If entry.FormID = 0UI Then Continue For
                Dim entryRec = _pluginManager.GetRecord(entry.FormID)
                If entryRec Is Nothing Then Continue For
                Select Case entryRec.Header.Signature
                    Case "NPC_"
                        result.Add(entry.FormID)
                    Case "LVLN"
                        result.AddRange(ComputeAndCacheLVLNLeaves(entry.FormID, inProgress))
                End Select
            Next
        End If
        inProgress.Remove(lvlnFormID)
        _lvlnLeavesCache(lvlnFormID) = result
        Return result
    End Function

    Private Function GetNpcTemplateSummary(npc As NPC_Data) As String
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return ""
        Dim parts As New List(Of String)
        For Each boxedCategory In [Enum].GetValues(GetType(NPC_TemplateCategory))
            Dim category = CType(boxedCategory, NPC_TemplateCategory)
            If NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then
                parts.Add(NpcManagerFormat.GetTemplateCategoryLabel(category))
            End If
        Next
        If parts.Count = 0 Then Return ""
        Return "tmpl: " & String.Join(", ", parts)
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

        For Each boxedCategory In [Enum].GetValues(GetType(NPC_TemplateCategory))
            Dim category = CType(boxedCategory, NPC_TemplateCategory)
            If Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then Continue For

            Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, category)
            If sourceFormID = 0UI Then Continue For

            dependencies.Add(New KeyValuePair(Of UInteger, String)(sourceFormID, NpcManagerFormat.GetTemplateCategoryLabel(category)))
        Next

        Return dependencies
    End Function

    Private Function BuildTemplateTreeNode(sourceId As UInteger,
                                           dependencyEdge As TemplateDependencyEdge,
                                           dependencyMap As Dictionary(Of UInteger, List(Of TemplateDependencyEdge)),
                                           npcById As IReadOnlyDictionary(Of UInteger, NPC_Data),
                                           filter As String,
                                           path As HashSet(Of UInteger)) As TreeNode
        Dim selfMatches As Boolean

        Dim node As TreeNode
        If npcById.ContainsKey(sourceId) Then
            Dim npc = npcById(sourceId)
            selfMatches = MatchesNpcFilter(npc, dependencyEdge, filter)
            node = New TreeNode(NpcDisplayHelpers.GetNpcNodeDisplayText(npc, dependencyEdge)) With {
                .Name = $"NPC_{npc.FormID:X8}",
                .Tag = npc
            }
        Else
            Dim sourceRec = _pluginManager.GetRecord(sourceId)
            If sourceRec Is Nothing OrElse sourceRec.Header.Signature <> "LVLN" Then Return Nothing

            selfMatches = NpcDisplayHelpers.MatchesRecordFilter(sourceRec, filter)
            node = New TreeNode(GetTemplateSourceDisplayText(sourceRec)) With {
                .Name = $"LVLN_{sourceId:X8}",
                .Tag = sourceRec
            }
        End If

        Dim childNodes As New List(Of TreeNode)()
        If Not path.Add(sourceId) Then
            childNodes.Add(New TreeNode("<cycle detected>") With {.Tag = Nothing})
        Else
            Dim edges As List(Of TemplateDependencyEdge) = Nothing
            If dependencyMap.TryGetValue(sourceId, edges) Then
                For Each childEdge In edges
                    Dim childNode = BuildTemplateTreeNode(childEdge.DependentNpc.FormID, childEdge, dependencyMap, npcById, filter, path)
                    If childNode IsNot Nothing Then childNodes.Add(childNode)
                Next
            End If
            path.Remove(sourceId)
        End If

        If filter.Length > 0 AndAlso Not selfMatches AndAlso childNodes.Count = 0 Then
            Return Nothing
        End If

        For Each childNode In childNodes
            CType(Nothing, TreeNode).Nodes.Add(childNode)
        Next

        Return Nothing
    End Function


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
        ' aplica al template tree path (BuildTemplateTreeNode), no a la lista plana de NPCs.
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


    Private Function GetTemplateSourceSortKey(sourceId As UInteger, npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As String
        If npcById.ContainsKey(sourceId) Then Return NpcDisplayHelpers.GetNpcNodeDisplayText(npcById(sourceId), Nothing)

        Dim sourceRec = _pluginManager.GetRecord(sourceId)
        If sourceRec Is Nothing Then Return sourceId.ToString("X8")
        Return GetTemplateSourceDisplayText(sourceRec)
    End Function

    Private Function GetTemplateSourceDisplayText(sourceRec As PluginRecord) As String
        If sourceRec Is Nothing Then Return "<missing template source>"
        If sourceRec.Header.Signature = "LVLN" Then
            Dim label = If(sourceRec.EditorID <> "", sourceRec.EditorID, sourceRec.Header.FormID.ToString("X8"))
            Dim pluginSuffix = If(String.IsNullOrWhiteSpace(sourceRec.SourcePluginName), "", $" [{sourceRec.SourcePluginName}]")
            Return $"LVLN {label}{pluginSuffix}"
        End If

        Return NpcManagerFormat.DescribeRecord(sourceRec)
    End Function

    ''' <summary>Per-preview render-pipeline state (last skeleton, ARMA clones, sculpt deltas,
    ''' tint timer, pristine pixel cache, current base state, etc.). One instance per
    ''' PreviewControl — MainForm holds the one for its main preview; editor forms will create
    ''' their own in a later phase. Built in MainForm_Load right after _previewControl. See
    ''' <see cref="NpcRenderHost"/> for the field-by-field documentation.</summary>
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

    Private Sub TreeViewNPCs_DrawNode(sender As Object, e As DrawTreeNodeEventArgs) Handles TreeViewNPCs.DrawNode
        Dim npc = TryCast(e.Node.Tag, NPC_Data)
        Dim lvln = TryCast(e.Node.Tag, LVLN_Data)
        Dim textColor = Color.Black
        If npc IsNot Nothing AndAlso IsTemplateOnly(npc) Then
            textColor = Color.Gray
        ElseIf lvln IsNot Nothing Then
            textColor = Color.DarkBlue
        End If

        ' NPCs with unsaved changes (edited this session or manually marked) render bold. The bold
        ' font is derived once from the tree's font and cached (_dirtyNodeFont).
        Dim nodeFont = TreeViewNPCs.Font
        If npc IsNot Nothing AndAlso _dirtyNpcs.Contains(npc.FormID) Then
            If _dirtyNodeFont Is Nothing Then _dirtyNodeFont = New Font(TreeViewNPCs.Font, FontStyle.Bold)
            nodeFont = _dirtyNodeFont
        End If

        ' Selection highlight. With manual multi-select the framework only marks SelectedNode, so we
        ' paint every node whose NPC FormID is in _selectedNpcFormIDs. The member actually being
        ' rendered (_currentRandomPickFormID) gets the full system highlight; the rest of a
        ' multi-selection get a paler highlight so the user can tell which one was rolled.
        Dim inMultiSet As Boolean = (npc IsNot Nothing AndAlso _selectedNpcFormIDs.Contains(npc.FormID))
        Dim isPicked As Boolean = (npc IsNot Nothing AndAlso _currentRandomPickFormID <> 0UI AndAlso npc.FormID = _currentRandomPickFormID)
        ' Only honor the framework's SelectedNode highlight when there is NO NPC multi-selection (e.g. a
        ' single LVLN / plugin / group node is focused). Otherwise the framework SelectedNode can diverge
        ' from our set and paint a PHANTOM second highlight — the "I selected one but two stay lit" bug.
        Dim frameworkSelected As Boolean = ((e.State And TreeNodeStates.Selected) <> 0) AndAlso _selectedNpcFormIDs.Count = 0
        If isPicked OrElse frameworkSelected Then
            e.Graphics.FillRectangle(SystemBrushes.Highlight, e.Bounds)
            TextRenderer.DrawText(e.Graphics, e.Node.Text, nodeFont, e.Bounds, SystemColors.HighlightText, TextFormatFlags.GlyphOverhangPadding)
        ElseIf inMultiSet Then
            If _multiSelectBrush Is Nothing Then _multiSelectBrush = New SolidBrush(Color.FromArgb(198, 220, 247))
            e.Graphics.FillRectangle(_multiSelectBrush, e.Bounds)
            TextRenderer.DrawText(e.Graphics, e.Node.Text, nodeFont, e.Bounds, textColor, TextFormatFlags.GlyphOverhangPadding)
        Else
            e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds)
            TextRenderer.DrawText(e.Graphics, e.Node.Text, nodeFont, e.Bounds, textColor, TextFormatFlags.GlyphOverhangPadding)
        End If
    End Sub

    ''' <summary>Tree selection changed (mouse or keyboard). With manual multi-select this no longer
    ''' renders directly — it updates the selection set (collapsing to a single NPC when no Ctrl/Shift
    ''' modifier is held; Ctrl/Shift mutations happen in <see cref="TreeViewNPCs_NodeMouseClick"/>) and
    ''' (re)starts the debounce timer. The render runs once the selection settles —
    ''' see <see cref="SelectionDebounceTimer_Tick"/> / <see cref="RenderFromCurrentSelection"/>.</summary>
    ''' <summary>AfterSelect ONLY refreshes the record-details panel. Selection-SET management lives
    ''' in <see cref="TreeViewNPCs_NodeMouseClick"/> (mouse) and <see cref="TreeViewNPCs_KeyUp"/>
    ''' (keyboard), so it can never fight this event: touching _selectedNpcFormIDs here collapsed or
    ''' deselected multi-selections because AfterSelect's order relative to NodeMouseClick is not
    ''' guaranteed (it can fire before OR after the click handler).</summary>
    Private Sub TreeViewNPCs_AfterSelect(sender As Object, e As TreeViewEventArgs) Handles TreeViewNPCs.AfterSelect
        Dim npc = TryCast(e.Node?.Tag, NPC_Data)
        PopulateRecordDetails(npc)
        ' Record which NPC the details panel now shows so the debounced render
        ' (RenderFromCurrentSelection, ~180 ms later) can skip rebuilding the identical detail tree
        ' for the common single-select case — it is built twice today (once here for instant
        ' feedback, once in the render). See _detailsAfterSelectFormID.
        _detailsAfterSelectFormID = If(npc IsNot Nothing, npc.FormID, 0UI)
    End Sub

    ''' <summary>Tree mouse click. LEFT click drives manual multi-select (plain = single, Ctrl =
    ''' toggle, Shift = range over the currently-shown NPC leaf nodes). RIGHT click opens the context
    ''' menu on NPC nodes only (never on plugin-group / LVLN / empty space), targeting the whole
    ''' multi-selection when the click lands inside it, otherwise just the clicked NPC. Right-click
    ''' does not start a render (no heavy work on a context click).</summary>
    Private Sub TreeViewNPCs_NodeMouseClick(sender As Object, e As TreeNodeMouseClickEventArgs) Handles TreeViewNPCs.NodeMouseClick
        If e.Button = MouseButtons.Left Then
            HandleLeftMultiSelectClick(e)
            Return
        End If
        If e.Button <> MouseButtons.Right Then Return

        Dim npc = TryCast(e.Node.Tag, NPC_Data)
        If npc Is Nothing Then Return

        ' Action targets: the whole multi-selection if the right-click landed inside it, otherwise
        ' re-select just this node and act on it alone.
        _contextMenuTargets.Clear()
        If _selectedNpcFormIDs.Count > 1 AndAlso _selectedNpcFormIDs.Contains(npc.FormID) Then
            _contextMenuTargets.AddRange(_selectedNpcFormIDs)
        Else
            _selectedNpcFormIDs.Clear()
            _selectedNpcFormIDs.Add(npc.FormID)
            _multiSelectAnchorNode = e.Node
            _currentRandomPickFormID = npc.FormID
            _contextMenuTargets.Add(npc.FormID)
            TreeViewNPCs.Invalidate()
        End If
        _contextMenuNpcFormID = npc.FormID

        ' Reset is only meaningful when at least one target has something to discard.
        MenuItemResetOverlay.Enabled = _contextMenuTargets.Any(
            Function(fid) _appliedPresets.ContainsKey(fid) OrElse _dirtyNpcs.Contains(fid))
        TreeViewNpcsContextMenu.Show(TreeViewNPCs, e.Location)
    End Sub

    ''' <summary>Apply a LEFT-click to the multi-selection per the held modifier. Only NPC leaf nodes
    ''' participate; clicks on group / LVLN nodes are handled by AfterSelect (which clears the set).
    ''' Plain click is handled here too (not only in AfterSelect) so clicking an already-selected node
    ''' still collapses a multi-selection to that one — AfterSelect won't fire when the selection
    ''' didn't change.</summary>
    Private Sub HandleLeftMultiSelectClick(e As TreeNodeMouseClickEventArgs)
        Dim npc = TryCast(e.Node.Tag, NPC_Data)
        If npc Is Nothing Then
            Dim lvln = TryCast(e.Node.Tag, LVLN_Data)
            ' LVLN nodes only count as a SINGLE selection (their own random pick). Held with
            ' Ctrl/Shift they are NOT considered — leave the current NPC multi-selection untouched.
            If lvln IsNot Nothing AndAlso (Control.ModifierKeys And (Keys.Control Or Keys.Shift)) <> 0 Then
                Return
            End If
            ' Plain LVLN click, or a plugin / group node → drop the NPC multi-selection. The tick then
            ' renders the LVLN's own random pick (single) via TreeViewNPCs.SelectedNode, or disables
            ' the per-NPC controls for a group node.
            _selectedNpcFormIDs.Clear()
            _multiSelectAnchorNode = Nothing
            TreeViewNPCs.Invalidate()
            RestartSelectionDebounce()
            Return
        End If

        ' Operate directly on the persistent _selectedNpcFormIDs — AfterSelect never touches it, so
        ' this is immune to the TreeView's mouse/select event ordering.
        Dim ctrlDown As Boolean = (Control.ModifierKeys And Keys.Control) <> 0
        Dim shiftDown As Boolean = (Control.ModifierKeys And Keys.Shift) <> 0

        If shiftDown AndAlso _multiSelectAnchorNode IsNot Nothing Then
            SelectNpcRange(_multiSelectAnchorNode, e.Node)   ' range REPLACES the selection
        ElseIf ctrlDown Then
            If Not _selectedNpcFormIDs.Remove(npc.FormID) Then _selectedNpcFormIDs.Add(npc.FormID)
            _multiSelectAnchorNode = e.Node
        Else
            ' Plain click → single-select.
            _selectedNpcFormIDs.Clear()
            _selectedNpcFormIDs.Add(npc.FormID)
            _multiSelectAnchorNode = e.Node
        End If

        TreeViewNPCs.Invalidate()
        RestartSelectionDebounce()
    End Sub

    ''' <summary>Keyboard navigation in the tree → single-select the focused node (no Ctrl/Shift
    ''' multi-select via keys for now). Fires only for keyboard, never mouse, so it cannot race the
    ''' click handler.</summary>
    Private Sub TreeViewNPCs_KeyUp(sender As Object, e As KeyEventArgs) Handles TreeViewNPCs.KeyUp
        Select Case e.KeyCode
            Case Keys.Up, Keys.Down, Keys.Left, Keys.Right, Keys.PageUp, Keys.PageDown, Keys.Home, Keys.End
                Dim node = TreeViewNPCs.SelectedNode
                Dim npc = TryCast(node?.Tag, NPC_Data)
                _selectedNpcFormIDs.Clear()
                If npc IsNot Nothing Then
                    _selectedNpcFormIDs.Add(npc.FormID)
                    _multiSelectAnchorNode = node
                Else
                    _multiSelectAnchorNode = Nothing
                End If
                TreeViewNPCs.Invalidate()
                RestartSelectionDebounce()
        End Select
    End Sub

    ''' <summary>Set the selection to the NPC leaf nodes between anchor and target in display order
    ''' (expand-aware: only currently-shown rows). Falls back to single-select if the anchor is no
    ''' longer in the tree (e.g. after a rebuild).</summary>
    Private Sub SelectNpcRange(anchorNode As TreeNode, targetNode As TreeNode)
        Dim flat = FlattenVisibleNodes()
        Dim iA = flat.IndexOf(anchorNode)
        Dim iB = flat.IndexOf(targetNode)
        _selectedNpcFormIDs.Clear()
        If iA < 0 OrElse iB < 0 Then
            Dim n0 = TryCast(targetNode.Tag, NPC_Data)
            If n0 IsNot Nothing Then _selectedNpcFormIDs.Add(n0.FormID)
            _multiSelectAnchorNode = targetNode
            Return
        End If
        Dim lo = Math.Min(iA, iB)
        Dim hi = Math.Max(iA, iB)
        For i = lo To hi
            Dim n = TryCast(flat(i).Tag, NPC_Data)
            If n IsNot Nothing Then _selectedNpcFormIDs.Add(n.FormID)
        Next
    End Sub

    ''' <summary>Currently-shown tree nodes in display order (expand-aware: collapsed children are
    ''' excluded, matching the rows the user sees). Used for Shift-range selection.</summary>
    Private Function FlattenVisibleNodes() As List(Of TreeNode)
        Dim acc As New List(Of TreeNode)()
        For Each top As TreeNode In TreeViewNPCs.Nodes
            FlattenVisibleInto(top, acc)
        Next
        Return acc
    End Function

    Private Sub FlattenVisibleInto(node As TreeNode, acc As List(Of TreeNode))
        acc.Add(node)
        If node.IsExpanded Then
            For Each child As TreeNode In node.Nodes
                FlattenVisibleInto(child, acc)
            Next
        End If
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
        Dim lvln = TryCast(TreeViewNPCs.SelectedNode?.Tag, LVLN_Data)
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
                        Return If(genderFilter = GenderFilterMode.Female, n.IsFemale, Not n.IsFemale)
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
        LoadNPCOnDemandAsync(npc, reqVersion)
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
        Dim targets = If(_contextMenuTargets.Count > 0, _contextMenuTargets.Distinct().ToList(), New List(Of UInteger))
        If targets.Count = 0 Then Return
        If targets.Count = 1 Then
            Await BuildCharGenSingle(targets(0))
        Else
            Await BuildCharGenForSelectionAsync(targets)
        End If
    End Sub

    ''' <summary>Context-menu "Reset": discard the in-memory overlay for the NPC and clear its dirty
    ''' flag (no longer bold). Overlay-only by design — does NOT delete any on-disk sidecar or saved
    ''' ESP override (user decision 2026-05-24). The NPC reverts to its current baseline record: the
    ''' saved override if it was saved this session (MergeOverridePlugin put it in GetRecord), else
    ''' vanilla. Destructive (drops BodyMorphs/Skin edits too) → confirmation first.</summary>
    Private Async Sub MenuItemResetOverlay_Click(sender As Object, e As EventArgs) Handles MenuItemResetOverlay.Click
        Dim sourceTargets = If(_contextMenuTargets.Count > 0, _contextMenuTargets, New List(Of UInteger) From {_contextMenuNpcFormID})
        ' Only NPCs that actually have something to discard (overlay or dirty mark).
        Dim targets = sourceTargets.
            Where(Function(fid) fid <> 0UI AndAlso (_appliedPresets.ContainsKey(fid) OrElse _dirtyNpcs.Contains(fid))).
            Distinct().ToList()
        If targets.Count = 0 Then Return

        Dim prompt = If(targets.Count = 1,
            "Discard all in-memory changes for this NPC (including BodyMorphs / Skin / Overlays edits) and revert to its current record?",
            $"Discard all in-memory changes for the {targets.Count} selected NPCs (including BodyMorphs / Skin / Overlays edits) and revert each to its current record?")
        If MessageBox.Show(Me,
                           prompt & vbCrLf & vbCrLf &
                           "This does NOT delete any ESP/ESM or sidecar already written to disk.",
                           "Reset NPC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) <> DialogResult.Yes Then
            Return
        End If

        Dim shownFormID As UInteger = If(_renderHost IsNot Nothing AndAlso _renderHost.LastRenderedState IsNot Nothing,
                                         _renderHost.LastRenderedState.RootNpcFormID, 0UI)
        Dim mustReRender As Boolean = False
        For Each fid In targets
            _appliedPresets.Remove(fid)
            _dirtyNpcs.Remove(fid)
            If fid = shownFormID Then mustReRender = True
        Next
        RefreshTreeAfterDirtyChange()

        ' Re-render from the baseline only if the currently-shown NPC was among those reset.
        If mustReRender AndAlso shownFormID <> 0UI Then
            Dim npc As NPC_Data = Nothing
            If _ctx.NpcCache.TryGetValue(shownFormID, npc) AndAlso npc IsNot Nothing Then
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
    ''' (el mismo baseline que el Designer fija al arrancar — ver MainForm.Designer.vb líneas
    ''' 576-745). Se llama desde <see cref="TreeViewNPCs_AfterSelect"/> cuando la selección del
    ''' árbol cae en un nodo NO accionable: un root de plugin / grupo "[LVLN]" (Tag = Nothing) o
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
        ButtonLoadLooksmenu.Enabled = False
        ButtonSaveLooksmenu.Enabled = False
        ButtonCopyLook.Enabled = False
        ButtonPasteLook.Enabled = False
        ButtonSavePlugin.Enabled = False
        ButtonBuildCharGen.Enabled = False
        ButtonSaveSceneNif.Enabled = False
    End Sub

    Private Async Sub LoadNPCOnDemandAsync(npc As NPC_Data, requestVersion As Integer)
        Try
            Dim _swL As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)
            SetStatus($"Loading assets for {npc}...")
            Await EnsureAssetDictionaryAsync()
            Logger.LogLazy(Function() $"[PERF-L] EnsureAssetDictionary @ {_swL.ElapsedMilliseconds}ms")
            If requestVersion <> _previewRequestVersion Then Return

            SetStatus($"Resolving {npc}...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
            Await Task.Run(Sub()
                               baseState = _stateResolver.ResolveNPCBaseState(npc, _renderHost)
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
    Private Async Sub LoadLVLNOnDemandAsync(lvlnData As LVLN_Data, requestVersion As Integer)
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
                npc = RecordParsers.ParseNPC(npcRec, If(npcRec.SourcePluginName, ""), _pluginManager)
            End If

            PopulateRecordDetails(npc)

            SetStatus($"Resolving {npc} (from {lvlnData.EditorID})...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
            Await Task.Run(Sub()
                               baseState = _stateResolver.ResolveNPCBaseState(npc, _renderHost)
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

        ' Disparar render: si hay OBTE, ResolveNpcCombinations re-rolea los modcol_* internamente
        ' (random pick por DontUseAll=True). Si solo hay OBTE sin outfit (robots, brahmin), este
        ' es el único re-roll que aplica.
        Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
        RenderOnDemandAsync(requestVersion)
    End Sub

    Private Sub ButtonRandomNPC_Click(sender As Object, e As EventArgs) Handles ButtonRandomNPC.Click
        ' Multi-selection acts as an ad-hoc leveled list: re-roll a (gender-filtered) random member.
        If _selectedNpcFormIDs.Count >= 2 Then
            RerollFromSelection()
            Return
        End If

        Dim selectedNode = TreeViewNPCs.SelectedNode
        If selectedNode Is Nothing Then Return

        ' If selected node is a LVLN, re-pick a random NPC from it
        Dim lvlnData = TryCast(selectedNode.Tag, LVLN_Data)
        If lvlnData IsNot Nothing Then
            Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvlnData, requestVersion)
            Return
        End If

        ' Re-resolve the SAME NPC — the LVLN in its template chain will produce
        ' different random picks (different face/gender) each time.
        Dim npc = TryCast(selectedNode.Tag, NPC_Data)
        If npc Is Nothing Then Return

        Dim requestVersion2 = Interlocked.Increment(_previewRequestVersion)
        LoadNPCOnDemandAsync(npc, requestVersion2)
    End Sub

    ''' <summary>Changing the gender filter re-rolls the pick: from the multi-selection (ad-hoc
    ''' leveled list) when 2+ NPCs are selected, otherwise from the selected LVLN node. No-op for a
    ''' single plain NPC (the filter only governs random picks).</summary>
    Private Sub ComboBoxGender_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ComboBoxGender.SelectedIndexChanged
        If _selectedNpcFormIDs.Count >= 2 Then
            RerollFromSelection()
            Return
        End If
        Dim lvln = TryCast(TreeViewNPCs.SelectedNode?.Tag, LVLN_Data)
        If lvln IsNot Nothing Then
            Dim v = Interlocked.Increment(_previewRequestVersion)
            LoadLVLNOnDemandAsync(lvln, v)
        End If
    End Sub

    Private Sub ButtonLightRig_Click(sender As Object, e As EventArgs) Handles ButtonLightRig.Click
        Dim form As New LightRigForm
        AddHandler form.LightsChanged, AddressOf OnLightRigChanged
        Try
            form.ShowDialog(Me)
        Finally
            RemoveHandler form.LightsChanged, AddressOf OnLightRigChanged
        End Try
    End Sub

    Private Sub OnLightRigChanged()
        If _previewControl IsNot Nothing AndAlso Not _previewControl.IsDisposed Then
            _previewControl.UpdateRequired = True
            _previewControl.Update()
        End If
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

        Dim useFaceGen = HasFaceGenAssets(state)

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
            .OnlyOutfitCollect = onlyOutfitCollect
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
        ' show only empty pickers. See ComputeFaceEditAvailability for the per-section rule.
        UpdateEditFaceEnabled()
        ' Gate the Edit Outfit button: enabled when an NPC with a race is loaded and the load order
        ' has any outfit. The per-race candidate filter is deferred to picker-open (GetOutfitCandidates).
        UpdateEditOutfitEnabled()

        ' Face tint compositing + RevealAllShapes are sequenced by the PostTextureUploadAction
        ' wired before RenderShapes (above). The library invokes them on the GL thread once the
        ' background diffuse uploads complete, so the bake passes always see populated Textures_Dictionary
        ' entries. RevealAllShapes is called inside the hook — shapes stay RenderHide=True until then.

        SetStatus($"Rendered {previewVariant.DisplayName} ({renderData.Shapes.Count} shapes)")
        Logger.LogLazy(Function() $"[PERF-R] ========== RenderCurrentStateAsync TOTAL = {_swR.ElapsedMilliseconds}ms ==========")
    End Function

    '' <summary>Output of <see cref="BuildRenderPlan"/>: the CPU-computed render data + skeleton/mount
    '' state that the UI-thread tail of RenderCurrentStateAsync submits to GL.</summary>
    Private NotInheritable Class RenderPlanResult
        Public RenderData As PreviewResolutionResult
        Public Inst As SkeletonInstance
        Public HeadInst As SkeletonInstance
        Public SkelByArma As Dictionary(Of UInteger, SkeletonInstance)
        Public SculptByArma As Dictionary(Of UInteger, Dictionary(Of String, System.Numerics.Vector3))
        Public ShapeToSkel As Dictionary(Of IRenderableShape, SkeletonInstance)
        Public Request As RenderRequest
    End Class

    '' <summary>CPU compute half of RenderCurrentStateAsync (Finding 2-core): resolves the preview
    '' variant, builds the merged pose + per-ARMA skeletons, runs the robot-chunk/socket mounting, and
    '' assembles the RenderRequest. PURE CPU — no WinForms controls, no GL/host.PreviewCtl — so the caller
    '' runs it on Task.Run, keeping only the GL submission (RenderShapes) + UI gating on the UI thread.
    '' Returns Nothing if the request was superseded; a result whose RenderData has no shapes signals 'no
    '' meshes' to the caller (which shows the status + returns).</summary>
    Private Function BuildRenderPlan(previewVariant As PreviewVariantDefinition, host As NpcRenderHost, state As NPCVisualState, requestVersion As Integer) As RenderPlanResult
        Dim _swBrp As System.Diagnostics.Stopwatch = If(Logger.Enabled, System.Diagnostics.Stopwatch.StartNew(), Nothing)
        Dim renderData = _meshCollector.ResolvePreviewVariant(previewVariant)
        Logger.LogLazy(Function() $"[PERF-BRP] ResolvePreviewVariant @ {_swBrp.ElapsedMilliseconds}ms ({renderData?.Shapes?.Count} shapes)")
        If requestVersion <> _previewRequestVersion Then Return Nothing

        If renderData Is Nothing OrElse renderData.Shapes.Count = 0 Then
            Return New RenderPlanResult With {.RenderData = renderData}
        End If

        ' Two independent checkboxes control bone pose (FMRS) and vertex morphs (chargen TRI).
        ' Both are honored during the initial full render; individual toggles after that are
        ' handled by the CheckedChanged handlers below using the granular Intent.MarkDirty flow
        ' (WM pattern from WM_RenderExtensions.vb), NOT a full reload via RenderShapes(request).
        ' Granular toggles inside BuildCompositeMorphResolver: face = CheckBoxApplyVertexMorphs,
        ' body = CheckBoxBodyTri. No master-AND gate here — composite returns Nothing on its own
        ' when both subsections are unchecked.
        Dim boneMorphsEnabled = host.Toggles.ApplyBoneMorphs
        Dim morphResolver = BuildCompositeMorphResolver(state, renderData, host)
        Logger.LogLazy(Function() $"[PERF-BRP] morphResolver @ {_swBrp.ElapsedMilliseconds}ms")

        ' Build a pose carrying the FMRI/FMRS face bone deltas (each region's bones become
        ' PoseTransformData entries). This pose is applied via SkeletonInstance.ApplyPose which
        ' sets DeltaTransform on each bone — the same mechanism body poses use. The checkbox
        ' toggle lets the user compare "raw face" (no pose, no morphs) vs "with FMRS applied" live.
        Dim bodyWeightEnabled = host.Toggles.ApplyBodyWeight
        Dim sculptEnabled = host.Toggles.ApplySculpt

        ' Per-ARMA skeleton flow (refactored 2026-04-27, replaces single shared skeleton):
        ' Each shape goes to a SkeletonInstance with its own ARMA's sculpt applied (if any), or to
        ' the base instance (no sculpt) if its ARMA has none. Generic for ANY ARMA — body / outfit /
        ' gloves / underarmor / etc. all follow the same rule. Multiple shapes from the same NIF
        ' share the same skeleton (cached by ArmorAddonFormID).
        ' Sculpt formula: H3 multiplicative (s = race_s · (1 + arma_d)) hardcoded — A REVISAR.
        ' Closure plan P0: fórmula correcta del engine no confirmada vs CK. H3 es candidata
        ' conceptual más limpia (cumple invariantes naturales) pero sin verificación pixel-match.
        ' Re-introducción del dropdown experimental se descartó 2026-04-29 tras detectar que el
        ' clip motivador era OMODs/add-ons no renderizados, no la fórmula.
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

        Dim basePose = _morphPoseResolver.BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled,
                                          inst, Nothing)  ' Nothing = no sculpt → base pose
        ' Bone-morphs → capa MorphDeltaTransform (deja libre la capa pose/animación).
        inst.ApplyBoneMorphPose(basePose)
        _morphPoseResolver.ApplyNeckNnamCompensation(inst)

        ' Head skeleton: SAME morph/FMRS pose as `inst` WITH body weight AND NNAM neck-fat (FASE 2
        ' 2026-06-24). Body weight scales the _skin LEAF bones (Head_skin/Face_skin/Neck1_skin + shared
        ' neck/chest), which do NOT propagate. El NNAM escala el hueso ANCESTRO "Neck"; antes se suprimía
        ' acá porque propagaba [1+nnam,1+nnam,1] a toda la cara (balloon). Ahora ApplyNeckNnamCompensation
        ' (post-pase) compensa a TODOS los hijos directos de "Neck" (comp = L_C⁻¹∘S⁻¹∘L_C ∘ FMRS, con shear),
        ' dejando la escala solo en los verts del "Neck" → la cara NO se infla y los FMRS del cuello quedan.
        ' Head-part shapes are routed here (loop below); animation frames applied too (ApplyAnimFrame). Built
        ' unconditionally + separate so it stays in sync with the BODY skeleton. No chunk/pipboy injection.
        Dim headInst = PrepareSkeleton(state, renderData)
        Dim headPose = _morphPoseResolver.BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled, headInst, Nothing)
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
                                                     armaSkel, sculpt)
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

        ' Inyectar bones internos de chunks BSConnectPoint-mounted (chunks robot, weapon
        ' mods, PA pieces) al SkeletonInstance del actor. Para cada shape con MountSocket
        ' resuelto, los bones del chunk que NO existen en el actor se agregan al dict
        ' anchored al socket.ParentBone con OriginalLocaLTransform = socketLocal × chunkRoot ×
        ' bone_local. Esto hace que SkinningHelper (con GlobalTransform=identity para
        ' skinned, paridad OS Anim.cpp:732) los encuentre via SkeletonDictionary y produzca
        ' v_world = bone.world × shapeBoneT × v_local correcto en actor-space.
        ' Mounting de chunks en ORDEN TOPOLÓGICO:
        '   - Host NIF (skeleton del actor) materializa sus sockets PRIMERO — todos los chunks
        '     pueden depender de C-X expuestos por el host (C-Head, C-HandLeft, C-PackBase).
        '   - Por cada chunk procesado: primero INJECT (crea bones internos), después MATERIALIZE
        '     (sus sockets pueden anclarse a esos bones internos recién creados, p.ej.
        '     P-PackTop.parent='TopLagBone' del PackBase02).
        '   - Topological order: A se procesa antes que B si B consume sub-socket C-X expuesto
        '     por A. Si A y B son independientes, orden indiferente.
        '
        ' Construcción del grafo de dependencia:
        '   - Cada shape NIF tiene Children (C-X names que consume) y Parents (P-X sub-sockets
        '     que expone con su tail).
        '   - A → B si tail de algún Children de B coincide con tail de algún Parent de A.
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
        ' NOTA: isRobotMount es SOLO filtro de verbosidad de diagnósticos. La aplicación V2
        ' SKEL-OVERRIDE ya NO depende de este flag (gate Fase 2.5 removido) — V2 aplica a
        ' robot Y biped por igual. Biped corre V2 sin emitir estos logs verbosos.
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

            ' [SOCKET-EFFECTIVE-OVERRIDE] Per OpenAI Vuelta 17: para chunks downstream del
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
                ' [FASE 3] Criterio estructural: el chunk-mount path es 'soy attachment con
                ' MountSocket resuelto', no 'soy de FormType X'. Antes el filtro NPC_ gateaba
                ' biped fuera; ahora capas 1+2 (coord fix + socket disambig) son compartidas.
                ' Capa 3 V2 aplica a robot Y biped: cualquier shape con MountSocket recibe el mount
                ' vía MountDeltaTransform en los huesos (ApplyMountPlanForActor → OverrideActorBoneWorld).
                ' isRobotMount quedó sólo como filtro de verbosidad.
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

            ' [FAKE-SKIN] Para shapes UNSKINNED dentro de un chunk BSConnectPoint, aplicar
            ' synthetic skin tying todos los vertices al chunk anchor con weight 1.0. Sin esto,
            ' SkinningHelper.vb:374 (path A unskinned) computa Mtot = GetGlobalTransform en chunk-
            ' local frame y el shader lo aplica como si fuera actor-world → geometría cae al
            ' origen (caso LightPlane en Protectron). Con fake-skin, la shape entra al path
            ' skinned nativo y el anchor.posedWorld se aplica per frame (= pose follow gratis,
            ' bounds/world cache también).
            '
            ' IMPORTANTE bind matrix: walkear backing → ... → parent_de_chunkRoot, NO componer
            ' chunkRoot.local. Per BSConnectPointBoneInjector.vb:137-140 chunkRoot.R es scene-
            ' viewer rotation del modelador, NO parte del attachment. anchor.world ya incluye
            ' la rotación del parent bone del actor (= Chest.R, que matchea chunkRoot.R por
            ' diseño del chunk). Si bind incluye chunkRoot.R y luego × anchor.world (que tiene
            ' Chest.R), se mete un flip espurio (verificado: composición R_chunk × R_anchor da
            ' rotación 180° Y para HeadProtectron → light termina detrás del actor).
            ' [BIPED-FAKE-SKIN] Gate semántico (per OpenAI Vuelta 19): el synthetic anchor para
            ' chunks unskinned attachment-style aplica a robot Y biped. Antes era robot-only
            ' (isRobotMount) → bipeds con chunk unskinned (ej. PA_T45_Headlamp sobre Mining Helmet
            ' en humano) caían al origen porque InjectChunkBonesIntoLiveSkeleton early-exit en shapes
            ' sin bones y nunca recibían ancla. Criterio: unskinned + Attachment + MountSocket resuelto.
            Dim fakeSkinCand As MeshCandidate = Nothing
            renderData.ShapeCandidate.TryGetValue(shape, fakeSkinCand)
            Dim isAttachmentMount As Boolean = fakeSkinCand IsNot Nothing AndAlso fakeSkinCand.Kind = MeshCandidateKind.Attachment AndAlso fakeSkinCand.MountSocket IsNot Nothing
            If Not shape.IsSkinned AndAlso isAttachmentMount Then
                ' [FAKE-SKIN-DIAG] Confirmaciones discriminantes (OpenAI Vuelta 19): estado de la shape
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
                        ' [CAMINO C — materialización lazy on-demand del C-X] (OpenAI Vuelta 20).
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
                        ' [FAKE-SKIN-DIAG] Confirmación #2 (OpenAI Vuelta 19): ¿el C-X counterpart
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

            ' [CHUNK-RESKIN-V2] Re-skinear shape.ShapeBoneTransforms usando la fórmula per-bone
            ' OpenAI: cada bone B del chunk obtiene W_B = G_B × A, donde:
            '   G_B    = global transform de B's NiNode en el árbol del chunk NIF (no derivado de inv(bind))
            '   G_CX   = global transform del NiNode C-X (declarado en BSConnectPoint::Children del chunk)
            '   P_world = parent_bone.world × socketLocal_chunk-source-if-override (M_mesh corregido)
            '   A      = inv(G_CX) × P_world   (único transform de attachment chunk→actor)
            '
            ' Math: render quiere v_world = sum(w_B · v · bind(B) · W_B). Con render actual usando
            ' actor.B.world del dict, modificamos bind tal que:
            '   bind' = correction.Compose(bind), donde correction = inv(actor.B.world).Compose(W_B)
            ' Resultado: v · bind' × actor.B.world = v · bind × W_B (engine-equivalente).
            '
            ' Para C-X bone, W_C-X = G_CX × A = G_CX × inv(G_CX) × P_world = P_world (= M_mesh corregido).
            ' Para otros bones (Arm1..Arm7, etc.), W_B preserva la posición relativa del chunk-NIF tree
            ' transportada por A — esto mantiene la articulación del chunk en vez de colapsar todos los
            ' bones en el mismo punto (que era el bug del attempt previo).
            ' Skip V2 reskin para shapes fake-skinned (HasSyntheticSkin=True). Su bind ya está
            ' seteado por ApplySyntheticAnchorSkin como el Mtot chunk-local; el shader compone
            ' con actor.anchor.posedWorld y produce vertex × Mtot × anchor.world correctamente.
            ' V2 sobre estas shapes meterá un factor espurio inv(chunkRoot.R) que rompe el render.
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

        ' DIAG (2026-05-13 socket-math): para cada chunk shape con socket, dumpear el
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

            ' DIAG VERTEX-TRACE (2026-05-14): per chunk shape, replicar la fórmula de skinning
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

            ' [STRATEGY-SIMULATE] Para vertex 0 de cada chunk con socket: computar v_world predicto
            ' bajo CUATRO hipótesis distintas de cómo derivar bone.world(actor). El render real NO
            ' se toca; sólo loggeamos predicciones para comparar visualmente contra lo que se ve.
            ' Math común: v_world = v_local · bind · bone.world (row-vec). bind = shape.ShapeBoneTransforms[k].
            '
            '   S1 = CURRENT     → bone.world = actor.SkeletonDictionary[bone].OriginalGlobalTransform
            '                      (lo que el render usa hoy: BPTD para shared, inv(bind)·P-X.world para injected).
            '   S2 = IDENTITY    → bone.world = inv(bind).  Equivale a chunkRoot.world(actor)=identity.
            '                      Implica que el chunk se autoreó "en actor world".
            '   S3 = SOCKET-ONLY → bone.world = socketLocal·inv(bind). NO compone parent_bone.world.
            '                      Equivale a chunkRoot.world(actor)=socketLocal.
            '   S4 = CX-ALIGN    → M = P-X.world·bind(C-X). bone.world = M·inv(bind).
            '                      "Alinea bind-derived chunk-C-X position al P-X world del socket".
            '                      Requiere que el chunk tenga un shapeBone cuyo nombre coincida con
            '                      la "C-X" declarada en BSConnectPoint::Children.
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
            .RecalculateNormals = True,
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
        CheckBoxRenderGore.Enabled = enabled
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

    ''' <summary>Entry point invoked right after RenderShapes. Tries to bake tints immediately;
    ''' if the face diffuse texture isn't in the cache yet (async upload pending), schedules a
    ''' polling timer that retries until the texture appears.</summary>


    ''' <summary>Diagnóstico: dumpea bounds per-mesh + scene AABB + tamaño del control + estado
    ''' actual de la OrbitCamera. Usado en pre/post ResetCamera para detectar si el cálculo
    ''' del frame es coherente con la geometría visible.</summary>

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
    ''' MainForm so EditFace_Form's <c>_mainForm.RefreshFaceTintLivePreview</c> call site is unchanged
    ''' by the Phase 2 split.</summary>
    Friend Function RefreshFaceTintLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Return _faceTintResolver.RefreshFaceTintLivePreview(host)
    End Function

    ''' <summary>Facade for the editors: delegates to the skin-override live-preview fast-path. Kept on
    ''' MainForm so EditBody_Form's <c>_mainForm.RefreshBodySkinLivePreview</c> call site is unchanged
    ''' by the Phase 2 split.</summary>
    Friend Function RefreshBodySkinLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Return _skinLivePreview.RefreshBodySkinLivePreview(host)
    End Function

    ''' <summary>Build the list of region-mask TXST swaps for an NPC. For each Morph Group
    ''' of the NPC's race, look up whether any preset in that group is currently active in
    ''' the NPC's MorphValues AND the preset has an MPPT TXST. If so, resolve:
    '''   - mask DDS: from the Morph Group's MPPK enum -> TintSlot 0..6 -> TintOption -> TTET[0]
    '''   - swap DDS bytes: from the preset's MPPT TXST.TX00 / TX01 / TX07
    ''' Returns one FaceRegionSwapInput per active preset (typically 0..3 for non-aged NPCs,
    ''' 3 for Murphy who has Arrugado in Forehead/Cheeks/Neck).</summary>

    ''' <summary>Build the layer list, find the face mesh diffuse cache entry, run the compositor
    ''' and mutate the cache entry. Returns True if at least one face mesh was successfully tinted,
    ''' False if the texture wasn't ready (caller should defer and retry).</summary>












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
                .Label = $"{slotName} — {draft.EditorID} ({realized.Count} pcs) [draft]",
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
                FilesDictionary_class.RegisterExtensions(".ssf", ".sclp")
                _assetDictionaryLoadTask = FilesDictionary_class.Fill_DictionaryAsync(_dataPath, progress)
            End If

            loadTask = _assetDictionaryLoadTask
        End SyncLock

        Await loadTask

        Dim scanReport = FilesDictionary_class.DrainScanReport()
        If scanReport.Count > 0 Then
            Dim hits As Integer = 0
            For Each r In scanReport
                If r.CacheHit Then hits += 1
            Next
            Dim misses As Integer = scanReport.Count - hits
            For Each r In scanReport
            Next
        End If


        ' Everything below this point is debug-only asset instrumentation (TRI / mouth-NIF
        ' enumeration) whose results are DISCARDED — pure investigation scaffolding, no functional
        ' effect on the app. EnsureAssetDictionaryAsync is awaited at the start of EVERY NPC
        ' selection; after the first build the awaited load task completes instantly, so this tail
        ' runs SYNCHRONOUSLY ON THE UI THREAD on every click — BA2 decompress + TriHeadParser ×12,
        ' four full NIF loads, and a full FilesDictionary scan. Skip it unless logging is on (drop
        ' the progress bar first, exactly as the tail would). Re-enable by turning on the logger.
        If Not Logger.Enabled Then
            If InvokeRequired Then
                BeginInvoke(Sub() ToolStripProgressBar1.Visible = False)
            Else
                ToolStripProgressBar1.Visible = False
            End If
            Return
        End If

        ' TRI-PROBE 2026-04-19: enumerate vanilla head TRIs to resolve the male _faceBones 1696
        ' vs chargen 1690 mismatch puzzle. Loads all known head TRI variants and logs vert count
        ' + morph names so we can decide which TRI is the correct morph source for _faceBones.
        Dim triProbePaths = {
            "meshes\actors\character\characterassets\basemalehead.tri",
            "meshes\actors\character\characterassets\basemaleheadchargen.tri",
            "meshes\actors\character\characterassets\basemaleheadold.tri",
            "meshes\actors\character\characterassets\basefemalehead.tri",
            "meshes\actors\character\characterassets\basefemaleheadchargen.tri",
            "meshes\actors\character\characterassets\humanoidhead.tri"
        }
        For Each probePath In triProbePaths
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(probePath, loc) Then
                Continue For
            End If
            Try
                Dim bytes = loc.GetBytes()
                If bytes Is Nothing OrElse bytes.Length < 64 Then
                    Continue For
                End If
                ' Verify FRTRI003 magic
                Dim magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 8)
                If Not magic.StartsWith("FRTRI") Then
                    Continue For
                End If
                ' Read 14 uint32 header fields at offset 8 to expose EVERYTHING (incl. numModifiers, numModVertices, unknowns)
                Dim h(13) As UInteger
                For k = 0 To 13
                    h(k) = BitConverter.ToUInt32(bytes, 8 + k * 4)
                Next
                Dim numVertices = h(0), numTriangles = h(1), numQuads = h(2), unk2 = h(3), unk3 = h(4)
                Dim numUV = h(5), flags = h(6), numMorphs = h(7), numModifiers = h(8), numModVertices = h(9)
                Dim unk7 = h(10), unk8 = h(11), unk9 = h(12), unk10 = h(13)
                ' Now parse via library for morph names (splits regular vs mod)
                Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
                If head IsNot Nothing Then
                    Dim regularNames = head.Morphs.Where(Function(m) Not m.IsModMorph).Select(Function(m) m.Name).ToList()
                    Dim modNames = head.Morphs.Where(Function(m) m.IsModMorph).Select(Function(m) m.Name).ToList()
                End If
            Catch ex As Exception
            End Try
        Next

        ' CHILD-TRI-DUMP: enumerate every .tri entry in FilesDictionary whose path contains
        ' "child" so we know exactly what TRIs vanilla / loaded mods ship for the Child race.
        ' If any of them carries the Brow/Chin/EyesMove morph names that HumanChildRace.MorphValues
        ' declares but BaseFemaleHeadChargen.tri lacks, we have a Child-specific TRI we should
        ' be loading instead of (or in addition to) the adult chargen TRI.
        Dim childTris = New List(Of String)
        For Each kv In FilesDictionary_class.Dictionary
            Dim k = kv.Key
            If k.EndsWith(".tri", StringComparison.OrdinalIgnoreCase) AndAlso
               k.Contains("child", StringComparison.OrdinalIgnoreCase) Then
                childTris.Add(k)
            End If
        Next
        childTris.Sort(StringComparer.OrdinalIgnoreCase)
        For Each childTri In childTris
            Dim probePath = childTri
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(probePath, loc) Then Continue For
            Try
                Dim bytes = loc.GetBytes()
                If bytes Is Nothing OrElse bytes.Length < 64 Then
                    Continue For
                End If
                Dim magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 8)
                If magic.StartsWith("FRTRI") Then
                    Dim numVerts = BitConverter.ToUInt32(bytes, 8)
                    Dim numMorphs = BitConverter.ToUInt32(bytes, 8 + 7 * 4)
                    Try
                        Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
                        If head IsNot Nothing Then
                            Dim allNames = head.Morphs.Select(Function(m) m.Name).ToList()
                        End If
                    Catch
                    End Try
                Else
                    ' PIRT (BodySlide) tri — different magic, just log size.
                End If
            Catch ex As Exception
            End Try
        Next

        ' MOUTH-NIF-PROBE: enumerate shapes (name + vert count) inside FemaleMouth.nif and its
        ' _faceBones variant so we know exactly what geometry the engine puts where.
        Dim mouthNifPaths = {
            "meshes\actors\character\characterassets\faceparts\femalemouth.nif",
            "meshes\actors\character\characterassets\faceparts\femalemouth_facebones.nif",
            "meshes\actors\character\characterassets\faceparts\femalemouthshadow.nif",
            "meshes\actors\character\characterassets\faceparts\femalemouthshadow_facebones.nif"
        }
        For Each np In mouthNifPaths
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(np, loc) Then
                Continue For
            End If
            Try
                Dim nifBytes = loc.GetBytes()
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then
                    Continue For
                End If
                Dim nif As New Nifcontent_Class_Manolo()
                nif.Load_Manolo(nifBytes)
                Dim shapes = nif.GetShapes()
                For Each sh In shapes
                    Dim shName = If(sh.Name IsNot Nothing, sh.Name.String, "<unnamed>")
                    Dim vCount As Integer = 0
                    Dim shKind As String = sh.GetType().Name
                    If ShapeGeometryFactory.IsSupported(sh) Then
                        Try
                            Dim geom = ShapeGeometryFactory.For(sh, nif)
                            vCount = geom.VertexCount
                        Catch
                        End Try
                    End If
                Next
            Catch ex As Exception
            End Try
        Next

        ' MOUTH-CHARGEN-PROBE: confirmed via .idx.bin scan that vanilla Meshes.ba2 ships
        ' MouthHumanChargen.tri + MouthShadowChargen.tri. The HDPT FemaleMouthHumanoidDefault
        ' does NOT declare them — only NAM0=1 → FemaleMouth.tri. Probe these two paths to see
        ' what sculpting morphs they contain (LipFeature*?) so we can decide if the mouth
        ' shape needs a per-shape chargen-tri override.
        Dim mouthChargenPaths = {
            "meshes\actors\character\characterassets\faceparts\mouthhumanchargen.tri",
            "meshes\actors\character\characterassets\faceparts\mouthshadowchargen.tri",
            "meshes\actors\character\characterassets\faceparts\femalemouth.tri",
            "meshes\actors\character\characterassets\faceparts\femalemouthshadow.tri",
            "meshes\actors\character\characterassets\faceparts\mouthhuman.tri",
            "meshes\actors\character\characterassets\faceparts\mouthshadow.tri"
        }
        For Each p In mouthChargenPaths
            Dim loc As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(p, loc) Then
                Continue For
            End If
            Try
                Dim bytes = loc.GetBytes()
                If bytes Is Nothing OrElse bytes.Length < 16 Then
                    Continue For
                End If
                Dim magic = System.Text.Encoding.ASCII.GetString(bytes, 0, 8)
                If magic.StartsWith("FRTRI") Then
                    Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
                    If head IsNot Nothing Then
                        Dim regularNames = head.Morphs.Where(Function(m) Not m.IsModMorph).Select(Function(m) m.Name).ToList()
                        Dim modNames = head.Morphs.Where(Function(m) m.IsModMorph).Select(Function(m) m.Name).ToList()
                    Else
                    End If
                Else
                End If
            Catch ex As Exception
            End Try
        Next

        If InvokeRequired Then
            BeginInvoke(Sub() ToolStripProgressBar1.Visible = False)
        Else
            ToolStripProgressBar1.Visible = False
        End If
    End Function




    ''' <summary>Compose face + body morph resolvers. Vanilla face FRTRI003 morphs and BodySlide
    ''' PIRT morphs travel through the same MorphPlan but never collide: each shape's resolver
    ''' lookup is keyed on its own .tri (face on FRTRI003, body on PIRT). MultiMorphResolver
    ''' merges channel lists; ApplyMorphPlan iterates them all per-shape uniformly.
    '''
    ''' Toggles are granular per-pipeline:
    '''   • CheckBoxApplyVertexMorphs gates the face FRTRI003 resolver only.
    '''   • CheckBoxBodyTri gates the body PIRT resolver only (inside BuildBodyMorphResolver).</summary>
    Friend Function BuildCompositeMorphResolver(state As NPCVisualState, renderData As PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _renderHost
        Dim face As IMorphResolver = Nothing
        If host.Toggles.ApplyVertexMorphs Then face = _morphPoseResolver.BuildFaceMorphResolver(state, renderData, host)
        Dim body = _morphPoseResolver.BuildBodyMorphResolver(state, renderData, host)
        ' Hair zap resolver: emite el/los canal(es) de zap para las shapes Hair {30,31} marcadas con
        ' ZapParts (Top/Long/Both) según el modelo complementario main/hairline. Gated por "Render
        ' headwear": OFF → no se engancha → la mesh se destapa en el próximo pase de morphs (igual que la
        ' oclusión de la mesh entera). Se incluye INDEPENDIENTE de los morphs face/body (un NPC con ambos
        ' morphs OFF igual debe zapear bajo gorra), y debe ser el ÚLTIMO delegate del composite así su
        ' canal de zap se agrega después de los canales de posición — el orden no afecta el resultado
        ' (zap = mask, position = vertex) pero mantiene el zap visible al final del plan.
        Dim hairTopZap = _morphPoseResolver.BuildHairTopZapResolver(renderData, host)

        ' Junta los delegates no-nulos. MultiMorphResolver filtra nulls, así que paso los tres.
        Dim delegates = New IMorphResolver() {face, body, hairTopZap}.Where(Function(r) r IsNot Nothing).ToArray()
        If delegates.Length = 0 Then Return Nothing
        If delegates.Length = 1 Then Return delegates(0)
        Return New MultiMorphResolver(delegates)
    End Function


    ''' <summary>Isolated bake-vs-app harness (CSV-only; zero library changes; zero global state mutation).
    '''
    ''' Loads fresh copies of the body + face skeleton NIFs from disk (same sources the app normally uses:
    ''' Config_App.Current.SkeletonFilePath for body, FaceSkeletonResolver for face), builds a local
    ''' per-bone bindT lookup by walking those fresh NIFs' NiNode hierarchies, then manually skins the
    ''' bake NIF's vertices with matsPose = matsBind (no Deltas, no Pose B, no MWGT, no FMRS). Compares
    ''' against the app's world-space render output and dumps a CSV alongside npc_preview.log.
    '''
    ''' Purpose: distinguish whether the ~0.16 RMS residual (Cait/Alijo vs CK FaceGen bake) comes from
    ''' Pose B / pipeline machinery or from elsewhere. Interpretation:
    '''   - RMS(V_raw, V_app) ≈ 0  → app matches bake via bindpose; Pose B is inocent (and unnecessary).
    '''   - RMS(V_raw, V_app) ≈ 0.16 → Pose B is not the culprit; residual is upstream in the app pipeline.
    '''   - Intermediate → datapoint; analyze bucketed by primary bone as the existing [FACEGEN-DIAG-WORLD].
    '''
    ''' Known contaminant: Alijo has MWGT.Fat > 0 and our app doesn't implement NNAM (neck-fat adjust).
    ''' Part of Alijo's Neck_skin residual is expected until NNAM is implemented. See memory P2.</summary>









    ''' <summary>Build the merged NPC pose: race-height + body-weight (MWGT×BSMS+MRSV+ARMA) + FMRS.
    ''' Order: race → body-weight → FMRS (top-down by skeleton hierarchy). The sources write to
    ''' disjoint field sets of PoseTransformData (race→Scale, BW→ScaleX/Y/Z, FMRS→T/R), so field-
    ''' level merging preserves each source's contribution even if the same bone appears in two.
    ''' See MergePoses for overlap detection (logs a [POSE-MERGE-OVERLAP] warning if sources collide
    ''' on the same field — should never fire with current sources).
    '''
    ''' Caller contract: <paramref name="skeleton"/> must already be loaded + (optionally) face/robot
    ''' merged BEFORE this is called, because BuildBodyWeightPose walks its hierarchy via
    ''' ResolveMrsvRegion to map bones to MRSV regions. RenderCurrentStateAsync primes the
    ''' SkeletonInstance via LoadFromKey + MergeRobotExtension + MergeAdditionalSkeleton(face) +
    ''' PrepareForShapes (cloth-inject — corre al final para ver el skeleton completo).</summary>
    ''' <summary>Builds a fresh per-NPC SkeletonInstance applying the three canonical sources
    ''' the engine uses for a race's bone hierarchy. Caller is responsible for ApplyPose
    ''' afterwards. Used to build the base skeleton and any per-ARMA clone (sculpt path).
    '''
    ''' Sources (en orden):
    '''   1) RACE.ANAM  — base skeleton declarado por la raza (puede ser un stub minimal de
    '''      pocos bones, como en robots; o el skeleton completo, como en humanos).
    '''   2) BPTD.MODL  — skeleton "real" apuntado por el record BPTD que cuelga de RACE.GNAM.
    '''      Para humanos coincide con RACE.ANAM (no-op). Para robots aporta el SkeletonRef.nif
    '''      (incluso cross-folder, como DLC01HandyCreateABot → DLC01\Robot\skeletonRefHandyDLC01.nif).
    '''      Esto reemplaza la heurística vieja MergeRobotExtendedSkeletonsIfRobot que enumeraba
    '''      siblings filesystem; el engine usa el pointer del record, así nosotros también.
    '''   3) Face bones convention (chargen-only) — sufijo `_[gender_]faceBones.nif` sibling del
    '''      RACE.ANAM. NO viene de records; es convención de filesystem que solo aporta bones
    '''      de cara (Jaw/LipUpper/etc) necesarios para chargen. No-op para razas sin face bones.
    '''
    ''' Orden importa: PrepareForShapes (cloth-bone injection) corre AL FINAL. Si lo llamáramos
    ''' antes del merge BPTD/face, los bones de esos skeletons aparecerían como "missing" y el
    ''' inject los buscaría en el cloth skeleton del NIF — no estarían ahí tampoco → fallo
    ''' silencioso (Debugger.Break en SkeletonClothOverlayHelper:96, sin log en release).</summary>
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

    ''' <summary>Check if pre-baked FaceGen NIF exists for this NPC.
    ''' Vanilla path: meshes\actors\character\facegendata\facegeom\&lt;plugin&gt;\&lt;formid:X8&gt;.nif
    ''' For templated NPCs, uses the model source FormID (the NPC that owns the visual traits).</summary>
    Private Function HasFaceGenAssets(state As NPCVisualState) As Boolean
        If state Is Nothing Then Return False
        Dim modelFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim path = ResolveFaceGenNifPath(modelFormID)
        Return path <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(path)
    End Function

    ''' <summary>Try to find a _faceBones.nif variant of the given mesh path.
    ''' Vanilla FO4 ships both <mesh>.nif (skinned to body bones only) and <mesh>_faceBones.nif
    ''' (skinned to face bones like Jaw/LipUpper/Cheek). The _faceBones variant is what the engine
    ''' uses at runtime for chargen/LooksMenu to enable FMRS bone deformation.
    ''' Returns the normalized _faceBones path if it exists in FilesDictionary, or empty string
    ''' if no variant exists. We do NOT filter by partType — empirically hair-region meshes
    ''' (e.g. femalehair28_hairline2.nif ? femalehair28_hairline2_facebones.nif) DO have variants,
    ''' so any partType filter would miss legitimate cases. Let the dictionary lookup decide.</summary>
    ''' <summary>Thin wrapper over <see cref="MeshPathHelpers.TryGetFaceBonesVariant"/>; same
    ''' helper module centralizes mesh path conventions for both render and offline bake.</summary>
    Private Function TryGetFaceBonesVariant(meshDictKey As String, partType As Integer) As String
        Return MeshPathHelpers.TryGetFaceBonesVariant(meshDictKey)
    End Function

    ''' <summary>Local FormID used in the FaceGen file name, per CK convention. Full plugins: strip the
    ''' high (load-order) byte (&amp; 0xFFFFFF). ESL/light plugins (high byte 0xFE): ALSO strip the 12-bit
    ''' light slot, leaving only the 12-bit record (&amp; 0xFFF). Mirrors <c>FaceGenBuilder.FaceGenLocalId</c>
    ''' (which is Private there, so the logic is duplicated here) — verified: ESL runtime 0xFE032800 →
    ''' CK writes "00000800", NOT "00032800". Without the ESL mask the light slot leaks into the name and
    ''' the game can't find the mesh/texture.</summary>
    Private Function FaceGenLocalId(npcFormID As UInteger) As UInteger
        If (npcFormID >> 24) = &HFEUI Then Return npcFormID And &HFFFUI
        Return npcFormID And &HFFFFFFUI
    End Function

    ''' <summary>Build the FaceGen NIF lookup path for a given NPC FormID.
    ''' Vanilla FO4 layout:
    '''   meshes\actors\character\facegendata\facegeom\&lt;plugin&gt;\&lt;formid:X8&gt;.nif   (the mesh)
    '''   textures\actors\character\facecustomization\&lt;plugin&gt;\&lt;formid:X8&gt;_d.dds (the texture)
    ''' Returns path normalized for FilesDictionary lookup, or empty if FormID can't be resolved.</summary>
    Private Function ResolveFaceGenNifPath(npcFormID As UInteger) As String
        If npcFormID = 0UI Then Return ""
        Dim pluginName = _pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(pluginName) Then Return ""
        ' FaceGen file name uses the LOCAL FormID, ESL-aware (FaceGenLocalId): full plugins strip the
        ' load-order byte, ESL plugins strip the load-order byte AND the 12-bit light slot.
        Dim localFormID = FaceGenLocalId(npcFormID)
        Return $"meshes\actors\character\facegendata\facegeom\{pluginName}\{localFormID:X8}.nif".ToLowerInvariant()
    End Function





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
            Dim kywds = _pluginManager.GetRecordsOfType("KYWD")
            If kywds IsNot Nothing Then
                For Each kw In kywds
                    If kw IsNot Nothing AndAlso String.Equals(kw.EditorID, "ArmorTypePower", StringComparison.OrdinalIgnoreCase) Then
                        _armorTypePowerKywdFid = kw.Header.FormID
                        Exit For
                    End If
                Next
            End If
        End If
        Return _armorTypePowerKywdFid
    End Function

    ''' <summary>True if the ARMO is power armor — carries the ArmorTypePower keyword. Cached per ARMO.</summary>
    Private Function ArmoIsPowerArmor(armoFID As UInteger) As Boolean
        If armoFID = 0UI Then Return False
        Dim kFid = ArmorTypePowerKeywordFid()
        If kFid = 0UI Then Return False
        Return _isPowerArmorArmoCache.GetOrAdd(armoFID,
            Function(fid)
                Dim a = _ctx.GetParsedArmo(fid)
                Return a IsNot Nothing AndAlso a.KeywordFormIDs.Contains(kFid)
            End Function)
    End Function

    ''' <summary>True if the race is a power-armor race — its RACE.WNAM (Skin) ARMO is power armor. Covers
    ''' vanilla PowerArmorRace + DLC/mod PA races without a hardcoded race list. Cached per race.</summary>
    Private Function RaceIsPowerArmor(raceFID As UInteger) As Boolean
        If raceFID = 0UI Then Return False
        Return _isPowerArmorRaceCache.GetOrAdd(raceFID,
            Function(fid)
                Dim rRec = _pluginManager.GetRecord(fid)
                If rRec Is Nothing OrElse rRec.Header.Signature <> "RACE" Then Return False
                Dim race = _ctx.ParseRaceCached(rRec)
                Return race IsNot Nothing AndAlso ArmoIsPowerArmor(race.SkinFormID)
            End Function)
    End Function








    ' Biped object slot bits (verified from wbDefinitionsFO4.pas:3745 wbBipedObjectFlags).
    ' Slot index = bit position + 30, so bit 0 = slot 30, bit 2 = slot 32, bit 16 = slot 46.
    ' Only the bits we actually use for head part occlusion are defined; body / hand slots
    ' (33/34/35) are handled implicitly by the "outfit wins over skin on same slot" loop in
    ' SelectWinningCandidates, no constants needed there.
    Friend Const SlotBitHairTop As UInteger = &H1UI         ' Slot 30 - Hair Top      (sombreros, gorros, cualquier headwear)
    Friend Const SlotBitHairLong As UInteger = &H2UI        ' Slot 31 - Hair Long     (cascos que cubren el largo del pelo)
    Friend Const SlotBitFaceGenHead As UInteger = &H4UI     ' Slot 32 - FaceGen Head  (casco integral / vault helmet — cubre LA CARA entera)
    Private Const SlotBitHeadband As UInteger = &H10000UI    ' Slot 46 - Headband      (bandana / hairband forehead, no cubre cara)
    Private Const SlotBitEyes As UInteger = &H20000UI        ' Slot 47 - Eyes          (glasses, goggles)
    Friend Const SlotBitBeard As UInteger = &H40000UI       ' Slot 48 - Beard         (algo equipable que pisa la zona barba)
    Friend Const SlotBitMouth As UInteger = &H80000UI       ' Slot 49 - Mouth         (bandana, máscara quirúrgica, gas mask boca)
    ' Slots 50-52 — nombres canónicos en wbDefinitionsFO4.pas:3766-3768. Categorización:
    '   • Neck (50)  → headwear: bandana de cuello, collar, bufanda. Es prenda equipable.
    '   • Ring (51)  → body (mano): anillo, accesorio de mano. Cae en HAND_MASK.
    '   • Scalp (52) → body (cabeza/cuello): overlay que sigue al body skin, no es prenda
    '                  equipable. Tratado como BODY (agregado a BODY_MASK en ClassifyShapeCategory).
    ' Slot 53 (Decapitation) NO se clasifica: es geometría de gore que aparece tras desmembrar
    ' al actor, no una prenda equipable. Slots 54-55 son "Unnamed" en xEdit (sin uso vanilla) —
    ' los dejamos fuera de toda máscara para no asignarlos al toggle equivocado.
    Private Const SlotBitNeck As UInteger = &H100000UI       ' Slot 50 - Neck          (bandana cuello, collar, bufanda)
    Friend Const SlotBitRing As UInteger = &H200000UI       ' Slot 51 - Ring          (anillo — body, va en la mano)
    Friend Const SlotBitScalp As UInteger = &H400000UI      ' Slot 52 - Scalp         (overlay cabeza/cuello — body, no prenda)
    Private Const SlotBitALArm As UInteger = &H1000UI        ' Slot 42 - [A] L Arm     (over-armor antebrazo izquierdo — bracer, PA L Arm)
    Friend Const SlotBitPipboy As UInteger = &H40000000UI   ' Slot 60 - Pipboy        (atado a la muñeca/antebrazo izquierdo)
    ''' <summary>Máscara unificada de bits "headwear": cualquier prenda de cabeza/cara/cuello.
    ''' Usada por ClassifyShapeCategory para categoría Headwear y por ApplyRenderToggleVisibility
    ''' para el toggle "Render headwear". Slots 30-32 (HairTop/HairLong/FaceGenHead) + 46-49
    ''' (Headband/Eyes/Beard/Mouth) + 50 (Neck). Ring (51) y Scalp (52) NO están acá — son body.</summary>
    Friend Const HEADWEAR_MASK As UInteger = SlotBitHairTop Or SlotBitHairLong Or SlotBitFaceGenHead Or
                                              SlotBitHeadband Or SlotBitEyes Or SlotBitBeard Or SlotBitMouth Or
                                              SlotBitNeck







    ''' <summary>Strict per-ARMA race match: the ARMA's RaceFormID equals the NPC's race, or its
    ''' AdditionalRaces (MODL) include it. Unified rule used by the render AND the skin/outfit/item
    ''' pickers. The permissive "arma.RaceFormID = 0 → any race" clause was REMOVED per user
    ''' 2026-05-24: a load-order sweep found 0 of 1084 ARMAs with RaceFormID=0 (all declare a race),
    ''' so the clause was dead — strict is preferred and unifies render + pickers on one rule. The
    ''' npcRaceFormID=0 guard stays: it keeps a degenerate NPC whose race didn't resolve from
    ''' rendering naked.</summary>
    Friend Shared Function ArmorAddonMatchesRace(arma As ARMA_Data, npcRaceFormID As UInteger) As Boolean
        If npcRaceFormID = 0UI Then Return True
        If arma.RaceFormID = npcRaceFormID Then Return True
        Return arma.AdditionalRaces.Contains(npcRaceFormID)
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

        If npc Is Nothing Then
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
            Return
        End If

        Try
            LabelRecordTitle.Text = $"  {npc} [{npc.PluginName}] FormID:{npc.FormID:X8}"

            ' --- Header ---
            Dim headerNode = AddNode(Nothing, $"NPC_ {npc.EditorID}  [{npc.FormID:X8}]  {npc.PluginName}")
            AddNode(headerNode, $"Full Name: {If(npc.FullName <> "", npc.FullName, "(none)")}")
            AddNode(headerNode, $"Editor ID: {npc.EditorID}")
            AddNode(headerNode, $"Form ID: {npc.FormID:X8}")
            AddNode(headerNode, $"Plugin: {npc.PluginName}")
            AddNode(headerNode, $"Gender: {If(npc.IsFemale, "Female", "Male")}")
            headerNode.Expand()

            ' --- Template Info ---
            If npc.TemplateFormID <> 0UI OrElse npc.TemplateActorFormIDs.Count > 0 Then
                Dim tplNode = AddNode(Nothing, $"Template Configuration  (flags: {npc.TemplateFlags:X4})")
                If npc.TemplateFormID <> 0UI Then
                    AddNode(tplNode, $"Base Template (TPLT): {DescribeFormID(npc.TemplateFormID)}")
                End If
                For Each kvp In npc.TemplateActorFormIDs
                    AddNode(tplNode, $"TPTA[{kvp.Key}] ({NpcManagerFormat.GetTemplateCategoryLabel(kvp.Key)}): {DescribeFormID(kvp.Value)}")
                Next
                Dim flagList As New List(Of String)
                For Each boxedCat In [Enum].GetValues(GetType(NPC_TemplateCategory))
                    Dim cat = CType(boxedCat, NPC_TemplateCategory)
                    If NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, cat) Then flagList.Add(NpcManagerFormat.GetTemplateCategoryLabel(cat))
                Next
                If flagList.Count > 0 Then AddNode(tplNode, $"Active flags: {String.Join(", ", flagList)}")
                tplNode.Expand()
            End If

            ' --- Traits (with inheritance) ---
            Dim traitsSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.Traits)
            Dim traitsNpc = If(traitsSource, npc)
            Dim traitsLabel = If(traitsSource IsNot Nothing AndAlso traitsSource.FormID <> npc.FormID,
                                 $"Traits  (inherited from {NpcManagerFormat.DescribeNpc(traitsSource)} [{traitsSource.FormID:X8}])",
                                 "Traits  (own)")
            Dim traitsNode = AddNode(Nothing, traitsLabel)
            AddNode(traitsNode, $"Race: {DescribeFormID(traitsNpc.RaceFormID)}")
            ExpandRaceDetails(traitsNode, traitsNpc.RaceFormID, traitsNpc.IsFemale)
            If traitsNpc.SkinFormID <> 0UI Then AddNode(traitsNode, $"Skin Armor: {DescribeFormID(traitsNpc.SkinFormID)}")
            Dim fmtMwgt = Function(v As Single?) If(v.HasValue, v.Value.ToString("F2"), "Default")
            AddNode(traitsNode, $"Weight: Thin={fmtMwgt(traitsNpc.WeightThin)}  Muscular={fmtMwgt(traitsNpc.WeightMuscular)}  Fat={fmtMwgt(traitsNpc.WeightFat)}")
            If traitsNpc.BodyMorphRegionValues.Count > 0 Then
                Dim morphNode = AddNode(traitsNode, $"Body Morph Regions ({traitsNpc.BodyMorphRegionValues.Count} values)")
                For i = 0 To traitsNpc.BodyMorphRegionValues.Count - 1
                    AddNode(morphNode, $"[{i}] = {traitsNpc.BodyMorphRegionValues(i):F4}")
                Next
            End If
            traitsNode.Expand()

            ' --- Inventory (with inheritance) ---
            Dim invSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.Inventory)
            Dim invNpc = If(invSource, npc)
            Dim invLabel = If(invSource IsNot Nothing AndAlso invSource.FormID <> npc.FormID,
                              $"Inventory  (inherited from {NpcManagerFormat.DescribeNpc(invSource)} [{invSource.FormID:X8}])",
                              "Inventory  (own)")
            Dim invNode = AddNode(Nothing, invLabel)
            If invNpc.DefaultOutfitFormID <> 0UI Then
                Dim outfitNode = AddNode(invNode, $"Default Outfit: {DescribeFormID(invNpc.DefaultOutfitFormID)}")
                ExpandOutfitDetails(outfitNode, invNpc.DefaultOutfitFormID)
            Else
                AddNode(invNode, "Default Outfit: (none)")
            End If
            If invNpc.SleepOutfitFormID <> 0UI Then
                Dim sleepNode = AddNode(invNode, $"Sleep Outfit: {DescribeFormID(invNpc.SleepOutfitFormID)}")
                ExpandOutfitDetails(sleepNode, invNpc.SleepOutfitFormID)
            End If
            invNode.Expand()

            ' --- Model / Appearance (with inheritance) ---
            Dim modelSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.ModelAnimation)
            Dim modelNpc = If(modelSource, npc)
            Dim modelLabel = If(modelSource IsNot Nothing AndAlso modelSource.FormID <> npc.FormID,
                                $"Appearance  (inherited from {NpcManagerFormat.DescribeNpc(modelSource)} [{modelSource.FormID:X8}])",
                                "Appearance  (own)")
            Dim modelNode = AddNode(Nothing, modelLabel)
            If modelNpc.HeadTextureFormID <> 0UI Then AddNode(modelNode, $"Head Texture: {DescribeFormID(modelNpc.HeadTextureFormID)}")
            If modelNpc.HairColorFormID <> 0UI Then AddNode(modelNode, $"Hair Color: {DescribeFormID(modelNpc.HairColorFormID)}")
            If modelNpc.FacialHairColorFormID <> 0UI Then AddNode(modelNode, $"Facial Hair Color: {DescribeFormID(modelNpc.FacialHairColorFormID)}")
            If modelNpc.HasTextureLighting Then AddNode(modelNode, $"Texture Lighting: R={modelNpc.TextureLightingColor.R} G={modelNpc.TextureLightingColor.G} B={modelNpc.TextureLightingColor.B}")

            ' Head Parts
            If modelNpc.HeadPartFormIDs.Count > 0 Then
                Dim hpNode = AddNode(modelNode, $"Head Parts ({modelNpc.HeadPartFormIDs.Count})")
                For Each hpFormID In modelNpc.HeadPartFormIDs
                    Dim hpRec = _pluginManager.GetRecord(hpFormID)
                    If hpRec IsNot Nothing Then
                        Dim hdpt = _ctx.ParseHdptCached(hpRec)
                        Dim typeName = NpcManagerFormat.GetHeadPartTypeName(hdpt.PartType)
                        Dim hpChildNode = AddNode(hpNode, $"[{typeName}] {hdpt.EditorID}  [{hpFormID:X8}]")
                        If hdpt.MeshPath <> "" Then AddNode(hpChildNode, $"Mesh: {hdpt.MeshPath}")
                        If hdpt.TextureSetFormID <> 0UI Then AddNode(hpChildNode, $"TextureSet: {DescribeFormID(hdpt.TextureSetFormID)}")
                        If hdpt.ColorFormID <> 0UI Then AddNode(hpChildNode, $"Color: {DescribeFormID(hdpt.ColorFormID)}")
                        If hdpt.ExtraPartFormIDs.Count > 0 Then
                            For Each epId In hdpt.ExtraPartFormIDs
                                AddNode(hpChildNode, $"Extra Part: {DescribeFormID(epId)}")
                            Next
                        End If
                    Else
                        AddNode(hpNode, $"HDPT [{hpFormID:X8}] (record not found)")
                    End If
                Next
                hpNode.Expand()
            End If

            ' Face Morphs
            If modelNpc.MorphValues.Count > 0 Then
                Dim morphNode = AddNode(modelNode, $"Face Morph Presets ({modelNpc.MorphValues.Count})")
                For Each kvp In modelNpc.MorphValues
                    AddNode(morphNode, $"Key {kvp.Key:X8} = {kvp.Value:F4}")
                Next
            End If

            ' Face Morph Sculpting
            If modelNpc.FaceMorphs.Count > 0 Then
                Dim fmNode = AddNode(modelNode, $"Face Morph Sculpting ({modelNpc.FaceMorphs.Count} morphs)")
                For Each fm In modelNpc.FaceMorphs
                    AddNode(fmNode, $"Morph {fm.Index:X8}: {fm.Values.Count} values")
                Next
            End If

            ' Tint Layers
            If modelNpc.FaceTintLayers.Count > 0 Then
                Dim tintNode = AddNode(modelNode, $"Face Tint Layers ({modelNpc.FaceTintLayers.Count})")
                For Each tl In modelNpc.FaceTintLayers
                    Dim colorStr = If(tl.Color <> Color.Empty, $" Color:({tl.Color.R},{tl.Color.G},{tl.Color.B},{tl.Color.A})", "")
                    AddNode(tintNode, $"Discr:{tl.Discriminator} Index:{tl.Index} Value:{tl.Value}{colorStr}")
                Next
            End If
            modelNode.Expand()

        Finally
            TreeViewRecordDetails.EndUpdate()
            TreeViewRecordDetails.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Follow template chain for a category and return the terminal NPC that provides the value.</summary>
    Private Function ResolveInheritedSourceNpc(npc As NPC_Data, category As NPC_TemplateCategory) As NPC_Data
        If npc Is Nothing OrElse Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, category) Then Return npc

        Dim visited As New HashSet(Of UInteger)
        Dim current = npc

        While current IsNot Nothing
            If visited.Contains(current.FormID) Then Exit While
            visited.Add(current.FormID)

            If Not NpcTemplateHelpers.HasTemplateFlag(current.TemplateFlags, category) Then Return current

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
                Dim lvln = RecordParsers.ParseLVLN(sourceRec, _pluginManager)
                Dim firstNpcId = lvln.Entries.Select(Function(e) e.FormID).
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

        Dim race = _ctx.ParseRaceCached(raceRec)
        Dim raceNode = AddNode(parentNode, $"Race: {race.FullName} [{race.EditorID}]")
        If isFemale Then
            If race.FemaleSkeletonPath <> "" Then AddNode(raceNode, $"Skeleton: {race.FemaleSkeletonPath}")
            If race.FemaleBodyMeshes.Count > 0 Then
                For Each mesh In race.FemaleBodyMeshes
                    AddNode(raceNode, $"Body Mesh: {mesh}")
                Next
            End If
            If race.FemaleDefaultFaceTextureFormID <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(race.FemaleDefaultFaceTextureFormID)}")
        Else
            If race.MaleSkeletonPath <> "" Then AddNode(raceNode, $"Skeleton: {race.MaleSkeletonPath}")
            If race.MaleBodyMeshes.Count > 0 Then
                For Each mesh In race.MaleBodyMeshes
                    AddNode(raceNode, $"Body Mesh: {mesh}")
                Next
            End If
            If race.MaleDefaultFaceTextureFormID <> 0UI Then AddNode(raceNode, $"Default Face Texture: {DescribeFormID(race.MaleDefaultFaceTextureFormID)}")
        End If
        If race.SkinFormID <> 0UI Then AddNode(raceNode, $"Race Skin: {DescribeFormID(race.SkinFormID)}")
    End Sub

    Private Sub ExpandOutfitDetails(parentNode As TreeNode, outfitFormID As UInteger)
        If outfitFormID = 0UI Then Return
        Dim outfitRec = _pluginManager.GetRecord(outfitFormID)
        If outfitRec Is Nothing Then Return

        If outfitRec.Header.Signature = "OTFT" Then
            Dim otft = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
            For Each itemFormID In otft.ItemFormIDs
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
                Dim slotStr = NpcManagerFormat.FormatSlotMask(armo.SlotMask)
                Dim armoNode = AddNode(parentNode, $"ARMO {armo.EditorID}  ""{armo.FullName}""  [{armo.FormID:X8}]  Slots:{slotStr}")

                ' Follow template armor
                If armo.TemplateArmorFormID <> 0UI Then
                    AddNode(armoNode, $"Template Armor: {DescribeFormID(armo.TemplateArmorFormID)}")
                End If

                ' Armor Addons
                For Each aaFormID In armo.ArmorAddonFormIDs
                    Dim aaRec = _pluginManager.GetRecord(aaFormID)
                    If aaRec Is Nothing OrElse aaRec.Header.Signature <> "ARMA" Then
                        AddNode(armoNode, $"ARMA [{aaFormID:X8}] (missing)")
                        Continue For
                    End If
                    Dim arma = _ctx.GetParsedArma(aaFormID)
                    Dim aaNode = AddNode(armoNode, $"ARMA {arma.EditorID}  [{arma.FormID:X8}]  Slots:{NpcManagerFormat.FormatSlotMask(arma.SlotMask)}")
                    If arma.MaleMeshPath <> "" Then AddNode(aaNode, $"Male Mesh: {arma.MaleMeshPath}")
                    If arma.FemaleMeshPath <> "" Then AddNode(aaNode, $"Female Mesh: {arma.FemaleMeshPath}")
                    If arma.MaleFPMeshPath <> "" Then AddNode(aaNode, $"Male 1P Mesh: {arma.MaleFPMeshPath}")
                    If arma.FemaleFPMeshPath <> "" Then AddNode(aaNode, $"Female 1P Mesh: {arma.FemaleFPMeshPath}")
                    If arma.MaleSkinTextureFormID <> 0UI Then AddNode(aaNode, $"Male Skin Texture: {DescribeFormID(arma.MaleSkinTextureFormID)}")
                    If arma.FemaleSkinTextureFormID <> 0UI Then AddNode(aaNode, $"Female Skin Texture: {DescribeFormID(arma.FemaleSkinTextureFormID)}")
                    If arma.MaleMaterialSwapFormID <> 0UI Then AddNode(aaNode, $"Male Material Swap: {DescribeFormID(arma.MaleMaterialSwapFormID)}")
                    If arma.FemaleMaterialSwapFormID <> 0UI Then AddNode(aaNode, $"Female Material Swap: {DescribeFormID(arma.FemaleMaterialSwapFormID)}")
                    If arma.AdditionalRaces.Count > 0 Then
                        For Each raceId In arma.AdditionalRaces
                            AddNode(aaNode, $"Additional Race: {DescribeFormID(raceId)}")
                        Next
                    End If
                Next

            Case "LVLI"
                Dim lvli = RecordParsers.ParseLVLI(itemRec, _pluginManager)
                Dim lvliNode = AddNode(parentNode, $"LVLI {lvli.EditorID}  [{lvli.FormID:X8}]  ({lvli.Entries.Count} entries)")
                For Each entry In lvli.Entries
                    ExpandOutfitItem(lvliNode, entry.FormID)
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

    ''' <summary>HDPT PNAM Type enum names (verified wbDefinitionsFO4.pas:7373).</summary>
#End Region

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' Persist UI-level config BEFORE teardown. Setting_Lightrig lives in shared Config_App
        ' (written in-memory by LightRigForm); RenderGore is NPC-only and lives in NPC_Config.
        NPC_Config.Current.RenderGore = CheckBoxRenderGore.Checked
        NPC_Config.Current.ShowCatUnique = CheckBoxCatUnique.Checked
        NPC_Config.Current.ShowCatGeneric = CheckBoxCatGeneric.Checked
        NPC_Config.Current.ShowCatTemplate = CheckBoxCatTemplate.Checked
        NPC_Config.Current.ShowCatUnused = CheckBoxCatUnused.Checked
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

        Try
            _selectionDebounceTimer.Stop()
            _selectionDebounceTimer.Dispose()
        Catch
        End Try

        If _multiSelectBrush IsNot Nothing Then
            _multiSelectBrush.Dispose()
            _multiSelectBrush = Nothing
        End If
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

    ''' <summary>NPCs the user has changed this session — drives the bold rendering in
    ''' <see cref="TreeViewNPCs_DrawNode"/>. Set on each editor commit (Load LM / Edit Face /
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

    ''' <summary>FormID of the NPC node the tree context menu was opened on. Set in
    ''' <see cref="TreeViewNPCs_NodeMouseClick"/>, consumed by the Mark/Reset menu handlers.</summary>
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
        Dim race As RACE_Data = Nothing
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec IsNot Nothing Then
            race = _ctx.ParseRaceCached(raceRec)
            If race IsNot Nothing AndAlso Not String.IsNullOrEmpty(race.EditorID) Then
                raceDisplay = race.EditorID
            End If
        End If
        Dim gender As Byte = If(_renderHost.CurrentBaseState.IsFemale, CByte(1), CByte(0))
        Dim raceDefaultsForLm As IEnumerable(Of UInteger) =
            If(_renderHost.CurrentBaseState.IsFemale, race?.FemaleHeadPartFormIDs, race?.MaleHeadPartFormIDs)

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
        Dim applyBody As Boolean = npcHasBodyTri
        Dim dialogResult As DialogResult
        Using dlg As New LooksmenuLoad_Form(_pluginManager, _dataPath, gender, raceDisplay, npcHasBodyTri,
                                            raceFormID, race, raceDefaultsForLm)
            AddHandler dlg.PreviewRequested, Sub(s, args) PreviewLooksmenuOverlay(npcFormID, npc, args.Preset, args.ApplyBodySliders)
            dialogResult = dlg.ShowDialog(Me)
            selected = dlg.SelectedPreset
            applyBody = dlg.ApplyBodySliders
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

        ' Per-HeadPart breakdown so we can spot when the preset actually declared Eyes/Hair but the
        ' merger discarded them (meaning we're losing them somewhere) vs. when the JSON simply
        ' didn't have those types (so the merger fell back to RACE defaults — expected).
        Dim hpTypeNames = New String() {"Misc", "Face", "Eyes", "Hair", "FacialHair", "Scar", "Eyebrows", "Meatcaps", "Teeth", "HeadRear"}
        For Each fid In selected.HeadPartFormIDs
            Dim rec = _pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then
                Continue For
            End If
            Dim hd = _ctx.ParseHdptCached(rec)
            Dim typeLabel = If(hd.PartType >= 0 AndAlso hd.PartType < hpTypeNames.Length, hpTypeNames(hd.PartType), $"type={hd.PartType}")
        Next
        If selected.UnresolvedHeadParts.Count > 0 Then
            For Each raw In selected.UnresolvedHeadParts
            Next
        End If

    End Sub

    ''' <summary>Live-preview handler invoked by <see cref="LooksmenuLoad_Form.PreviewRequested"/>
    ''' on every selection change. Applies (or removes) the overlay and triggers a non-blocking
    ''' re-render. Concurrency-safe via _previewRequestVersion: rapid clicks supersede each other,
    ''' only the latest survives.</summary>
    Private Sub PreviewLooksmenuOverlay(npcFormID As UInteger, npc As NPC_Data, preset As LooksmenuLoader.LooksmenuPreset, applyBodySliders As Boolean)
        If preset Is Nothing Then
            _appliedPresets.Remove(npcFormID)
        Else
            ' Respect the dialog's "Apply BodySlide sliders" checkbox: when unchecked, strip the
            ' dict before stamping the overlay so the resolver never sees them. We clone the
            ' preset so the dialog's parsed object stays intact (in case the user toggles the
            ' checkbox back on without re-selecting).
            Dim toApply = If(applyBodySliders, preset, ClonePresetWithoutBodySliders(preset))
            ' WYSIWYG: if the loaded JSON references an LM SkinTemplate, materialize its head/
            ' headRear HDPT swaps into preset.HeadPartFormIDs so Save ESP / Edit Face / Copy see
            ' the same picture the live render shows via ApplyPresetOverlayToNpcData. The template
            ' bundle is otherwise applied only to the runtime shadow; without this call the JSON
            ' could be loaded, the preview would render the template HDPTs, but exporting to ESP
            ' would emit raw NPC PNAM (no headRear swap).
            NpcRecordOverlay.MaterializeLmTemplateBundleToPreset(toApply, npc.IsFemale, AddressOf ResolveLmSkinTemplate)
            ' Normalizar el TemplateColorIndex de TODOS los layers re-derivándolo desde el Color — misma
            ' resolución que Copy/Paste hace en BuildPresetFromState. El load de LooksMenu toma el "ColorID"
            ' crudo del JSON, que no siempre coincide con el TemplateIndex del RACE; sin esto el resolver del
            ' render (match por TemplateIndex) no encuentra la entrada y el layer cae a su color crudo
            ' (skin-tone slot-12 -> pálido/blanco). Idempotente; no-op en no-Palette.
            Dim raceForTintNorm As RACE_Data = Nothing
            Dim raceRecForTintNorm = _pluginManager.GetRecord(npc.RaceFormID)
            If raceRecForTintNorm IsNot Nothing AndAlso raceRecForTintNorm.Header.Signature = "RACE" Then
                raceForTintNorm = _ctx.ParseRaceCached(raceRecForTintNorm)
            End If
            NormalizePresetTintTemplateColorIds(toApply, raceForTintNorm, npc.IsFemale)
            _appliedPresets(npcFormID) = toApply
        End If
        Dim previewVersion = Interlocked.Increment(_previewRequestVersion)
        ' Fire-and-forget: the Async lambda runs on the UI sync context (LoadNPCOnDemandAsyncFromExisting
        ' already marshals back to the UI thread for the render). Errors are swallowed silently here —
        ' the user is mid-selection and a popup would be more disruptive than a stale preview.
        Dim _unused = PreviewLooksmenuOverlayAsync(npc, previewVersion)
    End Sub

    ''' <summary>Deep-clone a preset and zero out BodyMorphSliders. Used by the LooksMenu Load
    ''' preview path when the dialog's "Apply BodySlide sliders" checkbox is OFF: we drop the
    ''' slider dict but keep every other field (tints/headparts/morphs/etc.) intact.
    '''
    ''' Delegates to LooksmenuLoader.ClonePreset (canonical) + then clears the slider dict.
    ''' That guarantees Has* flags and any other future field stay in sync with the rest of
    ''' the codebase without needing per-call-site updates.</summary>
    Private Shared Function ClonePresetWithoutBodySliders(p As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        Dim c = LooksmenuLoader.ClonePreset(p)
        If c IsNot Nothing Then c.BodyMorphSliders.Clear()
        Return c
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
        Await Task.Run(Sub()
                           baseState = _stateResolver.ResolveNPCBaseState(npc, host)
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
            _faceTintResolver.ClearFaceTintCaches()
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

    ''' <summary>If an overlay is registered for <paramref name="selectedNpcFormID"/>, return a
    ''' shallow copy of <paramref name="raw"/> with the preset's morph/face-tint fields swapped
    ''' in. The overlay is keyed by the NPC the user selected, NOT by the model template source —
    ''' so a preset on Piper does not bleed into other NPCs that share Piper's template chain.
    ''' Returns <paramref name="raw"/> unchanged when there's no overlay.
    '''
    ''' Per-field semantics replicate the engine's LoadPreset (CharGenInterface.cpp:259-628):
    '''   • HeadParts (line 308-321 + 323-342): the engine WIPES the actor's HeadParts list and
    '''     repopulates with the race chargen defaults FIRST, then iterates JSON HeadParts and
    '''     applies each via ChangeHeadPart. We follow the same shape — start from race defaults,
    '''     then merge preset entries in (the downstream MergeHeadPartsWithRaceDefaults handles
    '''     the per-PartType "preset wins, race fills gaps" logic). For NPC_Manager preview the
    '''     "race defaults" must come from the RACE record because the raw NPC_Data carries its
    '''     own PNAM list which is exactly what the preset is replacing.
    '''   • HairColor (line 344-359): if the JSON identifier doesn't resolve, GetFormFromIdentifier
    '''     returns nullptr and the if(form) guard skips assignment → preserves the actor's value.
    '''     We replicate: preset.HairColorFormID == 0 means "not in JSON, preserve raw".
    '''   • Weight (line 466-475): the engine assigns root["Weight"][i].asFloat() unconditionally;
    '''     a missing field becomes 0.0. Our parser leaves WeightX as Single?=Nothing when absent,
    '''     and we preserve raw weights in that case (more useful for Paste between NPCs than
    '''     reproducing the engine's "missing = zero" quirk that breaks body weight visually).
    '''   • Morphs.Values / Presets / Regions (line 363-450): the engine only clears + repopulates
    '''     when members.size() > 0. Empty/missing dicts/arrays preserve the actor's values.
    '''   • Morphs.Intensity (line 452-464): the engine ALWAYS calls SetFacialBoneMorphIntensity,
    '''     using 1.0 when missing. Our parser already defaults to 1.0F at parse time, so we
    '''     always overwrite — semantically equivalent.
    '''   • Tints (line 477-556): the engine ClearCharacterTints unconditionally when there's a
    '''     non-empty Tints dict; if the JSON has Tints with 0 members it still allocates the
    '''     array but doesn't push anything. We mirror: presence of preset means we replace the
    '''     tint list (even with empty); absence means preserve raw. The parser doesn't currently
    '''     distinguish "JSON had Tints:{}" from "no Tints key" but BuildPresetFromState only
    '''     populates FaceTintLayers when there are layers to capture, so empty == absent here.
    ''' </summary>
    ''' <summary>Thin instance wrapper over <see cref="NpcRecordOverlay.ApplyPresetOverlayToNpcData"/>;
    ''' threads <see cref="_pluginManager"/> + <see cref="_appliedPresets"/> through. Real impl
    ''' lives in the helper module so offline bake (FaceGenBuilder) can reuse without coupling
    ''' to MainForm instance state.</summary>
    Private Function ApplyPresetOverlayToNpcData(raw As NPC_Data, selectedNpcFormID As UInteger) As NPC_Data
        Return NpcRecordOverlay.ApplyPresetOverlayToNpcData(raw, selectedNpcFormID, _appliedPresets,
                                                            _pluginManager, AddressOf ResolveLmSkinTemplate,
                                                            AddressOf _ctx.ParseRaceCached)
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

    ''' <summary>Per-layer clone — delegates to the canonical helper.</summary>
    Private Function CloneFaceTint(tl As NPC_FaceTintLayerData) As NPC_FaceTintLayerData
        Return LooksmenuLoader.CloneFaceTintLayer(tl)
    End Function

    ''' <summary>Copy the round-trip-only NPC_Data fields from the raw parse to the shadow
    ''' produced by ApplyPresetOverlayToNpcData. The shadow only carries renderer-relevant
    ''' state (tints, morphs, headparts, etc.); fields like Vmad raw bytes, ACBS struct,
    ''' OBND, Factions, AI data, Object Template combinations, etc. — needed for byte-
    ''' equivalent re-emission by the writer — are NOT in the shadow.
    '''
    ''' This is a stop-gap so Save ESP can leverage the existing overlay helper without
    ''' rewriting the shadow. The cleaner long-term path would be to make
    ''' ApplyPresetOverlayToNpcData produce a full clone, but the renderer doesn't need it
    ''' and the helper has been stable for a year — touching it carries regression risk.</summary>
    Private Sub CopyRoundTripOnlyFieldsFromRaw(raw As NPC_Data, shadow As NPC_Data)
        ' VMAD raw payload (scripts) — must be preserved verbatim with FormID positions
        ' so SaveNpcEspWriter can re-emit it byte-equivalent under the new MAST list.
        shadow.Vmad = raw.Vmad
        ' OBND object bounds (12 bytes 6×s16). Required subrecord.
        shadow.ObjectBoundsRaw = raw.ObjectBoundsRaw
        ' ACBS struct (Flags + LevelOrLevelMult + CalcMin/Max + Disposition + TemplateFlags +
        ' BleedoutOverride + trailing bytes). Required.
        shadow.Acbs = raw.Acbs
        ' Optional/companion FormID fields with paired Has-flags.
        shadow.PreviewTransformFormID = raw.PreviewTransformFormID
        shadow.HasPreviewTransform = raw.HasPreviewTransform
        shadow.AnimationSoundFormID = raw.AnimationSoundFormID
        shadow.HasAnimationSound = raw.HasAnimationSound
        shadow.DeathItemFormID = raw.DeathItemFormID
        shadow.HasDeathItem = raw.HasDeathItem
        shadow.VoiceFormID = raw.VoiceFormID
        shadow.HasVoice = raw.HasVoice
        shadow.LegendaryTemplateFormID = raw.LegendaryTemplateFormID
        shadow.HasLegendaryTemplate = raw.HasLegendaryTemplate
        shadow.LegendaryChanceFormID = raw.LegendaryChanceFormID
        shadow.HasLegendaryChance = raw.HasLegendaryChance
        shadow.HasRace = raw.HasRace
        shadow.HasSpctCounter = raw.HasSpctCounter
        shadow.HasSkin = raw.HasSkin
        shadow.FarAwayModelFormID = raw.FarAwayModelFormID
        shadow.HasFarAwayModel = raw.HasFarAwayModel
        shadow.AttackRaceFormID = raw.AttackRaceFormID
        shadow.HasAttackRace = raw.HasAttackRace
        shadow.SpectatorOverrideFormID = raw.SpectatorOverrideFormID
        shadow.HasSpectatorOverride = raw.HasSpectatorOverride
        shadow.ObserveDeadBodyOverrideFormID = raw.ObserveDeadBodyOverrideFormID
        shadow.HasObserveDeadBodyOverride = raw.HasObserveDeadBodyOverride
        shadow.GuardWarnOverrideFormID = raw.GuardWarnOverrideFormID
        shadow.HasGuardWarnOverride = raw.HasGuardWarnOverride
        shadow.CombatOverrideFormID = raw.CombatOverrideFormID
        shadow.HasCombatOverride = raw.HasCombatOverride
        shadow.FollowerCommandFormID = raw.FollowerCommandFormID
        shadow.HasFollowerCommand = raw.HasFollowerCommand
        shadow.FollowerElevatorFormID = raw.FollowerElevatorFormID
        shadow.HasFollowerElevator = raw.HasFollowerElevator
        shadow.HasPrkzCounter = raw.HasPrkzCounter
        shadow.ForcedLocRefTypeFormID = raw.ForcedLocRefTypeFormID
        shadow.HasForcedLocRefType = raw.HasForcedLocRefType
        shadow.NativeTerminalFormID = raw.NativeTerminalFormID
        shadow.HasNativeTerminal = raw.HasNativeTerminal
        shadow.HasCoctCounter = raw.HasCoctCounter
        shadow.AiData = raw.AiData
        shadow.HasKsizCounter = raw.HasKsizCounter
        shadow.HasObjectTemplate = raw.HasObjectTemplate
        shadow.ClassFormID = raw.ClassFormID
        shadow.HasClass = raw.HasClass
        shadow.ShortName = raw.ShortName
        shadow.HasShortName = raw.HasShortName
        shadow.HasDataMarker = raw.HasDataMarker
        shadow.CalculatedStats = raw.CalculatedStats
        shadow.CombatStyleFormID = raw.CombatStyleFormID
        shadow.HasCombatStyle = raw.HasCombatStyle
        shadow.GiftFilterFormID = raw.GiftFilterFormID
        shadow.HasGiftFilter = raw.HasGiftFilter
        shadow.Nam5Raw = raw.Nam5Raw
        shadow.HeightMin = raw.HeightMin
        shadow.HasHeightMin = raw.HasHeightMin
        shadow.Nam7Raw = raw.Nam7Raw
        shadow.HeightMax = raw.HeightMax
        shadow.HasHeightMax = raw.HasHeightMax
        shadow.SoundLevel = raw.SoundLevel
        shadow.HasSoundLevel = raw.HasSoundLevel
        shadow.HasCs2hCounter = raw.HasCs2hCounter
        shadow.Cs2fByte = raw.Cs2fByte
        shadow.HasCs2eMarker = raw.HasCs2eMarker
        shadow.InheritsSoundsFromFormID = raw.InheritsSoundsFromFormID
        shadow.HasInheritsSoundsFrom = raw.HasInheritsSoundsFrom
        shadow.PowerArmorStandFormID = raw.PowerArmorStandFormID
        shadow.HasPowerArmorStand = raw.HasPowerArmorStand
        shadow.DefaultPackageListFormID = raw.DefaultPackageListFormID
        shadow.HasDefaultPackageList = raw.HasDefaultPackageList
        shadow.CrimeFactionFormID = raw.CrimeFactionFormID
        shadow.HasCrimeFaction = raw.HasCrimeFaction
        ' QNAM (TextureLightingFloats) is NOT round-trip-only — NpcRecordOverlay.ApplyPresetOverlayToNpcData
        ' already populates it: derived from slot-12 SkinTone tint when present, raw otherwise. Copying
        ' raw here would clobber that derivation and persist the original QNAM instead of the user's
        ' Edit Face skin-tint change. Removed 2026-05-16.
        shadow.HasFmin = raw.HasFmin
        shadow.ActivateTextOverride = raw.ActivateTextOverride
        shadow.HasActivateTextOverride = raw.HasActivateTextOverride
        shadow.MwgtRaw = raw.MwgtRaw
        shadow.HasMwgt = raw.HasMwgt
        shadow.HasFull = raw.HasFull
        shadow.HasTemplate = raw.HasTemplate
        ' HasDefaultOutfit is owned by ApplyPresetOverlayToNpcData (it derives the DOFT-emission gate
        ' from the outfit override). Copying it from raw here would clobber an override that added an
        ' outfit to an NPC whose raw record had none. SleepOutfit is not overridden → still copied.
        shadow.HasSleepOutfit = raw.HasSleepOutfit
        shadow.HasHairColor = raw.HasHairColor
        shadow.HasFacialHairColor = raw.HasFacialHairColor
        shadow.HasHeadTexture = raw.HasHeadTexture
        ' Collection-typed fields the overlay never touches — safe to share by reference.
        ' Writer enumerates only; if it ever starts mutating, switch to deep-copy.
        shadow.Factions = raw.Factions
        shadow.ActorEffectFormIDs = raw.ActorEffectFormIDs
        shadow.Destruction = raw.Destruction
        shadow.Attacks = raw.Attacks
        shadow.Perks = raw.Perks
        shadow.Properties = raw.Properties
        shadow.Inventory = raw.Inventory
        shadow.AiPackageFormIDs = raw.AiPackageFormIDs
        shadow.KeywordFormIDs = raw.KeywordFormIDs
        shadow.AttachParentSlotFormIDs = raw.AttachParentSlotFormIDs
        shadow.ObjectTemplateCombinations = raw.ObjectTemplateCombinations
        shadow.ActorSounds = raw.ActorSounds

        ' Parallel/derived collections (TintLayerStructs, FaceMorphTrailingBytes,
        ' MorphKeysOrdered) are NOT copied here. They're rebuilt by
        ' SyncParallelCollectionsAfterOverlay from the renderer-side lists, which is the
        ' single source of truth after the overlay runs. Copying them from raw was unsafe:
        ' a count-match heuristic was wrong because the overlay can replace N tints with N
        ' DIFFERENT tints, count match but content differs → writer emits raw tints instead
        ' of the overlay's. Trust only the renderer-side list.
    End Sub

    ''' <summary>Rebuild the parallel collections (TintLayerStructs, FaceMorphTrailingBytes,
    ''' MorphKeysOrdered) from the renderer-side lists. Runs ALWAYS, not just on count
    ''' mismatch — the overlay can replace N items with N different items (count match,
    ''' content different) and a count-match heuristic would silently write the raw items.
    '''
    ''' Pad7 of TEND is preserved from FaceTintLayers(i).RawTendBytes when available; for
    ''' new entries created by the preset (no raw bytes) Pad7 = 0 which matches vanilla
    ''' authoring.</summary>
    Private Sub SyncParallelCollectionsAfterOverlay(shadow As NPC_Data)
        ' --- TintLayerStructs paralela a FaceTintLayers ---
        ' xEdit emits TEND with three valid lengths driven by aOptionalFromElement=1:
        '   1 byte  → Value only (TextureSet, Discriminator=2)
        '   7 bytes → Value + Color + TemplateColorIndex (Palette/Mask, Discriminator=1)
        ' We mirror that on rebuild: HasColor / HasTemplateColorIndex come from the source
        ' bytes when available (entries cloned from raw preserve them); for new entries
        ' created by the preset (RawTendBytes = Nothing) we infer from Discriminator
        ' following vanilla convention (Discriminator=1 → Color+TCI, Discriminator=2 → Value
        ' only). The 5-byte "Color but no TCI" case is theoretically possible but vanilla
        ' never emits it, so the preset path also doesn't.
        Dim newTints As New List(Of (Teti As NPC_TetiStruct, Tend As NPC_TendStruct))
        For Each tl In shadow.FaceTintLayers
            Dim teti As New NPC_TetiStruct With {
                .DataType = tl.Discriminator,
                .Index = tl.Index
            }
            Dim tend As New NPC_TendStruct With {
                .RawValue = CByte(Math.Max(0, Math.Min(255, tl.Value))),
                .ColorR = tl.Color.R,
                .ColorG = tl.Color.G,
                .ColorB = tl.Color.B,
                .ColorPad = 0,
                .TemplateColorIndex = CShort(Math.Max(Short.MinValue, Math.Min(Short.MaxValue, tl.TemplateColorIndex)))
            }
            ' Decide HasColor / HasTemplateColorIndex from the source TEND length when
            ' available (preserves byte-equivalence for raw-cloned entries). Otherwise
            ' infer from Discriminator.
            If tl.RawTendBytes IsNot Nothing Then
                tend.HasColor = tl.RawTendBytes.Length >= 5
                tend.HasTemplateColorIndex = tl.RawTendBytes.Length >= 7
                If tl.RawTendBytes.Length >= 5 Then
                    tend.ColorPad = tl.RawTendBytes(4)
                End If
            Else
                ' Preset-created entry: vanilla convention.
                tend.HasColor = (tl.Discriminator = 1)
                tend.HasTemplateColorIndex = (tl.Discriminator = 1)
            End If
            newTints.Add((teti, tend))
        Next
        shadow.TintLayerStructs = newTints

        ' --- FaceMorphTrailingBytes paralela a FaceMorphs ---
        ' FMRS trailing "Unknown" wbByteArray (wbDefinitionsFO4.pas:10813). Three cases:
        '   • Parser captured raw with >28 bytes → preserve trailing portion verbatim.
        '   • Parser captured raw with exactly 28 bytes → no trailing (rare, mods that omitted).
        '   • Preset created a fresh entry (RawFmrsBytes is Nothing) → default 8 zero bytes,
        '     matching vanilla CK output. xEdit accepts variable size but vanilla always emits 8.
        Const VanillaFmrsTrailingSize As Integer = 8
        Dim newTrailing As New List(Of Byte())
        For Each fm In shadow.FaceMorphs
            If fm.RawFmrsBytes IsNot Nothing Then
                If fm.RawFmrsBytes.Length > 28 Then
                    Dim trail(fm.RawFmrsBytes.Length - 28 - 1) As Byte
                    Buffer.BlockCopy(fm.RawFmrsBytes, 28, trail, 0, trail.Length)
                    newTrailing.Add(trail)
                Else
                    newTrailing.Add(Array.Empty(Of Byte)())
                End If
            Else
                ' Preset-created entry without a matching raw — default to vanilla CK's 8 zeroes.
                Dim item2 = New Byte(VanillaFmrsTrailingSize - 1) {}
                newTrailing.Add(item2)
            End If
        Next
        shadow.FaceMorphTrailingBytes = newTrailing

        ' --- MorphKeysOrdered paralela a MorphValues ---
        Dim newKeys As New List(Of UInteger)
        For Each k In shadow.MorphValues.Keys
            newKeys.Add(k)
        Next
        shadow.MorphKeysOrdered = newKeys
    End Sub

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

    ''' <summary>Build a LooksmenuPreset that captures what's currently being rendered for the
    ''' selected NPC. Reads from the same effective NPC_Data the renderer consumes (template
    ''' source + applied overlay if any), and replicates the LooksMenu Save schema on the way out
    ''' so Paste can route through the exact same ApplyPresetOverlayToNpcData path that Load
    ''' Looksmenu uses — no parallel codepath, no schema drift between Save and Load.
    '''
    ''' Schema fidelity to CharGenInterface.cpp SavePreset:
    '''   - HeadParts: filters out IsExtraPart (flag 0x08), matching CharGenInterface.cpp:96.
    '''     This is safe because NPC_Manager's CollectHeadPartCandidate (line ~6326) recursively
    '''     expands each main HDPT's HNAM extras at render time — same as the engine does. So
    '''     the extras (lashes/AO/wet/hairlines) come back automatically when Paste applies the
    '''     main HDPTs from the preset. Verified empirically in npc_preview.log: TEOBAIO eye
    '''     HDPT with extras=3 loaded all 3 extras after preset apply.
    '''   - Tints: skips Value=0 entries (CharGenInterface.cpp:180-181 does the same).
    '''   - Morphs.Intensity: written even when 1.0 (we don't asymmetrically skip on Save the way
    '''     LooksMenu does, because preserving "explicit 1.0" matches what LoadPreset interprets).
    ''' </summary>
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
        For Each kv In effective.MorphValues
            preset.ChargenFaceMorphs(kv.Key) = kv.Value
        Next
        preset.BodyMorphValues.AddRange(effective.BodyMorphRegionValues)
        For Each fm In effective.FaceMorphs
            preset.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
        Next
        preset.FacialMorphIntensity = effective.FacialMorphIntensity

        ' BodySlide vertex sliders: F4SE-only, no record-level source. Pulled directly from the
        ' overlay preset for this NPC because ApplyPresetOverlayToNpcData doesn't touch them
        ' (NPC_Data has no BodyMorphs field). Without this copy Save LooksMenu would drop every
        ' BodySlide slider the user dialed in via the Edit Body form.
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(state.RootNpcFormID, overlay) AndAlso overlay IsNot Nothing Then
            For Each kv In overlay.BodyMorphSliders
                preset.BodyMorphSliders(kv.Key) = kv.Value
            Next
            ' LM SkinTemplate id is overlay-only (no record source). Carry through so Copy Look
            ' captures it and Save Looksmenu emits it.
            preset.SkinTemplateId = If(overlay.SkinTemplateId, "")
        End If

        ' NPC.WNAM skin override: capturamos la skin EFECTIVA que se está renderizando ahora —
        ' que ya considera overlay.SkinFormIDOverride si existe, raw NPC.WNAM como fallback, y
        ' RACE.WNAM si ambos son 0 (ver ApplyRaceFallbacks / RecomputeEffectiveSkinFormID).
        ' Capturar state.SkinFormID directamente vs overlay-only garantiza que Copy → Paste
        ' transfiera el skin AUNQUE el NPC source no tenga overlay explícito (caso típico:
        ' vanilla NPC con WNAM autoreado). SerializePreset (Save Looksmenu) NO emite este campo
        ' al JSON — es overlay/clipboard only, no afecta el round-trip JSON ↔ LM in-game.
        preset.SkinFormIDOverride = state.SkinFormID

        ' NPC.DOFT default outfit: capturamos el outfit EFECTIVO (post-override) igual que skin.
        ' Se arrastra Copy → Paste (gated por options.Outfit) y SerializePreset lo emite como
        ' _npcm_DefaultOutfit. state.DefaultOutfitFormID ya considera el override aplicado en
        ' ResolveNPCBaseState.
        preset.DefaultOutfitFormIDOverride = state.DefaultOutfitFormID

        ' NPC.ACBS bit 0x04 "Is CharGen Face Preset": misma semántica que skin. Capturamos el
        ' valor EFECTIVO — overlay si existe, sino raw NPC.ACBS bit. BuildFilteredPaste lo
        ' consume cuando options.IsCharGenPreset=True (línea ~12184). Sin esto Copy→Paste perdía
        ' la flag aunque el dialog tuviera el checkbox activo.
        Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
        If overlay IsNot Nothing AndAlso overlay.IsCharGenFacePreset.HasValue Then
            preset.IsCharGenFacePreset = overlay.IsCharGenFacePreset.Value
        Else
            preset.IsCharGenFacePreset = ((raw.AcbsFlags And AcbsBitIsCharGenFacePreset) <> 0UI)
        End If

        ' WYSIWYG: with the SkinTemplateId carried over, materialize the template's HDPT bundle
        ' into preset.HeadPartFormIDs so a paste at the destination NPC still emits the correct
        ' PNAM at Save ESP time. Without this, the clipboard would carry SkinTemplateId but its
        ' headRear swap would only ever exist in the runtime shadow, dropping out of any ESP
        ' the destination NPC writes after a paste.
        NpcRecordOverlay.MaterializeLmTemplateBundleToPreset(preset, state.IsFemale, AddressOf ResolveLmSkinTemplate)

        ' Tints — skip Value=0 entries (CharGenInterface.cpp:180-181). Order matters: it determines
        ' the layer composition order at render time (the ESP TETI/TEND order is the natural NPC
        ' record order, but the engine in-game reorders tints to match the RACE's TintTemplateGroups
        ' Options order — that's what gives non-conmutative blends like SoftLight a stable result
        ' across LM Save / Load. Without this reorder the TintOrder array would be ESP-record-order
        ' instead of RACE-Group-order; LM in-game writes RACE-Group-order, so to round-trip we need
        ' to match.
        '
        ' Also resolve each layer's positional TemplateColorIndex (vanilla NPC TEND stores POSITION
        ' in the RACE's TTEC array) into the absolute TemplateIndex of that color (what LooksMenu
        ' canonically emits as ColorID). Without this conversion ColorID round-trips as 0 because
        ' that's the position vanilla typically uses; LooksMenu in-game reports e.g. 1157/1824/1339.
        Dim raceRec = If(state.RaceFormID <> 0UI, _pluginManager.GetRecord(state.RaceFormID), Nothing)
        Dim race As RACE_Data = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            race = _ctx.ParseRaceCached(raceRec)
        End If

        ' Build a TETI.Index → RACE-order rank dict by walking the gender-appropriate TintGroups
        ' Options in order of appearance. Layers whose Index isn't found in the RACE (custom mods?)
        ' get rank Integer.MaxValue → appended at the end.
        Dim raceTintRank As New Dictionary(Of UShort, Integer)
        If race IsNot Nothing Then
            Dim tintGroups = If(state.IsFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
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

        Dim layersWithRank = effective.FaceTintLayers.
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

        ' BuildPresetFromState produces a complete snapshot of the rendered NPC. By definition
        ' every overlay-replaceable field is "present" in this snapshot, so all Has* flags are
        ' True — the resulting preset, when applied as overlay, fully replaces those fields on
        ' any NPC (including wiping when one of the lists ended up empty after edits).
        preset.HasFaceTintLayers = True
        preset.HasChargenFaceMorphs = True
        preset.HasBodyMorphValues = True
        preset.HasFaceBoneRegions = True
        preset.HasHeadPartFormIDs = True
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

    ''' <summary>Resolve layer.TemplateColorIndex (the TEND ColorID) purely from the layer's
    ''' colour. Delegates to FaceTintInputBuilder.ResolveTemplateColorIndex (single source of
    ''' truth shared with the editor): a TTEC preset whose CLFM RGB matches tl.Color wins; among
    ''' presets sharing that colour, the one whose Alpha is closest to the layer's opacity
    ''' (Value/100); no colour match → -1 (custom RGB outside the palette — the TEND RGB is used
    ''' directly, no CLFM link).
    '''
    ''' Per-user rule: the index tracks ONLY the colour; opacity is just the tiebreak among
    ''' equal-colour presets, never the -1-vs-index decision. This replaced an earlier fallback
    ''' that wrote pos=0's TemplateIndex for unmatched colours.
    '''
    ''' LooksMenu's SavePreset emits the same shape: ColorID = absolute TemplateIndex of the TTEC
    ''' entry whose CLFM RGB matches the TEND RGB (verified vs PiperESPM.json: layer 528 TEND RGB
    ''' (88,1,55) → TTEC pos=12 TemplateIndex=1333). LM's LoadPreset reads it back via
    ''' GetColorDataByID(colorID) (CharGenInterface.cpp:511); an out-of-palette ID (e.g. -1) is
    ''' coerced by LM to colors[0].colorID (CharGenInterface.cpp:514-517). The vanilla engine's
    ''' handling of -1 at FaceGen bake is NOT verified against the binary.</summary>
    Private Sub ResolveTemplateColorIdToAbsolute(layer As NPC_FaceTintLayerData, race As RACE_Data, isFemale As Boolean)
        If race Is Nothing OrElse layer Is Nothing OrElse layer.Discriminator <> 1US Then Return
        Dim opt = race.FindTintOption(layer.Index, isFemale)
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
    Private Sub NormalizePresetTintTemplateColorIds(preset As LooksmenuLoader.LooksmenuPreset, race As RACE_Data, isFemale As Boolean)
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

    ''' <summary>What the body editor can offer for this NPC, given the RACE record and the
    ''' loaded body shapes. Each section is gated independently: a race like Ghoul or
    ''' PowerArmorRace may declare no BSMS WeightScale / RangeModifier on any bone, in which
    ''' case the corresponding section has no engine effect and is hidden.
    '''
    ''' • HasMwgt — at least one BSMS WeightScale entry on a gender-matched bone (HasWeightScale).
    ''' • HasMrsv — at least one BSMS RangeModifier entry on a gender-matched bone
    '''   (HasRangeModifier). Per wbDefinitionsFO4.pas:5929 RangeModifier is only Y/Z; absence
    '''   means MRSV does nothing for that race.
    ''' • BodySlideSliders — the union of PIRT .tri morph names across all body shapes, after
    '''   excluding the WeightThin/Muscular/Fat reserved names. Empty = no body .tri loaded.</summary>
    Private Structure BodyEditAvailability
        Public HasMwgt As Boolean
        Public HasMrsv As Boolean
        Public BodySlideSliders As List(Of String)
        Public ReadOnly Property AnythingAvailable As Boolean
            Get
                Return HasMwgt OrElse HasMrsv OrElse (BodySlideSliders IsNot Nothing AndAlso BodySlideSliders.Count > 0)
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
            Dim race = _ctx.ParseRaceCached(raceRec)
            Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
            Dim chosen As RACE_BoneDataGender = race.BoneData.FirstOrDefault(Function(bd) bd.Gender = targetGender)
            If chosen Is Nothing OrElse chosen.Bones.Count = 0 Then
                chosen = race.BoneData.FirstOrDefault(Function(bd) bd.Bones.Count > 0)
            End If
            If chosen IsNot Nothing Then
                For Each bone In chosen.Bones
                    If bone.HasWeightScale Then avail.HasMwgt = True
                    If bone.HasRangeModifier Then avail.HasMrsv = True
                    If avail.HasMwgt AndAlso avail.HasMrsv Then Exit For
                Next
            End If
        End If

        If renderData IsNot Nothing AndAlso renderData.Shapes IsNot Nothing Then
            avail.BodySlideSliders = BodySlideTriResolver.EnumerateSliderNames(
                renderData.Shapes, renderData.MeshDictKeys)
        End If

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
            ' Should never happen if ButtonEditBody.Enabled gating is correct, but guard anyway.
            MessageBox.Show("This race has no MWGT/MRSV/BodySlide channels available.",
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
        If effectiveNpc IsNot Nothing AndAlso effectiveNpc.BodyMorphRegionValues IsNot Nothing Then
            For i = 0 To 4
                If i < effectiveNpc.BodyMorphRegionValues.Count Then
                    initial.Mrsv(i) = effectiveNpc.BodyMorphRegionValues(i)
                End If
            Next
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

    ''' <summary>Open the Edit Outfit picker (NPC.DOFT override) for the current NPC. The picker renders
    ''' its WYSIWYG preview through the SAME pipeline as this viewer (<see cref="PreviewOutfitInHostAsync"/>)
    ''' but into ITS OWN host with a host-scoped override — it never touches the shared overlay
    ''' (_appliedPresets) nor the main preview, so cancel is inherently non-destructive (nothing to undo).
    ''' On OK the chosen value is committed to the overlay and the MAIN preview reloads. Picker return:
    '''   Nothing → "(record default)" (clear override, preserve raw NPC.DOFT)
    '''   Some(0) → "(no outfit)"
    '''   Some(fid) → OTFT / draft override.</summary>
    Private Async Sub ButtonEditOutfit_Click(sender As Object, e As EventArgs) Handles ButtonEditOutfit.Click
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim st = _renderHost.LastRenderedState
        Dim npcFormID = st.RootNpcFormID
        Dim npc As NPC_Data = Nothing
        If Not _ctx.NpcCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then Return

        ' Raw record DOFT drives the "(record default)" pinned entry → Nothing semantic.
        Dim modelFormID = If(st.ModelSourceFormID <> 0UI, st.ModelSourceFormID, npcFormID)
        Dim rawNpc = _ctx.GetParsedNpc(modelFormID)
        Dim rawOutfit As UInteger = If(rawNpc IsNot Nothing, rawNpc.DefaultOutfitFormID, 0UI)

        Dim raceRec = If(st.RaceFormID <> 0UI, _pluginManager.GetRecord(st.RaceFormID), Nothing)
        Dim raceEditorID = If(raceRec IsNot Nothing, raceRec.EditorID, "?")

        Using dlg As New OutfitPicker_Form(Me, npcFormID, _appliedPresets, st.RaceFormID, raceEditorID, st.IsFemale, st.DefaultOutfitFormID, rawOutfit)
            ' Cancel: the picker rendered only into its own host; the main preview + overlay were never
            ' touched, so there is nothing to undo.
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return

            ' OK: commit the chosen value to the overlay and reload the MAIN preview.
            Dim result As UInteger? = dlg.SelectedOutfitOverride
            Dim previousOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
            Dim hadOverlay = _appliedPresets.TryGetValue(npcFormID, previousOverlay) AndAlso previousOverlay IsNot Nothing
            Dim p As LooksmenuLoader.LooksmenuPreset
            If hadOverlay Then
                p = previousOverlay
            Else
                p = New LooksmenuLoader.LooksmenuPreset()
                _appliedPresets(npcFormID) = p
            End If
            Dim priorOutfitOverride = p.DefaultOutfitFormIDOverride
            p.DefaultOutfitFormIDOverride = result

            Try
                Dim requestVersion = Interlocked.Increment(_previewRequestVersion)
                Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion)
                MarkNpcDirty(npcFormID)
            Catch ex As Exception
                ' Revert just the outfit field; don't clobber other overlay edits.
                p.DefaultOutfitFormIDOverride = priorOutfitOverride
                If Not hadOverlay Then _appliedPresets.Remove(npcFormID)
                MessageBox.Show($"Failed to render outfit: {ex.Message}{vbCrLf}Outfit reverted.",
                                "Edit Outfit", MessageBoxButtons.OK, MessageBoxIcon.Error)
            End Try
        End Using
    End Sub

    ' =====================================================================
    ' Edit Face — toolbar enable + dialog launch
    ' =====================================================================

    ''' <summary>What the face editor can offer for this NPC, given the RACE record + its
    ''' optional FacialBoneRegions JSON + the chargen TRI loaded for the face shape. Each
    ''' section gates independently; the button enables iff at least ONE section has content.
    ''' <para>
    ''' • HasHeadParts   — race declares chargen head parts for the active gender (RACE
    '''                    Female/MaleHeadPartFormIDs).<br/>
    ''' • HasHairColors  — race declares hair color CLFMs for the active gender (Female/MaleHairColorFormIDs).<br/>
    ''' • HasMorphPresets— at least one MorphGroup has at least one preset whose MorphName is
    '''                    present in the loaded chargen TRI (mirrors the "no presets → no
    '''                    sliders" rule used by EditFace_Form.BuildMorphGroupSections).<br/>
    ''' • HasFaceTints   — race declares tint template groups for the active gender
    '''                    (Female/MaleTintTemplateGroups).<br/>
    ''' • HasFaceBoneRegions — the FacialBoneRegions JSON exists for race+gender (the FMRS region
    '''                    sliders depend on it).
    ''' </para></summary>
    Private Structure FaceEditAvailability
        Public HasHeadParts As Boolean
        Public HasHairColors As Boolean
        Public HasMorphPresets As Boolean
        Public HasFaceTints As Boolean
        Public HasFaceBoneRegions As Boolean
        Public ReadOnly Property AnythingAvailable As Boolean
            Get
                Return HasHeadParts OrElse HasHairColors OrElse HasMorphPresets OrElse HasFaceTints OrElse HasFaceBoneRegions
            End Get
        End Property
    End Structure

    ''' <summary>Compute face-edit availability against the currently rendered NPC. The TRI
    ''' morph-name set comes from <see cref="NpcRenderHost.LastFaceTriMorphNames"/>, which is
    ''' populated post-render in <see cref="RenderCurrentStateAsync"/> right before this method
    ''' is invoked. The race record is parsed once and queried for each gendered list.</summary>
    Private Function ComputeFaceEditAvailability(state As NPCVisualState, host As NpcRenderHost) As FaceEditAvailability
        Dim avail As New FaceEditAvailability
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return avail
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return avail
        Dim race = _ctx.ParseRaceCached(raceRec)
        If race Is Nothing Then Return avail

        Dim headParts = If(state.IsFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)
        avail.HasHeadParts = (headParts IsNot Nothing AndAlso headParts.Count > 0)

        Dim hairColors = If(state.IsFemale, race.FemaleHairColorFormIDs, race.MaleHairColorFormIDs)
        avail.HasHairColors = (hairColors IsNot Nothing AndAlso hairColors.Count > 0)

        Dim tintGroups = If(state.IsFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        avail.HasFaceTints = (tintGroups IsNot Nothing AndAlso tintGroups.Count > 0)

        avail.HasFaceBoneRegions = (NpcMorphPoseResolver.GetFacialBoneRegionsForRace(race, state.IsFemale) IsNot Nothing)

        ' Morph presets — same filter as EditFace_Form.BuildMorphGroupSections: a preset counts
        ' only if its MorphName is present in the loaded chargen TRI. Empty TRI set means we
        ' don't have the morph names yet; bail conservatively (HasMorphPresets stays False).
        Dim triNames As HashSet(Of String) = If(host?.LastFaceTriMorphNames, Nothing)
        Dim morphGroups = If(state.IsFemale, race.FemaleMorphGroups, race.MaleMorphGroups)
        If morphGroups IsNot Nothing AndAlso triNames IsNot Nothing AndAlso triNames.Count > 0 Then
            For Each g In morphGroups
                If g.Presets Is Nothing Then Continue For
                For Each p In g.Presets
                    If Not String.IsNullOrEmpty(p.MorphName) AndAlso triNames.Contains(p.MorphName) Then
                        avail.HasMorphPresets = True
                        Exit For
                    End If
                Next
                If avail.HasMorphPresets Then Exit For
            Next
        End If

        Return avail
    End Function

    ''' <summary>Gate ButtonEditFace by <see cref="ComputeFaceEditAvailability"/>: the editor
    ''' opens only when at least one section has authored content for this race+gender. If the
    ''' race has no head parts, no hair colors, no morph presets backed by the loaded TRI, no
    ''' tint groups, and no FacialBoneRegions JSON, the editor would be entirely empty — same
    ''' rule Edit Body uses to skip a useless picker.</summary>
    Private Sub UpdateEditFaceEnabled()
        Dim shouldEnable As Boolean = False
        If _renderHost.LastRenderedState IsNot Nothing AndAlso _renderHost.LastRenderData IsNot Nothing Then
            Dim avail = ComputeFaceEditAvailability(_renderHost.LastRenderedState, _renderHost)
            ' Canonical race-level gate: even if the race exposes some authored content (hair colors,
            ' tint groups, …), Edit Face is only meaningful for a FaceGen race (one with a head/face).
            ' RaceSupportsFaceGen reads RACE.DATA bit 0x2 — the 0-exception discriminator — so non-FaceGen
            ' races (dog/creature/robot/turret/feral-ghoul/etc.) keep the button disabled.
            shouldEnable = avail.AnythingAvailable AndAlso
                           RaceUtil.RaceSupportsFaceGen(_renderHost.LastRenderedState.RaceFormID, _pluginManager)
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
        If _renderHost.LastRenderedState Is Nothing Then Return
        Dim raceFormID = _renderHost.LastRenderedState.RaceFormID
        If raceFormID = 0UI Then
            MessageBox.Show(Me, "This NPC has no resolved RACE — Edit Face needs the RACE record to populate" &
                            " palette / morph / region pickers.", "Edit Face", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim raceRec = _pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return
        Dim race = _ctx.ParseRaceCached(raceRec)
        If race Is Nothing Then Return

        ' Capture the raw NPC's AcbsFlags so the Edit Face form can compute the original bit and
        ' the form's Cancel rollback can restore it (the overlay only stores Boolean? — the raw
        ' value lives on the NPC record).
        Dim modelNpcFormID = If(_renderHost.LastRenderedState.ModelSourceFormID <> 0UI, _renderHost.LastRenderedState.ModelSourceFormID, _renderHost.LastRenderedState.FormID)
        Dim rawNpc = _ctx.GetParsedNpc(modelNpcFormID)
        Dim rawAcbsFlags As UInteger = If(rawNpc IsNot Nothing, rawNpc.AcbsFlags, 0UI)

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

    ''' <summary>Build CharGen — bakes the FaceGen NIF for the currently rendered NPC by
    ''' starting from the vanilla NIF in the BA2/loose pool and pruning shapes that do not
    ''' correspond to a HeadPart the NPC currently references (HeadPartFormIDs ∪ recursive
    ''' ExtraPartFormIDs). Output is written as .nif2 (not .nif) under
    ''' &lt;exe dir&gt;\BakedFaceGen\Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;plugin&gt;\&lt;FormID8hex&gt;.nif2
    ''' so the engine never sees it; the file is meant for side-by-side diff with the BA2
    ''' original. Each run also dumps the kept/dropped decision per shape to npc_preview.log.</summary>
    ''' <summary>Abre el diálogo CharGen Options (tamaño de textura por canal + formato del diffuse,
    ''' persistido en Config_App). El bake lee esos settings via FaceGenBuilder.OutputSettings.</summary>
    Private Sub ButtonCharGenOptions_Click(sender As Object, e As EventArgs) Handles ButtonCharGenOptions.Click
        Using f As New CharGenOptionsForm()
            If f.ShowDialog(Me) = DialogResult.OK Then
                ' Las convenciones FaceTint (tab "FaceTint Conventions") afectan el composite del render
                ' EN VIVO, no sólo el bake. Re-render el NPC actual para reflejarlo (decisión usuario
                ' 2026-06-04, opción A). RenderFromCurrentSelection re-corre la pipeline facetint completa.
                RenderFromCurrentSelection()
            End If
        End Using
    End Sub

    Private Async Sub ButtonBuildCharGen_Click(sender As Object, e As EventArgs) Handles ButtonBuildCharGen.Click
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
                    FaceTintCpuCompositor.BeginBatchDecodeCache()
                    Try
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
        MessageBox.Show(Me, summary, "Build CharGen", MessageBoxButtons.OK,
                        If(failed.Count = 0, MessageBoxIcon.Information, MessageBoxIcon.Warning))
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
        ' "select OK without thinking" path matches the legacy "paste everything" behavior.
        Dim options As PasteOptions
        Using dlg As New PasteOptionsDialog()
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            options = dlg.BuildOptions()
        End Using

        Dim filtered = BuildFilteredPaste(_clipboardPreset, npc, options)

        Dim previousOverlay As LooksmenuLoader.LooksmenuPreset = Nothing
        _appliedPresets.TryGetValue(npcFormID, previousOverlay)
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

    ''' <summary>Build a partial paste preset: take the SOURCE clipboard preset and, for every
    ''' category whose flag is False in <paramref name="options"/>, replace that category's
    ''' value(s) with the TARGET NPC's raw record value(s). The result is a preset that, when
    ''' applied via <see cref="ApplyPresetOverlayToNpcData"/>, leaves the unchecked categories
    ''' visually identical to "no overlay touched them" — because the overlay's value for those
    ''' fields IS the raw NPC's own value.
    ''' <para>Why copy raw instead of leaving empty: the overlay merge in
    ''' <see cref="ApplyPresetOverlayToNpcData"/> uses "Count > 0" as the gate for several
    ''' fields (FaceTintLayers, MorphValues, FaceBoneRegions, BodyMorphRegionValues), so an
    ''' empty list IS treated as "preserve raw" already. But for HeadParts the engine-faithful
    ''' behavior is wipe + race defaults + preset entries — empty preset HeadParts would still
    ''' wipe the target's NPC.PNAM. Copying the raw NPC's HeadPartFormIDs into the preset
    ''' guarantees engine-equivalent preservation regardless of which gate the overlay merge
    ''' uses for that field. Same principle for HairColor (0 vs raw value), Weight (Nothing vs
    ''' raw value), and IsCharGenPreset (Nothing vs raw ACBS bit).</para></summary>
    Private Function BuildFilteredPaste(source As LooksmenuLoader.LooksmenuPreset,
                                         targetRaw As NPC_Data,
                                         options As PasteOptions) As LooksmenuLoader.LooksmenuPreset
        Dim p As New LooksmenuLoader.LooksmenuPreset With {
            .SourcePath = source.SourcePath,
            .Gender = source.Gender
        }

        ' --- Body weight (3 floats) ---
        If options.BodyWeight Then
            p.WeightThin = source.WeightThin
            p.WeightMuscular = source.WeightMuscular
            p.WeightFat = source.WeightFat
        Else
            p.WeightThin = targetRaw.WeightThin
            p.WeightMuscular = targetRaw.WeightMuscular
            p.WeightFat = targetRaw.WeightFat
        End If

        ' --- Body regions (MRSV) ---
        ' Either branch leaves p.BodyMorphValues populated (from source or from targetRaw),
        ' and either way Paste authoritatively defines this field. Has*=True for both.
        If options.BodyRegions Then
            p.BodyMorphValues.AddRange(source.BodyMorphValues)
        Else
            p.BodyMorphValues.AddRange(targetRaw.BodyMorphRegionValues)
        End If
        p.HasBodyMorphValues = True

        ' --- Body sliders (BodySlide vertex morphs, F4SE-only — no record-level source) ---
        If options.BodySliders Then
            For Each kv In source.BodyMorphSliders
                p.BodyMorphSliders(kv.Key) = kv.Value
            Next
        End If
        ' If unchecked: leave p.BodyMorphSliders empty. The renderer's
        ' GetEffectiveBodyMorphSliders treats empty-or-missing as "vanilla NPC, no overlay
        ' sliders" — which is exactly what the target NPC was already showing pre-paste, since
        ' the target NPC's BodyMorphSliders only existed if a previous overlay was applied (and
        ' that overlay is being replaced wholesale by this paste). Engine-equivalent.

        ' --- Skin override (NPC.WNAM) ---
        If options.SkinOverride Then
            p.SkinFormIDOverride = source.SkinFormIDOverride
        Else
            ' Don't touch — overlay merge falls back to targetRaw.SkinFormID when
            ' SkinFormIDOverride is Nothing.
            p.SkinFormIDOverride = Nothing
        End If

        ' --- Default outfit (NPC.DOFT) ---
        If options.Outfit Then
            p.DefaultOutfitFormIDOverride = source.DefaultOutfitFormIDOverride
        Else
            ' Don't touch — overlay merge falls back to targetRaw.DefaultOutfitFormID (raw DOFT)
            ' when DefaultOutfitFormIDOverride is Nothing.
            p.DefaultOutfitFormIDOverride = Nothing
        End If

        ' --- LM skin template (F4SE SkinInterface, separate from NPC.WNAM record skin) ---
        If options.LmSkinTemplate Then
            p.SkinTemplateId = If(source.SkinTemplateId, "")
        Else
            p.SkinTemplateId = ""
        End If

        ' --- Face parts (HeadParts) ---
        ' The overlay merge does wipe + race-defaults + preset entries. To preserve the target
        ' NPC's HeadParts when the user unchecks this category, copy targetRaw.HeadPartFormIDs
        ' into the preset — the merge's "preset wins per type" rule then re-establishes the
        ' target's own selections over race defaults, exact same outcome as no overlay applied.
        If options.FaceParts Then
            p.HeadPartFormIDs.AddRange(source.HeadPartFormIDs)
        Else
            p.HeadPartFormIDs.AddRange(targetRaw.HeadPartFormIDs)
        End If
        p.HasHeadPartFormIDs = True

        ' --- Hair color (HCLF) ---
        If options.HairColor Then
            p.HairColorFormID = source.HairColorFormID
        Else
            ' Setting to 0 would also work (the merge falls back to targetRaw on 0), but
            ' setting it explicitly keeps Save LooksMenu round-trip stable.
            p.HairColorFormID = targetRaw.HairColorFormID
        End If

        ' --- Face tints (TETI/TEND list, includes scars/paint/skin tone palette) ---
        If options.FaceTints Then
            For Each tl In source.FaceTintLayers
                p.FaceTintLayers.Add(CloneFaceTint(tl))
            Next
        Else
            For Each tl In targetRaw.FaceTintLayers
                p.FaceTintLayers.Add(CloneFaceTint(tl))
            Next
        End If
        p.HasFaceTintLayers = True

        ' --- Face vertex morphs (chargen MSDV) ---
        If options.FaceVertexMorphs Then
            For Each kv In source.ChargenFaceMorphs
                p.ChargenFaceMorphs(kv.Key) = kv.Value
            Next
        Else
            For Each kv In targetRaw.MorphValues
                p.ChargenFaceMorphs(kv.Key) = kv.Value
            Next
        End If
        p.HasChargenFaceMorphs = True

        ' --- Face bone regions (FMRS) + morph intensity (FMIN) ---
        ' The two are paired: the engine always overwrites Intensity (1.0 default if missing
        ' in JSON), so to "preserve target" we have to copy targetRaw.FacialMorphIntensity.
        If options.FaceBoneRegions Then
            For Each kv In source.FaceBoneRegions
                p.FaceBoneRegions(kv.Key) = CType(kv.Value.Clone(), Single())
            Next
            p.FacialMorphIntensity = source.FacialMorphIntensity
        Else
            For Each fm In targetRaw.FaceMorphs
                p.FaceBoneRegions(fm.Index) = fm.Values.ToArray()
            Next
            p.FacialMorphIntensity = targetRaw.FacialMorphIntensity
        End If
        p.HasFaceBoneRegions = True

        ' --- IsCharGenFacePreset flag (ACBS bit 0x04) ---
        Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
        If options.IsCharGenPreset Then
            p.IsCharGenFacePreset = source.IsCharGenFacePreset
        Else
            ' Preserve target's existing ACBS bit. Read the raw record's ACBS and set the
            ' preset's flag explicitly so the overlay merge writes that exact bit back.
            p.IsCharGenFacePreset = ((targetRaw.AcbsFlags And AcbsBitIsCharGenFacePreset) <> 0UI)
        End If

        ' If the paste copied an LM SkinTemplate, populate the origin tracker so a later
        ' Retract (e.g. user opens EditBody on the target and switches the template) can
        ' identify exactly which HDPTs came from this template. Without this, the target's
        ' HeadPartFormIDs would carry the template's HDPTs but Retract would find an empty
        ' tracker and leave them stuck — a later combo change would then duplicate-by-PartType.
        If Not String.IsNullOrEmpty(p.SkinTemplateId) Then
            Dim tpl = ResolveLmSkinTemplate(p.SkinTemplateId)
            If tpl IsNot Nothing Then
                Dim genderIdx As Integer = If(p.Gender = 1, 1, 0)
                Dim head As UInteger = tpl.HeadHdptFormID(genderIdx)
                Dim rear As UInteger = tpl.HeadRearHdptFormID(genderIdx)
                If head <> 0UI AndAlso p.HeadPartFormIDs.Contains(head) Then p.LmTemplateInjectedHdptFormIDs.Add(head)
                If rear <> 0UI AndAlso p.HeadPartFormIDs.Contains(rear) Then p.LmTemplateInjectedHdptFormIDs.Add(rear)
                ' HasHeadPartFormIDsSetByTemplate stays False: Paste's Has*=True (line 9824)
                ' is asserted independently of the template (snapshot semantics). Retract should
                ' remove just the template's HDPTs, not flip Has* off.
            End If
        End If

        Return p
    End Function

    ''' <summary>Capture the current rendered state into a LooksMenu preset and save it to a JSON
    ''' file. Default location is Data\F4SE\Plugins\F4EE\Presets\ — the same folder
    ''' Load LooksMenu reads from. Default filename is the NPC's EditorID.</summary>
    Private Sub ButtonSaveLooksmenu_Click(sender As Object, e As EventArgs) Handles ButtonSaveLooksmenu.Click
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
                Dim json = LooksmenuLoader.SerializePreset(preset, _pluginManager)
                IO.File.WriteAllText(dlg.FileName, json, New System.Text.UTF8Encoding(False))
            Catch ex As Exception
                MessageBox.Show($"Failed to write preset: {ex.Message}", "Save LooksMenu",
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
    '''      (xEdit-style: drop unused masters, re-map FormIDs to new MAST list).
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
            .CopyRoundTripOnlyFieldsFromRaw = AddressOf CopyRoundTripOnlyFieldsFromRaw,
            .SyncParallelCollectionsAfterOverlay = AddressOf SyncParallelCollectionsAfterOverlay,
            .RunChargenBake = Function(npcFid As UInteger, anchor As String, srcPlugin As String,
                                        prog As IProgress(Of NpcOverrideSaver.SaveProgress)) _
                                   As Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String))
                                  Return RunChargenBake(npcFid, anchor, srcPlugin, prog)
                              End Function,
            .RunChargenPackBatch = Function(anchor As String,
                                             bundles As IReadOnlyList(Of NpcFaceGenPacker.BakedNpcBundle),
                                             prog As IProgress(Of NpcOverrideSaver.SaveProgress)) _
                                        As Task(Of (Summary As String, Success As Boolean))
                                       Return RunChargenPackBatch(anchor, bundles, prog)
                                   End Function,
            .OutfitDrafts = New List(Of OutfitDraft)(_outfitDrafts),
            .LeveledListDrafts = New List(Of LeveledListDraft)(_leveledListDrafts),
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
        If target Is Nothing OrElse execResult Is Nothing OrElse Not execResult.Success Then Return

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

        ' Step 6: re-read the just-saved records as the new baseline (mount the written plugin last
        ' in load order), strip the now-persisted ESP fields from each overlay (keeping non-ESP
        ' BodyMorphs/Skin), clear the dirty marks, regroup in the tree, and re-render the loaded NPC.
        Await ApplyPostSaveReadback(execResult.WrittenNpcFormIDs, target.TargetPath, execResult.DraftFormIdMap)

        Dim savedCount = execResult.WrittenNpcFormIDs.Count
        Dim what = If(savedCount = 1, $"{If(selectedInput.Npc?.EditorID, selectedFormID.ToString("X8"))}", $"{savedCount} NPCs")
        MessageBox.Show($"Saved {what} to {IO.Path.GetFileName(execResult.WriterResult.OutputPath)}.{execResult.ChargenSummary}{execResult.VerifierSummary}",
                        "Save ESP/ESM", MessageBoxButtons.OK, execResult.VerifierIcon)
    End Function

    ''' <summary>Build the per-NPC save input from a FormID: fetch + type-safe parse the raw record,
    ''' resolve its source plugin, and read the CharGen Face Preset flag. Returns Nothing when the
    ''' record is missing or not an NPC_. Note: after a prior Save this session mounted an auto-gen
    ''' override (MergeOverridePlugin), GetRecord returns that override — re-saving then uses the
    ''' saved state as its base, which is the intended "saved override is the new baseline" behaviour.</summary>
    Private Function TryBuildNpcSaveInput(npcFormID As UInteger) As NpcOverrideSaver.NpcSaveInput
        If npcFormID = 0UI Then Return Nothing
        Dim rawRecord = _pluginManager.GetRecord(npcFormID)
        If rawRecord Is Nothing OrElse rawRecord.Header.Signature <> "NPC_" Then Return Nothing
        Dim sourcePluginName = If(rawRecord.SourcePluginName, "")
        Dim rawNpcSpec = RecordParsers.ParseNPC(rawRecord, sourcePluginName, _pluginManager)
        If rawNpcSpec Is Nothing Then Return Nothing
        Dim npc As NPC_Data = Nothing
        _ctx.NpcCache.TryGetValue(npcFormID, npc)
        ' ACBS bit 0x04 = "Is CharGen Face Preset" (xEdit wbDefinitionsFO4).
        Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
        Return New NpcOverrideSaver.NpcSaveInput With {
            .NpcFormID = npcFormID,
            .Npc = If(npc, rawNpcSpec),
            .RawRecord = rawRecord,
            .RawNpcSpec = rawNpcSpec,
            .SourcePluginName = sourcePluginName,
            .IsCharGenFacePreset = (rawNpcSpec.AcbsFlags And AcbsBitIsCharGenFacePreset) <> 0UI
        }
    End Function

    ''' <summary>Strip the ESP-persisted fields from an NPC's overlay after a successful Save, keeping
    ''' only the F4SE-only fields that have no record equivalent (BodyMorphs sliders + Skin template).
    ''' The ESP fields are now in the saved override (re-read via MergeOverridePlugin), so dropping
    ''' them avoids a redundant overlay re-applying the same values, while the kept fields preserve
    ''' the user's BodyMorphs/Skin edits. Mirror of <see cref="HydrateAppliedPresetsFromSidecars"/>:
    ''' the residual overlay is structurally identical to a fresh sidecar hydration. If nothing
    ''' non-ESP remains, the overlay is removed entirely.</summary>
    Private Sub StripEspFieldsFromOverlay(npcFormID As UInteger)
        Dim overlay As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(npcFormID, overlay) OrElse overlay Is Nothing Then Return

        Dim residual As New LooksmenuLoader.LooksmenuPreset()
        Dim keptAnything = False
        If overlay.BodyMorphSliders IsNot Nothing AndAlso overlay.BodyMorphSliders.Count > 0 Then
            For Each kv In overlay.BodyMorphSliders
                residual.BodyMorphSliders(kv.Key) = kv.Value
            Next
            keptAnything = True
        End If
        If Not String.IsNullOrEmpty(overlay.SkinTemplateId) Then
            residual.SkinTemplateId = overlay.SkinTemplateId
            keptAnything = True
        End If

        If keptAnything Then
            _appliedPresets(npcFormID) = residual
        Else
            _appliedPresets.Remove(npcFormID)
        End If
    End Sub

    ''' <summary>Post-save re-read (Step 6). Mounts the just-written plugin as the top override so
    ''' GetRecord/GetParsedNpc return the saved state (the uncached GetParsedNpc means every later
    ''' render reflects it). For each written NPC: strip the ESP fields from its overlay, clear its
    ''' dirty mark (no longer bold), and move it to the saved plugin's tree group. Re-renders the
    ''' currently-loaded NPC when it was among those saved so the preview drops the overlay and shows
    ''' the clean saved record.</summary>
    Private Async Function ApplyPostSaveReadback(writtenFormIDs As List(Of UInteger), savedPluginPath As String,
                                                 draftFormIdMap As Dictionary(Of UInteger, UInteger)) As Task
        Dim mergeOk As Boolean = True
        Try
            _pluginManager.MergeOverridePlugin(savedPluginPath)
        Catch ex As Exception
            mergeOk = False
            Logger.LogLazy(Function() $"[SAVE-READBACK] MergeOverridePlugin failed for {savedPluginPath}: {ex.Message}")
        End Try

        Dim savedPluginName = IO.Path.GetFileName(savedPluginPath)

        ' Promote the just-written OTFT/LVLI drafts to real records BEFORE re-rendering: remap any overlay /
        ' remaining-draft reference that still points at a provisional FormID to the real record, drop the
        ' persisted drafts, and refresh the outfit universe so they reappear in the editor as real records.
        ' Only when the re-mount succeeded — otherwise the file-local→global resolution would be wrong and
        ' we keep the drafts so a retry still works.
        If mergeOk Then PromoteSavedDrafts(draftFormIdMap, savedPluginName)
        Dim reloadFid As UInteger = If(_renderHost?.LastRenderedState IsNot Nothing, _renderHost.LastRenderedState.RootNpcFormID, 0UI)
        Dim treeChanged = False

        For Each fid In writtenFormIDs
            StripEspFieldsFromOverlay(fid)
            ClearNpcDirty(fid)
            Dim cachedNpc As NPC_Data = Nothing
            If _ctx.NpcCache.TryGetValue(fid, cachedNpc) AndAlso cachedNpc IsNot Nothing Then
                If Not String.Equals(cachedNpc.PluginName, savedPluginName, StringComparison.OrdinalIgnoreCase) Then
                    cachedNpc.PluginName = savedPluginName
                    _npcSearchableCache(fid) = NpcDisplayHelpers.BuildNpcSearchableText(cachedNpc)
                    treeChanged = True
                End If
            End If
        Next

        ' When the tree grouping changed, rebuild it and re-select the loaded NPC — the AfterSelect
        ' handler re-renders it from the clean record. Otherwise re-render explicitly if the loaded
        ' NPC was saved (its overlay was just stripped and needs to drop off the preview).
        If treeChanged Then
            PopulateNPCTree(_pendingTreeFilter)
            If reloadFid <> 0UI Then
                Dim moved = TreeViewNPCs.Nodes.Find($"NPC_{reloadFid:X8}", searchAllChildren:=True)
                If moved IsNot Nothing AndAlso moved.Length > 0 Then
                    moved(0).EnsureVisible()
                    TreeViewNPCs.SelectedNode = moved(0)  ' fires AfterSelect → reload (clean state)
                    Return
                End If
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

        ' (1) Overlays: an outfit override still aimed at a promoted draft → its real record. (For the NPCs
        ' just saved this is redundant — their overlay DOFT is stripped right after — but it's what keeps a
        ' DIFFERENT NPC that shares the same draft pointing at the real record instead of a dead provisional.)
        For Each ov In _appliedPresets.Values
            If ov Is Nothing OrElse Not ov.DefaultOutfitFormIDOverride.HasValue Then Continue For
            Dim mapped As UInteger
            If realGlobal.TryGetValue(ov.DefaultOutfitFormIDOverride.Value, mapped) Then
                ov.DefaultOutfitFormIDOverride = mapped
            End If
        Next

        ' (2) Surviving drafts that reference a promoted one (a clean unsaved outfit pointing at a saved
        ' LVLI, or a non-emitted LVLI nesting a saved one): rewrite the provisional ref to the real FormID.
        For Each d In _outfitDrafts
            If d Is Nothing Then Continue For
            For i = 0 To d.ItemFormIDs.Count - 1
                Dim mapped As UInteger
                If realGlobal.TryGetValue(d.ItemFormIDs(i), mapped) Then d.ItemFormIDs(i) = mapped
            Next
        Next
        For Each d In _leveledListDrafts
            If d Is Nothing Then Continue For
            For Each e In d.Entries
                Dim mapped As UInteger
                If realGlobal.TryGetValue(e.RefFormID, mapped) Then e.RefFormID = mapped
            Next
        Next

        ' (3) Drop the promoted drafts. The throwaway preview sentinel is never in the map, so it survives.
        _outfitDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))
        _leveledListDrafts.RemoveAll(Function(d) d IsNot Nothing AndAlso realGlobal.ContainsKey(d.FormID))

        ' (4) Refresh the outfit universe so the newly-real OTFT/LVLI surface in Browse + the item lists.
        BuildOutfitUniverse()
    End Sub

    ''' <summary>Dump the current rendered scene to a multi-shape NIF with each visible shape
    ''' transformed into world-pose vertices. Filter is shape.RenderHide = False — the same flag
    ''' NpcRenderHost.ApplyRenderToggleVisibility sets from the render toggles (Body / Underarmor
    ''' / Armor / Headwear / Gore), shape category, ShapeCoveredByOutfit, and ShapeOccludedByHeadwear.
    ''' Whatever is visible in the preview is what gets exported.
    '''
    ''' Positioning uses geom.PerVertexSkinMatrix (per-vertex shape-local → world transform)
    ''' directly — same matrix the renderer uses on the CPU skinning path and the GPU SSBO bone
    ''' palette (Σw·matsPose for multi-bone skinned; bindT∘localT for single-bone skinned;
    ''' shape.T/R/S × parent_chain for unskinned). v_world = v_local × PerVertexSkinMatrix(i).
    ''' Equivalent for normals/tangents/bitangents via the normal matrix (transpose of inverse).
    '''
    ''' This bypasses SkinningHelper.BakeFromMemoryUsingOriginal, whose v_baked = v × MposeBlend ×
    ''' inv(MbindBlend) produces "rebind" coordinates (verts that yield world-pose when re-skinned
    ''' through MbindBlend) — useful for WM's build-shape flow that re-emits the shape with
    ''' skinning intact, wrong for our strip-skin export which needs absolute world coords.
    '''
    ''' Per shape: clone into destNif via CloneShape_Original (preserves shader + UVs + triangles +
    ''' skinning palette), inject world-pose verts/normals/tangents/bitangents through the clone's
    ''' IShapeGeometry adapter, reset clone's local T/R/S to identity (CloneShape_Original's
    ''' unskinned-path parent-baking would otherwise double-transform our already-absolute verts),
    ''' strip skin (IsSkinned=False + SkinInstanceRef.Clear()) so viewers don't re-apply the bone
    ''' palette.
    '''
    ''' Material handling: clone shader verbatim — BGSM/DDS paths in the destination point at
    ''' the same files as the source. Self-contained material inlining (FaceGenBuilder-style)
    ''' is out of scope for this MVP.</summary>
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

        Dim destNif As New Nifcontent_Class_Manolo()
        destNif.Create(NiVersion.GetFO4(), withRootNode:=True)

        Dim shapesWritten As Integer = 0
        Dim shapesFailed As Integer = 0
        Dim failureDetails As New System.Text.StringBuilder()
        Dim destIdx As Integer = 0

        For Each mesh In _previewControl.Model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim srcRenderable = mesh.MeshData.Shape
            If srcRenderable.RenderHide Then Continue For
            Dim srcINiShape = srcRenderable.NifShape
            Dim srcNif = srcRenderable.NifContent
            If srcINiShape Is Nothing OrElse srcNif Is Nothing Then Continue For

            Dim shapeName = If(srcINiShape.Name?.String, $"Shape_{destIdx}")
            Try
                Dim liveGeom = mesh.MeshData.Meshgeometry
                Dim localVerts = liveGeom.Vertices  ' post-morph, pre-skin (shape-local).
                Dim perVtxMat = liveGeom.PerVertexSkinMatrix
                If localVerts Is Nothing OrElse perVtxMat Is Nothing OrElse localVerts.Length <> perVtxMat.Length Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"{shapeName}: missing skin matrix / vertex data")
                    Continue For
                End If
                Dim n = localVerts.Length

                ' ── Hair zap compaction map ──
                ' If this shape had any hair partition zapped this render (ApplyZaps=True + VertexMask(i)=-1
                ' on the zapped verts — exactly the renderer's skip predicate at Render.vb:1118), DROP those
                ' verts from the export so the saved NIF carries the compacted mesh. AGNOSTIC to which
                ' partition was zapped (Top / Long / Both) — it keys purely on VertexMask(i)=-1, so it works
                ' unchanged for the main (zap Top) and the hairline (zap Long). oldToNew(i) maps a surviving
                ' source vertex to its packed destination index; -1 = removed. When the shape is not zapped,
                ' oldToNew is identity and nSurv == n (no behaviour change for normal shapes).
                Dim vm = liveGeom.VertexMask
                Dim hasZap As Boolean = srcRenderable.ApplyZaps AndAlso vm IsNot Nothing AndAlso vm.Length = n
                Dim oldToNew(n - 1) As Integer
                Dim nSurv As Integer = 0
                For i = 0 To n - 1
                    If hasZap AndAlso vm(i) = -1.0F Then
                        oldToNew(i) = -1
                    Else
                        oldToNew(i) = nSurv
                        nSurv += 1
                    End If
                Next
                Dim zappedCount As Integer = n - nSurv

                ' Compute world-pose attributes per SURVIVING vertex (packed in oldToNew order). Position
                ' via TransformPosition; normals/tangents/bitangents via per-vertex normal matrix
                ' (transpose of inverse of upper-left 3x3 of the skin matrix). Same formula the renderer uses.
                Dim worldPos As New List(Of System.Numerics.Vector3)(nSurv)
                Dim hasN = liveGeom.Normals IsNot Nothing AndAlso liveGeom.Normals.Length = n
                Dim hasT = liveGeom.Tangents IsNot Nothing AndAlso liveGeom.Tangents.Length = n
                Dim hasB = liveGeom.Bitangents IsNot Nothing AndAlso liveGeom.Bitangents.Length = n
                Dim worldN As List(Of System.Numerics.Vector3) = If(hasN, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldT As List(Of System.Numerics.Vector3) = If(hasT, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldB As List(Of System.Numerics.Vector3) = If(hasB, New List(Of System.Numerics.Vector3)(nSurv), Nothing)

                For i = 0 To n - 1
                    If oldToNew(i) < 0 Then Continue For  ' zapped crown vertex — drop from export
                    Dim m4 = perVtxMat(i)
                    Dim wv = Vector3d.TransformPosition(localVerts(i), m4)
                    worldPos.Add(New System.Numerics.Vector3(CSng(wv.X), CSng(wv.Y), CSng(wv.Z)))

                    If hasN OrElse hasT OrElse hasB Then
                        Dim m3 As New Matrix3d(m4)
                        Dim nm = m3.Inverted().Transposed()
                        Dim nm4 As Matrix4d = Matrix4d.Identity
                        nm4.M11 = nm.M11 : nm4.M12 = nm.M12 : nm4.M13 = nm.M13
                        nm4.M21 = nm.M21 : nm4.M22 = nm.M22 : nm4.M23 = nm.M23
                        nm4.M31 = nm.M31 : nm4.M32 = nm.M32 : nm4.M33 = nm.M33
                        If hasN Then
                            Dim nrm = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Normals(i), nm4))
                            worldN.Add(New System.Numerics.Vector3(CSng(nrm.X), CSng(nrm.Y), CSng(nrm.Z)))
                        End If
                        If hasT Then
                            Dim tan = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Tangents(i), nm4))
                            worldT.Add(New System.Numerics.Vector3(CSng(tan.X), CSng(tan.Y), CSng(tan.Z)))
                        End If
                        If hasB Then
                            Dim bit = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Bitangents(i), nm4))
                            worldB.Add(New System.Numerics.Vector3(CSng(bit.X), CSng(bit.Y), CSng(bit.Z)))
                        End If
                    End If
                Next

                Dim clonedINiShape = destNif.CloneShape_Original(srcINiShape, shapeName, srcNif)
                If clonedINiShape Is Nothing Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"Clone failed: {shapeName}")
                    Continue For
                End If

                ' Reset clone's local T/R/S to identity. CloneShape_Original's unskinned branch
                ' (NifContent_Class.vb:407+) bakes srcShape's parent_chain (without root) into
                ' destShape.T/R/S so unskinned clones display at the right NIF-world position.
                ' Our verts are ALREADY in world coords (via PerVertexSkinMatrix, which absorbs
                ' parent_chain for unskinned and bone palette for skinned). Leaving the baked
                ' T/R/S in place would double-transform the verts.
                clonedINiShape.Translation = New System.Numerics.Vector3(0, 0, 0)
                clonedINiShape.Rotation = New NiflySharp.Structs.Matrix33()
                clonedINiShape.Scale = 1.0F

                ' Write world-pose attributes into the clone via its polymorphic adapter.
                Dim cloneRenderable As New NifRenderableShape(destNif, clonedINiShape, destIdx)
                Dim cloneAdapter = cloneRenderable.Geometry

                ' When the crown was zapped, the clone still carries the SOURCE vertex count + triangles.
                ' Resize the per-vertex storage DOWN to the survivor count BEFORE writing the (already
                ' compacted, nSurv-long) attribute arrays, then remap + drop triangles below. Identity
                ' case (no zap): nSurv == n, ResizeVertices is a documented no-op — skip it to keep the
                ' normal-shape path byte-for-byte as before.
                If hasZap AndAlso zappedCount > 0 Then cloneAdapter.ResizeVertices(nSurv)

                cloneAdapter.SetVertexPositions(worldPos)
                If hasN AndAlso cloneAdapter.HasNormals Then cloneAdapter.SetNormals(worldN)
                If hasT AndAlso cloneAdapter.HasTangents Then cloneAdapter.SetTangents(worldT)
                If hasB AndAlso cloneAdapter.HasTangents Then cloneAdapter.SetBitangents(worldB)

                ' Remap triangles after vertex compaction. Drop any triangle that touched a zapped
                ' (crown) vertex; reindex the survivors through oldToNew; track per-new-triangle
                ' provenance (source triangle index) so SetTriangles(provenance) redistributes the
                ' BSSubIndexTriShape Segments/SubSegmentDatas consistently (the same contract WM's
                ' RemoveZaps uses → MorphingHelper.vb:226). liveGeom.Indices is in source-triangle
                ' order (SkinningHelper.vb:412 flattens GetTriangles()), so tr = oldTriIdx.
                Dim triCheckOk As Boolean = True
                If hasZap AndAlso zappedCount > 0 Then
                    Dim idxArr = liveGeom.Indices
                    If idxArr Is Nothing Then
                        triCheckOk = False
                    Else
                        Dim newTris As New List(Of NiflySharp.Structs.Triangle)(idxArr.Length \ 3)
                        Dim provenance As New List(Of Integer)(idxArr.Length \ 3)
                        For tr = 0 To idxArr.Length - 3 Step 3
                            Dim a = CInt(idxArr(tr)), b = CInt(idxArr(tr + 1)), c = CInt(idxArr(tr + 2))
                            If a < 0 OrElse a >= n OrElse b < 0 OrElse b >= n OrElse c < 0 OrElse c >= n Then Continue For
                            Dim na = oldToNew(a), nb = oldToNew(b), nc = oldToNew(c)
                            If na < 0 OrElse nb < 0 OrElse nc < 0 Then Continue For  ' triangle touched the crown
                            newTris.Add(New NiflySharp.Structs.Triangle(CUShort(na), CUShort(nb), CUShort(nc)))
                            provenance.Add(tr \ 3)
                        Next
                        cloneAdapter.SetTriangles(newTris, TriangleRemap.SameShape(provenance))

                        ' ── Consistency verification (counts before/after) ──
                        ' Confirm no exported triangle references a dropped vertex and the survivor count
                        ' matches. GetTriangles()/GetVertexPositions() read back what was written.
                        Dim writtenTris = cloneAdapter.GetTriangles()
                        Dim writtenVerts = cloneAdapter.GetVertexPositions()
                        Dim maxIdx As Integer = -1
                        For Each t In writtenTris
                            maxIdx = Math.Max(maxIdx, Math.Max(CInt(t.V1), Math.Max(CInt(t.V2), CInt(t.V3))))
                        Next
                        Dim shapeNameLog = shapeName
                        Dim nLog = n, nSurvLog = nSurv, zapLog = zappedCount
                        Dim wvCount = writtenVerts.Count, wtCount = writtenTris.Count, srcTriCount = idxArr.Length \ 3
                        Dim newTriCount = newTris.Count, maxIdxLog = maxIdx
                        Logger.LogLazy(Function() $"[ZAP-EXPORT] '{shapeNameLog}' verts {nLog}→{nSurvLog} (zapped {zapLog}); tris {srcTriCount}→{newTriCount}; readback verts={wvCount} tris={wtCount} maxTriVtxIdx={maxIdxLog}")
                        If wvCount <> nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export vertex count mismatch (expected {nSurv}, got {wvCount})")
                        End If
                        If maxIdx >= nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export triangle references dropped vertex (maxIdx {maxIdx} >= {nSurv})")
                        End If
                    End If
                End If

                If hasZap AndAlso zappedCount > 0 AndAlso Not triCheckOk Then
                    ' Compaction produced an inconsistent shape — skip it rather than write a corrupt NIF.
                    destNif.RemoveShape_Manolo(clonedINiShape)
                    shapesFailed += 1
                    Continue For
                End If

                ' Strip skin on the clone. For BSTriShape this clears the VertexAttribute.Skinned
                ' flag (FinalizeData → CalcDataSizes excludes the bone weight/index bytes from the
                ' per-vertex stream on save). For NiTriShape the setter is a no-op; the
                ' SkinInstanceRef.Clear() below is what disables skinning in that family.
                clonedINiShape.IsSkinned = False
                clonedINiShape.SkinInstanceRef?.Clear()

                shapesWritten += 1
                destIdx += 1
            Catch ex As Exception
                shapesFailed += 1
                failureDetails.AppendLine($"{shapeName}: {ex.Message}")
            End Try
        Next

        If shapesWritten = 0 Then
            MessageBox.Show("No visible shapes were exported." & vbCrLf & failureDetails.ToString(),
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If

        ' Drop unreferenced BSSkin_Instance / BSSkin_BoneData / NiSkinInstance / NiSkinData /
        ' NiSkinPartition blocks orphaned by the cleared SkinInstanceRefs.
        Try
            destNif.RemoveUnreferencedBlocks()
        Catch
        End Try

        Try
            destNif.Save_As_Manolo(outPath, Overwrite:=True)
        Catch ex As Exception
            MessageBox.Show($"Failed to write {outPath}: {ex.Message}",
                            "NPC Model to NIF", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End Try

        Dim summary = $"Wrote {shapesWritten} shape{If(shapesWritten = 1, "", "s")} to {outPath}."
        If shapesFailed > 0 Then
            summary &= vbCrLf & $"{shapesFailed} shape{If(shapesFailed = 1, "", "s")} failed:" & vbCrLf & failureDetails.ToString()
        End If
        MessageBox.Show(summary, "NPC Model to NIF", MessageBoxButtons.OK,
                        If(shapesFailed = 0, MessageBoxIcon.Information, MessageBoxIcon.Warning))
    End Sub

    ''' <summary>Phase 4a delegate: bake one NPC's FaceGen NIF + FaceCustomization textures to
    ''' loose files. UI-thread / GL-bound. Returns a <see cref="NpcFaceGenPacker.BakedNpcBundle"/>
    ''' identifying the loose so the orchestrator can batch them into one PackBatch call after the
    ''' whole bake loop. Never throws — failures surface via Success=False + FailureMessage.</summary>
    Private Async Function RunChargenBake(npcFormID As UInteger,
                                          anchorPluginPath As String,
                                          sourcePluginName As String,
                                          progress As IProgress(Of NpcOverrideSaver.SaveProgress)) As Task(Of (Success As Boolean, Skipped As Boolean, Bundle As NpcFaceGenPacker.BakedNpcBundle, FailureMessage As String))
        ReportSaveProgress(progress, "Baking CharGen NIF + textures…", "", False, 0, 0)

        ' Bake the SAME identity the "Build CharGen (loose)" button uses for the rendered NPC: its
        ' overlay-resolved model source (ModelSourceFormID, else FormID). But ONLY when the NPC being
        ' baked IS the one currently rendered — in a batch ("Apply to all") save the other NPCs are
        ' not rendered, so for those we bake their own FormID directly (FaceGenBuilder.BuildCharGen is
        ' headless: it resolves appearance from the record + overlay, no render-host state).
        Dim bakeFormID As UInteger = npcFormID
        Dim rendered = _renderHost?.LastRenderedState
        If rendered IsNot Nothing AndAlso rendered.RootNpcFormID = npcFormID Then
            bakeFormID = If(rendered.ModelSourceFormID <> 0UI, rendered.ModelSourceFormID,
                            If(rendered.FormID <> 0UI, rendered.FormID, npcFormID))
        End If

        ' GL-bound bake (FaceTintCompositor GPU pipeline + GL.GetTexImage readback) — MUST stay on
        ' the UI thread, which owns the OpenGL context. Runs synchronously: no await has happened
        ' yet, so we are still on the UI thread the orchestrator called us from. Single Await
        ' Task.Yield at entry would already have yielded; placing it here keeps the function async.
        Await Task.Yield()
        Dim bakeResult As FaceGenBuilder.BuildResult
        Try
            ' willBePacked:=True — Save ESP normally repacks the _2 loose into a BA2 under canonical
            ' names (NpcFaceGenPacker), so the NIF must embed canonical texture paths. When the BA2
            ' pack is skipped (loose-only mode, Ba2Version_FO4=0), canonical paths are STILL what the
            ' engine looks up at runtime — _2 suffix is only the disk filename, not the NIF reference.
            ' WriteGPUSandboxOutput corre el GL (para el _2b) -> sync en el hilo UI (contexto GL; ya estamos
            ' en él tras el Yield), INDEPENDIENTE de DebugMode. Sin ese flag (output CPU-only, sin GL) -> bake
            ' en thread de fondo (Await Task.Run). Secuencial -> sin race entre NPCs.
            Dim fidL = bakeFormID
            If FaceGenBuilder.WriteGPUSandboxOutput Then
                bakeResult = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets,
                                                         _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides,
                                                         willBePacked:=True, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
            Else
                bakeResult = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets,
                                                         _renderHost, AddressOf _materialResolver.ApplyShapeMaterialOverrides,
                                                         willBePacked:=True, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
            End If
        Catch ex As Exception
            Return (False, False, Nothing, $"CharGen bake failed: {ex.Message}")
        End Try

        If bakeResult.Skipped Then
            ' No FaceGen head parts (non-human race, etc.) → nothing to bake/pack. SKIP, not failure.
            Return (True, True, Nothing, "")
        End If
        If Not bakeResult.Success Then
            Return (False, False, Nothing, "CharGen bake failed")
        End If

        Dim originPlugin = _pluginManager.GetOriginatingPluginName(bakeFormID)
        ' ESL-aware local id (FaceGenLocalId): the packed FaceGen file name MUST match the engine's
        ' lookup name (FaceGenBuilder.ResolveFaceGenPath / ResolveFaceGenNifPath both use FaceGenLocalId),
        ' so an ESL NPC bakes to "00000800", not "00032800" — otherwise the lookup misses the packed file.
        Dim formIdLow = FaceGenLocalId(bakeFormID)
        Logger.LogLazy(Function() $"[CHARGEN-ID] save npcFormID=0x{npcFormID:X8} bakeFormID=0x{bakeFormID:X8} → originPlugin='{originPlugin}' formIdLow=0x{formIdLow:X8}")

        Dim bundle As New NpcFaceGenPacker.BakedNpcBundle With {
            .OriginPlugin = originPlugin,
            .FormIdLow = formIdLow,
            .DebugSandbox = FaceGenBuilder.DebugMode
        }
        Return (True, False, bundle, "")
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
                                                progress As IProgress(Of NpcOverrideSaver.SaveProgress)) As Task(Of (Summary As String, Success As Boolean))
        If bundles Is Nothing OrElse bundles.Count = 0 Then
            Return ("", True)
        End If

        ' Capture config on the UI thread before going to the worker — same pattern the original
        ' RunChargenBakeAndPack used.
        Dim dataPath = _dataPath
        Dim game = Config_App.Current.Game
        Dim ba2Version = NPC_Config.Current.Ba2Version_FO4

        ' Loose-only sentinel: skip the pack. The 4 loose files per NPC stay on disk where the
        ' engine auto-discovers them at runtime. Matches the user's intent for the
        ' "None - Loose files" option in the SaveEsp BA2 version combo.
        If ba2Version = 0UI Then
            Dim n = bundles.Count
            Return ($"BA2 pack skipped — {n} NPC{If(n = 1, "", "s")} left as loose files (None - Loose mode).", True)
        End If

        Try
            Dim packResult = Await Task.Run(
                Function()
                    Return NpcFaceGenPacker.PackBatch(
                        anchorPluginPath, dataPath, game, ba2Version, bundles,
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
                        End Sub)
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
            Dim total = bundles.Count
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
            If committed = 0 Then
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
                ' Surface the count in the MessageBox; the per-path breakdown lives in the log
                ' (Debug build only — Release has Logger.Enabled=False, so no log file is written).
                ' Don't reference the log here so the Release message doesn't point users at a
                ' non-existent file.
                summary &= $" ⚠ {missingBundles} NPC{If(missingBundles = 1, "", "s")} failed to pack ({missingSources} file{If(missingSources = 1, "", "s")} unaccounted for)."
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
            _autoGenPluginsCache = SaveEsp_Form.ScanAutoGeneratedPlugins(_dataPath)
        End If

        ' Refresh the per-target-NPC flag without re-loading anything from disk. The cached
        ' entries store NpcFormIDs (built once at scan time); flipping the flag is a cheap
        ' lookup.
        For Each ep In _autoGenPluginsCache
            ep.ContainsTargetNpc = ep.NpcFormIDs IsNot Nothing AndAlso ep.NpcFormIDs.Contains(targetNpcFormID)
        Next
        Return _autoGenPluginsCache
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




























