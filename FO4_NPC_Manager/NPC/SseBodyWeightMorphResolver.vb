Imports FO4_Base_Library
Imports NiflySharp
Imports OpenTK.Mathematics
Imports SysNumerics = System.Numerics

' ==========================================================================
' SSE vanilla body-weight (_0 / _1) vertex LERP, expressed as an IMorphResolver.
'
' Skyrim ships two weight models per body/armor addon: <mesh>_0.nif (weight 0)
' and <mesh>_1.nif (weight 100). The engine loads the ARMA-referenced model,
' derives the twin by flipping the 5th-from-last char of the path ('0'<->'1'),
' and blends v = v0*(1-t) + v1*t with t = clamp(NAM7/100, 0, 1) (System B,
' reference_sse_engine_facegen_re, load-time CPU).
'
' Instead of a load-time bake we express the LERP as ONE morph channel:
'   base = geom.NifLocalVertices (the addon model the app actually loaded)
'   delta_i = twinPos_i - base_i          (always twin - base)
'   weight  = If(baseDigit = '0', t, 1 - t)
' so MorphEngine.ApplyChannelsToVertexArray computes base + weight*delta ==
' v0*(1-t) + v1*t regardless of whether the loaded model is the _0 or _1 twin.
' This inherits the pipeline's normal/TBN recalc, world-cache invalidation,
' re-skin and GL re-upload for free (MorphEngine.ApplyMorphPlan) and gives the
' editor a cheap live weight slider.
'
' GAME GATE: only constructed for Skyrim NPCs (BuildSseBodyWeightResolver).
' FO4 never builds this resolver, so the FO4 render path is byte-identical.
' ==========================================================================

Friend Class SseBodyWeightMorphResolver
    Implements IMorphResolver

    ' t = clamp(NAM7/100, 0, 1). Shared by all shapes (per-actor weight).
    Private ReadOnly _t As Single
    Private ReadOnly _isFemale As Boolean
    Private ReadOnly _meshDictKeys As Dictionary(Of IRenderableShape, String)
    Private ReadOnly _shapeCandidate As Dictionary(Of IRenderableShape, MainForm.MeshCandidate)
    Private ReadOnly _ctx As NpcRenderContext

    ' Per-instance twin-position cache (keyed by twinKey & "|" & shapeName), SyncLock-guarded because
    ' ResolveMorphPlan runs under Parallel.ForEach (MorphEngine / PipelineStep_Morphs). Instance-scoped
    ' (not Shared) so a load-order change discards it with the resolver. Mirrors BodySlideTriResolver._pirtCache.
    Private ReadOnly _twinCache As New Dictionary(Of String, List(Of SysNumerics.Vector3))(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly _twinCacheLock As New Object()

    Friend Sub New(t As Single, isFemale As Boolean,
                   meshDictKeys As Dictionary(Of IRenderableShape, String),
                   shapeCandidate As Dictionary(Of IRenderableShape, MainForm.MeshCandidate),
                   ctx As NpcRenderContext)
        _t = t
        _isFemale = isFemale
        _meshDictKeys = meshDictKeys
        _shapeCandidate = shapeCandidate
        _ctx = ctx
    End Sub

    Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan _
            Implements IMorphResolver.ResolveMorphPlan
        Dim plan As New MorphPlan()
        If shape Is Nothing Then Return plan
        Dim shapeName = shape.ShapeName
        If String.IsNullOrEmpty(shapeName) Then Return plan

        ' (1) Per-shape mesh key (FilesDictionary key). Absent → no twin.
        Dim meshKey As String = Nothing
        If _meshDictKeys IsNot Nothing Then _meshDictKeys.TryGetValue(shape, meshKey)
        If String.IsNullOrEmpty(meshKey) Then Return plan

        ' (2) ARMA weight-slider flag (DNAM bit 0x02): the addon must declare _0/_1 weight models for
        '     this gender. Flag clear (or ARMA unresolvable) → single mesh, no twin. Consumes the
        '     already-parsed ARMA_Data — no parser edit. xEdit coerces 0→2, so most armors are enabled.
        If Not IsWeightSliderEnabled(shape) Then Return plan

        ' (3) Derive twin by flipping path[len-5] ('0'<->'1'). Non-twin path → single mesh no-op.
        Dim idx = meshKey.Length - 5   ' ".nif" = 4 chars; the weight digit precedes the dot
        If idx < 0 Then Return plan
        Dim baseDigit = meshKey(idx)
        If baseDigit <> "0"c AndAlso baseDigit <> "1"c Then Return plan
        Dim twinKey = meshKey.Substring(0, idx) & (If(baseDigit = "0"c, "1"c, "0"c)) & meshKey.Substring(idx + 1)

        ' (4) Load + cache the twin shape's local vertex positions.
        Dim twinPos = LoadTwinPositions(twinKey, shapeName)
        If twinPos Is Nothing Then Return plan

        ' Vertex-count/order identity guard: on any mismatch emit nothing (never partial-apply).
        If geom.NifLocalVertices Is Nothing OrElse twinPos.Count <> geom.NifLocalVertices.Length Then Return plan

        ' (5) Emit one channel. delta = twin - base (in MorphData.PosDiff's type = OpenTK Vector3/float);
        '     weight = If(baseDigit='0', t, 1-t). Sparse (skip near-zero deltas).
        Dim weight As Single = If(baseDigit = "0"c, _t, 1.0F - _t)
        Dim deltas As New List(Of MorphData)(twinPos.Count)
        For i = 0 To twinPos.Count - 1
            Dim bl = geom.NifLocalVertices(i)                       ' OpenTK Vector3d (double)
            Dim dx = CSng(twinPos(i).X - bl.X)
            Dim dy = CSng(twinPos(i).Y - bl.Y)
            Dim dz = CSng(twinPos(i).Z - bl.Z)
            If dx * dx + dy * dy + dz * dz < 0.0000001F Then Continue For
            deltas.Add(New MorphData With {.index = CUInt(i), .PosDiff = New Vector3(dx, dy, dz)})
        Next

        If deltas.Count > 0 Then plan.Channels.Add(New MorphChannel("SseBodyWeight", weight, deltas))
        Return plan
    End Function

    ''' <summary>True when the shape's owning ARMA declares _0/_1 weight models for the actor's gender
    ''' (DNAM byte[2]=male / byte[3]=female, bit 0x02). Flag clear, or the shape has no candidate / the
    ''' ARMA can't be resolved → False (conservative: render the loaded model with no weight morph rather
    ''' than guessing a twin). See SSE_BODY_MORPH_PLAN §1.1.</summary>
    Private Function IsWeightSliderEnabled(shape As IRenderableShape) As Boolean
        If _shapeCandidate Is Nothing OrElse _ctx Is Nothing Then Return False
        Dim cand As MainForm.MeshCandidate = Nothing
        If Not _shapeCandidate.TryGetValue(shape, cand) OrElse cand Is Nothing Then Return False
        If cand.ArmorAddonFormID = 0UI Then Return False
        Dim arma = _ctx.GetParsedArma(cand.ArmorAddonFormID)
        If arma Is Nothing Then Return False
        Dim flags As Byte = If(_isFemale, arma.FemaleWeightSliderFlags, arma.MaleWeightSliderFlags)
        Return (flags And &H2) <> 0
    End Function

    ''' <summary>Load the twin NIF's local vertex positions for the shape whose name matches
    ''' <paramref name="shapeName"/> (case-sensitive, matching the engine/LM convention). Cached per
    ''' (twinKey, shapeName). Returns Nothing when the twin file is missing, has no matching shape, or
    ''' the shape is not a supported geometry type — caller then emits no morph (graceful fallback).</summary>
    Private Function LoadTwinPositions(twinKey As String, shapeName As String) As List(Of SysNumerics.Vector3)
        Dim cacheKey = twinKey & "|" & shapeName
        SyncLock _twinCacheLock
            Dim cached As List(Of SysNumerics.Vector3) = Nothing
            If _twinCache.TryGetValue(cacheKey, cached) Then Return cached
        End SyncLock

        Dim result As List(Of SysNumerics.Vector3) = Nothing
        Try
            Dim bytes = MeshPathHelpers.TryLoadMeshBytes(twinKey)
            If bytes IsNot Nothing Then
                Dim twinNif As New Nifcontent_Class_Manolo()
                twinNif.Load_Manolo(bytes)
                Dim twinShape As INiShape = Nothing
                For Each s In twinNif.NifShapes
                    If s IsNot Nothing AndAlso s.Name IsNot Nothing AndAlso
                       String.Equals(s.Name.String, shapeName, StringComparison.Ordinal) AndAlso
                       ShapeGeometryFactory.IsSupported(s) Then
                        twinShape = s
                        Exit For
                    End If
                Next
                If twinShape IsNot Nothing Then
                    result = ShapeGeometryFactory.[For](twinShape, twinNif).GetVertexPositions()
                End If
            End If
        Catch
            result = Nothing
        End Try

        SyncLock _twinCacheLock
            _twinCache(cacheKey) = result   ' cache Nothing too (negative cache: don't re-decompress)
        End SyncLock
        Return result
    End Function

End Class
