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

        ''' <summary>Parallel list of host-publisher FormIDs (1:1 with <see cref="IncludedOmods"/>).
        ''' The host of a chunk is the OMOD that introduced the AP this chunk consumes into the
        ''' AP-pool — i.e. the chunk that publishes the socket the consumer mounts on. 0 = the
        ''' AP was in <c>initialApPool</c> (NPC.APPR seed), so the host is the actor/skeleton
        ''' root (no upstream chunk publisher).
        '''
        ''' Used for host-scoped socket resolution: a consumer's socket transform is looked up
        ''' in the host's published BSConnectPoint::Parents, walking the host chain up to root.
        ''' Replaces the flat global <c>SocketsDictionary</c> resolution which conflated
        ''' STATIC skeleton sockets and per-chunk publisher sockets into a single namespace.</summary>
        Public IncludedOmodHostFormID As New List(Of UInteger)
        ''' <summary>Parallel list of host-publisher ApIdx (1:1 with <see cref="IncludedOmods"/>).
        ''' Junto con <see cref="IncludedOmodHostFormID"/> identifica el asset host. Solo
        ''' para logging legible: la identidad runtime real es <see cref="IncludedOmodHostInstanceOrdinal"/>.
        ''' 0 (con HostFormID=0) = host es el actor/skeleton root.</summary>
        Public IncludedOmodHostApIdx As New List(Of Byte)

        ''' <summary>Parallel list de InstanceOrdinal monotónico (1:1 con <see cref="IncludedOmods"/>).
        ''' IDENTIDAD RUNTIME REAL del candidate. Asignado en expand-time (CollectOmodCandidate)
        ''' al emitir cada leaf — antes de cualquier dedup. Esto permite que el mismo OMOD asset
        ''' aparezca múltiples veces en el árbol de mounting (Bethesda reutiliza assets) sin
        ''' colapsar identidades. Ordinal 0 reservado para "skeleton root" (no usado por
        ''' candidates emitidos, solo como sentinel en host references). Ordinals reales ≥ 1.</summary>
        Public IncludedOmodInstanceOrdinal As New List(Of Integer)

        ''' <summary>Parallel list de host InstanceOrdinal (1:1 con <see cref="IncludedOmods"/>).
        ''' Apunta a la INSTANCIA del host (no al asset). 0 = host es skeleton root.
        ''' Junto con <see cref="IncludedOmodInstanceOrdinal"/> reemplaza la tuple (FormID, ApIdx)
        ''' como identidad. Inmune a colisiones por reúso de asset bajo hosts distintos.</summary>
        Public IncludedOmodHostInstanceOrdinal As New List(Of Integer)
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
        ' ARMO.APPR seedea el AP-pool inicial per wbDefinitionsFO4.pas:6206 (wbAPPR aparece en
        ' record ARMO después de TNAM y antes de wbObjectTemplate). Comentario previo decía
        ' "ARMO doesn't have an actor-level APPR" — incorrecto. Caso vivo: Armor_MiningHelmet
        ' declara ap_PowerArmor_HeadMod aquí; sin el seed, Helmet_Mining_Mod queda rechazado
        ' por pool empty y no se emite chunk biped. Children OMODs aceptados extienden el pool
        ' vía sus propias AttachParentSlots — mismo modelo que NPC.APPR/RACE.APPR.
        Dim initialPool As New HashSet(Of UInteger)
        If armo.AttachParentSlotFormIDs IsNot Nothing Then
            For Each fid In armo.AttachParentSlotFormIDs
                If fid <> 0UI Then initialPool.Add(fid)
            Next
        End If
        ResolveCombinationList(armo.Combinations, ctxKeywords, pm, result, initialPool)
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
        ' [INSTANCE-IDENTITY] Identidad runtime asignada al emit-time en CollectOmodCandidate
        ' (no al accept-time). Permite que el mismo OMOD asset aparezca múltiples veces en el
        ' árbol bajo hosts distintos sin colapsarse — caso teórico que Bethesda puede ejercer
        ' al reutilizar mods. La invariante "(FormID, ApIdx) único" del visited-set permanente
        ' ya no se asume; sustituida por DFS path tracking (stack) que solo previene CICLOS
        ' (un mismo nodo dentro del path actual), no expand-time dedup.
        '
        ' Ordinal counter: monotónico, incremental en cada CollectOmodCandidate exitoso.
        ' Ordinal 0 reservado para "skeleton root" (sentinel). Candidates reciben ≥ 1.
        Dim instanceOrdinalCounter As Integer = 0
        Dim pathStack As New HashSet(Of (UInteger, Byte))
        Dim candidates As New List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer))
        Dim unslottedOmods As New List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer))

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
                    CollectOmodCandidate(inc.ModFormID, inc.AttachPointIndex, inc.DontUseAll, pm, pathStack, candidates, unslottedOmods, instanceOrdinalCounter)
                Next
            End If
        Next

        ' Phase 2: AP-pool filter.
        Dim apPool As New HashSet(Of UInteger)(initialApPool)
        Dim apPoolBeforeStr = String.Join(",", apPool.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pm) & ")"))
        Logger.LogLazy(Function() $"[OBTE-POOL-INIT] initial pool({apPool.Count}) = [{apPoolBeforeStr}]")

        Dim accepted As New List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer, HostInstanceOrdinal As Integer, HostFormID As UInteger, HostApIdx As Byte))
        ' [APPROVIDER-PER-INSTANCE] Cada apFid puede tener MÚLTIPLES providers (instancias
        ' distintas que declaran el mismo AP en sus AttachParentSlots). Vanilla scope: 1
        ' provider por apFid (un NPC tiene UN torso). Mod scope teórico: ≥2 providers para
        ' un híbrido con dos torsos que ambos publican mismas AP names. Preservamos todos.
        ' Decisión de policy: FIRST-WINS en accept-time (la primera entry de la list es el
        ' host efectivo). Si list.Count > 1: log [OBTE-AP-MULTI-PROVIDER] flagging el caso
        ' para investigación futura. Sin evidencia engine de cómo Bethesda resuelve ambigüedad,
        ' first-wins es la única regla defendible.
        Dim apProvider As New Dictionary(Of UInteger, List(Of (HostOrdinal As Integer, HostFid As UInteger, HostApIdx As Byte)))
        For Each seedAp In initialApPool
            If Not apProvider.ContainsKey(seedAp) Then
                apProvider(seedAp) = New List(Of (Integer, UInteger, Byte)) From {(0, 0UI, CByte(0))}
            End If
        Next
        Dim pending As New List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer))(candidates)
        Dim iterations As Integer = 0
        Const maxIter As Integer = 16
        Do
            iterations += 1
            Dim changed As Boolean = False
            Dim stillPending As New List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer))
            For Each cand In pending
                If apPool.Contains(cand.Omod.AttachPointFormID) Then
                    ' Resolve host via APIDX-MATCH (preferido) → fallback FIRST-WINS.
                    ' Policy: la convención canónica Bethesda para desambiguar instancias
                    ' espaciales del mismo OMOD usa apIdx (downstream lo aplicamos en
                    ' ResolveMountSocket para "P-base|apIdx"). Aplicar la misma convención
                    ' en el provider resolution mantiene consistencia. Si multi-provider
                    ' Y consumer.ApIdx matchea EXACTAMENTE uno: usar ese (caso Codsworth
                    ' Mr Handy arms — cada arm mod va al brazo de su apIdx). Si no hay
                    ' match O hay múltiples matches: log diagnóstico + first-wins.
                    Dim hostList As List(Of (HostOrdinal As Integer, HostFid As UInteger, HostApIdx As Byte)) = Nothing
                    Dim hostOrd As Integer = 0
                    Dim hostFid As UInteger = 0UI
                    Dim hostApIdxResolved As Byte = 0
                    If apProvider.TryGetValue(cand.Omod.AttachPointFormID, hostList) AndAlso hostList.Count > 0 Then
                        If hostList.Count = 1 Then
                            ' Single provider — sin ambigüedad.
                            Dim only0 = hostList(0)
                            hostOrd = only0.HostOrdinal
                            hostFid = only0.HostFid
                            hostApIdxResolved = only0.HostApIdx
                        Else
                            ' Multi-provider. Match por apIdx primero.
                            Dim matches = hostList.Where(Function(p) p.HostApIdx = cand.ApIdx).ToList()
                            Dim apFidL = cand.Omod.AttachPointFormID, candL = cand, pmL_multi = pm, listCount = hostList.Count
                            Dim providersStr = String.Join(",", hostList.Select(Function(p) $"(ord={p.HostOrdinal},0x{p.HostFid:X8},apIdx={p.HostApIdx})"))
                            If matches.Count = 1 Then
                                ' apIdx-match unique — caso limpio (Codsworth Mr Handy mods).
                                Dim m0 = matches(0)
                                hostOrd = m0.HostOrdinal
                                hostFid = m0.HostFid
                                hostApIdxResolved = m0.HostApIdx
                                Dim hOrdL_log = hostOrd, hFidL_log = hostFid
                                Logger.LogLazy(Function() $"[OBTE-AP-MULTI-PROVIDER] cand={candL.Omod.EditorID}(0x{candL.Omod.FormID:X8}) ord={candL.InstanceOrdinal} apIdx={candL.ApIdx} ap=0x{apFidL:X8}({KywdEditorIdSafe(apFidL, pmL_multi)}) providers({listCount})=[{providersStr}] → apIdx-match unique ord={hOrdL_log} (0x{hFidL_log:X8})")
                            ElseIf matches.Count > 1 Then
                                ' Multi-provider Y multi-match — ambigüedad real, first-wins entre matches.
                                Dim m0 = matches(0)
                                hostOrd = m0.HostOrdinal
                                hostFid = m0.HostFid
                                hostApIdxResolved = m0.HostApIdx
                                Dim mCount = matches.Count, hOrdL_log = hostOrd
                                Logger.LogLazy(Function() $"[OBTE-AP-AMBIGUOUS-MATCH] cand={candL.Omod.EditorID}(0x{candL.Omod.FormID:X8}) ord={candL.InstanceOrdinal} apIdx={candL.ApIdx} ap=0x{apFidL:X8}({KywdEditorIdSafe(apFidL, pmL_multi)}) providers({listCount})=[{providersStr}] {mCount} apIdx matches → first-wins ord={hOrdL_log}")
                            Else
                                ' No apIdx match — fallback first-wins entre todos.
                                Dim first0 = hostList(0)
                                hostOrd = first0.HostOrdinal
                                hostFid = first0.HostFid
                                hostApIdxResolved = first0.HostApIdx
                                Dim hOrdL_log = hostOrd
                                Logger.LogLazy(Function() $"[OBTE-AP-NO-IDX-MATCH] cand={candL.Omod.EditorID}(0x{candL.Omod.FormID:X8}) ord={candL.InstanceOrdinal} apIdx={candL.ApIdx} ap=0x{apFidL:X8}({KywdEditorIdSafe(apFidL, pmL_multi)}) providers({listCount})=[{providersStr}] no provider with apIdx={candL.ApIdx} → fallback first-wins ord={hOrdL_log}")
                            End If
                        End If
                    End If
                    accepted.Add((cand.Omod, cand.ApIdx, cand.InstanceOrdinal, hostOrd, hostFid, hostApIdxResolved))
                    changed = True
                    If cand.Omod.AttachParentSlotFormIDs IsNot Nothing Then
                        For Each fid In cand.Omod.AttachParentSlotFormIDs
                            If fid = 0UI Then Continue For
                            apPool.Add(fid)
                            ' Append a la list (no overwrite). Permite multiplicidad real
                            ' visible para apIdx-match en la resolución.
                            Dim plist As List(Of (HostOrdinal As Integer, HostFid As UInteger, HostApIdx As Byte)) = Nothing
                            If Not apProvider.TryGetValue(fid, plist) Then
                                plist = New List(Of (Integer, UInteger, Byte))
                                apProvider(fid) = plist
                            End If
                            plist.Add((cand.InstanceOrdinal, cand.Omod.FormID, cand.ApIdx))
                        Next
                    End If
                    Dim cL = cand, pmL = pm, hostOrdL = hostOrd, hostFidL = hostFid, hostApIdxL = hostApIdxResolved
                    Dim addedApStr = If(cL.Omod.AttachParentSlotFormIDs Is Nothing, "(none)", String.Join(",", cL.Omod.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pmL) & ")")))
                    Logger.LogLazy(Function() $"[OBTE-POOL-ACCEPT] omod={cL.Omod.EditorID}(0x{cL.Omod.FormID:X8}) ord={cL.InstanceOrdinal} ap=0x{cL.Omod.AttachPointFormID:X8}({KywdEditorIdSafe(cL.Omod.AttachPointFormID, pmL)}) host=(ord={hostOrdL},0x{hostFidL:X8},apIdx={hostApIdxL}) addedAPs=[{addedApStr}]")
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
            result.IncludedOmodInstanceOrdinal.Add(entry.InstanceOrdinal)
            result.IncludedOmodHostInstanceOrdinal.Add(entry.HostInstanceOrdinal)
            result.IncludedOmodHostFormID.Add(entry.HostFormID)
            result.IncludedOmodHostApIdx.Add(entry.HostApIdx)
        Next
        For Each entry In unslottedOmods
            result.IncludedOmods.Add(entry.Omod)
            result.IncludedOmodApIdx.Add(entry.ApIdx)
            result.IncludedOmodInstanceOrdinal.Add(entry.InstanceOrdinal)
            ' Unslotted = container properties (color overlays, MSWP). No mount, no host concept.
            result.IncludedOmodHostInstanceOrdinal.Add(0)
            result.IncludedOmodHostFormID.Add(0UI)
            result.IncludedOmodHostApIdx.Add(CByte(0))
        Next
    End Sub

    ''' <summary>Collect an OMOD candidate (or recurse into its container).
    '''   - OMOD with AttachPoint != 0 → leaf candidate; added to candidates list with a
    '''     fresh InstanceOrdinal (incrementing the counter). Permite que el mismo OMOD asset
    '''     aparezca múltiples veces en el árbol bajo paths distintos sin colapsar identidad.
    '''   - OMOD without AttachPoint (container) → not emitted itself; its Includes are walked
    '''     according to <paramref name="dontUseAll"/>:
    '''       True  → random-pick 1 Include and recurse only on it (modcol_* mutex variants).
    '''       False → walk all Includes.
    '''
    ''' Cycle prevention: stack-based path tracking (push on entry, pop on exit). NO dedup
    ''' permanente — el mismo (FormID, ApIdx) puede aparecer DOS veces si llega via paths
    ''' distintos (no cíclicos). Reemplaza el visitedOmods HashSet permanente que colapsaba
    ''' identidades expandidas desde árboles distintos.</summary>
    Private Sub CollectOmodCandidate(omodFid As UInteger,
                                      apIdx As Byte,
                                      dontUseAll As Boolean,
                                      pm As PluginManager,
                                      pathStack As HashSet(Of (UInteger, Byte)),
                                      candidates As List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer)),
                                      unslotted As List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer)),
                                      ByRef instanceOrdinalCounter As Integer)
        If omodFid = 0UI Then Return
        Dim pathKey = (omodFid, apIdx)
        If pathStack.Contains(pathKey) Then
            ' Cycle detection — el mismo (fid, apIdx) ya está en el path actual del DFS.
            ' Skip silente; cycle real.
            Dim fl = omodFid, al = apIdx
            Logger.LogLazy(Function() $"[OBTE-CYCLE] omod=0x{fl:X8} apIdx={al} ya en path actual — skip ciclo")
            Return
        End If
        pathStack.Add(pathKey)
        Try
            Dim rec = pm.GetRecord(omodFid)
            If rec Is Nothing OrElse rec.Header.Signature <> "OMOD" Then Return

            Dim omod = CraftingRecordParsers.ParseOMOD(rec, pm)

            If omod.AttachPointFormID <> 0UI Then
                ' Leaf candidate — has its own AP, will compete in the AP-pool filter.
                instanceOrdinalCounter += 1
                Dim ord = instanceOrdinalCounter
                candidates.Add((omod, apIdx, ord))
                Dim ol = omod, fl = omodFid, al = apIdx, pmL = pm, ordL = ord
                Dim parentSlotsStr = If(ol.AttachParentSlotFormIDs Is Nothing, "(none)", String.Join(",", ol.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & KywdEditorIdSafe(f, pmL) & ")")))
                Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol.EditorID}(0x{fl:X8}) ord={ordL} ftype={ol.FormTypeSignature} ap=0x{ol.AttachPointFormID:X8}({KywdEditorIdSafe(ol.AttachPointFormID, pmL)}) apIdx={al} model='{ol.ModelPath}' parentSlots=[{parentSlotsStr}]")
                ' Recurse into the leaf's own Includes (some chunks expose sub-OMODs that
                ' depend on their AP being available — handled by AP-pool filter naturally).
                If omod.Includes IsNot Nothing AndAlso omod.Includes.Count > 0 Then
                    RecurseContainerIncludes(omod, dontUseAll, pm, pathStack, candidates, unslotted, instanceOrdinalCounter)
                End If
                Return
            End If

            ' Container (AP=0). DirectProperties of containers (color overlays, MSWP) go to the
            ' unslotted bucket and stack — they don't go through the AP filter.
            If omod.Properties IsNot Nothing AndAlso omod.Properties.Count > 0 AndAlso (omod.Includes Is Nothing OrElse omod.Includes.Count = 0) Then
                instanceOrdinalCounter += 1
                Dim ordU = instanceOrdinalCounter
                unslotted.Add((omod, apIdx, ordU))
                Dim ol = omod, fl = omodFid, ordUL = ordU
                Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol.EditorID}(0x{fl:X8}) ord={ordUL} ap=0 (container, properties-only) → unslotted bucket")
                Return
            End If

            Dim ol2 = omod, fl2 = omodFid, dontL = dontUseAll
            Logger.LogLazy(Function() $"[OBTE-CAND] omod={ol2.EditorID}(0x{fl2:X8}) ap=0 (container, recurse children, dontUseAll={dontL})")

            ' Recurse children per dontUseAll.
            RecurseContainerIncludes(omod, dontUseAll, pm, pathStack, candidates, unslotted, instanceOrdinalCounter)
        Finally
            pathStack.Remove(pathKey)
        End Try
    End Sub

    ''' <summary>Walk the Includes of an OMOD container according to the dontUseAll flag of
    ''' the parent Include that referenced this container:
    '''   True  → random-pick 1 Include (mutex variant for modcol_*).
    '''   False → walk all Includes.</summary>
    Private Sub RecurseContainerIncludes(omod As OMOD_Data,
                                          dontUseAll As Boolean,
                                          pm As PluginManager,
                                          pathStack As HashSet(Of (UInteger, Byte)),
                                          candidates As List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer)),
                                          unslotted As List(Of (Omod As OMOD_Data, ApIdx As Byte, InstanceOrdinal As Integer)),
                                          ByRef instanceOrdinalCounter As Integer)
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
            CollectOmodCandidate(pick.ModFormID, 0, pick.DontUseAll, pm, pathStack, candidates, unslotted, instanceOrdinalCounter)
        Else
            For Each inc In validIncludes
                CollectOmodCandidate(inc.ModFormID, 0, inc.DontUseAll, pm, pathStack, candidates, unslotted, instanceOrdinalCounter)
            Next
        End If
    End Sub


End Module
