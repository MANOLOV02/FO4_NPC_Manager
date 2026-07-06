Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="NPC_PerkEntry"/> (PRKR Perk FormID + u8 Rank) of an NPC's
''' perk list, opened from the NPC Editor's "Perks" tab. Mirror of <see cref="NpcFactionEntryEditor_Form"/>:
''' working copy in, fresh copy out on OK, Cancel never mutates the caller. Picker is over PERK records.</summary>
Public Class NpcPerkEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _perkFormID As UInteger

    ''' <summary>The edited perk entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As NPC_PerkEntry
        Get
            Return _result
        End Get
    End Property
    Private _result As NPC_PerkEntry

    Public Sub New(mainForm As MainForm, entry As NPC_PerkEntry)
        InitializeComponent()
        _mainForm = mainForm

        Dim src = If(entry, New NPC_PerkEntry())
        _perkFormID = src.PerkFormID
        NumRank.Value = ClampDec(CDec(src.Rank), NumRank)
        RenderPerk()

        AddHandler ButtonPickPerk.Click, AddressOf OnPickPerk
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderPerk()
        TextBoxPerk.Tag = _perkFormID
        TextBoxPerk.Text = If(_perkFormID = 0UI, "(none)",
                              $"{_mainForm.GetRecordDisplayNameForEditor(_perkFormID)} [0x{_perkFormID:X8}]")
    End Sub

    Private Sub OnPickPerk(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"PERK"},
                                           "Select Perk (PRKR)", _perkFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _perkFormID = dlg.SelectedFormID
            RenderPerk()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _perkFormID = 0UI Then
            MessageBox.Show(Me, "Choose a Perk (PERK) for this entry.", "Perk",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _result = New NPC_PerkEntry With {.PerkFormID = _perkFormID, .Rank = CByte(NumRank.Value)}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
