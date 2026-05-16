Imports FO4_Base_Library

''' <summary>
''' Canonical resolver for ARMO/NPC_ ObjectTemplate (OBTE/OBTS) combinations.
'''
''' Engine rule (revised 2026-05-10 after reading ARMA APPR + OMOD AttachPoint of vanilla
''' Combat_Torso, Mining Helmet, Synth, Mr Handy in dump v2):
'''
'''   The engine resolves OBTE SLOT-BY-SLOT, not "one combination wins":
'''
'''     1. ARMA exposes N "AttachParent slots" (each = a KYWD with Type=2 "Attach Point").
'''        Examples: ap_armor_Tier (Material), ap_armor_Lining, ap_armor_Size, ap_Legendary;
'''        ap_Bot_BotCore, ap_Bot_ArmsTypeA1, ap_Bot_ModSlotA for robots.
'''     2. Each OMOD declares which slot it occupies via OMOD.AttachPointFormID.
'''     3. Each combination defines a (partial) map  slot → OMOD  via its Includes.
'''     4. For every applicable combination (Default OR keyword-match), walked in record
'''        declaration order, the engine merges INCLUDES per slot — last OMOD wins per slot.
'''     5. The simultaneous union of (one OMOD per slot) is what renders.
'''
'''   Combination applicability (a combo enters the merge if):
'''     • IsDefault = True                                — baseline build (always)
'''     • Keywords ∩ ctxKeywords ≠ ∅                      — context-driven overlay
'''   Otherwise the combo is inert (workbench / leveled / quest selectable, not engine-applied).
'''   If NONE applies (vanilla edge), fallback to the first combination so the record renders.
'''
''' Why slot-by-slot and not "one combination":
'''   Combat_Torso Combo #0 (Default) = Size_A + Material_0 + Lining_Null. Combo #4 (kw-match) = ONLY
'''   Material_0B. The "one combination" rule would either drop Combo #4's material variant
'''   (if Default wins) or drop Size+Lining from the build (if Combo #4 wins). Slot-by-slot:
'''   Default fills all 3 slots, Combo #4 overrides only Material → final = Size_A + Material_0B
'''   + Lining_Null. Engine-correct.
'''
'''   Mining Helmet 8 Default combinations all carry Helmet_Mining_Mod + Headlamp + ColorVariant
'''   on the same 3 slots → with slot-by-slot, the LAST in declaration order wins per slot,
'''   producing a single coherent helmet (last color). With "all overlay" we'd stack 8 colors.
'''
''' OMODs WITHOUT AttachPoint (FormID=0):
'''   Vanilla: ~70 OMODs (botcol_* color overlays for Protectron, paint mods like
'''   DLC01Bot_Paint_Mechanist). They don't compete for a slot — go to an "unslotted" bucket
'''   and ALL stack. A Protectron's 7 botcol_* OMODs all render as additive color overlays.
'''
''' Sub-OMODs (OMOD.Includes recursion):
'''   Same bucketing. Each sub-OMOD's own AttachPointFormID decides slot vs unslotted.
'''   Visited-set anti-cycle (vanilla observed depth = 14 in Mr Handy chains).
'''
''' DirectProperties (Properties declared inline on a combination, not on an OMOD):
'''   No slot concept — they all stack from every applicable combination, in declaration order.
'''   Caso típico: BandanaSwapSkull MSWP ADD on a combination with Inc=0 / Props=1.
'''
''' OBTF=Editor Only flag: ignored. EncProtectron02's single applied combination is
''' OBTF=True with Inc=20 (the full robot build) — proves OBTF doesn't mean "skip at runtime".
''' </summary>
Public Module ObjectTemplateResolver

    ''' <summary>Bundle of "what the engine would apply" for one record under one keyword
    ''' context. Order matters in both lists — the applier walks them in sequence so SET vs
    ''' ADD operations on the same material chain compose correctly.</summary>
    Public Class CombinationResolution
        ''' <summary>Combinations that matched (Default OR keyword), in record declaration order.
        ''' Includes both inert (Includes-only and/or Properties-only) bundles.</summary>
        Public AppliedCombinations As New List(Of ARMO_Combination)

        ''' <summary>Properties declared inline on applied combinations, flattened in order.
        ''' Same OMOD_Property layout the OMOD records use; consumer uses PropertyIndex +
        ''' FunctionType + ValueType to dispatch. FormType context is implicit from the parent
        ''' record (ARMO → wbArmorPropertyEnum, NPC_ → wbActorPropertyEnum).</summary>
        Public DirectProperties As New List(Of OMOD_Property)

        ''' <summary>OMODs reached via Includes (recursive expansion), in walk order. Each
        ''' OMOD carries its own Properties and may itself include sub-OMODs (already expanded
        ''' here — consumer iterates a flat list). Visited-set anti-cycle guarantees no OMOD
        ''' appears twice even if reached through multiple paths in the same combination.</summary>
        Public IncludedOmods As New List(Of OMOD_Data)

        ''' <summary>Parallel list of AttachPointIndex values (1:1 with <see cref="IncludedOmods"/>).
        ''' For each emitted OMOD this carries the apIdx from its parent Include — engine uses it
        ''' to pick the indexed socket (P-X|N) when the OMOD's own AttachPoint maps to multiple
        ''' sockets in the host (Mr Handy 3 arms: same AP, apIdx 0/1/2). Sub-OMODs from
        ''' OMOD.Includes carry apIdx=0 (OMOD_Include doesn't have the field).</summary>
        Public IncludedOmodApIdx As New List(Of Byte)
    End Class

    ''' <summary>Resolve combinations for an ARMO under the given keyword context (typically
    ''' propagated from LVLI.LLKC chain — see arch_outfit_resolution.md). Returns an empty
    ''' resolution if armo.Combinations is empty (no OBTE on this ARMO).</summary>
    Public Function ResolveArmoCombinations(armo As ARMO_Data,
                                            ctxKeywords As List(Of UInteger),
                                            pm As PluginManager) As CombinationResolution
        Dim result As New CombinationResolution()
        If armo Is Nothing OrElse armo.Combinations Is Nothing OrElse armo.Combinations.Count = 0 Then
            Return result
        End If
        ' ARMO doesn't have an actor-level APPR — children pool starts empty (humanoid armor
        ' chains don't use the AP-filter mechanic the way robots/brahmin do).
        ResolveCombinationList(armo.Combinations, ctxKeywords, pm, result, New HashSet(Of UInteger))
        Return result
    End Function

    ''' <summary>Resolve combinations for an NPC_'s ObjectTemplate. Same algorithm as ARMO —
    ''' the difference is only the FormType context the consumer applies (NPC_ vs ARMO),
    ''' which decides which PropertyIndex enum is used to interpret each Property's idx.</summary>
    Public Function ResolveNpcCombinations(npc As NPC_Data,
                                           ctxKeywords As List(Of UInteger),
                                           pm As PluginManager) As CombinationResolution
        Dim result As New CombinationResolution()
        If npc Is Nothing OrElse Not npc.HasObjectTemplate OrElse npc.ObjectTemplateCombinations Is Nothing Then
            Return result
        End If
        ' NPC_.ObjectTemplateCombinations wraps each ARMO_Combination in NPC_ObjectTemplateCombination
        ' so we extract the inner combinations for the shared algorithm.
        Dim flat As New List(Of ARMO_Combination)
        For Each hdr In npc.ObjectTemplateCombinations
            If hdr.Combination IsNot Nothing Then flat.Add(hdr.Combination)
        Next
        ' Initial AP pool seeded from TWO sources:
        '   1. NPC.APPR (per-actor APPR — brahmin: ap_HornsL/HornsR/PackBase).
        '   2. RACE.APPR (per-race APPR — robots use this: HandyRace = ap_Bot_BotCore + ap_Bot_BotLegs).
        ' Children OMODs accepted into the pool extend it via their own AttachParentSlots
        ' (chain of authorization — TorsoHandy exposes ap_Bot_ModSlotA/B/C, etc.).
        Dim initialPool As New HashSet(Of UInteger)
        If npc.AttachParentSlotFormIDs IsNot Nothing Then
            For Each fid In npc.AttachParentSlotFormIDs
                If fid <> 0UI Then initialPool.Add(fid)
            Next
        End If
        ' RACE.APPR (resolved via NPC.RaceFormID).
        If npc.RaceFormID <> 0UI AndAlso pm IsNot Nothing Then
            Dim raceRec = pm.GetRecord(npc.RaceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                Dim race = RecordParsers.ParseRACE(raceRec, pm)
                If race IsNot Nothing AndAlso race.AttachParentSlotFormIDs IsNot Nothing Then
                    For Each fid In race.AttachParentSlotFormIDs
                        If fid <> 0UI Then initialPool.Add(fid)
                    Next
                End If
            End If
        End If
        ResolveCombinationList(flat, ctxKeywords, pm, result, initialPool)
        Return result
    End Function

    ' ───────────────────────────── internals ─────────────────────────────

    ''' <summary>Module-shared RNG for random child selection in modcol_* containers
    ''' (DontUseAll=True). Single instance so consecutive renders pick different variants.
    ''' Thread-unsafe but render resolution runs single-threaded.</summary>
    Private ReadOnly _rng As New Random()

    ''' <summary>[DIAG] Devuelve EditorID del KYWD para logging legible. "?" si record no
    ''' carga (caso típico: KYWD no incluido en SIGS_NPC_RENDERING o plugin no resuelto).
    ''' "-" para FormID=0.</summary>
    Friend Function KywdEditorIdSafe(fid As UInteger, pm As PluginManager) As String
        If fid = 0UI Then Return "-"
        If pm Is Nothing Then Return "?"
        Try
            Dim r = pm.GetRecord(fid)
            If r Is Nothing Then Return "?MISSING"
            If r.Header.Signature <> "KYWD" Then Return "?" & r.Header.Signature
            Return If(r.EditorID, "?NOEDID")
        Catch ex As Exception
            Return "?EX:" & ex.GetType().Name
        End Try
    End Function

    Private Sub ResolveCombinationList(combos As List(Of ARMO_Combination),
                                       ctxKeywords As List(Of UInteger),
                                       pm As PluginManager,
                                       result As CombinationResolution,
                                       initialApPool As HashSet(Of UInteger))
        ' Canonical engine rule (revised 2026-05-10 — verified by reading ARMA APPR + OMOD
        ' MNAM/AttachPoint of vanilla Armor_Combat_Torso, Mining Helmet, Combat Torso variants):
        '
        '   The ARMA exposes N "AttachParent slots" (each slot = one KYWD with Type=2).
        '   Each OMOD declares which slot it occupies via OMOD.AttachPointFormID (→ KYWD).
        '   Each combination defines a partial map  slot → OMOD.
        '   The engine resolves SLOT-BY-SLOT: walks every applicable combination in record
        '   declaration order; per slot, the LAST OMOD wins.
        '   The simultaneous union of (one OMOD per slot) is what the engine applies.
        '
        ' Combination applicability (a combo enters the merge if ANY of):
        '   • IsDefault = True                                  — baseline build
        '   • Keywords ∩ ctxKeywords ≠ ∅                        — context-driven overlay
        ' Combinations matching neither are inert (workbench/leveled alternatives the engine
        ' doesn't apply without explicit selection).
        '
        ' OMODs WITHOUT AttachPoint (FormID=0) — vanilla case: ~70 such OMODs (botcol_*,
        ' paint mods like DLC01Bot_Paint_Mechanist). They DON'T compete for a slot — they
        ' all stack. Treated as "unslotted bucket" so a Protectron with 7 botcol_* mods
        ' renders all 7 color overlays correctly.
        '
        ' Sub-OMODs (OMOD.Includes recursive) follow the same bucketing: each sub-OMOD's
        ' AttachPointFormID determines whether it slots or stacks. Visited-set guards
        ' against cycles (vanilla observed depth = 14 on Mr Handy chains).

        ' [DIAG] Log combos count + ctxKeywords for the resolve context.
        Dim ctxKwStr = If(ctxKeywords Is Nothing OrElse ctxKeywords.Count = 0, "(empty)",
                          String.Join(",", ctxKeywords.Select(Function(k) "0x" & k.ToString("X8") & "(" & KywdEditorIdSafe(k, pm) & ")")))
        Logger.LogLazy(Function() $"[OBTE-RESOLVE-START] combos={combos.Count} ctxKeywords={ctxKwStr} initialPool={initialApPool.Count}")

        ' Selection: build the applicable set in record declaration order.
        Dim applicable As New List(Of ARMO_Combination)
        Dim comboIdx As Integer = 0
        For Each combo In combos
            Dim curIdx = comboIdx
            comboIdx += 1
            If combo Is Nothing Then
                Logger.LogLazy(Function() $"[OBTE-COMBO] idx={curIdx} NULL combo — skipped")
                Continue For
            End If
            Dim isApplicable As Boolean = combo.IsDefault
            Dim reason As String = If(combo.IsDefault, "Default", "")
            Dim matchedKw As UInteger = 0UI
            If Not isApplicable AndAlso combo.Keywords IsNot Nothing AndAlso combo.Keywords.Count > 0 _
               AndAlso ctxKeywords IsNot Nothing AndAlso ctxKeywords.Count > 0 Then
                For Each kw In combo.Keywords
                    If ctxKeywords.Contains(kw) Then
                        isApplicable = True
                        matchedKw = kw
                        reason = $"KWMatch(0x{kw:X8}={KywdEditorIdSafe(kw, pm)})"
                        Exit For
                    End If
                Next
            End If
            Dim kwsStr = If(combo.Keywords Is Nothing OrElse combo.Keywords.Count = 0, "[]",
                            "[" & String.Join(",", combo.Keywords.Select(Function(k) "0x" & k.ToString("X8") & "(" & KywdEditorIdSafe(k, pm) & ")")) & "]")
            Dim incCount = If(combo.Includes Is Nothing, 0, combo.Includes.Count)
            Dim propCount = If(combo.Properties Is Nothing, 0, combo.Properties.Count)
            Dim isAppLog = isApplicable, reasonLog = If(isApplicable, reason, "INERT-no-default-no-kwmatch")
            Logger.LogLazy(Function() $"[OBTE-COMBO] idx={curIdx} isDefault={combo.IsDefault} kw={kwsStr} inc={incCount} props={propCount} applicable={isAppLog} reason={reasonLog}")
            If isApplicable Then applicable.Add(combo)
        Next

        ' Defensive fallback: if NOTHING is applicable but combos exist, take the first.
        ' Engine never leaves a record without at least one combination applied when OBTE present.
        If applicable.Count = 0 Then
            For Each combo In combos
                If combo IsNot Nothing Then
                    Logger.LogLazy(Function() $"[OBTE-FALLBACK] no combo applicable → forcing first non-null combo")
                    applicable.Add(combo)
                    Exit For
                End If
            Next
        End If

        If applicable.Count = 0 Then Return

        result.AppliedCombinations.AddRange(applicable)

        ' New resolution algorithm (2026-05-13, verified against vanilla brahmin OBTE):
        '
        ' Two-phase collection then AP-pool filter:
        '
        ' Phase 1 — Collect candidates by walking OBTE Includes and OMOD.Includes recursively.
        '   • OMOD with AttachPoint (a.k.a. "real chunk"): added to candidates list with its apIdx.
        '   • OMOD without AttachPoint (a.k.a. "container", e.g. modcol_*): not a candidate itself;
        '     recurse into its Includes.
        '   • Include.DontUseAll governs how the container's children are walked:
        '       True  → random pick 1 child (mutex variant — modcol_BrahminHorns picks L01/L02/L03 at random)
        '       False → walk all children
        '
        ' Phase 2 — Apply AP-pool filter to surviving candidates.
        '   pool starts at initialApPool (NPC.APPR seed). Then iterate to convergence:
        '     For each candidate not yet decided:
        '       If candidate.AttachPoint ∈ pool → accept, pool += candidate.AttachParentSlots
        '   Discarded candidates have AttachPoint outside the accumulated pool — they were
        '   declared by the OBTE but their parent chain doesn't authorize the slot
        '   (e.g. PackLight01 with AP=ap_PackLight01 is rejected if no accepted OMOD exposed
        '   ap_PackLight01 in its AttachParentSlots).
        ' Visited keyed by (FormID, apIdx): the same OMOD can be referenced multiple times
        ' with different apIdx values (Mr Handy 3 eyes = same Eye1B OMOD with apIdx 0/1/2;
        ' ModArmsHandyAR1A appears twice with apIdx 0 and 1). Dedup by FormID alone would
        ' collapse all instances to the first one.
        Dim visitedOmods As New HashSet(Of (UInteger, Byte))
        Dim candidates As New List(Of (Omod As OMOD_Data, ApIdx As Byte))
        Dim unslottedOmods As New List(Of (OMOD_Data, Byte))

        For Each combo In applicable
            ' DirectProperties — inline on combination, no slot concept (all stack).
            If combo.Properties IsNot Nothing Then
                For Each prop In combo.Properties
                    result.DirectProperties.Add(prop)
                Next
            End If
            If combo.Includes IsNot Nothing Then
                For Each inc In combo.Includes
                    If inc Is Nothing OrElse inc.ModFormID = 0UI Then Continue For
                    Dim incLocal = inc
                    Logger.LogLazy(Function() $"[OBTE-INC] modFid=0x{incLocal.ModFormID:X8} apIdx={incLocal.AttachPointIndex} dontUseAll={incLocal.DontUseAll}")
                    CollectOmodCandidate(inc.ModFormID, inc.AttachPointIndex, inc.DontUseAll, pm, visitedOmods, candidates, unslottedOmods)
                Next
            End If
        Next

        ' Phase 2: AP-pool filter.
        Dim apPool As New HashSet(Of UInteger)(initialApPool)
        Dim apPoolBeforeStr = String.Join(",", apPool.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pm) & ")"))
        Logger.LogLazy(Function() $"[OBTE-POOL-INIT] initial pool({apPool.Count}) = [{apPoolBeforeStr}]")

        Dim accepted As New List(Of (Omod As OMOD_Data, ApIdx As Byte))
        Dim pending As New List(Of (Omod As OMOD_Data, ApIdx As Byte))(candidates)
        Dim iterations As Integer = 0
        Const maxIter As Integer = 16
        Do
            iterations += 1
            Dim changed As Boolean = False
            Dim stillPending As New List(Of (OMOD_Data, Byte))
            For Each cand In pending
                If apPool.Contains(cand.Omod.AttachPointFormID) Then
                    accepted.Add(cand)
                    changed = True
                    If cand.Omod.AttachParentSlotFormIDs IsNot Nothing Then
                        For Each fid In cand.Omod.AttachParentSlotFormIDs
                            If fid <> 0UI Then apPool.Add(fid)
                        Next
                    End If
                    Dim cL = cand, pmL = pm
                    Logger.LogLazy(Function() $"[OBTE-POOL-ACCEPT] omod={cL.Omod.EditorID}(0x{cL.Omod.FormID:X8}) ap=0x{cL.Omod.AttachPointFormID:X8}({KywdEditorIdSafe(cL.Omod.AttachPointFormID, pmL)}) addedAPs=[{String.Join(",", cL.Omod.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pmL) & ")"))}]")
                Else
                    stillPending.Add(cand)
                End If
            Next
            pending = stillPending
            If Not changed Then Exit Do
            If iterations >= maxIter Then Exit Do
        Loop

        For Each rej In pending
            Dim rejL = rej, pmRej = pm
            Logger.LogLazy(Function() $"[OBTE-POOL-REJECT] omod={rejL.Omod.EditorID}(0x{rejL.Omod.FormID:X8}) ftype={rejL.Omod.FormTypeSignature} ap=0x{rejL.Omod.AttachPointFormID:X8}({KywdEditorIdSafe(rejL.Omod.AttachPointFormID, pmRej)}) (not in pool)")
        Next

        Logger.LogLazy(Function() $"[OBTE-RESOLVE] applicable={applicable.Count} collected={candidates.Count} accepted={accepted.Count} rejected={pending.Count} unslotted={unslottedOmods.Count} iterations={iterations}")

        ' Emit accepted slotted OMODs first, then unslotted (color overlays, properties-only).
        For Each entry In accepted
            result.IncludedOmods.Add(entry.Omod)
            result.IncludedOmodApIdx.Add(entry.ApIdx)
        Next
        For Each entry In unslottedOmods
            result.IncludedOmods.Add(entry.Item1)
            result.IncludedOmodApIdx.Add(entry.Item2)
        Next
    End Sub

    ''' <summary>Collect an OMOD candidate (or recurse into its container).
    '''   - OMOD with AttachPoint != 0 → leaf candidate; added to candidates list.
    '''   - OMOD without AttachPoint (container) → not emitted itself; its Includes are walked
    '''     according to <paramref name="dontUseAll"/>:
    '''       True  → random-pick 1 Include and recurse only on it (modcol_* mutex variants).
    '''       False → walk all Includes.
    ''' Visited-set guards against cycles.</summary>
    Private Sub CollectOmodCandidate(omodFid As UInteger,
                                      apIdx As Byte,
                                      dontUseAll As Boolean,
                                      pm As PluginManager,
                                      visited As HashSet(Of (UInteger, Byte)),
                                      candidates As List(Of (Omod As OMOD_Data, ApIdx As Byte)),
                                      unslotted As List(Of (OMOD_Data, Byte)))
        If omodFid = 0UI Then Return
        If Not visited.Add((omodFid, apIdx)) Then Return ' cycle / already expanded for this (fid, apIdx)

        Dim rec = pm.GetRecord(omodFid)
        If rec Is Nothing OrElse rec.Header.Signature <> "OMOD" Then Return

        Dim omod = CraftingRecordParsers.ParseOMOD(rec, pm)

        If omod.AttachPointFormID <> 0UI Then
            ' Leaf candidate — has its own AP, will compete in the AP-pool filter.
            candidates.Add((omod, apIdx))
            Dim ol = omod, fl = omodFid, al = apIdx, pmL = pm
            Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol.EditorID}(0x{fl:X8}) ftype={ol.FormTypeSignature} ap=0x{ol.AttachPointFormID:X8}({KywdEditorIdSafe(ol.AttachPointFormID, pmL)}) apIdx={al} model='{ol.ModelPath}' parentSlots=[{String.Join(",", ol.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pmL) & ")"))}]")
            ' Recurse into the leaf's own Includes (some chunks expose sub-OMODs that
            ' depend on their AP being available — handled by AP-pool filter naturally).
            If omod.Includes IsNot Nothing AndAlso omod.Includes.Count > 0 Then
                RecurseContainerIncludes(omod, dontUseAll, pm, visited, candidates, unslotted)
            End If
            Return
        End If

        ' Container (AP=0). DirectProperties of containers (color overlays, MSWP) go to the
        ' unslotted bucket and stack — they don't go through the AP filter.
        If omod.Properties IsNot Nothing AndAlso omod.Properties.Count > 0 AndAlso (omod.Includes Is Nothing OrElse omod.Includes.Count = 0) Then
            unslotted.Add((omod, apIdx))
            Dim ol = omod, fl = omodFid
            Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol.EditorID}(0x{fl:X8}) ap=0 (container, properties-only) → unslotted bucket")
            Return
        End If

        Dim ol2 = omod, fl2 = omodFid, dontL = dontUseAll
        Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol2.EditorID}(0x{fl2:X8}) ap=0 (container, recurse children, dontUseAll={dontL})")

        ' Recurse children per dontUseAll.
        RecurseContainerIncludes(omod, dontUseAll, pm, visited, candidates, unslotted)
    End Sub

    ''' <summary>Walk the Includes of an OMOD container according to the dontUseAll flag of
    ''' the parent Include that referenced this container:
    '''   True  → random-pick 1 Include (mutex variant for modcol_*).
    '''   False → walk all Includes.</summary>
    Private Sub RecurseContainerIncludes(omod As OMOD_Data,
                                          dontUseAll As Boolean,
                                          pm As PluginManager,
                                          visited As HashSet(Of (UInteger, Byte)),
                                          candidates As List(Of (Omod As OMOD_Data, ApIdx As Byte)),
                                          unslotted As List(Of (OMOD_Data, Byte)))
        If omod.Includes Is Nothing OrElse omod.Includes.Count = 0 Then Return
        Dim validIncludes = omod.Includes.Where(Function(i) i IsNot Nothing AndAlso i.ModFormID <> 0UI).ToList()
        If validIncludes.Count = 0 Then Return

        ' Note: OMOD_Include doesn't carry AttachPointIndex (only ARMO_CombinationInclude does);
        ' children of OMOD containers inherit apIdx=0. The DontUseAll flag IS present on
        ' OMOD_Include, so it propagates down through container chains correctly.
        If dontUseAll Then
            ' Random pick 1 of N.
            Dim pick = validIncludes(_rng.Next(validIncludes.Count))
            Dim ol = omod, pickL = pick
            Logger.LogLazy(Function() $"[OBTE-RANDOM-PICK] parent={ol.EditorID} picks include modFid=0x{pickL.ModFormID:X8} (of {validIncludes.Count})")
            CollectOmodCandidate(pick.ModFormID, 0, pick.DontUseAll, pm, visited, candidates, unslotted)
        Else
            For Each inc In validIncludes
                CollectOmodCandidate(inc.ModFormID, 0, inc.DontUseAll, pm, visited, candidates, unslotted)
            Next
        End If
    End Sub


End Module
