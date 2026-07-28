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
        LastSseFoldWasMandatory = False   ' lo recalcula TryApplyFaceTints por shape (SSE)

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

    ''' <summary>⚠️ PROVISORIO (vive con el checkbox de debug del render plegado). True si el ÚLTIMO NPC renderizado
    ''' plegó OBLIGATORIAMENTE en SSE (tenía skee MASKT y/u overlays de cara) — es decir, si el pliegue NO fue una
    ''' elección del toggle. La UI lo usa para deshabilitar el checkbox en esos NPCs: ahí no existe un "sin plegar"
    ''' fiel que mostrar, porque el bake también pliega.</summary>
    Friend Property LastSseFoldWasMandatory As Boolean

    ''' <summary>BOTH ENGINES: copy the authoritative face material's subsurface-scattering response
    ''' onto every body skin material whose response DIFFERS, so face and body skin light identically.
    ''' The face material (BSLightingShaderType.FaceTint) "wins" (is prioritized): its SubsurfaceLighting
    ''' (on/off) and SubsurfaceLightingRolloff are copied verbatim (including False) onto each body skin
    ''' material (the SkinTint flag, excluding the face itself) ONLY when that body's current values do
    ''' not already match the face's (no-op when they already agree). The render shader reads both
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
        ' Gate persistente (CharGen Options → Fixes, OFF por defecto): cuando está OFF, cada material de
        ' piel usa su PROPIO subsurface autorado (flag + rolloff) = engine-faithful. Cuando está ON, se
        ' copia SOLO el FLAG on/off de la cara al cuerpo (el rolloff queda SIEMPRE autorado, nunca se copia).
        If Not Config_App.Current.Setting_MatchHeadSubsurfaceFlagToBody Then Return
        If host Is Nothing Then host = _hostProvider()
        ' BOTH ENGINES. Prioritize the FACE: copy its subsurface response onto body skin materials, but
        ' ONLY where the body differs (the per-target guard below skips shapes that already match). This
        ' pairs with the removal of the SSE facegen `bSoftlight` force in Render.vb: subsurface is now
        ' bound per-material from each shape's own Soft_Lighting flag, and this pass reconciles the body
        ' to the face when they diverge. (RE 2026-07-23: SSE facegen subsurface is selected by the material
        ' SOFT_LIGHTING flag, not forced; vanilla/mod facegen heads ship it OFF. FO4 is deferred/value-driven.
        ' Full engine parity of the FO4 path was not byte-confirmed -- see memory notes.)
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
            ' Guard SOLO por el flag (el rolloff queda autorado, no se compara ni se copia).
            If preOn = faceOn Then Continue For

            ' Fires ONLY when body ≠ face (guard above skipped the equal case). Log both sides before mutating.
            Dim snLog = mesh.MeshData.Shape?.ShapeName
            Dim faceOnL = faceOn
            Dim faceRollL = faceRolloff
            Dim bodyOnL = preOn
            Dim bodyRollL = preRoll
            Logger.LogLazy(Function() $"[BODY-SUBSURFACE] MATCH FIRED (differ) shape='{snLog}' FACE(on={faceOnL} roll={faceRollL:F4}) BODY(on={bodyOnL} roll={bodyRollL:F4}) → body set to face")

            ' SOLO el flag on/off; el rolloff del cuerpo queda como viene autorado.
            mb.SubsurfaceLighting = faceOn
            applied += 1
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
    ''' <remarks>Era una Function cuyo Boolean NADIE leia: su unico call-site (linea ~59) descarta el
    ''' valor. Con eso `composedAny` / `faceMeshFoundButTextureNotReady` y el `retry later` que
    ''' documentaban eran codigo muerto -- ningun llamador reintentaba nunca. Se paso a Sub y se
    ''' borraron los dos acumuladores. Si alguna vez hace falta el reintento hay que reponer el valor
    ''' de retorno Y un llamador que lo lea; que exista el flag no alcanza.</remarks>
    Private Sub TryApplyFaceTints(state As MainForm.NPCVisualState, Optional host As NpcRenderHost = Nothing)
        If host Is Nothing Then host = _hostProvider()
        If state Is Nothing Then Return

        Dim built = BuildFaceTintLayerInputs(state)
        Dim layerInputs = built.layers
        Dim regionSwaps = built.regionSwaps
        Dim npcData = built.npcData
        Dim race = built.race
        ' Find the face mesh in the model, get its diffuse texture cache entry, and call the
        ' compositor on a copy. Then mutate the cache entry's GL Texture_ID so the existing
        ' render path picks up the modified diffuse without any library changes.
        ' El fetch del modelo SUBIO por encima de los corto-circuitos de abajo: para poder BAJAR el latch
        ' SkinToneBaked hace falta el modelo. Reordenar es inocuo -- los tres caminos devolvian True igual --
        ' salvo por un detalle: antes el caso `sin NPC` salia SIN tocar host.PreviewCtl, asi que se usa `?.`
        ' para no introducir una desreferencia nueva (NpcRenderHost:199 asume que PreviewCtl puede ser Nothing).
        Dim model = host.PreviewCtl?.Model
        If model Is Nothing OrElse model.meshes Is Nothing Then
            Return   ' no model — nothing we can do
        End If

        ' SIN NADA QUE COMPONER ⇒ hay que BAJAR el latch SkinToneBaked antes de salir.
        ' `SkinToneBaked` era un latch de UNA SOLA VIA: la asignacion del loop lo ponia en True y NADIE lo
        ' bajaba nunca (su flag hermano SseFoldDetailNeutralized si se resetea). Camino concreto que rompia:
        ' edicion viva de tints -> se restaura el diffuse PRISTINE -> el usuario borra todas las capas ->
        ' se salia por aca con el flag pegado en True -> Render.vb:3590 pone hasTint=False -> bHasTintColor
        ' =False -> el shader saltea el soft-light del tono. Resultado: esa malla se dibujaba SIN tono de
        ' piel sobre un diffuse que tampoco lo traia horneado, y solo se recuperaba reiniciando el NPC.
        ' SON DOS PUERTAS, NO UNA: `sin NPC/raza` (built.npcData Is Nothing) y `sin capas` (FO4). La primera
        ' se alcanza en edicion viva cuando el rebuild no resuelve el NPC, justo despues del restore a
        ' pristine -- mismo modo de falla. Las dos comparten el mismo bajado de flag.
        Dim nothingToCompose As Boolean =
            built.npcData Is Nothing OrElse
            (layerInputs.Count = 0 AndAlso Config_App.Current.Game <> Config_App.Game_Enum.Skyrim)
        ' (SSE compone el facetint desde el record del NPC, sin capas de plantilla FO4, asi que una lista
        '  vacia NO puede cortar en Skyrim: tiene que caer al loop donde corre la rama SSE.)
        If nothingToCompose Then
            For Each mesh In model.meshes
                If mesh Is Nothing OrElse mesh.MeshData Is Nothing OrElse mesh.MeshData.Material Is Nothing Then Continue For
                mesh.MeshData.Material.SkinToneBaked = False
            Next
            Return
        End If

        Dim seenFaceMeshes As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        ' Dedup del pliegue del NORMAL, gemelo de seenFaceMeshes (que dedupea el del diffuse por complexion): dos
        ' mallas FaceTint del mismo NPC que resuelven al MISMO _msn producen un pliegue IDÉNTICO por construcción,
        ' y las dos terminan bindeando la misma textura per-NPC. Ver ApplySseFaceOverlayNormals.
        Dim seenFaceNormals As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
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
            ' a SEPARATE texture-set slot 6 (-> material+0xA0 -> PS t3) that the facegen PS SOFT-LIGHTS onto the
            ' diffuse -- exactly like the body's skin tint (FacegenRGBTint). So compose the per-NPC facetint and
            ' install it as InnerLayerTexture; the shared render (bHasDetailMask -> texGlowmap) then applies it.
            ' Game-gated; FO4 keeps the path below.
            If Config_App.Current.Game = Config_App.Game_Enum.Skyrim Then
                ' ⭐ EL RENDER PLIEGA EXACTAMENTE CUANDO PLIEGA EL BAKE (misma condición: skee MASKT u overlays de cara).
                ' POR QUÉ: el bake compone las MASKT/overlays SOBRE EL ALBEDO YA TINTADO (softlight(diffuse,tint) ×
                ' amplify(detail)), y ese albedo sólo existe DESPUÉS de plegar. En el camino normal el albedo lo
                ' calcula el SHADER, así que no hay dónde meterlas ⇒ para mostrar lo mismo que se hornea, hay que
                ' plegar igual que el bake.
                '   sin MASKT/overlays → camino normal (facetint en slot 6; el shader corre la cadena = engine)
                '   con MASKT/overlays → fold (slot 0 = todo compuesto; slots 3/6 neutralizados; shader = identidad)
                ' Las MASKT salen del NIF del propio shape que se está renderizando (IRenderableShape ya expone
                ' NifContent + NifShape) ⇒ no puede quedar desincronizado con lo que se dibuja.
                Dim skeeRaw = SseSkeeMaskReader.ReadNifMaskLayersRaw(shape.NifContent, shape.NifShape)
                Dim faceOvl = ResolveFaceOverlaysForNpc(npcData)
                ' ⭐ CONDICIÓN IDÉNTICA A LA DEL BAKE (FaceGenBuilder.WriteSseFaceDiffuseWithOverlays):
                '   skee  → SseSkeeMaskReader.HasMaskLayers, que ES ReadNifMaskLayersRaw(...).Count > 0
                '   ovl   → SseOverlayCompositor.HasAnyFoldableFaceOverlay (diffuse O normal, con opacidad > 0)
                ' ⛔ NO usar `faceOvl.Count > 0`: un nodo Face[Ovl] sin diffuse NI normal (o con opacidad 0) no
                ' aporta nada al compose, y hacer plegar al render por él lo desviaba del bake en el otro sentido.
                Dim mustFold = (skeeRaw IsNot Nothing AndAlso skeeRaw.Count > 0) OrElse
                               SseOverlayCompositor.HasAnyFoldableFaceOverlay(faceOvl)
                If mustFold Then LastSseFoldWasMandatory = True

                ' ⭐⭐ PLIEGUE DEL NORMAL (_msn). INDEPENDIENTE del pliegue del diffuse: son dos texturas distintas,
                ' con dos gates distintos, y el bake ya las trata así (WriteSseFaceDiffuseWithOverlays gatea el
                ' bloque de NORMALES con HasFaceOverlayNormals, aparte del gate del diffuse). Acá el render pasa a
                ' hacer lo MISMO — antes NO componía el normal NUNCA, así que un face-paint con relieve se horneaba
                ' y no se veía en el preview: RENDER != BAKE.
                ' ⛔ El gate es el MISMO predicado que el bake (HasFaceOverlayNormals), no uno paralelo: sin overlay
                ' que aporte normal la clave queda "" y el bind cae al _msn vanilla, sin componer ni instalar nada.
                ' ⛔ Y la ASIGNACIÓN va sí o sí, en TODAS las salidas de abajo (incluida la del dedup y la del
                ' camino no plegado): es el reset que hace que borrar el último overlay-con-normal en vivo vuelva
                ' al _msn real, en vez de seguir bindeando el plegado viejo. Mismo motivo que el reset del diffuse.
                Dim foldedNormalKey As String = ""
                If SseOverlayCompositor.HasFaceOverlayNormals(faceOvl) Then
                    Dim nKey = SseFoldedNormalKeyFor(npcData.FormID)
                    Dim msnKey = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.NormalTexture)
                    If Not String.IsNullOrEmpty(msnKey) AndAlso seenFaceNormals.Contains(msnKey) Then
                        Dim mk = msnKey
                        Logger.LogLazy(Function() $"[SSE-FOLD] normal: compose OMITIDO, el _msn '{mk}' ya lo plegó otra mesh de este NPC — se reusa la MISMA textura per-NPC")
                        foldedNormalKey = nKey
                    ElseIf ApplySseFaceOverlayNormals(materialBase, model, faceOvl, nKey) Then
                        If Not String.IsNullOrEmpty(msnKey) Then seenFaceNormals.Add(msnKey)
                        foldedNormalKey = nKey
                    End If
                End If
                mesh.MeshData.Material.SseFoldedNormalKey = foldedNormalKey
                ' ⛔ El toggle de debug NO EXISTE en Release: no se lee el config siquiera. Así el pliegue forzado es
                ' IMPOSIBLE de encender fuera de Debug, en vez de depender de que el default quede en False.
                Dim forceFoldDebug As Boolean = False
#If DEBUG Then
                forceFoldDebug = NPC_Config.Current.SseRenderFoldedPath
#End If
                ' ⚠️ PROVISORIO: el toggle sólo FUERZA el fold en NPCs que no lo necesitan (vanilla), para poder
                ' comparar tono con/sin pliegue. Cuando mustFold ya es True, el fold va sí o sí (la UI lo deshabilita).
                ' ⛔ RAZA EFECTIVA (state.RaceFormID), NO npcData.RaceFormID: npcData sale del parse crudo +
                ' preset LM y NO lleva el override de raza del editor (NpcRecordOverride) — tras un cambio de
                ' raza el compose de la CARA usaba el catálogo de tints de la raza VIEJA mientras el body usaba
                ' la nueva ⇒ cara y cuerpo con tonos de razas distintas (medido: Argonian→Dremora, [SSE-QNAM]
                ' raceFid viejo idx=38 en la cara vs nuevo idx=1 en el body).
                ' ⭐⭐ DEDUP DEL FOLD (R2). Si dos meshes FaceTint del MISMO NPC resuelven al MISMO complexion, el
                ' fold de las dos es IDÉNTICO por construcción (mismo npcData, mismo complexion, misma ley) y ambas
                ' terminan usando la MISMA textura per-NPC. Plegar dos veces era trabajo tirado y, antes de que la
                ' clave fuera per-NPC, además hacía que el resultado dependiera del orden de iteración de
                ' `model.meshes`. Es el mismo `seenFaceMeshes` que la rama FO4 ya tenía (:399) y que a ésta nunca se
                ' le aplicó.
                ' ⛔ El dedup es SÓLO del compose. La segunda mesh SÍ recibe la clave del diffuse plegado y SÍ pasa
                ' por `ApplySseFacetint` (que instala el facetint bajo su clave per-NPC y escribe
                ' `materialBase.InnerLayerTexture` de ESE material): saltear cualquiera de las dos cosas dejaría al
                ' segundo shape sin diffuse plegado o sin slot 6.
                Dim complexionKey = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
                Dim alreadyFolded = Not String.IsNullOrEmpty(complexionKey) AndAlso seenFaceMeshes.Contains(complexionKey)
                If alreadyFolded Then
                    Dim shN = shape.ShapeName, ck2 = complexionKey
                    Logger.LogLazy(Function() $"[SSE-FOLD] shape='{shN}': compose OMITIDO, el complexion '{ck2}' ya lo plegó otra mesh de este NPC — se reusa la MISMA textura per-NPC")
                    mesh.MeshData.Material.SseFoldedDiffuseKey = SseFoldedDiffuseKeyFor(npcData.FormID)
                    ApplySseFacetint(materialBase, npcData, race, model, host, state.RaceFormID)
                    Continue For
                End If
                If mustFold OrElse forceFoldDebug Then
                    Dim foldedKeyOut As String = ""
                    If ApplySseFacetintFolded(materialBase, npcData, race, model, host, skeeRaw, faceOvl, foldedKeyOut, state.RaceFormID) Then
                        If Not String.IsNullOrEmpty(complexionKey) Then seenFaceMeshes.Add(complexionKey)
                        ' ⭐⭐ ACÁ se conecta el diffuse plegado con el bind: MaterialData.SseFoldedDiffuseKey es lo
                        ' único que hace que DiffuseTexture_ID devuelva la textura per-NPC en vez de la del
                        ' complexion compartido. Es per-mesh, así que dos NPCs con el mismo complexion no se pisan.
                        mesh.MeshData.Material.SseFoldedDiffuseKey = foldedKeyOut
                        ' ⛔ YA NO SE NEUTRALIZA NADA. El diffuse plegado viene PRE-COMPENSADO (la inversa de la
                        ' cadena del engine), así que los slots 3 y 6 quedan con su contenido REAL y el shader los
                        ' aplica normalmente: la cadena se cancela y sale este buffer. Idéntico al bake.
                        ' (Acá se seteaba `SseFoldDetailNeutralized = False`. La propiedad se ELIMINÓ: sus dos únicas
                        '  asignaciones la ponían en False, así que la rama del render que la consultaba era código
                        '  muerto desde que el fold dejó de neutralizar el slot 3. Ver Render.vb, bHasDetailMask.)
                        ' ⭐ IMPRESCINDIBLE: el slot 6 tiene que llevar el FACETINT REAL. El diffuse va
                        ' pre-compensado (con el softlight ya invertido), así que el shader NECESITA re-aplicar
                        ' softlight(slot0, facetint) para volver al buffer del fold. Sin esto el slot 6 queda sin
                        ' textura, el shader cae al gris default y el NPC PIERDE el skin tint — que es exactamente
                        ' lo que rompió al quitar la neutralización: antes el camino plegado instalaba él mismo un
                        ' gris acá, así que nunca hacía falta el facetint real.
                        ApplySseFacetint(materialBase, npcData, race, model, host, state.RaceFormID)
                        Continue For
                    End If
                End If
                ' Camino NO plegado: el shader aplica el detail (real o default 0.251) UNA vez = engine.
                ' ⭐ RESET OBLIGATORIO de la clave del diffuse plegado: si esta mesh venía de un fold en un render
                ' anterior (fold↔unfold en vivo, p.ej. el usuario borró el último overlay sin recargar el NPC), sin
                ' esto seguiría bindeando el diffuse plegado VIEJO en vez de volver al complexion.
                ' (Acá también se reseteaba `SseFoldDetailNeutralized`; propiedad eliminada — ver arriba.)
                mesh.MeshData.Material.SseFoldedDiffuseKey = ""
                ApplySseFacetint(materialBase, npcData, race, model, host, state.RaceFormID)
                Continue For
            End If

            Dim diffusePath = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
            If String.IsNullOrEmpty(diffusePath) Then Continue For
            If seenFaceMeshes.Contains(diffusePath) Then Continue For
            seenFaceMeshes.Add(diffusePath)

            ' Diffuse must be ready before we attempt anything — it's the channel every layer
            ' contributes to and it's the one whose dimensions drive the FBO size. If diffuse
            ' isn't loaded, skip this mesh.
            Dim diffuseEntry As PreviewModel.Texture_Loaded_Class = Nothing
            If Not model.Textures_Dictionary.TryGetValue(diffusePath, diffuseEntry) _
               OrElse diffuseEntry Is Nothing OrElse Not diffuseEntry.Loaded OrElse diffuseEntry.Texture_ID = 0 Then
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
            ' Marca por-malla que alimenta SkinToneBaked (= `esta malla tiene el tono horneado en su
            ' diffuse`, ver Render.vb). Los dos caminos la ponen distinto y a proposito:
            '  - GPU: True al llegar aca, y punto. Lo derivaba de pipelineResult.Diffuse.IsFresh, pero eso
            '    NO medía lo que parecía: IsFresh significa `el compositor devolvio una textura NUEVA en
            '    este canal`, y en el camino vivo el diffuse SIEMPRE sale nuevo -- antes de tocar ninguna
            '    capa el pipeline le convierte el espacio de color (lineal -> G22, porque el acumulador
            '    trabaja en G22) y esa conversion ya crea la textura. El predicado daba True siempre:
            '    era ceremonia y se saco.
            '  - CPU: el Boolean de ApplyCpuComposeToDict SI puede dar False, asi que ahi se conserva.
            Dim meshDiffuseBaked As Boolean = False
            If Config_App.Current.Setting_GPUSkinning Then
                ' ⭐ FaceGenBuilder.OutputSettings = la MISMA resolución/compresión por canal que usa el bake
                ' (BakeFaceTextures se la pasa a este mismo ApplyFaceTintPipeline). ⛔ Acá NO se pasaba, así que
                ' caía al default (Inherit = nativo) y el preview ignoraba CharGen Options: con un tamaño
                ' explícito el bake escribía D/N/S a ESE tamaño y el preview mostraba el compose a resolución
                ' nativa. Con Inherit es byte-inerte (era el valor efectivo anterior).
                Dim pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                    host.CompositorState, host.TintGpuCache,
                    diffuseEntry.Texture_ID, normalSrcId, specSrcId,
                    w, h, layerInputs, regionSwaps,
                    FaceGenBuilder.OutputSettings,
                    baseDiffuseIsLinearOnGpu:=diffuseEntry.IsSRGB,
                    headDiffuseAlphaTest:=(host.CurrentBaseState IsNot Nothing AndAlso host.CurrentBaseState.HeadDiffuseAlphaTest))

                ' Swap fresh IDs into the dict and delete the IDs they replaced. IsFresh=False
                ' means the channel had no contribution and the input ID stayed in place — no
                ' dict mutation, no delete.
                ApplyPipelineResultToDict(model, diffusePath, diffuseEntry, pipelineResult.Diffuse)
                If normalEntry IsNot Nothing Then ApplyPipelineResultToDict(model, normalPath, normalEntry, pipelineResult.Normal)
                If specEntry IsNot Nothing Then ApplyPipelineResultToDict(model, specPath, specEntry, pipelineResult.Specular)
                meshDiffuseBaked = True
            Else
                ' CPU-skinning: compose por CPU (mismos layers) desde los bytes source, y subir el resultado a GL.
                ' El diffuse sale g22 (formato bake); el render espera LINEAR (el path GL hace G22→Linear final),
                ' así que lo convertimos antes de subir. N/S ya son lineales. ⚠️ paridad GL==CPU en el render:
                ' verificar IN-APP (no testeable headless); si hay gamma, es este convert.
                If ApplyCpuComposeToDict(model, diffusePath, diffuseEntry, normalPath, normalEntry, specPath, specEntry,
                                         layerInputs, regionSwaps,
                                         (host.CurrentBaseState IsNot Nothing AndAlso host.CurrentBaseState.HeadDiffuseAlphaTest)) Then
                    meshDiffuseBaked = True
                End If
            End If

            ' "Ya está": the slot-12 skin tone is now BAKED into this face mesh's diffuse (the
            ' compositor processes it as the synthetic slot-12 layer). materialBase.SkinTint stays
            ' ENABLED (structural NIF/BGSM flag, never mutated) — instead we flag THIS mesh so Render
            ' makes the shader's own SkinTint soft-light a no-op for it. Without this the face gets the
            ' tone twice (baked composite + runtime soft-light of materialBase.SkinTintColor). The FO4
            ' body is untouched (SkinToneBaked stays False → engine-faithful runtime soft-light).
            ' AHORA ES UNA ASIGNACION, NO UN LATCH: vale exactamente `el DIFFUSE de esta malla salio
            ' compuesto en ESTA pasada`. Antes era `= True` incondicional y sin ningun camino que lo bajara.
            ' OJO: las salidas por `Continue For` de mas arriba (diffusePath vacio, diffuse ya visto por
            ' otra malla, textura no lista, w/h invalidos) NO llegan aca y por lo tanto NO reasignan el
            ' flag. En esas mallas conserva el valor de la pasada anterior. Los caminos de edicion viva
            ' restauran el diffuse a pristine antes de re-entrar, asi que el riesgo real es un True viejo
            ' sobre un diffuse ya restaurado; las dos puertas tempranas de mas arriba cubren los casos en
            ' que no hay NADA que componer, que es donde eso pasaba de verdad.
            mesh.MeshData.Material.SkinToneBaked = meshDiffuseBaked
        Next

    End Sub

    ''' <summary>Los overlays de CARA (nodos <c>Face [Ovl{n}]</c>) del preset aplicado al NPC — los MISMOS que el bake
    ''' pliega (<c>WriteSseFaceDiffuseWithOverlays</c>). Vacío si el NPC no tiene preset u overlays de cara. Los del
    ''' CUERPO (Body/Hands/Feet) NO van acá: el bake no los hornea y el engine los aplica en runtime, así que el render
    ''' los sigue dibujando como decal (shape.OverlayLayers) — sólo la CARA pasa por el fold.</summary>
    ''' <summary>Los overlays de CARA del NPC para el fold del RENDER. El test de nodo es el ÚNICO canónico
    ''' (<see cref="SseOverlayCompositor.IsFaceOverlay"/>), el mismo que usan el bake CPU, el bake GPU y el emisor
    ''' del script Papyrus — si divergen, un overlay se compone dos veces o ninguna.
    ''' <para>Acá SÍ se exige <c>DiffusePath</c>, a diferencia del bake: el fold del render compone únicamente el
    ''' DIFFUSE (ComposeCpu/ComposeGpu), no toca el normal de la cabeza. Un overlay solo-normal no tendría nada
    ''' que aportar y sólo dispararía un pliegue vacío. El bake, que sí pliega el normal, usa
    ''' <see cref="SseOverlayCompositor.HasAnyFoldableFaceOverlay"/>.</para></summary>
    ''' <summary>⭐ Clave PER-NPC del diffuse plegado en el diccionario de texturas del modelo. Espeja la ruta que el
    ''' bake escribe (<c>FaceGenData\FaceDiffuse\&lt;plugin&gt;\&lt;formID&gt;.dds</c>) por legibilidad en el log, pero NO
    ''' es un path que nadie lea de disco: ningún campo del material la referencia, así que el loader nunca la pide.
    ''' Lo único que importa es que sea ÚNICA POR NPC — que es justo lo que la clave del complexion no era.</summary>
    Private Function SseFoldedDiffuseKeyFor(npcFormID As UInteger) As String
        Dim origin As String = Nothing
        If _ctx IsNot Nothing AndAlso _ctx.PluginManager IsNot Nothing Then origin = _ctx.PluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(origin) Then origin = "unknown"
        Dim fg = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return FO4UnifiedMaterial_Class.CorrectTexturePath(
            $"textures\actors\character\facegendata\facediffuse\{origin}\{fg:X8}.dds")
    End Function

    ''' <summary>⭐ Clave PER-NPC del <c>_msn</c> plegado, gemela de <see cref="SseFoldedDiffuseKeyFor"/>. Espeja la
    ''' ruta que el bake escribe (<c>FaceGenData\FaceNormal\&lt;plugin&gt;\&lt;formID&gt;.dds</c>) por legibilidad en
    ''' el log; NO es un path que nadie lea de disco (ningún campo del material la referencia ⇒ el loader nunca la
    ''' pide). Lo único que importa es que sea ÚNICA POR NPC: la del <c>_msn</c> real es COMPARTIDA entre NPCs de la
    ''' misma raza, así que instalar ahí el pliegue le pasaría el relieve del tatuaje a la cabeza de al lado.</summary>
    Private Function SseFoldedNormalKeyFor(npcFormID As UInteger) As String
        Dim origin As String = Nothing
        If _ctx IsNot Nothing AndAlso _ctx.PluginManager IsNot Nothing Then origin = _ctx.PluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(origin) Then origin = "unknown"
        Dim fg = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return FO4UnifiedMaterial_Class.CorrectTexturePath(
            $"textures\actors\character\facegendata\facenormal\{origin}\{fg:X8}.dds")
    End Function

    ''' <summary>SSE — pliegue del NORMAL de la cabeza en vivo: compone los normales de los overlays de cara sobre
    ''' el <c>_msn</c> y lo instala bajo <paramref name="foldedNormalKey"/> (clave per-NPC que consume
    ''' <c>MaterialData.SseFoldedNormalKey</c> → <c>NormalTexture_ID</c>). False ⇒ el caller deja la clave en "" y
    ''' el bind se queda con el <c>_msn</c> real.
    '''
    ''' <para>⭐⭐ ES LA MISMA FUNCIÓN QUE EL BAKE, NO UNA RÉPLICA: el compose sale de
    ''' <see cref="SseOverlayCompositor.ComposeFaceOverlayNormalsIntoMsn"/>, con los MISMOS dos decodes (el
    ''' vectorial para el normal, el de color para la cobertura) y la MISMA textura por defecto del slot 0. Por eso
    ''' acá no hay un par CPU/GPU que pueda desincronizarse: el pliegue del normal tiene UNA sola implementación,
    ''' compartida por render y bake. (El flag <c>Setting_GPUSkinning</c> gobierna la cadena del DIFFUSE, que sí
    ''' tiene dos réplicas; el normal no entra en esa cadena — es otra textura, sin blend-ops ni espacios de
    ''' color.)</para>
    '''
    ''' <para>El <c>_msn</c> es MODEL-SPACE y sus 3 canales son ejes independientes, así que acá NO hay ninguna
    ''' conversión de espacio de color (a diferencia del diffuse): entra crudo y sale crudo, <c>IsSRGB=False</c>.</para>
    '''
    ''' <para>⭐ EL ALPHA SE PRESERVA TAL CUAL, NO SE MEZCLA — y el upload NO fuerza opaco (el del diffuse sí).
    ''' ⛔ NO es "porque lleva la máscara especular": en una malla MODEL-SPACE el mask especular sale del SLOT 7
    ''' (<c>texSpecular</c> = t2 del engine), canal <c>.r</c>. El <c>normalMap.a</c> lo lee SÓLO la rama no-MSN
    ''' (Shader_Class:2130-2155, medido sobre los 6864 PS de BSLightingShader: una malla no-MSN nunca samplea t2)
    ''' y el envmask del cubemap (:2367), que la piel no usa. O sea: en la cabeza SSE ese alpha no lo lee NADIE, y
    ''' mezclarlo sería inventar un canal. Corroborado en el propio source (<c>femalehead_msn_009.dds</c>: alpha
    ''' constante 255). Y sería ACTIVAMENTE peligroso: un normal de overlay en BC5 no tiene alpha y el decode lo
    ''' devuelve constante 1 ⇒ mezclarlo lo llevaría a blanco en toda el área cubierta, el mismo modo de falla que
    ''' tenía la cobertura.</para></summary>
    Private Function ApplySseFaceOverlayNormals(materialBase As FO4UnifiedMaterial_Class, model As PreviewModel,
                                                faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                                                foldedNormalKey As String) As Boolean
        Try
            Dim mKey = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.NormalTexture)
            If String.IsNullOrEmpty(mKey) Then
                Logger.LogLazy(Function() "[SSE-FOLD] normal ABORT: el material no tiene NormalTexture")
                Return False
            End If
            Dim mBytes = FilesDictionary_class.GetBytes(mKey)
            If mBytes Is Nothing Then
                Logger.LogLazy(Function() $"[SSE-FOLD] normal ABORT: GetBytes Nothing para '{mKey}'")
                Return False
            End If
            Dim mImg = FaceTintCpuCompositor.DecodeDds(mBytes)
            If mImg Is Nothing OrElse mImg.Rgba Is Nothing OrElse mImg.Width <= 0 OrElse mImg.Height <= 0 Then
                Logger.LogLazy(Function() $"[SSE-FOLD] normal ABORT: no decodifica el _msn '{mKey}'")
                Return False
            End If
            Dim mw = mImg.Width, mh = mImg.Height, npix = mw * mh
            Dim acc(npix * 4 - 1) As Single
            Array.Copy(mImg.Rgba, acc, acc.Length)
            ' MISMA recuperación que el bake para un _msn modeado de 2 canales (ver FaceGenBuilder): sin esto el
            ' pack de DecodeDds deja B=0 ⇒ z=−1 en toda la cabeza. Inerte con el _msn vanilla (4 canales).
            If mImg.Channels < 3 Then
                Dim chL = mImg.Channels
                Logger.LogLazy(Function() $"[SSE-FOLD] el _msn '{mKey}' trae SÓLO {chL} canales (BC5/R8G8): se reconstruye el eje Z")
                FaceTintCpuCompositor.ReconstructNormalZ(acc, npix)
            End If

            If Not SseOverlayCompositor.ComposeFaceOverlayNormalsIntoMsn(
                    acc, faceOvl, mw, mh,
                    AddressOf SseFaceTintComposer.DecodeTextureRgba,
                    AddressOf SseFaceTintComposer.DecodeNormalRgba) Then
                ' El gate dijo que sí y no aportó nada (textura ausente/ilegible, o sin cobertura legítima). Ya se
                ' reportó con [SSE-OVL]; acá se cae al _msn real en vez de instalar una copia idéntica.
                Logger.LogLazy(Function() "[SSE-FOLD] normal: ningún overlay aportó — se conserva el _msn vanilla (sin instalar copia)")
                Return False
            End If

            ' Resolución de salida = la MISMA que el bake (Setting_FaceGenNormalResolution): Inherit ⇒ nativo, que
            ' es un no-op. Sin esto el preview mostraría más relieve del que el DDS horneado va a tener.
            Dim outW = mw, outH = mh
            Dim accOut = acc
            If FaceGenBuilder.OutputSettings.Normal <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
                Dim t = FaceTintConvention.ResolveResolutionSize(FaceGenBuilder.OutputSettings.Normal, Math.Min(mw, mh))
                accOut = FaceTintCpuCompositor.ResampleRgbaFloat(acc, mw, mh, t, t) : outW = t : outH = t
            End If
            ' Rgba32f, igual que el diffuse plegado (sin cuantizar a 8 bits: esa pérdida es del ARCHIVO, no del
            ' compose). forceOpaque:=False ⇒ el alpha del _msn (máscara especular) viaja intacto.
            Dim id = SseFoldLayerStack.UploadRgba32f(accOut, outW * outH, outW, outH)
            If id = 0 Then
                Logger.LogLazy(Function() "[SSE-FOLD] normal ABORT: GL.GenTexture devolvió 0 (¿sin contexto GL?)")
                Return False
            End If
            InstallTexture(model, foldedNormalKey, id, outW, outH, isSrgb:=False)
            Logger.LogLazy(Function() $"[SSE-FOLD] normal plegado instalado bajo '{foldedNormalKey}' ({outW}x{outH}, desde '{mKey}')")
            Return True
        Catch ex As Exception
            Dim tN = ex.GetType().Name, mN = ex.Message
            Logger.LogLazy(Function() $"[SSE-FOLD] normal ABORT: {tN}: {mN}")
            Return False
        End Try
    End Function

    Private Function ResolveFaceOverlaysForNpc(npcData As NPC_Data) As IList(Of RaceMenuJslot.JslotOverlayNode)
        If npcData Is Nothing OrElse _appliedPresets Is Nothing Then Return Nothing
        Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
        If Not _appliedPresets.TryGetValue(npcData.FormID, preset) OrElse preset Is Nothing Then Return Nothing
        If preset.SseBodyOverlays Is Nothing Then Return Nothing
        ' ⭐⭐ FILTRO POR NODO Y NADA MÁS — es el MISMO predicado que usa el bake (SseOverlayCompositor.FaceOverlaysOnly).
        ' ⛔ Antes acá se exigía además `DiffusePath`, y eso DROPEABA los overlays de cara SOLO-NORMAL: para ese NPC
        ' el RENDER no plegaba y el BAKE sí (el bake gatea con HasAnyFoldableFaceOverlay = diffuse O normal), o sea
        ' que el preview mostraba una cara distinta de la que se horneaba. Es exactamente el bug que
        ' HasAnyFoldableFaceOverlay vino a arreglar en el bake, que había quedado sin aplicar de este lado.
        ' Filtrar por textura en el CALLER es el error: cada composer se queda con lo que puede consumir (el de
        ' diffuse quiere DiffusePath, el de normales quiere NormalPath).
        Return SseOverlayCompositor.FaceOverlaysOnly(preset.SseBodyOverlays)
    End Function

    ''' <summary>SSE — camino PLEGADO del render: compone lo MISMO que el bake plegado, en vivo. Corre cuando el NPC
    ''' tiene skee MASKT u overlays de cara (la MISMA condición que el bake), o cuando el toggle provisorio lo fuerza.
    ''' Orden de compose = EL DEL BAKE (WriteSseFaceDiffuseWithOverlays), una sola ley:
    '''   1. base = complexion (slot 0)
    '''   2. <see cref="SseFaceGenBaker.FoldFacetintIntoDiffuse"/> ⇒ softlight(complexion, facetint) × amplify(detail)
    '''   3. skee MASKT encima (sobre el albedo YA tintado — por eso hay que plegar para poder mostrarlas)
    '''   4. Face [Ovl] overlays encima
    '''   5. <see cref="SseFaceGenBaker.PreCompensateEngineChain"/> = INVERSA de la cadena del motor. Los slots 3 y 6
    '''      quedan con su contenido REAL y el shader los aplica normalmente ⇒ la cadena se cancela.
    ''' ⇒ el shader hace <c>softlight(precompensado, facetint) × amp(detail) = folded</c> y muestra el plegado tal cual.
    ''' ⛔ (El paso 5 decía "slot 3 = (63,64,63); slot 6 = gris 0.5 ⇒ softlight(folded, 0.5) × 1". Era la ley VIEJA:
    '''  se cayó al MEDIR in-game que neutralizar el slot 6 apaga la cara aunque el albedo dé aritméticamente exacto.)
    ''' ⭐ LOSSLESS (como FO4): no pasa por ningún encode/decode BCn — esa pérdida es del ARCHIVO, no del compose.
    ''' Devuelve False si algo falta (el caller cae al camino normal).</summary>
    ''' <param name="foldedDiffuseKey">SALIDA: la clave PER-NPC bajo la que quedó instalado el diffuse plegado. El
    ''' caller la copia a <c>MaterialData.SseFoldedDiffuseKey</c> de la mesh, que es lo que hace que el bind del
    ''' diffuse la use. Queda "" si la función devuelve False.</param>
    Private Function ApplySseFacetintFolded(materialBase As FO4UnifiedMaterial_Class, npcData As NPC_Data,
                                            race As RACE_Data, model As PreviewModel, host As NpcRenderHost,
                                            skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                                            faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                                            ByRef foldedDiffuseKey As String,
                                            Optional effRaceFid As UInteger = 0UI) As Boolean
        foldedDiffuseKey = ""
        If npcData Is Nothing Then Return False
        ' Raza EFECTIVA: la del state (override de raza del editor incluido). npcData.RaceFormID es la
        ' cruda del récord — sólo fallback cuando el caller no pasa la efectiva (paths sin state).
        If effRaceFid = 0UI Then effRaceFid = npcData.RaceFormID
        If race Is Nothing AndAlso effRaceFid <> 0UI Then
            Dim rr0 = _ctx.PluginManager.GetRecord(effRaceFid)
            If rr0 IsNot Nothing AndAlso rr0.Header.Signature = "RACE" Then race = _ctx.ParseRaceCached(rr0)
        End If
        If race Is Nothing Then Return False
        Dim npcRec = _ctx.PluginManager.GetRecord(npcData.FormID)
        If npcRec Is Nothing Then Return False

        ' --- 1. Complexion (slot 0) a su resolución NATIVA = la base del pliegue (igual que el bake). ---
        Dim cKey = FO4UnifiedMaterial_Class.CorrectTexturePath(materialBase.Diffuse_or_Base_Texture)
        If String.IsNullOrEmpty(cKey) Then
            Logger.LogLazy(Function() "[SSE-FOLD] ABORT: Diffuse_or_Base_Texture vacío")
            Return False
        End If
        ' ⛔ El diffuse DEBE estar YA CARGADO (mismo guard que el camino FO4, L297-302): si el resolver corre antes de
        ' que el loader suba la textura, cualquier entry que instalemos acá lo pisa el loader después ⇒ devolvemos False
        ' y el caller reintenta (o cae al camino normal), en vez de dejar el shape sin textura (BLANCO).
        Dim cEntry As PreviewModel.Texture_Loaded_Class = Nothing
        If Not model.Textures_Dictionary.TryGetValue(cKey, cEntry) _
           OrElse cEntry Is Nothing OrElse Not cEntry.Loaded OrElse cEntry.Texture_ID = 0 Then
            Logger.LogLazy(Function() $"[SSE-FOLD] ABORT: diffuse NO cargado aún (key='{cKey}' entry={If(cEntry Is Nothing, "null", $"Loaded={cEntry.Loaded} id={cEntry.Texture_ID}")})")
            Return False
        End If
        Dim cBytes = FilesDictionary_class.GetBytes(cKey)
        If cBytes Is Nothing Then
            Logger.LogLazy(Function() $"[SSE-FOLD] ABORT: GetBytes Nothing para '{cKey}'")
            Return False
        End If
        Dim cImg = FaceTintCpuCompositor.DecodeDds(cBytes)   ' (no 'cDec': CDec es una función intrínseca de VB)
        If cImg Is Nothing OrElse cImg.Rgba Is Nothing OrElse cImg.Width <= 0 OrElse cImg.Height <= 0 Then
            Logger.LogLazy(Function() $"[SSE-FOLD] ABORT: no decodifica el complexion '{cKey}'")
            Return False
        End If
        Dim w = cImg.Width, h = cImg.Height, npix = w * h
        Dim acc(npix * 4 - 1) As Single
        Array.Copy(cImg.Rgba, acc, acc.Length)

        ' ⭐⭐ RESOLUCIÓN DE SALIDA = CharGen Options (Setting_FaceGenDiffuseResolution), IGUAL QUE EL BAKE.
        ' ⛔ El render la IGNORABA: componía y subía siempre a la resolución NATIVA del complexion mientras el
        ' bake resampleaba al target (FaceGenBuilder, `ResolveResolutionSize` + `ResampleBgra`). Con Inherit
        ' coinciden y por eso no se notaba; con un tamaño explícito el preview mostraba MÁS detalle del que el
        ' juego iba a tener — el DDS horneado es el que el motor samplea. Violación de RENDER == BAKE.
        ' El resample es lo ÚLTIMO de la cadena (después del fold, las capas y la pre-compensación) y sobre los
        ' valores sRGB, exactamente como el bake. Además ABARATA el camino cuando el target < nativo: se sube
        ' 1024² en vez de 4096² (16× menos VRAM y menos upload).
        Dim outW = w, outH = h
        If FaceGenBuilder.OutputSettings.Diffuse <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
            Dim t = FaceTintConvention.ResolveResolutionSize(FaceGenBuilder.OutputSettings.Diffuse, Math.Min(w, h))
            outW = t : outH = t
        End If

        ' --- 2. Entradas COMUNES a los dos caminos. Decodificar el complexion/detail NO es compose (es leer el
        ' archivo): es la entrada compartida que garantiza inputs bit-idénticos a las dos réplicas (decodificar
        ' BCn por hardware tiene tolerancias de spec ⇒ rompería el "dan lo mismo" en el origen). ---
        Dim useGpu = Config_App.Current.Setting_GPUSkinning AndAlso host IsNot Nothing
        Dim detPath = materialBase.DisplacementTexture
        Dim detailAcc As Single() = Nothing
        If Not String.IsNullOrEmpty(detPath) Then detailAcc = SseFaceTintComposer.DecodeTextureRgba(detPath, w, h)
        ' Skin tone (QNAM) para los sentinels de las capas skee — también común a los dos caminos.
        Dim skinRgb As Double() = Nothing
        Dim q = SseFaceTintComposer.ResolveSkinToneQnam(_ctx.PluginManager, npcData, race, effRaceFid, npcData.IsFemale)
        If q.HasValue Then skinRgb = New Double() {q.Value.R / 255.0, q.Value.G / 255.0, q.Value.B / 255.0}

        ' --- 3. LA CADENA (facetint → fold → capas → sRGB→lin): PURA GPU o PURA CPU según el flag de la cámara.
        ' ⭐ EL FLAG (Setting_GPUSkinning) ES EL ÚNICO QUE DECIDE, y cada camino es PURO de punta a punta:
        '   GPU → SseFoldLayerStack.ComposeFoldedGpuResident: la cadena ENTERA corre en GL encadenando TEXTURAS
        '         (Rgba32f), CERO readbacks en el camino caliente. El readback existe SOLO en el sandbox de
        '         paridad (SseMeasureFoldParity) y en los stats del log (Logger.Enabled) — diagnóstico, no camino.
        '   CPU → todo en arrays Double (ComposeLinearRgba + FoldFacetintIntoDiffuse + ComposeCpu) + upload RGBA8.
        ' "Dan lo mismo" es requisito de RESULTADO — misma ley, mismos inputs decodificados UNA vez — y se MIDE
        ' con el sandbox; NO es un contrato de representación (el GPU ya no baja a Double entre etapas).
        ' ⛔ SIN FALLBACK. O TODO GPU O TODO CPU. Si el flag pide GPU y CUALQUIER etapa GL falla ⇒ ABORT con log
        ' (el caller cae al camino no plegado, que es visible) — nunca se compone por CPU a escondidas.
        Dim foldedId As Integer
        If useGpu Then
            Dim tintLayers = SseFaceTintComposer.BuildLayerInputs(_ctx.PluginManager, npcRec, race, effRaceFid,
                                                                  npcData.IsFemale, npcData.SseTintRaw, npcData.SseTintTexOverride)
            Dim measureParity As Boolean = False
#If DEBUG Then
            measureParity = NPC_Config.Current.SseMeasureFoldParity   ' sandbox: en Release ni se lee (duplica el compose)
#End If
            foldedId = SseFoldLayerStack.ComposeFoldedGpuResident(acc, tintLayers, detailAcc, skeeRaw, faceOvl,
                                                                  skinRgb, w, h, host, measureParity, outW, outH)
            If foldedId = 0 Then
                Logger.LogLazy(Function() "[SSE-FOLD] ABORT: la cadena GPU del pliegue falló y el flag pide GPU. NO se compone por CPU.")
                Return False
            End If
        Else
            ' --- CPU PURO: facetint compuesto a la resolución del complexion (lineal). ---
            Dim facetint = SseFaceTintComposer.ComposeLinearRgba(_ctx.PluginManager, npcRec, race, effRaceFid,
                                                                 npcData.IsFemale, w, h, Nothing,
                                                                 npcData.SseTintRaw, npcData.SseTintTexOverride)
            If facetint Is Nothing Then Return False

            ' Medias de las ENTRADAS (diagnóstico). El facetint (slot 6) entra por SOFT-LIGHT: neutro = 0.5, y un
            ' facetint real ronda 0.5-0.8 (>0.5 aclara, <0.5 oscurece; nunca satura). El que puede saturar es el
            ' DETAIL (slot 3), que va amplificado ×255/64: neutro = 0.251 y el amp que se loguea abajo debería dar
            ' ~1.0; si el detail se acerca a 1.0 el amp se va a ~4 ⇒ CARA BLANCA. El complexion ~0.28 sRGB.
            ' ⛔ GATEADO POR Logger.Enabled: LogLazy hace lazy el STRING, NO el CÁLCULO — y esto es un loop sobre
            ' TODA la cara que se pagaba en cada render aunque el log estuviera apagado. (El camino GPU loguea lo
            ' suyo dentro de la cadena, con readbacks igual de gateados.)
            If Logger.Enabled Then
                Dim mC(2) As Double, mF(2) As Double, mD(2) As Double
                For i = 0 To npix - 1
                    For ch = 0 To 2
                        mC(ch) += acc(i * 4 + ch)
                        mF(ch) += facetint(i * 4 + ch)
                        If detailAcc IsNot Nothing Then mD(ch) += detailAcc(i * 4 + ch)
                    Next
                Next
                Dim dMeanR = If(detailAcc Is Nothing, SseFaceGenBaker.EngineDefaultDetail, mD(0) / npix)
                Logger.LogLazy(Function() $"[SSE-FOLD] IN: complexion(sRGB)=({mC(0) / npix:F3},{mC(1) / npix:F3},{mC(2) / npix:F3}) " &
                                          $"facetint/softlight-b(lin)=({mF(0) / npix:F3},{mF(1) / npix:F3},{mF(2) / npix:F3}) " &
                                          $"detail={If(detailAcc Is Nothing, "NINGUNO(0.251=default engine)", $"({mD(0) / npix:F3},{mD(1) / npix:F3},{mD(2) / npix:F3})")} " &
                                          $"⇒ amp(detail)≈{SseFaceGenBaker.FgTintChannel(dMeanR, 0):F3}")
            End If

            ' PLIEGUE (softlight(complexion, facetint) × amplify(detail) = la ley FIJA del engine), y las capas SOBRE el base plegado —
            ' skee MASKT primero y Face [Ovl] después, MISMO ORDEN QUE EL BAKE (WriteSseFaceDiffuseWithOverlays).
            SseFaceGenBaker.FoldFacetintIntoDiffuse(acc, facetint, npix, detailAcc)
            If SseFoldLayerStack.HasWork(skeeRaw, faceOvl) Then
                SseFoldLayerStack.ComposeCpu(acc, skeeRaw, faceOvl, skinRgb, w, h)
            End If
            ' ⭐ MISMA LEY QUE EL BAKE: se invierte la cadena del engine (softlight con el facetint REAL × amplify
            ' del detail REAL) para que el shader — que aplica esa cadena con los slots 3 y 6 INTACTOS — vuelva a
            ' este mismo buffer. Antes el render neutralizaba los slots y el bake también; eso se cayó al medir
            ' in-game que neutralizar el slot 6 apaga la cara (el motor deriva de él algo más que el albedo).
            ' Render y bake tienen que hacer LO MISMO o el preview deja de predecir el juego.
            SseFaceGenBaker.PreCompensateEngineChain(acc, facetint, detailAcc, npix)

            ' Mismo gate que el IN: el loop de medias sólo se paga si alguien va a leer el log.
            If Logger.Enabled Then
                Dim mO(2) As Double
                For i = 0 To npix - 1
                    For ch = 0 To 2
                        mO(ch) += acc(i * 4 + ch)
                    Next
                Next
                Dim nSkeeL = If(skeeRaw Is Nothing, 0, skeeRaw.Count)
                Dim nOvlL = If(faceOvl Is Nothing, 0, faceOvl.Count)
                Logger.LogLazy(Function() $"[SSE-FOLD] OUT: folded(sRGB)=({mO(0) / npix:F3},{mO(1) / npix:F3},{mO(2) / npix:F3}) " &
                                          $"skeeLayers={nSkeeL} faceOverlays={nOvlL} capas=CPU  (esperado ~0.35-0.45; ~1.0 = satura)")
            End If

            ' --- LOSSLESS (como FO4): NO se pasa por ningún encode/decode BCn. Esa pérdida es del ARCHIVO, no del
            ' COMPOSE. `acc` está en sRGB; el render espera LINEAL crudo ⇒ se convierte sRGB→LINEAL acá.
            ' ⭐ Se sube Rgba32f (NO RGBA8) — MISMA convención que el camino GPU-residente, que deja la textura
            ' float sin cuantizar. Bajar a byte acá metía un redondeo que el GPU no tiene, y encima EN ESPACIO
            ' LINEAL, donde 8 bits aplastan las sombras (el sRGB de 8 bits es perceptualmente uniforme; el lineal
            ' NO): la paridad quedaba limitada por el TRANSPORTE en vez de por el compose. ⛔ No volver a RGBA8.
            ' La conversión es IN-PLACE sobre `acc` (local, no se lee después del upload) ⇒ sin allocation extra,
            ' y en orden RGBA directo — el camino de bytes tenía que swapear a BGRA, acá ese swap desaparece.
            ' Se CONSERVA el clamp a [0,1] que hacía ClampByte255: el fold puede pasarse de 1.0 (el amplify del
            ' detail llega a ×4 si el slot 3 trae valores altos) y saturar es el comportamiento previo; sacarlo
            ' sería un cambio de semántica aparte. ---
            ' ⭐ RESAMPLE AL TARGET **ANTES** DEL sRGB→LINEAL, y en FLOAT.
            '   · ANTES del cvt porque bilinear-en-sRGB ≠ bilinear-en-lineal, y el bake resamplea sobre los
            '     valores sRGB (su BGRA) ⇒ hacerlo después divergiría del bake Y del camino GPU (donde el
            '     ConvertTextureSpace final muestrea el sRGB y convierte en el mismo pase).
            '   · EN FLOAT (ResampleRgbaFloat, mismo filtro que ResampleBgra) para no cuantizar a 8 bits en el
            '     medio: la pérdida de bytes es del ARCHIVO, no del compose — misma regla que rige todo este path.
            Dim accOut = FaceTintCpuCompositor.ResampleRgbaFloat(acc, w, h, outW, outH)
            Dim outPix = outW * outH
            ' Paralelo por rangos (por-píxel puro, escrituras disjuntas ⇒ bit-idéntico): un Math.Pow por canal.
            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, outPix),
                Sub(range)
                    For i = range.Item1 To range.Item2 - 1
                        For ch = 0 To 2
                            Dim lin = SseFaceGenBaker.Srgb2Lin(accOut(i * 4 + ch))
                            accOut(i * 4 + ch) = CSng(If(lin < 0.0, 0.0, If(lin > 1.0, 1.0, lin)))
                        Next
                    Next
                End Sub)
            ' forceOpaque:=True = el alpha 255 que escribía el camino de bytes (misma ley que el GPU-residente).
            foldedId = SseFoldLayerStack.UploadRgba32f(accOut, outPix, outW, outH, forceOpaque:=True)
            If foldedId = 0 Then
                Logger.LogLazy(Function() "[SSE-FOLD] ABORT: GL.GenTexture devolvió 0 (¿sin contexto GL?)")
                Return False
            End If
        End If

        ' ⛔ NO se cambia NINGÚN path del material: sigue apuntando al complexion REAL. Apuntarlo a una ruta
        ' sintética hace que Process_Textures_GL se la pida al loader (pide todo path de Textures_Path_List que no
        ' esté ya en el diccionario), no exista en disco, y la shape salga BLANCA.
        '
        ' ⭐⭐ PERO LA TEXTURA VA BAJO UNA CLAVE **PER-NPC**, NO BAJO LA DEL COMPLEXION.
        ' La del complexion (`…\female\femalehead.dds`) es COMPARTIDA entre shapes y entre NPCs de la misma raza:
        ' instalar ahí el resultado del fold hacía que otra cabeza con el mismo complexion heredara el face-paint
        ' de ésta. El facetint nunca tuvo el problema porque su clave ya era per-NPC — acá se aplica la MISMA ley.
        ' El material NO referencia esta clave; el bind la alcanza por MaterialData.SseFoldedDiffuseKey, así que el
        ' loader nunca la pide y no puede haber cara blanca. IsSRGB=False: los bytes ya son lineales.
        Dim foldedKey = SseFoldedDiffuseKeyFor(npcData.FormID)
        ' outW/outH (no w/h): la textura instalada es la RESAMPLEADA al tamaño de CharGen Options; el entry
        ' tiene que declarar SU tamaño real o el resto del render lee dimensiones que no son las de la textura.
        InstallTexture(model, foldedKey, foldedId, outW, outH, isSrgb:=False)
        ' La consume DiffuseTexture_ID (Render.vb). Se resuelve por clave, no por id, así que un diccionario
        ' limpiado devuelve 0 y el bind se cae solo al complexion real — sin ventana de textura colgada.
        foldedDiffuseKey = foldedKey

        ' --- 5. Slots 3 y 6: SE DEJAN INTACTOS. ---
        ' Antes se les reemplazaba la textura por un neutro (detail (63,64,63) = amplify 1; tint 128 = softlight
        ' identidad) porque el diffuse traía la cadena ya plegada. Se cayó al MEDIR in-game: con el facetint
        ' neutralizado la cara sale oscura aunque el albedo dé aritméticamente exacto ⇒ el motor deriva del slot 6
        ' algo MÁS que el albedo (subsurface), y eso no se puede plegar en un diffuse.
        ' Ahora el diffuse va PRE-COMPENSADO (inversa de softlight×amplify, PreCompensateEngineChain), así que el
        ' shader aplica la cadena con los slots reales y vuelve al mismo buffer. MISMA LEY QUE EL BAKE.

        Return True
    End Function

    ''' <summary>⚠️ PROVISORIO (con <see cref="ApplySseFacetintFolded"/>). BGRA plano de un color constante.</summary>
        ' (Eliminada FlatBgra: la usaba el install de los neutros de los slots 3/6, que ya no existe.)

    ''' <summary>⚠️ PROVISORIO (con <see cref="ApplySseFacetintFolded"/>). Apunta el entry del diccionario a una textura
    ''' GL ya subida. El toggle recarga el NPC entero (ReloadCurrentNpcFull), que es quien reconstruye/libera.
    ''' ⛔ NUNCA libera una textura del LOADER (la del DDS original): puede seguir referenciada en otro lado y
    ''' borrarla invalida el handle ⇒ el sampler devuelve BLANCO. Por eso el gate es
    ''' <see cref="PreviewModel.Texture_Loaded_Class.OwnedByComposer"/> y no "prev &lt;&gt; 0".
    ''' ⭐ SÍ libera la anterior cuando la instalamos NOSOTROS: al pisar Texture_ID nadie más conserva ese handle,
    ''' y el fold se re-ejecuta en cada refresh de edición en vivo (NpcSkinLivePreview) y en el hook post-upload,
    ''' así que sin este borrado cada tick deja una textura huérfana — a 4096² son 268 MB de VRAM por tick con el
    ''' upload Rgba32f. La primera instalación NO borra (prev es del loader): el default de OwnedByComposer es False.</summary>
    Private Shared Sub InstallTexture(model As PreviewModel, key As String, id As Integer, w As Integer, h As Integer, isSrgb As Boolean)
        If id = 0 Then Return
        Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
        If model.Textures_Dictionary.TryGetValue(key, entry) AndAlso entry IsNot Nothing Then
            Dim prev = entry.Texture_ID
            Dim freedPrev = entry.OwnedByComposer AndAlso prev <> 0 AndAlso prev <> id
            If freedPrev Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(prev) : Catch : End Try
            entry.Texture_ID = id : entry.Loaded = True : entry.Size = New System.Drawing.Size(w, h) : entry.IsSRGB = isSrgb
            entry.OwnedByComposer = True
            Logger.LogLazy(Function() $"[SSE-FOLD]   install '{key}': id {prev} → {id} ({w}x{h}, sRGB={isSrgb}, prevLiberada={freedPrev})")
        Else
            ' .Path = key OBLIGATORIO: Render.CleanSingleTexture selecciona las texturas a liberar con
            ' `pf.Path.Equals(Cual)`. Una entrada con Path vacío NO matchea ⇒ se quita del diccionario y la
            ' textura GL queda sin borrar (fuga silenciosa).
            model.Textures_Dictionary(key) = New PreviewModel.Texture_Loaded_Class With {
                .Texture_ID = id, .Loaded = True, .Size = New System.Drawing.Size(w, h), .IsSRGB = isSrgb,
                .Path = key, .OwnedByComposer = True}
            Logger.LogLazy(Function() $"[SSE-FOLD]   install '{key}': NUEVO entry id={id} ({w}x{h}, sRGB={isSrgb})")
        End If
    End Sub

    ''' <summary>SSE: compose the per-NPC facetint (engine-exact, SseFaceTintComposer) and install it as the
    ''' FaceTint mesh's InnerLayerTexture (texture-set slot 6). The shared render SOFT-LIGHTS it onto the diffuse
    ''' (bHasDetailMask -> texGlowmap = PS t3), matching the engine facegen PS. NOT baked into the diffuse (that
    ''' is the FO4 path). Returns True when installed. SSE-only; the FO4 path is untouched.</summary>
    Private Function ApplySseFacetint(materialBase As FO4UnifiedMaterial_Class, npcData As NPC_Data, race As RACE_Data, model As PreviewModel,
                                      Optional host As NpcRenderHost = Nothing,
                                      Optional effRaceFid As UInteger = 0UI) As Boolean
        If npcData Is Nothing Then Return False
        ' Raza EFECTIVA: la del state (override de raza del editor incluido). npcData.RaceFormID es la
        ' cruda del récord — sólo fallback cuando el caller no pasa la efectiva (paths sin state).
        If effRaceFid = 0UI Then effRaceFid = npcData.RaceFormID
        ' race may be Nothing for SSE (the FO4 layer builder can return it unset); parse it from the effective race.
        If race Is Nothing AndAlso effRaceFid <> 0UI Then
            Dim rr = _ctx.PluginManager.GetRecord(effRaceFid)
            If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = _ctx.ParseRaceCached(rr)
        End If
        ' ⛔ Los `Return False` de acá abajo eran SILENCIOSOS: un fallo dejaba la cara con el facetint
        ' anterior sin ninguna traza (bug "el tint no responde" imposible de diagnosticar por log).
        ' Cada salida temprana loguea ahora su motivo bajo [SSE-FACETINT].
        If race Is Nothing Then
            Logger.LogLazy(Function() $"[SSE-FACETINT] skip: RACE 0x{effRaceFid:X8} no parsea (fid=0x{npcData.FormID:X8})")
            Return False
        End If
        Dim npcRec = _ctx.PluginManager.GetRecord(npcData.FormID)
        If npcRec Is Nothing Then
            Logger.LogLazy(Function() $"[SSE-FACETINT] skip: GetRecord(0x{npcData.FormID:X8}) Nothing")
            Return False
        End If
        ' ⭐⭐ TAMAÑO DEL FACETINT = CharGen Options, IGUAL QUE EL BAKE (FaceGenBuilder: `fSz =
        ' ResolveResolutionSize(OutputSettings.Diffuse, 512)`). ⛔ Estaba HARDCODEADO en 512²: con el setting en
        ' 1024/2048 el bake horneaba un facetint de ESE tamaño y el preview seguía componiendo a 512² ⇒ el
        ' preview no mostraba lo que se hornea. Es el camino NO plegado, o sea el de la mayoría de los NPC.
        ' 512 sigue siendo el default (Inherit → 512 = el tamaño vanilla del facetint), así que sin tocar el
        ' setting esto es byte-inerte.
        Dim W As Integer = FaceTintConvention.ResolveResolutionSize(FaceGenBuilder.OutputSettings.Diffuse, 512)
        Dim H As Integer = W

        ' ⭐ COMPOSITE = ESPEJO DEL FLAG DE LA CÁMARA (Setting_GPUSkinning), IGUAL QUE FO4. El flag es el ÚNICO
        ' criterio: GPU si está activo (y hay contexto GL), CPU si no. Ya NO hay un gate por overlays — el facetint
        ' es TINT-ONLY (los overlays y las skee-masks van sobre el DIFFUSE, en el fold), así que no hay nada que el
        ' GPU no pueda componer.
        ' ⛔ SIN FALLBACK: o todo GPU o todo CPU. Si el flag pide GPU y el GPU falla, se ABORTA con log — componer por
        ' CPU a escondidas taparía el bug y mostraría algo que el flag no pidió.
        ' ⭐ LOSSLESS EN AMBOS (como FO4): ninguno pasa por un encode/decode BCn. Antes el CPU comprimía a BC3 y lo
        ' descomprimía (para que el preview mostrara el archivo bakeado) mientras el GPU no ⇒ CPU y GPU NUNCA podían
        ' coincidir. La pérdida BCn es del ARCHIVO, no del COMPOSE: lo que tiene que ser agnóstico es el compose.
        ' ⭐ Y POR LO MISMO, AMBOS TERMINAN EN Rgba32f: antes los dos bajaban a Rgba8, cuantizando DENTRO del
        ' compose y en espacio LINEAL (un paso de byte lineal ≈ 13 pasos sRGB en sombras). El destino de 8 bits es
        ' del ARCHIVO que hornea el bake, no del preview. ⛔ No volver a UploadRgba8Linear acá.
        Dim newId As Integer
        If Config_App.Current.Setting_GPUSkinning AndAlso host IsNot Nothing Then
            newId = ComposeSseFacetintTexGpu(npcRec, race, npcData, W, H, host, effRaceFid)
            If newId = 0 Then
                Logger.LogLazy(Function() "[SSE-FACETINT] ABORT: el compose GPU falló y el flag pide GPU. NO se compone por CPU.")
                Return False
            End If
        Else
            Dim acc = SseFaceGenBaker.ComposeFacetintAcc(_ctx.PluginManager, npcRec, race, effRaceFid, npcData.IsFemale,
                                                         W, H, npcData.SseTintRaw, npcData.SseTintTexOverride)
            If acc Is Nothing Then
                Logger.LogLazy(Function() $"[SSE-FACETINT] ABORT: ComposeFacetintAcc Nothing (race=0x{effRaceFid:X8} fid=0x{npcData.FormID:X8})")
                Return False
            End If
            ' ⛔ CLAMP [0,1] OBLIGATORIO — NO borrar. Lo hacía `LinearRgbaToBgra` de forma IMPLÍCITA (su ClampByte
            ' por canal acota antes de escribir el byte); al dejar de pasar por bytes hay que hacerlo EXPLÍCITO o
            ' se rompe la paridad: el GPU clampea siempre (`res_c = clamp(res_c, 0.0, 1.0)` en el fragment del
            ' compositor) y el CPU quedaría sin acotar. Es ALCANZABLE: en SseFaceTintComposer.ComposeLayer la
            ' cobertura es `mask × tinv` con `tinv = TINV/100` SIN acotar ⇒ el lerp puede pasarse de [0,1].
            For i = 0 To W * H - 1
                For ch = 0 To 2
                    Dim v = acc(i * 4 + ch)
                    acc(i * 4 + ch) = If(v < 0.0, 0.0, If(v > 1.0, 1.0, v))
                Next
            Next
            ' Mismo destino que el GPU. (LinearRgbaToBgra sigue viva: la usa el dump TGA del bake, que SÍ escribe
            ' un archivo de 8 bits — ahí la cuantización es del formato de salida, no del compose.)
            newId = SseFoldLayerStack.UploadRgba32f(acc, W * H, W, H, forceOpaque:=True)
            If newId = 0 Then
                Logger.LogLazy(Function() "[SSE-FACETINT] ABORT: UploadRgba32f devolvió 0 (sin contexto GL?)")
                Return False
            End If
        End If
        Dim origin = _ctx.PluginManager.GetOriginatingPluginName(npcData.FormID)
        Dim fg = PluginManager.ToFaceGenLocalFormID(npcData.FormID)
        ' The engine facetint path (also what CK writes to NIF slot 6). Register the composed GL texture under
        ' this key so InnerLayerTexture_ID (GetTextureID) resolves it; it is linear, not sRGB.
        Dim facetintPath = $"textures\actors\character\facegendata\facetint\{origin}\{fg:X8}.dds"
        Dim key = FO4UnifiedMaterial_Class.CorrectTexturePath(facetintPath)
        Dim entry As PreviewModel.Texture_Loaded_Class = Nothing
        If model.Textures_Dictionary.TryGetValue(key, entry) AndAlso entry IsNot Nothing Then
            ' ⚠️ ACÁ el borrado es INCONDICIONAL, a diferencia de InstallTexture (que exige OwnedByComposer).
            ' NO es una contradicción: son claves con riesgo de COMPARTICIÓN distinto.
            '   · Esta clave es `facetint\<plugin>\<formid>.dds` = PER-NPC. La única textura que puede haber
            '     debajo es el facetint vanilla de ESTE NPC (cargado por el loader) o el nuestro anterior;
            '     ningún otro shape la referencia. Gatearla por OwnedByComposer haría que la PRIMERA
            '     instalación no liberara la del loader ⇒ fuga garantizada en cada NPC.
            '   · La de InstallTexture es la del COMPLEXION (`femalehead.dds`) = COMPARTIDA entre shapes y
            '     entre NPCs de la misma raza. Borrar ahí la del loader deja a otro shape con un handle
            '     inválido ⇒ sampler BLANCO. Por eso allá el gate SÍ es obligatorio.
            If entry.Texture_ID <> 0 AndAlso entry.Texture_ID <> newId Then OpenTK.Graphics.OpenGL4.GL.DeleteTexture(entry.Texture_ID)
            entry.Texture_ID = newId : entry.Loaded = True : entry.Size = New System.Drawing.Size(W, H) : entry.IsSRGB = False
            entry.OwnedByComposer = True
        Else
            ' .Path = key: ver la nota en InstallTexture (CleanSingleTexture matchea por Path; vacío = fuga).
            model.Textures_Dictionary(key) = New PreviewModel.Texture_Loaded_Class With {.Texture_ID = newId, .Loaded = True, .Size = New System.Drawing.Size(W, H), .IsSRGB = False, .Path = key, .OwnedByComposer = True}
        End If
        materialBase.InnerLayerTexture = facetintPath
        Logger.LogLazy(Function() $"[SSE-FACETINT] OK: compuesto e instalado slot6='{facetintPath}' texId={newId} (fid=0x{npcData.FormID:X8} race=0x{effRaceFid:X8})")
        Return True
    End Function


    ''' <summary>Rama GPU del render espejo del skinning: compone el facetint SSE (tint-only) PURO GPU — las MISMAS
    ''' capas que el CPU (<see cref="SseFaceTintComposer.BuildLayerInputs"/>) sobre un base PLANO = seed(0.5) vía
    ''' <see cref="FaceTintCompositor.ApplyFaceTintPipeline"/> (ley SSE all-linear). Es el MISMO compose que el
    ''' <c>_2b</c> del bake. Base subido LINEAL (baseDiffuseIsLinearOnGpu) = seed 0.5-lin.
    ''' Devuelve el TEXTURE-ID Rgba32f LINEAL, <b>propiedad del CALLER</b> (él lo instala y él lo libera; mismo
    ''' contrato que <c>ApplyPipelineResultToDict</c> y que <c>SseFoldLayerStack.ComposeFoldedGpuResident</c>:
    ''' <c>AllocateResultTextureAndFbo</c> genera una textura fresca por llamada — sólo el FBO se reusa, la
    ''' textura NO sale del pool de ping-pong). 0 = FALLO del GPU ⇒ el caller ABORTA con log; NO compone por CPU
    ''' (o todo GPU o todo CPU). GL-bound.
    ''' ⭐ GPU-RESIDENTE, SIN READBACK: antes hacía <c>GetTexImage</c> a bytes y re-subía Rgba8. Eso (a) frenaba el
    ''' pipeline con una transferencia bloqueante en el camino caliente — el mismo patrón que
    ''' <c>ComposeFoldedGpuResident</c> ya había eliminado — y (b) cuantizaba a 8 bits EN MEDIO del compose,
    ''' contra la doctrina "la pérdida BCn es del ARCHIVO, no del COMPOSE", y encima en espacio LINEAL.
    ''' ⭐ SEED EXACTA 0.5 EN FLOAT, no el byte 128 (=0.50196): el CPU (<c>ComposeFacetintAcc</c>) siembra 0.5
    ''' exacto, así que el byte metía una divergencia CPU/GPU de 0.00196 en el término del soft-light del albedo
    ''' facegen (cota del error resultante: <c>2·a·(1−a)·0.00196 ≤ 0.00098</c>). Estaba TAPADA por la cuantización que se acaba de quitar
    ''' (0.5×255 = 127.5 → redondeo bancario → 128 = justo el literal del GPU); al pasar a float quedaría
    ''' EXPUESTA. ⛔ No volver a sembrar por bytes: el float y la seed exacta van juntos.
    ''' Sin capas (raza sin tints) → la seed plana ES el facetint neutro correcto: se devuelve TAL CUAL,
    ''' transfiriendo la propiedad (<c>seedTex = 0</c>) para que el <c>Finally</c> no libere lo que se instala.</summary>
    Private Function ComposeSseFacetintTexGpu(npcRec As PluginRecord, race As RACE_Data, npcData As NPC_Data, w As Integer, h As Integer, host As NpcRenderHost,
                                              Optional effRaceFid As UInteger = 0UI) As Integer
        If host Is Nothing Then Return 0
        If effRaceFid = 0UI Then effRaceFid = npcData.RaceFormID   ' raza efectiva del caller; cruda solo como fallback
        Dim seedTex As Integer = 0
        Try
            seedTex = SseFoldLayerStack.UploadRgba32fFlat(0.5F, 0.5F, 0.5F, 1.0F, w, h)
            If seedTex = 0 Then Return 0
            Dim layers = SseFaceTintComposer.BuildLayerInputs(_ctx.PluginManager, npcRec, race, effRaceFid, npcData.IsFemale, npcData.SseTintRaw, npcData.SseTintTexOverride)
            If layers Is Nothing OrElse layers.Count = 0 Then
                Dim neutral = seedTex : seedTex = 0   ' transferencia de propiedad: la seed plana ES el resultado
                Return neutral
            End If
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              seedTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                              baseDiffuseIsLinearOnGpu:=True)
            ' ⛔ Sin fallback silencioso: si HAY capas y el pipeline no devolvió una textura fresca, es un FALLO del GPU
            ' (no "usá el base"): devolver la seed daría un facetint neutro y el NPC saldría con el tono equivocado
            ' sin que nadie se entere. 0 ⇒ el caller aborta con log.
            If pr Is Nothing OrElse pr.Diffuse Is Nothing OrElse Not pr.Diffuse.IsFresh Then Return 0
            Return pr.Diffuse.TextureId
        Finally
            ' La seed ya fue consumida por el pipeline (o nunca se usó). El id DEVUELTO nunca pasa por acá: o es
            ' la salida fresca del pipeline, o es la seed con la propiedad ya transferida (seedTex = 0).
            If seedTex <> 0 Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(seedTex) : Catch : End Try
        End Try
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
            ' meshCount + un censo de gates: cuántas meshes tienen material, cuántas son SkinTint, cuántas
            ' quedaron excluidas por FaceTint/override — para distinguir "el modelo del host no tiene body"
            ' (editor preview) de "el body está pero sus materiales no pasan los gates" (bug real).
            Dim total = model.meshes.Count
            Dim withMat = 0, skinTintN = 0, faceN = 0, ovrN = 0
            For Each m In model.meshes
                Dim mb = m?.MeshData?.Material?.MaterialBase
                If mb Is Nothing Then Continue For
                withMat += 1
                If mb.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint Then faceN += 1
                If mb.SkinTint Then
                    skinTintN += 1
                    If mb.SkinTintFromOverride Then ovrN += 1
                End If
            Next
            Logger.LogLazy(Function() $"[SSE-BODY] guard OK. QNAM=({qr},{qg},{qb},A={qa}) opacity={opL:F3} → applying to body SkinTint shapes (meshes={total} conMat={withMat} skinTint={skinTintN} faceTint={faceN} skinTintFromOverride={ovrN})")
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
                                           regionSwaps As IList(Of FaceRegionSwapInput),
                                           Optional headDiffuseAlphaTest As Boolean = False) As Boolean
        Dim dB = FilesDictionary_class.GetBytes(diffusePath)
        If dB Is Nothing Then Return False
        Dim nB = If(Not String.IsNullOrEmpty(normalPath), FilesDictionary_class.GetBytes(normalPath), Nothing)
        Dim sB = If(Not String.IsNullOrEmpty(specPath), FilesDictionary_class.GetBytes(specPath), Nothing)
        Dim cpu As FaceTintCpuCompositor.CpuPipelineResult
        Try
            ' ⭐ `FaceGenBuilder.OutputSettings` en vez de `Nothing` (= Inherit/nativo): el bake le pasa
            ' EXACTAMENTE esto a este MISMO ComposeCpuPipeline, así que pasarle Nothing hacía que el preview
            ' ignorara CharGen Options y mostrara más detalle del que el DDS horneado va a tener. Con Inherit el
            ' valor efectivo es el de antes ⇒ byte-inerte por default.
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(dB, nB, sB, layerInputs, regionSwaps, FaceGenBuilder.OutputSettings, diffusePath, normalPath, specPath, headDiffuseAlphaTest)
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
