Imports FO4_Base_Library

''' <summary>Modal editor for a SINGLE <see cref="ARMO_CombinationInclude"/> (an OMOD include) of an OBTS
''' combination, opened from the OBTS Combination editor's Includes tab (Add / Edit / double-click a row).
''' Mirror of <see cref="ObtsPropertyEditor_Form"/>: Designer-built UI, a working copy edited in place,
''' deep-copied at the borders so a Cancel never mutates the caller's include.
'''
''' Replaces the OLD inline-editable GridIncludes (Optional/DontUseAll checkbox cells + AttachPointIndex text
''' cell + OMOD double-click re-pick) so the grid is pure read-only — killing the reentrant crash.</summary>
Public Class ObtsIncludeEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private _omodFormID As UInteger

    ''' <summary>The edited include, valid only after <c>DialogResult.OK</c>. A fresh copy — the caller owns it.</summary>
    Public ReadOnly Property ResultInclude As ARMO_CombinationInclude
        Get
            Return _result
        End Get
    End Property
    Private _result As ARMO_CombinationInclude

    ''' <param name="mainForm">Owner — supplies the PluginManager for the OMOD picker + display names.</param>
    ''' <param name="inc">The include to edit. DEEP-COPIED in (never aliased); Nothing starts empty.</param>
    Public Sub New(mainForm As MainForm, inc As ARMO_CombinationInclude)
        InitializeComponent()
        _mainForm = mainForm

        Dim src = If(inc, New ARMO_CombinationInclude())
        _omodFormID = src.ModFormID
        NumAttach.Value = Math.Max(NumAttach.Minimum, Math.Min(NumAttach.Maximum, CDec(src.AttachPointIndex)))
        CheckOptional.Checked = src.IsOptional
        CheckDontUseAll.Checked = src.DontUseAll
        RenderOmod()

        AddHandler ButtonPickOmod.Click, AddressOf OnPickOmod
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub RenderOmod()
        LabelOmodValue.Text = If(_omodFormID = 0UI, "(none)",
                                 $"{_mainForm.GetRecordDisplayNameForEditor(_omodFormID)} [0x{_omodFormID:X8}]")
    End Sub

    Private Sub OnPickOmod(sender As Object, e As EventArgs)
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"OMOD"},
                                           "Select OMOD include", _omodFormID, allowNull:=False)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            _omodFormID = dlg.SelectedFormID
            RenderOmod()
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        _result = New ARMO_CombinationInclude With {
            .ModFormID = _omodFormID,
            .AttachPointIndex = CByte(NumAttach.Value),
            .IsOptional = CheckOptional.Checked,
            .DontUseAll = CheckDontUseAll.Checked}
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
