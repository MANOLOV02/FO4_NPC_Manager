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

''' <summary>Pure stateless pose / skeleton math extracted from MainForm (no instance state, no UI).
''' Real separate class (NOT a partial). Body-weight (MWGT x BSMS + MRSV + ARMA + NNAM neck), race
''' height, and field-level pose merge. See project_mainform_split.</summary>
Friend NotInheritable Class PoseMath
    Private Sub New()
    End Sub

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
    ' 2026-06-17: ClampBoth → Off por RE de Fallout4.exe (project_morph_clamp_re). La cadena completa
    ' del body-weight apply (0x6E0820→0x652100→0x6517A0→0x664850) NO tiene NI UN minss/maxss: el engine
    ' NO clampea el scale al RangeModifier. La "centralidad" la da el término K (Layer 1), no un clamp.
    Private Shared ReadOnly _bodyWeightClampModel As BodyWeightClampModel = BodyWeightClampModel.Off

    ''' <summary>Walk the skeleton hierarchy from a bone upward to determine which MRSV body
    ''' morph region (0..4) the bone belongs to. Returns -1 if no known region ancestor found.
    ''' The mapping is based on matching ancestor bone names to the major skeleton "trunk" bones:
    '''   HEAD → 0 (Head)
    '''   Chest, SPINE2, Neck → 1 (Upper Torso)
    '''   Arm (anywhere in ancestor chain) → 2 (Arms)
    '''   SPINE1, Pelvis, Butt (anywhere in ancestor chain) → 3 (Lower Torso)
    '''   Leg (anywhere in ancestor chain) → 4 (Legs)
    ''' This is inferred from the skeleton hierarchy, not from any Bethesda data file.</summary>
    ''' <summary>Bone→MRSV-region map, EXACT from CreationKit.exe (fn 0xA95C70 builds this hardcoded
    ''' table into a global map at RVA 0x3BA4330; see memory project_morph_clamp_re). The 48 body
    ''' "_skin" bones each carry a hardcoded region index 0..4 (0=Head, 1=Upper Torso, 2=Arms,
    ''' 3=Lower Torso, 4=Legs), matching NPC_.MRSV[0..4]. Replaces the old name-substring heuristic,
    ''' which mis-assigned Pelvis*/ButtFat* (engine→Legs, heuristic→Lower Torso) and Neck1 (engine→
    ''' Head, heuristic→Upper Torso). NOT data-driven in the engine — purely hardcoded.</summary>
    Private Shared ReadOnly _mrsvRegionByBone As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase) From {
        {"Head_skin", 0}, {"Face_skin", 0}, {"Neck1_skin", 0},
        {"Neck_skin", 1}, {"chest_skin", 1}, {"LBreast_skin", 1}, {"RBreast_skin", 1},
        {"Chest_Rear_Skin", 1}, {"Chest_Upper_Skin", 1}, {"Neck_Low_skin", 1},
        {"Spine2_skin", 1}, {"UpperBelly_skin", 1}, {"Spine2_Rear_skin", 1},
        {"Spine1_skin", 3}, {"Belly_skin", 3}, {"Spine1_Rear_skin", 3},
        {"LArm_ForeArm3_skin", 2}, {"LArm_ForeArm2_skin", 2}, {"LArm_ForeArm1_skin", 2},
        {"LArm_UpperTwist2_skin", 2}, {"LArm_UpperFat_skin", 2}, {"LArm_UpperTwist1_skin", 2},
        {"LArm_UpperArm_skin", 2}, {"LArm_CollarBone_skin", 2}, {"LArm_ShoulderFat_skin", 2},
        {"RArm_ForeArm3_skin", 2}, {"RArm_ForeArm2_skin", 2}, {"RArm_ForeArm1_skin", 2},
        {"RArm_UpperTwist2_skin", 2}, {"RArm_UpperFat_skin", 2}, {"RArm_UpperTwist1_skin", 2},
        {"RArm_UpperArm_skin", 2}, {"RArm_CollarBone_skin", 2}, {"RArm_ShoulderFat_skin", 2},
        {"LLeg_Calf_skin", 4}, {"LLeg_Calf_Low_skin", 4}, {"LLeg_Thigh_skin", 4},
        {"LLeg_Thigh_Low_skin", 4}, {"LLeg_Thigh_Fat_skin", 4},
        {"RLeg_Calf_skin", 4}, {"RLeg_Calf_Low_skin", 4}, {"RLeg_Thigh_skin", 4},
        {"RLeg_Thigh_Low_skin", 4}, {"RLeg_Thigh_Fat_skin", 4},
        {"Pelvis_skin", 4}, {"Pelvis_Rear_skin", 4}, {"RButtFat_skin", 4}, {"LButtFat_skin", 4}
    }

    ''' <summary>Map a skeleton bone to its MRSV region (0..4) using the EXACT engine table
    ''' (CreationKit.exe fn 0xA95C70, see _mrsvRegionByBone). Direct lookup on the bone's own name;
    ''' falls back to walking the parent chain (for non-vanilla skeletons whose morph bone isn't
    ''' itself a mapped _skin node). Returns -1 if no ancestor is a mapped body-morph bone — the
    ''' engine applies no MRSV scaling to such bones.</summary>
    Private Shared Function ResolveMrsvRegion(bone As HierarchiBone_class) As Integer
        Dim cur = bone
        Dim depth = 0
        While cur IsNot Nothing AndAlso depth < 20
            Dim n = cur.BoneName
            If n IsNot Nothing Then
                Dim region As Integer
                If _mrsvRegionByBone.TryGetValue(n, region) Then Return region
            End If
            cur = cur.Parent
            depth += 1
        End While
        Return -1
    End Function

    ''' <summary>Produce a pose with a single Root.Scale delta carrying the race height factor.
    ''' Empty / identity if raceHeight ≈ 1. The Scale propagates to every descendant bone via
    ''' Transform_Class.ComposeTransforms (NIF convention T·R·S with scale inheritance).</summary>
    Public Shared Function BuildRaceHeightPose(raceHeight As Single) As Poses_class
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

    ''' <summary>Field-level merge of multiple Poses_class into one. For each PoseTransformData field
    ''' (X/Y/Z/Pitch/Roll/Yaw/Scale/ScaleX/ScaleY/ScaleZ), non-identity values from later sources
    ''' overwrite earlier ones. If two sources both have non-identity on the same field → log a
    ''' [POSE-MERGE-OVERLAP] warning and use last-wins. The 3 pose sources (race/BW/FMRS) write to
    ''' disjoint field sets by design, so overlap should never fire — it's a canary for future
    ''' architectural regressions.</summary>
    Public Shared Function MergePoses(ParamArray sources As Poses_class()) As Poses_class
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
    ''' Also applies the CK RUNTIME NNAM neck-fat scale (Layer 2) as a multiplicative Y/Z scale on
    ''' the shared "Neck" bone (neckScaleY/neckScaleZ from ResolveNeckNnamScale) — the engine applies
    ''' this to the live skeleton (head+body) and NEVER bakes it, so it lives here, not in the FMRS
    ''' face pose / bake.
    ''' Requires SkeletonDictionary populated (ResolveMrsvRegion walks bone.Parent chain).</summary>
    Public Shared Function BuildBodyWeightPose(wt As Single, wm As Single, wf As Single,
                                                 genderBlock As RACE_BoneDataGender,
                                                 mrsvValues As List(Of Single),
                                                 neckScaleY As Single, neckScaleZ As Single,
                                                 armaDeltas As Dictionary(Of String, System.Numerics.Vector3),
                                                 skeleton As SkeletonInstance,
                                                 weightLayersEnabled As Boolean) As Poses_class
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
        ' NNAM (Layer 2) targets the literal "Neck" bone, which RACE.BoneData / ARMA may not list.
        ' Ensure it is in the iteration set so a Neck-only-NNAM NPC (no weight/sculpt) still emits it.
        If neckScaleY <> 1.0F OrElse neckScaleZ <> 1.0F Then allBoneNames.Add("Neck")

        ' --- Engine weight-triangle correction factor K (RE de Fallout4.exe, fn 0x6517A0/0x664850) ---
        ' El engine NO interpola linealmente: out_a = thin_a*wt + musc_a*wm + fat_a*wf - (mean_a-1)*K,
        ' con mean_a = (thin_a+musc_a+fat_a)/3 y K = "centralidad" del punto de peso (wt,wm,wf) en el
        ' triángulo equilátero Thin/Musc/Fat. Constantes √3/2, 1/√3, 2/√3 verificadas en el binario.
        ' En (⅓,⅓,⅓) → K=1 ⇒ out=1.0 (identidad). Usa los MISMOS wt/wm/wf que la parte lineal de abajo.
        ' Ver memoria project_morph_clamp_re. weightK se calcula 1× por NPC.
        Dim wTriX As Single = wm * 0.5F + wf - 0.5F
        Dim wTriY As Single = (wt + wf) * 0.866025F - 0.577350F
        Dim weightK As Single = (0.866025F - CSng(Math.Sqrt(wTriX * wTriX + wTriY * wTriY))) * 1.154701F

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
                ' Corrección de centralidad del engine (RE 0x664850): tira hacia identidad por (mean-1)*K.
                sx -= ((bone.ThinX + bone.MuscularX + bone.FatX) / 3.0F - 1.0F) * weightK
                sy -= ((bone.ThinY + bone.MuscularY + bone.FatY) / 3.0F - 1.0F) * weightK
                sz -= ((bone.ThinZ + bone.MuscularZ + bone.FatZ) / 3.0F - 1.0F) * weightK
            End If
            Dim sxR As Single = sx, syR As Single = sy, szR As Single = sz   ' snapshot post-RACE

            ' --- Clamp model (diagnostic): clamp the WEIGHT delta to the Range Modifier [Min,Max]
            ' BEFORE MRSV (Y/Z only — Range has no X). syR keeps the raw value for [BW-CLAMP-DIAG].
            If (clampModel = BodyWeightClampModel.ClampWeightL1 OrElse clampModel = BodyWeightClampModel.ClampBoth) _
               AndAlso bone IsNot Nothing AndAlso bone.HasRangeModifier Then
                sy = Math.Min(Math.Max(sy, 1.0F + bone.MinY), 1.0F + bone.MaxY)
                sz = Math.Min(Math.Max(sz, 1.0F + bone.MinZ), 1.0F + bone.MaxZ)
            End If

            ' --- Layer 2: NNAM neck-fat (CK RUNTIME Neck-bone scale; applied to the live skeleton, NEVER baked) ---
            ' Empirically validated 2026-06-17: CK does NOT bake this into FaceGeom; it is a runtime scale of the
            ' shared "Neck" bone (affects head+body). EXACT bone name "Neck", case-insensitive (Equals,
            ' NOT substring — so Neck1/Neckmuscle stay excluded), matching the OrdinalIgnoreCase bone set
            ' used everywhere else in this loop.
            If String.Equals(boneName, "Neck", StringComparison.OrdinalIgnoreCase) Then
                sy *= neckScaleY
                sz *= neckScaleZ
            End If

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
            ' AFTER MRSV, BEFORE ARMA. ARMA sculpt (Layer 4) then ADDS its delta to the clamped value.
            If (clampModel = BodyWeightClampModel.ClampFinal OrElse clampModel = BodyWeightClampModel.ClampBoth) _
               AndAlso bone IsNot Nothing AndAlso bone.HasRangeModifier Then
                sy = Math.Min(Math.Max(sy, 1.0F + bone.MinY), 1.0F + bone.MaxY)
                sz = Math.Min(Math.Max(sz, 1.0F + bone.MinZ), 1.0F + bone.MaxZ)
            End If
            Dim sxM As Single = sx, syM As Single = sy, szM As Single = sz   ' snapshot post-MRSV (= input a ARMA)

            ' --- Layer 4: ARMA Bone Scale Delta — ADITIVO en los 3 ejes, X incluido (2026-06-19) ---
            ' Fórmula: s = race_s + arma_d (componente a componente; X NO se resetea).
            ' Base RE (Fallout4.exe, build abril-2026, el que usa la app): el builder del
            ' bone-scale-array [BSSkin::Instance+0x50] = FUN_140652230 combina ADITIVAMENTE
            ' (out = weight_base + sculpt_delta por eje). En ESE array out0/X se resetea a 1.0
            ' cuando hay sculpt — PERO el render skin (walker FUN_14040a5d0) NO lee +0x50:
            ' cosecha node + node+0x70 (world transforms), que son X-capaces. El consumidor
            ' real del +0x50 no se localizó estáticamente (probable GPU skin-matrix prep).
            ' Como el render va por matrices de nodo (X-capaz) y la data de Fallout4.esm trae
            ' DeltaX deliberado en antebrazos (BoS underarmor X=+0.20 simétrico; Raider X=-0.19
            ' solo brazo derecho), se aplica DeltaX aditivo en vez de descartarlo.
            ' ⚠ Aditivo-vs-multiplicativo NO byte-probado vs CK ground-truth (consumidor +0x50
            ' GPU/oculto); pendiente test empírico in-game de un outfit con DeltaX (0x134293, 0x0AF0E1).
            Dim armaDX As Single = 0.0F, armaDY As Single = 0.0F, armaDZ As Single = 0.0F
            If armaDeltas IsNot Nothing Then
                Dim d As System.Numerics.Vector3
                If armaDeltas.TryGetValue(boneName, d) Then
                    armaDX = d.X
                    armaDY = d.Y
                    armaDZ = d.Z
                    sx = sxM + armaDX
                    sy = syM + armaDY
                    sz = szM + armaDZ
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

End Class
