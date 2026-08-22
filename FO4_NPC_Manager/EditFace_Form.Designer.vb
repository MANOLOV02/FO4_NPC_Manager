' UI built in Designer per 00-reglas-ui-y-vb.md.
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
        components = New ComponentModel.Container()
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(EditFace_Form))
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
        PanelSseCustomHair = New FlowLayoutPanel()
        ButtonSseCustomHairColor = New Button()
        ButtonSseCustomHairClear = New Button()
        LabelSseCustomHair = New Label()
        GroupBoxFaceFlags = New GroupBox()
        FaceFlagsLayout = New FlowLayoutPanel()
        CheckBoxIsCharGenFacePreset = New CheckBox()
        LabelCharGenHelp = New Label()
        GroupBoxSseHeadTexture = New GroupBox()
        FlowSseHeadTex = New FlowLayoutPanel()
        LabelSseHeadTex = New Label()
        ButtonSseHeadTexPick = New Button()
        ButtonSseHeadTexDefault = New Button()
        ButtonSseHeadTexClear = New Button()
        TabPageSseTints = New TabPage()
        PanelSseTints = New Panel()
        SseTintSplit = New TableLayoutPanel()
        SseTintListHost = New TableLayoutPanel()
        LabelSseTintLayers = New Label()
        ListBoxSseTintLayers = New DoubleBufferedListBox()
        GroupBoxSseTintDetail = New GroupBox()
        PanelSseTintDetail = New Panel()
        SseTintDetailLayout = New TableLayoutPanel()
        LabelSseTintColorSourceCaption = New Label()
        ComboBoxSseTintPreset = New ComboBox()
        LabelSseTintColorCaption = New Label()
        SseTintColorRow = New TableLayoutPanel()
        ButtonSseTintSwatch = New Button()
        ButtonSseTintCustom = New Button()
        LabelSseTintCoverageCaption = New Label()
        SliderSseTintCoverage = New TinySliderTextBox()
        LabelSseTintMaskCaption = New Label()
        LabelSseTintMask = New Label()
        SseTintMaskButtons = New TableLayoutPanel()
        ButtonSseTintMaskPick = New Button()
        ButtonSseTintMaskClear = New Button()
        ButtonSseTintReset = New Button()
        ButtonSseTintResetAll = New Button()
        LabelSseTintEmpty = New Label()
        TabPageSseFaceOverlays = New TabPage()
        SseFaceOvRoot = New TableLayoutPanel()
        LabelSseFaceOvHeader = New Label()
        SseFaceOvBody = New TableLayoutPanel()
        GroupBoxSseFacePaints = New GroupBox()
        SseFaceOvCatalogLayout = New TableLayoutPanel()
        TextBoxSseFaceOvFilter = New TextBox()
        ListBoxSseFacePaintCatalog = New ListBox()
        FlowSseFaceOvButtons = New FlowLayoutPanel()
        ButtonSseFaceOvAdd = New Button()
        ButtonSseFaceOvRemove = New Button()
        ButtonSseFaceOvUp = New Button()
        ButtonSseFaceOvDown = New Button()
        GroupBoxSseFaceOvApplied = New GroupBox()
        SseFaceOvRightLayout = New TableLayoutPanel()
        ListBoxSseFaceOvApplied = New ListBox()
        SseFaceOvDetail = New TableLayoutPanel()
        LabelSseFaceOvTexture = New Label()
        SseFaceOvDiffuseRow = New TableLayoutPanel()
        TextBoxSseFaceOvDiffuse = New TextBox()
        LabelSseFaceOvNormal = New Label()
        SseFaceOvNormalRow = New TableLayoutPanel()
        TextBoxSseFaceOvNormal = New TextBox()
        CheckBoxSseFaceOvTint = New CheckBox()
        ButtonSseFaceOvTintColor = New Button()
        LabelSseFaceOvOpacity = New Label()
        SliderSseFaceOvAlpha = New TinySliderTextBox()
        FlowSseFaceOvMagic = New FlowLayoutPanel()
        CheckBoxSseFaceOvMagic = New CheckBox()
        LabelSseFaceOvMagicNote = New Label()
        TabPageSseMorphs = New TabPage()
        PanelSseMorphs = New Panel()
        TabPageSseRaceMenu = New TabPage()
        SseRaceMenuRoot = New TableLayoutPanel()
        TextBoxSseRaceMenuFilter = New TextBox()
        FlowSseRaceMenu = New FlowLayoutPanel()
        LabelSseRaceMenuEmpty = New Label()
        TabPageSseSculpt = New TabPage()
        ListSseSculpt = New ListView()
        SseSculptButtonRow = New FlowLayoutPanel()
        ButtonRegenSseMorphs = New Button()
        ButtonDeleteSseSculpt = New Button()
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
        TabPageBoneRegions = New TabPage()
        BoneRegionsRoot = New TableLayoutPanel()
        BoneRegionsContainer = New Panel()
        BoneRegionsTabs = New TabControl()
        LabelBoneRegionsEmpty = New Label()
        GroupBoxFmin = New GroupBox()
        FminLayout = New TableLayoutPanel()
        LabelFminCaption = New Label()
        TrackBarFmin = New TinySliderTextBox()
        TabPageVertex = New TabPage()
        VertexMorphsPanel = New Panel()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        ButtonResetSection = New Button()
        PreviewSidebar = New TableLayoutPanel()
        RenderTogglesPanel = New FlowLayoutPanel()
        CheckBoxRenderGore = New CheckBox()
        PreviewHostPanel = New Panel()
        ToolTipSseTint = New ToolTip(components)
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
        PanelSseCustomHair.SuspendLayout()
        GroupBoxFaceFlags.SuspendLayout()
        FaceFlagsLayout.SuspendLayout()
        GroupBoxSseHeadTexture.SuspendLayout()
        FlowSseHeadTex.SuspendLayout()
        TabPageSseTints.SuspendLayout()
        PanelSseTints.SuspendLayout()
        SseTintSplit.SuspendLayout()
        SseTintListHost.SuspendLayout()
        GroupBoxSseTintDetail.SuspendLayout()
        PanelSseTintDetail.SuspendLayout()
        SseTintDetailLayout.SuspendLayout()
        SseTintColorRow.SuspendLayout()
        SseTintMaskButtons.SuspendLayout()
        TabPageSseFaceOverlays.SuspendLayout()
        SseFaceOvRoot.SuspendLayout()
        SseFaceOvBody.SuspendLayout()
        GroupBoxSseFacePaints.SuspendLayout()
        SseFaceOvCatalogLayout.SuspendLayout()
        FlowSseFaceOvButtons.SuspendLayout()
        GroupBoxSseFaceOvApplied.SuspendLayout()
        SseFaceOvRightLayout.SuspendLayout()
        SseFaceOvDetail.SuspendLayout()
        SseFaceOvDiffuseRow.SuspendLayout()
        SseFaceOvNormalRow.SuspendLayout()
        FlowSseFaceOvMagic.SuspendLayout()
        TabPageSseMorphs.SuspendLayout()
        TabPageSseRaceMenu.SuspendLayout()
        SseRaceMenuRoot.SuspendLayout()
        TabPageSseSculpt.SuspendLayout()
        SseSculptButtonRow.SuspendLayout()
        TabPageTints.SuspendLayout()
        TintsLayout.SuspendLayout()
        TintsButtonRow.SuspendLayout()
        PanelTintDetail.SuspendLayout()
        TintDetailLayout.SuspendLayout()
        TabPageBoneRegions.SuspendLayout()
        BoneRegionsRoot.SuspendLayout()
        BoneRegionsContainer.SuspendLayout()
        GroupBoxFmin.SuspendLayout()
        FminLayout.SuspendLayout()
        TabPageVertex.SuspendLayout()
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
        PreviewSplit.Panel1MinSize = 860
        ' 
        ' PreviewSplit.Panel2
        ' 
        PreviewSplit.Panel2.Controls.Add(PreviewSidebar)
        PreviewSplit.Size = New Size(1560, 781)
        PreviewSplit.SplitterDistance = 860
        PreviewSplit.TabIndex = 0
        ' 
        ' RootLayout
        ' 
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        RootLayout.Controls.Add(TabsFace, 0, 0)
        RootLayout.Controls.Add(BottomLayout, 0, 1)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 2
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(860, 781)
        RootLayout.TabIndex = 0
        ' 
        ' TabsFace
        ' 
        TabsFace.Controls.Add(TabPageFaceParts)
        TabsFace.Controls.Add(TabPageSseTints)
        TabsFace.Controls.Add(TabPageSseFaceOverlays)
        TabsFace.Controls.Add(TabPageSseMorphs)
        TabsFace.Controls.Add(TabPageSseRaceMenu)
        TabsFace.Controls.Add(TabPageSseSculpt)
        TabsFace.Controls.Add(TabPageTints)
        TabsFace.Controls.Add(TabPageBoneRegions)
        TabsFace.Controls.Add(TabPageVertex)
        TabsFace.Dock = DockStyle.Fill
        TabsFace.Location = New Point(11, 11)
        TabsFace.Name = "TabsFace"
        TabsFace.SelectedIndex = 0
        TabsFace.Size = New Size(838, 718)
        TabsFace.TabIndex = 0
        ' 
        ' TabPageFaceParts
        ' 
        TabPageFaceParts.Controls.Add(FacePartsLayout)
        TabPageFaceParts.Location = New Point(4, 24)
        TabPageFaceParts.Name = "TabPageFaceParts"
        TabPageFaceParts.Padding = New Padding(6)
        TabPageFaceParts.Size = New Size(830, 690)
        TabPageFaceParts.TabIndex = 0
        TabPageFaceParts.Text = "Parts"
        ' 
        ' FacePartsLayout
        ' 
        FacePartsLayout.ColumnCount = 1
        FacePartsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        FacePartsLayout.Controls.Add(GroupBoxHeadParts, 0, 0)
        FacePartsLayout.Controls.Add(GroupBoxHairColor, 0, 1)
        FacePartsLayout.Controls.Add(GroupBoxFaceFlags, 0, 2)
        FacePartsLayout.Controls.Add(GroupBoxSseHeadTexture, 0, 3)
        FacePartsLayout.Dock = DockStyle.Fill
        FacePartsLayout.Location = New Point(6, 6)
        FacePartsLayout.Name = "FacePartsLayout"
        FacePartsLayout.RowCount = 4
        FacePartsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 60.0F))
        FacePartsLayout.RowStyles.Add(New RowStyle())
        FacePartsLayout.RowStyles.Add(New RowStyle())
        FacePartsLayout.RowStyles.Add(New RowStyle())
        FacePartsLayout.Size = New Size(818, 678)
        FacePartsLayout.TabIndex = 0
        ' 
        ' GroupBoxHeadParts
        ' 
        GroupBoxHeadParts.Controls.Add(HeadPartsLayout)
        GroupBoxHeadParts.Dock = DockStyle.Fill
        GroupBoxHeadParts.Location = New Point(3, 3)
        GroupBoxHeadParts.Name = "GroupBoxHeadParts"
        GroupBoxHeadParts.Size = New Size(812, 404)
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
        HeadPartsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        HeadPartsLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 56.0F))
        HeadPartsLayout.Size = New Size(806, 382)
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
        TableLayoutPanel1.Location = New Point(7, 325)
        TableLayoutPanel1.Name = "TableLayoutPanel1"
        TableLayoutPanel1.RowCount = 2
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 25.0F))
        TableLayoutPanel1.RowStyles.Add(New RowStyle(SizeType.Absolute, 25.0F))
        TableLayoutPanel1.Size = New Size(792, 50)
        TableLayoutPanel1.TabIndex = 11
        ' 
        ' ButtonRemoveHeadPart
        ' 
        ButtonRemoveHeadPart.AutoSize = True
        ButtonRemoveHeadPart.Dock = DockStyle.Fill
        ButtonRemoveHeadPart.Location = New Point(660, 25)
        ButtonRemoveHeadPart.Margin = New Padding(0)
        ButtonRemoveHeadPart.Name = "ButtonRemoveHeadPart"
        ButtonRemoveHeadPart.Size = New Size(132, 25)
        ButtonRemoveHeadPart.TabIndex = 10
        ButtonRemoveHeadPart.Text = "-Remove"
        ' 
        ' ButtonAddMisc
        ' 
        ButtonAddMisc.AutoSize = True
        ButtonAddMisc.Dock = DockStyle.Fill
        ButtonAddMisc.Location = New Point(396, 25)
        ButtonAddMisc.Margin = New Padding(0)
        ButtonAddMisc.Name = "ButtonAddMisc"
        ButtonAddMisc.Size = New Size(132, 25)
        ButtonAddMisc.TabIndex = 9
        ButtonAddMisc.Text = "+Misc"
        ' 
        ' ButtonAddMeatcaps
        ' 
        ButtonAddMeatcaps.AutoSize = True
        ButtonAddMeatcaps.Dock = DockStyle.Fill
        ButtonAddMeatcaps.Location = New Point(264, 25)
        ButtonAddMeatcaps.Margin = New Padding(0)
        ButtonAddMeatcaps.Name = "ButtonAddMeatcaps"
        ButtonAddMeatcaps.Size = New Size(132, 25)
        ButtonAddMeatcaps.TabIndex = 8
        ButtonAddMeatcaps.Text = "+Meatcaps"
        ' 
        ' ButtonAddHeadRear
        ' 
        ButtonAddHeadRear.AutoSize = True
        ButtonAddHeadRear.Dock = DockStyle.Fill
        ButtonAddHeadRear.Location = New Point(132, 25)
        ButtonAddHeadRear.Margin = New Padding(0)
        ButtonAddHeadRear.Name = "ButtonAddHeadRear"
        ButtonAddHeadRear.Size = New Size(132, 25)
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
        ButtonAddTeeth.Size = New Size(132, 25)
        ButtonAddTeeth.TabIndex = 6
        ButtonAddTeeth.Text = "+Teeth"
        ' 
        ' ButtonAddScar
        ' 
        ButtonAddScar.AutoSize = True
        ButtonAddScar.Dock = DockStyle.Fill
        ButtonAddScar.Location = New Point(660, 0)
        ButtonAddScar.Margin = New Padding(0)
        ButtonAddScar.Name = "ButtonAddScar"
        ButtonAddScar.Size = New Size(132, 25)
        ButtonAddScar.TabIndex = 5
        ButtonAddScar.Text = "+Scar"
        ' 
        ' ButtonAddEyebrows
        ' 
        ButtonAddEyebrows.AutoSize = True
        ButtonAddEyebrows.Dock = DockStyle.Fill
        ButtonAddEyebrows.Location = New Point(528, 0)
        ButtonAddEyebrows.Margin = New Padding(0)
        ButtonAddEyebrows.Name = "ButtonAddEyebrows"
        ButtonAddEyebrows.Size = New Size(132, 25)
        ButtonAddEyebrows.TabIndex = 4
        ButtonAddEyebrows.Text = "+Eyebrows"
        ' 
        ' ButtonAddFacialHair
        ' 
        ButtonAddFacialHair.AutoSize = True
        ButtonAddFacialHair.Dock = DockStyle.Fill
        ButtonAddFacialHair.Location = New Point(396, 0)
        ButtonAddFacialHair.Margin = New Padding(0)
        ButtonAddFacialHair.Name = "ButtonAddFacialHair"
        ButtonAddFacialHair.Size = New Size(132, 25)
        ButtonAddFacialHair.TabIndex = 3
        ButtonAddFacialHair.Text = "+Facial Hair"
        ' 
        ' ButtonAddEyes
        ' 
        ButtonAddEyes.AutoSize = True
        ButtonAddEyes.Dock = DockStyle.Fill
        ButtonAddEyes.Location = New Point(264, 0)
        ButtonAddEyes.Margin = New Padding(0)
        ButtonAddEyes.Name = "ButtonAddEyes"
        ButtonAddEyes.Size = New Size(132, 25)
        ButtonAddEyes.TabIndex = 2
        ButtonAddEyes.Text = "+Eyes"
        ' 
        ' ButtonAddHair
        ' 
        ButtonAddHair.AutoSize = True
        ButtonAddHair.Dock = DockStyle.Fill
        ButtonAddHair.Location = New Point(132, 0)
        ButtonAddHair.Margin = New Padding(0)
        ButtonAddHair.Name = "ButtonAddHair"
        ButtonAddHair.Size = New Size(132, 25)
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
        ButtonAddFace.Size = New Size(132, 25)
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
        ListViewHeadParts.Size = New Size(792, 312)
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
        GroupBoxHairColor.Location = New Point(3, 413)
        GroupBoxHairColor.Name = "GroupBoxHairColor"
        GroupBoxHairColor.Size = New Size(812, 119)
        GroupBoxHairColor.TabIndex = 1
        GroupBoxHairColor.TabStop = False
        GroupBoxHairColor.Text = "Hair Color (NPC.HCLF)"
        ' 
        ' HairColorLayout
        ' 
        HairColorLayout.AutoSize = True
        HairColorLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        HairColorLayout.ColumnCount = 2
        HairColorLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        HairColorLayout.ColumnStyles.Add(New ColumnStyle())
        HairColorLayout.Controls.Add(ComboBoxHairColor, 0, 0)
        HairColorLayout.Controls.Add(ButtonClearHairColor, 1, 0)
        HairColorLayout.Controls.Add(PanelHairColorSwatch, 0, 1)
        HairColorLayout.Controls.Add(PanelSseCustomHair, 0, 2)
        HairColorLayout.Dock = DockStyle.Fill
        HairColorLayout.Location = New Point(3, 19)
        HairColorLayout.Name = "HairColorLayout"
        HairColorLayout.Padding = New Padding(4)
        HairColorLayout.RowCount = 3
        HairColorLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0F))
        HairColorLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 20.0F))
        HairColorLayout.RowStyles.Add(New RowStyle())
        HairColorLayout.Size = New Size(806, 97)
        HairColorLayout.TabIndex = 0
        ' 
        ' ComboBoxHairColor
        ' 
        ComboBoxHairColor.Dock = DockStyle.Fill
        ComboBoxHairColor.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxHairColor.Location = New Point(6, 6)
        ComboBoxHairColor.Margin = New Padding(2)
        ComboBoxHairColor.Name = "ComboBoxHairColor"
        ComboBoxHairColor.Size = New Size(694, 23)
        ComboBoxHairColor.TabIndex = 0
        ' 
        ' ButtonClearHairColor
        ' 
        ButtonClearHairColor.Dock = DockStyle.Fill
        ButtonClearHairColor.Location = New Point(704, 6)
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
        PanelHairColorSwatch.Size = New Size(694, 16)
        PanelHairColorSwatch.TabIndex = 2
        ' 
        ' PanelSseCustomHair
        ' 
        PanelSseCustomHair.AutoSize = True
        PanelSseCustomHair.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelSseCustomHair.Controls.Add(ButtonSseCustomHairColor)
        PanelSseCustomHair.Controls.Add(ButtonSseCustomHairClear)
        PanelSseCustomHair.Controls.Add(LabelSseCustomHair)
        PanelSseCustomHair.Dock = DockStyle.Fill
        PanelSseCustomHair.Location = New Point(6, 62)
        PanelSseCustomHair.Margin = New Padding(2)
        PanelSseCustomHair.Name = "PanelSseCustomHair"
        PanelSseCustomHair.Size = New Size(694, 29)
        PanelSseCustomHair.TabIndex = 3
        PanelSseCustomHair.WrapContents = False
        ' 
        ' ButtonSseCustomHairColor
        ' 
        ButtonSseCustomHairColor.AutoSize = True
        ButtonSseCustomHairColor.Location = New Point(2, 2)
        ButtonSseCustomHairColor.Margin = New Padding(2)
        ButtonSseCustomHairColor.Name = "ButtonSseCustomHairColor"
        ButtonSseCustomHairColor.Size = New Size(150, 25)
        ButtonSseCustomHairColor.TabIndex = 0
        ButtonSseCustomHairColor.Text = "Custom colour…"
        ' 
        ' ButtonSseCustomHairClear
        ' 
        ButtonSseCustomHairClear.AutoSize = True
        ButtonSseCustomHairClear.Location = New Point(156, 2)
        ButtonSseCustomHairClear.Margin = New Padding(2)
        ButtonSseCustomHairClear.Name = "ButtonSseCustomHairClear"
        ButtonSseCustomHairClear.Size = New Size(110, 25)
        ButtonSseCustomHairClear.TabIndex = 1
        ButtonSseCustomHairClear.Text = "Use list colour"
        ' 
        ' LabelSseCustomHair
        ' 
        LabelSseCustomHair.AutoSize = True
        LabelSseCustomHair.Location = New Point(276, 7)
        LabelSseCustomHair.Margin = New Padding(8, 7, 2, 2)
        LabelSseCustomHair.Name = "LabelSseCustomHair"
        LabelSseCustomHair.Size = New Size(0, 15)
        LabelSseCustomHair.TabIndex = 2
        ' 
        ' GroupBoxFaceFlags
        ' 
        GroupBoxFaceFlags.AutoSize = True
        GroupBoxFaceFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxFaceFlags.Controls.Add(FaceFlagsLayout)
        GroupBoxFaceFlags.Dock = DockStyle.Top
        GroupBoxFaceFlags.Location = New Point(3, 538)
        GroupBoxFaceFlags.Name = "GroupBoxFaceFlags"
        GroupBoxFaceFlags.Size = New Size(812, 70)
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
        FaceFlagsLayout.Size = New Size(806, 48)
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
        ' GroupBoxSseHeadTexture
        ' 
        GroupBoxSseHeadTexture.AutoSize = True
        GroupBoxSseHeadTexture.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSseHeadTexture.Controls.Add(FlowSseHeadTex)
        GroupBoxSseHeadTexture.Dock = DockStyle.Fill
        GroupBoxSseHeadTexture.Location = New Point(3, 614)
        GroupBoxSseHeadTexture.Name = "GroupBoxSseHeadTexture"
        GroupBoxSseHeadTexture.Size = New Size(812, 61)
        GroupBoxSseHeadTexture.TabIndex = 3
        GroupBoxSseHeadTexture.TabStop = False
        GroupBoxSseHeadTexture.Text = "Head texture (FTST)"
        GroupBoxSseHeadTexture.Visible = False
        ' 
        ' FlowSseHeadTex
        ' 
        FlowSseHeadTex.AutoSize = True
        FlowSseHeadTex.Controls.Add(LabelSseHeadTex)
        FlowSseHeadTex.Controls.Add(ButtonSseHeadTexPick)
        FlowSseHeadTex.Controls.Add(ButtonSseHeadTexDefault)
        FlowSseHeadTex.Controls.Add(ButtonSseHeadTexClear)
        FlowSseHeadTex.Dock = DockStyle.Fill
        FlowSseHeadTex.Location = New Point(3, 19)
        FlowSseHeadTex.Name = "FlowSseHeadTex"
        FlowSseHeadTex.Padding = New Padding(4)
        FlowSseHeadTex.Size = New Size(806, 39)
        FlowSseHeadTex.TabIndex = 0
        FlowSseHeadTex.WrapContents = False
        ' 
        ' LabelSseHeadTex
        ' 
        LabelSseHeadTex.AutoSize = True
        LabelSseHeadTex.Location = New Point(7, 13)
        LabelSseHeadTex.Margin = New Padding(3, 9, 12, 3)
        LabelSseHeadTex.Name = "LabelSseHeadTex"
        LabelSseHeadTex.Size = New Size(0, 15)
        LabelSseHeadTex.TabIndex = 0
        ' 
        ' ButtonSseHeadTexPick
        ' 
        ButtonSseHeadTexPick.AutoSize = True
        ButtonSseHeadTexPick.Location = New Point(22, 7)
        ButtonSseHeadTexPick.Name = "ButtonSseHeadTexPick"
        ButtonSseHeadTexPick.Size = New Size(87, 25)
        ButtonSseHeadTexPick.TabIndex = 1
        ButtonSseHeadTexPick.Text = "Change…"
        ' 
        ' ButtonSseHeadTexDefault
        ' 
        ButtonSseHeadTexDefault.AutoSize = True
        ButtonSseHeadTexDefault.Location = New Point(115, 7)
        ButtonSseHeadTexDefault.Name = "ButtonSseHeadTexDefault"
        ButtonSseHeadTexDefault.Size = New Size(140, 25)
        ButtonSseHeadTexDefault.TabIndex = 2
        ButtonSseHeadTexDefault.Text = "Use record default"
        ' 
        ' ButtonSseHeadTexClear
        ' 
        ButtonSseHeadTexClear.AutoSize = True
        ButtonSseHeadTexClear.Location = New Point(261, 7)
        ButtonSseHeadTexClear.Name = "ButtonSseHeadTexClear"
        ButtonSseHeadTexClear.Size = New Size(128, 25)
        ButtonSseHeadTexClear.TabIndex = 3
        ButtonSseHeadTexClear.Text = "Clear (no FTST)"
        ' 
        ' TabPageSseTints
        ' 
        TabPageSseTints.Controls.Add(PanelSseTints)
        TabPageSseTints.Location = New Point(4, 24)
        TabPageSseTints.Name = "TabPageSseTints"
        TabPageSseTints.Padding = New Padding(6)
        TabPageSseTints.Size = New Size(830, 690)
        TabPageSseTints.TabIndex = 2
        TabPageSseTints.Text = "Tints"
        ' 
        ' PanelSseTints
        ' 
        PanelSseTints.Controls.Add(SseTintSplit)
        PanelSseTints.Dock = DockStyle.Fill
        PanelSseTints.Location = New Point(6, 6)
        PanelSseTints.Name = "PanelSseTints"
        PanelSseTints.Size = New Size(818, 678)
        PanelSseTints.TabIndex = 0
        ' 
        ' SseTintSplit
        ' 
        SseTintSplit.ColumnCount = 2
        SseTintSplit.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 300.0F))
        SseTintSplit.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseTintSplit.Controls.Add(SseTintListHost, 0, 0)
        SseTintSplit.Controls.Add(GroupBoxSseTintDetail, 1, 0)
        SseTintSplit.Dock = DockStyle.Fill
        SseTintSplit.Location = New Point(0, 0)
        SseTintSplit.Name = "SseTintSplit"
        SseTintSplit.RowCount = 1
        SseTintSplit.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseTintSplit.Size = New Size(818, 678)
        SseTintSplit.TabIndex = 0
        ' 
        ' SseTintListHost
        ' 
        SseTintListHost.ColumnCount = 1
        SseTintListHost.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseTintListHost.Controls.Add(LabelSseTintLayers, 0, 0)
        SseTintListHost.Controls.Add(ListBoxSseTintLayers, 0, 1)
        SseTintListHost.Dock = DockStyle.Fill
        SseTintListHost.Location = New Point(3, 3)
        SseTintListHost.Name = "SseTintListHost"
        SseTintListHost.RowCount = 2
        SseTintListHost.RowStyles.Add(New RowStyle(SizeType.Absolute, 22.0F))
        SseTintListHost.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseTintListHost.Size = New Size(294, 672)
        SseTintListHost.TabIndex = 0
        ' 
        ' LabelSseTintLayers
        ' 
        LabelSseTintLayers.Dock = DockStyle.Fill
        LabelSseTintLayers.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
        LabelSseTintLayers.Location = New Point(3, 0)
        LabelSseTintLayers.Name = "LabelSseTintLayers"
        LabelSseTintLayers.Size = New Size(288, 22)
        LabelSseTintLayers.TabIndex = 0
        LabelSseTintLayers.Text = "RACE tint layers"
        LabelSseTintLayers.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ListBoxSseTintLayers
        ' 
        ListBoxSseTintLayers.Dock = DockStyle.Fill
        ListBoxSseTintLayers.DrawMode = DrawMode.OwnerDrawFixed
        ListBoxSseTintLayers.IntegralHeight = False
        ListBoxSseTintLayers.ItemHeight = 20
        ListBoxSseTintLayers.Location = New Point(3, 25)
        ListBoxSseTintLayers.Name = "ListBoxSseTintLayers"
        ListBoxSseTintLayers.Size = New Size(288, 644)
        ListBoxSseTintLayers.TabIndex = 1
        ' 
        ' GroupBoxSseTintDetail
        ' 
        GroupBoxSseTintDetail.Controls.Add(PanelSseTintDetail)
        GroupBoxSseTintDetail.Dock = DockStyle.Fill
        GroupBoxSseTintDetail.Location = New Point(306, 22)
        GroupBoxSseTintDetail.Margin = New Padding(6, 22, 4, 4)
        GroupBoxSseTintDetail.Name = "GroupBoxSseTintDetail"
        GroupBoxSseTintDetail.Size = New Size(508, 652)
        GroupBoxSseTintDetail.TabIndex = 1
        GroupBoxSseTintDetail.TabStop = False
        GroupBoxSseTintDetail.Text = "Selected layer"
        ' 
        ' PanelSseTintDetail
        ' 
        PanelSseTintDetail.AutoScroll = True
        PanelSseTintDetail.Controls.Add(SseTintDetailLayout)
        PanelSseTintDetail.Controls.Add(LabelSseTintEmpty)
        PanelSseTintDetail.Dock = DockStyle.Fill
        PanelSseTintDetail.Location = New Point(3, 19)
        PanelSseTintDetail.Name = "PanelSseTintDetail"
        PanelSseTintDetail.Padding = New Padding(8, 6, 8, 8)
        PanelSseTintDetail.Size = New Size(502, 630)
        PanelSseTintDetail.TabIndex = 0
        ' 
        ' SseTintDetailLayout
        ' 
        SseTintDetailLayout.AutoSize = True
        SseTintDetailLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SseTintDetailLayout.ColumnCount = 2
        SseTintDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 128.0F))
        SseTintDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseTintDetailLayout.Controls.Add(LabelSseTintColorSourceCaption, 0, 0)
        SseTintDetailLayout.Controls.Add(ComboBoxSseTintPreset, 1, 0)
        SseTintDetailLayout.Controls.Add(LabelSseTintColorCaption, 0, 1)
        SseTintDetailLayout.Controls.Add(SseTintColorRow, 1, 1)
        SseTintDetailLayout.Controls.Add(LabelSseTintCoverageCaption, 0, 2)
        SseTintDetailLayout.Controls.Add(SliderSseTintCoverage, 1, 2)
        SseTintDetailLayout.Controls.Add(LabelSseTintMaskCaption, 0, 3)
        SseTintDetailLayout.Controls.Add(LabelSseTintMask, 1, 3)
        SseTintDetailLayout.Controls.Add(SseTintMaskButtons, 1, 4)
        SseTintDetailLayout.Dock = DockStyle.Top
        SseTintDetailLayout.Location = New Point(8, 6)
        SseTintDetailLayout.Name = "SseTintDetailLayout"
        SseTintDetailLayout.RowCount = 5
        SseTintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 68.0F))
        SseTintDetailLayout.Size = New Size(486, 204)
        SseTintDetailLayout.TabIndex = 0
        ' 
        ' LabelSseTintColorSourceCaption
        ' 
        LabelSseTintColorSourceCaption.Dock = DockStyle.Fill
        LabelSseTintColorSourceCaption.Location = New Point(3, 0)
        LabelSseTintColorSourceCaption.Name = "LabelSseTintColorSourceCaption"
        LabelSseTintColorSourceCaption.Size = New Size(122, 34)
        LabelSseTintColorSourceCaption.TabIndex = 0
        LabelSseTintColorSourceCaption.Text = "Color source:"
        LabelSseTintColorSourceCaption.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ComboBoxSseTintPreset
        ' 
        ComboBoxSseTintPreset.Dock = DockStyle.Fill
        ComboBoxSseTintPreset.DrawMode = DrawMode.OwnerDrawFixed
        ComboBoxSseTintPreset.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxSseTintPreset.Location = New Point(131, 4)
        ComboBoxSseTintPreset.Margin = New Padding(3, 4, 3, 3)
        ComboBoxSseTintPreset.Name = "ComboBoxSseTintPreset"
        ComboBoxSseTintPreset.Size = New Size(352, 24)
        ComboBoxSseTintPreset.TabIndex = 1
        ' 
        ' LabelSseTintColorCaption
        ' 
        LabelSseTintColorCaption.Dock = DockStyle.Fill
        LabelSseTintColorCaption.Location = New Point(3, 34)
        LabelSseTintColorCaption.Name = "LabelSseTintColorCaption"
        LabelSseTintColorCaption.Size = New Size(122, 34)
        LabelSseTintColorCaption.TabIndex = 2
        LabelSseTintColorCaption.Text = "Color:"
        LabelSseTintColorCaption.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SseTintColorRow
        ' 
        SseTintColorRow.ColumnCount = 2
        SseTintColorRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseTintColorRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92.0F))
        SseTintColorRow.Controls.Add(ButtonSseTintSwatch, 0, 0)
        SseTintColorRow.Controls.Add(ButtonSseTintCustom, 1, 0)
        SseTintColorRow.Dock = DockStyle.Fill
        SseTintColorRow.Location = New Point(128, 34)
        SseTintColorRow.Margin = New Padding(0)
        SseTintColorRow.Name = "SseTintColorRow"
        SseTintColorRow.RowCount = 1
        SseTintColorRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseTintColorRow.Size = New Size(358, 34)
        SseTintColorRow.TabIndex = 3
        ' 
        ' ButtonSseTintSwatch
        ' 
        ButtonSseTintSwatch.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ButtonSseTintSwatch.Enabled = False
        ButtonSseTintSwatch.FlatStyle = FlatStyle.Popup
        ButtonSseTintSwatch.Location = New Point(3, 4)
        ButtonSseTintSwatch.Margin = New Padding(3, 4, 3, 3)
        ButtonSseTintSwatch.Name = "ButtonSseTintSwatch"
        ButtonSseTintSwatch.Size = New Size(260, 26)
        ButtonSseTintSwatch.TabIndex = 0
        ' 
        ' ButtonSseTintCustom
        ' 
        ButtonSseTintCustom.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ButtonSseTintCustom.Location = New Point(269, 4)
        ButtonSseTintCustom.Margin = New Padding(3, 4, 3, 3)
        ButtonSseTintCustom.Name = "ButtonSseTintCustom"
        ButtonSseTintCustom.Size = New Size(86, 26)
        ButtonSseTintCustom.TabIndex = 1
        ButtonSseTintCustom.Text = "Custom…"
        ToolTipSseTint.SetToolTip(ButtonSseTintCustom, "Pick a free RGB colour (TIAS = -1 = custom, like RaceMenu / the CK colour picker).")
        ' 
        ' LabelSseTintCoverageCaption
        ' 
        LabelSseTintCoverageCaption.Dock = DockStyle.Fill
        LabelSseTintCoverageCaption.Location = New Point(3, 68)
        LabelSseTintCoverageCaption.Name = "LabelSseTintCoverageCaption"
        LabelSseTintCoverageCaption.Size = New Size(122, 34)
        LabelSseTintCoverageCaption.TabIndex = 4
        LabelSseTintCoverageCaption.Text = "Coverage (TINV):"
        LabelSseTintCoverageCaption.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSseTintCoverage
        ' 
        SliderSseTintCoverage.AccentColor = SystemColors.HotTrack
        SliderSseTintCoverage.BackColor = SystemColors.Control
        SliderSseTintCoverage.DisplayFormat = "0.00"
        SliderSseTintCoverage.Dock = DockStyle.Fill
        SliderSseTintCoverage.LargeChange = 0.1R
        SliderSseTintCoverage.Location = New Point(131, 72)
        SliderSseTintCoverage.Margin = New Padding(3, 4, 3, 3)
        SliderSseTintCoverage.Maximum = 1.0R
        SliderSseTintCoverage.MinimumSize = New Size(100, 24)
        SliderSseTintCoverage.Name = "SliderSseTintCoverage"
        SliderSseTintCoverage.Size = New Size(352, 27)
        SliderSseTintCoverage.SmallChange = 0.01R
        SliderSseTintCoverage.TabIndex = 5
        SliderSseTintCoverage.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseTintCoverage.ThumbColor = SystemColors.HotTrack
        SliderSseTintCoverage.ThumbRadius = 4.0F
        SliderSseTintCoverage.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseTintMaskCaption
        ' 
        LabelSseTintMaskCaption.Dock = DockStyle.Fill
        LabelSseTintMaskCaption.Location = New Point(3, 102)
        LabelSseTintMaskCaption.Name = "LabelSseTintMaskCaption"
        LabelSseTintMaskCaption.Size = New Size(122, 34)
        LabelSseTintMaskCaption.TabIndex = 6
        LabelSseTintMaskCaption.Text = "Warpaint mask:"
        LabelSseTintMaskCaption.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' LabelSseTintMask
        ' 
        LabelSseTintMask.AutoEllipsis = True
        LabelSseTintMask.Dock = DockStyle.Fill
        LabelSseTintMask.Location = New Point(131, 102)
        LabelSseTintMask.Name = "LabelSseTintMask"
        LabelSseTintMask.Size = New Size(352, 34)
        LabelSseTintMask.TabIndex = 7
        LabelSseTintMask.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SseTintMaskButtons
        ' 
        SseTintMaskButtons.ColumnCount = 4
        SseTintMaskButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92.0F))
        SseTintMaskButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92.0F))
        SseTintMaskButtons.ColumnStyles.Add(New ColumnStyle())
        SseTintMaskButtons.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseTintMaskButtons.Controls.Add(ButtonSseTintMaskPick, 0, 0)
        SseTintMaskButtons.Controls.Add(ButtonSseTintMaskClear, 1, 0)
        SseTintMaskButtons.Controls.Add(ButtonSseTintReset, 2, 0)
        SseTintMaskButtons.Controls.Add(ButtonSseTintResetAll, 0, 1)
        SseTintMaskButtons.Dock = DockStyle.Fill
        SseTintMaskButtons.Location = New Point(128, 136)
        SseTintMaskButtons.Margin = New Padding(0)
        SseTintMaskButtons.Name = "SseTintMaskButtons"
        SseTintMaskButtons.RowCount = 2
        SseTintMaskButtons.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintMaskButtons.RowStyles.Add(New RowStyle(SizeType.Absolute, 34.0F))
        SseTintMaskButtons.Size = New Size(358, 68)
        SseTintMaskButtons.TabIndex = 8
        ' 
        ' ButtonSseTintMaskPick
        ' 
        ButtonSseTintMaskPick.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ButtonSseTintMaskPick.Location = New Point(3, 3)
        ButtonSseTintMaskPick.Margin = New Padding(3, 2, 3, 3)
        ButtonSseTintMaskPick.Name = "ButtonSseTintMaskPick"
        ButtonSseTintMaskPick.Size = New Size(86, 26)
        ButtonSseTintMaskPick.TabIndex = 0
        ButtonSseTintMaskPick.Text = "Choose…"
        ToolTipSseTint.SetToolTip(ButtonSseTintMaskPick, "Warpaint (RaceMenu): pick a tint mask registered by a mod. Empty = uses the RACE's own mask.")
        ' 
        ' ButtonSseTintMaskClear
        ' 
        ButtonSseTintMaskClear.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ButtonSseTintMaskClear.Location = New Point(95, 3)
        ButtonSseTintMaskClear.Margin = New Padding(3, 2, 3, 3)
        ButtonSseTintMaskClear.Name = "ButtonSseTintMaskClear"
        ButtonSseTintMaskClear.Size = New Size(86, 26)
        ButtonSseTintMaskClear.TabIndex = 1
        ButtonSseTintMaskClear.Text = "Clear"
        ' 
        ' ButtonSseTintReset
        ' 
        ButtonSseTintReset.Anchor = AnchorStyles.Left
        ButtonSseTintReset.AutoSize = True
        ButtonSseTintReset.Location = New Point(193, 3)
        ButtonSseTintReset.Margin = New Padding(9, 2, 3, 3)
        ButtonSseTintReset.Name = "ButtonSseTintReset"
        ButtonSseTintReset.Size = New Size(147, 26)
        ButtonSseTintReset.TabIndex = 2
        ButtonSseTintReset.Text = "Reset to RACE default"
        ' 
        ' ButtonSseTintResetAll
        ' 
        SseTintMaskButtons.SetColumnSpan(ButtonSseTintResetAll, 3)
        ButtonSseTintResetAll.Dock = DockStyle.Fill
        ButtonSseTintResetAll.Location = New Point(3, 36)
        ButtonSseTintResetAll.Margin = New Padding(3, 2, 3, 3)
        ButtonSseTintResetAll.Name = "ButtonSseTintResetAll"
        ButtonSseTintResetAll.Size = New Size(340, 29)
        ButtonSseTintResetAll.TabIndex = 3
        ButtonSseTintResetAll.Text = "Reset all tints to RACE default"
        ToolTipSseTint.SetToolTip(ButtonSseTintResetAll, resources.GetString("ButtonSseTintResetAll.ToolTip"))
        ' 
        ' LabelSseTintEmpty
        ' 
        LabelSseTintEmpty.AutoSize = True
        LabelSseTintEmpty.Location = New Point(8, 6)
        LabelSseTintEmpty.Name = "LabelSseTintEmpty"
        LabelSseTintEmpty.Size = New Size(256, 15)
        LabelSseTintEmpty.TabIndex = 1
        LabelSseTintEmpty.Text = "(this race declares no tint layers for this gender)"
        LabelSseTintEmpty.Visible = False
        ' 
        ' TabPageSseFaceOverlays
        ' 
        TabPageSseFaceOverlays.Controls.Add(SseFaceOvRoot)
        TabPageSseFaceOverlays.Location = New Point(4, 24)
        TabPageSseFaceOverlays.Name = "TabPageSseFaceOverlays"
        TabPageSseFaceOverlays.Padding = New Padding(6)
        TabPageSseFaceOverlays.Size = New Size(830, 690)
        TabPageSseFaceOverlays.TabIndex = 5
        TabPageSseFaceOverlays.Text = "Overlays (RM)"
        ' 
        ' SseFaceOvRoot
        ' 
        SseFaceOvRoot.ColumnCount = 1
        SseFaceOvRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvRoot.Controls.Add(LabelSseFaceOvHeader, 0, 0)
        SseFaceOvRoot.Controls.Add(SseFaceOvBody, 0, 1)
        SseFaceOvRoot.Dock = DockStyle.Fill
        SseFaceOvRoot.Location = New Point(6, 6)
        SseFaceOvRoot.Name = "SseFaceOvRoot"
        SseFaceOvRoot.RowCount = 2
        SseFaceOvRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 30.0F))
        SseFaceOvRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvRoot.Size = New Size(818, 678)
        SseFaceOvRoot.TabIndex = 0
        ' 
        ' LabelSseFaceOvHeader
        ' 
        LabelSseFaceOvHeader.Dock = DockStyle.Fill
        LabelSseFaceOvHeader.Location = New Point(3, 0)
        LabelSseFaceOvHeader.Name = "LabelSseFaceOvHeader"
        LabelSseFaceOvHeader.Padding = New Padding(3, 6, 3, 0)
        LabelSseFaceOvHeader.Size = New Size(812, 30)
        LabelSseFaceOvHeader.TabIndex = 0
        LabelSseFaceOvHeader.Text = "Face paint overlays. Choose a paint on the left, then Add → to apply it to this NPC."
        ' 
        ' SseFaceOvBody
        ' 
        SseFaceOvBody.ColumnCount = 3
        SseFaceOvBody.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42.0F))
        SseFaceOvBody.ColumnStyles.Add(New ColumnStyle())
        SseFaceOvBody.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58.0F))
        SseFaceOvBody.Controls.Add(GroupBoxSseFacePaints, 0, 0)
        SseFaceOvBody.Controls.Add(FlowSseFaceOvButtons, 1, 0)
        SseFaceOvBody.Controls.Add(GroupBoxSseFaceOvApplied, 2, 0)
        SseFaceOvBody.Dock = DockStyle.Fill
        SseFaceOvBody.Location = New Point(3, 33)
        SseFaceOvBody.Name = "SseFaceOvBody"
        SseFaceOvBody.RowCount = 1
        SseFaceOvBody.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvBody.Size = New Size(812, 642)
        SseFaceOvBody.TabIndex = 1
        ' 
        ' GroupBoxSseFacePaints
        ' 
        GroupBoxSseFacePaints.Controls.Add(SseFaceOvCatalogLayout)
        GroupBoxSseFacePaints.Dock = DockStyle.Fill
        GroupBoxSseFacePaints.Location = New Point(3, 3)
        GroupBoxSseFacePaints.Name = "GroupBoxSseFacePaints"
        GroupBoxSseFacePaints.Size = New Size(292, 636)
        GroupBoxSseFacePaints.TabIndex = 0
        GroupBoxSseFacePaints.TabStop = False
        GroupBoxSseFacePaints.Text = "Face paints (RaceMenu)"
        ' 
        ' SseFaceOvCatalogLayout
        ' 
        SseFaceOvCatalogLayout.ColumnCount = 1
        SseFaceOvCatalogLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvCatalogLayout.Controls.Add(TextBoxSseFaceOvFilter, 0, 0)
        SseFaceOvCatalogLayout.Controls.Add(ListBoxSseFacePaintCatalog, 0, 1)
        SseFaceOvCatalogLayout.Dock = DockStyle.Fill
        SseFaceOvCatalogLayout.Location = New Point(3, 19)
        SseFaceOvCatalogLayout.Name = "SseFaceOvCatalogLayout"
        SseFaceOvCatalogLayout.RowCount = 2
        SseFaceOvCatalogLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28.0F))
        SseFaceOvCatalogLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvCatalogLayout.Size = New Size(286, 614)
        SseFaceOvCatalogLayout.TabIndex = 0
        ' 
        ' TextBoxSseFaceOvFilter
        ' 
        TextBoxSseFaceOvFilter.Dock = DockStyle.Fill
        TextBoxSseFaceOvFilter.Location = New Point(3, 3)
        TextBoxSseFaceOvFilter.Name = "TextBoxSseFaceOvFilter"
        TextBoxSseFaceOvFilter.PlaceholderText = "Filter paints…"
        TextBoxSseFaceOvFilter.Size = New Size(280, 23)
        TextBoxSseFaceOvFilter.TabIndex = 0
        ' 
        ' ListBoxSseFacePaintCatalog
        ' 
        ListBoxSseFacePaintCatalog.Dock = DockStyle.Fill
        ListBoxSseFacePaintCatalog.DrawMode = DrawMode.OwnerDrawFixed
        ListBoxSseFacePaintCatalog.IntegralHeight = False
        ListBoxSseFacePaintCatalog.Location = New Point(3, 31)
        ListBoxSseFacePaintCatalog.Name = "ListBoxSseFacePaintCatalog"
        ListBoxSseFacePaintCatalog.Size = New Size(280, 580)
        ListBoxSseFacePaintCatalog.TabIndex = 1
        ' 
        ' FlowSseFaceOvButtons
        ' 
        FlowSseFaceOvButtons.AutoSize = True
        FlowSseFaceOvButtons.Controls.Add(ButtonSseFaceOvAdd)
        FlowSseFaceOvButtons.Controls.Add(ButtonSseFaceOvRemove)
        FlowSseFaceOvButtons.Controls.Add(ButtonSseFaceOvUp)
        FlowSseFaceOvButtons.Controls.Add(ButtonSseFaceOvDown)
        FlowSseFaceOvButtons.Dock = DockStyle.Fill
        FlowSseFaceOvButtons.FlowDirection = FlowDirection.TopDown
        FlowSseFaceOvButtons.Location = New Point(302, 40)
        FlowSseFaceOvButtons.Margin = New Padding(4, 40, 4, 0)
        FlowSseFaceOvButtons.Name = "FlowSseFaceOvButtons"
        FlowSseFaceOvButtons.Size = New Size(94, 602)
        FlowSseFaceOvButtons.TabIndex = 1
        FlowSseFaceOvButtons.WrapContents = False
        ' 
        ' ButtonSseFaceOvAdd
        ' 
        ButtonSseFaceOvAdd.AutoSize = True
        ButtonSseFaceOvAdd.Location = New Point(2, 2)
        ButtonSseFaceOvAdd.Margin = New Padding(2)
        ButtonSseFaceOvAdd.Name = "ButtonSseFaceOvAdd"
        ButtonSseFaceOvAdd.Size = New Size(75, 25)
        ButtonSseFaceOvAdd.TabIndex = 0
        ButtonSseFaceOvAdd.Text = "Add →"
        ' 
        ' ButtonSseFaceOvRemove
        ' 
        ButtonSseFaceOvRemove.AutoSize = True
        ButtonSseFaceOvRemove.Location = New Point(2, 31)
        ButtonSseFaceOvRemove.Margin = New Padding(2)
        ButtonSseFaceOvRemove.Name = "ButtonSseFaceOvRemove"
        ButtonSseFaceOvRemove.Size = New Size(90, 25)
        ButtonSseFaceOvRemove.TabIndex = 1
        ButtonSseFaceOvRemove.Text = "← Remove"
        ' 
        ' ButtonSseFaceOvUp
        ' 
        ButtonSseFaceOvUp.AutoSize = True
        ButtonSseFaceOvUp.Location = New Point(2, 72)
        ButtonSseFaceOvUp.Margin = New Padding(2, 14, 2, 2)
        ButtonSseFaceOvUp.Name = "ButtonSseFaceOvUp"
        ButtonSseFaceOvUp.Size = New Size(75, 25)
        ButtonSseFaceOvUp.TabIndex = 2
        ButtonSseFaceOvUp.Text = "Up"
        ' 
        ' ButtonSseFaceOvDown
        ' 
        ButtonSseFaceOvDown.AutoSize = True
        ButtonSseFaceOvDown.Location = New Point(2, 101)
        ButtonSseFaceOvDown.Margin = New Padding(2)
        ButtonSseFaceOvDown.Name = "ButtonSseFaceOvDown"
        ButtonSseFaceOvDown.Size = New Size(75, 25)
        ButtonSseFaceOvDown.TabIndex = 3
        ButtonSseFaceOvDown.Text = "Down"
        ' 
        ' GroupBoxSseFaceOvApplied
        ' 
        GroupBoxSseFaceOvApplied.Controls.Add(SseFaceOvRightLayout)
        GroupBoxSseFaceOvApplied.Dock = DockStyle.Fill
        GroupBoxSseFaceOvApplied.Location = New Point(403, 3)
        GroupBoxSseFaceOvApplied.Name = "GroupBoxSseFaceOvApplied"
        GroupBoxSseFaceOvApplied.Size = New Size(406, 636)
        GroupBoxSseFaceOvApplied.TabIndex = 2
        GroupBoxSseFaceOvApplied.TabStop = False
        GroupBoxSseFaceOvApplied.Text = "Applied face overlays"
        ' 
        ' SseFaceOvRightLayout
        ' 
        SseFaceOvRightLayout.ColumnCount = 1
        SseFaceOvRightLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvRightLayout.Controls.Add(ListBoxSseFaceOvApplied, 0, 0)
        SseFaceOvRightLayout.Controls.Add(SseFaceOvDetail, 0, 1)
        SseFaceOvRightLayout.Dock = DockStyle.Fill
        SseFaceOvRightLayout.Location = New Point(3, 19)
        SseFaceOvRightLayout.Name = "SseFaceOvRightLayout"
        SseFaceOvRightLayout.RowCount = 2
        SseFaceOvRightLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 45.0F))
        SseFaceOvRightLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 55.0F))
        SseFaceOvRightLayout.Size = New Size(400, 614)
        SseFaceOvRightLayout.TabIndex = 0
        ' 
        ' ListBoxSseFaceOvApplied
        ' 
        ListBoxSseFaceOvApplied.Dock = DockStyle.Fill
        ListBoxSseFaceOvApplied.DrawMode = DrawMode.OwnerDrawFixed
        ListBoxSseFaceOvApplied.IntegralHeight = False
        ListBoxSseFaceOvApplied.Location = New Point(3, 3)
        ListBoxSseFaceOvApplied.Name = "ListBoxSseFaceOvApplied"
        ListBoxSseFaceOvApplied.Size = New Size(394, 270)
        ListBoxSseFaceOvApplied.TabIndex = 0
        ' 
        ' SseFaceOvDetail
        ' 
        SseFaceOvDetail.AutoScroll = True
        SseFaceOvDetail.ColumnCount = 2
        SseFaceOvDetail.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 62.0F))
        SseFaceOvDetail.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvDetail.Controls.Add(LabelSseFaceOvTexture, 0, 0)
        SseFaceOvDetail.Controls.Add(SseFaceOvDiffuseRow, 1, 0)
        SseFaceOvDetail.Controls.Add(LabelSseFaceOvNormal, 0, 1)
        SseFaceOvDetail.Controls.Add(SseFaceOvNormalRow, 1, 1)
        SseFaceOvDetail.Controls.Add(CheckBoxSseFaceOvTint, 0, 2)
        SseFaceOvDetail.Controls.Add(ButtonSseFaceOvTintColor, 1, 2)
        SseFaceOvDetail.Controls.Add(LabelSseFaceOvOpacity, 0, 3)
        SseFaceOvDetail.Controls.Add(SliderSseFaceOvAlpha, 1, 3)
        SseFaceOvDetail.Controls.Add(FlowSseFaceOvMagic, 1, 4)
        SseFaceOvDetail.Controls.Add(LabelSseFaceOvMagicNote, 1, 5)
        SseFaceOvDetail.Dock = DockStyle.Fill
        SseFaceOvDetail.Location = New Point(3, 279)
        SseFaceOvDetail.Name = "SseFaceOvDetail"
        SseFaceOvDetail.RowCount = 7
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle())
        SseFaceOvDetail.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvDetail.Size = New Size(394, 332)
        SseFaceOvDetail.TabIndex = 1
        ' 
        ' LabelSseFaceOvTexture
        ' 
        LabelSseFaceOvTexture.Anchor = AnchorStyles.Left
        LabelSseFaceOvTexture.AutoSize = True
        LabelSseFaceOvTexture.Location = New Point(3, 13)
        LabelSseFaceOvTexture.Margin = New Padding(3, 9, 3, 0)
        LabelSseFaceOvTexture.Name = "LabelSseFaceOvTexture"
        LabelSseFaceOvTexture.Size = New Size(48, 15)
        LabelSseFaceOvTexture.TabIndex = 0
        LabelSseFaceOvTexture.Text = "Texture:"
        ' 
        ' SseFaceOvDiffuseRow
        ' 
        SseFaceOvDiffuseRow.AutoSize = True
        SseFaceOvDiffuseRow.ColumnCount = 1
        SseFaceOvDiffuseRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvDiffuseRow.Controls.Add(TextBoxSseFaceOvDiffuse, 0, 0)
        SseFaceOvDiffuseRow.Dock = DockStyle.Fill
        SseFaceOvDiffuseRow.Location = New Point(62, 4)
        SseFaceOvDiffuseRow.Margin = New Padding(0, 4, 6, 0)
        SseFaceOvDiffuseRow.Name = "SseFaceOvDiffuseRow"
        SseFaceOvDiffuseRow.RowCount = 1
        SseFaceOvDiffuseRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvDiffuseRow.Size = New Size(326, 29)
        SseFaceOvDiffuseRow.TabIndex = 1
        ' 
        ' TextBoxSseFaceOvDiffuse
        ' 
        TextBoxSseFaceOvDiffuse.Dock = DockStyle.Fill
        TextBoxSseFaceOvDiffuse.Location = New Point(3, 3)
        TextBoxSseFaceOvDiffuse.Name = "TextBoxSseFaceOvDiffuse"
        TextBoxSseFaceOvDiffuse.ReadOnly = True
        TextBoxSseFaceOvDiffuse.Size = New Size(320, 23)
        TextBoxSseFaceOvDiffuse.TabIndex = 0
        ' 
        ' LabelSseFaceOvNormal
        ' 
        LabelSseFaceOvNormal.Anchor = AnchorStyles.Left
        LabelSseFaceOvNormal.AutoSize = True
        LabelSseFaceOvNormal.Location = New Point(3, 46)
        LabelSseFaceOvNormal.Margin = New Padding(3, 9, 3, 0)
        LabelSseFaceOvNormal.Name = "LabelSseFaceOvNormal"
        LabelSseFaceOvNormal.Size = New Size(50, 15)
        LabelSseFaceOvNormal.TabIndex = 2
        LabelSseFaceOvNormal.Text = "Normal:"
        ' 
        ' SseFaceOvNormalRow
        ' 
        SseFaceOvNormalRow.AutoSize = True
        SseFaceOvNormalRow.ColumnCount = 1
        SseFaceOvNormalRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseFaceOvNormalRow.Controls.Add(TextBoxSseFaceOvNormal, 0, 0)
        SseFaceOvNormalRow.Dock = DockStyle.Fill
        SseFaceOvNormalRow.Location = New Point(62, 37)
        SseFaceOvNormalRow.Margin = New Padding(0, 4, 6, 0)
        SseFaceOvNormalRow.Name = "SseFaceOvNormalRow"
        SseFaceOvNormalRow.RowCount = 1
        SseFaceOvNormalRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseFaceOvNormalRow.Size = New Size(326, 29)
        SseFaceOvNormalRow.TabIndex = 3
        ' 
        ' TextBoxSseFaceOvNormal
        ' 
        TextBoxSseFaceOvNormal.Dock = DockStyle.Fill
        TextBoxSseFaceOvNormal.Location = New Point(3, 3)
        TextBoxSseFaceOvNormal.Name = "TextBoxSseFaceOvNormal"
        TextBoxSseFaceOvNormal.ReadOnly = True
        TextBoxSseFaceOvNormal.Size = New Size(320, 23)
        TextBoxSseFaceOvNormal.TabIndex = 0
        ' 
        ' CheckBoxSseFaceOvTint
        ' 
        CheckBoxSseFaceOvTint.AutoSize = True
        CheckBoxSseFaceOvTint.Location = New Point(3, 75)
        CheckBoxSseFaceOvTint.Margin = New Padding(3, 9, 3, 0)
        CheckBoxSseFaceOvTint.Name = "CheckBoxSseFaceOvTint"
        CheckBoxSseFaceOvTint.Size = New Size(47, 19)
        CheckBoxSseFaceOvTint.TabIndex = 4
        CheckBoxSseFaceOvTint.Text = "Tint"
        ' 
        ' ButtonSseFaceOvTintColor
        ' 
        ButtonSseFaceOvTintColor.Anchor = AnchorStyles.Left
        ButtonSseFaceOvTintColor.AutoSize = True
        ButtonSseFaceOvTintColor.Location = New Point(65, 69)
        ButtonSseFaceOvTintColor.Name = "ButtonSseFaceOvTintColor"
        ButtonSseFaceOvTintColor.Size = New Size(75, 25)
        ButtonSseFaceOvTintColor.TabIndex = 5
        ButtonSseFaceOvTintColor.Text = "Color…"
        ' 
        ' LabelSseFaceOvOpacity
        ' 
        LabelSseFaceOvOpacity.Anchor = AnchorStyles.Left
        LabelSseFaceOvOpacity.AutoSize = True
        LabelSseFaceOvOpacity.Location = New Point(3, 110)
        LabelSseFaceOvOpacity.Margin = New Padding(3, 9, 3, 0)
        LabelSseFaceOvOpacity.Name = "LabelSseFaceOvOpacity"
        LabelSseFaceOvOpacity.Size = New Size(51, 15)
        LabelSseFaceOvOpacity.TabIndex = 6
        LabelSseFaceOvOpacity.Text = "Opacity:"
        ' 
        ' SliderSseFaceOvAlpha
        ' 
        SliderSseFaceOvAlpha.AccentColor = SystemColors.HotTrack
        SliderSseFaceOvAlpha.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        SliderSseFaceOvAlpha.BackColor = SystemColors.Control
        SliderSseFaceOvAlpha.DisplayFormat = "0.00"
        SliderSseFaceOvAlpha.LargeChange = 0.1R
        SliderSseFaceOvAlpha.Location = New Point(65, 101)
        SliderSseFaceOvAlpha.Margin = New Padding(3, 4, 8, 3)
        SliderSseFaceOvAlpha.Maximum = 1.0R
        SliderSseFaceOvAlpha.MinimumSize = New Size(100, 24)
        SliderSseFaceOvAlpha.Name = "SliderSseFaceOvAlpha"
        SliderSseFaceOvAlpha.Size = New Size(321, 26)
        SliderSseFaceOvAlpha.SmallChange = 0.01R
        SliderSseFaceOvAlpha.TabIndex = 7
        SliderSseFaceOvAlpha.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseFaceOvAlpha.ThumbColor = SystemColors.HotTrack
        SliderSseFaceOvAlpha.ThumbRadius = 4.0F
        SliderSseFaceOvAlpha.TrackColor = SystemColors.ControlDark
        ' 
        ' FlowSseFaceOvMagic
        ' 
        FlowSseFaceOvMagic.Anchor = AnchorStyles.Left
        FlowSseFaceOvMagic.AutoSize = True
        FlowSseFaceOvMagic.Controls.Add(CheckBoxSseFaceOvMagic)
        FlowSseFaceOvMagic.Location = New Point(62, 130)
        FlowSseFaceOvMagic.Margin = New Padding(0)
        FlowSseFaceOvMagic.Name = "FlowSseFaceOvMagic"
        FlowSseFaceOvMagic.Size = New Size(139, 28)
        FlowSseFaceOvMagic.TabIndex = 8
        FlowSseFaceOvMagic.WrapContents = False
        ' 
        ' CheckBoxSseFaceOvMagic
        ' 
        CheckBoxSseFaceOvMagic.AutoSize = True
        CheckBoxSseFaceOvMagic.Location = New Point(0, 9)
        CheckBoxSseFaceOvMagic.Margin = New Padding(0, 9, 12, 0)
        CheckBoxSseFaceOvMagic.Name = "CheckBoxSseFaceOvMagic"
        CheckBoxSseFaceOvMagic.Size = New Size(127, 19)
        CheckBoxSseFaceOvMagic.TabIndex = 0
        CheckBoxSseFaceOvMagic.Text = "Magic (spell effect)"
        ' 
        ' LabelSseFaceOvMagicNote
        ' 
        LabelSseFaceOvMagicNote.AutoSize = True
        LabelSseFaceOvMagicNote.ForeColor = SystemColors.GrayText
        LabelSseFaceOvMagicNote.Location = New Point(65, 160)
        LabelSseFaceOvMagicNote.Margin = New Padding(3, 2, 3, 0)
        LabelSseFaceOvMagicNote.Name = "LabelSseFaceOvMagicNote"
        LabelSseFaceOvMagicNote.Size = New Size(319, 60)
        LabelSseFaceOvMagicNote.TabIndex = 9
        LabelSseFaceOvMagicNote.Text = resources.GetString("LabelSseFaceOvMagicNote.Text")
        ' 
        ' TabPageSseMorphs
        ' 
        TabPageSseMorphs.Controls.Add(PanelSseMorphs)
        TabPageSseMorphs.Location = New Point(4, 24)
        TabPageSseMorphs.Name = "TabPageSseMorphs"
        TabPageSseMorphs.Padding = New Padding(6)
        TabPageSseMorphs.Size = New Size(830, 690)
        TabPageSseMorphs.TabIndex = 1
        TabPageSseMorphs.Text = "Morphs"
        ' 
        ' PanelSseMorphs
        ' 
        PanelSseMorphs.Dock = DockStyle.Fill
        PanelSseMorphs.Location = New Point(6, 6)
        PanelSseMorphs.Name = "PanelSseMorphs"
        PanelSseMorphs.Size = New Size(818, 678)
        PanelSseMorphs.TabIndex = 0
        ' 
        ' TabPageSseRaceMenu
        ' 
        TabPageSseRaceMenu.Controls.Add(SseRaceMenuRoot)
        TabPageSseRaceMenu.Controls.Add(LabelSseRaceMenuEmpty)
        TabPageSseRaceMenu.Location = New Point(4, 24)
        TabPageSseRaceMenu.Name = "TabPageSseRaceMenu"
        TabPageSseRaceMenu.Padding = New Padding(6)
        TabPageSseRaceMenu.Size = New Size(830, 690)
        TabPageSseRaceMenu.TabIndex = 4
        TabPageSseRaceMenu.Text = "Extra morphs (RM)"
        ' 
        ' SseRaceMenuRoot
        ' 
        SseRaceMenuRoot.ColumnCount = 1
        SseRaceMenuRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        SseRaceMenuRoot.Controls.Add(TextBoxSseRaceMenuFilter, 0, 0)
        SseRaceMenuRoot.Controls.Add(FlowSseRaceMenu, 0, 1)
        SseRaceMenuRoot.Dock = DockStyle.Fill
        SseRaceMenuRoot.Location = New Point(6, 6)
        SseRaceMenuRoot.Name = "SseRaceMenuRoot"
        SseRaceMenuRoot.RowCount = 2
        SseRaceMenuRoot.RowStyles.Add(New RowStyle())
        SseRaceMenuRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        SseRaceMenuRoot.Size = New Size(818, 678)
        SseRaceMenuRoot.TabIndex = 0
        ' 
        ' TextBoxSseRaceMenuFilter
        ' 
        TextBoxSseRaceMenuFilter.Dock = DockStyle.Top
        TextBoxSseRaceMenuFilter.Location = New Point(3, 3)
        TextBoxSseRaceMenuFilter.Name = "TextBoxSseRaceMenuFilter"
        TextBoxSseRaceMenuFilter.PlaceholderText = "Filter sliders…"
        TextBoxSseRaceMenuFilter.Size = New Size(812, 23)
        TextBoxSseRaceMenuFilter.TabIndex = 0
        ' 
        ' FlowSseRaceMenu
        ' 
        FlowSseRaceMenu.AutoScroll = True
        FlowSseRaceMenu.Dock = DockStyle.Fill
        FlowSseRaceMenu.FlowDirection = FlowDirection.TopDown
        FlowSseRaceMenu.Location = New Point(3, 32)
        FlowSseRaceMenu.Name = "FlowSseRaceMenu"
        FlowSseRaceMenu.Padding = New Padding(4)
        FlowSseRaceMenu.Size = New Size(812, 643)
        FlowSseRaceMenu.TabIndex = 1
        FlowSseRaceMenu.WrapContents = False
        ' 
        ' LabelSseRaceMenuEmpty
        ' 
        LabelSseRaceMenuEmpty.Dock = DockStyle.Fill
        LabelSseRaceMenuEmpty.ForeColor = SystemColors.GrayText
        LabelSseRaceMenuEmpty.Location = New Point(6, 6)
        LabelSseRaceMenuEmpty.Name = "LabelSseRaceMenuEmpty"
        LabelSseRaceMenuEmpty.Padding = New Padding(10)
        LabelSseRaceMenuEmpty.Size = New Size(818, 678)
        LabelSseRaceMenuEmpty.TabIndex = 1
        LabelSseRaceMenuEmpty.Visible = False
        ' 
        ' TabPageSseSculpt
        ' 
        TabPageSseSculpt.Controls.Add(ListSseSculpt)
        TabPageSseSculpt.Controls.Add(SseSculptButtonRow)
        TabPageSseSculpt.Location = New Point(4, 24)
        TabPageSseSculpt.Name = "TabPageSseSculpt"
        TabPageSseSculpt.Padding = New Padding(6)
        TabPageSseSculpt.Size = New Size(830, 690)
        TabPageSseSculpt.TabIndex = 3
        TabPageSseSculpt.Text = "Sculpt (RM)"
        ' 
        ' ListSseSculpt
        ' 
        ListSseSculpt.Dock = DockStyle.Fill
        ListSseSculpt.FullRowSelect = True
        ListSseSculpt.Location = New Point(6, 6)
        ListSseSculpt.MultiSelect = False
        ListSseSculpt.Name = "ListSseSculpt"
        ListSseSculpt.Size = New Size(818, 643)
        ListSseSculpt.TabIndex = 0
        ListSseSculpt.UseCompatibleStateImageBehavior = False
        ListSseSculpt.View = View.Details
        ' 
        ' SseSculptButtonRow
        ' 
        SseSculptButtonRow.AutoSize = True
        SseSculptButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SseSculptButtonRow.Controls.Add(ButtonRegenSseMorphs)
        SseSculptButtonRow.Controls.Add(ButtonDeleteSseSculpt)
        SseSculptButtonRow.Dock = DockStyle.Bottom
        SseSculptButtonRow.Location = New Point(6, 649)
        SseSculptButtonRow.Name = "SseSculptButtonRow"
        SseSculptButtonRow.Padding = New Padding(0, 4, 0, 0)
        SseSculptButtonRow.Size = New Size(818, 35)
        SseSculptButtonRow.TabIndex = 1
        SseSculptButtonRow.WrapContents = False
        ' 
        ' ButtonRegenSseMorphs
        ' 
        ButtonRegenSseMorphs.AutoSize = True
        ButtonRegenSseMorphs.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRegenSseMorphs.Location = New Point(3, 7)
        ButtonRegenSseMorphs.Name = "ButtonRegenSseMorphs"
        ButtonRegenSseMorphs.Size = New Size(154, 25)
        ButtonRegenSseMorphs.TabIndex = 0
        ButtonRegenSseMorphs.Text = "Regenerate morphs (Beta)"
        ' 
        ' ButtonDeleteSseSculpt
        ' 
        ButtonDeleteSseSculpt.AutoSize = True
        ButtonDeleteSseSculpt.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonDeleteSseSculpt.Enabled = False
        ButtonDeleteSseSculpt.Location = New Point(163, 7)
        ButtonDeleteSseSculpt.Name = "ButtonDeleteSseSculpt"
        ButtonDeleteSseSculpt.Size = New Size(131, 25)
        ButtonDeleteSseSculpt.TabIndex = 1
        ButtonDeleteSseSculpt.Text = "Delete selected sculpt"
        ' 
        ' TabPageTints
        ' 
        TabPageTints.Controls.Add(TintsLayout)
        TabPageTints.Location = New Point(4, 24)
        TabPageTints.Name = "TabPageTints"
        TabPageTints.Padding = New Padding(6)
        TabPageTints.Size = New Size(830, 690)
        TabPageTints.TabIndex = 1
        TabPageTints.Text = "Tints"
        ' 
        ' TintsLayout
        ' 
        TintsLayout.ColumnCount = 1
        TintsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
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
        TintsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        TintsLayout.RowStyles.Add(New RowStyle())
        TintsLayout.RowStyles.Add(New RowStyle())
        TintsLayout.Size = New Size(818, 678)
        TintsLayout.TabIndex = 0
        ' 
        ' TextBoxTintFilter
        ' 
        TextBoxTintFilter.Dock = DockStyle.Fill
        TextBoxTintFilter.Location = New Point(7, 7)
        TextBoxTintFilter.Name = "TextBoxTintFilter"
        TextBoxTintFilter.PlaceholderText = "Filter by group or layer name…"
        TextBoxTintFilter.Size = New Size(804, 23)
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
        ListViewTints.Size = New Size(804, 434)
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
        TintsButtonRow.Location = New Point(7, 476)
        TintsButtonRow.Name = "TintsButtonRow"
        TintsButtonRow.Size = New Size(804, 31)
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
        ButtonRemoveAllInCategory.Size = New Size(124, 25)
        ButtonRemoveAllInCategory.TabIndex = 2
        ButtonRemoveAllInCategory.Text = "Remove all category"
        ' 
        ' ButtonRemoveZeroedTints
        ' 
        ButtonRemoveZeroedTints.AutoSize = True
        ButtonRemoveZeroedTints.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRemoveZeroedTints.Location = New Point(295, 3)
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
        PanelTintDetail.Location = New Point(7, 513)
        PanelTintDetail.Name = "PanelTintDetail"
        PanelTintDetail.Size = New Size(804, 158)
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
        TintDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
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
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 20.0F))
        TintDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 36.0F))
        TintDetailLayout.Size = New Size(798, 136)
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
        LabelTintLayerName.Font = New Font("Segoe UI", 9.0F, FontStyle.Bold)
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
        ComboBoxTintPalette.Size = New Size(623, 23)
        ComboBoxTintPalette.TabIndex = 3
        ' 
        ' ButtonTintCustomRGB
        ' 
        ButtonTintCustomRGB.Dock = DockStyle.Fill
        ButtonTintCustomRGB.Location = New Point(695, 43)
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
        PanelTintColorSwatch.Size = New Size(623, 16)
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
        TrackBarTintPercent.Size = New Size(621, 30)
        TrackBarTintPercent.TabIndex = 7
        TrackBarTintPercent.TextBoxTextAlign = HorizontalAlignment.Right
        TrackBarTintPercent.ThumbColor = SystemColors.HotTrack
        TrackBarTintPercent.ThumbRadius = 4.0F
        TrackBarTintPercent.TrackColor = SystemColors.ControlDark
        ' 
        ' TabPageBoneRegions
        ' 
        TabPageBoneRegions.Controls.Add(BoneRegionsRoot)
        TabPageBoneRegions.Location = New Point(4, 24)
        TabPageBoneRegions.Name = "TabPageBoneRegions"
        TabPageBoneRegions.Padding = New Padding(6)
        TabPageBoneRegions.Size = New Size(830, 690)
        TabPageBoneRegions.TabIndex = 3
        TabPageBoneRegions.Text = "Bone Morphs"
        ' 
        ' BoneRegionsRoot
        ' 
        BoneRegionsRoot.ColumnCount = 1
        BoneRegionsRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        BoneRegionsRoot.Controls.Add(BoneRegionsContainer, 0, 0)
        BoneRegionsRoot.Controls.Add(GroupBoxFmin, 0, 1)
        BoneRegionsRoot.Dock = DockStyle.Fill
        BoneRegionsRoot.Location = New Point(6, 6)
        BoneRegionsRoot.Name = "BoneRegionsRoot"
        BoneRegionsRoot.RowCount = 2
        BoneRegionsRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        BoneRegionsRoot.RowStyles.Add(New RowStyle())
        BoneRegionsRoot.Size = New Size(818, 678)
        BoneRegionsRoot.TabIndex = 0
        ' 
        ' BoneRegionsContainer
        ' 
        BoneRegionsContainer.Controls.Add(BoneRegionsTabs)
        BoneRegionsContainer.Controls.Add(LabelBoneRegionsEmpty)
        BoneRegionsContainer.Dock = DockStyle.Fill
        BoneRegionsContainer.Location = New Point(3, 3)
        BoneRegionsContainer.Name = "BoneRegionsContainer"
        BoneRegionsContainer.Size = New Size(812, 606)
        BoneRegionsContainer.TabIndex = 0
        ' 
        ' BoneRegionsTabs
        ' 
        BoneRegionsTabs.Dock = DockStyle.Fill
        BoneRegionsTabs.Location = New Point(0, 0)
        BoneRegionsTabs.Name = "BoneRegionsTabs"
        BoneRegionsTabs.SelectedIndex = 0
        BoneRegionsTabs.Size = New Size(812, 606)
        BoneRegionsTabs.TabIndex = 0
        ' 
        ' LabelBoneRegionsEmpty
        ' 
        LabelBoneRegionsEmpty.AutoSize = True
        LabelBoneRegionsEmpty.ForeColor = Color.Gray
        LabelBoneRegionsEmpty.Location = New Point(0, 0)
        LabelBoneRegionsEmpty.Name = "LabelBoneRegionsEmpty"
        LabelBoneRegionsEmpty.Padding = New Padding(8)
        LabelBoneRegionsEmpty.Size = New Size(16, 31)
        LabelBoneRegionsEmpty.TabIndex = 1
        LabelBoneRegionsEmpty.Visible = False
        ' 
        ' GroupBoxFmin
        ' 
        GroupBoxFmin.AutoSize = True
        GroupBoxFmin.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxFmin.Controls.Add(FminLayout)
        GroupBoxFmin.Dock = DockStyle.Fill
        GroupBoxFmin.Location = New Point(3, 615)
        GroupBoxFmin.Name = "GroupBoxFmin"
        GroupBoxFmin.Size = New Size(812, 60)
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
        FminLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        FminLayout.ColumnStyles.Add(New ColumnStyle())
        FminLayout.Controls.Add(LabelFminCaption, 0, 0)
        FminLayout.Controls.Add(TrackBarFmin, 1, 0)
        FminLayout.Dock = DockStyle.Fill
        FminLayout.Location = New Point(3, 19)
        FminLayout.Name = "FminLayout"
        FminLayout.Padding = New Padding(4)
        FminLayout.RowCount = 1
        FminLayout.RowStyles.Add(New RowStyle())
        FminLayout.Size = New Size(806, 38)
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
        TrackBarFmin.AllowExtremeValues = True
        TrackBarFmin.BackColor = SystemColors.Control
        TrackBarFmin.DisplayFormat = "0.00%"
        TrackBarFmin.Dock = DockStyle.Fill
        TrackBarFmin.InputScale = 0.01R
        TrackBarFmin.LargeChange = 0.25R
        TrackBarFmin.Location = New Point(97, 7)
        TrackBarFmin.Maximum = 4.0R
        TrackBarFmin.MinimumSize = New Size(100, 24)
        TrackBarFmin.Name = "TrackBarFmin"
        TrackBarFmin.Size = New Size(702, 24)
        TrackBarFmin.SmallChange = 0.01R
        TrackBarFmin.TabIndex = 1
        TrackBarFmin.TextBoxTextAlign = HorizontalAlignment.Right
        TrackBarFmin.ThumbColor = SystemColors.HotTrack
        TrackBarFmin.ThumbRadius = 4.0F
        TrackBarFmin.TrackColor = SystemColors.ControlDark
        ' 
        ' TabPageVertex
        ' 
        TabPageVertex.Controls.Add(VertexMorphsPanel)
        TabPageVertex.Location = New Point(4, 24)
        TabPageVertex.Name = "TabPageVertex"
        TabPageVertex.Padding = New Padding(6)
        TabPageVertex.Size = New Size(830, 690)
        TabPageVertex.TabIndex = 2
        TabPageVertex.Text = "Vertex Morphs"
        ' 
        ' VertexMorphsPanel
        ' 
        VertexMorphsPanel.Dock = DockStyle.Fill
        VertexMorphsPanel.Location = New Point(6, 6)
        VertexMorphsPanel.Name = "VertexMorphsPanel"
        VertexMorphsPanel.Size = New Size(818, 678)
        VertexMorphsPanel.TabIndex = 0
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
        BottomLayout.Location = New Point(11, 735)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(838, 35)
        BottomLayout.TabIndex = 1
        ' 
        ' ButtonOk
        ' 
        ButtonOk.Location = New Point(755, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Location = New Point(669, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' ButtonResetSection
        ' 
        ButtonResetSection.Location = New Point(553, 9)
        ButtonResetSection.Name = "ButtonResetSection"
        ButtonResetSection.Size = New Size(110, 23)
        ButtonResetSection.TabIndex = 2
        ButtonResetSection.Text = "Reset section"
        ' 
        ' PreviewSidebar
        ' 
        PreviewSidebar.ColumnCount = 1
        PreviewSidebar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        PreviewSidebar.Controls.Add(RenderTogglesPanel, 0, 0)
        PreviewSidebar.Controls.Add(PreviewHostPanel, 0, 1)
        PreviewSidebar.Dock = DockStyle.Fill
        PreviewSidebar.Location = New Point(0, 0)
        PreviewSidebar.Name = "PreviewSidebar"
        PreviewSidebar.RowCount = 2
        PreviewSidebar.RowStyles.Add(New RowStyle())
        PreviewSidebar.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        PreviewSidebar.Size = New Size(696, 781)
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
        RenderTogglesPanel.Size = New Size(690, 27)
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
        PreviewHostPanel.Size = New Size(690, 742)
        PreviewHostPanel.TabIndex = 0
        ' 
        ' EditFace_Form
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(1560, 781)
        Controls.Add(PreviewSplit)
        MinimumSize = New Size(1340, 820)
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
        HairColorLayout.PerformLayout()
        PanelSseCustomHair.ResumeLayout(False)
        PanelSseCustomHair.PerformLayout()
        GroupBoxFaceFlags.ResumeLayout(False)
        GroupBoxFaceFlags.PerformLayout()
        FaceFlagsLayout.ResumeLayout(False)
        FaceFlagsLayout.PerformLayout()
        GroupBoxSseHeadTexture.ResumeLayout(False)
        GroupBoxSseHeadTexture.PerformLayout()
        FlowSseHeadTex.ResumeLayout(False)
        FlowSseHeadTex.PerformLayout()
        TabPageSseTints.ResumeLayout(False)
        PanelSseTints.ResumeLayout(False)
        SseTintSplit.ResumeLayout(False)
        SseTintListHost.ResumeLayout(False)
        GroupBoxSseTintDetail.ResumeLayout(False)
        PanelSseTintDetail.ResumeLayout(False)
        PanelSseTintDetail.PerformLayout()
        SseTintDetailLayout.ResumeLayout(False)
        SseTintColorRow.ResumeLayout(False)
        SseTintMaskButtons.ResumeLayout(False)
        SseTintMaskButtons.PerformLayout()
        TabPageSseFaceOverlays.ResumeLayout(False)
        SseFaceOvRoot.ResumeLayout(False)
        SseFaceOvBody.ResumeLayout(False)
        SseFaceOvBody.PerformLayout()
        GroupBoxSseFacePaints.ResumeLayout(False)
        SseFaceOvCatalogLayout.ResumeLayout(False)
        SseFaceOvCatalogLayout.PerformLayout()
        FlowSseFaceOvButtons.ResumeLayout(False)
        FlowSseFaceOvButtons.PerformLayout()
        GroupBoxSseFaceOvApplied.ResumeLayout(False)
        SseFaceOvRightLayout.ResumeLayout(False)
        SseFaceOvDetail.ResumeLayout(False)
        SseFaceOvDetail.PerformLayout()
        SseFaceOvDiffuseRow.ResumeLayout(False)
        SseFaceOvDiffuseRow.PerformLayout()
        SseFaceOvNormalRow.ResumeLayout(False)
        SseFaceOvNormalRow.PerformLayout()
        FlowSseFaceOvMagic.ResumeLayout(False)
        FlowSseFaceOvMagic.PerformLayout()
        TabPageSseMorphs.ResumeLayout(False)
        TabPageSseRaceMenu.ResumeLayout(False)
        SseRaceMenuRoot.ResumeLayout(False)
        SseRaceMenuRoot.PerformLayout()
        TabPageSseSculpt.ResumeLayout(False)
        TabPageSseSculpt.PerformLayout()
        SseSculptButtonRow.ResumeLayout(False)
        SseSculptButtonRow.PerformLayout()
        TabPageTints.ResumeLayout(False)
        TintsLayout.ResumeLayout(False)
        TintsLayout.PerformLayout()
        TintsButtonRow.ResumeLayout(False)
        TintsButtonRow.PerformLayout()
        PanelTintDetail.ResumeLayout(False)
        PanelTintDetail.PerformLayout()
        TintDetailLayout.ResumeLayout(False)
        TintDetailLayout.PerformLayout()
        TabPageBoneRegions.ResumeLayout(False)
        BoneRegionsRoot.ResumeLayout(False)
        BoneRegionsRoot.PerformLayout()
        BoneRegionsContainer.ResumeLayout(False)
        BoneRegionsContainer.PerformLayout()
        GroupBoxFmin.ResumeLayout(False)
        GroupBoxFmin.PerformLayout()
        FminLayout.ResumeLayout(False)
        FminLayout.PerformLayout()
        TabPageVertex.ResumeLayout(False)
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
    Friend WithEvents TabPageSseMorphs As System.Windows.Forms.TabPage
    Friend WithEvents PanelSseMorphs As System.Windows.Forms.Panel
    Friend WithEvents TabPageSseSculpt As System.Windows.Forms.TabPage
    Friend WithEvents ListSseSculpt As System.Windows.Forms.ListView
    Friend WithEvents SseSculptButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonRegenSseMorphs As System.Windows.Forms.Button
    Friend WithEvents ButtonDeleteSseSculpt As System.Windows.Forms.Button
    Friend WithEvents TabPageSseTints As System.Windows.Forms.TabPage
    Friend WithEvents PanelSseTints As System.Windows.Forms.Panel
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
    Friend WithEvents PanelSseCustomHair As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSseCustomHairColor As System.Windows.Forms.Button
    Friend WithEvents ButtonSseCustomHairClear As System.Windows.Forms.Button
    Friend WithEvents LabelSseCustomHair As System.Windows.Forms.Label

    Friend WithEvents GroupBoxFaceFlags As System.Windows.Forms.GroupBox
    Friend WithEvents FaceFlagsLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckBoxIsCharGenFacePreset As System.Windows.Forms.CheckBox
    Friend WithEvents LabelCharGenHelp As System.Windows.Forms.Label

    Friend WithEvents GroupBoxSseHeadTexture As System.Windows.Forms.GroupBox
    Friend WithEvents FlowSseHeadTex As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelSseHeadTex As System.Windows.Forms.Label
    Friend WithEvents ButtonSseHeadTexPick As System.Windows.Forms.Button
    Friend WithEvents ButtonSseHeadTexDefault As System.Windows.Forms.Button
    Friend WithEvents ButtonSseHeadTexClear As System.Windows.Forms.Button

    Friend WithEvents TabPageSseRaceMenu As System.Windows.Forms.TabPage
    Friend WithEvents SseRaceMenuRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseRaceMenuFilter As System.Windows.Forms.TextBox
    Friend WithEvents FlowSseRaceMenu As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelSseRaceMenuEmpty As System.Windows.Forms.Label

    Friend WithEvents TabPageSseFaceOverlays As System.Windows.Forms.TabPage
    Friend WithEvents SseFaceOvRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseFaceOvHeader As System.Windows.Forms.Label
    Friend WithEvents SseFaceOvBody As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxSseFacePaints As System.Windows.Forms.GroupBox
    Friend WithEvents SseFaceOvCatalogLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseFaceOvFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListBoxSseFacePaintCatalog As System.Windows.Forms.ListBox
    Friend WithEvents FlowSseFaceOvButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSseFaceOvAdd As System.Windows.Forms.Button
    Friend WithEvents ButtonSseFaceOvRemove As System.Windows.Forms.Button
    Friend WithEvents ButtonSseFaceOvUp As System.Windows.Forms.Button
    Friend WithEvents ButtonSseFaceOvDown As System.Windows.Forms.Button
    Friend WithEvents GroupBoxSseFaceOvApplied As System.Windows.Forms.GroupBox
    Friend WithEvents SseFaceOvRightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListBoxSseFaceOvApplied As System.Windows.Forms.ListBox
    Friend WithEvents SseFaceOvDetail As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseFaceOvTexture As System.Windows.Forms.Label
    Friend WithEvents SseFaceOvDiffuseRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseFaceOvDiffuse As System.Windows.Forms.TextBox
    Friend WithEvents LabelSseFaceOvNormal As System.Windows.Forms.Label
    Friend WithEvents SseFaceOvNormalRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseFaceOvNormal As System.Windows.Forms.TextBox
    Friend WithEvents CheckBoxSseFaceOvTint As System.Windows.Forms.CheckBox
    Friend WithEvents ButtonSseFaceOvTintColor As System.Windows.Forms.Button
    Friend WithEvents LabelSseFaceOvOpacity As System.Windows.Forms.Label
    Friend WithEvents SliderSseFaceOvAlpha As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents FlowSseFaceOvMagic As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckBoxSseFaceOvMagic As System.Windows.Forms.CheckBox
    Friend WithEvents LabelSseFaceOvMagicNote As System.Windows.Forms.Label

    Friend WithEvents SseTintSplit As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents SseTintListHost As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseTintLayers As System.Windows.Forms.Label
    Friend WithEvents ListBoxSseTintLayers As DoubleBufferedListBox
    Friend WithEvents GroupBoxSseTintDetail As System.Windows.Forms.GroupBox
    Friend WithEvents PanelSseTintDetail As System.Windows.Forms.Panel
    Friend WithEvents SseTintDetailLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseTintColorSourceCaption As System.Windows.Forms.Label
    Friend WithEvents ComboBoxSseTintPreset As System.Windows.Forms.ComboBox
    Friend WithEvents LabelSseTintColorCaption As System.Windows.Forms.Label
    Friend WithEvents SseTintColorRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonSseTintSwatch As System.Windows.Forms.Button
    Friend WithEvents ButtonSseTintCustom As System.Windows.Forms.Button
    Friend WithEvents LabelSseTintCoverageCaption As System.Windows.Forms.Label
    Friend WithEvents SliderSseTintCoverage As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseTintMaskCaption As System.Windows.Forms.Label
    Friend WithEvents LabelSseTintMask As System.Windows.Forms.Label
    Friend WithEvents SseTintMaskButtons As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonSseTintMaskPick As System.Windows.Forms.Button
    Friend WithEvents ButtonSseTintMaskClear As System.Windows.Forms.Button
    Friend WithEvents ButtonSseTintReset As System.Windows.Forms.Button
    Friend WithEvents ButtonSseTintResetAll As System.Windows.Forms.Button
    Friend WithEvents LabelSseTintEmpty As System.Windows.Forms.Label
    Friend WithEvents ToolTipSseTint As System.Windows.Forms.ToolTip

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
    Friend WithEvents BoneRegionsTabs As System.Windows.Forms.TabControl
    Friend WithEvents LabelBoneRegionsEmpty As System.Windows.Forms.Label

    Friend WithEvents GroupBoxFmin As System.Windows.Forms.GroupBox
    Friend WithEvents FminLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFminCaption As System.Windows.Forms.Label
    Friend WithEvents TrackBarFmin As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents TableLayoutPanel1 As TableLayoutPanel
End Class
