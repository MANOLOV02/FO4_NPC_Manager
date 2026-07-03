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
    Public Property ArmorRating As UShort = 0US           ' FNAM
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
            .RaceFormID = RaceFormID, .TemplateArmorFormID = TemplateArmorFormID,
            .MaleWorldModelPath = MaleWorldModelPath, .FemaleWorldModelPath = FemaleWorldModelPath,
            .MaleMaterialSwapFormID = MaleMaterialSwapFormID, .FemaleMaterialSwapFormID = FemaleMaterialSwapFormID,
            .Value = Value, .Weight = Weight, .Health = Health,
            .ArmorRating = ArmorRating, .BaseAddonIndex = BaseAddonIndex, .StaggerRating = StaggerRating,
            .IsOverride = IsOverride, .IsNew = IsNew, .IsModified = IsModified,
            .CombinationsEdited = CombinationsEdited
        }
        For Each a In ArmorAddons
            c.ArmorAddons.Add(New ARMO_AddonEntry With {.AddonIndex = a.AddonIndex, .ArmaFormID = a.ArmaFormID})
        Next
        c.KeywordFormIDs.AddRange(KeywordFormIDs)
        c.AttachParentSlotFormIDs.AddRange(AttachParentSlotFormIDs)
        c.Combinations.AddRange(CloneCombinations(Combinations))
        Return c
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
