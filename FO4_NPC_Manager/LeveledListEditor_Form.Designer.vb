' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class LeveledListEditor_Form
    Inherits System.Windows.Forms.Form

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then components.Dispose()
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        LabelName = New Label()
        TextBoxName = New TextBox()
        CheckBoxCalcAllLevels = New CheckBox()
        CheckBoxCalcEachInCount = New CheckBox()
        CheckBoxUseAll = New CheckBox()
        LabelChanceNone = New Label()
        NumericChanceNone = New NumericUpDown()
        LabelMaxCount = New Label()
        NumericMaxCount = New NumericUpDown()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumericMaxCount, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        '
        ' LabelName
        '
        LabelName.AutoSize = True
        LabelName.Location = New Point(12, 15)
        LabelName.Name = "LabelName"
        LabelName.Text = "EDID: npcm_LVLI_"
        '
        ' TextBoxName
        '
        TextBoxName.Location = New Point(124, 12)
        TextBoxName.Name = "TextBoxName"
        TextBoxName.PlaceholderText = "name"
        TextBoxName.Size = New Size(256, 23)
        '
        ' CheckBoxCalcAllLevels
        '
        CheckBoxCalcAllLevels.AutoSize = True
        CheckBoxCalcAllLevels.Location = New Point(12, 48)
        CheckBoxCalcAllLevels.Name = "CheckBoxCalcAllLevels"
        CheckBoxCalcAllLevels.Text = "Calculate from all levels <= player's level (0x01)"
        '
        ' CheckBoxCalcEachInCount
        '
        CheckBoxCalcEachInCount.AutoSize = True
        CheckBoxCalcEachInCount.Location = New Point(12, 72)
        CheckBoxCalcEachInCount.Name = "CheckBoxCalcEachInCount"
        CheckBoxCalcEachInCount.Text = "Calculate for each item in count (0x02)"
        '
        ' CheckBoxUseAll
        '
        CheckBoxUseAll.AutoSize = True
        CheckBoxUseAll.Location = New Point(12, 96)
        CheckBoxUseAll.Name = "CheckBoxUseAll"
        CheckBoxUseAll.Text = "Use All (0x04)"
        '
        ' LabelChanceNone
        '
        LabelChanceNone.AutoSize = True
        LabelChanceNone.Location = New Point(12, 130)
        LabelChanceNone.Name = "LabelChanceNone"
        LabelChanceNone.Text = "Chance None (%):"
        '
        ' NumericChanceNone
        '
        NumericChanceNone.Location = New Point(124, 128)
        NumericChanceNone.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        NumericChanceNone.Name = "NumericChanceNone"
        NumericChanceNone.Size = New Size(56, 23)
        '
        ' LabelMaxCount
        '
        LabelMaxCount.AutoSize = True
        LabelMaxCount.Location = New Point(210, 130)
        LabelMaxCount.Name = "LabelMaxCount"
        LabelMaxCount.Text = "Max Count:"
        '
        ' NumericMaxCount
        '
        NumericMaxCount.Location = New Point(288, 128)
        NumericMaxCount.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumericMaxCount.Name = "NumericMaxCount"
        NumericMaxCount.Size = New Size(56, 23)
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(224, 168)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(75, 26)
        ButtonOk.Text = "OK"
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(305, 168)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 26)
        ButtonCancel.Text = "Cancel"
        '
        ' LeveledListEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(392, 206)
        Font = New Font("Segoe UI", 9.0F)
        Controls.Add(LabelName)
        Controls.Add(TextBoxName)
        Controls.Add(CheckBoxCalcAllLevels)
        Controls.Add(CheckBoxCalcEachInCount)
        Controls.Add(CheckBoxUseAll)
        Controls.Add(LabelChanceNone)
        Controls.Add(NumericChanceNone)
        Controls.Add(LabelMaxCount)
        Controls.Add(NumericMaxCount)
        Controls.Add(ButtonOk)
        Controls.Add(ButtonCancel)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "LeveledListEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "New leveled list"
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).EndInit()
        CType(NumericMaxCount, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelName As System.Windows.Forms.Label
    Friend WithEvents TextBoxName As System.Windows.Forms.TextBox
    Friend WithEvents CheckBoxCalcAllLevels As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxCalcEachInCount As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxUseAll As System.Windows.Forms.CheckBox
    Friend WithEvents LabelChanceNone As System.Windows.Forms.Label
    Friend WithEvents NumericChanceNone As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelMaxCount As System.Windows.Forms.Label
    Friend WithEvents NumericMaxCount As System.Windows.Forms.NumericUpDown
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
