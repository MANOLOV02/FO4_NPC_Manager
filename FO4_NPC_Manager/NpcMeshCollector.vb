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

''' <summary>Phase 2 of the MainForm split (MeshCollection/Mounting — Increment 2): the CANDIDATE
''' pipeline. ResolvePreviewVariant orchestrates collect (ARMO/OTFT/headparts/robot-chunk candidates) →
''' slot-conflict selection + headwear occlusion → LoadNifShapes (load NIF→shapes, per-shape material
''' overrides, populate PreviewResolutionResult). Pure data + NiflySharp parsing — NO WinForms controls,
''' NO GL/host execution (runs on the render Task.Run; the orchestrator RenderCurrentStateAsync +
''' PrepareSkeleton stay in MainForm and call this). DI: NpcRenderContext (PluginManager + parse caches),
''' NpcMaterialResolver (ApplyShapeMaterialOverrides), NpcStateResolver (ResolveSkeletonKey),
''' NpcMountingResolver (robot-chunk mount + sockets) + Func delegates for MainForm-resident helpers
''' (HasFaceGenAssets, ArmoIsPowerArmor, RaceIsPowerArmor — shared power-armor predicates kept in
''' MainForm because the outfit/armo-universe also uses them). Shared nested types (MeshCandidate,
''' PreviewResolutionResult, NPCVisualState, etc.) stay nested in MainForm and are referenced as
''' MainForm.&lt;T&gt;. See project_mainform_split.</summary>
Friend NotInheritable Class NpcMeshCollector
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _stateResolver As NpcStateResolver
    Private ReadOnly _mountingResolver As NpcMountingResolver
    Private ReadOnly _hasFaceGenAssets As Func(Of MainForm.NPCVisualState, Boolean)
    Private ReadOnly _armoIsPowerArmor As Func(Of UInteger, Boolean)
    Private ReadOnly _raceIsPowerArmor As Func(Of UInteger, Boolean)

    ''' <summary>Per-mesh cache for CandidateHairSlotMask, keyed by normalized mesh key
    ''' (candidate.DictKey, already a FilesDictionary key). Hair-slot occupancy is a property of the mesh
    ''' file alone (its BSSubIndexTriShape segmentation), stable across NPCs sharing the same hair mesh,
    ''' so it's worth memoizing. (Owner moved from MainForm._candidateHairSlotMaskCache.)</summary>
    Private ReadOnly _candidateHairSlotMaskCache As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase)

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   stateResolver As NpcStateResolver, mountingResolver As NpcMountingResolver,
                   hasFaceGenAssets As Func(Of MainForm.NPCVisualState, Boolean),
                   armoIsPowerArmor As Func(Of UInteger, Boolean),
                   raceIsPowerArmor As Func(Of UInteger, Boolean))
        _ctx = ctx
        _materialResolver = materialResolver
        _stateResolver = stateResolver
        _mountingResolver = mountingResolver
        _hasFaceGenAssets = hasFaceGenAssets
        _armoIsPowerArmor = armoIsPowerArmor
        _raceIsPowerArmor = raceIsPowerArmor
    End Sub

    Friend Function ResolvePreviewVariant(previewVariant As MainForm.PreviewVariantDefinition) As MainForm.PreviewResolutionResult
        Dim result As New MainForm.PreviewResolutionResult()
        If previewVariant Is Nothing OrElse previewVariant.State Is Nothing Then Return result
        Dim state = previewVariant.State


        result.Warnings.AddRange(previewVariant.Warnings)
        result.SkeletonKey = _stateResolver.ResolveSkeletonKey(previewVariant.State, result.Warnings)

        Dim candidates = CollectMeshCandidates(previewVariant.State, result.Warnings, previewVariant.UseFaceGen, previewVariant.OnlyFaceCollect, previewVariant.OnlyOutfitCollect)

        ' Engine-faithful, per-RACE head-part occlusion: RACE.DATA declares which worn biped slot hides each
        ' head-part channel (face-cull A, hair B, facial-hair C). Resolve the NPC's race once (cached parse;
        ' the same record is read ~20x/render) and turn it into slot-30-relative masks via RaceUtil. These
        ' drive both SelectWinningCandidates (which head parts to occlude/zap) and the render-time worn-slot
        ' slice (result.HeadOcclusionMask, consumed by NpcRenderHost.ApplyRenderToggleVisibility). Nothing race
        ' -> all masks 0 -> nothing occludes (safe under-hide), matching the old const's zero behaviour.
        Dim raceData As RACE_Data = Nothing
        If state.RaceFormID <> 0UI Then
            Dim raceRec = _ctx.PluginManager.GetRecord(state.RaceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then raceData = _ctx.ParseRaceCached(raceRec)
        End If
        Dim faceCullMask As UInteger = RaceUtil.RaceFaceCullMask(raceData)
        Dim hairMask As UInteger = RaceUtil.RaceHairMask(raceData)
        Dim facialHairMask As UInteger = RaceUtil.RaceFacialHairMask(raceData)
        result.HeadOcclusionMask = faceCullMask Or hairMask Or facialHairMask

        ' Per-segment worn-slot occlusion (Fase 2): LoadNifShapes records each worn-item shape's OWN slots
        ' + group id (ShapeOwnSlots / ShapeSlotGroup); ApplyRenderToggleVisibility recomputes the occlusion
        ' mask from the currently-rendered subset (a render toggle hiding an item drops its slots).
        Dim selectedCandidates = SelectWinningCandidates(candidates, faceCullMask, hairMask, facialHairMask)

        ' Diagnostic toggles "Render armor" / "Render only armor" se aplican vía RenderHide en
        ' el draw loop (sin re-resolver candidates). Cada shape se categoriza a la salida del
        ' resolver y los handlers de los CheckBoxes setean RenderHide según categoría + estado
        ' de los toggles. Ver ApplyRenderToggleVisibility.

        ' Sculpt source identification (rule per user 2026-04-27):
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
        ' subrecord BSMP/BSMB/BSMS (verificado: wbDefinitionsTES5.pas no los define) → ArmaBoneScaleDeltas
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
                    Dim aa = _ctx.GetParsedArma(candidate.ArmorAddonFormID)
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
                Dim aaL = If(candFidL <> 0UI, _ctx.GetParsedArma(candFidL), Nothing)
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

    Private Function CollectMeshCandidates(state As MainForm.NPCVisualState, warnings As List(Of String), Optional useFaceGen As Boolean = False, Optional onlyFaceCollect As Boolean = False, Optional onlyOutfitCollect As Boolean = False) As List(Of MainForm.MeshCandidate)
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
            CollectArmoCandidates(state.SkinFormID, state, MainForm.MeshCandidateKind.Skin, candidates, order, warnings)
        End If

        If Not onlyFaceCollect Then
            ' Use pre-resolved LoadoutArmorFormIDs (already expanded from LVLI).
            ' These are the final ARMO FormIDs for this specific variant.
            If state.LoadoutArmorFormIDs.Count > 0 Then
                For Each armoFormID In state.LoadoutArmorFormIDs
                    CollectArmoCandidates(armoFormID, state, MainForm.MeshCandidateKind.Outfit, candidates, order, warnings)
                Next
            ElseIf state.DefaultOutfitFormID <> 0UI Then
                ' Fallback: read OTFT directly (for NPCs without leveled expansion)
                Dim outfitRec = _ctx.PluginManager.GetRecord(state.DefaultOutfitFormID)
                If outfitRec Is Nothing OrElse outfitRec.Header.Signature <> "OTFT" Then
                    warnings.Add($"Default outfit {state.DefaultOutfitFormID:X8} is missing or not OTFT")
                Else
                    Dim outfit = RecordParsers.ParseOTFT(outfitRec, _ctx.PluginManager)
                    For Each itemFormID In outfit.ItemFormIDs
                        CollectArmoCandidates(itemFormID, state, MainForm.MeshCandidateKind.Outfit, candidates, order, warnings)
                    Next
                End If
            End If
        End If

        ' HeadParts: Full + OnlyFace; OnlyOutfit (single-piece preview) drops them.
        If Not onlyOutfitCollect Then
            Dim mergedHeadParts = MergeHeadPartsWithRaceDefaults(state)
            CollectHeadPartCandidates(mergedHeadParts, New HashSet(Of UInteger)(), candidates, order, warnings, state, useFaceGen)
        End If

        ' Robot path (NPC_.ObjectTemplate). Replaces the legacy "iterate combo #0
        ' OMODFormIDs flat list" branch. Engine rule (verified vs dump v2):
        '   1. ObjectTemplateResolver.ResolveNpcCombinations picks ONE combination
        '      (kw-match → first Default → first overall).
        '   2. Walk the chosen combination's IncludedOmods: each OMOD.ModelPath != ""
        '      is a chunk MainForm.MeshCandidate to mount via BSConnectPoint::Parents lookup
        '      from the actor's skeleton NIF (helper BSConnectPointReader).
        '   3. OMODs without ModelPath but with Properties feed OmodResolutionApplier
        '      with formType="NPC_" (idx 5 MaterialSwap, idx 4 ColorRemap).
        ' AttachPoint resolution: OMOD.AttachPointFormID → KYWD record → EditorID,
        ' matched case-insens against ConnectPointInfo.Name.
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
                                                               AddressOf _ctx.ParseRaceCached, AddressOf _ctx.ParseHdptCached)
    End Function

    Friend Sub CollectArmoCandidates(armoFormID As UInteger,
                                      state As MainForm.NPCVisualState,
                                      kind As MainForm.MeshCandidateKind,
                                      candidates As List(Of MainForm.MeshCandidate),
                                      ByRef order As Integer,
                                      warnings As List(Of String))
        Dim armo = _ctx.GetParsedArmo(armoFormID)
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
                raceHeadOcclSlots = RaceUtil.RaceHeadOcclusionMask(_ctx.ParseRaceCached(hdRaceRec))
            End If
        End If
        Dim skinSlots As UInteger = BipedSlots.RegionMask(BipedSlots.BipedRegion.Body) Or BipedSlots.RegionMask(BipedSlots.BipedRegion.Hands)
        Dim headOcclGate As UInteger = BipedSlots.HeadwearMaskForGame() Or (raceHeadOcclSlots And (Not skinSlots))

        Dim useFaceGen As Boolean = _hasFaceGenAssets(state)

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
        ' AdditionalRaces. The per-ARMA check (ArmorAddonMatchesRace) handles this correctly.
        ' Log the ARMO race only for visibility; don't reject based on it.

        ' Multi-addon resolution: ARMOs con varios `Models` (ej. Combat Torso = Lite/Mid/Heavy)
        ' eligen UN addon vía la cadena: LVLI.LLKC keywords → ARMO.OBTS combination keyword match
        ' → OMOD Property AddonIndex (idx 7 wbArmorPropertyEnum). Fallback a BaseAddonIndex (FNAM)
        ' o índice 0 si nada matchea.
        ' Spec: wbDefinitionsFO4.pas:6187-6192 (Models), 5867 (OBTS), 5710 (AddonIndex property),
        ' 1192-1245 (wbOBTEAddonIndexToStr — flujo del engine).
        ' AddonIndex resolution. El INDX en el array Models de la ARMO no es índice único —
        ' es etiqueta de "grupo de addons que se cargan juntos". El engine resuelve UN
        ' AddonIndex efectivo (default 0; override via OMOD AddonIndex Property cuando OBTS
        ' combination matchea contexto de keywords) y carga TODOS los Models cuyo INDX coincide.
        '   - Sturgess (Abbot): efectiveIdx=0, dos Models con INDX=0 (clothes+gloves) → carga ambos.
        '   - Gunner Combat Torso: keyword Heavy → OMOD AddonIndex=2 → carga el grupo INDX=2.
        ' ctxKeywords lifted out of the addon-resolve block so the OBTS/OMOD resolver below can
        ' use the same set. Source: LVLI.LLKC propagation (arch_outfit_resolution.md). Empty
        ' for ARMOs reached without a leveled outfit (e.g. NPC.WNAM skin) — combinations with
        ' Default=True still apply, keyword-only combinations don't.
        Dim ctxKeywords As List(Of UInteger) = Nothing
        state.LoadoutArmorContextKeywords?.TryGetValue(armoFormID, ctxKeywords)

        ' Resolve OBTS/OMOD canonical view ONCE per ARMO. Shared by every MainForm.MeshCandidate
        ' produced for this ARMO's addons — they all live under the same combination overlay.
        ' The applier runs in ApplyShapeMaterialOverrides after the ARMA-direct base swap.
        Dim omodResolution = ObjectTemplateResolver.ResolveArmoCombinations(armo, ctxKeywords, _ctx.PluginManager)

        ' [FASE 3] Chunk-mount path biped: OMODs con AttachPoint != 0 AND ModelPath != "" se
        ' montan vía BSConnectPoint igual que robot chunks. Delegate al shared con
        ' formType="ARMO". Para ARMOs sin chunk-mount OMODs (solo property modifiers tipo
        ' ap_armor_Lining/Tier/Size), el shared early-returns sin emitir candidates.
        ' Capa V2 ahora SÍ aplica a biped: el gate Fase 2.5 fue removido. Toda shape con
        ' MountSocket recibe el mount vía RE-BIND de su skin (huesos del esqueleto intactos).
        CollectOmodChunkCandidates(omodResolution, "ARMO", state, candidates, order, warnings)

        Dim addonOrder As List(Of UInteger)
        If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            ' ⭐ Skyrim ARMO Armature = RArray PLANO de MODL (ARMA FormIDs), SIN INDX/Addon-Index
            ' (verificado: SkinNaked 0x00000D64 = 25×MODL, 0×INDX). TODOS los armatures aplican
            ' (torso/hands/feet + variantes por raza/bestia/niño); el filtro de raza (raceOk) de abajo
            ' elige los que matchean al NPC. El INDX-variant grouping de FO4 NO existe en Skyrim, y
            ' aplicarlo tomaba sólo el armature en posición 0 (el parser da índice posicional sin INDX)
            ' → una skin multi-armature perdía body/hands/feet = "sin manos"/"sin cuerpo".
            addonOrder = armo.ArmorAddonFormIDs.ToList()
        ElseIf armo.ArmorAddons.Count >= 1 Then
            ' Resolve effective AddonIndex. ResolveEffectiveAddonIndex ahora devuelve Integer? —
            ' HasValue=True cuando hay OMOD override keyword-driven; sino Nothing → usar
            ' BaseAddonIndex (FNAM) si está, sino 0 (vanilla default).
            Dim resolved = ResolveEffectiveAddonIndex(armo, ctxKeywords)
            Dim effectiveIdx As Integer
            If resolved.HasValue Then
                effectiveIdx = resolved.Value
            ElseIf armo.BaseAddonIndex >= 0 Then
                effectiveIdx = armo.BaseAddonIndex
            Else
                effectiveIdx = 0
            End If

            ' Take ALL models whose INDX matches the effective AddonIndex (group, not single).
            addonOrder = New List(Of UInteger)
            For Each entry In armo.ArmorAddons
                If CInt(entry.AddonIndex) = effectiveIdx Then
                    addonOrder.Add(entry.ArmaFormID)
                End If
            Next
            ' Defensive fallback: si el INDX resuelto no existe en los Models (datos malformados
            ' o keyword-driven INDX que apunta a un grupo no presente), usar todas las entries
            ' con el menor INDX disponible — no crashear ni dejar el outfit vacío.
            If addonOrder.Count = 0 Then
                Dim minIdx As Integer = armo.ArmorAddons.Min(Function(e) CInt(e.AddonIndex))
                For Each entry In armo.ArmorAddons
                    If CInt(entry.AddonIndex) = minIdx Then addonOrder.Add(entry.ArmaFormID)
                Next
            End If
        Else
            addonOrder = armo.ArmorAddonFormIDs.ToList()
        End If

        ' Within-ARMO armature slot occupancy (engine "first addon claims the slot" rule, see the
        ' coveredSlots check before candidates.Add below). Accumulates the biped slots already taken
        ' by earlier race-matching armature entries of THIS ARMO.
        Dim coveredSlots As UInteger = 0UI
        For Each armaFormID In addonOrder
            Dim arma = _ctx.GetParsedArma(armaFormID)
            If arma Is Nothing Then Continue For
            ' raceOk drives the skip below (app logic, always computed). The block under
            ' If Logger.Enabled is PURELY diagnostic — it dumps every ARMA at the effective addon
            ' index (even race-skipped ones) with its model flags (MO2F/MO3F/MO4F/MO5F) + all four
            ' model paths, so the bombín "human + robot" duplicate can be read off the log: which
            ' addons sit at this index, which races they accept, and whether a second ARMA is
            ' pulling in a 1st-person / facebones / robot-variant model.
            Dim raceOk As Boolean = MainForm.ArmorAddonMatchesRace(arma, state.RaceFormID, _ctx.GetEffectiveArmorRaces(state.RaceFormID))
            If Logger.Enabled Then
                Dim a = arma
                Dim afid = armaFormID
                Dim armoFid = armoFormID
                Dim rOkL = raceOk
                Logger.LogLazy(Function() $"[ARMA-MODELFLAGS] ARMO=0x{armoFid:X8} ARMA=0x{afid:X8} '{a.EditorID}' " &
                    $"race=0x{a.RaceFormID:X8} addRaces=[{String.Join(",", a.AdditionalRaces.Select(Function(x) x.ToString("X8")))}] raceOk={rOkL} slot=0x{a.SlotMask:X8} | " &
                    $"MO2F=0x{a.MaleModelFlags:X2}({NpcManagerFormat.DescribeModelFlags(a.MaleModelFlags)}) MO3F=0x{a.FemaleModelFlags:X2}({NpcManagerFormat.DescribeModelFlags(a.FemaleModelFlags)}) " &
                    $"MO4F=0x{a.MaleFPModelFlags:X2} MO5F=0x{a.FemaleFPModelFlags:X2} | " &
                    $"MO2S(matswap)=0x{a.MaleMaterialSwapFormID:X8} MO3S=0x{a.FemaleMaterialSwapFormID:X8} MO2C(remap)={If(a.MaleColorRemapIndex.HasValue, a.MaleColorRemapIndex.Value.ToString("F3"), "none")} | " &
                    $"MOD2='{a.MaleMeshPath}' MOD3='{a.FemaleMeshPath}' MOD4='{a.MaleFPMeshPath}' MOD5='{a.FemaleFPMeshPath}'")
            End If
            If Not raceOk Then
                Continue For
            End If

            ' Pick the gender-matching bone scale block (if any) and log + stash it on the
            ' candidate. Engine-side these per-bone Vec3 deltas are added on top of RACE.BSMS
            ' to shape the outfit around the body (cinched waist, wider hips, vest volume).
            Dim targetGender As UInteger = If(state.IsFemale, 1UI, 0UI)
            Dim genderBoneScale As List(Of ARMA_BoneScaleDelta) = Nothing
            For Each bsg In arma.BoneScaleData
                If bsg.Gender <> targetGender Then Continue For
                If bsg.Bones.Count = 0 Then Continue For
                genderBoneScale = bsg.Bones
                For Each bd In bsg.Bones
                    Dim mag = Math.Sqrt(bd.DeltaX * bd.DeltaX + bd.DeltaY * bd.DeltaY + bd.DeltaZ * bd.DeltaZ)
                Next
                Exit For
            Next

            ' Resolve mesh path with ARMA-first / ARMO-WorldModel-fallback semantics.
            ' ARMO.MOD2 (male) / MOD4 (female) per wbDefinitionsFO4.pas:6164-6175 populate when the
            ' mesh is authored at ARMO level (robots: Assaultron skin has ARMO.MOD2=Assaultron.nif
            ' with empty ARMA.MOD2/MOD3). Humanoid armors inverse: ARMA has the mesh, ARMO.MOD2/MOD4
            ' usually empty. Gender mirror inside each source: try same-gender first, then opposite.
            Dim meshPath = If(state.IsFemale, arma.FemaleMeshPath, arma.MaleMeshPath)
            If meshPath = "" Then meshPath = If(arma.MaleMeshPath <> "", arma.MaleMeshPath, arma.FemaleMeshPath)
            If meshPath = "" Then
                meshPath = If(state.IsFemale, armo.FemaleWorldModelPath, armo.MaleWorldModelPath)
                If meshPath = "" Then meshPath = If(armo.MaleWorldModelPath <> "", armo.MaleWorldModelPath, armo.FemaleWorldModelPath)
            End If
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
            If useFaceGen Then
                Dim modelFlags As Byte = If(state.IsFemale, arma.FemaleModelFlags, arma.MaleModelFlags)
                If (modelFlags And &H1) <> 0 Then
                    Dim fbKey = MeshPathHelpers.TryGetFaceBonesVariant(armaDictKey)
                    If fbKey <> "" Then
                        If Logger.Enabled Then
                            Dim afidLog = armaFormID
                            Dim fbLog = fbKey
                            Logger.LogLazy(Function() $"[ARMA-FACEBONES] ARMA=0x{afidLog:X8} redirect base->_faceBones dictKey='{fbLog}'")
                        End If
                        armaDictKey = fbKey
                    End If
                End If
            End If

            Dim effSlotMask As UInteger = MainForm.EffectiveArmaSlotMask(arma, armo)

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
            candidates.Add(New MainForm.MeshCandidate With {
                .DictKey = armaDictKey,
                .SlotMask = effSlotMask Or (armo.SlotMask And headOcclGate),
                .ArmaOwnSlotMask = effSlotMask,
                .Priority = If(state.IsFemale, arma.FemalePriority, arma.MalePriority),
                .Kind = kind,
                .SourceFormID = armoFormID,
                .ArmorAddonFormID = armaFormID,
                .TextureSetFormID = If(state.IsFemale,
                                       If(arma.FemaleSkinTextureFormID <> 0UI, arma.FemaleSkinTextureFormID, arma.MaleSkinTextureFormID),
                                       If(arma.MaleSkinTextureFormID <> 0UI, arma.MaleSkinTextureFormID, arma.FemaleSkinTextureFormID)),
                .MaterialSwapFormID = If(state.IsFemale,
                                          If(arma.FemaleMaterialSwapFormID <> 0UI, arma.FemaleMaterialSwapFormID, arma.MaleMaterialSwapFormID),
                                          If(arma.MaleMaterialSwapFormID <> 0UI, arma.MaleMaterialSwapFormID, arma.FemaleMaterialSwapFormID)),
                .ColorRemapIndex = If(state.IsFemale,
                                       If(arma.FemaleColorRemapIndex.HasValue, arma.FemaleColorRemapIndex, arma.MaleColorRemapIndex),
                                       If(arma.MaleColorRemapIndex.HasValue, arma.MaleColorRemapIndex, arma.FemaleColorRemapIndex)),
                .OmodResolution = omodResolution,
                .Order = order,
                .ArmaBoneScaleDeltas = genderBoneScale
            })

            ' [OUTFIT-RESOLVE] dump por cada candidate emitido. Tag PIPBOY-CANDIDATE cuando el
            ' SlotMask contiene bit 30 (slot 60 - Pipboy, wbDefinitionsFO4.pas:3776). Permite ver
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

    ''' <summary>NPC robot path: walks NPC_.OBTE via the canonical resolver, picks ONE
    ''' combination, expands its IncludedOmods recursively, emits one MainForm.MeshCandidate per chunk
    ''' OMOD (with mount transform from BSConnectPoint::Parents lookup), and shares the
    ''' resolution across all emitted candidates so the applier runs Properties once at the
    ''' actor level.
    '''
    ''' Engine semantics (verified vs dump v2):
    '''   - Each chunk OMOD has ModelPath != "" and AttachPointFormID → KYWD whose EditorID
    '''     matches a BSConnectPoint::Parents.Name in the actor skeleton NIF.
    '''   - The chunk renders at the socket's local transform on top of the bone Parent.
    '''   - OMODs without ModelPath but with Properties (or DirectProperties on the combination)
    '''     contribute Materials/Color swaps applied via OmodResolutionApplier formType="NPC_".
    '''
    ''' AttachPoint logging: KYWD records were not loaded by the legacy plugin filter
    ''' (SIGS_NPC_RENDERING did not include "KYWD" until 2026-05-10). With the fix in place
    ''' AttachPoint EditorIDs resolve and chunks mount at the correct sockets.
    '''
    ''' Skeleton merge: handled by PrepareSkeleton via BodyPartSkeletonResolver (BPTD.MODL
    ''' from RACE.GNAM). Replaces the legacy MergeRobotExtendedSkeletonsIfRobot filesystem
    ''' heuristic. Chunks mount correctly via BSConnectPoint and standard
    ''' SkeletonInstance.MergeAdditionalSkeleton pipeline.</summary>
    Private Sub CollectRobotChunkCandidates(state As MainForm.NPCVisualState,
                                            candidates As List(Of MainForm.MeshCandidate),
                                            ByRef order As Integer,
                                            warnings As List(Of String))
        ' [DIAG] Entry log — confirma estado de entrada del robot path.
        Dim stateFid = state.FormID
        Dim stateRace = state.RaceFormID
        Dim hasOT = state.HasObjectTemplate
        Dim otCount = If(state.ObjectTemplateCombinations Is Nothing, 0, state.ObjectTemplateCombinations.Count)
        Dim apSlotCount = If(state.AttachParentSlotFormIDs Is Nothing, 0, state.AttachParentSlotFormIDs.Count)
        Dim apSlotStr = If(state.AttachParentSlotFormIDs Is Nothing OrElse state.AttachParentSlotFormIDs.Count = 0, "[]",
                           "[" & String.Join(",", state.AttachParentSlotFormIDs.Select(Function(f) "0x" & f.ToString("X8") & "(" & ObjectTemplateResolver.KywdEditorIdSafe(f, _ctx.PluginManager) & ")")) & "]")
        Logger.LogLazy(Function() $"[ROBOT-ENTRY] npc=0x{stateFid:X8} race=0x{stateRace:X8} hasOT={hasOT} combos={otCount} npcAPPR={apSlotCount}={apSlotStr}")

        ' Build a stub NPC_Data carrying the OBTE so we can re-use ResolveNpcCombinations.
        Dim stubNpc As New NPC_Data With {
            .FormID = state.FormID,
            .HasObjectTemplate = state.HasObjectTemplate,
            .RaceFormID = state.RaceFormID
        }
        For Each ch In state.ObjectTemplateCombinations
            stubNpc.ObjectTemplateCombinations.Add(ch)
        Next
        ' Propagate NPC.APPR — initial pool for the AP-filter inside ObjectTemplateResolver.
        ' RACE.APPR is read by the resolver itself via stubNpc.RaceFormID.
        If state.AttachParentSlotFormIDs IsNot Nothing Then
            stubNpc.AttachParentSlotFormIDs.AddRange(state.AttachParentSlotFormIDs)
        End If

        ' ctxKeywords: NPC robots typically don't get LVLI.LLKC propagation (they're not
        ' wrapped in OTFT). Pass empty so the resolver falls through to first-Default.
        Dim ctxKeywords As New List(Of UInteger)
        Dim resolution = ObjectTemplateResolver.ResolveNpcCombinations(stubNpc, ctxKeywords, _ctx.PluginManager)

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
            If omodPre Is Nothing OrElse String.IsNullOrEmpty(omodPre.ModelPath) Then Continue For
            Dim dictKeyPre = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(omodPre.ModelPath)
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
                ' [HOST-SCOPED] Poblar publisherSockets[omodPre.FormID] con TODOS los sockets
                ' que este chunk publica — sin merging con skeleton, sin FIRST-WINS. El
                ' namespace del publisher es propio. Conflicts dentro del mismo publisher
                ' (mismo nombre dos veces en el mismo chunk) son inconsistencia local —
                ' loggear, mantener primero.
                '
                ' Por cada socket computamos HostSocketGlobalT EN EL ESPACIO DEL NIF DEL HOST:
                '   - Si parent.NiNode existe en este NIF: parent.global.compose(socket.local).
                '   - Si parent.NiNode NO existe (parent name no aparece en este NIF tree):
                '     ParentFoundInHostNif=False; consumer fallback al path skeleton.
                '   - Si parent name está vacío: tratamos como parent=root del host NIF
                '     (identity), semántica engine para sockets sin parent explícito.
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

        ' Walk IncludedOmods (indexed: parallel list IncludedOmodApIdx carries the apIdx per emit).
        ' Each OMOD with ModelPath = chunk to mount; OMODs without ModelPath contribute Properties
        ' only (resolved en bloque por el applier al final).
        '
        ' Socket lookup rule (verified empirically against Codsworth host parents in fo4lib.log):
        '   1. The apEditorId is the OMOD.AttachPoint KYWD EditorID (e.g. 'ap_Bot_ArmsTypeA1').
        '      Host sockets use 'P-X' / 'P-X|N' naming convention. Strip the 'ap_Bot_' or 'ap_'
        '      prefix to get the base name (e.g. 'ArmsTypeA1') — host sockets are 'P-<base>'.
        '   2. Try 'P-<base>|<apIdx>' first (multi-instance like P-ArmsTypeA1|1, P-ModSlotB|2).
        '   3. Fall back to 'P-<base>' (single-instance — host has no |N suffix).
        ' Both shapes coexist in vanilla: TorsoHandy → P-BotCore (no suffix), Arm_Right_Flamer
        ' → P-ArmsTypeA1|1 (suffixed). The lookup tries indexed first and falls back.
        ' [HOST-SCOPED ORDINAL] hostChainMap[ordinal] = hostOrdinal del padre inmediato.
        ' Identidad por ordinal monotónico (expand-time, antes de cualquier dedup) garantiza
        ' que el mismo OMOD asset reutilizado bajo hosts distintos NO colapsa identidades.
        ' Ordinal 0 reservado para skeleton root sentinel.
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
            If String.IsNullOrEmpty(omod.ModelPath) Then Continue For ' property-only OMODs
            ' Note: vanilla rusty/variant OMODs (Bot_ArmLeftProtectronRusty1 etc.) have
            ' FormType=NONE while the originals have FormType=NPC_. Filtering by FormType
            ' would drop the variants — they render in-game, so we accept any FormType here.

            Dim apEditorId = _mountingResolver.ResolveAttachPointEditorId(omod.AttachPointFormID)
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

            Dim dictKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(omod.ModelPath)
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
        ' Per-render FLST cache so IsHdptValidForRace's race-membership checks parse each FLST
        ' at most once across the whole HDPT chain (vanilla has 3-4 distinct FLSTs referenced
        ' by hundreds of HDPTs).
        Dim flstCache As New Dictionary(Of UInteger, FLST_Data)
        ' Race defaults (gender-appropriate) so RACE-declared HDPTs always pass the check even
        ' when their RNAM is mod-inconsistent. Mirrors HeadPartPicker_Form's seed.
        Dim raceDefaults As New HashSet(Of UInteger)
        ' Non-humanoid race signal: a RACE that declares NO head parts (neither Male nor Female)
        ' is a creature/robot/dog race. RNAM=0 HDPTs only pass for humanoid races (engine drops
        ' them on dogs/robots even when NPC.PNAM has a buggy reference, e.g. EncRaiderDog01).
        Dim raceHasAnyHeadParts As Boolean = False
        Dim raceRec = If(state IsNot Nothing AndAlso state.RaceFormID <> 0UI, _ctx.PluginManager.GetRecord(state.RaceFormID), Nothing)
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
            Dim race = _ctx.ParseRaceCached(raceRec)
            Dim defs = If(state.IsFemale, race?.FemaleHeadPartFormIDs, race?.MaleHeadPartFormIDs)
            If defs IsNot Nothing Then
                For Each fid In defs : raceDefaults.Add(fid) : Next
            End If
            ' Either gender having head parts is enough — the race is humanoid.
            Dim maleCount = If(race?.MaleHeadPartFormIDs?.Count, 0)
            Dim femaleCount = If(race?.FemaleHeadPartFormIDs?.Count, 0)
            raceHasAnyHeadParts = (maleCount + femaleCount) > 0
        End If

        ' Pre-compute Misc->parent effective-type promotion for the top-level (parent=-1) case:
        ' vanilla NPC.PNAM often lists a hairline both in the hair's HNAM and standalone in PNAM;
        ' without this map the cascade depended on visit order. Shared helper = single source of
        ' truth with the bake's EnumerateHdptChain (no duplicated rule).
        Dim miscToParentEffective = HeadPartResolver.BuildMiscToParentEffective(headPartFormIDs, _ctx.PluginManager, AddressOf _ctx.ParseHdptCached)

        For Each hdptFormID In headPartFormIDs.Where(Function(id) id <> 0UI)
            CollectHeadPartCandidate(hdptFormID, visited, candidates, order, warnings, -1, state, useFaceGen, flstCache, raceDefaults, raceHasAnyHeadParts, miscToParentEffective)
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
                                         Optional flstCache As Dictionary(Of UInteger, FLST_Data) = Nothing,
                                         Optional raceDefaults As HashSet(Of UInteger) = Nothing,
                                         Optional raceHasAnyHeadParts As Boolean = True,
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
        Dim effectivePartType = HeadPartResolver.ResolveEffectivePartType(hdpt.PartType, parentPartType, hdptFormID, miscToParentEffective)

        ' Race-membership check: drop HDPTs the engine wouldn't render. The only practical
        ' case this catches today is RNAM=0 HDPTs assigned (via NPC.PNAM) to a non-humanoid
        ' race — e.g. EncRaiderDog01 lists MaleMouthHumanoidDirtyTeethMissing yet the engine
        ' renders no human teeth on raider dogs because RaiderDogRace declares zero head parts.
        ' Humanoid races (HumanRace, GhoulRace, etc.) keep all their RNAM=0 HDPTs as before.
        If flstCache IsNot Nothing AndAlso state IsNot Nothing AndAlso state.RaceFormID <> 0UI Then
            Dim raceOk = HeadPartResolver.IsHdptValidForRace(hdptFormID, state.RaceFormID, state.IsFemale, _ctx.PluginManager, flstCache, raceDefaults, raceHasAnyHeadParts, AddressOf _ctx.ParseHdptCached)
            If Not raceOk Then
                Return
            End If
        End If


        If hdpt.MeshPath <> "" Then
            ' Redirect face-region meshes to their _faceBones.nif variant only for NPCs with
            ' a custom CharGen face (useFaceGen=True). The _faceBones variants are rigged to face
            ' bones (Jaw, LipUpper_L, Cheek_R, etc) enabling FMRS bone transforms to deform the
            ' mesh. NPCs without FaceGen use default race face — no _faceBones redirect needed.
            Dim dictKey = NameUtils.NormalizeDictionaryKeyWithMeshesPrefix(hdpt.MeshPath)
            Dim originalDictKey = dictKey   ' antes del posible redirect a _faceBones (para log)
            Dim baseDictKeyForFaceBones As String = ""
            If useFaceGen Then
                Dim faceBonesKey = MeshPathHelpers.TryGetFaceBonesVariant(dictKey)
                If faceBonesKey <> "" Then
                    ' Solo HeadRear necesita copia de material desde el .nif base (el _faceBones
                    ' vanilla trae basehumanfemaleskin genérico en lugar de basehumanfemalerear).
                    ' Otros types usan el material del _faceBones tal cual.
                    If effectivePartType = 9 Then baseDictKeyForFaceBones = dictKey
                    dictKey = faceBonesKey
                End If
            End If

            ' Head-rear nape body texture: the vanilla-UV nape mesh needs a vanilla-UV body texture.
            ' For ghoul females the live body path is CBBE's CBBE-UV body (UV-mismatched). The
            ' clone-to-disk fix (vanilla bytes under a distinct path key, to dodge the shared
            ' path-keyed GL texture cache) is applied per-shape in ApplyShapeMaterialOverrides via the
            ' candidate's HeadPartHdptFormID gate. UsesBodyTexture stays the raw record value here —
            ' the previous override-proxy forcing (HumanRace 0x13746 + is-override heuristic) is gone.
            Dim effectiveUsesBodyTexture = hdpt.UsesBodyTexture

            ' Trace del candidato HeadPart: qué HDPT, tipo raw/effective, mesh ORIGINAL vs el
            ' redirect a _faceBones, el TXST (TNAM) y color. Para ojos esto deja ver de qué NIF
            ' sale el shape (femaleeyes.nif vs femaleeyes_faceBones.nif) y qué TNAM trae.
            If Logger.Enabled Then
                Dim hdptEidC = If(hdptRec.EditorID, "")
                Dim rawTypeC = hdpt.PartType
                Dim effTypeC = effectivePartType
                Dim origMeshC = If(hdpt.MeshPath, "")
                Dim finalKeyC = dictKey
                Dim redirectedC = Not String.Equals(originalDictKey, dictKey, StringComparison.OrdinalIgnoreCase)
                Dim tnamC = hdpt.TextureSetFormID
                Dim colorC = hdpt.ColorFormID
                Dim ubtC = effectiveUsesBodyTexture
                Dim ufgC = useFaceGen
                Logger.LogLazy(Function() $"[HDPT-CAND] hdpt=0x{hdptFormID:X8} eid='{hdptEidC}' rawType={rawTypeC} effType={effTypeC} useFaceGen={ufgC} TNAM=0x{tnamC:X8} color=0x{colorC:X8} usesBodyTex={ubtC} faceBonesRedirect={redirectedC} mesh='{origMeshC}' dictKey='{finalKeyC}'")

                ' NOSOTROS redirigimos face→_faceBones: dumpear el material INLINE de AMBOS NIFs
                ' (el original que CK usaría y el _faceBones que cargamos nosotros) para comparar si
                ' difieren en shader/normal/spec. El render solo carga el _faceBones, así que el
                ' original solo se ve acá.
                If redirectedC Then
                    NpcMaterialResolver.LogNifInlineMaterials(originalDictKey, $"ORIGINAL hdpt=0x{hdptFormID:X8}/{hdptEidC}")
                    NpcMaterialResolver.LogNifInlineMaterials(dictKey, $"FACEBONES hdpt=0x{hdptFormID:X8}/{hdptEidC}")
                End If
            End If

            candidates.Add(New MainForm.MeshCandidate With {
                .DictKey = dictKey,
                .BaseDictKeyForFaceBones = baseDictKeyForFaceBones,
                .SlotMask = 0UI,
                .Priority = 0,
                .Kind = MainForm.MeshCandidateKind.HeadPart,
                .HeadPartType = effectivePartType,
                .HeadPartTypeRaw = hdpt.PartType,
                .HeadPartColorFormID = hdpt.ColorFormID,
                .TextureSetFormID = hdpt.TextureSetFormID,
                .HeadPartHdptFormID = hdptFormID,
                .UseSolidTint = (hdpt.Flags And MainForm.HeadPartFlagUseSolidTint) <> 0,
                .UsesBodyTexture = effectiveUsesBodyTexture,
                .Order = order,
                .RaceMorphTriPath = hdpt.RaceMorphTriPath,
                .ChargenMorphTriPath = hdpt.ChargenMorphTriPath,
                .Hide = (effectivePartType = 7),
                .IsHnamExtra = (parentPartType >= 0)
            })
            order += 1
        End If

        ' Pass the effective type down so nested extras also inherit
        Dim childParentType = If(effectivePartType <> 0, effectivePartType, parentPartType)
        For Each extraPartFormID In hdpt.ExtraPartFormIDs
            CollectHeadPartCandidate(extraPartFormID, visited, candidates, order, warnings, childParentType, state, useFaceGen, flstCache, raceDefaults, raceHasAnyHeadParts, miscToParentEffective)
        Next
    End Sub

    ''' <summary>Resolve which candidates win their biped-slot tournament and which head parts the worn set
    ''' occludes. The head-part occlusion is RACE-driven (engine-faithful): the caller passes the slot-30-
    ''' relative masks derived from this NPC's RACE.DATA biped objects — <paramref name="faceCullMask"/> (A,
    ''' full-face cull), <paramref name="hairMask"/> (B, the hair channel = 30+B and 30+B+1), and
    ''' <paramref name="facialHairMask"/> (C, the beard slot). 0 mask = that channel occludes nothing.</summary>
    Private Function SelectWinningCandidates(candidates As List(Of MainForm.MeshCandidate),
                                             faceCullMask As UInteger, hairMask As UInteger,
                                             facialHairMask As UInteger) As List(Of MainForm.MeshCandidate)
        Dim selected As New List(Of MainForm.MeshCandidate)

        ' HDPT type=7 Meatcaps used to be filtered here. Now they pass through to the render
        ' pipeline and are marked in result.ShapeMeatcap so the "Render gore" toggle governs
        ' their visibility uniformly with the BSSubIndex SECTIONCAP/TORSOCAP shapes. The
        ' candidate.Hide flag survives through to ApplyShapeGeometry → ShapeMeatcap mapping.
        Dim visibleCandidates = candidates.ToList()

        ' First pass: resolve slotted candidates.
        ' Per FO4 biped slot spec (wbDefinitionsFO4.pas:3745-3778): slots [U] 36-40 (bits 6-10)
        ' and [A] 41-45 (bits 11-15) are separate layers designed to coexist — the underarmor
        ' declares bits the over-armor pieces partially overlap.
        '
        ' Regla "extended underarmor" (per usuario 2026-04-29): un candidate que declara BODY
        ' (bit 3) o algún bit [U] (6-10) Y simultáneamente algún bit [A] (11-15) es un underarmor
        ' "extendido" cuya mesh cubre los slots [A] declarados. Su geometría incluye piernas /
        ' brazos / torso. NO se puede coexistir con un over-armor [A] puro que reclame los mismos
        ' bits [A] — produciría dos geometrías superpuestas (clip visible). El extended underarmor
        ' RESERVA sus bits [A]: cualquier candidate puro [A] que declare bits ya reservados
        ' se descarta entero.
        '
        ' Caso DN061_LvlGunnerBoss (Gunner): AA_DCGuard_UnderArmor declara slot mask 0xC7F8 =
        ' BODY+[U]LArm+[U]RArm+[A]LLeg+[A]RLeg. Reserva bits 14, 15. Las combat legs (slot 0x4000
        ' / 0x8000) declaran bits 14/15 → se descartan. Las combat torso/arm (bits 11, 12) NO
        ' tocan los reservados → entran normalmente.
        ' (extended-underarmor BODY/[U]/[A] slot masks now live in SlotConflictResolver)

        ' Skin candidates (NPC_.WNAM / RACE.WNAM via state.SkinFormID) representan la base body
        ' geometry del NPC — NO son piezas equipables que compitan por slots con outfits/armor.
        ' xEdit wbDefinitionsFO4.pas:10705 + 11434 confirman que NPC_.WNAM y RACE.WNAM son slots
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

        ' Slot conflict resolution (pass 1a extended-underarmor + pass 1b atomic-mutex last-wins +
        ' pipboy↔[A]LArm mutex) extracted to SlotConflictResolver so the render path and the Edit
        ' Outfit "Create" tab share the SAME engine rules. Winners append to `selected` (skin was
        ' already added above, outside the tournament); occupiedSlots feeds the head-part occlusion
        ' (pass 2) + skin coverage (pass 3) below.
        '
        ' Resolve at the EQUIPPED-ARMO level, NOT per-ARMA. The engine mutexes on the equipped item's
        ' BOD2 as a unit (the whole ARMO wins or loses); CollectArmoCandidates emits ONE candidate per
        ' race-valid ARMA (each with its own effSlotMask + its own incrementing Order). Feeding those
        ' straight to the resolver lets a PARTIALLY-conflicting ARMO keep the ARMAs whose own slots don't
        ' overlap the winner — e.g. a "skin outfit" that loses BODY to an underwear still keeps its
        ' hand ARMA (slots 34/35 have no competitor), so its gloves render (and then occlude the naked
        ' hands' forearm seam). That diverges from BOTH the game and the Create tab, which resolves one
        ' union-masked piece per ARMO (GetArmoItemCandidates → ComputeArmoEffectiveSlotMask). Fix: group
        ' the ARMA candidates by owning ARMO (SourceFormID — all ARMAs of one ARMO share it, and different
        ' equipped ARMOs get disjoint Order ranges since `order` is ByRef-continuous), resolve with the
        ' group's UNION slot mask (== the ARMO's BOD2 footprint, same as the Create tab) and its EARLIEST
        ' Order, then expand each winning group back to its ARMAs. A SourceFormID of 0 (no owning ARMO)
        ' is its own singleton group → identical to the old per-item behaviour. Within-ARMO slot dedup
        ' already ran in CollectArmoCandidates, so no ARMA ever conflicts with its own siblings here.
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
        Dim slotResolution = SlotConflictResolver.ResolveSlotWinners(
            armoGroups,
            Function(g) g.Aggregate(0UI, Function(acc, c) acc Or c.SlotMask),
            Function(g) g.Min(Function(c) c.Order))
        For Each g In slotResolution.Winners
            selected.AddRange(g)
        Next
        Dim occupiedSlots As UInteger = slotResolution.OccupiedSlots

        ' Per-segment "covered by OTHER items" occlusion (ORDER / other-items rule, engine owner-slot
        ' branch 0x14035E22B) is NOT precomputed here anymore: it is rebuilt every render by
        ' ApplyRenderToggleVisibility from the items CURRENTLY rendered, so a render toggle that hides an
        ' item drops its slots from the occluding set. LoadNifShapes only records each shape's OWN slots +
        ' group id (ShapeOwnSlots / ShapeSlotGroup) — the inputs that recompute reads.

        ' Third pass: add slotless (head parts), hiding based on occupied biped slots.
        '
        ' Engine-faithful, RACE-driven occlusion (verified vs Fallout4.exe + .esm — see
        ' [[project_re_occlusion_engine]]). The engine does NOT use a fixed slot list; RACE.DATA declares three
        ' "biped object" fields that map (value v -> biped slot 30+v) to the slot whose coverage hides each
        ' head-part channel. The caller passed them in as slot-30-relative masks (0 = None):
        '   faceCullMask  (A) : full-face cull — HumanRace A=2 -> slot 32. Covered ⇒ whole head hidden.
        '   hairMask      (B) : hair channel — HumanRace B=0 -> slots {30,31} (uses 30+B AND 30+B+1).
        '   facialHairMask(C) : beard slot — HumanRace C=18 -> slot 48 (v124+ races only).
        ' These vary per race (A∈{-1,0,2}, B∈{-1,0,1}, C∈{-1,18}), so we never hardcode the human values.
        '
        ' Per head-part type (types 0/1 and "extra parts" never occlude — only the visual layers below apply):
        '   3 Hair (main + hairlines) : RENDER, per-segment + UNIFORM (main == hairline; NO inverse). Each hair
        '                     partition tagged biped# is hidden ⟺ its slot (30+bit) is covered AND lies within
        '                     the hair channel (occupiedSlots ∩ hairMask). Vanilla hair main AND hairlines are
        '                     tagged the SAME slots ({30} or {30,31}) → identical rule: hide the partition whose
        '                     slot is covered, keep the complement. Whole piece hidden if the face-cull slot is
        '                     covered (cascade) or every partition of the piece is covered. RENDER-ONLY; the
        '                     bake (FaceGenBuilder) keeps its own CK-faithful rule.
        '   4 FacialHair    : hidden iff worn covers the beard slot (facialHairMask) OR the face-cull slot.
        '                     (Slot-49 "Mouth" is NOT an engine occlusion slot — dropped.)
        '   6 Eyebrows      : hidden iff worn covers the face-cull slot.
        '   9 HeadRear      : NUNCA se oculta. Es geometría base del cráneo (back of head) que el
        '                     engine renderiza siempre.
        ' Hair-channel coverage of THIS race, intersected with the worn set. hairMask carries up to two bits
        ' (30+B, 30+B+1); whether a specific partition is occluded is tested per-partition against this.
        Dim hairCovered As UInteger = occupiedSlots And hairMask
        Dim hasFaceGenHead As Boolean = (occupiedSlots And faceCullMask) <> 0UI
        ' The two hair partitions a {30,31} piece can have. Engine-faithful: the partition bits are still the
        ' source mesh's biped-30/31 tags (BipedSlots.SlotBitHairTop/Long); a partition is "covered" only when its slot is
        ' both in the worn set AND in this race's hair channel (so a non-hair race with B=None never zaps hair).
        Dim hairTopCovered As Boolean = (hairCovered And BipedSlots.SlotBitHairTop) <> 0UI
        Dim hairLongCovered As Boolean = (hairCovered And BipedSlots.SlotBitHairLong) <> 0UI

        ' Pasada 2 — slotless NO-Skin: HeadParts y Attachments (chunks robot/pack via socket).
        ' HeadParts ocluidos por headwear aceptado se MARCAN con flag IsOccludedByHeadwear pero
        ' NO se descartan; ApplyRenderToggleVisibility decide hide en runtime para que "Render
        ' headwear" OFF los destape.
        '
        ' Attachments (NPC_.OBTE chunks) entran acá con SlotMask=0 + Kind=Attachment +
        ' ChunkOmodFormID>0. No participan en slot conflict resolution (mount via socket P-/C-,
        ' no via armor slot). Cuando estaban marcados Kind=Skin (pre-2026-05-15) hacían pasada 0
        ' Y caían acá → double-add (regresión 2026-05-10 Codsworth 12 chunks → winners=24); la
        ' separación en Kind.Attachment elimina ese caso por construcción.
        '
        ' EXCLUSIÓN Kind=Skin sigue siendo necesaria: los Skin con SlotMask=0 ya se aceptaron en
        ' la pasada 0 (skinCandidates) y no deben entrar de nuevo.
        For Each slotlessCandidate In visibleCandidates.Where(Function(c) c.SlotMask = 0UI AndAlso c.Kind <> MainForm.MeshCandidateKind.Skin).OrderBy(Function(c) c.Order)
            If slotlessCandidate.Kind = MainForm.MeshCandidateKind.HeadPart Then
                Dim occluded As Boolean = False
                ' Addons (HNAM-extras del parent O Misc top-level raw=0) son siempre exentos de la
                ' occlusion de headwear normal — sólo FaceGenHead (slot 32, casco full-face) los tapa.
                ' Cubre los dos caminos por los que un addon llega al render:
                '   a) HNAM-extra (parent>=0 en CollectHeadPartCandidate) — hairlines, mouth shadow,
                '      AO/wet, etc., independientemente de su raw type. Casos 2026-05-17: Hodges +
                '      gorra perdía hairline raw=Misc; otro hair cuya HNAM declara hairline raw=3
                '      (no Misc) también caía bajo HairTop sin esta exención.
                '   b) Misc top-level (raw=0, parent=-1) — addons standalone en NPC.PNAM/RACE que no
                '      están en HNAM de ningún parent listado (mouth shadow sueltos, etc.).
                ' OCLUSIÓN DE PELO — RENDER, per-segment, UNIFORME a main Y hairline, RACE-driven.
                ' LÓGICA (engine-faithful): cada partición de pelo (biped 30/31) se oculta ⟺ su slot está
                ' cubierto por el worn set Y cae dentro del canal de pelo de ESTA raza (hairMask = 30+B y
                ' 30+B+1). Una pieza {30,31} con una sola partición cubierta deja asomar la otra → zap parcial,
                ' no ocultar entera. La HAIRLINE (HNAM-extra) lleva el MISMO tag de slots que el main → MISMA
                ' regla (no inverso). La pieza entera se oculta si el slot face-cull (A) está cubierto (cascada
                ' full-head) o si TODAS sus particiones están cubiertas. hairSlotMask = bits {30→0x1, 31→0x2}
                ' de la mesh; addons NO-pelo (mouth shadow / eyes, biped 32 → hairSlotMask=0) caen al else.
                ' RENDER-ONLY: el bake (FaceGenBuilder) usa su propia regla CK-fiel.
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
                            occluded = (occupiedSlots And (facialHairMask Or faceCullMask)) <> 0UI
                        Case 6 ' Eyebrows
                            ' Cejas: oculto ⟺ worn cubre el slot face-cull (A).
                            occluded = (occupiedSlots And faceCullMask) <> 0UI
                            ' Type 9 HeadRear: nunca se ocluye por headwear (es base skull geometry).
                    End Select
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

    ''' <summary>Categoriza un MainForm.MeshCandidate per los toggles diagnósticos de visibilidad.
    ''' Usa el slot mask del candidate (de BOD2/BODT) y su Kind. La categoría se mapea a
    ''' RenderHide en ApplyRenderToggleVisibility según el estado de los CheckBoxes.</summary>
    ''' <summary>Friend (was Private) so the headless <c>--slot-diag</c> mode (Program.vb) can run the
    ''' REAL classification over Skyrim.esm ARMOs to confirm/verify the game-aware slot mapping without
    ''' rendering. Pure function of (SlotMask, Kind).</summary>
    Friend Shared Function ClassifyShapeCategory(candidate As MainForm.MeshCandidate) As MainForm.ShapeRenderCategory
        If candidate.Kind = MainForm.MeshCandidateKind.HeadPart Then Return MainForm.ShapeRenderCategory.HeadPart

        ' ⭐ Máscaras derivadas de la TABLA AUTORITATIVA por-juego (BipedSlots.RegionMask, sourced de
        ' los nombres de biped-object flags de xEdit wbDefinitionsFO4/TES5 — NO heurística). Aplicar la
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
        ' slot 60 es un slot modular genérico (xEdit '60 - Unnamed'), no un Pipboy → no forzar ArmorOver.
        If (Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim) _
           AndAlso (slot And BipedSlots.SlotBitPipboy) <> 0UI AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.ArmorOver
        ' Shield (SSE slot 39 = bit 9 = 0x200) — accesorio rígido del antebrazo izq (Prn='SHIELD'),
        ' anclado por ApplyPrnRigidAttach. Debe respetar el toggle "Render armor" IGUAL que el Pipboy:
        ' lo agrupamos ArmorOver (no declara bits [A], por eso explícito). SSE-only: en FO4 slot 39 es
        ' '[U] R Leg', otra cosa → no forzar (gate Skyrim). Usuario 2026-07-09: "cablea el shield al
        ' toggle show armor como el pipboy".
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim _
           AndAlso (slot And &H200UI) <> 0UI AndAlso candidate.Kind = MainForm.MeshCandidateKind.Outfit Then Return MainForm.ShapeRenderCategory.ArmorOver
        ' Resto (accessories 16+ raros, shapes sin slot, etc.).
        Return MainForm.ShapeRenderCategory.Other
    End Function

    ''' <summary>Human-readable decode of a wbModelFlags byte (MO2F/MO3F/MO4F/MO5F): bit 0x01 =
    ''' FaceBones, 0x02 = 1stPerson (TES5Edit wbDefinitionsFO4.pas:4622). Diagnostic only (used by the
    ''' [ARMA-MODELFLAGS] log).</summary>
    ''' <summary>Resuelve el AddonIndex selector para una ARMO multi-addon.
    ''' Devuelve un Integer con el INDX a forzar (e.g. Gunner Heavy = 2), o `Nothing`
    ''' (= "cargar TODOS los addons compatibles" — comportamiento default del engine).
    '''
    ''' El engine vanilla carga TODAS las ARMAs del array Models filtradas por raza/género.
    ''' La única forma de seleccionar UNA específica es vía OMOD AddonIndex Property (idx 7
    ''' de wbArmorPropertyEnum) disparada por una OBTS combination cuya Keywords matcheen
    ''' el contexto (LVLI.LLKC). Si NO hay tal match → cargar todos. Esto distingue:
    '''   - Caso Sturgess/Wastelander Heavy: ARMO empaqueta torso + gloves (multi-piece set)
    '''     sin keywords contextuales → cargar todos los addons.
    '''   - Caso Gunner Combat Torso: keyword `if_tmp_armor_Heavy` → OBTS combo "Pesado" →
    '''     OMOD `mod_armor_Combat_Torso_Size_C` con AddonIndex Property = 2 → cargar SOLO INDX=2.
    '''
    ''' BaseAddonIndex (FNAM byte 2-3) NO se usa como filtro per se — es el "default address"
    ''' al que apunta el ARMO si nadie lo modifica, pero el engine sigue cargando los demás
    ''' addons salvo override. Por eso lo ignoramos como selector exclusivo.
    '''
    ''' Spec: wbDefinitionsFO4.pas:6187-6192 (Models = INDX+MODL solamente, sin flag de exclusión),
    ''' :1192-1245 (wbOBTEAddonIndexToStr describe override). Memoria arch_arma_sculpt_rule.md
    ''' confirma flujo Gunner como caso single-winner via OMOD chain.</summary>
    Private Function ResolveEffectiveAddonIndex(armo As ARMO_Data, ctxKeywords As List(Of UInteger)) As Integer?
        ' OBTS combinations override sólo cuando hay keyword match con el contexto.
        If ctxKeywords Is Nothing OrElse ctxKeywords.Count = 0 OrElse armo.Combinations Is Nothing Then
            Return Nothing
        End If

        Dim effectiveIdx As Integer = -1
        For Each combo In armo.Combinations
            If combo.Keywords Is Nothing OrElse combo.Keywords.Count = 0 Then Continue For
            Dim matches = False
            For Each kw In combo.Keywords
                If ctxKeywords.Contains(kw) Then
                    matches = True
                    Exit For
                End If
            Next
            If Not matches Then Continue For

            ' Layer 1: la OBTS combination misma puede dictar el AddonIndex via su s16
            ' "Parent Combination Index" (wbDefinitionsFO4.pas:5874). -1 = "no override desde la
            ' OBTS, dejar que un OMOD include lo decida". ≥0 = la combination fija el AddonIndex.
            If combo.ParentCombinationIndex >= 0 Then
                effectiveIdx = combo.ParentCombinationIndex
            End If

            ' Layer 2: cada OMOD include dentro de la combination puede sobrescribir via su
            ' AddonIndex Property. wbDefinitionsFO4.pas:5710+5842 — FunctionType=0 SET (overwrite),
            ' FunctionType=2 ADD (add to running value). Vanilla dump v2 (2026-05-10): 59 SET
            ' casos + 10 ADD casos confirman ambos. Walk ops en orden de declaración del OMOD.
            For Each omodFid In combo.IncludeOMODFormIDs
                Dim omodRec = _ctx.PluginManager.GetRecord(omodFid)
                If omodRec Is Nothing OrElse omodRec.Header.Signature <> "OMOD" Then Continue For
                Dim omod = CraftingRecordParsers.ParseOMOD(omodRec, _ctx.PluginManager)
                For Each addonOp In omod.GetAddonIndexOps()
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

            ' ── SSE skin-ARMA BOD2-ownership de-dup (2026-07-09) ─────────────────────────────────────────────
            ' RULE (record-faithful, NOT an engine replica): a naked-skin ARMA renders only the shapes whose
            ' BSDismember partition falls within the slot(s) its OWN BOD2 declares. A shape whose partitions are
            ' ALL outside this ARMA's BOD2 is redundant asset-reuse — it is provided by the dedicated ARMA for
            ' that slot (or by the FaceGen head) — and is dropped.
            '
            ' WHY: a handful of vanilla SSE skin meshes are all-in-one bundles. childfeet.nif (ARMA
            ' 'NakedFeetChild' 0x0006C5FA, BOD2=Feet(37)) carries 6 shapes on partitions {37,32,33,30,1,0}: the
            ' real Feet PLUS body(32)/hands(33) — already owned by NakedTorsoChild→childbody.nif and
            ' NakedHandsChild→childhands.nif — and head/eyes/mouth(0/1/30) owned by the FaceGen HDPT head. The
            ' app's skin-TXST override (WNAM NAM0/1) then paints the feet-ARMA's UpperBodyFemale onto the
            ' bundled ChildHead, z-fighting the FaceGen head. Honoring each ARMA's declared BOD2 leaves head←
            ' FaceGen, body←childbody, hands←childhands, feet←childfeet — each part once, its own texture.
            '
            ' BLAST RADIUS — MEASURED (Tools/ChildSkinNifProbe DUMP 5/8, whole load order): of the 120 Kind=Skin
            ' ARMAs (SkinNaked + every RACE.WNAM), exactly ONE drops a shape — childfeet. The other 78 with a
            ' dismember mesh are clean; 41 creatures have no partitions. So this is a general rule that currently
            ' fires ONLY on childfeet (5 child races). It is NOT a childfeet hardcode; a future all-in-one skin
            ' mod is handled the same way, and every drop is logged.
            '
            ' WHY Kind=Skin GATE IS ESSENTIAL (also measured): 14 NON-skin ARMAs (boots with leg geometry on
            ' part 32, farm clothes with shoes on part 37, armor mods with EnvironmentMap partitions ≠ BOD2)
            ' WOULD wrongly drop shapes if the rule applied to them — "partition ∉ BOD2" is common and LEGIT for
            ' outfits/armor. The gate keeps the rule off all of those. Also SSE-only + never drops on unreadable/
            ' no-dismember (keep-on-doubt), so FO4 and every ambiguous case are untouched.
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
                                    Dim bp As Integer = CInt(p.BodyPart)
                                    If bp >= 200 Then           ' fold SBP_2xx/1xx → base slot
                                        bp -= 200
                                    ElseIf bp >= 100 Then
                                        bp -= 100
                                    End If
                                    parts.Add(bp)
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
                    Else
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
                ' Fix Bug HIGH #1+#2: sub-sockets que esta chunk NIF expone también necesitan
                ' rename del ParentBoneName, sino sub-chunks se anclan al bone |0 equivocado.
                RenameSubSocketParentBones(nif, candidate.MountApIdx)
            End If

            ' RESUELTO (2026-06-14): los shapes skinned a bones del ACTOR (PackBase brahmin: Pelvis/
            ' Spine; brazos Mr Handy) NO necesitan el MountSocket — cabalgan los bones del actor que YA
            ' están posicionados. La "mala posición" que se veía eran los bones PRIVADOS del chunk
            ' (lag bones, etc.), ahora colocados bien por InjectChunkBonesIntoLiveSkeleton (regla:
            ' A=actorWorld(huesoCompartido)×bind; privados en A×inv(bind) — ver memoria
            ' arch_injected_bone_shared_bone_inference; brahmin validado sin regresión).
            ' Por eso NO se aplica el socket a los bind transforms de estos shapes: aplicarlo a shapes
            ' que cabalgan el actor DISTORSIONA (verificado 2026-05-13: ambos órdenes rompieron todo).
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

            ' Sólo HeadRear: copia material part-específico desde el .nif base a los shapes del
            ' _faceBones (que vanilla autoreó con material genérico basehumanfemaleskin).
            CopyBaseMaterialsToFaceBonesShapes(candidate, shapes)

            _materialResolver.ApplyShapeMaterialOverrides(candidate, state, shapes)

            ' Diagnostic: dump the shader AFTER both passes (CopyBaseMaterialsToFaceBonesShapes
            ' for HeadRear + ApplyShapeMaterialOverrides for everyone). Pairing with the
            ' [NIF-LOAD-RAW] above lets us see if either pass mutated the shader type.
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
                result.ShapeArmaFormID(shape) = sculptSourceFormID
                result.ShapeCategory(shape) = category
                result.ShapeCoveredByOutfit(shape) = candidate.IsCoveredByOutfit
                result.ShapeOccludedByHeadwear(shape) = candidate.IsOccludedByHeadwear
                result.ShapeZapHairParts(shape) = candidate.ZapParts
                ' Per-segment worn-slot occlusion (Fase 2): record the inputs ApplyRenderToggleVisibility
                ' uses to rebuild IRenderableShape.CoveredSlotsMask every render. Only worn items
                ' (Kind=Outfit) contribute — their OWN biped-slot mask + a per-candidate group id; head
                ' parts / skin store nothing (own slots = 0). The toggle recompute derives:
                '   • head-part mask = (OR of rendered worn items' slots) AND result.HeadOcclusionMask
                '     (the per-NPC, RACE-driven head-region slots from RaceUtil.RaceHeadOcclusionMask; slot 33
                '     NECK is never in it so the head→body seam is never torn), gated by Render headwear.
                '   • worn-item mask = OR of OTHER rendered groups' slots (ORDER / other-items rule, engine
                '     owner-slot branch 0x14035E22B; group id excludes the item's own shapes — shared-slot
                '     safe, so the Pipboy's slot 60 still hides the outfit's biped-60 forearm).
                ' Recomputing from the rendered subset (not baking a static mask here) is what lets a render
                ' toggle that hides an item (e.g. Pipboy under Render armor OFF) un-occlude its segments.
                If candidate.Kind = MainForm.MeshCandidateKind.Outfit Then
                    result.ShapeOwnSlots(shape) = candidate.SlotMask
                    result.ShapeSlotGroup(shape) = occGroupId
                End If
                result.ShapeUsesBodyTexture(shape) = candidate.UsesBodyTexture
                ' HDPT type=7 Meatcaps (CK enum 7=Meatcaps, ver wbDefinitionsFO4 + comment en
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
    ''' Remap el ParentBoneName de cada sub-socket en la misma sufijo |N que los shape bones.
    ''' Fix de Bug HIGH #1 + #2 del análisis 2026-05-15.</summary>
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

    ''' <summary>HeadRear-only: cuando el HDPT fue redirigido a su variant *_faceBones.nif (rigging
    ''' facial para FMRS), los shapes del _faceBones traen material genérico (basehumanfemaleskin)
    ''' en lugar del material part-específico del .nif base (basehumanfemalerear). Replicamos el
    ''' comportamiento del engine: rigging del _faceBones + material del base. Match per-shape por
    ''' nombre con sufijo "_faceBones" removido (case-insensitive). Sólo aplica si
    ''' candidate.BaseDictKeyForFaceBones está poblado (= HeadRear con redirect).</summary>
    Private Sub CopyBaseMaterialsToFaceBonesShapes(candidate As MainForm.MeshCandidate, shapes As IEnumerable(Of IRenderableShape))
        If candidate Is Nothing OrElse shapes Is Nothing Then Return
        If String.IsNullOrEmpty(candidate.BaseDictKeyForFaceBones) Then Return

        Dim baseKey = candidate.BaseDictKeyForFaceBones
        Dim baseLoc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(baseKey, baseLoc) Then
            Return
        End If

        Dim baseBytes = baseLoc.GetBytes()
        If baseBytes Is Nothing OrElse baseBytes.Length = 0 Then
            Return
        End If

        Dim baseNif As Nifcontent_Class_Manolo
        Try
            baseNif = New Nifcontent_Class_Manolo()
            baseNif.Load_Manolo(baseBytes)
        Catch ex As Exception
            Return
        End Try

        ' Index base materials by stripped name (sin "_faceBones") para hacer match con los
        ' shapes del _faceBones que sí tienen el sufijo. Case-insensitive.
        Dim baseByStripped As New Dictionary(Of String, Nifcontent_Class_Manolo.RelatedMaterial_Class)(StringComparer.OrdinalIgnoreCase)
        For Each kv In baseNif.BaseMaterials
            baseByStripped(NameUtils.StripFaceBonesSuffix(kv.Key)) = kv.Value
        Next

        Dim copied As Integer = 0
        Dim missed As Integer = 0
        For Each shape In shapes
            Dim shapeName = shape.ShapeName
            If String.IsNullOrEmpty(shapeName) Then Continue For
            Dim stripped = NameUtils.StripFaceBonesSuffix(shapeName)
            Dim baseMat As Nifcontent_Class_Manolo.RelatedMaterial_Class = Nothing
            If baseByStripped.TryGetValue(stripped, baseMat) AndAlso baseMat IsNot Nothing Then
                Dim relMat = shape.ShapeMaterial
                If relMat IsNot Nothing Then
                    relMat.material = baseMat.material
                    relMat.path = baseMat.path
                    copied += 1
                End If
            Else
                missed += 1
            End If
        Next
    End Sub

End Class
