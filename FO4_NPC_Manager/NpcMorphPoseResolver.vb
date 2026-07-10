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

''' <summary>Phase 2 of the MainForm split: morph + pose resolution (face/body morph resolvers,
''' FMRS face-bone transforms, body-weight data, race height, merged NPC pose, facial-bone regions).
''' Standalone class, DI. Skeleton LOADING (PrepareSkeleton) + its caches stay in MainForm.
''' See project_mainform_split.</summary>
Friend NotInheritable Class NpcMorphPoseResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _overlay As Func(Of NPC_Data, UInteger, NPC_Data)
    Private ReadOnly _hostProvider As Func(Of NpcRenderHost)
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    ''' <summary>Resolve an LM body-overlay ("tattoo") template by (id, isFemale) — injected from
    ''' MainForm.ResolveOverlayTemplate so the per-gender template cache stays in MainForm (mirrors how
    ''' NpcStateResolver receives AddressOf ResolveLmSkinTemplate). Nothing on an unknown id ⇒ that
    ''' overlay contributes no layer (engine parity: GetTemplateByName null → ForEachOverlayBySlot skips,
    ''' OverlayInterface.cpp:443-448).</summary>
    Private ReadOnly _resolveOverlayTemplate As Func(Of String, Boolean, OverlayTemplate)
    Public Sub New(ctx As NpcRenderContext, overlay As Func(Of NPC_Data, UInteger, NPC_Data), hostProvider As Func(Of NpcRenderHost),
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   resolveOverlayTemplate As Func(Of String, Boolean, OverlayTemplate))
        _ctx = ctx
        _overlay = overlay
        _hostProvider = hostProvider
        _appliedPresets = appliedPresets
        _resolveOverlayTemplate = resolveOverlayTemplate
    End Sub

    ''' <summary>Build a face morph resolver for the given NPC visual state.
    ''' Uses MSDK/MSDV morph presets from Chargen.tri (via RACE mapping) and
    ''' FMRI/FMRS face bone transforms (applied via skeleton DeltaTransform).
    ''' Body weight morphs are NOT applied (vanilla uses hardcoded bone scaling, not TRI).</summary>
    Friend Function BuildFaceMorphResolver(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing Then Return Nothing

        ' "Show other gender" preview: the NPC's chargen (MSDK/MSDV) vertex morphs are gender-specific
        ' (baked against its own BaseMale/BaseFemaleHeadChargen.tri) and don't apply to a default target-
        ' gender head, so emit no face morphs — the race default head renders un-morphed.
        If host IsNot Nothing AndAlso host.PreviewGenderOverride.HasValue Then Return Nothing

        ' Get the full NPC_Data for the model source (the NPC whose face we're rendering)
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' No morph data at all? Skip. GAME-AWARE: FO4 face morphs live in MorphValues (MSDK/MSDV); SSE
        ' face morphs live in NAM9 (sliders) + NAMA (types), with MorphValues empty. So for SSE gate on
        ' Nam9Raw/NamaRaw instead (project_sse_nam9_morph_map).
        Dim isSse = (npcData.Game = Config_App.Game_Enum.Skyrim)
        If isSse Then
            If (npcData.Nam9Raw Is Nothing OrElse npcData.Nam9Raw.Length < 76) AndAlso (npcData.NamaRaw Is Nothing) Then Return Nothing
        ElseIf npcData.MorphValues.Count = 0 Then
            Return Nothing
        End If

        ' Get RACE morph definitions for mapping MSDK keys ? morph names
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        Dim morphValueDefs = race.MorphValues
        Dim morphPresetDefs = If(state.IsFemale, race.FemaleMorphPresets, race.MaleMorphPresets)
        Dim morphGroups = If(state.IsFemale, race.FemaleMorphGroups, race.MaleMorphGroups)


        ' Dump raw MSDK/MSDV table from this NPC (to see what keys+weights the record really has).
        ' Cross-reference each key against RACE.MSID (sliders) / MPPI (presets) / MPGS (group sliders)
        ' to show where each morph came from and why it's in the NPC.
        Dim sliderIndexSet As New HashSet(Of UInteger)
        If morphValueDefs IsNot Nothing Then
            For Each mv In morphValueDefs : sliderIndexSet.Add(mv.Index) : Next
        End If
        Dim presetIndexMap As New Dictionary(Of UInteger, String)
        If morphPresetDefs IsNot Nothing Then
            For Each mp In morphPresetDefs
                If Not presetIndexMap.ContainsKey(mp.Index) Then presetIndexMap(mp.Index) = mp.MorphName
            Next
        End If
        For Each kvp In npcData.MorphValues
            Dim key = kvp.Key
            Dim value = kvp.Value
            Dim classification As String

            Dim value1 As String = Nothing

            If sliderIndexSet.Contains(key) Then
                Dim mvDef = morphValueDefs.FirstOrDefault(Function(m) m.Index = key)
                classification = $"SLIDER (RACE.MSID) MSM0='{mvDef.MinName}' MSM1='{mvDef.MaxName}'"
            ElseIf presetIndexMap.TryGetValue(key, value1) Then
                classification = $"PRESET (RACE.MPPI) morphName='{value1}'"
            Else
                classification = "??? (not found in RACE MSID/MPPI for this gender)"
            End If
        Next

        ' Dump RACE morph structure for this gender: how many groups, and within each group how
        ' many presets and what morph name they point to. Shows whether the 4x DefaultFaceType0
        ' belongs to 4 distinct groups (as hypothesized) or something else.
        If morphGroups IsNot Nothing Then
            For Each g In morphGroups
                Dim presetSummary As New System.Text.StringBuilder()
                For k = 0 To g.Presets.Count - 1
                    If k > 0 Then presetSummary.Append(" | ")
                    Dim p = g.Presets(k)
                    presetSummary.Append($"MPPI=0x{p.Index:X8}[MPPN='{p.PresetName}']→MPPM='{p.MorphName}'")
                Next
                Dim slidersSummary As String = ""
                If g.SliderIndices IsNot Nothing AndAlso g.SliderIndices.Count > 0 Then
                    Dim sliderKeys = String.Join(",", g.SliderIndices.Select(Function(k) $"0x{k:X8}"))
                    slidersSummary = $" MPGS=[{sliderKeys}]"
                End If
            Next
        End If

        Return New NpcMorphResolver(
            npcData,
            morphValueDefs:=morphValueDefs,
            morphPresetDefs:=morphPresetDefs,
            meshDictKeys:=renderData.MeshDictKeys,
            shapeChargenTriPaths:=renderData.ShapeChargenTriPaths,
            shapeRaceMorphTriPaths:=renderData.ShapeRaceMorphTriPaths,
            raceEditorId:=RecordParsers.ResolveMorphRaceEditorId(race, _ctx.PluginManager))
    End Function

    ''' <summary>Returns the effective BodySlide slider dict for an NPC: the overlay preset's
    ''' BodyMorphSliders if one is applied, otherwise an empty dict (vanilla NPCs have no record-
    ''' level BodyMorphs — F4SE-only field).</summary>
    Private Function GetEffectiveBodyMorphSliders(rootNpcFormID As UInteger) As Dictionary(Of String, Single)
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(rootNpcFormID, preset) AndAlso preset.BodyMorphSliders IsNot Nothing Then
            Return preset.BodyMorphSliders
        End If
        Return New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
    End Function

    ''' <summary>Build a BodySlide vertex morph resolver for the NPC's effective slider state.
    ''' Returns Nothing when CheckBoxBodyTri is unchecked, when no sliders are active, or when
    ''' there are no shapes — lets MultiMorphResolver short-circuit.
    ''' The CheckBoxBodyTri toggle gates the entire BodySlide vertex-morph layer (BODYTRI .tri
    ''' lookup + slider apply). Unchecked = render exactly as if the JSON had no BodyMorphs key
    ''' for this NPC.</summary>
    Friend Function BuildBodyMorphResolver(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing OrElse renderData Is Nothing Then Return Nothing
        If Not host.Toggles.BodyTri Then Return Nothing
        Dim sliders = GetEffectiveBodyMorphSliders(state.RootNpcFormID)
        If sliders Is Nothing OrElse sliders.Count = 0 Then Return Nothing
        Return New BodySlideMorphResolver(sliders, renderData.MeshDictKeys)
    End Function

    ''' <summary>Build the SSE vanilla body-weight (_0/_1) vertex-LERP resolver for the NPC.
    ''' SSE-ONLY: returns Nothing for FO4 (which has no _0/_1 weight morph — body weight there is MWGT
    ''' bone-scaling), so the FO4 render path is untouched. The actor weight is NAM7 (SSE "weight" slot;
    ''' FO4 NAM7 is Unused), read from the Traits-source NPC_ (same source that owns NAM7), defaulting to
    ''' 100. The per-shape ARMA _0/_1 flag gate lives inside the resolver. See SSE_BODY_MORPH_PLAN §1.7.</summary>
    Friend Function BuildSseBodyWeightResolver(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing) As IMorphResolver
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing OrElse renderData Is Nothing Then Return Nothing
        ' Weight (NAM7) rides the Traits bucket → read it from the same source the face appearance uses.
        ' Wrap in the overlay so an Edit Body SSE weight edit (preset.SseWeight → shadow.Nam7Raw) renders
        ' live — same _overlay(...) seam BuildFaceMorphResolver uses for NAM9/NAMA.
        Dim npcData = _overlay(_ctx.GetParsedNpc(NpcStateFactory.FaceAppearanceSourceFormID(state)), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing
        If npcData.Game <> Config_App.Game_Enum.Skyrim Then Return Nothing
        Dim w = If(npcData.Nam7Raw IsNot Nothing AndAlso npcData.Nam7Raw.Length >= 4, BitConverter.ToSingle(npcData.Nam7Raw, 0), 100.0F)
        Dim t = Math.Max(0.0F, Math.Min(1.0F, w / 100.0F))
        Return New SseBodyWeightMorphResolver(t, state.IsFemale, renderData.MeshDictKeys, renderData.ShapeCandidate, _ctx)
    End Function

    ' The SSE HEAD/HAIR weight morph (formerly BuildSseHeadWeightResolver + SseHeadWeightMorphResolver +
    ' the hardcoded SseHeadWeightDelta table) is now applied inside the FACE morph plan: BuildFaceMorphPlanFromNam9
    ' adds a "SkinnyMorph" channel at frac = 1 - clamp(NAM7/100,0,1), read from each shape's own mesh .tri (merged
    ' by LoadTriForShape). Engine-derived (SkyrimSE.exe applier 0x1403B90D0 → 0x140430190), agnostic and race-aware
    ' (femalehead/argonian/khajiit/hairNN each ship their own SkinnyMorph), and shared by render + bake.

    ''' <summary>Resolve the applied preset's LM body overlays ("tattoos") into per-shape
    ''' <see cref="IRenderableShape.OverlayLayers"/> on the SKIN shapes of <paramref name="renderData"/>.
    ''' This is the render integration for the overlays feature: it SETS the layers directly (unlike the
    ''' morph resolvers, which return an IMorphResolver) because overlay layers are extra material passes,
    ''' not vertex deltas.
    '''
    ''' <para><b>Engine model</b> (F4SEPlugins-master/f4ee/OverlayInterface.cpp): for a biped slot S the
    ''' engine finds the SKIN shapes on that slot (the clones whose lighting material is kType_SkinTint,
    ''' :104), and for each applied overlay (iterated by priority ASCENDING — a multimap, :436) it looks up
    ''' <c>template->slotMaterial[S]</c> (ForEachOverlayBySlot :445-447); an overlay contributes a layer to
    ''' that shape IFF its template defines a material for slot S. LoadMaterialData (:186-197) then adds the
    ''' preset's offsetUV to the material's UV offset, multiplies scaleUV, and for a tintable BGEM sets the
    ''' effect base color (:227). We replicate that here, pre-baking the transform/tint onto the loaded
    ''' material per the OverlayMaterialLayer contract (the lib renders the supplied material as-is).</para>
    '''
    ''' <para><b>Skin shape + biped slot identification</b> (the riskiest inference): a shape is a SKIN
    ''' shape when its owning candidate's Kind = Skin — i.e. it was collected from the skin ARMO
    ''' (state.SkinFormID) at NpcMeshCollector.vb:276-278. That is the app's direct analogue of the engine
    ''' "clone the skin shapes" step; the per-shape→candidate map is renderData.ShapeCandidate
    ''' (NpcMeshCollector.vb:1862-1865). The candidate's biped slot(s) come from MeshCandidate.SlotMask,
    ''' bit (N-30) = biped slot N (BipedSlots.vb:6-7) — body = bit 3 (slot 33), hands = bits 4/5 (34/35),
    ''' head = bit 0 (slot 30). We additionally require the material to be skin-tinted
    ''' (material.NifShaderType = SkinTint) to mirror the engine's kType_SkinTint gate (:104) — a skin ARMO
    ''' NIF can carry non-skin shapes (eyes, etc.) and those must not receive body overlays.</para>
    '''
    ''' <para><b>Clearing</b>: when the NPC has no applied preset, or the preset has no overlays, EVERY
    ''' shape's OverlayLayers is set to Nothing and the method returns — so switching/clearing an NPC never
    ''' leaks a prior NPC's tattoos. (Each render plan rebuilds fresh shape instances, so a leak is already
    ''' impossible, but the explicit clear matches the behavior contract and is cheap.)</para></summary>
    Friend Sub ResolveOverlayLayers(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return

        ' GAME-AWARE: SSE (Skyrim) body overlays are RaceMenu path-based (no f4ee template catalog), sourced
        ' from the preset's SSE carrier and synthesized into materials here — a separate code path from the
        ' FO4 template resolution below. The FO4 path stays byte-identical (behind this gate). §3.2/§3.3.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ResolveSseOverlayLayers(state, renderData)
            Return
        End If

        ' Effective overlays for this NPC. Absent preset / HasOverlays=False / empty list ⇒ no tattoos.
        ' (HasOverlays=False means the preset never declared the Overlays field — preserve-raw semantics,
        ' same Has* convention the rest of the preset uses; raw NPCs have no record-level overlays.)
        Dim overlays As List(Of LooksmenuLoader.OverlayEntry) = Nothing
        If state IsNot Nothing Then
            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If _appliedPresets.TryGetValue(state.RootNpcFormID, preset) AndAlso preset IsNot Nothing AndAlso
               preset.HasOverlays AndAlso preset.Overlays IsNot Nothing AndAlso preset.Overlays.Count > 0 Then
                overlays = preset.Overlays
            End If
        End If

        ' No overlays → clear every shape and bail (guarantees no carry-over).
        If overlays Is Nothing OrElse _resolveOverlayTemplate Is Nothing Then
            For Each sh In renderData.Shapes
                If sh IsNot Nothing Then sh.OverlayLayers = Nothing
            Next
            Return
        End If

        ' Iterate overlays by Priority ASCENDING so a higher-priority overlay ends up LAST in each shape's
        ' layer list = drawn on top (the lib draws layers in list order; OverlayMaterialLayer.vb:53-57).
        ' OrderBy is a stable sort, so equal-priority entries keep preset/insertion order — matching the
        ' engine multimap, which also preserves insertion order within a priority bucket.
        Dim orderedOverlays = overlays.OrderBy(Function(e) e.Priority).ToList()

        For Each shape In renderData.Shapes
            If shape Is Nothing Then Continue For

            ' Engine membership gate (OverlayInterface.cpp:104 kType_SkinTint, via HasSkinChildren
            ' :832-848): an overlay applies to ANY skin-tinted shape on the matching worn biped slot —
            ' the naked body skin AND the exposed-skin parts of an OUTFIT (e.g. an outfit that leaves
            ' the arms bare). The ONLY membership test is "is this geometry skin-tint?"; fabric / armor
            ' (non-skin-tint) is excluded. The previous extra Kind=Skin restriction was WRONG — it
            ' excluded outfit skin-tint shapes (user-reported: overlays only showed on the naked skin).
            ' The candidate is still needed, but ONLY for its worn SlotMask (which biped slot the item
            ' occupies, so a body overlay lands on body-slot skin, a hand overlay on hand-slot skin).
            Dim cand As MainForm.MeshCandidate = Nothing
            Dim hasCand = renderData.ShapeCandidate.TryGetValue(shape, cand) AndAlso cand IsNot Nothing
            If Not hasCand OrElse Not ShapeIsSkinTinted(shape) Then
                shape.OverlayLayers = Nothing
                If Logger.Enabled Then
                    Dim kindS = If(cand IsNot Nothing, cand.Kind.ToString(), "<none>")
                    Dim tinted = ShapeIsSkinTinted(shape)
                    Logger.LogLazy(Function() $"[OVERLAY-DIAG] skip shape='{shape.ShapeName}' hasCand={hasCand} kind={kindS} skinTinted={tinted}")
                End If
                Continue For
            End If

            ' Biped slot INDICES (0..30 = SlotMask bit positions = overlays.json "slot" values) this skin
            ' shape occupies. NOT slot numbers — the template keys its materials by the index (see
            ' BipedSlotIndicesFromMask). Body=index 3, hands=index 4/5.
            Dim slotIndices = BipedSlotIndicesFromMask(cand.SlotMask)
            If slotIndices.Count = 0 Then
                shape.OverlayLayers = Nothing
                Continue For
            End If

            Dim layers As New List(Of OverlayMaterialLayer)
            For Each entry In orderedOverlays
                If entry Is Nothing OrElse String.IsNullOrEmpty(entry.TemplateId) Then Continue For
                Dim tpl = _resolveOverlayTemplate(entry.TemplateId, state.IsFemale)
                If tpl Is Nothing OrElse tpl.SlotMaterials Is Nothing Then
                    If Logger.Enabled Then Logger.LogLazy(Function() $"[OVERLAY-DIAG] template not resolved: id='{entry.TemplateId}' female={state.IsFemale}")
                    Continue For
                End If

                ' One layer per biped-slot-index this shape covers that the template defines a material
                ' for. A skin shape almost always covers a single skin slot (index 3 body / 4-5 hands /
                ' 0 head), but a multi-slot skin ARMA could legitimately add more than one — mirror the
                ' engine, which keys slotMaterial by the (index) slot being processed.
                For Each slotIdx In slotIndices
                    Dim slotMatPath As String = Nothing
                    If Not tpl.SlotMaterials.TryGetValue(slotIdx, slotMatPath) Then Continue For
                    If String.IsNullOrEmpty(slotMatPath) Then Continue For

                    Dim layer = BuildOverlayLayer(shape, slotMatPath, entry)
                    If layer IsNot Nothing Then layers.Add(layer)
                Next
            Next

            shape.OverlayLayers = If(layers.Count > 0, layers, Nothing)
            If Logger.Enabled Then
                Dim layerCount = layers.Count
                Dim idxList = String.Join(",", slotIndices)
                Logger.LogLazy(Function() $"[OVERLAY-DIAG] skin shape='{shape.ShapeName}' slotIdx=[{idxList}] mask=0x{cand.SlotMask:X8} overlays={orderedOverlays.Count} → layers={layerCount}")
            End If
        Next
    End Sub

    ''' <summary>Mirror of the engine kType_SkinTint gate (OverlayInterface.cpp:104): true when the shape's
    ''' resolved material is a skin-tint lighting material. Uses the SAME per-shape signal the app's
    ''' body-texture substitution keys off (NpcMaterialResolver.vb:1039, material.NifShaderType = SkinTint).
    ''' Defensive: any missing material link ⇒ False (a shape without a resolved skin material is not a
    ''' tattoo target).</summary>
    Private Shared Function ShapeIsSkinTinted(shape As IRenderableShape) As Boolean
        Dim rel = shape.ShapeMaterial
        If rel Is Nothing OrElse rel.material Is Nothing Then Return False
        Return rel.material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.SkinTint OrElse rel.material.SkinTint
    End Function

    ''' <summary>True for the head (FaceTint shader) — the target of RaceMenu "Face [Ovl{n}]" face-paint overlays.</summary>
    Private Shared Function ShapeIsFace(shape As IRenderableShape) As Boolean
        Dim rel = shape.ShapeMaterial
        If rel Is Nothing OrElse rel.material Is Nothing Then Return False
        Return rel.material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint
    End Function

    ''' <summary>True when a RaceMenu overlay node name is a FACE overlay ("Face [Ovl{n}]" / "Face [SOvl{n}]").</summary>
    Private Shared Function SseOverlayIsFaceNode(nodeName As String) As Boolean
        If String.IsNullOrEmpty(nodeName) Then Return False
        Return nodeName.TrimStart().StartsWith("Face", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Enumerate the biped slot INDICES (0..30) set in a SlotMask — i.e. the BIT POSITIONS,
    ''' NOT the actual slot numbers. This is deliberate: overlay templates key their slot materials by
    ''' the value in overlays.json's <c>"slot"</c> field, which is the biped object INDEX 0..30 (the
    ''' engine iterates <c>for i = 0; i &lt; 31</c> and looks up <c>slotMaterial[i]</c> — F4SE
    ''' OverlayInterface.cpp:425-454 / F4EEUpdateOverlays::Run), and that index equals the SlotMask bit
    ''' position (BipedSlots.vb:6-7 — bit (N-30) = biped slot N). So body = bit 3 = index 3 = slot 33,
    ''' hands = bit 4/5 = index 4/5. We MUST return the index (bit position) so it matches the template's
    ''' SlotMaterials keys; returning bit+30 (the slot number 33) never matched key 3 ⇒ no overlay layers.</summary>
    Private Shared Function BipedSlotIndicesFromMask(slotMask As UInteger) As List(Of Integer)
        Dim result As New List(Of Integer)
        For bit = 0 To 31
            If (slotMask And (1UI << bit)) <> 0UI Then result.Add(bit)
        Next
        Return result
    End Function

    ''' <summary>Load an overlay template's slot material and pre-bake the LooksMenu per-instance transform
    ''' (offsetUV/scaleUV) + tint onto it, then wrap it as an <see cref="OverlayMaterialLayer"/>.
    '''
    ''' <para>Material load mirrors the canonical chain (NifContent_Class.vb:216-228 / NpcMaterialResolver
    ''' LoadVanillaBodyMaterial:73-74): normalize the path with CorrectMaterialPath, strip then re-add the
    ''' Materials\ prefix in the Deserialize call, choosing GetType(BGEM) for .bgem (effect/tattoo) else
    ''' GetType(BGSM). We pass the skin shape's own NifShape (INiShape) + NifContent so Deserialize can seed
    ''' the alpha fields / resolve ShaderType from the NIF the same way the base shape's material was loaded
    ''' (it uses them only for that seeding — safe to reuse the skin shape's pair).</para>
    '''
    ''' <para>Pre-bake matches LoadMaterialData (OverlayInterface.cpp:186-197):
    ''' <c>oU += offsetUV.x; oV += offsetUV.y; sU *= scaleUV.x; sV *= scaleUV.y</c>. The renderer's overlay
    ''' pass uploads BOTH uvOffset (UOffset/VOffset) AND uvScale (UScale/VScale) for the layer's material
    ''' (Render.vb:3200-3201), so scaleUV is honored, not offset-only — we multiply UScale/VScale. Tint
    ''' (entry.Tint, rgba 0..1) is set as the BGEM base color (effectMaterial->kBaseColor, :227) via
    ''' BaseColor (System.Drawing.Color, BGEM-only; no-op on a BGSM, which matches the engine guarding the
    ''' tint write behind the effect-material branch).</para>
    '''
    ''' <para>Returns Nothing on load failure (defensive — a bad overlay material must not break the render).</para></summary>
    Private Shared Function BuildOverlayLayer(skinShape As IRenderableShape, slotMaterialPath As String,
                                              entry As LooksmenuLoader.OverlayEntry) As OverlayMaterialLayer
        Try
            Dim fullpath = FO4UnifiedMaterial_Class.CorrectMaterialPath(slotMaterialPath).StripPrefix(MaterialsPrefix)
            If String.IsNullOrEmpty(fullpath) Then Return Nothing
            Dim matType As Type = If(fullpath.EndsWith(".bgem", StringComparison.OrdinalIgnoreCase), GetType(BGEM), GetType(BGSM))

            Dim mat As New FO4UnifiedMaterial_Class()
            mat.Deserialize(MaterialsPrefix & fullpath, matType, skinShape.NifShape, skinShape.NifContent)

            ' offsetUV: add to the material's UV offset (engine :190-191). Nothing ⇒ default (0,0) = no-op.
            If entry.OffsetUV IsNot Nothing AndAlso entry.OffsetUV.Length >= 2 Then
                mat.UOffset += entry.OffsetUV(0)
                mat.VOffset += entry.OffsetUV(1)
            End If

            ' scaleUV: multiply the material's UV scale (engine :193-194). Nothing ⇒ default (1,1) = no-op.
            ' Honored by the renderer's overlay pass (Render.vb:3201 uploads uvScale from UScale/VScale).
            If entry.ScaleUV IsNot Nothing AndAlso entry.ScaleUV.Length >= 2 Then
                mat.UScale *= entry.ScaleUV(0)
                mat.VScale *= entry.ScaleUV(1)
            End If

            ' tint: BGEM effect base color (engine :227, behind kHasTintColor). Nothing ⇒ no tint. BaseColor
            ' is <BGEMOnly>, so on a BGSM the setter is a no-op — matching the engine guarding tint behind
            ' the effect-material branch. rgba are 0..1 (preset native); clamp before the 0..255 byte cast.
            If entry.Tint IsNot Nothing AndAlso entry.Tint.Length >= 4 Then
                mat.BaseColor = Color.FromArgb(ClampUnitToByte(entry.Tint(3)), ClampUnitToByte(entry.Tint(0)),
                                               ClampUnitToByte(entry.Tint(1)), ClampUnitToByte(entry.Tint(2)))
            End If

            Return New OverlayMaterialLayer With {
                .Material = New Nifcontent_Class_Manolo.RelatedMaterial_Class With {.material = mat, .path = fullpath}
            }
        Catch ex As Exception
            Logger.LogLazy(Function() $"[OVERLAY] failed to load overlay material '{slotMaterialPath}': {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>SSE (Skyrim) analogue of <see cref="ResolveOverlayLayers"/>: source PATH-based RaceMenu
    ''' overlays from the applied preset's <see cref="LooksmenuLoader.LooksmenuPreset.SseBodyOverlays"/>
    ''' carrier and synthesize an <see cref="OverlayMaterialLayer"/> per matching skin-tinted shape.
    '''
    ''' <para>Shape membership reuses the same gate as the FO4 path (<see cref="ShapeIsSkinTinted"/> +
    ''' <c>renderData.ShapeCandidate</c>), but the biped-slot match is SSE-specific: the overlay node name
    ''' (<c>Body</c>/<c>Hands</c>/<c>Feet</c>) maps to the SSE biped slot bits it covers
    ''' (<see cref="SseOverlayNodeSlotBits"/>, from the Skyrim table BipedSlots.vb) and the overlay lands on
    ''' any skin-tinted shape whose SlotMask intersects those bits. Draw order = list order (skee applies
    ''' Ovl0..N in node order; index 0 drawn first = bottom).</para>
    '''
    ''' <para>Blend is the SAME coplanar alpha-over decal as FO4 (Option B) — no per-mode Pegtop (the blend
    ''' mode is not in the .jslot, §3.1/§3.2). Absent preset / empty carrier ⇒ every shape cleared.</para></summary>
    Private Sub ResolveSseOverlayLayers(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult)
        Dim overlays As List(Of FO4_Base_Library.RaceMenuJslot.JslotOverlayNode) = Nothing
        Dim skinOverrides As List(Of FO4_Base_Library.RaceMenuJslot.JslotSkinOverride) = Nothing
        If state IsNot Nothing Then
            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If _appliedPresets.TryGetValue(state.RootNpcFormID, preset) AndAlso preset IsNot Nothing Then
                If preset.SseBodyOverlays IsNot Nothing AndAlso preset.SseBodyOverlays.Count > 0 Then overlays = preset.SseBodyOverlays
                If preset.SseSkinOverrides IsNot Nothing AndAlso preset.SseSkinOverrides.Count > 0 Then skinOverrides = preset.SseSkinOverrides
            End If
        End If

        ' Nothing at all (no tattoos AND no skin overrides) → clear every shape and bail (no carry-over).
        If overlays Is Nothing AndAlso skinOverrides Is Nothing Then
            For Each sh In renderData.Shapes
                If sh IsNot Nothing Then sh.OverlayLayers = Nothing
            Next
            Return
        End If

        For Each shape In renderData.Shapes
            If shape Is Nothing Then Continue For
            Dim cand As MainForm.MeshCandidate = Nothing
            Dim hasCand = renderData.ShapeCandidate.TryGetValue(shape, cand) AndAlso cand IsNot Nothing
            Dim isBodySkin = hasCand AndAlso ShapeIsSkinTinted(shape)
            ' The head (FaceTint shader) carries the RaceMenu "Face [Ovl{n}]" overlays (face paint), same decal
            ' mechanism as body tattoos but membership is by node name (Face) not a biped slot — the face isn't
            ' worn on a slot. skee64 g_enableFaceOverlays (OverlayInterface).
            Dim isFace = ShapeIsFace(shape)
            If Not isBodySkin AndAlso Not isFace Then
                shape.OverlayLayers = Nothing
                Continue For
            End If

            Dim layers As New List(Of OverlayMaterialLayer)
            If isBodySkin Then
                ' Skin overrides (RaceMenu body-paint replacing the skin texture) drawn FIRST = UNDER the tattoos.
                ' Membership = raw NiOverride slot-mask intersect (the .jslot slotMask uses the same bit=slot−30
                ' convention as the mesh candidate's worn SlotMask). Tint-only overrides (no diffuse) are persisted
                ' but not rendered here (a coplanar decal can't multiply the base skin — would flat-fill it).
                If skinOverrides IsNot Nothing Then
                    For Each sk In skinOverrides
                        If sk Is Nothing OrElse String.IsNullOrEmpty(sk.DiffusePath) Then Continue For
                        If (cand.SlotMask And sk.SlotMask) = 0UI Then Continue For
                        Dim layer = BuildSseSkinOverrideLayer(shape, sk)
                        If layer IsNot Nothing Then layers.Add(layer)
                    Next
                End If
            End If
            ' Overlays ON TOP. Iterate the applied list in REVERSE so the LAST list entry is drawn first (bottom)
            ' and the FIRST entry is drawn last (on top) — matching the "top of the list = drawn on top" UI + the
            ' skee node order (Ovl0 bottom → OvlN top). Body/Hands/Feet overlays go on the matching worn-slot skin
            ' shape; Face overlays go on the FaceTint head shape.
            If overlays IsNot Nothing Then
                For oi = overlays.Count - 1 To 0 Step -1
                    Dim ov = overlays(oi)
                    If ov Is Nothing OrElse String.IsNullOrEmpty(ov.DiffusePath) Then Continue For
                    Dim applies As Boolean
                    If SseOverlayIsFaceNode(ov.NodeName) Then
                        applies = isFace
                    Else
                        Dim nodeBits = SseOverlayNodeSlotBits(ov.NodeName)
                        applies = isBodySkin AndAlso nodeBits <> 0UI AndAlso (cand.SlotMask And nodeBits) <> 0UI
                    End If
                    If Not applies Then Continue For
                    Dim layer = BuildSseOverlayLayer(shape, ov)
                    If layer IsNot Nothing Then layers.Add(layer)
                Next
            End If
            shape.OverlayLayers = If(layers.Count > 0, layers, Nothing)
            If Logger.Enabled Then
                Dim layerCount = layers.Count
                Dim ovN = If(overlays IsNot Nothing, overlays.Count, 0)
                Dim skN = If(skinOverrides IsNot Nothing, skinOverrides.Count, 0)
                Logger.LogLazy(Function() $"[OVERLAY-SSE] shape='{shape.ShapeName}' mask=0x{cand.SlotMask:X8} overlays={ovN} skinOverrides={skN} → layers={layerCount}")
            End If
        Next
    End Sub

    ''' <summary>Synthesize an <see cref="OverlayMaterialLayer"/> for a RaceMenu SKIN override (body-paint):
    ''' identical decal machinery to <see cref="BuildSseOverlayLayer"/> (opaque alpha-over decal fully covers
    ''' the skin region = visually replaces the skin diffuse, which is the NiOverride texture-override effect),
    ''' but the diffuse/normal/tint come from the per-slot <see cref="RaceMenuJslot.JslotSkinOverride"/> instead
    ''' of a tattoo node.</summary>
    Private Shared Function BuildSseSkinOverrideLayer(skinShape As IRenderableShape, sk As FO4_Base_Library.RaceMenuJslot.JslotSkinOverride) As OverlayMaterialLayer
        Try
            Dim mat As New FO4UnifiedMaterial_Class()   ' fresh wrapper = normalized BGEM (effect material)
            mat.Diffuse_or_Base_Texture = If(sk.DiffusePath, "")
            If Not String.IsNullOrEmpty(sk.NormalPath) Then mat.NormalTexture = sk.NormalPath
            mat.AlphaBlendEnabled = True
            mat.Decal = True
            If sk.HasTint Then
                mat.BaseColor = Color.FromArgb(ClampUnitToByte(sk.TintA), ClampUnitToByte(sk.TintR),
                                               ClampUnitToByte(sk.TintG), ClampUnitToByte(sk.TintB))
            End If
            Return New OverlayMaterialLayer With {
                .Material = New Nifcontent_Class_Manolo.RelatedMaterial_Class With {.material = mat, .path = If(sk.DiffusePath, "")}
            }
        Catch ex As Exception
            Logger.LogLazy(Function() $"[OVERLAY-SSE] failed to synthesize skin override (slotMask=0x{sk.SlotMask:X8}, {sk.DiffusePath}): {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>SSE biped-slot bitmask (bit = slot−30) that a RaceMenu overlay node covers. AUTHORITATIVE per
    ''' skee64 (RaceMenu source), NOT reasoned from the slot table: overlays install on the FIXED biped parts
    ''' <c>BGSBipedObjectForm::kPart_Body/Hands/Feet</c> (OverlayInterface.cpp:1055/1069/1083) = SINGLE slots
    ''' 32/33/37 (bits 2/3/7) — never forearms(34)/calves(38). The overlay node-name set is a CLOSED, hardcoded
    ''' list — <c>Body/Hands/Feet/Face/Hair "[Ovl{n}]"</c> + spell <c>"[SOvl{n}]"</c> (OverlayInterface.h:23-46);
    ''' the StartsWith prefix catches both [Ovl] and [SOvl]. Any OTHER node in the .jslot <c>overrides</c> array is
    ''' a non-overlay node-override (custom node transform/texture) and correctly returns 0 (it is not a body
    ''' tattoo, and it is preserved verbatim on save). Face/Hair belong to the face pipeline → 0 here.</summary>
    Private Shared Function SseOverlayNodeSlotBits(nodeName As String) As UInteger
        If String.IsNullOrEmpty(nodeName) Then Return 0UI
        Dim n = nodeName.TrimStart()
        If n.StartsWith("Body", StringComparison.OrdinalIgnoreCase) Then Return (1UI << 2)   ' kPart_Body → slot 32
        If n.StartsWith("Hands", StringComparison.OrdinalIgnoreCase) Then Return (1UI << 3)  ' kPart_Hands → slot 33
        If n.StartsWith("Feet", StringComparison.OrdinalIgnoreCase) Then Return (1UI << 7)   ' kPart_Feet → slot 37
        Return 0UI
    End Function

    ''' <summary>Synthesize an <see cref="OverlayMaterialLayer"/> for a PATH-based RaceMenu overlay: build an
    ''' in-memory effect material (a fresh <see cref="FO4UnifiedMaterial_Class"/> defaults to a normalized
    ''' BGEM — the tattoo/effect kind) with the overlay's diffuse in the base slot, normal in the normal slot
    ''' (when present), tint as the BGEM base color, and alpha-over blend enabled so the decal pass composites
    ''' it over the skin (Render.vb reads AlphaBlendEnabled → HasAlphaBlend, and the blend funcs default to
    ''' SRC_ALPHA/INV_SRC_ALPHA). Wraps it exactly like <see cref="BuildOverlayLayer"/> (a RelatedMaterial_Class).
    ''' The texture-set API used: <c>Diffuse_or_Base_Texture</c> (→ BGEM.BaseTexture, FO4UnifiedMaterial_Class.vb:539),
    ''' <c>NormalTexture</c> (:467), <c>BaseColor</c> (BGEM base color+alpha, :2018), <c>AlphaBlendEnabled</c> (:431),
    ''' <c>Decal</c> (:960). Paths are stored raw; the render normalizes each via CorrectTexturePath.</summary>
    Private Shared Function BuildSseOverlayLayer(skinShape As IRenderableShape, ov As FO4_Base_Library.RaceMenuJslot.JslotOverlayNode) As OverlayMaterialLayer
        Try
            Dim mat As New FO4UnifiedMaterial_Class()   ' fresh wrapper = normalized BGEM (effect material)
            mat.Diffuse_or_Base_Texture = If(ov.DiffusePath, "")
            If Not String.IsNullOrEmpty(ov.NormalPath) Then mat.NormalTexture = ov.NormalPath
            ' Coplanar alpha-over decal (Option B). Blend funcs default to SRC_ALPHA / INV_SRC_ALPHA.
            mat.AlphaBlendEnabled = True
            mat.Decal = True
            If ov.HasTint Then
                mat.BaseColor = Color.FromArgb(ClampUnitToByte(ov.TintA), ClampUnitToByte(ov.TintR),
                                               ClampUnitToByte(ov.TintG), ClampUnitToByte(ov.TintB))
            End If
            Return New OverlayMaterialLayer With {
                .Material = New Nifcontent_Class_Manolo.RelatedMaterial_Class With {.material = mat, .path = If(ov.DiffusePath, "")}
            }
        Catch ex As Exception
            Logger.LogLazy(Function() $"[OVERLAY-SSE] failed to synthesize overlay '{ov.NodeName}' ({ov.DiffusePath}): {ex.Message}")
            Return Nothing
        End Try
    End Function

    ''' <summary>Map a 0..1 color component to a 0..255 byte, clamping out-of-range inputs.</summary>
    Private Shared Function ClampUnitToByte(v As Single) As Integer
        Dim n = CInt(Math.Round(v * 255.0F))
        Return Math.Min(255, Math.Max(0, n))
    End Function

    ''' <summary>Build the hair zap resolver from the per-shape ShapeZapHairParts map, gated on the
    ''' "Render headwear" toggle. Returns Nothing when headwear rendering is OFF (the zap must lift so the
    ''' mesh shows whole) or when no shape carries a non-None ZapParts. Also flips shape.ApplyZaps for the
    ''' flagged shapes so the renderer honours the VertexMask=-1 the resolver's zap channel sets.</summary>
    Friend Function BuildHairTopZapResolver(renderData As MainForm.PreviewResolutionResult, host As NpcRenderHost) As HairTopZapResolver
        If renderData Is Nothing Then Return Nothing
        Dim zapParts As New Dictionary(Of IRenderableShape, HairZapParts)()
        ' Render headwear OFF → no zap (la mesh se ve entera, igual que destapar el head part ocluido).
        If host IsNot Nothing AndAlso host.Toggles.RenderHeadwear Then
            For Each kv In renderData.ShapeZapHairParts
                If kv.Key IsNot Nothing AndAlso kv.Value <> HairZapParts.None Then zapParts(kv.Key) = kv.Value
            Next
        End If
        ' ApplyZaps por shape: ON sólo para las shapes que zapeamos ahora. Las demás OFF para que un
        ' toggle previo no deje el flag pegado (la mask se limpia sola en ApplyMorphPlan, pero el flag
        ' de la shape es persistente). Aplica a TODAS las shapes flageables, no sólo las activas.
        For Each kv In renderData.ShapeZapHairParts
            If kv.Key IsNot Nothing Then kv.Key.ApplyZaps = zapParts.ContainsKey(kv.Key)
        Next
        ' [HAIRZAP-DIAG] which shapes carry a non-None ZapParts in the render data, and which made it into
        ' the resolver's zap set (ApplyZaps). A hairline flagged at SelectWinningCandidates but missing
        ' here would mean its shape object diverged between LoadNifShapes and the resolver.
        If Logger.Enabled Then
            For Each kv In renderData.ShapeZapHairParts
                Dim shName = If(kv.Key Is Nothing, "<null>", If(kv.Key.ShapeName, "?"))
                Dim partsVal = kv.Value
                Dim inSet = kv.Key IsNot Nothing AndAlso zapParts.ContainsKey(kv.Key)
                Dim applyZapsVal = kv.Key IsNot Nothing AndAlso kv.Key.ApplyZaps
                Logger.LogLazy(Function() $"[HAIRZAP-DIAG] resolver shape='{shName}' ShapeZapParts={partsVal} inZapSet={inSet} ApplyZaps={applyZapsVal} renderHeadwear={If(host IsNot Nothing, host.Toggles.RenderHeadwear, False)}")
            Next
        End If
        If zapParts.Count = 0 Then Return Nothing
        Return New HairTopZapResolver(zapParts)
    End Function

    ''' <summary>Cache of parsed FacialBoneRegions files per race/gender key (e.g. "HumanRace:female").</summary>
    Private Shared ReadOnly _facialBoneRegionsCache As New Dictionary(Of String, FacialBoneRegionsFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Load and parse the per-race HumanRaceFacialBoneRegions<Gender>.txt JSON file.
    ''' Returns Nothing if the file doesn't exist or can't be parsed.</summary>
    Friend Shared Function GetFacialBoneRegionsForRace(race As RACE_Data, isFemale As Boolean) As FacialBoneRegionsFile
        If race Is Nothing OrElse String.IsNullOrEmpty(race.EditorID) Then Return Nothing

        Dim genderKey = If(isFemale, "Female", "Male")
        Dim cacheKey = race.EditorID & ":" & genderKey

        Dim cached As FacialBoneRegionsFile = Nothing
        If _facialBoneRegionsCache.TryGetValue(cacheKey, cached) Then Return cached

        ' Build candidate paths. Use race.EditorID as the base name (HumanRace, GhoulRace, etc.)
        Dim dataPath = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dataPath, loc) Then
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End If

        Try
            Dim bytes = loc.GetBytes()
            ' Dump the raw JSON to a sibling file so we can see exactly what the engine reads
            ' (independent of our parser). Compares against xEdit hex IDs to catch any parser
            ' bug. Path: same directory as the log file, named per gender.
            If Logger.Enabled Then
                Try
                    Dim dumpPath = IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"fbr_dump_{race.EditorID}_{genderKey}.txt")
                    IO.File.WriteAllBytes(dumpPath, bytes)
                Catch dumpEx As Exception
                End Try
            End If
            Dim parsed = FacialBoneRegionsFile.ParseFromBytes(bytes)
            _facialBoneRegionsCache(cacheKey) = parsed
            Return parsed
        Catch ex As Exception
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End Try
    End Function

    ''' <summary>Build a pose of face bone deltas from the NPC's FMRI/FMRS subrecords.
    ''' For each FMRI region, look up the region in the race's FacialBoneRegions JSON, then
    ''' for each bone in the region compute a per-axis delta by signed-lerping FMRS sliders
    ''' (clamped to [-1,+1]) across Minima/Default/Maxima, scaled by FMIN. Bone names are
    ''' prefixed with "skin_" to match SkeletonDictionary. Returns Nothing if no regions
    ''' file is found or no non-zero FMRS values contribute.</summary>
    ''' <summary>Thin instance wrapper over <see cref="FaceBonePoseBuilder.BuildFaceBoneTransforms"/>;
    ''' resolves the overlay-applied NPC + race + regions JSON from the state, then delegates the
    ''' FMRS math to the helper module. Real impl lives in the module so offline bake reuses it.</summary>
    Private Function BuildFaceBoneTransforms(state As MainForm.NPCVisualState) As Poses_class
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing OrElse npcData.FaceMorphs.Count = 0 Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return Nothing

        ' NNAM ("Neck Fat Adjustments Scale") is NOT part of this FMRS face-bone pose anymore — it is
        ' a CK RUNTIME scale of the shared "Neck" bone applied to the live skeleton (head+body), now
        ' threaded through BuildBodyWeightPose as Layer 2 (see ResolveNeckNnamScale).
        Return FaceBonePoseBuilder.BuildFaceBoneTransforms(npcData, regionsFile)
    End Function

    ''' <summary>Resolve the CK RUNTIME NNAM neck-bone scale for the NPC (the shared "Neck" bone
    ''' scale the engine applies live to head+body — NEVER baked). Mirrors the resolution the FMRS
    ''' wrapper does (overlay-applied NPC, cached RACE, race+gender regions JSON, RACE.{gender}NeckNNAMX/Y)
    ''' and delegates the math to <see cref="FaceBonePoseBuilder.ComputeNeckNnamScale"/>. Returns
    ''' (1,1) on any missing piece. Consumed by <see cref="BuildBodyWeightPose"/> (Layer 2).</summary>
    Private Function ResolveNeckNnamScale(state As MainForm.NPCVisualState) As (ScaleY As Single, ScaleZ As Single)
        If state Is Nothing Then Return (1.0F, 1.0F)

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return (1.0F, 1.0F)

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return (1.0F, 1.0F)
        Dim race = _ctx.ParseRaceCached(raceRec)

        Dim regionsFile = GetFacialBoneRegionsForRace(race, state.IsFemale)
        If regionsFile Is Nothing Then Return (1.0F, 1.0F)

        Dim neckNnamX As Single = If(state.IsFemale, race.FemaleNeckNNAMX, race.MaleNeckNNAMX)
        Dim neckNnamY As Single = If(state.IsFemale, race.FemaleNeckNNAMY, race.MaleNeckNNAMY)

        Return FaceBonePoseBuilder.ComputeNeckNnamScale(npcData, regionsFile, neckNnamX, neckNnamY)
    End Function

    ''' <summary>POST-PASE de la compensación NNAM anti-propagación. Llamar JUSTO DESPUÉS de
    ''' <c>ApplyBoneMorphPose</c> sobre el MISMO skeleton (BuildBodyWeightPose ya metió el scale del
    ''' NNAM en el hueso "Neck", Layer 2). Como <c>GetGlobalTransform</c> compone la cadena de padres,
    ''' esa escala PROPAGARÍA a los hijos (Neck → HEAD_Offset → HEAD → cara) = el bug "cara adelante".
    ''' Para cancelarla, a CADA hijo DIRECTO de "Neck" se le compone <c>comp = L_C⁻¹ ∘ S⁻¹ ∘ L_C</c>
    ''' sobre su MorphDelta existente (el FMRS que ApplyBoneMorphPose ya aplicó):
    ''' <c>MorphDelta_C' = comp ∘ FMRS_C</c>. Resultado: la escala queda SOLO en los verts pegados a
    ''' "Neck"; cara/cuello y sus morphs FMRS intactos. <c>comp</c> puede tener SHEAR (hijos rotados,
    ''' p.ej. skin_bone_*Neckmuscle*) → se asigna DIRECTO a <c>MorphDeltaTransform</c> (PoseTransformData
    ''' no representa shear). Orden de composición verificado numéricamente (Tools/NifVtxCompare --verifycomp).
    ''' <para>GATEO AUTOMÁTICO: la S se LEE de <c>neckBone.MorphDeltaTransform</c> (lo que el "Neck"
    ''' realmente recibió), NO se re-resuelve. Si el "Neck" no escaló (body-weight OFF — el scale se emite
    ''' solo dentro de la rama bodyWeightEnabled/hasSculpt de BuildMergedNpcPose —, NNAM inactivo, o
    ''' suprimido) su MorphDeltaTransform es Nothing → NO-OP. Así la comp (S⁻¹) NUNCA se aplica sin la S
    ''' correspondiente en el padre.</para>
    ''' Idempotencia: re-correr tras cada ApplyBoneMorphPose (que resetea la capa morph) — NO llamar dos
    ''' veces sin re-aplicar la pose (compondría la comp dos veces).</summary>
    Friend Sub ApplyNeckNnamCompensation(skeleton As SkeletonInstance)
        If skeleton Is Nothing OrElse Not skeleton.HasSkeleton Then Return
        Dim neckBone As HierarchiBone_class = Nothing
        If Not skeleton.SkeletonDictionary.TryGetValue("Neck", neckBone) OrElse neckBone Is Nothing Then Return

        ' S = lo que el "Neck" REALMENTE recibió en la capa morph (= la escala NNAM), leído del estado ya
        ' aplicado por ApplyBoneMorphPose. Derivarlo de acá (en vez de re-resolver el NNAM) AUTO-GATEA la
        ' comp con el scale: si el "Neck" NO escaló — body-weight OFF (el scale se emite solo dentro de la
        ' rama bodyWeightEnabled/hasSculpt de BuildMergedNpcPose), NNAM inactivo, o suprimido → sin entry en
        ' la pose → MorphDeltaTransform = Nothing → NO se compensa nada. Evita el bug de aplicar S⁻¹ a los
        ' hijos sin la S correspondiente en el padre (encogería la cara con body-weight destildado).
        Dim s = neckBone.MorphDeltaTransform
        If s Is Nothing Then Return
        Dim sInv = s.Inverse()

        Dim applied As Integer = 0
        For Each child In neckBone.Childrens
            If child Is Nothing OrElse child.OriginalLocaLTransform Is Nothing Then Continue For
            Dim lc = child.OriginalLocaLTransform
            ' comp = L_C⁻¹ ∘ S⁻¹ ∘ L_C — cancela la propagación del morph del "Neck" al subárbol del hijo.
            Dim comp = lc.Inverse().ComposeTransforms(sInv).ComposeTransforms(lc)
            Dim existing = child.MorphDeltaTransform
            ' MorphDelta_C' = comp ∘ (morph previo del hijo, p.ej. FMRS) — preserva su deformación.
            child.MorphDeltaTransform = If(existing Is Nothing, comp, comp.ComposeTransforms(existing))
            applied += 1
        Next
        Dim a = applied, ev = s.EffectiveScale
        Logger.LogLazy(Function() $"[NNAM-COMP] post-pase: comp (S_efectiva={ev}) aplicado a {a} hijo(s) directo(s) de 'Neck' (∘ morph previo).")
    End Sub

    ''' <summary>Resolve the NPC's MWGT weights and the RACE's per-bone weight scale data for
    ''' use by the skeleton resolver. Returns Nothing if the NPC has no MWGT or the RACE has
    ''' no bone data for the NPC's gender.</summary>
    Private Function ResolveBodyWeightData(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult) As (Wt As Single, Wm As Single, Wf As Single, GenderBlock As RACE_BoneDataGender, MrsvValues As List(Of Single), ArmaDeltas As Dictionary(Of String, System.Numerics.Vector3))
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' Use state.WeightX (resolved by ApplyRaceFallbacks) — these are post-sentinel-substitution
        ' floats. Reading npcData.WeightX directly here would propagate the Single.MaxValue sentinel
        ' for NPCs whose MWGT carries "Default" slots, which then explodes the body-weight bone
        ' scales to infinity downstream.
        Dim wt As Single = state.WeightThin
        Dim wm As Single = state.WeightMuscular
        Dim wf As Single = state.WeightFat
        Dim armaDeltas = renderData?.ArmaBoneScaleDeltas
        Dim hasMwgt = (wt + wm + wf) >= 0.001F
        Dim hasArmaDeltas = (armaDeltas IsNot Nothing AndAlso armaDeltas.Count > 0)
        If Not hasMwgt AndAlso Not hasArmaDeltas Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCached(raceRec)

        ' Log the FaceGen clamps for reference. TBD whether they apply to body BSMS output
        ' or only to face slider*FMIN. Not applying any clamp formula without spec.
        ' NNAM ("Neck Fat Adjustments Scale") is resolved separately (ResolveNeckNnamScale) and
        ' threaded into BuildBodyWeightPose as Layer 2 — the CK RUNTIME scale of the shared "Neck"
        ' bone (head+body), NOT a per-bone BSMS/MRSV body-weight input, so it is not part of this
        ' RACE.BoneData resolution.

        Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
        For Each bd In race.BoneData
            If bd.Gender = targetGender Then
                ' Dump archetype values for diagnostic bones to verify what the record actually says.
                Dim diagBones As String() = {"LBreast_skin", "RBreast_skin", "LButtFat_skin", "RButtFat_skin",
                                              "Belly_skin", "UpperBelly_skin", "Chest_skin", "Chest_Rear_Skin",
                                              "LArm_ShoulderFat_skin", "LLeg_Calf_skin", "LLeg_Thigh_skin"}
                For Each diagBone In diagBones
                    Dim bbb = bd.Bones.FirstOrDefault(Function(x) x.BoneName.Equals(diagBone, StringComparison.OrdinalIgnoreCase))
                Next
                If bd.Bones.Count > 0 Then Return (wt, wm, wf, bd, npcData.BodyMorphRegionValues, armaDeltas)
                Exit For
            End If
        Next
        If hasArmaDeltas Then
            Return (wt, wm, wf, New RACE_BoneDataGender With {.Gender = targetGender}, npcData.BodyMorphRegionValues, armaDeltas)
        End If
        Return Nothing
    End Function

    ''' <summary>Read race height (Male/Female Height from RACE.DATA) for the NPC's race. 1.0 if unknown.</summary>
    Private Function GetRaceHeight(state As MainForm.NPCVisualState) As Single
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return 1.0F
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 1.0F
        Dim race = _ctx.ParseRaceCached(raceRec)
        Dim h = If(state.IsFemale, race.FemaleHeight, race.MaleHeight)
        If h <= 0 Then Return 1.0F
        Return h
    End Function

    Friend Function BuildMergedNpcPose(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult,
                                        faceMorphsEnabled As Boolean,
                                        bodyWeightEnabled As Boolean,
                                        skeleton As SkeletonInstance,
                                        Optional armaSculptOverride As Dictionary(Of String, System.Numerics.Vector3) = Nothing,
                                        Optional suppressNeckNnam As Boolean = False) As Poses_class
        Dim racePose = PoseMath.BuildRaceHeightPose(GetRaceHeight(state))

        ' Body-weight (RACE.BSMS/MRSV) + ARMA sculpt. Sclpt y BW son toggles independientes:
        ' weightLayersEnabled=bodyWeightEnabled gobierna RACE.BSMS/MRSV; la capa ARMA se aplica si hay
        ' deltas (por eso el OrElse hasSculpt: un outfit con sculpt y BW=OFF igual arma la pose).
        Dim bwPose As Poses_class = Nothing
        Dim hasSculpt = (armaSculptOverride IsNot Nothing AndAlso armaSculptOverride.Count > 0)
        If bodyWeightEnabled OrElse hasSculpt Then
            Dim bwData = ResolveBodyWeightData(state, renderData)
            If bwData.GenderBlock IsNot Nothing Then
                Dim sculpt = If(armaSculptOverride, New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase))
                bwPose = PoseMath.BuildBodyWeightPose(bwData.Wt, bwData.Wm, bwData.Wf,
                                             bwData.GenderBlock, bwData.MrsvValues, sculpt,
                                             skeleton, bodyWeightEnabled)
            End If
        End If

        ' NNAM (neck-fat) — gateado SOLO por Apply Body Weight, INDEPENDIENTE de sculpt y de
        ' MWGT/RACE.BoneData (antes heredaba esos couplings por compartir BuildBodyWeightPose). Es el
        ' slider de cuello del chargen: block2 = FMRS PositionZ de la región IsNeckRegion × RACE.NNAM
        ' (ResolveNeckNnamScale). Se emite como su propio entry del hueso "Neck"; la anti-propagación a
        ' los hijos la hace el post-pase NpcMorphPoseResolver.ApplyNeckNnamCompensation (que lee esta S
        ' del "Neck" aplicado → auto-gateada: si acá no se emite, no hay comp).
        ' ⚠ NO se afirma que sea el mecanismo del engine (consumidor del +0x50 nunca hallado); es la
        ' compensación por-pose que da el resultado observable correcto (cara no se infla; escala solo
        ' los verts pegados al "Neck").
        Dim nnamPose As Poses_class = Nothing
        If bodyWeightEnabled AndAlso Not suppressNeckNnam Then
            Dim neckScale = ResolveNeckNnamScale(state)
            If neckScale.ScaleY <> 1.0F OrElse neckScale.ScaleZ <> 1.0F Then
                nnamPose = New Poses_class With {
                    .Name = "NNAM Neck",
                    .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
                    .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
                }
                nnamPose.Transforms("Neck") = New PoseTransformData With {.ScaleX = 1.0F, .ScaleY = neckScale.ScaleY, .ScaleZ = neckScale.ScaleZ}
            End If
        End If

        Dim fmrsPose As Poses_class = Nothing
        If faceMorphsEnabled Then
            fmrsPose = BuildFaceBoneTransforms(state)
        End If

        ' SSE RaceMenu NiOverride node transforms (body-scale sliders) — scale the named skeleton bones by the
        ' per-node uniform scale (e.g. "NPC L Breast" → 1.15). SSE-only; FO4 leaves the carrier Nothing. Same
        ' pose mechanism as NNAM (a PoseTransformData scale per bone), merged below.
        Dim sseNodePose As Poses_class = BuildSseNodeScalePose(state)

        Return PoseMath.MergePoses(racePose, bwPose, nnamPose, fmrsPose, sseNodePose)
    End Function

    ''' <summary>Build the SSE RaceMenu node-scale pose from the applied preset's SseNodeTransforms: one
    ''' uniform-scale PoseTransformData per named skeleton bone. Nothing on FO4 / when no transforms.</summary>
    Private Function BuildSseNodeScalePose(state As MainForm.NPCVisualState) As Poses_class
        If state Is Nothing OrElse Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return Nothing
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(state.RootNpcFormID, preset) OrElse preset Is Nothing Then Return Nothing
        Dim nts = preset.SseNodeTransforms
        If nts Is Nothing OrElse nts.Count = 0 Then Return Nothing
        Dim pose As New Poses_class With {
            .Name = "SSE Node Scale",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each nt In nts
            If nt Is Nothing OrElse Not nt.HasScale OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
            ' RaceMenu body-scale is a uniform node scale. 1.0 = unchanged; skip no-ops.
            If Math.Abs(nt.Scale - 1.0F) < 0.00001F Then Continue For
            pose.Transforms(nt.NodeName) = New PoseTransformData With {.ScaleX = nt.Scale, .ScaleY = nt.Scale, .ScaleZ = nt.Scale}
        Next
        If pose.Transforms.Count = 0 Then Return Nothing
        Return pose
    End Function

End Class
