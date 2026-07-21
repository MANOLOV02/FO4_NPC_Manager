Imports System.Linq
Imports FO4_Base_Library
Imports NiflySharp
Imports SysNumerics = System.Numerics

''' <summary>Estimates an armor-sculpt (.sclp) seed for an underarmor mesh by measuring, per <c>_skin</c>
''' bone and axis, how much the underarmor geometry is scaled relative to the naked reference body around
''' that bone's local frame. This is the <c>estNN</c> (nearest-neighbor least-squares) metric from
''' FO4_FaceTint_CLI (<c>BuildNNPairs</c> + <c>AccumulateNNScales</c>), ported verbatim so the app produces
''' the same numbers the CLI validated against authored SCLP files.
'''
''' <para>Model: the underarmor mesh = the body deformed by skinning the <c>_skin</c> bones with a diagonal
''' per-bone scale S_b applied in the bone-LOCAL frame around the ORIGIN. The skin→bone binds of the
''' underarmor and the body are identical, so bringing both a vertex <c>u</c> (underarmor) and its nearest
''' body match <c>p</c> into the bone-local frame reduces the per-axis scale to a least-squares slope by the
''' origin: <c>s = Σ(w·lu·lp) / Σ(w·lp²)</c>. X is left at 1.0 (never estimated). Only the vertical/depth
''' axes (Y, Z) carry the sculpt volume.</para>
'''
''' <para>App-local on purpose (the ARMA editor's Estimate button is the single consumer). Loads NIFs the
''' same way the render path does — <see cref="Nifcontent_Class_Manolo.Load_Manolo"/> + <see cref="NifRenderableShape"/>.</para></summary>
Public Module SclpEstimator

    ''' <summary>Why an axis ended up with the value it has. EXISTS BECAUSE THE OLD CONTRACT WAS AMBIGUOUS:
    ''' <see cref="EstimateAxis"/> returned the bare Single 1.0F for FOUR different situations — a genuine
    ''' measured scale of 1.0, a fit rejected by the residual gate, a degenerate denominator, and NaN/Inf —
    ''' so the caller could not tell "I measured 1.0" from "I could not measure". The ARMA editor then wrote
    ''' all four into the grid as if they were authored values. The NUMBERS were never wrong (the estimator
    ''' round-trips exactly on synthetic realistic geometry); only the contract was.</summary>
    Public Enum SclpAxisStatus
        ''' <summary>A real least-squares measurement that passed every gate. The value is meaningful.</summary>
        Measured
        ''' <summary>Sxx below 1e-6: no variance along this axis to regress against. NOT a measurement.</summary>
        NotMeasuredDegenerate
        ''' <summary>Normalized reconstruction residual above 0.25: the linear model does not explain the
        ''' geometry, so the slope is not trustworthy. NOT a measurement.</summary>
        NotMeasuredResidual
        ''' <summary>The slope came out NaN or Infinite. NOT a measurement.</summary>
        NotMeasuredNonFinite
    End Enum

    ''' <summary>One bone's estimate WITH its per-axis measurement status. The dangerous case this exists for is
    ''' the MIXED bone: one axis genuinely measured and the other failed. The "both axes failed" bone is already
    ''' dropped by the caller, but a mixed bone must still be emitted (the good axis is real data) while the
    ''' failed axis must NOT be presented as an authored value.</summary>
    Public NotInheritable Class SclpBoneEstimate
        Public Property Name As String
        ''' <summary>Always 1.0: X is never estimated (SCLP body scale is authored on Y/Z only).</summary>
        Public ReadOnly Property X As Single
            Get
                Return 1.0F
            End Get
        End Property
        Public Property Y As Single
        Public Property Z As Single
        Public Property YStatus As SclpAxisStatus
        Public Property ZStatus As SclpAxisStatus
        Public ReadOnly Property YMeasured As Boolean
            Get
                Return YStatus = SclpAxisStatus.Measured
            End Get
        End Property
        Public ReadOnly Property ZMeasured As Boolean
            Get
                Return ZStatus = SclpAxisStatus.Measured
            End Get
        End Property
        ''' <summary>True when NEITHER axis is a real measurement — nothing to show for this bone.</summary>
        Public ReadOnly Property AnyMeasured As Boolean
            Get
                Return YMeasured OrElse ZMeasured
            End Get
        End Property
        Public Function ToAbsolute() As SclpFile.SclpBoneAbsolute
            Return New SclpFile.SclpBoneAbsolute With {.Name = Name, .X = 1.0F, .Y = Y, .Z = Z}
        End Function
    End Class

    ' Per-bone vertex record collected from a NIF: model = raw vertex position (model space), local = that
    ' position brought into the bone-local frame by the shape's skin→bone bind, w = the vertex's continuous
    ' weight to the bone. (Same triple as the CLI's CollectBoneVertexData.)
    Private Structure BoneVert
        Public Model As SysNumerics.Vector3
        Public Local As SysNumerics.Vector3
        Public W As Single
    End Structure

    ''' <summary>Estimate absolute per-bone SCLP scales for one gender from the underarmor mesh
    ''' (<paramref name="uaNifBytes"/>) against the naked reference body (<paramref name="bodyNifBytes"/>).
    ''' Returns one <see cref="SclpFile.SclpBoneAbsolute"/> per <c>_skin</c> bone with a VALID, non-identity
    ''' estimate (X = 1.0 always, Y/Z = estimated slope or 1.0 when gated out). Empty list on any load
    ''' failure or when nothing estimated. Never throws for bad NIF data — malformed shapes are skipped.</summary>
    ''' <param name="wEps">Weight floor: vertices with weight ≤ this are excluded (numerically-null weights,
    ''' not a dominance heuristic). Matches the CLI's NN_WEPS default.</param>
    Public Function EstimateSclp(uaNifBytes As Byte(), bodyNifBytes As Byte(), Optional wEps As Single = 0.01F) As List(Of SclpFile.SclpBoneAbsolute)
        Return EstimateSclp(uaNifBytes, New Byte()() {bodyNifBytes}, wEps)
    End Function

    ''' <summary>Same as the single-body overload, but the reference body is the UNION of several naked-skin part
    ''' meshes (body + hands + feet) — their per-bone vertices are merged so every bone the underarmor touches has
    ''' a body counterpart, without picking a single ARMA. Malformed/empty entries are skipped.</summary>
    Public Function EstimateSclp(uaNifBytes As Byte(), bodyNifBytesList As IReadOnlyList(Of Byte()), Optional wEps As Single = 0.01F) As List(Of SclpFile.SclpBoneAbsolute)
        Return EstimateSclpDetailed(uaNifBytes, bodyNifBytesList, wEps).Select(Function(b) b.ToAbsolute()).ToList()
    End Function

    ''' <summary>Same estimate as <see cref="EstimateSclp"/> but preserving, PER AXIS, whether the number is a
    ''' real measurement or a "could not measure" identity fallback (<see cref="SclpAxisStatus"/>). Prefer this
    ''' overload: the plain one collapses both cases back to a bare 1.0F and loses the distinction, which is
    ''' exactly the ambiguity this contract exists to remove. Bones where NEITHER axis measured are not
    ''' emitted at all.</summary>
    Public Function EstimateSclpDetailed(uaNifBytes As Byte(), bodyNifBytesList As IReadOnlyList(Of Byte()), Optional wEps As Single = 0.01F) As List(Of SclpBoneEstimate)
        Dim result As New List(Of SclpBoneEstimate)
        If uaNifBytes Is Nothing OrElse uaNifBytes.Length = 0 Then Return result
        If bodyNifBytesList Is Nothing OrElse bodyNifBytesList.Count = 0 Then Return result

        ' Body reference = UNION of every naked-skin part's per-bone vertices (body + hands + feet), each read
        ' with ALL its skinned shapes. Underarmor = skinned shapes EXCLUDING skin-tint (the embedded body/skin)
        ' so the estimate measures the garment, not the body baked into the outfit NIF.
        Dim bodyData As New Dictionary(Of String, List(Of BoneVert))(StringComparer.OrdinalIgnoreCase)
        For Each bb In bodyNifBytesList
            If bb Is Nothing OrElse bb.Length = 0 Then Continue For
            Dim d = CollectBoneVertexData(bb, wEps, excludeSkinTint:=False)
            If d Is Nothing Then Continue For
            For Each kv In d
                Dim lst As List(Of BoneVert) = Nothing
                If Not bodyData.TryGetValue(kv.Key, lst) Then
                    lst = New List(Of BoneVert)()
                    bodyData(kv.Key) = lst
                End If
                lst.AddRange(kv.Value)
            Next
        Next
        If bodyData.Count = 0 Then Return result

        Dim uaData = CollectBoneVertexData(uaNifBytes, wEps, excludeSkinTint:=True)
        If uaData Is Nothing OrElse uaData.Count = 0 Then Return result

        For Each kv In uaData
            Dim bn = kv.Key
            ' Only *_skin bones carry SCLP scale (case-insensitive, matching the .sclp bone naming).
            If String.IsNullOrEmpty(bn) OrElse Not bn.EndsWith("_skin", StringComparison.OrdinalIgnoreCase) Then Continue For
            Dim cand As List(Of BoneVert) = Nothing
            If Not bodyData.TryGetValue(bn, cand) OrElse cand Is Nothing OrElse cand.Count = 0 Then Continue For   ' bone only in UA → skip

            ' Accumulate the per-origin least-squares regression per axis, over nearest-neighbor pairs
            ' (u → nearest body vertex p, in MODEL space), weighted by the vertex weight w. Sxx = Σ w·lp²,
            ' Sxy = Σ w·lu·lp, Syy = Σ w·lu² (for the reconstruction residual). Identical to the CLI.
            Dim sxx0, sxx1, sxx2, sxy0, sxy1, sxy2, syy0, syy1, syy2 As Double
            Dim dominated As Integer = 0   ' vertices with weight > 0.5 (bone-dominance gate)
            For Each u In kv.Value
                Dim best As Integer = -1
                Dim bestD As Single = Single.MaxValue
                For ci = 0 To cand.Count - 1
                    Dim d = SysNumerics.Vector3.DistanceSquared(u.Model, cand(ci).Model)
                    If d < bestD Then bestD = d : best = ci
                Next
                If best < 0 Then Continue For
                Dim lu = u.Local
                Dim lp = cand(best).Local
                Dim w As Double = CDbl(u.W)
                If u.W > 0.5F Then dominated += 1
                sxx0 += w * CDbl(lp.X) * CDbl(lp.X) : sxy0 += w * CDbl(lu.X) * CDbl(lp.X) : syy0 += w * CDbl(lu.X) * CDbl(lu.X)
                sxx1 += w * CDbl(lp.Y) * CDbl(lp.Y) : sxy1 += w * CDbl(lu.Y) * CDbl(lp.Y) : syy1 += w * CDbl(lu.Y) * CDbl(lu.Y)
                sxx2 += w * CDbl(lp.Z) * CDbl(lp.Z) : sxy2 += w * CDbl(lu.Z) * CDbl(lp.Z) : syy2 += w * CDbl(lu.Z) * CDbl(lu.Z)
            Next

            ' Bone-level gate: too few dominated vertices ⇒ noisy fit ⇒ leave the whole bone at identity.
            If dominated < 8 Then Continue For

            ' X is NEVER estimated (SCLP body scale is authored on Y/Z only). Y/Z per-axis: slope by origin
            ' with a residual gate — an axis whose fit does not explain the variance (>0.25) stays at 1.0.
            Dim yStatus As SclpAxisStatus, zStatus As SclpAxisStatus
            Dim yVal = EstimateAxis(sxx1, sxy1, syy1, yStatus)
            Dim zVal = EstimateAxis(sxx2, sxy2, syy2, zStatus)
            ' Emitir SÓLO si al menos un eje es MEDICIÓN REAL. El test viejo era `yVal = 1.0F AndAlso
            ' zVal = 1.0F`, que confundía "medí exactamente 1.0 en los dos ejes" (dato legítimo, se
            ' descartaba) con "no pude medir ninguno" (lo que se quería descartar). Ahora se pregunta por
            ' el ESTADO, no por el valor.
            If yStatus <> SclpAxisStatus.Measured AndAlso zStatus <> SclpAxisStatus.Measured Then Continue For

            result.Add(New SclpBoneEstimate With {.Name = bn, .Y = yVal, .Z = zVal, .YStatus = yStatus, .ZStatus = zStatus})
        Next

        Return result
    End Function

    ''' <summary>Per-axis least-squares slope by the origin (<c>s = Sxy/Sxx</c>) with the estNN gating.
    ''' The returned VALUE is unchanged from before (the math was never the problem); what is new is
    ''' <paramref name="status"/>, which says WHICH of the four outcomes produced it. The 1.0F fallback is
    ''' still returned for every failure so the value is safe to render, but the caller can now tell a
    ''' measured 1.0 apart from an unmeasurable axis.</summary>
    ''' <param name="status">Measured, or the specific reason the axis could not be measured.</param>
    Private Function EstimateAxis(sxx As Double, sxy As Double, syy As Double, ByRef status As SclpAxisStatus) As Single
        If sxx < 0.000001 Then
            status = SclpAxisStatus.NotMeasuredDegenerate
            Return 1.0F
        End If
        Dim s = sxy / sxx
        Dim resid = Math.Sqrt(Math.Max(0.0, syy - s * sxy) / Math.Max(0.000000001, syy))
        If resid > 0.25 Then
            status = SclpAxisStatus.NotMeasuredResidual
            Return 1.0F
        End If
        Dim r As Single = CSng(s)
        If Single.IsNaN(r) OrElse Single.IsInfinity(r) Then
            status = SclpAxisStatus.NotMeasuredNonFinite
            Return 1.0F
        End If
        status = SclpAxisStatus.Measured
        Return r
    End Function

    ''' <summary>Load a NIF from its bytes and return, per bone NAME (unioned across shapes), the list of
    ''' vertices skinned to that bone with weight &gt; <paramref name="wEps"/>: model = raw vertex position,
    ''' local = that position brought into the bone-local frame by the shape's skin→bone bind, w = the
    ''' continuous weight. Skipped shapes: unskinned, no geometry, and — when <paramref name="excludeSkinTint"/>
    ''' — skin-tint shapes (the embedded body/skin, mirror of NpcMorphPoseResolver.ShapeIsSkinTinted). Returns
    ''' Nothing when the NIF does not load. (Port of the CLI's CollectBoneVertexData.)</summary>
    Private Function CollectBoneVertexData(nifBytes As Byte(), wEps As Single, excludeSkinTint As Boolean) As Dictionary(Of String, List(Of BoneVert))
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            nif.Load_Manolo(nifBytes)
        Catch
            Return Nothing
        End Try
        If nif.Blocks Is Nothing Then Return Nothing

        Dim acc As New Dictionary(Of String, List(Of BoneVert))(StringComparer.OrdinalIgnoreCase)
        For blkIdx = 0 To nif.Blocks.Count - 1
            Dim shp = TryCast(nif.Blocks(blkIdx), INiShape)
            If shp Is Nothing Then Continue For
            Dim rs As NifRenderableShape
            Try
                rs = New NifRenderableShape(nif, shp, blkIdx)
            Catch
                Continue For
            End Try
            If rs.ShapeBones Is Nothing OrElse rs.ShapeBones.Count = 0 Then Continue For
            If excludeSkinTint AndAlso ShapeIsSkinTinted(rs) Then Continue For   ' skip embedded body/skin on the UA

            Dim verts As List(Of SysNumerics.Vector3) = Nothing
            Try
                verts = rs.Geometry?.GetVertexPositions()
            Catch
            End Try
            If verts Is Nothing OrElse verts.Count = 0 Then Continue For
            Dim skin As ShapeSkinningData
            Try
                skin = rs.Geometry.GetSkinning()
            Catch
                Continue For
            End Try
            Dim wpv = skin.WeightsPerVertex
            If wpv <= 0 OrElse skin.BoneIndices Is Nothing OrElse skin.BoneWeights Is Nothing Then Continue For
            Dim bones = rs.ShapeBones
            Dim binds = rs.ShapeBoneTransforms
            If binds Is Nothing Then Continue For
            Dim nVerts = Math.Min(verts.Count, skin.VertexCount)
            For i = 0 To nVerts - 1
                Dim vp = verts(i)
                For j = 0 To wpv - 1
                    Dim idx = i * wpv + j
                    If idx >= skin.BoneWeights.Length OrElse idx >= skin.BoneIndices.Length Then Continue For
                    Dim w As Single = CType(skin.BoneWeights(idx), Single)
                    If w <= wEps Then Continue For
                    Dim bi = CInt(skin.BoneIndices(idx))
                    If bi < 0 OrElse bi >= bones.Count OrElse bi >= binds.Count Then Continue For
                    Dim bn = bones(bi)?.Name?.String
                    If String.IsNullOrEmpty(bn) Then Continue For
                    Dim bind = binds(bi)
                    If bind Is Nothing Then Continue For
                    Dim pT As New Transform_Class With {.Translation = vp}
                    Dim lp = bind.ComposeTransforms(pT).Translation
                    Dim lst As List(Of BoneVert) = Nothing
                    If Not acc.TryGetValue(bn, lst) Then
                        lst = New List(Of BoneVert)()
                        acc(bn) = lst
                    End If
                    lst.Add(New BoneVert With {.Model = vp, .Local = lp, .W = w})
                Next
            Next
        Next
        Return acc
    End Function

    ''' <summary>Mirror of NpcMorphPoseResolver.ShapeIsSkinTinted: true when the shape's resolved material is a
    ''' skin-tint lighting material (embedded body/skin). Any missing material link ⇒ False.</summary>
    Private Function ShapeIsSkinTinted(shape As IRenderableShape) As Boolean
        Dim rel = shape.ShapeMaterial
        If rel Is Nothing OrElse rel.material Is Nothing Then Return False
        Return rel.material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint OrElse rel.material.SkinTint
    End Function

End Module
