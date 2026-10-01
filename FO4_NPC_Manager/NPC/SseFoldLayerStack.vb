Imports System.Linq
Imports FO4_Base_Library
Imports OpenTK.Graphics.OpenGL4

''' <summary>
''' SSE — la cadena del diffuse plegado (facetint → pliegue → capas skee MASKT + <c>Face [Ovl]</c> → inversa), con
''' las DOS réplicas: CPU (<see cref="ComposeCpu"/> + <see cref="SseFaceGenBaker"/>) y GPU
''' (<see cref="ComposeFoldedGpuResident"/>). El caller elige por el flag de cámara (<c>Setting_GPUSkinning</c>) y
''' NADA más cambia; la paridad se mide restando los dos resultados (sandbox).
'''
''' El PLIEGUE en sí (<c>albedo = softlight(complexion, facetint) × amplify(detail)</c>) NO es un stack de capas: es
''' la ley del engine, sin blend-ops ni cobertura. En CPU es <see cref="SseFaceGenBaker.FoldFacetintIntoDiffuse"/>; en
''' GPU, <see cref="FaceTintCompositor.ApplySseFoldPass"/> (pase propio). Lo que SÍ es un stack de capas son las
''' MASKT y los overlays (etapa Overlay).
'''
''' ESPACIOS: los del bucket Fold (<see cref="SseFaceGenBaker.FoldSpaces"/>). El albedo plegado se entrega a la etapa
''' Overlay y se recibe de vuelta por el MISMO par de conversiones en los dos caminos
''' (<see cref="SseFaceGenBaker.FoldedToOverlayBase"/> / <see cref="SseFaceGenBaker.OverlayBaseToFolded"/>).
'''
''' Ley CPU de las capas = <see cref="SseOverlayCompositor.ApplyOverlays"/> (decodificada del .fx de RaceMenu).
''' Ley GPU = el MISMO <see cref="FaceTintCompositor"/> que usa FO4, con las capas mapeadas 1:1:
'''   skee type 1 (Mask; el ÚNICO que producen las MASKT del NIF) → PaletteMask, canal R, color = MASKC,
'''                                                                 opacidad = MASKA, blend = normal ⇒ alpha-over
'''   skee type 2 (Solid)                                        → UniformColor (cobertura 1)
'''   skee type 0 (Texture × color)                              → TextureSetDiffuse + MultiplyTextureByColor
''' El blend-op sale de <see cref="SseOverlayCompositor.BlendOpFromSseMode"/> — la MISMA función que usa el CPU —,
''' así que los dos caminos no se pueden desincronizar por el mapeo.
''' Los overlays <c>Face [Ovl]</c> NO son capas de esa etapa: son la capa SkinTint que instala skee (técnica 5), y
''' se componen DESPUÉS, sobre el albedo plegado ya devuelto a <c>Fold.OutputSpace</c> — CPU
''' <see cref="SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse"/>, GPU
''' <see cref="FaceTintCompositor.ApplySseFaceOverlayPass"/>. El bucket Overlay no los gobierna: el motor no tiene
''' opción (decisión del usuario 2026-10-01).
''' </summary>
Friend Module SseFoldLayerStack

    ''' <summary>RENDER PURO GPU (pedido explícito del usuario): la cadena ENTERA del pliegue —
    ''' facetint → fold → capas (skee MASKT + Face [Ovl]) → unfold → resample— corre en GL encadenando
    ''' TEXTURAS (Rgba32f, float de punta a punta), con CERO readbacks en el camino caliente. Devuelve el
    ''' texture-id FINAL o 0 si CUALQUIER etapa falla (el caller aborta con log: SIN fallback a CPU, como siempre).
    ''' <para>ESPACIOS: los del bucket Fold (<see cref="SseFaceGenBaker.ResolveFoldSpaces"/>). El complexion entra en
    ''' <c>Fold.SrcSpace</c> y la textura devuelta SALE EN EL MISMO ESPACIO: es el diffuse pre-compensado que
    ''' reemplaza al complexion, así que se samplea exactamente como él (y es lo que el bake escribe en el DDS).
    ''' No hay espacio de salida que elija el caller.</para>
    '''
    ''' "Dan lo mismo" es requisito de RESULTADO, no de representación: la réplica CPU produce lo mismo por
    ''' construcción (misma ley, mismos resolvers, mismos inputs decodificados una vez) y se VERIFICA con el
    ''' sandbox de paridad (<paramref name="measureParity"/>) — el ÚNICO lugar donde este camino hace readbacks,
    ''' además de los stats del log cuando <c>Logger.Enabled</c> (diagnóstico opt-in, no camino caliente).
    '''
    ''' Diferencias de representación ASUMIDAS (documentadas, no bugs): float32 (GPU) vs float32/Double (CPU).
    ''' Qué queda en CPU (y por qué es legítimo, no impureza): el DECODE de los DDS fuente — es la ENTRADA
    ''' común a los dos caminos (leer el archivo no es compose) y garantiza inputs bit-idénticos: decodificar
    ''' BCn por hardware tiene tolerancias de spec ⇒ rompería el "dan lo mismo" EN EL ORIGEN.
    ''' El alpha del complexion VIAJA INTACTO por las cuatro etapas: con una cabeza ALPHA-TEST, pisarlo con 1
    ''' apaga su recorte y aparece geometría que el alpha cortaba, con borde negro. Por eso el upload es
    ''' forceOpaque:=False, los pases del pliegue copian el alpha de su entrada, y la etapa de capas va con
    ''' headDiffuseAlphaTest:=True — igual que el CPU, que no toca el alpha en ninguna etapa.</summary>
    Friend Function ComposeFoldedGpuResident(complexion As Single(),
                                             tintLayers As IList(Of FaceTintLayerInput),
                                             detailRaw As Single(),
                                             skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                                             faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                                             skinRgb As Double(),
                                             w As Integer, h As Integer,
                                             host As NpcRenderHost,
                                             measureParity As Boolean,
                                             Optional outW As Integer = 0, Optional outH As Integer = 0) As Integer
        If host Is Nothing OrElse complexion Is Nothing OrElse w <= 0 OrElse h <= 0 Then Return 0
        ' Tamaño de SALIDA (CharGen Options). 0/omitido = nativo.
        ' TODA la cadena (facetint, fold, capas, unfold) sigue corriendo a la resolución NATIVA del complexion:
        ' el resample es lo ÚLTIMO, igual que en el bake. Bajarlo antes cambiaría el resultado del pliegue.
        If outW <= 0 Then outW = w
        If outH <= 0 Then outH = h
        Dim npix = w * h
        ' Los espacios del pliegue, del MISMO resolver que usan el CPU y ApplySseFoldPass.
        Dim sp = SseFaceGenBaker.ResolveFoldSpaces()
        Dim seedTex = 0, tintTex = 0, complexTex = 0, detTex = 0, foldedTex = 0, layeredTex = 0, unfoldTex = 0
        Try
            ' --- 1. FACETINT: seed de LA LEY → capas de tint del RACE/NPC (si hay). Sin capas, el facetint
            ' ES el seed (raza sin tints = seed plano; con el default 0.5 eso es soft-light IDENTIDAD, NO es
            ' un fallo — 0.5 es además el default de engine del slot 6, DefaultGreyMap). ---
            ' EL SEED SALE DE CharGen Options, NO DE UN LITERAL. Fuente única = SseFaceTintComposer.TryGetFlatSeedRgb.
            Dim seedRgb = SseFaceTintComposer.TryGetFlatSeedRgb()
            If seedRgb Is Nothing Then
                ' Espejo EXACTO del CPU: sin seed constante no hay base de donde sembrar (facetint TINT-ONLY)
                ' y ComposeLinearRgba devuelve Nothing ⇒ se aborta con log en vez de taparlo con 0.5.
                Logger.LogLazy(Function() "[SSE-FOLD] ABORT: la ley pide seed desde textura base y el facetint es TINT-ONLY (no hay base). Igual que el camino CPU.")
                Return 0
            End If
            seedTex = UploadRgba32fFlat(seedRgb(0), seedRgb(1), seedRgb(2), 1.0F, w, h)
            If seedTex = 0 Then Return 0
            If tintLayers Is Nothing OrElse tintLayers.Count = 0 Then
                tintTex = seedTex : seedTex = 0                          ' ownership pasa a tintTex
            Else
                ' CAPACIDAD DEL ESPEJO CPU de este stack: SseFaceTintComposer.AccumSpaceCapability, la misma que
                ' declara su contraparte CPU (ComposeChannelAccum). NO es un If por nombre de juego.
                ' baseDiffuseSpace = el OutputSpace del canal: es el espacio en que el CPU EXPRESA el seed constante
                ' (Cvt1(seed, outSp, accSp)); la pipeline lo siembra desde ahí y cierra en ese mismo OutputSpace,
                ' que es donde queda el facetint en los dos caminos (sp.Tint). Antes iba "lineal porque el render
                ' decodeó" (baseDiffuseIsLinearOnGpu), que es el contrato de FO4 y no el de este stack.
                Dim prT = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                                   seedTex, 0, 0, w, h, tintLayers,
                                                                   New List(Of FaceRegionSwapInput)(),
                                                                   SseFaceTintComposer.AccumSpaceCapability,
                                                                   baseDiffuseSpace:=sp.Tint)
                If prT Is Nothing OrElse prT.Diffuse Is Nothing OrElse Not prT.Diffuse.IsFresh Then Return 0
                tintTex = prT.Diffuse.TextureId
                Try : GL.DeleteTexture(seedTex) : Catch : End Try
                seedTex = 0
            End If

            ' Stats de entrada — SOLO si alguien mira el log (el readback del facetint es diagnóstico).
            If Logger.Enabled Then
                Dim mC = MeanRgb(complexion, npix)
                Dim tintAcc = ReadbackRgba32f(tintTex, npix)
                Dim mF = If(tintAcc IsNot Nothing, MeanRgb(tintAcc, npix), Nothing)
                Dim mD = If(detailRaw IsNot Nothing, MeanRgb(detailRaw, npix), Nothing)
                Dim dMeanR = If(mD Is Nothing, SseFaceGenBaker.EngineDefaultDetail, mD(0))
                Logger.LogLazy(Function() $"[SSE-FOLD] IN (GPU-resident): complexion=({mC(0):F3},{mC(1):F3},{mC(2):F3}) " &
                                          If(mF Is Nothing, "facetint=(sin readback) ",
                                             $"facetint=({mF(0):F3},{mF(1):F3},{mF(2):F3}) ") &
                                          $"detail={If(mD Is Nothing, "NINGUNO(default engine)", $"({mD(0):F3},{mD(1):F3},{mD(2):F3})")} " &
                                          $"⇒ amp(detail)≈{SseFaceGenBaker.FgTintChannel(dMeanR, 0):F3} " &
                                          $"spaces(src,work,out,tint)=({sp.Src},{sp.Working},{sp.Output},{sp.Tint})")
            End If

            ' --- 2. FOLD: pase PROPIO (ApplySseFoldPass), espejo de SseFaceGenBaker.FoldFacetintIntoDiffuse. ---
            ' forceOpaque:=False — el alpha del complexion NO se pisa (ver el summary).
            complexTex = UploadRgba32f(complexion, npix, w, h, forceOpaque:=False)
            If complexTex = 0 Then Return 0
            If detailRaw IsNot Nothing Then
                detTex = UploadRgba32f(detailRaw, npix, w, h)
                If detTex = 0 Then Return 0
            End If
            foldedTex = FaceTintCompositor.ApplySseFoldPass(host.CompositorState, complexTex, tintTex, detTex, w, h, unfold:=False)
            If foldedTex = 0 Then Return 0
            ' Sólo el complexion queda consumido acá. tintTex/detTex SIGUEN VIVOS: los vuelve a necesitar el
            ' pase de UNFOLD del final (la inversa usa el MISMO facetint y el MISMO detail que el fold, o no
            ' cancela). Se liberan después de ese pase.
            Try : GL.DeleteTexture(complexTex) : Catch : End Try
            complexTex = 0

            ' Replica CPU del sandbox de paridad: vive FUERA del If de capas para que la comparacion final
            ' cubra TAMBIEN el unfold del paso 4.
            Dim accCpu As Single() = Nothing
            ' El facetint de la RÉPLICA CPU. Lo necesitan las DOS puntas de la cadena: la directa (fold) y la
            ' inversa (unfold). Tienen que ser EL MISMO buffer o la inversa no cancela.
            Dim facetintCpu As Single() = Nothing

            ' --- 3. CAPAS SOBRE el base plegado — mismo orden que el CPU/bake: (a) skee MASKT en la etapa Overlay,
            ' (b) Face [Ovl] en su pase propio sobre Fold.OutputSpace. ---
            Dim stackLayers As New List(Of FaceTintLayerInput)
            Dim faceLayers As New List(Of FaceTintCompositor.SseFaceOverlayGpuLayer)
            If HasWork(skeeRaw, faceOvl) Then
                stackLayers.AddRange(BuildSkeeGpuLayers(skeeRaw, skinRgb))
                faceLayers.AddRange(BuildFaceOverlayGpuLayers(faceOvl))
                ' Sin `Return 0` cuando no se pudo armar ninguna capa: tiraría el fold ya pagado y el render lo
                ' reintentaría en cada refresh, y el CPU (ComposeCpu) en el mismo caso compone cero capas y sigue.
                ' Se REPORTA (los composers ya loguean [SSE-OVL]/[SSE-SKEE] la textura que no pudieron leer).
                If stackLayers.Count = 0 AndAlso faceLayers.Count = 0 Then
                    Dim nSk = If(skeeRaw Is Nothing, 0, skeeRaw.Count), nOv = If(faceOvl Is Nothing, 0, faceOvl.Count)
                    Logger.LogLazy(Function() $"[SSE-FOLD] 0 capas GPU armadas de skee={nSk} ovl={nOv} (texturas ausentes/ilegibles) — se conserva el fold de la BASE, sin capas. Ver [SSE-OVL]/[SSE-SKEE].")
                End If
            End If

            ' SANDBOX (opt-in): el UNICO readback del camino — aca se MIDE que las dos replicas dan lo mismo.
            ' FUERA del If de capas: un NPC vanilla de SSE no trae capas y es el 100% del corpus vanilla.
            ' LA RÉPLICA CPU SE CONSTRUYE DE LAS MISMAS ENTRADAS, NO DEL RESULTADO DEL GPU: compose del facetint,
            ' pliegue, capas e inversa, todos medidos.
            If measureParity Then
                ' 1. FACETINT por el compositor CPU COMPARTIDO: la MISMA llamada que hace
                '    SseFaceTintComposer.ComposeLinearRgba (seed de la ley + las mismas capas + acumulador).
                Dim accF = FaceTintCpuCompositor.ComposeChannelAccum(
                    SseFaceTintComposer.BuildSeedSpec(), w, h, FaceTintChannel.Diffuse,
                    tintLayers, Nothing, Nothing, FaceTintCpuCompositor.FaceTintAlphaPolicy.Opaque)
                facetintCpu = If(accF Is Nothing, Nothing, FaceTintCpuCompositor.AccumToRgbaAos(accF))
                If facetintCpu Is Nothing Then
                    Logger.LogLazy(Function() "[SSE-FOLD] PARITY: la réplica CPU del facetint salió Nothing ⇒ esta imagen NO se compara (no se reporta paridad falsa).")
                Else
                    ' 2. FOLD por CPU sobre una COPIA del complexion (in-place: el buffer del caller no se toca).
                    accCpu = CType(complexion.Clone(), Single())
                    SseFaceGenBaker.FoldFacetintIntoDiffuse(accCpu, facetintCpu, npix, detailRaw)
                    ' 3. CAPAS (skee MASKT + Face [Ovl]) por CPU.
                    ComposeCpu(accCpu, skeeRaw, faceOvl, skinRgb, w, h)
                End If
            End If
            ' La etapa se gatea por HasWork, IGUAL que ComposeCpu: con trabajo declarado corre aunque ninguna capa
            ' se haya podido armar, porque el seed y el pase final de la pipeline hacen el MISMO par de conversiones
            ' que el CPU hace en ese caso.
            If HasWork(skeeRaw, faceOvl) Then
                ' stage:=Overlay — el stack resuelve el bucket OVERLAY, igual que su espejo CPU
                ' (SseOverlayCompositor.ApplyOverlays).
                ' baseDiffuseSpace:=Fold.OutputSpace — la base ES el albedo plegado, en el espacio que declara el
                ' bucket Fold; la pipeline la siembra de ahí a su acumulador (= SseFaceGenBaker.FoldedToOverlayBase
                ' en el CPU) y cierra en el OutputSpace del canal.
                ' headDiffuseAlphaTest:=True — el alpha de la base pasa INTACTO, como en el CPU. Sin esto la última
                ' capa dejaba el alpha opaco y una cabeza alpha-test con capas perdía su recorte en GPU.
                Dim prL = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                                   foldedTex, 0, 0, w, h, stackLayers,
                                                                   New List(Of FaceRegionSwapInput)(),
                                                                   SseFaceTintComposer.AccumSpaceCapability,
                                                                   headDiffuseAlphaTest:=True,
                                                                   stage:=FaceTintConvention.FaceTintStage.Overlay,
                                                                   baseDiffuseSpace:=sp.Output)
                If prL Is Nothing OrElse prL.Diffuse Is Nothing OrElse prL.SpaceConversionFailed Then Return 0
                If prL.Diffuse.IsFresh Then
                    layeredTex = prL.Diffuse.TextureId
                    Try : GL.DeleteTexture(foldedTex) : Catch : End Try
                Else
                    layeredTex = foldedTex                   ' pipeline sin nada que dibujar: devolvió la base tal cual
                End If
                foldedTex = 0
                ' La pipeline cerró en el OutputSpace del canal (sp.Tint); la inversa lee en Fold.OutputSpace. Es el
                ' segundo tramo de SseFaceGenBaker.OverlayBaseToFolded en el CPU.
                If sp.Tint <> sp.Output Then
                    Dim back = FaceTintCompositor.ConvertTextureSpace(host.CompositorState, layeredTex, w, h, sp.Tint, sp.Output)
                    If back = 0 Then Return 0
                    Try : GL.DeleteTexture(layeredTex) : Catch : End Try
                    layeredTex = back
                End If
                ' (b) Face [Ovl]: la capa SkinTint de skee, sobre el albedo plegado (= el CPU después de
                ' OverlayBaseToFolded). Sin capas dibujables devuelve 0 y la base queda.
                If faceLayers.Count > 0 Then
                    Dim pf = FaceTintCompositor.ApplySseFaceOverlayPass(host.CompositorState, host.TintGpuCache,
                                                                        layeredTex, w, h, faceLayers)
                    If pf.Failed Then Return 0
                    If pf.TextureId <> 0 Then
                        Try : GL.DeleteTexture(layeredTex) : Catch : End Try
                        layeredTex = pf.TextureId
                    End If
                End If
            Else
                layeredTex = foldedTex : foldedTex = 0
            End If

            ' --- 4. UNFOLD: invertir la cadena del engine sobre el resultado (base plegada + capas). Los slots 3
            ' y 6 del material NO se neutralizan, así que el shader del preview (y el del juego) van a aplicar
            ' softlight(.,facetint) × amplify(detail) encima; esto lo cancela de antemano y el resultado dibujado
            ' vuelve a ser exactamente el buffer compuesto. MISMO facetint y MISMO detail que el fold.
            ' Espejo GPU de SseFaceGenBaker.PreCompensateEngineChain. ---
            ' CENSO PRE-UNFOLD: separa la inversa (mal condicionada cerca de k = 1-2b = 0) del resto.
            If accCpu IsNot Nothing Then
                Dim preGpu = ReadbackRgba32f(layeredTex, npix)
                If preGpu IsNot Nothing Then NoteSseParityPre(accCpu, preGpu, npix)
            End If
            unfoldTex = FaceTintCompositor.ApplySseFoldPass(host.CompositorState, layeredTex, tintTex, detTex, w, h, unfold:=True)
            If unfoldTex = 0 Then Return 0
            Try : GL.DeleteTexture(layeredTex) : Catch : End Try
            layeredTex = 0
            ' Paridad CPU-vs-GPU de la cadena COMPLETA (facetint + fold + capas + unfold). La inversa del CPU usa
            ' SU PROPIO facetint (el que compuso en el paso 1), no un readback del GPU.
            If accCpu IsNot Nothing Then
                SseFaceGenBaker.PreCompensateEngineChain(accCpu, facetintCpu, detailRaw, npix)
                Dim accGpu = ReadbackRgba32f(unfoldTex, npix)
                If accGpu IsNot Nothing Then
                    Dim rms = RmsDiff255(accCpu, accGpu, npix)
                    Logger.LogLazy(Function() $"[SSE-FOLD] PARITY (sandbox): rmsCPUvsGPU={rms:F3}/255 (facetint + fold + capas + unfold)")
                    NoteSseParity(accCpu, accGpu, npix)
                End If
            End If
            For Each t In {tintTex, detTex}
                If t <> 0 Then Try : GL.DeleteTexture(t) : Catch : End Try
            Next
            tintTex = 0 : detTex = 0

            ' Stats de salida — mismo gate: readback solo con el log encendido.
            If Logger.Enabled Then
                Dim outAcc = ReadbackRgba32f(unfoldTex, npix)
                If outAcc IsNot Nothing Then
                    Dim mO = MeanRgb(outAcc, npix)
                    Dim nSkeeL = If(skeeRaw Is Nothing, 0, skeeRaw.Count)
                    Dim nOvlL = If(faceOvl Is Nothing, 0, faceOvl.Count)
                    Logger.LogLazy(Function() $"[SSE-FOLD] OUT (GPU-resident): diffuse pre-compensado=({mO(0):F3},{mO(1):F3},{mO(2):F3}) " &
                                              $"skeeLayers={nSkeeL} faceOverlays={nOvlL}")
                End If
            End If

            ' --- 5. RESAMPLE al tamaño de CharGen Options, si difiere. ---
            ' ConvertTextureSpace con origen = destino = Fold.SrcSpace: cvt cortocircuita y el pase es SÓLO el
            ' muestreo a outW×outH, con el MISMO bilineal (fetchAt) que replican FaceTintCpuCompositor.ResampleBgra
            ' (bake) y ResampleRgbaFloat (réplica CPU). Se resamplea sobre los valores en el espacio en que se
            ' almacenan —el del archivo—, que es el orden del bake.
            If outW = w AndAlso outH = h Then
                Dim res = unfoldTex : unfoldTex = 0
                Return res
            End If
            Dim outTex = FaceTintCompositor.ConvertTextureSpace(host.CompositorState, unfoldTex, outW, outH, sp.Src, sp.Src)
            If outTex = 0 Then Return 0
            Return outTex
        Finally
            ' Limpieza de intermedios que quedaron vivos (caminos de fallo). El id devuelto nunca está acá.
            For Each t In {seedTex, tintTex, complexTex, detTex, foldedTex, layeredTex, unfoldTex}
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

    ''' <summary>True si hay algo que componer (evita subir texturas al pedo).
    '''
    ''' <para>EL PREDICADO TIENE QUE SER EL MISMO QUE CONSUMEN LOS BUILDERS, o esto se vuelve un
    ''' "gate dice sí / compose no hace nada" con consecuencias caras. Decía <c>faceOvl.Count &gt; 0</c>, que
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

    ''' <summary>CPU: compone las capas sobre <paramref name="acc"/> (in place), que llega y sale en
    ''' <c>Fold.OutputSpace</c> — el albedo plegado. Réplica exacta de skee. Las MASKT componen en el espacio del
    ''' acumulador de la etapa Overlay (el par de conversiones de entrada/salida es el MISMO que hace el GPU); los
    ''' <c>Face [Ovl]</c> componen DESPUÉS, sobre el albedo plegado tal cual (= <c>ApplySseFaceOverlayPass</c>).
    ''' Sin trabajo no se convierte nada (el GPU tampoco pasa por la etapa).</summary>
    Friend Sub ComposeCpu(acc As Single(), skeeRaw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw),
                          faceOvl As IList(Of RaceMenuJslot.JslotOverlayNode),
                          skinRgb As Double(), w As Integer, h As Integer)
        If Not HasWork(skeeRaw, faceOvl) Then Return
        Dim npix = w * h
        Dim sp = SseFaceGenBaker.ResolveFoldSpaces()
        SseFaceGenBaker.FoldedToOverlayBase(acc, npix, sp)
        If skeeRaw IsNot Nothing AndAlso skeeRaw.Count > 0 Then
            Dim layers = SseSkeeMaskReader.ResolveLayersForCpu(skeeRaw, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba, skinRgb, Nothing)
            If layers.Count > 0 Then SseOverlayCompositor.ApplyOverlays(acc, layers, w, h)
        End If
        SseFaceGenBaker.OverlayBaseToFolded(acc, npix, sp)
        SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse(acc, faceOvl, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba)
    End Sub

    ''' <summary>skee raw → capas del compositor. Mapeo por TIPO (ver el resumen del módulo). El blend sale de
    ''' <see cref="SseOverlayCompositor.BlendOpFromSseMode"/> = la misma fuente que el CPU.</summary>
    Private Function BuildSkeeGpuLayers(raw As IList(Of SseSkeeMaskReader.SkeeMaskLayerRaw), skinRgb As Double()) As List(Of FaceTintLayerInput)
        Dim outL As New List(Of FaceTintLayerInput)
        If raw Is Nothing Then Return outL
        For Each l In raw
            ' Resuelve el color/sentinel con la MISMA función del CPU (no se re-implementa el ×2 del preset hair).
            ' `l.HasColor` VA SÍ O SÍ: sin él este adaptador usaría el default True y volvería a tratar un MASKC
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

    ''' <summary>Overlays <c>Face [Ovl]</c> → capas de <see cref="FaceTintCompositor.ApplySseFaceOverlayPass"/> (la capa
    ''' SkinTint de skee, alpha-over). Mismo orden y mismo filtro que el CPU
    ''' (<see cref="SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse"/>): orden de dibujo de skee, y tinte/opacidad
    ''' del MISMO resolvedor (<see cref="SseOverlayCompositor.ResolveSkinTintLayer"/>) — una capa sin plantilla o
    ''' con opacidad 0 no se arma, igual que el CPU la saltea.</summary>
    Private Function BuildFaceOverlayGpuLayers(overlays As IList(Of RaceMenuJslot.JslotOverlayNode)) As List(Of FaceTintCompositor.SseFaceOverlayGpuLayer)
        Dim outL As New List(Of FaceTintCompositor.SseFaceOverlayGpuLayer)
        If overlays Is Nothing Then Return outL
        ' FILTRAR POR NODO Face y por pool (IsFoldableFaceOverlay = Face MENOS el magic: un `Face [SOvl{n}]` no se
        ' pliega NUNCA). Mismo predicado que el CPU, que lo aplica adentro: una sola ley.
        Dim ordered = SseOverlayCompositor.SortFaceOverlays(
            overlays.Where(Function(o) SseOverlayCompositor.IsFoldableFaceOverlay(o) AndAlso
                                       Not String.IsNullOrEmpty(o.DiffusePath)).ToList())
        For Each ov In ordered
            Dim lp = SseOverlayCompositor.ResolveSkinTintLayer(ov)
            If Not lp.HasValue OrElse Not (lp.Value.Opacity > 0.0F) Then Continue For
            Dim texBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(ov.DiffusePath))
            If texBytes Is Nothing Then Continue For
            outL.Add(New FaceTintCompositor.SseFaceOverlayGpuLayer With {
                .DdsBytes = texBytes, .CacheKey = ov.DiffusePath,
                .TintR = lp.Value.TintR, .TintG = lp.Value.TintG, .TintB = lp.Value.TintB,
                .Opacity = lp.Value.Opacity,
                .DebugName = ov.NodeName})
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

    ''' <summary>Sube un acumulador Double RGBA como textura Rgba32f (float). NO se cuantiza a 8 bits: si el base
    ''' del GPU entrara en bytes, el camino GPU arrastraría un redondeo que el CPU no tiene y la paridad quedaría
    ''' limitada por el TRANSPORTE en vez de por el compose (que es lo que se quiere medir).
    ''' Friend (no Private): el camino CPU del fold (<c>ApplySseFacetintFolded</c>) sube su resultado por acá
    ''' TAMBIÉN, para que los dos caminos instalen la MISMA representación (Rgba32f) y el transporte deje de ser
    ''' una diferencia entre ellos. No volver a Private ni bajar el CPU a RGBA8.</summary>
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
        ' forceOpaque: pisa el canal alpha con 1. NINGÚN camino del pliegue del DIFFUSE lo usa ya: el alpha
        ' del complexion es dato vivo para una cabeza alpha-test y pisarlo apaga su recorte. Queda para quien
        ' suba un buffer que genuinamente no tenga alpha propio.
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
    ''' Friend (no Private): <c>ApplySseFacetint</c> siembra por acá TAMBIÉN, para que su seed sea el 0.5 EXACTO
    ''' y no el byte 128 (=0.50196) que usaba antes — el CPU siembra 0.5 exacto y la diferencia se propaga a
    ''' fgTint (2.00781 vs 2.01563). No volver a sembrar por bytes.</summary>
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

    ''' <summary>Limpia TAMBIÉN los tres campos del censo PRE-UNFOLD. Antes sólo limpiaba los `_ssp*`, así
    ''' que la línea PRE-UNFOLD acumulaba entre corridas: una segunda medición en el mismo proceso heredaba el
    ''' peor delta de la primera y no había forma de notarlo desde el reporte.</summary>
    Public Sub ResetSseParity()
        SyncLock _sspLock
            _sspSamples = 0 : _sspImages = 0 : _sspMax = 0 : _sspGe2 = 0 : _sspGe3 = 0 : _sspSumSq = 0
        End SyncLock
        SyncLock _sspPreLock
            _sspPreSamples = 0 : _sspPreMax = 0 : _sspPreGe3 = 0
        End SyncLock
    End Sub

End Module
