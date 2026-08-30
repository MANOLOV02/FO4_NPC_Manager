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
''' constructor. Pure data resolution — no UI, no GL. See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcStateResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _lvlnDataCache As Dictionary(Of UInteger, Canon.ILvln)
    Private ReadOnly _genderFilter As Func(Of MainForm.GenderFilterMode)
    Private ReadOnly _resolveLmSkinTemplate As Func(Of String, LmSkinTemplate)
    Private Shared ReadOnly _rng As New Random()
    ''' <summary>Per-resolve cache of LVLN picks. When the same LVLN is encountered multiple times
    ''' during a single NPC resolution (e.g. Traits and Model both use same LVLN), the same NPC
    ''' is returned. This is how FO4 works: one random pick per LVLN per spawn.</summary>
    <ThreadStatic> Private Shared _lvlnPickCache As Dictionary(Of UInteger, UInteger)

    ''' <summary>Ancla de la hoja de LVLN para UNA resolucion. NO es una cache: no tiene vida mas alla
    ''' de la llamada, no es Shared y no es ThreadStatic — viaja por parametro porque
    ''' <see cref="ResolveNPCBaseState"/> corre bajo <c>Task.Run</c> (MainForm:5463, :5558, :9191) y el
    ''' preview principal y el de un editor pueden resolver a la vez.
    ''' <para>TRES estados: <b>Nothing</b> (el default del parametro) = deducir el ancla del host ·
    ''' <b><see cref="Reroll"/></b> (hoja en 0) = RE-SORTEAR · instancia con hoja = anclar.
    ''' Se modela con una CLASE y no con <c>UInteger?</c> a proposito: el ternario sobre Nullable
    ''' colapsa Nothing con 0, y 0 es un valor VALIDO ("no hay hoja") distinto de "no vino ancla".
    ''' Ver 00-reglas-vb-trampas-que-me-comi.</para></summary>
    Friend NotInheritable Class LeveledLeafPin
        Public ReadOnly TraitsLeaf As UInteger
        Public Sub New(traitsLeaf As UInteger)
            Me.TraitsLeaf = traitsLeaf
        End Sub
        ''' <summary>El gesto de azar: la cadena vuelve a sortear.</summary>
        Public Shared ReadOnly Reroll As New LeveledLeafPin(0UI)
    End Class

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   lvlnDataCache As Dictionary(Of UInteger, Canon.ILvln),
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
    ''' <param name="pin">Ancla de la hoja de LVLN. <c>Nothing</c> = deducirla del propio
    ''' <paramref name="host"/> · <see cref="LeveledLeafPin.Reroll"/> = re-sortear (boton de azar, nodo
    ''' LVLN) · instancia con hoja = anclar a esa (un host de editor nace vacio, asi que el llamador le
    ''' pasa la hoja del preview principal). Ver <see cref="LeveledLeafPin"/>.</param>
    Friend Function ResolveNPCBaseState(npc As NPC_Data, host As NpcRenderHost,
                                        Optional pin As LeveledLeafPin = Nothing) As MainForm.NPCVisualState
        ' Fresh LVLN pick cache for this resolution — ensures consistent picks across categories
        _lvlnPickCache = New Dictionary(Of UInteger, UInteger)()

        ' ⛔ LA HOJA QUE ESTA EN PANTALLA GANA. La ley ya existia y estaba escrita en UN solo lugar
        ' (MainForm.ResolveLvlnPick_Friend, :9270-9292: "the leaf currently ON SCREEN wins ... rolling
        ' AGAIN would pin the actor to a DIFFERENT leaf -- they would edit face A and get face B"), pero
        ' solo la consumia NpcTemplateMaterializer.MakeCategoryOwn: el RENDER volvia a sortear, y el
        ' editor abria sobre una hoja distinta a la que el usuario estaba mirando (EditFace_Form:4841 al
        ' mostrarse y :4763 en cada FullReload -> MainForm:9192 -> aca).
        ' MEDIDO sobre el corpus real: 1134 NPC de FO4 y 1306 de SSE tienen 2+ resultados posibles en su
        ' cadena de Traits; de esos, 565/993 cambian de PNAM, 424/548 de GENERO y 75/447 de RAZA entre
        ' una resolucion y la siguiente.
        ' Se lee de `host.LastRenderedState`, que es el estado del ULTIMO render terminado de ESTE host:
        ' no es una cache nueva, es el mismo dato que ya alimenta ResolveLvlnPick_Friend.
        Dim pinnedTraitsLeaf As UInteger = 0UI
        If pin IsNot Nothing Then
            pinnedTraitsLeaf = pin.TraitsLeaf
        Else
            ' If/Else explicito, NO ternario: ver el doc de LeveledLeafPin.
            Dim mostrado As MainForm.NPCVisualState = Nothing
            If host IsNot Nothing Then mostrado = host.LastRenderedState
            If mostrado IsNot Nothing AndAlso mostrado.RootNpcFormID = npc.FormID Then
                pinnedTraitsLeaf = mostrado.TraitsSourceFormID
            End If
        End If

        Dim warnings As New List(Of String)
        Dim traits = ResolveTraitsStateFromNPC(npc.FormID, New HashSet(Of UInteger)(), warnings, pinnedTraitsLeaf)
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
            .TraitsSourceFormID = traits.SourceFormID,
            .HeadDiffuseAlphaTest = (npc.Game = Config_App.Game_Enum.Fallout4) AndAlso (npc.Record.ConfigurationFlags And &H1000000UI) <> 0UI
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
        ' gate; BuildFaceMorphResolver: FO4 early-return, SSE sigue con applyChargenMorphs:=False para
        ' que el SkinnyMorph del weight — per-actor, válido cross-gender — siga aplicando y la cabeza no
        ' abra costura contra el cuerpo _0/_1), so the head shows the race default without the
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

        ApplyRaceFallbacks(state, traits, _ctx.PluginManager, AddressOf _ctx.ParseRaceCanonCached)
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
            ' SSE RaceMenu absolute hair tint (packed RGB from the .jslot) — precedence over the CLFM at
            ' hair-material resolution (ResolveHairTintColor). SSE-only; Nothing on FO4 / presets without hairColor.
            If overlayPreset.SseHairColorRgb.HasValue Then state.SseHairColorRgb = overlayPreset.SseHairColorRgb
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
                        state.SkinFormID = Canon.CanonInterpretacion.SkinDe(_ctx.ParseRaceCanonCached(raceRec2))
                    End If
                End If
            End If
            ' Face TXST del preset (.jslot actor.headTexture, skee64 PresetInterface.cpp:158-160 escribe
            ' npc->headData->headTexture). Sin esto el preset se HORNEABA y se escribía al ESP —el shadow lo
            ' aplica en NpcRecordOverlay:199-205— pero NO se veía en el preview: RENDER ≠ BAKE. El comentario de
            ' precedencia de ese shadow (:187-194) ya describía este paso como si existiera acá; ahora existe.
            '
            ' PRECEDENCIA idéntica a la del shadow, por construcción: se aplica ACÁ y el bloque de la plantilla
            ' LM de abajo lo pisa ⇒ LM SkinTemplate > preset .jslot headTexture > raw NPC.FTST.
            '
            ' GAME-AWARENESS: el reparto es por ORIGEN DEL DATO, no por un If de juego.
            '   • FO4 → la vía es la plantilla LM (bundle f4ee) = el bloque de abajo. `SseHeadTextureFormIDOverride`
            '     queda en Nothing (LooksmenuLoader lo puebla sólo desde `.jslot`), así que esta rama no corre.
            '   • SSE → RaceMenu no tiene plantillas de piel; la vía es ésta.
            ' NO se agrega un gate `If isSse` a propósito: el shadow del BAKE tampoco lo tiene, y gatear un
            ' solo lado volvería a abrir la divergencia render/bake que este mismo fix cierra.
            '
            ' TRI-ESTADO (espejo exacto de NpcRecordOverlay :192-205, que es el camino del BAKE):
            '   Nothing → no participa, se conserva lo que dejó ApplyRaceFallbacks.
            '   <> 0    → override explícito.
            '   = 0     → clear explícito (ver el bloque de abajo, que NO es simétrico y por eso está comentado).
            ' El gate es `.HasValue`, NO `<> 0UI`: sobre un nullable esa comparación da Boolean? y colapsa
            ' Nothing con 0 — o sea, deja el clear indistinguible de "sin override", que es el bug original.
            If overlayPreset.SseHeadTextureFormIDOverride.HasValue Then
                Dim ovFtst As UInteger = overlayPreset.SseHeadTextureFormIDOverride.Value
                state.HeadTextureFormID = ovFtst
                ' Explicit VIAJA CON el valor: declara "este face TXST es del ACTOR", que es lo que le gana al
                ' HDPT.TNAM en ResolveTextureSet. Ver el invariante documentado en CloneVisualState.
                state.ExplicitHeadTextureFormID = ovFtst
                If ovFtst = 0UI Then
                    ' CLEAR EXPLÍCITO — y acá NO alcanza con dejar los dos campos en 0.
                    ' ApplyRaceFallbacks corrió ANTES que este bloque (:114 vs :126) y, cuando el FTST crudo era 0,
                    ' ya sustituyó el DefaultFaceTexture de la raza sobre HeadTextureFormID (:382-392, las DOS
                    ' ramas: la de `HeadPartFormIDs.Count = 0` y el `ElseIf`, que es la que corre en el caso
                    ' común de un NPC CON head parts y la que este bloque re-implementa). El clear
                    ' llega DESPUÉS, así que hay que RE-DISPARAR ese mismo fallback: sin esto HeadTextureFormID
                    ' queda en 0 mientras el BAKE —que resuelve el shadow primero y corre ApplyRaceFallbacks
                    ' después (FaceGenBuilder :752 → :766)— sí obtiene el DFT ⇒ RENDER ≠ BAKE.
                    ' Es EXACTAMENTE el mismo movimiento, por la misma causa de orden, que el clear de skin de
                    ' :152-160 (Some(0) → re-sustituir RACE.WNAM).
                    '
                    ' Se resuelve CONTRA LA RAZA, no con un 0 hardcodeado, y el CLEAR SÓLO SACA EL PRIMER ESCALÓN.
                    ' La precedencia que aplica acá es la de SSE — `FTST > DFT[sexo propio] > HDPT.TNAM`,
                    ' NpcMaterialResolver :458. NO decir "RE de ambos binarios": bajo FO4 lo IMPLEMENTADO es
                    ' `FTST > TNAM > DFTM (si TNAM=0)` (:488), y la lectura DFT>TNAM que sugiere el RE del CK está
                    ' marcada ahí mismo como SIN MEDIR y no aplicada. Lo único RE-verificado en ambos binarios es
                    ' que el DFT no cruza de género (ver el bloque de ApplyRaceFallbacks), que es otra cosa.
                    ' Qué ve el usuario, entonces, depende de la raza:
                    '   • RACE con DFTM/DFTF → la cara cae al DefaultFaceTexture de la raza, y ResolveTextureSet
                    '     SÍ arma capa aux (:468, rama `RACE.DFTM(Face-aux)`, que existe también en SSE).
                    '   • RACE sin DFT (el caso de las razas custom tipo UBE) → queda 0, no hay capa aux,
                    '     isFaceTextureSource=False y el HDPT.TNAM se aplica COMPLETO, incluido TX03=_sk.
                    ' NO decir "en SSE los RACE no traen DFT": el campo DFTM/DFTF lo declaran los DOS juegos
                    ' (mismo campo, subrecord propio de cada uno), y hay casos SSE medidos que los traen (ver el
                    ' ManakinRace de NpcMaterialResolver). El comportamiento correcto sale de respetar la ley, no
                    ' de suponer 0.
                    Dim raceRecFtst = _ctx.PluginManager.GetRecord(state.RaceFormID)
                    If raceRecFtst IsNot Nothing AndAlso raceRecFtst.Header.Signature = "RACE" Then
                        Dim raceFtst = _ctx.ParseRaceCanonCached(raceRecFtst)
                        ' DFTM/DFTF: cada juego lo declara con su propio nombre generado.
                        Dim raceFtstFo4 = TryCast(raceFtst, Canon.RaceFO4)
                        Dim raceFtstSse = TryCast(raceFtst, Canon.RaceSSE)
                        If raceFtstFo4 IsNot Nothing Then
                            state.HeadTextureFormID = If(state.IsFemale, raceFtstFo4.FemaleDefaultFaceTexture, raceFtstFo4.MaleDefaultFaceTexture)
                        ElseIf raceFtstSse IsNot Nothing Then
                            state.HeadTextureFormID = If(state.IsFemale, raceFtstSse.FemaleHeadDataDefaultFaceTextureFemale, raceFtstSse.MaleHeadDataDefaultFaceTextureMale)
                        End If
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
                        ' Explicit VIAJA CON el valor: es lo que declara "este face TXST es del ACTOR, no el
                        ' default de la raza", y de eso depende que ResolveTextureSet le gane al HDPT.TNAM
                        ' (:584). Esta línea corre DESPUÉS de ApplyRaceFallbacks —que ya fijó Explicit desde el
                        ' FTST crudo—, así que sin actualizarlo el par queda incoherente: HeadTextureFormID =
                        ' plantilla LM pero Explicit = FTST crudo, y el resolver aplicaría el FTST pisando la
                        ' plantilla que el usuario acaba de elegir.
                        ' Es lo que YA hace el BAKE: NpcRecordOverlay:205 mete el TXST de la plantilla en
                        ' shadow.HeadTextureFormID y ApplyRaceFallbacks se lo copia a Explicit (≠0 ⇒ no entra el
                        ' fallback DFTM) ⇒ en el bake la plantilla SIEMPRE le gana al TNAM. RENDER == BAKE.
                        state.ExplicitHeadTextureFormID = tpl.FaceTxstFormID(genderIdx)
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
            ' Sleep outfit (NPC.SOFT) override — set by the NPC Editor's Inventory tab. Applied at the state
            ' level so BuildOutfitComboEntries re-samples the chosen OTFT into the outfit combo (mirror of DOFT).
            If overlayPreset.SleepOutfitFormIDOverride.HasValue Then
                state.SleepOutfitFormID = overlayPreset.SleepOutfitFormIDOverride.Value
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
            ' El ajuste manual del tono del cuerpo tiene que estar EN el state antes de resolver: el resolver
            ' del cuerpo lo lee de ahi. Se clona para que el state de un render no comparta instancia con el
            ' overlay que el editor esta moviendo.
            state.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(overlayPreset.SkinToneOffset)
            Dim presetSkin = _materialResolver.ResolveNpcBodySkinToneColor(state)
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

    ''' <summary>Clon del state base. Es un clon MANUAL campo por campo: un campo nuevo de
    ''' <see cref="MainForm.NPCVisualState"/> que no se agregue acá se pierde SILENCIOSAMENTE en TODO render —
    ''' este clon es el que arma el state final de cada render (MainForm:5298, para sumarle el outfit), así que
    ''' lo que no se copie no llega al resolver de materiales por más bien que lo haya resuelto
    ''' <see cref="ResolveNPCBaseState"/>.
    ''' <para>MEDIDO así (log del app, NPC Aeri 0x0001360B, 2026-07-29): faltaba <c>SseHairColorRgb</c> y el
    ''' tinte absoluto de RaceMenu no llegaba nunca — al cargar un preset morado (0x30001C) el pelo se pintaba
    ''' con el CLFM del NPC (HairColor13BrightGrey = 90,95,105, que se ve casi blanco con el ×2). Sólo el
    ''' refresh live acertaba, porque muta <c>LastRenderedState</c> en sitio y no pasa por acá.</para>
    ''' <para>El MISMO bug se comía <c>ExplicitHeadTextureFormID</c> (arreglado en la misma tanda). Ese alimenta
    ''' la precedencia <b>FTST &gt; HDPT.TNAM &gt; DFTM</b> de ResolveTextureSet, y el efecto se reparte así:
    ''' <list type="bullet">
    ''' <item><b>FO4 — bug real y visible.</b> La rama FTST exige <c>Explicit &lt;&gt; 0</c> para pisar un TNAM
    '''   presente; con el campo en 0 ganaba el TNAM del head part, al revés de la ley validada byte-exacta
    '''   contra bakes del CK (NpcMaterialResolver:515-517, caso Mitch). El BAKE sí la cumplía —construye su
    '''   state con ApplyRaceFallbacks, FaceGenBuilder:299— así que esto era RENDER ≠ BAKE.
    '''   MEDIDO sobre Fallout4.esm: 715 NPC_ con FTST propio, de los cuales <b>422</b> tienen además una head
    '''   part Face con TNAM ≠ 0 — ésos son exactamente los que cambian (hacia el bake).</item>
    ''' <item><b>SSE — no-op de comportamiento.</b> Sus dos ramas (:549-552) resuelven al MISMO FormID:
    '''   <c>HeadTextureFormID</c> ya ES el FTST cuando el NPC tiene uno (ApplyRaceFallbacks sólo lo pisa con
    '''   DFTM si vale 0). Únicamente cambiaba la etiqueta del log.</item></list></para></summary>
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
            .ExplicitHeadTextureFormID = state.ExplicitHeadTextureFormID,
            .HeadDiffuseAlphaTest = state.HeadDiffuseAlphaTest,
            .HairColorFormID = state.HairColorFormID,
            .SseHairColorRgb = state.SseHairColorRgb,
            .FacialHairColorFormID = state.FacialHairColorFormID,
            .HasTextureLighting = state.HasTextureLighting,
            .TextureLightingColor = state.TextureLightingColor,
            .SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(state.SkinToneOffset),
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

    ''' <param name="parseRace">Optional cached RACE parser (NpcRenderContext.ParseRaceCanonCached). Falls back to a
    ''' direct <c>Canon.CanonRecords.Race</c> when Nothing — keeps the offline bake path pure.</param>
    Friend Shared Sub ApplyRaceFallbacks(state As MainForm.NPCVisualState, traits As MainForm.TraitsState, pluginManager As PluginManager,
                                         Optional parseRace As Func(Of PluginRecord, Canon.IRace) = Nothing)
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return

        Dim raceRec = pluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            ' No RACE record: all-Default MWGT can't be resolved → leave 0; explicit values pass through.
            state.WeightThin = traits.WeightThin.GetValueOrDefault(0.0F)
            state.WeightMuscular = traits.WeightMuscular.GetValueOrDefault(0.0F)
            state.WeightFat = traits.WeightFat.GetValueOrDefault(0.0F)
            Return
        End If

        Dim race = If(parseRace IsNot Nothing, parseRace(raceRec), Canon.CanonRecords.Race(raceRec, pluginManager))
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim raceSse = TryCast(race, Canon.RaceSSE)

        ' Materialize NPC.MWGT into final 3 floats. Substitution rule lives in ResolveBodyWeights.
        ' Done before the head/skin fallbacks so callers reading state.WeightX downstream always
        ' see resolved values.
        Dim resolvedWeights = NpcStateFactory.ResolveBodyWeights(traits, race, state.IsFemale)
        state.WeightThin = resolvedWeights.Thin
        state.WeightMuscular = resolvedWeights.Muscular
        state.WeightFat = resolvedWeights.Fat

        ' Por SkinDe y no por race.Skin a pelo: la misma ley la usa ahora el camino de ESCRITURA, y
        ' ahí la raza SÍ puede ser nula. Una sola versión, con guarda, para los dos.
        If state.SkinFormID = 0UI Then
            state.SkinFormID = Canon.CanonInterpretacion.SkinDe(race)
        End If

        ' FTST PROPIO del NPC (0 si no tiene), capturado ANTES del fallback DFTM de abajo. Acá
        ' state.HeadTextureFormID aún es el FTST del record; las líneas siguientes lo pisan con DFTM cuando es 0.
        ' Lo usa ResolveTextureSet para la precedencia FTST > HDPT.TNAM > DFTM (sin esto no se distingue FTST de DFTM).
        state.ExplicitHeadTextureFormID = state.HeadTextureFormID

        ' Head Part\HEAD y DFTM/DFTF: cada juego los declara con su propia colección (RaceFO4.Male/
        ' FemaleHeadParts + Male/FemaleDefaultFaceTexture; RaceSSE.HeadParts/HeadParts2 +
        ' MaleHeadDataDefaultFaceTextureMale/FemaleHeadDataDefaultFaceTextureFemale).
        ' DFTM/DFTF: SOLO el género propio — el motor NO cruza al otro género (RE 2026-07-16,
        ' AMBOS binarios byte-verificados): SSE runtime RegenerateHead 0x14042BEDB y FO4 CK
        ' resolver 0x140ED4244 hacen UNA sola lectura race.faceRelatedData[GetSex(npc)]
        ' (SSE race+0x4A8+sex*8 → frd+0xA0; FO4 CK race+0xBA8+sex*8 → frd+0x10); si es null
        ' caen a HDPT.TNAM, jamás al DFT del otro sexo. El fallback cruzado que había acá
        ' pintaba SkinHeadFemaleNord (DFTF) en el maniquí masculino de ManakinRace (sin DFTM).
        ' Las dos leyes -que head parts trae la raza y cual es su textura de cara por defecto- viven
        ' cada una en UN solo lugar, que ya contempla los dos juegos con sus nombres propios. Aca
        ' estaban copiadas a mano: corregir alla dejaba esta copia vieja sin que nadie se enterara.
        Dim maleHeadParts As IEnumerable(Of UInteger) = Canon.CanonInterpretacion.HeadPartsDe(race, isFemale:=False)
        Dim femaleHeadParts As IEnumerable(Of UInteger) = Canon.CanonInterpretacion.HeadPartsDe(race, isFemale:=True)
        Dim maleDefaultFaceTexture = Canon.CanonInterpretacion.DefaultFaceTextureDe(race, isFemale:=False)
        Dim femaleDefaultFaceTexture = Canon.CanonInterpretacion.DefaultFaceTextureDe(race, isFemale:=True)

        If state.HeadPartFormIDs.Count = 0 Then
            If state.IsFemale Then
                state.HeadPartFormIDs.AddRange(femaleHeadParts)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = femaleDefaultFaceTexture
            Else
                state.HeadPartFormIDs.AddRange(maleHeadParts)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = maleDefaultFaceTexture
            End If
        ElseIf state.HeadTextureFormID = 0UI Then
            state.HeadTextureFormID = If(state.IsFemale, femaleDefaultFaceTexture, maleDefaultFaceTexture)
        End If

        ' HairColor fallback: when NPC.HCLF is absent (and the template chain didn't supply one
        ' either — Model/Animation traits already collapsed by ResolveModelAnimationStateFromNPC),
        ' the engine reads RACE.HCLF[gender] (Default Hair Colors). Mirror that here. Each gender
        ' slot can be NULL per the schema's allowed-value list for the field (NULL or CLFM).
        ' NOTA: el cross-gender fallback de acá NO está verificado contra el motor (el análogo de
        ' DefaultFaceTexture se RE-verificó 2026-07-16 y NO cruza género — ver arriba); se conserva
        ' pendiente de su propio RE porque nadie midió un síntoma en su contra.
        If state.HairColorFormID = 0UI Then
            Dim hclf = race.DefaultHairColors
            Dim maleHcl As UInteger = If(hclf.Count > 0, hclf(0).DefaultHairColor, 0UI)
            Dim femaleHcl As UInteger = If(hclf.Count > 1, hclf(1).DefaultHairColor, 0UI)
            Dim ownGender = If(state.IsFemale, femaleHcl, maleHcl)
            Dim otherGender = If(state.IsFemale, maleHcl, femaleHcl)
            state.HairColorFormID = If(ownGender <> 0UI, ownGender, otherGender)
        End If
    End Sub

    Friend Function ResolveSkeletonKey(state As MainForm.NPCVisualState, warnings As List(Of String)) As String
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return ""

        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return ""

        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        Dim maleSkel = If(race.MaleSkeletalModelPresente, race.MaleSkeletalModel, "")
        Dim femaleSkel = If(race.FemaleSkeletalModelPresente, race.FemaleSkeletalModel, "")
        Dim skeletonPath = If(state.IsFemale, femaleSkel, maleSkel)
        If String.IsNullOrWhiteSpace(skeletonPath) Then
            skeletonPath = If(maleSkel <> "", maleSkel, femaleSkel)
        End If

        Dim dictionaryKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(skeletonPath)
        If dictionaryKey = "" Then warnings.Add($"No skeleton path resolved for race {state.RaceFormID:X8}")
        Return dictionaryKey
    End Function

    ''' <param name="pinnedTraitsLeaf">Hoja de LVLN que esta EN PANTALLA (0 = sortear). Viaja por
    ''' parametro y NO por un campo: <see cref="ResolveNPCBaseState"/> corre bajo <c>Task.Run</c>
    ''' (MainForm:5463, :5558, :9191) y dos previews pueden resolver a la vez.</param>
    Friend Function ResolveTraitsStateFromNPC(formID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String),
                                              Optional pinnedTraitsLeaf As UInteger = 0UI) As MainForm.TraitsState
        Dim npc = _ctx.GetParsedNpc(formID)
        If npc Is Nothing Then Return Nothing

        Dim own = NpcStateFactory.CreateOwnTraitsState(npc)
        If visited.Contains(formID) Then Return own

        Dim acbsOppGender As Boolean = (npc.Record.ConfigurationFlags And &H80000UI) <> 0UI

        If Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits) Then
            Return own
        End If

        visited.Add(formID)
        Dim sourceFormID = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
        Dim sourceRec = _ctx.PluginManager.GetRecord(sourceFormID)

        Dim resolved = ResolveTraitsStateFromTemplateSource(sourceFormID, visited, warnings, pinnedTraitsLeaf)
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
        If Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Inventory) Then Return own

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

    Private Function ResolveTraitsStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String),
                                                          pinnedTraitsLeaf As UInteger) As MainForm.TraitsState
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Traits", visited, warnings, pinnedTraitsLeaf)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveTraitsStateFromNPC(sourceRecord.Header.FormID, visited, warnings, pinnedTraitsLeaf)
    End Function

    Private Function ResolveInventoryStateFromTemplateSource(sourceFormID As UInteger, visited As HashSet(Of UInteger), warnings As List(Of String)) As MainForm.InventoryState
        ' 0UI a proposito: el ancla es de la cadena de Traits. La de Inventory sigue sorteando (frente
        ' declarado y NO arreglado: 181 NPC de FO4 y 281 de SSE cambian de outfit entre resoluciones,
        ' porque `NPCVisualState.InventorySourceFormID` esta declarado y nunca se asigna).
        Dim sourceRecord = ResolveTemplateSourceRecord(sourceFormID, "Inventory", visited, warnings, 0UI)
        If sourceRecord Is Nothing Then Return Nothing
        Return ResolveInventoryStateFromNPC(sourceRecord.Header.FormID, visited, warnings)
    End Function

    ''' <param name="pinnedLeaf">Ancla de la categoria que se esta caminando (0 = sortear). Ver
    ''' <see cref="ResolveSingleLeveledTemplate"/>.</param>
    Private Function ResolveTemplateSourceRecord(sourceFormID As UInteger, categoryName As String, visited As HashSet(Of UInteger),
                                                 warnings As List(Of String), pinnedLeaf As UInteger) As PluginRecord
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
                Dim resolvedFormID = ResolveSingleLeveledTemplate(sourceRecord, warnings, pinnedLeaf, categoryName)
                If resolvedFormID = 0UI Then Return Nothing
                If visited.Contains(resolvedFormID) Then Return Nothing
                Return ResolveTemplateSourceRecord(resolvedFormID, categoryName, visited, warnings, pinnedLeaf)
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

        Dim lvln As Canon.ILvln = Nothing
        If Not _lvlnDataCache.TryGetValue(lvlnFormID, lvln) Then
            Dim lvlnRec = GetRecordMemoized(lvlnFormID, recordMemo)
            If lvlnRec Is Nothing OrElse lvlnRec.Header.Signature <> "LVLN" Then Return 0UI
            ' Este fallback re-parsea un record que la caché puede haber salteado A PROPÓSITO: el barrido de
            ' arranque excluye los LVLN que no parsean (y lo reporta). Sin el Try, un LVLN roto que un template
            ' referencie volvía a lanzar acá — o sea en tiempo de RESOLUCIÓN DE ESTADO, con el usuario mirando
            ' un NPC. Sin lista no hay leaf que elegir: se devuelve 0, que es lo mismo que una lista vacía.
            lvln = NpcTemplateHelpers.TryAbrirLvlnTolerante(lvlnRec, _ctx.PluginManager)
            ' El FALLO también se cachea. Sin esto, un LVLN roto que un template referencie se re-parsea
            ' —con su Throw + Catch + log adentro de TryAbrirLvlnTolerante— en CADA resolución de estado, o sea en cada
            ' selección de NPC que lo toque, para un resultado que ya se sabe constante. Es seguro porque
            ' `_lvlnDataCache` se vacía en el rebuild de clasificación (MainForm:4163), que es justo lo que
            ' corre cuando se recargan los plugins: si el LVLN se arregla en disco, la caché ya no existe.
            _lvlnDataCache(lvlnFormID) = lvln
        End If
        If lvln Is Nothing Then Return 0UI

        ' Build weighted list of leaf NPC FormIDs: each entry contributes Count copies
        Dim weightedLeaves As New List(Of UInteger)()

        For Each entry In lvln.LeveledListEntries
            If entry.LeveledListEntryNPC = 0UI Then Continue For
            Dim entryRec = GetRecordMemoized(entry.LeveledListEntryNPC, recordMemo)
            If entryRec Is Nothing Then Continue For

            Dim weight = Math.Max(CInt(entry.LeveledListEntryCount), 1)

            Select Case entryRec.Header.Signature
                Case "NPC_"
                    For i = 0 To weight - 1
                        weightedLeaves.Add(entry.LeveledListEntryNPC)
                    Next
                Case "LVLN"
                    ' Recurse into nested LVLN: pick from sub-list, weighted by this entry's Count
                    For i = 0 To weight - 1
                        Dim subPick = PickWeightedRandomFromLVLN(entry.LeveledListEntryNPC, New HashSet(Of UInteger)(visited), recordMemo)
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
                                                        Return If(genderFilter = MainForm.GenderFilterMode.Female, npc.Record.ConfigurationFlagsFemale, Not npc.Record.ConfigurationFlagsFemale)
                                                    End If
                                                    Dim npcRec = GetRecordMemoized(fid, recordMemo)
                                                    If npcRec Is Nothing OrElse npcRec.Header.Signature <> "NPC_" Then Return True
                                                    ' Vista canónica en vez del parse completo: acá sólo hace falta el espejo de
                                                    ' género de ACBS, no las otras ~70 subrecords que ParseNPC decodifica.
                                                    Dim canonNpc = Canon.CanonRecords.Npc(npcRec, _ctx.PluginManager)
                                                    If canonNpc Is Nothing Then Return True
                                                    Dim esFemale = canonNpc.ConfigurationFlagsFemale
                                                    Return If(genderFilter = MainForm.GenderFilterMode.Female, esFemale, Not esFemale)
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
    ''' <remarks>Precedencia: (1) el pick que YA hizo esta misma resolucion para esta lista ·
    ''' (2) la hoja que esta EN PANTALLA · (3) el sorteo ponderado de siempre.</remarks>
    Private Function ResolveSingleLeveledTemplate(lvlnRec As PluginRecord, warnings As List(Of String),
                                                  pinnedLeaf As UInteger, categoryName As String) As UInteger
        Dim lvlnFormID = lvlnRec.Header.FormID

        ' Check cache first — same LVLN must return same pick within one NPC resolution
        If _lvlnPickCache IsNot Nothing Then
            Dim cached As UInteger = 0UI
            If _lvlnPickCache.TryGetValue(lvlnFormID, cached) Then
                Return cached
            End If
        End If

        ' El ancla y el sorteo van DENTRO DE UNA SOLA ventana de lock de lectura: el censo de hojas y el
        ' sorteo tienen que ver el MISMO juego de records. `PickWeightedRandomFromLVLN` vuelve a entrar
        ' al lock, cosa legal porque el RWLS es recursivo (PluginManager.vb:37,
        ' LockRecursionPolicy.SupportsRecursion). Es lectura pura: no se llama a ningun camino de
        ' ESCRITURA teniendo el de lectura.
        Dim picked As UInteger = _ctx.PluginManager.RunUnderRecordsReadLock(
            Function() As UInteger
                If pinnedLeaf <> 0UI Then
                    Dim hojas = NpcTemplateHelpers.CollectLvlnLeafNpcFormIDs(lvlnFormID, _ctx.PluginManager)
                    If hojas IsNot Nothing AndAlso hojas.Count > 0 Then
                        ' (2a) la hoja en pantalla es entrada DIRECTA de esta lista. Ley identica a
                        ' MainForm.ResolveLvlnPick_Friend (:9288); `CollectLvlnLeafNpcFormIDs` ya devuelve
                        ' el conjunto APLANADO (NpcTemplateHelpers.vb:74-75 recursa en las anidadas), asi
                        ' que las LVLN anidadas entran aca.
                        If hojas.Contains(pinnedLeaf) Then Return pinnedLeaf
                        ' (2b) la hoja en pantalla es el TERMINAL de la cadena Use-Traits de alguna hoja
                        ' de esta lista (LVLN -> hoja B -> Use Traits -> C). Sin esto el ancla se degrada
                        ' en silencio al sorteo en 69 NPC de FO4 y 235 de SSE — MEDIDO. Es EXACTO porque
                        ' ese tramo es DETERMINISTA: en el corpus NINGUNA cadena de Traits encadena dos
                        ' LVLN (hops max = 1 en los dos juegos).
                        ' Se devuelve la hoja B y NO el terminal C: el terminal iria al `_lvlnPickCache` y
                        ' la cadena de Inventory que comparta la lista se llevaria el actor equivocado.
                        ' SOLO para Traits: `TerminalDeTraits` camina el bit Traits, y aplicarlo a
                        ' Inventory buscaria en la cadena equivocada.
                        If categoryName = "Traits" Then
                            For Each hoja In hojas
                                If TerminalDeTraits(hoja) = pinnedLeaf Then Return hoja
                            Next
                        End If
                    End If
                End If
                ' (3) sin ancla utilizable: el sorteo de siempre, intacto.
                Return PickWeightedRandomFromLVLN(lvlnFormID, New HashSet(Of UInteger)())
            End Function)

        If picked = 0UI Then
            warnings.Add($"Leveled template {NpcManagerFormat.DescribeRecord(lvlnRec)} has no usable entries")
            Return 0UI
        End If

        If _lvlnPickCache IsNot Nothing Then _lvlnPickCache(lvlnFormID) = picked
        Return picked
    End Function

    ''' <summary>Final de la cadena "Use Traits" de <paramref name="formID"/> SIN pasar por ninguna LVLN
    ''' (si aparece una, se rinde y devuelve 0: no se sortea aca). Solo lo usa el paso (2b) de
    ''' <see cref="ResolveSingleLeveledTemplate"/>. Corre con el lock de lectura YA TOMADO — por eso usa
    ''' <c>GetRecordNoLock</c>, igual que el resto del walk.</summary>
    Private Function TerminalDeTraits(formID As UInteger) As UInteger
        Dim actual = formID
        Dim vistos As New HashSet(Of UInteger)()
        For paso = 0 To 31
            If Not vistos.Add(actual) Then Return 0UI
            Dim npc = _ctx.GetParsedNpc(actual)
            If npc Is Nothing Then Return 0UI
            If Not NpcTemplateHelpers.HasTemplateFlag(npc.Record.ConfigurationTemplateFlags, NPC_TemplateCategory.Traits) Then Return actual
            Dim siguiente = NpcTemplateHelpers.ResolveTemplateSourceFormID(npc, NPC_TemplateCategory.Traits)
            If siguiente = 0UI Then Return actual
            Dim rec = _ctx.PluginManager.GetRecordNoLock(siguiente)
            If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Return 0UI
            actual = siguiente
        Next
        Return 0UI
    End Function

    ''' <summary>Costura Friend de <see cref="TerminalDeTraits"/> para
    ''' <c>MainForm.ResolveLvlnPick_Friend</c>: la ley del ancla vive en UN solo sitio y el Save la
    ''' CONSUME, no la re-escribe. Toma el lock de lectura porque, a diferencia del llamador interno,
    ''' aca no esta tomado.</summary>
    Friend Function TerminalDeTraitsPublico(formID As UInteger) As UInteger
        Return _ctx.PluginManager.RunUnderRecordsReadLock(Function() TerminalDeTraits(formID))
    End Function

End Class
