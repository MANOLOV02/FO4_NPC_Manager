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

        AddHandler TextBoxEdid.TextChanged, AddressOf OnCampo
        AddHandler TextBoxName.TextChanged, AddressOf OnCampo
        AddHandler ButtonCloneFrom.Click, AddressOf OnClonarDeUnaQueExiste
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
            LabelBanner.Text = If(_draft.IsOverride, $"OVERRIDE of 0x{_draft.FormID:X8}", "NEW record")
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

    ''' <summary>«Arrancar de una lista que ya existe»: llena ESTE borrador con el contenido de otro
    ''' FLST. ⛔ NO es «hacer un record nuevo a partir de ese otro» — el destino ya tiene identidad, y
    ''' puede ser el OVERRIDE de un FLST que existe.</summary>
    ''' <summary>El camino de BAJA de un borrador ofrecido en este selector. ⛔ Es la mitad
    ''' obligatoria de ofrecerlo: sin él el botón «Delete / Revert…» ni se ve y el borrador queda sin
    ''' salida. Misma ley que en el editor de head parts.</summary>
    Private Function OnBorrarEntradaDeBorradorFlst(entry As FormIdPickerEntry) As Boolean
        If entry Is Nothing OrElse entry.FormID = 0UI Then Return False
        Return BorradoDeHeadParts.BorrarORevertir(_mainForm, Me, entry)
    End Function

    Private Sub OnClonarDeUnaQueExiste(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing Then Return
        ' ⛔ AVISO SÍ/NO CON «NO» POR DEFECTO. Este gesto REEMPLAZA todo lo que el borrador tiene — los
        ' miembros que el usuario agregó a mano incluidos — y no hay deshacer. Es la misma ley que el
        ' usuario decidió para el otro cartel de esta ola: genérico, sin contar nada, y el «sí» lo hace
        ' igual. Sólo se pregunta si hay algo que perder.
        If _draft.Record.Miembros().Count > 0 Then
            If MessageBox.Show(Me,
                               "This replaces everything in this form list with the contents of the one you " &
                               "pick. Anything you added here is lost. Continue?",
                               "Start from an existing list", MessageBoxButtons.YesNo,
                               MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) <> DialogResult.Yes Then Return
        End If
        ' ⛔ OFRECE LOS PROPIOS — borradores y guardados — Y LLEVA SU CAMINO DE BAJA, como todo
        ' selector de los editores que ya funcionan. Acá iba `Nothing, Nothing, Nothing`: sólo listaba
        ' las FLST del orden de carga, así que «arrancar de una que ya existe» no podía arrancar de una
        ' del propio usuario — ni de la que acababa de hacer, ni de la que guardó ayer.
        Dim propias As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.FlstDrafts()
            If d?.Record Is Nothing OrElse d.FormID = _draft.FormID Then Continue For
            propias.Add(New FormIdPickerEntry With {.FormID = d.FormID, .EditorID = d.Record.EditorID,
                                                    .DisplayName = d.Record.EditorID, .Signature = "FLST",
                                                    .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        Dim yaEstan As New HashSet(Of UInteger)(propias.Select(Function(x) x.FormID))
        For Each g In _mainForm.GetAuthoredRecords("FLST")
            If yaEstan.Contains(g.FormID) OrElse g.FormID = _draft.FormID Then Continue For
            propias.Add(New FormIdPickerEntry With {.FormID = g.FormID, .EditorID = g.EditorID,
                                                    .DisplayName = g.DisplayName, .Signature = "FLST",
                                                    .PluginName = "(saved)"})
        Next
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"FLST"},
                                           "Start from this form list", 0UI, False,
                                           If(propias.Count = 0, Nothing, propias), Nothing,
                                           AddressOf OnBorrarEntradaDeBorradorFlst)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim rec = _mainForm.PluginManagerForEditor.GetRecord(dlg.SelectedFormID)
            If rec Is Nothing Then Return
            ' ⛔⛔ LA IDENTIDAD DEL DESTINO SE GUARDA ANTES Y SE REPONE DESPUÉS. `FlstDraft.Clon` pasa
            ' por `Borradores.ReidentificarComoClon`, que es la ley de un record NUEVO y entre otras
            ' cosas BORRA el `EditorId` del contexto. Sobre un borrador que era override de un FLST
            ' existente, eso lo dejaba sin EditorID, y el emisor publica sus avisos con el EditorId del
            ' contexto — o sea con la identidad de otro record.
            Dim edidDestino = If(_draft.Record.EditorID, "")
            ' Las banderas del encabezado viven en la VISTA canónica, no en la interfaz `IFlst`: la
            ' interfaz describe los campos del record y éstas son de su identidad.
            Dim vistaDestino = TryCast(_draft.Record, Canon.CanonRecordView)
            Dim flagsDestino As UInteger = If(vistaDestino Is Nothing, 0UI, vistaDestino.RecordFlags)
            ' ⛔ Copiar el ÁRBOL, no los miembros a mano: trae también lo que este editor no muestra.
            Dim nuevo = FlstDraft.Clon(rec, _mainForm.PluginManagerForEditor, _draft.FormID)
            If nuevo Is Nothing Then Return
            Borradores.ReidentificarComoOverride(nuevo.Record, _draft.FormID, edidDestino, flagsDestino)
            _draft.Record = nuevo.Record
            ' El EditorID también va al SUBRECORD, no sólo al contexto: el `EDID` que se emite sale del
            ' árbol, y el árbol que acaba de entrar es el del ORIGEN.
            If edidDestino.Length > 0 Then _draft.Record.EditorID = edidDestino
            Volcar()
            Commit()
        End Using
    End Sub

    Private Sub OnAgregar(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing Then Return
        Dim sigs = If(_soloRazas, New String() {"RACE"}, New String() {})
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, sigs,
                                           If(_soloRazas, "Add a race", "Add a record"),
                                           0UI, False, Nothing, Nothing, Nothing)
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
    ''' dentro de un `Try` y si falla deja la guarda sin correr.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If _draft IsNot Nothing AndAlso _draft.IsNew Then
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
