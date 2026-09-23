Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>El editor de una lista de formularios (<c>FLST</c>): nueva o override. Existe por el
''' <c>RNAM</c> de un head part — «en qué razas es válido» es una FLST, así que sin esto un head part
''' propio no puede declararse válido para una raza custom.
'''
''' <para>⛔ <b><c>LNAM</c> es un arreglo de FormID SIN firma declarada en el esquema</b>
''' (<c>Wb.Fid("FormID")</c>): una FLST puede contener cualquier record. El filtro a RACE que este
''' editor aplica cuando lo abre el <c>RNAM</c> es <b>de la UI y no del record</b> — sirve para que el
''' usuario no meta una espada en una lista de razas, y no cambia lo que la lista puede llevar.</para>
'''
''' <para>El gesto por defecto es <b>clonar una que ya existe y agregarle una raza</b>, no armar una
''' desde cero. Medido: de los 2.546 HDPT de Fallout 4, los <b>2.528</b> con <c>RNAM</c> distinto de cero apuntan a <b>sólo SEIS</b> FLST
''' distintas en todo el juego (52 en Skyrim, contando el ganador por FormID), así que la lista que el
''' usuario necesita casi siempre es «una de ésas, más mi raza».</para></summary>
Public Class FormListEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _game As Canon.WbGame
    Private ReadOnly _soloRazas As Boolean
    Private _draft As FlstDraft
    Private _toma As TomaDeBorrador(Of FlstDraft)
    Private _cargando As Boolean

    Public ReadOnly Property ResultFlstFormID As UInteger
        Get
            Return If(_draft Is Nothing, 0UI, _draft.FormID)
        End Get
    End Property

    ''' <param name="draft">El borrador a editar. Nothing ⇒ uno nuevo, en blanco.</param>
    ''' <param name="soloRazas">Filtra el selector de «Add…» a RACE. Es de la UI: ver el ⛔ de la
    ''' clase.
    ''' <para>⛔ SIN DEFAULT, A PROPÓSITO. Era `Optional … = False`, y el default elegía la opción
    ''' MENOS útil para el único llamador que hay: el editor de head parts abre esta lista para el
    ''' `RNAM`, que es una lista de razas. Un llamador nuevo que se olvide el argumento no recibe un
    ''' error: recibe un selector sin filtro, y eso se ve como «el Add me ofrece todos los records
    ''' del juego». Obligatorio, el compilador lo pregunta.</para></param>
    ''' <summary>La toma se rechazó porque ese borrador ya está abierto en otro editor. Lo mira el
    ''' llamador: cerrar un formulario a medio construir dejaría al <c>ShowDialog</c> devolviendo un
    ''' resultado que nadie decidió.</summary>
    Friend ReadOnly Property TomaRechazada As Boolean
        Get
            Return _tomaRechazada
        End Get
    End Property
    Private _tomaRechazada As Boolean

    Public Sub New(mainForm As MainForm, draft As FlstDraft, soloRazas As Boolean)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        InitializeComponent()
        _mainForm = mainForm
        _soloRazas = soloRazas
        _game = If(Config_App.Current IsNot Nothing AndAlso
                   Config_App.Current.Game = Config_App.Game_Enum.Skyrim,
                   Canon.WbGame.Skyrim, Canon.WbGame.Fallout4)
        _toma = New TomaDeBorrador(Of FlstDraft)(
            buscar:=AddressOf _mainForm.FlstDraftPorFormId,
            registrar:=Sub(d) _mainForm.RegisterFlstDraft(d),
            bajar:=Sub(fid) _mainForm.UnregisterFlstDraft(fid),
            idDe:=Function(d) d.FormID,
            construirBase:=AddressOf ConstruirBaseDeDisco)

        If _game = Canon.WbGame.Skyrim Then
            TopRow.Controls.Remove(LabelName)
            TopRow.Controls.Remove(TextBoxName)
        End If
        ListViewMembers.Columns.Add("Member (LNAM)", 420)
        ListViewMembers.Columns.Add("Signature", 90)
        ListViewMembers.Columns.Add("Source", 220)
        LabelHint.Text = If(_soloRazas,
            "Opened from RNAM, so ""Add…"" is filtered to RACE — that filter is the UI's, not the record's: " &
            "LNAM has no declared signature and can hold anything. The usual answer is ""clone one of the six " &
            "lists the game already uses and add your race"".",
            "LNAM has no declared signature in the schema: this list can hold any record.")

        If draft Is Nothing Then
            draft = FlstDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
            If draft Is Nothing Then
                MessageBox.Show(Me, "This game's format does not declare FLST.", "Form List Editor",
                                MessageBoxButtons.OK, MessageBoxIcon.Error)
                Return
            End If
        End If
        _draft = draft
        Dim snap As FlstDraft = Nothing
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
        ' nuevo nace `IsNew` ⇒ `IsDirty` ⇒ la fase 2k lo EMITE al .esp — «todo borrador sucio se
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
        AddHandler TextBoxName.TextChanged, AddressOf OnCampo
        AddHandler ButtonAdd.Click, AddressOf OnAgregar
        AddHandler ButtonRemove.Click, AddressOf OnQuitar
        AddHandler ButtonUp.Click, Sub() Mover(-1)
        AddHandler ButtonDown.Click, Sub() Mover(1)
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
    End Sub

    Private Function ConstruirBaseDeDisco(fid As UInteger) As FlstDraft
        If fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Return OverridePristino(fid, _mainForm.PluginManagerForEditor)
    End Function

    '==============================================================================================
    ' LAS CUATRO PUERTAS DE OBJETIVO — el molde de HDPT / ARMO / ARMA, transcrito
    '
    ' ⛔ ANTES ESTE EDITOR NO TENÍA NINGUNA: el modo se INFERÍA de si el campo del llamador traía
    ' FormID cuando se apretó «New / Edit…». Vacío ⇒ nuevo, con valor ⇒ override. El usuario no podía
    ' decir «quiero un override» ni «quiero arrancar de éste», y no había forma de volver a abrir una
    ' FLST propia sin pasar por el campo de otro editor.
    '==============================================================================================

    ''' <summary>«New (blank)»: una FLST nueva y vacía.</summary>
    Private Sub OnNuevoEnBlanco(sender As Object, e As EventArgs)
        Dim d = FlstDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
        If d Is Nothing Then Return
        ' ⛔ `registrar:=False`: un borrador NUEVO no se commitea al abrir. Registrar NO es inocuo — la
        ' fase de guardado emite TODO borrador sucio, referenciado o no, así que abrir esta puerta y
        ' cerrar el editor escribiría una FLST vacía al .esp. Es la misma ley que el OK ya aplica.
        TomarYVolcar(d, registrar:=False)
    End Sub

    ''' <summary>«New from template…»: una FLST NUEVA con el contenido de una que ya existe.</summary>
    Private Sub OnNuevoDesdePlantilla(sender As Object, e As EventArgs)
        Dim fid = ElegirFlstReal("Pick the form list to copy")
        If fid = 0UI Then Return
        Dim rec = _mainForm.PluginManagerForEditor.GetRecord(fid)
        If rec Is Nothing Then Return
        Dim d = FlstDraft.Clon(rec, _mainForm.PluginManagerForEditor, _mainForm.AllocateDraftFormID())
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that form list.", "Form List Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d)
    End Sub

    ''' <summary>«Override existing…»: se edita una FLST que YA existe, conservando su identidad.</summary>
    Private Sub OnOverrideExistente(sender As Object, e As EventArgs)
        Dim fid = ElegirFlstReal("Pick the form list to override")
        If fid = 0UI Then Return
        CargarComoOverride(fid)
    End Sub

    ''' <summary>«Edit mine…»: las FLST PROPIAS — los borradores de esta sesión Y los que ya están
    ''' GUARDADOS en el .esp.
    ''' <para>⛔ LAS DOS MITADES. Con sólo los borradores, reabrir la app vacía la lista y el usuario ve
    ''' «no hay ninguno» sobre un plugin que YA tiene texture sets suyos. Los guardados salen de
    ''' <see cref="BorradoDeBorradores.EntradasPropias"/>, la sede única.</para>
    ''' <para>La baja la arma el propio selector: le pasamos el dueño y no hay `onDeleteEntry` que
    ''' acordarse de escribir.</para></summary>
    Private Sub OnEditarElMio(sender As Object, e As EventArgs)
        Dim entradas = _mainForm.FlstDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "FLST", .PluginName = If(d.IsOverride, "(override)", "(new)")}).ToList()
        entradas.AddRange(BorradoDeBorradores.EntradasPropias(_mainForm, "FLST", entradas))
        If entradas.Count = 0 Then
            MessageBox.Show(Me,
                            "No form lists of yours yet — neither drafts in this session nor saved ones " &
                            "in your plugin. Use New, New from template… or Override existing… first.",
                            "Form List Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As FormIdPicker_Form = FormIdPicker_Form.ParaBorradores(
                _mainForm, Me, {"FLST"}, "Edit my form list (drafts + saved)",
                0UI, False, entradas,
                formIdFilter:=Function(fid) entradas.Any(Function(x) x.FormID = fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If _draft IsNot Nothing AndAlso _draft.FormID = dlg.SelectedFormID Then
                MessageBox.Show(Me, "That is the form list you are already editing in this window.",
                                "Form List Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ' ⛔ UN BORRADOR ABIERTO EN OTRO EDITOR NO SE ABRE DOS VECES: el borrador ES el record y se
            ' comparte por REFERENCIA, así que dos ventanas sobre el mismo objeto se pisan el trabajo
            ' sin aviso. La marca la lleva `TomaDeBorrador`; acá sólo se pregunta.
            If Borradores.EstaTomado(dlg.SelectedFormID) Then
                MessageBox.Show(Me, "That form list is already open in another editor window. Close it first.",
                                "Form List Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            CargarComoOverride(dlg.SelectedFormID)
        End Using
    End Sub

    ''' <summary>El selector de una FLST REAL, que comparten «New from template…» y «Override
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
    Private Function ElegirFlstReal(titulo As String) As UInteger
        Dim propias = BorradoDeBorradores.EntradasPropias(_mainForm, "FLST", Nothing)
        ' ⛔⛔ CON DUEÑO. Este editor TIENE `_toma` y ya implementa el contrato, así que la condición de
        ' `ParaBorradoresSinDueno` —«el sitio no tiene ningún borrador tomado»— NO se cumple. Es el mismo
        ' error que esta ola le corrigió a `ArmaEditor_Form.PickTxstInto`, y acá pesa MÁS porque las clases
        ' COINCIDEN: `EntradasPropias` puede listar el record que este editor tiene tomado, y sin dueño
        ' `Planear` no sabe que lo tomó ESTA ventana ⇒ contesta «already open in another editor window»
        ' —mentira, es ésta— y niega un gesto que la ley concede («tomado por MÍ se permite»).
        Using dlg As FormIdPicker_Form = If(propias.Count = 0,
                New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"FLST"}, titulo, 0UI, allowNull:=False),
                FormIdPicker_Form.ParaBorradores(_mainForm, Me, {"FLST"}, titulo, 0UI, False, propias))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>Tomar <paramref name="fid"/> como override. ⛔ Si YA hay borrador bajo ese FormID, el
    ''' BORRADOR MANDA sobre el disco: se adopta, no se reconstruye — reconstruirlo tiraría lo que el
    ''' usuario ya editó en esta sesión.</summary>
    Private Sub CargarComoOverride(fid As UInteger)
        Dim ya = _mainForm.FlstDraftPorFormId(fid)
        If ya IsNot Nothing Then
            TomarYVolcar(ya)
            Return
        End If
        ' ⛔ POR LA FÁBRICA, no por `Edicion` pelado: la línea de base de la suciedad sale de la MISMA,
        ' y con dos construcciones distintas el record abría SUCIO sin que el usuario lo tocara.
        Dim d = OverridePristino(fid, _mainForm.PluginManagerForEditor)
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that form list.", "Form List Editor",
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
    Private Sub TomarYVolcar(d As FlstDraft, Optional registrar As Boolean = True)
        If d Is Nothing Then Return
        Dim snap As FlstDraft = Nothing
        Try
            snap = d.Clone()
        Catch
        End Try
        ' ⛔⛔ rev-37 · EL CAMBIO DE OBJETIVO ES DESTRUCTIVO Y ANTES NO AVISABA: la ley y su
        ' porqué están en `TomaDeBorrador.CambiarDestruyeTrabajo`. El predicado vive allá para que
        ' un testigo lo corra; el cartel, en `BorradoDeBorradores`, para que los cinco editores
        ' digan lo mismo.
        If _toma.CambiarDestruyeTrabajo(d, _draft IsNot Nothing AndAlso _draft.IsDirty) AndAlso
           Not BorradoDeBorradores.ConfirmarCambioDeObjetivo(Me, "form list", "Form List Editor") Then Return
        ' ⛔ LA PUERTA ÚNICA, Y ES MEDIBLE: abandonar + tomar + registrar viven en
        ' `TomaDeBorrador.CambiarObjetivo`, afuera del formulario, porque adentro ningún testigo
        ' puede correrla — cada OK abre validaciones con `MessageBox` y un modal cuelga al gate.
        ' Mismo movimiento que `PlanDeCierreDeListas` y `FilasPropiasDeValue1`.
        If Not _toma.CambiarObjetivo(d, snap, registrar) Then
            MessageBox.Show(Me, "That form list is already open in another editor window. Close it first.",
                            "Form List Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
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

    ''' <summary>EL BORRADOR OVERRIDE PRISTINO de un FLST real: el record del archivo, copiado, con
    ''' el EditorID sintetizado si no traía. Es LA FÁBRICA, y por eso la usan los DOS lados — el que
    ''' arma lo que el usuario edita Y <see cref="TomaDeBorrador(Of TD)"/> para la LÍNEA DE BASE.
    ''' <para>⛔⛔ TIENE QUE SER LA MISMA FUNCIÓN PARA LOS DOS, y acá no lo era: los dos lados
    ''' llamaban a `Edicion` pelado, y `CommitVivo` estampa un EditorID por defecto cuando el campo
    ''' está vacío ⇒ el borrador lo traía sintetizado y la base no, así que el editor marcaba SUCIO un
    ''' record que el usuario NO TOCÓ, desde el primer volcado. Es la misma ley que
    ''' `ArmoEditor_Form.OverridePristino` documenta con el caso medido; este editor se escribió sin
    ''' transcribirla.</para></summary>
    Friend Shared Function OverridePristino(fid As UInteger, plugins As PluginManager) As FlstDraft
        If plugins Is Nothing OrElse fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Dim rec = plugins.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "FLST" Then Return Nothing
        Dim d = FlstDraft.Edicion(rec, plugins)
        If d Is Nothing OrElse d.Record Is Nothing Then Return Nothing
        If String.IsNullOrEmpty(d.Record.EditorID) Then
            d.Record.EditorID = FlstDraft.EditorIdPrefix & $"{fid And &HFFFUI:X3}"
        End If
        d.IsModified = False
        Return d
    End Function

    Private Sub Volcar()
        If _draft?.Record Is Nothing Then Return
        _cargando = True
        Try
            Dim r = _draft.Record
            TextBoxEdid.Text = If(r.EditorID, "")
            ' ⛔ El FULL de una FLST existe en Fallout 4 y NO en Skyrim (medido en el esquema:
            ' `Rec_FLST` de TES5 declara sólo EDID + LNAM), así que no está en la interfaz común y
            ' entra por la vista concreta. En Skyrim el campo se SACA, no se deshabilita.
            Dim fo4 = TryCast(r, Canon.FlstFO4)
            If fo4 IsNot Nothing Then
                TextBoxName.Text = If(fo4.NamePresente, If(fo4.Name, ""), "")
            End If
            ActualizarBanner()
            RefrescarMiembros()
        Finally
            _cargando = False
        End Try
    End Sub

    Private Sub RefrescarMiembros()
        ListViewMembers.Items.Clear()
        If _draft?.Record Is Nothing Then Return
        For Each fid In _draft.Record.Miembros()
            Dim it As New ListViewItem(_mainForm.GetRecordDisplayNameForEditor(fid))
            Dim rec = _mainForm.PluginManagerForEditor.GetRecord(fid)
            it.SubItems.Add(If(rec Is Nothing, "(draft/unresolved)", rec.Header.Signature))
            it.SubItems.Add(If(rec Is Nothing, "(new)", If(rec.SourcePluginName, "")))
            it.Tag = fid
            ListViewMembers.Items.Add(it)
        Next
    End Sub

    '==============================================================================================
    ' ⛔ ACÁ VIVÍA «Start from an existing list…» (`ButtonCloneFrom` + `OnClonarDeUnaQueExiste`), y lo
    ' SACÓ EL USUARIO por decisión expresa (22-sep), a sabiendas de lo que se pierde.
    '
    ' QUÉ HACÍA, para que nadie lo reinvente creyendo que es otra cosa: REEMPLAZABA el contenido de
    ' ESTA lista con el de otra, CONSERVANDO la identidad del destino (su `EditorID`, sus banderas de
    ' encabezado y su FormID). No es «New from template…» — aquél CREA un record nuevo — ni
    ' «Override existing…» — aquél cambia el OBJETIVO del editor —. Era un tercer gesto, y por eso
    ' dos botones que se leían parecido hacían cosas opuestas sobre el trabajo del usuario.
    '
    ' ⛔ Y CON ÉL SE FUE `Borradores.ReidentificarComoOverride`, que era LA LEY DE ESTE GESTO Y DE
    ' NINGUN OTRO — su propio doc lo decía: «llená mi record con el contenido de ese otro», que NO es
    ' «haceme un record nuevo a partir de ese otro». Medido al sacarla: `ReidentificarComoClon` tiene
    ' OCHO consumidores y ella tenía CERO. Las cuatro puertas nuevas no la pueden usar:
    ' «New from template…» crea un record nuevo y pasa por `ReidentificarComoClon`, y
    ' «Override existing…» construye el borrador con `Edicion`, que trae la identidad del propio
    ' record. Dejarla `Public` y sin llamador sería peor que borrarla: código que parece disponible,
    ' que ningún gate ejercita, y que el próximo lector puede tomar por vigente.
    '==============================================================================================


    Private Sub OnAgregar(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing Then Return
        Dim sigs = If(_soloRazas, New String() {"RACE"}, New String() {})
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, sigs,
                                           If(_soloRazas, "Add a race", "Add a record"),
                                           0UI, False)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.SelectedFormID = 0UI Then Return
            If _draft.Record.Miembros().Contains(dlg.SelectedFormID) Then Return
            Dim nuevo = _draft.Record.AgregarFormIDs()
            If nuevo Is Nothing Then Return
            nuevo.FormID = dlg.SelectedFormID
        End Using
        RefrescarMiembros()
        Commit()
    End Sub

    Private Sub OnQuitar(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing OrElse ListViewMembers.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListViewMembers.SelectedItems(0).Tag)
        For Each m In _draft.Record.FormIDs.ToList()
            If m.FormID = fid Then _draft.Record.QuitarFormIDs(m) : Exit For
        Next
        RefrescarMiembros()
        Commit()
    End Sub

    Private Sub Mover(delta As Integer)
        If _draft?.Record Is Nothing OrElse ListViewMembers.SelectedIndices.Count = 0 Then Return
        Dim idx = ListViewMembers.SelectedIndices(0)
        Dim destino = idx + delta
        If destino < 0 OrElse destino >= ListViewMembers.Items.Count Then Return
        Dim orden = Enumerable.Range(0, ListViewMembers.Items.Count).ToList()
        orden(idx) = destino : orden(destino) = idx
        _draft.Record.ReordenarFormIDs(orden)
        RefrescarMiembros()
        If destino < ListViewMembers.Items.Count Then ListViewMembers.Items(destino).Selected = True
        Commit()
    End Sub

    Private Function Commit() As Boolean
        If _draft?.Record Is Nothing OrElse _cargando Then Return False
        Try
            _draft.Record.EditorID = TextBoxEdid.Text.Trim()
            Dim fo4c = TryCast(_draft.Record, Canon.FlstFO4)
            ' ⛔ EL FULL SE PUEDE VACIAR. Antes era `If … Length > 0 Then fo4c.Name = …`, o sea que
            ' borrar la caja dejaba el valor viejo puesto sin avisar: un no-op MUDO. Misma ley que en
            ' los otros dos editores de esta ola. (El `FULL` del FLST es de FO4; Skyrim no lo trae.)
            If fo4c IsNot Nothing Then
                Borradores.EscribirCampoOQuitar(TextBoxName.Text.Length > 0,
                                                Sub() fo4c.Name = TextBoxName.Text,
                                                Sub() fo4c.NamePresente = False)
            End If
        Catch ex As Exception
            Logger.LogLazy(Function() $"[FLST-EDITOR] commit falló: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
        Dim igual As Boolean = False
        If _toma.Base IsNot Nothing Then igual = _draft.ContentEquals(_toma.Base)
        _draft.IsModified = _toma.Sucio(igual)
        _mainForm.RegisterFlstDraft(_draft)
        Return True
    End Function

    Private Sub OnCampo(sender As Object, e As EventArgs)
        If _cargando Then Return
        Commit()
    End Sub

    ''' <summary>⛔⛔ OK SOBRE UN BORRADOR NUEVO Y SIN TOCAR NO REGISTRA NADA. Sin esta guarda,
    ''' abrir el editor y apretar OK escribía un FLST VACÍO al .esp con EditorID automático. Es la
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
                Dim enBlanco = FlstDraft.Nuevo(_draft.FormID, _game)
                intacto = enBlanco IsNot Nothing AndAlso _draft.ContentEquals(enBlanco)
            Catch ex As Exception
                Logger.LogLazy(Function() $"[FLST-EDITOR] guarda del OK: no se pudo construir el record en blanco: {{ex.GetType().Name}}: {{ex.Message}}")
            End Try
            If intacto Then
                ' El usuario no hizo un record: apretó OK sobre nada.
                _toma.Abandonar()
                DialogResult = DialogResult.Cancel : Close() : Return
            End If
        End If
        If Not Commit() Then
            MessageBox.Show(Me, "The edit could not be written to the record — nothing was accepted.",
                            "Form List Editor", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        If TextBoxEdid.Text.Trim().Length = 0 Then
            TextBoxEdid.Text = FlstDraft.EditorIdPrefix & $"{_draft.FormID And &HFFFUI:X3}"
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

    Private Sub FormListEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If DialogResult <> DialogResult.OK Then _toma.Abandonar()
    End Sub

End Class
