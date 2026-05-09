Imports FO4_Base_Library

''' <summary>
''' Reusable NPC record-parsing and LooksMenu preset overlay helpers. App-specific
''' (NPC_Manager only — Wardrobe_Manager has no NPC concept), so they live in this app's
''' NPC/ folder, not in FO4_Base_Library.
'''
''' Single-source-of-truth for the orchestration "fetch NPC record → parse → apply overlay".
''' Both the render path (MainForm.GetParsedNpc / MainForm.ApplyPresetOverlayToNpcData) and
''' the offline bake (FaceGenBuilder) consume this module so the two views never drift.
''' </summary>
Public Module NpcRecordOverlay

    ''' <summary>Parse the NPC record at the given FormID into NPC_Data. Returns Nothing if
    ''' the record is missing or has the wrong signature. The pluginManager is the single
    ''' source of records — no static state.</summary>
    Public Function GetParsedNpc(formID As UInteger, pluginManager As PluginManager) As NPC_Data
        Dim rec = pluginManager.GetRecord(formID)
        If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Return Nothing
        Dim pluginName = If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown")
        Return RecordParsers.ParseNPC(rec, pluginName, pluginManager)
    End Function

    ''' <summary>If an overlay is registered for <paramref name="selectedNpcFormID"/> in
    ''' <paramref name="appliedPresets"/>, return a shallow copy of <paramref name="raw"/>
    ''' with the preset's morph/face-tint/HeadPart/etc. fields swapped in. Otherwise return
    ''' <paramref name="raw"/> unchanged.
    '''
    ''' Per-field semantics replicate the engine's LoadPreset (CharGenInterface.cpp:259-628):
    '''   • HeadParts: race chargen defaults FIRST (engine WIPES then repopulates),
    '''     then preset entries appended; downstream MergeHeadPartsWithRaceDefaults dedupes
    '''     per-PartType ("preset wins, race fills gaps").
    '''   • HairColor: preset 0 means "not in JSON, preserve" (engine: nullptr form skips).
    '''   • Weight: preserve raw when preset doesn't carry a value (Single?=Nothing = absent).
    '''   • Morphs.Presets / Values / Regions: Has* presence flag drives wipe-vs-preserve
    '''     (HasX=True ⇒ apply preset content even if empty; =False ⇒ preserve raw).
    '''   • FacialMorphIntensity: always overwrite (engine always calls SetFacialBoneMorphIntensity,
    '''     using 1.0 when missing — parser already defaults to 1.0F so this is equivalent).
    '''   • Tints: Has*-driven, same shape as morphs.
    ''' </summary>
    Public Function ApplyPresetOverlayToNpcData(raw As NPC_Data,
                                                selectedNpcFormID As UInteger,
                                                appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                                pluginManager As PluginManager) As NPC_Data
        If raw Is Nothing Then Return raw
        If appliedPresets Is Nothing Then Return raw
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not appliedPresets.TryGetValue(selectedNpcFormID, preset) Then Return raw

        ' Shallow copy of NPC_Data with the preset-touched fields replaced. The base record stays
        ' immutable; downstream code that reads other fields (RaceFormID, TemplateFormID, etc) sees
        ' the same values it would have without the overlay.
        Dim shadow As New NPC_Data()
        shadow.FormID = raw.FormID
        shadow.EditorID = raw.EditorID
        shadow.FullName = raw.FullName
        shadow.RaceFormID = raw.RaceFormID
        ' NPC.WNAM (vanilla skin → ARMO). Three states for the overlay:
        '   Nothing       → preserve raw NPC.WNAM
        '   value <> 0    → ARMO override (e.g. a custom skin pulled in via Edit Face)
        '   value = 0     → explicit clear; the renderer's resolver falls back to RACE.WNAM
        shadow.SkinFormID = If(preset.SkinFormIDOverride.HasValue, preset.SkinFormIDOverride.Value, raw.SkinFormID)
        shadow.IsFemale = raw.IsFemale
        shadow.DefaultOutfitFormID = raw.DefaultOutfitFormID
        shadow.SleepOutfitFormID = raw.SleepOutfitFormID
        shadow.HeadTextureFormID = raw.HeadTextureFormID
        shadow.FacialHairColorFormID = raw.FacialHairColorFormID
        shadow.HasTextureLighting = raw.HasTextureLighting
        shadow.TextureLightingColor = raw.TextureLightingColor
        shadow.TemplateFormID = raw.TemplateFormID
        shadow.TemplateFlags = raw.TemplateFlags
        ' ACBS bit 2 (0x04 = "Is CharGen Face Preset"). Editor overlay can set/clear it for
        ' eventual ESP persistence; the renderer doesn't read this bit so it has no live visual
        ' effect — but Save ESP/ESM will emit shadow.AcbsFlags.
        Const AcbsBitIsCharGenFacePreset As UInteger = &H4UI
        Const AcbsBitIsCharGenFacePresetMask As UInteger = &HFFFFFFFBUI ' ~0x4 in 32-bit
        Dim acbs As UInteger = raw.AcbsFlags
        If preset.IsCharGenFacePreset.HasValue Then
            If preset.IsCharGenFacePreset.Value Then
                acbs = acbs Or AcbsBitIsCharGenFacePreset
            Else
                acbs = acbs And AcbsBitIsCharGenFacePresetMask
            End If
        End If
        shadow.AcbsFlags = acbs
        shadow.PluginName = raw.PluginName
        shadow.TemplateActorFormIDs = raw.TemplateActorFormIDs
        shadow.ObjectTemplateOMODFormIDs.AddRange(raw.ObjectTemplateOMODFormIDs)

        ' HeadParts: replicate engine wipe + race defaults + preset overrides.
        Dim raceRec = If(raw.RaceFormID <> 0UI, pluginManager.GetRecord(raw.RaceFormID), Nothing)
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            Dim race = RecordParsers.ParseRACE(raceRec, pluginManager)
            Dim raceDefaults = If(raw.IsFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)
            If raceDefaults IsNot Nothing Then shadow.HeadPartFormIDs.AddRange(raceDefaults)
        End If
        shadow.HeadPartFormIDs.AddRange(preset.HeadPartFormIDs)

        ' HairColor: preset 0 means "not in JSON, preserve" (engine behaviour: nullptr form skips).
        shadow.HairColorFormID = If(preset.HairColorFormID <> 0UI, preset.HairColorFormID, raw.HairColorFormID)

        ' Weight: preserve raw when preset doesn't carry a value.
        shadow.WeightThin = If(preset.WeightThin.HasValue, preset.WeightThin, raw.WeightThin)
        shadow.WeightMuscular = If(preset.WeightMuscular.HasValue, preset.WeightMuscular, raw.WeightMuscular)
        shadow.WeightFat = If(preset.WeightFat.HasValue, preset.WeightFat, raw.WeightFat)

        ' Morphs.Presets (MSDK/MSDV chargen vertex morphs).
        If preset.HasChargenFaceMorphs Then
            For Each kv In preset.ChargenFaceMorphs
                shadow.MorphValues(kv.Key) = kv.Value
            Next
        Else
            For Each kv In raw.MorphValues
                shadow.MorphValues(kv.Key) = kv.Value
            Next
        End If

        ' Morphs.Values (MRSV body region morphs).
        If preset.HasBodyMorphValues Then
            shadow.BodyMorphRegionValues.AddRange(preset.BodyMorphValues)
        Else
            shadow.BodyMorphRegionValues.AddRange(raw.BodyMorphRegionValues)
        End If

        ' Morphs.Regions (FMRI/FMRS face bone regions).
        Dim rawFmByIndex As New Dictionary(Of UInteger, NPC_FaceMorphData)
        For Each fm In raw.FaceMorphs
            If Not rawFmByIndex.ContainsKey(fm.Index) Then rawFmByIndex(fm.Index) = fm
        Next
        If preset.HasFaceBoneRegions Then
            For Each kv In preset.FaceBoneRegions
                Dim fm As New NPC_FaceMorphData With {.Index = kv.Key}
                fm.Values.AddRange(kv.Value)
                Dim matchedRaw As NPC_FaceMorphData = Nothing
                If rawFmByIndex.TryGetValue(kv.Key, matchedRaw) Then
                    fm.RawFmrsBytes = matchedRaw.RawFmrsBytes
                End If
                shadow.FaceMorphs.Add(fm)
            Next
        Else
            For Each fm In raw.FaceMorphs
                Dim copy As New NPC_FaceMorphData With {.Index = fm.Index}
                copy.Values.AddRange(fm.Values)
                copy.RawFmrsBytes = fm.RawFmrsBytes
                shadow.FaceMorphs.Add(copy)
            Next
        End If

        ' FacialMorphIntensity: always overwrite.
        shadow.FacialMorphIntensity = preset.FacialMorphIntensity

        ' Tints: Has*-driven.
        If preset.HasFaceTintLayers Then
            For Each tl In preset.FaceTintLayers
                shadow.FaceTintLayers.Add(LooksmenuLoader.CloneFaceTintLayer(tl))
            Next
        Else
            For Each tl In raw.FaceTintLayers
                shadow.FaceTintLayers.Add(LooksmenuLoader.CloneFaceTintLayer(tl))
            Next
        End If

        Return shadow
    End Function

End Module
