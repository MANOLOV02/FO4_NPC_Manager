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
            .TraitsSourceFormID = traits.SourceFormID,
            .HeadDiffuseAlphaTest = (npc.Game = Config_App.Game_Enum.Fallout4) AndAlso (npc.AcbsFlags And &H1000000UI) <> 0UI
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
                        state.SkinFormID = _ctx.ParseRaceCached(raceRec2).SkinFormID
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
            ' ⛔ NO se agrega un gate `If isSse` a propósito: el shadow del BAKE tampoco lo tiene, y gatear un
            ' solo lado volvería a abrir la divergencia render/bake que este mismo fix cierra.
            '
            ' TRI-ESTADO (espejo exacto de NpcRecordOverlay :192-205, que es el camino del BAKE):
            '   Nothing → no participa, se conserva lo que dejó ApplyRaceFallbacks.
            '   <> 0    → override explícito.
            '   = 0     → clear explícito (ver el bloque de abajo, que NO es simétrico y por eso está comentado).
            ' ⛔ El gate es `.HasValue`, NO `<> 0UI`: sobre un nullable esa comparación da Boolean? y colapsa
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
                    ' ⛔ Se resuelve CONTRA LA RAZA, no con un 0 hardcodeado, y el CLEAR SÓLO SACA EL PRIMER ESCALÓN.
                    ' La precedencia que aplica acá es la de SSE — `FTST > DFT[sexo propio] > HDPT.TNAM`,
                    ' NpcMaterialResolver :458. ⚠️ NO decir "RE de ambos binarios": bajo FO4 lo IMPLEMENTADO es
                    ' `FTST > TNAM > DFTM (si TNAM=0)` (:488), y la lectura DFT>TNAM que sugiere el RE del CK está
                    ' marcada ahí mismo como SIN MEDIR y no aplicada. Lo único RE-verificado en ambos binarios es
                    ' que el DFT no cruza de género (ver el bloque de ApplyRaceFallbacks), que es otra cosa.
                    ' Qué ve el usuario, entonces, depende de la raza:
                    '   • RACE con DFTM/DFTF → la cara cae al DefaultFaceTexture de la raza, y ResolveTextureSet
                    '     SÍ arma capa aux (:468, rama `RACE.DFTM(Face-aux)`, que existe también en SSE).
                    '   • RACE sin DFT (el caso de las razas custom tipo UBE) → queda 0, no hay capa aux,
                    '     isFaceTextureSource=False y el HDPT.TNAM se aplica COMPLETO, incluido TX03=_sk.
                    ' ⚠️ NO decir "en SSE los RACE no traen DFT": `RecordParsers.ParseRACE` parsea DFTM/DFTF SIN
                    ' gate de juego (Case "DFTM"/"DFTF"), y hay casos SSE medidos que los traen (ver el ManakinRace
                    ' de NpcMaterialResolver). El comportamiento correcto sale de respetar la ley, no de suponer 0.
                    Dim raceRecFtst = _ctx.PluginManager.GetRecord(state.RaceFormID)
                    If raceRecFtst IsNot Nothing AndAlso raceRecFtst.Header.Signature = "RACE" Then
                        Dim raceFtst = _ctx.ParseRaceCached(raceRecFtst)
                        state.HeadTextureFormID = If(state.IsFemale,
                                                     raceFtst.FemaleDefaultFaceTextureFormID,
                                                     raceFtst.MaleDefaultFaceTextureFormID)
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
                        ' ⛔ Explicit VIAJA CON el valor: es lo que declara "este face TXST es del ACTOR, no el
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

    ''' <summary>Clon del state base. ⛔ Es un clon MANUAL campo por campo: un campo nuevo de
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

        ' DFTM/DFTF: SOLO el género propio — el motor NO cruza al otro género (RE 2026-07-16,
        ' AMBOS binarios byte-verificados): SSE runtime RegenerateHead 0x14042BEDB y FO4 CK
        ' resolver 0x140ED4244 hacen UNA sola lectura race.faceRelatedData[GetSex(npc)]
        ' (SSE race+0x4A8+sex*8 → frd+0xA0; FO4 CK race+0xBA8+sex*8 → frd+0x10); si es null
        ' caen a HDPT.TNAM, jamás al DFT del otro sexo. El fallback cruzado que había acá
        ' pintaba SkinHeadFemaleNord (DFTF) en el maniquí masculino de ManakinRace (sin DFTM).
        If state.HeadPartFormIDs.Count = 0 Then
            If state.IsFemale Then
                state.HeadPartFormIDs.AddRange(race.FemaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = race.FemaleDefaultFaceTextureFormID
            Else
                state.HeadPartFormIDs.AddRange(race.MaleHeadPartFormIDs)
                If state.HeadTextureFormID = 0UI Then state.HeadTextureFormID = race.MaleDefaultFaceTextureFormID
            End If
        ElseIf state.HeadTextureFormID = 0UI Then
            state.HeadTextureFormID = If(state.IsFemale, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
        End If

        ' HairColor fallback: when NPC.HCLF is absent (and the template chain didn't supply one
        ' either — Model/Animation traits already collapsed by ResolveModelAnimationStateFromNPC),
        ' the engine reads RACE.HCLF[gender] (Default Hair Colors). Mirror that here. Each gender
        ' slot can be NULL per wbFormIDCk([NULL, CLFM]) at wbDefinitionsFO4.pas:11575.
        ' NOTA: el cross-gender fallback de acá NO está verificado contra el motor (el análogo de
        ' DefaultFaceTexture se RE-verificó 2026-07-16 y NO cruza género — ver arriba); se conserva
        ' pendiente de su propio RE porque nadie midió un síntoma en su contra.
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
            ' ⛔ Este fallback re-parsea un record que la caché puede haber salteado A PROPÓSITO: el barrido de
            ' arranque excluye los LVLN que no parsean (y lo reporta). Sin el Try, un LVLN roto que un template
            ' referencie volvía a lanzar acá — o sea en tiempo de RESOLUCIÓN DE ESTADO, con el usuario mirando
            ' un NPC. Sin lista no hay leaf que elegir: se devuelve 0, que es lo mismo que una lista vacía.
            lvln = RecordParsers.TryParseLVLN(lvlnRec, _ctx.PluginManager)
        End If
        If lvln Is Nothing Then Return 0UI

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
