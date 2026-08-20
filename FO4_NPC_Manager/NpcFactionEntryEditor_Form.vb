Imports FO4_Base_Library

''' <summary>Modal editor for the dos campos de UNA entrada SNAM (Faction FormID + s8 Rank) de la lista de
''' facciones de un NPC, abierto desde la pestaña "Factions" del editor de NPC (botón Add/Edit / doble clic
''' en una fila).
'''
''' Entran y salen los dos VALORES, no una entrada: la entrada es un nodo del record y la escribe el que
''' llama, recién con OK. Cancelar no toca nada. El picker de facción es un FormIdPicker sobre FACT.</summary>
Public Class NpcFactionEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _factionFormID As UInteger

    ''' <summary>La facción elegida, válida sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultFormID As UInteger
        Get
            Return _resultFormID
        End Get
    End Property
    Private _resultFormID As UInteger

    ''' <summary>El rango elegido, válido sólo tras <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property ResultRank As SByte
        Get
            Return _resultRank
        End Get
    End Property
    Private _resultRank As SByte

    ''' <param name="mainForm">Owner — supplies the PluginManager for the FACT picker + display names.</param>
    ''' <param name="factionFormID">La facción con la que arranca el formulario (0 = ninguna).</param>
    ''' <param name="rank">El rango con el que arranca el formulario.</param>
    Public Sub New(mainForm As MainForm, factionFormID As UInteger, rank As SByte)
        InitializeComponent()
        _mainForm = mainForm

        _factionFormID = factionFormID
        NumRank.Value = ClampDec(CDec(rank), NumRank)
        RenderFaction()

        AddHandler ButtonPickFaction.Click, AddressOf OnPickFaction
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderFaction()
        TextBoxFaction.Tag = _factionFormID
        TextBoxFaction.Text = If(_factionFormID = 0UI, "(none)",
                                 $"{_mainForm.GetRecordDisplayNameForEditor(_factionFormID)} [0x{_factionFormID:X8}]")
    End Sub

    ''' <summary>Faction picker — FormIdPicker over FACT. Required within an SNAM entry (allowNull:=False);
    ''' a cancelled/0 selection leaves the current value untouched.</summary>
    Private Sub OnPickFaction(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"FACT"},
                                           "Select Faction (SNAM)", _factionFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _factionFormID = dlg.SelectedFormID
            RenderFaction()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Faction is required — an SNAM entry with a 0 (NULL) faction is meaningless, so don't accept it.
        If _factionFormID = 0UI Then
            MessageBox.Show(Me, "Choose a Faction (FACT) for this entry.", "Faction",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _resultFormID = _factionFormID
        _resultRank = CSByte(NumRank.Value)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
