<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcFilterAdvanced_Form
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
        PanelRoot = New TableLayoutPanel()
        LabelSectionHeadParts = New Label()
        LabelFacetHair = New Label()
        TextBoxFacetHair = New TextBox()
        LabelFacetEyes = New Label()
        TextBoxFacetEyes = New TextBox()
        LabelFacetFace = New Label()
        TextBoxFacetFace = New TextBox()
        LabelFacetHeadTex = New Label()
        TextBoxFacetHeadTex = New TextBox()
        LabelSectionBody = New Label()
        LabelFacetSkin = New Label()
        TextBoxFacetSkin = New TextBox()
        LabelFacetOutfit = New Label()
        TextBoxFacetOutfit = New TextBox()
        LabelFacetRace = New Label()
        TextBoxFacetRace = New TextBox()
        LabelFacetHairColor = New Label()
        TextBoxFacetHairColor = New TextBox()
        LabelFacetTplt = New Label()
        TextBoxFacetTplt = New TextBox()
        LabelFacetOmod = New Label()
        TextBoxFacetOmod = New TextBox()
        PanelFlags = New FlowLayoutPanel()
        LabelFacetFlags = New Label()
        CheckBoxFlagFemale = New CheckBox()
        CheckBoxFlagMale = New CheckBox()
        CheckBoxFlagPreset = New CheckBox()
        CheckBoxFlagRobot = New CheckBox()
        CheckBoxFlagInherited = New CheckBox()
        CheckBoxFollowTemplates = New CheckBox()
        LabelFreeText = New Label()
        TextBoxFreeText = New TextBox()
        LabelPreview = New Label()
        LabelPreviewValue = New Label()
        LabelHint = New Label()
        PanelButtons = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancelDialog = New Button()
        ButtonResetFields = New Button()
        PanelRoot.SuspendLayout()
        PanelFlags.SuspendLayout()
        PanelButtons.SuspendLayout()
        SuspendLayout()
        '
        ' PanelRoot
        '
        ' 4 columns: AutoSize label / elastic box / AutoSize label / elastic box. Two pairs per row so
        ' the dialog stays narrow enough to sit next to the main window.
        PanelRoot.ColumnCount = 4
        PanelRoot.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        PanelRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        PanelRoot.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        PanelRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50.0F))
        PanelRoot.Controls.Add(LabelSectionHeadParts, 0, 0)
        PanelRoot.Controls.Add(LabelFacetHair, 0, 1)
        PanelRoot.Controls.Add(TextBoxFacetHair, 1, 1)
        PanelRoot.Controls.Add(LabelFacetEyes, 2, 1)
        PanelRoot.Controls.Add(TextBoxFacetEyes, 3, 1)
        PanelRoot.Controls.Add(LabelFacetFace, 0, 2)
        PanelRoot.Controls.Add(TextBoxFacetFace, 1, 2)
        PanelRoot.Controls.Add(LabelFacetHeadTex, 2, 2)
        PanelRoot.Controls.Add(TextBoxFacetHeadTex, 3, 2)
        PanelRoot.Controls.Add(LabelSectionBody, 0, 3)
        PanelRoot.Controls.Add(LabelFacetSkin, 0, 4)
        PanelRoot.Controls.Add(TextBoxFacetSkin, 1, 4)
        PanelRoot.Controls.Add(LabelFacetOutfit, 2, 4)
        PanelRoot.Controls.Add(TextBoxFacetOutfit, 3, 4)
        PanelRoot.Controls.Add(LabelFacetRace, 0, 5)
        PanelRoot.Controls.Add(TextBoxFacetRace, 1, 5)
        PanelRoot.Controls.Add(LabelFacetHairColor, 2, 5)
        PanelRoot.Controls.Add(TextBoxFacetHairColor, 3, 5)
        PanelRoot.Controls.Add(LabelFacetTplt, 0, 6)
        PanelRoot.Controls.Add(TextBoxFacetTplt, 1, 6)
        PanelRoot.Controls.Add(LabelFacetOmod, 2, 6)
        PanelRoot.Controls.Add(TextBoxFacetOmod, 3, 6)
        PanelRoot.Controls.Add(PanelFlags, 0, 7)
        PanelRoot.Controls.Add(LabelFreeText, 0, 8)
        PanelRoot.Controls.Add(TextBoxFreeText, 1, 8)
        PanelRoot.Controls.Add(LabelPreview, 0, 9)
        PanelRoot.Controls.Add(LabelPreviewValue, 1, 9)
        PanelRoot.Controls.Add(LabelHint, 0, 10)
        PanelRoot.Controls.Add(PanelButtons, 0, 11)
        PanelRoot.SetColumnSpan(LabelSectionHeadParts, 4)
        PanelRoot.SetColumnSpan(LabelSectionBody, 4)
        PanelRoot.SetColumnSpan(PanelFlags, 4)
        PanelRoot.SetColumnSpan(TextBoxFreeText, 3)
        PanelRoot.SetColumnSpan(LabelPreviewValue, 3)
        PanelRoot.SetColumnSpan(LabelHint, 4)
        PanelRoot.SetColumnSpan(PanelButtons, 4)
        ' Dock=Top + AutoSize (not Fill): the panel measures to exactly the height its rows need, and
        ' the form then sizes ITSELF to that in Load. With Dock=Fill and a fixed ClientSize the last
        ' row had no slack left and the buttons came out sliced in half.
        PanelRoot.AutoSize = True
        PanelRoot.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelRoot.Dock = DockStyle.Top
        PanelRoot.Location = New Point(0, 0)
        PanelRoot.Name = "PanelRoot"
        PanelRoot.Padding = New Padding(10, 8, 10, 10)
        PanelRoot.RowCount = 12
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ' ⚠ The LAST row is AutoSize too. A Percent-100 last row eats whatever the fixed ClientSize
        ' left over — which was negative here, so the button row got clipped.
        PanelRoot.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PanelRoot.Size = New Size(680, 470)
        PanelRoot.TabIndex = 0
        '
        ' Section headers
        '
        LabelSectionHeadParts.AutoSize = True
        LabelSectionHeadParts.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        LabelSectionHeadParts.Margin = New Padding(3, 2, 3, 4)
        LabelSectionHeadParts.Name = "LabelSectionHeadParts"
        LabelSectionHeadParts.Size = New Size(70, 15)
        LabelSectionHeadParts.TabIndex = 0
        LabelSectionHeadParts.Text = "Head parts"
        LabelSectionBody.AutoSize = True
        LabelSectionBody.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        LabelSectionBody.Margin = New Padding(3, 10, 3, 4)
        LabelSectionBody.Name = "LabelSectionBody"
        LabelSectionBody.Size = New Size(120, 15)
        LabelSectionBody.TabIndex = 0
        LabelSectionBody.Text = "Body and record"
        '
        ' Facet labels + boxes
        '
        LabelFacetHair.Anchor = AnchorStyles.Left
        LabelFacetHair.AutoSize = True
        LabelFacetHair.Name = "LabelFacetHair"
        LabelFacetHair.Size = New Size(31, 15)
        LabelFacetHair.TabIndex = 0
        LabelFacetHair.Text = "Hair:"
        TextBoxFacetHair.Dock = DockStyle.Fill
        TextBoxFacetHair.Name = "TextBoxFacetHair"
        TextBoxFacetHair.PlaceholderText = "EDID / name / mesh / 0xFormID"
        TextBoxFacetHair.Size = New Size(200, 23)
        TextBoxFacetHair.TabIndex = 1
        LabelFacetEyes.Anchor = AnchorStyles.Left
        LabelFacetEyes.AutoSize = True
        LabelFacetEyes.Name = "LabelFacetEyes"
        LabelFacetEyes.Size = New Size(36, 15)
        LabelFacetEyes.TabIndex = 2
        LabelFacetEyes.Text = "Eyes:"
        TextBoxFacetEyes.Dock = DockStyle.Fill
        TextBoxFacetEyes.Name = "TextBoxFacetEyes"
        TextBoxFacetEyes.PlaceholderText = "EDID / name / mesh / 0xFormID"
        TextBoxFacetEyes.Size = New Size(200, 23)
        TextBoxFacetEyes.TabIndex = 3
        LabelFacetFace.Anchor = AnchorStyles.Left
        LabelFacetFace.AutoSize = True
        LabelFacetFace.Name = "LabelFacetFace"
        LabelFacetFace.Size = New Size(35, 15)
        LabelFacetFace.TabIndex = 4
        LabelFacetFace.Text = "Face:"
        TextBoxFacetFace.Dock = DockStyle.Fill
        TextBoxFacetFace.Name = "TextBoxFacetFace"
        TextBoxFacetFace.PlaceholderText = "brows / scars / facial hair / misc"
        TextBoxFacetFace.Size = New Size(200, 23)
        TextBoxFacetFace.TabIndex = 5
        LabelFacetHeadTex.Anchor = AnchorStyles.Left
        LabelFacetHeadTex.AutoSize = True
        LabelFacetHeadTex.Name = "LabelFacetHeadTex"
        LabelFacetHeadTex.Size = New Size(62, 15)
        LabelFacetHeadTex.TabIndex = 6
        LabelFacetHeadTex.Text = "Head tex:"
        TextBoxFacetHeadTex.Dock = DockStyle.Fill
        TextBoxFacetHeadTex.Name = "TextBoxFacetHeadTex"
        TextBoxFacetHeadTex.PlaceholderText = "TXST (FTST)"
        TextBoxFacetHeadTex.Size = New Size(200, 23)
        TextBoxFacetHeadTex.TabIndex = 7
        LabelFacetSkin.Anchor = AnchorStyles.Left
        LabelFacetSkin.AutoSize = True
        LabelFacetSkin.Name = "LabelFacetSkin"
        LabelFacetSkin.Size = New Size(33, 15)
        LabelFacetSkin.TabIndex = 8
        LabelFacetSkin.Text = "Skin:"
        TextBoxFacetSkin.Dock = DockStyle.Fill
        TextBoxFacetSkin.Name = "TextBoxFacetSkin"
        TextBoxFacetSkin.PlaceholderText = "skin ARMO — 'none' = no WNAM"
        TextBoxFacetSkin.Size = New Size(200, 23)
        TextBoxFacetSkin.TabIndex = 9
        LabelFacetOutfit.Anchor = AnchorStyles.Left
        LabelFacetOutfit.AutoSize = True
        LabelFacetOutfit.Name = "LabelFacetOutfit"
        LabelFacetOutfit.Size = New Size(42, 15)
        LabelFacetOutfit.TabIndex = 10
        LabelFacetOutfit.Text = "Outfit:"
        TextBoxFacetOutfit.Dock = DockStyle.Fill
        TextBoxFacetOutfit.Name = "TextBoxFacetOutfit"
        TextBoxFacetOutfit.PlaceholderText = "OTFT (DOFT + SOFT)"
        TextBoxFacetOutfit.Size = New Size(200, 23)
        TextBoxFacetOutfit.TabIndex = 11
        LabelFacetRace.Anchor = AnchorStyles.Left
        LabelFacetRace.AutoSize = True
        LabelFacetRace.Name = "LabelFacetRace"
        LabelFacetRace.Size = New Size(37, 15)
        LabelFacetRace.TabIndex = 12
        LabelFacetRace.Text = "Race:"
        TextBoxFacetRace.Dock = DockStyle.Fill
        TextBoxFacetRace.Name = "TextBoxFacetRace"
        TextBoxFacetRace.PlaceholderText = "RACE — EDID / name / 0xFormID"
        TextBoxFacetRace.Size = New Size(200, 23)
        TextBoxFacetRace.TabIndex = 13
        LabelFacetHairColor.Anchor = AnchorStyles.Left
        LabelFacetHairColor.AutoSize = True
        LabelFacetHairColor.Name = "LabelFacetHairColor"
        LabelFacetHairColor.Size = New Size(66, 15)
        LabelFacetHairColor.TabIndex = 14
        LabelFacetHairColor.Text = "Hair color:"
        TextBoxFacetHairColor.Dock = DockStyle.Fill
        TextBoxFacetHairColor.Name = "TextBoxFacetHairColor"
        TextBoxFacetHairColor.PlaceholderText = "CLFM"
        TextBoxFacetHairColor.Size = New Size(200, 23)
        TextBoxFacetHairColor.TabIndex = 15
        LabelFacetTplt.Anchor = AnchorStyles.Left
        LabelFacetTplt.AutoSize = True
        LabelFacetTplt.Name = "LabelFacetTplt"
        LabelFacetTplt.Size = New Size(59, 15)
        LabelFacetTplt.TabIndex = 16
        LabelFacetTplt.Text = "Template:"
        TextBoxFacetTplt.Dock = DockStyle.Fill
        TextBoxFacetTplt.Name = "TextBoxFacetTplt"
        TextBoxFacetTplt.PlaceholderText = "TPLT / TPTA target"
        TextBoxFacetTplt.Size = New Size(200, 23)
        TextBoxFacetTplt.TabIndex = 17
        LabelFacetOmod.Anchor = AnchorStyles.Left
        LabelFacetOmod.AutoSize = True
        LabelFacetOmod.Name = "LabelFacetOmod"
        LabelFacetOmod.Size = New Size(43, 15)
        LabelFacetOmod.TabIndex = 18
        LabelFacetOmod.Text = "OMOD:"
        TextBoxFacetOmod.Dock = DockStyle.Fill
        TextBoxFacetOmod.Name = "TextBoxFacetOmod"
        TextBoxFacetOmod.PlaceholderText = "robot parts (OBTS)"
        TextBoxFacetOmod.Size = New Size(200, 23)
        TextBoxFacetOmod.TabIndex = 19
        '
        ' PanelFlags
        '
        PanelFlags.AutoSize = True
        PanelFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelFlags.Controls.Add(LabelFacetFlags)
        PanelFlags.Controls.Add(CheckBoxFlagFemale)
        PanelFlags.Controls.Add(CheckBoxFlagMale)
        PanelFlags.Controls.Add(CheckBoxFlagPreset)
        PanelFlags.Controls.Add(CheckBoxFlagRobot)
        PanelFlags.Controls.Add(CheckBoxFlagInherited)
        PanelFlags.Controls.Add(CheckBoxFollowTemplates)
        PanelFlags.Dock = DockStyle.Fill
        PanelFlags.Margin = New Padding(0, 8, 0, 4)
        PanelFlags.Name = "PanelFlags"
        PanelFlags.Size = New Size(604, 25)
        PanelFlags.TabIndex = 20
        PanelFlags.WrapContents = True
        LabelFacetFlags.Anchor = AnchorStyles.Left
        LabelFacetFlags.AutoSize = True
        LabelFacetFlags.Margin = New Padding(3, 6, 6, 0)
        LabelFacetFlags.Name = "LabelFacetFlags"
        LabelFacetFlags.Size = New Size(38, 15)
        LabelFacetFlags.TabIndex = 0
        LabelFacetFlags.Text = "Flags:"
        CheckBoxFlagFemale.AutoSize = True
        CheckBoxFlagFemale.Name = "CheckBoxFlagFemale"
        CheckBoxFlagFemale.Size = New Size(65, 19)
        CheckBoxFlagFemale.TabIndex = 1
        CheckBoxFlagFemale.Text = "Female"
        CheckBoxFlagFemale.UseVisualStyleBackColor = True
        CheckBoxFlagMale.AutoSize = True
        CheckBoxFlagMale.Name = "CheckBoxFlagMale"
        CheckBoxFlagMale.Size = New Size(52, 19)
        CheckBoxFlagMale.TabIndex = 2
        CheckBoxFlagMale.Text = "Male"
        CheckBoxFlagMale.UseVisualStyleBackColor = True
        CheckBoxFlagPreset.AutoSize = True
        CheckBoxFlagPreset.Name = "CheckBoxFlagPreset"
        CheckBoxFlagPreset.Size = New Size(116, 19)
        CheckBoxFlagPreset.TabIndex = 3
        CheckBoxFlagPreset.Text = "CharGen preset"
        CheckBoxFlagPreset.UseVisualStyleBackColor = True
        CheckBoxFlagRobot.AutoSize = True
        CheckBoxFlagRobot.Name = "CheckBoxFlagRobot"
        CheckBoxFlagRobot.Size = New Size(58, 19)
        CheckBoxFlagRobot.TabIndex = 4
        CheckBoxFlagRobot.Text = "Robot"
        CheckBoxFlagRobot.UseVisualStyleBackColor = True
        CheckBoxFlagInherited.AutoSize = True
        CheckBoxFlagInherited.Name = "CheckBoxFlagInherited"
        CheckBoxFlagInherited.Size = New Size(120, 19)
        CheckBoxFlagInherited.TabIndex = 5
        CheckBoxFlagInherited.Text = "Inherited look"
        CheckBoxFlagInherited.UseVisualStyleBackColor = True
        CheckBoxFollowTemplates.AutoSize = True
        CheckBoxFollowTemplates.Checked = True
        CheckBoxFollowTemplates.CheckState = CheckState.Checked
        CheckBoxFollowTemplates.Margin = New Padding(18, 3, 3, 3)
        CheckBoxFollowTemplates.Name = "CheckBoxFollowTemplates"
        CheckBoxFollowTemplates.Size = New Size(130, 19)
        CheckBoxFollowTemplates.TabIndex = 6
        CheckBoxFollowTemplates.Text = "Follow templates"
        CheckBoxFollowTemplates.UseVisualStyleBackColor = True
        '
        ' Free text + preview
        '
        LabelFreeText.Anchor = AnchorStyles.Left
        LabelFreeText.AutoSize = True
        LabelFreeText.Name = "LabelFreeText"
        LabelFreeText.Size = New Size(55, 15)
        LabelFreeText.TabIndex = 21
        LabelFreeText.Text = "Free text:"
        TextBoxFreeText.Dock = DockStyle.Fill
        TextBoxFreeText.Name = "TextBoxFreeText"
        TextBoxFreeText.PlaceholderText = "name / EditorID / FormID / plugin — the plain search"
        TextBoxFreeText.Size = New Size(500, 23)
        TextBoxFreeText.TabIndex = 22
        LabelPreview.Anchor = AnchorStyles.Left
        LabelPreview.AutoSize = True
        LabelPreview.Name = "LabelPreview"
        LabelPreview.Size = New Size(43, 15)
        LabelPreview.TabIndex = 23
        LabelPreview.Text = "Query:"
        LabelPreviewValue.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        LabelPreviewValue.AutoEllipsis = True
        LabelPreviewValue.Font = New Font("Consolas", 9.0F)
        LabelPreviewValue.Margin = New Padding(3, 6, 3, 3)
        LabelPreviewValue.Name = "LabelPreviewValue"
        LabelPreviewValue.Size = New Size(500, 15)
        LabelPreviewValue.TabIndex = 24
        LabelPreviewValue.Text = ""
        '
        ' LabelHint
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = SystemColors.GrayText
        LabelHint.Margin = New Padding(3, 8, 3, 4)
        LabelHint.Name = "LabelHint"
        LabelHint.Size = New Size(560, 30)
        LabelHint.TabIndex = 25
        LabelHint.Text = "Values are matched as substrings. Use '|' for alternatives (blue|green), 'none' for ""no such record"", " &
                         "and 0x1A2B or a full 8-digit hex to match a FormID." & vbCrLf &
                         "The same query can be typed straight into the search box; it is the only place the filter lives."
        '
        ' PanelButtons
        '
        PanelButtons.AutoSize = True
        PanelButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelButtons.Controls.Add(ButtonCancelDialog)
        PanelButtons.Controls.Add(ButtonOk)
        PanelButtons.Controls.Add(ButtonResetFields)
        PanelButtons.Dock = DockStyle.Fill
        PanelButtons.FlowDirection = FlowDirection.RightToLeft
        PanelButtons.Margin = New Padding(0, 6, 0, 0)
        PanelButtons.Name = "PanelButtons"
        PanelButtons.Size = New Size(604, 33)
        PanelButtons.TabIndex = 26
        ButtonOk.AutoSize = True
        ButtonOk.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonOk.MinimumSize = New Size(80, 26)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 26)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        ButtonCancelDialog.AutoSize = True
        ButtonCancelDialog.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonCancelDialog.MinimumSize = New Size(80, 26)
        ButtonCancelDialog.Name = "ButtonCancelDialog"
        ButtonCancelDialog.Size = New Size(80, 26)
        ButtonCancelDialog.TabIndex = 1
        ButtonCancelDialog.Text = "Cancel"
        ButtonCancelDialog.UseVisualStyleBackColor = True
        ButtonResetFields.AutoSize = True
        ButtonResetFields.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonResetFields.MinimumSize = New Size(110, 26)
        ButtonResetFields.Name = "ButtonResetFields"
        ButtonResetFields.Size = New Size(110, 26)
        ButtonResetFields.TabIndex = 2
        ButtonResetFields.Text = "Clear criteria"
        ButtonResetFields.UseVisualStyleBackColor = True
        '
        ' NpcFilterAdvanced_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ' Safety net: if a bigger system font / DPI still overflows what Load computes, the dialog
        ' scrolls instead of clipping the buttons.
        AutoScroll = True
        CancelButton = ButtonCancelDialog
        ClientSize = New Size(680, 470)
        Controls.Add(PanelRoot)
        MinimizeBox = False
        MaximizeBox = False
        MinimumSize = New Size(600, 320)
        Name = "NpcFilterAdvanced_Form"
        ShowIcon = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Advanced NPC filter"
        PanelRoot.ResumeLayout(False)
        PanelRoot.PerformLayout()
        PanelFlags.ResumeLayout(False)
        PanelFlags.PerformLayout()
        PanelButtons.ResumeLayout(False)
        PanelButtons.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents PanelRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSectionHeadParts As System.Windows.Forms.Label
    Friend WithEvents LabelSectionBody As System.Windows.Forms.Label
    Friend WithEvents LabelFacetHair As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetHair As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetEyes As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetEyes As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetFace As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetFace As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetHeadTex As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetHeadTex As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetSkin As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetSkin As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetOutfit As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetOutfit As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetRace As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetRace As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetHairColor As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetHairColor As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetTplt As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetTplt As System.Windows.Forms.TextBox
    Friend WithEvents LabelFacetOmod As System.Windows.Forms.Label
    Friend WithEvents TextBoxFacetOmod As System.Windows.Forms.TextBox
    Friend WithEvents PanelFlags As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelFacetFlags As System.Windows.Forms.Label
    Friend WithEvents CheckBoxFlagFemale As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFlagMale As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFlagPreset As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFlagRobot As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFlagInherited As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFollowTemplates As System.Windows.Forms.CheckBox
    Friend WithEvents LabelFreeText As System.Windows.Forms.Label
    Friend WithEvents TextBoxFreeText As System.Windows.Forms.TextBox
    Friend WithEvents LabelPreview As System.Windows.Forms.Label
    Friend WithEvents LabelPreviewValue As System.Windows.Forms.Label
    Friend WithEvents LabelHint As System.Windows.Forms.Label
    Friend WithEvents PanelButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancelDialog As System.Windows.Forms.Button
    Friend WithEvents ButtonResetFields As System.Windows.Forms.Button
End Class
