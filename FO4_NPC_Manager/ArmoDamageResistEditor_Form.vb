Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="ARMO_DamageResist"/> (DMGT Damage Type FormID + Value) of an
''' ARMO's DAMA block, opened from the ARMO Editor's "Damage Resist" tab (Add/Edit button / double-click a row).
'''
''' A working copy is edited in place and copied out into <see cref="ResultEntry"/> on OK; a Cancel never mutates
''' the caller's entry (mirror of <see cref="ArmoAddonEditor_Form"/>). The Damage Type picker is a FormIdPicker
''' over DMGT records.</summary>
Public Class ArmoDamageResistEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _damageTypeFormID As UInteger

    ''' <summary>The edited resistance entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As ARMO_DamageResist
        Get
            Return _result
        End Get
    End Property
    Private _result As ARMO_DamageResist

    ''' <param name="mainForm">Owner — supplies the PluginManager for the DMGT picker + display names.</param>
    ''' <param name="entry">The resistance entry to edit. DEEP-COPIED in (never aliased); Nothing starts empty.</param>
    Public Sub New(mainForm As MainForm, entry As ARMO_DamageResist)
        InitializeComponent()
        _mainForm = mainForm

        Dim src = If(entry, New ARMO_DamageResist())
        _damageTypeFormID = src.DamageTypeFormID
        NumValue.Value = ClampDec(CDec(src.Value), NumValue)
        RenderDamageType()

        AddHandler ButtonPickDamageType.Click, AddressOf OnPickDamageType
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderDamageType()
        TextBoxDamageType.Tag = _damageTypeFormID
        TextBoxDamageType.Text = If(_damageTypeFormID = 0UI, "(none)",
                                    $"{_mainForm.GetRecordDisplayNameForEditor(_damageTypeFormID)} [0x{_damageTypeFormID:X8}]")
    End Sub

    ''' <summary>Damage Type picker — FormIdPicker over DMGT. Required within a DAMA entry (allowNull:=False);
    ''' a cancelled/0 selection leaves the current value untouched.</summary>
    Private Sub OnPickDamageType(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"DMGT"},
                                           "Select Damage Type (DMGT)", _damageTypeFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _damageTypeFormID = dlg.SelectedFormID
            RenderDamageType()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Damage Type is required — a DAMA entry with a 0 (NULL) type is meaningless, so don't accept it.
        If _damageTypeFormID = 0UI Then
            MessageBox.Show(Me, "Choose a Damage Type (DMGT) for this resistance.", "Damage Resist",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _result = New ARMO_DamageResist With {.DamageTypeFormID = _damageTypeFormID, .Value = CUInt(NumValue.Value)}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
