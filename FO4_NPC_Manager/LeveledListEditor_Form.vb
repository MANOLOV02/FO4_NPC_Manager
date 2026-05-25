Imports System.Windows.Forms

''' <summary>Small modal to author a new leveled list (LVLI): EditorID name + the three LVLF flag
''' checkboxes (Calculate from all levels ≤ player's level / Calculate for each item in count / Use All) +
''' Chance None + Max Count. Returns the collected values; the caller (<see cref="OutfitPicker_Form"/>)
''' builds the <see cref="LeveledListDraft"/> with a provisional FormID. Entries (the pieces inside) are
''' added afterwards via the picker's "Add to lvl" button.</summary>
Public Class LeveledListEditor_Form

    Private ReadOnly _mainForm As MainForm

    Public Sub New(mainForm As MainForm)
        InitializeComponent()
        _mainForm = mainForm
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ''' <summary>Full EditorID (prefix + typed name).</summary>
    Public ReadOnly Property FullEditorID As String
        Get
            Return LeveledListDraft.EditorIdPrefix & TextBoxName.Text.Trim()
        End Get
    End Property

    Public ReadOnly Property CalcAllLevels As Boolean
        Get
            Return CheckBoxCalcAllLevels.Checked
        End Get
    End Property

    Public ReadOnly Property CalcEachInCount As Boolean
        Get
            Return CheckBoxCalcEachInCount.Checked
        End Get
    End Property

    Public ReadOnly Property UseAll As Boolean
        Get
            Return CheckBoxUseAll.Checked
        End Get
    End Property

    Public ReadOnly Property ChanceNoneValue As Byte
        Get
            Return CByte(NumericChanceNone.Value)
        End Get
    End Property

    Public ReadOnly Property MaxCountValue As Byte
        Get
            Return CByte(NumericMaxCount.Value)
        End Get
    End Property

    ''' <summary>ButtonOk carries DialogResult.OK; veto the auto-close (DialogResult.None) on validation
    ''' failure — empty name or an EditorID already in use.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If TextBoxName.Text.Trim().Length = 0 Then
            MessageBox.Show(Me, "Enter a name for the leveled list.", "New leveled list",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If
        If Not _mainForm.IsLeveledEditorIdAvailable(FullEditorID) Then
            MessageBox.Show(Me, $"EditorID '{FullEditorID}' is already in use. Choose another name.",
                            "New leveled list", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
    End Sub
End Class
