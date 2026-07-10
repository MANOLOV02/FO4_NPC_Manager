Imports System.Linq

''' <summary>Reusable FO4 biped slot-conflict resolution — the engine rule that decides which
''' equipped pieces survive when their slot masks overlap. Extracted from MainForm.SelectWinningCandidates
''' so the SAME rules drive both the render path (resolving an NPC's loadout) and the Edit Outfit
''' "Create" tab (resolving the user-assembled piece list). App-internal (NPC_Manager only — two
''' callers, one app) per feedback_always_correct_path_no_optional_debt.
'''
''' Rules implemented (see arch_slot_conflict_resolution memory):
'''   • Pass 1a — extended underarmor: a piece declaring an underlayer bit (BODY bit 3 or [U] bits
'''     6-10) AND an [A] bit (11-15) reserves its [A] bits and shields its whole mask; later pure-[A]
'''     pieces that touch those bits are discarded (Bridget/DCGuard rule). Processed ascending Order.
'''   • Pass 1b — atomic mutex, last-equipped wins: iterate descending Order; any-bit overlap with an
'''     already-claimed bit discards the piece WHOLE (not partial). Slot 60 (Pipboy) is stripped from the
'''     conflict check — it is coexist-by-design (the engine never mutexes on it; ~all body outfits set it
'''     for the 60/160 forearm swap, and the Pipboy device equips skip-conflict), but still occupies. There
'''     is NO Pipboy↔[A]LArm mutex (removed 2026-06-22 — different bits, the engine coexists them).
''' Skin / slotless pieces do NOT belong here — the caller handles those separately (skin always
''' accepted, head parts occlusion-tested). Items passed with SlotMask=0 are returned as winners
''' untouched (no conflict), so the resolver is safe to call on a mixed list.</summary>
Public Module SlotConflictResolver

    ' Biped slot region masks are now GAME-AWARE and sourced from the authoritative per-game slot→region
    ' table (BipedSlots.RegionMask, derived from xEdit wbBipedObjectFlags — NOT heuristic). Computed
    ' per-call in ResolveSlotWinners because the game can change at runtime. Skyrim has NO [U]/[A] layers
    ' (RegionMask(Under)/RegionMask(Over) = 0), so the extended-underarmor exception below is a no-op there;
    ' the byte-level engine RE (reference_sse_engine_occlusion_re Q4) also confirmed Skyrim strips no bit.

    ''' <summary>Outcome of a resolution: the kept pieces (Winners), the eliminated ones (Losers),
    ''' and the final occupied slot bitmask (so the render path can run head-part occlusion / skin
    ''' coverage checks against it without recomputing).</summary>
    Public Class SlotResolution(Of T)
        Public ReadOnly Winners As New List(Of T)
        Public ReadOnly Losers As New List(Of T)
        Public OccupiedSlots As UInteger
    End Class

    ''' <summary>Resolve slot conflicts over <paramref name="items"/>. <paramref name="slotMaskOf"/>
    ''' returns each item's BOD2/BODT slot mask; <paramref name="orderOf"/> returns its equip order
    ''' (ascending = earlier; descending order is "last equipped wins"). Winners are returned sorted
    ''' ascending by Order; the caller can re-sort if needed.</summary>
    Public Function ResolveSlotWinners(Of T)(items As IEnumerable(Of T),
                                             slotMaskOf As Func(Of T, UInteger),
                                             orderOf As Func(Of T, Integer)) As SlotResolution(Of T)
        Dim res As New SlotResolution(Of T)
        Dim list = items.ToList()

        ' Game-aware region masks from the authoritative xEdit slot table. Skyrim → Under/Over = 0,
        ' so the extended-underarmor exception (Pass 1a) is inert there; strip-60 (Pipboy coexist) is
        ' FO4-only (Skyrim slot 60 is a generic MOD slot; engine strips nothing — RE Q4).
        ' BODY_MASK stays the exact FO4 bit-3 const (slot 33): it only matters in the [A]-gated extended
        ' -underarmor check, which SSE never triggers (A_MASK=0), so this keeps FO4 byte-identical.
        Const BODY_MASK As UInteger = &H8UI
        Dim U_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Under)
        Dim A_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Over)
        Dim stripPipboy As Boolean = (Config_App.Current.Game = Config_App.Game_Enum.Fallout4)
        Dim isSse As Boolean = (Config_App.Current.Game = Config_App.Game_Enum.Skyrim)

        ' Materialize each item's slot mask exactly once: slotMaskOf is a caller-supplied delegate
        ' (BOD2/BODT lookup) and the original code re-invoked it per item across every pass.
        Dim maskOf As New Dictionary(Of T, UInteger)
        For Each it In list
            maskOf(it) = slotMaskOf(it)
        Next

        ' Slotless (mask=0) never conflict — accept verbatim, no slot contribution.
        res.Winners.AddRange(list.Where(Function(it) maskOf(it) = 0UI))
        Dim slotted = list.Where(Function(it) maskOf(it) <> 0UI).ToList()

        Dim occupied As UInteger = 0UI
        Dim reservedA As UInteger = 0UI
        Dim shielded As UInteger = 0UI

        ' Pass 1a — extended underarmors (underlayer + [A] in the same piece), ascending Order.
        Dim extended = slotted.Where(Function(it)
                                         Dim m = maskOf(it)
                                         Dim hasUnderlayer = (m And BODY_MASK) <> 0UI OrElse (m And U_MASK) <> 0UI
                                         Dim hasA = (m And A_MASK) <> 0UI
                                         Return hasUnderlayer AndAlso hasA
                                     End Function).OrderBy(orderOf).ToList()
        ' O(1) membership for the Pass 1b "not extended" filter (was List.Contains → O(n²)).
        Dim extendedSet As New HashSet(Of T)(extended)
        Dim acceptedExtended As New List(Of T)
        For Each it In extended
            Dim m = maskOf(it)
            Dim freeBits = m And Not occupied
            If freeBits = 0UI Then
                res.Losers.Add(it)        ' fully overlapped by an earlier extended underarmor
                Continue For
            End If
            occupied = occupied Or freeBits
            shielded = shielded Or m
            reservedA = reservedA Or (m And A_MASK)
            acceptedExtended.Add(it)
        Next

        ' Pass 1b — atomic mutex, last-equipped wins (descending Order).
        Dim acceptedReverse As New List(Of T)
        For Each it In slotted.Where(Function(x) Not extendedSet.Contains(x)).OrderByDescending(orderOf)
            Dim m = maskOf(it)
            ' [A] bits reserved by an extended underarmor → discard whole (Bridget exception).
            If (m And reservedA) <> 0UI Then res.Losers.Add(it) : Continue For
            ' Bits shielded by an extended underarmor → not displaceable.
            If (m And shielded) <> 0UI Then res.Losers.Add(it) : Continue For
            ' Atomic any-bit overlap, last-equipped wins. Slot 60 (Pipboy) is stripped from the conflict
            ' check because it is COEXIST-BY-DESIGN, not a competitive slot (RE Fallout4.exe + ESM data,
            ' 2026-06-22): the engine never mutexes on slot 60. The only slot-60-ONLY item is the Pipboy
            ' DEVICE (PipboyAA = 0x40000000), which equips via a per-instance skip-conflict flag (key-0x25,
            ' resolver gate 0x14027FCC0); and ~every body outfit ALSO sets slot 60 (e.g. AAVTScientist =
            ' 0x40000008 = 60+33) to carry the forearm 60/160 Pipboy-accommodation swap. Those outfits also
            ' declare slot 33 (BODY), so two of them still mutex on 33 — only the Pipboy device's pure-60
            ' overlap is neutralised. So "slot 60 is not a conflict bit" == the engine's net behaviour for
            ' every reachable equip set. Slot 60 STILL flows into `occupied` below (the 60/160 segment swap
            ' downstream reads it). There is NO engine mutex between Pipboy(60) and [A] L Arm(42): different
            ' bits → SlotsOverlap=0 → they coexist (lab coats declare 60+[A]42 and worn with a Pipboy fine).
            ' ⭐ Conflict rule is GAME-SPECIFIC (byte-level RE of BOTH engines):
            '  • FO4 = atomic ANY-BIT last-equipped-wins (verified resolver 0x1409889C0): any shared bit
            '    unequips the older piece WHOLE. FO4 armor has clean, non-overlapping slots ([A] Torso vs
            '    [A] Legs), so this is correct.
            '  • Skyrim = KEEP ALL, drop none (verified SkyrimSE.exe): the equip path EquipObject 0x1406C9820
            '    does NO biped-slot arbitration, and outfits equip each ARMO sequentially via 0x1402E1B00 →
            '    EquipObject, so every piece survives regardless of BOD2 overlap. The only raw any-bit AND
            '    (SlotsOverlap 0x1401CCA90, single caller 0x1403BD5A2) is render-side display de-dup, NOT an
            '    inventory rule. This is what makes cuirass(32,34,38)+boots(37,38)+gloves(33,34) coexist —
            '    they share calves(38)/forearms(34) but the engine drops none. (An earlier any-bit rule
            '    eliminated the cuirass; claim-free-bits was a wrong guess — see reference_sse_engine_occlusion_re.)
            Dim isConflict As Boolean
            If isSse Then
                ' Skyrim resolves by SLOT (user directive 2026-07-09; the old "keep-all" drew BOTH of
                ' two same-slot hats). Pass 1b runs descending Order (last-equipped first), so each biped
                ' slot is owned by the last-equipped item that claims it. PER-SLOT, not FO4's whole-item
                ' any-bit: an item is dropped ONLY when EVERY slot it occupies was already claimed by a
                ' later item; if it keeps ≥1 free slot it still renders. Validated vs real Skyrim.esm BOD2:
                '   • two hats (both 0x2802 = slots 31/41/43) → the earlier keeps no slot → DROPPED (fix).
                '   • cuirass(0x114=32,34,38) + boots(0x180=37,38) share only calves-38 → each keeps its
                '     primary slot → BOTH survive (whole-item any-bit would have wrongly dropped the boots).
                ' This supersedes the byte-RE "keep-all" note in reference_sse_engine_occlusion_re.
                isConflict = ((m And (Not occupied)) = 0UI)
            Else
                Dim conflictMask = If(stripPipboy, m And Not BipedSlots.SLOT_PIPBOY, m)
                Dim occupiedForCheck = If(stripPipboy, occupied And Not BipedSlots.SLOT_PIPBOY, occupied)
                isConflict = ((conflictMask And occupiedForCheck) <> 0UI)
            End If
            If isConflict Then res.Losers.Add(it) : Continue For
            occupied = occupied Or m   ' still accumulate the covered-slot union (drives skin occlusion)
            acceptedReverse.Add(it)
        Next
        acceptedReverse.Reverse()   ' back to chronological (ascending) order

        res.Winners.AddRange(acceptedExtended)
        res.Winners.AddRange(acceptedReverse)
        res.OccupiedSlots = occupied
        ' Stable ascending-Order winners (render does its own final OrderBy; Tab 2 wants this order).
        Dim sortedWinners = res.Winners.OrderBy(orderOf).ToList()
        res.Winners.Clear()
        res.Winners.AddRange(sortedWinners)
        Return res
    End Function

End Module
