Imports FO4_Base_Library
Imports NiflySharp.Blocks

' ==========================================================================
' BodySlide TRI (PIRT) resolution and loading.
'
' This helper handles ONLY PIRT-format .tri files (BodySlide / Outfit Studio output).
' It NEVER touches FRTRI003 (vanilla Bethesda face .tri) — face morphs go through
' NpcMorphResolver. The two formats and pipelines are intentionally separate.
'
' Path resolution mirrors LooksMenu's BodyMorphProcessor::Process exactly
' (F4SEPlugins-master/f4ee/BodyMorphInterface.cpp:1350-1396):
'   • Read NiStringExtraData with name "BODYTRI" from the NIF's ROOT NiNode.
'   • Prefix with "meshes\" and use that path verbatim.
'   • If the root has no BODYTRI extra-data → no BodySlide morphs apply, period.
' No filename-convention fallbacks (no <mesh>.tri swap, no _0/_1 stripping). LM
' doesn't have any either — diverging here would make the preview show morphs the
' user won't see in Fallout 4.
' ==========================================================================

Public Class BodySlideTriResolver
    ' Per-process cache: a given .tri PIRT is loaded at most once. Concurrent shape
    ' resolves share the cached result. SyncLock keeps it thread-safe even though
    ' typical render flow is single-threaded.
    Private Shared ReadOnly _pirtCache As New Dictionary(Of String, TriFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Resolve the PIRT .tri path for a shape. Returns Nothing if the NIF root
    ''' has no BODYTRI extra-data. Does NOT load the file — call LoadPirt with the result.
    ''' meshDictKey is unused (kept in the signature for caller-side stability while we
    ''' migrate; LM doesn't consult the mesh path at all).</summary>
    Public Shared Function ResolvePirtPath(shape As IRenderableShape, meshDictKey As String) As String
        Dim bodyTri = MeshPathHelpers.ReadBodyTriPath(shape, includeShapeLevel:=False)
        If String.IsNullOrEmpty(bodyTri) Then Return Nothing
        Return MeshPathHelpers.NormalizeMeshKey(bodyTri)
    End Function

    ''' <summary>Load a PIRT .tri from FilesDictionary. Returns Nothing if path is empty,
    ''' file is missing, or bytes are not in PIRT format. FRTRI003 files are explicitly
    ''' rejected — this helper only loads BodySlide morphs.</summary>
    Public Shared Function LoadPirt(normalizedPath As String) As TriFile
        If String.IsNullOrEmpty(normalizedPath) Then Return Nothing

        SyncLock _pirtCache
            Dim cached As TriFile = Nothing
            If _pirtCache.TryGetValue(normalizedPath, cached) Then Return cached
        End SyncLock

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(normalizedPath, loc) Then Return Nothing
        Dim bytes = loc.GetBytes()
        If bytes Is Nothing OrElse bytes.Length < 4 Then Return Nothing

        ' Only accept PIRT. FRTRI003 belongs to NpcMorphResolver's face pipeline.
        If Not (bytes(0) = &H50 AndAlso bytes(1) = &H49 AndAlso bytes(2) = &H52 AndAlso bytes(3) = &H54) Then
            Return Nothing
        End If

        Try
            Dim parsed = TriFileParser.ParseTriFromBytes(bytes)
            SyncLock _pirtCache
                _pirtCache(normalizedPath) = parsed
            End SyncLock
            Return parsed
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>Convenience: resolve and load in one call.</summary>
    Public Shared Function ResolveAndLoad(shape As IRenderableShape, meshDictKey As String) As TriFile
        Dim path = ResolvePirtPath(shape, meshDictKey)
        Return LoadPirt(path)
    End Function

    ''' <summary>Enumerate the union of slider names across all shapes' PIRT .tri files.
    ''' Excludes WeightThin/Muscular/Fat (applied via bone scaling, not vertex morph) and
    ''' MorphRegion0..15 (legacy CBBE convention; MRSV applies via bones too).
    ''' Returns alphabetically sorted distinct names.</summary>
    Public Shared Function EnumerateSliderNames(shapes As IEnumerable(Of IRenderableShape),
                                                meshDictKeys As Dictionary(Of IRenderableShape, String)) As List(Of String)
        Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If shapes Is Nothing Then Return New List(Of String)
        For Each shape In shapes
            Dim meshKey As String = Nothing
            If meshDictKeys IsNot Nothing Then meshDictKeys.TryGetValue(shape, meshKey)
            Dim pirt = ResolveAndLoad(shape, meshKey)
            If pirt Is Nothing Then Continue For
            For Each kvp In pirt.ShapeMorphs
                For Each morph In kvp.Value
                    If IsExcludedSliderName(morph.Name) Then Continue For
                    names.Add(morph.Name)
                Next
            Next
        Next
        Dim sorted = names.ToList()
        sorted.Sort(StringComparer.OrdinalIgnoreCase)
        Return sorted
    End Function

    ''' <summary>True if the morph name is reserved for a non-vertex-morph pipeline (MWGT or
    ''' MRSV) and should not appear as a BodySlide slider.</summary>
    Public Shared Function IsExcludedSliderName(name As String) As Boolean
        If String.IsNullOrEmpty(name) Then Return True
        If name.Equals("WeightThin", StringComparison.OrdinalIgnoreCase) Then Return True
        If name.Equals("WeightMuscular", StringComparison.OrdinalIgnoreCase) Then Return True
        If name.Equals("WeightFat", StringComparison.OrdinalIgnoreCase) Then Return True
        ' Legacy CBBE region morph naming. MRSV runs via bones now.
        If name.StartsWith("MorphRegion", StringComparison.OrdinalIgnoreCase) Then
            Dim rest = name.Substring("MorphRegion".Length)
            Dim n As Integer
            If Integer.TryParse(rest, n) Then Return True
        End If
        Return False
    End Function

End Class
