Imports FO4_Base_Library

''' <summary>An Armor Addon (ARMA) record being authored in the (future) ARMA/ARMO/MSWP editor — an
''' in-memory draft owned by MainForm (process scope) until persisted via the Save dialog. Mirrors
''' <see cref="OutfitDraft"/> exactly (same provisional-FormID/dirty/EditorID scheme, shared draft
''' FormID counter) but for the ARMA record type. This draft IS the authoring model the writer's
''' <see cref="SaveNpcEspWriter.ArmaRecordEntry"/> is built from in the saver (mirror of how an
''' <see cref="OutfitDraft"/> becomes an <c>OtftRecordEntry</c> in Phase 2c) — so its fields mirror
''' that entry class field-for-field.
'''
''' Two flavours (same contract as <see cref="OutfitDraft"/>):
'''   • NEW (IsOverride=False): a brand-new ARMA. <see cref="FormID"/> is a PROVISIONAL sentinel
'''     (high byte 0xFF, <see cref="OutfitDraft.IsDraftFormID"/>) so an owning ARMO draft can reference
'''     it before save; the writer assigns the real plugin self-index FormID and remaps at save time.
'''   • OVERRIDE (IsOverride=True): an edit of an existing ARMA keeping its EditorID. <see cref="FormID"/>
'''     IS that record's real GLOBAL FormID from the load order; the saver fetches
'''     <c>PluginManager.GetRecord(FormID)</c> for the entry's <c>SourceRecord</c> (every subrecord not
'''     re-emitted from the draft is copied verbatim from it) and reads the original VCS from its header.
'''
''' Material-swap refs (<see cref="MaleMaterialSwapFormID"/>/<see cref="FemaleMaterialSwapFormID"/>) may
''' point at an MSWP draft's provisional FormID; the saver's transitive closure pulls those MSWP drafts
''' in and the writer remaps them on emit.</summary>
Public Class ArmaDraft

    ''' <summary>Working EditorID prefix (type segment): <c>npcm_ARMA_&lt;name&gt;</c>. At save the
    ''' destination plugin name is injected (NpcOverrideSaver.ApplyEspNamespaceToEditorId) → final
    ''' <c>npcm_&lt;ESPNAME&gt;_ARMA_&lt;name&gt;</c>, identifiable + per-plugin namespaced in xEdit.</summary>
    Public Const EditorIdPrefix As String = "npcm_ARMA_"

    ''' <summary>NEW: provisional sentinel (0xFF…, from MainForm.AllocateDraftFormID). OVERRIDE: the
    ''' existing ARMA's real GLOBAL FormID (the saver uses it both as the entry FormID and as the
    ''' <c>GetRecord</c> key for <c>SourceRecord</c>).</summary>
    Public Property FormID As UInteger
    Public Property EditorID As String = ""

    Public Property SlotMask As UInteger                  ' BOD2 (u32)
    Public Property RaceFormID As UInteger                ' RNAM
    Public Property MalePriority As Byte = 0
    Public Property FemalePriority As Byte = 0
    Public Property MaleWeightSliderFlags As Byte = 0
    Public Property FemaleWeightSliderFlags As Byte = 0
    Public Property DetectionSoundValue As Byte = 0
    Public Property WeaponAdjust As Single = 0.0F
    Public Property MaleMeshPath As String = ""           ' MOD2
    Public Property FemaleMeshPath As String = ""         ' MOD3
    Public Property MaleFPMeshPath As String = ""         ' MOD4
    Public Property FemaleFPMeshPath As String = ""       ' MOD5
    Public Property MaleModelFlags As Byte = 0            ' MO2F
    Public Property FemaleModelFlags As Byte = 0          ' MO3F
    Public Property MaleFPModelFlags As Byte = 0          ' MO4F
    Public Property FemaleFPModelFlags As Byte = 0        ' MO5F
    Public Property MaleColorRemapIndex As Single? = Nothing   ' MO2C
    Public Property FemaleColorRemapIndex As Single? = Nothing ' MO3C
    Public Property MaleSkinTextureFormID As UInteger     ' NAM0 (TXST)
    Public Property FemaleSkinTextureFormID As UInteger   ' NAM1 (TXST)
    Public Property MaleSkinTextureSwapListFormID As UInteger   ' NAM2 (FLST)
    Public Property FemaleSkinTextureSwapListFormID As UInteger ' NAM3 (FLST)
    ''' <summary>MO2S (MSWP). May be a real FormID or an MSWP draft provisional sentinel.</summary>
    Public Property MaleMaterialSwapFormID As UInteger
    ''' <summary>MO3S (MSWP). May be a real FormID or an MSWP draft provisional sentinel.</summary>
    Public Property FemaleMaterialSwapFormID As UInteger
    Public ReadOnly Property AdditionalRaces As New List(Of UInteger)   ' MODL array (RACE)
    Public ReadOnly Property BoneScaleData As New List(Of ARMA_BoneScaleGender)  ' BSMP/BSMB/BSMS
    Public Property NoUnderarmorScaling As Boolean = False   ' header flag bit 6
    Public Property HasSculptData As Boolean = False         ' header flag bit 9
    Public Property HiRes1stPersonOnly As Boolean = False    ' header flag bit 30

    ''' <summary>True = override an existing ARMA (keep its EditorID + FormID). False = brand-new ARMA.</summary>
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

    Public Function Clone() As ArmaDraft
        Dim c As New ArmaDraft With {
            .FormID = FormID, .EditorID = EditorID, .SlotMask = SlotMask, .RaceFormID = RaceFormID,
            .MalePriority = MalePriority, .FemalePriority = FemalePriority,
            .MaleWeightSliderFlags = MaleWeightSliderFlags, .FemaleWeightSliderFlags = FemaleWeightSliderFlags,
            .DetectionSoundValue = DetectionSoundValue, .WeaponAdjust = WeaponAdjust,
            .MaleMeshPath = MaleMeshPath, .FemaleMeshPath = FemaleMeshPath,
            .MaleFPMeshPath = MaleFPMeshPath, .FemaleFPMeshPath = FemaleFPMeshPath,
            .MaleModelFlags = MaleModelFlags, .FemaleModelFlags = FemaleModelFlags,
            .MaleFPModelFlags = MaleFPModelFlags, .FemaleFPModelFlags = FemaleFPModelFlags,
            .MaleColorRemapIndex = MaleColorRemapIndex, .FemaleColorRemapIndex = FemaleColorRemapIndex,
            .MaleSkinTextureFormID = MaleSkinTextureFormID, .FemaleSkinTextureFormID = FemaleSkinTextureFormID,
            .MaleSkinTextureSwapListFormID = MaleSkinTextureSwapListFormID,
            .FemaleSkinTextureSwapListFormID = FemaleSkinTextureSwapListFormID,
            .MaleMaterialSwapFormID = MaleMaterialSwapFormID, .FemaleMaterialSwapFormID = FemaleMaterialSwapFormID,
            .NoUnderarmorScaling = NoUnderarmorScaling, .HasSculptData = HasSculptData,
            .HiRes1stPersonOnly = HiRes1stPersonOnly,
            .IsOverride = IsOverride, .IsNew = IsNew, .IsModified = IsModified
        }
        c.AdditionalRaces.AddRange(AdditionalRaces)
        For Each g In BoneScaleData
            Dim cg As New ARMA_BoneScaleGender With {.Gender = g.Gender}
            For Each d In g.Bones
                cg.Bones.Add(New ARMA_BoneScaleDelta With {
                    .BoneName = d.BoneName, .DeltaX = d.DeltaX, .DeltaY = d.DeltaY, .DeltaZ = d.DeltaZ})
            Next
            c.BoneScaleData.Add(cg)
        Next
        Return c
    End Function

End Class
