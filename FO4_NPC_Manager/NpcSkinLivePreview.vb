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
''' until MeshCollection was extracted (it depends on CollectArmoCandidates). See project_mainform_split.</summary>
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
        Dim effective As UInteger = If(raw IsNot Nothing, raw.SkinFormID, 0UI)
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
                effective = _ctx.ParseRaceCached(raceRec).SkinFormID
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
        Dim totalShapes As Integer = 0
        For Each cand In newCandidates
            Dim shapesForPath = oldGroups(cand.DictKey)
            _materialResolver.ApplyShapeMaterialOverrides(cand, host.LastRenderedState, shapesForPath)
            totalShapes += shapesForPath.Count
        Next

        ' Skin-tint substitution on OUTFIT shapes — outfit shapes with material.NifShaderType =
        ' SkinTint (escote, brazos expuestos, etc.) read their diffuse/normal/spec from the
        ' actor's body-skin TXST (race-specific). Without re-applying this here, an outfit
        ' rendered against the OLD skin still shows the OLD body diffuse on its skin patches
        ' even after the body shape itself updated.
        '
        ' The render normal does this inside ApplyShapeMaterialOverrides when candidate.Kind=Outfit
        ' (line ~7375), reading state.SkinFormID via ResolveActorSkinTextureSet. We can't re-call
        ' ApplyShapeMaterialOverrides on outfit candidates here (we don't have them cached in
        ' LastRenderData), so we replicate the per-shape texture sub directly.
        Dim outfitSkinTintShapes = ApplyOutfitSkinTintRefreshAfterBodySkinChange(host)

        ' Same idea for HeadPart shapes: HDPTs whose CK flag UsesBodyTexture=True read their diffuse
        ' from the body skin TXST, and the ghoul-female head-rear pulls the vanilla-UV body texture
        ' clone. The fast-path must re-derive both against the now-updated state.SkinFormID — otherwise
        ' a ghoul → human skin swap leaves the headRear with the old diffuse.
        Dim headPartBodyTexShapes = ApplyHeadPartBodyTextureRefreshAfterBodySkinChange(host)

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
        Return True
    End Function

    ''' <summary>Re-apply the per-shape "outfit SkinTint texture sub" to all outfit shapes whose
    ''' material is SkinTint. Mirrors the inline block in <see cref="ApplyShapeMaterialOverrides"/>
    ''' (line ~7442) that runs during a full render but only for the outfit candidate currently
    ''' being processed. Here we run it on every outfit shape already in the model, because the
    ''' actor's body skin just changed and outfits of any category (Underarmor / Armor / Glove)
    ''' may have skin-exposed patches that need to follow.
    '''
    ''' Region is inferred from the shape's category (GloveOutfit → Hand, everything else → Body)
    ''' instead of from candidate.SlotMask, since we don't have outfit candidates cached.
    ''' Returns the shape count touched (for logging).</summary>
    Private Function ApplyOutfitSkinTintRefreshAfterBodySkinChange(host As NpcRenderHost) As Integer
        Dim count As Integer = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return 0

        ' Resolve body and hand TXSTs once (state.SkinFormID was already updated by the caller).
        Dim bodyTxst = _materialResolver.ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Body)
        Dim handTxst = _materialResolver.ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Hand)

        For Each shape In renderData.Shapes
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            ' Only outfit categories — body-skin shapes (BodySkin/NakedHands) were handled by the
            ' Skin candidate pass above.
            If cat <> MainForm.ShapeRenderCategory.Underarmor _
               AndAlso cat <> MainForm.ShapeRenderCategory.ArmorOver _
               AndAlso cat <> MainForm.ShapeRenderCategory.GloveOutfit _
               AndAlso cat <> MainForm.ShapeRenderCategory.Headwear Then Continue For

            Dim relMat = shape.ShapeMaterial
            If relMat Is Nothing Then Continue For
            Dim mat = relMat.material
            If mat Is Nothing Then Continue For
            If mat.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.SkinTint Then Continue For

            Dim chosenTxst = If(cat = MainForm.ShapeRenderCategory.GloveOutfit, handTxst, bodyTxst)
            If chosenTxst Is Nothing Then Continue For

            ' Same fragment as ApplyShapeMaterialOverrides body — only the diffuse/normal/spec
            ' get substituted; material params (specular, smoothness, etc.) stay from the NIF.
            If chosenTxst.MaterialPath <> "" Then
                Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(chosenTxst.MaterialPath, mat, shape.NifShape, shape.NifContent)
                If bgsmMaterial IsNot Nothing Then
                    If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then mat.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                    If bgsmMaterial.NormalTexture <> "" Then mat.NormalTexture = bgsmMaterial.NormalTexture
                    If bgsmMaterial.SmoothSpecTexture <> "" Then mat.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                End If
            End If
            NpcMaterialResolver.ApplyTextureSetToMaterial(mat, chosenTxst)
            count += 1
        Next
        Return count
    End Function

    ''' <summary>Re-apply body-skin textures to HeadPart shapes when the actor body skin changed via
    ''' the fast-path. Two cases: (1) HDPTs with UsesBodyTexture=True get the actor's body TXST
    ''' re-applied; (2) the ghoul-female head-rear gets its vanilla-UV body texture clone re-derived
    ''' against the now-updated state.SkinFormID (shared MainForm helper, gated on the shape's
    ''' candidate). Without this a ghoul → human skin swap leaves the headRear with the OLD diffuse.
    ''' Returns the shape count touched.</summary>
    Private Function ApplyHeadPartBodyTextureRefreshAfterBodySkinChange(host As NpcRenderHost) As Integer
        Dim count As Integer = 0
        Dim renderData = host.LastRenderData
        Dim state = host.LastRenderedState
        If renderData Is Nothing OrElse state Is Nothing Then Return 0

        ' HeadParts always pull from the BODY region (not Hand) — by definition they're
        ' face/headRear/etc., never hands. ResolveActorSkinTextureSet uses the now-updated
        ' state.SkinFormID so this matches what a full render would produce.
        Dim bodyTxst = _materialResolver.ResolveActorSkinTextureSet(state, MainForm.SkinRegion.Body)
        If bodyTxst Is Nothing Then Return 0

        For Each shape In renderData.Shapes
            Dim cat As MainForm.ShapeRenderCategory = MainForm.ShapeRenderCategory.Other
            renderData.ShapeCategory.TryGetValue(shape, cat)
            If cat <> MainForm.ShapeRenderCategory.HeadPart Then Continue For

            Dim relMat = shape.ShapeMaterial
            If relMat Is Nothing Then Continue For
            Dim mat = relMat.material
            If mat Is Nothing Then Continue For

            ' Ghoul-female head-rear: re-derive the vanilla-UV body texture clone against the
            ' now-updated state.SkinFormID and overwrite D/N/S (same single-source helper the full
            ' render / bake use). Gated on the shape's candidate (HDPT bare-id + race ∈ ghoul set).
            Dim shapeCand As MainForm.MeshCandidate = Nothing
            renderData.ShapeCandidate.TryGetValue(shape, shapeCand)
            If shapeCand IsNot Nothing AndAlso NpcMaterialResolver.IsGhoulHeadRearCase(shapeCand.HeadPartHdptFormID, shapeCand.HeadPartType, state) Then
                _materialResolver.ApplyGhoulHeadRearClonedTextures(shapeCand, state, mat, shape)
                count += 1
                Continue For
            End If

            Dim usesBody As Boolean = False
            renderData.ShapeUsesBodyTexture.TryGetValue(shape, usesBody)
            If Not usesBody Then Continue For

            ' Same body-skin sub flow ApplyShapeMaterialOverrides uses: load BGSM (if MNAM
            ' present in TXST), copy texture slots only, then apply the rest of the TXST.
            ' Material params (specular, smoothness, subsurface) stay from the NIF.
            If bodyTxst.MaterialPath <> "" Then
                Dim bgsmMaterial = MaterialResolver.TryLoadMaterialFromDictionary(bodyTxst.MaterialPath, mat, shape.NifShape, shape.NifContent)
                If bgsmMaterial IsNot Nothing Then
                    If bgsmMaterial.Diffuse_or_Base_Texture <> "" Then mat.Diffuse_or_Base_Texture = bgsmMaterial.Diffuse_or_Base_Texture
                    If bgsmMaterial.NormalTexture <> "" Then mat.NormalTexture = bgsmMaterial.NormalTexture
                    If bgsmMaterial.SmoothSpecTexture <> "" Then mat.SmoothSpecTexture = bgsmMaterial.SmoothSpecTexture
                End If
            End If
            NpcMaterialResolver.ApplyTextureSetToMaterial(mat, bodyTxst)
            count += 1
        Next
        Return count
    End Function
End Class
