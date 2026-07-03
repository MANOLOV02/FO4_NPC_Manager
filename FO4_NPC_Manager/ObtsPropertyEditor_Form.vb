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

    Private ReadOnly _mainForm As MainForm
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
    Public Sub New(mainForm As MainForm, prop As OMOD_Property)
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
            NumFunction.Value = ClampDec(_prop.FunctionType, NumFunction)
            NumIndex.Value = ClampDec(_prop.PropertyIndex, NumIndex)
            TextBoxValue2.Text = FloatText(_prop.Value2)
            TextBoxStep.Text = FloatText(_prop.StepValue)
        Finally
            _loading = False
        End Try
        RenderValue1()   ' shows the FormID display (no re-resolve) or the numeric text per the current type

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

        Dim vt As OMOD_ValueType
        If [Enum].TryParse(Of OMOD_ValueType)(CStr(ComboValueType.SelectedItem), vt) Then _prop.ValueType = vt
        ' Changing to a non-FormID bucket must clear Value1FormID (a bucket switch is an explicit user action).
        If Not IsFormIdType(_prop.ValueType) Then _prop.Value1FormID = 0UI
        RenderValue1()
    End Sub

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

    ''' <summary>FormID Value1 picker — SAME broad signature set + bit mirroring as the old
    ''' <c>EditValue1ForRow</c> (no PropertyIndex→signature table in the model yet, TODO). Only this explicit
    ''' action mutates <c>Value1FormID</c>; Value1 mirrors the picked FormID's bits.</summary>
    Private Sub OnPickValue1(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor,
                                           {"MSWP", "OMOD", "KYWD", "ARMO", "ARMA"},
                                           "Pick Value1 (FormID)", _prop.Value1FormID, allowNull:=True)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            _prop.Value1FormID = dlg.SelectedFormID
            _prop.Value1 = BitConverter.ToSingle(BitConverter.GetBytes(_prop.Value1FormID), 0)
            LabelValue1FormID.Text = FormIdDisplay(_prop.Value1FormID)
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' ValueType is already committed by the change handler; flush the remaining scalar controls.
        _prop.FunctionType = CByte(NumFunction.Value)
        _prop.PropertyIndex = CUShort(NumIndex.Value)
        If Not IsFormIdType(_prop.ValueType) Then FlushNumericValue1(_prop.ValueType)
        _prop.Value2 = ParseFloat(TextBoxValue2.Text, _prop.Value2)
        _prop.StepValue = ParseFloat(TextBoxStep.Text, _prop.StepValue)

        ' Copy out so the returned property never aliases the working copy.
        _result = New OMOD_Property With {
            .ValueType = _prop.ValueType, .FunctionType = _prop.FunctionType, .PropertyIndex = _prop.PropertyIndex,
            .Value1 = _prop.Value1, .Value1FormID = _prop.Value1FormID, .Value2 = _prop.Value2, .StepValue = _prop.StepValue}
        DialogResult = DialogResult.OK
        Close()
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
