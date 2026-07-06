Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="NPC_FactionEntry"/> (SNAM Faction FormID + s8 Rank) of an
''' NPC's faction list, opened from the NPC Editor's "Factions" tab (Add/Edit button / double-click a row).
'''
''' A working copy is edited in place and copied out into <see cref="ResultEntry"/> on OK; a Cancel never
''' mutates the caller's entry (mirror of <see cref="ArmoDamageResistEditor_Form"/>). The Faction picker is a
''' FormIdPicker over FACT records.</summary>
Public Class NpcFactionEntryEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _factionFormID As UInteger

    ''' <summary>The edited faction entry, valid only after <c>DialogResult.OK</c>. A fresh copy — caller owns it.</summary>
    Public ReadOnly Property ResultEntry As NPC_FactionEntry
        Get
            Return _result
        End Get
    End Property
    Private _result As NPC_FactionEntry

    ''' <param name="mainForm">Owner — supplies the PluginManager for the FACT picker + display names.</param>
    ''' <param name="entry">The faction entry to edit. Copied in (never aliased); Nothing starts empty.</param>
    Public Sub New(mainForm As MainForm, entry As NPC_FactionEntry)
        InitializeComponent()
        _mainForm = mainForm

        Dim src = If(entry, New NPC_FactionEntry())
        _factionFormID = src.FactionFormID
        NumRank.Value = ClampDec(CDec(src.Rank), NumRank)
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
        _result = New NPC_FactionEntry With {.FactionFormID = _factionFormID, .Rank = CSByte(NumRank.Value)}
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Shared Function ClampDec(v As Decimal, num As NumericUpDown) As Decimal
        If v < num.Minimum Then Return num.Minimum
        If v > num.Maximum Then Return num.Maximum
        Return v
    End Function

End Class
