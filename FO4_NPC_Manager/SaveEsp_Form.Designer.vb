<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class SaveEsp_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

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
        components = New ComponentModel.Container()
        ToolTipWarning = New ToolTip(components)
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
        CheckBoxActivateInLoadOrder = New CheckBox()
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
        LabelHeader.Location = New Point(12, 18)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(42, 15)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Scope:"
        ' 
        ' PanelScope
        ' 
        PanelScope.Controls.Add(RadioScopeAllChanged)
        PanelScope.Controls.Add(RadioScopeSelected)
        ' Ancho MEDIDO con la fuente del diálogo y el peor rótulo posible ("All changed (9999)" =
        ' 124 px, "Selected (9999)" = 106 px): los contadores llegan a 4 dígitos sin recortarse.
        PanelScope.Location = New Point(64, 12)
        PanelScope.Name = "PanelScope"
        PanelScope.Size = New Size(266, 28)
        PanelScope.TabIndex = 1
        ' 
        ' RadioScopeAllChanged
        ' 
        RadioScopeAllChanged.AutoSize = True
        RadioScopeAllChanged.Checked = True
        RadioScopeAllChanged.Location = New Point(6, 3)
        RadioScopeAllChanged.Name = "RadioScopeAllChanged"
        RadioScopeAllChanged.Size = New Size(124, 22)
        RadioScopeAllChanged.TabIndex = 0
        RadioScopeAllChanged.TabStop = True
        RadioScopeAllChanged.Text = "All changed"
        RadioScopeAllChanged.UseVisualStyleBackColor = True
        ' 
        ' RadioScopeSelected
        ' 
        RadioScopeSelected.AutoSize = True
        RadioScopeSelected.Location = New Point(154, 3)
        RadioScopeSelected.Name = "RadioScopeSelected"
        RadioScopeSelected.Size = New Size(106, 22)
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
        GroupBoxTarget.Location = New Point(12, 52)
        GroupBoxTarget.Name = "GroupBoxTarget"
        GroupBoxTarget.Size = New Size(480, 248)
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
        ListBoxExisting.Location = New Point(30, 47)
        ListBoxExisting.Name = "ListBoxExisting"
        ListBoxExisting.Size = New Size(438, 100)
        ListBoxExisting.TabIndex = 1
        ' 
        ' RadioButtonNew
        ' 
        RadioButtonNew.AutoSize = True
        RadioButtonNew.Checked = True
        RadioButtonNew.Location = New Point(12, 156)
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
        LabelNewName.Location = New Point(30, 186)
        LabelNewName.Name = "LabelNewName"
        LabelNewName.Size = New Size(42, 15)
        LabelNewName.TabIndex = 3
        LabelNewName.Text = "Name:"
        ' 
        ' TextBoxNewName
        ' 
        TextBoxNewName.Location = New Point(140, 183)
        TextBoxNewName.Name = "TextBoxNewName"
        TextBoxNewName.Size = New Size(240, 23)
        TextBoxNewName.TabIndex = 4
        TextBoxNewName.Text = "NPC_Manager"
        ' 
        ' LabelExtension
        ' 
        LabelExtension.AutoSize = True
        LabelExtension.ForeColor = SystemColors.GrayText
        LabelExtension.Location = New Point(386, 186)
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
        CheckBoxLightMaster.Location = New Point(30, 216)
        CheckBoxLightMaster.Name = "CheckBoxLightMaster"
        CheckBoxLightMaster.Size = New Size(105, 19)
        CheckBoxLightMaster.TabIndex = 6
        CheckBoxLightMaster.Text = "Light (ESL flag)"
        CheckBoxLightMaster.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxMarkAsMaster
        ' 
        CheckBoxMarkAsMaster.AutoSize = True
        CheckBoxMarkAsMaster.Location = New Point(190, 216)
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
        GroupBoxSave.Location = New Point(508, 52)
        GroupBoxSave.Name = "GroupBoxSave"
        GroupBoxSave.Size = New Size(480, 182)
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
        ' ⛔ DECÍA "node scales" y desde el TRS completo eso es falso: se escribe escala + posición + rotación.
        CheckBoxEmitApplyScript.Text = "Attach the helper script (tattoos, body paint, bone edits, body sliders)"
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
        GroupBoxEncoding.Location = New Point(12, 312)
        GroupBoxEncoding.Name = "GroupBoxEncoding"
        GroupBoxEncoding.Size = New Size(480, 92)
        GroupBoxEncoding.TabIndex = 4
        GroupBoxEncoding.TabStop = False
        GroupBoxEncoding.Text = "Encoding && archive"
        ' 
        ' LabelEncoding
        ' 
        LabelEncoding.AutoSize = True
        LabelEncoding.Location = New Point(12, 30)
        LabelEncoding.Name = "LabelEncoding"
        LabelEncoding.Size = New Size(119, 15)
        LabelEncoding.TabIndex = 0
        LabelEncoding.Text = "Plugin text encoding:"
        ' 
        ' ComboBoxEncoding
        ' 
        ComboBoxEncoding.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxEncoding.Location = New Point(140, 27)
        ComboBoxEncoding.Name = "ComboBoxEncoding"
        ComboBoxEncoding.Size = New Size(240, 23)
        ComboBoxEncoding.TabIndex = 1
        ' 
        ' LabelEncodingHint
        ' 
        LabelEncodingHint.AutoSize = True
        LabelEncodingHint.ForeColor = SystemColors.GrayText
        LabelEncodingHint.Location = New Point(386, 30)
        LabelEncodingHint.Name = "LabelEncodingHint"
        LabelEncodingHint.Size = New Size(0, 15)
        LabelEncodingHint.TabIndex = 2
        ' 
        ' LabelBa2Version
        ' 
        LabelBa2Version.AutoSize = True
        LabelBa2Version.Location = New Point(12, 60)
        LabelBa2Version.Name = "LabelBa2Version"
        LabelBa2Version.Size = New Size(104, 15)
        LabelBa2Version.TabIndex = 3
        LabelBa2Version.Text = "BA2 version (FO4):"
        ' 
        ' ComboBoxBa2Version
        ' 
        ComboBoxBa2Version.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxBa2Version.Location = New Point(140, 57)
        ComboBoxBa2Version.Name = "ComboBoxBa2Version"
        ComboBoxBa2Version.Size = New Size(240, 23)
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
        GroupBoxLvlList.Location = New Point(508, 246)
        GroupBoxLvlList.Name = "GroupBoxLvlList"
        GroupBoxLvlList.Size = New Size(480, 158)
        GroupBoxLvlList.TabIndex = 5
        GroupBoxLvlList.TabStop = False
        GroupBoxLvlList.Text = "Leveled NPC list (LVLN)"
        ' 
        ' CheckBoxAddToLvlList
        ' 
        CheckBoxAddToLvlList.AutoSize = True
        CheckBoxAddToLvlList.Location = New Point(12, 22)
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
        RadioLvlNew.Location = New Point(28, 48)
        RadioLvlNew.Name = "RadioLvlNew"
        RadioLvlNew.Size = New Size(52, 19)
        RadioLvlNew.TabIndex = 1
        RadioLvlNew.TabStop = True
        RadioLvlNew.Text = "New:"
        RadioLvlNew.UseVisualStyleBackColor = True
        ' 
        ' TextBoxLvlNewName
        ' 
        TextBoxLvlNewName.Location = New Point(160, 45)
        TextBoxLvlNewName.Name = "TextBoxLvlNewName"
        TextBoxLvlNewName.Size = New Size(300, 23)
        TextBoxLvlNewName.TabIndex = 2
        ' 
        ' LabelLvlNewHint
        ' 
        LabelLvlNewHint.AutoSize = True
        LabelLvlNewHint.ForeColor = SystemColors.GrayText
        LabelLvlNewHint.Location = New Point(160, 74)
        LabelLvlNewHint.Name = "LabelLvlNewHint"
        LabelLvlNewHint.Size = New Size(173, 15)
        LabelLvlNewHint.TabIndex = 3
        LabelLvlNewHint.Text = "→ npcm_<esp>_LVLN_<name>"
        ' 
        ' RadioLvlExisting
        ' 
        RadioLvlExisting.AutoSize = True
        RadioLvlExisting.Location = New Point(28, 98)
        RadioLvlExisting.Name = "RadioLvlExisting"
        RadioLvlExisting.Size = New Size(124, 19)
        RadioLvlExisting.TabIndex = 4
        RadioLvlExisting.Text = "Existing in this esp:"
        RadioLvlExisting.UseVisualStyleBackColor = True
        ' 
        ' ComboBoxLvlExisting
        ' 
        ComboBoxLvlExisting.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxLvlExisting.Location = New Point(160, 95)
        ComboBoxLvlExisting.Name = "ComboBoxLvlExisting"
        ComboBoxLvlExisting.Size = New Size(300, 23)
        ComboBoxLvlExisting.TabIndex = 5
        ' 
        ' CheckBoxLvlNoDup
        ' 
        CheckBoxLvlNoDup.AutoSize = True
        CheckBoxLvlNoDup.Location = New Point(12, 126)
        CheckBoxLvlNoDup.Name = "CheckBoxLvlNoDup"
        CheckBoxLvlNoDup.Size = New Size(274, 19)
        CheckBoxLvlNoDup.TabIndex = 6
        CheckBoxLvlNoDup.Text = "Skip NPCs already in a leveled list of this plugin"
        CheckBoxLvlNoDup.UseVisualStyleBackColor = True
        ' 
        ' LabelWarning
        ' 
        LabelWarning.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelWarning.AutoEllipsis = True
        LabelWarning.ForeColor = Color.DarkOrange
        LabelWarning.Location = New Point(342, 8)
        LabelWarning.Name = "LabelWarning"
        ' Alto de DOS líneas: es el caso normal y define la banda de la cabecera. Los tres avisos
        ' juntos son cuatro líneas (el del ESL flip ocupa dos a este ancho, MEDIDO) y no entran acá:
        ' para eso GrowWarningBand agranda el label y baja los grupos. Ver UpdateWarning.
        LabelWarning.Size = New Size(646, 36)
        LabelWarning.TabIndex = 6
        ' Centrado vertical sobre el mismo eje que los radios de scope (ambos en y=26): con un solo
        ' aviso el texto no queda pegado arriba dejando el hueco abajo.
        LabelWarning.TextAlign = ContentAlignment.MiddleLeft
        '
        ' CheckBoxActivateInLoadOrder
        '
        CheckBoxActivateInLoadOrder.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        CheckBoxActivateInLoadOrder.AutoSize = True
        CheckBoxActivateInLoadOrder.Location = New Point(12, 415)
        CheckBoxActivateInLoadOrder.Name = "CheckBoxActivateInLoadOrder"
        CheckBoxActivateInLoadOrder.Size = New Size(300, 19)
        CheckBoxActivateInLoadOrder.TabIndex = 7
        CheckBoxActivateInLoadOrder.Text = "Activate in load order (Plugins.txt) — can be overridden by mod managers"
        CheckBoxActivateInLoadOrder.UseVisualStyleBackColor = True
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Point(832, 411)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(75, 27)
        ButtonOk.TabIndex = 8
        ButtonOk.Text = "Save"
        ButtonOk.UseVisualStyleBackColor = True
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(913, 411)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 27)
        ButtonCancel.TabIndex = 9
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        ' 
        ' SaveEsp_Form
        ' 
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(1000, 449)
        Controls.Add(LabelHeader)
        Controls.Add(PanelScope)
        ' Antes iba DESPUÉS de los GroupBox y solapado con ellos: agregado último queda al fondo del
        ' z-order, así que el aviso nunca se veía. Va acá, en la fila de scope y con el z-order arriba.
        Controls.Add(LabelWarning)
        Controls.Add(GroupBoxTarget)
        Controls.Add(GroupBoxSave)
        Controls.Add(GroupBoxEncoding)
        Controls.Add(GroupBoxLvlList)
        Controls.Add(CheckBoxActivateInLoadOrder)
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
    Friend WithEvents ToolTipWarning As ToolTip
    Friend WithEvents CheckBoxActivateInLoadOrder As CheckBox
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button

End Class
