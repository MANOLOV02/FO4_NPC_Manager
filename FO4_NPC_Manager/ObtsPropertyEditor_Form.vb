Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="OMOD_Property"/> of an OBTS combination, opened from the
''' OBTS Combination editor's Properties tab (Add / Edit / double-click a row). Mirror of
''' <see cref="ObtsCombinationEditor_Form"/>: Designer-built UI, a working copy edited in place, deep-copied
''' at the borders so a Cancel never mutates the caller's property.
'''
''' Replaces the OLD inline-editable GridProperties (ValueType combo cell + FunctionType/PropertyIndex/Value2/
''' Step text cells + the out-of-band Value1 picker) so the grid is pure read-only — killing the reentrant
''' <c>SetCurrentCellAddressCore</c> crash the inline edits caused.
'''
''' Value model faithfulness: <c>Value1</c> stores the raw 4-byte value reinterpreted as a Single, EXACTLY
''' as the old <c>ObtsCombinationEditor_Form.EditValue1ForRow</c> / <c>Value1Display</c> did (the Fase 1
''' round-trip depends on this — do NOT change the bit math). For FormID-typed properties the FormID lives in
''' <c>Value1FormID</c> and Value1 mirrors its bits; for FloatType Value1 is the float directly; for the
''' integer buckets Value1 holds the Int32 bits. Opening the modal on an existing FormID property DISPLAYS its
''' Value1FormID without re-resolving or overwriting it — the FormID only changes when the user picks in the
''' FormID dialog.</summary>
Public Class ObtsPropertyEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    Private ReadOnly _mainForm As MainForm
    ''' <summary>Ver el <c>param</c> del constructor. Decide si el selector de <c>Value1</c> ofrece
    ''' borradores propios.</summary>
    Private ReadOnly _duenoCensado As Boolean
    ''' <summary>The working copy (source of truth). Deep-copied from the incoming property in the ctor; copied
    ''' out into <see cref="ResultProperty"/> on OK. Never aliased to the caller's instance.</summary>
    Private ReadOnly _prop As OMOD_Property
    ''' <summary>Suppresses the ValueType-change reaction while the ctor loads the combo selection.</summary>
    Private _loading As Boolean

    ''' <summary>The edited property, valid only after <c>DialogResult.OK</c>. A fresh copy — the caller owns it.</summary>
    Public ReadOnly Property ResultProperty As OMOD_Property
        Get
            Return _result
        End Get
    End Property
    Private _result As OMOD_Property

    ''' <param name="mainForm">Owner — supplies the PluginManager for the Value1 FormID picker + display names.</param>
    ''' <param name="prop">The property to edit. DEEP-COPIED in (never aliased); Nothing starts a fresh IntType.</param>
    ''' <param name="duenoCensado">¿El record DUEÑO de esta combinación está en el censo de
    ''' referencias de borrador?
    ''' <para>⛔⛔ NO es «de dónde vengo»: es la ÚNICA condición que hace seguro ofrecer borradores en
    ''' el selector de <c>Value1</c>. Un ARMO borrador SÍ está censado — <c>CensoDeReferencias.DeBorrador</c>
    ''' recorre sus propiedades de OBTS —; un override de record de NPC NO: es la «segunda casa» que
    ''' <c>NPC/ReferenciasDeBorrador.vb</c> declara, y ni el censo ni el remapeo de la promoción la
    ''' recorren. Ofrecer borradores con un dueño sin censar significa que «Delete / Revert…» diría que a
    ''' ese borrador no lo referencia nadie, se borraría, Y el <c>.esp</c> saldría con el <c>0xFF</c>
    ''' provisional adentro de la propiedad — un cambio de BYTES que nadie pidió.</para>
    ''' <para>⛔ El default es la opción SEGURA y la insegura hay que ESCRIBIRLA: no es un centinela.
    ''' Un llamador que se olvide no rompe nada — sólo no ofrece borradores.</para></param>
    Public Sub New(mainForm As MainForm, prop As OMOD_Property,
                   Optional duenoCensado As Boolean = False)
        _duenoCensado = duenoCensado
        InitializeComponent()
        _mainForm = mainForm

        ' Deep-copy in so Cancel leaves the caller's property intact.
        Dim src = If(prop, New OMOD_Property With {.ValueType = OMOD_ValueType.IntType})
        _prop = New OMOD_Property With {
            .ValueType = src.ValueType, .FunctionType = src.FunctionType, .PropertyIndex = src.PropertyIndex,
            .Value1 = src.Value1, .Value1FormID = src.Value1FormID, .Value2 = src.Value2, .StepValue = src.StepValue}

        For Each nm In [Enum].GetNames(GetType(OMOD_ValueType))
            ComboValueType.Items.Add(nm)
        Next

        _loading = True
        Try
            ComboValueType.SelectedItem = _prop.ValueType.ToString()
            If ComboValueType.SelectedIndex < 0 AndAlso ComboValueType.Items.Count > 0 Then ComboValueType.SelectedIndex = 0
            RebuildFunctionCombo(_prop.FunctionType)
            NumIndex.Value = ClampDec(_prop.PropertyIndex, NumIndex)
            TextBoxStep.Text = FloatText(_prop.StepValue)
        Finally
            _loading = False
        End Try
        RenderValue1()   ' shows the FormID display (no re-resolve) or the numeric text per the current type
        RenderValue2()   ' Value2 is ALSO ValueType-dependent (int/float/bool/unused) — not always a float

        AddHandler ComboValueType.SelectedIndexChanged, AddressOf OnValueTypeChanged
        AddHandler ButtonPickValue1.Click, AddressOf OnPickValue1
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ''' <summary>Is <paramref name="vt"/> one of the two FormID buckets (Value1 = a FormID reference)?</summary>
    Private Shared Function IsFormIdType(vt As OMOD_ValueType) As Boolean
        Return vt = OMOD_ValueType.FormIDInt OrElse vt = OMOD_ValueType.FormIDFloat
    End Function

    ''' <summary>ValueType changed → first flush the current numeric text into <c>Value1</c> (if the OLD type was
    ''' numeric), then adopt the new type. Leaving a FormID bucket clears the resolved FormID (raw bits stay);
    ''' the Value1 UI is re-rendered so it switches between the FormID display and the numeric textbox.</summary>
    Private Sub OnValueTypeChanged(sender As Object, e As EventArgs)
        If _loading Then Return
        Dim oldType = _prop.ValueType
        If Not IsFormIdType(oldType) Then FlushNumericValue1(oldType)
        FlushValue2(oldType)   ' preserve the current Value2 (interpreted per the OLD type) before switching

        ' Capture the live FunctionType byte BEFORE the type (and thus the name list) changes, so it is preserved.
        Dim curFunc = CurrentFunctionByte()

        Dim vt As OMOD_ValueType
        If [Enum].TryParse(Of OMOD_ValueType)(CStr(ComboValueType.SelectedItem), vt) Then _prop.ValueType = vt
        ' Changing to a non-FormID bucket must clear Value1FormID (a bucket switch is an explicit user action).
        If Not IsFormIdType(_prop.ValueType) Then _prop.Value1FormID = 0UI
        RebuildFunctionCombo(curFunc)   ' the FunctionType name list is ValueType-dependent (see FunctionNamesFor)
        RenderValue1()
        RenderValue2()   ' re-display the same Value2 bits under the NEW type's interpretation
    End Sub

    ''' <summary>Per-ValueType FunctionType enum name lists — sourced VERBATIM from the OMOD
    ''' Properties format. The union has four cases; el ValueType decide a cuál mapea:
    '''   Int(0)/Float(1)/FormIDFloat(6) -> union case 0 (Float): 'SET','MUL+ADD','ADD'
    '''   Bool(2)                        -> union case 1 (Bool):  'SET','AND','OR'
    '''   Enum(5)                        -> union case 2 (Enum):  'SET'
    '''   FormIDInt(4)                   -> union case 3 (FormID):'SET','REM','ADD'
    '''   String(3) [absent from decider] -> decider default Result:=0 (Float case).
    ''' The stored byte is the 0-based index INTO the per-type list (index == byte), so SelectedIndex round-trips
    ''' byte-exactly.</summary>
    Private Shared Function FunctionNamesFor(vt As OMOD_ValueType) As String()
        Select Case vt
            Case OMOD_ValueType.BoolType
                Return New String() {"SET", "AND", "OR"}
            Case OMOD_ValueType.EnumType
                Return New String() {"SET"}
            Case OMOD_ValueType.FormIDInt
                Return New String() {"SET", "REM", "ADD"}
            Case Else   ' Int, Float, String, FormIDFloat all route to the Float case.
                Return New String() {"SET", "MUL+ADD", "ADD"}
        End Select
    End Function

    ''' <summary>Holds an out-of-range FunctionType byte (>= the current type's name count) losslessly: when
    ''' non-negative, ComboFunction's LAST item is a raw placeholder carrying this byte. -1 = no fallback item.</summary>
    Private _functionFallbackByte As Integer = -1

    ''' <summary>Repopulate <see cref="ComboFunction"/> for the current <c>_prop.ValueType</c> and select the item
    ''' matching <paramref name="preserveByte"/>. In-range bytes select by index (index == byte); an out-of-range
    ''' byte is kept as a trailing raw placeholder so no data is ever lost.</summary>
    Private Sub RebuildFunctionCombo(preserveByte As Integer)
        ComboFunction.Items.Clear()
        _functionFallbackByte = -1
        Dim names = FunctionNamesFor(_prop.ValueType)
        For Each nm In names
            ComboFunction.Items.Add(nm)
        Next
        If preserveByte >= 0 AndAlso preserveByte < names.Length Then
            ComboFunction.SelectedIndex = preserveByte
        Else
            _functionFallbackByte = preserveByte
            ComboFunction.Items.Add($"<raw {preserveByte}>")
            ComboFunction.SelectedIndex = ComboFunction.Items.Count - 1
        End If
    End Sub

    ''' <summary>The FunctionType byte currently selected in <see cref="ComboFunction"/>: the raw fallback value if
    ''' its placeholder is selected, else the SelectedIndex (which equals the byte for a named item).</summary>
    Private Function CurrentFunctionByte() As Integer
        If _functionFallbackByte >= 0 AndAlso ComboFunction.SelectedIndex = ComboFunction.Items.Count - 1 Then
            Return _functionFallbackByte
        End If
        If ComboFunction.SelectedIndex < 0 Then Return 0
        Return ComboFunction.SelectedIndex
    End Function

    ''' <summary>Show the right Value1 editor for the current ValueType: FormID types → a read-only display of
    ''' the CURRENT <c>Value1FormID</c> (never re-resolved) + the Choose button; else the numeric textbox
    ''' pre-filled with the float (FloatType) or the Int32 reinterpretation of the raw bits.</summary>
    Private Sub RenderValue1()
        Dim isFid = IsFormIdType(_prop.ValueType)
        LabelValue1FormID.Visible = isFid
        ButtonPickValue1.Visible = isFid
        TextBoxValue1.Visible = Not isFid
        If isFid Then
            LabelValue1FormID.Text = FormIdDisplay(_prop.Value1FormID)
        ElseIf _prop.ValueType = OMOD_ValueType.FloatType Then
            TextBoxValue1.Text = FloatText(_prop.Value1)
        Else
            TextBoxValue1.Text = BitConverter.ToInt32(BitConverter.GetBytes(_prop.Value1), 0).ToString(CultureInfo.InvariantCulture)
        End If
    End Sub

    ''' <summary>Read <see cref="TextBoxValue1"/> into <c>Value1</c> using the bit-exact semantics of the old
    ''' <c>EditValue1ForRow</c>: FloatType stores the float directly; every other numeric bucket stores the
    ''' Int32 bits reinterpreted as a Single. Invalid/blank input leaves the value unchanged.</summary>
    Private Sub FlushNumericValue1(vt As OMOD_ValueType)
        Dim text = TextBoxValue1.Text.Trim()
        If text.Length = 0 Then Return
        If vt = OMOD_ValueType.FloatType Then
            Dim f As Single
            If Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, f) Then _prop.Value1 = f
        Else
            Dim n As Integer
            If Integer.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, n) Then
                _prop.Value1 = BitConverter.ToSingle(BitConverter.GetBytes(n), 0)
            End If
        End If
    End Sub

    ''' <summary>QUÉ FILAS PROPIAS ofrece el selector de <c>Value1</c>. Vacía cuando el record dueño
    ''' NO está censado.
    ''' <para>⛔ Vive acá afuera, <c>Friend Shared</c> y PURA, por lo mismo que
    ''' <c>OutfitPicker_Form.PlanDeCierreDeListas</c>: adentro del manejador de un botón el testigo no
    ''' la puede correr — tendría que abrir un modal, que lo cuelga — y un caso que mira el TEXTO del
    ''' manejador mide la letra en vez de la conducta. No es un gancho de prueba: es la MISMA función
    ''' que usa el botón.</para>
    ''' <para>De las cinco firmas que el campo acepta, sólo tres tienen borrador: OMOD y KYWD no, así
    ''' que no hay nada que ofrecer de ellas.</para></summary>
    Friend Shared Function FilasPropiasDeValue1(mainForm As MainForm, duenoCensado As Boolean) As List(Of FormIdPickerEntry)
        Dim entradas As New List(Of FormIdPickerEntry)
        If mainForm Is Nothing OrElse Not duenoCensado Then Return entradas
        entradas.AddRange(mainForm.MswpDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "MSWP", .PluginName = If(d.IsOverride, "(override)", "(new)")}))
        entradas.AddRange(mainForm.ArmoDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "ARMO", .PluginName = If(d.IsOverride, "(override)", "(new)")}))
        entradas.AddRange(mainForm.ArmaDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "ARMA", .PluginName = If(d.IsOverride, "(override)", "(new)")}))
        For Each sig In New String() {"MSWP", "ARMO", "ARMA"}
            entradas.AddRange(BorradoDeBorradores.EntradasPropias(mainForm, sig, entradas))
        Next
        Return entradas
    End Function

    ''' <summary>FormID Value1 picker — SAME broad signature set + bit mirroring as the old
    ''' <c>EditValue1ForRow</c> (no PropertyIndex→signature table in the model yet, TODO). Only this explicit
    ''' action mutates <c>Value1FormID</c>; Value1 mirrors the picked FormID's bits.
    ''' <para>⛔ OFRECE LOS BORRADORES PROPIOS — pero SÓLO si el record dueño está censado.</para>
    ''' <para>De las cinco firmas que este campo acepta, tres tienen borrador (MSWP, ARMO, ARMA);
    ''' OMOD y KYWD no, así que no hay nada que ofrecer de ellas. Hasta esta ola no se ofrecía
    ''' ninguna: el usuario no podía apuntar una propiedad de object template a un record suyo sin
    ''' guardar primero.</para>
    ''' <para>⛔ Y con su camino de BAJA, que la fábrica arma sola. Este diálogo no toma ningún
    ''' borrador y escribe <c>_prop.Value1FormID</c> apenas vuelve el selector, así que va SIN
    ''' dueño — y el nombre de la fábrica obliga a decirlo.</para></summary>
    ''' <summary>EL SELECTOR DE <c>Value1</c>, ARMADO Y SIN MOSTRAR.
    ''' <para>⛔⛔ La CONSTRUCCIÓN es la ley —con filas propias y camino de baja, o con el ctor plano—
    ''' y vive afuera del manejador por lo mismo que <see cref="FilasPropiasDeValue1"/>: adentro del
    ''' clic el testigo no la puede correr, porque <c>ShowDialog</c> lo cuelga, y mirar el FUENTE mide
    ''' la ORTOGRAFÍA. El eje medía el INSUMO (las filas) y no el CONSUMO: el mutante «usar siempre el
    ''' ctor plano» sobrevivía VERDE. Así el gate construye EL MISMO objeto que ve el usuario.</para></summary>
    Friend Shared Function SelectorDeValue1(mainForm As MainForm,
                                            dueno As BorradoDeBorradores.IDuenoDeBorradores,
                                            duenoCensado As Boolean,
                                            actual As UInteger) As FormIdPicker_Form
        Dim entradas = FilasPropiasDeValue1(mainForm, duenoCensado)
        If entradas.Count = 0 Then
            Return New FormIdPicker_Form(mainForm.PluginManagerForEditor,
                                         {"MSWP", "OMOD", "KYWD", "ARMO", "ARMA"},
                                         "Pick Value1 (FormID)", actual, allowNull:=True)
        End If
        Return FormIdPicker_Form.ParaBorradores(mainForm, dueno,
                                                {"MSWP", "OMOD", "KYWD", "ARMO", "ARMA"},
                                                "Pick Value1 (FormID)", actual, True, entradas)
    End Function

    Private Sub OnPickValue1(sender As Object, e As EventArgs)
        ' ⛔ EL MISMO OBJETO QUE MIDE EL GATE: la construcción vive en `SelectorDeValue1`, no acá.
        Using dlg As FormIdPicker_Form = SelectorDeValue1(_mainForm, Me, _duenoCensado, _prop.Value1FormID)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _prop.Value1FormID = dlg.SelectedFormID
            _prop.Value1 = BitConverter.ToSingle(BitConverter.GetBytes(_prop.Value1FormID), 0)
            LabelValue1FormID.Text = FormIdDisplay(_prop.Value1FormID)
        End Using
    End Sub

    '==============================================================================================
    ' EL CONTRATO CON LA SEDE DE BAJA — `BorradoDeBorradores.IDuenoDeBorradores`
    '==============================================================================================

    ''' <summary>Este diálogo no TOMA ningún borrador: edita una propiedad suelta.</summary>
    Private Function FormIdTomado() As UInteger Implements BorradoDeBorradores.IDuenoDeBorradores.FormIdTomado
        Return 0UI
    End Function

    ''' <summary>⛔⛔ LA REFERENCIA DE ESTA PROPIEDAD NO ESTÁ EN NINGÚN RECORD REGISTRADO, y por eso
    ''' el censo no la ve.
    ''' <para>Las combinaciones de OBTS se editan sobre una COPIA despegada del record
    ''' (<c>ArmoEditor_Form</c>: <c>_comboHost = fo4.Copia()</c>), y la propiedad recién llega al
    ''' record del borrador tras el OK del modal de la combinación más el debounce. Mientras tanto
    ''' <c>GetDraftReferrers</c> no la encuentra: sin esto, la baja diría que a ese borrador no lo
    ''' apunta nadie y el usuario confirmaría A CIEGAS — y la propiedad quedaría con un 0xFF que el
    ''' remapeo no resuelve.</para>
    ''' <para>⚠️ <c>duenoCensado</c> contesta OTRA pregunta —¿qué record es el dueño?— y no tapa
    ''' éste, que es ¿la referencia llegó al record?</para></summary>
    Private Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String) _
            Implements BorradoDeBorradores.IDuenoDeBorradores.ReferenciasNoVolcadas
        If formID <> 0UI AndAlso formID = _prop.Value1FormID Then Return New String() {"object template property (not accepted yet)"}
        Return Enumerable.Empty(Of String)()
    End Function

    ''' <summary>POSTCONDICIÓN: si dieron de baja el record que esta propiedad apunta, se limpia —
    ''' aceptar con un FormID recién borrado escribiría una referencia muerta.</summary>
    Private Sub TrasLaBaja(formID As UInteger) Implements BorradoDeBorradores.IDuenoDeBorradores.TrasLaBaja
        If formID = 0UI OrElse formID <> _prop.Value1FormID Then Return
        _prop.Value1FormID = 0UI
        _prop.Value1 = 0.0F
        LabelValue1FormID.Text = FormIdDisplay(0UI)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' ValueType is already committed by the change handler; flush the remaining scalar controls.
        _prop.FunctionType = CByte(CurrentFunctionByte())
        _prop.PropertyIndex = CUShort(NumIndex.Value)
        If Not IsFormIdType(_prop.ValueType) Then FlushNumericValue1(_prop.ValueType)
        FlushValue2(_prop.ValueType)
        _prop.StepValue = ParseFloat(TextBoxStep.Text, _prop.StepValue)

        ' Copy out so the returned property never aliases the working copy.
        _result = New OMOD_Property With {
            .ValueType = _prop.ValueType, .FunctionType = _prop.FunctionType, .PropertyIndex = _prop.PropertyIndex,
            .Value1 = _prop.Value1, .Value1FormID = _prop.Value1FormID, .Value2 = _prop.Value2, .StepValue = _prop.StepValue}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Value2's storage type según su ValueType: FLOAT for Float(1)/FormIDFloat(6); INT for
    ''' Int(0)/FormIDInt(4); BOOL(=int) for Bool(2);
    ''' UNUSED (4 padding bytes) for String(3)/Enum(5). So Value2 is NOT always a float — displaying it as one
    ''' reinterprets an int's bits and shows a garbage denormal.</summary>
    Private Shared Function Value2IsFloat(vt As OMOD_ValueType) As Boolean
        Return vt = OMOD_ValueType.FloatType OrElse vt = OMOD_ValueType.FormIDFloat
    End Function

    ''' <summary>True unless Value2 is the unused 4-byte padding (String/Enum types) — those keep their raw bytes
    ''' untouched (read-only display).</summary>
    Private Shared Function Value2IsUsed(vt As OMOD_ValueType) As Boolean
        Return vt <> OMOD_ValueType.StringType AndAlso vt <> OMOD_ValueType.EnumType
    End Function

    ''' <summary>Show Value2 per the current ValueType: float (Float/FormIDFloat), the Int32 reinterpretation of
    ''' the raw bits (Int/FormIDInt/Bool), or "(unused)" read-only for String/Enum. Mirror of
    ''' <see cref="RenderValue1"/>'s numeric branch.</summary>
    Private Sub RenderValue2()
        Dim used = Value2IsUsed(_prop.ValueType)
        TextBoxValue2.Enabled = used
        If Not used Then
            TextBoxValue2.Text = "(unused)"
        ElseIf Value2IsFloat(_prop.ValueType) Then
            TextBoxValue2.Text = FloatText(_prop.Value2)
        Else
            TextBoxValue2.Text = BitConverter.ToInt32(BitConverter.GetBytes(_prop.Value2), 0).ToString(CultureInfo.InvariantCulture)
        End If
    End Sub

    ''' <summary>Read <see cref="TextBoxValue2"/> into <c>Value2</c> using the same bit-exact rule as
    ''' <see cref="FlushNumericValue1"/>: float types store the float directly; int/bool types store the Int32
    ''' bits reinterpreted as a Single; unused types keep the raw bytes. Blank/invalid input leaves it unchanged.</summary>
    Private Sub FlushValue2(vt As OMOD_ValueType)
        If Not Value2IsUsed(vt) Then Return
        Dim text = TextBoxValue2.Text.Trim()
        If text.Length = 0 Then Return
        If Value2IsFloat(vt) Then
            Dim f As Single
            If Single.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, f) Then _prop.Value2 = f
        Else
            Dim n As Integer
            If Integer.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, n) Then
                _prop.Value2 = BitConverter.ToSingle(BitConverter.GetBytes(n), 0)
            End If
        End If
    End Sub

    ' ===== helpers =====

    Private Function FormIdDisplay(fid As UInteger) As String
        If fid = 0UI Then Return "(none)"
        Return $"{_mainForm.GetRecordDisplayNameForEditor(fid)} [0x{fid:X8}]"
    End Function

    Private Shared Function FloatText(v As Single) As String
        Return v.ToString(CultureInfo.InvariantCulture)
    End Function

    Private Shared Function ParseFloat(text As String, fallback As Single) As Single
        Dim v As Single
        If Single.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, v) Then Return v
        Return fallback
    End Function

    Private Shared Function ClampDec(v As Integer, num As NumericUpDown) As Decimal
        Dim d As Decimal = CDec(v)
        If d < num.Minimum Then Return num.Minimum
        If d > num.Maximum Then Return num.Maximum
        Return d
    End Function

End Class
