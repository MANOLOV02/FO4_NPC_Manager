Imports System.Linq
Imports FO4_Base_Library
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' SSE — el STACK DE CAPAS del diffuse plegado (skee MASKT + overlays <c>Face [Ovl]</c>), con las DOS réplicas:
''' CPU (<see cref="ComposeCpu"/>) y GPU (<see cref="ComposeGpu"/>). Ambas reciben y devuelven lo MISMO — el
''' acumulador RGBA en sRGB, <c>Double()</c> de w×h×4 — así que el caller elige por el flag de cámara
''' (<c>Setting_GPUSkinning</c>) y NADA más cambia, y la paridad se mide restando los dos arrays.
'''
''' ⭐ QUÉ ENTRA ACÁ Y QUÉ NO. El PLIEGUE en sí (<c>albedo = fgTint(facetint) × softlight(complexion, detail)</c>,
''' <see cref="SseFaceGenBaker.FoldFacetintIntoDiffuse"/>) NO es un stack de capas: es una ley FIJA del engine, sin
''' blend-ops ni cobertura, y se computa igual en los dos caminos (en Double, sin cuantizar). Pasarla por el GPU
''' obligaría a mandar el facetint como TEXTURA de 8 bits, y el fgTint la amplifica ×255/64 ⇒ metería un error que
''' hoy no existe. Lo que SÍ es un stack de capas — y por eso honra el flag — son las MASKT y los overlays.
''' (El sandbox del bake igual hornea el <c>_2d</c> = pliegue por GPU, para medir también esa variante.)
'''
''' Ley CPU = <see cref="SseOverlayCompositor.ApplyOverlays"/> (decodificada del .fx de RaceMenu).
''' Ley GPU = el MISMO <see cref="FaceTintCompositor"/> que usa FO4, con las capas mapeadas 1:1:
'''   skee type 1 (Mask; el ÚNICO que producen las MASKT del NIF) → PaletteMask, canal R, color = MASKC,
'''                                                                 opacidad = MASKA, blend = normal ⇒ alpha-over
'''   skee type 2 (Solid)                                        → UniformColor (cobertura 1)
'''   skee type 0 (Texture × color) y los overlays               → TextureSetDiffuse + MultiplyTextureByColor
''' El blend-op sale de <see cref="SseOverlayCompositor.BlendOpFromSseMode"/> — la MISMA función que usa el CPU —,
''' así que los dos caminos no se pueden desincronizar por el mapeo.
''' </summary>
Friend Module SseFoldLayerStack

    ''' <summary>GPU: el PLIEGUE — <c>albedo = fgTint(facetint) × softlight(srgbToLin(complexion), detail)</c> — réplica
    ''' EXACTA de <see cref="SseFaceGenBaker.FoldFacetintIntoDiffuse"/> (CPU). Entra y sale lo MISMO que el CPU: el
    ''' complexion en sRGB y el resultado en sRGB (<c>Double()</c> RGBA), así que el caller elige camino por el flag y
    ''' nada más cambia. Nothing = FALLO del GPU (el caller aborta; NO se compone por CPU).
    '''
    ''' ⭐ TODO EN FLOAT (Rgba32f): complexion, facetint y detail se suben como textura float y el readback vuelve float.
    ''' MEDIDO por qué importa: el fold GPU viejo (<c>_2d</c>) transportaba los intermedios en 8 bits LINEALES y daba
    ''' RMS 2,4/255 y máx 18 contra el CPU — con el error concentrado en las sombras (5,7 medio en 0..31 vs 0,3 en los
    ''' claros), que es la firma de cuantizar en lineal: cerca del negro, 1 nivel lineal vale ~13 niveles sRGB. Y el
    ''' facetint, además, lo amplifica el fgTint ×255/64. En float no hay dónde perder nada.
    '''
    ''' ⭐ La LEY del pliegue es FIJA (engine, DXBC verificado) y vive en el shader (rama <c>uFgTintFold</c>), NO en
    ''' <see cref="FaceTintConvention"/>: esa convención es la ley (configurable) del bake de FaceTint del CK, otra cosa.
    ''' Si el fold la heredara, tocar una opción de la UI lo desviaría del engine.
    ''' ⚠️ SUPUESTO (hoy cierto, conviene saberlo): el seed y la salida del pipeline SÍ pasan por la convención
    ''' (<c>SeedDiffuseOutputSpace</c>). Con la ley SSE — que es ALL-LINEAR — esas dos conversiones son no-op y el
    ''' complexion entra/sale sin que nadie le toque el espacio, que es lo que el fold necesita. Si alguna vez se
    ''' cambian los espacios del bucket Diffuse de SSE a algo que no sea Linear, el pliegue GPU se desviaría del CPU
    ''' (el sandbox _2c-vs-_2d lo detectaría: para eso está).</summary>
    Friend Function FoldGpu(complexionSrgb As Double(), facetintLinear As Double(), detailRaw As Double(),
                            w As Integer, h As Integer, host As NpcRenderHost) As Double()
        If host Is Nothing OrElse complexionSrgb Is Nothing OrElse facetintLinear Is Nothing Then Return Nothing
        Dim npix = w * h
        Dim baseTex = UploadRgba32f(complexionSrgb, npix, w, h)        ' complexion en sRGB (el shader lo lineariza)
        If baseTex = 0 Then Return Nothing
        Dim tintTex As Integer = 0, detTex As Integer = 0, resId As Integer = 0
        Try
            tintTex = UploadRgba32f(facetintLinear, npix, w, h)        ' facetint CRUDO (lineal), sin cuantizar
            If tintTex = 0 Then Return Nothing
            If detailRaw IsNot Nothing Then detTex = UploadRgba32f(detailRaw, npix, w, h)   ' 0 ⇒ el shader usa b=0.5

            Dim foldLayer As New List(Of FaceTintLayerInput) From {
                New FaceTintLayerInput With {
                    .Kind = FaceTintLayerKind.TextureSetDiffuse,
                    .LayerTextureId = tintTex,                          ' ⭐ textura float, NO un DDS de 8 bits
                    .FoldDetailTextureId = detTex,
                    .FgTintFold = True,
                    .FgTintOffR = CSng(1.0 / 255.0), .FgTintOffG = 0F, .FgTintOffB = CSng(1.0 / 255.0),
                    .FgTintAmp = CSng(255.0 / 64.0),
                    .Opacity = 1.0F, .Slot = 0US, .IsTextureSet = True, .DebugName = "sse-fold"}}
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              baseTex, 0, 0, w, h, foldLayer, New List(Of FaceRegionSwapInput)(),
                                                              baseDiffuseIsLinearOnGpu:=True)
            resId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, 0)
            If resId = 0 Then Return Nothing
            Return ReadbackRgba32f(resId, npix)                         ' ya viene en sRGB (el shader lo encodea)
        Finally
            For Each t In {resId, detTex, tintTex, baseTex}
                If t <> 0 Then Try : GL.DeleteTexture(t) : Catch : End Try
            Next
        End Try
    End Function

    ''' <summary>GPU: compone el FACETINT (las capas de tint del RACE/NPC) sobre el seed 0.5, y lo devuelve en LINEAL
    ''' (<c>Double()</c> RGBA) — la misma salida que <see cref="SseFaceTintComposer.ComposeLinearRgba"/> (CPU). Readback
    ''' en float: el facetint alimenta el fold, que lo amplifica ×255/64, así que cuantizarlo a 8 bits acá multiplicaría
    ''' el error de redondeo por 4. Sin capas (raza sin tints) ⇒ 0.5 plano (= el seed, igual que el CPU), que NO es un
    ''' fallo. Nothing = FALLO del GPU ⇒ el caller aborta.</summary>
    Friend Function ComposeFacetintGpu(tintLayers As IList(Of FaceTintLayerInput), w As Integer, h As Integer, host As NpcRenderHost) As Double()
        If host Is Nothing Then Return Nothing
        Dim npix = w * h
        Dim seed(npix * 4 - 1) As Double
        For i = 0 To npix - 1
            seed(i * 4) = 0.5 : seed(i * 4 + 1) = 0.5 : seed(i * 4 + 2) = 0.5 : seed(i * 4 + 3) = 1.0
        Next
        If tintLayers Is Nothing OrElse tintLayers.Count = 0 Then Return seed
        Dim baseTex = UploadRgba32f(seed, npix, w, h)
        If baseTex = 0 Then Return Nothing
        Dim resId As Integer = 0
        Try
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              baseTex, 0, 0, w, h, tintLayers, New List(Of FaceRegionSwapInput)(),
                                                              baseDiffuseIsLinearOnGpu:=True)
            resId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, 0)
            If resId = 0 Then Return Nothing
            Return ReadbackRgba32f(resId, npix)
        Finally
            If resId <> 0 Then Try : GL.DeleteTexture(resId) : Catch : End Try
            Try : GL.DeleteTexture(baseTex) : Catch : End Try
        End Try
    End Function

    ''' <summary>True si hay algo que componer (evita subir texturas al pedo).</summary>
    Friend Function HasWork(skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                            faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode)) As Boolean
        Return (skeeRaw IsNot Nothing AndAlso skeeRaw.Count > 0) OrElse (faceOvl IsNot Nothing AndAlso faceOvl.Count > 0)
    End Function

    ''' <summary>CPU: compone las capas sobre <paramref name="acc"/> (sRGB, in place). Réplica exacta de skee.</summary>
    Friend Sub ComposeCpu(acc As Double(), skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                          faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                          skinRgb As Double(), w As Integer, h As Integer)
        If skeeRaw IsNot Nothing AndAlso skeeRaw.Count > 0 Then
            Dim layers = SseSkeeMaskReader.ResolveLayersForCpu(skeeRaw, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba, skinRgb, Nothing)
            If layers.Count > 0 Then SseOverlayCompositor.ApplyOverlays(acc, layers, w, h)
        End If
        SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse(acc, faceOvl, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba)
    End Sub

    ''' <summary>GPU: MISMO compose por el compositor compartido. El base (<paramref name="acc"/>, sRGB) se sube como
    ''' Rgba32f — FLOAT, no 8 bits — así que el único redondeo del camino GPU es el mismo del CPU (el byte final), y no
    ''' uno extra del transporte. Devuelve un acumulador NUEVO (sRGB, w×h×4) o Nothing si el GPU no puede (el caller
    ''' cae a CPU). No muta <paramref name="acc"/>. GL-bound: hay que llamarla con el contexto activo.</summary>
    Friend Function ComposeGpu(acc As Double(), skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                               faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                               skinRgb As Double(), w As Integer, h As Integer, host As NpcRenderHost) As Double()
        If host Is Nothing OrElse acc Is Nothing Then Return Nothing
        Dim layers As New List(Of FaceTintLayerInput)
        layers.AddRange(BuildSkeeGpuLayers(skeeRaw, skinRgb))
        layers.AddRange(BuildFaceOverlayGpuLayers(faceOvl))
        If layers.Count = 0 Then Return Nothing

        Dim npix = w * h
        Dim baseTex = UploadRgba32f(acc, npix, w, h)
        If baseTex = 0 Then Return Nothing
        Dim outAcc As Double() = Nothing
        Try
            ' baseDiffuseIsLinearOnGpu:=True ⇒ el pipeline toma el sample del base TAL CUAL (los Rgba32f no llevan
            ' decode sRGB en el sampler). El acumulador vive en el MISMO espacio que el acc del CPU: sRGB.
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              baseTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                              baseDiffuseIsLinearOnGpu:=True)
            Dim resId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, 0)
            If resId = 0 Then Return Nothing
            Try
                outAcc = ReadbackRgba32f(resId, npix)
            Finally
                Try : GL.DeleteTexture(resId) : Catch : End Try
            End Try
        Finally
            Try : GL.DeleteTexture(baseTex) : Catch : End Try
        End Try
        Return outAcc
    End Function

    ''' <summary>skee raw → capas del compositor. Mapeo por TIPO (ver el resumen del módulo). El blend sale de
    ''' <see cref="SseOverlayCompositor.BlendOpFromSseMode"/> = la misma fuente que el CPU.</summary>
    Private Function BuildSkeeGpuLayers(raw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw), skinRgb As Double()) As List(Of FaceTintLayerInput)
        Dim outL As New List(Of FaceTintLayerInput)
        If raw Is Nothing Then Return outL
        For Each l In raw
            ' Resuelve el color/sentinel con la MISMA función del CPU (no se re-implementa el ×2 del preset hair).
            Dim cpuLayer = SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Opacity, Nothing, l.LayerType, l.Blend, skinRgb, Nothing)
            Dim cr = ClampByte(cpuLayer.Color(0)), cg = ClampByte(cpuLayer.Color(1)), cb = ClampByte(cpuLayer.Color(2))
            Dim opa = CSng(cpuLayer.Color(3))
            Dim bop = SseOverlayCompositor.BlendOpFromSseMode(l.Blend).BlendOp
            Dim texBytes As Byte() = Nothing
            If Not String.IsNullOrEmpty(l.TexturePath) AndAlso l.LayerType <> 2 Then
                texBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(l.TexturePath))
            End If

            Select Case l.LayerType
                Case 2   ' Solid: color plano, cobertura 1, alpha = opacidad (CPU: la = ov.Color(3), sin textura).
                    ' FaceTintLayerKind no tiene un kind "color uniforme", pero NO hace falta inventarlo: una capa
                    ' TextureSet con ForceUniformColor (⇒ src = uColor, la textura NO se lee) y una máscara 1×1 BLANCA
                    ' OPACA (⇒ maskV = alpha = 1 ⇒ cov = 1 × opacidad) da EXACTAMENTE la ley CPU del solid.
                    outL.Add(New FaceTintLayerInput With {
                        .Kind = FaceTintLayerKind.TextureSetDiffuse,
                        .LayerDdsBytes = OpaqueWhite1x1Dds(), .LayerCacheKey = "sse-skee-solid-1x1",
                        .ForceUniformColor = True,
                        .R = cr, .G = cg, .B = cb, .Opacity = opa, .BlendOp = bop, .IsTextureSet = True,
                        .DebugName = "skee-solid"})
                Case 1   ' Mask (el de las MASKT del NIF): color plano, cobertura = canal R de la máscara × opacidad.
                    If texBytes Is Nothing Then Continue For
                    outL.Add(New FaceTintLayerInput With {
                        .Kind = FaceTintLayerKind.PaletteMask,
                        .LayerDdsBytes = texBytes, .LayerCacheKey = l.TexturePath,
                        .PaletteMaskChannel = 0,                      ' R — skee usa mask.r (el CPU: la = tr × color.a)
                        .R = cr, .G = cg, .B = cb, .Opacity = opa, .BlendOp = bop,
                        .DebugName = "skee-mask"})
                Case Else   ' Texture × color, alpha = alpha de la textura × opacidad.
                    If texBytes Is Nothing Then Continue For
                    outL.Add(New FaceTintLayerInput With {
                        .Kind = FaceTintLayerKind.TextureSetDiffuse,
                        .LayerDdsBytes = texBytes, .LayerCacheKey = l.TexturePath,
                        .MultiplyTextureByColor = True,
                        .R = cr, .G = cg, .B = cb, .Opacity = opa, .BlendOp = bop, .IsTextureSet = True,
                        .DebugName = "skee-tex"})
            End Select
        Next
        Return outL
    End Function

    ''' <summary>Overlays <c>Face [Ovl]</c> → capas del compositor (texture × tint, alpha-over). Mismo orden que el
    ''' CPU (<see cref="SseOverlayCompositor.SortFaceOverlays"/>).</summary>
    Private Function BuildFaceOverlayGpuLayers(overlays As IList(Of RaceMenuJslot.JslotOverlayNode)) As List(Of FaceTintLayerInput)
        Dim outL As New List(Of FaceTintLayerInput)
        If overlays Is Nothing Then Return outL
        ' ⛔ FILTRAR POR NODO Face, no sólo por "tiene diffuse". Esto FALTABA: el filtro era únicamente
        ' `Not IsNullOrEmpty(o.DiffusePath)`, así que este camino (GPU) componía los overlays de CUERPO dentro
        ' del diffuse de la CARA — mientras el camino CPU (ComposeFaceOverlaysIntoDiffuse) sí filtraba por Face.
        ' Los dos caminos tienen que dar el MISMO resultado: mismo predicado, una sola ley.
        Dim ordered = SseOverlayCompositor.SortFaceOverlays(
            overlays.Where(Function(o) SseOverlayCompositor.IsFaceOverlay(o) AndAlso
                                       Not String.IsNullOrEmpty(o.DiffusePath)).ToList())
        For Each ov In ordered
            Dim texBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(ov.DiffusePath))
            If texBytes Is Nothing Then Continue For
            outL.Add(New FaceTintLayerInput With {
                .Kind = FaceTintLayerKind.TextureSetDiffuse,
                .LayerDdsBytes = texBytes, .LayerCacheKey = ov.DiffusePath,
                .MultiplyTextureByColor = ov.HasTint,
                .R = ClampByte(ov.TintR), .G = ClampByte(ov.TintG), .B = ClampByte(ov.TintB),
                .Opacity = CSng(If(ov.HasAlpha, ov.Alpha, 1.0)),
                .BlendOp = 0, .IsTextureSet = True,
                .DebugName = "face-ovl"})
        Next
        Return outL
    End Function

    ''' <summary>RMS por canal entre dos acumuladores (en unidades de 0..255). Es la MEDIDA de paridad CPU vs GPU:
    ''' la usa el caller para loguearla en vez de suponerla.</summary>
    Friend Function RmsDiff255(a As Double(), b As Double(), npix As Integer) As Double
        If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return Double.NaN
        Dim s As Double = 0
        For i = 0 To npix - 1
            For ch = 0 To 2
                Dim d = (a(i * 4 + ch) - b(i * 4 + ch)) * 255.0
                s += d * d
            Next
        Next
        Return Math.Sqrt(s / (npix * 3.0))
    End Function

    Private Function ClampByte(v As Double) As Byte
        Return CByte(Math.Max(0.0, Math.Min(255.0, Math.Round(v * 255.0))))
    End Function

    Private _white1x1 As Byte()

    ''' <summary>DDS 1×1 blanco opaco: la máscara de las capas SOLID (ver <see cref="BuildSkeeGpuLayers"/>). No se lee
    ''' su color (ForceUniformColor); sólo su alpha=1, que es la cobertura plena del solid.</summary>
    Private Function OpaqueWhite1x1Dds() As Byte()
        If _white1x1 Is Nothing Then
            _white1x1 = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                1, 1, New Byte() {255, 255, 255, 255},
                DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm, generateMipMaps:=False)
        End If
        Return _white1x1
    End Function

    ''' <summary>Sube un acumulador Double RGBA como textura Rgba32f (float). ⭐ NO se cuantiza a 8 bits: si el base
    ''' del GPU entrara en bytes, el camino GPU arrastraría un redondeo que el CPU no tiene y la paridad quedaría
    ''' limitada por el TRANSPORTE en vez de por el compose (que es lo que se quiere medir).</summary>
    Private Function UploadRgba32f(acc As Double(), npix As Integer, w As Integer, h As Integer) As Integer
        Dim f(npix * 4 - 1) As Single
        For i = 0 To npix * 4 - 1
            f(i) = CSng(acc(i))
        Next
        Dim id = GL.GenTexture()
        If id = 0 Then Return 0
        GL.BindTexture(TextureTarget.Texture2D, id)
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba32f, w, h, 0, PixelFormat.Rgba, PixelType.Float, f)
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, CInt(TextureMinFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, CInt(TextureMagFilter.Linear))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, CInt(TextureWrapMode.ClampToEdge))
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, CInt(TextureWrapMode.ClampToEdge))
        GL.BindTexture(TextureTarget.Texture2D, 0)
        Return id
    End Function

    ''' <summary>Readback float del acumulador del compositor (Rgba32f) → Double RGBA, sin pasar por 8 bits.</summary>
    Private Function ReadbackRgba32f(texId As Integer, npix As Integer) As Double()
        Dim f(npix * 4 - 1) As Single
        GL.BindTexture(TextureTarget.Texture2D, texId)
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(f, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        GL.BindTexture(TextureTarget.Texture2D, 0)
        Dim d(npix * 4 - 1) As Double
        For i = 0 To npix * 4 - 1
            d(i) = f(i)
        Next
        Return d
    End Function

End Module
