Imports System.Linq
Imports FO4_Base_Library
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' SSE — el STACK DE CAPAS del diffuse plegado (skee MASKT + overlays <c>Face [Ovl]</c>), con las DOS réplicas:
''' CPU (<see cref="ComposeCpu"/>) y GPU (<see cref="ComposeGpu"/>). Ambas reciben y devuelven lo MISMO — el
''' acumulador RGBA en sRGB, <c>Double()</c> de w×h×4 — así que el caller elige por el flag de cámara
''' (<c>Setting_GPUSkinning</c>) y NADA más cambia, y la paridad se mide restando los dos arrays.
'''
''' ⭐ QUÉ ENTRA ACÁ Y QUÉ NO. El PLIEGUE en sí (<c>albedo = softlight(complexion, facetint) × amplify(detail)</c>,
''' <see cref="SseFaceGenBaker.FoldFacetintIntoDiffuse"/>) NO es un stack de capas: es una ley FIJA del engine, sin
''' blend-ops ni cobertura, y se computa igual en los dos caminos (en Double, sin cuantizar). Pasarla por el GPU
''' obligaría a mandar el facetint como TEXTURA de 8 bits, y la cadena la escala hasta ×255/64 por el amplify del
''' detail ⇒ metería un error que hoy no existe. Lo que SÍ es un stack de capas — y por eso honra el flag — son
''' las MASKT y los overlays.
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

    ''' <summary>⭐⭐ RENDER PURO GPU (pedido explícito del usuario): la cadena ENTERA del pliegue —
    ''' facetint → fold → capas (skee MASKT + Face [Ovl]) → sRGB→lin final — corre en GL encadenando
    ''' TEXTURAS (Rgba32f, float de punta a punta), con CERO readbacks en el camino caliente. Devuelve el
    ''' texture-id FINAL (diffuse plegado en LINEAL, listo para el dict con IsSRGB=False) o 0 si CUALQUIER
    ''' etapa falla (el caller aborta con log: SIN fallback a CPU, como siempre).
    '''
    ''' "Dan lo mismo" es requisito de RESULTADO, no de representación: la réplica CPU produce lo mismo por
    ''' construcción (misma ley, mismos inputs decodificados una vez) y se VERIFICA con el sandbox de paridad
    ''' (<paramref name="measureParity"/>) — el ÚNICO lugar donde este camino hace readbacks, además de los
    ''' stats del log cuando <c>Logger.Enabled</c> (diagnóstico opt-in, no camino caliente).
    '''
    ''' Diferencias de representación ASUMIDAS (documentadas, no bugs):
    '''  - float32 (GPU) vs Double (CPU): la de siempre (RMS medido 0,080/255, bajo el redondeo al byte);
    '''  - el final NO se cuantiza a 8 bits: la textura instalada queda Rgba32f LINEAL (el camino CPU
    '''    cuantiza a byte al subir RGBA8) ⇒ el GPU es ≤ medio paso de byte MÁS preciso — igual que el live
    '''    de FO4, que también instala la textura float del pipeline sin bajarla.
    ''' Qué queda en CPU (y por qué es legítimo, no impureza): el DECODE de los DDS fuente — es la ENTRADA
    ''' común a los dos caminos (leer el archivo no es compose) y garantiza inputs bit-idénticos: decodificar
    ''' BCn por hardware tiene tolerancias de spec ⇒ rompería el "dan lo mismo" EN EL ORIGEN.
    ''' El alpha del complexion se fuerza OPACO en el upload (el CPU escribe alpha=255 al final: misma ley).
    '''
    ''' El trío viejo (<see cref="ComposeFacetintGpu"/>/<see cref="FoldGpu"/>/<see cref="ComposeGpu"/>, con
    ''' readback por etapa) queda SOLO para el sandbox <c>_2d</c> del bake (FaceGenBuilder), que necesita los
    ''' intermedios en CPU para escribir los .dds de comparación.</summary>
    Friend Function ComposeFoldedGpuResident(complexionSrgb As Single(),
                                             tintLayers As IList(Of FaceTintLayerInput),
                                             detailRaw As Single(),
                                             skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                                             faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                                             skinRgb As Double(),
                                             w As Integer, h As Integer,
                                             host As NpcRenderHost,
                                             measureParity As Boolean,
                                             Optional outW As Integer = 0, Optional outH As Integer = 0) As Integer
        If host Is Nothing OrElse complexionSrgb Is Nothing OrElse w <= 0 OrElse h <= 0 Then Return 0
        ' Tamaño de SALIDA (CharGen Options). 0/omitido = nativo ⇒ comportamiento previo, bit-inerte.
        ' TODA la cadena (facetint, fold, capas, unfold) sigue corriendo a la resolución NATIVA del complexion:
        ' el resample es lo ÚLTIMO, igual que en el bake. Bajarlo antes cambiaría el resultado del pliegue.
        If outW <= 0 Then outW = w
        If outH <= 0 Then outH = h
        Dim npix = w * h
        Dim seedTex = 0, tintTex = 0, complexTex = 0, detTex = 0, foldedTex = 0, srgbTex = 0
        Try
            ' --- 1. FACETINT: seed plano 0.5 → capas de tint del RACE/NPC (si hay). Sin capas, el facetint
            ' ES el seed (raza sin tints = 0.5 plano = soft-light IDENTIDAD, NO es un fallo; = ComposeFacetintGpu.
            ' 0.5 es ademas el default de engine del slot 6, DefaultGreyMap). ---
            seedTex = UploadRgba32fFlat(0.5F, 0.5F, 0.5F, 1.0F, w, h)
            If seedTex = 0 Then Return 0
            If tintLayers Is Nothing OrElse tintLayers.Count = 0 Then
                tintTex = seedTex : seedTex = 0                          ' ownership pasa a tintTex
            Else
                ' ⭐ CAPACIDAD DEL ESPEJO CPU de TODO este stack (los 7 ApplyFaceTintPipeline de este archivo).
                ' Este camino comparte el compositor GL con FO4, pero su contraparte CPU es SseFaceTintComposer,
                ' que acumula SIEMPRE en OutputSpace (lerp uniforme del motor, sin ley de cuatro espacios). Al
                ' declararlo, AccumInCompositeSpace queda inerte acá aunque el config lo prenda ⇒ el GPU no se
                ' puede apartar del CPU. Si algun dia se implementa allá, se cambia SU constante y estos 7
                ' call sites la siguen solos. NO es un If por nombre de juego: es quien espeja este camino.
                Dim prT = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                                   seedTex, 0, 0, w, h, tintLayers,
                                                                   New List(Of FaceRegionSwapInput)(),
                                                                   SseFaceTintComposer.AccumSpaceCapability,
                                                                   baseDiffuseIsLinearOnGpu:=True)
                If prT Is Nothing OrElse prT.Diffuse Is Nothing OrElse Not prT.Diffuse.IsFresh Then Return 0
                tintTex = prT.Diffuse.TextureId
                Try : GL.DeleteTexture(seedTex) : Catch : End Try
                seedTex = 0
            End If

            ' Stats de entrada — SOLO si alguien mira el log (el readback del facetint es diagnóstico).
            If Logger.Enabled Then
                Dim mC = MeanRgb(complexionSrgb, npix)
                Dim tintAcc = ReadbackRgba32f(tintTex, npix)
                Dim mF = If(tintAcc IsNot Nothing, MeanRgb(tintAcc, npix), Nothing)
                Dim mD = If(detailRaw IsNot Nothing, MeanRgb(detailRaw, npix), Nothing)
                Dim dMeanR = If(mD Is Nothing, SseFaceGenBaker.EngineDefaultDetail, mD(0))
                Logger.LogLazy(Function() $"[SSE-FOLD] IN (GPU-resident): complexion(sRGB)=({mC(0):F3},{mC(1):F3},{mC(2):F3}) " &
                                          If(mF Is Nothing, "facetint=(sin readback) ",
                                             $"facetint/softlight-b(lin)=({mF(0):F3},{mF(1):F3},{mF(2):F3}) ") &
                                          $"detail={If(mD Is Nothing, "NINGUNO(0.251=default engine)", $"({mD(0):F3},{mD(1):F3},{mD(2):F3})")} " &
                                          $"⇒ amp(detail)≈{SseFaceGenBaker.FgTintChannel(dMeanR, 0):F3}")
            End If

            ' --- 2. FOLD: base = complexion (sRGB, alpha forzado opaco), capa = rama uFgTintFold del shader
            ' (softlight con el facetint × amplify del detail: la MISMA ley fija del engine que FoldFacetintIntoDiffuse en CPU). ---
            complexTex = UploadRgba32f(complexionSrgb, npix, w, h, forceOpaque:=True)
            If complexTex = 0 Then Return 0
            If detailRaw IsNot Nothing Then
                detTex = UploadRgba32f(detailRaw, npix, w, h)
                If detTex = 0 Then Return 0
            End If
            Dim foldLayer As New List(Of FaceTintLayerInput) From {
                New FaceTintLayerInput With {
                    .Kind = FaceTintLayerKind.TextureSetDiffuse,
                    .LayerTextureId = tintTex,
                    .FoldDetailTextureId = detTex,
                    .FgTintFold = True,
                    .FgTintOffR = CSng(1.0 / 255.0), .FgTintOffG = 0F, .FgTintOffB = CSng(1.0 / 255.0),
                    .FgTintAmp = CSng(255.0 / 64.0),
                    .Opacity = 1.0F, .Slot = 0US, .IsTextureSet = True, .DebugName = "sse-fold"}}
            Dim prF = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                               complexTex, 0, 0, w, h, foldLayer,
                                                               New List(Of FaceRegionSwapInput)(),
                                                               SseFaceTintComposer.AccumSpaceCapability,
                                                               baseDiffuseIsLinearOnGpu:=True)
            If prF Is Nothing OrElse prF.Diffuse Is Nothing OrElse Not prF.Diffuse.IsFresh Then Return 0
            foldedTex = prF.Diffuse.TextureId
            ' Sólo el complexion queda consumido acá. ⛔ tintTex/detTex SIGUEN VIVOS: los vuelve a necesitar el
            ' pase de UNFOLD del final (la inversa usa el MISMO facetint y el MISMO detail que el fold, o no
            ' cancela). Se liberan después de ese pase.
            If complexTex <> 0 Then Try : GL.DeleteTexture(complexTex) : Catch : End Try
            complexTex = 0

            ' Replica CPU del sandbox de paridad: vive FUERA del If de capas para que la comparacion final
            ' cubra TAMBIEN el unfold del paso 4 (antes se comparaba antes de invertir ⇒ el paso nuevo quedaba
            ' sin medir, que es como se colo la divergencia GPU en primer lugar).
            Dim accCpu As Single() = Nothing

            ' --- 3. CAPAS (skee MASKT + Face [Ovl]) SOBRE el base plegado — mismo orden que el CPU/bake. ---
            Dim stackLayers As New List(Of FaceTintLayerInput)
            If HasWork(skeeRaw, faceOvl) Then
                stackLayers.AddRange(BuildSkeeGpuLayers(skeeRaw, skinRgb))
                stackLayers.AddRange(BuildFaceOverlayGpuLayers(faceOvl))
                ' ⛔ ACÁ HABÍA UN `Return 0` ("había trabajo pero ninguna capa se pudo armar ⇒ FALLO, no se degrada
                ' en silencio"). Se elimina por DOS razones:
                '   1. TIRABA EL FOLD ENTERO ya pagado (decode del complexion a resolución nativa + detail +
                '      facetint + pase de fold) y, como nada cachea el fallo, el render lo reintentaba en cada
                '      refresh: el preview se quedaba "cargando". Un fallo caro y cíclico es peor que el síntoma
                '      que quería evitar.
                '   2. ROMPÍA LA PARIDAD CPU/GPU: `ComposeCpu` en el mismo caso NO falla — compone cero capas y
                '      sigue. Dos caminos que deben "dar lo mismo" no pueden diferir en si abortan.
                ' El fold de la BASE es válido y correcto por sí solo; lo que falta son las capas, y eso se
                ' REPORTA (los composers ya loguean [SSE-OVL]/[SSE-SKEE] la textura que no pudieron leer).
                If stackLayers.Count = 0 Then
                    Dim nSk = If(skeeRaw Is Nothing, 0, skeeRaw.Count), nOv = If(faceOvl Is Nothing, 0, faceOvl.Count)
                    Logger.LogLazy(Function() $"[SSE-FOLD] 0 capas GPU armadas de skee={nSk} ovl={nOv} (texturas ausentes/ilegibles) — se conserva el fold de la BASE, sin capas. Ver [SSE-OVL]/[SSE-SKEE].")
                End If
            End If

            ' SANDBOX (opt-in): el UNICO readback del camino — aca se MIDE que las dos replicas dan lo mismo,
            ' en vez de suponerlo.
            ' ⭐⭐ FUERA DEL `If stackLayers.Count > 0`. Estaba ADENTRO, y un NPC vanilla de SSE no trae ni
            ' overlays de RaceMenu ni skee ⇒ cero capas ⇒ la paridad no se medía JAMAS en el caso que es el
            ' 100% del corpus vanilla (medido: 368 invocaciones del sandbox, 0 muestras comparadas). Lo que
            ' hay que comparar ahi es el FOLD DE LA BASE, que es exactamente lo que el bake escribe.
            ' `ComposeCpu` con cero capas es un no-op declarado (ver la nota de arriba), asi que sacarlo de la
            ' compuerta no cambia el resultado del caso con capas.
            If measureParity Then
                accCpu = ReadbackRgba32f(foldedTex, npix)
                If accCpu IsNot Nothing Then ComposeCpu(accCpu, skeeRaw, faceOvl, skinRgb, w, h)
            End If
            If stackLayers.Count > 0 Then
                Dim prL = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                                   foldedTex, 0, 0, w, h, stackLayers,
                                                                   New List(Of FaceRegionSwapInput)(),
                                                                   SseFaceTintComposer.AccumSpaceCapability,
                                                                   baseDiffuseIsLinearOnGpu:=True)
                If prL Is Nothing OrElse prL.Diffuse Is Nothing OrElse Not prL.Diffuse.IsFresh Then Return 0
                srgbTex = prL.Diffuse.TextureId
                Try : GL.DeleteTexture(foldedTex) : Catch : End Try
                foldedTex = 0
            Else
                srgbTex = foldedTex : foldedTex = 0
            End If

            ' --- 4. UNFOLD: invertir la cadena del engine sobre el resultado (base plegada + capas). Los slots 3
            ' y 6 del material YA NO se neutralizan, así que el shader del preview (y el del juego) van a aplicar
            ' softlight(.,facetint) × amplify(detail) encima; esto lo cancela de antemano y el resultado dibujado
            ' vuelve a ser exactamente el buffer compuesto. MISMO facetint y MISMO detail que el fold.
            ' Espejo GPU de SseFaceGenBaker.PreCompensateEngineChain — si tocás una, tocá la otra. ---
            Dim unfoldLayer As New List(Of FaceTintLayerInput) From {
                New FaceTintLayerInput With {
                    .Kind = FaceTintLayerKind.TextureSetDiffuse,
                    .LayerTextureId = tintTex,
                    .FoldDetailTextureId = detTex,
                    .FgTintFold = True, .FgTintUnfold = True,
                    .FgTintOffR = CSng(1.0 / 255.0), .FgTintOffG = 0F, .FgTintOffB = CSng(1.0 / 255.0),
                    .FgTintAmp = CSng(255.0 / 64.0),
                    .Opacity = 1.0F, .Slot = 0US, .IsTextureSet = True, .DebugName = "sse-unfold"}}
            ' ⭐ CENSO PRE-UNFOLD: separa la inversa del resto. La inversa es mal condicionada cerca de
            ' k = 1-2b = 0 (el limite es la identidad, la formula daria 0/0), asi que ahi float32 (GPU) y
            ' float64 (CPU) pueden separarse MUCHO sin que las leyes difieran. Midiendo antes y despues se
            ' sabe cuanto aporta cada tramo en vez de suponerlo.
            If accCpu IsNot Nothing Then
                Dim preGpu = ReadbackRgba32f(srgbTex, npix)
                If preGpu IsNot Nothing Then NoteSseParityPre(accCpu, preGpu, npix)
            End If
            Dim tintTexForParity = tintTex
            Dim prU = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                               srgbTex, 0, 0, w, h, unfoldLayer,
                                                               New List(Of FaceRegionSwapInput)(),
                                                               SseFaceTintComposer.AccumSpaceCapability,
                                                               baseDiffuseIsLinearOnGpu:=True)
            If prU Is Nothing OrElse prU.Diffuse Is Nothing OrElse Not prU.Diffuse.IsFresh Then Return 0
            Try : GL.DeleteTexture(srgbTex) : Catch : End Try
            srgbTex = prU.Diffuse.TextureId
            ' Paridad CPU-vs-GPU de la cadena COMPLETA (fold + capas + unfold). La replica CPU aplica la misma
            ' inversa con el MISMO facetint (readback) y el MISMO detail, y recien ahi se compara.
            If accCpu IsNot Nothing Then
                Dim tintCpu = ReadbackRgba32f(tintTexForParity, npix)
                SseFaceGenBaker.PreCompensateEngineChain(accCpu, tintCpu, detailRaw, npix)
                Dim accGpu = ReadbackRgba32f(srgbTex, npix)
                If accGpu IsNot Nothing Then
                    Dim rms = RmsDiff255(accCpu, accGpu, npix)
                    Logger.LogLazy(Function() $"[SSE-FOLD] PARITY (sandbox): rmsCPUvsGPU={rms:F3}/255 (fold + capas + unfold)")
                    ' ⛔ El RMS solo NO alcanza para afirmar "paridad +-1": un maximo de 8 en 200 pixeles se
                    ' esconde detras de un RMS de 0,2. Ademas esto salia SOLO por Logger, que en el barrido esta
                    ' APAGADO => el unico instrumento de paridad del camino SSE no reportaba nada (se veia como
                    ' "0 comparable slots"). Se acumula un censo con el MISMO criterio que usa FO4 (maximo y cola
                    ' >=3) y lo imprime el runner.
                    NoteSseParity(accCpu, accGpu, npix)
                End If
            End If
            For Each t In {tintTex, detTex}
                If t <> 0 Then Try : GL.DeleteTexture(t) : Catch : End Try
            Next
            tintTex = 0 : detTex = 0

            ' Stats de salida — mismo gate: readback solo con el log encendido.
            If Logger.Enabled Then
                Dim outAcc = ReadbackRgba32f(srgbTex, npix)
                If outAcc IsNot Nothing Then
                    Dim mO = MeanRgb(outAcc, npix)
                    Dim nSkeeL = If(skeeRaw Is Nothing, 0, skeeRaw.Count)
                    Dim nOvlL = If(faceOvl Is Nothing, 0, faceOvl.Count)
                    Logger.LogLazy(Function() $"[SSE-FOLD] OUT (GPU-resident): folded(sRGB)=({mO(0):F3},{mO(1):F3},{mO(2):F3}) " &
                                              $"skeeLayers={nSkeeL} faceOverlays={nOvlL}  (esperado ~0.35-0.45; ~1.0 = satura)")
                End If
            End If

            ' --- 5. RESAMPLE al tamaño de CharGen Options + sRGB→lin FINAL, EN UN SOLO PASE. ---
            ' El cvt (mode 2 del shader compartido, curva IEC = SseFaceGenBaker.Srgb2Lin) renderiza un quad
            ' full-screen a un FBO de outW×outH muestreando `srgbTex` con UV normalizadas. Si outW/outH son
            ' MENORES que w/h, ese muestreo YA ES el downsample bilineal — y las texturas del compositor se
            ' allocan con MinFilter/MagFilter=Linear + ClampToEdge (AllocateResultTextureAndFbo), que es
            ' EXACTAMENTE el filtro que replican FaceTintCpuCompositor.ResampleBgra (bake) y ResampleRgbaFloat
            ' (réplica CPU de esto). No hace falta un pase aparte.
            ' ⭐ EL ORDEN IMPORTA Y ES PARTE DEL CONTRATO: se resamplea SOBRE LOS VALORES sRGB y RECIÉN DESPUÉS
            ' se convierte a lineal — bilinear en sRGB ≠ bilinear en lineal. Es el mismo orden que el bake
            ' (que resamplea el BGRA sRGB antes de encodear) y el que hace la réplica CPU. Si alguna vez se
            ' mueve el resample después del cvt, CPU, GPU y bake dejan de coincidir.
            Dim linTex = FaceTintCompositor.ConvertTextureSpace(host.CompositorState, srgbTex, outW, outH, 1, 0)
            If linTex = 0 Then Return 0
            Return linTex
        Finally
            ' Limpieza de intermedios que quedaron vivos (caminos de fallo). El id devuelto nunca está acá.
            For Each t In {seedTex, tintTex, complexTex, detTex, foldedTex, srgbTex}
                If t <> 0 Then Try : GL.DeleteTexture(t) : Catch : End Try
            Next
        End Try
    End Function

    ''' <summary>Media R/G/B de un acumulador RGBA (para los stats del log). SERIAL a propósito: una suma
    ''' flotante es dependiente del orden ⇒ paralelizarla cambiaría el valor logueado. Solo corre gateada.</summary>
    Private Function MeanRgb(acc As Single(), npix As Integer) As Double()
        Dim m(2) As Double
        For i = 0 To npix - 1
            m(0) += acc(i * 4) : m(1) += acc(i * 4 + 1) : m(2) += acc(i * 4 + 2)
        Next
        m(0) /= npix : m(1) /= npix : m(2) /= npix
        Return m
    End Function

    ''' <summary>GPU: el PLIEGUE — <c>albedo = softlight(srgbToLin(complexion), facetint) × amplify(detail)</c> — réplica
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
    Friend Function FoldGpu(complexionSrgb As Single(), facetintLinear As Single(), detailRaw As Single(),
                            w As Integer, h As Integer, host As NpcRenderHost) As Single()
        If host Is Nothing OrElse complexionSrgb Is Nothing OrElse facetintLinear Is Nothing Then Return Nothing
        Dim npix = w * h
        Dim baseTex = UploadRgba32f(complexionSrgb, npix, w, h)        ' complexion en sRGB (el shader lo lineariza)
        If baseTex = 0 Then Return Nothing
        Dim tintTex As Integer = 0, detTex As Integer = 0, resId As Integer = 0
        Try
            tintTex = UploadRgba32f(facetintLinear, npix, w, h)        ' facetint CRUDO (lineal), sin cuantizar
            If tintTex = 0 Then Return Nothing
            If detailRaw IsNot Nothing Then detTex = UploadRgba32f(detailRaw, npix, w, h)   ' 0 ⇒ el shader usa b=0.2509803922 (0.251, default engine)

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
                                                              SseFaceTintComposer.AccumSpaceCapability,
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
    Friend Function ComposeFacetintGpu(tintLayers As IList(Of FaceTintLayerInput), w As Integer, h As Integer, host As NpcRenderHost) As Single()
        If host Is Nothing Then Return Nothing
        Dim npix = w * h
        Dim seed(npix * 4 - 1) As Single
        ' Seed plano, paralelo por rangos (escrituras disjuntas ⇒ bit-idéntico).
        System.Threading.Tasks.Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(0, npix),
            Sub(range)
                For i = range.Item1 To range.Item2 - 1
                    seed(i * 4) = 0.5F : seed(i * 4 + 1) = 0.5F : seed(i * 4 + 2) = 0.5F : seed(i * 4 + 3) = 1.0F
                Next
            End Sub)
        If tintLayers Is Nothing OrElse tintLayers.Count = 0 Then Return seed
        Dim baseTex = UploadRgba32f(seed, npix, w, h)
        If baseTex = 0 Then Return Nothing
        Dim resId As Integer = 0
        Try
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              baseTex, 0, 0, w, h, tintLayers, New List(Of FaceRegionSwapInput)(),
                                                              SseFaceTintComposer.AccumSpaceCapability,
                                                              baseDiffuseIsLinearOnGpu:=True)
            resId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, 0)
            If resId = 0 Then Return Nothing
            Return ReadbackRgba32f(resId, npix)
        Finally
            If resId <> 0 Then Try : GL.DeleteTexture(resId) : Catch : End Try
            Try : GL.DeleteTexture(baseTex) : Catch : End Try
        End Try
    End Function

    ''' <summary>True si hay algo que componer (evita subir texturas al pedo).
    '''
    ''' <para>⭐⭐ EL PREDICADO TIENE QUE SER EL MISMO QUE CONSUMEN LOS BUILDERS, o esto se vuelve un
    ''' "gate dice sí / compose no hace nada" con consecuencias caras. ⛔ Decía <c>faceOvl.Count &gt; 0</c>, que
    ''' era correcto SÓLO mientras el caller le pasaba una lista ya filtrada por <c>DiffusePath</c>. Al pasar a
    ''' mandar TODOS los nodos <c>Face [Ovl]</c> (para que un overlay solo-normal deje de desaparecer), un nodo
    ''' sin diffuse hacía <c>HasWork</c>=True, <see cref="BuildFaceOverlayGpuLayers"/> devolvía 0 capas, y el
    ''' <c>Return 0</c> de abajo TIRABA A LA BASURA el fold entero — después de haber pagado el decode del
    ''' complexion a resolución NATIVA (4096² con COtR), el del detail, el compose del facetint y el pase de
    ''' fold. Y como nada cachea el fallo, se repetía en CADA refresh del render: el preview se quedaba
    ''' "cargando".</para>
    '''
    ''' <para>Ahora se pregunta exactamente lo que los builders pueden consumir: una capa skee sólida (type 2,
    ''' no necesita textura) o con ruta de máscara, y un overlay de cara con diffuse y opacidad &gt; 0
    ''' (<see cref="SseOverlayCompositor.HasBakeableFaceOverlays"/> = el mismo filtro de
    ''' <see cref="BuildFaceOverlayGpuLayers"/>).</para></summary>
    Friend Function HasWork(skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                            faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode)) As Boolean
        Dim anySkee = skeeRaw IsNot Nothing AndAlso
                      skeeRaw.Any(Function(l) l.LayerType = 2 OrElse Not String.IsNullOrEmpty(l.TexturePath))
        Return anySkee OrElse SseOverlayCompositor.HasBakeableFaceOverlays(faceOvl)
    End Function

    ''' <summary>CPU: compone las capas sobre <paramref name="acc"/> (sRGB, in place). Réplica exacta de skee.</summary>
    Friend Sub ComposeCpu(acc As Single(), skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
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
    ''' decide; NUNCA se cae a CPU en silencio). No muta <paramref name="acc"/>. GL-bound: contexto activo.
    ''' ⚠️ El RENDER ya NO pasa por acá (usa <see cref="ComposeFoldedGpuResident"/>, sin readbacks): este trío
    ''' con readback por etapa queda para el sandbox <c>_2d</c> del bake (FaceGenBuilder), que necesita los
    ''' intermedios en CPU para escribir los .dds de comparación.</summary>
    Friend Function ComposeGpu(acc As Single(), skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                               faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                               skinRgb As Double(), w As Integer, h As Integer, host As NpcRenderHost) As Single()
        If host Is Nothing OrElse acc Is Nothing Then Return Nothing
        Dim layers As New List(Of FaceTintLayerInput)
        layers.AddRange(BuildSkeeGpuLayers(skeeRaw, skinRgb))
        layers.AddRange(BuildFaceOverlayGpuLayers(faceOvl))
        If layers.Count = 0 Then Return Nothing

        Dim npix = w * h
        Dim baseTex = UploadRgba32f(acc, npix, w, h)
        If baseTex = 0 Then Return Nothing
        Dim outAcc As Single() = Nothing
        Try
            ' baseDiffuseIsLinearOnGpu:=True ⇒ el pipeline toma el sample del base TAL CUAL (los Rgba32f no llevan
            ' decode sRGB en el sampler). El acumulador vive en el MISMO espacio que el acc del CPU: sRGB.
            Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                              baseTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                              SseFaceTintComposer.AccumSpaceCapability,
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
            ' ⭐ `l.HasColor` VA SÍ O SÍ: sin él este adaptador usaría el default True y volvería a tratar un MASKC
            ' AUSENTE como el sentinel de pelo (0xFFFFFFFF), justo lo que el flag vino a arreglar — y el CPU, que sí
            ' lo pasa, daría OTRO color para la misma capa. Es una divergencia CPU-vs-GPU en el VALOR, invisible.
            Dim cpuLayer = SseOverlayCompositor.BuildSkeeMaskLayer(l.ColorArgb, l.Opacity, Nothing, l.LayerType, l.Blend, skinRgb, Nothing, l.HasColor)
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
    Friend Function RmsDiff255(a As Single(), b As Single(), npix As Integer) As Double
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
    ''' limitada por el TRANSPORTE en vez de por el compose (que es lo que se quiere medir).
    ''' ⭐ Friend (no Private): el camino CPU del fold (<c>ApplySseFacetintFolded</c>) sube su resultado por acá
    ''' TAMBIÉN, para que los dos caminos instalen la MISMA representación (Rgba32f) y el transporte deje de ser
    ''' una diferencia entre ellos. ⛔ No volver a Private ni bajar el CPU a RGBA8.</summary>
    Friend Function UploadRgba32f(acc As Single(), npix As Integer, w As Integer, h As Integer,
                                   Optional forceOpaque As Boolean = False) As Integer
        Dim f(npix * 4 - 1) As Single
        ' Copia elemento-a-elemento, paralela por rangos (disjunta ⇒ bit-idéntica). acc ya es Single (storage
        ' float32 del compositor); se copia a un buffer local para poder forzar alpha sin mutar el del caller.
        System.Threading.Tasks.Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(0, npix * 4),
            Sub(range)
                For i = range.Item1 To range.Item2 - 1
                    f(i) = acc(i)
                Next
            End Sub)
        ' forceOpaque: el camino GPU-residente fuerza alpha=1 EN EL UPLOAD del complexion — el pipeline
        ' propaga el alpha del prev tal cual, y el camino CPU escribe alpha=255 al final ⇒ misma ley.
        If forceOpaque Then
            System.Threading.Tasks.Parallel.ForEach(
                System.Collections.Concurrent.Partitioner.Create(0, npix),
                Sub(range)
                    For i = range.Item1 To range.Item2 - 1
                        f(i * 4 + 3) = 1.0F
                    Next
                End Sub)
        End If
        Return UploadRgba32fFromSingles(f, w, h)
    End Function

    ''' <summary>Textura Rgba32f plana de un color constante (el seed 0.5 del facetint). Sin pasar por Double.
    ''' ⭐ Friend (no Private): <c>ApplySseFacetint</c> siembra por acá TAMBIÉN, para que su seed sea el 0.5 EXACTO
    ''' y no el byte 128 (=0.50196) que usaba antes — el CPU siembra 0.5 exacto y la diferencia se propaga a
    ''' fgTint (2.00781 vs 2.01563). ⛔ No volver a sembrar por bytes.</summary>
    Friend Function UploadRgba32fFlat(r As Single, g As Single, b As Single, a As Single, w As Integer, h As Integer) As Integer
        Dim npix = w * h
        Dim f(npix * 4 - 1) As Single
        System.Threading.Tasks.Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(0, npix),
            Sub(range)
                For i = range.Item1 To range.Item2 - 1
                    f(i * 4) = r : f(i * 4 + 1) = g : f(i * 4 + 2) = b : f(i * 4 + 3) = a
                Next
            End Sub)
        Return UploadRgba32fFromSingles(f, w, h)
    End Function

    ''' <summary>Upload GL común de un buffer Single RGBA como Rgba32f (filtros/clamp = los del pipeline).</summary>
    Private Function UploadRgba32fFromSingles(f As Single(), w As Integer, h As Integer) As Integer
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

    ''' <summary>Friend: lo usa tambien el sandbox _2d del bake (FaceGenBuilder.WriteSseFacetint2dGpu) para el
    ''' UNICO readback de su cadena, la que encodea el DDS. Todo el resto del _2d corre en GPU.</summary>
    Friend Function ReadbackRgba32f(texId As Integer, npix As Integer) As Single()
        Dim f(npix * 4 - 1) As Single
        GL.BindTexture(TextureTarget.Texture2D, texId)
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(f, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            GL.GetTexImage(TextureTarget.Texture2D, 0, PixelFormat.Rgba, PixelType.Float, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        GL.BindTexture(TextureTarget.Texture2D, 0)
        Return f   ' el readback ya es Single (Rgba32f); el acumulador del compositor vive en float32
    End Function


    ' ===================================================================================================
    ' CENSO de paridad CPU-vs-GPU del camino SSE (fold + capas + unfold). Mismo criterio que el de FO4:
    ' lo que decide es el MAXIMO |delta| y la cola >=3, no el RMS.
    ' ===================================================================================================
    Private ReadOnly _sspLock As New Object()
    Private _sspSamples As Long = 0
    Private _sspImages As Long = 0
    Private _sspMax As Integer = 0
    Private _sspGe2 As Long = 0
    Private _sspGe3 As Long = 0
    Private _sspSumSq As Double = 0

    Private ReadOnly _sspPreLock As New Object()
    Private _sspPreSamples As Long = 0
    Private _sspPreMax As Integer = 0
    Private _sspPreGe3 As Long = 0

    Private Sub NoteSseParityPre(cpu As Single(), gpu As Single(), npix As Integer)
        Dim mx As Integer = 0, ge3 As Long = 0, n As Long = 0
        For i As Integer = 0 To npix - 1
            Dim b = i * 4
            For c As Integer = 0 To 2
                Dim a = CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, cpu(b + c))) * 255.0F))
                Dim g = CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, gpu(b + c))) * 255.0F))
                Dim d = Math.Abs(a - g)
                If d > mx Then mx = d
                If d >= 3 Then ge3 += 1
                n += 1
            Next
        Next
        SyncLock _sspPreLock
            _sspPreSamples += n : _sspPreGe3 += ge3
            If mx > _sspPreMax Then _sspPreMax = mx
        End SyncLock
    End Sub

    Private Sub NoteSseParity(cpu As Single(), gpu As Single(), npix As Integer)
        Dim mx As Integer = 0, ge2 As Long = 0, ge3 As Long = 0, ss As Double = 0, n As Long = 0
        For i As Integer = 0 To npix - 1
            Dim b = i * 4
            For c As Integer = 0 To 2
                Dim a = CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, cpu(b + c))) * 255.0F))
                Dim g = CInt(Math.Round(Math.Max(0.0F, Math.Min(1.0F, gpu(b + c))) * 255.0F))
                Dim d = Math.Abs(a - g)
                If d > mx Then mx = d
                If d >= 2 Then ge2 += 1
                If d >= 3 Then ge3 += 1
                ss += CDbl(d) * d
                n += 1
            Next
        Next
        SyncLock _sspLock
            _sspImages += 1 : _sspSamples += n : _sspGe2 += ge2 : _sspGe3 += ge3 : _sspSumSq += ss
            If mx > _sspMax Then _sspMax = mx
        End SyncLock
    End Sub

    Public Function SseParityReport() As String
        SyncLock _sspLock
            If _sspSamples = 0 Then
                Return "SSE CPU-vs-GPU parity (fold+layers+unfold): NOT MEASURED - the GPU sandbox produced no comparable image." &
                       vbLf & "   This run says NOTHING about the SSE GPU path."
            End If
            Dim rms = Math.Sqrt(_sspSumSq / _sspSamples)
            Dim pre = "   PRE-UNFOLD (fold+layers only): worst " & _sspPreMax & ", |delta|>=3: " & _sspPreGe3.ToString("N0") &
                      " of " & _sspPreSamples.ToString("N0") & vbLf
            Dim verdict = If(_sspMax <= 1, "   => within +-1: consistent with float32 (GPU) vs float64 (CPU).",
                             "   => " & _sspGe3 & " sample(s) differ by 3 or more: NOT explainable by precision.")
            Return "SSE CPU-vs-GPU parity (fold+layers+unfold):" & vbLf &
                   "   images compared : " & _sspImages & "   samples: " & _sspSamples.ToString("N0") & vbLf &
                   "   worst |delta|   : " & _sspMax & "   RMS: " & rms.ToString("F4") & vbLf &
                   "   |delta|>=2      : " & _sspGe2.ToString("N0") & "   |delta|>=3: " & _sspGe3.ToString("N0") & vbLf &
                   pre & verdict
        End SyncLock
    End Function

    Public Sub ResetSseParity()
        SyncLock _sspLock
            _sspSamples = 0 : _sspImages = 0 : _sspMax = 0 : _sspGe2 = 0 : _sspGe3 = 0 : _sspSumSq = 0
        End SyncLock
    End Sub

End Module
