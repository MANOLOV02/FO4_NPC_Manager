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
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Pipeline de CANDIDATOS del preview: recolecta (ARMO/OTFT/head parts/chunks de robot) →
''' resuelve conflicto de slots y oclusión por headwear → carga los NIF y aplica los overrides de material
''' por shape.
''' <para>Datos y parsing puros: NADA de WinForms ni de GL acá, porque corre en el Task de render. La
''' orquestación y el esqueleto se quedan en MainForm. Ver 61-perf-mainform-split.md.</para></summary>
Friend NotInheritable Class NpcMeshCollector
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _stateResolver As NpcStateResolver
    Private ReadOnly _mountingResolver As NpcMountingResolver
    Private ReadOnly _armoIsPowerArmor As Func(Of UInteger, Boolean)
    Private ReadOnly _raceIsPowerArmor As Func(Of UInteger, Boolean)

    ''' <summary>Per-mesh cache for CandidateHairSlotMask, keyed by normalized mesh key
    ''' (candidate.DictKey, already a FilesDictionary key). Hair-slot occupancy is a property of the mesh
    ''' file alone (its BSSubIndexTriShape segmentation), stable across NPCs sharing the same hair mesh,
    ''' so it's worth memoizing. (Owner moved from MainForm._candidateHairSlotMaskCache.)</summary>
    Private ReadOnly _candidateHairSlotMaskCache As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Per-mesh cache for CandidatePartitionSlotMask (Skyrim head-part occlusion). Same shape/
    ''' lifetime as <see cref="_candidateHairSlotMaskCache"/>: the BSDismemberSkinInstance partition set is a
    ''' property of the mesh file alone, stable across NPCs sharing the mesh.</summary>
    Private ReadOnly _candidatePartitionSlotMaskCache As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase)

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   stateResolver As NpcStateResolver, mountingResolver As NpcMountingResolver,
                   armoIsPowerArmor As Func(Of UInteger, Boolean),
                   raceIsPowerArmor As Func(Of UInteger, Boolean))
        _ctx = ctx
        _materialResolver = materialResolver
        _stateResolver = stateResolver
        _mountingResolver = mountingResolver
        _armoIsPowerArmor = armoIsPowerArmor
        _raceIsPowerArmor = raceIsPowerArmor
    End Sub

    ''' <summary>EL discriminador de FaceGen, engine-faithful: <c>RACE.DATA</c> bit 0x2 "FaceGen Head".
    ''' Con el bit claro, ninguno de los dos motores construye cabeza. Ver 40-bake-leyes-fo4.md.
    ''' <para>NO confundirlo con "¿existe el FaceGeom horneado?": aguas abajo del gate el motor elige
    ''' entre cargar el NIF horneado o armar la cabeza desde head parts, así que la ausencia del NIF elige
    ''' RAMA, no apaga el FaceGen. Usar esa heurística dejaba el insumo <c>_faceBones</c> sin recolectar y
    ''' los sliders de Bone Regions del editor no hacían nada.</para>
    ''' <para>Misma regla que el botón Edit Face y que el bake, pero acá vía el cache de razas del render
    ''' para no re-parsear el RACE en el hot path de la selección de NPC.</para></summary>
    Private Function RaceBuildsFaceGenHead(state As MainForm.NPCVisualState) As Boolean
        If state Is Nothing OrElse state.RaceFormID = 0UI Then Return False
        Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return False
        Dim race = _ctx.ParseRaceCanonCached(raceRec)
        ' DATA\Flags\FaceGen Head: mismo bit, declarado por cada juego con su propio nombre generado.
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        If raceFo4 IsNot Nothing Then Return raceFo4.DataFlagsFaceGenHead
        Dim raceSse = TryCast(race, Canon.RaceSSE)
        If raceSse IsNot Nothing Then Return raceSse.FlagsFaceGenHead
        Return False
    End Function

    Friend Function ResolvePreviewVariant(previewVariant As MainForm.PreviewVariantDefinition) As MainForm.PreviewResolutionResult
        Dim result As New MainForm.PreviewResolutionResult()
        If previewVariant Is Nothing OrElse previewVariant.State Is Nothing Then Return result
        Dim state = previewVariant.State


        result.Warnings.AddRange(previewVariant.Warnings)
        result.SkeletonKey = _stateResolver.ResolveSkeletonKey(previewVariant.State, result.Warnings)

        Dim candidates = CollectMeshCandidates(previewVariant.State, result.Warnings, previewVariant.UseFaceGen, previewVariant.OnlyFaceCollect, previewVariant.OnlyOutfitCollect,
                                               previewVariant.RaceFilterBypassArmaFormID)

        ' Engine-faithful, per-RACE head-part occlusion: RACE.DATA declares which worn biped slot hides each
        ' head-part channel (face-cull A, hair B, facial-hair C). Resolve the NPC's race once (cached parse;
        ' the same record is read ~20x/render) and turn it into slot-30-relative masks via RaceUtil. These
        ' drive both SelectWinningCandidates (which head parts to occlude/zap) and the render-time worn-slot
        ' slice (result.HeadOcclusionMask, consumed by NpcRenderHost.ApplyRenderToggleVisibility). Nothing race
        ' -> all masks 0 -> nothing occludes (safe under-hide), matching the old const's zero behaviour.
        Dim raceData As Canon.IRace = Nothing
        If state.RaceFormID <> 0UI Then
            Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then raceData = _ctx.ParseRaceCanonCached(raceRec)
        End If
        Dim faceCullMask As UInteger = RaceUtil.RaceFaceCullMask(raceData)
        Dim hairMask As UInteger = RaceUtil.RaceHairMask(raceData)
        Dim facialHairMask As UInteger = RaceUtil.RaceFacialHairMask(raceData)
        ' A (face-cull, whole-node) y B (hair slot) crudos, para que el render (SSE) reconstruya la máscara
        ' per-partición engine-fiel desde los BOD2 de los ítems ACTUALMENTE renderizados (attach 0x140218200
        ' fase 2). result.HeadOcclusionMask se fija abajo, ya con los winners resueltos (máscara EFECTIVA).
        result.HeadFaceCullMask = faceCullMask
        result.HeadHairSlotMask = hairMask
        ' Slot que ESTA raza reserva para el Pipboy (RACE.DATA 'Pipboy Biped Object').
        ' Es dato POR RAZA, no la constante 60 — el render lo consume para el
        ' strip coexist-by-design. 0 en Skyrim (el campo no existe en ese layout).
        result.PipboySlotMask = RaceUtil.RacePipboyMask(raceData)

        ' Per-segment worn-slot occlusion (Fase 2): LoadNifShapes records each worn-item shape's OWN slots
        ' + group id (ShapeOwnSlots / ShapeSlotGroup); ApplyRenderToggleVisibility recomputes the occlusion
        ' mask from the currently-rendered subset (a render toggle hiding an item drops its slots).
        Dim wornItemMasks As List(Of UInteger) = Nothing
        Dim wornSlotMask As UInteger = 0UI
        Dim selectedCandidates = SelectWinningCandidates(candidates, faceCullMask, hairMask, facialHairMask, wornItemMasks, wornSlotMask)

        ' Máscara EFECTIVA de oclusión de head-parts para consumidores legacy de HeadOcclusionMask.
        ' SSE: bit del slot de pelo del worn mask (mecanismo a) + BOD2 del ARMA que lo ocupa (mecanismo b),
        ' más el face-cull A (whole-node). FO4: sin cambios (unión de los tres canales A/B/C).
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            result.HeadOcclusionMask = HeadPartHideMask(hairMask, wornSlotMask, wornItemMasks) Or faceCullMask
        Else
            result.HeadOcclusionMask = faceCullMask Or hairMask Or facialHairMask
        End If

        ' Diagnostic toggles "Render armor" / "Render only armor" se aplican vía RenderHide en
        ' el draw loop (sin re-resolver candidates). Cada shape se categoriza a la salida del
        ' resolver y los handlers de los CheckBoxes setean RenderHide según categoría + estado
        ' de los toggles. Ver ApplyRenderToggleVisibility.

        ' Sculpt source identification (rule):
        '   - Underarmor source = ARMA con slot 33 (BODY) AND HasSculptData. Si existe, su SCLP
        '     aplica a TODOS los over-armor shapes (excepto los con NoUnderarmorScaling=True).
        '   - Si no hay slot-33 source: cada [U] piece (slots 36-40) provee SCLP para SU [A]
        '     correspondiente (37→42 LArm, 38→43 RArm, 39→44 LLeg, 40→45 RLeg, 36→41 Torso).
        '     Mapping de bit: A_bit = U_bit + 5.
        '   - El underarmor NO se aplica a sí mismo (su mesh ni el body desnudo bajo él).
        Const SLOT_BIT_BODY As Integer = 3
        Const U_BIT_FIRST As Integer = 6   ' U Torso
        Const U_BIT_LAST As Integer = 10   ' U RLeg
        Const A_BIT_FIRST As Integer = 11  ' A Torso
        Const A_BIT_LAST As Integer = 15   ' A RLeg
        Dim BODY_MASK As UInteger = 1UI << SLOT_BIT_BODY
        Dim U_MASK As UInteger = 0UI
        For b = U_BIT_FIRST To U_BIT_LAST
            U_MASK = U_MASK Or (1UI << b)
        Next

        Dim globalSculptSource As MainForm.MeshCandidate = Nothing
        Dim uSculptSourceByBit As New Dictionary(Of Integer, MainForm.MeshCandidate)
        ' SCULPT (ARMA BSMS bone-scale) es un mecanismo EXCLUSIVO de FO4: Skyrim ARMA no tiene NINGÚN
        ' subrecord BSMP/BSMB/BSMS (verificado contra el esquema del record ARMA de Skyrim, que no
        ' los define) → ArmaBoneScaleDeltas
        ' siempre vacío en SSE. Gate EXPLÍCITO FO4-only para que los bits de slot FO4 de abajo nunca se
        ' ejerzan bajo Skyrim (defensivo; el bloque ya era no-op data-driven). FO4 sin cambios.
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then
            For Each c In selectedCandidates
                If c.ArmaBoneScaleDeltas Is Nothing OrElse c.ArmaBoneScaleDeltas.Count = 0 Then Continue For
                If (c.SlotMask And BODY_MASK) <> 0 Then
                    If globalSculptSource Is Nothing Then globalSculptSource = c
                End If
                For b = U_BIT_FIRST To U_BIT_LAST
                    If (c.SlotMask And (1UI << b)) <> 0 Then
                        If Not uSculptSourceByBit.ContainsKey(b) Then uSculptSourceByBit(b) = c
                    End If
                Next
            Next
        End If

        ' [SCULPT-DECISION] diag: which candidate (if any) was picked as the slot-33 global sculpt
        ' source and which [U]-specific sources exist. Pairs with the per-candidate decision log
        ' below to verify the underarmor→over-armor rule is gating correctly. Log-only.
        If Logger.Enabled Then
            Dim gs = globalSculptSource
            Dim gsLog As String = If(gs Is Nothing, "none",
                $"0x{gs.ArmorAddonFormID:X8} slot=0x{gs.SlotMask:X} deltas={gs.ArmaBoneScaleDeltas.Count}")
            Dim uLog As String = String.Join(",", uSculptSourceByBit.Select(Function(kv) $"U{kv.Key}=0x{kv.Value.ArmorAddonFormID:X8}"))
            Logger.LogLazy(Function() $"[SCULPT-DECISION] sources: global={gsLog} uSpecific=[{uLog}]")
        End If

        Dim loadedNifs As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)

        ' Compute the over-armor [A] slot mask = bits 11..15.
        Dim A_MASK As UInteger = 0UI
        For b = A_BIT_FIRST To A_BIT_LAST
            A_MASK = A_MASK Or (1UI << b)
        Next

        For Each candidate In selectedCandidates
            ' SCULPT applies ONLY to over-armor [A] consumers, never to the source itself nor
            ' to anything else. The engine's two-skeleton model:
            '   - Skel "base" (RACE BSMS only): underarmor source, body skin, hands, head parts.
            '   - Skel "sculpted" (RACE BSMS + SCLP amplifier): pure [A] over-armor pieces.
            ' A candidate is a pure [A] consumer iff it declares at least one [A] bit (11-15)
            ' AND declares neither BODY (bit 3) nor any [U] bit (6-10). Otherwise it is the
            ' source itself (e.g. Armor_GunnerGuard_UnderArmor with slot 0xC7F8 = bits 3+7+8+14+15).
            Dim sculptToApply As List(Of ARMA_BoneScaleDelta) = Nothing
            Dim sourceFormID As UInteger = 0

            Dim isPureOverArmor = (candidate.SlotMask And A_MASK) <> 0 AndAlso
                                  (candidate.SlotMask And BODY_MASK) = 0 AndAlso
                                  (candidate.SlotMask And U_MASK) = 0
            If isPureOverArmor Then
                ' Check NoUnderarmorScaling flag (opt-out from receiving scaling).
                Dim noUnderArmorFlag As Boolean = False
                If candidate.ArmorAddonFormID <> 0UI Then
                    ' NoUnderarmorScaling es un bit de cabecera del ARMA de Fallout 4 solamente.
                    Dim aa = TryCast(_ctx.GetParsedArma(candidate.ArmorAddonFormID), Canon.ArmaFO4)
                    If aa IsNot Nothing Then noUnderArmorFlag = aa.NoUnderarmorScaling
                End If

                If Not noUnderArmorFlag Then
                    ' Precedence: [U] specific FIRST. Only if no [U] equivalent exists, fall back
                    ' to slot 33 BODY global source. Use ONE source only (first [A] bit match).
                    For ab = A_BIT_FIRST To A_BIT_LAST
                        If (candidate.SlotMask And (1UI << ab)) <> 0 Then
                            Dim ub = ab - 5
                            Dim uSrc As MainForm.MeshCandidate = Nothing
                            If uSculptSourceByBit.TryGetValue(ub, uSrc) Then
                                sculptToApply = uSrc.ArmaBoneScaleDeltas
                                sourceFormID = uSrc.ArmorAddonFormID
                                Exit For
                            End If
                        End If
                    Next
                    ' If no [U]-specific source for any covered [A] slot, fall back to slot 33.
                    If sculptToApply Is Nothing AndAlso globalSculptSource IsNot Nothing Then
                        sculptToApply = globalSculptSource.ArmaBoneScaleDeltas
                        sourceFormID = globalSculptSource.ArmorAddonFormID
                    End If
                End If
            End If
            ' Else: candidate is the underarmor source itself (BODY/[U] declared) or unrelated
            ' to the [U]→[A] system (hands, head, accessories) → renders on the base skeleton,
            ' never sculpted.

            ' [SCULPT-DECISION] per-candidate: shows slot, whether it qualified as pure over-armor,
            ' its own header flags (HasSculpt / NoUnderarmorScaling — the opt-out gate) and the final
            ' decision (how many sculpt deltas applied + from which source). Lets us verify whether
            ' the leg/torso [A] pieces SHOULD be taking the slot-33 underarmor sculpt at all. Log-only.
            If Logger.Enabled Then
                Dim candFidL = candidate.ArmorAddonFormID
                Dim slotL = candidate.SlotMask
                Dim isPOL = isPureOverArmor
                Dim ownDeltasL = If(candidate.ArmaBoneScaleDeltas Is Nothing, 0, candidate.ArmaBoneScaleDeltas.Count)
                Dim aaL = If(candFidL <> 0UI, TryCast(_ctx.GetParsedArma(candFidL), Canon.ArmaFO4), Nothing)
                Dim noUaL = aaL IsNot Nothing AndAlso aaL.NoUnderarmorScaling
                Dim hasSculptL = aaL IsNot Nothing AndAlso aaL.HasSculptData
                Dim srcL = sourceFormID
                Dim appliedL = If(sculptToApply Is Nothing, 0, sculptToApply.Count)
                Logger.LogLazy(Function() $"[SCULPT-DECISION] cand=0x{candFidL:X8} slot=0x{slotL:X} pureOverArmor={isPOL} ownSculptDeltas={ownDeltasL} hdr(HasSculpt={hasSculptL},NoUnderarmorScaling={noUaL}) -> sculptApplied={appliedL} from=0x{srcL:X8}")
            End If

            LoadNifShapes(candidate, previewVariant.State, loadedNifs, result, sculptToApply, sourceFormID)
        Next

        ' Mount-resolve pass for robot chunks: ahora que los NIFs están cargados, leer
        ' BSConnectPoint::Children del NIF de cada chunk (lista de point names "C-X" que el
        ' chunk declara) y matchear contra los sockets del skeleton (Name "P-X"). El match
        ' es la fuente canónica engine — el OMOD.AttachPoint KYWD del record es solo
        ' metadata del CK para validar compatibilidad chunk↔slot, no la fuente del mounting.
        _mountingResolver.ResolveRobotChunkMounts(selectedCandidates, loadedNifs, previewVariant.State, result.Warnings)

        ' NOTA: Pipboy synthetic-skin pass se ejecuta DESPUÉS de PrepareSkeleton (no acá), porque
        ' necesita el SkeletonInstance del actor para descubrir el bone target via lookup
        ' case-insensitive contra el dictionary (evita hardcodear "PipboyBone" — distintas razas
        ' pueden tener otra convención de nombre). Ver llamada post-PrepareSkeleton más abajo.

        ' Map shape → (MountSocket, chunkNif) para los robot chunks resueltos. Consumido por
        ' PrepareSkeleton para inyectar bones internos del chunk al SkeletonInstance del actor
        ' anchored al socket bone (BSConnectPointBoneInjector_Class). Solo se popula para
        ' chunks robot con MountSocket asignado — humanoides quedan ausentes y el inject
        ' es no-op para ellos (skinning normal del actor ya los posiciona).
        For Each cand In selectedCandidates
            If cand.MountSocket Is Nothing Then Continue For
            ' Use candidate-specific NIF (populated per-candidate by LoadNifShapes), not the
            ' DictKey lookup. Multi-instance candidates that share DictKey have DIFFERENT
            ' NIF instances — referencia identity matches only this candidate's shapes.
            Dim chunkNif As Nifcontent_Class_Manolo = Nothing
            If Not result.CandidateNif.TryGetValue(cand, chunkNif) Then
                If Logger.Enabled Then
                    Dim cfid = cand.SourceFormID
                    Logger.LogLazy(Function() $"[MOUNT-MAP] cand=0x{cfid:X8} NO CandidateNif entry — skipping")
                End If
                Continue For
            End If
            Dim matched As Integer = 0
            For Each shape In result.Shapes
                If shape.NifContent IsNot chunkNif Then Continue For
                result.ShapeMountSocket(shape) = cand.MountSocket
                result.ShapeChunkNif(shape) = chunkNif
                matched += 1
            Next
            If Logger.Enabled Then
                Dim cFidLog = cand.SourceFormID
                Dim socketNameLog = cand.MountSocket.Name
                Dim nifHashLog2 = chunkNif.GetHashCode()
                Dim matchedLog = matched
                Logger.LogLazy(Function() $"[MOUNT-MAP] cand=0x{cFidLog:X8} socket='{socketNameLog}' nifHash={nifHashLog2} matchedShapes={matchedLog}")
            End If
        Next
        If Logger.Enabled Then
            Dim shapeCountLog = result.Shapes.Count
            Dim mountCountLog = result.ShapeMountSocket.Count
            Logger.LogLazy(Function() $"[MOUNT-MAP] DONE result.Shapes.Count={shapeCountLog} result.ShapeMountSocket.Count={mountCountLog}")
        End If


        NpcManagerFormat.DeduplicateWarnings(result.Warnings)
        Return result
    End Function

    Private Function CollectMeshCandidates(state As MainForm.NPCVisualState, warnings As List(Of String), Optional useFaceGen As Boolean = False, Optional onlyFaceCollect As Boolean = False, Optional onlyOutfitCollect As Boolean = False,
                                           Optional raceFilterBypassArmaFormID As UInteger = 0UI) As List(Of MainForm.MeshCandidate)
        Dim candidates As New List(Of MainForm.MeshCandidate)
        Dim order As Integer = 0

        ' Collect scope (Full / OnlyFace / OnlyOutfit):
        '   • Skin (body) — Full only; OnlyFace and OnlyOutfit both drop it.
        '   • Outfit      — Full + OnlyOutfit (the picker's single-piece preview uses a 1-item draft);
        '                    OnlyFace drops it.
        '   • HeadParts + robot chunks — Full + OnlyFace; OnlyOutfit drops them.
        ' OnlyFaceCollect: editor host / MainForm "Only Face" ComboBox. OnlyOutfitCollect: the Edit Outfit
        ' picker's "selected piece only". Both funnel here via MainForm.PreviewVariantDefinition — no parallel paths.
        If Not onlyFaceCollect AndAlso Not onlyOutfitCollect AndAlso state.SkinFormID <> 0UI Then
            CollectArmoCandidates(state.SkinFormID, state, MainForm.MeshCandidateKind.Skin, candidates, order, warnings, raceFilterBypassArmaFormID)
        End If

        If Not onlyFaceCollect Then
            ' Use pre-resolved LoadoutArmorFormIDs (already expanded from LVLI).
            ' These are the final ARMO FormIDs for this specific variant.
            If state.LoadoutArmorFormIDs.Count > 0 Then
                For Each armoFormID In state.LoadoutArmorFormIDs
                    CollectArmoCandidates(armoFormID, state, MainForm.MeshCandidateKind.Outfit, candidates, order, warnings, raceFilterBypassArmaFormID)
                Next
            ElseIf state.DefaultOutfitFormID <> 0UI Then
                ' Fallback: read OTFT directly (for NPCs without leveled expansion)
                Dim outfitRec = _ctx.PluginManager.GetRecord(state.DefaultOutfitFormID)
                If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then
                    warnings.Add($"Default outfit {state.DefaultOutfitFormID:X8} is missing or not OTFT")
                Else
                    Dim outfit = Canon.CanonRecords.Otft(outfitRec, _ctx.PluginManager)
                    For Each itemFormID In outfit.Prendas()
                        CollectArmoCandidates(itemFormID, state, MainForm.MeshCandidateKind.Outfit, candidates, order, warnings, raceFilterBypassArmaFormID)
                    Next
                End If
            End If
        End If

        ' HeadParts: Full + OnlyFace; OnlyOutfit (single-piece preview) drops them.
        If Not onlyOutfitCollect Then
            Dim mergedHeadParts = MergeHeadPartsWithRaceDefaults(state)
            CollectHeadPartCandidates(mergedHeadParts, New HashSet(Of UInteger)(), candidates, order, warnings, state, useFaceGen)
        End If

        ' Camino robot (NPC_.ObjectTemplate). Regla del motor: ObjectTemplateResolver elige UNA combinacion
        ' (kw-match -> primer Default -> primera); cada IncludedOmod con ModelPath es un chunk a montar por
        ' BSConnectPoint::Parents del skeleton del actor; los OMOD sin ModelPath pero con Properties alimentan
        ' OmodResolutionApplier con formType="NPC_". El AttachPoint se resuelve OMOD.DataAttachPoint -> KYWD
        ' -> EditorID, matcheado case-insensitive contra ConnectPointInfo.Name. Ver 24-robots-mounting.
        If Not onlyOutfitCollect AndAlso state.HasObjectTemplate AndAlso state.ObjectTemplateCombinations IsNot Nothing _
           AndAlso state.ObjectTemplateCombinations.Count > 0 Then
            CollectRobotChunkCandidates(state, candidates, order, warnings)
        End If

        Return candidates
    End Function

    ''' <summary>Thin instance wrapper over the shared <see cref="HeadPartResolver.MergeHeadPartsWithRaceDefaults"/>;
    ''' threads <see cref="_ctx.PluginManager"/> through and unpacks the render-side state into the
    ''' helper's primitive parameter list. Real implementation + logging lives in the helper module.</summary>
    Friend Function MergeHeadPartsWithRaceDefaults(state As MainForm.NPCVisualState) As List(Of UInteger)
        If state Is Nothing Then Return New List(Of UInteger)
        Return HeadPartResolver.MergeHeadPartsWithRaceDefaults(state.RaceFormID, state.IsFemale, state.HeadPartFormIDs, _ctx.PluginManager,
                                                               AddressOf _ctx.ParseRaceCanonCached, AddressOf _ctx.ParseHdptCached)
    End Function

    ''' <param name="raceFilterBypassArmaFormID">Preview-only: the ONE ARMA (the ARMA editor's "Only Model"
    ''' subject) that is collected even when the engine's per-ARMA race match rejects it. 0 = engine rule for
    ''' every ARMA. See <see cref="NpcRenderHost.RaceFilterBypassArmaFormID"/>.</param>
    Friend Sub CollectArmoCandidates(armoFormID As UInteger,
                                      state As MainForm.NPCVisualState,
                                      kind As MainForm.MeshCandidateKind,
                                      candidates As List(Of MainForm.MeshCandidate),
                                      ByRef order As Integer,
                                      warnings As List(Of String),
                                      Optional raceFilterBypassArmaFormID As UInteger = 0UI)
        ' EFECTIVA: la pregunta es que MALLAS va a emitir el motor. Con `TNAM` la lista de armatures
        ' sale del TERMINAL (SkyrimSE 0x14027E540 / Fallout4 0x140462410) y los `MODL` del hijo son
        ' letra muerta. Medido: 2 records del orden de carga cambian lo que se dibuja, en las DOS
        ' direcciones, y alcanzables solo en las razas que declaran esas ARMA.
        Dim armo = _ctx.GetParsedArmoEfectivo(armoFormID)
        If armo Is Nothing Then Return

        ' Head-occlusion slots THIS NPC's race actually declares (RACE.DATA A/B/C via RaceUtil), so an
        ' ARMO that occludes a head-part on a NON-standard biped slot (modded races — e.g. hair on 41)
        ' still contributes it to the head-part occupancy footprint below. Body/hand slots are excluded so
        ' this can NEVER re-mark body skin as covered (the exact regression the static HEADWEAR gate at
        ' .SlotMask below was added to prevent — "broke hands"). For vanilla FO4/SSE races
        ' RaceHeadOcclusionMask is a subset of HeadwearMaskForGame(), so this union is a byte-for-byte
        ' no-op (verified: vanilla FO4 A/B/C use only slots {30,31,32,48}). Engine parity: Fallout4.exe
        ' 0x14051F210 ORs the ARMO's full BOD2 and 0x140506140 tests it against the race's A/B/C fields —
        ' this restores that data-driven behaviour without the full-union skin-coverage regression.
        Dim raceHeadOcclSlots As UInteger = 0UI
        If state.RaceFormID <> 0UI Then
            Dim hdRaceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
            If hdRaceRec IsNot Nothing AndAlso hdRaceRec.Header.Signature = "RACE" Then
                raceHeadOcclSlots = RaceUtil.RaceHeadOcclusionMask(_ctx.ParseRaceCanonCached(hdRaceRec))
            End If
        End If
        Dim skinSlots As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body) Or BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)
        Dim headOcclGate As UInteger = BipedSlots.HeadwearMaskForGame() Or (raceHeadOcclSlots And (Not skinSlots))

        Dim useFaceGen As Boolean = RaceBuildsFaceGenHead(state)

        ' Power-armor gate: an ArmorTypePower piece only fits an actor whose race is a power-armor race
        ' (in a frame). Drop the whole ARMO otherwise — PA armatures list HumanRace too, so the per-ARMA
        ' race check would render it on humans mounted wrong (see helper block above).
        If _armoIsPowerArmor(armoFormID) AndAlso Not _raceIsPowerArmor(state.RaceFormID) Then
            Logger.LogLazy(Function() $"[PA-GATE] dropped ARMO=0x{armoFormID:X8} (ArmorTypePower) — race=0x{state.RaceFormID:X8} is not a power-armor race")
            Return
        End If
        ' NO early-out on ARMO.RaceFormID: vanilla convention is each ARMA declares its own
        ' race compatibility via RNAM + AdditionalRaces (MODL entries). An ARMO with
        ' RNAM=HumanRace is commonly worn by Ghouls/Synths if the sub-ARMAs list those as
        ' AdditionalRaces. The per-ARMA check (EquipResolver.ArmaMatchesRace) handles this correctly.
        ' Log the ARMO race only for visibility; don't reject based on it.

        ' Multi-addon: los ARMO con varios Models (ej. Combat Torso Lite/Mid/Heavy) eligen UN addon por la
        ' cadena LVLI.LLKC -> keyword de combinacion OBTS -> OMOD Property AddonIndex, con fallback a
        ' BaseAddonIndex (FNAM) o al indice 0. El INDX del array Models NO es indice unico: es etiqueta de
        ' "grupo de addons que se cargan juntos", asi que el motor resuelve UN AddonIndex efectivo y carga
        ' TODOS los Models con ese INDX (Sturgess: idx 0 = clothes + gloves, los dos).
        ' ctxKeywords se saca del bloque de resolucion para que el resolver OBTS/OMOD de abajo use el MISMO
        ' set. Vacio si al ARMO no se llego por un outfit leveled (p.ej. la piel del WNAM): ahi las
        ' combinaciones Default=True siguen aplicando y las que dependen de keywords no.
        Dim ctxKeywords As List(Of UInteger) = Nothing
        state.LoadoutArmorContextKeywords?.TryGetValue(armoFormID, ctxKeywords)

        ' Resolve OBTS/OMOD canonical view ONCE per ARMO. Shared by every MainForm.MeshCandidate
        ' produced for this ARMO's addons — they all live under the same combination overlay.
        ' The applier runs in ApplyShapeMaterialOverrides after the ARMA-direct base swap.
        ' OBTS sólo existe en Fallout 4: Nothing en Skyrim resuelve a un CombinationResolution vacío,
        ' mismo comportamiento que antes cuando el SSE ARMO_Data.Combinations venía siempre vacío.
        Dim omodResolution = ObjectTemplateResolver.ResolveArmoCombinations(TryCast(armo, Canon.ArmoFO4), ctxKeywords, _ctx.PluginManager)

        ' [FASE 3] Chunk-mount path biped: OMODs con AttachPoint != 0 AND ModelPath != "" se
        ' montan vía BSConnectPoint igual que robot chunks. Delegate al shared con
        ' formType="ARMO". Para ARMOs sin chunk-mount OMODs (solo property modifiers tipo
        ' ap_armor_Lining/Tier/Size), el shared early-returns sin emitir candidates.
        ' Capa V2 ahora SÍ aplica a biped: el gate Fase 2.5 fue removido. Toda shape con
        ' MountSocket recibe el mount vía RE-BIND de su skin (huesos del esqueleto intactos).
        CollectOmodChunkCandidates(omodResolution, "ARMO", state, candidates, order, warnings)

        Dim addonOrder As List(Of UInteger)
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ' Skyrim ARMO Armature = RArray PLANO de MODL (ARMA FormIDs), SIN INDX/Addon-Index
            ' (verificado: SkinNaked 0x00000D64 = 25×MODL, 0×INDX). TODOS los armatures aplican
            ' (torso/hands/feet + variantes por raza/bestia/niño); el filtro de raza (raceOk) de abajo
            ' elige los que matchean al NPC. El INDX-variant grouping de FO4 NO existe en Skyrim, y
            ' aplicarlo tomaba sólo el armature en posición 0 (el parser da índice posicional sin INDX)
            ' → una skin multi-armature perdía body/hands/feet = "sin manos"/"sin cuerpo".
            addonOrder = Canon.CanonInterpretacion.LeerComplementos(armo).Select(Function(a) a.ArmaFormID).ToList()
        Else
            ' Models/BaseAddonIndex/Combinations sólo existen en el ARMO de Fallout 4 — la rama
            ' Skyrim ya volvió arriba, así que acá "armo" siempre resuelve a ArmoFO4.
            Dim armoFo4 = TryCast(armo, Canon.ArmoFO4)
            ' ⛔ Por la primitiva unica, no por `armoFo4.Models` directo: era el TERCER carril de
            ' lectura del layout -los otros dos, `ComplementosDe` y el del editor, ya se unificaron- y
            ' un layout leido en tres lados es la duplicacion que esta tanda vino a matar.
            ' `ARMO_AddonEntry` ya lleva `AddonIndex`, asi que cambia la FUENTE de las entradas y NO
            ' las reglas: el INDX efectivo por OBTS, el fallback a `BaseAddonIndex`, el take-por-grupo
            ' y el fallback defensivo del INDX minimo quedan exactamente igual.
            Dim fo4Models = If(armoFo4 IsNot Nothing,
                               Canon.CanonInterpretacion.LeerComplementos(armoFo4),
                               New List(Of ARMO_AddonEntry))
            If fo4Models.Count >= 1 Then
                ' Resolve effective AddonIndex. ResolveEffectiveAddonIndex ahora devuelve Integer? —
                ' HasValue=True cuando hay OMOD override keyword-driven; sino Nothing → usar
                ' BaseAddonIndex (FNAM) si está, sino 0 (vanilla default).
                Dim resolved = ResolveEffectiveAddonIndex(armoFo4, ctxKeywords)
                Dim effectiveIdx As Integer
                If resolved.HasValue Then
                    effectiveIdx = resolved.Value
                ElseIf armoFo4.BaseAddonIndex <> &HFFFFUS Then
                    effectiveIdx = armoFo4.BaseAddonIndex
                Else
                    effectiveIdx = 0
                End If

                ' Take ALL models whose INDX matches the effective AddonIndex (group, not single).
                addonOrder = New List(Of UInteger)
                For Each entry In fo4Models
                    If CInt(entry.AddonIndex) = effectiveIdx Then
                        addonOrder.Add(entry.ArmaFormID)
                    End If
                Next
                ' Defensive fallback: si el INDX resuelto no existe en los Models (datos malformados
                ' o keyword-driven INDX que apunta a un grupo no presente), usar todas las entries
                ' con el menor INDX disponible — no crashear ni dejar el outfit vacío.
                If addonOrder.Count = 0 Then
                    Dim minIdx As Integer = fo4Models.Min(Function(e) CInt(e.AddonIndex))
                    For Each entry In fo4Models
                        If CInt(entry.AddonIndex) = minIdx Then addonOrder.Add(entry.ArmaFormID)
                    Next
                End If
            Else
                addonOrder = Canon.CanonInterpretacion.LeerComplementos(armo).Select(Function(a) a.ArmaFormID).ToList()
            End If
        End If

        ' Within-ARMO armature slot occupancy (engine "first addon claims the slot" rule, see the
        ' coveredSlots check before candidates.Add below). Accumulates the biped slots already taken
        ' by earlier race-matching armature entries of THIS ARMO.
        Dim coveredSlots As UInteger = 0UI
        ' LEY ÚNICA: raza y footprint por armature salen de EquipResolver (FO4_Base_Library), acotado al
        ' grupo de Models que el AddonIndex efectivo seleccionó. Este bucle ya no decide slots: sólo resuelve
        ' lo suyo (malla, facebones, material swap, bone scale) para los armatures que la ley deja pasar.
        Dim armoFp = EquipResolver.BuildFootprint(armoFormID, _ctx.EquipCtx(state.RaceFormID, state.IsFemale), addonOrder)
        Dim addonFp As New Dictionary(Of UInteger, EquipResolver.ArmaFootprint)
        For Each af In armoFp.Addons
            addonFp(af.ArmaFormID) = af
        Next
        For Each armaFormID In addonOrder
            Dim arma = _ctx.GetParsedArma(armaFormID)
            If arma Is Nothing Then Continue For
            Dim fpArma As EquipResolver.ArmaFootprint = Nothing
            If Not addonFp.TryGetValue(armaFormID, fpArma) Then Continue For
            ' raceOk drives the skip below (app logic, always computed). The block under
            ' If Logger.Enabled is PURELY diagnostic — it dumps every ARMA at the effective addon
            ' index (even race-skipped ones) with its model flags (MO2F/MO3F/MO4F/MO5F) + all four
            ' model paths, so the bombín "human + robot" duplicate can be read off the log: which
            ' addons sit at this index, which races they accept, and whether a second ARMA is
            ' pulling in a 1st-person / facebones / robot-variant model.
            ' Engine rule, both games (RNAM + AdditionalRaces + the RACE.RNAM Armor-Race chain) — EXCEPT for the
            ' one ARMA the ARMA editor is previewing in "Only Model" scope, which is shown regardless of race so
            ' the user can see the mesh they're editing (see NpcRenderHost.RaceFilterBypassArmaFormID). No render
            ' path outside that editor scope passes a nonzero bypass.
            Dim raceOk As Boolean = fpArma.RaceOk _
                                    OrElse (raceFilterBypassArmaFormID <> 0UI AndAlso armaFormID = raceFilterBypassArmaFormID)
            If Logger.Enabled Then
                Dim a = arma
                Dim afid = armaFormID
                Dim armoFid = armoFormID
                Dim rOkL = raceOk
                Dim aFo4Log = TryCast(a, Canon.ArmaFO4)
                Dim maleFlagsLog As Byte = If(aFo4Log IsNot Nothing, aFo4Log.MaleFlags, CByte(0))
                Dim femaleFlagsLog As Byte = If(aFo4Log IsNot Nothing, aFo4Log.FemaleFlags, CByte(0))
                Dim maleFlags2Log As Byte = If(aFo4Log IsNot Nothing, aFo4Log.MaleFlags2, CByte(0))
                Dim femaleFlags2Log As Byte = If(aFo4Log IsNot Nothing, aFo4Log.FemaleFlags2, CByte(0))
                Dim maleSwapLog As UInteger = If(aFo4Log IsNot Nothing, aFo4Log.MaleMaterialSwap, 0UI)
                Dim femaleSwapLog As UInteger = If(aFo4Log IsNot Nothing, aFo4Log.FemaleMaterialSwap, 0UI)
                Dim maleRemapLog As Single? = If(aFo4Log IsNot Nothing AndAlso aFo4Log.MaleColorRemappingIndexPresente,
                                                 CType(aFo4Log.MaleColorRemappingIndex, Single?), Nothing)
                Logger.LogLazy(Function() $"[ARMA-MODELFLAGS] ARMO=0x{armoFid:X8} ARMA=0x{afid:X8} '{a.EditorID}' " &
                    $"race=0x{a.Race:X8} addRaces=[{String.Join(",", a.AdditionalRaces.Select(Function(x) x.Race.ToString("X8")))}] raceOk={rOkL} slot=0x{a.SlotMaskDe():X8} | " &
                    $"MO2F=0x{maleFlagsLog:X2}({NpcManagerFormat.DescribeModelFlags(maleFlagsLog)}) MO3F=0x{femaleFlagsLog:X2}({NpcManagerFormat.DescribeModelFlags(femaleFlagsLog)}) " &
                    $"MO4F=0x{maleFlags2Log:X2} MO5F=0x{femaleFlags2Log:X2} | " &
                    $"MO2S(matswap)=0x{maleSwapLog:X8} MO3S=0x{femaleSwapLog:X8} MO2C(remap)={If(maleRemapLog.HasValue, maleRemapLog.Value.ToString("F3"), "none")} | " &
                    $"MOD2='{a.MaleModelFilename}' MOD3='{a.FemaleModelFilename}' MOD4='{a.MaleModelFilename2}' MOD5='{a.FemaleModelFilename2}'")
            End If
            If Not raceOk Then
                Continue For
            End If

            ' Pick the gender-matching bone scale block (if any) and log + stash it on the
            ' candidate. Engine-side these per-bone Vec3 deltas are added on top of RACE.BSMS
            ' to shape the outfit around the body (cinched waist, wider hips, vest volume).
            Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
            Dim genderBoneScale As List(Of ARMA_BoneScaleDelta) = Nothing
            ' El sculpt (BSMP/BSMB/BSMS) sólo existe en el ARMA de Fallout 4.
            Dim armaFo4ForSculpt = TryCast(arma, Canon.ArmaFO4)
            If armaFo4ForSculpt IsNot Nothing Then
                For Each bsg In ArmaEditor_Form.ReadAllBoneScaleFromRecord(armaFo4ForSculpt)
                    If bsg.Gender <> targetGender Then Continue For
                    If bsg.Bones.Count = 0 Then Continue For
                    genderBoneScale = bsg.Bones
                    For Each bd In bsg.Bones
                        Dim mag = Math.Sqrt(bd.DeltaX * bd.DeltaX + bd.DeltaY * bd.DeltaY + bd.DeltaZ * bd.DeltaZ)
                    Next
                    Exit For
                Next
            End If

            ' Resolve mesh path with ARMA-first / ARMO-WorldModel-fallback semantics.
            ' ARMO.MOD2 (male) / MOD4 (female) populate when the mesh is authored at ARMO level
            ' (robots: Assaultron skin has ARMO.MOD2=Assaultron.nif with empty ARMA.MOD2/MOD3).
            ' Humanoid armors inverse: ARMA has the mesh, ARMO.MOD2/MOD4 usually empty. Gender mirror
            ' inside each source: try same-gender first, then opposite.
            ' ⛔ La decisión vive en `EquipResolver.ResolverMalla`, UNA sola vez. Estaba escrita acá y
            ' otra vez en `BuildFootprint` con un criterio distinto —allá sólo miraba la ARMA—, así que
            ' el gate de oclusión del bake y el render discrepaban justo en el patrón de los robots.
            Dim meshPath = EquipResolver.ResolverMalla(arma, armo, state.IsFemale).Ruta
            If meshPath = "" Then
                Continue For
            End If

            Dim armaDictKey As String = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(meshPath)
            ' "Has FaceBones Model" (MO2F/MO3F bit 0x01): the engine swaps this model for its
            ' <model>_faceBones.nif sibling (identical geometry, skinned to the face bones) on FaceGen
            ' NPCs so it deforms with the head's FMRS bone pose and covers the hair. Mirror of the HDPT
            ' face-region redirect (~line 10489). Fallback: TryGetFaceBonesVariant returns "" when the
            ' sibling is absent from FilesDictionary, so we keep the base mesh. Render/preview only; the
            ' bake is untouched.
            ' Head-bake: la ARMA con "Has FaceBones Model" (MO2F/MO3F bit 0x01) se DIBUJA plana y su
            ' `_faceBones` viaja como INSUMO (HeadBakeService). Igual que las head parts — regla única, sin
            ' excepción para ARMA (194 NIFs, no marginal). Sólo FO4: en SSE TryGetFaceBonesVariant da "".
            Dim armaFaceBonesInputKey As String = ""
            If useFaceGen AndAlso NPC_Config.IsHeadBakeActive() Then
                ' MO2F/MO3F (model flags) sólo existen en el ARMA de Fallout 4.
                Dim armaFo4ForFlags = TryCast(arma, Canon.ArmaFO4)
                Dim modelFlags As Byte = If(armaFo4ForFlags IsNot Nothing,
                    If(state.IsFemale, armaFo4ForFlags.FemaleFlags, armaFo4ForFlags.MaleFlags), CByte(0))
                If (modelFlags And &H1) <> 0 Then
                    Dim fbKey = MeshPathHelpers.TryGetFaceBonesVariant(armaDictKey)
                    If fbKey <> "" Then
                        armaFaceBonesInputKey = fbKey
                        If Logger.Enabled Then
                            Dim afidLog = armaFormID, fbLog = fbKey
                            Logger.LogLazy(Function() $"[ARMA-FACEBONES] ARMA=0x{afidLog:X8} input (head-bake) dictKey='{fbLog}'")
                        End If
                    End If
                End If
            End If

            Dim effSlotMask As UInteger = fpArma.GeometryMask

            ' Within-ARMO armature dedup. The engine processes the armature in Models order; the FIRST
            ' race-matching addon to claim a biped slot owns it, and a later addon overlapping an
            ' already-claimed slot is dropped. This is what selects the human variant over the Mr Handy
            ' variant of a hat that lists BOTH races at the same INDX (AAClothesMobsterHat #0 race={Human}
            ' + AAHandyMobsterHat #1 race={Human,Handy}, both INDX 0, both slot 30): on a human #0 claims
            ' slot 30 → #1 overlaps → dropped; on a Mr Handy #0 fails the race check → #1 claims it.
            ' Per-SLOT, so complementary same-index addons (Sturgess clothes BODY + gloves Hands, different
            ' slots) BOTH still load. Distinct from SelectWinningCandidates' cross-outfit last-equipped-wins
            ' (that's between DIFFERENT equipped ARMOs). Slotless addons (effSlotMask=0) are never dropped
            ' here — they occupy no biped slot.
            If effSlotMask <> 0UI AndAlso (effSlotMask And coveredSlots) <> 0UI Then
                Dim aEdid = If(arma.EditorID, "")
                Dim afid2 = armaFormID
                Dim armoFid2 = armoFormID
                Dim slotL = effSlotMask
                Logger.LogLazy(Function() $"[ARMA-ARMATURE-DEDUP] ARMO=0x{armoFid2:X8} dropped ARMA=0x{afid2:X8} '{aEdid}' slot=0x{slotL:X8} — biped slot already claimed by an earlier race-matching armature entry of this ARMO")
                Continue For
            End If
            coveredSlots = coveredSlots Or effSlotMask

            ' Occupancy footprint = per-ARMA mask PLUS the owning ARMO's HEAD-region bits. The engine builds
            ' head-part occlusion (Fallout4.exe 0x14051F210 → 0x140506140) and the equip mutex from the
            ' equipped ARMO's BOD2, so a helmet whose ARMO declares slot 31 (Hair Long) — or 32/46/48 — that
            ' its ARMA mesh doesn't render must still occlude those head-parts. GATED to HEADWEAR_MASK: only
            ' head/face/neck bits are added; body/hand/[A]/[U] bits the ARMO might declare are NOT (that gating
            ' is what keeps this from over-marking body skin as covered — the earlier full-union broke hands).
            ' The within-ARMO armature dedup above intentionally stays on the per-ARMA effSlotMask.
            ' MO2S/MO3S (material swap) y MO2C/MO3C (color remap) sólo existen en el ARMA de Fallout 4.
            Dim armaFo4ForSwap = TryCast(arma, Canon.ArmaFO4)
            Dim maleSwapC As UInteger = If(armaFo4ForSwap IsNot Nothing, armaFo4ForSwap.MaleMaterialSwap, 0UI)
            Dim femaleSwapC As UInteger = If(armaFo4ForSwap IsNot Nothing, armaFo4ForSwap.FemaleMaterialSwap, 0UI)
            Dim maleRemapC As Single? = If(armaFo4ForSwap IsNot Nothing AndAlso armaFo4ForSwap.MaleColorRemappingIndexPresente,
                                           CType(armaFo4ForSwap.MaleColorRemappingIndex, Single?), Nothing)
            Dim femaleRemapC As Single? = If(armaFo4ForSwap IsNot Nothing AndAlso armaFo4ForSwap.FemaleColorRemappingIndexPresente,
                                             CType(armaFo4ForSwap.FemaleColorRemappingIndex, Single?), Nothing)
            Dim armoSlotDe = armo.SlotMaskDe()
            candidates.Add(New MainForm.MeshCandidate With {
                .DictKey = armaDictKey,
                .FaceBonesDictKey = armaFaceBonesInputKey,
                .SlotMask = effSlotMask Or (armoSlotDe And headOcclGate),
                .ArmaOwnSlotMask = effSlotMask,
                .ArmoOwnSlotMask = armoSlotDe,
                .Priority = If(state.IsFemale, arma.DataFemalePriority, arma.DataMalePriority),
                .Kind = kind,
                .SourceFormID = armoFormID,
                .ArmorAddonFormID = armaFormID,
                .TextureSetFormID = If(state.IsFemale,
                                       If(arma.FemaleSkinTexture <> 0UI, arma.FemaleSkinTexture, arma.MaleSkinTexture),
                                       If(arma.MaleSkinTexture <> 0UI, arma.MaleSkinTexture, arma.FemaleSkinTexture)),
                .MaterialSwapFormID = If(state.IsFemale,
                                          If(femaleSwapC <> 0UI, femaleSwapC, maleSwapC),
                                          If(maleSwapC <> 0UI, maleSwapC, femaleSwapC)),
                .ColorRemapIndex = If(state.IsFemale,
                                       If(femaleRemapC.HasValue, femaleRemapC, maleRemapC),
                                       If(maleRemapC.HasValue, maleRemapC, femaleRemapC)),
                .OmodResolution = omodResolution,
                .Order = order,
                .ArmaBoneScaleDeltas = genderBoneScale
            })

            ' [OUTFIT-RESOLVE] dump por cada candidate emitido. Tag PIPBOY-CANDIDATE cuando el
            ' SlotMask contiene bit 30 (slot 60 - Pipboy). Permite ver
            ' qué ARMA produce el mesh del Pipboy, qué path se resuelve, qué slot mask trae, y
            ' poder cotejar contra el NIF (skinned? BSConnectPoint::Parents declarado?).
            Dim slotHex = effSlotMask.ToString("X8")
            Dim armoEdid = If(armo.EditorID, "")
            Dim armaEdid = If(arma.EditorID, "")
            Dim isPipboyBit As Boolean = (effSlotMask And BipedSlots.SlotBitPipboy) <> 0UI
            Dim tag = If(isPipboyBit, "[OUTFIT-RESOLVE PIPBOY-CANDIDATE]", "[OUTFIT-RESOLVE]")
            Dim meshPathL = meshPath
            Dim orderL = order
            Dim kindL = kind
            Logger.LogLazy(Function() $"{tag} kind={kindL} order={orderL} ARMO=0x{armoFormID:X8} '{armoEdid}' ARMA=0x{armaFormID:X8} '{armaEdid}' slot=0x{slotHex} mesh='{meshPathL}'")

            order += 1
        Next
    End Sub

    ''' <summary>Qué emite REALMENTE un ARMO sobre este NPC, contestado por el MISMO código que arma el
    ''' render: se corre <see cref="CollectArmoCandidates"/> sobre listas frescas y se mira qué salió.
    ''' <para>⛔ Son DOS preguntas distintas, y el selector de atuendos contestaba las dos con una sola
    ''' respuesta —«¿tiene armature de esta raza?»— que no es ninguna de las dos:</para>
    ''' <list type="bullet">
    ''' <item><b>Dibuja</b> = emitió ALGÚN candidate. Incluye el carril de chunk-mount de OMOD, que sale
    ''' ANTES de mirar los armatures y se dibuja por la pasada slotless de la resolución de slots. Un ARMO
    ''' que sólo monta por socket se ve perfecto y no tiene un solo armature: por eso
    ''' <c>ArmoFootprint.DibujaAlgunArmature</c> —que sólo mira armatures— es un SUBCONJUNTO de esto y
    ''' sirve para AFIRMAR, nunca para DESCARTAR.</item>
    ''' <item><b>Compite</b> = emitió algún candidate no-Skin con <c>SlotMask &lt;&gt; 0</c>. Es LITERALMENTE
    ''' el filtro con el que el render arma su torneo (<c>slottedCandidates</c>, en
    ''' <c>ApplyEquipSlotResolution</c>): un ARMO que no aporta ninguno no genera grupo, no genera
    ''' <c>EquipItem</c> y NO compite — no ocupa slot y no elimina a nadie. Los chunk-mounts salen con
    ''' <c>SlotMask = 0</c>, así que DIBUJAN SIN PELEAR EL SLOT.</item>
    ''' </list>
    ''' <para>Vive acá, al lado de la ley, y no en el formulario que la necesita: reconstruirla afuera sería
    ''' otra copia del colector, que es lo que <c>ResolverMalla</c> vino a terminar. Es de sólo lectura sobre
    ''' <paramref name="state"/> —las tres listas son del llamador— y corre en el hilo de UI igual que
    ''' <c>NpcSkinLivePreview.ResolveBodySkinCandidates</c>, que ya la llama así desde un diálogo.</para>
    ''' <para><b>El contexto OBTS se puede PEDIR</b> con <paramref name="ctxKeywords"/>. Acá había un
    ''' límite declarado —«las combinaciones se resuelven con <c>state.LoadoutArmorContextKeywords</c>,
    ''' que se llena al MUESTREAR el atuendo, así que una prenda recién agregada sólo resuelve las
    ''' <c>Default=True</c>»— con el argumento de que sembrar keywords sería inventar. <b>Ese argumento
    ''' caducó.</b> Las keywords del pick NO son especulativas: las trajo el sorteo REAL del LLKC y son
    ''' las MISMAS que <c>MainForm.ProyeccionesDelBorrador</c> le publica al preview y al guardado.
    ''' El selector las tenía en la mano y las tiraba, así que la fila calculaba OTRA variante —y OTRA
    ''' máscara— que el dibujo. Medido en Fallout 4: 331 ARMO con combinaciones gateadas por keyword, y
    ''' en 211 el contexto CAMBIA los OMOD incluidos.</para>
    '''
    ''' <para>⛔ <b>El estado COMPARTIDO no se toca.</b> Cuando el contexto pedido difiere del que el
    ''' estado ya trae, se trabaja sobre un CLON — el hilo de UI pregunta mientras los renders de fondo
    ''' leen el mismo estado. Y no hay una segunda ley del contexto: todos siguen leyéndolo de
    ''' <c>state.LoadoutArmorContextKeywords</c>; el llamador sólo aporta un estado que describe el
    ''' loadout que está armando, que es literalmente su pregunta.</para>
    '''
    ''' <para>⚠️ LÍMITE RESIDUAL, y es la VERDAD y no una carencia: una pieza ARMO <b>directa</b> no pasó
    ''' por ningún LLKC, así que su contexto es vacío de verdad —el borrador le da la misma lista vacía—
    ''' y sólo le aplican las combinaciones <c>Default=True</c>. Igual la piel del WNAM. Lo que se cerró
    ''' es la pieza LEVELED, que sí traía keywords y las perdía en el camino.</para>
    ''' <para>⛔ Y devuelve TAMBIÉN LAS TRES MÁSCARAS del torneo, porque son la MISMA respuesta y ya
    ''' estaban calculadas acá adentro. El selector de atuendos las derivaba por su cuenta con
    ''' <c>EquipResolver.BuildFootprint(addonFormIDs:=Nothing)</c> —la unión de TODOS los Models— mientras
    ''' el render las arma con los candidates que EMITIÓ: el grupo INDX que resolvió OBTS y ya pasado por
    ''' el dedup intra-ARMO <c>coveredSlots</c>. Medido sobre FO4: 10 de 1.067 ARMO donde
    ''' <c>coveredSlots</c> pierde bits de la unión, así que la fila podía decir «✗ eliminated» sobre una
    ''' prenda que el render dibuja. Se agregan con las MISMAS tres expresiones del torneo
    ''' (ver <c>armoGroups</c>/<c>equipItems</c> más abajo en este archivo) y sobre el MISMO filtro: el
    ''' grupo de un ARMO son sus candidates no-Skin con <c>SlotMask &lt;&gt; 0</c>. Una segunda derivación
    ''' es una segunda ley, y ésta ya divergía.</para>
    ''' <para>Sin candidates que compitan las tres van en 0 — que es lo mismo que el render hace al no
    ''' generar grupo: el ARMO no entra al torneo, no ocupa slot y no elimina a nadie.</para></summary>
    Friend Function EmisionDeArmo(armoFormID As UInteger,
                                  state As MainForm.NPCVisualState,
                                  Optional ctxKeywords As List(Of UInteger) = Nothing) _
                                  As (Dibuja As Boolean, Compite As Boolean, EquipMask As UInteger,
                                      GeometryMask As UInteger, OcclusionMask As UInteger)
        If armoFormID = 0UI OrElse state Is Nothing Then Return (False, False, 0UI, 0UI, 0UI)
        ' Sólo se clona cuando el contexto pedido DIFIERE del que el estado ya dice para este ARMO: con
        ' `Nothing` -o con el mismo- corre byte a byte como antes y no paga nada. Es exacto, no una
        ' heurística: se comparan los conjuntos.
        If ctxKeywords IsNot Nothing AndAlso Not MismoContextoDe(state, armoFormID, ctxKeywords) Then
            state = ConEsteContexto(state, armoFormID, ctxKeywords)
        End If
        Dim candidates As New List(Of MainForm.MeshCandidate)
        Dim order As Integer = 0
        Dim warnings As New List(Of String)
        CollectArmoCandidates(armoFormID, state, MainForm.MeshCandidateKind.Outfit, candidates, order, warnings)
        ' El grupo del ARMO, con el MISMO filtro que `slottedCandidates` del torneo.
        ' ⛔ EN UNA SOLA LINEA a proposito: el caso C16 del gate compara ESTE predicado contra el del
        ' render linea por linea, para que las dos leyes no se puedan separar. Partido en dos, el
        ' comparador ve medio predicado y da ROJO por la FORMA en que esta escrito.
        Dim grupo = candidates.Where(Function(c) c.Kind <> MainForm.MeshCandidateKind.Skin AndAlso c.SlotMask <> 0UI).ToList()
        Return (Dibuja:=candidates.Count > 0,
                Compite:=grupo.Count > 0,
                EquipMask:=If(grupo.Count > 0, grupo(0).ArmoOwnSlotMask, 0UI),
                GeometryMask:=grupo.Aggregate(0UI, Function(acc, c) acc Or c.ArmaOwnSlotMask),
                OcclusionMask:=grupo.Aggregate(0UI, Function(acc, c) acc Or c.SlotMask))
    End Function

    ''' <summary>¿El estado YA dice exactamente eso para este ARMO? Compara como CONJUNTO: el contexto es
    ''' un conjunto de keywords, no una secuencia, y dos órdenes distintos son el mismo contexto — la
    ''' misma razón por la que la llave del preview las ordena.</summary>
    Private Shared Function MismoContextoDe(state As MainForm.NPCVisualState, armoFormID As UInteger,
                                            ctxKeywords As List(Of UInteger)) As Boolean
        Dim actual As List(Of UInteger) = Nothing
        state.LoadoutArmorContextKeywords?.TryGetValue(armoFormID, actual)
        If actual Is Nothing Then Return ctxKeywords.Count = 0
        Return New HashSet(Of UInteger)(actual).SetEquals(ctxKeywords)
    End Function

    ''' <summary>Un estado IGUAL al de entrada salvo por el contexto OBTS de UN ARMO. El original no se
    ''' toca.
    ''' <para>⛔ Se REEMPLAZA LA INSTANCIA del diccionario en el clon en vez de escribir sobre la que
    ''' vino. Hoy <c>CloneVisualState</c> lo copia en PROFUNDIDAD (arma un <c>New List</c> por entrada
    ''' sobre el diccionario propio del clon), así que escribirle encima tampoco tocaría el original —
    ''' pero esta función no puede depender de eso: el día que aquélla pase a copiar la referencia, esta
    ''' escritura mutaría el estado COMPARTIDO que leen los renders de fondo, y lo haría en silencio.
    ''' Con la instancia propia, la garantía es de acá y no de allá.</para>
    ''' <para>El campo nunca es <c>Nothing</c> en la práctica —lo crea el inicializador de
    ''' <c>NPCVisualState</c>— pero se contempla igual, que es lo que ya hace el <c>?.</c> del sitio donde
    ''' se lee.</para></summary>
    Private Function ConEsteContexto(state As MainForm.NPCVisualState, armoFormID As UInteger,
                                     ctxKeywords As List(Of UInteger)) As MainForm.NPCVisualState
        Dim clon = _stateResolver.CloneVisualState(state)
        ' ⛔ UNA sola copia profunda, no dos. `CloneVisualState` ya armó un `New List` por entrada sobre su
        ' propio diccionario, así que reconstruirlo desde el ORIGINAL copiaba todo por segunda vez. Se
        ' parte del diccionario DEL CLON —cuyas listas ya son frescas— y sólo se copia el mapa.
        ' ⛔ Pero SE SIGUE REEMPLAZANDO LA INSTANCIA: ésa es la garantía de que escribirle no toca el
        ' estado compartido, y no puede depender de que `CloneVisualState` siga copiando en profundidad.
        ' La guarda de `Nothing` que había acá era INALCANZABLE: `CloneVisualState` recorre ese campo sin
        ' protección, así que un estado con el campo nulo habría tirado una línea antes.
        Dim propio As New Dictionary(Of UInteger, List(Of UInteger))(clon.LoadoutArmorContextKeywords)
        propio(armoFormID) = New List(Of UInteger)(ctxKeywords)
        clon.LoadoutArmorContextKeywords = propio
        Return clon
    End Function

    ''' <summary>Camino robot del NPC: recorre NPC_.OBTE por el resolver canonico, elige UNA combinacion,
    ''' expande sus IncludedOmods recursivamente y emite un candidate por chunk (con el transform de montaje
    ''' del lookup BSConnectPoint::Parents), compartiendo la resolucion entre todos los candidates para que el
    ''' applier corra las Properties una sola vez a nivel de actor.
    ''' <para>Semantica del motor: cada chunk OMOD tiene ModelPath y un AttachPointFormID cuyo KYWD.EditorID
    ''' matchea un BSConnectPoint::Parents.Name del skeleton; el chunk se dibuja en el transform local del
    ''' socket sobre el bone padre. Los OMOD sin ModelPath aportan swaps de material/color.</para>
    ''' <para>El merge de skeleton lo hace PrepareSkeleton via BPTD.MODL (RACE.GNAM), no una heuristica de
    ''' filesystem. Ver 24-robots-mounting.</para></summary>
    Private Sub CollectRobotChunkCandidates(state As MainForm.NPCVisualState,
                                            candidates As List(Of MainForm.MeshCandidate),
                                            ByRef order As Integer,
                                            warnings As List(Of String))
        ' [DIAG] Entry log — confirma estado de entrada del robot path.
        ' GATEADO POR Logger.Enabled: `LogLazy` hace lazy el STRING, no el CALCULO, y `apSlotStr` resuelve
        ' un KYWD (GetRecord + parse del EditorID) POR CADA attach-point del NPC. Eso se pagaba entero en
        ' release, donde el log ni existe.
        If Logger.Enabled Then
            Dim stateFid = state.FormID
            Dim stateRace = state.RaceFormID
            Dim hasOT = state.HasObjectTemplate
            Dim otCount = If(state.ObjectTemplateCombinations Is Nothing, 0, state.ObjectTemplateCombinations.Count)
            Dim apSlotCount = If(state.AttachParentSlotFormIDs Is Nothing, 0, state.AttachParentSlotFormIDs.Count)
            Dim apSlotStr = If(state.AttachParentSlotFormIDs Is Nothing OrElse state.AttachParentSlotFormIDs.Count = 0, "[]",
                               "[" & String.Join(",", state.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & ObjectTemplateResolver.KywdEditorIdSafe(f, _ctx.PluginManager) & ")")) & "]")
            Logger.LogLazy(Function() $"[ROBOT-ENTRY] npc=0x{stateFid:X8} race=0x{stateRace:X8} hasOT={hasOT} combos={otCount} npcAPPR={apSlotCount}={apSlotStr}")
        End If

        ' ctxKeywords: los robots NPC no suelen recibir la propagacion de LLKC de una LVLI (no van
        ' envueltos en un OTFT). Se pasa vacio para que el resolver caiga en la primera por defecto.
        ' El estado ya trae las combinaciones y los enganches del NPC; la APPR de la RAZA la lee el
        ' resolver por su cuenta a partir de la raza.
        Dim ctxKeywords As New List(Of UInteger)
        Dim combos As New List(Of Canon.IBloque_Combinations)
        For Each ch In state.ObjectTemplateCombinations
            If ch IsNot Nothing Then combos.Add(ch)
        Next
        Dim resolution = ObjectTemplateResolver.ResolveNpcCombinations(
            combos, state.AttachParentSlotFormIDs, state.RaceFormID, ctxKeywords, _ctx.PluginManager)

        ' Delegate to shared OMOD chunk-mounting collector (robot + biped share capas 1+2:
        ' coord fix + socket disambig). Capa 3 (V2 SKEL-OVERRIDE) aplica a robot Y biped
        ' (gate Fase 2.5 removido).
        CollectOmodChunkCandidates(resolution, "NPC_", state, candidates, order, warnings)
    End Sub

    ''' <summary>Shared OMOD chunk-mounting candidate emit. Toma una CombinationResolution
    ''' ya construida (vía ResolveNpcCombinations o ResolveArmoCombinations) y emite los
    ''' MeshCandidates Attachment con host-scoped socket resolution. formType marca el
    ''' origen ("NPC_" robot, "ARMO" biped) y se propaga al candidate para downstream
    ''' filtering. La capa V2 SKEL-OVERRIDE NO vive aquí — se colecta en CollectV2PlanForShape
    ''' (shape loop) y se aplica en ApplyMountPlanForActor. Robot Y biped por igual.</summary>
    Private Sub CollectOmodChunkCandidates(resolution As ObjectTemplateResolver.CombinationResolution,
                                           formType As String,
                                           state As MainForm.NPCVisualState,
                                           candidates As List(Of MainForm.MeshCandidate),
                                           ByRef order As Integer,
                                           warnings As List(Of String))

        If resolution.IncludedOmods.Count = 0 AndAlso resolution.DirectProperties.Count = 0 Then
            Return
        End If

        ' Load the actor's skeleton NIF once and pre-index its BSConnectPoint::Parents by
        ' socket name (case-insens). Used to look up MountSocket transform per chunk.
        Dim socketsByName = _mountingResolver.LoadActorBSConnectPoints(state, warnings)

        ' [HOST-SCOPED-SNAPSHOT] skeletonSockets = SRC1+SRC2 sockets ANTES de que SRC3
        ' contribuya. Estos son los sockets del actor/skeleton root — el fallback final
        ' de la cadena host walk. Cualquier socket que un chunk publique vía SRC3 vive en
        ' su propio namespace (publisherSockets[omodFid]) y se resuelve consultando el
        ' host inmediato del consumer hacia arriba. El namespace flat global socketsByName
        ' se mantiene para callers legacy que aún no migraron, pero el robot path mount-
        ' lookup ya no lo consulta — usa host-scoped.
        Dim skeletonSockets As New Dictionary(Of String, BSConnectPointReader.ConnectPointInfo)(socketsByName, StringComparer.OrdinalIgnoreCase)
        ' Per-publisher socket map: cada chunk publisher (OMOD FormID) tiene su propio
        ' diccionario de sockets que él publica vía BSConnectPoint::Parents. Sin merging
        ' con skeleton, sin FIRST-WINS — cada publisher tiene su namespace propio.
        ' Cada entry guarda MainForm.PublisherSocketInfo (Socket + HostSocketGlobalT + flag de parent),
        ' computado UNA vez al indexing time, reusado por todos los consumers de ese host.
        ' Keyed por OMOD FormID asset-level: los sockets que un OMOD publica son los mismos
        ' independiente de apIdx (son propiedad del NIF, no de la instancia). La identidad
        ' por instancia (FormID, ApIdx) la lleva hostChainMap aparte.
        Dim publisherSockets As New Dictionary(Of UInteger, Dictionary(Of String, MainForm.PublisherSocketInfo))

        ' Source 3 (runtime pre-mount): cada chunk en IncludedOmods puede exponer sub-sockets
        ' (BSConnectPoint::Parents en su NIF) que child chunks van a buscar para montarse.
        ' Estos sockets pueden vivir SOLO en el chunk NIF y no en RACE.ANAM/BPTD.MODL.
        ' Caso vivo Assaultron: TorsoAssaultron expone P-AssaultronArmorSlotTorsoFront/Rear,
        ' LegsAssaultron expone P-ModLegLeft/RightAssaultronArmorLow/Upper, HeadAssaultron
        ' expone P-HeadArmorAssaultron. Sin esta tercera fuente MOUNT-LOOKUP falla para los
        ' armors y caen al fallback __chunkAnchor__ con offset incorrecto.
        For preIdx = 0 To resolution.IncludedOmods.Count - 1
            Dim omodPre = resolution.IncludedOmods(preIdx)
            If omodPre Is Nothing OrElse String.IsNullOrEmpty(omodPre.ModelFileName) Then Continue For
            Dim dictKeyPre = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(omodPre.ModelFileName)
            Dim bytesPre = MeshPathHelpers.TryLoadMeshBytes(dictKeyPre)
            If bytesPre Is Nothing Then Continue For
            Try
                Dim nifPre As New Nifcontent_Class_Manolo()
                nifPre.Load_Manolo(bytesPre)
                Dim chunkParents = BSConnectPointReader.ReadParents(nifPre)
                ' [DIAG-CHAIN] Para cada sub-socket que el chunk expone, buscar el NiNode del
                ' parent_bone en la jerarquía interna del chunk. Si existe Y su chunk-world
                ' position difiere de actor.parent_bone.world, entonces el socket.local está
                ' relativo al chunk's internal view del bone (NO al actor's), y hay que
                ' encadenar via chunk's position cuando computamos M_mesh.
                For Each cpD In chunkParents
                    If cpD Is Nothing OrElse String.IsNullOrEmpty(cpD.ParentBoneName) Then Continue For
                    Try
                        Dim chunkParentNode = nifPre.FindBlockByName(Of NiflySharp.Blocks.NiNode)(cpD.ParentBoneName)
                        Dim socketNm = cpD.Name, parentNm = cpD.ParentBoneName, omNmL = omodPre.EditorID
                        If chunkParentNode IsNot Nothing Then
                            Dim chunkParentWorld = Transform_Class.GetGlobalTransform(chunkParentNode, nifPre)
                            Dim cpwT = chunkParentWorld.Translation, cpwR = chunkParentWorld.Rotation
                            Dim sT = cpD.Translation
                            ' Chain-derived socket world = chunk.parent.world × socket.local (translation rough)
                            Dim chainImpliedX = cpwT.X + sT.X, chainImpliedY = cpwT.Y + sT.Y, chainImpliedZ = cpwT.Z + sT.Z
                            Logger.LogLazy(Function() $"[DIAG-CHAIN]   chunk '{omNmL}' exposes socket='{socketNm}' parent='{parentNm}' chunk.parent.world.T=({cpwT.X:F3},{cpwT.Y:F3},{cpwT.Z:F3}) socket.local.T=({sT.X:F3},{sT.Y:F3},{sT.Z:F3}) chain-implied socket world (T sum, no rotation)=({chainImpliedX:F3},{chainImpliedY:F3},{chainImpliedZ:F3})")
                        Else
                            Logger.LogLazy(Function() $"[DIAG-CHAIN]   chunk '{omNmL}' exposes socket='{socketNm}' parent='{parentNm}' chunk hierarchy has NO NiNode named '{parentNm}' → socket.local interpretado contra actor.parent")
                        End If
                    Catch exCH As Exception
                        Dim socketNm2 = cpD.Name, exMsg = exCH.Message
                        Logger.LogLazy(Function() $"[DIAG-CHAIN] EXCEPTION socket='{socketNm2}': {exMsg}")
                    End Try
                Next
                ' [HOST-SCOPED] publisherSockets[omod.FormID] lleva TODOS los sockets que publica este chunk,
                ' sin merge con el skeleton y sin first-wins: el namespace del publisher es propio. Un nombre
                ' repetido dentro del MISMO chunk es inconsistencia local: se loguea y gana el primero.
                ' HostSocketGlobalT se computa EN EL ESPACIO DEL NIF DEL HOST: si el parent existe en este NIF,
                ' parent.global compuesto con socket.local; si no existe, ParentFoundInHostNif=False y el
                ' consumidor cae al camino del skeleton; si el parent name esta vacio, se trata como root del
                ' NIF (identidad), que es la semantica del motor para sockets sin parent explicito.
                Dim hostMap As Dictionary(Of String, MainForm.PublisherSocketInfo) = Nothing
                If Not publisherSockets.TryGetValue(omodPre.FormID, hostMap) Then
                    hostMap = New Dictionary(Of String, MainForm.PublisherSocketInfo)(StringComparer.OrdinalIgnoreCase)
                    publisherSockets(omodPre.FormID) = hostMap
                End If
                For Each cpHost In chunkParents
                    Dim nmHost = If(cpHost.Name, "")
                    If String.IsNullOrEmpty(nmHost) Then Continue For
                    If hostMap.ContainsKey(nmHost) Then
                        Dim nmHostL = nmHost, omNmHostL = omodPre.EditorID
                        Logger.LogLazy(Function() $"[SOCKETS-PUBLISHER-DUP]   '{nmHostL}' duplicado dentro del mismo chunk '{omNmHostL}' — keep first")
                        Continue For
                    End If
                    Dim parentFound As Boolean = False
                    Dim parentGlobal As New Transform_Class() ' identity default = host NIF root
                    Dim parentNm = If(cpHost.ParentBoneName, "")
                    If String.IsNullOrEmpty(parentNm) Then
                        ' Parent vacío = parent implícito root del host NIF (identity).
                        parentFound = True
                    Else
                        Dim parentNode = nifPre.FindBlockByName(Of NiflySharp.Blocks.NiNode)(parentNm)
                        If parentNode IsNot Nothing Then
                            parentFound = True
                            parentGlobal = Transform_Class.GetGlobalTransform(parentNode, nifPre)
                        End If
                    End If
                    Dim socketLocalAsTransform As New Transform_Class With {
                        .Translation = cpHost.Translation,
                        .Rotation = BSConnectPointReader.QuatToMatrix33(cpHost.Rotation),
                        .Scale = If(cpHost.Scale > 0.0F, cpHost.Scale, 1.0F)
                    }
                    Dim hostSocketGlobal As Transform_Class = parentGlobal.ComposeTransforms(socketLocalAsTransform)
                    hostMap(nmHost) = New MainForm.PublisherSocketInfo With {
                        .Socket = cpHost,
                        .HostSocketGlobalT = hostSocketGlobal,
                        .ParentFoundInHostNif = parentFound
                    }
                    Dim nmHostL2 = nmHost, omNmHostL2 = omodPre.EditorID, pfL = parentFound
                    Dim hsT = hostSocketGlobal.Translation
                    Logger.LogLazy(Function() $"[PUBLISHER-SOCKET-INDEX] chunk='{omNmHostL2}' socket='{nmHostL2}' parent='{parentNm}' parentFoundInHostNif={pfL} hostSocketGlobal.T=({hsT.X:F3},{hsT.Y:F3},{hsT.Z:F3})")
                Next
            Catch exPre As Exception
                Dim msgL = exPre.Message, omodNmL = omodPre.EditorID
                Logger.LogLazy(Function() $"[SOCKETS-SRC3-CHUNK] EXCEPTION reading chunk '{omodNmL}': {msgL}")
            End Try
        Next

        ' Recorre IncludedOmods (lista paralela IncludedOmodApIdx con el apIdx por emision). Cada OMOD con
        ' ModelPath es un chunk a montar; los que no tienen aportan solo Properties, resueltas en bloque por
        ' el applier al final.
        ' Lookup del socket: el apEditorId es el EditorID del KYWD de AttachPoint (ap_Bot_ArmsTypeA1) y los
        ' sockets del host se llaman P-<base> o P-<base>|<n>. Se saca el prefijo ap_Bot_ / ap_ y se prueba
        ' primero la forma indexada, luego la simple. Las dos conviven en vanilla (TorsoHandy -> P-BotCore,
        ' Arm_Right_Flamer -> P-ArmsTypeA1|1).
        ' [HOST-SCOPED ORDINAL] hostChainMap[ordinal] = ordinal del padre inmediato. La identidad por ordinal
        ' monotonico (en expand-time, antes de cualquier dedup) garantiza que el mismo OMOD reutilizado bajo
        ' hosts distintos no colapse identidades. El ordinal 0 queda reservado al root del skeleton.
        Dim hostChainMap As New Dictionary(Of Integer, Integer)
        For hi = 0 To resolution.IncludedOmods.Count - 1
            Dim omodHi = resolution.IncludedOmods(hi)
            If omodHi Is Nothing Then Continue For
            Dim ordHi As Integer = If(hi < resolution.IncludedOmodInstanceOrdinal.Count, resolution.IncludedOmodInstanceOrdinal(hi), 0)
            Dim hostOrdHi As Integer = If(hi < resolution.IncludedOmodHostInstanceOrdinal.Count, resolution.IncludedOmodHostInstanceOrdinal(hi), 0)
            If ordHi = 0 Then Continue For ' unslotted properties-only — no host concept

            Dim existingHL As Integer = Nothing

            If hostChainMap.TryGetValue(ordHi, existingHL) Then
                Dim ordHiL = ordHi, newHL = hostOrdHi
                Logger.LogLazy(Function() $"[HOSTCHAIN-OVERWRITE] ordinal={ordHiL} existing.host={existingHL} new.host={newHL} — bug de implementación: ordinal monotónico debería ser único")
            End If
            hostChainMap(ordHi) = hostOrdHi
        Next

        Dim chunkCount As Integer = 0
        For i = 0 To resolution.IncludedOmods.Count - 1
            Dim omod = resolution.IncludedOmods(i)
            Dim apIdx = If(i < resolution.IncludedOmodApIdx.Count, resolution.IncludedOmodApIdx(i), CByte(0))
            Dim ord As Integer = If(i < resolution.IncludedOmodInstanceOrdinal.Count, resolution.IncludedOmodInstanceOrdinal(i), 0)
            Dim hostOrd As Integer = If(i < resolution.IncludedOmodHostInstanceOrdinal.Count, resolution.IncludedOmodHostInstanceOrdinal(i), 0)
            Dim hostFid As UInteger = If(i < resolution.IncludedOmodHostFormID.Count, resolution.IncludedOmodHostFormID(i), 0UI)
            Dim hostApIdx As Byte = If(i < resolution.IncludedOmodHostApIdx.Count, resolution.IncludedOmodHostApIdx(i), CByte(0))
            If omod Is Nothing Then Continue For
            If String.IsNullOrEmpty(omod.ModelFileName) Then Continue For ' property-only OMODs
            ' Note: vanilla rusty/variant OMODs (Bot_ArmLeftProtectronRusty1 etc.) have
            ' FormType=NONE while the originals have FormType=NPC_. Filtering by FormType
            ' would drop the variants — they render in-game, so we accept any FormType here.

            Dim apEditorId = _mountingResolver.ResolveAttachPointEditorId(omod.DataAttachPoint)
            ' Host-scoped resolution: walk host chain por ORDINAL hasta caer en skeleton root.
            ' Devuelve MainForm.PublisherSocketInfo (con HostSocketGlobalT precomputado) + matchedHostOrdinal —
            ' el consumer no re-descubre el publisher después.
            Dim resolvedInfo As MainForm.PublisherSocketInfo = Nothing
            Dim matchedHostOrdResolved As Integer = 0
            Dim matchedHostFid As UInteger = 0UI
            Dim matchedHostAi As Byte = 0
            Dim socket = _mountingResolver.ResolveMountSocketHostScoped(apEditorId, apIdx, hostOrd, publisherSockets, hostChainMap, resolution, skeletonSockets, resolvedInfo, matchedHostOrdResolved, matchedHostFid, matchedHostAi)

            ' [SKELETON-FALLBACK-SOCKET] Resolución paralela contra skeletonSockets (SRC1+SRC2)
            ' para Path B. El skeleton publica P-X con ParentBoneName usando nomenclatura
            ' actor-skel (indexed: Arm1|0, Arm1|1, etc.), distinto al publisher chunk socket
            ' que usa chunk-internal naming sin suffix. Path B (chunks sin C-X NiNode interno)
            ' usa ESTE socket para que ResolveEffectiveWorld(parentBone) encuentre el bone
            ' indexed correcto en actor.skel. Nothing si el skeleton no publica este socket
            ' (raro — Path B caería al publisher socket como último recurso, loggeado).
            ' Lookup: indexed (P-base|apIdx) primero, plain (P-base) fallback.
            Dim skelFallbackSocket As BSConnectPointReader.ConnectPointInfo = Nothing
            If Not String.IsNullOrEmpty(apEditorId) Then
                Dim baseNm_fb = apEditorId
                If baseNm_fb.StartsWith("ap_Bot_", StringComparison.OrdinalIgnoreCase) Then
                    baseNm_fb = baseNm_fb.Substring("ap_Bot_".Length)
                ElseIf baseNm_fb.StartsWith("ap_", StringComparison.OrdinalIgnoreCase) Then
                    baseNm_fb = baseNm_fb.Substring("ap_".Length)
                End If
                Dim indexed_fb = $"P-{baseNm_fb}|{apIdx}"
                Dim plain_fb = $"P-{baseNm_fb}"
                If Not skeletonSockets.TryGetValue(indexed_fb, skelFallbackSocket) Then
                    skeletonSockets.TryGetValue(plain_fb, skelFallbackSocket)
                End If
            End If

            Dim apIdxLog = apIdx
            Dim apEditorLog = apEditorId
            Dim socketLocalForLog = socket
            Dim ordLog = ord, hostOrdLog = hostOrd, matchedOrdLog = matchedHostOrdResolved
            Dim hostFidLog = hostFid, hostApIdxLog = hostApIdx, matchedHostFidLog = matchedHostFid
            Dim skelFbForLog = skelFallbackSocket
            Logger.LogLazy(Function() $"[ROBOT-CHUNK] omod={omod.EditorID}({omod.FormID:X8}) ord={ordLog} apEditor='{apEditorLog}' apIdx={apIdxLog} host=(ord={hostOrdLog},0x{hostFidLog:X8},apIdx={hostApIdxLog}) matchedHost=(ord={matchedOrdLog},0x{matchedHostFidLog:X8}) → socket={If(socketLocalForLog Is Nothing, "NOT-FOUND", $"'{socketLocalForLog.Name}' onBone='{socketLocalForLog.ParentBoneName}'")} skelFallback={If(skelFbForLog Is Nothing, "NOT-FOUND", $"'{skelFbForLog.Name}' onBone='{skelFbForLog.ParentBoneName}'")}")

            Dim dictKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(omod.ModelFileName)
            candidates.Add(New MainForm.MeshCandidate With {
                .DictKey = dictKey,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MainForm.MeshCandidateKind.Attachment,
                .SourceFormID = omod.FormID,
                .ChunkOmodFormID = omod.FormID,
                .AttachPointKywdEditorId = apEditorId,
                .MountApIdx = apIdx,
                .MountSocket = socket,
                .SkeletonFallbackSocket = skelFallbackSocket,
                .ChunkInstanceOrdinal = ord,
                .MountHostOmodFormID = hostFid,
                .MountHostApIdx = hostApIdx,
                .MountHostInstanceOrdinal = hostOrd,
                .MatchedHostOmodFormID = matchedHostFid,
                .MatchedHostApIdx = matchedHostAi,
                .MatchedHostInstanceOrdinal = matchedHostOrdResolved,
                .ResolvedHostSocketGlobalT = resolvedInfo?.HostSocketGlobalT,
                .ParentFoundInMatchedHostNif = resolvedInfo IsNot Nothing AndAlso resolvedInfo.ParentFoundInHostNif,
                .OmodResolution = resolution,
                .OmodResolutionFormType = formType,
                .Order = order
            })
            order += 1
            chunkCount += 1
        Next

        ' [PRE-PASS A_HOST] La pre-pass que computa ChunkToActor por candidate corre más
        ' tarde, en V2 setup, donde el SkeletonInstance (inst) está disponible para resolver
        ' actor.parentBone.world en el path fallback (Path B). Ver PopulateRobotChunkChunkToActor
        ' llamado en RegisterRobotMountSockets / antes del V2 shape loop. Aquí solo persistimos
        ' las estructuras necesarias en renderData para que la pre-pass las pueda consumir.

    End Sub

    Private Sub CollectHeadPartCandidates(headPartFormIDs As IEnumerable(Of UInteger),
                                          visited As HashSet(Of UInteger),
                                          candidates As List(Of MainForm.MeshCandidate),
                                          ByRef order As Integer,
                                          warnings As List(Of String),
                                          state As MainForm.NPCVisualState,
                                          Optional useFaceGen As Boolean = False)
        ' THE race gate, engine-faithful: RACE.DATA bit 0x2 "FaceGen Head" (see RaceBuildsFaceGenHead).
        ' A race without it builds no facegen head at all, so none of its head parts render — this is what
        ' keeps human teeth/mouths off dogs, robots and creatures even when a buggy NPC.PNAM lists one
        ' (e.g. EncRaiderDog01 → MaleMouthHumanoidDirtyTeethMissing).
        ' It replaces the old "does the RACE declare any head parts?" proxy AND the per-HDPT RNAM check
        ' (see CollectHeadPartCandidate): the engine applies neither of those when assembling a worn head.
        If Not RaceBuildsFaceGenHead(state) Then
            Logger.LogLazy(Function() $"[HEADPART] race 0x{If(state Is Nothing, 0UI, state.RaceFormID):X8} has no RACE.DATA FaceGen-Head flag — no head parts rendered (the engine builds no facegen head for it).")
            Return
        End If

        ' Pre-compute Misc->parent effective-type promotion for the top-level (parent=-1) case:
        ' vanilla NPC.PNAM often lists a hairline both in the hair's HNAM and standalone in PNAM;
        ' without this map the cascade depended on visit order. Shared helper = single source of
        ' truth with the bake's EnumerateHdptChain (no duplicated rule).
        Dim miscToParentEffective = HeadPartResolver.BuildMiscToParentEffective(headPartFormIDs, _ctx.PluginManager, AddressOf _ctx.ParseHdptCached)

        For Each hdptFormID In headPartFormIDs.Where(Function(id) id <> 0UI)
            CollectHeadPartCandidate(hdptFormID, visited, candidates, order, warnings, -1, state, useFaceGen, miscToParentEffective)
        Next
    End Sub

    Private Sub CollectHeadPartCandidate(hdptFormID As UInteger,
                                         visited As HashSet(Of UInteger),
                                         candidates As List(Of MainForm.MeshCandidate),
                                         ByRef order As Integer,
                                         warnings As List(Of String),
                                         parentPartType As Integer,
                                         state As MainForm.NPCVisualState,
                                         Optional useFaceGen As Boolean = False,
                                         Optional miscToParentEffective As Dictionary(Of UInteger, Integer) = Nothing)
        If hdptFormID = 0UI Then Return
        If visited.Contains(hdptFormID) Then Return
        visited.Add(hdptFormID)

        Dim hdptRec = _ctx.PluginManager.GetRecord(hdptFormID)
        If hdptRec Is Nothing OrElse hdptRec.Header.Signature <> "HDPT" Then Return

        Dim hdpt = _ctx.ParseHdptCached(hdptRec)

        ' Extra parts (type=0/Misc) inherit the parent's type for color treatment.
        ' E.g. a hair extra part mesh needs the same hair palette remap as the main hair.
        ' Path principal: parent>=0 → cascade directo via HNAM recursion.
        ' Path top-level (parent=-1) con raw=0: si el merged list incluye un parent que declara
        ' este Misc en su HNAM (vanilla NPC.PNAM duplica hairlines típicamente), promovemos al
        ' effective de ese parent. Cierra el bug donde Hairline standalone en NPC.PNAM no
        ' cascadeaba si el visit order ponía el Misc antes del parent.
        ' Shared rule = single source of truth with the bake's EnumerateHdptChain.
        Dim effectivePartType = HeadPartResolver.ResolveEffectivePartType(hdpt.TipoDeParte(), parentPartType, hdptFormID, miscToParentEffective)

        ' NO agregar acá un gate por HDPT.RNAM: el motor NO filtra por "Valid Races" las head parts que un
        ' actor LLEVA — RNAM es filtro del CATÁLOGO del chargen (por eso los pickers sí lo aplican). Ponerlo
        ' en el render nos hacía más estrictos que el juego: los NPC de razas custom, cuyas razas se inyectan
        ' en las FormLists en RUNTIME por script, salían pelados. El gate real del motor es de RAZA
        ' (RACE.DATA bit 0x2) y lo aplica el caller. Ver 40-bake-reglas-comunes.md.

        If hdpt.ModelFileName <> "" Then
            ' El `_faceBones` (rig de los 68 huesos de cara: Jaw, LipUpper_L, Cheek_R…) es lo que permite
            ' que el FMRS deforme la malla. Se recolecta para toda raza que construya cabeza FaceGen
            ' (RaceBuildsFaceGenHead — el bit 0x2, ya garantizado por el early-return de
            ' CollectHeadPartCandidates); `useFaceGen` acá sólo puede venir en False por el
            ' PreviewGenderOverride de los editores ARMA/ARMO ("Show other gender" dibuja una cabeza
            ' race-default del OTRO género, que no es la del NPC y no debe morfear con su FMRS).
            Dim dictKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(hdpt.ModelFileName)
            ' Camino head-bake: NO se redirige. Se dibuja la malla PLANA — que es lo que dibujan el motor y
            ' el CK — y el `_faceBones` queda como INSUMO para HeadBakeService. Medido: el FaceGeom del BA2 usa
            ' el UV del PLANO 227 a 0, y su base material es la del plano con el TNAM encima; dibujar el
            ' `_faceBones` hacía caer el body-weight sobre los 68 huesos de cara en vez de los ~10 del rig plano.
            ' Sólo FO4 (en SSE TryGetFaceBonesVariant da "").
            Dim faceBonesInputKey As String = ""
            If useFaceGen AndAlso NPC_Config.IsHeadBakeActive() Then
                faceBonesInputKey = MeshPathHelpers.TryGetFaceBonesVariant(dictKey)
            End If

            ' Head-rear nape body texture: the vanilla-UV nape mesh needs a vanilla-UV body texture.
            ' For ghoul females the live body path is CBBE's CBBE-UV body (UV-mismatched). The
            ' clone-to-disk fix (vanilla bytes under a distinct path key, to dodge the shared
            ' path-keyed GL texture cache) is applied per-shape in ApplyShapeMaterialOverrides via the
            ' candidate's HeadPartHdptFormID gate. UsesBodyTexture stays the raw record value here —
            ' the previous override-proxy forcing (HumanRace 0x13746 + is-override heuristic) is gone.
            Dim effectiveUsesBodyTexture = hdpt.UsaTexturaDelCuerpo()

            ' Trace del candidato HeadPart: qué HDPT, tipo raw/effective, mesh, el TXST (TNAM) y color.
            ' Se dibuja SIEMPRE la malla plana (head-bake); el `_faceBones` es insumo, no se dibuja.
            If Logger.Enabled Then
                Dim hdptEidC = If(hdptRec.EditorID, "")
                Dim rawTypeC = hdpt.TipoDeParte()
                Dim effTypeC = effectivePartType
                Dim origMeshC = If(hdpt.ModelFileName, "")
                Dim finalKeyC = dictKey
                Dim fbInputC = faceBonesInputKey
                Dim tnamC = hdpt.TextureSet
                Dim colorC = hdpt.Color
                Dim ubtC = effectiveUsesBodyTexture
                Dim ufgC = useFaceGen
                Logger.LogLazy(Function() $"[HDPT-CAND] hdpt=0x{hdptFormID:X8} eid='{hdptEidC}' rawType={rawTypeC} effType={effTypeC} useFaceGen={ufgC} TNAM=0x{tnamC:X8} color=0x{colorC:X8} usesBodyTex={ubtC} mesh='{origMeshC}' dictKey='{finalKeyC}' faceBonesInput='{fbInputC}'")
            End If

            ' UseSolidTint ya NO se asigna acá: es propiedad calculada sobre HeadPartColorFormID (HDPT.CNAM),
            ' que sí se setea abajo. Este sitio usaba el flag DATA 0x10, gate que el corpus REFUTÓ (ninguna de
            ' las 5 HDPT con CNAM lo tiene seteado y el CK usó el CNAM igual) ⇒ el render caía al HCLF mientras
            ' el bake usaba el CNAM. Ver MainForm.MeshCandidate.UseSolidTint.
            candidates.Add(New MainForm.MeshCandidate With {
                .DictKey = dictKey,
                .FaceBonesDictKey = faceBonesInputKey,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MainForm.MeshCandidateKind.HeadPart,
                .HeadPartType = effectivePartType,
                .HeadPartTypeRaw = hdpt.TipoDeParte(),
                .HeadPartColorFormID = hdpt.Color,
                .TextureSetFormID = hdpt.TextureSet,
                .HeadPartHdptFormID = hdptFormID,
                .UsesBodyTexture = effectiveUsesBodyTexture,
                .Order = order,
                .RaceMorphTriPath = hdpt.ArchivoDeDeformacion(0UI),
                .ChargenMorphTriPath = hdpt.ArchivoDeDeformacion(2UI),
                .MeshMorphTriPath = hdpt.ArchivoDeDeformacion(1UI),
                .Hide = (effectivePartType = 7),
                .IsHnamExtra = (parentPartType >= 0)
            })
            order += 1
        End If

        ' Pass the effective type down so nested extras also inherit
        Dim childParentType = If(effectivePartType <> 0, effectivePartType, parentPartType)
        For Each extraPartFormID In hdpt.PartesExtra()
            CollectHeadPartCandidate(extraPartFormID, visited, candidates, order, warnings, childParentType, state, useFaceGen, miscToParentEffective)
        Next
    End Sub

    ''' <summary>Particiones de head-part que oculta el headwear en Skyrim. Son DOS mecanismos y cada uno lee
    ''' una mascara DISTINTA: (a) oculta la particion del SLOT DE PELO, decidido con el BOD2 del <b>ARMO</b>
    ''' equipado; (b) al adjuntar el item cuyo owner es el slot de pelo, oculta ademas toda particion cuyo slot
    ''' este agrupado con el, y ese grupo sale del BOD2 del <b>ARMA</b>. Un slot declarado solo por el ARMO no
    ''' agrupa nada: una capucha con ARMO [31,42] y ARMA de slot 31 [31,41,43] oculta 41 y 43, pero el 42 no
    ''' oculta nada. Ver 23-armor-oclusion-sse-re.
    ''' <para>No incluye el bit de face-cull, que es whole-node y no per-particion. Friend Shared para que la
    ''' regla viva en UN sitio, compartido por el selector y el host de render.</para></summary>
    Friend Shared Function HeadPartHideMask(hairSlotMask As UInteger, wornMask As UInteger,
                                            armatureMasks As IEnumerable(Of UInteger)) As UInteger
        If hairSlotMask = 0UI Then Return 0UI
        ' (a) el slot de pelo, si algún ARMO equipado lo declara.
        Dim hide As UInteger = wornMask And hairSlotMask
        ' (b) los demás slots del ARMA que quedó adjunta en el slot de pelo.
        If armatureMasks IsNot Nothing Then
            For Each m In armatureMasks
                If (m And hairSlotMask) <> 0UI Then hide = hide Or m
            Next
        End If
        Return hide
    End Function

    ''' <summary>Resolve which candidates win their biped-slot tournament and which head parts the worn set
    ''' occludes. The head-part occlusion is RACE-driven (engine-faithful): the caller passes the slot-30-
    ''' relative masks derived from this NPC's RACE.DATA biped objects — <paramref name="faceCullMask"/> (A,
    ''' full-face cull), <paramref name="hairMask"/> (B, the hair channel = 30+B and 30+B+1), and
    ''' <paramref name="facialHairMask"/> (C, the beard slot). 0 mask = that channel occludes nothing.
    ''' <paramref name="wornItemMasks"/> devuelve (out) el BOD2 del ARMA de cada pieza ganadora (mecanismo b
    ''' del attach 0x140218200) y <paramref name="wornSlotMask"/> el worn mask agregado (mecanismo a), para
    ''' que ResolvePreviewVariant arme la máscara efectiva sin recomputar el torneo.</summary>
    Private Function SelectWinningCandidates(candidates As List(Of MainForm.MeshCandidate),
                                             faceCullMask As UInteger, hairMask As UInteger,
                                             facialHairMask As UInteger,
                                             ByRef wornItemMasks As List(Of UInteger),
                                             ByRef wornSlotMask As UInteger) As List(Of MainForm.MeshCandidate)
        Dim selected As New List(Of MainForm.MeshCandidate)

        ' HDPT type=7 Meatcaps used to be filtered here. Now they pass through to the render
        ' pipeline and are marked in result.ShapeMeatcap so the "Render gore" toggle governs
        ' their visibility uniformly with the BSSubIndex SECTIONCAP/TORSOCAP shapes. The
        ' candidate.Hide flag survives through to ApplyShapeGeometry → ShapeMeatcap mapping.
        Dim visibleCandidates = candidates.ToList()

        ' Primera pasada: candidates CON slot.
        ' En FO4 las capas [U] (36-40) y [A] (41-45) están diseñadas para coexistir, así que el underarmor
        ' declara bits que las piezas de over-armor solapan parcialmente.
        ' Regla "extended underarmor": un candidate que declara BODY o algún bit [U] Y ADEMÁS algún bit
        ' [A] es un underarmor extendido cuya malla YA cubre esos slots [A] (incluye piernas o brazos). No
        ' puede coexistir con un over-armor puro [A] que reclame los mismos bits: serían dos geometrías
        ' superpuestas, con clip visible. Por eso RESERVA sus bits [A] y descarta entero al que los pida.
        ' Las máscaras concretas viven en EquipResolver (FO4_Base_Library).

        ' Skin candidates (NPC_.WNAM / RACE.WNAM via state.SkinFormID) representan la base body
        ' geometry del NPC — NO son piezas equipables que compitan por slots con outfits/armor.
        ' El esquema del record confirma que NPC_.WNAM y RACE.WNAM son slots
        ' dedicados ("Skin" ARMO), distintos del inventory de outfits. Cita engine doc Steam/Nexus
        ' habla de "vault suit + something else" — outfit vs outfit, nunca outfit vs body skin.
        ' Conceptualmente: un actor SIEMPRE tiene body mesh; un outfit lo CUBRE visualmente, no
        ' lo desequipa. `unequipall` deja al NPC en NakedTorso/NakedHands, no invisible.
        ' Por lo tanto: Skin candidates bypasean la slot conflict resolution. Siempre se aceptan
        ' enteros, y NO contribuyen a occupiedSlots/shieldedSlots/reservedAbits — quedan fuera
        ' del torneo. El toggle "Render body" + "Render underarmor" decide visibilidad post-hoc.
        Dim skinCandidates = visibleCandidates.Where(Function(c) c.Kind = MainForm.MeshCandidateKind.Skin).ToList()
        Dim nonSkinCandidates = visibleCandidates.Where(Function(c) c.Kind <> MainForm.MeshCandidateKind.Skin).ToList()
        For Each skinC In skinCandidates
            selected.Add(skinC)
        Next

        Dim slottedCandidates = nonSkinCandidates.Where(Function(c) c.SlotMask <> 0UI).ToList()

        ' La resolución de conflicto de slots vive en EquipResolver (FO4_Base_Library), para que el render y la pestaña
        ' Create del editor de outfits usen las MISMAS reglas del motor.
        ' Se resuelve a nivel ARMO EQUIPADO, no por ARMA: el motor hace mutex sobre el BOD2 del item
        ' equipado como unidad — el ARMO entero gana o pierde. Alimentar las ARMA sueltas al resolver dejaba
        ' que un ARMO PARCIALMENTE perdedor conservara las ARMA cuyos slots no chocaban con el ganador (un
        ' outfit que pierde el torso pero conserva sus guantes), lo que diverge del juego Y de la pestaña
        ' Create. Por eso se agrupan por ARMO dueño, se resuelve con la UNIÓN de slots del grupo y su Order
        ' más temprano, y recién después se expande el grupo ganador de vuelta a sus ARMA.
        ' Un candidate sin ARMO dueño es su propio grupo unitario. El dedup de slots dentro de un ARMO ya
        ' corrió antes, así que acá ninguna ARMA compite con sus hermanas.
        Dim armoGroups As New List(Of List(Of MainForm.MeshCandidate))
        Dim groupByArmo As New Dictionary(Of UInteger, List(Of MainForm.MeshCandidate))
        For Each c In slottedCandidates
            If c.SourceFormID = 0UI Then
                armoGroups.Add(New List(Of MainForm.MeshCandidate) From {c})
            Else
                Dim grp As List(Of MainForm.MeshCandidate) = Nothing
                If Not groupByArmo.TryGetValue(c.SourceFormID, grp) Then
                    grp = New List(Of MainForm.MeshCandidate)
                    groupByArmo(c.SourceFormID) = grp
                    armoGroups.Add(grp)
                End If
                grp.Add(c)
            End If
        Next
        ' LEY ÚNICA (EquipResolver, FO4_Base_Library). Un EquipItem por ARMO equipado, con sus tres
        ' máscaras: EquipMask = BOD2 crudo del ARMO (con la que el motor decide el mutex, en los DOS juegos)
        ' · GeometryMask = unión de los BOD2 de las ARMA del grupo (particiones; es lo que mira la excepción
        ' anti-clipping) · OcclusionMask = lo que el render venía usando como SlotMask (ARMA ∪ headwear del
        ' ARMO), que es lo que alimenta la cobertura de piel y la oclusión de head-parts aguas abajo.
        Dim equipItems = armoGroups.Select(Function(g) New EquipResolver.EquipItem With {
            .ArmoFormID = g.First().SourceFormID,
            .Order = g.Min(Function(c) c.Order),
            .EquipMask = g.First().ArmoOwnSlotMask,
            .GeometryMask = g.Aggregate(0UI, Function(acc, c) acc Or c.ArmaOwnSlotMask),
            .OcclusionMask = g.Aggregate(0UI, Function(acc, c) acc Or c.SlotMask),
            .Tag = g}).ToList()
        Dim slotResolution = EquipResolver.Resolve(equipItems)
        For Each it In slotResolution.Winners
            selected.AddRange(DirectCast(it.Tag, List(Of MainForm.MeshCandidate)))
        Next
        Dim occupiedSlots As UInteger = slotResolution.OccupiedSlots
        ' Máscaras que consume HeadPartHideMask (mecanismo b): el BOD2 del ARMA de cada pieza renderizada,
        ' NO su SlotMask (que es ARMA ∪ bits headwear del ARMO). El writer de la tabla del biped
        ' (@0x1402134E0) guarda el ARMATURE en `entry+0x18` recorriendo los bits del ARMA; un bit que sólo
        ' declara el ARMO nunca escribe `+0x18` y por lo tanto no agrupa ni oculta nada.
        ' Cada candidate ya es un armature filtrado por raza/género = el que el engine adjuntaría.
        wornItemMasks = slotResolution.Winners.SelectMany(Function(it) DirectCast(it.Tag, List(Of MainForm.MeshCandidate))).
            Select(Function(c) c.ArmaOwnSlotMask).Where(Function(m) m <> 0UI).ToList()
        ' WORN MASK DEL MOTOR — OR del BOD2 de los ARMO EQUIPADOS, y NADA de la ARMA. Verificado byte-level
        ' en el Fallout4.exe instalado: `0x14051F530` recorre la lista de ítems equipados del actor 3D
        ' (`[actor3D+0xF8]`, count `+0x68` / data `+0x58`, stride 0x10), saltea LIGH/WEAP/AMMO y por cada uno
        ' llama al virtual `vtable+0x238` = `GetBipedObjectSlotMask 0x140313B80`, que es
        ' `[AsBipedObjectForm(this)+8]` — para un ARMO, su BOD2. Una ARMA nunca está en esa lista.
        ' El driver de oclusión de head-parts `0x140506460` testea contra ESA máscara los tres canales que
        ' declara la RACE (A face-cull `race+0x1B0`, B pelo `+0x1B4` y su B+1, C barba `+0x1B8`).
        Dim wornEquipMask As UInteger = 0UI
        For Each it In slotResolution.Winners
            wornEquipMask = wornEquipMask Or it.EquipMask
        Next
        wornSlotMask = wornEquipMask

        ' Per-segment "covered by OTHER items" occlusion (ORDER / other-items rule, engine owner-slot
        ' branch 0x14035E22B) is NOT precomputed here anymore: it is rebuilt every render by
        ' ApplyRenderToggleVisibility from the items CURRENTLY rendered, so a render toggle that hides an
        ' item drops its slots from the occluding set. LoadNifShapes only records each shape's OWN slots +
        ' group id (ShapeOwnSlots / ShapeSlotGroup) — the inputs that recompute reads.

        ' Tercera pasada: head parts (sin slot propio), ocultando segun los slots biped ocupados.
        ' La oclusion la declara la RACE, no una lista fija de slots: RACE.DATA trae tres campos que mapean
        ' (v -> slot 30+v) el slot cuya cobertura oculta cada canal (face-cull, pelo, barba). Varian por raza,
        ' asi que NUNCA se hardcodean los valores humanos. Ver 23-armor-oclusion-fo4-re.
        ' Por tipo (0/1 y los extra parts nunca ocluyen):
        '   3 Hair       : per-segmento y uniforme; la pieza entera cae si se cubre el face-cull o todas sus
        '                  particiones. RENDER-ONLY: el bake tiene su propia regla fiel al CK.
        '   4 FacialHair : oculto si lo equipado cubre el slot de barba o el de face-cull.
        '   6 Eyebrows   : oculto si lo equipado cubre el slot de face-cull.
        '   9 HeadRear   : NUNCA, es geometria base del craneo.
        ' Rama por juego: FO4 intersecta lo equipado con el canal de pelo; SSE usa el BOD2 completo del item
        ' que ocupa el slot de pelo, no la union de lo equipado (ver HeadPartHideMask).
        ' FO4: los tres canales se testean contra el WORN MASK DEL MOTOR (BOD2 de los ARMO equipados), no
        ' contra `occupiedSlots` (que trae además los bits de la ARMA). Medido: 8 ARMO vanilla declaran el
        ' slot 32 en su ARMA y NO en su ARMO (Armor_HazmatSuit(+Damaged), Clothes_RaiderMod_Hood1/2/3,
        ' Armor_Raider_GreenHoodGasmask, Armor_Power_Raider_Helm, Clothes_InstituteWorkerwithHelmet) — con
        ' ellos puestos ocultábamos ceja, barba, sombra de boca y pelo que el motor NO oculta.
        ' SSE NO se toca: allá el canal de pelo va por el mecanismo (b) —el BOD2 de la ARMA adjunta que
        ' ocupa el slot, no la unión de lo equipado— y está medido que ningún ARMO vanilla de Skyrim declara
        ' el slot 30 en su ARMA sin declararlo también en el ARMO, así que el cambio sería inerte allá y la
        ' ley es distinta.
        ' La máscara contra la que se testean los TRES canales de head-part. Una sola vez, acá.
        Dim headChannelMask As UInteger
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            headChannelMask = occupiedSlots
        Else
            headChannelMask = wornEquipMask
        End If
        Dim hairCovered As UInteger
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            hairCovered = HeadPartHideMask(hairMask, occupiedSlots, wornItemMasks)
        Else
            hairCovered = headChannelMask And hairMask
        End If
        Dim hasFaceGenHead As Boolean = (headChannelMask And faceCullMask) <> 0UI
        ' The two hair partitions a {30,31} piece can have. Engine-faithful: the partition bits are still the
        ' source mesh's biped-30/31 tags (BipedSlots.SlotBitHairTop/Long); a partition is "covered" only when its slot is
        ' both in the worn set AND in this race's hair channel (so a non-hair race with B=None never zaps hair).
        Dim hairTopCovered As Boolean = (hairCovered And BipedSlots.SlotBitHairTop) <> 0UI
        Dim hairLongCovered As Boolean = (hairCovered And BipedSlots.SlotBitHairLong) <> 0UI

        ' Pasada 2 - slotless NO-Skin: HeadParts y Attachments (chunks de robot/pack via socket).
        ' Los head parts ocluidos por headwear se MARCAN con IsOccludedByHeadwear pero no se descartan:
        ' ApplyRenderToggleVisibility decide en runtime, asi "Render headwear" OFF los destapa.
        ' Los Attachments entran con SlotMask=0 + Kind=Attachment y no participan del conflicto de slots
        ' (montan por socket, no por slot de armadura). Marcarlos Kind=Skin los hacia entrar en la pasada 0 Y
        ' aca (double-add); el Kind propio elimina ese caso por construccion.
        ' La exclusion de Kind=Skin sigue haciendo falta: los Skin con SlotMask=0 ya se aceptaron en la pasada 0.
        For Each slotlessCandidate In visibleCandidates.Where(Function(c) c.SlotMask = 0UI AndAlso c.Kind <> MainForm.MeshCandidateKind.Skin).OrderBy(Function(c) c.Order)
            If slotlessCandidate.Kind = MainForm.MeshCandidateKind.HeadPart Then
                Dim occluded As Boolean = False
                ' Oclusion de head parts en Skyrim: la RACE declara DOS biped objects, no los tres de FO4 -
                ' A = cabeza, B = pelo. Si A esta cubierto hay cull de nodo completo de la cabeza y CASCADEA a
                ' todo head part (ojos, cejas, cicatrices). Si no, solo el canal de pelo pasa por el hider, y
                ' es PER-PARTICION: ese zap lo hace el RENDER, no este codigo.
                ' El fallback de nodo completo aplica SOLO a geometria de pelo SIN dismember: ojos, cejas y
                ' cicatrices vanilla no traen dismember, asi que si los tapara, cualquier casco los haria
                ' desaparecer. Por eso los no-pelo solo caen por A.
                If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
                    ' 0 fuera de la rama de pelo: los no-pelo no leen el NIF (no lo necesitan).
                    Dim partitionMask As UInteger = 0UI
                    If hasFaceGenHead Then
                        ' A (Head, RACE +0x12C) cubierto → whole-node cull de la cabeza; eyes/brows/etc. cascadean.
                        occluded = True
                    ElseIf slotlessCandidate.HeadPartType = MainForm.HeadPartTypeHair Then
                        ' Sólo el canal de pelo (B, RACE +0x130) pasa por el hider per-partición 0x1403C56B0.
                        ' Las hairline vanilla llegan como HDPT rawType=0 (Misc) extras de un padre type=3;
                        ' effectivePartType las promueve a Hair, así que HeadPartTypeHair las agarra.
                        partitionMask = CandidatePartitionSlotMask(slotlessCandidate)
                        If partitionMask = 0UI Then
                            occluded = (hairCovered <> 0UI)          ' sin dismember → fallback SetAppCulled del engine
                        ElseIf (partitionMask And (Not hairCovered)) = 0UI Then
                            occluded = True                           ' todas las particiones cubiertas → no renderiza nada
                        Else
                            occluded = False                          ' sobreviven hermanas → el render zapea per-partición
                        End If
                    Else
                        ' Eyes / Eyebrows / Scar / Face / Misc no-pelo: en SSE sólo los tapa el face-cull (A),
                        ' ya resuelto arriba. No hay canal propio (la RACE de Skyrim no declara facial-hair
                        ' biped object) y su geometría vanilla no trae BSDismember, así que NO cascadean con el pelo.
                        occluded = False
                    End If
                    ' ZapParts (Top/Long) es un mecanismo FO4; en SSE el zap es per-partición BSDismember en el render.
                    slotlessCandidate.ZapParts = HairZapParts.None
                    If Logger.Enabled Then
                        Dim dkD = slotlessCandidate.DictKey
                        Dim effTypeD = slotlessCandidate.HeadPartType
                        Dim rawTypeD = slotlessCandidate.HeadPartTypeRaw
                        Dim hnamD = slotlessCandidate.IsHnamExtra
                        Dim faceGenD = hasFaceGenHead
                        Dim partMaskD = partitionMask
                        Dim occSlotsD = occupiedSlots
                        Dim faceMaskD = faceCullMask
                        Dim hairSlotD = hairMask
                        Dim hairCovD = hairCovered
                        Dim occD = occluded
                        Logger.LogLazy(Function() $"[SSE-HEADPART-OCCL] dict='{dkD}' effType={effTypeD} rawType={rawTypeD} isHnamExtra={hnamD} hasFaceGenHead={faceGenD} partitionMask=0x{partMaskD:X} occupiedSlots=0x{occSlotsD:X} faceCullMask=0x{faceMaskD:X} hairSlotMask=0x{hairSlotD:X} hairCovered=0x{hairCovD:X} occluded={occD}")
                    End If
                Else
                ' Los addons (extras declarados por el padre, o Misc top-level) están exentos de la oclusión
                ' de headwear normal: sólo los tapa un casco full-face. Cubre los dos caminos por los que un
                ' addon llega al render — como extra de un padre (hairlines, mouth shadow, AO/wet) o suelto
                ' en el record del NPC/RACE sin figurar como extra de nadie.
                ' Oclusión de pelo: per-segmento, uniforme a main y hairline, y dirigida por la RACE. Cada
                ' partición se oculta ⟺ su slot está cubierto Y cae en el canal de pelo de ESTA raza; una
                ' pieza con una sola partición cubierta deja asomar la otra (zap parcial, no ocultar entera).
                ' La hairline lleva el MISMO tag de slots que el main, así que sigue la misma regla, no la
                ' inversa. La pieza entera cae si se cubre el face-cull o si se cubren TODAS sus particiones.
                ' RENDER-ONLY: el bake usa su propia regla fiel al CK.
                Dim hairSlotMask As UInteger = CandidateHairSlotMask(slotlessCandidate)
                If hairSlotMask <> 0UI Then
                    ' MODELO POR PARTICIÓN — pelo under-helmet de FO4. Una pieza {30,31} tiene dos particiones:
                    ' TOP (biped 30, corona) y LONG (biped 31). Main y hairline IGUALES: zap del TOP si el
                    ' worn set cubre slot 30 (dentro del canal de pelo), zap del LONG si cubre slot 31. Saca la
                    ' partición cubierta y deja la otra. Si AMBAS deben zapearse, un zap parcial dejaría el ring
                    ' compartido (v30∩v31) → se oculta la mesh entera vía IsOccludedByHeadwear. Face-cull (A)
                    ' cubierto gana sobre todo: pieza entera oculta. Piezas de UNA partición ({30}-only /
                    ' {31}-only) siguen la regla de su único slot (oculto ⟺ ese slot cubierto en el canal).
                    Dim hasBothHairParts As Boolean =
                        (hairSlotMask And BipedSlots.SlotBitHairTop) <> 0UI AndAlso (hairSlotMask And BipedSlots.SlotBitHairLong) <> 0UI
                    Dim zapParts As HairZapParts = HairZapParts.None
                    If hasFaceGenHead Then
                        ' Full-face cull: toda la cabeza tapada → pieza entera oculta, sin zap.
                        occluded = True
                    ElseIf hasBothHairParts Then
                        ' Pieza {30,31}: zap del TOP si slot 30 cubierto en el canal de pelo, zap del LONG si
                        ' slot 31 cubierto. Si ambos → oculta entera (el zap Both dejaría el ring compartido).
                        If hairTopCovered Then zapParts = zapParts Or HairZapParts.Top
                        If hairLongCovered Then zapParts = zapParts Or HairZapParts.Long
                        If zapParts = HairZapParts.Both Then
                            occluded = True
                            zapParts = HairZapParts.None
                        End If
                    Else
                        ' Pieza de una sola partición: oculta ⟺ su único slot está cubierto dentro del canal.
                        occluded = (hairSlotMask And hairCovered) = hairSlotMask
                    End If
                    slotlessCandidate.ZapParts = zapParts
                    ' [HAIRZAP-DIAG] per hair piece: dict mesh, IsHnamExtra, computed mask, occlusion, and
                    ' final ZapParts. Lets us see why a {30,31} piece zaps a partition: which of mask /
                    ' hasBothHairParts / occluded / hairTopCovered / hairLongCovered drives each partition.
                    If Logger.Enabled Then
                        Dim dkD = slotlessCandidate.DictKey
                        Dim hnamD = slotlessCandidate.IsHnamExtra
                        Dim maskD = hairSlotMask
                        Dim bothD = hasBothHairParts
                        Dim occD = occluded
                        Dim htD = hairTopCovered
                        Dim hlD = hairLongCovered
                        Dim occSlotsD = occupiedSlots
                        Dim hairMaskD = hairMask
                        Dim zapD = zapParts
                        Logger.LogLazy(Function() $"[HAIRZAP-DIAG] dict='{dkD}' isHnamExtra={hnamD} hairSlotMask=0x{maskD:X} hasBoth={bothD} occupiedSlots=0x{occSlotsD:X} raceHairMask=0x{hairMaskD:X} topCovered={htD} longCovered={hlD} occluded={occD} -> ZapParts={zapD}")
                    End If
                ElseIf slotlessCandidate.HeadPartType = MainForm.HeadPartTypeHair Then
                    ' Hair (effective type 3) with NO biped segments (hairSlotMask=0): there are no partitions
                    ' to zap per-segment, so the engine whole-node culls it when a covered hair-channel slot has
                    ' no matching segment (Fallout4.exe 0x14064E160 fallback). Checked by EFFECTIVE type and
                    ' BEFORE the addon branch below, so it also catches hair sub-parts that come in as rawType=0
                    ' / HNAM-extras — e.g. KS Hairdos "Aikea" main hair (rawType=3) AND its "AikeaHeadband"
                    ' (rawType=0, effType=3). Both are part of the hair and the helmet hides them together.
                    occluded = (hairCovered <> 0UI) OrElse hasFaceGenHead
                ElseIf slotlessCandidate.IsHnamExtra OrElse slotlessCandidate.HeadPartTypeRaw = 0 Then
                    ' Addon NO-pelo (mouth shadow / eye AO-wet, biped 32): sólo full-face cull lo tapa.
                    occluded = hasFaceGenHead
                Else
                    Select Case slotlessCandidate.HeadPartType
                        Case MainForm.HeadPartTypeFacialHair
                            ' Beard: oculto ⟺ worn cubre el slot de barba (C) O el slot face-cull (A).
                            ' (El antiguo término slot-49 "Mouth" NO es un slot de oclusión del engine.)
                            occluded = (headChannelMask And (facialHairMask Or faceCullMask)) <> 0UI
                        Case 6 ' Eyebrows
                            ' Cejas: oculto ⟺ worn cubre el slot face-cull (A).
                            occluded = (headChannelMask And faceCullMask) <> 0UI
                            ' Type 9 HeadRear: nunca se ocluye por headwear (es base skull geometry).
                    End Select
                End If
                End If
                If occluded Then
                    slotlessCandidate.IsOccludedByHeadwear = True
                End If
            End If
            selected.Add(slotlessCandidate)
        Next

        ' Marcar Skin candidates cuya geometría queda cubierta por algún outfit aceptado.
        ' occupiedSlots acumuló los bits de outfits + extended-underarmors (los Skin se aceptaron
        ' al principio sin contribuir a occupiedSlots). Si la SlotMask del Skin intersecta esos
        ' bits, el outfit lo tapa visualmente → RenderHide=True por default; cuando el usuario
        ' apaga "Render underarmor" se destapa para mostrar el body desnudo abajo.
        For Each skinC In skinCandidates
            If (skinC.SlotMask And occupiedSlots) <> 0UI Then
                skinC.IsCoveredByOutfit = True
            End If
        Next

        Return selected.OrderBy(Function(c) c.Order).ToList()
    End Function

    ''' <summary>Bits {BipedSlots.SlotBitHairTop 0x1 (biped 30), BipedSlots.SlotBitHairLong 0x2 (biped 31)} that the candidate's
    ''' source mesh occupies. Drives the RENDER hair-occlusion rule: a hair piece is hidden ⟺ the headwear
    ''' covers ALL the hair slots the piece occupies (mask ⊆ occupiedSlots) OR is a full-mask (slot 32).
    ''' Reads the mesh NIF from FilesDictionary (same path bake/render use: FilesDictionary_class.GetBytes
    ''' on the normalized DictKey), finds each BSSubIndexTriShape, and unions its segment biped objects via
    ''' <see cref="BSTriShapeGeometry.GetBipedObjects"/>. Works for a hair's main mesh and each hairline
    ''' (every hairline is its own candidate/mesh). Non-hair head parts (mouth shadow / eyes → biped 32)
    ''' return 0. If the mesh can't be read / has no segments → 0 (safe under-hide: show the hair).
    ''' RENDER-ONLY: the bake (FaceGenBuilder) keeps its own CK-faithful biped30only rule.</summary>
    Private Function CandidateHairSlotMask(candidate As MainForm.MeshCandidate) As UInteger
        If candidate Is Nothing Then Return 0UI
        Dim meshKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If String.IsNullOrEmpty(meshKey) Then Return 0UI

        Dim cached As UInteger
        If _candidateHairSlotMaskCache.TryGetValue(meshKey, cached) Then Return cached

        Dim result As UInteger = 0UI
        Try
            Dim bytes = FilesDictionary_class.GetBytes(meshKey)
            If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                Dim nif As New Nifcontent_Class_Manolo()
                nif.Load_Manolo(bytes)
                For Each shp In nif.GetShapes()
                    Dim subIdx = TryCast(shp, BSSubIndexTriShape)
                    If subIdx Is Nothing Then Continue For
                    Dim biped = BSTriShapeGeometry.GetBipedObjects(subIdx)
                    If biped.Contains(30UI) Then result = result Or BipedSlots.SlotBitHairTop
                    If biped.Contains(31UI) Then result = result Or BipedSlots.SlotBitHairLong
                Next
            End If
        Catch ex As Exception
            ' Mesh unreadable / unknown blocks / no segments → 0 (safe under-hide: show the hair).
            result = 0UI
        End Try

        _candidateHairSlotMaskCache(meshKey) = result
        Return result
    End Function

    ''' <summary>Slot-30-relative mask (bit i = biped slot 30+i) of every BSDismemberSkinInstance partition
    ''' across all shapes of the candidate's mesh. Drives the Skyrim head-part occlusion rule: the engine's
    ''' per-partition ApplyOcclusionToGeometry (0x1403C56B0) hides only the partition whose folded slot is
    ''' covered, so we need the mesh's full partition slot set to know whether ANY partition survives the
    ''' worn set. Each partition BodyPart is folded 1xx/2xx → base (v>=200 -> v-200 ; v>=100 -> v-100) and
    ''' accepted only in [30,61]. In Skyrim meshes are BSTriShape/BSDynamicTriShape with a
    ''' BSDismemberSkinInstance (not the FO4 BSSubIndexTriShape that CandidateHairSlotMask reads).
    ''' 0 = no dismember on any shape (ambiguous with "no valid partitions" — both fall back to whole-node
    ''' cull, exactly as the engine's SetAppCulled fallback does). If the mesh can't be read → 0.</summary>
    Private Function CandidatePartitionSlotMask(candidate As MainForm.MeshCandidate) As UInteger
        If candidate Is Nothing Then Return 0UI
        Dim meshKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If String.IsNullOrEmpty(meshKey) Then Return 0UI

        Dim cached As UInteger
        If _candidatePartitionSlotMaskCache.TryGetValue(meshKey, cached) Then Return cached

        Dim result As UInteger = 0UI
        Try
            Dim bytes = FilesDictionary_class.GetBytes(meshKey)
            If bytes IsNot Nothing AndAlso bytes.Length > 0 Then
                Dim nif As New Nifcontent_Class_Manolo()
                nif.Load_Manolo(bytes)
                For Each shp In nif.GetShapes()
                    Dim dism = TryCast(nif.GetBlock(Of NiSkinInstance)(shp.SkinInstanceRef), BSDismemberSkinInstance)
                    If dism Is Nothing OrElse dism.Partitions Is Nothing Then Continue For
                    For Each p In dism.Partitions
                        ' Ley del plegado: BipedSlots.FoldPartitionBodyPart (una sola sede). El filtro
                        ' [30,61] es de ESTE call site, no de la ley — ver su doc.
                        Dim v = BipedSlots.FoldPartitionBodyPart(CInt(p.BodyPart))
                        If v >= 30 AndAlso v <= 61 Then result = result Or (1UI << (v - 30))
                    Next
                Next
            End If
        Catch ex As Exception
            ' Mesh unreadable / unknown blocks → 0 (fallback whole-node cull, matches engine SetAppCulled).
            result = 0UI
        End Try

        _candidatePartitionSlotMaskCache(meshKey) = result
        Return result
    End Function

    ''' <summary>Friend (was Private) so the headless <c>--slot-diag</c> mode (Program.vb) can run the
    ''' REAL classification over Skyrim.esm ARMOs to confirm/verify the game-aware slot mapping without
    ''' rendering. Pure function of (SlotMask, Kind).</summary>
    Friend Shared Function ClassifyShapeCategory(candidate As MainForm.MeshCandidate) As MainForm.ShapeRenderCategory
        If candidate.Kind = MainForm.MeshCandidateKind.HeadPart Then Return MainForm.ShapeRenderCategory.HeadPart

        ' Máscaras derivadas de la TABLA AUTORITATIVA por-juego (BipedSlots.RegionMask, sourced de
        ' los nombres oficiales de biped-object flags del esquema de cada juego — NO heurística).
        ' Aplicar la
        ' semántica FO4 sobre datos SSE clasificaba mal TODO (medido con --slot-diag): armadura de cuerpo
        ' Skyrim (slot 32) → Headwear → auto-oclusión = "armadura oculta"; cuerpo desnudo (32) → Other.
        Dim BODY_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body)
        Dim HAND_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)
        Dim U_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Under)
        Dim A_MASK As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Over)
        Dim HEADWEAR As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Headwear)

        Dim slot = candidate.SlotMask
        Dim touchesBody = (slot And BODY_MASK) <> 0UI
        Dim touchesU = (slot And U_MASK) <> 0UI
        Dim touchesA = (slot And A_MASK) <> 0UI
        Dim touchesHand = (slot And HAND_MASK) <> 0UI
        Dim touchesHeadwear = (slot And HEADWEAR) <> 0UI
        Dim touchesBodyParts = touchesBody OrElse touchesU OrElse touchesA OrElse touchesHand

        ' Headwear: Kind=Outfit con bits exclusivos cabeza/cara/CUELLO (HairTop/HairLong/FaceGenHead/
        ' Headband/Eyes/Beard/Mouth/Neck) y SIN tocar bits del cuerpo. Las prendas de cuello (slot 50,
        ' ej. goggles colgados al cuello) DEBEN ocultarse con "Render headwear" — por eso Neck está en
        ' BipedSlots.HEADWEAR_MASK. Si toca bits cuerpo + cabeza
        ' (raro, ej. casco-cuello combinado) gana la categoría de cuerpo — el toggle headwear no
        ' debería desaparecer una pieza que también cubre torso. Evaluar antes que las otras
        ' porque las otras no chequean bits 16-19 que algunos headwear (Headband) usan en exclusiva.
        If touchesHeadwear AndAlso Not touchesBodyParts AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.Headwear
        ' Body skin desnudo: Kind=Skin con BODY (cubre torso+piernas+pies en FO4 — no hay slot feet).
        If touchesBody AndAlso candidate.Kind = MainForm.MeshCandidateKind.Skin Then Return MainForm.ShapeRenderCategory.BodySkin
        ' Naked hands: Skin con bits hand y sin BODY.
        If touchesHand AndAlso candidate.Kind = MainForm.MeshCandidateKind.Skin Then Return MainForm.ShapeRenderCategory.NakedHands
        ' Underarmor outfit: Kind=Outfit con BODY o [U] (AAClothesCait, fatigues, etc.).
        If (touchesBody OrElse touchesU) AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.Underarmor
        ' Glove de outfit: Outfit con bits hand sin BODY/[U].
        If touchesHand AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.GloveOutfit
        ' [A] puro: declara algún bit [A] sin BODY/[U].
        If touchesA Then Return MainForm.ShapeRenderCategory.ArmorOver
        ' Pipboy (slot 60 / 0x40000000) — accesorio de antebrazo izq. que el engine vanilla
        ' monta hardcoded en el player. Como NPC outfit puede aparecer y debe respetar el toggle
        ' "Render armor". No declara bits [A], por eso lo agrupamos acá explícito. FO4-ONLY: en SSE
        ' slot 60 es un slot modular genérico (sin nombre asignado en el esquema), no un Pipboy →
        ' no forzar ArmorOver.
        If (Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim) _
           AndAlso (slot And BipedSlots.SlotBitPipboy) <> 0UI AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.ArmorOver
        ' SSE — ACCESORIOS Y MOD-SLOTS → categoría ArmorOver, que en Skyrim el toggle rotula
        ' "Render accessories" (RenderToggleLabels). Skyrim NO tiene capa [U]/[A]: el eje real es
        ' "lo que viste" (cuerpo/manos → Underarmor/GloveOutfit, arriba) vs "lo que cuelga": anillo (36),
        ' escudo (39, rígido al antebrazo vía Prn='SHIELD'/ApplyPrnRigidAttach), cola (40) y los
        ' slots modulares sin nombre asignado (44-49 / 52-61: capas, mochilas, SOS…).
        ' Ninguno ocluye piel.
        ' El amuleto (35) NO cae acá: está en la región Headwear (BipedSlots) y lo agarra la regla de
        ' headwear de más arriba, igual que las prendas de cuello de FO4.
        ' Condición: Outfit que no toca NINGÚN bit de cuerpo/manos/headwear pero sí declara algún slot.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim _
           AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit _
           AndAlso Not touchesBodyParts AndAlso Not touchesHeadwear AndAlso slot <> 0UI Then Return MainForm.ShapeRenderCategory.ArmorOver
        ' Resto (accessories 16+ raros, shapes sin slot, etc.).
        Return MainForm.ShapeRenderCategory.Other
    End Function

    ''' <summary>Resuelve el selector de AddonIndex de una ARMO multi-addon: devuelve el INDX a forzar, o
    ''' Nothing = "cargar todos los addons compatibles", que es el default del motor.
    ''' <para>El motor carga TODAS las ARMA del array Models filtradas por raza/genero; la unica forma de
    ''' seleccionar una es la OMOD AddonIndex Property disparada por una combinacion OBTS cuyas keywords
    ''' matcheen el contexto (LVLI.LLKC). Eso distingue el ARMO que empaqueta un set multi-pieza sin keywords
    ''' (torso + guantes, se cargan los dos) del Combat Torso, donde la keyword Heavy fuerza INDX=2.</para>
    ''' <para>BaseAddonIndex (FNAM) NO se usa como filtro: es el default al que apunta el ARMO si nadie lo
    ''' modifica, pero el motor sigue cargando los demas addons salvo override. Ver 23-armor-arma-sculpt.</para></summary>
    Private Function ResolveEffectiveAddonIndex(armo As Canon.ArmoFO4, ctxKeywords As List(Of UInteger)) As Integer?
        ' OBTS combinations override sólo cuando hay keyword match con el contexto.
        If ctxKeywords Is Nothing OrElse ctxKeywords.Count = 0 OrElse armo Is Nothing Then
            Return Nothing
        End If

        Dim effectiveIdx As Integer = -1
        ' El bucle declara la interfaz de FORMA, no la clase generada: los nombres de las listas de
        ' adentro llevan sufijo en la clase (Keywords2, Properties2) y sin sufijo en la interfaz.
        For Each combo As Canon.IBloque_Combinations In armo.Combinations
            If combo.Keywords Is Nothing OrElse combo.Keywords.Count = 0 Then Continue For
            Dim matches = False
            For Each kw In combo.Keywords
                If ctxKeywords.Contains(kw.Keyword) Then
                    matches = True
                    Exit For
                End If
            Next
            If Not matches Then Continue For

            ' Layer 1: la OBTS combination misma puede dictar el AddonIndex via su s16
            ' "Parent Combination Index". -1 = "no override desde la
            ' OBTS, dejar que un OMOD include lo decida". ≥0 = la combination fija el AddonIndex.
            If combo.ObjectModTemplateItemParentCombinationIndex >= 0 Then
                effectiveIdx = combo.ObjectModTemplateItemParentCombinationIndex
            End If

            ' Layer 2: cada OMOD include dentro de la combination puede sobrescribir via su
            ' AddonIndex Property. FunctionType=0 SET (overwrite),
            ' FunctionType=2 ADD (add to running value) — medido: 59 casos SET + 10 casos ADD en el dump
            ' vanilla confirman que ambos existen. Walk ops en orden de declaración del OMOD.
            For Each inc In combo.Includes
                Dim omodRec = _ctx.PluginManager.GetRecord(inc.IncludeMod)
                If omodRec Is Nothing OrElse omodRec.Header.Signature <> "OMOD" Then Continue For
                Dim omod = Canon.CanonRecords.Omod(omodRec, _ctx.PluginManager)
                If omod Is Nothing Then Continue For
                For Each addonOp In AddonIndexOpsDe(omod)
                    Dim opLabel = If(addonOp.IsSet, "SET", "ADD")
                    Dim oldIdx = effectiveIdx
                    If addonOp.IsSet Then
                        effectiveIdx = addonOp.Value
                    Else
                        ' ADD over a still-uninitialized index treats the running base as 0
                        ' (engine convention: ADD without prior SET = absolute value).
                        effectiveIdx = If(effectiveIdx >= 0, effectiveIdx, 0) + addonOp.Value
                    End If
                Next
            Next
        Next

        If effectiveIdx >= 0 Then Return effectiveIdx
        Return Nothing
    End Function

    ''' <summary>Reimplementacion local de OMOD_Data.GetAddonIndexOps del parser viejo: junta todos los
    ''' ops de la Property de indice 7 (AddonIndex), en orden de declaracion, para que el caller los
    ''' pliegue via SET (pisa)/ADD (acumula). AddonIndex viaja siempre en la rama Int de la union
    ''' Value1 — dato de esquema, no una suposicion — asi que alcanza con esa
    ''' rama; una Property de indice 7 con otro ValueType es un record que no cumple el esquema y se
    ''' descarta en vez de adivinar de que rama leer.</summary>
    Private Function AddonIndexOpsDe(omod As Canon.IOmod) As List(Of (Value As Integer, IsSet As Boolean))
        Const AddonIndexProperty As UShort = 7US
        Const ValueTypeInt As Byte = 0 ' Canon.CanonRecords.Omod: Property\Value Type = Int.
        Dim ops As New List(Of (Integer, Boolean))
        For Each prop In omod.Properties
            If prop.[Property] <> AddonIndexProperty Then Continue For
            If prop.PropertyValueType <> ValueTypeInt Then Continue For
            ' Reinterpretar los 4 bytes crudos como Int32 (no convertir el VALOR): AddonIndex puede
            ' ser negativo y el patron de bits tiene que sobrevivir intacto, igual que hacia el
            ' parser viejo yendo y viniendo por un Single.
            Dim asInt = BitConverter.ToInt32(BitConverter.GetBytes(prop.PropertyValue1Int), 0)
            ops.Add((asInt, prop.PropertyFunctionType = 0))
        Next
        Return ops
    End Function

    Private Sub LoadNifShapes(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, loadedNifs As Dictionary(Of String, Nifcontent_Class_Manolo), result As MainForm.PreviewResolutionResult,
                              Optional sculptToApply As List(Of ARMA_BoneScaleDelta) = Nothing,
                              Optional sculptSourceFormID As UInteger = 0)
        Dim dictKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(candidate.DictKey)
        If dictKey = "" Then Return

        Dim bytes = MeshPathHelpers.TryLoadMeshBytes(dictKey)
        If bytes Is Nothing Then Return

        Try
            ' Parse a fresh NIF per candidate. Multi-instance robot chunks (Mr Handy 3 arms,
            ' 3 eyes) point to the same DictKey but each render-instance must own its own
            ' NIF + IRenderableShape so per-shape mutations (sculpt, morph, GPU upload) don't
            ' bleed across instances.
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            Dim trackChunkNif = candidate.ChunkOmodFormID <> 0UI
            Dim trackCandidateNif = trackChunkNif OrElse candidate.SlotMask = BipedSlots.SlotBitPipboy
            If trackChunkNif Then
                ' Keep one representative parsed NIF per DictKey only for chunk-mount consumers.
                loadedNifs(dictKey) = nif
            End If
            If trackCandidateNif Then
                ' Track the candidate↔NIF link only for paths that need the exact instance
                ' downstream (chunk mounting / pipboy synthetic skin).
                result.CandidateNif(candidate) = nif
            End If

            Dim shapes = NifRenderableShape.FromNif(nif)
            Dim logEnabled = Logger.Enabled

            ' De-dup de skin en SSE por pertenencia BOD2 (regla record-faithful, NO réplica del motor): una
            ' ARMA de piel desnuda renderiza sólo las shapes cuya partición cae dentro del slot que declara su
            ' PROPIO BOD2. Una shape enteramente fuera de él es reutilización de asset — ya la aporta la ARMA
            ' dedicada a ese slot, o la cabeza FaceGen — y se descarta.
            ' Existe porque algunas mallas de piel vanilla son bundles all-in-one: la de pies de niño trae
            ' además cuerpo, manos y cabeza, y el override de skin-TXST terminaba pintando la textura de los
            ' pies sobre esa cabeza, haciendo z-fighting con la FaceGen.
            ' EL GATE Kind=Skin ES ESENCIAL: "partición fuera del BOD2" es común y LEGÍTIMO en ropa y
            ' armadura (botas con geometría de pierna, etc.), así que aplicar la regla ahí descartaría shapes
            ' válidas. Además es SSE-only y nunca descarta si el mesh no se puede leer (keep-on-doubt).
            Const EnableSseSkinPartitionDedup As Boolean = True
            If EnableSseSkinPartitionDedup AndAlso Config_App.Current IsNot Nothing _
               AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim _
               AndAlso candidate IsNot Nothing AndAlso candidate.Kind = MainForm.MeshCandidateKind.Skin _
               AndAlso candidate.ArmaOwnSlotMask <> 0UI Then
                ' Key on the ARMA's OWN footprint (ArmaOwnSlotMask), NOT candidate.SlotMask — the latter is
                ' OR'd with the owning ARMO's head-occlusion bits (SkinNaked declares slot 30), which would
                ' credit a Feet(37) candidate slot 30 and wrongly KEEP childfeet's EyesChild (partition 30).
                Dim armaSlots As New HashSet(Of Integer)
                For bit = 0 To 31
                    If (candidate.ArmaOwnSlotMask And (1UI << bit)) <> 0UI Then armaSlots.Add(30 + bit)
                Next
                Dim kept As New List(Of NifRenderableShape)
                For Each sh In shapes
                    Dim parts As New List(Of Integer)
                    Try
                        Dim shp = sh.NifShape
                        Dim sir = If(shp IsNot Nothing, shp.SkinInstanceRef, Nothing)
                        If sir IsNot Nothing AndAlso sir.Index >= 0 Then
                            Dim dism = TryCast(nif.Blocks(sir.Index), NiflySharp.Blocks.BSDismemberSkinInstance)
                            If dism IsNot Nothing AndAlso dism.Partitions IsNot Nothing Then
                                For Each p In dism.Partitions
                                    ' Ley del plegado: BipedSlots.FoldPartitionBodyPart (una sola sede).
                                    ' ACÁ NO SE FILTRA [30,61], y NO es un olvido: abajo `parts.Count = 0`
                                    ' significa "no clasificable ⇒ conservar la shape". Filtrar convertiría
                                    ' una malla con todas sus particiones fuera de rango de DESCARTADA a
                                    ' RENDERIZADA. Ver la doc de FoldPartitionBodyPart.
                                    parts.Add(BipedSlots.FoldPartitionBodyPart(CInt(p.BodyPart)))
                                Next
                            End If
                        End If
                    Catch
                        ' Unreadable dismember → treat as no partitions (keep the shape; never drop on error).
                    End Try
                    ' Keep if the shape has no dismember (can't classify → don't drop) or ANY partition is a slot this ARMA owns.
                    Dim keep As Boolean = (parts.Count = 0) OrElse parts.Any(Function(pp) armaSlots.Contains(pp))
                    If keep Then
                        kept.Add(sh)
                    ElseIf Logger.Enabled Then
                        Dim shN = sh.ShapeName
                        Dim pj = String.Join(",", parts)
                        Dim sj = String.Join(",", armaSlots)
                        Logger.LogLazy(Function() $"[SSE-SKIN-DEDUP] drop shape='{shN}' parts=[{pj}] not in ARMA own-slots [{sj}] (bundled out-of-slot geometry, provided by the dedicated ARMA / FaceGen head).")
                    End If
                Next
                shapes = kept
            End If

            ' Multi-instance bone rename: chunks robot mounteados en P-X|<apIdx> traen
            ' bone references al set |0 nativo del NIF. Cuando MountApIdx > 0, hay que
            ' redirigir los bone references al set |<apIdx> del skeleton del actor (los 3
            ' sets |0/|1/|2 ya existen en el skeleton — verificado en log [SKEL-PRE]).
            ' Mutamos NiNode.Name.String solo de los bones referenciados por los shapes,
            ' sin tocar el resto del NIF. Reescritura quirúrgica per-instancia.
            If candidate.ChunkOmodFormID <> 0UI AndAlso candidate.MountApIdx > 0 Then
                RenameShapeBoneIndices(shapes, candidate.MountApIdx)
                ' Los sub-sockets que esta chunk NIF expone también necesitan rename del ParentBoneName,
                ' sino sub-chunks se anclan al bone |0 equivocado.
                RenameSubSocketParentBones(nif, candidate.MountApIdx)
            End If

            ' Los shapes skinned a bones del ACTOR (PackBase brahmin: Pelvis/Spine; brazos Mr Handy) NO
            ' necesitan el MountSocket — cabalgan los bones del actor que YA están posicionados. Los bones
            ' PRIVADOS del chunk (lag bones, etc.) los coloca InjectChunkBonesIntoLiveSkeleton (regla:
            ' A=actorWorld(huesoCompartido)×bind; privados en A×inv(bind) — ver memoria
            ' 24-robots-huesos-inyectados; brahmin validado sin regresión).
            ' Por eso NO se aplica el socket a los bind transforms de estos shapes: aplicarlo a shapes
            ' que cabalgan el actor DISTORSIONA (verificado: invertir el orden tampoco lo arregla).
            ' NO re-habilitar. (Pendiente: validar visualmente Mr Handy/Codsworth multi-instancia.)

            If logEnabled Then
                Dim candFidLog = candidate.SourceFormID
                Dim chunkOmodLog = candidate.ChunkOmodFormID
                Dim dkLog = dictKey
                Dim shapesCountLog = shapes.Count
                Dim nifHashLog = nif.GetHashCode()
                Logger.LogLazy(Function() $"[LOAD-NIF] candFid=0x{candFidLog:X8} chunkOmod=0x{chunkOmodLog:X8} dictKey='{dkLog}' shapes={shapesCountLog} nifHash={nifHashLog}")
            End If

            ' [PIPBOY-DIAG] Para candidates con bit Pipboy (slot 60 / 0x40000000), dump per-shape
            ' IsSkinned + lista de BSConnectPoint::Parents del NIF. Si IsSkinned=False y hay un
            ' parent socket (típicamente "P-PipBoy" en LArm_skin del esqueleto), el render debería
            ' anclar el mesh a ese socket; si IsSkinned=True y la pose del actor es la default,
            ' el mesh debería seguir al bone correspondiente. "Pipboy en el suelo" puede ser:
            '   a) no skinned + sin parent socket → mesh queda en world-origin de su NIF.
            '   b) skinned a bones que el esqueleto del actor no tiene → SSBO bone matrices
            '      colapsan al origin.
            '   c) socket declarado pero el chunk-mount resolver no lo aplica (sólo lo hace para
            '      candidates con ChunkOmodFormID; outfits regulares no pasan por mount-resolver).
            If logEnabled AndAlso (candidate.SlotMask And BipedSlots.SlotBitPipboy) <> 0UI Then
                Dim dkLog = dictKey
                Dim shapesCountLog = shapes.Count
                Dim slotL = candidate.SlotMask.ToString("X8")
                Dim armoL = candidate.SourceFormID
                Dim armaL = candidate.ArmorAddonFormID
                Logger.LogLazy(Function() $"[PIPBOY-DIAG] candidate ARMO=0x{armoL:X8} ARMA=0x{armaL:X8} slot=0x{slotL} mesh='{dkLog}' shapes={shapesCountLog}")
                For Each sh In shapes
                    Dim shName = If(sh.ShapeName, "")
                    Dim isSk = sh.IsSkinned
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   shape='{shName}' IsSkinned={isSk}")
                Next
                Try
                    Dim parents = BSConnectPointReader.ReadParents(nif)
                    If parents Is Nothing OrElse parents.Count = 0 Then
                        Logger.LogLazy(Function() "[PIPBOY-DIAG]   BSConnectPoint::Parents = (none declared in NIF)")
                    Else
                        For Each p In parents
                            Dim pn = p.Name
                            Dim parn = p.ParentBoneName
                            Dim pt = p.Translation
                            Logger.LogLazy(Function() $"[PIPBOY-DIAG]   ConnectPointParent name='{pn}' parentBone='{parn}' T=({pt.X:F3},{pt.Y:F3},{pt.Z:F3})")
                        Next
                    End If
                Catch ex As Exception
                    Dim msg = ex.Message
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   BSConnectPoint::Parents READ EXCEPTION: {msg}")
                End Try
                Try
                    Dim children = BSConnectPointReader.ReadChildren(nif)
                    If children.PointNames Is Nothing OrElse children.PointNames.Count = 0 Then
                        Logger.LogLazy(Function() "[PIPBOY-DIAG]   BSConnectPoint::Children = (none declared in NIF)")
                    Else
                        Dim skFlag = children.Skinned
                        Dim pointsStr = String.Join(",", children.PointNames)
                        Logger.LogLazy(Function() $"[PIPBOY-DIAG]   ConnectPointChildren skinnedFlag={skFlag} points=[{pointsStr}]")
                    End If
                Catch ex As Exception
                    Dim msg = ex.Message
                    Logger.LogLazy(Function() $"[PIPBOY-DIAG]   BSConnectPoint::Children READ EXCEPTION: {msg}")
                End Try
            End If

            ' Diagnostic: dump the raw shader of every shape STRAIGHT FROM THE NIF, before any
            ' material copy or override runs. Lets us see whether the engine's _faceBones variant
            ' carries FaceTint shaders or genérico Default — answers the Ghoul question (why does
            ' TryApplyFaceTints find no FaceTint mesh after load).
            If logEnabled Then
                For Each shape In shapes
                    MaterialResolver.EnsureShapeMaterialResolved(shape)
                    Dim rawMatPath As String = ""
                    Dim rawAT As String = "?"
                    Dim rawATRef As String = "?"
                    Dim rawABM As String = "?"
                    Dim shapeMat = shape.ShapeMaterial
                    If shapeMat IsNot Nothing Then
                        rawMatPath = If(shapeMat.path, "")
                        If shapeMat.material IsNot Nothing Then
                            rawAT = shapeMat.material.AlphaTest.ToString()
                            rawATRef = shapeMat.material.AlphaTestRef.ToString()
                            rawABM = shapeMat.material.AlphaBlendMode.ToString()
                        End If
                    End If
                    Dim rawHasNiAlp As String = "?"
                    If shape.NifShape IsNot Nothing AndAlso shape.NifShape.AlphaPropertyRef IsNot Nothing Then
                        rawHasNiAlp = (shape.NifShape.AlphaPropertyRef.Index <> -1).ToString()
                    End If
                    Dim shapeNameLog = shape.ShapeName
                    Dim rawAtLog = rawAT
                    Dim rawAtRefLog = rawATRef
                    Dim rawAbmLog = rawABM
                    Dim rawHasNiAlpLog = rawHasNiAlp
                    Dim rawPathLog = rawMatPath
                    Logger.LogLazy(Function() $"[ALPHA-PRE] shape='{shapeNameLog}' path='{rawPathLog}' AT={rawAtLog} ATRef={rawAtRefLog} ABM={rawAbmLog} hasNiAlp={rawHasNiAlpLog}")
                Next
            End If

            _materialResolver.ApplyShapeMaterialOverrides(candidate, state, shapes)

            ' Diagnostic: dump the shader AFTER ApplyShapeMaterialOverrides. Pairing with the
            ' [NIF-LOAD-RAW] above lets us see if the pass mutated the shader type.
            If logEnabled Then
                For Each shape In shapes
                    Dim postPath As String = ""
                    Dim postAT As String = "?"
                    Dim postATRef As String = "?"
                    Dim postABM As String = "?"
                    Dim shapeMat2 = shape.ShapeMaterial
                    If shapeMat2 IsNot Nothing Then
                        postPath = If(shapeMat2.path, "")
                        If shapeMat2.material IsNot Nothing Then
                            postAT = shapeMat2.material.AlphaTest.ToString()
                            postATRef = shapeMat2.material.AlphaTestRef.ToString()
                            postABM = shapeMat2.material.AlphaBlendMode.ToString()
                        End If
                    End If
                    Dim postHasNiAlp As String = "?"
                    If shape.NifShape IsNot Nothing AndAlso shape.NifShape.AlphaPropertyRef IsNot Nothing Then
                        postHasNiAlp = (shape.NifShape.AlphaPropertyRef.Index <> -1).ToString()
                    End If
                    Dim shapeNameLog2 = shape.ShapeName
                    Dim postPathLog = postPath
                    Dim postAtLog = postAT
                    Dim postAtRefLog = postATRef
                    Dim postAbmLog = postABM
                    Dim postHasNiAlpLog = postHasNiAlp
                    Logger.LogLazy(Function() $"[ALPHA-POST] shape='{shapeNameLog2}' path='{postPathLog}' AT={postAtLog} ATRef={postAtRefLog} ABM={postAbmLog} hasNiAlp={postHasNiAlpLog}")
                Next
            End If

            ' Convert the externally-determined sculpt-to-apply (per the slot-based rule
            ' computed in ResolvePreviewVariant) to a Dict(boneName -> Vec3). This is NOT the
            ' candidate's own ArmaBoneScaleDeltas — it's whatever sculpt SOURCE applies to this
            ' candidate's shapes (could be a slot-33 BODY underarmor's SCLP, a [U] piece's SCLP
            ' if the shape covers the matching [A] slot, or Nothing if rule says no scaling).
            Dim armaSculptDict As Dictionary(Of String, System.Numerics.Vector3) = Nothing
            If sculptToApply IsNot Nothing AndAlso sculptToApply.Count > 0 Then
                armaSculptDict = New Dictionary(Of String, System.Numerics.Vector3)(StringComparer.OrdinalIgnoreCase)
                For Each bd In sculptToApply
                    armaSculptDict(bd.BoneName) = New System.Numerics.Vector3(bd.DeltaX, bd.DeltaY, bd.DeltaZ)
                Next
            End If

            ' Compute render category once per candidate (igual para todos sus shapes).
            Dim category As MainForm.ShapeRenderCategory = ClassifyShapeCategory(candidate)

            ' Track shape -> dict key for TRI lookup, plus explicit HDPT TRI paths if present.
            ' Also: shape -> sculpt source FormID + shape -> sculpt deltas (for per-skeleton sculpt).
            ' ShapeArmaFormID is the FormID of the SCULPT SOURCE (not the candidate's own ARMA),
            ' so that shapes from different candidates pointing to the same source share a skeleton.
            ' One occlusion group id per candidate (LoadNifShapes runs once per candidate): all of this
            ' candidate's shapes share it so an item never occludes its own segments. See ShapeSlotGroup.
            Dim occGroupId As Integer = result.OcclusionGroupSeq
            result.OcclusionGroupSeq += 1
            For Each shape In shapes
                result.MeshDictKeys(shape) = dictKey
                ' Camino head-bake: el `_faceBones` NO se dibuja, viaja como insumo por shape.
                If candidate.FaceBonesDictKey <> "" Then result.ShapeFaceBonesKeys(shape) = candidate.FaceBonesDictKey
                result.ShapeArmaFormID(shape) = sculptSourceFormID
                result.ShapeCategory(shape) = category
                result.ShapeCoveredByOutfit(shape) = candidate.IsCoveredByOutfit
                result.ShapeOccludedByHeadwear(shape) = candidate.IsOccludedByHeadwear
                result.ShapeZapHairParts(shape) = candidate.ZapParts
                ' Oclusion por slot equipado, per-segmento: se guardan los inputs que ApplyRenderToggleVisibility
                ' usa para reconstruir CoveredSlotsMask en cada render. Solo aportan los items vestidos
                ' (Kind=Outfit), con su propia mascara de slots + un group id; head parts y piel no guardan nada.
                ' El recompute deriva: mascara de head part = (OR de los slots de los items renderizados) AND
                ' HeadOcclusionMask (los slots de la region de cabeza que declara la RACE; el slot 33 NECK nunca
                ' esta, asi que la costura cabeza-cuerpo no se rompe), gateada por Render headwear; mascara de
                ' item = OR de los slots de los OTROS grupos renderizados (el group id excluye las shapes
                ' propias, asi el slot 60 del Pipboy sigue tapando el antebrazo del outfit).
                ' Recomputar desde el subconjunto renderizado -y no hornear una mascara estatica aca- es lo que
                ' permite que un toggle que esconde un item destape sus segmentos.
                If candidate.Kind = MainForm.MeshCandidateKind.Outfit Then
                    result.ShapeOwnSlots(shape) = candidate.SlotMask
                    result.ShapeArmaOwnSlots(shape) = candidate.ArmaOwnSlotMask
                    ' DNAM priority del ARMA (gender-resuelto). SSE: desempata quién POSEE un slot compartido
                    ' para la oclusión per-partición por-dueño (fase 1 de 0x140218200, owner en entry+0x18).
                    result.ShapePriority(shape) = candidate.Priority
                    result.ShapeSlotGroup(shape) = occGroupId
                    ' Identidad de FORM del Pipboy (motor: compara el form contra los default objects
                    ' PipboyCleanObject_DO / PipboyDustyObject_DO, VA 0x1400F18B0 / 0x1400F18F0). NO se
                    ' deduce del slot: 3 de los 7 ARMO vanilla con slot-60-solo NO son Pipboys.
                    result.ShapeIsPipboyDevice(shape) = _ctx.PipboyDeviceArmoFormIDs().Contains(candidate.SourceFormID)
                End If
                result.ShapeUsesBodyTexture(shape) = candidate.UsesBodyTexture
                ' HDPT type=7 Meatcaps (CK enum 7=Meatcaps, ver comment en
                ' CollectHeadPartCandidate). Confirmed por estar en enum oficial de Bethesda;
                ' mismo nivel de certeza que BSDismemberBodyPartType SECTIONCAP/TORSOCAP. La
                ' clasificación por geometría (ClassifyShapeMeatcap) corre después en el loop
                ' de renderData.Shapes y puede sobreescribir esto si la shape ALSO tiene sub-
                ' segments meatcap — no es un problema porque ambos se gobiernan por el mismo
                ' toggle, solo cambia el log.
                If candidate.Hide Then
                    result.ShapeMeatcap(shape) = MainForm.MeatcapClassification.Confirmed
                End If
                If armaSculptDict IsNot Nothing Then
                    result.ShapeArmaSculpt(shape) = armaSculptDict
                End If
                If candidate.Kind = MainForm.MeshCandidateKind.HeadPart Then
                    If Not String.IsNullOrEmpty(candidate.ChargenMorphTriPath) Then
                        result.ShapeChargenTriPaths(shape) = candidate.ChargenMorphTriPath
                    End If
                    If Not String.IsNullOrEmpty(candidate.RaceMorphTriPath) Then
                        result.ShapeRaceMorphTriPaths(shape) = candidate.RaceMorphTriPath
                    End If
                    If Not String.IsNullOrEmpty(candidate.MeshMorphTriPath) Then
                        result.ShapeMeshMorphTriPaths(shape) = candidate.MeshMorphTriPath
                    End If
                End If
            Next

            ' DIAG: dump shape properties for chunk candidates (multi-instance debug).
            ' We want to verify what bone names the shape ACTUALLY references and if there's
            ' any anchor/transform info we're not consuming. Goal: figure out if the shape
            ' carries '|N' suffix already, or if engine adds it via something else.
            If logEnabled AndAlso candidate.ChunkOmodFormID <> 0UI Then
                Dim cFid = candidate.ChunkOmodFormID
                Dim apIdx = candidate.MountApIdx
                Dim sock = candidate.MountSocket
                Dim sockDesc As String
                If sock IsNot Nothing Then
                    Dim qx = sock.Rotation.X, qy = sock.Rotation.Y, qz = sock.Rotation.Z, qw = sock.Rotation.W
                    sockDesc = $"name='{sock.Name}' parentBone='{sock.ParentBoneName}' T=({sock.Translation.X:F2},{sock.Translation.Y:F2},{sock.Translation.Z:F2}) Quat(x,y,z,w)=({qx:F4},{qy:F4},{qz:F4},{qw:F4}) S={sock.Scale:F3}"
                Else
                    sockDesc = "Nothing"
                End If
                Dim rootNode = nif.GetRootNode()
                Dim rootDesc As String
                Dim rootIsIdentity As Boolean = False
                If rootNode IsNot Nothing Then
                    Dim r = rootNode.Rotation
                    Dim rt = rootNode.Translation
                    Dim rs = rootNode.Scale
                    Const eps As Single = 0.0001F
                    rootIsIdentity = (Math.Abs(rt.X) < eps AndAlso Math.Abs(rt.Y) < eps AndAlso Math.Abs(rt.Z) < eps AndAlso
                                      Math.Abs(rs - 1.0F) < eps AndAlso
                                      Math.Abs(r.M11 - 1.0F) < eps AndAlso Math.Abs(r.M12) < eps AndAlso Math.Abs(r.M13) < eps AndAlso
                                      Math.Abs(r.M21) < eps AndAlso Math.Abs(r.M22 - 1.0F) < eps AndAlso Math.Abs(r.M23) < eps AndAlso
                                      Math.Abs(r.M31) < eps AndAlso Math.Abs(r.M32) < eps AndAlso Math.Abs(r.M33 - 1.0F) < eps)
                    Dim idTag = If(rootIsIdentity, "IDENTITY", "NON-IDENTITY")
                    rootDesc = $"name='{rootNode.Name?.String}' {idTag} T=({rt.X:F4},{rt.Y:F4},{rt.Z:F4}) S={rs:F4} R=[{r.M11:F4},{r.M12:F4},{r.M13:F4} | {r.M21:F4},{r.M22:F4},{r.M23:F4} | {r.M31:F4},{r.M32:F4},{r.M33:F4}]"
                Else
                    rootDesc = "Nothing"
                End If
                Logger.LogLazy(Function() $"[CHUNK-PROP] omod=0x{cFid:X8} apIdx={apIdx} socket={sockDesc}")
                Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root: {rootDesc}")

                ' [DIAG-ROOT] NIF root global = walk hacia arriba desde root (es solo root.local).
                ' Para chunks con root NON-IDENTITY este es exactamente el transform que el render
                ' está IGNORANDO (SkinningHelper:151-156 fuerza GlobalTransform=Identity para skinned).
                If rootNode IsNot Nothing Then
                    Try
                        Dim rootGlobal = Transform_Class.GetGlobalTransform(rootNode, nif)
                        Dim rg = rootGlobal.Rotation
                        Dim rgt = rootGlobal.Translation
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root.computedGlobal: T=({rgt.X:F4},{rgt.Y:F4},{rgt.Z:F4}) S={rootGlobal.Scale:F4} R=[{rg.M11:F4},{rg.M12:F4},{rg.M13:F4} | {rg.M21:F4},{rg.M22:F4},{rg.M23:F4} | {rg.M31:F4},{rg.M32:F4},{rg.M33:F4}]")
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif.root.computedGlobal EXCEPTION: {ex.Message}")
                    End Try
                End If

                ' DIAG sub-sockets/children: dump BSConnectPoint::Parents (sub-sockets que el chunk
                ' EXPONE para que otro chunk se monte encima — ej. HandLeftProtectronClaw expone
                ' P-ModHandLeftProtectronArmor donde se mountea el armor) y BSConnectPoint::Children
                ' (lo que el chunk consume — el "C-X" que matchea contra algún P-X del host o de
                ' otro chunk previo). Sin estos datos el lookup AP→socket por strings es ciego.
                Try
                    Dim subSockets = BSConnectPointReader.ReadParents(nif)
                    If subSockets IsNot Nothing AndAlso subSockets.Count > 0 Then
                        Dim subSocketNames = String.Join(", ", subSockets.Select(Function(s) $"'{s.Name}'(parent='{s.ParentBoneName}')"))
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif EXPOSES sub-sockets({subSockets.Count}): [{subSocketNames}]")
                    Else
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif EXPOSES sub-sockets(0)")
                    End If
                Catch
                End Try
                Try
                    Dim children = BSConnectPointReader.ReadChildrenNames(nif)
                    If children IsNot Nothing AndAlso children.Count > 0 Then
                        Dim childList = String.Join(", ", children.Select(Function(c) $"'{c}'"))
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif CONSUMES children({children.Count}): [{childList}]")
                    Else
                        Logger.LogLazy(Function() $"[CHUNK-PROP]   nif CONSUMES children(0)")
                    End If
                Catch
                End Try

                For Each shape In shapes
                    Dim sh = shape
                    Dim shapeName = sh.ShapeName
                    Dim niShape = sh.NifShape
                    Dim niShapeT = "<no-transform>"
                    Dim ts = TryCast(niShape, NiflySharp.Blocks.NiAVObject)
                    If ts IsNot Nothing Then
                        Dim r = ts.Rotation
                        niShapeT = $"T=({ts.Translation.X:F2},{ts.Translation.Y:F2},{ts.Translation.Z:F2}) S={ts.Scale:F3} R=[{r.M11:F3},{r.M12:F3},{r.M13:F3} | {r.M21:F3},{r.M22:F3},{r.M23:F3} | {r.M31:F3},{r.M32:F3},{r.M33:F3}]"
                    End If

                    ' [DIAG-CHAIN] Cadena del shape NiAVObject hacia el root, con cada local.
                    ' Aporta info sobre intermedios entre shape y root (no son raros — Bethesda
                    ' a veces mete NiNodes wrapper con offsets). El render skinned actualmente
                    ' compone esta cadena y la fuerza a Identity (SkinningHelper:151-156).
                    Try
                        Dim curNode = TryCast(niShape, NiflySharp.Blocks.NiAVObject)
                        Dim depth As Integer = 0
                        While curNode IsNot Nothing
                            Dim cn = curNode
                            Dim cName = If(cn.Name?.String, "<null>")
                            Dim cT = cn.Translation
                            Dim cR = cn.Rotation
                            Dim cS = cn.Scale
                            Dim isRoot = (rootNode IsNot Nothing AndAlso ReferenceEquals(cn, rootNode))
                            Dim d = depth, isRootCap = isRoot, cNameCap = cName, cTcap = cT, cRcap = cR, cScap = cS
                            Const eps As Single = 0.0001F
                            Dim cIsId = (Math.Abs(cT.X) < eps AndAlso Math.Abs(cT.Y) < eps AndAlso Math.Abs(cT.Z) < eps AndAlso
                                         Math.Abs(cS - 1.0F) < eps AndAlso
                                         Math.Abs(cR.M11 - 1.0F) < eps AndAlso Math.Abs(cR.M12) < eps AndAlso Math.Abs(cR.M13) < eps AndAlso
                                         Math.Abs(cR.M21) < eps AndAlso Math.Abs(cR.M22 - 1.0F) < eps AndAlso Math.Abs(cR.M23) < eps AndAlso
                                         Math.Abs(cR.M31) < eps AndAlso Math.Abs(cR.M32) < eps AndAlso Math.Abs(cR.M33 - 1.0F) < eps)
                            Dim cIdTag = If(cIsId, "ID", "NON-ID")
                            Logger.LogLazy(Function() $"[CHUNK-PROP]     shape-chain[{d}] '{cNameCap}'{If(isRootCap, " (ROOT)", "")} {cIdTag} T=({cTcap.X:F4},{cTcap.Y:F4},{cTcap.Z:F4}) S={cScap:F4} R=[{cRcap.M11:F4},{cRcap.M12:F4},{cRcap.M13:F4}|{cRcap.M21:F4},{cRcap.M22:F4},{cRcap.M23:F4}|{cRcap.M31:F4},{cRcap.M32:F4},{cRcap.M33:F4}]")
                            If isRoot Then Exit While
                            curNode = TryCast(nif.GetParentNode(curNode), NiflySharp.Blocks.NiAVObject)
                            depth += 1
                            If depth > 20 Then Exit While
                        End While
                    Catch ex As Exception
                        Logger.LogLazy(Function() $"[CHUNK-PROP]     shape-chain EXCEPTION: {ex.Message}")
                    End Try

                    Dim boneNames As New List(Of String)
                    If sh.ShapeBones IsNot Nothing Then
                        For Each bn In sh.ShapeBones
                            Dim niN = TryCast(bn, NiflySharp.Blocks.NiNode)
                            boneNames.Add(If(niN?.Name?.String, "<null>"))
                        Next
                    End If
                    Dim boneNamesStr = String.Join(", ", boneNames)

                    Dim firstBindStr = "<no-bind>"
                    If sh.ShapeBoneTransforms IsNot Nothing AndAlso sh.ShapeBoneTransforms.Count > 0 Then
                        Dim firstBind = sh.ShapeBoneTransforms(0)
                        Dim fr = firstBind.Rotation
                        firstBindStr = $"T=({firstBind.Translation.X:F2},{firstBind.Translation.Y:F2},{firstBind.Translation.Z:F2}) S={firstBind.Scale:F3} R=[{fr.M11:F3},{fr.M12:F3},{fr.M13:F3} | {fr.M21:F3},{fr.M22:F3},{fr.M23:F3} | {fr.M31:F3},{fr.M32:F3},{fr.M33:F3}]"
                    End If

                    Logger.LogLazy(Function() $"[CHUNK-PROP]   shape='{shapeName}' niShape:{niShapeT}")
                    Logger.LogLazy(Function() $"[CHUNK-PROP]     ShapeBones({boneNames.Count})=[{boneNamesStr}]")
                    Logger.LogLazy(Function() $"[CHUNK-PROP]     firstBind={firstBindStr}")

                    ' All bind transforms — para multi-instance shape igual, podemos comparar
                    ' bind matrices a ver si difieren entre instancias (no deberían si vienen
                    ' del mismo NIF, pero confirmamos contra evidencia).
                    If sh.ShapeBoneTransforms IsNot Nothing Then
                        For i = 0 To sh.ShapeBoneTransforms.Count - 1
                            Dim bind = sh.ShapeBoneTransforms(i)
                            Dim boneNameLog = If(i < boneNames.Count, boneNames(i), $"<idx{i}>")
                            Dim br = bind.Rotation
                            Dim idxLog = i
                            Dim btDescLog = $"T=({bind.Translation.X:F2},{bind.Translation.Y:F2},{bind.Translation.Z:F2}) S={bind.Scale:F3} R=[{br.M11:F3},{br.M12:F3},{br.M13:F3}|{br.M21:F3},{br.M22:F3},{br.M23:F3}|{br.M31:F3},{br.M32:F3},{br.M33:F3}]"
                            Logger.LogLazy(Function() $"[CHUNK-PROP]     bind[{idxLog}] bone='{boneNameLog}' {btDescLog}")
                        Next
                    End If
                Next
            End If

            result.Shapes.AddRange(shapes)
            For Each sh In shapes
                If sh IsNot Nothing Then result.ShapeCandidate(sh) = candidate
            Next
        Catch ex As Exception
            ' ESTE Try ABARCA ~470 LÍNEAS Y SU ÚLTIMA SENTENCIA ES LA PUBLICACIÓN. Cualquier excepción
            ' en el medio deja el candidato con CERO shapes, que es indistinguible de "esta pieza no tiene
            ' malla" — el NPC se hornea sin la pieza y nadie se entera. Este mismo archivo ya loguea esta
            ' clase de fallo en otros dos sitios; acá faltaba.
            ' El ToString va con su propia red: esto corre DENTRO de un Catch de un metodo cuyo diseño
            ' entero es tragar y seguir. Una excepcion acá escaparia de ese contrato.
            Dim nm As String
            Try : nm = If(candidate Is Nothing, "<nothing>", candidate.ToString()) : Catch : nm = "<?>" : End Try
            Dim m = ex.GetType().Name & ": " & ex.Message
            Logger.LogLazy(Function() $"[MESH-COLLECT] el candidato '{nm}' quedó SIN shapes por una excepción: {m}")
        End Try
    End Sub

    ''' <summary>Reescribe el sufijo |N de los bone names referenciados por los shapes,
    ''' redirigiendo del set |0 nativo al set |&lt;apIdx&gt; del skeleton. Aplicado per-instancia
    ''' antes del render para que el skinning resuelva contra los bones correctos del actor.
    '''
    ''' Reescritura quirúrgica: solo NiNode.Name.String de bones presentes en ShapeBones.
    ''' No toca el resto del NIF (extra data, anim controllers, etc.). El NIF está clonado
    ''' por candidate (LoadNifShapes parsea fresh), así que mutar nombres no afecta otras
    ''' instancias del mismo path.</summary>
    Private Sub RenameShapeBoneIndices(shapes As IEnumerable(Of IRenderableShape), apIdx As Byte)
        If shapes Is Nothing OrElse apIdx = 0 Then Return
        Dim newSuffix = "|" & apIdx.ToString()
        For Each shape In shapes
            If shape Is Nothing OrElse shape.ShapeBones Is Nothing Then Continue For
            For Each bn In shape.ShapeBones
                Dim niNode = TryCast(bn, NiflySharp.Blocks.NiNode)
                If niNode Is Nothing OrElse niNode.Name Is Nothing Then Continue For
                Dim s = niNode.Name.String
                If String.IsNullOrEmpty(s) Then Continue For
                If s.EndsWith("|0", StringComparison.Ordinal) Then
                    Dim renamed = String.Concat(s.AsSpan(0, s.Length - 2), newSuffix)
                    niNode.Name.String = renamed
                    Dim sLog = s
                    Dim renamedLog = renamed
                    Logger.LogLazy(Function() $"[BONE-RENAME] '{sLog}' → '{renamedLog}'")
                End If
            Next
        Next
    End Sub

    ''' <summary>Cuando un chunk multi-instance (MountApIdx > 0) tiene sus shape bones renombrados
    ''' de `Bone|0` a `Bone|N`, los sub-sockets BSConnectPoint::Parents que esa chunk NIF expone
    ''' siguen apuntando a `Bone|0` en su `ParentBoneName` literal — esto hace que sub-chunks que
    ''' se mounten sobre el chunk parent terminen anclados al bone |0 en vez del |N correcto.
    ''' Remap el ParentBoneName de cada sub-socket en la misma sufijo |N que los shape bones.</summary>
    Private Sub RenameSubSocketParentBones(nif As Nifcontent_Class_Manolo, apIdx As Byte)
        If nif Is Nothing OrElse apIdx = 0 Then Return
        Dim root = nif.GetRootNode()
        If root Is Nothing OrElse root.ExtraDataList Is Nothing Then Return
        Dim newSuffix = "|" & apIdx.ToString()
        For Each ref In root.ExtraDataList.References
            Dim block = nif.Blocks(ref.Index)
            Dim parents = TryCast(block, NiflySharp.Blocks.BSConnectPoint_Parents)
            If parents Is Nothing OrElse parents.ConnectPoints Is Nothing Then Continue For
            For Each cp In parents.ConnectPoints
                If cp.Parent Is Nothing Then Continue For
                Dim s = cp.Parent.Content
                If String.IsNullOrEmpty(s) Then Continue For
                If s.EndsWith("|0", StringComparison.Ordinal) Then
                    Dim renamed = String.Concat(s.AsSpan(0, s.Length - 2), newSuffix)
                    cp.Parent.Content = renamed
                    Dim sLog = s, renamedLog = renamed
                    Dim socketLog = If(cp.Name?.Content, "<unnamed>")
                    Logger.LogLazy(Function() $"[SUBSOCKET-RENAME] socket='{socketLog}' ParentBone '{sLog}' → '{renamedLog}'")
                End If
            Next
        Next
    End Sub

End Class
