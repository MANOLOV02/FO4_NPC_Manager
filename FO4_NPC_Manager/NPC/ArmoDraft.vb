Imports FO4_Base_Library

''' <summary>An Armor (ARMO) record being authored in the (future) ARMA/ARMO/MSWP editor — an in-memory
''' draft owned by MainForm (process scope) until persisted via the Save dialog. Mirrors
''' <see cref="OutfitDraft"/> exactly (same provisional-FormID/dirty/EditorID scheme, shared draft
''' FormID counter) but for the ARMO record type. This draft IS the authoring model the writer's
''' <see cref="SaveNpcEspWriter.ArmoRecordEntry"/> is built from in the saver (mirror of how an
''' <see cref="OutfitDraft"/> becomes an <c>OtftRecordEntry</c> in Phase 2c) — so its fields mirror
''' that entry class field-for-field.
'''
''' Two flavours (same contract as <see cref="OutfitDraft"/>):
'''   • NEW (IsOverride=False): a brand-new ARMO. <see cref="FormID"/> is a PROVISIONAL sentinel
'''     (high byte 0xFF, <see cref="OutfitDraft.IsDraftFormID"/>) so an OTFT/LVLI draft (or an NPC WNAM
'''     skin override) can reference it before save; the writer assigns the real plugin self-index FormID
'''     and remaps at save time.
'''   • OVERRIDE (IsOverride=True): an edit of an existing ARMO keeping its EditorID. <see cref="FormID"/>
'''     IS that record's real GLOBAL FormID from the load order; the saver fetches
'''     <c>PluginManager.GetRecord(FormID)</c> for the entry's <c>SourceRecord</c> (every subrecord not
'''     in the OWNED set is copied verbatim from it) and reads the original VCS from its header.
'''
''' Dependency edges the saver's transitive closure walks: <see cref="ArmorAddons"/>.ArmaFormID → ARMA
''' draft, and <see cref="MaleMaterialSwapFormID"/>/<see cref="FemaleMaterialSwapFormID"/> → MSWP draft.
''' Any of those FormIDs may be a provisional sentinel; the writer remaps them on emit.</summary>
Public Class ArmoDraft

    ''' <summary>Working EditorID prefix (type segment): <c>npcm_ARMO_&lt;name&gt;</c>. At save the
    ''' destination plugin name is injected (NpcOverrideSaver.ApplyEspNamespaceToEditorId) → final
    ''' <c>npcm_&lt;ESPNAME&gt;_ARMO_&lt;name&gt;</c>, identifiable + per-plugin namespaced in xEdit.</summary>
    Public Const EditorIdPrefix As String = "npcm_ARMO_"

    ''' <summary>NEW: provisional sentinel (0xFF…, from MainForm.AllocateDraftFormID). OVERRIDE: the
    ''' existing ARMO's real GLOBAL FormID (the saver uses it both as the entry FormID and as the
    ''' <c>GetRecord</c> key for <c>SourceRecord</c>).</summary>
    Public Property FormID As UInteger
    Public Property EditorID As String = ""
    Public Property FullName As String = ""               ' FULL (optional)
    Public Property SlotMask As UInteger                  ' BOD2
    Public Property RaceFormID As UInteger                ' RNAM
    ''' <summary>INRD — Instance Naming rules ref ([INNR]). Single owned FormID, preserved byte-exact on an
    ''' unedited override (loaded from the source, re-emitted at its canonical position after DESC).</summary>
    Public Property InstanceNamingFormID As UInteger      ' INRD (INNR)
    ''' <summary>EITM — Object Effect / Enchantment ([ENCH]). Owned optional single FormID (omit when 0).</summary>
    Public Property EnchantmentFormID As UInteger         ' EITM (ENCH)
    ''' <summary>PTRN — Preview Transform ([TRNS]). Owned optional single FormID.</summary>
    Public Property PatternFormID As UInteger             ' PTRN (TRNS)
    ''' <summary>ETYP — Equip Type ([EQUP]). Owned optional single FormID.</summary>
    Public Property EquipTypeFormID As UInteger           ' ETYP (EQUP)
    ''' <summary>YNAM — Pickup Sound ([SNDR]). Owned optional single FormID.</summary>
    Public Property PickupSoundFormID As UInteger         ' YNAM (SNDR)
    ''' <summary>ZNAM — Drop Sound ([SNDR]). Owned optional single FormID.</summary>
    Public Property DropSoundFormID As UInteger           ' ZNAM (SNDR)
    ''' <summary>BAMT — Alternate Block Material ([MATT]). Owned optional single FormID.</summary>
    Public Property AlternateBlockMaterialFormID As UInteger  ' BAMT (MATT)
    ''' <summary>DESC — Description (translatable). Owned optional string (omit when empty).</summary>
    Public Property Description As String = ""            ' DESC
    ''' <summary>OBND — Object Bounds (required 6×i16 min/max XYZ). Editable; always emitted.</summary>
    Public Property ObndX1 As Short
    Public Property ObndY1 As Short
    Public Property ObndZ1 As Short
    Public Property ObndX2 As Short
    Public Property ObndY2 As Short
    Public Property ObndZ2 As Short
    ''' <summary>Header flag bit 2 = 'Non-Playable'. Owned; part of the record header flag word.</summary>
    Public Property NonPlayable As Boolean
    ''' <summary>DAMA — Damage Type Array / Resistances. Owned list (omit block when empty).</summary>
    Public ReadOnly Property DamageResistances As New List(Of ARMO_DamageResist)
    Public Property TemplateArmorFormID As UInteger       ' TNAM (ARMO)
    ''' <summary>Models: INDX + ArmaFormID. Each ArmaFormID may be a real FormID or an ARMA draft
    ''' provisional sentinel (the closure pulls in referenced ARMA drafts).</summary>
    Public ReadOnly Property ArmorAddons As New List(Of ARMO_AddonEntry)
    Public ReadOnly Property KeywordFormIDs As New List(Of UInteger)        ' KWDA
    Public ReadOnly Property AttachParentSlotFormIDs As New List(Of UInteger)  ' APPR (KYWD)
    ''' <summary>OBTE/OBTS Object Template combinations carried end-to-end so the editor preview applies the
    ''' combination's material swap (e.g. ClothesPreWarDressBlue) and a NEW-from-template ARMO keeps its object
    ''' template on save. Deep-copied out of the parsed cache (never aliased — the draft is mutated live) and
    ''' deep-copied again into ARMO_Data / ArmoRecordEntry (see <see cref="CloneCombinations"/>). The OVERRIDE save
    ''' path preserves the source OBTS bytes verbatim, so this list feeds only the NEW-record writer.</summary>
    Public ReadOnly Property Combinations As New List(Of ARMO_Combination)
    ''' <summary>True once the user has EDITED the Object Template (OBTS combinations) in the ARMO editor's
    ''' "Object Template" tab (set by any add/remove/reorder/sub-editor commit). The current OVERRIDE save path
    ''' copies the source OBTS bytes verbatim and ignores <see cref="Combinations"/>; a future save-override phase
    ''' will consult THIS flag to decide whether to re-serialize the edited combinations instead of preserving the
    ''' source bytes. Not part of <see cref="IsDirty"/> and never affects the current save.</summary>
    Public Property CombinationsEdited As Boolean = False
    Public Property MaleWorldModelPath As String = ""     ' MOD2 (robots)
    Public Property FemaleWorldModelPath As String = ""   ' MOD4
    ''' <summary>MO2S at ARMO level (MSWP). May be a real FormID or an MSWP draft provisional sentinel.</summary>
    Public Property MaleMaterialSwapFormID As UInteger
    ''' <summary>MO4S at ARMO level (MSWP). May be a real FormID or an MSWP draft provisional sentinel.</summary>
    Public Property FemaleMaterialSwapFormID As UInteger
    Public Property Value As Integer = 0                  ' DATA Value (s32)
    Public Property Weight As Single = 0.0F               ' DATA Weight
    Public Property Health As UInteger = 0UI              ' DATA Health
    Public Property ArmorRating As UShort = 0US           ' FNAM (FO4)
    ''' <summary>SKYRIM DNAM 'Armor Rating' (itS32, wire value = rating×100). Distinct from the FO4 <see cref="ArmorRating"/>
    ''' (u16 FNAM). Threaded so a Skyrim ARMO override preserves its rating on save. 0 for FO4 drafts.</summary>
    Public Property SkyrimArmorRating As Integer = 0
    Public Property BaseAddonIndex As UShort = 0US        ' FNAM (0 = load addon group 0)
    Public Property StaggerRating As Byte = 0             ' FNAM

    ''' <summary>True = override an existing ARMO (keep its EditorID + FormID). False = brand-new ARMO.</summary>
    Public Property IsOverride As Boolean
    ''' <summary>Never written to the ESP yet.</summary>
    Public Property IsNew As Boolean = True
    ''' <summary>Written before, edited again since.</summary>
    Public Property IsModified As Boolean = False

    ''' <summary>Either flag set → the save must (re)write it. Both cleared after save.</summary>
    Public ReadOnly Property IsDirty As Boolean
        Get
            Return IsNew OrElse IsModified
        End Get
    End Property

    Public Function Clone() As ArmoDraft
        Dim c As New ArmoDraft With {
            .FormID = FormID, .EditorID = EditorID, .FullName = FullName, .SlotMask = SlotMask,
            .RaceFormID = RaceFormID, .InstanceNamingFormID = InstanceNamingFormID,
            .EnchantmentFormID = EnchantmentFormID, .PatternFormID = PatternFormID,
            .EquipTypeFormID = EquipTypeFormID, .PickupSoundFormID = PickupSoundFormID,
            .DropSoundFormID = DropSoundFormID, .AlternateBlockMaterialFormID = AlternateBlockMaterialFormID,
            .Description = Description, .NonPlayable = NonPlayable,
            .ObndX1 = ObndX1, .ObndY1 = ObndY1, .ObndZ1 = ObndZ1,
            .ObndX2 = ObndX2, .ObndY2 = ObndY2, .ObndZ2 = ObndZ2,
            .TemplateArmorFormID = TemplateArmorFormID,
            .MaleWorldModelPath = MaleWorldModelPath, .FemaleWorldModelPath = FemaleWorldModelPath,
            .MaleMaterialSwapFormID = MaleMaterialSwapFormID, .FemaleMaterialSwapFormID = FemaleMaterialSwapFormID,
            .Value = Value, .Weight = Weight, .Health = Health,
            .ArmorRating = ArmorRating, .SkyrimArmorRating = SkyrimArmorRating,
            .BaseAddonIndex = BaseAddonIndex, .StaggerRating = StaggerRating,
            .IsOverride = IsOverride, .IsNew = IsNew, .IsModified = IsModified,
            .CombinationsEdited = CombinationsEdited
        }
        For Each a In ArmorAddons
            c.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = a.AddonIndex, .ArmaFormID = a.ArmaFormID})
        Next
        c.KeywordFormIDs.AddRange(KeywordFormIDs)
        c.AttachParentSlotFormIDs.AddRange(AttachParentSlotFormIDs)
        For Each dr In DamageResistances
            c.DamageResistances.Add(New ARMO_DamageResist With {.DamageTypeFormID = dr.DamageTypeFormID, .Value = dr.Value})
        Next
        c.Combinations.AddRange(CloneCombinations(Combinations))
        Return c
    End Function

    ''' <summary>True when every AUTHORED (save/render-relevant) field equals <paramref name="o"/>, ignoring the
    ''' identity/status flags (FormID / IsNew / IsModified / IsOverride). Object Template combinations are
    ''' DELIBERATELY excluded — the OVERRIDE save preserves the source OBTS bytes verbatim and edits to them are
    ''' tracked by the separate <see cref="CombinationsEdited"/> flag, which the caller ORs in. Used by the editor
    ''' so a preview commit only marks the override <c>IsModified</c> when the content actually changed against the
    ''' open-time snapshot (mirror of <see cref="ArmaDraft.ContentEquals"/>).</summary>
    Public Function ContentEquals(o As ArmoDraft) As Boolean
        If o Is Nothing Then Return False
        If Not String.Equals(EditorID, o.EditorID, StringComparison.Ordinal) Then Return False
        If Not String.Equals(FullName, o.FullName, StringComparison.Ordinal) Then Return False
        If SlotMask <> o.SlotMask Then Return False
        If RaceFormID <> o.RaceFormID Then Return False
        If InstanceNamingFormID <> o.InstanceNamingFormID Then Return False
        If EnchantmentFormID <> o.EnchantmentFormID Then Return False
        If PatternFormID <> o.PatternFormID Then Return False
        If EquipTypeFormID <> o.EquipTypeFormID Then Return False
        If PickupSoundFormID <> o.PickupSoundFormID Then Return False
        If DropSoundFormID <> o.DropSoundFormID Then Return False
        If AlternateBlockMaterialFormID <> o.AlternateBlockMaterialFormID Then Return False
        If Not String.Equals(Description, o.Description, StringComparison.Ordinal) Then Return False
        If NonPlayable <> o.NonPlayable Then Return False
        If ObndX1 <> o.ObndX1 OrElse ObndY1 <> o.ObndY1 OrElse ObndZ1 <> o.ObndZ1 _
           OrElse ObndX2 <> o.ObndX2 OrElse ObndY2 <> o.ObndY2 OrElse ObndZ2 <> o.ObndZ2 Then Return False
        If DamageResistances.Count <> o.DamageResistances.Count Then Return False
        For i = 0 To DamageResistances.Count - 1
            If DamageResistances(i).DamageTypeFormID <> o.DamageResistances(i).DamageTypeFormID Then Return False
            If DamageResistances(i).Value <> o.DamageResistances(i).Value Then Return False
        Next
        If TemplateArmorFormID <> o.TemplateArmorFormID Then Return False
        If Not String.Equals(MaleWorldModelPath, o.MaleWorldModelPath, StringComparison.Ordinal) Then Return False
        If Not String.Equals(FemaleWorldModelPath, o.FemaleWorldModelPath, StringComparison.Ordinal) Then Return False
        If MaleMaterialSwapFormID <> o.MaleMaterialSwapFormID Then Return False
        If FemaleMaterialSwapFormID <> o.FemaleMaterialSwapFormID Then Return False
        If Value <> o.Value Then Return False
        If Weight <> o.Weight Then Return False
        If Health <> o.Health Then Return False
        If ArmorRating <> o.ArmorRating Then Return False
        If SkyrimArmorRating <> o.SkyrimArmorRating Then Return False
        If BaseAddonIndex <> o.BaseAddonIndex Then Return False
        If StaggerRating <> o.StaggerRating Then Return False
        If ArmorAddons.Count <> o.ArmorAddons.Count Then Return False
        For i = 0 To ArmorAddons.Count - 1
            If ArmorAddons(i).AddonIndex <> o.ArmorAddons(i).AddonIndex Then Return False
            If ArmorAddons(i).ArmaFormID <> o.ArmorAddons(i).ArmaFormID Then Return False
        Next
        If KeywordFormIDs.Count <> o.KeywordFormIDs.Count Then Return False
        For i = 0 To KeywordFormIDs.Count - 1
            If KeywordFormIDs(i) <> o.KeywordFormIDs(i) Then Return False
        Next
        If AttachParentSlotFormIDs.Count <> o.AttachParentSlotFormIDs.Count Then Return False
        For i = 0 To AttachParentSlotFormIDs.Count - 1
            If AttachParentSlotFormIDs(i) <> o.AttachParentSlotFormIDs(i) Then Return False
        Next
        Return True
    End Function

    ''' <summary>Deep-copy an OBTS combination list (new ARMO_Combination instances with fresh Keywords/Includes/
    ''' Properties sublists) so the live-mutated draft never aliases the parsed cache — and neither does the
    ''' ARMO_Data / ArmoRecordEntry synthesized from it. Single source of truth for combination deep-copy shared by
    ''' the editor (BuildDraftFromExisting), the render synthesizer (BuildArmoDataFromDraft) and the saver
    ''' (BuildArmoEntry). Returns an empty list for a null source.</summary>
    Public Shared Function CloneCombinations(src As List(Of ARMO_Combination)) As List(Of ARMO_Combination)
        Dim dst As New List(Of ARMO_Combination)
        If src Is Nothing Then Return dst
        For Each combo In src
            If combo Is Nothing Then Continue For
            Dim cc As New ARMO_Combination With {
                .IsEditorOnly = combo.IsEditorOnly,
                .DisplayName = combo.DisplayName,
                .IsDefault = combo.IsDefault,
                .ParentCombinationIndex = combo.ParentCombinationIndex,
                .LevelMin = combo.LevelMin,
                .LevelMax = combo.LevelMax,
                .MinLevelForRanks = combo.MinLevelForRanks,
                .AltLevelsPerTier = combo.AltLevelsPerTier
            }
            If combo.Keywords IsNot Nothing Then cc.Keywords.AddRange(combo.Keywords)
            If combo.Includes IsNot Nothing Then
                For Each inc In combo.Includes
                    cc.Includes.Add(New ARMO_CombinationInclude With {
                        .ModFormID = inc.ModFormID,
                        .AttachPointIndex = inc.AttachPointIndex,
                        .IsOptional = inc.IsOptional,
                        .DontUseAll = inc.DontUseAll})
                Next
            End If
            If combo.Properties IsNot Nothing Then
                For Each p In combo.Properties
                    cc.Properties.Add(New OMOD_Property With {
                        .ValueType = p.ValueType,
                        .FunctionType = p.FunctionType,
                        .PropertyIndex = p.PropertyIndex,
                        .Value1 = p.Value1,
                        .Value1FormID = p.Value1FormID,
                        .Value2 = p.Value2,
                        .StepValue = p.StepValue})
                Next
            End If
            dst.Add(cc)
        Next
        Return dst
    End Function

End Class
