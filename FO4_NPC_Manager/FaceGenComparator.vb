Imports System.IO
Imports System.Text
Imports FO4_Base_Library
Imports NiflySharp

''' <summary>
''' Compare a generated FaceGen .nif2 against the original FaceGen .nif from Bethesda's BA2.
''' Drives the iterative bake: every iteration of FaceGenBuilder replaces a "copy" step with a
''' "construct from records" step, and this comparator measures how far we are from CK's
''' bake. Goal: zero structural diff and RMS &lt;= round-trip noise across all 11 shapes.
'''
''' Both inputs are NIF files in NIF-local space (no skinning resolved). All comparisons happen
''' on the raw NIF blocks: shape names, vertex counts, triangle counts, BGSM paths, bone lists
''' with their per-shape bind transforms, and per-vertex position deltas.
''' </summary>
Public Module FaceGenComparator

    Public Class ShapeReport
        Public Property Name As String = ""
        Public Property PresentInGenerated As Boolean
        Public Property PresentInBaked As Boolean
        Public Property GeneratedVertexCount As Integer = -1
        Public Property BakedVertexCount As Integer = -1
        Public Property GeneratedTriangleCount As Integer = -1
        Public Property BakedTriangleCount As Integer = -1
        ''' <summary>Material path (BGSM external file) when the shader points to one — empty
        ''' string when the shader has its data embedded (FaceGen baked NIF case). Even when
        ''' the path is empty, the material data lives inside the shader and is captured by
        ''' GeneratedMaterial / BakedMaterial below.</summary>
        Public Property GeneratedMaterialPath As String = ""
        Public Property BakedMaterialPath As String = ""
        ''' <summary>Unified material (FO4UnifiedMaterial_Class) populated from each side's
        ''' shader — works whether the shader carries an external BGSM path or has the data
        ''' embedded. Comparing these two with AreEqualTo / AreEqualWithTrace is what tells
        ''' us if textures, color values, alpha mode, and shader flags match.</summary>
        Public Property GeneratedMaterial As FO4UnifiedMaterial_Class
        Public Property BakedMaterial As FO4UnifiedMaterial_Class
        Public Property MaterialMatch As Boolean
        Public Property GeneratedBones As List(Of String) = New List(Of String)()
        Public Property BakedBones As List(Of String) = New List(Of String)()
        ''' <summary>Bones present in baked but not in generated. Each iteration of the builder
        ''' should be making this set smaller (we add the missing bones one source at a time).</summary>
        Public Property BonesMissingFromGenerated As List(Of String) = New List(Of String)()
        ''' <summary>Bones present in generated but not in baked. Should normally be empty (means
        ''' we put a bone CK didn't put there) — flag for investigation.</summary>
        Public Property BonesExtraInGenerated As List(Of String) = New List(Of String)()
        ''' <summary>Bones present in both — where we measure transform diff.</summary>
        Public Property BonesShared As List(Of String) = New List(Of String)()
        ''' <summary>Per-vertex RMS of position diff. Only computed when VC matches.</summary>
        Public Property VertexRms As Double = -1
        Public Property VertexMaxDiff As Double = -1
        ''' <summary>Per-shared-bone bind transform RMS (Translation only, scaled). Only when both have the bone.</summary>
        Public Property BoneTranslationRms As Double = -1
        ''' <summary>Per-shared-bone bind transform RMS — rotation off-diagonal magnitude (proxy
        ''' for orientation diff) and scale diff. Only when both have the bone.</summary>
        Public Property BoneRotationRms As Double = -1
        Public Property BoneScaleRms As Double = -1
        ''' <summary>Triangles diff. TriangleMismatch counts triangles that index different
        ''' vertices (same triangle index slot) — both as ordered triples and after sorting.
        ''' Sorted-mismatch tells us if windings are different but topology equal.</summary>
        Public Property TriangleMismatchOrdered As Integer = -1
        Public Property TriangleMismatchSorted As Integer = -1
        ''' <summary>Per-vertex normal/tangent/UV/vertex-color diffs. RMS of magnitude diff. -1
        ''' when VC mismatches or the field is absent on one side; -2 when the field is absent
        ''' on BOTH sides (both null → no diff).</summary>
        Public Property NormalRms As Double = -1
        Public Property TangentRms As Double = -1
        Public Property UvRms As Double = -1
        Public Property VertexColorRms As Double = -1
    End Class

    Public Class CompareReport
        Public Property GeneratedPath As String = ""
        Public Property BakedPath As String = ""
        Public Property Shapes As List(Of ShapeReport) = New List(Of ShapeReport)()
        Public Property Summary As String = ""
    End Class

    ''' <summary>Run the diff. Loads both NIFs, walks shapes by name, fills a report. Logs to
    ''' npc_preview.log under the [BUILDCHARGEN-DIFF] tag. Returns the report for the caller
    ''' to surface in a MessageBox.</summary>
    Public Function Compare(generatedPath As String, bakedFromBa2Bytes As Byte()) As CompareReport
        Dim report As New CompareReport With {.GeneratedPath = generatedPath}

        If Not File.Exists(generatedPath) Then
            report.Summary = $"Generated .nif2 not found: {generatedPath}"
            Logger.Log($"[BUILDCHARGEN-DIFF] {report.Summary}")
            Return report
        End If
        If bakedFromBa2Bytes Is Nothing OrElse bakedFromBa2Bytes.Length = 0 Then
            report.Summary = "Baked NIF bytes not provided."
            Logger.Log($"[BUILDCHARGEN-DIFF] {report.Summary}")
            Return report
        End If

        Dim genNif As New Nifcontent_Class_Manolo()
        Dim bakeNif As New Nifcontent_Class_Manolo()
        Try
            genNif.Load_Manolo(generatedPath)
        Catch ex As Exception
            report.Summary = $"Failed to load generated .nif2: {ex.GetType().Name}: {ex.Message}"
            Logger.Log($"[BUILDCHARGEN-DIFF] {report.Summary}")
            Return report
        End Try
        Try
            bakeNif.Load_Manolo(bakedFromBa2Bytes)
        Catch ex As Exception
            report.Summary = $"Failed to load baked NIF: {ex.GetType().Name}: {ex.Message}"
            Logger.Log($"[BUILDCHARGEN-DIFF] {report.Summary}")
            Return report
        End Try

        Dim sb As New StringBuilder()
        sb.AppendLine($"[BUILDCHARGEN-DIFF] === comparing generated vs baked ===")
        sb.AppendLine($"[BUILDCHARGEN-DIFF] generated: {generatedPath}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF] baked: (BA2 bytes, {bakedFromBa2Bytes.Length} B)")

        ' Index shapes by name on both sides.
        Dim genShapes = IndexShapesByName(genNif)
        Dim bakeShapes = IndexShapesByName(bakeNif)
        Dim allNames As New SortedSet(Of String)(StringComparer.OrdinalIgnoreCase)
        For Each k In genShapes.Keys : allNames.Add(k) : Next
        For Each k In bakeShapes.Keys : allNames.Add(k) : Next

        sb.AppendLine($"[BUILDCHARGEN-DIFF] shape count: generated={genShapes.Count} baked={bakeShapes.Count} union={allNames.Count}")

        ' NIF-level header diff: root node identity + global transform. The shape-level checks
        ' below assume both NIFs share the same coordinate frame; if root scale/translation
        ' differ here, every per-vertex/per-bone comparison downstream inherits that bias.
        Try
            Dim genRoot = genNif.GetRootNode()
            Dim bakeRoot = bakeNif.GetRootNode()
            Dim genRootName = If(genRoot?.Name?.String, "<null>")
            Dim bakeRootName = If(bakeRoot?.Name?.String, "<null>")
            Dim rootMatch = String.Equals(genRootName, bakeRootName, StringComparison.OrdinalIgnoreCase)
            sb.AppendLine($"[BUILDCHARGEN-DIFF] root node: gen='{genRootName}' bake='{bakeRootName}' match={rootMatch}")
            If genRoot IsNot Nothing AndAlso bakeRoot IsNot Nothing Then
                Dim gt = Transform_Class.GetGlobalTransform(genRoot, genNif)
                Dim bt = Transform_Class.GetGlobalTransform(bakeRoot, bakeNif)
                Dim dt = Math.Sqrt((gt.Translation.X - bt.Translation.X) ^ 2 +
                                   (gt.Translation.Y - bt.Translation.Y) ^ 2 +
                                   (gt.Translation.Z - bt.Translation.Z) ^ 2)
                Dim ds = Math.Abs(gt.Scale - bt.Scale)
                Dim r1 = gt.Rotation : Dim r2 = bt.Rotation
                Dim rotF = Math.Sqrt(
                    (r1.M11 - r2.M11) ^ 2 + (r1.M12 - r2.M12) ^ 2 + (r1.M13 - r2.M13) ^ 2 +
                    (r1.M21 - r2.M21) ^ 2 + (r1.M22 - r2.M22) ^ 2 + (r1.M23 - r2.M23) ^ 2 +
                    (r1.M31 - r2.M31) ^ 2 + (r1.M32 - r2.M32) ^ 2 + (r1.M33 - r2.M33) ^ 2)
                sb.AppendLine($"[BUILDCHARGEN-DIFF] root global transform diff: |dT|={dt:F6} |dRot|_F={rotF:F6} |dScale|={ds:F6}")
            End If
        Catch ex As Exception
            sb.AppendLine($"[BUILDCHARGEN-DIFF] root node diff failed: {ex.GetType().Name}: {ex.Message}")
        End Try

        For Each name In allNames
            Dim shapeReport As New ShapeReport With {.Name = name}
            Dim genShape As INiShape = Nothing
            Dim bakeShape As INiShape = Nothing
            shapeReport.PresentInGenerated = genShapes.TryGetValue(name, genShape)
            shapeReport.PresentInBaked = bakeShapes.TryGetValue(name, bakeShape)

            If Not shapeReport.PresentInGenerated AndAlso shapeReport.PresentInBaked Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF] shape '{name}': MISSING_FROM_GENERATED (only in baked)")
                report.Shapes.Add(shapeReport)
                Continue For
            End If
            If shapeReport.PresentInGenerated AndAlso Not shapeReport.PresentInBaked Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF] shape '{name}': EXTRA_IN_GENERATED (not in baked)")
                report.Shapes.Add(shapeReport)
                Continue For
            End If

            ' Both present — fill structural metadata + bone diff + vertex RMS.
            Dim genWrap As New NifRenderableShape(genNif, genShape, 0)
            Dim bakeWrap As New NifRenderableShape(bakeNif, bakeShape, 0)

            shapeReport.GeneratedVertexCount = CInt(genShape.VertexCount)
            shapeReport.BakedVertexCount = CInt(bakeShape.VertexCount)
            shapeReport.GeneratedTriangleCount = genShape.TriangleCount
            shapeReport.BakedTriangleCount = bakeShape.TriangleCount

            ' Unified material capture from each side. NifRenderableShape.ShapeMaterial wraps
            ' Nifcontent_Class_Manolo.GetRelatedMaterial(shape), which itself dispatches on
            ' shader type (BSLightingShaderProperty / BSEffectShaderProperty) and routes
            ' through FO4UnifiedMaterial_Class.Create_From_Shader — populating textures and
            ' flags from EITHER an external BGSM path OR embedded shader data. So just
            ' reading .material here covers both the source NIFs (external path) and the
            ' baked CK NIF (embedded — its shader carries no path, only inline values).
            Try
                shapeReport.GeneratedMaterialPath = If(genWrap.ShapeMaterial?.path, "")
                shapeReport.GeneratedMaterial = genWrap.ShapeMaterial?.material
            Catch
            End Try
            Try
                shapeReport.BakedMaterialPath = If(bakeWrap.ShapeMaterial?.path, "")
                shapeReport.BakedMaterial = bakeWrap.ShapeMaterial?.material
            Catch
            End Try
            Try
                If shapeReport.GeneratedMaterial IsNot Nothing AndAlso shapeReport.BakedMaterial IsNot Nothing Then
                    ' MaterialMatch ignores cosmetic-only path-style diffs (textures\ prefix,
                    ' \ vs /, casing) — same filter DumpMaterialDiff uses. A shape that ONLY
                    ' differs in path style is considered material-match for the summary count
                    ' (still listed in the per-property dump if any non-cosmetic field differs).
                    shapeReport.MaterialMatch = HasOnlyCosmeticDiffs(shapeReport.GeneratedMaterial, shapeReport.BakedMaterial)
                End If
            Catch
            End Try

            ' Bone diff. Names sourced from ShapeBones (already resolved by NifRenderableShape).
            Dim genBoneNames = genWrap.ShapeBones.Select(Function(n) If(n?.Name?.String, "")).ToList()
            Dim bakeBoneNames = bakeWrap.ShapeBones.Select(Function(n) If(n?.Name?.String, "")).ToList()
            shapeReport.GeneratedBones = genBoneNames
            shapeReport.BakedBones = bakeBoneNames
            Dim genSet As New HashSet(Of String)(genBoneNames, StringComparer.OrdinalIgnoreCase)
            Dim bakeSet As New HashSet(Of String)(bakeBoneNames, StringComparer.OrdinalIgnoreCase)
            shapeReport.BonesMissingFromGenerated = bakeSet.Where(Function(b) Not genSet.Contains(b)).OrderBy(Function(s) s).ToList()
            shapeReport.BonesExtraInGenerated = genSet.Where(Function(b) Not bakeSet.Contains(b)).OrderBy(Function(s) s).ToList()
            shapeReport.BonesShared = genSet.Intersect(bakeSet, StringComparer.OrdinalIgnoreCase).OrderBy(Function(s) s).ToList()

            ' Vertex RMS — only meaningful when VC matches. NIF-local space, no skinning applied.
            If shapeReport.GeneratedVertexCount = shapeReport.BakedVertexCount AndAlso shapeReport.GeneratedVertexCount > 0 Then
                Dim genVerts = genWrap.Geometry.GetVertexPositions()
                Dim bakeVerts = bakeWrap.Geometry.GetVertexPositions()
                Dim n = Math.Min(genVerts.Count, bakeVerts.Count)
                Dim sumSq As Double = 0
                Dim maxDiff As Double = 0
                For i = 0 To n - 1
                    Dim dx = CDbl(bakeVerts(i).X - genVerts(i).X)
                    Dim dy = CDbl(bakeVerts(i).Y - genVerts(i).Y)
                    Dim dz = CDbl(bakeVerts(i).Z - genVerts(i).Z)
                    Dim mag = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                    sumSq += mag * mag
                    If mag > maxDiff Then maxDiff = mag
                Next
                shapeReport.VertexRms = Math.Sqrt(sumSq / Math.Max(1, n))
                shapeReport.VertexMaxDiff = maxDiff
            End If

            ' Bone translation RMS over shared bones — translation component of per-shape bind.
            ' (Rotation/scale diff omitted in this iteration; add when translation alone is solved.)
            If shapeReport.BonesShared.Count > 0 Then
                Dim genBoneIdxByName As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                For i = 0 To genBoneNames.Count - 1
                    If Not genBoneIdxByName.ContainsKey(genBoneNames(i)) Then genBoneIdxByName(genBoneNames(i)) = i
                Next
                Dim bakeBoneIdxByName As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                For i = 0 To bakeBoneNames.Count - 1
                    If Not bakeBoneIdxByName.ContainsKey(bakeBoneNames(i)) Then bakeBoneIdxByName(bakeBoneNames(i)) = i
                Next
                Dim sumSq As Double = 0
                Dim countShared As Integer = 0
                For Each bn In shapeReport.BonesShared
                    Dim gi = -1, bi = -1
                    If genBoneIdxByName.TryGetValue(bn, gi) AndAlso bakeBoneIdxByName.TryGetValue(bn, bi) Then
                        Dim gt = genWrap.ShapeBoneTransforms(gi)
                        Dim bt = bakeWrap.ShapeBoneTransforms(bi)
                        Dim dx = CDbl(gt.Translation.X - bt.Translation.X)
                        Dim dy = CDbl(gt.Translation.Y - bt.Translation.Y)
                        Dim dz = CDbl(gt.Translation.Z - bt.Translation.Z)
                        sumSq += dx * dx + dy * dy + dz * dz
                        countShared += 1
                    End If
                Next
                If countShared > 0 Then shapeReport.BoneTranslationRms = Math.Sqrt(sumSq / countShared)
            End If

            ' Bone rotation off-diagonal RMS + scale RMS over shared bones. Rotation: |R - I|_F
            ' over the upper-left 3×3 block — captures any rotation deviation regardless of axis,
            ' since two NIFs that disagree on a bone's bind orientation will produce a nonzero
            ' off-diagonal magnitude in (R_gen - R_bake).
            If shapeReport.BonesShared.Count > 0 Then
                Dim genBoneIdxByName As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                For i = 0 To genBoneNames.Count - 1
                    If Not genBoneIdxByName.ContainsKey(genBoneNames(i)) Then genBoneIdxByName(genBoneNames(i)) = i
                Next
                Dim bakeBoneIdxByName As New Dictionary(Of String, Integer)(StringComparer.OrdinalIgnoreCase)
                For i = 0 To bakeBoneNames.Count - 1
                    If Not bakeBoneIdxByName.ContainsKey(bakeBoneNames(i)) Then bakeBoneIdxByName(bakeBoneNames(i)) = i
                Next
                Dim sumSqRot As Double = 0
                Dim sumSqScale As Double = 0
                Dim cnt As Integer = 0
                For Each bn In shapeReport.BonesShared
                    Dim gi = -1, bi = -1
                    If genBoneIdxByName.TryGetValue(bn, gi) AndAlso bakeBoneIdxByName.TryGetValue(bn, bi) Then
                        Dim gt = genWrap.ShapeBoneTransforms(gi)
                        Dim bt = bakeWrap.ShapeBoneTransforms(bi)
                        Dim r1 = gt.Rotation : Dim r2 = bt.Rotation
                        ' Frobenius squared of (R_gen - R_bake).
                        Dim d11 = r1.M11 - r2.M11 : Dim d12 = r1.M12 - r2.M12 : Dim d13 = r1.M13 - r2.M13
                        Dim d21 = r1.M21 - r2.M21 : Dim d22 = r1.M22 - r2.M22 : Dim d23 = r1.M23 - r2.M23
                        Dim d31 = r1.M31 - r2.M31 : Dim d32 = r1.M32 - r2.M32 : Dim d33 = r1.M33 - r2.M33
                        sumSqRot += d11 * d11 + d12 * d12 + d13 * d13 +
                                    d21 * d21 + d22 * d22 + d23 * d23 +
                                    d31 * d31 + d32 * d32 + d33 * d33
                        Dim ds = CDbl(gt.Scale - bt.Scale)
                        sumSqScale += ds * ds
                        cnt += 1
                    End If
                Next
                If cnt > 0 Then
                    shapeReport.BoneRotationRms = Math.Sqrt(sumSqRot / cnt)
                    shapeReport.BoneScaleRms = Math.Sqrt(sumSqScale / cnt)
                End If
            End If

            ' Triangle list comparison. Two flavours:
            '   Ordered: how many triangles disagree as (V1,V2,V3) tuples — picks up winding flips.
            '   Sorted: how many triangles disagree after sorting the indices — pure topology check.
            ' If sorted=0 and ordered>0 → same topology, different windings (potential normal flip).
            Try
                Dim genTris = genWrap.Geometry.GetTriangles()
                Dim bakeTris = bakeWrap.Geometry.GetTriangles()
                Dim nT = Math.Min(genTris.Count, bakeTris.Count)
                Dim mismatchOrd As Integer = 0
                Dim mismatchSorted As Integer = 0
                For i = 0 To nT - 1
                    Dim g = genTris(i) : Dim b = bakeTris(i)
                    If g.V1 <> b.V1 OrElse g.V2 <> b.V2 OrElse g.V3 <> b.V3 Then mismatchOrd += 1
                    Dim ga = {CInt(g.V1), CInt(g.V2), CInt(g.V3)} : Array.Sort(ga)
                    Dim ba = {CInt(b.V1), CInt(b.V2), CInt(b.V3)} : Array.Sort(ba)
                    If ga(0) <> ba(0) OrElse ga(1) <> ba(1) OrElse ga(2) <> ba(2) Then mismatchSorted += 1
                Next
                shapeReport.TriangleMismatchOrdered = mismatchOrd
                shapeReport.TriangleMismatchSorted = mismatchSorted
            Catch
            End Try

            ' Per-vertex auxiliary attribute diffs (normals, tangents, UVs, colors). Each one
            ' computed only when VC matches; -2 means "both sides absent" (no diff to report);
            ' -1 means "VC mismatch or one-sided absence" (skipped).
            If shapeReport.GeneratedVertexCount = shapeReport.BakedVertexCount AndAlso shapeReport.GeneratedVertexCount > 0 Then
                shapeReport.NormalRms = Vec3RmsOrSentinel(genWrap.Geometry.GetNormals(), bakeWrap.Geometry.GetNormals())
                shapeReport.TangentRms = Vec3RmsOrSentinel(genWrap.Geometry.GetTangents(), bakeWrap.Geometry.GetTangents())
                shapeReport.UvRms = UvRmsOrSentinel(genWrap.Geometry.GetUVs(), bakeWrap.Geometry.GetUVs())
                shapeReport.VertexColorRms = ColorRmsOrSentinel(genWrap.Geometry.GetVertexColors(), bakeWrap.Geometry.GetVertexColors())
            End If

            sb.AppendLine($"[BUILDCHARGEN-DIFF] shape '{name}':")
            sb.AppendLine($"[BUILDCHARGEN-DIFF]   VC gen={shapeReport.GeneratedVertexCount} bake={shapeReport.BakedVertexCount} match={shapeReport.GeneratedVertexCount = shapeReport.BakedVertexCount}")
            sb.AppendLine($"[BUILDCHARGEN-DIFF]   TC gen={shapeReport.GeneratedTriangleCount} bake={shapeReport.BakedTriangleCount} match={shapeReport.GeneratedTriangleCount = shapeReport.BakedTriangleCount}")
            sb.AppendLine($"[BUILDCHARGEN-DIFF]   material gen.path='{shapeReport.GeneratedMaterialPath}' bake.path='{shapeReport.BakedMaterialPath}'")
            If shapeReport.GeneratedMaterial IsNot Nothing AndAlso shapeReport.BakedMaterial IsNot Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   material content match={shapeReport.MaterialMatch}")
                If Not shapeReport.MaterialMatch Then
                    DumpMaterialDiff(shapeReport.GeneratedMaterial, shapeReport.BakedMaterial, sb)
                End If
            ElseIf shapeReport.GeneratedMaterial Is Nothing AndAlso shapeReport.BakedMaterial Is Nothing Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   material: <both sides have no shader/material>")
            Else
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   material: gen={(If(shapeReport.GeneratedMaterial Is Nothing, "<none>", "<present>"))} bake={(If(shapeReport.BakedMaterial Is Nothing, "<none>", "<present>"))}")
            End If
            sb.AppendLine($"[BUILDCHARGEN-DIFF]   bones gen={genBoneNames.Count} bake={bakeBoneNames.Count} shared={shapeReport.BonesShared.Count} missing-from-gen={shapeReport.BonesMissingFromGenerated.Count} extra-in-gen={shapeReport.BonesExtraInGenerated.Count}")
            If shapeReport.BonesMissingFromGenerated.Count > 0 Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]     missing-from-gen: {String.Join(", ", shapeReport.BonesMissingFromGenerated)}")
            End If
            If shapeReport.BonesExtraInGenerated.Count > 0 Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]     extra-in-gen: {String.Join(", ", shapeReport.BonesExtraInGenerated)}")
            End If
            If shapeReport.VertexRms >= 0 Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   vertex RMS={shapeReport.VertexRms:F6} max={shapeReport.VertexMaxDiff:F6}")
            Else
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   vertex RMS=N/A (VC mismatch or empty)")
            End If
            If shapeReport.BoneTranslationRms >= 0 Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   shared-bone translation RMS={shapeReport.BoneTranslationRms:F6}  rotation RMS(|R-I|_F)={FmtRms(shapeReport.BoneRotationRms)}  scale RMS={FmtRms(shapeReport.BoneScaleRms)}")
            End If
            If shapeReport.TriangleMismatchOrdered >= 0 Then
                sb.AppendLine($"[BUILDCHARGEN-DIFF]   triangles ordered-mismatch={shapeReport.TriangleMismatchOrdered}/{Math.Min(shapeReport.GeneratedTriangleCount, shapeReport.BakedTriangleCount)}  sorted-mismatch={shapeReport.TriangleMismatchSorted} (sorted=0+ordered>0 → winding flip)")
            End If
            sb.AppendLine($"[BUILDCHARGEN-DIFF]   normals RMS={FmtAttrRms(shapeReport.NormalRms)}  tangents RMS={FmtAttrRms(shapeReport.TangentRms)}  UVs RMS={FmtAttrRms(shapeReport.UvRms)}  colors RMS={FmtAttrRms(shapeReport.VertexColorRms)}")

            report.Shapes.Add(shapeReport)
        Next

        ' Aggregate summary.
        Dim totalShapes = report.Shapes.Count
        Dim bothPresent = report.Shapes.Where(Function(s) s.PresentInGenerated AndAlso s.PresentInBaked).ToList()
        Dim missingShapes = report.Shapes.Where(Function(s) Not s.PresentInGenerated AndAlso s.PresentInBaked).ToList()
        Dim extraShapes = report.Shapes.Where(Function(s) s.PresentInGenerated AndAlso Not s.PresentInBaked).ToList()
        Dim vcMismatches = bothPresent.Where(Function(s) s.GeneratedVertexCount <> s.BakedVertexCount).ToList()
        Dim totalMissingBones = bothPresent.Sum(Function(s) s.BonesMissingFromGenerated.Count)
        Dim totalExtraBones = bothPresent.Sum(Function(s) s.BonesExtraInGenerated.Count)
        Dim shapesWithVertexRms = bothPresent.Where(Function(s) s.VertexRms >= 0).ToList()
        Dim aggRms As Double = -1
        If shapesWithVertexRms.Count > 0 Then
            Dim totSumSq As Double = 0
            Dim totN As Integer = 0
            For Each s In shapesWithVertexRms
                totSumSq += s.VertexRms * s.VertexRms * s.GeneratedVertexCount
                totN += s.GeneratedVertexCount
            Next
            aggRms = If(totN > 0, Math.Sqrt(totSumSq / totN), -1)
        End If

        ' Aggregate the new dimensions: triangle mismatches + materials + auxiliary attributes.
        Dim totalTriOrdMismatch = bothPresent.Sum(Function(s) Math.Max(0, s.TriangleMismatchOrdered))
        Dim totalTriSortedMismatch = bothPresent.Sum(Function(s) Math.Max(0, s.TriangleMismatchSorted))
        Dim shapesWithMaterialMismatch = bothPresent.Where(Function(s) s.GeneratedMaterial IsNot Nothing AndAlso s.BakedMaterial IsNot Nothing AndAlso Not s.MaterialMatch).Count()
        Dim shapesWithNormalDiff = bothPresent.Where(Function(s) s.NormalRms > 0).Count()
        Dim shapesWithTangentDiff = bothPresent.Where(Function(s) s.TangentRms > 0).Count()
        Dim shapesWithUvDiff = bothPresent.Where(Function(s) s.UvRms > 0).Count()
        Dim shapesWithColorDiff = bothPresent.Where(Function(s) s.VertexColorRms > 0).Count()

        sb.AppendLine($"[BUILDCHARGEN-DIFF] === SUMMARY ===")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   shapes: total={totalShapes} both-present={bothPresent.Count} missing-from-gen={missingShapes.Count} extra-in-gen={extraShapes.Count}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   VC mismatches: {vcMismatches.Count}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   bones across all shapes: missing-from-gen={totalMissingBones} extra-in-gen={totalExtraBones}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   aggregate vertex RMS={(If(aggRms >= 0, aggRms.ToString("F6"), "N/A"))}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   triangles: shapes-with-ordered-mismatch={bothPresent.Where(Function(s) s.TriangleMismatchOrdered > 0).Count()}  shapes-with-sorted-mismatch={bothPresent.Where(Function(s) s.TriangleMismatchSorted > 0).Count()}  total-ordered-mismatch={totalTriOrdMismatch}  total-sorted-mismatch={totalTriSortedMismatch}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   materials: shapes-with-content-mismatch={shapesWithMaterialMismatch}/{bothPresent.Count}")
        sb.AppendLine($"[BUILDCHARGEN-DIFF]   per-vertex attrs: normals-diff={shapesWithNormalDiff}  tangents-diff={shapesWithTangentDiff}  UVs-diff={shapesWithUvDiff}  colors-diff={shapesWithColorDiff}")

        report.Summary = $"shapes both={bothPresent.Count}/{totalShapes} | VC mismatches={vcMismatches.Count} | bones missing={totalMissingBones} extra={totalExtraBones} | tri-mismatch ordered={totalTriOrdMismatch} sorted={totalTriSortedMismatch} | mat-diff={shapesWithMaterialMismatch} | agg vertex RMS={If(aggRms >= 0, aggRms.ToString("F6"), "N/A")}"
        Logger.Log(sb.ToString())
        Return report
    End Function

    ''' <summary>Loguea las propiedades del material que difieren entre gen y bake.
    ''' Delega íntegramente a <see cref="FO4UnifiedMaterial_Class.GetDifferences"/>: cada
    ''' propiedad pública nueva en el modelo aparece automáticamente en este dump sin
    ''' modificar el comparator. Cero selección manual de campos.
    '''
    ''' Filter: properties whose name ends in "Texture" or "Path" are compared after path
    ''' normalization (lowercase, backslash→slash, strip leading "textures/"). Two texture
    ''' paths that differ only in casing or separator are semantically identical to the engine
    ''' (FilesDictionary lookups are case-insensitive after the same normalization).</summary>
    Private Sub DumpMaterialDiff(gen As FO4UnifiedMaterial_Class, bake As FO4UnifiedMaterial_Class, sb As StringBuilder)
        Dim diffs = FO4UnifiedMaterial_Class.GetDifferences(gen, bake)
        For Each d In diffs
            If IsCosmeticDiff(d, gen, bake) Then Continue For
            sb.AppendLine($"[BUILDCHARGEN-DIFF]     mat.{d.PropertyName}: gen='{FormatValue(d.ValueA)}' bake='{FormatValue(d.ValueB)}'")
        Next
    End Sub

    ''' <summary>True when gen and bake have NO non-cosmetic property diffs — every
    ''' difference in <see cref="FO4UnifiedMaterial_Class.GetDifferences"/> is either a
    ''' texture/path that normalizes to the same string, or an engine-disabled field
    ''' (e.g. EmittanceColor when EmitEnabled=False on both). Used by MaterialMatch in the
    ''' report so the summary count reflects engine-meaningful diffs only.</summary>
    Private Function HasOnlyCosmeticDiffs(a As FO4UnifiedMaterial_Class, b As FO4UnifiedMaterial_Class) As Boolean
        For Each d In FO4UnifiedMaterial_Class.GetDifferences(a, b)
            If IsCosmeticDiff(d, a, b) Then Continue For
            Return False
        Next
        Return True
    End Function

    ''' <summary>Returns True for diffs the engine ignores or that are pure binary noise:
    '''   - Texture/path strings whose normalized form is identical (separator/case).
    '''   - EmittanceColor / EmittanceMult when EmitEnabled=False on both sides — the BGSM
    '''     binary itself omits the EmittanceColor bytes when EmitEnabled=False
    '''     (Material-Editor-master/MaterialLib/BGSM.cs:407-410, :576-579), so the field is
    '''     inert in-game; CK leaves it at the NIF shader default while we keep the BGSM
    '''     constructor default. Cosmetic only — no render impact.
    '''   - GrayscaleToPaletteScale when both GrayscaleToPaletteColor (BSLighting) and
    '''     GrayscaleToPaletteAlpha (BSEffect) are False on both sides — the scale field is
    '''     only sampled by the engine when one of those flags is on (NifSkope renderer.cpp
    '''     binds SAMP_GRAYSCALE only inside the G2P branch). Source authored values vs CK's
    '''     0.675 default are inert when the flag is off.</summary>
    Private Function IsCosmeticDiff(d As FO4UnifiedMaterial_Class.MaterialDifference,
                                    gen As FO4UnifiedMaterial_Class,
                                    bake As FO4UnifiedMaterial_Class) As Boolean
        If IsPathProperty(d.PropertyName) Then
            Dim sa = NormalizeTexturePath(TryCast(d.ValueA, String))
            Dim sb = NormalizeTexturePath(TryCast(d.ValueB, String))
            If String.Equals(sa, sb, StringComparison.Ordinal) Then Return True
        End If

        If (Not gen.EmitEnabled) AndAlso (Not bake.EmitEnabled) Then
            If d.PropertyName.Equals(NameOf(FO4UnifiedMaterial_Class.EmittanceColor), StringComparison.Ordinal) OrElse
               d.PropertyName.Equals(NameOf(FO4UnifiedMaterial_Class.EmittanceMult), StringComparison.Ordinal) Then
                Return True
            End If
        End If

        If d.PropertyName.Equals(NameOf(FO4UnifiedMaterial_Class.GrayscaleToPaletteScale), StringComparison.Ordinal) Then
            Dim genG2p = gen.GrayscaleToPaletteColor OrElse gen.GrayscaleToPaletteAlpha
            Dim bakeG2p = bake.GrayscaleToPaletteColor OrElse bake.GrayscaleToPaletteAlpha
            If (Not genG2p) AndAlso (Not bakeG2p) Then Return True
        End If

        Return False
    End Function

    Private Function IsPathProperty(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return False
        Return name.EndsWith("Texture", StringComparison.Ordinal) OrElse
               name.EndsWith("Path", StringComparison.Ordinal)
    End Function

    ''' <summary>Normalize a texture/material path the way the engine's FilesDictionary lookup
    ''' does it: lowercase, '\' → '/', strip leading "textures/" prefix, trim. Empty/null
    ''' returns "". Two paths that compare equal after this are interchangeable in-game.</summary>
    Private Function NormalizeTexturePath(p As String) As String
        If String.IsNullOrEmpty(p) Then Return ""
        Dim s = p.Trim().ToLowerInvariant().Replace("\"c, "/"c)
        Const Prefix As String = "textures/"
        If s.StartsWith(Prefix, StringComparison.Ordinal) Then s = s.Substring(Prefix.Length)
        Return s
    End Function

    Private Function FormatValue(v As Object) As String
        If v Is Nothing Then Return ""
        Return v.ToString()
    End Function

    Private Function FmtRms(v As Double) As String
        If v < 0 Then Return "N/A"
        Return v.ToString("F6")
    End Function

    Private Function FmtAttrRms(v As Double) As String
        If v = -2 Then Return "(both absent)"
        If v < 0 Then Return "N/A"
        Return v.ToString("F6")
    End Function

    ''' <summary>RMS of |a[i] - b[i]| over equal-length Vector3 lists. Returns -2 when both
    ''' lists are empty (no field on either side → no diff to report); -1 when one is empty
    ''' or counts mismatch.</summary>
    Private Function Vec3RmsOrSentinel(a As List(Of System.Numerics.Vector3),
                                        b As List(Of System.Numerics.Vector3)) As Double
        Dim aEmpty = (a Is Nothing OrElse a.Count = 0)
        Dim bEmpty = (b Is Nothing OrElse b.Count = 0)
        If aEmpty AndAlso bEmpty Then Return -2
        If aEmpty OrElse bEmpty Then Return -1
        If a.Count <> b.Count Then Return -1
        Dim sumSq As Double = 0
        For i = 0 To a.Count - 1
            Dim dx = CDbl(a(i).X - b(i).X)
            Dim dy = CDbl(a(i).Y - b(i).Y)
            Dim dz = CDbl(a(i).Z - b(i).Z)
            sumSq += dx * dx + dy * dy + dz * dz
        Next
        Return Math.Sqrt(sumSq / a.Count)
    End Function

    Private Function UvRmsOrSentinel(a As List(Of NiflySharp.Structs.TexCoord),
                                      b As List(Of NiflySharp.Structs.TexCoord)) As Double
        Dim aEmpty = (a Is Nothing OrElse a.Count = 0)
        Dim bEmpty = (b Is Nothing OrElse b.Count = 0)
        If aEmpty AndAlso bEmpty Then Return -2
        If aEmpty OrElse bEmpty Then Return -1
        If a.Count <> b.Count Then Return -1
        Dim sumSq As Double = 0
        For i = 0 To a.Count - 1
            Dim du = CDbl(a(i).U - b(i).U)
            Dim dv = CDbl(a(i).V - b(i).V)
            sumSq += du * du + dv * dv
        Next
        Return Math.Sqrt(sumSq / a.Count)
    End Function

    Private Function ColorRmsOrSentinel(a As List(Of NiflySharp.Structs.Color4),
                                         b As List(Of NiflySharp.Structs.Color4)) As Double
        Dim aEmpty = (a Is Nothing OrElse a.Count = 0)
        Dim bEmpty = (b Is Nothing OrElse b.Count = 0)
        If aEmpty AndAlso bEmpty Then Return -2
        If aEmpty OrElse bEmpty Then Return -1
        If a.Count <> b.Count Then Return -1
        Dim sumSq As Double = 0
        For i = 0 To a.Count - 1
            Dim dr = CDbl(a(i).R - b(i).R)
            Dim dg = CDbl(a(i).G - b(i).G)
            Dim db = CDbl(a(i).B - b(i).B)
            Dim da = CDbl(a(i).A - b(i).A)
            sumSq += dr * dr + dg * dg + db * db + da * da
        Next
        Return Math.Sqrt(sumSq / a.Count)
    End Function

    Private Function IndexShapesByName(nif As Nifcontent_Class_Manolo) As Dictionary(Of String, INiShape)
        Dim d As New Dictionary(Of String, INiShape)(StringComparer.OrdinalIgnoreCase)
        For Each shap In nif.GetShapes()
            Dim name = If(shap.Name?.String, "")
            If name = "" Then Continue For
            ' If duplicate name (rare), keep first.
            If Not d.ContainsKey(name) Then d(name) = shap
        Next
        Return d
    End Function

End Module
