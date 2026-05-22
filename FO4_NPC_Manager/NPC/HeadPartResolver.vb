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
        For t = 1 To 9
            Dim prov As String = Nothing
            If provenanceByType.TryGetValue(t, prov) Then
                Dim from = If(prov.StartsWith("NPC:"), "NPC", "RACE-DEFAULT")
                Dim tLocal = t
            End If
        Next
        Dim missedTypes = New List(Of String)
        For t = 1 To 9
            If Not provenanceByType.ContainsKey(t) Then missedTypes.Add(typeNames(t))
        Next

        Return finalList
    End Function

    ''' <summary>Whether <paramref name="hdptFormID"/> is valid for an NPC of <paramref name="raceFormID"/>.
    ''' Pass conditions:
    '''   a) HDPT.RNAM = 0 (empty Valid Races) AND the target RACE declares head parts at all
    '''      (i.e. is a humanoid race). Most vanilla hair/face HDPTs use RNAM=0 yet render
    '''      correctly on HumanRace NPCs because HumanRace declares head parts.
    '''   b) HDPT.RNAM points to a FLST whose ItemFormIDs contain raceFormID.
    '''   c) The NPC's RACE record names this HDPT as a gender-default in
    '''      Male/FemaleHeadPartFormIDs.
    '''
    ''' Why path (a) requires the RACE to have head parts: vanilla EncRaiderDog01
    ''' (Fallout4.esm 0x000B2BF2) lists the human MaleMouthHumanoidDirtyTeethMissing in its
    ''' NPC.PNAM (RNAM=0) but the engine doesn't render human teeth on raider dogs in-game.
    ''' RaiderDogRace declares zero head parts — it's a non-humanoid race. So RNAM=0 is
    ''' NOT a universal pass; it requires the RACE to be one that uses head parts at all.
    '''
    ''' <paramref name="raceHasAnyHeadParts"/> caller-supplied: True if the target RACE
    ''' declares head parts in either Male or Female list. False = non-humanoid race
    ''' (dog/robot/creature) where RNAM=0 HDPTs are silently dropped by the engine.
    '''
    ''' <paramref name="flstCache"/> is shared across calls so a batch of HDPTs against the
    ''' same race only parses each FLST once. Pass an empty dict on first call and reuse it.</summary>
    Public Function IsHdptValidForRace(hdptFormID As UInteger,
                                       raceFormID As UInteger,
                                       isFemale As Boolean,
                                       pluginManager As PluginManager,
                                       flstCache As Dictionary(Of UInteger, FLST_Data),
                                       Optional raceDefaults As HashSet(Of UInteger) = Nothing,
                                       Optional raceHasAnyHeadParts As Boolean = True) As Boolean
        If hdptFormID = 0UI OrElse pluginManager Is Nothing Then Return False
        Dim rec = pluginManager.GetRecord(hdptFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return False
        Dim hdpt = RecordParsers.ParseHDPT(rec, pluginManager)
        If hdpt Is Nothing Then Return False

        ' Path (a): no race restriction declared. Pass only if the RACE itself uses head parts
        ' (humanoid). Non-humanoid races (dog/robot/creature) drop RNAM=0 HDPTs even though
        ' a buggy NPC.PNAM might list one — engine-faithful behavior.
        If hdpt.ValidRacesFormID = 0UI Then Return raceHasAnyHeadParts

        ' Path (b): RNAM points to a FLST and the FLST contains the target race.
        Dim flst As FLST_Data = Nothing
        If Not flstCache.TryGetValue(hdpt.ValidRacesFormID, flst) Then
            Dim flstRec = pluginManager.GetRecord(hdpt.ValidRacesFormID)
            If flstRec IsNot Nothing AndAlso flstRec.Header.Signature = "FLST" Then
                flst = RecordParsers.ParseFLST(flstRec, pluginManager)
            End If
            flstCache(hdpt.ValidRacesFormID) = flst
        End If
        If flst IsNot Nothing AndAlso flst.ItemFormIDs.Contains(raceFormID) Then Return True

        ' Path (c): the NPC's RACE record declares this HDPT as a gender-default.
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

    ''' <summary>Precompute the Misc(0) -> parent-effective-type promotion over a set of root
    ''' HDPTs: if a root HDPT is declared as a Misc(0) HNAM extra of another root whose type is
    ''' non-zero, it inherits that parent's type even when visited at top level. Order-independent.
    ''' Single source of truth shared by the render candidate walk (MainForm.CollectHeadPartCandidates)
    ''' and <see cref="EnumerateHdptChain"/>.</summary>
    Public Function BuildMiscToParentEffective(rootFormIDs As IEnumerable(Of UInteger),
                                               pluginManager As PluginManager) As Dictionary(Of UInteger, Integer)
        Dim result As New Dictionary(Of UInteger, Integer)
        If rootFormIDs Is Nothing OrElse pluginManager Is Nothing Then Return result
        Dim parsed As New Dictionary(Of UInteger, HDPT_Data)
        For Each fid In rootFormIDs
            If fid = 0UI OrElse parsed.ContainsKey(fid) Then Continue For
            Dim rec = pluginManager.GetRecord(fid)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "HDPT" Then parsed(fid) = RecordParsers.ParseHDPT(rec, pluginManager)
        Next
        For Each parentKv In parsed
            Dim parentEff = parentKv.Value.PartType
            If parentEff = 0 Then Continue For
            If parentKv.Value.ExtraPartFormIDs Is Nothing Then Continue For
            For Each extraFid In parentKv.Value.ExtraPartFormIDs
                Dim extraData As HDPT_Data = Nothing
                If Not parsed.TryGetValue(extraFid, extraData) Then Continue For
                If extraData.PartType <> 0 Then Continue For
                If Not result.ContainsKey(extraFid) Then result(extraFid) = parentEff
            Next
        Next
        Return result
    End Function

    ''' <summary>Per-node effective-type rule: the HDPT's own PartType, unless it is Misc(0) — then
    ''' inherit the parent's effective type (HNAM cascade, <paramref name="parentPartType"/> &gt;= 0),
    ''' or the precomputed top-level promotion (<paramref name="parentPartType"/> &lt; 0). Single
    ''' source of truth shared by the render walk and <see cref="EnumerateHdptChain"/>.</summary>
    Public Function ResolveEffectivePartType(ownPartType As Integer,
                                             parentPartType As Integer,
                                             hdptFormID As UInteger,
                                             miscToParentEffective As Dictionary(Of UInteger, Integer)) As Integer
        If ownPartType <> 0 Then Return ownPartType
        If parentPartType >= 0 Then Return parentPartType
        If miscToParentEffective IsNot Nothing Then
            Dim promoted As Integer = 0
            If miscToParentEffective.TryGetValue(hdptFormID, promoted) Then Return promoted
        End If
        Return ownPartType
    End Function

    ''' <summary>One yielded entry of <see cref="EnumerateHdptChain"/>: the parsed HDPT plus the
    ''' EFFECTIVE part type. Effective type = the HDPT's own PartType, except a Misc(0) sub-part
    ''' reached through a parent's HNAM inherits the parent's type (a hair Hairline, HDPT
    ''' PartType=Misc, becomes effective type Hair=3). This is the single source of truth for the
    ''' rule the render applies inline in <c>MainForm.CollectHeadPartCandidate</c>; callers that
    ''' need to color/treat a sub-part like its parent (e.g. hair palette on a hairline) must use
    ''' <see cref="EffectivePartType"/>, not <c>Hdpt.PartType</c>.</summary>
    Public Class HdptChainEntry
        Public Property Hdpt As HDPT_Data
        Public Property EffectivePartType As Integer
    End Class

    ''' <summary>BFS expansion of an HDPT chain via <c>HDPT.ExtraPartFormIDs</c> (HNAM extras).
    ''' Yields every reachable HDPT (as <see cref="HdptChainEntry"/> carrying the effective part
    ''' type) starting from <paramref name="rootFormIDs"/>, including the roots themselves. Cycles
    ''' are guarded via a visited-set; non-HDPT records and unparseable HDPTs are silently skipped.
    '''
    ''' Vanilla HDPTs use HNAM to attach technical sub-parts (Lashes/AO/Wet for eyes, Hairlines
    ''' for hair, MouthShadow/Teeth for face). Anything that wants to "render the same set of
    ''' shapes the engine renders" needs this expansion — the parent mesh alone is incomplete.
    '''
    ''' Effective-type rule (mirrors the render walk): a Misc(0) sub-part inherits the effective
    ''' type of the parent that reached it through HNAM. A top-level Misc that is ALSO declared as
    ''' another root's HNAM extra is promoted to that parent's type (precomputed below) so the
    ''' result is order-independent — same as MainForm's miscToParentEffective.
    '''
    ''' Callers inside NPC_Manager: <see cref="FaceGenBuilder.BuildAllowedShapeMap"/> and
    ''' <see cref="HeadPartPicker_Form"/>.</summary>
    Public Iterator Function EnumerateHdptChain(rootFormIDs As IEnumerable(Of UInteger),
                                                pluginManager As PluginManager) As IEnumerable(Of HdptChainEntry)
        If rootFormIDs Is Nothing OrElse pluginManager Is Nothing Then Return
        Dim roots = rootFormIDs.Where(Function(f) f <> 0UI).ToList()

        ' Shared precompute (also used by the render walk) so the effective-type rule lives once.
        Dim miscToParentEffective = BuildMiscToParentEffective(roots, pluginManager)

        Dim visited As New HashSet(Of UInteger)
        ' Queue of (FormID, parent effective type). Roots carry parentEff = -1.
        Dim queue As New Queue(Of (Fid As UInteger, ParentEff As Integer))
        For Each fid In roots
            queue.Enqueue((fid, -1))
        Next
        While queue.Count > 0
            Dim item = queue.Dequeue()
            Dim fid = item.Fid
            If Not visited.Add(fid) Then Continue While
            Dim rec = pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue While
            Dim hdpt = RecordParsers.ParseHDPT(rec, pluginManager)
            If hdpt Is Nothing Then Continue While

            ' Effective type via the shared rule (same one the render walk uses).
            Dim effectiveType = ResolveEffectivePartType(hdpt.PartType, item.ParentEff, fid, miscToParentEffective)

            Yield New HdptChainEntry With {.Hdpt = hdpt, .EffectivePartType = effectiveType}

            ' Children inherit this node's effective type (so a hairline under hair stays Hair).
            Dim childParentEff = If(effectiveType <> 0, effectiveType, item.ParentEff)
            If hdpt.ExtraPartFormIDs IsNot Nothing Then
                For Each extraFid In hdpt.ExtraPartFormIDs
                    If extraFid <> 0UI Then queue.Enqueue((extraFid, childParentEff))
                Next
            End If
        End While
    End Function
End Module
