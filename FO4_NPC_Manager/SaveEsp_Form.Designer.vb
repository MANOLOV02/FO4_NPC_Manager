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
        CheckBoxMarkAsMaster = New CheckBox()
        CheckBoxLightMaster = New CheckBox()
        CheckBoxGenerateChargen = New CheckBox()
        CheckBoxWriteBssliders = New CheckBox()
        CheckBoxEmitBodyGen = New CheckBox()
        LabelEncoding = New Label()
        ComboBoxEncoding = New ComboBox()
        LabelBa2Version = New Label()
        ComboBoxBa2Version = New ComboBox()
        LabelWarning = New Label()
        PanelProgress = New Panel()
        LabelProgressStage = New Label()
        LabelProgressDetail = New Label()
        ProgressBarMain = New ProgressBar()
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
        ' CheckBoxMarkAsMaster
        '
        CheckBoxMarkAsMaster.AutoSize = True
        CheckBoxMarkAsMaster.Checked = False
        CheckBoxMarkAsMaster.CheckState = CheckState.Unchecked
        CheckBoxMarkAsMaster.Location = New Drawing.Point(12, 280)
        CheckBoxMarkAsMaster.Name = "CheckBoxMarkAsMaster"
        CheckBoxMarkAsMaster.Size = New Drawing.Size(280, 19)
        CheckBoxMarkAsMaster.TabIndex = 7
        CheckBoxMarkAsMaster.Text = "Mark as master (ESM flag)"
        CheckBoxMarkAsMaster.UseVisualStyleBackColor = True
        '
        ' CheckBoxLightMaster
        '
        CheckBoxLightMaster.AutoSize = True
        CheckBoxLightMaster.Checked = True
        CheckBoxLightMaster.CheckState = CheckState.Checked
        CheckBoxLightMaster.Location = New Drawing.Point(12, 305)
        CheckBoxLightMaster.Name = "CheckBoxLightMaster"
        CheckBoxLightMaster.Size = New Drawing.Size(280, 19)
        CheckBoxLightMaster.TabIndex = 8
        CheckBoxLightMaster.Text = "Light (ESL flag)"
        CheckBoxLightMaster.UseVisualStyleBackColor = True
        '
        ' CheckBoxGenerateChargen
        '
        CheckBoxGenerateChargen.AutoSize = True
        CheckBoxGenerateChargen.Checked = True
        CheckBoxGenerateChargen.CheckState = CheckState.Checked
        CheckBoxGenerateChargen.Location = New Drawing.Point(12, 330)
        CheckBoxGenerateChargen.Name = "CheckBoxGenerateChargen"
        CheckBoxGenerateChargen.Size = New Drawing.Size(360, 19)
        CheckBoxGenerateChargen.TabIndex = 9
        CheckBoxGenerateChargen.Text = "Generate baked CharGen (NIF + textures) into BA2"
        CheckBoxGenerateChargen.UseVisualStyleBackColor = True
        '
        ' LabelBa2Version
        '
        LabelBa2Version.AutoSize = True
        LabelBa2Version.Location = New Drawing.Point(12, 441)
        LabelBa2Version.Name = "LabelBa2Version"
        LabelBa2Version.Size = New Drawing.Size(110, 15)
        LabelBa2Version.TabIndex = 18
        LabelBa2Version.Text = "BA2 version (FO4):"
        '
        ' ComboBoxBa2Version
        '
        ComboBoxBa2Version.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxBa2Version.Location = New Drawing.Point(140, 438)
        ComboBoxBa2Version.Name = "ComboBoxBa2Version"
        ComboBoxBa2Version.Size = New Drawing.Size(280, 23)
        ComboBoxBa2Version.TabIndex = 19
        '
        ' CheckBoxWriteBssliders
        '
        CheckBoxWriteBssliders.AutoSize = True
        CheckBoxWriteBssliders.Checked = True
        CheckBoxWriteBssliders.CheckState = CheckState.Checked
        CheckBoxWriteBssliders.Location = New Drawing.Point(12, 355)
        CheckBoxWriteBssliders.Name = "CheckBoxWriteBssliders"
        CheckBoxWriteBssliders.Size = New Drawing.Size(480, 19)
        CheckBoxWriteBssliders.TabIndex = 10
        CheckBoxWriteBssliders.Text = "Save BodyMorphs + Skin sidecar (.bssliders, preserves editor state)"
        CheckBoxWriteBssliders.UseVisualStyleBackColor = True
        '
        ' CheckBoxEmitBodyGen
        '
        CheckBoxEmitBodyGen.AutoSize = True
        CheckBoxEmitBodyGen.Checked = False
        CheckBoxEmitBodyGen.CheckState = CheckState.Unchecked
        CheckBoxEmitBodyGen.Location = New Drawing.Point(12, 380)
        CheckBoxEmitBodyGen.Name = "CheckBoxEmitBodyGen"
        CheckBoxEmitBodyGen.Size = New Drawing.Size(520, 19)
        CheckBoxEmitBodyGen.TabIndex = 11
        CheckBoxEmitBodyGen.Text = "Emit BodyGen .ini (engine applies sliders on first-load in NEW saves)"
        CheckBoxEmitBodyGen.UseVisualStyleBackColor = True
        '
        ' LabelEncoding
        '
        LabelEncoding.AutoSize = True
        LabelEncoding.Location = New Drawing.Point(12, 411)
        LabelEncoding.Name = "LabelEncoding"
        LabelEncoding.Size = New Drawing.Size(120, 15)
        LabelEncoding.TabIndex = 16
        LabelEncoding.Text = "Plugin text encoding:"
        '
        ' ComboBoxEncoding
        '
        ComboBoxEncoding.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxEncoding.Location = New Drawing.Point(140, 408)
        ComboBoxEncoding.Name = "ComboBoxEncoding"
        ComboBoxEncoding.Size = New Drawing.Size(280, 23)
        ComboBoxEncoding.TabIndex = 17
        '
        ' LabelEncodingHint
        '
        LabelEncodingHint.AutoSize = True
        LabelEncodingHint.ForeColor = SystemColors.GrayText
        LabelEncodingHint.Location = New Drawing.Point(426, 411)
        LabelEncodingHint.Name = "LabelEncodingHint"
        LabelEncodingHint.Size = New Drawing.Size(0, 15)
        LabelEncodingHint.TabIndex = 20
        LabelEncodingHint.Text = ""
        '
        ' LabelWarning
        '
        LabelWarning.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelWarning.ForeColor = Drawing.Color.DarkOrange
        LabelWarning.Location = New Drawing.Point(12, 472)
        LabelWarning.Name = "LabelWarning"
        LabelWarning.Size = New Drawing.Size(520, 36)
        LabelWarning.TabIndex = 12
        LabelWarning.Text = ""
        '
        ' PanelProgress  (hidden until OK click — shows phase/detail/bar during the save work)
        '
        PanelProgress.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        PanelProgress.BorderStyle = BorderStyle.FixedSingle
        PanelProgress.Location = New Drawing.Point(12, 514)
        PanelProgress.Name = "PanelProgress"
        PanelProgress.Size = New Drawing.Size(520, 78)
        PanelProgress.TabIndex = 15
        PanelProgress.Visible = False
        PanelProgress.Controls.Add(LabelProgressStage)
        PanelProgress.Controls.Add(LabelProgressDetail)
        PanelProgress.Controls.Add(ProgressBarMain)
        '
        ' LabelProgressStage
        '
        LabelProgressStage.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelProgressStage.Font = New Drawing.Font("Segoe UI", 9.0F, Drawing.FontStyle.Bold)
        LabelProgressStage.Location = New Drawing.Point(8, 6)
        LabelProgressStage.Name = "LabelProgressStage"
        LabelProgressStage.Size = New Drawing.Size(504, 20)
        LabelProgressStage.TabIndex = 0
        LabelProgressStage.Text = ""
        '
        ' LabelProgressDetail
        '
        LabelProgressDetail.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelProgressDetail.AutoEllipsis = True
        LabelProgressDetail.ForeColor = SystemColors.GrayText
        LabelProgressDetail.Location = New Drawing.Point(8, 28)
        LabelProgressDetail.Name = "LabelProgressDetail"
        LabelProgressDetail.Size = New Drawing.Size(504, 18)
        LabelProgressDetail.TabIndex = 1
        LabelProgressDetail.Text = ""
        '
        ' ProgressBarMain
        '
        ProgressBarMain.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarMain.Location = New Drawing.Point(8, 50)
        ProgressBarMain.Name = "ProgressBarMain"
        ProgressBarMain.Size = New Drawing.Size(504, 18)
        ProgressBarMain.Style = ProgressBarStyle.Marquee
        ProgressBarMain.TabIndex = 2
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Drawing.Point(376, 601)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Drawing.Size(75, 27)
        ButtonOk.TabIndex = 13
        ButtonOk.Text = "Save"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Drawing.Point(457, 601)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Drawing.Size(75, 27)
        ButtonCancel.TabIndex = 14
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' SaveEsp_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        AutoScaleDimensions = New Drawing.SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Drawing.Size(544, 640)
        Controls.Add(LabelHeader)
        Controls.Add(RadioButtonExisting)
        Controls.Add(ListBoxExisting)
        Controls.Add(RadioButtonNew)
        Controls.Add(LabelNewName)
        Controls.Add(TextBoxNewName)
        Controls.Add(LabelExtension)
        Controls.Add(CheckBoxMarkAsMaster)
        Controls.Add(CheckBoxLightMaster)
        Controls.Add(CheckBoxGenerateChargen)
        Controls.Add(CheckBoxWriteBssliders)
        Controls.Add(CheckBoxEmitBodyGen)
        Controls.Add(LabelEncoding)
        Controls.Add(ComboBoxEncoding)
        Controls.Add(LabelEncodingHint)
        Controls.Add(LabelBa2Version)
        Controls.Add(ComboBoxBa2Version)
        Controls.Add(PanelProgress)
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
    Friend WithEvents CheckBoxMarkAsMaster As CheckBox
    Friend WithEvents CheckBoxLightMaster As CheckBox
    Friend WithEvents CheckBoxGenerateChargen As CheckBox
    Friend WithEvents CheckBoxWriteBssliders As CheckBox
    Friend WithEvents CheckBoxEmitBodyGen As CheckBox
    Friend WithEvents LabelEncoding As Label
    Friend WithEvents ComboBoxEncoding As ComboBox
    Friend WithEvents LabelEncodingHint As Label
    Friend WithEvents LabelBa2Version As Label
    Friend WithEvents ComboBoxBa2Version As ComboBox
    Friend WithEvents PanelProgress As Panel
    Friend WithEvents LabelProgressStage As Label
    Friend WithEvents LabelProgressDetail As Label
    Friend WithEvents ProgressBarMain As ProgressBar
    Friend WithEvents LabelWarning As Label
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button

End Class
