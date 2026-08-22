' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class EditBody_Form
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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(EditBody_Form))
        PreviewSplit = New SplitContainer()
        RootLayout = New TableLayoutPanel()
        TabsBody = New TabControl()
        TabPageBody = New TabPage()
        BodyTabLayout = New TableLayoutPanel()
        GroupBoxWeight = New GroupBox()
        WeightLayout = New TableLayoutPanel()
        WeightTriangle = New WeightTriangleControl()
        WeightLegend = New TableLayoutPanel()
        LabelMuscular = New Label()
        SliderMuscular = New TinySliderTextBox()
        LabelThin = New Label()
        SliderThin = New TinySliderTextBox()
        LabelFat = New Label()
        SliderFat = New TinySliderTextBox()
        GroupBoxMrsv = New GroupBox()
        MrsvLayout = New TableLayoutPanel()
        GroupBoxHeight = New GroupBox()
        HeightLayout = New TableLayoutPanel()
        LabelHeightMin = New Label()
        SliderHeightMin = New TinySliderTextBox()
        LabelHeightMax = New Label()
        SliderHeightMax = New TinySliderTextBox()
        GroupBoxSkin = New GroupBox()
        SkinLayout = New TableLayoutPanel()
        LabelWnam = New Label()
        WnamPickPanel = New TableLayoutPanel()
        ComboBoxWnam = New ComboBox()
        ButtonPickWnam = New Button()
        LabelLmSkinTemplate = New Label()
        ComboBoxLmSkinTemplate = New ComboBox()
        GroupBoxSseWeight = New GroupBox()
        SseWeightLayout = New TableLayoutPanel()
        LabelSseWeightNote = New Label()
        LabelSseWeight = New Label()
        SliderSseWeight = New TinySliderTextBox()
        TabPageBodySlide = New TabPage()
        BodySlideTabLayout = New TableLayoutPanel()
        GroupBoxBodySlide = New GroupBox()
        BodySlideLayout = New TableLayoutPanel()
        BodySlidePresetLayout = New TableLayoutPanel()
        ComboBoxBsPreset = New ComboBox()
        ComboBoxBsSize = New ComboBox()
        ButtonBsPresetClear = New Button()
        ButtonBsPresetBrowse = New Button()
        TextBoxBodySlideFilter = New TextBox()
        BodySlidePanel = New FlowLayoutPanel()
        LabelBodySlideEmpty = New Label()
        TabPageSseBodyScale = New TabPage()
        SseBodyScaleRoot = New TableLayoutPanel()
        SseNodeLeftCol = New TableLayoutPanel()
        CheckBoxSseShowAllNodes = New CheckBox()
        TextBoxSseNodeFilter = New TextBox()
        ListBoxSseNodes = New ListBox()
        PanelSseNodeDetail = New Panel()
        FlowSseNodeButtons = New FlowLayoutPanel()
        ButtonSseNodeReset = New Button()
        SseNodeDetailLayout = New TableLayoutPanel()
        LabelSseNodeScale = New Label()
        SliderSseNodeScale = New TinySliderTextBox()
        LabelSseNodePosX = New Label()
        SliderSseNodePosX = New TinySliderTextBox()
        LabelSseNodePosY = New Label()
        SliderSseNodePosY = New TinySliderTextBox()
        LabelSseNodePosZ = New Label()
        SliderSseNodePosZ = New TinySliderTextBox()
        LabelSseNodeRotX = New Label()
        SliderSseNodeRotX = New TinySliderTextBox()
        LabelSseNodeRotY = New Label()
        SliderSseNodeRotY = New TinySliderTextBox()
        LabelSseNodeRotZ = New Label()
        SliderSseNodeRotZ = New TinySliderTextBox()
        LabelSseNodeNote = New Label()
        TabPageSseSkinOverrides = New TabPage()
        SseSkinRoot = New TableLayoutPanel()
        LabelSseSkinHeader = New Label()
        SseSkinLeftPanel = New TableLayoutPanel()
        ListBoxSseSkinOverrides = New ListBox()
        FlowSseSkinButtons = New FlowLayoutPanel()
        ButtonSseSkinAdd = New Button()
        ButtonSseSkinRemove = New Button()
        GroupBoxSseSkinSlots = New GroupBox()
        FlowSseSkinSlots = New FlowLayoutPanel()
        SseSkinDetail = New TableLayoutPanel()
        LabelSseSkinTex0 = New Label()
        TextBoxSseSkinTex0 = New TextBox()
        ButtonSseSkinTexPick0 = New Button()
        ButtonSseSkinTexClear0 = New Button()
        LabelSseSkinTex1 = New Label()
        TextBoxSseSkinTex1 = New TextBox()
        ButtonSseSkinTexPick1 = New Button()
        ButtonSseSkinTexClear1 = New Button()
        LabelSseSkinTex2 = New Label()
        TextBoxSseSkinTex2 = New TextBox()
        ButtonSseSkinTexPick2 = New Button()
        ButtonSseSkinTexClear2 = New Button()
        LabelSseSkinTex7 = New Label()
        TextBoxSseSkinTex7 = New TextBox()
        ButtonSseSkinTexPick7 = New Button()
        ButtonSseSkinTexClear7 = New Button()
        CheckBoxSseSkinTint = New CheckBox()
        ButtonSseSkinTintColor = New Button()
        LabelSseSkinOpacity = New Label()
        SliderSseSkinAlpha = New TinySliderTextBox()
        TabPageOverlays = New TabPage()
        OverlaysTabLayout = New TableLayoutPanel()
        OverlayListsLayout = New TableLayoutPanel()
        GroupBoxOverlayAvailable = New GroupBox()
        OverlayAvailableLayout = New TableLayoutPanel()
        TextBoxOverlayFilter = New TextBox()
        ListBoxOverlayAvailable = New ListBox()
        OverlayCenterLayout = New TableLayoutPanel()
        FlowSseOverlayZone = New FlowLayoutPanel()
        LabelSseOverlayZone = New Label()
        ComboBoxSseOverlayZone = New ComboBox()
        ButtonOverlayAdd = New Button()
        ButtonOverlayRemove = New Button()
        GroupBoxOverlayApplied = New GroupBox()
        OverlayAppliedLayout = New TableLayoutPanel()
        ListBoxOverlayApplied = New ListBox()
        OverlayAppliedButtons = New FlowLayoutPanel()
        ButtonOverlayUp = New Button()
        ButtonOverlayDown = New Button()
        GroupBoxOverlayProps = New GroupBox()
        OverlayPropsLayout = New TableLayoutPanel()
        LabelOverlaySelected = New Label()
        LabelOverlayOffsetU = New Label()
        SliderOverlayOffsetU = New TinySliderTextBox()
        LabelOverlayOffsetV = New Label()
        SliderOverlayOffsetV = New TinySliderTextBox()
        LabelOverlayScaleU = New Label()
        SliderOverlayScaleU = New TinySliderTextBox()
        LabelOverlayScaleV = New Label()
        SliderOverlayScaleV = New TinySliderTextBox()
        CheckBoxOverlayTint = New CheckBox()
        OverlayTintRowLayout = New TableLayoutPanel()
        ButtonOverlayTintColor = New Button()
        LabelOverlayTintAlpha = New Label()
        SliderOverlayTintAlpha = New TinySliderTextBox()
        LabelSseOverlayTexture = New Label()
        SseOverlayDiffuseRow = New TableLayoutPanel()
        TextBoxSseOverlayDiffuse = New TextBox()
        LabelSseOverlayNormal = New Label()
        SseOverlayNormalRow = New TableLayoutPanel()
        TextBoxSseOverlayNormal = New TextBox()
        CheckBoxSseOverlayMagic = New CheckBox()
        LabelSseOverlayMagicNote = New Label()
        TabPageSkinTint = New TabPage()
        SkinTintPanelBody = New SkinTintPanel()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        ButtonResetSection = New Button()
        PreviewSidebar = New TableLayoutPanel()
        RenderTogglesPanel = New FlowLayoutPanel()
        CheckBoxRenderUnderarmor = New CheckBox()
        CheckBoxRenderArmor = New CheckBox()
        CheckBoxRenderHeadwear = New CheckBox()
        CheckBoxRenderGore = New CheckBox()
        PreviewHostPanel = New Panel()
        ToolTipSseNode = New ToolTip(components)
        ToolTipSseSkin = New ToolTip(components)
        CType(PreviewSplit, ComponentModel.ISupportInitialize).BeginInit()
        PreviewSplit.Panel1.SuspendLayout()
        PreviewSplit.Panel2.SuspendLayout()
        PreviewSplit.SuspendLayout()
        RootLayout.SuspendLayout()
        TabsBody.SuspendLayout()
        TabPageBody.SuspendLayout()
        BodyTabLayout.SuspendLayout()
        GroupBoxWeight.SuspendLayout()
        WeightLayout.SuspendLayout()
        WeightLegend.SuspendLayout()
        GroupBoxMrsv.SuspendLayout()
        GroupBoxHeight.SuspendLayout()
        HeightLayout.SuspendLayout()
        GroupBoxSkin.SuspendLayout()
        SkinLayout.SuspendLayout()
        WnamPickPanel.SuspendLayout()
        GroupBoxSseWeight.SuspendLayout()
        SseWeightLayout.SuspendLayout()
        TabPageBodySlide.SuspendLayout()
        BodySlideTabLayout.SuspendLayout()
        GroupBoxBodySlide.SuspendLayout()
        BodySlideLayout.SuspendLayout()
        BodySlidePresetLayout.SuspendLayout()
        TabPageSseBodyScale.SuspendLayout()
        SseBodyScaleRoot.SuspendLayout()
        SseNodeLeftCol.SuspendLayout()
        PanelSseNodeDetail.SuspendLayout()
        FlowSseNodeButtons.SuspendLayout()
        SseNodeDetailLayout.SuspendLayout()
        TabPageSseSkinOverrides.SuspendLayout()
        SseSkinRoot.SuspendLayout()
        SseSkinLeftPanel.SuspendLayout()
        FlowSseSkinButtons.SuspendLayout()
        GroupBoxSseSkinSlots.SuspendLayout()
        SseSkinDetail.SuspendLayout()
        TabPageOverlays.SuspendLayout()
        OverlaysTabLayout.SuspendLayout()
        OverlayListsLayout.SuspendLayout()
        GroupBoxOverlayAvailable.SuspendLayout()
        OverlayAvailableLayout.SuspendLayout()
        OverlayCenterLayout.SuspendLayout()
        FlowSseOverlayZone.SuspendLayout()
        GroupBoxOverlayApplied.SuspendLayout()
        OverlayAppliedLayout.SuspendLayout()
        OverlayAppliedButtons.SuspendLayout()
        GroupBoxOverlayProps.SuspendLayout()
        OverlayPropsLayout.SuspendLayout()
        OverlayTintRowLayout.SuspendLayout()
        SseOverlayDiffuseRow.SuspendLayout()
        SseOverlayNormalRow.SuspendLayout()
        TabPageSkinTint.SuspendLayout()
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
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TabsBody, 0, 0)
        RootLayout.Controls.Add(BottomLayout, 0, 1)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 2
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(860, 781)
        RootLayout.TabIndex = 0
        ' 
        ' TabsBody
        ' 
        TabsBody.Controls.Add(TabPageBody)
        TabsBody.Controls.Add(TabPageBodySlide)
        TabsBody.Controls.Add(TabPageSseBodyScale)
        TabsBody.Controls.Add(TabPageSseSkinOverrides)
        TabsBody.Controls.Add(TabPageOverlays)
        TabsBody.Controls.Add(TabPageSkinTint)
        TabsBody.Dock = DockStyle.Fill
        TabsBody.Location = New Point(11, 11)
        TabsBody.Name = "TabsBody"
        TabsBody.SelectedIndex = 0
        TabsBody.Size = New Size(838, 720)
        TabsBody.TabIndex = 0
        ' 
        ' TabPageBody
        ' 
        TabPageBody.Controls.Add(BodyTabLayout)
        TabPageBody.Location = New Point(4, 24)
        TabPageBody.Name = "TabPageBody"
        TabPageBody.Padding = New Padding(6)
        TabPageBody.Size = New Size(830, 692)
        TabPageBody.TabIndex = 0
        TabPageBody.Text = "Body"
        ' 
        ' BodyTabLayout
        ' 
        BodyTabLayout.AutoScroll = True
        BodyTabLayout.ColumnCount = 1
        BodyTabLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodyTabLayout.Controls.Add(GroupBoxWeight, 0, 0)
        BodyTabLayout.Controls.Add(GroupBoxMrsv, 0, 1)
        BodyTabLayout.Controls.Add(GroupBoxHeight, 0, 2)
        BodyTabLayout.Controls.Add(GroupBoxSkin, 0, 3)
        BodyTabLayout.Controls.Add(GroupBoxSseWeight, 0, 4)
        BodyTabLayout.Dock = DockStyle.Fill
        BodyTabLayout.Location = New Point(6, 6)
        BodyTabLayout.Name = "BodyTabLayout"
        BodyTabLayout.RowCount = 5
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.Size = New Size(818, 680)
        BodyTabLayout.TabIndex = 0
        ' 
        ' GroupBoxWeight
        ' 
        GroupBoxWeight.AutoSize = True
        GroupBoxWeight.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxWeight.Controls.Add(WeightLayout)
        GroupBoxWeight.Dock = DockStyle.Fill
        GroupBoxWeight.Location = New Point(3, 3)
        GroupBoxWeight.Name = "GroupBoxWeight"
        GroupBoxWeight.Size = New Size(812, 214)
        GroupBoxWeight.TabIndex = 0
        GroupBoxWeight.TabStop = False
        GroupBoxWeight.Text = "Weight (NPC.MWGT — applied via bone scaling)"
        ' 
        ' WeightLayout
        ' 
        WeightLayout.AutoSize = True
        WeightLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        WeightLayout.ColumnCount = 2
        WeightLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        WeightLayout.ColumnStyles.Add(New ColumnStyle())
        WeightLayout.Controls.Add(WeightTriangle, 0, 0)
        WeightLayout.Controls.Add(WeightLegend, 1, 0)
        WeightLayout.Dock = DockStyle.Fill
        WeightLayout.Location = New Point(3, 19)
        WeightLayout.Name = "WeightLayout"
        WeightLayout.Padding = New Padding(4)
        WeightLayout.RowCount = 1
        WeightLayout.RowStyles.Add(New RowStyle())
        WeightLayout.Size = New Size(806, 192)
        WeightLayout.TabIndex = 0
        ' 
        ' WeightTriangle
        ' 
        WeightTriangle.BackColor = SystemColors.Control
        WeightTriangle.Dock = DockStyle.Fill
        WeightTriangle.Location = New Point(6, 6)
        WeightTriangle.Margin = New Padding(2)
        WeightTriangle.MinimumSize = New Size(220, 180)
        WeightTriangle.Name = "WeightTriangle"
        WeightTriangle.Size = New Size(555, 180)
        WeightTriangle.TabIndex = 0
        ' 
        ' WeightLegend
        ' 
        WeightLegend.Anchor = AnchorStyles.None
        WeightLegend.AutoSize = True
        WeightLegend.AutoSizeMode = AutoSizeMode.GrowAndShrink
        WeightLegend.ColumnCount = 2
        WeightLegend.ColumnStyles.Add(New ColumnStyle())
        WeightLegend.ColumnStyles.Add(New ColumnStyle())
        WeightLegend.Controls.Add(LabelMuscular, 0, 0)
        WeightLegend.Controls.Add(SliderMuscular, 1, 0)
        WeightLegend.Controls.Add(LabelThin, 0, 1)
        WeightLegend.Controls.Add(SliderThin, 1, 1)
        WeightLegend.Controls.Add(LabelFat, 0, 2)
        WeightLegend.Controls.Add(SliderFat, 1, 2)
        WeightLegend.Location = New Point(571, 42)
        WeightLegend.Margin = New Padding(8, 2, 2, 2)
        WeightLegend.Name = "WeightLegend"
        WeightLegend.RowCount = 3
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.Size = New Size(229, 108)
        WeightLegend.TabIndex = 1
        ' 
        ' LabelMuscular
        ' 
        LabelMuscular.Anchor = AnchorStyles.Left
        LabelMuscular.AutoSize = True
        LabelMuscular.Location = New Point(3, 10)
        LabelMuscular.Name = "LabelMuscular"
        LabelMuscular.Size = New Size(59, 15)
        LabelMuscular.TabIndex = 0
        LabelMuscular.Text = "Muscular:"
        LabelMuscular.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderMuscular
        ' 
        SliderMuscular.AccentColor = SystemColors.HotTrack
        SliderMuscular.Anchor = AnchorStyles.None
        SliderMuscular.BackColor = SystemColors.Control
        SliderMuscular.DisplayFormat = "0.00%"
        SliderMuscular.InputScale = 0.01R
        SliderMuscular.LargeChange = 0.1R
        SliderMuscular.Location = New Point(67, 4)
        SliderMuscular.Margin = New Padding(2, 4, 2, 4)
        SliderMuscular.Maximum = 1R
        SliderMuscular.MinimumSize = New Size(140, 28)
        SliderMuscular.Name = "SliderMuscular"
        SliderMuscular.Size = New Size(160, 28)
        SliderMuscular.SmallChange = 0.01R
        SliderMuscular.TabIndex = 1
        SliderMuscular.TextBoxTextAlign = HorizontalAlignment.Right
        SliderMuscular.ThumbColor = SystemColors.HotTrack
        SliderMuscular.ThumbRadius = 4F
        SliderMuscular.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelThin
        ' 
        LabelThin.Anchor = AnchorStyles.Left
        LabelThin.AutoSize = True
        LabelThin.Location = New Point(3, 46)
        LabelThin.Name = "LabelThin"
        LabelThin.Size = New Size(34, 15)
        LabelThin.TabIndex = 2
        LabelThin.Text = "Thin:"
        LabelThin.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderThin
        ' 
        SliderThin.AccentColor = SystemColors.HotTrack
        SliderThin.Anchor = AnchorStyles.None
        SliderThin.BackColor = SystemColors.Control
        SliderThin.DisplayFormat = "0.00%"
        SliderThin.InputScale = 0.01R
        SliderThin.LargeChange = 0.1R
        SliderThin.Location = New Point(67, 40)
        SliderThin.Margin = New Padding(2, 4, 2, 4)
        SliderThin.Maximum = 1R
        SliderThin.MinimumSize = New Size(140, 28)
        SliderThin.Name = "SliderThin"
        SliderThin.Size = New Size(160, 28)
        SliderThin.SmallChange = 0.01R
        SliderThin.TabIndex = 3
        SliderThin.TextBoxTextAlign = HorizontalAlignment.Right
        SliderThin.ThumbColor = SystemColors.HotTrack
        SliderThin.ThumbRadius = 4F
        SliderThin.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelFat
        ' 
        LabelFat.Anchor = AnchorStyles.Left
        LabelFat.AutoSize = True
        LabelFat.Location = New Point(3, 82)
        LabelFat.Name = "LabelFat"
        LabelFat.Size = New Size(26, 15)
        LabelFat.TabIndex = 4
        LabelFat.Text = "Fat:"
        LabelFat.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderFat
        ' 
        SliderFat.AccentColor = SystemColors.HotTrack
        SliderFat.Anchor = AnchorStyles.None
        SliderFat.BackColor = SystemColors.Control
        SliderFat.DisplayFormat = "0.00%"
        SliderFat.InputScale = 0.01R
        SliderFat.LargeChange = 0.1R
        SliderFat.Location = New Point(67, 76)
        SliderFat.Margin = New Padding(2, 4, 2, 4)
        SliderFat.Maximum = 1R
        SliderFat.MinimumSize = New Size(140, 28)
        SliderFat.Name = "SliderFat"
        SliderFat.Size = New Size(160, 28)
        SliderFat.SmallChange = 0.01R
        SliderFat.TabIndex = 5
        SliderFat.TextBoxTextAlign = HorizontalAlignment.Right
        SliderFat.ThumbColor = SystemColors.HotTrack
        SliderFat.ThumbRadius = 4F
        SliderFat.TrackColor = SystemColors.ControlDark
        ' 
        ' GroupBoxMrsv
        ' 
        GroupBoxMrsv.AutoSize = True
        GroupBoxMrsv.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxMrsv.Controls.Add(MrsvLayout)
        GroupBoxMrsv.Dock = DockStyle.Fill
        GroupBoxMrsv.Location = New Point(3, 223)
        GroupBoxMrsv.Name = "GroupBoxMrsv"
        GroupBoxMrsv.Size = New Size(812, 30)
        GroupBoxMrsv.TabIndex = 1
        GroupBoxMrsv.TabStop = False
        GroupBoxMrsv.Text = "Body Morph Regions (NPC.MRSV — vanilla 5 regions, applied via bone scaling)"
        ' 
        ' MrsvLayout
        ' 
        MrsvLayout.AutoSize = True
        MrsvLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        MrsvLayout.ColumnCount = 2
        MrsvLayout.ColumnStyles.Add(New ColumnStyle())
        MrsvLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        MrsvLayout.Dock = DockStyle.Fill
        MrsvLayout.Location = New Point(3, 19)
        MrsvLayout.Name = "MrsvLayout"
        MrsvLayout.Padding = New Padding(4)
        MrsvLayout.RowCount = 5
        MrsvLayout.RowStyles.Add(New RowStyle())
        MrsvLayout.RowStyles.Add(New RowStyle())
        MrsvLayout.RowStyles.Add(New RowStyle())
        MrsvLayout.RowStyles.Add(New RowStyle())
        MrsvLayout.RowStyles.Add(New RowStyle())
        MrsvLayout.Size = New Size(806, 8)
        MrsvLayout.TabIndex = 0
        ' 
        ' GroupBoxHeight
        ' 
        GroupBoxHeight.AutoSize = True
        GroupBoxHeight.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxHeight.Controls.Add(HeightLayout)
        GroupBoxHeight.Dock = DockStyle.Fill
        GroupBoxHeight.Location = New Point(3, 259)
        GroupBoxHeight.Name = "GroupBoxHeight"
        GroupBoxHeight.Size = New Size(812, 102)
        GroupBoxHeight.TabIndex = 2
        GroupBoxHeight.TabStop = False
        GroupBoxHeight.Text = "Height (NPC.NAM6 / NAM4)"
        ' 
        ' HeightLayout
        ' 
        HeightLayout.AutoSize = True
        HeightLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        HeightLayout.ColumnCount = 2
        HeightLayout.ColumnStyles.Add(New ColumnStyle())
        HeightLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        HeightLayout.Controls.Add(LabelHeightMin, 0, 0)
        HeightLayout.Controls.Add(SliderHeightMin, 1, 0)
        HeightLayout.Controls.Add(LabelHeightMax, 0, 1)
        HeightLayout.Controls.Add(SliderHeightMax, 1, 1)
        HeightLayout.Dock = DockStyle.Fill
        HeightLayout.Location = New Point(3, 19)
        HeightLayout.Name = "HeightLayout"
        HeightLayout.Padding = New Padding(4)
        HeightLayout.RowCount = 2
        HeightLayout.RowStyles.Add(New RowStyle())
        HeightLayout.RowStyles.Add(New RowStyle())
        HeightLayout.Size = New Size(806, 80)
        HeightLayout.TabIndex = 0
        ' 
        ' LabelHeightMin
        ' 
        LabelHeightMin.Anchor = AnchorStyles.Left
        LabelHeightMin.AutoSize = True
        LabelHeightMin.Location = New Point(7, 14)
        LabelHeightMin.MinimumSize = New Size(64, 0)
        LabelHeightMin.Name = "LabelHeightMin"
        LabelHeightMin.Size = New Size(64, 15)
        LabelHeightMin.TabIndex = 0
        LabelHeightMin.Text = "Min:"
        LabelHeightMin.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderHeightMin
        ' 
        SliderHeightMin.AccentColor = SystemColors.HotTrack
        SliderHeightMin.AllowExtremeValues = True
        SliderHeightMin.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        SliderHeightMin.BackColor = SystemColors.Control
        SliderHeightMin.DisplayFormat = "0.00%"
        SliderHeightMin.InputScale = 0.01R
        SliderHeightMin.LargeChange = 0.1R
        SliderHeightMin.Location = New Point(76, 8)
        SliderHeightMin.Margin = New Padding(2, 4, 2, 4)
        SliderHeightMin.Maximum = 2R
        SliderHeightMin.Minimum = 0.2R
        SliderHeightMin.MinimumSize = New Size(140, 28)
        SliderHeightMin.Name = "SliderHeightMin"
        SliderHeightMin.Size = New Size(724, 28)
        SliderHeightMin.SmallChange = 0.01R
        SliderHeightMin.TabIndex = 1
        SliderHeightMin.TextBoxTextAlign = HorizontalAlignment.Right
        SliderHeightMin.ThumbColor = SystemColors.HotTrack
        SliderHeightMin.ThumbRadius = 4F
        SliderHeightMin.TrackColor = SystemColors.ControlDark
        SliderHeightMin.Value = 1R
        ' 
        ' LabelHeightMax
        ' 
        LabelHeightMax.Anchor = AnchorStyles.Left
        LabelHeightMax.AutoSize = True
        LabelHeightMax.Location = New Point(7, 50)
        LabelHeightMax.MinimumSize = New Size(64, 0)
        LabelHeightMax.Name = "LabelHeightMax"
        LabelHeightMax.Size = New Size(64, 15)
        LabelHeightMax.TabIndex = 2
        LabelHeightMax.Text = "Max:"
        LabelHeightMax.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderHeightMax
        ' 
        SliderHeightMax.AccentColor = SystemColors.HotTrack
        SliderHeightMax.AllowExtremeValues = True
        SliderHeightMax.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        SliderHeightMax.BackColor = SystemColors.Control
        SliderHeightMax.DisplayFormat = "0.00%"
        SliderHeightMax.InputScale = 0.01R
        SliderHeightMax.LargeChange = 0.1R
        SliderHeightMax.Location = New Point(76, 44)
        SliderHeightMax.Margin = New Padding(2, 4, 2, 4)
        SliderHeightMax.Maximum = 2R
        SliderHeightMax.Minimum = 0.2R
        SliderHeightMax.MinimumSize = New Size(140, 28)
        SliderHeightMax.Name = "SliderHeightMax"
        SliderHeightMax.Size = New Size(724, 28)
        SliderHeightMax.SmallChange = 0.01R
        SliderHeightMax.TabIndex = 3
        SliderHeightMax.TextBoxTextAlign = HorizontalAlignment.Right
        SliderHeightMax.ThumbColor = SystemColors.HotTrack
        SliderHeightMax.ThumbRadius = 4F
        SliderHeightMax.TrackColor = SystemColors.ControlDark
        SliderHeightMax.Value = 1R
        ' 
        ' GroupBoxSkin
        ' 
        GroupBoxSkin.AutoSize = True
        GroupBoxSkin.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSkin.Controls.Add(SkinLayout)
        GroupBoxSkin.Dock = DockStyle.Fill
        GroupBoxSkin.Location = New Point(3, 367)
        GroupBoxSkin.Name = "GroupBoxSkin"
        GroupBoxSkin.Size = New Size(812, 86)
        GroupBoxSkin.TabIndex = 3
        GroupBoxSkin.TabStop = False
        GroupBoxSkin.Text = "Skin"
        ' 
        ' SkinLayout
        ' 
        SkinLayout.AutoSize = True
        SkinLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SkinLayout.ColumnCount = 2
        SkinLayout.ColumnStyles.Add(New ColumnStyle())
        SkinLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinLayout.Controls.Add(LabelWnam, 0, 0)
        SkinLayout.Controls.Add(WnamPickPanel, 1, 0)
        SkinLayout.Controls.Add(LabelLmSkinTemplate, 0, 1)
        SkinLayout.Controls.Add(ComboBoxLmSkinTemplate, 1, 1)
        SkinLayout.Dock = DockStyle.Fill
        SkinLayout.Location = New Point(3, 19)
        SkinLayout.Name = "SkinLayout"
        SkinLayout.Padding = New Padding(4)
        SkinLayout.RowCount = 3
        SkinLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28F))
        SkinLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 28F))
        SkinLayout.RowStyles.Add(New RowStyle())
        SkinLayout.Size = New Size(806, 64)
        SkinLayout.TabIndex = 0
        ' 
        ' LabelWnam
        ' 
        LabelWnam.AutoSize = True
        LabelWnam.Location = New Point(6, 10)
        LabelWnam.Margin = New Padding(2, 6, 8, 2)
        LabelWnam.Name = "LabelWnam"
        LabelWnam.Size = New Size(109, 15)
        LabelWnam.TabIndex = 0
        LabelWnam.Text = "Skin (NPC.WNAM):"
        LabelWnam.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' WnamPickPanel
        ' 
        WnamPickPanel.ColumnCount = 2
        WnamPickPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        WnamPickPanel.ColumnStyles.Add(New ColumnStyle())
        WnamPickPanel.Controls.Add(ComboBoxWnam, 0, 0)
        WnamPickPanel.Controls.Add(ButtonPickWnam, 1, 0)
        WnamPickPanel.Dock = DockStyle.Fill
        WnamPickPanel.Location = New Point(123, 4)
        WnamPickPanel.Margin = New Padding(0)
        WnamPickPanel.Name = "WnamPickPanel"
        WnamPickPanel.RowCount = 1
        WnamPickPanel.RowStyles.Add(New RowStyle())
        WnamPickPanel.Size = New Size(679, 28)
        WnamPickPanel.TabIndex = 1
        ' 
        ' ComboBoxWnam
        ' 
        ComboBoxWnam.Dock = DockStyle.Fill
        ComboBoxWnam.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxWnam.Location = New Point(2, 2)
        ComboBoxWnam.Margin = New Padding(2)
        ComboBoxWnam.Name = "ComboBoxWnam"
        ComboBoxWnam.Size = New Size(641, 23)
        ComboBoxWnam.TabIndex = 1
        ' 
        ' ButtonPickWnam
        ' 
        ButtonPickWnam.Anchor = AnchorStyles.Left
        ButtonPickWnam.Location = New Point(647, 2)
        ButtonPickWnam.Margin = New Padding(2)
        ButtonPickWnam.Name = "ButtonPickWnam"
        ButtonPickWnam.Size = New Size(30, 23)
        ButtonPickWnam.TabIndex = 2
        ButtonPickWnam.Text = "..."
        ButtonPickWnam.UseVisualStyleBackColor = True
        ' 
        ' LabelLmSkinTemplate
        ' 
        LabelLmSkinTemplate.AutoSize = True
        LabelLmSkinTemplate.Location = New Point(6, 38)
        LabelLmSkinTemplate.Margin = New Padding(2, 6, 8, 2)
        LabelLmSkinTemplate.Name = "LabelLmSkinTemplate"
        LabelLmSkinTemplate.Size = New Size(87, 15)
        LabelLmSkinTemplate.TabIndex = 2
        LabelLmSkinTemplate.Text = "LM Skin (F4SE):"
        LabelLmSkinTemplate.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ComboBoxLmSkinTemplate
        ' 
        ComboBoxLmSkinTemplate.Dock = DockStyle.Fill
        ComboBoxLmSkinTemplate.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxLmSkinTemplate.Location = New Point(125, 34)
        ComboBoxLmSkinTemplate.Margin = New Padding(2)
        ComboBoxLmSkinTemplate.Name = "ComboBoxLmSkinTemplate"
        ComboBoxLmSkinTemplate.Size = New Size(675, 23)
        ComboBoxLmSkinTemplate.TabIndex = 3
        ' 
        ' GroupBoxSseWeight
        ' 
        GroupBoxSseWeight.AutoSize = True
        GroupBoxSseWeight.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSseWeight.Controls.Add(SseWeightLayout)
        GroupBoxSseWeight.Dock = DockStyle.Fill
        GroupBoxSseWeight.Location = New Point(3, 459)
        GroupBoxSseWeight.Name = "GroupBoxSseWeight"
        GroupBoxSseWeight.Size = New Size(812, 83)
        GroupBoxSseWeight.TabIndex = 4
        GroupBoxSseWeight.TabStop = False
        GroupBoxSseWeight.Text = "Weight (NPC.NAM7 — SSE _0 / _1 body morph)"
        GroupBoxSseWeight.Visible = False
        ' 
        ' SseWeightLayout
        ' 
        SseWeightLayout.AutoSize = True
        SseWeightLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SseWeightLayout.ColumnCount = 2
        SseWeightLayout.ColumnStyles.Add(New ColumnStyle())
        SseWeightLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseWeightLayout.Controls.Add(LabelSseWeightNote, 0, 0)
        SseWeightLayout.Controls.Add(LabelSseWeight, 0, 1)
        SseWeightLayout.Controls.Add(SliderSseWeight, 1, 1)
        SseWeightLayout.Dock = DockStyle.Fill
        SseWeightLayout.Location = New Point(3, 19)
        SseWeightLayout.Name = "SseWeightLayout"
        SseWeightLayout.Padding = New Padding(4)
        SseWeightLayout.RowCount = 2
        SseWeightLayout.RowStyles.Add(New RowStyle())
        SseWeightLayout.RowStyles.Add(New RowStyle())
        SseWeightLayout.Size = New Size(806, 61)
        SseWeightLayout.TabIndex = 0
        ' 
        ' LabelSseWeightNote
        ' 
        LabelSseWeightNote.AutoSize = True
        SseWeightLayout.SetColumnSpan(LabelSseWeightNote, 2)
        LabelSseWeightNote.Location = New Point(6, 6)
        LabelSseWeightNote.Margin = New Padding(2, 2, 2, 4)
        LabelSseWeightNote.Name = "LabelSseWeightNote"
        LabelSseWeightNote.Size = New Size(552, 15)
        LabelSseWeightNote.TabIndex = 0
        LabelSseWeightNote.Text = "Vanilla — stored in the NPC record (NPC.NAM7). Load/Save a RaceMenu preset from the main window."
        ' 
        ' LabelSseWeight
        ' 
        LabelSseWeight.Anchor = AnchorStyles.Left
        LabelSseWeight.AutoSize = True
        LabelSseWeight.Location = New Point(6, 34)
        LabelSseWeight.Margin = New Padding(2, 6, 8, 2)
        LabelSseWeight.Name = "LabelSseWeight"
        LabelSseWeight.Size = New Size(117, 15)
        LabelSseWeight.TabIndex = 1
        LabelSseWeight.Text = "Weight (NPC.NAM7)"
        LabelSseWeight.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSseWeight
        ' 
        SliderSseWeight.AccentColor = SystemColors.HotTrack
        SliderSseWeight.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        SliderSseWeight.BackColor = SystemColors.Control
        SliderSseWeight.DisplayFormat = "0\%"
        SliderSseWeight.Location = New Point(133, 33)
        SliderSseWeight.Margin = New Padding(2)
        SliderSseWeight.MinimumSize = New Size(140, 28)
        SliderSseWeight.Name = "SliderSseWeight"
        SliderSseWeight.Size = New Size(667, 28)
        SliderSseWeight.TabIndex = 2
        SliderSseWeight.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseWeight.ThumbColor = SystemColors.HotTrack
        SliderSseWeight.ThumbRadius = 4F
        SliderSseWeight.TrackColor = SystemColors.ControlDark
        ' 
        ' TabPageBodySlide
        ' 
        TabPageBodySlide.Controls.Add(BodySlideTabLayout)
        TabPageBodySlide.Location = New Point(4, 24)
        TabPageBodySlide.Name = "TabPageBodySlide"
        TabPageBodySlide.Padding = New Padding(6)
        TabPageBodySlide.Size = New Size(830, 692)
        TabPageBodySlide.TabIndex = 1
        TabPageBodySlide.Text = "BodySlide"
        ' 
        ' BodySlideTabLayout
        ' 
        BodySlideTabLayout.ColumnCount = 1
        BodySlideTabLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodySlideTabLayout.Controls.Add(GroupBoxBodySlide, 0, 0)
        BodySlideTabLayout.Controls.Add(LabelBodySlideEmpty, 0, 1)
        BodySlideTabLayout.Dock = DockStyle.Fill
        BodySlideTabLayout.Location = New Point(6, 6)
        BodySlideTabLayout.Name = "BodySlideTabLayout"
        BodySlideTabLayout.RowCount = 2
        BodySlideTabLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BodySlideTabLayout.RowStyles.Add(New RowStyle())
        BodySlideTabLayout.Size = New Size(818, 680)
        BodySlideTabLayout.TabIndex = 0
        ' 
        ' GroupBoxBodySlide
        ' 
        GroupBoxBodySlide.Controls.Add(BodySlideLayout)
        GroupBoxBodySlide.Dock = DockStyle.Fill
        GroupBoxBodySlide.Location = New Point(3, 3)
        GroupBoxBodySlide.Name = "GroupBoxBodySlide"
        GroupBoxBodySlide.Size = New Size(812, 651)
        GroupBoxBodySlide.TabIndex = 0
        GroupBoxBodySlide.TabStop = False
        GroupBoxBodySlide.Text = "BodySlide Sliders (PIRT .tri — vertex morphs, F4SE-only field)"
        ' 
        ' BodySlideLayout
        ' 
        BodySlideLayout.ColumnCount = 1
        BodySlideLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodySlideLayout.Controls.Add(BodySlidePresetLayout, 0, 0)
        BodySlideLayout.Controls.Add(TextBoxBodySlideFilter, 0, 1)
        BodySlideLayout.Controls.Add(BodySlidePanel, 0, 2)
        BodySlideLayout.Dock = DockStyle.Fill
        BodySlideLayout.Location = New Point(3, 19)
        BodySlideLayout.Name = "BodySlideLayout"
        BodySlideLayout.Padding = New Padding(4)
        BodySlideLayout.RowCount = 3
        BodySlideLayout.RowStyles.Add(New RowStyle())
        BodySlideLayout.RowStyles.Add(New RowStyle())
        BodySlideLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BodySlideLayout.Size = New Size(806, 629)
        BodySlideLayout.TabIndex = 0
        ' 
        ' BodySlidePresetLayout
        ' 
        BodySlidePresetLayout.AutoSize = True
        BodySlidePresetLayout.ColumnCount = 4
        BodySlidePresetLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodySlidePresetLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 80F))
        BodySlidePresetLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 58F))
        BodySlidePresetLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 92F))
        BodySlidePresetLayout.Controls.Add(ComboBoxBsPreset, 0, 0)
        BodySlidePresetLayout.Controls.Add(ComboBoxBsSize, 1, 0)
        BodySlidePresetLayout.Controls.Add(ButtonBsPresetClear, 2, 0)
        BodySlidePresetLayout.Controls.Add(ButtonBsPresetBrowse, 3, 0)
        BodySlidePresetLayout.Dock = DockStyle.Top
        BodySlidePresetLayout.Location = New Point(4, 4)
        BodySlidePresetLayout.Margin = New Padding(0, 0, 0, 4)
        BodySlidePresetLayout.Name = "BodySlidePresetLayout"
        BodySlidePresetLayout.RowCount = 1
        BodySlidePresetLayout.RowStyles.Add(New RowStyle())
        BodySlidePresetLayout.Size = New Size(798, 31)
        BodySlidePresetLayout.TabIndex = 2
        ' 
        ' ComboBoxBsPreset
        ' 
        ComboBoxBsPreset.Dock = DockStyle.Fill
        ComboBoxBsPreset.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxBsPreset.Location = New Point(3, 3)
        ComboBoxBsPreset.Name = "ComboBoxBsPreset"
        ComboBoxBsPreset.Size = New Size(562, 23)
        ComboBoxBsPreset.TabIndex = 0
        ' 
        ' ComboBoxBsSize
        ' 
        ComboBoxBsSize.Dock = DockStyle.Fill
        ComboBoxBsSize.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxBsSize.Items.AddRange(New Object() {"Default", "Big", "Small"})
        ComboBoxBsSize.Location = New Point(571, 3)
        ComboBoxBsSize.Name = "ComboBoxBsSize"
        ComboBoxBsSize.Size = New Size(74, 23)
        ComboBoxBsSize.TabIndex = 1
        ' 
        ' ButtonBsPresetClear
        ' 
        ButtonBsPresetClear.Dock = DockStyle.Fill
        ButtonBsPresetClear.Location = New Point(651, 3)
        ButtonBsPresetClear.Name = "ButtonBsPresetClear"
        ButtonBsPresetClear.Size = New Size(52, 25)
        ButtonBsPresetClear.TabIndex = 2
        ButtonBsPresetClear.Text = "Clear"
        ' 
        ' ButtonBsPresetBrowse
        ' 
        ButtonBsPresetBrowse.Dock = DockStyle.Fill
        ButtonBsPresetBrowse.Location = New Point(709, 3)
        ButtonBsPresetBrowse.Name = "ButtonBsPresetBrowse"
        ButtonBsPresetBrowse.Size = New Size(86, 25)
        ButtonBsPresetBrowse.TabIndex = 3
        ButtonBsPresetBrowse.Text = "Set BS exe…"
        ' 
        ' TextBoxBodySlideFilter
        ' 
        TextBoxBodySlideFilter.Dock = DockStyle.Top
        TextBoxBodySlideFilter.Location = New Point(7, 42)
        TextBoxBodySlideFilter.Name = "TextBoxBodySlideFilter"
        TextBoxBodySlideFilter.PlaceholderText = "Filter sliders…"
        TextBoxBodySlideFilter.Size = New Size(792, 23)
        TextBoxBodySlideFilter.TabIndex = 0
        ' 
        ' BodySlidePanel
        ' 
        BodySlidePanel.AutoScroll = True
        BodySlidePanel.Dock = DockStyle.Fill
        BodySlidePanel.FlowDirection = FlowDirection.TopDown
        BodySlidePanel.Location = New Point(7, 71)
        BodySlidePanel.Name = "BodySlidePanel"
        BodySlidePanel.Size = New Size(792, 551)
        BodySlidePanel.TabIndex = 1
        BodySlidePanel.WrapContents = False
        ' 
        ' LabelBodySlideEmpty
        ' 
        LabelBodySlideEmpty.AutoSize = True
        LabelBodySlideEmpty.ForeColor = Color.Gray
        LabelBodySlideEmpty.Location = New Point(8, 661)
        LabelBodySlideEmpty.Margin = New Padding(8, 4, 8, 4)
        LabelBodySlideEmpty.Name = "LabelBodySlideEmpty"
        LabelBodySlideEmpty.Size = New Size(452, 15)
        LabelBodySlideEmpty.TabIndex = 1
        LabelBodySlideEmpty.Text = "This NPC has no BodySlide morph data (no BODYTRI extra-data on any body shape)."
        LabelBodySlideEmpty.Visible = False
        ' 
        ' TabPageSseBodyScale
        ' 
        TabPageSseBodyScale.Controls.Add(SseBodyScaleRoot)
        TabPageSseBodyScale.Location = New Point(4, 24)
        TabPageSseBodyScale.Name = "TabPageSseBodyScale"
        TabPageSseBodyScale.Padding = New Padding(6)
        TabPageSseBodyScale.Size = New Size(830, 692)
        TabPageSseBodyScale.TabIndex = 4
        TabPageSseBodyScale.Text = "Transforms (RM)"
        ' 
        ' SseBodyScaleRoot
        ' 
        SseBodyScaleRoot.ColumnCount = 2
        SseBodyScaleRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 42F))
        SseBodyScaleRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 58F))
        SseBodyScaleRoot.Controls.Add(SseNodeLeftCol, 0, 0)
        SseBodyScaleRoot.Controls.Add(PanelSseNodeDetail, 1, 0)
        SseBodyScaleRoot.Controls.Add(LabelSseNodeNote, 0, 1)
        SseBodyScaleRoot.Dock = DockStyle.Fill
        SseBodyScaleRoot.Location = New Point(6, 6)
        SseBodyScaleRoot.Name = "SseBodyScaleRoot"
        SseBodyScaleRoot.RowCount = 2
        SseBodyScaleRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SseBodyScaleRoot.RowStyles.Add(New RowStyle())
        SseBodyScaleRoot.Size = New Size(818, 680)
        SseBodyScaleRoot.TabIndex = 0
        ' 
        ' SseNodeLeftCol
        ' 
        SseNodeLeftCol.ColumnCount = 1
        SseNodeLeftCol.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseNodeLeftCol.Controls.Add(CheckBoxSseShowAllNodes, 0, 0)
        SseNodeLeftCol.Controls.Add(TextBoxSseNodeFilter, 0, 1)
        SseNodeLeftCol.Controls.Add(ListBoxSseNodes, 0, 2)
        SseNodeLeftCol.Dock = DockStyle.Fill
        SseNodeLeftCol.Location = New Point(3, 3)
        SseNodeLeftCol.Name = "SseNodeLeftCol"
        SseNodeLeftCol.RowCount = 3
        SseNodeLeftCol.RowStyles.Add(New RowStyle())
        SseNodeLeftCol.RowStyles.Add(New RowStyle())
        SseNodeLeftCol.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SseNodeLeftCol.Size = New Size(337, 647)
        SseNodeLeftCol.TabIndex = 0
        ' 
        ' CheckBoxSseShowAllNodes
        ' 
        CheckBoxSseShowAllNodes.AutoSize = True
        CheckBoxSseShowAllNodes.Location = New Point(3, 3)
        CheckBoxSseShowAllNodes.Name = "CheckBoxSseShowAllNodes"
        CheckBoxSseShowAllNodes.Size = New Size(191, 19)
        CheckBoxSseShowAllNodes.TabIndex = 0
        CheckBoxSseShowAllNodes.Text = "Show all rig bones (+ weapons)"
        ToolTipSseNode.SetToolTip(CheckBoxSseShowAllNodes, resources.GetString("CheckBoxSseShowAllNodes.ToolTip"))
        ' 
        ' TextBoxSseNodeFilter
        ' 
        TextBoxSseNodeFilter.Dock = DockStyle.Fill
        TextBoxSseNodeFilter.Location = New Point(3, 28)
        TextBoxSseNodeFilter.Name = "TextBoxSseNodeFilter"
        TextBoxSseNodeFilter.PlaceholderText = "Filter nodes…"
        TextBoxSseNodeFilter.Size = New Size(331, 23)
        TextBoxSseNodeFilter.TabIndex = 1
        ' 
        ' ListBoxSseNodes
        ' 
        ListBoxSseNodes.Dock = DockStyle.Fill
        ListBoxSseNodes.IntegralHeight = False
        ListBoxSseNodes.ItemHeight = 15
        ListBoxSseNodes.Location = New Point(0, 54)
        ListBoxSseNodes.Margin = New Padding(0)
        ListBoxSseNodes.Name = "ListBoxSseNodes"
        ListBoxSseNodes.Size = New Size(337, 593)
        ListBoxSseNodes.TabIndex = 2
        ' 
        ' PanelSseNodeDetail
        ' 
        PanelSseNodeDetail.AutoScroll = True
        PanelSseNodeDetail.Controls.Add(FlowSseNodeButtons)
        PanelSseNodeDetail.Controls.Add(SseNodeDetailLayout)
        PanelSseNodeDetail.Dock = DockStyle.Fill
        PanelSseNodeDetail.Location = New Point(346, 3)
        PanelSseNodeDetail.Name = "PanelSseNodeDetail"
        PanelSseNodeDetail.Size = New Size(469, 647)
        PanelSseNodeDetail.TabIndex = 1
        ' 
        ' FlowSseNodeButtons
        ' 
        FlowSseNodeButtons.AutoSize = True
        FlowSseNodeButtons.Controls.Add(ButtonSseNodeReset)
        FlowSseNodeButtons.Dock = DockStyle.Top
        FlowSseNodeButtons.Location = New Point(0, 224)
        FlowSseNodeButtons.Margin = New Padding(0)
        FlowSseNodeButtons.Name = "FlowSseNodeButtons"
        FlowSseNodeButtons.Padding = New Padding(118, 0, 0, 0)
        FlowSseNodeButtons.Size = New Size(469, 32)
        FlowSseNodeButtons.TabIndex = 0
        ' 
        ' ButtonSseNodeReset
        ' 
        ButtonSseNodeReset.AutoSize = True
        ButtonSseNodeReset.Location = New Point(118, 4)
        ButtonSseNodeReset.Margin = New Padding(0, 4, 3, 3)
        ButtonSseNodeReset.Name = "ButtonSseNodeReset"
        ButtonSseNodeReset.Size = New Size(83, 25)
        ButtonSseNodeReset.TabIndex = 0
        ButtonSseNodeReset.Text = "Reset node"
        ' 
        ' SseNodeDetailLayout
        ' 
        SseNodeDetailLayout.ColumnCount = 2
        SseNodeDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 118F))
        SseNodeDetailLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseNodeDetailLayout.Controls.Add(LabelSseNodeScale, 0, 0)
        SseNodeDetailLayout.Controls.Add(SliderSseNodeScale, 1, 0)
        SseNodeDetailLayout.Controls.Add(LabelSseNodePosX, 0, 1)
        SseNodeDetailLayout.Controls.Add(SliderSseNodePosX, 1, 1)
        SseNodeDetailLayout.Controls.Add(LabelSseNodePosY, 0, 2)
        SseNodeDetailLayout.Controls.Add(SliderSseNodePosY, 1, 2)
        SseNodeDetailLayout.Controls.Add(LabelSseNodePosZ, 0, 3)
        SseNodeDetailLayout.Controls.Add(SliderSseNodePosZ, 1, 3)
        SseNodeDetailLayout.Controls.Add(LabelSseNodeRotX, 0, 4)
        SseNodeDetailLayout.Controls.Add(SliderSseNodeRotX, 1, 4)
        SseNodeDetailLayout.Controls.Add(LabelSseNodeRotY, 0, 5)
        SseNodeDetailLayout.Controls.Add(SliderSseNodeRotY, 1, 5)
        SseNodeDetailLayout.Controls.Add(LabelSseNodeRotZ, 0, 6)
        SseNodeDetailLayout.Controls.Add(SliderSseNodeRotZ, 1, 6)
        SseNodeDetailLayout.Dock = DockStyle.Top
        SseNodeDetailLayout.Location = New Point(0, 0)
        SseNodeDetailLayout.Name = "SseNodeDetailLayout"
        SseNodeDetailLayout.RowCount = 7
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        SseNodeDetailLayout.Size = New Size(469, 224)
        SseNodeDetailLayout.TabIndex = 1
        ' 
        ' LabelSseNodeScale
        ' 
        LabelSseNodeScale.Anchor = AnchorStyles.Left
        LabelSseNodeScale.AutoSize = True
        LabelSseNodeScale.Location = New Point(3, 12)
        LabelSseNodeScale.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodeScale.Name = "LabelSseNodeScale"
        LabelSseNodeScale.Size = New Size(34, 15)
        LabelSseNodeScale.TabIndex = 0
        LabelSseNodeScale.Text = "Scale"
        ' 
        ' SliderSseNodeScale
        ' 
        SliderSseNodeScale.AccentColor = SystemColors.HotTrack
        SliderSseNodeScale.AllowExtremeValues = True
        SliderSseNodeScale.BackColor = SystemColors.Control
        SliderSseNodeScale.DisplayFormat = "0.00"
        SliderSseNodeScale.Dock = DockStyle.Fill
        SliderSseNodeScale.LargeChange = 0.1R
        SliderSseNodeScale.Location = New Point(121, 3)
        SliderSseNodeScale.Maximum = 2R
        SliderSseNodeScale.Minimum = 0.01R
        SliderSseNodeScale.MinimumSize = New Size(100, 24)
        SliderSseNodeScale.Name = "SliderSseNodeScale"
        SliderSseNodeScale.Size = New Size(345, 26)
        SliderSseNodeScale.SmallChange = 0.01R
        SliderSseNodeScale.TabIndex = 1
        SliderSseNodeScale.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodeScale.ThumbColor = SystemColors.HotTrack
        SliderSseNodeScale.ThumbRadius = 4F
        SliderSseNodeScale.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodePosX
        ' 
        LabelSseNodePosX.Anchor = AnchorStyles.Left
        LabelSseNodePosX.AutoSize = True
        LabelSseNodePosX.Location = New Point(3, 44)
        LabelSseNodePosX.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodePosX.Name = "LabelSseNodePosX"
        LabelSseNodePosX.Size = New Size(60, 15)
        LabelSseNodePosX.TabIndex = 2
        LabelSseNodePosX.Text = "Position X"
        ' 
        ' SliderSseNodePosX
        ' 
        SliderSseNodePosX.AccentColor = SystemColors.HotTrack
        SliderSseNodePosX.AllowExtremeValues = True
        SliderSseNodePosX.BackColor = SystemColors.Control
        SliderSseNodePosX.DisplayFormat = "0.00"
        SliderSseNodePosX.Dock = DockStyle.Fill
        SliderSseNodePosX.FillMode = TinySliderFillMode.Center
        SliderSseNodePosX.LargeChange = 0.1R
        SliderSseNodePosX.Location = New Point(121, 35)
        SliderSseNodePosX.Maximum = 20R
        SliderSseNodePosX.Minimum = -20R
        SliderSseNodePosX.MinimumSize = New Size(100, 24)
        SliderSseNodePosX.Name = "SliderSseNodePosX"
        SliderSseNodePosX.Size = New Size(345, 26)
        SliderSseNodePosX.SmallChange = 0.01R
        SliderSseNodePosX.TabIndex = 3
        SliderSseNodePosX.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodePosX.ThumbColor = SystemColors.HotTrack
        SliderSseNodePosX.ThumbRadius = 4F
        SliderSseNodePosX.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodePosY
        ' 
        LabelSseNodePosY.Anchor = AnchorStyles.Left
        LabelSseNodePosY.AutoSize = True
        LabelSseNodePosY.Location = New Point(3, 76)
        LabelSseNodePosY.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodePosY.Name = "LabelSseNodePosY"
        LabelSseNodePosY.Size = New Size(60, 15)
        LabelSseNodePosY.TabIndex = 4
        LabelSseNodePosY.Text = "Position Y"
        ' 
        ' SliderSseNodePosY
        ' 
        SliderSseNodePosY.AccentColor = SystemColors.HotTrack
        SliderSseNodePosY.AllowExtremeValues = True
        SliderSseNodePosY.BackColor = SystemColors.Control
        SliderSseNodePosY.DisplayFormat = "0.00"
        SliderSseNodePosY.Dock = DockStyle.Fill
        SliderSseNodePosY.FillMode = TinySliderFillMode.Center
        SliderSseNodePosY.LargeChange = 0.1R
        SliderSseNodePosY.Location = New Point(121, 67)
        SliderSseNodePosY.Maximum = 20R
        SliderSseNodePosY.Minimum = -20R
        SliderSseNodePosY.MinimumSize = New Size(100, 24)
        SliderSseNodePosY.Name = "SliderSseNodePosY"
        SliderSseNodePosY.Size = New Size(345, 26)
        SliderSseNodePosY.SmallChange = 0.01R
        SliderSseNodePosY.TabIndex = 5
        SliderSseNodePosY.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodePosY.ThumbColor = SystemColors.HotTrack
        SliderSseNodePosY.ThumbRadius = 4F
        SliderSseNodePosY.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodePosZ
        ' 
        LabelSseNodePosZ.Anchor = AnchorStyles.Left
        LabelSseNodePosZ.AutoSize = True
        LabelSseNodePosZ.Location = New Point(3, 108)
        LabelSseNodePosZ.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodePosZ.Name = "LabelSseNodePosZ"
        LabelSseNodePosZ.Size = New Size(60, 15)
        LabelSseNodePosZ.TabIndex = 6
        LabelSseNodePosZ.Text = "Position Z"
        ' 
        ' SliderSseNodePosZ
        ' 
        SliderSseNodePosZ.AccentColor = SystemColors.HotTrack
        SliderSseNodePosZ.AllowExtremeValues = True
        SliderSseNodePosZ.BackColor = SystemColors.Control
        SliderSseNodePosZ.DisplayFormat = "0.00"
        SliderSseNodePosZ.Dock = DockStyle.Fill
        SliderSseNodePosZ.FillMode = TinySliderFillMode.Center
        SliderSseNodePosZ.LargeChange = 0.1R
        SliderSseNodePosZ.Location = New Point(121, 99)
        SliderSseNodePosZ.Maximum = 20R
        SliderSseNodePosZ.Minimum = -20R
        SliderSseNodePosZ.MinimumSize = New Size(100, 24)
        SliderSseNodePosZ.Name = "SliderSseNodePosZ"
        SliderSseNodePosZ.Size = New Size(345, 26)
        SliderSseNodePosZ.SmallChange = 0.01R
        SliderSseNodePosZ.TabIndex = 7
        SliderSseNodePosZ.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodePosZ.ThumbColor = SystemColors.HotTrack
        SliderSseNodePosZ.ThumbRadius = 4F
        SliderSseNodePosZ.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodeRotX
        ' 
        LabelSseNodeRotX.Anchor = AnchorStyles.Left
        LabelSseNodeRotX.AutoSize = True
        LabelSseNodeRotX.Location = New Point(3, 140)
        LabelSseNodeRotX.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodeRotX.Name = "LabelSseNodeRotX"
        LabelSseNodeRotX.Size = New Size(78, 15)
        LabelSseNodeRotX.TabIndex = 8
        LabelSseNodeRotX.Text = "Rotation X (°)"
        ' 
        ' SliderSseNodeRotX
        ' 
        SliderSseNodeRotX.AccentColor = SystemColors.HotTrack
        SliderSseNodeRotX.AllowExtremeValues = True
        SliderSseNodeRotX.BackColor = SystemColors.Control
        SliderSseNodeRotX.DisplayFormat = "0.0"
        SliderSseNodeRotX.Dock = DockStyle.Fill
        SliderSseNodeRotX.FillMode = TinySliderFillMode.Center
        SliderSseNodeRotX.LargeChange = 0.1R
        SliderSseNodeRotX.Location = New Point(121, 131)
        SliderSseNodeRotX.Maximum = 180R
        SliderSseNodeRotX.Minimum = -180R
        SliderSseNodeRotX.MinimumSize = New Size(100, 24)
        SliderSseNodeRotX.Name = "SliderSseNodeRotX"
        SliderSseNodeRotX.Size = New Size(345, 26)
        SliderSseNodeRotX.SmallChange = 0.01R
        SliderSseNodeRotX.TabIndex = 9
        SliderSseNodeRotX.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodeRotX.ThumbColor = SystemColors.HotTrack
        SliderSseNodeRotX.ThumbRadius = 4F
        SliderSseNodeRotX.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodeRotY
        ' 
        LabelSseNodeRotY.Anchor = AnchorStyles.Left
        LabelSseNodeRotY.AutoSize = True
        LabelSseNodeRotY.Location = New Point(3, 172)
        LabelSseNodeRotY.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodeRotY.Name = "LabelSseNodeRotY"
        LabelSseNodeRotY.Size = New Size(78, 15)
        LabelSseNodeRotY.TabIndex = 10
        LabelSseNodeRotY.Text = "Rotation Y (°)"
        ' 
        ' SliderSseNodeRotY
        ' 
        SliderSseNodeRotY.AccentColor = SystemColors.HotTrack
        SliderSseNodeRotY.AllowExtremeValues = True
        SliderSseNodeRotY.BackColor = SystemColors.Control
        SliderSseNodeRotY.DisplayFormat = "0.0"
        SliderSseNodeRotY.Dock = DockStyle.Fill
        SliderSseNodeRotY.FillMode = TinySliderFillMode.Center
        SliderSseNodeRotY.LargeChange = 0.1R
        SliderSseNodeRotY.Location = New Point(121, 163)
        SliderSseNodeRotY.Maximum = 180R
        SliderSseNodeRotY.Minimum = -180R
        SliderSseNodeRotY.MinimumSize = New Size(100, 24)
        SliderSseNodeRotY.Name = "SliderSseNodeRotY"
        SliderSseNodeRotY.Size = New Size(345, 26)
        SliderSseNodeRotY.SmallChange = 0.01R
        SliderSseNodeRotY.TabIndex = 11
        SliderSseNodeRotY.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodeRotY.ThumbColor = SystemColors.HotTrack
        SliderSseNodeRotY.ThumbRadius = 4F
        SliderSseNodeRotY.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodeRotZ
        ' 
        LabelSseNodeRotZ.Anchor = AnchorStyles.Left
        LabelSseNodeRotZ.AutoSize = True
        LabelSseNodeRotZ.Location = New Point(3, 204)
        LabelSseNodeRotZ.Margin = New Padding(3, 8, 3, 0)
        LabelSseNodeRotZ.Name = "LabelSseNodeRotZ"
        LabelSseNodeRotZ.Size = New Size(78, 15)
        LabelSseNodeRotZ.TabIndex = 12
        LabelSseNodeRotZ.Text = "Rotation Z (°)"
        ' 
        ' SliderSseNodeRotZ
        ' 
        SliderSseNodeRotZ.AccentColor = SystemColors.HotTrack
        SliderSseNodeRotZ.AllowExtremeValues = True
        SliderSseNodeRotZ.BackColor = SystemColors.Control
        SliderSseNodeRotZ.DisplayFormat = "0.0"
        SliderSseNodeRotZ.Dock = DockStyle.Fill
        SliderSseNodeRotZ.FillMode = TinySliderFillMode.Center
        SliderSseNodeRotZ.LargeChange = 0.1R
        SliderSseNodeRotZ.Location = New Point(121, 195)
        SliderSseNodeRotZ.Maximum = 180R
        SliderSseNodeRotZ.Minimum = -180R
        SliderSseNodeRotZ.MinimumSize = New Size(100, 24)
        SliderSseNodeRotZ.Name = "SliderSseNodeRotZ"
        SliderSseNodeRotZ.Size = New Size(345, 26)
        SliderSseNodeRotZ.SmallChange = 0.01R
        SliderSseNodeRotZ.TabIndex = 13
        SliderSseNodeRotZ.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseNodeRotZ.ThumbColor = SystemColors.HotTrack
        SliderSseNodeRotZ.ThumbRadius = 4F
        SliderSseNodeRotZ.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSseNodeNote
        ' 
        LabelSseNodeNote.AutoSize = True
        SseBodyScaleRoot.SetColumnSpan(LabelSseNodeNote, 2)
        LabelSseNodeNote.Dock = DockStyle.Fill
        LabelSseNodeNote.ForeColor = SystemColors.GrayText
        LabelSseNodeNote.Location = New Point(3, 653)
        LabelSseNodeNote.Name = "LabelSseNodeNote"
        LabelSseNodeNote.Padding = New Padding(6, 8, 6, 4)
        LabelSseNodeNote.Size = New Size(812, 27)
        LabelSseNodeNote.TabIndex = 1
        ' 
        ' TabPageSseSkinOverrides
        ' 
        TabPageSseSkinOverrides.Controls.Add(SseSkinRoot)
        TabPageSseSkinOverrides.Location = New Point(4, 24)
        TabPageSseSkinOverrides.Name = "TabPageSseSkinOverrides"
        TabPageSseSkinOverrides.Padding = New Padding(6)
        TabPageSseSkinOverrides.Size = New Size(830, 692)
        TabPageSseSkinOverrides.TabIndex = 5
        TabPageSseSkinOverrides.Text = "Skin Overrides (RM)"
        ' 
        ' SseSkinRoot
        ' 
        SseSkinRoot.ColumnCount = 2
        SseSkinRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        SseSkinRoot.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        SseSkinRoot.Controls.Add(LabelSseSkinHeader, 0, 0)
        SseSkinRoot.Controls.Add(SseSkinLeftPanel, 0, 1)
        SseSkinRoot.Controls.Add(GroupBoxSseSkinSlots, 0, 2)
        SseSkinRoot.Controls.Add(SseSkinDetail, 1, 1)
        SseSkinRoot.Dock = DockStyle.Fill
        SseSkinRoot.Location = New Point(6, 6)
        SseSkinRoot.Name = "SseSkinRoot"
        SseSkinRoot.RowCount = 3
        SseSkinRoot.RowStyles.Add(New RowStyle(SizeType.Absolute, 34F))
        SseSkinRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 55F))
        SseSkinRoot.RowStyles.Add(New RowStyle(SizeType.Percent, 45F))
        SseSkinRoot.Size = New Size(818, 680)
        SseSkinRoot.TabIndex = 0
        ' 
        ' LabelSseSkinHeader
        ' 
        SseSkinRoot.SetColumnSpan(LabelSseSkinHeader, 2)
        LabelSseSkinHeader.Dock = DockStyle.Fill
        LabelSseSkinHeader.Location = New Point(3, 6)
        LabelSseSkinHeader.Margin = New Padding(3, 6, 3, 0)
        LabelSseSkinHeader.Name = "LabelSseSkinHeader"
        LabelSseSkinHeader.Size = New Size(812, 28)
        LabelSseSkinHeader.TabIndex = 0
        LabelSseSkinHeader.Text = "RaceMenu skin overrides (NiOverride body-paint per slot). Loaded/saved with the .jslot + sidecar."
        ' 
        ' SseSkinLeftPanel
        ' 
        SseSkinLeftPanel.ColumnCount = 1
        SseSkinLeftPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseSkinLeftPanel.Controls.Add(ListBoxSseSkinOverrides, 0, 0)
        SseSkinLeftPanel.Controls.Add(FlowSseSkinButtons, 0, 1)
        SseSkinLeftPanel.Dock = DockStyle.Fill
        SseSkinLeftPanel.Location = New Point(3, 37)
        SseSkinLeftPanel.Name = "SseSkinLeftPanel"
        SseSkinLeftPanel.RowCount = 2
        SseSkinLeftPanel.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SseSkinLeftPanel.RowStyles.Add(New RowStyle())
        SseSkinLeftPanel.Size = New Size(403, 349)
        SseSkinLeftPanel.TabIndex = 1
        ' 
        ' ListBoxSseSkinOverrides
        ' 
        ListBoxSseSkinOverrides.Dock = DockStyle.Fill
        ListBoxSseSkinOverrides.DrawMode = DrawMode.OwnerDrawFixed
        ListBoxSseSkinOverrides.IntegralHeight = False
        ListBoxSseSkinOverrides.Location = New Point(0, 0)
        ListBoxSseSkinOverrides.Margin = New Padding(0)
        ListBoxSseSkinOverrides.Name = "ListBoxSseSkinOverrides"
        ListBoxSseSkinOverrides.Size = New Size(403, 312)
        ListBoxSseSkinOverrides.TabIndex = 0
        ' 
        ' FlowSseSkinButtons
        ' 
        FlowSseSkinButtons.AutoSize = True
        FlowSseSkinButtons.Controls.Add(ButtonSseSkinAdd)
        FlowSseSkinButtons.Controls.Add(ButtonSseSkinRemove)
        FlowSseSkinButtons.Dock = DockStyle.Fill
        FlowSseSkinButtons.Location = New Point(0, 315)
        FlowSseSkinButtons.Margin = New Padding(0, 3, 0, 3)
        FlowSseSkinButtons.Name = "FlowSseSkinButtons"
        FlowSseSkinButtons.Size = New Size(403, 31)
        FlowSseSkinButtons.TabIndex = 1
        FlowSseSkinButtons.WrapContents = False
        ' 
        ' ButtonSseSkinAdd
        ' 
        ButtonSseSkinAdd.AutoSize = True
        ButtonSseSkinAdd.Location = New Point(3, 3)
        ButtonSseSkinAdd.Name = "ButtonSseSkinAdd"
        ButtonSseSkinAdd.Size = New Size(50, 25)
        ButtonSseSkinAdd.TabIndex = 0
        ButtonSseSkinAdd.Text = "Add"
        ' 
        ' ButtonSseSkinRemove
        ' 
        ButtonSseSkinRemove.AutoSize = True
        ButtonSseSkinRemove.Location = New Point(59, 3)
        ButtonSseSkinRemove.Name = "ButtonSseSkinRemove"
        ButtonSseSkinRemove.Size = New Size(68, 25)
        ButtonSseSkinRemove.TabIndex = 1
        ButtonSseSkinRemove.Text = "Remove"
        ' 
        ' GroupBoxSseSkinSlots
        ' 
        SseSkinRoot.SetColumnSpan(GroupBoxSseSkinSlots, 2)
        GroupBoxSseSkinSlots.Controls.Add(FlowSseSkinSlots)
        GroupBoxSseSkinSlots.Dock = DockStyle.Fill
        GroupBoxSseSkinSlots.Location = New Point(3, 392)
        GroupBoxSseSkinSlots.Name = "GroupBoxSseSkinSlots"
        GroupBoxSseSkinSlots.Size = New Size(812, 285)
        GroupBoxSseSkinSlots.TabIndex = 2
        GroupBoxSseSkinSlots.TabStop = False
        GroupBoxSseSkinSlots.Text = "Biped slots — this override's slotMask (check the slots it targets)"
        ' 
        ' FlowSseSkinSlots
        ' 
        FlowSseSkinSlots.Dock = DockStyle.Fill
        FlowSseSkinSlots.Location = New Point(3, 19)
        FlowSseSkinSlots.Name = "FlowSseSkinSlots"
        FlowSseSkinSlots.Size = New Size(806, 263)
        FlowSseSkinSlots.TabIndex = 0
        ' 
        ' SseSkinDetail
        ' 
        SseSkinDetail.AutoScroll = True
        SseSkinDetail.ColumnCount = 4
        SseSkinDetail.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 108F))
        SseSkinDetail.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseSkinDetail.ColumnStyles.Add(New ColumnStyle())
        SseSkinDetail.ColumnStyles.Add(New ColumnStyle())
        SseSkinDetail.Controls.Add(LabelSseSkinTex0, 0, 0)
        SseSkinDetail.Controls.Add(TextBoxSseSkinTex0, 1, 0)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexPick0, 2, 0)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexClear0, 3, 0)
        SseSkinDetail.Controls.Add(LabelSseSkinTex1, 0, 1)
        SseSkinDetail.Controls.Add(TextBoxSseSkinTex1, 1, 1)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexPick1, 2, 1)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexClear1, 3, 1)
        SseSkinDetail.Controls.Add(LabelSseSkinTex2, 0, 2)
        SseSkinDetail.Controls.Add(TextBoxSseSkinTex2, 1, 2)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexPick2, 2, 2)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexClear2, 3, 2)
        SseSkinDetail.Controls.Add(LabelSseSkinTex7, 0, 3)
        SseSkinDetail.Controls.Add(TextBoxSseSkinTex7, 1, 3)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexPick7, 2, 3)
        SseSkinDetail.Controls.Add(ButtonSseSkinTexClear7, 3, 3)
        SseSkinDetail.Controls.Add(CheckBoxSseSkinTint, 0, 4)
        SseSkinDetail.Controls.Add(ButtonSseSkinTintColor, 1, 4)
        SseSkinDetail.Controls.Add(LabelSseSkinOpacity, 0, 5)
        SseSkinDetail.Controls.Add(SliderSseSkinAlpha, 1, 5)
        SseSkinDetail.Dock = DockStyle.Fill
        SseSkinDetail.Location = New Point(412, 37)
        SseSkinDetail.Name = "SseSkinDetail"
        SseSkinDetail.RowCount = 7
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle())
        SseSkinDetail.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SseSkinDetail.Size = New Size(403, 349)
        SseSkinDetail.TabIndex = 3
        ' 
        ' LabelSseSkinTex0
        ' 
        LabelSseSkinTex0.Anchor = AnchorStyles.Left
        LabelSseSkinTex0.AutoSize = True
        LabelSseSkinTex0.Location = New Point(3, 10)
        LabelSseSkinTex0.Margin = New Padding(3, 8, 3, 0)
        LabelSseSkinTex0.Name = "LabelSseSkinTex0"
        LabelSseSkinTex0.Size = New Size(47, 15)
        LabelSseSkinTex0.TabIndex = 0
        LabelSseSkinTex0.Text = "Diffuse:"
        ' 
        ' TextBoxSseSkinTex0
        ' 
        TextBoxSseSkinTex0.Dock = DockStyle.Fill
        TextBoxSseSkinTex0.Location = New Point(108, 4)
        TextBoxSseSkinTex0.Margin = New Padding(0, 4, 3, 0)
        TextBoxSseSkinTex0.Name = "TextBoxSseSkinTex0"
        TextBoxSseSkinTex0.ReadOnly = True
        TextBoxSseSkinTex0.Size = New Size(235, 23)
        TextBoxSseSkinTex0.TabIndex = 1
        ' 
        ' ButtonSseSkinTexPick0
        ' 
        ButtonSseSkinTexPick0.Location = New Point(346, 3)
        ButtonSseSkinTexPick0.Margin = New Padding(0, 3, 2, 0)
        ButtonSseSkinTexPick0.Name = "ButtonSseSkinTexPick0"
        ButtonSseSkinTexPick0.Size = New Size(26, 23)
        ButtonSseSkinTexPick0.TabIndex = 2
        ButtonSseSkinTexPick0.Text = "…"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexPick0, "Pick texture…")
        ' 
        ' ButtonSseSkinTexClear0
        ' 
        ButtonSseSkinTexClear0.Location = New Point(374, 3)
        ButtonSseSkinTexClear0.Margin = New Padding(0, 3, 3, 0)
        ButtonSseSkinTexClear0.Name = "ButtonSseSkinTexClear0"
        ButtonSseSkinTexClear0.Size = New Size(26, 23)
        ButtonSseSkinTexClear0.TabIndex = 3
        ButtonSseSkinTexClear0.Text = "×"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexClear0, "Clear")
        ' 
        ' LabelSseSkinTex1
        ' 
        LabelSseSkinTex1.Anchor = AnchorStyles.Left
        LabelSseSkinTex1.AutoSize = True
        LabelSseSkinTex1.Location = New Point(3, 37)
        LabelSseSkinTex1.Margin = New Padding(3, 8, 3, 0)
        LabelSseSkinTex1.Name = "LabelSseSkinTex1"
        LabelSseSkinTex1.Size = New Size(50, 15)
        LabelSseSkinTex1.TabIndex = 4
        LabelSseSkinTex1.Text = "Normal:"
        ' 
        ' TextBoxSseSkinTex1
        ' 
        TextBoxSseSkinTex1.Dock = DockStyle.Fill
        TextBoxSseSkinTex1.Location = New Point(108, 31)
        TextBoxSseSkinTex1.Margin = New Padding(0, 4, 3, 0)
        TextBoxSseSkinTex1.Name = "TextBoxSseSkinTex1"
        TextBoxSseSkinTex1.ReadOnly = True
        TextBoxSseSkinTex1.Size = New Size(235, 23)
        TextBoxSseSkinTex1.TabIndex = 5
        ' 
        ' ButtonSseSkinTexPick1
        ' 
        ButtonSseSkinTexPick1.Location = New Point(346, 30)
        ButtonSseSkinTexPick1.Margin = New Padding(0, 3, 2, 0)
        ButtonSseSkinTexPick1.Name = "ButtonSseSkinTexPick1"
        ButtonSseSkinTexPick1.Size = New Size(26, 23)
        ButtonSseSkinTexPick1.TabIndex = 6
        ButtonSseSkinTexPick1.Text = "…"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexPick1, "Pick texture…")
        ' 
        ' ButtonSseSkinTexClear1
        ' 
        ButtonSseSkinTexClear1.Location = New Point(374, 30)
        ButtonSseSkinTexClear1.Margin = New Padding(0, 3, 3, 0)
        ButtonSseSkinTexClear1.Name = "ButtonSseSkinTexClear1"
        ButtonSseSkinTexClear1.Size = New Size(26, 23)
        ButtonSseSkinTexClear1.TabIndex = 7
        ButtonSseSkinTexClear1.Text = "×"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexClear1, "Clear")
        ' 
        ' LabelSseSkinTex2
        ' 
        LabelSseSkinTex2.Anchor = AnchorStyles.Left
        LabelSseSkinTex2.AutoSize = True
        LabelSseSkinTex2.Location = New Point(3, 64)
        LabelSseSkinTex2.Margin = New Padding(3, 8, 3, 0)
        LabelSseSkinTex2.Name = "LabelSseSkinTex2"
        LabelSseSkinTex2.Size = New Size(92, 15)
        LabelSseSkinTex2.TabIndex = 8
        LabelSseSkinTex2.Text = "Subsurface (SK):"
        ' 
        ' TextBoxSseSkinTex2
        ' 
        TextBoxSseSkinTex2.Dock = DockStyle.Fill
        TextBoxSseSkinTex2.Location = New Point(108, 58)
        TextBoxSseSkinTex2.Margin = New Padding(0, 4, 3, 0)
        TextBoxSseSkinTex2.Name = "TextBoxSseSkinTex2"
        TextBoxSseSkinTex2.ReadOnly = True
        TextBoxSseSkinTex2.Size = New Size(235, 23)
        TextBoxSseSkinTex2.TabIndex = 9
        ' 
        ' ButtonSseSkinTexPick2
        ' 
        ButtonSseSkinTexPick2.Location = New Point(346, 57)
        ButtonSseSkinTexPick2.Margin = New Padding(0, 3, 2, 0)
        ButtonSseSkinTexPick2.Name = "ButtonSseSkinTexPick2"
        ButtonSseSkinTexPick2.Size = New Size(26, 23)
        ButtonSseSkinTexPick2.TabIndex = 10
        ButtonSseSkinTexPick2.Text = "…"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexPick2, "Pick texture…")
        ' 
        ' ButtonSseSkinTexClear2
        ' 
        ButtonSseSkinTexClear2.Location = New Point(374, 57)
        ButtonSseSkinTexClear2.Margin = New Padding(0, 3, 3, 0)
        ButtonSseSkinTexClear2.Name = "ButtonSseSkinTexClear2"
        ButtonSseSkinTexClear2.Size = New Size(26, 23)
        ButtonSseSkinTexClear2.TabIndex = 11
        ButtonSseSkinTexClear2.Text = "×"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexClear2, "Clear")
        ' 
        ' LabelSseSkinTex7
        ' 
        LabelSseSkinTex7.Anchor = AnchorStyles.Left
        LabelSseSkinTex7.AutoSize = True
        LabelSseSkinTex7.Location = New Point(3, 91)
        LabelSseSkinTex7.Margin = New Padding(3, 8, 3, 0)
        LabelSseSkinTex7.Name = "LabelSseSkinTex7"
        LabelSseSkinTex7.Size = New Size(55, 15)
        LabelSseSkinTex7.TabIndex = 12
        LabelSseSkinTex7.Text = "Specular:"
        ' 
        ' TextBoxSseSkinTex7
        ' 
        TextBoxSseSkinTex7.Dock = DockStyle.Fill
        TextBoxSseSkinTex7.Location = New Point(108, 85)
        TextBoxSseSkinTex7.Margin = New Padding(0, 4, 3, 0)
        TextBoxSseSkinTex7.Name = "TextBoxSseSkinTex7"
        TextBoxSseSkinTex7.ReadOnly = True
        TextBoxSseSkinTex7.Size = New Size(235, 23)
        TextBoxSseSkinTex7.TabIndex = 13
        ' 
        ' ButtonSseSkinTexPick7
        ' 
        ButtonSseSkinTexPick7.Location = New Point(346, 84)
        ButtonSseSkinTexPick7.Margin = New Padding(0, 3, 2, 0)
        ButtonSseSkinTexPick7.Name = "ButtonSseSkinTexPick7"
        ButtonSseSkinTexPick7.Size = New Size(26, 23)
        ButtonSseSkinTexPick7.TabIndex = 14
        ButtonSseSkinTexPick7.Text = "…"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexPick7, "Pick texture…")
        ' 
        ' ButtonSseSkinTexClear7
        ' 
        ButtonSseSkinTexClear7.Location = New Point(374, 84)
        ButtonSseSkinTexClear7.Margin = New Padding(0, 3, 3, 0)
        ButtonSseSkinTexClear7.Name = "ButtonSseSkinTexClear7"
        ButtonSseSkinTexClear7.Size = New Size(26, 23)
        ButtonSseSkinTexClear7.TabIndex = 15
        ButtonSseSkinTexClear7.Text = "×"
        ToolTipSseSkin.SetToolTip(ButtonSseSkinTexClear7, "Clear")
        ' 
        ' CheckBoxSseSkinTint
        ' 
        CheckBoxSseSkinTint.AutoSize = True
        CheckBoxSseSkinTint.Location = New Point(3, 118)
        CheckBoxSseSkinTint.Margin = New Padding(3, 10, 3, 0)
        CheckBoxSseSkinTint.Name = "CheckBoxSseSkinTint"
        CheckBoxSseSkinTint.Size = New Size(47, 19)
        CheckBoxSseSkinTint.TabIndex = 16
        CheckBoxSseSkinTint.Text = "Tint"
        ' 
        ' ButtonSseSkinTintColor
        ' 
        ButtonSseSkinTintColor.Anchor = AnchorStyles.Left
        ButtonSseSkinTintColor.AutoSize = True
        SseSkinDetail.SetColumnSpan(ButtonSseSkinTintColor, 3)
        ButtonSseSkinTintColor.Location = New Point(108, 114)
        ButtonSseSkinTintColor.Margin = New Padding(0, 6, 3, 0)
        ButtonSseSkinTintColor.Name = "ButtonSseSkinTintColor"
        ButtonSseSkinTintColor.Size = New Size(63, 25)
        ButtonSseSkinTintColor.TabIndex = 17
        ButtonSseSkinTintColor.Text = "Color…"
        ' 
        ' LabelSseSkinOpacity
        ' 
        LabelSseSkinOpacity.Anchor = AnchorStyles.Left
        LabelSseSkinOpacity.AutoSize = True
        LabelSseSkinOpacity.Location = New Point(3, 153)
        LabelSseSkinOpacity.Margin = New Padding(3, 10, 3, 0)
        LabelSseSkinOpacity.Name = "LabelSseSkinOpacity"
        LabelSseSkinOpacity.Size = New Size(51, 15)
        LabelSseSkinOpacity.TabIndex = 18
        LabelSseSkinOpacity.Text = "Opacity:"
        ' 
        ' SliderSseSkinAlpha
        ' 
        SliderSseSkinAlpha.AccentColor = SystemColors.HotTrack
        SliderSseSkinAlpha.BackColor = SystemColors.Control
        SseSkinDetail.SetColumnSpan(SliderSseSkinAlpha, 3)
        SliderSseSkinAlpha.DisplayFormat = "0.00"
        SliderSseSkinAlpha.Dock = DockStyle.Fill
        SliderSseSkinAlpha.LargeChange = 0.1R
        SliderSseSkinAlpha.Location = New Point(108, 143)
        SliderSseSkinAlpha.Margin = New Padding(0, 4, 8, 3)
        SliderSseSkinAlpha.Maximum = 1R
        SliderSseSkinAlpha.MinimumSize = New Size(100, 24)
        SliderSseSkinAlpha.Name = "SliderSseSkinAlpha"
        SliderSseSkinAlpha.Size = New Size(287, 26)
        SliderSseSkinAlpha.SmallChange = 0.01R
        SliderSseSkinAlpha.TabIndex = 19
        SliderSseSkinAlpha.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSseSkinAlpha.ThumbColor = SystemColors.HotTrack
        SliderSseSkinAlpha.ThumbRadius = 4F
        SliderSseSkinAlpha.TrackColor = SystemColors.ControlDark
        ' 
        ' TabPageOverlays
        ' 
        TabPageOverlays.Controls.Add(OverlaysTabLayout)
        TabPageOverlays.Location = New Point(4, 24)
        TabPageOverlays.Name = "TabPageOverlays"
        TabPageOverlays.Padding = New Padding(6)
        TabPageOverlays.Size = New Size(830, 692)
        TabPageOverlays.TabIndex = 2
        TabPageOverlays.Text = "Overlays (RM/LM)"
        ' 
        ' OverlaysTabLayout
        ' 
        OverlaysTabLayout.ColumnCount = 1
        OverlaysTabLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OverlaysTabLayout.Controls.Add(OverlayListsLayout, 0, 0)
        OverlaysTabLayout.Controls.Add(GroupBoxOverlayProps, 0, 1)
        OverlaysTabLayout.Dock = DockStyle.Fill
        OverlaysTabLayout.Location = New Point(6, 6)
        OverlaysTabLayout.Name = "OverlaysTabLayout"
        OverlaysTabLayout.RowCount = 2
        OverlaysTabLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        OverlaysTabLayout.RowStyles.Add(New RowStyle())
        OverlaysTabLayout.Size = New Size(818, 680)
        OverlaysTabLayout.TabIndex = 0
        ' 
        ' OverlayListsLayout
        ' 
        OverlayListsLayout.ColumnCount = 3
        OverlayListsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        OverlayListsLayout.ColumnStyles.Add(New ColumnStyle())
        OverlayListsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 50F))
        OverlayListsLayout.Controls.Add(GroupBoxOverlayAvailable, 0, 0)
        OverlayListsLayout.Controls.Add(OverlayCenterLayout, 1, 0)
        OverlayListsLayout.Controls.Add(GroupBoxOverlayApplied, 2, 0)
        OverlayListsLayout.Dock = DockStyle.Fill
        OverlayListsLayout.Location = New Point(3, 3)
        OverlayListsLayout.Name = "OverlayListsLayout"
        OverlayListsLayout.RowCount = 1
        OverlayListsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        OverlayListsLayout.Size = New Size(812, 353)
        OverlayListsLayout.TabIndex = 0
        ' 
        ' GroupBoxOverlayAvailable
        ' 
        GroupBoxOverlayAvailable.Controls.Add(OverlayAvailableLayout)
        GroupBoxOverlayAvailable.Dock = DockStyle.Fill
        GroupBoxOverlayAvailable.Location = New Point(3, 3)
        GroupBoxOverlayAvailable.Name = "GroupBoxOverlayAvailable"
        GroupBoxOverlayAvailable.Size = New Size(333, 347)
        GroupBoxOverlayAvailable.TabIndex = 0
        GroupBoxOverlayAvailable.TabStop = False
        GroupBoxOverlayAvailable.Text = "Available overlays"
        ' 
        ' OverlayAvailableLayout
        ' 
        OverlayAvailableLayout.ColumnCount = 1
        OverlayAvailableLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OverlayAvailableLayout.Controls.Add(TextBoxOverlayFilter, 0, 0)
        OverlayAvailableLayout.Controls.Add(ListBoxOverlayAvailable, 0, 1)
        OverlayAvailableLayout.Dock = DockStyle.Fill
        OverlayAvailableLayout.Location = New Point(3, 19)
        OverlayAvailableLayout.Name = "OverlayAvailableLayout"
        OverlayAvailableLayout.Padding = New Padding(4)
        OverlayAvailableLayout.RowCount = 2
        OverlayAvailableLayout.RowStyles.Add(New RowStyle())
        OverlayAvailableLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        OverlayAvailableLayout.Size = New Size(327, 325)
        OverlayAvailableLayout.TabIndex = 0
        ' 
        ' TextBoxOverlayFilter
        ' 
        TextBoxOverlayFilter.Dock = DockStyle.Top
        TextBoxOverlayFilter.Location = New Point(7, 7)
        TextBoxOverlayFilter.Name = "TextBoxOverlayFilter"
        TextBoxOverlayFilter.PlaceholderText = "Filter overlays…"
        TextBoxOverlayFilter.Size = New Size(313, 23)
        TextBoxOverlayFilter.TabIndex = 0
        ' 
        ' ListBoxOverlayAvailable
        ' 
        ListBoxOverlayAvailable.Dock = DockStyle.Fill
        ListBoxOverlayAvailable.IntegralHeight = False
        ListBoxOverlayAvailable.ItemHeight = 15
        ListBoxOverlayAvailable.Location = New Point(7, 36)
        ListBoxOverlayAvailable.Name = "ListBoxOverlayAvailable"
        ListBoxOverlayAvailable.Size = New Size(313, 282)
        ListBoxOverlayAvailable.TabIndex = 1
        ' 
        ' OverlayCenterLayout
        ' 
        OverlayCenterLayout.Anchor = AnchorStyles.None
        OverlayCenterLayout.AutoSize = True
        OverlayCenterLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayCenterLayout.ColumnCount = 1
        OverlayCenterLayout.ColumnStyles.Add(New ColumnStyle())
        OverlayCenterLayout.Controls.Add(FlowSseOverlayZone, 0, 0)
        OverlayCenterLayout.Controls.Add(ButtonOverlayAdd, 0, 1)
        OverlayCenterLayout.Controls.Add(ButtonOverlayRemove, 0, 2)
        OverlayCenterLayout.Location = New Point(342, 129)
        OverlayCenterLayout.Name = "OverlayCenterLayout"
        OverlayCenterLayout.RowCount = 3
        OverlayCenterLayout.RowStyles.Add(New RowStyle())
        OverlayCenterLayout.RowStyles.Add(New RowStyle())
        OverlayCenterLayout.RowStyles.Add(New RowStyle())
        OverlayCenterLayout.Size = New Size(128, 94)
        OverlayCenterLayout.TabIndex = 1
        ' 
        ' FlowSseOverlayZone
        ' 
        FlowSseOverlayZone.AutoSize = True
        FlowSseOverlayZone.Controls.Add(LabelSseOverlayZone)
        FlowSseOverlayZone.Controls.Add(ComboBoxSseOverlayZone)
        FlowSseOverlayZone.Location = New Point(0, 0)
        FlowSseOverlayZone.Margin = New Padding(0)
        FlowSseOverlayZone.Name = "FlowSseOverlayZone"
        FlowSseOverlayZone.Size = New Size(128, 32)
        FlowSseOverlayZone.TabIndex = 0
        FlowSseOverlayZone.Visible = False
        FlowSseOverlayZone.WrapContents = False
        ' 
        ' LabelSseOverlayZone
        ' 
        LabelSseOverlayZone.AutoSize = True
        LabelSseOverlayZone.Location = New Point(0, 7)
        LabelSseOverlayZone.Margin = New Padding(0, 7, 3, 0)
        LabelSseOverlayZone.Name = "LabelSseOverlayZone"
        LabelSseOverlayZone.Size = New Size(37, 15)
        LabelSseOverlayZone.TabIndex = 0
        LabelSseOverlayZone.Text = "Zone:"
        ' 
        ' ComboBoxSseOverlayZone
        ' 
        ComboBoxSseOverlayZone.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxSseOverlayZone.Items.AddRange(New Object() {"Body", "Hands", "Feet"})
        ComboBoxSseOverlayZone.Location = New Point(43, 3)
        ComboBoxSseOverlayZone.Margin = New Padding(3, 3, 3, 6)
        ComboBoxSseOverlayZone.Name = "ComboBoxSseOverlayZone"
        ComboBoxSseOverlayZone.Size = New Size(82, 23)
        ComboBoxSseOverlayZone.TabIndex = 1
        ' 
        ' ButtonOverlayAdd
        ' 
        ButtonOverlayAdd.AutoSize = True
        ButtonOverlayAdd.Location = New Point(3, 35)
        ButtonOverlayAdd.Name = "ButtonOverlayAdd"
        ButtonOverlayAdd.Size = New Size(73, 25)
        ButtonOverlayAdd.TabIndex = 1
        ButtonOverlayAdd.Text = "Add →"
        ' 
        ' ButtonOverlayRemove
        ' 
        ButtonOverlayRemove.AutoSize = True
        ButtonOverlayRemove.Location = New Point(3, 66)
        ButtonOverlayRemove.Name = "ButtonOverlayRemove"
        ButtonOverlayRemove.Size = New Size(73, 25)
        ButtonOverlayRemove.TabIndex = 2
        ButtonOverlayRemove.Text = "← Remove"
        ' 
        ' GroupBoxOverlayApplied
        ' 
        GroupBoxOverlayApplied.Controls.Add(OverlayAppliedLayout)
        GroupBoxOverlayApplied.Dock = DockStyle.Fill
        GroupBoxOverlayApplied.Location = New Point(476, 3)
        GroupBoxOverlayApplied.Name = "GroupBoxOverlayApplied"
        GroupBoxOverlayApplied.Size = New Size(333, 347)
        GroupBoxOverlayApplied.TabIndex = 2
        GroupBoxOverlayApplied.TabStop = False
        GroupBoxOverlayApplied.Text = "Applied overlays (top = drawn on top)"
        ' 
        ' OverlayAppliedLayout
        ' 
        OverlayAppliedLayout.ColumnCount = 1
        OverlayAppliedLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OverlayAppliedLayout.Controls.Add(ListBoxOverlayApplied, 0, 0)
        OverlayAppliedLayout.Controls.Add(OverlayAppliedButtons, 0, 1)
        OverlayAppliedLayout.Dock = DockStyle.Fill
        OverlayAppliedLayout.Location = New Point(3, 19)
        OverlayAppliedLayout.Name = "OverlayAppliedLayout"
        OverlayAppliedLayout.Padding = New Padding(4)
        OverlayAppliedLayout.RowCount = 2
        OverlayAppliedLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        OverlayAppliedLayout.RowStyles.Add(New RowStyle())
        OverlayAppliedLayout.Size = New Size(327, 325)
        OverlayAppliedLayout.TabIndex = 0
        ' 
        ' ListBoxOverlayApplied
        ' 
        ListBoxOverlayApplied.Dock = DockStyle.Fill
        ListBoxOverlayApplied.IntegralHeight = False
        ListBoxOverlayApplied.ItemHeight = 15
        ListBoxOverlayApplied.Location = New Point(7, 7)
        ListBoxOverlayApplied.Name = "ListBoxOverlayApplied"
        ListBoxOverlayApplied.Size = New Size(313, 274)
        ListBoxOverlayApplied.TabIndex = 0
        ' 
        ' OverlayAppliedButtons
        ' 
        OverlayAppliedButtons.AutoSize = True
        OverlayAppliedButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayAppliedButtons.Controls.Add(ButtonOverlayUp)
        OverlayAppliedButtons.Controls.Add(ButtonOverlayDown)
        OverlayAppliedButtons.Dock = DockStyle.Fill
        OverlayAppliedButtons.Location = New Point(7, 287)
        OverlayAppliedButtons.Name = "OverlayAppliedButtons"
        OverlayAppliedButtons.Size = New Size(313, 31)
        OverlayAppliedButtons.TabIndex = 1
        ' 
        ' ButtonOverlayUp
        ' 
        ButtonOverlayUp.AutoSize = True
        ButtonOverlayUp.Location = New Point(3, 3)
        ButtonOverlayUp.Name = "ButtonOverlayUp"
        ButtonOverlayUp.Size = New Size(60, 25)
        ButtonOverlayUp.TabIndex = 0
        ButtonOverlayUp.Text = "Up"
        ' 
        ' ButtonOverlayDown
        ' 
        ButtonOverlayDown.AutoSize = True
        ButtonOverlayDown.Location = New Point(69, 3)
        ButtonOverlayDown.Name = "ButtonOverlayDown"
        ButtonOverlayDown.Size = New Size(60, 25)
        ButtonOverlayDown.TabIndex = 1
        ButtonOverlayDown.Text = "Down"
        ' 
        ' GroupBoxOverlayProps
        ' 
        GroupBoxOverlayProps.AutoSize = True
        GroupBoxOverlayProps.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxOverlayProps.Controls.Add(OverlayPropsLayout)
        GroupBoxOverlayProps.Dock = DockStyle.Fill
        GroupBoxOverlayProps.Location = New Point(3, 362)
        GroupBoxOverlayProps.Name = "GroupBoxOverlayProps"
        GroupBoxOverlayProps.Size = New Size(812, 315)
        GroupBoxOverlayProps.TabIndex = 1
        GroupBoxOverlayProps.TabStop = False
        GroupBoxOverlayProps.Text = "Overlay properties"
        ' 
        ' OverlayPropsLayout
        ' 
        OverlayPropsLayout.AutoSize = True
        OverlayPropsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayPropsLayout.ColumnCount = 2
        OverlayPropsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        OverlayPropsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OverlayPropsLayout.Controls.Add(LabelOverlaySelected, 0, 0)
        OverlayPropsLayout.Controls.Add(LabelOverlayOffsetU, 0, 1)
        OverlayPropsLayout.Controls.Add(SliderOverlayOffsetU, 1, 1)
        OverlayPropsLayout.Controls.Add(LabelOverlayOffsetV, 0, 2)
        OverlayPropsLayout.Controls.Add(SliderOverlayOffsetV, 1, 2)
        OverlayPropsLayout.Controls.Add(LabelOverlayScaleU, 0, 3)
        OverlayPropsLayout.Controls.Add(SliderOverlayScaleU, 1, 3)
        OverlayPropsLayout.Controls.Add(LabelOverlayScaleV, 0, 4)
        OverlayPropsLayout.Controls.Add(SliderOverlayScaleV, 1, 4)
        OverlayPropsLayout.Controls.Add(CheckBoxOverlayTint, 0, 5)
        OverlayPropsLayout.Controls.Add(OverlayTintRowLayout, 1, 5)
        OverlayPropsLayout.Controls.Add(LabelSseOverlayTexture, 0, 6)
        OverlayPropsLayout.Controls.Add(SseOverlayDiffuseRow, 1, 6)
        OverlayPropsLayout.Controls.Add(LabelSseOverlayNormal, 0, 7)
        OverlayPropsLayout.Controls.Add(SseOverlayNormalRow, 1, 7)
        OverlayPropsLayout.Controls.Add(CheckBoxSseOverlayMagic, 1, 8)
        OverlayPropsLayout.Controls.Add(LabelSseOverlayMagicNote, 1, 9)
        OverlayPropsLayout.Dock = DockStyle.Fill
        OverlayPropsLayout.Location = New Point(3, 19)
        OverlayPropsLayout.Name = "OverlayPropsLayout"
        OverlayPropsLayout.Padding = New Padding(4)
        OverlayPropsLayout.RowCount = 10
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.Size = New Size(806, 293)
        OverlayPropsLayout.TabIndex = 0
        ' 
        ' LabelOverlaySelected
        ' 
        LabelOverlaySelected.AutoSize = True
        OverlayPropsLayout.SetColumnSpan(LabelOverlaySelected, 2)
        LabelOverlaySelected.ForeColor = SystemColors.GrayText
        LabelOverlaySelected.Location = New Point(7, 8)
        LabelOverlaySelected.Margin = New Padding(3, 4, 3, 6)
        LabelOverlaySelected.Name = "LabelOverlaySelected"
        LabelOverlaySelected.Size = New Size(116, 15)
        LabelOverlaySelected.TabIndex = 0
        LabelOverlaySelected.Text = "(no overlay selected)"
        ' 
        ' LabelOverlayOffsetU
        ' 
        LabelOverlayOffsetU.Anchor = AnchorStyles.Left
        LabelOverlayOffsetU.AutoSize = True
        LabelOverlayOffsetU.Location = New Point(7, 37)
        LabelOverlayOffsetU.Name = "LabelOverlayOffsetU"
        LabelOverlayOffsetU.Size = New Size(53, 15)
        LabelOverlayOffsetU.TabIndex = 1
        LabelOverlayOffsetU.Text = "Offset U:"
        LabelOverlayOffsetU.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderOverlayOffsetU
        ' 
        SliderOverlayOffsetU.AccentColor = SystemColors.HotTrack
        SliderOverlayOffsetU.BackColor = SystemColors.Control
        SliderOverlayOffsetU.DisplayFormat = "0.000"
        SliderOverlayOffsetU.Dock = DockStyle.Fill
        SliderOverlayOffsetU.FillMode = TinySliderFillMode.Center
        SliderOverlayOffsetU.LargeChange = 0.4R
        SliderOverlayOffsetU.Location = New Point(96, 31)
        SliderOverlayOffsetU.Margin = New Padding(2)
        SliderOverlayOffsetU.Maximum = 2R
        SliderOverlayOffsetU.Minimum = -2R
        SliderOverlayOffsetU.MinimumSize = New Size(140, 22)
        SliderOverlayOffsetU.Name = "SliderOverlayOffsetU"
        SliderOverlayOffsetU.Size = New Size(704, 28)
        SliderOverlayOffsetU.SmallChange = 0.001R
        SliderOverlayOffsetU.TabIndex = 2
        SliderOverlayOffsetU.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayOffsetU.ThumbColor = SystemColors.HotTrack
        SliderOverlayOffsetU.ThumbRadius = 4F
        SliderOverlayOffsetU.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelOverlayOffsetV
        ' 
        LabelOverlayOffsetV.Anchor = AnchorStyles.Left
        LabelOverlayOffsetV.AutoSize = True
        LabelOverlayOffsetV.Location = New Point(7, 69)
        LabelOverlayOffsetV.Name = "LabelOverlayOffsetV"
        LabelOverlayOffsetV.Size = New Size(52, 15)
        LabelOverlayOffsetV.TabIndex = 3
        LabelOverlayOffsetV.Text = "Offset V:"
        LabelOverlayOffsetV.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderOverlayOffsetV
        ' 
        SliderOverlayOffsetV.AccentColor = SystemColors.HotTrack
        SliderOverlayOffsetV.BackColor = SystemColors.Control
        SliderOverlayOffsetV.DisplayFormat = "0.000"
        SliderOverlayOffsetV.Dock = DockStyle.Fill
        SliderOverlayOffsetV.FillMode = TinySliderFillMode.Center
        SliderOverlayOffsetV.LargeChange = 0.4R
        SliderOverlayOffsetV.Location = New Point(96, 63)
        SliderOverlayOffsetV.Margin = New Padding(2)
        SliderOverlayOffsetV.Maximum = 2R
        SliderOverlayOffsetV.Minimum = -2R
        SliderOverlayOffsetV.MinimumSize = New Size(140, 22)
        SliderOverlayOffsetV.Name = "SliderOverlayOffsetV"
        SliderOverlayOffsetV.Size = New Size(704, 28)
        SliderOverlayOffsetV.SmallChange = 0.001R
        SliderOverlayOffsetV.TabIndex = 4
        SliderOverlayOffsetV.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayOffsetV.ThumbColor = SystemColors.HotTrack
        SliderOverlayOffsetV.ThumbRadius = 4F
        SliderOverlayOffsetV.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelOverlayScaleU
        ' 
        LabelOverlayScaleU.Anchor = AnchorStyles.Left
        LabelOverlayScaleU.AutoSize = True
        LabelOverlayScaleU.Location = New Point(7, 101)
        LabelOverlayScaleU.Name = "LabelOverlayScaleU"
        LabelOverlayScaleU.Size = New Size(48, 15)
        LabelOverlayScaleU.TabIndex = 5
        LabelOverlayScaleU.Text = "Scale U:"
        LabelOverlayScaleU.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderOverlayScaleU
        ' 
        SliderOverlayScaleU.AccentColor = SystemColors.HotTrack
        SliderOverlayScaleU.BackColor = SystemColors.Control
        SliderOverlayScaleU.DisplayFormat = "0.000"
        SliderOverlayScaleU.Dock = DockStyle.Fill
        SliderOverlayScaleU.LargeChange = 0.4R
        SliderOverlayScaleU.Location = New Point(96, 95)
        SliderOverlayScaleU.Margin = New Padding(2)
        SliderOverlayScaleU.Maximum = 2R
        SliderOverlayScaleU.Minimum = -2R
        SliderOverlayScaleU.MinimumSize = New Size(140, 22)
        SliderOverlayScaleU.Name = "SliderOverlayScaleU"
        SliderOverlayScaleU.Size = New Size(704, 28)
        SliderOverlayScaleU.SmallChange = 0.001R
        SliderOverlayScaleU.TabIndex = 6
        SliderOverlayScaleU.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayScaleU.ThumbColor = SystemColors.HotTrack
        SliderOverlayScaleU.ThumbRadius = 4F
        SliderOverlayScaleU.TrackColor = SystemColors.ControlDark
        SliderOverlayScaleU.Value = 1R
        ' 
        ' LabelOverlayScaleV
        ' 
        LabelOverlayScaleV.Anchor = AnchorStyles.Left
        LabelOverlayScaleV.AutoSize = True
        LabelOverlayScaleV.Location = New Point(7, 133)
        LabelOverlayScaleV.Name = "LabelOverlayScaleV"
        LabelOverlayScaleV.Size = New Size(47, 15)
        LabelOverlayScaleV.TabIndex = 7
        LabelOverlayScaleV.Text = "Scale V:"
        LabelOverlayScaleV.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderOverlayScaleV
        ' 
        SliderOverlayScaleV.AccentColor = SystemColors.HotTrack
        SliderOverlayScaleV.BackColor = SystemColors.Control
        SliderOverlayScaleV.DisplayFormat = "0.000"
        SliderOverlayScaleV.Dock = DockStyle.Fill
        SliderOverlayScaleV.LargeChange = 0.4R
        SliderOverlayScaleV.Location = New Point(96, 127)
        SliderOverlayScaleV.Margin = New Padding(2)
        SliderOverlayScaleV.Maximum = 2R
        SliderOverlayScaleV.Minimum = -2R
        SliderOverlayScaleV.MinimumSize = New Size(140, 22)
        SliderOverlayScaleV.Name = "SliderOverlayScaleV"
        SliderOverlayScaleV.Size = New Size(704, 28)
        SliderOverlayScaleV.SmallChange = 0.001R
        SliderOverlayScaleV.TabIndex = 8
        SliderOverlayScaleV.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayScaleV.ThumbColor = SystemColors.HotTrack
        SliderOverlayScaleV.ThumbRadius = 4F
        SliderOverlayScaleV.TrackColor = SystemColors.ControlDark
        SliderOverlayScaleV.Value = 1R
        ' 
        ' CheckBoxOverlayTint
        ' 
        CheckBoxOverlayTint.Anchor = AnchorStyles.Left
        CheckBoxOverlayTint.AutoSize = True
        CheckBoxOverlayTint.Location = New Point(7, 165)
        CheckBoxOverlayTint.Name = "CheckBoxOverlayTint"
        CheckBoxOverlayTint.Size = New Size(78, 19)
        CheckBoxOverlayTint.TabIndex = 9
        CheckBoxOverlayTint.Text = "Apply tint"
        ' 
        ' OverlayTintRowLayout
        ' 
        OverlayTintRowLayout.AutoSize = True
        OverlayTintRowLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayTintRowLayout.ColumnCount = 3
        OverlayTintRowLayout.ColumnStyles.Add(New ColumnStyle())
        OverlayTintRowLayout.ColumnStyles.Add(New ColumnStyle())
        OverlayTintRowLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OverlayTintRowLayout.Controls.Add(ButtonOverlayTintColor, 0, 0)
        OverlayTintRowLayout.Controls.Add(LabelOverlayTintAlpha, 1, 0)
        OverlayTintRowLayout.Controls.Add(SliderOverlayTintAlpha, 2, 0)
        OverlayTintRowLayout.Dock = DockStyle.Fill
        OverlayTintRowLayout.Location = New Point(96, 159)
        OverlayTintRowLayout.Margin = New Padding(2)
        OverlayTintRowLayout.Name = "OverlayTintRowLayout"
        OverlayTintRowLayout.RowCount = 1
        OverlayTintRowLayout.RowStyles.Add(New RowStyle())
        OverlayTintRowLayout.Size = New Size(704, 32)
        OverlayTintRowLayout.TabIndex = 10
        ' 
        ' ButtonOverlayTintColor
        ' 
        ButtonOverlayTintColor.Anchor = AnchorStyles.Left
        ButtonOverlayTintColor.BackColor = Color.White
        ButtonOverlayTintColor.FlatStyle = FlatStyle.Flat
        ButtonOverlayTintColor.Location = New Point(3, 4)
        ButtonOverlayTintColor.Name = "ButtonOverlayTintColor"
        ButtonOverlayTintColor.Size = New Size(40, 23)
        ButtonOverlayTintColor.TabIndex = 0
        ButtonOverlayTintColor.UseVisualStyleBackColor = False
        ' 
        ' LabelOverlayTintAlpha
        ' 
        LabelOverlayTintAlpha.AutoSize = True
        LabelOverlayTintAlpha.Location = New Point(49, 6)
        LabelOverlayTintAlpha.Margin = New Padding(3, 6, 3, 0)
        LabelOverlayTintAlpha.Name = "LabelOverlayTintAlpha"
        LabelOverlayTintAlpha.Size = New Size(51, 15)
        LabelOverlayTintAlpha.TabIndex = 1
        LabelOverlayTintAlpha.Text = "Opacity:"
        LabelOverlayTintAlpha.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderOverlayTintAlpha
        ' 
        SliderOverlayTintAlpha.AccentColor = SystemColors.HotTrack
        SliderOverlayTintAlpha.BackColor = SystemColors.Control
        SliderOverlayTintAlpha.DisplayFormat = "0%"
        SliderOverlayTintAlpha.Dock = DockStyle.Fill
        SliderOverlayTintAlpha.InputScale = 0.01R
        SliderOverlayTintAlpha.LargeChange = 0.1R
        SliderOverlayTintAlpha.Location = New Point(105, 2)
        SliderOverlayTintAlpha.Margin = New Padding(2)
        SliderOverlayTintAlpha.Maximum = 1R
        SliderOverlayTintAlpha.MinimumSize = New Size(120, 22)
        SliderOverlayTintAlpha.Name = "SliderOverlayTintAlpha"
        SliderOverlayTintAlpha.Size = New Size(597, 28)
        SliderOverlayTintAlpha.SmallChange = 0.01R
        SliderOverlayTintAlpha.TabIndex = 2
        SliderOverlayTintAlpha.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayTintAlpha.ThumbColor = SystemColors.HotTrack
        SliderOverlayTintAlpha.ThumbRadius = 4F
        SliderOverlayTintAlpha.TrackColor = SystemColors.ControlDark
        SliderOverlayTintAlpha.Value = 1R
        ' 
        ' LabelSseOverlayTexture
        ' 
        LabelSseOverlayTexture.Anchor = AnchorStyles.Left
        LabelSseOverlayTexture.AutoSize = True
        LabelSseOverlayTexture.Location = New Point(7, 202)
        LabelSseOverlayTexture.Margin = New Padding(3, 6, 3, 0)
        LabelSseOverlayTexture.Name = "LabelSseOverlayTexture"
        LabelSseOverlayTexture.Size = New Size(48, 15)
        LabelSseOverlayTexture.TabIndex = 11
        LabelSseOverlayTexture.Text = "Texture:"
        LabelSseOverlayTexture.Visible = False
        ' 
        ' SseOverlayDiffuseRow
        ' 
        SseOverlayDiffuseRow.AutoSize = True
        SseOverlayDiffuseRow.ColumnCount = 1
        SseOverlayDiffuseRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseOverlayDiffuseRow.Controls.Add(TextBoxSseOverlayDiffuse, 0, 0)
        SseOverlayDiffuseRow.Dock = DockStyle.Fill
        SseOverlayDiffuseRow.Location = New Point(94, 197)
        SseOverlayDiffuseRow.Margin = New Padding(0, 4, 6, 0)
        SseOverlayDiffuseRow.Name = "SseOverlayDiffuseRow"
        SseOverlayDiffuseRow.RowCount = 1
        SseOverlayDiffuseRow.RowStyles.Add(New RowStyle())
        SseOverlayDiffuseRow.Size = New Size(702, 23)
        SseOverlayDiffuseRow.TabIndex = 12
        SseOverlayDiffuseRow.Visible = False
        ' 
        ' TextBoxSseOverlayDiffuse
        ' 
        TextBoxSseOverlayDiffuse.Dock = DockStyle.Fill
        TextBoxSseOverlayDiffuse.Location = New Point(0, 0)
        TextBoxSseOverlayDiffuse.Margin = New Padding(0)
        TextBoxSseOverlayDiffuse.Name = "TextBoxSseOverlayDiffuse"
        TextBoxSseOverlayDiffuse.ReadOnly = True
        TextBoxSseOverlayDiffuse.Size = New Size(702, 23)
        TextBoxSseOverlayDiffuse.TabIndex = 0
        ' 
        ' LabelSseOverlayNormal
        ' 
        LabelSseOverlayNormal.Anchor = AnchorStyles.Left
        LabelSseOverlayNormal.AutoSize = True
        LabelSseOverlayNormal.Location = New Point(7, 229)
        LabelSseOverlayNormal.Margin = New Padding(3, 6, 3, 0)
        LabelSseOverlayNormal.Name = "LabelSseOverlayNormal"
        LabelSseOverlayNormal.Size = New Size(50, 15)
        LabelSseOverlayNormal.TabIndex = 13
        LabelSseOverlayNormal.Text = "Normal:"
        LabelSseOverlayNormal.Visible = False
        ' 
        ' SseOverlayNormalRow
        ' 
        SseOverlayNormalRow.AutoSize = True
        SseOverlayNormalRow.ColumnCount = 1
        SseOverlayNormalRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SseOverlayNormalRow.Controls.Add(TextBoxSseOverlayNormal, 0, 0)
        SseOverlayNormalRow.Dock = DockStyle.Fill
        SseOverlayNormalRow.Location = New Point(94, 224)
        SseOverlayNormalRow.Margin = New Padding(0, 4, 6, 0)
        SseOverlayNormalRow.Name = "SseOverlayNormalRow"
        SseOverlayNormalRow.RowCount = 1
        SseOverlayNormalRow.RowStyles.Add(New RowStyle())
        SseOverlayNormalRow.Size = New Size(702, 23)
        SseOverlayNormalRow.TabIndex = 14
        SseOverlayNormalRow.Visible = False
        ' 
        ' TextBoxSseOverlayNormal
        ' 
        TextBoxSseOverlayNormal.Dock = DockStyle.Fill
        TextBoxSseOverlayNormal.Location = New Point(0, 0)
        TextBoxSseOverlayNormal.Margin = New Padding(0)
        TextBoxSseOverlayNormal.Name = "TextBoxSseOverlayNormal"
        TextBoxSseOverlayNormal.ReadOnly = True
        TextBoxSseOverlayNormal.Size = New Size(702, 23)
        TextBoxSseOverlayNormal.TabIndex = 0
        ' 
        ' CheckBoxSseOverlayMagic
        ' 
        CheckBoxSseOverlayMagic.Anchor = AnchorStyles.Left
        CheckBoxSseOverlayMagic.AutoSize = True
        CheckBoxSseOverlayMagic.Location = New Point(97, 253)
        CheckBoxSseOverlayMagic.Margin = New Padding(3, 6, 3, 0)
        CheckBoxSseOverlayMagic.Name = "CheckBoxSseOverlayMagic"
        CheckBoxSseOverlayMagic.Size = New Size(127, 19)
        CheckBoxSseOverlayMagic.TabIndex = 15
        CheckBoxSseOverlayMagic.Text = "Magic (spell effect)"
        CheckBoxSseOverlayMagic.Visible = False
        ' 
        ' LabelSseOverlayMagicNote
        ' 
        LabelSseOverlayMagicNote.AutoSize = True
        LabelSseOverlayMagicNote.ForeColor = SystemColors.GrayText
        LabelSseOverlayMagicNote.Location = New Point(97, 274)
        LabelSseOverlayMagicNote.Margin = New Padding(3, 2, 3, 0)
        LabelSseOverlayMagicNote.Name = "LabelSseOverlayMagicNote"
        LabelSseOverlayMagicNote.Size = New Size(659, 15)
        LabelSseOverlayMagicNote.TabIndex = 16
        LabelSseOverlayMagicNote.Text = "Magic overlays come from a separate slot pool (iSpellOverlays in the skee64 ini). This app paints them like any other overlay."
        LabelSseOverlayMagicNote.Visible = False
        ' 
        ' TabPageSkinTint
        ' 
        TabPageSkinTint.Controls.Add(SkinTintPanelBody)
        TabPageSkinTint.Location = New Point(4, 24)
        TabPageSkinTint.Name = "TabPageSkinTint"
        TabPageSkinTint.Padding = New Padding(6)
        TabPageSkinTint.Size = New Size(830, 692)
        TabPageSkinTint.TabIndex = 3
        TabPageSkinTint.Text = "Skin tint match"
        ' 
        ' SkinTintPanelBody
        ' 
        SkinTintPanelBody.Dock = DockStyle.Fill
        SkinTintPanelBody.Location = New Point(6, 6)
        SkinTintPanelBody.Name = "SkinTintPanelBody"
        SkinTintPanelBody.Size = New Size(818, 680)
        SkinTintPanelBody.TabIndex = 0
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
        BottomLayout.Location = New Point(11, 737)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 4, 0, 0)
        BottomLayout.Size = New Size(838, 33)
        BottomLayout.TabIndex = 1
        ' 
        ' ButtonOk
        ' 
        ButtonOk.Location = New Point(755, 7)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Location = New Point(669, 7)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' ButtonResetSection
        ' 
        ButtonResetSection.Location = New Point(553, 7)
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
        PreviewSidebar.Size = New Size(696, 781)
        PreviewSidebar.TabIndex = 0
        ' 
        ' RenderTogglesPanel
        ' 
        RenderTogglesPanel.AutoSize = True
        RenderTogglesPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RenderTogglesPanel.Controls.Add(CheckBoxRenderUnderarmor)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderArmor)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderHeadwear)
        RenderTogglesPanel.Controls.Add(CheckBoxRenderGore)
        RenderTogglesPanel.Dock = DockStyle.Fill
        RenderTogglesPanel.Location = New Point(3, 3)
        RenderTogglesPanel.Name = "RenderTogglesPanel"
        RenderTogglesPanel.Padding = New Padding(2)
        RenderTogglesPanel.Size = New Size(690, 27)
        RenderTogglesPanel.TabIndex = 0
        ' 
        ' CheckBoxRenderUnderarmor
        ' 
        CheckBoxRenderUnderarmor.AutoSize = True
        CheckBoxRenderUnderarmor.Location = New Point(6, 4)
        CheckBoxRenderUnderarmor.Margin = New Padding(4, 2, 8, 2)
        CheckBoxRenderUnderarmor.Name = "CheckBoxRenderUnderarmor"
        CheckBoxRenderUnderarmor.Size = New Size(129, 19)
        CheckBoxRenderUnderarmor.TabIndex = 0
        CheckBoxRenderUnderarmor.Text = "Render underarmor"
        ' 
        ' CheckBoxRenderArmor
        ' 
        CheckBoxRenderArmor.AutoSize = True
        CheckBoxRenderArmor.Location = New Point(147, 4)
        CheckBoxRenderArmor.Margin = New Padding(4, 2, 8, 2)
        CheckBoxRenderArmor.Name = "CheckBoxRenderArmor"
        CheckBoxRenderArmor.Size = New Size(98, 19)
        CheckBoxRenderArmor.TabIndex = 1
        CheckBoxRenderArmor.Text = "Render armor"
        ' 
        ' CheckBoxRenderHeadwear
        ' 
        CheckBoxRenderHeadwear.AutoSize = True
        CheckBoxRenderHeadwear.Location = New Point(257, 4)
        CheckBoxRenderHeadwear.Margin = New Padding(4, 2, 8, 2)
        CheckBoxRenderHeadwear.Name = "CheckBoxRenderHeadwear"
        CheckBoxRenderHeadwear.Size = New Size(117, 19)
        CheckBoxRenderHeadwear.TabIndex = 2
        CheckBoxRenderHeadwear.Text = "Render headwear"
        ' 
        ' CheckBoxRenderGore
        ' 
        CheckBoxRenderGore.AutoSize = True
        CheckBoxRenderGore.Location = New Point(386, 4)
        CheckBoxRenderGore.Margin = New Padding(4, 2, 8, 2)
        CheckBoxRenderGore.Name = "CheckBoxRenderGore"
        CheckBoxRenderGore.Size = New Size(90, 19)
        CheckBoxRenderGore.TabIndex = 3
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
        ' EditBody_Form
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(1560, 781)
        Controls.Add(PreviewSplit)
        MinimumSize = New Size(1340, 820)
        Name = "EditBody_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Edit Body"
        PreviewSplit.Panel1.ResumeLayout(False)
        PreviewSplit.Panel2.ResumeLayout(False)
        CType(PreviewSplit, ComponentModel.ISupportInitialize).EndInit()
        PreviewSplit.ResumeLayout(False)
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TabsBody.ResumeLayout(False)
        TabPageBody.ResumeLayout(False)
        BodyTabLayout.ResumeLayout(False)
        BodyTabLayout.PerformLayout()
        GroupBoxWeight.ResumeLayout(False)
        GroupBoxWeight.PerformLayout()
        WeightLayout.ResumeLayout(False)
        WeightLayout.PerformLayout()
        WeightLegend.ResumeLayout(False)
        WeightLegend.PerformLayout()
        GroupBoxMrsv.ResumeLayout(False)
        GroupBoxMrsv.PerformLayout()
        GroupBoxHeight.ResumeLayout(False)
        GroupBoxHeight.PerformLayout()
        HeightLayout.ResumeLayout(False)
        HeightLayout.PerformLayout()
        GroupBoxSkin.ResumeLayout(False)
        GroupBoxSkin.PerformLayout()
        SkinLayout.ResumeLayout(False)
        SkinLayout.PerformLayout()
        WnamPickPanel.ResumeLayout(False)
        GroupBoxSseWeight.ResumeLayout(False)
        GroupBoxSseWeight.PerformLayout()
        SseWeightLayout.ResumeLayout(False)
        SseWeightLayout.PerformLayout()
        TabPageBodySlide.ResumeLayout(False)
        BodySlideTabLayout.ResumeLayout(False)
        BodySlideTabLayout.PerformLayout()
        GroupBoxBodySlide.ResumeLayout(False)
        BodySlideLayout.ResumeLayout(False)
        BodySlideLayout.PerformLayout()
        BodySlidePresetLayout.ResumeLayout(False)
        TabPageSseBodyScale.ResumeLayout(False)
        SseBodyScaleRoot.ResumeLayout(False)
        SseBodyScaleRoot.PerformLayout()
        SseNodeLeftCol.ResumeLayout(False)
        SseNodeLeftCol.PerformLayout()
        PanelSseNodeDetail.ResumeLayout(False)
        PanelSseNodeDetail.PerformLayout()
        FlowSseNodeButtons.ResumeLayout(False)
        FlowSseNodeButtons.PerformLayout()
        SseNodeDetailLayout.ResumeLayout(False)
        SseNodeDetailLayout.PerformLayout()
        TabPageSseSkinOverrides.ResumeLayout(False)
        SseSkinRoot.ResumeLayout(False)
        SseSkinLeftPanel.ResumeLayout(False)
        SseSkinLeftPanel.PerformLayout()
        FlowSseSkinButtons.ResumeLayout(False)
        FlowSseSkinButtons.PerformLayout()
        GroupBoxSseSkinSlots.ResumeLayout(False)
        SseSkinDetail.ResumeLayout(False)
        SseSkinDetail.PerformLayout()
        TabPageOverlays.ResumeLayout(False)
        OverlaysTabLayout.ResumeLayout(False)
        OverlaysTabLayout.PerformLayout()
        OverlayListsLayout.ResumeLayout(False)
        OverlayListsLayout.PerformLayout()
        GroupBoxOverlayAvailable.ResumeLayout(False)
        OverlayAvailableLayout.ResumeLayout(False)
        OverlayAvailableLayout.PerformLayout()
        OverlayCenterLayout.ResumeLayout(False)
        OverlayCenterLayout.PerformLayout()
        FlowSseOverlayZone.ResumeLayout(False)
        FlowSseOverlayZone.PerformLayout()
        GroupBoxOverlayApplied.ResumeLayout(False)
        OverlayAppliedLayout.ResumeLayout(False)
        OverlayAppliedLayout.PerformLayout()
        OverlayAppliedButtons.ResumeLayout(False)
        OverlayAppliedButtons.PerformLayout()
        GroupBoxOverlayProps.ResumeLayout(False)
        GroupBoxOverlayProps.PerformLayout()
        OverlayPropsLayout.ResumeLayout(False)
        OverlayPropsLayout.PerformLayout()
        OverlayTintRowLayout.ResumeLayout(False)
        OverlayTintRowLayout.PerformLayout()
        SseOverlayDiffuseRow.ResumeLayout(False)
        SseOverlayDiffuseRow.PerformLayout()
        SseOverlayNormalRow.ResumeLayout(False)
        SseOverlayNormalRow.PerformLayout()
        TabPageSkinTint.ResumeLayout(False)
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
    Friend WithEvents CheckBoxRenderUnderarmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderArmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderHeadwear As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderGore As System.Windows.Forms.CheckBox
    Friend WithEvents PreviewHostPanel As System.Windows.Forms.Panel
    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TabsBody As System.Windows.Forms.TabControl
    Friend WithEvents TabPageBody As System.Windows.Forms.TabPage
    Friend WithEvents TabPageBodySlide As System.Windows.Forms.TabPage
    Friend WithEvents BodyTabLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents BodySlideTabLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelBodySlideEmpty As System.Windows.Forms.Label
    Friend WithEvents GroupBoxHeight As System.Windows.Forms.GroupBox
    Friend WithEvents HeightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeightMin As System.Windows.Forms.Label
    Friend WithEvents SliderHeightMin As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelHeightMax As System.Windows.Forms.Label
    Friend WithEvents SliderHeightMax As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents GroupBoxWeight As System.Windows.Forms.GroupBox
    Friend WithEvents WeightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents WeightTriangle As WeightTriangleControl
    Friend WithEvents WeightLegend As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMuscular As System.Windows.Forms.Label
    Friend WithEvents SliderMuscular As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelThin As System.Windows.Forms.Label
    Friend WithEvents SliderThin As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelFat As System.Windows.Forms.Label
    Friend WithEvents SliderFat As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents GroupBoxSkin As System.Windows.Forms.GroupBox
    Friend WithEvents SkinLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelWnam As System.Windows.Forms.Label
    Friend WithEvents ComboBoxWnam As System.Windows.Forms.ComboBox
    Friend WithEvents WnamPickPanel As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonPickWnam As System.Windows.Forms.Button
    Friend WithEvents LabelLmSkinTemplate As System.Windows.Forms.Label
    Friend WithEvents ComboBoxLmSkinTemplate As System.Windows.Forms.ComboBox
    Friend WithEvents GroupBoxSseWeight As System.Windows.Forms.GroupBox
    Friend WithEvents SseWeightLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseWeightNote As System.Windows.Forms.Label
    Friend WithEvents LabelSseWeight As System.Windows.Forms.Label
    Friend WithEvents SliderSseWeight As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents GroupBoxMrsv As System.Windows.Forms.GroupBox
    Friend WithEvents MrsvLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxBodySlide As System.Windows.Forms.GroupBox
    Friend WithEvents BodySlideLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents BodySlidePresetLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ComboBoxBsPreset As System.Windows.Forms.ComboBox
    Friend WithEvents ComboBoxBsSize As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonBsPresetClear As System.Windows.Forms.Button
    Friend WithEvents ButtonBsPresetBrowse As System.Windows.Forms.Button
    Friend WithEvents TextBoxBodySlideFilter As System.Windows.Forms.TextBox
    Friend WithEvents BodySlidePanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonResetSection As System.Windows.Forms.Button
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents TabPageOverlays As System.Windows.Forms.TabPage
    Friend WithEvents OverlaysTabLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents OverlayListsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxOverlayAvailable As System.Windows.Forms.GroupBox
    Friend WithEvents OverlayAvailableLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxOverlayFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListBoxOverlayAvailable As System.Windows.Forms.ListBox
    Friend WithEvents OverlayCenterLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonOverlayAdd As System.Windows.Forms.Button
    Friend WithEvents ButtonOverlayRemove As System.Windows.Forms.Button
    Friend WithEvents FlowSseOverlayZone As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelSseOverlayZone As System.Windows.Forms.Label
    Friend WithEvents ComboBoxSseOverlayZone As System.Windows.Forms.ComboBox
    Friend WithEvents GroupBoxOverlayApplied As System.Windows.Forms.GroupBox
    Friend WithEvents OverlayAppliedLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListBoxOverlayApplied As System.Windows.Forms.ListBox
    Friend WithEvents OverlayAppliedButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOverlayUp As System.Windows.Forms.Button
    Friend WithEvents ButtonOverlayDown As System.Windows.Forms.Button
    Friend WithEvents GroupBoxOverlayProps As System.Windows.Forms.GroupBox
    Friend WithEvents OverlayPropsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelOverlaySelected As System.Windows.Forms.Label
    Friend WithEvents LabelOverlayOffsetU As System.Windows.Forms.Label
    Friend WithEvents SliderOverlayOffsetU As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelOverlayOffsetV As System.Windows.Forms.Label
    Friend WithEvents SliderOverlayOffsetV As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelOverlayScaleU As System.Windows.Forms.Label
    Friend WithEvents SliderOverlayScaleU As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelOverlayScaleV As System.Windows.Forms.Label
    Friend WithEvents SliderOverlayScaleV As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents CheckBoxOverlayTint As System.Windows.Forms.CheckBox
    Friend WithEvents OverlayTintRowLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonOverlayTintColor As System.Windows.Forms.Button
    Friend WithEvents LabelOverlayTintAlpha As System.Windows.Forms.Label
    Friend WithEvents SliderOverlayTintAlpha As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseOverlayTexture As System.Windows.Forms.Label
    Friend WithEvents SseOverlayDiffuseRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseOverlayDiffuse As System.Windows.Forms.TextBox
    Friend WithEvents LabelSseOverlayNormal As System.Windows.Forms.Label
    Friend WithEvents SseOverlayNormalRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxSseOverlayNormal As System.Windows.Forms.TextBox
    Friend WithEvents CheckBoxSseOverlayMagic As System.Windows.Forms.CheckBox
    Friend WithEvents LabelSseOverlayMagicNote As System.Windows.Forms.Label
    Friend WithEvents TabPageSseBodyScale As System.Windows.Forms.TabPage
    Friend WithEvents SseBodyScaleRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents SseNodeLeftCol As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckBoxSseShowAllNodes As System.Windows.Forms.CheckBox
    Friend WithEvents TextBoxSseNodeFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListBoxSseNodes As System.Windows.Forms.ListBox
    Friend WithEvents PanelSseNodeDetail As System.Windows.Forms.Panel
    Friend WithEvents FlowSseNodeButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSseNodeReset As System.Windows.Forms.Button
    Friend WithEvents SseNodeDetailLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseNodeScale As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodeScale As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodePosX As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodePosX As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodePosY As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodePosY As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodePosZ As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodePosZ As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodeRotX As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodeRotX As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodeRotY As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodeRotY As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodeRotZ As System.Windows.Forms.Label
    Friend WithEvents SliderSseNodeRotZ As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSseNodeNote As System.Windows.Forms.Label
    Friend WithEvents ToolTipSseNode As System.Windows.Forms.ToolTip
    Friend WithEvents TabPageSseSkinOverrides As System.Windows.Forms.TabPage
    Friend WithEvents SseSkinRoot As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseSkinHeader As System.Windows.Forms.Label
    Friend WithEvents SseSkinLeftPanel As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListBoxSseSkinOverrides As System.Windows.Forms.ListBox
    Friend WithEvents FlowSseSkinButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonSseSkinAdd As System.Windows.Forms.Button
    Friend WithEvents ButtonSseSkinRemove As System.Windows.Forms.Button
    Friend WithEvents GroupBoxSseSkinSlots As System.Windows.Forms.GroupBox
    Friend WithEvents FlowSseSkinSlots As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents SseSkinDetail As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSseSkinTex0 As System.Windows.Forms.Label
    Friend WithEvents TextBoxSseSkinTex0 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonSseSkinTexPick0 As System.Windows.Forms.Button
    Friend WithEvents ButtonSseSkinTexClear0 As System.Windows.Forms.Button
    Friend WithEvents LabelSseSkinTex1 As System.Windows.Forms.Label
    Friend WithEvents TextBoxSseSkinTex1 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonSseSkinTexPick1 As System.Windows.Forms.Button
    Friend WithEvents ButtonSseSkinTexClear1 As System.Windows.Forms.Button
    Friend WithEvents LabelSseSkinTex2 As System.Windows.Forms.Label
    Friend WithEvents TextBoxSseSkinTex2 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonSseSkinTexPick2 As System.Windows.Forms.Button
    Friend WithEvents ButtonSseSkinTexClear2 As System.Windows.Forms.Button
    Friend WithEvents LabelSseSkinTex7 As System.Windows.Forms.Label
    Friend WithEvents TextBoxSseSkinTex7 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonSseSkinTexPick7 As System.Windows.Forms.Button
    Friend WithEvents ButtonSseSkinTexClear7 As System.Windows.Forms.Button
    Friend WithEvents CheckBoxSseSkinTint As System.Windows.Forms.CheckBox
    Friend WithEvents ButtonSseSkinTintColor As System.Windows.Forms.Button
    Friend WithEvents LabelSseSkinOpacity As System.Windows.Forms.Label
    Friend WithEvents SliderSseSkinAlpha As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents ToolTipSseSkin As System.Windows.Forms.ToolTip
    Friend WithEvents TabPageSkinTint As System.Windows.Forms.TabPage
    Friend WithEvents SkinTintPanelBody As SkinTintPanel
End Class
