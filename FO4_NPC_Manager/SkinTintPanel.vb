Imports System.Diagnostics
Imports FO4_Base_Library

''' <summary>Lógica del tab "Skin Tint Adjustment" del editor de cuerpo (los controles viven en
''' SkinTintPanel.Designer.vb): ajusta el SKIN TONE del cuerpo (QNAM) con cuatro offsets — R/G/B y una
''' intensidad — para el caso en que el cuerpo y la cara vienen de mods distintos y el tono no matchea con
''' las reglas del motor.
'''
''' <para><b>Qué es cada cosa.</b> El ORIGEN es un píxel de la CARA: se le latchea el COLOR, y ese color no
''' se mueve nunca — el ajuste no toca la resolución que consume la cara (ver
''' <see cref="NpcMaterialResolver.ResolveNpcBodySkinToneColor"/>). El DESTINO es un píxel del CUERPO: se le
''' latchea la POSICIÓN, porque su color es justamente lo que el ajuste mueve y hay que re-muestrearlo en
''' cada iteración.</para>
'''
''' <para><b>Los dos píxeles son WYSIWYG</b> — el color final tal como se ve, con luz, sombra, tonemap y el
''' encode a display ya aplicados (Shader_Class: <c>tonemap()</c> + <c>pow(1/2.2)</c>). Por eso el auto-calc
''' es un LAZO CERRADO —aplicar, re-renderizar, re-muestrear— y no una inversión analítica: invertir la
''' cadena exigiría deshacer el tonemap (que no es separable por canal) y la iluminación del punto, y daría
''' un número lindo y equivocado. El lazo no reimplementa NI UN espacio: deja que el pipeline real calcule y
''' sólo lee el resultado.</para>
'''
''' <para><b>Game-aware.</b> Los cuatro valores son deltas canónicos en [-1..1] (<see cref="SkinToneQnamOffset"/>);
''' la UI muestra R/G/B en bytes y la intensidad en porcentaje. Dónde entra cada uno lo decide el resolver de
''' cada juego, no este formulario: en FO4 la intensidad ES el alpha del QNAM (la opacidad del soft-light del
''' cuerpo) y en SSE —donde el QNAM no tiene alpha— se PLIEGA dentro del color con el seed y la convención que
''' resuelve la config.</para></summary>
Public Class SkinTintPanel

    Public Sub New()
        ' Sin esta llamada el control se construye VACIO y el tab sale en blanco -- compilando VERDE, porque
        ' nada en el arbol exige que un UserControl tenga ctor. Es la misma clase de agujero que el
        ' ToolTip(components) sin asignar de EditFace_Form (ver 20-app-ui-migracion-designer-npc-manager.md).
        InitializeComponent()
    End Sub

    ' =====================================================================
    ' Puente al host. Este control era un PARCIAL de EditBody_Form y leia sus campos directo; ahora es una
    ' clase aparte, asi que los nueve miembros que necesita entran por aca. Se mantienen los MISMOS nombres
    ' que tenian como campos del formulario para que el cuerpo del tab no cambie de forma.
    ' El host se ata en Attach() y el evento del preview en OnPreviewReady(): el PreviewControl no existe al
    ' construirse el formulario -se crea en su Shown-, y por eso el ColorPicked no puede engancharse antes.
    ' =====================================================================
    Private _host As EditBody_Form = Nothing

    Private ReadOnly Property HostPreset As LooksmenuLoader.LooksmenuPreset
        Get
            If _host Is Nothing Then Return Nothing
            Return _host.SkinTintPreset
        End Get
    End Property

    Private ReadOnly Property HostPriorPreset As LooksmenuLoader.LooksmenuPreset
        Get
            If _host Is Nothing Then Return Nothing
            Return _host.SkinTintPriorPreset
        End Get
    End Property

    Private ReadOnly Property HostEditor As NpcRenderHost
        Get
            If _host Is Nothing Then Return Nothing
            Return _host.SkinTintEditorHost
        End Get
    End Property

    Private ReadOnly Property HostMain As MainForm
        Get
            If _host Is Nothing Then Return Nothing
            Return _host.SkinTintMainForm
        End Get
    End Property

    Private ReadOnly Property HostPreview As PreviewControl
        Get
            If _host Is Nothing Then Return Nothing
            Return _host.SkinTintPreview
        End Get
    End Property

    Private ReadOnly Property HostNpcFormID As UInteger
        Get
            If _host Is Nothing Then Return 0UI
            Return _host.SkinTintNpcFormID
        End Get
    End Property

    Private ReadOnly Property HostIsSse As Boolean
        Get
            If _host Is Nothing Then Return False
            Return _host.SkinTintIsSse
        End Get
    End Property

    ''' <summary>Se ESCRIBE (el sembrado de sliders lo levanta y lo restaura), asi que va con setter. Sigue
    ''' siendo el flag del formulario: la supresion tiene que valer para todos sus tabs, no solo para este.</summary>
    Private Property HostSuspendEvents As Boolean
        Get
            If _host Is Nothing Then Return False
            Return _host.SkinTintSuspendEvents
        End Get
        Set(value As Boolean)
            If _host IsNot Nothing Then _host.SkinTintSuspendEvents = value
        End Set
    End Property

    ''' <summary>Ata el control a su formulario y siembra el tab. Lo llama el .ctor del host, antes de que
    ''' exista el preview.</summary>
    Friend Sub Attach(host As EditBody_Form)
        _host = host
        InitSkinTintTab()
    End Sub

    ''' <summary>El PreviewControl ya existe (el host lo crea en su Shown): recien ahora se puede enganchar el
    ''' ColorPicked, que es lo que alimenta los dos pickers. Idempotente -RemoveHandler antes de AddHandler-
    ''' porque el host puede rehacer el preview.</summary>
    Friend Sub OnPreviewReady()
        Dim ctl = HostPreview
        If ctl Is Nothing Then Return
        RemoveHandler ctl.ColorPicked, AddressOf HostPreview_ColorPicked
        AddHandler ctl.ColorPicked, AddressOf HostPreview_ColorPicked
        RefreshSkinTintAvailability()
    End Sub

    ''' <summary>Reenvio del SelectedIndexChanged de TabsBody: el control ya no puede escuchar el TabControl
    ''' del formulario. <paramref name="mine"/> es True cuando el tab que quedo activo es el de este control.</summary>
    Friend Sub OnHostTabChanged(mine As Boolean)
        ' Este reenvio llega DENTRO del InitializeComponent del host: agregarle las TabPage al TabControl ya
        ' dispara su SelectedIndexChanged, y ahi el panel todavia no esta atado (Attach corre despues, en el
        ' .ctor). Mismo caso que el ComboBoxSseOverlayZone de EditBody_Form.vb:2105. Sin host no hay nada que
        ' refrescar ni picker que desarmar, asi que se sale.
        If _host Is Nothing Then Return
        ' Salir del tab desarma el picker SIEMPRE (el modo no puede sobrevivir a que el usuario se vaya).
        If Not mine Then
            DisarmSkinTintPicker()
        Else
            RefreshSkinTintAvailability()
        End If
    End Sub

    ''' <summary>Reenvio del FormClosing del host. No se usa el Dispose del propio control: el orden en que
    ''' Winforms destruye los hijos no esta garantizado y el picker tiene que desarmarse mientras el
    ''' PreviewControl todavia esta vivo.</summary>
    Friend Sub OnHostClosing()
        If _host Is Nothing Then Return
        ' Ultimo cinturon: el control se destruye enseguida, pero el modo no puede quedar prendido si algun
        ' dia el preview se reusara. Idempotente y a prueba de disposed.
        DisarmSkinTintPicker()
        Dim ctl = HostPreview
        If ctl IsNot Nothing Then RemoveHandler ctl.ColorPicked, AddressOf HostPreview_ColorPicked
        ' Las dos muestras son Bitmaps propios de este control.
        SetSkinTintPatchImage(_stSourcePatchImage, Nothing, Nothing)
        SetSkinTintPatchImage(_stTargetPatchImage, Nothing, Nothing)
    End Sub

    ' ===== Presupuesto de la búsqueda. Ninguna es un límite del dato: son el techo del lazo. =====
    ''' <summary>Lado de la ventana que se promedia al muestrear. 1 píxel solo queda a merced de un brillo
    ''' especular o del dithering del encode; 3x3 promedia sin cruzar bordes de la silueta.</summary>
    ''' <summary>Dispersion (max - min por canal, en niveles) por encima de la cual el parche NO es un color
    ''' plano: casi siempre significa que agarro el borde de la silueta, una costura o el filo de una sombra, y
    ''' su media deja de representar lo que el usuario quiso elegir. No BLOQUEA el pick -la regla es resolver y
    ''' dejar editar- pero se avisa, porque con 8x8 el riesgo es real donde con 1 pixel no existia.</summary>
    Private Const SkinTintPatchSpreadWarn As Double = 25.0R

    ' ===== Pesos del OBJETIVO que minimiza el auto-calc. =====
    ''' <summary>Peso de la parte de LUMINANCIA del residuo (el componente comun a los tres canales).
    ''' Deliberadamente MENOR que el de croma: un residuo IGUAL en R, G y B es una diferencia de BRILLO, y esa
    ''' sale sobre todo de que los dos puntos elegidos no reciben la misma luz -no es algo que el QNAM deba
    ''' perseguir, y perseguirla es justo lo que empujaba la solucion a los extremos-. </summary>
    Private Const SkinTintLumaWeight As Double = 1.0R
    ''' <summary>Peso de la parte de CROMA del residuo (lo que cada canal se desvia del promedio). Es el
    ''' desajuste de COLOR -el que hace que el cuerpo se lea de otra piel que la cara- y es exactamente lo que
    ''' el ajuste del QNAM puede y debe corregir. Al pesarlo mas, el resolver prefiere repartir el error parejo
    ''' entre los tres canales antes que clavar dos y dejar el tercero lejisimos.</summary>
    Private Const SkinTintChromaWeight As Double = 3.0R
    ''' <summary>Costo por MAGNITUD del ajuste (suma de cuadrados de los cuatro valores, en unidades de UI).
    ''' Sin el, un offset gigante que mejora el match en medio nivel "gana" igual.</summary>
    Private Const SkinTintMagnitudeCost As Double = 0.0005R
    ''' <summary>Costo por DESBALANCE entre R/G/B (cuanto se apartan de su propio promedio). Un desplazamiento
    ''' parejo de los tres es un cambio de brillo del tono y es barato; clavar UN canal en el tope mientras los
    ''' otros quedan cerca de cero es una torsion de color que casi nunca es lo que el usuario quiere.
    ''' <para>Calibrado para ser DESPRECIABLE en soluciones razonables y decisivo en las extremas: para un
    ''' ajuste (20, 10, 30) los dos costos juntos suman ~1 nivel², mientras que para el (177, 40, -255) que
    ''' motivo esta revision suman ~257 - o sea que ese extremo tiene que comprar mas de ~9 niveles por canal
    ''' de mejora real para que el resolver lo prefiera.</para></summary>
    Private Const SkinTintImbalanceCost As Double = 0.002R
    ''' <summary>Tamanos de muestreo ofrecidos, en pixeles de lado. Discretos y no un continuo: 2 alcanza
    ''' para un detalle chico, 8 (el default) promedia 64 pixeles y aguanta el moteado especular, y 16 sirve
    ''' para piel plana bien iluminada. El slider snapea al mas cercano de esta lista.</summary>
    Private Shared ReadOnly SkinTintSampleSizes As Integer() = {2, 4, 6, 8, 12, 16}
    Private Const SkinTintSampleSizeDefault As Integer = 8
    ''' <summary>Presupuesto del lazo, DERIVADO de Quality (ver <see cref="SkinTintQuality"/>). No son
    ''' limites del dato: son el techo de cuanto trabajo se le permite a la busqueda.</summary>
    Private Const SkinTintEvalsPerQuality As Integer = 80
    Private Const SkinTintEvalsBase As Integer = 80
    Private Const SkinTintSecondsPerQuality As Double = 3.0R
    Private Const SkinTintSecondsBase As Double = 3.0R
    ''' <summary>Mejora mínima (en la métrica de error) para aceptar un candidato. Sin epsilon el descenso
    ''' acepta ruido del muestreo y no termina nunca.</summary>
    Private Const SkinTintEpsilon As Double = 0.5R
    ''' <summary>Salto (en unidades de UI) de la diferencia finita con la que se mide si el punto REACCIONA.
    ''' Grande a proposito: con 1-2 unidades la respuesta se pierde en la cuantizacion del framebuffer.</summary>
    Private Const SkinTintProbeUi As Double = 24.0R
    ''' <summary>Reaccion minima para considerar que el punto sirve: niveles de pantalla por unidad de offset.
    ''' 0,05 = 24 unidades de offset mueven el pixel al menos ~1,2 niveles. El umbral ANTERIOR era 0,01, que
    ''' es RUIDO de cuantizacion, y con el la busqueda daba por bueno un punto que no responde.</summary>
    Private Const SkinTintMinSensitivity As Double = 0.05R
    ''' <summary>Iteraciones de biseccion por canal, para Quality 1. Cada punto de Quality suma una: 7 sobre
    ''' un rango de 510 unidades ya deja el paso final en ~4 unidades y el pulido cierra el resto.</summary>
    Private Const SkinTintBisectItersBase As Integer = 6

    Private Enum SkinTintPickTarget
        None = 0
        Source = 1
        Target = 2
    End Enum

    Private _stPick As SkinTintPickTarget = SkinTintPickTarget.None
    Private _stSourceColor As Color = Color.Empty
    Private _stSourcePoint As Point = Point.Empty
    Private _stHasSource As Boolean = False
    Private _stTargetPoint As Point = Point.Empty
    Private _stHasTarget As Boolean = False
    Private _stLastTargetColor As Color = Color.Empty
    ''' <summary>La MUESTRA de pixeles de cada pick, sin escalar, para dibujarla en su swatch. Un color plano
    ''' esconde justo lo que hay que ver: si el parche era piel pareja o si se comio un borde, una costura o el
    ''' filo de una sombra. Se dibuja magnificada con vecino mas cercano para que se vean los pixeles.</summary>
    Private _stSourcePatchImage As Bitmap = Nothing
    Private _stTargetPatchImage As Bitmap = Nothing
    ''' <summary>Firma de cámara+tamaño al momento del pick del DESTINO. Sólo el destino es una POSICIÓN, y
    ''' por eso es el único que el encuadre invalida: si la cámara se movió, ese punto ya no apunta a lo mismo y
    ''' el auto-calc estaría persiguiendo otro lugar. El ORIGEN es un COLOR latcheado — sobrevive a cualquier
    ''' movimiento de cámara, así que atarlo a la firma sólo generaba falsos positivos (y, peor, el falso
    ''' NEGATIVO de re-picar el origen después de mover la cámara: la firma se refrescaba y daba por bueno un
    ''' destino ya viejo).</summary>
    Private _stTargetCameraSignature As String = ""
    ''' <summary>Firma del encuadre al momento del pick del ORIGEN. NO se usa para invalidarlo -su color queda
    ''' latcheado y sobrevive a cualquier movimiento de camara- sino para saber si su POSICION todavia apunta a
    ''' lo mismo, unico caso en el que se lo puede volver a medir al cambiar el tamano de muestra.</summary>
    Private _stSourceCameraSignature As String = ""
    Private _stBusy As Boolean = False

    ''' <summary>Lo único del tab que NO puede vivir en el Designer: el texto depende del juego, y
    ''' <c>InitializeComponent</c> sólo admite código declarativo plano (sin ramas). El default del Designer
    ''' es el de FO4; acá se reemplaza bajo Skyrim. Además siembra los sliders desde el overlay.</summary>
    ''' <summary>Nivel de esfuerzo del auto-calc, leido del slider "Quality (passes)". 1 = una pasada rapida,
    ''' 6 = exhaustivo. Gobierna CUATRO cosas a la vez -barridos de biseccion, iteraciones por canal, pasos del
    ''' pulido y presupuesto de muestras/tiempo- porque subir uno solo no cambia el resultado: la busqueda se
    ''' corta por el que primero se agote.</summary>
    ''' <summary>Lado de la ventana de muestreo, leido del slider "Sample size". Todo lo que muestrea -los dos
    ''' pickers, el re-muestreo del destino y cada iteracion del auto-calc- pasa por aca, asi que origen y
    ''' destino SIEMPRE se comparan con la misma ventana.</summary>
    Private Function SkinTintSampleBox() As Integer
        If SliderSkinTintSampleSize Is Nothing Then Return SkinTintSampleSizeDefault
        Return SnapSkinTintSampleSize(SliderSkinTintSampleSize.Value)
    End Function

    ''' <summary>Valor permitido mas cercano de <see cref="SkinTintSampleSizes"/>.</summary>
    Private Shared Function SnapSkinTintSampleSize(raw As Double) As Integer
        Dim bestVal As Integer = SkinTintSampleSizeDefault
        Dim bestDist As Double = Double.MaxValue
        For Each v In SkinTintSampleSizes
            Dim dist = Math.Abs(raw - v)
            If dist < bestDist Then
                bestDist = dist
                bestVal = v
            End If
        Next
        Return bestVal
    End Function

    ''' <summary>El slider snapea a la lista y, si ya hay picks, los VUELVE A MEDIR con la ventana nueva: origen
    ''' y destino tienen que salir del mismo tamano de muestra o la comparacion arrastra un sesgo. El origen solo
    ''' se puede re-medir si el encuadre sigue siendo el de su pick (es una POSICION de pantalla); si la camara
    ''' se movio se conserva el color latcheado y se avisa.</summary>
    Private Sub OnSkinTintSampleSizeChanged(sender As Object, e As EventArgs) Handles SliderSkinTintSampleSize.ValueChanged
        If HostSuspendEvents OrElse _stBusy Then Return
        Dim snapped = SnapSkinTintSampleSize(SliderSkinTintSampleSize.Value)
        If Math.Abs(SliderSkinTintSampleSize.Value - snapped) > 0.001R Then
            Dim prev = HostSuspendEvents
            HostSuspendEvents = True
            Try
                SliderSkinTintSampleSize.Value = snapped
            Finally
                HostSuspendEvents = prev
            End Try
        End If

        Dim ctl = HostPreview
        If ctl Is Nothing OrElse ctl.IsDisposed Then Return

        Dim staleSource As Boolean = False
        If _stHasSource Then
            If String.Equals(_stSourceCameraSignature, SkinTintCameraSignature(), StringComparison.Ordinal) Then
                Dim patch = ctl.ReadPixelPatch(_stSourcePoint.X, _stSourcePoint.Y, snapped, presentFrame:=False, wantImage:=True)
                Dim c = patch.Mean
                If Not c.IsEmpty Then
                    _stSourceColor = c
                    PanelSkinTintSourceSwatch.BackColor = c
                    SetSkinTintPatchImage(_stSourcePatchImage, patch.Image, PanelSkinTintSourceSwatch)
                    LabelSkinTintSource.Text = $"RGB({c.R}, {c.G}, {c.B})  @ ({_stSourcePoint.X}, {_stSourcePoint.Y})  ·  {snapped}x{snapped} avg"
                End If
            Else
                staleSource = True
            End If
        End If

        ResampleSkinTintTarget()
        If staleSource Then
            LabelSkinTintGate.Text = "The source colour was sampled with the previous window size and the camera has moved since, so it could not be re-measured. Pick the source again to use the new sample size."
        Else
            LabelSkinTintGate.Text = ""
        End If
        UpdateSkinTintStatus()
    End Sub

    Private Function SkinTintQuality() As Integer
        If SliderSkinTintQuality Is Nothing Then Return 3
        Dim q = CInt(Math.Round(SliderSkinTintQuality.Value))
        If q < 1 Then q = 1
        If q > 6 Then q = 6
        Return q
    End Function

    Private Sub InitSkinTintTab()
        If HostIsSse Then
            LabelSkinTintIntensityMeaning.Text = "Intensity is folded into the colour (Skyrim's QNAM has no alpha): it moves the skin-tone layer's interpolation."
        End If
        SyncSkinTintSlidersFromOverlay()
    End Sub

    ' =====================================================================
    ' Modelo <-> UI
    ' =====================================================================

    ''' <summary>El offset VIVO del overlay, creándolo si hace falta. Nothing sólo si no hay preset.</summary>
    Private Function EnsureSkinTintOffset() As SkinToneQnamOffset
        Dim p = HostPreset
        If p Is Nothing Then Return Nothing
        If p.SkinToneOffset Is Nothing Then p.SkinToneOffset = New SkinToneQnamOffset()
        Return p.SkinToneOffset
    End Function

    Private Sub SyncSkinTintSlidersFromOverlay()
        Dim p = HostPreset
        Dim off As SkinToneQnamOffset = Nothing
        If p IsNot Nothing Then off = p.SkinToneOffset
        Dim r As Double = 0.0R, g As Double = 0.0R, b As Double = 0.0R, i As Double = 0.0R
        If off IsNot Nothing Then
            r = off.RUi : g = off.GUi : b = off.BUi : i = off.IntensityUi
        End If
        Dim prev = HostSuspendEvents
        HostSuspendEvents = True
        Try
            SliderSkinTintR.Value = Math.Round(r)
            SliderSkinTintG.Value = Math.Round(g)
            SliderSkinTintB.Value = Math.Round(b)
            SliderSkinTintIntensity.Value = Math.Round(i)
        Finally
            HostSuspendEvents = prev
        End Try
    End Sub

    Private Sub WriteSkinTintOffsetFromSliders()
        Dim off = EnsureSkinTintOffset()
        If off Is Nothing Then Return
        off.RUi = CSng(SliderSkinTintR.Value)
        off.GUi = CSng(SliderSkinTintG.Value)
        off.BUi = CSng(SliderSkinTintB.Value)
        off.IntensityUi = CSng(SliderSkinTintIntensity.Value)
    End Sub

    ''' <summary>Aplica un offset al overlay y REPINTA por el camino barato: sólo se re-resuelve el tono del
    ''' cuerpo y se reescriben sus uniforms — no se recompone la cara ni se toca una textura, porque el ajuste
    ''' no entra por ahí. Devuelve False si el host todavía no tiene estado (preview sin render).</summary>
    Private Function ApplySkinTintOffsetLive(off As SkinToneQnamOffset, deferredRepaint As Boolean) As Boolean
        Dim p = HostPreset
        If p Is Nothing OrElse HostEditor Is Nothing OrElse HostMain Is Nothing Then Return False
        p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(off)
        Dim ok As Boolean = HostMain.RefreshBodySkinToneLive(p.SkinToneOffset, HostEditor)
        Dim ctl = HostEditor.PreviewCtl
        If ctl IsNot Nothing Then
            If deferredRepaint Then
                ' RefreshRender solo ENCOLA el repintado (UpdateRequired + Invalidate). El Update() drena el
                ' WM_PAINT AHORA, asi el resultado del auto-calc y cada movimiento de slider se ven en el acto
                ' en vez de esperar al siguiente ciclo del message pump -- y sin recargar nada: el camino sigue
                ' siendo el barato (re-resolver el tono + reescribir uniforms). Mismo idioma que 00-reglas-ui-y-vb §8.
                ctl.RefreshRender()
                ctl.Update()
            Else
                ' El lazo del auto-calc no espera a WM_PAINT: ReadPixelDisplay(presentFrame:=False) dibuja al
                ' back buffer y lee de ahí, sin presentar. Sólo se marca el frame como pendiente para que el
                ' repintado normal posterior no se saltee.
                ctl.UpdateRequired = True
            End If
        End If
        Return ok
    End Function

    Private Sub OnSkinTintSliderChanged(sender As Object, e As EventArgs) _
        Handles SliderSkinTintR.ValueChanged, SliderSkinTintG.ValueChanged,
                SliderSkinTintB.ValueChanged, SliderSkinTintIntensity.ValueChanged
        If HostSuspendEvents OrElse _stBusy Then Return
        WriteSkinTintOffsetFromSliders()
        ApplySkinTintOffsetLive(EnsureSkinTintOffset(), deferredRepaint:=True)
        UpdateSkinTintStatus()
    End Sub

    Private Sub OnSkinTintSliderDragEnded(sender As Object, e As EventArgs) _
        Handles SliderSkinTintR.DragEnded, SliderSkinTintG.DragEnded,
                SliderSkinTintB.DragEnded, SliderSkinTintIntensity.DragEnded
        If HostSuspendEvents OrElse _stBusy Then Return
        ' Al soltar el slider (no en cada tick) se vuelve a leer el píxel de destino: el número que el usuario
        ' está persiguiendo tiene que ser el que ve, no el de hace tres arrastres.
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    Private Sub OnSkinTintResetClick(sender As Object, e As EventArgs) Handles ButtonSkinTintReset.Click
        If _stBusy Then Return
        Dim off = EnsureSkinTintOffset()
        If off Is Nothing Then Return
        off.R = 0.0F : off.G = 0.0F : off.B = 0.0F : off.Intensity = 0.0F
        SyncSkinTintSlidersFromOverlay()
        ApplySkinTintOffsetLive(off, deferredRepaint:=True)
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    ''' <summary>"Reset Section" del tab: vuelve al snapshot que el formulario tomó al abrirse (misma
    ''' semántica que el resto de las secciones), no a cero — el cero lo da el botón propio del tab.</summary>
    Friend Sub ResetSkinTintSection()
        Dim p = HostPreset
        If p Is Nothing Then Return
        Dim prior As SkinToneQnamOffset = Nothing
        If HostPriorPreset IsNot Nothing Then prior = HostPriorPreset.SkinToneOffset
        p.SkinToneOffset = SkinToneQnamOffset.CloneOrNothing(prior)
        SyncSkinTintSlidersFromOverlay()
        ApplySkinTintOffsetLive(p.SkinToneOffset, deferredRepaint:=True)
        ResampleSkinTintTarget()
        UpdateSkinTintStatus()
    End Sub

    ' =====================================================================
    ' Picker
    ' =====================================================================

    Private Sub OnSkinTintPickSourceClick(sender As Object, e As EventArgs) Handles ButtonSkinTintPickSource.Click
        ToggleSkinTintPick(SkinTintPickTarget.Source)
    End Sub

    Private Sub OnSkinTintPickTargetClick(sender As Object, e As EventArgs) Handles ButtonSkinTintPickTarget.Click
        ToggleSkinTintPick(SkinTintPickTarget.Target)
    End Sub

    ''' <summary>Arma (o desarma, si ya estaba armado el mismo) el modo picker del preview embebido. Es un
    ''' modo de UN disparo: el primer click muestrea y desarma. Ver <see cref="DisarmSkinTintPicker"/> para
    ''' las otras vías de salida.</summary>
    Private Sub ToggleSkinTintPick(what As SkinTintPickTarget)
        If _stBusy Then Return
        If _stPick = what Then
            DisarmSkinTintPicker()
            Return
        End If
        If HostPreview Is Nothing OrElse HostPreview.IsDisposed Then
            LabelSkinTintGate.Text = "The preview is not ready yet."
            Return
        End If
        _stPick = what
        HostPreview.ColorPickMode = True
        UpdateSkinTintPickButtons()
        LabelSkinTintGate.Text = ""
        If what = SkinTintPickTarget.Source Then
            LabelSkinTintStatus.Text = "Click the FACE in the preview to take the source colour."
        Else
            LabelSkinTintStatus.Text = "Click the BODY in the preview to mark the point to match."
        End If
    End Sub

    ''' <summary>Apaga el modo picker SIEMPRE que se salga de él — por un pick, por cambiar de tab, por
    ''' cerrar el formulario o por apretar de nuevo el mismo botón. El preview es un control COMPARTIDO con
    ''' las otras apps: dejarlo en modo picker le robaría el botón izquierdo a la cámara.</summary>
    Private Sub DisarmSkinTintPicker()
        _stPick = SkinTintPickTarget.None
        If HostPreview IsNot Nothing AndAlso Not HostPreview.IsDisposed Then
            HostPreview.ColorPickMode = False
        End If
        UpdateSkinTintPickButtons()
    End Sub

    Private Sub UpdateSkinTintPickButtons()
        If _stPick = SkinTintPickTarget.Source Then
            ButtonSkinTintPickSource.Text = "Picking... (cancel)"
        Else
            ButtonSkinTintPickSource.Text = "Pick source (face)..."
        End If
        If _stPick = SkinTintPickTarget.Target Then
            ButtonSkinTintPickTarget.Text = "Picking... (cancel)"
        Else
            ButtonSkinTintPickTarget.Text = "Pick target (body)..."
        End If
    End Sub

    Private Sub HostPreview_ColorPicked(sender As Object, e As PreviewControl.ColorPickedEventArgs)
        Dim what = _stPick
        DisarmSkinTintPicker()
        If what = SkinTintPickTarget.None OrElse e Is Nothing Then Return
        If e.Color.IsEmpty Then
            LabelSkinTintStatus.Text = "Could not read that pixel (was the preview drawn?). Try again."
            Return
        End If

        ' El evento ya trae la MEDIA del parche; se re-lee para tener tambien su dispersion y poder avisar
        ' cuando la ventana no es un color plano.
        ' UNA sola lectura para las tres cosas que necesita el pick: la media (que ya vino en el evento pero
        ' se re-lee junto), la dispersión para el aviso, y la imagen para el swatch.
        Dim box As Integer = SkinTintSampleBox()
        Dim spread As Double = 0.0R
        Dim patchImg As Bitmap = Nothing
        If HostPreview IsNot Nothing AndAlso Not HostPreview.IsDisposed Then
            Dim patch = HostPreview.ReadPixelPatch(e.X, e.Y, box, presentFrame:=False, wantImage:=True)
            spread = patch.Spread
            patchImg = patch.Image
        End If

        If what = SkinTintPickTarget.Source Then
            _stSourceColor = e.Color
            _stSourcePoint = New Point(e.X, e.Y)
            _stHasSource = True
            PanelSkinTintSourceSwatch.BackColor = e.Color
            SetSkinTintPatchImage(_stSourcePatchImage, patchImg, PanelSkinTintSourceSwatch)
            _stSourceCameraSignature = SkinTintCameraSignature()
            LabelSkinTintSource.Text = $"RGB({e.Color.R}, {e.Color.G}, {e.Color.B})  @ ({e.X}, {e.Y})  ·  {box}x{box} avg"
        Else
            _stTargetPoint = New Point(e.X, e.Y)
            _stHasTarget = True
            _stLastTargetColor = e.Color
            PanelSkinTintTargetSwatch.BackColor = e.Color
            SetSkinTintPatchImage(_stTargetPatchImage, patchImg, PanelSkinTintTargetSwatch)
            LabelSkinTintTarget.Text = $"position ({e.X}, {e.Y}) — now RGB({e.Color.R}, {e.Color.G}, {e.Color.B})  ·  {box}x{box} avg"
            ' La firma se toma SÓLO acá: es la única latcheada como posición.
            _stTargetCameraSignature = SkinTintCameraSignature()
        End If

        If spread > SkinTintPatchSpreadWarn Then
            LabelSkinTintGate.Text = $"That {box}x{box} patch is not a flat colour (spread {spread:F0} levels): it probably straddles an edge, a seam or a shadow border, so its average is not the colour you meant. Pick a flatter spot for a reliable match."
        End If

        UpdateSkinTintStatus()
    End Sub

    ''' <summary>Re-muestrea el píxel de DESTINO y refresca su swatch/etiqueta. No-op si no hay destino
    ''' elegido o si el encuadre cambió (el punto ya no apunta a lo mismo).</summary>
    Private Sub ResampleSkinTintTarget()
        If Not _stHasTarget OrElse SkinTintFrameMoved() Then Return
        Dim ctl = HostPreview
        If ctl Is Nothing OrElse ctl.IsDisposed Then Return
        ' Mismo muestreo determinista que el auto-calc: si leyera el front despues de un swap encolado, el
        ' numero que se muestra podria ser el del ajuste ANTERIOR.
        Dim box As Integer = SkinTintSampleBox()
        Dim patch = ctl.ReadPixelPatch(_stTargetPoint.X, _stTargetPoint.Y, box, presentFrame:=False, wantImage:=True)
        Dim c = patch.Mean
        If c.IsEmpty Then Return
        _stLastTargetColor = c
        PanelSkinTintTargetSwatch.BackColor = c
        SetSkinTintPatchImage(_stTargetPatchImage, patch.Image, PanelSkinTintTargetSwatch)
        Dim delta As String = ""
        If _stHasSource Then
            delta = $"  |  delta ({CInt(c.R) - CInt(_stSourceColor.R):+0;-0;0}, {CInt(c.G) - CInt(_stSourceColor.G):+0;-0;0}, {CInt(c.B) - CInt(_stSourceColor.B):+0;-0;0})"
        End If
        LabelSkinTintTarget.Text = $"position ({_stTargetPoint.X}, {_stTargetPoint.Y}) — now RGB({c.R}, {c.G}, {c.B})  ·  {box}x{box} avg{delta}"
    End Sub

    ''' <summary>Instala la imagen de una muestra en su slot, liberando la anterior (una por pick, y el pick
    ''' se repite: sin el Dispose se van acumulando bitmaps).</summary>
    Private Shared Sub SetSkinTintPatchImage(ByRef slot As Bitmap, img As Bitmap, panel As Panel)
        If slot IsNot img Then
            If slot IsNot Nothing Then slot.Dispose()
            slot = img
        End If
        If panel IsNot Nothing Then panel.Invalidate()
    End Sub

    Private Sub PanelSkinTintSourceSwatch_Paint(sender As Object, e As PaintEventArgs) Handles PanelSkinTintSourceSwatch.Paint
        PaintSkinTintPatch(e, _stSourcePatchImage, PanelSkinTintSourceSwatch)
    End Sub

    Private Sub PanelSkinTintTargetSwatch_Paint(sender As Object, e As PaintEventArgs) Handles PanelSkinTintTargetSwatch.Paint
        PaintSkinTintPatch(e, _stTargetPatchImage, PanelSkinTintTargetSwatch)
    End Sub

    ''' <summary>Dibuja la muestra magnificada al tamanio del swatch. <c>NearestNeighbor</c> + <c>Half</c> de
    ''' offset a proposito: se quiere ver el MOSAICO real de pixeles muestreados, no una version suavizada que
    ''' volveria a esconder la variacion. Sin imagen no hace nada y queda el BackColor (la media).</summary>
    Private Shared Sub PaintSkinTintPatch(e As PaintEventArgs, img As Bitmap, panel As Panel)
        If e Is Nothing OrElse img Is Nothing OrElse panel Is Nothing Then Return
        Dim g = e.Graphics
        Dim prevInterp = g.InterpolationMode
        Dim prevOffset = g.PixelOffsetMode
        Try
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half
            g.DrawImage(img, panel.ClientRectangle)
        Catch
        Finally
            g.InterpolationMode = prevInterp
            g.PixelOffsetMode = prevOffset
        End Try
    End Sub

    ''' <summary>Firma del encuadre (cámara + tamaño del control). Si cambia, los píxeles elegidos dejan de
    ''' apuntar a lo que el usuario eligió — y el auto-calc estaría optimizando contra otro punto.</summary>
    Private Function SkinTintCameraSignature() As String
        Dim ctl = HostPreview
        If ctl Is Nothing OrElse ctl.IsDisposed OrElse ctl.camera Is Nothing Then Return ""
        Dim c = ctl.camera
        Return String.Format(Globalization.CultureInfo.InvariantCulture,
                             "{0:F3}|{1:F3},{2:F3},{3:F3}|{4:F4},{5:F4},{6:F4}|{7}x{8}",
                             c.distance, c.FocusPosition.X, c.FocusPosition.Y, c.FocusPosition.Z,
                             c.Forward.X, c.Forward.Y, c.Forward.Z, ctl.Width, ctl.Height)
    End Function

    Private Function SkinTintFrameMoved() As Boolean
        If Not _stHasTarget OrElse String.IsNullOrEmpty(_stTargetCameraSignature) Then Return False
        Return Not String.Equals(_stTargetCameraSignature, SkinTintCameraSignature(), StringComparison.Ordinal)
    End Function

    ' =====================================================================
    ' Auto-calc (lazo cerrado)
    ' =====================================================================

    ''' <summary>Busca los cuatro offsets que llevan el pixel de destino al color de origen. Tres fases, todas
    ''' midiendo contra el render REAL (aplicar -> renderizar -> leer ese pixel); no hay ni una inversion
    ''' analitica de la cadena del motor.
    ''' <para><b>Fase 0 - ¿reacciona?</b> Una diferencia finita por canal mide cuanto se mueve el pixel por
    ''' unidad de offset. Solo decide si el punto SIRVE (umbral <see cref="SkinTintMinSensitivity"/>); si no
    ''' reacciona se avisa y se corta, en vez de devolver un resultado silenciosamente malo.</para>
    ''' <para><b>Fase 1 - biseccion por canal.</b> El valor del pixel es MONOTONO en el offset de su canal
    ''' (clamp lineal -> soft-light -> tonemap, las tres monotonas), asi que se bisecta el rango legal
    ''' [-255, 255] buscando el offset cuyo canal cae sobre el del origen. NO se divide por la sensibilidad:
    ''' un paso de Newton (<c>necesito / sensibilidad</c>) con sensibilidad chica daba saltos de miles de
    ''' unidades, clavaba los offsets en los topes y dejaba el QNAM efectivo saturado (medido en la app:
    ''' R+177 / B-255 => efectivo (255,255,0), cuerpo amarillo). La biseccion esta ACOTADA por construccion.
    ''' Cada canal se acepta solo si baja el error TOTAL.</para>
    ''' <para><b>Fase 2 - pulido.</b> Descenso por coordenadas con pasos chicos.</para>
    ''' <para><b>LA INTENSIDAD NO ES UN EJE LIBRE DE LA BUSQUEDA, Y ESO ES EL NUCLEO DEL DISENIO.</b> El
    ''' shader hace <c>resultado = mix(a, softlight(a, t), α)</c>, y el soft-light es IDENTIDAD en t = 0,5
    ''' (<c>2a(1−t) + √a(2t−1)</c> con t=0,5 da a). O sea <c>resultado − a ≈ α · c(a) · (t − 0,5)</c>: el tono y
    ''' la intensidad entran MULTIPLICANDOSE. En SSE es exacto y no aproximado, porque el pliegue es literalmente
    ''' <c>lerp(seed, color, TINV)</c>. Consecuencia: duplicar (t−0,5) y partir α al medio da EL MISMO PIXEL —
    ''' hay una familia de infinitas soluciones equivalentes, un valle plano. Un descenso por coordenadas no
    ''' puede recorrer ese valle (es diagonal: hay que mover los cuatro a la vez en proporcion), asi que
    ''' zigzaguea y se planta en cualquier punto de el, tipicamente contra un tope. Eso explicaba a la vez los
    ''' ajustes extremos en un canal Y que subir los pases no cambiara nada: mas iteraciones del mismo
    ''' movimiento no atraviesan un valle diagonal.
    ''' <para>La solucion NO es pelear contra el valle sino no crearlo: la busqueda mueve SOLO R/G/B, con la
    ''' intensidad en 0. Y no se pierde nada, porque lo unico que la intensidad aporta y el RGB no es ALCANCE
    ''' cuando el tono se satura: bajar α equivale a acercar t a 0,5, que el RGB hace solo. Por eso la fase de
    ''' alcance de abajo la sube unicamente cuando el tono TOCO un tope y todavia queda residuo.</para></para>
    ''' <para>Al final, red de seguridad: si el resultado es PEOR que el punto de partida se restaura el de
    ''' partida. El auto-calc nunca deja el ajuste peor de como estaba.</para></summary>
    Private Sub OnSkinTintAutoClick(sender As Object, e As EventArgs) Handles ButtonSkinTintAuto.Click
        If _stBusy Then Return
        If Not _stHasSource OrElse Not _stHasTarget Then
            LabelSkinTintGate.Text = "Pick a SOURCE pixel (face) and a TARGET pixel (body) first."
            Return
        End If
        If SkinTintFrameMoved() Then
            LabelSkinTintGate.Text = "The camera moved after the target was picked: pick the target again (it is a screen position)."
            Return
        End If
        LabelSkinTintGate.Text = ""

        _stBusy = True
        Dim prevCursor = Me.Cursor
        Me.Cursor = Cursors.WaitCursor
        SetSkinTintControlsEnabled(False)
        ' Lo que el usuario tenía ANTES de apretar el botón. No es el punto de partida de la búsqueda (ver
        ' abajo): sólo se usa para restaurarlo si la corrida se cae con excepción.
        Dim priorOffset = SkinToneQnamOffset.CloneOrNothing(EnsureSkinTintOffset())
        Try
            ' SIEMPRE se arranca de (0, 0, 0, 0) = el QNAM tal como lo DERIVA el record, no de los offsets
            ' que hubiera puestos. Dos razones:
            '   1. DETERMINISMO: el mismo NPC con los mismos dos píxeles da siempre el mismo resultado, sin
            '      depender de cuántas veces se apretó el botón antes.
            '   2. Un ajuste previo puede tener el QNAM efectivo SATURADO (algún canal en 0 o 255), y ahí el
            '      punto deja de reaccionar a cualquier cambio: la búsqueda arrancaría ciega. Medido en la app:
            '      con R+177 / B−255 el efectivo quedaba (255,255,0) y la fase 0 reportaba "no reacciona".
            Dim best As New SkinToneQnamOffset()
            Dim cur As Color = MeasureSkinTintTarget(best)
            If cur.IsEmpty Then
                LabelSkinTintGate.Text = "Could not sample the target point. Is it still on the body?"
                Return
            End If
            Dim evals As Integer = 1
            Dim sw = Stopwatch.StartNew()
            Dim quality As Integer = SkinTintQuality()
            Dim maxEvals As Integer = SkinTintEvalsBase + SkinTintEvalsPerQuality * quality
            Dim maxSeconds As Double = SkinTintSecondsBase + SkinTintSecondsPerQuality * quality
            Dim bisectIters As Integer = SkinTintBisectItersBase + quality
            Dim startCol As Color = cur
            Dim startErr As Double = SkinTintObjective(cur, best)
            Dim bestErr As Double = startErr

            ' --- Fase 0: ¿este punto REACCIONA al tono del cuerpo? ---
            ' Una diferencia finita por canal. Sólo decide si vale la pena seguir; la búsqueda de abajo NO
            ' divide por este número (ver el porqué en el resumen del método).
            Dim sens(2) As Double
            Dim anyResponse As Boolean = False
            For ch As Integer = 0 To 2
                Dim signUsed As Double = 1.0R
                Dim probe = OffsetWithChannelDelta(best, ch, SkinTintProbeUi)
                If probe Is Nothing Then
                    ' El canal ya está pegado al tope: se mide hacia el otro lado.
                    probe = OffsetWithChannelDelta(best, ch, -SkinTintProbeUi)
                    signUsed = -1.0R
                End If
                If probe Is Nothing Then Continue For
                Dim col = MeasureSkinTintTarget(probe)
                evals += 1
                If col.IsEmpty Then Continue For
                sens(ch) = (SkinTintChannel(col, ch) - SkinTintChannel(cur, ch)) / (signUsed * SkinTintProbeUi)
                If Math.Abs(sens(ch)) > SkinTintMinSensitivity Then anyResponse = True
            Next

            If Not anyResponse Then
                ' Se restaura el punto de partida de la búsqueda (cero = el tono del record): las pruebas de
                ' arriba dejaron candidatos aplicados.
                ApplySkinTintOffsetLive(New SkinToneQnamOffset(), deferredRepaint:=True)
                SyncSkinTintSlidersFromOverlay()
                ResampleSkinTintTarget()
                Dim why As String
                If SkinTintOffsetAtRail(best) Then
                    why = "the current offsets already push QNAM to its limits, so the tone cannot move any further. Reset to 0 and try again, or pick a different target."
                Else
                    why = "it is probably on clothing, hair or a head part (head/neck use the face pipeline, not the body tone), or it is fully in shadow. Pick a lit spot of bare body skin."
                End If
                LabelSkinTintGate.Text = "That target pixel does not react to the body skin tone: " & why
                Return
            End If

            ' --- Fase 1: BISECCIÓN por canal ---
            ' El valor del píxel es MONÓTONO en el offset de su canal (clamp lineal → soft-light → tonemap, las
            ' tres monótonas), así que se busca por bisección el offset cuyo canal cae sobre el del origen.
            ' Esto REEMPLAZA un paso de Newton (necesito/sensibilidad) que era el bug de fondo: con una
            ' sensibilidad chica la división daba saltos de miles de unidades, los offsets se clavaban en ±255
            ' y el QNAM efectivo quedaba saturado (ej. medido: R+177, B−255 ⇒ efectivo (255,255,0), cuerpo
            ' amarillo). La bisección está ACOTADA al rango legal por construcción: no puede explotar, y cuando
            ' el color pedido es inalcanzable converge al tope, que es lo mejor disponible.
            Dim sweepAccepted As Boolean
            For sweep As Integer = 1 To quality
                sweepAccepted = False
                For ch As Integer = 0 To 2
                    If evals >= maxEvals OrElse sw.Elapsed.TotalSeconds > maxSeconds Then Exit For
                    If Math.Abs(sens(ch)) <= SkinTintMinSensitivity Then Continue For

                    Dim aim As Double = CDbl(SkinTintChannel(_stSourceColor, ch))
                    Dim lo As Double = -SkinToneQnamOffset.RgbUiScale
                    Dim hi As Double = SkinToneQnamOffset.RgbUiScale

                    ' Extremos: si el objetivo queda fuera del rango alcanzable, el mejor es el tope.
                    Dim colLo = MeasureSkinTintTarget(WithChannelUi(best, ch, lo)) : evals += 1
                    Dim colHi = MeasureSkinTintTarget(WithChannelUi(best, ch, hi)) : evals += 1
                    If colLo.IsEmpty OrElse colHi.IsEmpty Then Continue For
                    Dim vLo As Double = SkinTintChannel(colLo, ch)
                    Dim vHi As Double = SkinTintChannel(colHi, ch)

                    Dim pick As Double
                    If vLo >= aim Then
                        pick = lo
                    ElseIf vHi <= aim Then
                        pick = hi
                    Else
                        For it As Integer = 1 To bisectIters
                            If evals >= maxEvals OrElse sw.Elapsed.TotalSeconds > maxSeconds Then Exit For
                            Dim mid As Double = (lo + hi) * 0.5R
                            Dim colMid = MeasureSkinTintTarget(WithChannelUi(best, ch, mid))
                            evals += 1
                            If colMid.IsEmpty Then Exit For
                            If SkinTintChannel(colMid, ch) < aim Then lo = mid Else hi = mid
                        Next
                        pick = (lo + hi) * 0.5R
                    End If

                    ' NO se salta directo a `pick`. La bisección iguala UN canal sin mirar el resto, y cuando
                    ' ese canal no puede alcanzar el objetivo devuelve el TOPE — que es de donde salían los
                    ' ajustes clavados en ±255 que compraban dos niveles de mejora. Se recorre el camino hacia
                    ' `pick` en fracciones y se elige por el OBJETIVO (color + costo del ajuste), así el
                    ' resolver se queda en el punto donde la mejora todavía paga lo que cuesta.
                    Dim fromUi As Double = OffsetChannelUi(best, ch)
                    For Each frac As Double In New Double() {1.0R, 0.7R, 0.45R, 0.25R}
                        If evals >= maxEvals OrElse sw.Elapsed.TotalSeconds > maxSeconds Then Exit For
                        Dim tryUi As Double = Math.Round(fromUi + (pick - fromUi) * frac)
                        If Math.Abs(tryUi - fromUi) < 0.5R Then Continue For
                        Dim cand = WithChannelUi(best, ch, tryUi)
                        Dim colCand = MeasureSkinTintTarget(cand)
                        evals += 1
                        If colCand.IsEmpty Then Continue For
                        Dim errCand = SkinTintObjective(colCand, cand)
                        If errCand < bestErr Then
                            best = cand
                            bestErr = errCand
                            cur = colCand
                            sweepAccepted = True
                        End If
                    Next
                Next
                ' Corte por ESTANCAMIENTO. Un barrido que no acepta un solo movimiento significa que el punto
                ' es estacionario para estos movimientos: repetirlo da resultados idénticos y sólo quema
                ' renders. Es lo que hace que subir "Quality" sea inofensivo en vez de lento.
                If Not sweepAccepted Then Exit For
            Next

            ' --- Fase 1b: ALCANCE por intensidad, sólo si hace falta ---
            ' La intensidad se mantuvo en 0 justamente para no crear el valle degenerado. Se sube sólo en el
            ' único caso en que aporta algo que el RGB no puede dar: el tono efectivo ya TOCÓ un tope y aún
            ' queda residuo de color. Ahí, amplificar el soft-light extiende el alcance. Cada valor que se
            ' prueba se acepta por el OBJETIVO, así que si no compra mejora real el costo de magnitud lo
            ' descarta y la intensidad se queda en 0 — que es la solución de norma mínima.
            If SkinTintOffsetAtRail(best) AndAlso SkinTintResidual(cur) > 4.0R Then
                For Each extra As Double In New Double() {15.0R, 30.0R, 50.0R, 75.0R, 100.0R}
                    If evals >= maxEvals OrElse sw.Elapsed.TotalSeconds > maxSeconds Then Exit For
                    Dim cand = best.Clone()
                    cand.IntensityUi = CSng(ClampUi(extra, SkinToneQnamOffset.IntensityUiScale))
                    Dim col = MeasureSkinTintTarget(cand)
                    evals += 1
                    If col.IsEmpty Then Continue For
                    Dim err = SkinTintObjective(col, cand)
                    If err < bestErr Then
                        best = cand
                        bestErr = err
                        cur = col
                    End If
                Next
            End If

            ' --- Fase 2: pulido por coordenadas sobre los cuatro valores ---
            ' Pasos del pulido según Quality: con 1 se queda en el grueso (8, 4) y recién de 3 para arriba
            ' baja al paso de 1 unidad, que es el que más muestras consume y menos mueve la aguja.
            Dim allSteps() As Double = {8.0R, 4.0R, 2.0R, 1.0R}
            Dim stepCount As Integer = Math.Min(allSteps.Length, quality + 1)
            ' La intensidad entra al pulido SÓLO si la fase de alcance la levantó de cero. Si sigue en cero, el
            ' espacio de búsqueda es no degenerado y hay que mantenerlo así: dejarla entrar re-crearía el valle
            ' que la fase 1 evita, y el pulido volvería a zigzaguear.
            Dim lastChannel As Integer = If(best.Intensity <> 0.0F, 3, 2)
            Dim aborted As Boolean = False
            For si As Integer = 0 To stepCount - 1
                If aborted Then Exit For
                Dim improved As Boolean = True
                While improved
                    improved = False
                    For ch As Integer = 0 To lastChannel
                        For dir As Integer = -1 To 1 Step 2
                            If evals >= maxEvals OrElse sw.Elapsed.TotalSeconds > maxSeconds Then
                                aborted = True
                                Exit For
                            End If
                            Dim cand = OffsetWithChannelDelta(best, ch, dir * allSteps(si))
                            If cand Is Nothing Then Continue For   ' el clamp lo dejo igual: no gastar un render
                            Dim col = MeasureSkinTintTarget(cand)
                            evals += 1
                            If col.IsEmpty Then Continue For
                            Dim err = SkinTintObjective(col, cand)
                            If err < bestErr - SkinTintEpsilon Then
                                best = cand
                                bestErr = err
                                cur = col
                                improved = True
                            End If
                        Next
                        If aborted Then Exit For
                    Next
                End While
            Next

            ' RED DE SEGURIDAD: nunca dejar el ajuste PEOR que como estaba. Sin esto, una corrida que sale
            ' mal (o que corta por presupuesto en un mal momento) deja aplicado un offset que el usuario no
            ' pidió — que es exactamente cómo se llegó al estado de QNAM saturado que motivó esta revisión.
            If bestErr > startErr Then
                best = New SkinToneQnamOffset()
                bestErr = startErr
            End If

            ' Dejar aplicado el MEJOR y presentarlo: el lazo dibuja al back buffer sin swap, así que la
            ' pantalla todavía muestra el frame anterior.
            ApplySkinTintOffsetLive(best, deferredRepaint:=True)
            SyncSkinTintSlidersFromOverlay()
            ResampleSkinTintTarget()

            ' El residuo que se REPORTA sale de los colores medidos, no del objetivo: el objetivo lleva pesos
            ' y costos, y mostrarlo como si fueran niveles sería mentira.
            Dim residual = SkinTintResidual(cur)
            Dim startResidual = SkinTintResidual(startCol)
            Dim dR = SkinTintChannel(cur, 0) - CDbl(_stSourceColor.R)
            Dim dG = SkinTintChannel(cur, 1) - CDbl(_stSourceColor.G)
            Dim dB = SkinTintChannel(cur, 2) - CDbl(_stSourceColor.B)
            Dim msg = $"Auto-calc (from the record tone, offsets 0; quality {quality}): {evals} samples in {sw.Elapsed.TotalSeconds:F1}s. Residual {startResidual:F1} → {residual:F1} RGB levels per channel; remaining delta ({dR:+0;-0;0}, {dG:+0;-0;0}, {dB:+0;-0;0})."
            If aborted Then msg &= " Stopped on budget."
            If SkinTintOffsetAtRail(best) Then
                msg &= " One or more channels ended at the limit: the tone you asked for is outside what QNAM can reach from this skin texture."
            ElseIf residual > 12.0R Then
                msg &= " Still high: the two spots probably don't have comparable lighting — pick spots with similar orientation and shading."
            End If
            LabelSkinTintStatus.Text = msg
        Catch ex As Exception
            Logger.LogLazy(Function() $"[SKINTINT] auto-calc failed for NPC 0x{HostNpcFormID:X8}: {ex.GetType().Name}: {ex.Message}")
            LabelSkinTintGate.Text = "Auto-calc failed: " & ex.Message
            ' Volver a lo que el usuario TENÍA: un fallo a mitad del lazo dejaría aplicado un candidato
            ' cualquiera. Acá sí se restaura el previo (no el cero): la corrida no llegó a concluir nada.
            ApplySkinTintOffsetLive(priorOffset, deferredRepaint:=True)
            SyncSkinTintSlidersFromOverlay()
        Finally
            _stBusy = False
            SetSkinTintControlsEnabled(True)
            Me.Cursor = prevCursor
        End Try
    End Sub

    ''' <summary>Clona <paramref name="src"/> FIJANDO el canal <paramref name="ch"/> en
    ''' <paramref name="valueUi"/> (unidades de UI, clampeado al rango del slider). La bisección necesita
    ''' posicionar un canal en un valor absoluto, no moverlo por deltas.</summary>
    Private Shared Function WithChannelUi(src As SkinToneQnamOffset, ch As Integer, valueUi As Double) As SkinToneQnamOffset
        Dim c = src.Clone()
        Dim v = ClampUi(valueUi, SkinToneQnamOffset.RgbUiScale)
        Select Case ch
            Case 0 : c.RUi = CSng(v)
            Case 1 : c.GUi = CSng(v)
            Case Else : c.BUi = CSng(v)
        End Select
        Return c
    End Function

    ''' <summary>True si el QNAM EFECTIVO (base + ajuste) quedó pegado a 0 o 255 en algún canal, o sea que el
    ''' tono pedido está fuera de lo que el QNAM puede dar sobre esta textura. Se consulta contra el valor
    ''' efectivo real (el que se va a escribir), no contra el offset.</summary>
    Private Function SkinTintOffsetAtRail(off As SkinToneQnamOffset) As Boolean
        If HostMain Is Nothing OrElse HostEditor Is Nothing Then Return False
        Dim eff = HostMain.ResolveBodySkinToneForHost(HostEditor)
        If Not eff.HasValue Then Return False
        Return eff.Value.R = 0 OrElse eff.Value.R = 255 OrElse
               eff.Value.G = 0 OrElse eff.Value.G = 255 OrElse
               eff.Value.B = 0 OrElse eff.Value.B = 255
    End Function

    ''' <summary>Canal <paramref name="ch"/> (0=R, 1=G, 2=B) de un color, como Double.</summary>
    Private Shared Function SkinTintChannel(c As Color, ch As Integer) As Double
        Select Case ch
            Case 0 : Return CDbl(c.R)
            Case 1 : Return CDbl(c.G)
            Case Else : Return CDbl(c.B)
        End Select
    End Function

    ''' <summary>Error de COLOR contra el origen, separado en luminancia y croma y pesado distinto (ver
    ''' <see cref="SkinTintLumaWeight"/> / <see cref="SkinTintChromaWeight"/>).
    ''' <para>NO es la distancia euclidiana pelada que habia antes. Esa trataba igual dos residuos muy
    ''' distintos: (-20,-20,-20), que es la MISMA piel con otra luz, y (0,0,-60), que es OTRA piel. Minimizando
    ''' la euclidiana el resolver gastaba rango extremo en un solo canal para bajar el residuo de brillo, y
    ''' terminaba con los offsets clavados en el tope y un desbalance de color peor que el que arreglaba.</para></summary>
    Private Function SkinTintColourError(col As Color) As Double
        Dim dr As Double = CDbl(col.R) - CDbl(_stSourceColor.R)
        Dim dg As Double = CDbl(col.G) - CDbl(_stSourceColor.G)
        Dim db As Double = CDbl(col.B) - CDbl(_stSourceColor.B)
        Dim meanD As Double = (dr + dg + db) / 3.0R
        Dim cr As Double = dr - meanD
        Dim cg As Double = dg - meanD
        Dim cb As Double = db - meanD
        Return SkinTintLumaWeight * 3.0R * meanD * meanD +
               SkinTintChromaWeight * (cr * cr + cg * cg + cb * cb)
    End Function

    ''' <summary>Residuo REPORTABLE: la distancia media por canal, en niveles, sin pesos ni costos. Es lo que
    ''' se le muestra al usuario -tiene que poder leerse como "cuanto me falta"-, NO lo que se minimiza.</summary>
    Private Function SkinTintResidual(col As Color) As Double
        Dim dr As Double = CDbl(col.R) - CDbl(_stSourceColor.R)
        Dim dg As Double = CDbl(col.G) - CDbl(_stSourceColor.G)
        Dim db As Double = CDbl(col.B) - CDbl(_stSourceColor.B)
        Return Math.Sqrt((dr * dr + dg * dg + db * db) / 3.0R)
    End Function

    ''' <summary>Lo que el auto-calc minimiza: error de color MAS el costo del propio ajuste. Los dos costos
    ''' son regularizadores, no restricciones: no prohiben nada, solo hacen que un ajuste grande o torcido
    ''' tenga que GANARSE su lugar con mejora real de color.</summary>
    Private Function SkinTintObjective(col As Color, off As SkinToneQnamOffset) As Double
        Dim e As Double = SkinTintColourError(col)
        If off Is Nothing Then Return e
        Dim r As Double = off.RUi, g As Double = off.GUi, b As Double = off.BUi
        ' La intensidad se lleva a unidades comparables con los canales (±100 % <-> ±255) para que su costo
        ' pese lo mismo que el de un canal.
        Dim i As Double = CDbl(off.IntensityUi) * (SkinToneQnamOffset.RgbUiScale / SkinToneQnamOffset.IntensityUiScale)
        Dim magSq As Double = r * r + g * g + b * b + i * i
        Dim meanRgb As Double = (r + g + b) / 3.0R
        Dim imbSq As Double = (r - meanRgb) * (r - meanRgb) +
                              (g - meanRgb) * (g - meanRgb) +
                              (b - meanRgb) * (b - meanRgb)
        Return e + SkinTintMagnitudeCost * magSq + SkinTintImbalanceCost * imbSq
    End Function

    ''' <summary>Valor (en unidades de UI) del canal <paramref name="ch"/> de un ajuste.</summary>
    Private Shared Function OffsetChannelUi(off As SkinToneQnamOffset, ch As Integer) As Double
        Select Case ch
            Case 0 : Return CDbl(off.RUi)
            Case 1 : Return CDbl(off.GUi)
            Case Else : Return CDbl(off.BUi)
        End Select
    End Function

    ''' <summary>Aplica el candidato, re-renderiza y devuelve el COLOR del pixel de destino. <c>Color.Empty</c>
    ''' si no se pudo muestrear. El muestreo es DETERMINISTA (<c>presentFrame:=False</c>): dibuja al back buffer
    ''' y lee de ahi, sin SwapBuffers - leer el front despues de un swap puede devolver el frame anterior y el
    ''' lazo mediria el candidato equivocado.</summary>
    Private Function MeasureSkinTintTarget(cand As SkinToneQnamOffset) As Color
        If Not ApplySkinTintOffsetLive(cand, deferredRepaint:=False) Then Return Color.Empty
        Dim ctl = HostPreview
        If ctl Is Nothing OrElse ctl.IsDisposed Then Return Color.Empty
        Dim c = ctl.ReadPixelDisplay(_stTargetPoint.X, _stTargetPoint.Y, SkinTintSampleBox(), presentFrame:=False)
        If c.IsEmpty Then Return Color.Empty
        _stLastTargetColor = c
        Return c
    End Function

    ''' <summary>Clona <paramref name="src"/> con un canal movido (en unidades de UI) y clampeado al rango del
    ''' slider. Devuelve Nothing si el clamp lo dejó idéntico — así el lazo no gasta un render en un candidato
    ''' que ya evaluó.</summary>
    Private Function OffsetWithChannelDelta(src As SkinToneQnamOffset, channel As Integer, deltaUi As Double) As SkinToneQnamOffset
        Dim c = src.Clone()
        Select Case channel
            Case 0 : c.RUi = CSng(ClampUi(CDbl(src.RUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case 1 : c.GUi = CSng(ClampUi(CDbl(src.GUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case 2 : c.BUi = CSng(ClampUi(CDbl(src.BUi) + deltaUi, SkinToneQnamOffset.RgbUiScale))
            Case Else : c.IntensityUi = CSng(ClampUi(CDbl(src.IntensityUi) + deltaUi, SkinToneQnamOffset.IntensityUiScale))
        End Select
        If c.R = src.R AndAlso c.G = src.G AndAlso c.B = src.B AndAlso c.Intensity = src.Intensity Then Return Nothing
        Return c
    End Function

    Private Shared Function ClampUi(v As Double, limit As Double) As Double
        If v < -limit Then Return -limit
        If v > limit Then Return limit
        Return v
    End Function

    ' =====================================================================
    ' Disponibilidad y estado
    ' =====================================================================

    ''' <summary>Habilita el tab sólo cuando el ajuste REALMENTE hace algo: hace falta que el skin tone del
    ''' cuerpo se derive (la raza tiene capa de skin-tone y resuelve), que es exactamente la condición bajo la
    ''' cual corre el soft-light del cuerpo. Un slider que no puede mover nada es peor que no tener slider.</summary>
    Friend Sub RefreshSkinTintAvailability()
        Dim tone As Nullable(Of Color) = Nothing
        If HostMain IsNot Nothing AndAlso HostEditor IsNot Nothing Then
            tone = HostMain.ResolveBaseSkinToneForHost(HostEditor)
        End If
        Dim available As Boolean = tone.HasValue
        SetSkinTintControlsEnabled(available)
        If Not available Then
            DisarmSkinTintPicker()
            LabelSkinTintGate.Text = "This NPC has no derivable skin tone (its race declares no skin-tone layer — synths, ghouls and robots don't have one), so QNAM does not tint the body and this adjustment would be inert."
            LabelSkinTintStatus.Text = ""
        Else
            LabelSkinTintGate.Text = ""
            UpdateSkinTintStatus(tone)
        End If
    End Sub

    Private Sub SetSkinTintControlsEnabled(enabled As Boolean)
        SliderSkinTintR.Enabled = enabled
        SliderSkinTintG.Enabled = enabled
        SliderSkinTintB.Enabled = enabled
        SliderSkinTintIntensity.Enabled = enabled
        ButtonSkinTintPickSource.Enabled = enabled
        ButtonSkinTintPickTarget.Enabled = enabled
        ButtonSkinTintReset.Enabled = enabled
        SliderSkinTintQuality.Enabled = enabled
        SliderSkinTintSampleSize.Enabled = enabled
        ButtonSkinTintAuto.Enabled = enabled AndAlso _stHasSource AndAlso _stHasTarget
    End Sub

    Private Sub UpdateSkinTintStatus(Optional baseTone As Nullable(Of Color) = Nothing)
        ButtonSkinTintAuto.Enabled = _stHasSource AndAlso _stHasTarget AndAlso Not _stBusy
        Dim effective As Nullable(Of Color) = Nothing
        If HostMain IsNot Nothing AndAlso HostEditor IsNot Nothing Then
            effective = HostMain.ResolveBodySkinToneForHost(HostEditor)
        End If
        Dim parts As New List(Of String)
        If baseTone.HasValue Then
            parts.Add($"QNAM base RGBA({baseTone.Value.R}, {baseTone.Value.G}, {baseTone.Value.B}, {baseTone.Value.A})")
        End If
        If effective.HasValue Then
            parts.Add($"effective RGBA({effective.Value.R}, {effective.Value.G}, {effective.Value.B}, {effective.Value.A})")
        End If
        If SkinTintFrameMoved() Then
            parts.Add("the camera moved since the target was picked")
        End If
        LabelSkinTintStatus.Text = String.Join("   ·   ", parts)
    End Sub

    ' =====================================================================
    ' Enganches del formulario
    ' =====================================================================

End Class
