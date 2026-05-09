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
    Public Function NormalizeMeshKey(rawPath As String) As String
        If String.IsNullOrWhiteSpace(rawPath) Then Return ""
        Dim normalized = rawPath.Replace("/", "\").Trim()
        If Not normalized.StartsWith("Meshes\", StringComparison.OrdinalIgnoreCase) Then
            normalized = "Meshes\" & normalized
        End If
        Return normalized.ToLowerInvariant()
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
        Dim candidate = meshKey.Substring(0, meshKey.Length - 4) & "_facebones.nif"
        If FilesDictionary_class.Dictionary.ContainsKey(candidate) Then Return candidate
        Return ""
    End Function
End Module
