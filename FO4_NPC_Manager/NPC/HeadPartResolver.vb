Imports System.Text
Imports FO4_Base_Library

''' <summary>
''' Head-part resolution helpers for NPC_Manager. App-specific (not promoted to
''' FO4_Base_Library) — only NPC_Manager merges NPC.PNAM with RACE.HeadParts;
''' Wardrobe_Manager has no NPC concept.
'''
''' Public Shared so multiple call sites inside NPC_Manager can share the same
''' implementation without duplication. Today's callers: MainForm render path +
''' FaceGenBuilder.
''' </summary>
Public Module HeadPartResolver

    ''' <summary>Merge NPC.PNAM head parts with RACE.HeadParts defaults per vanilla CK semantics.
    ''' Main types (1=Face, 2=Eyes, 3=Hair, 4=FacialHair, 5=Scar, 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=HeadRear):
    ''' NPC override wins; fall back to RACE default per type (gender-specific).
    ''' Type 0 Misc: should only appear as extras inside each main HDPT's HNAM; freestanding top-level
    ''' type=0 entries (rare/undocumented in vanilla) are preserved as additive to avoid data loss.
    ''' HDPT spec: wbDefinitionsFO4.pas:7373-7384.
    ''' RACE.HeadParts per gender: parsed into RACE_Data.MaleHeadPartFormIDs / FemaleHeadPartFormIDs.
    ''' Logs one [HEADPARTS-MERGE] summary line + per-type decision for traceability.</summary>
    Public Function MergeHeadPartsWithRaceDefaults(raceFormID As UInteger,
                                                   isFemale As Boolean,
                                                   npcHeadPartFormIDs As IReadOnlyList(Of UInteger),
                                                   pluginManager As PluginManager) As List(Of UInteger)
        Dim safeNpcParts As IReadOnlyList(Of UInteger) = If(npcHeadPartFormIDs, CType(New List(Of UInteger)(), IReadOnlyList(Of UInteger)))
        If raceFormID = 0UI Then Return safeNpcParts.ToList()
        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Return safeNpcParts.ToList()
        End If
        Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)
        Dim raceDefaults = If(isFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)

        ' Build merged dict by PartType for main types (1..9). Track provenance for logging.
        Dim mergedByType As New Dictionary(Of Integer, UInteger)
        Dim provenanceByType As New Dictionary(Of Integer, String) ' value format "RACE:{edid}" or "NPC:{edid}"
        Dim freestandingMisc As New List(Of UInteger)
        Dim miscProvenance As New List(Of String)

        ' Step 1: seed with RACE defaults
        For Each defFID In raceDefaults
            Dim defRec = pluginManager.GetRecord(defFID)
            If defRec Is Nothing OrElse defRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = RecordParsers.ParseHDPT(defRec, pluginManager)
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(defFID)
                miscProvenance.Add($"RACE:{hdpt.EditorID}")
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = defFID
                provenanceByType(hdpt.PartType) = $"RACE:{hdpt.EditorID}"
            End If
        Next

        ' Step 2: override with NPC.PNAM (NPC wins per main type, or accumulates for misc)
        For Each npcFID In safeNpcParts
            Dim npcRec = pluginManager.GetRecord(npcFID)
            If npcRec Is Nothing OrElse npcRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = RecordParsers.ParseHDPT(npcRec, pluginManager)
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(npcFID)
                miscProvenance.Add($"NPC:{hdpt.EditorID}")
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = npcFID
                provenanceByType(hdpt.PartType) = $"NPC:{hdpt.EditorID}"
            End If
        Next

        ' Step 3: build final list (main types sorted by type number + freestanding misc after)
        Dim finalList As New List(Of UInteger)
        For Each t In mergedByType.Keys.OrderBy(Function(k) k)
            finalList.Add(mergedByType(t))
        Next
        finalList.AddRange(freestandingMisc)

        ' Step 4: summary log — one line with per-type decision for traceability per NPC.
        Dim typeNames = New String() {"Misc", "Face", "Eyes", "Hair", "FacialHair", "Scar", "Eyebrows", "Meatcaps", "Teeth", "HeadRear"}
        Dim summary As New StringBuilder
        summary.Append($"  [HEADPARTS-MERGE] RACE '{race.EditorID}' {If(isFemale, "F", "M")} | NPC.PNAM={safeNpcParts.Count} race.defaults={raceDefaults.Count} → merged={finalList.Count}")
        NpcPreviewLog.Log(summary.ToString())
        For t = 1 To 9
            Dim prov As String = Nothing
            If provenanceByType.TryGetValue(t, prov) Then
                Dim from = If(prov.StartsWith("NPC:"), "NPC", "RACE-DEFAULT")
                Dim tLocal = t
                NpcPreviewLog.LogLazy(Function() $"    [HEADPARTS-MERGE] type={tLocal}/{typeNames(tLocal)}: from={from} {prov.Substring(prov.IndexOf(":"c) + 1)}")
            End If
        Next
        If freestandingMisc.Count > 0 Then
            NpcPreviewLog.LogLazy(Function() $"    [HEADPARTS-MERGE] freestanding-misc (type=0): {freestandingMisc.Count} entries [{String.Join(", ", miscProvenance)}]")
        End If
        Dim missedTypes = New List(Of String)
        For t = 1 To 9
            If Not provenanceByType.ContainsKey(t) Then missedTypes.Add(typeNames(t))
        Next
        If missedTypes.Count > 0 Then
            NpcPreviewLog.LogLazy(Function() $"    [HEADPARTS-MERGE] no-data-for-types: {String.Join(", ", missedTypes)} (neither RACE nor NPC declared; slot left empty)")
        End If

        Return finalList
    End Function

    ''' <summary>Whether <paramref name="hdptFormID"/> is valid for an NPC of <paramref name="raceFormID"/>.
    ''' Mirrors the engine's three pass conditions (any one suffices), the same logic
    ''' <see cref="HeadPartPicker_Form"/> uses to populate its candidate list:
    '''   a) HDPT.RNAM = 0 (no race restriction).
    '''   b) HDPT.RNAM points to a FLST whose ItemFormIDs contain raceFormID.
    '''   c) The NPC's RACE record names this HDPT as a gender-default in
    '''      Male/FemaleHeadPartFormIDs (RACE-declared defaults are valid by construction
    '''      even when the HDPT's own RNAM is inconsistent — some mods forget to update RNAM
    '''      after adding the HDPT to a new race's defaults).
    '''
    ''' <paramref name="flstCache"/> is shared across calls so a batch of HDPTs against the
    ''' same race only parses each FLST once. Pass an empty dict on first call and reuse it.</summary>
    Public Function IsHdptValidForRace(hdptFormID As UInteger,
                                       raceFormID As UInteger,
                                       isFemale As Boolean,
                                       pluginManager As PluginManager,
                                       flstCache As Dictionary(Of UInteger, FLST_Data),
                                       Optional raceDefaults As HashSet(Of UInteger) = Nothing) As Boolean
        If hdptFormID = 0UI OrElse pluginManager Is Nothing Then Return False
        Dim rec = pluginManager.GetRecord(hdptFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return False
        Dim hdpt = RecordParsers.ParseHDPT(rec, pluginManager)
        If hdpt Is Nothing Then Return False

        If hdpt.ValidRacesFormID = 0UI Then Return True

        Dim flst As FLST_Data = Nothing
        If Not flstCache.TryGetValue(hdpt.ValidRacesFormID, flst) Then
            Dim flstRec = pluginManager.GetRecord(hdpt.ValidRacesFormID)
            If flstRec IsNot Nothing AndAlso flstRec.Header.Signature = "FLST" Then
                flst = RecordParsers.ParseFLST(flstRec, pluginManager)
            End If
            flstCache(hdpt.ValidRacesFormID) = flst
        End If
        If flst IsNot Nothing AndAlso flst.ItemFormIDs.Contains(raceFormID) Then Return True

        If raceDefaults IsNot Nothing AndAlso raceDefaults.Contains(hdptFormID) Then Return True

        Return False
    End Function

    ''' <summary>Whether a LooksMenu preset is fully race-compatible with the target NPC's race.
    ''' Strict: every preset.HeadPartFormIDs must pass <see cref="IsHdptValidForRace"/>, AND every
    ''' preset.FaceTintLayer.Index must resolve to a tint option in RACE's gender-appropriate
    ''' TintTemplateGroups. Empty-set head parts and empty-set tints are vacuously OK.
    '''
    ''' Used by <see cref="LooksmenuLoad_Form"/> to optionally hide presets the engine would
    ''' partially-apply against this NPC's race (LM itself doesn't enforce race; the engine
    ''' just silently drops the incompatible HDPT/tint references).</summary>
    Public Function IsPresetCompatibleWithRace(preset As LooksmenuLoader.LooksmenuPreset,
                                               raceFormID As UInteger,
                                               isFemale As Boolean,
                                               pluginManager As PluginManager,
                                               race As RACE_Data,
                                               flstCache As Dictionary(Of UInteger, FLST_Data),
                                               raceDefaults As HashSet(Of UInteger)) As Boolean
        If preset Is Nothing OrElse pluginManager Is Nothing Then Return False

        ' HeadPart compatibility — every declared HDPT must be valid for the target race.
        If preset.HeadPartFormIDs IsNot Nothing Then
            For Each fid In preset.HeadPartFormIDs
                If fid = 0UI Then Continue For
                If Not IsHdptValidForRace(fid, raceFormID, isFemale, pluginManager, flstCache, raceDefaults) Then Return False
            Next
        End If

        ' FaceTint compatibility — every layer's Index must resolve in the target race's
        ' gender-appropriate TintTemplateGroups. NPC face tint Index is the TINI/Index
        ' subrecord that points into the RACE template tree.
        If race IsNot Nothing AndAlso preset.FaceTintLayers IsNot Nothing Then
            For Each layer In preset.FaceTintLayers
                If layer Is Nothing Then Continue For
                Dim opt = race.FindTintOption(layer.Index, isFemale)
                If opt Is Nothing Then Return False
            Next
        End If

        Return True
    End Function

    ''' <summary>BFS expansion of an HDPT chain via <c>HDPT.ExtraPartFormIDs</c> (HNAM extras).
    ''' Yields every reachable HDPT_Data starting from <paramref name="rootFormIDs"/>, including
    ''' the roots themselves. Cycles are guarded via a visited-set; non-HDPT records and
    ''' unparseable HDPTs are silently skipped (caller decides what to do with the rest).
    '''
    ''' Vanilla HDPTs use HNAM to attach technical sub-parts (Lashes/AO/Wet for eyes, Hairlines
    ''' for hair, MouthShadow/Teeth for face). Anything that wants to "render the same set of
    ''' shapes the engine renders" needs this expansion — the parent mesh alone is incomplete.
    '''
    ''' Callers inside NPC_Manager: <see cref="FaceGenBuilder.BuildAllowedShapeMap"/> (uses
    ''' the yielded HDPTs to build a name→data dict) and <see cref="HeadPartPicker_Form"/>
    ''' (loads each yielded HDPT's NIF + TXST into the preview). Both want the same enumeration
    ''' shape but disagree on what to do with each HDPT, so this iterator stops at "hand back
    ''' the records" rather than baking in a specific output shape.</summary>
    Public Iterator Function EnumerateHdptChain(rootFormIDs As IEnumerable(Of UInteger),
                                                pluginManager As PluginManager) As IEnumerable(Of HDPT_Data)
        If rootFormIDs Is Nothing OrElse pluginManager Is Nothing Then Return
        Dim visited As New HashSet(Of UInteger)
        Dim queue As New Queue(Of UInteger)
        For Each fid In rootFormIDs
            If fid <> 0UI Then queue.Enqueue(fid)
        Next
        While queue.Count > 0
            Dim fid = queue.Dequeue()
            If Not visited.Add(fid) Then Continue While
            Dim rec = pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue While
            Dim hdpt = RecordParsers.ParseHDPT(rec, pluginManager)
            If hdpt Is Nothing Then Continue While
            Yield hdpt
            If hdpt.ExtraPartFormIDs IsNot Nothing Then
                For Each extraFid In hdpt.ExtraPartFormIDs
                    If extraFid <> 0UI Then queue.Enqueue(extraFid)
                Next
            End If
        End While
    End Function
End Module
