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

End Module
