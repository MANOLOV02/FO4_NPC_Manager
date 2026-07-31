' UI built in Designer per 00-reglas-ui-y-vb (all controls declarative in InitializeComponent).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PresetCategoryPanel
    Inherits System.Windows.Forms.UserControl

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
        components = New System.ComponentModel.Container()
        Tips = New ToolTip(components)
        Root = New TableLayoutPanel()
        GroupBoxBody = New GroupBox()
        BodyLayout = New TableLayoutPanel()
        CheckBoxBodyWeight = New CheckBox()
        LabelCountBodyWeight = New Label()
        CheckBoxBodyRegions = New CheckBox()
        LabelCountBodyRegions = New Label()
        CheckBoxBodySliders = New CheckBox()
        LabelCountBodySliders = New Label()
        CheckBoxBodyScale = New CheckBox()
        LabelCountBodyScale = New Label()
        CheckBoxOverlays = New CheckBox()
        LabelCountOverlays = New Label()
        CheckBoxSkinOverride = New CheckBox()
        LabelCountSkinOverride = New Label()
        CheckBoxLmSkinTemplate = New CheckBox()
        LabelCountLmSkinTemplate = New Label()
        CheckBoxOutfit = New CheckBox()
        LabelCountOutfit = New Label()
        GroupBoxFace = New GroupBox()
        FaceLayout = New TableLayoutPanel()
        CheckBoxFaceParts = New CheckBox()
        LabelCountFaceParts = New Label()
        CheckBoxHairColor = New CheckBox()
        LabelCountHairColor = New Label()
        CheckBoxFaceTints = New CheckBox()
        LabelCountFaceTints = New Label()
        CheckBoxFaceVertexMorphs = New CheckBox()
        LabelCountFaceVertexMorphs = New Label()
        CheckBoxFaceBoneRegions = New CheckBox()
        LabelCountFaceBoneRegions = New Label()
        CheckBoxCustomMorphs = New CheckBox()
        LabelCountCustomMorphs = New Label()
        CheckBoxSculpt = New CheckBox()
        LabelCountSculpt = New Label()
        GroupBoxFlags = New GroupBox()
        FlagsLayout = New TableLayoutPanel()
        CheckBoxIsCharGenPreset = New CheckBox()
        LabelCountIsCharGenPreset = New Label()
        QuickRow = New FlowLayoutPanel()
        ButtonSelectAll = New Button()
        ButtonDeselectAll = New Button()
        Root.SuspendLayout()
        GroupBoxBody.SuspendLayout()
        BodyLayout.SuspendLayout()
        GroupBoxFace.SuspendLayout()
        FaceLayout.SuspendLayout()
        GroupBoxFlags.SuspendLayout()
        FlagsLayout.SuspendLayout()
        QuickRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        ' AutoScroll: the rows are absolute-height, so when a host gives the panel less room than the
        ' current game's category set needs the group boxes must scroll, not get clipped.
        Root.AutoScroll = True
        Root.ColumnCount = 1
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        Root.Controls.Add(GroupBoxBody, 0, 0)
        Root.Controls.Add(GroupBoxFace, 0, 1)
        Root.Controls.Add(GroupBoxFlags, 0, 2)
        Root.Controls.Add(QuickRow, 0, 3)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.RowCount = 4
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 226F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 178F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 56F))
        ' Percent: this row absorbs whatever spare height the host gives the panel, so the action row below it
        ' never overflows. What must NOT be read off it is its stretched height — see PreferredPanelHeight.
        Root.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        Root.Size = New Size(460, 500)
        Root.TabIndex = 0
        '
        ' GroupBoxBody
        '
        GroupBoxBody.Controls.Add(BodyLayout)
        GroupBoxBody.Dock = DockStyle.Fill
        GroupBoxBody.Location = New Point(0, 0)
        GroupBoxBody.Margin = New Padding(0, 0, 0, 6)
        GroupBoxBody.Name = "GroupBoxBody"
        GroupBoxBody.Padding = New Padding(8, 4, 8, 4)
        GroupBoxBody.Size = New Size(460, 220)
        GroupBoxBody.TabIndex = 0
        GroupBoxBody.TabStop = False
        GroupBoxBody.Text = "Body"
        '
        ' BodyLayout
        '
        BodyLayout.ColumnCount = 2
        BodyLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodyLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 74F))
        BodyLayout.Controls.Add(CheckBoxBodyWeight, 0, 0)
        BodyLayout.Controls.Add(LabelCountBodyWeight, 1, 0)
        BodyLayout.Controls.Add(CheckBoxBodyRegions, 0, 1)
        BodyLayout.Controls.Add(LabelCountBodyRegions, 1, 1)
        BodyLayout.Controls.Add(CheckBoxBodySliders, 0, 2)
        BodyLayout.Controls.Add(LabelCountBodySliders, 1, 2)
        BodyLayout.Controls.Add(CheckBoxBodyScale, 0, 3)
        BodyLayout.Controls.Add(LabelCountBodyScale, 1, 3)
        BodyLayout.Controls.Add(CheckBoxOverlays, 0, 4)
        BodyLayout.Controls.Add(LabelCountOverlays, 1, 4)
        BodyLayout.Controls.Add(CheckBoxSkinOverride, 0, 5)
        BodyLayout.Controls.Add(LabelCountSkinOverride, 1, 5)
        BodyLayout.Controls.Add(CheckBoxLmSkinTemplate, 0, 6)
        BodyLayout.Controls.Add(LabelCountLmSkinTemplate, 1, 6)
        BodyLayout.Controls.Add(CheckBoxOutfit, 0, 7)
        BodyLayout.Controls.Add(LabelCountOutfit, 1, 7)
        BodyLayout.Dock = DockStyle.Fill
        BodyLayout.Location = New Point(8, 20)
        BodyLayout.Name = "BodyLayout"
        BodyLayout.RowCount = 8
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        BodyLayout.Size = New Size(444, 196)
        BodyLayout.TabIndex = 0
        '
        ' CheckBoxBodyWeight
        '
        CheckBoxBodyWeight.AutoSize = True
        CheckBoxBodyWeight.Checked = True
        CheckBoxBodyWeight.CheckState = CheckState.Checked
        CheckBoxBodyWeight.Dock = DockStyle.Fill
        CheckBoxBodyWeight.Location = New Point(3, 3)
        CheckBoxBodyWeight.Name = "CheckBoxBodyWeight"
        CheckBoxBodyWeight.Size = New Size(360, 18)
        CheckBoxBodyWeight.TabIndex = 0
        CheckBoxBodyWeight.Text = "Body weight  (Thin / Muscular / Fat)"
        '
        ' LabelCountBodyWeight
        '
        LabelCountBodyWeight.Dock = DockStyle.Fill
        LabelCountBodyWeight.ForeColor = SystemColors.GrayText
        LabelCountBodyWeight.Location = New Point(370, 0)
        LabelCountBodyWeight.Name = "LabelCountBodyWeight"
        LabelCountBodyWeight.Size = New Size(68, 24)
        LabelCountBodyWeight.TabIndex = 0
        LabelCountBodyWeight.Text = "—"
        LabelCountBodyWeight.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxBodyRegions
        '
        CheckBoxBodyRegions.AutoSize = True
        CheckBoxBodyRegions.Checked = True
        CheckBoxBodyRegions.CheckState = CheckState.Checked
        CheckBoxBodyRegions.Dock = DockStyle.Fill
        CheckBoxBodyRegions.Location = New Point(3, 27)
        CheckBoxBodyRegions.Name = "CheckBoxBodyRegions"
        CheckBoxBodyRegions.Size = New Size(360, 18)
        CheckBoxBodyRegions.TabIndex = 1
        CheckBoxBodyRegions.Text = "Body regions  (MRSV per-region weights)"
        '
        ' LabelCountBodyRegions
        '
        LabelCountBodyRegions.Dock = DockStyle.Fill
        LabelCountBodyRegions.ForeColor = SystemColors.GrayText
        LabelCountBodyRegions.Location = New Point(370, 24)
        LabelCountBodyRegions.Name = "LabelCountBodyRegions"
        LabelCountBodyRegions.Size = New Size(68, 24)
        LabelCountBodyRegions.TabIndex = 1
        LabelCountBodyRegions.Text = "—"
        LabelCountBodyRegions.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxBodySliders
        '
        CheckBoxBodySliders.AutoSize = True
        CheckBoxBodySliders.Checked = True
        CheckBoxBodySliders.CheckState = CheckState.Checked
        CheckBoxBodySliders.Dock = DockStyle.Fill
        CheckBoxBodySliders.Location = New Point(3, 51)
        CheckBoxBodySliders.Name = "CheckBoxBodySliders"
        CheckBoxBodySliders.Size = New Size(360, 18)
        CheckBoxBodySliders.TabIndex = 2
        CheckBoxBodySliders.Text = "Body sliders  (BodySlide vertex morphs)"
        '
        ' LabelCountBodySliders
        '
        LabelCountBodySliders.Dock = DockStyle.Fill
        LabelCountBodySliders.ForeColor = SystemColors.GrayText
        LabelCountBodySliders.Location = New Point(370, 48)
        LabelCountBodySliders.Name = "LabelCountBodySliders"
        LabelCountBodySliders.Size = New Size(68, 24)
        LabelCountBodySliders.TabIndex = 2
        LabelCountBodySliders.Text = "—"
        LabelCountBodySliders.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxBodyScale
        '
        CheckBoxBodyScale.AutoSize = True
        CheckBoxBodyScale.Checked = True
        CheckBoxBodyScale.CheckState = CheckState.Checked
        CheckBoxBodyScale.Dock = DockStyle.Fill
        CheckBoxBodyScale.Location = New Point(3, 75)
        CheckBoxBodyScale.Name = "CheckBoxBodyScale"
        CheckBoxBodyScale.Size = New Size(360, 18)
        CheckBoxBodyScale.TabIndex = 3
        CheckBoxBodyScale.Text = "Body scale  (RaceMenu node transforms)"
        '
        ' LabelCountBodyScale
        '
        LabelCountBodyScale.Dock = DockStyle.Fill
        LabelCountBodyScale.ForeColor = SystemColors.GrayText
        LabelCountBodyScale.Location = New Point(370, 72)
        LabelCountBodyScale.Name = "LabelCountBodyScale"
        LabelCountBodyScale.Size = New Size(68, 24)
        LabelCountBodyScale.TabIndex = 3
        LabelCountBodyScale.Text = "—"
        LabelCountBodyScale.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxOverlays
        '
        CheckBoxOverlays.AutoSize = True
        CheckBoxOverlays.Checked = True
        CheckBoxOverlays.CheckState = CheckState.Checked
        CheckBoxOverlays.Dock = DockStyle.Fill
        CheckBoxOverlays.Location = New Point(3, 99)
        CheckBoxOverlays.Name = "CheckBoxOverlays"
        CheckBoxOverlays.Size = New Size(360, 18)
        CheckBoxOverlays.TabIndex = 4
        CheckBoxOverlays.Text = "Overlays  (tattoos / body paint)"
        '
        ' LabelCountOverlays
        '
        LabelCountOverlays.Dock = DockStyle.Fill
        LabelCountOverlays.ForeColor = SystemColors.GrayText
        LabelCountOverlays.Location = New Point(370, 96)
        LabelCountOverlays.Name = "LabelCountOverlays"
        LabelCountOverlays.Size = New Size(68, 24)
        LabelCountOverlays.TabIndex = 4
        LabelCountOverlays.Text = "—"
        LabelCountOverlays.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxSkinOverride
        '
        CheckBoxSkinOverride.AutoSize = True
        CheckBoxSkinOverride.Checked = True
        CheckBoxSkinOverride.CheckState = CheckState.Checked
        CheckBoxSkinOverride.Dock = DockStyle.Fill
        CheckBoxSkinOverride.Location = New Point(3, 123)
        CheckBoxSkinOverride.Name = "CheckBoxSkinOverride"
        CheckBoxSkinOverride.Size = New Size(360, 18)
        CheckBoxSkinOverride.TabIndex = 5
        CheckBoxSkinOverride.Text = "Skin override  (NPC.WNAM)"
        '
        ' LabelCountSkinOverride
        '
        LabelCountSkinOverride.Dock = DockStyle.Fill
        LabelCountSkinOverride.ForeColor = SystemColors.GrayText
        LabelCountSkinOverride.Location = New Point(370, 120)
        LabelCountSkinOverride.Name = "LabelCountSkinOverride"
        LabelCountSkinOverride.Size = New Size(68, 24)
        LabelCountSkinOverride.TabIndex = 5
        LabelCountSkinOverride.Text = "—"
        LabelCountSkinOverride.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxLmSkinTemplate
        '
        CheckBoxLmSkinTemplate.AutoSize = True
        CheckBoxLmSkinTemplate.Checked = True
        CheckBoxLmSkinTemplate.CheckState = CheckState.Checked
        CheckBoxLmSkinTemplate.Dock = DockStyle.Fill
        CheckBoxLmSkinTemplate.Location = New Point(3, 147)
        CheckBoxLmSkinTemplate.Name = "CheckBoxLmSkinTemplate"
        CheckBoxLmSkinTemplate.Size = New Size(360, 18)
        CheckBoxLmSkinTemplate.TabIndex = 6
        CheckBoxLmSkinTemplate.Text = "LM skin template  (F4SE)"
        '
        ' LabelCountLmSkinTemplate
        '
        LabelCountLmSkinTemplate.Dock = DockStyle.Fill
        LabelCountLmSkinTemplate.ForeColor = SystemColors.GrayText
        LabelCountLmSkinTemplate.Location = New Point(370, 144)
        LabelCountLmSkinTemplate.Name = "LabelCountLmSkinTemplate"
        LabelCountLmSkinTemplate.Size = New Size(68, 24)
        LabelCountLmSkinTemplate.TabIndex = 6
        LabelCountLmSkinTemplate.Text = "—"
        LabelCountLmSkinTemplate.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxOutfit
        '
        CheckBoxOutfit.AutoSize = True
        CheckBoxOutfit.Checked = True
        CheckBoxOutfit.CheckState = CheckState.Checked
        CheckBoxOutfit.Dock = DockStyle.Fill
        CheckBoxOutfit.Location = New Point(3, 171)
        CheckBoxOutfit.Name = "CheckBoxOutfit"
        CheckBoxOutfit.Size = New Size(360, 18)
        CheckBoxOutfit.TabIndex = 7
        CheckBoxOutfit.Text = "Outfit  (NPC.DOFT + NPC.SOFT)"
        '
        ' LabelCountOutfit
        '
        LabelCountOutfit.Dock = DockStyle.Fill
        LabelCountOutfit.ForeColor = SystemColors.GrayText
        LabelCountOutfit.Location = New Point(370, 168)
        LabelCountOutfit.Name = "LabelCountOutfit"
        LabelCountOutfit.Size = New Size(68, 24)
        LabelCountOutfit.TabIndex = 7
        LabelCountOutfit.Text = "—"
        LabelCountOutfit.TextAlign = ContentAlignment.MiddleRight
        '
        ' GroupBoxFace
        '
        GroupBoxFace.Controls.Add(FaceLayout)
        GroupBoxFace.Dock = DockStyle.Fill
        GroupBoxFace.Location = New Point(0, 226)
        GroupBoxFace.Margin = New Padding(0, 0, 0, 6)
        GroupBoxFace.Name = "GroupBoxFace"
        GroupBoxFace.Padding = New Padding(8, 4, 8, 4)
        GroupBoxFace.Size = New Size(460, 172)
        GroupBoxFace.TabIndex = 1
        GroupBoxFace.TabStop = False
        GroupBoxFace.Text = "Face"
        '
        ' FaceLayout
        '
        FaceLayout.ColumnCount = 2
        FaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 74F))
        FaceLayout.Controls.Add(CheckBoxFaceParts, 0, 0)
        FaceLayout.Controls.Add(LabelCountFaceParts, 1, 0)
        FaceLayout.Controls.Add(CheckBoxHairColor, 0, 1)
        FaceLayout.Controls.Add(LabelCountHairColor, 1, 1)
        FaceLayout.Controls.Add(CheckBoxFaceTints, 0, 2)
        FaceLayout.Controls.Add(LabelCountFaceTints, 1, 2)
        FaceLayout.Controls.Add(CheckBoxFaceVertexMorphs, 0, 3)
        FaceLayout.Controls.Add(LabelCountFaceVertexMorphs, 1, 3)
        FaceLayout.Controls.Add(CheckBoxCustomMorphs, 0, 4)
        FaceLayout.Controls.Add(LabelCountCustomMorphs, 1, 4)
        FaceLayout.Controls.Add(CheckBoxFaceBoneRegions, 0, 5)
        FaceLayout.Controls.Add(LabelCountFaceBoneRegions, 1, 5)
        FaceLayout.Controls.Add(CheckBoxSculpt, 0, 6)
        FaceLayout.Controls.Add(LabelCountSculpt, 1, 6)
        FaceLayout.Dock = DockStyle.Fill
        FaceLayout.Location = New Point(8, 20)
        FaceLayout.Name = "FaceLayout"
        FaceLayout.RowCount = 7
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FaceLayout.Size = New Size(444, 148)
        FaceLayout.TabIndex = 0
        '
        ' CheckBoxFaceParts
        '
        CheckBoxFaceParts.AutoSize = True
        CheckBoxFaceParts.Checked = True
        CheckBoxFaceParts.CheckState = CheckState.Checked
        CheckBoxFaceParts.Dock = DockStyle.Fill
        CheckBoxFaceParts.Location = New Point(3, 3)
        CheckBoxFaceParts.Name = "CheckBoxFaceParts"
        CheckBoxFaceParts.Size = New Size(360, 18)
        CheckBoxFaceParts.TabIndex = 0
        CheckBoxFaceParts.Text = "Face parts  (head, eyes, hair, mouth, …)"
        '
        ' LabelCountFaceParts
        '
        LabelCountFaceParts.Dock = DockStyle.Fill
        LabelCountFaceParts.ForeColor = SystemColors.GrayText
        LabelCountFaceParts.Location = New Point(370, 0)
        LabelCountFaceParts.Name = "LabelCountFaceParts"
        LabelCountFaceParts.Size = New Size(68, 24)
        LabelCountFaceParts.TabIndex = 0
        LabelCountFaceParts.Text = "—"
        LabelCountFaceParts.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxHairColor
        '
        CheckBoxHairColor.AutoSize = True
        CheckBoxHairColor.Checked = True
        CheckBoxHairColor.CheckState = CheckState.Checked
        CheckBoxHairColor.Dock = DockStyle.Fill
        CheckBoxHairColor.Location = New Point(3, 27)
        CheckBoxHairColor.Name = "CheckBoxHairColor"
        CheckBoxHairColor.Size = New Size(360, 18)
        CheckBoxHairColor.TabIndex = 1
        CheckBoxHairColor.Text = "Hair color  (HCLF)"
        '
        ' LabelCountHairColor
        '
        LabelCountHairColor.Dock = DockStyle.Fill
        LabelCountHairColor.ForeColor = SystemColors.GrayText
        LabelCountHairColor.Location = New Point(370, 24)
        LabelCountHairColor.Name = "LabelCountHairColor"
        LabelCountHairColor.Size = New Size(68, 24)
        LabelCountHairColor.TabIndex = 1
        LabelCountHairColor.Text = "—"
        LabelCountHairColor.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxFaceTints
        '
        CheckBoxFaceTints.AutoSize = True
        CheckBoxFaceTints.Checked = True
        CheckBoxFaceTints.CheckState = CheckState.Checked
        CheckBoxFaceTints.Dock = DockStyle.Fill
        CheckBoxFaceTints.Location = New Point(3, 51)
        CheckBoxFaceTints.Name = "CheckBoxFaceTints"
        CheckBoxFaceTints.Size = New Size(360, 18)
        CheckBoxFaceTints.TabIndex = 2
        CheckBoxFaceTints.Text = "Face tints  (skin tone, paint, scars, freckles, …)"
        '
        ' LabelCountFaceTints
        '
        LabelCountFaceTints.Dock = DockStyle.Fill
        LabelCountFaceTints.ForeColor = SystemColors.GrayText
        LabelCountFaceTints.Location = New Point(370, 48)
        LabelCountFaceTints.Name = "LabelCountFaceTints"
        LabelCountFaceTints.Size = New Size(68, 24)
        LabelCountFaceTints.TabIndex = 2
        LabelCountFaceTints.Text = "—"
        LabelCountFaceTints.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxFaceVertexMorphs
        '
        CheckBoxFaceVertexMorphs.AutoSize = True
        CheckBoxFaceVertexMorphs.Checked = True
        CheckBoxFaceVertexMorphs.CheckState = CheckState.Checked
        CheckBoxFaceVertexMorphs.Dock = DockStyle.Fill
        CheckBoxFaceVertexMorphs.Location = New Point(3, 75)
        CheckBoxFaceVertexMorphs.Name = "CheckBoxFaceVertexMorphs"
        CheckBoxFaceVertexMorphs.Size = New Size(360, 18)
        CheckBoxFaceVertexMorphs.TabIndex = 3
        CheckBoxFaceVertexMorphs.Text = "Face vertex morphs  (chargen MSDV sliders)"
        '
        ' LabelCountFaceVertexMorphs
        '
        LabelCountFaceVertexMorphs.Dock = DockStyle.Fill
        LabelCountFaceVertexMorphs.ForeColor = SystemColors.GrayText
        LabelCountFaceVertexMorphs.Location = New Point(370, 72)
        LabelCountFaceVertexMorphs.Name = "LabelCountFaceVertexMorphs"
        LabelCountFaceVertexMorphs.Size = New Size(68, 24)
        LabelCountFaceVertexMorphs.TabIndex = 3
        LabelCountFaceVertexMorphs.Text = "—"
        LabelCountFaceVertexMorphs.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxCustomMorphs
        '
        CheckBoxCustomMorphs.AutoSize = True
        CheckBoxCustomMorphs.Checked = True
        CheckBoxCustomMorphs.CheckState = CheckState.Checked
        CheckBoxCustomMorphs.Dock = DockStyle.Fill
        CheckBoxCustomMorphs.Location = New Point(3, 99)
        CheckBoxCustomMorphs.Name = "CheckBoxCustomMorphs"
        CheckBoxCustomMorphs.Size = New Size(360, 18)
        CheckBoxCustomMorphs.TabIndex = 4
        CheckBoxCustomMorphs.Text = "Custom morphs  (RaceMenu NiOverride)"
        '
        ' LabelCountCustomMorphs
        '
        LabelCountCustomMorphs.Dock = DockStyle.Fill
        LabelCountCustomMorphs.ForeColor = SystemColors.GrayText
        LabelCountCustomMorphs.Location = New Point(370, 96)
        LabelCountCustomMorphs.Name = "LabelCountCustomMorphs"
        LabelCountCustomMorphs.Size = New Size(68, 24)
        LabelCountCustomMorphs.TabIndex = 4
        LabelCountCustomMorphs.Text = "—"
        LabelCountCustomMorphs.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxFaceBoneRegions
        '
        CheckBoxFaceBoneRegions.AutoSize = True
        CheckBoxFaceBoneRegions.Checked = True
        CheckBoxFaceBoneRegions.CheckState = CheckState.Checked
        CheckBoxFaceBoneRegions.Dock = DockStyle.Fill
        CheckBoxFaceBoneRegions.Location = New Point(3, 99)
        CheckBoxFaceBoneRegions.Name = "CheckBoxFaceBoneRegions"
        CheckBoxFaceBoneRegions.Size = New Size(360, 18)
        CheckBoxFaceBoneRegions.TabIndex = 5
        CheckBoxFaceBoneRegions.Text = "Face bone regions  (FMRS sliders + morph intensity)"
        '
        ' LabelCountFaceBoneRegions
        '
        LabelCountFaceBoneRegions.Dock = DockStyle.Fill
        LabelCountFaceBoneRegions.ForeColor = SystemColors.GrayText
        LabelCountFaceBoneRegions.Location = New Point(370, 96)
        LabelCountFaceBoneRegions.Name = "LabelCountFaceBoneRegions"
        LabelCountFaceBoneRegions.Size = New Size(68, 24)
        LabelCountFaceBoneRegions.TabIndex = 5
        LabelCountFaceBoneRegions.Text = "—"
        LabelCountFaceBoneRegions.TextAlign = ContentAlignment.MiddleRight
        '
        ' CheckBoxSculpt
        '
        CheckBoxSculpt.AutoSize = True
        CheckBoxSculpt.Checked = True
        CheckBoxSculpt.CheckState = CheckState.Checked
        CheckBoxSculpt.Dock = DockStyle.Fill
        CheckBoxSculpt.Location = New Point(3, 123)
        CheckBoxSculpt.Name = "CheckBoxSculpt"
        CheckBoxSculpt.Size = New Size(360, 18)
        CheckBoxSculpt.TabIndex = 6
        CheckBoxSculpt.Text = "Sculpt  (per-vertex head / shape deltas)"
        '
        ' LabelCountSculpt
        '
        LabelCountSculpt.Dock = DockStyle.Fill
        LabelCountSculpt.ForeColor = SystemColors.GrayText
        LabelCountSculpt.Location = New Point(370, 120)
        LabelCountSculpt.Name = "LabelCountSculpt"
        LabelCountSculpt.Size = New Size(68, 24)
        LabelCountSculpt.TabIndex = 6
        LabelCountSculpt.Text = "—"
        LabelCountSculpt.TextAlign = ContentAlignment.MiddleRight
        '
        ' GroupBoxFlags
        '
        GroupBoxFlags.Controls.Add(FlagsLayout)
        GroupBoxFlags.Dock = DockStyle.Fill
        GroupBoxFlags.Location = New Point(0, 404)
        GroupBoxFlags.Margin = New Padding(0, 0, 0, 6)
        GroupBoxFlags.Name = "GroupBoxFlags"
        GroupBoxFlags.Padding = New Padding(8, 4, 8, 4)
        GroupBoxFlags.Size = New Size(460, 50)
        GroupBoxFlags.TabIndex = 2
        GroupBoxFlags.TabStop = False
        GroupBoxFlags.Text = "Flags"
        '
        ' FlagsLayout
        '
        FlagsLayout.ColumnCount = 2
        FlagsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FlagsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 74F))
        FlagsLayout.Controls.Add(CheckBoxIsCharGenPreset, 0, 0)
        FlagsLayout.Controls.Add(LabelCountIsCharGenPreset, 1, 0)
        FlagsLayout.Dock = DockStyle.Fill
        FlagsLayout.Location = New Point(8, 20)
        FlagsLayout.Name = "FlagsLayout"
        FlagsLayout.RowCount = 1
        FlagsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 24F))
        FlagsLayout.Size = New Size(444, 26)
        FlagsLayout.TabIndex = 0
        '
        ' CheckBoxIsCharGenPreset
        '
        CheckBoxIsCharGenPreset.AutoSize = True
        CheckBoxIsCharGenPreset.Checked = True
        CheckBoxIsCharGenPreset.CheckState = CheckState.Checked
        CheckBoxIsCharGenPreset.Dock = DockStyle.Fill
        CheckBoxIsCharGenPreset.Location = New Point(3, 3)
        CheckBoxIsCharGenPreset.Name = "CheckBoxIsCharGenPreset"
        CheckBoxIsCharGenPreset.Size = New Size(360, 18)
        CheckBoxIsCharGenPreset.TabIndex = 0
        CheckBoxIsCharGenPreset.Text = "CharGen Face Preset flag  (ACBS bit 0x04)"
        '
        ' LabelCountIsCharGenPreset
        '
        LabelCountIsCharGenPreset.Dock = DockStyle.Fill
        LabelCountIsCharGenPreset.ForeColor = SystemColors.GrayText
        LabelCountIsCharGenPreset.Location = New Point(370, 0)
        LabelCountIsCharGenPreset.Name = "LabelCountIsCharGenPreset"
        LabelCountIsCharGenPreset.Size = New Size(68, 24)
        LabelCountIsCharGenPreset.TabIndex = 0
        LabelCountIsCharGenPreset.Text = "—"
        LabelCountIsCharGenPreset.TextAlign = ContentAlignment.MiddleRight
        '
        ' QuickRow
        '
        QuickRow.AutoSize = True
        QuickRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        QuickRow.Controls.Add(ButtonSelectAll)
        QuickRow.Controls.Add(ButtonDeselectAll)
        QuickRow.Dock = DockStyle.Fill
        QuickRow.Location = New Point(0, 460)
        QuickRow.Margin = New Padding(0)
        QuickRow.Name = "QuickRow"
        QuickRow.Size = New Size(460, 40)
        QuickRow.TabIndex = 3
        '
        ' ButtonSelectAll
        '
        ButtonSelectAll.Location = New Point(3, 3)
        ButtonSelectAll.Name = "ButtonSelectAll"
        ButtonSelectAll.Size = New Size(90, 23)
        ButtonSelectAll.TabIndex = 0
        ButtonSelectAll.Text = "Select all"
        ButtonSelectAll.UseVisualStyleBackColor = True
        '
        ' ButtonDeselectAll
        '
        ButtonDeselectAll.Location = New Point(99, 3)
        ButtonDeselectAll.Name = "ButtonDeselectAll"
        ButtonDeselectAll.Size = New Size(90, 23)
        ButtonDeselectAll.TabIndex = 1
        ButtonDeselectAll.Text = "Deselect all"
        ButtonDeselectAll.UseVisualStyleBackColor = True
        '
        ' PresetCategoryPanel
        '
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        Controls.Add(Root)
        MinimumSize = New Size(360, 460)
        Name = "PresetCategoryPanel"
        Size = New Size(460, 500)
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
        ResumeLayout(False)
    End Sub

    Friend WithEvents Tips As System.Windows.Forms.ToolTip
    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxBody As System.Windows.Forms.GroupBox
    Friend WithEvents BodyLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxBodyWeight As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountBodyWeight As System.Windows.Forms.Label
    Friend WithEvents CheckBoxBodyRegions As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountBodyRegions As System.Windows.Forms.Label
    Friend WithEvents CheckBoxBodySliders As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountBodySliders As System.Windows.Forms.Label
    Friend WithEvents CheckBoxBodyScale As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountBodyScale As System.Windows.Forms.Label
    Friend WithEvents CheckBoxOverlays As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountOverlays As System.Windows.Forms.Label
    Friend WithEvents CheckBoxSkinOverride As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountSkinOverride As System.Windows.Forms.Label
    Friend WithEvents CheckBoxLmSkinTemplate As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountLmSkinTemplate As System.Windows.Forms.Label
    Friend WithEvents CheckBoxOutfit As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountOutfit As System.Windows.Forms.Label
    Friend WithEvents GroupBoxFace As System.Windows.Forms.GroupBox
    Friend WithEvents FaceLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxFaceParts As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountFaceParts As System.Windows.Forms.Label
    Friend WithEvents CheckBoxHairColor As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountHairColor As System.Windows.Forms.Label
    Friend WithEvents CheckBoxFaceTints As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountFaceTints As System.Windows.Forms.Label
    Friend WithEvents CheckBoxFaceVertexMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountFaceVertexMorphs As System.Windows.Forms.Label
    Friend WithEvents CheckBoxFaceBoneRegions As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountFaceBoneRegions As System.Windows.Forms.Label
    Friend WithEvents CheckBoxCustomMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountCustomMorphs As System.Windows.Forms.Label
    Friend WithEvents CheckBoxSculpt As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountSculpt As System.Windows.Forms.Label
    Friend WithEvents GroupBoxFlags As System.Windows.Forms.GroupBox
    Friend WithEvents FlagsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxIsCharGenPreset As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCountIsCharGenPreset As System.Windows.Forms.Label
    Friend WithEvents QuickRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSelectAll As System.Windows.Forms.Button
    Friend WithEvents ButtonDeselectAll As System.Windows.Forms.Button
End Class
