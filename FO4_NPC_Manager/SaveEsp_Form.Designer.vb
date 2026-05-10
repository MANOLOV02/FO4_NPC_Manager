<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class SaveEsp_Form
    Inherits System.Windows.Forms.Form

    Private components As System.ComponentModel.IContainer

    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Sub InitializeComponent()
        LabelHeader = New Label()
        RadioButtonExisting = New RadioButton()
        ListBoxExisting = New ListBox()
        RadioButtonNew = New RadioButton()
        LabelNewName = New Label()
        TextBoxNewName = New TextBox()
        LabelExtension = New Label()
        CheckBoxLightMaster = New CheckBox()
        CheckBoxGenerateChargen = New CheckBox()
        LabelWarning = New Label()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        SuspendLayout()
        '
        ' LabelHeader
        '
        LabelHeader.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelHeader.Location = New Drawing.Point(12, 9)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Drawing.Size(520, 32)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Save NPC override to plugin"
        '
        ' RadioButtonExisting
        '
        RadioButtonExisting.AutoSize = True
        RadioButtonExisting.Location = New Drawing.Point(12, 50)
        RadioButtonExisting.Name = "RadioButtonExisting"
        RadioButtonExisting.Size = New Drawing.Size(180, 19)
        RadioButtonExisting.TabIndex = 1
        RadioButtonExisting.Text = "Update existing plugin"
        RadioButtonExisting.UseVisualStyleBackColor = True
        '
        ' ListBoxExisting
        '
        ListBoxExisting.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        ListBoxExisting.IntegralHeight = False
        ListBoxExisting.Location = New Drawing.Point(30, 75)
        ListBoxExisting.Name = "ListBoxExisting"
        ListBoxExisting.Size = New Drawing.Size(502, 130)
        ListBoxExisting.TabIndex = 2
        '
        ' RadioButtonNew
        '
        RadioButtonNew.AutoSize = True
        RadioButtonNew.Checked = True
        RadioButtonNew.Location = New Drawing.Point(12, 215)
        RadioButtonNew.Name = "RadioButtonNew"
        RadioButtonNew.Size = New Drawing.Size(150, 19)
        RadioButtonNew.TabIndex = 3
        RadioButtonNew.Text = "Create new plugin"
        RadioButtonNew.UseVisualStyleBackColor = True
        '
        ' LabelNewName
        '
        LabelNewName.AutoSize = True
        LabelNewName.Location = New Drawing.Point(30, 244)
        LabelNewName.Name = "LabelNewName"
        LabelNewName.Size = New Drawing.Size(40, 15)
        LabelNewName.TabIndex = 4
        LabelNewName.Text = "Name:"
        '
        ' TextBoxNewName
        '
        TextBoxNewName.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxNewName.Location = New Drawing.Point(76, 241)
        TextBoxNewName.Name = "TextBoxNewName"
        TextBoxNewName.Size = New Drawing.Size(380, 23)
        TextBoxNewName.TabIndex = 5
        TextBoxNewName.Text = "NPC_Manager"
        '
        ' LabelExtension
        '
        LabelExtension.AutoSize = True
        LabelExtension.ForeColor = SystemColors.GrayText
        LabelExtension.Location = New Drawing.Point(462, 244)
        LabelExtension.Name = "LabelExtension"
        LabelExtension.Size = New Drawing.Size(28, 15)
        LabelExtension.TabIndex = 6
        LabelExtension.Text = ".esp"
        '
        ' CheckBoxLightMaster
        '
        CheckBoxLightMaster.AutoSize = True
        CheckBoxLightMaster.Checked = True
        CheckBoxLightMaster.CheckState = CheckState.Checked
        CheckBoxLightMaster.Location = New Drawing.Point(12, 280)
        CheckBoxLightMaster.Name = "CheckBoxLightMaster"
        CheckBoxLightMaster.Size = New Drawing.Size(280, 19)
        CheckBoxLightMaster.TabIndex = 7
        CheckBoxLightMaster.Text = "Light master (ESM+ESL — recommended)"
        CheckBoxLightMaster.UseVisualStyleBackColor = True
        '
        ' CheckBoxGenerateChargen
        '
        CheckBoxGenerateChargen.AutoSize = True
        CheckBoxGenerateChargen.Checked = True
        CheckBoxGenerateChargen.CheckState = CheckState.Checked
        CheckBoxGenerateChargen.Location = New Drawing.Point(12, 305)
        CheckBoxGenerateChargen.Name = "CheckBoxGenerateChargen"
        CheckBoxGenerateChargen.Size = New Drawing.Size(360, 19)
        CheckBoxGenerateChargen.TabIndex = 8
        CheckBoxGenerateChargen.Text = "Generate baked CharGen (NIF + textures) into BA2"
        CheckBoxGenerateChargen.UseVisualStyleBackColor = True
        '
        ' LabelWarning
        '
        LabelWarning.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelWarning.ForeColor = Drawing.Color.DarkOrange
        LabelWarning.Location = New Drawing.Point(12, 335)
        LabelWarning.Name = "LabelWarning"
        LabelWarning.Size = New Drawing.Size(520, 36)
        LabelWarning.TabIndex = 9
        LabelWarning.Text = ""
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Drawing.Point(376, 385)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Drawing.Size(75, 27)
        ButtonOk.TabIndex = 10
        ButtonOk.Text = "Save"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Drawing.Point(457, 385)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Drawing.Size(75, 27)
        ButtonCancel.TabIndex = 11
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' SaveEsp_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        AutoScaleDimensions = New Drawing.SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Drawing.Size(544, 424)
        Controls.Add(LabelHeader)
        Controls.Add(RadioButtonExisting)
        Controls.Add(ListBoxExisting)
        Controls.Add(RadioButtonNew)
        Controls.Add(LabelNewName)
        Controls.Add(TextBoxNewName)
        Controls.Add(LabelExtension)
        Controls.Add(CheckBoxLightMaster)
        Controls.Add(CheckBoxGenerateChargen)
        Controls.Add(LabelWarning)
        Controls.Add(ButtonOk)
        Controls.Add(ButtonCancel)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "SaveEsp_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Save NPC override (ESP/ESM)"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelHeader As Label
    Friend WithEvents RadioButtonExisting As RadioButton
    Friend WithEvents ListBoxExisting As ListBox
    Friend WithEvents RadioButtonNew As RadioButton
    Friend WithEvents LabelNewName As Label
    Friend WithEvents TextBoxNewName As TextBox
    Friend WithEvents LabelExtension As Label
    Friend WithEvents CheckBoxLightMaster As CheckBox
    Friend WithEvents CheckBoxGenerateChargen As CheckBox
    Friend WithEvents LabelWarning As Label
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button

End Class
