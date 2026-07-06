Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="NPC_PropertyEntry"/> (PRPS Actor Value FormID + f32 Value)
''' of an NPC's properties list, opened from the NPC Editor's "Properties" tab. Mirror of
''' <see cref="NpcFactionEntryEditor_Form"/>. The Actor Value picker is over AVIF records.</summary>
Public Class NpcPropertyEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _avFormID As UInteger

    ''' <summary>The edited property entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As NPC_PropertyEntry
        Get
            Return _result
        End Get
    End Property
    Private _result As NPC_PropertyEntry

    Public Sub New(mainForm As MainForm, entry As NPC_PropertyEntry)
        InitializeComponent()
        _mainForm = mainForm

        Dim src = If(entry, New NPC_PropertyEntry())
        _avFormID = src.ActorValueFormID
        NumValue.Value = ClampDec(CDec(src.Value), NumValue)
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
        _result = New NPC_PropertyEntry With {.ActorValueFormID = _avFormID, .Value = CSng(NumValue.Value)}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
