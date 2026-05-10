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
        ResolveCombinationList(armo.Combinations, ctxKeywords, pm, result)
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
        ResolveCombinationList(flat, ctxKeywords, pm, result)
        Return result
    End Function

    ' ───────────────────────────── internals ─────────────────────────────

    Private Sub ResolveCombinationList(combos As List(Of ARMO_Combination),
                                       ctxKeywords As List(Of UInteger),
                                       pm As PluginManager,
                                       result As CombinationResolution)
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

        ' Selection: build the applicable set in record declaration order.
        Dim applicable As New List(Of ARMO_Combination)
        For Each combo In combos
            If combo Is Nothing Then Continue For
            Dim isApplicable As Boolean = combo.IsDefault
            If Not isApplicable AndAlso combo.Keywords IsNot Nothing AndAlso combo.Keywords.Count > 0 _
               AndAlso ctxKeywords IsNot Nothing AndAlso ctxKeywords.Count > 0 Then
                For Each kw In combo.Keywords
                    If ctxKeywords.Contains(kw) Then
                        isApplicable = True
                        Exit For
                    End If
                Next
            End If
            If isApplicable Then applicable.Add(combo)
        Next

        ' Defensive fallback: if NOTHING is applicable but combos exist, take the first.
        ' Engine never leaves a record without at least one combination applied when OBTE present.
        If applicable.Count = 0 Then
            For Each combo In combos
                If combo IsNot Nothing Then
                    applicable.Add(combo)
                    Exit For
                End If
            Next
        End If

        If applicable.Count = 0 Then Return

        result.AppliedCombinations.AddRange(applicable)

        ' Slot-by-slot OMOD merge.
        '   slotMap          : AttachPoint FormID → most-recently-seen OMOD for that slot
        '   unslottedOmods   : OMODs with AttachPointFormID = 0 (don't compete; all stack)
        '   visitedOmods     : anti-cycle set for OMOD.Includes recursion
        Dim slotMap As New Dictionary(Of UInteger, OMOD_Data)
        Dim unslottedOmods As New List(Of OMOD_Data)
        Dim visitedOmods As New HashSet(Of UInteger)

        For Each combo In applicable
            ' DirectProperties — inline on combination, no slot concept (all stack).
            If combo.Properties IsNot Nothing Then
                For Each prop In combo.Properties
                    result.DirectProperties.Add(prop)
                Next
            End If
            ' Includes — walked recursively into the slot/unslotted buckets.
            If combo.Includes IsNot Nothing Then
                For Each inc In combo.Includes
                    If inc Is Nothing OrElse inc.ModFormID = 0UI Then Continue For
                    ExpandOmodIntoSlots(inc.ModFormID, pm, visitedOmods, slotMap, unslottedOmods)
                Next
            End If
        Next

        ' Emit slotted OMODs first (deterministic order: insertion order of Dictionary keys),
        ' then unslotted. The applier doesn't depend on this order semantically — both lists
        ' walk linearly applying mutations to shape materials.
        For Each kv In slotMap
            result.IncludedOmods.Add(kv.Value)
        Next
        result.IncludedOmods.AddRange(unslottedOmods)
    End Sub

    ''' <summary>Slot-aware OMOD expansion. The OMOD's own AttachPointFormID decides which
    ''' bucket it lands in (slot last-wins or unslotted stack). Sub-OMODs from OMOD.Includes
    ''' inherit the same bucketing rule based on THEIR own AttachPointFormID, not the parent's.</summary>
    Private Sub ExpandOmodIntoSlots(omodFid As UInteger,
                                    pm As PluginManager,
                                    visited As HashSet(Of UInteger),
                                    slotMap As Dictionary(Of UInteger, OMOD_Data),
                                    unslotted As List(Of OMOD_Data))
        If omodFid = 0UI Then Return
        If Not visited.Add(omodFid) Then Return ' cycle / already expanded

        Dim rec = pm.GetRecord(omodFid)
        If rec Is Nothing OrElse rec.Header.Signature <> "OMOD" Then Return

        Dim omod = CraftingRecordParsers.ParseOMOD(rec, pm)

        If omod.AttachPointFormID = 0UI Then
            unslotted.Add(omod)
        Else
            slotMap(omod.AttachPointFormID) = omod ' last-wins per slot
        End If

        If omod.Includes IsNot Nothing Then
            For Each inc In omod.Includes
                If inc Is Nothing OrElse inc.ModFormID = 0UI Then Continue For
                ExpandOmodIntoSlots(inc.ModFormID, pm, visited, slotMap, unslotted)
            Next
        End If
    End Sub

End Module
