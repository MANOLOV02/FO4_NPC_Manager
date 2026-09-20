Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>El editor de UNA condición (<c>CTDA</c>). Genérico a propósito: lo que edita es la
''' estructura del formato, no «la condición de un head part», así que sirve igual el día que se quiera
''' editar las de QUST/PERK/IDLE.
'''
''' <para>⛔⛔ <b>EL OPERADOR TIENE SEIS VALORES.</b> La tabla y el porqué —incluido el error que ya se
''' cometió leyendo la rama de PINTADO del control en vez del campo, y los 13.444 records del corpus que
''' lo refutan— están en <see cref="CondicionesDeHeadPart"/>. Acá se consumen, no se re-deciden.</para>
'''
''' <para>⛔ <b>El nombre de la función sale de la TABLA GENERADA</b> (479 en Fallout 4, 402 en Skyrim),
''' emitida del <c>.pas</c> de xEdit por <c>Tools\CanonLayoutGen</c>. No hay una lista escrita a mano:
''' 479 nombres copiados son 479 oportunidades de derivar de la fuente.</para>
'''
''' <para>⛔ <b>Y el TIPO de cada parámetro también sale de la tabla</b> (<c>Params</c>), no de un
''' <c>Select Case</c> local: es la misma tabla que usa el LECTOR para decidir qué rama de la unión
''' decodifica. Lo que este editor muestra es la rama que el árbol REALMENTE trae —se pregunta por los
''' <c>*Presente</c>—, así que nunca escribe una rama que el record no tenía.</para>
'''
''' <para>⛔ <b>LAS CINCUENTA CLASES DE PARÁMETRO, no cinco.</b> Acá había un hueco declarado: cinco
''' ramas escritas a mano (Quest, Quest Stage, Integer, Float, Alias) y las otras 45 mostradas en crudo.
''' Ya no: <c>wbConditionParameters</c> (<c>wbDefinitionsFO4.pas:5124-5180</c>,
''' <c>wbDefinitionsTES5.pas:3813-3880</c>) declara las 50 ramas con su clase, su rótulo, sus firmas y
''' sus enumerados, el generador las emite y <see cref="Canon.CanonInterpretacion.RamaDeParametroDeCondicion"/>
''' las sirve. Este editor sólo pregunta qué rama le toca y arma el control: selector de record con las
''' firmas declaradas para las 33 de FormID, combo con los valores del enum para las 9 (13 en Skyrim),
''' caja de texto para las escalares. Cero listas a mano.</para>
'''
''' <para>⚠️ <b>EL ÚNICO HUECO QUE QUEDA</b>, y no es de la app: Skyrim declara
''' <c>ptVATSValueFunction</c> nombrando un <c>wbVATSValueFunctionEnum</c> que xEdit <b>no define en
''' ningún archivo de definiciones</b> — es un resto de FO3/FNV. Alcanza a UNA de las 402 funciones
''' (<c>GetVATSValue</c>, índice 407), cuyo parámetro 1 es además una unión anidada. Esas dos ramas
''' degradan a «se muestra y no se toca», y <see cref="Canon.CanonInterpretacion.RamasDeParametroSinResolver"/>
''' lo dice por su nombre en vez de callarlo.</para></summary>
Public Class ConditionsEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _game As Canon.WbGame
    Private ReadOnly _cond As Canon.HdptFO4_Conditions

    ''' <summary>Los valores de <c>Run On</c> en el MISMO orden que los items del combo. El valor se lee
    ''' por índice contra esta lista y NO por <c>SelectedIndex</c> crudo: índice = valor sólo mientras el
    ''' enum sea 0..N contiguo, y eso es una coincidencia del esquema de hoy, no un contrato.</summary>
    ''' <summary>Los índices de función en el orden de los items del combo. El combo está ordenado por
    ''' NOMBRE, así que el índice del item no es el índice de la función: sin esta lista habría que volver
    ''' a parsear el rótulo, que es de donde venía el defecto.</summary>
    Private ReadOnly _clavesDeFuncion As New List(Of Integer)
    Private _funcionCrudaFueraDeTabla As Integer = -1
    ''' <summary>La función con la que el record ABRIÓ. Si al aceptar cambió, los parámetros se ponen en
    ''' cero: ver el ⛔ de <see cref="OnOk"/>.</summary>
    Private ReadOnly _funcionOriginal As Integer

    Private ReadOnly _clavesDeRunOn As New List(Of UInteger)
    Private _runOnCrudoFueraDeTabla As UInteger = 0UI
    Private _hayEntradaDeRunOnCruda As Boolean = False
    Private _cargando As Boolean
    ''' <summary>El valor del byte <c>Type</c> con el que se abrió, para poder devolverlo intacto si el
    ''' operador que trae no es uno de los seis que xEdit nombra.</summary>
    Private ReadOnly _tipoOriginal As Byte

    ''' <summary>La rama que cada parámetro tiene MOSTRADA ahora mismo — la de la función elegida en el
    ''' combo, no la que el record trajo. Se recalcula en <see cref="RefrescarParametros"/> y es lo que
    ''' <see cref="OnOk"/> usa para saber qué escribir y dónde.
    ''' <para>⛔ Índice 0 = parámetro 1, índice 1 = parámetro 2. Se guarda en vez de recalcularse en el
    ''' OK porque si los dos lados preguntaran por separado podrían discrepar: el usuario puede cambiar
    ''' el combo de función entre el refresco y el OK, y entonces el control que llenó y la rama donde se
    ''' escribe no serían la misma.</para></summary>
    Private ReadOnly _ramas(1) As Canon.CanonInterpretacion.RamaDeParametro

    ''' <summary>Las claves de los combos de enumerado, en el orden de sus items. Igual que con el combo
    ''' de funciones: el índice del item NO es el valor — los del <c>Misc Stat</c> son hashes de 32 bits
    ''' y los del <c>Furniture Entry</c> son bits—, así que la lista de claves es obligatoria.</summary>
    Private ReadOnly _clavesDeEnum As List(Of Long)() = {New List(Of Long), New List(Of Long)}

    ''' <summary>El valor que el record traía cuando NO es ninguno de los del enumerado, y la entrada
    ''' extra que lo representa. Mismo trato que el operador inválido y el Run On fuera de tabla: un
    ''' combo cerrado que no lo represente lo reescribiría en silencio.</summary>
    Private ReadOnly _crudoDeEnum(1) As Long

    Public Sub New(mainForm As MainForm, game As Canon.WbGame, cond As Canon.HdptFO4_Conditions)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        If cond Is Nothing Then Throw New ArgumentNullException(NameOf(cond))
        InitializeComponent()
        _mainForm = mainForm
        _game = game
        _cond = cond
        _tipoOriginal = cond.ConditionType
        _funcionOriginal = CInt(cond.ConditionFunction)

        LabelHint.Text =
            "The comparison operator has SIX values (xEdit's own table); 192 and 224 are the only two its " &
            "validator rejects. Both parameters — their type, the records they accept and the values of " &
            "the enumerated ones — come from xEdit's own condition-parameter table, not from a list " &
            "written here. Changing the function REBUILDS both parameters for the new function: the old " &
            "bytes describe the old parameter types, so they are not carried over."

        For Each kv In Canon.CanonInterpretacion.FuncionesDeCondicion(_game).OrderBy(Function(x) x.Value)
            ComboFunction.Items.Add($"{kv.Value}  [{kv.Key}]")
            _clavesDeFuncion.Add(CInt(kv.Key))
        Next
        ' Un índice de función que la tabla del juego no nombra se conserva con su propia entrada: es el
        ' mismo trato que el operador inválido y el `Run On` fuera de tabla.
        If Not _clavesDeFuncion.Contains(_funcionOriginal) Then
            _funcionCrudaFueraDeTabla = _funcionOriginal
            ComboFunction.Items.Add($"(function {_funcionOriginal} — not named by this game's table; kept as-is)")
        End If
        For Each o In CondicionesDeHeadPart.Operadores
            ComboOperator.Items.Add($"{o.Simbolo}   {o.Nombre}")
        Next
        ' ⛔ Si el record trae uno de los DOS inválidos, se agrega una entrada para ÉL: un combo cerrado
        ' que no lo represente lo reescribiría en silencio, que es el defecto de clase de esta ola.
        If Not CondicionesDeHeadPart.OperadorEsValido(_tipoOriginal) Then
            ComboOperator.Items.Add($"(raw {_tipoOriginal And CondicionesDeHeadPart.MascaraDeOperador} — xEdit calls this an unknown compare operator; kept as-is)")
        End If
        ' ⛔ Del ESQUEMA del juego, no de una lista a mano: Fallout 4 tiene ONCE y Skyrim OCHO, y
        ' 177 CTDA de Fallout 4 usan 8/9/10. Ver `CondicionesDeHeadPart.RunOnDe`.
        For Each r In CondicionesDeHeadPart.RunOnDe(_game)
            ComboRunOn.Items.Add($"{r.Valor} — {r.Nombre}")
            _clavesDeRunOn.Add(r.Valor)
        Next
        ' Y el valor que la tabla del juego no nombra se conserva con su propia entrada, igual que el
        ' operador inválido: el combo queda cerrado y no normaliza nada.
        If Not _clavesDeRunOn.Contains(_cond.ConditionRunOn) Then
            _runOnCrudoFueraDeTabla = _cond.ConditionRunOn
            ComboRunOn.Items.Add($"{_runOnCrudoFueraDeTabla} — (not named by this game's schema; kept as-is)")
            _hayEntradaDeRunOnCruda = True
        End If

        Volcar()
        AddHandler ComboFunction.SelectedIndexChanged, AddressOf OnFuncionCambiada
        AddHandler ButtonPickParam1.Click, AddressOf OnElegirParam1
        AddHandler ButtonPickParam2.Click, AddressOf OnElegirParam2
        AddHandler ButtonPickGlobal.Click, AddressOf OnElegirGlobal
        AddHandler CheckUseGlobal.CheckedChanged, AddressOf OnBanderaGlobalCambiada
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub Volcar()
        _cargando = True
        Try
            ' Función
            Dim iFun = _clavesDeFuncion.IndexOf(CInt(_cond.ConditionFunction))
            If iFun >= 0 Then
                ComboFunction.SelectedIndex = iFun
            ElseIf _funcionCrudaFueraDeTabla >= 0 Then
                ComboFunction.SelectedIndex = ComboFunction.Items.Count - 1
            End If
            ' Operador
            Dim op = CByte(_tipoOriginal And CondicionesDeHeadPart.MascaraDeOperador)
            Dim iOp = CondicionesDeHeadPart.Operadores.FindIndex(Function(o) o.Valor = op)
            ComboOperator.SelectedIndex = If(iOp >= 0, iOp, ComboOperator.Items.Count - 1)
            ' Banderas
            CheckOr.Checked = (_tipoOriginal And 1) <> 0
            CheckUseAliases.Checked = (_tipoOriginal And 2) <> 0
            CheckUseGlobal.Checked = (_tipoOriginal And 4) <> 0
            CheckUsePackdata.Checked = (_tipoOriginal And 8) <> 0
            CheckSwapSubject.Checked = (_tipoOriginal And 16) <> 0
            ' Valor de comparación
            If CheckUseGlobal.Checked Then
                TextBoxValue.Text = _mainForm.GetRecordDisplayNameForEditor(_cond.ConditionComparisonValueGlobal)
                TextBoxValue.Tag = _cond.ConditionComparisonValueGlobal
                TextBoxValue.ReadOnly = True
            Else
                TextBoxValue.Text = _cond.ConditionComparisonValueFloat.ToString(Globalization.CultureInfo.InvariantCulture)
                TextBoxValue.Tag = Nothing
                TextBoxValue.ReadOnly = False
            End If
            ButtonPickGlobal.Enabled = CheckUseGlobal.Checked
            ' Run On
            Dim r = CInt(_cond.ConditionRunOn)
            Dim iRun = _clavesDeRunOn.IndexOf(_cond.ConditionRunOn)
            If iRun >= 0 Then
                ComboRunOn.SelectedIndex = iRun
            ElseIf _hayEntradaDeRunOnCruda Then
                ComboRunOn.SelectedIndex = ComboRunOn.Items.Count - 1
            ElseIf ComboRunOn.Items.Count > 0 Then
                ComboRunOn.SelectedIndex = 0
            End If
            ' Parámetros
            RefrescarParametros()
        Finally
            _cargando = False
        End Try
    End Sub

    ''' <summary>Los rótulos y la editabilidad de los dos parámetros, decididos por la TABLA del juego.
    ''' Lo que se muestra es la rama que el árbol trae.</summary>
    Private Sub RefrescarParametros()
        Dim idx = FuncionDelCombo()
        Dim cambio As Boolean = (idx <> _funcionOriginal)
        For cual = 1 To 2
            _ramas(cual - 1) = Canon.CanonInterpretacion.RamaDeParametroDeCondicion(idx, cual, _game)
            ArmarParametro(cual, _ramas(cual - 1), cambio)
        Next
    End Sub

    ''' <summary>Arma el control de UN parámetro para la rama que le toca, y le pone el valor.
    ''' <para>El valor sale del árbol por la RUTA de la rama y sólo si la función NO cambió: con la
    ''' función nueva, los bytes que están en el record describen los parámetros de la función VIEJA y
    ''' mostrarlos sería ofrecerle al usuario un valor que no significa nada. Con la función cambiada el
    ''' control arranca neutro —vacío, o el primer valor del enum, o ninguna referencia— y lo que quede
    ''' ahí al aceptar es lo que se escribe: así los bytes viejos no pueden sobrevivir.</para></summary>
    Private Sub ArmarParametro(cual As Integer, rama As Canon.CanonInterpretacion.RamaDeParametro,
                               funcionCambio As Boolean)
        Dim lbl = If(cual = 1, LabelParam1, LabelParam2)
        Dim caja = If(cual = 1, TextBoxParam1, TextBoxParam2)
        Dim combo = If(cual = 1, ComboParam1, ComboParam2)
        Dim boton = If(cual = 1, ButtonPickParam1, ButtonPickParam2)
        Dim claves = _clavesDeEnum(cual - 1)

        ' El rótulo dice la CLASE de la rama, que es lo que el usuario necesita para saber qué se le
        ' está pidiendo; el rótulo de xEdit ya trae el nombre ('Actor Base', 'Quest Stage').
        lbl.Text = $"Param {cual} ({If(String.IsNullOrEmpty(rama.Nombre), "none", rama.Nombre)}):"

        claves.Clear()
        combo.Items.Clear()
        combo.Visible = False
        caja.Visible = True
        caja.Tag = Nothing
        boton.Enabled = False
        boton.Visible = True

        ' El valor que hay en el árbol, por la ruta de la rama de la función ORIGINAL: con la función
        ' cambiada no se lee nada, ver el <para> del doc.
        Dim actual As Object = Nothing
        If Not funcionCambio Then
            Dim ruta = Canon.CanonInterpretacion.RutaDeParametroDeCondicion(cual, rama)
            If ruta.Length > 0 Then actual = Canon.CanonInterpretacion.ValorDeCampo(_cond, ruta)
        End If

        If rama.EsNinguno Then
            caja.Text = "(this function does not take this parameter)"
            caja.ReadOnly = True
            boton.Visible = False

        ElseIf rama.EsReferencia Then
            ' Selector de record, con las firmas que la rama DECLARA. Una rama sin firmas no es un
            ' hueco: es `wbFormID` pelado, o sea que acepta cualquier record.
            caja.ReadOnly = True
            boton.Enabled = True
            Dim fid As UInteger = 0UI
            If actual IsNot Nothing Then
                Try
                    fid = CUInt(Convert.ToInt64(actual))
                Catch
                    fid = 0UI
                End Try
            End If
            caja.Tag = fid
            caja.Text = If(fid = 0UI, "(none)", _mainForm.GetRecordDisplayNameForEditor(fid))

        ElseIf rama.EsEnumerado Then
            caja.Visible = False
            combo.Visible = True
            boton.Visible = False
            For Each kv In rama.Valores.OrderBy(Function(x) x.Key)
                combo.Items.Add($"{kv.Key} — {kv.Value}")
                claves.Add(kv.Key)
            Next
            Dim v As Long = 0L
            If actual IsNot Nothing Then
                Try
                    v = Convert.ToInt64(actual)
                Catch
                    v = 0L
                End Try
            End If
            Dim iv = claves.IndexOf(v)
            If iv >= 0 Then
                combo.SelectedIndex = iv
            ElseIf funcionCambio OrElse actual Is Nothing Then
                ' Sin valor propio que conservar: arranca en el primero del enum.
                If combo.Items.Count > 0 Then combo.SelectedIndex = 0
            Else
                ' ⛔ El record trae un valor que el enum NO nombra y se le da su PROPIA entrada. Un
                ' combo cerrado que no lo represente lo reescribiría con el primer valor de la lista,
                ' en silencio: es el defecto de clase de esta ola, el mismo del operador inválido.
                _crudoDeEnum(cual - 1) = v
                combo.Items.Add($"{v} — (not named by this game's enum; kept as-is)")
                combo.SelectedIndex = combo.Items.Count - 1
            End If

        ElseIf rama.EsNumero Then
            caja.ReadOnly = False
            boton.Visible = False
            If actual Is Nothing Then
                caja.Text = "0"
            ElseIf TypeOf actual Is Single Then
                caja.Text = DirectCast(actual, Single).ToString(Globalization.CultureInfo.InvariantCulture)
            ElseIf TypeOf actual Is Double Then
                caja.Text = DirectCast(actual, Double).ToString(Globalization.CultureInfo.InvariantCulture)
            Else
                Try
                    caja.Text = Convert.ToInt64(actual).ToString(Globalization.CultureInfo.InvariantCulture)
                Catch
                    caja.Text = "0"
                End Try
            End If

        Else
            ' ⚠️ Rama sin forma declarada: la de reserva —función fuera de la tabla— y la unión anidada
            ' del VATS de Skyrim. Se muestra y no se toca, y el rótulo lo dice.
            caja.ReadOnly = True
            boton.Visible = False
            caja.Text = "(this parameter's branch has no editable shape here — it is preserved as-is)"
        End If
    End Sub

    ''' <summary>El índice de función elegido, por la LISTA DE CLAVES. ⛔ Antes parseaba el rótulo
    ''' buscando los corchetes y, si no los encontraba, devolvía la función que el record ya tenía: con el
    ''' combo editable eso significaba que escribir cualquier cosa «aceptaba» sin cambiar nada y sin
    ''' avisar. La entrada extra —la del índice que la tabla no nombra— tampoco tiene corchetes.</summary>
    Private Function FuncionDelCombo() As Integer
        Dim i = ComboFunction.SelectedIndex
        If i < 0 Then Return _funcionOriginal
        If i < _clavesDeFuncion.Count Then Return _clavesDeFuncion(i)
        Return If(_funcionCrudaFueraDeTabla >= 0, _funcionCrudaFueraDeTabla, _funcionOriginal)
    End Function

    Private Sub OnFuncionCambiada(sender As Object, e As EventArgs)
        If _cargando Then Return
        RefrescarParametros()
    End Sub

    Private Sub OnBanderaGlobalCambiada(sender As Object, e As EventArgs)
        If _cargando Then Return
        ButtonPickGlobal.Enabled = CheckUseGlobal.Checked
        TextBoxValue.ReadOnly = CheckUseGlobal.Checked
    End Sub

    Private Sub OnElegirParam1(sender As Object, e As EventArgs)
        ElegirParametro(1)
    End Sub

    Private Sub OnElegirParam2(sender As Object, e As EventArgs)
        ElegirParametro(2)
    End Sub

    ''' <summary>Elige el record de un parámetro de clase FormID, con las firmas que la rama DECLARA.
    ''' <para>⛔ Las firmas salen de <c>rama.Firmas</c> y no de un <c>Select Case</c> acá: son 33 ramas
    ''' con listas que van de una firma (<c>[FACT]</c>) a cuarenta y cinco (el <c>Base Object</c>), y
    ''' varias aceptan <c>FLST</c> o <c>NULL</c> además del tipo obvio — el <c>Keyword</c> de Fallout 4
    ''' es <c>[FLST,KYWD,NULL]</c>, no <c>[KYWD]</c>. Copiarlas sería 33 oportunidades de equivocar una.</para>
    ''' <para>Una rama SIN firmas no es un hueco: es <c>wbFormID</c> pelado, o sea que acepta cualquier
    ''' record, y el selector se abre sin filtro de firma.</para></summary>
    Private Sub ElegirParametro(cual As Integer)
        Dim rama = _ramas(cual - 1)
        If Not rama.EsReferencia Then Return
        Dim caja = If(cual = 1, TextBoxParam1, TextBoxParam2)

        Dim actual As UInteger = 0UI
        If caja.Tag IsNot Nothing Then
            Try
                actual = CUInt(caja.Tag)
            Catch
                actual = 0UI
            End Try
        End If

        Dim titulo = $"Pick the {If(String.IsNullOrEmpty(rama.Nombre), "record", rama.Nombre.ToLowerInvariant())}"
        ' La firma `NULL` que varias ramas declaran no es un tipo de record: es el permiso de dejarlo en
        ' cero, que el selector ya da por su cuenta. Pasársela como firma a filtrar no traería nada.
        Dim firmas = rama.Firmas.Where(Function(f) Not String.Equals(f, "NULL", StringComparison.Ordinal)).ToArray()
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, firmas, titulo,
                                           actual, False, Nothing, Nothing, Nothing)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            caja.Tag = dlg.SelectedFormID
            caja.Text = If(dlg.SelectedFormID = 0UI, "(none)",
                           _mainForm.GetRecordDisplayNameForEditor(dlg.SelectedFormID))
        End Using
    End Sub

    Private Sub OnElegirGlobal(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"GLOB"}, "Pick the global",
                                           _cond.ConditionComparisonValueGlobal, False, Nothing, Nothing, Nothing)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            TextBoxValue.Tag = dlg.SelectedFormID
            TextBoxValue.Text = _mainForm.GetRecordDisplayNameForEditor(dlg.SelectedFormID)
        End Using
    End Sub

    ''' <summary>True cuando este OK puso los parámetros en CERO porque la función cambió.
    ''' <para>⛔ Lo lee el llamador para MARCAR la fila: el cero es un reset, no un «ninguno»
    ''' universal — para un índice de alias o un enumerado es un valor con significado —, así que el
    ''' usuario tiene que enterarse de que quedó puesto y revisarlo. Sin esto el diálogo cierra y la
    ''' fila muestra la función nueva con los parámetros en 0 como si los hubiera elegido alguien.</para></summary>
    Public ReadOnly Property ParametrosCerados As Boolean
        Get
            Return _cero
        End Get
    End Property
    Private _cero As Boolean

    Private Sub OnOk(sender As Object, e As EventArgs)
        Try
            ' Función
            _cond.ConditionFunction = CUShort(FuncionDelCombo())
            ' Byte Type = operador + banderas. Si el operador que traía NO es uno de los seis, se
            ' conserva el que estaba: no se normaliza lo que el archivo trae.
            Dim op As Byte
            If ComboOperator.SelectedIndex >= 0 AndAlso
               ComboOperator.SelectedIndex < CondicionesDeHeadPart.Operadores.Count Then
                op = CondicionesDeHeadPart.Operadores(ComboOperator.SelectedIndex).Valor
            Else
                op = CByte(_tipoOriginal And CondicionesDeHeadPart.MascaraDeOperador)
            End If
            Dim banderas As Byte = 0
            If CheckOr.Checked Then banderas = CByte(banderas Or 1)
            If CheckUseAliases.Checked Then banderas = CByte(banderas Or 2)
            If CheckUseGlobal.Checked Then banderas = CByte(banderas Or 4)
            If CheckUsePackdata.Checked Then banderas = CByte(banderas Or 8)
            If CheckSwapSubject.Checked Then banderas = CByte(banderas Or 16)
            _cond.ConditionType = CByte(op Or banderas)
            ' Run On
            ' Por la LISTA DE CLAVES, no por el índice crudo: si la entrada elegida es la extra (la
            ' que el esquema no nombra), vuelve el valor que traía el record.
            If ComboRunOn.SelectedIndex >= 0 Then
                If ComboRunOn.SelectedIndex < _clavesDeRunOn.Count Then
                    _cond.ConditionRunOn = _clavesDeRunOn(ComboRunOn.SelectedIndex)
                Else
                    _cond.ConditionRunOn = _runOnCrudoFueraDeTabla
                End If
            End If
            ' Valor de comparación
            If CheckUseGlobal.Checked Then
                If TextBoxValue.Tag IsNot Nothing Then _cond.ConditionComparisonValueGlobal = CUInt(TextBoxValue.Tag)
            Else
                Dim v As Single
                If Single.TryParse(TextBoxValue.Text.Trim(), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, v) Then
                    _cond.ConditionComparisonValueFloat = v
                End If
            End If
            ' ⛔⛔ SI LA FUNCIÓN CAMBIÓ, LOS PARÁMETROS SE PONEN EN CERO.
            '
            ' El campo `Function` (u16) y los ocho bytes de parámetro son INDEPENDIENTES en el archivo,
            ' pero al LEER el decisor elige la rama de la unión por la función. Este editor escribe sólo la
            ' rama que el record ya traía —que es correcto para editar—, así que sin esto: el usuario cambia
            ' `GetStageDone` por otra función, el editor escribe la función nueva y CONSERVA los bytes de
            ' `ptQuest` + `ptQuestStage`, y al re-leer esos mismos bytes se interpretan como lo que la
            ' función nueva declare (un FormID leído como float, un enumerado, lo que toque). Es un CTDA
            ' semánticamente FALSO escrito al ESP sin un solo aviso. Lo levantó la revisión adversarial.
            '
            ' ⛔⛔ ACÁ DECÍA «cero es ‘ninguno’ en cualquiera de las clases de parámetro» Y ES FALSO:
            ' era una regla de la APP escrita como si fuera del formato. Contando las ramas de
            ' `UnionV(«Parameter #1», …)` en `Rec_HDPT`:
            '   · SÍ es la nulidad del motor en las ~40 `Wb.Fid(…)` (0 = null reference) y en
            '     `Wb.Bytes(«None», 4)`.
            '   · NO lo es en `Wb.Int(«Alias», s32)` — el índice 0 es un alias VÁLIDO, y el tipo es
            '     SIGNED, así que el idioma de «ninguno» ahí sería −1 —; ni en `Wb.Int(«Integer», s32)`,
            '     donde 0 es un entero legal; ni en las NUEVE `Wb.Enumerated(…, u32)`, donde el ordinal 0
            '     es un miembro NOMBRADO de cada enum — poner 0 en `Sex` no es «ninguno»: es un sexo.
            '   ⛔ Y NO se inventa el −1: para saber cuál es el «ninguno» de cada clase haría falta una
            ' cita del motor que no tengo. Lo que el cero ES, y alcanza: un RESET definido y
            ' reproducible — el mismo valor siempre, escrito, no heredado de la función anterior. Para
            ' las clases de índice y de enumerado es un valor que el usuario TIENE que revisar, y por eso
            ' la fila queda MARCADA hasta que lo toque (ver `ParametrosCerados`).
            '   La alternativa —rechazar el OK— deja al usuario sin forma de cambiar la función sin
            ' borrar la condición, así que no es mejor: es lo mismo escondido.
            Dim funcionCambio As Boolean = (FuncionDelCombo() <> _funcionOriginal)
            _cero = funcionCambio
            ' ⛔⛔ CON LA FUNCIÓN CAMBIADA, LAS TRES RAMAS DE PARÁMETRO SE REHACEN. NO SE CONSERVAN.
            '
            ' El campo `Function` (u16) y los bytes de parámetro son INDEPENDIENTES en el archivo, pero
            ' al LEER el decisor elige la rama de la unión por la función. Sin esto: el usuario cambia
            ' `GetStageDone` por otra función y los bytes de `ptQuest` + `ptQuestStage` sobreviven, y al
            ' re-leer se interpretan como lo que la función NUEVA declare — un FormID leído como float,
            ' un enumerado, lo que toque. Es un CTDA semánticamente FALSO escrito al ESP sin un aviso.
            ' Lo levantó la revisión adversarial.
            '
            ' Se ponen en cero POR RUTA y sin mirar la rama: el cero se deriva del valor que la hoja
            ' tiene puesto, así que no hay una lista de ramas a mano que pueda quedar incompleta —y
            ' enumerar ramas a mano es exactamente lo que garantiza que falte alguna—. Las tres rutas
            ' son las del esquema (`WbSchemaGen_FO4.Rec_HDPT`); el `Parameter #3` es otra unión decidida
            ' por la misma función, así que arrastra el mismo problema.
            '
            ' ⛔ Y el cero NO es «ninguno» en todas las clases: lo es en las ~40 referencias (0 = null)
            ' y en `Bytes('None', 4)`, pero NO en `Alias` ni `Integer` —s32, donde 0 es un valor legal—
            ' ni en los enumerados, donde el ordinal 0 es un miembro NOMBRADO (poner 0 en `Sex` no es
            ' «ninguno»: es un sexo). No se inventa el −1: para saber cuál es el «ninguno» de cada clase
            ' haría falta una cita del motor que no tengo. Lo que el cero ES, y alcanza: un RESET
            ' definido y reproducible, y la fila queda MARCADA hasta que el usuario lo revise (ver
            ' `ParametrosCerados`).
            If funcionCambio Then
                Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #1")
                Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #2")
                Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #3")
            End If

            ' ⛔ Y ENCIMA DE ESE RESET, LO QUE EL USUARIO DEJÓ EN LOS CONTROLES — de la función NUEVA.
            ' Esto es lo que saca el «acepta y volvé a abrir» que había antes: la escritura va por la
            ' ruta CON el rótulo de la rama, así que pasa por `WbEdit.ReevaluarAlternativa`, que vuelve
            ' a correr el decider y MATERIALIZA la rama que la función nueva pide. Con la función
            ' cambiada los controles se armaron neutros (ver `ArmarParametro`), así que escribir lo que
            ' tienen no puede arrastrar el valor viejo.
            For cual = 1 To 2
                EscribirParametro(cual, _ramas(cual - 1))
            Next
        Catch ex As Exception
            Logger.LogLazy(Function() $"[CTDA-EDITOR] commit falló: {ex.GetType().Name}: {ex.Message}")
            MessageBox.Show(Me, $"The condition could not be written: {ex.GetType().Name}.",
                            "Condition", MessageBoxButtons.OK, MessageBoxIcon.Error)
            DialogResult = DialogResult.None
            Return
        End Try
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Escribe UN parámetro en la rama que le toca, por la ruta del esquema.
    ''' <para>Las ramas que no se pueden editar —«ninguno», la de reserva, la unión anidada— no se
    ''' escriben: quedan como estaban (o en el cero del reset, si la función cambió). Un valor que no
    ''' parsea tampoco se escribe, en vez de escribir un cero que el usuario no pidió.</para></summary>
    Private Sub EscribirParametro(cual As Integer, rama As Canon.CanonInterpretacion.RamaDeParametro)
        If rama.EsNinguno OrElse rama.EsCruda Then Return
        Dim ruta = Canon.CanonInterpretacion.RutaDeParametroDeCondicion(cual, rama)
        If ruta.Length = 0 Then Return
        Dim caja = If(cual = 1, TextBoxParam1, TextBoxParam2)
        Dim combo = If(cual = 1, ComboParam1, ComboParam2)
        Dim claves = _clavesDeEnum(cual - 1)

        If rama.EsReferencia Then
            If caja.Tag Is Nothing Then Return
            Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, CUInt(caja.Tag))

        ElseIf rama.EsEnumerado Then
            ' Por la LISTA DE CLAVES y no por el índice: los valores pueden ser dispersos, y si la
            ' entrada elegida es la extra —la que el enum no nombra— vuelve el valor que traía.
            Dim i = combo.SelectedIndex
            If i < 0 Then Return
            Dim v As Long = If(i < claves.Count, claves(i), _crudoDeEnum(cual - 1))
            Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, v)

        ElseIf rama.Clase = "float" Then
            Dim f As Single
            If Single.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Float,
                               Globalization.CultureInfo.InvariantCulture, f) Then
                Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, f)
            End If

        Else
            ' Entero, con o sin signo según lo que declare la rama (`itS32` vs `itU32`): se parsea al
            ' ancho que corresponde para que un valor de 32 bits con el bit alto puesto no se rechace
            ' por «fuera de rango» en la rama sin signo, ni se acepte como positivo en la con signo.
            If rama.ConSigno Then
                Dim n As Integer
                If Integer.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Integer,
                                    Globalization.CultureInfo.InvariantCulture, n) Then
                    Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, n)
                End If
            Else
                Dim n As UInteger
                If UInteger.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Integer,
                                     Globalization.CultureInfo.InvariantCulture, n) Then
                    Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, n)
                End If
            End If
        End If
    End Sub

End Class
