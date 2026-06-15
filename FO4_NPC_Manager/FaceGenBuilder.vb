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

    Private Function PartTypeName(partType As Integer) As String
        Select Case partType
            Case PartTypeMisc : Return "Misc"
            Case PartTypeFace : Return "Face"
            Case PartTypeEyes : Return "Eyes"
            Case PartTypeHair : Return "Hair"
            Case PartTypeFacialHair : Return "Facial Hair"
            Case PartTypeScar : Return "Scar"
            Case PartTypeEyebrows : Return "Eyebrows"
            Case PartTypeMeatcaps : Return "Meatcaps"
            Case PartTypeTeeth : Return "Teeth"
            Case PartTypeHeadRear : Return "Head Rear"
            Case Else : Return $"<unknown {partType}>"
        End Select
    End Function

    ''' <summary>FormID local del FaceGen segun convencion CK. Full plugins: stripear el high byte
    ''' (&amp; 0xFFFFFF). ESL/light plugins (high byte 0xFE): stripear TAMBIEN el light-slot de 12 bits,
    ''' dejando solo el record de 12 bits (&amp; 0xFFF). CK nombra el NIF FaceGen y las texturas
    ''' FaceCustomization con este id enmascarado, zero-padded a 8 hex. Verificado: NPC ESL runtime
    ''' 0xFE032800 -> CK escribe "00000800" (record 0x800), NO "00032800". Bug previo: usaba &amp; 0xFFFFFF
    ''' para todos, dejando el light-slot del ESL en el nombre -> el juego no encontraba la textura.</summary>
    Private Function FaceGenLocalId(npcFormID As UInteger) As UInteger
        If (npcFormID >> 24) = &HFEUI Then Return npcFormID And &HFFFUI
        Return npcFormID And &HFFFFFFUI
    End Function

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
        Dim formIdLow = FaceGenLocalId(npcFormID)
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
    End Class

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
    Public Property WriteGPUSandboxOutput As Boolean = False
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
            Dim d = c.Setting_FaceGenDiffuseResolution
            Dim perLayer = c.Setting_FaceGenPerLayerResolution
            Dim dc = c.Setting_FaceGenDiffuseCompression
            Return New FaceTintConvention.FaceTintResolutionSettings With {
                .Diffuse = d,
                .Normal = If(perLayer, c.Setting_FaceGenNormalResolution, d),
                .Specular = If(perLayer, c.Setting_FaceGenSpecularResolution, d),
                .DiffuseCompression = dc,
                .NormalCompression = If(perLayer, c.Setting_FaceGenNormalCompression, NsCompressionFromDiffuse(dc)),
                .SpecularCompression = If(perLayer, c.Setting_FaceGenSpecularCompression, NsCompressionFromDiffuse(dc))
            }
        End Get
    End Property

    ''' <summary>Modo All: N/S siguen al Diffuse -> Uncompressed si el Diffuse es Uncompressed, sino BC5.</summary>
    Private Function NsCompressionFromDiffuse(d As FaceTintConvention.FaceTintDiffuseCompression) As FaceTintConvention.FaceTintNormalSpecularCompression
        Return If(d = FaceTintConvention.FaceTintDiffuseCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Bc5)
    End Function

    ''' <summary>True si el output del bake queda LOOSE en disco (no se empaqueta a un BA2): Build CharGen
    ''' loose (Not willBePacked) o Save ESP en modo loose-only (NPC_Config.Ba2Version_FO4 = 0). Los
    ''' artefactos de inspección (TGA, _2b) SOLO se escriben en este caso: el packer (NpcFaceGenPacker) mete
    ''' únicamente NIF + 3 DDS por nombre, así que un .tga/_2b en un BA2-save quedaría huérfano loose.</summary>
    Private Function OutputStaysLoose(willBePacked As Boolean) As Boolean
        If Not willBePacked Then Return True
        Return NPC_Config.Current Is Nothing OrElse NPC_Config.Current.Ba2Version_FO4 = 0
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
        If DebugMode Then
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
                .TextureLightingColor = npcData.TextureLightingColor
            }
            state.HeadPartFormIDs.AddRange(npcData.HeadPartFormIDs)
            ' Engine race fallbacks: NPC.WNAM=0 → RACE.SkinFormID, NPC head parts/texture/hair
            ' → RACE defaults, NPC.MWGT sentinel substitution. Same path the render uses; without
            ' it ResolveActorSkinTextureSet returns Nothing for NPCs that leave WNAM=0 (e.g.
            ' vanilla children) and the bake falls through to HDPT.TNAM, which for ChildHeadRear
            ' is hardcoded SkinBodyChildMale — wrong for female actors.
            MainForm.ApplyRaceFallbacks(state, MainForm.CreateOwnTraitsState(npcData), pluginManager)
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
            nif.Create(NiVersion.GetFO4(), withRootNode:=False)
            Dim faceRoot As New NiflySharp.Blocks.NiNode() With {
                .Name = New NiflySharp.NiStringRef(""),
                .Flags_ui = &H400EUI,
                .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
            }
            nif.AddBlock(faceRoot)
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
            result.Summary = "Raza sin FaceGen (perro/criatura/robot/feral-ghoul/etc.) — skipped, no NIF."
            Return result
        End If

        ' Build the canonical HDPT chain for this NPC. Each entry has its MeshPath and (later)
        ' chargen TRI / FMRS info. This is the AUTHORITATIVE list — the .nif2 contains exactly
        ' the shapes that come out of these sources.
        Dim hdptMap = BuildAllowedShapeMap(npcFormID, pluginManager)

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

        ' --- ITERATION 3: build the FaceGen bake state (NPC overlay + race morph defs +
        ' FMRS pose). Single source of truth, consumed by FaceGenBuildPipeline.BakeShape per
        ' HDPT to produce v_baked = inv(Mtot_orig) × v_world.
        Dim regionsFile As FacialBoneRegionsFile = Nothing
        Dim probeNpcRaw = NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager)
        If probeNpcRaw IsNot Nothing AndAlso probeNpcRaw.RaceFormID <> 0UI Then
            Dim raceRec = pluginManager.GetRecord(probeNpcRaw.RaceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                Dim raceProbe = RecordParsers.ParseRACE(raceRec, pluginManager)
                regionsFile = MainForm.GetFacialBoneRegionsForRace(raceProbe, probeNpcRaw.IsFemale)
            End If
        End If
        Dim bakeState As FaceGenBuildPipeline.BakeState =
            FaceGenBuildPipeline.BuildBakeState(npcFormID, pluginManager, appliedPresets, regionsFile)
        ' Names of every bone the actor's face + body skeletons expose. Used below
        ' to drop source shapes whose skin references a bone outside this set
        ' (CK-equivalent filter — see the call site for the rationale).
        Dim actorBoneNames As HashSet(Of String) = FaceGenBuildPipeline.GetActorBoneNames(bakeState)
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
                If actorBoneNames IsNot Nothing AndAlso actorBoneNames.Count > 0 Then
                    Try
                        Dim sti = TryCast(srcShape, NiflySharp.Blocks.BSTriShape)
                        If sti IsNot Nothing AndAlso sti.SkinInstanceRef IsNot Nothing AndAlso sti.SkinInstanceRef.Index >= 0 Then
                            Dim srcSi = TryCast(srcNif.Blocks(sti.SkinInstanceRef.Index), NiflySharp.Blocks.BSSkin_Instance)
                            If srcSi IsNot Nothing AndAlso srcSi.Bones IsNot Nothing Then
                                For bi As Integer = 0 To srcSi.Bones.Count - 1
                                    Dim bRef = srcSi.Bones.GetBlockRef(bi)
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
                        End If
                    Catch ex As Exception
                    End Try
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
                        If hdpt.PartType = PartTypeFace AndAlso host IsNot Nothing AndAlso state IsNot Nothing Then
                            BakeFaceTextures(nif, cloned, srcNif, srcShape,
                                             hdpt, effectiveHeadPartType, applyMaterialOverrides,
                                             npcFormID, originPlugin,
                                             pluginManager, appliedPresets, host,
                                             state, willBePacked,
                                             lmSkinTemplateResolver)
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
                                    ' Insert "_faceBones" before the trailing ":N" suffix (or at end if absent).
                                    Dim colonIdx = sourceName.LastIndexOf(":"c)
                                    Dim faceBonesName As String
                                    If colonIdx > 0 Then
                                        faceBonesName = String.Concat(sourceName.AsSpan(0, colonIdx), "_faceBones", sourceName.AsSpan(colonIdx))
                                    Else
                                        faceBonesName = sourceName & "_faceBones"
                                    End If
                                    For Each fs In fbnsShapes
                                        If String.Equals(If(fs.Name?.String, ""), faceBonesName, StringComparison.OrdinalIgnoreCase) Then
                                            fbnsShape = fs : Exit For
                                        End If
                                    Next
                                End If
                                If fbnsShape Is Nothing AndAlso fbnsShapes.Count = 1 Then
                                    fbnsShape = fbnsShapes(0)
                                End If
                                If fbnsShape IsNot Nothing Then
                                    Dim baked = FaceGenBuildPipeline.BakeShape(bakeState, nif, cloned, fbnsNif, fbnsShape, hdpt.ChargenMorphTriPath, srcNif:=srcNif, srcShape:=srcShape)
                                    If baked Then
                                        shapesMorphed += 1
                                    End If
                                Else
                                End If
                            End If
                        End If
                    Else
                    End If
                Catch ex As Exception
                End Try
            Next
            hdptProcessed += 1
        Next

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
                Next

                Dim droppedOrphanBones As Integer = 0
                For Each childIdx In faceGenRoot.Children.Indices.ToList()
                    Dim childBlk = nif.GetBlock(childIdx)
                    If TypeOf childBlk Is INiShape Then
                        shapeChildIdx.Add(childIdx)
                        Dim triShape = TryCast(childBlk, NiflySharp.Blocks.BSTriShape)
                        If triShape IsNot Nothing Then
                            ' #2 BoundingSphere → (0,0,0,0) como CK: el FaceGen vanilla deja la esfera
                            ' en cero y el engine computa los bounds del skinned desde los huesos.
                            ' Nosotros calculábamos valores reales (deriva de culling). Igualar a CK.
                            triShape.Bounds = New NiflySharp.Structs.BoundingSphere(System.Numerics.Vector3.Zero, 0.0F)
                            ' #1 skin.SkeletonRoot → BSFaceGenNiNodeSkinned. CK apunta el SkeletonRoot
                            ' (NiBlockPtr) del BSSkin::Instance al nodo skinned; nosotros lo dejábamos
                            ' null(-1). Lo seteamos al índice del nodo creado arriba.
                            Dim skinRef = triShape.SkinInstanceRef
                            If skinRef IsNot Nothing AndAlso skinRef.Index >= 0 AndAlso skinRef.Index < nif.Blocks.Count Then
                                Dim si = TryCast(nif.Blocks(skinRef.Index), NiflySharp.Blocks.BSSkin_Instance)
                                If si IsNot Nothing Then
                                    si.SkeletonRoot = New NiflySharp.NiBlockPtr(Of NiflySharp.Blocks.NiAVObject)(skinnedIdx)
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

        result.ShapesKept = shapesCloned
        result.ShapesDropped = 0

        ' Output path:
        '   DebugMode=False (default): <formID>.nif → pisa el CK bake; engine usa este al cargar.
        '   DebugMode=True: <formID>_2.nif → sandbox al lado del CK bake, sin pisar; engine
        '                   sigue usando el CK; el comparator diff-ea against CK BA2 baseline.
        Dim formIdLow = FaceGenLocalId(npcFormID)
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

        result.Success = True
        result.OutputPath = outAbs
        result.Summary = $"Wrote {outAbs} ({result.ShapesKept} shapes from {hdptProcessed} HDPTs)"
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
    ''' Seeds from <see cref="HeadPartResolver.MergeHeadPartsWithRaceDefaults"/> (NPC.PNAM ∪ RACE
    ''' defaults per PartType, NPC override wins; freestanding type=0 misc accumulated) — same
    ''' merge the render path uses, so RACE-default head parts like FemaleNeckGore (Meatcaps)
    ''' are included even when NPC_.PNAM doesn't list them. Then expands recursively via
    ''' HDPT.ExtraPartFormIDs (HNAM) so technical sub-parts (Lashes/AO/Wet, Hairlines, etc.)
    ''' that vanilla HDPTs reference internally are also allowed. Match is case-insensitive.
    '''
    ''' Returning the HDPT_Data per name (not just the name) gives downstream the MeshPath,
    ''' RaceMorphTriPath, ChargenMorphTriPath, PartType, etc. needed to construct each shape
    ''' from records (iteration 1+ replaces "copy from baked" with "load from MeshPath").</summary>
    Private Function BuildAllowedShapeMap(npcFormID As UInteger,
                                          pluginManager As PluginManager) As Dictionary(Of String, HeadPartResolver.HdptChainEntry)
        Dim allowed As New Dictionary(Of String, HeadPartResolver.HdptChainEntry)(StringComparer.OrdinalIgnoreCase)
        Dim npcRec = pluginManager.GetRecord(npcFormID)
        If npcRec Is Nothing Then Return allowed
        Dim npc = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pluginManager)
        If npc Is Nothing Then Return allowed

        ' Use the shared resolver (NPC.PNAM ∪ RACE defaults per PartType + freestanding misc).
        Dim mergedRoots = HeadPartResolver.MergeHeadPartsWithRaceDefaults(
            npc.RaceFormID, npc.IsFemale, npc.HeadPartFormIDs, pluginManager)

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

    ' Biped object slot bits (mismas que MainForm: bit n = slot 30+n). Sólo las que la oclusión
    ' de head parts usa. Slot 31 HairLong = 0x2, 32 FaceGenHead = 0x4, 48 Beard = 0x40000,
    ' 49 Mouth = 0x80000.
    Private Const BakeSlotBitHairLong As UInteger = &H2UI
    Private Const BakeSlotBitFaceGenHead As UInteger = &H4UI
    Private Const BakeSlotBitBeard As UInteger = &H40000UI
    Private Const BakeSlotBitMouth As UInteger = &H80000UI

    ''' <summary>Slots de headwear cubiertos por la DEFAULT OUTFIT (OTFT) del NPC, de forma
    ''' DETERMINISTA. Devuelve (slots, hasLVLI):
    '''   • slots = unión de los biped slots de cada ARMO directamente referenciada por el OTFT
    '''     (resolviendo la cadena de templates CNAM), uniendo por cada ARMO su SlotMask y el
    '''     EffectiveArmaSlotMask de cada ARMA (arma.SlotMask si != 0, sino armo.SlotMask) —
    '''     misma semántica que el render (MainForm.EffectiveArmaSlotMask / CollectArmoCandidates).
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

        For Each itemFID In otft.ItemFormIDs
            If itemFID = 0UI Then Continue For
            Dim itemRec = pluginManager.GetRecord(itemFID)
            If itemRec Is Nothing Then Continue For
            Select Case itemRec.Header.Signature
                Case "LVLI"
                    ' Randomized head piece → non-deterministic. El caller saltea la oclusión.
                    hasLVLI = True
                Case "ARMO"
                    ' ARMO determinista: aporta sus slots (resolviendo template CNAM → terminal).
                    Dim terminalFID = OutfitResolver.ResolveTerminalArmorFormID(itemFID, pluginManager)
                    If terminalFID = 0UI Then Continue For
                    Dim armoRec = pluginManager.GetRecord(terminalFID)
                    If armoRec Is Nothing OrElse armoRec.Header.Signature <> "ARMO" Then Continue For
                    Dim armo = RecordParsers.ParseARMO(armoRec, pluginManager)
                    slots = slots Or armo.SlotMask
                    For Each armaFID In armo.ArmorAddonFormIDs
                        If armaFID = 0UI Then Continue For
                        Dim armaRec = pluginManager.GetRecord(armaFID)
                        If armaRec Is Nothing OrElse armaRec.Header.Signature <> "ARMA" Then Continue For
                        Dim arma = RecordParsers.ParseARMA(armaRec, pluginManager)
                        slots = slots Or If(arma.SlotMask <> 0UI, arma.SlotMask, armo.SlotMask)
                    Next
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
            .HeadPartColorFormID = hdpt.ColorFormID,
            .UseSolidTint = (hdpt.ColorFormID <> 0UI)
        }

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
                NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bsls, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
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
                NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bes, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
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
            Return
        End If

        Dim diffusePath = mat.Diffuse_or_Base_Texture
        Dim normalPath = mat.NormalTexture
        Dim specPath = mat.SmoothSpecTexture
        If String.IsNullOrEmpty(diffusePath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffusePath empty (npcFormID=0x{npcFormID:X8})")
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
        Dim hairLutPathBake As String = MainForm.ResolveHairPaletteTexture(host, state, pluginManager)

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
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(diffuseBytes, normalBytesArr, specBytesArr, built.Layers, built.RegionSwaps, OutputSettings, diffuseKey, normalKey, specKey)
        Catch ex As Exception
            Dim m = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE-CPU] CPU compose failed: {m}")
        End Try

        If (Not needGl) AndAlso (cpu Is Nothing OrElse cpu.Diffuse Is Nothing OrElse cpu.Diffuse.Bgra Is Nothing) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: CPU compose produced no diffuse (npcFormID=0x{npcFormID:X8})")
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
                uploaded = DirectXDDSLoader.Load_And_GenerateOpenGLTextures_Memory(
                    uploadPaths.ToArray(), uploadBytes.ToArray(),
                    useCompress:=True, forceOpenGL:=False)
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
        Dim formIdLow = FaceGenLocalId(npcFormID)
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
        Dim dxgiN As Integer = If(os IsNot Nothing AndAlso os.NormalCompression = FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed,
                                  DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm, DirectXTextureConversionHelper.DxgiFormatBc5Unorm)
        Dim dxgiS As Integer = If(os IsNot Nothing AndAlso os.SpecularCompression = FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed,
                                  DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm, DirectXTextureConversionHelper.DxgiFormatBc5Unorm)
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
                Continue For
            End Try

            Dim outFile = Path.Combine(outDir, $"{formIdLow:X8}{entry.Suffix}")
            Try
                File.WriteAllBytes(outFile, ddsBytes)
                Logger.LogLazy(Function() $"[FACEBAKE] wrote '{outFile}'")
            Catch ex As Exception
                Logger.LogLazy(Function() $"[FACEBAKE] write FAILED '{outFile}': {ex.Message}")
                Continue For
            End Try

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
