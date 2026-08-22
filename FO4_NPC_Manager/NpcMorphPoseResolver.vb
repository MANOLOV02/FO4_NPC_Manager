Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split: morph + pose resolution (face/body morph resolvers,
''' FMRS face-bone transforms, body-weight data, race height, merged NPC pose, facial-bone regions).
''' Standalone class, DI. Skeleton LOADING (PrepareSkeleton) + its caches stay in MainForm.
''' See 61-perf-mainform-split.</summary>
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

        ' "Show other gender" preview: the NPC's chargen vertex morphs are gender-specific (baked against
        ' its own gender's chargen .tri) and don't apply to a default target-gender head, so no face-SHAPE
        ' morphs. FO4 ⇒ resolver entero afuera (su plan es solo forma). SSE ⇒ NO se puede abortar acá: el
        ' SkinnyMorph del weight vive en este plan, es per-actor y aplica a cualquier género (el mesh tri
        ' mergeado es el de la shape mostrada) — abortar dejaba la cabeza en peso neutro mientras el
        ' cuerpo _0/_1 lerpaeaba ⇒ costura. Se sigue con applyChargenMorphs:=False (solo weight).
        Dim genderOverride = host IsNot Nothing AndAlso host.PreviewGenderOverride.HasValue

        ' Get the full NPC_Data for the model source (the NPC whose face we're rendering)
        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing

        ' No morph data at all? Skip — FO4 ONLY (face morphs live in MorphValues; empty ⇒ empty plan).
        ' SSE has NO early-out: un NPC sin chargen (NAM9/NAMA ausentes — p.ej. Dremora) igual recibe el
        ' RACE base morph y el "SkinnyMorph" del weight (el engine aplica ambos sin mirar el chargen:
        ' applier 0x1403B90D0 lee actor+0x1FC incondicionalmente), y el bake (BuildFaceMorphPlan) tampoco
        ' gatea por NAM9. Gatear acá dejaba la cabeza en peso neutro mientras el cuerpo _0/_1 sí lerpaeaba
        ' ⇒ costura de cuello en todo weight ≠ 100 (render≠bake y render≠engine). BuildFaceMorphPlanFromNam9
        ' no-opea por canal cuando falta el dato, así que el resolver SSE "vacío" es naturalmente barato.
        Dim isSse = (npcData.Game = Config_App.Game_Enum.Skyrim)
        If Not isSse AndAlso (genderOverride OrElse npcData.Record.MorfosDeCara().Count = 0) Then Return Nothing

        ' Get RACE morph definitions for mapping MSDK keys ? morph names
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        ' MorphValues/MorphPresets/MorphGroups son exclusivos de Fallout 4 — Skyrim no los declara en RACE.
        ' Los tres bloques de morfos de la cara los declara SOLO Fallout 4: sus subrecords son MSID,
        ' MPPI y MPGS. Skyrim declara otra cosa en su lugar (MPAI/MPAV, las variantes de nariz, ceja,
        ' ojo y labio), que no son estas definiciones.
        '
        ' Con Skyrim quedan VACIOS y se sigue: NO se corta la funcion. El plan de morfos tambien lleva
        ' el morfo por peso, que es por actor y aplica a cualquier genero; salir aca deja la cabeza en
        ' peso neutro mientras el cuerpo interpola, y eso es la costura de cuello que documenta el
        ' comentario de arriba. Es tambien lo que hacia la lectura anterior: si el record no traia esos
        ' subrecords, las listas salian vacias y el resolvedor se armaba igual.
        ' Vacias, NO nulas: el modelo anterior declaraba las tres como listas ya construidas, asi que
        ' un record que no traia esos subrecords daba una lista sin elementos. Devolver nulo en su
        ' lugar cambia lo que ve todo lo de abajo.
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim morphValueDefs As IReadOnlyList(Of Canon.RaceFO4_MorphValues) =
            New List(Of Canon.RaceFO4_MorphValues)()
        Dim morphPresetDefs As New List(Of RACE_MorphPresetDef)
        Dim morphGroups As New List(Of RACE_MorphGroup)
        If raceFo4 IsNot Nothing Then
            morphValueDefs = raceFo4.MorphValues
            morphPresetDefs = raceFo4.ReadMorphPresetsFlat(state.IsFemale)
            morphGroups = raceFo4.ReadMorphGroups(state.IsFemale)
        End If


        ' Dump raw MSDK/MSDV table from this NPC (to see what keys+weights the record really has).
        ' Cross-reference each key against RACE.MSID (sliders) / MPPI (presets) / MPGS (group sliders)
        ' to show where each morph came from and why it's in the NPC.
        Dim sliderIndexSet As New HashSet(Of UInteger)
        If morphValueDefs IsNot Nothing Then
            For Each mv In morphValueDefs : sliderIndexSet.Add(mv.ValueIndex) : Next
        End If
        Dim presetIndexMap As New Dictionary(Of UInteger, String)
        If morphPresetDefs IsNot Nothing Then
            For Each mp In morphPresetDefs
                If Not presetIndexMap.ContainsKey(mp.Index) Then presetIndexMap(mp.Index) = mp.MorphName
            Next
        End If
        For Each kvp In npcData.Record.MorfosDeCara()
            Dim key = kvp.Key
            Dim value = kvp.Value
            Dim classification As String

            Dim value1 As String = Nothing

            If sliderIndexSet.Contains(key) Then
                Dim mvDef = morphValueDefs.FirstOrDefault(Function(m) m.ValueIndex = key)
                classification = $"SLIDER (RACE.MSID) MSM0='{mvDef.ValueMinName}' MSM1='{mvDef.ValueMaxName}'"
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

        ' GAME-AWARE toggles: en SSE dos canales viven DENTRO del plan de cara y por eso se pasan acá en vez
        ' de gatearse en el composite — el sculpt per-vértice de RaceMenu (checkbox "Sculpt", análogo del ARMA
        ' SCLP de FO4) y el SkinnyMorph de cabeza/pelo (checkbox "Body weight", que también gatea el _0/_1 del
        ' cuerpo en BuildSseBodyWeightResolver). En FO4 el plan no emite esos canales → los flags son inertes.
        Return New NpcMorphResolver(
            npcData,
            morphValueDefs:=morphValueDefs,
            morphPresetDefs:=morphPresetDefs,
            meshDictKeys:=renderData.MeshDictKeys,
            shapeChargenTriPaths:=renderData.ShapeChargenTriPaths,
            shapeRaceMorphTriPaths:=renderData.ShapeRaceMorphTriPaths,
            shapeMeshMorphTriPaths:=renderData.ShapeMeshMorphTriPaths,
            raceEditorId:=RecordParsers.ResolveMorphRaceEditorId(race, _ctx.PluginManager),
            raceKeywordEditorIds:=RecordParsers.GetRaceKeywordEditorIds(race, _ctx.PluginManager),
            applySculpt:=(host Is Nothing OrElse host.Toggles Is Nothing OrElse host.Toggles.ApplySculpt),
            applyBodyWeight:=(host Is Nothing OrElse host.Toggles Is Nothing OrElse host.Toggles.ApplyBodyWeight),
            applyChargenMorphs:=(Not genderOverride) AndAlso (host Is Nothing OrElse host.Toggles Is Nothing OrElse host.Toggles.ApplyVertexMorphs))
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
        ' Checkbox "Body weight" — el canal SSE del peso (NAM7). Antes se agregaba SIEMPRE al composite, así
        ' que el toggle no hacía nada en Skyrim. Apagarlo ahora deja el cuerpo en peso neutro (y el
        ' SkinnyMorph de la cabeza tampoco se emite — mismo flag, dentro del plan de cara).
        If Not host.Toggles.ApplyBodyWeight Then Return Nothing
        ' Weight (NAM7) rides the Traits bucket → read it from the same source the face appearance uses.
        ' Wrap in the overlay so an Edit Body SSE weight edit (preset.SseWeight → shadow.Nam7Raw) renders
        ' live — same _overlay(...) seam BuildFaceMorphResolver uses for NAM9/NAMA.
        Dim npcData = _overlay(_ctx.GetParsedNpc(NpcStateFactory.FaceAppearanceSourceFormID(state)), state.RootNpcFormID)
        If npcData Is Nothing Then Return Nothing
        If npcData.Game <> Config_App.Game_Enum.Skyrim Then Return Nothing
        ' Sin NAM7 el peso es 100: es el valor con el que el motor dibuja un actor que no lo declara.
        Dim w = If(npcData.Record.TienePesoDeSkyrim(), npcData.Record.PesoDeSkyrim(), 100.0F)
        Dim t = Math.Max(0.0F, Math.Min(1.0F, w / 100.0F))
        Return New SseBodyWeightMorphResolver(t, state.IsFemale, renderData.MeshDictKeys, renderData.ShapeCandidate, _ctx)
    End Function

    ' The SSE HEAD/HAIR weight morph (formerly BuildSseHeadWeightResolver + SseHeadWeightMorphResolver +
    ' the hardcoded SseHeadWeightDelta table) is now applied inside the FACE morph plan: BuildFaceMorphPlanFromNam9
    ' adds a "SkinnyMorph" channel at frac = 1 - clamp(NAM7/100,0,1), read from each shape's own mesh .tri (merged
    ' by LoadTriForShape). Engine-derived (SkyrimSE.exe applier 0x1403B90D0 → 0x140430190), agnostic and race-aware
    ' (femalehead/argonian/khajiit/hairNN each ship their own SkinnyMorph), and shared by render + bake.

    ''' <summary>Resuelve los overlays de cuerpo ("tatuajes") del preset aplicado a
    ''' <see cref="IRenderableShape.OverlayLayers"/> sobre las shapes de PIEL. Es la integracion de render de
    ''' la feature: SETEA las capas directo (a diferencia de los resolvers de morph, que devuelven un
    ''' IMorphResolver) porque una capa de overlay es un pase de material extra, no un delta de vertices.
    ''' <para><b>Modelo del motor</b> (f4ee OverlayInterface): para un slot biped S el motor busca las shapes de
    ''' PIEL de ese slot y, por cada overlay aplicado (en prioridad ASCENDENTE), consulta el material de slot de
    ''' su template; el overlay aporta capa a esa shape solo si el template define material para S. Despues suma
    ''' el offsetUV del preset al del material, multiplica el scaleUV y, en un BGEM tintable, setea el color
    ''' base. Aca se replica eso pre-horneando transform y tinte sobre el material cargado.</para>
    ''' <para><b>Identificacion de shape de piel y slot</b> (la inferencia mas riesgosa): una shape es de PIEL
    ''' cuando su candidate tiene Kind=Skin, o sea que se colecto del ARMO de piel - el analogo directo del
    ''' "clonar las shapes de piel" del motor. Los slots salen de MeshCandidate.SlotMask con bit (N-30) = slot
    ''' biped N. Ademas se exige que el material sea skin-tinted, espejando el gate kType_SkinTint del motor: un
    ''' NIF de ARMO de piel puede traer shapes que no son piel (ojos) y esas no deben recibir overlays.</para>
    ''' <para><b>Limpieza</b>: sin preset aplicado o sin overlays, TODA shape queda con OverlayLayers en Nothing,
    ''' asi que cambiar de NPC no puede filtrar los tatuajes del anterior.</para></summary>
    ''' <param name="host">El host QUE ESTÁ RENDERIZANDO (no <c>_hostProvider()</c>): de él sale
    ''' editor puedan discrepar sobre el pool magic.
    ''' <para>ES OBLIGATORIO. Era <c>Optional</c> "por compat de los call sites que no tienen host a mano", y esa
    ''' compat no existía: los dos call sites lo tenían. El único que lo omitía (el camino live) caía al
    ''' <c>_hostProvider()</c> = el host PRINCIPAL y borraba los overlays magic del preview del editor. Obligatorio,
    ''' el compilador cierra esa trampa para el próximo call site en vez de dejarla esperando.</para></param>
    Friend Sub ResolveOverlayLayers(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult,
                                    host As NpcRenderHost)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return

        ' GAME-AWARE: SSE (Skyrim) body overlays are RaceMenu path-based (no f4ee template catalog), sourced
        ' from the preset's SSE carrier and synthesized into materials here — a separate code path from the
        ' FO4 template resolution below. The FO4 path stays byte-identical (behind this gate). §3.2/§3.3.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ResolveSseOverlayLayers(state, renderData, host)
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

    ''' <summary>Distinct worn SlotMask of every body-skin shape this NPC actually renders — the set of targets a
    ''' RaceMenu skin override can bind to (a skin override applies to a body-skin shape whose SlotMask intersects
    ''' the override's, ResolveSseOverlayLayers:472). On an all-in-one body (one CBBE mesh covering slots
    ''' 32/33/37) this is a single combined mask, which is why picking Body vs Hands vs Feet all hit the same
    ''' shape; on separate body/hands/feet meshes it is one mask each. The Skin-Overrides editor builds its slot
    ''' picker from this so the choice maps to a real, distinct skin shape instead of a fixed guess.</summary>
    Friend Shared Function BodySkinSlotMasks(renderData As MainForm.PreviewResolutionResult) As List(Of UInteger)
        Dim result As New List(Of UInteger)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return result
        Dim seen As New HashSet(Of UInteger)
        For Each shape In renderData.Shapes
            If shape Is Nothing OrElse Not ShapeIsSkinTinted(shape) Then Continue For
            Dim cand As MainForm.MeshCandidate = Nothing
            If Not renderData.ShapeCandidate.TryGetValue(shape, cand) OrElse cand Is Nothing Then Continue For
            If cand.SlotMask = 0UI Then Continue For
            If seen.Add(cand.SlotMask) Then result.Add(cand.SlotMask)
        Next
        Return result
    End Function

    ''' <summary>True for the head (FaceTint shader) — the target of RaceMenu "Face [Ovl{n}]" face-paint overlays.</summary>
    Private Shared Function ShapeIsFace(shape As IRenderableShape) As Boolean
        Dim rel = shape.ShapeMaterial
        If rel Is Nothing OrElse rel.material Is Nothing Then Return False
        Return rel.material.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint
    End Function

    ''' <summary>Is this overlay/skin-override texture present in the load order (loose + BSA)? Uses the same
    ''' normalisation the material loader does (lowercase, backslashes, prepend "textures\") so "present" here
    ''' matches what the renderer can actually load. A missing texture ⇒ skip the overlay instead of flat-filling
    ''' the skin with the missing-texture placeholder.</summary>
    Private Shared Function SseTextureExists(path As String) As Boolean
        If String.IsNullOrWhiteSpace(path) Then Return False
        Dim key = path.Replace("/"c, "\"c).ToLowerInvariant()
        If Not key.StartsWith("textures\") Then key = "textures\" & key
        Return FO4_Base_Library.FilesDictionary_class.Dictionary.ContainsKey(key)
    End Function

    ''' <summary>True when a RaceMenu overlay node name is a FACE overlay ("Face [Ovl{n}]" / "Face [SOvl{n}]").
    ''' Predicado ÚNICO, compartido con el bake (CPU y GPU) y el emisor del script Papyrus — ver
    ''' <see cref="SseOverlayCompositor.IsFaceOverlayNodeName"/>. Cinco caminos decidían "es de cara" por su
    ''' cuenta y no todos coincidían; ahora hay una sola implementación.</summary>
    Private Shared Function SseOverlayIsFaceNode(nodeName As String) As Boolean
        Return SseOverlayCompositor.IsFaceOverlayNodeName(nodeName)
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

    ''' <summary>Carga el material de slot de un template de overlay, le pre-hornea el transform por instancia
    ''' de LooksMenu (offsetUV/scaleUV) y el tinte, y lo envuelve como <see cref="OverlayMaterialLayer"/>.
    ''' <para>La carga espeja la cadena canonica: normalizar el path, sacar y volver a poner el prefijo
    ''' Materials\ en el Deserialize, y elegir BGEM para .bgem (efecto/tatuaje) o BGSM si no. Se le pasa el
    ''' NifShape + NifContent de la shape de piel para que Deserialize siembre los campos de alpha y resuelva el
    ''' ShaderType igual que cuando se cargo el material base.</para>
    ''' <para>El pre-horneado replica LoadMaterialData: <c>oU += offsetUV.x; oV += offsetUV.y; sU *= scaleUV.x;
    ''' sV *= scaleUV.y</c>. El pase de overlay del render sube tanto uvOffset como uvScale, asi que el scaleUV
    ''' se honra y no solo el offset. El tinte se setea como color base del BGEM, que es no-op en un BGSM - lo
    ''' mismo que hace el motor guardando esa escritura tras la rama de material de efecto.</para>
    ''' <para>Nothing si falla la carga: un material de overlay roto no puede romper el render.</para></summary>
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

    ''' <summary>Analogo SSE de <see cref="ResolveOverlayLayers"/>: toma los overlays de RaceMenu (por PATH) del
    ''' carrier <see cref="LooksmenuLoader.LooksmenuPreset.SseBodyOverlays"/> del preset aplicado y sintetiza un
    ''' <see cref="OverlayMaterialLayer"/> por cada shape skin-tinted que matchee.
    ''' <para>La pertenencia de shape usa el mismo gate que el camino FO4, pero el match de slot es
    ''' SSE-especifico: el nombre del nodo de overlay (Body/Hands/Feet) mapea a los bits de slot biped que cubre
    ''' y el overlay cae en cualquier shape skin-tinted cuyo SlotMask los intersecte. El orden de dibujo es el de
    ''' <see cref="SseOverlayCompositor.CompositeOrderKey"/>: el pool normal ascendente y ENCIMA el pool magic
    ''' ascendente (skee instala el primario y despues el secundario). NO es el orden de la lista ni el indice pelado.</para>
    ''' <para>El blend es el MISMO decal coplanar alpha-over que en FO4: el modo de blend no esta en el .jslot.
    ''' Sin preset o con carrier vacio, todas las shapes se limpian.</para></summary>
    Private Sub ResolveSseOverlayLayers(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult,
                                        host As NpcRenderHost)
        ' Overlays (tattoos / body-hand-feet-face paint) become alpha-over decal layers here. Skin overrides do NOT:
        ' they are a per-slot texture-set REPLACEMENT on the skin material (skee NIOVTaskUpdateTexture), applied
        ' in place by NpcMaterialResolver.ApplyShapeMaterialOverrides — not a decal on top.
        Dim overlays As List(Of FO4_Base_Library.RaceMenuJslot.JslotOverlayNode) = Nothing
        If state IsNot Nothing Then
            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If _appliedPresets.TryGetValue(state.RootNpcFormID, preset) AndAlso preset IsNot Nothing Then
                If preset.SseBodyOverlays IsNot Nothing AndAlso preset.SseBodyOverlays.Count > 0 Then overlays = preset.SseBodyOverlays
            End If
        End If
        ' ¿Este preview dibuja el pool magic? Sin host, NO (default seguro = el retrato en reposo).

        ' No overlay decals → clear every shape's overlay layers and bail (skin overrides live on the material now).
        If overlays Is Nothing Then
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
            ' Overlays ON TOP, en el ORDEN DE COMPOSICIÓN DE skee: primero TODO el pool normal por índice
            ' ascendente ([Ovl0] abajo → [OvlN]) y ENCIMA todo el pool magic por índice ascendente
            ' ([SOvl0]…[SOvlM]) — SetupOverlay corre el loop primario completo y después el secundario, y cada
            ' InstallOverlay termina en AttachChild (OverlayInterface.cpp:659-668, :257). NO es la posición en la
            ' lista (el jslot puede venir en cualquier orden) y NO es el índice pelado: ordenar por índice sin mirar
            ' el pool empataba [SOvl0] con [Ovl0] y podía dejar el magic DEBAJO de [Ovl1]. La clave única está en
            ' SseOverlayCompositor.CompositeOrderKey y la comparten render y bake.
            ' El lib dibuja layers en orden de lista (primero=abajo), así que se agrega el de clave más baja primero.
            ' Body/Hands/Feet van en el shape de slot; Face en el head FaceTint.
            If overlays IsNot Nothing Then
                For Each ov In overlays.OrderBy(Function(o) SseOverlayCompositor.CompositeOrderKey(If(o IsNot Nothing, o.NodeName, Nothing)))
                    ' Skip an overlay with no texture OR whose texture is not in the load order (loose+BSA): with no
                    ' resolvable diffuse there is nothing to composite, and rendering it would flat-fill the skin
                    ' with the "missing texture" placeholder. Same rule for face and body overlays.
                    If ov Is Nothing OrElse String.IsNullOrEmpty(ov.DiffusePath) OrElse Not SseTextureExists(ov.DiffusePath) Then Continue For
                    Dim applies As Boolean
                    If SseOverlayIsFaceNode(ov.NodeName) Then
                        ' LA CARA TIENE DOS MECANISMOS Y NO SE SOLAPAN (ver SseOverlayCompositor.IsFoldableFaceOverlay):
                        '   Face [Ovl{n}]  (no-magic) ⇒ lo PLIEGA NpcFaceTintResolver dentro del diffuse de la cabeza,
                        '                               igual que el bake ⇒ acá NO va decal.
                        '   Face [SOvl{n}] (magic)    ⇒ NO se pliega nunca ⇒ acá SÍ va decal vivo.
                        ' ESTO ARREGLA UN DOBLE APLICADO REAL Y PREEXISTENTE: los dos caminos leen el MISMO
                        ' `preset.SseBodyOverlays` y los dos corrían sin gate, así que un face-paint normal se
                        ' componía DOS veces en el preview (horneado en el diffuse plegado + decal encima) y salía
                        ' más oscuro/saturado que lo que el bake escribe. El bake nunca tuvo el decal ⇒ era también
                        ' una violación de RENDER == BAKE.
                        applies = isFace AndAlso SseOverlayCompositor.IsSpellOverlay(ov)
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
                ' `.Count(pred)` NO compila sobre List(Of T): VB resuelve `Count` a la PROPIEDAD antes que a la
                ' extensión de LINQ. Where(...).Count() es la forma que sí toma el predicado.
                Dim spellN = If(overlays Is Nothing, 0, overlays.Where(Function(o) SseOverlayCompositor.IsSpellOverlay(o)).Count())
                ' El conteo de magic viaja CON el resultado: sin eso, "el overlay no aparece" no distingue entre
                ' "no resolvió la textura" y "el slot no le corresponde a esta forma".
                Logger.LogLazy(Function() $"[OVERLAY-SSE] shape='{shape.ShapeName}' mask=0x{cand.SlotMask:X8} overlays={ovN} (magic={spellN}) → layers={layerCount}")
            End If
        Next
    End Sub

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
            ' Opacity is skee64's kParam_ShaderAlpha (key 8 → BSShaderMaterial::alpha, ShaderUtilities.cpp:98),
            ' NOT the alpha byte of the tint colour: kParam_ShaderTintColor unpacks into an NiColor — RGB only
            ' (ShaderUtilities.cpp:119-125) — and only on FaceGenRGBTint/HairTint materials. An overlay with no
            ' alpha override is fully opaque.
            Dim opacity As Single = If(ov.HasAlpha, ov.Alpha, 1.0F)
            If ov.HasTint Then
                mat.BaseColor = Color.FromArgb(ClampUnitToByte(opacity), ClampUnitToByte(ov.TintR),
                                               ClampUnitToByte(ov.TintG), ClampUnitToByte(ov.TintB))
            Else
                mat.BaseColor = Color.FromArgb(ClampUnitToByte(opacity), 255, 255, 255)
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

    ''' <summary>Cache of parsed FacialBoneRegions files per race/gender key (e.g. "HumanRace:female").
    ''' <para>CONCURRENTDICTIONARY, NO Dictionary, y el motivo NO es cosmetico: el bake hornea VARIOS
    ''' NPCs a la vez y esto se pide POR NPC (via BuildCharGen). Un Dictionary en escritura concurrente puede
    ''' PERDER una entrada o colgarse re-hasheando, y perder una entrada aca significa saltear el morph
    ''' FMRS/FMRI ⇒ <b>CARA NEUTRA</b>, distinta en cada corrida. Es el unico race del bake que puede mover
    ''' BYTES de la salida.</para>
    ''' <para>El valor es funcion PURA de (EditorID, genero) —el mismo archivo parseado— asi que dos hilos
    ''' que pierdan la carrera escriben lo MISMO: last-write-wins es byte-neutro y no hace falta candado.</para></summary>
    Private Shared ReadOnly _facialBoneRegionsCache As New Concurrent.ConcurrentDictionary(Of String, FacialBoneRegionsFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Cuantas veces se pidio un <c>&lt;Race&gt;FacialBoneRegions&lt;G&gt;.txt</c> que NO existe en los
    ''' archives (contado UNA vez por raza+genero: la cache actua de latch). Distinto de cero significa que hubo
    ''' razas cuyo morph FMRS/FMRI se salteo y cuya cara pudo salir NEUTRA. Se expone para que un barrido pueda
    ''' reportarlo en vez de que el dato viva solo en el log — un batch con `Logger.Enabled=False` no veria nada.</summary>
    Friend Shared ReadOnly Property FacialBoneRegionsMisses As Integer
        Get
            Return Threading.Volatile.Read(_facialBoneRegionsMisses)
        End Get
    End Property
    Private Shared _facialBoneRegionsMisses As Integer = 0

    ''' <summary>Load and parse the per-race HumanRaceFacialBoneRegions<Gender>.txt JSON file
    ''' for the NPC's OWN gender. Returns Nothing if the file doesn't exist or can't be parsed.
    ''' <para>This is the GENDER CATALOG: the set of regions the editor offers for a race+gender.
    ''' It is NOT the right table to resolve an NPC's FMRI values against — for that use
    ''' <see cref="GetFacialBoneRegionsForFmriResolution"/>, which merges both gender tables
    ''' (see the measured evidence documented there).</para></summary>
    Friend Shared Function GetFacialBoneRegionsForRace(race As Canon.IRace, isFemale As Boolean) As FacialBoneRegionsFile
        If race Is Nothing OrElse String.IsNullOrEmpty(race.EditorID) Then Return Nothing

        Dim genderKey = If(isFemale, "Female", "Male")
        Dim cacheKey = race.EditorID & ":" & genderKey

        Dim cached As FacialBoneRegionsFile = Nothing
        If _facialBoneRegionsCache.TryGetValue(cacheKey, cached) Then Return cached

        ' Build candidate paths. Use race.EditorID as the base name (HumanRace, GhoulRace, etc.)
        Dim dataPath = $"meshes\actors\character\characterassets\{race.EditorID}FacialBoneRegions{genderKey}.txt".ToLowerInvariant()
        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(dataPath, loc) Then
            ' FALLO SILENCIOSO — ya no. Antes esto cacheaba Nothing y volvia mudo: un NPC con entradas
            ' FMRI/FMRS cuya raza no tiene <Race>FacialBoneRegions<G>.txt se come una CARA NEUTRA sin que
            ' nadie se entere. Vanilla solo trae HumanRace / GhoulRace / PowerArmorRace; NO existe
            ' HumanChildRaceFacialBoneRegions{Male,Female}.txt. Hoy es inerte —medido: los 42 NPCs
            ' infantiles tienen FMRI entries = 0— pero un mod que le ponga FMRS a un niño cae justo aca.
            ' Misma familia que el agujero del fallback de morphs de FaceGenBuilder; la diferencia es que
            ' este todavia no muerde.
            ' Se avisa UNA VEZ por (raza, genero): la propia cache actua de latch, asi que no spamea.
            Threading.Interlocked.Increment(_facialBoneRegionsMisses)
            Dim edidM = race.EditorID, gkM = genderKey, pathM = dataPath
            Logger.LogLazy(Function() $"[FBR] MISSING facial-bone-regions file for race '{edidM}' ({gkM}): '{pathM}' is not in the archives. Any FMRS/FMRI morph for this race will be SKIPPED and the face will be written NEUTRAL.")
            _facialBoneRegionsCache(cacheKey) = Nothing
            Return Nothing
        End If

        Try
            Dim bytes = loc.GetBytes()
            ' Dump the raw JSON to a sibling file so we can see exactly what the engine reads
            ' (independent of our parser). Compares against externally-sourced hex IDs to
            ' catch any parser bug. Path: same directory as the log file, named per gender.
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

    ''' <summary>Cache of the MERGED (both-gender) FacialBoneRegions table, keyed by race EditorID.
    ''' <para>ConcurrentDictionary por el MISMO motivo que <see cref="_facialBoneRegionsCache"/>: se pide por
    ''' NPC y el bake corre varios NPCs en paralelo. Perder una entrada = FMRI sin resolver = cara neutra.
    ''' El merge es funcion pura de (EditorID, genero) ⇒ last-write-wins es byte-neutro.</para></summary>
    Private Shared ReadOnly _facialBoneRegionsMergedCache As New Concurrent.ConcurrentDictionary(Of String, FacialBoneRegionsFile)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Tabla contra la que se resuelven los FMRI (IDs de region osea facial) de un NPC: las tablas
    ''' FacialBoneRegions Female Y Male de la raza, mergeadas en un solo mapa ID -> region.
    ''' <para><b>Por que las dos: el ID identifica la TABLA, no el genero del NPC.</b> Los dos JSON por genero de
    ''' una raza usan NAMESPACES DE ID DISJUNTOS (medido sobre HumanRace y GhoulRace de Fallout4.esm, 32 regiones
    ''' cada una, interseccion VACIA).</para>
    ''' <para>⛔ Los rangos observados son EVIDENCIA, NO UNA SPEC, y nada de este codigo ramifica sobre ellos: no
    ''' hay constante de rango ni test numerico. La resolucion es PURAMENTE FILE-DRIVEN - se cargan las dos
    ''' tablas de la raza y se indexa lo que cada archivo declara, asi que un mod con sus propios IDs funciona sin
    ''' cambios. Tampoco es una heuristica de "si falla, probar el otro archivo": el ID designa la tabla.</para>
    ''' <para><b>Por que importa</b> (medido, 10/10 aciertos y 0 falsos positivos sobre 895 controles): diez NPCs
    ''' de Fallout4.esm traen FMRI del namespace del genero OPUESTO. Cargando solo el archivo del genero propio
    ''' fallaban TODOS los lookups, no se aplicaba ninguna deformacion osea y la cabeza salia exactamente neutra
    ''' (desviacion 0,0000-0,0001 contra 0,068-0,290 del CK). Impacto: 83 de 377 shapes. Ningun FormID
    ''' hardcodeado: el trabajo lo hace la union de las tablas shipeadas.</para>
    ''' <para>⛔ SYNC: RENDER == BAKE. Es el unico punto de resolucion, usado por el render en vivo
    ''' (<see cref="BuildFaceBoneTransforms"/>, <see cref="ResolveNeckNnamScale"/>) y por el bake offline
    ''' (FaceGenBuilder -> FaceGenBuildPipeline.BuildBakeState).</para>
    ''' <para>GAME-AWARE: los JSON de FacialBoneRegions son un mecanismo de FALLOUT 4. En SSE no existen (sus
    ''' morphs de cara vienen de RACE NAM9 / .tri), asi que las dos cargas fallan y esto devuelve Nothing sin
    ''' necesidad de gate.</para>
    ''' <para><b>Desempate explicito</b>: la disjuncion es propiedad de los ARCHIVOS, no algo que se imponga. Si
    ''' una raza modeada compartiera un ID entre sus dos tablas, gana la del genero PROPIO del NPC (se inserta
    ''' segunda y pisa), asi que el peor caso degrada exactamente al comportamiento previo al fix.</para>
    ''' <para>Los archivos ausentes o ilegibles degradan limpio: cada lado es una carga independiente que
    ''' devuelve Nothing sin tirar, asi que con las dos presentes hay union, con una sola esa (PowerArmorRace
    ''' vanilla trae solo la Male) y sin ninguna, Nothing - que todos los callers ya tratan como "sin pose
    ''' osea".</para></summary>
    Friend Shared Function GetFacialBoneRegionsForFmriResolution(race As Canon.IRace, isFemale As Boolean) As FacialBoneRegionsFile
        If race Is Nothing OrElse String.IsNullOrEmpty(race.EditorID) Then Return Nothing

        Dim cacheKey = race.EditorID & ":" & If(isFemale, "Female", "Male") & ":merged"
        Dim cached As FacialBoneRegionsFile = Nothing
        If _facialBoneRegionsMergedCache.TryGetValue(cacheKey, cached) Then Return cached

        Dim own = GetFacialBoneRegionsForRace(race, isFemale)
        Dim other = GetFacialBoneRegionsForRace(race, Not isFemale)

        Dim merged As FacialBoneRegionsFile = Nothing
        If own Is Nothing AndAlso other Is Nothing Then
            merged = Nothing
        ElseIf other Is Nothing OrElse other.Regions Is Nothing OrElse other.Regions.Count = 0 Then
            merged = own                                  ' nothing to add — reuse the parsed instance
        Else
            merged = New FacialBoneRegionsFile()
            ' Opposite-gender namespace first, own gender second, so own gender wins any (unexpected)
            ' ID collision — see the collision policy above.
            For Each kv In other.Regions
                merged.Regions(kv.Key) = kv.Value
            Next
            If own IsNot Nothing AndAlso own.Regions IsNot Nothing Then
                For Each kv In own.Regions
                    merged.Regions(kv.Key) = kv.Value
                Next
            End If
        End If

        _facialBoneRegionsMergedCache(cacheKey) = merged
        Return merged
    End Function

    ''' <summary>Thin instance wrapper over <see cref="FaceBonePoseBuilder.BuildFaceBoneTransforms"/>;
    ''' resolves the overlay-applied NPC + race + regions JSON from the state, then delegates the
    ''' FMRS math to the helper module. Real impl lives in the module so offline bake reuses it.</summary>
    Private Function BuildFaceBoneTransforms(state As MainForm.NPCVisualState) As Poses_class
        If state Is Nothing Then Return Nothing

        Dim modelNpcFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        Dim npcData = _overlay(_ctx.GetParsedNpc(modelNpcFormID), state.RootNpcFormID)
        Dim npcFo4 = TryCast(npcData?.Record, Canon.NpcFO4)
        If npcFo4 Is Nothing OrElse npcFo4.FaceMorphs.Count = 0 Then Return Nothing

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = _ctx.ParseRaceCanonCached(raceRec)

        ' FMRI RESOLUTION → merged both-gender table (disjoint ID namespaces; 10 vanilla NPCs carry
        ' opposite-gender FMRI). See GetFacialBoneRegionsForFmriResolution.
        Dim regionsFile = GetFacialBoneRegionsForFmriResolution(race, state.IsFemale)
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
        Dim race = _ctx.ParseRaceCanonCached(raceRec)

        ' Same FMRI resolution rule as BuildFaceBoneTransforms: merged both-gender table.
        Dim regionsFile = GetFacialBoneRegionsForFmriResolution(race, state.IsFemale)
        If regionsFile Is Nothing Then Return (1.0F, 1.0F)

        ' NNAM (Neck Fat Adjustments Scale) es exclusivo de Fallout 4 — Skyrim no lo declara en RACE.
        Dim raceFo4Neck = TryCast(race, Canon.RaceFO4)
        Dim neckNnamX As Single = 0.0F
        Dim neckNnamY As Single = 0.0F
        If raceFo4Neck IsNot Nothing Then
            If state.IsFemale Then
                neckNnamX = raceFo4Neck.FemaleNeckFatAdjustmentsScaleX
                neckNnamY = raceFo4Neck.FemaleNeckFatAdjustmentsScaleY
            Else
                neckNnamX = raceFo4Neck.MaleNeckFatAdjustmentsScaleX
                neckNnamY = raceFo4Neck.MaleNeckFatAdjustmentsScaleY
            End If
        End If

        Return FaceBonePoseBuilder.ComputeNeckNnamScale(npcData, regionsFile, neckNnamX, neckNnamY)
    End Function

    ''' <summary>POST-PASE de la compensacion NNAM anti-propagacion. Llamar JUSTO DESPUES de
    ''' <c>ApplyBoneMorphPose</c> sobre el MISMO skeleton (BuildBodyWeightPose ya metio el scale del NNAM en el
    ''' hueso "Neck"). Como <c>GetGlobalTransform</c> compone la cadena de padres, esa escala PROPAGARIA a los
    ''' hijos (Neck -> HEAD_Offset -> HEAD -> cara), que es el bug de "cara adelante". Para cancelarla, a CADA
    ''' hijo DIRECTO de "Neck" se le compone <c>comp = L_C^-1 . S^-1 . L_C</c> sobre su MorphDelta existente, asi
    ''' que la escala queda SOLO en los verts pegados a "Neck" y los FMRS quedan intactos. <c>comp</c> puede
    ''' tener SHEAR (hijos rotados), por eso se asigna DIRECTO a MorphDeltaTransform: PoseTransformData no
    ''' representa shear.
    ''' <para>GATEO AUTOMATICO: la S se LEE del MorphDeltaTransform del "Neck" (lo que realmente recibio), no se
    ''' re-resuelve. Si el "Neck" no escalo (body-weight OFF, NNAM inactivo o suprimido) es Nothing y esto es
    ''' NO-OP, asi que la compensacion nunca se aplica sin su S correspondiente en el padre.</para>
    ''' <para>Idempotencia: re-correr tras cada ApplyBoneMorphPose, que resetea la capa de morph. No llamar dos
    ''' veces sin re-aplicar la pose, o compone la compensacion dos veces.</para></summary>
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
    Private Function ResolveBodyWeightData(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult) As (Wt As Single, Wm As Single, Wf As Single, GenderBlock As Canon.RaceFO4_BoneScaleData, MrsvValues As List(Of Single), ArmaDeltas As Dictionary(Of String, System.Numerics.Vector3), HayDatos As Boolean)
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
        ' Bone Data (Weight Scale + Range Modifier) es exclusivo de Fallout 4 — Skyrim no lo declara.
        ' Vacia, NO nula: el modelo anterior declaraba esta lista ya construida, asi que una raza que
        ' no trae esos subrecords daba una lista sin elementos y lo de abajo no encontraba el bloque
        ' del genero, que es el mismo resultado.
        Dim raceFo4 = TryCast(_ctx.ParseRaceCanonCached(raceRec), Canon.RaceFO4)
        Dim boneData As IReadOnlyList(Of Canon.RaceFO4_BoneScaleData) =
            If(raceFo4 Is Nothing, New List(Of Canon.RaceFO4_BoneScaleData)(), raceFo4.BoneScaleData)

        ' Log the FaceGen clamps for reference. TBD whether they apply to body BSMS output
        ' or only to face slider*FMIN. Not applying any clamp formula without spec.
        ' NNAM ("Neck Fat Adjustments Scale") is resolved separately (ResolveNeckNnamScale) and
        ' threaded into BuildBodyWeightPose as Layer 2 — the CK RUNTIME scale of the shared "Neck"
        ' bone (head+body), NOT a per-bone BSMS/MRSV body-weight input, so it is not part of this
        ' RACE.BoneData resolution.

        Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
        For Each bd In boneData
            If bd.BoneWeightScaleDataWeightScaleTargetGender = targetGender Then
                ' Dump archetype values for diagnostic bones to verify what the record actually says.
                Dim diagBones As String() = {"LBreast_skin", "RBreast_skin", "LButtFat_skin", "RButtFat_skin",
                                              "Belly_skin", "UpperBelly_skin", "Chest_skin", "Chest_Rear_Skin",
                                              "LArm_ShoulderFat_skin", "LLeg_Calf_skin", "LLeg_Thigh_skin"}
                For Each diagBone In diagBones
                    Dim bbb = bd.BoneWeightScales.FirstOrDefault(Function(x) x.BoneWeightScaleSetName.Equals(diagBone, StringComparison.OrdinalIgnoreCase))
                Next
                If bd.BoneWeightScales.Count > 0 OrElse bd.BoneRangeModifiers.Count > 0 Then
                    Return (wt, wm, wf, bd, npcData.Record.ValoresDeRegionCorporal(), armaDeltas, True)
                End If
                Exit For
            End If
        Next
        ' Sin bloque de huesos del RACE pero con deltas de escultura del ARMA: la pose se arma
        ' igual, sólo que la capa de peso queda en identidad. Lo dice HayDatos, no el bloque: el
        ' bloque ahora ES el del record y no hay ninguno que representar vacío.
        If hasArmaDeltas Then
            Return (wt, wm, wf, Nothing, npcData.Record.ValoresDeRegionCorporal(), armaDeltas, True)
        End If
        Return Nothing
    End Function

    ''' <summary>Read race height (Male/Female Height from RACE.DATA) for the NPC's race. 1.0 if unknown.</summary>
    Private Function GetRaceHeight(state As MainForm.NPCVisualState) As Single
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return 1.0F
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return 1.0F
        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        ' MaleHeight/FemaleHeight: mismo campo, cada juego lo declara con su propio subrecord/offset.
        Dim h As Single
        Dim nf = TryCast(race, Canon.RaceFO4)
        If nf IsNot Nothing Then
            h = If(state.IsFemale, nf.DataFemaleHeight, nf.DataMaleHeight)
        Else
            Dim nsse = TryCast(race, Canon.RaceSSE)
            h = If(nsse Is Nothing, 0.0F, If(state.IsFemale, nsse.FemaleHeight, nsse.MaleHeight))
        End If
        If h <= 0 Then Return 1.0F
        Return h
    End Function

    ''' <summary>Arma la pose mergeada del NPC: race-height + body-weight (MWGT x BSMS + MRSV + ARMA) + FMRS, en
    ''' ese orden (top-down por la jerarquia del esqueleto).
    ''' <para>Las tres fuentes escriben campos DISJUNTOS de <c>PoseTransformData</c> (race a Scale, body-weight a
    ''' ScaleX/Y/Z, FMRS a T/R), asi que el merge por campo preserva el aporte de cada una aunque el mismo hueso
    ''' aparezca en dos. <c>PoseMath.MergePoses</c> loguea si alguna vez colisionan: es un canario.</para>
    ''' <para>⛔ Contrato con el caller: el esqueleto tiene que estar YA cargado y mergeado (cara/robot) antes de
    ''' llamar, porque el paso de body-weight camina su jerarquia para mapear huesos a regiones MRSV.</para></summary>
    ''' <param name="faceMorphsEnabled">FO4: checkbox "Bone morphs (FMRS)" (y "sin gender override").</param>
    ''' <param name="nodeTransformsEnabled">SSE: el MISMO checkbox, que alla rotula "Node transforms (RaceMenu)"
    ''' y gatea los node transforms de NiOverride, el unico canal de deformacion por nodo de ese juego. Va aparte
    ''' de faceMorphsEnabled porque ese trae AND-eado el gender-override, que no aplica a una escala de nodo del
    ''' cuerpo. Default True = sin gatear, para callers no-UI.</param>
    Friend Function BuildMergedNpcPose(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult,
                                        faceMorphsEnabled As Boolean,
                                        bodyWeightEnabled As Boolean,
                                        skeleton As SkeletonInstance,
                                        Optional armaSculptOverride As Dictionary(Of String, System.Numerics.Vector3) = Nothing,
                                        Optional suppressNeckNnam As Boolean = False,
                                        Optional nodeTransformsEnabled As Boolean = True) As Poses_class
        Dim racePose = PoseMath.BuildRaceHeightPose(GetRaceHeight(state))

        ' Body-weight (RACE.BSMS/MRSV) + ARMA sculpt. Sclpt y BW son toggles independientes:
        ' weightLayersEnabled=bodyWeightEnabled gobierna RACE.BSMS/MRSV; la capa ARMA se aplica si hay
        ' deltas (por eso el OrElse hasSculpt: un outfit con sculpt y BW=OFF igual arma la pose).
        Dim bwPose As Poses_class = Nothing
        Dim hasSculpt = (armaSculptOverride IsNot Nothing AndAlso armaSculptOverride.Count > 0)
        If bodyWeightEnabled OrElse hasSculpt Then
            Dim bwData = ResolveBodyWeightData(state, renderData)
            If bwData.HayDatos Then
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
        ' NO se afirma que sea el mecanismo del engine (consumidor del +0x50 nunca hallado); es la
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
        ' pose mechanism as NNAM (a PoseTransformData scale per bone), merged below. Gateado por el checkbox
        ' "Node transforms (RaceMenu)" — el canal ApplyBoneMorphs bajo Skyrim.
        Dim sseNodePose As Poses_class = If(nodeTransformsEnabled, BuildSseNodeScalePose(state), Nothing)

        Return PoseMath.MergePoses(racePose, bwPose, nnamPose, fmrsPose, sseNodePose)
    End Function

    ''' <summary>Build the SSE RaceMenu node-transform pose from the applied preset's SseNodeTransforms: one
    ''' PoseTransformData per named skeleton bone carrying the full TRS — uniform scale (key 30), translation
    ''' (key 31 → X/Y/Z) and rotation (key 32 → the axis-angle Yaw/Pitch/Roll the WardrobeManager pose source
    ''' feeds straight into BSRotationToMatrix33).
    ''' <para>DECÍA "reproducing the .jslot's 3×3 matrix exactly" y hay que acotarlo: el pose sólo puede llevar
    ''' AXIS-ANGLE (<c>PoseTransformData</c> no tiene campo de matriz), mientras el <c>.jslot</c> y el ESP re-emiten
    ''' la matriz CRUDA cuando la hay. Reproduce la matriz exactamente para toda rotación propia — incluida la de
    ''' 180°, desde que <c>Matrix33ToBSRotation</c> saca bien ese eje. Lo que NO puede reproducir es una REFLEXIÓN
    ''' (det = −1), que no es una rotación y no tiene axis-angle: ahí el preview muestra la rotación más cercana y
    ''' el archivo/ESP llevan la reflexión. Ningún preset del corpus instalado trae rotación, así que el caso está
    ''' razonado y no medido; el arreglo, si aparece, es un campo de matriz en <c>PoseTransformData</c>.</para> This matches
    ''' skee's Impl_UpdateNodeAllTransforms, which composes finalLocal = baseTransform · (pos·scale·rot) — the
    ''' render's MorphDeltaTransform layer is exactly that override transform. Nothing on FO4 / when no
    ''' non-identity transforms.</summary>
    Private Function BuildSseNodeScalePose(state As MainForm.NPCVisualState) As Poses_class
        If state Is Nothing OrElse Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return Nothing
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(state.RootNpcFormID, preset) OrElse preset Is Nothing Then Return Nothing
        Dim nts = preset.SseNodeTransforms
        If nts Is Nothing OrElse nts.Count = 0 Then Return Nothing
        Dim pose As New Poses_class With {
            .Name = "SSE Node Transform",
            .Source = Poses_class.Pose_Source_Enum.WardrobeManager,
            .Transforms = New Dictionary(Of String, PoseTransformData)(StringComparer.OrdinalIgnoreCase)
        }
        For Each nt In nts
            If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
            ' Skip a node whose whole TRS is identity (1.0 scale / 0 offset / 0 rotation) — a no-op layer.
            If nt.IsIdentity Then Continue For
            Dim td As New PoseTransformData()
            If nt.HasScale Then td.Scale = nt.Scale                       ' uniform scale (key 30)
            If nt.HasPosition Then td.X = nt.PosX : td.Y = nt.PosY : td.Z = nt.PosZ   ' translation (key 31)
            If nt.HasRotation Then td.Yaw = nt.RotX : td.Pitch = nt.RotY : td.Roll = nt.RotZ  ' rotation axis-angle (key 32)
            pose.Transforms(nt.NodeName) = td
        Next
        If pose.Transforms.Count = 0 Then Return Nothing
        Return pose
    End Function

End Class
