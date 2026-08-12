Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics

''' <summary>
''' Reusable per-vertex skinning math for the FaceGen bake. Mirrors SkinningHelper's blend
''' (precomputed[k] × weight) but operates on raw NIF data, with no GL involvement.
'''
''' Two entry points:
''' • <see cref="SkinShapeWorldVertices"/>: classic blend with bind-only matsBind (used by the
'''   diagnostic harness DumpIsolatedBakeHarnessCSV — no FMRS applied to bones).
''' • <see cref="SkinShapeWorldVerticesWithPose"/>: blend with bind+pose matsPose where the
'''   pose is FMRS-applied (used by the offline FaceGen bake — produces v_world that matches
'''   the runtime render).
'''
''' App-specific (NPC_Manager). The math itself isn't NPC-specific, but the pipeline that
''' calls it (state → skeletons → FMRS pose → shape) is, so the helper lives here. If a
''' second app ever needs it, promote to FO4_Base_Library.
''' </summary>
Public Module SkinBakeMath

    ''' <summary>Skin a shape with bind-only matrices (no FMRS pose). Each vertex gets
    ''' transformed to world space by the weighted blend of <c>shapeGlobal × bindT[k] × localT[k]</c>
    ''' over its bone influences. Equivalent to "render at canonical bind pose" — the harness
    ''' compares this to the render's v_world to attribute residual to the morph stack.</summary>
    ''' <param name="shape">The shape whose vertex array drives the result (we skin its
    ''' GetVertexPositions). The shape carries its own ShapeBones (NiNode list) and
    ''' ShapeBoneTransforms (per-shape local offset BoneTransform[k]).</param>
    ''' <param name="shapeContainerNif">The NIF that owns <paramref name="shape"/> — used to
    ''' resolve the shape's parent NiNode global transform.</param>
    ''' <param name="boneBindResolver">Callback that, given a bone NiNode and the shape's NIF
    ''' container, returns its global bindT (typically <c>Transform_Class.GetGlobalTransform</c>
    ''' off a fresh skeleton instance walked by name). Pass-through so the caller decides which
    ''' skeleton to consult — face wins, body fallback, etc.</param>
    Public Function SkinShapeWorldVertices(shape As INiShape,
                                            shapeContainerNif As Nifcontent_Class_Manolo,
                                            boneBindResolver As Func(Of NiflySharp.Blocks.NiNode, Transform_Class)) As Vector3d()
        If shape Is Nothing OrElse shapeContainerNif Is Nothing Then Return Nothing
        Dim wrap As New NifRenderableShape(shapeContainerNif, shape, 0)
        Dim shapeBones = wrap.ShapeBones.ToArray()
        Dim shapeLocalTs = wrap.ShapeBoneTransforms.ToArray()
        If shapeBones.Length <> shapeLocalTs.Length Then Return Nothing
        Dim nBones = shapeBones.Length

        ' bindT per bone (caller resolves; usually face skel ∪ body skel ∪ shape-local fallback)
        Dim matsBind(nBones - 1) As Matrix4d
        For k = 0 To nBones - 1
            Dim bindT As Transform_Class = Nothing
            If boneBindResolver IsNot Nothing Then bindT = boneBindResolver(shapeBones(k))
            If bindT Is Nothing Then bindT = Transform_Class.GetGlobalTransform(shapeBones(k), shapeContainerNif)
            If bindT Is Nothing Then bindT = New Transform_Class()
            matsBind(k) = bindT.ComposeTransforms(shapeLocalTs(k)).ToMatrix4d()
        Next

        Return BlendShapeVertices(shape, shapeContainerNif, matsBind, nBones)
    End Function

    ''' <summary>Skin a shape with FMRS-applied pose matrices. For each bone, the per-bone
    ''' matrix is <c>shapeGlobal × poseT[k] × localT[k]</c> where <paramref name="bonePoseResolver"/>
    ''' returns either the bone's pose (bind × FMRS delta) or its bind if no FMRS applies.
    ''' This is what the runtime renderer effectively does — and what the offline FaceGen
    ''' bake needs to produce v_world matching the render.</summary>
    Public Function SkinShapeWorldVerticesWithPose(shape As INiShape,
                                                    shapeContainerNif As Nifcontent_Class_Manolo,
                                                    bonePoseResolver As Func(Of NiflySharp.Blocks.NiNode, Transform_Class)) As Vector3d()
        ' Same structure as the bind-only entry; the only difference is the resolver returns
        ' poseT (bind × FMRS) instead of bind. Code reuse via same path.
        Return SkinShapeWorldVertices(shape, shapeContainerNif, bonePoseResolver)
    End Function

    Private Function BlendShapeVertices(shape As INiShape,
                                         shapeContainerNif As Nifcontent_Class_Manolo,
                                         matsPerBone() As Matrix4d,
                                         nBones As Integer) As Vector3d()
        ' Shape global transform (parent chain). Typically Identity for FaceGen-style NIFs.
        Dim shapeNode = TryCast(shapeContainerNif.GetParentNode(shape), NiflySharp.Blocks.NiNode)
        If shapeNode Is Nothing Then shapeNode = shapeContainerNif.GetRootNode()
        Dim shapeGlobal As Matrix4d = If(shapeNode IsNot Nothing,
                                          Transform_Class.GetGlobalTransform(shapeNode, shapeContainerNif).ToMatrix4d(),
                                          Matrix4d.Identity)

        ' Precompute shapeGlobal × matsPerBone[k] (same formula as SkinningHelper line 267).
        Dim precomputed(nBones - 1) As Matrix4d
        For k = 0 To nBones - 1
            precomputed(k) = shapeGlobal * matsPerBone(k)
        Next

        ' Per-vertex blend: ES SkinningHelper.BlendBoneMatrices, no "la misma semantica".
        ' ⛔ Acá había una copia escrita a mano de esa ley. El comentario decía "same semantics as
        ' SkinningHelper.BlendBoneMatrices" — y ese "same semantics" es justo el estado que la regla
        ' del proyecto prohíbe: dos cuerpos que hay que mantener sincronizados a mano. Peor todavía,
        ' el gate `skin-blend` de FaceGenBuilder afirma que el bake corre esta misma ley, y probaba
        ' una función que el bake NO llamaba.
        ' Al llamarla, el bake además pasa a usar el blend VECTORIAL (FastGeom entró por dentro de
        ' BlendBoneMatrices, que es por lo que el SIMD nunca había tocado el bake).
        Dim geom = ShapeGeometryFactory.[For](shape, shapeContainerNif)
        Dim skin = geom.GetSkinning()
        Dim wpv = If(skin.WeightsPerVertex > 0, skin.WeightsPerVertex, 4)
        Dim flatIdx = skin.BoneIndices
        Dim flatWgt = skin.BoneWeights
        Dim verts = geom.GetVertexPositions()
        Dim vCount = verts.Count
        Dim vWorld(vCount - 1) As Vector3d

        ' Paleta plana para el blend vectorial: UNA vez por shape (20-60 matrices), no por vértice.
        Dim flatPal = FastGeom.BuildFlatPaletteS(precomputed)
        ' El guard por FILA (`i < skin.VertexCount`) es de acá: BlendBoneMatrices no conoce el índice
        ' de vértice. Sin fila de skin se le pasa Nothing, que es su camino de "sin skin" y devuelve
        ' precomputed(0) — el mismo resultado que daba el fallback de Σw=0 de la copia que había acá.
        Dim tieneFilas = flatIdx IsNot Nothing AndAlso flatWgt IsNot Nothing

        For i = 0 To vCount - 1
            Dim Mtot As Matrix4d
            If tieneFilas AndAlso i < skin.VertexCount Then
                Mtot = SkinningHelper.BlendBoneMatrices(flatWgt, flatIdx, i * wpv, wpv, precomputed, flatPal)
            Else
                Mtot = SkinningHelper.BlendBoneMatrices(Nothing, Nothing, 0, wpv, precomputed, flatPal)
            End If

            Dim vLocal As New Vector3d(verts(i).X, verts(i).Y, verts(i).Z)
            vWorld(i) = Vector3d.TransformPosition(vLocal, Mtot)
        Next

        Return vWorld
    End Function

End Module
