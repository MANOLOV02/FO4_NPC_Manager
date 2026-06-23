Imports FO4_Base_Library
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>
''' Orchestrator for the FaceGen offline bake. Composes the same building blocks the runtime
''' renderer uses (NpcRecordOverlay, FaceSkeletonResolver, FaceBonePoseBuilder, MorphEngine,
''' SkinBakeMath, NpcMorphPoseResolver.GetFacialBoneRegionsForRace) without ever needing a GL context or
''' an NpcRenderHost. Outputs per-shape "world vertices" (v_world) — the positions a vertex
''' would occupy after the runtime render produced it from the _facebones source mesh.
'''
''' Bake math (per shape):
'''   1) Load the _facebones mesh source (vertices in shape-local, skin partition referencing
'''      face bones + body bones).
'''   2) Apply the chargen TRI vertex morphs in NIF-local space (MorphEngine).
'''   3) Load fresh body skel + face skel; build FMRS pose from NPC FaceMorphs + race regions
'''      JSON; apply that pose to the face skel via SkeletonInstance.ApplyPose.
'''   4) Skin every vertex with the pose-applied bones — this gives v_world equivalent to the
'''      runtime render output.
'''
''' Once v_world is in hand, the caller (FaceGenBuilder) walks the face-bone hierarchy to
''' redistribute weights to ORIG palette ancestors and computes v_baked = inv(Mtot_orig) ×
''' v_world to write back into the .nif2 with body-only skin partition.
'''
''' App-specific orchestrator. The individual helpers it composes can be reused by other apps
''' (none currently); this orchestrator is NPC_Manager-specific by definition.
''' </summary>
Public Module FaceGenBuildPipeline

    ''' <summary>Per-shape result of <see cref="ComputeWorldVerticesForShape"/>.</summary>
    Public Class WorldVertResult
        ''' <summary>v_world per vertex in render-pipeline coords (post-FMRS, post-chargen-morph).</summary>
        Public Property WorldVertices As Vector3d()
        ''' <summary>Skin instance with FMRS pose applied — the same one used to compute v_world.
        ''' Caller uses this for parent-walk + ancestor lookup when redistributing weights to ORIG palette.</summary>
        Public Property FaceSkel As SkeletonInstance
        ''' <summary>Body skel (no pose) — fallback for bones not in face skel (HEAD, Neck_skin, ...).</summary>
        Public Property BodySkel As SkeletonInstance
    End Class

    ''' <summary>Compute v_world for a shape from its `_facebones` source NIF, with the
    ''' overlay-applied NPC's FMRS pose + chargen morphs applied. Mutates
    ''' <paramref name="facebonesShape"/>'s vertex array in place when chargen morphs apply
    ''' (this is fine because the shape is a freshly-loaded clone from disk, owned by this
    ''' pipeline run). Returns Nothing if any required input is missing.</summary>
    Public Function ComputeWorldVerticesForShape(state As BakeState,
                                                  facebonesNif As Nifcontent_Class_Manolo,
                                                  facebonesShape As INiShape,
                                                  chargenTriPath As String) As WorldVertResult
        If state Is Nothing OrElse facebonesShape Is Nothing OrElse facebonesNif Is Nothing Then Return Nothing

        ' 1) Apply chargen TRI morphs to the facebones shape's vertex array (in place; the
        ' caller passes a freshly-loaded NIF that nobody else reads).
        ApplyChargenMorphsInPlace(facebonesNif, facebonesShape, chargenTriPath, state)

        ' 2) Build face-skel SkeletonInstance with FMRS bone-morph applied. Va a la capa
        ' MorphDeltaTransform (igual que el render en vivo); el bake lee GetGlobalTransform, que
        ' compone todas las capas, así que el resultado es idéntico y la semántica queda uniforme.
        Dim faceSkel = LoadFaceSkeleton(state)
        If state.FmrsPose IsNot Nothing AndAlso faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
            faceSkel.ApplyBoneMorphPose(state.FmrsPose)
        End If

        ' 3) Build body-skel SkeletonInstance. Body bones are at canonical bind in the bake (no
        ' MWGT/MRSV/raceHeight per the bake-without-bodyweight rule) WITH ONE EXCEPTION: the CK
        ' NNAM neck-fat scale targets the literal body bone "Neck", which is NOT a face bone. The
        ' FmrsPose's "Neck" entry is dropped by the faceSkel ApplyBoneMorphPose ContainsKey guard
        ' when the _faceBones.nif does not declare "Neck" (it is a face-bone-only rig), so the
        ' pose-resolver below would fall back to bodySkel's *bind* "Neck" and lose the scale. Apply
        ' the FmrsPose to bodySkel too: ApplyBoneMorphPose only writes entries whose key exists in
        ' the target dictionary, so on bodySkel this sets exactly the "Neck" morph (the "skin_*"
        ' face entries no-op) — matching how the render applies the same merged pose to the full
        ' skeleton that contains "Neck". If the faceBones rig DOES declare "Neck", faceSkel (also
        ' pose-applied, resolver-first) carries the identical scale, so the result is the same.
        Dim bodySkel = LoadBodySkeleton(state)
        If state.FmrsPose IsNot Nothing AndAlso bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
            bodySkel.ApplyBoneMorphPose(state.FmrsPose)
        End If

        ' 4) Skin shape with poseT resolved from faceSkel ∪ bodySkel ∪ shape-internal fallback.
        Dim resolver = BuildPoseResolver(faceSkel, bodySkel, facebonesNif)
        Dim vWorld = SkinBakeMath.SkinShapeWorldVerticesWithPose(facebonesShape, facebonesNif, resolver)

        Return New WorldVertResult With {
            .WorldVertices = vWorld,
            .FaceSkel = faceSkel,
            .BodySkel = bodySkel
        }
    End Function

    ''' <summary>Per-NPC bake context. Built once at the start of a BuildCharGen run and
    ''' reused for every shape in the HDPT chain.</summary>
    Public Class BakeState
        Public Property NpcFormID As UInteger
        Public Property RaceFormID As UInteger
        Public Property IsFemale As Boolean
        ''' <summary>NPC_Data with overlay applied (LooksMenu preset folded in).</summary>
        Public Property NpcData As NPC_Data
        Public Property Race As RACE_Data
        Public Property RaceMorphValueDefs As List(Of RACE_MorphValueDef)
        Public Property RaceMorphPresetDefs As List(Of RACE_MorphPresetDef)
        Public Property FmrsPose As Poses_class
        Public Property PluginManager As PluginManager
        ''' <summary>Cached chargen TRI parses, keyed by normalized mesh path. The same .tri
        ''' is referenced by multiple HDPTs in some cases (rare for face).</summary>
        Public Property TriHeadCache As New Dictionary(Of String, TriHeadFile)(StringComparer.OrdinalIgnoreCase)
    End Class

    ''' <summary>Build a BakeState for one NPC. Loads NPC, applies LooksMenu overlay, parses
    ''' RACE, resolves face regions JSON, builds FMRS pose. Returns Nothing if NPC or RACE
    ''' resolution fails.</summary>
    Public Function BuildBakeState(npcFormID As UInteger,
                                    pluginManager As PluginManager,
                                    appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                    facialBoneRegions As FacialBoneRegionsFile) As BakeState
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(npcFormID, pluginManager, appliedPresets)
        If npcData Is Nothing Then Return Nothing

        Dim raceRec = pluginManager.GetRecord(npcData.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)

        Dim fmrsPose As Poses_class = Nothing
        If facialBoneRegions IsNot Nothing Then
            ' NNAM ("Neck Fat Adjustments Scale") is deliberately NOT included in the bake. It is a
            ' CK RUNTIME scale of the shared "Neck" bone (head+body) applied to the live skeleton —
            ' empirically validated 2026-06-17 that CK does NOT bake it into FaceGeom. The render
            ' path applies it via BuildBodyWeightPose (Layer 2); the bake must stay NNAM-free.
            fmrsPose = FaceBonePoseBuilder.BuildFaceBoneTransforms(npcData, facialBoneRegions)
        End If

        Return New BakeState With {
            .NpcFormID = npcFormID,
            .RaceFormID = npcData.RaceFormID,
            .IsFemale = npcData.IsFemale,
            .NpcData = npcData,
            .Race = race,
            .RaceMorphValueDefs = race.MorphValues,
            .RaceMorphPresetDefs = If(npcData.IsFemale, race.FemaleMorphPresets, race.MaleMorphPresets),
            .FmrsPose = fmrsPose,
            .PluginManager = pluginManager
        }
    End Function

    ''' <summary>Build a bind-only resolver (NO FMRS) over body skel ∪ face skel ∪ shape NIF
    ''' fallback. This is what CK / the runtime uses when it skins a baked face NIF: every bone
    ''' resolves to its canonical bind transform. Reused by <see cref="BakeShape"/> for the
    ''' inverse step and by the post-write render-vs-baked comparison harness.</summary>
    Public Function BuildBindResolver(faceSkel As SkeletonInstance,
                                       bodySkel As SkeletonInstance,
                                       shapeNif As Nifcontent_Class_Manolo) As Func(Of NiNode, Transform_Class)
        Return Function(boneNode As NiNode) As Transform_Class
                   If boneNode Is Nothing Then Return Nothing
                   Dim bn = If(boneNode.Name?.String, "")
                   If bn = "" Then Return Nothing
                   If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If bodySkel.SkeletonDictionary.TryGetValue(bn, hb) AndAlso hb IsNot Nothing Then
                           Return hb.OriginalGetGlobalTransform
                       End If
                   End If
                   If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If faceSkel.SkeletonDictionary.TryGetValue(bn, hb) AndAlso hb IsNot Nothing Then
                           Return hb.OriginalGetGlobalTransform
                       End If
                   End If
                   Return Transform_Class.GetGlobalTransform(boneNode, shapeNif)
               End Function
    End Function

    ''' <summary>Full per-shape bake: compute v_world from the FBNS source (FMRS-applied) and
    ''' write v_baked = inv(Mtot_orig) × v_world into the cloned ORIG shape so that, when later
    ''' skinned by the runtime/CK with body-only bones, each vertex lands at the SAME world
    ''' position the renderer's FBNS skin path would produce. This is the core of iter-3.
    '''
    ''' Preconditions: the cloned ORIG shape lives in <paramref name="destNif"/>; the FBNS NIF
    ''' is loaded fresh (caller-owned) and its shape has the same VertexCount and ordering as
    ''' the cloned ORIG (verified empirically by the THREEWAY harness for face HDPTs).
    '''
    Public Function BakeShape(state As BakeState,
                               destNif As Nifcontent_Class_Manolo,
                               clonedOrigShape As INiShape,
                               facebonesNif As Nifcontent_Class_Manolo,
                               facebonesShape As INiShape,
                               chargenTriPath As String,
                               Optional srcNif As Nifcontent_Class_Manolo = Nothing,
                               Optional srcShape As INiShape = Nothing) As Boolean
        If state Is Nothing OrElse destNif Is Nothing OrElse clonedOrigShape Is Nothing Then Return False
        If facebonesNif Is Nothing OrElse facebonesShape Is Nothing Then Return False

        ' 1) v_world via FBNS skin with FMRS pose applied + chargen morphs.
        Dim wr = ComputeWorldVerticesForShape(state, facebonesNif, facebonesShape, chargenTriPath)
        If wr Is Nothing OrElse wr.WorldVertices Is Nothing Then
            Return False
        End If
        Dim vWorld = wr.WorldVertices

        ' 2a) Inject cloth-physics bones from the source NIF's BSClothExtraData into bodySkel.
        ' Hair shapes (Hair28.nif et al.) carry an HKX skeleton inside BSClothExtraData with the
        ' bind reference pose for cloth bones (Hair_C_Cloth00..02 etc.). The render injects these
        ' at PrepareForShapes time so the live skin uses the HKX bind reference. Without this,
        ' the bake's bind resolver falls back to Transform_Class.GetGlobalTransform(boneNode,
        ' destNif) which reads whatever the cloned NIF carries for that bone — leading to
        ' mismatched Mtot_orig vs CK and ~2 unit vertex RMS on hair shapes.
        If srcNif IsNot Nothing AndAlso srcShape IsNot Nothing AndAlso wr.BodySkel IsNot Nothing AndAlso wr.BodySkel.HasSkeleton Then
            Try
                Dim clothSkel = SkeletonClothOverlayHelper_Class.ParseClothSkeleton(srcNif)
                If clothSkel IsNot Nothing Then
                    Dim clothWrap As New NifRenderableShape(srcNif, srcShape, 0)
                    SkeletonClothOverlayHelper_Class.InjectMissingBonesIntoLiveSkeleton(clothWrap, wr.BodySkel, clothSkel)
                End If
            Catch ex As Exception
            End Try
        End If

        ' 2) Per-vertex Mtot_orig from ORIG bones at canonical bind (body skel ∪ face skel ∪
        ' shape-internal fallback). NO FMRS applied to ORIG bones — the ORIG palette is body
        ' bones (HEAD, Neck_skin, ...) + a few face hooks; CK at bake time keeps them at bind.
        ' With cloth-bone injection above, the resolver also resolves Hair_C_Cloth* etc. via the
        ' HKX reference pose instead of falling through to the NIF-crude transform.
        Dim origResolver = BuildBindResolver(wr.FaceSkel, wr.BodySkel, destNif)

        ' Walk the cloned ORIG to compute its per-vertex Mtot at bind.
        Dim wrap As New NifRenderableShape(destNif, clonedOrigShape, 0)
        Dim shapeBones = wrap.ShapeBones.ToArray()
        Dim shapeLocalTs = wrap.ShapeBoneTransforms.ToArray()
        If shapeBones.Length <> shapeLocalTs.Length OrElse shapeBones.Length = 0 Then
            Return False
        End If
        Dim nBones = shapeBones.Length
        Dim shapeNode = TryCast(destNif.GetParentNode(clonedOrigShape), NiflySharp.Blocks.NiNode)
        If shapeNode Is Nothing Then shapeNode = destNif.GetRootNode()
        Dim shapeGlobal As Matrix4d = If(shapeNode IsNot Nothing,
                                          Transform_Class.GetGlobalTransform(shapeNode, destNif).ToMatrix4d(),
                                          Matrix4d.Identity)
        Dim precomputedOrig(nBones - 1) As Matrix4d
        For k = 0 To nBones - 1
            Dim bindT As Transform_Class = Nothing
            If origResolver IsNot Nothing Then bindT = origResolver(shapeBones(k))
            If bindT Is Nothing Then bindT = Transform_Class.GetGlobalTransform(shapeBones(k), destNif)
            If bindT Is Nothing Then bindT = New Transform_Class()
            precomputedOrig(k) = shapeGlobal * bindT.ComposeTransforms(shapeLocalTs(k)).ToMatrix4d()
        Next

        Dim geom = ShapeGeometryFactory.[For](clonedOrigShape, destNif)
        Dim skin = geom.GetSkinning()
        Dim wpv = If(skin.WeightsPerVertex > 0, skin.WeightsPerVertex, 4)
        Dim flatIdx = skin.BoneIndices
        Dim flatWgt = skin.BoneWeights
        Dim positions = geom.GetVertexPositions()
        Dim vCount = positions.Count

        If vCount <> vWorld.Length Then
            Return False
        End If

        Dim baked As New List(Of System.Numerics.Vector3)(vCount)
        Dim singularCount As Integer = 0
        For i = 0 To vCount - 1
            Dim Mtot As Matrix4d = Matrix4d.Zero
            Dim sumW As Double = 0
            Dim baseSlot = i * wpv
            If flatIdx IsNot Nothing AndAlso flatWgt IsNot Nothing AndAlso i < skin.VertexCount Then
                For j = 0 To wpv - 1
                    Dim w = CDbl(CSng(flatWgt(baseSlot + j)))
                    sumW += w
                    Dim idx = CInt(flatIdx(baseSlot + j))
                    If idx >= 0 AndAlso idx < nBones Then Mtot += precomputedOrig(idx) * w
                Next
            End If
            If sumW = 0 Then
                If nBones > 0 Then
                    Dim idx0 = If(flatIdx IsNot Nothing AndAlso flatIdx.Length > 0 AndAlso i < skin.VertexCount,
                                  CInt(flatIdx(baseSlot)), 0)
                    Mtot = precomputedOrig(Math.Max(0, Math.Min(idx0, nBones - 1)))
                End If
            Else
                Mtot = Mtot * (1.0 / sumW)
            End If

            Dim vBaked As Vector3d
            Try
                Dim invMtot = Matrix4d.Invert(Mtot)
                vBaked = Vector3d.TransformPosition(vWorld(i), invMtot)
            Catch
                ' Singular Mtot — keep ORIG vertex as fallback. Should be extremely rare.
                singularCount += 1
                vBaked = New Vector3d(positions(i).X, positions(i).Y, positions(i).Z)
            End Try
            baked.Add(New System.Numerics.Vector3(CSng(vBaked.X), CSng(vBaked.Y), CSng(vBaked.Z)))
        Next

        geom.SetVertexPositions(baked)
        Try : geom.UpdateBounds() : Catch : End Try

        ' In-memory round-trip self-check: re-skin the just-written shape against the same
        ' bind resolver and measure RMS vs vWorld. If this is ≈0 the math is bit-exact and any
        ' residual seen by the post-Save harness is from disk write/read or shape-partition
        ' rewrites. If this is non-zero, the residual is float-precision in the writeback.
        Try
            Dim vCheck = SkinBakeMath.SkinShapeWorldVertices(clonedOrigShape, destNif, origResolver)
            If vCheck IsNot Nothing AndAlso vCheck.Length = vCount Then
                Dim ssq As Double = 0
                Dim mx As Double = 0
                For i = 0 To vCount - 1
                    Dim dx = vWorld(i).X - vCheck(i).X
                    Dim dy = vWorld(i).Y - vCheck(i).Y
                    Dim dz = vWorld(i).Z - vCheck(i).Z
                    Dim m = dx * dx + dy * dy + dz * dz
                    ssq += m
                    Dim mag = Math.Sqrt(m)
                    If mag > mx Then mx = mag
                Next
                Dim rms = Math.Sqrt(ssq / vCount)
            End If
        Catch ex As Exception
        End Try

        Return True
    End Function

    ''' <summary>Walk the face skeleton hierarchy from <paramref name="boneName"/> upward
    ''' until we hit a bone whose name is in <paramref name="palette"/>. Returns the matching
    ''' bone name, or empty string if the walk reaches the root without finding one.</summary>
    Public Function WalkParentToPaletteAncestor(boneName As String,
                                                 faceSkel As SkeletonInstance,
                                                 palette As HashSet(Of String)) As String
        If faceSkel Is Nothing OrElse Not faceSkel.HasSkeleton Then Return ""
        If palette Is Nothing OrElse palette.Count = 0 Then Return ""
        If String.IsNullOrEmpty(boneName) Then Return ""
        ' Direct hit: the bone itself is already in palette (typical for body bones in the FBNS shape).
        If palette.Contains(boneName) Then Return boneName

        Dim current = boneName
        Dim guard = 0
        While guard < 32  ' Sanity bound — vanilla face skeleton has depth <10.
            guard += 1
            Dim parent = faceSkel.GetParentNodeNameSkeleton(current)
            If String.IsNullOrEmpty(parent) Then Return ""
            If palette.Contains(parent) Then Return parent
            current = parent
        End While
        Return ""
    End Function

    ' --- private helpers ---

    Private Sub ApplyChargenMorphsInPlace(nif As Nifcontent_Class_Manolo,
                                           shape As INiShape,
                                           chargenTriPath As String,
                                           state As BakeState)
        If String.IsNullOrEmpty(chargenTriPath) Then Return
        Dim triKey = MeshPathHelpers.NormalizeMeshKey(chargenTriPath)
        Dim triHead As TriHeadFile = Nothing
        If Not state.TriHeadCache.TryGetValue(triKey, triHead) Then
            Dim triBytes = FilesDictionary_class.GetBytes(triKey)
            If triBytes Is Nothing OrElse triBytes.Length = 0 Then
                Return
            End If
            Try
                triHead = TriHeadParser.ParseTriHeadFromBytes(triBytes)
            Catch ex As Exception
                Return
            End Try
            state.TriHeadCache(triKey) = triHead
        End If
        If triHead Is Nothing Then
            Return
        End If

        Dim plan = NpcMorphResolver.BuildFaceMorphPlanFromTriHead(state.NpcData, state.RaceMorphValueDefs, state.RaceMorphPresetDefs, triHead, logShapeName:=If(shape.Name?.String, ""))
        If plan Is Nothing OrElse Not plan.HasMorphs Then Return

        Dim geom = ShapeGeometryFactory.[For](shape, nif)
        Dim positionsFloat = geom.GetVertexPositions()
        Dim count = positionsFloat.Count
        If count = 0 Then Return
        Dim positionsDouble(count - 1) As Vector3d
        For i = 0 To count - 1
            positionsDouble(i) = New Vector3d(positionsFloat(i).X, positionsFloat(i).Y, positionsFloat(i).Z)
        Next
        Dim morphed = MorphEngine.ApplyChannelsToVertexArray(positionsDouble, plan)
        Dim outFloat As New List(Of System.Numerics.Vector3)(count)
        For i = 0 To count - 1
            outFloat.Add(New System.Numerics.Vector3(CSng(morphed(i).X), CSng(morphed(i).Y), CSng(morphed(i).Z)))
        Next
        geom.SetVertexPositions(outFloat)
    End Sub

    ''' <summary>Returns the union of bone names from the actor's face + body skeletons.
    ''' Used by the bake to drop source shapes that skin to bones outside this set
    ''' (e.g. MaleEyesGhoul.nif's GhoulTearDuct sub-shape, which skins to a custom
    ''' 'GhoulTearDuct' bone that no actor skeleton exposes — CK drops it for that reason).
    ''' Returns an empty (case-insensitive) HashSet if either skeleton load fails.</summary>
    Public Function GetActorBoneNames(state As BakeState) As HashSet(Of String)
        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If state Is Nothing Then Return names
        Try
            Dim faceSkel = LoadFaceSkeleton(state)
            If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                For Each k In faceSkel.SkeletonDictionary.Keys
                    names.Add(k)
                Next
            End If
        Catch ex As Exception
        End Try
        Try
            Dim bodySkel = LoadBodySkeleton(state)
            If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                For Each k In bodySkel.SkeletonDictionary.Keys
                    names.Add(k)
                Next
            End If
        Catch ex As Exception
        End Try
        Return names
    End Function

    Private Function LoadFaceSkeleton(state As BakeState) As SkeletonInstance
        Dim bytes = FaceSkeletonResolver.TryLoadFaceSkeletonBytes(state.RaceFormID, state.IsFemale, state.PluginManager)
        If bytes Is Nothing Then Return Nothing
        Dim skel As New SkeletonInstance()
        If Not skel.LoadFromBytes(bytes) Then Return Nothing
        Return skel
    End Function

    Private Function LoadBodySkeleton(state As BakeState) As SkeletonInstance
        ' Body skel path comes from RACE.ANAM (FemaleSkeletonPath / MaleSkeletonPath).
        Dim path = If(state.IsFemale, state.Race.FemaleSkeletonPath, state.Race.MaleSkeletonPath)
        If String.IsNullOrEmpty(path) Then path = If(state.IsFemale, state.Race.MaleSkeletonPath, state.Race.FemaleSkeletonPath)
        If String.IsNullOrEmpty(path) Then Return Nothing
        Dim key = MeshPathHelpers.NormalizeMeshKey(path)
        Dim skel As New SkeletonInstance()
        If Not skel.LoadFromKey(key) Then Return Nothing
        Return skel
    End Function

    Private Function BuildPoseResolver(faceSkel As SkeletonInstance,
                                        bodySkel As SkeletonInstance,
                                        shapeNif As Nifcontent_Class_Manolo) As Func(Of NiNode, Transform_Class)
        Return Function(boneNode As NiNode) As Transform_Class
                   If boneNode Is Nothing Then Return Nothing
                   Dim boneName = If(boneNode.Name?.String, "")
                   If boneName = "" Then Return Nothing

                   ' 1) Face skel wins (FMRS bone-morph folded in via ApplyBoneMorphPose →
                   ' GetGlobalTransform includes the MorphDeltaTransform layer).
                   If faceSkel IsNot Nothing AndAlso faceSkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If faceSkel.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                           Return hb.GetGlobalTransform
                       End If
                   End If

                   ' 2) Body skel fallback. GetGlobalTransform (NOT OriginalGetGlobalTransform) so the
                   ' MorphDeltaTransform layer is included: the only morph applied to bodySkel here is
                   ' the CK NNAM neck-fat scale on the literal "Neck" bone (see ComputeWorldVerticesForShape).
                   ' Every other body bone has MorphDeltaTransform=Nothing, so GetGlobalTransform == bind
                   ' for them — identical to the previous OriginalGetGlobalTransform for all non-Neck bones.
                   If bodySkel IsNot Nothing AndAlso bodySkel.HasSkeleton Then
                       Dim hb As HierarchiBone_class = Nothing
                       If bodySkel.SkeletonDictionary.TryGetValue(boneName, hb) AndAlso hb IsNot Nothing Then
                           Return hb.GetGlobalTransform
                       End If
                   End If

                   ' 3) Last fallback: walk the bone's parent chain in the shape's own NIF.
                   Return Transform_Class.GetGlobalTransform(boneNode, shapeNif)
               End Function
    End Function

End Module
