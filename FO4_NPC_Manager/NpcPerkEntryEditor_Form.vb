Imports FO4_Base_Library

''' <summary>Modal editor for los dos campos de UNA entrada PRKR (Perk FormID + u8 Rank) de la lista de
''' ventajas de un NPC, abierto desde la pestaña "Perks" del editor de NPC. Espejo de
''' <see cref="NpcFactionEntryEditor_Form"/>: entran y salen los dos VALORES, la entrada del record la
''' escribe el que llama recién con OK. El picker es sobre PERK.</summary>
Public Class NpcPerkEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _perkFormID As UInteger

    ''' <summary>La ventaja elegida, válida sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultFormID As UInteger
        Get
            Return _resultFormID
        End Get
    End Property
    Private _resultFormID As UInteger

    ''' <summary>El rango elegido, válido sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultRank As Byte
        Get
            Return _resultRank
        End Get
    End Property
    Private _resultRank As Byte

    Public Sub New(mainForm As MainForm, perkFormID As UInteger, rank As Byte)
        InitializeComponent()
        _mainForm = mainForm

        _perkFormID = perkFormID
        NumRank.Value = ClampDec(CDec(rank), NumRank)
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
        _resultFormID = _perkFormID
        _resultRank = CByte(NumRank.Value)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
