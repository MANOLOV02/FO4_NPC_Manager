Imports System.IO
Imports System.Linq
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Graphics.OpenGL4
Imports OpenTK.Mathematics
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>
''' Build CharGen — hornea el FaceGen de un NPC (el <c>.nif</c> más sus texturas de cara) replicando
''' lo que produce el Creation Kit.
'''
''' <para>Arma el NIF desde cero: por cada HDPT de la cadena resuelta del NPC carga su
''' <c>MeshPath</c>, clona sus shapes a un shell game-aware, les aplica el material resuelto por el
''' MISMO camino que el render, los morphea y los skinnea. No parte del FaceGeom del CK.</para>
'''
''' <para>Salida según <see cref="DebugMode"/>: en release escribe los nombres canónicos (pisa el bake
''' del CK y es lo que consume el motor); en debug escribe un sandbox con sufijo <c>_2</c> al lado del
''' artefacto del CK para poder diffear.</para>
'''
''' <para>Reglas del bake, leyes por motor y estado de la paridad contra el CK:
''' <c>40-bake-reglas-comunes.md</c>, <c>40-bake-leyes-fo4.md</c>, <c>40-bake-leyes-sse.md</c>.</para>
''' </summary>
Public Module FaceGenBuilder

    ''' <summary>HDPT.PartType enum values.</summary>
    Public Const PartTypeMisc As Integer = 0
    Public Const PartTypeFace As Integer = 1
    Public Const PartTypeEyes As Integer = 2
    Public Const PartTypeHair As Integer = 3
    Public Const PartTypeFacialHair As Integer = 4
    Public Const PartTypeScar As Integer = 5
    Public Const PartTypeEyebrows As Integer = 6
    Public Const PartTypeMeatcaps As Integer = 7
    Public Const PartTypeTeeth As Integer = 8
    Public Const PartTypeHeadRear As Integer = 9

    ' (Removido: _srgbToG22Lut / BuildSrgbToG22Lut / ApplySrgbToGamma22Diffuse — el encode de storage
    '  sRGB->g22 del diffuse ahora nace en el SEED del path único (FaceTintCompositor.ApplyFaceTintPipeline,
    '  en float, sin tabla byte->byte). Ver el comentario del slot 0 en BakeFaceTextures.)

    ''' <summary>Resolve the FaceGen NIF path the engine would load for this NPC. Path layout
    ''' is "Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;origin plugin filename&gt;\&lt;FormID8hex&gt;.nif"
    ''' where origin plugin is the master that owns this FormID — high-byte of the global
    ''' FormID resolved through PluginManager.GetOriginatingPluginName (which handles ESL
    ''' FE prefix correctly via record SourcePluginName).</summary>
    Public Function ResolveFaceGenPath(npcFormID As UInteger, pluginManager As PluginManager) As String
        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then Return ""
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return FaceGenPaths.GeomNif(originPlugin, formIdLow)
    End Function

    ''' <summary>Result of a BuildCharGen run.</summary>
    Public Class BuildResult
        Public Property Success As Boolean
        ''' <summary>True when the NPC has no FaceGen-eligible head parts (non-human race, robot,
        ''' etc.) so there was nothing to bake. NOT a failure — callers should count it as a SKIP
        ''' (no .nif written). When True, Success is False.</summary>
        Public Property Skipped As Boolean
        ''' <summary>Where the .nif was written (only when Success). Empty otherwise.</summary>
        Public Property OutputPath As String = ""
        ''' <summary>One-line user-facing summary suitable for a MessageBox.</summary>
        Public Property Summary As String = ""
        Public Property ShapesKept As Integer
        Public Property ShapesDropped As Integer
        ''' <summary>FO4 face-texture bake: number of face-texture outputs (slots 0/1/7) that FAILED to
        ''' encode/write, or that had no source to bake. 0 = all good. The NIF still wrote (Success stays
        ''' True), but every missing DDS will surface as "unaccounted for" at BA2 pack time — so this count
        ''' lets the save summary show the CAUSE instead of a silent "1 OK" followed by "0/1 packed".</summary>
        Public Property TextureSlotsFailed As Integer
        ''' <summary>First texture-bake failure reason (exception type + message + slot/size/format, or the
        ''' bail reason). Representative message for the user-facing summary. Empty when TextureSlotsFailed=0.</summary>
        Public Property TextureFailureDetail As String = ""

        ''' <summary>Sueltos que ESTE bake dejo FUERA del layout por NPC y que su NIF referencia, como
        ''' rutas relativas a Data. Hoy: el clon de UV vanilla de la nuca de gul
        ''' (<c>NpcMaterialResolver.HeadRearClonedTextureRoot</c>), que vive en una raiz que se inventa
        ''' la app y que por lo tanto NINGUN mod trae.
        ''' <para>Sin esto el mod se publica con un NIF apuntando a texturas que no entrega: MEDIDO,
        ''' 24 de 2877 NIF horneados del corpus las referencian.</para>
        ''' <para>NO entran en <c>FaceGenFileSpecs</c>: esa lista sirve tambien al flujo de BORRADO, y
        ''' este activo es COMPARTIDO por todas las gules de la raza - entregarlo si, borrarlo no.</para></summary>
        Public ReadOnly Property ExtraLooseFiles As New List(Of String)

        ''' <summary>Rutas ABSOLUTAS de las texturas de cara que ESTE bake escribió, anotadas en el punto
        ''' de escritura. Es lo que hace posible barrer los restos del bake anterior DESPUÉS de hornear en
        ''' vez de antes: se borra lo que quedó y este bake no reescribió.
        ''' <para>⛔ Vive acá, en el resultado DE ESTE NPC, y no en un campo del módulo: `FaceGenBuilder`
        ''' es un Module (todo Shared) y `BuildCharGen` corre bajo Parallel.ForEach por NPC, así que un
        ''' conjunto compartido se corrompería entre hilos.</para></summary>
        Public ReadOnly Property TexturasEscritas As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ''' <summary>Las salidas de textura de cara que a ESTE bake le correspondía producir. Se marca en el
        ''' punto donde el bake DECIDE que el canal existe —el material de la cabeza declara ese path—, ANTES
        ''' de ir a buscar los bytes y ANTES de encodear: si después el archivo no aparece, no decodifica, o
        ''' falla el encode o la escritura, la salida SIGUE declarada, así que el packer la sigue exigiendo y
        ''' la falla no queda muda.
        ''' <para>⛔ NO CONFUNDIR con <see cref="TexturasEscritas"/>, y sobre todo NO reemplazar esto por
        ''' aquello. Aquél es el RESULTADO (qué archivos quedaron en disco) y sirve al barrido de restos;
        ''' éste es la DECISIÓN. Decidir con el resultado qué exige el packer convierte CUALQUIER bake
        ''' fallido en silencio: la lista sale vacía, los specs se vuelven no exigidos y el bundle commitea
        ''' sin sus texturas.</para>
        ''' <para>Vacío NO es una falla, es "no correspondía". Pasa cuando el NPC no tiene ninguna head part
        ''' de tipo Face —el gate de PartTypeFace del loop de HDPT de <c>BuildCharGen</c> nunca abre y
        ''' <c>BakeFaceTextures</c> no corre— y cuando el material de la cabeza no declara normal o
        ''' specular.</para></summary>
        Friend Property SalidasDeTexturaDeclaradas As FaceGenPaths.SalidaDeTexturaDeCara

        ''' <summary>Este resultado viene de un <c>BuildCharGen</c> COMPLETO (llegó a escribir el NIF), así
        ''' que su <see cref="SalidasDeTexturaDeclaradas"/> —vacía o no— es la buena.
        ''' <para>⛔ NO significa "corrió BakeFaceTextures": eso ya se probó y estaba mal. Un NPC sin ninguna
        ''' head part de tipo Face no llama a BakeFaceTextures, así que la bandera quedaba apagada, el packer
        ''' caía al fail-closed y seguía exigiendo las tres DDS — justo el caso que había que arreglar.</para>
        ''' <para>Existe porque el default de un enum de banderas es cero, o sea "no declaró nada", y ese
        ''' default solo sería FAIL-OPEN: un camino futuro que arme bundles sin poblar esto dejaría de exigir
        ''' las texturas de cara EN SILENCIO. Apagada, el packer exige TODO — el comportamiento previo a la
        ''' ley "requerido = declarado".</para></summary>
        Friend Property DeclaracionDeSalidasPoblada As Boolean

        ''' <summary>Las salidas de textura de cara que este bake REALMENTE escribio, por IDENTIDAD y no
        ''' por ruta. Es el RESULTADO; <see cref="SalidasDeTexturaDeclaradas"/> es la DECISION, y el packer
        ''' necesita las dos: la declaracion le dice si un AUSENTE es un error, y esto le dice si un
        ''' PRESENTE es suyo o resto de un horneado anterior.
        ''' <para>Por identidad a proposito. La primera version comparaba rutas absolutas, y las dos puntas
        ''' las arman de fuentes distintas: el bake sale de <c>BakeOutputRoot.Current()</c> -que
        ''' <c>--outdir</c> MUEVE- y el packer de su propio <c>dataDir</c>. Con las raices distintas no
        ''' coincide ningun string, y como esto vive en el camino de lo REQUERIDO el resultado era que se
        ''' caian todos los bundles de FO4 en cada Save.</para></summary>
        Friend Property SalidasDeTexturaEscritas As FaceGenPaths.SalidaDeTexturaDeCara
    End Class


    ''' <summary>Dual-mode bake toggle, DRIVEN BY THE LOGGER. ON only when
    ''' <see cref="Logger.Enabled"/> is True (diagnostic session); OFF (release) otherwise.
    ''' OFF (release): output canonical paths (<formID>.nif + _d.dds / _msn.dds / _s.dds) — pisa el
    ''' CK BA2 bake; el engine in-game usa nuestro output; texturas comprimidas BC3/BC5; sin
    ''' comparator ni dumps. ON (logger activo): output sandbox (<formID>_2.nif + _d_2.dds etc.)
    ''' alongside CK's, B8G8R8A8 sin comprimir; el comparator se dispara contra el CK BA2 baseline y
    ''' loguea <c>[BUILDCHARGEN-DIFF]</c> / <c>[FACEBAKE-TEXDIFF]</c>. Para diagnosticar contra CK,
    ''' encender el Logger (Logger.Enabled = True). Read-only a propósito: el modo debug y el logging
    ''' van juntos, así no quedan desincronizados (ver 40-bake-reglas-comunes memory).</summary>
    Public ReadOnly Property DebugMode As Boolean
        Get
            Return Logger.Enabled
        End Get
    End Property
    ''' <summary>Enciende/apaga el BAKE de texturas de cara (SSE: facetint _d + fold de overlays; FO4:
    ''' FaceCustomization D/N/S). Default True = comportamiento normal de la app. El barrido de validación
    ''' de NIF del CLI lo apaga para no componer DDS (es el costo dominante del batch).
    ''' OJO: apagarlo NO es neutro para el NIF — esas rutinas además REESCRIBEN slots del shader
    ''' (SSE: slot 6 facetint y el slot 0 plegado; FO4: slots 0/1/7). Con esto en False, esos slots
    ''' quedan como los dejó la resolución de material, así que un barrido en este modo NO valida el
    ''' slot 6 (ni el fold del slot 0). Para declarar 100% hay que correr además una pasada con DDS.</summary>
    Public Property BakeFaceTexturesEnabled As Boolean = True

    ''' <summary>Saltea el ENCODE DDS (BCn + mips) y su escritura a disco, en LOS DOS JUEGOS: FO4 (los 3 canales
    ''' D/_msn/_s de FaceCustomization) y SSE (el facetint _d). SOLO para barridos que validan el NIF
    ''' (--ssecomparebatch), donde los pixeles del DDS no se miran. Junto con
    ''' <see cref="FaceTintCpuCompositor.SkipPixelCompose"/> saca el costo per-NPC dominante del barrido FO4.
    ''' NO cambia lo que el bake escribe en el NIF: el texture-set se crea igual y los paths de los slots se
    ''' escriben igual (son deterministas: formID + plugin + sufijo), como si el encode hubiera salido bien.
    ''' El decode de los sources NO se gatea: ya esta amortizado entre NPCs por BatchDecodeCache y ademas es lo
    ''' que determina que slots existen.</summary>
    Public Property SkipDdsEncode As Boolean = False

    ''' <summary>Anota en el resultado del bake toda ruta del material que cuelgue de una raiz que
    ''' INVENTA la app -hoy solo <c>NpcMaterialResolver.HeadRearClonedTextureRoot</c>- y que por lo
    ''' tanto no trae ni el juego ni ningun mod. Es lo que la entrega tiene que llevar ademas de los
    ''' archivos por NPC de <c>FaceGenFileSpecs</c>.
    ''' <para>Normaliza con la MISMA funcion que usa el clonador (<c>CorrectTexturePath</c>), o la
    ''' comparacion falla por el prefijo "Textures\" o por el separador.</para></summary>
    Private Sub AnotarSueltoInventado(mat As FO4UnifiedMaterial_Class, result As BuildResult)
        If mat Is Nothing OrElse result Is Nothing Then Return
        For Each ruta In {mat.Diffuse_or_Base_Texture, mat.NormalTexture, mat.SmoothSpecTexture}
            If String.IsNullOrEmpty(ruta) Then Continue For
            Dim norm = FO4UnifiedMaterial_Class.CorrectTexturePath(ruta)
            If String.IsNullOrEmpty(norm) Then Continue For
            If Not norm.StartsWith(NpcMaterialResolver.HeadRearClonedTextureRoot, StringComparison.OrdinalIgnoreCase) Then Continue For
            If Not result.ExtraLooseFiles.Contains(norm, StringComparer.OrdinalIgnoreCase) Then result.ExtraLooseFiles.Add(norm)
        Next
    End Sub

    ' =========================== INSTRUMENTACION DE FASES ==============================
    ' POR QUE EXISTE: el bake se venia optimizando midiendo SOLO el tiempo total, que dice SI mejoro pero
    ' no DONDE se va el tiempo. Medido aparte: el proceso usa 7,66 de 12 hilos (63,8 %) ⇒ ~36 % del wall
    ' corre en UN hilo, y sin este desglose no hay forma de saber cual fase es.
    ' Costo: unos pocos Stopwatch.GetTimestamp por NPC (no por pixel) sobre ~1,6 s ⇒ <0,01 %. Los acumuladores
    ' son Long con Interlocked ⇒ seguro si alguna vez se paraleliza el loop de NPCs.
    ' Cada una de las cuatro fases de abajo mide una etapa real y separada (records/clone/morph-skin/...);
    ' el "other" que queda es de verdad el resto (parseo de materiales, oclusion, contadores, el shell del
    ' NIF) y no un bucket sin nombrar donde cae todo sin decir cual de esas cosas pesa.
    Public Enum BakePhase
        SourceNifParse = 0   ' GetBytes + Load_Manolo de las mallas fuente (se rehace POR NPC: loadedSources es local)
        Textures = 1         ' BakeFaceTextures (FO4 D/N/S) + WriteSseFacetintDds (SSE facetint) = el compose de pixeles
        NifWrite = 2         ' serializar + escribir el NIF de salida
        ' --- las tres que abren el "other" ---
        RecordResolve = 3    ' overlay del NPC + mapa de HDPT permitidos + BakeState + huesos del esqueleto del actor
        ShapeClone = 4       ' CloneShape_Original: copiar la shape del source al shell de salida
        MorphSkin = 5        ' BakeShape (FO4 _faceBones: skin-rebind + FMRS) + ApplyChargenMorphsInPlace (morph por vertice)
        Total = 6            ' todo BuildCharGen, para poder calcular el resto por diferencia
        Count = 7
    End Enum
    Private ReadOnly _phaseTicks(CInt(BakePhase.Count) - 1) As Long
    Private ReadOnly _phaseHits(CInt(BakePhase.Count) - 1) As Long

    ''' <summary>Suma el tiempo transcurrido desde <paramref name="t0"/> (un Stopwatch.GetTimestamp) a la fase.</summary>
    Public Sub PhaseAdd(p As BakePhase, t0 As Long)
        Threading.Interlocked.Add(_phaseTicks(CInt(p)), Stopwatch.GetTimestamp() - t0)
        Threading.Interlocked.Increment(_phaseHits(CInt(p)))
    End Sub

    Public Sub PhaseReset()
        For i = 0 To CInt(BakePhase.Count) - 1
            Threading.Interlocked.Exchange(_phaseTicks(i), 0L)
            Threading.Interlocked.Exchange(_phaseHits(i), 0L)
        Next
    End Sub

    ' ===================================================================================
    ' GATE SIMD: los self-tests de paridad, UNA sola vez por proceso, antes del primer bake.
    ' ===================================================================================
    ''' <summary>Resultado del gate, calculado UNA sola vez y de forma realmente atómica.
    ''' <para>NO usar un <c>Interlocked.CompareExchange</c> sobre un flag "ya corrió": ese patrón marca
    ''' HECHO *antes* de correr los tests, así que un segundo hilo que entre durante ese ~1 s ve el flag en 1,
    ''' el resultado todavía vacío, y <b>se va sin gate</b> — justo lo que el gate existe para impedir. Y si los
    ''' tests LANZAN (la <c>AggregateException</c> de un <c>Parallel.ForEach</c>), el flag queda en 1 con el
    ''' resultado vacío PARA SIEMPRE y todas las llamadas siguientes pasan en silencio.
    ''' <c>ExecutionAndPublication</c> da las dos cosas: una sola ejecución, y publicación segura del valor
    ''' (además de re-lanzar la misma excepción a todos los hilos si la hubo).</para></summary>
    Private ReadOnly _simdGate As New Lazy(Of String)(AddressOf SimdParityFailure,
                                                      Threading.LazyThreadSafetyMode.ExecutionAndPublication)

    ''' <summary>Corre los self-tests de paridad vector-vs-escalar la PRIMERA vez que se hornea algo, y
    ''' cachea el resultado. Idempotente y thread-safe.
    ''' <para>Si alguno falla, LANZA. No es una advertencia: si el camino vectorial no es bit-idéntico al
    ''' escalar, cada byte que se hornee a partir de ahí es basura silenciosa — y peor, distinta según la CPU.
    ''' Fallar acá cuesta un mensaje; no fallar cuesta un corpus entero mal horneado.</para>
    ''' <para>EL BARRIDO LO LLAMA UNA VEZ, ANTES DEL LOOP (BakeAllRunner). No alcanza con que el Lazy sea
    ''' thread-safe: CUATRO de los self-tests corren <c>Parallel.ForEach</c> por dentro, y con el loop de NPCs
    ''' paralelo el primer hilo que entra se queda con la publicación del Lazy mientras los demás esperan ⇒
    ''' stall de arranque (no deadlock: Parallel usa el hilo llamador como worker, así que progresa).
    ''' NO sacar la llamada de <c>BuildCharGen</c>: ése es el gate del camino de la UI, que no pasa por el
    ''' runner. Llamarlo dos veces es gratis — el Lazy ya corrió.</para></summary>
    ''' <remarks>EL MENSAJE NO PUEDE AFIRMAR EL EJE EQUIVOCADO: esta lista mezcla varios ejes (ver
    ''' <see cref="ParityAxis"/>) y el detalle que se adjunta trae el slug del test que falló. Contraejemplo
    ''' concreto: con <c>Fold.SoftLight</c> en un modelo no-default falla <c>fold-golden</c> —un GOLDEN
    ''' ABSOLUTO, no una comparación vector-vs-escalar— así que el mensaje sólo puede culpar a la CPU cuando
    ''' el eje que falló es efectivamente el vectorial.</remarks>
    Public Sub EnsureSimdParityGate()
        Dim r = _simdGate.Value
        If r.Length = 0 Then Return
        Dim slug = If(r.StartsWith("["), r.Substring(1, Math.Max(0, r.IndexOf("]"c) - 1)), "")
        ' El default es VectorVsScalar: los DOS ejes que quedan en el binario acusan a la CPU, así que un
        ' slug que no se resuelva (no debería pasar: sale de esta misma tabla) no puede nombrar un eje ajeno.
        Dim axis = ParityAxis.VectorVsScalar
        For Each t In _parityTests
            If t.Slug = slug Then axis = t.Axis : Exit For
        Next
        Dim what = "el camino vectorial NO es bit-identico al escalar ⇒ los bytes saldrian distintos segun la CPU"
        Throw New InvalidOperationException(
            $"Parity gate FAILED [{axis}] — {what}. Hornear ahora produciria bytes que no describen la ley. Detalle: " & r)
    End Sub

    ''' <summary>Eje de verificación al que pertenece cada self-test. El gate mezcla cosas que NO prueban lo
    ''' mismo, y reportarlas juntas fue el defecto: quien leía "BIT-IDENTICAL" se llevaba que el camino GPU
    ''' estaba cubierto, cuando ningún test de esta lista toca el GPU.
    ''' <para>Sólo quedan DOS ejes acá: GlslLexical, GoldenAbsolute y LawConsistency viven en Tools/ParityGate,
    ''' no en el binario. El criterio: <b>en el binario sólo viaja el test cuyo resultado DEPENDE DE LA
    ''' MÁQUINA DEL USUARIO</b>, y lo único que depende del rig ajeno es el ancho que elija <c>Vector(Of T)</c>.
    ''' Un test de ley o de léxico da lo mismo acá que allá: si falla del otro lado, fallaba también de éste.
    ''' Ver memoria 00-reglas-self-tests-no-van-en-el-binario.</para></summary>
    Public Enum ParityAxis
        ''' <summary>escalar == V128 == V256 == Vector(Of T). Corre SIEMPRE (no depende de que haya SIMD).</summary>
        ScalarVsWidths
        ''' <summary>espejo vectorial == escalar. Sólo corre con SIMD acelerado: sin él, el espejo ni se usa
        ''' y el test hace early-return devolviendo "" — que es indistinguible de "pasó".</summary>
        VectorVsScalar
    End Enum

    ''' <summary>Los DIEZ self-tests que VIAJAN CON EL BINARIO, en orden, con su eje. Es la ÚNICA lista: la
    ''' consumen tanto <see cref="SimdParityFailure"/> (que aborta el bake) como <see cref="ParityAxesReport"/>
    ''' (que declara qué se cubrió).
    ''' <para>El de ANCHOS va primero: es la base de la que dependen los demás. Si los anchos divergen, el
    ''' MISMO binario hornea caras distintas según la CPU y un gate de bytes de UNA máquina no lo vería. Ese
    ''' es, exactamente, el único motivo por el que esta lista existe dentro de una app que se distribuye.</para>
    ''' <para>Estos OCHO viven en <c>Tools/ParityGate</c>, no acá: <c>glsl-ascii</c>,
    ''' <c>fold-golden</c>, <c>accum-space</c>, <c>cache-keys</c>, <c>bilinear</c>, <c>resample-hoist</c>,
    ''' <c>qnam-face</c> y <c>softlight-inv</c>. NO es pérdida de cobertura —siguen corriendo, como gate de
    ''' BUILD— y NO pueden volver: su resultado no depende de la máquina del usuario, así que correrlos en el
    ''' binario de producción no descubre nada y cuesta ~1 s en el primer Bake. Dos de ellos además MUTABAN
    ''' globales de producción en caliente. Ver memoria 00-reglas-self-tests-no-van-en-el-binario.</para>
    ''' <para>SE FUE <c>sse-layer</c> (<c>SseFaceTintComposer.ComposeLayerSelfTest</c>) en la fase 5, y NO se
    ''' perdió cobertura: contrastaba el espejo vectorial del loop de capas PROPIO de SSE, y ese loop se BORRÓ
    ''' —SSE compone por <c>FaceTintCpuCompositor.ComposeChannelAccum</c>, igual que Fallout—. Sus ejes (ley
    ''' SSE all-linear, los cuatro canales de máscara, largos que no son múltiplo del ancho, cobertura cero)
    ''' los cubren <c>compose</c> y <c>bytepack</c>, que son los de la implementación que ahora corre. Un test
    ''' que apunta a código borrado no es cobertura: es un nombre en la lista.</para></summary>
    Private ReadOnly _parityTests As (Slug As String, Axis As ParityAxis, Run As Func(Of String))() = {
        ("widths", ParityAxis.ScalarVsWidths, AddressOf FastPow.WidthParitySelfTest),
        ("compose", ParityAxis.VectorVsScalar, AddressOf FaceTintCpuCompositor.ComposeVectorSelfTest),
        ("spaces", ParityAxis.VectorVsScalar, AddressOf FaceTintCpuCompositor.VectorPathsSelfTest),
        ("pack-double", ParityAxis.ScalarVsWidths, AddressOf FaceTintCpuCompositor.PackRoundDoubleSelfTest),
        ("bytepack", ParityAxis.VectorVsScalar, AddressOf FaceTintCpuCompositor.PhaseAVectorSelfTest),
        ("seed-swap", ParityAxis.VectorVsScalar, AddressOf FaceTintCpuCompositor.SeedAndSwapVectorSelfTest),
        ("overlays", ParityAxis.VectorVsScalar, AddressOf SseOverlayCompositor.OverlayVectorSelfTest),
        ("baker", ParityAxis.VectorVsScalar, AddressOf SseFaceGenBaker.BakerVectorSelfTest),
        ("fold-models", ParityAxis.VectorVsScalar, AddressOf SseFaceGenBaker.FoldSoftLightModelsVectorSelfTest),
        ("skin-blend", ParityAxis.VectorVsScalar, AddressOf SkinningHelper.SkinningSimdSelfTest)
    }
    ' `skin-blend`: corre la funcion REAL SkinningHelper.BlendBoneMatrices por sus dos caminos (con paleta
    ' plana ⇒ vectorial, sin paleta ⇒ escalar) y los compara bit a bit. El bake usa esa misma ley
    ' (SkinBakeMath / FaceGenBuildPipeline), asi que una divergencia ahi saldria a los vertices horneados.
    ' El comentario va ACA y no adentro del inicializador: VB no acepta una linea de comentario entre los
    ' elementos de un `From { ... }` (BC30201) — cuesta un build entero descubrirlo.

    ''' <summary>Corre los self-tests en orden. Devuelve "" si todos pasan, o el primer fallo (con su slug,
    ''' que antes se perdía: el mensaje no decía cuál de los diez había fallado).</summary>
    Public Function SimdParityFailure() As String
        For Each t In _parityTests
            Dim r = t.Run()
            If r.Length > 0 Then Return $"[{t.Slug}] {r}"
        Next
        Return ""
    End Function

    ''' <summary>Qué EJES cubrió realmente esta corrida. Existe porque un "BIT-IDENTICAL" pelado mentía por
    ''' partida doble: (a) sin SIMD acelerado los siete tests de espejo vectorial hacen early-return y el gate
    ''' pasa VACÍO, y (b) ningún test de este gate mira el camino GPU, que se mide aparte y puede no haber
    ''' corrido. Cada eje se declara por separado, y el que no corrió dice NOT RUN, no "OK".</summary>
    ''' <param name="failure">Lo que devolvió <see cref="SimdParityFailure"/>. Sin esto el reporte decía
    ''' BIT-IDENTICAL en el eje que acababa de fallar, y los ejes posteriores —que por el corto-circuito del
    ''' gate NI SIQUIERA CORRIERON— se anunciaban como verdes.</param>
    Public Function ParityAxesReport(Optional failure As String = "") As String
        Dim vectorRan = FastPow.AcceleratedV
        ' El slug viaja al principio del mensaje de fallo; se resuelve contra la MISMA tabla, así no hay
        ' una segunda lista de nombres que pueda quedar desfasada.
        Dim failIdx = -1
        If Not String.IsNullOrEmpty(failure) Then
            For k = 0 To _parityTests.Length - 1
                If failure.StartsWith("[" & _parityTests(k).Slug & "]", StringComparison.Ordinal) Then failIdx = k : Exit For
            Next
        End If
        Dim sb As New Text.StringBuilder()
        For Each ax In {ParityAxis.ScalarVsWidths, ParityAxis.VectorVsScalar}
            Dim idxs = Enumerable.Range(0, _parityTests.Length).Where(Function(k) _parityTests(k).Axis = ax).ToArray()
            Dim slugs = String.Join("/", idxs.Select(Function(k) _parityTests(k).Slug))
            Dim n = idxs.Length
            Dim state As String
            If failIdx >= 0 AndAlso idxs.Contains(failIdx) Then
                state = $"*** FAILED *** en [{_parityTests(failIdx).Slug}]"
            ElseIf failIdx >= 0 AndAlso idxs.All(Function(k) k > failIdx) Then
                state = $"NOT RUN ({n} test(s)) — el gate cortó en [{_parityTests(failIdx).Slug}]"
            ElseIf ax = ParityAxis.VectorVsScalar AndAlso Not vectorRan Then
                state = $"NOT RUN ({n} test(s)) — sin SIMD acelerado el espejo vectorial no se usa y los tests salen vacíos"
            Else
                state = $"BIT-IDENTICAL ({n} test(s))"
            End If
            sb.AppendLine($"     {ax,-16} : {state}   [{slugs}]")
        Next
        ' Los ejes que este gate NO cubre se nombran igual, para que su ausencia sea VISIBLE y no tácita.
        ' CpuVsGpu se mide horneando con FGBAKE_GPU_PARITY=1 y sale por ParityReport/SseParityReport.
        ' Los otros tres se fueron a Tools/ParityGate (no dependen de la máquina del usuario);
        ' se listan acá para que nadie lea este reporte y crea que las leyes quedaron sin gate.
        sb.AppendLine($"     {"CpuVsGpu",-16} : NOT COVERED BY THIS GATE — ver ParityReport / SseParityReport")
        sb.Append($"     {"Law/Glsl/Golden",-16} : NOT COVERED BY THIS GATE — son gate de BUILD: Tools/ParityGate")
        Return sb.ToString()
    End Function

    ''' <summary>Desglose legible. "resto" = Total − (las fases medidas): records, morphs, skinning, clonado.</summary>
    Public Function PhaseReport() As String
        Dim f = CDbl(Stopwatch.Frequency)
        Dim tot = _phaseTicks(CInt(BakePhase.Total)) / f
        If tot <= 0 Then Return "Bake phases: no data."
        Dim sb As New Text.StringBuilder()
        sb.AppendLine("Bake phases (summed over ALL NPCs; the total is accumulated CPU, not wall clock):")
        Dim medido As Double = 0
        For Each p In {BakePhase.RecordResolve, BakePhase.SourceNifParse, BakePhase.ShapeClone,
                       BakePhase.MorphSkin, BakePhase.Textures, BakePhase.NifWrite}
            Dim s = _phaseTicks(CInt(p)) / f
            medido += s
            sb.AppendLine($"   {p,-16} {s,10:F1} s  ({100.0 * s / tot,5:F1} % of total)  n={_phaseHits(CInt(p))}")
        Next
        ' Lo que queda DESPUES de abrir records/clone/morph-skin: parseo de materiales, oclusion de slots,
        ' contadores y el armado del shell. Si este renglon vuelve a ser grande, hay otra fase que nombrar.
        sb.AppendLine($"   {"other",-16} {tot - medido,10:F1} s  ({100.0 * (tot - medido) / tot,5:F1} % of total)  (materiales/oclusion/shell)")
        sb.AppendLine($"   {"TOTAL",-16} {tot,10:F1} s  n={_phaseHits(CInt(BakePhase.Total))}")
        ' Ancho SIMD efectivo + paridad del espejo vectorial. Va en el reporte porque el tiempo de arriba no
        ' se puede leer sin saber por que camino corrio: 4,05x con AVX2, 2,30x con SSE2, y los tres anchos dan
        ' los MISMOS bytes. Un MISMATCH aca invalida la corrida entera — se marca, no se degrada en silencio.
        ' El ancho REAL lo decide Vector(Of T), no la presencia de AVX2: con DOTNET_MaxVectorTBitWidth=128
        ' sobre una CPU con AVX2, decir "Vector256 (8 lanes)" contradecia al `lanes=` de la misma linea.
        Dim simd = If(FastPow.AcceleratedV, $"Vector(Of T) de {FastPow.LaneCount * 32} bits", "scalar (sin SIMD)")
        ' Los DIEZ self-tests de paridad, no sólo el del compose: cada módulo vectorizado tiene el suyo y
        ' un MISMATCH en cualquiera invalida la corrida. El de overlays importa especialmente porque el
        ' corpus VANILLA no tiene overlays de RaceMenu ⇒ un barrido A/B no los ejercita nunca; y el del pack
        ' en Double cubre el camino 4K de SSE, que redondea con OTRA ley que el byte-pack de FO4.
        ' El gate ya corrio ANTES del primer NPC (EnsureSimdParityGate). Aca sólo se REPORTA: si hubiera
        ' fallado, BuildCharGen habria lanzado y no habria bake que reportar.
        ' El veredicto va POR EJE: un "BIT-IDENTICAL" plano decia que estaba todo cubierto incluso cuando
        ' los siete tests de espejo vectorial no habian corrido, y sin nombrar nunca al eje CPU-vs-GPU.
        Dim parity = _simdGate.Value          ' YA calculado por el gate; re-correrlo eran ~1,1 s por reporte
        sb.AppendLine($"   compose SIMD path: {simd}   lanes={FastPow.LaneCount}")
        If parity.Length > 0 Then sb.AppendLine("   parity gate: *** MISMATCH *** " & parity)
        sb.AppendLine("   parity gate, by axis:")
        sb.AppendLine(ParityAxesReport(parity))
        Return sb.ToString()
    End Function
    ' ===================================================================================

    ' ===================================================================================
    ' PARIDAD CPU-vs-GPU del compose de texturas (ver RecordCpuGpuParity en BakeFaceTextures).
    ' ===================================================================================
    Private ReadOnly _parityLock As New Object()
    Private _parSlots As Long = 0            ' slots comparados (D/N/S de cada NPC)
    Private _parPixels As Long = 0           ' pixeles comparados (SUMA sobre slots) — el "n" del reporte
    Private _parExact As Long = 0            ' pixeles con los 4 bytes identicos
    Private _parSqErr As Double = 0.0        ' suma de (cpu-gpu)^2 sobre los 3 canales de color
    Private _parMaxD As Integer = 0          ' peor |delta| de un solo canal en todo el corpus
    Private _parWorst As String = ""         ' quien lo produjo
    Private _parSizeMismatch As Long = 0     ' slots descartados por tamaño distinto (NO se promedian)
    Private _parInvalid As Long = 0          ' NPCs cuya medicion NO vale (el GL no dejo la salida en OutputSpace)
    Private _parInvalidWhy As String = ""
    ''' <summary>Histograma del |delta| por pixel: indices 0..7 exactos, 8 = "8 o mas". Discrimina PRECISION
    ''' (decaimiento suave desde 0) de ERROR DE IMPLEMENTACION (cumulo o poblacion con delta grande).</summary>
    Private ReadOnly _parHist(8) As Long
    Private _parTailWithSwaps As Long = 0
    Private _parTailNoSwaps As Long = 0
    Private _parPixWithSwaps As Long = 0
    Private _parPixNoSwaps As Long = 0
    Private _parAlphaMismatch As Long = 0    ' pixeles con color igual pero ALPHA distinto (regla aparte)
    ''' <summary>Slots cuya cola (|delta| >= 3) es > 0, y los peores con su conteo. Discrimina si la cola
    ''' viene de POCOS slots (condición de dato puntual: una textura o capa concreta) o de TODOS
    ''' (divergencia sistémica de la ley) — dos causas que piden investigaciones opuestas.</summary>
    Private _parSlotsWithTail As Long = 0
    Private ReadOnly _parTailTop As New List(Of (Npc As UInteger, Slot As String, N As Long))
    ''' <summary>Ocupación 8x8 (UV normalizado) de los píxeles con |delta| >= 3. Es el discriminador que
    ''' sigue al histograma: agrupados en pocas celdas ⇒ la causa es espacial (una máscara que cada lado
    ''' resamplea distinto); repartidos parejo por toda la cara ⇒ es aritmética.</summary>
    Private ReadOnly _parGrid(63) As Long
    Private ReadOnly _parTailByChannel(2) As Long   ' 0=Diffuse(slot 0) 1=Normal(slot 1) 2=Specular(slot 7)

    ''' <summary>Marca la corrida de paridad como NO VALIDA. La llama quien detecta que el GL no pudo dejar su
    ''' salida en el espacio que el CPU asume; a partir de ahi comparar los dos buffers mide la conversion
    ''' fallida, no el compositor. Se REPORTA — no se descarta en silencio ni se promedia con lo bueno.</summary>
    Public Sub ParityInvalidate(reason As String)
        SyncLock _parityLock
            _parInvalid += 1
            If _parInvalidWhy = "" Then _parInvalidWhy = reason
        End SyncLock
        Logger.LogLazy(Function() $"[FACEBAKE-PARITY] MEDICION INVALIDADA: {reason}")
    End Sub

    ''' <summary>Resetea los acumuladores de paridad CPU-vs-GPU. Lo llama el runner al empezar un barrido.</summary>
    Public Sub ParityReset()
        SyncLock _parityLock
            _parSlots = 0 : _parPixels = 0 : _parExact = 0 : _parSqErr = 0.0
            _parMaxD = 0 : _parWorst = "" : _parSizeMismatch = 0
            _parInvalid = 0 : _parInvalidWhy = ""
            _parTailWithSwaps = 0 : _parTailNoSwaps = 0 : _parPixWithSwaps = 0 : _parPixNoSwaps = 0
        End SyncLock
    End Sub

    ''' <summary>Acumula la comparacion de UN slot entre el buffer CPU y el buffer GPU (los dos BGRA byte,
    ''' mismo tamaño). Solo los 3 canales de COLOR entran en el RMS: el alpha lo decide `keepBaseAlpha` en el
    ''' CPU y `uForceOpaqueAlpha` en el GL, que es una regla aparte y no una diferencia de compose.</summary>
    Private Sub RecordCpuGpuParity(slot As Integer, suffix As String, cpu As Byte(), gpu As Byte(),
                                   w As Integer, h As Integer, npcFormID As UInteger,
                                   nSwaps As Integer, nLayers As Integer)
        Dim n = w * h
        ' Un tamaño distinto NO se promedia con lo demas: se CUENTA aparte y se loguea. Comparar buffers de
        ' distinto largo daria un numero sin significado (o un crash), que es peor que no medir.
        If cpu.Length <> n * 4 OrElse gpu.Length <> n * 4 Then
            SyncLock _parityLock : _parSizeMismatch += 1 : End SyncLock
            Logger.LogLazy(Function() $"[FACEBAKE-PARITY] slot={slot}{suffix} npc=0x{npcFormID:X8}: BUFFERS DE DISTINTO LARGO (cpu={cpu.Length} gpu={gpu.Length} esperado={n * 4}) — NO comparado")
            Return
        End If
        Dim exact As Long = 0, sq As Double = 0.0, maxD As Integer = 0, alphaMis As Long = 0
        Dim worstX As Integer = -1, worstY As Integer = -1
        ' HISTOGRAMA DEL |delta| POR PIXEL. Es el discriminador barato entre las dos explicaciones posibles
        ' de una divergencia CPU-vs-GPU:
        '   · PRECISION acumulada (float32 del FBO vs float64 del CPU): decaimiento SUAVE desde 0, la cola
        '     se extingue rapido y no hay estructura.
        '   · ERROR DE IMPLEMENTACION (una rama de la ley que no coincide): cumulo en valores concretos, o
        '     una POBLACION de pixeles con delta grande, o concentracion espacial.
        ' Sin esto solo se ve "maxD=8", que no distingue "un pixel raro" de "media cara corrida".
        Dim hist(8) As Long   ' 0,1,2,3,4,5,6,7, 8+
        Dim grid(63) As Long  ' ocupacion 8x8 de los |delta| >= 3
        For i = 0 To n - 1
            Dim o = i * 4
            Dim d0 = CInt(cpu(o)) - CInt(gpu(o))
            Dim d1 = CInt(cpu(o + 1)) - CInt(gpu(o + 1))
            Dim d2 = CInt(cpu(o + 2)) - CInt(gpu(o + 2))
            sq += CDbl(d0 * d0 + d1 * d1 + d2 * d2)
            Dim m = Math.Max(Math.Abs(d0), Math.Max(Math.Abs(d1), Math.Abs(d2)))
            hist(Math.Min(m, 8)) += 1
            If m >= 3 Then
                ' Celda 8x8 en UV normalizado: independiente de la resolucion del canal, asi que las caras
                ' de 512 y de 1024 se agregan en la MISMA grilla y la comparacion tiene sentido.
                Dim gx = Math.Min(7, (i Mod w) * 8 \ w)
                Dim gy = Math.Min(7, (i \ w) * 8 \ h)
                grid(gy * 8 + gx) += 1
            End If
            If m > maxD Then
                maxD = m
                worstX = i Mod w : worstY = i \ w
            End If
            ' `exact` es COLOR-ONLY, coherente con el RMS (que tambien excluye el alpha). Antes exigia
            ' ademas que el alpha coincidiera, y entonces el reporte podia decir "RMS 0,0000" al lado de
            ' "identicos 0 %" — dos numeros que se contradicen — justo en el caso que la doc declara fuera
            ' de alcance (el alpha lo deciden `keepBaseAlpha` en el CPU y `uForceOpaqueAlpha` en el GL, que
            ' son reglas distintas y NO una diferencia de compose). El alpha se cuenta aparte.
            If m = 0 Then exact += 1
            If cpu(o + 3) <> gpu(o + 3) Then alphaMis += 1
        Next
        SyncLock _parityLock
            For k = 0 To 8 : _parHist(k) += hist(k) : Next
            For k = 0 To 63 : _parGrid(k) += grid(k) : Next
            Dim chIdx = If(slot = 1, 1, If(slot = 7, 2, 0))
            Dim tail As Long = 0
            For k = 3 To 8 : tail += hist(k) : Next
            _parTailByChannel(chIdx) += tail
            ' LOCALIZACION POR FASE: los region swaps son un MODO APARTE del shader (uMode=1) con su
            ' propio codigo, mientras el CPU los pasa por el MISMO ComposeOne que los tints. Si la cola
            ' vive SOLO en NPCs con swaps, la divergencia esta en esa rama; si aparece igual sin swaps,
            ' esta en el camino de tints. Es la medicion que separa las dos, en vez de proponer candidatos.
            If nSwaps > 0 Then
                _parTailWithSwaps += tail : _parPixWithSwaps += n
            Else
                _parTailNoSwaps += tail : _parPixNoSwaps += n
            End If
            _parAlphaMismatch += alphaMis
            Dim slotTail As Long = 0
            For k = 3 To 8 : slotTail += hist(k) : Next
            If slotTail > 0 Then
                _parSlotsWithTail += 1
                _parTailTop.Add((npcFormID, $"{slot}{suffix}", slotTail))
            End If
        End SyncLock
        Dim rms = Math.Sqrt(sq / (n * 3.0))
        SyncLock _parityLock
            _parSlots += 1 : _parPixels += n : _parExact += exact : _parSqErr += sq
            If maxD > _parMaxD Then
                _parMaxD = maxD
                _parWorst = $"0x{npcFormID:X8} slot {slot}{suffix} ({w}x{h})"
            End If
        End SyncLock
        Logger.LogLazy(Function() $"[FACEBAKE-PARITY] 0x{npcFormID:X8} slot={slot}{suffix} {w}x{h}: rmsCPUvsGPU={rms:F4}/255 maxD={maxD} exactos={exact}/{n} ({100.0 * exact / n:F2} %)")
    End Sub

    ''' <summary>Reporte agregado de paridad CPU-vs-GPU. SIEMPRE imprime el n (slots y pixeles comparados):
    ''' "0 diferencias" sobre 0 comparables es un instrumento roto, no un resultado — y en este arnes ya paso
    ''' dos veces. Devuelve el cartel de "no se midio" cuando no corrio el GL.</summary>
    Public Function ParityReport() As String
        SyncLock _parityLock
            If _parSlots = 0 Then
                ' NO se afirma la causa. Antes esto decia "el compositor GL no corrio (needGl=False)" sin
                ' haberlo comprobado, y hay otras rutas a cero slots (ResultId=0, GetTexImage que tira, el
                ' buffer CPU ausente, o TODOS los slots descartados por tamaño). Afirmar una causa no medida es
                ' justo lo que este instrumento existe para no hacer. Se listan los hechos que SI se saben.
                Dim sb0 As New Text.StringBuilder()
                sb0.AppendLine("CPU-vs-GPU parity: NOT MEASURED - 0 comparable slots.")
                sb0.AppendLine($"   GPU compositor ran: {If(WriteGPUSandboxOutput, "yes (WriteGPUSandboxOutput=True)", "NO (WriteGPUSandboxOutput=False -> needGl=False)")}")
                If _parSizeMismatch > 0 Then sb0.AppendLine($"   {_parSizeMismatch} slot(s) were dropped because CPU and GPU buffers had different sizes.")
                If _parInvalid > 0 Then sb0.AppendLine($"   {_parInvalid} NPC(s) invalidated the measurement. First: {_parInvalidWhy}")
                sb0.Append("   This run says NOTHING about the GPU path.")
                Return sb0.ToString()
            End If
            Dim rms = Math.Sqrt(_parSqErr / (_parPixels * 3.0))
            Dim sb As New Text.StringBuilder()
            sb.AppendLine("CPU-vs-GPU parity of the compose (in memory, BEFORE the BCn encode):")
            sb.AppendLine($"   slots compared   : {_parSlots}   pixels compared: {_parPixels:N0}")
            sb.AppendLine($"   global RMS       : {rms:F4}/255 (3 colour channels; alpha follows a separate rule)")
            sb.AppendLine($"   identical pixels : {_parExact:N0} ({100.0 * _parExact / _parPixels:F3} %)  [colour only, same basis as the RMS]")
            sb.AppendLine($"   alpha mismatches : {_parAlphaMismatch:N0} ({100.0 * _parAlphaMismatch / _parPixels:F3} %)  [keepBaseAlpha vs uForceOpaqueAlpha - a different rule, not a compose diff]")
            sb.AppendLine($"   worst |delta|    : {_parMaxD}" & If(_parMaxD > 0, $"  at {_parWorst}", ""))
            ' HISTOGRAMA: es lo que separa PRECISION de BUG. float32-vs-float64 da un decaimiento suave que
            ' se extingue en 1-2; una poblacion con delta 3+ o un escalon significan que una rama de la ley NO
            ' coincide entre los dos compositores. Sin esto solo se ve "worst=8", que no distingue un pixel
            ' raro de media cara corrida.
            sb.AppendLine("   |delta| histogram (per pixel, max over the 3 colour channels):")
            For k = 0 To 8
                Dim label = If(k = 8, "8+", k.ToString())
                Dim pct = 100.0 * _parHist(k) / _parPixels
                sb.AppendLine($"      {label,3} : {_parHist(k),14:N0}  {pct,7:F3} %")
            Next
            Dim tail As Long = 0
            For k = 3 To 8 : tail += _parHist(k) : Next
            If tail > 0 Then
                sb.AppendLine($"   ⛔ {tail:N0} pixel(s) ({100.0 * tail / _parPixels:F4} %) differ by 3 or more.")
                sb.AppendLine("      float32 (GPU FBO) vs float64 (CPU) explains +-1 and, at a stretch, 2.")
                sb.AppendLine("      A population at 3+ needs a CAUSE - but do NOT read it as 'law divergence'")
                sb.AppendLine("      by itself. MEASURED 2026-07-30, three candidate causes REFUTED with data:")
        sb.AppendLine("        - layer/mask resampling: REFUTED (measured 0/320 resampled bindings in the")
        sb.AppendLine("          channel carrying the tail). Counter removed once it had served its purpose.")
                sb.AppendLine("        - the mask pow (MaskConv=G22Encode): forcing Raw made the tail WORSE")
                sb.AppendLine("          (178 -> 353), so the pow is not the amplifier.")
                sb.AppendLine("        - alpha rules: 0 alpha mismatches over 54.5 M px.")
                sb.AppendLine("      What IS established: crossed BCn decode paths (CPU DirectXTex vs GPU hardware)")
                sb.AppendLine("      accounted for 94% of it and is fixed. The residue is still unexplained -")
                sb.AppendLine("      say so, do not attribute it.")
                sb.AppendLine($"      by channel:  _d={_parTailByChannel(0):N0}  _msn={_parTailByChannel(1):N0}  _s={_parTailByChannel(2):N0}")
                ' Correlacion con el RESAMPLEO: el bilineal del CPU (Double) y el del sampler GL (pesos en
                ' punto fijo de 8 bits por spec) NO coinciden bit a bit. Si la cola vive en el canal que mas
                ' resamplea, esa es la causa; si vive donde NO se resamplea, hay que buscar en otro lado.
                Dim rw = If(_parPixWithSwaps > 0, 1000000.0 * _parTailWithSwaps / _parPixWithSwaps, 0.0)
                Dim rn = If(_parPixNoSwaps > 0, 1000000.0 * _parTailNoSwaps / _parPixNoSwaps, 0.0)
                sb.AppendLine($"      tail by phase:  NPCs WITH region swaps: {_parTailWithSwaps:N0} px over {_parPixWithSwaps:N0} ({rw:F2} ppm)")
                sb.AppendLine($"                      NPCs WITHOUT swaps   : {_parTailNoSwaps:N0} px over {_parPixNoSwaps:N0} ({rn:F2} ppm)")
                If _parPixWithSwaps > 0 AndAlso _parPixNoSwaps > 0 Then
                    sb.AppendLine(If(rn < rw / 4.0, "      => the tail follows the REGION SWAP path (uMode=1).",
                                     If(rw < rn / 4.0, "      => the tail follows the TINT path, not the swaps.",
                                        "      => both phases carry it: the cause is shared, not swap-specific.")))
                End If
                sb.AppendLine($"      slots carrying tail: {_parSlotsWithTail} of {_parSlots}" &
                              If(_parSlotsWithTail = _parSlots, "  => SYSTEMIC (every slot)", "  => LOCALISED (a data condition, not the law)"))
                For Each t In _parTailTop.OrderByDescending(Function(x) x.N).Take(6)
                    sb.AppendLine($"         0x{t.Npc:X8} slot {t.Slot}: {t.N:N0} px")
                Next
                ' Grilla 8x8 en UV: AGRUPADO => causa espacial (una mascara/capa que cada lado resamplea
                ' distinto). REPARTIDO parejo => aritmetica. Es la pregunta que sigue al histograma.
                sb.AppendLine("      8x8 UV occupancy of those pixels (rows = V top->bottom):")
                Dim gmax As Long = 0
                For k = 0 To 63 : If _parGrid(k) > gmax Then gmax = _parGrid(k)
                Next
                For gy = 0 To 7
                    Dim row As New Text.StringBuilder("        ")
                    For gx = 0 To 7
                        row.Append($"{_parGrid(gy * 8 + gx),9:N0}")
                    Next
                    sb.AppendLine(row.ToString())
                Next
                Dim occupied = 0
                For k = 0 To 63 : If _parGrid(k) > 0 Then occupied += 1
                Next
                sb.AppendLine($"      cells with any: {occupied}/64   busiest cell: {gmax:N0} ({100.0 * gmax / Math.Max(1L, tail):F1} % of the tail)")
                If occupied <= 16 OrElse gmax > tail \ 4 Then
                    sb.AppendLine("      => CLUSTERED: the tail is spatially concentrated. That points at a LAYER/MASK")
                    sb.AppendLine("         that each side resamples differently (CPU SampleChannelAt vs GPU bilinear),")
                    sb.AppendLine("         not at arithmetic. Same family as the specular size divergence.")
                Else
                    sb.AppendLine("      => SPREAD: the tail is spread across the face, which points at arithmetic")
                    sb.AppendLine("         rather than at one mis-sampled layer.")
                End If
            Else
                sb.AppendLine("   All deltas are <= 2, which is consistent with float32 (GPU FBO) vs float64 (CPU).")
            End If
            If _parSizeMismatch > 0 Then sb.AppendLine($"   ⚠ {_parSizeMismatch} slot(s) NOT compared (CPU and GPU buffer sizes differ)")
            If _parInvalid > 0 Then
                sb.AppendLine($"   ⛔ RUN NOT VALID: {_parInvalid} NPC(s) where GL did not leave the output in OutputSpace.")
                sb.AppendLine($"      First: {_parInvalidWhy}")
                sb.AppendLine("      The numbers above MEASURE THAT FAILURE, not the compositor. Do not use them.")
            End If
            Return sb.ToString()
        End SyncLock
    End Function
    ' ===================================================================================

    ''' <summary>Cuando True el bake corre TAMBIÉN el pipeline GL y escribe el <c>_2b.dds</c> (salida GPU)
    ''' al lado del <c>_2.dds</c> (CPU), para medir paridad GPU-vs-CPU.
    ''' <para>Es el flag que realmente enciende GL, así que es la compuerta correcta para todo lo que
    ''' necesite contexto GL — no <see cref="DebugMode"/>. El CLI headless deja DebugMode=True (para
    ''' conservar el naming <c>_2</c>) pero éste en False, y así hornea 100 % CPU.</para>
    ''' <para>Default (override = Nothing) = <see cref="Logger.Enabled"/>, el comportamiento de la app.</para></summary>
    Private _gpuSandboxOverride As Boolean? = Nothing
    Public Property WriteGPUSandboxOutput As Boolean
        Get
            Return If(_gpuSandboxOverride, Logger.Enabled)
        End Get
        Set(value As Boolean)
            _gpuSandboxOverride = value
        End Set
    End Property
    ''' <summary>Tilde "Generate TGA" del diálogo CharGen Options (persistido en Config). Cuando está ON,
    ''' escribe un TGA UNCOMPRESSED al lado de cada .dds (CPU y, si corrió, GPU) — lossless aunque el .dds
    ''' sea BCn. ReadOnly: lo maneja el setting, no un setter externo.</summary>
    Public ReadOnly Property WriteTGASandboxOutput As Boolean
        Get
            Return If(Config_App.Current IsNot Nothing, Config_App.Current.Setting_FaceGenGenerateTga, False)
        End Get
    End Property


    ''' <summary>Settings de salida del bake (resolución por canal + compresión del diffuse), DERIVADO del
    ''' config persistido (Config_App, botón "CharGen Options"). Single source of truth = config; sin estado
    ''' que sincronizar. Se pasa idéntico al compositor GL y al CPU (-> GL==CPU). Lógica de tamaño:
    '''   PerLayer=False (ALL, default): los 3 canales usan el tamaño Diffuse (N/S heredan de D).
    '''   PerLayer=True: cada canal su propio tamaño.
    ''' Default config = All + Inherit (nativo) + BC3 = comportamiento actual / byte-comparable a gen3.</summary>
    Public ReadOnly Property OutputSettings As FaceTintConvention.FaceTintResolutionSettings
        Get
            Dim c = Config_App.Current
            Dim isSse = (c.Game = Config_App.Game_Enum.Skyrim)
            Dim d = c.Setting_FaceGenDiffuseResolution
            Dim perLayer = c.Setting_FaceGenPerLayerResolution
            ' Compresión PER-GAME (set del juego activo → sin leak entre juegos). All-mode: FO4 deriva N del D
            ' (NsCompressionFromDiffuse → BC5 tangent-space); SSE el N sigue al D (model-space, "All uniforme").
            ' Per-layer: cada canal el suyo. Specular = FO4-only (SSE no lo bakea).
            Dim dc = If(isSse, c.Setting_FaceGenDiffuseCompression_SSE, c.Setting_FaceGenDiffuseCompression)
            Dim nc = If(isSse, c.Setting_FaceGenNormalCompression_SSE, c.Setting_FaceGenNormalCompression)
            Return New FaceTintConvention.FaceTintResolutionSettings With {
                .Diffuse = d,
                .Normal = If(perLayer, c.Setting_FaceGenNormalResolution, d),
                .Specular = If(perLayer, c.Setting_FaceGenSpecularResolution, d),
                .DiffuseCompression = dc,
                .NormalCompression = If(perLayer, nc, If(isSse, NormalCompressionAllModeSse(), NsCompressionFromDiffuse(dc))),
                .SpecularCompression = If(perLayer, c.Setting_FaceGenSpecularCompression, NsCompressionFromDiffuse(dc))
            }
        End Get
    End Property

    ''' <summary>Modo All FO4: N/S siguen al Diffuse -> Uncompressed si el Diffuse es Uncompressed, sino BC5
    ''' (el _n de FaceCustomization es tangent-space 2-canales ⇒ BC5).</summary>
    Private Function NsCompressionFromDiffuse(d As FaceTintConvention.FaceTintDiffuseCompression) As FaceTintConvention.FaceTintNormalSpecularCompression
        Return If(d = FaceTintConvention.FaceTintDiffuseCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed,
                  FaceTintConvention.FaceTintNormalSpecularCompression.Bc5)
    End Function

    ''' <summary>Modo All SSE: el normal SIEMPRE Uncompressed — NO sigue al diffuse. El <c>_msn</c> es MODEL-SPACE:
    ''' sus 3 canales son X/Y/Z INDEPENDIENTES, y cualquier BCn comprime RGB a una línea por bloque 4×4, destruyendo la
    ''' dirección de la normal. MEDIDO (probe <c>--reencodetest</c>, mismo encoder del bake, MaleHead_msn 1024²):
    ''' BC3 → RGB RMS 5.07/255 (max B 148/255, 97.5% pixels alterados); Uncompressed → RMS 0.000 = round-trip EXACTO,
    ''' pixel-idéntico al vanilla (que ES Uncompressed 32bpp). El shader facegen lee el G-buffer de normales (o2.xy) de
    ''' este slot ⇒ comprimirlo rompe lighting/sombras/reflexiones de toda la cara. El diffuse SÍ tolera BCn (es color),
    ''' por eso el normal se desacopla de él. Vale para CUALQUIER caso (con o sin overlay-normal). El usuario puede
    ''' forzar otro formato en per-layer (CharGen Options), pero el DEFAULT del modo All es el fiel al vanilla.</summary>
    Private Function NormalCompressionAllModeSse() As FaceTintConvention.FaceTintNormalSpecularCompression
        Return FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
    End Function

    ''' <summary>True si el output del bake queda LOOSE en disco (no se empaqueta a un BA2): Build CharGen
    ''' loose (Not willBePacked) o Save ESP en modo loose-only (NPC_Config.Ba2Version_FO4 = 0). Los
    ''' artefactos de inspección (TGA, _2b) SOLO se escriben en este caso: el packer (NpcFaceGenPacker) mete
    ''' únicamente NIF + 3 DDS por nombre, así que un .tga/_2b en un BA2-save quedaría huérfano loose.</summary>
    Private Function OutputStaysLoose(willBePacked As Boolean) As Boolean
        If Not willBePacked Then Return True
        ' Game-aware loose sentinel: FO4 = Ba2Version_FO4 = 0 (byte-identical to the old check), SSE = Archive_SSE = 0.
        ' IsLooseOnly null-guards NPC_Config.Current (returns True → stays loose), preserving the prior guard.
        Return NPC_Config.IsLooseOnly(If(Config_App.Current IsNot Nothing, Config_App.Current.Game, Config_App.Game_Enum.Fallout4))
    End Function

    ''' <summary>Delegate matching the signature of <c>MainForm.ApplyShapeMaterialOverrides</c>.
    ''' BuildCharGen invokes this with a one-element shape list to resolve the material for the
    ''' NPC being baked — same code path the live render uses, no preview dependency.</summary>
    Friend Delegate Sub ApplyShapeMaterialOverridesDelegate(candidate As MainForm.MeshCandidate, state As MainForm.NPCVisualState, shapes As IEnumerable(Of IRenderableShape))

    ''' <param name="willBePacked">Distinguishes the two consumers of this bake, which differ ONLY
    ''' in DebugMode and ONLY in the texture path embedded inside the NIF:
    '''   True  = Save ESP path: the loose _2 outputs get repacked into a BA2 under canonical
    '''           (non-_2) names by NpcFaceGenPacker, so the NIF must embed canonical paths
    '''           (&lt;id&gt;_d.dds) to match the renamed BA2 entries.
    '''   False = "Build CharGen (loose)" button: nothing repacks/renames, so the NIF must embed
    '''           the actual on-disk path (&lt;id&gt;_d_2.dds) or the standalone loose NIF references a
    '''           texture that does not exist under that name.
    ''' In release (DebugMode=Off) Suffix == CanonSuffix, so this flag is a no-op.</param>
    Friend Function BuildCharGen(npcFormID As UInteger,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                 willBePacked As Boolean,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing,
                                 Optional lutDataPath As String = Nothing) As BuildResult

        ' GATE SIMD, UNA VEZ POR PROCESO Y ANTES DE HORNEAR NADA.
        ' POR QUE ACA Y NO EN PhaseReport: los self-tests vivian SOLO adentro de PhaseReport(), y a
        ' PhaseReport lo llama UNICAMENTE BakeAllRunner, DESPUES de terminar todo el barrido. O sea que
        ' (a) un bake normal desde la UI no los corria NUNCA — cero gate en produccion — y (b) en un barrido
        ' de corpus un MISMATCH aparecia despues de ~45 min, con los bytes YA escritos a disco.
        ' Aca corre antes del primer pixel y falla RUIDOSO: si los caminos vectoriales no son bit-identicos
        ' al escalar, hornear es producir bytes que no valen nada.
        EnsureSimdParityGate()

        ' Toma el contexto GL para ESTE bake antes de cualquier operacion GL. El "contexto actual" de
        ' OpenTK es por HILO y a nivel proceso, y coexisten varios PreviewControl (MainForm, EditFace,
        ' EditBody...): el OnPaint de cualquiera hace MakeCurrent sobre el suyo y nos roba el contexto.
        ' Sin este guard las operaciones GL del bake apuntan al contexto que quedo activo.
        ' El gate es WriteGPUSandboxOutput (el flag que realmente corre GL), NO DebugMode: sin GL el
        ' bake es 100 % CPU y puede correr en un thread de fondo, donde MakeCurrent FALLARIA porque el
        ' contexto es del hilo de UI.
        If WriteGPUSandboxOutput Then
            Try
                host?.PreviewCtl?.EnsureContextCurrent()
            Catch ex As Exception
                Dim msgL = ex.Message
                Dim typeL = ex.GetType().Name
                Logger.LogLazy(Function() $"[FACEBAKE-FAIL] MakeCurrent threw at bake entry npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
            End Try
        End If

        ' Estado visual del NPC que se hornea, independiente del que el preview este mostrando: el bake NUNCA
        ' lee estado del host. Parsea el record + aplica el overlay del preset de LooksMenu (el mismo que usa el
        ' render vivo) y copia los campos que consume el resolver de material; si aparece un camino del resolver
        ' que toque otro campo, hay que copiarlo aca.
        ' WYSIWYG: si el usuario eligio un SkinTemplate de LooksMenu, el bake tiene que aplicar ese bundle igual
        ' que el render, o el NIF horneado diverge del WNAM que el writer pone en el ESP.
        ' ⛔ ACA NO SE CAMINA LA CADENA DE PLANTILLAS, Y ESO ES CORRECTO - no "arreglarlo". El CK nunca exporta
        ' FaceGen para un NPC que hereda "Use Traits" (medido en los dos juegos), asi que sembrar el state desde
        ' el traits-source fabricaria un artefacto que el CK no produce jamas. El flujo legitimo es el inverso y
        ' ya existe: NpcTemplateMaterializer.MakeCategoryOwn(Traits). Ver 40-bake-reglas-comunes.
        ' Arranca RecordResolve: overlay del NPC, mapa de HDPT, BakeState y huesos del actor (ver BakePhase).
        Dim tRec = Stopwatch.GetTimestamp()
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(
            npcFormID, pluginManager, appliedPresets, lmSkinTemplateResolver)
        Dim state As MainForm.NPCVisualState = Nothing
        If npcData IsNot Nothing Then
            ' .SseHairColorRgb = SSE RaceMenu absolute hair tint. Sin esto el bake resolvía el pelo por el
            ' CLFM mientras el preview mostraba el RGB del preset ⇒ RENDER ≠ BAKE. Lo consume el MISMO
            ' ApplyMaterialPaletteHairColor que corre el render (vía applyMaterialOverrides).
            ' Nothing fuera de Skyrim: el overlay solo lo puebla en SSE.
            ' (El comentario vive ACÁ y no entre los miembros: VB no admite comentarios dentro de un
            '  inicializador With { } — rompe el parser con BC30985.)
            state = New MainForm.NPCVisualState With {
                .FormID = npcFormID,
                .RootNpcFormID = npcFormID,
                .ModelSourceFormID = npcFormID,
                .RaceFormID = npcData.Record.Race,
                .IsFemale = npcData.Record.ConfigurationFlagsFemale,
                .SkinFormID = npcData.Record.Skin,
                .HeadTextureFormID = npcData.Record.HeadTexture,
                .HairColorFormID = npcData.Record.HairColor,
                .SseHairColorRgb = npcData.SseHairColorRgb,
                .FacialHairColorFormID = npcData.Record.ColorDeBarba(),
                .HasTextureLighting = npcData.Record.TextureLightingRedPresente,
                .TextureLightingColor = npcData.Record.ColorDeIluminacionDeTextura(),
                .HeadDiffuseAlphaTest = (npcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (npcData.Record.ConfigurationFlags And &H1000000UI) <> 0UI
            }
            state.HeadPartFormIDs.AddRange(npcData.Record.PartesDeCabeza())
            ' Engine race fallbacks: NPC.WNAM=0 → RACE.SkinFormID, NPC head parts/texture/hair
            ' → RACE defaults, NPC.MWGT sentinel substitution. Same path the render uses; without
            ' it ResolveActorSkinTextureSet returns Nothing for NPCs that leave WNAM=0 (e.g.
            ' vanilla children) and the bake falls through to HDPT.TNAM, which for ChildHeadRear
            ' is hardcoded SkinBodyChildMale — wrong for female actors.
            NpcStateResolver.ApplyRaceFallbacks(state, NpcStateFactory.CreateOwnTraitsState(npcData), pluginManager)
        End If
        Dim result As New BuildResult()

        Dim originPlugin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(originPlugin) Then
            result.Summary = "Could not resolve origin plugin for this NPC."
            Return result
        End If

        ' Build a fresh FO4 NIF — same path OutfitStudio takes when importing OBJ/FBX without
        ' a base mesh ([OutfitProject.cpp:515-531] calls workNif.Create(NiVersion::getFO4())).
        ' NiVersion.GetFO4() = (V20_2_0_7, user=12, stream=130), the canonical FO4 framing CK
        ' writes. withRootNode=True drops in the root NiNode the engine expects.
        ' Game-aware bake: SSE (Skyrim) difiere de FO4 en root del shell, zeroing de bounds y tipo de skin
        ' (NiSkinInstance/BSDismember vs BSSkin::Instance). Declarado a scope de metodo para gatear el reparent.
        Dim isSSEBake As Boolean = (Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game = Config_App.Game_Enum.Skyrim)
        Dim nif As New Nifcontent_Class_Manolo()
        Try
            ' Shell GAME-AWARE, medido byte a byte contra los FaceGeom vanilla de cada juego:
            '   FO4 = stream 130, root NiNode  name ""              Flags 0x400E  (el CK nunca usa BSFadeNode)
            '   SSE = stream 100, root BSFadeNode name "<localId>.NIF" Flags 0x000E (SSE siempre BSFadeNode)
            ' Se agrega como bloque 0 para que GetRootNode()/CloneShape parenteen contra el; los shapes se
            ' re-cuelgan despues bajo un BSFaceGenNiNodeSkinned. Todo lo demas del bake es game-agnostico.
            If isSSEBake Then
                nif.Create(NiVersion.GetSSE(), withRootNode:=False)
                Dim faceRootSse As New NiflySharp.Blocks.BSFadeNode() With {
                    .Name = New NiflySharp.NiStringRef($"{PluginManager.ToFaceGenLocalFormID(npcFormID):X8}.NIF"),
                    .Flags_ui = &HEUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                nif.AddBlock(faceRootSse)
            Else
                nif.Create(NiVersion.GetFO4(), withRootNode:=False)
                Dim faceRoot As New NiflySharp.Blocks.NiNode() With {
                    .Name = New NiflySharp.NiStringRef(""),
                    .Flags_ui = &H400EUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                nif.AddBlock(faceRoot)
            End If
        Catch ex As Exception
            result.Summary = $"Failed to create FaceGen NIF shell: {ex.Message}"
            Return result
        End Try

        ' Preventive race-level eligibility gate (canonical FaceGen-Head flag, version-aware) — run
        ' BEFORE BuildAllowedShapeMap. A non-FaceGen race (dog/creature/robot/turret/feral ghoul/etc.)
        ' has no head/face to bake; without this gate a dog NPC carrying a stray human Teeth HDPT in
        ' PNAM resolves a non-empty hdptMap (passing the Count=0 guard below) yet every shape is dropped
        ' at clone time → an empty NIF gets written. RaceSupportsFaceGen reads RACE.DATA bit 0x2 and is
        ' the 0-exception discriminator. Uses the same race FormID source BuildAllowedShapeMap consumes
        ' (NPC_.RaceFormID; the LM overlay never rewrites the race).
        Dim gateRaceFormID As UInteger = If(npcData IsNot Nothing, npcData.Record.Race, 0UI)
        If Not RaceUtil.RaceSupportsFaceGen(gateRaceFormID, pluginManager) Then
            result.Skipped = True
            result.Success = False
            result.Summary = "Race has no FaceGen (dog/creature/robot/feral ghoul/etc.) — skipped, no NIF."
            Return result
        End If

        ' Build the canonical HDPT chain for this NPC. Each entry has its MeshPath and (later)
        ' chargen TRI / FMRS info. This is the AUTHORITATIVE list — the .nif contains exactly
        ' the shapes that come out of these sources. Seeded from `state` (= overlaid npcData +
        ' ApplyRaceFallbacks), the SAME list the live render walks — so a modified chargen bakes
        ' the head parts the preview shows, not the raw record's.
        Dim hdptMap = BuildAllowedShapeMap(state, pluginManager)

        ' No FaceGen-eligible head parts (non-human race, robot, turret, creature, …) → nothing to
        ' bake. This is a SKIP, not a failure: don't write an empty NIF, and let the caller count it
        ' separately (batch summary / Save) instead of reporting a spurious "fail".
        If hdptMap Is Nothing OrElse hdptMap.Count = 0 Then
            result.Skipped = True
            result.Success = False
            result.Summary = "No FaceGen head parts for this NPC — skipped."
            Return result
        End If

        ' GATE DEL WRAPPER NATIVO, EN EL CHOKEPOINT DEL BAKE. Por acá pasan TODAS las escrituras de
        ' DDS de la app (GUI 1 NPC, GUI multiselección, Save ESP, --bake-all con y sin ventana, --bake-geom
        ' y el CLI) y todas las lecturas de texturas DX10 desde BA2. Con el wrapper desajustado cada source
        ' DX10 se lee como 0 bytes y el bake escribe caras equivocadas EN SILENCIO.
        ' VA ACÁ y no antes: un NPC que sale por `Skipped` (raza sin FaceGen, sin head parts) DESCARTA
        ' `TextureSlotsFailed` río abajo, así que reportarlo antes de este punto no se ve.
        ' NO va por `Logger`: está forzado a False en Release y ninguna de las dos GUI lo prende. El canal
        ' que SÍ se ve en los tres consumidores es `RecordTextureFailure` → `BuildResult`.
        ' NO reemplaza al gate de arranque de los modos headless: aquel ABORTA en 200 ms; éste reporta por
        ' NPC y dejaría correr un barrido entero escribiendo NIF sin texturas. Son dos cosas distintas.
        Dim fallaWrapper = DirectXTexWrapperGate.Verificar()
        If fallaWrapper <> "" Then
            RecordTextureFailure(result, "componente nativo de texturas incompatible: " &
                                         fallaWrapper.Replace(vbCrLf, " ").Trim())
        End If

        ' Ensamblado de shapes desde las mallas fuente, un HDPT por vez: se carga HDPT.MeshPath del pool
        ' y se clonan TODAS sus shapes al shell. No hay name-matching contra el FaceGeom del CK — las
        ' mallas fuente deciden qué shapes existen. Un mismo NIF puede estar referenciado por varios HDPT
        ' (los ojos aparecen en Eyes y en sus extras Lashes/AO/Wet), así que `loadedSources` evita
        ' recargarlo y `clonedShapeNames` evita insertarlo dos veces.
        Dim loadedSources As New Dictionary(Of String, Nifcontent_Class_Manolo)(StringComparer.OrdinalIgnoreCase)
        Dim clonedShapeNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        Dim hdptProcessed As Integer = 0
        Dim hdptSourceMissing As Integer = 0
        Dim hdptSourceLoadFail As Integer = 0
        Dim shapesCloned As Integer = 0
        Dim shapesSkippedDup As Integer = 0
        Dim shapesMorphed As Integer = 0
        ' Shapes con `_faceBones` cargado pero sin ninguna shape que matchee ⇒ se escriben SIN morphear.
        ' Se cuenta y se loguea (FormID + shape) para que la caída no sea silenciosa.
        Dim shapesFbnsUnmatched As Integer = 0
        ' FO4 sin `_faceBones`, morpheada por el fallback de chargen-morphs. El radio esperado es chico
        ' (cabezas infantiles): un número grande acá es señal de alarma, no de éxito.
        Dim shapesFo4NoFbnsMorphed As Integer = 0
        ' FO4, shape SIN _faceBones y SIN bakeState => escrita NEUTRA. Es la caida silenciosa que QUEDA
        ' despues del fix; se cuenta y se reporta en el Summary por el mismo motivo que shapesFbnsUnmatched:
        ' si solo vive en el log, un batch con logging apagado canta exito.
        Dim shapesFo4NoFbnsNoMorph As Integer = 0
        ' El bake de texturas FaceCustomization (FO4) es PER-NPC y no per-shape: los 3 DDS se llaman
        ' <formID>_d/_msn/_s y no llevan nada de la shape, asi que este flag lo corre una sola vez.
        Dim fo4FaceTexturesBaked As Boolean = False
        ' Shapes que se cayeron DENTRO del loop (clone/material/bake tiraron). Antes el Try gigante se las
        ' tragaba enteras y el batch las contaba como exito.
        Dim shapesFailed As Integer = 0

        ' --- ITERATION 3: build the FaceGen bake state (NPC overlay + race morph defs +
        ' FMRS pose). Single source of truth, consumed by FaceGenBuildPipeline.BakeShape per
        ' HDPT to produce v_baked = inv(Mtot_orig) × v_world.
        Dim regionsFile As FacialBoneRegionsFile = Nothing
        Dim probeNpcRaw = NpcRecordOverlay.GetParsedNpc(npcFormID, pluginManager)
        ' Raza EFECTIVA para las FacialBoneRegions: preferir el npcData overlaid (ya stampado con el
        ' override de raza del editor); probeNpcRaw es el parse crudo y tras un cambio de raza apuntaría
        ' a las regiones de la raza vieja.
        Dim probeRaceFid As UInteger = If(npcData IsNot Nothing AndAlso npcData.Record.Race <> 0UI,
                                          npcData.Record.Race, If(probeNpcRaw IsNot Nothing, probeNpcRaw.Record.Race, 0UI))
        If probeNpcRaw IsNot Nothing AndAlso probeRaceFid <> 0UI Then
            Dim raceRec = pluginManager.GetRecord(probeRaceFid)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                Dim raceProbe = Canon.CanonRecords.Race(raceRec, pluginManager)
                ' BAKE == RENDER: resolve FMRI against the MERGED both-gender table, exactly like
                ' NpcMorphPoseResolver.BuildFaceBoneTransforms does for the live render. The two
                ' per-gender JSONs use disjoint ID namespaces, and 10 vanilla NPCs carry FMRI from
                ' the opposite gender's namespace — own-gender-only lookup silently baked a neutral
                ' head for them. See GetFacialBoneRegionsForFmriResolution for the measured evidence.
                regionsFile = NpcMorphPoseResolver.GetFacialBoneRegionsForFmriResolution(raceProbe, probeNpcRaw.Record.ConfigurationFlagsFemale)
            End If
        End If
        Dim bakeState As FaceGenBuildPipeline.BakeState =
            FaceGenBuildPipeline.BuildBakeState(npcFormID, pluginManager, appliedPresets, regionsFile)
        ' Names of every bone the actor's face + body skeletons expose. Used below
        ' to drop source shapes whose skin references a bone outside this set
        ' (CK-equivalent filter — see the call site for the rationale).
        Dim actorBoneNames As HashSet(Of String) = FaceGenBuildPipeline.GetActorBoneNames(bakeState)
        PhaseAdd(BakePhase.RecordResolve, tRec)   ' cierra el tramo abierto antes de ResolveOverlaidNpcData
        ' Desambiguación EN EL ORIGEN: GetActorBoneNames devuelve un set VACÍO si fallan las dos cargas
        ' de esqueleto (face y body). Con el set vacío el filtro de huesos desconocidos se auto-deshabilita
        ' aguas abajo, y hasta ahora lo hacía EN SILENCIO ⇒ "0 shapes dropeados" era ambiguo: no se podía
        ' distinguir "no había nada que dropear" de "el filtro ni siquiera pudo correr". Se loguea una vez
        ' por bake, acá, donde está la causa.
        If actorBoneNames Is Nothing OrElse actorBoneNames.Count = 0 Then
            Logger.LogLazy(Function() $"[FACEBAKE] unknown-bone filter DISABLED for npcFormID=0x{npcFormID:X8}: actor skeleton bone set is EMPTY (face+body skeleton load failed) — no source shape can be dropped in this bake")
        End If
        ' Skin-tint strength for SkinTint shapes (shaderType=5). It's the NPC's QNAM/SkinTone-layer
        ' alpha — a SEPARATE float from the skin tone RGB (NpcRecordOverlay derives both into
        ' TextureLightingFloats: RGB from the SkinTone palette, A from the layer opacity, else the
        ' raw QNAM float). The LIBRARY Save_To_Shader writes it to the shader (gated on SkinTint);
        ' we only hand it the value, because it's NPC-level (the BGSM has no skin-tint-alpha field) —
        ' exactly the split used for the skin tone COLOR. Use the float (not Color.A/255). 1.0 if absent.
        Dim skinTintAlpha As Single = 1.0F
        If bakeState IsNot Nothing AndAlso bakeState.NpcData IsNot Nothing Then
            skinTintAlpha = bakeState.NpcData.Record.AlphaDeIluminacionDeTextura()
        End If
        ' Slots de headwear que cubre la DEFAULT OUTFIT del NPC. Alimentan la oclusión de pelo/barba/cejas
        ' que se aplica más abajo, por shape; la regla completa está documentada en ese sitio.
        Dim outfitResolved = ResolveOutfitHeadwearSlots(npcData, pluginManager)
        Dim outfitSlots As UInteger = outfitResolved.Slots
        Dim outfitHasHairLong As Boolean = (outfitSlots And BakeSlotBitHairLong) <> 0UI
        Dim outfitHasFaceGenHead As Boolean = (outfitSlots And BakeSlotBitFaceGenHead) <> 0UI
        Dim outfitHasBeard As Boolean = (outfitSlots And BakeSlotBitBeard) <> 0UI
        Dim outfitHasMouth As Boolean = (outfitSlots And BakeSlotBitMouth) <> 0UI
        ' Captura para el sandbox FORZADO _2c (debug+sandbox): head shape + complexion/normal ORIGINALES (antes de
        ' que el pass normal mute los slots), para correr el replacer completo en cualquier NPC y salvar _2c.NIF.
        Dim sseForcedHead As INiShape = Nothing
        Dim sseForcedComplexion As String = Nothing, sseForcedNormal As String = Nothing, sseForcedDetail As String = Nothing
        For Each kv In hdptMap.OrderBy(Function(p) p.Value.Hdpt.TipoDeParte()).ThenBy(Function(p) p.Key)
            Dim hdptName = kv.Key
            Dim hdpt = kv.Value.Hdpt
            Dim effectiveHeadPartType = kv.Value.EffectivePartType
            If String.IsNullOrEmpty(hdpt.ModelFileName) Then
                hdptSourceMissing += 1
                Dim hnLog = hdptName
                Logger.LogLazy(Function() $"[FACEBAKE] HDPT '{hnLog}' has empty MeshPath; shape skipped")
                Continue For
            End If

            ' Source resolution: arrancamos SIEMPRE del original `<mesh>.nif`, NO del
            ' `<mesh>_facebones.nif`. El log three-way (BUILDCHARGEN-THREEWAY) confirmó
            ' empíricamente — 11/11 shapes en Alijo — que el bake de CK usa la bone palette
            ' del ORIGINAL, no la del _facebones. El _facebones agrega face bones al skin
            ' partition para soporte runtime de FMRS pero CK al bakear los descarta.
            ' (faceBonesKey solo se usa para diagnóstico three-way; no se carga para clonar.)
            Dim baseKey = MeshPathHelpers.NormalizeMeshKey(hdpt.ModelFileName)
            Dim faceBonesKey = MeshPathHelpers.TryGetFaceBonesVariant(baseKey)
            Dim sourceKey = baseKey

            Dim srcNif As Nifcontent_Class_Manolo = Nothing
            If Not loadedSources.TryGetValue(sourceKey, srcNif) Then
                Dim srcBytes As Byte() = Nothing
                Try
                    srcBytes = FilesDictionary_class.GetBytes(sourceKey)
                Catch ex As Exception
                    ' F8: catch vacio. Cae en el guard de abajo (hdptSourceMissing) pero sin decir la CAUSA.
                    Dim skE = sourceKey, tS = ex.GetType().Name, mS = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE] GetBytes lanzo para '{skE}': {tS}: {mS}")
                End Try
                If srcBytes Is Nothing OrElse srcBytes.Length = 0 Then
                    hdptSourceMissing += 1
                    Dim skLogMiss = sourceKey
                    Logger.LogLazy(Function() $"[FACEBAKE] source mesh not in FilesDictionary: '{skLogMiss}'; shape skipped")
                    Continue For
                End If
                srcNif = New Nifcontent_Class_Manolo()
                Try
                    Dim tParse = Stopwatch.GetTimestamp()
                    srcNif.Load_Manolo(srcBytes)
                    PhaseAdd(BakePhase.SourceNifParse, tParse)   ' ver BakePhase: esto se REHACE por NPC (loadedSources es local)
                Catch ex As Exception
                    hdptSourceLoadFail += 1
                    Dim skLogFail = sourceKey
                    Logger.LogLazy(Function() $"[FACEBAKE] source NIF failed to load: '{skLogFail}': {ex.GetType().Name}: {ex.Message}; shape skipped")
                    Continue For
                End Try

                ' SSE: un head part en formato Skyrim LE (NiTriShape) tiene que salir BSDynamicTriShape, igual
                ' que el CK. No-op si el source ya es SSE.
                Try
                    Dim srcVer = srcNif.Header?.Version
                    If isSSEBake AndAlso srcVer IsNot Nothing AndAlso srcVer.IsSK Then
                        srcNif.Optimize(Config_App.Game_Enum.Skyrim, headPartsOnly:=True)
                    End If
                Catch exOpt As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE] OptimizeFor LE->SSE head parts failed for '{sourceKey}': {exOpt.GetType().Name}: {exOpt.Message}")
                End Try

                loadedSources(sourceKey) = srcNif
            End If

            ' Clonado de shapes al shell. `CloneShape_Original` resuelve la semántica cross-NIF
            ' (preservación del bone-skin, deep clone de shader + texture set).
            ' REGLA DE NOMBRE: el FaceGeom nombra cada shape con el EditorID de su HDPT, sin importar cómo
            ' se llame la shape dentro del NIF fuente; por eso el nombre destino se pasa al clonar y no
            ' hace falta renombrar después. Si un NIF fuente trae más de una shape, la primera queda como
            ' `<EditorID>` y el resto como `<EditorID>_<nombre origen>` para no colisionar.
            '
            ' Los huesos de física de cloth (Hair_*_Cloth, Ponytail_*, SideTail_*) NO están en
            ' skeleton.nif: viven en el hkaSkeleton embebido en el BSClothExtraData de ESTE NIF de pelo.
            ' Sin recolectarlos, el filtro "unknown bone" de más abajo descarta la shape de pelo con
            ' física entera. Ver 25-cloth-inyeccion-de-huesos.md.
            Dim clothBoneNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Try
                Dim srcClothSkel = SkeletonClothOverlayHelper_Class.ParseClothSkeleton(srcNif)
                If srcClothSkel IsNot Nothing AndAlso srcClothSkel.Bones IsNot Nothing Then
                    For Each cb In srcClothSkel.Bones
                        If cb IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(cb.Name) Then clothBoneNames.Add(cb.Name.Trim())
                    Next
                End If
            Catch ex As Exception
                ' F8: catch vacio. Sin cloth-bones el filtro de huesos desconocidos DROPEA la shape de pelo con
                ' fisica entera (el caso que el propio filtro documenta como excepcion). Tiene que verse.
                Dim tCl = ex.GetType().Name, mCl = ex.Message
                Logger.LogLazy(Function() $"[FACEBAKE] no se pudo leer el cloth-skeleton del source: {tCl}: {mCl} — el pelo con fisica puede dropearse")
            End Try

            Dim srcShapes = srcNif.GetShapes().ToList()
            Dim shapeIdxInThisHdpt As Integer = 0
            For Each srcShape In srcShapes
                Dim sourceName = If(srcShape.Name?.String, "")
                If sourceName = "" Then
                    Continue For
                End If
                Dim destName As String
                If srcShapes.Count = 1 OrElse shapeIdxInThisHdpt = 0 Then
                    destName = hdptName
                Else
                    destName = $"{hdptName}_{sourceName}"
                End If
                shapeIdxInThisHdpt += 1
                If clonedShapeNames.Contains(destName) Then
                    shapesSkippedDup += 1
                    Continue For
                End If
                ' CK-equivalent filter: drop a source shape whose skin references a bone
                ' that doesn't exist en el esqueleto del actor NI en los cloth-bones del NIF.
                ' Vanilla example: MaleEyesGhoul.nif holds two shapes — the iris (skins to
                ' 'Head') and a tear-duct sub-shape (skins to a custom 'GhoulTearDuct' bone
                ' that the actor's skeleton.nif does not expose). CK drops the second; we
                ' mirror that here so the bake doesn't carry an unrenderable extra shape.
                ' EXCEPCIÓN cloth-physics (#pelo): los cloth-bones (Hair_*_Cloth, Ponytail_*,
                ' SideTail_*) NO están en skeleton.nif pero SÍ en el BSClothExtraData del NIF
                ' (clothBoneNames) — son legítimos y CK los conserva, así que NO se descartan.
                Dim skipUnknownBone As String = Nothing
                ' Auto-deshabilitado si no pudimos cargar NINGÚN esqueleto del actor (set vacío ⇒ no hay
                ' contra qué contrastar). Ese caso YA se loguea una vez por bake en el ORIGEN, donde se
                ' resuelve actorBoneNames — acá no se repite por shape para no inundar el log.
                If actorBoneNames IsNot Nothing AndAlso actorBoneNames.Count > 0 Then
                    Try
                        Dim sti = TryCast(srcShape, NiflySharp.Blocks.BSTriShape)
                        If sti IsNot Nothing AndAlso sti.SkinInstanceRef IsNot Nothing AndAlso sti.SkinInstanceRef.Index >= 0 Then
                            Dim skBlk = srcNif.Blocks(sti.SkinInstanceRef.Index)
                            ' Nombres de hueso del skin, POR JUEGO. FO4 = BSSkin::Instance;
                            ' SSE = NiSkinInstance / BSDismemberSkinInstance (hereda de NiSkinInstance).
                            ' Antes este sitio SÓLO hacía TryCast a BSSkin_Instance ⇒ en un bake SSE el
                            ' cast daba Nothing siempre y el filtro era VACUO (no podía dispararse nunca).
                            ' Mismo camino que el barrido de referencedBones más abajo (el HashSet homónimo).
                            Dim skinBoneRefs As New List(Of Integer)
                            Dim srcSi = TryCast(skBlk, NiflySharp.Blocks.BSSkin_Instance)
                            If srcSi IsNot Nothing AndAlso srcSi.Bones IsNot Nothing Then
                                For bi As Integer = 0 To srcSi.Bones.Count - 1
                                    skinBoneRefs.Add(srcSi.Bones.GetBlockRef(bi))
                                Next
                            Else
                                Dim srcNiSi = TryCast(skBlk, NiflySharp.Blocks.NiSkinInstance)
                                If srcNiSi IsNot Nothing AndAlso srcNiSi.Bones IsNot Nothing Then
                                    For bi As Integer = 0 To srcNiSi.Bones.Count - 1
                                        skinBoneRefs.Add(srcNiSi.Bones.GetBlockRef(bi))
                                    Next
                                End If
                            End If
                            For Each bRef In skinBoneRefs
                                If bRef < 0 Then Continue For
                                Dim bNode = TryCast(srcNif.Blocks(bRef), NiflySharp.Blocks.NiNode)
                                Dim bName = bNode?.Name?.String
                                If Not String.IsNullOrEmpty(bName) AndAlso Not actorBoneNames.Contains(bName) _
                                   AndAlso Not clothBoneNames.Contains(bName) Then
                                    skipUnknownBone = bName
                                    Exit For
                                End If
                            Next
                        End If
                    Catch ex As Exception
                        ' F8: catch vacio. Si esto tira, el filtro de huesos desconocidos se AUTO-DESHABILITA para
                        ' esta shape (skipUnknownBone queda Nothing => se clona igual). No es fatal, pero tiene que
                        ' verse: era indistinguible de "no habia nada que dropear".
                        Dim snB = sourceName, tB = ex.GetType().Name, mB = ex.Message
                        Logger.LogLazy(Function() $"[FACEBAKE] el filtro de huesos desconocidos fallo en la shape '{snB}' y NO pudo evaluarla: {tB}: {mB}")
                    End Try
                End If
                ' En SSE este filtro es DETECT-ONLY a propósito, no por olvido. Su razón de ser es un
                ' caso de FO4 y no hay ningún caso de SSE que deba arreglar; en cambio el conjunto de
                ' shapes del bake SSE ya está medido contra el CK y cerrado CON el filtro inactivo
                ' (ver 40-bake-estado-cerrado.md). Un filtro que sólo QUITA shapes no puede mejorar eso y
                ' sí puede reintroducir divergencias, así que acá se loguea lo que se dropearía y no se
                ' dropea. Si un barrido no emite ninguna línea [FACEBAKE-SSE-DRYRUN], habilitarlo es un
                ' no-op seguro; si emite alguna, hay que justificarla shape por shape ANTES de tocar esto.
                If skipUnknownBone IsNot Nothing AndAlso isSSEBake Then
                    Dim hnDry = hdptName
                    Dim snDry = sourceName
                    Dim bnDry = skipUnknownBone
                    Logger.LogLazy(Function() $"[FACEBAKE-SSE-DRYRUN] would drop shape '{snDry}' from HDPT '{hnDry}': skins to bone '{bnDry}' not in actor skeleton nor cloth-bones — NOT dropped (SSE shape-set is measured at ~0 defects vs CK; drop not enabled without evidence)")
                    skipUnknownBone = Nothing
                End If
                If skipUnknownBone IsNot Nothing Then
                    Dim hnLog = hdptName
                    Dim snLog = sourceName
                    Dim bnLog = skipUnknownBone
                    Logger.LogLazy(Function() $"[FACEBAKE] dropping shape '{snLog}' from HDPT '{hnLog}': skins to bone '{bnLog}' not in actor skeleton")
                    Continue For
                End If
                Try
                    Dim tClone = Stopwatch.GetTimestamp()
                    Dim cloned = nif.CloneShape_Original(srcShape, destName, srcNif)
                    PhaseAdd(BakePhase.ShapeClone, tClone)
                    If cloned IsNot Nothing Then
                        clonedShapeNames.Add(destName)
                        shapesCloned += 1

                        ' Oclusion de headwear (regla determinista, 0 excepciones sobre 958 piezas), por tipo
                        ' EFECTIVO del HDPT (un Misc bajo pelo cuenta como Hair; bajo barba, como FacialHair):
                        '   Hair       : oculto si el shape es biped {30}-sin-{31} Y el outfit cubre el 31.
                        '   FacialHair : oculto si el outfit cubre 32 / 48 / 49.
                        '   Eyebrows   : oculto si el outfit cubre 32.     HeadRear: nunca.
                        ' outfitSlots solo suma ARMO DETERMINISTICAS, por eso no se gatea por "el outfit tiene
                        ' LVLI": un outfit puramente LVLI no cubre nada (under-hide, el lado seguro) y un casco
                        ' ARMO fijo ocluye igual aunque otra pieza sea LVLI. Ocultar = OR del bit 0x1, como el render.
                        ' ⛔⛔ SOLO FO4. En Skyrim el CK NO hornea la oclusion, la deja a runtime (0 de 20.611
                        ' shapes de los facegeom vanilla llevan el bit), y ademas esta regla usa semantica de
                        ' slots de FO4: alla el 32 es el CUERPO, asi que "el outfit cubre 32" seria "lleva ropa"
                        ' y ocultaria las cejas de todo NPC vestido.
                        If Config_App.Current IsNot Nothing AndAlso Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then
                            Try
                                Dim occlude As Boolean = False
                                Select Case effectiveHeadPartType
                                    Case PartTypeHair
                                        occlude = outfitHasHairLong AndAlso ShapeBiped30Only(cloned)
                                    Case PartTypeFacialHair
                                        occlude = (outfitHasFaceGenHead OrElse outfitHasBeard OrElse outfitHasMouth)
                                    Case PartTypeEyebrows
                                        occlude = outfitHasFaceGenHead
                                        ' PartTypeHeadRear (9): nunca se ocluye.
                                End Select
                                If occlude Then
                                    ' INiShape expone Flags_ui (NiAVObject). El shape clonado trae 0xE
                                    ' (visible); OR 0x1 lo marca hidden, igual que el render.
                                    cloned.Flags_ui = cloned.Flags_ui Or &H1UI
                                End If
                            Catch ex As Exception
                                ' La regla de oclusión se documenta arriba como "determinista, 0
                                ' excepciones sobre 958 piezas". Si igual tira, la shape se hornea VISIBLE
                                ' y el pelo atraviesa el casco — con un NIF byte-indistinguible del caso
                                ' legítimo, o sea imposible de detectar después. Se deja seguir (una pieza
                                ' mal ocluida no justifica perder el NPC entero) pero NO en silencio.
                                Dim mo = ex.GetType().Name & ": " & ex.Message
                                Logger.LogLazy(Function() $"[BAKE-OCCL] la regla de oclusión de headwear falló, la shape queda VISIBLE: {mo}")
                            End Try
                        End If

                        ' Cloth-physics hair (#pelo): CK cuelga el BSClothExtraData (el hkaSkeleton
                        ' de los cloth-bones) de la SHAPE del pelo, no del root (audit byte-fidelity:
                        ' 256/256 NIFs FaceGen de CK lo traen en la shape; 0 en el root). CloneShape_Original
                        ' NO transfiere el cloth extradata → lo clonamos del NIF source y lo colgamos de
                        ' la shape clonada del pelo, replicando a CK. Idempotente: si la shape ya tiene
                        ' uno, no duplica. Los cloth-bone NiNodes los crea el clone cross-file de NiflySharp
                        ' (re-mapea los bones del skin por nombre) y el reparent loop los cuelga flat del root.
                        If clothBoneNames.Count > 0 Then
                            Try
                                nif.TransferShapeClothExtraDataFrom(srcNif, cloned)
                            Catch ex As Exception
                                Logger.LogLazy(Function() $"[FACEBAKE] cloth extradata transfer failed for '{destName}': {ex.GetType().Name}: {ex.Message}")
                            End Try
                        End If

                        ' (c) ECED y demás extradata de la shape: ya NO se transfiere acá. La preservación
                        ' del ExtraDataList de la shape source (incl. BSEyeCenterExtraData) se hace de forma
                        ' GENERAL dentro de CloneShape_Original (cross-file), así lo conservan también WM /
                        ' SplitShape. Ver NifContent_Class.CloneShape_Original.

                        ' CBBE source for the female rear head ships with a malformed SSFFile
                        ' ("...\FacePssf", no extension). CK blanks SSFFile when baking; clear
                        ' only this exact (shape name, value) pair so we don't touch anything
                        ' else.
                        Try
                            If destName = "FemaleHeadHumanRearTEMP" Then
                                Dim subIdx = TryCast(cloned, NiflySharp.Blocks.BSSubIndexTriShape)
                                If subIdx IsNot Nothing AndAlso Not IsNothing(subIdx.SegmentData) AndAlso subIdx.SegmentData.SSFFile IsNot Nothing Then
                                    Const MalformedFacePssf As String = "Meshes\Actors\Character\CharacterAssets\FacePssf"
                                    If subIdx.SegmentData.SSFFile.Content = MalformedFacePssf Then
                                        subIdx.SegmentData.SSFFile.Content = ""
                                    End If
                                End If
                            End If
                        Catch ex As Exception
                        End Try

                        ' Match CK's behaviour: in a baked FaceGen NIF the shader carries no
                        ' BGSM/BGEM external path — all material data lives inline in the shader
                        ' block. Cleared here so the comparator (and the engine at draw time)
                        ' read material from the embedded shader, not from a now-stale BGSM
                        ' lookup. Equivalent to what CK does at bake time.
                        Try
                            Dim shad = TryCast(nif.GetShader(cloned), NiflySharp.Blocks.BSShaderProperty)
                            If shad IsNot Nothing AndAlso shad.Name IsNot Nothing Then
                                shad.Name.String = ""
                            End If
                        Catch ex As Exception
                        End Try

                        ' Copy the render-resolved material into the cloned shape's inline
                        ' shader. The render has already chained TXST + MNAM-BGSM + per-NPC
                        ' tints + palette resolution to produce the FINAL textures and shader
                        ' params for this shape — we just transcribe the result into the .nif
                        ' so it ships self-contained (no external BGSM lookup). Texture slots
                        ' + a curated set of non-texture fields the MAT-DIAG showed CK actually
                        ' bakes inline (NifShaderType, Hair, SkinTint, Glowmap, EnvironmentMapping,
                        ' Alpha, AlphaTest, AlphaTestRef, HairTintColor, SkinTintColor,
                        ' BaseColor, NonOccluder). AlphaBlendMode left as the source has it
                        ' (Unknown) per user instruction — CK's normalization to None is purely
                        ' cosmetic at this point.
                        Try
                            Dim matAplicado = ApplyRenderResolvedMaterialToShape(nif, cloned, srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides, skinTintAlpha)
                            ' El material que quedo ES el que se embebe en el NIF. Si alguna de sus tres
                            ' rutas cuelga de la raiz que INVENTA la app, este NPC no se puede entregar sin
                            ' ese archivo: no lo trae ningun mod ni el juego. Se declara aca, por NPC, y
                            ' viaja con el bundle a cualquiera de los caminos de entrega.
                            AnotarSueltoInventado(matAplicado, result)
                        Catch exMat As Exception
                            ' El link al BGSM externo YA se corto (arriba, cuando se vacia shad.Name.String), asi
                            ' que el shader inline es LA LEY y quedo INDETERMINADO (`Save_To_Shader` escribe ~25
                            ' flags y 8 slots en secuencia; la derivacion de ShaderType es la ULTIMA linea del
                            ' branch). Y sin ShaderType derivado, `redirectSlotsToFaceCustomization` da False: los
                            ' 3 DDS se escriben, el NIF sigue apuntando a las texturas vanilla y el log afirma
                            ' "= comportamiento del CK". Por eso la shape SALE del NIF.
                            ' Y EL ROLLBACK VA COMPLETO. `clonedShapeNames`/`shapesCloned` se
                            ' incrementaron arriba (al clonar), ANTES de esto. Sacar la shape sin revertirlos deja
                            ' el guard F7 de mas abajo (`If shapesCloned = 0 Then Success = False`) sin
                            ' disparar y se escribe un FaceGeom VACIO con Success = True: cabeza INVISIBLE
                            ' in-game reportada como exito. Y `clonedShapeNames` sin revertir hace que otro
                            ' HDPT con el mismo destName se saltee como "duplicado" de algo que ya no existe.
                            ' `shapesFailed` es el canal que SI llega al usuario sin depender del Logger
                            ' (Summary: "N shape(s) DROPPED by an exception").
                            Dim mt = exMat.GetType().Name & ": " & exMat.Message
                            Dim dnMat = destName
                            Logger.LogLazy(Function() $"[BAKE-MAT] shape '{dnMat}' SACADA del NIF (shader inline indeterminado, link externo ya cortado): {mt}")
                            Try : nif.RemoveBlock(cloned) : Catch : End Try
                            clonedShapeNames.Remove(destName)
                            shapesCloned -= 1
                            shapesFailed += 1
                            Continue For
                        End Try

                        ' Bake de las texturas de cara, sólo para la shape Face: compone D/N/S, las encodea
                        ' con el formato DXGI del NIF fuente (BC3/BC5/BC5 + mips) bajo
                        ' Textures\Actors\Character\FaceCustomization\<plugin>\, y reapunta los slots 0/1/7
                        ' del shader clonado. En DebugMode el nombre lleva el sufijo _2 para no pisar el
                        ' artefacto del CK.
                        ' Corre con host (app) o headless-CPU (el CLI pasa host=Nothing y sin GL: sólo el
                        ' compositor CPU). El GL interno ya está gateado por needGl.
                        If hdpt.TipoDeParte() = PartTypeFace AndAlso state IsNot Nothing AndAlso BakeFaceTexturesEnabled Then
                            If isSSEBake Then
                                ' SSE bakes a single facetint _d DDS (CPU compose, no GL) to NIF slot 6, NOT
                                ' the FO4 FaceCustomization D/N/S. Uses the overlaid tints so an Edit Face tint
                                ' edit bakes WYSIWYG.
                                ' Captura para el _2c forzado (SOLO debug): head + complexion/normal ORIGINALES
                                ' ANTES de que el pass normal pueda mutar los slots (evita doble-pliegue). La captura
                                ' y el forzado son 100% CPU (fold+neutral+normal), NO tocan GL — pero el gate NO
                                ' puede ser sólo DebugMode: con Logger apagado (el caso normal de un barrido) esta
                                ' captura no ocurría nunca, sseForcedHead quedaba Nothing y el sandbox _2c/_2d —el
                                ' ÚNICO que ejercita el camino GPU de SSE— no corría (medido: reachability gate=0
                                ' sobre 451 NPCs horneados). El gate real es DebugMode OrElse WriteGPUSandboxOutput,
                                ' la MISMA compuerta que usa el consumidor más abajo (donde se lee sseForcedHead).
                                If DebugMode OrElse WriteGPUSandboxOutput Then
                                    Dim sp = GetSseHeadSlotPaths(nif, cloned)
                                    sseForcedHead = cloned : sseForcedComplexion = sp.Slot0 : sseForcedNormal = sp.Slot1 : sseForcedDetail = sp.Slot3
                                End If
                                Dim tTexS = Stopwatch.GetTimestamp()
                                WriteSseFacetintDds(nif, cloned, npcFormID, originPlugin, pluginManager, npcData, willBePacked, result, host:=host)
                                PhaseAdd(BakePhase.Textures, tTexS)
                                ' Bake RaceMenu FACE overlays into a per-NPC diffuse (slot 0). Gated + no-op for
                                ' vanilla NPCs (no face overlays) ⇒ the facetint-only path above is unchanged.
                                ' SIN host: el fold es 100% CPU y no debe poder leer nada del render.
                                WriteSseFaceDiffuseWithOverlays(nif, cloned, npcFormID, originPlugin, pluginManager, npcData, appliedPresets, willBePacked, result)

                                ' SIEMPRE, pliegue o no. Los slots 0/1/3 son relativos a Data\Textures\ y NO
                                ' pueden llevar el prefijo 'textures\'; el camino no plegado dejaba el valor crudo
                                ' de la resolución de material, que a veces YA viene prefijado, y eso daba CARA
                                ' MARRÓN (medido). Ver NormalizeSseHeadTexSetSlots. Va DESPUÉS de
                                ' las dos rutinas para normalizar también lo que ellas hayan escrito — es
                                ' idempotente, así que sobre un path ya correcto no hace nada.
                                NormalizeSseHeadTexSetSlots(nif, cloned, npcFormID)

                                ' DIAGNOSTICO DEL RESULTADO REAL, no de la intencion. Vuelca los slots del head
                                ' JUSTO COMO QUEDARON tras las dos rutinas, y si el archivo de cada uno EXISTE en
                                ' disco. Existe porque el sintoma "la primera grabada no muestra el facetint" tiene
                                ' un unico mecanismo posible: el albedo del motor es
                                ' softlight(slot0, slot6) x amp(slot3), y el fold escribe slot0 PRE-COMPENSADO
                                ' contando con que el motor re-aplique el facetint del slot 6. Si el slot 6 llega
                                ' vacio (o su .dds no esta), el motor usa su gris default = identidad y se ve el
                                ' buffer DIVIDIDO por el facetint: la cara sin tono. Con esto se distingue en UNA
                                ' corrida si el problema es el slot, el archivo, o ninguno de los dos.
                                If Logger.Enabled Then
                                    Try
                                        Dim sp6 = GetSseHeadSlotPaths(nif, cloned)
                                        Dim tsDbg As NiflySharp.Blocks.BSShaderTextureSet = Nothing
                                        Dim sprDbg = cloned.ShaderPropertyRef
                                        If sprDbg IsNot Nothing AndAlso sprDbg.Index >= 0 Then
                                            Dim lspDbg = TryCast(nif.Blocks(sprDbg.Index), NiflySharp.Blocks.BSLightingShaderProperty)
                                            If lspDbg IsNot Nothing AndAlso lspDbg.TextureSetRef IsNot Nothing AndAlso lspDbg.TextureSetRef.Index >= 0 Then
                                                tsDbg = TryCast(nif.Blocks(lspDbg.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
                                            End If
                                        End If
                                        Dim s6 = If(tsDbg IsNot Nothing AndAlso tsDbg.Textures IsNot Nothing AndAlso tsDbg.Textures.Count > 6, tsDbg.Textures(6).Content, "<sin slot 6>")
                                        Dim dp = Config_App.Current.DataPath
                                        ' EL PATH EMBEBIDO NO ES EL NOMBRE EN DISCO. En DebugMode el bake escribe
                                        ' `<id>_2.dds` pero, con willBePacked, EMBEBE el canónico `<id>.dds` (el packer
                                        ' renombra al meterlo al archive). Chequear el embebido contra el disco reporta
                                        ' "FALTA" para archivos que SÍ se escribieron — un falso positivo que casi me
                                        ' hace diagnosticar un bug inexistente. Se prueban las DOS formas.
                                        Dim onDisk = Function(embedded As String) As String
                                                         If String.IsNullOrEmpty(embedded) Then Return "(vacio)"
                                                         Dim rel = StripTexRoot(embedded)
                                                         Dim full = IO.Path.Combine(dp, "Textures", rel)
                                                         If IO.File.Exists(full) Then Return "EXISTE"
                                                         Dim dbg = IO.Path.Combine(IO.Path.GetDirectoryName(full),
                                                                                   IO.Path.GetFileNameWithoutExtension(full) & "_2" & IO.Path.GetExtension(full))
                                                         If IO.File.Exists(dbg) Then Return "EXISTE (como _2, DebugMode)"
                                                         Return "**FALTA** (ni canonico ni _2)"
                                                     End Function
                                        Dim l0 = sp6.Slot0, l1 = sp6.Slot1, l3 = sp6.Slot3, l6 = s6
                                        Dim d0 = onDisk(l0), d6 = onDisk(l6)
                                        Logger.LogLazy(Function() $"[FACEBAKE][SSE][SLOTS] npc=0x{npcFormID:X8}" & vbCrLf &
                                                                  $"    slot0 (diffuse)  = '{l0}'   -> {d0}" & vbCrLf &
                                                                  $"    slot1 (_msn)     = '{l1}'" & vbCrLf &
                                                                  $"    slot3 (detail)   = '{l3}'" & vbCrLf &
                                                                  $"    slot6 (facetint) = '{l6}'   -> {d6}")
                                    Catch exDbg As Exception
                                        Dim tDbg = exDbg.GetType().Name, mDbg = exDbg.Message
                                        Logger.LogLazy(Function() $"[FACEBAKE][SSE][SLOTS] volcado fallo: {tDbg}: {mDbg}")
                                    End Try
                                End If
                            ElseIf host IsNot Nothing OrElse Not WriteGPUSandboxOutput Then
                                ' UNA SOLA VEZ POR NPC (F4). BakeFaceTextures escribe SIEMPRE los mismos tres
                                ' nombres (<formID>_d/_msn/_s.dds) — no dependen de la shape. Con dos shapes bajo
                                ' HDPTs PartType=Face (o un source con dos shapes), la segunda pisaba los DDS de la
                                ' primera y las dos quedaban apuntando al mismo archivo: ganaba la última, en
                                ' silencio y según el orden de iteración. El REDIRECT de slots sí sigue siendo
                                ' per-shape (y sigue gateado por shader type dentro de BakeFaceTextures): lo que se
                                ' hace una sola vez es COMPONER Y ESCRIBIR.
                                If Not fo4FaceTexturesBaked Then
                                    fo4FaceTexturesBaked = True
                                    ' F3: los <id>_d/_msn/_s de un bake ANTERIOR sobreviven si este bake no produce
                                    ' alguno de los tres (p.ej. el source no tiene normal ⇒ slot 1 se saltea), y el
                                    ' packer los toma del DISCO igual: el bundle sale mezcla de dos bakes.
                                    '
                                    ' ⛔ El barrido va DESPUÉS de hornear, no antes. Borrar antes sacaba los
                                    ' archivos del mod bajo Mod Organizer —el borrado los saca del árbol virtual y
                                    ' lo que el bake escribe después es un archivo NUEVO, que cae en `overwrite`—,
                                    ' así que cada horneado se llevaba las texturas de cara fuera del mod.
                                    ' Corriendo al final se borra sólo lo que este bake NO reescribió, que es
                                    ' exactamente lo que F3 quería sacar.
                                    ' Va en Finally: si BakeFaceTextures tira, se barre igual con lo que alcanzó a
                                    ' escribir — que es la misma salida que daba el borrado previo.
                                    Dim tTexF = Stopwatch.GetTimestamp()
                                    Try
                                        BakeFaceTextures(nif, cloned, srcNif, srcShape,
                                                         hdpt, effectiveHeadPartType, applyMaterialOverrides,
                                                         npcFormID, originPlugin,
                                                         pluginManager, appliedPresets, host,
                                                         state, willBePacked, result,
                                                         lmSkinTemplateResolver, lutDataPath)
                                    Finally
                                        DeleteStaleFaceCustomizationArtifacts(npcFormID, originPlugin,
                                                                              result?.TexturasEscritas)
                                    End Try
                                    PhaseAdd(BakePhase.Textures, tTexF)
                                Else
                                    Dim dnLog = destName
                                    Logger.LogLazy(Function() $"[FACEBAKE] shape '{dnLog}': el bake de texturas FaceCustomization YA CORRIÓ para este NPC — no se recompone (los 3 DDS son per-NPC, no per-shape)")
                                End If
                            End If
                        End If

                        ' MATERIAL DIAG moved to AFTER Save_As_Manolo (disk write). Comparing
                        ' the in-memory `nif` here is misleading because Save_To_Shader writes
                        ' don't fully reflect what the serializer emits to disk. The post-save
                        ' MAT-DIAG block reloads the .nif from disk and compares against the
                        ' CK reference NIF — that's the only honest "what's on disk vs CK"
                        ' comparison.

                        ' --- ITERATION 3: bake the shape. Load the matching FBNS source on
                        ' demand, hand it (and the cloned ORIG) to FaceGenBuildPipeline.BakeShape
                        ' which (a) computes v_world by skinning the FBNS shape with the FMRS
                        ' pose-applied face skel + body skel (= what the runtime renderer
                        ' produces), then (b) writes v_baked = inv(Mtot_orig) × v_world into
                        ' the cloned ORIG so its body-only skin partition lands each vertex at
                        ' the same world position the renderer's FBNS skin path would.
                        '
                        ' The FBNS NIF is loaded only when present (HDPTs without _facebones
                        ' variant fall back to the cloned ORIG-bind vertices, no further math).
                        ' Chargen TRI morphs are folded into v_world inside ComputeWorldVerticesForShape.
                        If bakeState IsNot Nothing AndAlso faceBonesKey <> "" Then
                            Dim fbnsNif As Nifcontent_Class_Manolo = Nothing
                            If Not loadedSources.TryGetValue(faceBonesKey, fbnsNif) Then
                                Dim fbnsBytes As Byte() = Nothing
                                Try
                                    fbnsBytes = FilesDictionary_class.GetBytes(faceBonesKey)
                                Catch ex As Exception
                                    ' F8: catch vacio. Sin FBNS la shape se escribe SIN morphear (cabeza neutra) y
                                    ' hasta aca no lo decia nadie: el contador shapesFbnsUnmatched sólo cubre el caso
                                    ' "cargo pero no matcheo", no "no se pudo leer".
                                    Dim fkE = faceBonesKey, tE = ex.GetType().Name, msgE = ex.Message
                                    Logger.LogLazy(Function() $"[FACEGEN-FBNS] no se pudo leer '{fkE}': {tE}: {msgE} — la shape se escribe SIN morphear")
                                End Try
                                If fbnsBytes IsNot Nothing AndAlso fbnsBytes.Length > 0 Then
                                    fbnsNif = New Nifcontent_Class_Manolo()
                                    Try
                                        fbnsNif.Load_Manolo(fbnsBytes)
                                        loadedSources(faceBonesKey) = fbnsNif
                                    Catch ex As Exception
                                        fbnsNif = Nothing
                                    End Try
                                End If
                            End If
                            If fbnsNif IsNot Nothing Then
                                ' Match the FBNS shape to the ORIG source shape. Vanilla naming
                                ' convention (verified empirically for Alijo): ORIG='<base>:N',
                                ' FBNS='<base>_faceBones:N'. We try (in order):
                                '   (1) exact name match (modded NIFs sometimes share name);
                                '   (2) "<base>_faceBones:N" insertion;
                                '   (3) single-shape FBNS NIF → use it (dominant face HDPT path).
                                Dim fbnsShapes = fbnsNif.GetShapes().ToList()
                                Dim fbnsShape As INiShape = Nothing
                                For Each fs In fbnsShapes
                                    If String.Equals(If(fs.Name?.String, ""), sourceName, StringComparison.OrdinalIgnoreCase) Then
                                        fbnsShape = fs : Exit For
                                    End If
                                Next
                                If fbnsShape Is Nothing Then
                                    ' Tier 2, AHORA IGUAL AL CK. FUENTE: CreationKit.exe 0x14093C030 busca
                                    ' "_faceBones" (string @RVA 0x3017F30) como SUBSTRING CASE-INSENSITIVE en
                                    ' CUALQUIER POSICIÓN del nombre de la shape FBNS, y hace un splice de 10
                                    ' chars (constante @RVA 0x3B9DE50) para recuperar el nombre base; compara
                                    ' ESE resultado contra el nombre de la shape ORIG.
                                    ' Lo anterior CONSTRUÍA el nombre esperado insertando "_faceBones" justo
                                    ' antes del ":N" final — o sea, sólo reconocía UNA posición. Cualquier NIF
                                    ' cuyo sufijo no fuera exactamente ':N' (o que llevara el token en otro
                                    ' lado) no matcheaba, aunque el CK sí lo matchea.
                                    Const FaceBonesToken As String = "_faceBones"
                                    For Each fs In fbnsShapes
                                        Dim fsName As String = If(fs.Name?.String, "")
                                        Dim tokIdx = fsName.IndexOf(FaceBonesToken, StringComparison.OrdinalIgnoreCase)
                                        If tokIdx < 0 Then Continue For
                                        Dim spliced = fsName.Remove(tokIdx, FaceBonesToken.Length)
                                        If String.Equals(spliced, sourceName, StringComparison.OrdinalIgnoreCase) Then
                                            fbnsShape = fs : Exit For
                                        End If
                                    Next
                                End If
                                ' Tier 3 ("si el FBNS tiene una sola shape, usala") — SIN CONTRAPARTE EN EL CK:
                                ' el 0x14093C030 sólo hace el match por nombre de arriba, no tiene fallback por
                                ' cardinalidad. Sólo puede SOBRE-matchear (emparejar shapes que el CK dejaría sin
                                ' morphear). Medido: 0 de 501 casos vanilla lo usan ⇒ no aporta cobertura real.
                                ' Se DEJA por ahora para no cambiar dos cosas a la vez en la misma corrida, pero
                                ' ahora es visible: cuando dispara, se loguea como tier3 (ver abajo).
                                Dim usedTier3 As Boolean = False
                                If fbnsShape Is Nothing AndAlso fbnsShapes.Count = 1 Then
                                    fbnsShape = fbnsShapes(0)
                                    usedTier3 = True
                                End If
                                If fbnsShape IsNot Nothing Then
                                    If usedTier3 AndAlso Logger.Enabled Then
                                        Dim shNameT3 = sourceName
                                        Logger.LogLazy(Function() $"[FACEGEN-FBNS] tier3 (single-shape fallback, NO tiene contraparte en el CK) npc=0x{npcFormID:X8} shape='{shNameT3}' fbns='{faceBonesKey}'")
                                    End If
                                    Dim tMs = Stopwatch.GetTimestamp()
                                    Dim baked = FaceGenBuildPipeline.BakeShape(bakeState, nif, cloned, fbnsNif, fbnsShape, hdpt.ArchivoDeDeformacion(2UI), srcNif:=srcNif, srcShape:=srcShape, raceMorphTriPath:=hdpt.ArchivoDeDeformacion(0UI))
                                    PhaseAdd(BakePhase.MorphSkin, tMs)
                                    If baked Then
                                        shapesMorphed += 1
                                    End If
                                Else
                                    ' CAÍDA SILENCIOSA — ya no. El FBNS cargó pero ninguna de sus shapes matcheó,
                                    ' así que esta shape se escribe SIN morphear y el batch la contaba como éxito.
                                    ' Se contabiliza y se registra FormID + shape + los nombres candidatos.
                                    shapesFbnsUnmatched += 1
                                    If Logger.Enabled Then
                                        Dim shNameU = sourceName
                                        Dim fbKeyU = faceBonesKey
                                        Dim candNames = String.Join(",", fbnsShapes.Select(Function(f) If(f.Name?.String, "")))
                                        Logger.LogLazy(Function() $"[FACEGEN-FBNS] SIN MATCH — shape escrita SIN morphear. npc=0x{npcFormID:X8} shape='{shNameU}' fbns='{fbKeyU}' fbnsShapes=[{candNames}]")
                                    End If
                                End If
                            End If
                        ElseIf bakeState IsNot Nothing Then
                            ' Morph por vertice del mesh neutro. Entran LOS DOS JUEGOS: SSE no tiene rig
                            ' _faceBones / FMRS / skin-rebind, asi que su cabeza es un morph puro y la rama
                            ' BakeShape de arriba nunca corre; FO4 entra cuando la shape no tiene _faceBones.nif
                            ' (p.ej. las cabezas infantiles). ⛔ Sin esto se escribia NEUTRA en silencio y el
                            ' batch la contaba como exito.
                            ' El mesh-tri es el SkinnyMorph (morph de PESO del actor) y su fuente autoritativa y
                            ' UNICA es HDPT NAM0=1: el CK lo aplica solo si el record lo declara. ⛔ No adivinarlo
                            ' por basename del NIF (el .tri no siempre comparte nombre, y adivinar aplica tris que
                            ' el CK ignora, sobre-morpheando el pelo). ⛔ Es ley de SSE y solo se pasa ahi: en FO4
                            ' el peso corporal lo hornea otro mecanismo (MWGT como escala en los huesos *_skin) y
                            ' pasarlo aca lo aplicaria DOS veces.
                            Dim hdptMeshTri As String = If(isSSEBake, hdpt.ArchivoDeDeformacion(1UI), Nothing)
                            Dim tMv = Stopwatch.GetTimestamp()
                            FaceGenBuildPipeline.ApplyChargenMorphsInPlace(nif, cloned, hdpt.ArchivoDeDeformacion(2UI), hdpt.ArchivoDeDeformacion(0UI), bakeState, hdptMeshTri)
                            PhaseAdd(BakePhase.MorphSkin, tMv)
                            shapesMorphed += 1
                            If Not isSSEBake Then shapesFo4NoFbnsMorphed += 1
                        ElseIf Not isSSEBake Then
                            ' CAIDA SILENCIOSA que queda: FO4, sin `_faceBones` Y sin bakeState ⇒ la shape se
                            ' escribe NEUTRA y nadie se entera. Se cuenta y se loguea, igual que shapesFbnsUnmatched.
                            shapesFo4NoFbnsNoMorph += 1
                            If Logger.Enabled Then
                                Dim shNameNM = sourceName
                                Logger.LogLazy(Function() $"[FACEGEN-FBNS] FO4 sin _faceBones y sin bakeState — shape escrita SIN morphear. npc=0x{npcFormID:X8} shape='{shNameNM}'")
                            End If
                        End If
                    End If
                Catch ex As Exception
                    ' CATCH VACIO -> ya no. Este Try envuelve el clone + la resolucion de material + el bake de
                    ' texturas + BakeShape: se tragaba la caida de una shape ENTERA sin contador ni log, y el batch
                    ' la reportaba como exito. Mismo tratamiento que shapesFbnsUnmatched (que nacio del mismo
                    ' modo de fallo): se cuenta, se loguea con FormID + shape, y sube al Summary.
                    shapesFailed += 1
                    Dim dnErr = destName, tErr = ex.GetType().Name, mErr = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE] shape '{dnErr}' DESCARTADA por excepcion (npc=0x{npcFormID:X8}): {tErr}: {mErr}")
                End Try
            Next
            hdptProcessed += 1
        Next

        ' El peso del actor (NAM7) llega a las head parts como un MORPH plano, no como skinning:
        '   value = 1 - NAM7/100  →  BSFaceGenNiNode::ApplyMorph(type=3 "Custom Morph", index=0, value)
        '   type 3 / index 0 == el canal "SkinnyMorph"; deltas del .tri NAM0=1 del PROPIO head part.
        ' Ya implementado en NpcMorphResolver (canal SkinnyMorph); verificado contra el CK sobre pelo.
        ' Las BARBAS (NAM0=0/2) conservan un residual ~0,067 que está PROBADO que NO es combinación
        ' lineal de sus morphs ni transformación afín, y es independiente del NPC. Es otro mecanismo del
        ' CK, no un canal que falte: NO intentar cerrarlo con morphs ni con heurísticas.

        ' El CK comparte un BSShaderTextureSet cuando coincide el MATERIAL, no solo las rutas: el dueno del
        ' texture set es el BSLightingShaderMaterial, asi que su cache se indexa por el payload completo (los 8
        ' paths + emissive color/multiple, alpha, refraction, glossiness, specular color y strength, mas el
        ' clamp mode). Las shader FLAGS, el nombre y el controller viven en el shader property y NO entran.
        ' Derivado de datos (75 FaceGeom del CK, exigiendo reproducir el grafo de sharing exacto): solo los 8
        ' paths reproducen 47/75; paths + payload de material, 75/75; agregando las flags, 36/75 (o sea que las
        ' flags NO entran). De los 28 pares que el CK dejo separados pese a tener los 8 paths identicos, 28/28
        ' difieren exclusivamente en specularStrength.
        ' ⛔ NO clonar un texture set por shape: el sharing es DELIBERADO. Agregar un campo que falte solo puede
        ' SEPARAR lo que hoy se mergea, que es la direccion segura.
        Try
            Dim seenTexset As New Dictionary(Of String, Integer)()
            For Each sh In nif.NifShapes.ToList()
                Dim lsp = TryCast(nif.GetShader(sh), NiflySharp.Blocks.BSLightingShaderProperty)
                If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then Continue For
                Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
                If ts Is Nothing OrElse ts.Textures Is Nothing Then Continue For
                ' HairTintColor va en la clave: es la cola específica del shader type Hair Tint y forma parte
                ' del material. MEDIDO sobre los pares que el CK dejó SEPARADOS teniendo los 8 paths Y el resto
                ' del material idénticos (9 NPCs argonianos, ej. 0001412E 'HairArgonianMale07' vs
                ' 'HairArgonianMale07Hairline'): el ÚNICO campo que difiere es HairTintColor
                ' (0,290196/0,270588/0,380392 vs 0,211765/0,274510/0,376471). Sin él los mergeábamos.
                ' El formato "R" (round-trip) es exacto a nivel bit — necesario porque en esos mismos pares
                ' SpecularColor difiere en 1 ULP y cualquier redondeo los volvería a colapsar.
                Dim matKey = String.Join(";",
                    $"{lsp.EmissiveColor.R:R},{lsp.EmissiveColor.G:R},{lsp.EmissiveColor.B:R}",
                    $"{lsp.EmissiveMultiple:R}",
                    $"{lsp.Alpha:R}",
                    $"{lsp.RefractionStrength:R}",
                    $"{lsp.Glossiness:R}",
                    $"{lsp.SpecularColor.R:R},{lsp.SpecularColor.G:R},{lsp.SpecularColor.B:R}",
                    $"{lsp.SpecularStrength:R}",
                    $"{lsp.HairTintColor.R:R},{lsp.HairTintColor.G:R},{lsp.HairTintColor.B:R}",
                    $"clamp={CInt(lsp.TextureClampMode)}")
                Dim key = String.Join("|", ts.Textures.Select(Function(t) If(t?.Content, "").ToLowerInvariant())) & "||" & matKey
                Dim canonIdx As Integer
                If seenTexset.TryGetValue(key, canonIdx) Then
                    lsp.TextureSetRef = New NiflySharp.NiBlockRef(Of NiflySharp.Blocks.BSShaderTextureSet) With {.Index = canonIdx}
                Else
                    seenTexset(key) = lsp.TextureSetRef.Index
                End If
            Next
        Catch ex As Exception
            ' F8: catch vacio. Si el dedup tira, el NIF sale con MAS BSShaderTextureSet de los que escribe el CK
            ' (medido: la clave vieja dejaba 41% de los NPC con uno de menos; esta rama es el fallo simetrico).
            ' No rompe el render, pero desvia del CK y el comparator lo va a marcar sin explicar por que.
            Dim tT = ex.GetType().Name, mT = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE] el dedup de BSShaderTextureSet fallo (el NIF puede quedar con texture-sets de mas): {tT}: {mT}")
        End Try

        ' Drop any blocks left orphan after the strip+clone passes (e.g. the baked shell's
        ' shader properties / texture sets that were rooted only by the now-removed shapes).
        Try
            nif.RemoveUnreferencedBlocks()
        Catch ex As Exception
            ' F8: catch vacio. Falla => quedan bloques huerfanos en el NIF (mas grande y distinto del CK).
            Dim tR1 = ex.GetType().Name, mR1 = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE] RemoveUnreferencedBlocks (pre-reparent) fallo: {tR1}: {mR1}")
        End Try

        ' --- FaceGen shell parity (Fase 1): los shapes deben colgar de un NiNode
        ' 'BSFaceGenNiNodeSkinned' (Flags 0x0E=14, identidad — verificado con byte-compare vs BA2,
        ' 88/88; NO 0x2000000E, que trae de más el bit 0x20000000 — mismo defecto que tenía el root),
        ' NO directo del root. El root ya es
        ' NiNode "" (creado arriba). Sin esta capa el FaceGen LOOSE no renderiza la
        ' cabeza (el engine FaceGen exige la geometría skinneada bajo ese nodo). Los huesos (NiNode)
        ' quedan como hijos directos del root, igual que CK.
        ' Corre DESPUÉS de RemoveUnreferencedBlocks para operar sobre índices de bloque ya finales.
        Try
            Dim faceGenRoot = nif.GetRootNode()
            If faceGenRoot IsNot Nothing AndAlso faceGenRoot.Children IsNot Nothing Then
                Dim skinnedNode As New NiflySharp.Blocks.NiNode() With {
                    .Name = New NiflySharp.NiStringRef("BSFaceGenNiNodeSkinned"),
                    .Flags_ui = &HEUI,
                    .Rotation = New NiflySharp.Structs.Matrix33 With {.M11 = 1.0F, .M22 = 1.0F, .M33 = 1.0F}
                }
                Dim skinnedIdx = nif.AddBlock(skinnedNode)

                Dim boneChildIdx As New List(Of Integer)
                Dim shapeChildIdx As New List(Of Integer)
                ' Race height (RACE.DATA Female/MaleHeight, ya parseado en bakeState.Race). CK escala
                ' las TRANSLATIONS de los nodos de hueso por este factor (female ≈ 0.98). Solo a los
                ' nodos de referencia: la geometría queda ×1.0 (la escala real la aplica el motor al
                ' actor en runtime; hornearla en la malla la dejaría doble-escalada). Verificado vs CK:
                ' nodos female = base × 0.98, geo ×1.0, bind ×1.0.
                Dim raceHeight As Single = 1.0F
                If bakeState IsNot Nothing AndAlso bakeState.Race IsNot Nothing Then
                    ' MaleHeight/FemaleHeight: mismo campo, cada juego lo declara con su propio subrecord.
                    Dim rh As Single
                    Dim nfHeight = TryCast(bakeState.Race, Canon.RaceFO4)
                    If nfHeight IsNot Nothing Then
                        rh = If(bakeState.IsFemale, nfHeight.DataFemaleHeight, nfHeight.DataMaleHeight)
                    Else
                        Dim nsseHeight = TryCast(bakeState.Race, Canon.RaceSSE)
                        rh = If(nsseHeight Is Nothing, 0.0F,
                                If(bakeState.IsFemale, nsseHeight.FemaleHeight, nsseHeight.MaleHeight))
                    End If
                    If rh > 0.0F Then raceHeight = rh
                End If

                ' Build the set of bone block indices that SOME BSSkin::Instance references
                ' (either as .Bones[i] or as .SkeletonRoot). Used below to drop bone NiNodes
                ' that ended up orphaned after the strip+clone passes (e.g. MaleEyes.nif's
                ' look-at dummy 'EyeLeftDummy001', or 'GhoulTearDuct' after we dropped its
                ' shape via the unknown-bone filter). Mirrors CK behaviour.
                Dim referencedBones As New HashSet(Of Integer)
                For Each anyBlk In nif.Blocks
                    Dim si = TryCast(anyBlk, NiflySharp.Blocks.BSSkin_Instance)
                    If si IsNot Nothing Then
                        If si.Bones IsNot Nothing Then
                            For bi As Integer = 0 To si.Bones.Count - 1
                                Dim bRef = si.Bones.GetBlockRef(bi)
                                If bRef >= 0 Then referencedBones.Add(bRef)
                            Next
                        End If
                        If si.SkeletonRoot IsNot Nothing AndAlso si.SkeletonRoot.Index >= 0 Then
                            referencedBones.Add(si.SkeletonRoot.Index)
                        End If
                    End If
                    ' SSE: skin es NiSkinInstance / BSDismemberSkinInstance (hereda de NiSkinInstance),
                    ' no BSSkin::Instance. Sin esto referencedBones queda vacio y el guard dropea todos
                    ' los huesos. Bones = NiBlockPtrArray<NiNode>, SkeletonRoot = NiBlockPtr<NiNode>.
                    Dim niSi = TryCast(anyBlk, NiflySharp.Blocks.NiSkinInstance)
                    If niSi IsNot Nothing Then
                        If niSi.Bones IsNot Nothing Then
                            For bi As Integer = 0 To niSi.Bones.Count - 1
                                Dim bRef = niSi.Bones.GetBlockRef(bi)
                                If bRef >= 0 Then referencedBones.Add(bRef)
                            Next
                        End If
                        If niSi.SkeletonRoot IsNot Nothing AndAlso niSi.SkeletonRoot.Index >= 0 Then referencedBones.Add(niSi.SkeletonRoot.Index)
                    End If
                Next

                Dim droppedOrphanBones As Integer = 0
                Dim normalizedNoAnimSync As Integer = 0
                For Each childIdx In faceGenRoot.Children.Indices.ToList()
                    Dim childBlk = nif.GetBlock(childIdx)
                    If TypeOf childBlk Is INiShape Then
                        shapeChildIdx.Add(childIdx)
                        Dim triShape = TryCast(childBlk, NiflySharp.Blocks.BSTriShape)
                        If triShape IsNot Nothing Then
                            ' BoundingSphere: FO4 CK deja la esfera en (0,0,0,0) (el engine la computa del
                            ' skinned desde los huesos). SSE CK, en cambio, COPIA el bounds real del head base
                            ' (verificado --ckdelta: CK==base, non-zero). Game-aware: solo FO4 pone cero.
                            If Not isSSEBake Then
                                triShape.Bounds = New NiflySharp.Structs.BoundingSphere(System.Numerics.Vector3.Zero, 0.0F)
                            End If
                            ' skin.SkeletonRoot → BSFaceGenNiNodeSkinned. FO4 = BSSkin::Instance;
                            ' SSE = NiSkinInstance / BSDismemberSkinInstance (hereda). Game-aware.
                            Dim skinRef = triShape.SkinInstanceRef
                            If skinRef IsNot Nothing AndAlso skinRef.Index >= 0 AndAlso skinRef.Index < nif.Blocks.Count Then
                                Dim skBlk = nif.Blocks(skinRef.Index)
                                Dim si = TryCast(skBlk, NiflySharp.Blocks.BSSkin_Instance)
                                If si IsNot Nothing Then
                                    si.SkeletonRoot = New NiflySharp.NiBlockPtr(Of NiflySharp.Blocks.NiAVObject)(skinnedIdx)
                                Else
                                    Dim niSi = TryCast(skBlk, NiflySharp.Blocks.NiSkinInstance)
                                    If niSi IsNot Nothing Then
                                        niSi.SkeletonRoot = New NiflySharp.NiBlockPtr(Of NiflySharp.Blocks.NiNode)(skinnedIdx)
                                    End If
                                    ' PF_EDITOR_VISIBLE (bit 0 de BSPartFlag): el CK lo ASSERTA, no lo preserva.
                                    ' MEDIDO sobre el corpus SSE entero contra los 3215 FaceGeom del CK:
                                    '   mallas FUENTE (482 head parts): bit0 apagado en 463/590 (78,5 %)
                                    '   CK  : 22.787/22.787 con bit0  (11.697 en `1` + 11.090 en `257`)
                                    '   app : 162/22.787   — preservábamos el de la fuente
                                    ' Es un OR del bit 0 y NADA MÁS: el bit 8 (PF_START_NET_BONESET) ya coincide
                                    ' con el CK en el 100 % (11.697 sin él + 11.090 con él, exacto), así que forzar
                                    ' el default 257 del esquema sería un ERROR — le pondría el bit 8 a 11.697
                                    ' particiones donde el CK escribe 1.
                                    ' Por qué no hace falta gate por juego además del TryCast: BSDismemberSkinInstance
                                    ' es de Skyrim; FO4 usa BSSkin::Instance, que NO tiene particiones (cero bloques de
                                    ' este tipo en los 1508 FaceGeom de FO4). El gate real es el tipo. Se deja igual el
                                    ' isSSEBake para que la intención quede escrita y nadie lo "generalice".
                                    ' Es paridad, no un fix: nif.xml lo documenta como "Editor flags … Visible in Editor".
                                    If isSSEBake Then
                                        Dim dsi = TryCast(skBlk, NiflySharp.Blocks.BSDismemberSkinInstance)
                                        If dsi IsNot Nothing AndAlso dsi.Partitions IsNot Nothing Then
                                            For pi As Integer = 0 To dsi.Partitions.Count - 1
                                                ' BodyPartList es una STRUCT dentro de un List: mutar la copia no
                                                ' escribe nada, hay que reasignar por índice.
                                                Dim bp = dsi.Partitions(pi)
                                                bp.PartFlag = bp.PartFlag Or NiflySharp.Enums.BSPartFlag.PF_EDITOR_VISIBLE
                                                dsi.Partitions(pi) = bp
                                            Next
                                        End If
                                    End If
                                End If
                            End If
                        End If
                    Else
                        ' Bone node (flat child of root). Paridad CK:
                        '  - race height: escalar SOLO la translation del nodo por raceHeight (ver arriba).
                        '    Geometría y bind intactos.
                        ' NO renombrar "HEAD"→"Head". CK MANTIENE "HEAD" (mayúscula) en el
                        ' nodo y en BSSkin::Instance.bones — verificado con NiflySharp contra los FaceGen del
                        ' BA2 (47/47 = "HEAD"). Ojo: la creencia "CK normaliza a Head, igualar por paridad byte"
                        ' es FALSA (salió de refs contaminadas; ver 10-stack-arnes-de-medicion) — no reintroducir ese renombre.
                        ' La fuente del esqueleto ya es "HEAD" = CK → lo dejamos intacto. El skin referencia el
                        ' nodo por puntero, así que con NO renombrar quedan iguales el nombre del nodo Y la ref.
                        Dim boneNode = TryCast(childBlk, NiflySharp.Blocks.NiNode)
                        ' Orphan-bone guard: drop this bone NiNode from root.Children iff
                        '   - no BSSkin::Instance references it (Bones[] or SkeletonRoot)
                        '   - it has no children of its own (no subtree depends on it)
                        ' Conservative: any extra reference and we keep it. The post-reparent
                        ' RemoveUnreferencedBlocks call (below) actually evicts the block.
                        If boneNode IsNot Nothing _
                           AndAlso Not referencedBones.Contains(childIdx) _
                           AndAlso (boneNode.Children Is Nothing OrElse boneNode.Children.Count = 0) Then
                            Dim bNameLog = If(boneNode.Name?.String, "")
                            Logger.LogLazy(Function() $"[FACEBAKE] dropping orphan bone NiNode('{bNameLog}'): no skin references it, no children")
                            droppedOrphanBones += 1
                            Continue For
                        End If
                        boneChildIdx.Add(childIdx)
                        If boneNode IsNot Nothing Then
                            If raceHeight <> 1.0F Then
                                boneNode.Translation = boneNode.Translation * raceHeight
                            End If
                            ' El bit "No Anim Sync (S)" se LIMPIA en los nodos de hueso del FaceGeom, para
                            ' igualar al CK, que lo normaliza SIEMPRE. Divergía porque el arte fuente de
                            ' Bethesda lo trae inconsistente y nosotros heredábamos el de la malla que aportara
                            ' el nodo.
                            ' Es seguro: el FaceGeom NO es el esqueleto de animación — el motor re-skinea con el
                            ' del personaje. Si el flag hiciera algo acá, el CK lo estaría destruyendo en TODAS
                            ' las cabezas vanilla. En FO4 es no-op medido: el bit no existe.
                            ' ⛔ EL BIT LO DECLARA `HkxPoseImportSession.BitDeNoAnimSync`, no una Const local.
                            Dim NoAnimSyncSBit As UInteger = HkxPoseImportSession.BitDeNoAnimSync.S
                            If (boneNode.Flags_ui And NoAnimSyncSBit) <> 0UI Then
                                boneNode.Flags_ui = boneNode.Flags_ui And (Not NoAnimSyncSBit)
                                normalizedNoAnimSync += 1
                            End If
                        End If
                    End If
                Next

                ' root.Children = huesos + BSFaceGenNiNodeSkinned ; skinnedNode.Children = los shapes
                boneChildIdx.Add(skinnedIdx)
                faceGenRoot.Children.SetIndices(boneChildIdx)
                skinnedNode.Children.SetIndices(shapeChildIdx)
                Logger.LogLazy(Function() $"[FACEBAKE] reparent OK: {shapeChildIdx.Count} shapes bajo BSFaceGenNiNodeSkinned, {boneChildIdx.Count - 1} huesos en root, {droppedOrphanBones} huesos huerfanos descartados, {normalizedNoAnimSync} nodos con NoAnimSync(S) normalizado a 0")
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE] reparent BSFaceGenNiNodeSkinned FAILED: {ex.GetType().Name}: {ex.Message}")
        End Try

        ' Second pass to evict the now-unreferenced orphan bone blocks (the loop above
        ' only removed them from root.Children; they still sit in nif.Blocks). This is
        ' the same idempotent helper called pre-reparent.
        Try
            nif.RemoveUnreferencedBlocks()
        Catch ex As Exception
            ' F8: catch vacio. Este es el pase que EVICTA los huesos huerfanos que el reparent saco de
            ' root.Children; si falla, esos NiNode siguen en nif.Blocks y el NIF diverge del CK.
            Dim tR2 = ex.GetType().Name, mR2 = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE] RemoveUnreferencedBlocks (post-reparent) fallo, quedan huesos huerfanos: {tR2}: {mR2}")
        End Try

        ' SSE HDT-SMP: el vínculo físico del pelo —NiStringExtraData "HDT Skinned Mesh Physics Object",
        ' cuyo StringData es la ruta al XML de física— cuelga del ROOT del NIF fuente, no de la shape. El
        ' shell se reconstruye desde cero, así que CloneShape_Original (que solo preserva el extradata de la
        ' SHAPE) no lo trae y el motor nunca carga el XML → el pelo pierde la física SMP. Lo re-emitimos en
        ' el root horneado desde cada parte fuente que lo traiga; el helper es idempotente (pelo + hairline
        ' apuntan al mismo XML → se agrega una sola vez) y filtra por nombre (no toca BODYTRI). El nombre de
        ' shape ya coincide con el tag del XML porque el mod nombra el HDPT.EditorID == shape == per-vertex-shape
        ' (p.ej. KSSMP_Amor) y el bake renombra a EditorID. El XML NO se copia: ruta fija ya instalada. Solo
        ' SSE (FO4 no usa HDT-SMP; el helper es no-op si el source no trae el bloque). Corre tras el
        ' RemoveUnreferencedBlocks final, con el root ya finalizado, justo antes de guardar.
        If isSSEBake Then
            For Each srcNifForSmp In loadedSources.Values
                Try
                    nif.TransferRootSmpExtraDataFrom(srcNifForSmp)
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE] SMP root extradata transfer failed: {ex.GetType().Name}: {ex.Message}")
                End Try
            Next

            ' ⛔ EL RENOMBRE PUEDE MATAR LA FISICA, EN SILENCIO. El XML liga por NOMBRE DE SHAPE
            ' (<per-vertex-shape name="X">), y este bake renombra cada shape a `hdpt.EditorID`. Que eso
            ' coincida NO es una ley: se midio en UN mod (KS Hairdos, donde el autor nombro
            ' HDPT.EditorID == shape == tag). Otro mod que no lo haga sale con el link intacto, el motor
            ' carga el XML, no encuentra la shape, y el pelo queda duro sin UN SOLO error.
            ' Aca no se arregla el nombre -eso seria pisar la decision del autor y romper la paridad con
            ' el CK-, se AVISA, que es lo unico honesto: el que hornea puede renombrar el tag del XML.
            Try
                Dim rutaSmp = nif.TryGetSmpPhysicsXmlPath()
                If Not String.IsNullOrWhiteSpace(rutaSmp) Then
                    Dim xml = SmpPhysicsXml.LeerPorRutaRelativa(SmpPhysicsXml.SinPrefijoData(rutaSmp))
                    Dim parseo As Boolean = False
                    Dim pedidos = SmpPhysicsXml.NombresDeShape(xml, parseo)
                    If Not parseo Then
                        Dim rutaL = rutaSmp
                        Logger.LogLazy(Function() $"[FACEBAKE-SMP] no pude leer/parsear el XML '{rutaL}': no puedo " &
                                                  "verificar que los nombres de shape sigan coincidiendo.")
                    ElseIf pedidos.Count > 0 Then
                        Dim presentes = New HashSet(Of String)(
                            nif.NifShapes.Select(Function(sh) If(sh.Name?.String, "")), StringComparer.OrdinalIgnoreCase)
                        Dim faltan = pedidos.Where(Function(x) Not presentes.Contains(x)).ToList()
                        If faltan.Count = pedidos.Count Then
                            Dim rutaL = rutaSmp, faltanL = String.Join(", ", faltan), hayL = String.Join(", ", presentes)
                            Logger.LogLazy(Function() $"[FACEBAKE-SMP] ⚠ LA FISICA VA A QUEDAR MUERTA: el XML '{rutaL}' " &
                                                      $"referencia {faltanL} y el NIF horneado no tiene NINGUNA de esas " &
                                                      $"shapes (tiene: {hayL}). El rename del bake rompio el vinculo por nombre.")
                        ElseIf faltan.Count > 0 Then
                            Dim rutaL = rutaSmp, faltanL = String.Join(", ", faltan)
                            Logger.LogLazy(Function() $"[FACEBAKE-SMP] ⚠ el XML '{rutaL}' referencia shapes que el NIF " &
                                                      $"horneado no tiene: {faltanL}. Esa parte de la fisica no va a correr.")
                        End If
                    End If
                End If
            Catch ex As Exception
                Logger.LogLazy(Function() $"[FACEBAKE-SMP] la verificacion de nombres fallo: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If

        result.ShapesKept = shapesCloned
        ' F7: era `= 0` HARDCODEADO, contra su propia doc ("shapes dropeadas"). Ahora es la suma real de todo lo
        ' que se cayo en el camino: HDPT sin mesh o cuyo source no carga, duplicados, y shapes que reventaron.
        result.ShapesDropped = hdptSourceMissing + hdptSourceLoadFail + shapesSkippedDup + shapesFailed

        ' F7: NO ESCRIBIR UN NIF VACIO CON Success=True. Los dos guards previos (raza sin FaceGen, hdptMap
        ' vacio) corren ANTES del loop; si todas las shapes se caen DENTRO (source ausente, filtro de huesos, una
        ' excepcion), shapesCloned queda en 0 y hasta aca se guardaba igual un FaceGeom sin geometria, se reportaba
        ' Success=True, y el packer lo metia al BSA. In-game eso es una cabeza INVISIBLE. Es un FALLO, no un skip:
        ' el NPC SI tenia head parts (por eso paso el guard de hdptMap) y no pudimos construirlos.
        If shapesCloned = 0 Then
            result.Success = False
            result.Summary = $"Could not build ANY shape for this NPC ({hdptProcessed} HDPT processed): " &
                             $"{hdptSourceMissing} with no mesh/source, {hdptSourceLoadFail} that failed to load, " &
                             $"{shapesSkippedDup} duplicated, {shapesFailed} with an exception. The .NIF is NOT written " &
                             "(an empty FaceGeom leaves the head invisible in-game). See the [FACEBAKE] log."
            Logger.LogLazy(Function() $"[FACEBAKE] ABORT npc=0x{npcFormID:X8}: 0 shapes clonadas, no se escribe el NIF")
            Return result
        End If

        ' Output path:
        '   DebugMode=False (default): <formID>.nif → pisa el CK bake; engine usa este al cargar.
        '   DebugMode=True: <formID>_2.nif → sandbox al lado del CK bake, sin pisar; engine
        '                   sigue usando el CK; el comparator diff-ea against CK BA2 baseline.
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Dim dataPathForNif = BakeOutputRoot.Current()
        If String.IsNullOrEmpty(dataPathForNif) Then
            result.Summary = "DataPath unset; cannot write .nif"
            Return result
        End If
        ' Extension uppercase ".NIF" to match CK vanilla exactly (CK writes <FormID>.NIF). Cosmetic
        ' on Windows (case-insensitive FS) but removes it as a variable while we chase the loose bug.
        Dim nifFileName = FaceGenPaths.GeomNifFileName(formIdLow, If(DebugMode, "_2", ""))
        Dim outAbs = Path.Combine(dataPathForNif, FaceGenPaths.GeomDir(originPlugin), nifFileName)
        Try
            Directory.CreateDirectory(Path.GetDirectoryName(outAbs))
            Dim tWrite = Stopwatch.GetTimestamp()
            nif.Save_As_Manolo(outAbs, Overwrite:=True)
            PhaseAdd(BakePhase.NifWrite, tWrite)
        Catch ex As Exception
            result.Summary = $"Failed to write {nifFileName}: {ex.Message}"
            Return result
        End Try

        ' === SANDBOX FORZADO _2c (SSE, SOLO debug) ===: tras el _2.NIF, fuerza el replacer COMPLETO _d/_n
        ' (pliegue + neutralizar slot 6 + normal) AUNQUE el NPC no tenga tints ni overlays, sobre el complexion y
        ' el normal ORIGINALES (capturados antes del pass normal, asi que no hay doble pliegue), y salva un
        ' FaceGeom _2c.NIF paralelo. No toca el _2/_2b y es 100 % CPU.
        ' El `OrElse WriteGPUSandboxOutput` importa: DebugMode ES Logger.Enabled y el barrido corre con el Logger
        ' APAGADO a proposito, asi que este bloque -el UNICO que ejercita el camino GPU de SSE y por lo tanto el
        ' unico que puede medir su paridad CPU/GPU- no se ejecutaba NUNCA en un barrido, y el instrumento
        ' reportaba "0 comparable slots" no porque coincidiera sino porque no habia corrido nada.
        If isSSEBake AndAlso (DebugMode OrElse WriteGPUSandboxOutput) AndAlso sseForcedHead IsNot Nothing Then
            Try
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ENTER: complexion='{sseForcedComplexion}' normal='{sseForcedNormal}'")
                ' result:=Nothing — el _2c es un SANDBOX de debug: sus fallos no son fallos del bake real y no
                ' deben contarse en TextureSlotsFailed (RecordTextureFailure ya null-guardea).
                WriteSseFaceDiffuseWithOverlays(nif, sseForcedHead, npcFormID, originPlugin, pluginManager, npcData,
                                                appliedPresets, willBePacked:=False, result:=Nothing, forcedSuffix:="_2c",
                                                complexionPathOverride:=sseForcedComplexion, normalPathOverride:=sseForcedNormal,
                                                detailPathOverride:=sseForcedDetail)
                Dim nif2c = Path.Combine(dataPathForNif, FaceGenPaths.GeomDir(originPlugin),
                                         FaceGenPaths.GeomNifFileName(formIdLow, "_2c"))
                nif.Save_As_Manolo(nif2c, Overwrite:=True)
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] forced replacer sandbox -> {formIdLow:X8}_2c.NIF (+ _2c textures)")

                ' _2d = MISMO pliegue pero desde GPU (la cadena facegen corrida por el shader), para confirmar CPU(_2c)==GPU(_2d).
                ' Requiere host GL (solo app). Usa el complexion ORIGINAL capturado (= el que pliega el _2c) + las capas
                ' de tint del NPC. Es puro GPU (recompone el facetint + pliega en GPU), no copia el _2c CPU.
                If host IsNot Nothing AndAlso WriteGPUSandboxOutput Then
                    Dim npcRec2d = pluginManager.GetRecord(npcFormID)
                    Dim raceFid2d As UInteger = If(npcData IsNot Nothing, npcData.Record.Race, 0UI)
                    Dim race2d As Canon.IRace = Nothing
                    If npcRec2d IsNot Nothing AndAlso raceFid2d <> 0UI Then
                        Dim rr2d = pluginManager.GetRecord(raceFid2d)
                        If rr2d IsNot Nothing AndAlso rr2d.Header.Signature = "RACE" Then race2d = Canon.CanonRecords.Race(rr2d, pluginManager)
                    End If
                    Dim cplx = If(Not String.IsNullOrEmpty(sseForcedComplexion), sseForcedComplexion, GetSseHeadSlotPaths(nif, sseForcedHead).Slot0)
                    If npcRec2d IsNot Nothing AndAlso race2d IsNot Nothing AndAlso Not String.IsNullOrEmpty(cplx) Then
                        Dim glayers2d = SseFaceTintComposer.BuildLayerInputs(pluginManager, npcRec2d, race2d, raceFid2d, npcData.Record.ConfigurationFlagsFemale,
                                                                            SseFaceTintComposer.CapasDeTinteSse(npcData.Record), npcData.SseTintTexOverride)
                        If glayers2d IsNot Nothing AndAlso glayers2d.Count > 0 Then
                            ' Los MISMOS Face* overlays que el _2c/_2 componen en CPU, para que el _2d (GPU) sea el replacer
                            ' COMPLETO (fold + overlays) y matchee el facepaint. Preset del NPC (SseBodyOverlays).
                            Dim preset2d As LooksmenuLoader.LooksmenuPreset = Nothing
                            If appliedPresets IsNot Nothing Then appliedPresets.TryGetValue(npcFormID, preset2d)
                            ' SOLO los overlays de CARA. Antes se pasaba `preset2d.SseBodyOverlays` ENTERO (cuerpo
                            ' incluido) y el layer-builder del GPU tampoco filtraba por nodo ⇒ los tatuajes de cuerpo
                            ' terminaban compuestos DENTRO de la cara. Predicado único: FaceOverlaysOnly, que hoy es
                            ' `IsFoldableFaceOverlay` = cara MENOS el pool magic (un Face [SOvl] no se hornea nunca).
                            Dim overlays2d = SseOverlayCompositor.FaceOverlaysOnly(
                                If(preset2d IsNot Nothing, preset2d.SseBodyOverlays, Nothing))
                            WriteSseFacetint2dGpu(glayers2d, cplx, sseForcedDetail, overlays2d, formIdLow, originPlugin, host)
                        End If
                    End If
                End If
            Catch ex2c As Exception
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c sandbox failed: {ex2c.GetType().Name}: {ex2c.Message}")
            End Try
        End If

        ' LA DECLARACIÓN DE SALIDAS QUEDA CERRADA ACÁ, y no adentro de BakeFaceTextures. Cerrarla allá
        ' significaba "corrió el bake de texturas", y entonces el caso que motivó todo esto —un NPC sin
        ' ninguna head part de tipo Face, donde el gate no abre y BakeFaceTextures NO se llama— dejaba la
        ' bandera apagada, indistinguible de "nadie pobló esto". El packer caía al fail-closed y seguía
        ' exigiendo las tres DDS. Acá significa lo que tiene que significar: este BuildResult viene de un
        ' BuildCharGen que llegó a escribir el NIF, así que su declaración —vacía o no— es la buena.
        result.DeclaracionDeSalidasPoblada = True
        result.Success = True
        result.OutputPath = outAbs
        result.Summary = $"Wrote {outAbs} ({result.ShapesKept} shapes from {hdptProcessed} HDPTs)"
        ' Caída silenciosa del match FBNS: shapes escritas SIN morphear. Va al Summary porque si sólo
        ' vive en el log, un batch con logging apagado reporta éxito con cabezas neutras.
        If shapesFbnsUnmatched > 0 Then
            result.Summary &= $" | WARNING: {shapesFbnsUnmatched} shape(s) with no FBNS match — written WITHOUT morphing, see [FACEGEN-FBNS] log"
        End If
        If shapesFo4NoFbnsNoMorph > 0 Then
            result.Summary &= $" | WARNING: {shapesFo4NoFbnsNoMorph} FO4 shape(s) with no _faceBones and no bakeState — written WITHOUT morphing, see [FACEGEN-FBNS] log"
        End If
        If shapesFo4NoFbnsMorphed > 0 AndAlso Logger.Enabled Then
            Dim nMorphedNoFbns = shapesFo4NoFbnsMorphed
            Logger.LogLazy(Function() $"[FACEGEN-FBNS] FO4 sin _faceBones: {nMorphedNoFbns} shape(s) morpheadas por el fallback de chargen-morphs (npc=0x{npcFormID:X8})")
        End If
        If shapesFailed > 0 Then
            result.Summary &= $" | WARNING: {shapesFailed} shape(s) DROPPED by an exception — see [FACEBAKE] log"
        End If
        ' F5: el fallo de texturas VA AL SUMMARY, no solo a TextureSlotsFailed. De los 6 call sites de
        ' BuildCharGen, SOLO Save ESP (MainForm.RunChargenBake) leia esa propiedad; "Build CharGen (loose)", el
        ' batch loose, Bake All y los 3 del CLI reportaban OK con las 3 DDS falladas. Poniendolo aca lo ven TODOS
        ' sin tocar ningun caller — mismo patron que shapesFbnsUnmatched. Save ESP conserva ademas su tratamiento
        ' propio (flip del icono a Warning), que lee la propiedad.
        If result.TextureSlotsFailed > 0 Then
            result.Summary &= $" | WARNING: {result.TextureSlotsFailed} face texture(s) FAILED — {result.TextureFailureDetail}"
        End If
        If hdptSourceMissing > 0 OrElse hdptSourceLoadFail > 0 Then
            result.Summary &= $" | WARNING: {hdptSourceMissing} source mesh(es) missing, {hdptSourceLoadFail} failed to load — see [FACEBAKE] log"
        End If

        ' DebugMode: run comparator against the CK BA2 baseline. The baseline path is the canonical
        ' one (no _2 suffix) — FilesDictionary resolves either a loose CK output or the vanilla
        ' BA2 entry. Skipped silently when bytes can't be obtained (NPC has no CK bake on disk).
        If DebugMode Then
            Try
                Dim bakedRelPath = ResolveFaceGenPath(npcFormID, pluginManager)
                If Not String.IsNullOrEmpty(bakedRelPath) Then
                    Dim bakedBytes = TryGetFilesDictionaryBytes(bakedRelPath)
                    If bakedBytes IsNot Nothing AndAlso bakedBytes.Length > 0 Then
                        Dim cmp = FaceGenComparator.Compare(outAbs, bakedBytes)
                        result.Summary &= $" | [DIFF] {cmp.Summary}"
                    Else
                        result.Summary &= $" | [DIFF] no CK baseline bytes for '{bakedRelPath}'"
                    End If
                Else
                    result.Summary &= " | [DIFF] could not resolve baked FaceGen path"
                End If
            Catch ex As Exception
                result.Summary &= $" | [DIFF] comparator threw: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        Return result
    End Function


    ''' <summary>Mapa de nombre de shape a su Canon.IHdpt, o sea que se permite en la salida horneada. Se siembra
    ''' con <see cref="HeadPartResolver.MergeHeadPartsWithRaceDefaults"/> sobre state.HeadPartFormIDs (la lista
    ''' OVERLAIDA con fallback de raza, identica a la del render y NO el PNAM crudo), asi que los overrides de
    ''' LooksMenu/Edit-Face hornean exactamente lo que muestra el preview y los head parts por defecto de la
    ''' raza siguen entrando aunque el PNAM no los liste. Despues expande recursivamente por HNAM para incluir
    ''' los sub-parts tecnicos (pestanas, AO/wet, hairlines). Match case-insensitive.
    ''' <para>Devuelve el Canon.IHdpt y no solo el nombre porque aguas abajo hacen falta MeshPath, los dos paths de
    ''' .tri y el PartType para construir cada shape.</para></summary>
    Private Function BuildAllowedShapeMap(state As MainForm.NPCVisualState,
                                          pluginManager As PluginManager) As Dictionary(Of String, HeadPartResolver.HdptChainEntry)
        Dim allowed As New Dictionary(Of String, HeadPartResolver.HdptChainEntry)(StringComparer.OrdinalIgnoreCase)
        If state Is Nothing Then Return allowed

        ' Seed from the SAME head-part list the live render walks: NpcMeshCollector does
        ' MergeHeadPartsWithRaceDefaults(state) over state.HeadPartFormIDs, where `state` is the
        ' overlaid npcData (ResolveOverlaidNpcData) + ApplyRaceFallbacks. Re-parsing the RAW NPC
        ' record here (the previous behaviour) ignored the LooksMenu/Edit-Face overlay, so any
        ' head-part change made before Save ESP — eye-colour Eyes HDPT (its TNAM is the eye
        ' diffuse), hair, brows, FacialHair, scars, LM SkinTemplate head/headRear swaps — baked
        ' vanilla while the material `state` honoured the overlay → bake diverged from the render.
        ' Same function + same inputs as the render = bake == render by construction.
        Dim mergedRoots = HeadPartResolver.MergeHeadPartsWithRaceDefaults(
            state.RaceFormID, state.IsFemale, state.HeadPartFormIDs, pluginManager)

        ' Walk the chain via the shared HNAM-expanding iterator (cycles guarded inside). Each
        ' entry carries the EFFECTIVE part type (Misc hairline under hair → Hair=3), the single
        ' source of truth shared with the render walk — so the bake colors sub-parts like the
        ' render does. First-write wins on EditorID collisions.
        For Each entry In HeadPartResolver.EnumerateHdptChain(mergedRoots, pluginManager)
            If String.IsNullOrEmpty(entry.Hdpt.EditorID) Then Continue For
            If Not allowed.ContainsKey(entry.Hdpt.EditorID) Then allowed(entry.Hdpt.EditorID) = entry
        Next

        Return allowed
    End Function

    ' Biped object slot bits used by head-part occlusion, aliased to the shared BipedSlots table
    ' (single source of truth) so a slot-value change there can't silently drift this bake path.
    Private Const BakeSlotBitHairLong As UInteger = BipedSlots.SlotBitHairLong
    Private Const BakeSlotBitFaceGenHead As UInteger = BipedSlots.SlotBitFaceGenHead
    Private Const BakeSlotBitBeard As UInteger = BipedSlots.SlotBitBeard
    Private Const BakeSlotBitMouth As UInteger = BipedSlots.SlotBitMouth

    ''' <summary>Slots de headwear que cubre la DEFAULT OUTFIT del NPC, de forma DETERMINISTA.
    ''' <para>SYNC: RENDER == BAKE — la unión usa el MISMO filtro por raza que el render
    ''' (<c>EquipResolver.BuildFootprint</c>). Unir todas las ARMA sin filtrar hacía que una de otra
    ''' raza —o una pieza de power armor, que lista la raza humana para su modelo de inventario— aportara
    ''' slots que el motor nunca viste en este actor, y el bake sobre-ocluía pelo y barba que el render sí
    ''' muestra.</para>
    ''' <para>`hasLVLI` marca que algún item directo del outfit es una lista nivelada: eso randomiza la
    ''' pieza al equipar, así que el caller NO aplica oclusión de pelo/barba y prefiere under-hide. Ojo: una
    ''' ARMO determinista SÍ aporta sus slots aunque OTROS items sean LVLI.</para>
    ''' <para>Sin RNG: sólo mira los items DIRECTOS del outfit, no samplea ni expande listas.</para></summary>
    Private Function ResolveOutfitHeadwearSlots(npcData As NPC_Data,
                                                pluginManager As PluginManager) As (Slots As UInteger, HasLVLI As Boolean)
        Dim slots As UInteger = 0UI
        Dim hasLVLI As Boolean = False
        If npcData Is Nothing OrElse npcData.Record.DefaultOutfit = 0UI OrElse pluginManager Is Nothing Then
            Return (slots, hasLVLI)
        End If

        Dim otftRec = pluginManager.GetRecord(npcData.Record.DefaultOutfit)
        If otftRec Is Nothing OrElse otftRec.Header.Signature <> "OTFT" Then Return (slots, hasLVLI)
        Dim otft = Canon.CanonRecords.Otft(otftRec, pluginManager)

        ' Resolvers RecordParsers-direct (el bake no tiene NpcRenderContext; el OTFT es chico, sin cache).
        ' La LÓGICA vive en los cores compartidos con el render — acá sólo se cablean los parsers.
        ' RNAM\Armor Race y WNAM\Skin están en la interfaz común: la vista canónica alcanza sin TryCast.
        Dim parseRace = Function(rec As PluginRecord) Canon.CanonRecords.Race(rec, pluginManager)
        Dim parseArma = Function(fid As UInteger) As Canon.IArma
                            If fid = 0UI Then Return Nothing
                            Dim r = pluginManager.GetRecord(fid)
                            If r Is Nothing OrElse r.Header.Signature <> "ARMA" Then Return Nothing
                            Return Canon.CanonRecords.Arma(r, pluginManager)
                        End Function
        ' El CRUDO: lo que dice el archivo. Se conserva porque es el eslabon de la cadena de `TNAM`, y
        ' esa llamada interna tiene que ser SIEMPRE cruda (si no, cada apertura cuesta O(profundidad²)).
        Dim parseArmoCrudo = Function(fid As UInteger) As Canon.IArmo
                                 If fid = 0UI Then Return Nothing
                                 Dim r = pluginManager.GetRecord(fid)
                                 If r Is Nothing OrElse r.Header.Signature <> "ARMO" Then Return Nothing
                                 Return Canon.CanonRecords.Armo(r, pluginManager)
                             End Function
        ' ⛔ El BAKE hornea lo que el motor DIBUJA, asi que va por la vista EFECTIVA. Medido: 35 NPC de
        ' 7.205, por 15 OTFT, cambian de bytes horneados. Es el mismo resolvedor que usa el render, por
        ' RENDER == BAKE: el CLI headless entra por aca (`BuildCharGen`) y no arma su propio
        ' `EquipContext`, asi que queda cubierto sin cablear nada aparte.
        Dim parseArmo = Function(fid As UInteger) As Canon.IArmo
                            Return Canon.CanonHerencia.ArmoEfectivo(fid, parseArmoCrudo)
                        End Function
        Dim effectiveArmorRaces = NpcRenderContext.WalkArmorRaceChain(
            npcData.Record.Race, Function(fid As UInteger) pluginManager.GetRecord(fid), parseRace)
        Dim paKywdFid As UInteger = MainForm.FindArmorTypePowerKeywordFid(pluginManager)
        Dim raceIsPa As Boolean = False
        If paKywdFid <> 0UI AndAlso npcData.Record.Race <> 0UI Then
            Dim raceRec = pluginManager.GetRecord(npcData.Record.Race)
            If raceRec IsNot Nothing AndAlso raceRec.Header.Signature = "RACE" Then
                raceIsPa = MainForm.IsPowerArmorRaceData(parseRace(raceRec), paKywdFid, parseArmo)
            End If
        End If

        ' Contexto de la ley única para el bake: los mismos resolvers de arriba (sin NpcRenderContext) y el
        ' gate PA ya calculado. RENDER == BAKE: de acá sale EXACTAMENTE el mismo footprint que en el render.
        ' ArmoResolver/ArmaResolver viven en EquipResolver (Records\, no se toca) y siguen pidiendo el
        ' modelo *_Data legado: se puentea acá con los mismos parseArmo/parseArma de arriba. Nota: este
        ' inicializador NO admite comentarios intercalados entre sus miembros — el VB de este proyecto
        ' los desarma en cuanto uno de los miembros es un lambda multilínea (IsPowerArmorArmo más abajo).
        Dim eqCtx As New EquipResolver.EquipContext With {
            .PluginManager = pluginManager,
            .RaceFormID = npcData.Record.Race,
            .IsFemale = npcData.Record.ConfigurationFlagsFemale,
            .EffectiveArmorRaces = effectiveArmorRaces,
            .ArmoResolver = parseArmo,
            .ArmaResolver = parseArma,
            .IsPowerArmorArmo = Function(fid As UInteger, vista As Canon.IArmo)
                                    Return vista IsNot Nothing AndAlso MainForm.IsPowerArmorArmoData(vista, paKywdFid)
                                End Function,
            .IsPowerArmorRace = raceIsPa}

        For Each itemFID In otft.Prendas()
            If itemFID = 0UI Then Continue For
            Dim itemRec = pluginManager.GetRecord(itemFID)
            If itemRec Is Nothing Then Continue For
            Select Case itemRec.Header.Signature
                Case "LVLI"
                    ' Randomized head piece → non-deterministic. El caller saltea la oclusión.
                    hasLVLI = True
                Case "ARMO"
                    ' ARMO determinista: aporta sus slots race-valid (resolviendo template CNAM → terminal).
                    Dim terminalFID = OutfitResolver.ResolveTerminalArmorFormID(itemFID, pluginManager)
                    If terminalFID = 0UI Then Continue For
                    Dim armo = parseArmo(terminalFID)
                    If armo Is Nothing Then Continue For
                    If Canon.CanonInterpretacion.LeerComplementos(armo).Count = 0 Then
                        ' ⛔ Un ARMO SIN ARMATURES no ocluye NADA, y esto es lo que decía el render, no una
                        ' decisión de acá. Acá antes se ocluia con su BOD2 afirmando «el render cae al mesh
                        ' fallback ARMO.MOD2, p.ej. robots»: FALSO, esa caída vive DENTRO del bucle de armatures
                        ' del colector, que con cero armatures no se ejecuta.
                        ' La otra vía por la que el colector SÍ emite geometría sin armatures —el chunk-mount de
                        ' OMOD— tampoco ocluye: ese candidato nace con `SlotMask = 0` y `slottedCandidates`
                        ' filtra `SlotMask <> 0`, así que no entra al torneo ni a `headChannelMask`. Dibuja, pero
                        ' no tapa. Por eso la respuesta es NO OCLUIR, sin excepciones — hubo una versión que
                        ' preguntaba por chunk-mount y ocluia con el BOD2 crudo: en el único caso donde disparaba,
                        ' hacía lo contrario del render.
                        Continue For
                    End If

                    ' LEY ÚNICA (EquipResolver, FO4_Base_Library) — el mismo footprint que el render y los
                    ' editores. El gate de power-armor entra por el contexto, no como un if repetido acá.
                    Dim fp = EquipResolver.BuildFootprint(terminalFID, eqCtx)
                    ' Valid=False ⇒ ningún addon race-valid con mesh ⇒ el engine no viste nada de este
                    ' ARMO en este actor ⇒ 0 slots (el fallback del footprint es para display, no para
                    ' oclusión).
                    If fp.Valid Then slots = slots Or fp.OcclusionMask
            End Select
        Next

        Return (slots, hasLVLI)
    End Function

    ''' <summary>Lee si un shape clonado (BSSubIndexTriShape) es "biped30only": ocupa el biped
    ''' object 30 (HairTop) pero NO el 31 (HairLong). Misma definición y lectura de segmentos que
    ''' el render (BSTriShapeGeometry.GetBipedObjects). Devuelve False si el shape no es subindex o
    ''' no tiene segmentos.</summary>
    Private Function ShapeBiped30Only(shape As INiShape) As Boolean
        Dim subIdx = TryCast(shape, BSSubIndexTriShape)
        If subIdx Is Nothing Then Return False
        Dim biped = BSTriShapeGeometry.GetBipedObjects(subIdx)
        Return biped.Contains(30UI) AndAlso Not biped.Contains(31UI)
    End Function



    ''' <summary>Resuelve el material FINAL de un head-part igual que el render: envuelve el shape SOURCE como
    ''' IRenderableShape, arma el MeshCandidate del HDPT (incluido el HeadPartHdptFormID, que dispara el clon
    ''' vanilla-UV del head-rear de gul) y corre el MISMO delegate <paramref name="applyMaterialOverrides"/>
    ''' (cadena TXST/FTST + MNAM-BGSM + tints + palette) que usa el render. Devuelve el material con D/N/S ya
    ''' RESUELTOS por el FaceTextureSet del NPC, o Nothing si no hay resolver o falla el wrap.
    ''' <para>Fuente unica: lo consumen <see cref="ApplyRenderResolvedMaterialToShape"/> y
    ''' <see cref="BakeFaceTextures"/>, de modo que render y bake parten de las MISMAS texturas resueltas.
    ''' GetRelatedMaterial construye un material fresco por llamada, asi que resolver dos veces es idempotente y
    ''' sin estado compartido.</para></summary>
    Private Function ResolveRenderResolvedShapeMaterial(srcNif As Nifcontent_Class_Manolo,
                                                        srcShape As INiShape,
                                                        hdpt As Canon.IHdpt,
                                                        effectiveHeadPartType As Integer,
                                                        state As MainForm.NPCVisualState,
                                                        pluginManager As PluginManager,
                                                        applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate) As FO4UnifiedMaterial_Class
        If applyMaterialOverrides Is Nothing Then Return Nothing
        Dim sourceName As String = If(srcShape?.Name?.String, "")

        ' Wrap the SOURCE shape (not any cloned one) as IRenderableShape so the resolver sees the
        ' original shader with its BGSM path intact (a cloned shape's shader gets Name="" inline and
        ' would lose every BGSM field outside the shader — Wrinkles texture, AO Normal slot, etc.).
        Dim wrapper As NifRenderableShape
        Try
            wrapper = New NifRenderableShape(srcNif, srcShape, 0)
        Catch ex As Exception
            Dim shapeNameL = sourceName
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] NifRenderableShape wrap shape='{shapeNameL}': {typeL}: {msgL}")
            Return Nothing
        End Try

        ' Build a minimal MeshCandidate from the HDPT in scope. For Build CharGen the candidate
        ' chain is straightforward (HDPT → Face/Eyes/Hair/etc.) so we don't need the full
        ' Outfit/LVLN/OBTS/OMOD resolution that the live render runs.
        ' HeadPartType = EFFECTIVE type (Misc hairline under hair → Hair=3) so the shared
        ' material resolver colors sub-parts like the render does (e.g. hair palette on the
        ' hairline). HeadPartTypeRaw keeps the HDPT's own type for any raw-type logic downstream.
        ' HeadPartHdptFormID drives MainForm's ghoul-female head-rear vanilla-UV clone gate inside
        ' ApplyShapeMaterialOverrides (the delegate below), so the BAKED NIF references the
        ' persistent vanilla-bytes clone (fixes in-game too). UsesBodyTexture stays the raw record
        ' value — the previous override-proxy forcing heuristic was removed (single source of truth
        ' now lives in MainForm.ApplyGhoulHeadRearClonedTextures).
        Dim candidate As New MainForm.MeshCandidate With {
            .Kind = MainForm.MeshCandidateKind.HeadPart,
            .HeadPartType = effectiveHeadPartType,
            .HeadPartTypeRaw = hdpt.TipoDeParte(),
            .TextureSetFormID = hdpt.TextureSet,
            .HeadPartHdptFormID = hdpt.FormID,
            .UsesBodyTexture = hdpt.UsaTexturaDelCuerpo(),
            .HeadPartColorFormID = hdpt.Color
        }
        ' UseSolidTint ya NO se asigna acá: es propiedad calculada sobre HeadPartColorFormID, con la MISMA
        ' definición medida (`CNAM <> 0`) que este sitio ya tenía. El render la construía distinto (flag DATA
        ' 0x10) ⇒ divergía. Ver MainForm.MeshCandidate.UseSolidTint.

        ' Run the same per-shape resolver the render uses. Mutates wrapper.ShapeMaterial in-place.
        Try
            applyMaterialOverrides(candidate, state, {DirectCast(wrapper, IRenderableShape)})
        Catch ex As Exception
            Dim shapeNameL = sourceName
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] applyMaterialOverrides shape='{shapeNameL}': {typeL}: {msgL}")
            Return Nothing
        End Try

        Return wrapper.ShapeMaterial?.material
    End Function

    ''' <summary>Aplica al shape el material resuelto como lo hace el render, y DEVUELVE ese
    ''' material: el bake necesita saber que texturas quedaron embebidas para poder declarar las
    ''' que INVENTA la app y que ningun mod entrega. Era una Sub; tiene un solo llamador.</summary>
    Private Function ApplyRenderResolvedMaterialToShape(nif As Nifcontent_Class_Manolo,
                                                    cloned As INiShape,
                                                    srcNif As Nifcontent_Class_Manolo,
                                                    srcShape As INiShape,
                                                    hdpt As Canon.IHdpt,
                                                    effectiveHeadPartType As Integer,
                                                    state As MainForm.NPCVisualState,
                                                    pluginManager As PluginManager,
                                                    applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                                    skinTintAlpha As Single) As FO4UnifiedMaterial_Class
        ' Resolve the FINAL material exactly like the render (TXST/FTST + MNAM-BGSM + tints + palette);
        ' shared with the FaceTint bake so both transcribe / composite the SAME resolved textures.
        Dim mat = ResolveRenderResolvedShapeMaterial(srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides)
        If mat Is Nothing Then
            Return Nothing
        End If

        ' POST-RESOLVER snapshot: same fields after the resolver ran. Diff against PRE shows
        ' which fields the resolver chain (TXST.MNAM swap, MSWP swap, tint colour overrides, etc.)
        ' actually mutated. Should match what gets serialized to disk by Save_To_Shader below.

        ' La TRANSCRIPCIÓN vive en un método aparte porque tiene DOS consumidores: este bake y el export
        ' a NIF (FaceTextureRepointer). Los dos producen la misma clase de artefacto — un shape SIN material
        ' externo — así que les corresponde el MISMO gesto. Copiarlo en vez de compartirlo fue lo primero que
        ' se intentó, y se desincronizó al toque: la versión a mano no tenía el centinela de Emissive de más
        ' abajo y apagaba el Emissive en las 9 cabezas de FO4.
        TranscribeResolvedMaterialToShader(nif, cloned, mat, skinTintAlpha)
        Return mat
    End Function

    ''' <summary>Vuelca el material YA RESUELTO al shader INLINE del shape y corta el link al BGSM externo,
    ''' que es lo que vuelve al NIF autocontenido.
    ''' <para>El corte del nombre no es cosmético y no es opcional: verificado en Fallout4.exe, con
    ''' <c>prop+0x10</c> NO vacío los 3 call-sites de carga propia SÍ cargan el BGSM y
    ''' <c>ApplyMaterialToGeometry 0x142169BB0</c> reemplaza el <b>texture set ENTERO</b>
    ''' (<c>prop+0x1d0 ← mat+0x78</c>, 0x142163B70). Con el nombre vacío bailan en la guarda de largo
    ''' y el shader inline pasa a ser la ley.</para>
    ''' <para>⛔ LA VA DE ESA GUARDA SE RETIRÓ (2026-08-22). Decía <c>0x14167C300 → je al epílogo</c>, y en
    ''' el <c>Fallout4.exe</c> instalado esa dirección es un <c>ret</c> suelto, fuera de toda función que
    ''' el índice conozca — no un <c>je</c>. No se reemplaza por otra dirección porque no se re-hizo el RE:
    ''' inventar una sería peor que no tener ninguna. <b>La LEY sigue en pie</b> (se verificó por conducta,
    ''' no por esta VA); lo que se retira es la cita.</para>
    ''' <para>Consumidores: el bake de FaceGen y el export a NIF. Ver 30-fo4-material-vs-nif.</para></summary>
    Friend Sub TranscribeResolvedMaterialToShader(nif As Nifcontent_Class_Manolo,
                                                  shape As INiShape,
                                                  mat As FO4UnifiedMaterial_Class,
                                                  skinTintAlpha As Single)
        If nif Is Nothing OrElse shape Is Nothing OrElse mat Is Nothing Then Return
        Dim shad = nif.GetShader(shape)
        If shad Is Nothing Then
            Return
        End If

        Try
            Dim bsls = TryCast(shad, BSLightingShaderProperty)
            If bsls IsNot Nothing Then
                Dim bgsm = TryCast(mat.Underlying_Material, BGSM)
                If bgsm Is Nothing Then
                    Return
                End If
                ' TX05 (EnvMask) en NIF spec es dual-purpose: para shaders FaceTint el motor
                ' lo usa como Wrinkles. CK al bakear FaceGen escribe BGSM.WrinklesTexture en
                ' TX05 cuando el shader es FaceTint. Para todo lo demás, va EnvmapMaskTexture
                ' (NIF inline TX05 capturado en _EnvmapMaskPath; ver Deserialize sidecar JSON).
                Dim slot5Path As String
                If mat.NifShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint AndAlso
                   Not String.IsNullOrEmpty(mat.WrinklesTexture) Then
                    slot5Path = mat.WrinklesTexture
                Else
                    slot5Path = mat.EnvmapMaskTexture
                End If
                ' Hand the library the per-NPC skin-tint strength (from the NPC's QNAM/SkinTone-layer
                ' alpha). Save_To_Shader writes it to shad.SkinTintAlpha (only when SkinTint) — the
                ' value is NPC-level, not a BGSM field, so the app provides it (same split as the skin
                ' tone COLOR which the resolver puts in HairTintColor and the library writes).
                mat.SkinTintAlpha = skinTintAlpha
                mat.Save_To_Shader(nif, shape, bsls, mat.NifShaderType, slot5Path)
                ' CK al bakear el FaceGen deja shad.Name vacío en el shader inline (no
                ' linkea al BGSM external). Replicamos eso para que el .nif sea standalone
                ' (todos los datos del material viven embedded en el shader, sin depender
                ' del .bgsm en disco) y para que el comparator embedded-vs-embedded de
                ' GetRelatedMaterial caiga en la rama Create_From_Shader igual que el bake CK.
                If bsls.Name IsNot Nothing Then bsls.Name.String = ""

                ' CK convention for non-emissive shapes: when the BGSM source does NOT mark
                ' the material as emissive, CK still emits Emissive=True + EmittanceColor=(0,0,0)
                ' as a "field present, no light" centinela. Replicating lines up most baked
                ' shapes' Emit fields with CK output. Verified against Alijo (8 shapes) and Carol
                ' (NeckGore, EmitEnabled=True with rgb=(255,0,18) for ghoul gore must keep its
                ' real colour). Branch gated on source mat.EmitEnabled to preserve real emisives.
                If Not mat.EmitEnabled Then
                    bsls.Emissive = True
                    bsls.EmissiveColor = New NiflySharp.Structs.Color4(0.0F, 0.0F, 0.0F, 1.0F)
                End If
                bsls.RootMaterialName = ""
                ' CK sets Transform_Changed (F4SPF2 bit 7) on every baked FaceGen shape — universal
                ' across all 4 reference NPCs (human M/F, ghoul, supermutant), every single shape,
                ' no exception (measured). It's a housekeeping flag,
                ' not a material field (absent from the BGSM), so it belongs here with the other CK
                ' bake conventions, not in Save_To_Shader. shad.Type was set by Save_To_Shader above,
                ' so SetFlagSF2 resolves the FO4-specific bit correctly.
                ' GAME-GATED: esta es una convención del bake CK de FO4 (Transform_Changed = F4SPF2 bit 7).
                ' En un shader SK ese MISMO bit 7 es Assume_Shadowmask → aplicarlo a SSE corrompía el shader
                ' (medido: head/mouth/hair ganaban 0x80 vs CK). CK SSE NO lo setea (SSPF2 del CK == source).
                If Not nif.Header.Version.IsSSE Then
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bsls, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
                End If
                ' El ShaderType horneado es FUNCION DETERMINISTICA de los flags del material: el CK NO preserva
                ' el tipo de la fuente, lo DERIVA. Probado al 100 % sobre el corpus FaceGen vanilla de FO4 (1490
                ' NIF / 14136 lighting shapes, 8 combinaciones de flags, todas puras). Precedencia (load-bearing
                ' Glow > Face; el resto nunca coexiste): Glowmap -> GlowShader · Facegen -> FaceTint · SkinTint ->
                ' SkinTint · Hair -> HairTint · EnvironmentMapping -> EnvironmentMap · si no, Default.
                ' Testigo de que es por FLAGS y no por el inline: eyelashes.bgsm trae inline EnvironmentMap con su
                ' flag apagado y el CK las hornea Default (1381/1381).
                ' Se deriva de los BOOLS del material (fieles a los flags tras Create_From_Shader). Es BAKE-ONLY:
                ' la Derive de la libreria queda conservadora porque en meshes generales bGlowmap y
                ' bEnvironmentMapping conviven con inline Default.
                ' ⛔ GAME-GATED: en SSE el CK PRESERVA el shader type y los flags del source, asi que derivar alla
                ' colapsaba los ojos a Default y rompia el shader.
                If Not nif.Header.Version.IsSSE Then
                    Dim bakedType As Enums.BSLightingShaderType
                    If mat.Glowmap Then
                        bakedType = Enums.BSLightingShaderType.GlowShader
                    ElseIf mat.Facegen Then
                        bakedType = Enums.BSLightingShaderType.FaceTint
                    ElseIf mat.SkinTint Then
                        bakedType = Enums.BSLightingShaderType.SkinTint
                    ElseIf mat.Hair Then
                        bakedType = Enums.BSLightingShaderType.HairTint
                    ElseIf mat.EnvironmentMapping Then
                        bakedType = Enums.BSLightingShaderType.EnvironmentMap
                    Else
                        bakedType = Enums.BSLightingShaderType.Default
                    End If
                    bsls.ShaderType = bakedType
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF1(bsls, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags1.Eye_Environment_Mapping), False)
                End If

            Else
                Dim bes = TryCast(shad, BSEffectShaderProperty)
                If bes Is Nothing Then
                    Return
                End If
                Dim bgem = TryCast(mat.Underlying_Material, BGEM)
                If bgem Is Nothing Then
                    Return
                End If
                mat.Save_To_Shader(nif, shape, bes)
                If bes.Name IsNot Nothing Then bes.Name.String = ""
                ' Transform_Changed: el CK lo setea en TODO shape horneado, también los effect shaders, así
                ' que esta rama necesita el mismo tratamiento que la de lighting.
                ' GAME-GATED, y no por analogía: el bit 7 significa COSAS DISTINTAS según el juego (en
                ' Skyrim es Assume_Shadowmask), y BSEffectShaderProperty no tiene enum propio — usa el MISMO
                ' par de enums que el lighting, elegido por VERSIÓN. Encima el helper despacha por el Type
                ' del shader y no por la clase del bloque, así que en un NIF de SSE esto escribiría
                ' Assume_Shadowmask sin traducción: el mismo modo de corrupción ya medido en la otra rama.
                If Not nif.Header.Version.IsSSE Then
                    NiflySharp.Helpers.ShaderHelper.SetFlagSF2(bes, CUInt(NiflySharp.Enums.Fallout4ShaderPropertyFlags2.Transform_Changed), True)
                End If
            End If
        Catch ex As Exception
            ' ESTE METODO VUELVE A FALLAR FUERTE, Y LA DECISION DE TRAGAR VIVE EN EL LLAMADOR.
            ' Lo tragué acá razonando sobre UN llamador —el loop de shapes del bake— y tiene TRES:
            ' `FaceTextureRepointer` y `ShapeMaterialTranscriber` (export a NIF). Para esos dos, tragar es
            ' PEOR que fallar: siguen y reportan exito (`Outcome.Written`, `shapesWritten += 1`) sobre una
            ' shape cuyo link al BGSM externo NO se corto —el corte esta DESPUES del Save_To_Shader— asi que
            ' el motor reemplaza el texture set entero y descarta todo lo que el export escribio.
            ' Y no digo "queda con material default", que es lo que afirmaba el comentario anterior:
            ' `Save_To_Shader` escribe ~25 flags y 8 slots en secuencia, asi que un fallo a mitad deja el
            ' shader INDETERMINADO. (Tambien saco la mencion al bit `Skinned`: `Skinned` no aparece ni una
            ' vez en FO4UnifiedMaterial_Class, o sea que esa justificacion no se sostenia.)
            Dim m = ex.GetType().Name & ": " & ex.Message
            Logger.LogLazy(Function() $"[BAKE-MAT] TranscribeResolvedMaterialToShader falló: {m}")
            Throw
        End Try
    End Sub

    ''' <summary>Bake del facetint de SSE: compone el <c>_d</c> por NPC (CPU, engine-exact, WYSIWYG con la
    ''' edicion de tints) bajo la carpeta FaceTint del plugin y apunta ahi el slot 6 del texture-set de la shape
    ''' Face. SSE-only; reemplaza al bake D/N/S de FO4.
    ''' <para>Naming de debug igual que el bake de FO4: el DDS en disco lleva sufijo <c>_2</c>, y el que se
    ''' EMBEBE en el NIF depende del consumidor - el canonico cuando <paramref name="willBePacked"/> (el packer
    ''' renombra), el <c>_2</c> real si no, para que un NIF suelto referencie un archivo que existe. En
    ''' debug+sandbox emite ademas el <c>_2b</c> (recompose por GPU) para medir paridad CPU==GPU.</para></summary>
    ''' <param name="result">BuildResult del bake, para <see cref="RecordTextureFailure"/>. El facetint es un
    ''' artefacto REQUERIDO del bundle de SSE: si no se escribe, el NIF entra al archive sin el y la cara sale
    ''' MARRON in-game. Por eso todo bail de aca tiene que REPORTAR: si solo loguea, en release es un no-op y el
    ''' save informa OK mientras el usuario descubre el problema recien al empaquetar.</param>
    Private Sub WriteSseFacetintDds(nif As Nifcontent_Class_Manolo, cloned As INiShape, npcFormID As UInteger,
                                    originPlugin As String, pluginManager As PluginManager,
                                    npcData As NPC_Data, willBePacked As Boolean,
                                    result As BuildResult,
                                    Optional host As NpcRenderHost = Nothing)
        Try
            ' SE DECLARA AL ENTRAR, antes de cualquier bail. Si este metodo corre es porque el NPC tiene head
            ' part de tipo Face, y entonces el facetint SIEMPRE correspondia. Declararlo aca hace que los
            ' cinco bails de abajo -que son FALLAS, todos reportan- signifiquen "falta": el bundle se
            ' descarta entero y los sueltos se conservan para reintentar.
            ' Antes el spec llevaba Salida=Ninguna, o sea exento del chequeo de pertenencia, y entonces un
            ' facetint de un horneado ANTERIOR entraba al BSA con el NIF nuevo por el solo hecho de existir
            ' -el barrido de SSE no toca FaceTint\ a proposito-: cara mezcla de dos horneados.
            ' ⛔ Esta declaracion YA NO es lo que hace requerido al facetint: eso lo garantiza
            ' FaceGenPaths.SalidasSiempreRequeridas, porque a este metodo solo se entra si el NPC tiene head
            ' part de tipo Face y el que NO la tiene tambien necesita el archivo. Queda como lo que dice ser
            ' -la DECISION del bake- y para que los cinco bails de abajo sigan leyendose como "falta".
            If result IsNot Nothing Then
                result.SalidasDeTexturaDeclaradas = result.SalidasDeTexturaDeclaradas Or FaceGenPaths.SalidaDeTexturaDeCara.SseFaceTint
            End If
            If npcData Is Nothing Then
                RecordTextureFailure(result, "facetint SSE: could not resolve the NPC (npcData Nothing)")
                Return
            End If
            Dim npcRec = pluginManager.GetRecord(npcFormID)
            If npcRec Is Nothing Then
                RecordTextureFailure(result, $"facetint SSE: the NPC record 0x{npcFormID:X8} does not resolve")
                Return
            End If
            Dim raceFid As UInteger = npcData.Record.Race
            Dim race As Canon.IRace = Nothing
            If raceFid <> 0UI Then
                Dim rr = pluginManager.GetRecord(raceFid)
                If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = Canon.CanonRecords.Race(rr, pluginManager)
            End If
            If race Is Nothing Then
                RecordTextureFailure(result, $"facetint SSE: the RACE 0x{raceFid:X8} does not resolve (without it there are no tint layers)")
                Return
            End If
            ' Overlaid tints + RaceMenu overlays (Edit Face edits) so the bake is byte-WYSIWYG with the live
            ' preview (both call BakeFaceTintDds with the same tint override + overlays).
            Dim tintOverride = SseFaceTintComposer.CapasDeTinteSse(npcData.Record)
            ' Tamaño del facetint = propiedad Setting_FaceGenDiffuseResolution (Inherit→512 vanilla = default byte-inerte;
            ' 1024/2048/… si el usuario lo sube). NO hardcodeado. El facetint es el "diffuse" del facegen SSE.
            Dim fSz = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, 512)
            ' Formato del facetint = el elegido por el usuario (CharGen Options → Diffuse), NO hardcodeado. Antes
            ' BakeFaceTintDds forzaba BC3, así que el facetint real y el neutral del fold podían salir con formatos
            ' distintos según el NPC estuviera plegado o no.
            ' GATE del encode (ver SkipDdsEncode). El Nothing/no-Nothing SI decide el slot 6, y sale del
            ' COMPOSE (ComposeFacetintAcc), no del encode: un NPC sin capas de tint no tiene facetint y el bake
            ' no le escribe el slot. Por eso en modo gateado se corre igual el compose (512x512) y se saltea
            ' SOLO el EncodeLinearRgbaToBc3 + el File.Write ⇒ misma condicion, mismo NIF.
            Dim dds As Byte() = Nothing
            ' Acumulador del facetint, devuelto por BakeFaceTintDds para que el volcado TGA de abajo NO
            ' vuelva a componer. Con "Generate TGA" tildado se componia DOS veces por NPC.
            Dim facetintAcc As Single() = Nothing
            If SkipDdsEncode Then
                If SseFaceGenBaker.ComposeFacetintAcc(pluginManager, npcRec, race, raceFid, npcData.Record.ConfigurationFlagsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride) Is Nothing Then
                    RecordTextureFailure(result, "facetint SSE: composing the tint layers produced nothing (ComposeFacetintAcc Nothing)")
                    Return
                End If
            Else
                dds = SseFaceGenBaker.BakeFaceTintDds(pluginManager, npcRec, race, raceFid, npcData.Record.ConfigurationFlagsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride, DiffuseDxgiFromSetting(), facetintAcc)
                If dds Is Nothing Then
                    RecordTextureFailure(result, $"facetint SSE: compose/encode of the _d failed ({fSz}x{fSz}, dxgi={DiffuseDxgiFromSetting()})")
                    Return
                End If
            End If
            Dim fgLocal = PluginManager.ToFaceGenLocalFormID(npcFormID)
            Dim tintDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalTint, originPlugin)
            Dim suffix = If(DebugMode, "_2.dds", ".dds")          ' on-disk name (sandbox in DebugMode)
            Dim embeddedSuffix = If(willBePacked, ".dds", suffix) ' the packer renames _2 → canonical
            Dim rel = tintDir & $"{fgLocal:X8}{suffix}"
            Dim outFile = IO.Path.Combine(BakeOutputRoot.Current(), rel)
            If Not SkipDdsEncode Then
                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
                IO.File.WriteAllBytes(outFile, dds)
                ' Identidad: es lo que le dice al packer que ESTE facetint es de ESTE horneado.
                If result IsNot Nothing Then
                    result.SalidasDeTexturaEscritas = result.SalidasDeTexturaEscritas Or FaceGenPaths.SalidaDeTexturaDeCara.SseFaceTint
                End If
            End If
            ' TGA lossless del _2 (CPU) cuando "Generate TGA" está marcado (= FO4). Recompone el acc SOLO en ese
            ' caso (no re-decodea el BC3) para dumpear el buffer pre-encode, byte-igual al que se encodeó.
            If WriteTGASandboxOutput Then
                ' Se REUSA el acumulador que ya compuso BakeFaceTintDds (facetintAcc). Antes se llamaba a
                ' ComposeFacetintAcc otra vez: una composicion COMPLETA del facetint por NPC, tirada. El TGA
                ' sale del MISMO buffer que se encodeo, asi que es byte-identico al de antes por construccion
                ' (misma funcion pura, mismas entradas) — solo que ahora se ejecuta una vez en vez de dos.
                ' Fallback: si venimos por la rama SkipDdsEncode no hay acc, y ahi si hay que componerlo.
                Dim accT = If(facetintAcc, SseFaceGenBaker.ComposeFacetintAcc(pluginManager, npcRec, race, raceFid, npcData.Record.ConfigurationFlagsFemale, fSz, fSz, tintOverride, npcData.SseTintTexOverride))
                If accT IsNot Nothing Then MaybeWriteTgaBeside(outFile, fSz, fSz, SseFaceGenBaker.LinearRgbaToBgra(accT, fSz, fSz))
            End If
            ' Point the head shape's texture-set slot 6 (facetint) at the engine path (Data-relative).
            Dim spr = cloned.ShaderPropertyRef
            If spr IsNot Nothing AndAlso spr.Index >= 0 Then
                Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
                If lsp IsNot Nothing AndAlso lsp.TextureSetRef IsNot Nothing AndAlso lsp.TextureSetRef.Index >= 0 Then
                    Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
                    ' El slot 6 sigue la MISMA ley que el resto (bake CK 0x141d0ea00): sólo lo escribe el
                    ' branch type 4 FaceTint. El gate del call site es por HDPT.PartType=Face, que NO es
                    ' equivalente: un head part de cara puede tener un shape autorado con otro shader type.
                    ' MEDIDO: 'MaleHeadManekin' (HDPT 0x1078799, PartType=Face, TNAM=0, MODL=ManekinHead.nif)
                    ' tiene shape shType=Default(0) ⇒ el CK deja el slot 6 VACÍO, y nosotros le escribíamos el
                    ' facetint. 8 NPCs (Dawnguard 00008B34/0000D1BE · Dragonborn 0002A378/79/7A ·
                    ' HearthFires 00008B32/00015D5D · Skyrim 00089A85).
                    If lsp.ShaderType_SK_FO4 <> NiflySharp.Enums.BSLightingShaderType.FaceTint Then
                        ' Gateado por Logger.Enabled ADEMÁS del LogLazy: sin el gate se aloca la clausura en
                        ' CADA shape de CADA NPC aunque el log esté apagado. Convención del codebase.
                        If Logger.Enabled Then
                            Dim stL6 = lsp.ShaderType_SK_FO4
                            Logger.LogLazy(Function() $"[FACEBAKE][SSE] slot6 NO escrito: shape shType={stL6} (≠FaceTint) — ley CK 0x141d0ea00")
                        End If
                    ElseIf ts IsNot Nothing AndAlso ts.Textures IsNot Nothing AndAlso ts.Textures.Count > 6 Then
                        ts.Textures(6).Content = EmbeddedEngineTexPath(tintDir & $"{fgLocal:X8}{embeddedSuffix}")
                        ' NOTE: NO "Textures\" prefix on the skin slots 0/7. MEDIDO vs BSA CK (batch SSE): CK escribe
                        ' el head diffuse SIN prefijo (p.ej. 'Actors\Character\Male\MaleHead.dds'), byte-igual al
                        ' valor ya resuelto del skin TXST. Un intento anterior de prefijar 0/7 fue medido contra un
                        ' FaceGeom LOOSE (mi propio bake ya prefijado ⇒ circular, gotcha
                        ' 10-stack-arnes-de-medicion) y RETRACTADO: prefijar rompía la paridad.
                    End If
                End If
            End If
            ' `dds` es Nothing en la rama SkipDdsEncode ⇒ `dds.Length` reventaba con una NRE (latente: LogLazy
            ' sólo invoca la lambda si Logger.Enabled). La NRE caía en el catch de abajo, que la reportaba como
            ' "facetint bake failed" AUNQUE el slot 6 ya se había escrito bien, y de paso se comía el sandbox _2b.
            Dim ddsLen = If(dds Is Nothing, 0, dds.Length)
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _d -> {rel} ({ddsLen}b{If(SkipDdsEncode, ", encode SALTEADO", "")})")

            ' === _2b GPU SANDBOX del facetint BASE (debug+sandbox, requiere host GL) ===
            ' Contraparte GPU del _2 (CPU): compone PURO GPU las MISMAS capas de tint (BuildLayerInputs) sobre un
            ' base PLANO = seed(0.5) vía ApplyFaceTintPipeline y hace readback → _2b. NO sube el resultado CPU (eso
            ' sería trampa y no mediría nada): RECOMPONE en GPU para medir la paridad CPU==GPU del facetint base.
            ' Espejo exacto del _2b de FO4 y del _2b de overlays. Sólo app (host); la paridad la confirma el usuario.
            If host IsNot Nothing AndAlso DebugMode AndAlso WriteGPUSandboxOutput Then
                Try
                    Dim glayers = SseFaceTintComposer.BuildLayerInputs(pluginManager, npcRec, race, raceFid, npcData.Record.ConfigurationFlagsFemale,
                                                                       SseFaceTintComposer.CapasDeTinteSse(npcData.Record), npcData.SseTintTexOverride)
                    If glayers IsNot Nothing AndAlso glayers.Count > 0 Then WriteSseFacetint2bGpu(glayers, fSz, fSz, fgLocal, originPlugin, host)
                Catch ex2b As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _2b GPU failed: {ex2b.GetType().Name}: {ex2b.Message}")
                End Try
            End If
        Catch ex As Exception
            Dim tN = ex.GetType().Name, mN = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint bake failed: {tN}: {mN}")
            RecordTextureFailure(result, $"facetint SSE: {tN}: {mN}")
        End Try
    End Sub

    ''' <summary>_2b GPU del facetint BASE: recompone las capas de tint del NPC (PaletteMask, canal R, ley SSE) sobre
    ''' un base PLANO = seed(0.5) por GL (<see cref="FaceTintCompositor.ApplyFaceTintPipeline"/>), readback → encode →
    ''' <c>FaceTint\&lt;plugin&gt;\&lt;id&gt;_2b.dds</c> (BC3, = formato del <c>_2</c>). Compose PURO GPU (NO sube el
    ''' resultado CPU del <c>_2</c>): el par <c>_2</c>/<c>_2b</c> mide la paridad CPU==GPU del facetint. Base subido
    ''' como LINEAL (<c>baseDiffuseIsLinearOnGpu:=True</c>) para que el seed 0.5 GL coincida con el 0.5-lin del CPU.
    ''' GL-bound (corre en el hilo del host). SSE-only, debug sandbox.</summary>
    Private Sub WriteSseFacetint2bGpu(layers As IList(Of FaceTintLayerInput), w As Integer, h As Integer,
                                      fgLocal As UInteger, originPlugin As String, host As NpcRenderHost)
        Dim gbra = ComposeSseFacetintBgraOnGpu(layers, w, h, host)
        If gbra Is Nothing Then Return
        Dim mips = CInt(Math.Floor(Math.Log(Math.Min(w, h), 2))) + 1
        ' Formato = el MISMO que el _2 (CharGen Options → Diffuse), NO hardcodeado: el _2b existe para compararse
        ' contra el _2, así que si el _2 sale BC7/Uncompressed y el _2b quedara fijo en BC3, la comparación medía
        ' la diferencia de FORMATO en vez de la paridad CPU-vs-GPU que se quiere medir.
        Dim dds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(w, h, gbra, DiffuseDxgiFromSetting(), generateMipMaps:=True, generatedMipLevels:=mips)
        If dds Is Nothing Then Return
        Dim rel = FaceGenPaths.TexturaDds(FaceGenPaths.CanalTint, originPlugin, fgLocal, "_2b")
        Dim outFile = IO.Path.Combine(BakeOutputRoot.Current(), rel)
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
        IO.File.WriteAllBytes(outFile, dds)
        MaybeWriteTgaBeside(outFile, w, h, gbra)
        Logger.LogLazy(Function() $"[FACEBAKE][SSE] facetint _2b GPU -> {rel} ({dds.Length}b)")
    End Sub

    ''' <summary>Compone las capas de tint SSE (PaletteMask, ley SSE all-linear) sobre un base PLANO = seed(0.5) por
    ''' GL (<see cref="FaceTintCompositor.ApplyFaceTintPipeline"/>) y hace readback → BGRA lineal (W·H·4). Base subido
    ''' como LINEAL (baseDiffuseIsLinearOnGpu) para que el seed 0.5 GL == el 0.5-lin del CPU. Nothing si falla.
    ''' Contraparte GPU del compose CPU del facetint (SseFaceTintComposer.ComposeLinearRgba). GL-bound (host).</summary>
    Private Function ComposeSseFacetintBgraOnGpu(layers As IList(Of FaceTintLayerInput), w As Integer, h As Integer, host As NpcRenderHost) As Byte()
        If host Is Nothing OrElse layers Is Nothing OrElse layers.Count = 0 OrElse w <= 0 OrElse h <= 0 Then Return Nothing
        Dim npix = w * h
        ' SEED DE LA LEY Y EN FLOAT. Acá había un `Const seedByte As Byte = 128` cuyo propio comentario decía
        ' "= ActiveSettings.SeedConstant" — pero era un LITERAL, y encima CUANTIZADO: 128/255 = 0,50196, no 0,5.
        ' El _2b existe para medir la paridad CPU-vs-GPU del facetint CONTRA el _2, que siembra 0,5 EXACTO por
        ' CPU ⇒ ese medio LSB era un sesgo del INSTRUMENTO, no del compositor, y el fgTint lo amplifica ×255/64
        ' (2,00781 vs 2,01563). Se siembra por el MISMO helper float que usa el render (UploadRgba32fFlat), que
        ' es justo lo que su doc pide ("No volver a sembrar por bytes").
        Dim seedRgb = SseFaceTintComposer.TryGetFlatSeedRgb()
        If seedRgb Is Nothing Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2b ABORT: la ley pide seed desde textura base y el facetint es TINT-ONLY (no hay base).")
            Return Nothing
        End If
        Dim baseTex = SseFoldLayerStack.UploadRgba32fFlat(seedRgb(0), seedRgb(1), seedRgb(2), 1.0F, w, h)
        If baseTex = 0 Then Return Nothing
        ' Espejo CPU de ESTE camino = SseFaceTintComposer (este _2d es la contraparte GPU del _2c, que es
        ' 100 % CPU por ese modulo) ⇒ se declara SU capacidad, no la del compositor CPU de FO4.
        Dim pr = FaceTintCompositor.ApplyFaceTintPipeline(host.CompositorState, host.TintGpuCache,
                                                          baseTex, 0, 0, w, h, layers, New List(Of FaceRegionSwapInput)(),
                                                          SseFaceTintComposer.AccumSpaceCapability,
                                                          baseDiffuseIsLinearOnGpu:=True)
        Dim resultId = If(pr IsNot Nothing AndAlso pr.Diffuse IsNot Nothing AndAlso pr.Diffuse.IsFresh, pr.Diffuse.TextureId, baseTex)
        Dim gbuf = ReadbackGlBgra(resultId, npix)
        If resultId <> baseTex Then Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(resultId) : Catch : End Try
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(baseTex) : Catch : End Try
        Return gbuf
    End Function

    ''' <summary>_2d = el pliegue SSE **100% GPU**, contraparte exacta del _2c (100% CPU). Corre EXACTAMENTE las mismas
    ''' funciones que el RENDER (<see cref="SseFoldLayerStack"/>) ⇒ el sandbox mide el código que de verdad se ejecuta,
    ''' no una copia paralela que se puede desincronizar. Tres pasos, todos GPU y todos en FLOAT (Rgba32f):
    '''   1. facetint  = ComposeFacetintGpu(capas de tint sobre seed 0.5)      [lineal]
    '''   2. pliegue   = FoldGpu(complexion, facetint, detail)                 [ley del engine: softlight(_,tint) x amplify(detail)]
    '''   3. capas     = ComposeGpu(skee MASKT + overlays Face[Ovl])           [stack de capas]
    ''' NADA de intermedios en 8 bits. La versión anterior transportaba el facetint como DDS y hacía el readback en
    ''' bytes LINEALES: MEDIDO contra el _2c daba RMS 2,4/255 y máx 18, con el error concentrado en las sombras (5,7 medio
    ''' en 0..31 vs 0,3 en 128..159) — la firma de cuantizar en lineal (cerca del negro 1 nivel lineal ≈ 13 niveles sRGB),
    ''' agravado porque el amplify del detail escala la cadena hasta ×255/64. En float el transporte deja de limitar la paridad.
    ''' GL-bound (host). SSE-only, debug sandbox.</summary>
    Private Sub WriteSseFacetint2dGpu(layers As IList(Of FaceTintLayerInput), complexionPath As String, detailPath As String,
                                      overlays As IList(Of RaceMenuJslot.JslotOverlayNode),
                                      fgLocal As UInteger, originPlugin As String, host As NpcRenderHost)
        If host Is Nothing OrElse String.IsNullOrEmpty(complexionPath) Then Return
        ' complexion (slot 0) a su tamaño NATIVO (= el tamaño al que el _2c pliega en CPU), en sRGB.
        Dim srcBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(complexionPath))
        If srcBytes Is Nothing Then Return
        Dim dec = FaceTintCpuCompositor.DecodeDds(srcBytes)
        If dec Is Nothing OrElse dec.Rgba8 Is Nothing OrElse dec.Width <= 0 OrElse dec.Height <= 0 Then Return
        Dim w = dec.Width, h = dec.Height, npix = w * h
        Dim det As Single() = If(Not String.IsNullOrEmpty(detailPath), SseFaceTintComposer.DecodeTextureRgba(detailPath, w, h), Nothing)

        ' 1-4) TODO GPU, UNA sola cadena: facetint -> fold -> capas -> UNFOLD (rama uFgTintFold==2 del shader).
        ' Es la MISMA funcion que ejecuta el render (ComposeFoldedGpuResident), asi que este sandbox mide lo que
        ' el render corre de verdad. El unico readback es el de abajo, para encodear el DDS.
        ' NO mezclar CPU aca: un sandbox mitad-CPU-mitad-GPU no mide NINGUN camino real -- por eso se elimino en
        ' su momento el _2b del diffuse, y por eso el unfold NO se hace con PreCompensateEngineChain aca.
        ' El _2d no lee MASKT del NIF (el _2c tampoco) => skeeRaw = Nothing, y sin skee no hace falta skinRgb.
        Dim foldedId = SseFoldLayerStack.ComposeFoldedGpuResident(dec.ToUnitArray(), layers, det, Nothing, overlays,
                                                                  Nothing, w, h, host,
                                                                  measureParity:=True)
        ' measureParity:=True (antes hardcodeado en False). El _2d existe EXACTAMENTE para comparar el
        ' CPU (_2c) contra el GPU, y con el flag en False el unico instrumento de paridad del camino SSE
        ' quedaba apagado justo en el sandbox que lo motiva. Ahora emite `[SSE-FOLD] PARITY rmsCPUvsGPU=`.
        ' Costo: un readback + una replica CPU, y este bloque ya esta gateado por DebugMode + GPU sandbox.
        If foldedId = 0 Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: la cadena GPU (fold + capas + unfold) fallo.")
            Return
        End If
        ' ComposeFoldedGpuResident devuelve LINEAL A PROPOSITO (corre un cvt sRGB->lineal internamente
        ' porque ESA textura alimenta al RENDER, que muestrea en lineal). El _2d, en cambio, es un artefacto de
        ' DISCO y tiene que quedar en sRGB igual que el _2c/_2. Volcarlo tal cual era el bug: MEDIDO sobre 285.978
        ' muestras, _2d == sRGB_to_linear(_2c) EXACTO (err medio 0,255/255, max 0,942, CERO fuera de +-2, contra un
        ' err de control de 44,1) => el arnes que existe justamente para confirmar CPU(_2c)==GPU(_2d) daba un
        ' desacuerdo del 99,989% de los pixeles que NO era del pliegue. No afectaba al juego (lo que se empaqueta
        ' sale del camino CPU y es byte-identico al _2c), solo cegaba la validacion de paridad.
        ' Se deshace con la MISMA funcion (cvt(0,1) es la inversa exacta del cvt(1,0) que corre dentro de
        ' ComposeFoldedGpuResident), en GPU y
        ' ANTES del readback: nada de matematica CPU nueva que pueda derivar del shader. El orden queda igual que
        ' el _2c/_2 (resample del BGRA en sRGB y recien despues el encode), que es el que exige la paridad.
        Dim srgbId = FaceTintCompositor.ConvertTextureSpace(host.CompositorState, foldedId, w, h, 0, 1)
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(foldedId) : Catch : End Try
        If srgbId = 0 Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: el cvt lineal->sRGB de salida fallo.")
            Return
        End If
        Dim acc = SseFoldLayerStack.ReadbackRgba32f(srgbId, npix)
        Try : OpenTK.Graphics.OpenGL4.GL.DeleteTexture(srgbId) : Catch : End Try
        If acc Is Nothing Then
            Logger.LogLazy(Function() "[FACEBAKE][SSE] _2d ABORT: readback del resultado GPU fallo.")
            Return
        End If

        ' acc (sRGB, ya convertido arriba) -> BGRA. ClampByte255 de esta clase espera 0..255 (NO multiplica).
        Dim gbuf(npix * 4 - 1) As Byte
        For i = 0 To npix - 1
            gbuf(i * 4) = ClampByte255(acc(i * 4 + 2) * 255.0)      ' B
            gbuf(i * 4 + 1) = ClampByte255(acc(i * 4 + 1) * 255.0)  ' G
            gbuf(i * 4 + 2) = ClampByte255(acc(i * 4) * 255.0)      ' R
            gbuf(i * 4 + 3) = 255
        Next

        ' Resolución de salida = Setting_FaceGenDiffuseResolution (Inherit→nativo no-op; resample filtro FO4 = release/_2c).
        Dim gpW = w, gpH = h, gpBuf = gbuf
        If OutputSettings.Diffuse <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
            Dim gt = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, Math.Min(w, h))
            gpBuf = FaceTintCpuCompositor.ResampleBgra(gbuf, w, h, gt, gt) : gpW = gt : gpH = gt
        End If
        Dim mips = CInt(Math.Floor(Math.Log(Math.Min(gpW, gpH), 2))) + 1
        Dim dds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(gpW, gpH, gpBuf, DiffuseDxgiFromSetting(), generateMipMaps:=True, generatedMipLevels:=mips)
        If dds Is Nothing Then Return
        Dim rel = FaceGenPaths.TexturaDds(FaceGenPaths.CanalDiffuse, originPlugin, fgLocal, "_2d")
        Dim outFile = IO.Path.Combine(BakeOutputRoot.Current(), rel)
        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
        IO.File.WriteAllBytes(outFile, dds)
        MaybeWriteTgaBeside(outFile, gpW, gpH, gpBuf)
        Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2d pliegue PURO GPU (float) -> {rel} ({dds.Length}b, {w}x{h})")
    End Sub

    ''' <summary>Cuando "Generate TGA" está marcado (<see cref="WriteTGASandboxOutput"/>, = FO4), escribe un TGA
    ''' UNCOMPRESSED lossless al lado del .dds indicado, desde el MISMO BGRA que se encodeó (no re-decodea el BCn).
    ''' No-op si el toggle está off o el BGRA es Nothing. Espejo del dump TGA del bake FO4 (BakeFaceTextures).</summary>
    Private Sub MaybeWriteTgaBeside(ddsAbsPath As String, w As Integer, h As Integer, bgra As Byte())
        If Not WriteTGASandboxOutput OrElse bgra Is Nothing OrElse String.IsNullOrEmpty(ddsAbsPath) OrElse w <= 0 OrElse h <= 0 Then Return
        Try
            Dim tga = IO.Path.ChangeExtension(ddsAbsPath, "tga")
            FaceTintCompositor.WriteBgraToTga(tga, bgra, w, h)
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] wrote TGA '{tga}'")
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] TGA write failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Readback de una textura GL RGBA8 a BGRA (W·H·4 bytes). Nothing si falla. GL-bound.</summary>
    Private Function ReadbackGlBgra(texId As Integer, npix As Integer) As Byte()
        If texId = 0 OrElse npix <= 0 Then Return Nothing
        Dim gbuf(npix * 4 - 1) As Byte
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, texId)
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(gbuf, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.GetTexImage(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        Return gbuf
    End Function

    ''' <summary>Path embebido para el SLOT 6 (facetint) en SSE: <c>data\Textures\…</c>. Es el único slot que
    ''' lleva ese prefijo, porque lo carga otro loader del motor. Un path mal prefijado acá deja el slot en
    ''' NULL y la cara sale MARRÓN. Ley completa y mediciones: <c>40-bake-leyes-sse.md</c>.
    ''' <para>Sólo SSE, y el guard va acá y no en los call sites: que la corrección dependa de en qué
    ''' función estás es el supuesto implícito que se rompe al mover código.</para></summary>
    Private Function EmbeddedEngineTexPath(relUnderData As String) As String
        If String.IsNullOrEmpty(relUnderData) Then Return relUnderData
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return relUnderData
        ' IDEMPOTENTE y agnóstico de la forma de entrada: se normaliza a la raíz y se reconstruye la única forma
        ' que el motor acepta en este slot. Da igual si viene 'Textures\x', 'textures/x', '\Textures\x',
        ' 'data\Textures\x' o 'x' — sale siempre 'data\Textures\x'. Sin esto, un cambio de call site que ya
        ' trajera el prefijo producía 'data\data\Textures\...' en silencio.
        Return "data\Textures\" & StripTexRoot(relUnderData)
    End Function

    ''' <summary>Como <see cref="StripTexRoot"/> pero saca SÓLO el prefijo <c>data\</c>: un <c>textures\</c>
    ''' inicial se CONSERVA VERBATIM, porque el motor lo tolera y el CK lo escribe.
    ''' <para>NO usar para el slot 6: ése se reconstruye entero con <see cref="EmbeddedEngineTexPath"/>.</para></summary>
    Private Function StripDataRootOnly(p As String) As String
        If String.IsNullOrEmpty(p) Then Return ""
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return p
        Dim s = p.Replace("/"c, "\"c).TrimStart("\"c)
        If s.StartsWith("data\", StringComparison.OrdinalIgnoreCase) Then s = s.Substring(5).TrimStart("\"c)
        Return s
    End Function

    Private Function StripTexRoot(p As String) As String
        If String.IsNullOrEmpty(p) Then Return ""
        Dim s = p.Replace("/"c, "\"c).TrimStart("\"c)
        If s.StartsWith("data\", StringComparison.OrdinalIgnoreCase) Then s = s.Substring(5).TrimStart("\"c)
        If s.StartsWith("textures\", StringComparison.OrdinalIgnoreCase) Then s = s.Substring(9).TrimStart("\"c)
        Return s
    End Function

    ''' <summary>Path embebido para los slots que NO son el facetint (0 diffuse, 1 <c>_msn</c>, 3 detail):
    ''' SIN prefijo, relativos a <c>Data\Textures\</c>. NO usar el prefijo del slot 6 acá — estos slots los
    ''' carga otro loader y quedarían en NULL (cara marrón en el camino plegado).
    ''' <para>Sólo SSE; en FO4 la convención es otra y es la fiel al CK. Ver <c>40-bake-leyes-sse.md</c>.</para></summary>
    Private Function EmbeddedTexSetPath(relUnderData As String) As String
        If String.IsNullOrEmpty(relUnderData) Then Return relUnderData
        If Config_App.Current Is Nothing OrElse Config_App.Current.Game <> Config_App.Game_Enum.Skyrim Then Return relUnderData
        ' IDEMPOTENTE: entre 'Textures\x', 'textures/x', '\Textures\x', 'data\Textures\x' o 'x' devuelve siempre
        ' 'x'. Ver StripTexRoot.
        Return StripTexRoot(relUnderData)
    End Function

    ''' <summary>Borra los artefactos que SÓLO produce el camino PLEGADO (<c>FaceDiffuse\</c> y
    ''' <c>FaceNormal\</c>) cuando este bake NO pliega: si no, sobreviven de una corrida anterior y el packer
    ''' los mete al archive, porque toma el source del DISCO. Se borran los dos naming (canónico y <c>_2</c>),
    ''' porque alternar Debug/Release deja stale de ambos.
    ''' <para>NO borrar acá <c>FaceTint\</c>: es el único artefacto que existe en LOS DOS caminos, y
    ''' <see cref="WriteSseFacetintDds"/> tiene salidas tempranas, así que borrarlo al entrar y no re-escribirlo
    ''' dejaría al NPC SIN tint (cara marrón) en vez de con uno viejo.</para></summary>
    ''' <param name="escritas">Rutas que ESTE bake escribió (<see cref="BuildResult.TexturasEscritas"/>):
    ''' no se tocan. Igual que en FO4, el barrido corre DESPUÉS de escribir y borra sólo lo que quedó del
    ''' bake anterior — borrar antes sacaba los archivos del mod bajo Mod Organizer.</param>
    Private Sub DeleteFoldedOnlyArtifacts(npcFormID As UInteger, originPlugin As String,
                                          escritas As HashSet(Of String))
        If String.IsNullOrEmpty(originPlugin) OrElse Config_App.Current Is Nothing Then Return
        Dim dataPath = BakeOutputRoot.Current()
        If String.IsNullOrEmpty(dataPath) Then Return
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        ' ⛔ Se RECORRE la tabla del módulo, no una lista escrita acá. Estaban los nombres de canal
        ' ({"FaceDiffuse","FaceNormal"}) y el par sufijo+extensión ({".dds","_2.dds"}) literales, o sea el
        ' MISMO defecto que `SalidasFo4` ya había cerrado del lado de FO4: un canal nuevo en FaceGenPaths
        ' dejaba su resto sin barrer y nada avisaba. La extensión tampoco se escribe acá — la pone
        ' `TexturaDds`, que es la misma función con la que el bake arma la ruta que ESCRIBE.
        For Each canal In FaceGenPaths.CanalesPlegadosSse
            For Each sufijo In {"", FaceGenPaths.SufijoSandbox}
                Dim rel = FaceGenPaths.TexturaDds(canal, originPlugin, formIdLow, sufijo)
                Dim full = IO.Path.Combine(dataPath, rel)
                If escritas IsNot Nothing AndAlso escritas.Contains(full) Then Continue For
                Try
                    If IO.File.Exists(full) Then
                        IO.File.Delete(full)
                        Logger.LogLazy(Function() $"[FACEBAKE][SSE] stale del camino PLEGADO borrado (este bake NO pliega): {rel}")
                    End If
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] no se pudo borrar el stale '{rel}': {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
        Next
    End Sub

    ''' <param name="result">BuildResult del bake, para <see cref="RecordTextureFailure"/>. Se reportan SOLO los
    ''' fallos REALES, no la salida vanilla, que es el caso normal de un NPC sin overlays.</param>
    ''' <summary>Contraparte FO4 de <see cref="DeleteFoldedOnlyArtifacts"/>: borra los _d/_msn/_s de
    ''' FaceCustomization de un bake ANTERIOR, en los dos naming (canonico y <c>_2</c>), justo antes de que este
    ''' bake escriba los suyos.
    ''' <para>⛔ El problema: <see cref="BakeFaceTextures"/> saltea un slot cuando el head source no tiene ese
    ''' canal, y eso es legitimo. El archivo de la corrida anterior QUEDA en disco, y el packer arma el bundle
    ''' leyendo el DISCO, asi que se lo lleva al BA2 aunque este bake no lo haya producido y el NIF no lo
    ''' referencie: el bundle termina siendo mezcla de dos bakes.</para>
    ''' <para>Corre UNA sola vez por NPC: si corriera por shape, la segunda borraria lo que escribio la primera.</para></summary>
    ''' <param name="escritas">Rutas que ESTE bake escribió (<see cref="BuildResult.TexturasEscritas"/>).
    ''' No se tocan: lo que se borra es lo que quedó del bake anterior y este no reescribió.</param>
    Private Sub DeleteStaleFaceCustomizationArtifacts(npcFormID As UInteger, originPlugin As String,
                                                     escritas As HashSet(Of String))
        If String.IsNullOrEmpty(originPlugin) OrElse Config_App.Current Is Nothing Then Return
        Dim dataPath = BakeOutputRoot.Current()
        If String.IsNullOrEmpty(dataPath) Then Return
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        ' Se recorre la MISMA tabla que el plan de slots del bake y la lista de specs del packer. Estaba
        ' escrita aca otra vez -{"_d","_msn","_s"} a mano-, asi que una fila nueva en SalidasFo4 dejaba su
        ' resto sin barrer sin que nada avisara.
        For Each salidaFo4 In FaceGenPaths.SalidasFo4
            For Each sandbox In {False, True}
                Dim rel = FaceGenPaths.CustomizacionDds(originPlugin, formIdLow, salidaFo4, sandbox)
                Dim full = IO.Path.Combine(dataPath, rel)
                ' ⛔ La ruta se compara TAL CUAL se arma en los dos lados (mismo BakeOutputRoot + mismo
                ' CustomizacionDir). Nada de Path.GetFullPath: resuelve contra el directorio actual del
                ' proceso, que lo mueve cualquier diálogo de archivo, y ahí el barrido borraría lo que
                ' se acaba de hornear.
                If escritas IsNot Nothing AndAlso escritas.Contains(full) Then Continue For
                Try
                    If IO.File.Exists(full) Then
                        IO.File.Delete(full)
                        Logger.LogLazy(Function() $"[FACEBAKE] stale de un bake anterior borrado (este bake no lo reescribió): {rel}")
                    End If
                Catch ex As Exception
                    Dim tD = ex.GetType().Name, mD = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE] no se pudo borrar el stale '{rel}': {tD}: {mD}")
                End Try
            Next
        Next
    End Sub

    ''' <summary>Saca el prefijo <c>data\</c> de los slots 0/1/3 del head, SIEMPRE (pliegue o no): ese prefijo
    ''' es el del slot 6 y en estos slots deja el path en NULL ⇒ cara marrón.
    ''' <para>NO extender esto a pelar también <c>textures\</c>: el motor lo tolera y el CK lo ESCRIBE, así
    ''' que sacarlo es una sobre-corrección. Ley del CK ("cada slot verbatim desde quien lo provee") y su
    ''' medición sobre el corpus completo: <c>40-bake-leyes-sse.md</c>.</para>
    ''' <para>Idempotente y game-aware (no-op en FO4). El slot 6 queda fuera: lleva la convención opuesta.</para></summary>
    Private Sub NormalizeSseHeadTexSetSlots(nif As Nifcontent_Class_Manolo, cloned As INiShape, npcFormID As UInteger)
        Try
            Dim spr = cloned.ShaderPropertyRef
            If spr Is Nothing OrElse spr.Index < 0 Then Return
            Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
            If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then Return
            Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
            If ts Is Nothing OrElse ts.Textures Is Nothing Then Return
            For Each slot In {0, 1, 3}
                If ts.Textures.Count <= slot Then Continue For
                Dim before = If(ts.Textures(slot).Content, "")
                If String.IsNullOrEmpty(before) Then Continue For
                Dim after = StripDataRootOnly(before)
                If Not String.Equals(before, after, StringComparison.Ordinal) Then
                    ts.Textures(slot).Content = after
                    Dim sL = slot, bL = before, aL = after
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] slot {sL} NORMALIZADO: '{bL}' -> '{aL}' (un path relativo a Data\Textures\ no puede llevar el prefijo 'data\'; el 'textures\' SI se conserva — el CK lo escribe y el motor lo tolera)")
                End If
            Next
        Catch ex As Exception
            Dim tN = ex.GetType().Name, mN = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] no se pudieron normalizar los slots del head: {tN}: {mN}")
        End Try
    End Sub

    Private Sub WriteSseFaceDiffuseWithOverlays(nif As Nifcontent_Class_Manolo, cloned As INiShape, npcFormID As UInteger,
                                                originPlugin As String, pluginManager As PluginManager, npcData As NPC_Data,
                                                appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                                willBePacked As Boolean, result As BuildResult,
                                                Optional forcedSuffix As String = Nothing,
                                                Optional complexionPathOverride As String = Nothing,
                                                Optional normalPathOverride As String = Nothing,
                                                Optional detailPathOverride As String = Nothing)
        Try
            Dim forced = Not String.IsNullOrEmpty(forcedSuffix)
            ' El toggle "Bake RaceMenu overlays" NO aplica al forzado (_2c): el _2c ejercita el replacer completo
            ' aunque el usuario tenga el bake de overlays apagado. Sólo gatea el path normal (gateado por overlays).
            If Config_App.Current Is Nothing Then Return

            ' ⛔ BORRADO DE LOS STALE DEL CAMINO PLEGADO, AL ENTRAR Y SIN CONDICIONES. Los FaceDiffuse\<id>.dds y
            ' FaceNormal\<id>.dds los produce SOLO el camino plegado: si un bake anterior plego y este no, quedan
            ' en disco y el packer los mete al archive igual (toma el Source del DISCO) aunque el NIF nuevo
            ' apunte al complexion vanilla, o sea un bundle mezcla de dos bakes.
            ' Antes esto vivia en UNA sola de las ~12 salidas de esta funcion y las otras once se iban dejando el
            ' stale. Ponerlo ACA hace imposible que una salida nueva se lo saltee: es la unica forma de que "el
            ' camino elegido es el UNICO que deja archivos" sea una invariante y no una lista de call sites.
            ' El sandbox forzado (_2c) se excluye: corre DESPUES del pass normal sobre el mismo NIF y borraria
            ' justamente los _2.dds que ese pass acaba de escribir.
            '
            ' ⛔ El barrido va al FINAL de esta funcion, no aca: borrarlos antes los sacaba del mod bajo Mod
            ' Organizer (el borrado los saca del arbol virtual y lo que se escribe despues es un archivo
            ' nuevo, que cae en `overwrite`). Corriendo al final se borra solo lo que este bake NO reescribio,
            ' que es lo que este barrido queria sacar. El Try/Finally cubre las ~12 salidas de la funcion,
            ' que es la invariante que el comentario de arriba pide.
            Try

            If Not forced AndAlso Not Config_App.Current.Setting_BakeSseRaceMenuOverlays Then Return
            ' Fuentes a bakear: (a) RaceMenu Face [Ovl] overlays del preset (si hay) + (b) skee MASKT masks del
            ' head shape. Gate BARATO (sin decode): salir solo si NINGUNA de las dos aporta → vanilla intacto.
            ' En modo FORZADO (_2c) el gate NO aplica: se corre el replacer completo igual.
            Dim preset As LooksmenuLoader.LooksmenuPreset = Nothing
            If appliedPresets IsNot Nothing Then appliedPresets.TryGetValue(npcFormID, preset)
            Dim overlays = If(preset IsNot Nothing, preset.SseBodyOverlays, Nothing)
            ' El gate mira DIFFUSE **O** NORMAL (HasAnyFoldableFaceOverlay). Un overlay de cara SOLO-NORMAL
            ' (NormalPath sin DiffusePath) es válido — ComposeFaceOverlayNormalsIntoMsn lo pliega usando el alpha
            ' del propio normal como cobertura. Gatear sólo por diffuse hacía SALIR TEMPRANO y el normal no se
            ' plegaba nunca; y como el script Papyrus salteaba TODO nodo Face* (la cara era territorio del bake,
            ' siempre), ese overlay no lo aplicaba nadie: desaparecía.
            ' HOY EL EMISOR NO SALTEA TODO Face*: saltea los del pool NORMAL y sólo con el toggle de bake
            ' prendido; los `Face [SOvl]` (pool MAGIC) van SIEMPRE por el script, porque este gate —vía
            ' HasAnyFoldableFaceOverlay ⇒ IsFoldableFaceOverlay— los EXCLUYE del pliegue por diseño. Sin esa
            ' exclusión se hornearía permanentemente una capa que el motor prende y apaga en runtime.
            Dim hasOverlays = SseOverlayCompositor.HasAnyFoldableFaceOverlay(overlays)
            Dim hasSkee = SseSkeeMaskReader.HasMaskLayers(nif, cloned)

            ' DIAGNOSTICO DEL GATE. El render y el bake leen el MISMO `appliedPresets`, así que tienen que
            ' decidir igual — y se midió una grabada donde el render plegó (faceOverlays=1) y 16 s después el bake
            ' NO plegó. Esto vuelca las entradas EXACTAS de la decisión para que la próxima corrida diga cuál de
            ' las tres cosas pasó: no se resolvió el preset, el nodo no es Face, o no pasa el filtro de
            ' diffuse/normal/opacidad. Sin esto sólo se ve el resultado, no la causa.
            If Logger.Enabled Then
                Dim presetOk = (preset IsNot Nothing)
                Dim nAll = If(overlays Is Nothing, -1, overlays.Count)
                Dim faceList = SseOverlayCompositor.FaceOverlaysOnly(overlays)
                Dim detail As String = ""
                For Each ov In faceList
                    detail &= $"{vbCrLf}        nodo='{ov.NodeName}' diffuse='{If(ov.DiffusePath, "")}' normal='{If(ov.NormalPath, "")}' " &
                              $"hasAlpha={ov.HasAlpha} alpha={If(ov.HasAlpha, ov.Alpha, 1.0F)} visible={SseOverlayCompositor.OverlayIsVisible(ov)}"
                Next
                If faceList.Count = 0 Then detail = vbCrLf & "        (ningun nodo Face en el preset)"
                Dim hO = hasOverlays, hS = hasSkee, nF = faceList.Count
                Logger.LogLazy(Function() $"[FACEBAKE][SSE][GATE] npc=0x{npcFormID:X8} presetResuelto={presetOk} overlaysEnPreset={nAll} nodosFace={nF} " &
                                          $"hasOverlays={hO} hasSkee={hS} ⇒ {If(hO OrElse hS, "PLIEGA", "NO PLIEGA")}" & detail)
            End If
            ' VANILLA (no se pliega): el slot 0 queda intacto y NO se produce FaceDiffuse/FaceNormal. Los stale del
            ' camino plegado ya se borraron al entrar (ver arriba), así que acá sólo hay que salir.
            If Not forced AndAlso Not (hasOverlays OrElse hasSkee) Then Return

            ' Head shape's resolved slot-0 diffuse (the complexion base we overlay ONTO).
            Dim spr = cloned.ShaderPropertyRef
            If spr Is Nothing OrElse spr.Index < 0 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: ShaderPropertyRef null")
                RecordTextureFailure(result, "fold SSE: the head shape has no ShaderPropertyRef")
                Return
            End If
            Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
            If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: BSLightingShaderProperty/TextureSetRef null")
                RecordTextureFailure(result, "fold SSE: the head shape has no BSLightingShaderProperty/TextureSet")
                Return
            End If
            Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
            If ts Is Nothing OrElse ts.Textures Is Nothing OrElse ts.Textures.Count < 1 Then
                If forced Then Logger.LogLazy(Function() "[FACEBAKE][SSE] _2c ABORT: BSShaderTextureSet null/empty")
                RecordTextureFailure(result, "fold SSE: the head's BSShaderTextureSet is empty")
                Return
            End If
            ' Complexion base = slot 0, SALVO override (forzado _2c: el pass normal ya pudo mutar slot0 a un diffuse
            ' plegado ⇒ para NO doble-plegar, el forzado recibe el complexion ORIGINAL capturado antes de mutar).
            Dim diffPath = If(forced AndAlso Not String.IsNullOrEmpty(complexionPathOverride), complexionPathOverride, ts.Textures(0).Content)
            If String.IsNullOrEmpty(diffPath) Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion path empty (override='{complexionPathOverride}', slot0='{ts.Textures(0).Content}')")
                RecordTextureFailure(result, "fold SSE: the head's slot 0 (complexion) is empty — there is no base to fold onto")
                Return
            End If

            ' Decode the complexion at its native size (mip0).
            Dim srcBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(diffPath))
            If srcBytes Is Nothing Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion bytes not found for '{diffPath}'")
                RecordTextureFailure(result, $"fold SSE: the complexion '{diffPath}' is neither on disk nor in the archives")
                Return
            End If
            Dim decoded = FaceTintCpuCompositor.DecodeDds(srcBytes)
            If decoded Is Nothing OrElse decoded.Rgba8 Is Nothing OrElse decoded.Width <= 0 OrElse decoded.Height <= 0 Then
                If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: complexion decode failed for '{diffPath}'")
                RecordTextureFailure(result, $"fold SSE: the complexion '{diffPath}' could not be decoded")
                Return
            End If
            Dim w = decoded.Width, h = decoded.Height
            Dim npix = w * h
            Dim acc(npix * 4 - 1) As Single
            decoded.CopyUnitTo(acc)

            ' === PLIEGUE (orden fiel a RaceMenu) ===
            ' El overlay va DESPUÉS del skin tint. El engine hace albedo = softlight(diffuse, facetint_d) × amplify(detail).
            ' Para que el overlay NO quede teñido por el skin tint, plegamos esa cadena DENTRO del diffuse: la base
            ' sobre la que van los overlays es el albedo YA tintado. base ES el albedo skin-tinted; overlays encima.
            ' (Este comentario decía "y neutralizamos los slots 3 y 6". YA NO: los dos quedan con su contenido
            '  REAL y la cadena del motor se cancela con PreCompensateEngineChain, más abajo.)
            ' Facetint y detail HOISTEADOS: los consume el fold y después la pre-compensación (fuera de este scope).
            Dim detailAcc As Single() = Nothing
            Dim facetint As Single() = Nothing
            Dim npcRec = pluginManager.GetRecord(npcFormID)
            Dim raceFid As UInteger = npcData.Record.Race
            Dim race As Canon.IRace = Nothing
            If npcRec IsNot Nothing AndAlso raceFid <> 0UI Then
                Dim rr = pluginManager.GetRecord(raceFid)
                If rr IsNot Nothing AndAlso rr.Header.Signature = "RACE" Then race = Canon.CanonRecords.Race(rr, pluginManager)
            End If
            If npcRec IsNot Nothing AndAlso race IsNot Nothing Then
                ' facetint _d LINEAL al tamaño del complexion (misma resolución que el diffuse que multiplica).
                ' Es SOLO los tints de RACE (skin tone + warpaint) — los overlays de cara NO van acá (van sobre el
                ' base DESPUÉS del pliegue, ese es el orden de RaceMenu). Mismo _d que WriteSseFacetintDds compone.
                facetint = SseFaceTintComposer.ComposeLinearRgba(pluginManager, npcRec, race, raceFid, npcData.Record.ConfigurationFlagsFemale, w, h,
                                                                     Nothing, SseFaceTintComposer.CapasDeTinteSse(npcData.Record), npcData.SseTintTexOverride)
                ' Detail (slot 3): es el término AMPLIFICADO que el motor multiplica DESPUÉS del soft-light
                ' con el facetint. Se pliega acá para que la BASE sobre la que van los overlays sea el albedo
                ' completo (el orden de RaceMenu), y al final se PRE-COMPENSA — el slot 3 se deja con su
                ' contenido REAL, no se neutraliza (ver el bloque del slot 3 más abajo). Es detail crudo; si no
                ' hay, el default del motor es 0.251, que NO es identidad.
                ' En el sandbox forzado hay que usar el detail ORIGINAL capturado antes de mutar: el shape
                ' clonado se comparte con el pass normal, así que leerlo en vivo puede dar vacío y el fold
                ' caería al default en vez del detail real.
                Dim detailPath = If(forced, If(detailPathOverride, ""), If(ts.Textures.Count > 3, ts.Textures(3).Content, ""))
                detailAcc = If(Not String.IsNullOrEmpty(detailPath), SseFaceTintComposer.DecodeTextureRgba(detailPath, w, h), Nothing)
                If facetint IsNot Nothing Then SseFaceGenBaker.FoldFacetintIntoDiffuse(acc, facetint, npix, detailAcc)   ' albedo = softlight(complexion, facetint) x amplify(detail)
            End If

            ' (a) skee MASKT masks (dyeable heads) sobre el base plegado, luego (b) los Face [Ovl] overlays
            ' (orden por índice de nodo, = skee/render). Cualquiera puede faltar; OR de las dos.
            Dim skinRgb = SseSkinRgbForNpc(pluginManager, npcData, npcFormID)
            Dim anySkee = SseSkeeMaskReader.ComposeNifMaskLayersIntoDiffuse(nif, cloned, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba, skinRgb, Nothing, acc)
            Dim anyOvl = SseOverlayCompositor.ComposeFaceOverlaysIntoDiffuse(acc, overlays, w, h, AddressOf SseFaceTintComposer.DecodeTextureRgba)
            ' EL GATE DIJO QUE SÍ Y EL COMPOSE NO APORTÓ NADA ⇒ ES UN FALLO, NO UN NO-OP.
            ' Los gates (HasAnyFoldableFaceOverlay / HasMaskLayers) ya replican todo lo que se puede saber SIN tocar
            ' el disco: nodo Face, ruta de textura y opacidad > 0. Lo único que queda fuera es que la textura exista
            ' y decodifique — y si eso falla, la cara pierde su face-paint. Antes se salía en silencio (y encima sin
            ' borrar los stale). Ahora el borrado ya ocurrió al entrar y el fallo se reporta.
            If Not forced AndAlso Not (anySkee OrElse anyOvl) Then
                RecordTextureFailure(result, "fold SSE: the NPC declares face overlays/skee masks but NONE could be composed (see [SSE-OVL]/[SSE-SKEE] in the log: texture missing or unreadable)")
                Return
            End If

            ' PRE-COMPENSACIÓN DE LA CADENA DEL MOTOR: es lo que hace que el juego muestre lo mismo que el
            ' preview. Hasta acá `acc` es el albedo que dibuja el preview; el motor va a calcular
            ' softlight(slot0, facetint) x amp(detail) con los dos slots en su contenido REAL, así que se
            ' invierten ambos términos y la cadena se cancela.
            ' NO neutralizar el facetint a gris: da el albedo aritméticamente exacto y AUN ASÍ la cara sale
            ' oscura in-game, porque el motor deriva del slot 6 algo más que el albedo (subsurface) y eso no se
            ' puede plegar en una textura de diffuse.
            SseFaceGenBaker.PreCompensateEngineChain(acc, facetint, detailAcc, npix)

            ' Paralelo por rangos + VECTORIZADO: la conversión float→byte es puramente por píxel (sin estado
            ' compartido) ⇒ bit-idéntica al serial. Pesa porque el fold corre a resolución NATIVA: 16,7 M
            ' iteraciones a 4096².
            ' La ley es la de ClampByte255 (redondeo en DOUBLE) y por eso NO usa el byte-pack de FO4, que
            ' redondea en Single: cerca de los .5 dan bytes distintos. Ver el comentario del helper.
            Dim bgra(w * h * 4 - 1) As Byte
            FaceTintCpuCompositor.PackUnitRgbaToBgraRoundDouble(acc, bgra, w * h)
            ' Resolución de salida = Setting_FaceGenDiffuseResolution (Inherit→nativo = no-op byte-inerte; 1024/2048/…
            ' resamplea con el MISMO filtro bilineal GL_LINEAR+clamp que el compositor FO4 → matchea el per-layer FO4).
            Dim dOutW = w, dOutH = h, dOutBgra = bgra
            If OutputSettings.Diffuse <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
                Dim t = FaceTintConvention.ResolveResolutionSize(OutputSettings.Diffuse, Math.Min(w, h))
                dOutBgra = FaceTintCpuCompositor.ResampleBgra(bgra, w, h, t, t) : dOutW = t : dOutH = t
            End If
            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(dOutW, dOutH), 2))) + 1
            ' Compresión del diffuse = la que elige el usuario en CharGen Options (no hardcode).
            ' SkipDdsEncode saltea BCn+mips y la escritura, pero el slot del NIF se escribe IGUAL porque su path
            ' es determinista ⇒ un barrido valida el MISMO NIF sin pagar el costo dominante.
            Dim outDds As Byte() = Nothing
            If Not SkipDdsEncode Then
                outDds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                    width:=dOutW, height:=dOutH, bgraPixels:=dOutBgra,
                    outputDxgiFormat:=DiffuseDxgiFromSetting(),
                    generateMipMaps:=True, generatedMipLevels:=mipLevels)
                If outDds Is Nothing Then
                    If forced Then Logger.LogLazy(Function() $"[FACEBAKE][SSE] _2c ABORT: encode returned Nothing ({w}x{h}, dxgi={DiffuseDxgiFromSetting()})")
                    RecordTextureFailure(result, $"fold SSE: encoding the folded diffuse failed ({dOutW}x{dOutH}, dxgi={DiffuseDxgiFromSetting()})")
                    Return
                End If
            End If

            Dim fgLocal = PluginManager.ToFaceGenLocalFormID(npcFormID)
            Dim dir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalDiffuse, originPlugin)
            ' Naming: forzado (_2c) usa ESE sufijo en disco Y embebido (nunca packea); normal = _2/canónico.
            Dim suffix = If(forced, forcedSuffix & ".dds", If(DebugMode, "_2.dds", ".dds"))
            Dim embeddedSuffix = If(forced, suffix, If(willBePacked, ".dds", suffix))
            Dim rel = dir & $"{fgLocal:X8}{suffix}"
            Dim outFile = IO.Path.Combine(BakeOutputRoot.Current(), rel)
            If Not SkipDdsEncode Then
                Try
                    IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(outFile))
                    IO.File.WriteAllBytes(outFile, outDds)
                    result?.TexturasEscritas.Add(outFile)   ' lo que este bake escribió: el barrido no lo toca
                    ' Y la IDENTIDAD, que es de lo UNICO que depende el packer para meter este DDS al
                    ' archive: lleva tag (SseHeadDiffuse) => nunca es "requerido" => si no esta marcado
                    ' como escrito, se saltea. Sin esta linea el fold per-NPC de SSE se escribia en disco,
                    ' el NIF quedaba apuntandolo y el archive NO lo llevaba: en la maquina del autor se ve
                    ' bien -el suelto esta- y en la del que instala el mod, cara marron.
                    ' `Not forced` a proposito: el pase _2c escribe <id>_2c.dds, que NO es el spec.
                    If result IsNot Nothing AndAlso Not forced Then
                        result.SalidasDeTexturaEscritas = result.SalidasDeTexturaEscritas Or FaceGenPaths.SalidaDeTexturaDeCara.SseHeadDiffuse
                    End If
                Catch exW As Exception
                    Dim tW = exW.GetType().Name, mW = exW.Message
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] no se pudo escribir '{rel}': {tW}: {mW}")
                    RecordTextureFailure(result, $"fold SSE: could not write the folded diffuse: {tW}: {mW}")
                    Return
                End Try
                MaybeWriteTgaBeside(outFile, dOutW, dOutH, dOutBgra)
            End If
            ' Slot 0 = texture-set normal ⇒ SIN prefijo (ver EmbeddedTexSetPath: el `data\` es SÓLO del slot 6).
            ts.Textures(0).Content = EmbeddedTexSetPath(dir & $"{fgLocal:X8}{embeddedSuffix}")

            ' EL SLOT 3 (detail) NO SE TOCA — no volver a escribir un "neutro" ahí. Razones medidas:
            '   1) el motor puede REINSTALAR el slot 3 desde el TXST resuelto al attachear la cabeza, y ahí el
            '      neutro se descarta y el amplify se aplica DOS veces (cara ~2 % más oscura);
            '   2) BC3 no puede codificar el valor neutro exacto, así que ni siquiera da amplify 1,0;
            '   3) era el único artefacto COMPARTIDO por plugin, y el load order decidía cuál servía.
            ' Con el detail REAL en el slot 3 el motor aplica amplify una sola vez, igual que en el camino no
            ' plegado, y el resultado es correcto tanto si respeta el slot como si lo reinstala.

            ' EL SLOT 6 YA NO SE NEUTRALIZA — en NINGÚN camino. MEDIDO in-game: con el gris el albedo daba
            ' aritméticamente exacto (motor vs preview = +0,37%) y AUN ASÍ la cara salía oscura; con el facetint
            ' REAL desaparece el oscurecimiento ⇒ el motor deriva del slot 6 algo MÁS que el albedo (subsurface),
            ' que no se puede plegar en una textura de diffuse. El slot 6 conserva el facetint real y la cadena
            ' del motor se cancela por PreCompensateEngineChain.
            Dim outLen = If(outDds Is Nothing, 0, outDds.Length)   ' Nothing en la rama SkipDdsEncode
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] face diffuse+overlays -> {rel} ({outLen}b{If(SkipDdsEncode, ", encode SALTEADO", "")}, {w}x{h}); slots 3/6 = REALES, cadena pre-compensada")

            ' === NORMALES: en el _msn del head (slot 1). Non-forced: SOLO si un overlay aporta normal (compone
            ' decode→lerp cobertura→RENORMALIZE→encode). FORZADO (_2c): SIEMPRE se emite el _n (re-encodea el _msn del
            ' head, con overlays si los hay) para que el replacer sea completo _d+_n. Formato = la propiedad
            ' Setting_FaceGenNormalCompression (NormalDxgiFromSetting), DEFAULT Uncompressed = formato VANILLA del _msn
            ' de SSE (32bpp RGBA8, MEDIDO del BSA) — NO BC7 (los _msn BC7 sueltos son mods; y BC7 crasheaba el encode). ===
            If ts.Textures.Count > 1 AndAlso (forced OrElse SseOverlayCompositor.HasFaceOverlayNormals(overlays)) Then
                Try
                    Dim msnPath = If(forced AndAlso Not String.IsNullOrEmpty(normalPathOverride), normalPathOverride, ts.Textures(1).Content)
                    If Not String.IsNullOrEmpty(msnPath) Then
                        Dim msnBytes = FilesDictionary_class.GetBytes(FO4UnifiedMaterial_Class.CorrectTexturePath(msnPath))
                        If msnBytes IsNot Nothing Then
                            Dim mDec = FaceTintCpuCompositor.DecodeDds(msnBytes)
                            If mDec IsNot Nothing AndAlso mDec.Rgba8 IsNot Nothing AndAlso mDec.Width > 0 AndAlso mDec.Height > 0 Then
                                Dim mw = mDec.Width, mh = mDec.Height
                                Dim macc(mw * mh * 4 - 1) As Single
                                mDec.CopyUnitTo(macc)
                                ' El _msn vanilla es uncompressed 32bpp (4 canales) ⇒ esto NO corre en el caso normal
                                ' y el resultado queda byte-idéntico. Sólo muerde con un _msn MODEADO de 2 canales,
                                ' donde el pack de DecodeDds habría dado B=0 ⇒ z=−1 en TODA la cabeza. Ojo: para un
                                ' model-space la reconstrucción no puede recuperar un z genuinamente negativo — un
                                ' _msn en 2 canales ya es una fuente inválida (es lo que CharGen Options impide
                                ' generar); esto es RECUPERACIÓN, no garantía. Sin esto era basura muda.
                                If mDec.Channels < 3 Then
                                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] el _msn de la cabeza trae SÓLO {mDec.Channels} canales (BC5/R8G8): se reconstruye el eje Z. Un _msn model-space válido es de 4 canales — revisá el replacer instalado.")
                                    FaceTintCpuCompositor.ReconstructNormalZ(macc, mw * mh)
                                End If
                                ' Compone overlay-normals si los hay (in-place). En forced sin overlays queda el head
                                ' normal tal cual → se re-encodea igual (replacer _n self-contained).
                                ' decodeNormal = el decode VECTORIAL (reconstruye Z de un BC5) — los normales de los
                                ' face-paint de RaceMenu son tangent-space y BC5 es su formato estándar. El de color
                                ' se sigue usando para la COBERTURA (el alpha del diffuse del overlay).
                                Dim composedN = SseOverlayCompositor.ComposeFaceOverlayNormalsIntoMsn(
                                    macc, overlays, mw, mh,
                                    AddressOf SseFaceTintComposer.DecodeTextureRgba,
                                    AddressOf SseFaceTintComposer.DecodeNormalRgba)
                                If composedN OrElse forced Then
                                    ' Paralelo por rangos + vectorizado, MISMA justificación y MISMO helper que
                                    ' la conversión del diffuse de arriba: por-píxel puro, escrituras disjuntas
                                    ' ⇒ bit-idéntico al serial. El _msn se procesa a la resolución nativa del
                                    ' head normal (1024²-4096²).
                                    Dim mbgra(mw * mh * 4 - 1) As Byte
                                    FaceTintCpuCompositor.PackUnitRgbaToBgraRoundDouble(macc, mbgra, mw * mh)
                                    ' Resolución = Setting_FaceGenNormalResolution (Inherit→nativo no-op; resample filtro FO4).
                                    Dim nOutW = mw, nOutH = mh, nOutBgra = mbgra
                                    If OutputSettings.Normal <> FaceTintConvention.FaceTintChannelResolution.Inherit Then
                                        Dim t = FaceTintConvention.ResolveResolutionSize(OutputSettings.Normal, Math.Min(mw, mh))
                                        nOutBgra = FaceTintCpuCompositor.ResampleBgra(mbgra, mw, mh, t, t) : nOutW = t : nOutH = t
                                    End If
                                    Dim mmips = CInt(Math.Floor(Math.Log(Math.Min(nOutW, nOutH), 2))) + 1
                                    ' Formato = propiedad Setting_FaceGenNormalCompression. NO hardcodeado.
                                    Dim mDds = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                                        width:=nOutW, height:=nOutH, bgraPixels:=nOutBgra,
                                        outputDxgiFormat:=NormalDxgiFromSetting(),
                                        generateMipMaps:=True, generatedMipLevels:=mmips)
                                    If mDds IsNot Nothing Then
                                        Dim ndir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalNormal, originPlugin)
                                        Dim nRel = ndir & $"{fgLocal:X8}{suffix}"
                                        Dim nFile = IO.Path.Combine(BakeOutputRoot.Current(), nRel)
                                        IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(nFile))
                                        IO.File.WriteAllBytes(nFile, mDds)
                                        result?.TexturasEscritas.Add(nFile)   ' idem: el barrido no lo toca
                                        ' Identidad, mismo motivo que el diffuse de arriba.
                                        If result IsNot Nothing AndAlso Not forced Then
                                            result.SalidasDeTexturaEscritas = result.SalidasDeTexturaEscritas Or FaceGenPaths.SalidaDeTexturaDeCara.SseHeadNormal
                                        End If
                                        MaybeWriteTgaBeside(nFile, nOutW, nOutH, nOutBgra)
                                        ' Slot 1 = texture-set normal ⇒ SIN prefijo. Ver EmbeddedTexSetPath.
                                        ts.Textures(1).Content = EmbeddedTexSetPath(ndir & $"{fgLocal:X8}{embeddedSuffix}")
                                        Logger.LogLazy(Function() $"[FACEBAKE][SSE] face normal+overlays -> {nRel} ({mDds.Length}b, {mw}x{mh})")
                                    End If
                                End If
                            End If
                        End If
                    End If
                Catch exM As Exception
                    Logger.LogLazy(Function() $"[FACEBAKE][SSE] face normal bake failed: {exM.GetType().Name}: {exM.Message}")
                End Try
            End If

            ' El sandbox _2b del DIFFUSE (overlays por GPU sobre un base plegado en CPU) SE ELIMINÓ: era un camino
            ' CRUZADO (mitad CPU, mitad GPU) y por eso no medía nada que se ejecute de verdad — ningún camino real
            ' mezcla. Los dos sandboxes que quedan son puros y comparables entre sí: _2c = TODO CPU (= el release) y
            ' _2d = TODO GPU. (El _2b del FACETINT sigue: ese sí es un compose puro GPU del facetint.)
            Finally
                ' Acá se barre lo que quedó del bake anterior y ESTE no reescribió. Cubre las ~12 salidas
                ' de la función, incluidos los Return tempranos y el camino de excepción: si el bake no
                ' llegó a escribir nada, el conjunto viene vacío y se borran los cuatro candidatos, que es
                ' exactamente lo que hacía el borrado previo.
                If Not forced Then DeleteFoldedOnlyArtifacts(npcFormID, originPlugin, result?.TexturasEscritas)
            End Try
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FACEBAKE][SSE] face diffuse+overlays bake failed: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub


    ''' <summary>Sube un BGRA a una textura GL RGBA8 (linear, clamp). Devuelve 0 si falla. GL-bound.</summary>
    Private Function UploadBgraToGl(bgra As Byte(), w As Integer, h As Integer) As Integer
        Dim id = OpenTK.Graphics.OpenGL4.GL.GenTexture()
        If id = 0 Then Return 0
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, id)
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMinFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMinFilter.Linear))
        OpenTK.Graphics.OpenGL4.GL.TexParameter(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, OpenTK.Graphics.OpenGL4.TextureParameterName.TextureMagFilter, CInt(OpenTK.Graphics.OpenGL4.TextureMagFilter.Linear))
        Dim handle = Runtime.InteropServices.GCHandle.Alloc(bgra, Runtime.InteropServices.GCHandleType.Pinned)
        Try
            OpenTK.Graphics.OpenGL4.GL.TexImage2D(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelInternalFormat.Rgba8, w, h, 0,
                OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, OpenTK.Graphics.OpenGL4.PixelType.UnsignedByte, handle.AddrOfPinnedObject())
        Finally
            handle.Free()
        End Try
        OpenTK.Graphics.OpenGL4.GL.BindTexture(OpenTK.Graphics.OpenGL4.TextureTarget.Texture2D, 0)
        Return id
    End Function

    Private Function ClampByte255(v As Double) As Byte
        Return CByte(Math.Max(0.0, Math.Min(255.0, Math.Round(v))))
    End Function

    ''' <summary>AUTO-TEST del contexto GL: sube un patron conocido, lo pasa por el MISMO pase del compositor
    ''' que usa el bake (<see cref="FaceTintCompositor.ConvertTextureSpace"/>, uMode=2) y verifica que el
    ''' readback lo devuelva. Devuelve Nothing si el GL sirve, o el motivo del fallo.
    ''' <para>POR QUE EXISTE: un contexto GL puede crearse "bien" y despues no dibujar nada —ventana sin
    ''' mostrar, driver que no da un framebuffer usable, contexto current en otro hilo—. El sintoma es un
    ''' readback en CERO, y eso NO se distingue de "el compose dio negro": la corrida reportaria una paridad
    ''' perfecta o una divergencia enorme, las dos inventadas. Mejor fallar acá, antes de medir 200 NPCs.</para>
    ''' <para>Se testea con FBO (que es lo que usa el compositor), NO con el framebuffer por defecto de la
    ''' ventana: es exactamente el camino que despues se va a ejercer.</para></summary>
    ''' (Friend, no Public: <see cref="NpcRenderHost"/> es Friend y un Public lo expondria fuera del proyecto.)
    Friend Function GlSelfTest(host As NpcRenderHost) As String
        If host Is Nothing Then Return "no NpcRenderHost"
        If host.CompositorState Is Nothing Then Return "the host has no CompositorState"
        Const N As Integer = 8
        ' Patron NO uniforme y NO simetrico entre canales: un buffer en cero, o con los canales cruzados, o
        ' recortado, falla. (Un patron constante pasaria un readback de basura constante.)
        Dim src(N * N * 4 - 1) As Byte
        For i = 0 To N * N - 1
            src(i * 4) = CByte((i * 3) Mod 256)          ' B
            src(i * 4 + 1) = CByte((i * 7 + 11) Mod 256) ' G
            src(i * 4 + 2) = CByte((i * 13 + 29) Mod 256)  ' R
            src(i * 4 + 3) = 255
        Next
        Dim texId As Integer = 0, outId As Integer = 0
        Try
            texId = UploadBgraToGl(src, N, N)
            If texId = 0 Then Return "GL.GenTexture/TexImage2D returned 0 (the test texture could not be uploaded)"
            ' Conversion IDENTIDAD (0->0): el shader cortocircuita la curva pero el quad SE DIBUJA igual, asi
            ' que esto ejercita programa + VAO + FBO + readback, que es todo lo que puede fallar.
            outId = FaceTintCompositor.ConvertTextureSpace(host.CompositorState, texId, N, N, 0, 0)
            If outId = 0 Then Return "ConvertTextureSpace returned 0 (the compositor could not draw: FBO/program/VAO)"
            Dim got = ReadbackGlBgra(outId, N * N)
            If got Is Nothing Then Return "the readback returned Nothing"
            If got.Length <> src.Length Then Return $"the readback returned {got.Length} bytes, {src.Length} were expected"
            ' (a) todo igual a un mismo byte = buffer sin dibujar (el caso clasico: todo 0).
            Dim allSame As Boolean = True
            For i = 1 To got.Length - 1
                If got(i) <> got(0) Then allSame = False : Exit For
            Next
            If allSame Then Return $"the readback is CONSTANT (all 0x{got(0):X2}) — the GL drew nothing"
            ' (b) tiene que reproducir el patron. Tolerancia 1: el FBO es Rgba32f y el redondeo de vuelta a
            ' byte puede mover 1 en valores cerca de x.5. Mas de 1 no es redondeo, es otra cosa.
            Dim worst As Integer = 0, worstAt As Integer = -1
            For i = 0 To got.Length - 1
                Dim d = Math.Abs(CInt(got(i)) - CInt(src(i)))
                If d > worst Then worst = d : worstAt = i
            Next
            If worst > 1 Then Return $"the readback does NOT reproduce the pattern (worst delta {worst} at byte {worstAt}; tolerance 1)"
            Return Nothing
        Catch ex As Exception
            Return $"{ex.GetType().Name}: {ex.Message}"
        Finally
            If outId <> 0 Then Try : GL.DeleteTexture(outId) : Catch : End Try
            If texId <> 0 Then Try : GL.DeleteTexture(texId) : Catch : End Try
        End Try
    End Function

    ''' <summary>Lee el content de los slots 0 (diffuse/complexion) y 1 (normal/_msn) del texture-set del head
    ''' shape. Para capturar los paths ORIGINALES antes de que el bake los mute (sandbox _2c). ("","") si no resuelve.</summary>
    Private Function GetSseHeadSlotPaths(nif As Nifcontent_Class_Manolo, cloned As INiShape) As (Slot0 As String, Slot1 As String, Slot3 As String)
        Try
            Dim spr = cloned.ShaderPropertyRef
            If spr Is Nothing OrElse spr.Index < 0 Then Return ("", "", "")
            Dim lsp = TryCast(nif.Blocks(spr.Index), NiflySharp.Blocks.BSLightingShaderProperty)
            If lsp Is Nothing OrElse lsp.TextureSetRef Is Nothing OrElse lsp.TextureSetRef.Index < 0 Then Return ("", "", "")
            Dim ts = TryCast(nif.Blocks(lsp.TextureSetRef.Index), NiflySharp.Blocks.BSShaderTextureSet)
            If ts Is Nothing OrElse ts.Textures Is Nothing Then Return ("", "", "")
            Dim s0 = If(ts.Textures.Count > 0, ts.Textures(0).Content, "")
            Dim s1 = If(ts.Textures.Count > 1, ts.Textures(1).Content, "")
            Dim s3 = If(ts.Textures.Count > 3, ts.Textures(3).Content, "")   ' detail/Displacement (softlight)
            Return (s0, s1, s3)
        Catch
            Return ("", "", "")
        End Try
    End Function

    ''' <summary>NPC skin colour (linear RGB [0,1]) for the skee −2 skin-preset. Reuses the SAME QNAM the SSE
    ''' facetint + body use (SseFaceTintComposer.ResolveSkinToneQnam), so a skee mask tinted "skin" matches the
    ''' rest. Nothing when unresolved (BuildSkeeMaskLayer then falls back to the literal colour).</summary>
    Private Function SseSkinRgbForNpc(pluginManager As PluginManager, npcData As NPC_Data, npcFormID As UInteger) As Double()
        Try
            If pluginManager Is Nothing OrElse npcData Is Nothing OrElse npcData.Record.Race = 0UI Then Return Nothing
            Dim rr = pluginManager.GetRecord(npcData.Record.Race)
            If rr Is Nothing OrElse rr.Header.Signature <> "RACE" Then Return Nothing
            Dim race = Canon.CanonRecords.Race(rr, pluginManager)
            Dim q = SseFaceTintComposer.ResolveSkinToneQnam(pluginManager, npcData, race, npcData.Record.Race, npcData.Record.ConfigurationFlagsFemale)
            If Not q.HasValue Then Return Nothing
            Return New Double() {q.Value.R / 255.0, q.Value.G / 255.0, q.Value.B / 255.0}
        Catch
            Return Nothing
        End Try
    End Function

    ''' <summary>DXGI del diffuse de salida según el setting del usuario (CharGen Options → Format del Diffuse):
    ''' BC3 (default) / BC7 / Uncompressed (B8G8R8A8). Misma tabla que el bake de FO4.
    ''' <para>Es Friend y no Private a propósito: el RENDER llama a ESTA misma función para encodear el
    ''' facetint del preview, así preview y bake salen de una sola fuente en vez de duplicar la lógica.</para></summary>
    Friend Function DiffuseDxgiFromSetting() As Integer
        ' Via OutputSettings ⇒ per-game (SSE vs FO4) y All/per-layer aware, como el bake FO4 (BakeFaceTextures).
        Dim os = If(Config_App.Current IsNot Nothing, OutputSettings, Nothing)
        Dim dc = If(os IsNot Nothing, os.DiffuseCompression, FaceTintConvention.FaceTintDiffuseCompression.Bc3)
        Select Case dc
            Case FaceTintConvention.FaceTintDiffuseCompression.Bc7 : Return DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case FaceTintConvention.FaceTintDiffuseCompression.Uncompressed : Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case Else : Return DirectXTextureConversionHelper.DxgiFormatBc3Unorm
        End Select
    End Function

    ''' <summary>DXGI del NORMAL facegen de SSE (el <c>_msn</c> del slot 1), a partir de
    ''' <see cref="Config_Class.Setting_FaceGenNormalCompression_SSE"/> (CharGen Options) — pero ACOTADO a los
    ''' formatos que ese canal admite. Ver <see cref="ClampMsnDxgiForSse"/>: el setting no es libre acá.</summary>
    Private Function NormalDxgiFromSetting() As Integer
        ' Via OutputSettings ⇒ per-game + All/per-layer (SSE All: Uncompressed; per-layer: Setting_..._SSE).
        Dim os = If(Config_App.Current IsNot Nothing, OutputSettings, Nothing)
        Dim c = If(os IsNot Nothing, os.NormalCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed)
        Return ClampMsnDxgiForSse(c)
    End Function

    ''' <summary>El <c>_msn</c> de cabeza en SSE es model-space: 3 canales INDEPENDIENTES + alpha. BC5 (2
    ''' canales) y BC1 (sin alpha) NO PUEDEN representarlo, así que se acotan a Uncompressed (= vanilla) o BC7.
    ''' BC3 sí se honra: degrada el RGB pero CONSERVA el alpha, y esa es una compensación que el usuario puede
    ''' querer; perder un canal entero no lo es.
    ''' <para>El enum y el setting NO se tocan: la misma combo sirve a FO4, donde BC5 SÍ es correcto porque
    ''' allá el normal es tangent-space de 2 canales. La UI ya no ofrece BC5 en SSE; este clamp es la red para
    ''' un config viejo que lo tenga persistido, y avisa cuando dispara.</para></summary>
    Private Function ClampMsnDxgiForSse(c As FaceTintConvention.FaceTintNormalSpecularCompression) As Integer
        Select Case c
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed
                Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc7
                Return DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc3
                ' Se respeta: pierde RGB pero CONSERVA el alpha (= la máscara de specular del _msn).
                Return DirectXTextureConversionHelper.DxgiFormatBc3Unorm
            Case Else
                ' Bc5 (y cualquier valor futuro de 2 canales): NO puede representar este canal — sin B (sin Z de
                ' la normal) y sin alpha. La UI de SSE ya no lo ofrece; esto cubre un config persistido viejo.
                Dim cL = c
                Logger.LogLazy(Function() $"[FACEBAKE][SSE] _msn: el formato '{cL}' es de 2 canales — no tiene B (Z de la " &
                                          "normal model-space) ni alpha (la máscara de specular). Medido: el _msn de cabeza " &
                                          "vanilla es 24/24 uncompressed 32bpp CON alpha. Se escribe Uncompressed.")
                Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
        End Select
    End Function

    ''' <summary>DXGI de un canal Normal\Specular a partir del enum de compresión. Tabla ÚNICA para los dos canales
    ''' y los dos juegos: los 4 valores del enum se honran (antes el bake FO4 mapeaba sólo Uncompressed-vs-BC5 y
    ''' comía en silencio un BC7/BC3 elegido en CharGen Options).</summary>
    Private Function NsDxgiFromCompression(c As FaceTintConvention.FaceTintNormalSpecularCompression) As Integer
        Select Case c
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc5 : Return DirectXTextureConversionHelper.DxgiFormatBc5Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Uncompressed : Return DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case FaceTintConvention.FaceTintNormalSpecularCompression.Bc7 : Return DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case Else : Return DirectXTextureConversionHelper.DxgiFormatBc3Unorm   ' Bc3
        End Select
    End Function

    ''' <summary>Record one face-texture bake failure on the BuildResult so the save summary surfaces the
    ''' CAUSE (a silent per-slot catch + "bake OK" otherwise hid it — the user only saw "0/1 packed, N files
    ''' unaccounted"). Accumulates the count and keeps the FIRST detail as the representative message.</summary>
    Private Sub RecordTextureFailure(result As BuildResult, detail As String)
        If result Is Nothing Then Return
        result.TextureSlotsFailed += 1
        If String.IsNullOrEmpty(result.TextureFailureDetail) Then result.TextureFailureDetail = detail
    End Sub

    Private Sub BakeFaceTextures(nif As Nifcontent_Class_Manolo,
                                 cloned As INiShape,
                                 srcNif As Nifcontent_Class_Manolo,
                                 srcShape As INiShape,
                                 hdpt As Canon.IHdpt,
                                 effectiveHeadPartType As Integer,
                                 applyMaterialOverrides As ApplyShapeMaterialOverridesDelegate,
                                 npcFormID As UInteger,
                                 originPlugin As String,
                                 pluginManager As PluginManager,
                                 appliedPresets As Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset),
                                 host As NpcRenderHost,
                                 state As MainForm.NPCVisualState,
                                 willBePacked As Boolean,
                                 result As BuildResult,
                                 Optional lmSkinTemplateResolver As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing,
                                 Optional lutDataPath As String = Nothing)
        Logger.LogLazy(Function() $"[FACEBAKE] enter npcFormID=0x{npcFormID:X8} originPlugin='{originPlugin}' srcShape='{srcShape?.Name?.ToString()}'")
        ' El material fuente se resuelve por el MISMO camino que el render, no desde el NIF crudo: en NPCs
        ' con FaceTextureSet ese set pisa el diffuse de la cabeza, y el CK compone el FaceTint sobre la
        ' cabeza RESUELTA. Si el bake partiera de la cruda, el _d se compone sobre la base equivocada.
        ' EL DIFFUSE SE DECLARA AL ENTRAR, antes de cualquier BAIL. Si este metodo corre es porque el NPC
        ' tiene head part de tipo Face, y entonces el _d SIEMPRE correspondia: lo dice el propio loop de
        ' slots ("slot 0 is always expected; its absence is a real failure").
        ' ⛔ Estaba mas abajo, despues de resolver el material, y eso dejaba DOS salidas de FALLA -material
        ' Nothing, y diffusePath vacio- declarando "nada". El packer leia "no correspondia", no exigia
        ' ninguna DDS, el bundle COMMITEABA con el NIF solo y el paso 5 le borraba el suelto: un bake
        ' fallido terminaba con el NIF dentro del BA2 y sin nada que reintentar. Fail-OPEN.
        ' Declarando aca, esos dos caminos son lo que son: falta el _d ⇒ el bundle se descarta entero y los
        ' sueltos se conservan, igual que antes de la ley "requerido = declarado".
        If result IsNot Nothing Then
            result.SalidasDeTexturaDeclaradas = result.SalidasDeTexturaDeclaradas Or FaceGenPaths.SalidaDeTexturaDeCara.Fo4Diffuse
        End If

        Dim mat = ResolveRenderResolvedShapeMaterial(srcNif, srcShape, hdpt, effectiveHeadPartType, state, pluginManager, applyMaterialOverrides)
        If mat Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: resolved source material is Nothing (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "could not resolve the face material (no D/N/S texture paths)")
            Return
        End If

        Dim diffusePath = mat.Diffuse_or_Base_Texture
        Dim normalPath = mat.NormalTexture
        Dim specPath = mat.SmoothSpecTexture
        If String.IsNullOrEmpty(diffusePath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffusePath empty (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "the face material has no diffuse texture path")
            Return
        End If

        ' DECLARACIÓN DE SALIDAS — VA ACÁ, y no más abajo. Lo que el packer le exige a este NPC es lo que el
        ' bake DECIDE acá que le corresponde componer, y la decisión es "el material de la cabeza declara
        ' este canal". El diffuse siempre: el guard de arriba ya garantiza que trae path.
        ' ⛔ NO mover esto al loop de slots. Ahí ya sería un RESULTADO: el guard `bgra Is Nothing` del loop
        ' colapsa TRES cosas distintas —el material no declara el canal, lo declara y el archivo no está en
        ' disco/archives, lo declara y el DDS no decodifica— y sólo la primera es "no correspondía".
        ' Declarando acá, las otras dos siguen siendo exigidas por el packer, que hoy es el ÚNICO que las
        ' detecta: los slots 1/7 no llaman a RecordTextureFailure, así que no hay texWarn ni, en Release,
        ' log. Con la declaración en el loop, una cabeza con el `_msn` roto se horneaba en silencio.
        If result IsNot Nothing Then
            ' El diffuse ya se declaro al entrar. Normal y specular se declaran ACA, porque su predicado es
            ' "el material declara ese canal" y recien ahora se sabe.
            Dim declaradas As FaceGenPaths.SalidaDeTexturaDeCara = FaceGenPaths.SalidaDeTexturaDeCara.Ninguna
            If Not String.IsNullOrEmpty(normalPath) Then declaradas = declaradas Or FaceGenPaths.SalidaDeTexturaDeCara.Fo4Normal
            If Not String.IsNullOrEmpty(specPath) Then declaradas = declaradas Or FaceGenPaths.SalidaDeTexturaDeCara.Fo4Specular
            result.SalidasDeTexturaDeclaradas = result.SalidasDeTexturaDeclaradas Or declaradas
        End If

        ' --- 2. Resolve the NPC's race + gender so we can build layers + region swaps. ---
        ' Forward the LM SkinTemplate resolver so face TXST overrides from the bundle land here
        ' (template.face[gender] → npcData.HeadTextureFormID), keeping the bake's tint inputs
        ' aligned with what the live render shows.
        Dim npcData = NpcRecordOverlay.ResolveOverlaidNpcData(
            npcFormID, pluginManager, appliedPresets, lmSkinTemplateResolver)
        If npcData Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: npcData is Nothing (npcFormID=0x{npcFormID:X8})")
            ' F1: era MUDO. El NIF se escribe igual y las 3 DDS son REQUERIDAS por el packer => el save decia OK
            ' con la cabeza apuntando a texturas inexistentes.
            RecordTextureFailure(result, "could not resolve the NPC for the texture bake (npcData Nothing)")
            Return
        End If

        ' El LUT de la ceja lo resuelve FaceTintInputBuilder desde el RACE (ver
        ' LmHairColorLutLoader.ResolveBrowPaletteTexture). Antes se resolvía acá recorriendo la malla de pelo
        ' del NPC, y esa variante existía SÓLO para esquivar la del render, que recorría las mallas EN
        ' PANTALLA y en un batch le daba a todo el lote el LUT del NPC seleccionado. Con el origen en el
        ' RACE el peligro desaparece de raíz: no hay malla que recorrer ni host del que leer, y GUI, batch
        ' y CLI comparten literalmente el mismo código.
        Dim built = FaceTintLayerBuilder.Build(
            modelFormID:=npcFormID,
            rootFormID:=npcFormID,
            raceFormID:=npcData.Record.Race,
            isFemale:=npcData.Record.ConfigurationFlagsFemale,
            pluginManager:=pluginManager,
            appliedPresets:=appliedPresets,
            tintBytesCache:=Nothing,
            hairColorFormID:=state.HairColorFormID,
            hasTextureLighting:=state.HasTextureLighting,
            textureLightingColorArgb:=state.TextureLightingColor.ToArgb(),
            dataPath:=lutDataPath)

        ' Los env vars de diagnostico `FGBAKE_LAYER_CUTOFF` / `FGBAKE_SWAP_CUTOFF` (truncaban capas y swaps
        ' en LOS DOS compositores para aislar la fase que diverge) NO viven en el bake: truncar el compose
        ' es un foot-gun. La receta exacta para re-armarlos, con todos los numeros, esta en memoria:
        ' 40-bake-estado-cerrado.

        ' --- 3. Upload face source D/N/S to GL temporaries (these are the inputs to the pipeline). ---
        Dim diffuseKey = FO4UnifiedMaterial_Class.CorrectTexturePath(diffusePath)
        Dim normalKey = FO4UnifiedMaterial_Class.CorrectTexturePath(normalPath)
        Dim specKey = FO4UnifiedMaterial_Class.CorrectTexturePath(specPath)

        Dim diffuseBytes = TryGetFilesDictionaryBytes(diffuseKey)
        Dim normalBytesArr = TryGetFilesDictionaryBytes(normalKey)
        Dim specBytesArr = TryGetFilesDictionaryBytes(specKey)
        If diffuseBytes Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse bytes not resolved key='{diffuseKey}' (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, $"face diffuse texture not found on disk / in archives: '{diffuseKey}'")
            Return
        End If

        ' FLAGS INDEPENDIENTES:
        '  - CPU = output principal, SIEMPRE (el `cpu` de abajo). Formato + NOMBRE por DebugMode
        '    (release: canonico _d.dds + BCn; debug: _d_2.dds + uncompressed B8G8R8A8). No depende del GL
        '    -> el bake puede correr async (Await Task.Run en el caller).
        '  - WriteGPUSandboxOutput = corre el GL y escribe el _2b (MISMO formato que el CPU, NOMBRE siempre
        '    _2b). INDEPENDIENTE de DebugMode -> needGl = este flag. Como toca GL, el bake DEBE ir sync en el
        '    hilo UI (contexto GL): el caller (MainForm) lo agenda sync cuando WriteGPUSandboxOutput.
        '  - WriteTGASandboxOutput = ademas un TGA UNCOMPRESSED al lado de cada .dds (CPU y, si corrio, GPU),
        '    desde el buffer en memoria (lossless aunque el .dds sea BCn). INDEPENDIENTE de DebugMode (release tambien).
        Dim needGl As Boolean = WriteGPUSandboxOutput
        Dim cpu As FaceTintCpuCompositor.CpuPipelineResult = Nothing

        Try
            cpu = FaceTintCpuCompositor.ComposeCpuPipeline(diffuseBytes, normalBytesArr, specBytesArr, built.Layers, built.RegionSwaps, OutputSettings, diffuseKey, normalKey, specKey,
                                                           headDiffuseAlphaTest:=(npcData.Game = Config_App.Game_Enum.Fallout4) AndAlso (npcData.Record.ConfigurationFlags And &H1000000UI) <> 0UI)
        Catch ex As Exception
            ' F1: la EXCEPCION se reporta aca con tipo y mensaje. Antes solo iba al log y el fallo se INFERIA rio
            ' abajo ("no hubo diffuse"), inferencia que ademas solo corria en la rama CPU-only: en DebugMode
            ' (needGl=True) quedaba completamente mudo.
            Dim tC = ex.GetType().Name, mC = ex.Message
            Logger.LogLazy(Function() $"[FACEBAKE-CPU] CPU compose failed: {tC}: {mC}")
            RecordTextureFailure(result, $"the CPU compositor threw: {tC}: {mC}")
        End Try

        If (Not needGl) AndAlso (cpu Is Nothing OrElse cpu.Diffuse Is Nothing OrElse cpu.Diffuse.Bgra Is Nothing) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: CPU compose produced no diffuse (npcFormID=0x{npcFormID:X8})")
            RecordTextureFailure(result, "the CPU compositor produced no diffuse pixels (see [FACEBAKE-CPU] log for the cause)")
            Return
        End If

        Dim tempIds As New List(Of Integer)
        Dim diffEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim normEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim specEntry As PreviewModel.Texture_Loaded_Class = Nothing
        Dim w As Integer, h As Integer
        If needGl Then
            ' --- GL path (DebugMode): upload source D/N/S a GL para correr el GPU pipeline (escribe _2). ---
            Dim uploadPaths As New List(Of String)
            Dim uploadBytes As New List(Of Byte())
            uploadPaths.Add(diffuseKey) : uploadBytes.Add(diffuseBytes)
            If normalBytesArr IsNot Nothing Then
                uploadPaths.Add(normalKey) : uploadBytes.Add(normalBytesArr)
            End If
            If specBytesArr IsNot Nothing Then
                uploadPaths.Add(specKey) : uploadBytes.Add(specBytesArr)
            End If

            Dim uploaded As Dictionary(Of String, PreviewModel.Texture_Loaded_Class) = Nothing
            Try
                ' srgb=False para TODAS: la base del bake se carga CRUDA (el seed hace srgbToLin, base raw =
                ' baseDiffuseIsLinearOnGpu=False); el decode lo hace el compositor por convención, no el SRV.
                ' useCompress SALE DE LA MISMA PROPIEDAD QUE LOS OTROS SITIOS DE CARGA, no de un True fijo.
                ' Este es el CUARTO sitio que sube texturas a GL (los otros tres viven en FaceTintCompositor) y
                ' era el unico que no consultaba la propiedad. Con True fijo el seed del acumulador GPU quedaba
                ' descomprimido POR HARDWARE mientras el CPU decodifica el MISMO DDS por software (DirectXTex):
                ' dos decoders distintos sobre los mismos bytes = paths CRUZADOS, justo lo que la paridad mide.
                ' MEDIDO (muestra de 60 NPCs, comparando SOLO el seed, sin swaps ni capas): 5.265 px con
                ' |delta|>=3 y peor 7, repartidos _msn=4.323 _s=912 _d=30 — y el mismo conteo exacto (214 px)
                ' repetido en NPCs distintos que comparten el `_msn`, o sea una propiedad de la TEXTURA y no
                ' del NPC, que es la firma de una diferencia de decode y no de aritmetica.
                uploaded = DirectXDDSLoader.Load_And_GenerateOpenGLTextures_Memory(
                    uploadPaths.ToArray(), uploadBytes.ToArray(),
                    useCompress:=FaceTintCompositor.GlDecodeUseCompress, forceOpenGL:=False, Srgb:=New Boolean(uploadPaths.Count - 1) {})
            Catch ex As Exception
                Dim tU = ex.GetType().Name, mU = ex.Message
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: GL upload threw {tU}: {mU} (npcFormID=0x{npcFormID:X8})")
                RecordTextureFailure(result, $"uploading the source textures to GL threw: {tU}: {mU}")
                Return
            End Try

            uploaded.TryGetValue(diffuseKey, diffEntry)
            uploaded.TryGetValue(normalKey, normEntry)
            uploaded.TryGetValue(specKey, specEntry)
            If diffEntry IsNot Nothing AndAlso diffEntry.Texture_ID <> 0 Then tempIds.Add(diffEntry.Texture_ID)
            If normEntry IsNot Nothing AndAlso normEntry.Texture_ID <> 0 Then tempIds.Add(normEntry.Texture_ID)
            If specEntry IsNot Nothing AndAlso specEntry.Texture_ID <> 0 Then tempIds.Add(specEntry.Texture_ID)

            If diffEntry Is Nothing OrElse diffEntry.Texture_ID = 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse GL texture id 0 (npcFormID=0x{npcFormID:X8})")
                RecordTextureFailure(result, "the source diffuse texture could not be uploaded to GL (id 0)")
                DeleteGlTextures(tempIds)
                Return
            End If

            w = diffEntry.Size.Width
            h = diffEntry.Size.Height
            If w <= 0 OrElse h <= 0 Then
                Dim wB = w, hB = h
                Logger.LogLazy(Function() $"[FACEBAKE] BAIL: diffuse size {wB}x{hB} (npcFormID=0x{npcFormID:X8})")
                RecordTextureFailure(result, $"the source diffuse texture has an invalid size ({wB}x{hB})")
                DeleteGlTextures(tempIds)
                Return
            End If
        Else
            ' --- CPU-only (release): sin GL. El tamaño sale del resultado CPU. ---
            w = cpu.Diffuse.Width
            h = cpu.Diffuse.Height
        End If

        ' --- 4. GL pipeline (SOLO needGl = DebugMode): region-swap + tint compose en GPU para escribir
        ' el _2 de comparación (vs el _2b del CPU). En RELEASE-CPU NO corre -> no se duplica GPU+CPU y el
        ' bake no toca GL (async). El CPU ya se compuso arriba (cpu). ---
        Dim pipelineResult As FaceTintCompositor.FaceTintPipelineResult = Nothing
        If needGl Then
            pipelineResult = FaceTintCompositor.ApplyFaceTintPipeline(
                host.CompositorState, host.TintGpuCache,
                diffEntry.Texture_ID,
                If(normEntry?.Texture_ID, 0),
                If(specEntry?.Texture_ID, 0),
                w, h,
                built.Layers, built.RegionSwaps,
                FaceTintCpuCompositor.AccumSpaceCapability,
                OutputSettings)
        End If

        ' Track any fresh textures the pipeline produced so we can delete them on exit. (Nothing en
        ' release-CPU: no hubo GL pipeline.)
        Dim freshIds As New List(Of Integer)
        If pipelineResult IsNot Nothing Then
            If pipelineResult.Diffuse.IsFresh Then freshIds.Add(pipelineResult.Diffuse.TextureId)
            If pipelineResult.Normal.IsFresh Then freshIds.Add(pipelineResult.Normal.TextureId)
            If pipelineResult.Specular.IsFresh Then freshIds.Add(pipelineResult.Specular.TextureId)
            ' El pase final AccumSpace->OutputSpace del GL fallo en algun canal ⇒ ese canal quedo en
            ' AccumSpace y su gamma esta corrida. Se INVALIDA la medicion de paridad de este NPC en vez de
            ' dejar que la divergencia se lea como defecto del compositor. (Es el consumidor de
            ' FaceTintPipelineResult.SpaceConversionFailed: sin esto el flag existiria y no lo miraria nadie.)
            If pipelineResult.SpaceConversionFailed Then
                ParityInvalidate($"0x{npcFormID:X8}: the GL's final AccumSpace->OutputSpace pass failed")
            End If
        End If

        ' --- 5. Output dir + slot plan + texture-set for slot rewrites. ---
        Dim formIdLow = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Dim dataPath = BakeOutputRoot.Current()
        If String.IsNullOrEmpty(dataPath) Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: Config_App.Current.DataPath empty (npcFormID=0x{npcFormID:X8})")
            ' (Este caso ademas aborta BuildCharGen mas abajo con Success=False, asi que NO llega a escribirse un
            '  NIF huerfano. Se reporta igual para que la causa aparezca en el detalle del save.)
            RecordTextureFailure(result, "DataPath not configured: there is nowhere to write the textures")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If
        Dim outDir = Path.Combine(dataPath, FaceGenPaths.CustomizacionDir(originPlugin))
        Try : Directory.CreateDirectory(outDir) : Catch : End Try

        ' Los sufijos salen de FaceGenPaths.SufijoDe, igual que los del packer: el `_2` del sandbox lo
        ' decide UN solo lugar. Estaban escritos acá a mano y otra vez allá, y tenían que coincidir.
        ' Formato por canal = SETTINGS (decisión usuario: independiente de DebugMode; DebugMode solo decide
        ' el NOMBRE _2 y si corre el GL). Diffuse: BC3 (default) / BC7 / Uncompressed. N/S: BC5 (default) /
        ' Uncompressed. Uncompressed = B8G8R8A8 (true-color, sin pérdida). Para inspección lossless sin tocar
        ' el formato del .dds está el tilde Generate TGA (WriteTGASandboxOutput).
        Dim os = OutputSettings
        Dim dxgiD As Integer
        Select Case If(os IsNot Nothing, os.DiffuseCompression, FaceTintConvention.FaceTintDiffuseCompression.Bc3)
            Case FaceTintConvention.FaceTintDiffuseCompression.Bc7 : dxgiD = DirectXTextureConversionHelper.DxgiFormatBc7Unorm
            Case FaceTintConvention.FaceTintDiffuseCompression.Uncompressed : dxgiD = DirectXTextureConversionHelper.DxgiFormatB8G8R8A8Unorm
            Case Else : dxgiD = DirectXTextureConversionHelper.DxgiFormatBc3Unorm
        End Select
        ' N/S: los 4 formatos del enum (BC5 default / Uncompressed / BC7 / BC3), no sólo Uncompressed-vs-BC5.
        Dim dxgiN As Integer = NsDxgiFromCompression(If(os IsNot Nothing, os.NormalCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Bc5))
        Dim dxgiS As Integer = NsDxgiFromCompression(If(os IsNot Nothing, os.SpecularCompression, FaceTintConvention.FaceTintNormalSpecularCompression.Bc5))
        ' En disco los DDS usan siempre Suffix (con _2 en DebugMode); el sufijo que se EMBEBE en el NIF
        ' depende del consumidor: con willBePacked el packer renombra a canónico al empaquetar, así que se
        ' embebe el canónico; sin él no renombra nadie y hay que embeber el nombre real de disco, o el NIF
        ' suelto referenciaría un archivo que no existe. En release ambos coinciden.
        ' W/H por canal = tamaño del RESULTADO del pipeline (no el del source, que puede diferir si el enum
        ' de resolución pidió otro), con fallback al nativo. ResultId = textura GL, 0 en el camino CPU.
        Dim pr = pipelineResult
        ' EL PLAN DE SLOTS SE RECORRE DE LA TABLA, no se escribe acá. El conjunto {slot 0/1/7,
        ' "_d"/"_msn"/"_s"} estaba escrito DOS veces —acá y en NpcFaceGenPacker.FaceGenFileSpecs— y tenían
        ' que coincidir para que el packer encontrara lo que este bake escribe. Ahora los dos recorren
        ' FaceGenPaths.SalidasFo4: un canal nuevo es UNA FILA.
        ' El cableado por canal (qué DXGI y de qué canal del pipeline sale) SÍ es propio del bake y vive
        ' acá; el Case Else TIRA a propósito, para que una fila nueva en la tabla sin su cableado falle
        ' RUIDOSO en vez de desaparecer del plan en silencio.
        Dim slotPlan As New List(Of (Slot As Integer, ResultId As Integer, Dxgi As Integer, Suffix As String, CanonSuffix As String, W As Integer, H As Integer, Salida As FaceGenPaths.SalidaDeTexturaDeCara))
        For Each salidaFo4 In FaceGenPaths.SalidasFo4
            Dim suf = FaceGenPaths.SufijoDe(salidaFo4, DebugMode)
            Select Case salidaFo4.Salida
                Case FaceGenPaths.SalidaDeTexturaDeCara.Fo4Diffuse
                    slotPlan.Add((salidaFo4.Slot, If(pr IsNot Nothing, pr.Diffuse.TextureId, 0), dxgiD, suf, salidaFo4.SufijoCanon,
                                  SlotDim(pr?.Diffuse, cpu?.Diffuse, w, True), SlotDim(pr?.Diffuse, cpu?.Diffuse, h, False), salidaFo4.Salida))
                Case FaceGenPaths.SalidaDeTexturaDeCara.Fo4Normal
                    slotPlan.Add((salidaFo4.Slot, If(pr IsNot Nothing, pr.Normal.TextureId, 0), dxgiN, suf, salidaFo4.SufijoCanon,
                                  SlotDim(pr?.Normal, cpu?.Normal, w, True), SlotDim(pr?.Normal, cpu?.Normal, h, False), salidaFo4.Salida))
                Case FaceGenPaths.SalidaDeTexturaDeCara.Fo4Specular
                    slotPlan.Add((salidaFo4.Slot, If(pr IsNot Nothing, pr.Specular.TextureId, 0), dxgiS, suf, salidaFo4.SufijoCanon,
                                  SlotDim(pr?.Specular, cpu?.Specular, w, True), SlotDim(pr?.Specular, cpu?.Specular, h, False), salidaFo4.Salida))
                Case Else
                    ' EN INGLÉS: este texto viaja en ex.Message y el Save lo muestra tal cual en su
                    ' MessageBox ("CharGen bake failed: ..."), o sea que ES UI. Los comentarios y los logs
                    ' de este archivo van en castellano; los strings que ve el usuario, no.
                    Throw New InvalidOperationException(
                        $"FaceGenPaths.SalidasFo4 declares output '{salidaFo4.Salida}' (slot {salidaFo4.Slot}) but " &
                        "BakeFaceTextures has no pipeline channel wired for it. Add its branch here when you add the table row.")
            End Select
        Next

        Dim bsls = TryCast(nif.GetShader(cloned), BSLightingShaderProperty)
        Dim texset As BSShaderTextureSet = Nothing
        If bsls IsNot Nothing AndAlso bsls.TextureSetRef IsNot Nothing AndAlso bsls.TextureSetRef.Index <> -1 Then
            texset = TryCast(nif.Blocks(bsls.TextureSetRef.Index), BSShaderTextureSet)
        End If
        If texset Is Nothing OrElse texset.Textures Is Nothing Then
            Logger.LogLazy(Function() $"[FACEBAKE] BAIL: cloned shape has no BSShaderTextureSet (npcFormID=0x{npcFormID:X8})")
            ' F1: era MUDO y el NIF se escribia igual, con la cabeza apuntando a 3 DDS que nunca se generaron.
            RecordTextureFailure(result, "the cloned face shape has no BSShaderTextureSet: slots 0/1/7 cannot be written")
            DeleteGlTextures(tempIds) : DeleteGlTextures(freshIds)
            Return
        End If

        ' LEY: el redirect de los slots 0/1/7 a FaceCustomization se gatea por el SHADER TYPE del MATERIAL
        ' DEL SHAPE (Face = 4), NO por HDPT.PartType del record. Ver 30-fo4-gate-composite-por-shadertype.md.
        ' El gate va ACÁ (en el redirect de slots) y NO en el call site: el CK COMPONE y EXPORTA las
        ' texturas igual —están shippeadas en el BA2— y sólo se saltea la ASIGNACIÓN al NIF. Apagar el bake
        ' entero desde el call site ya rompió el NIF una vez (shape sin texture set propio ⇒ se deduplicaba
        ' con otra).
        ' El tipo se lee del shader del shape CLONADO, que ya lo derivó de los bools del material resuelto;
        ' no se re-deriva acá para no duplicar la regla.
        Dim shapeShaderType = bsls.ShaderType_SK_FO4
        Dim redirectSlotsToFaceCustomization As Boolean =
            (shapeShaderType = NiflySharp.Enums.BSLightingShaderType.FaceTint)
        If Not redirectSlotsToFaceCustomization Then
            ' F2 — EL PREDICADO NO SE TOCA (replica cmp eax,4 del CK 0x140ed9020 sobre el tipo DERIVADO de los
            ' bools del material; validado sobre 14.136 shapes). Lo que cambia es que deja de ser INVISIBLE.
            ' Y NO ES UN FALLO: que la DDS se componga, se escriba y se empaquete SIN que el NIF la referencie es
            ' EXACTAMENTE lo que hace el CK — medido en el bloque de la ley de arriba: el CK shippeo 0001763B_d.DDS
            ' en el BA2 para DLC04Oswald sin una sola referencia en su NIF (export SIN gate 0x140ab8760 vs asignacion
            ' CON gate 0x140ed9020). Por eso NO se llama a RecordTextureFailure: seria un falso positivo que marcaria
            ' el save como Warning. Solo se REGISTRA, y sin el gate de Logger.Enabled (es un evento raro, una vez por
            ' NPC, no un log por shape).
            Dim stG = shapeShaderType
            Logger.LogLazy(Function() $"[FACEBAKE] slots 0/1/7 NO redirigidos (= comportamiento del CK): shape shType={stG} (≠FaceTint), ley CK 0x140ed9020. La DDS se compone y empaqueta igual, sin referencia en el NIF (npcFormID=0x{npcFormID:X8})")
        End If

        ' --- 6. Per-slot: readback → encode → write → rewrite slot path → diff vs CK. ---
        For Each entry In slotPlan
            Dim ddW As Integer = entry.W, ddH As Integer = entry.H
            Dim cbSlot As Byte() = CpuBgraForSlot(cpu, entry.Slot)   ' CPU bgra del canal (Nothing si no hay)

            ' GPU readback SOLO needGl (DebugMode) + textura válida. En release-CPU no hay textura -> sin GL.
            Dim gpuBgra As Byte() = Nothing
            If needGl AndAlso entry.ResultId <> 0 Then
                Dim gbuf(ddW * ddH * 4 - 1) As Byte
                Try
                    GL.BindTexture(TextureTarget.Texture2D, entry.ResultId)
                    Dim handle = Runtime.InteropServices.GCHandle.Alloc(gbuf, Runtime.InteropServices.GCHandleType.Pinned)
                    Try
                        GL.GetTexImage(TextureTarget.Texture2D, 0, OpenTK.Graphics.OpenGL4.PixelFormat.Bgra, PixelType.UnsignedByte, handle.AddrOfPinnedObject())
                    Finally
                        handle.Free()
                    End Try
                    gpuBgra = gbuf
                Catch ex As Exception
                    Dim slotL = entry.Slot
                    Dim suffixL = entry.Suffix
                    Dim resultIdL = entry.ResultId
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[FACEBAKE-FAIL] GL.GetTexImage slot={slotL}{suffixL} ResultId={resultIdL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                    gpuBgra = Nothing
                End Try
            End If

            ' Instrumento de paridad CPU-vs-GPU: es el único punto donde los dos compositores tienen el mismo
            ' canal, del mismo NPC, en el mismo formato y tamaño, y se compara ANTES del encode BCn (así el
            ' número no lleva codec adentro). Existe porque el bake corre 100 % CPU, así que sin esto el
            ' compositor GL —el del render— no se ejercita en un barrido. Se alimenta con FGBAKE_GPU_PARITY=1.
            If cbSlot IsNot Nothing AndAlso gpuBgra IsNot Nothing Then
                RecordCpuGpuParity(entry.Slot, entry.Suffix, cbSlot, gpuBgra, ddW, ddH, npcFormID, If(built.RegionSwaps IsNot Nothing, built.RegionSwaps.Count, 0), If(built.Layers IsNot Nothing, built.Layers.Count, 0))
            End If

            ' OUTPUT principal (_d.dds release / _d_2.dds debug): SIEMPRE CPU (el path always-on, byte-exacto a
            ' build_3). El GPU es contingente (solo DumpIntermediates) y va al _2b de comparacion. Fallback a GPU
            ' solo si por algun motivo no hay CPU; si tampoco hay GPU -> skip el slot.
            Dim bgra As Byte() = If(cbSlot, gpuBgra)
            If bgra Is Nothing Then
                Logger.LogLazy(Function() $"[FACEBAKE] slot {entry.Slot}{entry.Suffix}: sin textura (ni CPU ni GPU) — SKIPPED (npcFormID=0x{npcFormID:X8})")
                ' ⛔ SE REPORTA TODO CANAL QUE EL MATERIAL DECLARÓ Y NO SALIÓ, no sólo el slot 0.
                ' Acá decía «los slots 1/7 faltan legítimamente cuando la cabeza no los trae, no los marques
                ' como fallo». Eso era cierto ANTES de que la declaración de salidas subiera a :3634-3641:
                ' hoy un slot 1/7 sólo llega DECLARADO si el material NOMBRÓ ese canal, así que "declarado y
                ' sin píxeles" es un `_msn`/`_s` que el material nombra y no está ni en disco ni en los
                ' archives. Y eso NO es inocuo: el packer lo exige, no lo encuentra, y descarta el bundle
                ' ENTERO del NPC —el NIF de FaceGeom incluido—. `result.FailedBundles` dice CUÁL bundle se
                ' cayó; nadie decía POR QUÉ, porque este guard era el único que lo sabía y se quedaba mudo.
                ' El propio comentario de la declaración ya nombraba el hueco («los slots 1/7 no llaman a
                ' RecordTextureFailure, así que no hay texWarn ni, en Release, log»). Esto lo cierra.
                '
                ' ⛔ NO CONTRADICE al guard de `If Not redirectSlotsToFaceCustomization` (:3917), que a
                ' propósito NO reporta: allá el canal se compone bien y sólo no se ASIGNA al NIF —que es lo
                ' que hace el CK—, así que marcar Warning sería un falso positivo. La diferencia es
                ' DECLARADO vs NUNCA-DECLARADO, y es toda la diferencia: allá no hay nada que falte; acá el
                ' material prometió un canal que no existe.
                '
                ' La identidad del slot sale de la TABLA (`FaceGenPaths.SalidasFo4`), no de una lista de
                ' slots escrita acá — misma razón que el barrido de stale. El mapa identidad→path repite el
                ' de la declaración (:3638-3639) porque `mat` tiene tres campos con nombre propio y no hay
                ' otra forma de llegar al path desde la identidad.
                Dim salidaDelSlot = FaceGenPaths.SalidasFo4.FirstOrDefault(Function(s) s.Slot = entry.Slot)
                Dim fueDeclarada = result IsNot Nothing AndAlso
                                   (result.SalidasDeTexturaDeclaradas And salidaDelSlot.Salida) <>
                                   FaceGenPaths.SalidaDeTexturaDeCara.Ninguna
                If fueDeclarada Then
                    Dim rutaDeclarada =
                        If(salidaDelSlot.Salida = FaceGenPaths.SalidaDeTexturaDeCara.Fo4Normal, normalPath,
                        If(salidaDelSlot.Salida = FaceGenPaths.SalidaDeTexturaDeCara.Fo4Specular, specPath,
                           diffusePath))
                    RecordTextureFailure(result,
                        $"slot {entry.Slot}{entry.Suffix} ({salidaDelSlot.SufijoCanon}): the head material " &
                        $"declares '{rutaDeclarada}' but no pixels were composed (neither CPU nor GPU). " &
                        "The packer requires this output, so this NPC's whole FaceGen bundle is discarded — " &
                        "FaceGeom NIF included. Check that the texture exists on disk or in the archives.")
                End If
                Continue For
            End If

            ' El diffuse NO se re-encodea acá: el compositor ya convierte el source sRGB->g22 UNA vez, en
            ' float y antes de componer, así que el bgra que se lee YA está en g22 (que es como el motor
            ' almacena la FaceCustomization). N/S son datos lineales y van raw.

            Dim mipLevels = CInt(Math.Floor(Math.Log(Math.Min(ddW, ddH), 2))) + 1
            Dim ddsBytes As Byte() = Nothing
            ' GATE del encode+escritura del DDS (ver SkipDdsEncode). Se saltea el BCn+mips y el File.Write, y se
            ' cae DIRECTO a la reescritura del slot de abajo — igual que si el encode hubiera salido bien.
            If Not SkipDdsEncode Then
                Try
                    ddsBytes = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                    width:=ddW, height:=ddH, bgraPixels:=bgra,
                    outputDxgiFormat:=entry.Dxgi,
                    generateMipMaps:=True, generatedMipLevels:=mipLevels)
                Catch ex As Exception
                    Dim slotL = entry.Slot
                    Dim suffixL = entry.Suffix
                    Dim dxgiL = entry.Dxgi
                    ' Report the dims actually passed to the encode (ddW/ddH), not the source dims (w/h).
                    Dim wL = ddW
                    Dim hL = ddH
                    Dim mipsL = mipLevels
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[FACEBAKE-FAIL] DDS encode slot={slotL}{suffixL} dxgi={dxgiL} {wL}x{hL} mips={mipsL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                    RecordTextureFailure(result, $"{typeL}: {msgL} (encode slot {slotL}{suffixL}, {wL}x{hL}, dxgi={dxgiL})")
                    Continue For
                End Try

                Dim outFile = Path.Combine(outDir, $"{formIdLow:X8}{entry.Suffix}")
                Try
                    File.WriteAllBytes(outFile, ddsBytes)
                    ' Se anota ACÁ, donde se escribe: es lo que el barrido de restos usa para saber qué NO
                    ' borrar. Ver DeleteStaleFaceCustomizationArtifacts.
                    result?.TexturasEscritas.Add(outFile)
                    ' Y la IDENTIDAD de la salida, que es lo que consume el packer para decidir si este
                    ' archivo es de ESTE horneado. A la RUTA no se le puede preguntar: el bake escribe bajo
                    ' BakeOutputRoot.Current() -que --outdir MUEVE- y el packer arma la suya con el dataDir
                    ' que le pasa el Save. Comparar los dos strings da falso justo cuando las raices
                    ' difieren, y como la salida esta DECLARADA eso significa "falta": se caian TODOS los
                    ' bundles de FO4, en cada Save.
                    If result IsNot Nothing Then
                        result.SalidasDeTexturaEscritas = result.SalidasDeTexturaEscritas Or entry.Salida
                    End If
                    Logger.LogLazy(Function() $"[FACEBAKE] wrote '{outFile}'")
                Catch ex As Exception
                    Dim slotW = entry.Slot
                    Dim suffixW = entry.Suffix
                    Dim msgW = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE] write FAILED '{outFile}': {msgW}")
                    RecordTextureFailure(result, $"could not write the DDS to disk (slot {slotW}{suffixW}): {msgW}")
                    Continue For
                End Try
            End If

            ' TGA del CPU: copia UNCOMPRESSED (true-color) al lado del .dds, desde el buffer en memoria
            ' (bgra) -> lossless aunque el .dds sea BCn. Gateado SOLO por WriteTGASandboxOutput
            ' (independiente de DebugMode -> tambien en release). Nombre = el del CPU: {id}_d.tga en
            ' release, {id}_d_2.tga en debug (sigue a entry.Suffix). SOLO si el output queda loose (no se
            ' empaqueta a BA2): el .tga no entra al BA2 y quedaría huérfano. Ver OutputStaysLoose.
            If WriteTGASandboxOutput AndAlso OutputStaysLoose(willBePacked) Then
                Try
                    Dim tgaSuffix = Path.ChangeExtension(entry.Suffix, "tga")
                    Dim outTga = Path.Combine(outDir, $"{formIdLow:X8}{tgaSuffix}")
                    FaceTintCompositor.WriteBgraToTga(outTga, bgra, ddW, ddH)
                    Logger.LogLazy(Function() $"[FACEBAKE] wrote '{outTga}'")
                Catch ex As Exception
                    Dim slotL = entry.Slot
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[FACEBAKE-FAIL] TGA dump slot={slotL} npcFormID=0x{npcFormID:X8}: {typeL}: {msgL}")
                End Try
            End If

            ' Output GPU (_2b): SOLO si corrio el GL (gpuBgra <> Nothing = WriteGPUSandboxOutput,
            ' independiente de DebugMode). .dds con el MISMO formato que el CPU (entry.Dxgi: BCn en release,
            ' B8G8R8A8 en debug) y NOMBRE SIEMPRE _2b ({id}_d_2b.dds, armado desde CanonSuffix para no
            ' depender del _2 del Suffix). Su TGA (uncompressed, desde gpuBgra) si WriteTGASandboxOutput.
            ' Sirve para diff directo CPU vs GPU al mismo formato. SOLO si el output queda loose (mismo
            ' motivo que el TGA: el packer no mete el _2b -> quedaría huérfano en un BA2 save).
            If gpuBgra IsNot Nothing AndAlso OutputStaysLoose(willBePacked) Then
                Dim slotL2 = entry.Slot
                Try
                    Dim suffix2b = entry.CanonSuffix.Replace(".dds", "_2b.dds")
                    Dim mips2b = CInt(Math.Floor(Math.Log(Math.Min(ddW, ddH), 2))) + 1
                    Dim dds2b = DirectXTextureConversionHelper.Bgra32BytesToDdsBytes(
                        width:=ddW, height:=ddH, bgraPixels:=gpuBgra,
                        outputDxgiFormat:=entry.Dxgi,
                        generateMipMaps:=True, generatedMipLevels:=mips2b)
                    File.WriteAllBytes(Path.Combine(outDir, $"{formIdLow:X8}{suffix2b}"), dds2b)
                    Logger.LogLazy(Function() $"[FACEBAKE-GPU] wrote '{formIdLow:X8}{suffix2b}' slot={slotL2}")
                    If WriteTGASandboxOutput Then
                        FaceTintCompositor.WriteBgraToTga(Path.Combine(outDir, $"{formIdLow:X8}{Path.ChangeExtension(suffix2b, "tga")}"), gpuBgra, ddW, ddH)
                    End If
                Catch ex As Exception
                    Dim m = ex.Message
                    Logger.LogLazy(Function() $"[FACEBAKE-GPU] _2b write failed slot={slotL2}: {m}")
                End Try
            End If

            ' Gate por shader-type del material (ver bloque de la ley arriba, RE CK 0x140ed9020): si el
            ' shape no es Face/FaceTint el CK NO asigna NINGÚN slot ⇒ el shape conserva las texturas ya
            ' transcriptas por ApplyRenderResolvedMaterialToShape. El DDS de arriba SÍ se compuso y
            ' escribió, igual que el CK (que shippeó 0001763B_d.DDS para Oswald sin referenciarlo).
            If Not redirectSlotsToFaceCustomization Then Continue For

            Dim embeddedSuffix = If(willBePacked, entry.CanonSuffix, entry.Suffix)
            ' Full "Data\Textures\..." prefix, matching CK vanilla exactly (CK's loose FaceGen renders
            ' fine with this prefix — verified — so the prefix is NOT the loose-breaker).
            ' La carpeta sale de FaceGenPaths, igual que en el packer y en FaceTextureRepointer -que
            ' escribe ESTE MISMO slot para ESTE MISMO archivo en el export a NIF-. Estaba como literal
            ' aca: los dos escritores del slot con la ruta escrita de dos formas distintas.
            ' El prefijo "Data\" es de esta punta (asi lo escribe el CK) y no vive en el helper.
            Dim canonicalNifPath = "Data\" & FaceGenPaths.CustomizacionDir(originPlugin) &
                                  $"{formIdLow:X8}{embeddedSuffix}"
            While texset.Textures.Count <= entry.Slot
                texset.Textures.Add(New NiflySharp.NiString4 With {.Content = ""})
            End While
            If texset.Textures(entry.Slot) Is Nothing Then
                texset.Textures(entry.Slot) = New NiflySharp.NiString4 With {.Content = canonicalNifPath}
            Else
                texset.Textures(entry.Slot).Content = canonicalNifPath
            End If


        Next

        ' --- 7. Cleanup. Delete the source temporaries we uploaded AND any fresh outputs the
        ' pipeline generated. The pipeline already deleted any intermediate fresh IDs; here we
        ' only own the explicit "kept" outputs (Diffuse/Normal/Specular .TextureId where
        ' IsFresh=True) and the uploaded source IDs. Source IDs that were also returned
        ' verbatim as outputs (IsFresh=False) get deleted via tempIds — they are NOT in
        ' freshIds.
        DeleteGlTextures(tempIds)
        DeleteGlTextures(freshIds)
    End Sub

    ''' <summary>Read raw DDS bytes from FilesDictionary; returns Nothing on miss / empty / IO error.</summary>
    Private Function TryGetFilesDictionaryBytes(normalizedKey As String) As Byte()
        If String.IsNullOrEmpty(normalizedKey) Then Return Nothing
        Try
            Dim bytes = FilesDictionary_class.GetBytes(normalizedKey)
            If bytes Is Nothing OrElse bytes.Length = 0 Then
                Logger.LogLazy(Function() $"[FACEBAKE-FAIL] FilesDictionary.GetBytes returned empty for key='{normalizedKey}'")
                Return Nothing
            End If
            Return bytes
        Catch ex As Exception
            Dim keyL = normalizedKey
            Dim msgL = ex.Message
            Dim typeL = ex.GetType().Name
            Logger.LogLazy(Function() $"[FACEBAKE-FAIL] FilesDictionary.GetBytes threw for key='{keyL}': {typeL}: {msgL}")
            Return Nothing
        End Try
    End Function

    Private Sub DeleteGlTextures(ids As List(Of Integer))
        If ids Is Nothing Then Return
        For Each id In ids
            If id = 0 Then Continue For
            Try : GL.DeleteTexture(id) : Catch : End Try
        Next
        ids.Clear()
    End Sub

    ''' <summary>Tamaño (W si isWidth, sino H) del slot: del canal del pipeline GL si tiene (>0), sino del
    ''' canal CPU, sino el fallback (nativo). Sirve para el readback/encode en GL y CPU por igual.</summary>
    Private Function SlotDim(pl As FaceTintCompositor.FaceTintPipelineChannelResult, cpuCh As FaceTintCpuCompositor.CpuChannelResult, fallback As Integer, isWidth As Boolean) As Integer
        If pl IsNot Nothing Then
            Dim v = If(isWidth, pl.Width, pl.Height)
            If v > 0 Then Return v
        End If
        If cpuCh IsNot Nothing Then
            Dim v = If(isWidth, cpuCh.Width, cpuCh.Height)
            If v > 0 Then Return v
        End If
        Return fallback
    End Function

    ''' <summary>BGRA del canal del resultado CPU para un slot del bake (0=Diffuse, 1=Normal, 7=Specular).
    ''' Nothing si el resultado o el canal son Nothing.</summary>
    Private Function CpuBgraForSlot(cpu As FaceTintCpuCompositor.CpuPipelineResult, slot As Integer) As Byte()
        If cpu Is Nothing Then Return Nothing
        Dim ch As FaceTintCpuCompositor.CpuChannelResult
        Select Case slot
            Case 0 : ch = cpu.Diffuse
            Case 1 : ch = cpu.Normal
            Case 7 : ch = cpu.Specular
            Case Else
                ' ERA FAIL-OPEN: cualquier slot desconocido caia en el diffuse, asi que una fila nueva en
                ' FaceGenPaths.SalidasFo4 sin su cableado se horneaba CON LOS PIXELES DEL DIFFUSE, se
                ' escribia, se declaraba y se empaquetaba. Silencioso, y adentro del BA2 que se publica.
                Throw New ArgumentOutOfRangeException(
                    NameOf(slot), slot,
                    "No CPU channel is wired for this FaceCustomization slot. Add its branch here when you add the row to FaceGenPaths.SalidasFo4.")
        End Select
        Return ch?.Bgra
    End Function

End Module
