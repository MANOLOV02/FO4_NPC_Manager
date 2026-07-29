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
        PanelScope = New Panel()
        RadioScopeAllChanged = New RadioButton()
        RadioScopeSelected = New RadioButton()
        GroupBoxTarget = New GroupBox()
        RadioButtonExisting = New RadioButton()
        ListBoxExisting = New ListBox()
        RadioButtonNew = New RadioButton()
        LabelNewName = New Label()
        TextBoxNewName = New TextBox()
        LabelExtension = New Label()
        CheckBoxLightMaster = New CheckBox()
        CheckBoxMarkAsMaster = New CheckBox()
        GroupBoxSave = New GroupBox()
        CheckBoxGenerateChargen = New CheckBox()
        CheckBoxRemoveChargenFlag = New CheckBox()
        CheckBoxEmitBodyGen = New CheckBox()
        CheckBoxEmitApplyScript = New CheckBox()
        CheckBoxOverrideScriptVersion = New CheckBox()
        NumericUpDownScriptVersion = New NumericUpDown()
        CheckBoxSaveNewOutfits = New CheckBox()
        GroupBoxEncoding = New GroupBox()
        LabelEncoding = New Label()
        ComboBoxEncoding = New ComboBox()
        LabelEncodingHint = New Label()
        LabelBa2Version = New Label()
        ComboBoxBa2Version = New ComboBox()
        GroupBoxLvlList = New GroupBox()
        CheckBoxAddToLvlList = New CheckBox()
        RadioLvlNew = New RadioButton()
        TextBoxLvlNewName = New TextBox()
        LabelLvlNewHint = New Label()
        RadioLvlExisting = New RadioButton()
        ComboBoxLvlExisting = New ComboBox()
        CheckBoxLvlNoDup = New CheckBox()
        LabelWarning = New Label()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        PanelScope.SuspendLayout()
        GroupBoxTarget.SuspendLayout()
        GroupBoxSave.SuspendLayout()
        CType(NumericUpDownScriptVersion, ComponentModel.ISupportInitialize).BeginInit()
        GroupBoxEncoding.SuspendLayout()
        GroupBoxLvlList.SuspendLayout()
        SuspendLayout()
        ' 
        ' LabelHeader
        ' 
        LabelHeader.AutoSize = True
        LabelHeader.Location = New Point(12, 14)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(42, 15)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Scope:"
        ' 
        ' PanelScope
        ' 
        PanelScope.Controls.Add(RadioScopeAllChanged)
        PanelScope.Controls.Add(RadioScopeSelected)
        PanelScope.Location = New Point(64, 8)
        PanelScope.Name = "PanelScope"
        PanelScope.Size = New Size(484, 26)
        PanelScope.TabIndex = 1
        ' 
        ' RadioScopeAllChanged
        ' 
        RadioScopeAllChanged.AutoSize = True
        RadioScopeAllChanged.Checked = True
        RadioScopeAllChanged.Location = New Point(6, 4)
        RadioScopeAllChanged.Name = "RadioScopeAllChanged"
        RadioScopeAllChanged.Size = New Size(88, 19)
        RadioScopeAllChanged.TabIndex = 0
        RadioScopeAllChanged.TabStop = True
        RadioScopeAllChanged.Text = "All changed"
        RadioScopeAllChanged.UseVisualStyleBackColor = True
        ' 
        ' RadioScopeSelected
        ' 
        RadioScopeSelected.AutoSize = True
        RadioScopeSelected.Location = New Point(160, 4)
        RadioScopeSelected.Name = "RadioScopeSelected"
        RadioScopeSelected.Size = New Size(95, 19)
        RadioScopeSelected.TabIndex = 1
        RadioScopeSelected.Text = "Selected only"
        RadioScopeSelected.UseVisualStyleBackColor = True
        ' 
        ' GroupBoxTarget
        ' 
        GroupBoxTarget.Controls.Add(RadioButtonExisting)
        GroupBoxTarget.Controls.Add(ListBoxExisting)
        GroupBoxTarget.Controls.Add(RadioButtonNew)
        GroupBoxTarget.Controls.Add(LabelNewName)
        GroupBoxTarget.Controls.Add(TextBoxNewName)
        GroupBoxTarget.Controls.Add(LabelExtension)
        GroupBoxTarget.Controls.Add(CheckBoxLightMaster)
        GroupBoxTarget.Controls.Add(CheckBoxMarkAsMaster)
        GroupBoxTarget.Location = New Point(12, 42)
        GroupBoxTarget.Name = "GroupBoxTarget"
        GroupBoxTarget.Size = New Size(536, 240)
        GroupBoxTarget.TabIndex = 2
        GroupBoxTarget.TabStop = False
        GroupBoxTarget.Text = "Target plugin"
        ' 
        ' RadioButtonExisting
        ' 
        RadioButtonExisting.AutoSize = True
        RadioButtonExisting.Location = New Point(12, 22)
        RadioButtonExisting.Name = "RadioButtonExisting"
        RadioButtonExisting.Size = New Size(143, 19)
        RadioButtonExisting.TabIndex = 0
        RadioButtonExisting.Text = "Update existing plugin"
        RadioButtonExisting.UseVisualStyleBackColor = True
        ' 
        ' ListBoxExisting
        ' 
        ListBoxExisting.IntegralHeight = False
        ListBoxExisting.ItemHeight = 15
        ListBoxExisting.Location = New Point(30, 46)
        ListBoxExisting.Name = "ListBoxExisting"
        ListBoxExisting.Size = New Size(494, 96)
        ListBoxExisting.TabIndex = 1
        ' 
        ' RadioButtonNew
        ' 
        RadioButtonNew.AutoSize = True
        RadioButtonNew.Checked = True
        RadioButtonNew.Location = New Point(12, 150)
        RadioButtonNew.Name = "RadioButtonNew"
        RadioButtonNew.Size = New Size(121, 19)
        RadioButtonNew.TabIndex = 2
        RadioButtonNew.TabStop = True
        RadioButtonNew.Text = "Create new plugin"
        RadioButtonNew.UseVisualStyleBackColor = True
        ' 
        ' LabelNewName
        ' 
        LabelNewName.AutoSize = True
        LabelNewName.Location = New Point(30, 182)
        LabelNewName.Name = "LabelNewName"
        LabelNewName.Size = New Size(42, 15)
        LabelNewName.TabIndex = 3
        LabelNewName.Text = "Name:"
        ' 
        ' TextBoxNewName
        ' 
        TextBoxNewName.Location = New Point(140, 179)
        TextBoxNewName.Name = "TextBoxNewName"
        TextBoxNewName.Size = New Size(300, 23)
        TextBoxNewName.TabIndex = 4
        TextBoxNewName.Text = "NPC_Manager"
        ' 
        ' LabelExtension
        ' 
        LabelExtension.AutoSize = True
        LabelExtension.ForeColor = SystemColors.GrayText
        LabelExtension.Location = New Point(446, 182)
        LabelExtension.Name = "LabelExtension"
        LabelExtension.Size = New Size(28, 15)
        LabelExtension.TabIndex = 5
        LabelExtension.Text = ".esp"
        ' 
        ' CheckBoxLightMaster
        ' 
        CheckBoxLightMaster.AutoSize = True
        CheckBoxLightMaster.Checked = True
        CheckBoxLightMaster.CheckState = CheckState.Checked
        CheckBoxLightMaster.Location = New Point(30, 210)
        CheckBoxLightMaster.Name = "CheckBoxLightMaster"
        CheckBoxLightMaster.Size = New Size(105, 19)
        CheckBoxLightMaster.TabIndex = 6
        CheckBoxLightMaster.Text = "Light (ESL flag)"
        CheckBoxLightMaster.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxMarkAsMaster
        ' 
        CheckBoxMarkAsMaster.AutoSize = True
        CheckBoxMarkAsMaster.Location = New Point(190, 210)
        CheckBoxMarkAsMaster.Name = "CheckBoxMarkAsMaster"
        CheckBoxMarkAsMaster.Size = New Size(163, 19)
        CheckBoxMarkAsMaster.TabIndex = 7
        CheckBoxMarkAsMaster.Text = "Mark as master (ESM flag)"
        CheckBoxMarkAsMaster.UseVisualStyleBackColor = True
        ' 
        ' GroupBoxSave
        ' 
        GroupBoxSave.Controls.Add(CheckBoxGenerateChargen)
        GroupBoxSave.Controls.Add(CheckBoxRemoveChargenFlag)
        GroupBoxSave.Controls.Add(CheckBoxEmitBodyGen)
        GroupBoxSave.Controls.Add(CheckBoxEmitApplyScript)
        GroupBoxSave.Controls.Add(CheckBoxOverrideScriptVersion)
        GroupBoxSave.Controls.Add(NumericUpDownScriptVersion)
        GroupBoxSave.Controls.Add(CheckBoxSaveNewOutfits)
        GroupBoxSave.Location = New Point(12, 290)
        GroupBoxSave.Name = "GroupBoxSave"
        GroupBoxSave.Size = New Size(536, 182)
        GroupBoxSave.TabIndex = 3
        GroupBoxSave.TabStop = False
        GroupBoxSave.Text = "What to save"
        ' 
        ' CheckBoxGenerateChargen
        ' 
        CheckBoxGenerateChargen.AutoSize = True
        CheckBoxGenerateChargen.Checked = True
        CheckBoxGenerateChargen.CheckState = CheckState.Checked
        CheckBoxGenerateChargen.Location = New Point(12, 24)
        CheckBoxGenerateChargen.Name = "CheckBoxGenerateChargen"
        CheckBoxGenerateChargen.Size = New Size(268, 19)
        CheckBoxGenerateChargen.TabIndex = 0
        CheckBoxGenerateChargen.Text = "Bake CharGen (NIF + textures) → BA2 / Looses"
        CheckBoxGenerateChargen.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxRemoveChargenFlag
        ' 
        CheckBoxRemoveChargenFlag.AutoSize = True
        CheckBoxRemoveChargenFlag.Checked = True
        CheckBoxRemoveChargenFlag.CheckState = CheckState.Checked
        CheckBoxRemoveChargenFlag.Location = New Point(32, 48)
        CheckBoxRemoveChargenFlag.Name = "CheckBoxRemoveChargenFlag"
        CheckBoxRemoveChargenFlag.Size = New Size(322, 19)
        CheckBoxRemoveChargenFlag.TabIndex = 1
        CheckBoxRemoveChargenFlag.Text = "Remove 'Is CharGen Face Preset' flag from saved NPC(s)"
        CheckBoxRemoveChargenFlag.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxEmitBodyGen
        ' 
        CheckBoxEmitBodyGen.AutoSize = True
        CheckBoxEmitBodyGen.Checked = True
        CheckBoxEmitBodyGen.CheckState = CheckState.Checked
        CheckBoxEmitBodyGen.Location = New Point(12, 76)
        CheckBoxEmitBodyGen.Name = "CheckBoxEmitBodyGen"
        CheckBoxEmitBodyGen.Size = New Size(271, 19)
        CheckBoxEmitBodyGen.TabIndex = 2
        CheckBoxEmitBodyGen.Text = "Emit BodyGen .ini (body sliders on first spawn)"
        CheckBoxEmitBodyGen.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxEmitApplyScript
        ' 
        CheckBoxEmitApplyScript.AutoSize = True
        CheckBoxEmitApplyScript.Checked = True
        CheckBoxEmitApplyScript.CheckState = CheckState.Checked
        CheckBoxEmitApplyScript.Location = New Point(12, 102)
        CheckBoxEmitApplyScript.Name = "CheckBoxEmitApplyScript"
        CheckBoxEmitApplyScript.Size = New Size(356, 19)
        CheckBoxEmitApplyScript.TabIndex = 3
        CheckBoxEmitApplyScript.Text = "Emit apply-script (overlays / skin / node scales / body morphs)"
        CheckBoxEmitApplyScript.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxOverrideScriptVersion
        ' 
        CheckBoxOverrideScriptVersion.AutoSize = True
        CheckBoxOverrideScriptVersion.Location = New Point(32, 126)
        CheckBoxOverrideScriptVersion.Name = "CheckBoxOverrideScriptVersion"
        CheckBoxOverrideScriptVersion.Size = New Size(112, 19)
        CheckBoxOverrideScriptVersion.TabIndex = 4
        CheckBoxOverrideScriptVersion.Text = "Override version"
        CheckBoxOverrideScriptVersion.UseVisualStyleBackColor = True
        ' 
        ' NumericUpDownScriptVersion
        ' 
        NumericUpDownScriptVersion.Enabled = False
        NumericUpDownScriptVersion.Location = New Point(150, 123)
        NumericUpDownScriptVersion.Maximum = New Decimal(New Integer() {999999, 0, 0, 0})
        NumericUpDownScriptVersion.Name = "NumericUpDownScriptVersion"
        NumericUpDownScriptVersion.Size = New Size(78, 23)
        NumericUpDownScriptVersion.TabIndex = 5
        NumericUpDownScriptVersion.TextAlign = HorizontalAlignment.Right
        NumericUpDownScriptVersion.Value = New Decimal(New Integer() {1, 0, 0, 0})
        ' 
        ' CheckBoxSaveNewOutfits
        ' 
        CheckBoxSaveNewOutfits.AutoSize = True
        CheckBoxSaveNewOutfits.Location = New Point(12, 152)
        CheckBoxSaveNewOutfits.Name = "CheckBoxSaveNewOutfits"
        CheckBoxSaveNewOutfits.Size = New Size(112, 19)
        CheckBoxSaveNewOutfits.TabIndex = 6
        CheckBoxSaveNewOutfits.Text = "Save new outfits"
        CheckBoxSaveNewOutfits.UseVisualStyleBackColor = True
        ' 
        ' GroupBoxEncoding
        ' 
        GroupBoxEncoding.Controls.Add(LabelEncoding)
        GroupBoxEncoding.Controls.Add(ComboBoxEncoding)
        GroupBoxEncoding.Controls.Add(LabelEncodingHint)
        GroupBoxEncoding.Controls.Add(LabelBa2Version)
        GroupBoxEncoding.Controls.Add(ComboBoxBa2Version)
        GroupBoxEncoding.Location = New Point(12, 480)
        GroupBoxEncoding.Name = "GroupBoxEncoding"
        GroupBoxEncoding.Size = New Size(536, 88)
        GroupBoxEncoding.TabIndex = 4
        GroupBoxEncoding.TabStop = False
        GroupBoxEncoding.Text = "Encoding && archive"
        ' 
        ' LabelEncoding
        ' 
        LabelEncoding.AutoSize = True
        LabelEncoding.Location = New Point(12, 26)
        LabelEncoding.Name = "LabelEncoding"
        LabelEncoding.Size = New Size(119, 15)
        LabelEncoding.TabIndex = 0
        LabelEncoding.Text = "Plugin text encoding:"
        ' 
        ' ComboBoxEncoding
        ' 
        ComboBoxEncoding.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxEncoding.Location = New Point(140, 23)
        ComboBoxEncoding.Name = "ComboBoxEncoding"
        ComboBoxEncoding.Size = New Size(300, 23)
        ComboBoxEncoding.TabIndex = 1
        ' 
        ' LabelEncodingHint
        ' 
        LabelEncodingHint.AutoSize = True
        LabelEncodingHint.ForeColor = SystemColors.GrayText
        LabelEncodingHint.Location = New Point(446, 26)
        LabelEncodingHint.Name = "LabelEncodingHint"
        LabelEncodingHint.Size = New Size(0, 15)
        LabelEncodingHint.TabIndex = 2
        ' 
        ' LabelBa2Version
        ' 
        LabelBa2Version.AutoSize = True
        LabelBa2Version.Location = New Point(12, 55)
        LabelBa2Version.Name = "LabelBa2Version"
        LabelBa2Version.Size = New Size(104, 15)
        LabelBa2Version.TabIndex = 3
        LabelBa2Version.Text = "BA2 version (FO4):"
        ' 
        ' ComboBoxBa2Version
        ' 
        ComboBoxBa2Version.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxBa2Version.Location = New Point(140, 52)
        ComboBoxBa2Version.Name = "ComboBoxBa2Version"
        ComboBoxBa2Version.Size = New Size(300, 23)
        ComboBoxBa2Version.TabIndex = 4
        ' 
        ' GroupBoxLvlList
        ' 
        GroupBoxLvlList.Controls.Add(CheckBoxAddToLvlList)
        GroupBoxLvlList.Controls.Add(RadioLvlNew)
        GroupBoxLvlList.Controls.Add(TextBoxLvlNewName)
        GroupBoxLvlList.Controls.Add(LabelLvlNewHint)
        GroupBoxLvlList.Controls.Add(RadioLvlExisting)
        GroupBoxLvlList.Controls.Add(ComboBoxLvlExisting)
        GroupBoxLvlList.Controls.Add(CheckBoxLvlNoDup)
        GroupBoxLvlList.Location = New Point(12, 576)
        GroupBoxLvlList.Name = "GroupBoxLvlList"
        GroupBoxLvlList.Size = New Size(536, 124)
        GroupBoxLvlList.TabIndex = 5
        GroupBoxLvlList.TabStop = False
        GroupBoxLvlList.Text = "Leveled NPC list (LVLN)"
        ' 
        ' CheckBoxAddToLvlList
        ' 
        CheckBoxAddToLvlList.AutoSize = True
        CheckBoxAddToLvlList.Location = New Point(10, 20)
        CheckBoxAddToLvlList.Name = "CheckBoxAddToLvlList"
        CheckBoxAddToLvlList.Size = New Size(232, 19)
        CheckBoxAddToLvlList.TabIndex = 0
        CheckBoxAddToLvlList.Text = "Add saved NPC(s) to a Leveled NPC list"
        CheckBoxAddToLvlList.UseVisualStyleBackColor = True
        ' 
        ' RadioLvlNew
        ' 
        RadioLvlNew.AutoSize = True
        RadioLvlNew.Checked = True
        RadioLvlNew.Location = New Point(28, 44)
        RadioLvlNew.Name = "RadioLvlNew"
        RadioLvlNew.Size = New Size(52, 19)
        RadioLvlNew.TabIndex = 1
        RadioLvlNew.TabStop = True
        RadioLvlNew.Text = "New:"
        RadioLvlNew.UseVisualStyleBackColor = True
        ' 
        ' TextBoxLvlNewName
        ' 
        TextBoxLvlNewName.Location = New Point(160, 42)
        TextBoxLvlNewName.Name = "TextBoxLvlNewName"
        TextBoxLvlNewName.Size = New Size(165, 23)
        TextBoxLvlNewName.TabIndex = 2
        ' 
        ' LabelLvlNewHint
        ' 
        LabelLvlNewHint.AutoSize = True
        LabelLvlNewHint.ForeColor = SystemColors.GrayText
        LabelLvlNewHint.Location = New Point(331, 45)
        LabelLvlNewHint.Name = "LabelLvlNewHint"
        LabelLvlNewHint.Size = New Size(173, 15)
        LabelLvlNewHint.TabIndex = 3
        LabelLvlNewHint.Text = "→ npcm_<esp>_LVLN_<name>"
        ' 
        ' RadioLvlExisting
        ' 
        RadioLvlExisting.AutoSize = True
        RadioLvlExisting.Location = New Point(28, 69)
        RadioLvlExisting.Name = "RadioLvlExisting"
        RadioLvlExisting.Size = New Size(124, 19)
        RadioLvlExisting.TabIndex = 4
        RadioLvlExisting.Text = "Existing in this esp:"
        RadioLvlExisting.UseVisualStyleBackColor = True
        ' 
        ' ComboBoxLvlExisting
        ' 
        ComboBoxLvlExisting.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxLvlExisting.Location = New Point(160, 66)
        ComboBoxLvlExisting.Name = "ComboBoxLvlExisting"
        ComboBoxLvlExisting.Size = New Size(364, 23)
        ComboBoxLvlExisting.TabIndex = 5
        ' 
        ' CheckBoxLvlNoDup
        ' 
        CheckBoxLvlNoDup.AutoSize = True
        CheckBoxLvlNoDup.Location = New Point(10, 98)
        CheckBoxLvlNoDup.Name = "CheckBoxLvlNoDup"
        CheckBoxLvlNoDup.Size = New Size(274, 19)
        CheckBoxLvlNoDup.TabIndex = 6
        CheckBoxLvlNoDup.Text = "Skip NPCs already in a leveled list of this plugin"
        CheckBoxLvlNoDup.UseVisualStyleBackColor = True
        ' 
        ' LabelWarning
        ' 
        LabelWarning.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelWarning.ForeColor = Color.DarkOrange
        LabelWarning.Location = New Point(12, 706)
        LabelWarning.Name = "LabelWarning"
        LabelWarning.Size = New Size(536, 34)
        LabelWarning.TabIndex = 6
        ' 
        ' ButtonOk
        ' 
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Point(392, 746)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(75, 27)
        ButtonOk.TabIndex = 7
        ButtonOk.Text = "Save"
        ButtonOk.UseVisualStyleBackColor = True
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(473, 746)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 27)
        ButtonCancel.TabIndex = 8
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        ' 
        ' SaveEsp_Form
        ' 
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(560, 785)
        Controls.Add(LabelHeader)
        Controls.Add(PanelScope)
        Controls.Add(GroupBoxTarget)
        Controls.Add(GroupBoxSave)
        Controls.Add(GroupBoxEncoding)
        Controls.Add(GroupBoxLvlList)
        Controls.Add(LabelWarning)
        Controls.Add(ButtonOk)
        Controls.Add(ButtonCancel)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "SaveEsp_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Save NPC override (ESP/ESM)"
        PanelScope.ResumeLayout(False)
        PanelScope.PerformLayout()
        GroupBoxTarget.ResumeLayout(False)
        GroupBoxTarget.PerformLayout()
        GroupBoxSave.ResumeLayout(False)
        GroupBoxSave.PerformLayout()
        CType(NumericUpDownScriptVersion, ComponentModel.ISupportInitialize).EndInit()
        GroupBoxEncoding.ResumeLayout(False)
        GroupBoxEncoding.PerformLayout()
        GroupBoxLvlList.ResumeLayout(False)
        GroupBoxLvlList.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelHeader As Label
    Friend WithEvents PanelScope As Panel
    Friend WithEvents RadioScopeAllChanged As RadioButton
    Friend WithEvents RadioScopeSelected As RadioButton
    Friend WithEvents GroupBoxTarget As GroupBox
    Friend WithEvents RadioButtonExisting As RadioButton
    Friend WithEvents ListBoxExisting As ListBox
    Friend WithEvents RadioButtonNew As RadioButton
    Friend WithEvents LabelNewName As Label
    Friend WithEvents TextBoxNewName As TextBox
    Friend WithEvents LabelExtension As Label
    Friend WithEvents CheckBoxMarkAsMaster As CheckBox
    Friend WithEvents CheckBoxLightMaster As CheckBox
    Friend WithEvents GroupBoxSave As GroupBox
    Friend WithEvents CheckBoxGenerateChargen As CheckBox
    Friend WithEvents CheckBoxRemoveChargenFlag As CheckBox
    Friend WithEvents CheckBoxEmitBodyGen As CheckBox
    Friend WithEvents CheckBoxEmitApplyScript As CheckBox
    Friend WithEvents CheckBoxOverrideScriptVersion As CheckBox
    Friend WithEvents NumericUpDownScriptVersion As NumericUpDown
    Friend WithEvents CheckBoxSaveNewOutfits As CheckBox
    Friend WithEvents GroupBoxEncoding As GroupBox
    Friend WithEvents LabelEncoding As Label
    Friend WithEvents ComboBoxEncoding As ComboBox
    Friend WithEvents LabelEncodingHint As Label
    Friend WithEvents LabelBa2Version As Label
    Friend WithEvents ComboBoxBa2Version As ComboBox
    Friend WithEvents GroupBoxLvlList As GroupBox
    Friend WithEvents CheckBoxAddToLvlList As CheckBox
    Friend WithEvents RadioLvlNew As RadioButton
    Friend WithEvents TextBoxLvlNewName As TextBox
    Friend WithEvents LabelLvlNewHint As Label
    Friend WithEvents RadioLvlExisting As RadioButton
    Friend WithEvents ComboBoxLvlExisting As ComboBox
    Friend WithEvents CheckBoxLvlNoDup As CheckBox
    Friend WithEvents LabelWarning As Label
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button

End Class
