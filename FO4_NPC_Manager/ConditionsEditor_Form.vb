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
''' firmas declaradas para las 33 de FormID, combo con los valores del enum para las 9, caja de texto
''' para las escalares. Cero listas a mano.</para>
'''
''' <para>⚠️ <b>LO DE SKYRIM ESTÁ EMITIDO, NO MEDIDO.</b> El generador saca la tabla de los DOS
''' juegos en la misma pasada, así que las 58 ramas de Skyrim (13 de enumerado) existen y son
''' correctas — pero <b>no tienen consumidor ni testigo</b>: a este editor se llega sólo desde el de
''' head parts, que hace <c>TryCast(draft.Record, Canon.HdptFO4)</c>, y el <c>HDPT</c> de Skyrim no
''' declara <c>CTDA</c> (cero en <c>WbSchemaGen_TES5</c> contra uno en <c>WbSchemaGen_FO4</c>). Dicho
''' de otra forma: en Skyrim este diálogo es inalcanzable. Las tablas quedan por decisión del
''' usuario — el día que se editen las condiciones de un <c>QUST</c> o un <c>PERK</c> ya están —,
''' y lo que se corrigió es el TÍTULO: antes decía «13 en Skyrim» junto a los números medidos de
''' Fallout 4, y eso se lee como si estuviera ejercitado.</para>
'''
''' <para>⛔⛔ <b>EL <c>CTDA</c> TIENE CUATRO UNIONES, NO UNA</b>, y sus discriminadores son la
''' <b>función</b>, los bits <b><c>0x02</c></b> (<i>Use Aliases</i>) y <b><c>0x08</c></b>
''' (<i>Use PackData</i>) del byte <c>Type</c>, el bit <b><c>0x04</c></b> (<i>Use Global</i>) y el
''' <b><c>Run On</c></b>. Censado sobre <c>WbSchemaGen_FO4.vb</c>, bloque <c>Rec_HDPT</c>:
''' <list type="table">
''' <item><term><c>Comparison Value</c></term><description>bit <c>0x04</c> del <c>Type</c> — float o
''' <c>Fid("GLOB")</c> SIN <c>NULL</c> (<c>WbDecidersImpl2.ConditionCompValue</c>)</description></item>
''' <item><term><c>Parameter #1</c> / <c>#2</c></term><description>la función <b>más</b> los bits
''' <c>0x02</c>/<c>0x08</c> y el <c>Run On</c>, vía <c>OrdinalDeParametroAjustado</c></description></item>
''' <item><term><c>Reference</c></term><description><c>Run On = 2</c>; con cualquier otro valor la rama es
''' <c>Int("Unused", u32)</c> y el campo NO es una referencia</description></item>
''' <item><term><c>Parameter #3</c></term><description>el <c>Run On</c> a secas — <b>NO la función</b>.
''' ONCE ramas en Fallout 4 y OCHO en Skyrim; nueve (seis en Skyrim) son <c>Int s32</c> idénticas, la 5 es
''' <c>Quest Alias</c> y la 7 <c>Event Data</c></description></item>
''' </list>
''' Esa asimetría 11/8 es la misma del <c>Run On</c> y es la que hace que los valores 8, 9 y 10 sean de
''' Fallout 4 únicamente — en Skyrim caen fuera de la unión, y el modo de falla no es «se lee mal» sino
''' que <c>WbUnionDef.Parse</c> TIRA <c>WbLayoutException</c>: el record no se lee. Medido sobre los dos
''' corpus: 263.461 <c>CTDA</c> y <b>ninguno</b> indexa fuera de su unión (Fallout 4 llega exactamente a
''' 10, Skyrim exactamente a 7).</para>
'''
''' <para>⚠️ <b>Y SKYRIM TIENE UN QUINTO DISCRIMINADOR, ANIDADO, que este editor NO cubre</b>:
''' <c>WbSchemaGen_TES5</c> declara <c>VATS Value Param</c> como una unión <b>dentro</b> de la rama
''' <c>ptVATSValueFunction</c> de <c>Parameter #1</c>, y su decisor es
''' <c>Sibling(parent, "Parameter #1")</c> — o sea que <b>el VALOR de un parámetro discrimina otra
''' unión</b>. Fallout 4 no la tiene (su <c>Parameter #1</c> pasa de <c>Quest Stage</c> directo a
''' <c>Enumerated("Alignment")</c>), así que hoy no alcanza a nada; queda como hueco DECLARADO con su
''' nombre, junto al otro hueco de Skyrim.</para>
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

    ''' <summary>El `Run On` con el que se abrió. Es discriminador de DOS uniones — `Parameter #3` y
    ''' `Reference` — y no estaba guardado, así que no había con qué comparar.</summary>
    Private ReadOnly _runOnOriginal As UInteger

    ''' <summary>LA FOTO de la condición en el estado de APERTURA. Sirve a dos cosas a la vez:
    ''' <para>(a) restaurar si el gesto se abandona — Cancel, el `Catch` del OK, o el OK que se niega
    ''' a cerrar y después se cancela —, porque el `OnOk` escribe `Function`, `Type`, `Run On` y el
    ''' valor de comparación ANTES de los parámetros: si algo falla en el medio, el record quedaba con
    ''' la mitad del cambio;</para>
    ''' <para>(b) ⛔ y contestar «qué rama tenía este parámetro al abrir», que es la mitad que el
    ''' predicado del reset necesita. NO se puede sacar del árbol vivo: cuando el reset pregunta, el OK
    ''' ya escribió los discriminadores nuevos. Y NO se replica el decisor en el editor: `Parameter #3`
    ''' lo decide el `Run On` y `Reference` el `Run On = 2`, así que replicarlos sería el tercer y el
    ''' cuarto dueño de la misma ley. Sobre la FOTO el decisor da la respuesta de apertura por sí
    ''' solo: una ley, dos evaluaciones, cero réplicas.</para></summary>
    Private _foto As Canon.WbNode

    Public Sub New(mainForm As MainForm, game As Canon.WbGame, cond As Canon.HdptFO4_Conditions)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        If cond Is Nothing Then Throw New ArgumentNullException(NameOf(cond))
        InitializeComponent()
        _mainForm = mainForm
        _game = game
        _cond = cond
        _tipoOriginal = cond.ConditionType
        _funcionOriginal = CInt(cond.ConditionFunction)
        _runOnOriginal = cond.ConditionRunOn

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
        ' ⛔⛔ LOS CINCO CONTROLES QUE MUEVEN UN DISCRIMINADOR, NO UNO. `RefrescarParametros` se
        ' disparaba SOLO desde el combo de funciones, y los otros tres discriminadores del `CTDA`
        ' --los bits 0x02 y 0x08 del `Type`, y el `Run On`-- no tenian handler. Entonces: tildar
        ' `Use Aliases` no rearmaba nada, el formulario seguia mostrando un selector de record para
        ' un parametro que el motor ya iba a leer como indice de alias, el usuario elegia un record
        ' y el OK se lo tiraba. Un boton que hace algo visible y no escribe nada.
        AddHandler ComboFunction.SelectedIndexChanged, AddressOf OnDiscriminadorCambiado
        AddHandler CheckUseAliases.CheckedChanged, AddressOf OnDiscriminadorCambiado
        AddHandler CheckUsePackdata.CheckedChanged, AddressOf OnDiscriminadorCambiado
        AddHandler ComboRunOn.SelectedIndexChanged, AddressOf OnDiscriminadorCambiado
        AddHandler ButtonPickParam1.Click, AddressOf OnElegirParam1
        AddHandler ButtonPickParam2.Click, AddressOf OnElegirParam2
        AddHandler ButtonPickReference.Click, AddressOf OnElegirReference
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

    ''' <summary>Los rótulos, los controles y los valores de los dos parámetros, decididos por la sede
    ''' ÚNICA del tipo de parámetro — la que aplica los cuatro discriminadores, no sólo la función.
    ''' <para>⛔ Se dispara desde los CINCO controles que mueven un discriminador. Ver el ⛔ del
    ''' constructor: cableada sólo al combo de funciones, tildar `Use Aliases` no rearmaba nada.</para>
    ''' <para>⛔ Y el «cambió» que decide si el control arranca neutro NO es «cambió la función» ni
    ''' «cambió el índice de rama»: es «cambió el DOMINIO», y lo contesta
    ''' <c>Canon.WbDominio.MismoDominio</c> comparando la `Def` de apertura —evaluada sobre la FOTO—
    ''' contra la de ahora. De las once ramas de `Parameter #3`, nueve son idénticas: por índice se
    ''' reseteaba de gusto.</para></summary>
    Private Sub RefrescarParametros()
        Dim idx = FuncionDelCombo()
        Dim tipoAhora As Long = TipoDelFormulario()
        Dim runOnAhora As Long = RunOnDelFormulario()
        For cual = 1 To 2
            _ramas(cual - 1) = Canon.CanonInterpretacion.RamaDeParametroDeCondicion(
                idx, cual, _game, tipoAhora, runOnAhora)
            ArmarParametro(cual, _ramas(cual - 1), CambioDeDominio(cual))
        Next
        ArmarTercero(runOnAhora)
        ArmarReferencia(runOnAhora)
    End Sub

    ''' <summary>El byte `Type` que el formulario tiene AHORA — operador más banderas.
    ''' <para>⛔ Se arma con la misma cuenta que el OK, y por eso vive en una sede: si el refresco y el
    ''' OK la calcularan por separado, el control que el usuario ve y la rama donde se escribe podrían
    ''' discrepar.</para></summary>
    Private Function TipoDelFormulario() As Long
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
        Return CLng(CByte(op Or banderas))
    End Function

    ''' <summary>El `Run On` que el formulario tiene AHORA, por la LISTA DE CLAVES (el índice del item
    ''' no es el valor, y la entrada extra devuelve lo que el record traía).</summary>
    Private Function RunOnDelFormulario() As Long
        Dim i = ComboRunOn.SelectedIndex
        If i < 0 Then Return CLng(_runOnOriginal)
        If i < _clavesDeRunOn.Count Then Return CLng(_clavesDeRunOn(i))
        Return CLng(_runOnCrudoFueraDeTabla)
    End Function

    ''' <summary>La rama de ese parámetro cambió de DOMINIO respecto de la apertura.
    ''' <para>La `Def` de apertura se evalúa sobre la FOTO, así que la contesta el DECISOR y no una
    ''' réplica del decisor escrita acá. Sin foto no se puede afirmar nada y se devuelve True — el lado
    ''' conservador es resetear, que pierde un dato, no conservar, que escribiría bytes falsos.</para></summary>
    ''' <summary>Vuelve la condición al estado de la FOTO. Es el deshacer de las tres salidas de
    ''' abandono del OK; sin esto, el `OnOk` que escribe los discriminadores y falla en los parámetros
    ''' dejaba el record con la mitad del cambio.
    ''' <para>Se restauran los HIJOS y no el nodo: el nodo lo referencia el árbol del borrador — el
    ''' llamador nos pasó <c>fo4.Conditions(idx)</c>, la vista VIVA —, así que reemplazarlo dejaría al
    ''' padre apuntando al viejo.</para>
    ''' <para>⛔ Y REPONER LOS HIJOS ALCANZA SÓLO PORQUE ESTE NODO ES UN <c>RStruct</c> SIN VALOR PROPIO
    ''' NI <c>ParsedCount</c>. El estado de un <c>WbNode</c> es una lista cerrada (<c>WbNodeExtras</c>,
    ''' diez campos) y para este nodo TODO vive en los hijos: <c>UnionBranch</c> en los nodos de unión
    ''' del <c>CTDA</c>, <c>TerminatorCount</c>/<c>RawOverride</c> en las hojas de texto,
    ''' <c>ShortRead</c> y las tres de referencia en las hojas de valor. El <c>ParsedCount</c> vive en
    ''' el nodo del ARREGLO, que es el PADRE, y el <c>ParsedFormVersion</c> en la raíz.
    ''' ⛔ Para un nodo CON valor propio, o para el nodo de un arreglo con <c>WithCountPath</c>, reponer
    ''' los hijos NO alcanza. Queda escrito porque alguien va a copiar esta rutina a otro editor.</para>
    ''' <para>Es IDEMPOTENTE a propósito — la foto no se consume —, y hace falta: por el camino del
    ''' Cancel corre dos veces.</para></summary>
    ''' <summary>Se abandonó la edición: la condición vuelve al estado de la FOTO.
    ''' <para>⛔ VA EN `FormClosing` Y NO EN UN HANDLER DEL BOTÓN, porque el Cancel de este diálogo es
    ''' `ButtonCancel.DialogResult` puesto en el Designer y no pasa por código. Así queda cubierta la
    ''' TERCERA salida, que es la que se escapaba: el OK que se niega a cerrar porque una escritura no se
    ''' pudo hacer, y el usuario después cancela — ahi los discriminadores YA estaban escritos.</para>
    ''' <para>Si la foto no existe es que el OK nunca arrancó, y entonces no hay nada escrito que
    ''' deshacer.</para></summary>
    Private Sub ConditionsEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) _
            Handles Me.FormClosing
        If DialogResult <> DialogResult.OK Then RestaurarFoto()
    End Sub

    Private Sub RestaurarFoto()
        If _foto Is Nothing OrElse _cond.Node Is Nothing Then Return
        Try
            ' ⛔ SE CLONA CADA HIJO CON EL NODO VIVO COMO PADRE. Aca se clonaba la FOTO ENTERA y se le
            ' robaban los hijos, y esos hijos quedaban con `.Parent` apuntando al clon que se
            ' descartaba: `Clonar(padre)` cablea `.Parent = padre` en cada nivel. El `h.Parent = ...`
            ' que venia despues arreglaba SOLO el primer nivel; los nietos seguian colgados del
            ' huerfano.
            ' Y es PEOR que un padre nulo: con `Nothing` la guarda de `AsegurarRamaVigente` lo caza,
            ' con un padre INCORRECTO no dispara ninguna guarda. Rompe todo lo que SUBE --
            ' `WbPath.ResolveUpwards`, el `AsegurarEnArregloOAncestro` de `WbEdit`, y el `n.Path` con
            ' el que se reportan los hallazgos, que nombraria una ruta enraizada en un nodo que ya no
            ' existe.
            ' Y asi la foto NO se consume, con lo cual esta rutina es IDEMPOTENTE a proposito --
            ' hace falta: por el camino del Cancel corre DOS veces, porque el
            ' `ButtonCancel.DialogResult` del Designer cierra y despues `FormClosing` vuelve a
            ' restaurar.
            _cond.Node.LimpiarHijos()
            For Each h In _foto.Children
                _cond.Node.AddChild(h.Clonar(_cond.Node))
            Next
        Catch
            ' ⚠ Si la restauración falla, el record queda como quedó: no hay nada mejor que hacer
            ' acá, y tragarlo es preferible a tirar encima de la excepción que nos trajo.
        End Try
    End Sub

    ''' <summary>La rama del `Parameter #3` cambió de dominio.
    ''' <para>⛔ VA APARTE de las de los parámetros 1 y 2 porque su discriminador es OTRO: `Parameter #3`
    ''' lo decide el `Run On` a secas (`WbDecidersImpl2.ConditionParam3` devuelve
    ''' `CInt(Sibling(parent,"Run On").Value)`), no la función. El comentario que había acá decía que era
    ''' «otra unión decidida por la misma función» y era FALSO — por eso un cambio de función borraba un
    ''' `Quest Alias` que el usuario nunca tocó.</para>
    ''' <para>El editor no expone este campo todavía, así que el reset es la única red: de las once
    ''' ramas, sólo el par 5↔7 (`Quest Alias` `Int s32` ↔ `Event Data` `Enumerated s32`) reinterpreta.
    ''' Las otras nueve son `Int s32` idénticas y `MismoDominio` las deja pasar.</para></summary>
    ''' <summary>El `Parameter #3`, rótulado con la rama que el `Run On` elige.
    ''' <para>⛔ NINGUNA de sus once ramas es un FormID — nueve son `Int s32` idénticas, la 5 es
    ''' `Quest Alias` (`Int s32` con otro formateador) y la 7 `Event Data` (`Enumerated s32`) — así que
    ''' no lleva selector de record: lleva una caja numérica y el rótulo dice qué es AHORA. Es la misma
    ''' información que antes no existía en ningún lado: el campo se editaba desde xEdit o no se
    ''' editaba.</para>
    ''' <para>El valor se lee por la ruta, que resuelve la rama vigente.</para></summary>
    Private Sub ArmarTercero(runOnAhora As Long)
        Dim nombre = Canon.CanonInterpretacion.NombreDeRamaVigenteDeCampo(_cond, "CTDA\Parameter #3")
        LabelParam3.Text = $"Param 3 ({If(nombre.Length = 0, "—", nombre)}):"
        Dim v = Canon.CanonInterpretacion.ValorDeCampo(_cond, "CTDA\Parameter #3")
        If v Is Nothing Then
            TextBoxParam3.Text = "0"
        Else
            Try
                TextBoxParam3.Text = Convert.ToInt64(v).ToString(Globalization.CultureInfo.InvariantCulture)
            Catch
                TextBoxParam3.Text = "0"
            End Try
        End If
        TextBoxParam3.ReadOnly = False
    End Sub

    ''' <summary>El `Reference`, que es una REFERENCIA sólo con `Run On = 2`.
    ''' <para>⛔ `WbDecidersImpl2.ConditionReference` devuelve 1 nada más en ese caso; con cualquier
    ''' otro `Run On` la rama es `Int("Unused", u32)` y el campo NO es un FormID. Por eso el selector
    ''' se habilita o se apaga, en vez de ofrecer siempre un picker que escribiría un FormID en un
    ''' entero — que es justo el camino por el que un FormID no pasaría por el remapper de
    ''' masters.</para></summary>
    Private Sub ArmarReferencia(runOnAhora As Long)
        Dim esReferencia As Boolean = (runOnAhora = 2L)
        LabelReference.Text = If(esReferencia, "Reference (REFR):", "Reference (unused with this Run On):")
        ButtonPickReference.Enabled = esReferencia
        TextBoxReference.ReadOnly = esReferencia
        Dim v = Canon.CanonInterpretacion.ValorDeCampo(_cond, "CTDA\Reference")
        Dim fid As UInteger = 0UI
        If v IsNot Nothing Then
            Try
                fid = CUInt(Convert.ToInt64(v) And &HFFFFFFFFL)
            Catch
                fid = 0UI
            End Try
        End If
        If esReferencia Then
            TextBoxReference.Tag = fid
            TextBoxReference.Text = If(fid = 0UI, "(none)", _mainForm.GetRecordDisplayNameForEditor(fid))
        Else
            TextBoxReference.Tag = Nothing
            TextBoxReference.Text = fid.ToString(Globalization.CultureInfo.InvariantCulture)
        End If
    End Sub

    ''' <summary>Elige el `REFR` del campo `Reference`. Sólo corre con `Run On = 2`.</summary>
    Private Sub OnElegirReference(sender As Object, e As EventArgs)
        If RunOnDelFormulario() <> 2L Then Return
        Dim actual As UInteger = 0UI
        If TextBoxReference.Tag IsNot Nothing Then
            Try
                actual = CUInt(TextBoxReference.Tag)
            Catch
                actual = 0UI
            End Try
        End If
        ' Las firmas salen del esquema: la rama es `Wb.Fid("Reference")` sin lista, o sea que acepta
        ' cualquier record colocado. Se ofrecen las dos que el `.pas` nombra para esta union.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"REFR", "ACHR"},
                                           "Pick the reference", actual, False, Nothing, Nothing, Nothing)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            TextBoxReference.Tag = dlg.SelectedFormID
            TextBoxReference.Text = If(dlg.SelectedFormID = 0UI, "(none)",
                                       _mainForm.GetRecordDisplayNameForEditor(dlg.SelectedFormID))
        End Using
    End Sub

    Private Function CambioDeDominioDelTercero() As Boolean
        ' Sin foto no se decide: ver el ⛔ de `CambioDeDominio`. La politica es UNA y vive en el OK.
        If _foto Is Nothing Then Return False
        Dim ruta = "CTDA\Parameter #3"
        Dim nAntes = _foto.ByFieldPath(ruta)
        Dim nAhora = _cond.Node?.ByFieldPath(ruta)
        If nAntes Is Nothing OrElse nAhora Is Nothing Then Return False
        Dim defAntes = Canon.WbEdit.RamaQueElDecisorElige(nAntes, _cond.Context)
        Dim defAhora = Canon.WbEdit.RamaQueElDecisorElige(nAhora, _cond.Context)
        If defAntes Is Nothing OrElse defAhora Is Nothing Then Return False
        Return Not Canon.WbDominio.MismoDominio(defAntes, defAhora)
    End Function

    ''' <summary>Falta la FOTO y además cambió un discriminador ⇒ no se puede decidir nada y el OK se
    ''' rechaza.
    ''' <para>⛔ ES LA POLÍTICA ÚNICA DEL FALLO, y reemplaza a dos opuestas que había: sin foto,
    ''' <c>CambioDeDominio</c> devolvía True (resetear, o sea tirar el dato) y
    ''' <c>CambioDeDominioDelTercero</c> devolvía False (conservar, o sea escribir un <c>CTDA</c>
    ''' falso). El mismo fallo interno decidía distinto para dos campos del mismo record, y en silencio
    ''' — elegía cuál de los dos daños, no si había daño.</para>
    ''' <para>La forma tiene precedente en el árbol: la guarda de entrada de
    ''' <c>NpcRecordOverlay.AplicarOverlay</c> hace lo mismo con la sede de head parts — la pieza es
    ''' obligatoria y su ausencia es un error del llamador, no un dato.</para>
    ''' <para>Si NINGÚN discriminador cambió, la foto no hace falta: no hay ninguna rama que se
    ''' reinterprete, así que el caso normal —editar un valor sin tocar la función ni las banderas— no
    ''' se rechaza nunca.</para></summary>
    Private Function HayQueRechazarPorFaltaDeFoto() As Boolean
        If _foto IsNot Nothing Then Return False
        If FuncionDelCombo() <> _funcionOriginal Then Return True
        If TipoDelFormulario() <> CLng(_tipoOriginal) Then Return True
        If RunOnDelFormulario() <> CLng(_runOnOriginal) Then Return True
        Return False
    End Function

    Private Function CambioDeDominio(cual As Integer) As Boolean
        ' ⛔ SIN FOTO NO SE DECIDE: se rechaza el OK. Aca esta funcion devolvia True (resetear) y
        ' `CambioDeDominioDelTercero` devolvia False (conservar) -- o sea que el MISMO fallo interno
        ' producia dos decisiones OPUESTAS para dos campos del mismo record, y ninguna se le decia al
        ' usuario. Las dos pierden algo: resetear tira el dato, conservar escribe un `CTDA` falso. La
        ' asimetria no elegia entre «pierde» y «no pierde»: elegia CUAL de los dos danos, en silencio.
        ' La politica unica es `HayQueRechazarPorFaltaDeFoto`, que corre en el OK: si falta la foto Y
        ' un discriminador cambio, el OK no cierra y dice por que. Si no cambio ninguno, la foto no
        ' hace falta y el caso normal no se molesta.
        If _foto Is Nothing Then Return False
        Dim ruta = "CTDA\Parameter #" & cual.ToString(Globalization.CultureInfo.InvariantCulture)
        Dim nAntes = _foto.ByFieldPath(ruta)
        Dim nAhora = _cond.Node?.ByFieldPath(ruta)
        If nAntes Is Nothing OrElse nAhora Is Nothing Then Return True
        Dim defAntes = Canon.WbEdit.RamaQueElDecisorElige(nAntes, _cond.Context)
        Dim defAhora = Canon.WbEdit.RamaQueElDecisorElige(nAhora, _cond.Context)
        If defAntes Is Nothing OrElse defAhora Is Nothing Then Return True
        Return Not Canon.WbDominio.MismoDominio(defAntes, defAhora)
    End Function

    ''' <summary>Arma el control de UN parámetro para la rama que le toca, y le pone el valor.
    ''' <para>El valor sale del árbol por la RUTA de la rama y sólo si la función NO cambió: con la
    ''' función nueva, los bytes que están en el record describen los parámetros de la función VIEJA y
    ''' mostrarlos sería ofrecerle al usuario un valor que no significa nada. Con la función cambiada el
    ''' control arranca neutro —vacío, o el primer valor del enum, o ninguna referencia— y lo que quede
    ''' ahí al aceptar es lo que se escribe: así los bytes viejos no pueden sobrevivir.</para></summary>
    Private Sub ArmarParametro(cual As Integer, rama As Canon.CanonInterpretacion.RamaDeParametro,
                               ramaCambio As Boolean)
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
        If Not ramaCambio Then
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
            ElseIf ramaCambio OrElse actual Is Nothing Then
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

        ElseIf rama.Clase = "especial" AndAlso rama.Nombre = "String" Then
            ' ⛔ EL TEXTO VIVE EN EL `CIS1`/`CIS2`, NO EN EL u32. La rama `String` es un
            ' `wbInteger('String', itU32, ...)` cuyo `ToInt` devuelve CERO incondicional y manda el
            ' texto al subrecord hermano (`wbDefinitionsCommon.pas:2940-2956`). Acá se editaba el u32
            ' como número y el texto real no se tocaba nunca.
            caja.ReadOnly = False
            boton.Visible = False
            caja.Text = TextoDelCis(cual)

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

    ''' <summary>Cualquiera de los CINCO discriminadores cambio: se rearman los controles.
    ''' <para>Se llamaba `OnFuncionCambiada` y estaba cableado solo al combo de funciones. El nombre
    ''' importa: el `CTDA` tiene CUATRO uniones y sus discriminadores son la funcion, los bits 0x02 y
    ''' 0x08 del `Type` y el `Run On`.</para></summary>
    Private Sub OnDiscriminadorCambiado(sender As Object, e As EventArgs)
        If _cargando Then Return
        RefrescarParametros()
    End Sub

    ''' <summary>El `Use Global` del byte `Type` (bit 0x04), que decide la rama de la union
    ''' `Comparison Value`: float o referencia a un `GLOB`.
    '''
    ''' <para>⛔⛔ EL ESTADO «TILDADO Y SIN GLOBAL» NO EXISTE MAS, Y ESO ES UNA DECISION DE BYTES DEL
    ''' USUARIO. El esquema declara <c>Wb.Fid("Comparison Value - Global", "GLOB")</c> —GLOB y nada
    ''' mas, SIN <c>NULL</c>—, y este arbol ya se nego una vez a escribir una referencia nula en un
    ''' campo asi (el <c>sr.Skin = 0</c> de <c>NpcRecordOverlay</c>, con medicion: cero referencias
    ''' nulas en 3.482.830 records). MEDIDO para ESTE campo: <b>12.283 `CTDA` con el bit 0x04 en los
    ''' dos corpus y CERO con el global en cero</b>.</para>
    '''
    ''' <para>Antes se podia tildar sin elegir: el `Type` salia con el 0x04 puesto y los bytes del
    ''' float viejo se releian como FormID de `GLOB` —un `1.0` quedaba apuntando al `GLOB 0x3F800000`,
    ''' que no existe—. Y al destildar, la caja seguia con el NOMBRE del global, asi que el
    ''' `Single.TryParse` fallaba y el FormID se releia como float. Los dos mudos.</para>
    '''
    ''' <para>La salida elegida no es rechazar el OK ni escribir un 0: es que el estado ilegal sea
    ''' INALCANZABLE. Tildar abre el selector; si el usuario cancela, la tilde vuelve sola.</para></summary>
    Private Sub OnBanderaGlobalCambiada(sender As Object, e As EventArgs)
        If _cargando Then Return
        If CheckUseGlobal.Checked Then
            If TextBoxValue.Tag Is Nothing Then
                OnElegirGlobal(Nothing, EventArgs.Empty)
                If TextBoxValue.Tag Is Nothing Then
                    ' Cancelo el selector: la tilde vuelve sola, y no se pasa por aca de nuevo
                    ' porque `_cargando` corta la reentrada.
                    _cargando = True
                    Try
                        CheckUseGlobal.Checked = False
                    Finally
                        _cargando = False
                    End Try
                End If
            End If
        Else
            ' Destildar VUELVE AL FLOAT: se limpia el nombre del global, que si no dejaba a
            ' `Single.TryParse` fallando y al FormID viajando al archivo como float.
            TextBoxValue.Tag = Nothing
            TextBoxValue.Text = _cond.ConditionComparisonValueFloat.ToString(
                Globalization.CultureInfo.InvariantCulture)
        End If
        ButtonPickGlobal.Enabled = CheckUseGlobal.Checked
        TextBoxValue.ReadOnly = CheckUseGlobal.Checked
        RefrescarParametros()
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
        ' ⛔ LA FOTO, ANTES DE LA PRIMERA ESCRITURA. No en el constructor: lo que hay que poder
        ' restaurar es el estado con el que este OK arrancó. Sirve para las TRES salidas de abandono
        ' —Cancel, el `Catch` de acá abajo, y el OK que se niega a cerrar y después se cancela— y
        ' además es de donde `CambioDeDominio` saca la rama de apertura.
        If _cond.Node IsNot Nothing Then _foto = _cond.Node.Clonar()
        Try
            ' ⛔⛔ EL ORDEN DE ESTAS TRES ESCRITURAS ES LA PRECONDICIÓN DE TODO LO QUE SIGUE, NO UNA
            ' COMODIDAD. `Type`, `Function` y `Run On` son los DISCRIMINADORES de las cuatro uniones del
            ' `CTDA`, y `WbEdit.AsegurarRamaVigente` —por donde pasa toda escritura de parámetro— los
            ' busca EN EL ÁRBOL (`Sibling(parent, ...)`), no por parámetro. Si alguna de las tres se
            ' escribiera DESPUÉS del bucle de `EscribirParametro`, la rama se materializaría con el
            ' discriminador viejo y volverían los cuatro defectos de golpe, en silencio.
            '   Mutante que lo ataja: mover la escritura del `Run On` (o del `Type`) abajo del bucle
            '   ⇒ el EJE 5 de `CondicionesGate` tiene que dar ROJO.
            ' Función
            _cond.ConditionFunction = CUShort(FuncionDelCombo())
            ' ⛔ POR LA SEDE, NO POR UNA SEGUNDA CUENTA. Acá estaban escritas de nuevo la cuenta del
            ' byte `Type` (operador + las cinco banderas) y la del `Run On`, que `TipoDelFormulario` y
            ' `RunOnDelFormulario` ya hacen para armar los controles. Dos cuentas de la misma cosa es el
            ' defecto de clase de esta ola: el día que difieran, el control que el usuario ve y la rama
            ' donde se escribe dejan de ser la misma, y eso no lo ve ningún compilador.
            ' Las dos sedes conservan lo que el archivo traía cuando el valor no está en la tabla —el
            ' operador que no es uno de los seis, el `Run On` que el esquema no nombra—, así que abrir y
            ' aceptar no normaliza nada.
            _cond.ConditionType = CByte(TipoDelFormulario())
            _cond.ConditionRunOn = CUInt(RunOnDelFormulario())
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
            ' ⛔⛔ EL RESET DISPARA POR «CAMBIÓ EL DOMINIO», Y SÓLO EN LA UNIÓN QUE CAMBIÓ.
            '
            ' Antes decía `funcionCambio = (FuncionDelCombo() <> _funcionOriginal)` y ponía en cero los
            ' TRES parámetros. Eso tenía dos defectos medidos:
            '   · el `CTDA` tiene CUATRO uniones y sus discriminadores son la función, los bits 0x02/0x08
            '     del `Type` y el `Run On` — así que cambiar cualquiera de los otros tres no disparaba
            '     nada y se escribía un `CTDA` semánticamente falso;
            '   · y el `Parameter #3` NO lo decide la función sino el `Run On` (`ConditionParam3` lee
            '     `Sibling(parent,"Run On")`), así que resetearlo por un cambio de función borraba un
            '     `Quest Alias` que el usuario nunca tocó.
            '
            ' Y el predicado NO es «cambió el índice de rama»: de las once ramas de `Parameter #3`, NUEVE
            ' son `Int s32` idénticas, con lo cual `Run On 0→1` reseteaba de gusto. Es «cambió el
            ' DOMINIO», y lo contesta `Canon.WbDominio.MismoDominio` sobre las `Def` que el DECISOR elige
            ' — la de apertura evaluada sobre la FOTO, la de ahora sobre el árbol ya escrito.
            '
            ' ⛔ Con los controles ya rearmados (ver `RefrescarParametros`), para los parámetros 1 y 2 el
            ' reset casi no hace falta: lo que el usuario dejó en el control es por construcción un valor
            ' legal de la rama nueva. El reset queda como la red para lo que NO tiene control.
            ' ⛔ SIN FOTO Y CON UN DISCRIMINADOR CAMBIADO, EL OK NO CIERRA. Es la politica UNICA del
            ' fallo: no se adivina que parametros perdieron significado. Si no cambio ningun
            ' discriminador, la foto no hace falta y el caso normal no se molesta.
            If HayQueRechazarPorFaltaDeFoto() Then
                MessageBox.Show(Me,
                                "The state this dialog opened with could not be captured, and you " &
                                "changed the function, a flag or Run On — which reinterprets the " &
                                "parameter bytes." & Environment.NewLine & Environment.NewLine &
                                "Without that snapshot there is no way to tell which parameters lost " &
                                "their meaning, so nothing is written. Press Cancel — which discards " &
                                "EVERY change you made to this condition — and reopen it.",
                                "Condition", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                DialogResult = DialogResult.None
                Return
            End If

            Dim cambio1 = CambioDeDominio(1)
            Dim cambio2 = CambioDeDominio(2)
            Dim cambio3 = CambioDeDominioDelTercero()
            _cero = cambio1 OrElse cambio2 OrElse cambio3
            If cambio1 Then Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #1")
            If cambio2 Then Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #2")
            If cambio3 Then Canon.CanonInterpretacion.PonerCeroEnCampo(_cond, "CTDA\Parameter #3")

            ' ⛔ Y ENCIMA DEL RESET, LO QUE EL USUARIO DEJÓ EN LOS CONTROLES. La escritura va por
            ' `AsegurarRutaConRamasVigentes`, que re-decide la rama con los discriminadores YA escritos
            ' arriba — de ahí que el orden sea la precondición.
            ' ⛔ Y SE MIRA EL BOOLEANO: si una escritura no se pudo hacer, el OK NO cierra. Acá se
            ' ignoraba, y por ese canal un `False` era un no-op mudo.
            Dim noSePudo As New List(Of String)
            For cual = 1 To 2
                If Not EscribirParametro(cual, _ramas(cual - 1)) Then
                    noSePudo.Add($"Param {cual} ({_ramas(cual - 1).Nombre})")
                End If
            Next
            ' ⛔ EL `CIS` HUÉRFANO SE SACA. Si el parámetro dejó de ser `ptString`, el subrecord con el
            ' texto viejo ya no describe nada: queda un `CIS1` colgado de una condición cuyo parámetro
            ' ahora es un FormID. Y con el reset disparando por CUATRO discriminadores en vez de uno,
            ' este caso pasa de raro a normal.
            For cual = 1 To 2
                Dim esTexto As Boolean = (_ramas(cual - 1).Clase = "especial" AndAlso
                                          _ramas(cual - 1).Nombre = "String")
                If Not esTexto Then SacarCisHuerfano(cual)
            Next

            ' Los dos campos que rev-19 expuso, por la misma puerta y con el mismo booleano.
            If Not EscribirTercero() Then noSePudo.Add("Param 3")
            If Not EscribirReferencia() Then noSePudo.Add("Reference")
            If noSePudo.Count > 0 Then
                ' ⛔ EL MENSAJE DICE QUÉ CAMPO, QUE LA ÚNICA SALIDA ES CANCEL, Y LO QUE CANCEL CUESTA. Un
                ' diálogo que dice «no se pudo escribir» y no cierra es un callejón sin salida.
                MessageBox.Show(Me,
                                "These fields could not be written: " & String.Join(", ", noSePudo.ToArray()) & "." &
                                Environment.NewLine & Environment.NewLine &
                                "The value does not fit what the format declares for the branch this " &
                                "condition now selects. Change the value, or press Cancel — which discards " &
                                "EVERY change you made to this condition, not just this field.",
                                "Condition", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                DialogResult = DialogResult.None
                Return
            End If
        Catch ex As Exception
            ' ⛔ SE RESTAURA LA FOTO. El OK escribe `Function`, `Type`, `Run On` y el valor de
            ' comparación ANTES de los parámetros, así que sin esto una excepción en el medio dejaba el
            ' record con la mitad del cambio — y el diálogo abierto, con lo cual un Cancel posterior no
            ' lo desarmaba.
            RestaurarFoto()
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
    ''' <summary>Escribe UN parámetro en la rama vigente. Devuelve False si no se pudo.
    ''' <para>⛔ DEVUELVE Boolean Y EL OK LO MIRA. Era un `Sub`, así que un `PonerValorEnCampo` que no
    ''' podía escribir se perdía en silencio y el diálogo cerraba con OK igual. Las ramas que no se
    ''' pueden editar —«ninguno», la de reserva, la unión anidada— devuelven True: no es que fallaran,
    ''' es que no había nada que escribir.</para></summary>
    ''' <summary>El `Parameter #3`: entero con signo, porque las once ramas son `s32`.</summary>
    ''' <summary>La firma del subrecord que lleva el texto de ese parámetro: `CIS1` para el 1 y `CIS2`
    ''' para el 2. <para>El pareo sale del mismo callback de xEdit que manda el texto ahí:
    ''' <c>Elements[5]</c> es `Parameter #1` y <c>Elements[6]</c> es `Parameter #2` en el struct del
    ''' `CTDA` (0 Type, 1 Unused, 2 Comparison Value, 3 Function, 4 Unused, 5 P#1, 6 P#2). NO se
    ''' cruzan.</para></summary>
    Private Function FirmaDelCis(cual As Integer) As String
        Return If(cual = 1, "CIS1", "CIS2")
    End Function

    ''' <summary>El texto que el `CIS` de ese parámetro trae, o "" si el subrecord no está.
    ''' <para>⚠️ No distingue «ausente» de «presente y vacío» — los dos dan "" — y esa distinción SÍ
    ''' importa al escribir: la lleva <see cref="EscribirTextoDelCis"/> mirando la presencia.</para></summary>
    Private Function TextoDelCis(cual As Integer) As String
        Dim v = Canon.CanonInterpretacion.ValorDeCampo(_cond, FirmaDelCis(cual))
        Return If(TryCast(v, String), "")
    End Function

    ''' <summary>Escribe el texto de un parámetro `ptString` en su `CIS1`/`CIS2`, y **NO toca el u32**.
    '''
    ''' <para>⛔ POR QUÉ NO SE PONE EL u32 EN CERO, aunque xEdit lo haga. `wbConditionStringToInt`
    ''' devuelve <c>Result := 0</c> incondicional, pero eso describe lo que emite XEDIT cuando construye
    ''' el valor. El corpus lo escribió el CK, y ahí el u32 trae residuo con forma de puntero en el
    ''' <b>97 %</b> de los casos (Fallout 4: 14.401 parámetros `ptString`, sólo 426 en cero). Escribir 0
    ''' sería NORMALIZAR un campo que el usuario no tocó.</para>
    ''' <para>⛔ Y el motivo NO es «no aporta identidad»: el <c>cpIgnore</c> y la clave de orden en
    ''' <c>'0'</c> son afirmaciones sobre XEDIT —su detección de conflictos y su orden—, no sobre el
    ''' motor. El motivo verdadero es que el CK no authorea ese campo, deja residuo, y preservarlo es lo
    ''' único que no inventa bytes. Escrito como «no aporta identidad», el próximo lo lee como «se puede
    ''' poner en cero» y el defecto vuelve.</para>
    ''' <para>LA PRESENCIA, con sujeto medido para cada caso: ausente y sin texto ⇒ sigue ausente
    ''' (`CIS2` ausente ×31 en Skyrim); ausente y con texto ⇒ se crea; presente y se vacía ⇒ queda
    ''' presente y VACÍO (×2 en Fallout 4, ×3 en Skyrim — es un estado del corpus, no un accidente);
    ''' presente y con texto ⇒ se escribe.</para></summary>
    Private Function EscribirTextoDelCis(cual As Integer, texto As String) As Boolean
        Dim firma = FirmaDelCis(cual)
        Dim t = If(texto, "")
        Dim estaba As Boolean = (_cond.Node IsNot Nothing AndAlso
                                 _cond.Node.ByFieldPath(firma) IsNot Nothing)
        If t.Length = 0 AndAlso Not estaba Then Return True
        Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, firma, t)
    End Function

    ''' <summary>Saca el `CIS1`/`CIS2` que quedó HUÉRFANO porque el parámetro dejó de ser `ptString`.
    ''' <para>⛔ RELATIVO AL NODO DE ESTA CONDICIÓN, no por firma sobre el árbol.
    ''' <c>WbEdit.RemoveSubrecord</c> mira sólo los hijos de la RAÍZ del record —y el `CIS1` cuelga del
    ''' `Condition` dentro del arreglo `Conditions`, así que devolvería 0 sin sacar nada, en silencio—, y
    ''' <c>RemoveSubrecordEnTodoElArbol</c> se llevaría los `CIS1` de las TRES condiciones de un `HDPT`
    ''' (el corpus tiene 40 `HDPT` con condiciones). <c>QuitarCampo</c> sube hasta el subrecord y lo saca,
    ''' relativo al nodo que se le pasa.</para></summary>
    Private Sub SacarCisHuerfano(cual As Integer)
        If _cond.Node Is Nothing Then Return
        Canon.WbEdit.QuitarCampo(_cond.Node, FirmaDelCis(cual))
    End Sub

    Private Function EscribirTercero() As Boolean
        Dim n As Integer
        If Not Integer.TryParse(TextBoxParam3.Text.Trim(), Globalization.NumberStyles.Integer,
                                Globalization.CultureInfo.InvariantCulture, n) Then Return False
        Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, "CTDA\Parameter #3", n)
    End Function

    ''' <summary>El `Reference`: FormID con `Run On = 2`, y entero sin signo con cualquier otro.
    ''' <para>⛔ Las dos ramas se escriben por la MISMA ruta, y es `AsegurarRamaVigente` quien elige en
    ''' cuál aterriza — con el `Run On` ya escrito. Si se escribiera el FormID sin que la rama se
    ''' re-decidiera, iría a parar al `Int("Unused", u32)` y de ahi NO pasa por `WbFormIdWalker`: no se
    ''' remapearía al índice de master del destino ni entraría en la pasada que los descubre.</para></summary>
    Private Function EscribirReferencia() As Boolean
        If RunOnDelFormulario() = 2L Then
            If TextBoxReference.Tag Is Nothing Then Return True
            Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, "CTDA\Reference",
                                                               CUInt(TextBoxReference.Tag))
        End If
        Dim n As UInteger
        If Not UInteger.TryParse(TextBoxReference.Text.Trim(), Globalization.NumberStyles.Integer,
                                 Globalization.CultureInfo.InvariantCulture, n) Then Return False
        Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, "CTDA\Reference", n)
    End Function

    Private Function EscribirParametro(cual As Integer,
                                       rama As Canon.CanonInterpretacion.RamaDeParametro) As Boolean
        If rama.EsNinguno OrElse rama.EsCruda Then Return True
        Dim ruta = Canon.CanonInterpretacion.RutaDeParametroDeCondicion(cual, rama)
        If ruta.Length = 0 Then Return True
        Dim caja = If(cual = 1, TextBoxParam1, TextBoxParam2)
        Dim combo = If(cual = 1, ComboParam1, ComboParam2)
        Dim claves = _clavesDeEnum(cual - 1)

        If rama.Clase = "especial" AndAlso rama.Nombre = "String" Then
            ' ⛔ EL TEXTO AL `CIS`, Y EL u32 **NO SE TOCA**. Ver `EscribirTextoDelCis`.
            Return EscribirTextoDelCis(cual, caja.Text)
        End If

        If rama.EsReferencia Then
            ' ⛔ «El usuario no eligió nada» NO es una falla: es que no hay nada que escribir. Los cuatro
            ' `Return` mudos que había acá significaban cosas distintas y se leían igual.
            If caja.Tag Is Nothing Then Return True
            Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, CUInt(caja.Tag))

        ElseIf rama.EsEnumerado Then
            ' Por la LISTA DE CLAVES y no por el índice: los valores pueden ser dispersos, y si la
            ' entrada elegida es la extra —la que el enum no nombra— vuelve el valor que traía.
            Dim i = combo.SelectedIndex
            If i < 0 Then Return True
            Dim v As Long = If(i < claves.Count, claves(i), _crudoDeEnum(cual - 1))
            Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, v)

        ElseIf rama.Clase = "float" Then
            Dim f As Single
            ' ⛔ UN `TryParse` QUE FALLA SÍ ES UNA FALLA, y tiene que llegar al OK: acá el texto que el
            ' usuario escribió se descartaba en silencio. `NaN`/`Infinity` PASAN este `TryParse` —los
            ' acepta `InvariantCulture`, verificado— y los rechaza el conversor de dominio, que es donde
            ' está la medición: 251.215 floats del corpus, cero no finitos.
            If Not Single.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Float,
                                   Globalization.CultureInfo.InvariantCulture, f) Then Return False
            Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, f)

        Else
            ' Entero, con o sin signo según lo que declare la rama (`itS32` vs `itU32`): se parsea al
            ' ancho que corresponde para que un valor de 32 bits con el bit alto puesto no se rechace
            ' por «fuera de rango» en la rama sin signo, ni se acepte como positivo en la con signo.
            If rama.ConSigno Then
                Dim n As Integer
                If Not Integer.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Integer,
                                        Globalization.CultureInfo.InvariantCulture, n) Then Return False
                Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, n)
            Else
                Dim n As UInteger
                If Not UInteger.TryParse(caja.Text.Trim(), Globalization.NumberStyles.Integer,
                                         Globalization.CultureInfo.InvariantCulture, n) Then Return False
                Return Canon.CanonInterpretacion.PonerValorEnCampo(_cond, ruta, n)
            End If
        End If
    End Function

End Class
