Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics

''' <summary>
''' Export the currently-rendered NPC scene (the live <see cref="PreviewModel.RenderableMesh"/> list) to a NIF
''' on disk. This is pure build/bake/serialize domain logic extracted VERBATIM from MainForm's
''' <c>ButtonSaveSceneNif_Click</c> handler — the handler keeps only the SaveFileDialog, the
''' default-name plumbing and the result MessageBoxes; everything that constructs the destination
''' NIF, bakes world-pose vertices via the per-vertex skin matrices, computes the normal/tangent/
''' bitangent transforms, runs the hair-zap vertex compaction (oldToNew remap) and serializes the
''' shapes lives here.
'''
''' Faithful code-motion only: the statements below are the originals, with member access rebound
''' from <c>_previewControl.Model.meshes</c> to the injected <paramref name="meshes"/> parameter.
''' No math, ordering, type or zap/skin/serialize logic was changed.
''' </summary>
Public NotInheritable Class SceneNifExporter

    Private Sub New()
    End Sub

    ''' <summary>Cantidad máxima de vértices que un shape puede tener y seguir siendo indexable por
    ''' los triángulos del NIF. <c>NiflySharp.Structs.Triangle</c> guarda V1/V2/V3 como
    ''' <c>UShort</c> ⇒ índices válidos 0..65535 ⇒ 65536 vértices. Por encima de esto el cast a
    ''' UShort trunca en silencio (wraparound) y la malla sale destrozada; no hay camino de
    ''' índices de 32 bits en este formato, así que el shape se falla explícitamente.</summary>
    Private Const MaxUShortIndexableVerts As Integer = 65536

    ''' <summary>Outcome of an <see cref="Export"/> call. Carries the counts + per-shape failure
    ''' text the UI needs to build its summary MessageBox, plus a distinct save-error message for
    ''' the case where <c>Save_As_Manolo</c> threw (so the UI can show the error-icon box and
    ''' early-return exactly as the original handler did).</summary>
    Public Structure ExportResult
        ''' <summary>Count of shapes successfully written into the destination NIF.</summary>
        Public ShapesWritten As Integer
        ''' <summary>Count of shapes that failed (clone failure, missing data, inconsistent zap
        ''' compaction, or a per-shape exception).</summary>
        Public ShapesFailed As Integer
        ''' <summary>Per-shape failure details, one line per failure (same text the original
        ''' handler accumulated).</summary>
        Public FailureDetails As String
        ''' <summary>Non-Nothing only when the final <c>Save_As_Manolo</c> threw; carries the
        ''' exception message so the UI can show the "Failed to write {path}" error box.</summary>
        Public SaveError As String
    End Structure

    ''' <summary>Build a FO4 NIF from the live rendered meshes and write it to
    ''' <paramref name="outPath"/>. Bakes each visible shape's world-pose vertices/normals/
    ''' tangents/bitangents through its per-vertex skin matrices, applies hair-zap vertex
    ''' compaction, strips skin on the clones, drops orphaned skin blocks and saves.
    ''' Never throws — failures surface via <see cref="ExportResult"/>.</summary>
    Public Shared Function Export(meshes As IEnumerable(Of PreviewModel.RenderableMesh), outPath As String) As ExportResult
        Dim destNif As New Nifcontent_Class_Manolo()
        destNif.Create(NiVersion.GetFO4(), withRootNode:=True)

        Dim shapesWritten As Integer = 0
        Dim shapesFailed As Integer = 0
        Dim failureDetails As New System.Text.StringBuilder()
        Dim destIdx As Integer = 0

        For Each mesh In meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Shape Is Nothing Then Continue For
            Dim srcRenderable = mesh.MeshData.Shape
            If srcRenderable.RenderHide Then Continue For
            Dim srcINiShape = srcRenderable.NifShape
            Dim srcNif = srcRenderable.NifContent
            If srcINiShape Is Nothing OrElse srcNif Is Nothing Then Continue For

            Dim shapeName = If(srcINiShape.Name?.String, $"Shape_{destIdx}")
            Try
                ' Guarantee PerVertexSkinMatrix is current before the bake reads it. After a GPU-mode
                ' animation play, RecomputeGPUBoneMatrices skips pass-2 (updatePerVertexSkin=False) and
                ' leaves PerVertexMatrixValid=False with PerVertexSkinMatrix STALE (SkinningHelper.vb
                ' ~889-908, 1099-1106). This Save dialog runs OFF the render loop with NO GL context, so
                ' nothing has recomputed it. GetWorldVertices is the SAME canonical CPU-only path the
                ' renderer's own world-cache readers use (Render.vb:1124/2562/3246, OcclusionRaytracer):
                ' it calls ComputeWorldSpaceCache → EnsurePerVertexSkinMatrix, which lazily re-blends
                ' PerVertexSkinMatrix from BoneMatsPose (pure Matrix4d math, GL-free) and sets
                ' PerVertexMatrixValid=True. Pass the LIVE field ByRef so the in-place element rewrite +
                ' the validity flag land on the live SkinnedGeometry — idempotent and identical to the
                ' renderer's next reader, so the on-screen model is not corrupted (it only fills the
                ' world cache + flips WorldCacheValid/PerVertexMatrixValid True; no dirty flag the
                ' renderer needs is cleared). When already valid this is a cheap no-op.
                ' Guard on Vertices IsNot Nothing exactly as the renderer's readers do
                ' (OcclusionRaytracer.vb:88) — a degenerate geom falls through to the existing
                ' missing-data check below, which fails the shape gracefully.
                If mesh.MeshData.Meshgeometry.Vertices IsNot Nothing Then
                    SkinningHelper.GetWorldVertices(mesh.MeshData.Meshgeometry)
                End If

                ' Copy AFTER ensuring freshness. SkinnedGeometry is a STRUCT (Render.vb:1656), so this is
                ' a by-value snapshot — but PerVertexSkinMatrix is an array reference shared with the live
                ' field, and EnsurePerVertexSkinMatrix rewrote that array's elements in place above, so the
                ' bake below reads the now-fresh matrices through this copy.
                Dim liveGeom = mesh.MeshData.Meshgeometry
                Dim localVerts = liveGeom.Vertices  ' post-morph, pre-skin (shape-local).
                Dim perVtxMat = liveGeom.PerVertexSkinMatrix
                If localVerts Is Nothing OrElse perVtxMat Is Nothing OrElse localVerts.Length <> perVtxMat.Length Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"{shapeName}: missing skin matrix / vertex data")
                    Continue For
                End If
                Dim n = localVerts.Length

                ' ── Hair zap compaction map ──
                ' If this shape had any hair partition zapped this render (ApplyZaps=True + VertexMask(i)=-1
                ' on the zapped verts — exactly the renderer's zap-skip predicate in Render.vb), DROP those
                ' verts from the export so the saved NIF carries the compacted mesh. AGNOSTIC to which
                ' partition was zapped (Top / Long / Both) — it keys purely on VertexMask(i)=-1, so it works
                ' unchanged for the main (zap Top) and the hairline (zap Long). oldToNew(i) maps a surviving
                ' source vertex to its packed destination index; -1 = removed. When the shape is not zapped,
                ' oldToNew is identity and nSurv == n (no behaviour change for normal shapes).
                Dim vm = liveGeom.VertexMask
                Dim hasZap As Boolean = srcRenderable.ApplyZaps AndAlso vm IsNot Nothing AndAlso vm.Length = n
                Dim oldToNew(n - 1) As Integer
                Dim nSurv As Integer = 0
                For i = 0 To n - 1
                    If hasZap AndAlso vm(i) = -1.0F Then
                        oldToNew(i) = -1
                    Else
                        oldToNew(i) = nSurv
                        nSurv += 1
                    End If
                Next
                Dim zappedCount As Integer = n - nSurv

                ' Compute world-pose attributes per SURVIVING vertex (packed in oldToNew order). Position
                ' via TransformPosition; normals/tangents/bitangents via per-vertex normal matrix
                ' (transpose of inverse of upper-left 3x3 of the skin matrix). Same formula the renderer uses.
                Dim worldPos As New List(Of System.Numerics.Vector3)(nSurv)
                Dim hasN = liveGeom.Normals IsNot Nothing AndAlso liveGeom.Normals.Length = n
                Dim hasT = liveGeom.Tangents IsNot Nothing AndAlso liveGeom.Tangents.Length = n
                Dim hasB = liveGeom.Bitangents IsNot Nothing AndAlso liveGeom.Bitangents.Length = n
                Dim worldN As List(Of System.Numerics.Vector3) = If(hasN, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldT As List(Of System.Numerics.Vector3) = If(hasT, New List(Of System.Numerics.Vector3)(nSurv), Nothing)
                Dim worldB As List(Of System.Numerics.Vector3) = If(hasB, New List(Of System.Numerics.Vector3)(nSurv), Nothing)

                For i = 0 To n - 1
                    If oldToNew(i) < 0 Then Continue For  ' zapped crown vertex — drop from export
                    Dim m4 = perVtxMat(i)
                    Dim wv = Vector3d.TransformPosition(localVerts(i), m4)
                    worldPos.Add(New System.Numerics.Vector3(CSng(wv.X), CSng(wv.Y), CSng(wv.Z)))

                    If hasN OrElse hasT OrElse hasB Then
                        Dim nm As Matrix3d = SkinningHelper.NormalMatrixOrIdentity(m4)
                        Dim nm4 As Matrix4d = Matrix4d.Identity
                        nm4.M11 = nm.M11 : nm4.M12 = nm.M12 : nm4.M13 = nm.M13
                        nm4.M21 = nm.M21 : nm4.M22 = nm.M22 : nm4.M23 = nm.M23
                        nm4.M31 = nm.M31 : nm4.M32 = nm.M32 : nm4.M33 = nm.M33
                        If hasN Then
                            Dim nrm = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Normals(i), nm4))
                            worldN.Add(New System.Numerics.Vector3(CSng(nrm.X), CSng(nrm.Y), CSng(nrm.Z)))
                        End If
                        If hasT Then
                            Dim tan = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Tangents(i), nm4))
                            worldT.Add(New System.Numerics.Vector3(CSng(tan.X), CSng(tan.Y), CSng(tan.Z)))
                        End If
                        If hasB Then
                            Dim bit = Vector3d.Normalize(Vector3d.TransformNormal(liveGeom.Bitangents(i), nm4))
                            worldB.Add(New System.Numerics.Vector3(CSng(bit.X), CSng(bit.Y), CSng(bit.Z)))
                        End If
                    End If
                Next

                Dim clonedINiShape = destNif.CloneShape_Original(srcINiShape, shapeName, srcNif)
                If clonedINiShape Is Nothing Then
                    shapesFailed += 1
                    failureDetails.AppendLine($"Clone failed: {shapeName}")
                    Continue For
                End If

                ' Reset clone's local T/R/S to identity. CloneShape_Original's unskinned branch
                ' (NifContent_Class.vb:407+) bakes srcShape's parent_chain (without root) into
                ' destShape.T/R/S so unskinned clones display at the right NIF-world position.
                ' Our verts are ALREADY in world coords (via PerVertexSkinMatrix, which absorbs
                ' parent_chain for unskinned and bone palette for skinned). Leaving the baked
                ' T/R/S in place would double-transform the verts.
                clonedINiShape.Translation = New System.Numerics.Vector3(0, 0, 0)
                clonedINiShape.Rotation = New NiflySharp.Structs.Matrix33()
                clonedINiShape.Scale = 1.0F

                ' Write world-pose attributes into the clone via its polymorphic adapter.
                Dim cloneRenderable As New NifRenderableShape(destNif, clonedINiShape, destIdx)
                Dim cloneAdapter = cloneRenderable.Geometry

                ' When the crown was zapped, the clone still carries the SOURCE vertex count + triangles.
                ' Resize the per-vertex storage DOWN to the survivor count BEFORE writing the (already
                ' compacted, nSurv-long) attribute arrays, then remap + drop triangles below. Identity
                ' case (no zap): nSurv == n, ResizeVertices is a documented no-op — skip it to keep the
                ' normal-shape path byte-for-byte as before.
                If hasZap AndAlso zappedCount > 0 Then cloneAdapter.ResizeVertices(nSurv)

                cloneAdapter.SetVertexPositions(worldPos)
                If hasN AndAlso cloneAdapter.HasNormals Then cloneAdapter.SetNormals(worldN)
                If hasT AndAlso cloneAdapter.HasTangents Then cloneAdapter.SetTangents(worldT)
                If hasB AndAlso cloneAdapter.HasTangents Then cloneAdapter.SetBitangents(worldB)

                ' Remap triangles after vertex compaction. Drop any triangle that touched a zapped
                ' (crown) vertex; reindex the survivors through oldToNew; track per-new-triangle
                ' provenance (source triangle index) so SetTriangles(provenance) redistributes the
                ' BSSubIndexTriShape Segments/SubSegmentDatas consistently (the same contract WM's
                ' RemoveZaps uses → MorphingHelper.vb:226). liveGeom.Indices is in source-triangle
                ' order (SkinningHelper.vb:412 flattens GetTriangles()), so tr = oldTriIdx.
                Dim triCheckOk As Boolean = True
                If hasZap AndAlso zappedCount > 0 Then
                    Dim idxArr = liveGeom.Indices
                    If idxArr Is Nothing Then
                        triCheckOk = False
                    ElseIf nSurv > MaxUShortIndexableVerts Then
                        ' ⛔ NiflySharp.Structs.Triangle almacena V1/V2/V3 como UShort, así que sólo puede
                        ' direccionar índices 0..65535 (⇒ como mucho 65536 vértices). Con nSurv por encima
                        ' de eso, el CUShort(na/nb/nc) de abajo TRUNCA en silencio (wraparound) y produce
                        ' una malla destrozada. No hay camino de índices de 32 bits en este formato/adapter,
                        ' así que el shape se falla explícitamente en vez de escribir un NIF corrupto.
                        ' Se chequea ANTES del loop: hacerlo después es inútil, porque los valores ya
                        ' truncados vuelven a caer dentro del rango válido (ver nota en el bloque de
                        ' verificación de abajo).
                        triCheckOk = False
                        failureDetails.AppendLine($"{shapeName}: zap export vertex count {nSurv} exceeds the {MaxUShortIndexableVerts}-vertex limit of 16-bit triangle indices — shape skipped (would corrupt the mesh)")
                        ' Copias locales antes del lambda (mismo patrón que el log de abajo): la lambda
                        ' captura por referencia y estas son variables del loop de shapes.
                        Dim ovfShapeName = shapeName
                        Dim ovfNSurv = nSurv
                        Logger.LogLazy(Function() $"[ZAP-EXPORT] '{ovfShapeName}' SKIPPED: nSurv={ovfNSurv} > {MaxUShortIndexableVerts} (16-bit triangle index overflow)")
                    Else
                        Dim newTris As New List(Of NiflySharp.Structs.Triangle)(idxArr.Length \ 3)
                        Dim provenance As New List(Of Integer)(idxArr.Length \ 3)
                        ' Máximo de los índices remapeados PRE-CAST. El chequeo de consistencia de abajo lee
                        ' los triángulos ya escritos (post-CUShort) y por eso no puede ver un overflow; éste sí.
                        Dim maxNewIdxPreCast As Integer = -1
                        For tr = 0 To idxArr.Length - 3 Step 3
                            Dim a = CInt(idxArr(tr)), b = CInt(idxArr(tr + 1)), c = CInt(idxArr(tr + 2))
                            If a < 0 OrElse a >= n OrElse b < 0 OrElse b >= n OrElse c < 0 OrElse c >= n Then Continue For
                            Dim na = oldToNew(a), nb = oldToNew(b), nc = oldToNew(c)
                            If na < 0 OrElse nb < 0 OrElse nc < 0 Then Continue For  ' triangle touched the crown
                            maxNewIdxPreCast = Math.Max(maxNewIdxPreCast, Math.Max(na, Math.Max(nb, nc)))
                            newTris.Add(New NiflySharp.Structs.Triangle(CUShort(na), CUShort(nb), CUShort(nc)))
                            provenance.Add(tr \ 3)
                        Next
                        ' Red de seguridad sobre los valores PRE-CAST (la guarda de nSurv de arriba ya debería
                        ' haber cubierto esto; si dispara, oldToNew produjo un índice fuera del rango de nSurv).
                        If maxNewIdxPreCast >= nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export remapped triangle index out of range before cast (maxIdx {maxNewIdxPreCast} >= {nSurv})")
                        End If
                        cloneAdapter.SetTriangles(newTris, TriangleRemap.SameShape(provenance))

                        ' ── Consistency verification (counts before/after) ──
                        ' Confirm no exported triangle references a dropped vertex and the survivor count
                        ' matches. GetTriangles()/GetVertexPositions() read back what was written.
                        ' ⚠️ OJO: este bloque lee los índices YA casteados a UShort, así que NO puede
                        ' detectar un overflow de 16 bits — un wraparound da un maxIdx chico que pasa el
                        ' chequeo. Eso lo cubren la guarda de nSurv y el chequeo pre-cast de arriba; esto
                        ' valida el round-trip del writer (que lo escrito sea lo que se pidió escribir).
                        Dim writtenTris = cloneAdapter.GetTriangles()
                        Dim writtenVerts = cloneAdapter.GetVertexPositions()
                        Dim maxIdx As Integer = -1
                        For Each t In writtenTris
                            maxIdx = Math.Max(maxIdx, Math.Max(CInt(t.V1), Math.Max(CInt(t.V2), CInt(t.V3))))
                        Next
                        Dim shapeNameLog = shapeName
                        Dim nLog = n, nSurvLog = nSurv, zapLog = zappedCount
                        Dim wvCount = writtenVerts.Count, wtCount = writtenTris.Count, srcTriCount = idxArr.Length \ 3
                        Dim newTriCount = newTris.Count, maxIdxLog = maxIdx
                        Logger.LogLazy(Function() $"[ZAP-EXPORT] '{shapeNameLog}' verts {nLog}→{nSurvLog} (zapped {zapLog}); tris {srcTriCount}→{newTriCount}; readback verts={wvCount} tris={wtCount} maxTriVtxIdx={maxIdxLog}")
                        If wvCount <> nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export vertex count mismatch (expected {nSurv}, got {wvCount})")
                        End If
                        If maxIdx >= nSurv Then
                            triCheckOk = False
                            failureDetails.AppendLine($"{shapeName}: zap export triangle references dropped vertex (maxIdx {maxIdx} >= {nSurv})")
                        End If
                    End If
                End If

                If hasZap AndAlso zappedCount > 0 AndAlso Not triCheckOk Then
                    ' Compaction produced an inconsistent shape — skip it rather than write a corrupt NIF.
                    destNif.RemoveShape_Manolo(clonedINiShape)
                    shapesFailed += 1
                    Continue For
                End If

                ' Strip skin on the clone. For BSTriShape this clears the VertexAttribute.Skinned
                ' flag (FinalizeData → CalcDataSizes excludes the bone weight/index bytes from the
                ' per-vertex stream on save). For NiTriShape the setter is a no-op; the
                ' SkinInstanceRef.Clear() below is what disables skinning in that family.
                clonedINiShape.IsSkinned = False
                clonedINiShape.SkinInstanceRef?.Clear()

                shapesWritten += 1
                destIdx += 1
            Catch ex As Exception
                shapesFailed += 1
                failureDetails.AppendLine($"{shapeName}: {ex.Message}")
            End Try
        Next

        If shapesWritten = 0 Then
            Return New ExportResult With {
                .ShapesWritten = 0,
                .ShapesFailed = shapesFailed,
                .FailureDetails = failureDetails.ToString(),
                .SaveError = Nothing
            }
        End If

        ' Drop unreferenced BSSkin_Instance / BSSkin_BoneData / NiSkinInstance / NiSkinData /
        ' NiSkinPartition blocks orphaned by the cleared SkinInstanceRefs.
        Try
            destNif.RemoveUnreferencedBlocks()
        Catch
        End Try

        Try
            destNif.Save_As_Manolo(outPath, Overwrite:=True)
        Catch ex As Exception
            Return New ExportResult With {
                .ShapesWritten = shapesWritten,
                .ShapesFailed = shapesFailed,
                .FailureDetails = failureDetails.ToString(),
                .SaveError = ex.Message
            }
        End Try

        Return New ExportResult With {
            .ShapesWritten = shapesWritten,
            .ShapesFailed = shapesFailed,
            .FailureDetails = failureDetails.ToString(),
            .SaveError = Nothing
        }
    End Function

End Class
