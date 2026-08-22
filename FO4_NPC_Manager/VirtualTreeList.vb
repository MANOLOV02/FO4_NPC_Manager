Imports System.Drawing
Imports System.Windows.Forms
Imports System.Windows.Forms.VisualStyles
Imports System.Runtime.InteropServices

''' <summary>Los colores del arbol de NPC, TODOS derivados de <see cref="SystemColors"/>.
'''
''' <para>Ninguno es fijo, y no es una preferencia estetica: un color escrito a mano deja de tener
''' contraste en cuanto el usuario cambia a tema oscuro o a alto contraste, y ahi el texto se vuelve
''' ilegible sin que nadie se entere. Derivandolos, el arbol acompaña el tema del sistema solo.</para>
'''
''' <para>UNA excepcion, y esta medida: `SystemColors` tiene 33 colores y NINGUNO significa
''' error/peligro (se listaron todos para comprobarlo). El de "marcado para borrar" es el unico que no
''' se puede derivar, asi que se calcula con contraste contra el fondo VIGENTE en vez de quedar
''' clavado — ver <see cref="Peligro"/>.</para></summary>
Public Module ColoresDelArbol

    ''' <summary>Texto normal.</summary>
    Public ReadOnly Property Texto As Color
        Get
            Return SystemColors.WindowText
        End Get
    End Property

    ''' <summary>Texto apagado: los NPC que solo sirven de plantilla. `GrayText` es exactamente eso —
    ''' "presente pero no protagonista"— y el sistema ya lo ajusta por tema.</summary>
    Public ReadOnly Property Apagado As Color
        Get
            Return SystemColors.GrayText
        End Get
    End Property

    ''' <summary>Acento: las leveled lists. `HotTrack` es el azul de enlace del tema, o sea el color con
    ''' el que el sistema marca "esto es de otra clase y se puede accionar".</summary>
    Public ReadOnly Property Acento As Color
        Get
            Return SystemColors.HotTrack
        End Get
    End Property

    ''' <summary>Fondo de la multi-seleccion propia (la que no es la del sistema). Sale de mezclar el
    ''' color de resaltado con el del fondo, asi que es el MISMO tono que usa Windows para seleccionar,
    ''' apenas insinuado — y cambia con el tema.</summary>
    Public ReadOnly Property SeleccionSuave As Color
        Get
            Return Mezclar(SystemColors.Highlight, SystemColors.Window, 0.28)
        End Get
    End Property

    ''' <summary>Fondo de la fila bajo el puntero, cuando el tema no ofrece el suyo.</summary>
    Public ReadOnly Property Caliente As Color
        Get
            Return Mezclar(SystemColors.Highlight, SystemColors.Window, 0.12)
        End Get
    End Property

    ''' <summary>Lineas de jerarquia. Se mezcla el gris del tema con el fondo para que guien la vista
    ''' sin competir con el texto.</summary>
    Public ReadOnly Property Lineas As Color
        Get
            Return Mezclar(SystemColors.GrayText, SystemColors.Window, 0.55)
        End Get
    End Property

    ''' <summary>Marcado para borrar. ⛔ Es el UNICO que no se puede derivar: no hay color de error en
    ''' `SystemColors`. En vez de dejarlo clavado se elige el tono segun el fondo vigente, para que
    ''' conserve contraste tanto en tema claro como en oscuro.</summary>
    Public ReadOnly Property Peligro As Color
        Get
            Return If(FondoOscuro(), Color.FromArgb(255, 120, 120), Color.FromArgb(178, 34, 34))
        End Get
    End Property

    ''' <summary>Si el fondo de ventana del tema es oscuro. Luminancia perceptual (Rec. 601).</summary>
    Public Function FondoOscuro() As Boolean
        Dim c = SystemColors.Window
        Return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) < 128.0
    End Function

    ''' <summary>Mezcla <paramref name="frente"/> sobre <paramref name="fondo"/> con peso
    ''' <paramref name="peso"/> (0 = todo fondo, 1 = todo frente). Opaco a proposito: un color con alfa
    ''' obliga a que quien lo use sepa que hay que pintar el fondo primero.</summary>
    Public Function Mezclar(frente As Color, fondo As Color, peso As Double) As Color
        If peso < 0.0 Then peso = 0.0
        If peso > 1.0 Then peso = 1.0
        Return Color.FromArgb(
            CInt(Math.Round(frente.R * peso + fondo.R * (1.0 - peso))),
            CInt(Math.Round(frente.G * peso + fondo.G * (1.0 - peso))),
            CInt(Math.Round(frente.B * peso + fondo.B * (1.0 - peso))))
    End Function

End Module

''' <summary>Lo que quien usa el control decide sobre UNA fila: con qué fuente y de qué color va el
''' texto, y si la fila lleva un fondo propio.
''' <para>El control no conoce la ley de "gris si es sólo plantilla, azul si es una leveled list,
''' negrita si tiene cambios sin guardar, tachado si está marcado para borrar, celeste si es parte de la
''' multi-selección": esa ley es del formulario y sigue viviendo en un solo lugar. El control pregunta,
''' resuelve los estados que SÍ son suyos (selección, foco, puntero encima) y dibuja.</para></summary>
Public NotInheritable Class EstiloDeFila
    Public Property Fuente As Font
    Public Property Texto As Color
    ''' <summary>Fondo propio, o Nothing para el que corresponda por estado. Lo usa el resaltado de la
    ''' multi-selección, que no es el de Windows. La selección del sistema le gana.</summary>
    Public Property Fondo As Color?
End Class

Public NotInheritable Class PintarFilaEventArgs
    Inherits EventArgs
    Public ReadOnly Property Fila As FilaDeArbol
    Public ReadOnly Property Estilo As New EstiloDeFila
    ''' <summary>Si Windows considera seleccionada a esta fila.</summary>
    Public ReadOnly Property SeleccionadaPorElSistema As Boolean
    ''' <summary>Si el CONTROL tiene el foco. Una fila seleccionada se ve distinta cuando el foco está en
    ''' otro lado, y esa diferencia es la que le dice al usuario dónde va a ir lo que teclee.</summary>
    Public ReadOnly Property ControlConFoco As Boolean
    ''' <summary>Si el puntero está sobre esta fila.</summary>
    Public ReadOnly Property Caliente As Boolean
    Public Sub New(fila As FilaDeArbol, seleccionada As Boolean, conFoco As Boolean, caliente As Boolean)
        _Fila = fila
        _SeleccionadaPorElSistema = seleccionada
        _ControlConFoco = conFoco
        _Caliente = caliente
    End Sub
End Class

Public NotInheritable Class FilaEventArgs
    Inherits EventArgs
    Public ReadOnly Property Fila As FilaDeArbol
    Public ReadOnly Property Boton As MouseButtons
    Public ReadOnly Property Punto As Point
    Public ReadOnly Property Modificadores As Keys
    Public Sub New(fila As FilaDeArbol, boton As MouseButtons, punto As Point, modificadores As Keys)
        _Fila = fila
        _Boton = boton
        _Punto = punto
        _Modificadores = modificadores
    End Sub
End Class

''' <summary>Un árbol con sangría y glifos de expandir, dibujado sobre un <see cref="ListView"/> en modo
''' VIRTUAL.
'''
''' <para>POR QUÉ NO UN TreeView. El <c>TreeView</c> de WinForms no es virtual —no lo es en .NET 8 ni en
''' .NET 9; <c>VirtualMode</c> sólo existe en <c>ListView</c> y en <c>DataGridView</c>— así que CADA nodo
''' es un ítem Win32 con su handle. Medido en esta máquina: 7.000 nodos cuestan 972 ms de alta y 863 de
''' baja, o sea ~1,9 s cada vez que se repuebla, que es cada tecla del buscador. Acá el costo no depende
''' de cuántas filas hay sino de cuántas se VEN: cambiar el conjunto son 1,45 ms con 4.473 filas, y
''' también con 200.000.</para>
'''
''' <para>La jerarquía se arma aplanando el árbol a una lista de filas visibles (ver
''' <see cref="ModeloDeArbol"/>) y dibujando la sangría y el glifo por cuenta propia. Es el mismo diseño
''' que usan las TreeListView virtuales conocidas; lo que se evita es la dependencia, no el patrón.</para>
'''
''' <para><c>DataGridView</c> quedó descartado con número: su modo virtual tarda <b>1.930 ms</b> en
''' cambiar el conjunto contra los 1,45 de éste, y además no tiene jerarquía.</para>
'''
''' <para>Los estados visuales (selección con foco y sin foco, puntero encima, foco de teclado) se piden
''' al TEMA de Windows, no se inventan con colores fijos: así acompañan el tema del sistema y el modo de
''' alto contraste. Si los estilos visuales están apagados hay un dibujo propio de reemplazo — no un
''' hueco.</para></summary>
Public Class VirtualTreeList
    Inherits ListView

    ' --- Doble buffer nativo del common control, igual que BufferedTreeView -------------------------
    Private Const LVM_FIRST As Integer = &H1000
    Private Const LVM_SETEXTENDEDLISTVIEWSTYLE As Integer = LVM_FIRST + 54
    Private Const LVS_EX_DOUBLEBUFFER As Integer = &H10000

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    ''' <summary>Ancho de la sangría por nivel, a 96 ppp. Se escala con el DPI del control.</summary>
    Private Const SANGRIA_96 As Integer = 19
    ''' <summary>Lado de la caja del glifo a 96 ppp: es el tamaño en que Windows dibuja el suyo.</summary>
    Private Const GLIFO_96 As Integer = 16

    Private ReadOnly _modelo As New ModeloDeArbol()
    Private ReadOnly _columna As ColumnHeader

    ''' <summary>Fila bajo el puntero, o -1. Se repinta SÓLO la que entra y la que sale: invalidar el
    ''' control entero en cada movimiento del mouse es lo que hace que un hot-track se sienta pesado.</summary>
    Private _filaCaliente As Integer = -1

    ''' <summary>El DOWN del gesto en curso cayó sobre el GLIFO, así que el UP que viene también se
    ''' suprime. Sólo la usa <see cref="WndProc"/>.
    '''
    ''' <para>⛔ POR QUE NO ALCANZA CON OnMouseDown, que es lo que había antes. `ListView.WmMouseDown`
    ''' llama a `OnMouseDown` y DESPUES a `DefWndProc`, que es quien mueve la selección nativa. O sea
    ''' que un override de `OnMouseDown` no puede suprimirla: corre antes y el daño lo hace el de
    ''' después. MEDIDO con mensajes Win32 reales: con la bandera vieja, clickear el glifo de un plugin
    ''' dejaba `seleccion=[PLUGIN_Mod2.esp]` igual, con cuatro `OnSelectedIndexChanged` en el medio. La
    ''' única forma es no llamar a `MyBase.WndProc`.</para>
    '''
    ''' <para>Se apaga en el DOWN siguiente por las dos ramas, y en <see cref="OnLostFocus"/>: si el UP
    ''' se pierde —otra ventana toma la activación— la bandera quedaría encendida y se comería un UP
    ''' huérfano posterior.</para></summary>
    Private _glifoEnCurso As Boolean

    ''' <summary>Las filas seleccionadas, POR CLAVE. El control las gobierna —incluido el rango con
    ''' Shift— porque es mecanica del control, no del formulario: sin esto cada consumidor reimplementa
    ''' ancla, rango y alternado, y los tres salen distintos.
    '''
    ''' <para>⛔ POR CLAVE Y NO POR REFERENCIA. `PopulateNPCTree` hace `Limpiar()` y reconstruye TODAS
    ''' las filas, así que un conjunto de referencias apunta a objetos que ya no están en el árbol.
    ''' MEDIDO: con 3 filas elegidas, después de repoblar `Seleccionadas` devolvía 0 —filtra por
    ''' `Visibles`— mientras el conjunto crudo seguía diciendo 3, y el formulario no se enteraba de
    ''' nada. Con la clave, la selección SOBREVIVE al repoblado sin que nadie la restaure.</para>
    '''
    ''' <para>La clave NO es única: el mismo NPC cuelga de su plugin y de cada LVLN. Eso es la semántica
    ''' que la app ya tiene —`TreeViewNPCs_PintarFila` resalta por `FormID`, o sea que las dos copias ya
    ''' se pintaban juntas— así que un conjunto de claves es fiel a lo que se ve.</para></summary>
    Private ReadOnly _seleccionadas As New HashSet(Of String)(StringComparer.Ordinal)

    ''' <summary>Desde donde mide el rango un Shift+click: la ultima fila elegida SIN Shift. Es lo que
    ''' hace que Shift extienda en vez de empezar de cero.
    '''
    ''' <para>⛔ ESTA SIGUE SIENDO UNA REFERENCIA, a diferencia del conjunto de arriba, y no es un
    ''' descuido. Es estado EFIMERO y POSICIONAL de un gesto, no de identidad: por clave habría que
    ''' resolverla con `Modelo.PorClave`, que guarda sólo la PRIMERA fila de esa clave, y el rango se
    ''' mediría desde la instancia equivocada cuando el NPC cuelga también de un LVLN. Que muera al
    ''' repoblar es correcto — ver la degradación a click simple en <see cref="AplicarClick"/>.</para></summary>
    Private _ancla As FilaDeArbol

    ''' <summary>La clave de la fila enfocada. Estado DEL CONTROL, no derivado de `SelectedIndices`.
    ''' <para>⛔ Antes `Refrescar` lo leía con `FilaEnfocadaActual()?.Clave`, y eso estaba MUERTO en su
    ''' único call site: `PopulateNPCTree` hace `modelo.Limpiar()` ANTES, así que `Visibles.Count = 0` y
    ''' devolvía siempre `Nothing`. MEDIDO: tras repoblar, el foco quedaba en nada. O sea que cada tecla
    ''' del buscador perdía el foco y la preservación "por clave" que el comentario prometía no ocurría
    ''' nunca.</para>
    ''' <para>Se escribe en UN SOLO lugar, <see cref="OnSelectedIndexChanged"/>, porque todo lo que mueve
    ''' el foco pasa por ahí: mutar `SelectedIndices` dispara el evento de forma síncrona. No se borra
    ''' cuando la selección queda vacía — ese es justamente el valor que hay que reponer.</para></summary>
    Private _claveEnfocada As String

    ''' <summary>Lápices y pinceles del pintado, creados una vez.
    ''' <para>Estaban dentro del dibujo de CADA fila: con ~30 filas visibles y un repintado por cuadro de
    ''' scroll son cientos de objetos GDI por segundo, que es basura que el recolector después tiene que
    ''' barrer justo mientras el usuario arrastra la barra. Se rehacen cuando el usuario cambia el tema
    ''' (ver <see cref="AlCambiarPreferencias"/>), que es lo único que puede moverles el color.</para></summary>
    Private _lapizLineas As Pen
    Private _pincelCaliente As SolidBrush

    Private ReadOnly Property LapizLineas As Pen
        Get
            If _lapizLineas Is Nothing Then
                _lapizLineas = New Pen(ColoresDelArbol.Lineas) With {.DashStyle = Drawing2D.DashStyle.Dot}
            End If
            Return _lapizLineas
        End Get
    End Property

    Private ReadOnly Property PincelCaliente As SolidBrush
        Get
            If _pincelCaliente Is Nothing Then _pincelCaliente = New SolidBrush(ColoresDelArbol.Caliente)
            Return _pincelCaliente
        End Get
    End Property

    ''' <summary>El usuario cambió el tema o los colores del sistema: se tiran los recursos cacheados para
    ''' que se rehagan con los colores nuevos, y se repinta. Sin esto el árbol se queda con los colores
    ''' viejos hasta que se reinicie la app.</summary>
    Private Sub AlCambiarPreferencias(remitente As Object, e As Microsoft.Win32.UserPreferenceChangedEventArgs)
        If e.Category <> Microsoft.Win32.UserPreferenceCategory.Color AndAlso
           e.Category <> Microsoft.Win32.UserPreferenceCategory.VisualStyle Then Return
        SoltarRecursos()
        Invalidate()
    End Sub

    Private Sub SoltarRecursos()
        _lapizLineas?.Dispose() : _lapizLineas = Nothing
        _pincelCaliente?.Dispose() : _pincelCaliente = Nothing
    End Sub

    Public Event PintarFila As EventHandler(Of PintarFilaEventArgs)
    Public Event FilaClickeada As EventHandler(Of FilaEventArgs)
    ''' <summary>La fila con el foco cambió (mouse o teclado). Es el equivalente de AfterSelect.</summary>
    Public Event FilaEnfocada As EventHandler(Of FilaEventArgs)
    ''' <summary>Cambio el CONJUNTO seleccionado (click simple, Ctrl o Shift).</summary>
    Public Event SeleccionCambiada As EventHandler

    Public Sub New()
        View = View.Details
        HeaderStyle = ColumnHeaderStyle.None
        FullRowSelect = True
        ' Single-select como el TreeView: la multi-selección de NPC es del formulario, con su propio
        ' conjunto y su propio resaltado, igual que antes.
        MultiSelect = False
        HideSelection = False
        LabelEdit = False
        VirtualMode = True
        OwnerDraw = True
        BorderStyle = BorderStyle.FixedSingle
        VirtualListSize = 0
        _columna = Columns.Add("")
        DoubleBuffered = True
        SetStyle(ControlStyles.OptimizedDoubleBuffer Or ControlStyles.AllPaintingInWmPaint, True)
        AddHandler Microsoft.Win32.SystemEvents.UserPreferenceChanged, AddressOf AlCambiarPreferencias
    End Sub

    ''' <summary>⛔ LA BAJA DEL EVENTO NO ES OPCIONAL. `SystemEvents` es ESTÁTICO: mientras el handler
    ''' siga enganchado, el runtime conserva una referencia viva a este control y con él a todo el
    ''' formulario. Un control que se suscribe y no se da de baja no se libera nunca.</summary>
    Protected Overrides Sub Dispose(disposing As Boolean)
        If disposing Then
            RemoveHandler Microsoft.Win32.SystemEvents.UserPreferenceChanged, AddressOf AlCambiarPreferencias
            SoltarRecursos()
        End If
        MyBase.Dispose(disposing)
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        ' Doble buffer del common control. El managed no alcanza para el ListView: el scroll lo dibuja
        ' el control nativo y sin esto parpadea.
        SendMessage(Handle, LVM_SETEXTENDEDLISTVIEWSTYLE, New IntPtr(LVS_EX_DOUBLEBUFFER), New IntPtr(LVS_EX_DOUBLEBUFFER))
        AjustarColumna()
    End Sub

    Protected Overrides Sub OnSizeChanged(e As EventArgs)
        MyBase.OnSizeChanged(e)
        AjustarColumna()
    End Sub

    ''' <summary>La única columna ocupa todo el ancho útil: es lo que hace que la selección de fila
    ''' completa llegue de borde a borde y que no aparezca una barra horizontal falsa.</summary>
    Private Sub AjustarColumna()
        If _columna Is Nothing OrElse Not IsHandleCreated Then Return
        Dim ancho = ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4
        If ancho < 32 Then ancho = 32
        If _columna.Width <> ancho Then _columna.Width = ancho
    End Sub

    ''' <summary>Las filas seleccionadas, EN EL ORDEN EN QUE SE VEN.
    ''' <para>⚠️ Recorre las filas visibles, o sea que cuesta O(visibles) por acceso. Es para el momento
    ''' en que hay que ACTUAR sobre la selección (guardar, hornear, borrar), no para el pintado: leerla
    ''' desde el dibujo de cada fila convierte un repintado en O(n²). Para el pintado está
    ''' <see cref="EstaEnLaSeleccion"/>, que es O(1).</para>
    ''' <para>⚠️ La clave NO es única — el mismo NPC cuelga de su plugin y de cada LVLN — así que esto
    ''' puede devolver VARIAS filas de la misma clave. Hoy no daña: el único consumidor vuelca a un
    ''' `HashSet(Of UInteger)` de FormID. Quien necesite CONTAR NPC distintos tiene que agrupar.</para></summary>
    Public ReadOnly Property Seleccionadas As IReadOnlyList(Of FilaDeArbol)
        Get
            Return Modelo.Visibles.Where(Function(f) _seleccionadas.Contains(f.Clave)).ToList()
        End Get
    End Property

    ''' <summary>Si esa fila está elegida. O(1), y es EL predicado: el pintado lo llama por fila, así
    ''' que no puede costar O(visibles).
    ''' <para>⛔ Acá había además un `CantidadSeleccionada` que devolvía `_seleccionadas.Count`. Se
    ''' borró: contaba el conjunto CRUDO mientras `Seleccionadas` filtra por `Visibles`, así que los dos
    ''' respondían la misma pregunta con números distintos —medido, 3 contra 0 después de un repoblado—
    ''' y ninguno de los dos tenía call site de producción. Dos accesores del mismo hecho que se
    ''' contradicen es la trampa lista para el próximo consumidor.</para></summary>
    Public Function EstaEnLaSeleccion(fila As FilaDeArbol) As Boolean
        Return fila IsNot Nothing AndAlso _seleccionadas.Contains(fila.Clave)
    End Function



    ''' <summary>Aplica un click sobre una fila con sus modificadores: sin nada reemplaza la seleccion,
    ''' Ctrl alterna esa fila, Shift toma el rango desde el ancla. Es la misma semantica del explorador
    ''' de Windows, que es la que el usuario ya tiene en los dedos.</summary>
    Private Sub AplicarClick(fila As FilaDeArbol, modificadores As Keys)
        If fila Is Nothing Then Return
        Dim conCtrl = (modificadores And Keys.Control) = Keys.Control
        Dim conShift = (modificadores And Keys.Shift) = Keys.Shift
        ' ⛔ El ancla puede estar MUERTA: repoblar destruye las filas y `IndiceVisible` devuelve -1.
        ' Antes eso caía igual en el `Invalidate` + `RaiseEvent` del final, o sea que un Shift+click
        ' anunciaba un cambio que no había ocurrido y disparaba un render que nadie pidió. La salida
        ' correcta no es callar el evento sino degradar a click simple —que es lo que hace cualquier
        ' lista sin ancla— y volver a anclar acá.
        Dim iAncla = If(_ancla Is Nothing, -1, Modelo.IndiceVisible(_ancla))
        If conShift AndAlso iAncla >= 0 Then
            Dim b2 = Modelo.IndiceVisible(fila)
            If b2 >= 0 Then
                If Not conCtrl Then _seleccionadas.Clear()
                For i = Math.Min(iAncla, b2) To Math.Max(iAncla, b2)
                    _seleccionadas.Add(Modelo.Visibles(i).Clave)
                Next
            End If
        ElseIf conCtrl Then
            If Not _seleccionadas.Remove(fila.Clave) Then _seleccionadas.Add(fila.Clave)
            _ancla = fila
        Else
            ' Si ya era la única seleccionada no hay cambio que anunciar: sin esto, un click sobre la
            ' fila ya elegida dispara el evento de nuevo y con él todo lo que cuelga (re-render).
            If _seleccionadas.Count = 1 AndAlso _seleccionadas.Contains(fila.Clave) Then
                _ancla = fila
                Return
            End If
            _seleccionadas.Clear()
            _seleccionadas.Add(fila.Clave)
            _ancla = fila
        End If
        Invalidate()
        RaiseEvent SeleccionCambiada(Me, EventArgs.Empty)
    End Sub

    Public ReadOnly Property Modelo As ModeloDeArbol
        Get
            Return _modelo
        End Get
    End Property

    Private ReadOnly Property Sangria As Integer
        Get
            Return CInt(Math.Round(SANGRIA_96 * DeviceDpi / 96.0))
        End Get
    End Property

    Private ReadOnly Property LadoDelGlifo As Integer
        Get
            Return CInt(Math.Round(GLIFO_96 * DeviceDpi / 96.0))
        End Get
    End Property

    ' ============================================================================================
    ' Estado
    ' ============================================================================================

    ''' <summary>Vuelve a aplanar el modelo y se lo informa a Windows.
    ''' <para>Conserva la fila enfocada POR CLAVE y no por índice: al cambiar el filtro la misma fila
    ''' cambia de posición, y con el índice el foco saltaría a otro NPC en cada tecla.</para></summary>
    Public Sub Refrescar()
        _modelo.Aplanar()
        AplicarTamanio()
    End Sub

    ''' <summary>Publica el tamaño nuevo y restablece el foco por clave. Único sitio que toca
    ''' <c>VirtualListSize</c>: hacerlo en varios lados es como se llega a pedirle a Windows una fila que
    ''' el modelo ya no tiene.</summary>
    Private Sub AplicarTamanio()
        Dim idxFoco As Integer = -1
        BeginUpdate()
        Try
            SelectedIndices.Clear()
            VirtualListSize = _modelo.Visibles.Count
            ' El foco sale del CAMPO, no de `SelectedIndices`: para cuando esto corre desde un repoblado,
            ' el modelo ya fue vaciado y leerlo de la lista devolvería siempre Nothing.
            If _claveEnfocada IsNot Nothing Then
                ' ⛔ SE BUSCA EN LO QUE SE VE, no con `PorClave`. `PorClave` devuelve la PRIMERA fila
                ' indexada con esa clave, y el mismo NPC cuelga de su plugin Y de cada LVLN: si la
                ' primera quedó oculta, `IndiceVisible` daba -1 y el foco se perdía AUNQUE otra copia
                ' estuviera visible. MEDIDO: con el NPC visible en la fila 3, colapsar el grupo de la
                ' otra copia dejaba `SelectedIndices(0) = -1`.
                idxFoco = _modelo.Visibles.FindIndex(Function(f) String.Equals(f.Clave, _claveEnfocada, StringComparison.Ordinal))
                If idxFoco >= 0 Then SelectedIndices.Add(idxFoco)
            End If
        Finally
            EndUpdate()
        End Try

        ' ⛔ Y SE REPONE LA POSICIÓN DEL SCROLL. Cambiar `VirtualListSize` NO la corrige: Windows
        ' conserva el desplazamiento anterior, así que si la lista se acortó -lo que pasa en CADA tecla
        ' del buscador- el scroll queda apuntando más allá del final y arriba queda una franja EN BLANCO,
        ' que recién se llena cuando algo fuerza el repintado. `Invalidate()` no alcanza: el área está
        ' bien pintada, lo que está mal es DÓNDE mira la vista.
        ' Se lleva a la fila enfocada, que es lo que el usuario espera seguir viendo; si el filtro la dejo
        ' afuera, al principio.
        If VirtualListSize > 0 Then
            If idxFoco >= 0 Then
                EnsureVisible(idxFoco)
            Else
                ' Sin fila enfocada en la lista nueva, la vista vuelve al principio. ⛔ `EnsureVisible(0)`
                ' SOLO no alcanza: si la fila 0 quedo dentro del area cliente -aunque sea pegada al borde
                ' de abajo, con toda la franja de arriba EN BLANCO- Windows concluye que ya esta visible y
                ' no mueve nada. MEDIDO: tras acortar la lista, `GetItemRect(0).Top` daba 532 con el
                ' cliente de 559 px de alto, que es exactamente la franja blanca que se ve al filtrar.
                ' Ir al FINAL y volver al principio fuerza a Windows a recalcular el desplazamiento; es
                ' API publica y no depende de la semantica del dy de LVM_SCROLL, que cambia con la vista.
                ' El desplazamiento se lee del SISTEMA y se deshace exactamente. `EnsureVisible(0)` no
                ' sirve cuando el contenido nuevo ENTRA entero -- Windows concluye que la fila 0 ya esta
                ' visible aunque este dibujada abajo de una franja en blanco- y `GetItemRect`/`GetItemAt`
                ' no son fiables en modo virtual, asi que tampoco se pueden usar para decidir.
                Dim pos = PosicionDeScroll()
                If pos > 0 Then SendMessage(Handle, LVM_SCROLL, IntPtr.Zero, New IntPtr(-pos))
            End If
        End If

        _filaCaliente = -1
        ' ⛔ `Invalidate()` SOLO MARCA el area como sucia: el repintado queda para cuando Windows
        ' procese el WM_PAINT, y en un ListView OWNER-DRAW en modo virtual eso puede no pasar hasta que
        ' algo mas lo fuerce. Sintoma reportado por el usuario: al escribir en el filtro queda una franja
        ' EN BLANCO arriba "y se llena cuando lo muevo" -- mover la lista es justamente lo que fuerza el
        ' WM_PAINT que faltaba. `Update()` lo emite ya.
        ' ⚠️ No reproducido en el arnes: el modelo sintetico del gate se auto-corrige (falsificado, G5b
        ' queda verde igual sin esto). Es un arreglo dirigido al sintoma descripto, no probado por gate.
        Invalidate()
        Update()
    End Sub

    ''' <summary>Si esa fila es la seleccionada. Ver el comentario de OnDrawItem: `e.State` no sirve en
    ''' modo virtual.</summary>
    Private Function EstaSeleccionada(indice As Integer) As Boolean
        Return SelectedIndices IsNot Nothing AndAlso SelectedIndices.Count > 0 AndAlso SelectedIndices(0) = indice
    End Function

    Public Function FilaEnfocadaActual() As FilaDeArbol
        If SelectedIndices Is Nothing OrElse SelectedIndices.Count = 0 Then Return Nothing
        Dim i = SelectedIndices(0)
        If i < 0 OrElse i >= _modelo.Visibles.Count Then Return Nothing
        Return _modelo.Visibles(i)
    End Function

    ''' <summary>Enfoca la fila de esa clave, abriendo lo que haga falta para que se vea, y la trae a la
    ''' vista. Reemplaza al <c>Nodes.Find(…, searchAllChildren:=True)</c> + <c>EnsureVisible</c> +
    ''' <c>SelectedNode</c> de antes.</summary>
    Public Function EnfocarClave(clave As String) As Boolean
        Dim fila = _modelo.PorClave(clave)
        If fila Is Nothing Then Return False
        If _modelo.AbrirAncestros(fila) Then
            _modelo.Aplanar()
            AplicarTamanio()
        End If
        Dim idx = _modelo.IndiceVisible(fila)
        If idx < 0 Then Return False
        SelectedIndices.Clear()
        SelectedIndices.Add(idx)
        EnsureVisible(idx)
        Return True
    End Function

    Public Sub AlternarFila(fila As FilaDeArbol)
        If Not _modelo.Alternar(fila) Then Return
        _modelo.Aplanar()
        AplicarTamanio()
    End Sub

    ' ============================================================================================
    ' Datos: Windows pide sólo las filas que se ven
    ' ============================================================================================
    Protected Overrides Sub OnRetrieveVirtualItem(e As RetrieveVirtualItemEventArgs)
        ' El texto va VACÍO a propósito: lo dibuja OnDrawItem con su sangría. Si se lo damos acá, el
        ' control lo pinta además por su cuenta y el texto se ve dos veces, corrido.
        e.Item = New ListViewItem(String.Empty)
        MyBase.OnRetrieveVirtualItem(e)
    End Sub

    ''' <summary>Búsqueda por teclado: escribir "ra" salta al primer "Raider". Sin esto, en modo virtual
    ''' la tecla no hace nada y se pierde una forma de navegar que el TreeView sí daba.</summary>
    Protected Overrides Sub OnSearchForVirtualItem(e As SearchForVirtualItemEventArgs)
        If Not String.IsNullOrEmpty(e.Text) AndAlso _modelo.Visibles.Count > 0 Then
            Dim desde = Math.Max(0, e.StartIndex)
            For salto = 0 To _modelo.Visibles.Count - 1
                Dim i = (desde + salto) Mod _modelo.Visibles.Count
                If _modelo.Visibles(i).Texto.StartsWith(e.Text, StringComparison.CurrentCultureIgnoreCase) Then
                    e.Index = i
                    Exit For
                End If
            Next
        End If
        MyBase.OnSearchForVirtualItem(e)
    End Sub

    ' ============================================================================================
    ' Pintado
    ' ============================================================================================
    Protected Overrides Sub OnDrawColumnHeader(e As DrawListViewColumnHeaderEventArgs)
        e.DrawDefault = False   ' no hay cabecera visible; dibujarla por defecto deja una línea
    End Sub

    Protected Overrides Sub OnDrawSubItem(e As DrawListViewSubItemEventArgs)
        e.DrawDefault = False   ' todo se pinta en OnDrawItem, de una sola vez por fila
    End Sub

    Protected Overrides Sub OnDrawItem(e As DrawListViewItemEventArgs)
        If e.ItemIndex < 0 OrElse e.ItemIndex >= _modelo.Visibles.Count Then
            e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds)
            Return
        End If
        Dim fila = _modelo.Visibles(e.ItemIndex)
        ' ⛔ NO se usa `e.State`. En modo virtual llega con el bit de selección puesto para TODAS las
        ' filas: el gate de render lo mostró de una — la lista entera pintada de azul. La verdad la tiene
        ' el conjunto del control, que además soporta Ctrl y Shift.
        Dim seleccionada = EstaEnLaSeleccion(fila)
        Dim esLaDelFoco = EstaSeleccionada(e.ItemIndex)
        Dim conFoco = Focused
        Dim caliente = (e.ItemIndex = _filaCaliente)

        Dim args As New PintarFilaEventArgs(fila, seleccionada, conFoco, caliente)
        args.Estilo.Fuente = Font
        args.Estilo.Texto = ForeColor
        RaiseEvent PintarFila(Me, args)

        Dim fuente = If(args.Estilo.Fuente, Font)
        Dim colorTexto = args.Estilo.Texto

        ' --- fondo, por prioridad de estado ---------------------------------------------------
        ' La selección del sistema le gana al fondo propio: es la misma precedencia que tenía el
        ' owner-draw del árbol viejo (primero el resaltado, después el celeste de la multi-selección).
        If seleccionada Then
            colorTexto = PintarSeleccion(e.Graphics, e.Bounds, conFoco)
        ElseIf args.Estilo.Fondo.HasValue Then
            Using b As New SolidBrush(args.Estilo.Fondo.Value)
                e.Graphics.FillRectangle(b, e.Bounds)
            End Using
        ElseIf caliente Then
            PintarCaliente(e.Graphics, e.Bounds)
        Else
            e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds)
        End If

        Dim izquierda = e.Bounds.Left + fila.Nivel * Sangria

        ' --- líneas de jerarquía -----------------------------------------------------------------
        DibujarLineas(e.Graphics, e.Bounds, fila)

        ' --- glifo ------------------------------------------------------------------------------
        If fila.TieneHijos Then
            DibujarGlifo(e.Graphics, New Rectangle(izquierda, e.Bounds.Top, Sangria, e.Bounds.Height),
                         fila.Expandida, caliente)
        End If

        ' --- texto ------------------------------------------------------------------------------
        Dim xTexto = izquierda + Sangria
        Dim rectTexto = New Rectangle(xTexto, e.Bounds.Top, Math.Max(0, e.Bounds.Right - xTexto), e.Bounds.Height)
        TextRenderer.DrawText(e.Graphics, fila.Texto, fuente, rectTexto, colorTexto,
                              TextFormatFlags.VerticalCenter Or TextFormatFlags.EndEllipsis Or
                              TextFormatFlags.NoPrefix Or TextFormatFlags.GlyphOverhangPadding)

        ' --- foco de teclado --------------------------------------------------------------------
        If esLaDelFoco AndAlso conFoco Then
            ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds)
        End If
    End Sub

    ''' <summary>Fondo de una fila seleccionada, y el color de texto que le corresponde. Devuelve ese
    ''' color para que el llamador no tenga que repetir la decisión.
    ''' <para>CON foco y SIN foco son dos estados distintos del tema, no el mismo con otra opacidad: sin
    ''' foco Windows usa un resaltado apagado, y es lo que le dice al usuario que las teclas van a ir a
    ''' otro control.</para></summary>
    ''' <summary>Fondo de una fila seleccionada, y el color de texto que le corresponde.
    ''' <para>⛔ LOS COLORES DEL SISTEMA VIENEN EN PARES Y SE USAN EN PARES: si se pinta
    ''' <c>Highlight</c>, el texto va en <c>HighlightText</c>; si se pinta <c>Control</c>, va en
    ''' <c>ControlText</c>. Es lo que hacen los controles de Windows, y es lo que garantiza contraste en
    ''' CUALQUIER tema —incluido alto contraste— sin que nadie tenga que elegir un color.</para>
    ''' <para>Acá NO se usa el tema visual para el fondo, y es a propósito: dibujar el fondo del tema y
    ''' después decidir el texto por cuenta propia fue exactamente el defecto que dejó texto oscuro sobre
    ''' azul saturado. El par del sistema no puede desparejarse.</para>
    ''' <para>Con foco y sin foco son dos pares distintos: es lo que le dice al usuario a qué control van
    ''' a ir las teclas.</para></summary>
    Private Shared Function PintarSeleccion(g As Graphics, r As Rectangle, conFoco As Boolean) As Color
        If conFoco Then
            g.FillRectangle(SystemBrushes.Highlight, r)
            Return SystemColors.HighlightText
        End If
        g.FillRectangle(SystemBrushes.Control, r)
        Return SystemColors.ControlText
    End Function

    Private Sub PintarCaliente(g As Graphics, r As Rectangle)
        If Application.RenderWithVisualStyles AndAlso
           DibujarConTema(VisualStyleElement.ListView.Item.Hot, g, r) Then Return
        ' Sin tema: el mismo tono de resaltado del sistema, apenas insinuado. Ver ColoresDelArbol.
        g.FillRectangle(PincelCaliente, r)
    End Sub

    ''' <summary>Dibuja un elemento del tema si existe. Devuelve False cuando no está definido, para que
    ''' el llamador use su reemplazo: un tema incompleto no puede dejar la fila sin fondo.</summary>
    Private Shared Function DibujarConTema(elemento As VisualStyleElement, g As Graphics, r As Rectangle) As Boolean
        Try
            If Not VisualStyleRenderer.IsElementDefined(elemento) Then Return False
            Dim rr As New VisualStyleRenderer(elemento)
            rr.DrawBackground(g, r)
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Las líneas que muestran de quién cuelga cada fila.
    ''' <para>Por cada nivel por encima del suyo se dibuja una vertical SÓLO si ese ancestro todavía
    ''' tiene hermanos debajo (<see cref="FilaDeArbol.EsUltimoHermano"/>): así la línea se corta en la
    ''' última rama en vez de seguir hasta el final de la lista, que es lo que la haría ilegible.</para>
    ''' <para>La del nivel propio baja hasta el medio y sigue hasta abajo sólo si no es el último
    ''' hermano, y de ahí sale la horizontal hacia el glifo. Es el mismo trazado que dibuja un TreeView
    ''' con `ShowLines`.</para></summary>
    Private Sub DibujarLineas(g As Graphics, limites As Rectangle, fila As FilaDeArbol)
        If fila.Nivel <= 0 Then Return
        Dim medioY = limites.Top + limites.Height \ 2
        Dim lapiz = LapizLineas


        ' verticales de los ancestros que aún tienen hermanos debajo
        Dim ancestro = fila.Padre
        Dim nivel = fila.Nivel - 1
        While ancestro IsNot Nothing AndAlso nivel >= 0
            If Not ancestro.EsUltimoHermano Then
                Dim x = limites.Left + nivel * Sangria + Sangria \ 2
                g.DrawLine(lapiz, x, limites.Top, x, limites.Bottom)
            End If
            ancestro = ancestro.Padre
            nivel -= 1
        End While

        ' el codo de la fila misma
        Dim cx = limites.Left + fila.Nivel * Sangria + Sangria \ 2
        Dim hasta = If(fila.EsUltimoHermano, medioY, limites.Bottom)
        g.DrawLine(lapiz, cx, limites.Top, cx, hasta)
        g.DrawLine(lapiz, cx, medioY, limites.Left + (fila.Nivel + 1) * Sangria, medioY)
    End Sub

    ''' <summary>El triangulito de expandir. Se pide al tema para que se vea como el del explorador (y
    ''' acompañe alto contraste); con el puntero encima se usa la variante resaltada, que es lo que hace
    ''' que el control se sienta vivo.</summary>
    Private Sub DibujarGlifo(g As Graphics, celda As Rectangle, expandida As Boolean, caliente As Boolean)
        Dim lado = LadoDelGlifo
        Dim caja As New Rectangle(celda.Left + (celda.Width - lado) \ 2,
                                  celda.Top + (celda.Height - lado) \ 2, lado, lado)
        If Application.RenderWithVisualStyles Then
            If caliente Then
                ' Variante "hot" del explorador moderno. No está definida en todos los temas, de ahí la
                ' cadena de reemplazos en vez de una sola llamada.
                Dim hot = If(expandida, VisualStyleElement.TreeView.Glyph.Opened,
                                        VisualStyleElement.TreeView.Glyph.Closed)
                If DibujarConTema(hot, g, caja) Then Return
            End If
            Dim normal = If(expandida, VisualStyleElement.TreeView.Glyph.Opened,
                                       VisualStyleElement.TreeView.Glyph.Closed)
            If DibujarConTema(normal, g, caja) Then Return
        End If
        DibujarGlifoSimple(g, caja, expandida)
    End Sub

    Private Shared Sub DibujarGlifoSimple(g As Graphics, caja As Rectangle, expandida As Boolean)
        Dim r = Rectangle.Inflate(caja, -3, -3)
        Using p As New Pen(SystemColors.ControlDarkDark)
            g.DrawRectangle(p, r)
            Dim medioY = r.Top + r.Height \ 2
            g.DrawLine(p, r.Left + 2, medioY, r.Right - 2, medioY)
            If Not expandida Then
                Dim medioX = r.Left + r.Width \ 2
                g.DrawLine(p, medioX, r.Top + 2, medioX, r.Bottom - 2)
            End If
        End Using
    End Sub

    ' ============================================================================================
    ' Interacción
    ' ============================================================================================

    Public Function FilaEn(punto As Point) As FilaDeArbol
        Dim i = IndiceEn(punto)
        If i < 0 Then Return Nothing
        Return _modelo.Visibles(i)
    End Function

    Private Function IndiceEn(punto As Point) As Integer
        Dim it = GetItemAt(punto.X, punto.Y)
        If it Is Nothing Then Return -1
        If it.Index < 0 OrElse it.Index >= _modelo.Visibles.Count Then Return -1
        Return it.Index
    End Function

    ''' <summary>Si el punto cae sobre el glifo de esa fila. Usa la MISMA aritmética que el pintado, así
    ''' que lo que se ve y lo que responde al click no se pueden separar.</summary>
    Private Function EsElGlifo(fila As FilaDeArbol, punto As Point) As Boolean
        If fila Is Nothing OrElse Not fila.TieneHijos Then Return False
        Dim izquierda = fila.Nivel * Sangria
        Return punto.X >= izquierda AndAlso punto.X < izquierda + Sangria
    End Function

    Protected Overrides Sub OnMouseMove(e As MouseEventArgs)
        MyBase.OnMouseMove(e)
        CambiarFilaCaliente(IndiceEn(e.Location))
    End Sub

    Protected Overrides Sub OnMouseLeave(e As EventArgs)
        MyBase.OnMouseLeave(e)
        CambiarFilaCaliente(-1)
    End Sub

    ''' <summary>Repinta SÓLO la fila que deja de estar caliente y la que pasa a estarlo.</summary>
    Private Sub CambiarFilaCaliente(indice As Integer)
        If indice = _filaCaliente Then Return
        Dim anterior = _filaCaliente
        _filaCaliente = indice
        InvalidarFila(anterior)
        InvalidarFila(indice)
    End Sub

    ''' <summary>Repinta una sola fila.
    ''' <para>`GetItemRect` y NO `Items(i).Bounds`: en modo virtual, tocar `Items` MATERIALIZA el ítem
    ''' —dispara `RetrieveVirtualItem`— y esto corre dos veces por cada movimiento del mouse. La
    ''' geometría de una fila la sabe el control sin fabricar nada.</para></summary>
    Private Sub InvalidarFila(indice As Integer)
        If indice < 0 OrElse indice >= VirtualListSize Then Return
        Try
            Invalidate(GetItemRect(indice))
        Catch ex As ArgumentOutOfRangeException
            ' El tamaño virtual cambió entre el cálculo y el repintado: no hay nada que invalidar.
        End Try
    End Sub

    Protected Overrides Sub OnMouseDown(e As MouseEventArgs)
        Dim indice = IndiceEn(e.Location)
        Dim fila = If(indice >= 0, _modelo.Visibles(indice), Nothing)

        ' ⛔ ACÁ NO HAY RAMA DE GLIFO, y no es un olvido: el click en el glifo lo resuelve `WndProc` y
        ' este handler NO CORRE para ese gesto. La rama que había acá no servía —`DefWndProc` corre
        ' después y seleccionaba igual— y tenerla en los dos lados dejaba la misma ley escrita dos veces.

        ' El botón derecho SELECCIONA primero. Es lo que espera cualquiera: el menú contextual tiene que
        ' actuar sobre la fila que se clickeó, no sobre la que quedó seleccionada antes.
        If fila IsNot Nothing AndAlso e.Button = MouseButtons.Right Then
            SelectedIndices.Clear()
            SelectedIndices.Add(indice)
        End If

        MyBase.OnMouseDown(e)
    End Sub

    Protected Overrides Sub OnMouseUp(e As MouseEventArgs)
        MyBase.OnMouseUp(e)
        ' El UP del gesto de glifo lo suprime `WndProc`, así que acá no llega y no hace falta guarda.
        Dim fila = FilaEn(e.Location)
        If fila Is Nothing Then Return
        ' El botón derecho no rearma la selección si la fila YA estaba dentro: es lo que permite abrir
        ' el menú sobre una multi-selección sin perderla.
        If e.Button = MouseButtons.Left OrElse Not EstaEnLaSeleccion(fila) Then
            AplicarClick(fila, ModifierKeys)
        End If
        RaiseEvent FilaClickeada(Me, New FilaEventArgs(fila, e.Button, e.Location, ModifierKeys))
    End Sub

    Protected Overrides Sub OnDoubleClick(e As EventArgs)
        MyBase.OnDoubleClick(e)
        ' ⛔ Un doble click sobre el GLIFO no es un doble click de fila: acá se alterna
        ' `FilaEnfocadaActual()` —la fila SELECCIONADA— y sin distinguirlos, doblecliquear el glifo del
        ' plugin B colapsaba el plugin A. Ya no hace falta guarda: `WndProc` suprime el WM_LBUTTONDBLCLK
        ' del glifo y este handler NO CORRE para ese gesto.
        Dim fila = FilaEnfocadaActual()
        If fila IsNot Nothing AndAlso fila.TieneHijos Then AlternarFila(fila)
    End Sub

    ''' <summary>Izquierda cierra o sube al padre; derecha abre o baja al primer hijo. Es la navegación
    ''' que da un árbol y que se perdería si esto fuera una lista pelada.</summary>
    Protected Overrides Sub OnKeyDown(e As KeyEventArgs)
        Dim fila = FilaEnfocadaActual()
        If fila IsNot Nothing Then
            If e.KeyCode = Keys.Left Then
                If fila.TieneHijos AndAlso fila.Expandida Then
                    AlternarFila(fila)
                ElseIf fila.Padre IsNot Nothing Then
                    EnfocarFila(fila.Padre)
                End If
                e.Handled = True
                Return
            ElseIf e.KeyCode = Keys.Right Then
                If fila.TieneHijos Then
                    If Not fila.Expandida Then
                        AlternarFila(fila)
                    Else
                        EnfocarFila(fila.Hijos(0))
                    End If
                End If
                e.Handled = True
                Return
            End If
        End If
        MyBase.OnKeyDown(e)
    End Sub

    Private Sub EnfocarFila(fila As FilaDeArbol)
        Dim idx = _modelo.IndiceVisible(fila)
        If idx < 0 Then Return
        SelectedIndices.Clear()
        SelectedIndices.Add(idx)
        EnsureVisible(idx)
    End Sub

    ' El resaltado de la selección cambia con el foco, así que hay que repintar cuando entra y sale.
    Protected Overrides Sub OnGotFocus(e As EventArgs)
        MyBase.OnGotFocus(e)
        Invalidate()
    End Sub

    Protected Overrides Sub OnLostFocus(e As EventArgs)
        MyBase.OnLostFocus(e)
        ' Si el gesto del glifo se corta a la mitad —otra ventana toma la activación antes del UP— la
        ' bandera quedaría encendida y se comería el próximo UP huérfano. Acá se cierra el gesto.
        _glifoEnCurso = False
        Invalidate()
    End Sub

    ''' <summary>Desplaza el contenido del ListView. Es la unica forma de reponer el scroll: `EnsureVisible`
    ''' no sirve cuando la fila 0 YA esta dentro del area cliente -aunque sea abajo de todo- porque
    ''' Windows concluye que no hay nada que hacer.</summary>
    Private Const LVM_SCROLL As Integer = &H1014

    <Runtime.InteropServices.StructLayout(Runtime.InteropServices.LayoutKind.Sequential)>
    Private Structure SCROLLINFO
        Public cbSize As Integer
        Public fMask As Integer
        Public nMin As Integer
        Public nMax As Integer
        Public nPage As Integer
        Public nPos As Integer
        Public nTrackPos As Integer
    End Structure

    <Runtime.InteropServices.DllImport("user32.dll")>
    Private Shared Function GetScrollInfo(hWnd As IntPtr, nBar As Integer, ByRef si As SCROLLINFO) As Boolean
    End Function

    Private Const SB_VERT As Integer = 1
    Private Const SIF_POS As Integer = &H4

    ''' <summary>Posicion REAL del scroll vertical, la que reporta el sistema. `GetItemRect` y
    ''' `GetItemAt` no sirven en modo virtual: devuelven valores que no corresponden a lo que se ve.</summary>
    Friend Function PosicionDeScroll() As Integer
        Dim si As New SCROLLINFO()
        si.cbSize = Runtime.InteropServices.Marshal.SizeOf(GetType(SCROLLINFO))
        si.fMask = SIF_POS
        If Not GetScrollInfo(Handle, SB_VERT, si) Then Return 0
        Return si.nPos
    End Function

    Private Const WM_LBUTTONDOWN As Integer = &H201
    Private Const WM_LBUTTONUP As Integer = &H202
    Private Const WM_LBUTTONDBLCLK As Integer = &H203

    ''' <summary>Desempaqueta el punto de un `LParam` de mouse, con EXTENSION DE SIGNO.
    ''' <para>⛔ NO usar `LParam.ToInt32()`: en x64 el `IntPtr` es de 64 bits y con una coordenada
    ''' negativa el DWORD empaquetado vale más que `Int32.MaxValue` ⇒ `OverflowException` en medio del
    ''' bombeo de mensajes. Y `And &amp;HFFFF` a secas devuelve 65532 donde el valor real es -4. Esto es el
    ''' equivalente de `GET_X_LPARAM`/`GET_Y_LPARAM` de la API.</para>
    ''' <para>⛔ Tampoco `Cursor.Position` + `PointToClient`: para un mensaje ENCOLADO el cursor pudo
    ''' haberse movido entre el post y el despacho, y además dejaría el camino intesteable — el gate
    ''' postea el click en una coordenada con el cursor físico en cualquier lado.</para></summary>
    Private Shared Function PuntoDeLParam(lp As IntPtr) As Point
        Dim v As Long = lp.ToInt64()
        Return New Point(CInt(((v And &HFFFFL) Xor &H8000L) - &H8000L),
                         CInt((((v >> 16) And &HFFFFL) Xor &H8000L) - &H8000L))
    End Function

    ''' <summary>Intercepta el click sobre el GLIFO antes de que Windows lo convierta en una selección.
    '''
    ''' <para>⛔ ES EL UNICO LUGAR DONDE SE PUEDE. `ListView.WmMouseDown` llama a `OnMouseDown` y
    ''' DESPUES a `DefWndProc`; la selección la mueve el segundo, así que ningún override la puede
    ''' suprimir. La única salida es no llamar a `MyBase.WndProc`. MEDIDO con `PostMessage` real: sin
    ''' esto, clickear el glifo de un plugin dejaba `seleccion=[PLUGIN_Mod2.esp]`; con esto, el grupo
    ''' colapsa (8 filas visibles → 5) y la selección no se mueve.</para>
    '''
    ''' <para>Se suprime el gesto COMPLETO. El UP también, porque si no `OnMouseUp` llama a
    ''' `AplicarClick` y elige igual. Y el DBLCLK, porque el doble click rápido sobre el glifo manda
    ''' DOWN/UP/DBLCLK/UP y el tercero volvería a seleccionar. ⛔ El DBLCLK se suprime pero NO alterna:
    ''' alternar dos veces abriría y cerraría, y el usuario no vería nada.</para>
    '''
    ''' <para>El `Focus()` es necesario: al no pasar por `DefWndProc`, el control no toma el foco de
    ''' teclado por su cuenta y las flechas dejarían de andar después de expandir con el mouse.</para>
    '''
    ''' <para>`AlternarFila` corre RE-ENTRANTE acá adentro (`BeginUpdate`, `LVM_SETITEMCOUNT` y
    ''' `LVM_SETITEMSTATE` son mensajes síncronos a esta misma ventana). Está medido que no traba y que
    ''' produce exactamente dos `OnSelectedIndexChanged`, los de `AplicarTamanio`.</para></summary>
    Protected Overrides Sub WndProc(ByRef m As Message)
        Select Case m.Msg
            Case WM_LBUTTONDOWN, WM_LBUTTONDBLCLK
                Dim p = PuntoDeLParam(m.LParam)
                Dim fila = FilaEn(p)
                If fila IsNot Nothing AndAlso EsElGlifo(fila, p) Then
                    _glifoEnCurso = True
                    If Not Focused Then Focus()
                    If m.Msg = WM_LBUTTONDOWN Then AlternarFila(fila)
                    Return
                End If
                _glifoEnCurso = False
            Case WM_LBUTTONUP
                If _glifoEnCurso Then
                    _glifoEnCurso = False
                    Return
                End If
        End Select
        MyBase.WndProc(m)
    End Sub

    Protected Overrides Sub OnSelectedIndexChanged(e As EventArgs)
        MyBase.OnSelectedIndexChanged(e)
        Dim fila = FilaEnfocadaActual()
        If fila Is Nothing Then Return
        ' El foco es estado del control y se recuerda por CLAVE: es lo que `Refrescar` repone después de
        ' un repoblado. Se escribe acá y en ningún otro lado — todo lo que mueve el foco muta
        ' `SelectedIndices` y por lo tanto pasa por este evento, de forma síncrona.
        ' ⛔ PERO NO CON Ctrl/Shift APRETADO. Con Ctrl el click DESELECCIONA, y escribir la clave igual
        ' dejaba el foco APUNTANDO A ALGO QUE YA NO ESTÁ en el conjunto. El siguiente `AplicarTamanio` lo
        ' reponía como foco, `Not EstaEnLaSeleccion(fila)` daba True y la multi-selección se reemplazaba
        ' por la fila que el usuario acababa de SACAR. MEDIDO: elegir 1,2,3,4 → Ctrl+click de nuevo en la
        ' 4 → colapsar otro grupo ⇒ quedaba {4} sola, y "Save Selected" habría escrito ese NPC y no los
        ' otros tres. Es la misma guarda que usa el colapso, tres líneas más abajo, por el mismo motivo.
        If (ModifierKeys And (Keys.Control Or Keys.Shift)) = 0 Then _claveEnfocada = fila.Clave
        ' LA NAVEGACIÓN POR TECLADO TAMBIÉN MUEVE LA SELECCIÓN. Las flechas cambian el foco sin pasar
        ' por el click, y en cualquier lista la selección sigue al foco. Con Ctrl o Shift apretados no:
        ' ahí manda el click, que ya calculó el conjunto (alternar o rango).
        If (ModifierKeys And (Keys.Control Or Keys.Shift)) = 0 Then
            ' ⛔ LA CONDICION ES "el foco se fue a algo que NO estaba elegido", no "hay exactamente una
            ' elegida". Con la vieja, CUALQUIER expandir/colapsar destruía la multi-selección:
            ' `AplicarTamanio` hace `SelectedIndices.Add`, Windows dispara este evento, y con 3 filas
            ' elegidas `Count = 1` daba False ⇒ colapsaba a una. MEDIDO: 3 filas → colapsar OTRO grupo
            ' → quedaba 1. Y eso no es cosmético: `_selectedNpcFormIDs` decide qué NPC se escriben al
            ' ESP en el scope "Selected", así que "Save Selected" habría escrito 1 en vez de 3.
            If Not EstaEnLaSeleccion(fila) Then
                _seleccionadas.Clear()
                _seleccionadas.Add(fila.Clave)
                _ancla = fila
                Invalidate()
                RaiseEvent SeleccionCambiada(Me, EventArgs.Empty)
            End If
        End If
        RaiseEvent FilaEnfocada(Me, New FilaEventArgs(fila, MouseButtons.None, Point.Empty, ModifierKeys))
    End Sub

End Class
