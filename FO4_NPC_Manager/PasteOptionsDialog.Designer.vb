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
        Root = New System.Windows.Forms.TableLayoutPanel()
        LabelHeader = New System.Windows.Forms.Label()
        GroupBoxBody = New System.Windows.Forms.GroupBox()
        BodyLayout = New System.Windows.Forms.TableLayoutPanel()
        CheckBoxBodyWeight = New System.Windows.Forms.CheckBox()
        CheckBoxBodyRegions = New System.Windows.Forms.CheckBox()
        CheckBoxBodySliders = New System.Windows.Forms.CheckBox()
        CheckBoxSkinOverride = New System.Windows.Forms.CheckBox()
        GroupBoxFace = New System.Windows.Forms.GroupBox()
        FaceLayout = New System.Windows.Forms.TableLayoutPanel()
        CheckBoxFaceParts = New System.Windows.Forms.CheckBox()
        CheckBoxHairColor = New System.Windows.Forms.CheckBox()
        CheckBoxFaceTints = New System.Windows.Forms.CheckBox()
        CheckBoxFaceVertexMorphs = New System.Windows.Forms.CheckBox()
        CheckBoxFaceBoneRegions = New System.Windows.Forms.CheckBox()
        GroupBoxFlags = New System.Windows.Forms.GroupBox()
        FlagsLayout = New System.Windows.Forms.TableLayoutPanel()
        CheckBoxIsCharGenPreset = New System.Windows.Forms.CheckBox()
        QuickRow = New System.Windows.Forms.FlowLayoutPanel()
        ButtonSelectAll = New System.Windows.Forms.Button()
        ButtonDeselectAll = New System.Windows.Forms.Button()
        ButtonRow = New System.Windows.Forms.FlowLayoutPanel()
        ButtonOk = New System.Windows.Forms.Button()
        ButtonCancel = New System.Windows.Forms.Button()
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
        Root.Dock = System.Windows.Forms.DockStyle.Fill
        Root.ColumnCount = 1
        Root.RowCount = 6
        Root.Padding = New System.Windows.Forms.Padding(12)
        Root.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 40.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 130.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 155.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 70.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Absolute, 36.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        Root.Controls.Add(LabelHeader, 0, 0)
        Root.Controls.Add(GroupBoxBody, 0, 1)
        Root.Controls.Add(GroupBoxFace, 0, 2)
        Root.Controls.Add(GroupBoxFlags, 0, 3)
        Root.Controls.Add(QuickRow, 0, 4)
        Root.Controls.Add(ButtonRow, 0, 5)
        '
        ' LabelHeader
        '
        LabelHeader.Dock = System.Windows.Forms.DockStyle.Fill
        LabelHeader.Margin = New System.Windows.Forms.Padding(0, 0, 0, 8)
        LabelHeader.Text = "Choose which parts of the copied look to paste. Unchecked categories keep the target NPC's original values."
        LabelHeader.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        ' GroupBoxBody
        '
        GroupBoxBody.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxBody.Margin = New System.Windows.Forms.Padding(0, 0, 0, 6)
        GroupBoxBody.Padding = New System.Windows.Forms.Padding(8, 4, 8, 8)
        GroupBoxBody.MinimumSize = New System.Drawing.Size(0, 120)
        GroupBoxBody.Text = "Body"
        GroupBoxBody.Controls.Add(BodyLayout)
        '
        ' BodyLayout
        '
        BodyLayout.Dock = System.Windows.Forms.DockStyle.Fill
        BodyLayout.ColumnCount = 1
        BodyLayout.RowCount = 4
        BodyLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        BodyLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25.0F))
        BodyLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25.0F))
        BodyLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25.0F))
        BodyLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 25.0F))
        BodyLayout.Controls.Add(CheckBoxBodyWeight, 0, 0)
        BodyLayout.Controls.Add(CheckBoxBodyRegions, 0, 1)
        BodyLayout.Controls.Add(CheckBoxBodySliders, 0, 2)
        BodyLayout.Controls.Add(CheckBoxSkinOverride, 0, 3)
        '
        ' CheckBoxBodyWeight
        '
        CheckBoxBodyWeight.AutoSize = True
        CheckBoxBodyWeight.Checked = True
        CheckBoxBodyWeight.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxBodyWeight.Text = "Body weight  (Thin / Muscular / Fat)"
        '
        ' CheckBoxBodyRegions
        '
        CheckBoxBodyRegions.AutoSize = True
        CheckBoxBodyRegions.Checked = True
        CheckBoxBodyRegions.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxBodyRegions.Text = "Body regions  (MRSV per-region weights)"
        '
        ' CheckBoxBodySliders
        '
        CheckBoxBodySliders.AutoSize = True
        CheckBoxBodySliders.Checked = True
        CheckBoxBodySliders.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxBodySliders.Text = "Body sliders  (BodySlide vertex morphs)"
        '
        ' CheckBoxSkinOverride
        '
        CheckBoxSkinOverride.AutoSize = True
        CheckBoxSkinOverride.Checked = True
        CheckBoxSkinOverride.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxSkinOverride.Text = "Skin override  (NPC.WNAM)"
        '
        ' GroupBoxFace
        '
        GroupBoxFace.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxFace.Margin = New System.Windows.Forms.Padding(0, 0, 0, 6)
        GroupBoxFace.Padding = New System.Windows.Forms.Padding(8, 4, 8, 8)
        GroupBoxFace.MinimumSize = New System.Drawing.Size(0, 145)
        GroupBoxFace.Text = "Face"
        GroupBoxFace.Controls.Add(FaceLayout)
        '
        ' FaceLayout
        '
        FaceLayout.Dock = System.Windows.Forms.DockStyle.Fill
        FaceLayout.ColumnCount = 1
        FaceLayout.RowCount = 5
        FaceLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        FaceLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20.0F))
        FaceLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20.0F))
        FaceLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20.0F))
        FaceLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20.0F))
        FaceLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 20.0F))
        FaceLayout.Controls.Add(CheckBoxFaceParts, 0, 0)
        FaceLayout.Controls.Add(CheckBoxHairColor, 0, 1)
        FaceLayout.Controls.Add(CheckBoxFaceTints, 0, 2)
        FaceLayout.Controls.Add(CheckBoxFaceVertexMorphs, 0, 3)
        FaceLayout.Controls.Add(CheckBoxFaceBoneRegions, 0, 4)
        '
        ' CheckBoxFaceParts
        '
        CheckBoxFaceParts.AutoSize = True
        CheckBoxFaceParts.Checked = True
        CheckBoxFaceParts.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxFaceParts.Text = "Face parts  (head, eyes, hair, mouth, …)"
        '
        ' CheckBoxHairColor
        '
        CheckBoxHairColor.AutoSize = True
        CheckBoxHairColor.Checked = True
        CheckBoxHairColor.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxHairColor.Text = "Hair color  (HCLF)"
        '
        ' CheckBoxFaceTints
        '
        CheckBoxFaceTints.AutoSize = True
        CheckBoxFaceTints.Checked = True
        CheckBoxFaceTints.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxFaceTints.Text = "Face tints  (skin tone, paint, scars, freckles, …)"
        '
        ' CheckBoxFaceVertexMorphs
        '
        CheckBoxFaceVertexMorphs.AutoSize = True
        CheckBoxFaceVertexMorphs.Checked = True
        CheckBoxFaceVertexMorphs.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxFaceVertexMorphs.Text = "Face vertex morphs  (chargen MSDV sliders)"
        '
        ' CheckBoxFaceBoneRegions
        '
        CheckBoxFaceBoneRegions.AutoSize = True
        CheckBoxFaceBoneRegions.Checked = True
        CheckBoxFaceBoneRegions.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxFaceBoneRegions.Text = "Face bone regions  (FMRS sliders + morph intensity)"
        '
        ' GroupBoxFlags
        '
        GroupBoxFlags.Dock = System.Windows.Forms.DockStyle.Fill
        GroupBoxFlags.Margin = New System.Windows.Forms.Padding(0, 0, 0, 6)
        GroupBoxFlags.Padding = New System.Windows.Forms.Padding(8, 4, 8, 8)
        GroupBoxFlags.MinimumSize = New System.Drawing.Size(0, 60)
        GroupBoxFlags.Text = "Flags"
        GroupBoxFlags.Controls.Add(FlagsLayout)
        '
        ' FlagsLayout
        '
        FlagsLayout.Dock = System.Windows.Forms.DockStyle.Fill
        FlagsLayout.ColumnCount = 1
        FlagsLayout.RowCount = 1
        FlagsLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        FlagsLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        FlagsLayout.Controls.Add(CheckBoxIsCharGenPreset, 0, 0)
        '
        ' CheckBoxIsCharGenPreset
        '
        CheckBoxIsCharGenPreset.AutoSize = True
        CheckBoxIsCharGenPreset.Checked = True
        CheckBoxIsCharGenPreset.CheckState = System.Windows.Forms.CheckState.Checked
        CheckBoxIsCharGenPreset.Text = "CharGen Face Preset flag  (ACBS bit 0x04)"
        '
        ' QuickRow
        '
        QuickRow.Dock = System.Windows.Forms.DockStyle.Fill
        QuickRow.FlowDirection = System.Windows.Forms.FlowDirection.LeftToRight
        QuickRow.AutoSize = True
        QuickRow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        QuickRow.Margin = New System.Windows.Forms.Padding(0, 0, 0, 8)
        QuickRow.Controls.Add(ButtonSelectAll)
        QuickRow.Controls.Add(ButtonDeselectAll)
        '
        ' ButtonSelectAll
        '
        ButtonSelectAll.Text = "Select all"
        ButtonSelectAll.Width = 90
        ButtonSelectAll.Margin = New System.Windows.Forms.Padding(0, 0, 6, 0)
        '
        ' ButtonDeselectAll
        '
        ButtonDeselectAll.Text = "Deselect all"
        ButtonDeselectAll.Width = 90
        '
        ' ButtonRow
        '
        ButtonRow.Dock = System.Windows.Forms.DockStyle.Fill
        ButtonRow.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        ButtonRow.Padding = New System.Windows.Forms.Padding(0, 6, 0, 0)
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Controls.Add(ButtonCancel)
        '
        ' ButtonOk
        '
        ButtonOk.Text = "OK"
        ButtonOk.Width = 80
        ButtonOk.DialogResult = System.Windows.Forms.DialogResult.OK
        ButtonOk.Margin = New System.Windows.Forms.Padding(6, 0, 0, 0)
        '
        ' ButtonCancel
        '
        ButtonCancel.Text = "Cancel"
        ButtonCancel.Width = 80
        ButtonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel
        '
        ' PasteOptionsDialog
        '
        Text = "Paste Look — choose categories"
        ClientSize = New System.Drawing.Size(480, 520)
        MinimumSize = New System.Drawing.Size(480, 520)
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        Controls.Add(Root)
        Root.ResumeLayout(False)
        Root.PerformLayout()
        GroupBoxBody.ResumeLayout(False)
        GroupBoxBody.PerformLayout()
        BodyLayout.ResumeLayout(False)
        BodyLayout.PerformLayout()
        GroupBoxFace.ResumeLayout(False)
        GroupBoxFace.PerformLayout()
        FaceLayout.ResumeLayout(False)
        FaceLayout.PerformLayout()
        GroupBoxFlags.ResumeLayout(False)
        GroupBoxFlags.PerformLayout()
        FlagsLayout.ResumeLayout(False)
        FlagsLayout.PerformLayout()
        QuickRow.ResumeLayout(False)
        QuickRow.PerformLayout()
        ButtonRow.ResumeLayout(False)
        ButtonRow.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents GroupBoxBody As System.Windows.Forms.GroupBox
    Friend WithEvents BodyLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxBodyWeight As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxBodyRegions As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxBodySliders As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxSkinOverride As System.Windows.Forms.CheckBox
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
