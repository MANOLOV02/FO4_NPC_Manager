Imports FO4_Base_Library

''' <summary>
''' Reusable FMRS → face-bone pose builder. Builds a <see cref="Poses_class"/> of per-bone
''' deltas from the NPC's FMRI/FMRS subrecords, the race's FacialBoneRegions JSON, and FMIN.
'''
''' App-specific (NPC_Manager). Both the render path (MainForm.BuildFaceBoneTransforms) and
''' the offline bake (FaceGenBuilder) consume this so the FMRS math lives in one place.
''' </summary>
Public Module FaceBonePoseBuilder

    ''' <summary>Build a pose of face bone deltas from the parsed NPC + race + face regions
    ''' file. Inputs are primitive so any caller can construct them however they want (overlay
    ''' applied vs raw, etc.). Returns Nothing when no FMRS values contribute.</summary>
    ''' <param name="npcData">Parsed NPC_Data with FaceMorphs (FMRI/FMRS) and FacialMorphIntensity.
    ''' Should already have any LooksMenu overlay applied if the caller wants overlay-effective
    ''' values (FaceGenBuilder does, render path does too via ApplyPresetOverlayToNpcData).</param>
    ''' <param name="regionsFile">Parsed FacialBoneRegions JSON for the race+gender pair, e.g.
    ''' the result of <see cref="NpcMorphPoseResolver.GetFacialBoneRegionsForRace"/>. Required.</param>
    Public Function BuildFaceBoneTransforms(npcData As NPC_Data,
                                             regionsFile As FacialBoneRegionsFile) As Poses_class
        If npcData Is Nothing OrElse regionsFile Is Nothing Then Return Nothing
        If npcData.FaceMorphs Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        ' CK accumulates the raw 9-float FMRS deltas ADDITIVELY across regions (decompiled
        ' FUN_140419a30 = pure dst[i]+=src[i], per region in FUN_140a8f530), then builds ONE
        ' transform at the end (FUN_140a96f20: Euler→matrix from the SUMMED rotation, summed
        ' translation, summed scale). Do NOT compose rotation matrices or multiply scale per
        ' region — that diverges (non-commutative / product vs sum) for multi-region bones.
        Dim accPos As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
        Dim accRot As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
        Dim accScale As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)

        ' Log RACE region count vs NPC FaceMorphs count, and which indices the NPC references
        ' vs which ones the JSON declares. Helps spot missing regions (CK shows all RACE regions
        ' as sliders; NPC only stores the ones with non-default values).
        Dim raceRegionIndices = regionsFile.Regions.Keys.OrderBy(Function(i) i).ToList()
        Dim npcIndices = npcData.FaceMorphs.Select(Function(f) f.Index).OrderBy(Function(i) i).ToList()
        Dim missingInNpc = raceRegionIndices.Except(npcIndices).ToList()
        Dim extraInNpc = npcIndices.Except(raceRegionIndices).ToList()
        If missingInNpc.Count > 0 Then
            Dim missingDetail = String.Join(", ", missingInNpc.Take(10).Select(Function(i)
                                                                                   Dim r As FacialBoneRegion = Nothing
                                                                                   regionsFile.Regions.TryGetValue(i, r)
                                                                                   Return $"{i}('{If(r IsNot Nothing, r.Name, "?")}')"
                                                                               End Function))
        End If

        For Each fm In npcData.FaceMorphs
            Dim region As FacialBoneRegion = Nothing
            If Not regionsFile.Regions.TryGetValue(fm.Index, region) Then
                Continue For
            End If

            Dim px = fm.PositionX
            Dim py = fm.PositionY
            Dim pz = fm.PositionZ
            Dim rx = fm.RotationX
            Dim ry = fm.RotationY
            Dim rz = fm.RotationZ
            Dim sc = fm.Scale

            Dim isZero As Boolean = (Math.Abs(px) < 0.0001F AndAlso Math.Abs(py) < 0.0001F AndAlso Math.Abs(pz) < 0.0001F AndAlso
                                     Math.Abs(rx) < 0.0001F AndAlso Math.Abs(ry) < 0.0001F AndAlso Math.Abs(rz) < 0.0001F AndAlso
                                     Math.Abs(sc) < 0.0001F)
            Dim nonZeroMark As String = If(isZero, " (all-zero, will skip)", "")

            ' Skip regions with all-zero FMRS (no deformation at all)
            If Math.Abs(px) < 0.0001F AndAlso Math.Abs(py) < 0.0001F AndAlso Math.Abs(pz) < 0.0001F AndAlso
               Math.Abs(rx) < 0.0001F AndAlso Math.Abs(ry) < 0.0001F AndAlso Math.Abs(rz) < 0.0001F AndAlso
               Math.Abs(sc) < 0.0001F Then Continue For

            For Each boneEntry In region.Bones
                Dim targetBoneName = "skin_" & boneEntry.Bone

                ' FMIN as linear multiplier AFTER LerpFmrs, symmetric across pos/rot/scale.
                ' Empirical validation 2026-04-19 (Cient FMIN=2 / Preston FMIN=4). See
                ' MainForm 2026-04-19 commit notes for the full rationale; the math is
                ' centralized here so render and bake never drift.
                ' region.Default{Position,Rotation,Scale} are deliberately NOT passed: the engine's
                ' lerp never sees them (see LerpFmrs — the bone struct it receives is [Minima|Maxima]
                ' only). Keeping them out of the call is what stops the "− default" convention from
                ' creeping back in.
                Dim deltaPos As New System.Numerics.Vector3(
                    LerpFmrs(px, boneEntry.MinimaPosition.X, boneEntry.MaximaPosition.X) * fmin,
                    LerpFmrs(py, boneEntry.MinimaPosition.Y, boneEntry.MaximaPosition.Y) * fmin,
                    LerpFmrs(pz, boneEntry.MinimaPosition.Z, boneEntry.MaximaPosition.Z) * fmin)

                Dim deltaRot As New System.Numerics.Vector3(
                    LerpFmrs(rx, boneEntry.MinimaRotation.X, boneEntry.MaximaRotation.X) * fmin,
                    LerpFmrs(ry, boneEntry.MinimaRotation.Y, boneEntry.MaximaRotation.Y) * fmin,
                    LerpFmrs(rz, boneEntry.MinimaRotation.Z, boneEntry.MaximaRotation.Z) * fmin)

                Dim deltaScale As New System.Numerics.Vector3(
                    LerpFmrs(sc, boneEntry.MinimaScale.X, boneEntry.MaximaScale.X) * fmin,
                    LerpFmrs(sc, boneEntry.MinimaScale.Y, boneEntry.MaximaScale.Y) * fmin,
                    LerpFmrs(sc, boneEntry.MinimaScale.Z, boneEntry.MaximaScale.Z) * fmin)

                ' Accumulate the raw 9-float deltas ADDITIVELY across regions (CK FUN_140419a30:
                ' dst[i] += src[i]). No matrix build, no ComposeTransforms, no scale product here —
                ' the single transform is built once after the loop from the summed deltas.
                Dim existingPos As System.Numerics.Vector3
                accPos(targetBoneName) = If(accPos.TryGetValue(targetBoneName, existingPos), existingPos, System.Numerics.Vector3.Zero) + deltaPos

                Dim existingRot As System.Numerics.Vector3
                accRot(targetBoneName) = If(accRot.TryGetValue(targetBoneName, existingRot), existingRot, System.Numerics.Vector3.Zero) + deltaRot

                Dim existingScale As System.Numerics.Vector3
                accScale(targetBoneName) = If(accScale.TryGetValue(targetBoneName, existingScale), existingScale, System.Numerics.Vector3.Zero) + deltaScale
            Next
        Next

        If accPos.Count = 0 Then Return Nothing

        ' Convert the Transform_Class deltas into a Poses_class with PoseTransformData entries.
        Dim pose As New Poses_class With {
            .Name = "FMRS Face Morph",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each kv In accPos
            Dim sumPos = kv.Value
            Dim sumRot = accRot(kv.Key)
            Dim sumScale = accScale(kv.Key)

            ' Build ONE transform from the SUMMED deltas (CK FUN_140a96f20). Negate all three
            ' rotation angles to undo EulerXYZToMatrix33's J·R·J permutation (FMRS JSON uses
            ' standard convention; the function expects pre-inverted angles). Same negation as
            ' before — only the accumulation moved from compose/product to sum.
            Dim rotation = Transform_Class.EulerXYZToMatrix33(-sumRot.X, -sumRot.Y, -sumRot.Z)
            Dim rotVec = Transform_Class.Matrix33ToBSRotation(rotation)
            pose.Transforms(kv.Key) = New PoseTransformData With {
                .X = sumPos.X,
                .Y = sumPos.Y,
                .Z = sumPos.Z,
                .Yaw = rotVec.X,
                .Pitch = rotVec.Y,
                .Roll = rotVec.Z,
                .Scale = 1.0F,
                .ScaleX = 1.0F + sumScale.X,
                .ScaleY = 1.0F + sumScale.Y,
                .ScaleZ = 1.0F + sumScale.Z
            }
        Next

        Return pose
    End Function

    ''' <summary>Compute the CK RUNTIME NNAM ("Neck Fat Adjustments Scale") neck-bone scale.
    ''' This is a runtime scale of the shared "Neck" bone (affects head + body together) that the
    ''' engine applies to the live skeleton — it is NEVER baked into the FaceGeom head .nif
    ''' (empirically validated 2026-06-17: baked .nif vs CreationKit vanilla). The body-weight pose
    ''' builder consumes this and multiplies it onto the live "Neck" bone scale.
    '''
    ''' Driven by the chargen neck slider (the FaceMorph PositionZ of the region the JSON flags
    ''' IsNeckRegion), NOT by body weight. Strictly data-driven off the IsNeckRegion flag — no
    ''' hardcoded region ID. race.NeckNNAMX → bone Y scale, race.NeckNNAMY → bone Z scale.</summary>
    ''' <param name="npcData">Parsed NPC_Data (overlay-applied) with FaceMorphs + FacialMorphIntensity.</param>
    ''' <param name="regionsFile">Parsed FacialBoneRegions JSON for the race+gender pair.</param>
    ''' <param name="neckNnamX">RACE.NNAM X for the NPC's gender. Maps to the "Neck" bone's Y scale.</param>
    ''' <param name="neckNnamY">RACE.NNAM Y for the NPC's gender. Maps to the "Neck" bone's Z scale.</param>
    ''' <returns>(ScaleY, ScaleZ) for the "Neck" bone; (1,1) when NNAM does not contribute.</returns>
    Public Function ComputeNeckNnamScale(npcData As NPC_Data,
                                          regionsFile As FacialBoneRegionsFile,
                                          neckNnamX As Single,
                                          neckNnamY As Single) As (ScaleY As Single, ScaleZ As Single)
        If npcData Is Nothing OrElse regionsFile Is Nothing Then Return (1.0F, 1.0F)
        If npcData.FaceMorphs Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return (1.0F, 1.0F)

        ' Pick the neck region THROUGH THE NPC'S OWN FMRI, not by scanning the table for the first
        ' IsNeckRegion. regionsFile is now the MERGED both-gender table (see
        ' NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution: the two per-gender JSONs use
        ' disjoint ID namespaces, and 10 vanilla NPCs carry opposite-gender FMRI), so it contains
        ' TWO IsNeckRegion entries — one per gender — and a FirstOrDefault would depend on dictionary
        ' order and could return the gender the NPC does not use, silently zeroing NNAM.
        ' Driving off fm.Index is order-independent AND automatically gender-correct: the NPC's FMRI
        ' identifies the table it came from, which is exactly the rule the merge is built on.
        Dim block2 As Single = 0.0F
        For Each fm In npcData.FaceMorphs
            Dim r As FacialBoneRegion = Nothing
            If regionsFile.Regions.TryGetValue(fm.Index, r) AndAlso r IsNot Nothing AndAlso r.IsNeckRegion Then
                block2 = fm.PositionZ
                Exit For
            End If
        Next
        If block2 <= 0.0F Then Return (1.0F, 1.0F)

        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)
        Return (1.0F + neckNnamX * fmin * block2, 1.0F + neckNnamY * fmin * block2)
    End Function

    ''' <summary>⭐ THE FMRS INTERPOLATION LAW — single source of truth for render AND bake.
    '''
    ''' Per-axis DELTA from a FMRS-driven slider:
    '''   fmrsVal = 0  → 0        (no morph applied)
    '''   fmrsVal = +1 → maxVal
    '''   fmrsVal = -1 → minVal
    ''' Two independent slopes, one per side; minVal is NOT assumed to be −maxVal.
    '''
    ''' ⛔ THE REGION'S "Defaults" FIELD DOES NOT PARTICIPATE. Do not re-introduce a
    ''' `− default` term, and do not compare Min/Max against Default anywhere (see
    ''' <see cref="IsFmrsAxisLive"/>).
    '''
    ''' FUENTE — RE of both binaries, disassembled and byte-verified 2026-07-19:
    '''   Fallout4.exe  (render) FUN_1403fd920  @ RVA 0x3FD920
    '''   CreationKit.exe (bake) FUN_140419CD0  @ RVA 0x419CD0
    ''' Both are structurally identical and contain ZERO subtract instructions
    ''' (no subss/subps anywhere in either function body). Per axis they emit exactly:
    '''     comiss s, 0 ; jbe .min
    '''     .max:  s * [rcx+0x24+4i]        ' Maxima[i]
    '''     .min:  (s * [rcx+0x00+4i]) XOR 0x80000000   ' = |s| * Minima[i]
    '''     out[i] = result * xmm3          ' xmm3 = FMIN
    ''' The bone struct passed in rcx is exactly 0x48 bytes = [Minima(9 floats) @0x00..0x20 |
    ''' Maxima(9 floats) @0x24..0x44]. There is NO Defaults slot in it: the engine could not
    ''' subtract a Default here even in principle. "Defaults" is parsed by the CK JSON loader
    ''' @0xAF8817 into a DIFFERENT object — the region struct, at [region+0x00..0x20] — and is
    ''' never routed into this computation.
    ''' Rotation's deg→rad (×0.0174533) is applied by the engine only to indices 3-5, which is
    ''' why callers pass rotation in JSON degrees and the caller-side Euler build expects them.
    '''
    ''' NOTE on the s = 0 boundary: the engine's `jbe` sends s = 0 down the MIN branch, yielding
    ''' −(0 × min) = −0.0, where this function yields +0.0. Both are zero and are subsequently
    ''' only multiplied and summed, so the distinction is not observable.</summary>
    Public Function LerpFmrs(fmrsVal As Single, minVal As Single, maxVal As Single) As Single
        Dim s = Math.Max(-1.0F, Math.Min(1.0F, fmrsVal))
        If s >= 0 Then
            Return s * maxVal
        Else
            Return (-s) * minVal
        End If
    End Function

    ''' <summary>Can this axis of a bone entry ever produce a non-zero delta?
    '''
    ''' Corollary of <see cref="LerpFmrs"/> and derived from it so the two can never drift: the
    ''' only outputs reachable are s·max and |s|·min, so the axis is dead iff BOTH endpoints are
    ''' zero — regardless of the region's Defaults.
    '''
    ''' ⛔ Do NOT compare against the region Default. That was the old editor rule
    ''' (EditFace_Form.RegionLiveComponents) and it contradicts the engine: with a non-zero
    ''' Default it would both hide live axes (min=max=0 but Default≠0 → reported live... and
    ''' worse, min≠0 with min=Default → reported dead while the engine still moves the bone).
    ''' Inert in vanilla only because all 32 regions across the 6 shipped JSONs have
    ''' Defaults = 0 on all 9 components; modded races can break that.</summary>
    Public Function IsFmrsAxisLive(minVal As Single, maxVal As Single) As Boolean
        Return minVal <> 0.0F OrElse maxVal <> 0.0F
    End Function

End Module
