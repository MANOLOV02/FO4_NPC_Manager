' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class LeveledEntryDialog_Form
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
        LabelItem = New Label()
        LabelLevel = New Label()
        NumericLevel = New NumericUpDown()
        LabelCount = New Label()
        NumericCount = New NumericUpDown()
        LabelChanceNone = New Label()
        NumericChanceNone = New NumericUpDown()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        CType(NumericLevel, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumericCount, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).BeginInit()
        SuspendLayout()
        '
        ' LabelItem
        '
        LabelItem.AutoSize = True
        LabelItem.Location = New Point(12, 12)
        LabelItem.Name = "LabelItem"
        LabelItem.Text = "Add <item> into <lvl>"
        '
        ' LabelLevel
        '
        LabelLevel.AutoSize = True
        LabelLevel.Location = New Point(12, 44)
        LabelLevel.Name = "LabelLevel"
        LabelLevel.Text = "Level:"
        '
        ' NumericLevel
        '
        NumericLevel.Location = New Point(110, 42)
        NumericLevel.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumericLevel.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        NumericLevel.Name = "NumericLevel"
        NumericLevel.Size = New Size(80, 23)
        NumericLevel.Value = New Decimal(New Integer() {1, 0, 0, 0})
        '
        ' LabelCount
        '
        LabelCount.AutoSize = True
        LabelCount.Location = New Point(12, 76)
        LabelCount.Name = "LabelCount"
        LabelCount.Text = "Count:"
        '
        ' NumericCount
        '
        NumericCount.Location = New Point(110, 74)
        NumericCount.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumericCount.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        NumericCount.Name = "NumericCount"
        NumericCount.Size = New Size(80, 23)
        NumericCount.Value = New Decimal(New Integer() {1, 0, 0, 0})
        '
        ' LabelChanceNone
        '
        LabelChanceNone.AutoSize = True
        LabelChanceNone.Location = New Point(12, 108)
        LabelChanceNone.Name = "LabelChanceNone"
        LabelChanceNone.Text = "Chance None (%):"
        '
        ' NumericChanceNone
        '
        NumericChanceNone.Location = New Point(110, 106)
        NumericChanceNone.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        NumericChanceNone.Name = "NumericChanceNone"
        NumericChanceNone.Size = New Size(80, 23)
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(120, 144)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(75, 26)
        ButtonOk.Text = "OK"
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(201, 144)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 26)
        ButtonCancel.Text = "Cancel"
        '
        ' LeveledEntryDialog_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(288, 182)
        Font = New Font("Segoe UI", 9.0F)
        Controls.Add(LabelItem)
        Controls.Add(LabelLevel)
        Controls.Add(NumericLevel)
        Controls.Add(LabelCount)
        Controls.Add(NumericCount)
        Controls.Add(LabelChanceNone)
        Controls.Add(NumericChanceNone)
        Controls.Add(ButtonOk)
        Controls.Add(ButtonCancel)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "LeveledEntryDialog_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Leveled list entry"
        CType(NumericLevel, ComponentModel.ISupportInitialize).EndInit()
        CType(NumericCount, ComponentModel.ISupportInitialize).EndInit()
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).EndInit()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelItem As System.Windows.Forms.Label
    Friend WithEvents LabelLevel As System.Windows.Forms.Label
    Friend WithEvents NumericLevel As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelCount As System.Windows.Forms.Label
    Friend WithEvents NumericCount As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelChanceNone As System.Windows.Forms.Label
    Friend WithEvents NumericChanceNone As System.Windows.Forms.NumericUpDown
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
