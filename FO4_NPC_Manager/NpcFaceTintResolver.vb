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
' Alias (no import plano): OpenGL4 trae PixelFormat/TextureUnit que chocan con System.Drawing.Imaging.
Imports Gl4 = OpenTK.Graphics.OpenGL4

''' <summary>Phase 2 of the MainForm split: FaceTint compositor EXECUTION — runs the
''' face-tint compositor + the skin SoftLight / subsurface pre-passes onto the GL textures,
''' snapshots/rolls back pristine diffuse pixels, and the live face-tint refresh path. Standalone
''' class, DI. The skin-override live-preview fast-path (RefreshBodySkinLivePreview et al.) stays in
''' MainForm because it is coupled to CollectArmoCandidates (MeshCollection, not yet extracted) and
''' calls back into ApplyFaceTintOverlay / RestoreCapturedDiffusesToPristine here. See 61-perf-mainform-split.</summary>
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
        LastSseBakeEmitsFoldedNormal = False

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

    ''' <summary>¿El bake de este NPC va a emitir el _msn plegado? Es el MISMO predicado que usa el
    ''' bake (HasFaceOverlayNormals), calculado acá por shape al componer la escena. Lo consume el
    ''' repunte del export para decidir si pisa el slot 1: preguntarle al disco no sirve, porque el
    ''' DDS puede estar dentro de un BA2 y se leería como ausente.</summary>
    Friend Property LastSseBakeEmitsFoldedNormal As Boolean

    ''' <summary>Copia la respuesta de subsurface del material de la CARA sobre cada material de piel del
    ''' cuerpo cuya respuesta difiera, para que cara y cuerpo se iluminen igual. Gana la cara y se copian los
    ''' dos campos verbatim, incluido el False. No-op cuando ya coinciden.
    ''' <para>âš ï¸ Es una HEURISTICA, no una ley del motor: va detras de un toggle, OFF por defecto. Ver
    ''' 30-fo4-subsurface-match-heuristica.</para>
    ''' <para>No aplica los guards del SoftLight (skin tone, QNAM): el subsurface es una propiedad de
    ''' iluminacion del material, independiente del TONO de piel. Render-only y sin persistencia.</para></summary>
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
        ' ⛔ SACADO: `shaderInventoryForDiag`. Era una List(Of String) que se poblaba con un string interpolado
        ' POR CADA malla no-FaceTint, en cada compose (o sea en cada refresh de edicion viva), y que NO LA LEIA
        ' NADIE — el "se emite en el camino de fallo" que prometia el comentario nunca se escribio. Trabajo y
        ' allocations puros en release. Si alguna vez hace falta el inventario, va CONSTRUIDO DENTRO de un
        ' `If Logger.Enabled Then`, no fuera.
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
                    LastSseBakeEmitsFoldedNormal = True
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
                ' âš ï¸ PROVISORIO: el toggle solo FUERZA el fold en NPCs que no lo necesitan (vanilla), para poder
                ' comparar tono con y sin pliegue. Con mustFold=True el fold va si o si y la UI lo deshabilita.
                ' â›” RAZA EFECTIVA (state.RaceFormID) y NO npcData.RaceFormID: npcData sale del parse crudo mas
                ' el preset LM y no lleva el override de raza del editor, asi que tras cambiar de raza la CARA
                ' componia con el catalogo de tints de la raza VIEJA y el cuerpo con la nueva.
                ' DEDUP DEL FOLD: si dos meshes FaceTint del MISMO NPC resuelven al MISMO complexion, el fold de
                ' las dos es identico por construccion y ambas terminan usando la misma textura per-NPC.
                ' â›” El dedup es SOLO del compose: la segunda mesh igual recibe la clave del diffuse plegado y
                ' pasa por ApplySseFacetint (que instala el facetint bajo la clave per-NPC y escribe el
                ' InnerLayerTexture de ESE material). Saltear cualquiera de las dos la deja sin diffuse o sin slot 6.
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
                        ' â›” YA NO SE NEUTRALIZA NADA: el diffuse plegado viene PRE-COMPENSADO (la inversa de la
                        ' cadena del motor), asi que los slots 3 y 6 quedan con su contenido REAL, el shader los
                        ' aplica normalmente y la cadena se cancela. Identico al bake.
                        ' El slot 6 tiene que llevar el FACETINT REAL: como el diffuse va pre-compensado, el
                        ' shader NECESITA re-aplicar softlight(slot0, facetint) para volver al buffer del fold.
                        ' Sin eso el slot 6 queda sin textura, el shader cae al gris default y el NPC PIERDE el
                        ' skin tint.
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

            ' ⛔ SYNC: CPU/GPU compositor — el COMPOSITE ES ESPEJO DEL SKINNING: con GPU-skinning va el
            ' compositor GL, con CPU-skinning el compositor CPU (el MISMO que usa el bake). Los dos tienen que
            ' dar el mismo resultado; ver 50-facetint-leyes-y-compositor.md.
            ' `baseDiffuseIsLinearOnGpu`: acá el base se REUSA de la textura del render. Si el render la cargó
            ' como SRV sRGB, el sample ya es lineal y el seed sólo encodea — volver a aplicarle srgbToLin sería
            ' un DOBLE DECODE. El bake y el CLI cargan la base cruda y pasan False.
            ' `meshDiffuseBaked` alimenta SkinToneBaked ("esta malla tiene el tono horneado en su diffuse").
            ' En GPU es True al llegar acá, sin más: derivarlo de IsFresh no medía lo que parecía, porque en el
            ' camino vivo el diffuse SIEMPRE sale nuevo (el cambio de espacio ya crea la textura). En CPU sí
            ' puede dar False, así que ahí se conserva el booleano real.
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
                    FaceTintCpuCompositor.AccumSpaceCapability,
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
                                         (host.CurrentBaseState IsNot Nothing AndAlso host.CurrentBaseState.HeadDiffuseAlphaTest),
                                         host) Then
                    meshDiffuseBaked = True
                End If
            End If

            ' El tono de piel del slot 12 queda HORNEADO en el diffuse de esta malla (el compositor lo procesa
            ' como capa sintetica). materialBase.SkinTint sigue habilitado -es flag estructural del NIF/BGSM y
            ' no se muta-; en cambio se marca ESTA malla para que Render vuelva no-op su propio softlight de
            ' SkinTint. Sin esto la cara recibe el tono dos veces. El cuerpo FO4 no se toca.
            ' Es una ASIGNACION, no un latch: vale exactamente "el diffuse de esta malla salio compuesto en ESTA
            ' pasada". â›” Las salidas por Continue For de mas arriba no llegan aca y por lo tanto NO reasignan el
            ' flag: esas mallas conservan el valor de la pasada anterior. Los caminos de edicion viva restauran
            ' el diffuse a pristine antes de re-entrar, y las dos puertas tempranas cubren los casos sin nada
            ' que componer, que es donde eso pasaba de verdad.
            mesh.MeshData.Material.SkinToneBaked = meshDiffuseBaked
        Next

    End Sub

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

    ''' <summary>SSE - pliegue del NORMAL de la cabeza en vivo: compone los normales de los overlays de cara
    ''' sobre el <c>_msn</c> y lo instala bajo <paramref name="foldedNormalKey"/>. False implica que el caller
    ''' deja la clave en "" y el bind se queda con el <c>_msn</c> real.
    ''' <para>ES LA MISMA FUNCION QUE EL BAKE, NO UNA REPLICA: el compose sale de
    ''' <see cref="SseOverlayCompositor.ComposeFaceOverlayNormalsIntoMsn"/>, con los mismos dos decodes y la
    ''' misma textura por defecto del slot 0. El pliegue del normal tiene UNA sola implementacion, asi que aca
    ''' no hay un par CPU/GPU que pueda desincronizarse (Setting_GPUSkinning gobierna la cadena del DIFFUSE, que
    ''' si tiene dos replicas; el normal no entra en esa cadena).</para>
    ''' <para>El <c>_msn</c> es MODEL-SPACE y sus 3 canales son ejes independientes: no hay ninguna conversion
    ''' de espacio de color, entra crudo y sale crudo (IsSRGB=False).</para>
    ''' <para>â›” EL ALPHA SE PRESERVA TAL CUAL, NO SE MEZCLA. No es "porque lleva la mascara especular": en una
    ''' malla model-space el mask especular sale del SLOT 7. El alpha del normal lo lee solo la rama no-MSN y el
    ''' envmask del cubemap, que la piel no usa - en la cabeza SSE no lo lee NADIE y mezclarlo seria inventar un
    ''' canal. Ademas seria peligroso: un normal de overlay en BC5 no tiene alpha y el decode lo devuelve
    ''' constante 1, asi que mezclarlo lo llevaria a blanco en toda el area cubierta.</para></summary>
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
            If mImg Is Nothing OrElse mImg.Rgba8 Is Nothing OrElse mImg.Width <= 0 OrElse mImg.Height <= 0 Then
                Logger.LogLazy(Function() $"[SSE-FOLD] normal ABORT: no decodifica el _msn '{mKey}'")
                Return False
            End If
            Dim mw = mImg.Width, mh = mImg.Height, npix = mw * mh
            Dim acc(npix * 4 - 1) As Single
            mImg.CopyUnitTo(acc)
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

    ''' <summary>SSE - camino PLEGADO del render: compone en vivo lo MISMO que el bake plegado, con el MISMO
    ''' orden (WriteSseFaceDiffuseWithOverlays), una sola ley:
    '''   1. base = complexion (slot 0)
    '''   2. FoldFacetintIntoDiffuse: softlight(complexion, facetint) x amplify(detail)
    '''   3. skee MASKT encima (sobre el albedo YA tintado: por eso hay que plegar para poder mostrarlas)
    '''   4. overlays Face [Ovl] encima
    '''   5. PreCompensateEngineChain = inversa de la cadena del motor; los slots 3 y 6 quedan con su contenido
    '''      REAL y el shader los aplica normalmente, asi que la cadena se cancela y se ve el plegado tal cual.
    ''' Corre cuando el NPC tiene skee MASKT u overlays de cara (la MISMA condicion que el bake) o cuando el
    ''' toggle provisorio lo fuerza. LOSSLESS como en FO4: no pasa por ningun encode/decode BCn, esa perdida es
    ''' del ARCHIVO. Devuelve False si falta algo y el caller cae al camino normal.</summary>
    ''' <param name="foldedDiffuseKey">SALIDA: clave PER-NPC bajo la que quedo instalado el diffuse plegado; el
    ''' caller la copia al MaterialData de la mesh, que es lo que hace que el bind la use. "" si devuelve False.</param>
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
        ' ⭐ Cacheado contra el cache per-NPC del host (mismo que usa el compose CPU): el complexion es la
        ' textura MAS GRANDE de la cara (4096² con COtR) y este decode corria ENTERO en cada fold, o sea en
        ' cada refresh de edicion viva, en los DOS modos de camara. El valor es funcion pura de los bytes.
        ' (no 'cDec': CDec es una función intrínseca de VB)
        Dim cImg = FaceTintCpuCompositor.DecodeDdsCached(host?.TintCpuDecodeCache, cKey, cBytes)
        If cImg Is Nothing OrElse cImg.Rgba8 Is Nothing OrElse cImg.Width <= 0 OrElse cImg.Height <= 0 Then
            Logger.LogLazy(Function() $"[SSE-FOLD] ABORT: no decodifica el complexion '{cKey}'")
            Return False
        End If
        Dim w = cImg.Width, h = cImg.Height, npix = w * h
        Dim acc(npix * 4 - 1) As Single
        cImg.CopyUnitTo(acc)

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

            ' LOSSLESS: no se pasa por ningún encode/decode BCn — esa pérdida es del ARCHIVO, no del compose.
            ' ⛔ Se sube Rgba32f, NO RGBA8: bajar a byte acá mete un redondeo que el GPU no tiene, y encima en
            ' espacio LINEAL, donde 8 bits aplastan las sombras. Dejaría la paridad limitada por el TRANSPORTE
            ' en vez de por el compose. No volver a RGBA8.
            ' ⛔ SYNC: el RESAMPLE va ANTES del sRGB→lineal y en FLOAT: bilineal-en-sRGB ≠ bilineal-en-lineal,
            ' y el bake resamplea sobre los valores sRGB — hacerlo después divergiría del bake y del camino GPU.
            ' Se conserva el clamp a [0,1]: el fold puede pasarse de 1.0 y saturar es el comportamiento previo.
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
            ' ⛔⛔ forceOpaque:=False — EL ALPHA DEL COMPLEXION TIENE QUE SOBREVIVIR AL PLIEGUE.
            ' Antes iba True ("el alpha 255 que escribía el camino de bytes"), dando por sentado que el alpha
            ' del diffuse de una cara es inerte. NO LO ES cuando la malla de cabeza es ALPHA-TEST: el shader
            ' hace `color.a *= texDiffuse.a` y después `if (fragColor.a < alphaThreshold) discard`, así que con
            ' alpha 1 en todos los téxeles el test NO DESCARTA NADA y aparece geometría de la cabeza que el
            ' alpha recortaba, con sus téxeles negros de borde. Caso medido: EnhancedMaleKhajiitHead, que
            ' dibuja con AlphaTestRef=73 — al plegar salía un borde negro alrededor de los bigotes.
            ' El alpha viaja intacto por toda la cadena (PreCompOne saltea el canal 3, el resample lo respeta):
            ' el único punto que lo pisaba era este upload.
            ' ⛔ SYNC: el camino GPU-residente hace lo MISMO en el upload del complexion (SseFoldLayerStack).
            foldedId = SseFoldLayerStack.UploadRgba32f(accOut, outPix, outW, outH, forceOpaque:=False)
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
        MatchLoaderSampling(id, w, h)
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

    ''' <summary>⭐ Le da a una textura COMPUESTA el MISMO sampleo que el loader le da al DDS que reemplaza:
    ''' cadena de mips + <c>LinearMipmapLinear</c> + anisotropía máxima + wrap <c>Repeat</c> (DirectXDDSLoader).
    ''' <para>⛔ POR QUÉ. El upload del pliegue (<c>SseFoldLayerStack.UploadRgba32fFromSingles</c>) deja
    ''' <c>MinFilter=Linear</c> y UN SOLO nivel, así que la cara plegada era la ÚNICA textura del render SIN
    ''' minificación: a 2048² sobre una cara de unos cientos de píxeles son ~3 niveles de mip salteados. El
    ''' detalle fino (pelo, rayas, bigotes) sale a brillo pleno y titila al rotar, mientras el MISMO NPC sin
    ''' plegar se ve filtrado ⇒ diferencia fold-vs-no-fold que NO viene de la aritmética del compose. Es el
    ''' MISMO modo de falla que documenta FaceTintCompositor (pisar el LinearMipmapLinear del loader), pero acá
    ''' la textura es NUEVA: no se pisaba nada, nunca se ponía.</para>
    ''' <para>⛔ El wrap va <c>Repeat</c> (el del loader) y no el <c>ClampToEdge</c> del upload: lo que se
    ''' reemplaza es el complexion, que el render sampleaba con Repeat. Sin mips los dos daban lo mismo dentro
    ''' de [0,1]; con mips los niveles altos mezclan el otro borde y la diferencia se ve.</para>
    ''' <para>Costo: +33 % de VRAM por textura. A 2048² Rgba32f, 64 MB → ~85 MB.</para></summary>
    Private Shared Sub MatchLoaderSampling(id As Integer, w As Integer, h As Integer)
        Gl4.GL.BindTexture(Gl4.TextureTarget.Texture2D, id)
        ' El pliegue sube UN solo nivel (no viene de un DDS), así que la cadena se genera acá. Los niveles
        ' que produce GenerateMipmap son los mismos que declara el DDS: hasta 1x1.
        Gl4.GL.GenerateMipmap(Gl4.GenerateMipmapTarget.Texture2D)
        Dim levels = 1
        Dim d = Math.Max(w, h)
        While d > 1
            d \= 2
            levels += 1
        End While
        ' ⛔ SYNC: la ley de sampleo es UNA y vive en el loader. Acá NO se re-escribe: se la llama.
        DirectXDDSLoader.ApplySamplingState(Gl4.TextureTarget.Texture2D, levels, useNearest:=False, isCubemap:=False)
        Gl4.GL.BindTexture(Gl4.TextureTarget.Texture2D, 0)
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

        ' COMPOSITE = ESPEJO DEL FLAG DE LA CAMARA (Setting_GPUSkinning), igual que FO4: GPU si esta activo y
        ' hay contexto GL, CPU si no. Ya no hay gate por overlays, porque el facetint es TINT-ONLY (overlays y
        ' skee-masks van sobre el DIFFUSE, en el fold).
        ' â›” SIN FALLBACK: o todo GPU o todo CPU. Si el flag pide GPU y el GPU falla se ABORTA con log; componer
        ' por CPU a escondidas taparia el bug y mostraria algo que el flag no pidio.
        ' LOSSLESS en ambos y ambos terminan en Rgba32f: la perdida BCn y la cuantizacion a 8 bits son del
        ' ARCHIVO que hornea el bake, no del COMPOSE. â›” No volver a UploadRgba8Linear aca.
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


    ''' <summary>Rama GPU espejo del skinning: compone el facetint SSE (tint-only) puro GPU, con las MISMAS
    ''' capas que el CPU sobre un base PLANO = seed(0.5) via <see cref="FaceTintCompositor.ApplyFaceTintPipeline"/>
    ''' (ley SSE all-linear). Es el mismo compose que el <c>_2b</c> del bake.
    ''' <para>Devuelve el texture-id Rgba32f LINEAL, <b>propiedad del CALLER</b> (el lo instala y el lo libera):
    ''' AllocateResultTextureAndFbo genera una textura fresca por llamada, solo el FBO se reusa. 0 = fallo del
    ''' GPU y el caller ABORTA con log, no compone por CPU. GL-bound.</para>
    ''' <para>GPU-RESIDENTE, SIN READBACK: el GetTexImage + re-subida Rgba8 previo frenaba el pipeline con una
    ''' transferencia bloqueante en el camino caliente y cuantizaba a 8 bits EN MEDIO del compose, en espacio
    ''' lineal.</para>
    ''' <para>â›” SEED EXACTA 0.5 EN FLOAT, no el byte 128 (=0,50196): el CPU siembra 0,5 exacto, asi que el byte
    ''' metia una divergencia CPU/GPU en el termino del softlight. Estaba TAPADA por la cuantizacion que se
    ''' acaba de quitar. No volver a sembrar por bytes: el float y la seed exacta van juntos.</para>
    ''' <para>Sin capas (raza sin tints) la seed plana ES el facetint neutro correcto y se devuelve tal cual,
    ''' transfiriendo la propiedad para que el Finally no libere lo que se instala.</para></summary>
    Private Function ComposeSseFacetintTexGpu(npcRec As PluginRecord, race As RACE_Data, npcData As NPC_Data, w As Integer, h As Integer, host As NpcRenderHost,
                                              Optional effRaceFid As UInteger = 0UI) As Integer
        If host Is Nothing Then Return 0
        If effRaceFid = 0UI Then effRaceFid = npcData.RaceFormID   ' raza efectiva del caller; cruda solo como fallback
        Dim seedTex As Integer = 0
        Try
            ' ⭐ EL SEED SALE DE LA LEY (CharGen Options), NO DE UN LITERAL. Estaba cableado en 0.5F acá,
            ' que es el camino del facetint NO plegado = el de la MAYORÍA de los NPC, y corre por GPU por
            ' default ⇒ mover el seed no cambiaba el render. Fuente única: SseFaceTintComposer.TryGetFlatSeedRgb
            ' (= BuildSeedSpec), la MISMA que usan el compose CPU y el QNAM del cuerpo.
            Dim seedRgb = SseFaceTintComposer.TryGetFlatSeedRgb()
            If seedRgb Is Nothing Then
                ' Espejo EXACTO del camino CPU: sin seed constante no hay de dónde sembrar (el facetint es
                ' TINT-ONLY) y ComposeLinearRgba devuelve Nothing. Se aborta con log, no se tapa con 0.5.
                Logger.LogLazy(Function() "[SSE-FACETINT] ABORT: la ley pide seed desde textura base y el facetint es TINT-ONLY (no hay base). Igual que el camino CPU.")
                Return 0
            End If
            seedTex = SseFoldLayerStack.UploadRgba32fFlat(seedRgb(0), seedRgb(1), seedRgb(2), 1.0F, w, h)
            If seedTex = 0 Then Return 0
            Dim layers = SseFaceTintComposer.BuildLayerInputs(_ctx.PluginManager, npcRec, race, effRaceFid, npcData.IsFemale, npcData.SseTintRaw, npcData.SseTintTexOverride)
            If layers Is Nothing OrElse layers.Count = 0 Then
                Dim neutral = seedTex : seedTex = 0   ' transferencia de propiedad: la seed plana ES el resultado
                Return neutral
            End If
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              seedTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                              SseFaceTintComposer.AccumSpaceCapability,
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
                                           Optional headDiffuseAlphaTest As Boolean = False,
                                           Optional host As NpcRenderHost = Nothing) As Boolean
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
            ' ⭐ decodeCache = el espejo CPU del TintGpuCache per-host (ver NpcRenderHost.TintCpuDecodeCache).
            ' Sin el, cada refresh de edicion viva re-decodificaba por DirectXTex TODAS las DDS (source D/N/S +
            ' cada capa + cada mascara de swap) mientras el camino GPU las tenia residentes desde el primer
            ' compose. Byte-inerte: el valor cacheado es funcion pura de (bytes, tamaño destino).
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(dB, nB, sB, layerInputs, regionSwaps, FaceGenBuilder.OutputSettings, diffusePath, normalPath, specPath, headDiffuseAlphaTest,
                                                           decodeCache:=host?.TintCpuDecodeCache)
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
        Dim h = _hostProvider()
        h.TintGpuCache.Clear()
        ' Espejo CPU del cache GL: MISMA vida per-NPC (ver NpcRenderHost.TintCpuDecodeCache). Si se limpiara
        ' uno y no el otro, el modo CPU se quedaria con decodes de la raza/TXST del NPC anterior.
        h.TintCpuDecodeCache.Clear()
        h.PristineDiffusePixels.Clear()
        ' ⭐ SSE: MISMA vida que los de FO4 de arriba. Sus caches de TEXTURA (mascara resampleada + fuente
        ' decodificada) son los unicos que pesan de verdad en este modulo, y sin esto sobrevivian toda la
        ' sesion — en la app, que corre SIN techo de presupuesto, eso es memoria que solo crece navegando.
        ' Se sueltan al cambiar de NPC raiz y se CONSERVAN entre recargas del mismo NPC, asi la edicion viva
        ' no paga el re-decode. Los caches de RECORD (capas por raza, CLFM) NO se tocan acá: su vida es la del
        ' load order (SseFaceTintComposer.ClearCaches, desde InvalidateParseCaches).
        SseFaceTintComposer.ClearTextureCaches()
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
            ' Mismo motivo que el HCLF de arriba: el state se sembró una vez al cargar el NPC, así que sin este
            ' re-pull una edición del RGB de pelo (SSE) no se vería hasta recargar. Se asigna DIRECTO (incluido
            ' Nothing) porque limpiar el override es una edición válida: volver al CLFM. SSE-only por origen
            ' (en FO4 el campo es siempre Nothing).
            host.LastRenderedState.SseHairColorRgb = overlayPreset.SseHairColorRgb
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

        ' Stage 3: refresca los uniforms de material (SkinTintColor + GrayscaleToPalette / HairTintColor) de
        ' cada shape cargada. Se setearon al cargar el NIF y son invisibles a MarkDirty(Textures), asi que se
        ' mutan in place.
        ' Se itera renderData.Shapes y no model.meshes para poder cruzar cada shape con su candidate: sin ese
        ' contexto el codigo previo aplicaba color de pelo a CUALQUIER material con paleta habilitada y se
        ' filtraba a armadura de robot, cara y cuerpo. El helper compartido ApplyMaterialPaletteHairColor impone
        ' la regla del motor y es el mismo camino que usa la carga del NIF.
        ' El refresh de SkinTintColor queda inline con la resolucion simple de SkinTone; la carga del NIF usa la
        ' mas rica, que considera solidTintColor para head parts de cara. Esa asimetria es un frente aparte
        ' (50-facetint-residuos-aceptados) y no se toca aca para no cambiar el render de la cara en vivo.
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
                        ' El loader produce orden GDI Format32bppArgb, que en memoria es B,G,R,A: hay que subir
                        ' con PixelFormat.Bgra para que el driver haga el swap. Con Rgba el cuerpo salia azul.
                        ' Y se re-sube en el MISMO espacio de color que uso la carga original: los pixeles
                        ' pristine son los bytes sRGB que decodifico el loader, asi que si el original era un SRV
                        ' sRGB hay que restaurarlo como Srgb8Alpha8 para que la muestra decodifique a lineal.
                        ' Restaurar como Rgba8 plano con entry.IsSRGB=True desincronizaba
                        ' baseDiffuseIsLinearOnGpu y corria el tono en el siguiente composite.
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
