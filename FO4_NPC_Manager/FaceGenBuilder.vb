Imports System.IO
Imports System.Text
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

    ''' <summary>When True the bake writes to "_2.nif" / "_2.dds" sandboxed paths so the engine
    ''' keeps using CK's authoritative output, AND emits the full per-shape diff vs CK BA2 bake
    ''' (THREEWAY, TEXSRC, RENDERDIFF, MAT-DIAG, POST-SAVE-EMBED). Use during development to
    ''' compare our output against CK byte-by-byte.
    '''
    ''' When False (default — release mode) the bake writes "&lt;FormID&gt;.nif" / "&lt;FormID&gt;_d.dds"
    ''' directly into the loose folder (clobbers CK's bake; the engine picks ours up). All
    ''' diff/dump output is skipped because there is no longer a CK reference to compare
    ''' against — we BECAME the bake. Logging that depends on the verbose pipeline ([BUILDCHARGEN-*])
    ''' is also skipped to keep the log small.</summary>
    Public Property DebugMode As Boolean = False

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

    ''' <summary>Resolve the FaceGen NIF path the engine would load for this NPC. Path layout
    ''' is "Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;origin plugin filename&gt;\&lt;FormID8hex&gt;.nif"
    ''' where origin plugin is the master that owns this FormID — high-byte of the global
    ''' FormID resolved through PluginManager.GetOriginatingPluginName (which handles ESL
    ''' FE prefix correctly via record SourcePluginName).</summary>
    Public Function ResolveFaceGenPath(npcFormID As UInteger, pluginManager As PluginManager) As String
        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then Return ""
        Dim formIdLow = (npcFormID And &HFFFFFFUI)
        Return $"Meshes\Actors\Character\FaceGenData\FaceGeom\{originPlugin}\{formIdLow:X8}.nif"
    End Function

    ''' <summary>Result of a BuildCharGen run.</summary>
    Public Class BuildResult
        Public Property Success As Boolean
        ''' <summary>Where the .nif2 was written (only when Success). Empty otherwise.</summary>
        Public Property OutputPath As String = ""
        ''' <summary>One-line user-facing summary suitable for a MessageBox.</summary>
        Public Property Summary As String = ""
        Public Property ShapesKept As Integer
        Public Property ShapesDropped As Integer
        ''' <summary>Result of comparing the generated .nif2 against the BA2 baked NIF.
        ''' Nothing if the build failed before reaching the compare step.</summary>
        Public Property Compare As FaceGenComparator.CompareReport
    End Class

    ''' <summary>Build a baked FaceGen NIF for this NPC. See module-level summary for the
    ''' v0 strategy. Always also writes a structured dump to npc_preview.log so the user
    ''' can review the kept/dropped decision per shape. Returns BuildResult; on failure
    ''' OutputPath is empty and Summary explains why.</summary>
    ''' <summary>Delegate matching the signature of <c>MainForm.ApplyShapeMaterialOverrides</c>.
    ''' BuildCharGen invokes this with a one-element shape list to resolve the material for the
    ''' NPC being baked — same code path the live render uses, no preview dependency.</summary>
    Friend Delegate Sub ApplyShapeMaterialOverridesDelegate(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, shapes As IEnumerable(Of IRenderableShape))

    Friend Function BuildCharGen(npcFormID As UInteger,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing) As BuildResult

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
        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager),
            npcFormID, appliedPresets, pluginManager, lmSkinTemplateResolver)
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
        Dim sb As New StringBuilder()
        sb.AppendLine($"[BUILDCHARGEN] === NPC FormID={npcFormID:X8} ===")

        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        sb.AppendLine($"[BUILDCHARGEN] origin plugin: '{originPlugin}'")
        If String.IsNullOrEmpty(originPlugin) Then
            NpcPreviewLog.Log(sb.ToString())
            result.Summary = "Could not resolve origin plugin for this NPC."
            Return result
        End If

        Dim faceGenPath = ResolveFaceGenPath(npcFormID, pluginManager)
        sb.AppendLine($"[BUILDCHARGEN] facegen path: '{faceGenPath}'")

        ' Try to load the vanilla FaceGen if it exists. Two reasons:
        '   - DebugMode: provides a reference to compare our output against (FaceGenComparator,
        '     DumpHdptThreeWay/TextureSources, ProbeFaceCustomizationDdsFormats, RenderVsBaked).
        '   - Release: not needed at all — we build the .nif2 from scratch and become the bake.
        '
        ' For NPCs with the IsCharGenFacePreset ACBS flag (player-spouse, chargen presets) CK
        ' never produced a FaceGen, so vanillaBytes will be Nothing — that's expected and not
        ' an error. The bake proceeds with a freshly created NIF.
        Dim vanillaBytes As Byte() = Nothing
        Try
            vanillaBytes = FilesDictionary_class.GetBytes(faceGenPath)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN] FilesDictionary.GetBytes threw: {ex.GetType().Name}: {ex.Message}")
        End Try
        If vanillaBytes Is Nothing OrElse vanillaBytes.Length = 0 Then
            sb.AppendLine("[BUILDCHARGEN] no vanilla FaceGen present in BA2/loose — building from scratch (expected for IsCharGenFacePreset NPCs)")
            vanillaBytes = Nothing
        Else
            sb.AppendLine($"[BUILDCHARGEN] vanilla FaceGen present: {vanillaBytes.Length} bytes (used for diff-vs-CK reference only)")
        End If

        ' Build a fresh FO4 NIF — same path OutfitStudio takes when importing OBJ/FBX without
        ' a base mesh ([OutfitProject.cpp:515-531] calls workNif.Create(NiVersion::getFO4())).
        ' NiVersion.GetFO4() = (V20_2_0_7, user=12, stream=130), the canonical FO4 framing CK
        ' writes. withRootNode=True drops in the root NiNode the engine expects.
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Create(NiVersion.GetFO4(), withRootNode:=True)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN] NIF Create threw: {ex.GetType().Name}: {ex.Message}")
            NpcPreviewLog.Log(sb.ToString())
            result.Summary = $"Failed to create FaceGen NIF shell: {ex.Message}"
            Return result
        End Try
        sb.AppendLine("[BUILDCHARGEN] created blank FO4 NIF shell (V20_2_0_7 / user=12 / stream=130)")

        ' Diagnostic dump of the NPC's HeadParts (the build target).
        DumpNpcHeadParts(npcFormID, pluginManager, sb)

        ' Build the canonical HDPT chain for this NPC. Each entry has its MeshPath and (later)
        ' chargen TRI / FMRS info. This is the AUTHORITATIVE list — the .nif2 contains exactly
        ' the shapes that come out of these sources. We do NOT consult the baked NIF's shape
        ' list anymore; the baked NIF is reference for the comparator only.
        Dim hdptMap = BuildAllowedShapeMap(npcFormID, pluginManager, sb)
        sb.AppendLine($"[BUILDCHARGEN] resolved HDPT chain ({hdptMap.Count}): {String.Join(", ", hdptMap.Keys.OrderBy(Function(s) s))}")

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
        ' Render-vs-baked comparison stash. For every shape that BakeShape successfully processed
        ' we keep its v_world + the face/body skel pair so we can re-skin OURS and CK with bind-
        ' only resolvers after the .nif2 is written. Keyed by destination shape name.
        Dim renderStash As New Dictionary(Of String, (vWorld As Vector3d(), faceSkel As SkeletonInstance, bodySkel As SkeletonInstance))(StringComparer.OrdinalIgnoreCase)
        Dim hdptProcessed As Integer = 0
        Dim hdptSourceMissing As Integer = 0
        Dim hdptSourceLoadFail As Integer = 0
        Dim shapesCloned As Integer = 0
        Dim shapesSkippedDup As Integer = 0
        Dim shapesMorphed As Integer = 0

        ' Reload a separate copy of the baked NIF as DIAGNOSTIC reference (debug only).
        ' Pure observation — never mutated. Skipped in release: we're going to overwrite the
        ' BA2-baked reference anyway, the diff has no audience. Also skipped when no vanilla
        ' bake exists (IsCharGenFacePreset NPCs) — there's nothing to compare against.
        Dim bakedRefNif As Nifcontent_Class_Manolo = Nothing
        If DebugMode AndAlso vanillaBytes IsNot Nothing Then
            Try
                bakedRefNif = New Nifcontent_Class_Manolo()
                bakedRefNif.Load_Manolo(vanillaBytes)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN] failed to load baked diagnostic copy: {ex.Message}")
                bakedRefNif = Nothing
            End Try
        End If

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
        sb.AppendLine($"[BUILDCHARGEN] bake state: built={bakeState IsNot Nothing} regions={regionsFile IsNot Nothing} fmrsPose={(bakeState?.FmrsPose IsNot Nothing)}")
        For Each kv In hdptMap.OrderBy(Function(p) p.Value.PartType).ThenBy(Function(p) p.Key)
            Dim hdptName = kv.Key
            Dim hdpt = kv.Value
            sb.AppendLine($"[BUILDCHARGEN] HDPT '{hdptName}' (PartType={hdpt.PartType}) MeshPath='{hdpt.MeshPath}'")
            If String.IsNullOrEmpty(hdpt.MeshPath) Then
                sb.AppendLine($"[BUILDCHARGEN]   skipped: HDPT has no MeshPath")
                hdptSourceMissing += 1
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
            sb.AppendLine($"[BUILDCHARGEN]   using base mesh (CK bake matches ORIG bone palette): '{sourceKey}'")
            If faceBonesKey <> "" Then
                sb.AppendLine($"[BUILDCHARGEN]   note: _facebones variant exists at '{faceBonesKey}' but is NOT used for clone (only for diagnostic)")
            End If

            ' Three-way diagnostic dump (original / _facebones / baked) per HDPT — pure
            ' observation, no flow change. Lets us track that ORIG bone palette stays
            ' aligned with BAKE as we iterate the rest (vertex morphs, etc).
            If DebugMode Then
                DumpHdptThreeWay(hdptName, hdpt, baseKey, faceBonesKey, bakedRefNif, sb)
                DumpHdptTextureSources(hdptName, hdpt, baseKey, faceBonesKey, pluginManager, npcFormID, bakedRefNif, sb)
            End If
            Dim srcNif As Nifcontent_Class_Manolo = Nothing
            If Not loadedSources.TryGetValue(sourceKey, srcNif) Then
                Dim srcBytes As Byte() = Nothing
                Try
                    srcBytes = FilesDictionary_class.GetBytes(sourceKey)
                Catch ex As Exception
                    sb.AppendLine($"[BUILDCHARGEN]   FilesDictionary.GetBytes threw: {ex.GetType().Name}: {ex.Message}")
                End Try
                If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
                    sb.AppendLine($"[BUILDCHARGEN]   source NIF not found in BA2/loose: {sourceKey}")
                    hdptSourceMissing += 1
                    Continue For
                End If
                srcNif = New Nifcontent_Class_Manolo()
                Try
                    srcNif.Load_Manolo(srcBytes)
                Catch ex As Exception
                    sb.AppendLine($"[BUILDCHARGEN]   source NIF load failed: {ex.GetType().Name}: {ex.Message}")
                    hdptSourceLoadFail += 1
                    Continue For
                End Try
                loadedSources(sourceKey) = srcNif
                sb.AppendLine($"[BUILDCHARGEN]   loaded source ({srcBytes.Length} B)")
            Else
                sb.AppendLine($"[BUILDCHARGEN]   source already loaded (cache hit)")
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
            Dim srcShapes = srcNif.GetShapes().ToList()
            sb.AppendLine($"[BUILDCHARGEN]   shapes in source: {srcShapes.Count}")
            ' Log shader inline + BGSM material content for each shape AS LOADED FROM DISK.
            ' This is the baseline: anything that mutates these values downstream (resolver,
            ' MNAM swap, MSWP swap) will show up as a divergence in the comparison logs.
            For Each ss In srcShapes
                LogShapeLoadedMaterial(srcNif, ss, "SOURCE-LOAD", sb)
            Next
            Dim shapeIdxInThisHdpt As Integer = 0
            For Each srcShape In srcShapes
                Dim sourceName = If(srcShape.Name?.String, "")
                If sourceName = "" Then
                    sb.AppendLine($"[BUILDCHARGEN]     skipped unnamed shape (type={srcShape.GetType().Name})")
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
                    sb.AppendLine($"[BUILDCHARGEN]     skipped duplicate dest name '{destName}' (already cloned)")
                    shapesSkippedDup += 1
                    Continue For
                End If
                Try
                    Dim cloned = nif.CloneShape_Original(srcShape, destName, srcNif)
                    If cloned IsNot Nothing Then
                        clonedShapeNames.Add(destName)
                        shapesCloned += 1
                        sb.AppendLine($"[BUILDCHARGEN]     cloned source='{sourceName}' as '{destName}' VC={cloned.VertexCount} TC={cloned.TriangleCount} type={cloned.GetType().Name}")

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
                            sb.AppendLine($"[BUILDCHARGEN]     shader path clear failed: {ex.GetType().Name}: {ex.Message}")
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
                        ApplyRenderResolvedMaterialToShape(nif, cloned, srcNif, srcShape, hdpt, state, pluginManager, applyMaterialOverrides, sb)

                        ' --- FaceCustomization texture bake: only for the Face shape (PartType=1).
                        ' GL-readback the 3 GPU textures the FaceTintCompositor wrote (D/N/S),
                        ' encode each with the source-NIF's DXGI format (BC3/BC5/BC5 + mips —
                        ' verified empirically via DDSPROBE), write to <outputRoot>\Data\Textures\
                        ' Actors\Character\FaceCustomization\<plugin>\<formID>_*.dds2 and rewrite
                        ' the cloned shader's slot 0/1/7 to point to those paths. The .dds2
                        ' extension keeps us from clobbering the real CK FaceCustomization on
                        ' disk; for a real bake this would emit .dds and the engine would pick
                        ' those up directly.
                        If hdpt.PartType = PartTypeFace AndAlso host IsNot Nothing Then
                            BakeFaceTextures(nif, cloned, srcNif, srcShape,
                                             npcFormID, originPlugin,
                                             pluginManager, appliedPresets, host, sb,
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
                                    sb.AppendLine($"[BUILDCHARGEN]     FBNS read failed '{faceBonesKey}': {ex.GetType().Name}: {ex.Message}")
                                End Try
                                If fbnsBytes IsNot Nothing AndAlso fbnsBytes.Length > 0 Then
                                    fbnsNif = New Nifcontent_Class_Manolo()
                                    Try
                                        fbnsNif.Load_Manolo(fbnsBytes)
                                        loadedSources(faceBonesKey) = fbnsNif
                                    Catch ex As Exception
                                        sb.AppendLine($"[BUILDCHARGEN]     FBNS load failed '{faceBonesKey}': {ex.GetType().Name}: {ex.Message}")
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
                                        faceBonesName = sourceName.Substring(0, colonIdx) & "_faceBones" & sourceName.Substring(colonIdx)
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
                                    Dim stashV As Vector3d() = Nothing
                                    Dim stashFace As SkeletonInstance = Nothing
                                    Dim stashBody As SkeletonInstance = Nothing
                                    Dim baked = FaceGenBuildPipeline.BakeShape(bakeState, nif, cloned, fbnsNif, fbnsShape, hdpt.ChargenMorphTriPath, sb, stashV, stashFace, stashBody, srcNif, srcShape)
                                    If baked Then
                                        shapesMorphed += 1
                                        If stashV IsNot Nothing Then
                                            renderStash(destName) = (stashV, stashFace, stashBody)
                                        End If
                                    End If
                                Else
                                    sb.AppendLine($"[BUILDCHARGEN]     FBNS has no shape matching '{sourceName}' — leaving ORIG bind verts")
                                End If
                            End If
                        End If
                    Else
                        sb.AppendLine($"[BUILDCHARGEN]     CloneShape_Original returned Nothing for source='{sourceName}'")
                    End If
                Catch ex As Exception
                    sb.AppendLine($"[BUILDCHARGEN]     CloneShape_Original threw on source='{sourceName}': {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
            hdptProcessed += 1
        Next

        ' Drop any blocks left orphan after the strip+clone passes (e.g. the baked shell's
        ' shader properties / texture sets that were rooted only by the now-removed shapes).
        Try
            nif.RemoveUnreferencedBlocks()
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN] RemoveUnreferencedBlocks threw: {ex.GetType().Name}: {ex.Message}")
        End Try

        ' --- DIAG: probe the DDS header of the FaceCustomization textures CK references in
        ' the baked head shape's shader. Goal: discover the DXGI format CK uses for _d, _msn,
        ' _s so we can match it when we generate the bake (BC1/BC3/BC5/BC7 each have very
        ' different size and quality tradeoffs). Pure observation, runs once per build, no
        ' mutation. Looks up the head shape in the baked NIF reference and probes whatever
        ' texture paths its inline shader contains.
        If DebugMode Then
            Try
                ProbeFaceCustomizationDdsFormats(bakedRefNif, sb)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] threw: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If

        result.ShapesKept = shapesCloned
        result.ShapesDropped = 0
        sb.AppendLine($"[BUILDCHARGEN] assembly summary: hdpt-processed={hdptProcessed} hdpt-source-missing={hdptSourceMissing} hdpt-source-load-fail={hdptSourceLoadFail} shapes-cloned={shapesCloned} shapes-skipped-dup={shapesSkippedDup} shapes-morphed={shapesMorphed}")

        ' Write the .nif2 directly into the live FO4 loose folder. The .nif2 extension keeps
        ' the engine from picking it up (it reads .nif), so the canonical .nif on disk (CK's
        ' or vanilla via BA2 fallback) stays authoritative; renaming .nif2→.nif makes the
        ' engine use our bake.
        Dim formIdLow = (npcFormID And &HFFFFFFUI)
        Dim dataPathForNif = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPathForNif) Then
            sb.AppendLine("[BUILDCHARGEN] ABORT: Config_App.Current.DataPath empty — cannot resolve loose folder for .nif2")
            NpcPreviewLog.Log(sb.ToString())
            result.Summary = "DataPath unset; cannot write .nif2"
            Return result
        End If
        Dim nifSuffix = If(DebugMode, "_2.nif", ".nif")
        Dim outAbs = Path.Combine(dataPathForNif,
                                  "Meshes", "Actors", "Character", "FaceGenData", "FaceGeom",
                                  originPlugin, $"{formIdLow:X8}{nifSuffix}")
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(outAbs))
            nif.Save_As_Manolo(outAbs, Overwrite:=True)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN] Save_As_Manolo threw: {ex.GetType().Name}: {ex.Message}")
            NpcPreviewLog.Log(sb.ToString())
            result.Summary = $"Failed to write .nif2: {ex.Message}"
            Return result
        End Try
        sb.AppendLine($"[BUILDCHARGEN] wrote: {outAbs}")

        ' Debug-only post-save observation: BAKED-OURS dump + POST-SAVE-EMBED reload + comparator
        ' vs CK BA2 bake + render-vs-baked harness. All of these read the just-written file from
        ' disk and compare against CK's bake — only meaningful in DebugMode where we wrote a
        ' sandboxed _2.nif alongside CK's .nif. In release mode we OVERWROTE CK's .nif, so the
        ' "BA2 bytes" reference no longer represents an independent baseline; we ARE the bake.
        If DebugMode Then
            ' BAKED-OURS dump: read back the .nif2 we just wrote and dump shader-inline texture
            ' slots + AlphaProperty for every shape, side-by-side comparable to BAKED-CK so we
            ' can verify per-slot what we actually emit vs what CK emits.
            DumpOurBakeAllShapes(outAbs, sb)

            ' POST-SAVE-EMBED: reload the .nif2 from disk and log each shape's resolved material
            ' lighting fields. Diff against POST-RESOLVER shows what Save_To_Shader actually
            ' serialized (in case the writer drops or transforms fields).
            Try
                Dim savedNif As New Nifcontent_Class_Manolo()
                savedNif.Load_Manolo(File.ReadAllBytes(outAbs))
                For Each savedShape In savedNif.GetShapes()
                    LogShapeLoadedMaterial(savedNif, savedShape, "POST-SAVE-EMBED", sb)
                Next
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-MATLOG] [POST-SAVE-EMBED] reload threw: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If

        NpcPreviewLog.Log(sb.ToString())

        result.Success = True
        result.OutputPath = outAbs

        If DebugMode AndAlso vanillaBytes IsNot Nothing Then
            ' Compare the freshly written .nif2 against the BA2 baked NIF.
            Dim cmp = FaceGenComparator.Compare(outAbs, vanillaBytes)
            result.Compare = cmp

            ' Render-vs-baked world-space comparison.
            Try
                DumpRenderVsBakedHarness(outAbs, vanillaBytes, renderStash)
            Catch ex As Exception
                NpcPreviewLog.Log($"[BUILDCHARGEN-RENDERDIFF] EXCEPTION {ex.GetType().Name}: {ex.Message}")
            End Try

            result.Summary = $"Wrote {outAbs}{Environment.NewLine}" &
                             $"Cloned {result.ShapesKept} shape(s) from {hdptProcessed} HDPT source(s).{Environment.NewLine}" &
                             $"Diff vs CK bake: {cmp.Summary}{Environment.NewLine}" &
                             "See npc_preview.log [BUILDCHARGEN] + [BUILDCHARGEN-DIFF] for details."
        ElseIf DebugMode Then
            ' DebugMode but no vanilla to compare against (IsCharGenFacePreset NPC).
            result.Summary = $"Wrote {outAbs} ({result.ShapesKept} shapes from {hdptProcessed} HDPTs) — no vanilla FaceGen for diff"
        Else
            result.Summary = $"Wrote {outAbs} ({result.ShapesKept} shapes from {hdptProcessed} HDPTs)"
        End If

        Return result
    End Function

    ''' <summary>Stage-1 dump (kept for diagnostics). Loads the FaceGen NIF and logs its
    ''' structure plus the HDPT records the NPC references. No file is written.</summary>
    Public Function DumpFaceGenStructure(npcFormID As UInteger, pluginManager As PluginManager) As String
        Dim sb As New StringBuilder()
        sb.AppendLine($"[BUILDCHARGEN-DUMP] === NPC FormID={npcFormID:X8} ===")

        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        sb.AppendLine($"[BUILDCHARGEN-DUMP] origin plugin: '{originPlugin}'")

        Dim faceGenPath = ResolveFaceGenPath(npcFormID, pluginManager)
        sb.AppendLine($"[BUILDCHARGEN-DUMP] facegen path: '{faceGenPath}'")
        If faceGenPath = "" Then
            NpcPreviewLog.Log(sb.ToString())
            Return "Could not resolve origin plugin for this NPC."
        End If

        Dim bytes As Byte() = Nothing
        Try
            bytes = FilesDictionary_class.GetBytes(faceGenPath)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-DUMP] FilesDictionary.GetBytes threw: {ex.GetType().Name}: {ex.Message}")
        End Try

        If bytes Is Nothing OrElse bytes.Length = 0 Then
            sb.AppendLine("[BUILDCHARGEN-DUMP] no bytes returned — file not present in BA2/loose pool")
            NpcPreviewLog.Log(sb.ToString())
            Return $"FaceGen NIF not found in BA2/loose: {faceGenPath}"
        End If
        sb.AppendLine($"[BUILDCHARGEN-DUMP] loaded {bytes.Length} bytes from BA2/loose")

        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(bytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-DUMP] NIF load threw: {ex.GetType().Name}: {ex.Message}")
            NpcPreviewLog.Log(sb.ToString())
            Return $"Failed to parse FaceGen NIF: {ex.Message}"
        End Try

        Dim shapeCount = DumpNifShapes(nif, sb)
        DumpNpcHeadParts(npcFormID, pluginManager, sb)

        NpcPreviewLog.Log(sb.ToString())
        Return $"Dump written to npc_preview.log ({shapeCount} shapes, see [BUILDCHARGEN-DUMP] entries)."
    End Function

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
                                          pluginManager As PluginManager,
                                          sb As StringBuilder) As Dictionary(Of String, HDPT_Data)
        Dim allowed As New Dictionary(Of String, HDPT_Data)(StringComparer.OrdinalIgnoreCase)
        Dim npcRec = pluginManager.GetRecord(npcFormID)
        If npcRec Is Nothing Then Return allowed
        Dim npc = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pluginManager)
        If npc Is Nothing Then Return allowed

        ' Use the shared resolver (NPC.PNAM ∪ RACE defaults per PartType + freestanding misc).
        Dim mergedRoots = HeadPartResolver.MergeHeadPartsWithRaceDefaults(
            npc.RaceFormID, npc.IsFemale, npc.HeadPartFormIDs, pluginManager)

        ' Walk the chain via the shared HNAM-expanding iterator (cycles guarded inside).
        ' First-write wins: if an HDPT EditorID is reachable through multiple paths
        ' (RACE default + NPC override + extra-part), the resolver puts NPC override
        ' first in mergedRoots so the first Yield preserves override semantics.
        For Each hdpt In HeadPartResolver.EnumerateHdptChain(mergedRoots, pluginManager)
            If String.IsNullOrEmpty(hdpt.EditorID) Then Continue For
            If Not allowed.ContainsKey(hdpt.EditorID) Then allowed(hdpt.EditorID) = hdpt
        Next

        Return allowed
    End Function

    Private Function DumpNifShapes(nif As Nifcontent_Class_Manolo, sb As StringBuilder) As Integer
        Dim shapes = nif.GetShapes().ToList()
        sb.AppendLine($"[BUILDCHARGEN-DUMP] --- NIF shapes (count={shapes.Count}) ---")

        Dim idx As Integer = 0
        For Each shap In shapes
            Dim shapeName = If(shap.Name?.String, "<null>")
            Dim shapeType = shap.GetType().Name
            Dim vertCount As Integer = -1
            Dim triCount As Integer = -1
            Try
                vertCount = CInt(shap.VertexCount)
            Catch
            End Try
            Try
                triCount = shap.TriangleCount
            Catch
            End Try

            Dim bgsmPath As String = "<none>"
            Try
                Dim relMat = nif.GetRelatedMaterial(shap)
                If relMat IsNot Nothing AndAlso relMat.path IsNot Nothing Then
                    bgsmPath = If(relMat.path = "", "<empty>", relMat.path)
                End If
            Catch ex As Exception
                bgsmPath = $"<error: {ex.Message}>"
            End Try

            Dim skinDesc As String = "<unskinned>"
            Try
                Dim skinInst = nif.GetBlock(Of NiSkinInstance)(shap.SkinInstanceRef)
                If skinInst IsNot Nothing Then
                    skinDesc = skinInst.GetType().Name
                Else
                    Dim bsSkin = nif.GetBlock(Of BSSkin_Instance)(shap.SkinInstanceRef)
                    If bsSkin IsNot Nothing Then skinDesc = "BSSkin_Instance"
                End If
            Catch ex As Exception
                skinDesc = $"<error: {ex.Message}>"
            End Try

            sb.AppendLine($"[BUILDCHARGEN-DUMP]   shape[{idx}] name='{shapeName}' type={shapeType} VC={vertCount} TC={triCount} skin={skinDesc} bgsm='{bgsmPath}'")
            idx += 1
        Next
        Return shapes.Count
    End Function

    Private Sub DumpNpcHeadParts(npcFormID As UInteger, pluginManager As PluginManager, sb As StringBuilder)
        Dim npcRec = pluginManager.GetRecord(npcFormID)
        If npcRec Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-DUMP] --- HeadParts: NPC record not found ---")
            Return
        End If
        Dim npc = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pluginManager)
        If npc Is Nothing OrElse npc.HeadPartFormIDs Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-DUMP] --- HeadParts: NPC has no HeadPartFormIDs ---")
            Return
        End If

        sb.AppendLine($"[BUILDCHARGEN-DUMP] --- HeadParts referenced by NPC (count={npc.HeadPartFormIDs.Count}) ---")
        Dim idx As Integer = 0
        For Each hdptFormID In npc.HeadPartFormIDs
            Dim hdptRec = pluginManager.GetRecord(hdptFormID)
            If hdptRec Is Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-DUMP]   hdpt[{idx}] FormID={hdptFormID:X8} <record not found>")
                idx += 1
                Continue For
            End If
            If hdptRec.Header.Signature <> "HDPT" Then
                sb.AppendLine($"[BUILDCHARGEN-DUMP]   hdpt[{idx}] FormID={hdptFormID:X8} <wrong signature: {hdptRec.Header.Signature}>")
                idx += 1
                Continue For
            End If
            Dim hdpt = RecordParsers.ParseHDPT(hdptRec, pluginManager)
            sb.AppendLine($"[BUILDCHARGEN-DUMP]   hdpt[{idx}] FormID={hdptFormID:X8} EDID='{hdpt.EditorID}' Full='{hdpt.FullName}' PartType={hdpt.PartType} ({PartTypeName(hdpt.PartType)}) MeshPath='{hdpt.MeshPath}'")
            idx += 1
        Next
    End Sub

    ''' <summary>Diagnostic-only dump per HDPT comparing the three relevant NIF sources side
    ''' by side: <mesh>.nif (original, body bones only), <mesh>_facebones.nif (face bones in
    ''' skin partition), and the corresponding shape inside the baked CK FaceGen. Each line
    ''' lists shape names, vertex/triangle counts, and the bone palette. Used to decide
    ''' empirically which source to bake from.</summary>
    Private Sub DumpHdptThreeWay(hdptName As String,
                                 hdpt As HDPT_Data,
                                 baseKey As String,
                                 faceBonesKey As String,
                                 bakedRef As Nifcontent_Class_Manolo,
                                 sb As StringBuilder)
        sb.AppendLine($"[BUILDCHARGEN-THREEWAY] === HDPT '{hdptName}' ===")

        ' (1) Original mesh: <mesh>.nif (no _facebones suffix).
        DumpNifSourceForThreeWay("ORIG", baseKey, sb)

        ' (2) _facebones variant if present.
        If faceBonesKey <> "" Then
            DumpNifSourceForThreeWay("FBNS", faceBonesKey, sb)
        Else
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   FBNS: <no _facebones variant>")
        End If

        ' (3) Baked CK side: find the shape with the same name (= EditorID) in the baked NIF.
        If bakedRef Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   BAKE: <baked NIF unavailable>")
            Return
        End If
        Dim bakeShape As INiShape = Nothing
        For Each s In bakedRef.GetShapes()
            If String.Equals(If(s.Name?.String, ""), hdptName, StringComparison.OrdinalIgnoreCase) Then
                bakeShape = s
                Exit For
            End If
        Next
        If bakeShape Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   BAKE: <no shape with name '{hdptName}' in baked NIF>")
            Return
        End If
        Try
            Dim wrap As New NifRenderableShape(bakedRef, bakeShape, 0)
            DumpShapeMetricsForThreeWay("BAKE", bakeShape, wrap, sb)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   BAKE: dump failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

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
    Private Sub DumpHdptTextureSources(hdptName As String,
                                       hdpt As HDPT_Data,
                                       baseKey As String,
                                       faceBonesKey As String,
                                       pluginManager As PluginManager,
                                       npcFormID As UInteger,
                                       bakedRefNif As Nifcontent_Class_Manolo,
                                       sb As StringBuilder)
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC] === HDPT '{hdptName}' (PartType={hdpt.PartType}) ===")

        ' (A) Original mesh NIF
        DumpNifTextureSources("ORIG", baseKey, sb)

        ' (B) _facebones variant if present
        If faceBonesKey <> "" Then
            DumpNifTextureSources("FBNS", faceBonesKey, sb)
        Else
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FBNS: <no _facebones variant>")
        End If

        ' (BAKED-CK) Dump the actual baked NIF shader for the shape with name=EditorID,
        ' so we can compare what CK ended up writing against the four upstream sources.
        DumpBakedShaderForHdpt(hdptName, bakedRefNif, sb)

        ' (FTST) Per-NPC TXST (NPC_.WNAM/FTST), Face shader picks it up at bake time.
        DumpNpcFtst(npcFormID, pluginManager, sb)

        ' (C) TXST record (HDPT.TNAM): MNAM-pointed material
        ' (D) TXST record (HDPT.TNAM): direct TX00..TX07 paths
        If hdpt.TextureSetFormID = 0UI Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   TNAM: <no TXST attached>")
            Return
        End If
        Dim txstRec = pluginManager.GetRecord(hdpt.TextureSetFormID)
        If txstRec Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   TNAM TXST 0x{hdpt.TextureSetFormID:X8}: <not resolvable>")
            Return
        End If
        If txstRec.Header.Signature <> "TXST" Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   TNAM 0x{hdpt.TextureSetFormID:X8}: <signature {txstRec.Header.Signature}, not TXST>")
            Return
        End If
        Dim txst As TXST_Data = Nothing
        Try
            txst = RecordParsers.ParseTXST(txstRec, pluginManager)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   TNAM TXST parse failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try

        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   TNAM TXST '{txst.EditorID}' (0x{txst.FormID:X8}) flags=0x{txst.Flags:X4} faceGen={txst.IsFacegenTextures}")

        ' (C) MNAM-pointed material
        If String.IsNullOrEmpty(txst.MaterialPath) Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     MNAM: <empty>")
        Else
            DumpMaterialFromDictionary("MNAM", txst.MaterialPath, sb)
        End If

        ' (D) TXST direct slots (TX00..TX07)
        DumpTxstDirectSlots(txst, sb)

        ' (E) Hardcoded BGSM probe (user-supplied, MODT Material#0 of FemaleEyesHumanHazel
        ' shows materials\actors\character\humancommon\eyes.bgsm — log it once per HDPT for
        ' direct comparison with the chain above. Marked HARDCODED per
        ' feedback_no_hardcoding.md — this is a diagnostic dump explicitly authorized by user.)
        DumpMaterialFromDictionary("HARDCODED-eyes.bgsm",
                                   "materials\actors\character\humancommon\eyes.bgsm", sb)
    End Sub

    Private Sub DumpNifTextureSources(label As String, dictKey As String, sb As StringBuilder)
        Dim srcBytes As Byte() = Nothing
        Try
            srcBytes = FilesDictionary_class.GetBytes(dictKey)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   {label}: read failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   {label}: <not in FilesDictionary: {dictKey}>")
            Return
        End If
        Dim tmpNif As New Nifcontent_Class_Manolo()
        Try
            tmpNif.Load_Manolo(srcBytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   {label}: load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   {label}: '{dictKey}'")

        For Each shap In tmpNif.GetShapes()
            Dim shapeName = If(shap.Name?.String, "<unnamed>")
            Dim shad = tmpNif.GetShader(shap)
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            Dim bsef = TryCast(shad, BSEffectShaderProperty)

            If bsls IsNot Nothing Then
                Dim shaderType = bsls.ShaderType_SK_FO4
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     [{label}] shape='{shapeName}' shader=BSLightingShaderProperty type={shaderType}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       [{label}] shader-inline HasGreyscaleToPaletteColor={bsls.HasGreyscaleToPaletteColor} GrayscaleToPaletteScale={bsls.GrayscaleToPaletteScale}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       [{label}] shader-inline HasEnvironmentMapping={bsls.HasEnvironmentMapping} HasEyeEnvironmentMapping={bsls.HasEyeEnvironmentMapping} EnvironmentMapScale={bsls.EnvironmentMapScale} HasGlowmap={bsls.HasGlowmap} HasSpecular={bsls.HasSpecular} SpecularColor={bsls.SpecularColor} SpecularStrength={bsls.SpecularStrength}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       [{label}] shader-inline Emissive={bsls.Emissive} EmissiveColor={bsls.EmissiveColor} EmissiveMultiple={bsls.EmissiveMultiple} HasRimlight={bsls.HasRimlight} RimlightPower={bsls.RimlightPower} HasBacklight={bsls.HasBacklight} BacklightPower={bsls.BacklightPower} HasSoftlight={bsls.HasSoftlight} SubsurfaceRolloff={bsls.SubsurfaceRolloff} RootMaterialName='{bsls.RootMaterialName}'")
                ' Inline TX00..TX07 from the shader's TextureSet
                If bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index >= 0 Then
                    Dim ts = TryCast(tmpNif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
                    If ts IsNot Nothing AndAlso ts.Textures IsNot Nothing Then
                        DumpInlineTxStrings($"{label} shader-inline (BSLightingShader)", ts.Textures, sb)
                    End If
                End If
            ElseIf bsef IsNot Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     [{label}] shape='{shapeName}' shader=BSEffectShaderProperty")
                Dim slot00 = If(bsef.SourceTexture?.Content, "")
                Dim slotG = If(bsef.GreyscaleTexture?.Content, "")
                Dim slotE = If(bsef.EnvMapTexture?.Content, "")
                If slot00 <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       inline source='{slot00}'")
                If slotG <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       inline greyscale='{slotG}'")
                If slotE <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       inline envmap='{slotE}'")
            Else
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     [{label}] shape='{shapeName}' shader=<{If(shad Is Nothing, "null", shad.GetType().Name)}>")
            End If

            ' Related material (BGSM/BGEM via shader.Name path or BSEffect material)
            Try
                Dim relMat = tmpNif.GetRelatedMaterial(shap)
                If relMat IsNot Nothing AndAlso relMat.material IsNot Nothing Then
                    Dim path = If(relMat.path, "")
                    sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       related-material path='{path}'")
                    DumpMaterialSlots($"{label} related-material", relMat.material, sb)
                Else
                    sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       related-material: <none resolved>")
                End If
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       related-material exception: {ex.GetType().Name}: {ex.Message}")
            End Try
        Next
    End Sub

    Private Sub DumpInlineTxStrings(label As String, textures As System.Collections.Generic.IList(Of NiflySharp.NiString4), sb As StringBuilder)
        Dim slotNames = New String() {"Diffuse", "Normal", "Glow/Greyscale", "Greyscale/Height", "Envmap", "EnvmapMask/Wrinkles", "(unused)", "SmoothSpec"}
        For i = 0 To Math.Min(textures.Count - 1, 7)
            Dim p = If(textures(i)?.Content, "")
            If p = "" Then Continue For
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} TX0{i} {slotNames(i)}='{p}'")
        Next
    End Sub

    Private Sub DumpMaterialFromDictionary(label As String, materialPath As String, sb As StringBuilder)
        Dim normalized = FO4UnifiedMaterial_Class.CorrectMaterialPath(materialPath)
        Dim ext = System.IO.Path.GetExtension(normalized).ToLowerInvariant()
        Dim mat As New FO4UnifiedMaterial_Class()
        Try
            Select Case ext
                Case ".bgsm"
                    mat.Deserialize(normalized, GetType(BGSM))
                Case ".bgem"
                    mat.Deserialize(normalized, GetType(BGEM))
                Case Else
                    sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     {label} '{materialPath}': unsupported extension '{ext}'")
                    Return
            End Select
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     {label} '{materialPath}': load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     {label} material='{normalized}' shaderType={mat.NifShaderType}")
        DumpMaterialSlots(label, mat, sb)
    End Sub

    Private Sub DumpMaterialSlots(label As String, mat As FO4UnifiedMaterial_Class, sb As StringBuilder)
        If Not String.IsNullOrEmpty(mat.Diffuse_or_Base_Texture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} diffuse='{mat.Diffuse_or_Base_Texture}'")
        If Not String.IsNullOrEmpty(mat.NormalTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} normal='{mat.NormalTexture}'")
        If Not String.IsNullOrEmpty(mat.SmoothSpecTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} smoothSpec='{mat.SmoothSpecTexture}'")
        If Not String.IsNullOrEmpty(mat.GreyscaleTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} greyscale='{mat.GreyscaleTexture}'")
        If Not String.IsNullOrEmpty(mat.GlowTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} glow='{mat.GlowTexture}'")
        If Not String.IsNullOrEmpty(mat.EnvmapTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} envmap='{mat.EnvmapTexture}'")
        If Not String.IsNullOrEmpty(mat.EnvmapMaskTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} envmapMask='{mat.EnvmapMaskTexture}'")
        If Not String.IsNullOrEmpty(mat.SpecularTexture) Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} specular='{mat.SpecularTexture}'")
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} GrayscaleToPaletteColor={mat.GrayscaleToPaletteColor} GrayscaleToPaletteAlpha={mat.GrayscaleToPaletteAlpha} GrayscaleToPaletteScale={mat.GrayscaleToPaletteScale}")
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} Glowmap={mat.Glowmap} Hair={mat.Hair} SkinTint={mat.SkinTint} Facegen={mat.Facegen} EnvironmentMapping={mat.EnvironmentMapping} NifShaderType={mat.NifShaderType}")
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]       {label} EmitEnabled={mat.EmitEnabled} EmittanceColor={mat.EmittanceColor} EmittanceMult={mat.EmittanceMult} RimLighting={mat.RimLighting} RimPower={mat.RimPower} BackLighting={mat.BackLighting} BackLightPower={mat.BackLightPower} SpecularEnabled={mat.SpecularEnabled} SpecularColor={mat.SpecularColor:X8} SpecularMult={mat.SpecularMult} Smoothness={mat.Smoothness} SubsurfaceLighting={mat.SubsurfaceLighting} SubsurfaceLightingRolloff={mat.SubsurfaceLightingRolloff} SoftEnabled={mat.SoftEnabled} SoftDepth={mat.SoftDepth} RootMaterialPath='{mat.RootMaterialPath}'")
    End Sub

    Private Sub DumpTxstDirectSlots(txst As TXST_Data, sb As StringBuilder)
        If txst.DiffuseTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX00 diffuse='{txst.DiffuseTexture}'")
        If txst.NormalTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX01 normal='{txst.NormalTexture}'")
        If txst.WrinklesTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX02 wrinkles='{txst.WrinklesTexture}'")
        If txst.GlowTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX03 glow='{txst.GlowTexture}'")
        If txst.HeightTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX04 height='{txst.HeightTexture}'")
        If txst.EnvironmentTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX05 envMap='{txst.EnvironmentTexture}'")
        If txst.MultilayerTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX06 multilayer='{txst.MultilayerTexture}'")
        If txst.SmoothSpecTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     TXST TX07 smoothSpec='{txst.SmoothSpecTexture}'")
    End Sub

    ''' <summary>Dump the shader of every shape in the .nif2 we just wrote. Lets us
    ''' compare BAKED-OURS line by line against BAKED-CK from DumpBakedShaderForHdpt.</summary>
    Private Sub DumpOurBakeAllShapes(outNif2Path As String, sb As StringBuilder)
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC] === BAKED-OURS readback from '{outNif2Path}' ===")
        Dim bytes As Byte() = Nothing
        Try
            bytes = File.ReadAllBytes(outNif2Path)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-OURS read failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        Dim ourNif As New Nifcontent_Class_Manolo()
        Try
            ourNif.Load_Manolo(bytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-OURS load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        For Each shap In ourNif.GetShapes()
            Dim shapeName = If(shap.Name?.String, "<unnamed>")
            Dim shad = ourNif.GetShader(shap)
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            Dim bsef = TryCast(shad, BSEffectShaderProperty)
            If bsls IsNot Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-OURS shape='{shapeName}' shader=BSLightingShaderProperty type={bsls.ShaderType_SK_FO4}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS shader-inline HasGreyscaleToPaletteColor={bsls.HasGreyscaleToPaletteColor} GrayscaleToPaletteScale={bsls.GrayscaleToPaletteScale}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS shader-inline HasEnvironmentMapping={bsls.HasEnvironmentMapping} HasEyeEnvironmentMapping={bsls.HasEyeEnvironmentMapping} EnvironmentMapScale={bsls.EnvironmentMapScale} HasGlowmap={bsls.HasGlowmap} HasSpecular={bsls.HasSpecular} SpecularColor={bsls.SpecularColor} SpecularStrength={bsls.SpecularStrength}")
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS shader-inline Emissive={bsls.Emissive} EmissiveColor={bsls.EmissiveColor} EmissiveMultiple={bsls.EmissiveMultiple} HasRimlight={bsls.HasRimlight} RimlightPower={bsls.RimlightPower} HasBacklight={bsls.HasBacklight} BacklightPower={bsls.BacklightPower} HasSoftlight={bsls.HasSoftlight} SubsurfaceRolloff={bsls.SubsurfaceRolloff} RootMaterialName='{bsls.RootMaterialName}'")
                If bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index >= 0 Then
                    Dim ts = TryCast(ourNif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
                    If ts IsNot Nothing AndAlso ts.Textures IsNot Nothing Then
                        DumpInlineTxStrings("BAKED-OURS shader-inline (BSLightingShader)", ts.Textures, sb)
                    End If
                End If
            ElseIf bsef IsNot Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-OURS shape='{shapeName}' shader=BSEffectShaderProperty")
                If bsef.SourceTexture IsNot Nothing AndAlso bsef.SourceTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS inline source='{bsef.SourceTexture.Content}'")
                If bsef.GreyscaleTexture IsNot Nothing AndAlso bsef.GreyscaleTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS inline greyscale='{bsef.GreyscaleTexture.Content}'")
                If bsef.EnvMapTexture IsNot Nothing AndAlso bsef.EnvMapTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS inline envmap='{bsef.EnvMapTexture.Content}'")
            Else
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-OURS shape='{shapeName}' shader=<{If(shad Is Nothing, "null", shad.GetType().Name)}>")
            End If
            Try
                If shap.AlphaPropertyRef IsNot Nothing AndAlso shap.AlphaPropertyRef.Index >= 0 Then
                    Dim alp = TryCast(ourNif.Blocks(shap.AlphaPropertyRef.Index), NiflySharp.Blocks.NiAlphaProperty)
                    If alp IsNot Nothing Then
                        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS NiAlphaProperty Threshold={alp.Threshold} Flags.AlphaBlend={alp.Flags.AlphaBlend} Flags.AlphaTest={alp.Flags.AlphaTest}")
                    End If
                Else
                    sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS NiAlphaProperty: <none>")
                End If
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-OURS NiAlphaProperty exception: {ex.GetType().Name}: {ex.Message}")
            End Try
        Next
    End Sub

    ''' <summary>Dump the BAKED CK shader for the shape with name=hdptName. This is what
    ''' CK actually wrote — the ground truth we want to match. Compared against the four
    ''' upstream sources (ORIG/FBNS/MNAM/TXST/FTST), it tells us which source CK trusts.</summary>
    Private Sub DumpBakedShaderForHdpt(hdptName As String, bakedRefNif As Nifcontent_Class_Manolo, sb As StringBuilder)
        If bakedRefNif Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-CK: <baked NIF unavailable>")
            Return
        End If
        Dim bakeShape As INiShape = Nothing
        For Each s In bakedRefNif.GetShapes()
            If String.Equals(If(s.Name?.String, ""), hdptName, StringComparison.OrdinalIgnoreCase) Then
                bakeShape = s : Exit For
            End If
        Next
        If bakeShape Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-CK: <no shape '{hdptName}' in baked NIF>")
            Return
        End If
        Dim shad = bakedRefNif.GetShader(bakeShape)
        Dim bsls = TryCast(shad, BSLightingShaderProperty)
        Dim bsef = TryCast(shad, BSEffectShaderProperty)
        If bsls IsNot Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-CK shape='{hdptName}' shader=BSLightingShaderProperty type={bsls.ShaderType_SK_FO4}")
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK shader-inline HasGreyscaleToPaletteColor={bsls.HasGreyscaleToPaletteColor} GrayscaleToPaletteScale={bsls.GrayscaleToPaletteScale}")
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK shader-inline HasEnvironmentMapping={bsls.HasEnvironmentMapping} HasEyeEnvironmentMapping={bsls.HasEyeEnvironmentMapping} EnvironmentMapScale={bsls.EnvironmentMapScale} HasGlowmap={bsls.HasGlowmap} HasSpecular={bsls.HasSpecular} SpecularColor={bsls.SpecularColor} SpecularStrength={bsls.SpecularStrength}")
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK shader-inline Emissive={bsls.Emissive} EmissiveColor={bsls.EmissiveColor} EmissiveMultiple={bsls.EmissiveMultiple} HasRimlight={bsls.HasRimlight} RimlightPower={bsls.RimlightPower} HasBacklight={bsls.HasBacklight} BacklightPower={bsls.BacklightPower} HasSoftlight={bsls.HasSoftlight} SubsurfaceRolloff={bsls.SubsurfaceRolloff} RootMaterialName='{bsls.RootMaterialName}'")
            If bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index >= 0 Then
                Dim ts = TryCast(bakedRefNif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
                If ts IsNot Nothing AndAlso ts.Textures IsNot Nothing Then
                    DumpInlineTxStrings("BAKED-CK shader-inline (BSLightingShader)", ts.Textures, sb)
                End If
            End If
        ElseIf bsef IsNot Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-CK shape='{hdptName}' shader=BSEffectShaderProperty")
            If bsef.SourceTexture IsNot Nothing AndAlso bsef.SourceTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK inline source='{bsef.SourceTexture.Content}'")
            If bsef.GreyscaleTexture IsNot Nothing AndAlso bsef.GreyscaleTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK inline greyscale='{bsef.GreyscaleTexture.Content}'")
            If bsef.EnvMapTexture IsNot Nothing AndAlso bsef.EnvMapTexture.Content <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK inline envmap='{bsef.EnvMapTexture.Content}'")
        Else
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   BAKED-CK shape='{hdptName}' shader=<{If(shad Is Nothing, "null", shad.GetType().Name)}>")
        End If
        ' Alpha property (so we see AlphaTestRef + flags directly).
        Try
            If bakeShape.AlphaPropertyRef IsNot Nothing AndAlso bakeShape.AlphaPropertyRef.Index >= 0 Then
                Dim alp = TryCast(bakedRefNif.Blocks(bakeShape.AlphaPropertyRef.Index), NiflySharp.Blocks.NiAlphaProperty)
                If alp IsNot Nothing Then
                    sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK NiAlphaProperty Threshold={alp.Threshold} Flags.AlphaBlend={alp.Flags.AlphaBlend} Flags.AlphaTest={alp.Flags.AlphaTest}")
                End If
            Else
                sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK NiAlphaProperty: <none>")
            End If
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     BAKED-CK NiAlphaProperty exception: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Dump NPC.FTST (HeadTextureFormID) — the per-NPC TXST. The Face HDPT bake
    ''' typically picks slot mappings from here when the HDPT has no TNAM TXST.</summary>
    Private Sub DumpNpcFtst(npcFormID As UInteger, pluginManager As PluginManager, sb As StringBuilder)
        Dim npcRaw = NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager)
        If npcRaw Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FTST: <NPC parse failed>")
            Return
        End If
        If npcRaw.HeadTextureFormID = 0UI Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FTST: <NPC has no HeadTextureFormID>")
            Return
        End If
        Dim txstRec = pluginManager.GetRecord(npcRaw.HeadTextureFormID)
        If txstRec Is Nothing OrElse txstRec.Header.Signature <> "TXST" Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FTST 0x{npcRaw.HeadTextureFormID:X8}: <not resolvable as TXST>")
            Return
        End If
        Dim txst As TXST_Data
        Try
            txst = RecordParsers.ParseTXST(txstRec, pluginManager)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FTST parse failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        sb.AppendLine($"[BUILDCHARGEN-TEXSRC]   FTST (NPC.HeadTexture) '{txst.EditorID}' (0x{txst.FormID:X8}) flags=0x{txst.Flags:X4} faceGen={txst.IsFacegenTextures}")
        If Not String.IsNullOrEmpty(txst.MaterialPath) Then
            sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST MNAM material='{txst.MaterialPath}'")
        End If
        If txst.DiffuseTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX00 diffuse='{txst.DiffuseTexture}'")
        If txst.NormalTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX01 normal='{txst.NormalTexture}'")
        If txst.WrinklesTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX02 wrinkles='{txst.WrinklesTexture}'")
        If txst.GlowTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX03 glow='{txst.GlowTexture}'")
        If txst.HeightTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX04 height='{txst.HeightTexture}'")
        If txst.EnvironmentTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX05 envMap='{txst.EnvironmentTexture}'")
        If txst.MultilayerTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX06 multilayer='{txst.MultilayerTexture}'")
        If txst.SmoothSpecTexture <> "" Then sb.AppendLine($"[BUILDCHARGEN-TEXSRC]     FTST TX07 smoothSpec='{txst.SmoothSpecTexture}'")
    End Sub

    Private Sub DumpNifSourceForThreeWay(label As String, dictKey As String, sb As StringBuilder)
        Dim srcBytes As Byte() = Nothing
        Try
            srcBytes = FilesDictionary_class.GetBytes(dictKey)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   {label}: read failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   {label}: <not found in FilesDictionary: {dictKey}>")
            Return
        End If
        Dim tmpNif As New Nifcontent_Class_Manolo()
        Try
            tmpNif.Load_Manolo(srcBytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   {label}: load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        Dim shapes = tmpNif.GetShapes().ToList()
        sb.AppendLine($"[BUILDCHARGEN-THREEWAY]   {label}: '{dictKey}' shapes={shapes.Count}")
        For Each s In shapes
            Try
                Dim wrap As New NifRenderableShape(tmpNif, s, 0)
                DumpShapeMetricsForThreeWay(label, s, wrap, sb)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-THREEWAY]     {label} dump failed for '{If(s.Name?.String, "")}': {ex.Message}")
            End Try
        Next
    End Sub

    Private Sub DumpShapeMetricsForThreeWay(label As String,
                                            shape As INiShape,
                                            wrap As NifRenderableShape,
                                            sb As StringBuilder)
        Dim sname = If(shape.Name?.String, "")
        Dim vc = CInt(shape.VertexCount)
        Dim tc = shape.TriangleCount
        Dim boneNames = wrap.ShapeBones.Select(Function(n) If(n?.Name?.String, "")).ToList()
        sb.AppendLine($"[BUILDCHARGEN-THREEWAY]     {label}: shape='{sname}' VC={vc} TC={tc} bones({boneNames.Count})=[{String.Join(", ", boneNames)}]")

        ' Per-bone bind transform from BSSkin_BoneData (FO4) / NiSkinData (SSE/legacy).
        ' Hipótesis a verificar (2026-05-07): los face bones del `_facebones.nif` traen un
        ' bind transform NO-identity respecto al face skeleton — eso sería el morph del
        ' bake pre-aplicado al bone bind. Si CK al bakear "colapsa" esos bones al ORIG
        ' palette debe propagar ese delta al vertex array antes de dropear los bones.
        ' Solo dumpeo cuando el label es FBNS para no inflar el log con los body-only ORIG/BAKE.
        If label = "FBNS" AndAlso wrap.ShapeBoneTransforms IsNot Nothing Then
            Dim transforms = wrap.ShapeBoneTransforms.ToList()
            For i = 0 To Math.Min(boneNames.Count, transforms.Count) - 1
                Dim bn = boneNames(i)
                ' Filter to face bones only; the body bones in FBNS share their bind with ORIG
                ' (already verified by [BIND-CHECK] in the existing harness output).
                If Not bn.StartsWith("skin_bone_", StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim t = transforms(i)
                If t Is Nothing Then Continue For
                Dim tr = t.Translation
                Dim sc = t.Scale
                Dim r = t.Rotation
                ' Identity check: translation magnitude, rotation off-diagonal magnitude, scale-1.
                Dim tMag = Math.Sqrt(tr.X * tr.X + tr.Y * tr.Y + tr.Z * tr.Z)
                Dim rOff = Math.Abs(r.M11 - 1) + Math.Abs(r.M22 - 1) + Math.Abs(r.M33 - 1) +
                           Math.Abs(r.M12) + Math.Abs(r.M13) +
                           Math.Abs(r.M21) + Math.Abs(r.M23) +
                           Math.Abs(r.M31) + Math.Abs(r.M32)
                Dim sOff = Math.Abs(sc - 1.0F)
                Dim isIdentity = (tMag < 0.001) AndAlso (rOff < 0.001) AndAlso (sOff < 0.001)
                Dim flag = If(isIdentity, "IDENT", "BAKED")
                sb.AppendLine($"[BUILDCHARGEN-THREEWAY]       FBNS-BIND {flag} '{bn}' T=({tr.X:F4},{tr.Y:F4},{tr.Z:F4}) S={sc:F4} Rdiag=({r.M11:F4},{r.M22:F4},{r.M33:F4}) tMag={tMag:F4} rOff={rOff:F4} sOff={sOff:F4}")
            Next
        End If
    End Sub


    ''' <summary>Per-shape world-space comparison: render's v_world (already produced by
    ''' BakeShape) vs OUR .nif2 re-skinned at bind, vs CK-baked NIF re-skinned at bind. The
    ''' re-skin uses a bind-only resolver (NO FMRS) — exactly what the runtime does when it
    ''' renders a baked face NIF. Logs <c>[BUILDCHARGEN-RENDERDIFF]</c> per shape + aggregates.
    '''
    ''' Single-source-of-truth: every per-vertex skin operation funnels through SkinBakeMath +
    ''' the same FaceGenBuildPipeline.BuildBindResolver used inside BakeShape. No math is
    ''' duplicated here.</summary>
    Private Sub DumpRenderVsBakedHarness(oursAbsPath As String,
                                          ckBakedBytes As Byte(),
                                          renderStash As Dictionary(Of String, (vWorld As Vector3d(), faceSkel As SkeletonInstance, bodySkel As SkeletonInstance)))
        Dim sb As New StringBuilder()
        sb.AppendLine("[BUILDCHARGEN-RENDERDIFF] === render-vs-baked world-space ===")
        sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] generated: {oursAbsPath}")
        sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] baked: (CK BA2 bytes, {ckBakedBytes.Length} B)")

        If renderStash Is Nothing OrElse renderStash.Count = 0 Then
            sb.AppendLine("[BUILDCHARGEN-RENDERDIFF] renderStash empty — no shapes to compare")
            NpcPreviewLog.Log(sb.ToString())
            Return
        End If

        Dim oursNif As New Nifcontent_Class_Manolo()
        Dim ckNif As New Nifcontent_Class_Manolo()
        Try
            oursNif.Load_Manolo(IO.File.ReadAllBytes(oursAbsPath))
            ckNif.Load_Manolo(ckBakedBytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] NIF reload failed: {ex.GetType().Name}: {ex.Message}")
            NpcPreviewLog.Log(sb.ToString())
            Return
        End Try

        Dim aggOursSumSq As Double = 0
        Dim aggCkSumSq As Double = 0
        Dim aggOursVsCkSumSq As Double = 0
        Dim aggCount As Integer = 0
        Dim oursMaxRms As Double = 0
        Dim ckMaxRms As Double = 0
        Dim oursVsCkMaxRms As Double = 0

        For Each kv In renderStash.OrderBy(Function(p) p.Key)
            Dim shapeName = kv.Key
            Dim vWorld = kv.Value.vWorld
            If vWorld Is Nothing OrElse vWorld.Length = 0 Then Continue For

            Dim oursShape = FindShapeByName(oursNif, shapeName)
            Dim ckShape = FindShapeByName(ckNif, shapeName)
            If oursShape Is Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] shape '{shapeName}': not found in OURS .nif2")
                Continue For
            End If

            Dim resolver = FaceGenBuildPipeline.BuildBindResolver(kv.Value.faceSkel, kv.Value.bodySkel, oursNif)
            Dim vOurs = SkinBakeMath.SkinShapeWorldVertices(oursShape, oursNif, resolver)
            Dim vCk As Vector3d() = Nothing
            If ckShape IsNot Nothing Then
                Dim resolverCk = FaceGenBuildPipeline.BuildBindResolver(kv.Value.faceSkel, kv.Value.bodySkel, ckNif)
                vCk = SkinBakeMath.SkinShapeWorldVertices(ckShape, ckNif, resolverCk)
            End If

            ' RMS render-vs-OURS
            Dim oursRms As Double = 0
            Dim oursMax As Double = 0
            If vOurs IsNot Nothing AndAlso vOurs.Length = vWorld.Length Then
                Dim ssq As Double = 0
                For i = 0 To vWorld.Length - 1
                    Dim dx = vWorld(i).X - vOurs(i).X
                    Dim dy = vWorld(i).Y - vOurs(i).Y
                    Dim dz = vWorld(i).Z - vOurs(i).Z
                    Dim m = dx * dx + dy * dy + dz * dz
                    ssq += m
                    Dim mag = Math.Sqrt(m)
                    If mag > oursMax Then oursMax = mag
                Next
                oursRms = Math.Sqrt(ssq / vWorld.Length)
                aggOursSumSq += ssq
            End If

            ' RMS render-vs-CK
            Dim ckRms As Double = -1
            Dim ckMax As Double = 0
            If vCk IsNot Nothing AndAlso vCk.Length = vWorld.Length Then
                Dim ssq As Double = 0
                For i = 0 To vWorld.Length - 1
                    Dim dx = vWorld(i).X - vCk(i).X
                    Dim dy = vWorld(i).Y - vCk(i).Y
                    Dim dz = vWorld(i).Z - vCk(i).Z
                    Dim m = dx * dx + dy * dy + dz * dz
                    ssq += m
                    Dim mag = Math.Sqrt(m)
                    If mag > ckMax Then ckMax = mag
                Next
                ckRms = Math.Sqrt(ssq / vWorld.Length)
                aggCkSumSq += ssq
            End If

            ' RMS ours-vs-ck (world-space, both re-skinned with the same bind resolver — so any
            ' diff isolates the shape-data difference between our .nif2 and CK's bake).
            Dim oursVsCkRms As Double = -1
            Dim oursVsCkMax As Double = 0
            If vOurs IsNot Nothing AndAlso vCk IsNot Nothing AndAlso vOurs.Length = vCk.Length Then
                Dim ssq As Double = 0
                For i = 0 To vOurs.Length - 1
                    Dim dx = vOurs(i).X - vCk(i).X
                    Dim dy = vOurs(i).Y - vCk(i).Y
                    Dim dz = vOurs(i).Z - vCk(i).Z
                    Dim m = dx * dx + dy * dy + dz * dz
                    ssq += m
                    Dim mag = Math.Sqrt(m)
                    If mag > oursVsCkMax Then oursVsCkMax = mag
                Next
                oursVsCkRms = Math.Sqrt(ssq / vOurs.Length)
                aggOursVsCkSumSq += ssq
            End If

            aggCount += vWorld.Length
            If oursRms > oursMaxRms Then oursMaxRms = oursRms
            If ckRms > ckMaxRms Then ckMaxRms = ckRms
            If oursVsCkRms > oursVsCkMaxRms Then oursVsCkMaxRms = oursVsCkRms

            Dim ckCol = If(ckRms < 0, "n/a (no shape in CK)", $"RMS={ckRms:F6} max={ckMax:F6}")
            Dim oursVsCkCol = If(oursVsCkRms < 0, "n/a", $"RMS={oursVsCkRms:F6} max={oursVsCkMax:F6}")
            sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] shape '{shapeName}' VC={vWorld.Length}  ours-vs-render RMS={oursRms:F6} max={oursMax:F6}  |  ck-vs-render {ckCol}  |  ours-vs-ck {oursVsCkCol}")
        Next

        Dim aggOursRms = If(aggCount > 0, Math.Sqrt(aggOursSumSq / aggCount), 0.0)
        Dim aggCkRms = If(aggCount > 0, Math.Sqrt(aggCkSumSq / aggCount), 0.0)
        Dim aggOursVsCkRms = If(aggCount > 0, Math.Sqrt(aggOursVsCkSumSq / aggCount), 0.0)
        sb.AppendLine($"[BUILDCHARGEN-RENDERDIFF] AGGREGATE  ours-vs-render RMS={aggOursRms:F6} (per-shape max={oursMaxRms:F6})  ck-vs-render RMS={aggCkRms:F6} (per-shape max={ckMaxRms:F6})  ours-vs-ck RMS={aggOursVsCkRms:F6} (per-shape max={oursVsCkMaxRms:F6})  total verts={aggCount}")

        NpcPreviewLog.Log(sb.ToString())
    End Sub

    Private Function FindShapeByName(nif As Nifcontent_Class_Manolo, name As String) As INiShape
        For Each s In nif.GetShapes()
            If String.Equals(If(s.Name?.String, ""), name, StringComparison.OrdinalIgnoreCase) Then Return s
        Next
        Return Nothing
    End Function
    ''' <summary>Diagnostic-only: read the DDS header of the FaceCustomization textures CK
    ''' references in the baked head shape and log their format/dims/mips. Runs once per
    ''' build. Used to settle empirically which DXGI format CK uses for the per-NPC face
    ''' bake textures (_d / _msn / _s) so we can match it when generating the bake from
    ''' the FaceTintCompositor's GPU output. Reads from FilesDictionary (BA2 / loose pool).</summary>
    Private Sub ProbeFaceCustomizationDdsFormats(bakedRefNif As Nifcontent_Class_Manolo, sb As StringBuilder)
        If bakedRefNif Is Nothing Then Return
        ' Find the head shape in the baked NIF (the one whose name contains "Head" and is
        ' not "HeadRear" — face shape per HDPT.PartType=1).
        Dim headShape As INiShape = Nothing
        For Each s In bakedRefNif.GetShapes()
            Dim n = If(s.Name?.String, "")
            If n.IndexOf("Head", StringComparison.OrdinalIgnoreCase) >= 0 _
               AndAlso n.IndexOf("Rear", StringComparison.OrdinalIgnoreCase) < 0 Then
                headShape = s : Exit For
            End If
        Next
        If headShape Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-DDSPROBE] no head shape found in baked NIF")
            Return
        End If
        Dim shad = TryCast(bakedRefNif.GetShader(headShape), BSLightingShaderProperty)
        If shad Is Nothing OrElse shad.TextureSetRef Is Nothing OrElse shad.TextureSetRef.Index = -1 Then
            sb.AppendLine("[BUILDCHARGEN-DDSPROBE] head shape has no BSLightingShader/TextureSet")
            Return
        End If
        Dim texset = TryCast(bakedRefNif.Blocks(shad.TextureSetRef.Index), BSShaderTextureSet)
        If texset Is Nothing OrElse texset.Textures Is Nothing Then Return

        Dim slotNames = New String() {"Diffuse", "Normal", "Glow", "Greyscale/Height", "Envmap", "EnvmapMask/Wrinkles", "(unused)", "SmoothSpec"}
        sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] === head shape '{headShape.Name?.String}' texture set (BAKED CK, FaceCustomization swap) ===")
        For i = 0 To Math.Min(texset.Textures.Count - 1, 7)
            Dim path = If(texset.Textures(i)?.Content, "")
            If String.IsNullOrEmpty(path) Then Continue For
            ' Normalize the path to the dictionary's canonical form (Textures\ prefix added,
            ' separators corrected, etc.) — same path the render uses.
            Dim normalized = FO4UnifiedMaterial_Class.CorrectTexturePath(path)
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]   slot[{i}]={slotNames(i)} path='{path}' normalized='{normalized}'")
            Dim bytes As Byte() = Nothing
            Try
                bytes = FilesDictionary_class.GetBytes(normalized)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     read failed: {ex.GetType().Name}: {ex.Message}")
                Continue For
            End Try
            If bytes Is Nothing OrElse bytes.Length < 128 Then
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     not in dictionary or too small ({If(bytes Is Nothing, 0, bytes.Length)} B)")
                Continue For
            End If
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     {DescribeDdsHeader(bytes)}")
        Next

        ' Now probe the SOURCE-NIF textures (vanilla head BGSM that the shape originally
        ' referenced pre-FaceCustomization). The bake should match those formats — that's
        ' the convention the engine expects for a face shape's texture slots.
        ProbeSourceNifHeadTextureFormats(sb)
    End Sub

    ''' <summary>Diagnostic-only: open the vanilla head NIF, read its shader's BGSM, and
    ''' probe the DDS format of the source D/N/S textures. The face bake should write the
    ''' per-NPC FaceCustomization textures using the same DXGI format these source textures
    ''' use, so the engine's sampler / mip behavior stays consistent.</summary>
    Private Sub ProbeSourceNifHeadTextureFormats(sb As StringBuilder)
        Const HeadNifKey As String = "meshes\actors\character\characterassets\basefemalehead.nif"
        Dim nifBytes As Byte() = Nothing
        Try
            nifBytes = FilesDictionary_class.GetBytes(HeadNifKey)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] source-NIF read failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] source-NIF '{HeadNifKey}' not in FilesDictionary")
            Return
        End If

        Dim srcNif As New Nifcontent_Class_Manolo()
        Try
            srcNif.Load_Manolo(nifBytes)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] source-NIF load failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try

        Dim srcShape As INiShape = Nothing
        For Each s In srcNif.GetShapes()
            srcShape = s : Exit For
        Next
        If srcShape Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] source-NIF has no shapes")
            Return
        End If

        ' Resolve the material via the standard library path (handles both BGSM and shader
        ' inline). For BaseFemaleHead.nif this loads basehumanfemaleskinhead.bgsm.
        Dim relMat = srcNif.GetRelatedMaterial(srcShape)
        Dim mat = relMat?.material
        If mat Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] source-NIF shape has no resolved material")
            Return
        End If

        sb.AppendLine($"[BUILDCHARGEN-DDSPROBE] === source NIF head shape (vanilla pre-bake) bgsm='{relMat?.path}' ===")
        Dim slotPaths = New (Name As String, Path As String)() {
            ("Diffuse   ", If(mat.Diffuse_or_Base_Texture, "")),
            ("Normal    ", If(mat.NormalTexture, "")),
            ("SmoothSpec", If(mat.SmoothSpecTexture, ""))
        }
        For Each entry In slotPaths
            If String.IsNullOrEmpty(entry.Path) Then
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]   {entry.Name}: <empty>")
                Continue For
            End If
            Dim normalized = FO4UnifiedMaterial_Class.CorrectTexturePath(entry.Path)
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]   {entry.Name} path='{entry.Path}' normalized='{normalized}'")
            Dim bytes As Byte() = Nothing
            Try
                bytes = FilesDictionary_class.GetBytes(normalized)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     read failed: {ex.GetType().Name}: {ex.Message}")
                Continue For
            End Try
            If bytes Is Nothing OrElse bytes.Length < 128 Then
                sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     not in dictionary or too small ({If(bytes Is Nothing, 0, bytes.Length)} B)")
                Continue For
            End If
            sb.AppendLine($"[BUILDCHARGEN-DDSPROBE]     {DescribeDdsHeader(bytes)}")
        Next
    End Sub

    ''' <summary>Parse a DDS file's header (DDS_HEADER + optional DDS_HEADER_DXT10) and return
    ''' a one-line description: format, dims, mips, file size. Reference: DDS file layout per
    ''' DirectXTex / Microsoft DDS spec — magic 'DDS '@0, DDS_HEADER@4 (124 bytes), DXT10 ext
    ''' @128 (20 bytes) when DDPF_FOURCC pixelformat carries 'DX10'.</summary>
    Private Function DescribeDdsHeader(bytes As Byte()) As String
        If bytes Is Nothing OrElse bytes.Length < 128 Then Return "<too small>"
        ' DDS magic at byte 0..3
        If bytes(0) <> &H44 OrElse bytes(1) <> &H44 OrElse bytes(2) <> &H53 OrElse bytes(3) <> &H20 Then
            Return "<not a DDS file (magic mismatch)>"
        End If
        ' DDS_HEADER starts at offset 4. Width@12, Height@16, MipMapCount@28.
        Dim height = BitConverter.ToInt32(bytes, 12)
        Dim width = BitConverter.ToInt32(bytes, 16)
        Dim mips = BitConverter.ToInt32(bytes, 28)
        ' DDPF (DDS_PIXELFORMAT) at offset 76, length 32. dwFourCC at offset 84 (= 76+8).
        Dim fourCC = System.Text.Encoding.ASCII.GetString(bytes, 84, 4)
        Dim dxgiFormat As Integer = -1
        Dim dxgiFormatName As String = ""
        If fourCC = "DX10" AndAlso bytes.Length >= 148 Then
            ' DDS_HEADER_DXT10 starts at byte 128. dxgiFormat is the first 4 bytes.
            dxgiFormat = BitConverter.ToInt32(bytes, 128)
            dxgiFormatName = $" dxgi={dxgiFormat} ({DxgiFormatName(dxgiFormat)})"
        End If
        Return $"fourCC='{fourCC}'{dxgiFormatName} {width}x{height} mips={mips} fileSize={bytes.Length}"
    End Function

    ''' <summary>Map common DXGI_FORMAT enum values to readable names. Only the ones FaceGen
    ''' textures might use are listed; unknown values fall through to a numeric label.</summary>
    Private Function DxgiFormatName(format As Integer) As String
        Select Case format
            Case 28 : Return "R8G8B8A8_UNORM"
            Case 29 : Return "R8G8B8A8_UNORM_SRGB"
            Case 71 : Return "BC1_UNORM"
            Case 72 : Return "BC1_UNORM_SRGB"
            Case 74 : Return "BC2_UNORM"
            Case 77 : Return "BC3_UNORM"
            Case 78 : Return "BC3_UNORM_SRGB"
            Case 80 : Return "BC4_UNORM"
            Case 81 : Return "BC4_SNORM"
            Case 83 : Return "BC5_UNORM"
            Case 84 : Return "BC5_SNORM"
            Case 87 : Return "B8G8R8A8_UNORM"
            Case 91 : Return "B8G8R8A8_UNORM_SRGB"
            Case 95 : Return "BC6H_UF16"
            Case 96 : Return "BC6H_SF16"
            Case 98 : Return "BC7_UNORM"
            Case 99 : Return "BC7_UNORM_SRGB"
            Case Else : Return $"<unknown {format}>"
        End Select
    End Function

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
    Private Sub ApplyRenderResolvedMaterialToShape(nif As Nifcontent_Class_Manolo,
                                                    cloned As INiShape,
                                                    srcNif As Nifcontent_Class_Manolo,
                                                    srcShape As INiShape,
                                                    hdpt As HDPT_Data,
                                                    state As MainForm.NPCVisualState,
                                                    pluginManager As PluginManager,
                                                    applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                                    sb As StringBuilder)
        Dim sourceName As String = If(cloned.Name?.String, "")
        If applyMaterialOverrides Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: no override delegate provided — leaving cloned shader intact")
            Return
        End If

        ' Wrap the SOURCE shape (not the cloned one) as IRenderableShape so the resolver sees
        ' the original shader with its BGSM path intact. The cloned shape's shader was already
        ' overwritten earlier in BuildCharGen with shad.Name="" (CK-faithful: bake NIFs carry
        ' material inline, not via external BGSM linkage). If we wrapped the cloned shape, the
        ' wrapper's GetRelatedMaterial would only see the inline fields and lose every BGSM
        ' field that lives outside the shader (Wrinkles texture, AO Normal slot, etc.).
        ' The resolver reads from the source NIF; we transcribe its result into the cloned
        ' shape's inline shader at the bottom of this function.
        Dim wrapper As NifRenderableShape
        Try
            wrapper = New NifRenderableShape(srcNif, srcShape, 0)
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: NifRenderableShape ctor threw: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try

        ' CBBE-style override fix mirrors MainForm.CollectHeadPartCandidate: if the HDPT is
        ' FemaleHeadHumanRearTEMP (vanilla 0x0004D0E9), the flag is False, the record comes
        ' from an override (originating plugin ≠ Fallout4.esm), and the actor is NOT
        ' Human-female, force UsesBodyTexture=True so the resolver substitutes the actor's
        ' body skin TXST. Same rule render uses; bake must mirror it because both consume
        ' the same ApplyShapeMaterialOverrides delegate downstream.
        ' Bare ID compare: load-order prefix in high byte differs per plugin (vanilla=0x00,
        ' overrides=0x01..0xFF) but the record ID is shared. Mask to low 24 bits so CBBE-style
        ' overrides (e.g. 0x0104D0E9) match the vanilla bare ID.
        Const FemaleHeadHumanRearTEMPBareID As UInteger = &H4D0E9UI
        Const HumanRaceBareID As UInteger = &H13746UI
        Dim hdptFormID = hdpt.FormID
        Dim effectiveUsesBodyTexture = hdpt.UsesBodyTexture
        If (hdptFormID And &HFFFFFFUI) = FemaleHeadHumanRearTEMPBareID AndAlso Not hdpt.UsesBodyTexture Then
            ' Override detection: PluginRecord.SourcePluginName carries the plugin that won the
            ' merge for this record (last override). GetOriginatingPluginName returns the master
            ' that owns the FormID (Fallout4.esm here) which is wrong signal for "is override".
            Dim hdptRec = pluginManager.GetRecord(hdptFormID)
            Dim sourcePlugin As String = If(hdptRec?.SourcePluginName, "")
            Dim isOverride = Not String.Equals(sourcePlugin, "Fallout4.esm", StringComparison.OrdinalIgnoreCase) AndAlso Not String.IsNullOrEmpty(sourcePlugin)
            Dim raceBare As UInteger = If(state IsNot Nothing, state.RaceFormID And &HFFFFFFUI, 0UI)
            Dim isHumanFemale = (state IsNot Nothing) AndAlso raceBare = HumanRaceBareID AndAlso state.IsFemale
            If isOverride AndAlso Not isHumanFemale Then
                effectiveUsesBodyTexture = True
                sb.AppendLine($"[BUILDCHARGEN] [CBBE-HEADREAR] forced UsesBodyTexture=True (HDPT 0x{hdptFormID:X8} override from '{sourcePlugin}', actor not Human-Female)")
            End If
        End If

        ' Build a minimal MeshCandidate from the HDPT in scope. For Build CharGen the candidate
        ' chain is straightforward (HDPT → Face/Eyes/Hair/etc.) so we don't need the full
        ' Outfit/LVLN/OBTS/OMOD resolution that the live render runs.
        Dim candidate As New MainForm.MeshCandidate With {
            .Kind = MainForm.MeshCandidateKind.HeadPart,
            .HeadPartType = hdpt.PartType,
            .HeadPartTypeRaw = hdpt.PartType,
            .TextureSetFormID = hdpt.TextureSetFormID,
            .UsesBodyTexture = effectiveUsesBodyTexture,
            .HeadPartColorFormID = hdpt.ColorFormID,
            .UseSolidTint = (hdpt.ColorFormID <> 0UI)
        }

        ' PRE-RESOLVER snapshot: what the wrapper's material looks like right after the source
        ' NIF + BGSM was loaded, BEFORE the resolver chain runs.
        Dim preMat = wrapper.ShapeMaterial?.material
        If preMat IsNot Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [PRE-RESOLVER] shape='{sourceName}' path='{If(wrapper.ShapeMaterial.path, "")}' BackLighting={preMat.BackLighting} BackLightPower={preMat.BackLightPower} RimLighting={preMat.RimLighting} RimPower={preMat.RimPower} EmitEnabled={preMat.EmitEnabled} EmittanceColor={preMat.EmittanceColor} SpecularEnabled={preMat.SpecularEnabled} SubRoll={preMat.SubsurfaceLightingRolloff} RootMat='{If(preMat.RootMaterialPath, "")}'")
        Else
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [PRE-RESOLVER] shape='{sourceName}' wrapper.ShapeMaterial.material is Nothing")
        End If

        ' Run the same per-shape resolver the render uses. Mutates wrapper.ShapeMaterial in-place.
        Try
            applyMaterialOverrides(candidate, state, {DirectCast(wrapper, IRenderableShape)})
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: ApplyShapeMaterialOverrides threw: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try

        Dim mat = wrapper.ShapeMaterial?.material
        If mat Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: resolver produced no material for '{sourceName}'")
            Return
        End If

        ' POST-RESOLVER snapshot: same fields after the resolver ran. Diff against PRE shows
        ' which fields the resolver chain (TXST.MNAM swap, MSWP swap, tint colour overrides, etc.)
        ' actually mutated. Should match what gets serialized to disk by Save_To_Shader below.
        sb.AppendLine($"[BUILDCHARGEN-MATLOG] [POST-RESOLVER] shape='{sourceName}' path='{If(wrapper.ShapeMaterial?.path, "")}' BackLighting={mat.BackLighting} BackLightPower={mat.BackLightPower} RimLighting={mat.RimLighting} RimPower={mat.RimPower} EmitEnabled={mat.EmitEnabled} EmittanceColor={mat.EmittanceColor} SpecularEnabled={mat.SpecularEnabled} SubRoll={mat.SubsurfaceLightingRolloff} RootMat='{If(mat.RootMaterialPath, "")}'")

        Dim shad = nif.GetShader(cloned)
        If shad Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: cloned shape has no shader")
            Return
        End If

        Try
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            If bsls IsNot Nothing Then
                Dim bgsm = TryCast(mat.Underlying_Material, BGSM)
                If bgsm Is Nothing Then
                    sb.AppendLine($"[BUILDCHARGEN]     mat-copy: BSLighting shader but underlying material is not BGSM ({mat.Underlying_Material?.GetType().Name})")
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
                FO4UnifiedMaterial_Class.Save_To_Shader(nif, cloned, bsls, bgsm, mat.NifShaderType, slot5Path)
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
            Else
                Dim bes = TryCast(shad, BSEffectShaderProperty)
                If bes Is Nothing Then
                    sb.AppendLine($"[BUILDCHARGEN]     mat-copy: unknown shader type {shad.GetType().Name}")
                    Return
                End If
                Dim bgem = TryCast(mat.Underlying_Material, BGEM)
                If bgem Is Nothing Then
                    sb.AppendLine($"[BUILDCHARGEN]     mat-copy: BSEffect shader but underlying material is not BGEM ({mat.Underlying_Material?.GetType().Name})")
                    Return
                End If
                FO4UnifiedMaterial_Class.Save_To_Shader(nif, cloned, bes, bgem)
                If bes.Name IsNot Nothing Then bes.Name.String = ""
            End If
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: applied render-resolved material to '{sourceName}' (shader={shad.GetType().Name})")
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN]     mat-copy: Save_To_Shader threw: {ex.GetType().Name}: {ex.Message}")
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
    ''' folder under <c>Data\Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;\&lt;formId&gt;_*_2.dds</c>,
    ''' and rewrite slots 0/1/7 of the cloned shape's TextureSet to point at the canonical
    ''' engine paths.
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
                                 npcFormID As UInteger,
                                 originPlugin As String,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 sb As StringBuilder,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing)
        ' --- 1. Resolve the face source material (D/N/S texture paths) from the source NIF. ---
        Dim relMat = srcNif.GetRelatedMaterial(srcShape)
        Dim mat = relMat?.material
        If mat Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-FACEBAKE] ABORT: srcShape has no resolved material")
            Return
        End If

        Dim diffusePath = mat.Diffuse_or_Base_Texture
        Dim normalPath = mat.NormalTexture
        Dim specPath = mat.SmoothSpecTexture
        If String.IsNullOrEmpty(diffusePath) Then
            sb.AppendLine("[BUILDCHARGEN-FACEBAKE] ABORT: face source material has empty Diffuse path")
            Return
        End If

        ' --- 2. Resolve the NPC's race + gender so we can build layers + region swaps. ---
        ' Forward the LM SkinTemplate resolver so face TXST overrides from the bundle land here
        ' (template.face[gender] → npcData.HeadTextureFormID), keeping the bake's tint inputs
        ' aligned with what the live render shows.
        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager),
            npcFormID, appliedPresets, pluginManager, lmSkinTemplateResolver)
        If npcData Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-FACEBAKE] ABORT: NPC record could not be parsed")
            Return
        End If

        Dim built = FaceTintLayerBuilder.Build(
            modelFormID:=npcFormID,
            rootFormID:=npcFormID,
            raceFormID:=npcData.RaceFormID,
            isFemale:=npcData.IsFemale,
            pluginManager:=pluginManager,
            appliedPresets:=appliedPresets,
            tintBytesCache:=Nothing)
        sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] tint inputs: {built.Layers.Count} layers, {built.RegionSwaps.Count} region swaps")

        ' --- 3. Upload face source D/N/S to GL temporaries (these are the inputs to the pipeline). ---
        Dim diffuseKey = FO4UnifiedMaterial_Class.CorrectTexturePath(diffusePath)
        Dim normalKey = FO4UnifiedMaterial_Class.CorrectTexturePath(normalPath)
        Dim specKey = FO4UnifiedMaterial_Class.CorrectTexturePath(specPath)

        Dim diffuseBytes = TryGetFilesDictionaryBytes(diffuseKey)
        Dim normalBytesArr = TryGetFilesDictionaryBytes(normalKey)
        Dim specBytesArr = TryGetFilesDictionaryBytes(specKey)
        If diffuseBytes Is Nothing Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] ABORT: diffuse '{diffuseKey}' not in FilesDictionary")
            Return
        End If
        If normalBytesArr Is Nothing Then sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] WARN: normal '{normalKey}' missing — face bake will skip slot 1")
        If specBytesArr Is Nothing Then sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] WARN: spec '{specKey}' missing — face bake will skip slot 7")

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
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] ABORT: GL upload failed: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try

        Dim tempIds As New List(Of Integer)
        Dim diffEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim normEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim specEntry As PreviewModel.Texture_Loaded_Class = Nothing
        uploaded.TryGetValue(diffuseKey, diffEntry)
        uploaded.TryGetValue(normalKey, normEntry)
        uploaded.TryGetValue(specKey, specEntry)
        If diffEntry IsNot Nothing AndAlso diffEntry.Texture_ID <> 0 Then tempIds.Add(diffEntry.Texture_ID)
        If normEntry IsNot Nothing AndAlso normEntry.Texture_ID <> 0 Then tempIds.Add(normEntry.Texture_ID)
        If specEntry IsNot Nothing AndAlso specEntry.Texture_ID <> 0 Then tempIds.Add(specEntry.Texture_ID)

        If diffEntry Is Nothing OrElse diffEntry.Texture_ID = 0 Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] ABORT: diffuse upload produced no GL texture")
            DeleteGlTextures(tempIds)
            Return
        End If

        Dim w = diffEntry.Size.Width
        Dim h = diffEntry.Size.Height
        If w <= 0 OrElse h <= 0 Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] ABORT: diffuse degenerate dims {w}x{h}")
            DeleteGlTextures(tempIds)
            Return
        End If
        sb.AppendLine($"[BUILDCHARGEN-FACEBAKE] uploaded D/N/S to GL temporals: D=#{diffEntry.Texture_ID} N=#{If(normEntry?.Texture_ID, 0)} S=#{If(specEntry?.Texture_ID, 0)} {w}x{h}")

        ' --- 4. Run the shared compositor pipeline (region-swap + tint compose). ---
        Dim pipelineLogger As Action(Of String) = Sub(msg) sb.AppendLine($"[BUILDCHARGEN-FACEBAKE/PIPELINE]{msg}")
        Dim pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
            host.CompositorState, host.TintGpuCache,
            diffEntry.Texture_ID,
            If(normEntry?.Texture_ID, 0),
            If(specEntry?.Texture_ID, 0),
            w, h,
            built.Layers, built.RegionSwaps,
            pipelineLogger)

        ' Track any fresh textures the pipeline produced so we can delete them on exit.
        Dim freshIds As New List(Of Integer)
        If pipelineResult.Diffuse.IsFresh Then freshIds.Add(pipelineResult.Diffuse.TextureId)
        If pipelineResult.Normal.IsFresh Then freshIds.Add(pipelineResult.Normal.TextureId)
        If pipelineResult.Specular.IsFresh Then freshIds.Add(pipelineResult.Specular.TextureId)

        ' --- 5. Output dir + slot plan + texture-set for slot rewrites. ---
        Dim formIdLow = (npcFormID And &HFFFFFFUI)
        Dim dataPath = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPath) Then
            sb.AppendLine("[BUILDCHARGEN-FACEBAKE] ABORT: Config_App.Current.DataPath empty — cannot resolve loose folder")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If
        Dim outDir = Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin)
        Try : Directory.CreateDirectory(outDir) : Catch : End Try

        ' Suffix gating: in DebugMode we keep the "_2" sandbox so the engine reads CK's authoritative
        ' textures and we can diff side-by-side. In release we overwrite CK's FaceCustomization
        ' (clobbering its baked diffuse/normal/spec).
        Dim suffixD = If(DebugMode, "_d_2.dds", "_d.dds")
        Dim suffixN = If(DebugMode, "_msn_2.dds", "_msn.dds")
        Dim suffixS = If(DebugMode, "_s_2.dds", "_s.dds")
        Dim slotPlan = New (Slot As Integer, ResultId As Integer, Dxgi As Integer, Suffix As String)() {
            (0, pipelineResult.Diffuse.TextureId, DirectXTextureConversionHelper.DxgiFormatBc3Unorm, suffixD),
            (1, pipelineResult.Normal.TextureId, DirectXTextureConversionHelper.DxgiFormatBc5Unorm, suffixN),
            (7, pipelineResult.Specular.TextureId, DirectXTextureConversionHelper.DxgiFormatBc5Unorm, suffixS)
        }

        Dim bsls = TryCast(nif.GetShader(cloned), BSLightingShaderProperty)
        Dim texset As BSShaderTextureSet = Nothing
        If bsls IsNot Nothing AndAlso bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index <> -1 Then
            texset = TryCast(nif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
        End If
        If texset Is Nothing OrElse texset.Textures Is Nothing Then
            sb.AppendLine("[BUILDCHARGEN-FACEBAKE] cloned shape has no BSShaderTextureSet — cannot rewrite slot paths")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If

        ' --- 6. Per-slot: readback → encode → write → rewrite slot path → diff vs CK. ---
        For Each entry In slotPlan
            If entry.ResultId = 0 Then
                sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]   slot[{entry.Slot}] no GL output (no source/contribution) — skipping")
                Continue For
            End If

            Dim bgra(w * h * 4 - 1) As Byte
            Try
                GL.BindTexture(TextureTarget.Texture2D, entry.ResultId)
                Dim handle = Runtime.InteropServices.GCHandle.Alloc(bgra, Runtime.InteropServices.GCHandleType.Pinned)
                Try
                    GL.GetTexImage(TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject())
                Finally
                    handle.Free()
                End Try
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]   slot[{entry.Slot}] GetTexImage failed: {ex.GetType().Name}: {ex.Message}")
                Continue For
            End Try

            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(w, h), 2))) + 1
            Dim ddsBytes As Byte() = Nothing
            Try
                ddsBytes = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                    width:=w, height:=h, bgraPixels:=bgra,
                    outputDxgiFormat:=entry.Dxgi,
                    generateMipMaps:=True, generatedMipLevels:=mipLevels)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]   slot[{entry.Slot}] encode (DXGI={entry.Dxgi}, mips={mipLevels}) failed: {ex.GetType().Name}: {ex.Message}")
                Continue For
            End Try

            Dim outFile = Path.Combine(outDir, $"{formIdLow:X8}{entry.Suffix}")
            Try
                File.WriteAllBytes(outFile, ddsBytes)
            Catch ex As Exception
                sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]   slot[{entry.Slot}] write '{outFile}' failed: {ex.GetType().Name}: {ex.Message}")
                Continue For
            End Try

            Dim canonicalNifPath = $"Data\Textures\Actors\Character\FaceCustomization\{originPlugin}\{formIdLow:X8}{entry.Suffix}"
            While texset.Textures.Count <= entry.Slot
                texset.Textures.Add(New NiflySharp.NiString4 With {.Content = ""})
            End While
            If texset.Textures(entry.Slot) Is Nothing Then
                texset.Textures(entry.Slot) = New NiflySharp.NiString4 With {.Content = canonicalNifPath}
            Else
                texset.Textures(entry.Slot).Content = canonicalNifPath
            End If

            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]   slot[{entry.Slot}] {w}x{h} → '{outFile}' ({ddsBytes.Length} B, DXGI={entry.Dxgi}, mips={mipLevels}); shader path → '{canonicalNifPath}'")

            If DebugMode Then
                Try
                    Dim ckDdsBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(canonicalNifPath))
                    If ckDdsBytes IsNot Nothing AndAlso ckDdsBytes.Length > 128 Then
                        LogFaceBakeBgraDiff(entry.Slot, w, h, bgra, ckDdsBytes, sb)
                    Else
                        sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK reference '{canonicalNifPath}' not in FilesDictionary, skipping diff")
                    End If
                Catch ex As Exception
                    sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK diff failed: {ex.GetType().Name}: {ex.Message}")
                End Try
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
                NpcPreviewLog.Log($"[BUILDCHARGEN-FACEBAKE] TryGetFilesDictionaryBytes miss: '{normalizedKey}' (Nothing or empty)")
                Return Nothing
            End If
            Return bytes
        Catch ex As Exception
            NpcPreviewLog.Log($"[BUILDCHARGEN-FACEBAKE] TryGetFilesDictionaryBytes threw on '{normalizedKey}': {ex.GetType().Name}: {ex.Message}")
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

    ''' <summary>Decode the CK reference DDS (uncompressed BGRA8 per DDSPROBE: fourCC='    ',
    ''' 1 mip, 4 bytes/pixel) and compare per channel against our composited BGRA buffer.
    ''' Logs RMS per B/G/R/A channel and overall. The CK file layout is documented in DDSPROBE
    ''' so we can decode it inline without going through DirectXTex.</summary>
    Private Sub LogFaceBakeBgraDiff(slot As Integer, w As Integer, h As Integer, ourBgra As Byte(), ckDds As Byte(), sb As StringBuilder)
        ' Validate CK header fingerprint: 'DDS ', dims, no FourCC compression, expected size.
        If ckDds.Length < 128 + w * h * 4 Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK ref too small ({ckDds.Length} B, expected >={128 + w * h * 4})")
            Return
        End If
        Dim ckW = BitConverter.ToInt32(ckDds, 16)
        Dim ckH = BitConverter.ToInt32(ckDds, 12)
        If ckW <> w OrElse ckH <> h Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK ref dims {ckW}x{ckH} ≠ ours {w}x{h} — skipping diff")
            Return
        End If
        Dim fourCC = System.Text.Encoding.ASCII.GetString(ckDds, 84, 4)
        If fourCC <> "    " Then
            sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK ref fourCC='{fourCC}' (not raw BGRA) — diff requires decoding, skipping")
            Return
        End If
        Dim ckPixelOffset = 128
        Dim n = w * h
        Dim sumB As Double = 0, sumG As Double = 0, sumR As Double = 0, sumA As Double = 0
        Dim maxB As Integer = 0, maxG As Integer = 0, maxR As Integer = 0, maxA As Integer = 0
        For i = 0 To n - 1
            Dim pi = i * 4
            Dim ci = ckPixelOffset + pi
            Dim db = CInt(ourBgra(pi)) - CInt(ckDds(ci))
            Dim dg = CInt(ourBgra(pi + 1)) - CInt(ckDds(ci + 1))
            Dim dr = CInt(ourBgra(pi + 2)) - CInt(ckDds(ci + 2))
            Dim da = CInt(ourBgra(pi + 3)) - CInt(ckDds(ci + 3))
            sumB += db * db : sumG += dg * dg : sumR += dr * dr : sumA += da * da
            If Math.Abs(db) > maxB Then maxB = Math.Abs(db)
            If Math.Abs(dg) > maxG Then maxG = Math.Abs(dg)
            If Math.Abs(dr) > maxR Then maxR = Math.Abs(dr)
            If Math.Abs(da) > maxA Then maxA = Math.Abs(da)
        Next
        Dim rmsB = Math.Sqrt(sumB / n), rmsG = Math.Sqrt(sumG / n), rmsR = Math.Sqrt(sumR / n), rmsA = Math.Sqrt(sumA / n)
        Dim rmsTotal = Math.Sqrt((sumB + sumG + sumR + sumA) / (4.0 * n))
        sb.AppendLine($"[BUILDCHARGEN-FACEBAKE]     CK diff slot[{slot}]: RMS B={rmsB:F2} G={rmsG:F2} R={rmsR:F2} A={rmsA:F2} total={rmsTotal:F2} (0-255 scale); max B={maxB} G={maxG} R={maxR} A={maxA}")
    End Sub

    ''' <summary>Log the shader-inline + related-material lighting fields for a shape, tagged
    ''' with a stage label (e.g. "SOURCE-LOAD" right after Load_Manolo). Used to track where
    ''' material values diverge across the bake pipeline: load → resolver → TXST.MNAM swap →
    ''' MSWP swap → final embed. Comparing the same shape's tag-by-tag lines tells us which
    ''' stage mutated each field.</summary>
    Friend Sub LogShapeLoadedMaterial(nif As Nifcontent_Class_Manolo, shape As INiShape, stageTag As String, sb As StringBuilder)
        If nif Is Nothing OrElse shape Is Nothing Then Return
        Dim shapeName = If(shape.Name?.String, "<unnamed>")

        ' Shader inline (raw values written into the BSLightingShaderProperty / BSEffectShader
        ' inside the NIF — what the engine actually reads at draw time when no external BGSM
        ' is consulted). Independent of the .bgsm on disk.
        Try
            Dim shad = nif.GetShader(shape)
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            If bsls IsNot Nothing Then
                Dim shaderName = If(bsls.Name?.String, "")
                sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}] shape='{shapeName}' shader=BSLighting type={bsls.ShaderType_SK_FO4} Name='{shaderName}'")
                sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   shader-inline Emissive={bsls.Emissive} EmissiveColor={bsls.EmissiveColor} EmissiveMultiple={bsls.EmissiveMultiple} HasRimlight={bsls.HasRimlight} RimlightPower={bsls.RimlightPower} HasBacklight={bsls.HasBacklight} BacklightPower={bsls.BacklightPower} HasSoftlight={bsls.HasSoftlight} SubsurfaceRolloff={bsls.SubsurfaceRolloff} HasSpecular={bsls.HasSpecular} HasGlowmap={bsls.HasGlowmap} HasEnvironmentMapping={bsls.HasEnvironmentMapping} HasGreyscaleToPaletteColor={bsls.HasGreyscaleToPaletteColor}")
            Else
                Dim bes = TryCast(shad, BSEffectShaderProperty)
                If bes IsNot Nothing Then
                    Dim shaderName = If(bes.Name?.String, "")
                    sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}] shape='{shapeName}' shader=BSEffect Name='{shaderName}'")
                Else
                    sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}] shape='{shapeName}' shader=<unknown {shad?.GetType().Name}>")
                End If
            End If
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   shader inline read failed: {ex.GetType().Name}: {ex.Message}")
        End Try

        ' Related-material (what GetRelatedMaterial resolves: shader inline + BGSM file on disk
        ' merged into a unified material).  This is the value the resolver chain consumes.
        Try
            Dim rel = nif.GetRelatedMaterial(shape)
            If rel Is Nothing OrElse rel.material Is Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   related-material: Nothing")
                Return
            End If
            Dim mat = rel.material
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   related-material path='{If(rel.path, "")}' shaderType={mat.NifShaderType} Hair={mat.Hair} SkinTint={mat.SkinTint} Glowmap={mat.Glowmap} EnvironmentMapping={mat.EnvironmentMapping}")
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   related-material EmitEnabled={mat.EmitEnabled} EmittanceColor={mat.EmittanceColor} EmittanceMult={mat.EmittanceMult} RimLighting={mat.RimLighting} RimPower={mat.RimPower} BackLighting={mat.BackLighting} BackLightPower={mat.BackLightPower} SpecularEnabled={mat.SpecularEnabled} SubsurfaceLighting={mat.SubsurfaceLighting} SubsurfaceLightingRolloff={mat.SubsurfaceLightingRolloff} RootMaterialPath='{mat.RootMaterialPath}'")
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   related-material textures D='{mat.Diffuse_or_Base_Texture}' N='{mat.NormalTexture}' S='{mat.SmoothSpecTexture}' Glow='{mat.GlowTexture}' Greyscale='{mat.GreyscaleTexture}' Envmap='{mat.EnvmapTexture}' EnvMask='{mat.EnvmapMaskTexture}' Wrinkles='{mat.WrinklesTexture}'")
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-MATLOG] [{stageTag}]   related-material read failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

End Module
