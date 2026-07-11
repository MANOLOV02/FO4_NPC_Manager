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

''' <summary>Phase 2 of the MainForm split: FaceTint compositor EXECUTION — runs the
''' face-tint compositor + the skin SoftLight / subsurface pre-passes onto the GL textures,
''' snapshots/rolls back pristine diffuse pixels, and the live face-tint refresh path. Standalone
''' class, DI. The skin-override live-preview fast-path (RefreshBodySkinLivePreview et al.) stays in
''' MainForm because it is coupled to CollectArmoCandidates (MeshCollection, not yet extracted) and
''' calls back into ApplyFaceTintOverlay / RestoreCapturedDiffusesToPristine here. See project_mainform_split.</summary>
Friend NotInheritable Class NpcFaceTintResolver
    Private ReadOnly _ctx As NpcRenderContext
    Private ReadOnly _materialResolver As NpcMaterialResolver
    Private ReadOnly _hostProvider As Func(Of NpcRenderHost)
    Private ReadOnly _appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)

    ''' <summary>Process-lifetime cache of every face-tint DDS byte buffer we have ever pulled
    ''' from the FilesDictionary. Keyed by the normalized "textures\..." path. A Nothing entry
    ''' is a *negative* cache for paths that resolve to a missing or empty file, so we don't
    ''' retry the same lookup on the next NPC. Reused across NPCs of the same race (region masks
    ''' are identical) and across re-previews of the same NPC. Invalidate via ClearFaceTintCaches
    ''' when the FilesDictionary is rebuilt. (Owner moved from MainForm._tintBytesCache.)</summary>
    Private ReadOnly _tintBytesCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

    Public Sub New(ctx As NpcRenderContext, materialResolver As NpcMaterialResolver,
                   hostProvider As Func(Of NpcRenderHost),
                   appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset))
        _ctx = ctx
        _materialResolver = materialResolver
        _hostProvider = hostProvider
        _appliedPresets = appliedPresets
    End Sub

    ''' <summary>Run the face-tint compositor + the two skin-softlight pre-passes for the given
    ''' state. ALL targets (face/body diffuse) are required to be already uploaded into the GL
    ''' Textures_Dictionary before calling — that's the contract of the
    ''' <c>RenderIntent.PostTextureUploadAction</c> hook this is registered to. No defer
    ''' machinery: if the hook fired, textures are guaranteed ready.</summary>
    Friend Sub ApplyFaceTintOverlay(state As MainForm.NPCVisualState, renderData As MainForm.PreviewResolutionResult, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing Then Return

        ' Single skin-tone path: the slot-12 SkinTone (authored, or a QNAM stand-in synthesized
        ' in FaceTintLayerBuilder when the NPC authors none) is composed as a normal tint layer
        ' in engine rank order INSIDE TryApplyFaceTints. Detail tints ranked after slot 12 (brow,
        ' scars) therefore compose on top of the toned skin instead of being washed out by a
        ' separate full-face SoftLight post-pass (there is NO face-side skin-tone post-pass — the
        ' face skin tone lives entirely in the slot-12 composite, which mutates the face diffuse and
        ' is why only the FACE needs the pristine snapshot for live edit).
        TryApplyFaceTints(state, host)
        ' Render-only: make body skin light like the face (subsurface scattering). Runs before
        ' the SoftLight pass and is NOT gated by its skin-tone guards — see method docs.
        MatchBodySkinSubsurfaceToFace(host)
        TryApplyBodySkinSoftLight(state, host)
    End Sub

    ''' <summary>Render-only: copy the authoritative face material's subsurface-scattering
    ''' response onto every body skin material so face and body skin light identically. The
    ''' face material (BSLightingShaderType.FaceTint) "wins": its SubsurfaceLighting (on/off)
    ''' and SubsurfaceLightingRolloff are copied verbatim (including False) onto each body skin
    ''' material (the SkinTint flag, excluding the face itself). The render shader reads both
    ''' fields per material every draw (Render.vb: bSoftlight + subsurfaceRolloff), so this
    ''' mutation takes effect on the next frame with no texture work.
    '''
    ''' Sole precondition: a face material exists AND a body skin material exists — none of the
    ''' SoftLight guards (HasTextureLighting / race SkinTone catalog / QNAM opacity) apply,
    ''' because subsurface response is a material lighting property independent of skin TONE.
    ''' Runs at the render-finalization chokepoint (ApplyFaceTintOverlay), by which point every
    ''' shape's material is fully resolved (per-candidate ApplyShapeMaterialOverrides already
    ''' ran) and is not re-resolved again before the draw.
    '''
    ''' Render-only / no persistence: each shape owns a fresh material instance deserialized per
    ''' load (TryLoadMaterialFromDictionary: New + Deserialize — no shared cache), the FaceGen
    ''' bake builds its own material wrappers (FaceGenBuilder), and Save ESP never serializes
    ''' material fields. Values come from the loaded face material (its BGSM/inline shader), never
    ''' hardcoded. BGSM-only: the SubsurfaceLighting getter throws on non-BGSM/BGEM and BGEM has
    ''' no such field, so both source and targets are gated to BGSM-backed materials.</summary>
    Private Sub MatchBodySkinSubsurfaceToFace(host As NpcRenderHost)
        If host Is Nothing Then host = _hostProvider()
        Dim model = host?.PreviewCtl?.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] skip: model/meshes Nothing")
            Return
        End If

        ' Source: the authoritative face material (FaceTint shader, BGSM-backed).
        Dim faceFound As Boolean = False
        Dim faceOn As Boolean = False
        Dim faceRolloff As Single = 0.0F
        For Each fm In model.meshes
            If fm Is Nothing OrElse fm.MeshData Is Nothing OrElse fm.MeshData.Material Is Nothing Then Continue For
            Dim fmb = fm.MeshData.Material.MaterialBase
            If fmb Is Nothing Then Continue For
            If fmb.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
            If Not (TypeOf fmb.Underlying_Material Is BGSM) Then Continue For
            faceOn = fmb.SubsurfaceLighting
            faceRolloff = fmb.SubsurfaceLightingRolloff
            faceFound = True
            Exit For
        Next
        If Not faceFound Then
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] skip: no FaceTint source material in scene")
            Return
        End If

        Dim faceOnLog = faceOn
        Dim faceRollLog = faceRolloff
        Logger.LogLazy(Function() $"[BODY-SUBSURFACE] face source on={faceOnLog} rolloff={faceRollLog:F4}")

        ' Targets: body skin materials (SkinTint flag, not the face), BGSM-backed. Same shape
        ' set TryApplyBodySkinSoftLight touches.
        Dim applied As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim mb = mesh.MeshData.Material.MaterialBase
            If mb Is Nothing Then Continue For
            If Not mb.SkinTint Then Continue For
            If mb.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
            If Not (TypeOf mb.Underlying_Material Is BGSM) Then Continue For

            Dim preOn = mb.SubsurfaceLighting
            Dim preRoll = mb.SubsurfaceLightingRolloff
            If preOn = faceOn AndAlso preRoll = faceRolloff Then Continue For

            mb.SubsurfaceLighting = faceOn
            mb.SubsurfaceLightingRolloff = faceRolloff
            applied += 1
            Dim snLog = mesh.MeshData.Shape?.ShapeName
            Dim preOnL = preOn
            Dim preRollL = preRoll
            Dim newOnL = faceOn
            Dim newRollL = faceRolloff
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] shape='{snLog}' {preOnL}/{preRollL:F4} → {newOnL}/{newRollL:F4} (from face)")
        Next

        Dim appliedLog = applied
        Logger.LogLazy(Function() $"[BODY-SUBSURFACE] done applied={appliedLog}")
    End Sub

    ''' <summary>Build the per-NPC face tint inputs (region swaps + ordered layer list)
    ''' from the NPC's parsed records and tint preset overlays. Pure data — no GL state,
    ''' no Model touch, no Textures_Dictionary access. Used by both the live render path
    ''' (TryApplyFaceTints) and the standalone bake path (FaceGenBuilder.BakeFaceTextures)
    ''' so they share one source of truth for layer composition + ordering.
    '''
    ''' Returns Nothing values inside the tuple's npcData/race when the inputs can't be
    ''' resolved; layers/regionSwaps are always non-Nothing (empty list when nothing applies).</summary>

    Public Function BuildFaceTintLayerInputs(state As MainForm.NPCVisualState) As (
        layers As List(Of FaceTintLayerInput),
        regionSwaps As List(Of FaceRegionSwapInput),
        npcData As NPC_Data,
        race As RACE_Data)

        Dim emptyResult = (
            layers:=New List(Of FaceTintLayerInput),
            regionSwaps:=New List(Of FaceRegionSwapInput),
            npcData:=CType(Nothing, NPC_Data),
            race:=CType(Nothing, RACE_Data))

        If state Is Nothing Then Return emptyResult

        Dim modelFormID = NpcStateFactory.FaceAppearanceSourceFormID(state)
        ' Resolve the hair LUT path so slot Brows palette layers can drive their per-pixel
        ' grayscale-to-palette colour off the same LUT the hair/brow MESHES sample at render
        ' time. BGSM-first / RACE.HNAM fallback lives in ResolveHairPaletteTexture (single
        ' source of truth shared with the mesh-side ApplyMaterialPaletteHairColor).
        Dim hairLutPath As String = NpcMaterialResolver.ResolveHairPaletteTexture(_hostProvider(), state, _ctx.PluginManager)
        ' Diagnostic: dump what the brow tint will use (LUT path + HCLF RemappingIndex) alongside
        ' what each loaded hair/grayscale MESH material uses (GreyscaleTexture + GrayscaleToPaletteScale),
        ' so the two can be compared 1:1 against the [PALSCALE-WRITE] mesh log. Confirms palette
        ' (LUT) + index (scale) parity between the brow face-tint and the brow MESH.
        If Logger.Enabled Then
            Dim browHcfid = state.HairColorFormID
            Dim browClfmDiag = _materialResolver.ResolveColorFormData(browHcfid)
            Dim browRow As Single = If(browClfmDiag IsNot Nothing, browClfmDiag.RemappingIndex, -1.0F)
            Dim browHasRemap As Boolean = (browClfmDiag IsNot Nothing AndAlso browClfmDiag.HasRemappingIndex)
            Dim browHasColor As Boolean = (browClfmDiag IsNot Nothing AndAlso browClfmDiag.HasColor)
            Dim browLutKey = FO4UnifiedMaterial_Class.CorrectTexturePath(hairLutPath)
            Logger.LogLazy(Function() $"[BROW-LUT-RESOLVE] hairFid=0x{browHcfid:X8} hasColor={browHasColor} hasRemap={browHasRemap} row={browRow:F4} lutPath='{hairLutPath}' lutKey='{browLutKey}'")
            Dim model0 = _hostProvider()?.PreviewCtl?.Model
            If model0 IsNot Nothing AndAlso model0.meshes IsNot Nothing Then
                For Each mDiag In model0.meshes
                    If mDiag Is Nothing OrElse mDiag.MeshData Is Nothing OrElse mDiag.MeshData.Material Is Nothing Then Continue For
                    Dim mbDiag = mDiag.MeshData.Material.MaterialBase
                    If mbDiag Is Nothing Then Continue For
                    If Not (mbDiag.Hair OrElse mbDiag.GrayscaleToPaletteColor) Then Continue For
                    Dim shapeNm = If(mDiag.MeshData.Shape IsNot Nothing, mDiag.MeshData.Shape.ShapeName, "<?>")
                    Dim gtexDiag = If(mbDiag.GreyscaleTexture, "")
                    Dim gtexKeyDiag = FO4UnifiedMaterial_Class.CorrectTexturePath(gtexDiag)
                    Dim scaleDiag = mbDiag.GrayscaleToPaletteScale
                    Logger.LogLazy(Function() $"[BROW-MESH-LUT] shape='{shapeNm}' hair={mbDiag.Hair} g2p={mbDiag.GrayscaleToPaletteColor} scale={scaleDiag:F4} greyTex='{gtexDiag}' greyKey='{gtexKeyDiag}'")
                Next
            End If
        End If
        Dim built = FaceTintLayerBuilder.Build(
            modelFormID:=modelFormID,
            rootFormID:=state.RootNpcFormID,
            raceFormID:=state.RaceFormID,
            isFemale:=state.IsFemale,
            pluginManager:=_ctx.PluginManager,
            appliedPresets:=_appliedPresets,
            tintBytesCache:=_tintBytesCache,
            hairLutPath:=hairLutPath,
            hairColorFormID:=state.HairColorFormID,
            hasTextureLighting:=state.HasTextureLighting,
            textureLightingColorArgb:=state.TextureLightingColor.ToArgb(),
            parseRace:=AddressOf _ctx.ParseRaceCached)

        Return (built.Layers, built.RegionSwaps, built.NpcData, built.Race)
    End Function

    ''' <summary>Live-render path: build the layer inputs (shared with the bake) and apply
    ''' them onto the model's face textures via the compositor. Mutates Textures_Dictionary
    ''' GL Texture_IDs in place — same semantics this function had before the
    ''' BuildFaceTintLayerInputs extraction.</summary>
    Private Function TryApplyFaceTints(state As MainForm.NPCVisualState, Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing Then Return False

        Dim built = BuildFaceTintLayerInputs(state)
        If built.npcData Is Nothing Then Return True ' no NPC / no race / no tint layers
        Dim layerInputs = built.layers
        Dim regionSwaps = built.regionSwaps
        Dim npcData = built.npcData
        Dim race = built.race
        ' SSE composes the facetint from the NPC record directly (no FO4 tint-template layers), so an empty
        ' FO4 layer list must NOT short-circuit -- fall through to the mesh loop where the SSE branch runs.
        If layerInputs.Count = 0 AndAlso Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return True

        ' Find the face mesh in the model, get its diffuse texture cache entry, and call the
        ' compositor on a copy. Then mutate the cache entry's GL Texture_ID so the existing
        ' render path picks up the modified diffuse without any library changes.
        Dim model = host.PreviewCtl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Return True   ' no model — nothing we can do, don't retry forever
        End If

        Dim composedAny As Boolean = False
        Dim faceMeshFoundButTextureNotReady As Boolean = False
        Dim seenFaceMeshes As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        ' Diagnostic: when the FaceTint shader filter rejects every mesh (typical Ghoul/Child
        ' bug — the engine uses a different BSLightingShaderType for these races), enumerate
        ' every mesh's shape name + shader type so we can see what we DO have vs what we look
        ' for. Only emitted on the failure path below to keep the log compact.
        Dim shaderInventoryForDiag As New List(Of String)
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim shape = mesh.MeshData.Shape
            If shape Is Nothing Then Continue For

            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For

            ' The actual face shape uses the FaceTint shader (BSLightingShaderType). Other "head"
            ' shapes (BaseFemaleHeadRear with body texture, mouth, lashes, eyes) use SkinTint or
            ' EnvMap. Filtering by shader type avoids touching the headrear / mouth diffuses.
            If materialBase.NifShaderType <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then
                shaderInventoryForDiag.Add($"shape='{shape.ShapeName}' shader={materialBase.NifShaderType}")
                Continue For
            End If

            ' SSE: unlike FO4 (which bakes the facetint INTO the diffuse), the SSE engine keeps the facetint as
            ' a SEPARATE texture-set slot 6 that the FaceTint PS multiplies onto the albedo (verified
            ' sse_facegen_skin.asm t4). So compose the per-NPC facetint and install it as InnerLayerTexture; the
            ' shared render (bFacetintAlbedo -> texGlowmap) then applies it. Game-gated; FO4 keeps the path below.
            If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
                If ApplySseFacetint(materialBase, npcData, race, model, host) Then composedAny = True
                Continue For
            End If

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seenFaceMeshes.Contains(diffusePath) Then Continue For
            seenFaceMeshes.Add(diffusePath)

            ' Diffuse must be ready before we attempt anything — it's the channel every layer
            ' contributes to and it's the one whose dimensions drive the FBO size. If diffuse
            ' isn't loaded, signal "retry later".
            Dim diffuseEntry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, diffuseEntry) _
               OrElse diffuseEntry Is Nothing OrElse Not diffuseEntry.Loaded OrElse diffuseEntry.Texture_ID = 0 Then
                faceMeshFoundButTextureNotReady = True
                Continue For
            End If

            Dim w = diffuseEntry.Size.Width
            Dim h = diffuseEntry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Continue For
            End If

            ' Resolve N + S entries from the dict; passing 0 to the pipeline for any channel
            ' whose texture isn't loaded just skips that channel (compositor returns IsFresh=False).
            Dim normalPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.NormalTexture)
            Dim specPath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.SmoothSpecTexture)
            Dim normalEntry As PreviewModel.Texture_Loaded_Class = Nothing
            Dim specEntry As PreviewModel.Texture_Loaded_Class = Nothing
            model.Textures_Dictionary.TryGetValue(normalPath, normalEntry)
            model.Textures_Dictionary.TryGetValue(specPath, specEntry)
            Dim normalSrcId As Integer = If(normalEntry IsNot Nothing AndAlso normalEntry.Loaded, normalEntry.Texture_ID, 0)
            Dim specSrcId As Integer = If(specEntry IsNot Nothing AndAlso specEntry.Loaded, specEntry.Texture_ID, 0)

            ' Snapshot pristine bytes for live-edit rollback BEFORE the pipeline replaces IDs.
            ' The compositor calls GL.DeleteTexture on the previous fresh ID; without these
            ' snapshots a live tint edit can't roll back to a clean baseline (every refresh
            ' would compose on top of the previous bake).
            CapturePristineDiffusePixels(diffusePath, diffuseEntry.IsSRGB, host)
            ' Normal/specular get pristine snapshots only when their entries are present in the
            ' dict (otherwise there's nothing to roll back from on those channels).

            ' Run the shared compositor pipeline (region-swap → tint compose). Single source
            ' of truth for both render and bake; this caller is responsible for the dict
            ' swap below (the bake instead reads back + encodes the result IDs).
            ' baseDiffuseIsLinearOnGpu: el base se reusa de la textura del render (diffuseEntry). Si el render
            ' la cargó como SRV sRGB (IsSRGB=True), el sample YA es lineal ⇒ el seed encodea-only y NO la
            ' vuelve a srgbToLin (evita el doble-decode que introdujo el cambio de loader sRGB). El bake/CLI
            ' cargan el base crudo y pasan False (default).
            ' COMPOSITE = ESPEJO DEL SKINNING (Setting_GPUSkinning): GPU-skinning → composite GL
            ' (ApplyFaceTintPipeline); CPU-skinning → composite CPU (ComposeCpuPipeline, el MISMO que el bake, con
            ' paridad probada) + upload. El modo GPU queda IDÉNTICO al comportamiento previo (sin regresión).
            If Config_App.Current.Setting_GPUSkinning Then
                Dim pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                    host.CompositorState, host.TintGpuCache,
                    diffuseEntry.Texture_ID, normalSrcId, specSrcId,
                    w, h, layerInputs, regionSwaps,
                    baseDiffuseIsLinearOnGpu:=diffuseEntry.IsSRGB)

                ' Swap fresh IDs into the dict and delete the IDs they replaced. IsFresh=False
                ' means the channel had no contribution and the input ID stayed in place — no
                ' dict mutation, no delete.
                ApplyPipelineResultToDict(model, diffusePath, diffuseEntry, pipelineResult.Diffuse)
                If normalEntry IsNot Nothing Then ApplyPipelineResultToDict(model, normalPath, normalEntry, pipelineResult.Normal)
                If specEntry IsNot Nothing Then ApplyPipelineResultToDict(model, specPath, specEntry, pipelineResult.Specular)
                If pipelineResult.Diffuse.IsFresh OrElse pipelineResult.Normal.IsFresh OrElse pipelineResult.Specular.IsFresh Then
                    composedAny = True
                End If
            Else
                ' CPU-skinning: compose por CPU (mismos layers) desde los bytes source, y subir el resultado a GL.
                ' El diffuse sale g22 (formato bake); el render espera LINEAR (el path GL hace G22→Linear final),
                ' así que lo convertimos antes de subir. N/S ya son lineales. ⚠️ paridad GL==CPU en el render:
                ' verificar IN-APP (no testeable headless); si hay gamma, es este convert.
                If ApplyCpuComposeToDict(model, diffusePath, diffuseEntry, normalPath, normalEntry, specPath, specEntry,
                                         layerInputs, regionSwaps) Then composedAny = True
            End If

            ' "Ya está": the slot-12 skin tone is now BAKED into this face mesh's diffuse (the
            ' compositor processes it as the synthetic slot-12 layer). materialBase.SkinTint stays
            ' ENABLED (structural NIF/BGSM flag, never mutated) — instead we flag THIS mesh so Render
            ' makes the shader's own SkinTint soft-light a no-op for it. Without this the face gets the
            ' tone twice (baked composite + runtime soft-light of materialBase.SkinTintColor). The FO4
            ' body is untouched (SkinToneBaked stays False → engine-faithful runtime soft-light).
            mesh.MeshData.Material.SkinToneBaked = True
        Next

        ' If we found a face mesh but its texture wasn't ready, signal "retry later".
        ' If we composed at least one, success. Otherwise nothing matched — give up (no retry).
        If composedAny Then Return True
        If faceMeshFoundButTextureNotReady Then Return False
        Return True
    End Function

    ''' <summary>SSE: compose the per-NPC facetint (engine-exact, SseFaceTintComposer) and install it as the
    ''' FaceTint mesh's InnerLayerTexture (texture-set slot 6). The shared render multiplies it onto the albedo
    ''' (bFacetintAlbedo -> texGlowmap), matching the engine FaceTint PS (sse_facegen_skin.asm t4). NOT baked
    ''' into the diffuse (that is the FO4 path). Returns True when installed. SSE-only; the FO4 path is untouched.</summary>
    Private Function ApplySseFacetint(materialBase As FO4UnifiedMaterial_Class, npcData As NPC_Data, race As RACE_Data, model As PreviewModel,
                                      Optional host As NpcRenderHost = Nothing) As Boolean
        If npcData Is Nothing Then Return False
        ' race may be Nothing for SSE (the FO4 layer builder can return it unset); parse it from RaceFormID.
        If race Is Nothing AndAlso npcData.RaceFormID <> 0UI Then
            Dim rr = _ctx.PluginManager.GetRecord(npcData.RaceFormID)
            If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = _ctx.ParseRaceCached(rr)
        End If
        If race Is Nothing Then Return False
        Dim npcRec = _ctx.PluginManager.GetRecord(npcData.FormID)
        If npcRec Is Nothing Then Return False
        Const W As Integer = 512, H As Integer = 512
        Dim overlays = ResolveSseOverlays(npcData)
        Dim hasOverlays = overlays IsNot Nothing AndAlso overlays.Count > 0

        ' COMPOSITE = ESPEJO DEL SKINNING (Setting_GPUSkinning), IGUAL QUE FO4: GPU-skinning → compose GPU PURO
        ' (ApplyFaceTintPipeline sobre base plano 0.5 = el mismo del _2b); CPU-skinning → compose CPU (BakeFaceTintDds,
        ' WYSIWYG con el bake). NO se mezcla CPU↔GPU: el GPU corre SOLO cuando el facetint es tint-only (sin overlays,
        ' que hoy sólo tienen álgebra CPU en ApplyOverlays) — con overlays se compone TODO por CPU (puro CPU). Si el
        ' GPU falla, cae a CPU (nunca queda a medias). La paridad GPU==CPU del render la confirma el usuario in-app.
        Dim bgra As Byte() = Nothing
        If Config_App.Current.Setting_GPUSkinning AndAlso host IsNot Nothing AndAlso Not hasOverlays Then
            bgra = ComposeSseFacetintBgraGpu(npcRec, race, npcData, W, H, host)
        End If
        If bgra Is Nothing Then
            ' CPU (WYSIWYG con el bake): compose + RaceMenu overlays + BC3 encode (misma fn que el bake on-disk) y
            ' DECODE del BC3, así el preview muestra la textura baked+compressed exacta (incluye pérdida DXT5+overlays).
            Dim dds = SseFaceGenBaker.BakeFaceTintDds(_ctx.PluginManager, npcRec, race, npcData.RaceFormID, npcData.IsFemale, W, H, overlays, npcData.SseTintRaw, npcData.SseTintTexOverride)
            If dds Is Nothing Then Return False
            Dim dec = FaceTintCpuCompositor.DecodeDds(dds, W, H)
            If dec.Rgba Is Nothing Then Return False
            Dim b(W * H * 4 - 1) As Byte
            For i = 0 To W * H - 1
                b(i * 4) = ClampByte255(dec.Rgba(i * 4 + 2))       ' B
                b(i * 4 + 1) = ClampByte255(dec.Rgba(i * 4 + 1))   ' G
                b(i * 4 + 2) = ClampByte255(dec.Rgba(i * 4))       ' R
                b(i * 4 + 3) = 255
            Next
            bgra = b
        End If
        Dim newId = UploadRgba8Linear(bgra, W, H)
        If newId = 0 Then Return False
        Dim origin = _ctx.PluginManager.GetOriginatingPluginName(npcData.FormID)
        Dim fg = PluginManager.ToFaceGenLocalFormID(npcData.FormID)
        ' The engine facetint path (also what CK writes to NIF slot 6). Register the composed GL texture under
        ' this key so InnerLayerTexture_ID (GetTextureID) resolves it; it is linear, not sRGB.
        Dim facetintPath = $"textures\actors\character\facegendata\facetint\{origin}\{fg:X8}.dds"
        Dim key = FO4UnifiedMaterial_Class.CorrectTexturePath(facetintPath)
        Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
        If model.Textures_Dictionary.TryGetValue(key, entry) AndAlso entry IsNot Nothing Then
            If entry.Texture_ID <> 0 AndAlso entry.Texture_ID <> newId Then OpenTK.Graphics.OpenGL4.GL.DeleteTexture(entry.Texture_ID)
            entry.Texture_ID = newId : entry.Loaded = True : entry.Size = New System.Drawing.Size(W, H) : entry.IsSRGB = False
        Else
            model.Textures_Dictionary(key) = New PreviewModel.Texture_Loaded_Class With {.Texture_ID = newId, .Loaded = True, .Size = New System.Drawing.Size(W, H), .IsSRGB = False}
        End If
        materialBase.InnerLayerTexture = facetintPath
        Return True
    End Function

    ''' <summary>RaceMenu overlay layers (from the .jslot tintInfo overlay entries) for the compose — the same
    ''' list the bake passes so preview == bake. npcData carries them via the overlay; Nothing for vanilla NPCs
    ''' (ApplyOverlays no-ops), so the bake pipeline still runs identically.</summary>
    Private Function ResolveSseOverlays(npcData As NPC_Data) As IList(Of SseOverlayCompositor.SseOverlay)
        Return If(npcData IsNot Nothing, npcData.SseOverlays, Nothing)
    End Function

    ''' <summary>Rama GPU del render espejo del skinning: compone el facetint SSE (tint-only) PURO GPU — las MISMAS
    ''' capas que el CPU (<see cref="SseFaceTintComposer.BuildLayerInputs"/>) sobre un base PLANO = seed(0.5) vía
    ''' <see cref="FaceTintCompositor.ApplyFaceTintPipeline"/> (ley SSE all-linear), readback → BGRA lineal 512².
    ''' Es el MISMO compose que el <c>_2b</c> del bake. Base subido LINEAL (baseDiffuseIsLinearOnGpu) = seed 0.5-lin.
    ''' Sin capas (raza sin tints) → 0.5 plano. Nothing si el host/upload falla → el caller cae a CPU. GL-bound.</summary>
    Private Function ComposeSseFacetintBgraGpu(npcRec As PluginRecord, race As RACE_Data, npcData As NPC_Data, w As Integer, h As Integer, host As NpcRenderHost) As Byte()
        If host Is Nothing Then Return Nothing
        Dim npix = w * h
        Dim baseBgra(npix * 4 - 1) As Byte
        For i = 0 To npix - 1
            baseBgra(i * 4) = 128 : baseBgra(i * 4 + 1) = 128 : baseBgra(i * 4 + 2) = 128 : baseBgra(i * 4 + 3) = 255   ' seed 0.5
        Next
        Dim layers = SseFaceTintComposer.BuildLayerInputs(_ctx.PluginManager, npcRec, race, npcData.RaceFormID, npcData.IsFemale, npcData.SseTintRaw, npcData.SseTintTexOverride)
        If layers Is Nothing OrElse layers.Count = 0 Then Return baseBgra   ' sin tints → 0.5 plano (= seed)
        Dim baseTex = UploadRgba8Linear(baseBgra, w, h)
        If baseTex = 0 Then Return Nothing
        Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                          baseTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                          baseDiffuseIsLinearOnGpu:=True)
        Dim resultId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, baseTex)
        Dim gbuf(npix * 4 - 1) As Byte
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, resultId)
        Dim handle = System.Runtime.InteropServices.GCHandle.Alloc(gbuf, System.Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.GetTexImage(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        If resultId <> baseTex Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(resultId) : Catch : End Try
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(baseTex) : Catch : End Try
        Return gbuf
    End Function

    Private Shared Function ClampByte255(v As Double) As Byte
        Return CByte(Math.Max(0.0, Math.Min(255.0, Math.Round(v * 255.0))))
    End Function

    ''' <summary>Upload a BGRA byte buffer to a fresh GL Rgba8 (linear, non-sRGB) 2D texture. Mirrors the
    ''' pristine-restore upload; BGRA source order + PixelFormat.Bgra so the driver stores RGBA correctly.</summary>
    Private Shared Function UploadRgba8Linear(bgra As Byte(), w As Integer, h As Integer) As Integer
        Dim id = OpenTK.Graphics.OpenGL4.GL.GenTexture()
        If id = 0 Then Return 0
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, id)
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear))
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear))
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapS, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapT, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
        Dim handle = System.Runtime.InteropServices.GCHandle.Alloc(bgra, System.Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.TexImage2D(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0,
                OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8, w, h, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,
                handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        Return id
    End Function

    ''' <summary>Records the per-actor skin tone on every body SkinTint shape (hands/neck/body, NOT the
    ''' face) as material SkinTintColor + SkinTintAlpha. The SkinTint shader soft-lights the untoned body
    ''' diffuse with it at render (uEffectiveType==4, engine-faithful) — nothing is baked into the texture,
    ''' so the body needs NO pristine snapshot. Guarded by the race's SkinTone tint catalog (humans have it;
    ''' synth/ghoul/robot don't → skip; their skin shapes aren't human skin-tone).</summary>
    Private Sub TryApplyBodySkinSoftLight(state As MainForm.NPCVisualState, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing OrElse Not state.HasTextureLighting Then
            If Logger.Enabled Then
                Dim hasSt = (state IsNot Nothing AndAlso state.HasTextureLighting)
                Logger.LogLazy(Function() $"[SSE-BODY] EARLY-RETURN state Nothing or HasTextureLighting=False (hasTL={hasSt}) → body skin tone NOT applied")
            End If
            Return
        End If
        Dim raceRec = If(state.RaceFormID <> 0UI, _ctx.PluginManager.GetRecord(state.RaceFormID), Nothing)
        Dim race As RACE_Data = Nothing
        If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then race = _ctx.ParseRaceCached(raceRec)
        ' Game-aware skin-tone catalog guard. FO4: race's slot-12 SkinTone tint options. SSE: no slot-12 —
        ' the race's tint layer whose TINP mask type == 6 (RaceHasSkinToneLayer). FO4 branch unchanged.
        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
            If Not SseFaceTintComposer.RaceHasSkinToneLayer(_ctx.PluginManager, state.RaceFormID, state.IsFemale) Then
                If Logger.Enabled Then
                    Dim rfL = state.RaceFormID
                    Logger.LogLazy(Function() $"[SSE-BODY] EARLY-RETURN race 0x{rfL:X8} has NO skin-tone layer (TINP=6) → body skin tone NOT applied")
                End If
                Return
            End If
        Else
            If race Is Nothing OrElse race.FindTintOptionsBySlot(TintSlot.SkinTone, state.IsFemale).Count = 0 Then Return
        End If
        Dim model = host.PreviewCtl.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then Return
        Dim qnam = state.TextureLightingColor
        Dim opacity As Single = Math.Max(0.0F, Math.Min(1.0F, CSng(qnam.A) / 255.0F))
        If Logger.Enabled Then
            Dim qr = qnam.R, qg = qnam.G, qb = qnam.B, qa = qnam.A, opL = opacity
            Logger.LogLazy(Function() $"[SSE-BODY] guard OK. QNAM=({qr},{qg},{qb},A={qa}) opacity={opL:F3} → applying to body SkinTint shapes")
        End If
        If opacity <= 0.001F Then Return
        Dim appliedCount As Integer = 0
        For Each mesh In model.meshes
            If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
            Dim materialBase = mesh.MeshData.Material.MaterialBase
            If materialBase Is Nothing Then Continue For
            ' Body = SkinTint material que NO es la cara (la cara va por su propio path slot-12 del compositor).
            If Not materialBase.SkinTint Then Continue For
            If materialBase.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then Continue For
            ' A RaceMenu skin override with a key-7 tint already set this shape's SkinTintColor; skee replays the
            ' override over the base skin tone, so the override wins — don't overwrite it with the QNAM tone.
            If materialBase.SkinTintFromOverride Then Continue For
            ' Engine model: registrar el skin tone per-actor; el shader SkinTint lo soft-lightea sobre el
            ' diffuse untoned al render (NO se hornea). SkinTintAlpha lleva la opacidad del QNAM.
            materialBase.SkinTintColor = Color.FromArgb(qnam.R, qnam.G, qnam.B)
            materialBase.SkinTintAlpha = opacity
            appliedCount += 1
            If Logger.Enabled Then
                Dim shTy = materialBase.NifShaderType.ToString()
                Dim msn = materialBase.ModelSpaceNormals
                Dim nrm = If(materialBase.NormalTexture, "")
                Dim qr = qnam.R, qg = qnam.G, qb = qnam.B
                Logger.LogLazy(Function() $"[SSE-BODY] applied SkinTintColor=({qr},{qg},{qb}) to body shape shader={shTy} MSN={msn} normal='{nrm}'")
            End If
        Next
        If Logger.Enabled Then
            Dim ac = appliedCount
            Logger.LogLazy(Function() $"[SSE-BODY] total body SkinTint shapes tinted = {ac}")
        End If
    End Sub

    ''' <summary>Apply one channel's pipeline result to the model's Textures_Dictionary: swap
    ''' the fresh GL texture ID into the cache entry and delete the ID it replaced. No-op when
    ''' the pipeline reported IsFresh=False (channel had no contribution; input ID stayed in
    ''' place).</summary>
    ''' <summary>Composite CPU-skinning path (espejo del GL ApplyFaceTintPipeline): compone por CPU con los MISMOS
    ''' layers desde los bytes source (FilesDictionary), y sube cada canal a GL, swapeando el dict. El diffuse se
    ''' convierte g22→linear antes de subir (el render GL deja el output en linear; ver comentario del caller).
    ''' N/S se suben tal cual (ya lineales). Returns True si algún canal se compuso. GL-bound (corre en el hilo GL).</summary>
    Private Function ApplyCpuComposeToDict(model As PreviewModel,
                                           diffusePath As String, diffuseEntry As PreviewModel.Texture_Loaded_Class,
                                           normalPath As String, normalEntry As PreviewModel.Texture_Loaded_Class,
                                           specPath As String, specEntry As PreviewModel.Texture_Loaded_Class,
                                           layerInputs As IList(Of FaceTintLayerInput),
                                           regionSwaps As IList(Of FaceRegionSwapInput)) As Boolean
        Dim dB = FilesDictionary_class.GetBytes(diffusePath)
        If dB Is Nothing Then Return False
        Dim nB = If(Not String.IsNullOrEmpty(normalPath), FilesDictionary_class.GetBytes(normalPath), Nothing)
        Dim sB = If(Not String.IsNullOrEmpty(specPath), FilesDictionary_class.GetBytes(specPath), Nothing)
        Dim cpu As FaceTintCpuCompositor.CpuPipelineResult
        Try
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(dB, nB, sB, layerInputs, regionSwaps, Nothing, diffusePath, normalPath, specPath)
        Catch ex As Exception
            Dim m = ex.Message
            Logger.LogLazy(Function() $"[FACETINT-CPU-RENDER] compose failed: {m}")
            Return False
        End Try
        If cpu Is Nothing Then Return False
        Dim any = False
        ' Diffuse: g22 → linear antes de subir (paridad con el output linear del path GL).
        If cpu.Diffuse IsNot Nothing AndAlso cpu.Diffuse.Bgra IsNot Nothing AndAlso diffuseEntry IsNot Nothing Then
            FaceTintCpuCompositor.G22DiffuseBgraToLinearInPlace(cpu.Diffuse.Bgra)
            SwapCpuChannelIntoDict(diffuseEntry, cpu.Diffuse.Bgra, cpu.Diffuse.Width, cpu.Diffuse.Height) : any = True
        End If
        ' N/S: ya lineales, subir tal cual.
        If cpu.Normal IsNot Nothing AndAlso cpu.Normal.Bgra IsNot Nothing AndAlso normalEntry IsNot Nothing Then
            SwapCpuChannelIntoDict(normalEntry, cpu.Normal.Bgra, cpu.Normal.Width, cpu.Normal.Height) : any = True
        End If
        If cpu.Specular IsNot Nothing AndAlso cpu.Specular.Bgra IsNot Nothing AndAlso specEntry IsNot Nothing Then
            SwapCpuChannelIntoDict(specEntry, cpu.Specular.Bgra, cpu.Specular.Width, cpu.Specular.Height) : any = True
        End If
        Return any
    End Function

    ''' <summary>Sube un BGRA compuesto por CPU a una textura GL nueva y la swapea en el dict entry (borra la vieja).
    ''' Mismo contrato que ApplyPipelineResultToDict pero desde bytes CPU (linear, IsSRGB=False).</summary>
    Private Sub SwapCpuChannelIntoDict(entry As PreviewModel.Texture_Loaded_Class, bgra As Byte(), w As Integer, h As Integer)
        If entry Is Nothing OrElse bgra Is Nothing OrElse w <= 0 OrElse h <= 0 Then Return
        Dim newId = UploadRgba8Linear(bgra, w, h)
        If newId = 0 Then Return
        Dim oldId = entry.Texture_ID
        entry.Texture_ID = newId
        entry.IsSRGB = False
        entry.Size = New System.Drawing.Size(w, h)
        If oldId <> 0 AndAlso oldId <> newId Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
    End Sub

    Private Sub ApplyPipelineResultToDict(model As PreviewModel,
                                          texPath As String,
                                          entry As PreviewModel.Texture_Loaded_Class,
                                          chResult As FaceTintCompositor.FaceTintPipelineChannelResult)
        If chResult Is Nothing OrElse Not chResult.IsFresh Then Return
        If entry Is Nothing OrElse model Is Nothing OrElse String.IsNullOrEmpty(texPath) Then Return
        Dim oldId = entry.Texture_ID
        If chResult.TextureId = 0 OrElse chResult.TextureId = oldId Then Return
        entry.Texture_ID = chResult.TextureId
        ' The composite output is a plain RGBA8 FBO texture sampled RAW (the live path's final
        ' G22→Linear convert leaves linear values, no sRGB SRV decode). Keep entry.IsSRGB honest so
        ' anything that reads it post-composite (without a preceding pristine rollback) sees the truth.
        entry.IsSRGB = False
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
    End Sub

    ''' <summary>Drop every cached face-tint byte buffer and decoded GL texture. Call this
    ''' when the FilesDictionary is rebuilt (BA2 mount/unmount, plugin reload) so a stale
    ''' BA2 read cannot leak into a new asset set.</summary>
    Friend Sub ClearFaceTintCaches()
        _tintBytesCache.Clear()
        _hostProvider().TintGpuCache.Clear()
        _hostProvider().PristineDiffusePixels.Clear()
    End Sub

    ''' <summary>Decode-once snapshot: read the DDS bytes for <paramref name="diffusePath"/>,
    ''' run them through the native loader to get the level-0 RGBA8 pixel buffer, and stash
    ''' (pixels, width, height) in <see cref="_hostProvider().PristineDiffusePixels"/>. No-op when a path is
    ''' already cached — the on-disk DDS doesn't change for the lifetime of an NPC.
    '''
    ''' Called from the per-path compositor entry points before the original Texture_ID gets
    ''' destroyed. The decode happens exactly once per path per NPC; every subsequent live
    ''' tint refresh just re-uploads the cached pixels without touching the DDS again.</summary>
    Private Sub CapturePristineDiffusePixels(diffusePath As String, isSRGB As Boolean, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _hostProvider()
        If String.IsNullOrEmpty(diffusePath) Then Return
        If host.PristineDiffusePixels.ContainsKey(diffusePath) Then Return

        Dim loc As FilesDictionary_class.File_Location = Nothing
        If Not FilesDictionary_class.Dictionary.TryGetValue(diffusePath, loc) Then
            ' Negative cache so we don't keep retrying paths that don't resolve.
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        Dim ddsBytes As Byte() = Nothing
        Try
            ddsBytes = loc.GetBytes()
        Catch
        End Try
        If ddsBytes Is Nothing OrElse ddsBytes.Length = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        ' Decode through the native wrapper. ConvertForBitmap gives us the RGBA8 level-0
        ' pixels straight back (matching what CreateBitmapFromDDS uses internally) — that's
        ' exactly what we need for a fast TexImage2D upload. We reuse this rather than
        ' Loader.LoadTextures because we don't want to maintain GL format swizzles / mipmap
        ' chains; the live tint refresh only needs the level-0 RGBA8 pixels.
        Dim tex As DirectXTexWrapperCLI.TextureLoaded

        Try
            tex = DirectXTexWrapperCLI.Loader.ConvertForBitmap(ddsBytes)
        Catch ex As Exception
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End Try
        If tex Is Nothing OrElse Not tex.Loaded OrElse tex.Levels Is Nothing OrElse tex.Levels.Count = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If

        Dim lvl = tex.Levels(0)
        If lvl Is Nothing OrElse lvl.Data Is Nothing OrElse lvl.Data.Length = 0 Then
            host.PristineDiffusePixels(diffusePath) = Nothing
            Return
        End If
        ' Copy the bytes off the native object (the wrapper recycles its own buffer); we want
        ' a managed array that lives independently of the wrapper's lifetime.
        Dim pixels(lvl.Data.Length - 1) As Byte
        Buffer.BlockCopy(lvl.Data, 0, pixels, 0, lvl.Data.Length)

        host.PristineDiffusePixels(diffusePath) = New NpcRenderHost.PristinePixels With {
            .Pixels = pixels,
            .Width = lvl.Width,
            .Height = lvl.Height,
            .DGXFormat_Original = tex.DxgiCodeOriginal,
            .DGXFormat_Final = tex.DxgiCodeFinal,
            .IsSRGB = isSRGB
        }

        ' Free the wrapper's per-level buffers ASAP — we have our own copy now.
        Try
            For Each l In tex.Levels
                l.Data = Nothing
            Next
            tex.Levels.Clear()
        Catch
        End Try
    End Sub

    ''' <summary>Live tint refresh path. Restores every captured diffuse to its untinted
    ''' baseline, re-runs the face tint compositor and the face/body skin SoftLight passes,
    ''' and refreshes the SkinTintColor / HairTintColor uniforms in place. No geometry reload.
    '''
    ''' Returns False if any pristine path failed to resolve (caller should fall back to a
    ''' full reload for correctness on this edit).</summary>
    Public Function RefreshFaceTintLivePreview(Optional host As NpcRenderHost = Nothing) As Boolean
        Logger.LogLazy(Function() $"[LIVE-EDIT] RefreshFaceTintLivePreview ENTRY")
        If host Is Nothing Then host = _hostProvider()
        If host.LastRenderedState Is Nothing OrElse host.LastRenderData Is Nothing Then
            Logger.LogLazy(Function() $"[LIVE-EDIT] skip: LastRenderedState/Data Nothing")
            Return False
        End If
        Dim model = host.PreviewCtl?.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Logger.LogLazy(Function() $"[LIVE-EDIT] skip: model/meshes Nothing")
            Return False
        End If

        ' Stage 0: re-pull QNAM (and any other state field that overlay-mutates per edit)
        ' from the overlay preset. host.LastRenderedState was seeded once at NPC load (line
        ' 4106-4107 path) so it's stale after the user changes the combo. Without this sync,
        ' the rest of the function reads the OLD HairColorFormID — render shows previous hair
        ' color regardless of what the user picked.
        Dim overlayPreset As LooksmenuLoader.LooksmenuPreset = Nothing
        If _appliedPresets.TryGetValue(host.LastRenderedState.RootNpcFormID, overlayPreset) Then
            If overlayPreset.HairColorFormID <> 0UI Then
                host.LastRenderedState.HairColorFormID = overlayPreset.HairColorFormID
            End If
        End If

        ' Stage 1: roll every face/body diffuse cache entry back to its pristine bytes. Each
        ' entry's Texture_ID currently points to a tinted/softlighted bake; we re-decode the
        ' original bytes onto a fresh GL texture, swap it into the entry, and delete the stale
        ' baked one. After this, the next compositor + softlight passes will start from a
        ' clean baseline.
        If Not RestoreCapturedDiffusesToPristine(model, host) Then
            ' Some path lacked pristine bytes (FilesDictionary miss) — the live preview can't
            ' guarantee correctness without a full reload.
            Return False
        End If

        ' Stage 2a: TryApplyBodySkinSoftLight reads state.TextureLightingColor and SoftLights
        ' the body diffuse with that colour. ResolveNPCBaseState normally seeds it from the
        ' overlay's slot-12 SkinTone (line 4045-4048), but that runs only on a full reload —
        ' a live tint edit doesn't touch state. We have to push the freshly-resolved skin
        ' tone into state ourselves before calling the SoftLight pass, otherwise body would
        ' be tinted with the previous QNAM/SkinTone snapshot and face/body would diverge as
        ' the user moves the slot-12 colour combo.
        Dim freshSkinTone = _materialResolver.ResolveNpcSkinToneColor(host.LastRenderedState)
        Dim hasValueLog = freshSkinTone.HasValue
        If freshSkinTone.HasValue Then
            Dim fsR = freshSkinTone.Value.R
            Dim fsG = freshSkinTone.Value.G
            Dim fsB = freshSkinTone.Value.B
            Dim fsA = freshSkinTone.Value.A
            Logger.LogLazy(Function() $"[LIVE-EDIT] Stage 2a fresh skinTone=RGBA({fsR},{fsG},{fsB},{fsA}) — pushing to state.TextureLightingColor")
            host.LastRenderedState.HasTextureLighting = True
            host.LastRenderedState.TextureLightingColor = freshSkinTone.Value
        Else
            Logger.LogLazy(Function() $"[LIVE-EDIT] Stage 2a freshSkinTone=Nothing — state.TextureLightingColor NOT updated")
        End If

        ' Stage 2b: re-run compositor + SoftLight passes (same chain ApplyFaceTintOverlay uses
        ' on first render). The compositor will read npcData.FaceTintLayers from the
        ' overlay-applied NPC_Data, so the freshly-edited preset is what gets baked.
        ApplyFaceTintOverlay(host.LastRenderedState, host.LastRenderData, host)

        ' Stage 3: refresh material uniforms (SkinTintColor + GrayscaleToPalette / HairTintColor)
        ' on every loaded shape. These were set at NIF-load time inside ApplyShapeMaterialOverrides
        ' and are invisible to MarkDirty(Textures); we mutate them in place.
        '
        ' Iterate renderData.Shapes (not model.meshes) so each shape can be looked up against
        ' renderData.ShapeCandidate — without candidate context the prior code path applied hair
        ' color to ANY palette-enabled material, leaking it into robot armor / face / body shapes.
        ' Shared helper ApplyMaterialPaletteHairColor enforces the engine rule (Hair/FacialHair/
        ' Brow HDPTs only) and is the same code path NIF-load uses now — no parallel copy to
        ' drift.
        '
        ' SkinTintColor refresh stays inline here using the simple SkinTone resolution (no
        ' candidate-aware override). NIF-load uses the richer ResolveSkinTintColor which factors
        ' in solidTintColor for face HeadParts — that asymmetry is a separate frontier (see
        ' project_palette_routing_pending.md). Not touched here to avoid changing render behavior
        ' for face shapes edited in live preview.
        Dim renderData = host.LastRenderData
        Dim skinTone = _materialResolver.ResolveNpcSkinToneColor(host.LastRenderedState)
        For Each shape In renderData.Shapes
            If shape Is Nothing Then Continue For
            Dim relatedMaterial = shape.ShapeMaterial
            If relatedMaterial Is Nothing OrElse relatedMaterial.material Is Nothing Then Continue For
            Dim mat = relatedMaterial.material

            ' Don't overwrite a RaceMenu skin-override tint (key 7) with the actor skin tone — the override wins.
            If mat.SkinTint AndAlso skinTone.HasValue AndAlso Not mat.SkinTintFromOverride Then
                mat.SkinTintColor = skinTone.Value
            End If

            Dim shapeCandidate As MainForm.MeshCandidate = Nothing
            renderData.ShapeCandidate.TryGetValue(shape, shapeCandidate)
            _materialResolver.ApplyMaterialPaletteHairColor(mat, shapeCandidate, host.LastRenderedState, Nothing)
        Next

        Return True
    End Function

    ''' <summary>Roll every pristine-cached diffuse back to its untinted baseline by uploading
    ''' the cached RGBA8 pixels to a fresh GL texture and installing it in the cache entry. The
    ''' DDS decode happened exactly once when we captured pristine; from then on every refresh
    ''' is just a 4MB texture upload (~1ms per face/body diffuse).
    '''
    ''' Returns False when a captured path's pristine pixels are missing — caller falls back
    ''' to full reload.</summary>
    Friend Function RestoreCapturedDiffusesToPristine(model As PreviewModel, Optional host As NpcRenderHost = Nothing) As Boolean
        If host Is Nothing Then host = _hostProvider()
        Dim pristineCount = host.PristineDiffusePixels.Count
        Logger.LogLazy(Function() $"[ROLLBACK] entry pristineCount={pristineCount}")
        If host.PristineDiffusePixels.Count = 0 Then
            ' Nothing was ever composited — nothing to restore. The upcoming ApplyFaceTintOverlay
            ' will run for the first time and CapturePristineDiffusePixels will populate the
            ' cache as the compositor walks each path.
            Logger.LogLazy(Function() $"[ROLLBACK] skip: nothing captured yet")
            Return True
        End If

        For Each kv In host.PristineDiffusePixels
            Dim path = kv.Key
            Dim pristine = kv.Value
            If pristine Is Nothing OrElse pristine.Pixels Is Nothing OrElse pristine.Pixels.Length = 0 Then
                ' Negative cache hit — we tried to capture this path before and failed. Bail
                ' out so the caller can full-reload instead of silently leaving stale tints.
                Dim pathLog = path
                Logger.LogLazy(Function() $"[ROLLBACK] FAIL path='{pathLog}' reason=negative-cache (pristine bytes missing)")
                Return False
            End If
            Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(path, entry) Then
                Dim pathLog2 = path
                Logger.LogLazy(Function() $"[ROLLBACK] skip path='{pathLog2}' reason=not-in-dict")
                Continue For
            End If
            If entry Is Nothing Then
                Dim pathLog3 = path
                Logger.LogLazy(Function() $"[ROLLBACK] skip path='{pathLog3}' reason=entry-Nothing")
                Continue For
            End If

            ' Allocate a fresh GL texture and upload the cached RGBA8 pixels straight into it.
            ' This is the single hot path on every slider tick — if you change anything here
            ' measure the slider responsiveness afterwards.
            Dim newId As Integer = 0
            Dim uploadOk As Boolean = False
            Try
                ' Drain any pre-existing GL error so the post-upload check below only
                ' reports failures attributable to THIS upload.
                Dim drainGuard As Integer = 0
                Do While OpenTK.Graphics.OpenGL4.GL.GetError() <> OpenTK.Graphics.OpenGL4.ErrorCode.NoError
                    drainGuard += 1
                    If drainGuard > 32 Then Exit Do
                Loop

                newId = OpenTK.Graphics.OpenGL4.GL.GenTexture()
                If newId = 0 Then
                Else
                    OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, newId)
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapS, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
                    OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureWrapT, CInt(OpenTK.Graphics.OpenGL4.TextureWrapMode.ClampToEdge))
                    Dim handle = System.Runtime.InteropServices.GCHandle.Alloc(pristine.Pixels, System.Runtime.InteropServices.GCHandleType.Pinned)
                    Try
                        ' DirectXTexWrapperCLI.Loader.ConvertForBitmap (the source of pristine.Pixels)
                        ' produces GDI Format32bppArgb byte order, which is B,G,R,A in memory. Tell
                        ' OpenGL that with PixelFormat.Bgra; the driver swaps to RGBA on upload so the
                        ' internal representation is correct. Using PixelFormat.Rgba here gave a blue
                        ' body (the body diffuse came back with R and B swapped on every live refresh).
                        ' Re-upload in the SAME colour-space the original load used. pristine.Pixels are
                        ' the sRGB-ENCODED (display) bytes ConvertForBitmap decoded from the DDS. If the
                        ' original was an sRGB SRV (IsSRGB), restore it as Srgb8Alpha8 so the sample decodes
                        ' to LINEAR on read (matching the live load); otherwise raw Rgba8. Restoring as plain
                        ' Rgba8 while entry.IsSRGB stayed True desynced baseDiffuseIsLinearOnGpu → tone/gamma
                        ' shift on the next composite (the "edit" regression).
                        Dim internalFmt = If(pristine.IsSRGB,
                            OpenTK.Graphics.OpenGL4.PixelInternalFormat.Srgb8Alpha8,
                            OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8)
                        OpenTK.Graphics.OpenGL4.GL.TexImage2D(
                            OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0,
                            internalFmt,
                            pristine.Width, pristine.Height, 0,
                            OpenTK.Graphics.OpenGL4.PixelFormat.Bgra,
                            OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte,
                            handle.AddrOfPinnedObject())
                    Finally
                        handle.Free()
                    End Try
                    OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)

                    ' Certify GL accepted the upload. A silent error here means the texture
                    ' is allocated but contents are undefined (driver typically zeros it =
                    ' solid black). Refuse to install it in the cache; caller will FullReload.
                    Dim postErr = OpenTK.Graphics.OpenGL4.GL.GetError()
                    If postErr <> OpenTK.Graphics.OpenGL4.ErrorCode.NoError Then
                    Else
                        uploadOk = True
                    End If
                End If
            Catch ex As Exception
            End Try

            If Not uploadOk Then
                If newId <> 0 Then
                    Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(newId) : Catch : End Try
                End If
                Dim pathLog4 = path
                Logger.LogLazy(Function() $"[ROLLBACK] FAIL path='{pathLog4}' reason=upload-failed")
                Return False
            End If

            Dim oldId = entry.Texture_ID
            entry.Texture_ID = newId
            entry.Size = New Size(pristine.Width, pristine.Height)
            entry.DGXFormat_Original = pristine.DGXFormat_Original
            entry.DGXFormat_Final = pristine.DGXFormat_Final
            ' Restore the sRGB-ness to match the re-uploaded texture so the next composite's
            ' baseDiffuseIsLinearOnGpu (= entry.IsSRGB) is correct.
            entry.IsSRGB = pristine.IsSRGB
            entry.Loaded = True
            If oldId <> 0 AndAlso oldId <> newId Then
                Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(oldId) : Catch : End Try
            End If
            Dim pathLog5 = path
            Dim oldIdLog = oldId
            Dim newIdLog = newId
            Logger.LogLazy(Function() $"[ROLLBACK] restored path='{pathLog5}' oldTex={oldIdLog} → pristineTex={newIdLog}")
        Next
        Logger.LogLazy(Function() $"[ROLLBACK] done OK")
        Return True
    End Function

End Class
