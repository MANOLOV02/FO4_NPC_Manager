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
    Private _allNPCs As New List(Of NPC_Data)
    Private _previewControl As PreviewControl
    Private _dataPath As String = ""
    Private _assetDictionaryLoadTask As Task = Nothing
    Private ReadOnly _assetDictionaryLock As New Object()
    Private _previewRequestVersion As Integer = 0
    Private Shared ReadOnly _rng As New Random()
    ' ConcurrentDictionary, not Dictionary: GetParsedNpc writes on a cache miss (12458) from the
    ' background ResolveNPCBaseState path (Await Task.Run), which can race a concurrent render's read.
    Private _npcByIdCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, NPC_Data)()
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

    ''' <summary>Process-lifetime cache of every face-tint DDS byte buffer we have ever pulled
    ''' from the FilesDictionary. Keyed by the normalized "textures\..." path. A Nothing entry
    ''' is a *negative* cache for paths that resolve to a missing or empty file, so we don't
    ''' retry the same lookup on the next NPC. Reused across NPCs of the same race (region masks
    ''' are identical) and across re-previews of the same NPC. Invalidate via
    ''' <see cref="ClearFaceTintCaches"/> when the FilesDictionary is rebuilt.</summary>
    Private ReadOnly _tintBytesCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Per-MainForm caches of parsed ARMO/ARMA records keyed by FormID. <see cref="_pluginManager"/>
    ''' is ReadOnly (set once in the ctor) and the underlying records are immutable post-load, so a parsed
    ''' result is stable for this MainForm's lifetime — a new load order means a new PluginManager which
    ''' (ReadOnly) means a new MainForm, so these caches die with the load order and need no explicit
    ''' invalidation. ConcurrentDictionary because ResolvePreviewVariant → CollectMeshCandidates runs on a
    ''' Task.Run background thread and overlapping renders can execute concurrently. Reached via
    ''' <see cref="GetParsedArmo"/> / <see cref="GetParsedArma"/>, which replace the per-call
    ''' RecordParsers.ParseARMO/ParseARMA that re-decoded the same records on every render.</summary>
    Private ReadOnly _parsedArmoCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, ARMO_Data)()
    Private ReadOnly _parsedArmaCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, ARMA_Data)()
    ' Same rationale/lifetime as the ARMO/ARMA caches above. RACE especially: the NPC's race record is
    ' re-parsed ~20×/render (skeleton setup, body-weight, skin resolution) — memoizing collapses it to one.
    Private ReadOnly _parsedRaceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, RACE_Data)()
    Private ReadOnly _parsedHdptCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, HDPT_Data)()

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
        _parsedRaceCache.Clear()
        _parsedHdptCache.Clear()
        _skelHkxBytesCache.Clear()
        _skelBptdBytesCache.Clear()
        _skelFaceBytesCache.Clear()
        _npcByIdCache?.Clear()
    End Sub

    ' _renderHost.TintGpuCache, _renderHost.PristineDiffusePixels and the PristinePixels nested class moved to
    ' NpcRenderHost so each preview surface owns its own caches.

    ' xEdit wbDefinitionsFO4.pas:7365-7372 mapea HDPT.DATA flags POR POSICIÓN del array
    ' wbFlags, no por el valor en los comentarios `{0x...}`. Posiciones reales:
    '   bit 0 (0x01) Playable, bit 1 (0x02) Male, bit 2 (0x04) Female,
    '   bit 3 (0x08) IsExtraPart, bit 4 (0x10) UseSolidTint, bit 5 (0x20) UsesBodyTexture.
    Private Const HeadPartFlagUseSolidTint As Byte = &H10
    ''' <summary>Flag bit at position 3 of HDPT.DATA — "Is Extra Part". Verified against
    ''' wbDefinitionsFO4.pas:7369 (entry 4 in the wbFlags positional array). Set on HDPTs that are
    ''' addons referenced via another HDPT's HNAM (eyelashes, hairlines, etc.) rather than
    ''' standalone parts. CharGenInterface.cpp:96 filters these out when serializing a preset.</summary>
    Private Const HeadPartFlagIsExtra As Byte = &H8
    Private Const HeadPartTypeFace As Integer = 1
    Private Const HeadPartTypeEyes As Integer = 2
    Private Const HeadPartTypeHair As Integer = 3
    Private Const HeadPartTypeFacialHair As Integer = 4
    Private Const HeadPartTypeHeadRear As Integer = 9

    Private Enum PreviewMode
        FullCharacter = 0
        OnlyFace = 1
    End Enum

    Private Enum GenderFilterMode
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

    Private Class PreviewVariantDefinition
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

    Private Class TemplateDependencyEdge
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

    ''' <summary>Devuelve la clasificación meatcap de un sub-segment. Reglas duras del NIF
    ''' arriba; cualquier otro valor (incluido 0, los rangos biped 30..61 y rangos robot
    ''' 65..95) cae en Normal. Función pura, sin side effects, llamable durante load.</summary>
    Friend Shared Function ClassifyMeatcap(sub_ As BSTriShapeGeometry.NifSubSegmentInfo) As MeatcapClassification
        If sub_ Is Nothing Then Return MeatcapClassification.Normal
        Dim slot As UInteger = sub_.UserSlotID
        ' Confirmed: BSDismemberBodyPartType SECTIONCAP_* y TORSOCAP_* — enum NIF.
        If (slot >= 101UI AndAlso slot <= 113UI) OrElse (slot >= 201UI AndAlso slot <= 213UI) Then
            Return MeatcapClassification.Confirmed
        End If
        ' Tentative: BS-OS .xrc los etiqueta "Gore", Bethesda no los confirma. Auditable.
        If slot = 100UI OrElse slot = 102UI OrElse slot = 103UI Then
            Return MeatcapClassification.Tentative
        End If
        Return MeatcapClassification.Normal
    End Function

    ''' <summary>Clasifica una shape entera mirando todos sus sub-segments. Una shape se
    ''' considera meatcap si CUALQUIER sub no-vacío (numTris>0) lo es. Devuelve la peor
    ''' clasificación encontrada (Confirmed > Tentative > Normal) para que el log distinga
    ''' shapes 100% spec de las dependientes de BS-OS. Shapes sin BSSubIndexTriShape o sin
    ''' segmentation devuelven Normal.</summary>
    Friend Shared Function ClassifyShapeMeatcap(geom As IShapeGeometry) As MeatcapClassification
        If geom Is Nothing Then Return MeatcapClassification.Normal
        Dim subIndex = TryCast(geom.BackingShape, BSSubIndexTriShape)
        If subIndex Is Nothing Then Return MeatcapClassification.Normal
        Dim snap = BSTriShapeGeometry.GetSegmentation(subIndex)
        If snap.IsEmpty Then Return MeatcapClassification.Normal

        Dim worst As MeatcapClassification = MeatcapClassification.Normal
        For Each parentSeg In snap.Info.Segs
            If parentSeg.Subs Is Nothing Then Continue For
            For Each sub_ In parentSeg.Subs
                Dim c = ClassifyMeatcap(sub_)
                If c > worst Then worst = c
                If worst = MeatcapClassification.Confirmed Then Return worst ' early exit
            Next
        Next
        Return worst
    End Function

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
        ' [TEST: TPLT-traits-bucket] Face-appearance fields moved here from ModelAnimationState.
        ' xEdit's wbTemplateFlags lists 15 bits but doesn't pin each NPC_ subrecord to a specific
        ' bit. The previous bucketing (HeadParts/HairColor/HeadTexture/QNAM under ModelAnimation)
        ' was an undocumented convention. Trying these under Traits since the CK label "Use Traits"
        ' covers the actor's visual identity (race, skin, head, hair) — same conceptual bucket as
        ' RNAM/WNAM/MWGT. Revert by moving the 5 fields back to ModelAnimationState if any cohort
        ' regression appears.
        Public HeadTextureFormID As UInteger
        Public HairColorFormID As UInteger
        Public FacialHairColorFormID As UInteger
        Public HasTextureLighting As Boolean
        Public TextureLightingColor As Color = Color.Empty
        Public HeadPartFormIDs As New List(Of UInteger)
    End Class

    Private Class InventoryState
        Public DefaultOutfitFormID As UInteger
        Public SleepOutfitFormID As UInteger
    End Class

    Private Class ModelAnimationState
        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM moved
        ' to TraitsState. ObjectTemplateOMODFormIDs kept here — OBTS combinations are model
        ' assembly (robot parts), conceptually closer to Model/Animation than to Traits.
        ''' <summary>Legacy flat list of OMOD FormIDs from combo #0 (kept for back-compat).</summary>
        Public ObjectTemplateOMODFormIDs As New List(Of UInteger)
        ''' <summary>Full OBTE/OBTS combinations — used by the new robot path resolver.</summary>
        Public ObjectTemplateCombinations As New List(Of FO4_Base_Library.NPC_ObjectTemplateCombination)
        ''' <summary>True when source NPC_ had an OBTE present.</summary>
        Public HasObjectTemplate As Boolean = False
        ''' <summary>NPC.APPR (Attach Parent Slots) — list of AP keywords the actor exposes
        ''' as the initial pool for OBTE OMOD AP-filter. Brahmin: [ap_HornsL, ap_HornsR, ap_PackBase];
        ''' Codsworth: presumed empty/different — AP filter only applies when NPC.APPR != empty.</summary>
        Public AttachParentSlotFormIDs As New List(Of UInteger)
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

    ''' <summary>Build an empty (no transforms) Poses_class that, when passed through the
    ''' face bone skeleton resolver, triggers AppplyPoseToSkeleton's internal Reset() without
    ''' applying any deltas. Used to clear the previous FMRS state when toggling the bone
    ''' morphs checkbox OFF.</summary>
    Private Shared Function BuildEmptyFacePose() As Poses_class
        Return New Poses_class With {
            .Name = "__reset_face_pose__",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)
        }
    End Function

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

    ''' <summary>ARMA Bone Scale Delta application model. The xEdit field is named "Bone Scale
    ''' <summary>ARMA Sculpt Data application formula. HARDCODED to H3 multiplicative
    ''' (s = race_s · (1 + arma_d)) on 2026-04-27 after consolidating the slot-based per-shape
    ''' application rule. **A REVISAR** — la fórmula matemática (cómo se combina arma_d con race_s)
    ''' NO está confirmada experimentalmente contra CK ground truth. Es la candidata más
    ''' conceptualmente limpia (cumple las 3 invariantes naturales: identity outfit, identity race,
    ''' suma de volumen donde delta>0). Pero el test diferencial CK que confirme esta fórmula
    ''' (clon ARMA con bone modificado a valor conocido) está pendiente.</summary>
    ''' <summary>Toggle body-weight pose (MWGT × BSMS + MRSV + ARMA sculpt H3). Triggers granular
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
    Private Enum BodyWeightClampModel
        Off = 0
        ClampWeightL1 = 1
        ClampFinal = 2
        ClampBoth = 3
    End Enum

    ''' <summary>Active body-weight clamp model, read per-bone in BuildBodyWeightPose. Set to
    ''' ClampBoth: honors the documented "Range clamps the weight delta" intent AND keeps the final
    ''' bone scale inside [1+Min, 1+Max] regardless of weight/MRSV direction (see RecordParsers.vb
    ''' RACE_BoneData docs). NOTE: chosen default pending CK confirmation on opposite-direction
    ''' bones (e.g. ShoulderFat). The other models stay in the enum so this can be changed in the
    ''' future by editing this one line — no re-plumbing. (The diagnostic ComboBox that exposed all
    ''' four models was removed once ClampBoth was selected.)</summary>
    Private Shared ReadOnly _bodyWeightClampModel As BodyWeightClampModel = BodyWeightClampModel.ClampBoth

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
        Dim basePose = BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, host.LastSkeletonInstance, Nothing)
        ' Los bone-morphs van a la capa MorphDeltaTransform (no a la pose). Así la capa Delta
        ' (pose/animación) queda libre y el morph sobrevive a un futuro ApplyPose por frame.
        host.LastSkeletonInstance.ApplyBoneMorphPose(basePose)
        ' [MOUNTDELTA-PREPASS] Repopular MountDelta desde la cache del render inicial (re-write
        ' idempotente; ApplyBoneMorphPose no borra el mount).
        ApplyMountPlanForActor(host.LastSkeletonInstance, host.LastRenderData)

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
            Dim poseForArma = BuildMergedNpcPose(host.LastRenderedState, host.LastRenderData, fmrsEnabled, bwEnabled, armaSkel, sculpt)
            armaSkel.ApplyBoneMorphPose(poseForArma)
            ' [MOUNTDELTA-PREPASS] Per-instance MountDelta para este clone sculpt — repopula desde cache.
            ApplyMountPlanForActor(armaSkel, host.LastRenderData)
        Next

        ' Re-route shape→skel mappings según el toggle actual. Sclpt=ON → shapes con sculpt apuntan
        ' a su per-ARMA skel; Sclpt=OFF → todos apuntan al base. La mutación es visible al resolver
        ' porque éste tiene el dict por referencia.
        If host.LastShapeToSkel IsNot Nothing Then
            For Each shape In host.LastRenderData.Shapes
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
    Private ReadOnly _animRaceCache As New Dictionary(Of String, AnimRaceModel)(StringComparer.OrdinalIgnoreCase)
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
            If key = _animCacheKey AndAlso _animClips IsNot Nothing Then Return _animClips.Count > 0  ' combo ya poblado

            Dim model As AnimRaceModel = Nothing
            If Not _animRaceCache.TryGetValue(key, model) Then
                Dim loader As Func(Of String, Byte()) = AddressOf LoadAnimHkxBytes
                model = New AnimRaceModel With {
                    .Clips = BehaviorClipEnumerator.EnumerateClips(rb, loader).
                                OrderBy(Function(c) AnimClipLabel(c), StringComparer.OrdinalIgnoreCase).ToList(),
                    .SkeletonBytes = LoadAnimHkxBytes(BehaviorClipEnumerator.ResolveHavokSkeleton(rb, loader))
                }
                _animRaceCache(key) = model
                Logger.LogLazy(Function() $"[ANIM-BAR] enumerated race {key} ({rb.RaceEditorID}): {model.Clips.Count} clips, skeletonBytes={If(model.SkeletonBytes Is Nothing, 0, model.SkeletonBytes.Length)}")
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
            End If

            _animClips = model.Clips
            _animSkeletonBytes = model.SkeletonBytes
            _animCacheKey = key
            ' Combo plano = TODO (sin filtros de género/1ª-persona; el filtrado vive solo en el picker). Lista paralela
            ' propia para que el remap de índices y el add-if-missing del picker sigan andando uniformemente.
            _animComboClips = _animClips.ToList()

            _animSuppress = True
            ComboAnim.BeginUpdate()
            ComboAnim.Items.Clear()
            ComboAnim.Items.Add("(None — static)")
            ComboAnim.Items.AddRange(_animComboClips.Select(Function(c) CObj(AnimClipLabel(c))).ToArray())
            ComboAnim.SelectedIndex = 0
            ComboAnim.EndUpdate()
            _animSuppress = False
            Logger.LogLazy(Function() $"[ANIM-BAR] NPC 0x{fid:X8} race={rb.RaceEditorID}({key}): {_animClips.Count} clips, skeletonBytes={If(_animSkeletonBytes Is Nothing, 0, _animSkeletonBytes.Length)}{If(_animSkeletonBytes Is Nothing, " ** NO HAVOK SKELETON RESOLVED **", "")}")
            Return _animClips.Count > 0
        Catch ex As Exception
            Logger.LogLazy(Function() $"[ANIM-BAR] resolve failed: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
    End Function

    ' Activa/desactiva el modo "reproduciendo animación" del control de render (como WM): cuando está
    ' activo, los frames se aplican como cambio de POSE solamente — sin reset de cámara ni recompute de
    ' bounds (Render.vb) → updates eficientes. Se apaga al volver a estático o al cambiar de NPC.
    Private Sub SetPlayingAnimation(value As Boolean)
        If _renderHost IsNot Nothing AndAlso _renderHost.PreviewCtl IsNot Nothing Then _renderHost.PreviewCtl.PlayingAnimation = value
    End Sub

    ' Combo plano = TODO (incluye los clips de 1ª persona, que el picker oculta por defecto). Como acá NO hay
    ' filtro, marcamos "1st-person" en el texto para distinguir los clips de cámara/viewmodel (inútiles para
    ' preview de NPC). Sufijo, no prefijo, para no alterar el orden alfabético por nombre del combo.
    Private Shared Function AnimClipLabel(c As ResolvedAnimationClip) As String
        Dim nm = If(String.IsNullOrWhiteSpace(c.ClipName), System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile), c.ClipName)
        Dim roles = If(c.Roles.Count > 0, $"  [{String.Join(",", c.Roles)}]", "")
        Dim fp = If(c.Is1stPersonOnly, "  · 1st-person", "")
        Return $"{nm}{roles}{fp}"
    End Function

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
                    ComboAnim.Items.Add(AnimClipLabel(picked))
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
        InitializeComponent()
        _pluginManager = pluginManager
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

        ' Bulk-parse uses the full ParseNPC. The cache (_allNPCs / _npcByIdCache) is consumed
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
            If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then Continue For

            Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData)
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
                If HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.BaseData) Then
                    Return ResolveInheritedFullName(ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.BaseData), visited)
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
        _npcByIdCache = New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, NPC_Data)(
            _allNPCs.GroupBy(Function(n) n.FormID).Select(Function(g) g.First()).ToDictionary(Function(n) n.FormID))
        _templateDependencyMapCache = BuildTemplateDependencyMap(_npcByIdCache)
        _templateRootSourceIdsCache = BuildTemplateTreeRootSourceIds(_npcByIdCache, _templateDependencyMapCache)
        ' Pre-build per-NPC caches (searchable text + display label) en bulk una sola vez. La
        ' lectura per-keystroke pasa a Dictionary.TryGetValue O(1) sin string formatting.
        ' BuildNPCClassification() llamado a continuación pisa los caches y los rellena de nuevo
        ' — orden importa: classifications limpia ambos primero, así no acumulamos entries
        ' obsoletos.
        BuildNPCClassification()
        BuildSkinArmoUniverse()
        BuildOutfitUniverse()
        BuildLmSkinTemplateCache()
        For Each npc In _npcByIdCache.Values
            _npcSearchableCache(npc.FormID) = BuildNpcSearchableText(npc)
            _npcDisplayLabelCache(npc.FormID) = BuildNpcDisplayLabel(npc)
        Next
    End Sub

    ''' <summary>Build the concatenated lowercase searchable text for an NPC. Mirror the same 6
    ''' fields que MatchesNpcFilter comparaba (ToString, EditorID, FullName, PluginName,
    ''' FormID hex). Single string permite reducir el match a un IndexOf en lugar de 6.</summary>
    Private Shared Function BuildNpcSearchableText(npc As NPC_Data) As String
        If npc Is Nothing Then Return ""
        Dim sb As New System.Text.StringBuilder()
        sb.Append(If(npc.ToString(), "")).Append("|"c)
        sb.Append(If(npc.EditorID, "")).Append("|"c)
        sb.Append(If(npc.FullName, "")).Append("|"c)
        sb.Append(If(npc.PluginName, "")).Append("|"c)
        sb.Append(npc.FormID.ToString("X8"))
        Return sb.ToString().ToLowerInvariant()
    End Function

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
                armo = GetParsedArmo(armoFID)
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
                    arma = GetParsedArma(addon.ArmaFormID)
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
        Dim traits = ResolveTraitsStateFromNPC(npcFormID, New HashSet(Of UInteger)(), warnings)
        If traits Is Nothing Then Return 0UI
        If traits.HairColorFormID <> 0UI Then Return traits.HairColorFormID

        Dim raceRec = _pluginManager.GetRecord(traits.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 0UI
        Dim race = ParseRaceCached(raceRec)
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
            armo = GetParsedArmo(armoFID)
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
                Dim race = ParseRaceCached(rec)
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
                armo = GetParsedArmo(armoFID)
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
                    arma = GetParsedArma(addon.ArmaFormID)
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
    Private Shared Function EffectiveArmaSlotMask(arma As ARMA_Data, armo As ARMO_Data) As UInteger
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
                    armo = GetParsedArmo(armoFID)
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
                    tArmo = GetParsedArmo(terminalFID)
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
                unionMask = unionMask Or ComputeArmoEffectiveSlotMask(GetParsedArmo(t), npcRaceFID, isFemale).Mask
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
                arma = GetParsedArma(addon.ArmaFormID)
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
            mask = mask Or ComputeArmoEffectiveSlotMask(GetParsedArmo(t), npcRaceFID, isFemale).Mask
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
            For Each kv In _npcByIdCache
                Dim npc = kv.Value
                If npc Is Nothing OrElse Not _npcsInGameWorld.Contains(npc.FormID) Then Continue For
                If Not NpcInheritsVisualAppearance(npc) Then Continue For
                inheritingInWorld += 1
                If _directlyPlacedNPCFormIDs.Contains(npc.FormID) Then
                    placedInheriting += 1
                    Dim n = npc
                    Logger.LogLazy(Function() $"[SECTION1-DISCARD] placed+inheriting (hidden from ESP-madre) " &
                                              $"FormID=0x{n.FormID:X8} '{DescribeNpc(n)}' plugin='{n.PluginName}' " &
                                              $"templateFlags=0x{n.TemplateFlags:X4} " &
                                              $"useTraits={HasTemplateFlag(n.TemplateFlags, NPC_TemplateCategory.Traits)} " &
                                              $"useModelAnim={HasTemplateFlag(n.TemplateFlags, NPC_TemplateCategory.ModelAnimation)} " &
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

    ''' <summary>True if this NPC inherits its visual appearance (Traits or ModelAnimation) from any template.
    ''' Such NPCs are generic — their look is defined by the template chain, not by themselves.</summary>
    Private Shared Function NpcInheritsVisualAppearance(npc As NPC_Data) As Boolean
        If npc Is Nothing OrElse npc.TemplateFlags = 0US Then Return False
        Return HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) OrElse
               HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation)
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
        Dim ownFace = Not NpcInheritsVisualAppearance(n)
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

        If (_npcByIdCache Is Nothing OrElse _npcByIdCache.Count = 0) AndAlso _allNPCs.Count > 0 Then
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
                        displayLabel = BuildNpcDisplayLabel(npc)
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
                            If Not _npcByIdCache.TryGetValue(leafFid, leafNpc) Then Continue For
                            If onlyChanged AndAlso Not _dirtyNpcs.Contains(leafFid) Then Continue For
                            If normalizedFilter.Length > 0 AndAlso Not MatchesNpcFilter(leafNpc, Nothing, normalizedFilter) Then Continue For
                            visibleLeaves.Add(leafNpc)
                        Next

                        If onlyChanged Then
                            If visibleLeaves.Count = 0 Then Continue For
                        ElseIf normalizedFilter.Length > 0 AndAlso visibleLeaves.Count = 0 AndAlso Not MatchesRecordFilter(rec, normalizedFilter) Then
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
                                    childLabel = BuildNpcDisplayLabel(leafNpc)
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
                                If Not _npcByIdCache.TryGetValue(leafFid, leafNpc) Then Continue For
                                Dim childLabel As String = Nothing
                                If Not _npcDisplayLabelCache.TryGetValue(leafFid, childLabel) Then
                                    childLabel = BuildNpcDisplayLabel(leafNpc)
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

    ''' <summary>Display label for an NPC tree node: "FullName (EditorID, FormID)" with fallbacks
    ''' a EditorID (FormID) cuando no hay FullName, o sólo FormID cuando tampoco hay EditorID.
    ''' Compartido por Section 1 placed NPCs y Section 2 LVLN children.</summary>
    Private Shared Function BuildNpcDisplayLabel(npc As NPC_Data) As String
        Dim formIdText = npc.FormID.ToString("X8")
        If npc.FullName <> "" Then
            Dim parenContent = If(npc.EditorID <> "", $"{npc.EditorID}, {formIdText}", formIdText)
            Return $"{npc.FullName} ({parenContent})"
        ElseIf npc.EditorID <> "" Then
            Return $"{npc.EditorID} ({formIdText})"
        End If
        Return formIdText
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
            If HasTemplateFlag(npc.TemplateFlags, category) Then
                parts.Add(GetTemplateCategoryLabel(category))
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
                           Dim compare = StringComparer.OrdinalIgnoreCase.Compare(GetNpcNodeDisplayText(left.DependentNpc, left), GetNpcNodeDisplayText(right.DependentNpc, right))
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
            If Not HasTemplateFlag(npc.TemplateFlags, category) Then Continue For

            Dim sourceFormID = ResolveTemplateSourceFormID(npc, category)
            If sourceFormID = 0UI Then Continue For

            dependencies.Add(New KeyValuePair(Of UInteger, String)(sourceFormID, GetTemplateCategoryLabel(category)))
        Next

        Return dependencies
    End Function

    Private Shared Function GetTemplateCategoryLabel(category As NPC_TemplateCategory) As String
        Select Case category
            Case NPC_TemplateCategory.AIData
                Return "AI Data"
            Case NPC_TemplateCategory.AIPackages
                Return "AI Packages"
            Case NPC_TemplateCategory.ModelAnimation
                Return "Model/Animation"
            Case NPC_TemplateCategory.BaseData
                Return "Base Data"
            Case NPC_TemplateCategory.DefaultPackageList
                Return "Default Package List"
            Case Else
                Return category.ToString()
        End Select
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
            node = New TreeNode(GetNpcNodeDisplayText(npc, dependencyEdge)) With {
                .Name = $"NPC_{npc.FormID:X8}",
                .Tag = npc
            }
        Else
            Dim sourceRec = _pluginManager.GetRecord(sourceId)
            If sourceRec Is Nothing OrElse sourceRec.Header.Signature <> "LVLN" Then Return Nothing

            selfMatches = MatchesRecordFilter(sourceRec, filter)
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

    Private Shared Function GetNpcNodeDisplayText(npc As NPC_Data, dependencyEdge As TemplateDependencyEdge) As String
        Dim baseText = If(npc Is Nothing, "<unknown NPC>", npc.ToString())
        If dependencyEdge Is Nothing OrElse dependencyEdge.Categories.Count = 0 Then Return baseText
        Return $"{baseText} (template: {String.Join(", ", dependencyEdge.Categories)})"
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
            Dim fallback = BuildNpcSearchableText(npc)
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

    Private Shared Function MatchesRecordFilter(rec As PluginRecord, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) Then Return True
        If rec Is Nothing Then Return False
        If Not String.IsNullOrEmpty(rec.EditorID) AndAlso rec.EditorID.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If rec.Header.FormID.ToString("X8").Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If Not String.IsNullOrEmpty(rec.SourcePluginName) AndAlso rec.SourcePluginName.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        If Not String.IsNullOrEmpty(rec.Header.Signature) AndAlso rec.Header.Signature.Contains(filter, StringComparison.OrdinalIgnoreCase) Then Return True
        Return False
    End Function

    Private Function GetTemplateSourceSortKey(sourceId As UInteger, npcById As IReadOnlyDictionary(Of UInteger, NPC_Data)) As String
        If npcById.ContainsKey(sourceId) Then Return GetNpcNodeDisplayText(npcById(sourceId), Nothing)

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

        Return DescribeRecord(sourceRec)
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
        PopulateRecordDetails(TryCast(e.Node?.Tag, NPC_Data))
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
            If targetFid <> 0UI AndAlso _npcByIdCache.TryGetValue(targetFid, npc) AndAlso npc IsNot Nothing Then
                _currentRandomPickFormID = targetFid
                RefreshMultiSelectControls()
                TreeViewNPCs.Invalidate()
                PopulateRecordDetails(npc)
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
                    If _npcByIdCache.TryGetValue(fid, n) AndAlso n IsNot Nothing Then
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
        If pick = 0UI OrElse Not _npcByIdCache.TryGetValue(pick, npc) OrElse npc Is Nothing Then Return
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
            If _npcByIdCache.TryGetValue(shownFormID, npc) AndAlso npc IsNot Nothing Then
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
            SetStatus($"Loading assets for {npc}...")
            Await EnsureAssetDictionaryAsync()
            If requestVersion <> _previewRequestVersion Then Return

            SetStatus($"Resolving {npc}...")
            Dim baseState As NPCVisualState = Nothing
            Dim outfitEntries As List(Of OutfitComboEntry) = Nothing
            Await Task.Run(Sub()
                               baseState = ResolveNPCBaseState(npc, _renderHost)
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
            Dim pickedFormID = PickWeightedRandomFromLVLN(lvlnData.FormID, New HashSet(Of UInteger)())
            If pickedFormID = 0UI Then
                SetStatus($"No NPCs found in {lvlnData.EditorID}")
                Return
            End If

            Dim npc As NPC_Data = Nothing
            _npcByIdCache.TryGetValue(pickedFormID, npc)
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
                               baseState = ResolveNPCBaseState(npc, _renderHost)
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

        ' Cualquier re-render del preview principal (cualquier path: wrapper, RenderFromCurrentSelection,
        ' etc.) invalida la animación en curso y refresca la barra al NPC actual (CurrentBaseState ya es
        ' el nuevo). Imprescindible para que el combo NO quede con clips de la raza anterior.
        If host Is _renderHost Then RefreshAnimBarForCurrentNpc()

        ' Build final state with selected outfit
        Dim state = CloneVisualState(host.CurrentBaseState)
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
            .DisplayName = $"{DescribeNpc(GetParsedNpc(state.FormID))} | {ComboBoxOutfit.Text}",
            .State = state,
            .UseFaceGen = useFaceGen,
            .OnlyFaceCollect = onlyFaceCollect,
            .OnlyOutfitCollect = onlyOutfitCollect
        }

        SetStatus($"Rendering {previewVariant.DisplayName}...")
        Dim renderData As PreviewResolutionResult = Nothing
        Await Task.Run(Sub() renderData = ResolvePreviewVariant(previewVariant))
        If requestVersion <> _previewRequestVersion Then Return

        If renderData Is Nothing OrElse renderData.Shapes.Count = 0 Then
            SetStatus($"No meshes found{BuildWarningSuffix(renderData?.Warnings)}")
            Return
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
        ApplyPipboySyntheticSkin(renderData, inst)

        Dim basePose = BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled,
                                          inst, Nothing)  ' Nothing = no sculpt → base pose
        ' Bone-morphs → capa MorphDeltaTransform (deja libre la capa pose/animación).
        inst.ApplyBoneMorphPose(basePose)

        ' DIAG POST-PASE: dump del estado de los bones inyectados de chunks robot DESPUÉS de ApplyPose.
        Dim shapeToSkel As New Dictionary(Of IRenderableShape, SkeletonInstance)
        Dim skelByArma As New Dictionary(Of UInteger, SkeletonInstance)
        Dim sculptByArma As New Dictionary(Of UInteger, Dictionary(Of String, System.Numerics.Vector3))
        For Each shape In renderData.Shapes
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
                Dim poseForArma = BuildMergedNpcPose(state, renderData, boneMorphsEnabled, bodyWeightEnabled,
                                                     armaSkel, sculpt)
                armaSkel.ApplyBoneMorphPose(poseForArma)
                skelByArma(armaFormID) = armaSkel
                sculptByArma(armaFormID) = sculpt
            End If
            shapeToSkel(shape) = armaSkel
        Next

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
                Dim cls = ClassifyShapeMeatcap(sh.Geometry)
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
                    EnsureChunkToActor(_cand, _candByOrdinal, renderData, targetSkel, targetWB, ensureVisiting)
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

            CollectV2PlanForShape(shape, socket, targetSkel, renderData, targetWB, isRobotMount)
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
        ApplyMountPlanForActor(inst, renderData)
        ' Per-instance scope: cada clone sculpt aplica su propio subset (filtrado por TargetSkel).
        For Each kv In skelByArma
            Dim cloneInst = kv.Value
            If cloneInst Is Nothing OrElse ReferenceEquals(cloneInst, inst) Then Continue For
            ApplyMountPlanForActor(cloneInst, renderData)
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
                        Dim childNorm = StripInstanceSuffix(childName)
                        For idx As Integer = 0 To shapeBones2.Count - 1
                            Dim niN = TryCast(shapeBones2(idx), NiflySharp.Blocks.NiNode)
                            Dim bn = If(niN?.Name?.String, "")
                            Dim bnNorm = StripInstanceSuffix(bn)
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
                        Dim childNorm5 = StripInstanceSuffix(childName)
                        For Each candidateBlock In sh.NifContent.Blocks
                            Dim niNodeCand = TryCast(candidateBlock, NiflySharp.Blocks.NiNode)
                            If niNodeCand Is Nothing Then Continue For
                            Dim candName = If(niNodeCand.Name?.String, "")
                            Dim candNorm = StripInstanceSuffix(candName)
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

        Dim request As New RenderRequest With {
            .Shapes = renderData.Shapes,
            .SkeletonResolver = skelResolver,
            .MorphResolver = morphResolver,
            .RecalculateNormals = True,
            .ResetCamera = True
        }
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
                                                             ApplyFaceTintOverlay(capturedState, capturedRenderData, capturedHost)
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

        ' Cache the resolved state + render data + skeleton instance so the morph/pose checkbox
        ' handlers can rebuild the merged pose on demand without re-running the full preview
        ' resolution pipeline. See CheckBoxApplyBoneMorphs_CheckedChanged /
        ' CheckBoxApplyVertexMorphs_CheckedChanged below — they follow the WM granular
        ' Intent.MarkDirty(Pose)/MarkDirty(Morphs) pattern, not a full reload.
        host.LastRenderedState = state
        host.LastRenderData = renderData
        host.LastSkeletonInstance = inst
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
                Dim normTri = NormalizeDictionaryKeyWithMeshesPrefix(faceTriPath)
                Dim triLoc As FilesDictionary_class.File_Location = Nothing
                If FilesDictionary_class.Dictionary.TryGetValue(normTri, triLoc) Then
                    Dim triBytes = triLoc.GetBytes()
                    If triBytes IsNot Nothing AndAlso triBytes.Length > 0 Then
                        Dim triHead = TriHeadParser.ParseTriHeadFromBytes(triBytes)
                        If triHead IsNot Nothing AndAlso triHead.Morphs IsNot Nothing Then
                            For Each m In triHead.Morphs
                                If Not String.IsNullOrEmpty(m.Name) Then
                                    host.LastFaceTriMorphNames.Add(m.Name)
                                End If
                            Next
                        End If
                    End If
                End If
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
    End Function

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
        If Not _npcByIdCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
            Throw New InvalidOperationException($"NPC 0x{npcFormID:X8} not in cache.")
        End If
        Dim requestVersion = Threading.Interlocked.Increment(_previewRequestVersion)
        Await LoadNPCOnDemandAsyncFromExisting(npc, requestVersion, targetHost)
    End Function

    ''' <summary>Entry point invoked right after RenderShapes. Tries to bake tints immediately;
    ''' if the face diffuse texture isn't in the cache yet (async upload pending), schedules a
    ''' polling timer that retries until the texture appears.</summary>
    ''' <summary>Run the face-tint compositor + the two skin-softlight pre-passes for the given
    ''' state. ALL targets (face/body diffuse) are required to be already uploaded into the GL
    ''' Textures_Dictionary before calling — that's the contract of the
    ''' <c>RenderIntent.PostTextureUploadAction</c> hook this is registered to. No defer
    ''' machinery: if the hook fired, textures are guaranteed ready.</summary>
    Private Sub ApplyFaceTintOverlay(state As NPCVisualState, renderData As PreviewResolutionResult, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If state Is Nothing Then Return

        ' Single skin-tone path: the slot-12 SkinTone (authored, or a QNAM stand-in synthesized
        ' in FaceTintLayerBuilder when the NPC authors none) is composed as a normal tint layer
        ' in engine rank order INSIDE TryApplyFaceTints. Detail tints ranked after slot 12 (brow,
        ' scars) therefore compose on top of the toned skin instead of being washed out by a
        ' separate full-face SoftLight post-pass. No face-side TryApplyFaceSkinSoftLight anymore.
        ' Body SoftLight stays a separate pass (different meshes).
        TryApplyFaceTints(state, host)
        ' Render-only: make body skin light like the face (subsurface scattering). Runs before
        ' the SoftLight pass and is NOT gated by its skin-tone guards — see method docs.
        MatchBodySkinSubsurfaceToFace(host)
        TryApplyBodySkinSoftLight(state, host)
    End Sub

    ''' <summary>Render-only: copy the authoritative face material's subsurface-scattering
    ''' response onto every body skin material so face and body skin light identically. The
    ''' face material (BSLightingShaderType.FaceTint) "wins": its SubsurfaceLighting (on/off)
    ''' and SubsurfaceLightingRolloff are copied verbatim (including False) onto each body skin
    ''' material (the SkinTint flag, excluding the face itself). The render shader reads both
    ''' fields per material every draw (Render.vb: bSoftlight + subsurfaceRolloff), so this
    ''' mutation takes effect on the next frame with no texture work.
    '''
    ''' Sole precondition: a face material exists AND a body skin material exists — none of the
    ''' SoftLight guards (HasTextureLighting / race SkinTone catalog / QNAM opacity) apply,
    ''' because subsurface response is a material lighting property independent of skin TONE.
    ''' Runs at the render-finalization chokepoint (ApplyFaceTintOverlay), by which point every
    ''' shape's material is fully resolved (per-candidate ApplyShapeMaterialOverrides already
    ''' ran) and is not re-resolved again before the draw.
    '''
    ''' Render-only / no persistence: each shape owns a fresh material instance deserialized per
    ''' load (TryLoadMaterialFromDictionary: New + Deserialize — no shared cache), the FaceGen
    ''' bake builds its own material wrappers (FaceGenBuilder), and Save ESP never serializes
    ''' material fields. Values come from the loaded face material (its BGSM/inline shader), never
    ''' hardcoded. BGSM-only: the SubsurfaceLighting getter throws on non-BGSM/BGEM and BGEM has
    ''' no such field, so both source and targets are gated to BGSM-backed materials.</summary>
    Private Sub MatchBodySkinSubsurfaceToFace(host As NpcRenderHost)
        If host Is Nothing Then host = _renderHost
        Dim model = host?.PreviewCtl?.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] skip: model/meshes Nothing")
            Return
        End If

        ' Source: the authoritative face material (FaceTint shader, BGSM-backed).
        Dim faceFound As Boolean = False
        Dim faceOn As Boolean = False
        Dim faceRolloff As Single = 0.0F
        For Each fm In model.meshes
            If fm Is Nothing OrElse fm.MeshData Is Nothing OrElse fm.MeshData.Material Is Nothing Then Continue For
            Dim fmb = fm.MeshData.Material.MaterialBase
            If fmb Is Nothing Then Continue For
            If fmb.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
            If Not (TypeOf fmb.Underlying_Material Is BGSM) Then Continue For
            faceOn = fmb.SubsurfaceLighting
            faceRolloff = fmb.SubsurfaceLightingRolloff
            faceFound = True
            Exit For
        Next
        If Not faceFound Then
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] skip: no FaceTint source material in scene")
            Return
        End If

        Dim faceOnLog = faceOn
        Dim faceRollLog = faceRolloff
        Logger.LogLazy(Function() $"[BODY-SUBSURFACE] face source on={faceOnLog} rolloff={faceRollLog:F4}")

        ' Targets: body skin materials (SkinTint flag, not the face), BGSM-backed. Same shape
        ' set TryApplyBodySkinSoftLight touches.
        Dim applied As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim mb = mesh.MeshData.Material.MaterialBase
            If mb Is Nothing Then Continue For
            If Not mb.SkinTint Then Continue For
            If mb.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
            If Not (TypeOf mb.Underlying_Material Is BGSM) Then Continue For

            Dim preOn = mb.SubsurfaceLighting
            Dim preRoll = mb.SubsurfaceLightingRolloff
            If preOn = faceOn AndAlso preRoll = faceRolloff Then Continue For

            mb.SubsurfaceLighting = faceOn
            mb.SubsurfaceLightingRolloff = faceRolloff
            applied += 1
            Dim snLog = mesh.MeshData.Shape?.ShapeName
            Dim preOnL = preOn
            Dim preRollL = preRoll
            Dim newOnL = faceOn
            Dim newRollL = faceRolloff
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] shape='{snLog}' {preOnL}/{preRollL:F4} → {newOnL}/{newRollL:F4} (from face)")
        Next

        Dim appliedLog = applied
        Logger.LogLazy(Function() $"[BODY-SUBSURFACE] done applied={appliedLog}")
    End Sub

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
    ''' <summary>Build the per-NPC face tint inputs (region swaps + ordered layer list)
    ''' from the NPC's parsed records and tint preset overlays. Pure data — no GL state,
    ''' no Model touch, no Textures_Dictionary access. Used by both the live render path
    ''' (TryApplyFaceTints) and the standalone bake path (FaceGenBuilder.BakeFaceTextures)
    ''' so they share one source of truth for layer composition + ordering.
    '''
    ''' Returns Nothing values inside the tuple's npcData/race when the inputs can't be
    ''' resolved; layers/regionSwaps are always non-Nothing (empty list when nothing applies).</summary>
    ''' <summary>The FormID to read FACE/BODY appearance (tint, chargen + face-bone morphs, MRSV,
    ''' skin-tone, FaceGen NIF) from: the resolved Traits source (inherited) when set, else the NPC's
    ''' own FormID. For a non-inheriting NPC this equals the root, so every read is byte-identical to
    ''' before — only template-inheriting NPCs change. Mirrors how HeadPartFormIDs/Hair already resolve
    ''' from the Traits source. Replaces the old ModelSourceFormID-or-root pattern, which always fell to
    ''' root because ModelSourceFormID was never wired in the render path.</summary>
    Private Shared Function FaceAppearanceSourceFormID(state As NPCVisualState) As UInteger
        If state Is Nothing Then Return 0UI
        Return If(state.TraitsSourceFormID <> 0UI, state.TraitsSourceFormID, state.FormID)
    End Function

    Friend Function BuildFaceTintLayerInputs(state As NPCVisualState) As (
        layers As List(Of FaceTintLayerInput),
        regionSwaps As List(Of FaceRegionSwapInput),
        npcData As NPC_Data,
        race As RACE_Data)

        Dim emptyResult = (
            layers:=New List(Of FaceTintLayerInput),
            regionSwaps:=New List(Of FaceRegionSwapInput),
            npcData:=CType(Nothing, NPC_Data),
            race:=CType(Nothing, RACE_Data))

        If state Is Nothing Then Return emptyResult

        Dim modelFormID = FaceAppearanceSourceFormID(state)
        ' Resolve the hair LUT path so slot Brows palette layers can drive their per-pixel
        ' grayscale-to-palette colour off the same LUT the hair/brow MESHES sample at render
        ' time. BGSM-first / RACE.HNAM fallback lives in ResolveHairPaletteTexture (single
        ' source of truth shared with the mesh-side ApplyMaterialPaletteHairColor).
        Dim hairLutPath As String = ResolveHairPaletteTexture(_renderHost, state, _pluginManager)
        ' Diagnostic: dump what the brow tint will use (LUT path + HCLF RemappingIndex) alongside
        ' what each loaded hair/grayscale MESH material uses (GreyscaleTexture + GrayscaleToPaletteScale),
        ' so the two can be compared 1:1 against the [PALSCALE-WRITE] mesh log. Confirms palette
        ' (LUT) + index (scale) parity between the brow face-tint and the brow MESH.
        If Logger.Enabled Then
            Dim browHcfid = state.HairColorFormID
            Dim browClfmDiag = ResolveColorFormData(browHcfid)
            Dim browRow As Single = If(browClfmDiag IsNot Nothing, browClfmDiag.RemappingIndex, -1.0F)
            Dim browHasRemap As Boolean = (browClfmDiag IsNot Nothing AndAlso browClfmDiag.HasRemappingIndex)
            Dim browHasColor As Boolean = (browClfmDiag IsNot Nothing AndAlso browClfmDiag.HasColor)
            Dim browLutKey = FO4UnifiedMaterial_Class.CorrectTexturePath(hairLutPath)
            Logger.LogLazy(Function() $"[BROW-LUT-RESOLVE] hairFid=0x{browHcfid:X8} hasColor={browHasColor} hasRemap={browHasRemap} row={browRow:F4} lutPath='{hairLutPath}' lutKey='{browLutKey}'")
            Dim model0 = _renderHost?.PreviewCtl?.Model
            If model0 IsNot Nothing AndAlso model0.meshes IsNot Nothing Then
                For Each mDiag In model0.meshes
                    If mDiag Is Nothing OrElse mDiag.MeshData Is Nothing OrElse mDiag.MeshData.Material Is Nothing Then Continue For
                    Dim mbDiag = mDiag.MeshData.Material.MaterialBase
                    If mbDiag Is Nothing Then Continue For
                    If Not (mbDiag.Hair OrElse mbDiag.GrayscaleToPaletteColor) Then Continue For
                    Dim shapeNm = If(mDiag.MeshData.Shape IsNot Nothing, mDiag.MeshData.Shape.ShapeName, "<?>")
                    Dim gtexDiag = If(mbDiag.GreyscaleTexture, "")
                    Dim gtexKeyDiag = FO4UnifiedMaterial_Class.CorrectTexturePath(gtexDiag)
                    Dim scaleDiag = mbDiag.GrayscaleToPaletteScale
                    Logger.LogLazy(Function() $"[BROW-MESH-LUT] shape='{shapeNm}' hair={mbDiag.Hair} g2p={mbDiag.GrayscaleToPaletteColor} scale={scaleDiag:F4} greyTex='{gtexDiag}' greyKey='{gtexKeyDiag}'")
                Next
            End If
        End If
        Dim built = FaceTintLayerBuilder.Build(
            modelFormID:=modelFormID,
            rootFormID:=state.RootNpcFormID,
            raceFormID:=state.RaceFormID,
            isFemale:=state.IsFemale,
            pluginManager:=_pluginManager,
            appliedPresets:=_appliedPresets,
            tintBytesCache:=_tintBytesCache,
            hairLutPath:=hairLutPath,
            hairColorFormID:=state.HairColorFormID,
            hasTextureLighting:=state.HasTextureLighting,
            textureLightingColorArgb:=state.TextureLightingColor.ToArgb(),
            parseRace:=AddressOf ParseRaceCached)

        Return (built.Layers, built.RegionSwaps, built.NpcData, built.Race)
    End Function


    ''' <summary>Live-render path: build the layer inputs (shared with the bake) and apply
    ''' them onto the model's face textures via the compositor. Mutates Textures_Dictionary
    ''' GL Texture_IDs in place — same semantics this function had before the
    ''' BuildFaceTintLayerInputs extraction.</summary>
    Private Function TryApplyFaceTints(state As NPCVisualState, Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _renderHost
        If state Is Nothing Then Return False

        Dim built = BuildFaceTintLayerInputs(state)
        If built.npcData Is Nothing Then Return True ' no NPC / no race / no tint layers
        Dim layerInputs = built.layers
        Dim regionSwaps = built.regionSwaps
        Dim npcData = built.npcData
        Dim race = built.race
        If layerInputs.Count = 0 Then Return True

        ' Find the face mesh in the model, get its diffuse texture cache entry, and call the
        ' compositor on a copy. Then mutate the cache entry's GL Texture_ID so the existing
        ' render path picks up the modified diffuse without any library changes.
        Dim model = host.PreviewCtl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Return True   ' no model — nothing we can do, don't retry forever
        End If

        Dim composedAny As Boolean = False
        Dim faceMeshFoundButTextureNotReady As Boolean = False
        Dim seenFaceMeshes As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        ' Diagnostic: when the FaceTint shader filter rejects every mesh (typical Ghoul/Child
        ' bug — the engine uses a different BSLightingShaderType for these races), enumerate
        ' every mesh's shape name + shader type so we can see what we DO have vs what we look
        ' for. Only emitted on the failure path below to keep the log compact.
        Dim shaderInventoryForDiag As New List(Of String)
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim shape = mesh.MeshData.Shape
            If shape Is Nothing Then Continue For

            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' The actual face shape uses the FaceTint shader (BSLightingShaderType). Other "head"
            ' shapes (BaseFemaleHeadRear with body texture, mouth, lashes, eyes) use SkinTint or
            ' EnvMap. Filtering by shader type avoids touching the headrear / mouth diffuses.
            If materialBase.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then
                shaderInventoryForDiag.Add($"shape='{shape.ShapeName}' shader={materialBase.NifShaderType}")
                Continue For
            End If

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seenFaceMeshes.Contains(diffusePath) Then Continue For
            seenFaceMeshes.Add(diffusePath)

            ' Diffuse must be ready before we attempt anything — it's the channel every layer
            ' contributes to and it's the one whose dimensions drive the FBO size. If diffuse
            ' isn't loaded, signal "retry later".
            Dim diffuseEntry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, diffuseEntry) _
               OrElse diffuseEntry Is Nothing OrElse Not diffuseEntry.Loaded OrElse diffuseEntry.Texture_ID = 0 Then
                faceMeshFoundButTextureNotReady = True
                Continue For
            End If

            Dim w = diffuseEntry.Size.Width
            Dim h = diffuseEntry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Continue For
            End If

            ' Resolve N + S entries from the dict; passing 0 to the pipeline for any channel
            ' whose texture isn't loaded just skips that channel (compositor returns IsFresh=False).
            Dim normalPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.NormalTexture)
            Dim specPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.SmoothSpecTexture)
            Dim normalEntry As PreviewModel.Texture_Loaded_Class = Nothing
            Dim specEntry As PreviewModel.Texture_Loaded_Class = Nothing
            model.Textures_Dictionary.TryGetValue(normalPath, normalEntry)
            model.Textures_Dictionary.TryGetValue(specPath, specEntry)
            Dim normalSrcId As Integer = If(normalEntry IsNot Nothing AndAlso normalEntry.Loaded, normalEntry.Texture_ID, 0)
            Dim specSrcId As Integer = If(specEntry IsNot Nothing AndAlso specEntry.Loaded, specEntry.Texture_ID, 0)

            ' Snapshot pristine bytes for live-edit rollback BEFORE the pipeline replaces IDs.
            ' The compositor calls GL.DeleteTexture on the previous fresh ID; without these
            ' snapshots a live tint edit can't roll back to a clean baseline (every refresh
            ' would compose on top of the previous bake).
            CapturePristineDiffusePixels(diffusePath, host)
            ' Normal/specular get pristine snapshots only when their entries are present in the
            ' dict (otherwise there's nothing to roll back from on those channels).

            ' Run the shared compositor pipeline (region-swap → tint compose). Single source
            ' of truth for both render and bake; this caller is responsible for the dict
            ' swap below (the bake instead reads back + encodes the result IDs).
            Dim pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                host.CompositorState, host.TintGpuCache,
                diffuseEntry.Texture_ID, normalSrcId, specSrcId,
                w, h, layerInputs, regionSwaps)

            ' Swap fresh IDs into the dict and delete the IDs they replaced. IsFresh=False
            ' means the channel had no contribution and the input ID stayed in place — no
            ' dict mutation, no delete.
            ApplyPipelineResultToDict(model, diffusePath, diffuseEntry, pipelineResult.Diffuse)
            If normalEntry IsNot Nothing Then ApplyPipelineResultToDict(model, normalPath, normalEntry, pipelineResult.Normal)
            If specEntry IsNot Nothing Then ApplyPipelineResultToDict(model, specPath, specEntry, pipelineResult.Specular)
            If pipelineResult.Diffuse.IsFresh OrElse pipelineResult.Normal.IsFresh OrElse pipelineResult.Specular.IsFresh Then
                composedAny = True
            End If

            ' materialBase.SkinTint stays ENABLED. The render shader's `albedo *= tintColor`
            ' uniform handles slot 12 SkinTone uniformly on both face and body meshes, using
            ' the same resolved colour. Skin tone is not a FaceTint layer in our pipeline.
        Next

        ' If we found a face mesh but its texture wasn't ready, signal "retry later".
        ' If we composed at least one, success. Otherwise nothing matched — give up (no retry).
        If composedAny Then Return True
        If faceMeshFoundButTextureNotReady Then Return False
        Return True
    End Function

    ''' <summary>Returns True iff the NPC has an active face tint layer at the race's SkinTone
    ''' slot — i.e. the layer that the engine's <c>characterCreation-&gt;skinTint</c> pointer
    ''' resolves to for this race. When such a layer exists, the face compositor processes it
    ''' as a normal Palette layer (with its authored BlendOp, typically SoftLight) and produces
    ''' <c>softlight(base, skinColor)</c> on the face diffuse, so the standalone QNAM SoftLight
    ''' fallback must skip to avoid double-applying.
    ''' <para>Detection: <c>opt.Slot == <see cref="TintSlot.SkinTone"/></c> with a non-trivial
    ''' slider value. The Slot enum is not a hardcoded magic number — it is the schema-defined
    ''' field name in <c>RACE.TintTemplateGroups[*].Options[*].Slot</c> (xEdit
    ''' wbDefinitionsFO4.pas:3478). Bethesda's authoring convention places the skin-tone
    ''' Palette template at this slot, and the engine's race-load resolves
    ''' <c>characterCreation-&gt;skinTint</c> to that exact template. From an offline parser's
    ''' perspective this slot is the canonical, structural anchor for the skin-tone layer —
    ''' verified against F4SE/ScaleformNatives.cpp:860-922 (GetSkinColor uses
    ''' <c>skinTemplate-&gt;templateIndex</c> to match NPC tint entries; that templateIndex
    ''' originated from the slot-12 option at race-load).</para>
    ''' <para>The TTEF 'Takes Skin Tone' flag (bit 2) is for SECONDARY layers (eye sockets,
    ''' lips) that get tinted by the already-resolved <c>npc-&gt;skinColor</c> — it is NOT the
    ''' dispatch for the base skin-tone layer itself, which is why AnneHargraves' 'Tono de piel'
    ''' has flags=0x0000 yet is correctly identified by Slot=SkinTone.</para></summary>
    Private Function NpcHasSkinToneLayer(npcData As NPC_Data, race As RACE_Data, isFemale As Boolean) As Boolean
        If npcData Is Nothing OrElse race Is Nothing Then Return False
        If npcData.FaceTintLayers Is Nothing OrElse npcData.FaceTintLayers.Count = 0 Then Return False
        For Each tl In npcData.FaceTintLayers
            Dim opt = race.FindTintOption(tl.Index, isFemale)
            If opt Is Nothing Then Continue For
            If opt.Slot <> CUShort(TintSlot.SkinTone) Then Continue For
            If tl.Value <= 0 Then Continue For
            Return True
        Next
        Return False
    End Function

    ''' <summary>Two-step skin tint — face FALLBACK side. Some NPCs (e.g. MayorMcDonough, Alice)
    ''' do NOT declare a face tint layer in slot 12 SkinTone. For them the face compositor never
    ''' runs the slot-12 Palette path, so the face misses the SoftLight that the body gets from
    ''' TryApplyBodySkinSoftLight against QNAM. The result without this fallback: face = base *
    ''' QNAM but body = softlight(base, qnam) * qnam — visibly mismatched.
    '''
    ''' This function applies the same one-shot SoftLight pre-pass to the face mesh diffuse
    ''' (using QNAM as the colour, same as body) so both meshes end up doing
    '''   final = softlight(base, qnam) * qnam
    ''' even when slot 12 is absent. Symmetric with the body path.
    '''
    ''' MUST be called BEFORE TryApplyFaceTints (see ApplyFaceTintOverlay) so this synthetic
    ''' slot-12 stand-in tones the BASE skin first and the detail tints (brow/scar/etc.) then
    ''' composite ON TOP of it instead of being washed out. (Earlier this ran last, which
    ''' whitened brow and scar layers -- the bug this ordering fixes.) Skipped silently when the
    ''' NPC already has a slot 12 layer (compositor sequences it in-rank), when QNAM is absent,
    ''' or when the face diffuse isn't in cache yet.
    '''
    ''' Pristine capture: this function calls CapturePristineDiffusePixels BEFORE the SoftLight
    ''' upload so RefreshFaceTintLivePreview can roll back to the untinted byte image on every
    ''' live edit. Without that capture the live refresh path has nothing to restore and each
    ''' edit re-applies softlight on top of the previous baked result, visibly brightening the
    ''' face on every slider tick (Alice repro). The capture happens on the very first call so
    ''' first-render and live-edit produce the same number of softlight passes (exactly one).</summary>
    Private Sub TryApplyFaceSkinSoftLight(state As NPCVisualState, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If state Is Nothing Then Return
        If Not state.HasTextureLighting Then
            Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] skip: HasTextureLighting=False")
            Return
        End If

        Dim modelFormID = FaceAppearanceSourceFormID(state)
        Dim npcData = ApplyPresetOverlayToNpcData(GetParsedNpc(modelFormID), state.RootNpcFormID)
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        Dim race As RACE_Data = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            race = ParseRaceCached(raceRec)
        End If

        ' Guard race-catalog: paralelo al de TryApplyBodySkinSoftLight. Si la raza NO declara
        ' slot SkinTone en su TintTemplateGroups, no aplica fallback de face softlight tampoco —
        ' synth/ghoul/robot no deberían recibirlo, sus shapes face no son SkinTint en sentido humano.
        If race Is Nothing OrElse race.FindTintOptionsBySlot(TintSlot.SkinTone, state.IsFemale).Count = 0 Then
            Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] skip: race has no SkinTone tint catalog")
            Return
        End If

        ' If the NPC has any active layer with TTEF 'Takes Skin Tone' (bit 2), the face compositor
        ' already softlight-modulated the diffuse via that layer's Palette path. Don't double-apply.
        If NpcHasSkinToneLayer(npcData, race, state.IsFemale) Then
            Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] skip: NPC has SkinTone layer (compositor already did softlight)")
            Return
        End If
        Dim qnamE = state.TextureLightingColor
        Dim qnamER = qnamE.R, qnamEG = qnamE.G, qnamEB = qnamE.B, qnamEA = qnamE.A
        Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] entry qnam=RGBA({qnamER},{qnamEG},{qnamEB},{qnamEA}) -> WILL run uniform softlight over whole face diffuse (washes brow/scar tints composited earlier)")

        Dim model = host.PreviewCtl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then Return

        Dim qnam = state.TextureLightingColor
        Dim qR As Single = CSng(qnam.R) / 255.0F
        Dim qG As Single = CSng(qnam.G) / 255.0F
        Dim qB As Single = CSng(qnam.B) / 255.0F
        ' Opacity unificada con compositor face: mix(prev, SoftLight(prev, full_color), opacity)
        ' donde opacity = qnam.A / 255 = tl.Value / 100 = intensidad authored del slot-12 SkinTone.
        ' El shader se encarga de la interpolación; NO atenuar el color toward neutral grey acá.
        Dim opacity As Single = CSng(qnam.A) / 255.0F
        If opacity <= 0.001F Then Return

        Const SoftLightOp As Integer = 3

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim affected As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' Only the face mesh — the body mesh is handled by TryApplyBodySkinSoftLight.
            If materialBase.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seen.Contains(diffusePath) Then Continue For
            seen.Add(diffusePath)

            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, entry) _
               OrElse entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
                Continue For
            End If

            Dim w = entry.Size.Width, h = entry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Continue For
            End If

            ' Snapshot pristine BEFORE the SoftLight upload destroys the original Texture_ID.
            ' This is the contract with RefreshFaceTintLivePreview: every diffuse that we are
            ' about to softlight here must exist in host.PristineDiffusePixels so the live edit
            ' path can roll back to it before re-applying. CapturePristineDiffusePixels is a
            ' no-op when the path was already captured (line 3937 ContainsKey early-out), so
            ' calling it on every render is cheap and guarantees idempotency for the slot-12
            ' NPCs that go through TryApplyFaceTints (the compositor captures separately) AND
            ' for the no-slot-12 NPCs that only reach softlight via this fallback.
            CapturePristineDiffusePixels(diffusePath, host)

            Dim newTexId = FaceTintCompositor.ApplyUniformBlendOntoFaceTexture(
                host.CompositorState, entry.Texture_ID, w, h, qR, qG, qB, SoftLightOp, opacity)
            If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
                Continue For
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newTexId
            Dim diffuseLog = diffusePath
            Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] applied diffuse='{diffuseLog}' oldTex={oldId} -> newTex={newTexId}")
            Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            affected += 1
        Next

        Dim affectedLog = affected
        Logger.LogLazy(Function() $"[FACE-SOFTLIGHT] done affected={affectedLog}")
    End Sub

    ''' <summary>Two-step skin tint experiment — body side. Applies a one-shot SoftLight pass
    ''' against the NPC's QNAM TextureLightingColor onto every body diffuse texture (any mesh
    ''' with material.SkinTint = True that isn't the face mesh). The render shader still
    ''' multiplies by material.SkinTintColor afterwards, so the body ends up doing
    '''   final = softlight(base, qnam) * qnam
    ''' which is symmetric with the face's
    '''   final = softlight(base, slot12) * slot12
    ''' (when slot 12 is composited as a Palette layer with SoftLight blend op and the render
    ''' tint stays enabled). NO material modification — material.SkinTint and SkinTintColor
    ''' stay exactly as set by the existing override pipeline.
    '''
    ''' Skipped silently when state has no QNAM, when the model has no body meshes, or when
    ''' their diffuse textures aren't in cache yet (the post-texture-upload hook reschedules in
    ''' that case — see RenderIntent.PostTextureUploadAction wiring at MainForm:1864).</summary>
    Private Sub TryApplyBodySkinSoftLight(state As NPCVisualState, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If state Is Nothing Then
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip: state=Nothing")
            Return
        End If
        If Not state.HasTextureLighting Then
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip: HasTextureLighting=False")
            Return
        End If

        ' Guard race-catalog: si la raza del actor NO declara ningún TintOption de slot SkinTone
        ' (TintSlot 12) en su MaleTintTemplateGroups / FemaleTintTemplateGroups, no debería existir
        ' tinta de piel para este actor — el engine vanilla no la ofrece. Aplica QNAM softlight
        ' a body skin de razas no-humanas (Synth, Feral Ghoul, Robot) generaba tinta espuria que
        ' divergía el color entre shapes SkinTint=True vs SkinTint=False del mismo NIF
        ' (caso 2026-05-18 SynthGen2Mech: Gen2MechNew:1 recibía softlight; G2Skin_LArm/Rleg/etc no
        ' por SkinTint=False). HumanRace tiene el slot en el catálogo aunque el NPC no liste
        ' tints explícitos en NPC.PNAM, así que humanos vanilla bare-NPCs siguen pasando el guard.
        Dim raceRec = If(state.RaceFormID <> 0UI, _pluginManager.GetRecord(state.RaceFormID), Nothing)
        Dim race As RACE_Data = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            race = ParseRaceCached(raceRec)
        End If
        If race Is Nothing OrElse race.FindTintOptionsBySlot(TintSlot.SkinTone, state.IsFemale).Count = 0 Then
            Dim raceEdid = If(race?.EditorID, "?")
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip: race '{raceEdid}' has no SkinTone tint catalog (non-skin-tone race)")
            Return
        End If

        Dim model = host.PreviewCtl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip: model/meshes Nothing")
            Return
        End If

        Dim qnam = state.TextureLightingColor
        Dim qnamLogR = qnam.R
        Dim qnamLogG = qnam.G
        Dim qnamLogB = qnam.B
        Dim qnamLogA = qnam.A
        Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] entry qnam=RGBA({qnamLogR},{qnamLogG},{qnamLogB},{qnamLogA})")

        ' QNAM is RGBA — the alpha channel is the body SoftLight opacity. Vanilla NPCs ship
        ' with alpha=1.0 (byte 255) by convention, synced to the slot-12 SkinTone tint layer's
        ' Value. When the editor lowers slot-12 %, ResolveNpcSkinToneColor packs the new %
        ' back here as alpha so face compositor and body SoftLight stay in lockstep.
        '
        ' Opacity unificada con compositor face: pasamos color FULL + opacity al shader, que hace
        ' mix(prev, SoftLight(prev, color), opacity). Misma fórmula que ComposeOntoFaceTexture
        ' (mix(prev, blended, coverage)) — face y body matchean para cualquier opacity intermedia,
        ' no solo en 0 y 1. La atenuación previa toward neutral grey está reemplazada por la
        ' interpolación post-blend dentro del shader.
        Dim opacity As Single = Math.Max(0.0F, Math.Min(1.0F, CSng(qnam.A) / 255.0F))
        If opacity <= 0.001F Then
            Dim oLog = opacity
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip: opacity={oLog:F3} too low (no-op)")
            Return
        End If

        Dim qR As Single = CSng(qnam.R) / 255.0F
        Dim qG As Single = CSng(qnam.G) / 255.0F
        Dim qB As Single = CSng(qnam.B) / 255.0F
        Dim qrLog = qR
        Dim qgLog = qG
        Dim qbLog = qB
        Dim opLog = opacity
        Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] full color q=({qrLog:F3},{qgLog:F3},{qbLog:F3}) opacity={opLog:F3}")

        ' BlendOp matches the face slot 12 path: SoftLight (Photoshop / W3C SVG) so the
        ' two meshes use the same compositing operation against the same colour.
        Const SoftLightOp As Integer = 3

        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim affected As Integer = 0
        Dim totalMeshes As Integer = 0
        Dim filtered_noSkinTint As Integer = 0
        Dim filtered_faceTint As Integer = 0
        Dim filtered_noPath As Integer = 0
        Dim filtered_dupePath As Integer = 0
        Dim filtered_notLoaded As Integer = 0
        For Each mesh In model.meshes
            totalMeshes += 1
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            Dim shapeName = mesh.MeshData.Shape?.ShapeName
            ' Body = SkinTint material that is NOT the face. Face has its own slot-12 path
            ' running through the FaceTint compositor; touching it again here would double
            ' the SoftLight on the diffuse.
            If Not materialBase.SkinTint Then
                filtered_noSkinTint += 1
                Dim snLog = shapeName
                Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip shape='{snLog}' reason=SkinTint=False")
                Continue For
            End If
            If materialBase.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then
                filtered_faceTint += 1
                Continue For
            End If

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then
                filtered_noPath += 1
                Continue For
            End If
            If seen.Contains(diffusePath) Then
                filtered_dupePath += 1
                Continue For
            End If
            seen.Add(diffusePath)

            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, entry) _
               OrElse entry Is Nothing OrElse Not entry.Loaded OrElse entry.Texture_ID = 0 Then
                filtered_notLoaded += 1
                Dim snLog2 = shapeName
                Dim dpLog = diffusePath
                Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] skip shape='{snLog2}' diffuse='{dpLog}' reason=not-loaded")
                Continue For
            End If

            Dim w = entry.Size.Width, h = entry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Continue For
            End If

            ' Snapshot pristine before SoftLight destroys the original Texture_ID.
            CapturePristineDiffusePixels(diffusePath, host)

            Dim preTexId = entry.Texture_ID
            ' Working space del body = el que el resolver da para SkinTone (slot 12, Palette, softlight).
            ' Single source of truth: si cambia la convencion SkinTone en FaceTintConvention, body y cara
            ' sincronizan solos. Hoy resuelve g22.
            Dim bodyConv = FaceTintConvention.ResolveConvention(
                isTextureSet:=False, slot:=12US, blendOp:=SoftLightOp,
                channel:=FaceTintChannel.Diffuse, useHairPalette:=False)
            Dim newTexId = FaceTintCompositor.ApplyUniformBlendOntoFaceTexture(
                host.CompositorState, entry.Texture_ID, w, h, qR, qG, qB, SoftLightOp, opacity,
                workingSpace:=CInt(bodyConv.WorkingSpace))
            If newTexId = 0 OrElse newTexId = entry.Texture_ID Then
                Dim snLog3 = shapeName
                Dim preLog = preTexId
                Dim newLog = newTexId
                Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] compose-fail shape='{snLog3}' preTex={preLog} newTex={newLog}")
                Continue For
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newTexId
            Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            affected += 1
            Dim snLog4 = shapeName
            Dim dpLog2 = diffusePath
            Dim oldLog = oldId
            Dim newLog2 = newTexId
            Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] applied shape='{snLog4}' diffuse='{dpLog2}' oldTex={oldLog} → newTex={newLog2}")
        Next

        Dim affectedLog = affected
        Dim totalLog = totalMeshes
        Logger.LogLazy(Function() $"[BODY-SOFTLIGHT] done affected={affectedLog} totalMeshes={totalLog} filtered_noSkinTint={filtered_noSkinTint} filtered_faceTint={filtered_faceTint} filtered_noPath={filtered_noPath} filtered_dupePath={filtered_dupePath} filtered_notLoaded={filtered_notLoaded}")
    End Sub

    ''' <summary>Apply one channel's pipeline result to the model's Textures_Dictionary: swap
    ''' the fresh GL texture ID into the cache entry and delete the ID it replaced. No-op when
    ''' the pipeline reported IsFresh=False (channel had no contribution; input ID stayed in
    ''' place).</summary>
    Private Sub ApplyPipelineResultToDict(model As PreviewModel,
                                          texPath As String,
                                          entry As PreviewModel.Texture_Loaded_Class,
                                          chResult As FaceTintCompositor.FaceTintPipelineChannelResult)
        If chResult Is Nothing OrElse Not chResult.IsFresh Then Return
        If entry Is Nothing OrElse model Is Nothing OrElse String.IsNullOrEmpty(texPath) Then Return
        Dim oldId = entry.Texture_ID
        If chResult.TextureId = 0 OrElse chResult.TextureId = oldId Then Return
        entry.Texture_ID = chResult.TextureId
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
    End Sub

    ' LoadTintLayerBytes* moved to FaceTintInputBuilder. Wrappers below preserve the
    ' existing private signatures used elsewhere in MainForm (and pass our shared
    ' _tintBytesCache so the per-process cache stays single-instance).
    Private Function LoadTintLayerBytes(rawPath As String) As Byte()
        Return FaceTintInputBuilder.LoadTintLayerBytes(rawPath, _tintBytesCache)
    End Function

    Private Function LoadTintLayerBytesAndKey(rawPath As String) As (Bytes As Byte(), Key As String)
        Return FaceTintInputBuilder.LoadTintLayerBytesAndKey(rawPath, _tintBytesCache)
    End Function

    Private Function LoadTintLayerBytesByKey(normalizedKey As String) As Byte()
        Return FaceTintInputBuilder.LoadTintLayerBytesByKey(normalizedKey, _tintBytesCache)
    End Function

    ''' <summary>Drop every cached face-tint byte buffer and decoded GL texture. Call this
    ''' when the FilesDictionary is rebuilt (BA2 mount/unmount, plugin reload) so a stale
    ''' BA2 read cannot leak into a new asset set.</summary>
    Private Sub ClearFaceTintCaches()
        _tintBytesCache.Clear()
        _renderHost.TintGpuCache.Clear()
        _renderHost.PristineDiffusePixels.Clear()
    End Sub

    ''' <summary>Decode-once snapshot: read the DDS bytes for <paramref name="diffusePath"/>,
    ''' run them through the native loader to get the level-0 RGBA8 pixel buffer, and stash
    ''' (pixels, width, height) in <see cref="_renderHost.PristineDiffusePixels"/>. No-op when a path is
    ''' already cached — the on-disk DDS doesn't change for the lifetime of an NPC.
    '''
    ''' Called from the per-path compositor entry points before the original Texture_ID gets
    ''' destroyed. The decode happens exactly once per path per NPC; every subsequent live
    ''' tint refresh just re-uploads the cached pixels without touching the DDS again.</summary>
    Private Sub CapturePristineDiffusePixels(diffusePath As String, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _renderHost
        If String.IsNullOrEmpty(diffusePath) Then Return
        If host.PristineDiffusePixels.ContainsKey(diffusePath) Then Return

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(diffusePath, loc) Then
            ' Negative cache so we don't keep retrying paths that don't resolve.
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        Dim ddsBytes As Byte() = Nothing
        Try
            ddsBytes = loc.GetBytes()
        Catch
        End Try
        If ddsBytes Is Nothing OrElse ddsBytes.Length = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        ' Decode through the native wrapper. ConvertForBitmap gives us the RGBA8 level-0
        ' pixels straight back (matching what CreateBitmapFromDDS uses internally) — that's
        ' exactly what we need for a fast TexImage2D upload. We reuse this rather than
        ' Loader.LoadTextures because we don't want to maintain GL format swizzles / mipmap
        ' chains; the live tint refresh only needs the level-0 RGBA8 pixels.
        Dim tex As DirectXTexWrapperCLI.TextureLoaded

        Try
            tex = DirectXTexWrapperCLI.Loader.ConvertForBitmap(ddsBytes)
        Catch ex As Exception
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End Try
        If tex Is Nothing OrElse Not tex.Loaded OrElse tex.Levels Is Nothing OrElse tex.Levels.Count = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        Dim lvl = tex.Levels(0)
        If lvl Is Nothing OrElse lvl.Data Is Nothing OrElse lvl.Data.Length = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If
        ' Copy the bytes off the native object (the wrapper recycles its own buffer); we want
        ' a managed array that lives independently of the wrapper's lifetime.
        Dim pixels(lvl.Data.Length - 1) As Byte
        Buffer.BlockCopy(lvl.Data, 0, pixels, 0, lvl.Data.Length)

        host.PristineDiffusePixels(diffusePath) = New NpcRenderHost.PristinePixels With {
            .Pixels = pixels,
            .Width = lvl.Width,
            .Height = lvl.Height,
            .DGXFormat_Original = tex.DxgiCodeOriginal,
            .DGXFormat_Final = tex.DxgiCodeFinal
        }

        ' Free the wrapper's per-level buffers ASAP — we have our own copy now.
        Try
            For Each l In tex.Levels
                l.Data = Nothing
            Next
            tex.Levels.Clear()
        Catch
        End Try
    End Sub

    ''' <summary>Recompute the effective SkinFormID for an NPC by re-applying the same overlay
    ''' precedence chain that <see cref="ApplyPresetOverlayToNpcData"/> uses: LM SkinTemplate
    ''' bundle wins, then NPC.WNAM SkinFormIDOverride (Some(0) → fall back to RACE.WNAM), else
    ''' the raw NPC.WNAM. Used by the fast-path so a combo edit lands on state.SkinFormID
    ''' without re-running the full ResolveNPCBaseState pipeline.
    ''' Returns the effective FormID (may be 0 if no resolution succeeds).</summary>
    Private Function RecomputeEffectiveSkinFormID(rootNpcFormID As UInteger, raceFormID As UInteger,
                                                   rawNpcFormID As UInteger) As UInteger
        Dim raw = GetParsedNpc(rawNpcFormID)
        Dim effective As UInteger = If(raw IsNot Nothing, raw.SkinFormID, 0UI)
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(rootNpcFormID, overlayPreset) AndAlso overlayPreset IsNot Nothing Then
            If overlayPreset.SkinFormIDOverride.HasValue Then
                effective = overlayPreset.SkinFormIDOverride.Value
            End If
            ' LM SkinTemplate ARMO wins (matches NpcRecordOverlay.ApplyPresetOverlayToNpcData order).
            If Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
                Dim tpl = ResolveLmSkinTemplate(overlayPreset.SkinTemplateId)
                If tpl IsNot Nothing AndAlso tpl.SkinArmoFormID <> 0UI Then
                    effective = tpl.SkinArmoFormID
                End If
            End If
        End If
        ' RACE.WNAM fallback: matches ApplyRaceFallbacks (state.SkinFormID = 0 → race.SkinFormID).
        If effective = 0UI AndAlso raceFormID <> 0UI Then
            Dim raceRec = _pluginManager.GetRecord(raceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                effective = ParseRaceCached(raceRec).SkinFormID
            End If
        End If
        Return effective
    End Function

    ''' <summary>Resolve the body skin's MeshCandidates from the host's current state. A skin
    ''' ARMO commonly emits multiple candidates (NakedTorso + NakedHands) — one per ARMA in the
    ''' addon group — so the fast-path needs ALL of them, not just the first. Builds the same
    ''' candidates <see cref="CollectArmoCandidates"/> would emit during a full render, so the
    ''' fast-path uses byte-identical TXST/MSWP resolution as the normal pipeline.
    ''' Returns empty list when state.SkinFormID is 0 or no candidates could be built.</summary>
    Private Function ResolveBodySkinCandidates(state As NPCVisualState) As List(Of MeshCandidate)
        Dim candidates As New List(Of MeshCandidate)
        If state Is Nothing OrElse state.SkinFormID = 0UI Then Return candidates
        Dim order As Integer = 0
        Dim warnings As New List(Of String)
        CollectArmoCandidates(state.SkinFormID, state, MeshCandidateKind.Skin, candidates, order, warnings)
        Return candidates
    End Function

    ''' <summary>Snapshot the (DictKey → shapes) map of the host's currently-loaded body-skin
    ''' shapes. Used by the fast-path to decide which shapes get which new candidate's TXST/MSWP
    ''' applied without walking <see cref="PreviewResolutionResult.Shapes"/> twice.</summary>
    Private Function GroupBodySkinShapesByMeshPath(renderData As PreviewResolutionResult) As Dictionary(Of String, List(Of IRenderableShape))
        Dim groups As New Dictionary(Of String, List(Of IRenderableShape))(StringComparer.OrdinalIgnoreCase)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return groups
        For Each shape In renderData.Shapes
            Dim cat As ShapeRenderCategory = ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat <> ShapeRenderCategory.BodySkin AndAlso cat <> ShapeRenderCategory.NakedHands Then Continue For
            Dim key As String = ""
            renderData.MeshDictKeys.TryGetValue(shape, key)
            If String.IsNullOrEmpty(key) Then Continue For
            Dim bucket As List(Of IRenderableShape) = Nothing
            If Not groups.TryGetValue(key, bucket) Then
                bucket = New List(Of IRenderableShape)
                groups(key) = bucket
            End If
            bucket.Add(shape)
        Next
        Return groups
    End Function

    ''' <summary>Fast-path for skin override changes (NPC.WNAM / LM SkinTemplate combos in
    ''' EditBody). When the new skin ARMO's mesh-path SET matches the currently-loaded one, we
    ''' re-resolve TXST + MSWP per candidate and call <see cref="ApplyShapeMaterialOverrides"/>
    ''' over the matching shapes — material fields mutate in place, no VBO regeneration. ~1ms
    ''' instead of ~50-100ms for a full reload.
    '''
    ''' A skin ARMO normally emits 2 ARMAs (NakedTorso + NakedHands) → 2 candidates with distinct
    ''' DictKeys. The fast-path matches them by DictKey: same SET of mesh paths in the new skin
    ''' as in the old one ⇒ apply each candidate to its corresponding shape group. Any DictKey
    ''' missing on either side ⇒ bail to the full reload (different geometry layout).
    '''
    ''' Returns False when the mesh path set differs or state/render data is incomplete. The
    ''' fast-path does NOT diverge from the normal render — it calls the same
    ''' CollectArmoCandidates + ApplyShapeMaterialOverrides helpers, so any change to those
    ''' automatically flows here too.</summary>
    Friend Function RefreshBodySkinLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _renderHost
        If host?.LastRenderedState Is Nothing OrElse host?.LastRenderData Is Nothing Then
            Return False
        End If

        ' If the active LM template carries head / headRear HDPT swaps, the fast-path can't
        ' reapply them because (a) we don't track HDPT shapes by PartType in LastRenderData,
        ' and (b) a HDPT swap may bring a different mesh path that requires geometry reload.
        ' Bail to the full reload so ResolveNPCBaseState picks up the bundle correctly.
        ' face TXST (state.HeadTextureFormID) is just a texture override — that COULD be
        ' fast-pathed, but skipping it together keeps the rule simple and consistent: any
        ' face-side LM bundle ⇒ full reload.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(host.LastRenderedState.RootNpcFormID, overlayPreset) AndAlso overlayPreset IsNot Nothing _
           AndAlso Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
            Dim tpl = ResolveLmSkinTemplate(overlayPreset.SkinTemplateId)
            If tpl IsNot Nothing Then
                Dim genderIdx As Integer = If(host.LastRenderedState.IsFemale, 1, 0)
                If tpl.HeadHdptFormID(genderIdx) <> 0UI _
                   OrElse tpl.HeadRearHdptFormID(genderIdx) <> 0UI _
                   OrElse tpl.FaceTxstFormID(genderIdx) <> 0UI Then
                    Return False
                End If
            End If
        End If

        ' Sync host state's SkinFormID with the overlay BEFORE resolving candidates. The host
        ' state was set up at the previous render; the overlay (where the combo writes) is the
        ' live source of truth. Without this the candidates resolve against the OLD skin.
        Dim modelFormID = FaceAppearanceSourceFormID(host.LastRenderedState)
        Dim oldSkinFid = host.LastRenderedState.SkinFormID
        host.LastRenderedState.SkinFormID = RecomputeEffectiveSkinFormID(
            host.LastRenderedState.RootNpcFormID, host.LastRenderedState.RaceFormID, modelFormID)
        Dim newSkinFid = host.LastRenderedState.SkinFormID

        Dim newCandidates = ResolveBodySkinCandidates(host.LastRenderedState)
        If newCandidates.Count = 0 Then
            Return False
        End If

        ' Group existing body-skin shapes by their mesh path. This is the "old" set — the shapes
        ' currently uploaded to the GL.
        Dim oldGroups = GroupBodySkinShapesByMeshPath(host.LastRenderData)
        Dim oldKeys = String.Join(",", oldGroups.Keys.OrderBy(Function(k) k))
        Dim newKeys = String.Join(",", newCandidates.Select(Function(c) c.DictKey).OrderBy(Function(k) k))

        ' Path SET must match exactly — same count, same DictKeys (case-insensitive). Otherwise
        ' the new skin has a different geometry layout (more/fewer ARMAs, or a different mesh
        ' path) and we can't safely re-apply materials over the old shapes.
        If newCandidates.Count <> oldGroups.Count Then
            Return False
        End If
        For Each cand In newCandidates
            If Not oldGroups.ContainsKey(cand.DictKey) Then
                Dim missing = cand.DictKey
                Return False
            End If
        Next

        ' Path sets match. Apply each new candidate's TXST/MSWP to its corresponding shape group.
        Dim totalShapes As Integer = 0
        For Each cand In newCandidates
            Dim shapesForPath = oldGroups(cand.DictKey)
            ApplyShapeMaterialOverrides(cand, host.LastRenderedState, shapesForPath)
            totalShapes += shapesForPath.Count
        Next

        ' Skin-tint substitution on OUTFIT shapes — outfit shapes with material.NifShaderType =
        ' SkinTint (escote, brazos expuestos, etc.) read their diffuse/normal/spec from the
        ' actor's body-skin TXST (race-specific). Without re-applying this here, an outfit
        ' rendered against the OLD skin still shows the OLD body diffuse on its skin patches
        ' even after the body shape itself updated.
        '
        ' The render normal does this inside ApplyShapeMaterialOverrides when candidate.Kind=Outfit
        ' (line ~7375), reading state.SkinFormID via ResolveActorSkinTextureSet. We can't re-call
        ' ApplyShapeMaterialOverrides on outfit candidates here (we don't have them cached in
        ' LastRenderData), so we replicate the per-shape texture sub directly.
        Dim outfitSkinTintShapes = ApplyOutfitSkinTintRefreshAfterBodySkinChange(host)

        ' Same idea for HeadPart shapes: HDPTs whose CK flag UsesBodyTexture=True (or whose CBBE
        ' override fix forced it True for non-Human-Female actors) read their diffuse from the
        ' body skin TXST. The fast-path must update those when state.SkinFormID changes too —
        ' otherwise a ghoul → human skin swap leaves the headRear with the old ghoul diffuse.
        Dim headPartBodyTexShapes = ApplyHeadPartBodyTextureRefreshAfterBodySkinChange(host)

        ' [TEST: fastpath-skin-softlight] Re-bake softlight + face tints after the skin swap.
        ' Original fastpath called RefreshRender (paint-only) and skipped TryApplyBodySkinSoftLight,
        ' so the new body diffuse rendered without the QNAM softlight that the full render bakes.
        ' Replicates the RefreshFaceTintLivePreview pattern: rollback every captured diffuse to
        ' pristine, then route through MarkDirty(Textures) + InvalidateRender so Process_Textures_GL
        ' picks up any new diffuse paths (Texture-only branch, async upload + PostTextureUploadAction
        ' hook fires when ready). Caso (1) mismo path → hook sync inmediato; caso (2) path nuevo →
        ' espera al upload y rebakea sobre la textura nueva.
        Dim model = host.PreviewCtl?.Model
        If model Is Nothing Then
            Return False
        End If
        If Not RestoreCapturedDiffusesToPristine(model, host) Then
            Return False
        End If

        Dim capturedState = host.LastRenderedState
        Dim capturedRenderData = host.LastRenderData
        Dim capturedHost = host
        Dim capturedRequestVersion = _previewRequestVersion
        host.PreviewCtl.Intent.PostTextureUploadAction = Sub(m)
                                                             If capturedHost Is Nothing OrElse capturedHost.IsDisposed Then Return
                                                             If capturedRequestVersion <> _previewRequestVersion Then Return
                                                             ApplyFaceTintOverlay(capturedState, capturedRenderData, capturedHost)
                                                         End Sub
        host.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Textures)
        host.PreviewCtl.InvalidateRender()
        Return True
    End Function

    ''' <summary>Re-apply the per-shape "outfit SkinTint texture sub" to all outfit shapes whose
    ''' material is SkinTint. Mirrors the inline block in <see cref="ApplyShapeMaterialOverrides"/>
    ''' (line ~7442) that runs during a full render but only for the outfit candidate currently
    ''' being processed. Here we run it on every outfit shape already in the model, because the
    ''' actor's body skin just changed and outfits of any category (Underarmor / Armor / Glove)
    ''' may have skin-exposed patches that need to follow.
    '''
    ''' Region is inferred from the shape's category (GloveOutfit → Hand, everything else → Body)
    ''' instead of from candidate.SlotMask, since we don't have outfit candidates cached.
    ''' Returns the shape count touched (for logging).</summary>
    Private Function ApplyOutfitSkinTintRefreshAfterBodySkinChange(host As NpcRenderHost) As Integer
        Dim count As Integer = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return 0

        ' Resolve body and hand TXSTs once (state.SkinFormID was already updated by the caller).
        Dim bodyTxst = ResolveActorSkinTextureSet(state, SkinRegion.Body)
        Dim handTxst = ResolveActorSkinTextureSet(state, SkinRegion.Hand)

        For Each shape In renderData.Shapes
            Dim cat As ShapeRenderCategory = ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            ' Only outfit categories — body-skin shapes (BodySkin/NakedHands) were handled by the
            ' Skin candidate pass above.
            If cat <> ShapeRenderCategory.Underarmor _
               AndAlso cat <> ShapeRenderCategory.ArmorOver _
               AndAlso cat <> ShapeRenderCategory.GloveOutfit _
               AndAlso cat <> ShapeRenderCategory.Headwear Then Continue For

            Dim relMat = shape.ShapeMaterial
            If relMat Is Nothing Then Continue For
            Dim mat = relMat.material
            If mat Is Nothing Then Continue For
            If mat.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.SkinTint Then Continue For

            Dim chosenTxst = If(cat = ShapeRenderCategory.GloveOutfit, handTxst, bodyTxst)
            If chosenTxst Is Nothing Then Continue For

            ' Same fragment as ApplyShapeMaterialOverrides body — only the diffuse/normal/spec
            ' get substituted; material params (specular, smoothness, etc.) stay from the NIF.
            If chosenTxst.MaterialPath <> "" Then
                Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(chosenTxst.MaterialPath, mat, shape.NifShape, shape.NifContent)
                If bgsmMaterial IsNot Nothing Then
                    If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then mat.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                    If bgsmMaterial.NormalTexture <> "" Then mat.NormalTexture = bgsmMaterial.NormalTexture
                    If bgsmMaterial.SmoothSpecTexture <> "" Then mat.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                End If
            End If
            ApplyTextureSetToMaterial(mat, chosenTxst)
            count += 1
        Next
        Return count
    End Function

    ''' <summary>Re-apply body-skin TXST to HeadPart shapes whose owning HDPT had
    ''' UsesBodyTexture=True (post CBBE fix). The full render's ResolveTextureSet
    ''' (line ~7741) feeds the actor's body TXST into these HeadParts; when state.SkinFormID
    ''' changes via the fast-path, those HeadParts must follow or they keep showing the OLD
    ''' body diffuse (e.g. ghoul NPC + WNAM swapped to human skin → headRear mesh stays with
    ''' the ghoul body texture). Returns the shape count touched.</summary>
    Private Function ApplyHeadPartBodyTextureRefreshAfterBodySkinChange(host As NpcRenderHost) As Integer
        Dim count As Integer = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return 0

        ' HeadParts always pull from the BODY region (not Hand) — by definition they're
        ' face/headRear/etc., never hands. ResolveActorSkinTextureSet uses the now-updated
        ' state.SkinFormID so this matches what a full render would produce.
        Dim bodyTxst = ResolveActorSkinTextureSet(state, SkinRegion.Body)
        If bodyTxst Is Nothing Then Return 0

        For Each shape In renderData.Shapes
            Dim cat As ShapeRenderCategory = ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat <> ShapeRenderCategory.HeadPart Then Continue For
            Dim usesBody As Boolean = False
            renderData.ShapeUsesBodyTexture.TryGetValue(shape, usesBody)
            If Not usesBody Then Continue For

            Dim relMat = shape.ShapeMaterial
            If relMat Is Nothing Then Continue For
            Dim mat = relMat.material
            If mat Is Nothing Then Continue For

            ' Same body-skin sub flow ApplyShapeMaterialOverrides uses: load BGSM (if MNAM
            ' present in TXST), copy texture slots only, then apply the rest of the TXST.
            ' Material params (specular, smoothness, subsurface) stay from the NIF.
            If bodyTxst.MaterialPath <> "" Then
                Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(bodyTxst.MaterialPath, mat, shape.NifShape, shape.NifContent)
                If bgsmMaterial IsNot Nothing Then
                    If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then mat.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                    If bgsmMaterial.NormalTexture <> "" Then mat.NormalTexture = bgsmMaterial.NormalTexture
                    If bgsmMaterial.SmoothSpecTexture <> "" Then mat.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                End If
            End If
            ApplyTextureSetToMaterial(mat, bodyTxst)
            count += 1
        Next
        Return count
    End Function

    ''' <summary>Live tint refresh path. Restores every captured diffuse to its untinted
    ''' baseline, re-runs the face tint compositor and the face/body skin SoftLight passes,
    ''' and refreshes the SkinTintColor / HairTintColor uniforms in place. No geometry reload.
    '''
    ''' Returns False if any pristine path failed to resolve (caller should fall back to a
    ''' full reload for correctness on this edit).</summary>
    Friend Function RefreshFaceTintLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Logger.LogLazy(Function() $"[LIVE-EDIT] RefreshFaceTintLivePreview ENTRY")
        If host Is Nothing Then host = _renderHost
        If host.LastRenderedState Is Nothing OrElse host.LastRenderData Is Nothing Then
            Logger.LogLazy(Function() $"[LIVE-EDIT] skip: LastRenderedState/Data Nothing")
            Return False
        End If
        Dim model = host.PreviewCtl?.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Logger.LogLazy(Function() $"[LIVE-EDIT] skip: model/meshes Nothing")
            Return False
        End If

        ' Stage 0: re-pull QNAM (and any other state field that overlay-mutates per edit)
        ' from the overlay preset. host.LastRenderedState was seeded once at NPC load (line
        ' 4106-4107 path) so it's stale after the user changes the combo. Without this sync,
        ' the rest of the function reads the OLD HairColorFormID — render shows previous hair
        ' color regardless of what the user picked.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(host.LastRenderedState.RootNpcFormID, overlayPreset) Then
            If overlayPreset.HairColorFormID <> 0UI Then
                host.LastRenderedState.HairColorFormID = overlayPreset.HairColorFormID
            End If
        End If

        ' Stage 1: roll every face/body diffuse cache entry back to its pristine bytes. Each
        ' entry's Texture_ID currently points to a tinted/softlighted bake; we re-decode the
        ' original bytes onto a fresh GL texture, swap it into the entry, and delete the stale
        ' baked one. After this, the next compositor + softlight passes will start from a
        ' clean baseline.
        If Not RestoreCapturedDiffusesToPristine(model, host) Then
            ' Some path lacked pristine bytes (FilesDictionary miss) — the live preview can't
            ' guarantee correctness without a full reload.
            Return False
        End If

        ' Stage 2a: TryApplyBodySkinSoftLight reads state.TextureLightingColor and SoftLights
        ' the body diffuse with that colour. ResolveNPCBaseState normally seeds it from the
        ' overlay's slot-12 SkinTone (line 4045-4048), but that runs only on a full reload —
        ' a live tint edit doesn't touch state. We have to push the freshly-resolved skin
        ' tone into state ourselves before calling the SoftLight pass, otherwise body would
        ' be tinted with the previous QNAM/SkinTone snapshot and face/body would diverge as
        ' the user moves the slot-12 colour combo.
        Dim freshSkinTone = ResolveNpcSkinToneColor(host.LastRenderedState)
        Dim hasValueLog = freshSkinTone.HasValue
        If freshSkinTone.HasValue Then
            Dim fsR = freshSkinTone.Value.R
            Dim fsG = freshSkinTone.Value.G
            Dim fsB = freshSkinTone.Value.B
            Dim fsA = freshSkinTone.Value.A
            Logger.LogLazy(Function() $"[LIVE-EDIT] Stage 2a fresh skinTone=RGBA({fsR},{fsG},{fsB},{fsA}) — pushing to state.TextureLightingColor")
            host.LastRenderedState.HasTextureLighting = True
            host.LastRenderedState.TextureLightingColor = freshSkinTone.Value
        Else
            Logger.LogLazy(Function() $"[LIVE-EDIT] Stage 2a freshSkinTone=Nothing — state.TextureLightingColor NOT updated")
        End If

        ' Stage 2b: re-run compositor + SoftLight passes (same chain ApplyFaceTintOverlay uses
        ' on first render). The compositor will read npcData.FaceTintLayers from the
        ' overlay-applied NPC_Data, so the freshly-edited preset is what gets baked.
        ApplyFaceTintOverlay(host.LastRenderedState, host.LastRenderData, host)

        ' Stage 3: refresh material uniforms (SkinTintColor + GrayscaleToPalette / HairTintColor)
        ' on every loaded shape. These were set at NIF-load time inside ApplyShapeMaterialOverrides
        ' and are invisible to MarkDirty(Textures); we mutate them in place.
        '
        ' Iterate renderData.Shapes (not model.meshes) so each shape can be looked up against
        ' renderData.ShapeCandidate — without candidate context the prior code path applied hair
        ' color to ANY palette-enabled material, leaking it into robot armor / face / body shapes.
        ' Shared helper ApplyMaterialPaletteHairColor enforces the engine rule (Hair/FacialHair/
        ' Brow HDPTs only) and is the same code path NIF-load uses now — no parallel copy to
        ' drift.
        '
        ' SkinTintColor refresh stays inline here using the simple SkinTone resolution (no
        ' candidate-aware override). NIF-load uses the richer ResolveSkinTintColor which factors
        ' in solidTintColor for face HeadParts — that asymmetry is a separate frontier (see
        ' project_palette_routing_pending.md). Not touched here to avoid changing render behavior
        ' for face shapes edited in live preview.
        Dim renderData = host.LastRenderData
        Dim skinTone = ResolveNpcSkinToneColor(host.LastRenderedState)
        For Each shape In renderData.Shapes
            If shape Is Nothing Then Continue For
            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing OrElse relatedMaterial.material Is Nothing Then Continue For
            Dim mat = relatedMaterial.material

            If mat.SkinTint AndAlso skinTone.HasValue Then
                mat.SkinTintColor = skinTone.Value
            End If

            Dim shapeCandidate As MeshCandidate = Nothing
            renderData.ShapeCandidate.TryGetValue(shape, shapeCandidate)
            ApplyMaterialPaletteHairColor(mat, shapeCandidate, host.LastRenderedState, Nothing)
        Next

        Return True
    End Function

    ''' <summary>Roll every pristine-cached diffuse back to its untinted baseline by uploading
    ''' the cached RGBA8 pixels to a fresh GL texture and installing it in the cache entry. The
    ''' DDS decode happened exactly once when we captured pristine; from then on every refresh
    ''' is just a 4MB texture upload (~1ms per face/body diffuse).
    '''
    ''' Returns False when a captured path's pristine pixels are missing — caller falls back
    ''' to full reload.</summary>
    Private Function RestoreCapturedDiffusesToPristine(model As PreviewModel, Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _renderHost
        Dim pristineCount = host.PristineDiffusePixels.Count
        Logger.LogLazy(Function() $"[ROLLBACK] entry pristineCount={pristineCount}")
        If host.PristineDiffusePixels.Count = 0 Then
            ' Nothing was ever composited — nothing to restore. The upcoming ApplyFaceTintOverlay
            ' will run for the first time and CapturePristineDiffusePixels will populate the
            ' cache as the compositor walks each path.
            Logger.LogLazy(Function() $"[ROLLBACK] skip: nothing captured yet")
            Return True
        End If

        For Each kv In host.PristineDiffusePixels
            Dim path = kv.Key
            Dim pristine = kv.Value
            If pristine Is Nothing OrElse pristine.Pixels Is Nothing OrElse pristine.Pixels.Length = 0 Then
                ' Negative cache hit — we tried to capture this path before and failed. Bail
                ' out so the caller can full-reload instead of silently leaving stale tints.
                Dim pathLog = path
                Logger.LogLazy(Function() $"[ROLLBACK] FAIL path='{pathLog}' reason=negative-cache (pristine bytes missing)")
                Return False
            End If
            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(path, entry) Then
                Dim pathLog2 = path
                Logger.LogLazy(Function() $"[ROLLBACK] skip path='{pathLog2}' reason=not-in-dict")
                Continue For
            End If
            If entry Is Nothing Then
                Dim pathLog3 = path
                Logger.LogLazy(Function() $"[ROLLBACK] skip path='{pathLog3}' reason=entry-Nothing")
                Continue For
            End If

            ' Allocate a fresh GL texture and upload the cached RGBA8 pixels straight into it.
            ' This is the single hot path on every slider tick — if you change anything here
            ' measure the slider responsiveness afterwards.
            Dim newId As Integer = 0
            Dim uploadOk As Boolean = False
            Try
                ' Drain any pre-existing GL error so the post-upload check below only
                ' reports failures attributable to THIS upload.
                Dim drainGuard As Integer = 0
                Do While OpenTK.Graphics.OpenGL4.GL.GetError() <> OpenTK.Graphics.OpenGL4.ErrorCode.NoError
                    drainGuard += 1
                    If drainGuard > 32 Then Exit Do
                Loop

                newId = OpenTK.Graphics.OpenGL4.GL.GenTexture()
                If newId = 0 Then
                Else
                    OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, newId)
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapS, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapT, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
                    Dim handle = System.Runtime.InteropServices.GCHandle.Alloc(pristine.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned)
                    Try
                        ' DirectXTexWrapperCLI.Loader.ConvertForBitmap (the source of pristine.Pixels)
                        ' produces GDI Format32bppArgb byte order, which is B,G,R,A in memory. Tell
                        ' OpenGL that with PixelFormat.Bgra; the driver swaps to RGBA on upload so the
                        ' internal representation is correct. Using PixelFormat.Rgba here gave a blue
                        ' body (the body diffuse came back with R and B swapped on every live refresh).
                        OpenTK.Graphics.OpenGL4.GL.TexImage2D(
                            OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0,
                            OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8,
                            pristine.Width, pristine.Height, 0,
                            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                            OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,
                            handle.AddrOfPinnedObject())
                    Finally
                        handle.Free()
                    End Try
                    OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)

                    ' Certify GL accepted the upload. A silent error here means the texture
                    ' is allocated but contents are undefined (driver typically zeros it =
                    ' solid black). Refuse to install it in the cache; caller will FullReload.
                    Dim postErr = OpenTK.Graphics.OpenGL4.GL.GetError()
                    If postErr <> OpenTK.Graphics.OpenGL4.ErrorCode.NoError Then
                    Else
                        uploadOk = True
                    End If
                End If
            Catch ex As Exception
            End Try

            If Not uploadOk Then
                If newId <> 0 Then
                    Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(newId) : Catch : End Try
                End If
                Dim pathLog4 = path
                Logger.LogLazy(Function() $"[ROLLBACK] FAIL path='{pathLog4}' reason=upload-failed")
                Return False
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newId
            entry.Size = New Size(pristine.Width, pristine.Height)
            entry.DGXFormat_Original = pristine.DGXFormat_Original
            entry.DGXFormat_Final = pristine.DGXFormat_Final
            entry.Loaded = True
            If oldId <> 0 AndAlso oldId <> newId Then
                Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            End If
            Dim pathLog5 = path
            Dim oldIdLog = oldId
            Dim newIdLog = newId
            Logger.LogLazy(Function() $"[ROLLBACK] restored path='{pathLog5}' oldTex={oldIdLog} → pristineTex={newIdLog}")
        Next
        Logger.LogLazy(Function() $"[ROLLBACK] done OK")
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

    ''' <summary>Per-resolve cache of LVLN picks. When the same LVLN is encountered multiple times
    ''' during a single NPC resolution (e.g. Traits and Model both use same LVLN), the same NPC
    ''' is returned. This is how FO4 works: one random pick per LVLN per spawn.</summary>
    <ThreadStatic> Private Shared _lvlnPickCache As Dictionary(Of UInteger, UInteger)

    ''' <summary>Resolve the NPC's base visual state (traits + model, without outfit expansion).</summary>
    ''' <param name="host">The render host this resolution feeds. Supplies the host-scoped outfit
    ''' preview override (Edit Outfit picker) so the preview never mutates the shared overlay. Pass the
    ''' host being rendered into (<c>_renderHost</c> for the main preview).</param>
    Private Function ResolveNPCBaseState(npc As NPC_Data, host As NpcRenderHost) As NPCVisualState
        ' Fresh LVLN pick cache for this resolution — ensures consistent picks across categories
        _lvlnPickCache = New Dictionary(Of UInteger, UInteger)()

        Dim warnings As New List(Of String)
        Dim traits = ResolveTraitsStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim inventory = ResolveInventoryStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim model = ResolveModelAnimationStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)

        If traits Is Nothing Then traits = CreateOwnTraitsState(npc)
        If inventory Is Nothing Then inventory = CreateOwnInventoryState(npc)
        If model Is Nothing Then model = CreateOwnModelAnimationState(npc)

        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM
        ' now sourced from `traits` (was `model`). OBTS combinations stay on `model`.
        Dim state As New NPCVisualState With {
            .FormID = npc.FormID,
            .RootNpcFormID = npc.FormID,
            .IsFemale = traits.IsFemale,
            .RaceFormID = traits.RaceFormID,
            .SkinFormID = traits.SkinFormID,
            .DefaultOutfitFormID = inventory.DefaultOutfitFormID,
            .SleepOutfitFormID = inventory.SleepOutfitFormID,
            .HeadTextureFormID = traits.HeadTextureFormID,
            .HairColorFormID = traits.HairColorFormID,
            .FacialHairColorFormID = traits.FacialHairColorFormID,
            .HasTextureLighting = traits.HasTextureLighting,
            .TextureLightingColor = traits.TextureLightingColor,
            .TraitsSourceFormID = traits.SourceFormID
        }

        state.HeadPartFormIDs.AddRange(traits.HeadPartFormIDs)
        state.ObjectTemplateOMODFormIDs.AddRange(model.ObjectTemplateOMODFormIDs)
        state.ObjectTemplateCombinations.AddRange(model.ObjectTemplateCombinations)
        state.HasObjectTemplate = model.HasObjectTemplate
        state.AttachParentSlotFormIDs.AddRange(model.AttachParentSlotFormIDs)
        ApplyRaceFallbacks(state, traits, _pluginManager, AddressOf ParseRaceCached)
        state.HeadPartFormIDs = state.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()

        ' Apply per-NPC LooksMenu overlay (if any) AFTER the template chain + race fallbacks ran.
        ' This is what makes the preset visible in the preview: HeadParts / HairColor / Weight in
        ' the state would otherwise come from the model/traits template source. The morph and tint
        ' overlays live in ApplyPresetOverlayToNpcData (consumed by BuildFaceMorphResolver and
        ' TryApplyFaceTints) — same mechanism, different access point.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(state.RootNpcFormID, overlayPreset) Then
            If overlayPreset.HeadPartFormIDs.Count > 0 Then
                state.HeadPartFormIDs = overlayPreset.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()
            End If
            If overlayPreset.HairColorFormID <> 0UI Then
                state.HairColorFormID = overlayPreset.HairColorFormID
            End If
            If overlayPreset.WeightThin.HasValue Then state.WeightThin = overlayPreset.WeightThin.Value
            If overlayPreset.WeightMuscular.HasValue Then state.WeightMuscular = overlayPreset.WeightMuscular.Value
            If overlayPreset.WeightFat.HasValue Then state.WeightFat = overlayPreset.WeightFat.Value

            ' Skin overrides — same precedence the NpcRecordOverlay shadow applies, but on the
            ' state level. ResolveTraitsStateFromNPC re-parses the raw NPC by FormID (chain walk)
            ' and never sees the overlay, so without this block ResolveActorSkinTextureSet ends
            ' up reading state.SkinFormID = raw NPC.WNAM and the body/hands skin doesn't change.
            '   1) NPC.WNAM record override: SkinFormIDOverride.HasValue → take that value
            '      (Some(0) intentionally clears, downstream ApplyRaceFallbacks already substituted
            '      RACE.WNAM on raw zero so we re-trigger the same fallback here).
            '   2) LM SkinTemplate (F4SE bundle) wins after — mirrors SkinInterface.cpp:316-320.
            '      Bundle's face TXST + head/headRear HDPT live in shadow.HeadTextureFormID /
            '      shadow.HeadPartFormIDs; those flow into the state via the model/traits chain
            '      already (HeadPartFormIDs were just overwritten above; HeadTextureFormID is set
            '      below if the LM template carries one).
            If overlayPreset.SkinFormIDOverride.HasValue Then
                state.SkinFormID = overlayPreset.SkinFormIDOverride.Value
                If state.SkinFormID = 0UI Then
                    Dim raceRec2 = _pluginManager.GetRecord(state.RaceFormID)
                    If raceRec2 IsNot Nothing AndAlso raceRec2.Header.Signature = "RACE" Then
                        state.SkinFormID = ParseRaceCached(raceRec2).SkinFormID
                    End If
                End If
            End If
            If Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
                Dim tpl = ResolveLmSkinTemplate(overlayPreset.SkinTemplateId)
                If tpl IsNot Nothing Then
                    If tpl.SkinArmoFormID <> 0UI Then state.SkinFormID = tpl.SkinArmoFormID
                    Dim genderIdx As Integer = If(state.IsFemale, 1, 0)
                    If tpl.FaceTxstFormID(genderIdx) <> 0UI Then
                        state.HeadTextureFormID = tpl.FaceTxstFormID(genderIdx)
                    End If
                    ' HDPT replacements — the helper reads each new HDPT's own PartType to
                    ' decide which slot to replace, engine-faithful per SkinInterface.cpp:292.
                    NpcRecordOverlay.ApplyLmHdptReplacementPublic(state.HeadPartFormIDs, tpl.HeadHdptFormID(genderIdx), _pluginManager)
                    NpcRecordOverlay.ApplyLmHdptReplacementPublic(state.HeadPartFormIDs, tpl.HeadRearHdptFormID(genderIdx), _pluginManager)
                End If
            End If

            ' Default outfit (NPC.DOFT) override — set by the Edit Outfit picker. Applied at the
            ' state level so BuildOutfitComboEntries (called right after ResolveNPCBaseState in
            ' LoadNPCOnDemandAsyncFromExisting) re-samples the chosen OTFT and the render consumes it.
            '   value <> 0 → OTFT override   ·   value = 0 → no outfit (naked)   ·   Nothing → preserve.
            If overlayPreset.DefaultOutfitFormIDOverride.HasValue Then
                state.DefaultOutfitFormID = overlayPreset.DefaultOutfitFormIDOverride.Value
            End If

            ' Body/face skin-tone parity. The face compositor consumes overlay tint layers via
            ' ApplyPresetOverlayToNpcData, so the face picks up the preset's skin tone. The body
            ' compositor (TryApplyBodySkinSoftLight) reads state.TextureLightingColor — which
            ' otherwise stays the original NPC's QNAM and produces a face/body tone mismatch.
            ' Derive an effective TextureLightingColor from the preset's slot 12 SkinTone tint
            ' (resolved via ResolveNpcSkinToneColor: same CLFM/TEND lookup the face compositor
            ' uses) so both meshes composite against the same colour. LooksMenu in-game gets
            ' parity for free because the engine reads from the actor's tint array, which the
            ' preset just rewrote — we have to re-derive it manually because QNAM is a vanilla
            ' record-level field that LooksMenu doesn't serialize.
            Dim presetSkin = ResolveNpcSkinToneColor(state)
            If presetSkin.HasValue Then
                state.HasTextureLighting = True
                state.TextureLightingColor = presetSkin.Value
            End If
        End If

        ' Out-of-band outfit preview (Edit Outfit picker) — applied LAST and scoped to the host being
        ' rendered into, so it NEVER touches the shared overlay (_appliedPresets): browsing outfits in
        ' the picker leaves the main render's committed state untouched. Inert on the main host
        ' (OutfitPreviewActive=False). Value: Nothing → raw record DOFT · 0 → naked · fid → OTFT/draft.
        If host IsNot Nothing AndAlso host.OutfitPreviewActive Then
            state.DefaultOutfitFormID = If(host.OutfitPreviewOverride, inventory.DefaultOutfitFormID)
        End If

        Return state
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

    ''' <summary>Build a face morph resolver for the given NPC visual state.
    ''' Uses MSDK/MSDV morph presets from Chargen.tri (via RACE mapping) and
    ''' FMRI/FMRS face bone transforms (applied via skeleton DeltaTransform).
    ''' Body weight morphs are NOT applied (vanilla uses hardcoded bone scaling, not TRI).</summary>
    Private Function BuildFaceMorphResolver(state As NPCVisualState, renderData As PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _renderHost
        If state Is Nothing Then Return Nothing

        ' Get the full NPC_Data for the model source (the NPC whose face we're rendering)
        Dim modelNpcFormID = FaceAppearanceSourceFormID(state)
        Dim npcData = ApplyPresetOverlayToNpcData(GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' No morph data at all? Skip
        If npcData.MorphValues.Count = 0 Then Return Nothing

        ' Get RACE morph definitions for mapping MSDK keys ? morph names
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = ParseRaceCached(raceRec)

        Dim morphValueDefs = race.MorphValues
        Dim morphPresetDefs = If(state.IsFemale, race.FemaleMorphPresets, race.MaleMorphPresets)
        Dim morphGroups = If(state.IsFemale, race.FemaleMorphGroups, race.MaleMorphGroups)


        ' Dump raw MSDK/MSDV table from this NPC (to see what keys+weights the record really has).
        ' Cross-reference each key against RACE.MSID (sliders) / MPPI (presets) / MPGS (group sliders)
        ' to show where each morph came from and why it's in the NPC.
        Dim sliderIndexSet As New HashSet(Of UInteger)
        If morphValueDefs IsNot Nothing Then
            For Each mv In morphValueDefs : sliderIndexSet.Add(mv.Index) : Next
        End If
        Dim presetIndexMap As New Dictionary(Of UInteger, String)
        If morphPresetDefs IsNot Nothing Then
            For Each mp In morphPresetDefs
                If Not presetIndexMap.ContainsKey(mp.Index) Then presetIndexMap(mp.Index) = mp.MorphName
            Next
        End If
        For Each kvp In npcData.MorphValues
            Dim key = kvp.Key
            Dim value = kvp.Value
            Dim classification As String

            Dim value1 As String = Nothing

            If sliderIndexSet.Contains(key) Then
                Dim mvDef = morphValueDefs.FirstOrDefault(Function(m) m.Index = key)
                classification = $"SLIDER (RACE.MSID) MSM0='{mvDef.MinName}' MSM1='{mvDef.MaxName}'"
            ElseIf presetIndexMap.TryGetValue(key, value1) Then
                classification = $"PRESET (RACE.MPPI) morphName='{value1}'"
            Else
                classification = "??? (not found in RACE MSID/MPPI for this gender)"
            End If
        Next

        ' Dump RACE morph structure for this gender: how many groups, and within each group how
        ' many presets and what morph name they point to. Shows whether the 4x DefaultFaceType0
        ' belongs to 4 distinct groups (as hypothesized) or something else.
        If morphGroups IsNot Nothing Then
            For Each g In morphGroups
                Dim presetSummary As New System.Text.StringBuilder()
                For k = 0 To g.Presets.Count - 1
                    If k > 0 Then presetSummary.Append(" | ")
                    Dim p = g.Presets(k)
                    presetSummary.Append($"MPPI=0x{p.Index:X8}[MPPN='{p.PresetName}']→MPPM='{p.MorphName}'")
                Next
                Dim slidersSummary As String = ""
                If g.SliderIndices IsNot Nothing AndAlso g.SliderIndices.Count > 0 Then
                    Dim sliderKeys = String.Join(",", g.SliderIndices.Select(Function(k) $"0x{k:X8}"))
                    slidersSummary = $" MPGS=[{sliderKeys}]"
                End If
            Next
        End If

        Return New NpcMorphResolver(
            npcData,
            morphValueDefs:=morphValueDefs,
            morphPresetDefs:=morphPresetDefs,
            meshDictKeys:=renderData.MeshDictKeys,
            shapeChargenTriPaths:=renderData.ShapeChargenTriPaths,
            shapeRaceMorphTriPaths:=renderData.ShapeRaceMorphTriPaths)
    End Function

    ''' <summary>Returns the effective BodySlide slider dict for an NPC: the overlay preset's
    ''' BodyMorphSliders if one is applied, otherwise an empty dict (vanilla NPCs have no record-
    ''' level BodyMorphs — F4SE-only field).</summary>
    Private Function GetEffectiveBodyMorphSliders(rootNpcFormID As UInteger) As Dictionary(Of String, Single)
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(rootNpcFormID, preset) AndAlso preset.BodyMorphSliders IsNot Nothing Then
            Return preset.BodyMorphSliders
        End If
        Return New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
    End Function

    ''' <summary>Build a BodySlide vertex morph resolver for the NPC's effective slider state.
    ''' Returns Nothing when CheckBoxBodyTri is unchecked, when no sliders are active, or when
    ''' there are no shapes — lets MultiMorphResolver short-circuit.
    ''' The CheckBoxBodyTri toggle gates the entire BodySlide vertex-morph layer (BODYTRI .tri
    ''' lookup + slider apply). Unchecked = render exactly as if the JSON had no BodyMorphs key
    ''' for this NPC.</summary>
    Private Function BuildBodyMorphResolver(state As NPCVisualState, renderData As PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _renderHost
        If state Is Nothing OrElse renderData Is Nothing Then Return Nothing
        If Not host.Toggles.BodyTri Then Return Nothing
        Dim sliders = GetEffectiveBodyMorphSliders(state.RootNpcFormID)
        If sliders Is Nothing OrElse sliders.Count = 0 Then Return Nothing
        Return New BodySlideMorphResolver(sliders, renderData.MeshDictKeys)
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
        If host.Toggles.ApplyVertexMorphs Then face = BuildFaceMorphResolver(state, renderData, host)
        Dim body = BuildBodyMorphResolver(state, renderData, host)
        ' Hair zap resolver: emite el/los canal(es) de zap para las shapes Hair {30,31} marcadas con
        ' ZapParts (Top/Long/Both) según el modelo complementario main/hairline. Gated por "Render
        ' headwear": OFF → no se engancha → la mesh se destapa en el próximo pase de morphs (igual que la
        ' oclusión de la mesh entera). Se incluye INDEPENDIENTE de los morphs face/body (un NPC con ambos
        ' morphs OFF igual debe zapear bajo gorra), y debe ser el ÚLTIMO delegate del composite así su
        ' canal de zap se agrega después de los canales de posición — el orden no afecta el resultado
        ' (zap = mask, position = vertex) pero mantiene el zap visible al final del plan.
        Dim hairTopZap = BuildHairTopZapResolver(renderData, host)

        ' Junta los delegates no-nulos. MultiMorphResolver filtra nulls, así que paso los tres.
        Dim delegates = New IMorphResolver() {face, body, hairTopZap}.Where(Function(r) r IsNot Nothing).ToArray()
        If delegates.Length = 0 Then Return Nothing
        If delegates.Length = 1 Then Return delegates(0)
        Return New MultiMorphResolver(delegates)
    End Function

    ''' <summary>Build the hair zap resolver from the per-shape ShapeZapHairParts map, gated on the
    ''' "Render headwear" toggle. Returns Nothing when headwear rendering is OFF (the zap must lift so the
    ''' mesh shows whole) or when no shape carries a non-None ZapParts. Also flips shape.ApplyZaps for the
    ''' flagged shapes so the renderer honours the VertexMask=-1 the resolver's zap channel sets.</summary>
    Private Function BuildHairTopZapResolver(renderData As PreviewResolutionResult, host As NpcRenderHost) As HairTopZapResolver
        If renderData Is Nothing Then Return Nothing
        Dim zapParts As New Dictionary(Of IRenderableShape, HairZapParts)()
        ' Render headwear OFF → no zap (la mesh se ve entera, igual que destapar el head part ocluido).
        If host IsNot Nothing AndAlso host.Toggles.RenderHeadwear Then
            For Each kv In renderData.ShapeZapHairParts
                If kv.Key IsNot Nothing AndAlso kv.Value <> HairZapParts.None Then zapParts(kv.Key) = kv.Value
            Next
        End If
        ' ApplyZaps por shape: ON sólo para las shapes que zapeamos ahora. Las demás OFF para que un
        ' toggle previo no deje el flag pegado (la mask se limpia sola en ApplyMorphPlan, pero el flag
        ' de la shape es persistente). Aplica a TODAS las shapes flageables, no sólo las activas.
        For Each kv In renderData.ShapeZapHairParts
            If kv.Key IsNot Nothing Then kv.Key.ApplyZaps = zapParts.ContainsKey(kv.Key)
        Next
        ' [HAIRZAP-DIAG] which shapes carry a non-None ZapParts in the render data, and which made it into
        ' the resolver's zap set (ApplyZaps). A hairline flagged at SelectWinningCandidates but missing
        ' here would mean its shape object diverged between LoadNifShapes and the resolver.
        If Logger.Enabled Then
            For Each kv In renderData.ShapeZapHairParts
                Dim shName = If(kv.Key Is Nothing, "<null>", If(kv.Key.ShapeName, "?"))
                Dim partsVal = kv.Value
                Dim inSet = kv.Key IsNot Nothing AndAlso zapParts.ContainsKey(kv.Key)
                Dim applyZapsVal = kv.Key IsNot Nothing AndAlso kv.Key.ApplyZaps
                Logger.LogLazy(Function() $"[HAIRZAP-DIAG] resolver shape='{shName}' ShapeZapParts={partsVal} inZapSet={inSet} ApplyZaps={applyZapsVal} renderHeadwear={If(host IsNot Nothing, host.Toggles.RenderHeadwear, False)}")
            Next
        End If
        If zapParts.Count = 0 Then Return Nothing
        Return New HairTopZapResolver(zapParts)
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

    ''' <summary>Cache of parsed FacialBoneRegions files per race/gender key (e.g. "HumanRace:female").</summary>
    Private Shared ReadOnly _facialBoneRegionsCache As New Dictionary(Of String, FacialBoneRegionsFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Load and parse the per-race HumanRaceFacialBoneRegions<Gender>.txt JSON file.
    ''' Returns Nothing if the file doesn't exist or can't be parsed.</summary>
    Friend Shared Function GetFacialBoneRegionsForRace(race As RACE_Data, isFemale As Boolean) As FacialBoneRegionsFile
        If race Is Nothing OrElse String.IsNullOrEmpty(race.EditorID) Then Return Nothing

        Dim genderKey = If(isFemale, "Female", "Male")
        Dim cacheKey = race.EditorID & ":" & genderKey

        Dim cached As FacialBoneRegionsFile = Nothing
        If _facialBoneRegionsCache.TryGetValue(cacheKey, cached) Then Return cached

        ' Build candidate paths. Use race.EditorID as the base name (HumanRace, GhoulRace, etc.)
        Dim dataPath = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dataPath, loc) Then
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End If

        Try
            Dim bytes = loc.GetBytes()
            ' Dump the raw JSON to a sibling file so we can see exactly what the engine reads
            ' (independent of our parser). Compares against xEdit hex IDs to catch any parser
            ' bug. Path: same directory as the log file, named per gender.
            If Logger.Enabled Then
                Try
                    Dim dumpPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"fbr_dump_{race.EditorID}_{genderKey}.txt")
                    IO.File.WriteAllBytes(dumpPath, bytes)
                Catch dumpEx As Exception
                End Try
            End If
            Dim parsed = FacialBoneRegionsFile.ParseFromBytes(bytes)
            _facialBoneRegionsCache(cacheKey) = parsed
            Return parsed
        Catch ex As Exception
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End Try
    End Function

    ''' <summary>Build a pose of face bone deltas from the NPC's FMRI/FMRS subrecords.
    ''' For each FMRI region, look up the region in the race's FacialBoneRegions JSON, then
    ''' for each bone in the region compute a per-axis delta by signed-lerping FMRS sliders
    ''' (clamped to [-1,+1]) across Minima/Default/Maxima, scaled by FMIN. Bone names are
    ''' prefixed with "skin_" to match SkeletonDictionary. Returns Nothing if no regions
    ''' file is found or no non-zero FMRS values contribute.</summary>
    ''' <summary>Thin instance wrapper over <see cref="FaceBonePoseBuilder.BuildFaceBoneTransforms"/>;
    ''' resolves the overlay-applied NPC + race + regions JSON from the state, then delegates the
    ''' FMRS math to the helper module. Real impl lives in the module so offline bake reuses it.</summary>
    Private Function BuildFaceBoneTransforms(state As NPCVisualState) As Poses_class
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = FaceAppearanceSourceFormID(state)
        Dim npcData = ApplyPresetOverlayToNpcData(GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = ParseRaceCached(raceRec)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return Nothing

        Return FaceBonePoseBuilder.BuildFaceBoneTransforms(npcData, regionsFile)
    End Function


    ''' <summary>Compute the per-axis DELTA from a FMRS-driven slider.
    ''' fmrsVal is the NPC's slider value for this axis (clamped to [-1,+1] by the engine).
    '''   fmrsVal = 0  ? 0      (no morph applied)
    '''   fmrsVal = +1 ? maxVal - defaultVal
    '''   fmrsVal = -1 ? minVal - defaultVal
    ''' Negative values map toward minima, positive toward maxima. Returns the DELTA from the
    ''' rest pose (default), NOT the lerped absolute value, so applying the result is just an
    ''' add/multiply on the bone's existing local transform.
    ''' Source: 3-point lerp Min?Default?Max inferred from CommonLibF4 BGSCharacterMorph layout
    ''' (region-level Transform default + per-bone TransformMinMax).</summary>
    Private Shared Function LerpFmrs(fmrsVal As Single, defaultVal As Single, minVal As Single, maxVal As Single) As Single
        Return FaceBonePoseBuilder.LerpFmrs(fmrsVal, defaultVal, minVal, maxVal)
    End Function

    ''' <summary>Resolve the NPC's MWGT weights and the RACE's per-bone weight scale data for
    ''' use by the skeleton resolver. Returns Nothing if the NPC has no MWGT or the RACE has
    ''' no bone data for the NPC's gender.</summary>
    Private Function ResolveBodyWeightData(state As NPCVisualState, renderData As PreviewResolutionResult) As (Wt As Single, Wm As Single, Wf As Single, GenderBlock As RACE_BoneDataGender, MrsvValues As List(Of Single), ArmaDeltas As Dictionary(Of String, System.Numerics.Vector3), NnamX As Single, NnamY As Single)
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = FaceAppearanceSourceFormID(state)
        Dim npcData = ApplyPresetOverlayToNpcData(GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' Use state.WeightX (resolved by ApplyRaceFallbacks) — these are post-sentinel-substitution
        ' floats. Reading npcData.WeightX directly here would propagate the Single.MaxValue sentinel
        ' for NPCs whose MWGT carries "Default" slots, which then explodes the body-weight bone
        ' scales to infinity downstream.
        Dim wt As Single = state.WeightThin
        Dim wm As Single = state.WeightMuscular
        Dim wf As Single = state.WeightFat
        Dim armaDeltas = renderData?.ArmaBoneScaleDeltas
        Dim hasMwgt = (wt + wm + wf) >= 0.001F
        Dim hasArmaDeltas = (armaDeltas IsNot Nothing AndAlso armaDeltas.Count > 0)
        If Not hasMwgt AndAlso Not hasArmaDeltas Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = ParseRaceCached(raceRec)

        ' Log the FaceGen clamps for reference. TBD whether they apply to body BSMS output
        ' or only to face slider*FMIN. Not applying any clamp formula without spec.

        ' Log NNAM raw (both genders) — "Neck Fat Adjustments Scale" per xEdit, 4 unknown bytes + X + Y.
        ' Hypothesis: the 4 bytes may be (thin, musc, fat, pad) weights. Interpretation pending.
        Dim fmtNNAM = Function(raw As Byte(), xv As Single, yv As Single) As String
                          If raw Is Nothing Then Return "none"
                          Return $"bytes=[{raw(0):X2} {raw(1):X2} {raw(2):X2} {raw(3):X2}] (dec={raw(0)},{raw(1)},{raw(2)},{raw(3)}) X={xv:F4} Y={yv:F4}"
                      End Function

        ' Gender-resolved NNAM ("Neck Fat Adjustments Scale" — xEdit wbDefinitionsFO4.pas:11639/11657).
        ' Consumed by BuildBodyWeightPose as HIPÓTESIS H1 (multiplicative neck-fat modifier).
        ' The 4-byte Unknown prefix is NOT read — HumanRace vanilla has it zero and no spec exists
        ' to decode it. If a race ships it non-zero we flag and proceed with H1 anyway (unchanged).
        Dim nnamX As Single = If(state.IsFemale, race.FemaleNeckNNAMX, race.MaleNeckNNAMX)
        Dim nnamY As Single = If(state.IsFemale, race.FemaleNeckNNAMY, race.MaleNeckNNAMY)
        Dim nnamRaw = If(state.IsFemale, race.FemaleNeckNNAMRaw, race.MaleNeckNNAMRaw)
        Logger.LogLazy(Function() $"[NNAM-DIAG] race={race.EditorID} gender={If(state.IsFemale, "F", "M")} MWGT(t={wt.ToString("F3", CultureInfo.InvariantCulture)},m={wm.ToString("F3", CultureInfo.InvariantCulture)},f={wf.ToString("F3", CultureInfo.InvariantCulture)}) NNAM_M={fmtNNAM(race.MaleNeckNNAMRaw, race.MaleNeckNNAMX, race.MaleNeckNNAMY)} NNAM_F={fmtNNAM(race.FemaleNeckNNAMRaw, race.FemaleNeckNNAMX, race.FemaleNeckNNAMY)}")

        Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
        For Each bd In race.BoneData
            If bd.Gender = targetGender Then
                ' Dump archetype values for diagnostic bones to verify what the record actually says.
                Dim diagBones As String() = {"LBreast_skin", "RBreast_skin", "LButtFat_skin", "RButtFat_skin",
                                              "Belly_skin", "UpperBelly_skin", "Chest_skin", "Chest_Rear_Skin",
                                              "LArm_ShoulderFat_skin", "LLeg_Calf_skin", "LLeg_Thigh_skin"}
                For Each diagBone In diagBones
                    Dim bbb = bd.Bones.FirstOrDefault(Function(x) x.BoneName.Equals(diagBone, StringComparison.OrdinalIgnoreCase))
                Next
                If bd.Bones.Count > 0 Then Return (wt, wm, wf, bd, npcData.BodyMorphRegionValues, armaDeltas, nnamX, nnamY)
                Exit For
            End If
        Next
        If hasArmaDeltas Then
            Return (wt, wm, wf, New RACE_BoneDataGender With {.Gender = targetGender}, npcData.BodyMorphRegionValues, armaDeltas, nnamX, nnamY)
        End If
        Return Nothing
    End Function

    ''' <summary>Walk the skeleton hierarchy from a bone upward to determine which MRSV body
    ''' morph region (0..4) the bone belongs to. Returns -1 if no known region ancestor found.
    ''' The mapping is based on matching ancestor bone names to the major skeleton "trunk" bones:
    '''   HEAD → 0 (Head)
    '''   Chest, SPINE2, Neck → 1 (Upper Torso)
    '''   Arm (anywhere in ancestor chain) → 2 (Arms)
    '''   SPINE1, Pelvis, Butt (anywhere in ancestor chain) → 3 (Lower Torso)
    '''   Leg (anywhere in ancestor chain) → 4 (Legs)
    ''' This is inferred from the skeleton hierarchy, not from any Bethesda data file.</summary>
    Private Shared Function ResolveMrsvRegion(bone As HierarchiBone_class) As Integer
        Dim cur = bone
        Dim depth = 0
        While cur IsNot Nothing AndAlso depth < 20
            Dim n = cur.BoneName
            If n IsNot Nothing Then
                Dim upper = n.ToUpperInvariant()
                If upper.Contains("LEG") OrElse upper.Contains("LLEG") OrElse upper.Contains("RLEG") Then Return 4
                If upper.Contains("ARM") OrElse upper.Contains("LARM") OrElse upper.Contains("RARM") Then Return 2
                If upper.Contains("BUTT") Then Return 3
                If upper = "HEAD" OrElse upper.StartsWith("HEAD") Then Return 0
                If upper = "SPINE1" OrElse upper = "SPINE1_OFFSET" Then Return 3
                If upper = "PELVIS" OrElse upper = "PELVIS_OFFSET" Then Return 3
                If upper = "SPINE2" OrElse upper = "SPINE2_OFFSET" Then Return 1
                If upper = "CHEST" OrElse upper = "CHEST_OFFSET" Then Return 1
                If upper = "NECK" OrElse upper = "NECK_OFFSET" Then Return 1
            End If
            cur = cur.Parent
            depth += 1
        End While
        Return -1
    End Function

    ''' <summary>Produce a pose with a single Root.Scale delta carrying the race height factor.
    ''' Empty / identity if raceHeight ≈ 1. The Scale propagates to every descendant bone via
    ''' Transform_Class.ComposeTransforms (NIF convention T·R·S with scale inheritance).</summary>
    Private Shared Function BuildRaceHeightPose(raceHeight As Single) As Poses_class
        Dim pose As New Poses_class With {
            .Name = "Race Height",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        If Math.Abs(raceHeight - 1.0F) > 0.0001F Then
            pose.Transforms("Root") = New PoseTransformData With {.Scale = raceHeight}
        End If
        Return pose
    End Function

    ''' <summary>Read race height (Male/Female Height from RACE.DATA) for the NPC's race. 1.0 if unknown.</summary>
    Private Function GetRaceHeight(state As NPCVisualState) As Single
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return 1.0F
        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 1.0F
        Dim race = ParseRaceCached(raceRec)
        Dim h = If(state.IsFemale, race.FemaleHeight, race.MaleHeight)
        If h <= 0 Then Return 1.0F
        Return h
    End Function

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
    ''' <summary>Build the merged pose. <paramref name="armaSculptOverride"/>: when supplied, REPLACES
    ''' the per-shape ARMA sculpt that would otherwise be resolved from renderData. Pass Nothing for
    ''' "no sculpt" (base pose used by shapes whose ARMA has no sculpt). Pass a specific dict (per ARMA)
    ''' when building the pose for a specific armor's skeleton clone — the per-skeleton-per-ARMA flow.</summary>
    ' [REFACTOR-NOTE] Stays in MainForm because: calls Private MainForm helpers GetRaceHeight,
    ' ResolveBodyWeightData and BuildFaceBoneTransforms — all three use _pluginManager,
    ' GetParsedNpc, ApplyPresetOverlayToNpcData and GetFacialBoneRegionsForRace (MainForm-private
    ' state and helpers). Per Phase 3 sub-phase D-3b rules: no callbacks, no deeper refactor —
    ' leave in MainForm until those helpers are also migrated.
    Private Function BuildMergedNpcPose(state As NPCVisualState, renderData As PreviewResolutionResult,
                                        faceMorphsEnabled As Boolean,
                                        bodyWeightEnabled As Boolean,
                                        skeleton As SkeletonInstance,
                                        Optional armaSculptOverride As Dictionary(Of String, System.Numerics.Vector3) = Nothing) As Poses_class
        Dim racePose = BuildRaceHeightPose(GetRaceHeight(state))

        Dim bwPose As Poses_class = Nothing
        Dim hasSculpt = (armaSculptOverride IsNot Nothing AndAlso armaSculptOverride.Count > 0)
        If bodyWeightEnabled OrElse hasSculpt Then
            Dim bwData = ResolveBodyWeightData(state, renderData)
            If bwData.GenderBlock IsNot Nothing Then
                ' ARMA sculpt override (if provided) is the per-skeleton-per-ARMA sculpt source.
                ' Sculpt formula hardcoded H3 multiplicative (closure plan P0 — A REVISAR).
                Dim sculpt = If(armaSculptOverride, New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase))
                ' Sclpt y BW son toggles independientes. weightLayersEnabled=bodyWeightEnabled
                ' gobierna las layers RACE.BSMS / NNAM / MRSV (1-3); la layer ARMA (4) se aplica
                ' siempre que haya deltas. BW=OFF + Sclpt=ON → sólo capa 4 (s = 1·(1+arma_d)).
                bwPose = BuildBodyWeightPose(bwData.Wt, bwData.Wm, bwData.Wf,
                                             bwData.GenderBlock, bwData.MrsvValues, sculpt,
                                             bwData.NnamX, bwData.NnamY,
                                             skeleton, bodyWeightEnabled)
            End If
        End If

        Dim fmrsPose As Poses_class = Nothing
        If faceMorphsEnabled Then
            fmrsPose = BuildFaceBoneTransforms(state)
        End If

        Return MergePoses(racePose, bwPose, fmrsPose)
    End Function

    ''' <summary>Field-level merge of multiple Poses_class into one. For each PoseTransformData field
    ''' (X/Y/Z/Pitch/Roll/Yaw/Scale/ScaleX/ScaleY/ScaleZ), non-identity values from later sources
    ''' overwrite earlier ones. If two sources both have non-identity on the same field → log a
    ''' [POSE-MERGE-OVERLAP] warning and use last-wins. The 3 pose sources (race/BW/FMRS) write to
    ''' disjoint field sets by design, so overlap should never fire — it's a canary for future
    ''' architectural regressions.</summary>
    Private Shared Function MergePoses(ParamArray sources As Poses_class()) As Poses_class
        Dim merged As New Poses_class With {
            .Name = "NPC Bone Transforms",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each src In sources
            If src Is Nothing Then Continue For
            For Each kv In src.Transforms
                Dim bone = kv.Key
                Dim newPose = kv.Value
                Dim existing As PoseTransformData = Nothing
                If merged.Transforms.TryGetValue(bone, existing) Then
                    Dim conflicts As New List(Of String)
                    If newPose.X <> 0 Then
                        If existing.X <> 0 Then conflicts.Add("X")
                        existing.X = newPose.X
                    End If
                    If newPose.Y <> 0 Then
                        If existing.Y <> 0 Then conflicts.Add("Y")
                        existing.Y = newPose.Y
                    End If
                    If newPose.Z <> 0 Then
                        If existing.Z <> 0 Then conflicts.Add("Z")
                        existing.Z = newPose.Z
                    End If
                    If newPose.Pitch <> 0 Then
                        If existing.Pitch <> 0 Then conflicts.Add("Pitch")
                        existing.Pitch = newPose.Pitch
                    End If
                    If newPose.Roll <> 0 Then
                        If existing.Roll <> 0 Then conflicts.Add("Roll")
                        existing.Roll = newPose.Roll
                    End If
                    If newPose.Yaw <> 0 Then
                        If existing.Yaw <> 0 Then conflicts.Add("Yaw")
                        existing.Yaw = newPose.Yaw
                    End If
                    If newPose.Scale <> 1 Then
                        If existing.Scale <> 1 Then conflicts.Add("Scale")
                        existing.Scale = newPose.Scale
                    End If
                    If newPose.ScaleX <> 1 Then
                        If existing.ScaleX <> 1 Then conflicts.Add("ScaleX")
                        existing.ScaleX = newPose.ScaleX
                    End If
                    If newPose.ScaleY <> 1 Then
                        If existing.ScaleY <> 1 Then conflicts.Add("ScaleY")
                        existing.ScaleY = newPose.ScaleY
                    End If
                    If newPose.ScaleZ <> 1 Then
                        If existing.ScaleZ <> 1 Then conflicts.Add("ScaleZ")
                        existing.ScaleZ = newPose.ScaleZ
                    End If
                    merged.Transforms(bone) = existing
                Else
                    merged.Transforms(bone) = newPose
                End If
            Next
        Next
        Return merged
    End Function

    ''' <summary>Build a pose of non-uniform bone-scale deltas from NPC MWGT + RACE BSMS
    ''' (weight scale layer) and NPC MRSV + RACE BSMS "Range" (region modifier layer).
    ''' Requires SkeletonDictionary populated (ResolveMrsvRegion walks bone.Parent chain).</summary>
    Private Shared Function BuildBodyWeightPose(wt As Single, wm As Single, wf As Single,
                                                 genderBlock As RACE_BoneDataGender,
                                                 mrsvValues As List(Of Single),
                                                 armaDeltas As Dictionary(Of String, System.Numerics.Vector3),
                                                 nnamX As Single, nnamY As Single,
                                                 skeleton As SkeletonInstance,
                                                 weightLayersEnabled As Boolean) As Poses_class
        ' Temp toggle 2026-05-17: disables Layer 2 NNAM entirely to A/B against CK reference.
        ' Hypothesis: Layer 1 BSMS alone matches CK for fat neck; NNAM multiplicative on top is the
        ' source of the over-thickening user reported. Flip back to False to restore production
        ' behavior once the discriminator is logged.
        Const DisableNnamLayer2 As Boolean = True
        Const Eps As Single = 0.001F
        Dim clampModel = _bodyWeightClampModel
        Dim pose As New Poses_class With {
                .Name = "MWGT Body Weight",
                .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
                .Transforms = New Dictionary(Of String, PoseTransformData)
            }
        Dim affected As Integer = 0
        Dim skippedNoSkel As Integer = 0
        Dim skippedNegligibleScale As Integer = 0
        Dim unmatched As New List(Of String)

        ' Diagnostic buffer: per-bone rows for the [BW-CLAMP-DIAG] summary at the end. Captures
        ' the Layer-1 raw weight scale (SyRaw/SzRaw), the Range Modifier bounds (Min/Max Y/Z),
        ' the final emitted scale and the MRSV/ARMA contributions — enough for the log to show
        ' whether the weight DELTA overshoots the Range clamp the parser documents
        ' (RecordParsers.vb:955-959), per bone.
        Dim diag As New List(Of (Name As String, HasWS As Boolean, HasRange As Boolean, SyRaw As Single, SzRaw As Single, MinY As Single, MaxY As Single, MinZ As Single, MaxZ As Single, Region As Integer, Slider As Single, SyFinal As Single, SzFinal As Single, ArmaDY As Single, ArmaDZ As Single, RestY As Single, RestZ As Single))

        ' Build the bone set as union(RACE.BoneData, ARMA.BoneScaleDeltas). ARMA may cover
        ' bones that RACE doesn't list for this gender (outfit-specific bones) — we still
        ' apply their delta on top of the identity RACE scale.
        Dim boneLookup As New Dictionary(Of String, RACE_BoneData)(StringComparer.OrdinalIgnoreCase)
        For Each b In genderBlock.Bones
            boneLookup(b.BoneName) = b
        Next
        Dim allBoneNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each b In genderBlock.Bones
            allBoneNames.Add(b.BoneName)
        Next
        If armaDeltas IsNot Nothing Then
            For Each kv In armaDeltas
                allBoneNames.Add(kv.Key)
            Next
        End If

        ' [NNAM-DIAG] dump candidate set: every bone with "Neck" in name, tagging _skin suffix
        ' and HasWeightScale. Lets the user see which bones the substring-match in Layer 2
        ' selects before any scale is applied.
        Dim neckCandidates = allBoneNames.
            Where(Function(n) n.Contains("Neck", StringComparison.OrdinalIgnoreCase)).
            OrderBy(Function(n) n, StringComparer.OrdinalIgnoreCase).
            ToList()
        Logger.LogLazy(Function() $"[NNAM-DIAG] BuildBodyWeightPose inputs: wt={wt.ToString("F3", CultureInfo.InvariantCulture)} wm={wm.ToString("F3", CultureInfo.InvariantCulture)} wf={wf.ToString("F3", CultureInfo.InvariantCulture)} nnamX={nnamX.ToString("F4", CultureInfo.InvariantCulture)} nnamY={nnamY.ToString("F4", CultureInfo.InvariantCulture)} weightLayersEnabled={weightLayersEnabled} neck-bone-count={neckCandidates.Count}")
        For Each nc In neckCandidates
            Dim cb As RACE_BoneData = Nothing
            Dim hasWS As Boolean = False
            If boneLookup.TryGetValue(nc, cb) Then hasWS = cb.HasWeightScale
            Logger.LogLazy(Function() $"[NNAM-DIAG]   candidate bone='{nc}' isSkin={nc.EndsWith("_skin", StringComparison.OrdinalIgnoreCase)} inRaceBoneData={cb IsNot Nothing} HasWeightScale={hasWS}")
        Next

        For Each boneName In allBoneNames
            Dim skelBone As HierarchiBone_class = Nothing
            Dim restY As Single = 0.0F, restZ As Single = 0.0F
            If skeleton.SkeletonDictionary.TryGetValue(boneName, skelBone) Then
                If skelBone.OriginalLocaLTransform IsNot Nothing Then
                    restY = skelBone.OriginalLocaLTransform.Translation.Y
                    restZ = skelBone.OriginalLocaLTransform.Translation.Z
                End If
            Else
                skippedNoSkel += 1
                If unmatched.Count < 20 Then unmatched.Add(boneName)
                Continue For
            End If

            ' Diagnostic 2026-04-26: dump bind rotation for bones with ARMA delta to determine
            ' if frame-of-application could be the issue. (Análisis general de pre vs post bind
            ' usa el RBIND-DUMP en MainForm post PrepareSkeleton, que es más amplio.)
            If armaDeltas IsNot Nothing AndAlso armaDeltas.ContainsKey(boneName) Then
                Dim r = skelBone.OriginalLocaLTransform.Rotation
                Dim isIdentity = Math.Abs(r.M11 - 1.0F) < 0.001F AndAlso Math.Abs(r.M22 - 1.0F) < 0.001F AndAlso
                                 Math.Abs(r.M33 - 1.0F) < 0.001F AndAlso Math.Abs(r.M12) < 0.001F AndAlso
                                 Math.Abs(r.M13) < 0.001F AndAlso Math.Abs(r.M21) < 0.001F AndAlso
                                 Math.Abs(r.M23) < 0.001F AndAlso Math.Abs(r.M31) < 0.001F AndAlso
                                 Math.Abs(r.M32) < 0.001F
            End If

            ' Per-layer detailed logging (added 2026-04-26 for Fase 2 body-morph audit).
            ' Captures snapshots after each layer + computes the three ARMA hypotheses in
            ' parallel without recompiling, so the user can A/B them against in-game screenshots.
            ' All logs use InvariantCulture so float decimals use '.' regardless of OS locale.
            Dim bone As RACE_BoneData = Nothing
            boneLookup.TryGetValue(boneName, bone)

            ' --- Layer 0: identity ---
            Dim sx As Single = 1.0F, sy As Single = 1.0F, sz As Single = 1.0F

            ' --- Layer 1: RACE.BSMS WeightScale (3 archetype interpolation) ---
            ' RACE.BSMS WeightScale = 9 floats = 3 × Vec3 (Thin, Musc, Fat) × (X, Y, Z).
            ' Parser reads all 9 (RecordParsers.vb:1216-1226). Previously only Y/Z were consumed
            ' here; X was silently discarded. Fixed 2026-04-19 per audit — ignored X caused the
            ' systematic X-dominant residual vs CK FaceGen bake at shared neck bones.
            If weightLayersEnabled AndAlso bone IsNot Nothing AndAlso bone.HasWeightScale Then
                sx = bone.ThinX * wt + bone.MuscularX * wm + bone.FatX * wf
                sy = bone.ThinY * wt + bone.MuscularY * wm + bone.FatY * wf
                sz = bone.ThinZ * wt + bone.MuscularZ * wm + bone.FatZ * wf
            End If
            Dim sxR As Single = sx, syR As Single = sy, szR As Single = sz   ' snapshot post-RACE

            ' --- Clamp model (diagnostic): clamp the WEIGHT delta to the Range Modifier [Min,Max]
            ' BEFORE MRSV (Y/Z only — Range has no X). syR keeps the raw value for [BW-CLAMP-DIAG].
            If (clampModel = BodyWeightClampModel.ClampWeightL1 OrElse clampModel = BodyWeightClampModel.ClampBoth) _
               AndAlso bone IsNot Nothing AndAlso bone.HasRangeModifier Then
                sy = Math.Min(Math.Max(sy, 1.0F + bone.MinY), 1.0F + bone.MaxY)
                sz = Math.Min(Math.Max(sz, 1.0F + bone.MinZ), 1.0F + bone.MaxZ)
            End If

            ' --- Layer 2: NNAM (multiplicative neck-fat adjust) — H-NNAM-1 ---
            ' NNAM ("Neck Fat Adjustments Scale" — RACE.NNAM inside the head block, xEdit spec
            ' wbDefinitionsFO4.pas:11639/11657). HIPÓTESIS H1 2026-04-19: multiplicative neck-fat
            ' modifier on RACE-declared weight-scale bones whose name contains "Neck"; driven
            ' only by MWGT.Fat (matches the record's literal name "Neck Fat"). "Budget realloc"
            ' model falsified 2026-04-19 (see npc_manager_closure_plan P2). The 4-byte Unknown
            ' prefix is ignored: HumanRace vanilla has it zero, semantics unresolved. Reaches the
            ' head-mesh neck verts via bones shared between head and body skin (Neck_skin,
            ' Neck_Low_skin, Neck1_skin). Validate with harness [BW-only] RMS Científica < 0.10
            ' before promoting out of hypothesis.
            Dim isNeckCandidate As Boolean = boneName.Contains("Neck", StringComparison.OrdinalIgnoreCase)
            Dim nnamApplied As Boolean = False
            If weightLayersEnabled AndAlso bone IsNot Nothing AndAlso bone.HasWeightScale _
               AndAlso isNeckCandidate _
               AndAlso (Math.Abs(nnamX) > Single.Epsilon OrElse Math.Abs(nnamY) > Single.Epsilon) Then
                Dim mulX As Single = (1.0F + nnamX * wf)
                Dim mulY As Single = (1.0F + nnamY * wf)
                Dim sxBefore As Single = sx, syBefore As Single = sy
                If Not DisableNnamLayer2 Then
                    sx *= mulX
                    sy *= mulY
                    nnamApplied = True
                End If
                Logger.LogLazy(Function() $"[NNAM-DIAG] {If(DisableNnamLayer2, "WOULD-APPLY", "APPLY")} bone='{boneName}' isSkin={boneName.EndsWith("_skin", StringComparison.OrdinalIgnoreCase)} preLayer1(sx={sxR.ToString("F4", CultureInfo.InvariantCulture)},sy={syR.ToString("F4", CultureInfo.InvariantCulture)},sz={szR.ToString("F4", CultureInfo.InvariantCulture)}) preLayer2(sx={sxBefore.ToString("F4", CultureInfo.InvariantCulture)},sy={syBefore.ToString("F4", CultureInfo.InvariantCulture)}) mul(x={mulX.ToString("F4", CultureInfo.InvariantCulture)},y={mulY.ToString("F4", CultureInfo.InvariantCulture)}) post(sx={sx.ToString("F4", CultureInfo.InvariantCulture)},sy={sy.ToString("F4", CultureInfo.InvariantCulture)},sz={sz.ToString("F4", CultureInfo.InvariantCulture)})")
            ElseIf isNeckCandidate Then
                Dim reason As String =
                    If(Not weightLayersEnabled, "weightLayers=off",
                    If(bone Is Nothing, "no-race-bone-data",
                    If(Not bone.HasWeightScale, "no-WeightScale",
                    If(Math.Abs(nnamX) < Single.Epsilon AndAlso Math.Abs(nnamY) < Single.Epsilon, "nnam-zero", "unknown"))))
                Logger.LogLazy(Function() $"[NNAM-DIAG] SKIP  bone='{boneName}' isSkin={boneName.EndsWith("_skin", StringComparison.OrdinalIgnoreCase)} reason={reason}")
            End If
            Dim sxN As Single = sx, syN As Single = sy, szN As Single = sz   ' snapshot post-NNAM

            ' --- Layer 3: MRSV (Range Modifier) — interpretación H-MRSV-2 (canal interpolado) ---
            ' BSMS RangeModifier spec has only Min/Max Y and Z (no X) — per
            ' wbDefinitionsFO4.pas:5929. MRSV does NOT contribute to X.
            ' Hipótesis alternativa H-MRSV-1 (clamp puro) NO implementada — discriminar via
            ' screenshot in-game con NPC que tenga MWGT con sy_raw > 1+MaxY (RACE pide más que MaxY).
            Dim region As Integer = -1
            Dim slider As Single = 0.0F
            Dim mrsvApplied As Boolean = False
            If weightLayersEnabled AndAlso bone IsNot Nothing AndAlso bone.HasRangeModifier AndAlso mrsvValues IsNot Nothing AndAlso mrsvValues.Count >= 5 Then
                region = ResolveMrsvRegion(skelBone)
                If region >= 0 AndAlso region < mrsvValues.Count Then
                    slider = mrsvValues(region)
                    If slider >= 0 Then
                        sy += slider * bone.MaxY
                        sz += slider * bone.MaxZ
                    Else
                        sy += (-slider) * bone.MinY
                        sz += (-slider) * bone.MinZ
                    End If
                    mrsvApplied = True
                End If
            End If

            ' --- Clamp model (diagnostic): clamp the TOTAL weight+MRSV delta to [Min,Max] (Y/Z)
            ' AFTER MRSV, BEFORE ARMA. ARMA sculpt (Layer 4) then multiplies the clamped value.
            If (clampModel = BodyWeightClampModel.ClampFinal OrElse clampModel = BodyWeightClampModel.ClampBoth) _
               AndAlso bone IsNot Nothing AndAlso bone.HasRangeModifier Then
                sy = Math.Min(Math.Max(sy, 1.0F + bone.MinY), 1.0F + bone.MaxY)
                sz = Math.Min(Math.Max(sz, 1.0F + bone.MinZ), 1.0F + bone.MaxZ)
            End If
            Dim sxM As Single = sx, syM As Single = sy, szM As Single = sz   ' snapshot post-MRSV (= input a ARMA)

            ' --- Layer 4: ARMA Bone Scale Delta — H3 multiplicative HARDCODED (A REVISAR) ---
            ' Fórmula: s = race_s · (1 + arma_d). Aplicada componente a componente.
            ' Conceptualmente la más limpia (cumple las 3 invariantes naturales) pero NO
            ' confirmada experimentalmente vs CK ground truth. Closure plan P0.
            ' 17 fórmulas alternativas + 2 swap conventions + worldFrame opt-in se probaron
            ' 2026-04-29 vs Gunner — ninguna resolvió el clip motivador, que terminó siendo
            ' por OMODs no renderizados. Dropdown experimental eliminado tras esa sesión.
            Dim armaDX As Single = 0.0F, armaDY As Single = 0.0F, armaDZ As Single = 0.0F
            If armaDeltas IsNot Nothing Then
                Dim d As System.Numerics.Vector3
                If armaDeltas.TryGetValue(boneName, d) Then
                    armaDX = d.X
                    armaDY = d.Y
                    armaDZ = d.Z
                    sx = sxM * (1.0F + armaDX)
                    sy = syM * (1.0F + armaDY)
                    sz = szM * (1.0F + armaDZ)
                End If
            End If
            Dim sxA As Single = sx, syA As Single = sy, szA As Single = sz   ' snapshot post-ARMA (final)


            If Math.Abs(sx - 1.0F) < Eps AndAlso Math.Abs(sy - 1.0F) < Eps AndAlso Math.Abs(sz - 1.0F) < Eps Then
                skippedNegligibleScale += 1
                Continue For
            End If

            diag.Add((boneName,
                      If(bone IsNot Nothing, bone.HasWeightScale, False),
                      If(bone IsNot Nothing, bone.HasRangeModifier, False),
                      syR, szR,
                      If(bone IsNot Nothing, bone.MinY, 0.0F),
                      If(bone IsNot Nothing, bone.MaxY, 0.0F),
                      If(bone IsNot Nothing, bone.MinZ, 0.0F),
                      If(bone IsNot Nothing, bone.MaxZ, 0.0F),
                      region, slider, sy, sz, armaDY, armaDZ, restY, restZ))

            pose.Transforms(boneName) = New PoseTransformData With {
                    .ScaleX = sx,
                    .ScaleY = sy,
                    .ScaleZ = sz
                }
            affected += 1
        Next

        ' Body-weight summary + per-bone clamp diagnostic. Log-only: the String.Join, OrderBy and
        ' per-row formatting are all dedicated to logging, so the whole block is guarded by
        ' Logger.Enabled (logging convention). [BW-CLAMP-DIAG] shows, per affected bone, the
        ' Layer-1 raw weight scale vs the Range Modifier bounds the parser documents as a CLAMP on
        ' the weight DELTA (RecordParsers.vb:955-959): ifClampedWeight = 1 + clamp(raw-1,Min,Max),
        ' and weightExcess = (raw-1) - clamp(...) = how far the raw weight delta overshoots the
        ' Range. Positive weightExcess on Leg/Thigh/Calf bones is the suspected cause of legs
        ' reading fatter than CK. mrsv/arma columns attribute any extra contribution; NOT a clamp.
        If Logger.Enabled Then
            Dim mrsvStr = If(mrsvValues Is Nothing OrElse mrsvValues.Count = 0,
                                 "null/empty",
                                 String.Join(",", mrsvValues.Select(Function(v) v.ToString("F3", CultureInfo.InvariantCulture))))
            Dim mrsvStrLog = mrsvStr
            Dim armaCountLog = If(armaDeltas Is Nothing, 0, armaDeltas.Count)
            Dim affectedLog = affected
            Dim skelLog = skippedNoSkel
            Dim negLog = skippedNegligibleScale
            Dim wtLog = wt, wmLog = wm, wfLog = wf
            Logger.LogLazy(Function() $"[BW-CLAMP-DIAG] SUMMARY mwgt(t={wtLog.ToString("F3", CultureInfo.InvariantCulture)},m={wmLog.ToString("F3", CultureInfo.InvariantCulture)},f={wfLog.ToString("F3", CultureInfo.InvariantCulture)}) mrsv=[{mrsvStrLog}] armaBones={armaCountLog} affected={affectedLog} skippedNoSkel={skelLog} skippedNegligible={negLog}")
            For Each r In diag.OrderBy(Function(x) x.Name)
                Dim row = r
                Logger.LogLazy(Function()
                                   Dim inv = CultureInfo.InvariantCulture
                                   Dim cY As Single = Math.Min(Math.Max(row.SyRaw - 1.0F, row.MinY), row.MaxY)
                                   Dim cZ As Single = Math.Min(Math.Max(row.SzRaw - 1.0F, row.MinZ), row.MaxZ)
                                   Return $"[BW-CLAMP-DIAG] bone='{row.Name}' WS={row.HasWS} Range={row.HasRange} " &
                                          $"L1raw(sy={row.SyRaw.ToString("F4", inv)},sz={row.SzRaw.ToString("F4", inv)}) " &
                                          $"range(Y=[{row.MinY.ToString("F4", inv)},{row.MaxY.ToString("F4", inv)}],Z=[{row.MinZ.ToString("F4", inv)},{row.MaxZ.ToString("F4", inv)}]) " &
                                          $"ifClampedWeight(sy={(1.0F + cY).ToString("F4", inv)},sz={(1.0F + cZ).ToString("F4", inv)}) " &
                                          $"weightExcess(y={((row.SyRaw - 1.0F) - cY).ToString("F4", inv)},z={((row.SzRaw - 1.0F) - cZ).ToString("F4", inv)}) " &
                                          $"mrsv(region={row.Region},slider={row.Slider.ToString("F3", inv)}) " &
                                          $"arma(dy={row.ArmaDY.ToString("F4", inv)},dz={row.ArmaDZ.ToString("F4", inv)}) " &
                                          $"FINAL(sy={row.SyFinal.ToString("F4", inv)},sz={row.SzFinal.ToString("F4", inv)}) " &
                                          $"rest(y={row.RestY.ToString("F2", inv)},z={row.RestZ.ToString("F2", inv)})"
                               End Function)
            Next
        End If

        If affected = 0 Then Return Nothing
        Return pose
    End Function

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
        Dim modelFormID = FaceAppearanceSourceFormID(state)
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

    Private Function ResolvePreviewVariant(previewVariant As PreviewVariantDefinition) As PreviewResolutionResult
        Dim result As New PreviewResolutionResult()
        If previewVariant Is Nothing OrElse previewVariant.State Is Nothing Then Return result
        Dim state = previewVariant.State


        result.Warnings.AddRange(previewVariant.Warnings)
        result.SkeletonKey = ResolveSkeletonKey(previewVariant.State, result.Warnings)

        Dim candidates = CollectMeshCandidates(previewVariant.State, result.Warnings, previewVariant.UseFaceGen, previewVariant.OnlyFaceCollect, previewVariant.OnlyOutfitCollect)
        Dim selectedCandidates = SelectWinningCandidates(candidates)

        ' Diagnostic toggles "Render armor" / "Render only armor" se aplican vía RenderHide en
        ' el draw loop (sin re-resolver candidates). Cada shape se categoriza a la salida del
        ' resolver y los handlers de los CheckBoxes setean RenderHide según categoría + estado
        ' de los toggles. Ver ApplyRenderToggleVisibility.

        ' Sculpt source identification (rule per user 2026-04-27):
        '   - Underarmor source = ARMA con slot 33 (BODY) AND HasSculptData. Si existe, su SCLP
        '     aplica a TODOS los over-armor shapes (excepto los con NoUnderarmorScaling=True).
        '   - Si no hay slot-33 source: cada [U] piece (slots 36-40) provee SCLP para SU [A]
        '     correspondiente (37→42 LArm, 38→43 RArm, 39→44 LLeg, 40→45 RLeg, 36→41 Torso).
        '     Mapping de bit: A_bit = U_bit + 5.
        '   - El underarmor NO se aplica a sí mismo (su mesh ni el body desnudo bajo él).
        Const SLOT_BIT_BODY As Integer = 3
        Const U_BIT_FIRST As Integer = 6   ' U Torso
        Const U_BIT_LAST As Integer = 10   ' U RLeg
        Const A_BIT_FIRST As Integer = 11  ' A Torso
        Const A_BIT_LAST As Integer = 15   ' A RLeg
        Dim BODY_MASK As UInteger = 1UI << SLOT_BIT_BODY
        Dim U_MASK As UInteger = 0UI
        For b = U_BIT_FIRST To U_BIT_LAST
            U_MASK = U_MASK Or (1UI << b)
        Next

        Dim globalSculptSource As MeshCandidate = Nothing
        Dim uSculptSourceByBit As New Dictionary(Of Integer, MeshCandidate)
        For Each c In selectedCandidates
            If c.ArmaBoneScaleDeltas Is Nothing OrElse c.ArmaBoneScaleDeltas.Count = 0 Then Continue For
            If (c.SlotMask And BODY_MASK) <> 0 Then
                If globalSculptSource Is Nothing Then globalSculptSource = c
            End If
            For b = U_BIT_FIRST To U_BIT_LAST
                If (c.SlotMask And (1UI << b)) <> 0 Then
                    If Not uSculptSourceByBit.ContainsKey(b) Then uSculptSourceByBit(b) = c
                End If
            Next
        Next

        ' [SCULPT-DECISION] diag: which candidate (if any) was picked as the slot-33 global sculpt
        ' source and which [U]-specific sources exist. Pairs with the per-candidate decision log
        ' below to verify the underarmor→over-armor rule is gating correctly. Log-only.
        If Logger.Enabled Then
            Dim gs = globalSculptSource
            Dim gsLog As String = If(gs Is Nothing, "none",
                $"0x{gs.ArmorAddonFormID:X8} slot=0x{gs.SlotMask:X} deltas={gs.ArmaBoneScaleDeltas.Count}")
            Dim uLog As String = String.Join(",", uSculptSourceByBit.Select(Function(kv) $"U{kv.Key}=0x{kv.Value.ArmorAddonFormID:X8}"))
            Logger.LogLazy(Function() $"[SCULPT-DECISION] sources: global={gsLog} uSpecific=[{uLog}]")
        End If

        Dim loadedNifs As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)

        ' Compute the over-armor [A] slot mask = bits 11..15.
        Dim A_MASK As UInteger = 0UI
        For b = A_BIT_FIRST To A_BIT_LAST
            A_MASK = A_MASK Or (1UI << b)
        Next

        For Each candidate In selectedCandidates
            ' SCULPT applies ONLY to over-armor [A] consumers, never to the source itself nor
            ' to anything else. The engine's two-skeleton model:
            '   - Skel "base" (RACE BSMS only): underarmor source, body skin, hands, head parts.
            '   - Skel "sculpted" (RACE BSMS + SCLP amplifier): pure [A] over-armor pieces.
            ' A candidate is a pure [A] consumer iff it declares at least one [A] bit (11-15)
            ' AND declares neither BODY (bit 3) nor any [U] bit (6-10). Otherwise it is the
            ' source itself (e.g. Armor_GunnerGuard_UnderArmor with slot 0xC7F8 = bits 3+7+8+14+15).
            Dim sculptToApply As List(Of ARMA_BoneScaleDelta) = Nothing
            Dim sourceFormID As UInteger = 0

            Dim isPureOverArmor = (candidate.SlotMask And A_MASK) <> 0 AndAlso
                                  (candidate.SlotMask And BODY_MASK) = 0 AndAlso
                                  (candidate.SlotMask And U_MASK) = 0
            If isPureOverArmor Then
                ' Check NoUnderarmorScaling flag (opt-out from receiving scaling).
                Dim noUnderArmorFlag As Boolean = False
                If candidate.ArmorAddonFormID <> 0UI Then
                    Dim aa = GetParsedArma(candidate.ArmorAddonFormID)
                    If aa IsNot Nothing Then noUnderArmorFlag = aa.NoUnderarmorScaling
                End If

                If Not noUnderArmorFlag Then
                    ' Precedence: [U] specific FIRST. Only if no [U] equivalent exists, fall back
                    ' to slot 33 BODY global source. Use ONE source only (first [A] bit match).
                    For ab = A_BIT_FIRST To A_BIT_LAST
                        If (candidate.SlotMask And (1UI << ab)) <> 0 Then
                            Dim ub = ab - 5
                            Dim uSrc As MeshCandidate = Nothing
                            If uSculptSourceByBit.TryGetValue(ub, uSrc) Then
                                sculptToApply = uSrc.ArmaBoneScaleDeltas
                                sourceFormID = uSrc.ArmorAddonFormID
                                Exit For
                            End If
                        End If
                    Next
                    ' If no [U]-specific source for any covered [A] slot, fall back to slot 33.
                    If sculptToApply Is Nothing AndAlso globalSculptSource IsNot Nothing Then
                        sculptToApply = globalSculptSource.ArmaBoneScaleDeltas
                        sourceFormID = globalSculptSource.ArmorAddonFormID
                    End If
                End If
            End If
            ' Else: candidate is the underarmor source itself (BODY/[U] declared) or unrelated
            ' to the [U]→[A] system (hands, head, accessories) → renders on the base skeleton,
            ' never sculpted.

            ' [SCULPT-DECISION] per-candidate: shows slot, whether it qualified as pure over-armor,
            ' its own header flags (HasSculpt / NoUnderarmorScaling — the opt-out gate) and the final
            ' decision (how many sculpt deltas applied + from which source). Lets us verify whether
            ' the leg/torso [A] pieces SHOULD be taking the slot-33 underarmor sculpt at all. Log-only.
            If Logger.Enabled Then
                Dim candFidL = candidate.ArmorAddonFormID
                Dim slotL = candidate.SlotMask
                Dim isPOL = isPureOverArmor
                Dim ownDeltasL = If(candidate.ArmaBoneScaleDeltas Is Nothing, 0, candidate.ArmaBoneScaleDeltas.Count)
                Dim aaL = If(candFidL <> 0UI, GetParsedArma(candFidL), Nothing)
                Dim noUaL = aaL IsNot Nothing AndAlso aaL.NoUnderarmorScaling
                Dim hasSculptL = aaL IsNot Nothing AndAlso aaL.HasSculptData
                Dim srcL = sourceFormID
                Dim appliedL = If(sculptToApply Is Nothing, 0, sculptToApply.Count)
                Logger.LogLazy(Function() $"[SCULPT-DECISION] cand=0x{candFidL:X8} slot=0x{slotL:X} pureOverArmor={isPOL} ownSculptDeltas={ownDeltasL} hdr(HasSculpt={hasSculptL},NoUnderarmorScaling={noUaL}) -> sculptApplied={appliedL} from=0x{srcL:X8}")
            End If

            LoadNifShapes(candidate, previewVariant.State, loadedNifs, result, sculptToApply, sourceFormID)
        Next

        ' Mount-resolve pass for robot chunks: ahora que los NIFs están cargados, leer
        ' BSConnectPoint::Children del NIF de cada chunk (lista de point names "C-X" que el
        ' chunk declara) y matchear contra los sockets del skeleton (Name "P-X"). El match
        ' es la fuente canónica engine — el OMOD.AttachPoint KYWD del record es solo
        ' metadata del CK para validar compatibilidad chunk↔slot, no la fuente del mounting.
        ResolveRobotChunkMounts(selectedCandidates, loadedNifs, previewVariant.State, result.Warnings)

        ' NOTA: Pipboy synthetic-skin pass se ejecuta DESPUÉS de PrepareSkeleton (no acá), porque
        ' necesita el SkeletonInstance del actor para descubrir el bone target via lookup
        ' case-insensitive contra el dictionary (evita hardcodear "PipboyBone" — distintas razas
        ' pueden tener otra convención de nombre). Ver llamada post-PrepareSkeleton más abajo.

        ' Map shape → (MountSocket, chunkNif) para los robot chunks resueltos. Consumido por
        ' PrepareSkeleton para inyectar bones internos del chunk al SkeletonInstance del actor
        ' anchored al socket bone (BSConnectPointBoneInjector_Class). Solo se popula para
        ' chunks robot con MountSocket asignado — humanoides quedan ausentes y el inject
        ' es no-op para ellos (skinning normal del actor ya los posiciona).
        For Each cand In selectedCandidates
            If cand.MountSocket Is Nothing Then Continue For
            ' Use candidate-specific NIF (populated per-candidate by LoadNifShapes), not the
            ' DictKey lookup. Multi-instance candidates that share DictKey have DIFFERENT
            ' NIF instances — referencia identity matches only this candidate's shapes.
            Dim chunkNif As Nifcontent_Class_Manolo = Nothing
            If Not result.CandidateNif.TryGetValue(cand, chunkNif) Then
                If Logger.Enabled Then
                    Dim cfid = cand.SourceFormID
                    Logger.LogLazy(Function() $"[MOUNT-MAP] cand=0x{cfid:X8} NO CandidateNif entry — skipping")
                End If
                Continue For
            End If
            Dim matched As Integer = 0
            For Each shape In result.Shapes
                If shape.NifContent IsNot chunkNif Then Continue For
                result.ShapeMountSocket(shape) = cand.MountSocket
                result.ShapeChunkNif(shape) = chunkNif
                matched += 1
            Next
            If Logger.Enabled Then
                Dim cFidLog = cand.SourceFormID
                Dim socketNameLog = cand.MountSocket.Name
                Dim nifHashLog2 = chunkNif.GetHashCode()
                Dim matchedLog = matched
                Logger.LogLazy(Function() $"[MOUNT-MAP] cand=0x{cFidLog:X8} socket='{socketNameLog}' nifHash={nifHashLog2} matchedShapes={matchedLog}")
            End If
        Next
        If Logger.Enabled Then
            Dim shapeCountLog = result.Shapes.Count
            Dim mountCountLog = result.ShapeMountSocket.Count
            Logger.LogLazy(Function() $"[MOUNT-MAP] DONE result.Shapes.Count={shapeCountLog} result.ShapeMountSocket.Count={mountCountLog}")
        End If


        DeduplicateWarnings(result.Warnings)
        Return result
    End Function

    Private Function CloneVisualState(state As NPCVisualState) As NPCVisualState
        Dim clone As New NPCVisualState With {
            .FormID = state.FormID,
            .RootNpcFormID = state.RootNpcFormID,
            .TraitsSourceFormID = state.TraitsSourceFormID,
            .InventorySourceFormID = state.InventorySourceFormID,
            .ModelSourceFormID = state.ModelSourceFormID,
            .VariantLabel = state.VariantLabel,
            .IsFemale = state.IsFemale,
            .RaceFormID = state.RaceFormID,
            .SkinFormID = state.SkinFormID,
            .DefaultOutfitFormID = state.DefaultOutfitFormID,
            .SleepOutfitFormID = state.SleepOutfitFormID,
            .HeadTextureFormID = state.HeadTextureFormID,
            .HairColorFormID = state.HairColorFormID,
            .FacialHairColorFormID = state.FacialHairColorFormID,
            .HasTextureLighting = state.HasTextureLighting,
            .TextureLightingColor = state.TextureLightingColor,
            .WeightThin = state.WeightThin,
            .WeightMuscular = state.WeightMuscular,
            .WeightFat = state.WeightFat
        }
        clone.HeadPartFormIDs.AddRange(state.HeadPartFormIDs)
        clone.LoadoutArmorFormIDs.AddRange(state.LoadoutArmorFormIDs)
        For Each kv In state.LoadoutArmorContextKeywords
            clone.LoadoutArmorContextKeywords(kv.Key) = New List(Of UInteger)(kv.Value)
        Next
        clone.ObjectTemplateOMODFormIDs.AddRange(state.ObjectTemplateOMODFormIDs)
        clone.ObjectTemplateCombinations.AddRange(state.ObjectTemplateCombinations)
        clone.HasObjectTemplate = state.HasObjectTemplate
        clone.AttachParentSlotFormIDs.AddRange(state.AttachParentSlotFormIDs)
        Return clone
    End Function

    ''' <param name="parseRace">Optional cached RACE parser (MainForm.ParseRaceCached). Falls back to a
    ''' direct <c>RecordParsers.ParseRACE</c> when Nothing — keeps the offline bake path pure.</param>
    Friend Shared Sub ApplyRaceFallbacks(state As NPCVisualState, traits As TraitsState, pluginManager As PluginManager,
                                         Optional parseRace As Func(Of PluginRecord, RACE_Data) = Nothing)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return

        Dim raceRec = pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            ' No RACE record: all-Default MWGT can't be resolved → leave 0; explicit values pass through.
            state.WeightThin = traits.WeightThin.GetValueOrDefault(0.0F)
            state.WeightMuscular = traits.WeightMuscular.GetValueOrDefault(0.0F)
            state.WeightFat = traits.WeightFat.GetValueOrDefault(0.0F)
            Return
        End If

        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), RecordParsers.ParseRACE(raceRec, pluginManager))

        ' Materialize NPC.MWGT into final 3 floats. Substitution rule lives in ResolveBodyWeights.
        ' Done before the head/skin fallbacks so callers reading state.WeightX downstream always
        ' see resolved values.
        Dim resolvedWeights = ResolveBodyWeights(traits, race, state.IsFemale)
        state.WeightThin = resolvedWeights.Thin
        state.WeightMuscular = resolvedWeights.Muscular
        state.WeightFat = resolvedWeights.Fat

        If state.SkinFormID = 0UI Then
            state.SkinFormID = race.SkinFormID
        End If

        ' FTST PROPIO del NPC (0 si no tiene), capturado ANTES del fallback DFTM de abajo. Acá
        ' state.HeadTextureFormID aún es el FTST del record; las líneas siguientes lo pisan con DFTM cuando es 0.
        ' Lo usa ResolveTextureSet para la precedencia FTST > HDPT.TNAM > DFTM (sin esto no se distingue FTST de DFTM).
        state.ExplicitHeadTextureFormID = state.HeadTextureFormID

        If state.HeadPartFormIDs.Count = 0 Then
            If state.IsFemale Then
                state.HeadPartFormIDs.AddRange(race.FemaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadPartFormIDs.AddRange(race.MaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        ElseIf state.HeadTextureFormID = 0UI Then
            If state.IsFemale Then
                state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        End If

        ' HairColor fallback: when NPC.HCLF is absent (and the template chain didn't supply one
        ' either — Model/Animation traits already collapsed by ResolveModelAnimationStateFromNPC),
        ' the engine reads RACE.HCLF[gender] (Default Hair Colors). Mirror that here. Each gender
        ' slot can be NULL per wbFormIDCk([NULL, CLFM]) at wbDefinitionsFO4.pas:11575 — same
        ' "own gender first, fallback to the other" rule we use for DefaultFaceTexture above.
        If state.HairColorFormID = 0UI Then
            Dim ownGender = If(state.IsFemale, race.FemaleDefaultHairColorFormID, race.MaleDefaultHairColorFormID)
            Dim otherGender = If(state.IsFemale, race.MaleDefaultHairColorFormID, race.FemaleDefaultHairColorFormID)
            state.HairColorFormID = If(ownGender <> 0UI, ownGender, otherGender)
        End If
    End Sub

    ''' <summary>Materialize NPC.MWGT into 3 concrete floats, applying the engine's "Default"
    ''' sentinel substitution rule. Each NPC.MWGT slot may come as Nothing (the parser flagged
    ''' it as Single.MaxValue, the wire encoding of "field not assigned" — see
    ''' RecordParsers.ReadOptionalFloat). Substitution rule:
    '''   • 0 Defaults → return as-is, do NOT renormalize (respect the record's data even if
    '''     it doesn't sum to 1).
    '''   • 1 Default  → fill the missing slot with clamp(1 - sum(other 2), 0, +∞). The two
    '''     explicit values stay untouched. Result sums to 1 unless the two explicit values
    '''     exceeded 1 (in which case the missing slot is 0 and the sum stays > 1).
    '''   • 2 Defaults → fill the missing slots from RACE.{Male|Female}DefaultWeight{X}, then
    '''     renormalize the 3 to sum=1 (skip if total is 0).
    '''   • 3 Defaults → use RACE.{Male|Female}DefaultWeight{X} verbatim; do NOT renormalize.
    ''' RACE defaults are read per-gender. If RACE doesn't carry the field (record &lt; v109),
    ''' fallback is 0.
    ''' Logs the raw → resolved transition when any substitution happened, for audit.</summary>
    Private Shared Function ResolveBodyWeights(traits As TraitsState, race As RACE_Data, isFemale As Boolean) As (Thin As Single, Muscular As Single, Fat As Single)
        Dim rawT = traits.WeightThin
        Dim rawM = traits.WeightMuscular
        Dim rawF = traits.WeightFat
        Dim defaultCount = 0
        If Not rawT.HasValue Then defaultCount += 1
        If Not rawM.HasValue Then defaultCount += 1
        If Not rawF.HasValue Then defaultCount += 1

        Dim resT As Single, resM As Single, resF As Single

        Select Case defaultCount
            Case 0
                resT = rawT.Value
                resM = rawM.Value
                resF = rawF.Value
            Case 1
                Dim a As Single, b As Single
                If Not rawT.HasValue Then
                    a = rawM.Value : b = rawF.Value
                    resT = Math.Max(0.0F, 1.0F - a - b) : resM = a : resF = b
                ElseIf Not rawM.HasValue Then
                    a = rawT.Value : b = rawF.Value
                    resT = a : resM = Math.Max(0.0F, 1.0F - a - b) : resF = b
                Else
                    a = rawT.Value : b = rawM.Value
                    resT = a : resM = b : resF = Math.Max(0.0F, 1.0F - a - b)
                End If
            Case 2
                Dim raceT = If(isFemale, race.FemaleDefaultWeightThin, race.MaleDefaultWeightThin).GetValueOrDefault(0.0F)
                Dim raceM = If(isFemale, race.FemaleDefaultWeightMuscular, race.MaleDefaultWeightMuscular).GetValueOrDefault(0.0F)
                Dim raceF = If(isFemale, race.FemaleDefaultWeightFat, race.MaleDefaultWeightFat).GetValueOrDefault(0.0F)
                resT = If(rawT, raceT)
                resM = If(rawM, raceM)
                resF = If(rawF, raceF)
                Dim sum = resT + resM + resF
                If sum > 0.0F Then
                    resT /= sum : resM /= sum : resF /= sum
                End If
            Case Else  ' 3
                resT = If(isFemale, race.FemaleDefaultWeightThin, race.MaleDefaultWeightThin).GetValueOrDefault(0.0F)
                resM = If(isFemale, race.FemaleDefaultWeightMuscular, race.MaleDefaultWeightMuscular).GetValueOrDefault(0.0F)
                resF = If(isFemale, race.FemaleDefaultWeightFat, race.MaleDefaultWeightFat).GetValueOrDefault(0.0F)
        End Select

        If defaultCount > 0 Then
            Dim rawStr = $"({(If(rawT.HasValue, rawT.Value.ToString("F3"), "Default"))},{(If(rawM.HasValue, rawM.Value.ToString("F3"), "Default"))},{(If(rawF.HasValue, rawF.Value.ToString("F3"), "Default"))})"
        End If

        Return (resT, resM, resF)
    End Function

    Private Function ResolveSkeletonKey(state As NPCVisualState, warnings As List(Of String)) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return ""

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = ParseRaceCached(raceRec)
        Dim skeletonPath = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrWhiteSpace(skeletonPath) Then
            skeletonPath = If(race.MaleSkeletonPath <> "", race.MaleSkeletonPath, race.FemaleSkeletonPath)
        End If

        Dim dictionaryKey = NormalizeDictionaryKeyWithMeshesPrefix(skeletonPath)
        If dictionaryKey = "" Then warnings.Add($"No skeleton path resolved for race {state.RaceFormID:X8}")
        Return dictionaryKey
    End Function

    Private Function ResolveTraitsStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As TraitsState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnTraitsState(npc)
        If visited.Contains(formID) Then Return own

        Dim acbsOppGender As Boolean = (npc.AcbsFlags And &H80000UI) <> 0UI

        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) Then
            Return own
        End If

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
        Dim sourceRec = _pluginManager.GetRecord(sourceFormID)

        Dim resolved = ResolveTraitsStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Traits template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveInventoryStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As InventoryState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnInventoryState(npc)
        If visited.Contains(formID) Then Return own
        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Inventory) Then Return own

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Inventory)
        Dim resolved = ResolveInventoryStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Inventory template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveModelAnimationStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As ModelAnimationState
        Dim npc = GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = CreateOwnModelAnimationState(npc)
        If visited.Contains(formID) Then Return own
        If Not HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.ModelAnimation) Then Return own

        visited.Add(formID)
        Dim sourceFormID = ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.ModelAnimation)
        Dim resolved = ResolveModelAnimationStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Model/Animation template unresolved for {DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveTraitsStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As TraitsState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Traits", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveTraitsStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveInventoryStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As InventoryState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Inventory", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveInventoryStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveModelAnimationStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As ModelAnimationState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Model/Animation", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveModelAnimationStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveTemplateSourceRecord(sourceFormID As UInteger, categoryName As String, visited As HashSet(Of UInteger), warnings As List(Of String)) As PluginRecord
        If sourceFormID = 0UI Then Return Nothing

        Dim sourceRecord = _pluginManager.GetRecord(sourceFormID)
        If sourceRecord Is Nothing Then
            warnings.Add($"Missing {categoryName} template source {sourceFormID:X8}")
            Return Nothing
        End If

        Select Case sourceRecord.Header.Signature
            Case "NPC_"
                Return sourceRecord
            Case "LVLN"
                Dim resolvedFormID = ResolveSingleLeveledTemplate(sourceRecord, warnings)
                If resolvedFormID = 0UI Then Return Nothing
                If visited.Contains(resolvedFormID) Then Return Nothing
                Return ResolveTemplateSourceRecord(resolvedFormID, categoryName, visited, warnings)
            Case Else
                warnings.Add($"Unsupported {categoryName} template source {sourceRecord.Header.Signature} [{sourceFormID:X8}]")
                Return Nothing
        End Select
    End Function

    ''' <summary>Pick a random leaf NPC from a LVLN, using Count as weight, recursing into nested LVLNs.
    ''' Ignores Level requirements and ChanceNone for NPC leveled lists.</summary>
    Private Function PickWeightedRandomFromLVLN(lvlnFormID As UInteger, visited As HashSet(Of UInteger)) As UInteger
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return 0UI
        visited.Add(lvlnFormID)

        Dim lvln As LVLN_Data = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            Dim lvlnRec = _pluginManager.GetRecord(lvlnFormID)
            If lvlnRec Is Nothing OrElse lvlnRec.Header.Signature <> "LVLN" Then Return 0UI
            lvln = RecordParsers.ParseLVLN(lvlnRec, _pluginManager)
        End If

        ' Build weighted list of leaf NPC FormIDs: each entry contributes Count copies
        Dim weightedLeaves As New List(Of UInteger)()

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = _pluginManager.GetRecord(entry.FormID)
            If entryRec Is Nothing Then Continue For

            Dim weight = Math.Max(CInt(entry.Count), 1)

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    For i = 0 To weight - 1
                        weightedLeaves.Add(entry.FormID)
                    Next
                Case "LVLN"
                    ' Recurse into nested LVLN: pick from sub-list, weighted by this entry's Count
                    For i = 0 To weight - 1
                        Dim subPick = PickWeightedRandomFromLVLN(entry.FormID, New HashSet(Of UInteger)(visited))
                        If subPick <> 0UI Then weightedLeaves.Add(subPick)
                    Next
            End Select
        Next

        If weightedLeaves.Count = 0 Then Return 0UI

        ' Apply gender filter if set
        Dim genderFilter = CurrentGenderFilter
        If genderFilter <> GenderFilterMode.Random Then
            Dim filtered = weightedLeaves.Where(Function(fid)
                                                    Dim npc As NPC_Data = Nothing
                                                    If _npcByIdCache.TryGetValue(fid, npc) Then
                                                        Return If(genderFilter = GenderFilterMode.Female, npc.IsFemale, Not npc.IsFemale)
                                                    End If
                                                    Dim npcRec = _pluginManager.GetRecord(fid)
                                                    If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then Return True
                                                    Dim parsed = RecordParsers.ParseNPC(npcRec, "", _pluginManager)
                                                    Return If(genderFilter = GenderFilterMode.Female, parsed.IsFemale, Not parsed.IsFemale)
                                                End Function).ToList()
            If filtered.Count > 0 Then weightedLeaves = filtered
        End If

        Dim picked = weightedLeaves(_rng.Next(weightedLeaves.Count))
        Return picked
    End Function

    ''' <summary>Pick a single NPC from a LVLN for template resolution. Uses Count as weight.
    ''' Results are cached per NPC resolution to ensure consistent picks across categories.</summary>
    Private Function ResolveSingleLeveledTemplate(lvlnRec As PluginRecord, warnings As List(Of String)) As UInteger
        Dim lvlnFormID = lvlnRec.Header.FormID

        ' Check cache first — same LVLN must return same pick within one NPC resolution
        If _lvlnPickCache IsNot Nothing Then
            Dim cached As UInteger = 0UI
            If _lvlnPickCache.TryGetValue(lvlnFormID, cached) Then
                Return cached
            End If
        End If

        Dim picked = PickWeightedRandomFromLVLN(lvlnFormID, New HashSet(Of UInteger)())

        If picked = 0UI Then
            warnings.Add($"Leveled template {DescribeRecord(lvlnRec)} has no usable entries")
            Return 0UI
        End If

        If _lvlnPickCache IsNot Nothing Then _lvlnPickCache(lvlnFormID) = picked
        Return picked
    End Function

    Private Function CollectMeshCandidates(state As NPCVisualState, warnings As List(Of String), Optional useFaceGen As Boolean = False, Optional onlyFaceCollect As Boolean = False, Optional onlyOutfitCollect As Boolean = False) As List(Of MeshCandidate)
        Dim candidates As New List(Of MeshCandidate)
        Dim order As Integer = 0

        ' Collect scope (Full / OnlyFace / OnlyOutfit):
        '   • Skin (body) — Full only; OnlyFace and OnlyOutfit both drop it.
        '   • Outfit      — Full + OnlyOutfit (the picker's single-piece preview uses a 1-item draft);
        '                    OnlyFace drops it.
        '   • HeadParts + robot chunks — Full + OnlyFace; OnlyOutfit drops them.
        ' OnlyFaceCollect: editor host / MainForm "Only Face" ComboBox. OnlyOutfitCollect: the Edit Outfit
        ' picker's "selected piece only". Both funnel here via PreviewVariantDefinition — no parallel paths.
        If Not onlyFaceCollect AndAlso Not onlyOutfitCollect AndAlso state.SkinFormID <> 0UI Then
            CollectArmoCandidates(state.SkinFormID, state, MeshCandidateKind.Skin, candidates, order, warnings)
        End If

        If Not onlyFaceCollect Then
            ' Use pre-resolved LoadoutArmorFormIDs (already expanded from LVLI).
            ' These are the final ARMO FormIDs for this specific variant.
            If state.LoadoutArmorFormIDs.Count > 0 Then
                For Each armoFormID In state.LoadoutArmorFormIDs
                    CollectArmoCandidates(armoFormID, state, MeshCandidateKind.Outfit, candidates, order, warnings)
                Next
            ElseIf state.DefaultOutfitFormID <> 0UI Then
                ' Fallback: read OTFT directly (for NPCs without leveled expansion)
                Dim outfitRec = _pluginManager.GetRecord(state.DefaultOutfitFormID)
                If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then
                    warnings.Add($"Default outfit {state.DefaultOutfitFormID:X8} is missing or not OTFT")
                Else
                    Dim outfit = RecordParsers.ParseOTFT(outfitRec, _pluginManager)
                    For Each itemFormID In outfit.ItemFormIDs
                        CollectArmoCandidates(itemFormID, state, MeshCandidateKind.Outfit, candidates, order, warnings)
                    Next
                End If
            End If
        End If

        ' HeadParts: Full + OnlyFace; OnlyOutfit (single-piece preview) drops them.
        If Not onlyOutfitCollect Then
            Dim mergedHeadParts = MergeHeadPartsWithRaceDefaults(state)
            CollectHeadPartCandidates(mergedHeadParts, New HashSet(Of UInteger)(), candidates, order, warnings, state, useFaceGen)
        End If

        ' Robot path (NPC_.ObjectTemplate). Replaces the legacy "iterate combo #0
        ' OMODFormIDs flat list" branch. Engine rule (verified vs dump v2):
        '   1. ObjectTemplateResolver.ResolveNpcCombinations picks ONE combination
        '      (kw-match → first Default → first overall).
        '   2. Walk the chosen combination's IncludedOmods: each OMOD.ModelPath != ""
        '      is a chunk MeshCandidate to mount via BSConnectPoint::Parents lookup
        '      from the actor's skeleton NIF (helper BSConnectPointReader).
        '   3. OMODs without ModelPath but with Properties feed OmodResolutionApplier
        '      with formType="NPC_" (idx 5 MaterialSwap, idx 4 ColorRemap).
        ' AttachPoint resolution: OMOD.AttachPointFormID → KYWD record → EditorID,
        ' matched case-insens against ConnectPointInfo.Name.
        If Not onlyOutfitCollect AndAlso state.HasObjectTemplate AndAlso state.ObjectTemplateCombinations IsNot Nothing _
           AndAlso state.ObjectTemplateCombinations.Count > 0 Then
            CollectRobotChunkCandidates(state, candidates, order, warnings)
        End If

        Return candidates
    End Function

    ''' <summary>Thin instance wrapper over the shared <see cref="HeadPartResolver.MergeHeadPartsWithRaceDefaults"/>;
    ''' threads <see cref="_pluginManager"/> through and unpacks the render-side state into the
    ''' helper's primitive parameter list. Real implementation + logging lives in the helper module.</summary>
    Private Function MergeHeadPartsWithRaceDefaults(state As NPCVisualState) As List(Of UInteger)
        If state Is Nothing Then Return New List(Of UInteger)
        Return HeadPartResolver.MergeHeadPartsWithRaceDefaults(state.RaceFormID, state.IsFemale, state.HeadPartFormIDs, _pluginManager,
                                                               AddressOf ParseRaceCached, AddressOf ParseHdptCached)
    End Function

    ''' <summary>Parse (and cache) an ARMO by FormID. Returns Nothing if the FormID does not resolve
    ''' to an ARMO record. Does NOT swallow parse exceptions — callers that need to tolerate a malformed
    ''' record keep their own Try/Catch around the call (same behavior as the previous inline
    ''' RecordParsers.ParseARMO). See <see cref="_parsedArmoCache"/> for cache lifetime/thread-safety.</summary>
    Private Function GetParsedArmo(formID As UInteger) As ARMO_Data
        If formID = 0UI Then Return Nothing
        Return _parsedArmoCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = _pluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMO" Then Return Nothing
                Return RecordParsers.ParseARMO(rec, _pluginManager)
            End Function)
    End Function

    ''' <summary>Parse (and cache) an ARMA by FormID. Returns Nothing if the FormID does not resolve to
    ''' an ARMA record. Does NOT swallow parse exceptions (see <see cref="GetParsedArmo"/>).</summary>
    Private Function GetParsedArma(formID As UInteger) As ARMA_Data
        If formID = 0UI Then Return Nothing
        Return _parsedArmaCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = _pluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMA" Then Return Nothing
                Return RecordParsers.ParseARMA(rec, _pluginManager)
            End Function)
    End Function

    ''' <summary>Parse (and cache) a RACE from an already-fetched record, keyed by its FormID. Drop-in
    ''' replacement for the inline <c>ParseRaceCached(rec)</c> at every call site —
    ''' the record is already in scope so behavior is identical, just memoized. Same lifetime/thread-safety
    ''' as <see cref="_parsedArmoCache"/> (dies with the load order; ConcurrentDictionary for overlapping renders).</summary>
    Private Function ParseRaceCached(rRec As PluginRecord) As RACE_Data
        If rRec Is Nothing Then Return Nothing
        Return _parsedRaceCache.GetOrAdd(rRec.Header.FormID, Function(fid) RecordParsers.ParseRACE(rRec, _pluginManager))
    End Function

    ''' <summary>Parse (and cache) an HDPT from an already-fetched record, keyed by its FormID.
    ''' Drop-in replacement for inline <c>ParseHdptCached(rec)</c>.</summary>
    Private Function ParseHdptCached(hRec As PluginRecord) As HDPT_Data
        If hRec Is Nothing Then Return Nothing
        Return _parsedHdptCache.GetOrAdd(hRec.Header.FormID, Function(fid) RecordParsers.ParseHDPT(hRec, _pluginManager))
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
                Dim a = GetParsedArmo(fid)
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
                Dim race = ParseRaceCached(rRec)
                Return race IsNot Nothing AndAlso ArmoIsPowerArmor(race.SkinFormID)
            End Function)
    End Function

    Private Sub CollectArmoCandidates(armoFormID As UInteger,
                                      state As NPCVisualState,
                                      kind As MeshCandidateKind,
                                      candidates As List(Of MeshCandidate),
                                      ByRef order As Integer,
                                      warnings As List(Of String))
        Dim armo = GetParsedArmo(armoFormID)
        If armo Is Nothing Then Return

        Dim useFaceGen As Boolean = HasFaceGenAssets(state)

        ' Power-armor gate: an ArmorTypePower piece only fits an actor whose race is a power-armor race
        ' (in a frame). Drop the whole ARMO otherwise — PA armatures list HumanRace too, so the per-ARMA
        ' race check would render it on humans mounted wrong (see helper block above).
        If ArmoIsPowerArmor(armoFormID) AndAlso Not RaceIsPowerArmor(state.RaceFormID) Then
            Logger.LogLazy(Function() $"[PA-GATE] dropped ARMO=0x{armoFormID:X8} (ArmorTypePower) — race=0x{state.RaceFormID:X8} is not a power-armor race")
            Return
        End If
        ' NO early-out on ARMO.RaceFormID: vanilla convention is each ARMA declares its own
        ' race compatibility via RNAM + AdditionalRaces (MODL entries). An ARMO with
        ' RNAM=HumanRace is commonly worn by Ghouls/Synths if the sub-ARMAs list those as
        ' AdditionalRaces. The per-ARMA check (ArmorAddonMatchesRace) handles this correctly.
        ' Log the ARMO race only for visibility; don't reject based on it.

        ' Multi-addon resolution: ARMOs con varios `Models` (ej. Combat Torso = Lite/Mid/Heavy)
        ' eligen UN addon vía la cadena: LVLI.LLKC keywords → ARMO.OBTS combination keyword match
        ' → OMOD Property AddonIndex (idx 7 wbArmorPropertyEnum). Fallback a BaseAddonIndex (FNAM)
        ' o índice 0 si nada matchea.
        ' Spec: wbDefinitionsFO4.pas:6187-6192 (Models), 5867 (OBTS), 5710 (AddonIndex property),
        ' 1192-1245 (wbOBTEAddonIndexToStr — flujo del engine).
        ' AddonIndex resolution. El INDX en el array Models de la ARMO no es índice único —
        ' es etiqueta de "grupo de addons que se cargan juntos". El engine resuelve UN
        ' AddonIndex efectivo (default 0; override via OMOD AddonIndex Property cuando OBTS
        ' combination matchea contexto de keywords) y carga TODOS los Models cuyo INDX coincide.
        '   - Sturgess (Abbot): efectiveIdx=0, dos Models con INDX=0 (clothes+gloves) → carga ambos.
        '   - Gunner Combat Torso: keyword Heavy → OMOD AddonIndex=2 → carga el grupo INDX=2.
        ' ctxKeywords lifted out of the addon-resolve block so the OBTS/OMOD resolver below can
        ' use the same set. Source: LVLI.LLKC propagation (arch_outfit_resolution.md). Empty
        ' for ARMOs reached without a leveled outfit (e.g. NPC.WNAM skin) — combinations with
        ' Default=True still apply, keyword-only combinations don't.
        Dim ctxKeywords As List(Of UInteger) = Nothing
        state.LoadoutArmorContextKeywords?.TryGetValue(armoFormID, ctxKeywords)

        ' Resolve OBTS/OMOD canonical view ONCE per ARMO. Shared by every MeshCandidate
        ' produced for this ARMO's addons — they all live under the same combination overlay.
        ' The applier runs in ApplyShapeMaterialOverrides after the ARMA-direct base swap.
        Dim omodResolution = ObjectTemplateResolver.ResolveArmoCombinations(armo, ctxKeywords, _pluginManager)

        ' [FASE 3] Chunk-mount path biped: OMODs con AttachPoint != 0 AND ModelPath != "" se
        ' montan vía BSConnectPoint igual que robot chunks. Delegate al shared con
        ' formType="ARMO". Para ARMOs sin chunk-mount OMODs (solo property modifiers tipo
        ' ap_armor_Lining/Tier/Size), el shared early-returns sin emitir candidates.
        ' Capa V2 ahora SÍ aplica a biped: el gate Fase 2.5 fue removido. Toda shape con
        ' MountSocket recibe el mount vía RE-BIND de su skin (huesos del esqueleto intactos).
        CollectOmodChunkCandidates(omodResolution, "ARMO", state, candidates, order, warnings)

        Dim addonOrder As List(Of UInteger)
        If armo.ArmorAddons.Count >= 1 Then
            ' Resolve effective AddonIndex. ResolveEffectiveAddonIndex ahora devuelve Integer? —
            ' HasValue=True cuando hay OMOD override keyword-driven; sino Nothing → usar
            ' BaseAddonIndex (FNAM) si está, sino 0 (vanilla default).
            Dim resolved = ResolveEffectiveAddonIndex(armo, ctxKeywords)
            Dim effectiveIdx As Integer
            If resolved.HasValue Then
                effectiveIdx = resolved.Value
            ElseIf armo.BaseAddonIndex >= 0 Then
                effectiveIdx = armo.BaseAddonIndex
            Else
                effectiveIdx = 0
            End If

            ' Take ALL models whose INDX matches the effective AddonIndex (group, not single).
            addonOrder = New List(Of UInteger)
            For Each entry In armo.ArmorAddons
                If CInt(entry.AddonIndex) = effectiveIdx Then
                    addonOrder.Add(entry.ArmaFormID)
                End If
            Next
            ' Defensive fallback: si el INDX resuelto no existe en los Models (datos malformados
            ' o keyword-driven INDX que apunta a un grupo no presente), usar todas las entries
            ' con el menor INDX disponible — no crashear ni dejar el outfit vacío.
            If addonOrder.Count = 0 Then
                Dim minIdx As Integer = armo.ArmorAddons.Min(Function(e) CInt(e.AddonIndex))
                For Each entry In armo.ArmorAddons
                    If CInt(entry.AddonIndex) = minIdx Then addonOrder.Add(entry.ArmaFormID)
                Next
            End If
        Else
            addonOrder = armo.ArmorAddonFormIDs.ToList()
        End If

        ' Within-ARMO armature slot occupancy (engine "first addon claims the slot" rule, see the
        ' coveredSlots check before candidates.Add below). Accumulates the biped slots already taken
        ' by earlier race-matching armature entries of THIS ARMO.
        Dim coveredSlots As UInteger = 0UI
        For Each armaFormID In addonOrder
            Dim arma = GetParsedArma(armaFormID)
            If arma Is Nothing Then Continue For
            ' raceOk drives the skip below (app logic, always computed). The block under
            ' If Logger.Enabled is PURELY diagnostic — it dumps every ARMA at the effective addon
            ' index (even race-skipped ones) with its model flags (MO2F/MO3F/MO4F/MO5F) + all four
            ' model paths, so the bombín "human + robot" duplicate can be read off the log: which
            ' addons sit at this index, which races they accept, and whether a second ARMA is
            ' pulling in a 1st-person / facebones / robot-variant model.
            Dim raceOk As Boolean = ArmorAddonMatchesRace(arma, state.RaceFormID)
            If Logger.Enabled Then
                Dim a = arma
                Dim afid = armaFormID
                Dim armoFid = armoFormID
                Dim rOkL = raceOk
                Logger.LogLazy(Function() $"[ARMA-MODELFLAGS] ARMO=0x{armoFid:X8} ARMA=0x{afid:X8} '{a.EditorID}' " &
                    $"race=0x{a.RaceFormID:X8} addRaces=[{String.Join(",", a.AdditionalRaces.Select(Function(x) x.ToString("X8")))}] raceOk={rOkL} slot=0x{a.SlotMask:X8} | " &
                    $"MO2F=0x{a.MaleModelFlags:X2}({DescribeModelFlags(a.MaleModelFlags)}) MO3F=0x{a.FemaleModelFlags:X2}({DescribeModelFlags(a.FemaleModelFlags)}) " &
                    $"MO4F=0x{a.MaleFPModelFlags:X2} MO5F=0x{a.FemaleFPModelFlags:X2} | " &
                    $"MO2S(matswap)=0x{a.MaleMaterialSwapFormID:X8} MO3S=0x{a.FemaleMaterialSwapFormID:X8} MO2C(remap)={If(a.MaleColorRemapIndex.HasValue, a.MaleColorRemapIndex.Value.ToString("F3"), "none")} | " &
                    $"MOD2='{a.MaleMeshPath}' MOD3='{a.FemaleMeshPath}' MOD4='{a.MaleFPMeshPath}' MOD5='{a.FemaleFPMeshPath}'")
            End If
            If Not raceOk Then
                Continue For
            End If

            ' Pick the gender-matching bone scale block (if any) and log + stash it on the
            ' candidate. Engine-side these per-bone Vec3 deltas are added on top of RACE.BSMS
            ' to shape the outfit around the body (cinched waist, wider hips, vest volume).
            Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
            Dim genderBoneScale As List(Of ARMA_BoneScaleDelta) = Nothing
            For Each bsg In arma.BoneScaleData
                If bsg.Gender <> targetGender Then Continue For
                If bsg.Bones.Count = 0 Then Continue For
                genderBoneScale = bsg.Bones
                For Each bd In bsg.Bones
                    Dim mag = Math.Sqrt(bd.DeltaX * bd.DeltaX + bd.DeltaY * bd.DeltaY + bd.DeltaZ * bd.DeltaZ)
                Next
                Exit For
            Next

            ' Resolve mesh path with ARMA-first / ARMO-WorldModel-fallback semantics.
            ' ARMO.MOD2 (male) / MOD4 (female) per wbDefinitionsFO4.pas:6164-6175 populate when the
            ' mesh is authored at ARMO level (robots: Assaultron skin has ARMO.MOD2=Assaultron.nif
            ' with empty ARMA.MOD2/MOD3). Humanoid armors inverse: ARMA has the mesh, ARMO.MOD2/MOD4
            ' usually empty. Gender mirror inside each source: try same-gender first, then opposite.
            Dim meshPath = If(state.IsFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If meshPath = "" Then meshPath = If(arma.MaleMeshPath <> "", arma.MaleMeshPath, arma.FemaleMeshPath)
            If meshPath = "" Then
                meshPath = If(state.IsFemale, armo.FemaleWorldModelPath, armo.MaleWorldModelPath)
                If meshPath = "" Then meshPath = If(armo.MaleWorldModelPath <> "", armo.MaleWorldModelPath, armo.FemaleWorldModelPath)
            End If
            If meshPath = "" Then
                Continue For
            End If

            Dim armaDictKey As String = NormalizeDictionaryKeyWithMeshesPrefix(meshPath)
            ' "Has FaceBones Model" (MO2F/MO3F bit 0x01): the engine swaps this model for its
            ' <model>_faceBones.nif sibling (identical geometry, skinned to the face bones) on FaceGen
            ' NPCs so it deforms with the head's FMRS bone pose and covers the hair. Mirror of the HDPT
            ' face-region redirect (~line 10489). Fallback: TryGetFaceBonesVariant returns "" when the
            ' sibling is absent from FilesDictionary, so we keep the base mesh. Render/preview only; the
            ' bake is untouched.
            If useFaceGen Then
                Dim modelFlags As Byte = If(state.IsFemale, arma.FemaleModelFlags, arma.MaleModelFlags)
                If (modelFlags And &H1) <> 0 Then
                    Dim fbKey = TryGetFaceBonesVariant(armaDictKey, -1)
                    If fbKey <> "" Then
                        If Logger.Enabled Then
                            Dim afidLog = armaFormID
                            Dim fbLog = fbKey
                            Logger.LogLazy(Function() $"[ARMA-FACEBONES] ARMA=0x{afidLog:X8} redirect base->_faceBones dictKey='{fbLog}'")
                        End If
                        armaDictKey = fbKey
                    End If
                End If
            End If

            Dim effSlotMask As UInteger = EffectiveArmaSlotMask(arma, armo)

            ' Within-ARMO armature dedup. The engine processes the armature in Models order; the FIRST
            ' race-matching addon to claim a biped slot owns it, and a later addon overlapping an
            ' already-claimed slot is dropped. This is what selects the human variant over the Mr Handy
            ' variant of a hat that lists BOTH races at the same INDX (AAClothesMobsterHat #0 race={Human}
            ' + AAHandyMobsterHat #1 race={Human,Handy}, both INDX 0, both slot 30): on a human #0 claims
            ' slot 30 → #1 overlaps → dropped; on a Mr Handy #0 fails the race check → #1 claims it.
            ' Per-SLOT, so complementary same-index addons (Sturgess clothes BODY + gloves Hands, different
            ' slots) BOTH still load. Distinct from SelectWinningCandidates' cross-outfit last-equipped-wins
            ' (that's between DIFFERENT equipped ARMOs). Slotless addons (effSlotMask=0) are never dropped
            ' here — they occupy no biped slot.
            If effSlotMask <> 0UI AndAlso (effSlotMask And coveredSlots) <> 0UI Then
                Dim aEdid = If(arma.EditorID, "")
                Dim afid2 = armaFormID
                Dim armoFid2 = armoFormID
                Dim slotL = effSlotMask
                Logger.LogLazy(Function() $"[ARMA-ARMATURE-DEDUP] ARMO=0x{armoFid2:X8} dropped ARMA=0x{afid2:X8} '{aEdid}' slot=0x{slotL:X8} — biped slot already claimed by an earlier race-matching armature entry of this ARMO")
                Continue For
            End If
            coveredSlots = coveredSlots Or effSlotMask

            candidates.Add(New MeshCandidate With {
                .DictKey = armaDictKey,
                .SlotMask = effSlotMask,
                .Priority = If(state.IsFemale, arma.FemalePriority, arma.MalePriority),
                .Kind = kind,
                .SourceFormID = armoFormID,
                .ArmorAddonFormID = armaFormID,
                .TextureSetFormID = If(state.IsFemale,
                                       If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                                       If(arma.MaleSkinTextureFormID <> 0UI, arma.MaleSkinTextureFormID, arma.FemaleSkinTextureFormID)),
                .MaterialSwapFormID = If(state.IsFemale,
                                          If(arma.FemaleMaterialSwapFormID <> 0UI, arma.FemaleMaterialSwapFormID, arma.MaleMaterialSwapFormID),
                                          If(arma.MaleMaterialSwapFormID <> 0UI, arma.MaleMaterialSwapFormID, arma.FemaleMaterialSwapFormID)),
                .ColorRemapIndex = If(state.IsFemale,
                                       If(arma.FemaleColorRemapIndex.HasValue, arma.FemaleColorRemapIndex, arma.MaleColorRemapIndex),
                                       If(arma.MaleColorRemapIndex.HasValue, arma.MaleColorRemapIndex, arma.FemaleColorRemapIndex)),
                .OmodResolution = omodResolution,
                .Order = order,
                .ArmaBoneScaleDeltas = genderBoneScale
            })

            ' [OUTFIT-RESOLVE] dump por cada candidate emitido. Tag PIPBOY-CANDIDATE cuando el
            ' SlotMask contiene bit 30 (slot 60 - Pipboy, wbDefinitionsFO4.pas:3776). Permite ver
            ' qué ARMA produce el mesh del Pipboy, qué path se resuelve, qué slot mask trae, y
            ' poder cotejar contra el NIF (skinned? BSConnectPoint::Parents declarado?).
            Dim slotHex = effSlotMask.ToString("X8")
            Dim armoEdid = If(armo.EditorID, "")
            Dim armaEdid = If(arma.EditorID, "")
            Dim isPipboyBit As Boolean = (effSlotMask And &H40000000UI) <> 0UI
            Dim tag = If(isPipboyBit, "[OUTFIT-RESOLVE PIPBOY-CANDIDATE]", "[OUTFIT-RESOLVE]")
            Dim meshPathL = meshPath
            Dim orderL = order
            Dim kindL = kind
            Logger.LogLazy(Function() $"{tag} kind={kindL} order={orderL} ARMO=0x{armoFormID:X8} '{armoEdid}' ARMA=0x{armaFormID:X8} '{armaEdid}' slot=0x{slotHex} mesh='{meshPathL}'")

            order += 1
        Next
    End Sub

    ''' <summary>Compute the EFFECTIVE UsesBodyTexture flag for an HDPT, applying the CBBE-style
    ''' override fix for FemaleHeadHumanRearTEMP (vanilla FormID 0x0004D0E9).
    '''
    ''' Pure function over (hdpt, formID, sourceRecord, state). Used by:
    ''' • CollectHeadPartCandidate during full render (to populate MeshCandidate.UsesBodyTexture).
    ''' • RefreshBodySkinLivePreview's HeadPart refresh path during fast-path skin changes.
    ''' Both code paths share this helper so the fix applies identically — no drift.
    '''
    ''' The fix: some body replacers override this HDPT and clear UsesBodyTexture, which leaves
    ''' the headrear stuck on the vanilla basehumanfemaleskin material — wrong for non-human
    ''' races (ghoul/synth/etc). Rule: if HDPT is FemaleHeadHumanRearTEMP AND it comes from an
    ''' override (originating plugin ≠ Fallout4.esm) AND the flag is False, force it True for
    ''' any actor that is NOT Human-Female. Human-Female keeps False (vanilla path).
    '''
    ''' FormID compare uses low-24-bits mask: load-order prefix differs per plugin chain (vanilla
    ''' Fallout4.esm gets 0x00, mods get 0x01..0xFF), but the bare record ID is shared.
    '''
    ''' <paramref name="logTag"/> distinguishes log lines coming from different call sites
    ''' (e.g. "CBBE-HEADREAR" for full render vs "CBBE-HEADREAR-FAST" for the fast-path).</summary>
    Private Function ComputeEffectiveUsesBodyTexture(hdpt As HDPT_Data, hdptFormID As UInteger,
                                                       hdptRec As PluginRecord, state As NPCVisualState,
                                                       Optional logTag As String = "CBBE-HEADREAR") As Boolean
        Const FemaleHeadHumanRearTEMPBareID As UInteger = &H4D0E9UI
        Const HumanRaceBareID As UInteger = &H13746UI
        Dim effective = hdpt.UsesBodyTexture
        If (hdptFormID And &HFFFFFFUI) = FemaleHeadHumanRearTEMPBareID AndAlso Not hdpt.UsesBodyTexture Then
            Dim sourcePlugin As String = If(hdptRec?.SourcePluginName, "")
            Dim isOverride = Not String.Equals(sourcePlugin, "Fallout4.esm", StringComparison.OrdinalIgnoreCase) AndAlso Not String.IsNullOrEmpty(sourcePlugin)
            Dim raceBare As UInteger = If(state IsNot Nothing, state.RaceFormID And &HFFFFFFUI, 0UI)
            Dim isHumanFemale = (state IsNot Nothing) AndAlso raceBare = HumanRaceBareID AndAlso state.IsFemale
            If isOverride AndAlso Not isHumanFemale Then
                effective = True
            End If
        End If
        Return effective
    End Function

    ''' <summary>NPC robot path: walks NPC_.OBTE via the canonical resolver, picks ONE
    ''' combination, expands its IncludedOmods recursively, emits one MeshCandidate per chunk
    ''' OMOD (with mount transform from BSConnectPoint::Parents lookup), and shares the
    ''' resolution across all emitted candidates so the applier runs Properties once at the
    ''' actor level.
    '''
    ''' Engine semantics (verified vs dump v2):
    '''   - Each chunk OMOD has ModelPath != "" and AttachPointFormID → KYWD whose EditorID
    '''     matches a BSConnectPoint::Parents.Name in the actor skeleton NIF.
    '''   - The chunk renders at the socket's local transform on top of the bone Parent.
    '''   - OMODs without ModelPath but with Properties (or DirectProperties on the combination)
    '''     contribute Materials/Color swaps applied via OmodResolutionApplier formType="NPC_".
    '''
    ''' AttachPoint logging: KYWD records were not loaded by the legacy plugin filter
    ''' (SIGS_NPC_RENDERING did not include "KYWD" until 2026-05-10). With the fix in place
    ''' AttachPoint EditorIDs resolve and chunks mount at the correct sockets.
    '''
    ''' Skeleton merge: handled by PrepareSkeleton via BodyPartSkeletonResolver (BPTD.MODL
    ''' from RACE.GNAM). Replaces the legacy MergeRobotExtendedSkeletonsIfRobot filesystem
    ''' heuristic. Chunks mount correctly via BSConnectPoint and standard
    ''' SkeletonInstance.MergeAdditionalSkeleton pipeline.</summary>
    Private Sub CollectRobotChunkCandidates(state As NPCVisualState,
                                            candidates As List(Of MeshCandidate),
                                            ByRef order As Integer,
                                            warnings As List(Of String))
        ' [DIAG] Entry log — confirma estado de entrada del robot path.
        Dim stateFid = state.FormID
        Dim stateRace = state.RaceFormID
        Dim hasOT = state.HasObjectTemplate
        Dim otCount = If(state.ObjectTemplateCombinations Is Nothing, 0, state.ObjectTemplateCombinations.Count)
        Dim apSlotCount = If(state.AttachParentSlotFormIDs Is Nothing, 0, state.AttachParentSlotFormIDs.Count)
        Dim apSlotStr = If(state.AttachParentSlotFormIDs Is Nothing OrElse state.AttachParentSlotFormIDs.Count = 0, "[]",
                           "[" & String.Join(",", state.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & ObjectTemplateResolver.KywdEditorIdSafe(f, _pluginManager) & ")")) & "]")
        Logger.LogLazy(Function() $"[ROBOT-ENTRY] npc=0x{stateFid:X8} race=0x{stateRace:X8} hasOT={hasOT} combos={otCount} npcAPPR={apSlotCount}={apSlotStr}")

        ' Build a stub NPC_Data carrying the OBTE so we can re-use ResolveNpcCombinations.
        Dim stubNpc As New NPC_Data With {
            .FormID = state.FormID,
            .HasObjectTemplate = state.HasObjectTemplate,
            .RaceFormID = state.RaceFormID
        }
        For Each ch In state.ObjectTemplateCombinations
            stubNpc.ObjectTemplateCombinations.Add(ch)
        Next
        ' Propagate NPC.APPR — initial pool for the AP-filter inside ObjectTemplateResolver.
        ' RACE.APPR is read by the resolver itself via stubNpc.RaceFormID.
        If state.AttachParentSlotFormIDs IsNot Nothing Then
            stubNpc.AttachParentSlotFormIDs.AddRange(state.AttachParentSlotFormIDs)
        End If

        ' ctxKeywords: NPC robots typically don't get LVLI.LLKC propagation (they're not
        ' wrapped in OTFT). Pass empty so the resolver falls through to first-Default.
        Dim ctxKeywords As New List(Of UInteger)
        Dim resolution = ObjectTemplateResolver.ResolveNpcCombinations(stubNpc, ctxKeywords, _pluginManager)

        ' Delegate to shared OMOD chunk-mounting collector (robot + biped share capas 1+2:
        ' coord fix + socket disambig). Capa 3 (V2 SKEL-OVERRIDE) aplica a robot Y biped
        ' (gate Fase 2.5 removido).
        CollectOmodChunkCandidates(resolution, "NPC_", state, candidates, order, warnings)
    End Sub

    ''' <summary>Shared OMOD chunk-mounting candidate emit. Toma una CombinationResolution
    ''' ya construida (vía ResolveNpcCombinations o ResolveArmoCombinations) y emite los
    ''' MeshCandidates Attachment con host-scoped socket resolution. formType marca el
    ''' origen ("NPC_" robot, "ARMO" biped) y se propaga al candidate para downstream
    ''' filtering. La capa V2 SKEL-OVERRIDE NO vive aquí — se colecta en CollectV2PlanForShape
    ''' (shape loop) y se aplica en ApplyMountPlanForActor. Robot Y biped por igual.</summary>
    Private Sub CollectOmodChunkCandidates(resolution As ObjectTemplateResolver.CombinationResolution,
                                           formType As String,
                                           state As NPCVisualState,
                                           candidates As List(Of MeshCandidate),
                                           ByRef order As Integer,
                                           warnings As List(Of String))

        If resolution.IncludedOmods.Count = 0 AndAlso resolution.DirectProperties.Count = 0 Then
            Return
        End If

        ' Load the actor's skeleton NIF once and pre-index its BSConnectPoint::Parents by
        ' socket name (case-insens). Used to look up MountSocket transform per chunk.
        Dim socketsByName = LoadActorBSConnectPoints(state, warnings)

        ' [HOST-SCOPED-SNAPSHOT] skeletonSockets = SRC1+SRC2 sockets ANTES de que SRC3
        ' contribuya. Estos son los sockets del actor/skeleton root — el fallback final
        ' de la cadena host walk. Cualquier socket que un chunk publique vía SRC3 vive en
        ' su propio namespace (publisherSockets[omodFid]) y se resuelve consultando el
        ' host inmediato del consumer hacia arriba. El namespace flat global socketsByName
        ' se mantiene para callers legacy que aún no migraron, pero el robot path mount-
        ' lookup ya no lo consulta — usa host-scoped.
        Dim skeletonSockets As New Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)(socketsByName, StringComparer.OrdinalIgnoreCase)
        ' Per-publisher socket map: cada chunk publisher (OMOD FormID) tiene su propio
        ' diccionario de sockets que él publica vía BSConnectPoint::Parents. Sin merging
        ' con skeleton, sin FIRST-WINS — cada publisher tiene su namespace propio.
        ' Cada entry guarda PublisherSocketInfo (Socket + HostSocketGlobalT + flag de parent),
        ' computado UNA vez al indexing time, reusado por todos los consumers de ese host.
        ' Keyed por OMOD FormID asset-level: los sockets que un OMOD publica son los mismos
        ' independiente de apIdx (son propiedad del NIF, no de la instancia). La identidad
        ' por instancia (FormID, ApIdx) la lleva hostChainMap aparte.
        Dim publisherSockets As New Dictionary(Of UInteger, Dictionary(Of String, PublisherSocketInfo))

        ' Source 3 (runtime pre-mount): cada chunk en IncludedOmods puede exponer sub-sockets
        ' (BSConnectPoint::Parents en su NIF) que child chunks van a buscar para montarse.
        ' Estos sockets pueden vivir SOLO en el chunk NIF y no en RACE.ANAM/BPTD.MODL.
        ' Caso vivo Assaultron: TorsoAssaultron expone P-AssaultronArmorSlotTorsoFront/Rear,
        ' LegsAssaultron expone P-ModLegLeft/RightAssaultronArmorLow/Upper, HeadAssaultron
        ' expone P-HeadArmorAssaultron. Sin esta tercera fuente MOUNT-LOOKUP falla para los
        ' armors y caen al fallback __chunkAnchor__ con offset incorrecto.
        For preIdx = 0 To resolution.IncludedOmods.Count - 1
            Dim omodPre = resolution.IncludedOmods(preIdx)
            If omodPre Is Nothing OrElse String.IsNullOrEmpty(omodPre.ModelPath) Then Continue For
            Dim dictKeyPre = NormalizeDictionaryKeyWithMeshesPrefix(omodPre.ModelPath)
            Dim locPre As FilesDictionary_class.File_Location = Nothing
            If Not FilesDictionary_class.Dictionary.TryGetValue(dictKeyPre, locPre) Then Continue For
            Try
                Dim bytesPre = locPre.GetBytes()
                If bytesPre Is Nothing OrElse bytesPre.Length = 0 Then Continue For
                Dim nifPre As New Nifcontent_Class_Manolo()
                nifPre.Load_Manolo(bytesPre)
                Dim chunkParents = BSConnectPointReader.ReadParents(nifPre)
                ' [DIAG-CHAIN] Para cada sub-socket que el chunk expone, buscar el NiNode del
                ' parent_bone en la jerarquía interna del chunk. Si existe Y su chunk-world
                ' position difiere de actor.parent_bone.world, entonces el socket.local está
                ' relativo al chunk's internal view del bone (NO al actor's), y hay que
                ' encadenar via chunk's position cuando computamos M_mesh.
                For Each cpD In chunkParents
                    If cpD Is Nothing OrElse String.IsNullOrEmpty(cpD.ParentBoneName) Then Continue For
                    Try
                        Dim chunkParentNode = nifPre.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cpD.ParentBoneName)
                        Dim socketNm = cpD.Name, parentNm = cpD.ParentBoneName, omNmL = omodPre.EditorID
                        If chunkParentNode IsNot Nothing Then
                            Dim chunkParentWorld = Transform_Class.GetGlobalTransform(chunkParentNode, nifPre)
                            Dim cpwT = chunkParentWorld.Translation, cpwR = chunkParentWorld.Rotation
                            Dim sT = cpD.Translation
                            ' Chain-derived socket world = chunk.parent.world × socket.local (translation rough)
                            Dim chainImpliedX = cpwT.X + sT.X, chainImpliedY = cpwT.Y + sT.Y, chainImpliedZ = cpwT.Z + sT.Z
                            Logger.LogLazy(Function() $"[DIAG-CHAIN]   chunk '{omNmL}' exposes socket='{socketNm}' parent='{parentNm}' chunk.parent.world.T=({cpwT.X:F3},{cpwT.Y:F3},{cpwT.Z:F3}) socket.local.T=({sT.X:F3},{sT.Y:F3},{sT.Z:F3}) chain-implied socket world (T sum, no rotation)=({chainImpliedX:F3},{chainImpliedY:F3},{chainImpliedZ:F3})")
                        Else
                            Logger.LogLazy(Function() $"[DIAG-CHAIN]   chunk '{omNmL}' exposes socket='{socketNm}' parent='{parentNm}' chunk hierarchy has NO NiNode named '{parentNm}' → socket.local interpretado contra actor.parent")
                        End If
                    Catch exCH As Exception
                        Dim socketNm2 = cpD.Name, exMsg = exCH.Message
                        Logger.LogLazy(Function() $"[DIAG-CHAIN] EXCEPTION socket='{socketNm2}': {exMsg}")
                    End Try
                Next
                ' [HOST-SCOPED] Poblar publisherSockets[omodPre.FormID] con TODOS los sockets
                ' que este chunk publica — sin merging con skeleton, sin FIRST-WINS. El
                ' namespace del publisher es propio. Conflicts dentro del mismo publisher
                ' (mismo nombre dos veces en el mismo chunk) son inconsistencia local —
                ' loggear, mantener primero.
                '
                ' Por cada socket computamos HostSocketGlobalT EN EL ESPACIO DEL NIF DEL HOST:
                '   - Si parent.NiNode existe en este NIF: parent.global.compose(socket.local).
                '   - Si parent.NiNode NO existe (parent name no aparece en este NIF tree):
                '     ParentFoundInHostNif=False; consumer fallback al path skeleton.
                '   - Si parent name está vacío: tratamos como parent=root del host NIF
                '     (identity), semántica engine para sockets sin parent explícito.
                Dim hostMap As Dictionary(Of String, PublisherSocketInfo) = Nothing
                If Not publisherSockets.TryGetValue(omodPre.FormID, hostMap) Then
                    hostMap = New Dictionary(Of String, PublisherSocketInfo)(StringComparer.OrdinalIgnoreCase)
                    publisherSockets(omodPre.FormID) = hostMap
                End If
                For Each cpHost In chunkParents
                    Dim nmHost = If(cpHost.Name, "")
                    If String.IsNullOrEmpty(nmHost) Then Continue For
                    If hostMap.ContainsKey(nmHost) Then
                        Dim nmHostL = nmHost, omNmHostL = omodPre.EditorID
                        Logger.LogLazy(Function() $"[SOCKETS-PUBLISHER-DUP]   '{nmHostL}' duplicado dentro del mismo chunk '{omNmHostL}' — keep first")
                        Continue For
                    End If
                    Dim parentFound As Boolean = False
                    Dim parentGlobal As New Transform_Class() ' identity default = host NIF root
                    Dim parentNm = If(cpHost.ParentBoneName, "")
                    If String.IsNullOrEmpty(parentNm) Then
                        ' Parent vacío = parent implícito root del host NIF (identity).
                        parentFound = True
                    Else
                        Dim parentNode = nifPre.FindBlockByName(Of NiflySharp.Blocks.NiNode)(parentNm)
                        If parentNode IsNot Nothing Then
                            parentFound = True
                            parentGlobal = Transform_Class.GetGlobalTransform(parentNode, nifPre)
                        End If
                    End If
                    Dim socketLocalAsTransform As New Transform_Class With {
                        .Translation = cpHost.Translation,
                        .Rotation = BSConnectPointReader.QuatToMatrix33(cpHost.Rotation),
                        .Scale = If(cpHost.Scale > 0.0F, cpHost.Scale, 1.0F)
                    }
                    Dim hostSocketGlobal As Transform_Class = parentGlobal.ComposeTransforms(socketLocalAsTransform)
                    hostMap(nmHost) = New PublisherSocketInfo With {
                        .Socket = cpHost,
                        .HostSocketGlobalT = hostSocketGlobal,
                        .ParentFoundInHostNif = parentFound
                    }
                    Dim nmHostL2 = nmHost, omNmHostL2 = omodPre.EditorID, pfL = parentFound
                    Dim hsT = hostSocketGlobal.Translation
                    Logger.LogLazy(Function() $"[PUBLISHER-SOCKET-INDEX] chunk='{omNmHostL2}' socket='{nmHostL2}' parent='{parentNm}' parentFoundInHostNif={pfL} hostSocketGlobal.T=({hsT.X:F3},{hsT.Y:F3},{hsT.Z:F3})")
                Next
            Catch exPre As Exception
                Dim msgL = exPre.Message, omodNmL = omodPre.EditorID
                Logger.LogLazy(Function() $"[SOCKETS-SRC3-CHUNK] EXCEPTION reading chunk '{omodNmL}': {msgL}")
            End Try
        Next

        ' Walk IncludedOmods (indexed: parallel list IncludedOmodApIdx carries the apIdx per emit).
        ' Each OMOD with ModelPath = chunk to mount; OMODs without ModelPath contribute Properties
        ' only (resolved en bloque por el applier al final).
        '
        ' Socket lookup rule (verified empirically against Codsworth host parents in fo4lib.log):
        '   1. The apEditorId is the OMOD.AttachPoint KYWD EditorID (e.g. 'ap_Bot_ArmsTypeA1').
        '      Host sockets use 'P-X' / 'P-X|N' naming convention. Strip the 'ap_Bot_' or 'ap_'
        '      prefix to get the base name (e.g. 'ArmsTypeA1') — host sockets are 'P-<base>'.
        '   2. Try 'P-<base>|<apIdx>' first (multi-instance like P-ArmsTypeA1|1, P-ModSlotB|2).
        '   3. Fall back to 'P-<base>' (single-instance — host has no |N suffix).
        ' Both shapes coexist in vanilla: TorsoHandy → P-BotCore (no suffix), Arm_Right_Flamer
        ' → P-ArmsTypeA1|1 (suffixed). The lookup tries indexed first and falls back.
        ' [HOST-SCOPED ORDINAL] hostChainMap[ordinal] = hostOrdinal del padre inmediato.
        ' Identidad por ordinal monotónico (expand-time, antes de cualquier dedup) garantiza
        ' que el mismo OMOD asset reutilizado bajo hosts distintos NO colapsa identidades.
        ' Ordinal 0 reservado para skeleton root sentinel.
        Dim hostChainMap As New Dictionary(Of Integer, Integer)
        For hi = 0 To resolution.IncludedOmods.Count - 1
            Dim omodHi = resolution.IncludedOmods(hi)
            If omodHi Is Nothing Then Continue For
            Dim ordHi As Integer = If(hi < resolution.IncludedOmodInstanceOrdinal.Count, resolution.IncludedOmodInstanceOrdinal(hi), 0)
            Dim hostOrdHi As Integer = If(hi < resolution.IncludedOmodHostInstanceOrdinal.Count, resolution.IncludedOmodHostInstanceOrdinal(hi), 0)
            If ordHi = 0 Then Continue For ' unslotted properties-only — no host concept

            Dim existingHL As Integer = Nothing

            If hostChainMap.TryGetValue(ordHi, existingHL) Then
                Dim ordHiL = ordHi, newHL = hostOrdHi
                Logger.LogLazy(Function() $"[HOSTCHAIN-OVERWRITE] ordinal={ordHiL} existing.host={existingHL} new.host={newHL} — bug de implementación: ordinal monotónico debería ser único")
            End If
            hostChainMap(ordHi) = hostOrdHi
        Next

        Dim chunkCount As Integer = 0
        For i = 0 To resolution.IncludedOmods.Count - 1
            Dim omod = resolution.IncludedOmods(i)
            Dim apIdx = If(i < resolution.IncludedOmodApIdx.Count, resolution.IncludedOmodApIdx(i), CByte(0))
            Dim ord As Integer = If(i < resolution.IncludedOmodInstanceOrdinal.Count, resolution.IncludedOmodInstanceOrdinal(i), 0)
            Dim hostOrd As Integer = If(i < resolution.IncludedOmodHostInstanceOrdinal.Count, resolution.IncludedOmodHostInstanceOrdinal(i), 0)
            Dim hostFid As UInteger = If(i < resolution.IncludedOmodHostFormID.Count, resolution.IncludedOmodHostFormID(i), 0UI)
            Dim hostApIdx As Byte = If(i < resolution.IncludedOmodHostApIdx.Count, resolution.IncludedOmodHostApIdx(i), CByte(0))
            If omod Is Nothing Then Continue For
            If String.IsNullOrEmpty(omod.ModelPath) Then Continue For ' property-only OMODs
            ' Note: vanilla rusty/variant OMODs (Bot_ArmLeftProtectronRusty1 etc.) have
            ' FormType=NONE while the originals have FormType=NPC_. Filtering by FormType
            ' would drop the variants — they render in-game, so we accept any FormType here.

            Dim apEditorId = ResolveAttachPointEditorId(omod.AttachPointFormID)
            ' Host-scoped resolution: walk host chain por ORDINAL hasta caer en skeleton root.
            ' Devuelve PublisherSocketInfo (con HostSocketGlobalT precomputado) + matchedHostOrdinal —
            ' el consumer no re-descubre el publisher después.
            Dim resolvedInfo As PublisherSocketInfo = Nothing
            Dim matchedHostOrdResolved As Integer = 0
            Dim matchedHostFid As UInteger = 0UI
            Dim matchedHostAi As Byte = 0
            Dim socket = ResolveMountSocketHostScoped(apEditorId, apIdx, hostOrd, publisherSockets, hostChainMap, resolution, skeletonSockets, resolvedInfo, matchedHostOrdResolved, matchedHostFid, matchedHostAi)

            ' [SKELETON-FALLBACK-SOCKET] Resolución paralela contra skeletonSockets (SRC1+SRC2)
            ' para Path B. El skeleton publica P-X con ParentBoneName usando nomenclatura
            ' actor-skel (indexed: Arm1|0, Arm1|1, etc.), distinto al publisher chunk socket
            ' que usa chunk-internal naming sin suffix. Path B (chunks sin C-X NiNode interno)
            ' usa ESTE socket para que ResolveEffectiveWorld(parentBone) encuentre el bone
            ' indexed correcto en actor.skel. Nothing si el skeleton no publica este socket
            ' (raro — Path B caería al publisher socket como último recurso, loggeado).
            ' Lookup: indexed (P-base|apIdx) primero, plain (P-base) fallback.
            Dim skelFallbackSocket As BSConnectPointReader.ConnectPointInfo = Nothing
            If Not String.IsNullOrEmpty(apEditorId) Then
                Dim baseNm_fb = apEditorId
                If baseNm_fb.StartsWith("ap_Bot_", StringComparison.OrdinalIgnoreCase) Then
                    baseNm_fb = baseNm_fb.Substring("ap_Bot_".Length)
                ElseIf baseNm_fb.StartsWith("ap_", StringComparison.OrdinalIgnoreCase) Then
                    baseNm_fb = baseNm_fb.Substring("ap_".Length)
                End If
                Dim indexed_fb = $"P-{baseNm_fb}|{apIdx}"
                Dim plain_fb = $"P-{baseNm_fb}"
                If Not skeletonSockets.TryGetValue(indexed_fb, skelFallbackSocket) Then
                    skeletonSockets.TryGetValue(plain_fb, skelFallbackSocket)
                End If
            End If

            Dim apIdxLog = apIdx
            Dim apEditorLog = apEditorId
            Dim socketLocalForLog = socket
            Dim ordLog = ord, hostOrdLog = hostOrd, matchedOrdLog = matchedHostOrdResolved
            Dim hostFidLog = hostFid, hostApIdxLog = hostApIdx, matchedHostFidLog = matchedHostFid
            Dim skelFbForLog = skelFallbackSocket
            Logger.LogLazy(Function() $"[ROBOT-CHUNK] omod={omod.EditorID}({omod.FormID:X8}) ord={ordLog} apEditor='{apEditorLog}' apIdx={apIdxLog} host=(ord={hostOrdLog},0x{hostFidLog:X8},apIdx={hostApIdxLog}) matchedHost=(ord={matchedOrdLog},0x{matchedHostFidLog:X8}) → socket={If(socketLocalForLog Is Nothing, "NOT-FOUND", $"'{socketLocalForLog.Name}' onBone='{socketLocalForLog.ParentBoneName}'")} skelFallback={If(skelFbForLog Is Nothing, "NOT-FOUND", $"'{skelFbForLog.Name}' onBone='{skelFbForLog.ParentBoneName}'")}")

            Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(omod.ModelPath)
            candidates.Add(New MeshCandidate With {
                .DictKey = dictKey,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MeshCandidateKind.Attachment,
                .SourceFormID = omod.FormID,
                .ChunkOmodFormID = omod.FormID,
                .AttachPointKywdEditorId = apEditorId,
                .MountApIdx = apIdx,
                .MountSocket = socket,
                .SkeletonFallbackSocket = skelFallbackSocket,
                .ChunkInstanceOrdinal = ord,
                .MountHostOmodFormID = hostFid,
                .MountHostApIdx = hostApIdx,
                .MountHostInstanceOrdinal = hostOrd,
                .MatchedHostOmodFormID = matchedHostFid,
                .MatchedHostApIdx = matchedHostAi,
                .MatchedHostInstanceOrdinal = matchedHostOrdResolved,
                .ResolvedHostSocketGlobalT = resolvedInfo?.HostSocketGlobalT,
                .ParentFoundInMatchedHostNif = resolvedInfo IsNot Nothing AndAlso resolvedInfo.ParentFoundInHostNif,
                .OmodResolution = resolution,
                .OmodResolutionFormType = formType,
                .Order = order
            })
            order += 1
            chunkCount += 1
        Next

        ' [PRE-PASS A_HOST] La pre-pass que computa ChunkToActor por candidate corre más
        ' tarde, en V2 setup, donde el SkeletonInstance (inst) está disponible para resolver
        ' actor.parentBone.world en el path fallback (Path B). Ver PopulateRobotChunkChunkToActor
        ' llamado en RegisterRobotMountSockets / antes del V2 shape loop. Aquí solo persistimos
        ' las estructuras necesarias en renderData para que la pre-pass las pueda consumir.

    End Sub

    ''' <summary>Hierarchy depth de un bone en actor.skel — cuenta cuántos parents tiene hasta
    ''' el root. Usado para sortear shape bones en orden top-down antes de aplicar overrides
    ''' (parent-first). Sin esto, si los shape bones del NIF están en orden no-hierarchical
    ''' (ej. LEFT arm Protectron: LUpperArmTwist=child primero, LClavicleTwist=root al final),
    ''' los overrides children fires antes que parent → cuando parent override fires → cascade
    ''' al children rompe su world (cascade drift). Procesar en depth-order garantiza que cada
    ''' child se overridea contra el parent.world ya finalizado.</summary>
    Private Function GetBoneHierarchyDepth(hb As HierarchiBone_class) As Integer
        If hb Is Nothing Then Return 0
        Dim depth As Integer = 0
        Dim current = hb
        Dim safety As Integer = 0
        While current.Parent IsNot Nothing AndAlso safety < 200
            depth += 1
            current = current.Parent
            safety += 1
        End While
        Return depth
    End Function

    ''' <summary>Quita el sufijo de instancia numérico tras el último "|" (p.ej. "C-X|2" → "C-X").
    ''' Si no hay "|", o no hay dígitos tras el "|", o algún char tras el "|" no es dígito,
    ''' devuelve s sin cambios.</summary>
    Private Shared Function StripInstanceSuffix(s As String) As String
        If String.IsNullOrEmpty(s) Then Return s
        Dim pp = s.LastIndexOf("|"c)
        If pp <= 0 OrElse pp >= s.Length - 1 Then Return s
        For Each c In s.Substring(pp + 1)
            If Not Char.IsDigit(c) Then Return s
        Next
        Return s.Substring(0, pp)
    End Function

    ''' <summary>COLECTA el plan V2 SKEL-OVERRIDE para una shape con mount socket: computa cxNode,
    ''' G_CX, parentBoneWorld, M_mesh, y por cada bone (W_B = A × G_B, A = M_mesh × inv(G_CX)) agrega un
    ''' <see cref="MountDesiredWorldEntry"/> al plan <see cref="PreviewResolutionResult.MountDesiredWorlds"/>
    ''' (con <c>TargetSkel</c>) más actualiza <paramref name="chunkWBHistory"/> para la cascade
    ''' cross-shape. NO aplica MountDelta — eso lo hace <see cref="ApplyMountPlanForActor"/> en
    ''' orden topológico tras el shape loop (fuente única de verdad para initial render + pose-dirty).
    ''' Si cxNode no se encuentra en chunk NIF, emite DIAG-BIND-BAKE diagnostics.
    ''' Try/Catch envolvente — excepciones se loggean sin propagar al shape loop.</summary>
    Private Sub CollectV2PlanForShape(shape As IRenderableShape,
                                       socket As BSConnectPointReader.ConnectPointInfo,
                                       targetSkel As SkeletonInstance,
                                       renderData As PreviewResolutionResult,
                                       wbHistory As Dictionary(Of String, Transform_Class),
                                       isRobotMount As Boolean)
        If shape.ShapeBones Is Nothing OrElse shape.ShapeBoneTransforms Is Nothing Then Return
        Try
            ' Derive cxName from the actual mount socket (counterpart of socket.Name).
            ' El chunk's BSConnectPoint::Children PointName puede ser inconsistente con el
            ' socket donde OBTE lo monta: ej. HeadArmorProtectron.nif (clean) declara
            ' Children=["C-Head"] pero se monta en P-HeadArmorProtectron. Usar el cxName
            ' del chunk hace que V2 elija G_CX de un NiNode posicionado para OTRO frame
            ' de attachment (C-Head a altura de cabeza vs C-HeadArmorProtectron a altura
            ' del helmet socket) → A equivocado → casco rotado/caído. OBTE es autoritativo.
            ' Convención canónica (per BSConnectPointBoneInjector.TryGetSocketCounterpartName):
            ' "P-X" → "C-X", "P_X" → "C_X".
            Dim cxName As String = If(socket IsNot Nothing, BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(socket.Name), "")

            If Not String.IsNullOrEmpty(cxName) Then
                ' Find C-X NiNode (try exact, fallback suffix strip).
                Dim cxNode As NiflySharp.Blocks.NiNode = shape.NifContent.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cxName)
                If cxNode Is Nothing Then
                    Dim cxNormSearch = StripInstanceSuffix(cxName)
                    For Each blk In shape.NifContent.Blocks
                        Dim cand = TryCast(blk, NiflySharp.Blocks.NiNode)
                        If cand Is Nothing Then Continue For
                        Dim candNm = If(cand.Name?.String, "")
                        If String.Equals(StripInstanceSuffix(candNm), cxNormSearch, StringComparison.OrdinalIgnoreCase) Then
                            cxNode = cand
                            Exit For
                        End If
                    Next
                End If

                If cxNode IsNot Nothing Then
                    Dim G_CX = Transform_Class.GetGlobalTransform(cxNode, shape.NifContent)

                    ' Compute P_world (M_mesh) desde el socket dict-existing.
                    '
                    ' UNIFICACIÓN: parentBoneWorld usa ResolveEffectiveWorld para respetar V2
                    ' de parent chunks. Si un chunk anterior corrió V2 sobre socket.ParentBoneName,
                    ' su W_B vive en chunkWBHistory[ParentBoneName] y representa la posición real
                    ' del bone post-V2. Sin esto, V2 sobre chunks que montan en V2-corregidos
                    ' usaría posiciones desactualizadas y la cascada se rompería.
                    Dim parentBoneWorld As Transform_Class = ResolveEffectiveWorld(wbHistory, targetSkel, socket.ParentBoneName)
                    If isRobotMount AndAlso Logger.Enabled Then
                        Dim hasOverride = wbHistory.ContainsKey(socket.ParentBoneName)
                        Dim shL = shape.ShapeName, pbnL = socket.ParentBoneName, hoL = hasOverride
                        Dim pwT = parentBoneWorld.Translation
                        Logger.LogLazy(Function() $"[V2-MMESH] shape='{shL}' parent_bone='{pbnL}' effective_world.T=({pwT.X:F3},{pwT.Y:F3},{pwT.Z:F3}) (chunkWBHistory-override={hoL})")
                    End If

                    ' socket.Translation YA viene del resolver en Parents space (BSConnectPoint::Parents
                    ' = chunk-source declaration).

                    ' [HOST-SCOPED PATH A] Si la pre-pass A_HOST ya computó cand.ChunkToActor
                    ' (Path A: M_mesh = host.ChunkToActor × HostSocketGlobalT en espacio del NIF
                    ' del host), V2 deriva M_mesh = A × G_CX para mantener consistencia con
                    ' downstream checks (ACTOR-RIG vs MODULE-RIG depende de M_mesh.T). Esto
                    ' reemplaza el cálculo legacy parentBoneWorld × socketLocal solo cuando
                    ' la pre-pass aplicó Path A — sino el path skeleton actual sigue.
                    Dim _candForShape As MeshCandidate = Nothing
                    renderData.ShapeCandidate.TryGetValue(shape, _candForShape)

                    Dim M_mesh As Transform_Class
                    If _candForShape IsNot Nothing AndAlso _candForShape.ChunkToActor IsNot Nothing Then
                        ' Path A: A ya fue computado por la pre-pass usando coord system del
                        ' host NIF correctamente. Derivar M_mesh = A × G_CX para mantener
                        ' compatibilidad con downstream checks (ACTOR-RIG vs MODULE-RIG).
                        M_mesh = _candForShape.ChunkToActor.ComposeTransforms(G_CX)
                        Dim shL_pa = shape.ShapeName
                        Dim mmTl = M_mesh.Translation
                        Logger.LogLazy(Function() $"[V2-MMESH-PATH-A] shape='{shL_pa}' using pre-pass ChunkToActor, M_mesh.T=({mmTl.X:F3},{mmTl.Y:F3},{mmTl.Z:F3})")
                    Else
                        ' Path B (legacy parentBone × socket.local) ELIMINADO: confirmado INALCANZABLE
                        ' (barrido 4473 NPCs = 0 disparos, 2026-06-14). Fail-loud: si ChunkToActor no
                        ' resolvió (cadena de hosts rota/ciclo), gritarlo y saltar el shape — nunca
                        ' computar el mount por el camino no-canónico en silencio.
                        Dim pbReason As String = If(_candForShape Is Nothing, "candForShape=Nothing", "ChunkToActor=Nothing")
                        Dim pbShape As String = shape.ShapeName, pbSocket As String = If(socket.Name, "?")
                        Logger.LogLazy(Function() $"[PATH-B-IMPOSIBLE] shape='{pbShape}' socket='{pbSocket}' reason={pbReason} — ChunkToActor no resuelto, shape salteado")
                        MessageBox.Show("PATH B IMPOSIBLE — no debería pasar." & vbCrLf & vbCrLf &
                                        "shape  = " & pbShape & vbCrLf &
                                        "socket = " & pbSocket & vbCrLf &
                                        "razón  = " & pbReason & vbCrLf & vbCrLf &
                                        "La cadena de hosts no resolvió ChunkToActor. Shape salteado.",
                                        "Path B imposible", MessageBoxButtons.OK, MessageBoxIcon.Error)
                        Return
                    End If

                    ' [DIAG-CHUNKROOT] Hipótesis: GetGlobalTransform incluye chunkRoot.local
                    ' en su composición. Per BSConnectPointBoneInjector.vb:137-140 el
                    ' chunkRoot.local es "scene-viewer rotation del modelador, NO parte del
                    ' attachment". Si chunkRoot.local NO es identity, V2 (y SKIP) lo metería
                    ' espurio en G_CX / G_B / W_B → rotación/translation extra en render.
                    ' Loguear con-root vs stripped para confirmar la magnitud del impacto.
                    ' Corre PRE-skip así también vemos arms (que van a SKIP).
                    If isRobotMount AndAlso Logger.Enabled Then
                        Try
                            Dim chunkRootNode = shape.NifContent.GetRootNode()
                            Dim chunkRootLocal As Transform_Class
                            If chunkRootNode IsNot Nothing Then
                                chunkRootLocal = New Transform_Class(chunkRootNode)
                            Else
                                chunkRootLocal = New Transform_Class()
                            End If
                            Dim chunkRootIsIdent = chunkRootLocal.Equals(New Transform_Class())
                            Dim invChunkRoot = chunkRootLocal.Inverse()
                            Dim G_CX_stripped = G_CX.ComposeTransforms(invChunkRoot)
                            Dim invGCXStripped = G_CX_stripped.Inverse()
                            Dim A_with = M_mesh.ComposeTransforms(G_CX.Inverse())
                            Dim A_stripped = M_mesh.ComposeTransforms(invGCXStripped)
                            Dim shL_cr = shape.ShapeName, cxL_cr = cxName, isIdL = chunkRootIsIdent
                            Dim crT = chunkRootLocal.Translation, crR = chunkRootLocal.Rotation
                            Dim gcxT = G_CX.Translation, gcxR = G_CX.Rotation
                            Dim gcxStrT = G_CX_stripped.Translation, gcxStrR = G_CX_stripped.Rotation
                            Dim aT_cr = A_with.Translation, aR_cr = A_with.Rotation
                            Dim asT = A_stripped.Translation, asR = A_stripped.Rotation
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT] shape='{shL_cr}' cx='{cxL_cr}' chunkRoot.local IDENTITY={isIdL} T=({crT.X:F3},{crT.Y:F3},{crT.Z:F3}) R=[{crR.M11:F3},{crR.M12:F3},{crR.M13:F3}|{crR.M21:F3},{crR.M22:F3},{crR.M23:F3}|{crR.M31:F3},{crR.M32:F3},{crR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   G_CX(with-root).T=({gcxT.X:F3},{gcxT.Y:F3},{gcxT.Z:F3}) R=[{gcxR.M11:F3},{gcxR.M12:F3},{gcxR.M13:F3}|{gcxR.M21:F3},{gcxR.M22:F3},{gcxR.M23:F3}|{gcxR.M31:F3},{gcxR.M32:F3},{gcxR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   G_CX(stripped).T=({gcxStrT.X:F3},{gcxStrT.Y:F3},{gcxStrT.Z:F3}) R=[{gcxStrR.M11:F3},{gcxStrR.M12:F3},{gcxStrR.M13:F3}|{gcxStrR.M21:F3},{gcxStrR.M22:F3},{gcxStrR.M23:F3}|{gcxStrR.M31:F3},{gcxStrR.M32:F3},{gcxStrR.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   A(with-root).T=({aT_cr.X:F3},{aT_cr.Y:F3},{aT_cr.Z:F3}) R=[{aR_cr.M11:F3},{aR_cr.M12:F3},{aR_cr.M13:F3}|{aR_cr.M21:F3},{aR_cr.M22:F3},{aR_cr.M23:F3}|{aR_cr.M31:F3},{aR_cr.M32:F3},{aR_cr.M33:F3}]")
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]   A(stripped).T=({asT.X:F3},{asT.Y:F3},{asT.Z:F3}) R=[{asR.M11:F3},{asR.M12:F3},{asR.M13:F3}|{asR.M21:F3},{asR.M22:F3},{asR.M23:F3}|{asR.M31:F3},{asR.M32:F3},{asR.M33:F3}]")
                            For sbiCR = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                                Dim niNCR = TryCast(shape.ShapeBones(sbiCR), NiflySharp.Blocks.NiNode)
                                If niNCR Is Nothing Then Continue For
                                Dim bnNmCR = If(niNCR.Name?.String, "")
                                If String.IsNullOrEmpty(bnNmCR) Then Continue For
                                Dim G_B_with = Transform_Class.GetGlobalTransform(niNCR, shape.NifContent)
                                Dim G_B_stripped = G_B_with.ComposeTransforms(invChunkRoot)
                                Dim WB_with = A_with.ComposeTransforms(G_B_with)
                                Dim WB_stripped = A_stripped.ComposeTransforms(G_B_stripped)
                                Dim wT_w = WB_with.Translation, wT_s = WB_stripped.Translation
                                Dim diff = Math.Sqrt((wT_w.X - wT_s.X) ^ 2 + (wT_w.Y - wT_s.Y) ^ 2 + (wT_w.Z - wT_s.Z) ^ 2)
                                Dim shLb = shape.ShapeName, bnLb = bnNmCR, dL = diff
                                Logger.LogLazy(Function() $"[DIAG-CHUNKROOT]     bone='{bnLb}' W_B(with).T=({wT_w.X:F3},{wT_w.Y:F3},{wT_w.Z:F3}) W_B(stripped).T=({wT_s.X:F3},{wT_s.Y:F3},{wT_s.Z:F3}) |diff|={dL:F3}")
                            Next
                        Catch exCR As Exception
                            Dim shL_cr = shape.ShapeName, msgL = exCR.Message
                            Logger.LogLazy(Function() $"[DIAG-CHUNKROOT] shape='{shL_cr}' EXCEPTION: {msgL}")
                        End Try
                    End If

                    ' (discriminador ACTOR-RIG/MODULE-RIG removido: ambas ramas computaban W_B = A × G_B idéntico; una sola rama abajo)

                    Dim invGCX = G_CX.Inverse()
                    ' A = inv(G_CX) × M_mesh in row-vec composition = M_mesh.Compose(invGCX)
                    Dim A = M_mesh.ComposeTransforms(invGCX)

                    Dim reskinCount As Integer = 0
                    Dim skipCount As Integer = 0
                    ' [DEPTH-ORDER] Sortear shape bones por hierarchy depth en actor.skel
                    ' (parent primero) antes de aplicar overrides. Sin esto, cascade drift
                    ' rompe arms con NIF order non-hierarchical.
                    Dim boneList_mod As New List(Of Tuple(Of Integer, NiflySharp.Blocks.NiNode, HierarchiBone_class, Integer))
                    Dim seenBones_mod As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                    For sbi_pre2 = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                        Dim niN_pre2 = TryCast(shape.ShapeBones(sbi_pre2), NiflySharp.Blocks.NiNode)
                        If niN_pre2 Is Nothing Then Continue For
                        Dim bnName_pre2 = If(niN_pre2.Name?.String, "")
                        If String.IsNullOrEmpty(bnName_pre2) Then Continue For
                        Dim hb_pre2 As HierarchiBone_class = Nothing
                        If Not targetSkel.SkeletonDictionary.TryGetValue(bnName_pre2, hb_pre2) Then
                            skipCount += 1
                            Continue For
                        End If
                        If seenBones_mod.Add(bnName_pre2) Then
                            Dim depth_pre2 = GetBoneHierarchyDepth(hb_pre2)
                            boneList_mod.Add(Tuple.Create(sbi_pre2, niN_pre2, hb_pre2, depth_pre2))
                        End If
                        ' [CHAIN-INTERMEDIATES] Walk parent chain de esta shape bone via chunk NIF
                        ' tree (GetParentNode). Para cada parent intermedio (hasta C-X), si su
                        ' nombre está en actor.SkeletonDictionary, agregarlo al boneList. Cubre
                        ' HeadAssaultron (HeadNod intermedio entre Neck y HeadTwist en chain[1]),
                        ' y cualquier otro chunk MODULE-RIG con bones intermedios no declarados
                        ' como shape bones. Depth-sort más abajo procesa todo top-down → sin
                        ' cascade drift. Idéntico al patrón ACTOR-RIG.
                        Dim parentNode_mod = TryCast(shape.NifContent.GetParentNode(niN_pre2), NiflySharp.Blocks.NiNode)
                        Dim safetyHops_mod As Integer = 0
                        While parentNode_mod IsNot Nothing AndAlso Not ReferenceEquals(parentNode_mod, cxNode) AndAlso safetyHops_mod < 20
                            Dim parentNm_pre2 = If(parentNode_mod.Name?.String, "")
                            If Not String.IsNullOrEmpty(parentNm_pre2) Then
                                Dim parentHb_pre2 As HierarchiBone_class = Nothing
                                If targetSkel.SkeletonDictionary.TryGetValue(parentNm_pre2, parentHb_pre2) AndAlso seenBones_mod.Add(parentNm_pre2) Then
                                    Dim depthP_pre2 = GetBoneHierarchyDepth(parentHb_pre2)
                                    boneList_mod.Add(Tuple.Create(-1, parentNode_mod, parentHb_pre2, depthP_pre2))
                                End If
                            End If
                            parentNode_mod = TryCast(shape.NifContent.GetParentNode(parentNode_mod), NiflySharp.Blocks.NiNode)
                            safetyHops_mod += 1
                        End While
                    Next
                    ' [CHUNK-TREE-FULL] El árbol del chunk COMPLETO define la distribución de Bethesda:
                    ' además de los skinned bones + sus cadenas, escribir TODO NiNode del chunk NIF que
                    ' exista en el actor (ramas hermanas y el propio C-X). Caso probado: TorsoAssaultron
                    ' trae LClavicle/RClavicle como nodos NO skinneados (ramas de Chest, fuera de las
                    ' cadenas de Spine) con el local DESPLEGADO (5.942,−4.773,2.658) == la constante que
                    ' juegan los clips; sin escribirlos, el despliegue entero del brazo caía como mount
                    ' sobre el primer skinned del chunk de brazo (LClavicleTwist +18.59) — distribución
                    ' que NO es la de Bethesda y dobla la cadena al animar. El C-X (W = A×G_CX = socket
                    ' publicado, ej. P-Head==(12.391,−3.921)==constante del clip) también se escribe:
                    ' el hueso socket VIVE donde su P-X lo publica.
                    For Each blk_mod In shape.NifContent.Blocks
                        Dim treeNode_mod = TryCast(blk_mod, NiflySharp.Blocks.NiNode)
                        If treeNode_mod Is Nothing OrElse treeNode_mod.Name Is Nothing Then Continue For
                        Dim treeNm_mod = If(treeNode_mod.Name.String, "")
                        If String.IsNullOrEmpty(treeNm_mod) Then Continue For
                        ' ⛔ Nodos con sufijo de instancia '|<dígitos>' EXCLUIDOS del tree-walk: los chunks
                        ' multi-instancia (ModTorsoHandyEye/ArmsTypeA1 ×3) comparten UN NIF cuyos nodos
                        ' se llaman '...|0' FIJO — escribirlos por nombre apila las 3 instancias en el
                        ' socket |0 (regresión: ojos mezclados, brazos corridos). Esos huesos los maneja
                        ' el path skinned+cadenas, que sí tiene el mapeo apIdx por instancia.
                        ' Usa el MISMO discriminador '|<dígitos>' que StripInstanceSuffix / apIdx-sub (antes
                        ' era IndexOf("|") crudo = cualquier pipe; verificado 2026-06-14: 0 nombres con
                        ' sufijo no-numérico en toda la data → el cambio es no-regresivo y consistente).
                        If StripInstanceSuffix(treeNm_mod) <> treeNm_mod Then Continue For
                        Dim treeHb_mod As HierarchiBone_class = Nothing
                        If targetSkel.SkeletonDictionary.TryGetValue(treeNm_mod, treeHb_mod) AndAlso seenBones_mod.Add(treeNm_mod) Then
                            boneList_mod.Add(Tuple.Create(-1, treeNode_mod, treeHb_mod, GetBoneHierarchyDepth(treeHb_mod)))
                        End If
                    Next
                    boneList_mod.Sort(Function(x_sort2, y_sort2) x_sort2.Item4.CompareTo(y_sort2.Item4))
                    For Each entry_mod In boneList_mod
                        Dim sbi = entry_mod.Item1
                        Dim niN = entry_mod.Item2
                        Dim actorBhb = entry_mod.Item3
                        Dim boneName = actorBhb.BoneName
                        Dim actor_B_world = actorBhb.OriginalGetGlobalTransform

                        ' [CHUNK-ACCUMULATION] Si bone fue reskin-eado por chunk previo, loggear
                        ' delta entre actor.world (que usamos para corregir) y prev_W_B (donde el
                        ' chunk previo realmente puso el geometry). Si delta ≠ 0, este sub-chunk
                        ' está usando referencia stale.
                        Dim prevWB As Transform_Class = Nothing
                        If isRobotMount AndAlso wbHistory.TryGetValue(boneName, prevWB) AndAlso prevWB IsNot Nothing Then
                            Dim aT0 = actor_B_world.Translation, pT0 = prevWB.Translation
                            Dim dX = pT0.X - aT0.X, dY = pT0.Y - aT0.Y, dZ = pT0.Z - aT0.Z
                            Dim bnL0 = boneName, shL0 = shape.ShapeName
                            Logger.LogLazy(Function() $"[CHUNK-ACCUMULATION] shape='{shL0}' bone='{bnL0}' actor.world=({aT0.X:F3},{aT0.Y:F3},{aT0.Z:F3}) prevChunkWB=({pT0.X:F3},{pT0.Y:F3},{pT0.Z:F3}) delta=({dX:F3},{dY:F3},{dZ:F3})")
                        End If

                        ' G_B desde el chunk NIF tree (no desde inv(bind)).
                        Dim G_B = Transform_Class.GetGlobalTransform(niN, shape.NifContent)
                        ' W_B = G_B × A (in row-vec composition).
                        Dim W_B = A.ComposeTransforms(G_B)

                        ' Acumular W_B en history para que sub-chunks posteriores puedan compararse
                        ' (cascade cross-shape en colección).
                        wbHistory(boneName) = W_B
                        ' [MOUNTDELTA-PLAN] V2 SOLO colecta el plan; el apply lo hace
                        ' ApplyMountPlanForActor en orden topológico tras el shape loop.
                        renderData.MountDesiredWorlds.Add(New MountDesiredWorldEntry With {
                            .BoneName = actorBhb.BoneName,
                            .DesiredWorld = W_B,
                            .ContextLabel = "V2-MODULE-" & shape.ShapeName,
                            .TargetSkel = targetSkel
                        })
                        reskinCount += 1
                        If isRobotMount AndAlso Logger.Enabled Then
                            Dim sbiL = sbi, bnL = boneName, shL = shape.ShapeName
                            Dim wBT = W_B.Translation, gBT = G_B.Translation
                            Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' bone[{sbiL}] '{bnL}' G_B.T=({gBT.X:F3},{gBT.Y:F3},{gBT.Z:F3}) W_B.T=({wBT.X:F3},{wBT.Y:F3},{wBT.Z:F3}) → plan entry collected")
                        End If
                    Next
                    If isRobotMount AndAlso Logger.Enabled Then
                        Dim rcL = reskinCount, skL = skipCount, shL = shape.ShapeName, cxL = cxName
                        Dim AT = A.Translation, GCXT = G_CX.Translation
                        Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' cx='{cxL}' G_CX.T=({GCXT.X:F3},{GCXT.Y:F3},{GCXT.Z:F3}) A.T=({AT.X:F3},{AT.Y:F3},{AT.Z:F3}) summary: reskin={rcL} skip={skL}")
                    End If
                ElseIf isRobotMount AndAlso Logger.Enabled Then
                    Dim shL = shape.ShapeName, cxL = cxName
                    Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' cx='{cxL}' SKIP: C-X NiNode not found in chunk NIF tree")

                    ' [DIAG-BIND-BAKE] Para chunks SKIP (sin C-X), comparar inv(bind) vs actor.bone.world
                    ' vs actor.parent_bone.world × socket.local (M_mesh). Permite ver empíricamente si el
                    ' bind tiene baked SOLO bone (Assaultron, needs fix) o bone+socket (Protectron, OK as-is).
                    Try
                        Dim parentBoneHbDiag As HierarchiBone_class = Nothing
                        If targetSkel.SkeletonDictionary.TryGetValue(socket.ParentBoneName, parentBoneHbDiag) Then
                            Dim parentBoneWorldDiag = parentBoneHbDiag.OriginalGetGlobalTransform
                            Dim socketLocalDiag As New Transform_Class With {
                                .Translation = socket.Translation,
                                .Rotation = BSConnectPointReader.QuatToMatrix33(socket.Rotation),
                                .Scale = If(socket.Scale > 0.0F, socket.Scale, 1.0F)
                            }
                            Dim mMeshDiag = parentBoneWorldDiag.ComposeTransforms(socketLocalDiag)
                            Dim mmT_outer = mMeshDiag.Translation

                            For sbiD = 0 To Math.Min(shape.ShapeBones.Count, shape.ShapeBoneTransforms.Count) - 1
                                Dim niN = TryCast(shape.ShapeBones(sbiD), NiflySharp.Blocks.NiNode)
                                If niN Is Nothing Then Continue For
                                Dim boneName = If(niN.Name?.String, "")
                                If String.IsNullOrEmpty(boneName) Then Continue For
                                Dim bind = shape.ShapeBoneTransforms(sbiD)
                                If bind Is Nothing Then Continue For
                                Dim bindT As New Transform_Class With {
                                    .Translation = bind.Translation,
                                    .Rotation = bind.Rotation,
                                    .Scale = bind.Scale,
                                    .ScaleVector = bind.ScaleVector
                                }
                                Dim invBind = bindT.Inverse()
                                Dim invBindT = invBind.Translation
                                Dim actorBhbDiag As HierarchiBone_class = Nothing
                                Dim hasActor = targetSkel.SkeletonDictionary.TryGetValue(boneName, actorBhbDiag)
                                Dim aBT As System.Numerics.Vector3 = If(hasActor, actorBhbDiag.OriginalGetGlobalTransform.Translation, New System.Numerics.Vector3(0, 0, 0))
                                Dim dT_bone As Double = If(hasActor,
                                    Math.Sqrt((invBindT.X - aBT.X) ^ 2 + (invBindT.Y - aBT.Y) ^ 2 + (invBindT.Z - aBT.Z) ^ 2),
                                    Double.NaN)
                                Dim dT_mmesh As Double = Math.Sqrt((invBindT.X - mmT_outer.X) ^ 2 + (invBindT.Y - mmT_outer.Y) ^ 2 + (invBindT.Z - mmT_outer.Z) ^ 2)
                                Dim verdict As String
                                If Not hasActor Then
                                    verdict = "actor.bone NOT-IN-DICT"
                                ElseIf dT_bone < 1.0 AndAlso dT_mmesh > 1.0 Then
                                    verdict = "BIND≈ACTOR.BONE (sin socket; chunk-frame literal)"
                                ElseIf dT_mmesh < 1.0 AndAlso dT_bone > 1.0 Then
                                    verdict = "BIND≈M_MESH (socket baked; renders OK as-is)"
                                ElseIf dT_bone < dT_mmesh Then
                                    verdict = "CLOSER-TO-BONE"
                                Else
                                    verdict = "CLOSER-TO-M_MESH"
                                End If
                                Dim shLD = shape.ShapeName, bnLD = boneName, ibTL = invBindT, aBTL = aBT, mmTL = mmT_outer, dTbL = dT_bone, dTmL = dT_mmesh, vrL = verdict
                                Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD}' bone='{bnLD}' inv(bind).T=({ibTL.X:F3},{ibTL.Y:F3},{ibTL.Z:F3}) actor.bone.T=({aBTL.X:F3},{aBTL.Y:F3},{aBTL.Z:F3}) M_mesh.T=({mmTL.X:F3},{mmTL.Y:F3},{mmTL.Z:F3}) dT_bone={dTbL:F3} dT_mmesh={dTmL:F3} → {vrL}")
                            Next
                        Else
                            Dim shLD2 = shape.ShapeName, pbnL = socket.ParentBoneName
                            Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD2}' SKIP: parent bone '{pbnL}' NOT-IN-DICT")
                        End If
                    Catch exBb As Exception
                        Dim shLD3 = shape.ShapeName, msgL = exBb.Message
                        Logger.LogLazy(Function() $"[DIAG-BIND-BAKE] shape='{shLD3}' EXCEPTION: {msgL}")
                    End Try
                End If
            ElseIf isRobotMount AndAlso Logger.Enabled Then
                Dim shL = shape.ShapeName
                Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' SKIP: chunk has no BSConnectPoint::Children")
            End If
        Catch ex As Exception
            Dim shL = shape.ShapeName, exL = ex
            Logger.LogLazy(Function() $"[CHUNK-RESKIN-V2] shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
        End Try
    End Sub

    ''' <summary>Override actor bone world position to match <paramref name="desiredWorld"/>
    ''' (donde el chunk quiere el bone) escribiendo <c>MountDeltaTransform</c> sin mutar el
    ''' bind original. Children del bone cascadean automáticamente via parent chain.
    ''' Matemática: <c>newLocal = inv(parent.OriginalGetGlobalTransform) × desiredWorld</c>;
    ''' <c>MountDelta = inv(OrigL) × newLocal</c>. La ANIMACIÓN no pelea con esto:
    ''' <c>local = M × L_anim</c> con <c>M = (O×Mount) × inv(clipBase)</c>, y HkxPoseImportSession
    ''' mide del propio clip su frame de autoría (clips autoreados sobre el rig → M=Mount, el mount
    ''' persiste al animar; clips autoreados sobre el ensamblado → M=I, el clip ya trae el mount).</summary>
    Private Sub OverrideActorBoneWorld(hb As HierarchiBone_class,
                                        desiredWorld As Transform_Class,
                                        contextLabel As String)
        If hb Is Nothing OrElse desiredWorld Is Nothing Then Return
        Dim currentWorld = hb.OriginalGetGlobalTransform
        Dim cT = currentWorld.Translation, dT = desiredWorld.Translation
        Dim diff = Math.Sqrt((cT.X - dT.X) ^ 2 + (cT.Y - dT.Y) ^ 2 + (cT.Z - dT.Z) ^ 2)
        Dim parentWorld As Transform_Class
        If hb.Parent IsNot Nothing Then
            parentWorld = hb.Parent.OriginalGetGlobalTransform
        Else
            parentWorld = New Transform_Class()
        End If
        Dim newLocal = parentWorld.Inverse().ComposeTransforms(desiredWorld)
        Dim newMountDelta = hb.OriginalLocaLTransform.Inverse().ComposeTransforms(newLocal)
        ' El conflicto real "2 chunks → 1 hueso" se detecta fail-loud en ApplyMountPlanForActor (duplicado
        ' within-pass). Acá ya NO se loguea el caso cross-pase (hb.MountDeltaTransform de un render previo),
        ' que NO es conflicto sino re-aplicación normal por pose/re-render.
        hb.MountDeltaTransform = newMountDelta
        Dim bnL = hb.BoneName, ctxL = contextLabel, ctL = cT, dL = dT, diL = diff, mdT = newMountDelta.Translation
        Logger.LogLazy(Function() $"[MOUNTDELTA-WRITE] bone='{bnL}' ctx='{ctxL}' was.world.T=({ctL.X:F3},{ctL.Y:F3},{ctL.Z:F3}) → wants.world.T=({dL.X:F3},{dL.Y:F3},{dL.Z:F3}) diff={diL:F3} MountDelta.T=({mdT.X:F3},{mdT.Y:F3},{mdT.Z:F3})")
    End Sub

    ''' <summary>Nombres de los huesos que ALGUNA shape renderizada usa como skin-bone (geometría depende
    ''' de ellos). Se usa para distinguir un conflicto de mount que IMPORTA (skin-bone con malla) de uno
    ''' sobre un marker SIN malla (ej. ProjectileNode escrito por el tree-walk como bone[-1], que no
    ''' afecta el render aunque varios chunks lo quieran en lugares distintos).</summary>
    Private Shared Function BuildRenderedSkinBoneNames(renderData As PreviewResolutionResult) As HashSet(Of String)
        Dim s As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return s
        For Each sh In renderData.Shapes
            If sh Is Nothing OrElse sh.ShapeBones Is Nothing Then Continue For
            For Each b In sh.ShapeBones
                Dim niN = TryCast(b, NiflySharp.Blocks.NiNode)
                Dim nm = niN?.Name?.String
                If Not String.IsNullOrEmpty(nm) Then s.Add(nm)
            Next
        Next
        Return s
    End Function

    ''' <summary>Aplicador canónico ÚNICO del plan de mount. Recorre el plan
    ''' <c>renderData.MountDesiredWorlds</c> (orden topológico) y escribe <c>MountDeltaTransform</c>
    ''' vía <see cref="OverrideActorBoneWorld"/>. Patrón: <c>ApplyPose → ApplyMountPlanForActor</c>.
    ''' Per-instance scope vía TargetSkel.</summary>
    Private Sub ApplyMountPlanForActor(inst As SkeletonInstance, renderData As PreviewResolutionResult)
        If inst Is Nothing OrElse renderData Is Nothing Then Return
        If renderData.MountDesiredWorlds Is Nothing OrElse renderData.MountDesiredWorlds.Count = 0 Then Return

        Dim writtenCount As Integer = 0
        Dim skippedNoBone As Integer = 0
        Dim skippedScopeMismatch As Integer = 0
        ' [DEPTH-ORDER GLOBAL] Aplicar PADRE-PRIMERO sobre el plan entero: con entradas cross-shape
        ' (el árbol del torso trae LClavicle, el chunk de brazo trae sus huesos) el orden de colección
        ' no garantiza topología. OverrideActorBoneWorld computa el local contra el world ACTUAL del
        ' parent: si un hijo se aplica antes que su padre, el cascade posterior del padre lo corre.
        Dim applyList As New List(Of (Entry As MountDesiredWorldEntry, Bone As HierarchiBone_class, Depth As Integer))
        For Each entry In renderData.MountDesiredWorlds
            If entry Is Nothing OrElse String.IsNullOrEmpty(entry.BoneName) Then Continue For
            If entry.TargetSkel IsNot Nothing AndAlso entry.TargetSkel IsNot inst Then
                skippedScopeMismatch += 1
                Continue For
            End If
            Dim hb As HierarchiBone_class = Nothing
            If Not inst.SkeletonDictionary.TryGetValue(entry.BoneName, hb) OrElse hb Is Nothing Then
                skippedNoBone += 1
                Continue For
            End If
            applyList.Add((entry, hb, GetBoneHierarchyDepth(hb)))
        Next
        ' Sort ESTABLE por depth: entre entradas del mismo bone (last-write-wins del plan) se
        ' conserva el orden de colección.
        Dim applyOrdered = applyList.OrderBy(Function(x) x.Depth).ToList()
        ' [MOUNTDELTA-CONFLICT-IMPOSIBLE] Fail-loud (como Path B). Conflicto que IMPORTA = 2+ entradas del
        ' plan quieren el mismo hueso en lugares DISTINTOS (diff>0.5) EN ESTE pase, Y el hueso tiene
        ' GEOMETRÍA (es skin-bone de alguna shape renderizada). Dos filtros contra falsos positivos:
        '   (1) diff>0.5  → descarta el caso benigno multi-part / multi-instancia (varias shapes del mismo
        '       chunk al mismo hueso con el MISMO W_B, ej. Mr Handy EyeArm1|N: brazo+iris+lente → idéntico).
        '   (2) skin-bone → descarta markers SIN malla escritos por el tree-walk como bone[-1] (ej.
        '       ProjectileNode = boca del arma: en un CreateABot 3 armas lo quieren en 3 lugares, pero NO
        '       hay geometría ahí → no afecta el render). Verificado 2026-06-14 con NPC 0x0100FF0A.
        ' (within-pass Dictionary fresco por llamada → NO confunde el re-render cross-pase. last-write-wins
        ' se sigue aplicando.)
        Dim appliedWorlds As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim skinBoneNames As HashSet(Of String) = Nothing ' lazy: se construye solo en el 1er diff>0.5
        For Each item In applyOrdered
            Dim prevDesired As Transform_Class = Nothing
            If appliedWorlds.TryGetValue(item.Bone.BoneName, prevDesired) AndAlso prevDesired IsNot Nothing AndAlso item.Entry.DesiredWorld IsNot Nothing Then
                Dim pT = prevDesired.Translation, nT = item.Entry.DesiredWorld.Translation
                Dim dd As Double = Math.Sqrt((pT.X - nT.X) ^ 2 + (pT.Y - nT.Y) ^ 2 + (pT.Z - nT.Z) ^ 2)
                If dd > 0.5 Then
                    If skinBoneNames Is Nothing Then skinBoneNames = BuildRenderedSkinBoneNames(renderData)
                    If skinBoneNames.Contains(item.Bone.BoneName) Then
                        Dim bnDup As String = item.Bone.BoneName, ctxDup As String = If(item.Entry.ContextLabel, "?"), ddL As Double = dd
                        Logger.LogLazy(Function() $"[MOUNTDELTA-CONFLICT-IMPOSIBLE] bone='{bnDup}' ctx='{ctxDup}' diff={ddL:F3} — 2 chunks quieren el mismo SKIN-BONE en lugares DISTINTOS")
                        MessageBox.Show("MOUNTDELTA CONFLICT IMPOSIBLE — no debería pasar." & vbCrLf & vbCrLf &
                                        "bone = " & bnDup & vbCrLf &
                                        "ctx  = " & ctxDup & vbCrLf &
                                        "diff = " & dd.ToString("F2") & vbCrLf & vbCrLf &
                                        "2 chunks quieren el MISMO skin-bone (con geometría) en lugares DISTINTOS." & vbCrLf &
                                        "Aplica last-write-wins. La regla canónica sería 'gana el host que publica el hueso'.",
                                        "MountDelta conflict imposible — REVISAR", MessageBoxButtons.OK, MessageBoxIcon.Error)
                    Else
                        Dim bnMk As String = item.Bone.BoneName, ctxMk As String = If(item.Entry.ContextLabel, "?"), ddMk As Double = dd
                        Logger.LogLazy(Function() $"[MOUNTDELTA-MARKER-CONFLICT] bone='{bnMk}' ctx='{ctxMk}' diff={ddMk:F3} — sin geometría (no skin-bone), no afecta render, silenciado")
                    End If
                End If
            End If
            appliedWorlds(item.Bone.BoneName) = item.Entry.DesiredWorld
            OverrideActorBoneWorld(item.Bone, item.Entry.DesiredWorld, item.Entry.ContextLabel & "-APPLY")
            writtenCount += 1
        Next

        Dim instBonesL = inst.SkeletonDictionary.Count, cacheL = renderData.MountDesiredWorlds.Count
        Dim writtenL = writtenCount, skippedL = skippedNoBone, skippedScopeL = skippedScopeMismatch
        Logger.LogLazy(Function() $"[MOUNTDELTA-PREPASS] inst.bones={instBonesL} cache.entries={cacheL} written={writtenL} skipped(boneNotInDict)={skippedL} skipped(scopeMismatch)={skippedScopeL}")
    End Sub

    ''' <summary>Resuelve la "posición efectiva" de un bone en el actor world: si un parent
    ''' chunk corrió V2 sobre ese bone (su W_B vive en chunkWBHistory), devuelve W_B. Sino,
    ''' devuelve actor.bone.world del SkeletonDictionary. Identity si el bone no existe.
    '''
    ''' Esta es la pieza central de la unificación matemática V2 / PROPAGATE-V2 /
    ''' PROPAGATE-V2-ANCHOR. Las 3 fixes computan correction = inv(actor.B.world) × desired_W(B),
    ''' donde desired_W(B) = ResolveEffectiveWorld(B). La fórmula es idéntica; la diferencia
    ''' entre fixes es solo dónde se aplica el correction (bind del shape vs anchor.local del
    ''' chunk vs nuevo bind via V2 reskin).</summary>
    Private Function ResolveEffectiveWorld(chunkWBHistory As Dictionary(Of String, Transform_Class),
                                            inst As SkeletonInstance,
                                            boneName As String) As Transform_Class
        If chunkWBHistory IsNot Nothing AndAlso Not String.IsNullOrEmpty(boneName) Then
            Dim wb As Transform_Class = Nothing
            If chunkWBHistory.TryGetValue(boneName, wb) AndAlso wb IsNot Nothing Then
                Return wb
            End If
        End If
        If inst IsNot Nothing AndAlso Not String.IsNullOrEmpty(boneName) Then
            Dim hb As HierarchiBone_class = Nothing
            If inst.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                Return hb.OriginalGetGlobalTransform
            End If
        End If
        Return New Transform_Class()
    End Function

    ''' <summary>Host-scoped resolution del MountSocket de un robot chunk. Walkea la cadena
    ''' de hosts: <c>host inmediato → host del host → ... → skeleton root</c>. En cada nivel
    ''' busca el socket en el namespace local del publisher (BSConnectPoint::Parents que ese
    ''' chunk publica). Si no aparece en ningún host de la cadena, cae al <c>skeletonSockets</c>
    ''' (SRC1+SRC2: RACE.ANAM + BPTD.MODL).
    '''
    ''' Reemplaza la resolución flat global que mezclaba STATIC skeleton + per-chunk publishers
    ''' en un único <c>SocketsDictionary</c> y forzaba políticas FIRST-WINS/CHUNK-WINS para
    ''' decidir conflicts artificiales que en realidad eran namespaces distintos. Caso vivo:
    ''' Assaultron Torso publica P-ArmRight con T=(8.666, ...) acomodado a sus hombros
    ''' estrechos; el skeleton vanilla publica P-ArmRight con T=(18.772, ...) genérico
    ''' humanoide. Con host-scoped, ArmRightAssaultron (host = TorsoAssaultron) resuelve
    ''' contra el T=(8.666) del torso publisher → brazo encastra. El skeleton sólo se
    ''' consulta si NINGÚN host de la cadena publicó P-ArmRight.</summary>
    Private Function ResolveMountSocketHostScoped(apEditorId As String,
                                                  apIdx As Byte,
                                                  hostOrdinal As Integer,
                                                  publisherSockets As Dictionary(Of UInteger, Dictionary(Of String, PublisherSocketInfo)),
                                                  hostChainMap As Dictionary(Of Integer, Integer),
                                                  resolution As ObjectTemplateResolver.CombinationResolution,
                                                  skeletonSockets As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo),
                                                  ByRef resolvedInfo As PublisherSocketInfo,
                                                  ByRef matchedHostOrdinal As Integer,
                                                  ByRef matchedHostFormID As UInteger,
                                                  ByRef matchedHostApIdx As Byte) _
                                                  As BSConnectPointReader.ConnectPointInfo
        resolvedInfo = Nothing
        matchedHostOrdinal = 0
        matchedHostFormID = 0UI
        matchedHostApIdx = 0
        If String.IsNullOrEmpty(apEditorId) Then
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='' → NOT-FOUND (KYWD not resolvable)")
            Return Nothing
        End If
        Dim baseName = apEditorId
        Dim stripped As String = ""
        If baseName.StartsWith("ap_Bot_", StringComparison.OrdinalIgnoreCase) Then
            stripped = "ap_Bot_"
            baseName = baseName.Substring("ap_Bot_".Length)
        ElseIf baseName.StartsWith("ap_", StringComparison.OrdinalIgnoreCase) Then
            stripped = "ap_"
            baseName = baseName.Substring("ap_".Length)
        End If
        Dim indexed = $"P-{baseName}|{apIdx}"
        Dim plain = $"P-{baseName}"
        Dim apEditorIdLog = apEditorId, baseNameLog = baseName, strippedLog = stripped
        Dim indexedLog = indexed, plainLog = plain, apIdxLog = apIdx

        ' Walk host chain por ORDINAL runtime. Safety cap contra ciclo (no debería ocurrir
        ' — ordinals son monotónicos, no se pueden ciclar, pero defensivo).
        Dim currentOrd As Integer = hostOrdinal
        Dim hops As Integer = 0
        Const maxHops As Integer = 32
        Dim chainTrace As New System.Text.StringBuilder()
        While currentOrd <> 0 AndAlso hops < maxHops
            ' Lookup FormID del OMOD para este ordinal via resolution parallel arrays.
            ' Necesario porque publisherSockets sigue keyeado por FormID (asset-level —
            ' los sockets son propiedad del NIF, idénticos entre instancias del mismo asset).
            Dim currentFid As UInteger = 0UI
            For idx = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                If resolution.IncludedOmodInstanceOrdinal(idx) = currentOrd Then
                    Dim om = resolution.IncludedOmods(idx)
                    If om IsNot Nothing Then currentFid = om.FormID
                    Exit For
                End If
            Next
            chainTrace.Append($"→(ord={currentOrd},0x{currentFid:X8})")
            Dim hostMap As Dictionary(Of String, PublisherSocketInfo) = Nothing
            If currentFid <> 0UI AndAlso publisherSockets.TryGetValue(currentFid, hostMap) Then
                Dim info As PublisherSocketInfo = Nothing
                If hostMap.TryGetValue(indexed, info) Then
                    resolvedInfo = info
                    matchedHostOrdinal = currentOrd
                    matchedHostFormID = currentFid
                    ' Lookup apIdx via parallel arrays para logging legible.
                    For idx2 = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                        If resolution.IncludedOmodInstanceOrdinal(idx2) = currentOrd Then
                            matchedHostApIdx = resolution.IncludedOmodApIdx(idx2)
                            Exit For
                        End If
                    Next
                    Dim ordL = currentOrd, fidL = currentFid, traceL = chainTrace.ToString()
                    Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL} → MATCH '{indexedLog}' at host=(ord={ordL},0x{fidL:X8}) parent='{info.Socket.ParentBoneName}' parentFoundInHost={info.ParentFoundInHostNif}")
                    Return info.Socket
                End If
                If hostMap.TryGetValue(plain, info) Then
                    resolvedInfo = info
                    matchedHostOrdinal = currentOrd
                    matchedHostFormID = currentFid
                    For idx2 = 0 To resolution.IncludedOmodInstanceOrdinal.Count - 1
                        If resolution.IncludedOmodInstanceOrdinal(idx2) = currentOrd Then
                            matchedHostApIdx = resolution.IncludedOmodApIdx(idx2)
                            Exit For
                        End If
                    Next
                    Dim ordL = currentOrd, fidL = currentFid, traceL = chainTrace.ToString()
                    Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL} → MATCH '{plainLog}' at host=(ord={ordL},0x{fidL:X8}) parent='{info.Socket.ParentBoneName}' parentFoundInHost={info.ParentFoundInHostNif}")
                    Return info.Socket
                End If
            End If
            Dim parentOrd As Integer = 0
            hostChainMap.TryGetValue(currentOrd, parentOrd)
            currentOrd = parentOrd
            hops += 1
        End While

        ' Fallback: skeleton root (SRC1+SRC2). resolvedInfo queda Nothing → consumer cae
        ' al Path B fallback en V2 (actor.parentBone × socket.local con ResolveEffectiveWorld).
        Dim sk As BSConnectPointReader.ConnectPointInfo = Nothing
        If skeletonSockets.TryGetValue(indexed, sk) Then
            Dim traceL = chainTrace.ToString()
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL}→skeleton → MATCH '{indexedLog}' parent='{sk.ParentBoneName}'")
            Return sk
        End If
        If skeletonSockets.TryGetValue(plain, sk) Then
            Dim traceL = chainTrace.ToString()
            Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceL}→skeleton → MATCH '{plainLog}' parent='{sk.ParentBoneName}'")
            Return sk
        End If
        Dim traceFinal = chainTrace.ToString()
        Logger.LogLazy(Function() $"[MOUNT-LOOKUP-HS] apEditorId='{apEditorIdLog}' apIdx={apIdxLog} base='{baseNameLog}' chain={traceFinal}→skeleton → NOT-FOUND (tried '{indexedLog}' and '{plainLog}' in every host + skeleton)")
        Return Nothing
    End Function

    ''' <summary>Compute (si no está cacheado) y devuelve cand.ChunkToActor para un robot
    ''' chunk. Recursivo — Path A consulta host.ChunkToActor, que si no está set se computa
    ''' lazy via EnsureChunkToActor(host). Esto desacopla el compute de ChunkToActor del
    ''' shape materialization: un host que publica sockets pero no emite shapes propias
    ''' (caso "host publisher sin shapes") nunca dispara JIT por shape loop, pero recibe
    ''' ChunkToActor cuando algún descendant con shapes lo requiere via recursión.
    '''
    ''' Cycle detection: <paramref name="visiting"/> set DFS coloring. Push del ordinal al
    ''' entrar (Try); pop al salir (Finally). Si recursión llega a ordinal ya en visiting
    ''' es ciclo real (loggeado, fallback Path B sin host). Cap defensivo 32 hops también.
    '''
    ''' Devuelve cand.ChunkToActor (o Nothing si compute falla — cand queda sin ChunkToActor).</summary>
    Private Function EnsureChunkToActor(cand As MeshCandidate,
                                         candByOrdinal As Dictionary(Of Integer, MeshCandidate),
                                         renderData As PreviewResolutionResult,
                                         targetSkel As SkeletonInstance,
                                         wbHistory As Dictionary(Of String, Transform_Class),
                                         visiting As HashSet(Of Integer)) As Transform_Class
        If cand Is Nothing Then Return Nothing
        If cand.ChunkToActor IsNot Nothing Then Return cand.ChunkToActor
        If cand.MountSocket Is Nothing Then Return Nothing

        Dim ordSelf = cand.ChunkInstanceOrdinal
        If ordSelf <> 0 AndAlso visiting.Contains(ordSelf) Then
            ' Ciclo detectado — el ordinal actual ya está siendo computado más arriba en
            ' la recursión. Log y NO recursar.
            Dim ordL = ordSelf, nmL = If(cand.MountSocket?.Name, "?")
            Logger.LogLazy(Function() $"[A_HOST-CYCLE] ord={ordL} socket='{nmL}' — ciclo detectado en host chain (DFS visiting set hit), no recursar; ChunkToActor queda Nothing")
            Return Nothing
        End If

        If ordSelf <> 0 Then visiting.Add(ordSelf)
        Try
            ' Resolver host's ChunkToActor recursivamente si Path A puede aplicar.
            Dim hostA As Transform_Class = Nothing
            Dim usedPathA As Boolean = False
            Dim hostCand As MeshCandidate = Nothing
            If cand.ParentFoundInMatchedHostNif AndAlso cand.ResolvedHostSocketGlobalT IsNot Nothing AndAlso cand.MatchedHostInstanceOrdinal <> 0 Then
                candByOrdinal.TryGetValue(cand.MatchedHostInstanceOrdinal, hostCand)
                If hostCand IsNot Nothing Then
                    hostA = EnsureChunkToActor(hostCand, candByOrdinal, renderData, targetSkel, wbHistory, visiting)
                End If
            End If

            ' Resolver G_CX desde chunk NIF — necesario en ambos paths.
            Dim chunkNif As Nifcontent_Class_Manolo = Nothing
            If Not renderData.CandidateNif.TryGetValue(cand, chunkNif) OrElse chunkNif Is Nothing Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, nmL_dbg = If(cand.MountSocket?.Name, "?"), fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{nmL_dbg}' reason=CandidateNif-miss")
                Return Nothing
            End If
            Dim socketNm = If(cand.MountSocket.Name, "")
            If String.IsNullOrEmpty(socketNm) OrElse socketNm.Length <= 2 Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{socketNm}' reason=socket-name-too-short")
                Return Nothing
            End If
            Dim cxNm As String = BSConnectPointBoneInjector_Class.TryGetSocketCounterpartName(socketNm)
            If String.IsNullOrEmpty(cxNm) Then
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{socketNm}' reason=cxNm-empty (socket sin prefix P-/P_)")
                Return Nothing
            End If
            Dim cxNode = chunkNif.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cxNm)
            If cxNode Is Nothing Then
                ' Strip-on-NIF-side fallback: chunks multi-instance comparten el MISMO NIF
                ' (mismo OMOD asset, distintos apIdx publisher-side). El NIF tiene UN único
                ' C-X NiNode (típicamente con sufijo apIdx fijo authoreado, p.ej. `C-X|0`).
                ' Cuando el resolver da socket `P-X|2` → cxNm=`C-X|2` exact no matchea el NIF
                ' que tiene `C-X|0`. Regla: cualquier NiNode cuyo base (pre-`|`) coincida con
                ' cxNm base es el mismo socket — el sufijo numérico es índice publisher, no
                ' parte del nombre semántico. Esto cubre Codsworth Bot_ModTorsoHandyEye1B
                ' apIdx=1/2 (NIF tiene C-ModSlotB|0, socket pide |1 o |2 → strip a C-ModSlotB
                ' en ambos lados → match). Paridad con la lógica StripSfx del V2 legacy
                ' (líneas ~2703-2712 inline en shape loop pre-refactor).
                Dim cxNormSearch = StripInstanceSuffix(cxNm)
                For Each blk In chunkNif.Blocks
                    Dim candBlk = TryCast(blk, NiflySharp.Blocks.NiNode)
                    If candBlk Is Nothing Then Continue For
                    Dim candNm = If(candBlk.Name?.String, "")
                    If String.Equals(StripInstanceSuffix(candNm), cxNormSearch, StringComparison.OrdinalIgnoreCase) Then
                        cxNode = candBlk
                        Exit For
                    End If
                Next
            End If
            If cxNode Is Nothing Then
                ' Chunk no tiene C-X NiNode interno — caso "attachment-style" (mesh skinned
                ' directamente a un bone parent del actor sin chunk-internal coord system).
                ' Path A no aplica; el chunk render va por el path INJECT/legacy con
                ' SkeletonFallbackSocket en el shape loop (SOCKET-EFFECTIVE-OVERRIDE).
                Dim ordL_dbg = cand.ChunkInstanceOrdinal, fidL_dbg = cand.ChunkOmodFormID, sNmL_dbg = socketNm, cxNmL_dbg = cxNm
                Logger.LogLazy(Function() $"[A_HOST-JIT-EARLY] ord={ordL_dbg} fid=0x{fidL_dbg:X8} socket='{sNmL_dbg}' cxNm='{cxNmL_dbg}' reason=cxNode-not-found-in-chunk-NIF (attachment-style chunk, render via legacy INJECT path)")
                Return Nothing
            End If
            Dim G_CX = Transform_Class.GetGlobalTransform(cxNode, chunkNif)

            ' Path A si host A está computado.
            Dim M_mesh As Transform_Class = Nothing
            Dim pathBSource As String = ""
            If hostA IsNot Nothing Then
                M_mesh = hostA.ComposeTransforms(cand.ResolvedHostSocketGlobalT)
                usedPathA = True
            Else
                ' [PATH B — SOCKET SOURCE SEPARATION] Per OpenAI Vuelta 17: el publisher chunk
                ' socket usa chunk-internal naming (parent='Arm1' sin suffix), pero Path B
                ' resuelve parent contra actor.skel que tiene indexed (Arm1|0/1/2). Eso rompe
                ' multi-instance attachments (Codsworth Mr Handy ModArmsHandyAR1A apIdx=0/1).
                ' Fix estructural: Path B usa SkeletonFallbackSocket (publisher SRC1/SRC2 con
                ' parent indexed correcto), NO el publisher chunk socket. Sólo cae al publisher
                ' socket como último recurso (loggeado) si skeleton no tiene este socket name.
                Dim socketForPathB As BSConnectPointReader.ConnectPointInfo = cand.SkeletonFallbackSocket
                If socketForPathB IsNot Nothing Then
                    pathBSource = "skel"
                Else
                    socketForPathB = cand.MountSocket
                    pathBSource = "publisher-fallback"
                    Dim ordL_pbf = ordSelf, nmL_pbf = socketNm
                    Logger.LogLazy(Function() $"[A_HOST-JIT-PATHB-FALLBACK] ord={ordL_pbf} socket='{nmL_pbf}' — SkeletonFallbackSocket is Nothing, usando publisher socket (último recurso; parent puede no estar en actor.skel)")
                End If
                ' [PATH B — APIDX SUBSTITUTION] Skeleton publica P-X con UN solo parent indexed
                ' (típicamente '|0'). Para consumers multi-instance con apIdx != 0, sustituir
                ' el suffix del parent para apuntar al bone indexed correcto del actor skel.
                ' Caso vivo Mr Handy: skeleton P-ModArmsSlotA parent='Arm1|0'. Consumer apIdx=1
                ' (Flamer arm mod) necesita parent='Arm1|1'. Engine convention empírica: el
                ' suffix '|N' del parent matchea el apIdx del consumer.
                Dim parentForPathB = If(socketForPathB.ParentBoneName, "")
                Dim parentForLookup = parentForPathB
                If cand.MountApIdx <> 0 AndAlso Not String.IsNullOrEmpty(parentForPathB) Then
                    Dim pipe = parentForPathB.LastIndexOf("|"c)
                    If pipe > 0 AndAlso pipe < parentForPathB.Length - 1 Then
                        Dim sfx = parentForPathB.Substring(pipe + 1)
                        Dim allDigits As Boolean = True
                        For Each c In sfx
                            If Not Char.IsDigit(c) Then allDigits = False : Exit For
                        Next
                        If allDigits Then
                            parentForLookup = String.Concat(parentForPathB.AsSpan(0, pipe + 1), cand.MountApIdx.ToString())
                            If Not String.Equals(parentForLookup, parentForPathB, StringComparison.Ordinal) Then
                                Dim ordL_sub = ordSelf, origL = parentForPathB, newL = parentForLookup, apL = cand.MountApIdx
                                Logger.LogLazy(Function() $"[A_HOST-JIT-PATHB-APIDX-SUB] ord={ordL_sub} parent '{origL}' → '{newL}' (consumer apIdx={apL})")
                            End If
                        End If
                    End If
                End If
                Dim parentBoneWorld = ResolveEffectiveWorld(wbHistory, targetSkel, parentForLookup)
                Dim socketLocal As New Transform_Class With {
                    .Translation = socketForPathB.Translation,
                    .Rotation = BSConnectPointReader.QuatToMatrix33(socketForPathB.Rotation),
                    .Scale = If(socketForPathB.Scale > 0.0F, socketForPathB.Scale, 1.0F)
                }
                M_mesh = parentBoneWorld.ComposeTransforms(socketLocal)
            End If
            cand.ChunkToActor = M_mesh.ComposeTransforms(G_CX.Inverse())

            Dim ordL2 = ordSelf, matchedOrdL = cand.MatchedHostInstanceOrdinal, sNmL = socketNm
            Dim pathL = If(usedPathA, "A(host.ChunkToActor × HostSocketGlobalT)", "B(" & pathBSource & " × socket.local)")
            Dim mmT = M_mesh.Translation, aT = cand.ChunkToActor.Translation
            Logger.LogLazy(Function() $"[A_HOST-JIT] ord={ordL2} socket='{sNmL}' matchedHost.ord={matchedOrdL} path={pathL} M_mesh.T=({mmT.X:F3},{mmT.Y:F3},{mmT.Z:F3}) A.T=({aT.X:F3},{aT.Y:F3},{aT.Z:F3})")
            Return cand.ChunkToActor
        Finally
            If ordSelf <> 0 Then visiting.Remove(ordSelf)
        End Try
    End Function

    ''' <summary>Resolve OMOD.AttachPointFormID (KYWD FormID) to the KYWD's EditorID. Returns ""
    ''' when the FormID is 0 or the record isn't loaded (which happened for every OMOD before the
    ''' KYWD loader fix on 2026-05-10).</summary>
    Private Function ResolveAttachPointEditorId(kywdFormID As UInteger) As String
        If kywdFormID = 0UI Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0 → empty")
            Return ""
        End If
        Dim rec = _pluginManager.GetRecord(kywdFormID)
        Dim fidLog = kywdFormID
        If rec Is Nothing Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → NOT FOUND in PluginManager")
            Return ""
        End If
        If rec.Header.Signature <> "KYWD" Then
            Dim sig = rec.Header.Signature
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → wrong sig '{sig}' (expected KYWD)")
            Return ""
        End If
        Dim eid = If(rec.EditorID, "")
        If String.IsNullOrEmpty(eid) Then
            Logger.LogLazy(Function() $"[AP-RESOLVE] kywdFid=0x{fidLog:X8} → KYWD with empty EditorID")
        End If
        Return eid
    End Function

    ''' <summary>Load the actor's skeleton NIFs and index every BSConnectPoint::Parents socket by
    ''' Name (case-insens). Reads sockets from BOTH skeleton sources used by PrepareSkeleton:
    ''' (1) RACE.ANAM (resolved via ResolveSkeletonKey), and (2) BPTD.MODL (resolved via
    ''' RACE.GNAM → BPTD). For humanoides ambos coinciden y la 2da pasada es no-op por dedupe.
    ''' Para robots la 2da pasada aporta los sockets reales (P-ArmsTypeA1|0/1/2, P-BotCore,
    ''' P-BotLegs, P-ModSlotA/B, etc.) que viven en SkeletonRef.nif y no en el stub RACE.ANAM.
    ''' Last-wins on duplicate names (BPTD.MODL pisa al RACE.ANAM cuando hay colisión, igual
    ''' criterio que PrepareSkeleton tiene para bones via MergeAdditionalSkeleton).</summary>
    Private Function LoadActorBSConnectPoints(state As NPCVisualState, warnings As List(Of String)) As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)
        Dim dict As New Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)(StringComparer.OrdinalIgnoreCase)

        ' Source 1: RACE.ANAM
        Dim skelKey = ResolveSkeletonKey(state, warnings)
        Dim countAfterSrc1 As Integer = 0
        If Not String.IsNullOrEmpty(skelKey) Then
            IndexSocketsFromSkeletonKey(skelKey, dict)
            countAfterSrc1 = dict.Count
            Logger.LogLazy(Function() $"[SOCKETS-SRC1-RACE.ANAM] key='{skelKey}' addedTotal={countAfterSrc1}")
        Else
            Logger.LogLazy(Function() $"[SOCKETS-SRC1-RACE.ANAM] skelKey EMPTY → skipped")
        End If

        ' Source 2: BPTD.MODL (via RACE.GNAM) — aporta sockets cross-folder y los del SkeletonRef.
        Dim bptdBytes = BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(state.RaceFormID, _pluginManager)
        If bptdBytes IsNot Nothing AndAlso bptdBytes.Length > 0 Then
            IndexSocketsFromBytes(bptdBytes, "BPTD.MODL", dict)
            Dim countAfterSrc2 = dict.Count
            Dim diff = countAfterSrc2 - countAfterSrc1
            Logger.LogLazy(Function() $"[SOCKETS-SRC2-BPTD.MODL] bytes={bptdBytes.Length} totalAfter={countAfterSrc2} delta={diff} (delta cuenta nuevos+overwrites; overwrites no detectables sin tracking adicional)")
        Else
            Logger.LogLazy(Function() $"[SOCKETS-SRC2-BPTD.MODL] BPTD bytes EMPTY → skipped")
        End If

        ' [DIAG] Dump completo del dict — sockets disponibles para el resolver.
        Dim sorted = dict.OrderBy(Function(kv) kv.Key).ToList()
        Logger.LogLazy(Function() $"[SOCKETS-DICT] count={sorted.Count}")
        For Each kv In sorted
            Dim name = kv.Key
            Dim cp = kv.Value
            Dim t = cp.Translation
            Dim qx = cp.Rotation.X, qy = cp.Rotation.Y, qz = cp.Rotation.Z, qw = cp.Rotation.W
            Dim parentBone = cp.ParentBoneName
            Dim sc = cp.Scale
            Logger.LogLazy(Function() $"[SOCKETS-DICT]   '{name}' parent='{parentBone}' T=({t.X:F3},{t.Y:F3},{t.Z:F3}) QuatNiflyXYZW=({qx:F4},{qy:F4},{qz:F4},{qw:F4}) [disco(w,x,y,z)=({qx:F4},{qy:F4},{qz:F4},{qw:F4})] S={sc:F3}")
        Next

        Return dict
    End Function

    ''' <summary>Helper: load NIF bytes from FilesDictionary by key + index its BSConnectPoint
    ''' sockets into the target dict (last-wins on duplicate Name).</summary>
    Private Sub IndexSocketsFromSkeletonKey(skelKey As String, dict As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo))
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(skelKey, loc) Then
            Return
        End If
        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return
            IndexSocketsFromBytes(bytes, skelKey, dict)
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>Mount the standalone Pipboy ARMO mesh on the actor's pipboy bone via synthetic
    ''' skin. El ARMO Pipboy ships con NIF unskinned + sin BSConnectPoint::Children — engine
    ''' vanilla hardcoded mountea a un bone del actor cuyo nombre contiene "pipboy" (HumanRace
    ''' 369-bone expone "PipboyBone" + "PipboyBone_Offset"). Convención inalcanzable desde data
    ''' del record; la replicamos via synthetic skin + bone lookup dinámico.
    '''
    ''' Lookup target: case-insensitive contra el SkeletonDictionary del actor. Distintas razas
    ''' pueden traer otra convención de nombre (Ghoul, Child, Synth Race) o ninguna — NO
    ''' hardcodeamos "PipboyBone". Preferimos el match que NO termina en "_Offset" (es el bone
    ''' deformable, el _Offset es rest anchor; vanilla mountea al deformable).
    '''
    ''' Bind matrix: walking shape backing → parent → ... → root (exclusive). Misma fórmula que
    ''' FAKE-SKIN del Protectron HeadLight (MainForm.vb:2716-2748); root.local se excluye porque
    ''' en vanilla Bethesda authora ahí la transform de "scene viewer" del CK, no parte del attach.
    '''
    ''' Gate: SOLO standalone Pipboy ARMO (slot==SlotBitPipboy exacto, sólo bit 30). Outfits que
    ''' declaran bit Pipboy junto con otros bits (ej. ClothesVaultTecScientist slot=0x40000008
    ''' BODY+Pipboy) NO entran — son outfits regulares con sus propios shapes skinneados, el bit
    ''' Pipboy es declarativo de slot reserve, no garantiza pipboy mesh built-in. Check IsSkinned
    ''' per-shape adicional como defense-in-depth.
    '''
    ''' Si el actor skeleton no expone ningún bone "*pipboy*" → log warning + skip; el Pipboy
    ''' renderiza al origin igual que sin fix (no es regresión, sólo no-op).</summary>
    Private Sub ApplyPipboySyntheticSkin(result As PreviewResolutionResult, inst As SkeletonInstance)
        If result Is Nothing OrElse inst Is Nothing Then Return

        Dim hasPipboyCandidate As Boolean = result.CandidateNif.Keys.Any(Function(c) c.SlotMask = SlotBitPipboy)
        If Not hasPipboyCandidate Then Return

        ' Discover pipboy bone target del skeleton del actor (case-insensitive, sin hardcoding).
        Dim pipboyBoneName As String = Nothing
        Dim pipboyCandidates = inst.SkeletonDictionary.Keys.
            Where(Function(k) k.Contains("pipboy", StringComparison.OrdinalIgnoreCase)).
            ToList()
        If pipboyCandidates.Count > 0 Then
            Dim primary = pipboyCandidates.FirstOrDefault(Function(k) Not k.EndsWith("_Offset", StringComparison.OrdinalIgnoreCase))
            pipboyBoneName = If(primary, pipboyCandidates(0))
        End If
        If pipboyBoneName Is Nothing Then
            Logger.LogLazy(Function() "[PIPBOY-DIAG] FAKE-SKIN skip: no '*pipboy*' bone en actor skeleton — Pipboy renderiza al origin (raza sin chargen-bones?)")
            Return
        End If
        Dim boneNameL = pipboyBoneName
        Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN target bone resolved: '{boneNameL}'")

        For Each cand In result.CandidateNif.Keys
            If cand.SlotMask <> SlotBitPipboy Then Continue For
            Dim pipboyNif As Nifcontent_Class_Manolo = Nothing
            If Not result.CandidateNif.TryGetValue(cand, pipboyNif) Then Continue For
            Dim rootNode = pipboyNif.GetRootNode()
            If rootNode Is Nothing Then Continue For

            ' Guard "no traen mounting": si el NIF declara BSConnectPoint::Children (mecanismo
            ' de socket-mounting via "C-X" → "P-X" match contra el actor skeleton), el modder
            ' quiso usar ese path — NO aplicar synthetic skin para no doblar el montaje.
            ' Vanilla Pipboy NIF no declara children (verificado en log: 0 children), así que
            ' este guard no dispara en data vanilla; es defensa contra mods custom.
            Try
                Dim childrenInfo = BSConnectPointReader.ReadChildren(pipboyNif)
                If childrenInfo.PointNames IsNot Nothing AndAlso childrenInfo.PointNames.Count > 0 Then
                    Dim candFidL = cand.SourceFormID
                    Dim ptsL = String.Join(",", childrenInfo.PointNames)
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN skip cand=0x{candFidL:X8}: NIF declara BSConnectPoint::Children=[{ptsL}] — mod usa socket-mounting, no hardcoded bone attach")
                    Continue For
                End If
            Catch exChildren As Exception
                Dim msg = exChildren.Message
                Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN ReadChildren EXCEPTION: {msg} (proceediendo con synthetic skin)")
            End Try

            For Each shape In result.Shapes
                If shape.NifContent IsNot pipboyNif Then Continue For
                If shape.IsSkinned Then Continue For
                Dim asOverride = TryCast(shape, IRuntimeSkinOverride)
                If asOverride Is Nothing Then Continue For

                Try
                    Dim backing = shape.Geometry.BackingShape
                    Dim bindMatrix As New Transform_Class(backing)
                    Dim curNode As NiflySharp.Blocks.NiNode = TryCast(pipboyNif.GetParentNode(backing), NiflySharp.Blocks.NiNode)
                    While curNode IsNot Nothing AndAlso Not ReferenceEquals(curNode, rootNode)
                        bindMatrix = New Transform_Class(curNode).ComposeTransforms(bindMatrix)
                        curNode = TryCast(pipboyNif.GetParentNode(curNode), NiflySharp.Blocks.NiNode)
                    End While

                    Dim placeholder As New NiflySharp.Blocks.NiNode With {
                        .Name = New NiflySharp.NiStringRef(pipboyBoneName)
                    }
                    asOverride.ApplySyntheticAnchorSkin(placeholder, bindMatrix)

                    Dim shL = shape.ShapeName
                    Dim bT = bindMatrix.Translation
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN shape='{shL}' anchor='{boneNameL}' bind.T=({bT.X:F3},{bT.Y:F3},{bT.Z:F3})")
                Catch ex As Exception
                    Dim shL = shape.ShapeName, exL = ex
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG] FAKE-SKIN shape='{shL}' EXCEPTION: {exL.GetType().Name}: {exL.Message}")
                End Try
            Next
        Next
    End Sub

    ''' <summary>Mount-resolve pass for robot chunks. Delegates al
    ''' <see cref="ConnectPointMountResolver"/> de la lib (engine-canónica P-/C- match).
    '''
    ''' Filtrar candidates que vengan del robot path: Kind=Attachment es el discriminador
    ''' canónico (ChunkOmodFormID>0 y SlotMask=0 son condiciones implícitas del Kind, pero las
    ''' mantenemos como defensa explícita por si surge un Attachment con otra topología). Cargar
    ''' el "host NIF" (BPTD.MODL del race) una sola vez para esta corrida — los sockets viven
    ''' ahí (el RACE.ANAM stub solo trae 2 sockets; el real es BPTD.MODL).</summary>
    Private Sub ResolveRobotChunkMounts(candidates As List(Of MeshCandidate),
                                         loadedNifs As Dictionary(Of String, Nifcontent_Class_Manolo),
                                         state As NPCVisualState,
                                         warnings As List(Of String))
        Dim robotChunks = candidates.Where(Function(c) c.SlotMask = 0UI AndAlso
                                                       c.Kind = MeshCandidateKind.Attachment AndAlso
                                                       c.ChunkOmodFormID <> 0UI).ToList()
        If robotChunks.Count = 0 Then Return

        ' Cargar el host NIF (BPTD.MODL) — fuente canónica de sockets per race. Si el race
        ' no tiene BPTD (humanoides puros) los sockets vienen del RACE.ANAM, pero los chunks
        ' robot solo aparecen en races con BPTD/OBTE así que en la práctica esto siempre tira.
        Dim hostNif = LoadHostNifForMounting(state)

        ' Construir lista de addons. Key = candidate.DictKey (único por chunk en este flow).
        Dim addons = robotChunks.Select(Function(c)
                                            Dim nif As Nifcontent_Class_Manolo = Nothing
                                            loadedNifs.TryGetValue(c.DictKey, nif)
                                            Return New MountAddon With {
                                                .Key = c.DictKey,
                                                .Nif = nif,
                                                .Label = $"omod=0x{c.ChunkOmodFormID:X8} chunk='{c.DictKey}'"
                                            }
                                        End Function).Where(Function(a) a.Nif IsNot Nothing).ToList()

        Dim resolutions = ConnectPointMountResolver.Instance.ResolveMounts(hostNif, addons)

        ' Aplicar resultados al MountSocket de cada candidate. Si CollectRobotChunkCandidates
        ' ya lo resolvió vía AP+apIdx (camino preferido para chunks robot multi-instance), no
        ' pisar — la resolución por NIF children del legacy resolver mountea todo a |0 y rompería
        ' los multi-instance Mr Handy arms/eyes.
        Dim resolved As Integer = 0, noMatch As Integer = 0, noChildren As Integer = 0
        For Each cand In robotChunks
            If cand.MountSocket IsNot Nothing Then
                resolved += 1
                Continue For
            End If
            Dim r As MountResolution = Nothing
            If Not resolutions.TryGetValue(cand.DictKey, r) Then Continue For
            Select Case r.Status
                Case MountResolutionStatus.Resolved
                    cand.MountSocket = r.MatchedSocket
                    resolved += 1
                Case MountResolutionStatus.NoChildren
                    noChildren += 1
                Case MountResolutionStatus.NoMatch
                    noMatch += 1
            End Select
        Next

    End Sub

    ''' <summary>Carga el NIF "host" para mounting de chunks: el BPTD.MODL del race (fuente
    ''' canónica de sockets). Devuelve Nothing si la race no tiene BPTD o el NIF no se puede
    ''' leer — el resolver tolera host Nothing devolviendo NoMatch para todos los addons.</summary>
    Private Function LoadHostNifForMounting(state As NPCVisualState) As Nifcontent_Class_Manolo
        Dim bytes = BodyPartSkeletonResolver.TryLoadBptdSkeletonBytes(state.RaceFormID, _pluginManager)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
        Try
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Return nif
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>Helper: parse NIF from bytes + index its BSConnectPoint sockets into the target
    ''' dict (last-wins on duplicate Name). Source label only for logging.</summary>
    Private Sub IndexSocketsFromBytes(bytes As Byte(), sourceLabel As String, dict As Dictionary(Of String, BSConnectPointReader.ConnectPointInfo))
        Try
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim parents = BSConnectPointReader.ReadParents(nif)
            Dim added As Integer = 0
            For Each p In parents
                If String.IsNullOrEmpty(p.Name) Then Continue For
                dict(p.Name) = p
                added += 1
            Next
        Catch ex As Exception
        End Try
    End Sub

    Private Sub CollectHeadPartCandidates(headPartFormIDs As IEnumerable(Of UInteger),
                                          visited As HashSet(Of UInteger),
                                          candidates As List(Of MeshCandidate),
                                          ByRef order As Integer,
                                          warnings As List(Of String),
                                          state As NPCVisualState,
                                          Optional useFaceGen As Boolean = False)
        ' Per-render FLST cache so IsHdptValidForRace's race-membership checks parse each FLST
        ' at most once across the whole HDPT chain (vanilla has 3-4 distinct FLSTs referenced
        ' by hundreds of HDPTs).
        Dim flstCache As New Dictionary(Of UInteger, FLST_Data)
        ' Race defaults (gender-appropriate) so RACE-declared HDPTs always pass the check even
        ' when their RNAM is mod-inconsistent. Mirrors HeadPartPicker_Form's seed.
        Dim raceDefaults As New HashSet(Of UInteger)
        ' Non-humanoid race signal: a RACE that declares NO head parts (neither Male nor Female)
        ' is a creature/robot/dog race. RNAM=0 HDPTs only pass for humanoid races (engine drops
        ' them on dogs/robots even when NPC.PNAM has a buggy reference, e.g. EncRaiderDog01).
        Dim raceHasAnyHeadParts As Boolean = False
        Dim raceRec = If(state IsNot Nothing AndAlso state.RaceFormID <> 0UI, _pluginManager.GetRecord(state.RaceFormID), Nothing)
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            Dim race = ParseRaceCached(raceRec)
            Dim defs = If(state.IsFemale, race?.FemaleHeadPartFormIDs, race?.MaleHeadPartFormIDs)
            If defs IsNot Nothing Then
                For Each fid In defs : raceDefaults.Add(fid) : Next
            End If
            ' Either gender having head parts is enough — the race is humanoid.
            Dim maleCount = If(race?.MaleHeadPartFormIDs?.Count, 0)
            Dim femaleCount = If(race?.FemaleHeadPartFormIDs?.Count, 0)
            raceHasAnyHeadParts = (maleCount + femaleCount) > 0
        End If

        ' Pre-compute Misc->parent effective-type promotion for the top-level (parent=-1) case:
        ' vanilla NPC.PNAM often lists a hairline both in the hair's HNAM and standalone in PNAM;
        ' without this map the cascade depended on visit order. Shared helper = single source of
        ' truth with the bake's EnumerateHdptChain (no duplicated rule).
        Dim miscToParentEffective = HeadPartResolver.BuildMiscToParentEffective(headPartFormIDs, _pluginManager, AddressOf ParseHdptCached)

        For Each hdptFormID In headPartFormIDs.Where(Function(id) id <> 0UI)
            CollectHeadPartCandidate(hdptFormID, visited, candidates, order, warnings, -1, state, useFaceGen, flstCache, raceDefaults, raceHasAnyHeadParts, miscToParentEffective)
        Next
    End Sub

    Private Sub CollectHeadPartCandidate(hdptFormID As UInteger,
                                         visited As HashSet(Of UInteger),
                                         candidates As List(Of MeshCandidate),
                                         ByRef order As Integer,
                                         warnings As List(Of String),
                                         parentPartType As Integer,
                                         state As NPCVisualState,
                                         Optional useFaceGen As Boolean = False,
                                         Optional flstCache As Dictionary(Of UInteger, FLST_Data) = Nothing,
                                         Optional raceDefaults As HashSet(Of UInteger) = Nothing,
                                         Optional raceHasAnyHeadParts As Boolean = True,
                                         Optional miscToParentEffective As Dictionary(Of UInteger, Integer) = Nothing)
        If hdptFormID = 0UI Then Return
        If visited.Contains(hdptFormID) Then Return
        visited.Add(hdptFormID)

        Dim hdptRec = _pluginManager.GetRecord(hdptFormID)
        If hdptRec Is Nothing OrElse hdptRec.Header.Signature <> "HDPT" Then Return

        Dim hdpt = ParseHdptCached(hdptRec)

        ' Extra parts (type=0/Misc) inherit the parent's type for color treatment.
        ' E.g. a hair extra part mesh needs the same hair palette remap as the main hair.
        ' Path principal: parent>=0 → cascade directo via HNAM recursion.
        ' Path top-level (parent=-1) con raw=0: si el merged list incluye un parent que declara
        ' este Misc en su HNAM (vanilla NPC.PNAM duplica hairlines típicamente), promovemos al
        ' effective de ese parent. Cierra el bug donde Hairline standalone en NPC.PNAM no
        ' cascadeaba si el visit order ponía el Misc antes del parent.
        ' Shared rule = single source of truth with the bake's EnumerateHdptChain.
        Dim effectivePartType = HeadPartResolver.ResolveEffectivePartType(hdpt.PartType, parentPartType, hdptFormID, miscToParentEffective)

        ' Race-membership check: drop HDPTs the engine wouldn't render. The only practical
        ' case this catches today is RNAM=0 HDPTs assigned (via NPC.PNAM) to a non-humanoid
        ' race — e.g. EncRaiderDog01 lists MaleMouthHumanoidDirtyTeethMissing yet the engine
        ' renders no human teeth on raider dogs because RaiderDogRace declares zero head parts.
        ' Humanoid races (HumanRace, GhoulRace, etc.) keep all their RNAM=0 HDPTs as before.
        If flstCache IsNot Nothing AndAlso state IsNot Nothing AndAlso state.RaceFormID <> 0UI Then
            Dim raceOk = HeadPartResolver.IsHdptValidForRace(hdptFormID, state.RaceFormID, state.IsFemale, _pluginManager, flstCache, raceDefaults, raceHasAnyHeadParts, AddressOf ParseHdptCached)
            If Not raceOk Then
                Return
            End If
        End If


        If hdpt.MeshPath <> "" Then
            ' Redirect face-region meshes to their _faceBones.nif variant only for NPCs with
            ' a custom CharGen face (useFaceGen=True). The _faceBones variants are rigged to face
            ' bones (Jaw, LipUpper_L, Cheek_R, etc) enabling FMRS bone transforms to deform the
            ' mesh. NPCs without FaceGen use default race face — no _faceBones redirect needed.
            Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(hdpt.MeshPath)
            Dim originalDictKey = dictKey   ' antes del posible redirect a _faceBones (para log)
            Dim baseDictKeyForFaceBones As String = ""
            If useFaceGen Then
                Dim faceBonesKey = TryGetFaceBonesVariant(dictKey, effectivePartType)
                If faceBonesKey <> "" Then
                    ' Solo HeadRear necesita copia de material desde el .nif base (el _faceBones
                    ' vanilla trae basehumanfemaleskin genérico en lugar de basehumanfemalerear).
                    ' Otros types usan el material del _faceBones tal cual.
                    If effectivePartType = 9 Then baseDictKeyForFaceBones = dictKey
                    dictKey = faceBonesKey
                End If
            End If

            Dim effectiveUsesBodyTexture = ComputeEffectiveUsesBodyTexture(hdpt, hdptFormID, hdptRec, state, logTag:="CBBE-HEADREAR")

            ' Trace del candidato HeadPart: qué HDPT, tipo raw/effective, mesh ORIGINAL vs el
            ' redirect a _faceBones, el TXST (TNAM) y color. Para ojos esto deja ver de qué NIF
            ' sale el shape (femaleeyes.nif vs femaleeyes_faceBones.nif) y qué TNAM trae.
            If Logger.Enabled Then
                Dim hdptEidC = If(hdptRec.EditorID, "")
                Dim rawTypeC = hdpt.PartType
                Dim effTypeC = effectivePartType
                Dim origMeshC = If(hdpt.MeshPath, "")
                Dim finalKeyC = dictKey
                Dim redirectedC = Not String.Equals(originalDictKey, dictKey, StringComparison.OrdinalIgnoreCase)
                Dim tnamC = hdpt.TextureSetFormID
                Dim colorC = hdpt.ColorFormID
                Dim ubtC = effectiveUsesBodyTexture
                Dim ufgC = useFaceGen
                Logger.LogLazy(Function() $"[HDPT-CAND] hdpt=0x{hdptFormID:X8} eid='{hdptEidC}' rawType={rawTypeC} effType={effTypeC} useFaceGen={ufgC} TNAM=0x{tnamC:X8} color=0x{colorC:X8} usesBodyTex={ubtC} faceBonesRedirect={redirectedC} mesh='{origMeshC}' dictKey='{finalKeyC}'")

                ' NOSOTROS redirigimos face→_faceBones: dumpear el material INLINE de AMBOS NIFs
                ' (el original que CK usaría y el _faceBones que cargamos nosotros) para comparar si
                ' difieren en shader/normal/spec. El render solo carga el _faceBones, así que el
                ' original solo se ve acá.
                If redirectedC Then
                    LogNifInlineMaterials(originalDictKey, $"ORIGINAL hdpt=0x{hdptFormID:X8}/{hdptEidC}")
                    LogNifInlineMaterials(dictKey, $"FACEBONES hdpt=0x{hdptFormID:X8}/{hdptEidC}")
                End If
            End If

            candidates.Add(New MeshCandidate With {
                .DictKey = dictKey,
                .BaseDictKeyForFaceBones = baseDictKeyForFaceBones,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MeshCandidateKind.HeadPart,
                .HeadPartType = effectivePartType,
                .HeadPartTypeRaw = hdpt.PartType,
                .HeadPartColorFormID = hdpt.ColorFormID,
                .TextureSetFormID = hdpt.TextureSetFormID,
                .UseSolidTint = (hdpt.Flags And HeadPartFlagUseSolidTint) <> 0,
                .UsesBodyTexture = effectiveUsesBodyTexture,
                .Order = order,
                .RaceMorphTriPath = hdpt.RaceMorphTriPath,
                .ChargenMorphTriPath = hdpt.ChargenMorphTriPath,
                .Hide = (effectivePartType = 7),
                .IsHnamExtra = (parentPartType >= 0)
            })
            order += 1
        End If

        ' Pass the effective type down so nested extras also inherit
        Dim childParentType = If(effectivePartType <> 0, effectivePartType, parentPartType)
        For Each extraPartFormID In hdpt.ExtraPartFormIDs
            CollectHeadPartCandidate(extraPartFormID, visited, candidates, order, warnings, childParentType, state, useFaceGen, flstCache, raceDefaults, raceHasAnyHeadParts, miscToParentEffective)
        Next
    End Sub

    ' Biped object slot bits (verified from wbDefinitionsFO4.pas:3745 wbBipedObjectFlags).
    ' Slot index = bit position + 30, so bit 0 = slot 30, bit 2 = slot 32, bit 16 = slot 46.
    ' Only the bits we actually use for head part occlusion are defined; body / hand slots
    ' (33/34/35) are handled implicitly by the "outfit wins over skin on same slot" loop in
    ' SelectWinningCandidates, no constants needed there.
    Private Const SlotBitHairTop As UInteger = &H1UI         ' Slot 30 - Hair Top      (sombreros, gorros, cualquier headwear)
    Private Const SlotBitHairLong As UInteger = &H2UI        ' Slot 31 - Hair Long     (cascos que cubren el largo del pelo)
    Private Const SlotBitFaceGenHead As UInteger = &H4UI     ' Slot 32 - FaceGen Head  (casco integral / vault helmet — cubre LA CARA entera)
    Private Const SlotBitHeadband As UInteger = &H10000UI    ' Slot 46 - Headband      (bandana / hairband forehead, no cubre cara)
    Private Const SlotBitEyes As UInteger = &H20000UI        ' Slot 47 - Eyes          (glasses, goggles)
    Private Const SlotBitBeard As UInteger = &H40000UI       ' Slot 48 - Beard         (algo equipable que pisa la zona barba)
    Private Const SlotBitMouth As UInteger = &H80000UI       ' Slot 49 - Mouth         (bandana, máscara quirúrgica, gas mask boca)
    ' Slots 50-52 — nombres canónicos en wbDefinitionsFO4.pas:3766-3768. Categorización:
    '   • Neck (50)  → headwear: bandana de cuello, collar, bufanda. Es prenda equipable.
    '   • Ring (51)  → body (mano): anillo, accesorio de mano. Cae en HAND_MASK.
    '   • Scalp (52) → body (cabeza/cuello): overlay que sigue al body skin, no es prenda
    '                  equipable. Tratado como BODY (agregado a BODY_MASK en ClassifyShapeCategory).
    ' Slot 53 (Decapitation) NO se clasifica: es geometría de gore que aparece tras desmembrar
    ' al actor, no una prenda equipable. Slots 54-55 son "Unnamed" en xEdit (sin uso vanilla) —
    ' los dejamos fuera de toda máscara para no asignarlos al toggle equivocado.
    Private Const SlotBitNeck As UInteger = &H100000UI       ' Slot 50 - Neck          (bandana cuello, collar, bufanda)
    Private Const SlotBitRing As UInteger = &H200000UI       ' Slot 51 - Ring          (anillo — body, va en la mano)
    Private Const SlotBitScalp As UInteger = &H400000UI      ' Slot 52 - Scalp         (overlay cabeza/cuello — body, no prenda)
    Private Const SlotBitALArm As UInteger = &H1000UI        ' Slot 42 - [A] L Arm     (over-armor antebrazo izquierdo — bracer, PA L Arm)
    Private Const SlotBitPipboy As UInteger = &H40000000UI   ' Slot 60 - Pipboy        (atado a la muñeca/antebrazo izquierdo)
    ''' <summary>Máscara unificada de bits "headwear": cualquier prenda de cabeza/cara/cuello.
    ''' Usada por ClassifyShapeCategory para categoría Headwear y por ApplyRenderToggleVisibility
    ''' para el toggle "Render headwear". Slots 30-32 (HairTop/HairLong/FaceGenHead) + 46-49
    ''' (Headband/Eyes/Beard/Mouth) + 50 (Neck). Ring (51) y Scalp (52) NO están acá — son body.</summary>
    Private Const HEADWEAR_MASK As UInteger = SlotBitHairTop Or SlotBitHairLong Or SlotBitFaceGenHead Or
                                              SlotBitHeadband Or SlotBitEyes Or SlotBitBeard Or SlotBitMouth Or
                                              SlotBitNeck

    Private Function SelectWinningCandidates(candidates As List(Of MeshCandidate)) As List(Of MeshCandidate)
        Dim selected As New List(Of MeshCandidate)

        ' HDPT type=7 Meatcaps used to be filtered here. Now they pass through to the render
        ' pipeline and are marked in result.ShapeMeatcap so the "Render gore" toggle governs
        ' their visibility uniformly with the BSSubIndex SECTIONCAP/TORSOCAP shapes. The
        ' candidate.Hide flag survives through to ApplyShapeGeometry → ShapeMeatcap mapping.
        Dim hiddenCandidates = candidates.Where(Function(c) c.Hide).ToList()
        Dim visibleCandidates = candidates.ToList()

        ' First pass: resolve slotted candidates.
        ' Per FO4 biped slot spec (wbDefinitionsFO4.pas:3745-3778): slots [U] 36-40 (bits 6-10)
        ' and [A] 41-45 (bits 11-15) are separate layers designed to coexist — the underarmor
        ' declares bits the over-armor pieces partially overlap.
        '
        ' Regla "extended underarmor" (per usuario 2026-04-29): un candidate que declara BODY
        ' (bit 3) o algún bit [U] (6-10) Y simultáneamente algún bit [A] (11-15) es un underarmor
        ' "extendido" cuya mesh cubre los slots [A] declarados. Su geometría incluye piernas /
        ' brazos / torso. NO se puede coexistir con un over-armor [A] puro que reclame los mismos
        ' bits [A] — produciría dos geometrías superpuestas (clip visible). El extended underarmor
        ' RESERVA sus bits [A]: cualquier candidate puro [A] que declare bits ya reservados
        ' se descarta entero.
        '
        ' Caso DN061_LvlGunnerBoss (Gunner): AA_DCGuard_UnderArmor declara slot mask 0xC7F8 =
        ' BODY+[U]LArm+[U]RArm+[A]LLeg+[A]RLeg. Reserva bits 14, 15. Las combat legs (slot 0x4000
        ' / 0x8000) declaran bits 14/15 → se descartan. Las combat torso/arm (bits 11, 12) NO
        ' tocan los reservados → entran normalmente.
        ' (extended-underarmor BODY/[U]/[A] slot masks now live in SlotConflictResolver)

        ' Skin candidates (NPC_.WNAM / RACE.WNAM via state.SkinFormID) representan la base body
        ' geometry del NPC — NO son piezas equipables que compitan por slots con outfits/armor.
        ' xEdit wbDefinitionsFO4.pas:10705 + 11434 confirman que NPC_.WNAM y RACE.WNAM son slots
        ' dedicados ("Skin" ARMO), distintos del inventory de outfits. Cita engine doc Steam/Nexus
        ' habla de "vault suit + something else" — outfit vs outfit, nunca outfit vs body skin.
        ' Conceptualmente: un actor SIEMPRE tiene body mesh; un outfit lo CUBRE visualmente, no
        ' lo desequipa. `unequipall` deja al NPC en NakedTorso/NakedHands, no invisible.
        ' Por lo tanto: Skin candidates bypasean la slot conflict resolution. Siempre se aceptan
        ' enteros, y NO contribuyen a occupiedSlots/shieldedSlots/reservedAbits — quedan fuera
        ' del torneo. El toggle "Render body" + "Render underarmor" decide visibilidad post-hoc.
        Dim skinCandidates = visibleCandidates.Where(Function(c) c.Kind = MeshCandidateKind.Skin).ToList()
        Dim nonSkinCandidates = visibleCandidates.Where(Function(c) c.Kind <> MeshCandidateKind.Skin).ToList()
        For Each skinC In skinCandidates
            selected.Add(skinC)
        Next

        Dim slottedCandidates = nonSkinCandidates.Where(Function(c) c.SlotMask <> 0UI).ToList()

        ' Slot conflict resolution (pass 1a extended-underarmor + pass 1b atomic-mutex last-wins +
        ' pipboy↔[A]LArm mutex) extracted to SlotConflictResolver so the render path and the Edit
        ' Outfit "Create" tab share the SAME engine rules. Winners append to `selected` (skin was
        ' already added above, outside the tournament); occupiedSlots feeds the head-part occlusion
        ' (pass 2) + skin coverage (pass 3) below.
        Dim slotResolution = SlotConflictResolver.ResolveSlotWinners(
            slottedCandidates, Function(c) c.SlotMask, Function(c) c.Order)
        selected.AddRange(slotResolution.Winners)
        Dim occupiedSlots As UInteger = slotResolution.OccupiedSlots

        ' Third pass: add slotless (head parts), hiding based on occupied biped slots.
        '
        ' Slot semantics (verified wbDefinitionsFO4.pas:3745):
        '   30 HairTop      — every hat or helmet (the slot every headwear takes)
        '   31 HairLong     — some helmets that wrap longer hair
        '   32 FaceGenHead  — full helmet that covers the entire face (vault helmet, gas mask)
        '   46 Headband     — bandana / hairband on the forehead, does NOT cover face
        '   48 Beard        — equipable beard slot (rare; covers the beard area when used)
        '   49 Mouth        — bandana / surgical mask / mostacho replacement that covers mouth+beard
        '
        ' Occlusion matrix (head parts of type 0/1 and "extra parts" never occlude — only the
        ' four slotless visual layers below apply):
        '   3 Hair (main+hairlines) : RENDER, GEOMÉTRICA UNIFORME (main y hairline igual). Cada pieza se
        '                     oculta ⟺ COBERTURA TOTAL (el headwear cubre TODOS los slots que la pieza ocupa,
        '                     {30/31}⊆occupiedSlots) O full-mask (32). Gorra [30] sin 31 NO oculta un {30,31}
        '                     (Piper/Moe → el largo asoma, el {30}-hairline redundante sí se oculta); gorro
        '                     [30,31] oculta todo (Caravan); full-mask 32 oculta todo (Mechanist). Las gorras
        '                     NO traen pelo (tela: PiperCapF→PiperCap.BGSM) → el pelo bajo la gorra es del
        '                     FaceGen. Addons NO-pelo (mouth shadow/eyes, biped 32, hairSlotMask=0): sólo
        '                     full-mask. RENDER-ONLY; el bake usa su propia regla CK-fiel.
        '   4 FacialHair    : hidden by FaceGenHead or Beard or Mouth        (covers the beard region)
        '   6 Eyebrows      : hidden by FaceGenHead only                     (full helmet covers brows)
        '   9 HeadRear      : NUNCA se oculta. Es geometría base del cráneo (back of head) que el
        '                     engine renderiza siempre. La regla previa "HairTop AND HairLong" era
        '                     una invención mía sin fuente, hacía desaparecer el back of head en
        '                     NPCs con casco normal (combat helmet, baseball cap). Removida.
        Dim hasHairTop As Boolean = (occupiedSlots And SlotBitHairTop) <> 0UI
        Dim hasHairLong As Boolean = (occupiedSlots And SlotBitHairLong) <> 0UI
        Dim hasFaceGenHead As Boolean = (occupiedSlots And SlotBitFaceGenHead) <> 0UI
        Dim hasBeard As Boolean = (occupiedSlots And SlotBitBeard) <> 0UI
        Dim hasMouth As Boolean = (occupiedSlots And SlotBitMouth) <> 0UI

        ' Pasada 2 — slotless NO-Skin: HeadParts y Attachments (chunks robot/pack via socket).
        ' HeadParts ocluidos por headwear aceptado se MARCAN con flag IsOccludedByHeadwear pero
        ' NO se descartan; ApplyRenderToggleVisibility decide hide en runtime para que "Render
        ' headwear" OFF los destape.
        '
        ' Attachments (NPC_.OBTE chunks) entran acá con SlotMask=0 + Kind=Attachment +
        ' ChunkOmodFormID>0. No participan en slot conflict resolution (mount via socket P-/C-,
        ' no via armor slot). Cuando estaban marcados Kind=Skin (pre-2026-05-15) hacían pasada 0
        ' Y caían acá → double-add (regresión 2026-05-10 Codsworth 12 chunks → winners=24); la
        ' separación en Kind.Attachment elimina ese caso por construcción.
        '
        ' EXCLUSIÓN Kind=Skin sigue siendo necesaria: los Skin con SlotMask=0 ya se aceptaron en
        ' la pasada 0 (skinCandidates) y no deben entrar de nuevo.
        For Each slotlessCandidate In visibleCandidates.Where(Function(c) c.SlotMask = 0UI AndAlso c.Kind <> MeshCandidateKind.Skin).OrderBy(Function(c) c.Order)
            If slotlessCandidate.Kind = MeshCandidateKind.HeadPart Then
                Dim occluded As Boolean = False
                ' Addons (HNAM-extras del parent O Misc top-level raw=0) son siempre exentos de la
                ' occlusion de headwear normal — sólo FaceGenHead (slot 32, casco full-face) los tapa.
                ' Cubre los dos caminos por los que un addon llega al render:
                '   a) HNAM-extra (parent>=0 en CollectHeadPartCandidate) — hairlines, mouth shadow,
                '      AO/wet, etc., independientemente de su raw type. Casos 2026-05-17: Hodges +
                '      gorra perdía hairline raw=Misc; otro hair cuya HNAM declara hairline raw=3
                '      (no Misc) también caía bajo HairTop sin esta exención.
                '   b) Misc top-level (raw=0, parent=-1) — addons standalone en NPC.PNAM/RACE que no
                '      están en HNAM de ningún parent listado (mouth shadow sueltos, etc.).
                ' OCLUSIÓN DE PELO — RENDER, GEOMÉTRICA (cobertura total), UNIFORME a main Y hairline.
                ' LÓGICA: una pieza de pelo se oculta ⟺ el headwear cubre TODAS las regiones (biped 30
                ' HairTop / 31 HairLong) que la pieza ocupa, O es full-mask (slot 32 FaceGenHead = toda la
                ' cabeza). Si una región que el pelo ocupa queda libre, esa parte se ve → no se puede ocultar
                ' la malla entera. Cada pieza por sus propios slots:
                '   {30} (top)   : oculto bajo [30] o [30,31] o 32.
                '   {31} (largo) : oculto bajo [31] o [30,31] o 32.
                '   {30,31} (90%): oculto SÓLO bajo [30,31] o 32. Gorra[30] → el largo asoma → SE MUESTRA.
                ' Ej: Piper/Moe (gorra cubre 30, no 31) → su main {30,31} SE MUESTRA (su {30}-hairline se
                ' oculta, redundante). Caravan (gorro [30,31]) → todo oculto. Mechanist (32) → todo oculto.
                ' Las gorras NO traen pelo (son tela) → el pelo bajo la gorra es el del FaceGen. hairSlotMask
                ' = bits {30→0x1, 31→0x2} de la mesh. Los addons NO-pelo (mouth shadow / eyes, biped 32 →
                ' hairSlotMask=0) caen al else: sólo full-mask los tapa. RENDER-ONLY: el bake usa su regla CK.
                Dim hairSlotMask As UInteger = CandidateHairSlotMask(slotlessCandidate)
                If hairSlotMask <> 0UI Then
                    ' MODELO COMPLEMENTARIO POR PARTICIÓN — pelo (under-helmet de FO4). Una pieza {30,31}
                    ' tiene dos particiones: TOP (biped 30, corona) y LONG (biped 31, abajo). La HAIRLINE
                    ' (HNAM-extra, IsHnamExtra) es el COMPLEMENTO INVERSO del MAIN, por partición:
                    '   MAIN     : una partición se ZAPEA cuando su slot ESTÁ cubierto
                    '              (top → zap si hasHairTop; long → zap si hasHairLong).
                    '   HAIRLINE : una partición se ZAPEA cuando su slot NO está cubierto
                    '              (top → zap si NOT hasHairTop; long → zap si NOT hasHairLong).
                    ' FULL-MASK 32 (hasFaceGenHead): ambos (main y hairline) se ocultan ENTEROS (gana sobre
                    ' cualquier zap parcial). Si AMBAS particiones deben zapearse (Both), la pieza desaparece
                    ' entera: el ring compartido (v30∩v31, que NINGÚN top-/long-only set incluye) sobreviviría
                    ' a un zap parcial, así que se oculta la mesh entera vía IsOccludedByHeadwear en vez de un
                    ' zap Both incompleto. Resultados ({30,31}):
                    '   sin gorro      → main entero visible; hairline Both → oculta entera.
                    '   gorra [30]     → main zap Top (queda long); hairline zap Long (queda TOP = borde frente).
                    '   casco [30,31]  → main Both → oculto entero; hairline None → entera visible (under-helmet).
                    '   full-mask [32] → ambos occluded enteros.
                    ' Piezas de UNA sola partición ({30}-only / {31}-only) no tienen complemento por partición:
                    ' siguen la regla de cobertura total (oculto ⟺ su único slot cubierto), igual para main y
                    ' hairline, más full-mask 32.
                    Dim hasBothHairParts As Boolean =
                        (hairSlotMask And SlotBitHairTop) <> 0UI AndAlso (hairSlotMask And SlotBitHairLong) <> 0UI
                    Dim zapParts As HairZapParts = HairZapParts.None
                    If hasFaceGenHead Then
                        ' Full-mask: toda la cabeza tapada → pieza entera oculta, sin zap.
                        occluded = True
                    ElseIf hasBothHairParts Then
                        ' Pieza {30,31} — MAIN y HAIRLINE se tratan IGUAL, por partición (la hairline COPIA
                        ' al main, sin invertir): zap del TOP si el headwear cubre slot 30, zap del LONG si
                        ' cubre slot 31. Saca la partición cubierta (en la hairline, el top alto que clava;
                        ' queda el largo bajo). Si cubre AMBOS, un zap parcial dejaría el ring compartido →
                        ' oculta entera. (El zap-only de la hairline aplica por el blindaje HasZaps.)
                        If hasHairTop Then zapParts = zapParts Or HairZapParts.Top
                        If hasHairLong Then zapParts = zapParts Or HairZapParts.Long
                        If zapParts = HairZapParts.Both Then
                            occluded = True
                            zapParts = HairZapParts.None
                        End If
                    Else
                        ' Pieza de una sola partición: cobertura total de su único slot (sin complemento).
                        occluded = (hairSlotMask And occupiedSlots) = hairSlotMask
                    End If
                    slotlessCandidate.ZapParts = zapParts
                    ' [HAIRZAP-DIAG] per hair piece: dict mesh, IsHnamExtra, computed mask, occlusion, and
                    ' final ZapParts. Lets us see why a {30,31} hairline (HNAM-extra) diverges from the main:
                    ' which of mask / hasBothHairParts / occluded / hasHairTop/Long drives each partition.
                    If Logger.Enabled Then
                        Dim dkD = slotlessCandidate.DictKey
                        Dim hnamD = slotlessCandidate.IsHnamExtra
                        Dim maskD = hairSlotMask
                        Dim bothD = hasBothHairParts
                        Dim occD = occluded
                        Dim htD = hasHairTop
                        Dim hlD = hasHairLong
                        Dim occSlotsD = occupiedSlots
                        Dim zapD = zapParts
                        Logger.LogLazy(Function() $"[HAIRZAP-DIAG] dict='{dkD}' isHnamExtra={hnamD} hairMask=0x{maskD:X} hasBoth={bothD} occupiedSlots=0x{occSlotsD:X} hasHairTop={htD} hasHairLong={hlD} occluded={occD} -> ZapParts={zapD}")
                    End If
                ElseIf slotlessCandidate.IsHnamExtra OrElse slotlessCandidate.HeadPartTypeRaw = 0 Then
                    ' Addon NO-pelo (mouth shadow / eye AO-wet, biped 32): sólo full-mask lo tapa.
                    occluded = hasFaceGenHead
                Else
                    Select Case slotlessCandidate.HeadPartType
                        Case HeadPartTypeFacialHair
                            occluded = hasFaceGenHead OrElse hasBeard OrElse hasMouth
                        Case 6 ' Eyebrows
                            occluded = hasFaceGenHead
                            ' Type 9 HeadRear: nunca se ocluye por headwear (es base skull geometry).
                    End Select
                End If
                If occluded Then
                    slotlessCandidate.IsOccludedByHeadwear = True
                End If
            End If
            selected.Add(slotlessCandidate)
        Next

        ' Marcar Skin candidates cuya geometría queda cubierta por algún outfit aceptado.
        ' occupiedSlots acumuló los bits de outfits + extended-underarmors (los Skin se aceptaron
        ' al principio sin contribuir a occupiedSlots). Si la SlotMask del Skin intersecta esos
        ' bits, el outfit lo tapa visualmente → RenderHide=True por default; cuando el usuario
        ' apaga "Render underarmor" se destapa para mostrar el body desnudo abajo.
        For Each skinC In skinCandidates
            If (skinC.SlotMask And occupiedSlots) <> 0UI Then
                skinC.IsCoveredByOutfit = True
            End If
        Next

        Return selected.OrderBy(Function(c) c.Order).ToList()
    End Function

    ''' <summary>Per-mesh cache for <see cref="CandidateHairSlotMask"/>, keyed by normalized mesh key
    ''' (candidate.DictKey, already a FilesDictionary key). Hair-slot occupancy is a property of the mesh
    ''' file alone (its BSSubIndexTriShape segmentation), stable across NPCs sharing the same hair mesh,
    ''' so it's worth memoizing.</summary>
    Private ReadOnly _candidateHairSlotMaskCache As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Bits {SlotBitHairTop 0x1 (biped 30), SlotBitHairLong 0x2 (biped 31)} that the candidate's
    ''' source mesh occupies. Drives the RENDER hair-occlusion rule: a hair piece is hidden ⟺ the headwear
    ''' covers ALL the hair slots the piece occupies (mask ⊆ occupiedSlots) OR is a full-mask (slot 32).
    ''' Reads the mesh NIF from FilesDictionary (same path bake/render use: FilesDictionary_class.GetBytes
    ''' on the normalized DictKey), finds each BSSubIndexTriShape, and unions its segment biped objects via
    ''' <see cref="BSTriShapeGeometry.GetBipedObjects"/>. Works for a hair's main mesh and each hairline
    ''' (every hairline is its own candidate/mesh). Non-hair head parts (mouth shadow / eyes → biped 32)
    ''' return 0. If the mesh can't be read / has no segments → 0 (safe under-hide: show the hair).
    ''' RENDER-ONLY: the bake (FaceGenBuilder) keeps its own CK-faithful biped30only rule.</summary>
    Private Function CandidateHairSlotMask(candidate As MeshCandidate) As UInteger
        If candidate Is Nothing Then Return 0UI
        Dim meshKey = NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If String.IsNullOrEmpty(meshKey) Then Return 0UI

        Dim cached As UInteger
        If _candidateHairSlotMaskCache.TryGetValue(meshKey, cached) Then Return cached

        Dim result As UInteger = 0UI
        Try
            Dim bytes = FilesDictionary_class.GetBytes(meshKey)
            If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                Dim nif As New Nifcontent_Class_Manolo()
                nif.Load_Manolo(bytes)
                For Each shp In nif.GetShapes()
                    Dim subIdx = TryCast(shp, BSSubIndexTriShape)
                    If subIdx Is Nothing Then Continue For
                    Dim biped = BSTriShapeGeometry.GetBipedObjects(subIdx)
                    If biped.Contains(30UI) Then result = result Or SlotBitHairTop
                    If biped.Contains(31UI) Then result = result Or SlotBitHairLong
                Next
            End If
        Catch ex As Exception
            ' Mesh unreadable / unknown blocks / no segments → 0 (safe under-hide: show the hair).
            result = 0UI
        End Try

        _candidateHairSlotMaskCache(meshKey) = result
        Return result
    End Function

    Private Shared Function CandidateKindRank(kind As MeshCandidateKind) As Integer
        Select Case kind
            Case MeshCandidateKind.Outfit
                Return 2
            Case MeshCandidateKind.Skin
                Return 1
            Case Else
                Return 0
        End Select
    End Function

    Private Shared Function MatchesRace(recordRaceFormID As UInteger, npcRaceFormID As UInteger) As Boolean
        If recordRaceFormID = 0UI OrElse npcRaceFormID = 0UI Then Return True
        Return recordRaceFormID = npcRaceFormID
    End Function

    ''' <summary>Categoriza un MeshCandidate per los toggles diagnósticos de visibilidad.
    ''' Usa el slot mask del candidate (de BOD2/BODT) y su Kind. La categoría se mapea a
    ''' RenderHide en ApplyRenderToggleVisibility según el estado de los CheckBoxes.</summary>
    Private Shared Function ClassifyShapeCategory(candidate As MeshCandidate) As ShapeRenderCategory
        If candidate.Kind = MeshCandidateKind.HeadPart Then Return ShapeRenderCategory.HeadPart

        ' BODY_BIT cubre el torso (slot 33). Scalp (slot 52) es overlay de cabeza/cuello que
        ' sigue al body skin, no una prenda — agrupado con BODY así un Scalp Skin cae en
        ' BodySkin igual que el torso, y un Scalp Outfit cae en Underarmor. Caso real raro
        ' pero la semántica del bit es "geometría de body, no equipable".
        Const BODY_MASK As UInteger = (1UI << 3) Or SlotBitScalp
        Dim U_MASK As UInteger = 0UI
        For b = 6 To 10 : U_MASK = U_MASK Or (1UI << b) : Next
        Dim A_MASK As UInteger = 0UI
        For b = 11 To 15 : A_MASK = A_MASK Or (1UI << b) : Next
        ' Slot 34 (L Hand) + 35 (R Hand) son las manos del actor. Slot 51 (Ring) es un accesorio
        ' que va EN la mano — categóricamente body, mismo toggle visual que glove/hand.
        Const HAND_MASK As UInteger = (1UI << 4) Or (1UI << 5) Or SlotBitRing

        Dim slot = candidate.SlotMask
        Dim touchesBody = (slot And BODY_MASK) <> 0UI
        Dim touchesU = (slot And U_MASK) <> 0UI
        Dim touchesA = (slot And A_MASK) <> 0UI
        Dim touchesHand = (slot And HAND_MASK) <> 0UI
        Dim touchesHeadwear = (slot And HEADWEAR_MASK) <> 0UI
        Dim touchesBodyParts = touchesBody OrElse touchesU OrElse touchesA OrElse touchesHand

        ' Headwear: Kind=Outfit con bits exclusivos cabeza/cara (HairTop/HairLong/FaceGenHead/
        ' Headband/Eyes/Beard/Mouth) y SIN tocar bits del cuerpo. Si toca bits cuerpo + cabeza
        ' (raro, ej. casco-cuello combinado) gana la categoría de cuerpo — el toggle headwear no
        ' debería desaparecer una pieza que también cubre torso. Evaluar antes que las otras
        ' porque las otras no chequean bits 16-19 que algunos headwear (Headband) usan en exclusiva.
        If touchesHeadwear AndAlso Not touchesBodyParts AndAlso candidate.Kind = MeshCandidateKind.Outfit Then Return ShapeRenderCategory.Headwear
        ' Body skin desnudo: Kind=Skin con BODY (cubre torso+piernas+pies en FO4 — no hay slot feet).
        If touchesBody AndAlso candidate.Kind = MeshCandidateKind.Skin Then Return ShapeRenderCategory.BodySkin
        ' Naked hands: Skin con bits hand y sin BODY.
        If touchesHand AndAlso candidate.Kind = MeshCandidateKind.Skin Then Return ShapeRenderCategory.NakedHands
        ' Underarmor outfit: Kind=Outfit con BODY o [U] (AAClothesCait, fatigues, etc.).
        If (touchesBody OrElse touchesU) AndAlso candidate.Kind = MeshCandidateKind.Outfit Then Return ShapeRenderCategory.Underarmor
        ' Glove de outfit: Outfit con bits hand sin BODY/[U].
        If touchesHand AndAlso candidate.Kind = MeshCandidateKind.Outfit Then Return ShapeRenderCategory.GloveOutfit
        ' [A] puro: declara algún bit [A] sin BODY/[U].
        If touchesA Then Return ShapeRenderCategory.ArmorOver
        ' Pipboy (slot 60 / 0x40000000) — accesorio de antebrazo izq. que el engine vanilla
        ' monta hardcoded en el player. Como NPC outfit puede aparecer y debe respetar el toggle
        ' "Render armor". No declara bits [A], por eso lo agrupamos acá explícito.
        If (slot And &H40000000UI) <> 0UI AndAlso candidate.Kind = MeshCandidateKind.Outfit Then Return ShapeRenderCategory.ArmorOver
        ' Resto (accessories 16+ raros, shapes sin slot, etc.).
        Return ShapeRenderCategory.Other
    End Function

    ''' <summary>Strict per-ARMA race match: the ARMA's RaceFormID equals the NPC's race, or its
    ''' AdditionalRaces (MODL) include it. Unified rule used by the render AND the skin/outfit/item
    ''' pickers. The permissive "arma.RaceFormID = 0 → any race" clause was REMOVED per user
    ''' 2026-05-24: a load-order sweep found 0 of 1084 ARMAs with RaceFormID=0 (all declare a race),
    ''' so the clause was dead — strict is preferred and unifies render + pickers on one rule. The
    ''' npcRaceFormID=0 guard stays: it keeps a degenerate NPC whose race didn't resolve from
    ''' rendering naked.</summary>
    Private Shared Function ArmorAddonMatchesRace(arma As ARMA_Data, npcRaceFormID As UInteger) As Boolean
        If npcRaceFormID = 0UI Then Return True
        If arma.RaceFormID = npcRaceFormID Then Return True
        Return arma.AdditionalRaces.Contains(npcRaceFormID)
    End Function

    ''' <summary>Human-readable decode of a wbModelFlags byte (MO2F/MO3F/MO4F/MO5F): bit 0x01 =
    ''' FaceBones, 0x02 = 1stPerson (TES5Edit wbDefinitionsFO4.pas:4622). Diagnostic only (used by the
    ''' [ARMA-MODELFLAGS] log).</summary>
    Private Shared Function DescribeModelFlags(b As Byte) As String
        If b = 0 Then Return "none"
        Dim parts As New List(Of String)
        If (b And &H1) <> 0 Then parts.Add("FaceBones")
        If (b And &H2) <> 0 Then parts.Add("1stPerson")
        Dim extra = b And Not CByte(&H3)
        If extra <> 0 Then parts.Add($"unk0x{extra:X2}")
        Return String.Join("|", parts)
    End Function

    ''' <summary>Resuelve el AddonIndex selector para una ARMO multi-addon.
    ''' Devuelve un Integer con el INDX a forzar (e.g. Gunner Heavy = 2), o `Nothing`
    ''' (= "cargar TODOS los addons compatibles" — comportamiento default del engine).
    '''
    ''' El engine vanilla carga TODAS las ARMAs del array Models filtradas por raza/género.
    ''' La única forma de seleccionar UNA específica es vía OMOD AddonIndex Property (idx 7
    ''' de wbArmorPropertyEnum) disparada por una OBTS combination cuya Keywords matcheen
    ''' el contexto (LVLI.LLKC). Si NO hay tal match → cargar todos. Esto distingue:
    '''   - Caso Sturgess/Wastelander Heavy: ARMO empaqueta torso + gloves (multi-piece set)
    '''     sin keywords contextuales → cargar todos los addons.
    '''   - Caso Gunner Combat Torso: keyword `if_tmp_armor_Heavy` → OBTS combo "Pesado" →
    '''     OMOD `mod_armor_Combat_Torso_Size_C` con AddonIndex Property = 2 → cargar SOLO INDX=2.
    '''
    ''' BaseAddonIndex (FNAM byte 2-3) NO se usa como filtro per se — es el "default address"
    ''' al que apunta el ARMO si nadie lo modifica, pero el engine sigue cargando los demás
    ''' addons salvo override. Por eso lo ignoramos como selector exclusivo.
    '''
    ''' Spec: wbDefinitionsFO4.pas:6187-6192 (Models = INDX+MODL solamente, sin flag de exclusión),
    ''' :1192-1245 (wbOBTEAddonIndexToStr describe override). Memoria arch_arma_sculpt_rule.md
    ''' confirma flujo Gunner como caso single-winner via OMOD chain.</summary>
    Private Function ResolveEffectiveAddonIndex(armo As ARMO_Data, ctxKeywords As List(Of UInteger)) As Integer?
        ' OBTS combinations override sólo cuando hay keyword match con el contexto.
        If ctxKeywords Is Nothing OrElse ctxKeywords.Count = 0 OrElse armo.Combinations Is Nothing Then
            Return Nothing
        End If

        Dim effectiveIdx As Integer = -1
        For Each combo In armo.Combinations
            If combo.Keywords Is Nothing OrElse combo.Keywords.Count = 0 Then Continue For
            Dim matches = False
            For Each kw In combo.Keywords
                If ctxKeywords.Contains(kw) Then
                    matches = True
                    Exit For
                End If
            Next
            If Not matches Then Continue For

            ' Layer 1: la OBTS combination misma puede dictar el AddonIndex via su s16
            ' "Parent Combination Index" (wbDefinitionsFO4.pas:5874). -1 = "no override desde la
            ' OBTS, dejar que un OMOD include lo decida". ≥0 = la combination fija el AddonIndex.
            If combo.ParentCombinationIndex >= 0 Then
                effectiveIdx = combo.ParentCombinationIndex
            End If

            ' Layer 2: cada OMOD include dentro de la combination puede sobrescribir via su
            ' AddonIndex Property. wbDefinitionsFO4.pas:5710+5842 — FunctionType=0 SET (overwrite),
            ' FunctionType=2 ADD (add to running value). Vanilla dump v2 (2026-05-10): 59 SET
            ' casos + 10 ADD casos confirman ambos. Walk ops en orden de declaración del OMOD.
            For Each omodFid In combo.IncludeOMODFormIDs
                Dim omodRec = _pluginManager.GetRecord(omodFid)
                If omodRec Is Nothing OrElse omodRec.Header.Signature <> "OMOD" Then Continue For
                Dim omod = CraftingRecordParsers.ParseOMOD(omodRec, _pluginManager)
                For Each addonOp In omod.GetAddonIndexOps()
                    Dim opLabel = If(addonOp.IsSet, "SET", "ADD")
                    Dim oldIdx = effectiveIdx
                    If addonOp.IsSet Then
                        effectiveIdx = addonOp.Value
                    Else
                        ' ADD over a still-uninitialized index treats the running base as 0
                        ' (engine convention: ADD without prior SET = absolute value).
                        effectiveIdx = If(effectiveIdx >= 0, effectiveIdx, 0) + addonOp.Value
                    End If
                Next
            Next
        Next

        If effectiveIdx >= 0 Then Return effectiveIdx
        Return Nothing
    End Function

    Private Sub LoadNifShapes(candidate As MeshCandidate, state As NPCVisualState, loadedNifs As Dictionary(Of String, Nifcontent_Class_Manolo), result As PreviewResolutionResult,
                              Optional sculptToApply As List(Of ARMA_BoneScaleDelta) = Nothing,
                              Optional sculptSourceFormID As UInteger = 0)
        Dim dictKey = NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If dictKey = "" Then Return

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dictKey, loc) Then Return

        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return

            ' Parse a fresh NIF per candidate. Multi-instance robot chunks (Mr Handy 3 arms,
            ' 3 eyes) point to the same DictKey but each render-instance must own its own
            ' NIF + IRenderableShape so per-shape mutations (sculpt, morph, GPU upload) don't
            ' bleed across instances.
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim trackChunkNif = candidate.ChunkOmodFormID <> 0UI
            Dim trackCandidateNif = trackChunkNif OrElse candidate.SlotMask = SlotBitPipboy
            If trackChunkNif Then
                ' Keep one representative parsed NIF per DictKey only for chunk-mount consumers.
                loadedNifs(dictKey) = nif
            End If
            If trackCandidateNif Then
                ' Track the candidate↔NIF link only for paths that need the exact instance
                ' downstream (chunk mounting / pipboy synthetic skin).
                result.CandidateNif(candidate) = nif
            End If

            Dim shapes = NifRenderableShape.FromNif(nif)
            Dim logEnabled = Logger.Enabled

            ' Multi-instance bone rename: chunks robot mounteados en P-X|<apIdx> traen
            ' bone references al set |0 nativo del NIF. Cuando MountApIdx > 0, hay que
            ' redirigir los bone references al set |<apIdx> del skeleton del actor (los 3
            ' sets |0/|1/|2 ya existen en el skeleton — verificado en log [SKEL-PRE]).
            ' Mutamos NiNode.Name.String solo de los bones referenciados por los shapes,
            ' sin tocar el resto del NIF. Reescritura quirúrgica per-instancia.
            If candidate.ChunkOmodFormID <> 0UI AndAlso candidate.MountApIdx > 0 Then
                RenameShapeBoneIndices(shapes, candidate.MountApIdx)
                ' Fix Bug HIGH #1+#2: sub-sockets que esta chunk NIF expone también necesitan
                ' rename del ParentBoneName, sino sub-chunks se anclan al bone |0 equivocado.
                RenameSubSocketParentBones(nif, candidate.MountApIdx)
            End If

            ' RESUELTO (2026-06-14): los shapes skinned a bones del ACTOR (PackBase brahmin: Pelvis/
            ' Spine; brazos Mr Handy) NO necesitan el MountSocket — cabalgan los bones del actor que YA
            ' están posicionados. La "mala posición" que se veía eran los bones PRIVADOS del chunk
            ' (lag bones, etc.), ahora colocados bien por InjectChunkBonesIntoLiveSkeleton (regla:
            ' A=actorWorld(huesoCompartido)×bind; privados en A×inv(bind) — ver memoria
            ' arch_injected_bone_shared_bone_inference; brahmin validado sin regresión).
            ' Por eso ApplySocketToBindTransforms quedó comentado: aplicar el socket a shapes que
            ' cabalgan el actor DISTORSIONA (verificado 2026-05-13: ambos órdenes rompieron todo).
            ' NO re-habilitar. (Pendiente: validar visualmente Mr Handy/Codsworth multi-instancia.)
            'If candidate.MountSocket IsNot Nothing Then
            '    ApplySocketToBindTransforms(shapes, candidate.MountSocket)
            'End If

            If logEnabled Then
                Dim candFidLog = candidate.SourceFormID
                Dim chunkOmodLog = candidate.ChunkOmodFormID
                Dim dkLog = dictKey
                Dim shapesCountLog = shapes.Count
                Dim nifHashLog = nif.GetHashCode()
                Logger.LogLazy(Function() $"[LOAD-NIF] candFid=0x{candFidLog:X8} chunkOmod=0x{chunkOmodLog:X8} dictKey='{dkLog}' shapes={shapesCountLog} nifHash={nifHashLog}")
            End If

            ' [PIPBOY-DIAG] Para candidates con bit Pipboy (slot 60 / 0x40000000), dump per-shape
            ' IsSkinned + lista de BSConnectPoint::Parents del NIF. Si IsSkinned=False y hay un
            ' parent socket (típicamente "P-PipBoy" en LArm_skin del esqueleto), el render debería
            ' anclar el mesh a ese socket; si IsSkinned=True y la pose del actor es la default,
            ' el mesh debería seguir al bone correspondiente. "Pipboy en el suelo" puede ser:
            '   a) no skinned + sin parent socket → mesh queda en world-origin de su NIF.
            '   b) skinned a bones que el esqueleto del actor no tiene → SSBO bone matrices
            '      colapsan al origin.
            '   c) socket declarado pero el chunk-mount resolver no lo aplica (sólo lo hace para
            '      candidates con ChunkOmodFormID; outfits regulares no pasan por mount-resolver).
            If logEnabled AndAlso (candidate.SlotMask And &H40000000UI) <> 0UI Then
                Dim dkLog = dictKey
                Dim shapesCountLog = shapes.Count
                Dim slotL = candidate.SlotMask.ToString("X8")
                Dim armoL = candidate.SourceFormID
                Dim armaL = candidate.ArmorAddonFormID
                Logger.LogLazy(Function() $"[PIPBOY-DIAG] candidate ARMO=0x{armoL:X8} ARMA=0x{armaL:X8} slot=0x{slotL} mesh='{dkLog}' shapes={shapesCountLog}")
                For Each sh In shapes
                    Dim shName = If(sh.ShapeName, "")
                    Dim isSk = sh.IsSkinned
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   shape='{shName}' IsSkinned={isSk}")
                Next
                Try
                    Dim parents = BSConnectPointReader.ReadParents(nif)
                    If parents Is Nothing OrElse parents.Count = 0 Then
                        Logger.LogLazy(Function() "[PIPBOY-DIAG]   BSConnectPoint::Parents = (none declared in NIF)")
                    Else
                        For Each p In parents
                            Dim pn = p.Name
                            Dim parn = p.ParentBoneName
                            Dim pt = p.Translation
                            Logger.LogLazy(Function() $"[PIPBOY-DIAG]   ConnectPointParent name='{pn}' parentBone='{parn}' T=({pt.X:F3},{pt.Y:F3},{pt.Z:F3})")
                        Next
                    End If
                Catch ex As Exception
                    Dim msg = ex.Message
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   BSConnectPoint::Parents READ EXCEPTION: {msg}")
                End Try
                Try
                    Dim children = BSConnectPointReader.ReadChildren(nif)
                    If children.PointNames Is Nothing OrElse children.PointNames.Count = 0 Then
                        Logger.LogLazy(Function() "[PIPBOY-DIAG]   BSConnectPoint::Children = (none declared in NIF)")
                    Else
                        Dim skFlag = children.Skinned
                        Dim pointsStr = String.Join(",", children.PointNames)
                        Logger.LogLazy(Function() $"[PIPBOY-DIAG]   ConnectPointChildren skinnedFlag={skFlag} points=[{pointsStr}]")
                    End If
                Catch ex As Exception
                    Dim msg = ex.Message
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   BSConnectPoint::Children READ EXCEPTION: {msg}")
                End Try
            End If

            ' Diagnostic: dump the raw shader of every shape STRAIGHT FROM THE NIF, before any
            ' material copy or override runs. Lets us see whether the engine's _faceBones variant
            ' carries FaceTint shaders or genérico Default — answers the Ghoul question (why does
            ' TryApplyFaceTints find no FaceTint mesh after load).
            If logEnabled Then
                For Each shape In shapes
                    MaterialResolver.EnsureShapeMaterialResolved(shape)
                    Dim rawMatPath As String = ""
                    Dim rawAT As String = "?"
                    Dim rawATRef As String = "?"
                    Dim rawABM As String = "?"
                    Dim shapeMat = shape.ShapeMaterial
                    If shapeMat IsNot Nothing Then
                        rawMatPath = If(shapeMat.path, "")
                        If shapeMat.material IsNot Nothing Then
                            rawAT = shapeMat.material.AlphaTest.ToString()
                            rawATRef = shapeMat.material.AlphaTestRef.ToString()
                            rawABM = shapeMat.material.AlphaBlendMode.ToString()
                        End If
                    End If
                    Dim rawHasNiAlp As String = "?"
                    If shape.NifShape IsNot Nothing AndAlso shape.NifShape.AlphaPropertyRef IsNot Nothing Then
                        rawHasNiAlp = (shape.NifShape.AlphaPropertyRef.Index <> -1).ToString()
                    End If
                    Dim shapeNameLog = shape.ShapeName
                    Dim rawAtLog = rawAT
                    Dim rawAtRefLog = rawATRef
                    Dim rawAbmLog = rawABM
                    Dim rawHasNiAlpLog = rawHasNiAlp
                    Dim rawPathLog = rawMatPath
                    Logger.LogLazy(Function() $"[ALPHA-PRE] shape='{shapeNameLog}' path='{rawPathLog}' AT={rawAtLog} ATRef={rawAtRefLog} ABM={rawAbmLog} hasNiAlp={rawHasNiAlpLog}")
                Next
            End If

            ' Sólo HeadRear: copia material part-específico desde el .nif base a los shapes del
            ' _faceBones (que vanilla autoreó con material genérico basehumanfemaleskin).
            CopyBaseMaterialsToFaceBonesShapes(candidate, shapes)

            ApplyShapeMaterialOverrides(candidate, state, shapes)

            ' Diagnostic: dump the shader AFTER both passes (CopyBaseMaterialsToFaceBonesShapes
            ' for HeadRear + ApplyShapeMaterialOverrides for everyone). Pairing with the
            ' [NIF-LOAD-RAW] above lets us see if either pass mutated the shader type.
            If logEnabled Then
                For Each shape In shapes
                    Dim postPath As String = ""
                    Dim postAT As String = "?"
                    Dim postATRef As String = "?"
                    Dim postABM As String = "?"
                    Dim shapeMat2 = shape.ShapeMaterial
                    If shapeMat2 IsNot Nothing Then
                        postPath = If(shapeMat2.path, "")
                        If shapeMat2.material IsNot Nothing Then
                            postAT = shapeMat2.material.AlphaTest.ToString()
                            postATRef = shapeMat2.material.AlphaTestRef.ToString()
                            postABM = shapeMat2.material.AlphaBlendMode.ToString()
                        End If
                    End If
                    Dim postHasNiAlp As String = "?"
                    If shape.NifShape IsNot Nothing AndAlso shape.NifShape.AlphaPropertyRef IsNot Nothing Then
                        postHasNiAlp = (shape.NifShape.AlphaPropertyRef.Index <> -1).ToString()
                    End If
                    Dim shapeNameLog2 = shape.ShapeName
                    Dim postPathLog = postPath
                    Dim postAtLog = postAT
                    Dim postAtRefLog = postATRef
                    Dim postAbmLog = postABM
                    Dim postHasNiAlpLog = postHasNiAlp
                    Logger.LogLazy(Function() $"[ALPHA-POST] shape='{shapeNameLog2}' path='{postPathLog}' AT={postAtLog} ATRef={postAtRefLog} ABM={postAbmLog} hasNiAlp={postHasNiAlpLog}")
                Next
            End If

            ' Convert the externally-determined sculpt-to-apply (per the slot-based rule
            ' computed in ResolvePreviewVariant) to a Dict(boneName -> Vec3). This is NOT the
            ' candidate's own ArmaBoneScaleDeltas — it's whatever sculpt SOURCE applies to this
            ' candidate's shapes (could be a slot-33 BODY underarmor's SCLP, a [U] piece's SCLP
            ' if the shape covers the matching [A] slot, or Nothing if rule says no scaling).
            Dim armaSculptDict As Dictionary(Of String, System.Numerics.Vector3) = Nothing
            If sculptToApply IsNot Nothing AndAlso sculptToApply.Count > 0 Then
                armaSculptDict = New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
                For Each bd In sculptToApply
                    armaSculptDict(bd.BoneName) = New System.Numerics.Vector3(bd.DeltaX, bd.DeltaY, bd.DeltaZ)
                Next
            End If

            ' Compute render category once per candidate (igual para todos sus shapes).
            Dim category As ShapeRenderCategory = ClassifyShapeCategory(candidate)

            ' Track shape -> dict key for TRI lookup, plus explicit HDPT TRI paths if present.
            ' Also: shape -> sculpt source FormID + shape -> sculpt deltas (for per-skeleton sculpt).
            ' ShapeArmaFormID is the FormID of the SCULPT SOURCE (not the candidate's own ARMA),
            ' so that shapes from different candidates pointing to the same source share a skeleton.
            For Each shape In shapes
                result.MeshDictKeys(shape) = dictKey
                result.ShapeArmaFormID(shape) = sculptSourceFormID
                result.ShapeCategory(shape) = category
                result.ShapeCoveredByOutfit(shape) = candidate.IsCoveredByOutfit
                result.ShapeOccludedByHeadwear(shape) = candidate.IsOccludedByHeadwear
                result.ShapeZapHairParts(shape) = candidate.ZapParts
                result.ShapeUsesBodyTexture(shape) = candidate.UsesBodyTexture
                ' HDPT type=7 Meatcaps (CK enum 7=Meatcaps, ver wbDefinitionsFO4 + comment en
                ' CollectHeadPartCandidate). Confirmed por estar en enum oficial de Bethesda;
                ' mismo nivel de certeza que BSDismemberBodyPartType SECTIONCAP/TORSOCAP. La
                ' clasificación por geometría (ClassifyShapeMeatcap) corre después en el loop
                ' de renderData.Shapes y puede sobreescribir esto si la shape ALSO tiene sub-
                ' segments meatcap — no es un problema porque ambos se gobiernan por el mismo
                ' toggle, solo cambia el log.
                If candidate.Hide Then
                    result.ShapeMeatcap(shape) = MeatcapClassification.Confirmed
                End If
                If armaSculptDict IsNot Nothing Then
                    result.ShapeArmaSculpt(shape) = armaSculptDict
                End If
                If candidate.Kind = MeshCandidateKind.HeadPart Then
                    If Not String.IsNullOrEmpty(candidate.ChargenMorphTriPath) Then
                        result.ShapeChargenTriPaths(shape) = candidate.ChargenMorphTriPath
                    End If
                    If Not String.IsNullOrEmpty(candidate.RaceMorphTriPath) Then
                        result.ShapeRaceMorphTriPaths(shape) = candidate.RaceMorphTriPath
                    End If
                End If
            Next

            ' DIAG: dump shape properties for chunk candidates (multi-instance debug).
            ' We want to verify what bone names the shape ACTUALLY references and if there's
            ' any anchor/transform info we're not consuming. Goal: figure out if the shape
            ' carries '|N' suffix already, or if engine adds it via something else.
            If logEnabled AndAlso candidate.ChunkOmodFormID <> 0UI Then
                Dim cFid = candidate.ChunkOmodFormID
                Dim apIdx = candidate.MountApIdx
                Dim sock = candidate.MountSocket
                Dim sockDesc As String
                If sock IsNot Nothing Then
                    Dim qx = sock.Rotation.X, qy = sock.Rotation.Y, qz = sock.Rotation.Z, qw = sock.Rotation.W
                    sockDesc = $"name='{sock.Name}' parentBone='{sock.ParentBoneName}' T=({sock.Translation.X:F2},{sock.Translation.Y:F2},{sock.Translation.Z:F2}) Quat(x,y,z,w)=({qx:F4},{qy:F4},{qz:F4},{qw:F4}) S={sock.Scale:F3}"
                Else
                    sockDesc = "Nothing"
                End If
                Dim rootNode = nif.GetRootNode()
                Dim rootDesc As String
                Dim rootIsIdentity As Boolean = False
                If rootNode IsNot Nothing Then
                    Dim r = rootNode.Rotation
                    Dim rt = rootNode.Translation
                    Dim rs = rootNode.Scale
                    Const eps As Single = 0.0001F
                    rootIsIdentity = (Math.Abs(rt.X) < eps AndAlso Math.Abs(rt.Y) < eps AndAlso Math.Abs(rt.Z) < eps AndAlso
                                      Math.Abs(rs - 1.0F) < eps AndAlso
                                      Math.Abs(r.M11 - 1.0F) < eps AndAlso Math.Abs(r.M12) < eps AndAlso Math.Abs(r.M13) < eps AndAlso
                                      Math.Abs(r.M21) < eps AndAlso Math.Abs(r.M22 - 1.0F) < eps AndAlso Math.Abs(r.M23) < eps AndAlso
                                      Math.Abs(r.M31) < eps AndAlso Math.Abs(r.M32) < eps AndAlso Math.Abs(r.M33 - 1.0F) < eps)
                    Dim idTag = If(rootIsIdentity, "IDENTITY", "NON-IDENTITY")
                    rootDesc = $"name='{rootNode.Name?.String}' {idTag} T=({rt.X:F4},{rt.Y:F4},{rt.Z:F4}) S={rs:F4} R=[{r.M11:F4},{r.M12:F4},{r.M13:F4} | {r.M21:F4},{r.M22:F4},{r.M23:F4} | {r.M31:F4},{r.M32:F4},{r.M33:F4}]"
                Else
                    rootDesc = "Nothing"
                End If
                Logger.LogLazy(Function() $"[CHUNK-PROP] omod=0x{cFid:X8} apIdx={apIdx} socket={sockDesc}")
                Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root: {rootDesc}")

                ' [DIAG-ROOT] NIF root global = walk hacia arriba desde root (es solo root.local).
                ' Para chunks con root NON-IDENTITY este es exactamente el transform que el render
                ' está IGNORANDO (SkinningHelper:151-156 fuerza GlobalTransform=Identity para skinned).
                If rootNode IsNot Nothing Then
                    Try
                        Dim rootGlobal = Transform_Class.GetGlobalTransform(rootNode, nif)
                        Dim rg = rootGlobal.Rotation
                        Dim rgt = rootGlobal.Translation
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root.computedGlobal: T=({rgt.X:F4},{rgt.Y:F4},{rgt.Z:F4}) S={rootGlobal.Scale:F4} R=[{rg.M11:F4},{rg.M12:F4},{rg.M13:F4} | {rg.M21:F4},{rg.M22:F4},{rg.M23:F4} | {rg.M31:F4},{rg.M32:F4},{rg.M33:F4}]")
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root.computedGlobal EXCEPTION: {ex.Message}")
                    End Try
                End If

                ' DIAG sub-sockets/children: dump BSConnectPoint::Parents (sub-sockets que el chunk
                ' EXPONE para que otro chunk se monte encima — ej. HandLeftProtectronClaw expone
                ' P-ModHandLeftProtectronArmor donde se mountea el armor) y BSConnectPoint::Children
                ' (lo que el chunk consume — el "C-X" que matchea contra algún P-X del host o de
                ' otro chunk previo). Sin estos datos el lookup AP→socket por strings es ciego.
                Try
                    Dim subSockets = BSConnectPointReader.ReadParents(nif)
                    If subSockets IsNot Nothing AndAlso subSockets.Count > 0 Then
                        Dim subSocketNames = String.Join(", ", subSockets.Select(Function(s) $"'{s.Name}'(parent='{s.ParentBoneName}')"))
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif EXPOSES sub-sockets({subSockets.Count}): [{subSocketNames}]")
                    Else
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif EXPOSES sub-sockets(0)")
                    End If
                Catch
                End Try
                Try
                    Dim children = BSConnectPointReader.ReadChildrenNames(nif)
                    If children IsNot Nothing AndAlso children.Count > 0 Then
                        Dim childList = String.Join(", ", children.Select(Function(c) $"'{c}'"))
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif CONSUMES children({children.Count}): [{childList}]")
                    Else
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif CONSUMES children(0)")
                    End If
                Catch
                End Try

                For Each shape In shapes
                    Dim sh = shape
                    Dim shapeName = sh.ShapeName
                    Dim niShape = sh.NifShape
                    Dim niShapeT = "<no-transform>"
                    Dim ts = TryCast(niShape, NiflySharp.Blocks.NiAVObject)
                    If ts IsNot Nothing Then
                        Dim r = ts.Rotation
                        niShapeT = $"T=({ts.Translation.X:F2},{ts.Translation.Y:F2},{ts.Translation.Z:F2}) S={ts.Scale:F3} R=[{r.M11:F3},{r.M12:F3},{r.M13:F3} | {r.M21:F3},{r.M22:F3},{r.M23:F3} | {r.M31:F3},{r.M32:F3},{r.M33:F3}]"
                    End If

                    ' [DIAG-CHAIN] Cadena del shape NiAVObject hacia el root, con cada local.
                    ' Aporta info sobre intermedios entre shape y root (no son raros — Bethesda
                    ' a veces mete NiNodes wrapper con offsets). El render skinned actualmente
                    ' compone esta cadena y la fuerza a Identity (SkinningHelper:151-156).
                    Try
                        Dim curNode = TryCast(niShape, NiflySharp.Blocks.NiAVObject)
                        Dim depth As Integer = 0
                        While curNode IsNot Nothing
                            Dim cn = curNode
                            Dim cName = If(cn.Name?.String, "<null>")
                            Dim cT = cn.Translation
                            Dim cR = cn.Rotation
                            Dim cS = cn.Scale
                            Dim isRoot = (rootNode IsNot Nothing AndAlso ReferenceEquals(cn, rootNode))
                            Dim d = depth, isRootCap = isRoot, cNameCap = cName, cTcap = cT, cRcap = cR, cScap = cS
                            Const eps As Single = 0.0001F
                            Dim cIsId = (Math.Abs(cT.X) < eps AndAlso Math.Abs(cT.Y) < eps AndAlso Math.Abs(cT.Z) < eps AndAlso
                                         Math.Abs(cS - 1.0F) < eps AndAlso
                                         Math.Abs(cR.M11 - 1.0F) < eps AndAlso Math.Abs(cR.M12) < eps AndAlso Math.Abs(cR.M13) < eps AndAlso
                                         Math.Abs(cR.M21) < eps AndAlso Math.Abs(cR.M22 - 1.0F) < eps AndAlso Math.Abs(cR.M23) < eps AndAlso
                                         Math.Abs(cR.M31) < eps AndAlso Math.Abs(cR.M32) < eps AndAlso Math.Abs(cR.M33 - 1.0F) < eps)
                            Dim cIdTag = If(cIsId, "ID", "NON-ID")
                            Logger.LogLazy(Function() $"[CHUNK-PROP]     shape-chain[{d}] '{cNameCap}'{If(isRootCap, " (ROOT)", "")} {cIdTag} T=({cTcap.X:F4},{cTcap.Y:F4},{cTcap.Z:F4}) S={cScap:F4} R=[{cRcap.M11:F4},{cRcap.M12:F4},{cRcap.M13:F4}|{cRcap.M21:F4},{cRcap.M22:F4},{cRcap.M23:F4}|{cRcap.M31:F4},{cRcap.M32:F4},{cRcap.M33:F4}]")
                            If isRoot Then Exit While
                            curNode = TryCast(nif.GetParentNode(curNode), NiflySharp.Blocks.NiAVObject)
                            depth += 1
                            If depth > 20 Then Exit While
                        End While
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[CHUNK-PROP]     shape-chain EXCEPTION: {ex.Message}")
                    End Try

                    Dim boneNames As New List(Of String)
                    If sh.ShapeBones IsNot Nothing Then
                        For Each bn In sh.ShapeBones
                            Dim niN = TryCast(bn, NiflySharp.Blocks.NiNode)
                            boneNames.Add(If(niN?.Name?.String, "<null>"))
                        Next
                    End If
                    Dim boneNamesStr = String.Join(", ", boneNames)

                    Dim firstBindStr = "<no-bind>"
                    If sh.ShapeBoneTransforms IsNot Nothing AndAlso sh.ShapeBoneTransforms.Count > 0 Then
                        Dim firstBind = sh.ShapeBoneTransforms(0)
                        Dim fr = firstBind.Rotation
                        firstBindStr = $"T=({firstBind.Translation.X:F2},{firstBind.Translation.Y:F2},{firstBind.Translation.Z:F2}) S={firstBind.Scale:F3} R=[{fr.M11:F3},{fr.M12:F3},{fr.M13:F3} | {fr.M21:F3},{fr.M22:F3},{fr.M23:F3} | {fr.M31:F3},{fr.M32:F3},{fr.M33:F3}]"
                    End If

                    Logger.LogLazy(Function() $"[CHUNK-PROP]   shape='{shapeName}' niShape:{niShapeT}")
                    Logger.LogLazy(Function() $"[CHUNK-PROP]     ShapeBones({boneNames.Count})=[{boneNamesStr}]")
                    Logger.LogLazy(Function() $"[CHUNK-PROP]     firstBind={firstBindStr}")

                    ' All bind transforms — para multi-instance shape igual, podemos comparar
                    ' bind matrices a ver si difieren entre instancias (no deberían si vienen
                    ' del mismo NIF, pero confirmamos contra evidencia).
                    If sh.ShapeBoneTransforms IsNot Nothing Then
                        For i = 0 To sh.ShapeBoneTransforms.Count - 1
                            Dim bind = sh.ShapeBoneTransforms(i)
                            Dim boneNameLog = If(i < boneNames.Count, boneNames(i), $"<idx{i}>")
                            Dim br = bind.Rotation
                            Dim idxLog = i
                            Dim btDescLog = $"T=({bind.Translation.X:F2},{bind.Translation.Y:F2},{bind.Translation.Z:F2}) S={bind.Scale:F3} R=[{br.M11:F3},{br.M12:F3},{br.M13:F3}|{br.M21:F3},{br.M22:F3},{br.M23:F3}|{br.M31:F3},{br.M32:F3},{br.M33:F3}]"
                            Logger.LogLazy(Function() $"[CHUNK-PROP]     bind[{idxLog}] bone='{boneNameLog}' {btDescLog}")
                        Next
                    End If
                Next
            End If

            result.Shapes.AddRange(shapes)
            For Each sh In shapes
                If sh IsNot Nothing Then result.ShapeCandidate(sh) = candidate
            Next
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>Reescribe el sufijo |N de los bone names referenciados por los shapes,
    ''' redirigiendo del set |0 nativo al set |&lt;apIdx&gt; del skeleton. Aplicado per-instancia
    ''' antes del render para que el skinning resuelva contra los bones correctos del actor.
    '''
    ''' Reescritura quirúrgica: solo NiNode.Name.String de bones presentes en ShapeBones.
    ''' No toca el resto del NIF (extra data, anim controllers, etc.). El NIF está clonado
    ''' por candidate (LoadNifShapes parsea fresh), así que mutar nombres no afecta otras
    ''' instancias del mismo path.</summary>
    Private Sub RenameShapeBoneIndices(shapes As IEnumerable(Of IRenderableShape), apIdx As Byte)
        If shapes Is Nothing OrElse apIdx = 0 Then Return
        Dim newSuffix = "|" & apIdx.ToString()
        For Each shape In shapes
            If shape Is Nothing OrElse shape.ShapeBones Is Nothing Then Continue For
            For Each bn In shape.ShapeBones
                Dim niNode = TryCast(bn, NiflySharp.Blocks.NiNode)
                If niNode Is Nothing OrElse niNode.Name Is Nothing Then Continue For
                Dim s = niNode.Name.String
                If String.IsNullOrEmpty(s) Then Continue For
                If s.EndsWith("|0", StringComparison.Ordinal) Then
                    Dim renamed = String.Concat(s.AsSpan(0, s.Length - 2), newSuffix)
                    niNode.Name.String = renamed
                    Dim sLog = s
                    Dim renamedLog = renamed
                    Logger.LogLazy(Function() $"[BONE-RENAME] '{sLog}' → '{renamedLog}'")
                End If
            Next
        Next
    End Sub

    ''' <summary>Cuando un chunk multi-instance (MountApIdx > 0) tiene sus shape bones renombrados
    ''' de `Bone|0` a `Bone|N`, los sub-sockets BSConnectPoint::Parents que esa chunk NIF expone
    ''' siguen apuntando a `Bone|0` en su `ParentBoneName` literal — esto hace que sub-chunks que
    ''' se mounten sobre el chunk parent terminen anclados al bone |0 en vez del |N correcto.
    ''' Remap el ParentBoneName de cada sub-socket en la misma sufijo |N que los shape bones.
    ''' Fix de Bug HIGH #1 + #2 del análisis 2026-05-15.</summary>
    Private Sub RenameSubSocketParentBones(nif As Nifcontent_Class_Manolo, apIdx As Byte)
        If nif Is Nothing OrElse apIdx = 0 Then Return
        Dim root = nif.GetRootNode()
        If root Is Nothing OrElse root.ExtraDataList Is Nothing Then Return
        Dim newSuffix = "|" & apIdx.ToString()
        For Each ref In root.ExtraDataList.References
            Dim block = nif.Blocks(ref.Index)
            Dim parents = TryCast(block, NiflySharp.Blocks.BSConnectPoint_Parents)
            If parents Is Nothing OrElse parents.ConnectPoints Is Nothing Then Continue For
            For Each cp In parents.ConnectPoints
                If cp.Parent Is Nothing Then Continue For
                Dim s = cp.Parent.Content
                If String.IsNullOrEmpty(s) Then Continue For
                If s.EndsWith("|0", StringComparison.Ordinal) Then
                    Dim renamed = String.Concat(s.AsSpan(0, s.Length - 2), newSuffix)
                    cp.Parent.Content = renamed
                    Dim sLog = s, renamedLog = renamed
                    Dim socketLog = If(cp.Name?.Content, "<unnamed>")
                    Logger.LogLazy(Function() $"[SUBSOCKET-RENAME] socket='{socketLog}' ParentBone '{sLog}' → '{renamedLog}'")
                End If
            Next
        Next
    End Sub

    ''' <summary>Pre-compose socket transform into the bind matrices of every bone weighted by
    ''' the shapes. Math (row-vector convention used by the lib's Transform_Class):
    '''   render formula:        v_out = v · inverse(bind) · boneWorld
    '''   want with socket:      v_out = v · inverse(bind_new) · boneWorld
    '''                                = v · inverse(bind) · socket · boneWorld
    '''   solve:                 inverse(bind_new) = inverse(bind) · socket
    '''                          bind_new = inverse(socket) · bind
    '''                          (in lib API: bind.Compose(socket.Inverse()) — "apply socket.Inverse() first then bind")
    ''' Mutates the Transform_Class instances in place. Each candidate has a fresh NIF parsed,
    ''' so this mutation does not bleed into other instances of the same DictKey.</summary>
    Private Sub ApplySocketToBindTransforms(shapes As IEnumerable(Of IRenderableShape),
                                             socket As BSConnectPointReader.ConnectPointInfo)
        If shapes Is Nothing OrElse socket Is Nothing Then Return
        Dim socketT As New Transform_Class With {
            .Translation = socket.Translation,
            .Rotation = BSConnectPointReader.QuatToMatrix33(socket.Rotation),
            .Scale = If(socket.Scale > 0.0F, socket.Scale, 1.0F)
        }
        Dim socketInv = socketT.Inverse()
        For Each shape In shapes
            If shape Is Nothing OrElse shape.ShapeBoneTransforms Is Nothing Then Continue For
            For i = 0 To shape.ShapeBoneTransforms.Count - 1
                Dim bind = shape.ShapeBoneTransforms(i)
                If bind Is Nothing Then Continue For
                Dim composed = socketInv.ComposeTransforms(bind)
                bind.Translation = composed.Translation
                bind.Rotation = composed.Rotation
                bind.Scale = composed.Scale
                bind.ScaleVector = composed.ScaleVector
            Next
        Next
    End Sub

    ''' <summary>HeadRear-only: cuando el HDPT fue redirigido a su variant *_faceBones.nif (rigging
    ''' facial para FMRS), los shapes del _faceBones traen material genérico (basehumanfemaleskin)
    ''' en lugar del material part-específico del .nif base (basehumanfemalerear). Replicamos el
    ''' comportamiento del engine: rigging del _faceBones + material del base. Match per-shape por
    ''' nombre con sufijo "_faceBones" removido (case-insensitive). Sólo aplica si
    ''' candidate.BaseDictKeyForFaceBones está poblado (= HeadRear con redirect).</summary>
    Private Sub CopyBaseMaterialsToFaceBonesShapes(candidate As MeshCandidate, shapes As IEnumerable(Of IRenderableShape))
        If candidate Is Nothing OrElse shapes Is Nothing Then Return
        If String.IsNullOrEmpty(candidate.BaseDictKeyForFaceBones) Then Return

        Dim baseKey = candidate.BaseDictKeyForFaceBones
        Dim baseLoc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(baseKey, baseLoc) Then
            Return
        End If

        Dim baseBytes = baseLoc.GetBytes()
        If baseBytes Is Nothing OrElse baseBytes.Length = 0 Then
            Return
        End If

        Dim baseNif As Nifcontent_Class_Manolo
        Try
            baseNif = New Nifcontent_Class_Manolo()
            baseNif.Load_Manolo(baseBytes)
        Catch ex As Exception
            Return
        End Try

        ' Index base materials by stripped name (sin "_faceBones") para hacer match con los
        ' shapes del _faceBones que sí tienen el sufijo. Case-insensitive.
        Dim baseByStripped As New Dictionary(Of String, Nifcontent_Class_Manolo.RelatedMaterial_Class)(StringComparer.OrdinalIgnoreCase)
        For Each kv In baseNif.BaseMaterials
            baseByStripped(StripFaceBonesSuffix(kv.Key)) = kv.Value
        Next

        Dim copied As Integer = 0
        Dim missed As Integer = 0
        For Each shape In shapes
            Dim shapeName = shape.ShapeName
            If String.IsNullOrEmpty(shapeName) Then Continue For
            Dim stripped = StripFaceBonesSuffix(shapeName)
            Dim baseMat As Nifcontent_Class_Manolo.RelatedMaterial_Class = Nothing
            If baseByStripped.TryGetValue(stripped, baseMat) AndAlso baseMat IsNot Nothing Then
                Dim relMat = shape.ShapeMaterial
                If relMat IsNot Nothing Then
                    relMat.material = baseMat.material
                    relMat.path = baseMat.path
                    copied += 1
                End If
            Else
                missed += 1
            End If
        Next
    End Sub

    ''' <summary>Quita el sufijo "_faceBones" (case-insensitive) del nombre del shape para hacer
    ''' match con el shape correspondiente en el NIF base. Preserva ":N" (subindex de BSSubIndexTriShape).
    ''' Ej: "BaseFemaleHeadRear_faceBones:0" → "BaseFemaleHeadRear:0".</summary>
    Private Shared Function StripFaceBonesSuffix(name As String) As String
        If String.IsNullOrEmpty(name) Then Return name
        Const Suffix As String = "_faceBones"
        Dim idx = name.IndexOf(Suffix, StringComparison.OrdinalIgnoreCase)
        If idx < 0 Then Return name
        Return String.Concat(name.AsSpan(0, idx), name.AsSpan(idx + Suffix.Length))
    End Function

    Friend Sub ApplyShapeMaterialOverrides(candidate As MeshCandidate, state As NPCVisualState, shapes As IEnumerable(Of IRenderableShape))
        If shapes Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        DumpAllTxstFlagsOnce()  ' diagnóstico one-shot: todos los TXST + flag (gateado por Logger.Enabled)

        If logEnabled Then
            Dim candFidLog As UInteger = If(candidate IsNot Nothing, candidate.SourceFormID, 0UI)
            Dim chunkOmodLog As UInteger = If(candidate IsNot Nothing, candidate.ChunkOmodFormID, 0UI)
            Dim candKindLog As String = If(candidate IsNot Nothing, candidate.Kind.ToString(), "<no-cand>")
            Dim ctxLog As String = If(candidate IsNot Nothing AndAlso candidate.OmodResolutionFormType IsNot Nothing, candidate.OmodResolutionFormType, "")
            Dim mswpLog As UInteger = If(candidate IsNot Nothing, candidate.MaterialSwapFormID, 0UI)
            Dim cremapLog As String = If(candidate IsNot Nothing AndAlso candidate.ColorRemapIndex.HasValue, candidate.ColorRemapIndex.Value.ToString("F4"), "none")
            Dim hasOmodResLog As Boolean = candidate IsNot Nothing AndAlso candidate.OmodResolution IsNot Nothing
            Dim shapeCountLog As Integer = shapes.Count()
            Logger.LogLazy(Function() $"[SHAPEMAT-ENTRY] cand=0x{candFidLog:X8} kind={candKindLog} chunkOmod=0x{chunkOmodLog:X8} ctxFormType='{ctxLog}' shapes={shapeCountLog} armaMSWP=0x{mswpLog:X8} armaColorRemap={cremapLog} hasOmodResolution={hasOmodResLog}")
        End If

        ' Material override pipeline order (matches engine application order):
        '   1. ARMA-direct base swap (MaterialSwapFormID + ColorRemapIndex per gender on the ARMA
        '      record itself — semantically SET).
        '   2. OBTS/OMOD resolution from the parent ARMO — DirectProperties of applied
        '      combinations, then Properties of every IncludedOmod, in declaration order.
        '      SET overwrites the current material; ADD muta lo que dejó la pasada anterior.
        ' (3) Texture/Skin/Hair palette overrides happen later in this method and read whatever
        ' material this pipeline left in place.
        If candidate IsNot Nothing AndAlso candidate.MaterialSwapFormID <> 0UI Then
            ShapeMaterialOverrides.ApplyMaterialSwap(candidate.MaterialSwapFormID,
                                                    ShapeMaterialOverrides.MaterialSwapFunction.SET,
                                                    shapes, _pluginManager)
        End If
        If candidate IsNot Nothing AndAlso candidate.ColorRemapIndex.HasValue Then
            ShapeMaterialOverrides.ApplyColorRemap(candidate.ColorRemapIndex.Value, 0.0F,
                                                   ShapeMaterialOverrides.ColorRemapFunction.SET,
                                                   shapes)
        End If
        If candidate IsNot Nothing AndAlso candidate.OmodResolution IsNot Nothing Then
            ' FormType context comes from the candidate. Humanoid path (CollectArmoCandidates)
            ' sets "ARMO"; NPC robot path (CollectRobotChunkCandidates) sets "NPC_". Drives
            ' which PropertyIndex enum interprets each Property idx.
            OmodResolutionApplier.ApplyResolutionToShapes(candidate.OmodResolution, candidate.OmodResolutionFormType, shapes, _pluginManager)
        End If

        Dim solidTintColor = ResolveHeadPartSolidTintColor(candidate)
        Dim hairTintColor = ResolveHairTintColor(candidate, state, solidTintColor)
        Dim skinTintColor = ResolveSkinTintColor(candidate, state, solidTintColor)
        Dim textureSet = ResolveTextureSet(candidate, state)

        ' Skin substitution per-shape para Outfit: el engine vanilla sustituye la diffuse de shapes
        ' con shader SkinTint dentro de un outfit (escote, brazos expuestos) por la del actor's body
        ' skin (race-specific). Sólo aplica a Outfit. HeadParts usan TXST propio del HDPT (o FaceTint
        ' shader para Face). Skin candidates conservan TXST nativo via ARMA.
        Dim actorBodySkinTxst As TXST_Data = Nothing
        If candidate IsNot Nothing AndAlso candidate.Kind = MeshCandidateKind.Outfit Then
            Dim region = ResolveSkinRegionForOutfit(candidate)
            actorBodySkinTxst = ResolveActorSkinTextureSet(state, region)
        End If

        For Each shape In shapes
            MaterialResolver.EnsureShapeMaterialResolved(shape)

            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing Then Continue For

            Dim matPre = relatedMaterial.material
            If logEnabled AndAlso matPre IsNot Nothing Then
                Dim palOnPre = matPre.GrayscaleToPaletteColor
                Dim palScalePre = matPre.GrayscaleToPaletteScale
                Dim greyTexPre = If(matPre.GreyscaleTexture, "")
                Dim shapeNamePre = shape.ShapeName
                Logger.LogLazy(Function() $"[PALSCALE-PRE] shape='{shapeNamePre}' path='{relatedMaterial.path}' palColor={palOnPre} palScale={palScalePre:F4} greyTex='{greyTexPre}' (post-load, pre-overrides)")

                ' Snapshot del material INLINE del NIF/BGSM ANTES de cualquier override TXST/FTST.
                ' Para ojos esto muestra la FUENTE de EyeGloss_n / eyeenvironmentmask_m (lo que el
                ' shader de ojos trae) vs lo que después intenta pisar el TXST (EyeBrown_n / Eye_s).
                Dim shP = matPre.NifShaderType.ToString()
                Dim isBgsmP = matPre.IsBGSM()
                Dim dP = If(matPre.Diffuse_or_Base_Texture, "")
                Dim nP = If(matPre.NormalTexture, "")
                Dim sP = If(matPre.SmoothSpecTexture, "")
                Dim specP = If(matPre.SpecularTexture, "")
                Dim wP = If(matPre.WrinklesTexture, "")
                Dim envP = If(matPre.EnvmapTexture, "")
                Logger.LogLazy(Function() $"[SHAPEMAT-PRE-TEX] shape='{shapeNamePre}' shader={shP} isBGSM={isBgsmP} (inline NIF/BGSM source, pre-TXST) D='{dP}' N='{nP}' S='{sP}' spec='{specP}' W='{wP}' env='{envP}'")
            End If

            ApplyTextureSetOverrides(textureSet, relatedMaterial, candidate.UsesBodyTexture, shape.NifShape, shape.NifContent,
                                     isHeadPartTextureSet:=(candidate IsNot Nothing AndAlso candidate.Kind = MeshCandidateKind.HeadPart),
                                     isFaceHeadPart:=(candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace))

            Dim material = relatedMaterial.material
            If material Is Nothing Then Continue For

            If logEnabled Then
                Dim palOnPostTxst = material.GrayscaleToPaletteColor
                Dim palScalePostTxst = material.GrayscaleToPaletteScale
                Dim shapeNamePre = shape.ShapeName
                Logger.LogLazy(Function() $"[PALSCALE-POST-TXST] shape='{shapeNamePre}' palColor={palOnPostTxst} palScale={palScalePostTxst:F4} (post TXST/MNAM override)")
            End If

            ' Shape con piel expuesta (shader=SkinTint): sustituir SÓLO sus texturas (diffuse +
            ' normal + spec) por las del body skin del actor (race-specific). Material params
            ' (specular, smoothness, subsurface, etc.) NO se tocan — vienen del NIF original.
            ' Decisión per-shape via material.NifShaderType porque un mismo .nif suele tener shapes
            ' mixtos. El render lee el path desde relatedMaterial.material (Render.vb:1362).
            If actorBodySkinTxst IsNot Nothing AndAlso material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint Then
                Dim diffuseBefore = material.Diffuse_or_Base_Texture
                ' Si el TXST trae MaterialPath (MNAM .bgsm), las texturas viven dentro del BGSM —
                ' cargar el BGSM para extraer sus paths. NO copiamos otros params del BGSM (sólo
                ' las texturas), preservando los params del material original del shape.
                If actorBodySkinTxst.MaterialPath <> "" Then
                    Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(actorBodySkinTxst.MaterialPath, material, shape.NifShape, shape.NifContent)
                    If bgsmMaterial IsNot Nothing Then
                        If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then material.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                        If bgsmMaterial.NormalTexture <> "" Then material.NormalTexture = bgsmMaterial.NormalTexture
                        If bgsmMaterial.SmoothSpecTexture <> "" Then material.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                        If logEnabled Then
                            Dim mnamL = If(actorBodySkinTxst.MaterialPath, "")
                            Dim shapeL = shape.ShapeName
                            Logger.LogLazy(Function() $"[SKINSUB-MNAM] shape='{shapeL}' bodyBgsm='{mnamL}' → copia D/N/SmoothSpec del BGSM body (otros params del NIF; SKIP)")
                        End If
                    End If
                End If
                If logEnabled Then
                    Dim shapeSubL = shape.ShapeName
                    Logger.LogLazy(Function() $"[SKINSUB] shape='{shapeSubL}' SkinTint en Outfit → sustituye texturas por body skin del actor (luego TXST slots encima)")
                End If
                ApplyTextureSetToMaterial(material, actorBodySkinTxst)
            End If

            ' Hair/Palette + HairTintColor: shared with RefreshFaceTintLivePreview via helper.
            ' Pre-resolved hairTintColor (incl. solidTintColor head-part color) passed as override
            ' so the helper can short-circuit ResolveColorFormColor for hair HeadParts whose
            ' candidate carries a richer color choice. Helper is the single source of truth for
            ' the engine-faithful gate (Hair/FacialHair/Brow HDPTs only) — removes the prior
            ' If/ElseIf duplication and the looser parallel copy in RefreshFaceTintLivePreview.
            ApplyMaterialPaletteHairColor(material, candidate, state, hairTintColor)

            ' Skin-tint FIEL al material resuelto (SIN force). El render tinta los shapes cuyo material
            ' resolvió SkinTint=True — piel real (body/hands/rear-head) ya viene SkinTint de su fuente
            ' (verificado en log: preST=True en todos esos). Se ELIMINÓ ShouldForceSkinTint: era
            ' redundante para piel real y forzaba MAL a no-piel (PAFrame01/Stingwing/basesuit por el
            ' catch-all Kind=Skin). MouthShadow/bocas humanas/ojos/lashes nunca lo necesitaron (force=False
            ' en el log). Ahora el material resuelto manda y nada se muta para el render.
            If logEnabled Then
                Dim shapeNameST = shape.ShapeName
                Dim matShaderST = material.NifShaderType.ToString()
                Dim stVal = material.SkinTint.ToString()
                Logger.LogLazy(Function() $"[SKINTINT-RESOLVED] shape='{shapeNameST}' matShader={matShaderST} SkinTint={stVal} (faithful, no force)")
            End If

            If material.SkinTint AndAlso skinTintColor.HasValue Then
                material.SkinTintColor = skinTintColor.Value
            End If

            If solidTintColor.HasValue AndAlso Not material.Hair AndAlso Not material.SkinTint Then
                shape.TintColor = solidTintColor.Value
            End If

            If logEnabled Then
                Dim shapeNameFinal = shape.ShapeName
                Dim pathFinal = If(relatedMaterial.path, "")
                Dim rootFinal = If(material.RootMaterialPath, "")
                Dim shaderFinal = material.NifShaderType.ToString()
                Dim isBgsmFinal = material.IsBGSM()
                Dim palOnFinal = material.GrayscaleToPaletteColor
                Dim palScaleFinal = material.GrayscaleToPaletteScale
                Dim texDiff = If(material.Diffuse_or_Base_Texture, "")
                Dim texNorm = If(material.NormalTexture, "")
                Dim texGlow = If(material.GlowTexture, "")
                Dim texGrey = If(material.GreyscaleTexture, "")
                Dim texSpec = If(material.SpecularTexture, "")
                Dim texSmSpec = If(material.SmoothSpecTexture, "")
                Dim texEnv = If(material.EnvmapTexture, "")
                Dim texEnvMask = If(material.EnvmapMaskTexture, "")
                Dim texLight = If(material.LightingTexture, "")
                Dim texWrink = If(material.WrinklesTexture, "")
                Dim texInner = If(material.InnerLayerTexture, "")
                Dim texTintMask = If(material.TintMaskTexture, "")
                Logger.LogLazy(Function() $"[SHAPEMAT-FINAL] shape='{shapeNameFinal}' path='{pathFinal}' root='{rootFinal}' shader={shaderFinal} isBGSM={isBgsmFinal} palette={palOnFinal} palScale={palScaleFinal:F4}")
                Logger.LogLazy(Function() $"[SHAPEMAT-FINAL-TEX] shape='{shapeNameFinal}' diff='{texDiff}' norm='{texNorm}' glow='{texGlow}' grey='{texGrey}' spec='{texSpec}' smSpec='{texSmSpec}' env='{texEnv}' envMask='{texEnvMask}' light='{texLight}' wrink='{texWrink}' inner='{texInner}' tintMask='{texTintMask}'")
            End If
        Next
    End Sub

    ''' <summary>Engine-faithful palette/HairTintColor resolution for hair HeadParts. Single source
    ''' of truth — used by BOTH the NIF-load pass (<see cref="ApplyShapeMaterialOverrides"/>) and
    ''' the live face-tint preset refresh (<see cref="RefreshFaceTintLivePreview"/>). Previously
    ''' duplicated in those two sites with subtly different guards; the looser guard at the live
    ''' path leaked hair color into any palette-enabled material (robot armor, face shapes with
    ''' palette opt-in, etc.). This helper enforces the engine rule once.
    '''
    ''' Engine rule: <c>CLFM.RemappingIndex</c> is consumed only by HDPTs that the engine equips
    ''' with a NPC color form. That's Hair (3) / FacialHair (4) / Brow (6) via NPC.HNAM / NPC.QNAM.
    ''' Other HeadParts (Face / Eyes / HeadRear / Meatcaps) carry palette in their BGSM but their
    ''' engine-correct paint comes from TETI SkinTone or the FaceTintCompositor, not from this path.
    ''' Misc (0) deferred — open question whether some Misc parts legitimately need hair color.
    '''
    ''' <para>Behavior per resolved <c>hairColorFormID</c>:
    ''' <list type="number">
    ''' <item>If CLFM has RemappingIndex AND a palette LUT path is resolvable (BGSM-first, RACE.HNAM
    '''   fallback): set <c>GrayscaleToPaletteColor=True</c>, <c>GrayscaleToPaletteScale=clfm.RemappingIndex</c>,
    '''   <c>GreyscaleTexture=palTex</c>.</item>
    ''' <item>Else: fall back to <c>HairTintColor</c>. Caller can pre-resolve a richer tint (NIF-load
    '''   passes ResolveHairTintColor with solidTintColor consideration) via
    '''   <paramref name="hairTintColorOverride"/>; if Nothing the helper resolves via
    '''   ResolveColorFormColor on the hair color form.</item>
    ''' </list></para>
    '''
    ''' No-op for: material=Nothing, candidate not IsHairHeadPart, or material that's neither Hair
    ''' shader nor palette opt-in. Silent (no warning logs) — those are expected for the vast
    ''' majority of shapes; the diagnostic only fires when the helper actually mutates state.
    ''' </summary>
    Friend Sub ApplyMaterialPaletteHairColor(material As FO4UnifiedMaterial_Class,
                                             candidate As MeshCandidate,
                                             state As NPCVisualState,
                                             hairTintColorOverride As Nullable(Of Color))
        If material Is Nothing Then Return
        If Not IsHairHeadPart(candidate) Then Return
        If Not (material.Hair OrElse material.GrayscaleToPaletteColor) Then Return

        Dim logEnabled = Logger.Enabled
        ' Hair/FacialHair/Brow all read NPC.HCLF. NPC.BCLF is preserved in the ESP for
        ' round-trip (Save ESP writes raw BCLF untouched) but ignored at render/bake time:
        ' F4SE/LooksMenu in-game also only reads headData->hairColor (CharGenInterface.cpp
        ' ProcessHairColor), and a workspace audit found BCLF used by 5/4473 NPCs total
        ' (all from one CC pack, 4 redundant with HCLF). Unifying on HCLF aligns with the
        ' in-game runtime the user actually sees.
        Dim hairColorFormID As UInteger = If(state IsNot Nothing, state.HairColorFormID, 0UI)

        Dim didPalette As Boolean = False
        If hairColorFormID <> 0UI Then
            Dim clfm = ResolveColorFormData(hairColorFormID)
            If clfm IsNot Nothing AndAlso clfm.HasRemappingIndex Then
                ' PRESERVAR el opt-in de palette de la FUENTE (no forzarlo). Probado sobre el corpus
                ' FaceGen vanilla (BeardRuleProbe 2026-06-13, 1100 shapes de barba, 7 diffuse): el flag
                ' GreyscaleToPalette_Color es UNIFORME por barba (función de la barba fuente, NO del NPC,
                ' 0 casos mix). CK lo deja como vino la fuente: barbas tintables (facialhair01/02, haircurly*)
                ' con flag ON; stubble (hairshaved04) con flag OFF. Nuestro código forzaba ON para toda
                ' shape Hair → rompía las OFF (88/1100). Fix: solo encender el flag + inyectar la textura
                ' del LUT si la FUENTE ya optó por palette (flag propio o textura greyscale propia).
                ' El SCALE (RemappingIndex) se escribe SIEMPRE — CK lo propaga uniforme por NPC, inerte
                ' en las shapes sin flag/textura (memoria grayscale 2026-05-25).
                Dim sourceHadPalette As Boolean = material.GrayscaleToPaletteColor OrElse Not String.IsNullOrEmpty(material.GreyscaleTexture)
                Dim oldPalColor = material.GrayscaleToPaletteColor
                Dim oldScale = material.GrayscaleToPaletteScale
                Dim oldGreyTex = If(logEnabled, If(material.GreyscaleTexture, ""), Nothing)
                material.GrayscaleToPaletteScale = clfm.RemappingIndex
                Dim palTex As String = ""
                If sourceHadPalette Then
                    ' Priority: BGSM's own GreyscaleTexture first (per-shape, picked by the stylist
                    ' for THIS mesh), RACE.HNAM/HLTX as fallback. The engine in-game binds the LUT
                    ' from the material's TXST slot 3 at render time (F4SE CharGenInterface.cpp:
                    ' 1106-1179, ProcessHairColor → SetTextureFilename(3, ...)). Vanilla
                    ' HumanChildRace ships without HNAM/HLTX precisely because the BGSM carries it.
                    palTex = If(material.GreyscaleTexture, "")
                    If palTex = "" Then palTex = ResolveRaceHairLookupTexture(state, _pluginManager)
                    If palTex <> "" Then
                        material.GrayscaleToPaletteColor = True
                        material.GreyscaleTexture = palTex
                    End If
                End If
                ' La rama palette manejó el material (escribió el scale) → no caer al HairTintColor
                ' fallback, que pisaría el HairTintColor de la fuente (CK no lo cambia en barbas OFF).
                didPalette = True
                If logEnabled Then
                    Dim newScale = clfm.RemappingIndex
                    Dim hairFidL = hairColorFormID
                    Dim palTexL = palTex
                    Dim srcHad = sourceHadPalette
                    Dim newPal = material.GrayscaleToPaletteColor
                    Logger.LogLazy(Function() $"[PALSCALE-WRITE] branch=Hair-CLFM hdptType={candidate.HeadPartType} hairColorFid=0x{hairFidL:X8} sourceHadPalette={srcHad} oldPalColor={oldPalColor} oldScale={oldScale:F4} oldGreyTex='{oldGreyTex}' → newPalColor={newPal} newScale={newScale:F4} newGreyTex='{palTexL}'")
                End If
            End If
        End If

        If Not didPalette Then
            Dim effectiveHairColor = hairTintColorOverride
            If Not effectiveHairColor.HasValue AndAlso hairColorFormID <> 0UI Then
                effectiveHairColor = ResolveColorFormColor(hairColorFormID)
            End If
            If effectiveHairColor.HasValue Then
                Dim oldHairCol = material.HairTintColor
                material.HairTintColor = effectiveHairColor.Value
                If logEnabled Then
                    Dim newColLog = effectiveHairColor.Value
                    Logger.LogLazy(Function() $"[HAIRTINT-WRITE] hdptType={candidate.HeadPartType} oldRGB=({oldHairCol.R},{oldHairCol.G},{oldHairCol.B}) → newRGB=({newColLog.R},{newColLog.G},{newColLog.B})")
                End If
            End If
        End If
    End Sub

    ''' <summary>Resuelve el TXST del body skin del actor (NPC.WNAM o RACE.WNAM via state.SkinFormID),
    ''' diferenciando por región: BODY (torso/legs) o HAND. El engine in-game sustituye la diffuse
    ''' texture de los shapes con BSLightingShaderType.SkinTint por la del actor — esto permite a
    ''' un mismo .nif outfit (autoreado con texturas embebidas humanas) verse correcto sobre ghoul,
    ''' synth, super mutant, etc. La sustitución debe usar la textura body (NakedTorso ARMA) para
    ''' shapes con piel del torso/brazos/legs y la hand (NakedHands ARMA) para shapes en gloves
    ''' con piel expuesta de manos.
    ''' Retorna Nothing si state.SkinFormID no resuelve a un ARMO con ARMA gender-correct válida.</summary>
    Private Function ResolveActorSkinTextureSet(state As NPCVisualState, region As SkinRegion) As TXST_Data
        If state Is Nothing OrElse state.SkinFormID = 0UI Then Return Nothing

        Dim armo = GetParsedArmo(state.SkinFormID)
        If armo Is Nothing Then Return Nothing

        Const BODY_BIT As UInteger = 1UI << 3
        Const HAND_MASK As UInteger = (1UI << 4) Or (1UI << 5)

        ' Iterar las ARMAs del Skin ARMO; elegir la que cubra la región pedida.
        For Each entry In armo.ArmorAddons
            Dim arma = GetParsedArma(entry.ArmaFormID)
            If arma Is Nothing Then Continue For
            Dim armaSlot = arma.SlotMask

            Dim matches As Boolean = False
            Select Case region
                Case SkinRegion.Body
                    matches = (armaSlot And BODY_BIT) <> 0UI
                Case SkinRegion.Hand
                    matches = (armaSlot And HAND_MASK) <> 0UI AndAlso (armaSlot And BODY_BIT) = 0UI
            End Select
            If Not matches Then Continue For

            Dim txstFID = If(state.IsFemale,
                             If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                             If(arma.MaleSkinTextureFormID <> 0UI, arma.MaleSkinTextureFormID, arma.FemaleSkinTextureFormID))
            If txstFID = 0UI Then Continue For

            Dim txstRec = _pluginManager.GetRecord(txstFID)
            If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then Continue For

            Return RecordParsers.ParseTXST(txstRec, _pluginManager)
        Next

        Return Nothing
    End Function

    Private Enum SkinRegion
        Body = 0
        Hand = 1
    End Enum

    ''' <summary>Decide qué región de skin (Body vs Hand) corresponde a un Outfit candidate según
    ''' su SlotMask. Outfits tipo "MOutfit/FOutfit" (cubren BODY+[U]) → Body; gloves outfits (sólo
    ''' bits hand sin BODY/[U]) → Hand. Para [A] over-armor con piel expuesta (raro), el slot
    ''' indica qué cubre — si toca BODY/[U] usar Body; si sólo [A]/hand → Hand.</summary>
    Private Shared Function ResolveSkinRegionForOutfit(candidate As MeshCandidate) As SkinRegion
        If candidate Is Nothing Then Return SkinRegion.Body
        Const BODY_BIT As UInteger = 1UI << 3
        Const HAND_MASK As UInteger = (1UI << 4) Or (1UI << 5)
        Dim U_MASK As UInteger = 0UI
        For b = 6 To 10 : U_MASK = U_MASK Or (1UI << b) : Next

        Dim slot = candidate.SlotMask
        Dim touchesBodyOrU = (slot And BODY_BIT) <> 0UI OrElse (slot And U_MASK) <> 0UI
        Dim touchesHand = (slot And HAND_MASK) <> 0UI

        ' Body/[U] tiene precedencia sobre hand: outfits tipo "all-in-one" con BODY+hands
        ' (ej. AAClothesCait slot 33+34+35) usan body skin para la zona de torso/brazos.
        If touchesBodyOrU Then Return SkinRegion.Body
        If touchesHand Then Return SkinRegion.Hand
        Return SkinRegion.Body  ' default seguro: si no toca nada conocido (raro), body.
    End Function

    Private Function ResolveHeadPartSolidTintColor(candidate As MeshCandidate) As Nullable(Of Color)
        If candidate Is Nothing OrElse Not candidate.UseSolidTint Then Return Nothing
        Return ResolveColorFormColor(candidate.HeadPartColorFormID)
    End Function

    Private Function ResolveTextureSet(candidate As MeshCandidate, state As NPCVisualState) As TXST_Data
        Dim logEnabled = Logger.Enabled
        ' Regla canónica HeadPart TXST resolution (per HDPT.DATA flags spec
        ' wbDefinitionsFO4.pas:7365-7372):
        '   A) sin TNAM, sin UsesBodyTexture → Nothing (deja lo embebido del NIF).
        '   B) con TNAM, sin UsesBodyTexture → usa TNAM (lo que el HDPT trae).
        '   C) UsesBodyTexture=True → body TXST del actor (state.SkinFormID → NakedTorso ARMA →
        '      Male/FemaleTxst gender-correct). La cadena SkinFormID es race-specific, así un mismo
        '      HDPT compartido entre razas (RNAM=FLST con Human+Ghoul, ej. FemaleHeadHumanRearTEMP)
        '      renderiza con texturas distintas según la raza del NPC.
        ' Caso particular Face: si un HDPT cuyo *raw* PartType=Face no tiene TNAM, fallback a
        ' state.HeadTextureFormID (NPC.FTST). Esto cubre HDPTs Face vanilla que dependen del
        ' FTST per-NPC (ej. NPCs con makeup pre-bakeado en el FTST). IMPORTANTE: usa
        ' HeadPartTypeRaw (no HeadPartType=effective) — sub-parts Misc cuyo effective se
        ' hereda como Face vía HNAM-parent (MouthShadowFemale, eye lashes/AO/wet) NO deben
        ' tomar el FTST del head, lo que les pisaba el Diffuse del shader source con
        ' basefemalehead_d.dds en vez de su propio path autoreado. CK al bakear respeta el
        ' material original de esos sub-parts; verificado contra Alijo vanilla.
        ' Esta regla aplica SÓLO a HeadPart. Skin/Outfit candidates conservan su propio flujo.
        If candidate IsNot Nothing AndAlso candidate.Kind = MeshCandidateKind.HeadPart Then
            ' Caso C: UsesBodyTexture=True gana sobre TNAM.
            If candidate.UsesBodyTexture AndAlso state IsNot Nothing Then
                Dim bodyTxst = ResolveActorSkinTextureSet(state, SkinRegion.Body)
                If bodyTxst IsNot Nothing Then
                    If logEnabled Then
                        Dim bFid = bodyTxst.FormID, bMnam = If(bodyTxst.MaterialPath, "")
                        Dim bD = If(bodyTxst.DiffuseTexture, ""), bN = If(bodyTxst.NormalTexture, ""), bS = If(bodyTxst.SmoothSpecTexture, "")
                        Logger.LogLazy(Function() $"[TXST-RESOLVE] source=BodySkin(UsesBodyTexture) txst=0x{bFid:X8} mnam='{bMnam}' D='{bD}' N='{bN}' S='{bS}'")
                    End If
                    Return bodyTxst
                End If
                ' Fallthrough si el actor no tiene body skin resuelto (raro): seguir con TNAM/Face.
            End If
        End If

        Dim textureSetFormID As UInteger = 0UI
        Dim txstSource As String = "none"

        If candidate IsNot Nothing Then
            textureSetFormID = candidate.TextureSetFormID
            If textureSetFormID <> 0UI Then txstSource = "HDPT.TNAM"
            ' Precedencia de la textura base para Face head parts: FTST (propio del NPC) > HDPT.TNAM > DFTM (default
            ' de la raza). El FTST PROPIO (state.ExplicitHeadTextureFormID, capturado ANTES del fallback DFTM en
            ' BuildNPCVisualState) REEMPLAZA el TNAM — la cara declarada del NPC gana sobre el skin default del HDPT
            ' (ej. Mitch FTST=SkinHeadMayor pisa MaleHeadHuman.TNAM=SkinHeadHeroMale). Si no hay FTST propio, queda el
            ' TNAM del head part. Sólo si tampoco hay TNAM se cae a DFTM (state.HeadTextureFormID = DFTM cuando no hay
            ' FTST propio, llenado en :7584). Guard raw=Face (HeadPartTypeRaw, NO effective) protege sub-parts Misc
            ' heredados como Face (MouthShadow/AO/lashes/wet) que conservan su propio material. (Antes:
            ' state.HeadTextureFormID=FTST-o-DFTM pisaba el TNAM -> DFTM le ganaba a TNAM en razas con DFTM<>TNAM; mal.)
            If candidate.Kind = MeshCandidateKind.HeadPart AndAlso candidate.HeadPartTypeRaw = HeadPartTypeFace AndAlso state IsNot Nothing Then
                If state.ExplicitHeadTextureFormID <> 0UI Then
                    textureSetFormID = state.ExplicitHeadTextureFormID
                    txstSource = "NPC.FTST(Face-override)"
                ElseIf textureSetFormID = 0UI AndAlso state.HeadTextureFormID <> 0UI Then
                    textureSetFormID = state.HeadTextureFormID
                    txstSource = "RACE.DFTM(Face-fallback)"
                End If
            End If
        End If

        If textureSetFormID = 0UI Then Return Nothing

        Dim rec = _pluginManager.GetRecord(textureSetFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then
            If logEnabled Then
                Dim fidL = textureSetFormID, srcL = txstSource
                Logger.LogLazy(Function() $"[TXST-RESOLVE] source={srcL} formID=0x{fidL:X8} → NOT-FOUND-or-not-TXST")
            End If
            Return Nothing
        End If

        Dim parsed = RecordParsers.ParseTXST(rec, _pluginManager)
        If logEnabled Then
            Dim srcL2 = txstSource, pEid = If(parsed.EditorID, ""), pMnam = If(parsed.MaterialPath, "")
            Dim pD = If(parsed.DiffuseTexture, ""), pN = If(parsed.NormalTexture, ""), pS = If(parsed.SmoothSpecTexture, ""), pW = If(parsed.WrinklesTexture, "")
            ' DNAM flags (wbDefinitionsFO4.pas:7350): 0x0001 NoSpecularMap, 0x0002 FacegenTextures, 0x0004 HasModelSpaceNormal.
            ' Hipótesis: 'FacegenTextures' (0x0002) marca el set de complexión (full D/N/S en el bake) vs TXST normal.
            Dim pFlags = parsed.Flags
            Dim pFacegen = (pFlags And &H2US) <> 0US, pNoSpec = (pFlags And &H1US) <> 0US, pMsn = (pFlags And &H4US) <> 0US
            Logger.LogLazy(Function() $"[TXST-RESOLVE] source={srcL2} txst=0x{parsed.FormID:X8} eid='{pEid}' flags=0x{pFlags:X4}(facegen={pFacegen},noSpec={pNoSpec},msn={pMsn}) mnam='{pMnam}' D='{pD}' N='{pN}' S='{pS}' W='{pW}'")
        End If
        Return parsed
    End Function

    ''' <summary>Pisa los paths de texturas del material con los del TXST (D / N / W / Glow /
    ''' Height / Env / Multilayer / Spec). Si el TXST trae un .bgsm/.bgem en MaterialPath,
    ''' carga ese material y reemplaza el del shape. <c>Friend Shared</c> para que
    ''' HeadPartPicker_Form pueda reutilizarlo en su preview de HDPT.</summary>
    Friend Shared Sub ApplyTextureSetOverrides(textureSet As TXST_Data, relatedMaterial As Nifcontent_Class_Manolo.RelatedMaterial_Class, usesBodyTexture As Boolean, shap As NiflySharp.INiShape, nif As Nifcontent_Class_Manolo, Optional isHeadPartTextureSet As Boolean = False, Optional isFaceHeadPart As Boolean = False)
        If textureSet Is Nothing OrElse relatedMaterial Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        Dim material = relatedMaterial.material
        If material Is Nothing Then Return

        ' MNAM-loaded rule (split by HDPT.UsesBodyTexture, verified empirically vs CK bake):
        '   - UsesBodyTexture=True : full-replace. The HDPT declares "this part wears the
        '     body skin" so the MNAM-pointed BGSM is the body-skin material in its entirety;
        '     D + N + S + everything else come from the override. Verified vs Alice
        '     ChildHeadRear (vanilla female child, MNAM=childfemalebody.bgsm) and the
        '     Carol-style ghoul HeadRear with CBBE override.
        '   - UsesBodyTexture=False: diffuse-only. The MNAM just supplies the surface tint
        '     for this specific shape; Normal/SmoothSpec/Envmap/shaderType/EnvironmentMapping/
        '     TwoSided all stay from the inline NIF shader. Verified vs Valentine
        '     SynthGen2HeadRearValentine (TXST.MNAM=gen2skindirty.bgsm has type=Default
        '     no-Envmap, but CK bake kept inline type=EnvironmentMap with the Envmap path
        '     and the non-dirty SmoothSpec).
        ' The TXST's TX## slots are layered on top by ApplyTextureSetToMaterial below, so any
        ' slot the TXST explicitly sets still wins regardless of the branch above.
        If textureSet.MaterialPath <> "" Then
            Dim overrideMaterial = MaterialResolver.TryLoadMaterialFromDictionary(textureSet.MaterialPath, material, shap, nif)
            If overrideMaterial IsNot Nothing Then
                ' TEXTURES-ONLY + ALPHA (2026-06-15): el MNAM del TXST aporta SOLO sus paths de textura
                ' MÁS el alpha (AlphaTest/AlphaBlend) verbatim. El resto del shader (ShaderType/
                ' SubsurfaceRolloff/BackLight/Smoothness/Specular/flags) queda del clon del mesh FUENTE —
                ' el .bgsm es el material runtime del engine, CK nunca lo hornea en el shader del FaceGen
                ' NIF. Verificado por identidad contra los 10.197 shapes de CK: donde hay MNAM, o es
                ' FaceGen=True (CK=shader del fuente) o FaceGen=False con source==material; ninguna shape
                ' bakeada necesita el shader del material. Reemplaza el experimento full-replace y el
                ' viejo gate usesBodyTexture.
                ' El alpha SÍ se toma del material override (no era así en la versión 06-14 textures-only):
                ' CK emite un NiAlphaProperty gobernado por el alpha del material de cabeza (p.ej.
                ' Gen2SkinHeadValentine.BGSM AlphaTest=True/Ref=128/Blend=Standard) y sin esto el NIF
                ' bakeado perdía el NiAlphaProperty y el flag SF2 Alpha_Test. Decisión de auditoría.
                ' Ver reference_facegen_ck_must_come_from_ba2.
                material.Diffuse_or_Base_Texture = overrideMaterial.Diffuse_or_Base_Texture
                material.NormalTexture = overrideMaterial.NormalTexture
                material.SmoothSpecTexture = overrideMaterial.SmoothSpecTexture
                material.GreyscaleTexture = overrideMaterial.GreyscaleTexture
                material.GlowTexture = overrideMaterial.GlowTexture
                material.WrinklesTexture = overrideMaterial.WrinklesTexture
                material.EnvmapTexture = overrideMaterial.EnvmapTexture
                material.SpecularTexture = overrideMaterial.SpecularTexture
                material.LightingTexture = overrideMaterial.LightingTexture
                material.FlowTexture = overrideMaterial.FlowTexture
                material.InnerLayerTexture = overrideMaterial.InnerLayerTexture
                material.DisplacementTexture = overrideMaterial.DisplacementTexture
                ' Alpha (AlphaTest/AlphaBlend) del material override SÓLO para el head part de cara
                ' (PartType=Face). CK emite el NiAlphaProperty gobernado por el alpha del material de
                ' cabeza sólo en synth con reemplazo (Valentine/DiMa). Pelo/barba/neckgore/ojos/mouth
                ' conservan el alpha de su material fuente (= CK) y NO se tocan acá.
                If isFaceHeadPart Then
                    material.AlphaTest = overrideMaterial.AlphaTest
                    material.AlphaTestRef = overrideMaterial.AlphaTestRef
                    material.AlphaBlendMode = overrideMaterial.AlphaBlendMode
                End If
                relatedMaterial.path = FO4UnifiedMaterial_Class.CorrectMaterialPath(textureSet.MaterialPath)
                If logEnabled Then
                    Dim mnamL = If(textureSet.MaterialPath, ""), ubt = usesBodyTexture
                    Logger.LogLazy(Function() $"[TXST-MNAM] mnam='{mnamL}' usesBodyTexture={ubt} → TEXTURES-ONLY (shader del fuente)")
                End If
            End If
        End If

        ApplyTextureSetToMaterial(material, textureSet, isHeadPartTextureSet)
    End Sub

    Friend Shared Sub ApplyTextureSetToMaterial(material As FO4UnifiedMaterial_Class, textureSet As TXST_Data, Optional isHeadPartTextureSet As Boolean = False)
        If material Is Nothing OrElse textureSet Is Nothing Then Return

        Dim logEnabled = Logger.Enabled
        ' Slot override gate — regla confirmada por dump de 997 TXST (2026-05-31, bug Alana/OldHumanFemale).
        ' Por defecto el TXST pisa TODOS los slots que resuelven (D+N+W+Glow+Height+Env+Inner+SmoothSpec).
        ' ÚNICA excepción (diffuse-only): el TextureSet de un HEAD PART SIN el flag DNAM 'Facegen Textures'
        ' (0x0002, xEdit wbDefinitionsFO4.pas:7350). Ese es un swatch per-part (color de ojo/boca): el BGSM
        ' del shape posee N/S/env (ej. ojo vanilla: EyeGloss_n + eyeenvironmentmask_m, que CK conserva).
        '   - Con el flag (complexión/piel SkinHead*/SkinBody*, y mods que lo setean p.ej. TEOB eyes) → full D/N/S.
        '   - Fuera de head-part (body/outfit/armadura) → full (no se aplica la excepción).
        ' Confirmado en dump: TODOS los EyesMaleHuman* vanilla = facegen=False (diffuse-only); todas las
        ' complexiones = facegen=True (full). Reemplaza el viejo gate por mnamEmpty (que descartaba el
        ' Old_n/_s del Face = el bug) y el parche transitorio por match "Eyes".
        Dim isFacegen = (textureSet.Flags And &H2US) <> 0US
        Dim diffuseOnly = isHeadPartTextureSet AndAlso Not isFacegen
        Dim txstFid = textureSet.FormID

        If logEnabled Then
            Dim txstEid = If(textureSet.EditorID, "")
            Dim mnamLog = If(textureSet.MaterialPath, "")
            Dim noSpecL = (textureSet.Flags And &H1US) <> 0US, msnL = (textureSet.Flags And &H4US) <> 0US
            Dim flagsL = textureSet.Flags, hpL = isHeadPartTextureSet, fgL = isFacegen, doL = diffuseOnly
            Logger.LogLazy(Function() $"[TXST-APPLY] txst=0x{txstFid:X8} eid='{txstEid}' flags=0x{flagsL:X4}(facegen={fgL},noSpec={noSpecL},msn={msnL}) headPart={hpL} → diffuseOnly={doL} mnam='{mnamLog}'")
        End If

        ' Diffuse (TX00): nunca se gatea. Resto: se salta solo si diffuseOnly (head-part sin flag Facegen).
        If TxstSlotDecision(txstFid, "Diffuse", textureSet.DiffuseTexture, material.Diffuse_or_Base_Texture, gatedSlot:=False, diffuseOnly:=diffuseOnly) Then material.Diffuse_or_Base_Texture = textureSet.DiffuseTexture
        If TxstSlotDecision(txstFid, "Normal", textureSet.NormalTexture, material.NormalTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.NormalTexture = textureSet.NormalTexture
        If TxstSlotDecision(txstFid, "Wrinkles", textureSet.WrinklesTexture, material.WrinklesTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.WrinklesTexture = textureSet.WrinklesTexture
        If TxstSlotDecision(txstFid, "Glow", textureSet.GlowTexture, material.GlowTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.GlowTexture = textureSet.GlowTexture
        If TxstSlotDecision(txstFid, "Height", textureSet.HeightTexture, material.DisplacementTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.DisplacementTexture = textureSet.HeightTexture
        If TxstSlotDecision(txstFid, "Envmap", textureSet.EnvironmentTexture, material.EnvmapTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.EnvmapTexture = textureSet.EnvironmentTexture
        If TxstSlotDecision(txstFid, "InnerLayer", textureSet.MultilayerTexture, material.InnerLayerTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.InnerLayerTexture = textureSet.MultilayerTexture
        If TxstSlotDecision(txstFid, "SmoothSpec", textureSet.SmoothSpecTexture, material.SmoothSpecTexture, gatedSlot:=True, diffuseOnly:=diffuseOnly) Then material.SmoothSpecTexture = textureSet.SmoothSpecTexture
    End Sub

    ''' <summary>DIAGNÓSTICO (2026-05-31): carga un NIF desde el FilesDictionary y loguea el material
    ''' INLINE (shader + texturas, sin overrides TXST/FTST) de cada shape, con el tag
    ''' <c>[NIF-INLINE-MAT]</c>. Sirve para comparar lo que trae el NIF ORIGINAL vs el que NOSOTROS
    ''' redirigimos a <c>_faceBones</c> (CollectHeadPartCandidate), porque el _faceBones puede traer
    ''' un shader/textura distintos al original (ej. HeadRear trae basehumanfemaleskin genérico).
    ''' Todo gateado por <c>Logger.Enabled</c> — carga un NIF de más, solo con logging activo.</summary>
    Private Sub LogNifInlineMaterials(rawDictKey As String, label As String)
        If Not Logger.Enabled Then Return
        Dim key = NormalizeDictionaryKeyWithMeshesPrefix(rawDictKey)
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If String.IsNullOrEmpty(key) OrElse Not FilesDictionary_class.Dictionary.TryGetValue(key, loc) Then
            Dim kL = key, lblL = label
            Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{kL}' → NOT-IN-DICT")
            Return
        End If
        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then Return
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim shapes = NifRenderableShape.FromNif(nif)
            If shapes Is Nothing Then Return
            For Each shape In shapes
                MaterialResolver.EnsureShapeMaterialResolved(shape)
                Dim rm = shape.ShapeMaterial
                Dim snL = shape.ShapeName, keyL = key, lblL = label
                If rm Is Nothing OrElse rm.material Is Nothing Then
                    Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' shape='{snL}' → no-material")
                    Continue For
                End If
                Dim m = rm.material
                Dim shdr = m.NifShaderType.ToString(), isBgsm = m.IsBGSM(), pathL = If(rm.path, "")
                Dim d = If(m.Diffuse_or_Base_Texture, ""), n = If(m.NormalTexture, ""), s = If(m.SmoothSpecTexture, "")
                Dim sp = If(m.SpecularTexture, ""), w = If(m.WrinklesTexture, ""), env = If(m.EnvmapTexture, "")
                Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' shape='{snL}' shader={shdr} isBGSM={isBgsm} matPath='{pathL}' D='{d}' N='{n}' S='{s}' spec='{sp}' W='{w}' env='{env}'")
            Next
        Catch ex As Exception
            Dim msgL = ex.Message, lblL = label, keyL = key
            Logger.LogLazy(Function() $"[NIF-INLINE-MAT] {lblL} dictKey='{keyL}' → EX: {msgL}")
        End Try
    End Sub

    Private Shared _txstFlagDumpDone As Boolean = False

    ''' <summary>DIAGNÓSTICO one-shot (2026-05-31): dumpea TODOS los TXST cargados (vanilla + mods) con
    ''' su flag DNAM (0x0001 NoSpecularMap, 0x0002 FacegenTextures, 0x0004 ModelSpaceNormal) y qué
    ''' slots traen (D/N/S). Sirve para auditar el universo del gate: facegen=True → full D/N/S;
    ''' facegen=False → diffuse-only (skip N/S). Gateado por Logger.Enabled, corre UNA vez por sesión.
    ''' Tag [TXST-DUMP]. Puede ser ruidoso (miles de TXST en el load order) — filtrar por 'facegen='.</summary>
    Private Sub DumpAllTxstFlagsOnce()
        If Not Logger.Enabled OrElse _txstFlagDumpDone Then Return
        _txstFlagDumpDone = True
        If _pluginManager Is Nothing Then Return
        Dim list As List(Of PluginRecord) = Nothing
        If Not _pluginManager.RecordsByType.TryGetValue("TXST", list) OrElse list Is Nothing Then
            Logger.LogLazy(Function() "[TXST-DUMP] no hay TXST en RecordsByType")
            Return
        End If
        Dim total = list.Count, facegenCount = 0
        For Each rec In list
            Dim t = RecordParsers.ParseTXST(rec, _pluginManager)
            Dim fg = (t.Flags And &H2US) <> 0US
            If fg Then facegenCount += 1
            Dim ns = (t.Flags And &H1US) <> 0US, ms = (t.Flags And &H4US) <> 0US
            Dim fid = t.FormID, eid = If(t.EditorID, ""), fl = t.Flags
            Dim hasD = Not String.IsNullOrEmpty(t.DiffuseTexture)
            Dim hasN = Not String.IsNullOrEmpty(t.NormalTexture)
            Dim hasS = Not String.IsNullOrEmpty(t.SmoothSpecTexture)
            Dim src = If(rec.SourcePluginName, "")
            Logger.LogLazy(Function() $"[TXST-DUMP] 0x{fid:X8} '{eid}' flags=0x{fl:X4} facegen={fg} noSpec={ns} msn={ms} D={hasD} N={hasN} S={hasS} plugin='{src}'")
        Next
        Dim totL = total, fgL = facegenCount
        Logger.LogLazy(Function() $"[TXST-DUMP] === total={totL} facegen={fgL} (full D/N/S) / no-facegen={totL - fgL} (diffuse-only en el gate) ===")
    End Sub

    ''' <summary>Decide si un slot TX0n del TXST pisa al material y loguea la decisión (tag
    ''' <c>[TXST-SLOT]</c>) — incluido el motivo del SKIP. Por defecto aplica si el path resuelve en
    ''' FilesDictionary; el ÚNICO skip es <paramref name="gatedSlot"/> (True para todo menos Diffuse)
    ''' AndAlso <paramref name="diffuseOnly"/> (head-part sin flag 'Facegen Textures'). Ver
    ''' ApplyTextureSetToMaterial para la regla completa.</summary>
    Private Shared Function TxstSlotDecision(txstFid As UInteger, label As String, txstPath As String,
                                             currentValue As String, gatedSlot As Boolean, diffuseOnly As Boolean) As Boolean
        Dim hasPath = Not String.IsNullOrEmpty(txstPath)
        Dim resolves = TxstSlotResolves(txstPath, label, currentValue)
        Dim blocked = gatedSlot AndAlso diffuseOnly
        Dim apply = resolves AndAlso Not blocked
        If Logger.Enabled Then
            Dim reason As String
            If apply Then
                reason = "APPLY"
            ElseIf Not hasPath Then
                reason = "skip:empty-path"
            ElseIf blocked Then
                reason = "skip:HEADPART-DIFFUSE-ONLY"
            ElseIf Not resolves Then
                reason = "skip:unresolved-in-dict"
            Else
                reason = "skip:unknown"
            End If

            Dim pathL = If(txstPath, ""), keptL = If(currentValue, ""), reasonL = reason
            Dim resolvesL = resolves, doL = diffuseOnly, gsL = gatedSlot
            Logger.LogLazy(Function() $"[TXST-SLOT] txst=0x{txstFid:X8} slot={label} txstPath='{pathL}' resolves={resolvesL} gatedSlot={gsL} diffuseOnly={doL} → {reasonL} (kept='{keptL}')")
        End If
        Return apply
    End Function

    ''' <summary>True when a TXST TX0n path is non-empty AND its file exists in the
    ''' FilesDictionary (BA2 / loose pool). Logs a one-line drop trace when the path is
    ''' set but unresolvable, so empirical rule confirmation stays visible in the log.</summary>
    Private Shared Function TxstSlotResolves(txstPath As String, slotLabel As String, currentSlotValue As String) As Boolean
        If String.IsNullOrEmpty(txstPath) Then Return False
        Dim normalized = FO4UnifiedMaterial_Class.CorrectTexturePath(txstPath)
        If String.IsNullOrEmpty(normalized) Then Return False
        If FilesDictionary_class.Dictionary.ContainsKey(normalized) Then Return True
        Dim keptValue = If(currentSlotValue, "")
        Return False
    End Function

    ' ApplyMaterialSwap lives in the shared ShapeMaterialOverrides module (in FO4_Base_Library)
    ' so it can be reused by the NPC ObjectTemplate / OMOD path. The generic material-resolution
    ' helpers (EnsureShapeMaterialResolved / TryLoadMaterialFromDictionary) now live in the lib's
    ' MaterialResolver module and are called directly from there, e.g.
    ' `ShapeMaterialOverrides.ApplyMaterialSwap(formID, func, shapes, _pluginManager)`.

    Private Function ResolveHairTintColor(candidate As MeshCandidate, state As NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        ' Hair/FacialHair/Brow all read NPC.HCLF (see ApplyMaterialPaletteHairColor for the
        ' rationale: BCLF ignored at render/bake, preserved untouched in the ESP).
        Select Case candidate.HeadPartType
            Case HeadPartTypeHair, HeadPartTypeFacialHair, 6
                Dim hairColor = ResolveColorFormColor(state.HairColorFormID)
                If hairColor.HasValue Then Return hairColor
        End Select

        If headPartColor.HasValue Then Return headPartColor
        Return Nothing
    End Function


    Private Function TryResolveHairPaletteRemap(candidate As MeshCandidate, state As NPCVisualState, ByRef paletteTexture As String, ByRef paletteScale As Single) As Boolean
        paletteTexture = ""
        paletteScale = 0.0F

        If candidate Is Nothing OrElse state Is Nothing OrElse Not IsHairHeadPart(candidate) Then Return False

        ' Hair/FacialHair/Brow all read NPC.HCLF (see ApplyMaterialPaletteHairColor).
        Dim colorFormID As UInteger = state.HairColorFormID

        Dim clfm = ResolveColorFormData(colorFormID)
        If clfm Is Nothing OrElse Not clfm.HasRemappingIndex Then Return False

        paletteTexture = ResolveRaceHairLookupTexture(state, _pluginManager)
        If paletteTexture = "" Then Return False

        paletteScale = clfm.RemappingIndex
        Return True
    End Function

    ''' <summary>Resolve the effective hair palette texture path for a given host + state, using
    ''' the BGSM-first / RACE-fallback rule the renderer applies. Single source of truth so the
    ''' UI swatch, the NIF-load material override, and the live-tint refresh agree. Returns
    ''' "" when no palette is available from any source.
    ''' <para>Priority:</para>
    ''' <list>
    ''' <item>Walk the host's loaded HAIR shapes (mat.Hair only — NOT every g2p material, which
    '''       would also match recolourable armor) and return the first non-empty
    '''       <c>material.GreyscaleTexture</c>. Per-shape, authored by the stylist, matches what
    '''       the engine binds at TXST slot 3.</item>
    ''' <item>Otherwise fall back to <see cref="ResolveRaceHairLookupTexture"/> (RACE.HNAM/HLTX).
    '''       Vanilla HumanRace declares HNAM and most hair BGSMs duplicate it, but
    '''       HumanChildRace ships without HNAM/HLTX so we must rely on the BGSM there.</item>
    ''' </list></summary>
    Friend Shared Function ResolveHairPaletteTexture(host As NpcRenderHost, state As NPCVisualState, pluginManager As PluginManager) As String
        If host IsNot Nothing AndAlso host.PreviewCtl IsNot Nothing _
           AndAlso host.PreviewCtl.Model IsNot Nothing AndAlso host.PreviewCtl.Model.meshes IsNot Nothing Then
            For Each mesh In host.PreviewCtl.Model.meshes
                If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
                Dim mb = mesh.MeshData.Material.MaterialBase
                If mb Is Nothing Then Continue For
                ' Require a REAL hair material (BGSM Hair flag). The old test
                ' "mb.Hair OrElse mb.GrayscaleToPaletteColor" also matched recolourable ARMOR
                ' (GrayscaleToPaletteColor=True, Hair=False): when the NPC wore e.g. combat armor,
                ' its palette (CombatArmor_palette_d) preceded the hair shape in the mesh list and
                ' was returned as the brow LUT instead of the hair colour LUT (HairColor_*_d). That
                ' was the root cause of the wrong / load-order-"unstable" brow palette. Armor has
                ' Hair=False, so this filter excludes it; bald NPCs fall through to RACE HNAM/HLTX.
                If Not mb.Hair Then Continue For
                Dim gtex = If(mb.GreyscaleTexture, "")
                If gtex <> "" Then Return gtex
            Next
        End If
        Return ResolveRaceHairLookupTexture(state, pluginManager)
    End Function

    Friend Shared Function ResolveRaceHairLookupTexture(state As NPCVisualState, pluginManager As PluginManager) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI OrElse pluginManager Is Nothing Then Return ""

        Dim raceRec = pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)
        If race Is Nothing Then Return ""

        Dim lookupCandidates = New String() {race.HairColorLookupTexture, race.HairColorExtendedLookupTexture}
        For Each lookupTexture In lookupCandidates
            Dim correctedPath = FO4UnifiedMaterial_Class.CorrectTexturePath(lookupTexture)
            If correctedPath <> "" AndAlso FilesDictionary_class.Dictionary.ContainsKey(correctedPath) Then
                Return lookupTexture
            End If
        Next

        For Each lookupTexture In lookupCandidates
            If Not String.IsNullOrWhiteSpace(lookupTexture) Then Return lookupTexture
        Next

        Return ""
    End Function

    Private Shared Function IsHairHeadPart(candidate As MeshCandidate) As Boolean
        If candidate Is Nothing OrElse candidate.Kind <> MeshCandidateKind.HeadPart Then Return False
        ' Hair (3), Facial Hair (4), Hairline/Brow (6) all use hair color
        Return candidate.HeadPartType = HeadPartTypeHair OrElse
               candidate.HeadPartType = HeadPartTypeFacialHair OrElse
               candidate.HeadPartType = 6
    End Function
    Private Function ResolveSkinTintColor(candidate As MeshCandidate, state As NPCVisualState, headPartColor As Nullable(Of Color)) As Nullable(Of Color)
        ' PRIORITY 1: the NPC's SkinTone tint layer (TETI slot 12).
        ' This is the authoritative source for a character's skin color in FO4 — it's what the engine
        ' uses when applying skin tint. Both my face tint overlay (which skips SkinTone) and the legacy
        ' SkinTintColor multiplier need this value to produce the correct final color.
        If state IsNot Nothing AndAlso candidate IsNot Nothing AndAlso candidate.HeadPartType = HeadPartTypeFace Then
            Dim skinToneColor = ResolveNpcSkinToneColor(state)
            If skinToneColor.HasValue Then Return skinToneColor
        End If

        If state IsNot Nothing AndAlso state.HasTextureLighting Then
            Return state.TextureLightingColor
        End If

        If candidate.HeadPartType = HeadPartTypeFace AndAlso headPartColor.HasValue Then
            Return headPartColor
        End If

        Return Nothing
    End Function

    ''' <summary>Resolve the NPC's effective skin-tone colour and pack it RGB + (tl.Value as
    ''' alpha) — same shape as QNAM RGBA. Body SoftLight reads .A as the opacity factor; the
    ''' face compositor reads tl.Value directly. Both stay in lockstep because they trace back
    ''' to the same source: the layer at the race's SkinTone slot, which is what the engine's
    ''' <c>characterCreation-&gt;skinTint</c> pointer resolves to (verified F4SE
    ''' ScaleformNatives.cpp:860-922).
    ''' <para>The Slot enum value here is a schema-defined field name (xEdit
    ''' wbDefinitionsFO4.pas:3478), not a hardcoded magic number. Returns Nothing when the NPC
    ''' has no layer at the SkinTone slot or the race / CLFM lookup fails.</para></summary>
    Private Function ResolveNpcSkinToneColor(state As NPCVisualState) As Nullable(Of Color)
        If state Is Nothing Then Return Nothing
        Dim modelNpcFormID = FaceAppearanceSourceFormID(state)
        Dim npcData = ApplyPresetOverlayToNpcData(GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        Dim raceRec = _pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = ParseRaceCached(raceRec)

        ' Single source of truth — same derivation NpcRecordOverlay uses at save time, so the
        ' preview's body skin tone and the persisted ESP's QNAM are guaranteed to agree.
        Return NpcRecordOverlay.DeriveSkinToneQnam(npcData, race, state.IsFemale, _pluginManager)
    End Function

    Private Function ResolveColorFormColor(formID As UInteger) As Nullable(Of Color)
        Dim clfm = ResolveColorFormData(formID)
        If clfm Is Nothing OrElse Not clfm.HasColor Then Return Nothing
        Return clfm.Color
    End Function

    Private Function ResolveColorFormData(formID As UInteger) As CLFM_Data
        If formID = 0UI Then Return Nothing

        Dim rec = _pluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Return Nothing

        Return RecordParsers.ParseCLFM(rec, _pluginManager)
    End Function

    ''' <summary>Thin wrapper over <see cref="MeshPathHelpers.NormalizeMeshKey"/>; centralizes
    ''' path normalization in MeshPathHelpers so render path + offline bake never drift.</summary>
    Private Shared Function NormalizeDictionaryKeyWithMeshesPrefix(path As String) As String
        Return MeshPathHelpers.NormalizeMeshKey(path)
    End Function

    Private Shared Function HasTemplateFlag(flags As UShort, category As NPC_TemplateCategory) As Boolean
        Dim mask = CUShort(1 << CInt(category))
        Return (flags And mask) <> 0US
    End Function

    Private Shared Function ResolveTemplateSourceFormID(npc As NPC_Data, category As NPC_TemplateCategory) As UInteger
        Dim specificFormID As UInteger = 0UI
        If npc.TemplateActorFormIDs.TryGetValue(category, specificFormID) AndAlso specificFormID <> 0UI Then
            Return specificFormID
        End If

        Return npc.TemplateFormID
    End Function

    ''' <summary>Thin instance wrapper over <see cref="NpcRecordOverlay.GetParsedNpc"/>;
    ''' threads <see cref="_pluginManager"/> through. Real impl lives in the helper module
    ''' so offline bake (FaceGenBuilder) can reuse without touching MainForm.</summary>
    Private Function GetParsedNpc(formID As UInteger) As NPC_Data
        ' Route through the bulk-parse cache: _npcByIdCache holds the same full ParseNPC output the
        ' helper would re-produce (both use RecordParsers.ParseNPC), so a hit avoids re-parsing the
        ' record 5+ times per frame. Miss = a FormID outside the placed-NPC universe (e.g. a TPLT
        ' model source) → parse via the helper and memoize so repeat lookups within the render are free.
        Dim cached As NPC_Data = Nothing
        If _npcByIdCache IsNot Nothing AndAlso _npcByIdCache.TryGetValue(formID, cached) AndAlso cached IsNot Nothing Then
            Return cached
        End If
        Dim parsed = NpcRecordOverlay.GetParsedNpc(formID, _pluginManager)
        If parsed IsNot Nothing AndAlso _npcByIdCache IsNot Nothing Then _npcByIdCache(formID) = parsed
        Return parsed
    End Function

    Friend Shared Function CreateOwnTraitsState(npc As NPC_Data) As TraitsState
        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM
        ' now seeded here so they ride the Traits chain walk.
        Dim state As New TraitsState With {
            .SourceFormID = npc.FormID,
            .IsFemale = npc.IsFemale,
            .RaceFormID = npc.RaceFormID,
            .SkinFormID = npc.SkinFormID,
            .WeightThin = npc.WeightThin,
            .WeightMuscular = npc.WeightMuscular,
            .WeightFat = npc.WeightFat,
            .HeadTextureFormID = npc.HeadTextureFormID,
            .HairColorFormID = npc.HairColorFormID,
            .FacialHairColorFormID = npc.FacialHairColorFormID,
            .HasTextureLighting = npc.HasTextureLighting,
            .TextureLightingColor = npc.TextureLightingColor
        }
        state.HeadPartFormIDs.AddRange(npc.HeadPartFormIDs)
        Return state
    End Function

    Private Shared Function CreateOwnInventoryState(npc As NPC_Data) As InventoryState
        Return New InventoryState With {
            .DefaultOutfitFormID = npc.DefaultOutfitFormID,
            .SleepOutfitFormID = npc.SleepOutfitFormID
        }
    End Function

    Private Shared Function CreateOwnModelAnimationState(npc As NPC_Data) As ModelAnimationState
        ' [TEST: TPLT-traits-bucket] Face-appearance fields moved to CreateOwnTraitsState.
        Dim state As New ModelAnimationState
        state.ObjectTemplateOMODFormIDs.AddRange(npc.ObjectTemplateOMODFormIDs)
        state.ObjectTemplateCombinations.AddRange(npc.ObjectTemplateCombinations)
        state.HasObjectTemplate = npc.HasObjectTemplate
        If npc.AttachParentSlotFormIDs IsNot Nothing Then
            state.AttachParentSlotFormIDs.AddRange(npc.AttachParentSlotFormIDs)
        End If
        Return state
    End Function

    Private Shared Function DescribeNpc(npc As NPC_Data) As String
        If npc Is Nothing Then Return "<unknown NPC>"
        If npc.EditorID <> "" Then Return npc.EditorID
        If npc.FullName <> "" Then Return npc.FullName
        Return npc.FormID.ToString("X8")
    End Function

    Private Shared Function DescribeRecord(rec As PluginRecord) As String
        If rec Is Nothing Then Return "<unknown record>"
        If rec.EditorID <> "" Then Return rec.EditorID
        Return $"{rec.Header.Signature} {rec.Header.FormID:X8}"
    End Function

    Private Shared Sub DeduplicateWarnings(warnings As List(Of String))
        If warnings Is Nothing OrElse warnings.Count <= 1 Then Return
        Dim unique = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList()
        warnings.Clear()
        warnings.AddRange(unique)
    End Sub

    Private Shared Function BuildWarningSuffix(warnings As IList(Of String)) As String
        If warnings Is Nothing OrElse warnings.Count = 0 Then Return ""
        Return $" ({warnings(0)})"
    End Function

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
                    AddNode(tplNode, $"TPTA[{kvp.Key}] ({GetTemplateCategoryLabel(kvp.Key)}): {DescribeFormID(kvp.Value)}")
                Next
                Dim flagList As New List(Of String)
                For Each boxedCat In [Enum].GetValues(GetType(NPC_TemplateCategory))
                    Dim cat = CType(boxedCat, NPC_TemplateCategory)
                    If HasTemplateFlag(npc.TemplateFlags, cat) Then flagList.Add(GetTemplateCategoryLabel(cat))
                Next
                If flagList.Count > 0 Then AddNode(tplNode, $"Active flags: {String.Join(", ", flagList)}")
                tplNode.Expand()
            End If

            ' --- Traits (with inheritance) ---
            Dim traitsSource = ResolveInheritedSourceNpc(npc, NPC_TemplateCategory.Traits)
            Dim traitsNpc = If(traitsSource, npc)
            Dim traitsLabel = If(traitsSource IsNot Nothing AndAlso traitsSource.FormID <> npc.FormID,
                                 $"Traits  (inherited from {DescribeNpc(traitsSource)} [{traitsSource.FormID:X8}])",
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
                              $"Inventory  (inherited from {DescribeNpc(invSource)} [{invSource.FormID:X8}])",
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
                                $"Appearance  (inherited from {DescribeNpc(modelSource)} [{modelSource.FormID:X8}])",
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
                        Dim hdpt = ParseHdptCached(hpRec)
                        Dim typeName = GetHeadPartTypeName(hdpt.PartType)
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
        If npc Is Nothing OrElse Not HasTemplateFlag(npc.TemplateFlags, category) Then Return npc

        Dim visited As New HashSet(Of UInteger)
        Dim current = npc

        While current IsNot Nothing
            If visited.Contains(current.FormID) Then Exit While
            visited.Add(current.FormID)

            If Not HasTemplateFlag(current.TemplateFlags, category) Then Return current

            Dim sourceFormID = ResolveTemplateSourceFormID(current, category)
            If sourceFormID = 0UI Then Return current

            ' If source is a leveled NPC, try to get the first entry
            Dim sourceRec = _pluginManager.GetRecord(sourceFormID)
            If sourceRec Is Nothing Then Return current

            If sourceRec.Header.Signature = "NPC_" Then
                current = GetParsedNpc(sourceFormID)
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
                current = GetParsedNpc(firstNpcId)
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

        Dim race = ParseRaceCached(raceRec)
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
                Dim armo = GetParsedArmo(itemFormID)
                Dim slotStr = FormatSlotMask(armo.SlotMask)
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
                    Dim arma = GetParsedArma(aaFormID)
                    Dim aaNode = AddNode(armoNode, $"ARMA {arma.EditorID}  [{arma.FormID:X8}]  Slots:{FormatSlotMask(arma.SlotMask)}")
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
    Private Shared Function GetHeadPartTypeName(partType As Integer) As String
        Select Case partType
            Case 0 : Return "Misc"
            Case 1 : Return "Face"
            Case 2 : Return "Eyes"
            Case 3 : Return "Hair"
            Case 4 : Return "Facial Hair"
            Case 5 : Return "Scar"
            Case 6 : Return "Eyebrows"
            Case 7 : Return "Meatcaps"
            Case 8 : Return "Teeth"
            Case 9 : Return "Head Rear"
            Case Else : Return $"Type{partType}"
        End Select
    End Function

    Private Shared Function FormatSlotMask(mask As UInteger) As String
        If mask = 0UI Then Return "(none)"
        Dim slots As New List(Of String)
        Dim bitMask As UInteger = 1UI
        For bit = 0 To 31
            If (mask And bitMask) <> 0UI Then
                slots.Add((30 + bit).ToString())
            End If
            bitMask <<= 1
        Next
        Return String.Join(",", slots)
    End Function

#End Region

    Private Sub MainForm_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' Persist UI-level config BEFORE teardown. Setting_Lightrig lives in shared Config_App
        ' (written in-memory by LightRigForm); RenderGore is NPC-only and lives in NPC_Config.
        NPC_Config.Current.RenderGore = CheckBoxRenderGore.Checked
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
        If Not _npcByIdCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
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
            race = ParseRaceCached(raceRec)
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
            Dim hd = ParseHdptCached(rec)
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
                raceForTintNorm = ParseRaceCached(raceRecForTintNorm)
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
                           baseState = ResolveNPCBaseState(npc, host)
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
            ClearFaceTintCaches()
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
            Dim modelFormID = FaceAppearanceSourceFormID(baseState)
            Dim effective = ApplyPresetOverlayToNpcData(GetParsedNpc(modelFormID), baseState.RootNpcFormID)
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
                                                            AddressOf ParseRaceCached)
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
        Dim modelFormID = FaceAppearanceSourceFormID(state)
        Dim raw = GetParsedNpc(modelFormID)
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
        Dim merged = MergeHeadPartsWithRaceDefaults(state)
        For Each fid In merged
            If fid = 0UI Then Continue For
            Dim rec = _pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue For
            Dim hd = ParseHdptCached(rec)
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
            race = ParseRaceCached(raceRec)
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
            Dim race = ParseRaceCached(raceRec)
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
        Dim modelNpcFormID = FaceAppearanceSourceFormID(_renderHost.LastRenderedState)
        Dim effectiveNpc = ApplyPresetOverlayToNpcData(GetParsedNpc(modelNpcFormID), _renderHost.LastRenderedState.RootNpcFormID)
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
        If Not _npcByIdCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then Return

        ' Raw record DOFT drives the "(record default)" pinned entry → Nothing semantic.
        Dim modelFormID = If(st.ModelSourceFormID <> 0UI, st.ModelSourceFormID, npcFormID)
        Dim rawNpc = GetParsedNpc(modelFormID)
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
        Dim race = ParseRaceCached(raceRec)
        If race Is Nothing Then Return avail

        Dim headParts = If(state.IsFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)
        avail.HasHeadParts = (headParts IsNot Nothing AndAlso headParts.Count > 0)

        Dim hairColors = If(state.IsFemale, race.FemaleHairColorFormIDs, race.MaleHairColorFormIDs)
        avail.HasHairColors = (hairColors IsNot Nothing AndAlso hairColors.Count > 0)

        Dim tintGroups = If(state.IsFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        avail.HasFaceTints = (tintGroups IsNot Nothing AndAlso tintGroups.Count > 0)

        avail.HasFaceBoneRegions = (GetFacialBoneRegionsForRace(race, state.IsFemale) IsNot Nothing)

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
        Dim race = ParseRaceCached(raceRec)
        If race Is Nothing Then Return

        ' Capture the raw NPC's AcbsFlags so the Edit Face form can compute the original bit and
        ' the form's Cancel rollback can restore it (the overlay only stores Boolean? — the raw
        ' value lives on the NPC record).
        Dim modelNpcFormID = If(_renderHost.LastRenderedState.ModelSourceFormID <> 0UI, _renderHost.LastRenderedState.ModelSourceFormID, _renderHost.LastRenderedState.FormID)
        Dim rawNpc = GetParsedNpc(modelNpcFormID)
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
                result = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
            Else
                result = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
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
                            Dim name = If(_npcByIdCache.TryGetValue(fid, npc) AndAlso npc IsNot Nothing, npc.ToString(), fid.ToString("X8"))
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
                                    r = FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
                                Else
                                    r = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets, _renderHost, AddressOf ApplyShapeMaterialOverrides, willBePacked:=False, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate))
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
        Dim raw = GetParsedNpc(modelFormID)
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
        If Not _npcByIdCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
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
        If Not _npcByIdCache.TryGetValue(npcFormID, npc) OrElse npc Is Nothing Then
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
        _npcByIdCache.TryGetValue(npcFormID, npc)
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
            If _npcByIdCache.TryGetValue(fid, cachedNpc) AndAlso cachedNpc IsNot Nothing Then
                If Not String.Equals(cachedNpc.PluginName, savedPluginName, StringComparison.OrdinalIgnoreCase) Then
                    cachedNpc.PluginName = savedPluginName
                    _npcSearchableCache(fid) = BuildNpcSearchableText(cachedNpc)
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
            If _npcByIdCache.TryGetValue(reloadFid, npc) AndAlso npc IsNot Nothing Then
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
        _npcByIdCache.TryGetValue(npcFormID, npc)

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
                                                         _renderHost, AddressOf ApplyShapeMaterialOverrides,
                                                         willBePacked:=True, lmSkinTemplateResolver:=AddressOf ResolveLmSkinTemplate)
            Else
                bakeResult = Await Task.Run(Function() FaceGenBuilder.BuildCharGen(fidL, _pluginManager, _appliedPresets,
                                                         _renderHost, AddressOf ApplyShapeMaterialOverrides,
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




























