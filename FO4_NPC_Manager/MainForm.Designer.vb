<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MainForm
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
        components = New ComponentModel.Container()
        TreeViewNpcsContextMenu = New ContextMenuStrip(components)
        MenuItemMarkChanged = New ToolStripMenuItem()
        MenuItemResetOverlay = New ToolStripMenuItem()
        MenuItemSaveSelected = New ToolStripMenuItem()
        MenuItemBuildChargen = New ToolStripMenuItem()
        SplitContainer1 = New SplitContainer()
        SplitContainerLeft = New SplitContainer()
        PanelNpcList = New Panel()
        SplitContainer2 = New SplitContainer()
        TextBoxSearch = New TextBox()
        LabelSearch = New Label()
        CheckBoxOnlyChanged = New CheckBox()
        LabelShowCategories = New Label()
        CheckBoxCatUnique = New CheckBox()
        CheckBoxCatGeneric = New CheckBox()
        CheckBoxCatTemplate = New CheckBox()
        CheckBoxCatUnused = New CheckBox()
        TreeViewNPCs = New BufferedTreeView()
        PanelRecordDetails = New Panel()
        TreeViewRecordDetails = New TreeView()
        LabelRecordTitle = New Label()
        PanelPreviewLayout = New TableLayoutPanel()
        PanelPreviewToolbar = New TableLayoutPanel()
        CheckBoxRenderGore = New CheckBox()
        CheckBoxBodyTri = New CheckBox()
        LabelPreviewMode = New Label()
        ComboBoxPreviewMode = New ComboBox()
        ComboBoxGender = New ComboBox()
        LabelOutfit = New Label()
        ComboBoxOutfit = New ComboBox()
        LabelMorphs = New Label()
        CheckBoxApplyBoneMorphs = New CheckBox()
        CheckBoxApplyVertexMorphs = New CheckBox()
        CheckBoxApplyBodyWeight = New CheckBox()
        LabelRenders = New Label()
        CheckBoxRenderBody = New CheckBox()
        CheckBoxRenderUnderarmor = New CheckBox()
        CheckBoxRenderArmor = New CheckBox()
        CheckBoxRenderHeadwear = New CheckBox()
        ButtonRandomNPC = New Button()
        ButtonReroll = New Button()
        ButtonLightRig = New Button()
        CheckBoxApplySculpt = New CheckBox()
        PanelActionsToolbar = New FlowLayoutPanel()
        LabelEdit = New Label()
        ButtonEditFace = New Button()
        ButtonEditBody = New Button()
        ButtonEditOutfit = New Button()
        ButtonArmaEditor = New Button()
        ButtonArmoEditor = New Button()
        SeparatorActions1 = New Label()
        LabelLooksMenu = New Label()
        ButtonLoadLooksmenu = New Button()
        ButtonSaveLooksmenu = New Button()
        SeparatorActions2 = New Label()
        LabelLook = New Label()
        ButtonCopyLook = New Button()
        ButtonPasteLook = New Button()
        SeparatorActions3 = New Label()
        Label1 = New Label()
        ButtonSavePlugin = New Button()
        ButtonBuildCharGen = New Button()
        Label2 = New Label()
        Label3 = New Label()
        ButtonSaveSceneNif = New Button()
        ButtonCharGenOptions = New Button()
        PanelPreviewHost = New Panel()
        LabelStatus = New Label()
        PanelAnimBar = New FlowLayoutPanel()
        ComboAnim = New ComboBox()
        ButtonSelectAnim = New Button()
        ButtonAnimPlay = New Button()
        SliderAnimFrame = New TinySliderTextBox()
        LabelAnimMs = New Label()
        NumericAnimFrameMs = New NumericUpDown()
        StatusStrip1 = New StatusStrip()
        ToolStripStatusLabel1 = New ToolStripStatusLabel()
        ToolStripProgressBar1 = New ToolStripProgressBar()
        TreeViewNpcsContextMenu.SuspendLayout()
        CType(SplitContainer1, ComponentModel.ISupportInitialize).BeginInit()
        SplitContainer1.Panel1.SuspendLayout()
        SplitContainer1.Panel2.SuspendLayout()
        SplitContainer1.SuspendLayout()
        CType(SplitContainerLeft, ComponentModel.ISupportInitialize).BeginInit()
        SplitContainerLeft.Panel1.SuspendLayout()
        SplitContainerLeft.Panel2.SuspendLayout()
        SplitContainerLeft.SuspendLayout()
        PanelNpcList.SuspendLayout()
        CType(SplitContainer2, ComponentModel.ISupportInitialize).BeginInit()
        SplitContainer2.Panel1.SuspendLayout()
        SplitContainer2.Panel2.SuspendLayout()
        SplitContainer2.SuspendLayout()
        PanelRecordDetails.SuspendLayout()
        PanelPreviewLayout.SuspendLayout()
        PanelPreviewToolbar.SuspendLayout()
        PanelActionsToolbar.SuspendLayout()
        PanelPreviewHost.SuspendLayout()
        PanelAnimBar.SuspendLayout()
        CType(NumericAnimFrameMs, ComponentModel.ISupportInitialize).BeginInit()
        StatusStrip1.SuspendLayout()
        SuspendLayout()
        ' 
        ' TreeViewNpcsContextMenu
        ' 
        TreeViewNpcsContextMenu.Items.AddRange(New ToolStripItem() {MenuItemMarkChanged, MenuItemResetOverlay, MenuItemSaveSelected, MenuItemBuildChargen})
        TreeViewNpcsContextMenu.Name = "TreeViewNpcsContextMenu"
        TreeViewNpcsContextMenu.Size = New Size(213, 92)
        ' 
        ' MenuItemMarkChanged
        ' 
        MenuItemMarkChanged.Name = "MenuItemMarkChanged"
        MenuItemMarkChanged.Size = New Size(212, 22)
        MenuItemMarkChanged.Text = "Mark as changed"
        ' 
        ' MenuItemResetOverlay
        ' 
        MenuItemResetOverlay.Name = "MenuItemResetOverlay"
        MenuItemResetOverlay.Size = New Size(212, 22)
        MenuItemResetOverlay.Text = "Reset (discard changes)"
        ' 
        ' MenuItemSaveSelected
        ' 
        MenuItemSaveSelected.Name = "MenuItemSaveSelected"
        MenuItemSaveSelected.Size = New Size(212, 22)
        MenuItemSaveSelected.Text = "Save Selected (ESP/ESM)..."
        ' 
        ' MenuItemBuildChargen
        ' 
        MenuItemBuildChargen.Name = "MenuItemBuildChargen"
        MenuItemBuildChargen.Size = New Size(212, 22)
        MenuItemBuildChargen.Text = "Build CharGen (loose)"
        ' 
        ' SplitContainer1
        ' 
        SplitContainer1.Dock = DockStyle.Fill
        SplitContainer1.Location = New Point(0, 0)
        SplitContainer1.Name = "SplitContainer1"
        ' 
        ' SplitContainer1.Panel1
        ' 
        SplitContainer1.Panel1.Controls.Add(SplitContainerLeft)
        SplitContainer1.Panel1MinSize = 220
        ' 
        ' SplitContainer1.Panel2
        ' 
        SplitContainer1.Panel2.Controls.Add(PanelPreviewLayout)
        SplitContainer1.Panel2MinSize = 400
        SplitContainer1.Size = New Size(1904, 1019)
        SplitContainer1.SplitterDistance = 700
        SplitContainer1.TabIndex = 0
        ' 
        ' SplitContainerLeft
        ' 
        SplitContainerLeft.Dock = DockStyle.Fill
        SplitContainerLeft.Location = New Point(0, 0)
        SplitContainerLeft.Name = "SplitContainerLeft"
        SplitContainerLeft.Orientation = Orientation.Horizontal
        ' 
        ' SplitContainerLeft.Panel1
        ' 
        SplitContainerLeft.Panel1.Controls.Add(PanelNpcList)
        SplitContainerLeft.Panel1MinSize = 150
        ' 
        ' SplitContainerLeft.Panel2
        ' 
        SplitContainerLeft.Panel2.Controls.Add(PanelRecordDetails)
        SplitContainerLeft.Panel2MinSize = 150
        SplitContainerLeft.Size = New Size(700, 1019)
        SplitContainerLeft.SplitterDistance = 550
        SplitContainerLeft.TabIndex = 0
        ' 
        ' PanelNpcList
        ' 
        PanelNpcList.Controls.Add(SplitContainer2)
        PanelNpcList.Dock = DockStyle.Fill
        PanelNpcList.Location = New Point(0, 0)
        PanelNpcList.Name = "PanelNpcList"
        PanelNpcList.Size = New Size(700, 550)
        PanelNpcList.TabIndex = 0
        ' 
        ' SplitContainer2
        ' 
        SplitContainer2.Dock = DockStyle.Fill
        SplitContainer2.FixedPanel = FixedPanel.Panel1
        SplitContainer2.Location = New Point(0, 0)
        SplitContainer2.Name = "SplitContainer2"
        SplitContainer2.Orientation = Orientation.Horizontal
        ' 
        ' SplitContainer2.Panel1
        ' 
        SplitContainer2.Panel1.Controls.Add(TextBoxSearch)
        SplitContainer2.Panel1.Controls.Add(LabelSearch)
        SplitContainer2.Panel1.Controls.Add(CheckBoxOnlyChanged)
        SplitContainer2.Panel1.Controls.Add(LabelShowCategories)
        SplitContainer2.Panel1.Controls.Add(CheckBoxCatUnique)
        SplitContainer2.Panel1.Controls.Add(CheckBoxCatGeneric)
        SplitContainer2.Panel1.Controls.Add(CheckBoxCatTemplate)
        SplitContainer2.Panel1.Controls.Add(CheckBoxCatUnused)
        ' 
        ' SplitContainer2.Panel2
        ' 
        SplitContainer2.Panel2.Controls.Add(TreeViewNPCs)
        SplitContainer2.Size = New Size(700, 550)
        SplitContainer2.SplitterDistance = 78
        SplitContainer2.TabIndex = 3
        ' 
        ' TextBoxSearch
        ' 
        TextBoxSearch.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxSearch.Location = New Point(59, 17)
        TextBoxSearch.Name = "TextBoxSearch"
        TextBoxSearch.PlaceholderText = "Filter NPCs..."
        TextBoxSearch.Size = New Size(638, 23)
        TextBoxSearch.TabIndex = 1
        ' 
        ' LabelSearch
        ' 
        LabelSearch.AutoSize = True
        LabelSearch.Location = New Point(7, 20)
        LabelSearch.Name = "LabelSearch"
        LabelSearch.Size = New Size(45, 15)
        LabelSearch.TabIndex = 2
        LabelSearch.Text = "Search:"
        ' 
        ' CheckBoxOnlyChanged
        ' 
        CheckBoxOnlyChanged.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        CheckBoxOnlyChanged.AutoSize = True
        CheckBoxOnlyChanged.Location = New Point(416, 49)
        CheckBoxOnlyChanged.Name = "CheckBoxOnlyChanged"
        CheckBoxOnlyChanged.Size = New Size(100, 19)
        CheckBoxOnlyChanged.TabIndex = 3
        CheckBoxOnlyChanged.Text = "Only changed"
        CheckBoxOnlyChanged.UseVisualStyleBackColor = True
        ' 
        ' LabelShowCategories
        ' 
        LabelShowCategories.AutoSize = True
        LabelShowCategories.Location = New Point(7, 51)
        LabelShowCategories.Name = "LabelShowCategories"
        LabelShowCategories.Size = New Size(39, 15)
        LabelShowCategories.TabIndex = 4
        LabelShowCategories.Text = "Show:"
        ' 
        ' CheckBoxCatUnique
        ' 
        CheckBoxCatUnique.AutoSize = True
        CheckBoxCatUnique.Checked = True
        CheckBoxCatUnique.CheckState = CheckState.Checked
        CheckBoxCatUnique.Location = New Point(55, 49)
        CheckBoxCatUnique.Name = "CheckBoxCatUnique"
        CheckBoxCatUnique.Size = New Size(94, 19)
        CheckBoxCatUnique.TabIndex = 5
        CheckBoxCatUnique.Text = "Unique faces"
        CheckBoxCatUnique.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxCatGeneric
        ' 
        CheckBoxCatGeneric.AutoSize = True
        CheckBoxCatGeneric.Location = New Point(156, 49)
        CheckBoxCatGeneric.Name = "CheckBoxCatGeneric"
        CheckBoxCatGeneric.Size = New Size(66, 19)
        CheckBoxCatGeneric.TabIndex = 6
        CheckBoxCatGeneric.Text = "Generic"
        CheckBoxCatGeneric.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxCatTemplate
        ' 
        CheckBoxCatTemplate.AutoSize = True
        CheckBoxCatTemplate.Location = New Point(232, 49)
        CheckBoxCatTemplate.Name = "CheckBoxCatTemplate"
        CheckBoxCatTemplate.Size = New Size(107, 19)
        CheckBoxCatTemplate.TabIndex = 7
        CheckBoxCatTemplate.Text = "Template bases"
        CheckBoxCatTemplate.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxCatUnused
        ' 
        CheckBoxCatUnused.AutoSize = True
        CheckBoxCatUnused.Location = New Point(344, 49)
        CheckBoxCatUnused.Name = "CheckBoxCatUnused"
        CheckBoxCatUnused.Size = New Size(66, 19)
        CheckBoxCatUnused.TabIndex = 8
        CheckBoxCatUnused.Text = "Unused"
        CheckBoxCatUnused.UseVisualStyleBackColor = True
        ' 
        ' TreeViewNPCs
        ' 
        TreeViewNPCs.BorderStyle = BorderStyle.FixedSingle
        TreeViewNPCs.Dock = DockStyle.Fill
        TreeViewNPCs.DrawMode = TreeViewDrawMode.OwnerDrawText
        TreeViewNPCs.HideSelection = False
        TreeViewNPCs.Location = New Point(0, 0)
        TreeViewNPCs.Name = "TreeViewNPCs"
        TreeViewNPCs.Size = New Size(700, 468)
        TreeViewNPCs.TabIndex = 0
        ' 
        ' PanelRecordDetails
        ' 
        PanelRecordDetails.Controls.Add(TreeViewRecordDetails)
        PanelRecordDetails.Controls.Add(LabelRecordTitle)
        PanelRecordDetails.Dock = DockStyle.Fill
        PanelRecordDetails.Location = New Point(0, 0)
        PanelRecordDetails.Name = "PanelRecordDetails"
        PanelRecordDetails.Size = New Size(700, 465)
        PanelRecordDetails.TabIndex = 0
        ' 
        ' TreeViewRecordDetails
        ' 
        TreeViewRecordDetails.BorderStyle = BorderStyle.None
        TreeViewRecordDetails.Dock = DockStyle.Fill
        TreeViewRecordDetails.Font = New Font("Segoe UI", 9F)
        TreeViewRecordDetails.Location = New Point(0, 24)
        TreeViewRecordDetails.Name = "TreeViewRecordDetails"
        TreeViewRecordDetails.Size = New Size(700, 441)
        TreeViewRecordDetails.TabIndex = 0
        ' 
        ' LabelRecordTitle
        ' 
        LabelRecordTitle.BackColor = SystemColors.ControlDark
        LabelRecordTitle.Dock = DockStyle.Top
        LabelRecordTitle.Font = New Font("Segoe UI Semibold", 9.75F, FontStyle.Bold)
        LabelRecordTitle.ForeColor = SystemColors.ControlLightLight
        LabelRecordTitle.Location = New Point(0, 0)
        LabelRecordTitle.Name = "LabelRecordTitle"
        LabelRecordTitle.Size = New Size(700, 24)
        LabelRecordTitle.TabIndex = 1
        LabelRecordTitle.Text = "  Record Details"
        LabelRecordTitle.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' PanelPreviewLayout
        ' 
        PanelPreviewLayout.ColumnCount = 1
        PanelPreviewLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PanelPreviewLayout.Controls.Add(PanelPreviewToolbar, 0, 0)
        PanelPreviewLayout.Controls.Add(PanelActionsToolbar, 0, 1)
        PanelPreviewLayout.Controls.Add(PanelPreviewHost, 0, 2)
        PanelPreviewLayout.Controls.Add(PanelAnimBar, 0, 3)
        PanelPreviewLayout.Dock = DockStyle.Fill
        PanelPreviewLayout.Location = New Point(0, 0)
        PanelPreviewLayout.Name = "PanelPreviewLayout"
        PanelPreviewLayout.Padding = New Padding(8, 6, 8, 6)
        PanelPreviewLayout.RowCount = 4
        PanelPreviewLayout.RowStyles.Add(New RowStyle())
        PanelPreviewLayout.RowStyles.Add(New RowStyle())
        PanelPreviewLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PanelPreviewLayout.RowStyles.Add(New RowStyle())
        PanelPreviewLayout.Size = New Size(1200, 1019)
        PanelPreviewLayout.TabIndex = 0
        ' 
        ' PanelPreviewToolbar
        ' 
        PanelPreviewToolbar.AutoSize = True
        PanelPreviewToolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelPreviewToolbar.ColumnCount = 7
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 20F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 30F))
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderGore, 5, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxBodyTri, 4, 2)
        PanelPreviewToolbar.Controls.Add(LabelPreviewMode, 0, 0)
        PanelPreviewToolbar.Controls.Add(ComboBoxPreviewMode, 1, 0)
        PanelPreviewToolbar.Controls.Add(ComboBoxGender, 2, 0)
        PanelPreviewToolbar.Controls.Add(LabelOutfit, 0, 1)
        PanelPreviewToolbar.Controls.Add(ComboBoxOutfit, 1, 1)
        PanelPreviewToolbar.Controls.Add(LabelMorphs, 0, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyBoneMorphs, 1, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyVertexMorphs, 2, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyBodyWeight, 3, 2)
        PanelPreviewToolbar.Controls.Add(LabelRenders, 0, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderBody, 1, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderUnderarmor, 2, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderArmor, 3, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderHeadwear, 4, 3)
        PanelPreviewToolbar.Controls.Add(ButtonRandomNPC, 6, 0)
        PanelPreviewToolbar.Controls.Add(ButtonReroll, 6, 1)
        PanelPreviewToolbar.Controls.Add(ButtonLightRig, 6, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplySculpt, 5, 2)
        PanelPreviewToolbar.Dock = DockStyle.Top
        PanelPreviewToolbar.Location = New Point(11, 9)
        PanelPreviewToolbar.Name = "PanelPreviewToolbar"
        PanelPreviewToolbar.RowCount = 4
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.Size = New Size(1178, 111)
        PanelPreviewToolbar.TabIndex = 0
        ' 
        ' CheckBoxRenderGore
        ' 
        CheckBoxRenderGore.AutoSize = True
        CheckBoxRenderGore.Checked = True
        CheckBoxRenderGore.CheckState = CheckState.Checked
        CheckBoxRenderGore.Location = New Point(932, 91)
        CheckBoxRenderGore.Margin = New Padding(2, 1, 8, 1)
        CheckBoxRenderGore.Name = "CheckBoxRenderGore"
        CheckBoxRenderGore.Size = New Size(90, 19)
        CheckBoxRenderGore.TabIndex = 19
        CheckBoxRenderGore.Text = "Render gore"
        ' 
        ' CheckBoxBodyTri
        ' 
        CheckBoxBodyTri.AutoSize = True
        CheckBoxBodyTri.Checked = True
        CheckBoxBodyTri.CheckState = CheckState.Checked
        CheckBoxBodyTri.Location = New Point(717, 61)
        CheckBoxBodyTri.Margin = New Padding(2, 1, 8, 1)
        CheckBoxBodyTri.Name = "CheckBoxBodyTri"
        CheckBoxBodyTri.Size = New Size(114, 19)
        CheckBoxBodyTri.TabIndex = 17
        CheckBoxBodyTri.Text = "Body Sliders (Tri)"
        ' 
        ' LabelPreviewMode
        ' 
        LabelPreviewMode.Anchor = AnchorStyles.Left
        LabelPreviewMode.AutoSize = True
        LabelPreviewMode.Location = New Point(2, 10)
        LabelPreviewMode.Margin = New Padding(2, 6, 4, 0)
        LabelPreviewMode.Name = "LabelPreviewMode"
        LabelPreviewMode.Size = New Size(51, 15)
        LabelPreviewMode.TabIndex = 0
        LabelPreviewMode.Text = "Preview:"
        ' 
        ' ComboBoxPreviewMode
        ' 
        ComboBoxPreviewMode.Dock = DockStyle.Fill
        ComboBoxPreviewMode.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxPreviewMode.Items.AddRange(New Object() {"Full Character", "Only Face"})
        ComboBoxPreviewMode.Location = New Point(72, 2)
        ComboBoxPreviewMode.Margin = New Padding(2, 2, 4, 2)
        ComboBoxPreviewMode.Name = "ComboBoxPreviewMode"
        ComboBoxPreviewMode.Size = New Size(209, 23)
        ComboBoxPreviewMode.TabIndex = 1
        ' 
        ' ComboBoxGender
        ' 
        PanelPreviewToolbar.SetColumnSpan(ComboBoxGender, 4)
        ComboBoxGender.Dock = DockStyle.Fill
        ComboBoxGender.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxGender.Items.AddRange(New Object() {"Random", "Male", "Female"})
        ComboBoxGender.Location = New Point(287, 2)
        ComboBoxGender.Margin = New Padding(2, 2, 4, 2)
        ComboBoxGender.Name = "ComboBoxGender"
        ComboBoxGender.Size = New Size(854, 23)
        ComboBoxGender.TabIndex = 2
        ' 
        ' LabelOutfit
        ' 
        LabelOutfit.Anchor = AnchorStyles.Left
        LabelOutfit.AutoSize = True
        LabelOutfit.Location = New Point(2, 40)
        LabelOutfit.Margin = New Padding(2, 6, 4, 0)
        LabelOutfit.Name = "LabelOutfit"
        LabelOutfit.Size = New Size(41, 15)
        LabelOutfit.TabIndex = 4
        LabelOutfit.Text = "Outfit:"
        ' 
        ' ComboBoxOutfit
        ' 
        PanelPreviewToolbar.SetColumnSpan(ComboBoxOutfit, 5)
        ComboBoxOutfit.Dock = DockStyle.Fill
        ComboBoxOutfit.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxOutfit.Location = New Point(72, 32)
        ComboBoxOutfit.Margin = New Padding(2, 2, 4, 2)
        ComboBoxOutfit.Name = "ComboBoxOutfit"
        ComboBoxOutfit.Size = New Size(1069, 23)
        ComboBoxOutfit.TabIndex = 5
        ' 
        ' LabelMorphs
        ' 
        LabelMorphs.Anchor = AnchorStyles.Left
        LabelMorphs.AutoSize = True
        LabelMorphs.Location = New Point(2, 69)
        LabelMorphs.Margin = New Padding(2, 4, 4, 0)
        LabelMorphs.Name = "LabelMorphs"
        LabelMorphs.Size = New Size(51, 15)
        LabelMorphs.TabIndex = 15
        LabelMorphs.Text = "Morphs:"
        ' 
        ' CheckBoxApplyBoneMorphs
        ' 
        CheckBoxApplyBoneMorphs.AutoSize = True
        CheckBoxApplyBoneMorphs.Checked = True
        CheckBoxApplyBoneMorphs.CheckState = CheckState.Checked
        CheckBoxApplyBoneMorphs.Location = New Point(72, 61)
        CheckBoxApplyBoneMorphs.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplyBoneMorphs.Name = "CheckBoxApplyBoneMorphs"
        CheckBoxApplyBoneMorphs.Size = New Size(138, 19)
        CheckBoxApplyBoneMorphs.TabIndex = 7
        CheckBoxApplyBoneMorphs.Text = "Bone morphs (FMRS)"
        ' 
        ' CheckBoxApplyVertexMorphs
        ' 
        CheckBoxApplyVertexMorphs.AutoSize = True
        CheckBoxApplyVertexMorphs.Checked = True
        CheckBoxApplyVertexMorphs.CheckState = CheckState.Checked
        CheckBoxApplyVertexMorphs.Location = New Point(287, 61)
        CheckBoxApplyVertexMorphs.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplyVertexMorphs.Name = "CheckBoxApplyVertexMorphs"
        CheckBoxApplyVertexMorphs.Size = New Size(129, 19)
        CheckBoxApplyVertexMorphs.TabIndex = 8
        CheckBoxApplyVertexMorphs.Text = "Vertex morphs (TRI)"
        ' 
        ' CheckBoxApplyBodyWeight
        ' 
        CheckBoxApplyBodyWeight.AutoSize = True
        CheckBoxApplyBodyWeight.Checked = True
        CheckBoxApplyBodyWeight.CheckState = CheckState.Checked
        CheckBoxApplyBodyWeight.Location = New Point(502, 61)
        CheckBoxApplyBodyWeight.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplyBodyWeight.Name = "CheckBoxApplyBodyWeight"
        CheckBoxApplyBodyWeight.Size = New Size(140, 19)
        CheckBoxApplyBodyWeight.TabIndex = 9
        CheckBoxApplyBodyWeight.Text = "Body weight (MWGT)"
        ' 
        ' LabelRenders
        ' 
        LabelRenders.Anchor = AnchorStyles.Left
        LabelRenders.AutoSize = True
        LabelRenders.Location = New Point(2, 95)
        LabelRenders.Margin = New Padding(2, 4, 4, 0)
        LabelRenders.Name = "LabelRenders"
        LabelRenders.Size = New Size(52, 15)
        LabelRenders.TabIndex = 16
        LabelRenders.Text = "Renders:"
        ' 
        ' CheckBoxRenderBody
        ' 
        CheckBoxRenderBody.AutoSize = True
        CheckBoxRenderBody.Checked = True
        CheckBoxRenderBody.CheckState = CheckState.Checked
        CheckBoxRenderBody.Location = New Point(72, 91)
        CheckBoxRenderBody.Margin = New Padding(2, 1, 8, 1)
        CheckBoxRenderBody.Name = "CheckBoxRenderBody"
        CheckBoxRenderBody.Size = New Size(93, 19)
        CheckBoxRenderBody.TabIndex = 11
        CheckBoxRenderBody.Text = "Render body"
        ' 
        ' CheckBoxRenderUnderarmor
        ' 
        CheckBoxRenderUnderarmor.AutoSize = True
        CheckBoxRenderUnderarmor.Checked = True
        CheckBoxRenderUnderarmor.CheckState = CheckState.Checked
        CheckBoxRenderUnderarmor.Location = New Point(287, 91)
        CheckBoxRenderUnderarmor.Margin = New Padding(2, 1, 8, 1)
        CheckBoxRenderUnderarmor.Name = "CheckBoxRenderUnderarmor"
        CheckBoxRenderUnderarmor.Size = New Size(129, 19)
        CheckBoxRenderUnderarmor.TabIndex = 12
        CheckBoxRenderUnderarmor.Text = "Render underarmor"
        ' 
        ' CheckBoxRenderArmor
        ' 
        CheckBoxRenderArmor.AutoSize = True
        CheckBoxRenderArmor.Checked = True
        CheckBoxRenderArmor.CheckState = CheckState.Checked
        CheckBoxRenderArmor.Location = New Point(502, 91)
        CheckBoxRenderArmor.Margin = New Padding(2, 1, 8, 1)
        CheckBoxRenderArmor.Name = "CheckBoxRenderArmor"
        CheckBoxRenderArmor.Size = New Size(98, 19)
        CheckBoxRenderArmor.TabIndex = 13
        CheckBoxRenderArmor.Text = "Render armor"
        ' 
        ' CheckBoxRenderHeadwear
        ' 
        CheckBoxRenderHeadwear.AutoSize = True
        CheckBoxRenderHeadwear.Checked = True
        CheckBoxRenderHeadwear.CheckState = CheckState.Checked
        CheckBoxRenderHeadwear.Location = New Point(717, 91)
        CheckBoxRenderHeadwear.Margin = New Padding(2, 1, 8, 1)
        CheckBoxRenderHeadwear.Name = "CheckBoxRenderHeadwear"
        CheckBoxRenderHeadwear.Size = New Size(117, 19)
        CheckBoxRenderHeadwear.TabIndex = 14
        CheckBoxRenderHeadwear.Text = "Render headwear"
        ' 
        ' ButtonRandomNPC
        ' 
        ButtonRandomNPC.Anchor = AnchorStyles.Right
        ButtonRandomNPC.FlatStyle = FlatStyle.System
        ButtonRandomNPC.Font = New Font("Segoe UI Symbol", 10F, FontStyle.Bold)
        ButtonRandomNPC.Location = New Point(1150, 2)
        ButtonRandomNPC.Margin = New Padding(2)
        ButtonRandomNPC.Name = "ButtonRandomNPC"
        ButtonRandomNPC.Size = New Size(26, 26)
        ButtonRandomNPC.TabIndex = 3
        ButtonRandomNPC.Text = "🎲"
        ' 
        ' ButtonReroll
        ' 
        ButtonReroll.Anchor = AnchorStyles.Right
        ButtonReroll.FlatStyle = FlatStyle.System
        ButtonReroll.Font = New Font("Segoe UI Symbol", 10F, FontStyle.Bold)
        ButtonReroll.Location = New Point(1150, 32)
        ButtonReroll.Margin = New Padding(2)
        ButtonReroll.Name = "ButtonReroll"
        ButtonReroll.Size = New Size(26, 26)
        ButtonReroll.TabIndex = 6
        ButtonReroll.Text = "↻"
        ' 
        ' ButtonLightRig
        ' 
        ButtonLightRig.Anchor = AnchorStyles.Right
        ButtonLightRig.FlatStyle = FlatStyle.System
        ButtonLightRig.Font = New Font("Segoe UI Symbol", 10F, FontStyle.Bold)
        ButtonLightRig.Location = New Point(1150, 62)
        ButtonLightRig.Margin = New Padding(2)
        ButtonLightRig.Name = "ButtonLightRig"
        ButtonLightRig.Size = New Size(26, 26)
        ButtonLightRig.TabIndex = 18
        ButtonLightRig.Text = "💡"
        ' 
        ' CheckBoxApplySculpt
        ' 
        CheckBoxApplySculpt.AutoSize = True
        CheckBoxApplySculpt.Checked = True
        CheckBoxApplySculpt.CheckState = CheckState.Checked
        CheckBoxApplySculpt.Location = New Point(932, 61)
        CheckBoxApplySculpt.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplySculpt.Name = "CheckBoxApplySculpt"
        CheckBoxApplySculpt.Size = New Size(134, 19)
        CheckBoxApplySculpt.TabIndex = 10
        CheckBoxApplySculpt.Text = "Sculpt (ARMA SCLP)"
        ' 
        ' PanelActionsToolbar
        ' 
        PanelActionsToolbar.AutoSize = True
        PanelActionsToolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelActionsToolbar.Controls.Add(LabelEdit)
        PanelActionsToolbar.Controls.Add(ButtonEditFace)
        PanelActionsToolbar.Controls.Add(ButtonEditBody)
        PanelActionsToolbar.Controls.Add(ButtonEditOutfit)
        PanelActionsToolbar.Controls.Add(ButtonArmaEditor)
        PanelActionsToolbar.Controls.Add(ButtonArmoEditor)
        PanelActionsToolbar.Controls.Add(SeparatorActions1)
        PanelActionsToolbar.Controls.Add(LabelLooksMenu)
        PanelActionsToolbar.Controls.Add(ButtonLoadLooksmenu)
        PanelActionsToolbar.Controls.Add(ButtonSaveLooksmenu)
        PanelActionsToolbar.Controls.Add(SeparatorActions2)
        PanelActionsToolbar.Controls.Add(LabelLook)
        PanelActionsToolbar.Controls.Add(ButtonCopyLook)
        PanelActionsToolbar.Controls.Add(ButtonPasteLook)
        PanelActionsToolbar.Controls.Add(SeparatorActions3)
        PanelActionsToolbar.Controls.Add(Label1)
        PanelActionsToolbar.Controls.Add(ButtonSavePlugin)
        PanelActionsToolbar.Controls.Add(ButtonBuildCharGen)
        PanelActionsToolbar.Controls.Add(Label2)
        PanelActionsToolbar.Controls.Add(Label3)
        PanelActionsToolbar.Controls.Add(ButtonSaveSceneNif)
        PanelActionsToolbar.Controls.Add(ButtonCharGenOptions)
        PanelActionsToolbar.Dock = DockStyle.Top
        PanelActionsToolbar.Location = New Point(11, 126)
        PanelActionsToolbar.Name = "PanelActionsToolbar"
        PanelActionsToolbar.Size = New Size(1178, 64)
        PanelActionsToolbar.TabIndex = 1
        ' 
        ' LabelEdit
        ' 
        LabelEdit.Anchor = AnchorStyles.None
        LabelEdit.AutoSize = True
        LabelEdit.Location = New Point(3, 8)
        LabelEdit.Margin = New Padding(3)
        LabelEdit.Name = "LabelEdit"
        LabelEdit.Size = New Size(30, 15)
        LabelEdit.TabIndex = 0
        LabelEdit.Text = "Edit:"
        LabelEdit.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ButtonEditFace
        ' 
        ButtonEditFace.AutoSize = True
        ButtonEditFace.Enabled = False
        ButtonEditFace.Location = New Point(38, 2)
        ButtonEditFace.Margin = New Padding(2)
        ButtonEditFace.MinimumSize = New Size(80, 28)
        ButtonEditFace.Name = "ButtonEditFace"
        ButtonEditFace.Size = New Size(80, 28)
        ButtonEditFace.TabIndex = 1
        ButtonEditFace.Text = "Face"
        ButtonEditFace.UseVisualStyleBackColor = True
        ' 
        ' ButtonEditBody
        ' 
        ButtonEditBody.AutoSize = True
        ButtonEditBody.Enabled = False
        ButtonEditBody.Location = New Point(122, 2)
        ButtonEditBody.Margin = New Padding(2)
        ButtonEditBody.MinimumSize = New Size(80, 28)
        ButtonEditBody.Name = "ButtonEditBody"
        ButtonEditBody.Size = New Size(80, 28)
        ButtonEditBody.TabIndex = 2
        ButtonEditBody.Text = "Body"
        ButtonEditBody.UseVisualStyleBackColor = True
        ' 
        ' ButtonEditOutfit
        ' 
        ButtonEditOutfit.AutoSize = True
        ButtonEditOutfit.Enabled = False
        ButtonEditOutfit.Location = New Point(206, 2)
        ButtonEditOutfit.Margin = New Padding(2)
        ButtonEditOutfit.MinimumSize = New Size(80, 28)
        ButtonEditOutfit.Name = "ButtonEditOutfit"
        ButtonEditOutfit.Size = New Size(80, 28)
        ButtonEditOutfit.TabIndex = 3
        ButtonEditOutfit.Text = "Outfit"
        ButtonEditOutfit.UseVisualStyleBackColor = True
        '
        ' ButtonArmaEditor
        '
        ButtonArmaEditor.AutoSize = True
        ButtonArmaEditor.Location = New Point(290, 2)
        ButtonArmaEditor.Margin = New Padding(2)
        ButtonArmaEditor.MinimumSize = New Size(90, 28)
        ButtonArmaEditor.Name = "ButtonArmaEditor"
        ButtonArmaEditor.Size = New Size(90, 28)
        ButtonArmaEditor.TabIndex = 4
        ButtonArmaEditor.Text = "ARMA Editor"
        ButtonArmaEditor.UseVisualStyleBackColor = True
        '
        ' ButtonArmoEditor
        '
        ButtonArmoEditor.AutoSize = True
        ButtonArmoEditor.Location = New Point(382, 2)
        ButtonArmoEditor.Margin = New Padding(2)
        ButtonArmoEditor.MinimumSize = New Size(90, 28)
        ButtonArmoEditor.Name = "ButtonArmoEditor"
        ButtonArmoEditor.Size = New Size(90, 28)
        ButtonArmoEditor.TabIndex = 5
        ButtonArmoEditor.Text = "ARMO Editor"
        ButtonArmoEditor.UseVisualStyleBackColor = True
        '
        ' SeparatorActions1
        ' 
        SeparatorActions1.BorderStyle = BorderStyle.Fixed3D
        SeparatorActions1.Location = New Point(296, 4)
        SeparatorActions1.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions1.Name = "SeparatorActions1"
        SeparatorActions1.Size = New Size(2, 24)
        SeparatorActions1.TabIndex = 4
        ' 
        ' LabelLooksMenu
        ' 
        LabelLooksMenu.Anchor = AnchorStyles.None
        LabelLooksMenu.AutoSize = True
        LabelLooksMenu.Location = New Point(309, 8)
        LabelLooksMenu.Margin = New Padding(3)
        LabelLooksMenu.Name = "LabelLooksMenu"
        LabelLooksMenu.Size = New Size(72, 15)
        LabelLooksMenu.TabIndex = 5
        LabelLooksMenu.Text = "LooksMenu:"
        LabelLooksMenu.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ButtonLoadLooksmenu
        ' 
        ButtonLoadLooksmenu.AutoSize = True
        ButtonLoadLooksmenu.Enabled = False
        ButtonLoadLooksmenu.Location = New Point(386, 2)
        ButtonLoadLooksmenu.Margin = New Padding(2)
        ButtonLoadLooksmenu.MinimumSize = New Size(80, 28)
        ButtonLoadLooksmenu.Name = "ButtonLoadLooksmenu"
        ButtonLoadLooksmenu.Size = New Size(80, 28)
        ButtonLoadLooksmenu.TabIndex = 6
        ButtonLoadLooksmenu.Text = "Load"
        ButtonLoadLooksmenu.UseVisualStyleBackColor = True
        ' 
        ' ButtonSaveLooksmenu
        ' 
        ButtonSaveLooksmenu.AutoSize = True
        ButtonSaveLooksmenu.Enabled = False
        ButtonSaveLooksmenu.Location = New Point(470, 2)
        ButtonSaveLooksmenu.Margin = New Padding(2)
        ButtonSaveLooksmenu.MinimumSize = New Size(80, 28)
        ButtonSaveLooksmenu.Name = "ButtonSaveLooksmenu"
        ButtonSaveLooksmenu.Size = New Size(80, 28)
        ButtonSaveLooksmenu.TabIndex = 7
        ButtonSaveLooksmenu.Text = "Save"
        ButtonSaveLooksmenu.UseVisualStyleBackColor = True
        ' 
        ' SeparatorActions2
        ' 
        SeparatorActions2.BorderStyle = BorderStyle.Fixed3D
        SeparatorActions2.Location = New Point(560, 4)
        SeparatorActions2.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions2.Name = "SeparatorActions2"
        SeparatorActions2.Size = New Size(2, 24)
        SeparatorActions2.TabIndex = 8
        ' 
        ' LabelLook
        ' 
        LabelLook.Anchor = AnchorStyles.None
        LabelLook.AutoSize = True
        LabelLook.Location = New Point(573, 8)
        LabelLook.Margin = New Padding(3)
        LabelLook.Name = "LabelLook"
        LabelLook.Size = New Size(36, 15)
        LabelLook.TabIndex = 9
        LabelLook.Text = "Look:"
        LabelLook.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ButtonCopyLook
        ' 
        ButtonCopyLook.AutoSize = True
        ButtonCopyLook.Enabled = False
        ButtonCopyLook.Location = New Point(614, 2)
        ButtonCopyLook.Margin = New Padding(2)
        ButtonCopyLook.MinimumSize = New Size(80, 28)
        ButtonCopyLook.Name = "ButtonCopyLook"
        ButtonCopyLook.Size = New Size(80, 28)
        ButtonCopyLook.TabIndex = 10
        ButtonCopyLook.Text = "Copy"
        ButtonCopyLook.UseVisualStyleBackColor = True
        ' 
        ' ButtonPasteLook
        ' 
        ButtonPasteLook.Anchor = AnchorStyles.Left
        ButtonPasteLook.AutoSize = True
        ButtonPasteLook.Enabled = False
        ButtonPasteLook.Location = New Point(698, 2)
        ButtonPasteLook.Margin = New Padding(2)
        ButtonPasteLook.MinimumSize = New Size(80, 28)
        ButtonPasteLook.Name = "ButtonPasteLook"
        ButtonPasteLook.Size = New Size(80, 28)
        ButtonPasteLook.TabIndex = 11
        ButtonPasteLook.Text = "Paste"
        ButtonPasteLook.UseVisualStyleBackColor = True
        ' 
        ' SeparatorActions3
        ' 
        SeparatorActions3.BorderStyle = BorderStyle.Fixed3D
        SeparatorActions3.Location = New Point(788, 4)
        SeparatorActions3.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions3.Name = "SeparatorActions3"
        SeparatorActions3.Size = New Size(2, 24)
        SeparatorActions3.TabIndex = 12
        ' 
        ' Label1
        ' 
        Label1.Anchor = AnchorStyles.None
        Label1.AutoSize = True
        Label1.Location = New Point(801, 8)
        Label1.Margin = New Padding(3)
        Label1.Name = "Label1"
        Label1.Size = New Size(57, 15)
        Label1.TabIndex = 15
        Label1.Text = "Generate:"
        Label1.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ButtonSavePlugin
        ' 
        ButtonSavePlugin.AutoSize = True
        ButtonSavePlugin.Enabled = False
        ButtonSavePlugin.Location = New Point(863, 2)
        ButtonSavePlugin.Margin = New Padding(2)
        ButtonSavePlugin.MinimumSize = New Size(110, 28)
        ButtonSavePlugin.Name = "ButtonSavePlugin"
        ButtonSavePlugin.Size = New Size(110, 28)
        ButtonSavePlugin.TabIndex = 13
        ButtonSavePlugin.Text = "Save ESP/ESM"
        ButtonSavePlugin.UseVisualStyleBackColor = True
        ' 
        ' ButtonBuildCharGen
        ' 
        ButtonBuildCharGen.AutoSize = True
        ButtonBuildCharGen.Enabled = False
        ButtonBuildCharGen.Location = New Point(977, 2)
        ButtonBuildCharGen.Margin = New Padding(2)
        ButtonBuildCharGen.MinimumSize = New Size(110, 28)
        ButtonBuildCharGen.Name = "ButtonBuildCharGen"
        ButtonBuildCharGen.Size = New Size(132, 28)
        ButtonBuildCharGen.TabIndex = 2
        ButtonBuildCharGen.Text = "Build CharGen (loose)"
        ButtonBuildCharGen.UseVisualStyleBackColor = True
        ' 
        ' Label2
        ' 
        Label2.BorderStyle = BorderStyle.Fixed3D
        Label2.Location = New Point(1119, 4)
        Label2.Margin = New Padding(8, 4, 8, 4)
        Label2.Name = "Label2"
        Label2.Size = New Size(2, 24)
        Label2.TabIndex = 16
        ' 
        ' Label3
        ' 
        Label3.Anchor = AnchorStyles.None
        Label3.AutoSize = True
        Label3.Location = New Point(1132, 8)
        Label3.Margin = New Padding(3)
        Label3.Name = "Label3"
        Label3.Size = New Size(40, 15)
        Label3.TabIndex = 17
        Label3.Text = "Extras:"
        Label3.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ButtonSaveSceneNif
        ' 
        ButtonSaveSceneNif.AutoSize = True
        ButtonSaveSceneNif.Enabled = False
        ButtonSaveSceneNif.Location = New Point(2, 34)
        ButtonSaveSceneNif.Margin = New Padding(2)
        ButtonSaveSceneNif.MinimumSize = New Size(120, 28)
        ButtonSaveSceneNif.Name = "ButtonSaveSceneNif"
        ButtonSaveSceneNif.Size = New Size(132, 28)
        ButtonSaveSceneNif.TabIndex = 14
        ButtonSaveSceneNif.Text = "NPC Model to NIF"
        ButtonSaveSceneNif.UseVisualStyleBackColor = True
        ' 
        ' ButtonCharGenOptions
        ' 
        ButtonCharGenOptions.AutoSize = True
        ButtonCharGenOptions.Location = New Point(138, 34)
        ButtonCharGenOptions.Margin = New Padding(2)
        ButtonCharGenOptions.MinimumSize = New Size(110, 28)
        ButtonCharGenOptions.Name = "ButtonCharGenOptions"
        ButtonCharGenOptions.Size = New Size(120, 28)
        ButtonCharGenOptions.TabIndex = 3
        ButtonCharGenOptions.Text = "CharGen Options"
        ButtonCharGenOptions.UseVisualStyleBackColor = True
        ' 
        ' PanelPreviewHost
        ' 
        PanelPreviewHost.BackColor = Color.FromArgb(CByte(40), CByte(40), CByte(44))
        PanelPreviewHost.Controls.Add(LabelStatus)
        PanelPreviewHost.Dock = DockStyle.Fill
        PanelPreviewHost.Location = New Point(11, 196)
        PanelPreviewHost.Name = "PanelPreviewHost"
        PanelPreviewHost.Size = New Size(1178, 777)
        PanelPreviewHost.TabIndex = 0
        ' 
        ' LabelStatus
        ' 
        LabelStatus.Dock = DockStyle.Fill
        LabelStatus.Font = New Font("Segoe UI", 11F)
        LabelStatus.ForeColor = SystemColors.GrayText
        LabelStatus.Location = New Point(0, 0)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(1178, 777)
        LabelStatus.TabIndex = 1
        LabelStatus.Text = "Loading..."
        LabelStatus.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' PanelAnimBar
        ' 
        PanelAnimBar.AutoSize = True
        PanelAnimBar.Controls.Add(ComboAnim)
        PanelAnimBar.Controls.Add(ButtonSelectAnim)
        PanelAnimBar.Controls.Add(ButtonAnimPlay)
        PanelAnimBar.Controls.Add(SliderAnimFrame)
        PanelAnimBar.Controls.Add(LabelAnimMs)
        PanelAnimBar.Controls.Add(NumericAnimFrameMs)
        PanelAnimBar.Dock = DockStyle.Fill
        PanelAnimBar.Location = New Point(8, 980)
        PanelAnimBar.Margin = New Padding(0, 4, 0, 0)
        PanelAnimBar.Name = "PanelAnimBar"
        PanelAnimBar.Size = New Size(1184, 33)
        PanelAnimBar.TabIndex = 2
        PanelAnimBar.WrapContents = False
        ' 
        ' ComboAnim
        ' 
        ComboAnim.DropDownStyle = ComboBoxStyle.DropDownList
        ComboAnim.Location = New Point(3, 4)
        ComboAnim.Margin = New Padding(3, 4, 3, 3)
        ComboAnim.Name = "ComboAnim"
        ComboAnim.Size = New Size(260, 23)
        ComboAnim.TabIndex = 0
        ' 
        ' ButtonSelectAnim
        ' 
        ButtonSelectAnim.AutoSize = True
        ButtonSelectAnim.Location = New Point(269, 3)
        ButtonSelectAnim.Margin = New Padding(3, 3, 12, 3)
        ButtonSelectAnim.Name = "ButtonSelectAnim"
        ButtonSelectAnim.Size = New Size(116, 25)
        ButtonSelectAnim.TabIndex = 1
        ButtonSelectAnim.Text = "Select Animation…"
        ButtonSelectAnim.UseVisualStyleBackColor = True
        ' 
        ' ButtonAnimPlay
        ' 
        ButtonAnimPlay.Enabled = False
        ButtonAnimPlay.Location = New Point(400, 3)
        ButtonAnimPlay.Margin = New Padding(3, 3, 8, 3)
        ButtonAnimPlay.Name = "ButtonAnimPlay"
        ButtonAnimPlay.Size = New Size(40, 25)
        ButtonAnimPlay.TabIndex = 2
        ButtonAnimPlay.Text = "▶"
        ButtonAnimPlay.UseVisualStyleBackColor = True
        ' 
        ' SliderAnimFrame
        ' 
        SliderAnimFrame.AccentColor = SystemColors.HotTrack
        SliderAnimFrame.BackColor = SystemColors.Control
        SliderAnimFrame.DisplayFormat = "0"
        SliderAnimFrame.Enabled = False
        SliderAnimFrame.Location = New Point(451, 4)
        SliderAnimFrame.Margin = New Padding(3, 4, 3, 3)
        SliderAnimFrame.Maximum = 0R
        SliderAnimFrame.MinimumSize = New Size(100, 24)
        SliderAnimFrame.Name = "SliderAnimFrame"
        SliderAnimFrame.ShowTicks = True
        SliderAnimFrame.Size = New Size(300, 26)
        SliderAnimFrame.TabIndex = 3
        SliderAnimFrame.TextBoxTextAlign = HorizontalAlignment.Right
        SliderAnimFrame.ThumbColor = SystemColors.HotTrack
        SliderAnimFrame.ThumbRadius = 4F
        SliderAnimFrame.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelAnimMs
        ' 
        LabelAnimMs.Anchor = AnchorStyles.None
        LabelAnimMs.AutoSize = True
        LabelAnimMs.Location = New Point(764, 9)
        LabelAnimMs.Margin = New Padding(10, 0, 1, 0)
        LabelAnimMs.Name = "LabelAnimMs"
        LabelAnimMs.Size = New Size(29, 15)
        LabelAnimMs.TabIndex = 4
        LabelAnimMs.Text = "FPS:"
        ' 
        ' NumericAnimFrameMs
        ' 
        NumericAnimFrameMs.Anchor = AnchorStyles.None
        NumericAnimFrameMs.Enabled = False
        NumericAnimFrameMs.Location = New Point(795, 5)
        NumericAnimFrameMs.Margin = New Padding(1, 0, 3, 0)
        NumericAnimFrameMs.Maximum = New Decimal(New Integer() {120, 0, 0, 0})
        NumericAnimFrameMs.Minimum = New Decimal(New Integer() {1, 0, 0, 0})
        NumericAnimFrameMs.Name = "NumericAnimFrameMs"
        NumericAnimFrameMs.Size = New Size(62, 23)
        NumericAnimFrameMs.TabIndex = 5
        NumericAnimFrameMs.TextAlign = HorizontalAlignment.Right
        NumericAnimFrameMs.Value = New Decimal(New Integer() {30, 0, 0, 0})
        ' 
        ' StatusStrip1
        ' 
        StatusStrip1.Items.AddRange(New ToolStripItem() {ToolStripStatusLabel1, ToolStripProgressBar1})
        StatusStrip1.Location = New Point(0, 1019)
        StatusStrip1.Name = "StatusStrip1"
        StatusStrip1.Size = New Size(1904, 22)
        StatusStrip1.TabIndex = 1
        ' 
        ' ToolStripStatusLabel1
        ' 
        ToolStripStatusLabel1.Name = "ToolStripStatusLabel1"
        ToolStripStatusLabel1.Size = New Size(1889, 17)
        ToolStripStatusLabel1.Spring = True
        ToolStripStatusLabel1.Text = "Ready"
        ToolStripStatusLabel1.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' ToolStripProgressBar1
        ' 
        ToolStripProgressBar1.Name = "ToolStripProgressBar1"
        ToolStripProgressBar1.Size = New Size(100, 16)
        ToolStripProgressBar1.Visible = False
        ' 
        ' MainForm
        ' 
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoScroll = True
        ClientSize = New Size(1904, 1041)
        Controls.Add(SplitContainer1)
        Controls.Add(StatusStrip1)
        MinimumSize = New Size(1024, 720)
        Name = "MainForm"
        StartPosition = FormStartPosition.CenterScreen
        Text = "FO4 NPC Manager"
        WindowState = FormWindowState.Maximized
        TreeViewNpcsContextMenu.ResumeLayout(False)
        SplitContainer1.Panel1.ResumeLayout(False)
        SplitContainer1.Panel2.ResumeLayout(False)
        CType(SplitContainer1, ComponentModel.ISupportInitialize).EndInit()
        SplitContainer1.ResumeLayout(False)
        SplitContainerLeft.Panel1.ResumeLayout(False)
        SplitContainerLeft.Panel2.ResumeLayout(False)
        CType(SplitContainerLeft, ComponentModel.ISupportInitialize).EndInit()
        SplitContainerLeft.ResumeLayout(False)
        PanelNpcList.ResumeLayout(False)
        SplitContainer2.Panel1.ResumeLayout(False)
        SplitContainer2.Panel1.PerformLayout()
        SplitContainer2.Panel2.ResumeLayout(False)
        CType(SplitContainer2, ComponentModel.ISupportInitialize).EndInit()
        SplitContainer2.ResumeLayout(False)
        PanelRecordDetails.ResumeLayout(False)
        PanelPreviewLayout.ResumeLayout(False)
        PanelPreviewLayout.PerformLayout()
        PanelPreviewToolbar.ResumeLayout(False)
        PanelPreviewToolbar.PerformLayout()
        PanelActionsToolbar.ResumeLayout(False)
        PanelActionsToolbar.PerformLayout()
        PanelPreviewHost.ResumeLayout(False)
        PanelAnimBar.ResumeLayout(False)
        PanelAnimBar.PerformLayout()
        CType(NumericAnimFrameMs, ComponentModel.ISupportInitialize).EndInit()
        StatusStrip1.ResumeLayout(False)
        StatusStrip1.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
    Friend WithEvents SplitContainerLeft As System.Windows.Forms.SplitContainer
    Friend WithEvents PanelNpcList As System.Windows.Forms.Panel
    Friend WithEvents PanelRecordDetails As System.Windows.Forms.Panel
    Friend WithEvents PanelPreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents PanelPreviewHost As System.Windows.Forms.Panel
    Friend WithEvents TreeViewNPCs As BufferedTreeView
    Friend WithEvents TreeViewNpcsContextMenu As System.Windows.Forms.ContextMenuStrip
    Friend WithEvents MenuItemMarkChanged As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents MenuItemResetOverlay As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents MenuItemSaveSelected As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents MenuItemBuildChargen As System.Windows.Forms.ToolStripMenuItem
    Friend WithEvents TextBoxSearch As System.Windows.Forms.TextBox
    Friend WithEvents LabelSearch As System.Windows.Forms.Label
    Friend WithEvents CheckBoxOnlyChanged As System.Windows.Forms.CheckBox
    Friend WithEvents LabelShowCategories As System.Windows.Forms.Label
    Friend WithEvents CheckBoxCatUnique As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxCatGeneric As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxCatTemplate As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxCatUnused As System.Windows.Forms.CheckBox
    Friend WithEvents TreeViewRecordDetails As System.Windows.Forms.TreeView
    Friend WithEvents LabelRecordTitle As System.Windows.Forms.Label
    Friend WithEvents PanelPreviewToolbar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelPreviewMode As System.Windows.Forms.Label
    Friend WithEvents ComboBoxPreviewMode As System.Windows.Forms.ComboBox
    Friend WithEvents ComboBoxGender As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonRandomNPC As System.Windows.Forms.Button
    Friend WithEvents LabelOutfit As System.Windows.Forms.Label
    Friend WithEvents ComboBoxOutfit As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonReroll As System.Windows.Forms.Button
    Friend WithEvents LabelMorphs As System.Windows.Forms.Label
    Friend WithEvents LabelRenders As System.Windows.Forms.Label
    Friend WithEvents PanelActionsToolbar As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelEdit As System.Windows.Forms.Label
    Friend WithEvents ButtonEditFace As System.Windows.Forms.Button
    Friend WithEvents ButtonBuildCharGen As System.Windows.Forms.Button
    Friend WithEvents ButtonCharGenOptions As System.Windows.Forms.Button
    Friend WithEvents ButtonSaveSceneNif As System.Windows.Forms.Button
    Friend WithEvents ButtonEditBody As System.Windows.Forms.Button
    Friend WithEvents ButtonEditOutfit As System.Windows.Forms.Button
    Friend WithEvents ButtonArmaEditor As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoEditor As System.Windows.Forms.Button
    Friend WithEvents SeparatorActions1 As System.Windows.Forms.Label
    Friend WithEvents LabelLooksMenu As System.Windows.Forms.Label
    Friend WithEvents ButtonLoadLooksmenu As System.Windows.Forms.Button
    Friend WithEvents ButtonSaveLooksmenu As System.Windows.Forms.Button
    Friend WithEvents SeparatorActions2 As System.Windows.Forms.Label
    Friend WithEvents LabelLook As System.Windows.Forms.Label
    Friend WithEvents ButtonCopyLook As System.Windows.Forms.Button
    Friend WithEvents ButtonPasteLook As System.Windows.Forms.Button
    Friend WithEvents SeparatorActions3 As System.Windows.Forms.Label
    Friend WithEvents ButtonSavePlugin As System.Windows.Forms.Button
    Friend WithEvents CheckBoxApplyBoneMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyVertexMorphs As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyBodyWeight As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplySculpt As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderArmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderUnderarmor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderBody As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRenderHeadwear As System.Windows.Forms.CheckBox
    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents PanelAnimBar As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ComboAnim As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonSelectAnim As System.Windows.Forms.Button
    Friend WithEvents ButtonAnimPlay As System.Windows.Forms.Button
    Friend WithEvents SliderAnimFrame As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents NumericAnimFrameMs As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelAnimMs As System.Windows.Forms.Label
    Friend WithEvents StatusStrip1 As System.Windows.Forms.StatusStrip
    Friend WithEvents ToolStripStatusLabel1 As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents ToolStripProgressBar1 As System.Windows.Forms.ToolStripProgressBar
    Friend WithEvents SplitContainer2 As SplitContainer
    Friend WithEvents CheckBoxBodyTri As CheckBox
    Friend WithEvents CheckBoxRenderGore As CheckBox
    Friend WithEvents ButtonLightRig As System.Windows.Forms.Button
    Friend WithEvents Label1 As Label
    Friend WithEvents Label2 As Label
    Friend WithEvents Label3 As Label
End Class
