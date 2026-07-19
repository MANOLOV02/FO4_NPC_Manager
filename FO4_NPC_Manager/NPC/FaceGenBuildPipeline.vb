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
                                                  chargenTriPath As String,
                                                  Optional raceMorphTriPath As String = Nothing) As WorldVertResult
        If state Is Nothing OrElse facebonesShape Is Nothing OrElse facebonesNif Is Nothing Then Return Nothing

        ' 1) Apply chargen TRI morphs to the facebones shape's vertex array (in place; the
        ' caller passes a freshly-loaded NIF that nobody else reads).
        ApplyChargenMorphsInPlace(facebonesNif, facebonesShape, chargenTriPath, raceMorphTriPath, state)

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
                               Optional srcShape As INiShape = Nothing,
                               Optional raceMorphTriPath As String = Nothing) As Boolean
        If state Is Nothing OrElse destNif Is Nothing OrElse clonedOrigShape Is Nothing Then Return False
        If facebonesNif Is Nothing OrElse facebonesShape Is Nothing Then Return False

        ' 1) v_world via FBNS skin with FMRS pose applied + chargen morphs.
        Dim wr = ComputeWorldVerticesForShape(state, facebonesNif, facebonesShape, chargenTriPath, raceMorphTriPath)
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
        ' Buffer reusable para la normalizacion de pesos del MOTOR (ver EngineSkinWeightNormalization). Lado INVERSO
        ' (mundo → local del mesh destino) = el segundo SkinBlend del CK (invert=1, 142B6F91E), con
        ' la paleta del destino. El drift ε = s_src/s_dst − 1 sólo aparece si AMBOS lados (el forward
        ' de SkinBakeMath y este inverso) corren la misma ley. Gate apagado ⇒ bit-idéntico.
        Dim ckW(EngineSkinWeightNormalization.Slots - 1) As Single

        For i = 0 To vCount - 1
            Dim Mtot As Matrix4d = Matrix4d.Zero
            Dim sumW As Double = 0
            Dim baseSlot = i * wpv
            Dim hasSkinRow = flatIdx IsNot Nothing AndAlso flatWgt IsNot Nothing AndAlso i < skin.VertexCount

            If hasSkinRow AndAlso EngineSkinWeightNormalization.TryComputeWeights(flatWgt, baseSlot, wpv, ckW) Then
                For j = 0 To EngineSkinWeightNormalization.Slots - 1
                    If ckW(j) > 0.0F Then
                        Dim idx = CInt(flatIdx(baseSlot + j))
                        If idx >= 0 AndAlso idx < nBones Then Mtot += precomputedOrig(idx) * CDbl(ckW(j))
                    End If
                Next
            Else
                If hasSkinRow Then
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

    ''' <summary>Apply the chargen (+ SSE race) TRI vertex morphs to <paramref name="shape"/>'s vertex array
    ''' IN PLACE, using the SAME morph plan the live render builds (NpcMorphResolver: FO4 MSDK/MSDV+MPPI, SSE
    ''' NAM9/NAMA over the merged race+chargen TriHead) + the SSE head-weight delta. Friend so the SSE bake
    ''' path (which has no <c>_faceBones</c> rig, so it never enters <see cref="BakeShape"/>) can morph the
    ''' cloned head shape directly — a pure per-vertex morph, no FMRS/skin-rebind. FO4 keeps using it via
    ''' <see cref="ComputeWorldVerticesForShape"/> on the FBNS shape. No-op when the shape's HDPT declares no
    ''' chargen tri or the plan has no matching morph channels.</summary>
    Friend Sub ApplyChargenMorphsInPlace(nif As Nifcontent_Class_Manolo,
                                           shape As INiShape,
                                           chargenTriPath As String,
                                           raceMorphTriPath As String,
                                           state As BakeState,
                                           Optional headMeshTriPath As String = Nothing)
        Dim isSse = state.NpcData IsNot Nothing AndAlso state.NpcData.Game = Config_App.Game_Enum.Skyrim

        ' Build the TriHead the morph plan reads from. The RUNTIME resolver
        ' (NpcMorphResolver.LoadTriForShape) merges the HDPT race-morph tri (NAM0=0, e.g.
        ' FemaleHeadRaces.tri) WITH the chargen tri (NAM0=2) into ONE TriHead. The bake MUST do the
        ' same for SSE or it silently drops the racial base face morph — BuildFaceMorphPlanFromNam9
        ' applies it by RACE EditorID at weight 1, but that morph lives ONLY in the race tri, so a
        ' chargen-only TriHead makes GetMorph(EditorID) return Nothing and the channel no-ops. That
        ' is exactly why the live render (which merges) looked right while the baked NIF did not.
        ' FO4 stays chargen-only (validated byte-exact vs CK; its plan requests MSM/MPPM sculpt names
        ' that all live in the chargen tri, and the render's FO4 merge adds only unused expression
        ' morphs) — merging the race tri for FO4 is intentionally skipped to protect that path.
        ' Shape geometry up front so its vertex count can drive the SSE High Poly Head .tri redirect (below) and be
        ' reused for the morph write. IShapeGeometry.VertexCount is cheap (no vertex copy).
        Dim geom = ShapeGeometryFactory.[For](shape, nif)
        Dim shapeVerts = geom.VertexCount

        Dim triHead As TriHeadFile
        If isSse Then
            ' SSE merges race (NAM0=0) + chargen (NAM0=2) + the head MESH tri (SkinnyMorph weight morph). The
            ' mesh tri is per-part and RACE-AWARE by construction: headMeshTriPath is ChangeExtension of THIS
            ' head-part's own mesh (femalehead / ...argonian / ...khajiit / hairNN — each ships its own SkinnyMorph
            ' at its own vertex count), so no race table or vertex-count gate is needed.
            '
            ' HPH redirect — the SAME resolver the live render calls (NpcMorphResolver.ResolveHphHeadPartTriPath), so
            ' render == bake by construction (regla de oro). Opt-in + SSE-gated; returns each record path unchanged
            ' unless it's missing/wrong-topology/empty for a known HPH part (e.g. brows, whose HDPT ships only NAM0=1).
            Dim vertsOf As Func(Of String, Integer) = Function(p)
                                                          Dim h = LoadHeadTriCached(p, state)
                                                          Return If(h Is Nothing, -1, CInt(h.NumVertices))
                                                      End Function
            Dim rRace = NpcMorphResolver.ResolveHphHeadPartTriPath(raceMorphTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Race, vertsOf)
            Dim rChargen = NpcMorphResolver.ResolveHphHeadPartTriPath(chargenTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Chargen, vertsOf)
            Dim rMesh = NpcMorphResolver.ResolveHphHeadPartTriPath(headMeshTriPath, shapeVerts, NpcMorphResolver.HphTriSlot.Mesh, vertsOf)
            triHead = LoadMergedHeadTri(rRace, rChargen, state, rMesh)
        Else
            triHead = LoadHeadTriCached(chargenTriPath, state)
        End If
        If triHead Is Nothing Then
            Return
        End If

        ' Single per-game morph plan — the SAME builder the live render calls (NpcMorphResolver.
        ' BuildFaceMorphPlan): FO4 = MSDK/MSDV+MPPI via RACE defs; SSE = race base + NAM9 + NAMA + RaceMenu.
        ' One path per game, no divergence between render and bake.
        ' Race base morph is looked up by the MORPH-race EditorID (RACE.NAM8 redirect: e.g. Dremora→DarkElf,
        ' every *Vampire→base race), not the actor's raw race — see RecordParsers.ResolveMorphRaceEditorId.
        Dim plan = NpcMorphResolver.BuildFaceMorphPlan(state.NpcData, triHead,
                                                       RecordParsers.ResolveMorphRaceEditorId(state.Race, state.PluginManager),
                                                       state.RaceMorphValueDefs, state.RaceMorphPresetDefs,
                                                       raceKeywordEditorIds:=RecordParsers.GetRaceKeywordEditorIds(state.Race, state.PluginManager),
                                                       shapeChargenTriPath:=chargenTriPath)
        If plan Is Nothing OrElse Not plan.HasMorphs Then Return

        Dim positionsFloat = geom.GetVertexPositions()
        Dim count = positionsFloat.Count
        If count = 0 Then Return
        Dim positionsDouble(count - 1) As Vector3d
        For i = 0 To count - 1
            positionsDouble(i) = New Vector3d(positionsFloat(i).X, positionsFloat(i).Y, positionsFloat(i).Z)
        Next
        ' The SSE actor-weight head/hair morph is now a normal channel INSIDE the plan (SkinnyMorph, added by
        ' BuildFaceMorphPlanFromNam9 from the merged head MESH tri) — no separate table pass. Render and bake
        ' therefore weight-morph through this single MorphEngine call, per-part and race-aware.
        Dim morphed = MorphEngine.ApplyChannelsToVertexArray(positionsDouble, plan)
        Dim outFloat As New List(Of System.Numerics.Vector3)(count)
        For i = 0 To count - 1
            outFloat.Add(New System.Numerics.Vector3(CSng(morphed(i).X), CSng(morphed(i).Y), CSng(morphed(i).Z)))
        Next
        ' VertexDesc (SSE): OJO al histórico. SetVertexPositions hacía HasVertices=True también en
        ' BSDynamicTriShape, encendiendo el atributo "Vertex" y DUPLICANDO las posiciones al buffer estático
        ' (VertSize 5→9 en el head). Una nota anterior lo dio por "diff cosmético" y era FALSO: con la posición
        ' en el estático el motor deja de leerla del array dinámico, que es donde escribe la animación facial
        ' ⇒ cara correcta pero CONGELADA, sin lip-sync ni expresiones (confirmado in-game 2026-07-11).
        ' Arreglado en la raíz (NiflySharp BSTriShape.SetVertexPositions: guard `is not BSDynamicTriShape`),
        ' así que aquí ya no hace falta nada. El estático SÍ debe seguir llevando el morph: FinalizeData llama
        ' a CalcDynamicData, que regenera el array dinámico A PARTIR de él — por eso escribir dynShape.Vertices
        ' directo no funciona (se machaca).
        geom.SetVertexPositions(outFloat)
    End Sub

    ''' <summary>Load+parse a head TriHead by mesh path, cached per-BakeState (Nothing is cached too so a
    ''' missing/broken path is not re-attempted for every shape). Returns Nothing on miss/parse-fail.</summary>
    Private Function LoadHeadTriCached(triPath As String, state As BakeState) As TriHeadFile
        If String.IsNullOrEmpty(triPath) Then Return Nothing
        Dim key = MeshPathHelpers.NormalizeMeshKey(triPath)
        Dim head As TriHeadFile = Nothing
        If state.TriHeadCache.TryGetValue(key, head) Then Return head
        head = ParseHeadTri(key)
        state.TriHeadCache(key) = head
        Return head
    End Function

    ''' <summary>Parse a TriHead from a normalized FilesDictionary key. Nothing on missing bytes / parse error.</summary>
    Private Function ParseHeadTri(normalizedKey As String) As TriHeadFile
        Dim bytes = FilesDictionary_class.GetBytes(normalizedKey)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return Nothing
        Try
            Dim head = TriHeadParser.ParseTriHeadFromBytes(bytes)
            ' Apply the vanilla mouth fix (22 mouth deltas zeroed) iff the toggle is on and this is the
            ' female chargen tri — so a bake matches the live render WYSIWYG. No-op for every other file.
            ChargenMouthFix.MaybeApplyInPlace(normalizedKey, head)
            Return head
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

    ''' <summary>Load the race-morph tri (HDPT NAM0=0) merged with the chargen tri (NAM0=2) into a single
    ''' TriHead, mirroring NpcMorphResolver.LoadTriForShape: race morphs first, chargen morphs added only
    ''' when the race tri does not already carry that name (race wins name collisions). The merge is cached
    ''' per-BakeState under a composite key so it (and its parses) happen once per race/chargen pair. The
    ''' race side is parsed FRESH (not the shared per-path cache) so mutating it with the chargen morphs can
    ''' never corrupt a TriHead reused elsewhere. Falls back to whichever side is present when one is missing.</summary>
    ''' <remarks>Friend (no Private) para que SseMorphReverseEngineer construya la MISMA base mergeada
    ''' que usa el bake. Duplicar estas 35 líneas allá crearía una segunda fuente de verdad que se
    ''' desincroniza en silencio (precedencia race&gt;chargen&gt;mesh, parses frescos, extended tris de
    ''' RaceMenu, comboKey de caché).</remarks>
    Friend Function LoadMergedHeadTri(raceMorphTriPath As String, chargenTriPath As String, state As BakeState,
                                       Optional headMeshTriPath As String = Nothing) As TriHeadFile
        Dim raceKey = If(String.IsNullOrEmpty(raceMorphTriPath), "", MeshPathHelpers.NormalizeMeshKey(raceMorphTriPath))
        Dim chargenKey = If(String.IsNullOrEmpty(chargenTriPath), "", MeshPathHelpers.NormalizeMeshKey(chargenTriPath))
        Dim meshKey = If(String.IsNullOrEmpty(headMeshTriPath), "", MeshPathHelpers.NormalizeMeshKey(headMeshTriPath))
        Dim comboKey = "merged:" & raceKey & "|" & chargenKey & "|" & meshKey
        Dim merged As TriHeadFile = Nothing
        If state.TriHeadCache.TryGetValue(comboKey, merged) Then Return merged

        ' Parse ALL THREE sides FRESH (owned copies) so the merge base is never a shared per-path cache object —
        ' MergeChargenIntoRaceTriHead mutates its first arg, and folding into a shared TriHead would corrupt it for
        ' other consumers. Precedence race > chargen > mesh (add-if-absent). Merging the head MESH tri LAST is what
        ' brings "SkinnyMorph" (the actor-weight head/hair morph) into the plan's TriHead — the SAME source the live
        ' render picks up in LoadTriForShape. CRUCIAL for HAIR/hairline/beard parts: they have NO race/chargen tri
        ' at all, only their own mesh tri (hairNN.tri = a single SkinnyMorph), so the base ends up BEING that fresh
        ' mesh TriHead and the weight morph still applies. The comboKey includes meshKey so distinct meshes don't alias.
        Dim raceHead As TriHeadFile = If(raceKey = "", Nothing, ParseHeadTri(raceKey))
        Dim chargenHead As TriHeadFile = If(chargenKey = "", Nothing, ParseHeadTri(chargenKey))
        Dim meshHead As TriHeadFile = If(meshKey = "", Nothing, ParseHeadTri(meshKey))
        Dim baseTri As TriHeadFile = raceHead
        NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, chargenHead)
        NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, meshHead)

        ' RaceMenu extended morphs — the SAME merge the live render does (NpcMorphResolver.LoadTriForShape).
        ' A .jslot custom morph names a morph that lives in an extended .tri mapped from the chargen tri by
        ' morphs.ini. Without merging them the bake would silently drop every extended slider while the render
        ' showed it, breaking render == bake.
        Dim catalog = NpcMorphResolver.SliderCatalog
        If catalog IsNot Nothing AndAlso Not String.IsNullOrEmpty(chargenTriPath) Then
            For Each extTriPath In catalog.GetExtendedMorphTris(chargenTriPath)
                Dim extHead = ParseHeadTri(MeshPathHelpers.NormalizeMeshKey(extTriPath))
                If extHead IsNot Nothing Then NpcMorphResolver.MergeChargenIntoRaceTriHead(baseTri, extHead)
            Next
        End If
        merged = baseTri
        state.TriHeadCache(comboKey) = merged
        Return merged
    End Function

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
