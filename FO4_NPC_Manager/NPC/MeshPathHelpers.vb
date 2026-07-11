Imports FO4_Base_Library

''' <summary>
''' Path normalization helpers for FilesDictionary lookups of mesh files.
''' App-specific (NPC_Manager) but reusable by both the render path (MainForm) and the
''' offline bake (FaceGenBuilder).
'''
''' HDPT.MeshPath / RACE.SkeletonPath / etc. are stored in records without the "Meshes\"
''' prefix and with mixed casing. FilesDictionary keys are lowercase + "meshes\"-prefixed.
''' These helpers centralize the conversion and the "_facebones.nif" sibling lookup so the
''' render and bake never drift on this convention.
''' </summary>
Public Module MeshPathHelpers

    ''' <summary>Normalize a raw mesh path for FilesDictionary lookup. Handles missing
    ''' "Meshes\" prefix, slash direction, and case. Empty/whitespace input returns "".</summary>
    ''' <summary>Normalize a raw mesh path for FilesDictionary lookup. Delegates to the shared
    ''' game-relative normalizer (same one behind <c>CorrectMaterialPath</c>/<c>CorrectTexturePath</c>)
    ''' so meshes, materials and textures all strip absolute build-machine prefixes identically —
    ''' e.g. Far Harbor's VRRetriever ARMA ships MOD2 as
    ''' "c:\projects\fallout4\build\pc\data\meshes\actors\dlc03\vrretriever\...", which must resolve
    ''' to "meshes\actors\dlc03\vrretriever\...". Empty/whitespace input returns "".</summary>
    Public Function NormalizeMeshKey(rawPath As String) As String
        Return FO4UnifiedMaterial_Class.CorrectMeshPath(rawPath)
    End Function

    ''' <summary>Look for a `_facebones.nif` sibling of the given normalized mesh key.
    ''' Vanilla FO4 ships every face mesh in two flavours:
    '''   <c>&lt;mesh&gt;.nif</c>           — skinned to body bones only.
    '''   <c>&lt;mesh&gt;_facebones.nif</c> — same mesh skinned to face bones (Jaw/Cheek/Eyelid/etc.).
    ''' The engine at runtime (and CK at bake time) prefers the _facebones variant when it
    ''' exists, because that's what makes FMRS bone deformation possible.
    ''' Returns the sibling key if present in FilesDictionary, empty otherwise.</summary>
    Public Function TryGetFaceBonesVariant(meshKey As String) As String
        If String.IsNullOrEmpty(meshKey) Then Return ""
        If Not meshKey.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) Then Return ""
        Dim candidate = String.Concat(meshKey.AsSpan(0, meshKey.Length - 4), "_facebones.nif")
        If FilesDictionary_class.Dictionary.ContainsKey(candidate) Then Return candidate
        Return ""
    End Function

    ''' <summary>Load the bytes for an already-normalized FilesDictionary key. Returns Nothing if the
    ''' key is empty/absent, the file is smaller than <paramref name="minBytes"/>, or reading it throws.
    ''' Centralizes the TryGetValue + GetBytes + size-check pattern shared by the skeleton resolvers
    ''' (FaceSkeletonResolver, BodyPartSkeletonResolver) and the TRI resolvers. Normalize the path first
    ''' with <see cref="NormalizeMeshKey"/>. <paramref name="minBytes"/> defaults to 1 (reject only empty
    ''' files); pass a larger value where the caller needs a minimum header size (e.g. 8 for a TRI magic).</summary>
    Public Function TryLoadMeshBytes(normalizedKey As String, Optional minBytes As Integer = 1) As Byte()
        If String.IsNullOrEmpty(normalizedKey) Then Return Nothing
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(normalizedKey, loc) Then Return Nothing
        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length < minBytes Then Return Nothing
            Return bytes
        Catch ex As Exception
            Logger.LogLazy(Function() $"[MESH-LOAD] '{normalizedKey}' read failed: {ex.GetType().Name}: {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>Read the "BODYTRI" NiStringExtraData path from a shape's NIF. Returns the raw stored
    ''' path ("" if absent — caller normalizes). <paramref name="includeShapeLevel"/>=True scans the
    ''' shape's own extra data first then the root NiNode (NpcMorphResolver's fallback); =False scans the
    ''' ROOT NiNode ONLY, mirroring LooksMenu's BodyMorphProcessor::Process (BodyMorphInterface.cpp:1356)
    ''' which BodySlideTriResolver replicates. Centralizes the scan loop that used to live duplicated in
    ''' both resolvers; the includeShapeLevel flag preserves their (intentionally different) scopes.</summary>
    Public Function ReadBodyTriPath(shape As IRenderableShape, includeShapeLevel As Boolean) As String
        If shape Is Nothing OrElse shape.NifContent Is Nothing OrElse shape.NifContent.Blocks Is Nothing Then Return ""
        Dim blocks = shape.NifContent.Blocks

        ' (a) Shape-level extra data — only when the caller opts in.
        If includeShapeLevel AndAlso shape.NifShape IsNot Nothing AndAlso shape.NifShape.ExtraDataList IsNot Nothing Then
            For Each edRef In shape.NifShape.ExtraDataList.References
                If edRef.Index < 0 OrElse edRef.Index >= blocks.Count Then Continue For
                Dim ed = TryCast(blocks(edRef.Index), NiflySharp.Blocks.NiStringExtraData)
                If ed IsNot Nothing AndAlso ed.Name IsNot Nothing AndAlso ed.Name.String = "BODYTRI" Then Return If(ed.StringData?.String, "")
            Next
        End If

        ' (b) Root NiNode extra data — the LM-faithful path (first NiNode in the block list).
        Dim rootNode As NiflySharp.Blocks.NiNode = Nothing
        For Each blk In blocks
            Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
            If nn IsNot Nothing Then rootNode = nn : Exit For
        Next
        If rootNode IsNot Nothing AndAlso rootNode.ExtraDataList IsNot Nothing Then
            For Each edRef In rootNode.ExtraDataList.References
                If edRef.Index < 0 OrElse edRef.Index >= blocks.Count Then Continue For
                Dim ed = TryCast(blocks(edRef.Index), NiflySharp.Blocks.NiStringExtraData)
                If ed IsNot Nothing AndAlso ed.Name IsNot Nothing AndAlso ed.Name.String = "BODYTRI" Then Return If(ed.StringData?.String, "")
            Next
        End If
        Return ""
    End Function

    ''' <summary>Scan the ENTIRE NIF block list for the first <c>NiStringExtraData</c> named "BODYTRI" and
    ''' return its stored path ("" if none). This is the SSE (RaceMenu / skee64) resolution: skee64's
    ''' <c>MorphCache::ApplyMorphs</c> (SKSE64Plugins skee64/BodyMorphInterface.cpp:690) does
    ''' <c>VisitObjects(rootNode, …)</c> — it walks the WHOLE subtree and takes the FIRST object carrying a
    ''' BODYTRI extra-data, NOT just the root. This is required because BodySlide/OutfitStudio attaches
    ''' BODYTRI to a NiShape (not the root) for Skyrim/SSE builds, while it uses the root only for
    ''' FO4/FO4VR/FO76 (BodySlideApp.cpp: AddTriData toRoot=true for FO4, per-shape for SSE). A BodySlide
    ''' NIF carries exactly ONE BODYTRI, so "first anywhere" == "the one BODYTRI" — traversal order is
    ''' immaterial. FO4 (F4EE BodyMorphProcessor::Process, root->GetExtraData) stays root-only via
    ''' <see cref="ReadBodyTriPath"/>; this method is the SSE-faithful counterpart.
    '''
    ''' <paramref name="foundOwner"/> receives a short description of where the BODYTRI was found
    ''' (block index + owning shape name if it sits on a shape, "&lt;root&gt;" if on the root node) for
    ''' diagnostics; "" when none was found.</summary>
    Public Function ScanBodyTriAnywhere(shape As IRenderableShape, ByRef foundOwner As String) As String
        foundOwner = ""
        If shape Is Nothing OrElse shape.NifContent Is Nothing OrElse shape.NifContent.Blocks Is Nothing Then Return ""
        Dim blocks = shape.NifContent.Blocks

        ' Find the first BODYTRI string-extra anywhere, and (for the diagnostic) which AVObject owns it.
        Dim edIndex As Integer = -1
        Dim bodyTri As String = ""
        For i = 0 To blocks.Count - 1
            Dim ed = TryCast(blocks(i), NiflySharp.Blocks.NiStringExtraData)
            If ed IsNot Nothing AndAlso ed.Name IsNot Nothing AndAlso ed.Name.String = "BODYTRI" Then
                edIndex = i
                bodyTri = If(ed.StringData?.String, "")
                Exit For
            End If
        Next
        If edIndex < 0 Then Return ""

        ' Owner classification (diagnostics only): does this extra-data ref hang off the root node or off a
        ' shape? Root → "<root>"; a shape → the shape's name. Uses GetShapes (INiShape exposes ExtraDataList
        ' + Name) so we avoid fragile concrete NiflySharp type refs. Gated behind Logger.Enabled.
        If Logger.Enabled Then
            Dim owner As String = "<orphan/unref>"
            ' Root NiNode first.
            Dim rootNode As NiflySharp.Blocks.NiNode = Nothing
            For Each blk In blocks
                Dim nn = TryCast(blk, NiflySharp.Blocks.NiNode)
                If nn IsNot Nothing Then rootNode = nn : Exit For
            Next
            If rootNode IsNot Nothing AndAlso rootNode.ExtraDataList IsNot Nothing Then
                For Each edRef In rootNode.ExtraDataList.References
                    If edRef.Index = edIndex Then owner = "<root>" : Exit For
                Next
            End If
            ' Then shapes.
            If owner = "<orphan/unref>" AndAlso shape.NifContent IsNot Nothing Then
                Try
                    For Each sh In shape.NifContent.GetShapes()
                        If sh Is Nothing OrElse sh.ExtraDataList Is Nothing Then Continue For
                        For Each edRef In sh.ExtraDataList.References
                            If edRef.Index = edIndex Then owner = $"shape '{If(sh.Name?.String, "")}'" : Exit For
                        Next
                        If owner <> "<orphan/unref>" Then Exit For
                    Next
                Catch
                End Try
            End If
            foundOwner = $"block#{edIndex} on {owner}"
        End If
        Return bodyTri
    End Function
End Module
