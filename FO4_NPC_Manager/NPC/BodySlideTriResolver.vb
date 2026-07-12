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
    ' Per-process cache: a given .tri PIRT is loaded at most once, and a failed load (missing in
    ' FilesDictionary, non-PIRT magic, unparseable) is remembered as Nothing so we don't re-decompress
    ' BA2 bytes for an absent path on every render. ResolveMorphPlan runs under Parallel.ForEach
    ' (PipelineStep_Morphs), so several shapes hit the SAME path at once — PathLoadCache serializes
    ' those on a per-path gate and hands them all the same result. See PathLoadCache.vb for the race
    ' this replaced.
    Private Shared ReadOnly _pirtCache As New PathLoadCache(Of TriFile)()

    ''' <summary>Drop the per-process PIRT parse cache. Call on load-order change (FilesDictionary rebuilt) so
    ''' a stale parse from a path that now resolves to different bytes is discarded and the parsed-geometry
    ''' entries are freed. Within a FIXED load order it's never called, so each BodySlide .tri is parsed at
    ''' most once — no re-parse churn during a session.</summary>
    Public Shared Sub ClearCaches()
        _pirtCache.Clear()
    End Sub

    ''' <summary>Resolve the PIRT .tri path for a shape. Returns Nothing if the NIF root
    ''' has no BODYTRI extra-data. Does NOT load the file — call LoadPirt with the result.
    ''' meshDictKey is unused (kept in the signature for caller-side stability while we
    ''' migrate; LM doesn't consult the mesh path at all).</summary>
    Public Shared Function ResolvePirtPath(shape As IRenderableShape, meshDictKey As String) As String
        ' GAME-AWARE BODYTRI resolution — BodySlide/OutfitStudio marks the .tri path in a DIFFERENT place
        ' per game (BodySlideApp.cpp AddTriData): FO4/FO4VR/FO76 → root NiNode; Skyrim/SSE → a NiShape.
        ' So we mirror each engine's reader:
        '   • FO4  (F4EE BodyMorphProcessor::Process, BodyMorphInterface.cpp:1356): root->GetExtraData → root only.
        '   • SSE  (skee64 MorphCache::ApplyMorphs, BodyMorphInterface.cpp:690): VisitObjects → whole subtree.
        ' Reading root-only under SSE misses outfit .tri files whose BODYTRI sits on the shape → no morphs
        ' (the observed "BodySlide works on the body but not the outfit" bug).
        Dim isSse = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim bodyTri As String
        Dim foundOwner As String = ""
        If isSse Then
            bodyTri = MeshPathHelpers.ScanBodyTriAnywhere(shape, foundOwner)
        Else
            bodyTri = MeshPathHelpers.ReadBodyTriPath(shape, includeShapeLevel:=False)
        End If

        If Logger.Enabled Then
            Dim shName = If(shape Is Nothing, "<null>", If(shape.ShapeName, "?"))
            Dim rawTri = If(bodyTri, "")
            Dim ownerS = foundOwner
            Logger.LogLazy(Function() $"[BODYSLIDE-TRI] shape='{shName}' game={If(isSse, "SSE", "FO4")} meshKey='{If(meshDictKey, "")}' bodyTriRaw='{rawTri}' foundAt='{ownerS}'")
        End If

        If String.IsNullOrEmpty(bodyTri) Then Return Nothing
        Return MeshPathHelpers.NormalizeMeshKey(bodyTri)
    End Function

    ''' <summary>Load a PIRT .tri from FilesDictionary. Returns Nothing if path is empty,
    ''' file is missing, or bytes are not in PIRT format. FRTRI003 files are explicitly
    ''' rejected — this helper only loads BodySlide morphs.</summary>
    Public Shared Function LoadPirt(normalizedPath As String) As TriFile
        If String.IsNullOrEmpty(normalizedPath) Then Return Nothing

        Return _pirtCache.GetOrLoad(normalizedPath,
            Function() As TriFile
                ' Routed through MeshPathHelpers.TryLoadMeshBytes (minBytes:=4 preserves the PIRT-magic guard)
                ' so the TryGetValue + GetBytes + size-check lives in one place (DUP-004).
                Dim bytes = MeshPathHelpers.TryLoadMeshBytes(normalizedPath, minBytes:=4)
                If bytes Is Nothing Then Return Nothing

                ' Only accept PIRT. FRTRI003 belongs to NpcMorphResolver's face pipeline.
                If Not (bytes(0) = &H50 AndAlso bytes(1) = &H49 AndAlso bytes(2) = &H52 AndAlso bytes(3) = &H54) Then
                    Return Nothing
                End If

                Return TriFileParser.ParseTriFromBytes(bytes)
            End Function)
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
