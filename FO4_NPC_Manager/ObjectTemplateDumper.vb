Imports System.IO
Imports System.Text
Imports FO4_Base_Library

''' <summary>
''' One-shot exhaustive dump of every Object Template / OMOD reachable from the loaded NPCs.
''' Walks the engine's full reference chain so we have a single canonical document of every
''' (PropertyIndex, ValueType, FunctionType) and every Include/Property layout vanilla actually
''' uses — informs every render-side decision about OBTS/OMOD support.
'''
''' Reference chain walked:
'''
'''   NPC_
'''     ├── ObjectTemplate (OBTE/OBTS combinations) — direct
'''     ├── DefaultOutfit (DOFT) → OTFT.ItemFormIDs → each entry (ARMO/LVLI/anything)
'''     │     LVLI: recurse all entries (no random pick — we want the universe of possibilities)
'''     └── Inventory (CNTO array) → each ItemFormID (ARMO/LVLI/MISC/AMMO/anything)
'''
'''   ARMO (reached from any of the above)
'''     ├── Combinations (OBTE/OBTS) — same parser as NPC_.ObjectTemplate
'''     ├── ArmorAddons (INDX → ARMA FormID) — listed for context, not expanded into render path
'''     └── TemplateArmor (TNAM) — logged as link, NOT recursively expanded (that would multiply
'''         the dump 5x without informing the OBTS analysis)
'''
'''   OMOD (reached from any OBTS Include or from an OMOD's own sub-Includes)
'''     ├── All record header + DATA fields (RecordFlags, Description, Filter, LooseModFormID,
'''     │   Priority, FormType, MaxRank, LevelTierScaledOffset, AttachPoint, AttachParentSlots,
'''     │   LegacyItems, MNAM/FNAM keywords, UnknownBool1/2)
'''     ├── Includes (sub-OMODs, with MinimumLevel + Optional + DontUseAll)
'''     └── Properties (24-byte each, decoded per ValueType + FunctionType)
'''     Each unique OMOD printed once in Section 2; recursion guarded by visited-set.
'''
''' Output sections:
'''   1. Combinations grouped by Race, deduplicated by structural signature.
'''   2. Cross-NPC index of every unique OMOD reached.
'''   3. Property × FunctionType × ValueType matrix.
'''   4. Cross-NPC index of every unique ARMO reached from DOFT/Inventory (Combinations only;
'''      ARMA terminals listed by FormID for completeness without expanding their meshes).
''' </summary>
Friend Module ObjectTemplateDumper

    Friend Sub Run(allNpcs As IEnumerable(Of NPC_Data),
                   pluginManager As PluginManager,
                   outputPath As String,
                   Optional progress As IProgress(Of String) = Nothing)
        If allNpcs Is Nothing OrElse pluginManager Is Nothing Then Return

        Report(progress, "Dump: scanning loaded NPCs...")

        Dim sb As New StringBuilder(2 * 1024 * 1024)
        sb.AppendLine("=== FO4 OBJECT TEMPLATE EXHAUSTIVE DUMP (v2 — full reference chain) ===")
        sb.AppendLine($"generated:  {DateTime.Now:yyyy-MM-dd HH:mm:ss}")
        sb.AppendLine("source:     all loaded NPCs — walks NPC_.OBTE + DOFT(OTFT/LVLI) + Inventory(CNTO)")
        sb.AppendLine("            recursion: OMOD.Includes (guarded by visited-set)")
        sb.AppendLine("            no random sampling: every reachable ARMO is processed")
        sb.AppendLine()

        Dim allNpcsList = allNpcs.Where(Function(n) n IsNot Nothing).ToList()
        Dim npcsWithObte = allNpcsList.Where(Function(n) n.HasObjectTemplate AndAlso n.ObjectTemplateCombinations.Count > 0).ToList()

        ' Discovered records (visited-sets + parsed cache)
        Dim omodCache As New Dictionary(Of UInteger, OMOD_Data)
        Dim omodReferenceCount As New Dictionary(Of UInteger, Integer) ' OMOD FID -> total references seen across the whole walk
        Dim armoCache As New Dictionary(Of UInteger, ARMO_Data)
        Dim armoReferenceCount As New Dictionary(Of UInteger, Integer) ' ARMO FID reached from DOFT/Inventory
        Dim visitedOtft As New HashSet(Of UInteger)
        Dim visitedLvli As New HashSet(Of UInteger)

        ' Walk DOFT + Inventory across all NPCs to populate ARMO/OMOD discovery sets.
        Report(progress, $"Dump: walking DOFT + Inventory across {allNpcsList.Count} NPCs...")
        Dim npcCounter As Integer = 0
        For Each npc In allNpcsList
            npcCounter += 1
            If npcCounter Mod 500 = 0 Then
                Report(progress, $"Dump: walking NPCs {npcCounter}/{allNpcsList.Count}")
            End If
            ' DOFT
            If npc.DefaultOutfitFormID <> 0UI Then
                WalkOutfitOrItem(npc.DefaultOutfitFormID, pluginManager, armoCache, armoReferenceCount,
                                 omodCache, omodReferenceCount, visitedOtft, visitedLvli)
            End If
            ' Inventory
            If npc.Inventory IsNot Nothing Then
                For Each inv In npc.Inventory
                    If inv.ItemFormID <> 0UI Then
                        WalkOutfitOrItem(inv.ItemFormID, pluginManager, armoCache, armoReferenceCount,
                                         omodCache, omodReferenceCount, visitedOtft, visitedLvli)
                    End If
                Next
            End If
            ' NPC_.OBTE Includes — also count toward OMOD references
            For Each ch In npc.ObjectTemplateCombinations
                If ch.Combination Is Nothing Then Continue For
                For Each inc In ch.Combination.Includes
                    If inc.ModFormID <> 0UI Then
                        DiscoverOmodRecursive(inc.ModFormID, pluginManager, omodCache, omodReferenceCount)
                    End If
                Next
            Next
        Next

        sb.AppendLine($"NPCs scanned (total):                           {allNpcsList.Count}")
        sb.AppendLine($"NPCs with HasObjectTemplate=True:               {npcsWithObte.Count}")
        sb.AppendLine($"Unique ARMOs reached from DOFT/Inventory:       {armoReferenceCount.Count}")
        sb.AppendLine($"Unique OMODs reached (any path, recursive):     {omodReferenceCount.Count}")
        sb.AppendLine($"OTFTs visited:                                  {visitedOtft.Count}")
        sb.AppendLine($"LVLIs visited:                                  {visitedLvli.Count}")
        sb.AppendLine()

        ' ──────────────── SECTION 1 ────────────────
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine("SECTION 1 — NPC_.ObjectTemplate Combinations grouped by Race")
        sb.AppendLine("(dedup by structural signature within race)")
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine()

        Dim byRace = npcsWithObte.GroupBy(Function(n) n.RaceFormID).
                                   OrderBy(Function(g) DescribeRecord(g.Key, pluginManager)).ToList()
        Dim raceCounter As Integer = 0
        For Each raceGroup In byRace
            raceCounter += 1
            Report(progress, $"Dump: SECTION 1 — race {raceCounter}/{byRace.Count} ({raceGroup.Count()} NPCs)")
            Dim raceFid = raceGroup.Key
            Dim raceLabel = DescribeRecord(raceFid, pluginManager)
            sb.AppendLine($"[RACE {raceLabel}]  ({raceGroup.Count()} NPCs with OBTE)")

            Dim sigBucket As New Dictionary(Of String, SignatureBucket)
            For Each npc In raceGroup
                For Each comboHdr In npc.ObjectTemplateCombinations
                    If comboHdr.Combination Is Nothing Then Continue For
                    Dim sig = ComputeCombinationSignature(comboHdr)
                    Dim bucket As SignatureBucket = Nothing
                    If Not sigBucket.TryGetValue(sig, bucket) Then
                        bucket = New SignatureBucket With {.SampleHeader = comboHdr}
                        sigBucket(sig) = bucket
                    End If
                    bucket.OccurrenceCount += 1
                    If bucket.SampleNpcFormIDs.Count < 5 AndAlso Not bucket.SampleNpcFormIDs.Contains(npc.FormID) Then
                        bucket.SampleNpcFormIDs.Add(npc.FormID)
                    End If
                Next
            Next

            Dim sigIndex As Integer = 0
            For Each kvp In sigBucket.OrderByDescending(Function(p) p.Value.OccurrenceCount)
                sigIndex += 1
                Dim bucket = kvp.Value
                Dim hdr = bucket.SampleHeader
                Dim cmb = hdr.Combination
                sb.AppendLine($"  ──── Signature #{sigIndex}  (seen in {bucket.OccurrenceCount} combinations across {bucket.SampleNpcFormIDs.Count}+ sampled NPCs) ────")
                AppendCombination(sb, hdr, "    ", "NPC_", pluginManager)
                sb.AppendLine($"    Sample NPCs (up to 5):")
                For Each npcFid In bucket.SampleNpcFormIDs
                    sb.AppendLine($"      {DescribeRecord(npcFid, pluginManager)}")
                Next
                sb.AppendLine()
            Next
            sb.AppendLine()
        Next

        ' ──────────────── SECTION 2 ────────────────
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine("SECTION 2 — Unique OMODs reached (any path, recursive)")
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine()
        sb.AppendLine($"Total unique OMODs:  {omodReferenceCount.Count}")
        sb.AppendLine()

        Dim orderedOmods = omodReferenceCount.OrderByDescending(Function(p) p.Value).ThenBy(Function(p) p.Key).ToList()
        Dim omodCounter As Integer = 0
        For Each pair In orderedOmods
            omodCounter += 1
            If omodCounter Mod 100 = 0 OrElse omodCounter = orderedOmods.Count Then
                Report(progress, $"Dump: SECTION 2 — OMOD {omodCounter}/{orderedOmods.Count}")
            End If
            Dim omodFid = pair.Key
            Dim refCount = pair.Value
            Dim omod = ResolveOmod(omodFid, pluginManager, omodCache)
            If omod Is Nothing Then
                sb.AppendLine($"[OMOD {omodFid:X8}]  (referenced {refCount} times) — RECORD NOT FOUND")
                sb.AppendLine()
                Continue For
            End If
            AppendFullOmod(sb, omod, refCount, pluginManager)
        Next

        ' ──────────────── SECTION 3 ────────────────
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine("SECTION 3 — Property × FunctionType × ValueType matrix")
        sb.AppendLine("(occurrences across ALL Properties seen anywhere in this dump)")
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine()

        Report(progress, "Dump: SECTION 3 — building property matrix...")
        Dim matrix As New Dictionary(Of String, Integer)

        ' NPC_.OBTE direct combination Properties (FormType=NPC_)
        For Each npc In npcsWithObte
            For Each ch In npc.ObjectTemplateCombinations
                If ch.Combination Is Nothing Then Continue For
                For Each prop In ch.Combination.Properties
                    AccumulateMatrix(matrix, "NPC_", prop)
                Next
            Next
        Next
        ' ARMO.OBTE direct combination Properties (FormType=ARMO)
        For Each kv In armoCache
            Dim a = kv.Value
            If a Is Nothing Then Continue For
            For Each c In a.Combinations
                For Each prop In c.Properties
                    AccumulateMatrix(matrix, "ARMO", prop)
                Next
            Next
        Next
        ' OMOD direct Properties (FormType taken from the OMOD itself)
        For Each pair In orderedOmods
            Dim omod = ResolveOmod(pair.Key, pluginManager, omodCache)
            If omod Is Nothing Then Continue For
            For Each prop In omod.Properties
                AccumulateMatrix(matrix, omod.FormTypeSignature, prop)
            Next
        Next

        For Each row In matrix.OrderBy(Function(p) p.Key)
            sb.AppendLine($"  {row.Key}  →  {row.Value} occurrences")
        Next
        sb.AppendLine()

        ' ──────────────── SECTION 4 ────────────────
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine("SECTION 4 — Unique ARMOs reached from DOFT/Inventory")
        sb.AppendLine("(Combinations + addon list; ARMA terminals listed by FormID, meshes not expanded)")
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine()
        sb.AppendLine($"Total unique ARMOs:  {armoReferenceCount.Count}")
        sb.AppendLine()

        Dim orderedArmos = armoReferenceCount.OrderByDescending(Function(p) p.Value).ThenBy(Function(p) p.Key).ToList()
        Dim armoCounter As Integer = 0
        For Each pair In orderedArmos
            armoCounter += 1
            If armoCounter Mod 100 = 0 OrElse armoCounter = orderedArmos.Count Then
                Report(progress, $"Dump: SECTION 4 — ARMO {armoCounter}/{orderedArmos.Count}")
            End If
            Dim armoFid = pair.Key
            Dim refCount = pair.Value
            Dim armo As ARMO_Data = Nothing
            armoCache.TryGetValue(armoFid, armo)
            If armo Is Nothing Then
                sb.AppendLine($"[ARMO {armoFid:X8}]  (referenced from {refCount} DOFT/Inventory paths) — RECORD NOT FOUND")
                sb.AppendLine()
                Continue For
            End If
            AppendFullArmo(sb, armo, refCount, pluginManager)
        Next

        ' ──────────────── SECTION 5 ────────────────
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine("SECTION 5 — Sources of MeshCandidate.SlotMask = 0")
        sb.AppendLine("(why does it matter: SelectWinningCandidates routes Skin SlotMask=0 through the")
        sb.AppendLine(" SKIN-PASS bypass; everything else with SlotMask=0 went through the slotless")
        sb.AppendLine(" headpart pass. A regression on 2026-05-10 had robot Skin chunks (SlotMask=0)")
        sb.AppendLine(" entering BOTH passes → 12 candidates → 24 winners. Validating that vanilla")
        sb.AppendLine(" SlotMask=0 emitters really come ONLY from the NPC robot path lets us trust the")
        sb.AppendLine(" `Kind <> Skin` filter in pasada 2 instead of adding generic dedup.)")
        sb.AppendLine("────────────────────────────────────────────────────────")
        sb.AppendLine()

        Report(progress, "Dump: SECTION 5 — scanning ARMO/ARMA SlotMask=0 sources...")

        ' Source A — Outfit/Skin path (CollectArmoCandidates):
        '   line 6877: candidate.SlotMask = If(arma.SlotMask <> 0UI, arma.SlotMask, armo.SlotMask)
        '   Both ARMA.SlotMask=0 AND ARMO.SlotMask=0 → emitted candidate has SlotMask=0.
        '
        ' For every reachable ARMO walk its addon ARMAs and report any pair (ARMO, ARMA)
        ' that produces SlotMask=0 efectivo. This is the engine-faithful list of candidates
        ' that would land in SKIN-PASS via the humanoid path with SlotMask=0.
        Dim sourceAHits As New List(Of String)
        For Each kv In armoCache
            Dim a = kv.Value
            If a Is Nothing Then Continue For
            For Each entry In a.ArmorAddons
                Dim armaRec = pluginManager.GetRecord(entry.ArmaFormID)
                If armaRec Is Nothing OrElse armaRec.Header.Signature <> "ARMA" Then Continue For
                Dim arma = RecordParsers.ParseARMA(armaRec, pluginManager)
                Dim effectiveSlot As UInteger = If(arma.SlotMask <> 0UI, arma.SlotMask, a.SlotMask)
                If effectiveSlot = 0UI Then
                    sourceAHits.Add($"  ARMO={DescribeRecord(a.FormID, pluginManager)} race={DescribeRecord(a.RaceFormID, pluginManager)} ARMA={DescribeRecord(entry.ArmaFormID, pluginManager)} (ARMO.SlotMask=0x{a.SlotMask:X8} ARMA.SlotMask=0x{arma.SlotMask:X8})")
                End If
            Next
        Next

        sb.AppendLine($"Source A — Outfit/Skin path (CollectArmoCandidates) — total ARMA shapes that would emit SlotMask=0:  {sourceAHits.Count}")
        If sourceAHits.Count = 0 Then
            sb.AppendLine("  (none — no vanilla ARMO/ARMA pair reachable from DOFT/Inventory yields SlotMask=0)")
        Else
            For Each line In sourceAHits
                sb.AppendLine(line)
            Next
        End If
        sb.AppendLine()

        ' Source B — NPC robot path (CollectRobotChunkCandidates):
        '   Hardcoded `.SlotMask = 0UI` per chunk emit (MainForm.vb:7050 area).
        '   For every NPC with HasObjectTemplate, count how many chunks (OMODs with ModelPath != "")
        '   the canonical resolver would emit. That's exactly how many SlotMask=0 candidates the
        '   robot path produces.
        Dim sourceBHits As New Dictionary(Of UInteger, Integer) ' npcFormID → chunk count
        Dim totalRobotChunks As Integer = 0
        For Each npc In npcsWithObte
            ' Re-run the resolver as the runtime would (empty ctxKeywords for NPC robots).
            Dim resolution = ObjectTemplateResolver.ResolveNpcCombinations(npc, New List(Of UInteger), pluginManager)
            Dim chunks As Integer = 0
            For Each omod In resolution.IncludedOmods
                If omod IsNot Nothing AndAlso Not String.IsNullOrEmpty(omod.ModelPath) Then chunks += 1
            Next
            If chunks > 0 Then
                sourceBHits(npc.FormID) = chunks
                totalRobotChunks += chunks
            End If
        Next

        sb.AppendLine($"Source B — NPC robot path (CollectRobotChunkCandidates) — total NPC robot chunks emitted with SlotMask=0:  {totalRobotChunks} (from {sourceBHits.Count} NPCs)")
        sb.AppendLine("  Top NPCs by chunk count:")
        Dim topNpcs = sourceBHits.OrderByDescending(Function(p) p.Value).Take(15).ToList()
        For Each pair In topNpcs
            sb.AppendLine($"    {pair.Value,3} chunks  {DescribeRecord(pair.Key, pluginManager)}")
        Next
        sb.AppendLine()

        ' Source C — HeadParts (CollectHeadPartCandidates):
        '   Always emits SlotMask=0 by design (head parts have no biped slot bits). Routed via
        '   pasada 2 (slotless), which is INTENTIONAL — the `Kind <> Skin` filter we apply
        '   precisely preserves this behavior. Listed here for completeness only.
        sb.AppendLine("Source C — HeadPart path (CollectHeadPartCandidates) — every HDPT emits SlotMask=0 by design.")
        sb.AppendLine("  (Not enumerated here — vanilla has hundreds of HDPTs and they're not the regression source.)")
        sb.AppendLine()

        sb.AppendLine("CONCLUSION:")
        If sourceAHits.Count = 0 Then
            sb.AppendLine("  Source A is empty → no humanoid Outfit/Skin candidate emits SlotMask=0 in vanilla.")
            sb.AppendLine("  Source B is the SOLE emitter of (Kind=Skin AND SlotMask=0) candidates.")
            sb.AppendLine("  ⇒ The `Kind <> Skin` filter in pasada 2 is engine-correct; no humanoid case affected.")
        Else
            sb.AppendLine("  Source A is NOT empty — there are humanoid ARMA shapes that emit SlotMask=0.")
            sb.AppendLine("  ⇒ The `Kind <> Skin` filter would also exclude those from pasada 2. Verify each")
            sb.AppendLine("    listed case to decide whether they need the slotless pass or already get covered")
            sb.AppendLine("    by SKIN-PASS (Outfit candidates wouldn't — they'd lose render).")
        End If
        sb.AppendLine()

        sb.AppendLine("=== END OF DUMP ===")

        Report(progress, $"Dump: writing {sb.Length:N0} chars to {outputPath}...")
        File.WriteAllText(outputPath, sb.ToString(), New UTF8Encoding(encoderShouldEmitUTF8Identifier:=False))
        Report(progress, $"Dump: done — {npcsWithObte.Count} OBTE-NPCs, {armoReferenceCount.Count} ARMOs, {omodReferenceCount.Count} OMODs")
    End Sub

    ' ───────────────────────────── Walk helpers ─────────────────────────────

    ''' <summary>Resolve a FormID that may be ARMO / OTFT / LVLI / anything else, dispatching
    ''' to the appropriate walker. Unknown record types are ignored silently (we only care
    ''' about the chain that leads to ARMO/OMOD).</summary>
    Private Sub WalkOutfitOrItem(formID As UInteger,
                                 pm As PluginManager,
                                 armoCache As Dictionary(Of UInteger, ARMO_Data),
                                 armoRefs As Dictionary(Of UInteger, Integer),
                                 omodCache As Dictionary(Of UInteger, OMOD_Data),
                                 omodRefs As Dictionary(Of UInteger, Integer),
                                 visitedOtft As HashSet(Of UInteger),
                                 visitedLvli As HashSet(Of UInteger))
        If formID = 0UI Then Return
        Dim rec = pm.GetRecord(formID)
        If rec Is Nothing Then Return
        Select Case rec.Header.Signature
            Case "OTFT"
                If visitedOtft.Add(formID) Then
                    Dim otft = RecordParsers.ParseOTFT(rec, pm)
                    For Each itemFid In otft.ItemFormIDs
                        WalkOutfitOrItem(itemFid, pm, armoCache, armoRefs, omodCache, omodRefs, visitedOtft, visitedLvli)
                    Next
                End If
            Case "LVLI"
                If visitedLvli.Add(formID) Then
                    Dim lvli = RecordParsers.ParseLVLI(rec, pm)
                    For Each entry In lvli.Entries
                        WalkOutfitOrItem(entry.FormID, pm, armoCache, armoRefs, omodCache, omodRefs, visitedOtft, visitedLvli)
                    Next
                End If
            Case "ARMO"
                ' Bump ref count even if we've cached it already — we want occurrence frequency.
                Dim cur As Integer = 0
                armoRefs.TryGetValue(formID, cur)
                armoRefs(formID) = cur + 1
                If Not armoCache.ContainsKey(formID) Then
                    Dim armo = RecordParsers.ParseARMO(rec, pm)
                    armoCache(formID) = armo
                    ' OMODs in ARMO.Combinations also count
                    For Each c In armo.Combinations
                        For Each inc In c.Includes
                            If inc.ModFormID <> 0UI Then
                                DiscoverOmodRecursive(inc.ModFormID, pm, omodCache, omodRefs)
                            End If
                        Next
                    Next
                End If
            Case Else
                ' WEAP / MISC / AMMO / KEYM / etc. — out of scope for this dump.
        End Select
    End Sub

    ''' <summary>Recursively discover an OMOD and every sub-OMOD it references via Includes.
    ''' Bumps reference count on every visit. Uses omodCache as a visited-set marker —
    ''' presence of the key (even with Nothing value) means "already walked this OMOD".</summary>
    Private Sub DiscoverOmodRecursive(formID As UInteger,
                                      pm As PluginManager,
                                      omodCache As Dictionary(Of UInteger, OMOD_Data),
                                      omodRefs As Dictionary(Of UInteger, Integer))
        If formID = 0UI Then Return
        Dim cur As Integer = 0
        omodRefs.TryGetValue(formID, cur)
        omodRefs(formID) = cur + 1

        If omodCache.ContainsKey(formID) Then Return ' already walked (or marked NotFound)

        Dim rec = pm.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "OMOD" Then
            omodCache(formID) = Nothing ' mark visited as NotFound to avoid retry storms
            Return
        End If
        Dim parsed = CraftingRecordParsers.ParseOMOD(rec, pm)
        omodCache(formID) = parsed

        For Each inc In parsed.Includes
            If inc.ModFormID <> 0UI Then
                DiscoverOmodRecursive(inc.ModFormID, pm, omodCache, omodRefs)
            End If
        Next
    End Sub

    Private Function ResolveOmod(formID As UInteger, pm As PluginManager, cache As Dictionary(Of UInteger, OMOD_Data)) As OMOD_Data
        If formID = 0UI Then Return Nothing
        Dim cached As OMOD_Data = Nothing
        If cache.TryGetValue(formID, cached) Then Return cached
        Dim rec = pm.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "OMOD" Then
            cache(formID) = Nothing
            Return Nothing
        End If
        Dim parsed = CraftingRecordParsers.ParseOMOD(rec, pm)
        cache(formID) = parsed
        Return parsed
    End Function

    ' ───────────────────────────── Append helpers ─────────────────────────────

    Private Sub AppendCombination(sb As StringBuilder,
                                  hdr As NPC_ObjectTemplateCombination,
                                  indent As String,
                                  formTypeContext As String,
                                  pm As PluginManager)
        Dim cmb = hdr.Combination
        sb.AppendLine($"{indent}DisplayName:               ""{hdr.DisplayName}""  (OBTF EditorOnly={hdr.IsEditorOnly})")
        sb.AppendLine($"{indent}IsDefault:                 {cmb.IsDefault}")
        sb.AppendLine($"{indent}ParentCombinationIndex:    {cmb.ParentCombinationIndex}")
        sb.AppendLine($"{indent}LevelMin/Max:              {cmb.LevelMin} / {cmb.LevelMax}")
        sb.AppendLine($"{indent}MinLevelForRanks:          {cmb.MinLevelForRanks}")
        sb.AppendLine($"{indent}AltLevelsPerTier:          {cmb.AltLevelsPerTier}")
        sb.AppendLine($"{indent}Keywords ({cmb.Keywords.Count}):")
        For Each kwFid In cmb.Keywords
            sb.AppendLine($"{indent}  {DescribeRecord(kwFid, pm)}")
        Next
        sb.AppendLine($"{indent}Includes ({cmb.Includes.Count}):")
        For idx = 0 To cmb.Includes.Count - 1
            Dim inc = cmb.Includes(idx)
            sb.AppendLine($"{indent}  [{idx}] OMOD={DescribeRecord(inc.ModFormID, pm)}")
            sb.AppendLine($"{indent}       AttachPointIndex={inc.AttachPointIndex}  Optional={inc.IsOptional}  DontUseAll={inc.DontUseAll}")
        Next
        sb.AppendLine($"{indent}Properties ({cmb.Properties.Count}) — declared directly on this combination:")
        For pi = 0 To cmb.Properties.Count - 1
            AppendPropertyDescription(sb, cmb.Properties(pi), pi, indent & "  ", formTypeContext, pm)
        Next
    End Sub

    Private Sub AppendFullOmod(sb As StringBuilder, omod As OMOD_Data, refCount As Integer, pm As PluginManager)
        sb.AppendLine($"[OMOD {DescribeRecord(omod.FormID, pm)}]  (referenced {refCount} times)")
        sb.AppendLine($"  RecordFlags:        0x{omod.RecordFlags:X8}  ({DescribeOmodRecordFlags(omod.RecordFlags)})")
        sb.AppendLine($"  FullName:           ""{omod.FullName}""")
        sb.AppendLine($"  Description:        ""{TrimForLog(omod.Description, 200)}""")
        sb.AppendLine($"  ModelPath:          ""{omod.ModelPath}""")
        sb.AppendLine($"  Filter (FLTR):      ""{omod.Filter}""")
        sb.AppendLine($"  LooseModFormID:     {DescribeRecord(omod.LooseModFormID, pm)}")
        sb.AppendLine($"  Priority:           {omod.Priority}")
        sb.AppendLine($"  FormType:           {omod.FormTypeSignature}")
        sb.AppendLine($"  MaxRank:            {omod.MaxRank}")
        sb.AppendLine($"  LevelTierScaledOff: {omod.LevelTierScaledOffset}")
        sb.AppendLine($"  UnknownBool1/2:     {omod.UnknownBool1} / {omod.UnknownBool2}")
        sb.AppendLine($"  AttachPoint:        {DescribeRecord(omod.AttachPointFormID, pm)}")
        sb.AppendLine($"  AttachParentSlots ({omod.AttachParentSlotFormIDs.Count}):")
        For Each apsFid In omod.AttachParentSlotFormIDs
            sb.AppendLine($"    {DescribeRecord(apsFid, pm)}")
        Next
        sb.AppendLine($"  LegacyItems ({omod.LegacyItems.Count}):")
        For idx = 0 To omod.LegacyItems.Count - 1
            Dim it = omod.LegacyItems(idx)
            sb.AppendLine($"    [{idx}] Value1=0x{it.Value1:X8}  Value2=0x{it.Value2:X8}")
        Next
        sb.AppendLine($"  TargetKeywords/MNAM ({omod.TargetKeywordFormIDs.Count}):")
        For Each kwFid In omod.TargetKeywordFormIDs
            sb.AppendLine($"    {DescribeRecord(kwFid, pm)}")
        Next
        sb.AppendLine($"  FilterKeywords/FNAM ({omod.FilterKeywordFormIDs.Count}):")
        For Each kwFid In omod.FilterKeywordFormIDs
            sb.AppendLine($"    {DescribeRecord(kwFid, pm)}")
        Next
        sb.AppendLine($"  Includes ({omod.Includes.Count}):")
        For idx = 0 To omod.Includes.Count - 1
            Dim inc = omod.Includes(idx)
            sb.AppendLine($"    [{idx}] OMOD={DescribeRecord(inc.ModFormID, pm)}")
            sb.AppendLine($"         MinimumLevel={inc.MinimumLevel}  Optional={inc.IsOptional}  DontUseAll={inc.DontUseAll}")
        Next
        sb.AppendLine($"  Properties ({omod.Properties.Count}):")
        For pi = 0 To omod.Properties.Count - 1
            AppendPropertyDescription(sb, omod.Properties(pi), pi, "    ", omod.FormTypeSignature, pm)
        Next
        sb.AppendLine()
    End Sub

    Private Sub AppendFullArmo(sb As StringBuilder, armo As ARMO_Data, refCount As Integer, pm As PluginManager)
        sb.AppendLine($"[ARMO {DescribeRecord(armo.FormID, pm)}]  (referenced from {refCount} DOFT/Inventory paths)")
        sb.AppendLine($"  FullName:           ""{armo.FullName}""")
        sb.AppendLine($"  Race:               {DescribeRecord(armo.RaceFormID, pm)}")
        sb.AppendLine($"  SlotMask:           0x{armo.SlotMask:X8}")
        sb.AppendLine($"  TemplateArmor:      {DescribeRecord(armo.TemplateArmorFormID, pm)}  (TPLT — link only, not recursively expanded)")
        sb.AppendLine($"  BaseAddonIndex:     {armo.BaseAddonIndex}")
        sb.AppendLine($"  MaleWorldModel:     ""{armo.MaleWorldModelPath}""")
        sb.AppendLine($"  FemaleWorldModel:   ""{armo.FemaleWorldModelPath}""")
        sb.AppendLine($"  ArmorAddons (Models, INDX→ARMA, count={armo.ArmorAddons.Count}):")
        For idx = 0 To armo.ArmorAddons.Count - 1
            Dim entry = armo.ArmorAddons(idx)
            sb.AppendLine($"    INDX={entry.AddonIndex}  ARMA={DescribeRecord(entry.ArmaFormID, pm)}")
        Next
        sb.AppendLine($"  Combinations ({armo.Combinations.Count}):")
        For ci = 0 To armo.Combinations.Count - 1
            Dim c = armo.Combinations(ci)
            sb.AppendLine($"    ──── Combination #{ci} ────")
            sb.AppendLine($"      IsDefault:                 {c.IsDefault}")
            sb.AppendLine($"      ParentCombinationIndex:    {c.ParentCombinationIndex}")
            sb.AppendLine($"      LevelMin/Max:              {c.LevelMin} / {c.LevelMax}")
            sb.AppendLine($"      MinLevelForRanks:          {c.MinLevelForRanks}")
            sb.AppendLine($"      AltLevelsPerTier:          {c.AltLevelsPerTier}")
            sb.AppendLine($"      Keywords ({c.Keywords.Count}):")
            For Each kwFid In c.Keywords
                sb.AppendLine($"        {DescribeRecord(kwFid, pm)}")
            Next
            sb.AppendLine($"      Includes ({c.Includes.Count}):")
            For ii = 0 To c.Includes.Count - 1
                Dim inc = c.Includes(ii)
                sb.AppendLine($"        [{ii}] OMOD={DescribeRecord(inc.ModFormID, pm)}")
                sb.AppendLine($"             AttachPointIndex={inc.AttachPointIndex}  Optional={inc.IsOptional}  DontUseAll={inc.DontUseAll}")
            Next
            sb.AppendLine($"      Properties ({c.Properties.Count}):")
            For pi = 0 To c.Properties.Count - 1
                AppendPropertyDescription(sb, c.Properties(pi), pi, "        ", "ARMO", pm)
            Next
        Next
        sb.AppendLine()
    End Sub

    Private Sub AppendPropertyDescription(sb As StringBuilder,
                                          prop As OMOD_Property,
                                          idx As Integer,
                                          indent As String,
                                          formTypeContext As String,
                                          pm As PluginManager)
        Dim propName = PropertyIndexName(formTypeContext, prop.PropertyIndex)
        Dim valueTypeName = ValueTypeNameOf(prop.ValueType)
        Dim funcTypeName = FunctionTypeNameOf(prop.ValueType, prop.FunctionType)
        sb.AppendLine($"{indent}[{idx}] PropertyIdx={prop.PropertyIndex} ({propName})  ValueType={CInt(prop.ValueType)} ({valueTypeName})  FunctionType={prop.FunctionType} ({funcTypeName})")
        Select Case prop.ValueType
            Case OMOD_ValueType.IntType, OMOD_ValueType.EnumType
                Dim asInt = BitConverter.ToInt32(BitConverter.GetBytes(prop.Value1), 0)
                sb.AppendLine($"{indent}    Value1: Int={asInt}  rawHex=0x{BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value1), 0):X8}")
            Case OMOD_ValueType.FloatType
                sb.AppendLine($"{indent}    Value1: Float={prop.Value1:R}  rawHex=0x{BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value1), 0):X8}")
            Case OMOD_ValueType.BoolType
                Dim asInt = BitConverter.ToInt32(BitConverter.GetBytes(prop.Value1), 0)
                sb.AppendLine($"{indent}    Value1: Bool={(asInt <> 0)}  raw={asInt}")
            Case OMOD_ValueType.StringType
                sb.AppendLine($"{indent}    Value1: String/raw=0x{BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value1), 0):X8}")
            Case OMOD_ValueType.FormIDInt
                sb.AppendLine($"{indent}    Value1: FormID={DescribeRecord(prop.Value1FormID, pm)}  Value2(Int)={BitConverter.ToInt32(BitConverter.GetBytes(prop.Value2), 0)}")
            Case OMOD_ValueType.FormIDFloat
                sb.AppendLine($"{indent}    Value1: FormID={DescribeRecord(prop.Value1FormID, pm)}  Value2(Float)={prop.Value2:R}")
            Case Else
                sb.AppendLine($"{indent}    Value1: rawHex=0x{BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value1), 0):X8}")
        End Select
        If prop.ValueType <> OMOD_ValueType.FormIDInt AndAlso prop.ValueType <> OMOD_ValueType.FormIDFloat Then
            sb.AppendLine($"{indent}    Value2: Float={prop.Value2:R}  rawHex=0x{BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value2), 0):X8}")
        End If
        sb.AppendLine($"{indent}    Step:   {prop.StepValue:R}")
    End Sub

    Private Sub AccumulateMatrix(matrix As Dictionary(Of String, Integer),
                                 formTypeContext As String,
                                 prop As OMOD_Property)
        Dim propName = PropertyIndexName(formTypeContext, prop.PropertyIndex)
        Dim valueTypeName = ValueTypeNameOf(prop.ValueType)
        Dim funcTypeName = FunctionTypeNameOf(prop.ValueType, prop.FunctionType)
        Dim key = $"FormType={formTypeContext,-6}  PropertyIdx={prop.PropertyIndex,3} ({propName,-22})  ValueType={valueTypeName,-14}  FunctionType={funcTypeName}"
        Dim cur As Integer = 0
        matrix.TryGetValue(key, cur)
        matrix(key) = cur + 1
    End Sub

    ' ───────────────────────────── Signature ─────────────────────────────

    Private Class SignatureBucket
        Public SampleHeader As NPC_ObjectTemplateCombination
        Public OccurrenceCount As Integer = 0
        Public SampleNpcFormIDs As New List(Of UInteger)
    End Class

    Private Function ComputeCombinationSignature(hdr As NPC_ObjectTemplateCombination) As String
        Dim cmb = hdr.Combination
        If cmb Is Nothing Then Return "(null)"
        Dim sb As New StringBuilder(256)
        sb.Append(If(hdr.IsEditorOnly, "OBTF1|", "OBTF0|"))
        sb.Append("FULL=").Append(If(hdr.DisplayName, "")).Append("|")
        sb.Append("D=").Append(If(cmb.IsDefault, "1", "0"))
        sb.Append("|PCI=").Append(cmb.ParentCombinationIndex)
        sb.Append("|LMn=").Append(cmb.LevelMin).Append(",LMx=").Append(cmb.LevelMax)
        sb.Append("|MLR=").Append(cmb.MinLevelForRanks).Append(",ALPT=").Append(cmb.AltLevelsPerTier)
        sb.Append("|KW=[")
        For Each kw In cmb.Keywords.OrderBy(Function(x) x)
            sb.Append(kw.ToString("X8")).Append(",")
        Next
        sb.Append("]|INC=[")
        For Each inc In cmb.Includes
            sb.Append(inc.ModFormID.ToString("X8")).Append("/").
               Append(inc.AttachPointIndex).Append("/").
               Append(If(inc.IsOptional, "O", "_")).Append("/").
               Append(If(inc.DontUseAll, "X", "_")).Append(",")
        Next
        sb.Append("]|PROP=[")
        For Each prop In cmb.Properties
            sb.Append("V").Append(CInt(prop.ValueType)).
               Append("F").Append(prop.FunctionType).
               Append("P").Append(prop.PropertyIndex).
               Append("V1=").Append(BitConverter.ToUInt32(BitConverter.GetBytes(prop.Value1), 0).ToString("X8")).
               Append("V2=").Append(prop.Value2.ToString("R")).
               Append("S=").Append(prop.StepValue.ToString("R")).Append(",")
        Next
        sb.Append("]")
        Return sb.ToString()
    End Function

    ' ───────────────────────────── Lookups ─────────────────────────────

    Private Function PropertyIndexName(formType As String, idx As UShort) As String
        Select Case formType
            Case "ARMO"
                Select Case idx
                    Case 0US : Return "Enchantments"
                    Case 1US : Return "BashImpactDataSet"
                    Case 2US : Return "BlockMaterial"
                    Case 3US : Return "Keywords"
                    Case 4US : Return "Weight"
                    Case 5US : Return "Value"
                    Case 6US : Return "Rating"
                    Case 7US : Return "AddonIndex"
                    Case 8US : Return "BodyPart"
                    Case 9US : Return "DamageTypeValue"
                    Case 10US : Return "ActorValues"
                    Case 11US : Return "Health"
                    Case 12US : Return "ColorRemappingIndex"
                    Case 13US : Return "MaterialSwaps"
                    Case Else : Return "?"
                End Select
            Case "NPC_"
                Select Case idx
                    Case 0US : Return "Keywords"
                    Case 1US : Return "ForcedInventory"
                    Case 2US : Return "XPOffset"
                    Case 3US : Return "Enchantments"
                    Case 4US : Return "ColorRemappingIndex"
                    Case 5US : Return "MaterialSwaps"
                    Case Else : Return "?"
                End Select
            Case "WEAP"
                Select Case idx
                    Case 31US : Return "Keywords"
                    Case 88US : Return "ColorRemappingIndex"
                    Case 89US : Return "MaterialSwaps"
                    Case Else : Return "?"
                End Select
            Case Else
                Return "?"
        End Select
    End Function

    Private Function ValueTypeNameOf(vt As OMOD_ValueType) As String
        Select Case vt
            Case OMOD_ValueType.IntType : Return "Int"
            Case OMOD_ValueType.FloatType : Return "Float"
            Case OMOD_ValueType.BoolType : Return "Bool"
            Case OMOD_ValueType.StringType : Return "String"
            Case OMOD_ValueType.FormIDInt : Return "FormID,Int"
            Case OMOD_ValueType.EnumType : Return "Enum"
            Case OMOD_ValueType.FormIDFloat : Return "FormID,Float"
            Case Else : Return $"?({CInt(vt)})"
        End Select
    End Function

    Private Function FunctionTypeNameOf(vt As OMOD_ValueType, ft As Byte) As String
        Select Case vt
            Case OMOD_ValueType.FloatType
                Select Case ft
                    Case 0 : Return "SET"
                    Case 1 : Return "MUL+ADD"
                    Case 2 : Return "ADD"
                    Case Else : Return $"?({ft})"
                End Select
            Case OMOD_ValueType.BoolType
                Select Case ft
                    Case 0 : Return "SET"
                    Case 1 : Return "AND"
                    Case 2 : Return "OR"
                    Case Else : Return $"?({ft})"
                End Select
            Case OMOD_ValueType.EnumType
                Select Case ft
                    Case 0 : Return "SET"
                    Case Else : Return $"?({ft})"
                End Select
            Case OMOD_ValueType.FormIDInt, OMOD_ValueType.FormIDFloat
                Select Case ft
                    Case 0 : Return "SET"
                    Case 1 : Return "REM"
                    Case 2 : Return "ADD"
                    Case Else : Return $"?({ft})"
                End Select
            Case Else
                Select Case ft
                    Case 0 : Return "SET"
                    Case 1 : Return "REM"
                    Case 2 : Return "ADD"
                    Case Else : Return $"?({ft})"
                End Select
        End Select
    End Function

    ''' <summary>OMOD record header flags per wbDefinitionsFO4.pas:12864-12866. Only 2 named
    ''' bits; everything else folds into the standard Bethesda record flags (compressed,
    ''' deleted, etc.) which we leave as raw hex above.</summary>
    Private Function DescribeOmodRecordFlags(flags As UInteger) As String
        Dim parts As New List(Of String)
        If (flags And &H8UI) <> 0UI Then parts.Add("Legendary Mod")
        If (flags And &H40UI) <> 0UI Then parts.Add("Mod Collection")
        If parts.Count = 0 Then Return "(none named)"
        Return String.Join(", ", parts)
    End Function

    Private Function TrimForLog(s As String, maxLen As Integer) As String
        If String.IsNullOrEmpty(s) Then Return ""
        Dim oneLine = s.Replace(vbCrLf, " ").Replace(vbLf, " ").Replace(vbCr, " ")
        If oneLine.Length <= maxLen Then Return oneLine
        Return oneLine.Substring(0, maxLen) & "..."
    End Function

    Private Function DescribeRecord(formID As UInteger, pm As PluginManager) As String
        If formID = 0UI Then Return "(none)"
        Dim rec = pm.GetRecord(formID)
        If rec Is Nothing Then Return $"[{formID:X8}] (record not loaded)"
        Dim sig = rec.Header.Signature
        Dim edid = If(rec.EditorID <> "", rec.EditorID, sig)
        Dim plugin = If(String.IsNullOrWhiteSpace(rec.SourcePluginName), "", $" @{rec.SourcePluginName}")
        Return $"{sig}:{formID:X8} ""{edid}""{plugin}"
    End Function

    Private Sub Report(progress As IProgress(Of String), message As String)
        If progress IsNot Nothing Then progress.Report(message)
    End Sub

End Module
