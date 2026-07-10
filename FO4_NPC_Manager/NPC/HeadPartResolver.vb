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
    ''' RACE.HeadParts per gender: parsed into RACE_Data.MaleHeadPartFormIDs / FemaleHeadPartFormIDs.</summary>
    ''' <param name="parseRace">Optional cached RACE parser. <param name="parseHdpt">Optional cached
    ''' HDPT parser. Both fall back to direct <c>RecordParsers.Parse*</c> when Nothing (offline bake path).</param>
    Public Function MergeHeadPartsWithRaceDefaults(raceFormID As UInteger,
                                                   isFemale As Boolean,
                                                   npcHeadPartFormIDs As IReadOnlyList(Of UInteger),
                                                   pluginManager As PluginManager,
                                                   Optional parseRace As Func(Of PluginRecord, RACE_Data) = Nothing,
                                                   Optional parseHdpt As Func(Of PluginRecord, HDPT_Data) = Nothing) As List(Of UInteger)
        Dim safeNpcParts As IReadOnlyList(Of UInteger) = If(npcHeadPartFormIDs, CType(New List(Of UInteger)(), IReadOnlyList(Of UInteger)))
        If raceFormID = 0UI Then Return safeNpcParts.ToList()
        Dim raceRec = pluginManager.GetRecord(raceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Return safeNpcParts.ToList()
        End If
        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), RecordParsers.ParseRACE(raceRec, pluginManager))
        Dim raceDefaults = If(isFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)

        ' Build merged dict by PartType for main types (1..9).
        Dim mergedByType As New Dictionary(Of Integer, UInteger)
        Dim freestandingMisc As New List(Of UInteger)

        ' Step 1: seed with RACE defaults
        For Each defFID In raceDefaults
            Dim defRec = pluginManager.GetRecord(defFID)
            If defRec Is Nothing OrElse defRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = If(parseHdpt IsNot Nothing, parseHdpt(defRec), RecordParsers.ParseHDPT(defRec, pluginManager))
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(defFID)
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = defFID
            End If
        Next

        ' Step 2: override with NPC.PNAM (NPC wins per main type, or accumulates for misc)
        For Each npcFID In safeNpcParts
            Dim npcRec = pluginManager.GetRecord(npcFID)
            If npcRec Is Nothing OrElse npcRec.Header.Signature <> "HDPT" Then Continue For
            Dim hdpt = If(parseHdpt IsNot Nothing, parseHdpt(npcRec), RecordParsers.ParseHDPT(npcRec, pluginManager))
            If hdpt.PartType = 0 Then
                freestandingMisc.Add(npcFID)
            ElseIf hdpt.PartType >= 1 AndAlso hdpt.PartType <= 9 Then
                mergedByType(hdpt.PartType) = npcFID
            End If
        Next

        ' Step 3: build final list (main types sorted by type number + freestanding misc after)
        Dim finalList As New List(Of UInteger)
        For Each t In mergedByType.Keys.OrderBy(Function(k) k)
            finalList.Add(mergedByType(t))
        Next
        finalList.AddRange(freestandingMisc)

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
                                       Optional raceHasAnyHeadParts As Boolean = True,
                                       Optional parseHdpt As Func(Of PluginRecord, HDPT_Data) = Nothing) As Boolean
        If hdptFormID = 0UI OrElse pluginManager Is Nothing Then Return False
        Dim rec = pluginManager.GetRecord(hdptFormID)
        If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return False
        Dim hdpt = If(parseHdpt IsNot Nothing, parseHdpt(rec), RecordParsers.ParseHDPT(rec, pluginManager))
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

    ''' <summary>Whether a LooksMenu preset is race-compatible with the target NPC's race.
    ''' Compatibility is decided by HEAD PARTS ONLY: every preset.HeadPartFormIDs must pass
    ''' <see cref="IsHdptValidForRace"/>. Empty-set head parts are vacuously OK.
    '''
    ''' FaceTint layers do NOT gate compatibility (user rule, 2026-07-09): a tint whose Index
    ''' doesn't resolve against this race is NOT a reason to hide/drop the whole preset. Such a
    ''' tint is preserved verbatim in the NPC's FaceTintLayers (round-trips on Save) but is inert:
    ''' the compositor skips it (FaceTintInputBuilder: FindTintOption Nothing -> Continue) and the
    ''' Face editor hides its row (EditFace_Form.RefreshTintsList). So it is neither applied nor
    ''' editable, just carried. Head parts still gate because a wrong-race HDPT would visually
    ''' swap a whole mesh (hair/eyes) — a partial-apply the user does want hidden.
    '''
    ''' Used by <see cref="LooksmenuLoad_Form"/> to optionally hide presets the engine would
    ''' partially-apply against this NPC's race (LM itself doesn't enforce race).
    '''
    ''' <paramref name="ignoreFaceBaseHeadPart"/> (SSE path, user rule 2026-07-09): skip the base
    ''' HEAD part (PNAM PartType=1, "Face") from the gate. RaceMenu .jslot presets carry the preset
    ''' author's race-specific base head (e.g. FemaleHeadRedguard, whose Valid-Races FLST lists only
    ''' Redguard+Vampire), which legitimately fails a Nord NPC. But skee applies the preset's sculpt
    ''' over whatever race's own base head, so a mismatched base head must NOT drop the whole preset
    ''' — otherwise ~all cross-race presets vanish from the SSE browser. Hair/eyes/brows still gate.
    ''' FO4 leaves this False (unchanged behaviour).</summary>
    Public Function IsPresetCompatibleWithRace(preset As LooksmenuLoader.LooksmenuPreset,
                                               raceFormID As UInteger,
                                               isFemale As Boolean,
                                               pluginManager As PluginManager,
                                               race As RACE_Data,
                                               flstCache As Dictionary(Of UInteger, FLST_Data),
                                               raceDefaults As HashSet(Of UInteger),
                                               Optional ignoreFaceBaseHeadPart As Boolean = False) As Boolean
        If preset Is Nothing OrElse pluginManager Is Nothing Then Return False

        ' Diagnostic: when the logger is on, record the concrete reason a preset is judged
        ' race-incompatible (which HDPT). Gated + lazy so it's a no-op with logging off.
        ' presetName is captured once for the lambdas below.
        Dim presetName As String = IO.Path.GetFileName(preset.SourcePath)

        ' HeadPart compatibility — every declared HDPT must be valid for the target race.
        ' (Tints are deliberately NOT checked here — see the summary above.)
        If preset.HeadPartFormIDs IsNot Nothing Then
            For Each fid In preset.HeadPartFormIDs
                If fid = 0UI Then Continue For
                ' SSE: don't let the race-specific base HEAD (Face, PartType=1) gate the preset.
                If ignoreFaceBaseHeadPart Then
                    Dim hrec = pluginManager.GetRecord(fid)
                    If hrec IsNot Nothing AndAlso hrec.Header.Signature = "HDPT" Then
                        Dim hd = RecordParsers.ParseHDPT(hrec, pluginManager)
                        If hd IsNot Nothing AndAlso hd.PartType = 1 Then
                            Dim fidFace = fid
                            Logger.LogLazy(Function() $"[LMLoad] '{presetName}': skipping base-head (Face) HDPT 0x{fidFace:X8} from race-compat gate (SSE — skee applies the preset sculpt over the NPC's own base head).")
                            Continue For
                        End If
                    End If
                End If
                If Not IsHdptValidForRace(fid, raceFormID, isFemale, pluginManager, flstCache, raceDefaults) Then
                    Dim fidLocal = fid
                    Logger.LogLazy(Function() $"[LMLoad] DROP '{presetName}' as race-incompatible: HDPT 0x{fidLocal:X8} not valid for race 0x{raceFormID:X8} (gender={If(isFemale, "F", "M")}). HDPT's RACE/FLST does not list this race and it is not a race gender-default.")
                    Return False
                End If
            Next
        End If

        Logger.LogLazy(Function() $"[LMLoad] KEEP '{presetName}' as race-compatible for race 0x{raceFormID:X8} (gender={If(isFemale, "F", "M")}). (HeadParts OK; unresolved tint layers, if any, are preserved verbatim but not applied/editable.)")
        Return True
    End Function

    ''' <summary>Precompute the Misc(0) -> parent-effective-type promotion over a set of root
    ''' HDPTs: if a root HDPT is declared as a Misc(0) HNAM extra of another root whose type is
    ''' non-zero, it inherits that parent's type even when visited at top level. Order-independent.
    ''' Single source of truth shared by the render candidate walk (MainForm.CollectHeadPartCandidates)
    ''' and <see cref="EnumerateHdptChain"/>.</summary>
    Public Function BuildMiscToParentEffective(rootFormIDs As IEnumerable(Of UInteger),
                                               pluginManager As PluginManager,
                                               Optional parseHdpt As Func(Of PluginRecord, HDPT_Data) = Nothing) As Dictionary(Of UInteger, Integer)
        Dim result As New Dictionary(Of UInteger, Integer)
        If rootFormIDs Is Nothing OrElse pluginManager Is Nothing Then Return result
        Dim parsed As New Dictionary(Of UInteger, HDPT_Data)
        For Each fid In rootFormIDs
            If fid = 0UI OrElse parsed.ContainsKey(fid) Then Continue For
            Dim rec = pluginManager.GetRecord(fid)
            If rec IsNot Nothing AndAlso rec.Header.Signature = "HDPT" Then parsed(fid) = If(parseHdpt IsNot Nothing, parseHdpt(rec), RecordParsers.ParseHDPT(rec, pluginManager))
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

    ''' <summary>Cascade-remove the now-orphaned standalone Misc(0) children of a head part that was
    ''' REMOVED or REPLACED. A standalone Misc that lived in <paramref name="removedParentFid"/>'s
    ''' ExtraPartFormIDs (HNAM) becomes an orphan once its parent is gone: its effective type collapses
    ''' to Misc(0), no hair/beard palette applies, and it renders with the BGSM-default colour as a Misc
    ''' root. We drop those — EXCEPT any extra still claimed by another parent currently in
    ''' <paramref name="headParts"/> (this includes a replacement parent that shares the extra, so a
    ''' hairline declared by both the old and new hair survives as a live HNAM child). Non-Misc extras,
    ''' and Misc that were never in the removed parent's HNAM (independent addons: mouth shadow, AO/wet),
    ''' are left untouched.
    '''
    ''' Single source of truth shared by two callers, so a preset-load hair swap drops the old hairline
    ''' EXACTLY the way the manual editor does:
    '''   • <c>EditFace_Form.OnRemoveHeadPart</c> / <c>OnAddHeadPart</c> (manual remove / replace).
    '''   • <c>NpcOverrideSaver</c> Phase 1c (a filtered preset that replaced a main-type parent).
    ''' The saver caller MUST gate the call on "the parent's PartType was actually replaced by the
    ''' preset" — passing an unchanged parent here would (correctly) do nothing, but the gate keeps a raw
    ''' extra whose parent type is untouched (eyelashes on untouched eyes) from ever being considered.
    '''
    ''' <paramref name="resolveHdpt"/> maps a FormID to its parsed <see cref="HDPT_Data"/> (or Nothing);
    ''' callers pass their own cache (EditFace's _allHeadPartsByFid, the saver's parse cache).</summary>
    Public Sub CascadeRemoveOrphanedHnamMisc(headParts As List(Of UInteger),
                                             removedParentFid As UInteger,
                                             resolveHdpt As Func(Of UInteger, HDPT_Data))
        If headParts Is Nothing OrElse resolveHdpt Is Nothing Then Return
        Dim removedHdpt = resolveHdpt(removedParentFid)
        If removedHdpt Is Nothing Then Return
        If removedHdpt.PartType = 0 Then Return   ' a Misc has no HNAM children to orphan
        If removedHdpt.ExtraPartFormIDs Is Nothing OrElse removedHdpt.ExtraPartFormIDs.Count = 0 Then Return

        Dim extras As New HashSet(Of UInteger)(removedHdpt.ExtraPartFormIDs)
        ' If another head part still in the list declares one of these extras in its HNAM, it's a live
        ' HNAM child of that parent — keep it (covers a hairline shared by the old and new hair).
        Dim claimedByOtherParent As New HashSet(Of UInteger)
        For Each otherFid In headParts
            If otherFid = removedParentFid Then Continue For
            Dim otherHdpt = resolveHdpt(otherFid)
            If otherHdpt Is Nothing OrElse otherHdpt.ExtraPartFormIDs Is Nothing Then Continue For
            For Each ex In otherHdpt.ExtraPartFormIDs
                If extras.Contains(ex) Then claimedByOtherParent.Add(ex)
            Next
        Next
        For i = headParts.Count - 1 To 0 Step -1
            Dim fid = headParts(i)
            If Not extras.Contains(fid) Then Continue For
            If claimedByOtherParent.Contains(fid) Then Continue For
            Dim extraHdpt = resolveHdpt(fid)
            If extraHdpt Is Nothing OrElse extraHdpt.PartType <> 0 Then Continue For
            headParts.RemoveAt(i)
        Next
    End Sub

    ''' <summary>Given the raw NPC.PNAM head parts and a preset's head parts, return the raw standalone
    ''' Misc FormIDs that become ORPHANS because the preset replaced their parent — i.e. the set an
    ''' apply (Load LooksMenu/RaceMenu, Copy/Paste) must record in
    ''' <see cref="LooksmenuLoader.LooksmenuPreset.SuppressedRawHeadPartFormIDs"/> so the save-time raw
    ''' union drops them, dropping the old hairline exactly the way Edit Face does on a manual hair swap.
    '''
    ''' Merges raw ∪ preset the same way NpcOverrideSaver Phase 1c persists (one HDPT per main type,
    ''' preset wins; Misc(0) accumulated), then for every main-type parent the preset REPLACED with a
    ''' different HDPT (any type — hair, eyes, brows, …) collects that raw parent's orphaned Misc HNAM
    ''' children via the shared <see cref="CascadeRemoveOrphanedHnamMisc"/> (diffed before/after). This
    ''' makes Save agree with the render, which already rebuilds head parts as race-defaults + preset
    ''' (raw wiped) so a replaced parent's old raw extras never show. NOT the Cait-class lash regression:
    ''' that came from UNCONDITIONALLY filtering raw extras, whereas this only fires on an actual
    ''' replacement (a parent the preset left untouched suppresses nothing), and the cascade keeps any
    ''' extra still claimed by a surviving parent's HNAM (vanilla new eyes re-declare their lashes → kept).
    ''' <paramref name="resolveHdpt"/> maps FormID → parsed HDPT (or Nothing).</summary>
    Public Function ComputeReplacedParentOrphanMisc(rawParts As IEnumerable(Of UInteger),
                                                    presetParts As IEnumerable(Of UInteger),
                                                    resolveHdpt As Func(Of UInteger, HDPT_Data)) As HashSet(Of UInteger)
        Dim result As New HashSet(Of UInteger)
        If rawParts Is Nothing OrElse presetParts Is Nothing OrElse resolveHdpt Is Nothing Then Return result

        Dim rawByType As New Dictionary(Of Integer, UInteger)
        Dim mergedByType As New Dictionary(Of Integer, UInteger)
        Dim miscList As New List(Of UInteger)
        Dim seenMisc As New HashSet(Of UInteger)
        Dim classify = Sub(fid As UInteger, isRaw As Boolean)
                           If fid = 0UI Then Return
                           Dim hd = resolveHdpt(fid)
                           If hd Is Nothing Then Return
                           If hd.PartType = 0 Then
                               If seenMisc.Add(fid) Then miscList.Add(fid)
                           ElseIf hd.PartType >= 1 AndAlso hd.PartType <= 9 Then
                               mergedByType(hd.PartType) = fid   ' preset (classified 2nd) wins per type
                               If isRaw Then rawByType(hd.PartType) = fid
                           End If
                       End Sub
        For Each fid In rawParts : classify(fid, True) : Next
        For Each fid In presetParts : classify(fid, False) : Next

        ' Flat merged list the saver would persist (main types ordered, then Misc) — the context the
        ' orphan check runs against so a Misc still claimed by a surviving parent is NOT suppressed.
        Dim finalFlat As New List(Of UInteger)
        For Each t In mergedByType.Keys.OrderBy(Function(k) k) : finalFlat.Add(mergedByType(t)) : Next
        finalFlat.AddRange(miscList)

        For Each kv In rawByType
            Dim finalParent As UInteger = 0
            If mergedByType.TryGetValue(kv.Key, finalParent) AndAlso finalParent <> kv.Value Then
                ' This main-type parent was REPLACED by the preset with a different HDPT (hair→hair,
                ' eyes→eyes, …). Drop its orphaned standalone Misc children, matching what the render
                ' already shows: BuildShadow rebuilds head parts as race-defaults + preset (raw WIPED),
                ' so a replaced parent's old raw extras never render — suppressing them just makes Save
                ' agree with the preview. Two guards keep this from over-reaching:
                '   • REPLACEMENT-gated (finalParent <> raw): a parent the preset didn't touch (e.g. a
                '     plain re-save that keeps the NPC's own eyes) suppresses nothing — so this is NOT the
                '     Cait-class regression, which came from UNCONDITIONALLY filtering raw extras.
                '   • CascadeRemoveOrphanedHnamMisc keeps any extra still claimed by a surviving parent's
                '     HNAM (vanilla new eyes re-declare the lashes → they stay; the old HAIR's hairline is
                '     not claimed by the new hair → it goes). Diff before/after to collect exactly what dropped.
                Dim before As New HashSet(Of UInteger)(finalFlat)
                CascadeRemoveOrphanedHnamMisc(finalFlat, kv.Value, resolveHdpt)
                before.ExceptWith(finalFlat)
                result.UnionWith(before)
            End If
        Next
        Return result
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
                                                pluginManager As PluginManager,
                                                Optional parseHdpt As Func(Of PluginRecord, HDPT_Data) = Nothing) As IEnumerable(Of HdptChainEntry)
        If rootFormIDs Is Nothing OrElse pluginManager Is Nothing Then Return
        Dim roots = rootFormIDs.Where(Function(f) f <> 0UI).ToList()

        ' Shared precompute (also used by the render walk) so the effective-type rule lives once.
        Dim miscToParentEffective = BuildMiscToParentEffective(roots, pluginManager, parseHdpt)

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
            Dim hdpt = If(parseHdpt IsNot Nothing, parseHdpt(rec), RecordParsers.ParseHDPT(rec, pluginManager))
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
