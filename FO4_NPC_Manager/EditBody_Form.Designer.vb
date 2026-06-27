' UI built in Designer per feedback_ui_in_designer.md.
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
        GroupBoxSkin = New GroupBox()
        SkinLayout = New TableLayoutPanel()
        LabelWnam = New Label()
        ComboBoxWnam = New ComboBox()
        LabelLmSkinTemplate = New Label()
        ComboBoxLmSkinTemplate = New ComboBox()
        GroupBoxMrsv = New GroupBox()
        MrsvLayout = New TableLayoutPanel()
        TabPageBodySlide = New TabPage()
        BodySlideTabLayout = New TableLayoutPanel()
        GroupBoxBodySlide = New GroupBox()
        BodySlideLayout = New TableLayoutPanel()
        TextBoxBodySlideFilter = New TextBox()
        BodySlidePanel = New FlowLayoutPanel()
        LabelBodySlideEmpty = New Label()
        TabPageOverlays = New TabPage()
        OverlaysTabLayout = New TableLayoutPanel()
        OverlayListsLayout = New TableLayoutPanel()
        GroupBoxOverlayAvailable = New GroupBox()
        OverlayAvailableLayout = New TableLayoutPanel()
        TextBoxOverlayFilter = New TextBox()
        ListBoxOverlayAvailable = New ListBox()
        OverlayCenterLayout = New TableLayoutPanel()
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
        GroupBoxSkin.SuspendLayout()
        SkinLayout.SuspendLayout()
        GroupBoxMrsv.SuspendLayout()
        TabPageBodySlide.SuspendLayout()
        BodySlideTabLayout.SuspendLayout()
        GroupBoxBodySlide.SuspendLayout()
        BodySlideLayout.SuspendLayout()
        TabPageOverlays.SuspendLayout()
        OverlaysTabLayout.SuspendLayout()
        OverlayListsLayout.SuspendLayout()
        GroupBoxOverlayAvailable.SuspendLayout()
        OverlayAvailableLayout.SuspendLayout()
        OverlayCenterLayout.SuspendLayout()
        GroupBoxOverlayApplied.SuspendLayout()
        OverlayAppliedLayout.SuspendLayout()
        OverlayAppliedButtons.SuspendLayout()
        GroupBoxOverlayProps.SuspendLayout()
        OverlayPropsLayout.SuspendLayout()
        OverlayTintRowLayout.SuspendLayout()
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
        PreviewSplit.Panel1MinSize = 550
        ' 
        ' PreviewSplit.Panel2
        ' 
        PreviewSplit.Panel2.Controls.Add(PreviewSidebar)
        PreviewSplit.Size = New Size(1084, 621)
        PreviewSplit.SplitterDistance = 550
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
        RootLayout.Size = New Size(550, 621)
        RootLayout.TabIndex = 0
        ' 
        ' TabsBody
        ' 
        TabsBody.Controls.Add(TabPageBody)
        TabsBody.Controls.Add(TabPageBodySlide)
        TabsBody.Controls.Add(TabPageOverlays)
        TabsBody.Dock = DockStyle.Fill
        TabsBody.Location = New Point(11, 11)
        TabsBody.Name = "TabsBody"
        TabsBody.SelectedIndex = 0
        TabsBody.Size = New Size(528, 560)
        TabsBody.TabIndex = 0
        ' 
        ' TabPageBody
        ' 
        TabPageBody.Controls.Add(BodyTabLayout)
        TabPageBody.Location = New Point(4, 24)
        TabPageBody.Name = "TabPageBody"
        TabPageBody.Padding = New Padding(6)
        TabPageBody.Size = New Size(520, 532)
        TabPageBody.TabIndex = 0
        TabPageBody.Text = "Body"
        ' 
        ' BodyTabLayout
        ' 
        BodyTabLayout.ColumnCount = 1
        BodyTabLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodyTabLayout.Controls.Add(GroupBoxWeight, 0, 0)
        BodyTabLayout.Controls.Add(GroupBoxSkin, 0, 1)
        BodyTabLayout.Controls.Add(GroupBoxMrsv, 0, 2)
        BodyTabLayout.Dock = DockStyle.Fill
        BodyTabLayout.Location = New Point(6, 6)
        BodyTabLayout.Name = "BodyTabLayout"
        BodyTabLayout.RowCount = 4
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle())
        BodyTabLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BodyTabLayout.Size = New Size(508, 520)
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
        GroupBoxWeight.Size = New Size(502, 214)
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
        WeightLayout.Size = New Size(496, 192)
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
        WeightTriangle.Size = New Size(245, 180)
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
        WeightLegend.Location = New Point(261, 57)
        WeightLegend.Margin = New Padding(8, 2, 2, 2)
        WeightLegend.Name = "WeightLegend"
        WeightLegend.RowCount = 3
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.RowStyles.Add(New RowStyle())
        WeightLegend.Size = New Size(229, 78)
        WeightLegend.TabIndex = 1
        ' 
        ' LabelMuscular
        ' 
        LabelMuscular.Anchor = AnchorStyles.Left
        LabelMuscular.AutoSize = True
        LabelMuscular.Location = New Point(3, 5)
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
        SliderMuscular.DisplayFormat = "0.00"
        SliderMuscular.LargeChange = 0.1R
        SliderMuscular.Location = New Point(67, 2)
        SliderMuscular.Margin = New Padding(2)
        SliderMuscular.Maximum = 1R
        SliderMuscular.MinimumSize = New Size(140, 22)
        SliderMuscular.Name = "SliderMuscular"
        SliderMuscular.Size = New Size(160, 22)
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
        LabelThin.Location = New Point(3, 31)
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
        SliderThin.DisplayFormat = "0.00"
        SliderThin.LargeChange = 0.1R
        SliderThin.Location = New Point(67, 28)
        SliderThin.Margin = New Padding(2)
        SliderThin.Maximum = 1R
        SliderThin.MinimumSize = New Size(140, 22)
        SliderThin.Name = "SliderThin"
        SliderThin.Size = New Size(160, 22)
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
        LabelFat.Location = New Point(3, 57)
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
        SliderFat.DisplayFormat = "0.00"
        SliderFat.LargeChange = 0.1R
        SliderFat.Location = New Point(67, 54)
        SliderFat.Margin = New Padding(2)
        SliderFat.Maximum = 1R
        SliderFat.MinimumSize = New Size(140, 22)
        SliderFat.Name = "SliderFat"
        SliderFat.Size = New Size(160, 22)
        SliderFat.SmallChange = 0.01R
        SliderFat.TabIndex = 5
        SliderFat.TextBoxTextAlign = HorizontalAlignment.Right
        SliderFat.ThumbColor = SystemColors.HotTrack
        SliderFat.ThumbRadius = 4F
        SliderFat.TrackColor = SystemColors.ControlDark
        ' 
        ' GroupBoxSkin
        ' 
        GroupBoxSkin.AutoSize = True
        GroupBoxSkin.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSkin.Controls.Add(SkinLayout)
        GroupBoxSkin.Dock = DockStyle.Fill
        GroupBoxSkin.Location = New Point(3, 223)
        GroupBoxSkin.Name = "GroupBoxSkin"
        GroupBoxSkin.Size = New Size(502, 84)
        GroupBoxSkin.TabIndex = 1
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
        SkinLayout.Controls.Add(ComboBoxWnam, 1, 0)
        SkinLayout.Controls.Add(LabelLmSkinTemplate, 0, 1)
        SkinLayout.Controls.Add(ComboBoxLmSkinTemplate, 1, 1)
        SkinLayout.Dock = DockStyle.Fill
        SkinLayout.Location = New Point(3, 19)
        SkinLayout.Name = "SkinLayout"
        SkinLayout.Padding = New Padding(4)
        SkinLayout.RowCount = 2
        SkinLayout.RowStyles.Add(New RowStyle())
        SkinLayout.RowStyles.Add(New RowStyle())
        SkinLayout.Size = New Size(496, 62)
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
        ' ComboBoxWnam
        ' 
        ComboBoxWnam.Dock = DockStyle.Fill
        ComboBoxWnam.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxWnam.Location = New Point(125, 6)
        ComboBoxWnam.Margin = New Padding(2)
        ComboBoxWnam.Name = "ComboBoxWnam"
        ComboBoxWnam.Size = New Size(365, 23)
        ComboBoxWnam.TabIndex = 1
        ' 
        ' LabelLmSkinTemplate
        ' 
        LabelLmSkinTemplate.AutoSize = True
        LabelLmSkinTemplate.Location = New Point(6, 37)
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
        ComboBoxLmSkinTemplate.Location = New Point(125, 33)
        ComboBoxLmSkinTemplate.Margin = New Padding(2)
        ComboBoxLmSkinTemplate.Name = "ComboBoxLmSkinTemplate"
        ComboBoxLmSkinTemplate.Size = New Size(365, 23)
        ComboBoxLmSkinTemplate.TabIndex = 3
        ' 
        ' GroupBoxMrsv
        ' 
        GroupBoxMrsv.AutoSize = True
        GroupBoxMrsv.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxMrsv.Controls.Add(MrsvLayout)
        GroupBoxMrsv.Dock = DockStyle.Fill
        GroupBoxMrsv.Location = New Point(3, 313)
        GroupBoxMrsv.Name = "GroupBoxMrsv"
        GroupBoxMrsv.Size = New Size(502, 30)
        GroupBoxMrsv.TabIndex = 2
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
        MrsvLayout.Size = New Size(496, 8)
        MrsvLayout.TabIndex = 0
        ' 
        ' TabPageBodySlide
        ' 
        TabPageBodySlide.Controls.Add(BodySlideTabLayout)
        TabPageBodySlide.Location = New Point(4, 24)
        TabPageBodySlide.Name = "TabPageBodySlide"
        TabPageBodySlide.Padding = New Padding(6)
        TabPageBodySlide.Size = New Size(520, 532)
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
        BodySlideTabLayout.Size = New Size(508, 520)
        BodySlideTabLayout.TabIndex = 0
        ' 
        ' GroupBoxBodySlide
        ' 
        GroupBoxBodySlide.Controls.Add(BodySlideLayout)
        GroupBoxBodySlide.Dock = DockStyle.Fill
        GroupBoxBodySlide.Location = New Point(3, 3)
        GroupBoxBodySlide.Name = "GroupBoxBodySlide"
        GroupBoxBodySlide.Size = New Size(502, 491)
        GroupBoxBodySlide.TabIndex = 0
        GroupBoxBodySlide.TabStop = False
        GroupBoxBodySlide.Text = "BodySlide Sliders (PIRT .tri — vertex morphs, F4SE-only field)"
        ' 
        ' BodySlideLayout
        ' 
        BodySlideLayout.ColumnCount = 1
        BodySlideLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BodySlideLayout.Controls.Add(TextBoxBodySlideFilter, 0, 0)
        BodySlideLayout.Controls.Add(BodySlidePanel, 0, 1)
        BodySlideLayout.Dock = DockStyle.Fill
        BodySlideLayout.Location = New Point(3, 19)
        BodySlideLayout.Name = "BodySlideLayout"
        BodySlideLayout.Padding = New Padding(4)
        BodySlideLayout.RowCount = 2
        BodySlideLayout.RowStyles.Add(New RowStyle())
        BodySlideLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BodySlideLayout.Size = New Size(496, 469)
        BodySlideLayout.TabIndex = 0
        ' 
        ' TextBoxBodySlideFilter
        ' 
        TextBoxBodySlideFilter.Dock = DockStyle.Top
        TextBoxBodySlideFilter.Location = New Point(7, 7)
        TextBoxBodySlideFilter.Name = "TextBoxBodySlideFilter"
        TextBoxBodySlideFilter.PlaceholderText = "Filter sliders…"
        TextBoxBodySlideFilter.Size = New Size(482, 23)
        TextBoxBodySlideFilter.TabIndex = 0
        ' 
        ' BodySlidePanel
        ' 
        BodySlidePanel.AutoScroll = True
        BodySlidePanel.Dock = DockStyle.Fill
        BodySlidePanel.FlowDirection = FlowDirection.TopDown
        BodySlidePanel.Location = New Point(7, 36)
        BodySlidePanel.Name = "BodySlidePanel"
        BodySlidePanel.Size = New Size(482, 426)
        BodySlidePanel.TabIndex = 1
        BodySlidePanel.WrapContents = False
        ' 
        ' LabelBodySlideEmpty
        ' 
        LabelBodySlideEmpty.AutoSize = True
        LabelBodySlideEmpty.ForeColor = Color.Gray
        LabelBodySlideEmpty.Location = New Point(8, 501)
        LabelBodySlideEmpty.Margin = New Padding(8, 4, 8, 4)
        LabelBodySlideEmpty.Name = "LabelBodySlideEmpty"
        LabelBodySlideEmpty.Size = New Size(452, 15)
        LabelBodySlideEmpty.TabIndex = 1
        LabelBodySlideEmpty.Text = "This NPC has no BodySlide morph data (no BODYTRI extra-data on any body shape)."
        LabelBodySlideEmpty.Visible = False
        ' 
        ' TabPageOverlays
        ' 
        TabPageOverlays.Controls.Add(OverlaysTabLayout)
        TabPageOverlays.Location = New Point(4, 24)
        TabPageOverlays.Name = "TabPageOverlays"
        TabPageOverlays.Padding = New Padding(6)
        TabPageOverlays.Size = New Size(520, 532)
        TabPageOverlays.TabIndex = 2
        TabPageOverlays.Text = "Overlays"
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
        OverlaysTabLayout.Size = New Size(508, 520)
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
        OverlayListsLayout.Size = New Size(502, 289)
        OverlayListsLayout.TabIndex = 0
        ' 
        ' GroupBoxOverlayAvailable
        ' 
        GroupBoxOverlayAvailable.Controls.Add(OverlayAvailableLayout)
        GroupBoxOverlayAvailable.Dock = DockStyle.Fill
        GroupBoxOverlayAvailable.Location = New Point(3, 3)
        GroupBoxOverlayAvailable.Name = "GroupBoxOverlayAvailable"
        GroupBoxOverlayAvailable.Size = New Size(202, 283)
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
        OverlayAvailableLayout.Size = New Size(196, 261)
        OverlayAvailableLayout.TabIndex = 0
        ' 
        ' TextBoxOverlayFilter
        ' 
        TextBoxOverlayFilter.Dock = DockStyle.Top
        TextBoxOverlayFilter.Location = New Point(7, 7)
        TextBoxOverlayFilter.Name = "TextBoxOverlayFilter"
        TextBoxOverlayFilter.PlaceholderText = "Filter overlays…"
        TextBoxOverlayFilter.Size = New Size(182, 23)
        TextBoxOverlayFilter.TabIndex = 0
        ' 
        ' ListBoxOverlayAvailable
        ' 
        ListBoxOverlayAvailable.Dock = DockStyle.Fill
        ListBoxOverlayAvailable.IntegralHeight = False
        ListBoxOverlayAvailable.ItemHeight = 15
        ListBoxOverlayAvailable.Location = New Point(7, 36)
        ListBoxOverlayAvailable.Name = "ListBoxOverlayAvailable"
        ListBoxOverlayAvailable.Size = New Size(182, 218)
        ListBoxOverlayAvailable.TabIndex = 1
        ' 
        ' OverlayCenterLayout
        ' 
        OverlayCenterLayout.Anchor = AnchorStyles.None
        OverlayCenterLayout.AutoSize = True
        OverlayCenterLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayCenterLayout.ColumnCount = 1
        OverlayCenterLayout.ColumnStyles.Add(New ColumnStyle())
        OverlayCenterLayout.Controls.Add(ButtonOverlayAdd, 0, 0)
        OverlayCenterLayout.Controls.Add(ButtonOverlayRemove, 0, 1)
        OverlayCenterLayout.Location = New Point(211, 113)
        OverlayCenterLayout.Name = "OverlayCenterLayout"
        OverlayCenterLayout.RowCount = 2
        OverlayCenterLayout.RowStyles.Add(New RowStyle())
        OverlayCenterLayout.RowStyles.Add(New RowStyle())
        OverlayCenterLayout.Size = New Size(79, 62)
        OverlayCenterLayout.TabIndex = 1
        ' 
        ' ButtonOverlayAdd
        ' 
        ButtonOverlayAdd.AutoSize = True
        ButtonOverlayAdd.Location = New Point(3, 3)
        ButtonOverlayAdd.Name = "ButtonOverlayAdd"
        ButtonOverlayAdd.Size = New Size(73, 25)
        ButtonOverlayAdd.TabIndex = 0
        ButtonOverlayAdd.Text = "Add →"
        ' 
        ' ButtonOverlayRemove
        ' 
        ButtonOverlayRemove.AutoSize = True
        ButtonOverlayRemove.Location = New Point(3, 34)
        ButtonOverlayRemove.Name = "ButtonOverlayRemove"
        ButtonOverlayRemove.Size = New Size(73, 25)
        ButtonOverlayRemove.TabIndex = 1
        ButtonOverlayRemove.Text = "← Remove"
        ' 
        ' GroupBoxOverlayApplied
        ' 
        GroupBoxOverlayApplied.Controls.Add(OverlayAppliedLayout)
        GroupBoxOverlayApplied.Dock = DockStyle.Fill
        GroupBoxOverlayApplied.Location = New Point(296, 3)
        GroupBoxOverlayApplied.Name = "GroupBoxOverlayApplied"
        GroupBoxOverlayApplied.Size = New Size(203, 283)
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
        OverlayAppliedLayout.Size = New Size(197, 261)
        OverlayAppliedLayout.TabIndex = 0
        ' 
        ' ListBoxOverlayApplied
        ' 
        ListBoxOverlayApplied.Dock = DockStyle.Fill
        ListBoxOverlayApplied.IntegralHeight = False
        ListBoxOverlayApplied.ItemHeight = 15
        ListBoxOverlayApplied.Location = New Point(7, 7)
        ListBoxOverlayApplied.Name = "ListBoxOverlayApplied"
        ListBoxOverlayApplied.Size = New Size(183, 210)
        ListBoxOverlayApplied.TabIndex = 0
        ' 
        ' OverlayAppliedButtons
        ' 
        OverlayAppliedButtons.AutoSize = True
        OverlayAppliedButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        OverlayAppliedButtons.Controls.Add(ButtonOverlayUp)
        OverlayAppliedButtons.Controls.Add(ButtonOverlayDown)
        OverlayAppliedButtons.Dock = DockStyle.Fill
        OverlayAppliedButtons.Location = New Point(7, 223)
        OverlayAppliedButtons.Name = "OverlayAppliedButtons"
        OverlayAppliedButtons.Size = New Size(183, 31)
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
        GroupBoxOverlayProps.Location = New Point(3, 298)
        GroupBoxOverlayProps.Name = "GroupBoxOverlayProps"
        GroupBoxOverlayProps.Size = New Size(502, 219)
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
        OverlayPropsLayout.Dock = DockStyle.Fill
        OverlayPropsLayout.Location = New Point(3, 19)
        OverlayPropsLayout.Name = "OverlayPropsLayout"
        OverlayPropsLayout.Padding = New Padding(4)
        OverlayPropsLayout.RowCount = 6
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.RowStyles.Add(New RowStyle())
        OverlayPropsLayout.Size = New Size(496, 197)
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
        SliderOverlayOffsetU.Size = New Size(394, 28)
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
        SliderOverlayOffsetV.Size = New Size(394, 28)
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
        SliderOverlayScaleU.Size = New Size(394, 28)
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
        SliderOverlayScaleV.Size = New Size(394, 28)
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
        OverlayTintRowLayout.Size = New Size(394, 32)
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
        LabelOverlayTintAlpha.Size = New Size(41, 15)
        LabelOverlayTintAlpha.TabIndex = 1
        LabelOverlayTintAlpha.Text = "Alpha:"
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
        SliderOverlayTintAlpha.Location = New Point(95, 2)
        SliderOverlayTintAlpha.Margin = New Padding(2)
        SliderOverlayTintAlpha.Maximum = 1R
        SliderOverlayTintAlpha.MinimumSize = New Size(120, 22)
        SliderOverlayTintAlpha.Name = "SliderOverlayTintAlpha"
        SliderOverlayTintAlpha.Size = New Size(297, 28)
        SliderOverlayTintAlpha.SmallChange = 0.01R
        SliderOverlayTintAlpha.TabIndex = 2
        SliderOverlayTintAlpha.TextBoxTextAlign = HorizontalAlignment.Right
        SliderOverlayTintAlpha.ThumbColor = SystemColors.HotTrack
        SliderOverlayTintAlpha.ThumbRadius = 4F
        SliderOverlayTintAlpha.TrackColor = SystemColors.ControlDark
        SliderOverlayTintAlpha.Value = 1R
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
        BottomLayout.Location = New Point(11, 577)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 4, 0, 0)
        BottomLayout.Size = New Size(528, 33)
        BottomLayout.TabIndex = 1
        ' 
        ' ButtonOk
        ' 
        ButtonOk.Location = New Point(445, 7)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Location = New Point(359, 7)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' ButtonResetSection
        ' 
        ButtonResetSection.Location = New Point(243, 7)
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
        PreviewSidebar.Size = New Size(530, 621)
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
        RenderTogglesPanel.Size = New Size(524, 27)
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
        PreviewHostPanel.Size = New Size(524, 582)
        PreviewHostPanel.TabIndex = 0
        ' 
        ' EditBody_Form
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(1084, 621)
        Controls.Add(PreviewSplit)
        MinimumSize = New Size(1000, 660)
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
        GroupBoxSkin.ResumeLayout(False)
        GroupBoxSkin.PerformLayout()
        SkinLayout.ResumeLayout(False)
        SkinLayout.PerformLayout()
        GroupBoxMrsv.ResumeLayout(False)
        GroupBoxMrsv.PerformLayout()
        TabPageBodySlide.ResumeLayout(False)
        BodySlideTabLayout.ResumeLayout(False)
        BodySlideTabLayout.PerformLayout()
        GroupBoxBodySlide.ResumeLayout(False)
        BodySlideLayout.ResumeLayout(False)
        BodySlideLayout.PerformLayout()
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
    Friend WithEvents LabelLmSkinTemplate As System.Windows.Forms.Label
    Friend WithEvents ComboBoxLmSkinTemplate As System.Windows.Forms.ComboBox
    Friend WithEvents GroupBoxMrsv As System.Windows.Forms.GroupBox
    Friend WithEvents MrsvLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxBodySlide As System.Windows.Forms.GroupBox
    Friend WithEvents BodySlideLayout As System.Windows.Forms.TableLayoutPanel
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
End Class
