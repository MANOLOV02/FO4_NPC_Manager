Imports FO4_Base_Library

''' <summary>A per-field OVERRIDE of the scalar / list subrecords of a single NPC_ record, authored in the
''' NPC Editor and applied at Save time. The counterpart to the LooksMenu overlay (<see cref="LooksmenuLoader.
''' LooksmenuPreset"/>) for the record fields the overlay does NOT carry (Name, ACBS flags/level, identity
''' FormIDs, keywords, factions, inventory, object-template combinations).
'''
''' WHY a bag of OPTIONAL fields: the Save path re-parses each NPC fresh from the plugin and copies every
''' scalar/list field by reference from that parse (MainForm.CopyRoundTripOnlyFieldsFromRaw). This override is
''' applied by MainForm's ApplyNpcRecordOverride delegate JUST AFTER that copy (a delegate on
''' <see cref="NpcOverrideSaver.SaveContext"/>, called in BuildOverrideEntry Phase 1a), so the user's edit WINS
''' without touching the round-trip copy. Only fields the user actually changed are set (Nullable for scalars,
''' Nothing for lists) so an untouched subrecord round-trips verbatim from the source record.
'''
''' Accumulation: the editor MERGES into any existing override for the NPC (a second edit ORs-in its changes),
''' so <see cref="TraitsChanged"/> stays latched once set — otherwise a later edit whose snapshot already
''' reflects the first edit would drop the template-flag hook and the earlier Race/Voice/OBTS edit would stop
''' taking effect at runtime.</summary>
Public Class NpcRecordOverride

    ' --- Scalars (Nothing/no-value = not overridden). String "" is a valid (cleared) value, distinct from Nothing. ---
    Public Property FullName As String = Nothing            ' FULL
    Public Property ShortName As String = Nothing           ' SHRT
    Public Property AcbsFlags As UInteger? = Nothing        ' ACBS Flags (u32 @+0)
    Public Property XpValueOffset As Short? = Nothing       ' ACBS XP Value Offset (s16 @+4)
    Public Property Level As UShort? = Nothing              ' ACBS Level / LevelMult (u16 @+6, raw — union gated by 0x80)
    Public Property CalcMinLevel As UShort? = Nothing       ' ACBS Calc Min Level
    Public Property CalcMaxLevel As UShort? = Nothing       ' ACBS Calc Max Level
    Public Property DispositionBase As Short? = Nothing     ' ACBS Disposition Base (s16)
    Public Property TemplateFlags As UShort? = Nothing      ' ACBS/NPC Template Flags (u16) — reserved; editor leaves it unset
    Public Property RaceFormID As UInteger? = Nothing       ' RNAM
    Public Property VoiceFormID As UInteger? = Nothing      ' VTCK
    Public Property ClassFormID As UInteger? = Nothing      ' CNAM
    Public Property CombatStyleFormID As UInteger? = Nothing ' ZNAM

    ' --- Lists (Nothing = not overridden; a set list REPLACES the source list in full). ---
    Public Property Keywords As List(Of UInteger) = Nothing                        ' KWDA
    Public Property AttachParentSlots As List(Of UInteger) = Nothing               ' APPR (attach-point KYWD)
    Public Property Factions As List(Of NPC_FactionEntry) = Nothing                ' SNAM
    Public Property Inventory As List(Of NPC_InventoryItem) = Nothing              ' CNTO/COED
    Public Property Perks As List(Of NPC_PerkEntry) = Nothing                      ' PRKR (+PRKZ count)
    Public Property ActorEffects As List(Of UInteger) = Nothing                    ' SPLO (+SPCT count) → SPEL
    Public Property Properties As List(Of NPC_PropertyEntry) = Nothing             ' PRPS → AVIF + float
    Public Property ObjectTemplateCombinations As List(Of NPC_ObjectTemplateCombination) = Nothing  ' OBTE/OBTS

    ''' <summary>True once the user edited a Traits-category field (Race / Voice / Object Template). Drives the
    ''' template-flag hook (MakeCategoryOwn / clear Use-Traits) at apply time so the edit isn't overwritten by
    ''' the engine's CopyFromTemplate. Latched (OR-ed) across successive edits.</summary>
    Public Property TraitsChanged As Boolean = False

    ''' <summary>True when this override carries at least one edited field — used to decide whether to store it.</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return FullName Is Nothing AndAlso ShortName Is Nothing AndAlso Not AcbsFlags.HasValue AndAlso
                   Not XpValueOffset.HasValue AndAlso Not Level.HasValue AndAlso Not CalcMinLevel.HasValue AndAlso
                   Not CalcMaxLevel.HasValue AndAlso Not DispositionBase.HasValue AndAlso Not TemplateFlags.HasValue AndAlso
                   Not RaceFormID.HasValue AndAlso Not VoiceFormID.HasValue AndAlso Not ClassFormID.HasValue AndAlso
                   Not CombatStyleFormID.HasValue AndAlso Keywords Is Nothing AndAlso AttachParentSlots Is Nothing AndAlso
                   Factions Is Nothing AndAlso Inventory Is Nothing AndAlso Perks Is Nothing AndAlso
                   ActorEffects Is Nothing AndAlso Properties Is Nothing AndAlso ObjectTemplateCombinations Is Nothing
        End Get
    End Property

End Class
