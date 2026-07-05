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
    ''' <summary>SNDD — Footstep Sound ref ([FSTS]). Single owned FormID, preserved byte-exact on an unedited
    ''' override (loaded from the source, re-emitted at its canonical position after the Additional Races).</summary>
    Public Property FootstepSetFormID As UInteger         ' SNDD (FSTS)
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
    ''' <summary>MO4S — 1st-person Male material swap (MSWP). Owned optional single FormID.</summary>
    Public Property MaleFPMaterialSwapFormID As UInteger
    ''' <summary>MO5S — 1st-person Female material swap (MSWP). Owned optional single FormID.</summary>
    Public Property FemaleFPMaterialSwapFormID As UInteger
    ''' <summary>ONAM — Art Object ([ARTO]). Owned optional single FormID, emitted after SNDD.</summary>
    Public Property ArtObjectFormID As UInteger
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
            .FootstepSetFormID = FootstepSetFormID,
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
            .MaleFPMaterialSwapFormID = MaleFPMaterialSwapFormID, .FemaleFPMaterialSwapFormID = FemaleFPMaterialSwapFormID,
            .ArtObjectFormID = ArtObjectFormID,
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

    ''' <summary>True when every AUTHORED (save/render-relevant) field equals <paramref name="o"/> — i.e. the
    ''' record content is identical, ignoring the identity/status flags (FormID / IsNew / IsModified / IsOverride).
    ''' Used by the editor to decide whether an OVERRIDE was actually CHANGED: the preview commits the panels on
    ''' every render, so <c>IsModified</c> must be set from a real content diff against the open-time snapshot, not
    ''' from the mere act of committing. Compares all fields the writer emits so a change to any of them is caught
    ''' (no false "identical"); the reverse (a false "changed") is harmless — it just re-emits an identical override.</summary>
    Public Function ContentEquals(o As ArmaDraft) As Boolean
        If o Is Nothing Then Return False
        If Not String.Equals(EditorID, o.EditorID, StringComparison.Ordinal) Then Return False
        If SlotMask <> o.SlotMask Then Return False
        If RaceFormID <> o.RaceFormID Then Return False
        If FootstepSetFormID <> o.FootstepSetFormID Then Return False
        If MalePriority <> o.MalePriority Then Return False
        If FemalePriority <> o.FemalePriority Then Return False
        If MaleWeightSliderFlags <> o.MaleWeightSliderFlags Then Return False
        If FemaleWeightSliderFlags <> o.FemaleWeightSliderFlags Then Return False
        If DetectionSoundValue <> o.DetectionSoundValue Then Return False
        If WeaponAdjust <> o.WeaponAdjust Then Return False
        If Not String.Equals(MaleMeshPath, o.MaleMeshPath, StringComparison.Ordinal) Then Return False
        If Not String.Equals(FemaleMeshPath, o.FemaleMeshPath, StringComparison.Ordinal) Then Return False
        If Not String.Equals(MaleFPMeshPath, o.MaleFPMeshPath, StringComparison.Ordinal) Then Return False
        If Not String.Equals(FemaleFPMeshPath, o.FemaleFPMeshPath, StringComparison.Ordinal) Then Return False
        If MaleModelFlags <> o.MaleModelFlags Then Return False
        If FemaleModelFlags <> o.FemaleModelFlags Then Return False
        If MaleFPModelFlags <> o.MaleFPModelFlags Then Return False
        If FemaleFPModelFlags <> o.FemaleFPModelFlags Then Return False
        If Not NullableSingleEquals(MaleColorRemapIndex, o.MaleColorRemapIndex) Then Return False
        If Not NullableSingleEquals(FemaleColorRemapIndex, o.FemaleColorRemapIndex) Then Return False
        If MaleSkinTextureFormID <> o.MaleSkinTextureFormID Then Return False
        If FemaleSkinTextureFormID <> o.FemaleSkinTextureFormID Then Return False
        If MaleSkinTextureSwapListFormID <> o.MaleSkinTextureSwapListFormID Then Return False
        If FemaleSkinTextureSwapListFormID <> o.FemaleSkinTextureSwapListFormID Then Return False
        If MaleMaterialSwapFormID <> o.MaleMaterialSwapFormID Then Return False
        If FemaleMaterialSwapFormID <> o.FemaleMaterialSwapFormID Then Return False
        If MaleFPMaterialSwapFormID <> o.MaleFPMaterialSwapFormID Then Return False
        If FemaleFPMaterialSwapFormID <> o.FemaleFPMaterialSwapFormID Then Return False
        If ArtObjectFormID <> o.ArtObjectFormID Then Return False
        If NoUnderarmorScaling <> o.NoUnderarmorScaling Then Return False
        If HasSculptData <> o.HasSculptData Then Return False
        If HiRes1stPersonOnly <> o.HiRes1stPersonOnly Then Return False
        If AdditionalRaces.Count <> o.AdditionalRaces.Count Then Return False
        For i = 0 To AdditionalRaces.Count - 1
            If AdditionalRaces(i) <> o.AdditionalRaces(i) Then Return False
        Next
        If BoneScaleData.Count <> o.BoneScaleData.Count Then Return False
        For i = 0 To BoneScaleData.Count - 1
            Dim g1 = BoneScaleData(i), g2 = o.BoneScaleData(i)
            If g1.Gender <> g2.Gender Then Return False
            If g1.Bones.Count <> g2.Bones.Count Then Return False
            For j = 0 To g1.Bones.Count - 1
                Dim b1 = g1.Bones(j), b2 = g2.Bones(j)
                If Not String.Equals(b1.BoneName, b2.BoneName, StringComparison.Ordinal) Then Return False
                If b1.DeltaX <> b2.DeltaX OrElse b1.DeltaY <> b2.DeltaY OrElse b1.DeltaZ <> b2.DeltaZ Then Return False
            Next
        Next
        Return True
    End Function

    Private Shared Function NullableSingleEquals(a As Single?, b As Single?) As Boolean
        If a.HasValue <> b.HasValue Then Return False
        If Not a.HasValue Then Return True
        Return a.Value = b.Value
    End Function

End Class
