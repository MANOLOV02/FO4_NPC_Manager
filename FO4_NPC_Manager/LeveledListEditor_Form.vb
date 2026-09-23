Imports System.Linq
Imports System.Windows.Forms
Imports FO4_Base_Library

''' <summary>El editor de una lista por nivel (<c>LVLI</c>): nueva, clonada de una plantilla, override
''' de una que ya existe, o una propia que se vuelve a abrir.
'''
''' <para>⛔⛔ <b>ERA UN RECOLECTOR DE SEIS VALORES, NO UN EDITOR.</b> Devolvía nombre + tres banderas
''' + Chance None + Max Count, y el <see cref="LeveledListDraft"/> lo construía el llamador. Por eso era
''' la única de las ocho clases con borrador sin override explícito y sin forma de reabrir una lista
''' propia: el modo se decidía afuera y no había puerta que lo dijera.</para>
'''
''' <para>⛔⛔ <b>EL EDITOR NO TOCA LOS LIBROS DE SESIÓN DEL SELECTOR DE ATUENDOS.</b> Ese diálogo lleva
''' dos —las que creó él y las que promovió— y de ellos depende que cancelarlo sea seguro. Acá se
''' devuelve <see cref="ResultOrigen"/> y ARCHIVA EL LLAMADOR, que es donde la ley vive hoy: registrar y
''' apuntar ocurren juntos, en un solo lugar. Si el editor archivara por su cuenta, la ley nacería en
''' dos sedes — y la tercera puerta (<c>Edit mine…</c>) es la peligrosa: ADOPTA una lista que YA existía,
''' así que no va a NINGÚN libro. Meterla en «creadas» haría que cancelar el selector le borre al usuario
''' una lista que hizo y aceptó antes.</para></summary>
Public Class LeveledListEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    ''' <summary>De dónde salió el objetivo con el que se acepta. Lo mira el llamador para saber a cuál
    ''' de sus libros va —o a ninguno—.</summary>
    Public Enum OrigenDelObjetivo
        ''' <summary>La creó este editor (en blanco o desde una plantilla) ⇒ al libro de las CREADAS.</summary>
        Creada = 0
        ''' <summary>Existía y este editor la promovió a override ⇒ al libro de las PROMOVIDAS.</summary>
        Promovida = 1
        ''' <summary>Ya era un borrador antes de abrir este editor ⇒ a NINGÚN libro: no es nuestra.</summary>
        Adoptada = 2
    End Enum

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _game As Canon.WbGame
    Private _draft As LeveledListDraft
    Private _toma As TomaDeBorrador(Of LeveledListDraft)
    Private _origen As OrigenDelObjetivo = OrigenDelObjetivo.Creada
    Private _cargando As Boolean

    ''' <summary>El FormID con el que se acepta. 0 si no hay objetivo.</summary>
    Public ReadOnly Property ResultLvliFormID As UInteger
        Get
            Return If(_draft Is Nothing, 0UI, _draft.FormID)
        End Get
    End Property

    ''' <summary>A qué libro del llamador va el objetivo. Ver el ⛔ de la clase.</summary>
    Public ReadOnly Property ResultOrigen As OrigenDelObjetivo
        Get
            Return _origen
        End Get
    End Property

    Public Sub New(mainForm As MainForm)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        InitializeComponent()
        _mainForm = mainForm
        _game = Canon.CanonBridge.SessionGame()
        _toma = New TomaDeBorrador(Of LeveledListDraft)(
            buscar:=AddressOf _mainForm.TryGetLeveledListDraft,
            registrar:=Sub(d) _mainForm.RegisterLeveledListDraft(d),
            bajar:=Sub(fid) _mainForm.UnregisterLeveledListDraft(fid),
            idDe:=Function(d) d.FormID,
            construirBase:=AddressOf ConstruirBaseDeDisco)

        ' ⛔ El «Max Count» (LVLM) sólo existe en Fallout 4 — en Skyrim ese subrecord no está en el
        ' formato. La fila se SACA en vez de deshabilitarse: una fila AutoSize con el control oculto
        ' colapsa a 0 px, y dejar un campo muerto en pantalla afirma que el juego lo tiene.
        If _game <> Canon.WbGame.Fallout4 Then
            GridCampos.Controls.Remove(LabelMaxCount)
            GridCampos.Controls.Remove(NumericMaxCount)
            LabelMaxCount.Dispose()
            NumericMaxCount.Dispose()
        End If

        AddHandler ButtonNewBlank.Click, AddressOf OnNuevoEnBlanco
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnNuevoDesdePlantilla
        AddHandler ButtonOverrideExisting.Click, AddressOf OnOverrideExistente
        AddHandler ButtonEditMine.Click, AddressOf OnEditarElMio
        AddHandler TextBoxName.TextChanged, AddressOf OnCampo
        AddHandler CheckBoxCalcAllLevels.CheckedChanged, AddressOf OnCampo
        AddHandler CheckBoxCalcEachInCount.CheckedChanged, AddressOf OnCampo
        AddHandler CheckBoxUseAll.CheckedChanged, AddressOf OnCampo
        AddHandler NumericChanceNone.ValueChanged, AddressOf OnCampo
        If _game = Canon.WbGame.Fallout4 Then AddHandler NumericMaxCount.ValueChanged, AddressOf OnCampo
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel

        ' Se abre sobre una lista NUEVA, que es el gesto con el que este editor nació («New LVL…»).
        ' ⛔ `registrar:=False`: un borrador nuevo no se commitea al abrir. Registrar NO es inocuo — la
        ' fase 2d emite TODO borrador sucio, referenciado o no —, así que abrir y cerrar escribiría una
        ' lista vacía al .esp.
        EmpezarEnBlanco(registrar:=False)
    End Sub

    Private Function ConstruirBaseDeDisco(fid As UInteger) As LeveledListDraft
        If fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Return OverridePristino(fid, _mainForm.PluginManagerForEditor)
    End Function

    ''' <summary>EL BORRADOR OVERRIDE PRISTINO de una LVLI real: el record del archivo, copiado. Es LA
    ''' FÁBRICA, y por eso la usan los DOS lados — el que arma lo que el usuario edita Y
    ''' <see cref="TomaDeBorrador(Of TD)"/> para la LÍNEA DE BASE. Con dos construcciones distintas el
    ''' record abre SUCIO sin que el usuario lo toque.</summary>
    ''' <summary>⛔⛔ <b>NO SE REEMPLAZA POR <c>MainForm.BuildLeveledOverrideDraftFromReal</c></b>,
    ''' aunque las dos construyan «el override de un LVLI real» y la duplicación salte a la vista.
    ''' Las diferencias están MEDIDAS y son las que importan:</summary>
    ''' <remarks><list type="number">
    ''' <item>la del <c>MainForm</c> <b>REGISTRA</b> (<c>RegisterLeveledListDraft</c>) y devuelve el
    ''' borrador que YA hubiera bajo ese FormID; ésta no hace ninguna de las dos cosas;</item>
    ''' <item>ésta deja <c>IsModified = False</c>.</item></list>
    ''' <para>Y eso es obligatorio porque ésta es TAMBIÉN el <c>construirBase</c> de
    ''' <c>TomaDeBorrador</c>: la LÍNEA DE BASE contra la que se decide si el borrador quedó sucio.
    ''' Con la del <c>MainForm</c> en su lugar, pedir la base REGISTRARÍA un borrador, y encima
    ''' devolvería el borrador VIVO en vez del record del disco — así que la comparación daría
    ''' «limpio» siempre y el trabajo del usuario no se guardaría. Es la misma ley que
    ''' <c>TextureSetEditor_Form.OverridePristino</c> documenta con su caso medido: la fábrica del
    ''' borrador y la de la base tienen que ser LA MISMA función, y por eso no puede registrar.</para>
    ''' <para>El caso que la otra resuelve adentro, esta puerta ya lo cubre afuera:
    ''' <c>CargarComoOverride</c> pregunta <c>TryGetLeveledListDraft</c> ANTES y adopta el que ya
    ''' existía.</para></remarks>
    Friend Shared Function OverridePristino(fid As UInteger, plugins As PluginManager) As LeveledListDraft
        If plugins Is Nothing OrElse fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Dim rec = plugins.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "LVLI" Then Return Nothing
        Dim d = LeveledListDraft.Edicion(rec, plugins)
        If d Is Nothing OrElse d.Record Is Nothing Then Return Nothing
        d.IsModified = False
        Return d
    End Function

    '==============================================================================================
    ' LAS CUATRO PUERTAS DE OBJETIVO
    '==============================================================================================

    Private Sub OnNuevoEnBlanco(sender As Object, e As EventArgs)
        EmpezarEnBlanco(registrar:=False)
    End Sub

    Private Sub EmpezarEnBlanco(registrar As Boolean)
        Dim d = LeveledListDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
        If d Is Nothing Then Return
        TomarYVolcar(d, OrigenDelObjetivo.Creada, registrar)
    End Sub

    ''' <summary>«New from template…»: una lista NUEVA con el contenido de una que ya existe.</summary>
    Private Sub OnNuevoDesdePlantilla(sender As Object, e As EventArgs)
        Dim fid = ElegirLvliReal("Pick the leveled list to copy")
        If fid = 0UI Then Return
        Dim rec = _mainForm.PluginManagerForEditor.GetRecord(fid)
        If rec Is Nothing Then Return
        Dim d = LeveledListDraft.Edicion(rec, _mainForm.PluginManagerForEditor)
        If d Is Nothing OrElse d.Record Is Nothing Then
            MessageBox.Show(Me, "Could not parse that leveled list.", "Leveled list",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        ' ⛔ Copiar el ÁRBOL y darle identidad NUEVA: la ley del clon es `ReidentificarComoClon`, la
        ' misma que usan las otras cinco clases. Sin ella el «clon» nacería con la identidad del origen.
        Dim fidNuevo = _mainForm.AllocateDraftFormID()
        d.FormID = fidNuevo
        d.IsOverride = False
        d.IsNew = True
        Borradores.ReidentificarComoClon(d.Record, fidNuevo)
        TomarYVolcar(d, OrigenDelObjetivo.Creada)
    End Sub

    ''' <summary>«Override existing…»: se edita una LVLI que YA existe, conservando su identidad.</summary>
    Private Sub OnOverrideExistente(sender As Object, e As EventArgs)
        Dim fid = ElegirLvliReal("Pick the leveled list to override")
        If fid = 0UI Then Return
        CargarComoOverride(fid)
    End Sub

    ''' <summary>«Edit mine…»: las listas PROPIAS — borradores de esta sesión y las ya GUARDADAS.
    ''' <para>⛔⛔ Lo que se elija acá se ADOPTA: ya era un borrador antes de abrir este editor, así que
    ''' NO es nuestro y no va a ningún libro del llamador. Ver el ⛔ de la clase.</para></summary>
    Private Sub OnEditarElMio(sender As Object, e As EventArgs)
        Dim entradas = _mainForm.LeveledListDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "LVLI", .PluginName = If(d.IsOverride, "(override)", "(new)")}).ToList()
        entradas.AddRange(BorradoDeBorradores.EntradasPropias(_mainForm, "LVLI", entradas))
        If entradas.Count = 0 Then
            MessageBox.Show(Me,
                            "No leveled lists of yours yet — neither drafts in this session nor saved " &
                            "ones in your plugin. Use New, New from template… or Override existing… first.",
                            "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As FormIdPicker_Form = FormIdPicker_Form.ParaBorradores(
                _mainForm, Me, {"LVLI"}, "Edit my leveled list (drafts + saved)",
                0UI, False, entradas,
                formIdFilter:=Function(fid) entradas.Any(Function(x) x.FormID = fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If _draft IsNot Nothing AndAlso _draft.FormID = dlg.SelectedFormID Then
                MessageBox.Show(Me, "That is the leveled list you are already editing in this window.",
                                "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ' ⛔ Un borrador abierto en OTRA ventana no se abre dos veces: el borrador ES el record y se
            ' comparte por referencia. Acá importa de verdad porque el selector de atuendos navega por
            ' las listas con su propia pila, que también marca.
            If Borradores.EstaTomado(dlg.SelectedFormID) Then
                MessageBox.Show(Me, "That leveled list is already open elsewhere. Close it first.",
                                "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            Dim ya = _mainForm.TryGetLeveledListDraft(dlg.SelectedFormID)
            If ya IsNot Nothing Then
                TomarYVolcar(ya, OrigenDelObjetivo.Adoptada)
                Return
            End If
            ' Un GUARDADO todavía no tiene borrador: se abre como override de su record.
            CargarComoOverride(dlg.SelectedFormID)
        End Using
    End Sub

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
    Private Function ElegirLvliReal(titulo As String) As UInteger
        Dim propias = BorradoDeBorradores.EntradasPropias(_mainForm, "LVLI", Nothing)
        ' ⛔⛔ CON DUEÑO. Este editor TIENE `_toma` y ya implementa el contrato, así que la condición de
        ' `ParaBorradoresSinDueno` —«el sitio no tiene ningún borrador tomado»— NO se cumple. Es el mismo
        ' error que esta ola le corrigió a `ArmaEditor_Form.PickTxstInto`, y acá pesa MÁS porque las clases
        ' COINCIDEN: `EntradasPropias` puede listar el record que este editor tiene tomado, y sin dueño
        ' `Planear` no sabe que lo tomó ESTA ventana ⇒ contesta «already open in another editor window»
        ' —mentira, es ésta— y niega un gesto que la ley concede («tomado por MÍ se permite»).
        Using dlg As FormIdPicker_Form = If(propias.Count = 0,
                New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"LVLI"}, titulo, 0UI, allowNull:=False),
                FormIdPicker_Form.ParaBorradores(_mainForm, Me, {"LVLI"}, titulo, 0UI, False, propias))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>Tomar <paramref name="fid"/> como override. Si YA hay borrador bajo ese FormID, el
    ''' BORRADOR MANDA sobre el disco y se ADOPTA — no es nuestro.</summary>
    Private Sub CargarComoOverride(fid As UInteger)
        Dim ya = _mainForm.TryGetLeveledListDraft(fid)
        If ya IsNot Nothing Then
            TomarYVolcar(ya, OrigenDelObjetivo.Adoptada)
            Return
        End If
        Dim d = OverridePristino(fid, _mainForm.PluginManagerForEditor)
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that leveled list.", "Leveled list",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d, OrigenDelObjetivo.Promovida)
    End Sub

    ''' <summary>Cambiar el objetivo del editor.
    ''' <para>⛔⛔ ABANDONA LA TOMA ANTERIOR PRIMERO, o cada vuelta por una puerta deja el borrador
    ''' anterior REGISTRADO y se van acumulando listas vacías.</para></summary>
    Private Sub TomarYVolcar(d As LeveledListDraft, origen As OrigenDelObjetivo,
                             Optional registrar As Boolean = True)
        If d Is Nothing OrElse d.Record Is Nothing Then Return
        Dim snap As LeveledListDraft = Nothing
        Try
            snap = d.Clone()
        Catch
        End Try
        ' ⛔⛔ rev-37 · EL CAMBIO DE OBJETIVO ES DESTRUCTIVO Y ANTES NO AVISABA: la ley y su
        ' porqué están en `TomaDeBorrador.CambiarDestruyeTrabajo`. El predicado vive allá para que
        ' un testigo lo corra; el cartel, en `BorradoDeBorradores`, para que los cinco editores
        ' digan lo mismo.
        If _toma.CambiarDestruyeTrabajo(d, _draft IsNot Nothing AndAlso _draft.IsDirty) AndAlso
           Not BorradoDeBorradores.ConfirmarCambioDeObjetivo(Me, "leveled list", "Leveled list") Then Return
        ' ⛔ LA PUERTA ÚNICA, Y ES MEDIBLE: abandonar + tomar + registrar viven en
        ' `TomaDeBorrador.CambiarObjetivo`, afuera del formulario, porque adentro ningún testigo
        ' puede correrla — cada OK abre validaciones con `MessageBox` y un modal cuelga al gate.
        ' Mismo movimiento que `PlanDeCierreDeListas` y `FilasPropiasDeValue1`.
        If Not _toma.CambiarObjetivo(d, snap, registrar) Then
            MessageBox.Show(Me, "That leveled list is already open elsewhere. Close it first.",
                            "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _draft = d
        _origen = origen
        Volcar()
        If registrar Then Commit()
        ActualizarBanner()
    End Sub

    '==============================================================================================
    ' Volcado y commit
    '==============================================================================================

    Private Sub Volcar()
        If _draft?.Record Is Nothing Then Return
        _cargando = True
        Try
            Dim r = _draft.Record
            Dim edid = If(r.EditorID, "")
            ' ⛔ Y LA OTRA MITAD: el recorte del prefijo es SÓLO para los nuevos. Sobre un override que sí
            ' lo tiene en el archivo, recortarlo acá y no volver a pegarlo en `Commit` le COMERÍA el
            ' prefijo al record real — el mismo renombre, en la otra dirección.
            TextBoxName.Text = If(_draft.IsNew AndAlso edid.StartsWith(LeveledListDraft.EditorIdPrefix, StringComparison.OrdinalIgnoreCase),
                                  edid.Substring(LeveledListDraft.EditorIdPrefix.Length), edid)
            ' ⛔ Y LA PANTALLA TIENE QUE DECIR LO MISMO QUE EL RECORD. Con el EditorID de un override ya
            ' conservado, el rótulo seguía anunciando el prefijo `npcm_LVLI_` y la caja seguía editable: la
            ' ventana declaraba un prefijo que no aplica y dejaba RENOMBRAR un record real tipeando. Es la
            ' misma rama que `MswpSubEditor_Form` ya aplica («OVERRIDE: the box holds the kept EDID
            ' verbatim (read-only)»).
            LabelName.Text = If(_draft.IsNew, "EDID: " & LeveledListDraft.EditorIdPrefix, "EDID (kept):")
            TextBoxName.ReadOnly = Not _draft.IsNew
            CheckBoxCalcAllLevels.Checked = r.FlagsCalculateFromAllLevelsPlayerSLevel
            CheckBoxCalcEachInCount.Checked = r.FlagsCalculateForEachItemInCount
            CheckBoxUseAll.Checked = r.FlagsUseAll
            NumericChanceNone.Value = ClampNum(r.ChanceNone, NumericChanceNone)
            Dim fo4 = TryCast(r, Canon.LvliFO4)
            If fo4 IsNot Nothing AndAlso _game = Canon.WbGame.Fallout4 Then
                NumericMaxCount.Value = ClampNum(fo4.MaxCount, NumericMaxCount)
            End If
            LabelEntries.Text = $"{r.LeveledListEntries.Count} entry(ies). Entries are added from the outfit picker."
        Finally
            _cargando = False
        End Try
    End Sub

    Private Shared Function ClampNum(v As Integer, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

    ''' <summary>El EditorID que corresponde al objetivo: con prefijo si es NUEVO, verbatim si es
    ''' OVERRIDE.
    ''' <para>⛔ UNA SOLA SEDE porque hay TRES lugares que lo arman —`Volcar`, `Commit` y la
    ''' validación de `OnOk`— y con tres copias basta que una se olvide para renombrar el record
    ''' del usuario o para comerle el prefijo.</para></summary>
    Private Function EdidQueCorresponde() As String
        Dim nombre = TextBoxName.Text.Trim()
        If _draft IsNot Nothing AndAlso Not _draft.IsNew Then Return nombre
        Return LeveledListDraft.EditorIdPrefix & nombre
    End Function

    Private Function Commit() As Boolean
        If _draft?.Record Is Nothing OrElse _cargando Then Return False
        Try
            Dim r = _draft.Record
            ' ⛔⛔ EL PREFIJO ES DE LOS NUEVOS, NO DE LOS OVERRIDE, y acá se pegaba INCONDICIONAL.
            ' `Commit` corre AL ABRIR (`TomarYVolcar` → `If registrar Then Commit()`), así que override-ar
            ' `LL_Raider_Weapons` y aceptar SIN TOCAR NADA lo escribía al .esp como
            ' `npcm_LVLI_LL_Raider_Weapons`: un RENOMBRE que el usuario no pidió. Y encima
            ' `ContentEquals(Base)` daba False por ese solo cambio ⇒ `IsModified` ⇒ la fase 2d lo emitía
            ' igual. Los otros cuatro editores ya tienen esta rama —`MswpSubEditor_Form.OnOk`: «OVERRIDE:
            ' the box holds the kept EDID verbatim»— y el `OnOk` de ESTE archivo ya la declaraba por
            ' escrito («Un override conserva el EditorID del record REAL»): el comentario decía una cosa y
            ' el código hacía la otra, en el mismo archivo.
            r.EditorID = EdidQueCorresponde()
            r.FlagsCalculateFromAllLevelsPlayerSLevel = CheckBoxCalcAllLevels.Checked
            r.FlagsCalculateForEachItemInCount = CheckBoxCalcEachInCount.Checked
            r.FlagsUseAll = CheckBoxUseAll.Checked
            r.ChanceNone = CByte(NumericChanceNone.Value)
            Dim fo4 = TryCast(r, Canon.LvliFO4)
            If fo4 IsNot Nothing AndAlso _game = Canon.WbGame.Fallout4 Then fo4.MaxCount = CByte(NumericMaxCount.Value)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LVLI-EDITOR] commit falló: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
        Dim igual As Boolean = False
        If _toma.Base IsNot Nothing Then igual = _draft.ContentEquals(_toma.Base)
        _draft.IsModified = _toma.Sucio(igual)
        _mainForm.RegisterLeveledListDraft(_draft)
        Return True
    End Function

    Private Sub OnCampo(sender As Object, e As EventArgs)
        If _cargando Then Return
        Commit()
    End Sub

    Private Sub ActualizarBanner()
        If _draft Is Nothing Then
            LabelBanner.Text = "No target — use New, New from template… or Override existing…"
        Else
            LabelBanner.Text = If(_draft.IsOverride, $"OVERRIDE of 0x{_draft.FormID:X8}", "NEW record")
        End If
    End Sub

    '==============================================================================================
    ' OK / Cancel
    '==============================================================================================

    ''' <summary>⛔ OK SOBRE UN BORRADOR NUEVO Y SIN NOMBRE NO REGISTRA NADA: abrir el editor y aceptar
    ''' escribiría una LVLI vacía al .esp con EditorID automático. Es la misma guarda que los editores de
    ''' TXST y de head parts ya aplican.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If _draft Is Nothing Then
            DialogResult = DialogResult.Cancel : Close() : Return
        End If
        If TextBoxName.Text.Trim().Length = 0 Then
            MessageBox.Show(Me, "Enter a name for the leveled list.", "Leveled list",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If
        ' ⛔ SÓLO PARA LOS NUEVOS. Un override conserva el EditorID del record REAL, que
        ' `IsLeveledEditorIdAvailable` reporta como TOMADO porque está en el plugin: chequearlo
        ' rechazaría todo override al aceptar. Un override no elige nombre: no hay nada que validar.
        Dim edid = EdidQueCorresponde()
        If _draft.IsNew AndAlso Not _mainForm.IsLeveledEditorIdAvailable(edid) Then
            MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another name.",
                            "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
        If Not Commit() Then
            MessageBox.Show(Me, "The edit could not be written to the record — nothing was accepted.",
                            "Leveled list", MessageBoxButtons.OK, MessageBoxIcon.Error)
            DialogResult = DialogResult.None
            Return
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

    Private Sub LeveledListEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If DialogResult <> DialogResult.OK Then _toma.Abandonar()
    End Sub

    '==============================================================================================
    ' EL CONTRATO CON LA SEDE DE BAJA — `BorradoDeBorradores.IDuenoDeBorradores`
    '==============================================================================================

    Private Function FormIdTomado() As UInteger Implements BorradoDeBorradores.IDuenoDeBorradores.FormIdTomado
        Return If(_draft Is Nothing, 0UI, _draft.FormID)
    End Function

    ''' <summary>Las ENTRADAS de la lista no se editan acá —se agregan desde el selector de atuendos—, así
    ''' que este editor no tiene ningún buffer sin volcar.</summary>
    Private Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String) _
            Implements BorradoDeBorradores.IDuenoDeBorradores.ReferenciasNoVolcadas
        Return Enumerable.Empty(Of String)()
    End Function

    ''' <summary>POSTCONDICIÓN de la baja: se SUELTA la toma y el editor queda sin objetivo.
    ''' <para>⛔ `Soltar`, no `Abandonar`: abandonar REPONDRÍA lo que el usuario acaba de borrar, por
    ''' encima del `MarkRecordForRemoval` del mismo gesto.</para></summary>
    Private Sub TrasLaBaja(formID As UInteger) Implements BorradoDeBorradores.IDuenoDeBorradores.TrasLaBaja
        If _draft Is Nothing OrElse _draft.FormID <> formID Then Return
        _toma.Soltar()
        _draft = Nothing
        ActualizarBanner()
    End Sub

End Class
