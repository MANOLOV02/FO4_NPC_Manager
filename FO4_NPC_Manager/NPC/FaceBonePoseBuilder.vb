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
    ''' the result of <see cref="MainForm.GetFacialBoneRegionsForRace"/>. Required.</param>
    Public Function BuildFaceBoneTransforms(npcData As NPC_Data,
                                             regionsFile As FacialBoneRegionsFile) As Poses_class
        If npcData Is Nothing OrElse regionsFile Is Nothing Then Return Nothing
        If npcData.FaceMorphs Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        Dim result As New Dictionary(Of String, Transform_Class)(StringComparer.OrdinalIgnoreCase)
        Dim boneScales As New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
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
                Dim deltaPos As New System.Numerics.Vector3(
                    LerpFmrs(px, region.DefaultPosition.X, boneEntry.MinimaPosition.X, boneEntry.MaximaPosition.X) * fmin,
                    LerpFmrs(py, region.DefaultPosition.Y, boneEntry.MinimaPosition.Y, boneEntry.MaximaPosition.Y) * fmin,
                    LerpFmrs(pz, region.DefaultPosition.Z, boneEntry.MinimaPosition.Z, boneEntry.MaximaPosition.Z) * fmin)

                Dim deltaRot As New System.Numerics.Vector3(
                    LerpFmrs(rx, region.DefaultRotation.X, boneEntry.MinimaRotation.X, boneEntry.MaximaRotation.X) * fmin,
                    LerpFmrs(ry, region.DefaultRotation.Y, boneEntry.MinimaRotation.Y, boneEntry.MaximaRotation.Y) * fmin,
                    LerpFmrs(rz, region.DefaultRotation.Z, boneEntry.MinimaRotation.Z, boneEntry.MaximaRotation.Z) * fmin)

                Dim deltaScale As New System.Numerics.Vector3(
                    LerpFmrs(sc, region.DefaultScale.X, boneEntry.MinimaScale.X, boneEntry.MaximaScale.X) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Y, boneEntry.MinimaScale.Y, boneEntry.MaximaScale.Y) * fmin,
                    LerpFmrs(sc, region.DefaultScale.Z, boneEntry.MinimaScale.Z, boneEntry.MaximaScale.Z) * fmin)

                ' Negate all three rotation angles to undo EulerXYZToMatrix33's J·R·J permutation
                ' (FMRS JSON uses standard convention; the function expects pre-inverted angles).
                ' Confirmado 2026-04-18 matemática + empírica en X/Y/Z.
                Dim rotation = Transform_Class.EulerXYZToMatrix33(-deltaRot.X, -deltaRot.Y, -deltaRot.Z)
                Dim xform As New Transform_Class With {
                    .Rotation = rotation,
                    .Translation = deltaPos,
                    .Scale = 1.0F
                }
                Dim boneScaleVec = New System.Numerics.Vector3(
                    1.0F + deltaScale.X, 1.0F + deltaScale.Y, 1.0F + deltaScale.Z)

                ' Accumulate non-uniform scale per bone (multiply across regions)
                Dim existingScale As System.Numerics.Vector3
                If boneScales.TryGetValue(targetBoneName, existingScale) Then
                    boneScales(targetBoneName) = existingScale * boneScaleVec
                Else
                    boneScales(targetBoneName) = boneScaleVec
                End If

                ' Compose rotation+translation across regions
                Dim existing As Transform_Class = Nothing
                If result.TryGetValue(targetBoneName, existing) AndAlso existing IsNot Nothing Then
                    result(targetBoneName) = existing.ComposeTransforms(xform)
                Else
                    result(targetBoneName) = xform
                End If

                Dim isAnyNonZero As Boolean = (Math.Abs(deltaPos.X) > 0.0001F OrElse Math.Abs(deltaPos.Y) > 0.0001F OrElse Math.Abs(deltaPos.Z) > 0.0001F _
                                            OrElse Math.Abs(deltaRot.X) > 0.0001F OrElse Math.Abs(deltaRot.Y) > 0.0001F OrElse Math.Abs(deltaRot.Z) > 0.0001F _
                                            OrElse Math.Abs(deltaScale.X) > 0.0001F OrElse Math.Abs(deltaScale.Y) > 0.0001F OrElse Math.Abs(deltaScale.Z) > 0.0001F)
            Next
        Next

        If result.Count = 0 Then Return Nothing

        ' Convert the Transform_Class deltas into a Poses_class with PoseTransformData entries.
        Dim pose As New Poses_class With {
            .Name = "FMRS Face Morph",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each kv In result
            Dim xform = kv.Value
            Dim rotVec = Transform_Class.Matrix33ToBSRotation(xform.Rotation)
            Dim sc As System.Numerics.Vector3
            If Not boneScales.TryGetValue(kv.Key, sc) Then
                sc = New System.Numerics.Vector3(1.0F, 1.0F, 1.0F)
            End If
            pose.Transforms(kv.Key) = New PoseTransformData With {
                .X = xform.Translation.X,
                .Y = xform.Translation.Y,
                .Z = xform.Translation.Z,
                .Yaw = rotVec.X,
                .Pitch = rotVec.Y,
                .Roll = rotVec.Z,
                .Scale = 1.0F,
                .ScaleX = sc.X,
                .ScaleY = sc.Y,
                .ScaleZ = sc.Z
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

        Dim neckRegion = regionsFile.Regions.Values.FirstOrDefault(Function(r) r.IsNeckRegion)
        If neckRegion Is Nothing Then Return (1.0F, 1.0F)

        Dim block2 As Single = 0.0F
        For Each fm In npcData.FaceMorphs
            If fm.Index = neckRegion.ID Then
                block2 = fm.PositionZ
                Exit For
            End If
        Next
        If block2 <= 0.0F Then Return (1.0F, 1.0F)

        Dim fmin = If(npcData.FacialMorphIntensity <= 0.0F, 1.0F, npcData.FacialMorphIntensity)
        Return (1.0F + neckNnamX * fmin * block2, 1.0F + neckNnamY * fmin * block2)
    End Function

    ''' <summary>Per-axis DELTA from a FMRS-driven slider.
    ''' fmrsVal is the NPC's slider value for this axis (clamped to [-1,+1] by the engine).
    '''   fmrsVal = 0  → 0      (no morph applied)
    '''   fmrsVal = +1 → maxVal - defaultVal
    '''   fmrsVal = -1 → minVal - defaultVal
    ''' Negative values map toward minima, positive toward maxima. Returns the DELTA from the
    ''' rest pose (default), not the lerped absolute value.</summary>
    Public Function LerpFmrs(fmrsVal As Single, defaultVal As Single, minVal As Single, maxVal As Single) As Single
        ' Engine-faithful (Fallout4.exe FUN_1403fd920): raw = s*max (s>=0) or |s|*min (s<0).
        ' The engine does NOT subtract the region Default here — Min/Max are already
        ' additive offsets (0 = no change). defaultVal kept for signature parity but unused.
        Dim s = Math.Max(-1.0F, Math.Min(1.0F, fmrsVal))
        If s >= 0 Then
            Return s * maxVal
        Else
            Return (-s) * minVal
        End If
    End Function

End Module
