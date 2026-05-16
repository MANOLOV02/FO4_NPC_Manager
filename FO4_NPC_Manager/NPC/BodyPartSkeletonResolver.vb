Imports FO4_Base_Library

''' <summary>
''' Resolves the per-race extra-skeleton path declared in the BPTD record (Body Part Data)
''' that the RACE points to via GNAM.
'''
''' Engine schema (xEdit wbDefinitionsFO4.pas:8043-8144 + 11594):
'''   RACE.GNAM → BPTD FormID
'''   BPTD.MODL → string path to a NIF that holds the FULL bone hierarchy for this race
'''
''' For humans BPTD.MODL == RACE.ANAM (both point to Character\skeleton.nif), so merging is
''' a no-op. For robots BPTD.MODL is the SkeletonRef.nif (or cross-folder skeleton ref like
''' DLC01HandyCreateABotRace → DLC01\Robot\skeletonRefHandyDLC01.nif) — that's where the
''' actual rig lives, while RACE.ANAM is a 5-bone stub.
'''
''' This resolver replaces the legacy MergeRobotExtendedSkeletonsIfRobot heuristic which
''' enumerated `skeleton*.nif` siblings under the RACE.ANAM folder gated by SkeletonRef.nif
''' presence. The new path uses the engine's own pointer (BPTD.MODL) so cross-folder cases
''' work without hardcoded crossover maps.
''' </summary>
Public Module BodyPartSkeletonResolver

    ''' <summary>Resolve and load the BPTD.MODL skeleton bytes for the race the NPC belongs
    ''' to. Returns Nothing when the race has no GNAM, the BPTD has no MODL, or the file is
    ''' missing from the FilesDictionary. Logs under [BPTD-SKEL].</summary>
    Public Function TryLoadBptdSkeletonBytes(raceFormID As UInteger,
                                              pluginManager As PluginManager) As Byte()
        If raceFormID = 0UI OrElse pluginManager Is Nothing Then Return Nothing

        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)
        If race.BodyPartDataFormID = 0UI Then
            Return Nothing
        End If

        Dim bptdRec = pluginManager.GetRecord(race.BodyPartDataFormID)
        If bptdRec Is Nothing OrElse bptdRec.Header.Signature <> "BPTD" Then
            Return Nothing
        End If

        Dim bptd = ActorRecordParsers.ParseBPTD(bptdRec, pluginManager)
        If String.IsNullOrEmpty(bptd.ModelPath) Then
            Return Nothing
        End If

        Dim normalized = MeshPathHelpers.NormalizeMeshKey(bptd.ModelPath)
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(normalized, loc) Then
            Return Nothing
        End If

        Try
            Dim bytes = loc.GetBytes()
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                Return Nothing
            End If
            Return bytes
        Catch ex As Exception
            Return Nothing
        End Try
    End Function

End Module
