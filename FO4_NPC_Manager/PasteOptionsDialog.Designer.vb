' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PasteOptionsDialog
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
        Root = New TableLayoutPanel()
        LabelHeader = New Label()
        GroupBoxBody = New GroupBox()
        BodyLayout = New TableLayoutPanel()
        CheckBoxBodyWeight = New CheckBox()
        CheckBoxBodyRegions = New CheckBox()
        CheckBoxBodySliders = New CheckBox()
        CheckBoxOverlays = New CheckBox()
        CheckBoxSkinOverride = New CheckBox()
        CheckBoxLmSkinTemplate = New CheckBox()
        CheckBoxOutfit = New CheckBox()
        GroupBoxFace = New GroupBox()
        FaceLayout = New TableLayoutPanel()
        CheckBoxFaceParts = New CheckBox()
        CheckBoxHairColor = New CheckBox()
        CheckBoxFaceTints = New CheckBox()
        CheckBoxFaceVertexMorphs = New CheckBox()
        CheckBoxFaceBoneRegions = New CheckBox()
        GroupBoxFlags = New GroupBox()
        FlagsLayout = New TableLayoutPanel()
        CheckBoxIsCharGenPreset = New CheckBox()
        QuickRow = New FlowLayoutPanel()
        ButtonSelectAll = New Button()
        ButtonDeselectAll = New Button()
        ButtonRow = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        Root.SuspendLayout()
        GroupBoxBody.SuspendLayout()
        BodyLayout.SuspendLayout()
        GroupBoxFace.SuspendLayout()
        FaceLayout.SuspendLayout()
        GroupBoxFlags.SuspendLayout()
        FlagsLayout.SuspendLayout()
        QuickRow.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        ' 
        ' Root
        ' 
        Root.ColumnCount = 1
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        Root.Controls.Add(LabelHeader, 0, 0)
        Root.Controls.Add(GroupBoxBody, 0, 1)
        Root.Controls.Add(GroupBoxFace, 0, 2)
        Root.Controls.Add(GroupBoxFlags, 0, 3)
        Root.Controls.Add(QuickRow, 0, 4)
        Root.Controls.Add(ButtonRow, 0, 5)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(12)
        Root.RowCount = 6
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 40F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 183F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 155F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 70F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 36F))
        Root.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        Root.Size = New Size(480, 573)
        Root.TabIndex = 0
        ' 
        ' LabelHeader
        ' 
        LabelHeader.Dock = DockStyle.Fill
        LabelHeader.Location = New Point(12, 12)
        LabelHeader.Margin = New Padding(0, 0, 0, 8)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(456, 32)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Choose which parts of the copied look to paste. Unchecked categories keep the target NPC's original values."
        LabelHeader.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' GroupBoxBody
        ' 
        GroupBoxBody.Controls.Add(BodyLayout)
        GroupBoxBody.Dock = DockStyle.Fill
        GroupBoxBody.Location = New Point(12, 52)
        GroupBoxBody.Margin = New Padding(0, 0, 0, 6)
        GroupBoxBody.MinimumSize = New Size(0, 203)
        GroupBoxBody.Name = "GroupBoxBody"
        GroupBoxBody.Padding = New Padding(8, 4, 8, 8)
        GroupBoxBody.Size = New Size(456, 203)
        GroupBoxBody.TabIndex = 1
        GroupBoxBody.TabStop = False
        GroupBoxBody.Text = "Body"
        ' 
        ' BodyLayout
        ' 
        BodyLayout.ColumnCount = 1
        BodyLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodyLayout.Controls.Add(CheckBoxBodyWeight, 0, 0)
        BodyLayout.Controls.Add(CheckBoxBodyRegions, 0, 1)
        BodyLayout.Controls.Add(CheckBoxBodySliders, 0, 2)
        BodyLayout.Controls.Add(CheckBoxOverlays, 0, 3)
        BodyLayout.Controls.Add(CheckBoxSkinOverride, 0, 4)
        BodyLayout.Controls.Add(CheckBoxLmSkinTemplate, 0, 5)
        BodyLayout.Controls.Add(CheckBoxOutfit, 0, 6)
        BodyLayout.Dock = DockStyle.Fill
        BodyLayout.Location = New Point(8, 20)
        BodyLayout.Name = "BodyLayout"
        BodyLayout.RowCount = 7
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 14.28F))
        BodyLayout.Size = New Size(440, 175)
        BodyLayout.TabIndex = 0
        ' 
        ' CheckBoxBodyWeight
        ' 
        CheckBoxBodyWeight.AutoSize = True
        CheckBoxBodyWeight.Checked = True
        CheckBoxBodyWeight.CheckState = CheckState.Checked
        CheckBoxBodyWeight.Location = New Point(3, 3)
        CheckBoxBodyWeight.Name = "CheckBoxBodyWeight"
        CheckBoxBodyWeight.Size = New Size(217, 18)
        CheckBoxBodyWeight.TabIndex = 0
        CheckBoxBodyWeight.Text = "Body weight  (Thin / Muscular / Fat)"
        ' 
        ' CheckBoxBodyRegions
        ' 
        CheckBoxBodyRegions.AutoSize = True
        CheckBoxBodyRegions.Checked = True
        CheckBoxBodyRegions.CheckState = CheckState.Checked
        CheckBoxBodyRegions.Location = New Point(3, 27)
        CheckBoxBodyRegions.Name = "CheckBoxBodyRegions"
        CheckBoxBodyRegions.Size = New Size(243, 18)
        CheckBoxBodyRegions.TabIndex = 1
        CheckBoxBodyRegions.Text = "Body regions  (MRSV per-region weights)"
        ' 
        ' CheckBoxBodySliders
        ' 
        CheckBoxBodySliders.AutoSize = True
        CheckBoxBodySliders.Checked = True
        CheckBoxBodySliders.CheckState = CheckState.Checked
        CheckBoxBodySliders.Location = New Point(3, 51)
        CheckBoxBodySliders.Name = "CheckBoxBodySliders"
        CheckBoxBodySliders.Size = New Size(233, 18)
        CheckBoxBodySliders.TabIndex = 2
        CheckBoxBodySliders.Text = "Body sliders  (BodySlide vertex morphs)"
        '
        ' CheckBoxOverlays
        '
        CheckBoxOverlays.AutoSize = True
        CheckBoxOverlays.Checked = True
        CheckBoxOverlays.CheckState = CheckState.Checked
        CheckBoxOverlays.Location = New Point(3, 75)
        CheckBoxOverlays.Name = "CheckBoxOverlays"
        CheckBoxOverlays.Size = New Size(200, 18)
        CheckBoxOverlays.TabIndex = 3
        CheckBoxOverlays.Text = "Overlays  (tattoos / body paint)"
        '
        ' CheckBoxSkinOverride
        '
        CheckBoxSkinOverride.AutoSize = True
        CheckBoxSkinOverride.Checked = True
        CheckBoxSkinOverride.CheckState = CheckState.Checked
        CheckBoxSkinOverride.Location = New Point(3, 99)
        CheckBoxSkinOverride.Name = "CheckBoxSkinOverride"
        CheckBoxSkinOverride.Size = New Size(174, 18)
        CheckBoxSkinOverride.TabIndex = 4
        CheckBoxSkinOverride.Text = "Skin override  (NPC.WNAM)"
        '
        ' CheckBoxLmSkinTemplate
        '
        CheckBoxLmSkinTemplate.AutoSize = True
        CheckBoxLmSkinTemplate.Checked = True
        CheckBoxLmSkinTemplate.CheckState = CheckState.Checked
        CheckBoxLmSkinTemplate.Location = New Point(3, 123)
        CheckBoxLmSkinTemplate.Name = "CheckBoxLmSkinTemplate"
        CheckBoxLmSkinTemplate.Size = New Size(155, 18)
        CheckBoxLmSkinTemplate.TabIndex = 5
        CheckBoxLmSkinTemplate.Text = "LM skin template  (F4SE)"
        '
        ' CheckBoxOutfit
        '
        CheckBoxOutfit.AutoSize = True
        CheckBoxOutfit.Checked = True
        CheckBoxOutfit.CheckState = CheckState.Checked
        CheckBoxOutfit.Location = New Point(3, 147)
        CheckBoxOutfit.Name = "CheckBoxOutfit"
        CheckBoxOutfit.Size = New Size(168, 19)
        CheckBoxOutfit.TabIndex = 6
        CheckBoxOutfit.Text = "Outfit  (NPC.DOFT default)"
        ' 
        ' GroupBoxFace
        ' 
        GroupBoxFace.Controls.Add(FaceLayout)
        GroupBoxFace.Dock = DockStyle.Fill
        GroupBoxFace.Location = New Point(12, 210)
        GroupBoxFace.Margin = New Padding(0, 0, 0, 6)
        GroupBoxFace.MinimumSize = New Size(0, 145)
        GroupBoxFace.Name = "GroupBoxFace"
        GroupBoxFace.Padding = New Padding(8, 4, 8, 8)
        GroupBoxFace.Size = New Size(456, 149)
        GroupBoxFace.TabIndex = 2
        GroupBoxFace.TabStop = False
        GroupBoxFace.Text = "Face"
        ' 
        ' FaceLayout
        ' 
        FaceLayout.ColumnCount = 1
        FaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FaceLayout.Controls.Add(CheckBoxFaceParts, 0, 0)
        FaceLayout.Controls.Add(CheckBoxHairColor, 0, 1)
        FaceLayout.Controls.Add(CheckBoxFaceTints, 0, 2)
        FaceLayout.Controls.Add(CheckBoxFaceVertexMorphs, 0, 3)
        FaceLayout.Controls.Add(CheckBoxFaceBoneRegions, 0, 4)
        FaceLayout.Dock = DockStyle.Fill
        FaceLayout.Location = New Point(8, 20)
        FaceLayout.Name = "FaceLayout"
        FaceLayout.RowCount = 5
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 20F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 20F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 20F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 20F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 20F))
        FaceLayout.Size = New Size(440, 121)
        FaceLayout.TabIndex = 0
        ' 
        ' CheckBoxFaceParts
        ' 
        CheckBoxFaceParts.AutoSize = True
        CheckBoxFaceParts.Checked = True
        CheckBoxFaceParts.CheckState = CheckState.Checked
        CheckBoxFaceParts.Location = New Point(3, 3)
        CheckBoxFaceParts.Name = "CheckBoxFaceParts"
        CheckBoxFaceParts.Size = New Size(231, 18)
        CheckBoxFaceParts.TabIndex = 0
        CheckBoxFaceParts.Text = "Face parts  (head, eyes, hair, mouth, …)"
        ' 
        ' CheckBoxHairColor
        ' 
        CheckBoxHairColor.AutoSize = True
        CheckBoxHairColor.Checked = True
        CheckBoxHairColor.CheckState = CheckState.Checked
        CheckBoxHairColor.Location = New Point(3, 27)
        CheckBoxHairColor.Name = "CheckBoxHairColor"
        CheckBoxHairColor.Size = New Size(121, 18)
        CheckBoxHairColor.TabIndex = 1
        CheckBoxHairColor.Text = "Hair color  (HCLF)"
        ' 
        ' CheckBoxFaceTints
        ' 
        CheckBoxFaceTints.AutoSize = True
        CheckBoxFaceTints.Checked = True
        CheckBoxFaceTints.CheckState = CheckState.Checked
        CheckBoxFaceTints.Location = New Point(3, 51)
        CheckBoxFaceTints.Name = "CheckBoxFaceTints"
        CheckBoxFaceTints.Size = New Size(264, 18)
        CheckBoxFaceTints.TabIndex = 2
        CheckBoxFaceTints.Text = "Face tints  (skin tone, paint, scars, freckles, …)"
        ' 
        ' CheckBoxFaceVertexMorphs
        ' 
        CheckBoxFaceVertexMorphs.AutoSize = True
        CheckBoxFaceVertexMorphs.Checked = True
        CheckBoxFaceVertexMorphs.CheckState = CheckState.Checked
        CheckBoxFaceVertexMorphs.Location = New Point(3, 75)
        CheckBoxFaceVertexMorphs.Name = "CheckBoxFaceVertexMorphs"
        CheckBoxFaceVertexMorphs.Size = New Size(256, 18)
        CheckBoxFaceVertexMorphs.TabIndex = 3
        CheckBoxFaceVertexMorphs.Text = "Face vertex morphs  (chargen MSDV sliders)"
        ' 
        ' CheckBoxFaceBoneRegions
        ' 
        CheckBoxFaceBoneRegions.AutoSize = True
        CheckBoxFaceBoneRegions.Checked = True
        CheckBoxFaceBoneRegions.CheckState = CheckState.Checked
        CheckBoxFaceBoneRegions.Location = New Point(3, 99)
        CheckBoxFaceBoneRegions.Name = "CheckBoxFaceBoneRegions"
        CheckBoxFaceBoneRegions.Size = New Size(300, 19)
        CheckBoxFaceBoneRegions.TabIndex = 4
        CheckBoxFaceBoneRegions.Text = "Face bone regions  (FMRS sliders + morph intensity)"
        ' 
        ' GroupBoxFlags
        ' 
        GroupBoxFlags.Controls.Add(FlagsLayout)
        GroupBoxFlags.Dock = DockStyle.Fill
        GroupBoxFlags.Location = New Point(12, 365)
        GroupBoxFlags.Margin = New Padding(0, 0, 0, 6)
        GroupBoxFlags.MinimumSize = New Size(0, 60)
        GroupBoxFlags.Name = "GroupBoxFlags"
        GroupBoxFlags.Padding = New Padding(8, 4, 8, 8)
        GroupBoxFlags.Size = New Size(456, 64)
        GroupBoxFlags.TabIndex = 3
        GroupBoxFlags.TabStop = False
        GroupBoxFlags.Text = "Flags"
        ' 
        ' FlagsLayout
        ' 
        FlagsLayout.ColumnCount = 1
        FlagsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FlagsLayout.Controls.Add(CheckBoxIsCharGenPreset, 0, 0)
        FlagsLayout.Dock = DockStyle.Fill
        FlagsLayout.Location = New Point(8, 20)
        FlagsLayout.Name = "FlagsLayout"
        FlagsLayout.RowCount = 1
        FlagsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        FlagsLayout.Size = New Size(440, 36)
        FlagsLayout.TabIndex = 0
        ' 
        ' CheckBoxIsCharGenPreset
        ' 
        CheckBoxIsCharGenPreset.AutoSize = True
        CheckBoxIsCharGenPreset.Checked = True
        CheckBoxIsCharGenPreset.CheckState = CheckState.Checked
        CheckBoxIsCharGenPreset.Location = New Point(3, 3)
        CheckBoxIsCharGenPreset.Name = "CheckBoxIsCharGenPreset"
        CheckBoxIsCharGenPreset.Size = New Size(243, 19)
        CheckBoxIsCharGenPreset.TabIndex = 0
        CheckBoxIsCharGenPreset.Text = "CharGen Face Preset flag  (ACBS bit 0x04)"
        ' 
        ' QuickRow
        ' 
        QuickRow.AutoSize = True
        QuickRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        QuickRow.Controls.Add(ButtonSelectAll)
        QuickRow.Controls.Add(ButtonDeselectAll)
        QuickRow.Dock = DockStyle.Fill
        QuickRow.Location = New Point(12, 435)
        QuickRow.Margin = New Padding(0, 0, 0, 8)
        QuickRow.Name = "QuickRow"
        QuickRow.Size = New Size(456, 28)
        QuickRow.TabIndex = 4
        ' 
        ' ButtonSelectAll
        ' 
        ButtonSelectAll.Location = New Point(3, 3)
        ButtonSelectAll.Name = "ButtonSelectAll"
        ButtonSelectAll.Size = New Size(90, 23)
        ButtonSelectAll.TabIndex = 0
        ButtonSelectAll.Text = "Select all"
        ' 
        ' ButtonDeselectAll
        ' 
        ButtonDeselectAll.Location = New Point(99, 3)
        ButtonDeselectAll.Name = "ButtonDeselectAll"
        ButtonDeselectAll.Size = New Size(90, 23)
        ButtonDeselectAll.TabIndex = 1
        ButtonDeselectAll.Text = "Deselect all"
        ' 
        ' ButtonRow
        ' 
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Dock = DockStyle.Fill
        ButtonRow.FlowDirection = FlowDirection.RightToLeft
        ButtonRow.Location = New Point(15, 474)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New Padding(0, 6, 0, 0)
        ButtonRow.Size = New Size(450, 59)
        ButtonRow.TabIndex = 5
        ' 
        ' ButtonOk
        ' 
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(367, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(281, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' PasteOptionsDialog
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(480, 573)
        Controls.Add(Root)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        MinimumSize = New Size(480, 573)
        Name = "PasteOptionsDialog"
        StartPosition = FormStartPosition.CenterParent
        Text = "Paste Look — choose categories"
        Root.ResumeLayout(False)
        Root.PerformLayout()
        GroupBoxBody.ResumeLayout(False)
        BodyLayout.ResumeLayout(False)
        BodyLayout.PerformLayout()
        GroupBoxFace.ResumeLayout(False)
        FaceLayout.ResumeLayout(False)
        FaceLayout.PerformLayout()
        GroupBoxFlags.ResumeLayout(False)
        FlagsLayout.ResumeLayout(False)
        FlagsLayout.PerformLayout()
        QuickRow.ResumeLayout(False)
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents GroupBoxBody As System.Windows.Forms.GroupBox
    Friend WithEvents BodyLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxBodyWeight As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxBodyRegions As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxBodySliders As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxOverlays As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxSkinOverride As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxLmSkinTemplate As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxOutfit As System.Windows.Forms.CheckBox
    Friend WithEvents GroupBoxFace As System.Windows.Forms.GroupBox
    Friend WithEvents FaceLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxFaceParts As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxHairColor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFaceTints As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFaceVertexMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxFaceBoneRegions As System.Windows.Forms.CheckBox
    Friend WithEvents GroupBoxFlags As System.Windows.Forms.GroupBox
    Friend WithEvents FlagsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxIsCharGenPreset As System.Windows.Forms.CheckBox
    Friend WithEvents QuickRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSelectAll As System.Windows.Forms.Button
    Friend WithEvents ButtonDeselectAll As System.Windows.Forms.Button
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
