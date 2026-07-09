''' <summary>Canonical FO4 biped-object slot bitmasks (the BOD2/BODT flag bits), consolidated
''' here so MainForm and every render/resolver site share ONE definition instead of each
''' re-declaring or inlining the same hex literal.
'''
''' Source spec (format, NOT game data): wbDefinitionsFO4.pas:3745-3778, wbBipedObjectFlags.
''' Convention used throughout: bit (N - 30) = biped slot N. So bit 0 = slot 30 (Hair Top),
''' bit 2 = slot 32 (FaceGen Head), bit 16 = slot 46 (Headband), bit 30 = slot 60 (Pipboy).
''' This is the SAME (N-30) bit convention that FO4_Base_Library uses when it derives slot
''' masks from partitions (BSTriShapeGeometry / RecordParsers); the two are kept conceptually
''' aligned by this comment. Base_Library's internal convention is NOT modified here — this
''' module only holds the NPC_Manager-side named constants.
'''
''' App-internal (NPC_Manager only). Only the bits actually used for head-part occlusion, the
''' headwear mask and Pipboy handling are named; body/hand slots (33/34/35) are handled
''' implicitly by the "outfit wins over skin on same slot" loop in SelectWinningCandidates.</summary>
Public Module BipedSlots

    Public Const SlotBitHairTop As UInteger = &H1UI         ' Slot 30 - Hair Top      (sombreros, gorros, cualquier headwear)
    Public Const SlotBitHairLong As UInteger = &H2UI        ' Slot 31 - Hair Long     (cascos que cubren el largo del pelo)
    Public Const SlotBitFaceGenHead As UInteger = &H4UI     ' Slot 32 - FaceGen Head  (casco integral / vault helmet — cubre LA CARA entera)
    Public Const SlotBitHeadband As UInteger = &H10000UI    ' Slot 46 - Headband      (bandana / hairband forehead, no cubre cara)
    Public Const SlotBitEyes As UInteger = &H20000UI        ' Slot 47 - Eyes          (glasses, goggles)
    Public Const SlotBitBeard As UInteger = &H40000UI       ' Slot 48 - Beard         (algo equipable que pisa la zona barba)
    Public Const SlotBitMouth As UInteger = &H80000UI       ' Slot 49 - Mouth         (bandana, máscara quirúrgica, gas mask boca)
    Public Const SlotBitNeck As UInteger = &H100000UI       ' Slot 50 - Neck          (bandana cuello, collar, bufanda)
    Public Const SlotBitRing As UInteger = &H200000UI       ' Slot 51 - Ring          (anillo — body, va en la mano)
    Public Const SlotBitScalp As UInteger = &H400000UI      ' Slot 52 - Scalp         (overlay cabeza/cuello — body, no prenda)
    Public Const SlotBitALArm As UInteger = &H1000UI        ' Slot 42 - [A] L Arm     (over-armor antebrazo izquierdo — bracer, PA L Arm)
    Public Const SlotBitPipboy As UInteger = &H40000000UI   ' Slot 60 - Pipboy        (atado a la muñeca/antebrazo izquierdo)

    ''' <summary>Máscara unificada de bits "headwear": prenda de cabeza/cara/CUELLO. La consume
    ''' ClassifyShapeCategory para la categoría Headwear, que el toggle "Render headwear" oculta.
    ''' Slots 30-32 (HairTop/HairLong/FaceGenHead) + 46-50 (Headband/Eyes/Beard/Mouth/NECK). Las
    ''' prendas de CUELLO (slot 50 — bandanas/pañuelos, p.ej. AA_Dog_Hankie 0x100000) DEBEN ocultarse
    ''' con el toggle "Render headwear", por eso Neck SÍ va acá (requisito del usuario; verificado en
    ''' debug: BandanaM_faceBones cat=Headwear hide=True con renderHeadwear=False). Ring (51) y Scalp
    ''' (52) NO van acá: son geometría de body, no prenda.</summary>
    Public Const HEADWEAR_MASK As UInteger = SlotBitHairTop Or SlotBitHairLong Or SlotBitFaceGenHead Or
                                             SlotBitHeadband Or SlotBitEyes Or SlotBitBeard Or SlotBitMouth Or
                                             SlotBitNeck

    ''' <summary>Pipboy slot-60 bit, alias of <see cref="SlotBitPipboy"/>. Kept as a second name
    ''' because the slot-conflict / occlusion code historically referred to it as SLOT_PIPBOY;
    ''' both point at the same &amp;H40000000UI bit.</summary>
    Public Const SLOT_PIPBOY As UInteger = SlotBitPipboy

    ' ════════════════════════════════════════════════════════════════════════════════════════════
    ' ⭐ TABLA AUTORITATIVA de slots por juego — NO heurística. Fuente: los NOMBRES de los biped-object
    ' flags de xEdit (wbBipedObjectFlags), leídos de TES5Edit\Core\ verbatim:
    '   FO4 → wbDefinitionsFO4.pas:3745-3778   ·   Skyrim → wbDefinitionsTES5.pas:2590-2622
    ' Las tablas NO salen de las RACE (la RACE sólo REFERENCIA slots vía sus occlusion biped objects
    ' Head/Hair/Body — reference_sse_engine_occlusion_re); los slots los define el formato del juego.
    ' Cada slot cae en una REGIÓN según su nombre declarado: Hair/Head/Circlet/Ears→Headwear,
    ' Body/Feet/Calves→Body, Hands/Forearms→Hands, [U]→Under, [A]→Over, Amulet/Ring/Shield→Accessory.
    ' (FO4 conserva 2 agrupaciones de la app documentadas: Ring→Hands "va en la mano", Scalp→Body overlay.)
    ' ════════════════════════════════════════════════════════════════════════════════════════════
    Public Enum BipedRegion
        None = 0
        Headwear    ' cabeza/pelo/cara — Render headwear, oculta head-parts
        Body        ' torso/piernas/pies — piel de cuerpo
        Hands       ' manos/antebrazos — piel de manos
        Under       ' FO4 [U] underlayer
        Over        ' FO4 [A] over-armor / Pipboy device
        Accessory   ' amulet/ring/shield/tail — sin oclusión de piel
    End Enum

    ' Índice = slot − 30 (bit 0..31). Sourced de xEdit (ver cita arriba).
    Private ReadOnly _fo4Regions As BipedRegion() = BuildFo4Regions()
    Private ReadOnly _sseRegions As BipedRegion() = BuildSseRegions()

    Private Function BuildFo4Regions() As BipedRegion()
        Dim r(31) As BipedRegion   ' wbDefinitionsFO4.pas:3745-3778
        r(0) = BipedRegion.Headwear  ' 30 Hair Top
        r(1) = BipedRegion.Headwear  ' 31 Hair Long
        r(2) = BipedRegion.Headwear  ' 32 FaceGen Head
        r(3) = BipedRegion.Body      ' 33 BODY
        r(4) = BipedRegion.Hands     ' 34 L Hand
        r(5) = BipedRegion.Hands     ' 35 R Hand
        r(6) = BipedRegion.Under     ' 36 [U] Torso
        r(7) = BipedRegion.Under     ' 37 [U] L Arm
        r(8) = BipedRegion.Under     ' 38 [U] R Arm
        r(9) = BipedRegion.Under     ' 39 [U] L Leg
        r(10) = BipedRegion.Under    ' 40 [U] R Leg
        r(11) = BipedRegion.Over     ' 41 [A] Torso
        r(12) = BipedRegion.Over     ' 42 [A] L Arm
        r(13) = BipedRegion.Over     ' 43 [A] R Arm
        r(14) = BipedRegion.Over     ' 44 [A] L Leg
        r(15) = BipedRegion.Over     ' 45 [A] R Leg
        r(16) = BipedRegion.Headwear ' 46 Headband
        r(17) = BipedRegion.Headwear ' 47 Eyes
        r(18) = BipedRegion.Headwear ' 48 Beard
        r(19) = BipedRegion.Headwear ' 49 Mouth
        r(20) = BipedRegion.Headwear ' 50 Neck
        r(21) = BipedRegion.Hands    ' 51 Ring (app: va EN la mano)
        r(22) = BipedRegion.Body     ' 52 Scalp (app: overlay de body)
        ' 53-59 Decapitation/Unnamed/Shield → None ; 60 Pipboy manejado aparte (Over gated Outfit)
        Return r
    End Function

    Private Function BuildSseRegions() As BipedRegion()
        Dim r(31) As BipedRegion   ' wbDefinitionsTES5.pas:2590-2622
        r(0) = BipedRegion.Headwear   ' 30 Head
        r(1) = BipedRegion.Headwear   ' 31 Hair
        r(2) = BipedRegion.Body       ' 32 Body
        r(3) = BipedRegion.Hands      ' 33 Hands
        r(4) = BipedRegion.Hands      ' 34 Forearms
        r(5) = BipedRegion.Accessory  ' 35 Amulet
        r(6) = BipedRegion.Accessory  ' 36 Ring
        r(7) = BipedRegion.Body       ' 37 Feet
        r(8) = BipedRegion.Body       ' 38 Calves
        r(9) = BipedRegion.Accessory  ' 39 Shield
        r(10) = BipedRegion.Accessory ' 40 Tail
        r(11) = BipedRegion.Headwear  ' 41 LongHair
        r(12) = BipedRegion.Headwear  ' 42 Circlet
        r(13) = BipedRegion.Headwear  ' 43 Ears
        ' 44-49 Unnamed, 50 DecapitateHead, 51 Decapitate, 52-60 Unnamed/mod, 61 FX01 → None
        Return r
    End Function

    ''' <summary>Región de cada slot para el juego actual (índice = slot−30). Skyrim vs FO4.</summary>
    Public Function RegionsForGame() As BipedRegion()
        Return If(Config_App.Current.Game = Config_App.Game_Enum.Skyrim, _sseRegions, _fo4Regions)
    End Function

    ''' <summary>Máscara (bits slot−30) de todos los slots de una región, para el juego actual.</summary>
    Public Function RegionMask(region As BipedRegion) As UInteger
        Dim regions = RegionsForGame()
        Dim m As UInteger = 0UI
        For b = 0 To 31
            If regions(b) = region Then m = m Or (1UI << b)
        Next
        Return m
    End Function

    ''' <summary>Headwear mask game-aware = <see cref="RegionMask"/>(Headwear). FO4 conserva
    ''' <see cref="HEADWEAR_MASK"/>; Skyrim = 30/31/41/42/43 (NO slot 32=cuerpo).</summary>
    Public Function HeadwearMaskForGame() As UInteger
        Return RegionMask(BipedRegion.Headwear)
    End Function

End Module
