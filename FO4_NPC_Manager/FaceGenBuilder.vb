Imports System.IO
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics

''' <summary>
''' Build CharGen — generates a baked FaceGen NIF for the current NPC by starting from the
''' vanilla FaceGen NIF in the BA2/loose pool and pruning shapes that don't correspond to a
''' HeadPart the NPC currently references. Output is written as .nif2 (not .nif) under a
''' sandbox path so it never collides with the engine's lookup; the file is meant for
''' side-by-side diff against the BA2 original.
'''
''' v0 strategy:
'''   1. Resolve origin plugin from FormID high-byte; build the FaceGen path the engine
'''      would load: Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;plugin&gt;\&lt;FormID8hex&gt;.nif.
'''   2. Fetch bytes via FilesDictionary_class.GetBytes (BA2 + loose with override semantics).
'''   3. Build the "allowed shape names" set: union of EditorID for every HDPT in
'''      NPC_.HeadPartFormIDs plus everything in their HDPT.ExtraPartFormIDs (HNAM) — the
'''      engine expansion of the head part chain.
'''   4. RemoveShape_Manolo() any shape whose Name is not in that set.
'''   5. Save_As_Manolo() with .nif2 extension under &lt;exe dir&gt;\BakedFaceGen\...
'''
''' Match is case-insensitive because NIF shape Name and HDPT.EditorID may differ in case
''' across vanilla vs modded content.
'''
''' Each run also writes a structured dump to npc_preview.log: NIF shape list, NPC HeadParts
''' (with PartType + MeshPath), and the kept/dropped decision per shape with reason.
''' </summary>
Public Module FaceGenBuilder

    ''' <summary>HDPT.PartType enum values per xEdit wbDefinitionsFO4.pas:7373-7384.</summary>
    Public Const PartTypeMisc As Integer = 0
    Public Const PartTypeFace As Integer = 1
    Public Const PartTypeEyes As Integer = 2
    Public Const PartTypeHair As Integer = 3
    Public Const PartTypeFacialHair As Integer = 4
    Public Const PartTypeScar As Integer = 5
    Public Const PartTypeEyebrows As Integer = 6
    Public Const PartTypeMeatcaps As Integer = 7
    Public Const PartTypeTeeth As Integer = 8
    Public Const PartTypeHeadRear As Integer = 9

    ' (Removido: _srgbToG22Lut / BuildSrgbToG22Lut / ApplySrgbToGamma22Diffuse — el encode de storage
    '  sRGB->g22 del diffuse ahora nace en el SEED del path único (FaceTintCompositor.ApplyFaceTintPipeline,
    '  en float, sin tabla byte->byte). Ver el comentario del slot 0 en BakeFaceTextures.)

    ''' <summary>Resolve the FaceGen NIF path the engine would load for this NPC. Path layout
    ''' is "Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;origin plugin filename&gt;\&lt;FormID8hex&gt;.nif"
    ''' where origin plugin is the master that owns this FormID — high-byte of the global
    ''' FormID resolved through PluginManager.GetOriginatingPluginName (which handles ESL
    ''' FE prefix correctly via record SourcePluginName).</summary>
    Public Function ResolveFaceGenPath(npcFormID As UInteger, pluginManager As PluginManager) As String
        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then Return ""
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return $"Meshes\Actors\Character\FaceGenData\FaceGeom\{originPlugin}\{formIdLow:X8}.nif"
    End Function

    ''' <summary>Result of a BuildCharGen run.</summary>
    Public Class BuildResult
        Public Property Success As Boolean
        ''' <summary>True when the NPC has no FaceGen-eligible head parts (non-human race, robot,
        ''' etc.) so there was nothing to bake. NOT a failure — callers should count it as a SKIP
        ''' (no .nif written). When True, Success is False.</summary>
        Public Property Skipped As Boolean
        ''' <summary>Where the .nif2 was written (only when Success). Empty otherwise.</summary>
        Public Property OutputPath As String = ""
        ''' <summary>One-line user-facing summary suitable for a MessageBox.</summary>
        Public Property Summary As String = ""
        Public Property ShapesKept As Integer
        Public Property ShapesDropped As Integer
        ''' <summary>SSE only: True when this NPC's facetint was folded into the head diffuse, so the head shape's
        ''' slot 3 (detail) was pointed at the plugin's SHARED neutral-detail gray (facedetailneutral.dds, softlight
        ''' identity 0.5) instead of a real detail map. Signals the packer (via <see cref="NpcFaceGenPacker.BakedNpcBundle"/>)
        ''' to also pack that single shared detail. (The facetint itself stays a per-NPC canonical &lt;id&gt;.dds — the
        ''' engine builds that path itself and ignores the NIF slot 6, so it can't be shared.)</summary>
        Public Property UsedSharedNeutralDetail As Boolean
        ''' <summary>FO4 face-texture bake: number of face-texture outputs (slots 0/1/7) that FAILED to
        ''' encode/write, or that had no source to bake. 0 = all good. The NIF still wrote (Success stays
        ''' True), but every missing DDS will surface as "unaccounted for" at BA2 pack time — so this count
        ''' lets the save summary show the CAUSE instead of a silent "1 OK" followed by "0/1 packed".</summary>
        Public Property TextureSlotsFailed As Integer
        ''' <summary>First texture-bake failure reason (exception type + message + slot/size/format, or the
        ''' bail reason). Representative message for the user-facing summary. Empty when TextureSlotsFailed=0.</summary>
        Public Property TextureFailureDetail As String = ""
    End Class

    ''' <summary>SSE fold scratch flag: set by <c>WriteSseFaceDiffuseWithOverlays</c> (non-forced path) when it
    ''' points the head NIF's slot 6 at the plugin's shared neutral gray. Reset immediately before the SSE bake
    ''' call and read immediately after — both synchronous within one <c>BuildCharGen</c> (no await between), so
    ''' the module-level scratch is race-free (bakes run sequentially on the awaited UI thread).</summary>
    Private _sseFoldUsedSharedNeutralDetail As Boolean

    ''' <summary>Dual-mode bake toggle, DRIVEN BY THE LOGGER. ON only when
    ''' <see cref="Logger.Enabled"/> is True (diagnostic session); OFF (release) otherwise.
    ''' OFF (release): output canonical paths (<formID>.nif + _d.dds / _msn.dds / _s.dds) — pisa el
    ''' CK BA2 bake; el engine in-game usa nuestro output; texturas comprimidas BC3/BC5; sin
    ''' comparator ni dumps. ON (logger activo): output sandbox (<formID>_2.nif + _d_2.dds etc.)
    ''' alongside CK's, B8G8R8A8 sin comprimir; el comparator se dispara contra el CK BA2 baseline y
    ''' loguea <c>[BUILDCHARGEN-DIFF]</c> / <c>[FACEBAKE-TEXDIFF]</c>. Para diagnosticar contra CK,
    ''' encender el Logger (Logger.Enabled = True). Read-only a propósito: el modo debug y el logging
    ''' van juntos, así no quedan desincronizados (ver arch_facegen_debug_mode memory).</summary>
    Public ReadOnly Property DebugMode As Boolean
        Get
            Return Logger.Enabled
        End Get
    End Property
    ''' <summary>Cuando True, el bake corre TAMBIÉN el pipeline GL y escribe el `_2b.dds` (salida GPU) al
    ''' lado del `_2.dds` (CPU) para comparar GPU-vs-CPU. Atado a DebugMode (Logger.Enabled): en build Debug
    ''' sale automático junto con el `_2`; en Release no corre GL (bake CPU-only). Toca GL ⇒ el caller
    ''' (MainForm.BuildCharGenSingle) lo agenda SYNC en el hilo UI (contexto GL).</summary>
    ''' AHORA SWITCHEABLE: el CLI headless lo apaga (=False) para correr el bake 100% CPU sin GL (needGl=
    ''' WriteGPUSandboxOutput), manteniendo el naming `_2` (DebugMode=Logger.Enabled). Default
    ''' (override=Nothing) = comportamiento de la app (Logger.Enabled).
    ''' <summary>Enciende/apaga el BAKE de texturas de cara (SSE: facetint _d + fold de overlays; FO4:
    ''' FaceCustomization D/N/S). Default True = comportamiento normal de la app. El barrido de validación
    ''' de NIF del CLI lo apaga para no componer DDS (es el costo dominante del batch).
    ''' ⚠️ OJO: apagarlo NO es neutro para el NIF — esas rutinas además REESCRIBEN slots del shader
    ''' (SSE: slot 6 facetint y el slot 0 plegado; FO4: slots 0/1/7). Con esto en False, esos slots
    ''' quedan como los dejó la resolución de material, así que un barrido en este modo NO valida el
    ''' slot 6 (ni el fold del slot 0). Para declarar 100% hay que correr además una pasada con DDS.</summary>
    Public Property BakeFaceTexturesEnabled As Boolean = True

    ''' <summary>Saltea el ENCODE DDS (BCn + mips) y su escritura a disco, en LOS DOS JUEGOS: FO4 (los 3 canales
    ''' D/_msn/_s de FaceCustomization) y SSE (el facetint _d). SOLO para barridos que validan el NIF
    ''' (--ssecomparebatch), donde los pixeles del DDS no se miran. Junto con
    ''' <see cref="FaceTintCpuCompositor.SkipPixelCompose"/> saca el costo per-NPC dominante del barrido FO4.
    ''' ⛔ NO cambia lo que el bake escribe en el NIF: el texture-set se crea igual y los paths de los slots se
    ''' escriben igual (son deterministas: formID + plugin + sufijo), como si el encode hubiera salido bien.
    ''' El decode de los sources NO se gatea: ya esta amortizado entre NPCs por BatchDecodeCache y ademas es lo
    ''' que determina que slots existen.</summary>
    Public Property SkipDdsEncode As Boolean = False

    Private _gpuSandboxOverride As Boolean? = Nothing
    Public Property WriteGPUSandboxOutput As Boolean
        Get
            Return If(_gpuSandboxOverride, Logger.Enabled)
        End Get
        Set(value As Boolean)
            _gpuSandboxOverride = value
        End Set
    End Property
    ''' <summary>Tilde "Generate TGA" del diálogo CharGen Options (persistido en Config). Cuando está ON,
    ''' escribe un TGA UNCOMPRESSED al lado de cada .dds (CPU y, si corrió, GPU) — lossless aunque el .dds
    ''' sea BCn. ReadOnly: lo maneja el setting, no un setter externo.</summary>
    Public ReadOnly Property WriteTGASandboxOutput As Boolean
        Get
            Return If(Config_App.Current IsNot Nothing, Config_App.Current.Setting_FaceGenGenerateTga, False)
        End Get
    End Property


    ''' <summary>Settings de salida del bake (resolución por canal + compresión del diffuse), DERIVADO del
    ''' config persistido (Config_App, botón "CharGen Options"). Single source of truth = config; sin estado
    ''' que sincronizar. Se pasa idéntico al compositor GL y al CPU (-> GL==CPU). Lógica de tamaño:
    '''   PerLayer=False (ALL, default): los 3 canales usan el tamaño Diffuse (N/S heredan de D).
    '''   PerLayer=True: cada canal su propio tamaño.
    ''' Default config = All + Inherit (nativo) + BC3 = comportamiento actual / byte-comparable a gen3.</summary>
    Public ReadOnly Property OutputSettings As FaceTintConvention.FaceTintResolutionSettings
        Get
            Dim c = Config_App.Current
            Dim isSse = (c.Game = Config_App.Game_Enum.Skyrim)
            Dim d = c.Setting_FaceGenDiffuseResolution
            Dim perLayer = c.Setting_FaceGenPerLayerResolution
            ' Compresión PER-GAME (set del juego activo → sin leak entre juegos). All-mode: FO4 deriva N del D
            ' (NsCompressionFromDiffuse → BC5 tangent-space); SSE el N sigue al D (model-space, "All uniforme").
            ' Per-layer: cada canal el suyo. Specular = FO4-only (SSE no lo bakea).
            Dim dc = If(isSse, c.Setting_FaceGenDiffuseCompression_SSE, c.Setting_FaceGenDiffuseCompression)
            Dim nc = If(isSse, c.Setting_FaceGenNormalCompression_SSE, c.Setting_FaceGenNormalCompression)
            Return New FaceTintConvention.FaceTintResolutionSettings With {
                .Diffuse = d,
                .Normal = If(perLayer, c.Setting_FaceGenNormalResolution, d),
                .Specular = If(perLayer, c.Setting_FaceGenSpecularResolution, d),
                .DiffuseCompression = dc,
                .NormalCompression = If(perLayer, nc, If(isSse, NormalCompressionAllModeSse(), NsCompressionFromDiffuse(dc))),
                .SpecularCompression = If(perLayer, c.Setting_FaceGenSpecularCompression, NsCompressionFromDiffuse(dc))
            }
        End Get
    End Property

    ''' <summary>Modo All FO4: N/S siguen al Diffuse -> Uncompressed si el Diffuse es Uncompressed, sino BC5
    ''' (el _n de FaceCustomization es tangent-space 2-canales ⇒ BC5).</summary>
    Private Function NsCompressionFromDiffuse(d As FaceTintConvention.FaceTintDiffuseCompression) As FaceTintConvention.FaceTintNormalSpecularCompression
        Return If(d = FaceTintConvention.FaceTintDiffuseCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Bc5)
    End Function

    ''' <summary>Modo All SSE: el normal SIEMPRE Uncompressed — NO sigue al diffuse. El <c>_msn</c> es MODEL-SPACE:
    ''' sus 3 canales son X/Y/Z INDEPENDIENTES, y cualquier BCn comprime RGB a una línea por bloque 4×4, destruyendo la
    ''' dirección de la normal. MEDIDO (probe <c>--reencodetest</c>, mismo encoder del bake, MaleHead_msn 1024²):
    ''' BC3 → RGB RMS 5.07/255 (max B 148/255, 97.5% pixels alterados); Uncompressed → RMS 0.000 = round-trip EXACTO,
    ''' pixel-idéntico al vanilla (que ES Uncompressed 32bpp). El shader facegen lee el G-buffer de normales (o2.xy) de
    ''' este slot ⇒ comprimirlo rompe lighting/sombras/reflexiones de toda la cara. El diffuse SÍ tolera BCn (es color),
    ''' por eso el normal se desacopla de él. Vale para CUALQUIER caso (con o sin overlay-normal). El usuario puede
    ''' forzar otro formato en per-layer (CharGen Options), pero el DEFAULT del modo All es el fiel al vanilla.</summary>
    Private Function NormalCompressionAllModeSse() As FaceTintConvention.FaceTintNormalSpecularCompression
        Return FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
    End Function

    ''' <summary>True si el output del bake queda LOOSE en disco (no se empaqueta a un BA2): Build CharGen
    ''' loose (Not willBePacked) o Save ESP en modo loose-only (NPC_Config.Ba2Version_FO4 = 0). Los
    ''' artefactos de inspección (TGA, _2b) SOLO se escriben en este caso: el packer (NpcFaceGenPacker) mete
    ''' únicamente NIF + 3 DDS por nombre, así que un .tga/_2b en un BA2-save quedaría huérfano loose.</summary>
    Private Function OutputStaysLoose(willBePacked As Boolean) As Boolean
        If Not willBePacked Then Return True
        ' Game-aware loose sentinel: FO4 = Ba2Version_FO4 = 0 (byte-identical to the old check), SSE = Archive_SSE = 0.
        ' IsLooseOnly null-guards NPC_Config.Current (returns True → stays loose), preserving the prior guard.
        Return NPC_Config.IsLooseOnly(If(Config_App.Current IsNot Nothing, Config_App.Current.Game, Config_App.Game_Enum.Fallout4))
    End Function

    ''' <summary>Build a baked FaceGen NIF for this NPC. See module-level summary for the
    ''' v0 strategy. Always also writes a structured dump to npc_preview.log so the user
    ''' can review the kept/dropped decision per shape. Returns BuildResult; on failure
    ''' OutputPath is empty and Summary explains why.</summary>
    ''' <summary>Delegate matching the signature of <c>MainForm.ApplyShapeMaterialOverrides</c>.
    ''' BuildCharGen invokes this with a one-element shape list to resolve the material for the
    ''' NPC being baked — same code path the live render uses, no preview dependency.</summary>
    Friend Delegate Sub ApplyShapeMaterialOverridesDelegate(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, shapes As IEnumerable(Of IRenderableShape))

    ''' <param name="willBePacked">Distinguishes the two consumers of this bake, which differ ONLY
    ''' in DebugMode and ONLY in the texture path embedded inside the NIF:
    '''   True  = Save ESP path: the loose _2 outputs get repacked into a BA2 under canonical
    '''           (non-_2) names by NpcFaceGenPacker, so the NIF must embed canonical paths
    '''           (&lt;id&gt;_d.dds) to match the renamed BA2 entries.
    '''   False = "Build CharGen (loose)" button: nothing repacks/renames, so the NIF must embed
    '''           the actual on-disk path (&lt;id&gt;_d_2.dds) or the standalone loose NIF references a
    '''           texture that does not exist under that name.
    ''' In release (DebugMode=Off) Suffix == CanonSuffix, so this flag is a no-op.</param>
    Friend Function BuildCharGen(npcFormID As UInteger,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                 willBePacked As Boolean,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing) As BuildResult

        ' Claim the GL context for THIS bake before any GL operation. OpenTK's "current context"
        ' is per-thread, process-wide — multiple PreviewControls coexist (MainForm + EditFace_Form
        ' + EditBody_Form etc.), each with its own context. Any sibling's Invalidate→OnPaint will
        ' MakeCurrent on its own control and steal the context from ours. Without this guard, the
        ' bake's GL ops (texture upload, GetTexImage readback, etc.) target whatever context was
        ' last current — which intermittently caused "diffuse GL texture id 0" bails after the
        ' user occluded the window (a sibling's WM_PAINT fired on restore and stole the context).
        ' No-op when already current.
        ' SOLO en DebugMode el bake usa GL (escribe el _2 de comparación vs el _2b del CPU) -> hay que
        ' MakeCurrent en el hilo GL. En RELEASE el bake es 100% CPU (FaceTintCpuCompositor: decode/compose/
        ' encode por wrapper DirectXTex, sin GL) -> puede correr ASYNC en un thread de fondo, donde
        ' MakeCurrent FALLARIA (el contexto GL es per-thread, del hilo UI). Por eso se saltea fuera de debug.
        ' Gate por WriteGPUSandboxOutput (el que realmente corre GL = needGl), NO por DebugMode: el CLI
        ' headless deja DebugMode=True (naming _2) pero WriteGPUSandboxOutput=False (sin GL) ⇒ no toca contexto.
        ' En la app WriteGPUSandboxOutput==Logger.Enabled==DebugMode, así que el gate es idéntico al previo.
        If WriteGPUSandboxOutput Then
            Try
                host?.PreviewCtl?.EnsureContextCurrent()
            Catch ex As Exception
                Dim msgL = ex.Message
                Dim typeL = ex.GetType().Name
                Logger.LogLazy(Function() $"[FACEBAKE-FAIL] MakeCurrent threw at bake entry npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
            End Try
        End If

        ' Build the visual state for the NPC being baked (NPC Y), independent of whatever NPC
        ' the preview is showing (NPC X). The bake must NEVER read state from the host. We
        ' parse the NPC record + apply LooksMenu preset overlay (same overlay the live render
        ' uses) and copy the six fields the material resolver consumes:
        '   HairColorFormID, FacialHairColorFormID, SkinFormID, HeadTextureFormID, RaceFormID, IsFemale
        ' Other state fields (outfit/loadout/weight/etc.) are not needed by the per-shape
        ' material resolver and stay at default. If the future reveals a resolver path that
        ' touches another field, surface it here and copy it from npcData.
        '
        ' WYSIWYG rule: if the user picked an LM SkinTemplate in EditBody, the bake must apply
        ' that bundle exactly the same way the live render does — otherwise the .nif2 baked here
        ' diverges from the WNAM the writer puts in the ESP. The resolver is forwarded from the
        ' caller (MainForm) so the bake sees the same template the preview saw.
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(
            npcFormID, pluginManager, appliedPresets, lmSkinTemplateResolver)
        Dim state As MainForm.NPCVisualState = Nothing
        If npcData IsNot Nothing Then
            state = New MainForm.NPCVisualState With {
                .FormID = npcFormID,
                .RootNpcFormID = npcFormID,
                .ModelSourceFormID = npcFormID,
                .RaceFormID = npcData.RaceFormID,
                .IsFemale = npcData.IsFemale,
                .SkinFormID = npcData.SkinFormID,
                .HeadTextureFormID = npcData.HeadTextureFormID,
                .HairColorFormID = npcData.HairColorFormID,
                .FacialHairColorFormID = npcData.FacialHairColorFormID,
                .HasTextureLighting = npcData.HasTextureLighting,
                .TextureLightingColor = npcData.TextureLightingColor,
                .HeadDiffuseAlphaTest = (npcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (npcData.AcbsFlags And &H1000000UI) <> 0UI
            }
            state.HeadPartFormIDs.AddRange(npcData.HeadPartFormIDs)
            ' Engine race fallbacks: NPC.WNAM=0 → RACE.SkinFormID, NPC head parts/texture/hair
            ' → RACE defaults, NPC.MWGT sentinel substitution. Same path the render uses; without
            ' it ResolveActorSkinTextureSet returns Nothing for NPCs that leave WNAM=0 (e.g.
            ' vanilla children) and the bake falls through to HDPT.TNAM, which for ChildHeadRear
            ' is hardcoded SkinBodyChildMale — wrong for female actors.
            NpcStateResolver.ApplyRaceFallbacks(state, NpcStateFactory.CreateOwnTraitsState(npcData), pluginManager)
        End If
        Dim result As New BuildResult()

        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then
            result.Summary = "Could not resolve origin plugin for this NPC."
            Return result
        End If

        ' Build a fresh FO4 NIF — same path OutfitStudio takes when importing OBJ/FBX without
        ' a base mesh ([OutfitProject.cpp:515-531] calls workNif.Create(NiVersion::getFO4())).
        ' NiVersion.GetFO4() = (V20_2_0_7, user=12, stream=130), the canonical FO4 framing CK
        ' writes. withRootNode=True drops in the root NiNode the engine expects.
        ' Game-aware bake: SSE (Skyrim) difiere de FO4 en root del shell, zeroing de bounds y tipo de skin
        ' (NiSkinInstance/BSDismember vs BSSkin::Instance). Declarado a scope de metodo para gatear el reparent.
        Dim isSSEBake As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            ' Shell idéntico a CK (verificado con NiflySharp contra los 1094 FaceGen vanilla del BA2,
            ' 2026-06-13): root = NiNode con nombre "" y Flags 0x0000400E — NO BSFadeNode (CK NUNCA usa
            ' BSFadeNode como root: 0/1094) ni el NiNode "Scene Root" que deja Create(withRootNode:=True).
            ' (La nota previa "root=BSFadeNode 0x2000400E, el loader loose lo exige" era FALSA — medida
            ' contra <id>.NIF sueltos que eran bakes viejos nuestros, no CK; ver
            ' reference_facegen_ck_must_come_from_ba2.) Lo agregamos como bloque 0 para que
            ' GetRootNode()/CloneShape parenteen contra él. (Los shapes se re-cuelgan luego bajo un nodo
            ' BSFaceGenNiNodeSkinned — ver post-proceso más abajo.)
            ' GAME-AWARE shell. SSE y FO4 difieren en el FaceGeom root (medido byte-a-byte contra los BA2
            ' vanilla, verificado con --nifraw independiente de nifly):
            '   FO4  = NiVersion stream 130, root = NiNode name "" Flags 0x400E   (CK FO4 NUNCA usa BSFadeNode: 0/1094)
            '   SSE  = NiVersion stream 100, root = BSFadeNode name "<localId>.NIF" Flags 0x000E  (SSE SIEMPRE BSFadeNode)
            ' Todo lo demas (clone de shapes, morph, skin/bounds copiados del source) es game-agnostico.
            If isSSEBake Then
                nif.Create(NiVersion.GetSSE(), withRootNode:=False)
                Dim faceRootSse As New NiflySharp.Blocks.BSFadeNode() With {
                    .Name = New NiflySharp.NiStringRef($"{PluginManager.ToFaceGenLocalFormID(npcFormID):X8}.NIF"),
                    .Flags_ui = &HEUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                nif.AddBlock(faceRootSse)
            Else
                nif.Create(NiVersion.GetFO4(), withRootNode:=False)
                Dim faceRoot As New NiflySharp.Blocks.NiNode() With {
                    .Name = New NiflySharp.NiStringRef(""),
                    .Flags_ui = &H400EUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                nif.AddBlock(faceRoot)
            End If
        Catch ex As Exception
            result.Summary = $"Failed to create FaceGen NIF shell: {ex.Message}"
            Return result
        End Try

        ' Preventive race-level eligibility gate (canonical FaceGen-Head flag, version-aware) — run
        ' BEFORE BuildAllowedShapeMap. A non-FaceGen race (dog/creature/robot/turret/feral ghoul/etc.)
        ' has no head/face to bake; without this gate a dog NPC carrying a stray human Teeth HDPT in
        ' PNAM resolves a non-empty hdptMap (passing the Count=0 guard below) yet every shape is dropped
        ' at clone time → an empty NIF gets written. RaceSupportsFaceGen reads RACE.DATA bit 0x2 and is
        ' the 0-exception discriminator. Uses the same race FormID source BuildAllowedShapeMap consumes
        ' (NPC_.RaceFormID; the LM overlay never rewrites the race).
        Dim gateRaceFormID As UInteger = If(npcData IsNot Nothing, npcData.RaceFormID, 0UI)
        If Not RaceUtil.RaceSupportsFaceGen(gateRaceFormID, pluginManager) Then
            result.Skipped = True
            result.Success = False
            result.Summary = "Race has no FaceGen (dog/creature/robot/feral ghoul/etc.) — skipped, no NIF."
            Return result
        End If

        ' Build the canonical HDPT chain for this NPC. Each entry has its MeshPath and (later)
        ' chargen TRI / FMRS info. This is the AUTHORITATIVE list — the .nif2 contains exactly
        ' the shapes that come out of these sources. Seeded from `state` (= overlaid npcData +
        ' ApplyRaceFallbacks), the SAME list the live render walks — so a modified chargen bakes
        ' the head parts the preview shows, not the raw record's.
        Dim hdptMap = BuildAllowedShapeMap(state, pluginManager)

        ' No FaceGen-eligible head parts (non-human race, robot, turret, creature, …) → nothing to
        ' bake. This is a SKIP, not a failure: don't write an empty NIF, and let the caller count it
        ' separately (batch summary / Save) instead of reporting a spurious "fail".
        If hdptMap Is Nothing OrElse hdptMap.Count = 0 Then
            result.Skipped = True
            result.Success = False
            result.Summary = "No FaceGen head parts for this NPC — skipped."
            Return result
        End If

        ' --- ITERATION 1: assemble shapes from source meshes per HDPT.
        '
        ' For each HDPT in the resolved chain, load HDPT.MeshPath from BA2/loose, take ALL
        ' shapes inside, and clone each one into the .nif2 shell. No name-matching with the
        ' baked NIF — the source NIFs decide what shapes exist. The same source NIF can be
        ' referenced by multiple HDPTs (e.g. FemaleEyes.nif appears in Eyes + Lashes/AO/Wet
        ' extras); a `loadedSources` cache prevents double-loading and a `clonedShapeNames`
        ' set prevents duplicate inserts.
        '
        ' What this iteration does NOT yet do (will appear as comparator deltas):
        '   - Apply chargen TRI vertex morphs (HDPT.ChargenMorphTriPath × NPC.MorphValues).
        '   - Bake FMRS bone deltas into the skin partition's bone bind transforms.
        '   - Merge face-skeleton bones into shapes that need them.
        ' Each subsequent iteration tackles one of these and the comparator measures progress.
        Dim loadedSources As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)
        Dim clonedShapeNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim hdptProcessed As Integer = 0
        Dim hdptSourceMissing As Integer = 0
        Dim hdptSourceLoadFail As Integer = 0
        Dim shapesCloned As Integer = 0
        Dim shapesSkippedDup As Integer = 0
        Dim shapesMorphed As Integer = 0
        ''' Shapes que tenían FBNS cargado pero NINGUNA shape del FBNS matcheó ⇒ se escriben SIN morphear.
        ''' Antes esto era un `Else` VACÍO: el batch reportaba éxito con shapes neutras. Ahora se cuenta y
        ''' se loguea (FormID + shape) para que la caída no sea silenciosa.
        Dim shapesFbnsUnmatched As Integer = 0

        ' --- ITERATION 3: build the FaceGen bake state (NPC overlay + race morph defs +
        ' FMRS pose). Single source of truth, consumed by FaceGenBuildPipeline.BakeShape per
        ' HDPT to produce v_baked = inv(Mtot_orig) × v_world.
        Dim regionsFile As FacialBoneRegionsFile = Nothing
        Dim probeNpcRaw = NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager)
        ' Raza EFECTIVA para las FacialBoneRegions: preferir el npcData overlaid (ya stampado con el
        ' override de raza del editor); probeNpcRaw es el parse crudo y tras un cambio de raza apuntaría
        ' a las regiones de la raza vieja.
        Dim probeRaceFid As UInteger = If(npcData IsNot Nothing AndAlso npcData.RaceFormID <> 0UI,
                                          npcData.RaceFormID, If(probeNpcRaw IsNot Nothing, probeNpcRaw.RaceFormID, 0UI))
        If probeNpcRaw IsNot Nothing AndAlso probeRaceFid <> 0UI Then
            Dim raceRec = pluginManager.GetRecord(probeRaceFid)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                Dim raceProbe = RecordParsers.ParseRACE(raceRec, pluginManager)
                ' BAKE == RENDER: resolve FMRI against the MERGED both-gender table, exactly like
                ' NpcMorphPoseResolver.BuildFaceBoneTransforms does for the live render. The two
                ' per-gender JSONs use disjoint ID namespaces, and 10 vanilla NPCs carry FMRI from
                ' the opposite gender's namespace — own-gender-only lookup silently baked a neutral
                ' head for them. See GetFacialBoneRegionsForFmriResolution for the measured evidence.
                regionsFile = NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution(raceProbe, probeNpcRaw.IsFemale)
            End If
        End If
        Dim bakeState As FaceGenBuildPipeline.BakeState =
            FaceGenBuildPipeline.BuildBakeState(npcFormID, pluginManager, appliedPresets, regionsFile)
        ' Names of every bone the actor's face + body skeletons expose. Used below
        ' to drop source shapes whose skin references a bone outside this set
        ' (CK-equivalent filter — see the call site for the rationale).
        Dim actorBoneNames As HashSet(Of String) = FaceGenBuildPipeline.GetActorBoneNames(bakeState)
        ' Desambiguación EN EL ORIGEN: GetActorBoneNames devuelve un set VACÍO si fallan las dos cargas
        ' de esqueleto (face y body). Con el set vacío el filtro de huesos desconocidos se auto-deshabilita
        ' aguas abajo, y hasta ahora lo hacía EN SILENCIO ⇒ "0 shapes dropeados" era ambiguo: no se podía
        ' distinguir "no había nada que dropear" de "el filtro ni siquiera pudo correr". Se loguea una vez
        ' por bake, acá, donde está la causa.
        If actorBoneNames Is Nothing OrElse actorBoneNames.Count = 0 Then
            Logger.LogLazy(Function() $"[FACEBAKE] unknown-bone filter DISABLED for npcFormID=0x{npcFormID:X8}: actor skeleton bone set is EMPTY (face+body skeleton load failed) — no source shape can be dropped in this bake")
        End If
        ' Skin-tint strength for SkinTint shapes (shaderType=5). It's the NPC's QNAM/SkinTone-layer
        ' alpha — a SEPARATE float from the skin tone RGB (NpcRecordOverlay derives both into
        ' TextureLightingFloats: RGB from the SkinTone palette, A from the layer opacity, else the
        ' raw QNAM float). The LIBRARY Save_To_Shader writes it to the shader (gated on SkinTint);
        ' we only hand it the value, because it's NPC-level (the BGSM has no skin-tint-alpha field) —
        ' exactly the split used for the skin tone COLOR. Use the float (not Color.A/255). 1.0 if absent.
        Dim skinTintAlpha As Single = 1.0F
        If bakeState IsNot Nothing AndAlso bakeState.NpcData IsNot Nothing AndAlso bakeState.NpcData.TextureLightingFloats IsNot Nothing Then
            skinTintAlpha = bakeState.NpcData.TextureLightingFloats.A
        End If
        ' Hair/helmet occlusion: SÍ se setea el flag hidden (NiAVObject Flags bit 0x1) en el bake.
        ' La regla canónica fue PROBADA determinista (0 excepciones / 958 hair parts) y depende sólo
        ' del record del NPC (su DEFAULT OUTFIT) + la mesh del part — no del item equipado en runtime:
        '   • HAIR (type 3 + hairlines de hair): hidden ⟺ el shape es biped {30}-without-{31}
        '     (ocupa HairTop 30 pero NO HairLong 31) Y la outfit cubre slot 31 (HairLong).
        '   • FacialHair (type 4 + hairlines de barba): hidden ⟺ la outfit cubre slot 32 / 48 / 49
        '     (FaceGenHead / Beard / Mouth).
        '   • Eyebrows (type 6): hidden ⟺ la outfit cubre slot 32 (FaceGenHead).
        '   • HeadRear (type 9): nunca.
        ' Si la outfit incluye una LVLI que podría ser la pieza de cabeza, NO se aplica oclusión de
        ' pelo/barba (el casco es randomizado → no determinista; se prefiere under-hide). Una ARMO
        ' determinista del outfit SÍ aplica aunque otros items sean LVLI. Esto iguala la regla que el
        ' render aplica en MainForm.SelectWinningCandidates. (Antes el bake dejaba todo visible "por
        ' no-determinismo"; eso fue refutado.)
        Dim outfitResolved = ResolveOutfitHeadwearSlots(npcData, pluginManager)
        Dim outfitSlots As UInteger = outfitResolved.Slots
        Dim outfitHasHairLong As Boolean = (outfitSlots And BakeSlotBitHairLong) <> 0UI
        Dim outfitHasFaceGenHead As Boolean = (outfitSlots And BakeSlotBitFaceGenHead) <> 0UI
        Dim outfitHasBeard As Boolean = (outfitSlots And BakeSlotBitBeard) <> 0UI
        Dim outfitHasMouth As Boolean = (outfitSlots And BakeSlotBitMouth) <> 0UI
        ' Captura para el sandbox FORZADO _2c (debug+sandbox): head shape + complexion/normal ORIGINALES (antes de
        ' que el pass normal mute los slots), para correr el replacer completo en cualquier NPC y salvar _2c.NIF.
        Dim sseForcedHead As INiShape = Nothing
        Dim sseForcedComplexion As String = Nothing, sseForcedNormal As String = Nothing, sseForcedDetail As String = Nothing
        For Each kv In hdptMap.OrderBy(Function(p) p.Value.Hdpt.PartType).ThenBy(Function(p) p.Key)
            Dim hdptName = kv.Key
            Dim hdpt = kv.Value.Hdpt
            Dim effectiveHeadPartType = kv.Value.EffectivePartType
            If String.IsNullOrEmpty(hdpt.MeshPath) Then
                hdptSourceMissing += 1
                Dim hnLog = hdptName
                Logger.LogLazy(Function() $"[FACEBAKE] HDPT '{hnLog}' has empty MeshPath; shape skipped")
                Continue For
            End If

            ' Source resolution: arrancamos SIEMPRE del original `<mesh>.nif`, NO del
            ' `<mesh>_facebones.nif`. El log three-way (BUILDCHARGEN-THREEWAY) confirmó
            ' empíricamente — 11/11 shapes en Alijo — que el bake de CK usa la bone palette
            ' del ORIGINAL, no la del _facebones. El _facebones agrega face bones al skin
            ' partition para soporte runtime de FMRS pero CK al bakear los descarta.
            ' (faceBonesKey solo se usa para diagnóstico three-way; no se carga para clonar.)
            Dim baseKey = MeshPathHelpers.NormalizeMeshKey(hdpt.MeshPath)
            Dim faceBonesKey = MeshPathHelpers.TryGetFaceBonesVariant(baseKey)
            Dim sourceKey = baseKey

            Dim srcNif As Nifcontent_Class_Manolo = Nothing
            If Not loadedSources.TryGetValue(sourceKey, srcNif) Then
                Dim srcBytes As Byte() = Nothing
                Try
                    srcBytes = FilesDictionary_class.GetBytes(sourceKey)
                Catch ex As Exception
                End Try
                If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
                    hdptSourceMissing += 1
                    Dim skLogMiss = sourceKey
                    Logger.LogLazy(Function() $"[FACEBAKE] source mesh not in FilesDictionary: '{skLogMiss}'; shape skipped")
                    Continue For
                End If
                srcNif = New Nifcontent_Class_Manolo()
                Try
                    srcNif.Load_Manolo(srcBytes)
                Catch ex As Exception
                    hdptSourceLoadFail += 1
                    Dim skLogFail = sourceKey
                    Logger.LogLazy(Function() $"[FACEBAKE] source NIF failed to load: '{skLogFail}': {ex.GetType().Name}: {ex.Message}; shape skipped")
                    Continue For
                End Try

                ' SSE: un head part en formato Skyrim LE (NiTriShape) debe salir BSDynamicTriShape, igual que
                ' el CK al hornear FaceGeom. NiflySharp.OptimizeFor(HeadPartsOnly:=True) hace exactamente esa
                ' conversión LE→SSE (NiTriShape → BSDynamicTriShape; ver NifFile.cs:1990-1993) — no-op si el
                ' source ya es SSE. El fix del VertexDesc dinámico vive en la raíz de NiflySharp
                ' (BSTriShape.SetVertexPositions con guard is-not-BSDynamicTriShape), así que no hace falta
                ' nada más acá. Sin esto, la app clonaba el NiTriShape LE crudo (pelo 'dawn' de Adisla) y el
                ' NIF divergía del CK: 3 shapes NiTriShape vs BSDynamicTriShape + ~880KB de más.
                Try
                    Dim srcVer = srcNif.Header?.Version
                    If isSSEBake AndAlso srcVer IsNot Nothing AndAlso srcVer.IsSK Then
                        srcNif.Optimize(Config_App.Game_Enum.Skyrim, headPartsOnly:=True)
                    End If
                Catch exOpt As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE] OptimizeFor LE->SSE head parts failed for '{sourceKey}': {exOpt.GetType().Name}: {exOpt.Message}")
                End Try

                loadedSources(sourceKey) = srcNif
            Else
            End If

            ' Clone every shape from this source into the shell. CloneShape_Original handles
            ' the cross-NIF clone semantics (bone-skin preservation, shader+texture-set deep
            ' clone, the NiSkinData VertexWeights aliasing workaround documented in
            ' NifContent_Class.vb:428-454).
            ' Naming rule (verified empirically against Alijo's baked CK FaceGen, 11/11 shapes):
            ' the baked NIF names each shape with its HDPT.EditorID — independent of the
            ' source NIF's internal shape name. So we pass `hdptName` (= HDPT EditorID) as
            ' the destination name to CloneShape_Original; the cloned shape lands with the
            ' correct name in one step, no post-rename needed.
            '
            ' If a single source NIF holds N>1 shapes (rare in vanilla face content), the
            ' first lands as `<EditorID>` and the rest as `<EditorID>_<sourceName>` so they
            ' don't collide in the destination. Vanilla face HDPTs we've seen have exactly
            ' one shape per source, so the simple branch is the dominant path.
            ' Cloth-physics bones (Hair_*_Cloth, Ponytail_*, SideTail_*) NO viven en skeleton.nif:
            ' están en el hkaSkeleton embebido en el BSClothExtraData de ESTE NIF de pelo. Sin esto,
            ' el filtro "unknown bone" de abajo descartaba la shape de pelo con física entera — CK la
            ' conserva. Ver reference_facegen_ck_must_come_from_ba2 (#pelo) y arch_cloth_bones_inject.
            Dim clothBoneNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim srcClothSkel = SkeletonClothOverlayHelper_Class.ParseClothSkeleton(srcNif)
                If srcClothSkel IsNot Nothing AndAlso srcClothSkel.Bones IsNot Nothing Then
                    For Each cb In srcClothSkel.Bones
                        If cb IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(cb.Name) Then clothBoneNames.Add(cb.Name.Trim())
                    Next
                End If
            Catch ex As Exception
            End Try

            Dim srcShapes = srcNif.GetShapes().ToList()
            Dim shapeIdxInThisHdpt As Integer = 0
            For Each srcShape In srcShapes
                Dim sourceName = If(srcShape.Name?.String, "")
                If sourceName = "" Then
                    Continue For
                End If
                Dim destName As String
                If srcShapes.Count = 1 OrElse shapeIdxInThisHdpt = 0 Then
                    destName = hdptName
                Else
                    destName = $"{hdptName}_{sourceName}"
                End If
                shapeIdxInThisHdpt += 1
                If clonedShapeNames.Contains(destName) Then
                    shapesSkippedDup += 1
                    Continue For
                End If
                ' CK-equivalent filter: drop a source shape whose skin references a bone
                ' that doesn't exist en el esqueleto del actor NI en los cloth-bones del NIF.
                ' Vanilla example: MaleEyesGhoul.nif holds two shapes — the iris (skins to
                ' 'Head') and a tear-duct sub-shape (skins to a custom 'GhoulTearDuct' bone
                ' that the actor's skeleton.nif does not expose). CK drops the second; we
                ' mirror that here so the bake doesn't carry an unrenderable extra shape.
                ' EXCEPCIÓN cloth-physics (#pelo): los cloth-bones (Hair_*_Cloth, Ponytail_*,
                ' SideTail_*) NO están en skeleton.nif pero SÍ en el BSClothExtraData del NIF
                ' (clothBoneNames) — son legítimos y CK los conserva, así que NO se descartan.
                Dim skipUnknownBone As String = Nothing
                ' Auto-deshabilitado si no pudimos cargar NINGÚN esqueleto del actor (set vacío ⇒ no hay
                ' contra qué contrastar). Ese caso YA se loguea una vez por bake en el ORIGEN, donde se
                ' resuelve actorBoneNames — acá no se repite por shape para no inundar el log.
                If actorBoneNames IsNot Nothing AndAlso actorBoneNames.Count > 0 Then
                    Try
                        Dim sti = TryCast(srcShape, NiflySharp.Blocks.BSTriShape)
                        If sti IsNot Nothing AndAlso sti.SkinInstanceRef IsNot Nothing AndAlso sti.SkinInstanceRef.Index >= 0 Then
                            Dim skBlk = srcNif.Blocks(sti.SkinInstanceRef.Index)
                            ' Nombres de hueso del skin, POR JUEGO. FO4 = BSSkin::Instance;
                            ' SSE = NiSkinInstance / BSDismemberSkinInstance (hereda de NiSkinInstance).
                            ' Antes este sitio SÓLO hacía TryCast a BSSkin_Instance ⇒ en un bake SSE el
                            ' cast daba Nothing siempre y el filtro era VACUO (no podía dispararse nunca).
                            ' Mismo camino que el barrido de referencedBones más abajo (ver :1004).
                            Dim skinBoneRefs As New List(Of Integer)
                            Dim srcSi = TryCast(skBlk, NiflySharp.Blocks.BSSkin_Instance)
                            If srcSi IsNot Nothing AndAlso srcSi.Bones IsNot Nothing Then
                                For bi As Integer = 0 To srcSi.Bones.Count - 1
                                    skinBoneRefs.Add(srcSi.Bones.GetBlockRef(bi))
                                Next
                            Else
                                Dim srcNiSi = TryCast(skBlk, NiflySharp.Blocks.NiSkinInstance)
                                If srcNiSi IsNot Nothing AndAlso srcNiSi.Bones IsNot Nothing Then
                                    For bi As Integer = 0 To srcNiSi.Bones.Count - 1
                                        skinBoneRefs.Add(srcNiSi.Bones.GetBlockRef(bi))
                                    Next
                                End If
                            End If
                            For Each bRef In skinBoneRefs
                                If bRef < 0 Then Continue For
                                Dim bNode = TryCast(srcNif.Blocks(bRef), NiflySharp.Blocks.NiNode)
                                Dim bName = bNode?.Name?.String
                                If Not String.IsNullOrEmpty(bName) AndAlso Not actorBoneNames.Contains(bName) _
                                   AndAlso Not clothBoneNames.Contains(bName) Then
                                    skipUnknownBone = bName
                                    Exit For
                                End If
                            Next
                        End If
                    Catch ex As Exception
                    End Try
                End If
                ' ⚠️ SSE = DETECT-ONLY A PROPÓSITO (no es un olvido; es la parte conservadora del fix).
                ' Hacer que el filtro ALCANCE el skin de SSE (arriba) y hacerlo DROPEAR en SSE son dos
                ' decisiones distintas. La segunda NO está respaldada por ninguna medición y el riesgo
                ' es asimétrico:
                '   • La razón de ser del filtro es FO4 y está sourced: MaleEyesGhoul.nif / 'GhoulTearDuct'.
                '     No existe ningún caso SSE documentado que el filtro deba arreglar.
                '   • El conjunto de shapes del bake SSE ya está MEDIDO contra el CK y cerrado: baseline
                '     7 categorías con `ausentes 5` y `count 6` → final ~0 defectos propios sobre 2800+
                '     NPCs / 7 categorías (--ssecomparebatch vs CK del BSA, sesión 2026-07-18; ver
                '     project_facegen_bake_closure_20260718). Ese ~0 se logró con el filtro VACUO en SSE.
                ' ⇒ un filtro que sólo QUITA shapes no tiene nada que arreglar en SSE y sólo puede
                ' reintroducir `ausentes`/`count`. Por eso en SSE se LOGUEA lo que se dropearía y NO se
                ' dropea. Si el barrido muestra 0 líneas [FACEBAKE-SSE-DRYRUN], habilitarlo es un no-op
                ' seguro; si muestra alguna, hay que justificar shape por shape ANTES de tocar esto.
                If skipUnknownBone IsNot Nothing AndAlso isSSEBake Then
                    Dim hnDry = hdptName
                    Dim snDry = sourceName
                    Dim bnDry = skipUnknownBone
                    Logger.LogLazy(Function() $"[FACEBAKE-SSE-DRYRUN] would drop shape '{snDry}' from HDPT '{hnDry}': skins to bone '{bnDry}' not in actor skeleton nor cloth-bones — NOT dropped (SSE shape-set is measured at ~0 defects vs CK; drop not enabled without evidence)")
                    skipUnknownBone = Nothing
                End If
                If skipUnknownBone IsNot Nothing Then
                    Dim hnLog = hdptName
                    Dim snLog = sourceName
                    Dim bnLog = skipUnknownBone
                    Logger.LogLazy(Function() $"[FACEBAKE] dropping shape '{snLog}' from HDPT '{hnLog}': skins to bone '{bnLog}' not in actor skeleton")
                    Continue For
                End If
                Try
                    Dim cloned = nif.CloneShape_Original(srcShape, destName, srcNif)
                    If cloned IsNot Nothing Then
                        clonedShapeNames.Add(destName)
                        shapesCloned += 1

                        ' Oclusión de headwear (regla verificada, 0 excepciones / 958 hair parts).
                        ' Por tipo efectivo del HDPT (Misc bajo hair → Hair=3, bajo barba → FacialHair=4):
                        '   • Hair: hidden ⟺ shape biped {30}-without-{31} Y outfit cubre slot 31 HairLong.
                        '   • FacialHair: hidden ⟺ outfit cubre slot 32 / 48 / 49.
                        '   • Eyebrows(6): hidden ⟺ outfit cubre slot 32.
                        '   • HeadRear(9): nunca.
                        ' outfitSlots = unión de slots de ARMO DETERMINÍSTICOS del outfit (LVLI no aporta
                        ' slots). Por eso NO se gatea por outfitHasLVLI: un outfit pure-LVLI da slots=0 →
                        ' sin cobertura → under-hide solo; un casco ARMO fijo (p.ej. WinterHat slot 31)
                        ' ocluye igual aunque OTRO item sea LVLI (brazo). Caso Sully (001073FC) confirmado.
                        ' Hidden = setear NiAVObject Flags bit 0x1 sobre el 0xE (visible) del shape clonado.
                        ' ⛔⛔ SÓLO FO4. En SKYRIM el CK **NO HORNEA LA OCLUSIÓN** en el FaceGeom — la deja a RUNTIME
                        ' (el actor se pone y se saca el gorro). MEDIDO sobre los facegeom vanilla de SSE:
                        ' **0 de 20.611 shapes** con el bit 0x1 (16.155 en flags=14, 4.455 en 524302; ninguno hidden).
                        ' El único hidden que apareció en todo Skyrim era... un NIF nuestro.
                        ' Y además la regla de abajo usa SEMÁNTICA DE SLOTS DE FO4, que en Skyrim significa OTRA COSA:
                        '   slot 32 = FaceGenHead en FO4  ⇒  **en Skyrim es el CUERPO** (BipedSlots.vb:139: "Skyrim =
                        '   30/31/41/42/43 (NO slot 32=cuerpo)"). Con lo cual `outfitHasFaceGenHead` en SSE equivalía a
                        '   "¿lleva ropa?" ⇒ las CEJAS se ocultaban en TODO NPC vestido (medido: nuestras cejas salían
                        '   Flags=15 hidden vs 14 visible del CK, mismo NPC). Idem Beard(48)/Mouth(49).
                        ' ⇒ En SSE no se toca el flag: el shape queda visible (0xE) y el engine ocluye en runtime.
                        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then
                            Try
                                Dim occlude As Boolean = False
                                Select Case effectiveHeadPartType
                                    Case PartTypeHair
                                        occlude = outfitHasHairLong AndAlso ShapeBiped30Only(cloned)
                                    Case PartTypeFacialHair
                                        occlude = (outfitHasFaceGenHead OrElse outfitHasBeard OrElse outfitHasMouth)
                                    Case PartTypeEyebrows
                                        occlude = outfitHasFaceGenHead
                                        ' PartTypeHeadRear (9): nunca se ocluye.
                                End Select
                                If occlude Then
                                    ' INiShape expone Flags_ui (NiAVObject). El shape clonado trae 0xE
                                    ' (visible); OR 0x1 lo marca hidden, igual que el render.
                                    cloned.Flags_ui = cloned.Flags_ui Or &H1UI
                                End If
                            Catch ex As Exception
                            End Try
                        End If

                        ' Cloth-physics hair (#pelo): CK cuelga el BSClothExtraData (el hkaSkeleton
                        ' de los cloth-bones) de la SHAPE del pelo, no del root (audit byte-fidelity:
                        ' 256/256 NIFs FaceGen de CK lo traen en la shape; 0 en el root). CloneShape_Original
                        ' NO transfiere el cloth extradata → lo clonamos del NIF source y lo colgamos de
                        ' la shape clonada del pelo, replicando a CK. Idempotente: si la shape ya tiene
                        ' uno, no duplica. Los cloth-bone NiNodes los crea el clone cross-file de NiflySharp
                        ' (re-mapea los bones del skin por nombre) y el reparent loop los cuelga flat del root.
                        If clothBoneNames.Count > 0 Then
                            Try
                                nif.TransferShapeClothExtraDataFrom(srcNif, cloned)
                            Catch ex As Exception
                                Logger.LogLazy(Function() $"[FACEBAKE] cloth extradata transfer failed for '{destName}': {ex.GetType().Name}: {ex.Message}")
                            End Try
                        End If

                        ' (c) ECED y demás extradata de la shape: ya NO se transfiere acá. La preservación
                        ' del ExtraDataList de la shape source (incl. BSEyeCenterExtraData) se hace de forma
                        ' GENERAL dentro de CloneShape_Original (cross-file), así lo conservan también WM /
                        ' SplitShape. Ver NifContent_Class.CloneShape_Original.

                        ' CBBE source for the female rear head ships with a malformed SSFFile
                        ' ("...\FacePssf", no extension). CK blanks SSFFile when baking; clear
                        ' only this exact (shape name, value) pair so we don't touch anything
                        ' else.
                        Try
                            If destName = "FemaleHeadHumanRearTEMP" Then
                                Dim subIdx = TryCast(cloned, NiflySharp.Blocks.BSSubIndexTriShape)
                                If subIdx IsNot Nothing AndAlso Not IsNothing(subIdx.SegmentData) AndAlso subIdx.SegmentData.SSFFile IsNot Nothing Then
                                    Const MalformedFacePssf As String = "Meshes\Actors\Character\CharacterAssets\FacePssf"
                                    If subIdx.SegmentData.SSFFile.Content = MalformedFacePssf Then
                                        subIdx.SegmentData.SSFFile.Content = ""
                                    End If
                                End If
                            End If
                        Catch ex As Exception
                        End Try

                        ' Match CK's behaviour: in a baked FaceGen NIF the shader carries no
                        ' BGSM/BGEM external path — all material data lives inline in the shader
                        ' block. Cleared here so the comparator (and the engine at draw time)
                        ' read material from the embedded shader, not from a now-stale BGSM
                        ' lookup. Equivalent to what CK does at bake time.
                        Try
                            Dim shad = TryCast(nif.GetShader(cloned), NiflySharp.Blocks.BSShaderProperty)
                            If shad IsNot Nothing AndAlso shad.Name IsNot Nothing Then
                                shad.Name.String = ""
                            End If
                        Catch ex As Exception
                        End Try

                        ' Copy the render-resolved material into the cloned shape's inline
                        ' shader. The render has already chained TXST + MNAM-BGSM + per-NPC
                        ' tints + palette resolution to produce the FINAL textures and shader
                        ' params for this shape — we just transcribe the result into the .nif2
                        ' so it ships self-contained (no external BGSM lookup). Texture slots
                        ' + a curated set of non-texture fields the MAT-DIAG showed CK actually
                        ' bakes inline (NifShaderType, Hair, SkinTint, Glowmap, EnvironmentMapping,
                        ' Alpha, AlphaTest, AlphaTestRef, HairTintColor, SkinTintColor,
                        ' BaseColor, NonOccluder). AlphaBlendMode left as the source has it
                        ' (Unknown) per user instruction — CK's normalization to None is purely
                        ' cosmetic at this point.
                        ApplyRenderResolvedMaterialToShape(nif, cloned, srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides, skinTintAlpha)

                        ' --- FaceCustomization texture bake: only for the Face shape (PartType=1).
                        ' GL-readback the 3 GPU textures the FaceTintCompositor wrote (D/N/S),
                        ' encode each with the source-NIF's DXGI format (BC3/BC5/BC5 + mips —
                        ' verified empirically via DDSPROBE), write to <outputRoot>\Data\Textures\
                        ' Actors\Character\FaceCustomization\<plugin>\<formID>_*.dds2 and rewrite
                        ' the cloned shader's slot 0/1/7 to point to those paths. The .dds2
                        ' extension keeps us from clobbering the real CK FaceCustomization on
                        ' disk; for a real bake this would emit .dds and the engine would pick
                        ' those up directly.
                        ' Bake de texturas: corre con host (app) O headless-CPU (CLI: host=Nothing pero
                        ' WriteGPUSandboxOutput=False ⇒ needGl=False, sólo el compositor CPU, que no necesita
                        ' GL). El GL interno ya está gateado por needGl; ResolveHairPaletteTexture es null-safe.
                        If hdpt.PartType = PartTypeFace AndAlso state IsNot Nothing AndAlso BakeFaceTexturesEnabled Then
                            If isSSEBake Then
                                ' SSE bakes a single facetint _d DDS (CPU compose, no GL) to NIF slot 6, NOT
                                ' the FO4 FaceCustomization D/N/S. Uses the overlaid tints so an Edit Face tint
                                ' edit bakes WYSIWYG.
                                ' Captura para el _2c forzado (SOLO debug): head + complexion/normal ORIGINALES
                                ' ANTES de que el pass normal pueda mutar los slots (evita doble-pliegue). La captura
                                ' y el forzado son 100% CPU (fold+neutral+normal), NO tocan GL ⇒ gate = DebugMode a
                                ' secas (NO WriteGPUSandboxOutput: eso apagaba el _2c en el bake loose async).
                                If DebugMode Then
                                    Dim sp = GetSseHeadSlotPaths(nif, cloned)
                                    sseForcedHead = cloned : sseForcedComplexion = sp.Slot0 : sseForcedNormal = sp.Slot1 : sseForcedDetail = sp.Slot3
                                End If
                                WriteSseFacetintDds(nif, cloned, npcFormID, originPlugin, pluginManager, npcData, willBePacked, host:=host)
                                ' Bake RaceMenu FACE overlays into a per-NPC diffuse (slot 0). Gated + no-op for
                                ' vanilla NPCs (no face overlays) ⇒ the facetint-only path above is unchanged.
                                _sseFoldUsedSharedNeutralDetail = False
                                WriteSseFaceDiffuseWithOverlays(nif, cloned, npcFormID, originPlugin, pluginManager, npcData, appliedPresets, willBePacked, host:=host)
                                result.UsedSharedNeutralDetail = result.UsedSharedNeutralDetail OrElse _sseFoldUsedSharedNeutralDetail
                            ElseIf host IsNot Nothing OrElse Not WriteGPUSandboxOutput Then
                                BakeFaceTextures(nif, cloned, srcNif, srcShape,
                                                 hdpt, effectiveHeadPartType, applyMaterialOverrides,
                                                 npcFormID, originPlugin,
                                                 pluginManager, appliedPresets, host,
                                                 state, willBePacked, result,
                                                 lmSkinTemplateResolver)
                            End If
                        End If

                        ' MATERIAL DIAG moved to AFTER Save_As_Manolo (disk write). Comparing
                        ' the in-memory `nif` here is misleading because Save_To_Shader writes
                        ' don't fully reflect what the serializer emits to disk. The post-save
                        ' MAT-DIAG block reloads the .nif2 from disk and compares against the
                        ' CK reference NIF — that's the only honest "what's on disk vs CK"
                        ' comparison.

                        ' --- ITERATION 3: bake the shape. Load the matching FBNS source on
                        ' demand, hand it (and the cloned ORIG) to FaceGenBuildPipeline.BakeShape
                        ' which (a) computes v_world by skinning the FBNS shape with the FMRS
                        ' pose-applied face skel + body skel (= what the runtime renderer
                        ' produces), then (b) writes v_baked = inv(Mtot_orig) × v_world into
                        ' the cloned ORIG so its body-only skin partition lands each vertex at
                        ' the same world position the renderer's FBNS skin path would.
                        '
                        ' The FBNS NIF is loaded only when present (HDPTs without _facebones
                        ' variant fall back to the cloned ORIG-bind vertices, no further math).
                        ' Chargen TRI morphs are folded into v_world inside ComputeWorldVerticesForShape.
                        If bakeState IsNot Nothing AndAlso faceBonesKey <> "" Then
                            Dim fbnsNif As Nifcontent_Class_Manolo = Nothing
                            If Not loadedSources.TryGetValue(faceBonesKey, fbnsNif) Then
                                Dim fbnsBytes As Byte() = Nothing
                                Try
                                    fbnsBytes = FilesDictionary_class.GetBytes(faceBonesKey)
                                Catch ex As Exception
                                End Try
                                If fbnsBytes IsNot Nothing AndAlso fbnsBytes.Length > 0 Then
                                    fbnsNif = New Nifcontent_Class_Manolo()
                                    Try
                                        fbnsNif.Load_Manolo(fbnsBytes)
                                        loadedSources(faceBonesKey) = fbnsNif
                                    Catch ex As Exception
                                        fbnsNif = Nothing
                                    End Try
                                End If
                            End If
                            If fbnsNif IsNot Nothing Then
                                ' Match the FBNS shape to the ORIG source shape. Vanilla naming
                                ' convention (verified empirically for Alijo): ORIG='<base>:N',
                                ' FBNS='<base>_faceBones:N'. We try (in order):
                                '   (1) exact name match (modded NIFs sometimes share name);
                                '   (2) "<base>_faceBones:N" insertion;
                                '   (3) single-shape FBNS NIF → use it (dominant face HDPT path).
                                Dim fbnsShapes = fbnsNif.GetShapes().ToList()
                                Dim fbnsShape As INiShape = Nothing
                                For Each fs In fbnsShapes
                                    If String.Equals(If(fs.Name?.String, ""), sourceName, StringComparison.OrdinalIgnoreCase) Then
                                        fbnsShape = fs : Exit For
                                    End If
                                Next
                                If fbnsShape Is Nothing Then
                                    ' Tier 2, AHORA IGUAL AL CK. FUENTE: CreationKit.exe 0x14093C030 busca
                                    ' "_faceBones" (string @RVA 0x3017F30) como SUBSTRING CASE-INSENSITIVE en
                                    ' CUALQUIER POSICIÓN del nombre de la shape FBNS, y hace un splice de 10
                                    ' chars (constante @RVA 0x3B9DE50) para recuperar el nombre base; compara
                                    ' ESE resultado contra el nombre de la shape ORIG.
                                    ' Lo anterior CONSTRUÍA el nombre esperado insertando "_faceBones" justo
                                    ' antes del ":N" final — o sea, sólo reconocía UNA posición. Cualquier NIF
                                    ' cuyo sufijo no fuera exactamente ':N' (o que llevara el token en otro
                                    ' lado) no matcheaba, aunque el CK sí lo matchea.
                                    Const FaceBonesToken As String = "_faceBones"
                                    For Each fs In fbnsShapes
                                        Dim fsName As String = If(fs.Name?.String, "")
                                        Dim tokIdx = fsName.IndexOf(FaceBonesToken, StringComparison.OrdinalIgnoreCase)
                                        If tokIdx < 0 Then Continue For
                                        Dim spliced = fsName.Remove(tokIdx, FaceBonesToken.Length)
                                        If String.Equals(spliced, sourceName, StringComparison.OrdinalIgnoreCase) Then
                                            fbnsShape = fs : Exit For
                                        End If
                                    Next
                                End If
                                ' Tier 3 ("si el FBNS tiene una sola shape, usala") — SIN CONTRAPARTE EN EL CK:
                                ' el 0x14093C030 sólo hace el match por nombre de arriba, no tiene fallback por
                                ' cardinalidad. Sólo puede SOBRE-matchear (emparejar shapes que el CK dejaría sin
                                ' morphear). Medido: 0 de 501 casos vanilla lo usan ⇒ no aporta cobertura real.
                                ' Se DEJA por ahora para no cambiar dos cosas a la vez en la misma corrida, pero
                                ' ahora es visible: cuando dispara, se loguea como tier3 (ver abajo).
                                Dim usedTier3 As Boolean = False
                                If fbnsShape Is Nothing AndAlso fbnsShapes.Count = 1 Then
                                    fbnsShape = fbnsShapes(0)
                                    usedTier3 = True
                                End If
                                If fbnsShape IsNot Nothing Then
                                    If usedTier3 AndAlso Logger.Enabled Then
                                        Dim shNameT3 = sourceName
                                        Logger.LogLazy(Function() $"[FACEGEN-FBNS] tier3 (single-shape fallback, NO tiene contraparte en el CK) npc=0x{npcFormID:X8} shape='{shNameT3}' fbns='{faceBonesKey}'")
                                    End If
                                    Dim baked = FaceGenBuildPipeline.BakeShape(bakeState, nif, cloned, fbnsNif, fbnsShape, hdpt.ChargenMorphTriPath, srcNif:=srcNif, srcShape:=srcShape, raceMorphTriPath:=hdpt.RaceMorphTriPath)
                                    If baked Then
                                        shapesMorphed += 1
                                    End If
                                Else
                                    ' CAÍDA SILENCIOSA — ya no. El FBNS cargó pero ninguna de sus shapes matcheó,
                                    ' así que esta shape se escribe SIN morphear y el batch la contaba como éxito.
                                    ' Se contabiliza y se registra FormID + shape + los nombres candidatos.
                                    shapesFbnsUnmatched += 1
                                    If Logger.Enabled Then
                                        Dim shNameU = sourceName
                                        Dim fbKeyU = faceBonesKey
                                        Dim candNames = String.Join(",", fbnsShapes.Select(Function(f) If(f.Name?.String, "")))
                                        Logger.LogLazy(Function() $"[FACEGEN-FBNS] SIN MATCH — shape escrita SIN morphear. npc=0x{npcFormID:X8} shape='{shNameU}' fbns='{fbKeyU}' fbnsShapes=[{candNames}]")
                                    End If
                                End If
                            End If
                        ElseIf bakeState IsNot Nothing AndAlso isSSEBake Then
                            ' SSE has no `_faceBones` rig / FMRS / skin-rebind: the CK FaceGeom head is a PURE
                            ' per-vertex morph of the neutral mesh (measured: neutral-vs-CK RMS 0.275 collapses
                            ' to 0.046 once the NAM9/NAMA+race morph is applied). The FO4 BakeShape branch above
                            ' never runs (no FBNS), so without this the SSE head shipped NEUTRAL — that is why
                            ' the baked NIF diverged from the live render, which morphs via the same builder.
                            ' Apply that exact plan to the cloned shape in place (reuses ApplyChargenMorphsInPlace
                            ' → NpcMorphResolver.BuildFaceMorphPlanFromNam9 + MorphEngine; no second morph impl).
                            ' Pass THIS head-part's own mesh tri (MaleHead.tri / ...argonian / ElfHair08.tri, etc.)
                            ' so its SkinnyMorph (the actor-weight morph) is applied per-part and race-aware.
                            ' AUTHORITATIVE and ONLY source = HDPT NAM0=1 "Tri" (hdpt.TriPath): the CK applies the
                            ' mesh weight morph IFF the record declares it here. The NIF and its tri do NOT always
                            ' share a basename (Hair08.nif → Elf\Male\ElfHair08.tri), so the old ChangeExtension(MeshPath)
                            ' guess both MISSED the correct tri (elf/nord hair shipped un-weighted) AND, worse, wrongly
                            ' applied a same-named tri the CK ignores — e.g. HairMaleDarkElf02 has NO NAM0=1 yet
                            ' MaleDarkElfHair02.tri exists on disk; the CK leaves that hair un-weighted, so the guess
                            ' over-morphed it (+0.57 RMS vs CK). Dropping the guess makes both cases match CK: NAM0=1
                            ' parts get their SkinnyMorph, NAM0-less parts stay neutral. Verified vs vanilla SSE FaceGeom.
                            Dim hdptMeshTri As String = hdpt.TriPath
                            FaceGenBuildPipeline.ApplyChargenMorphsInPlace(nif, cloned, hdpt.ChargenMorphTriPath, hdpt.RaceMorphTriPath, bakeState, hdptMeshTri)
                            shapesMorphed += 1
                        End If
                    End If
                Catch ex As Exception
                End Try
            Next
            hdptProcessed += 1
        Next

        ' ✅ RESUELTO 2026-07-18 (RE del CK + verificación numérica) — este comentario decía que el diff de
        ' posiciones del pelo vs CK era NAM7 aplicado por "un hueso weight-ajustado" y que faltaba el mecanismo.
        ' Acertaba en NAM7 y ERRABA en el vehículo: NO es skinning ni surface-conform, es un MORPH plano.
        '   value = 1 - NAM7/100   (CK SSE 0x1418C32B0: GetWeight [TESNPC+0x204] · mulss 0.01 · subss desde 1.0)
        '   BSFaceGenNiNode::ApplyMorph(type=3 "Custom Morph", index=0, value)  [0x141D1D6B0]
        '   type 3 / index 0 == el morph llamado "SkinnyMorph"; deltas del .tri NAM0=1 del PROPIO head part.
        ' Se reparte a TODOS los hijos del BSFaceGenNiNode sin filtrar por tipo, y el apply de type3/index0 va
        ' por un camino especial [0x141D18540 → 0x141D18B10] que escribe el array de vértices BASE, que es lo
        ' que el writer termina volcando al archivo.
        ' VERIFICADO numéricamente sobre el pelo (fg_00013255 'HairMaleNord09' vs hair09.nif neutral):
        '   CK == neutral + (1-NAM7/100)·SkinnyMorph  →  residual max 0,00247 rms 0,0001, con NAM7=75,00 exacto.
        ' NUESTRO CÓDIGO YA LO HACE (NpcMorphResolver.vb, canal SkinnyMorph) ⇒ el pelo está CERRADO.
        ' ⚠️ ABIERTO y DISTINTO: las BARBAS (head parts que declaran NAM0=0/2) siguen con residual max ~0,067
        ' rms ~0,0096, y está PROBADO que ese residual NO es expresable como combinación lineal de sus 177
        ' morphs ni como transformación afín, y que es INDEPENDIENTE del NPC (correlación 0,978). Es otro
        ' mecanismo del CK, no un canal de morph que falte. NO intentar cerrarlo con morphs ni con heurísticas.

        ' ⭐⭐ El CK comparte un BSShaderTextureSet cuando coincide el MATERIAL, no sólo las rutas.
        ' El dueño del texture set es el BSLightingShaderMaterial, así que la caché del CK se indexa por el
        ' payload del material completo: los 8 paths + emissive color/multiple, alpha, refraction strength,
        ' glossiness, specular color y specular strength. Las shader FLAGS (SSPF1/SSPF2), el nombre y el
        ' controller viven en el shader property y NO entran en la clave.
        ' (El texture clamp mode también forma parte del material en el CK, pero NiflySharp no lo expone
        ' públicamente en BSLightingShaderProperty — sólo el campo protegido `_textureClampMode`. Omitirlo es
        ' seguro: la clave mínima validada 75/75 fue "paths + specularStrength", y ésta la contiene.)
        ' DERIVADO DE DATOS (2026-07-18, 75 FaceGeom del CK extraídos del BSA vanilla, exigiendo reproducir el
        ' grafo de sharing exacto — mismos bloques y mismas shapes agrupadas):
        '     sólo los 8 paths (la clave vieja) ....... 47/75
        '     paths + payload de material ............. 75/75
        '     paths + material + SSPF1/SSPF2 .......... 36/75   (⇒ las flags NO entran)
        ' Y de los 28 pares que el CK dejó SEPARADOS pese a tener los 8 paths idénticos, 28/28 difieren
        ' EXCLUSIVAMENTE en specularStrength (caso canónico: hair09.nif 2,51 vs hairline09.nif 1,82 sobre la
        ' misma HairLong.dds ⇒ el CK escribe 2 texsets, la clave vieja escribía 1).
        ' ⛔ CORRIGE la premisa anterior ("8 texturas idénticas", inferida del ÚNICO caso Narri): Narri es
        ' justamente el subconjunto donde specularStrength coincide, por eso la generalización pasó
        ' desapercibida. MEDIDO vanilla limpio: 1036 NPCs (41%) con un BSShaderTextureSet de menos, tasa
        ' coherente con el 37% de fallo de la muestra. Ver feedback_ch_engine_source (el tamaño del diff no
        ' valida una regla) y arch_facegen_bake_rules.
        Try
            Dim seenTexset As New Dictionary(Of String, Integer)()
            For Each sh In nif.NifShapes.ToList()
                Dim lsp = TryCast(nif.GetShader(sh), NiflySharp.Blocks.BSLightingShaderProperty)
                If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then Continue For
                Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
                If ts Is Nothing OrElse ts.Textures Is Nothing Then Continue For
                ' ⭐ HairTintColor va en la clave: es la cola específica del shader type Hair Tint y forma parte
                ' del material. MEDIDO sobre los pares que el CK dejó SEPARADOS teniendo los 8 paths Y el resto
                ' del material idénticos (9 NPCs argonianos, ej. 0001412E 'HairArgonianMale07' vs
                ' 'HairArgonianMale07Hairline'): el ÚNICO campo que difiere es HairTintColor
                ' (0,290196/0,270588/0,380392 vs 0,211765/0,274510/0,376471). Sin él los mergeábamos.
                ' El formato "R" (round-trip) es exacto a nivel bit — necesario porque en esos mismos pares
                ' SpecularColor difiere en 1 ULP y cualquier redondeo los volvería a colapsar.
                Dim matKey = String.Join(";",
                    $"{lsp.EmissiveColor.R:R},{lsp.EmissiveColor.G:R},{lsp.EmissiveColor.B:R}",
                    $"{lsp.EmissiveMultiple:R}",
                    $"{lsp.Alpha:R}",
                    $"{lsp.RefractionStrength:R}",
                    $"{lsp.Glossiness:R}",
                    $"{lsp.SpecularColor.R:R},{lsp.SpecularColor.G:R},{lsp.SpecularColor.B:R}",
                    $"{lsp.SpecularStrength:R}",
                    $"{lsp.HairTintColor.R:R},{lsp.HairTintColor.G:R},{lsp.HairTintColor.B:R}")
                Dim key = String.Join("|", ts.Textures.Select(Function(t) If(t?.Content, "").ToLowerInvariant())) & "||" & matKey
                Dim canonIdx As Integer
                If seenTexset.TryGetValue(key, canonIdx) Then
                    lsp.TextureSetRef = New NiflySharp.NiBlockRef(Of NiflySharp.Blocks.BSShaderTextureSet) With {.Index = canonIdx}
                Else
                    seenTexset(key) = lsp.TextureSetRef.Index
                End If
            Next
        Catch ex As Exception
        End Try

        ' Drop any blocks left orphan after the strip+clone passes (e.g. the baked shell's
        ' shader properties / texture sets that were rooted only by the now-removed shapes).
        Try
            nif.RemoveUnreferencedBlocks()
        Catch ex As Exception
        End Try

        ' --- FaceGen shell parity (Fase 1): los shapes deben colgar de un NiNode
        ' 'BSFaceGenNiNodeSkinned' (Flags 0x0E=14, identidad — verificado con byte-compare vs BA2,
        ' 88/88; antes poníamos 0x2000000E con el bit 0x20000000 de más, gemelo del bug del root #1),
        ' NO directo del root. El root ya es
        ' NiNode "" (creado arriba; ver #1 2026-06-13). Sin esta capa el FaceGen LOOSE no renderiza la
        ' cabeza (el engine FaceGen exige la geometría skinneada bajo ese nodo). Los huesos (NiNode)
        ' quedan como hijos directos del root, igual que CK.
        ' Corre DESPUÉS de RemoveUnreferencedBlocks para operar sobre índices de bloque ya finales.
        Try
            Dim faceGenRoot = nif.GetRootNode()
            If faceGenRoot IsNot Nothing AndAlso faceGenRoot.Children IsNot Nothing Then
                Dim skinnedNode As New NiflySharp.Blocks.NiNode() With {
                    .Name = New NiflySharp.NiStringRef("BSFaceGenNiNodeSkinned"),
                    .Flags_ui = &HEUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                Dim skinnedIdx = nif.AddBlock(skinnedNode)

                Dim boneChildIdx As New List(Of Integer)
                Dim shapeChildIdx As New List(Of Integer)
                ' Race height (RACE.DATA Female/MaleHeight, ya parseado en bakeState.Race). CK escala
                ' las TRANSLATIONS de los nodos de hueso por este factor (female ≈ 0.98). Solo a los
                ' nodos de referencia: la geometría queda ×1.0 (la escala real la aplica el motor al
                ' actor en runtime; hornearla en la malla la dejaría doble-escalada). Verificado vs CK
                ' 2026-05-25: nodos female = base × 0.98, geo ×1.0, bind ×1.0.
                Dim raceHeight As Single = 1.0F
                If bakeState IsNot Nothing AndAlso bakeState.Race IsNot Nothing Then
                    Dim rh = If(bakeState.IsFemale, bakeState.Race.FemaleHeight, bakeState.Race.MaleHeight)
                    If rh > 0.0F Then raceHeight = rh
                End If

                ' Build the set of bone block indices that SOME BSSkin::Instance references
                ' (either as .Bones[i] or as .SkeletonRoot). Used below to drop bone NiNodes
                ' that ended up orphaned after the strip+clone passes (e.g. MaleEyes.nif's
                ' look-at dummy 'EyeLeftDummy001', or 'GhoulTearDuct' after we dropped its
                ' shape via the unknown-bone filter). Mirrors CK behaviour.
                Dim referencedBones As New HashSet(Of Integer)
                For Each anyBlk In nif.Blocks
                    Dim si = TryCast(anyBlk, NiflySharp.Blocks.BSSkin_Instance)
                    If si IsNot Nothing Then
                        If si.Bones IsNot Nothing Then
                            For bi As Integer = 0 To si.Bones.Count - 1
                                Dim bRef = si.Bones.GetBlockRef(bi)
                                If bRef >= 0 Then referencedBones.Add(bRef)
                            Next
                        End If
                        If si.SkeletonRoot IsNot Nothing AndAlso si.SkeletonRoot.Index >= 0 Then
                            referencedBones.Add(si.SkeletonRoot.Index)
                        End If
                    End If
                    ' SSE: skin es NiSkinInstance / BSDismemberSkinInstance (hereda de NiSkinInstance),
                    ' no BSSkin::Instance. Sin esto referencedBones queda vacio y el guard dropea todos
                    ' los huesos. Bones = NiBlockPtrArray<NiNode>, SkeletonRoot = NiBlockPtr<NiNode>.
                    Dim niSi = TryCast(anyBlk, NiflySharp.Blocks.NiSkinInstance)
                    If niSi IsNot Nothing Then
                        If niSi.Bones IsNot Nothing Then
                            For bi As Integer = 0 To niSi.Bones.Count - 1
                                Dim bRef = niSi.Bones.GetBlockRef(bi)
                                If bRef >= 0 Then referencedBones.Add(bRef)
                            Next
                        End If
                        If niSi.SkeletonRoot IsNot Nothing AndAlso niSi.SkeletonRoot.Index >= 0 Then referencedBones.Add(niSi.SkeletonRoot.Index)
                    End If
                Next

                Dim droppedOrphanBones As Integer = 0
                For Each childIdx In faceGenRoot.Children.Indices.ToList()
                    Dim childBlk = nif.GetBlock(childIdx)
                    If TypeOf childBlk Is INiShape Then
                        shapeChildIdx.Add(childIdx)
                        Dim triShape = TryCast(childBlk, NiflySharp.Blocks.BSTriShape)
                        If triShape IsNot Nothing Then
                            ' BoundingSphere: FO4 CK deja la esfera en (0,0,0,0) (el engine la computa del
                            ' skinned desde los huesos). SSE CK, en cambio, COPIA el bounds real del head base
                            ' (verificado --ckdelta: CK==base, non-zero). Game-aware: solo FO4 pone cero.
                            If Not isSSEBake Then
                                triShape.Bounds = New NiflySharp.Structs.BoundingSphere(System.Numerics.Vector3.Zero, 0.0F)
                            End If
                            ' skin.SkeletonRoot → BSFaceGenNiNodeSkinned. FO4 = BSSkin::Instance;
                            ' SSE = NiSkinInstance / BSDismemberSkinInstance (hereda). Game-aware.
                            Dim skinRef = triShape.SkinInstanceRef
                            If skinRef IsNot Nothing AndAlso skinRef.Index >= 0 AndAlso skinRef.Index < nif.Blocks.Count Then
                                Dim skBlk = nif.Blocks(skinRef.Index)
                                Dim si = TryCast(skBlk, NiflySharp.Blocks.BSSkin_Instance)
                                If si IsNot Nothing Then
                                    si.SkeletonRoot = New NiflySharp.NiBlockPtr(Of NiflySharp.Blocks.NiAVObject)(skinnedIdx)
                                Else
                                    Dim niSi = TryCast(skBlk, NiflySharp.Blocks.NiSkinInstance)
                                    If niSi IsNot Nothing Then
                                        niSi.SkeletonRoot = New NiflySharp.NiBlockPtr(Of NiflySharp.Blocks.NiNode)(skinnedIdx)
                                    End If
                                End If
                            End If
                        End If
                    Else
                        ' Bone node (flat child of root). Paridad CK:
                        '  - race height: escalar SOLO la translation del nodo por raceHeight (ver arriba).
                        '    Geometría y bind intactos.
                        ' #2 (2026-06-13): NO renombrar "HEAD"→"Head". CK MANTIENE "HEAD" (mayúscula) en el
                        ' nodo y en BSSkin::Instance.bones — verificado con NiflySharp contra los FaceGen del
                        ' BA2 (47/47 = "HEAD"). La nota previa "CK normaliza a Head, igualar por paridad byte"
                        ' era FALSA (medida contra refs contaminadas; ver reference_facegen_ck_must_come_from_ba2).
                        ' La fuente del esqueleto ya es "HEAD" = CK → lo dejamos intacto. El skin referencia el
                        ' nodo por puntero, así que con NO renombrar quedan iguales el nombre del nodo Y la ref.
                        Dim boneNode = TryCast(childBlk, NiflySharp.Blocks.NiNode)
                        ' Orphan-bone guard: drop this bone NiNode from root.Children iff
                        '   - no BSSkin::Instance references it (Bones[] or SkeletonRoot)
                        '   - it has no children of its own (no subtree depends on it)
                        ' Conservative: any extra reference and we keep it. The post-reparent
                        ' RemoveUnreferencedBlocks call (below) actually evicts the block.
                        If boneNode IsNot Nothing _
                           AndAlso Not referencedBones.Contains(childIdx) _
                           AndAlso (boneNode.Children Is Nothing OrElse boneNode.Children.Count = 0) Then
                            Dim bNameLog = If(boneNode.Name?.String, "")
                            Logger.LogLazy(Function() $"[FACEBAKE] dropping orphan bone NiNode('{bNameLog}'): no skin references it, no children")
                            droppedOrphanBones += 1
                            Continue For
                        End If
                        boneChildIdx.Add(childIdx)
                        If boneNode IsNot Nothing Then
                            If raceHeight <> 1.0F Then
                                boneNode.Translation = boneNode.Translation * raceHeight
                            End If
                        End If
                    End If
                Next

                ' root.Children = huesos + BSFaceGenNiNodeSkinned ; skinnedNode.Children = los shapes
                boneChildIdx.Add(skinnedIdx)
                faceGenRoot.Children.SetIndices(boneChildIdx)
                skinnedNode.Children.SetIndices(shapeChildIdx)
                Logger.LogLazy(Function() $"[FACEBAKE] reparent OK: {shapeChildIdx.Count} shapes bajo BSFaceGenNiNodeSkinned, {boneChildIdx.Count - 1} huesos en root, {droppedOrphanBones} huesos huerfanos descartados")
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE] reparent BSFaceGenNiNodeSkinned FAILED: {ex.GetType().Name}: {ex.Message}")
        End Try

        ' Second pass to evict the now-unreferenced orphan bone blocks (the loop above
        ' only removed them from root.Children; they still sit in nif.Blocks). This is
        ' the same idempotent helper called pre-reparent.
        Try
            nif.RemoveUnreferencedBlocks()
        Catch ex As Exception
        End Try

        ' SSE HDT-SMP: el vínculo físico del pelo —NiStringExtraData "HDT Skinned Mesh Physics Object",
        ' cuyo StringData es la ruta al XML de física— cuelga del ROOT del NIF fuente, no de la shape. El
        ' shell se reconstruye desde cero, así que CloneShape_Original (que solo preserva el extradata de la
        ' SHAPE) no lo trae y el motor nunca carga el XML → el pelo pierde la física SMP. Lo re-emitimos en
        ' el root horneado desde cada parte fuente que lo traiga; el helper es idempotente (pelo + hairline
        ' apuntan al mismo XML → se agrega una sola vez) y filtra por nombre (no toca BODYTRI). El nombre de
        ' shape ya coincide con el tag del XML porque el mod nombra el HDPT.EditorID == shape == per-vertex-shape
        ' (p.ej. KSSMP_Amor) y el bake renombra a EditorID. El XML NO se copia: ruta fija ya instalada. Solo
        ' SSE (FO4 no usa HDT-SMP; el helper es no-op si el source no trae el bloque). Corre tras el
        ' RemoveUnreferencedBlocks final, con el root ya finalizado, justo antes de guardar.
        If isSSEBake Then
            For Each srcNifForSmp In loadedSources.Values
                Try
                    nif.TransferRootSmpExtraDataFrom(srcNifForSmp)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE] SMP root extradata transfer failed: {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
        End If

        result.ShapesKept = shapesCloned
        result.ShapesDropped = 0

        ' Output path:
        '   DebugMode=False (default): <formID>.nif → pisa el CK bake; engine usa este al cargar.
        '   DebugMode=True: <formID>_2.nif → sandbox al lado del CK bake, sin pisar; engine
        '                   sigue usando el CK; el comparator diff-ea against CK BA2 baseline.
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Dim dataPathForNif = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPathForNif) Then
            result.Summary = "DataPath unset; cannot write .nif"
            Return result
        End If
        ' Extension uppercase ".NIF" to match CK vanilla exactly (CK writes <FormID>.NIF). Cosmetic
        ' on Windows (case-insensitive FS) but removes it as a variable while we chase the loose bug.
        Dim nifFileName = If(DebugMode, $"{formIdLow:X8}_2.NIF", $"{formIdLow:X8}.NIF")
        Dim outAbs = Path.Combine(dataPathForNif,
                                  "Meshes", "Actors", "Character", "FaceGenData", "FaceGeom",
                                  originPlugin, nifFileName)
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(outAbs))
            nif.Save_As_Manolo(outAbs, Overwrite:=True)
        Catch ex As Exception
            result.Summary = $"Failed to write {nifFileName}: {ex.Message}"
            Return result
        End Try

        ' === SANDBOX FORZADO _2c (SSE, SOLO debug) ===: tras el _2.NIF, forzar el replacer COMPLETO _d/_n
        ' (pliegue + neutralizar slot6 + normal) AUNQUE el NPC no tenga tints/overlays, sobre el complexion/normal
        ' ORIGINALES (capturados antes del pass normal ⇒ sin doble-pliegue), y salvar un FaceGeom _2c.NIF paralelo.
        ' Nunca en release (gate DebugMode). 100% CPU ⇒ NO exige WriteGPUSandboxOutput (antes lo exigía y el _2c
        ' desaparecía en el bake loose async). No toca el _2/_2b.
        If isSSEBake AndAlso DebugMode AndAlso sseForcedHead IsNot Nothing Then
            Try
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ENTER: complexion='{sseForcedComplexion}' normal='{sseForcedNormal}'")
                WriteSseFaceDiffuseWithOverlays(nif, sseForcedHead, npcFormID, originPlugin, pluginManager, npcData,
                                                appliedPresets, willBePacked:=False, forcedSuffix:="_2c",
                                                complexionPathOverride:=sseForcedComplexion, normalPathOverride:=sseForcedNormal,
                                                detailPathOverride:=sseForcedDetail)
                Dim nif2c = Path.Combine(dataPathForNif, "Meshes", "Actors", "Character", "FaceGenData", "FaceGeom",
                                         originPlugin, $"{formIdLow:X8}_2c.NIF")
                nif.Save_As_Manolo(nif2c, Overwrite:=True)
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] forced replacer sandbox -> {formIdLow:X8}_2c.NIF (+ _2c textures)")

                ' _2d = MISMO pliegue pero desde GPU (complexion × fgTint por el shader), para confirmar CPU(_2c)==GPU(_2d).
                ' Requiere host GL (solo app). Usa el complexion ORIGINAL capturado (= el que pliega el _2c) + las capas
                ' de tint del NPC. Es puro GPU (recompone el facetint + pliega en GPU), no copia el _2c CPU.
                If host IsNot Nothing AndAlso WriteGPUSandboxOutput Then
                    Dim npcRec2d = pluginManager.GetRecord(npcFormID)
                    Dim raceFid2d As UInteger = If(npcData IsNot Nothing, npcData.RaceFormID, 0UI)
                    Dim race2d As RACE_Data = Nothing
                    If npcRec2d IsNot Nothing AndAlso raceFid2d <> 0UI Then
                        Dim rr2d = pluginManager.GetRecord(raceFid2d)
                        If rr2d IsNot Nothing AndAlso rr2d.Header.Signature = "RACE" Then race2d = RecordParsers.ParseRACE(rr2d, pluginManager)
                    End If
                    Dim cplx = If(Not String.IsNullOrEmpty(sseForcedComplexion), sseForcedComplexion, GetSseHeadSlotPaths(nif, sseForcedHead).Slot0)
                    If npcRec2d IsNot Nothing AndAlso race2d IsNot Nothing AndAlso Not String.IsNullOrEmpty(cplx) Then
                        Dim glayers2d = SseFaceTintComposer.BuildLayerInputs(pluginManager, npcRec2d, race2d, raceFid2d, npcData.IsFemale,
                                                                            npcData.SseTintRaw, npcData.SseTintTexOverride)
                        If glayers2d IsNot Nothing AndAlso glayers2d.Count > 0 Then
                            ' Los MISMOS Face* overlays que el _2c/_2 componen en CPU, para que el _2d (GPU) sea el replacer
                            ' COMPLETO (fold + overlays) y matchee el facepaint. Preset del NPC (SseBodyOverlays).
                            Dim preset2d As LooksmenuLoader.LooksmenuPreset = Nothing
                            If appliedPresets IsNot Nothing Then appliedPresets.TryGetValue(npcFormID, preset2d)
                            ' ⛔ SOLO los overlays de CARA. Antes se pasaba `preset2d.SseBodyOverlays` ENTERO (cuerpo
                            ' incluido) y el layer-builder del GPU tampoco filtraba por nodo ⇒ los tatuajes de cuerpo
                            ' terminaban compuestos DENTRO de la cara. Predicado único: SseOverlayCompositor.IsFaceOverlay.
                            Dim overlays2d = SseOverlayCompositor.FaceOverlaysOnly(
                                If(preset2d IsNot Nothing, preset2d.SseBodyOverlays, Nothing))
                            WriteSseFacetint2dGpu(glayers2d, cplx, sseForcedDetail, overlays2d, formIdLow, originPlugin, host)
                        End If
                    End If
                End If
            Catch ex2c As Exception
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c sandbox failed: {ex2c.GetType().Name}: {ex2c.Message}")
            End Try
        End If

        result.Success = True
        result.OutputPath = outAbs
        result.Summary = $"Wrote {outAbs} ({result.ShapesKept} shapes from {hdptProcessed} HDPTs)"
        ' Caída silenciosa del match FBNS: shapes escritas SIN morphear. Va al Summary porque si sólo
        ' vive en el log, un batch con logging apagado reporta éxito con cabezas neutras.
        If shapesFbnsUnmatched > 0 Then
            result.Summary &= $" | WARNING: {shapesFbnsUnmatched} shape(s) sin match FBNS — escritas SIN morphear, ver [FACEGEN-FBNS] log"
        End If
        If hdptSourceMissing > 0 OrElse hdptSourceLoadFail > 0 Then
            result.Summary &= $" | WARNING: {hdptSourceMissing} source mesh(es) missing, {hdptSourceLoadFail} failed to load — see [FACEBAKE] log"
        End If

        ' DebugMode: run comparator against the CK BA2 baseline. The baseline path is the canonical
        ' one (no _2 suffix) — FilesDictionary resolves either a loose CK output or the vanilla
        ' BA2 entry. Skipped silently when bytes can't be obtained (NPC has no CK bake on disk).
        If DebugMode Then
            Try
                Dim bakedRelPath = ResolveFaceGenPath(npcFormID, pluginManager)
                If Not String.IsNullOrEmpty(bakedRelPath) Then
                    Dim bakedBytes = TryGetFilesDictionaryBytes(bakedRelPath)
                    If bakedBytes IsNot Nothing AndAlso bakedBytes.Length > 0 Then
                        Dim cmp = FaceGenComparator.Compare(outAbs, bakedBytes)
                        result.Summary &= $" | [DIFF] {cmp.Summary}"
                    Else
                        result.Summary &= $" | [DIFF] no CK baseline bytes for '{bakedRelPath}'"
                    End If
                Else
                    result.Summary &= " | [DIFF] could not resolve baked FaceGen path"
                End If
            Catch ex As Exception
                result.Summary &= $" | [DIFF] comparator threw: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        Return result
    End Function

    ''' <summary>Stage-1 dump (kept for diagnostics). Loads the FaceGen NIF and logs its
    ''' structure plus the HDPT records the NPC references. No file is written.</summary>

    ''' <summary>Compute the map of shape names → owning HDPT_Data, allowed in the baked output.
    ''' Seeds from <see cref="HeadPartResolver.MergeHeadPartsWithRaceDefaults"/> over
    ''' <paramref name="state"/>.HeadPartFormIDs (the OVERLAID + race-fallback list, identical to the
    ''' render's NpcMeshCollector.MergeHeadPartsWithRaceDefaults(state) call — NOT the raw NPC.PNAM),
    ''' so LooksMenu/Edit-Face head-part overrides bake exactly what the preview shows, and RACE-default
    ''' head parts like FemaleNeckGore (Meatcaps) are still included when PNAM doesn't list them.
    ''' Then expands recursively via
    ''' HDPT.ExtraPartFormIDs (HNAM) so technical sub-parts (Lashes/AO/Wet, Hairlines, etc.)
    ''' that vanilla HDPTs reference internally are also allowed. Match is case-insensitive.
    '''
    ''' Returning the HDPT_Data per name (not just the name) gives downstream the MeshPath,
    ''' RaceMorphTriPath, ChargenMorphTriPath, PartType, etc. needed to construct each shape
    ''' from records (iteration 1+ replaces "copy from baked" with "load from MeshPath").</summary>
    Private Function BuildAllowedShapeMap(state As MainForm.NPCVisualState,
                                          pluginManager As PluginManager) As Dictionary(Of String, HeadPartResolver.HdptChainEntry)
        Dim allowed As New Dictionary(Of String, HeadPartResolver.HdptChainEntry)(StringComparer.OrdinalIgnoreCase)
        If state Is Nothing Then Return allowed

        ' Seed from the SAME head-part list the live render walks: NpcMeshCollector does
        ' MergeHeadPartsWithRaceDefaults(state) over state.HeadPartFormIDs, where `state` is the
        ' overlaid npcData (ResolveOverlaidNpcData) + ApplyRaceFallbacks. Re-parsing the RAW NPC
        ' record here (the previous behaviour) ignored the LooksMenu/Edit-Face overlay, so any
        ' head-part change made before Save ESP — eye-colour Eyes HDPT (its TNAM is the eye
        ' diffuse), hair, brows, FacialHair, scars, LM SkinTemplate head/headRear swaps — baked
        ' vanilla while the material `state` honoured the overlay → bake diverged from the render.
        ' Same function + same inputs as the render = bake == render by construction.
        Dim mergedRoots = HeadPartResolver.MergeHeadPartsWithRaceDefaults(
            state.RaceFormID, state.IsFemale, state.HeadPartFormIDs, pluginManager)

        ' Walk the chain via the shared HNAM-expanding iterator (cycles guarded inside). Each
        ' entry carries the EFFECTIVE part type (Misc hairline under hair → Hair=3), the single
        ' source of truth shared with the render walk — so the bake colors sub-parts like the
        ' render does. First-write wins on EditorID collisions.
        For Each entry In HeadPartResolver.EnumerateHdptChain(mergedRoots, pluginManager)
            If String.IsNullOrEmpty(entry.Hdpt.EditorID) Then Continue For
            If Not allowed.ContainsKey(entry.Hdpt.EditorID) Then allowed(entry.Hdpt.EditorID) = entry
        Next

        Return allowed
    End Function

    ' Biped object slot bits used by head-part occlusion, aliased to the shared BipedSlots table
    ' (single source of truth) so a slot-value change there can't silently drift this bake path.
    Private Const BakeSlotBitHairLong As UInteger = BipedSlots.SlotBitHairLong
    Private Const BakeSlotBitFaceGenHead As UInteger = BipedSlots.SlotBitFaceGenHead
    Private Const BakeSlotBitBeard As UInteger = BipedSlots.SlotBitBeard
    Private Const BakeSlotBitMouth As UInteger = BipedSlots.SlotBitMouth

    ''' <summary>Slots de headwear cubiertos por la DEFAULT OUTFIT (OTFT) del NPC, de forma
    ''' DETERMINISTA. Devuelve (slots, hasLVLI):
    '''   • slots = unión, por cada ARMO directamente referenciada por el OTFT (resolviendo la cadena de
    '''     templates CNAM), de su footprint RACE-VALID: MainForm.ComputeArmoEffectiveSlotMaskCore —
    '''     EffectiveArmaSlotMask de cada ARMA que matchea la raza del NPC (RNAM + AdditionalRaces +
    '''     cadena RACE.RNAM Armor Race, vía NpcRenderContext.WalkArmorRaceChain) y tiene mesh de género,
    '''     ∪ los bits headwear del ARMO. MISMO filtro que el render (CollectArmoCandidates raceOk + gate
    '''     PA): antes se unían TODOS los ARMAs sin filtro y un ARMA de otra raza (o una pieza
    '''     ArmorTypePower, que lista HumanRace para el modelo de inventario) aportaba slots que el engine
    '''     nunca viste en este actor → el bake sobre-ocluía pelo/barba que el render muestra
    '''     (violación RENDER == BAKE). Piezas PA se dropean enteras salvo raza PA (misma regla del
    '''     render); un ARMO sin race-valid addons no aporta nada; un ARMO SIN armatures (fallback MOD2
    '''     del render, p.ej. robots) conserva su BOD2 propio. Aproximación heredada del Create tab
    '''     (documentada en el core): la unión corre sobre todos los addons race-valid sin resolver el
    '''     grupo INDX efectivo de FO4 — sin contexto de keywords (OTFT directo, no LVLI) el índice
    '''     efectivo sería BaseAddonIndex/0 igualmente y las variantes multi-INDX comparten footprint.
    '''   • hasLVLI = True si ALGÚN item directo del OTFT es una LVLI. Una LVLI randomiza la pieza
    '''     (casco) al equipar → NO determinista; el caller NO aplica oclusión de pelo/barba en ese
    '''     caso (prefiere under-hide). OJO: una ARMO determinista del outfit SÍ aporta sus slots
    '''     aunque OTROS items sean LVLI (p.ej. una LVLI de brazo no anula un casco ARMO fijo).
    ''' Sin RNG: sólo mira los items DIRECTOS del OTFT (no samplea ni expande LVLIs).</summary>
    Private Function ResolveOutfitHeadwearSlots(npcData As NPC_Data,
                                                pluginManager As PluginManager) As (Slots As UInteger, HasLVLI As Boolean)
        Dim slots As UInteger = 0UI
        Dim hasLVLI As Boolean = False
        If npcData Is Nothing OrElse npcData.DefaultOutfitFormID = 0UI OrElse pluginManager Is Nothing Then
            Return (slots, hasLVLI)
        End If

        Dim otftRec = pluginManager.GetRecord(npcData.DefaultOutfitFormID)
        If otftRec Is Nothing OrElse otftRec.Header.Signature <> "OTFT" Then Return (slots, hasLVLI)
        Dim otft = RecordParsers.ParseOTFT(otftRec, pluginManager)

        ' Resolvers RecordParsers-direct (el bake no tiene NpcRenderContext; el OTFT es chico, sin cache).
        ' La LÓGICA vive en los cores compartidos con el render — acá sólo se cablean los parsers.
        Dim parseRace = Function(rec As PluginRecord) RecordParsers.ParseRACE(rec, pluginManager)
        Dim parseArma = Function(fid As UInteger) As ARMA_Data
                            If fid = 0UI Then Return Nothing
                            Dim r = pluginManager.GetRecord(fid)
                            If r Is Nothing OrElse r.Header.Signature <> "ARMA" Then Return Nothing
                            Return RecordParsers.ParseARMA(r, pluginManager)
                        End Function
        Dim parseArmo = Function(fid As UInteger) As ARMO_Data
                            If fid = 0UI Then Return Nothing
                            Dim r = pluginManager.GetRecord(fid)
                            If r Is Nothing OrElse r.Header.Signature <> "ARMO" Then Return Nothing
                            Return RecordParsers.ParseARMO(r, pluginManager)
                        End Function
        Dim effectiveArmorRaces = NpcRenderContext.WalkArmorRaceChain(
            npcData.RaceFormID, Function(fid As UInteger) pluginManager.GetRecord(fid), parseRace)
        Dim paKywdFid As UInteger = MainForm.FindArmorTypePowerKeywordFid(pluginManager)
        Dim raceIsPa As Boolean = False
        If paKywdFid <> 0UI AndAlso npcData.RaceFormID <> 0UI Then
            Dim raceRec = pluginManager.GetRecord(npcData.RaceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                raceIsPa = MainForm.IsPowerArmorRaceData(parseRace(raceRec), paKywdFid, parseArmo)
            End If
        End If

        For Each itemFID In otft.ItemFormIDs
            If itemFID = 0UI Then Continue For
            Dim itemRec = pluginManager.GetRecord(itemFID)
            If itemRec Is Nothing Then Continue For
            Select Case itemRec.Header.Signature
                Case "LVLI"
                    ' Randomized head piece → non-deterministic. El caller saltea la oclusión.
                    hasLVLI = True
                Case "ARMO"
                    ' ARMO determinista: aporta sus slots race-valid (resolviendo template CNAM → terminal).
                    Dim terminalFID = OutfitResolver.ResolveTerminalArmorFormID(itemFID, pluginManager)
                    If terminalFID = 0UI Then Continue For
                    Dim armo = parseArmo(terminalFID)
                    If armo Is Nothing Then Continue For
                    ' Gate PA — misma regla que el render (CollectArmoCandidates) y el Create tab.
                    If MainForm.IsPowerArmorArmoData(armo, paKywdFid) AndAlso Not raceIsPa Then Continue For
                    If armo.ArmorAddons.Count = 0 Then
                        ' ARMO sin armatures (el render cae al mesh fallback ARMO.MOD2, p.ej. robots):
                        ' su BOD2 propio cuenta, como antes.
                        slots = slots Or armo.SlotMask
                        Continue For
                    End If
                    Dim fp = MainForm.ComputeArmoEffectiveSlotMaskCore(
                        armo, npcData.RaceFormID, npcData.IsFemale, parseArma, effectiveArmorRaces)
                    ' Valid=False ⇒ ningún addon race-valid con mesh ⇒ el engine no viste nada de este
                    ' ARMO en este actor ⇒ 0 slots (el fallback recordSlot/BOD2 del Mask es para el
                    ' display del Create tab, no para oclusión).
                    If fp.Valid Then slots = slots Or fp.Mask
            End Select
        Next

        Return (slots, hasLVLI)
    End Function

    ''' <summary>Lee si un shape clonado (BSSubIndexTriShape) es "biped30only": ocupa el biped
    ''' object 30 (HairTop) pero NO el 31 (HairLong). Misma definición y lectura de segmentos que
    ''' el render (BSTriShapeGeometry.GetBipedObjects). Devuelve False si el shape no es subindex o
    ''' no tiene segmentos.</summary>
    Private Function ShapeBiped30Only(shape As INiShape) As Boolean
        Dim subIdx = TryCast(shape, BSSubIndexTriShape)
        If subIdx Is Nothing Then Return False
        Dim biped = BSTriShapeGeometry.GetBipedObjects(subIdx)
        Return biped.Contains(30UI) AndAlso Not biped.Contains(31UI)
    End Function



    ''' <summary>Diagnostic-only dump per HDPT comparing the three relevant NIF sources side
    ''' by side: <mesh>.nif (original, body bones only), <mesh>_facebones.nif (face bones in
    ''' skin partition), and the corresponding shape inside the baked CK FaceGen. Each line
    ''' lists shape names, vertex/triangle counts, and the bone palette. Used to decide
    ''' empirically which source to bake from.</summary>

    ''' <summary>Dump four texture sources for a HDPT side-by-side, so we can see exactly
    ''' where each path comes from in the resolution chain CK uses when baking:
    '''   A) ORIG NIF (HDPT.MODL) shader-inline texture slots + related BGSM/BGEM (if any)
    '''   B) _facebones NIF (if exists) — same dump
    '''   C) TXST.MNAM-pointed material (BGSM/BGEM) texture slots
    '''   D) TXST own TX00..TX07 direct paths
    ''' Pure observation. The bake (CK and ours) blends these — comparing them lets us
    ''' decide which source CK trusts when conflicts exist (the EyesHazel TXST overlay
    ''' obsoletos case is the canonical example: TXST has legacy `EyeBrown_n.dds` while
    ''' the BGSM has modern `eyegloss_n.dds`).</summary>






    ''' <summary>Dump NPC.FTST (HeadTextureFormID) — the per-NPC TXST. The Face HDPT bake
    ''' typically picks slot mappings from here when the HDPT has no TNAM TXST.</summary>




    ''' <summary>Per-shape world-space comparison: render's v_world (already produced by
    ''' BakeShape) vs OUR .nif2 re-skinned at bind, vs CK-baked NIF re-skinned at bind. The
    ''' re-skin uses a bind-only resolver (NO FMRS) — exactly what the runtime does when it
    ''' renders a baked face NIF. Logs <c>[BUILDCHARGEN-RENDERDIFF]</c> per shape + aggregates.
    '''
    ''' Single-source-of-truth: every per-vertex skin operation funnels through SkinBakeMath +
    ''' the same FaceGenBuildPipeline.BuildBindResolver used inside BakeShape. No math is
    ''' duplicated here.</summary>

    ''' <summary>Diagnostic-only: read the DDS header of the FaceCustomization textures CK
    ''' references in the baked head shape and log their format/dims/mips. Runs once per
    ''' build. Used to settle empirically which DXGI format CK uses for the per-NPC face
    ''' bake textures (_d / _msn / _s) so we can match it when generating the bake from
    ''' the FaceTintCompositor's GPU output. Reads from FilesDictionary (BA2 / loose pool).</summary>

    ''' <summary>Diagnostic-only: open the vanilla head NIF, read its shader's BGSM, and
    ''' probe the DDS format of the source D/N/S textures. The face bake should write the
    ''' per-NPC FaceCustomization textures using the same DXGI format these source textures
    ''' use, so the engine's sampler / mip behavior stays consistent.</summary>

    ''' <summary>Parse a DDS file's header (DDS_HEADER + optional DDS_HEADER_DXT10) and return
    ''' a one-line description: format, dims, mips, file size. Reference: DDS file layout per
    ''' DirectXTex / Microsoft DDS spec — magic 'DDS '@0, DDS_HEADER@4 (124 bytes), DXT10 ext
    ''' @128 (20 bytes) when DDPF_FOURCC pixelformat carries 'DX10'.</summary>

    ''' <summary>Map common DXGI_FORMAT enum values to readable names. Only the ones FaceGen
    ''' textures might use are listed; unknown values fall through to a numeric label.</summary>

    ''' <summary>Find the render-side mesh whose shader carries the material we want to copy.
    ''' The render uses the source NIF's shape names: ORIG clones come in as "&lt;base&gt;:N",
    ''' face-bones-rigged clones as "&lt;base&gt;_faceBones:N". Returns Nothing when no mesh is
    ''' found (caller skips the copy and the cloned shape keeps its source-NIF shader inline).</summary>
    ''' <summary>Apply the render-resolved material to the cloned shape's inline shader.
    ''' Reuses <see cref="FO4UnifiedMaterial_Class.Save_To_Shader"/> verbatim — the same path
    ''' the editor uses to persist material edits to a NIF — so the .nif2 carries every shader
    ''' field the unified material exposes (texture slots, shader flags, Alpha/AlphaTest,
    ''' tints, BaseColor, etc.) inline. Cero duplicación con la librería.
    '''
    ''' Caller-known caveats (the render-resolved material gets these wrong vs CK and we'll
    ''' fix them in the source render path, not here):
    '''   - FemaleHeadHuman: render leaves SkinTint=True, CK bakes False; render keeps base
    '''     Diffuse/Normal/Spec, CK swaps to FaceCustomization\&lt;FormID&gt;_*.dds (FaceTintCompositor
    '''     bake, separate iteration).
    '''   - MouthShadowFemale: render Diffuse leaks basefemalehead_d.dds (resolver bug).
    '''   - HairFemale03_Hairline: render Hair=False/NifShaderType=Default, CK Hair=True/HairTint.
    '''   - HairFemale03 / NeckGore / Mouth: render applies tints (128/26 grey), CK leaves white
    '''     because the shader doesn't consume them.
    ''' AlphaBlendMode Unknown→None left as-is per user; investigating separately.</summary>
    ''' <summary>Resuelve el material FINAL de un head-part igual que la ruta de render: envuelve el
    ''' shape SOURCE como IRenderableShape, arma el MeshCandidate del HDPT (incluyendo HeadPartHdptFormID
    ''' que gatilla el clon vanilla-UV del head-rear ghoul en ApplyShapeMaterialOverrides) y corre el
    ''' MISMO delegate <paramref name="applyMaterialOverrides"/> (cadena
    ''' TXST/FTST + MNAM-BGSM + tints + palette) que usa el render. Devuelve el material con D/N/S ya
    ''' RESUELTOS por el FaceTextureSet del NPC — p.ej. para NPCs viejos el head OldHumanFemaleHead_d
    ''' pisa al BaseFemaleHead_d del material crudo del NIF — o Nothing si no hay resolver / falla el
    ''' wrap. Single source of truth: lo consumen <see cref="ApplyRenderResolvedMaterialToShape"/>
    ''' (transcribe el material al shader inline del .nif2) y <see cref="BakeFaceTextures"/> (base D/N/S
    ''' del FaceTintCompositor), de modo que render y bake parten de las MISMAS texturas resueltas.
    ''' GetRelatedMaterial construye un material fresco por llamada, así resolver dos veces (una por
    ''' consumidor) es idempotente y sin estado compartido.</summary>
    Private Function ResolveRenderResolvedShapeMaterial(srcNif As Nifcontent_Class_Manolo,
                                                        srcShape As INiShape,
                                                        hdpt As HDPT_Data,
                                                        effectiveHeadPartType As Integer,
                                                        state As MainForm.NPCVisualState,
                                                        pluginManager As PluginManager,
                                                        applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate) As FO4UnifiedMaterial_Class
        If applyMaterialOverrides Is Nothing Then Return Nothing
        Dim sourceName As String = If(srcShape?.Name?.String, "")

        ' Wrap the SOURCE shape (not any cloned one) as IRenderableShape so the resolver sees the
        ' original shader with its BGSM path intact (a cloned shape's shader gets Name="" inline and
        ' would lose every BGSM field outside the shader — Wrinkles texture, AO Normal slot, etc.).
        Dim wrapper As NifRenderableShape
        Try
            wrapper = New NifRenderableShape(srcNif, srcShape, 0)
        Catch ex As Exception
            Dim shapeNameL = sourceName
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] NifRenderableShape wrap shape='{shapeNameL}': {typeL}: {msgL}")
            Return Nothing
        End Try

        ' Build a minimal MeshCandidate from the HDPT in scope. For Build CharGen the candidate
        ' chain is straightforward (HDPT → Face/Eyes/Hair/etc.) so we don't need the full
        ' Outfit/LVLN/OBTS/OMOD resolution that the live render runs.
        ' HeadPartType = EFFECTIVE type (Misc hairline under hair → Hair=3) so the shared
        ' material resolver colors sub-parts like the render does (e.g. hair palette on the
        ' hairline). HeadPartTypeRaw keeps the HDPT's own type for any raw-type logic downstream.
        ' HeadPartHdptFormID drives MainForm's ghoul-female head-rear vanilla-UV clone gate inside
        ' ApplyShapeMaterialOverrides (the delegate below), so the BAKED NIF references the
        ' persistent vanilla-bytes clone (fixes in-game too). UsesBodyTexture stays the raw record
        ' value — the previous override-proxy forcing heuristic was removed (single source of truth
        ' now lives in MainForm.ApplyGhoulHeadRearClonedTextures).
        Dim candidate As New MainForm.MeshCandidate With {
            .Kind = MainForm.MeshCandidateKind.HeadPart,
            .HeadPartType = effectiveHeadPartType,
            .HeadPartTypeRaw = hdpt.PartType,
            .TextureSetFormID = hdpt.TextureSetFormID,
            .HeadPartHdptFormID = hdpt.FormID,
            .UsesBodyTexture = hdpt.UsesBodyTexture,
            .HeadPartColorFormID = hdpt.ColorFormID
        }
        ' UseSolidTint ya NO se asigna acá: es propiedad calculada sobre HeadPartColorFormID, con la MISMA
        ' definición medida (`CNAM <> 0`) que este sitio ya tenía. El render la construía distinto (flag DATA
        ' 0x10) ⇒ divergía. Ver MainForm.MeshCandidate.UseSolidTint.

        ' Run the same per-shape resolver the render uses. Mutates wrapper.ShapeMaterial in-place.
        Try
            applyMaterialOverrides(candidate, state, {DirectCast(wrapper, IRenderableShape)})
        Catch ex As Exception
            Dim shapeNameL = sourceName
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] applyMaterialOverrides shape='{shapeNameL}': {typeL}: {msgL}")
            Return Nothing
        End Try

        Return wrapper.ShapeMaterial?.material
    End Function

    Private Sub ApplyRenderResolvedMaterialToShape(nif As Nifcontent_Class_Manolo,
                                                    cloned As INiShape,
                                                    srcNif As Nifcontent_Class_Manolo,
                                                    srcShape As INiShape,
                                                    hdpt As HDPT_Data,
                                                    effectiveHeadPartType As Integer,
                                                    state As MainForm.NPCVisualState,
                                                    pluginManager As PluginManager,
                                                    applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                                    skinTintAlpha As Single)
        ' Resolve the FINAL material exactly like the render (TXST/FTST + MNAM-BGSM + tints + palette);
        ' shared with the FaceTint bake so both transcribe / composite the SAME resolved textures.
        Dim mat = ResolveRenderResolvedShapeMaterial(srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides)
        If mat Is Nothing Then
            Return
        End If

        ' POST-RESOLVER snapshot: same fields after the resolver ran. Diff against PRE shows
        ' which fields the resolver chain (TXST.MNAM swap, MSWP swap, tint colour overrides, etc.)
        ' actually mutated. Should match what gets serialized to disk by Save_To_Shader below.

        Dim shad = nif.GetShader(cloned)
        If shad Is Nothing Then
            Return
        End If

        Try
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            If bsls IsNot Nothing Then
                Dim bgsm = TryCast(mat.Underlying_Material, BGSM)
                If bgsm Is Nothing Then
                    Return
                End If
                ' TX05 (EnvMask) en NIF spec es dual-purpose: para shaders FaceTint el motor
                ' lo usa como Wrinkles. CK al bakear FaceGen escribe BGSM.WrinklesTexture en
                ' TX05 cuando el shader es FaceTint. Para todo lo demás, va EnvmapMaskTexture
                ' (NIF inline TX05 capturado en _EnvmapMaskPath; ver Deserialize sidecar JSON).
                Dim slot5Path As String
                If mat.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint AndAlso
                   Not String.IsNullOrEmpty(mat.WrinklesTexture) Then
                    slot5Path = mat.WrinklesTexture
                Else
                    slot5Path = mat.EnvmapMaskTexture
                End If
                ' Hand the library the per-NPC skin-tint strength (from the NPC's QNAM/SkinTone-layer
                ' alpha). Save_To_Shader writes it to shad.SkinTintAlpha (only when SkinTint) — the
                ' value is NPC-level, not a BGSM field, so the app provides it (same split as the skin
                ' tone COLOR which the resolver puts in HairTintColor and the library writes).
                mat.SkinTintAlpha = skinTintAlpha
                mat.Save_To_Shader(nif, cloned, bsls, mat.NifShaderType, slot5Path)
                ' CK al bakear el FaceGen deja shad.Name vacío en el shader inline (no
                ' linkea al BGSM external). Replicamos eso para que el .nif2 sea standalone
                ' (todos los datos del material viven embedded en el shader, sin depender
                ' del .bgsm en disco) y para que el comparator embedded-vs-embedded de
                ' GetRelatedMaterial caiga en la rama Create_From_Shader igual que el bake CK.
                If bsls.Name IsNot Nothing Then bsls.Name.String = ""

                ' CK convention for non-emissive shapes: when the BGSM source does NOT mark
                ' the material as emissive, CK still emits Emissive=True + EmittanceColor=(0,0,0)
                ' as a "field present, no light" centinela. Replicating lines up most baked
                ' shapes' Emit fields with CK output. Verified against Alijo (8 shapes) and Carol
                ' (NeckGore, EmitEnabled=True with rgb=(255,0,18) for ghoul gore must keep its
                ' real colour). Branch gated on source mat.EmitEnabled to preserve real emisives.
                If Not mat.EmitEnabled Then
                    bsls.Emissive = True
                    bsls.EmissiveColor = New NiflySharp.Structs.Color4(0.0F, 0.0F, 0.0F, 1.0F)
                End If
                bsls.RootMaterialName = ""
                ' CK sets Transform_Changed (F4SPF2 bit 7) on every baked FaceGen shape — universal
                ' across all 4 reference NPCs (human M/F, ghoul, supermutant), every single shape,
                ' no exception (measured 2026-05-25 via C:\temp\flagcmp.py). It's a housekeeping flag,
                ' not a material field (absent from the BGSM), so it belongs here with the other CK
                ' bake conventions, not in Save_To_Shader. shad.Type was set by Save_To_Shader above,
                ' so SetFlagSF2 resolves the FO4-specific bit correctly.
                ' ⭐ GAME-GATED: esta es una convención del bake CK de FO4 (Transform_Changed = F4SPF2 bit 7).
                ' En un shader SK ese MISMO bit 7 es Assume_Shadowmask → aplicarlo a SSE corrompía el shader
                ' (medido: head/mouth/hair ganaban 0x80 vs CK). CK SSE NO lo setea (SSPF2 del CK == source).
                If Not nif.Header.Version.IsSSE Then
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bsls, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
                End If
                ' ShaderType baked = FUNCIÓN DETERMINÍSTICA de los flags del material — PROBADO al 100%
                ' sobre el corpus FaceGen vanilla COMPLETO (1490 NIF, 14136 lighting shapes; cross-tab en
                ' c:\tmp\facegen_flag_to_type.txt: 8 combinaciones de flags, TODAS puras → un único tipo
                ' cada una). CK NO preserva el tipo de la fuente: lo DERIVA. Evidencia del "por qué":
                ' eyelashes.bgsm trae shader inline=EnvironmentMap, pero su flag Environment_Mapping=OFF,
                ' así que CK bakea las pestañas como Default (1381/1381). iris/Wet traen EnvironmentMap con
                ' el flag ON → lo conservan. Precedencia (load-bearing: Glow>Face; el resto nunca coexiste):
                '   Glowmap → GlowShader · Facegen → FaceTint · SkinTint → SkinTint · Hair → HairTint ·
                '   EnvironmentMapping → EnvironmentMap · else Default
                ' Esto SUBSUME y reemplaza las 3 reglas ad-hoc previas (clear EyeEnv + colapso de ojo +
                ' promoción de pelo): todas eran casos de esta única ley. El residual de la precedencia
                ' Face-primero (DLC04 ghoul brillante, Face+Glow → GlowShader) obligó a Glow por encima de
                ' Face. Eye_Environment_Mapping (bit 17) = 0 en 14136/14136 → clear incondicional.
                ' UN SOLO PATH: derivamos de los BOOLS del material. Tras Create_From_Shader (que setea
                ' los bools desde los flags) y eliminado el force, los bools YA son fieles a los flags
                ' baked: Save escribe Glow/Env/Hair desde estos mismos bools, y Face/Skin_Tint se preservan
                ' del shader fuente clonado que Create leyó → coinciden. (Antes leíamos bsls.ShaderFlags
                ' como workaround contra la contaminación del force, ya eliminado.) Precedencia PROBADA al
                ' 100% sobre el corpus FaceGen vanilla (cross-tab 14136 shapes, c:\tmp\facegen_flag_to_type.txt:
                ' 8 combinaciones puras). CK NO preserva el tipo de la fuente: lo DERIVA. eyelashes.bgsm trae
                ' inline=EnvironmentMap pero su flag Environment_Mapping=OFF → CK = Default (1381/1381);
                ' iris/Wet con el flag ON conservan EnvironmentMap. Load-bearing: Glow>Face (residual DLC04
                ' ghoul brillante Face+Glow→GlowShader); el resto no coexiste. Eye_Environment_Mapping
                ' (bit17) = 0 en 14136/14136 → clear incondicional. Bake-only: la Derive de la librería
                ' (editor) queda conservadora (meshes generales: bGlowmap/bEnvironmentMapping conviven con
                ' inline Default — contexto distinto).
                ' ⭐ GAME-GATED: derivar el ShaderType de los bools + clear de Eye_Environment_Mapping son
                ' convenciones del bake CK de FO4 (probadas sobre el corpus FaceGen FO4, 14136 shapes). En SSE
                ' NO aplican: CK SSE PRESERVA el shader type + flags del source (medido: EyesFemale=EyeEnvmap con
                ' Eye_Environment_Mapping ON, tanto en source como en CK). Derivar aquí colapsaba los ojos a
                ' Default y borraba el bit 17 → shader roto. Para SSE dejamos lo que Save_To_Shader escribió
                ' (type = mat.NifShaderType del source; flags = del source clonado), que coincide con CK.
                If Not nif.Header.Version.IsSSE Then
                    Dim bakedType As Enums.BSLightingShaderType
                    If mat.Glowmap Then
                        bakedType = Enums.BSLightingShaderType.GlowShader
                    ElseIf mat.Facegen Then
                        bakedType = Enums.BSLightingShaderType.FaceTint
                    ElseIf mat.SkinTint Then
                        bakedType = Enums.BSLightingShaderType.SkinTint
                    ElseIf mat.Hair Then
                        bakedType = Enums.BSLightingShaderType.HairTint
                    ElseIf mat.EnvironmentMapping Then
                        bakedType = Enums.BSLightingShaderType.EnvironmentMap
                    Else
                        bakedType = Enums.BSLightingShaderType.Default
                    End If
                    bsls.ShaderType = bakedType
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF1(bsls, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags1.Eye_Environment_Mapping), False)
                End If

            Else
                Dim bes = TryCast(shad, BSEffectShaderProperty)
                If bes Is Nothing Then
                    Return
                End If
                Dim bgem = TryCast(mat.Underlying_Material, BGEM)
                If bgem Is Nothing Then
                    Return
                End If
                mat.Save_To_Shader(nif, cloned, bes)
                If bes.Name IsNot Nothing Then bes.Name.String = ""
                ' Transform_Changed (F4SPF2 bit 7) — CK lo setea en TODO shape baked, también los
                ' effect shaders (AO/MouthShadow). El fix de lighting (bsls) lo cubría, pero esta
                ' rama (bes) se lo saltaba → AO/MouthShadow quedaban con bit 7 = 0 vs CK 1. Mismo
                ' tratamiento que bsls. shad.Type ya quedó seteado por Save_To_Shader arriba.
                ' ⭐ GAME-GATED (mismo guard que la rama bsls de arriba, :1610). NO es analogía: está
                ' VERIFICADO en el formato, no inferido del caso lighting. Dos hechos de fuente:
                '   1) nif.xml declara el bit 7 con SIGNIFICADO DISTINTO por juego —
                '      SkyrimShaderPropertyFlags2 bit 7 = Assume_Shadowmask (nif.xml:6424) vs
                '      Fallout4ShaderPropertyFlags2 bit 7 = Transform_Changed (nif.xml:6496).
                '   2) BSEffectShaderProperty NO tiene un enum de flags propio: declara sus campos
                '      `Shader Flags 1/2` con suffix SK = Skyrim*PropertyFlags* cuando la versión es
                '      < FO4, y con suffix FO4 = Fallout4*PropertyFlags* cuando es FO4 (nif.xml:6653-6656).
                '      Es EXACTAMENTE el mismo par de enums que usa BSLightingShaderProperty — ambos
                '      heredan de BSShaderProperty y difieren por VERSIÓN, no por tipo de bloque.
                ' ⇒ en un NIF SSE este SetFlagSF2 escribe Assume_Shadowmask sobre el effect shader,
                ' el mismo modo de corrupción que ya se midió y se gateó en la rama de lighting.
                ' Refuerzo mecánico: ShaderHelper.SetFlagSF2 despacha por `shader.Type` (ShaderGameType),
                ' NO por la clase del bloque (ShaderHelper.cs:257-298) — con Type=SK el valor numérico
                ' crudo (1<<7) cae en ShaderFlags_SSPF2 sin traducción alguna.
                If Not nif.Header.Version.IsSSE Then
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bes, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
                End If
            End If
        Catch ex As Exception
        End Try
    End Sub

    ''' <summary>Diagnostic-only: per cloned shape, log the material the render has resolved
    ''' (post-MainForm.ApplyShapeMaterialOverrides, with TXST + MNAM-BGSM + per-NPC tints +
    ''' palette already applied) side-by-side with the material the CK baked NIF has inline.
    ''' <summary>For the Face shape (PartType=1), bake the per-NPC FaceCustomization textures
    ''' (_d, _msn, _s) by GL-readback of the GPU textures the FaceTintCompositor wrote into
    ''' Textures_Dictionary, encoding each with the source-NIF's DXGI format (BC3 for diffuse,
    ''' BC5 for normal+spec — confirmed empirically by DDSPROBE), and writing to .dds2 under
    ''' &lt;outputRoot&gt;\Data\Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\&lt;FormID&gt;_*.dds2.
    ''' The .dds2 extension prevents clobbering CK's real .dds; for a real bake we'd write
    ''' .dds and the engine would pick those up. Then rewrites slots 0/1/7 of the cloned
    ''' shape's texture set to the new .dds path.
    '''
    ''' Also reads the CK-baked .dds (uncompressed BGRA8) and logs a per-channel BGRA RMS
    ''' diff — gives a quantitative signal of how close our composited bake is to CK's.</summary>
    ''' <summary>Bake the per-NPC FaceCustomization _d/_msn/_s textures, write them to the loose
    ''' folder under <c>Data\Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\&lt;formId&gt;_*_2.dds</c>
    ''' (the _2 suffix only in DebugMode), and rewrite slots 0/1/7 of the cloned shape's TextureSet
    ''' to point at those textures. The embedded path uses the canonical (non-_2) name when
    ''' <paramref name="willBePacked"/> is True (Save ESP → BA2 repack renames to canonical) or the
    ''' actual on-disk _2 name when False ("Build CharGen (loose)" → standalone, no rename).
    '''
    ''' Standalone — does NOT read the live preview <c>Textures_Dictionary</c>. The face source
    ''' D/N/S DDS bytes are pulled directly from the FilesDictionary via the source NIF's
    ''' resolved material, uploaded to GL temporaries, run through the same
    ''' <see cref="FaceTintCompositor.ApplyFaceTintPipeline"/> the live render uses, read back,
    ''' encoded, and persisted. Both temporaries and pipeline outputs are deleted before return
    ''' (host's CompositorState + TintGpuCache are reused but never observed mutating any
    ''' caller-owned state).</summary>
    ''' <summary>SSE facetint bake: compose the per-NPC facetint _d (CPU, engine-exact, WYSIWYG with the
    ''' overlaid tint edit), write it to &lt;DataPath&gt;\Textures\Actors\Character\FaceGenData\FaceTint\&lt;plugin&gt;\
    ''' &lt;formID&gt;.dds, and point the cloned Face shape's texture-set slot 6 at it. SSE-only (game-gated);
    ''' replaces the FO4 FaceCustomization D/N/S bake.
    '''
    ''' Debug naming = SAME logic as the FO4 bake (see <see cref="BakeFaceTextures"/>): in DebugMode the DDS
    ''' lands as <c>&lt;formID&gt;_2.dds</c> (sandbox next to CK's, never clobbering it), and the suffix embedded
    ''' into the NIF depends on the consumer — canonical when <paramref name="willBePacked"/> (the packer
    ''' renames the _2 loose to canonical entries), the actual on-disk _2 name otherwise ("Build CharGen
    ''' (loose)"), so the standalone NIF references a file that exists. En debug+sandbox además emite el <c>_2b</c>
    ''' (recompose GPU del MISMO facetint vía <see cref="WriteSseFacetint2bGpu"/>) para medir paridad CPU==GPU, y un
    ''' TGA lossless por cada .dds cuando "Generate TGA" está marcado (igual que el bake FO4).</summary>
    Private Sub WriteSseFacetintDds(nif As Nifcontent_Class_Manolo, cloned As INiShape, npcFormID As UInteger,
                                    originPlugin As String, pluginManager As PluginManager,
                                    npcData As NPC_Data, willBePacked As Boolean,
                                    Optional host As NpcRenderHost = Nothing)
        Try
            If npcData Is Nothing Then Return
            Dim npcRec = pluginManager.GetRecord(npcFormID)
            If npcRec Is Nothing Then Return
            Dim raceFid As UInteger = npcData.RaceFormID
            Dim race As RACE_Data = Nothing
            If raceFid <> 0UI Then
                Dim rr = pluginManager.GetRecord(raceFid)
                If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = RecordParsers.ParseRACE(rr, pluginManager)
            End If
            If race Is Nothing Then Return
            ' Overlaid tints + RaceMenu overlays (Edit Face edits) so the bake is byte-WYSIWYG with the live
            ' preview (both call BakeFaceTintDds with the same tint override + overlays).
            Dim tintOverride As IList(Of NPC_RawSubrecord) = npcData.SseTintRaw
            ' Tamaño del facetint = propiedad Setting_FaceGenDiffuseResolution (Inherit→512 vanilla = default byte-inerte;
            ' 1024/2048/… si el usuario lo sube). NO hardcodeado. El facetint es el "diffuse" del facegen SSE.
            Dim fSz = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, 512)
            ' Formato del facetint = el elegido por el usuario (CharGen Options → Diffuse), NO hardcodeado. Antes
            ' BakeFaceTintDds forzaba BC3, así que el facetint real y el neutral del fold podían salir con formatos
            ' distintos según el NPC estuviera plegado o no.
            ' GATE del encode (ver SkipDdsEncode). ⛔ El Nothing/no-Nothing SI decide el slot 6, y sale del
            ' COMPOSE (ComposeFacetintAcc), no del encode: un NPC sin capas de tint no tiene facetint y el bake
            ' no le escribe el slot. Por eso en modo gateado se corre igual el compose (512x512) y se saltea
            ' SOLO el EncodeLinearRgbaToBc3 + el File.Write ⇒ misma condicion, mismo NIF.
            Dim dds As Byte() = Nothing
            If SkipDdsEncode Then
                If SseFaceGenBaker.ComposeFacetintAcc(pluginManager, npcRec, race, raceFid, npcData.IsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride) Is Nothing Then Return
            Else
                dds = SseFaceGenBaker.BakeFaceTintDds(pluginManager, npcRec, race, raceFid, npcData.IsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride, DiffuseDxgiFromSetting())
                If dds Is Nothing Then Return
            End If
            Dim fgLocal = PluginManager.ToFaceGenLocalFormID(npcFormID)
            Dim tintDir = $"Textures\Actors\Character\FaceGenData\FaceTint\{originPlugin}\"
            Dim suffix = If(DebugMode, "_2.dds", ".dds")          ' on-disk name (sandbox in DebugMode)
            Dim embeddedSuffix = If(willBePacked, ".dds", suffix) ' the packer renames _2 → canonical
            Dim rel = tintDir & $"{fgLocal:X8}{suffix}"
            Dim outFile = IO.Path.Combine(Config_App.Current.DataPath, rel)
            If Not SkipDdsEncode Then
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
                IO.File.WriteAllBytes(outFile, dds)
            End If
            ' TGA lossless del _2 (CPU) cuando "Generate TGA" está marcado (= FO4). Recompone el acc SOLO en ese
            ' caso (no re-decodea el BC3) para dumpear el buffer pre-encode, byte-igual al que se encodeó.
            If WriteTGASandboxOutput Then
                Dim accT = SseFaceGenBaker.ComposeFacetintAcc(pluginManager, npcRec, race, raceFid, npcData.IsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride)
                If accT IsNot Nothing Then MaybeWriteTgaBeside(outFile, fSz, fSz, SseFaceGenBaker.LinearRgbaToBgra(accT, fSz, fSz))
            End If
            ' Point the head shape's texture-set slot 6 (facetint) at the engine path (Data-relative).
            Dim spr = cloned.ShaderPropertyRef
            If spr IsNot Nothing AndAlso spr.Index >= 0 Then
                Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
                If lsp IsNot Nothing AndAlso lsp.TextureSetRef IsNot Nothing AndAlso lsp.TextureSetRef.Index >= 0 Then
                    Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
                    ' ⭐ El slot 6 sigue la MISMA ley que el resto (bake CK 0x141d0ea00): sólo lo escribe el
                    ' branch type 4 FaceTint. El gate del call site es por HDPT.PartType=Face, que NO es
                    ' equivalente: un head part de cara puede tener un shape autorado con otro shader type.
                    ' MEDIDO: 'MaleHeadManekin' (HDPT 0x1078799, PartType=Face, TNAM=0, MODL=ManekinHead.nif)
                    ' tiene shape shType=Default(0) ⇒ el CK deja el slot 6 VACÍO, y nosotros le escribíamos el
                    ' facetint. 8 NPCs (Dawnguard 00008B34/0000D1BE · Dragonborn 0002A378/79/7A ·
                    ' HearthFires 00008B32/00015D5D · Skyrim 00089A85).
                    If lsp.ShaderType_SK_FO4 <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then
                        ' Gateado por Logger.Enabled ADEMÁS del LogLazy: sin el gate se aloca la clausura en
                        ' CADA shape de CADA NPC aunque el log esté apagado. Convención del codebase.
                        If Logger.Enabled Then
                            Dim stL6 = lsp.ShaderType_SK_FO4
                            Logger.LogLazy(Function() $"[FACEBAKE][SSE] slot6 NO escrito: shape shType={stL6} (≠FaceTint) — ley CK 0x141d0ea00")
                        End If
                    ElseIf ts IsNot Nothing AndAlso ts.Textures IsNot Nothing AndAlso ts.Textures.Count > 6 Then
                        ts.Textures(6).Content = EmbeddedEngineTexPath(tintDir & $"{fgLocal:X8}{embeddedSuffix}")
                        ' NOTE: NO "Textures\" prefix on the skin slots 0/7. MEDIDO vs BSA CK (batch SSE): CK escribe
                        ' el head diffuse SIN prefijo (p.ej. 'Actors\Character\Male\MaleHead.dds'), byte-igual al
                        ' valor ya resuelto del skin TXST. Un intento anterior de prefijar 0/7 fue medido contra un
                        ' FaceGeom LOOSE (mi propio bake ya prefijado ⇒ circular, gotcha
                        ' reference_facegen_ck_must_come_from_ba2) y RETRACTADO: prefijar rompía la paridad.
                    End If
                End If
            End If
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _d -> {rel} ({dds.Length}b)")

            ' === _2b GPU SANDBOX del facetint BASE (debug+sandbox, requiere host GL) ===
            ' Contraparte GPU del _2 (CPU): compone PURO GPU las MISMAS capas de tint (BuildLayerInputs) sobre un
            ' base PLANO = seed(0.5) vía ApplyFaceTintPipeline y hace readback → _2b. NO sube el resultado CPU (eso
            ' sería trampa y no mediría nada): RECOMPONE en GPU para medir la paridad CPU==GPU del facetint base.
            ' Espejo exacto del _2b de FO4 y del _2b de overlays. Sólo app (host); la paridad la confirma el usuario.
            If host IsNot Nothing AndAlso DebugMode AndAlso WriteGPUSandboxOutput Then
                Try
                    Dim glayers = SseFaceTintComposer.BuildLayerInputs(pluginManager, npcRec, race, raceFid, npcData.IsFemale,
                                                                       npcData.SseTintRaw, npcData.SseTintTexOverride)
                    If glayers IsNot Nothing AndAlso glayers.Count > 0 Then WriteSseFacetint2bGpu(glayers, fSz, fSz, fgLocal, originPlugin, host)
                Catch ex2b As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _2b GPU failed: {ex2b.GetType().Name}: {ex2b.Message}")
                End Try
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint bake failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>_2b GPU del facetint BASE: recompone las capas de tint del NPC (PaletteMask, canal R, ley SSE) sobre
    ''' un base PLANO = seed(0.5) por GL (<see cref="FaceTintCompositor.ApplyFaceTintPipeline"/>), readback → encode →
    ''' <c>FaceTint\&lt;plugin&gt;\&lt;id&gt;_2b.dds</c> (BC3, = formato del <c>_2</c>). Compose PURO GPU (NO sube el
    ''' resultado CPU del <c>_2</c>): el par <c>_2</c>/<c>_2b</c> mide la paridad CPU==GPU del facetint. Base subido
    ''' como LINEAL (<c>baseDiffuseIsLinearOnGpu:=True</c>) para que el seed 0.5 GL coincida con el 0.5-lin del CPU.
    ''' GL-bound (corre en el hilo del host). SSE-only, debug sandbox.</summary>
    Private Sub WriteSseFacetint2bGpu(layers As IList(Of FaceTintLayerInput), w As Integer, h As Integer,
                                      fgLocal As UInteger, originPlugin As String, host As NpcRenderHost)
        Dim gbra = ComposeSseFacetintBgraOnGpu(layers, w, h, host)
        If gbra Is Nothing Then Return
        Dim mips = CInt(Math.Floor(Math.Log(Math.Min(w, h), 2))) + 1
        ' Formato = el MISMO que el _2 (CharGen Options → Diffuse), NO hardcodeado: el _2b existe para compararse
        ' contra el _2, así que si el _2 sale BC7/Uncompressed y el _2b quedara fijo en BC3, la comparación medía
        ' la diferencia de FORMATO en vez de la paridad CPU-vs-GPU que se quiere medir.
        Dim dds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(w, h, gbra, DiffuseDxgiFromSetting(), generateMipMaps:=True, generatedMipLevels:=mips)
        If dds Is Nothing Then Return
        Dim rel = $"Textures\Actors\Character\FaceGenData\FaceTint\{originPlugin}\{fgLocal:X8}_2b.dds"
        Dim outFile = IO.Path.Combine(Config_App.Current.DataPath, rel)
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
        IO.File.WriteAllBytes(outFile, dds)
        MaybeWriteTgaBeside(outFile, w, h, gbra)
        Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _2b GPU -> {rel} ({dds.Length}b)")
    End Sub

    ''' <summary>Compone las capas de tint SSE (PaletteMask, ley SSE all-linear) sobre un base PLANO = seed(0.5) por
    ''' GL (<see cref="FaceTintCompositor.ApplyFaceTintPipeline"/>) y hace readback → BGRA lineal (W·H·4). Base subido
    ''' como LINEAL (baseDiffuseIsLinearOnGpu) para que el seed 0.5 GL == el 0.5-lin del CPU. Nothing si falla.
    ''' Contraparte GPU del compose CPU del facetint (SseFaceTintComposer.ComposeLinearRgba). GL-bound (host).</summary>
    Private Function ComposeSseFacetintBgraOnGpu(layers As IList(Of FaceTintLayerInput), w As Integer, h As Integer, host As NpcRenderHost) As Byte()
        If host Is Nothing OrElse layers Is Nothing OrElse layers.Count = 0 OrElse w <= 0 OrElse h <= 0 Then Return Nothing
        Dim npix = w * h
        Const seedByte As Byte = 128   ' round(0.5*255) = seed constante SSE (ActiveSettings.SeedConstant)
        Dim baseBgra(npix * 4 - 1) As Byte
        For i = 0 To npix - 1
            baseBgra(i * 4) = seedByte : baseBgra(i * 4 + 1) = seedByte : baseBgra(i * 4 + 2) = seedByte : baseBgra(i * 4 + 3) = 255
        Next
        Dim baseTex = UploadBgraToGl(baseBgra, w, h)
        If baseTex = 0 Then Return Nothing
        Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                          baseTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                          baseDiffuseIsLinearOnGpu:=True)
        Dim resultId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, baseTex)
        Dim gbuf = ReadbackGlBgra(resultId, npix)
        If resultId <> baseTex Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(resultId) : Catch : End Try
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(baseTex) : Catch : End Try
        Return gbuf
    End Function

    ''' <summary>_2d = el pliegue SSE **100% GPU**, contraparte exacta del _2c (100% CPU). Corre EXACTAMENTE las mismas
    ''' funciones que el RENDER (<see cref="SseFoldLayerStack"/>) ⇒ el sandbox mide el código que de verdad se ejecuta,
    ''' no una copia paralela que se puede desincronizar. Tres pasos, todos GPU y todos en FLOAT (Rgba32f):
    '''   1. facetint  = ComposeFacetintGpu(capas de tint sobre seed 0.5)      [lineal]
    '''   2. pliegue   = FoldGpu(complexion, facetint, detail)                 [ley del engine: fgTint × softlight]
    '''   3. capas     = ComposeGpu(skee MASKT + overlays Face[Ovl])           [stack de capas]
    ''' ⛔ NADA de intermedios en 8 bits. La versión anterior transportaba el facetint como DDS y hacía el readback en
    ''' bytes LINEALES: MEDIDO contra el _2c daba RMS 2,4/255 y máx 18, con el error concentrado en las sombras (5,7 medio
    ''' en 0..31 vs 0,3 en 128..159) — la firma de cuantizar en lineal (cerca del negro 1 nivel lineal ≈ 13 niveles sRGB),
    ''' agravado porque el fgTint amplifica el facetint ×255/64. En float el transporte deja de limitar la paridad.
    ''' GL-bound (host). SSE-only, debug sandbox.</summary>
    Private Sub WriteSseFacetint2dGpu(layers As IList(Of FaceTintLayerInput), complexionPath As String, detailPath As String,
                                      overlays As IList(Of RaceMenuJslot.JslotOverlayNode),
                                      fgLocal As UInteger, originPlugin As String, host As NpcRenderHost)
        If host Is Nothing OrElse String.IsNullOrEmpty(complexionPath) Then Return
        ' complexion (slot 0) a su tamaño NATIVO (= el tamaño al que el _2c pliega en CPU), en sRGB.
        Dim srcBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(complexionPath))
        If srcBytes Is Nothing Then Return
        Dim dec = FaceTintCpuCompositor.DecodeDds(srcBytes)
        If dec Is Nothing OrElse dec.Rgba Is Nothing OrElse dec.Width <= 0 OrElse dec.Height <= 0 Then Return
        Dim w = dec.Width, h = dec.Height, npix = w * h
        Dim det As Single() = If(Not String.IsNullOrEmpty(detailPath), SseFaceTintComposer.DecodeTextureRgba(detailPath, w, h), Nothing)

        ' 1) facetint por GPU (float, lineal).
        Dim facetint = SseFoldLayerStack.ComposeFacetintGpu(layers, w, h, host)
        If facetint Is Nothing Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: el compose GPU del facetint falló.")
            Return
        End If
        ' 2) pliegue por GPU (float). Sale en sRGB, igual que el fold CPU. Todo en Single (storage float32).
        Dim acc = SseFoldLayerStack.FoldGpu(dec.Rgba, facetint, det, w, h, host)
        If acc Is Nothing Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: el pliegue GPU falló.")
            Return
        End If
        ' 3) stack de capas por GPU (overlays Face[Ovl]). El bake del _2d no lee MASKT del NIF (el _2c tampoco) ⇒ Nothing.
        If overlays IsNot Nothing AndAlso overlays.Count > 0 Then
            Dim withOvl = SseFoldLayerStack.ComposeGpu(acc, Nothing, overlays, Nothing, w, h, host)
            If withOvl Is Nothing Then
                Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: el compose GPU de los overlays falló.")
                Return
            End If
            acc = withOvl
        End If

        ' acc (sRGB, Double) -> BGRA. ⚠ ClampByte255 de esta clase espera 0..255 (NO multiplica).
        Dim gbuf(npix * 4 - 1) As Byte
        For i = 0 To npix - 1
            gbuf(i * 4) = ClampByte255(acc(i * 4 + 2) * 255.0)      ' B
            gbuf(i * 4 + 1) = ClampByte255(acc(i * 4 + 1) * 255.0)  ' G
            gbuf(i * 4 + 2) = ClampByte255(acc(i * 4) * 255.0)      ' R
            gbuf(i * 4 + 3) = 255
        Next

        ' Resolución de salida = Setting_FaceGenDiffuseResolution (Inherit→nativo no-op; resample filtro FO4 = release/_2c).
        Dim gpW = w, gpH = h, gpBuf = gbuf
        If OutputSettings.Diffuse <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
            Dim gt = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, Math.Min(w, h))
            gpBuf = FaceTintCpuCompositor.ResampleBgra(gbuf, w, h, gt, gt) : gpW = gt : gpH = gt
        End If
        Dim mips = CInt(Math.Floor(Math.Log(Math.Min(gpW, gpH), 2))) + 1
        Dim dds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(gpW, gpH, gpBuf, DiffuseDxgiFromSetting(), generateMipMaps:=True, generatedMipLevels:=mips)
        If dds Is Nothing Then Return
        Dim rel = $"Textures\Actors\Character\FaceGenData\FaceDiffuse\{originPlugin}\{fgLocal:X8}_2d.dds"
        Dim outFile = IO.Path.Combine(Config_App.Current.DataPath, rel)
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
        IO.File.WriteAllBytes(outFile, dds)
        MaybeWriteTgaBeside(outFile, gpW, gpH, gpBuf)
        Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2d pliegue PURO GPU (float) -> {rel} ({dds.Length}b, {w}x{h})")
    End Sub

    ''' <summary>Cuando "Generate TGA" está marcado (<see cref="WriteTGASandboxOutput"/>, = FO4), escribe un TGA
    ''' UNCOMPRESSED lossless al lado del .dds indicado, desde el MISMO BGRA que se encodeó (no re-decodea el BCn).
    ''' No-op si el toggle está off o el BGRA es Nothing. Espejo del dump TGA del bake FO4 (BakeFaceTextures).</summary>
    Private Sub MaybeWriteTgaBeside(ddsAbsPath As String, w As Integer, h As Integer, bgra As Byte())
        If Not WriteTGASandboxOutput OrElse bgra Is Nothing OrElse String.IsNullOrEmpty(ddsAbsPath) OrElse w <= 0 OrElse h <= 0 Then Return
        Try
            Dim tga = IO.Path.ChangeExtension(ddsAbsPath, "tga")
            FaceTintCompositor.WriteBgraToTga(tga, bgra, w, h)
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] wrote TGA '{tga}'")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] TGA write failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Readback de una textura GL RGBA8 a BGRA (W·H·4 bytes). Nothing si falla. GL-bound.</summary>
    Private Function ReadbackGlBgra(texId As Integer, npix As Integer) As Byte()
        If texId = 0 OrElse npix <= 0 Then Return Nothing
        Dim gbuf(npix * 4 - 1) As Byte
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, texId)
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(gbuf, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.GetTexImage(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        Return gbuf
    End Function

    ''' <summary>SSE: when the NPC has RaceMenu FACE overlays (Face [Ovl] face-paint), bake them INTO a per-NPC
    ''' head diffuse and repoint the head shape's texture-set slot 0 at it — the engine renders these live and
    ''' never bakes them, so this is the "bake RaceMenu options into the texture" fix (SSE, gated by
    ''' <see cref="Config_App.Setting_BakeSseRaceMenuOverlays"/>, default on). GATED: NPCs with NO bakeable face
    ''' overlay get NOTHING (slot 0 keeps the shared vanilla complexion) — so vanilla output is byte-unchanged.
    ''' The overlays come from the applied preset's <see cref="LooksmenuLoader.LooksmenuPreset.SseBodyOverlays"/>
    ''' (the SAME list the editor edits and the render draws as decals — single source, no dead SseOverlay
    ''' struct). Composite = <see cref="SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse"/> (skee normal.fx
    ''' alpha-over). Debug naming mirrors the facetint (_2 in DebugMode; embedded slot 0 uses canonical when
    ''' willBePacked, else the on-disk _2 name).</summary>
    ''' <param name="forcedSuffix">Cuando NO es Nothing (p.ej. "_2c"), corre en modo SANDBOX FORZADO (debug):
    ''' pliega+reemplaza el _d/_n AUNQUE el NPC no tenga overlays/tints (bypass del gate), escribe las texturas con
    ''' ESE sufijo, y embebe ESE sufijo en los slots (nunca se packea). Sirve para ejercitar el replacer completo en
    ''' cualquier NPC. Nothing = comportamiento normal (gateado por overlays, naming _2/canónico).</param>
    ''' <summary>⭐⭐ Path tal como debe quedar EMBEBIDO en el texture-set del NIF para las texturas que genera el bake
    ''' (FaceTint / FaceDiffuse / FaceNormal), es decir las que viven bajo <c>Data\Textures\...</c>.
    '''
    ''' ⛔ EL BUG QUE ESTO ARREGLA (brown face, MEDIDO contra el NIF vanilla del BSA): los paths de un BSShaderTextureSet
    ''' son RELATIVOS A <c>Data\Textures\</c>. Por eso el CK escribe el head diffuse como
    ''' <c>Actors\Character\Female\FemaleHead.dds</c> (SIN prefijo). Nosotros, en cambio, escribíamos
    ''' <c>Textures\Actors\Character\FaceGenData\FaceTint\...</c> ⇒ el engine lo resolvía como
    ''' <c>Data\Textures\<b>Textures\</b>Actors\...</c> ⇒ NO EXISTE ⇒ el tint queda NULL ⇒ CARA MARRÓN.
    ''' El CK resuelve lo mismo con la OTRA convención que el engine acepta: prefijo <c>data\</c>, que es absoluto desde
    ''' Data\ y por eso puede llevar el <c>Textures\</c> adentro. Vanilla, byte a byte (0008774F):
    '''   slot 6 = <c>data\Textures\Actors\Character\FaceGenData\FaceTint\Skyrim.esm\0008774F.dds</c>
    ''' ⇒ replicamos EXACTAMENTE eso. (La ruta EN DISCO no cambia: esa sí es <c>Textures\...</c> relativa a DataPath.)
    ''' ⚠️ Esto REFUTA la nota vieja de que "el engine ignora el slot 6 y arma el path solo": si lo ignorara, un path
    ''' roto ahí no daría brown face — y lo da.</summary>
    ''' <para>⛔ GAME-AWARE: SÓLO SSE. En FO4 el bake embebe sus paths de facegen SIN este prefijo y FUNCIONA (medido:
    ''' el bake FO4 es byte-fiel al CK) ⇒ no se toca. Los 5 call-sites viven en funciones <c>WriteSse*</c>, pero el
    ''' guard por juego se pone acá igual: que la corrección dependa de en qué función estás es exactamente el tipo de
    ''' supuesto implícito que después se rompe al mover código.</para>
    Private Function EmbeddedEngineTexPath(relUnderData As String) As String
        If String.IsNullOrEmpty(relUnderData) Then Return relUnderData
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return relUnderData
        Return "data\" & relUnderData
    End Function

    ''' <summary>Borra los artefactos que SÓLO produce el camino PLEGADO — <c>FaceDiffuse\&lt;plugin&gt;\&lt;id&gt;.dds</c> y
    ''' <c>FaceNormal\&lt;plugin&gt;\&lt;id&gt;.dds</c> — cuando este bake NO pliega. Sin esto quedan de un bake plegado anterior
    ''' y el packer los mete al BSA (toma el Source del DISCO), aunque el NIF nuevo apunte al complexion vanilla.
    ''' ⭐ Se borran AMBOS naming (canónico y <c>_2</c> de DebugMode): alternar Debug/Release deja stale de los dos.
    ''' ⛔ NO se toca <c>FaceTint\&lt;id&gt;.dds</c> (existe en LOS DOS caminos con contenido opuesto — real vs neutral — y el
    ''' bake lo reescribe siempre ⇒ se pisa solo) ni <c>facedetailneutral.dds</c> (es COMPARTIDO entre NPCs: borrarlo por
    ''' un NPC que dejó de plegar rompería a los que sí pliegan; el packer ya lo emite sólo si algún NPC lo usa).</summary>
    Private Sub DeleteFoldedOnlyArtifacts(npcFormID As UInteger, originPlugin As String)
        If String.IsNullOrEmpty(originPlugin) OrElse Config_App.Current Is Nothing Then Return
        Dim dataPath = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPath) Then Return
        Dim hex = PluginManager.ToFaceGenLocalFormID(npcFormID).ToString("X8")
        For Each subDir In {"FaceDiffuse", "FaceNormal"}   ' 'dir' es palabra reservada en VB (función Dir)
            For Each suffix In {".dds", "_2.dds"}
                Dim rel = IO.Path.Combine("Textures\Actors\Character\FaceGenData", subDir, originPlugin, hex & suffix)
                Dim full = IO.Path.Combine(dataPath, rel)
                Try
                    If IO.File.Exists(full) Then
                        IO.File.Delete(full)
                        Logger.LogLazy(Function() $"[FACEBAKE][SSE] stale del camino PLEGADO borrado (este bake NO pliega): {rel}")
                    End If
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] no se pudo borrar el stale '{rel}': {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
        Next
    End Sub

    Private Sub WriteSseFaceDiffuseWithOverlays(nif As Nifcontent_Class_Manolo, cloned As INiShape, npcFormID As UInteger,
                                                originPlugin As String, pluginManager As PluginManager, npcData As NPC_Data,
                                                appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                                willBePacked As Boolean, Optional forcedSuffix As String = Nothing,
                                                Optional complexionPathOverride As String = Nothing,
                                                Optional normalPathOverride As String = Nothing,
                                                Optional detailPathOverride As String = Nothing,
                                                Optional host As NpcRenderHost = Nothing)
        Try
            Dim forced = Not String.IsNullOrEmpty(forcedSuffix)
            ' El toggle "Bake RaceMenu overlays" NO aplica al forzado (_2c): el _2c ejercita el replacer completo
            ' aunque el usuario tenga el bake de overlays apagado. Sólo gatea el path normal (gateado por overlays).
            If Config_App.Current Is Nothing Then Return
            If Not forced AndAlso Not Config_App.Current.Setting_BakeSseRaceMenuOverlays Then Return
            ' Fuentes a bakear: (a) RaceMenu Face [Ovl] overlays del preset (si hay) + (b) skee MASKT masks del
            ' head shape. Gate BARATO (sin decode): salir solo si NINGUNA de las dos aporta → vanilla intacto.
            ' En modo FORZADO (_2c) el gate NO aplica: se corre el replacer completo igual.
            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If appliedPresets IsNot Nothing Then appliedPresets.TryGetValue(npcFormID, preset)
            Dim overlays = If(preset IsNot Nothing, preset.SseBodyOverlays, Nothing)
            ' ⛔ El gate mira DIFFUSE **O** NORMAL (HasAnyFoldableFaceOverlay). Un overlay de cara SOLO-NORMAL
            ' (NormalPath sin DiffusePath) es válido — ComposeFaceOverlayNormalsIntoMsn lo pliega usando el alpha
            ' del propio normal como cobertura. Gatear sólo por diffuse hacía SALIR TEMPRANO y el normal no se
            ' plegaba nunca; y como el script Papyrus saltea TODO nodo Face* (la cara es territorio del bake,
            ' siempre), ese overlay no lo aplicaba nadie: desaparecía.
            Dim hasOverlays = SseOverlayCompositor.HasAnyFoldableFaceOverlay(overlays)
            Dim hasSkee = SseSkeeMaskReader.HasMaskLayers(nif, cloned)
            If Not forced AndAlso Not (hasOverlays OrElse hasSkee) Then
                ' ⛔⛔ VANILLA (no se pliega): el slot 0 queda intacto... PERO HAY QUE BORRAR LOS ARTEFACTOS DEL CAMINO
                ' PLEGADO. Los dos caminos producen conjuntos DISTINTOS de archivos:
                '   no plegado → FaceTint\<id>.dds = facetint REAL. NO produce FaceDiffuse ni FaceNormal.
                '   plegado    → FaceTint\<id>.dds = NEUTRAL gris + FaceDiffuse\<id>.dds + [FaceNormal\<id>.dds].
                ' El FaceTint comparte path en ambos ⇒ se PISA solo (el bake lo reescribe siempre) ⇒ no hay stale.
                ' Pero FaceDiffuse/FaceNormal SÓLO existen en el plegado: si el NPC se bakeó plegado ANTES (p.ej.
                ' tenía un overlay que después se le quitó) esos archivos QUEDAN en disco, y el packer los toma del
                ' DISCO (FaceGenFileSpecs: Source = ruta en disco, IsOptional sólo dice "si falta, saltealo" — nunca
                ' "si sobra, ignoralo") ⇒ **entran al BSA aunque el NIF nuevo no los referencie**. MEDIDO: un BSA con
                ' el NIF vanilla y un FaceDiffuse plegado adentro, mezcla de dos bakes. Borrarlos acá es lo que hace
                ' que el camino elegido sea el ÚNICO que deja archivos.
                DeleteFoldedOnlyArtifacts(npcFormID, originPlugin)
                Return
            End If

            ' Head shape's resolved slot-0 diffuse (the complexion base we overlay ONTO).
            Dim spr = cloned.ShaderPropertyRef
            If spr Is Nothing OrElse spr.Index < 0 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: ShaderPropertyRef null")
                Return
            End If
            Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
            If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: BSLightingShaderProperty/TextureSetRef null")
                Return
            End If
            Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
            If ts Is Nothing OrElse ts.Textures Is Nothing OrElse ts.Textures.Count < 1 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: BSShaderTextureSet null/empty")
                Return
            End If
            ' Complexion base = slot 0, SALVO override (forzado _2c: el pass normal ya pudo mutar slot0 a un diffuse
            ' plegado ⇒ para NO doble-plegar, el forzado recibe el complexion ORIGINAL capturado antes de mutar).
            Dim diffPath = If(forced AndAlso Not String.IsNullOrEmpty(complexionPathOverride), complexionPathOverride, ts.Textures(0).Content)
            If String.IsNullOrEmpty(diffPath) Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion path empty (override='{complexionPathOverride}', slot0='{ts.Textures(0).Content}')")
                Return
            End If

            ' Decode the complexion at its native size (mip0).
            Dim srcBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(diffPath))
            If srcBytes Is Nothing Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion bytes not found for '{diffPath}'")
                Return
            End If
            Dim decoded = FaceTintCpuCompositor.DecodeDds(srcBytes)
            If decoded Is Nothing OrElse decoded.Rgba Is Nothing OrElse decoded.Width <= 0 OrElse decoded.Height <= 0 Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion decode failed for '{diffPath}'")
                Return
            End If
            Dim w = decoded.Width, h = decoded.Height
            Dim npix = w * h
            Dim acc(npix * 4 - 1) As Single
            Array.Copy(decoded.Rgba, acc, acc.Length)

            ' === PLIEGUE (orden fiel a RaceMenu) ===
            ' El overlay va DESPUÉS del skin tint. El engine hace albedo *= fgTint(facetint_d). Para que el overlay
            ' NO quede teñido por el skin tint, plegamos el facetint DENTRO del diffuse (base = complexion × fgTint),
            ' y neutralizamos el slot 6 (así el engine no re-aplica). base ES el albedo skin-tinted; overlays encima.
            Dim npcRec = pluginManager.GetRecord(npcFormID)
            Dim raceFid As UInteger = npcData.RaceFormID
            Dim race As RACE_Data = Nothing
            If npcRec IsNot Nothing AndAlso raceFid <> 0UI Then
                Dim rr = pluginManager.GetRecord(raceFid)
                If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = RecordParsers.ParseRACE(rr, pluginManager)
            End If
            If npcRec IsNot Nothing AndAlso race IsNot Nothing Then
                ' facetint _d LINEAL al tamaño del complexion (misma resolución que el diffuse que multiplica).
                ' Es SOLO los tints de RACE (skin tone + warpaint) — los overlays de cara NO van acá (van sobre el
                ' base DESPUÉS del pliegue, ese es el orden de RaceMenu). Mismo _d que WriteSseFacetintDds compone.
                Dim facetint = SseFaceTintComposer.ComposeLinearRgba(pluginManager, npcRec, race, raceFid, npcData.IsFemale, w, h,
                                                                     Nothing, npcData.SseTintRaw, npcData.SseTintTexOverride)
                ' Detail mask (slot 3 / DisplacementTexture): el engine hace softlight(complexion, detail) ANTES del
                ' fgTint (Shader_Class 1864→1878). Se pliega ACÁ y se NEUTRALIZA el slot 3 abajo (si no, el engine lo
                ' re-aplica sobre el _2c). Detail crudo (no está en color textures). Si no hay, softlight identidad.
                ' En FORZADO (_2c) el slot 3 del head clonado YA lo neutralizo el pass non-forced (se comparte el mismo
                ' `cloned`) ⇒ leerlo en vivo daria "" y el fold saltearia el softlight del detail (→ _2c MAS CLARO que
                ' _2/_2d, bug medido). Usar el detail ORIGINAL capturado antes de mutar (detailPathOverride), igual que
                ' el complexion. En non-forced el slot 3 se lee en vivo (aun sin neutralizar en este punto) = correcto.
                Dim detailPath = If(forced, If(detailPathOverride, ""), If(ts.Textures.Count > 3, ts.Textures(3).Content, ""))
                Dim detailAcc As Single() = If(Not String.IsNullOrEmpty(detailPath), SseFaceTintComposer.DecodeTextureRgba(detailPath, w, h), Nothing)
                If facetint IsNot Nothing Then SseFaceGenBaker.FoldFacetintIntoDiffuse(acc, facetint, npix, detailAcc)   ' albedo = fgTint × softlight(complexion, detail)
            End If

            ' (a) skee MASKT masks (dyeable heads) sobre el base plegado, luego (b) los Face [Ovl] overlays
            ' (orden por índice de nodo, = skee/render). Cualquiera puede faltar; OR de las dos.
            Dim skinRgb = SseSkinRgbForNpc(pluginManager, npcData, npcFormID)
            Dim anySkee = SseSkeeMaskReader.ComposeNifMaskLayersIntoDiffuse(nif, cloned, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba, skinRgb, Nothing, acc)
            Dim anyOvl = SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse(acc, overlays, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba)
            ' En modo FORZADO igual escribimos (el pliegue solo ya es el replacer, aunque no haya overlays).
            If Not forced AndAlso Not (anySkee OrElse anyOvl) Then Return

            Dim bgra(w * h * 4 - 1) As Byte
            For i = 0 To w * h - 1
                bgra(i * 4) = ClampByte255(acc(i * 4 + 2) * 255.0)      ' B
                bgra(i * 4 + 1) = ClampByte255(acc(i * 4 + 1) * 255.0)  ' G
                bgra(i * 4 + 2) = ClampByte255(acc(i * 4) * 255.0)      ' R
                bgra(i * 4 + 3) = ClampByte255(acc(i * 4 + 3) * 255.0)  ' A
            Next
            ' Resolución de salida = Setting_FaceGenDiffuseResolution (Inherit→nativo = no-op byte-inerte; 1024/2048/…
            ' resamplea con el MISMO filtro bilineal GL_LINEAR+clamp que el compositor FO4 → matchea el per-layer FO4).
            Dim dOutW = w, dOutH = h, dOutBgra = bgra
            If OutputSettings.Diffuse <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
                Dim t = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, Math.Min(w, h))
                dOutBgra = FaceTintCpuCompositor.ResampleBgra(bgra, w, h, t, t) : dOutW = t : dOutH = t
            End If
            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(dOutW, dOutH), 2))) + 1
            ' MISMA compresión del diffuse que elige el usuario en CharGen Options (Setting_FaceGenDiffuseCompression):
            ' BC3 (default SSE) / BC7 / Uncompressed. No hardcode.
            Dim outDds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                width:=dOutW, height:=dOutH, bgraPixels:=dOutBgra,
                outputDxgiFormat:=DiffuseDxgiFromSetting(),
                generateMipMaps:=True, generatedMipLevels:=mipLevels)
            If outDds Is Nothing Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: encode returned Nothing ({w}x{h}, dxgi={DiffuseDxgiFromSetting()})")
                Return
            End If

            Dim fgLocal = PluginManager.ToFaceGenLocalFormID(npcFormID)
            Dim dir = $"Textures\Actors\Character\FaceGenData\FaceDiffuse\{originPlugin}\"
            ' Naming: forzado (_2c) usa ESE sufijo en disco Y embebido (nunca packea); normal = _2/canónico.
            Dim suffix = If(forced, forcedSuffix & ".dds", If(DebugMode, "_2.dds", ".dds"))
            Dim embeddedSuffix = If(forced, suffix, If(willBePacked, ".dds", suffix))
            Dim rel = dir & $"{fgLocal:X8}{suffix}"
            Dim outFile = IO.Path.Combine(Config_App.Current.DataPath, rel)
            IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
            IO.File.WriteAllBytes(outFile, outDds)
            MaybeWriteTgaBeside(outFile, dOutW, dOutH, dOutBgra)
            ts.Textures(0).Content = EmbeddedEngineTexPath(dir & $"{fgLocal:X8}{embeddedSuffix}")

            ' NEUTRALIZAR slot 3 (detail/Displacement): el softlight(complexion, detail_real) YA se plegó en el diffuse
            ' (slot 0). El engine hace softlight(diffuse, detail) SIEMPRE; para que sea IDENTIDAD hay que dejar el slot 3
            ' en gris 0.5 (softlight(x,0.5)=x). ⛔ NO se puede VACIAR el slot 3: el engine rellena un detail vacío con su
            ' default BSShader_DefFacegenDetail = UNIFORME 0x40 = 0.251 (RE byte-level SkyrimSE.exe 0x140E57E30 rellena
            ' 0x40404040; = vanilla blankdetailmap; NO la Bayer 0.1235 de DitheringNoise), NO 0.5 → oscurecería la cara.
            ' Se escribe un detail neutral COMPARTIDO por plugin (constante ⇒ dedup; el engine SÍ
            ' respeta el slot 3 del NIF, a diferencia del tint que arma por path canónico) y se apunta el slot 3 ahí.
            Try
                If ts.Textures.Count > 3 Then
                    Dim tintDir3 = $"Textures\Actors\Character\FaceGenData\FaceTint\{originPlugin}\"
                    Dim detailNeutral = SseFaceGenBaker.NeutralDetailDds(512, 512, DiffuseDxgiFromSetting())
                    If detailNeutral IsNot Nothing Then
                        Dim detLoose = tintDir3 & "facedetailneutral" & suffix           ' _2c/_2/.dds en disco
                        Dim detEmbed = tintDir3 & "facedetailneutral" & embeddedSuffix    ' canónico .dds cuando willBePacked
                        Dim detFile = IO.Path.Combine(Config_App.Current.DataPath, detLoose)
                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(detFile))
                        IO.File.WriteAllBytes(detFile, detailNeutral)
                        ts.Textures(3).Content = EmbeddedEngineTexPath(detEmbed)
                        ' Non-forced (release/loose/packed) usa el detail neutral COMPARTIDO por plugin ⇒ el packer lo
                        ' empaqueta una vez. El forced (_2c, sandbox debug) nunca packea, no marca el flag.
                        If Not forced Then _sseFoldUsedSharedNeutralDetail = True
                    Else
                        ts.Textures(3).Content = ""   ' fallback: sin detail neutral, el engine cae a su default 0.251 (re-oscurece el fold: peor, pero no rompe)
                    End If
                End If
            Catch exD As Exception
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] slot3 detail-neutral failed: {exD.Message}")
                If ts.Textures.Count > 3 Then ts.Textures(3).Content = ""
            End Try

            ' NEUTRALIZAR slot 6: el facetint se plegó en el diffuse → gris neutral (fgTint=1) → engine albedo*=1.
            ' Normal: sobrescribe el _d de facetint (_2) que WriteSseFacetintDds escribió, slot6 sigue ahí.
            ' Forzado (_2c): escribe un facetint neutral SEPARADO (_2c) y apunta slot6 ahí — NO pisa el _2 real.
            Try
                Dim tintDir = $"Textures\Actors\Character\FaceGenData\FaceTint\{originPlugin}\"
                ' Formato = el del Diffuse en CharGen Options (DiffuseDxgiFromSetting: per-game + All/per-layer), NO
                ' hardcodeado. Default SSE = BC3 = el formato vanilla del facetint (medido en Skyrim - Textures0.bsa).
                Dim neutral = SseFaceGenBaker.NeutralFacetintDds(512, 512, DiffuseDxgiFromSetting())
                If forced Then
                    ' El neutral es una CONSTANTE (gris (63,64,63)/255) idéntica para TODOS los NPC. En vez de duplicar
                    ' un DDS por NPC (<id>_2c.dds), se escribe UN ÚNICO archivo COMPARTIDO por plugin y todos los
                    ' _2c.NIF apuntan ahí. Se RE-ESCRIBE en cada bake (no skip-if-exists): si algún día cambiamos el gris,
                    ' el archivo no queda stale. Es un facetint REAL que da fgTint=1 en el juego (seguro, no vacía el slot).
                    Dim sharedNeutral = tintDir & "facetintneutral" & forcedSuffix & ".dds"   ' facetintneutral_2c.dds, compartido
                    Dim ntFile = IO.Path.Combine(Config_App.Current.DataPath, sharedNeutral)
                    IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(ntFile))
                    If neutral IsNot Nothing Then IO.File.WriteAllBytes(ntFile, neutral)
                    If ts.Textures.Count > 6 Then ts.Textures(6).Content = EmbeddedEngineTexPath(sharedNeutral)
                Else
                    ' RELEASE / Save ESP: el facetint se plegó en el diffuse (slot 0) ⇒ el <id>.dds canónico debe quedar
                    ' NEUTRAL (gris (63,64,63)/255 → fgTint≈1) para que el engine multiplique por 1 y no re-aplique tint.
                    ' ⛔ CRÍTICO (RE byte-exacto SkyrimSE.exe, ver reference_sse_engine_facegen_re): el engine IGNORA el
                    ' slot 6 del NIF; SIEMPRE ARMA y CARGA `FaceTint\<plugin>\<id>.dds` canónico (BuildFaceTintPath
                    ' 0x1403B8BB0 → ApplyFaceTintToHeadMaterial 0x1403BC400 → material+0xA0). Si ese archivo NO existe →
                    ' tint = NULL → CARA BROWN y el diffuse plegado (con overlay) queda aplastado. Por eso NO se puede
                    ' borrar `<id>.dds` ni redirigir a un `facetintneutral.dds` compartido (el engine nunca lo mira):
                    ' hay que SOBRESCRIBIR el mismo `<id>.dds` per-NPC (que WriteSseFacetintDds ya escribió con el facetint
                    ' real y al que el slot 6 ya apunta) con el gris neutral. El slot 6 queda igual (a <id>.dds). El packer
                    ' emite Source/Entry = <id>.dds per-NPC para el facetint (nunca dedup, igual que CK escribe un facetint
                    ' por NPC); sólo el detail neutral (slot 3) se comparte. Si NeutralFacetintDds fallara, se deja el
                    ' <id>.dds real intacto (fallback consistente: mejor el facetint real que un archivo faltante).
                    If neutral IsNot Nothing Then
                        Dim perNpcTint = IO.Path.Combine(Config_App.Current.DataPath, tintDir & $"{fgLocal:X8}{suffix}")
                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(perNpcTint))
                        IO.File.WriteAllBytes(perNpcTint, neutral)
                        If ts.Textures.Count > 6 Then ts.Textures(6).Content = EmbeddedEngineTexPath(tintDir & $"{fgLocal:X8}{embeddedSuffix}")
                    End If
                End If
            Catch exN As Exception
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] slot6 neutralize failed: {exN.Message}")
            End Try
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] face diffuse+overlays -> {rel} ({outDds.Length}b, {w}x{h}); facetint slot6 neutralized")

            ' === NORMALES: en el _msn del head (slot 1). Non-forced: SOLO si un overlay aporta normal (compone
            ' decode→lerp cobertura→RENORMALIZE→encode). FORZADO (_2c): SIEMPRE se emite el _n (re-encodea el _msn del
            ' head, con overlays si los hay) para que el replacer sea completo _d+_n. Formato = la propiedad
            ' Setting_FaceGenNormalCompression (NormalDxgiFromSetting), DEFAULT Uncompressed = formato VANILLA del _msn
            ' de SSE (32bpp RGBA8, MEDIDO del BSA) — NO BC7 (los _msn BC7 sueltos son mods; y BC7 crasheaba el encode). ===
            If ts.Textures.Count > 1 AndAlso (forced OrElse SseOverlayCompositor.HasFaceOverlayNormals(overlays)) Then
                Try
                    Dim msnPath = If(forced AndAlso Not String.IsNullOrEmpty(normalPathOverride), normalPathOverride, ts.Textures(1).Content)
                    If Not String.IsNullOrEmpty(msnPath) Then
                        Dim msnBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(msnPath))
                        If msnBytes IsNot Nothing Then
                            Dim mDec = FaceTintCpuCompositor.DecodeDds(msnBytes)
                            If mDec IsNot Nothing AndAlso mDec.Rgba IsNot Nothing AndAlso mDec.Width > 0 AndAlso mDec.Height > 0 Then
                                Dim mw = mDec.Width, mh = mDec.Height
                                Dim macc(mw * mh * 4 - 1) As Single
                                Array.Copy(mDec.Rgba, macc, macc.Length)
                                ' Compone overlay-normals si los hay (in-place). En forced sin overlays queda el head
                                ' normal tal cual → se re-encodea igual (replacer _n self-contained).
                                Dim composedN = SseOverlayCompositor.ComposeFaceOverlayNormalsIntoMsn(macc, overlays, mw, mh, AddressOf SseFaceTintComposer.DecodeTextureRgba)
                                If composedN OrElse forced Then
                                    Dim mbgra(mw * mh * 4 - 1) As Byte
                                    For i = 0 To mw * mh - 1
                                        mbgra(i * 4) = ClampByte255(macc(i * 4 + 2) * 255.0)      ' B
                                        mbgra(i * 4 + 1) = ClampByte255(macc(i * 4 + 1) * 255.0)  ' G
                                        mbgra(i * 4 + 2) = ClampByte255(macc(i * 4) * 255.0)      ' R
                                        mbgra(i * 4 + 3) = ClampByte255(macc(i * 4 + 3) * 255.0)  ' A
                                    Next
                                    ' Resolución = Setting_FaceGenNormalResolution (Inherit→nativo no-op; resample filtro FO4).
                                    Dim nOutW = mw, nOutH = mh, nOutBgra = mbgra
                                    If OutputSettings.Normal <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
                                        Dim t = FaceTintConvention.ResolveResolutionSize(OutputSettings.Normal, Math.Min(mw, mh))
                                        nOutBgra = FaceTintCpuCompositor.ResampleBgra(mbgra, mw, mh, t, t) : nOutW = t : nOutH = t
                                    End If
                                    Dim mmips = CInt(Math.Floor(Math.Log(Math.Min(nOutW, nOutH), 2))) + 1
                                    ' Formato = propiedad Setting_FaceGenNormalCompression. NO hardcodeado.
                                    Dim mDds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                                        width:=nOutW, height:=nOutH, bgraPixels:=nOutBgra,
                                        outputDxgiFormat:=NormalDxgiFromSetting(),
                                        generateMipMaps:=True, generatedMipLevels:=mmips)
                                    If mDds IsNot Nothing Then
                                        Dim ndir = $"Textures\Actors\Character\FaceGenData\FaceNormal\{originPlugin}\"
                                        Dim nRel = ndir & $"{fgLocal:X8}{suffix}"
                                        Dim nFile = IO.Path.Combine(Config_App.Current.DataPath, nRel)
                                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(nFile))
                                        IO.File.WriteAllBytes(nFile, mDds)
                                        MaybeWriteTgaBeside(nFile, nOutW, nOutH, nOutBgra)
                                        ts.Textures(1).Content = EmbeddedEngineTexPath(ndir & $"{fgLocal:X8}{embeddedSuffix}")
                                        Logger.LogLazy(Function() $"[FACEBAKE][SSE] face normal+overlays -> {nRel} ({mDds.Length}b, {mw}x{mh})")
                                    End If
                                End If
                            End If
                        End If
                    End If
                Catch exM As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] face normal bake failed: {exM.GetType().Name}: {exM.Message}")
                End Try
            End If

            ' ⛔ El sandbox _2b del DIFFUSE (overlays por GPU sobre un base plegado en CPU) SE ELIMINÓ: era un camino
            ' CRUZADO (mitad CPU, mitad GPU) y por eso no medía nada que se ejecute de verdad — ningún camino real
            ' mezcla. Los dos sandboxes que quedan son puros y comparables entre sí: _2c = TODO CPU (= el release) y
            ' _2d = TODO GPU. (El _2b del FACETINT sigue: ese sí es un compose puro GPU del facetint.)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] face diffuse+overlays bake failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub


    ''' <summary>Sube un BGRA a una textura GL RGBA8 (linear, clamp). Devuelve 0 si falla. GL-bound.</summary>
    Private Function UploadBgraToGl(bgra As Byte(), w As Integer, h As Integer) As Integer
        Dim id = OpenTK.Graphics.OpenGL4.GL.GenTexture()
        If id = 0 Then Return 0
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, id)
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear))
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear))
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(bgra, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.TexImage2D(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8, w, h, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        Return id
    End Function

    Private Function ClampByte255(v As Double) As Byte
        Return CByte(Math.Max(0.0, Math.Min(255.0, Math.Round(v))))
    End Function

    ''' <summary>Lee el content de los slots 0 (diffuse/complexion) y 1 (normal/_msn) del texture-set del head
    ''' shape. Para capturar los paths ORIGINALES antes de que el bake los mute (sandbox _2c). ("","") si no resuelve.</summary>
    Private Function GetSseHeadSlotPaths(nif As Nifcontent_Class_Manolo, cloned As INiShape) As (Slot0 As String, Slot1 As String, Slot3 As String)
        Try
            Dim spr = cloned.ShaderPropertyRef
            If spr Is Nothing OrElse spr.Index < 0 Then Return ("", "", "")
            Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
            If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then Return ("", "", "")
            Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
            If ts Is Nothing OrElse ts.Textures Is Nothing Then Return ("", "", "")
            Dim s0 = If(ts.Textures.Count > 0, ts.Textures(0).Content, "")
            Dim s1 = If(ts.Textures.Count > 1, ts.Textures(1).Content, "")
            Dim s3 = If(ts.Textures.Count > 3, ts.Textures(3).Content, "")   ' detail/Displacement (softlight)
            Return (s0, s1, s3)
        Catch
            Return ("", "", "")
        End Try
    End Function

    ''' <summary>NPC skin colour (linear RGB [0,1]) for the skee −2 skin-preset. Reuses the SAME QNAM the SSE
    ''' facetint + body use (SseFaceTintComposer.ResolveSkinToneQnam), so a skee mask tinted "skin" matches the
    ''' rest. Nothing when unresolved (BuildSkeeMaskLayer then falls back to the literal colour).</summary>
    Private Function SseSkinRgbForNpc(pluginManager As PluginManager, npcData As NPC_Data, npcFormID As UInteger) As Double()
        Try
            If pluginManager Is Nothing OrElse npcData Is Nothing OrElse npcData.RaceFormID = 0UI Then Return Nothing
            Dim rr = pluginManager.GetRecord(npcData.RaceFormID)
            If rr Is Nothing OrElse rr.Header.Signature <> "RACE" Then Return Nothing
            Dim race = RecordParsers.ParseRACE(rr, pluginManager)
            Dim q = SseFaceTintComposer.ResolveSkinToneQnam(pluginManager, npcData, race, npcData.RaceFormID, npcData.IsFemale)
            If Not q.HasValue Then Return Nothing
            Return New Double() {q.Value.R / 255.0, q.Value.G / 255.0, q.Value.B / 255.0}
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>DXGI del diffuse de salida según el setting del usuario (CharGen Options → Format del Diffuse):
    ''' BC3 (default) / BC7 / Uncompressed (B8G8R8A8). Misma tabla que el bake FO4 (BakeFaceTextures).</summary>
    ''' <summary>Friend (no Private): el RENDER (NpcFaceTintResolver) llama a esta MISMA función para encodear el
    ''' facetint del preview, así el formato del preview y el del bake salen de una sola fuente (el setting del
    ''' usuario) en vez de duplicar la lógica o hardcodear.</summary>
    Friend Function DiffuseDxgiFromSetting() As Integer
        ' Via OutputSettings ⇒ per-game (SSE vs FO4) y All/per-layer aware, como el bake FO4 (BakeFaceTextures).
        Dim os = If(Config_App.Current IsNot Nothing, OutputSettings, Nothing)
        Dim dc = If(os IsNot Nothing, os.DiffuseCompression, FaceTintConvention.FaceTintDiffuseCompression.Bc3)
        Select Case dc
            Case FaceTintConvention.FaceTintDiffuseCompression.Bc7 : Return DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case FaceTintConvention.FaceTintDiffuseCompression.Uncompressed : Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case Else : Return DirectXTextureConversionHelper.DxgiFormatBc3Unorm
        End Select
    End Function

    ''' <summary>DXGI del NORMAL facegen según <see cref="Config_Class.Setting_FaceGenNormalCompression"/>
    ''' (CharGenOptions). Default BC7 = formato vanilla del _msn de SSE (model-space, 3 canales). NO hardcodea.</summary>
    Private Function NormalDxgiFromSetting() As Integer
        ' Via OutputSettings ⇒ per-game + All/per-layer (SSE All: sigue el diffuse; per-layer: Setting_..._SSE).
        Dim os = If(Config_App.Current IsNot Nothing, OutputSettings, Nothing)
        Return NsDxgiFromCompression(If(os IsNot Nothing, os.NormalCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Bc3))
    End Function

    ''' <summary>DXGI de un canal Normal\Specular a partir del enum de compresión. Tabla ÚNICA para los dos canales
    ''' y los dos juegos: los 4 valores del enum se honran (antes el bake FO4 mapeaba sólo Uncompressed-vs-BC5 y
    ''' comía en silencio un BC7/BC3 elegido en CharGen Options).</summary>
    Private Function NsDxgiFromCompression(c As FaceTintConvention.FaceTintNormalSpecularCompression) As Integer
        Select Case c
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc5 : Return DirectXTextureConversionHelper.DxgiFormatBc5Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed : Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc7 : Return DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case Else : Return DirectXTextureConversionHelper.DxgiFormatBc3Unorm   ' Bc3
        End Select
    End Function

    ''' <summary>Record one face-texture bake failure on the BuildResult so the save summary surfaces the
    ''' CAUSE (a silent per-slot catch + "bake OK" otherwise hid it — the user only saw "0/1 packed, N files
    ''' unaccounted"). Accumulates the count and keeps the FIRST detail as the representative message.</summary>
    Private Sub RecordTextureFailure(result As BuildResult, detail As String)
        If result Is Nothing Then Return
        result.TextureSlotsFailed += 1
        If String.IsNullOrEmpty(result.TextureFailureDetail) Then result.TextureFailureDetail = detail
    End Sub

    Private Sub BakeFaceTextures(nif As Nifcontent_Class_Manolo,
                                 cloned As INiShape,
                                 srcNif As Nifcontent_Class_Manolo,
                                 srcShape As INiShape,
                                 hdpt As HDPT_Data,
                                 effectiveHeadPartType As Integer,
                                 applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                 npcFormID As UInteger,
                                 originPlugin As String,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 state As MainForm.NPCVisualState,
                                 willBePacked As Boolean,
                                 result As BuildResult,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing)
        Logger.LogLazy(Function() $"[FACEBAKE] enter npcFormID=0x{npcFormID:X8} originPlugin='{originPlugin}' srcShape='{srcShape?.Name?.ToString()}'")
        ' --- 1. Resolve the face source material (D/N/S texture paths) the SAME way the render does
        ' (TXST/FTST + MNAM-BGSM + tints + palette) — NOT the raw NIF material. For age/FTST NPCs the
        ' FaceTextureSet pisa the head Diffuse (e.g. BaseFemaleHead_d → OldHumanFemaleHead_d); CK and
        ' the live render composite the FaceTint onto THAT resolved head, so the bake must too —
        ' otherwise the baked _d sits on the wrong base head and never byte-matches CK. Same helper
        ' ApplyRenderResolvedMaterialToShape uses to transcribe the .nif2 material (single source of
        ' truth: render base == .nif2 inline == FaceTint compositor base). ---
        Dim mat = ResolveRenderResolvedShapeMaterial(srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides)
        If mat Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: resolved source material is Nothing (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "could not resolve the face material (no D/N/S texture paths)")
            Return
        End If

        Dim diffusePath = mat.Diffuse_or_Base_Texture
        Dim normalPath = mat.NormalTexture
        Dim specPath = mat.SmoothSpecTexture
        If String.IsNullOrEmpty(diffusePath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffusePath empty (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "the face material has no diffuse texture path")
            Return
        End If

        ' --- 2. Resolve the NPC's race + gender so we can build layers + region swaps. ---
        ' Forward the LM SkinTemplate resolver so face TXST overrides from the bundle land here
        ' (template.face[gender] → npcData.HeadTextureFormID), keeping the bake's tint inputs
        ' aligned with what the live render shows.
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(
            npcFormID, pluginManager, appliedPresets, lmSkinTemplateResolver)
        If npcData Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: npcData is Nothing (npcFormID=0x{npcFormID:X8})")
            Return
        End If

        ' Resolve the hair LUT path for slot Brows palette layers via the same BGSM-first /
        ' RACE-fallback rule the live render and the EditFace swatch use. Single source of truth
        ' (MainForm.ResolveHairPaletteTexture). Empty string when neither source resolves -- the
        ' builder then skips the palette branch; RGB-CLFM (HasColor) still works.
        Dim hairLutPathBake As String = NpcMaterialResolver.ResolveHairPaletteTexture(host, state, pluginManager)

        Dim built = FaceTintLayerBuilder.Build(
            modelFormID:=npcFormID,
            rootFormID:=npcFormID,
            raceFormID:=npcData.RaceFormID,
            isFemale:=npcData.IsFemale,
            pluginManager:=pluginManager,
            appliedPresets:=appliedPresets,
            tintBytesCache:=Nothing,
            hairLutPath:=hairLutPathBake,
            hairColorFormID:=state.HairColorFormID,
            hasTextureLighting:=state.HasTextureLighting,
            textureLightingColorArgb:=state.TextureLightingColor.ToArgb())

        ' --- 3. Upload face source D/N/S to GL temporaries (these are the inputs to the pipeline). ---
        Dim diffuseKey = FO4UnifiedMaterial_Class.CorrectTexturePath(diffusePath)
        Dim normalKey = FO4UnifiedMaterial_Class.CorrectTexturePath(normalPath)
        Dim specKey = FO4UnifiedMaterial_Class.CorrectTexturePath(specPath)

        Dim diffuseBytes = TryGetFilesDictionaryBytes(diffuseKey)
        Dim normalBytesArr = TryGetFilesDictionaryBytes(normalKey)
        Dim specBytesArr = TryGetFilesDictionaryBytes(specKey)
        If diffuseBytes Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse bytes not resolved key='{diffuseKey}' (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, $"face diffuse texture not found on disk / in archives: '{diffuseKey}'")
            Return
        End If

        ' FLAGS INDEPENDIENTES:
        '  - CPU = output principal, SIEMPRE (el `cpu` de abajo). Formato + NOMBRE por DebugMode
        '    (release: canonico _d.dds + BCn; debug: _d_2.dds + uncompressed B8G8R8A8). No depende del GL
        '    -> el bake puede correr async (Await Task.Run en el caller).
        '  - WriteGPUSandboxOutput = corre el GL y escribe el _2b (MISMO formato que el CPU, NOMBRE siempre
        '    _2b). INDEPENDIENTE de DebugMode -> needGl = este flag. Como toca GL, el bake DEBE ir sync en el
        '    hilo UI (contexto GL): el caller (MainForm) lo agenda sync cuando WriteGPUSandboxOutput.
        '  - WriteTGASandboxOutput = ademas un TGA UNCOMPRESSED al lado de cada .dds (CPU y, si corrio, GPU),
        '    desde el buffer en memoria (lossless aunque el .dds sea BCn). INDEPENDIENTE de DebugMode (release tambien).
        Dim needGl As Boolean = WriteGPUSandboxOutput
        Dim cpu As FaceTintCpuCompositor.CpuPipelineResult = Nothing

        Try
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(diffuseBytes, normalBytesArr, specBytesArr, built.Layers, built.RegionSwaps, OutputSettings, diffuseKey, normalKey, specKey,
                                                           headDiffuseAlphaTest:=(npcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (npcData.AcbsFlags And &H1000000UI) <> 0UI)
        Catch ex As Exception
            Dim m = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE-CPU] CPU compose failed: {m}")
        End Try

        If (Not needGl) AndAlso (cpu Is Nothing OrElse cpu.Diffuse Is Nothing OrElse cpu.Diffuse.Bgra Is Nothing) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: CPU compose produced no diffuse (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "the CPU compositor produced no diffuse pixels (see [FACEBAKE-CPU] log for the cause)")
            Return
        End If

        Dim tempIds As New List(Of Integer)
        Dim diffEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim normEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim specEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim w As Integer, h As Integer
        If needGl Then
            ' --- GL path (DebugMode): upload source D/N/S a GL para correr el GPU pipeline (escribe _2). ---
            Dim uploadPaths As New List(Of String)
            Dim uploadBytes As New List(Of Byte())
            uploadPaths.Add(diffuseKey) : uploadBytes.Add(diffuseBytes)
            If normalBytesArr IsNot Nothing Then
                uploadPaths.Add(normalKey) : uploadBytes.Add(normalBytesArr)
            End If
            If specBytesArr IsNot Nothing Then
                uploadPaths.Add(specKey) : uploadBytes.Add(specBytesArr)
            End If

            Dim uploaded As Dictionary(Of String, PreviewModel.Texture_Loaded_Class) = Nothing
            Try
                ' srgb=False para TODAS: la base del bake se carga CRUDA (el seed hace srgbToLin, base raw =
                ' baseDiffuseIsLinearOnGpu=False); el decode lo hace el compositor por convención, no el SRV.
                uploaded = DirectXDDSLoader.Load_And_GenerateOpenGLTextures_Memory(
                    uploadPaths.ToArray(), uploadBytes.ToArray(),
                    useCompress:=True, forceOpenGL:=False, Srgb:=New Boolean(uploadPaths.Count - 1) {})
            Catch ex As Exception
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: GL upload threw {ex.GetType().Name}: {ex.Message} (npcFormID=0x{npcFormID:X8})")
                Return
            End Try

            uploaded.TryGetValue(diffuseKey, diffEntry)
            uploaded.TryGetValue(normalKey, normEntry)
            uploaded.TryGetValue(specKey, specEntry)
            If diffEntry IsNot Nothing AndAlso diffEntry.Texture_ID <> 0 Then tempIds.Add(diffEntry.Texture_ID)
            If normEntry IsNot Nothing AndAlso normEntry.Texture_ID <> 0 Then tempIds.Add(normEntry.Texture_ID)
            If specEntry IsNot Nothing AndAlso specEntry.Texture_ID <> 0 Then tempIds.Add(specEntry.Texture_ID)

            If diffEntry Is Nothing OrElse diffEntry.Texture_ID = 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse GL texture id 0 (npcFormID=0x{npcFormID:X8})")
                DeleteGlTextures(tempIds)
                Return
            End If

            w = diffEntry.Size.Width
            h = diffEntry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse size {w}x{h} (npcFormID=0x{npcFormID:X8})")
                DeleteGlTextures(tempIds)
                Return
            End If
        Else
            ' --- CPU-only (release): sin GL. El tamaño sale del resultado CPU. ---
            w = cpu.Diffuse.Width
            h = cpu.Diffuse.Height
        End If

        ' --- 4. GL pipeline (SOLO needGl = DebugMode): region-swap + tint compose en GPU para escribir
        ' el _2 de comparación (vs el _2b del CPU). En RELEASE-CPU NO corre -> no se duplica GPU+CPU y el
        ' bake no toca GL (async). El CPU ya se compuso arriba (cpu). ---
        Dim pipelineResult As FaceTintCompositor.FaceTintPipelineResult = Nothing
        If needGl Then
            pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                host.CompositorState, host.TintGpuCache,
                diffEntry.Texture_ID,
                If(normEntry?.Texture_ID, 0),
                If(specEntry?.Texture_ID, 0),
                w, h,
                built.Layers, built.RegionSwaps,
                OutputSettings)
        End If

        ' Track any fresh textures the pipeline produced so we can delete them on exit. (Nothing en
        ' release-CPU: no hubo GL pipeline.)
        Dim freshIds As New List(Of Integer)
        If pipelineResult IsNot Nothing Then
            If pipelineResult.Diffuse.IsFresh Then freshIds.Add(pipelineResult.Diffuse.TextureId)
            If pipelineResult.Normal.IsFresh Then freshIds.Add(pipelineResult.Normal.TextureId)
            If pipelineResult.Specular.IsFresh Then freshIds.Add(pipelineResult.Specular.TextureId)
        End If

        ' --- 5. Output dir + slot plan + texture-set for slot rewrites. ---
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Dim dataPath = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: Config_App.Current.DataPath empty (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If
        Dim outDir = Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin)
        Try : Directory.CreateDirectory(outDir) : Catch : End Try

        Dim suffixD = If(DebugMode, "_d_2.dds", "_d.dds")
        Dim suffixN = If(DebugMode, "_msn_2.dds", "_msn.dds")
        Dim suffixS = If(DebugMode, "_s_2.dds", "_s.dds")
        ' Formato por canal = SETTINGS (decisión usuario: independiente de DebugMode; DebugMode solo decide
        ' el NOMBRE _2 y si corre el GL). Diffuse: BC3 (default) / BC7 / Uncompressed. N/S: BC5 (default) /
        ' Uncompressed. Uncompressed = B8G8R8A8 (true-color, sin pérdida). Para inspección lossless sin tocar
        ' el formato del .dds está el tilde Generate TGA (WriteTGASandboxOutput).
        Dim os = OutputSettings
        Dim dxgiD As Integer
        Select Case If(os IsNot Nothing, os.DiffuseCompression, FaceTintConvention.FaceTintDiffuseCompression.Bc3)
            Case FaceTintConvention.FaceTintDiffuseCompression.Bc7 : dxgiD = DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case FaceTintConvention.FaceTintDiffuseCompression.Uncompressed : dxgiD = DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case Else : dxgiD = DirectXTextureConversionHelper.DxgiFormatBc3Unorm
        End Select
        ' N/S: los 4 formatos del enum (BC5 default / Uncompressed / BC7 / BC3), no sólo Uncompressed-vs-BC5.
        Dim dxgiN As Integer = NsDxgiFromCompression(If(os IsNot Nothing, os.NormalCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Bc5))
        Dim dxgiS As Integer = NsDxgiFromCompression(If(os IsNot Nothing, os.SpecularCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Bc5))
        ' CanonSuffix = the canonical (non-_2) suffix. The DDS files on disk always use Suffix
        ' (which carries _2 in DebugMode). The suffix embedded INTO the NIF depends on the consumer
        ' (willBePacked), because the two paths reconcile the _2 differently:
        '   willBePacked=True  (Save ESP): NpcFaceGenPacker repacks the _2 loose into a BA2 under
        '       canonical names, so embed CanonSuffix (<id>_d.dds) to match the renamed entries.
        '   willBePacked=False ("Build CharGen (loose)" button): nothing repacks/renames, so embed
        '       the actual on-disk Suffix (<id>_d_2.dds) — otherwise the standalone loose NIF would
        '       reference <id>_d.dds, which does not exist on disk under that name.
        ' In release Suffix already equals CanonSuffix, so willBePacked is a no-op either way.
        ' W/H por canal = el tamaño del RESULTADO del pipeline (= target de resolución del canal, o nativo
        ' si Inherit). Se lee back a ESE tamaño, no al del source (que puede diferir si el enum de
        ' resolución pidió otro tamaño). Fallback al nativo (w/h) si el pipeline no lo seteó (0).
        ' ResultId = textura GL del pipeline (0 en release-CPU). W/H = tamaño del resultado (del pipeline GL
        ' si corrió, sino del resultado CPU; fallback w/h). En CPU-only pipelineResult es Nothing.
        Dim pr = pipelineResult
        Dim slotPlan = New(Slot As Integer, ResultId As Integer, Dxgi As Integer, Suffix As String, CanonSuffix As String, W As Integer, H As Integer)() {
            (0, If(pr IsNot Nothing, pr.Diffuse.TextureId, 0), dxgiD, suffixD, "_d.dds", SlotDim(pr?.Diffuse, cpu?.Diffuse, w, True), SlotDim(pr?.Diffuse, cpu?.Diffuse, h, False)),
            (1, If(pr IsNot Nothing, pr.Normal.TextureId, 0), dxgiN, suffixN, "_msn.dds", SlotDim(pr?.Normal, cpu?.Normal, w, True), SlotDim(pr?.Normal, cpu?.Normal, h, False)),
            (7, If(pr IsNot Nothing, pr.Specular.TextureId, 0), dxgiS, suffixS, "_s.dds", SlotDim(pr?.Specular, cpu?.Specular, w, True), SlotDim(pr?.Specular, cpu?.Specular, h, False))
        }

        Dim bsls = TryCast(nif.GetShader(cloned), BSLightingShaderProperty)
        Dim texset As BSShaderTextureSet = Nothing
        If bsls IsNot Nothing AndAlso bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index <> -1 Then
            texset = TryCast(nif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
        End If
        If texset Is Nothing OrElse texset.Textures Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: cloned shape has no BSShaderTextureSet (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If

        ' ⭐ LEY: el redirect de los slots 0/1/7 a FaceCustomization se gatea por el SHADER TYPE del
        ' MATERIAL DEL SHAPE (Face/FaceTint = 4), NO por HDPT.PartType = Face del record.
        ' RE CreationKit.exe (FO4) `0x140ed9020` = fn de asignación del texture-set FaceCustomization
        ' por-shape:
        '     0x140ed9062 mov rsi,[rbx+0x58]   ; material
        '     0x140ed9075 call [rax+0x28]      ; material.GetType()
        '     0x140ed9078 cmp eax,4            ; Face?
        '     0x140ed907b jne 0x140ed9453      ; << si != 4 -> NO asigna NINGÚN slot (0, 1 ni 7)
        ' (ver memoria project_re_facegen_composite_gate_shadertype).
        '
        ' MEDIDO — DLC04Oswald (DLCNukaWorld.esm 0x0601763B), shape 'DLC04MaleHeadGhoulGlowing':
        '   material = DLC04_GhoulHeadGlowing.BGSM ⇒ Glowmap ⇒ baked type GlowShader (≠ FaceTint).
        '   CK: TX00/01/07 = GhoulMaleHead_d/_n/_s (del TXST resuelto) y TX02 = GhoulMaleHeadGlowing_g
        '   conservado; CERO referencias a FaceCustomization en su NIF. Su HDPT SÍ es PartType=Face,
        '   por eso pasaba nuestro gate del call site; su material NO es Face, por eso el CK lo saltea.
        '
        ' ⚠️ El gate va ACÁ (redirect de slots) y NO en el call site (~:739), por evidencia medida:
        '   el CK SÍ shippeó `0001763B_d.DDS` en el BA2 para ese mismo NPC ⇒ el CK COMPONE y EXPORTA
        '   las texturas (fn de export `0x140ab8760`, sin gate) y sólo se saltea la ASIGNACIÓN al NIF
        '   (`0x140ed9020`, con gate). Apagar el bake entero desde el call site además ya rompió el NIF
        '   una vez (shape sin BSShaderTextureSet propio ⇒ la cara se deduplicaba con otra shape).
        ' Mismo patrón que el fix ya cerrado del lado SSE para el slot 6 (ver WriteSseFacetintDds).
        '
        ' El tipo se lee del shader del shape CLONADO, que ApplyRenderResolvedMaterialToShape ya derivó
        ' de los bools del material resuelto (Glow > Facegen > SkinTint > Hair > Env > Default), que es
        ' la misma ley del bake CK de FO4. No se re-deriva acá para no duplicar la regla.
        ' Camino Skyrim NO afectado: BakeFaceTextures sólo se llama en la rama FO4 del call site.
        Dim shapeShaderType = bsls.ShaderType_SK_FO4
        Dim redirectSlotsToFaceCustomization As Boolean =
            (shapeShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint)
        If Not redirectSlotsToFaceCustomization AndAlso Logger.Enabled Then
            Logger.LogLazy(Function() $"[FACEBAKE] slots 0/1/7 NO redirigidos: shape shType={shapeShaderType} (≠FaceTint) — ley CK 0x140ed9020 (npcFormID=0x{npcFormID:X8})")
        End If

        ' --- 6. Per-slot: readback → encode → write → rewrite slot path → diff vs CK. ---
        For Each entry In slotPlan
            Dim ddW As Integer = entry.W, ddH As Integer = entry.H
            Dim cbSlot As Byte() = CpuBgraForSlot(cpu, entry.Slot)   ' CPU bgra del canal (Nothing si no hay)

            ' GPU readback SOLO needGl (DebugMode) + textura válida. En release-CPU no hay textura -> sin GL.
            Dim gpuBgra As Byte() = Nothing
            If needGl AndAlso entry.ResultId <> 0 Then
                Dim gbuf(ddW * ddH * 4 - 1) As Byte
                Try
                    GL.BindTexture(TextureTarget.Texture2D, entry.ResultId)
                    Dim handle = Runtime.InteropServices.GCHandle.Alloc(gbuf, Runtime.InteropServices.GCHandleType.Pinned)
                    Try
                        GL.GetTexImage(TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject())
                    Finally
                        handle.Free()
                    End Try
                    gpuBgra = gbuf
                Catch ex As Exception
                    Dim slotL = entry.Slot
                    Dim suffixL = entry.Suffix
                    Dim resultIdL = entry.ResultId
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[FACEBAKE-FAIL] GL.GetTexImage slot={slotL}{suffixL} ResultId={resultIdL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                    gpuBgra = Nothing
                End Try
            End If

            ' OUTPUT principal (_d.dds release / _d_2.dds debug): SIEMPRE CPU (el path always-on, byte-exacto a
            ' build_3). El GPU es contingente (solo DumpIntermediates) y va al _2b de comparacion. Fallback a GPU
            ' solo si por algun motivo no hay CPU; si tampoco hay GPU -> skip el slot.
            Dim bgra As Byte() = If(cbSlot, gpuBgra)
            If bgra Is Nothing Then
                Logger.LogLazy(Function() $"[FACEBAKE] slot {entry.Slot}{entry.Suffix}: sin textura (ni CPU ni GPU) — SKIPPED (npcFormID=0x{npcFormID:X8})")
                ' Slot 0 (diffuse) is always expected; its absence is a real failure. Slots 1/7 (normal/spec)
                ' are legitimately absent when the source head has none — don't flag those as failures.
                If entry.Slot = 0 Then RecordTextureFailure(result, $"slot 0{entry.Suffix}: no composed pixels (neither CPU nor GPU produced a diffuse)")
                Continue For
            End If

            ' STORAGE encode del DIFFUSE: el engine almacena la FaceCustomization diffuse en gamma-2.2.
            ' Con el PATH UNICO (ley gen3) el acumulador D ya vive en G22 desde el SEED: el compositor
            ' (FaceTintCompositor.ApplyFaceTintPipeline) convierte el source sRGB->g22 UNA vez, en float,
            ' antes de componer. Por eso aca NO se re-encodea: el bgra leido del resultado YA es g22. N/S
            ' (slot 1/7) son datos lineales y se escriben raw. (Antes el compositor acumulaba en sRGB y
            ' aca se hacia ApplySrgbToGamma22Diffuse byte->byte; eso se movio al seed, en float, sin
            ' quantizar -> byte-comparable a CK / al `_3` de gen3.)

            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(ddW, ddH), 2))) + 1
            Dim ddsBytes As Byte() = Nothing
            ' GATE del encode+escritura del DDS (ver SkipDdsEncode). Se saltea el BCn+mips y el File.Write, y se
            ' cae DIRECTO a la reescritura del slot de abajo — igual que si el encode hubiera salido bien.
            If Not SkipDdsEncode Then
            Try
                ddsBytes = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                    width:=ddW, height:=ddH, bgraPixels:=bgra,
                    outputDxgiFormat:=entry.Dxgi,
                    generateMipMaps:=True, generatedMipLevels:=mipLevels)
            Catch ex As Exception
                Dim slotL = entry.Slot
                Dim suffixL = entry.Suffix
                Dim dxgiL = entry.Dxgi
                ' Report the dims actually passed to the encode (ddW/ddH), not the source dims (w/h).
                Dim wL = ddW
                Dim hL = ddH
                Dim mipsL = mipLevels
                Dim msgL = ex.Message
                Dim typeL = ex.GetType().Name
                Logger.LogLazy(Function() $"[FACEBAKE-FAIL] DDS encode slot={slotL}{suffixL} dxgi={dxgiL} {wL}x{hL} mips={mipsL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                RecordTextureFailure(result, $"{typeL}: {msgL} (encode slot {slotL}{suffixL}, {wL}x{hL}, dxgi={dxgiL})")
                Continue For
            End Try

            Dim outFile = Path.Combine(outDir, $"{formIdLow:X8}{entry.Suffix}")
            Try
                File.WriteAllBytes(outFile, ddsBytes)
                Logger.LogLazy(Function() $"[FACEBAKE] wrote '{outFile}'")
            Catch ex As Exception
                Dim slotW = entry.Slot
                Dim suffixW = entry.Suffix
                Dim msgW = ex.Message
                Logger.LogLazy(Function() $"[FACEBAKE] write FAILED '{outFile}': {msgW}")
                RecordTextureFailure(result, $"could not write the DDS to disk (slot {slotW}{suffixW}): {msgW}")
                Continue For
            End Try
            End If

            ' TGA del CPU: copia UNCOMPRESSED (true-color) al lado del .dds, desde el buffer en memoria
            ' (bgra) -> lossless aunque el .dds sea BCn. Gateado SOLO por WriteTGASandboxOutput
            ' (independiente de DebugMode -> tambien en release). Nombre = el del CPU: {id}_d.tga en
            ' release, {id}_d_2.tga en debug (sigue a entry.Suffix). SOLO si el output queda loose (no se
            ' empaqueta a BA2): el .tga no entra al BA2 y quedaría huérfano. Ver OutputStaysLoose.
            If WriteTGASandboxOutput AndAlso OutputStaysLoose(willBePacked) Then
                Try
                    Dim tgaSuffix = Path.ChangeExtension(entry.Suffix, "tga")
                    Dim outTga = Path.Combine(outDir, $"{formIdLow:X8}{tgaSuffix}")
                    FaceTintCompositor.WriteBgraToTga(outTga, bgra, ddW, ddH)
                    Logger.LogLazy(Function() $"[FACEBAKE] wrote '{outTga}'")
                Catch ex As Exception
                    Dim slotL = entry.Slot
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[FACEBAKE-FAIL] TGA dump slot={slotL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                End Try
            End If

            ' Output GPU (_2b): SOLO si corrio el GL (gpuBgra <> Nothing = WriteGPUSandboxOutput,
            ' independiente de DebugMode). .dds con el MISMO formato que el CPU (entry.Dxgi: BCn en release,
            ' B8G8R8A8 en debug) y NOMBRE SIEMPRE _2b ({id}_d_2b.dds, armado desde CanonSuffix para no
            ' depender del _2 del Suffix). Su TGA (uncompressed, desde gpuBgra) si WriteTGASandboxOutput.
            ' Sirve para diff directo CPU vs GPU al mismo formato. SOLO si el output queda loose (mismo
            ' motivo que el TGA: el packer no mete el _2b -> quedaría huérfano en un BA2 save).
            If gpuBgra IsNot Nothing AndAlso OutputStaysLoose(willBePacked) Then
                Dim slotL2 = entry.Slot
                Try
                    Dim suffix2b = entry.CanonSuffix.Replace(".dds", "_2b.dds")
                    Dim mips2b = CInt(Math.Floor(Math.Log(Math.Min(ddW, ddH), 2))) + 1
                    Dim dds2b = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                        width:=ddW, height:=ddH, bgraPixels:=gpuBgra,
                        outputDxgiFormat:=entry.Dxgi,
                        generateMipMaps:=True, generatedMipLevels:=mips2b)
                    File.WriteAllBytes(Path.Combine(outDir, $"{formIdLow:X8}{suffix2b}"), dds2b)
                    Logger.LogLazy(Function() $"[FACEBAKE-GPU] wrote '{formIdLow:X8}{suffix2b}' slot={slotL2}")
                    If WriteTGASandboxOutput Then
                        FaceTintCompositor.WriteBgraToTga(Path.Combine(outDir, $"{formIdLow:X8}{Path.ChangeExtension(suffix2b, "tga")}"), gpuBgra, ddW, ddH)
                    End If
                Catch ex As Exception
                    Dim m = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE-GPU] _2b write failed slot={slotL2}: {m}")
                End Try
            End If

            ' Gate por shader-type del material (ver bloque de la ley arriba, RE CK 0x140ed9020): si el
            ' shape no es Face/FaceTint el CK NO asigna NINGÚN slot ⇒ el shape conserva las texturas ya
            ' transcriptas por ApplyRenderResolvedMaterialToShape. El DDS de arriba SÍ se compuso y
            ' escribió, igual que el CK (que shippeó 0001763B_d.DDS para Oswald sin referenciarlo).
            If Not redirectSlotsToFaceCustomization Then Continue For

            Dim embeddedSuffix = If(willBePacked, entry.CanonSuffix, entry.Suffix)
            ' Full "Data\Textures\..." prefix, matching CK vanilla exactly (CK's loose FaceGen renders
            ' fine with this prefix — verified 2026-05-25 — so the prefix is NOT the loose-breaker).
            Dim canonicalNifPath = $"Data\Textures\Actors\Character\FaceCustomization\{originPlugin}\{formIdLow:X8}{embeddedSuffix}"
            While texset.Textures.Count <= entry.Slot
                texset.Textures.Add(New NiflySharp.NiString4 With {.Content = ""})
            End While
            If texset.Textures(entry.Slot) Is Nothing Then
                texset.Textures(entry.Slot) = New NiflySharp.NiString4 With {.Content = canonicalNifPath}
            Else
                texset.Textures(entry.Slot).Content = canonicalNifPath
            End If


        Next

        ' --- 7. Cleanup. Delete the source temporaries we uploaded AND any fresh outputs the
        ' pipeline generated. The pipeline already deleted any intermediate fresh IDs; here we
        ' only own the explicit "kept" outputs (Diffuse/Normal/Specular .TextureId where
        ' IsFresh=True) and the uploaded source IDs. Source IDs that were also returned
        ' verbatim as outputs (IsFresh=False) get deleted via tempIds — they are NOT in
        ' freshIds.
        DeleteGlTextures(tempIds)
        DeleteGlTextures(freshIds)
    End Sub

    ''' <summary>Read raw DDS bytes from FilesDictionary; returns Nothing on miss / empty / IO error.</summary>
    Private Function TryGetFilesDictionaryBytes(normalizedKey As String) As Byte()
        If String.IsNullOrEmpty(normalizedKey) Then Return Nothing
        Try
            Dim bytes = FilesDictionary_class.GetBytes(normalizedKey)
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE-FAIL] FilesDictionary.GetBytes returned empty for key='{normalizedKey}'")
                Return Nothing
            End If
            Return bytes
        Catch ex As Exception
            Dim keyL = normalizedKey
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] FilesDictionary.GetBytes threw for key='{keyL}': {typeL}: {msgL}")
            Return Nothing
        End Try
    End Function

    Private Sub DeleteGlTextures(ids As List(Of Integer))
        If ids Is Nothing Then Return
        For Each id In ids
            If id = 0 Then Continue For
            Try : GL.DeleteTexture(id) : Catch : End Try
        Next
        ids.Clear()
    End Sub

    ''' <summary>Tamaño (W si isWidth, sino H) del slot: del canal del pipeline GL si tiene (>0), sino del
    ''' canal CPU, sino el fallback (nativo). Sirve para el readback/encode en GL y CPU por igual.</summary>
    Private Function SlotDim(pl As FaceTintCompositor.FaceTintPipelineChannelResult, cpuCh As FaceTintCpuCompositor.CpuChannelResult, fallback As Integer, isWidth As Boolean) As Integer
        If pl IsNot Nothing Then
            Dim v = If(isWidth, pl.Width, pl.Height)
            If v > 0 Then Return v
        End If
        If cpuCh IsNot Nothing Then
            Dim v = If(isWidth, cpuCh.Width, cpuCh.Height)
            If v > 0 Then Return v
        End If
        Return fallback
    End Function

    ''' <summary>BGRA del canal del resultado CPU para un slot del bake (0=Diffuse, 1=Normal, 7=Specular).
    ''' Nothing si el resultado o el canal son Nothing.</summary>
    Private Function CpuBgraForSlot(cpu As FaceTintCpuCompositor.CpuPipelineResult, slot As Integer) As Byte()
        If cpu Is Nothing Then Return Nothing
        Dim ch As FaceTintCpuCompositor.CpuChannelResult
        Select Case slot
            Case 1 : ch = cpu.Normal
            Case 7 : ch = cpu.Specular
            Case Else : ch = cpu.Diffuse
        End Select
        Return If(ch Is Nothing, Nothing, ch.Bgra)
    End Function

    ''' <summary>Log the shader-inline + related-material lighting fields for a shape, tagged
    ''' with a stage label (e.g. "SOURCE-LOAD" right after Load_Manolo). Used to track where
    ''' material values diverge across the bake pipeline: load → resolver → TXST.MNAM swap →
    ''' MSWP swap → final embed. Comparing the same shape's tag-by-tag lines tells us which
    ''' stage mutated each field.</summary>

End Module
