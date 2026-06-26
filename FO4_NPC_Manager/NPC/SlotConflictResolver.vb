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

    ' FO4 biped slot bitmasks (format spec, wbDefinitionsFO4.pas:3745-3778 — NOT game data).
    Private Const BODY_MASK As UInteger = &H8UI            ' bit 3  — slot 33 BODY
    Private ReadOnly U_MASK As UInteger = BuildRange(6, 10)   ' bits 6-10  — slots 36-40 [U] underlayer
    Private ReadOnly A_MASK As UInteger = BuildRange(11, 15)  ' bits 11-15 — slots 41-45 [A] over-armor
    ' SLOT_PIPBOY (bit 30 — slot 60 Pipboy) now lives in the shared BipedSlots module.

    Private Function BuildRange(loBit As Integer, hiBit As Integer) As UInteger
        Dim m As UInteger = 0UI
        For b = loBit To hiBit
            m = m Or (1UI << b)
        Next
        Return m
    End Function

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
            Dim conflictMask = m And Not BipedSlots.SLOT_PIPBOY
            Dim occupiedForCheck = occupied And Not BipedSlots.SLOT_PIPBOY
            If (conflictMask And occupiedForCheck) <> 0UI Then res.Losers.Add(it) : Continue For
            occupied = occupied Or m
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
