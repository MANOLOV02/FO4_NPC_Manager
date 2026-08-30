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

''' <summary>Phase 2 of the MainForm split (Increment 3): the skin-override LIVE-PREVIEW fast-path.
''' When the user changes the NPC's skin (NPC.WNAM / LM SkinTemplate) in EditBody and the new skin
''' ARMO's mesh-path SET matches the currently-loaded one, this re-resolves TXST/MSWP per candidate and
''' re-applies materials in place (no VBO regen) — then rolls back + re-bakes the face tint + skin
''' SoftLight. Orchestrates across resolvers: NpcMeshCollector (CollectArmoCandidates),
''' NpcMaterialResolver (ApplyShapeMaterialOverrides / ResolveActorSkinTextureSet / ghoul head-rear),
''' NpcFaceTintResolver (RestoreCapturedDiffusesToPristine / ApplyFaceTintOverlay). The GL submission
''' (PostTextureUploadAction / MarkDirty / InvalidateRender) goes through host.PreviewCtl. DI: ctx +
''' the three resolvers + host-provider + shared _appliedPresets + Func delegates for the MainForm-
''' resident LM-skin-template resolver and the live _previewRequestVersion token. Was kept in MainForm
''' until MeshCollection was extracted (it depends on CollectArmoCandidates). See 61-perf-mainform-split.</summary>
Friend NotInheritable Class NpcSkinLivePreview
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _meshCollector As NpcMeshCollector
    Private ReadOnly _faceTintResolver As NpcFaceTintResolver
    Private ReadOnly _hostProvider As Func(Of NpcRenderHost)
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)
    Private ReadOnly _resolveLmSkinTemplate As Func(Of String, LmSkinTemplate)
    Private ReadOnly _previewRequestVersionProvider As Func(Of Integer)

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   meshCollector As NpcMeshCollector, faceTintResolver As NpcFaceTintResolver,
                   hostProvider As Func(Of NpcRenderHost),
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                   resolveLmSkinTemplate As Func(Of String, LmSkinTemplate),
                   previewRequestVersionProvider As Func(Of Integer))
        _ctx = ctx
        _materialResolver = materialResolver
        _meshCollector = meshCollector
        _faceTintResolver = faceTintResolver
        _hostProvider = hostProvider
        _appliedPresets = appliedPresets
        _resolveLmSkinTemplate = resolveLmSkinTemplate
        _previewRequestVersionProvider = previewRequestVersionProvider
    End Sub

    ''' <summary>Recompute the effective SkinFormID for an NPC by re-applying the same overlay
    ''' precedence chain that <see cref="ApplyPresetOverlayToNpcData"/> uses: LM SkinTemplate
    ''' bundle wins, then NPC.WNAM SkinFormIDOverride (Some(0) → fall back to RACE.WNAM), else
    ''' the raw NPC.WNAM. Used by the fast-path so a combo edit lands on state.SkinFormID
    ''' without re-running the full ResolveNPCBaseState pipeline.
    ''' Returns the effective FormID (may be 0 if no resolution succeeds).</summary>
    Private Function RecomputeEffectiveSkinFormID(rootNpcFormID As UInteger, raceFormID As UInteger,
                                                   rawNpcFormID As UInteger) As UInteger
        Dim raw = _ctx.GetParsedNpc(rawNpcFormID)
        Dim effective As UInteger = If(raw IsNot Nothing, raw.Record.Skin, 0UI)
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(rootNpcFormID, overlayPreset) AndAlso overlayPreset IsNot Nothing Then
            If overlayPreset.SkinFormIDOverride.HasValue Then
                effective = overlayPreset.SkinFormIDOverride.Value
            End If
            ' LM SkinTemplate ARMO wins (matches NpcRecordOverlay.ApplyPresetOverlayToNpcData order).
            If Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
                Dim tpl = _resolveLmSkinTemplate(overlayPreset.SkinTemplateId)
                If tpl IsNot Nothing AndAlso tpl.SkinArmoFormID <> 0UI Then
                    effective = tpl.SkinArmoFormID
                End If
            End If
        End If
        ' RACE.WNAM fallback: matches ApplyRaceFallbacks (state.SkinFormID = 0 → race.SkinFormID).
        If effective = 0UI AndAlso raceFormID <> 0UI Then
            Dim raceRec = _ctx.PluginManager.GetRecord(raceFormID)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                ' Por SkinDe y no por .Skin a pelo: es la MISMA ley que el guardado, y este es el
                ' preview EN VIVO de la piel. Con dos copias, el día que la ley cambie el preview
                ' muestra una piel y el ESP graba otra — RENDER == BAKE.
                effective = Canon.CanonInterpretacion.SkinDe(_ctx.ParseRaceCanonCached(raceRec))
            End If
        End If
        Return effective
    End Function

    ''' <summary>Resolve the body skin's MeshCandidates from the host's current state. A skin
    ''' ARMO commonly emits multiple candidates (NakedTorso + NakedHands) — one per ARMA in the
    ''' addon group — so the fast-path needs ALL of them, not just the first. Builds the same
    ''' candidates <see cref="CollectArmoCandidates"/> would emit during a full render, so the
    ''' fast-path uses byte-identical TXST/MSWP resolution as the normal pipeline.
    ''' Returns empty list when state.SkinFormID is 0 or no candidates could be built.</summary>
    Private Function ResolveBodySkinCandidates(state As MainForm.NPCVisualState) As List(Of MainForm.MeshCandidate)
        Dim candidates As New List(Of MainForm.MeshCandidate)
        If state Is Nothing OrElse state.SkinFormID = 0UI Then Return candidates
        Dim order As Integer = 0
        Dim warnings As New List(Of String)
        _meshCollector.CollectArmoCandidates(state.SkinFormID, state, MainForm.MeshCandidateKind.Skin, candidates, order, warnings)
        Return candidates
    End Function

    ''' <summary>Snapshot the (DictKey → shapes) map of the host's currently-loaded body-skin
    ''' shapes. Used by the fast-path to decide which shapes get which new candidate's TXST/MSWP
    ''' applied without walking <see cref="MainForm.PreviewResolutionResult.Shapes"/> twice.</summary>
    Private Function GroupBodySkinShapesByMeshPath(renderData As MainForm.PreviewResolutionResult) As Dictionary(Of String, List(Of IRenderableShape))
        Dim groups As New Dictionary(Of String, List(Of IRenderableShape))(StringComparer.OrdinalIgnoreCase)
        If renderData Is Nothing OrElse renderData.Shapes Is Nothing Then Return groups
        For Each shape In renderData.Shapes
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat <> MainForm.ShapeRenderCategory.BodySkin AndAlso cat <> MainForm.ShapeRenderCategory.NakedHands Then Continue For
            Dim key As String = ""
            renderData.MeshDictKeys.TryGetValue(shape, key)
            If String.IsNullOrEmpty(key) Then Continue For
            Dim bucket As List(Of IRenderableShape) = Nothing
            If Not groups.TryGetValue(key, bucket) Then
                bucket = New List(Of IRenderableShape)
                groups(key) = bucket
            End If
            bucket.Add(shape)
        Next
        Return groups
    End Function

    ''' <summary>Fast-path for skin override changes (NPC.WNAM / LM SkinTemplate combos in
    ''' EditBody). When the new skin ARMO's mesh-path SET matches the currently-loaded one, we
    ''' re-resolve TXST + MSWP per candidate and call <see cref="ApplyShapeMaterialOverrides"/>
    ''' over the matching shapes — material fields mutate in place, no VBO regeneration. ~1ms
    ''' instead of ~50-100ms for a full reload.
    '''
    ''' A skin ARMO normally emits 2 ARMAs (NakedTorso + NakedHands) → 2 candidates with distinct
    ''' DictKeys. The fast-path matches them by DictKey: same SET of mesh paths in the new skin
    ''' as in the old one ⇒ apply each candidate to its corresponding shape group. Any DictKey
    ''' missing on either side ⇒ bail to the full reload (different geometry layout).
    '''
    ''' Returns False when the mesh path set differs or state/render data is incomplete. The
    ''' fast-path does NOT diverge from the normal render — it calls the same
    ''' CollectArmoCandidates + ApplyShapeMaterialOverrides helpers, so any change to those
    ''' automatically flows here too.</summary>
    Public Function RefreshBodySkinLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _hostProvider()
        If host?.LastRenderedState Is Nothing OrElse host?.LastRenderData Is Nothing Then
            Return False
        End If

        ' If the active LM template carries head / headRear HDPT swaps, the fast-path can't
        ' reapply them because (a) we don't track HDPT shapes by PartType in LastRenderData,
        ' and (b) a HDPT swap may bring a different mesh path that requires geometry reload.
        ' Bail to the full reload so ResolveNPCBaseState picks up the bundle correctly.
        ' face TXST (state.HeadTextureFormID) is just a texture override — that COULD be
        ' fast-pathed, but skipping it together keeps the rule simple and consistent: any
        ' face-side LM bundle ⇒ full reload.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(host.LastRenderedState.RootNpcFormID, overlayPreset) AndAlso overlayPreset IsNot Nothing _
           AndAlso Not String.IsNullOrEmpty(overlayPreset.SkinTemplateId) Then
            Dim tpl = _resolveLmSkinTemplate(overlayPreset.SkinTemplateId)
            If tpl IsNot Nothing Then
                Dim genderIdx As Integer = If(host.LastRenderedState.IsFemale, 1, 0)
                If tpl.HeadHdptFormID(genderIdx) <> 0UI _
                   OrElse tpl.HeadRearHdptFormID(genderIdx) <> 0UI _
                   OrElse tpl.FaceTxstFormID(genderIdx) <> 0UI Then
                    Return False
                End If
            End If
        End If

        ' Sync host state's SkinFormID with the overlay BEFORE resolving candidates. The host
        ' state was set up at the previous render; the overlay (where the combo writes) is the
        ' live source of truth. Without this the candidates resolve against the OLD skin.
        Dim modelFormID = NpcStateFactory.FaceAppearanceSourceFormID(host.LastRenderedState)
        Dim oldSkinFid = host.LastRenderedState.SkinFormID
        host.LastRenderedState.SkinFormID = RecomputeEffectiveSkinFormID(
            host.LastRenderedState.RootNpcFormID, host.LastRenderedState.RaceFormID, modelFormID)
        Dim newSkinFid = host.LastRenderedState.SkinFormID

        Dim newCandidates = ResolveBodySkinCandidates(host.LastRenderedState)
        If newCandidates.Count = 0 Then
            Return False
        End If

        ' Group existing body-skin shapes by their mesh path. This is the "old" set — the shapes
        ' currently uploaded to the GL.
        Dim oldGroups = GroupBodySkinShapesByMeshPath(host.LastRenderData)
        Dim oldKeys = String.Join(",", oldGroups.Keys.OrderBy(Function(k) k))
        Dim newKeys = String.Join(",", newCandidates.Select(Function(c) c.DictKey).OrderBy(Function(k) k))

        ' Path SET must match exactly — same count, same DictKeys (case-insensitive). Otherwise
        ' the new skin has a different geometry layout (more/fewer ARMAs, or a different mesh
        ' path) and we can't safely re-apply materials over the old shapes.
        If newCandidates.Count <> oldGroups.Count Then
            Return False
        End If
        For Each cand In newCandidates
            If Not oldGroups.ContainsKey(cand.DictKey) Then
                Dim missing = cand.DictKey
                Return False
            End If
        Next

        ' Path sets match. Apply each new candidate's TXST/MSWP to its corresponding shape group.
        '
        ' RESTAURAR ANTES DE RE-APLICAR. `EnsureShapeMaterialResolved` MEMOIZA (MaterialResolver:117:
        ' si el shape ya tiene material, Return), así que estas shapes traen el material YA MUTADO por el
        ' render anterior y esto era "override sobre override": todo slot que el TXST NUEVO deje vacío
        ' conservaba el del skin VIEJO (`TxstSlotDecision` → `skip:empty-path (kept=...)`), y el `path`
        ' quedaba con el del MSWP anterior. Re-derivar el par autorado y correr la ley completa encima hace
        ' que el fast path arranque del MISMO estado que el render completo — que es lo único que puede
        ' sostener el gate A/A.
        Dim totalShapes As Integer = 0
        For Each cand In newCandidates
            Dim shapesForPath = oldGroups(cand.DictKey)
            For Each sh In shapesForPath
                If Not NpcMaterialResolver.TryRestoreAuthoredMaterial(sh) Then Return False
                ' El mapa shape→candidate apuntaba al candidate del skin ANTERIOR: los candidates de
                ' cuerpo del fast path son objetos NUEVOS (ResolveBodySkinCandidates → CollectArmoCandidates)
                ' y nadie lo reescribía. Con este fix ese mapa pasa a ser la fuente de la ley única del fast
                ' path (ver los dos bloques de abajo), y además lo lee después NpcFaceTintResolver
                ' (ApplyMaterialPaletteHairColor). Stale ahí = candidate viejo alimentando reglas nuevas.
                host.LastRenderData.ShapeCandidate(sh) = cand
            Next
            _materialResolver.ApplyShapeMaterialOverrides(cand, host.LastRenderedState, shapesForPath)
            totalShapes += shapesForPath.Count
        Next

        ' Skin-tint substitution on OUTFIT shapes — outfit shapes with material.NifShaderType =
        ' SkinTint (escote, brazos expuestos, etc.) read their diffuse/normal/spec from the
        ' actor's body-skin TXST (race-specific). Without re-applying this here, an outfit
        ' rendered against the OLD skin still shows the OLD body diffuse on its skin patches
        ' even after the body shape itself updated.
        '
        ' El render completo lo hace dentro de ApplyShapeMaterialOverrides cuando candidate.Kind=Outfit,
        ' leyendo state.SkinFormID vía ResolveActorSkinTextureSet — y acá se llama a ESA MISMA función, con
        ' los candidates que SÍ están cacheados en renderData.ShapeCandidate. (El comentario que estaba acá
        ' decía lo contrario —"no los tenemos cacheados, así que replicamos la sustitución a mano"— y era
        ' falso desde antes de este cambio: la función de head parts de abajo ya los venía leyendo.)
        Dim outfitSkinTintShapes As Integer = 0
        If Not TryRefreshOutfitCandidatesAfterBodySkinChange(host, outfitSkinTintShapes) Then Return False

        ' Same idea for HeadPart shapes: HDPTs whose CK flag UsesBodyTexture=True read their diffuse
        ' from the body skin TXST, and the ghoul-female head-rear pulls the vanilla-UV body texture
        ' clone. The fast-path must re-derive both against the now-updated state.SkinFormID — otherwise
        ' a ghoul → human skin swap leaves the headRear with the old diffuse.
        Dim headPartBodyTexShapes As Integer = 0
        If Not TryRefreshHeadPartCandidatesAfterBodySkinChange(host, headPartBodyTexShapes) Then Return False

        ' [TEST: fastpath-skin-softlight] Re-bake softlight + face tints after the skin swap.
        ' Original fastpath called RefreshRender (paint-only) and skipped TryApplyBodySkinSoftLight,
        ' so the new body diffuse rendered without the QNAM softlight that the full render bakes.
        ' Replicates the RefreshFaceTintLivePreview pattern: rollback every captured diffuse to
        ' pristine, then route through MarkDirty(Textures) + InvalidateRender so Process_Textures_GL
        ' picks up any new diffuse paths (Texture-only branch, async upload + PostTextureUploadAction
        ' hook fires when ready). Caso (1) mismo path → hook sync inmediato; caso (2) path nuevo →
        ' espera al upload y rebakea sobre la textura nueva.
        Dim model = host.PreviewCtl?.Model
        If model Is Nothing Then
            Return False
        End If
        If Not _faceTintResolver.RestoreCapturedDiffusesToPristine(model, host) Then
            Return False
        End If

        Dim capturedState = host.LastRenderedState
        Dim capturedRenderData = host.LastRenderData
        Dim capturedHost = host
        Dim capturedRequestVersion = _previewRequestVersionProvider()
        host.PreviewCtl.Intent.PostTextureUploadAction = Sub(m)
                                                             If capturedHost Is Nothing OrElse capturedHost.IsDisposed Then Return
                                                             If capturedRequestVersion <> _previewRequestVersionProvider() Then Return
                                                             _faceTintResolver.ApplyFaceTintOverlay(capturedState, capturedRenderData, capturedHost)
                                                         End Sub
        host.PreviewCtl.Intent.MarkDirty(RenderDirtyFlags.Textures)
        host.PreviewCtl.InvalidateRender()

        ' MARCADOR DEL GATE A/A. Sin esta línea no hay forma de saber, leyendo el log, si un cambio de
        ' piel salió por el fast path o por la recarga completa — y distinguir eso es EXACTAMENTE lo que el
        ' gate necesita: compara el `[SHAPEMAT-FINAL*]`/`[SHADER-CMP]` de un cambio fast-path contra el de
        ' un render completo del MISMO estado, y deben ser idénticos. Si la línea no aparece, el cambio se
        ' fue por `RenderInHostAsync` (EditBody_Form.TriggerSkinChangeReload) y esa corrida NO sirve como
        ' lado A del gate.
        If Logger.Enabled Then
            Dim bodyN = totalShapes, outN = outfitSkinTintShapes, headN = headPartBodyTexShapes
            Dim oldFid = oldSkinFid, newFid = newSkinFid
            Logger.LogLazy(Function() $"[SKINFAST] APLICADO skin 0x{oldFid:X8}→0x{newFid:X8} shapes: body={bodyN} outfit={outN} headPart={headN}")
        End If
        Return True
    End Function

    ''' <summary>Re-resuelve los candidates de OUTFIT tras un cambio de piel del actor: restaura el material
    ''' AUTORADO de sus shapes y vuelve a correr <c>ApplyShapeMaterialOverrides</c>, que es la ley completa
    ''' (MSWP + ColorRemap + OMOD + TXST + tints + sustitución de piel). Hace falta porque un outfit
    ''' renderizado contra la piel VIEJA sigue mostrando su diffuse en los parches de piel expuesta.
    '''
    ''' UNA LEY, UN LUGAR. Acá vivía una RÉPLICA A MANO de la sustitución de piel del render (resolver
    ''' body/hand TXST, elegir región por shape, cargar el MNAM, `ApplyTextureSetToMaterial`). Se borró: era
    ''' una segunda copia de la misma ley sobre la misma entrada, y este camino es SÓLO-RENDER e inalcanzable
    ''' desde el CLI ⇒ el arnés de NIFs es CIEGO a él por definición y el drift no se detectaba.
    '''
    ''' Se procesa el CANDIDATE COMPLETO, no sólo sus shapes SkinTint: `ApplyShapeMaterialOverrides`
    ''' razona a nivel candidate (MSWP/ColorRemap/OMOD), así que darle un subconjunto lo haría divergir del
    ''' render por otro lado. Amplía la superficie respecto de la versión anterior, a propósito.
    '''
    ''' Restaurar SIN volver a aplicar deja el shape en crudo, por eso las dos cosas van juntas y
    ''' cualquier fallo intermedio devuelve False ⇒ el caller cae a la recarga completa, que reconstruye las
    ''' shapes desde cero (`NpcMeshCollector.LoadNifShapes`) y descarta las medio-restauradas.
    '''
    ''' Devuelve False si hay que caer a la recarga completa. <paramref name="touched"/> = shapes tocadas.</summary>
    Private Function TryRefreshOutfitCandidatesAfterBodySkinChange(host As NpcRenderHost, ByRef touched As Integer) As Boolean
        touched = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return False

        ' UNA LEY, UN LUGAR. Acá había una RÉPLICA A MANO del bloque de sustitución de piel del render
        ' (resolver body/hand TXST, elegir región por shape, cargar el MNAM, ApplyTextureSetToMaterial).
        ' Eso es una segunda copia de la misma ley, mantenida a mano, sobre la misma entrada — y ya había
        ' driftado antes. Ahora se restaura el material autorado y se corre `ApplyShapeMaterialOverrides`,
        ' que ES la ley: MSWP + ColorRemap + OMOD + TXST + tints + sustitución de piel, en el orden del motor.
        '
        ' POR CANDIDATE COMPLETO, no por el subconjunto SkinTint. `ApplyShapeMaterialOverrides` aplica
        ' MSWP/ColorRemap/OMOD a nivel CANDIDATE: si se le pasara sólo las shapes SkinTint vería un conjunto
        ' distinto al del render y el fast path divergiría por otro lado. Sí, esto amplía la superficie
        ' respecto de la versión anterior — a propósito, y hacia lo que hace el render completo.
        Dim byCandidate As New Dictionary(Of MainForm.MeshCandidate, List(Of IRenderableShape))()
        For Each shape In renderData.Shapes
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            ' Only outfit categories — body-skin shapes (BodySkin/NakedHands) were handled by the
            ' Skin candidate pass above.
            If cat <> MainForm.ShapeRenderCategory.Underarmor _
               AndAlso cat <> MainForm.ShapeRenderCategory.ArmorOver _
               AndAlso cat <> MainForm.ShapeRenderCategory.GloveOutfit _
               AndAlso cat <> MainForm.ShapeRenderCategory.Headwear Then Continue For

            ' Sin candidate no hay ley que correr ⇒ recarga completa. Antes acá se toleraba el Nothing
            ' porque ResolveSkinRegionForOutfit(Nothing) devolvía Body; ahora el candidate ES la entrada
            ' de todo el pipeline, así que adivinarlo sería inventar un render.
            Dim shapeCand As MainForm.MeshCandidate = Nothing
            If Not renderData.ShapeCandidate.TryGetValue(shape, shapeCand) OrElse shapeCand Is Nothing Then Return False

            Dim bucket As List(Of IRenderableShape) = Nothing
            If Not byCandidate.TryGetValue(shapeCand, bucket) Then
                bucket = New List(Of IRenderableShape)()
                byCandidate(shapeCand) = bucket
            End If
            bucket.Add(shape)
        Next

        For Each kv In byCandidate
            For Each sh In kv.Value
                If Not NpcMaterialResolver.TryRestoreAuthoredMaterial(sh) Then Return False
            Next
            _materialResolver.ApplyShapeMaterialOverrides(kv.Key, state, kv.Value)
            touched += kv.Value.Count
        Next
        Return True
    End Function

    ''' <summary>Re-apply body-skin textures to HeadPart shapes when the actor body skin changed via
    ''' the fast-path. Two cases: (1) HDPTs with UsesBodyTexture=True get the actor's body TXST
    ''' re-applied; (2) the ghoul-female head-rear gets its vanilla-UV body texture clone re-derived
    ''' against the now-updated state.SkinFormID (shared MainForm helper, gated on the shape's
    ''' candidate). Without this a ghoul → human skin swap leaves the headRear with the OLD diffuse.
    ''' Returns the shape count touched.</summary>
    Private Function TryRefreshHeadPartCandidatesAfterBodySkinChange(host As NpcRenderHost, ByRef touched As Integer) As Boolean
        touched = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return False

        ' ACÁ HABÍA UN `If bodyTxst Is Nothing Then Return 0` QUE VOLVÍA ESTE REFRESH INALCANZABLE
        ' JUSTO EN EL CASO QUE MOTIVÓ TODO ESTO. Con una raza cuyo armature de piel no declara skin TXST
        ' (UBE: NAM0=NAM1=0), `ResolveActorSkinTextureSet` devuelve Nothing ⇒ early return ⇒ un HDPT con
        ' UsesBodyTexture=True se quedaba con la diffuse/normal/spec del skin ANTERIOR, y de paso se
        ' salteaba el clon de head-rear ghoul, que vivía DESPUÉS del return. "Sin TXST" es un RESULTADO
        ' válido de la resolución, no una condición de salida.
        '
        ' Se recorre por CANDIDATE (igual que los outfits) y se incluyen SÓLO los candidates que poseen al
        ' menos una shape que este refresh debe tocar — UsesBodyTexture o head-rear ghoul: el MISMO conjunto
        ' de shapes de antes. De esos candidates se toman TODAS sus shapes, por la misma razón que en los
        ' outfits: `ApplyShapeMaterialOverrides` razona a nivel candidate.
        '
        ' POR QUÉ AGRUPAR POR CANDIDATE NO ARRASTRA LA CARA — la razón es ESTRUCTURAL, no un dato:
        ' `ShapeUsesBodyTexture` es por CANDIDATE, no por shape (MainForm :730-736: "True iff the shape's
        ' owning CANDIDATE had UsesBodyTexture=True"), así que es uniforme dentro de un candidate y no puede
        ' colar un hermano sin el flag. Y si algún día un HDPT de CARA declara el flag DATA 0x40, esa shape
        ' entra — y ESTÁ BIEN que entre, porque el render completo también la trataría así (render == render).
        ' NO escribir "la cara no entra porque no lleva UsesBodyTexture": eso describe los datos de hoy y
        ' se lee como un invariante que no existe.
        ' Tampoco pisa FaceGen: la textura compuesta no vive en el material (SSE va por `SseFoldedDiffuseKey`
        ' en MaterialData, FO4 compone en la textura GL keyed por path), y el caller vuelve a correr
        ' RestoreCapturedDiffusesToPristine + ApplyFaceTintOverlay después de esto.
        Dim byCandidate As New Dictionary(Of MainForm.MeshCandidate, List(Of IRenderableShape))()
        Dim needsRefresh As New HashSet(Of MainForm.MeshCandidate)()
        For Each shape In renderData.Shapes
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat <> MainForm.ShapeRenderCategory.HeadPart Then Continue For

            Dim usesBody As Boolean = False
            renderData.ShapeUsesBodyTexture.TryGetValue(shape, usesBody)

            Dim shapeCand As MainForm.MeshCandidate = Nothing
            If Not renderData.ShapeCandidate.TryGetValue(shape, shapeCand) OrElse shapeCand Is Nothing Then
                ' Sin candidate no se puede correr la ley. Si la shape NO necesitaba refresh, es inocuo
                ' saltearla; si SÍ (UsesBodyTexture), bailamos a la recarga completa en vez de aplicarle
                ' una versión degradada de la regla — que es lo que hacía antes
                ' (ApplyTextureSetOverrides con isHeadPartTextureSet=False derivado de un candidate nulo).
                If usesBody Then Return False
                Continue For
            End If

            Dim bucket As List(Of IRenderableShape) = Nothing
            If Not byCandidate.TryGetValue(shapeCand, bucket) Then
                bucket = New List(Of IRenderableShape)()
                byCandidate(shapeCand) = bucket
            End If
            bucket.Add(shape)

            ' Head-rear ghoul: el clon de textura con UV vanilla lo aplica ApplyShapeMaterialOverrides
            ' internamente (NpcMaterialResolver :1503), así que acá sólo marca al candidate como "hay que
            ' re-resolverlo" — no se re-implementa nada.
            If usesBody OrElse NpcMaterialResolver.IsGhoulHeadRearCase(shapeCand.HeadPartHdptFormID, shapeCand.HeadPartType, state) Then
                needsRefresh.Add(shapeCand)
            End If
        Next

        For Each cand In needsRefresh
            Dim shapesForCand = byCandidate(cand)
            For Each sh In shapesForCand
                If Not NpcMaterialResolver.TryRestoreAuthoredMaterial(sh) Then Return False
            Next
            _materialResolver.ApplyShapeMaterialOverrides(cand, state, shapesForCand)
            touched += shapesForCand.Count
        Next
        Return True
    End Function

End Class
