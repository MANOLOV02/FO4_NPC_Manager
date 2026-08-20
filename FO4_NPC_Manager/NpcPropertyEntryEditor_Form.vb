Imports FO4_Base_Library

''' <summary>Modal editor for los dos campos de UNA entrada PRPS (Actor Value FormID + f32 Value) de la
''' lista de propiedades de un NPC, abierto desde la pestaña "Properties" del editor de NPC. Espejo de
''' <see cref="NpcFactionEntryEditor_Form"/>. El picker de valor de actor es sobre AVIF.</summary>
Public Class NpcPropertyEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _avFormID As UInteger

    ''' <summary>El valor de actor elegido, válido sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultFormID As UInteger
        Get
            Return _resultFormID
        End Get
    End Property
    Private _resultFormID As UInteger

    ''' <summary>El valor numérico elegido, válido sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultValue As Single
        Get
            Return _resultValue
        End Get
    End Property
    Private _resultValue As Single

    Public Sub New(mainForm As MainForm, avFormID As UInteger, value As Single)
        InitializeComponent()
        _mainForm = mainForm

        _avFormID = avFormID
        NumValue.Value = ClampDec(CDec(value), NumValue)
        RenderActorValue()

        AddHandler ButtonPickAv.Click, AddressOf OnPickActorValue
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderActorValue()
        TextBoxAv.Tag = _avFormID
        TextBoxAv.Text = If(_avFormID = 0UI, "(none)",
                            $"{_mainForm.GetRecordDisplayNameForEditor(_avFormID)} [0x{_avFormID:X8}]")
    End Sub

    Private Sub OnPickActorValue(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"AVIF"},
                                           "Select Actor Value (PRPS)", _avFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _avFormID = dlg.SelectedFormID
            RenderActorValue()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _avFormID = 0UI Then
            MessageBox.Show(Me, "Choose an Actor Value (AVIF) for this property.", "Property",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _resultFormID = _avFormID
        _resultValue = CSng(NumValue.Value)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
