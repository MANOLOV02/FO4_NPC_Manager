Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>El editor de un conjunto de texturas (<c>TXST</c>): nuevo o override. Existe porque un head
''' part propio necesita su propia textura — el <c>TNAM</c> de un HDPT apunta a un TXST y, en los ojos,
''' <b>ese TXST ES la diffuse</b>.
'''
''' <para>⛔ <b>LAS OCHO RANURAS SE LLAMAN DISTINTO EN CADA JUEGO, y sólo CINCO están en la interfaz
''' común.</b> Medido sobre las vistas generadas: <c>ITxst</c> expone Diffuse, Normal/Gloss,
''' Environment, Height y Multilayer; las otras tres viven en la clase concreta y NO son las mismas —
''' Fallout 4 tiene <i>Wrinkles</i>, <i>Glow</i> y <i>Smooth Spec</i>, y Skyrim <i>Environment
''' Mask/Subsurface Tint</i>, <i>Glow/Detail Map</i> y <i>Backlight Mask/Specular</i>. Un editor escrito
''' sólo contra la interfaz deja tres ranuras sin tocar, y en FO4 una de ellas (<c>TX07</c>, Smooth
''' Spec) la usan 716 de los 1.000 TXST del corpus.</para>
'''
''' <para>⛔ <b><c>OBND</c> no se edita: se muestra.</b> Está en el 100 % del corpus (2.579 de 2.579), y
''' su valor no se inventa — un clon copia el de su plantilla y uno en blanco lleva ceros, que es un
''' valor TRANSCRITO del archivo (486 de 1.000 en FO4, 1.483 de 1.579 en Skyrim).</para>
'''
''' <para>⛔ <b><c>DODT</c> (decal) se PRESERVA sin editarse</b> — 317 de 1.000 en FO4 lo traen, ninguno
''' de los que necesita un head part. Viene gratis en la copia del árbol; el editor dice que está y no
''' lo toca, que es distinto de perderlo.</para></summary>
Public Class TextureSetEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _game As Canon.WbGame
    Private _draft As TxstDraft
    Private _toma As TomaDeBorrador(Of TxstDraft)
    Private _cargando As Boolean
    Private ReadOnly _cajas As New Dictionary(Of String, TextBox)

    Public ReadOnly Property ResultTxstFormID As UInteger
        Get
            Return If(_draft Is Nothing, 0UI, _draft.FormID)
        End Get
    End Property

    ''' <param name="draft">El borrador a editar. Nothing ⇒ se crea uno nuevo, en blanco.</param>
    ''' <summary>La toma se rechazó porque ese borrador ya está abierto en otro editor. Lo mira el
    ''' llamador: cerrar un formulario a medio construir dejaría al <c>ShowDialog</c> devolviendo un
    ''' resultado que nadie decidió.</summary>
    Friend ReadOnly Property TomaRechazada As Boolean
        Get
            Return _tomaRechazada
        End Get
    End Property
    Private _tomaRechazada As Boolean

    Public Sub New(mainForm As MainForm, draft As TxstDraft)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        InitializeComponent()
        _mainForm = mainForm
        _game = If(Config_App.Current IsNot Nothing AndAlso
                   Config_App.Current.Game = Config_App.Game_Enum.Skyrim,
                   Canon.WbGame.Skyrim, Canon.WbGame.Fallout4)
        _toma = New TomaDeBorrador(Of TxstDraft)(
            buscar:=AddressOf _mainForm.TxstDraftPorFormId,
            registrar:=Sub(d) _mainForm.RegisterTxstDraft(d),
            bajar:=Sub(fid) _mainForm.UnregisterTxstDraft(fid),
            idDe:=Function(d) d.FormID,
            construirBase:=AddressOf ConstruirBaseDeDisco)

        ' La fila del MNAM es la que sigue a las de textura, y el número sale de CUÁNTAS se armaron —no
        ' de un 8 escrito tres veces—, que es lo mismo que declara la grilla del diseñador: las ranuras
        ' `AutoSize` y una fila más, vacía, que se come el sobrante.
        Dim filaMnam = ArmarFilasDeTextura()
        If _game = Canon.WbGame.Fallout4 Then
            GridTextures.Controls.Add(LabelMnam, 0, filaMnam)
            GridTextures.Controls.Add(TextBoxMnam, 1, filaMnam)
            GridTextures.Controls.Add(ButtonBrowseMnam, 2, filaMnam)
            AddHandler ButtonBrowseMnam.Click, AddressOf OnBuscarMaterial
        Else
            ' ⛔ En Skyrim la fila no existe, y un control que nunca entra a `Controls` tampoco lo
            ' alcanza el `Dispose` del formulario: queda huérfano. Se sueltan los tres acá. Es seguro —
            ' el `AddHandler` de más abajo sobre un control liberado es legal, y `Volcar`/`Commit` sólo
            ' los tocan dentro de la rama de Fallout 4, gateada por `TryCast(r, Canon.TxstFO4)`.
            LabelMnam.Dispose()
            TextBoxMnam.Dispose()
            ButtonBrowseMnam.Dispose()
        End If

        If draft Is Nothing Then
            draft = TxstDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
            If draft Is Nothing Then
                MessageBox.Show(Me, "This game's format does not declare TXST.", "Texture Set Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
        End If
        _draft = draft
        Dim snap As TxstDraft = Nothing
        Try
            snap = _draft.Clone()
        Catch
        End Try
        ' ⛔⛔ LA TOMA VA PRIMERO, Y SI SE RECHAZA EL EDITOR NO ABRE. Acá había un `_tomaRechazada =
        ' True` que anotaba y SEGUÍA, y eso era PEOR que no tener la ley: el editor quedaba con
        ' `_draft` apuntando a un borrador cuya toma viva es de OTRO editor, y `CommitVivo()` lo
        ' REGISTRABA — o sea mutaba el objeto compartido y lo reponía en el mapa, que es exactamente el
        ' daño que la ley existe para evitar.
        ' ⛔ Y el comentario que había acá decía «no rompe bytes»: es FALSO. Un borrador registrado y
        ' sucio lo emite la fase 2l —«todo borrador SUCIO se emite, referenciado o no»—, así que
        ' llegaba al `.esp`. Es el mismo razonamiento con el que se cerró el OK sobre un borrador en
        ' blanco: registrar NO es inocuo.
        ' ⛔ Y además `Tomar` sale por su `Return False` ANTES de fijar `_actual`, con lo cual
        ' `Abandonar`/`Soltar` al cerrar quedaban en no-ops: no había limpieza Y el FormID quedaba SIN
        ' MARCA, así que un tercer editor también lo podía abrir.
        If Not _toma.Tomar(_draft, snap) Then
            ' El formulario se niega a abrir por sí mismo: así el llamador no tiene que
            ' preguntar nada y `TomaRechazada` no es un canal que nadie lee.
            _tomaRechazada = True
            DialogResult = DialogResult.Cancel
            Return
        End If
        Volcar()
        ' ⛔⛔ NO SE COMMITEA UN BORRADOR NUEVO AL ABRIR. Acá había un `Commit()` incondicional, y
        ' `Commit` REGISTRA: abrir el editor y cerrarlo dejaba un record en el registro, y un borrador
        ' nuevo nace `IsNew` ⇒ `IsDirty` ⇒ la fase 2j lo EMITE al .esp — «todo borrador sucio se
        ' emite, referenciado o no». Es el MISMO defecto que el editor de head parts documenta y
        ' arregla dos veces (el record `npcm_<esp>_HDPT_802` que el usuario encontró en xEdit y no
        ' sabía de dónde salía), y no se había replicado acá. Lo levantó la revisión limpia.
        '   Sobre un OVERRIDE sí se commitea: ese record YA existe, registrarlo no lo crea, y sin el
        ' commit la vista del editor y el registro arrancan distintos.
        If _draft.IsOverride Then Commit()

        AddHandler ButtonNewBlank.Click, AddressOf OnNuevoEnBlanco
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnNuevoDesdePlantilla
        AddHandler ButtonOverrideExisting.Click, AddressOf OnOverrideExistente
        AddHandler ButtonEditMine.Click, AddressOf OnEditarElMio
        AddHandler TextBoxEdid.TextChanged, AddressOf OnCampo
        AddHandler TextBoxMnam.TextChanged, AddressOf OnCampo
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
    End Sub

    '==============================================================================================
    ' LAS CUATRO PUERTAS DE OBJETIVO — el molde de HDPT / ARMO / ARMA, transcrito
    '
    ' ⛔ ANTES ESTE EDITOR NO TENÍA NINGUNA: el modo se INFERÍA de si el campo del llamador traía
    ' FormID cuando se apretó «New / Edit…». Vacío ⇒ nuevo, con valor ⇒ override. El usuario no podía
    ' decir «quiero un override» ni «quiero arrancar de éste», y no había forma de volver a abrir un
    ' TXST propio sin pasar por el campo de otro editor.
    '==============================================================================================

    ''' <summary>«New (blank)»: un TXST nuevo y vacío.</summary>
    Private Sub OnNuevoEnBlanco(sender As Object, e As EventArgs)
        Dim d = TxstDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
        If d Is Nothing Then Return
        ' ⛔ `registrar:=False`: un borrador NUEVO no se commitea al abrir. Registrar NO es inocuo — la
        ' fase de guardado emite TODO borrador sucio, referenciado o no, así que abrir esta puerta y
        ' cerrar el editor escribiría un TXST vacío al .esp. Es la misma ley que el OK ya aplica.
        TomarYVolcar(d, registrar:=False)
    End Sub

    ''' <summary>«New from template…»: un TXST NUEVO con el contenido de uno que ya existe.</summary>
    Private Sub OnNuevoDesdePlantilla(sender As Object, e As EventArgs)
        Dim fid = ElegirTxstReal("Pick the texture set to copy")
        If fid = 0UI Then Return
        Dim rec = _mainForm.PluginManagerForEditor.GetRecord(fid)
        If rec Is Nothing Then Return
        Dim d = TxstDraft.Clon(rec, _mainForm.PluginManagerForEditor, _mainForm.AllocateDraftFormID())
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that texture set.", "Texture Set Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d)
    End Sub

    ''' <summary>«Override existing…»: se edita un TXST que YA existe, conservando su identidad.</summary>
    Private Sub OnOverrideExistente(sender As Object, e As EventArgs)
        Dim fid = ElegirTxstReal("Pick the texture set to override")
        If fid = 0UI Then Return
        CargarComoOverride(fid)
    End Sub

    ''' <summary>«Edit mine…»: los TXST PROPIOS — los borradores de esta sesión Y los que ya están
    ''' GUARDADOS en el .esp.
    ''' <para>⛔ LAS DOS MITADES. Con sólo los borradores, reabrir la app vacía la lista y el usuario ve
    ''' «no hay ninguno» sobre un plugin que YA tiene texture sets suyos. Los guardados salen de
    ''' <see cref="BorradoDeBorradores.EntradasPropias"/>, la sede única.</para>
    ''' <para>La baja la arma el propio selector: le pasamos el dueño y no hay `onDeleteEntry` que
    ''' acordarse de escribir.</para></summary>
    Private Sub OnEditarElMio(sender As Object, e As EventArgs)
        Dim entradas = _mainForm.TxstDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "TXST", .PluginName = If(d.IsOverride, "(override)", "(new)")}).ToList()
        entradas.AddRange(BorradoDeBorradores.EntradasPropias(_mainForm, "TXST", entradas))
        If entradas.Count = 0 Then
            MessageBox.Show(Me,
                            "No texture sets of yours yet — neither drafts in this session nor saved ones " &
                            "in your plugin. Use New, New from template… or Override existing… first.",
                            "Texture Set Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As FormIdPicker_Form = FormIdPicker_Form.ParaBorradores(
                _mainForm, Me, {"TXST"}, "Edit my texture set (drafts + saved)",
                0UI, False, entradas,
                formIdFilter:=Function(fid) entradas.Any(Function(x) x.FormID = fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If _draft IsNot Nothing AndAlso _draft.FormID = dlg.SelectedFormID Then
                MessageBox.Show(Me, "That is the texture set you are already editing in this window.",
                                "Texture Set Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ' ⛔ UN BORRADOR ABIERTO EN OTRO EDITOR NO SE ABRE DOS VECES: el borrador ES el record y se
            ' comparte por REFERENCIA, así que dos ventanas sobre el mismo objeto se pisan el trabajo
            ' sin aviso. La marca la lleva `TomaDeBorrador`; acá sólo se pregunta.
            If Borradores.EstaTomado(dlg.SelectedFormID) Then
                MessageBox.Show(Me, "That texture set is already open in another editor window. Close it first.",
                                "Texture Set Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            CargarComoOverride(dlg.SelectedFormID)
        End Using
    End Sub

    ''' <summary>El selector de un TXST REAL, que comparten «New from template…» y «Override
    ''' existing…». Devuelve 0 si se canceló.</summary>
    ''' <summary>Elegir un record REAL para «New from template…» / «Override existing…».
    ''' <para>⛔⛔ Y LOS PROPIOS YA GUARDADOS TAMBIÉN. Los cuatro <c>ElegirXxxReal</c> usaban el ctor
    ''' PLANO, que sólo lista el orden de carga: un record que el usuario creó con esta app y ya guardó
    ''' NO aparecía, así que no podía partir de su propio trabajo — ni para clonarlo ni para
    ''' override-arlo. El código que esta ola borró decía textualmente que eso era el defecto.</para>
    ''' <para>⛔ Van SIN dueño y ésa es la decisión: este diálogo no toma ningún borrador —devuelve un
    ''' FormID y se cierra—, así que no hay precondición ni postcondición que cumplir. Las filas son
    ''' records REALES, no borradores, pero igual pasan por la fábrica: el botón «Delete / Revert…» se
    ''' habilita sólo sobre las filas que el llamador pasa, y un record propio guardado SÍ tiene salida
    ''' (<c>Gesto.QuitarGuardado</c>).</para></summary>
    Private Function ElegirTxstReal(titulo As String) As UInteger
        Dim propias = BorradoDeBorradores.EntradasPropias(_mainForm, "TXST", Nothing)
        ' ⛔⛔ CON DUEÑO. Este editor TIENE `_toma` y ya implementa el contrato, así que la condición de
        ' `ParaBorradoresSinDueno` —«el sitio no tiene ningún borrador tomado»— NO se cumple. Es el mismo
        ' error que esta ola le corrigió a `ArmaEditor_Form.PickTxstInto`, y acá pesa MÁS porque las clases
        ' COINCIDEN: `EntradasPropias` puede listar el record que este editor tiene tomado, y sin dueño
        ' `Planear` no sabe que lo tomó ESTA ventana ⇒ contesta «already open in another editor window»
        ' —mentira, es ésta— y niega un gesto que la ley concede («tomado por MÍ se permite»).
        Using dlg As FormIdPicker_Form = If(propias.Count = 0,
                New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"TXST"}, titulo, 0UI, allowNull:=False),
                FormIdPicker_Form.ParaBorradores(_mainForm, Me, {"TXST"}, titulo, 0UI, False, propias))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>Tomar <paramref name="fid"/> como override. ⛔ Si YA hay borrador bajo ese FormID, el
    ''' BORRADOR MANDA sobre el disco: se adopta, no se reconstruye — reconstruirlo tiraría lo que el
    ''' usuario ya editó en esta sesión.</summary>
    Private Sub CargarComoOverride(fid As UInteger)
        Dim ya = _mainForm.TxstDraftPorFormId(fid)
        If ya IsNot Nothing Then
            TomarYVolcar(ya)
            Return
        End If
        ' ⛔ POR LA FÁBRICA, no por `Edicion` pelado: la línea de base de la suciedad sale de la MISMA,
        ' y con dos construcciones distintas el record abría SUCIO sin que el usuario lo tocara.
        Dim d = OverridePristino(fid, _mainForm.PluginManagerForEditor)
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that texture set.", "Texture Set Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d)
    End Sub

    ''' <summary>Cambiar el objetivo del editor.
    ''' <para>⛔⛔ ABANDONA LA TOMA ANTERIOR PRIMERO. Sin esto, cada vuelta por una de las cuatro puertas
    ''' deja el borrador anterior REGISTRADO: el editor nace con uno en blanco, así que se iban
    ''' acumulando borradores vacíos y «Edit mine…» los listaba todos. Es el defecto que el editor de
    ''' head parts documenta como «era el ÚNICO editor de la casa sin este paso».</para></summary>
    ''' <param name="registrar">False para un NUEVO EN BLANCO: se toma y se vuelca pero NO se commitea,
    ''' así que no queda registrado hasta que el usuario escriba algo.</param>
    Private Sub TomarYVolcar(d As TxstDraft, Optional registrar As Boolean = True)
        If d Is Nothing Then Return
        Dim snap As TxstDraft = Nothing
        Try
            snap = d.Clone()
        Catch
        End Try
        ' ⛔⛔ rev-37 · EL CAMBIO DE OBJETIVO ES DESTRUCTIVO Y ANTES NO AVISABA: la ley y su
        ' porqué están en `TomaDeBorrador.CambiarDestruyeTrabajo`. El predicado vive allá para que
        ' un testigo lo corra; el cartel, en `BorradoDeBorradores`, para que los cinco editores
        ' digan lo mismo.
        If _toma.CambiarDestruyeTrabajo(d, _draft IsNot Nothing AndAlso _draft.IsDirty) AndAlso
           Not BorradoDeBorradores.ConfirmarCambioDeObjetivo(Me, "texture set", "Texture Set Editor") Then Return
        ' ⛔ LA PUERTA ÚNICA, Y ES MEDIBLE: abandonar + tomar + registrar viven en
        ' `TomaDeBorrador.CambiarObjetivo`, afuera del formulario, porque adentro ningún testigo
        ' puede correrla — cada OK abre validaciones con `MessageBox` y un modal cuelga al gate.
        ' Mismo movimiento que `PlanDeCierreDeListas` y `FilasPropiasDeValue1`.
        If Not _toma.CambiarObjetivo(d, snap, registrar) Then
            MessageBox.Show(Me, "That texture set is already open in another editor window. Close it first.",
                            "Texture Set Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _draft = d
        Volcar()
        ' ⛔ El REGISTRO ya lo hizo la puerta (`CambiarObjetivo`). Acá sólo se vuelca el
        ' formulario al record cuando el objetivo ya es parte del trabajo del usuario.
        If registrar Then Commit()
        ActualizarBanner()
    End Sub

    ''' <summary>El banner dice el MODO, que es el dato más importante de la ventana.</summary>
    Private Sub ActualizarBanner()
        If _draft Is Nothing Then
            LabelBanner.Text = "No target — use New, New from template… or Override existing…"
        Else
            LabelBanner.Text = If(_draft.IsOverride, $"OVERRIDE of 0x{_draft.FormID:X8}", "NEW record")
        End If
    End Sub

    '==============================================================================================
    ' EL CONTRATO CON LA SEDE DE BAJA — `BorradoDeBorradores.IDuenoDeBorradores`
    '==============================================================================================

    ''' <summary>El FormID que este editor tiene tomado. Alimenta la precondición de `Planear`.</summary>
    Private Function FormIdTomado() As UInteger Implements BorradoDeBorradores.IDuenoDeBorradores.FormIdTomado
        Return If(_draft Is Nothing, 0UI, _draft.FormID)
    End Function

    ''' <summary>Sin buffers: cada campo se vuelca al record por <c>Commit</c> apenas cambia, así que el
    ''' censo de referrers ya los ve.</summary>
    Private Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String) _
            Implements BorradoDeBorradores.IDuenoDeBorradores.ReferenciasNoVolcadas
        Return Enumerable.Empty(Of String)()
    End Function

    ''' <summary>POSTCONDICIÓN de la baja.
    ''' <para>⛔⛔ SE SUELTA LA TOMA, NO SE ABANDONA. `Abandonar` REPONDRÍA lo que el usuario acaba de
    ''' borrar —la toma todavía tiene su snapshot y la ley diría «restaurar»—, por encima del
    ''' `MarkRecordForRemoval` del mismo gesto. Los dos métodos existen por esta diferencia, y ésta es
    ''' la trampa que el editor de head parts documenta con el reporte del usuario: «si hago
    ''' delete/revert se duplica».</para></summary>
    Private Sub TrasLaBaja(formID As UInteger) Implements BorradoDeBorradores.IDuenoDeBorradores.TrasLaBaja
        If _draft Is Nothing OrElse _draft.FormID <> formID Then Return
        _toma.Soltar()
        _draft = Nothing
        ActualizarBanner()
    End Sub

    ''' <summary>EL BORRADOR OVERRIDE PRISTINO de un TXST real: el record del archivo, copiado, con
    ''' el EditorID sintetizado si no traía. Es LA FÁBRICA, y por eso la usan los DOS lados — el que
    ''' arma lo que el usuario edita Y <see cref="TomaDeBorrador(Of TD)"/> para la LÍNEA DE BASE.
    ''' <para>⛔⛔ TIENE QUE SER LA MISMA FUNCIÓN PARA LOS DOS, y acá no lo era: los dos lados
    ''' llamaban a `Edicion` pelado, y `CommitVivo` estampa un EditorID por defecto cuando el campo
    ''' está vacío ⇒ el borrador lo traía sintetizado y la base no, así que el editor marcaba SUCIO un
    ''' record que el usuario NO TOCÓ, desde el primer volcado. Es la misma ley que
    ''' `ArmoEditor_Form.OverridePristino` documenta con el caso medido; este editor se escribió sin
    ''' transcribirla.</para></summary>
    Friend Shared Function OverridePristino(fid As UInteger, plugins As PluginManager) As TxstDraft
        If plugins Is Nothing OrElse fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Dim rec = plugins.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then Return Nothing
        Dim d = TxstDraft.Edicion(rec, plugins)
        If d Is Nothing OrElse d.Record Is Nothing Then Return Nothing
        If String.IsNullOrEmpty(d.Record.EditorID) Then
            d.Record.EditorID = TxstDraft.EditorIdPrefix & $"{fid And &HFFFUI:X3}"
        End If
        d.IsModified = False
        Return d
    End Function

    Private Function ConstruirBaseDeDisco(fid As UInteger) As TxstDraft
        Return OverridePristino(fid, _mainForm.PluginManagerForEditor)
    End Function

    ''' <summary>Las ocho filas, con los rótulos DEL JUEGO de la sesión. Las tres específicas se agregan
    ''' con su nombre propio: no son «la misma ranura con otro nombre».
    ''' <para>Devuelve el índice de la PRIMERA fila libre, que es donde va el MNAM en Fallout 4.</para></summary>
    Private Function ArmarFilasDeTextura() As Integer
        Dim filas = If(_game = Canon.WbGame.Skyrim,
            New (Clave As String, Rotulo As String)() {
                ("Diffuse", "TX00 — Diffuse"),
                ("NormalGloss", "TX01 — Normal/Gloss"),
                ("EnvironmentMaskSubsurfaceTint", "TX02 — Environment Mask/Subsurface Tint"),
                ("GlowDetailMap", "TX03 — Glow/Detail Map"),
                ("Height", "TX04 — Height"),
                ("Environment", "TX05 — Environment"),
                ("Multilayer", "TX06 — Multilayer"),
                ("BacklightMaskSpecular", "TX07 — Backlight Mask/Specular")},
            New (Clave As String, Rotulo As String)() {
                ("Diffuse", "TX00 — Diffuse"),
                ("NormalGloss", "TX01 — Normal/Gloss"),
                ("Wrinkles", "TX02 — Wrinkles"),
                ("Glow", "TX03 — Glow"),
                ("Height", "TX04 — Height"),
                ("Environment", "TX05 — Environment"),
                ("Multilayer", "TX06 — Multilayer"),
                ("SmoothSpec", "TX07 — Smooth Spec")})
        For i = 0 To filas.Length - 1
            Dim lbl As New Label() With {.AutoSize = True, .Anchor = AnchorStyles.Left, .Text = filas(i).Rotulo}
            Dim tb As New TextBox() With {.Dock = DockStyle.Fill, .Tag = filas(i).Clave}
            AddHandler tb.TextChanged, AddressOf OnCampo
            Dim btn As New Button() With {.AutoSize = True, .Text = "Browse…", .Tag = tb}
            AddHandler btn.Click, AddressOf OnBuscarTextura
            GridTextures.Controls.Add(lbl, 0, i)
            GridTextures.Controls.Add(tb, 1, i)
            GridTextures.Controls.Add(btn, 2, i)
            _cajas(filas(i).Clave) = tb
        Next
        Return filas.Length
    End Function

    ''' <summary>⛔⛔ EL PICKER DEL DICCIONARIO DE ARCHIVOS, NO UN DIÁLOGO DEL SISTEMA. Acá había un
    ''' <c>OpenFileDialog</c> en las ocho ranuras, y un diálogo del sistema ve el disco y NADA MÁS: las
    ''' texturas y los materiales vanilla viven DENTRO de los BA2 (Fallout 4) y los BSA (Skyrim), así que
    ''' con él el usuario no podía elegir ninguno de los que el juego trae — sólo los que algún mod dejó
    ''' sueltos en <c>Data</c>. Y el recorte a mano que venía con él —buscar <c>\textures\</c> dentro de
    ''' la ruta absoluta y cortar; si no aparecía, dejar el NOMBRE DEL ARCHIVO SOLO— inventaba una ruta
    ''' que el juego no resuelve.
    ''' <para>⛔ La siembra, la guarda del diccionario vacío y el recorte del prefijo NO se escriben acá:
    ''' son las mismas tres decisiones para todos los selectores de la app y viven en
    ''' <see cref="PickerDeAssets"/>, que siembra con la misma normalización que usa el RENDER. Acá hubo
    ''' una versión propia y más débil, y este bloque todavía la describía como si siguiera viva —dos
    ''' <c>summary</c> sobre el mismo miembro, en desacuerdo entre ellos—; lo levantó la revisión.</para>
    ''' <para>El record guarda el valor SIN el prefijo raíz: medido sobre los 845 TXST ganadores de
    ''' Fallout 4, 0 de 654 <c>TX00</c> traen <c>textures\</c> y 0 de 186 <c>MNAM</c> traen
    ''' <c>materials\</c>.</para></summary>
    Private Sub OnBuscarTextura(sender As Object, e As EventArgs)
        Dim btn = TryCast(sender, Button)
        Dim tb = TryCast(btn?.Tag, TextBox)
        If tb Is Nothing Then Return
        PickerDeAssets.ElegirEnCaja(Me, tb, FilesDictionary_class.TexturesDictionary_Filter)
    End Sub

    ''' <summary>El <c>MNAM</c> es un archivo de material del juego: mismo picker, raíz
    ''' <c>Materials\</c>. Las extensiones son las del filtro de materiales que la app ya usa en el
    ''' editor de substituciones (<c>.bgsm</c> + <c>.bgem</c>); el campo es un string libre y el motor no
    ''' restringe la extensión, así que el filtro NO se acota a <c>.bgsm</c> por más que en el corpus los
    ''' 186 TXST de Fallout 4 que traen MNAM lleven los 186 un <c>.bgsm</c>.</summary>
    Private Sub OnBuscarMaterial(sender As Object, e As EventArgs)
        PickerDeAssets.ElegirEnCaja(Me, TextBoxMnam, FilesDictionary_class.MaterialsDictionary_Filter)
    End Sub

    Private Sub Volcar()
        If _draft?.Record Is Nothing Then Return
        _cargando = True
        Try
            Dim r = _draft.Record
            TextBoxEdid.Text = If(r.EditorID, "")
            ActualizarBanner()
            PonerCaja("Diffuse", r.TexturesRGBADiffuse)
            PonerCaja("NormalGloss", r.TexturesRGBANormalGloss)
            PonerCaja("Height", r.TexturesRGBAHeight)
            PonerCaja("Environment", r.TexturesRGBAEnvironment)
            PonerCaja("Multilayer", r.TexturesRGBAMultilayer)
            Dim fo4 = TryCast(r, Canon.TxstFO4)
            If fo4 IsNot Nothing Then
                PonerCaja("Wrinkles", fo4.TexturesRGBAWrinkles)
                PonerCaja("Glow", fo4.TexturesRGBAGlow)
                PonerCaja("SmoothSpec", fo4.TexturesRGBASmoothSpec)
                TextBoxMnam.Text = If(fo4.MaterialPresente, If(fo4.Material, ""), "")
            End If
            Dim sse = TryCast(r, Canon.TxstSSE)
            If sse IsNot Nothing Then
                PonerCaja("EnvironmentMaskSubsurfaceTint", sse.TexturesRGBAEnvironmentMaskSubsurfaceTint)
                PonerCaja("GlowDetailMap", sse.TexturesRGBAGlowDetailMap)
                PonerCaja("BacklightMaskSpecular", sse.TexturesRGBABacklightMaskSpecular)
            End If
            LabelObnd.Text = "Object bounds (OBND): kept as-is — it is in 100 % of the corpus and its value " &
                             "is not invented (a clone copies its template's; a blank one is zeros, which " &
                             "486 of 1.000 FO4 texture sets carry)."
            ' ⛔ POR PRESENCIA, NO POR VALOR. Acá el predicado era `r.DecalDataMinWidth <> 0.0F OrElse
            ' r.DecalDataMaxWidth <> 0.0F`, o sea que un `DODT` PRESENTE con los anchos en cero se
            ' rotulaba «none» — sobre un campo que este mismo formulario promete «preservado, no
            ' editable». Es la ley «AUSENTE NO ES CERO» que esta ola escribe tres veces (`MODC`,
            ' `TNAM`/`CNAM`, `MNAM`), rota justo en el rótulo que la tendría que reflejar.
            LabelDodt.Text = If(r.DecalDataMinWidthPresente OrElse r.DecalDataMaxWidthPresente,
                                "Decal data (DODT): present — preserved, not editable here.",
                                "Decal data (DODT): none.")
        Finally
            _cargando = False
        End Try
    End Sub

    Private Sub PonerCaja(clave As String, valor As String)
        Dim tb As TextBox = Nothing
        If _cajas.TryGetValue(clave, tb) Then tb.Text = If(valor, "")
    End Sub

    Private Function Caja(clave As String) As String
        Dim tb As TextBox = Nothing
        If _cajas.TryGetValue(clave, tb) Then Return tb.Text.Trim()
        Return ""
    End Function

    Private Function Commit() As Boolean
        If _draft?.Record Is Nothing OrElse _cargando Then Return False
        Try
            Dim r = _draft.Record
            r.EditorID = TextBoxEdid.Text.Trim()
            ' ⛔⛔ LAS OCHO RANURAS POR PRESENCIA, NO POR ASIGNACIÓN. Acá estaban las ocho escritas
            ' como `r.TexturesRGBAX = Caja(«X»)` sin condición, y escribir un campo que no está LO
            ' CREA (`CanonView.Escribir`), así que un override de un TXST vanilla BROTABA ranuras de
            ' textura VACÍAS que el usuario nunca autoró — el mismo defecto que el `TNAM`/`CNAM`/`MODS`
            ' del editor de head parts, que el usuario ya vio en xEdit. El tamaño está medido: el
            ' `TX07` lo traen 716 de los 1.000 TXST de Fallout 4, o sea que **284 no lo traen**.
            '   ⛔ El QUITADO va por la propiedad de presencia de la vista, que está enrutada por la
            ' ruta del esquema. Por firma no servía: los `TX00..TX07` cuelgan del
            ' `RStruct(«Textures (RGB/A)»)` y no de la raíz. Y así tampoco hay que acordarse de que las
            ' ranuras NO son las mismas en los dos juegos — `TX02` es `Wrinkles` en FO4 y
            ' `Environment Mask/Subsurface Tint` en Skyrim, `TX03` es `Glow` contra `Glow/Detail Map`,
            ' `TX07` es `Smooth Spec` contra `Backlight Mask/Specular` —: cada vista trae su ruta.
            Borradores.EscribirCampoOQuitar(Caja("Diffuse").Length > 0,
                                            Sub() r.TexturesRGBADiffuse = Caja("Diffuse"),
                                            Sub() r.TexturesRGBADiffusePresente = False)
            Borradores.EscribirCampoOQuitar(Caja("NormalGloss").Length > 0,
                                            Sub() r.TexturesRGBANormalGloss = Caja("NormalGloss"),
                                            Sub() r.TexturesRGBANormalGlossPresente = False)
            Borradores.EscribirCampoOQuitar(Caja("Height").Length > 0,
                                            Sub() r.TexturesRGBAHeight = Caja("Height"),
                                            Sub() r.TexturesRGBAHeightPresente = False)
            Borradores.EscribirCampoOQuitar(Caja("Environment").Length > 0,
                                            Sub() r.TexturesRGBAEnvironment = Caja("Environment"),
                                            Sub() r.TexturesRGBAEnvironmentPresente = False)
            Borradores.EscribirCampoOQuitar(Caja("Multilayer").Length > 0,
                                            Sub() r.TexturesRGBAMultilayer = Caja("Multilayer"),
                                            Sub() r.TexturesRGBAMultilayerPresente = False)
            Dim fo4 = TryCast(r, Canon.TxstFO4)
            If fo4 IsNot Nothing Then
                Borradores.EscribirCampoOQuitar(Caja("Wrinkles").Length > 0,
                                                Sub() fo4.TexturesRGBAWrinkles = Caja("Wrinkles"),
                                                Sub() fo4.TexturesRGBAWrinklesPresente = False)
                Borradores.EscribirCampoOQuitar(Caja("Glow").Length > 0,
                                                Sub() fo4.TexturesRGBAGlow = Caja("Glow"),
                                                Sub() fo4.TexturesRGBAGlowPresente = False)
                Borradores.EscribirCampoOQuitar(Caja("SmoothSpec").Length > 0,
                                                Sub() fo4.TexturesRGBASmoothSpec = Caja("SmoothSpec"),
                                                Sub() fo4.TexturesRGBASmoothSpecPresente = False)
                ' ⛔ Y EL MNAM SE PUEDE VACIAR: antes el `If … Length > 0` dejaba el valor viejo puesto
                ' sin avisar, o sea que borrar la caja era un no-op MUDO. Misma ley que las ranuras.
                Borradores.EscribirCampoOQuitar(TextBoxMnam.Text.Trim().Length > 0,
                                                Sub() fo4.Material = TextBoxMnam.Text.Trim(),
                                                Sub() fo4.MaterialPresente = False)
            End If
            Dim sse = TryCast(r, Canon.TxstSSE)
            If sse IsNot Nothing Then
                Borradores.EscribirCampoOQuitar(Caja("EnvironmentMaskSubsurfaceTint").Length > 0,
                                                Sub() sse.TexturesRGBAEnvironmentMaskSubsurfaceTint = Caja("EnvironmentMaskSubsurfaceTint"),
                                                Sub() sse.TexturesRGBAEnvironmentMaskSubsurfaceTintPresente = False)
                Borradores.EscribirCampoOQuitar(Caja("GlowDetailMap").Length > 0,
                                                Sub() sse.TexturesRGBAGlowDetailMap = Caja("GlowDetailMap"),
                                                Sub() sse.TexturesRGBAGlowDetailMapPresente = False)
                Borradores.EscribirCampoOQuitar(Caja("BacklightMaskSpecular").Length > 0,
                                                Sub() sse.TexturesRGBABacklightMaskSpecular = Caja("BacklightMaskSpecular"),
                                                Sub() sse.TexturesRGBABacklightMaskSpecularPresente = False)
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[TXST-EDITOR] commit falló: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
        Dim igual As Boolean = False
        If _toma.Base IsNot Nothing Then igual = _draft.ContentEquals(_toma.Base)
        _draft.IsModified = _toma.Sucio(igual)
        _mainForm.RegisterTxstDraft(_draft)
        Return True
    End Function

    Private Sub OnCampo(sender As Object, e As EventArgs)
        If _cargando Then Return
        Commit()
    End Sub

    ''' <summary>⛔⛔ OK SOBRE UN BORRADOR NUEVO Y SIN TOCAR NO REGISTRA NADA. Sin esta guarda,
    ''' abrir el editor y apretar OK escribía un TXST VACÍO al .esp con EditorID automático. Es la
    ''' gemela de la del editor de head parts, y el predicado es el mismo: «¿sigue siendo idéntico a uno
    ''' en blanco?», construido en el acto — no contra una foto de apertura, que sale de un `Clone()`
    ''' dentro de un `Try` y si falla deja la guarda sin correr.</para>
    ''' <para>⛔⛔ LA PRIMERA GUARDA ES «NO HAY OBJETIVO», Y FALTABA. `TrasLaBaja` deja
    ''' <c>_draft = Nothing</c> —correcto: el usuario acaba de borrar ese borrador desde el
    ''' selector—, y desde ahí el OK caía en <c>Commit()</c>, que sin borrador devuelve False y
    ''' abre «The edit could not be written…». <b>La ventana quedaba sin forma de cerrarse por
    ''' OK</b>: cada clic repetía el mismo cartel de error sobre algo que no es un error.</para>
    ''' <para>La salida ya estaba escrita en <c>LeveledListEditor_Form.OnOk</c> y se transcribe
    ''' literal: sin objetivo, el OK es un Cancel.</para></summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If _draft Is Nothing Then
            DialogResult = DialogResult.Cancel : Close() : Return
        End If
        If _draft.IsNew Then
            Dim intacto As Boolean = False
            Try
                Dim enBlanco = TxstDraft.Nuevo(_draft.FormID, _game)
                intacto = enBlanco IsNot Nothing AndAlso _draft.ContentEquals(enBlanco)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[TXST-EDITOR] guarda del OK: no se pudo construir el record en blanco: {{ex.GetType().Name}}: {{ex.Message}}")
            End Try
            If intacto Then
                ' El usuario no hizo un record: apretó OK sobre nada.
                _toma.Abandonar()
                DialogResult = DialogResult.Cancel : Close() : Return
            End If
        End If
        If Not Commit() Then
            MessageBox.Show(Me, "The edit could not be written to the record — nothing was accepted.",
                            "Texture Set Editor", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        If TextBoxEdid.Text.Trim().Length = 0 Then
            TextBoxEdid.Text = TxstDraft.EditorIdPrefix & $"{_draft.FormID And &HFFFUI:X3}"
            Commit()
        End If
        _toma.Soltar()
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub OnCancel(sender As Object, e As EventArgs)
        _toma.Abandonar()
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    Private Sub TextureSetEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If DialogResult <> DialogResult.OK Then _toma.Abandonar()
    End Sub

End Class
