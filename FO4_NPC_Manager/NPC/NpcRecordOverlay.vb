Imports FO4_Base_Library

''' <summary>
''' Reusable NPC record-parsing and LooksMenu preset overlay helpers. App-specific
''' (NPC_Manager only — Wardrobe_Manager has no NPC concept), so they live in this app's
''' NPC/ folder, not in FO4_Base_Library.
'''
''' Single-source-of-truth for the orchestration "fetch NPC record → parse → apply overlay".
''' Both the render path (NpcRenderContext.GetParsedNpc / MainForm.ApplyPresetOverlayToNpcData) and
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

    ''' <summary>Convenience composition of <see cref="GetParsedNpc"/> + <see cref="ApplyPresetOverlayToNpcData"/>:
    ''' fetch+parse the NPC record and apply the LooksMenu preset overlay in one call. Returns Nothing if the
    ''' NPC record doesn't resolve. Single source of truth for the FaceGen bake paths (FaceGenBuilder.BuildCharGen /
    ''' .BakeFaceTextures, FaceGenBuildPipeline.BuildBakeState) which all needed the same two-step sequence.</summary>
    Public Function ResolveOverlaidNpcData(npcFormID As UInteger,
                                           pluginManager As PluginManager,
                                           appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                           Optional lmSkinTemplateResolver As ResolveLmSkinTemplateDelegate = Nothing) As NPC_Data
        Dim raw = GetParsedNpc(npcFormID, pluginManager)
        If raw Is Nothing Then Return Nothing
        Return ApplyPresetOverlayToNpcData(raw, npcFormID, appliedPresets, pluginManager, lmSkinTemplateResolver)
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
    ''' <summary>Resolve an LM SkinTemplate id to its full bundle. Returns Nothing if the id
    ''' isn't loaded. Optional injection so the offline bake path (FaceGenBuilder) can opt out —
    ''' F4SE skin overrides are runtime only and don't apply to baked CharGen output.</summary>
    Public Delegate Function ResolveLmSkinTemplateDelegate(templateId As String) As LmSkinTemplate

    ''' <summary>HDPT.PartType enum values matching xEdit wbDefinitionsFO4.pas:7373-7384. These
    ''' are the values the parser surfaces in HDPT_Data.PartType and that the renderer reads via
    ''' state.HeadPartFormIDs lookups — NOT the F4SE runtime BGSHeadPart::Type enum (which uses
    ''' different numbering). Used by ApplyLmHdptReplacement and by MainForm's overlay merge.</summary>
    Public Const HdptPartType_Misc As Byte = 0
    Public Const HdptPartType_Face As Byte = 1
    Public Const HdptPartType_Eyes As Byte = 2
    Public Const HdptPartType_Hair As Byte = 3
    Public Const HdptPartType_FacialHair As Byte = 4
    Public Const HdptPartType_Scar As Byte = 5
    Public Const HdptPartType_Eyebrows As Byte = 6
    Public Const HdptPartType_Meatcaps As Byte = 7
    Public Const HdptPartType_Teeth As Byte = 8
    Public Const HdptPartType_HeadRear As Byte = 9

    ''' <summary>Public wrapper over ApplyLmHdptReplacement so MainForm's overlay merge can call
    ''' the same helper the shadow uses, ensuring identical replacement semantics across both
    ''' code paths. PartType is read from the new HDPT itself (engine-faithful per
    ''' SkinInterface.cpp:292), so callers don't pass a target — the helper figures it out.</summary>
    Public Sub ApplyLmHdptReplacementPublic(headParts As List(Of UInteger), newHdptFormID As UInteger,
                                              pluginManager As PluginManager)
        ApplyLmHdptReplacement(headParts, newHdptFormID, pluginManager)
    End Sub

    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCached). When Nothing,
    ''' falls back to a direct <c>RecordParsers.ParseRACE</c> — keeps the offline bake path pure.</param>
    Public Function ApplyPresetOverlayToNpcData(raw As NPC_Data,
                                                selectedNpcFormID As UInteger,
                                                appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                                pluginManager As PluginManager,
                                                Optional lmSkinTemplateResolver As ResolveLmSkinTemplateDelegate = Nothing,
                                                Optional parseRace As Func(Of PluginRecord, RACE_Data) = Nothing) As NPC_Data
        If raw Is Nothing Then Return raw
        If appliedPresets Is Nothing Then Return raw
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not appliedPresets.TryGetValue(selectedNpcFormID, preset) Then Return raw

        ' Shallow copy of NPC_Data with the preset-touched fields replaced. The base record stays
        ' immutable; downstream code that reads other fields (RaceFormID, TemplateFormID, etc) sees
        ' the same values it would have without the overlay.
        ' NPC.WNAM (vanilla skin → ARMO). Three states for the overlay:
        '   Nothing       → preserve raw NPC.WNAM
        '   value <> 0    → ARMO override (e.g. a custom skin pulled in via Edit Face)
        '   value = 0     → explicit clear; the renderer's resolver falls back to RACE.WNAM
        Dim shadow As New NPC_Data With {
            .FormID = raw.FormID,
            .EditorID = raw.EditorID,
            .FullName = raw.FullName,
            .RaceFormID = raw.RaceFormID,
            .SkinFormID = If(preset.SkinFormIDOverride, raw.SkinFormID)
        }
        ' LM SkinTemplate (F4SE bundle) wins over NPC.WNAM at preview time, mirroring
        ' SkinInterface.cpp:250-332 in F4SEPlugins-master/f4ee — ApplyOverride applies the
        ' template's `skin` ARMO + face[gender] TXST + head[gender] HDPT + rear[gender] HDPT.
        ' Skin and face TXST are applied here; head / headRear HDPT replacement is applied below
        ' after the preset HeadParts merge so the bundle sits on top of preset overrides.
        Dim lmTemplate As LmSkinTemplate = Nothing
        If Not String.IsNullOrEmpty(preset.SkinTemplateId) AndAlso lmSkinTemplateResolver IsNot Nothing Then
            lmTemplate = lmSkinTemplateResolver(preset.SkinTemplateId)
            If lmTemplate IsNot Nothing AndAlso lmTemplate.SkinArmoFormID <> 0UI Then
                shadow.SkinFormID = lmTemplate.SkinArmoFormID
            End If
        End If
        shadow.IsFemale = raw.IsFemale
        ' NPC.DOFT (default outfit → OTFT). Three states, same shape as SkinFormID:
        '   Nothing    → preserve raw NPC.DOFT
        '   value <> 0 → OTFT override (Edit Outfit picker)
        '   value = 0  → explicit "no outfit" (naked)
        shadow.DefaultOutfitFormID = If(preset.DefaultOutfitFormIDOverride, raw.DefaultOutfitFormID)
        ' DOFT emission gate: the writer emits DOFT only when HasDefaultOutfit. When the override is
        ' active, derive the flag from it — value<>0 → emit DOFT=value; value=0 → "no outfit", omit
        ' DOFT. Without this, an override on an NPC whose raw record had no DOFT would be dropped at
        ' write time (CopyRoundTripOnlyFieldsFromRaw no longer copies HasDefaultOutfit — this owns it).
        ' Nothing → preserve raw flag.
        shadow.HasDefaultOutfit = If(preset.DefaultOutfitFormIDOverride.HasValue, preset.DefaultOutfitFormIDOverride.Value <> 0UI, raw.HasDefaultOutfit)
        shadow.SleepOutfitFormID = raw.SleepOutfitFormID
        ' HeadTextureFormID: LM template face TXST overrides if present (mirrors
        ' SkinInterface.cpp:307-313 — overlay sets npc->headData->faceTextures = template.face[gender]).
        Dim lmFaceTxst As UInteger = 0UI
        If lmTemplate IsNot Nothing Then
            Dim genderIdx As Integer = If(raw.IsFemale, 1, 0)
            If lmTemplate.FaceTxstFormID(genderIdx) <> 0UI Then lmFaceTxst = lmTemplate.FaceTxstFormID(genderIdx)
        End If
        shadow.HeadTextureFormID = If(lmFaceTxst <> 0UI, lmFaceTxst, raw.HeadTextureFormID)
        ' HasHeadTexture is the writer's "emit FTST subrecord" gate. When the LM template
        ' injects a face TXST, mark Has*=True so Save ESP emits the override even if the raw
        ' NPC didn't carry an FTST of its own. Otherwise the bundle's face[gender] would land
        ' in the preview but disappear at ESP write time — WYSIWYG broken.
        shadow.HasHeadTexture = raw.HasHeadTexture OrElse (lmFaceTxst <> 0UI)
        shadow.FacialHairColorFormID = raw.FacialHairColorFormID
        ' QNAM (TextureLighting): seeded from raw here. Post-FaceTintLayers copy below we
        ' re-derive from the preset's slot-12 SkinTone tint via DeriveSkinToneQnam so the
        ' written ESP carries the face/body match the preview shows. Without that derivation
        ' the shadow's QNAM is stale (no Edit Face mutation ever reaches QNAM). Single source
        ' of truth shared with MainForm.ResolveNpcSkinToneColor.
        shadow.HasTextureLighting = raw.HasTextureLighting
        shadow.TextureLightingColor = raw.TextureLightingColor
        shadow.TextureLightingFloats = raw.TextureLightingFloats
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
        ' Parse RACE ONCE here (cached via parseRace when supplied by the render path; direct parse
        ' on the offline bake path) and reuse for both the HeadParts seed and the QNAM derivation below.
        Dim raceRec = If(raw.RaceFormID <> 0UI, pluginManager.GetRecord(raw.RaceFormID), Nothing)
        Dim raceIsValid As Boolean = raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE"
        Dim race As RACE_Data = Nothing
        If raceIsValid Then
            race = If(parseRace IsNot Nothing, parseRace(raceRec), RecordParsers.ParseRACE(raceRec, pluginManager))
            Dim raceDefaults = If(raw.IsFemale, race.FemaleHeadPartFormIDs, race.MaleHeadPartFormIDs)
            If raceDefaults IsNot Nothing Then shadow.HeadPartFormIDs.AddRange(raceDefaults)
        End If
        shadow.HeadPartFormIDs.AddRange(preset.HeadPartFormIDs)

        ' LM SkinTemplate head / headRear: replace the per-PartType HDPT entry. Mirrors
        ' SkinInterface.cpp:289-303 — npc->ChangeHeadPart(template.head/rear, false, false)
        ' which swaps the existing Face / HeadRear part for the template's. We do it here as a
        ' post-merge override so that the resulting list is "race defaults + preset overrides
        ' + LM bundle". HDPT.Type enum per xEdit wbDefinitionsFO4.pas:7373-7384:
        '   0=Misc, 1=Face, 2=Eyes, 3=Hair, 4=FacialHair, 5=Scar, 6=Eyebrows, 7=Meatcaps,
        '   8=Teeth, 9=HeadRear.
        ' (Note: the F4SE C++ enum uses different numbering — kTypeFace=0, kTypeHeadRear=2 etc.
        ' That's the runtime BGSHeadPart::Type, NOT the record PartType. Our parser already
        ' surfaces the record value in HDPT_Data.PartType, so we match xEdit's numbering.)
        If lmTemplate IsNot Nothing Then
            Dim genderIdx As Integer = If(raw.IsFemale, 1, 0)
            ' The helper reads each HDPT's own PartType to decide which slot to replace,
            ' so a JSON template that puts (e.g.) a Hair HDPT in "maleHead" replaces the
            ' Hair slot, not Face. Engine-faithful per SkinInterface.cpp:292.
            ApplyLmHdptReplacement(shadow.HeadPartFormIDs, lmTemplate.HeadHdptFormID(genderIdx), pluginManager)
            ApplyLmHdptReplacement(shadow.HeadPartFormIDs, lmTemplate.HeadRearHdptFormID(genderIdx), pluginManager)
        End If

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

        ' QNAM derivation (post-Tints): if the shadow now carries a slot-12 SkinTone tint, re-derive
        ' QNAM from it so the saved ESP matches the face/body skin tone the preview composites with.
        ' LooksMenu doesn't serialize QNAM (CharGenInterface.cpp doesn't emit "TextureLighting") —
        ' the engine reads it at runtime from the actor's tint array. We mirror that here at write
        ' time so the persisted record carries the effective colour, not the original raw QNAM that
        ' the user's edits never reached. If the shadow has no SkinTone tint, leave the raw-seeded
        ' QNAM (line 122-127) untouched.
        If raceIsValid Then
            Dim derivedSkinTone = DeriveSkinToneQnam(shadow, race, raw.IsFemale, pluginManager)
            Dim tintCountLog = shadow.FaceTintLayers.Count
            Dim hasDerivedLog = derivedSkinTone.HasValue
            If derivedSkinTone.HasValue Then
                shadow.HasTextureLighting = True
                shadow.TextureLightingColor = derivedSkinTone.Value
                shadow.TextureLightingFloats = New NPC_TextureLightingFloats With {
                    .R = derivedSkinTone.Value.R / 255.0F,
                    .G = derivedSkinTone.Value.G / 255.0F,
                    .B = derivedSkinTone.Value.B / 255.0F,
                    .A = derivedSkinTone.Value.A / 255.0F
                }
                Dim dR = derivedSkinTone.Value.R
                Dim dG = derivedSkinTone.Value.G
                Dim dB = derivedSkinTone.Value.B
                Dim dA = derivedSkinTone.Value.A
                Dim fidLog = raw.FormID
                Logger.LogLazy(Function() $"[QNAM-OVERLAY] fid=0x{fidLog:X8} derived from SkinTone tint: RGBA=({dR},{dG},{dB},{dA}) tintCount={tintCountLog}")
            Else
                Dim rawFloatsR As Single = If(raw.TextureLightingFloats IsNot Nothing, raw.TextureLightingFloats.R, 0.0F)
                Dim rawFloatsG As Single = If(raw.TextureLightingFloats IsNot Nothing, raw.TextureLightingFloats.G, 0.0F)
                Dim rawFloatsB As Single = If(raw.TextureLightingFloats IsNot Nothing, raw.TextureLightingFloats.B, 0.0F)
                Dim rawFloatsA As Single = If(raw.TextureLightingFloats IsNot Nothing, raw.TextureLightingFloats.A, 1.0F)
                Dim rawFloatsLog As String = If(raw.TextureLightingFloats Is Nothing, "Nothing", $"({rawFloatsR:F3},{rawFloatsG:F3},{rawFloatsB:F3},{rawFloatsA:F3})")
                Dim fidLog = raw.FormID
                Logger.LogLazy(Function() $"[QNAM-OVERLAY] fid=0x{fidLog:X8} NO derivation — preserving raw QNAM={rawFloatsLog} tintCount={tintCountLog}")
            End If
        End If

        Return shadow
    End Function

    ''' <summary>Derive the effective QNAM (TextureLightingColor) from the NPC's slot-12 SkinTone
    ''' tint layer. Returns Nothing when no such layer exists or its palette doesn't resolve. The
    ''' returned Color packs RGB from the palette CLFM and A from tl.Value (the layer's percent,
    ''' scaled to 0..255) — same shape MainForm.ResolveNpcSkinToneColor consumes. Single source of
    ''' truth shared by render (preview) and save (NpcRecordOverlay) so the two never drift.
    ''' <para>The Slot enum value is a schema-defined field name (xEdit wbDefinitionsFO4.pas:3478),
    ''' NOT a hardcoded magic number — this is the canonical lookup for "skin tint layer".</para></summary>
    Public Function DeriveSkinToneQnam(npc As NPC_Data, race As RACE_Data, isFemale As Boolean, pluginManager As PluginManager) As Nullable(Of Color)
        If npc Is Nothing OrElse race Is Nothing Then Return Nothing

        ' Iterar las capas MERGED (autoradas + defaults HEREDADOS de RACE), NO solo npc.FaceTintLayers.
        ' Asi el skin-tone HEREDADO (slot-12 que el NPC no autora) tambien resuelve -> el render (uniform
        ' albedo*=tintColor) y el save lo toman SIN tener que materializarlo en Face Edit. El heredado se
        ' comporta identico a uno autorado: MergeTintLayersWithRaceDefaults ya pone Color=CLFM y
        ' Value=Alpha*100 del TemplateColor por el indice TTED. Ver [[arch_facetint_race_default_inheritance]].
        Dim safeNpc As IList(Of NPC_FaceTintLayerData) = If(npc.FaceTintLayers, CType(New List(Of NPC_FaceTintLayerData)(), IList(Of NPC_FaceTintLayerData)))
        Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(safeNpc, race, isFemale, pluginManager)

        For Each m In merged
            Dim tl = m.Layer
            Dim opt = race.FindTintOption(tl.Index, isFemale)
            If opt Is Nothing Then Continue For
            If opt.Slot <> CUShort(TintSlot.SkinTone) Then Continue For
            If tl.Discriminator <> 1 Then Continue For   ' Palette only — color source for skin tone

            If tl.Color <> Color.Empty Then
                Dim alphaByte As Integer = Math.Max(0, Math.Min(255, CInt(Math.Round(CSng(tl.Value) * 2.55F))))
                Return Color.FromArgb(alphaByte, tl.Color.R, tl.Color.G, tl.Color.B)
            End If
        Next

        Return Nothing
    End Function

    ''' <summary>Replace the HDPT entry of <paramref name="targetPartType"/> in
    ''' <paramref name="headParts"/> with <paramref name="newHdptFormID"/>. No-op if the new
    ''' FormID is 0 or doesn't resolve to an HDPT record. Mirrors
    ''' <c>TESNPC::ChangeHeadPart</c> (SkinInterface.cpp:289-297): the engine looks up the
    ''' current HDPT of the same PartType and overwrites it. If no entry of that PartType
    ''' exists yet, we append the new one (LM in-game also calls AddHeadPart in that branch).</summary>
    ''' <summary>Replace the entry in <paramref name="headParts"/> whose PartType matches the
    ''' new HDPT's PartType, with <paramref name="newHdptFormID"/>. PartType is READ from the new
    ''' HDPT itself — we do NOT assume "head=Face" or "headRear=HeadRear". This matches engine
    ''' behaviour: F4SE's <c>SkinInterface.cpp:292</c> calls <c>npc->ChangeHeadPart(headPart, ...)</c>
    ''' which internally uses <c>headPart->type</c> as the target slot, not a hardcoded category.
    ''' So a JSON template that puts a Hair HDPT in "maleHead" replaces the Hair slot, not Face.
    ''' If no entry of that PartType exists in <paramref name="headParts"/>, the new HDPT is
    ''' appended (mirrors engine post-AddHeadPart fallthrough).</summary>
    Private Sub ApplyLmHdptReplacement(headParts As List(Of UInteger), newHdptFormID As UInteger,
                                        pluginManager As PluginManager)
        If newHdptFormID = 0UI Then Return
        Dim newRec = pluginManager.GetRecord(newHdptFormID)
        If newRec Is Nothing OrElse newRec.Header.Signature <> "HDPT" Then Return

        ' Read the target PartType from the NEW HDPT — engine-faithful (engine reads
        ' headPart->type for the slot lookup, doesn't accept it as an argument).
        Dim targetPartType As Integer
        Try
            Dim newHdpt = RecordParsers.ParseHDPT(newRec, pluginManager)
            targetPartType = newHdpt.PartType
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-HDPT-REPLACE] HDPT 0x{newHdptFormID:X8} parse failed; replacement skipped: {ex.GetType().Name}: {ex.Message}")
            Return
        End Try
        ' PartType=0 (Misc) is freestanding (extras like eyelashes, AO meshes) — those don't
        ' replace anything, they just accumulate. Add as freestanding.
        If targetPartType = 0 Then
            If Not headParts.Contains(newHdptFormID) Then headParts.Add(newHdptFormID)
            Return
        End If

        ' Find the index of the existing HDPT of the same PartType. Walk from the front; if
        ' multiple exist (shouldn't happen for vanilla NPCs but mods may inject) we replace the
        ' first and remove the rest to mirror engine post-Add (one slot per PartType).
        Dim replaceIdx As Integer = -1
        Dim removalIndices As New List(Of Integer)
        For i = 0 To headParts.Count - 1
            Dim r = pluginManager.GetRecord(headParts(i))
            If r Is Nothing OrElse r.Header.Signature <> "HDPT" Then Continue For
            Try
                Dim hd = RecordParsers.ParseHDPT(r, pluginManager)
                If hd.PartType = targetPartType Then
                    If replaceIdx < 0 Then
                        replaceIdx = i
                    Else
                        removalIndices.Add(i)
                    End If
                End If
            Catch
            End Try
        Next

        If replaceIdx >= 0 Then
            headParts(replaceIdx) = newHdptFormID
            ' Remove duplicates back-to-front so indices stay valid.
            For j = removalIndices.Count - 1 To 0 Step -1
                headParts.RemoveAt(removalIndices(j))
            Next
        Else
            headParts.Add(newHdptFormID)
        End If
    End Sub

    ''' <summary>Single source of truth for "the preset must reflect the LM template's bundle,
    ''' not just the id". Materializes <paramref name="preset.SkinTemplateId"/>'s head + headRear
    ''' HDPT swaps into <paramref name="preset.HeadPartFormIDs"/> and marks
    ''' <c>HasHeadPartFormIDs=True</c>, so any downstream consumer (Save ESP writer, Edit Face
    ''' seed, Copy Look snapshot) sees the same picture the live render already shows via
    ''' <see cref="ApplyPresetOverlayToNpcData"/>.
    '''
    ''' Idempotent: HDPTs already present in the list are NOT duplicated. Safe to call multiple
    ''' times on the same preset.
    '''
    ''' Called by every path that touches a preset whose <c>SkinTemplateId</c> is set:
    ''' • Load LooksMenu (after parsing the JSON).
    ''' • Copy Look (BuildPresetFromState, after copying SkinTemplateId from overlay).
    ''' • Edit Face seed (so the user sees the HDPTs the LM template injected).
    ''' • EditBody combo handler (when the user picks a template from the dropdown).
    ''' No-op when SkinTemplateId is empty or the resolver doesn't find the template.</summary>
    Public Sub MaterializeLmTemplateBundleToPreset(preset As LooksmenuLoader.LooksmenuPreset,
                                                    isFemale As Boolean,
                                                    resolver As ResolveLmSkinTemplateDelegate)
        If preset Is Nothing Then Return
        If String.IsNullOrEmpty(preset.SkinTemplateId) Then Return
        If resolver Is Nothing Then Return
        Dim tpl = resolver(preset.SkinTemplateId)
        If tpl Is Nothing Then Return

        Dim genderIdx As Integer = If(isFemale, 1, 0)
        Dim head As UInteger = tpl.HeadHdptFormID(genderIdx)
        Dim rear As UInteger = tpl.HeadRearHdptFormID(genderIdx)
        If head = 0UI AndAlso rear = 0UI Then Return

        ' Track each HDPT we inject so Retract can identify and remove ONLY the template's
        ' contribution later. AddHdptIfMissingPreset is idempotent vs the list, but the set
        ' should get the FormID even if it was already present in the list (which may have
        ' come from raw NPC PNAM and now coincides with the template — Retract still needs to
        ' know "the template asserted this one too").
        If head <> 0UI Then
            AddHdptIfMissingPreset(preset.HeadPartFormIDs, head)
            preset.LmTemplateInjectedHdptFormIDs.Add(head)
        End If
        If rear <> 0UI Then
            AddHdptIfMissingPreset(preset.HeadPartFormIDs, rear)
            preset.LmTemplateInjectedHdptFormIDs.Add(rear)
        End If
        ' Only flip Has* if it wasn't already True. If something else (Edit Face / Paste)
        ' set it before us, preserve that authority — we record our own flag separately.
        If Not preset.HasHeadPartFormIDs Then
            preset.HasHeadPartFormIDs = True
            preset.HasHeadPartFormIDsSetByTemplate = True
        End If
    End Sub

    ''' <summary>Inverse of <see cref="MaterializeLmTemplateBundleToPreset"/>: removes from
    ''' <paramref name="preset.HeadPartFormIDs"/> exactly the HDPTs a previous Materialize call
    ''' injected (tracked in <see cref="LooksmenuLoader.LooksmenuPreset.LmTemplateInjectedHdptFormIDs"/>),
    ''' and resets <c>HasHeadPartFormIDs=False</c> only if Materialize was the one that flipped it
    ''' (tracked via <c>HasHeadPartFormIDsSetByTemplate</c>). Edits made by Edit Face / Paste / Load
    ''' LM HeadParts arrays are preserved verbatim — Retract NEVER touches them.
    '''
    ''' Used by EditBody's LM template combo handler to do a clean revert before applying a new
    ''' template (or when the user goes back to "(none)").</summary>
    Public Sub RetractLmTemplateBundleFromPreset(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then Return
        If preset.LmTemplateInjectedHdptFormIDs.Count = 0 AndAlso
           Not preset.HasHeadPartFormIDsSetByTemplate Then Return

        For Each fid In preset.LmTemplateInjectedHdptFormIDs
            preset.HeadPartFormIDs.Remove(fid)
        Next
        preset.LmTemplateInjectedHdptFormIDs.Clear()
        If preset.HasHeadPartFormIDsSetByTemplate Then
            preset.HasHeadPartFormIDs = False
            preset.HasHeadPartFormIDsSetByTemplate = False
        End If
    End Sub

    Private Sub AddHdptIfMissingPreset(list As List(Of UInteger), hdptFormID As UInteger)
        If hdptFormID = 0UI Then Return
        If list.Contains(hdptFormID) Then Return
        list.Add(hdptFormID)
    End Sub

End Module
