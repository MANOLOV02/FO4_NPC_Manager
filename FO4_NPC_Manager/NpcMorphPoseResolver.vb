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

''' <summary>Phase 2 of the MainForm split: morph + pose resolution (face/body morph resolvers,
''' FMRS face-bone transforms, body-weight data, race height, merged NPC pose, facial-bone regions).
''' Standalone class, DI. Skeleton LOADING (PrepareSkeleton) + its caches stay in MainForm.
''' See project_mainform_split.</summary>
Friend NotInheritable Class NpcMorphPoseResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _overlay As Func(Of NPC_Data, UInteger, NPC_Data)
    Private ReadOnly _hostProvider As Func(Of NpcRenderHost)
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Public Sub New(ctx As NpcRenderContext, overlay As Func(Of NPC_Data, UInteger, NPC_Data), hostProvider As Func(Of NpcRenderHost),
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset))
        _ctx = ctx
        _overlay = overlay
        _hostProvider = hostProvider
        _appliedPresets = appliedPresets
    End Sub

    ''' <summary>Build a face morph resolver for the given NPC visual state.
    ''' Uses MSDK/MSDV morph presets from Chargen.tri (via RACE mapping) and
    ''' FMRI/FMRS face bone transforms (applied via skeleton DeltaTransform).
    ''' Body weight morphs are NOT applied (vanilla uses hardcoded bone scaling, not TRI).</summary>
    Friend Function BuildFaceMorphResolver(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing Then Return Nothing

        ' Get the full NPC_Data for the model source (the NPC whose face we're rendering)
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' No morph data at all? Skip
        If npcData.MorphValues.Count = 0 Then Return Nothing

        ' Get RACE morph definitions for mapping MSDK keys ? morph names
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

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
    Friend Function BuildBodyMorphResolver(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing OrElse renderData Is Nothing Then Return Nothing
        If Not host.Toggles.BodyTri Then Return Nothing
        Dim sliders = GetEffectiveBodyMorphSliders(state.RootNpcFormID)
        If sliders Is Nothing OrElse sliders.Count = 0 Then Return Nothing
        Return New BodySlideMorphResolver(sliders, renderData.MeshDictKeys)
    End Function

    ''' <summary>Build the hair zap resolver from the per-shape ShapeZapHairParts map, gated on the
    ''' "Render headwear" toggle. Returns Nothing when headwear rendering is OFF (the zap must lift so the
    ''' mesh shows whole) or when no shape carries a non-None ZapParts. Also flips shape.ApplyZaps for the
    ''' flagged shapes so the renderer honours the VertexMask=-1 the resolver's zap channel sets.</summary>
    Friend Function BuildHairTopZapResolver(renderData As MainForm.PreviewResolutionResult, host As NpcRenderHost) As HairTopZapResolver
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
    Private Function BuildFaceBoneTransforms(state As MainForm.NPCVisualState) As Poses_class
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return Nothing

        ' NNAM ("Neck Fat Adjustments Scale") is NOT part of this FMRS face-bone pose anymore — it is
        ' a CK RUNTIME scale of the shared "Neck" bone applied to the live skeleton (head+body), now
        ' threaded through BuildBodyWeightPose as Layer 2 (see ResolveNeckNnamScale).
        Return FaceBonePoseBuilder.BuildFaceBoneTransforms(npcData, regionsFile)
    End Function

    ''' <summary>Resolve the CK RUNTIME NNAM neck-bone scale for the NPC (the shared "Neck" bone
    ''' scale the engine applies live to head+body — NEVER baked). Mirrors the resolution the FMRS
    ''' wrapper does (overlay-applied NPC, cached RACE, race+gender regions JSON, RACE.{gender}NeckNNAMX/Y)
    ''' and delegates the math to <see cref="FaceBonePoseBuilder.ComputeNeckNnamScale"/>. Returns
    ''' (1,1) on any missing piece. Consumed by <see cref="BuildBodyWeightPose"/> (Layer 2).</summary>
    Private Function ResolveNeckNnamScale(state As MainForm.NPCVisualState) As (ScaleY As Single, ScaleZ As Single)
        If state Is Nothing Then Return (1.0F, 1.0F)

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return (1.0F, 1.0F)

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return (1.0F, 1.0F)
        Dim race = _ctx.ParseRaceCached(raceRec)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return (1.0F, 1.0F)

        Dim neckNnamX As Single = If(state.IsFemale, race.FemaleNeckNNAMX, race.MaleNeckNNAMX)
        Dim neckNnamY As Single = If(state.IsFemale, race.FemaleNeckNNAMY, race.MaleNeckNNAMY)

        Return FaceBonePoseBuilder.ComputeNeckNnamScale(npcData, regionsFile, neckNnamX, neckNnamY)
    End Function

    ''' <summary>POST-PASE de la compensación NNAM anti-propagación. Llamar JUSTO DESPUÉS de
    ''' <c>ApplyBoneMorphPose</c> sobre el MISMO skeleton (BuildBodyWeightPose ya metió el scale del
    ''' NNAM en el hueso "Neck", Layer 2). Como <c>GetGlobalTransform</c> compone la cadena de padres,
    ''' esa escala PROPAGARÍA a los hijos (Neck → HEAD_Offset → HEAD → cara) = el bug "cara adelante".
    ''' Para cancelarla, a CADA hijo DIRECTO de "Neck" se le compone <c>comp = L_C⁻¹ ∘ S⁻¹ ∘ L_C</c>
    ''' sobre su MorphDelta existente (el FMRS que ApplyBoneMorphPose ya aplicó):
    ''' <c>MorphDelta_C' = comp ∘ FMRS_C</c>. Resultado: la escala queda SOLO en los verts pegados a
    ''' "Neck"; cara/cuello y sus morphs FMRS intactos. <c>comp</c> puede tener SHEAR (hijos rotados,
    ''' p.ej. skin_bone_*Neckmuscle*) → se asigna DIRECTO a <c>MorphDeltaTransform</c> (PoseTransformData
    ''' no representa shear). Orden de composición verificado numéricamente (Tools/NifVtxCompare --verifycomp).
    ''' <para>GATEO AUTOMÁTICO: la S se LEE de <c>neckBone.MorphDeltaTransform</c> (lo que el "Neck"
    ''' realmente recibió), NO se re-resuelve. Si el "Neck" no escaló (body-weight OFF — el scale se emite
    ''' solo dentro de la rama bodyWeightEnabled/hasSculpt de BuildMergedNpcPose —, NNAM inactivo, o
    ''' suprimido) su MorphDeltaTransform es Nothing → NO-OP. Así la comp (S⁻¹) NUNCA se aplica sin la S
    ''' correspondiente en el padre.</para>
    ''' Idempotencia: re-correr tras cada ApplyBoneMorphPose (que resetea la capa morph) — NO llamar dos
    ''' veces sin re-aplicar la pose (compondría la comp dos veces).</summary>
    Friend Sub ApplyNeckNnamCompensation(skeleton As SkeletonInstance)
        If skeleton Is Nothing OrElse Not skeleton.HasSkeleton Then Return
        Dim neckBone As HierarchiBone_class = Nothing
        If Not skeleton.SkeletonDictionary.TryGetValue("Neck", neckBone) OrElse neckBone Is Nothing Then Return

        ' S = lo que el "Neck" REALMENTE recibió en la capa morph (= la escala NNAM), leído del estado ya
        ' aplicado por ApplyBoneMorphPose. Derivarlo de acá (en vez de re-resolver el NNAM) AUTO-GATEA la
        ' comp con el scale: si el "Neck" NO escaló — body-weight OFF (el scale se emite solo dentro de la
        ' rama bodyWeightEnabled/hasSculpt de BuildMergedNpcPose), NNAM inactivo, o suprimido → sin entry en
        ' la pose → MorphDeltaTransform = Nothing → NO se compensa nada. Evita el bug de aplicar S⁻¹ a los
        ' hijos sin la S correspondiente en el padre (encogería la cara con body-weight destildado).
        Dim s = neckBone.MorphDeltaTransform
        If s Is Nothing Then Return
        Dim sInv = s.Inverse()

        Dim applied As Integer = 0
        For Each child In neckBone.Childrens
            If child Is Nothing OrElse child.OriginalLocaLTransform Is Nothing Then Continue For
            Dim lc = child.OriginalLocaLTransform
            ' comp = L_C⁻¹ ∘ S⁻¹ ∘ L_C — cancela la propagación del morph del "Neck" al subárbol del hijo.
            Dim comp = lc.Inverse().ComposeTransforms(sInv).ComposeTransforms(lc)
            Dim existing = child.MorphDeltaTransform
            ' MorphDelta_C' = comp ∘ (morph previo del hijo, p.ej. FMRS) — preserva su deformación.
            child.MorphDeltaTransform = If(existing Is Nothing, comp, comp.ComposeTransforms(existing))
            applied += 1
        Next
        Dim a = applied, ev = s.EffectiveScale
        Logger.LogLazy(Function() $"[NNAM-COMP] post-pase: comp (S_efectiva={ev}) aplicado a {a} hijo(s) directo(s) de 'Neck' (∘ morph previo).")
    End Sub

    ''' <summary>Resolve the NPC's MWGT weights and the RACE's per-bone weight scale data for
    ''' use by the skeleton resolver. Returns Nothing if the NPC has no MWGT or the RACE has
    ''' no bone data for the NPC's gender.</summary>
    Private Function ResolveBodyWeightData(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult) As (Wt As Single, Wm As Single, Wf As Single, GenderBlock As RACE_BoneDataGender, MrsvValues As List(Of Single), ArmaDeltas As Dictionary(Of String, System.Numerics.Vector3))
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
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

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        ' Log the FaceGen clamps for reference. TBD whether they apply to body BSMS output
        ' or only to face slider*FMIN. Not applying any clamp formula without spec.
        ' NNAM ("Neck Fat Adjustments Scale") is resolved separately (ResolveNeckNnamScale) and
        ' threaded into BuildBodyWeightPose as Layer 2 — the CK RUNTIME scale of the shared "Neck"
        ' bone (head+body), NOT a per-bone BSMS/MRSV body-weight input, so it is not part of this
        ' RACE.BoneData resolution.

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
                If bd.Bones.Count > 0 Then Return (wt, wm, wf, bd, npcData.BodyMorphRegionValues, armaDeltas)
                Exit For
            End If
        Next
        If hasArmaDeltas Then
            Return (wt, wm, wf, New RACE_BoneDataGender With {.Gender = targetGender}, npcData.BodyMorphRegionValues, armaDeltas)
        End If
        Return Nothing
    End Function

    ''' <summary>Read race height (Male/Female Height from RACE.DATA) for the NPC's race. 1.0 if unknown.</summary>
    Private Function GetRaceHeight(state As MainForm.NPCVisualState) As Single
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return 1.0F
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 1.0F
        Dim race = _ctx.ParseRaceCached(raceRec)
        Dim h = If(state.IsFemale, race.FemaleHeight, race.MaleHeight)
        If h <= 0 Then Return 1.0F
        Return h
    End Function

    Friend Function BuildMergedNpcPose(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult,
                                        faceMorphsEnabled As Boolean,
                                        bodyWeightEnabled As Boolean,
                                        skeleton As SkeletonInstance,
                                        Optional armaSculptOverride As Dictionary(Of String, System.Numerics.Vector3) = Nothing,
                                        Optional suppressNeckNnam As Boolean = False) As Poses_class
        Dim racePose = PoseMath.BuildRaceHeightPose(GetRaceHeight(state))

        ' Body-weight (RACE.BSMS/MRSV) + ARMA sculpt. Sclpt y BW son toggles independientes:
        ' weightLayersEnabled=bodyWeightEnabled gobierna RACE.BSMS/MRSV; la capa ARMA se aplica si hay
        ' deltas (por eso el OrElse hasSculpt: un outfit con sculpt y BW=OFF igual arma la pose).
        Dim bwPose As Poses_class = Nothing
        Dim hasSculpt = (armaSculptOverride IsNot Nothing AndAlso armaSculptOverride.Count > 0)
        If bodyWeightEnabled OrElse hasSculpt Then
            Dim bwData = ResolveBodyWeightData(state, renderData)
            If bwData.GenderBlock IsNot Nothing Then
                Dim sculpt = If(armaSculptOverride, New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase))
                bwPose = PoseMath.BuildBodyWeightPose(bwData.Wt, bwData.Wm, bwData.Wf,
                                             bwData.GenderBlock, bwData.MrsvValues, sculpt,
                                             skeleton, bodyWeightEnabled)
            End If
        End If

        ' NNAM (neck-fat) — gateado SOLO por Apply Body Weight, INDEPENDIENTE de sculpt y de
        ' MWGT/RACE.BoneData (antes heredaba esos couplings por compartir BuildBodyWeightPose). Es el
        ' slider de cuello del chargen: block2 = FMRS PositionZ de la región IsNeckRegion × RACE.NNAM
        ' (ResolveNeckNnamScale). Se emite como su propio entry del hueso "Neck"; la anti-propagación a
        ' los hijos la hace el post-pase NpcMorphPoseResolver.ApplyNeckNnamCompensation (que lee esta S
        ' del "Neck" aplicado → auto-gateada: si acá no se emite, no hay comp).
        ' ⚠ NO se afirma que sea el mecanismo del engine (consumidor del +0x50 nunca hallado); es la
        ' compensación por-pose que da el resultado observable correcto (cara no se infla; escala solo
        ' los verts pegados al "Neck").
        Dim nnamPose As Poses_class = Nothing
        If bodyWeightEnabled AndAlso Not suppressNeckNnam Then
            Dim neckScale = ResolveNeckNnamScale(state)
            If neckScale.ScaleY <> 1.0F OrElse neckScale.ScaleZ <> 1.0F Then
                nnamPose = New Poses_class With {
                    .Name = "NNAM Neck",
                    .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
                    .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
                }
                nnamPose.Transforms("Neck") = New PoseTransformData With {.ScaleX = 1.0F, .ScaleY = neckScale.ScaleY, .ScaleZ = neckScale.ScaleZ}
            End If
        End If

        Dim fmrsPose As Poses_class = Nothing
        If faceMorphsEnabled Then
            fmrsPose = BuildFaceBoneTransforms(state)
        End If

        Return PoseMath.MergePoses(racePose, bwPose, nnamPose, fmrsPose)
    End Function

End Class
