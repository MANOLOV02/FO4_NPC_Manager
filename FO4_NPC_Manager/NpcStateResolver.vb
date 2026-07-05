Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split: NPC visual-state resolution (traits/inventory/model
''' template-chain walk, race fallbacks, skeleton key, leveled-NPC pick). Standalone class, DI via
''' constructor. Pure data resolution — no UI, no GL. See project_mainform_split.</summary>
Friend NotInheritable Class NpcStateResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _lvlnDataCache As Dictionary(Of UInteger, LVLN_Data)
    Private ReadOnly _genderFilter As Func(Of MainForm.GenderFilterMode)
    Private ReadOnly _resolveLmSkinTemplate As Func(Of String, LmSkinTemplate)
    Private Shared ReadOnly _rng As New Random()
    ''' <summary>Per-resolve cache of LVLN picks. When the same LVLN is encountered multiple times
    ''' during a single NPC resolution (e.g. Traits and Model both use same LVLN), the same NPC
    ''' is returned. This is how FO4 works: one random pick per LVLN per spawn.</summary>
    <ThreadStatic> Private Shared _lvlnPickCache As Dictionary(Of UInteger, UInteger)

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   lvlnDataCache As Dictionary(Of UInteger, LVLN_Data),
                   genderFilter As Func(Of MainForm.GenderFilterMode),
                   resolveLmSkinTemplate As Func(Of String, LmSkinTemplate))
        _ctx = ctx
        _materialResolver = materialResolver
        _appliedPresets = appliedPresets
        _lvlnDataCache = lvlnDataCache
        _genderFilter = genderFilter
        _resolveLmSkinTemplate = resolveLmSkinTemplate
    End Sub

    ''' <summary>Resolve the NPC's base visual state (traits + model, without outfit expansion).</summary>
    ''' <param name="host">The render host this resolution feeds. Supplies the host-scoped outfit
    ''' preview override (Edit Outfit picker) so the preview never mutates the shared overlay. Pass the
    ''' host being rendered into (<c>_renderHost</c> for the main preview).</param>
    Friend Function ResolveNPCBaseState(npc As NPC_Data, host As NpcRenderHost) As MainForm.NPCVisualState
        ' Fresh LVLN pick cache for this resolution — ensures consistent picks across categories
        _lvlnPickCache = New Dictionary(Of UInteger, UInteger)()

        Dim warnings As New List(Of String)
        Dim traits = ResolveTraitsStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)
        Dim inventory = ResolveInventoryStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings)

        If traits Is Nothing Then traits = NpcStateFactory.CreateOwnTraitsState(npc)
        If inventory Is Nothing Then inventory = NpcStateFactory.CreateOwnInventoryState(npc)

        ' [TEST: TPLT-traits-bucket] HeadTexture/HairColor/FacialHairColor/HeadParts/QNAM
        ' now sourced from `traits` (was `model`). OBTS combinations stay on `model`.
        Dim state As New MainForm.NPCVisualState With {
            .FormID = npc.FormID,
            .RootNpcFormID = npc.FormID,
            .IsFemale = traits.IsFemale,
            .RaceFormID = traits.RaceFormID,
            .SkinFormID = traits.SkinFormID,
            .DefaultOutfitFormID = inventory.DefaultOutfitFormID,
            .SleepOutfitFormID = inventory.SleepOutfitFormID,
            .HeadTextureFormID = traits.HeadTextureFormID,
            .HairColorFormID = traits.HairColorFormID,
            .FacialHairColorFormID = traits.FacialHairColorFormID,
            .HasTextureLighting = traits.HasTextureLighting,
            .TextureLightingColor = traits.TextureLightingColor,
            .TraitsSourceFormID = traits.SourceFormID
        }

        state.HeadPartFormIDs.AddRange(traits.HeadPartFormIDs)
        ' OBTE/OBTS + APPR now ride the Traits chain (see TraitsState / CreateOwnTraitsState): inherited
        ' via Use Traits, not Use Model/Animation. Base robot (no flags) -> own OBTS; rank variants
        ' (Use Traits) -> template source's OBTS. Measured 225 fixes / 0 regressions across the load order.
        state.ObjectTemplateOMODFormIDs.AddRange(traits.ObjectTemplateOMODFormIDs)
        state.ObjectTemplateCombinations.AddRange(traits.ObjectTemplateCombinations)
        state.HasObjectTemplate = traits.HasObjectTemplate
        state.AttachParentSlotFormIDs.AddRange(traits.AttachParentSlotFormIDs)

        ' "Show other gender" preview (ARMA/ARMO editors): render a DEFAULT actor of the target gender
        ' for this NPC's race — NOT the source NPC with a flipped bit. The source NPC's head parts, face
        ' texture, hair color, skin ARMO and body weights are gender-specific identity baked/authored for
        ' its ORIGINAL gender, so wipe them here and let ApplyRaceFallbacks (below) repopulate all of them
        ' from the RACE defaults for the TARGET gender. Downstream, everything gender-dependent keys off
        ' state.IsFemale: skeleton (ResolveSkeletonKey), height + body-weight bone-scaling (GetRaceHeight /
        ' ResolveBodyWeightData pick the RACE Male/Female block), body mesh (MOD2/MOD3), skin TXST
        ' (NAM0/NAM1) and material swaps (MO2S/MO3S). Gender-specific face morphs (chargen MSDK/MSDV +
        ' FMRS face bones) and the NPC FaceGen head are suppressed in the render orchestrator when the
        ' override is active (RenderCurrentStateAsync useFaceGen gate, BuildRenderPlan boneMorphsEnabled
        ' gate, BuildFaceMorphResolver early-return), so the head shows the race default without the
        ' original gender's baked face. HOST-SCOPED: the main render leaves PreviewGenderOverride Nothing,
        ' so this whole block is inert there.
        Dim genderOverrideActive As Boolean = host IsNot Nothing AndAlso host.PreviewGenderOverride.HasValue
        If genderOverrideActive Then
            state.IsFemale = host.PreviewGenderOverride.Value
            state.HeadPartFormIDs.Clear()
            state.HeadTextureFormID = 0UI
            state.HairColorFormID = 0UI
            state.SkinFormID = 0UI
            ' Null the NPC's MWGT so ResolveBodyWeights (inside ApplyRaceFallbacks) falls back to the
            ' RACE default weights for the target gender instead of reusing the source gender's values.
            traits.WeightThin = Nothing
            traits.WeightMuscular = Nothing
            traits.WeightFat = Nothing
        End If

        ApplyRaceFallbacks(state, traits, _ctx.PluginManager, AddressOf _ctx.ParseRaceCached)
        state.HeadPartFormIDs = state.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()

        ' Apply per-NPC LooksMenu overlay (if any) AFTER the template chain + race fallbacks ran.
        ' This is what makes the preset visible in the preview: HeadParts / HairColor / Weight in
        ' the state would otherwise come from the model/traits template source. The morph and tint
        ' overlays live in ApplyPresetOverlayToNpcData (consumed by BuildFaceMorphResolver and
        ' TryApplyFaceTints) — same mechanism, different access point.
        ' Skipped entirely under a gender override: the LM overlay is the source actor's own-gender
        ' identity (head parts / weights / skin / tint), which would re-inject exactly what the block
        ' above wiped for the "other gender" default-actor preview.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not genderOverrideActive AndAlso _appliedPresets.TryGetValue(state.RootNpcFormID, overlayPreset) Then
            If overlayPreset.HeadPartFormIDs.Count > 0 Then
                state.HeadPartFormIDs = overlayPreset.HeadPartFormIDs.Where(Function(id) id <> 0UI).Distinct().ToList()
            End If
            If overlayPreset.HairColorFormID <> 0UI Then
                state.HairColorFormID = overlayPreset.HairColorFormID
            End If
            If overlayPreset.WeightThin.HasValue Then state.WeightThin = overlayPreset.WeightThin.Value
            If overlayPreset.WeightMuscular.HasValue Then state.WeightMuscular = overlayPreset.WeightMuscular.Value
            If overlayPreset.WeightFat.HasValue Then state.WeightFat = overlayPreset.WeightFat.Value

            ' Skin overrides — same precedence the NpcRecordOverlay shadow applies, but on the
            ' state level. ResolveTraitsStateFromNPC re-parses the raw NPC by FormID (chain walk)
            ' and never sees the overlay, so without this block ResolveActorSkinTextureSet ends
            ' up reading state.SkinFormID = raw NPC.WNAM and the body/hands skin doesn't change.
            '   1) NPC.WNAM record override: SkinFormIDOverride.HasValue → take that value
            '      (Some(0) intentionally clears, downstream ApplyRaceFallbacks already substituted
            '      RACE.WNAM on raw zero so we re-trigger the same fallback here).
            '   2) LM SkinTemplate (F4SE bundle) wins after — mirrors SkinInterface.cpp:316-320.
            '      Bundle's face TXST + head/headRear HDPT live in shadow.HeadTextureFormID /
            '      shadow.HeadPartFormIDs; those flow into the state via the model/traits chain
            '      already (HeadPartFormIDs were just overwritten above; HeadTextureFormID is set
            '      below if the LM template carries one).
            If overlayPreset.SkinFormIDOverride.HasValue Then
                state.SkinFormID = overlayPreset.SkinFormIDOverride.Value
                If state.SkinFormID = 0UI Then
                    Dim raceRec2 = _ctx.PluginManager.GetRecord(state.RaceFormID)
                    If raceRec2 IsNot Nothing AndAlso raceRec2.Header.Signature = "RACE" Then
                        state.SkinFormID = _ctx.ParseRaceCached(raceRec2).SkinFormID
                    End If
                End If
            End If
            If Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
                Dim tpl = _resolveLmSkinTemplate(overlayPreset.SkinTemplateId)
                If tpl IsNot Nothing Then
                    If tpl.SkinArmoFormID <> 0UI Then state.SkinFormID = tpl.SkinArmoFormID
                    Dim genderIdx As Integer = If(state.IsFemale, 1, 0)
                    If tpl.FaceTxstFormID(genderIdx) <> 0UI Then
                        state.HeadTextureFormID = tpl.FaceTxstFormID(genderIdx)
                    End If
                    ' HDPT replacements — the helper reads each new HDPT's own PartType to
                    ' decide which slot to replace, engine-faithful per SkinInterface.cpp:292.
                    NpcRecordOverlay.ApplyLmHdptReplacementPublic(state.HeadPartFormIDs, tpl.HeadHdptFormID(genderIdx), _ctx.PluginManager)
                    NpcRecordOverlay.ApplyLmHdptReplacementPublic(state.HeadPartFormIDs, tpl.HeadRearHdptFormID(genderIdx), _ctx.PluginManager)
                End If
            End If

            ' Default outfit (NPC.DOFT) override — set by the Edit Outfit picker. Applied at the
            ' state level so BuildOutfitComboEntries (called right after ResolveNPCBaseState in
            ' LoadNPCOnDemandAsyncFromExisting) re-samples the chosen OTFT and the render consumes it.
            '   value <> 0 → OTFT override   ·   value = 0 → no outfit (naked)   ·   Nothing → preserve.
            If overlayPreset.DefaultOutfitFormIDOverride.HasValue Then
                state.DefaultOutfitFormID = overlayPreset.DefaultOutfitFormIDOverride.Value
            End If

            ' Body/face skin-tone parity. The face compositor consumes overlay tint layers via
            ' ApplyPresetOverlayToNpcData, so the face picks up the preset's skin tone. The body
            ' compositor (TryApplyBodySkinSoftLight) reads state.TextureLightingColor — which
            ' otherwise stays the original NPC's QNAM and produces a face/body tone mismatch.
            ' Derive an effective TextureLightingColor from the preset's slot 12 SkinTone tint
            ' (resolved via ResolveNpcSkinToneColor: same CLFM/TEND lookup the face compositor
            ' uses) so both meshes composite against the same colour. LooksMenu in-game gets
            ' parity for free because the engine reads from the actor's tint array, which the
            ' preset just rewrote — we have to re-derive it manually because QNAM is a vanilla
            ' record-level field that LooksMenu doesn't serialize.
            Dim presetSkin = _materialResolver.ResolveNpcSkinToneColor(state)
            If presetSkin.HasValue Then
                state.HasTextureLighting = True
                state.TextureLightingColor = presetSkin.Value
            End If
        End If

        ' Out-of-band outfit preview (Edit Outfit picker) — applied LAST and scoped to the host being
        ' rendered into, so it NEVER touches the shared overlay (_appliedPresets): browsing outfits in
        ' the picker leaves the main render's committed state untouched. Inert on the main host
        ' (OutfitPreviewActive=False). Value: Nothing → raw record DOFT · 0 → naked · fid → OTFT/draft.
        If host IsNot Nothing AndAlso host.OutfitPreviewActive Then
            state.DefaultOutfitFormID = If(host.OutfitPreviewOverride, inventory.DefaultOutfitFormID)
        End If

        Return state
    End Function

    Friend Function CloneVisualState(state As MainForm.NPCVisualState) As MainForm.NPCVisualState
        Dim clone As New MainForm.NPCVisualState With {
            .FormID = state.FormID,
            .RootNpcFormID = state.RootNpcFormID,
            .TraitsSourceFormID = state.TraitsSourceFormID,
            .InventorySourceFormID = state.InventorySourceFormID,
            .ModelSourceFormID = state.ModelSourceFormID,
            .VariantLabel = state.VariantLabel,
            .IsFemale = state.IsFemale,
            .RaceFormID = state.RaceFormID,
            .SkinFormID = state.SkinFormID,
            .DefaultOutfitFormID = state.DefaultOutfitFormID,
            .SleepOutfitFormID = state.SleepOutfitFormID,
            .HeadTextureFormID = state.HeadTextureFormID,
            .HairColorFormID = state.HairColorFormID,
            .FacialHairColorFormID = state.FacialHairColorFormID,
            .HasTextureLighting = state.HasTextureLighting,
            .TextureLightingColor = state.TextureLightingColor,
            .WeightThin = state.WeightThin,
            .WeightMuscular = state.WeightMuscular,
            .WeightFat = state.WeightFat
        }
        clone.HeadPartFormIDs.AddRange(state.HeadPartFormIDs)
        clone.LoadoutArmorFormIDs.AddRange(state.LoadoutArmorFormIDs)
        For Each kv In state.LoadoutArmorContextKeywords
            clone.LoadoutArmorContextKeywords(kv.Key) = New List(Of UInteger)(kv.Value)
        Next
        clone.ObjectTemplateOMODFormIDs.AddRange(state.ObjectTemplateOMODFormIDs)
        clone.ObjectTemplateCombinations.AddRange(state.ObjectTemplateCombinations)
        clone.HasObjectTemplate = state.HasObjectTemplate
        clone.AttachParentSlotFormIDs.AddRange(state.AttachParentSlotFormIDs)
        Return clone
    End Function

    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCached). Falls back to a
    ''' direct <c>RecordParsers.ParseRACE</c> when Nothing — keeps the offline bake path pure.</param>
    Friend Shared Sub ApplyRaceFallbacks(state As MainForm.NPCVisualState, traits As MainForm.TraitsState, pluginManager As PluginManager,
                                         Optional parseRace As Func(Of PluginRecord, RACE_Data) = Nothing)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return

        Dim raceRec = pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            ' No RACE record: all-Default MWGT can't be resolved → leave 0; explicit values pass through.
            state.WeightThin = traits.WeightThin.GetValueOrDefault(0.0F)
            state.WeightMuscular = traits.WeightMuscular.GetValueOrDefault(0.0F)
            state.WeightFat = traits.WeightFat.GetValueOrDefault(0.0F)
            Return
        End If

        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), RecordParsers.ParseRACE(raceRec, pluginManager))

        ' Materialize NPC.MWGT into final 3 floats. Substitution rule lives in ResolveBodyWeights.
        ' Done before the head/skin fallbacks so callers reading state.WeightX downstream always
        ' see resolved values.
        Dim resolvedWeights = NpcStateFactory.ResolveBodyWeights(traits, race, state.IsFemale)
        state.WeightThin = resolvedWeights.Thin
        state.WeightMuscular = resolvedWeights.Muscular
        state.WeightFat = resolvedWeights.Fat

        If state.SkinFormID = 0UI Then
            state.SkinFormID = race.SkinFormID
        End If

        ' FTST PROPIO del NPC (0 si no tiene), capturado ANTES del fallback DFTM de abajo. Acá
        ' state.HeadTextureFormID aún es el FTST del record; las líneas siguientes lo pisan con DFTM cuando es 0.
        ' Lo usa ResolveTextureSet para la precedencia FTST > HDPT.TNAM > DFTM (sin esto no se distingue FTST de DFTM).
        state.ExplicitHeadTextureFormID = state.HeadTextureFormID

        If state.HeadPartFormIDs.Count = 0 Then
            If state.IsFemale Then
                state.HeadPartFormIDs.AddRange(race.FemaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadPartFormIDs.AddRange(race.MaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        ElseIf state.HeadTextureFormID = 0UI Then
            If state.IsFemale Then
                state.HeadTextureFormID = If(race.FemaleDefaultFaceTextureFormID <> 0UI, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
            Else
                state.HeadTextureFormID = If(race.MaleDefaultFaceTextureFormID <> 0UI, race.MaleDefaultFaceTextureFormID, race.FemaleDefaultFaceTextureFormID)
            End If
        End If

        ' HairColor fallback: when NPC.HCLF is absent (and the template chain didn't supply one
        ' either — Model/Animation traits already collapsed by ResolveModelAnimationStateFromNPC),
        ' the engine reads RACE.HCLF[gender] (Default Hair Colors). Mirror that here. Each gender
        ' slot can be NULL per wbFormIDCk([NULL, CLFM]) at wbDefinitionsFO4.pas:11575 — same
        ' "own gender first, fallback to the other" rule we use for DefaultFaceTexture above.
        If state.HairColorFormID = 0UI Then
            Dim ownGender = If(state.IsFemale, race.FemaleDefaultHairColorFormID, race.MaleDefaultHairColorFormID)
            Dim otherGender = If(state.IsFemale, race.MaleDefaultHairColorFormID, race.FemaleDefaultHairColorFormID)
            state.HairColorFormID = If(ownGender <> 0UI, ownGender, otherGender)
        End If
    End Sub

    Friend Function ResolveSkeletonKey(state As MainForm.NPCVisualState, warnings As List(Of String)) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return ""

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = _ctx.ParseRaceCached(raceRec)
        Dim skeletonPath = If(state.IsFemale, race.FemaleSkeletonPath, race.MaleSkeletonPath)
        If String.IsNullOrWhiteSpace(skeletonPath) Then
            skeletonPath = If(race.MaleSkeletonPath <> "", race.MaleSkeletonPath, race.FemaleSkeletonPath)
        End If

        Dim dictionaryKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(skeletonPath)
        If dictionaryKey = "" Then warnings.Add($"No skeleton path resolved for race {state.RaceFormID:X8}")
        Return dictionaryKey
    End Function

    Friend Function ResolveTraitsStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As MainForm.TraitsState
        Dim npc = _ctx.GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = NpcStateFactory.CreateOwnTraitsState(npc)
        If visited.Contains(formID) Then Return own

        Dim acbsOppGender As Boolean = (npc.AcbsFlags And &H80000UI) <> 0UI

        If Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Traits) Then
            Return own
        End If

        visited.Add(formID)
        Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
        Dim sourceRec = _ctx.PluginManager.GetRecord(sourceFormID)

        Dim resolved = ResolveTraitsStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Traits template unresolved for {NpcManagerFormat.DescribeNpc(npc)}")
        Return own
    End Function

    Private Function ResolveInventoryStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As MainForm.InventoryState
        Dim npc = _ctx.GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = NpcStateFactory.CreateOwnInventoryState(npc)
        If visited.Contains(formID) Then Return own
        If Not NpcTemplateHelpers.HasTemplateFlag(npc.TemplateFlags, NPC_TemplateCategory.Inventory) Then Return own

        visited.Add(formID)
        Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Inventory)
        Dim resolved = ResolveInventoryStateFromTemplateSource(sourceFormID, visited, warnings)
        visited.Remove(formID)

        If resolved IsNot Nothing Then Return resolved

        warnings.Add($"Inventory template unresolved for {NpcManagerFormat.DescribeNpc(npc)}")
        Return own
    End Function

    ' NOTE: the former Model/Animation bucket (ResolveModelAnimationStateFromNPC + its template-source
    ' helper + ModelAnimationState/CreateOwnModelAnimationState) was removed: the only data it ever
    ' carried was the NPC ObjectTemplate (OBTE/OBTS), which is inherited via Use Traits — not Use
    ' Model/Animation — so it now rides the Traits chain (ResolveTraitsStateFromNPC). Measured across
    ' all 4365 load-order NPC_: 225 fixes, 0 regressions (GutsyTemplateProbe).

    Private Function ResolveTraitsStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As MainForm.TraitsState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Traits", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveTraitsStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveInventoryStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As MainForm.InventoryState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Inventory", visited, warnings)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveInventoryStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    Private Function ResolveTemplateSourceRecord(sourceFormID As UInteger, categoryName As String, visited As HashSet(Of UInteger), warnings As List(Of String)) As PluginRecord
        If sourceFormID = 0UI Then Return Nothing

        Dim sourceRecord = _ctx.PluginManager.GetRecord(sourceFormID)
        If sourceRecord Is Nothing Then
            warnings.Add($"Missing {categoryName} template source {sourceFormID:X8}")
            Return Nothing
        End If

        Select Case sourceRecord.Header.Signature
            Case "NPC_"
                Return sourceRecord
            Case "LVLN"
                Dim resolvedFormID = ResolveSingleLeveledTemplate(sourceRecord, warnings)
                If resolvedFormID = 0UI Then Return Nothing
                If visited.Contains(resolvedFormID) Then Return Nothing
                Return ResolveTemplateSourceRecord(resolvedFormID, categoryName, visited, warnings)
            Case Else
                warnings.Add($"Unsupported {categoryName} template source {sourceRecord.Header.Signature} [{sourceFormID:X8}]")
                Return Nothing
        End Select
    End Function

    ''' <summary>Pick a random leaf NPC from a LVLN, using Count as weight, recursing into nested LVLNs.
    ''' Ignores Level requirements and ChanceNone for NPC leveled lists.</summary>
    ''' <remarks>Thin entry point: acquires the PluginManager records READ lock for the whole walk,
    ''' then allocates a per-resolution record-fetch memo and delegates to the recursive worker. Holding
    ''' the read lock freezes <c>AllRecords</c> against the only post-load writer (the Save read-back's
    ''' <c>MergeOverridePlugin</c>, which takes the WRITE lock) for the walk's short duration, so every
    ''' fetch — including the memoized first-seen records and the un-memoized re-fetches an unmemoized
    ''' walk would do — observes ONE consistent record set. That is what makes the memo provably
    ''' identical to an unmemoized walk even under a concurrent Save: no mid-walk record swap can occur.
    ''' Concurrent readers (the render thread's own read-locked lookups) are unaffected — only the rare
    ''' Save writer waits. Inside the body every fetch uses the lock-free <c>GetRecordNoLock</c> via
    ''' <see cref="GetRecordMemoized"/> (the read lock is already held), so there is no re-entrant lock
    ''' acquisition. The memo still collapses the redundant fetches the walk would otherwise make (every
    ''' entry re-fetched on every call; the same child records re-fetched across nested-LVLN recursion
    ''' branches; gender-filter leaves re-fetched). RNG / eligibility / weight accumulation are
    ''' untouched.</remarks>
    Friend Function PickWeightedRandomFromLVLN(lvlnFormID As UInteger, visited As HashSet(Of UInteger)) As UInteger
        Return _ctx.PluginManager.RunUnderRecordsReadLock(
            Function() PickWeightedRandomFromLVLN(lvlnFormID, visited, New Dictionary(Of UInteger, PluginRecord)()))
    End Function

    ''' <summary>Recursive worker for <see cref="PickWeightedRandomFromLVLN"/>. <paramref name="recordMemo"/>
    ''' is threaded through the whole walk (including nested-LVLN recursion) so each FormID is fetched
    ''' from the PluginManager at most once per top-level resolution. A FormID that resolves to Nothing
    ''' is cached as Nothing too, preserving the original null handling without a re-fetch.</summary>
    Private Function PickWeightedRandomFromLVLN(lvlnFormID As UInteger, visited As HashSet(Of UInteger),
                                                recordMemo As Dictionary(Of UInteger, PluginRecord)) As UInteger
        If lvlnFormID = 0UI OrElse visited.Contains(lvlnFormID) Then Return 0UI
        visited.Add(lvlnFormID)

        Dim lvln As LVLN_Data = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            Dim lvlnRec = GetRecordMemoized(lvlnFormID, recordMemo)
            If lvlnRec Is Nothing OrElse lvlnRec.Header.Signature <> "LVLN" Then Return 0UI
            lvln = RecordParsers.ParseLVLN(lvlnRec, _ctx.PluginManager)
        End If

        ' Build weighted list of leaf NPC FormIDs: each entry contributes Count copies
        Dim weightedLeaves As New List(Of UInteger)()

        For Each entry In lvln.Entries
            If entry.FormID = 0UI Then Continue For
            Dim entryRec = GetRecordMemoized(entry.FormID, recordMemo)
            If entryRec Is Nothing Then Continue For

            Dim weight = Math.Max(CInt(entry.Count), 1)

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    For i = 0 To weight - 1
                        weightedLeaves.Add(entry.FormID)
                    Next
                Case "LVLN"
                    ' Recurse into nested LVLN: pick from sub-list, weighted by this entry's Count
                    For i = 0 To weight - 1
                        Dim subPick = PickWeightedRandomFromLVLN(entry.FormID, New HashSet(Of UInteger)(visited), recordMemo)
                        If subPick <> 0UI Then weightedLeaves.Add(subPick)
                    Next
            End Select
        Next

        If weightedLeaves.Count = 0 Then Return 0UI

        ' Apply gender filter if set
        Dim genderFilter = _genderFilter()
        If genderFilter <> MainForm.GenderFilterMode.Random Then
            Dim filtered = weightedLeaves.Where(Function(fid)
                                                    Dim npc As NPC_Data = Nothing
                                                    If _ctx.NpcCache.TryGetValue(fid, npc) Then
                                                        Return If(genderFilter = MainForm.GenderFilterMode.Female, npc.IsFemale, Not npc.IsFemale)
                                                    End If
                                                    Dim npcRec = GetRecordMemoized(fid, recordMemo)
                                                    If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then Return True
                                                    Dim parsed = RecordParsers.ParseNPC(npcRec, "", _ctx.PluginManager)
                                                    Return If(genderFilter = MainForm.GenderFilterMode.Female, parsed.IsFemale, Not parsed.IsFemale)
                                                End Function).ToList()
            If filtered.Count > 0 Then weightedLeaves = filtered
        End If

        Dim picked = weightedLeaves(_rng.Next(weightedLeaves.Count))
        Return picked
    End Function

    ''' <summary>Memoized record fetch: returns the cached record for <paramref name="formID"/> if the
    ''' walk has fetched it already, else fetches from the PluginManager and stores the result (Nothing
    ''' included). Uses the lock-free <c>GetRecordNoLock</c> because the caller (the
    ''' <see cref="PickWeightedRandomFromLVLN"/> entry point) already holds the records read lock for the
    ''' whole walk via <c>RunUnderRecordsReadLock</c> — so this returns BYTE-IDENTICALLY what the
    ''' lock-taking <c>GetRecord</c> would (same <c>AllRecords</c>, same Nothing-on-miss) minus a
    ''' redundant re-entrant lock acquisition, and every fetch in the walk sees the same writer-frozen
    ''' record set.</summary>
    Private Function GetRecordMemoized(formID As UInteger, recordMemo As Dictionary(Of UInteger, PluginRecord)) As PluginRecord
        Dim rec As PluginRecord = Nothing
        If recordMemo.TryGetValue(formID, rec) Then Return rec
        rec = _ctx.PluginManager.GetRecordNoLock(formID)
        recordMemo(formID) = rec
        Return rec
    End Function

    ''' <summary>Pick a single NPC from a LVLN for template resolution. Uses Count as weight.
    ''' Results are cached per NPC resolution to ensure consistent picks across categories.</summary>
    Private Function ResolveSingleLeveledTemplate(lvlnRec As PluginRecord, warnings As List(Of String)) As UInteger
        Dim lvlnFormID = lvlnRec.Header.FormID

        ' Check cache first — same LVLN must return same pick within one NPC resolution
        If _lvlnPickCache IsNot Nothing Then
            Dim cached As UInteger = 0UI
            If _lvlnPickCache.TryGetValue(lvlnFormID, cached) Then
                Return cached
            End If
        End If

        Dim picked = PickWeightedRandomFromLVLN(lvlnFormID, New HashSet(Of UInteger)())

        If picked = 0UI Then
            warnings.Add($"Leveled template {NpcManagerFormat.DescribeRecord(lvlnRec)} has no usable entries")
            Return 0UI
        End If

        If _lvlnPickCache IsNot Nothing Then _lvlnPickCache(lvlnFormID) = picked
        Return picked
    End Function

End Class
