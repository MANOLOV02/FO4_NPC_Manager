Imports FO4_Base_Library

''' <summary>
''' Reusable face-skeleton lookup. Vanilla FO4 ships face skeletons as siblings of the body
''' skeleton declared in RACE.ANAM:
'''   <c>&lt;body_basename&gt;_&lt;gender&gt;_faceBones.nif</c>   (gender-specific, preferred)
'''   <c>&lt;body_basename&gt;_faceBones.nif</c>             (generic fallback)
'''
''' App-specific (NPC_Manager) — both the render path (MainForm) and the offline bake
''' (FaceGenBuilder) consume this so the convention is centralized.
''' </summary>
Public Module FaceSkeletonResolver

    ''' <summary>Try to load the face skeleton bytes for the given NPC visual state. Returns
    ''' Nothing if the race has no body skeleton declared, or if neither candidate path exists
    ''' in the FilesDictionary. Logs the load (or the misses) under the [FACE-SKEL] tag for
    ''' parity with the existing render-path log surface.</summary>
    Public Function TryLoadFaceSkeletonBytes(raceFormID As UInteger,
                                              isFemale As Boolean,
                                              pluginManager As PluginManager) As Byte()
        If raceFormID = 0UI Then Return Nothing
        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)

        Dim bodySkel = If(isFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then bodySkel = If(isFemale, race.MaleSkeletonPath, race.FemaleSkeletonPath)
        If String.IsNullOrEmpty(bodySkel) Then Return Nothing

        ' Strip .nif, build candidate face skel paths
        Dim basePath = bodySkel
        If basePath.EndsWith(".nif", StringComparison.OrdinalIgnoreCase) Then
            basePath = basePath.Substring(0, basePath.Length - 4)
        End If

        Dim genderSuffix = If(isFemale, "_female", "_male")
        Dim candidates = {
            basePath & genderSuffix & "_faceBones.nif",
            basePath & "_faceBones.nif"
        }

        For Each raw In candidates
            Dim bytes = MeshPathHelpers.TryLoadMeshBytes(MeshPathHelpers.NormalizeMeshKey(raw))
            If bytes IsNot Nothing Then Return bytes
        Next

        Return Nothing
    End Function

End Module
