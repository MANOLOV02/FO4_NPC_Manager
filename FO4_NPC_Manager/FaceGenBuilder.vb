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
    End Class

    ''' <summary>Dual-mode bake toggle. False (release, default): output canonical paths
    ''' (<formID>.nif + _d.dds / _msn.dds / _s.dds) — pisa el CK BA2 bake; el engine in-game
    ''' usa nuestro output. True (debug): output sandbox (<formID>_2.nif + _d_2.dds etc.)
    ''' alongside CK's; comparator se dispara contra el CK BA2 baseline y loguea
    ''' <c>[BUILDCHARGEN-DIFF]</c>. Re-activar a True sólo para diagnóstico contra CK
    ''' (ver arch_facegen_debug_mode memory). Toggle programático:
    ''' <c>FaceGenBuilder.DebugMode = True</c>.</summary>
    ' TEMP: True para habilitar el comparador de texturas [FACEBAKE-TEXDIFF] + diff de geometría
    ' [BUILDCHARGEN-DIFF] (output sandbox _2, no pisa CK). Volver a False para release.
    Public Property DebugMode As Boolean = True

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
            nif.Create(NiVersion.GetFO4(), withRootNode:=True)
        Catch ex As Exception
            result.Summary = $"Failed to create FaceGen NIF shell: {ex.Message}"
            Return result
        End Try

        ' Build the canonical HDPT chain for this NPC. Each entry has its MeshPath and (later)
        ' chargen TRI / FMRS info. This is the AUTHORITATIVE list — the .nif2 contains exactly
        ' the shapes that come out of these sources.
        Dim hdptMap = BuildAllowedShapeMap(npcFormID, pluginManager)

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
        For Each kv In hdptMap.OrderBy(Function(p) p.Value.Hdpt.PartType).ThenBy(Function(p) p.Key)
            Dim hdptName = kv.Key
            Dim hdpt = kv.Value.Hdpt
            Dim effectiveHeadPartType = kv.Value.EffectivePartType
            If String.IsNullOrEmpty(hdpt.MeshPath) Then
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

            Dim srcNif As Nifcontent_Class_Manolo = Nothing
            If Not loadedSources.TryGetValue(sourceKey, srcNif) Then
                Dim srcBytes As Byte() = Nothing
                Try
                    srcBytes = FilesDictionary_class.GetBytes(sourceKey)
                Catch ex As Exception
                End Try
                If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
                    hdptSourceMissing += 1
                    Continue For
                End If
                srcNif = New Nifcontent_Class_Manolo()
                Try
                    srcNif.Load_Manolo(srcBytes)
                Catch ex As Exception
                    hdptSourceLoadFail += 1
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
                Try
                    Dim cloned = nif.CloneShape_Original(srcShape, destName, srcNif)
                    If cloned IsNot Nothing Then
                        clonedShapeNames.Add(destName)
                        shapesCloned += 1

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
                        ApplyRenderResolvedMaterialToShape(nif, cloned, srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides)

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
                                             npcFormID, originPlugin,
                                             pluginManager, appliedPresets, host,
                                             state,
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

        result.ShapesKept = shapesCloned
        result.ShapesDropped = 0

        ' Output path:
        '   DebugMode=False (default): <formID>.nif → pisa el CK bake; engine usa este al cargar.
        '   DebugMode=True: <formID>_2.nif → sandbox al lado del CK bake, sin pisar; engine
        '                   sigue usando el CK; el comparator diff-ea against CK BA2 baseline.
        Dim formIdLow = (npcFormID And &HFFFFFFUI)
        Dim dataPathForNif = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPathForNif) Then
            result.Summary = "DataPath unset; cannot write .nif"
            Return result
        End If
        Dim nifFileName = If(DebugMode, $"{formIdLow:X8}_2.nif", $"{formIdLow:X8}.nif")
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
    Private Sub ApplyRenderResolvedMaterialToShape(nif As Nifcontent_Class_Manolo,
                                                    cloned As INiShape,
                                                    srcNif As Nifcontent_Class_Manolo,
                                                    srcShape As INiShape,
                                                    hdpt As HDPT_Data,
                                                    effectiveHeadPartType As Integer,
                                                    state As MainForm.NPCVisualState,
                                                    pluginManager As PluginManager,
                                                    applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate)
        Dim sourceName As String = If(cloned.Name?.String, "")
        If applyMaterialOverrides Is Nothing Then
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
            End If
        End If

        ' Build a minimal MeshCandidate from the HDPT in scope. For Build CharGen the candidate
        ' chain is straightforward (HDPT → Face/Eyes/Hair/etc.) so we don't need the full
        ' Outfit/LVLN/OBTS/OMOD resolution that the live render runs.
        ' HeadPartType = EFFECTIVE type (Misc hairline under hair → Hair=3) so the shared
        ' material resolver colors sub-parts like the render does (e.g. hair palette on the
        ' hairline). HeadPartTypeRaw keeps the HDPT's own type for any raw-type logic downstream.
        Dim candidate As New MainForm.MeshCandidate With {
            .Kind = MainForm.MeshCandidateKind.HeadPart,
            .HeadPartType = effectiveHeadPartType,
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
        Else
        End If

        ' Run the same per-shape resolver the render uses. Mutates wrapper.ShapeMaterial in-place.
        Try
            applyMaterialOverrides(candidate, state, {DirectCast(wrapper, IRenderableShape)})
        Catch ex As Exception
            Return
        End Try

        Dim mat = wrapper.ShapeMaterial?.material
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
                                 state As MainForm.NPCVisualState,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing)
        Logger.LogLazy(Function() $"[FACEBAKE] enter npcFormID=0x{npcFormID:X8} originPlugin='{originPlugin}' srcShape='{srcShape?.Name?.ToString()}'")
        ' --- 1. Resolve the face source material (D/N/S texture paths) from the source NIF. ---
        Dim relMat = srcNif.GetRelatedMaterial(srcShape)
        Dim mat = relMat?.material
        If mat Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: source material is Nothing (npcFormID=0x{npcFormID:X8})")
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
        Dim npcData = NpcRecordOverlay.ApplyPresetOverlayToNpcData(
            NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager),
            npcFormID, appliedPresets, pluginManager, lmSkinTemplateResolver)
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
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse GL texture id 0 (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds)
            Return
        End If

        Dim w = diffEntry.Size.Width
        Dim h = diffEntry.Size.Height
        If w <= 0 OrElse h <= 0 Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse size {w}x{h} (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds)
            Return
        End If

        ' --- 4. Run the shared compositor pipeline (region-swap + tint compose). ---
        ' TEMP DEBUG: enable per-layer/per-swap delta logging ONLY for the bake (never render),
        ' and only in DebugMode. The compositor double-gates the readback on this flag AND
        ' Logger.Enabled, so it costs nothing in release. Restored in Finally.
        Dim prevPerLayerDiffLog = FaceTintCompositor.PerLayerDiffLog
        If DebugMode Then FaceTintCompositor.PerLayerDiffLog = True
        Dim pipelineResult As FaceTintCompositor.FaceTintPipelineResult
        Try
            pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                host.CompositorState, host.TintGpuCache,
                diffEntry.Texture_ID,
                If(normEntry?.Texture_ID, 0),
                If(specEntry?.Texture_ID, 0),
                w, h,
                built.Layers, built.RegionSwaps)
        Finally
            FaceTintCompositor.PerLayerDiffLog = prevPerLayerDiffLog
        End Try

        ' Track any fresh textures the pipeline produced so we can delete them on exit.
        Dim freshIds As New List(Of Integer)
        If pipelineResult.Diffuse.IsFresh Then freshIds.Add(pipelineResult.Diffuse.TextureId)
        If pipelineResult.Normal.IsFresh Then freshIds.Add(pipelineResult.Normal.TextureId)
        If pipelineResult.Specular.IsFresh Then freshIds.Add(pipelineResult.Specular.TextureId)

        ' --- 5. Output dir + slot plan + texture-set for slot rewrites. ---
        Dim formIdLow = (npcFormID And &HFFFFFFUI)
        Dim dataPath = Config_App.Current.DataPath
        If String.IsNullOrEmpty(dataPath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: Config_App.Current.DataPath empty (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If
        Dim outDir = Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin)
        Try : Directory.CreateDirectory(outDir) : Catch : End Try

        ' Suffix:
        '   DebugMode=False (default): _d.dds / _msn.dds / _s.dds → pisa CK textures.
        '   DebugMode=True: _d_2.dds / _msn_2.dds / _s_2.dds → sandbox alongside CK.
        ' El NIF emitido referencia el suffix que corresponde (ver canonicalNifPath abajo).
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
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: cloned shape has no BSShaderTextureSet (npcFormID=0x{npcFormID:X8})")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If

        ' --- 6. Per-slot: readback → encode → write → rewrite slot path → diff vs CK. ---
        For Each entry In slotPlan
            If entry.ResultId = 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE] slot {entry.Slot}{entry.Suffix}: pipeline produced no texture (ResultId=0) — SKIPPED (npcFormID=0x{npcFormID:X8})")
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
                Continue For
            End Try

            ' (Texture pixel comparison vs CK now lives in FaceGenComparator's [BUILDCHARGEN-DIFF],
            ' loading from each NIF shader's ACTUAL texture path -- not a convention name.)

            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(w, h), 2))) + 1
            Dim ddsBytes As Byte() = Nothing
            Try
                ddsBytes = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                    width:=w, height:=h, bgraPixels:=bgra,
                    outputDxgiFormat:=entry.Dxgi,
                    generateMipMaps:=True, generatedMipLevels:=mipLevels)
            Catch ex As Exception
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

            Dim canonicalNifPath = $"Data\Textures\Actors\Character\FaceCustomization\{originPlugin}\{formIdLow:X8}{entry.Suffix}"
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
                Return Nothing
            End If
            Return bytes
        Catch ex As Exception
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

    ''' <summary>Log the shader-inline + related-material lighting fields for a shape, tagged
    ''' with a stage label (e.g. "SOURCE-LOAD" right after Load_Manolo). Used to track where
    ''' material values diverge across the bake pipeline: load → resolver → TXST.MNAM swap →
    ''' MSWP swap → final embed. Comparing the same shape's tag-by-tag lines tells us which
    ''' stage mutated each field.</summary>

End Module
