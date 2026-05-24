' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class EditFace_Form
    Inherits EditorFormBase

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
        PreviewSplit = New SplitContainer()
        RootLayout = New TableLayoutPanel()
        TabsFace = New TabControl()
        TabPageFaceParts = New TabPage()
        FacePartsLayout = New TableLayoutPanel()
        GroupBoxHeadParts = New GroupBox()
        HeadPartsLayout = New TableLayoutPanel()
        TableLayoutPanel1 = New TableLayoutPanel()
        ButtonRemoveHeadPart = New Button()
        ButtonAddMisc = New Button()
        ButtonAddMeatcaps = New Button()
        ButtonAddHeadRear = New Button()
        ButtonAddTeeth = New Button()
        ButtonAddScar = New Button()
        ButtonAddEyebrows = New Button()
        ButtonAddFacialHair = New Button()
        ButtonAddEyes = New Button()
        ButtonAddHair = New Button()
        ButtonAddFace = New Button()
        ListViewHeadParts = New ListView()
        ColHeadPartType = New ColumnHeader()
        ColHeadPartEditorID = New ColumnHeader()
        ColHeadPartName = New ColumnHeader()
        ColHeadPartPlugin = New ColumnHeader()
        ColHeadPartFormID = New ColumnHeader()
        GroupBoxHairColor = New GroupBox()
        HairColorLayout = New TableLayoutPanel()
        ComboBoxHairColor = New ComboBox()
        ButtonClearHairColor = New Button()
        PanelHairColorSwatch = New Panel()
        GroupBoxFaceFlags = New GroupBox()
        FaceFlagsLayout = New FlowLayoutPanel()
        CheckBoxIsCharGenFacePreset = New CheckBox()
        LabelCharGenHelp = New Label()
        TabPageTints = New TabPage()
        TintsLayout = New TableLayoutPanel()
        TextBoxTintFilter = New TextBox()
        ListViewTints = New ListView()
        ColumnTintGroup = New ColumnHeader()
        ColumnTintSlot = New ColumnHeader()
        ColumnTintLayer = New ColumnHeader()
        ColumnTintColor = New ColumnHeader()
        ColumnTintPercent = New ColumnHeader()
        TintsButtonRow = New FlowLayoutPanel()
        ButtonAddTint = New Button()
        ButtonRemoveTint = New Button()
        ButtonRemoveAllInCategory = New Button()
        ButtonRemoveZeroedTints = New Button()
        PanelTintDetail = New GroupBox()
        TintDetailLayout = New TableLayoutPanel()
        LabelTintLayerCaption = New Label()
        LabelTintLayerName = New Label()
        LabelTintPaletteCaption = New Label()
        ComboBoxTintPalette = New ComboBox()
        ButtonTintCustomRGB = New Button()
        PanelTintColorSwatch = New Panel()
        LabelTintPercentCaption = New Label()
        TrackBarTintPercent = New TinySliderTextBox()
        TabPageVertex = New TabPage()
        VertexMorphsPanel = New Panel()
        TabPageBoneRegions = New TabPage()
        BoneRegionsRoot = New TableLayoutPanel()
        BoneRegionsContainer = New Panel()
        GroupBoxFmin = New GroupBox()
        FminLayout = New TableLayoutPanel()
        LabelFminCaption = New Label()
        TrackBarFmin = New TinySliderTextBox()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        ButtonResetSection = New Button()
        PreviewSidebar = New TableLayoutPanel()
        RenderTogglesPanel = New FlowLayoutPanel()
        CheckBoxRenderGore = New CheckBox()
        PreviewHostPanel = New Panel()
        CType(PreviewSplit, ComponentModel.ISupportInitialize).BeginInit()
        PreviewSplit.Panel1.SuspendLayout()
        PreviewSplit.Panel2.SuspendLayout()
        PreviewSplit.SuspendLayout()
        RootLayout.SuspendLayout()
        TabsFace.SuspendLayout()
        TabPageFaceParts.SuspendLayout()
        FacePartsLayout.SuspendLayout()
        GroupBoxHeadParts.SuspendLayout()
        HeadPartsLayout.SuspendLayout()
        TableLayoutPanel1.SuspendLayout()
        GroupBoxHairColor.SuspendLayout()
        HairColorLayout.SuspendLayout()
        GroupBoxFaceFlags.SuspendLayout()
        FaceFlagsLayout.SuspendLayout()
        TabPageTints.SuspendLayout()
        TintsLayout.SuspendLayout()
        TintsButtonRow.SuspendLayout()
        PanelTintDetail.SuspendLayout()
        TintDetailLayout.SuspendLayout()
        TabPageVertex.SuspendLayout()
        TabPageBoneRegions.SuspendLayout()
        BoneRegionsRoot.SuspendLayout()
        GroupBoxFmin.SuspendLayout()
        FminLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        PreviewSidebar.SuspendLayout()
        RenderTogglesPanel.SuspendLayout()
        SuspendLayout()
        ' 
        ' PreviewSplit
        ' 
        PreviewSplit.Dock = DockStyle.Fill
        PreviewSplit.FixedPanel = FixedPanel.Panel1
        PreviewSplit.Location = New Point(0, 0)
        PreviewSplit.Name = "PreviewSplit"
        ' 
        ' PreviewSplit.Panel1
        ' 
        PreviewSplit.Panel1.Controls.Add(RootLayout)
        PreviewSplit.Panel1MinSize = 750
        ' 
        ' PreviewSplit.Panel2
        ' 
        PreviewSplit.Panel2.Controls.Add(PreviewSidebar)
        PreviewSplit.Size = New Size(1384, 651)
        PreviewSplit.SplitterDistance = 750
        PreviewSplit.TabIndex = 0
        ' 
        ' RootLayout
        ' 
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TabsFace, 0, 0)
        RootLayout.Controls.Add(BottomLayout, 0, 1)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 2
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(750, 651)
        RootLayout.TabIndex = 0
        ' 
        ' TabsFace
        ' 
        TabsFace.Controls.Add(TabPageFaceParts)
        TabsFace.Controls.Add(TabPageTints)
        TabsFace.Controls.Add(TabPageVertex)
        TabsFace.Controls.Add(TabPageBoneRegions)
        TabsFace.Dock = DockStyle.Fill
        TabsFace.Location = New Point(11, 11)
        TabsFace.Name = "TabsFace"
        TabsFace.SelectedIndex = 0
        TabsFace.Size = New Size(728, 588)
        TabsFace.TabIndex = 0
        ' 
        ' TabPageFaceParts
        ' 
        TabPageFaceParts.Controls.Add(FacePartsLayout)
        TabPageFaceParts.Location = New Point(4, 24)
        TabPageFaceParts.Name = "TabPageFaceParts"
        TabPageFaceParts.Padding = New Padding(6)
        TabPageFaceParts.Size = New Size(720, 560)
        TabPageFaceParts.TabIndex = 0
        TabPageFaceParts.Text = "Face Parts"
        ' 
        ' FacePartsLayout
        ' 
        FacePartsLayout.ColumnCount = 1
        FacePartsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FacePartsLayout.Controls.Add(GroupBoxHeadParts, 0, 0)
        FacePartsLayout.Controls.Add(GroupBoxHairColor, 0, 1)
        FacePartsLayout.Controls.Add(GroupBoxFaceFlags, 0, 2)
        FacePartsLayout.Dock = DockStyle.Fill
        FacePartsLayout.Location = New Point(6, 6)
        FacePartsLayout.Name = "FacePartsLayout"
        FacePartsLayout.RowCount = 3
        FacePartsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 60F))
        FacePartsLayout.RowStyles.Add(New RowStyle())
        FacePartsLayout.RowStyles.Add(New RowStyle())
        FacePartsLayout.Size = New Size(708, 548)
        FacePartsLayout.TabIndex = 0
        ' 
        ' GroupBoxHeadParts
        ' 
        GroupBoxHeadParts.Controls.Add(HeadPartsLayout)
        GroupBoxHeadParts.Dock = DockStyle.Fill
        GroupBoxHeadParts.Location = New Point(3, 3)
        GroupBoxHeadParts.Name = "GroupBoxHeadParts"
        GroupBoxHeadParts.Size = New Size(702, 374)
        GroupBoxHeadParts.TabIndex = 0
        GroupBoxHeadParts.TabStop = False
        GroupBoxHeadParts.Text = "Head Parts (NPC.PNAM — full reload on change)"
        ' 
        ' HeadPartsLayout
        ' 
        HeadPartsLayout.ColumnCount = 1
        HeadPartsLayout.ColumnStyles.Add(New ColumnStyle())
        HeadPartsLayout.Controls.Add(TableLayoutPanel1, 0, 1)
        HeadPartsLayout.Controls.Add(ListViewHeadParts, 0, 0)
        HeadPartsLayout.Dock = DockStyle.Fill
        HeadPartsLayout.Location = New Point(3, 19)
        HeadPartsLayout.Name = "HeadPartsLayout"
        HeadPartsLayout.Padding = New Padding(4)
        HeadPartsLayout.RowCount = 2
        HeadPartsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        HeadPartsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 56F))
        HeadPartsLayout.Size = New Size(696, 352)
        HeadPartsLayout.TabIndex = 0
        ' 
        ' TableLayoutPanel1
        ' 
        TableLayoutPanel1.ColumnCount = 6
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666679F))
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666641F))
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666641F))
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666641F))
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666641F))
        TableLayoutPanel1.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 16.6666641F))
        TableLayoutPanel1.Controls.Add(ButtonRemoveHeadPart, 5, 1)
        TableLayoutPanel1.Controls.Add(ButtonAddMisc, 3, 1)
        TableLayoutPanel1.Controls.Add(ButtonAddMeatcaps, 2, 1)
        TableLayoutPanel1.Controls.Add(ButtonAddHeadRear, 1, 1)
        TableLayoutPanel1.Controls.Add(ButtonAddTeeth, 0, 1)
        TableLayoutPanel1.Controls.Add(ButtonAddScar, 5, 0)
        TableLayoutPanel1.Controls.Add(ButtonAddEyebrows, 4, 0)
        TableLayoutPanel1.Controls.Add(ButtonAddFacialHair, 3, 0)
        TableLayoutPanel1.Controls.Add(ButtonAddEyes, 2, 0)
        TableLayoutPanel1.Controls.Add(ButtonAddHair, 1, 0)
        TableLayoutPanel1.Controls.Add(ButtonAddFace, 0, 0)
        TableLayoutPanel1.Dock = DockStyle.Fill
        TableLayoutPanel1.Location = New Point(7, 295)
        TableLayoutPanel1.Name = "TableLayoutPanel1"
        TableLayoutPanel1.RowCount = 2
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 25F))
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 25F))
        TableLayoutPanel1.Size = New Size(682, 50)
        TableLayoutPanel1.TabIndex = 11
        ' 
        ' ButtonRemoveHeadPart
        ' 
        ButtonRemoveHeadPart.AutoSize = True
        ButtonRemoveHeadPart.Dock = DockStyle.Fill
        ButtonRemoveHeadPart.Location = New Point(565, 25)
        ButtonRemoveHeadPart.Margin = New Padding(0)
        ButtonRemoveHeadPart.Name = "ButtonRemoveHeadPart"
        ButtonRemoveHeadPart.Size = New Size(117, 25)
        ButtonRemoveHeadPart.TabIndex = 10
        ButtonRemoveHeadPart.Text = "-Remove"
        ' 
        ' ButtonAddMisc
        ' 
        ButtonAddMisc.AutoSize = True
        ButtonAddMisc.Dock = DockStyle.Fill
        ButtonAddMisc.Location = New Point(339, 25)
        ButtonAddMisc.Margin = New Padding(0)
        ButtonAddMisc.Name = "ButtonAddMisc"
        ButtonAddMisc.Size = New Size(113, 25)
        ButtonAddMisc.TabIndex = 9
        ButtonAddMisc.Text = "+Misc"
        ' 
        ' ButtonAddMeatcaps
        ' 
        ButtonAddMeatcaps.AutoSize = True
        ButtonAddMeatcaps.Dock = DockStyle.Fill
        ButtonAddMeatcaps.Location = New Point(226, 25)
        ButtonAddMeatcaps.Margin = New Padding(0)
        ButtonAddMeatcaps.Name = "ButtonAddMeatcaps"
        ButtonAddMeatcaps.Size = New Size(113, 25)
        ButtonAddMeatcaps.TabIndex = 8
        ButtonAddMeatcaps.Text = "+Meatcaps"
        ' 
        ' ButtonAddHeadRear
        ' 
        ButtonAddHeadRear.AutoSize = True
        ButtonAddHeadRear.Dock = DockStyle.Fill
        ButtonAddHeadRear.Location = New Point(113, 25)
        ButtonAddHeadRear.Margin = New Padding(0)
        ButtonAddHeadRear.Name = "ButtonAddHeadRear"
        ButtonAddHeadRear.Size = New Size(113, 25)
        ButtonAddHeadRear.TabIndex = 7
        ButtonAddHeadRear.Text = "+Head Rear"
        ' 
        ' ButtonAddTeeth
        ' 
        ButtonAddTeeth.AutoSize = True
        ButtonAddTeeth.Dock = DockStyle.Fill
        ButtonAddTeeth.Location = New Point(0, 25)
        ButtonAddTeeth.Margin = New Padding(0)
        ButtonAddTeeth.Name = "ButtonAddTeeth"
        ButtonAddTeeth.Size = New Size(113, 25)
        ButtonAddTeeth.TabIndex = 6
        ButtonAddTeeth.Text = "+Teeth"
        ' 
        ' ButtonAddScar
        ' 
        ButtonAddScar.AutoSize = True
        ButtonAddScar.Dock = DockStyle.Fill
        ButtonAddScar.Location = New Point(565, 0)
        ButtonAddScar.Margin = New Padding(0)
        ButtonAddScar.Name = "ButtonAddScar"
        ButtonAddScar.Size = New Size(117, 25)
        ButtonAddScar.TabIndex = 5
        ButtonAddScar.Text = "+Scar"
        ' 
        ' ButtonAddEyebrows
        ' 
        ButtonAddEyebrows.AutoSize = True
        ButtonAddEyebrows.Dock = DockStyle.Fill
        ButtonAddEyebrows.Location = New Point(452, 0)
        ButtonAddEyebrows.Margin = New Padding(0)
        ButtonAddEyebrows.Name = "ButtonAddEyebrows"
        ButtonAddEyebrows.Size = New Size(113, 25)
        ButtonAddEyebrows.TabIndex = 4
        ButtonAddEyebrows.Text = "+Eyebrows"
        ' 
        ' ButtonAddFacialHair
        ' 
        ButtonAddFacialHair.AutoSize = True
        ButtonAddFacialHair.Dock = DockStyle.Fill
        ButtonAddFacialHair.Location = New Point(339, 0)
        ButtonAddFacialHair.Margin = New Padding(0)
        ButtonAddFacialHair.Name = "ButtonAddFacialHair"
        ButtonAddFacialHair.Size = New Size(113, 25)
        ButtonAddFacialHair.TabIndex = 3
        ButtonAddFacialHair.Text = "+Facial Hair"
        ' 
        ' ButtonAddEyes
        ' 
        ButtonAddEyes.AutoSize = True
        ButtonAddEyes.Dock = DockStyle.Fill
        ButtonAddEyes.Location = New Point(226, 0)
        ButtonAddEyes.Margin = New Padding(0)
        ButtonAddEyes.Name = "ButtonAddEyes"
        ButtonAddEyes.Size = New Size(113, 25)
        ButtonAddEyes.TabIndex = 2
        ButtonAddEyes.Text = "+Eyes"
        ' 
        ' ButtonAddHair
        ' 
        ButtonAddHair.AutoSize = True
        ButtonAddHair.Dock = DockStyle.Fill
        ButtonAddHair.Location = New Point(113, 0)
        ButtonAddHair.Margin = New Padding(0)
        ButtonAddHair.Name = "ButtonAddHair"
        ButtonAddHair.Size = New Size(113, 25)
        ButtonAddHair.TabIndex = 1
        ButtonAddHair.Text = "+Hair"
        ' 
        ' ButtonAddFace
        ' 
        ButtonAddFace.AutoSize = True
        ButtonAddFace.Dock = DockStyle.Fill
        ButtonAddFace.Location = New Point(0, 0)
        ButtonAddFace.Margin = New Padding(0)
        ButtonAddFace.Name = "ButtonAddFace"
        ButtonAddFace.Size = New Size(113, 25)
        ButtonAddFace.TabIndex = 0
        ButtonAddFace.Text = "+Face"
        ' 
        ' ListViewHeadParts
        ' 
        ListViewHeadParts.Columns.AddRange(New ColumnHeader() {ColHeadPartType, ColHeadPartEditorID, ColHeadPartName, ColHeadPartPlugin, ColHeadPartFormID})
        ListViewHeadParts.Dock = DockStyle.Fill
        ListViewHeadParts.FullRowSelect = True
        ListViewHeadParts.Location = New Point(7, 7)
        ListViewHeadParts.MultiSelect = False
        ListViewHeadParts.Name = "ListViewHeadParts"
        ListViewHeadParts.Size = New Size(682, 282)
        ListViewHeadParts.TabIndex = 0
        ListViewHeadParts.UseCompatibleStateImageBehavior = False
        ListViewHeadParts.View = View.Details
        ' 
        ' ColHeadPartType
        ' 
        ColHeadPartType.Text = "Type"
        ColHeadPartType.Width = 90
        ' 
        ' ColHeadPartEditorID
        ' 
        ColHeadPartEditorID.Text = "Editor ID"
        ColHeadPartEditorID.Width = 200
        ' 
        ' ColHeadPartName
        ' 
        ColHeadPartName.Text = "Name"
        ColHeadPartName.Width = 180
        ' 
        ' ColHeadPartPlugin
        ' 
        ColHeadPartPlugin.Text = "Plugin"
        ColHeadPartPlugin.Width = 110
        ' 
        ' ColHeadPartFormID
        ' 
        ColHeadPartFormID.Text = "FormID"
        ColHeadPartFormID.Width = 70
        ' 
        ' GroupBoxHairColor
        ' 
        GroupBoxHairColor.AutoSize = True
        GroupBoxHairColor.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxHairColor.Controls.Add(HairColorLayout)
        GroupBoxHairColor.Dock = DockStyle.Top
        GroupBoxHairColor.Location = New Point(3, 383)
        GroupBoxHairColor.Name = "GroupBoxHairColor"
        GroupBoxHairColor.Size = New Size(702, 86)
        GroupBoxHairColor.TabIndex = 1
        GroupBoxHairColor.TabStop = False
        GroupBoxHairColor.Text = "Hair Color (NPC.QNAM)"
        ' 
        ' HairColorLayout
        ' 
        HairColorLayout.AutoSize = True
        HairColorLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        HairColorLayout.ColumnCount = 2
        HairColorLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        HairColorLayout.ColumnStyles.Add(New ColumnStyle())
        HairColorLayout.Controls.Add(ComboBoxHairColor, 0, 0)
        HairColorLayout.Controls.Add(ButtonClearHairColor, 1, 0)
        HairColorLayout.Controls.Add(PanelHairColorSwatch, 0, 1)
        HairColorLayout.Dock = DockStyle.Fill
        HairColorLayout.Location = New Point(3, 19)
        HairColorLayout.Name = "HairColorLayout"
        HairColorLayout.Padding = New Padding(4)
        HairColorLayout.RowCount = 2
        HairColorLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36F))
        HairColorLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 20F))
        HairColorLayout.Size = New Size(696, 64)
        HairColorLayout.TabIndex = 0
        ' 
        ' ComboBoxHairColor
        ' 
        ComboBoxHairColor.Dock = DockStyle.Fill
        ComboBoxHairColor.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxHairColor.Location = New Point(6, 6)
        ComboBoxHairColor.Margin = New Padding(2)
        ComboBoxHairColor.Name = "ComboBoxHairColor"
        ComboBoxHairColor.Size = New Size(584, 23)
        ComboBoxHairColor.TabIndex = 0
        ' 
        ' ButtonClearHairColor
        ' 
        ButtonClearHairColor.Dock = DockStyle.Fill
        ButtonClearHairColor.Location = New Point(594, 6)
        ButtonClearHairColor.Margin = New Padding(2)
        ButtonClearHairColor.MinimumSize = New Size(96, 0)
        ButtonClearHairColor.Name = "ButtonClearHairColor"
        HairColorLayout.SetRowSpan(ButtonClearHairColor, 2)
        ButtonClearHairColor.Size = New Size(96, 52)
        ButtonClearHairColor.TabIndex = 1
        ButtonClearHairColor.Text = "Clear"
        ' 
        ' PanelHairColorSwatch
        ' 
        PanelHairColorSwatch.BackColor = Color.Gray
        PanelHairColorSwatch.BorderStyle = BorderStyle.FixedSingle
        PanelHairColorSwatch.Dock = DockStyle.Fill
        PanelHairColorSwatch.Location = New Point(6, 42)
        PanelHairColorSwatch.Margin = New Padding(2)
        PanelHairColorSwatch.Name = "PanelHairColorSwatch"
        PanelHairColorSwatch.Size = New Size(584, 16)
        PanelHairColorSwatch.TabIndex = 2
        ' 
        ' GroupBoxFaceFlags
        ' 
        GroupBoxFaceFlags.AutoSize = True
        GroupBoxFaceFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxFaceFlags.Controls.Add(FaceFlagsLayout)
        GroupBoxFaceFlags.Dock = DockStyle.Top
        GroupBoxFaceFlags.Location = New Point(3, 475)
        GroupBoxFaceFlags.Name = "GroupBoxFaceFlags"
        GroupBoxFaceFlags.Size = New Size(702, 70)
        GroupBoxFaceFlags.TabIndex = 2
        GroupBoxFaceFlags.TabStop = False
        GroupBoxFaceFlags.Text = "FaceGen flags (ACBS)"
        ' 
        ' FaceFlagsLayout
        ' 
        FaceFlagsLayout.AutoSize = True
        FaceFlagsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FaceFlagsLayout.Controls.Add(CheckBoxIsCharGenFacePreset)
        FaceFlagsLayout.Controls.Add(LabelCharGenHelp)
        FaceFlagsLayout.Dock = DockStyle.Fill
        FaceFlagsLayout.FlowDirection = FlowDirection.TopDown
        FaceFlagsLayout.Location = New Point(3, 19)
        FaceFlagsLayout.Name = "FaceFlagsLayout"
        FaceFlagsLayout.Padding = New Padding(4)
        FaceFlagsLayout.Size = New Size(696, 48)
        FaceFlagsLayout.TabIndex = 0
        FaceFlagsLayout.WrapContents = False
        ' 
        ' CheckBoxIsCharGenFacePreset
        ' 
        CheckBoxIsCharGenFacePreset.AutoSize = True
        CheckBoxIsCharGenFacePreset.Location = New Point(7, 7)
        CheckBoxIsCharGenFacePreset.Name = "CheckBoxIsCharGenFacePreset"
        CheckBoxIsCharGenFacePreset.Size = New Size(145, 19)
        CheckBoxIsCharGenFacePreset.TabIndex = 0
        CheckBoxIsCharGenFacePreset.Text = "Is CharGen Face Preset"
        ' 
        ' LabelCharGenHelp
        ' 
        LabelCharGenHelp.AutoSize = True
        LabelCharGenHelp.ForeColor = SystemColors.GrayText
        LabelCharGenHelp.Location = New Point(7, 29)
        LabelCharGenHelp.MaximumSize = New Size(640, 0)
        LabelCharGenHelp.Name = "LabelCharGenHelp"
        LabelCharGenHelp.Size = New Size(602, 15)
        LabelCharGenHelp.TabIndex = 1
        LabelCharGenHelp.Text = "Marks the NPC as a chargen template.  The engine will remorph every time. Recommended false + build chargen"
        ' 
        ' TabPageTints
        ' 
        TabPageTints.Controls.Add(TintsLayout)
        TabPageTints.Location = New Point(4, 24)
        TabPageTints.Name = "TabPageTints"
        TabPageTints.Padding = New Padding(6)
        TabPageTints.Size = New Size(720, 560)
        TabPageTints.TabIndex = 1
        TabPageTints.Text = "Face Tints"
        ' 
        ' TintsLayout
        ' 
        TintsLayout.ColumnCount = 1
        TintsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        TintsLayout.Controls.Add(TextBoxTintFilter, 0, 0)
        TintsLayout.Controls.Add(ListViewTints, 0, 1)
        TintsLayout.Controls.Add(TintsButtonRow, 0, 2)
        TintsLayout.Controls.Add(PanelTintDetail, 0, 3)
        TintsLayout.Dock = DockStyle.Fill
        TintsLayout.Location = New Point(6, 6)
        TintsLayout.Name = "TintsLayout"
        TintsLayout.Padding = New Padding(4)
        TintsLayout.RowCount = 4
        TintsLayout.RowStyles.Add(New RowStyle())
        TintsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        TintsLayout.RowStyles.Add(New RowStyle())
        TintsLayout.RowStyles.Add(New RowStyle())
        TintsLayout.Size = New Size(708, 548)
        TintsLayout.TabIndex = 0
        ' 
        ' TextBoxTintFilter
        ' 
        TextBoxTintFilter.Dock = DockStyle.Fill
        TextBoxTintFilter.Location = New Point(7, 7)
        TextBoxTintFilter.Name = "TextBoxTintFilter"
        TextBoxTintFilter.PlaceholderText = "Filter by group or layer name…"
        TextBoxTintFilter.Size = New Size(694, 23)
        TextBoxTintFilter.TabIndex = 0
        ' 
        ' ListViewTints
        ' 
        ListViewTints.Columns.AddRange(New ColumnHeader() {ColumnTintGroup, ColumnTintSlot, ColumnTintLayer, ColumnTintColor, ColumnTintPercent})
        ListViewTints.Dock = DockStyle.Fill
        ListViewTints.FullRowSelect = True
        ListViewTints.Location = New Point(7, 36)
        ListViewTints.MultiSelect = False
        ListViewTints.Name = "ListViewTints"
        ListViewTints.Size = New Size(694, 304)
        ListViewTints.TabIndex = 0
        ListViewTints.UseCompatibleStateImageBehavior = False
        ListViewTints.View = View.Details
        ' 
        ' ColumnTintGroup
        ' 
        ColumnTintGroup.Text = "Group"
        ColumnTintGroup.Width = 110
        ' 
        ' ColumnTintSlot
        ' 
        ColumnTintSlot.Text = "Slot"
        ColumnTintSlot.Width = 100
        ' 
        ' ColumnTintLayer
        ' 
        ColumnTintLayer.Text = "Layer"
        ColumnTintLayer.Width = 280
        ' 
        ' ColumnTintColor
        ' 
        ColumnTintColor.Text = "Color"
        ColumnTintColor.Width = 100
        ' 
        ' ColumnTintPercent
        ' 
        ColumnTintPercent.Text = "%"
        ColumnTintPercent.Width = 50
        ' 
        ' TintsButtonRow
        ' 
        TintsButtonRow.AutoSize = True
        TintsButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TintsButtonRow.Controls.Add(ButtonAddTint)
        TintsButtonRow.Controls.Add(ButtonRemoveTint)
        TintsButtonRow.Controls.Add(ButtonRemoveAllInCategory)
        TintsButtonRow.Controls.Add(ButtonRemoveZeroedTints)
        TintsButtonRow.Dock = DockStyle.Fill
        TintsButtonRow.Location = New Point(7, 346)
        TintsButtonRow.Name = "TintsButtonRow"
        TintsButtonRow.Size = New Size(694, 31)
        TintsButtonRow.TabIndex = 1
        ' 
        ' ButtonAddTint
        ' 
        ButtonAddTint.AutoSize = True
        ButtonAddTint.Location = New Point(3, 3)
        ButtonAddTint.Name = "ButtonAddTint"
        ButtonAddTint.Size = New Size(75, 25)
        ButtonAddTint.TabIndex = 0
        ButtonAddTint.Text = "Add…"
        ' 
        ' ButtonRemoveTint
        ' 
        ButtonRemoveTint.AutoSize = True
        ButtonRemoveTint.Location = New Point(84, 3)
        ButtonRemoveTint.Name = "ButtonRemoveTint"
        ButtonRemoveTint.Size = New Size(75, 25)
        ButtonRemoveTint.TabIndex = 1
        ButtonRemoveTint.Text = "Remove"
        '
        ' ButtonRemoveAllInCategory
        '
        ButtonRemoveAllInCategory.AutoSize = True
        ButtonRemoveAllInCategory.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRemoveAllInCategory.Location = New Point(165, 3)
        ButtonRemoveAllInCategory.Name = "ButtonRemoveAllInCategory"
        ButtonRemoveAllInCategory.Size = New Size(125, 25)
        ButtonRemoveAllInCategory.TabIndex = 2
        ButtonRemoveAllInCategory.Text = "Remove all category"
        '
        ' ButtonRemoveZeroedTints
        '
        ButtonRemoveZeroedTints.AutoSize = True
        ButtonRemoveZeroedTints.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRemoveZeroedTints.Location = New Point(296, 3)
        ButtonRemoveZeroedTints.Name = "ButtonRemoveZeroedTints"
        ButtonRemoveZeroedTints.Size = New Size(113, 25)
        ButtonRemoveZeroedTints.TabIndex = 3
        ButtonRemoveZeroedTints.Text = "Remove all zeroed"
        ' 
        ' PanelTintDetail
        ' 
        PanelTintDetail.AutoSize = True
        PanelTintDetail.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelTintDetail.Controls.Add(TintDetailLayout)
        PanelTintDetail.Dock = DockStyle.Fill
        PanelTintDetail.Location = New Point(7, 383)
        PanelTintDetail.Name = "PanelTintDetail"
        PanelTintDetail.Size = New Size(694, 158)
        PanelTintDetail.TabIndex = 2
        PanelTintDetail.TabStop = False
        PanelTintDetail.Text = "Selected layer"
        ' 
        ' TintDetailLayout
        ' 
        TintDetailLayout.AutoSize = True
        TintDetailLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TintDetailLayout.ColumnCount = 3
        TintDetailLayout.ColumnStyles.Add(New ColumnStyle())
        TintDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        TintDetailLayout.ColumnStyles.Add(New ColumnStyle())
        TintDetailLayout.Controls.Add(LabelTintLayerCaption, 0, 0)
        TintDetailLayout.Controls.Add(LabelTintLayerName, 1, 0)
        TintDetailLayout.Controls.Add(LabelTintPaletteCaption, 0, 1)
        TintDetailLayout.Controls.Add(ComboBoxTintPalette, 1, 1)
        TintDetailLayout.Controls.Add(ButtonTintCustomRGB, 2, 1)
        TintDetailLayout.Controls.Add(PanelTintColorSwatch, 1, 2)
        TintDetailLayout.Controls.Add(LabelTintPercentCaption, 0, 3)
        TintDetailLayout.Controls.Add(TrackBarTintPercent, 1, 3)
        TintDetailLayout.Dock = DockStyle.Fill
        TintDetailLayout.Location = New Point(3, 19)
        TintDetailLayout.Name = "TintDetailLayout"
        TintDetailLayout.Padding = New Padding(4)
        TintDetailLayout.RowCount = 4
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 20F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36F))
        TintDetailLayout.Size = New Size(688, 136)
        TintDetailLayout.TabIndex = 0
        ' 
        ' LabelTintLayerCaption
        ' 
        LabelTintLayerCaption.AutoSize = True
        LabelTintLayerCaption.Location = New Point(7, 4)
        LabelTintLayerCaption.Name = "LabelTintLayerCaption"
        LabelTintLayerCaption.Size = New Size(38, 15)
        LabelTintLayerCaption.TabIndex = 0
        LabelTintLayerCaption.Text = "Layer:"
        ' 
        ' LabelTintLayerName
        ' 
        LabelTintLayerName.AutoSize = True
        TintDetailLayout.SetColumnSpan(LabelTintLayerName, 2)
        LabelTintLayerName.Font = New Font("Segoe UI", 9F, FontStyle.Bold)
        LabelTintLayerName.Location = New Point(68, 4)
        LabelTintLayerName.Name = "LabelTintLayerName"
        LabelTintLayerName.Size = New Size(43, 15)
        LabelTintLayerName.TabIndex = 1
        LabelTintLayerName.Text = "(none)"
        ' 
        ' LabelTintPaletteCaption
        ' 
        LabelTintPaletteCaption.AutoSize = True
        LabelTintPaletteCaption.Location = New Point(7, 40)
        LabelTintPaletteCaption.Name = "LabelTintPaletteCaption"
        LabelTintPaletteCaption.Size = New Size(39, 15)
        LabelTintPaletteCaption.TabIndex = 2
        LabelTintPaletteCaption.Text = "Color:"
        ' 
        ' ComboBoxTintPalette
        ' 
        ComboBoxTintPalette.Dock = DockStyle.Fill
        ComboBoxTintPalette.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxTintPalette.Location = New Point(67, 42)
        ComboBoxTintPalette.Margin = New Padding(2)
        ComboBoxTintPalette.Name = "ComboBoxTintPalette"
        ComboBoxTintPalette.Size = New Size(513, 23)
        ComboBoxTintPalette.TabIndex = 3
        ' 
        ' ButtonTintCustomRGB
        ' 
        ButtonTintCustomRGB.Dock = DockStyle.Fill
        ButtonTintCustomRGB.Location = New Point(585, 43)
        ButtonTintCustomRGB.MinimumSize = New Size(96, 0)
        ButtonTintCustomRGB.Name = "ButtonTintCustomRGB"
        TintDetailLayout.SetRowSpan(ButtonTintCustomRGB, 2)
        ButtonTintCustomRGB.Size = New Size(96, 50)
        ButtonTintCustomRGB.TabIndex = 4
        ButtonTintCustomRGB.Text = "Custom RGB…"
        ' 
        ' PanelTintColorSwatch
        ' 
        PanelTintColorSwatch.BackColor = Color.White
        PanelTintColorSwatch.BorderStyle = BorderStyle.FixedSingle
        PanelTintColorSwatch.Dock = DockStyle.Fill
        PanelTintColorSwatch.Location = New Point(67, 78)
        PanelTintColorSwatch.Margin = New Padding(2)
        PanelTintColorSwatch.Name = "PanelTintColorSwatch"
        PanelTintColorSwatch.Size = New Size(513, 16)
        PanelTintColorSwatch.TabIndex = 5
        ' 
        ' LabelTintPercentCaption
        ' 
        LabelTintPercentCaption.AutoSize = True
        LabelTintPercentCaption.Location = New Point(7, 96)
        LabelTintPercentCaption.Name = "LabelTintPercentCaption"
        LabelTintPercentCaption.Size = New Size(55, 15)
        LabelTintPercentCaption.TabIndex = 6
        LabelTintPercentCaption.Text = "Intensity:"
        ' 
        ' TrackBarTintPercent
        ' 
        TrackBarTintPercent.AccentColor = SystemColors.HotTrack
        TrackBarTintPercent.AllowExtremeValues = True
        TrackBarTintPercent.BackColor = SystemColors.Control
        TrackBarTintPercent.DisplayFormat = "0\%"
        TrackBarTintPercent.Dock = DockStyle.Fill
        TrackBarTintPercent.Location = New Point(68, 99)
        TrackBarTintPercent.MinimumSize = New Size(100, 24)
        TrackBarTintPercent.Name = "TrackBarTintPercent"
        TrackBarTintPercent.Size = New Size(511, 30)
        TrackBarTintPercent.TabIndex = 7
        TrackBarTintPercent.TextBoxTextAlign = HorizontalAlignment.Right
        TrackBarTintPercent.ThumbColor = SystemColors.HotTrack
        TrackBarTintPercent.ThumbRadius = 4F
        TrackBarTintPercent.TrackColor = SystemColors.ControlDark
        ' 
        ' TabPageVertex
        ' 
        TabPageVertex.Controls.Add(VertexMorphsPanel)
        TabPageVertex.Location = New Point(4, 24)
        TabPageVertex.Name = "TabPageVertex"
        TabPageVertex.Padding = New Padding(6)
        TabPageVertex.Size = New Size(720, 560)
        TabPageVertex.TabIndex = 2
        TabPageVertex.Text = "Vertex Morphs"
        ' 
        ' VertexMorphsPanel
        ' 
        VertexMorphsPanel.Dock = DockStyle.Fill
        VertexMorphsPanel.Location = New Point(6, 6)
        VertexMorphsPanel.Name = "VertexMorphsPanel"
        VertexMorphsPanel.Size = New Size(708, 548)
        VertexMorphsPanel.TabIndex = 0
        ' 
        ' TabPageBoneRegions
        ' 
        TabPageBoneRegions.Controls.Add(BoneRegionsRoot)
        TabPageBoneRegions.Location = New Point(4, 24)
        TabPageBoneRegions.Name = "TabPageBoneRegions"
        TabPageBoneRegions.Padding = New Padding(6)
        TabPageBoneRegions.Size = New Size(720, 560)
        TabPageBoneRegions.TabIndex = 3
        TabPageBoneRegions.Text = "Bone Regions"
        ' 
        ' BoneRegionsRoot
        ' 
        BoneRegionsRoot.ColumnCount = 1
        BoneRegionsRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BoneRegionsRoot.Controls.Add(BoneRegionsContainer, 0, 0)
        BoneRegionsRoot.Controls.Add(GroupBoxFmin, 0, 1)
        BoneRegionsRoot.Dock = DockStyle.Fill
        BoneRegionsRoot.Location = New Point(6, 6)
        BoneRegionsRoot.Name = "BoneRegionsRoot"
        BoneRegionsRoot.RowCount = 2
        BoneRegionsRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BoneRegionsRoot.RowStyles.Add(New RowStyle())
        BoneRegionsRoot.Size = New Size(708, 548)
        BoneRegionsRoot.TabIndex = 0
        ' 
        ' BoneRegionsContainer
        ' 
        BoneRegionsContainer.Dock = DockStyle.Fill
        BoneRegionsContainer.Location = New Point(3, 3)
        BoneRegionsContainer.Name = "BoneRegionsContainer"
        BoneRegionsContainer.Size = New Size(702, 476)
        BoneRegionsContainer.TabIndex = 0
        ' 
        ' GroupBoxFmin
        ' 
        GroupBoxFmin.AutoSize = True
        GroupBoxFmin.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxFmin.Controls.Add(FminLayout)
        GroupBoxFmin.Dock = DockStyle.Fill
        GroupBoxFmin.Location = New Point(3, 485)
        GroupBoxFmin.Name = "GroupBoxFmin"
        GroupBoxFmin.Size = New Size(702, 60)
        GroupBoxFmin.TabIndex = 1
        GroupBoxFmin.TabStop = False
        GroupBoxFmin.Text = "Facial Morph Intensity (NPC.FMIN — multiplier on FMRS deltas)"
        ' 
        ' FminLayout
        ' 
        FminLayout.AutoSize = True
        FminLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FminLayout.ColumnCount = 3
        FminLayout.ColumnStyles.Add(New ColumnStyle())
        FminLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FminLayout.ColumnStyles.Add(New ColumnStyle())
        FminLayout.Controls.Add(LabelFminCaption, 0, 0)
        FminLayout.Controls.Add(TrackBarFmin, 1, 0)
        FminLayout.Dock = DockStyle.Fill
        FminLayout.Location = New Point(3, 19)
        FminLayout.Name = "FminLayout"
        FminLayout.Padding = New Padding(4)
        FminLayout.RowCount = 1
        FminLayout.RowStyles.Add(New RowStyle())
        FminLayout.Size = New Size(696, 38)
        FminLayout.TabIndex = 0
        ' 
        ' LabelFminCaption
        ' 
        LabelFminCaption.AutoSize = True
        LabelFminCaption.Location = New Point(7, 4)
        LabelFminCaption.Name = "LabelFminCaption"
        LabelFminCaption.Size = New Size(84, 15)
        LabelFminCaption.TabIndex = 0
        LabelFminCaption.Text = "Intensity (0..4):"
        LabelFminCaption.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' TrackBarFmin
        ' 
        TrackBarFmin.AccentColor = SystemColors.HotTrack
        TrackBarFmin.BackColor = SystemColors.Control
        TrackBarFmin.DisplayFormat = "0.00%"
        TrackBarFmin.Dock = DockStyle.Fill
        TrackBarFmin.InputScale = 0.01R
        TrackBarFmin.LargeChange = 0.25R
        TrackBarFmin.Location = New Point(97, 7)
        TrackBarFmin.Maximum = 4R
        TrackBarFmin.MinimumSize = New Size(100, 24)
        TrackBarFmin.Name = "TrackBarFmin"
        TrackBarFmin.Size = New Size(592, 24)
        TrackBarFmin.SmallChange = 0.01R
        TrackBarFmin.TabIndex = 1
        TrackBarFmin.TextBoxTextAlign = HorizontalAlignment.Right
        TrackBarFmin.ThumbColor = SystemColors.HotTrack
        TrackBarFmin.ThumbRadius = 4F
        TrackBarFmin.TrackColor = SystemColors.ControlDark
        ' 
        ' BottomLayout
        ' 
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonResetSection)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 605)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(728, 35)
        BottomLayout.TabIndex = 1
        ' 
        ' ButtonOk
        ' 
        ButtonOk.Location = New Point(645, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Location = New Point(559, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' ButtonResetSection
        ' 
        ButtonResetSection.Location = New Point(443, 9)
        ButtonResetSection.Name = "ButtonResetSection"
        ButtonResetSection.Size = New Size(110, 23)
        ButtonResetSection.TabIndex = 2
        ButtonResetSection.Text = "Reset section"
        ' 
        ' PreviewSidebar
        ' 
        PreviewSidebar.ColumnCount = 1
        PreviewSidebar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PreviewSidebar.Controls.Add(RenderTogglesPanel, 0, 0)
        PreviewSidebar.Controls.Add(PreviewHostPanel, 0, 1)
        PreviewSidebar.Dock = DockStyle.Fill
        PreviewSidebar.Location = New Point(0, 0)
        PreviewSidebar.Name = "PreviewSidebar"
        PreviewSidebar.RowCount = 2
        PreviewSidebar.RowStyles.Add(New RowStyle())
        PreviewSidebar.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PreviewSidebar.Size = New Size(630, 651)
        PreviewSidebar.TabIndex = 0
        ' 
        ' RenderTogglesPanel
        ' 
        RenderTogglesPanel.AutoSize = True
        RenderTogglesPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RenderTogglesPanel.Controls.Add(CheckBoxRenderGore)
        RenderTogglesPanel.Dock = DockStyle.Fill
        RenderTogglesPanel.Location = New Point(3, 3)
        RenderTogglesPanel.Name = "RenderTogglesPanel"
        RenderTogglesPanel.Padding = New Padding(2)
        RenderTogglesPanel.Size = New Size(624, 27)
        RenderTogglesPanel.TabIndex = 0
        ' 
        ' CheckBoxRenderGore
        ' 
        CheckBoxRenderGore.AutoSize = True
        CheckBoxRenderGore.Location = New Point(6, 4)
        CheckBoxRenderGore.Margin = New Padding(4, 2, 8, 2)
        CheckBoxRenderGore.Name = "CheckBoxRenderGore"
        CheckBoxRenderGore.Size = New Size(90, 19)
        CheckBoxRenderGore.TabIndex = 0
        CheckBoxRenderGore.Text = "Render gore"
        ' 
        ' PreviewHostPanel
        ' 
        PreviewHostPanel.Dock = DockStyle.Fill
        PreviewHostPanel.Location = New Point(3, 36)
        PreviewHostPanel.Name = "PreviewHostPanel"
        PreviewHostPanel.Size = New Size(624, 612)
        PreviewHostPanel.TabIndex = 0
        ' 
        ' EditFace_Form
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(1384, 651)
        Controls.Add(PreviewSplit)
        MinimumSize = New Size(1200, 690)
        Name = "EditFace_Form"
        StartPosition = FormStartPosition.CenterScreen
        Text = "Edit Face"
        PreviewSplit.Panel1.ResumeLayout(False)
        PreviewSplit.Panel2.ResumeLayout(False)
        CType(PreviewSplit, ComponentModel.ISupportInitialize).EndInit()
        PreviewSplit.ResumeLayout(False)
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TabsFace.ResumeLayout(False)
        TabPageFaceParts.ResumeLayout(False)
        FacePartsLayout.ResumeLayout(False)
        FacePartsLayout.PerformLayout()
        GroupBoxHeadParts.ResumeLayout(False)
        HeadPartsLayout.ResumeLayout(False)
        TableLayoutPanel1.ResumeLayout(False)
        TableLayoutPanel1.PerformLayout()
        GroupBoxHairColor.ResumeLayout(False)
        GroupBoxHairColor.PerformLayout()
        HairColorLayout.ResumeLayout(False)
        GroupBoxFaceFlags.ResumeLayout(False)
        GroupBoxFaceFlags.PerformLayout()
        FaceFlagsLayout.ResumeLayout(False)
        FaceFlagsLayout.PerformLayout()
        TabPageTints.ResumeLayout(False)
        TintsLayout.ResumeLayout(False)
        TintsLayout.PerformLayout()
        TintsButtonRow.ResumeLayout(False)
        TintsButtonRow.PerformLayout()
        PanelTintDetail.ResumeLayout(False)
        PanelTintDetail.PerformLayout()
        TintDetailLayout.ResumeLayout(False)
        TintDetailLayout.PerformLayout()
        TabPageVertex.ResumeLayout(False)
        TabPageBoneRegions.ResumeLayout(False)
        BoneRegionsRoot.ResumeLayout(False)
        BoneRegionsRoot.PerformLayout()
        GroupBoxFmin.ResumeLayout(False)
        GroupBoxFmin.PerformLayout()
        FminLayout.ResumeLayout(False)
        FminLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        PreviewSidebar.ResumeLayout(False)
        PreviewSidebar.PerformLayout()
        RenderTogglesPanel.ResumeLayout(False)
        RenderTogglesPanel.PerformLayout()
        ResumeLayout(False)
    End Sub

    Friend WithEvents PreviewSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents PreviewSidebar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents RenderTogglesPanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckBoxRenderGore As System.Windows.Forms.CheckBox
    Friend WithEvents PreviewHostPanel As System.Windows.Forms.Panel
    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TabsFace As System.Windows.Forms.TabControl
    Friend WithEvents TabPageFaceParts As System.Windows.Forms.TabPage
    Friend WithEvents TabPageTints As System.Windows.Forms.TabPage
    Friend WithEvents TabPageVertex As System.Windows.Forms.TabPage
    Friend WithEvents TabPageBoneRegions As System.Windows.Forms.TabPage
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonResetSection As System.Windows.Forms.Button

    Friend WithEvents FacePartsLayout As System.Windows.Forms.TableLayoutPanel

    Friend WithEvents GroupBoxHeadParts As System.Windows.Forms.GroupBox
    Friend WithEvents HeadPartsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListViewHeadParts As System.Windows.Forms.ListView
    Friend WithEvents ColHeadPartType As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColHeadPartEditorID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColHeadPartName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColHeadPartPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColHeadPartFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ButtonAddFace As System.Windows.Forms.Button
    Friend WithEvents ButtonAddHair As System.Windows.Forms.Button
    Friend WithEvents ButtonAddEyes As System.Windows.Forms.Button
    Friend WithEvents ButtonAddFacialHair As System.Windows.Forms.Button
    Friend WithEvents ButtonAddEyebrows As System.Windows.Forms.Button
    Friend WithEvents ButtonAddScar As System.Windows.Forms.Button
    Friend WithEvents ButtonAddTeeth As System.Windows.Forms.Button
    Friend WithEvents ButtonAddHeadRear As System.Windows.Forms.Button
    Friend WithEvents ButtonAddMeatcaps As System.Windows.Forms.Button
    Friend WithEvents ButtonAddMisc As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveHeadPart As System.Windows.Forms.Button

    Friend WithEvents GroupBoxHairColor As System.Windows.Forms.GroupBox
    Friend WithEvents HairColorLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ComboBoxHairColor As System.Windows.Forms.ComboBox
    Friend WithEvents PanelHairColorSwatch As System.Windows.Forms.Panel
    Friend WithEvents ButtonClearHairColor As System.Windows.Forms.Button

    Friend WithEvents GroupBoxFaceFlags As System.Windows.Forms.GroupBox
    Friend WithEvents FaceFlagsLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckBoxIsCharGenFacePreset As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCharGenHelp As System.Windows.Forms.Label


    Friend WithEvents TintsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListViewTints As System.Windows.Forms.ListView
    Friend WithEvents ColumnTintGroup As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnTintSlot As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnTintLayer As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnTintColor As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnTintPercent As System.Windows.Forms.ColumnHeader
    Friend WithEvents TintsButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddTint As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveTint As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveAllInCategory As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveZeroedTints As System.Windows.Forms.Button
    Friend WithEvents TextBoxTintFilter As System.Windows.Forms.TextBox

    Friend WithEvents PanelTintDetail As System.Windows.Forms.GroupBox
    Friend WithEvents TintDetailLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelTintLayerCaption As System.Windows.Forms.Label
    Friend WithEvents LabelTintLayerName As System.Windows.Forms.Label
    Friend WithEvents LabelTintPaletteCaption As System.Windows.Forms.Label
    Friend WithEvents ComboBoxTintPalette As System.Windows.Forms.ComboBox
    Friend WithEvents PanelTintColorSwatch As System.Windows.Forms.Panel
    Friend WithEvents ButtonTintCustomRGB As System.Windows.Forms.Button
    Friend WithEvents LabelTintPercentCaption As System.Windows.Forms.Label
    Friend WithEvents TrackBarTintPercent As FO4_Base_Library.TinySliderTextBox

    Friend WithEvents VertexMorphsPanel As System.Windows.Forms.Panel

    Friend WithEvents BoneRegionsRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents BoneRegionsContainer As System.Windows.Forms.Panel

    Friend WithEvents GroupBoxFmin As System.Windows.Forms.GroupBox
    Friend WithEvents FminLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFminCaption As System.Windows.Forms.Label
    Friend WithEvents TrackBarFmin As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents TableLayoutPanel1 As TableLayoutPanel
End Class
