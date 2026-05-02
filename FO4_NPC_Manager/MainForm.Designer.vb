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
        SplitContainer1 = New SplitContainer()
        SplitContainerLeft = New SplitContainer()
        PanelNpcList = New Panel()
        SplitContainer2 = New SplitContainer()
        TextBoxSearch = New TextBox()
        LabelSearch = New Label()
        TreeViewNPCs = New TreeView()
        PanelRecordDetails = New Panel()
        TreeViewRecordDetails = New TreeView()
        LabelRecordTitle = New Label()
        SplitContainerPreview = New SplitContainer()
        PanelPreviewControls = New Panel()
        PanelActionsToolbar = New TableLayoutPanel()
        LabelEdit = New Label()
        ButtonEditFace = New Button()
        ButtonEditBody = New Button()
        ButtonEditOutfit = New Button()
        SeparatorActions1 = New Label()
        LabelLooksMenu = New Label()
        ButtonLoadLooksmenu = New Button()
        ButtonSaveLooksmenu = New Button()
        SeparatorActions2 = New Label()
        LabelLook = New Label()
        ButtonCopyLook = New Button()
        ButtonPasteLook = New Button()
        SeparatorActions3 = New Label()
        ButtonSavePlugin = New Button()
        PanelPreviewToolbar = New TableLayoutPanel()
        LabelPreviewMode = New Label()
        ComboBoxPreviewMode = New ComboBox()
        ComboBoxGender = New ComboBox()
        LabelOutfit = New Label()
        ComboBoxOutfit = New ComboBox()
        LabelMorphs = New Label()
        CheckBoxApplyBoneMorphs = New CheckBox()
        CheckBoxApplyVertexMorphs = New CheckBox()
        CheckBoxApplyBodyWeight = New CheckBox()
        CheckBoxApplySculpt = New CheckBox()
        LabelRenders = New Label()
        CheckBoxRenderBody = New CheckBox()
        CheckBoxRenderUnderarmor = New CheckBox()
        CheckBoxRenderArmor = New CheckBox()
        CheckBoxRenderHeadwear = New CheckBox()
        ButtonRandomNPC = New Button()
        ButtonReroll = New Button()
        LabelStatus = New Label()
        PanelPreviewHost = New Panel()
        StatusStrip1 = New StatusStrip()
        ToolStripStatusLabel1 = New ToolStripStatusLabel()
        ToolStripProgressBar1 = New ToolStripProgressBar()
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
        CType(SplitContainerPreview, ComponentModel.ISupportInitialize).BeginInit()
        SplitContainerPreview.Panel1.SuspendLayout()
        SplitContainerPreview.Panel2.SuspendLayout()
        SplitContainerPreview.SuspendLayout()
        PanelPreviewControls.SuspendLayout()
        PanelActionsToolbar.SuspendLayout()
        PanelPreviewToolbar.SuspendLayout()
        StatusStrip1.SuspendLayout()
        SuspendLayout()
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
        SplitContainer1.Panel2.Controls.Add(SplitContainerPreview)
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
        ' 
        ' SplitContainer2.Panel2
        ' 
        SplitContainer2.Panel2.Controls.Add(TreeViewNPCs)
        SplitContainer2.Size = New Size(700, 550)
        SplitContainer2.SplitterDistance = 49
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
        ' TreeViewNPCs
        ' 
        TreeViewNPCs.BorderStyle = BorderStyle.FixedSingle
        TreeViewNPCs.Dock = DockStyle.Fill
        TreeViewNPCs.DrawMode = TreeViewDrawMode.OwnerDrawText
        TreeViewNPCs.HideSelection = False
        TreeViewNPCs.Location = New Point(0, 0)
        TreeViewNPCs.Name = "TreeViewNPCs"
        TreeViewNPCs.Size = New Size(700, 497)
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
        TreeViewRecordDetails.Font = New Font("Cascadia Code", 8.5F)
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
        ' SplitContainerPreview
        ' 
        SplitContainerPreview.Dock = DockStyle.Fill
        SplitContainerPreview.FixedPanel = FixedPanel.Panel1
        SplitContainerPreview.Location = New Point(0, 0)
        SplitContainerPreview.Name = "SplitContainerPreview"
        SplitContainerPreview.Orientation = Orientation.Horizontal
        ' 
        ' SplitContainerPreview.Panel1
        ' 
        SplitContainerPreview.Panel1.Controls.Add(PanelPreviewControls)
        SplitContainerPreview.Panel1MinSize = 120
        ' 
        ' SplitContainerPreview.Panel2
        ' 
        SplitContainerPreview.Panel2.Controls.Add(PanelPreviewHost)
        SplitContainerPreview.Panel2MinSize = 200
        SplitContainerPreview.Size = New Size(1200, 1019)
        SplitContainerPreview.SplitterDistance = 160
        SplitContainerPreview.TabIndex = 0
        ' 
        ' PanelPreviewControls
        ' 
        PanelPreviewControls.Controls.Add(PanelActionsToolbar)
        PanelPreviewControls.Controls.Add(PanelPreviewToolbar)
        PanelPreviewControls.Controls.Add(LabelStatus)
        PanelPreviewControls.Dock = DockStyle.Fill
        PanelPreviewControls.Location = New Point(0, 0)
        PanelPreviewControls.Name = "PanelPreviewControls"
        PanelPreviewControls.Padding = New Padding(8, 6, 8, 6)
        PanelPreviewControls.Size = New Size(1200, 160)
        PanelPreviewControls.TabIndex = 0
        ' 
        ' PanelActionsToolbar
        ' 
        PanelActionsToolbar.AutoSize = True
        PanelActionsToolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelActionsToolbar.ColumnCount = 15
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle())
        PanelActionsToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PanelActionsToolbar.Controls.Add(LabelEdit, 0, 0)
        PanelActionsToolbar.Controls.Add(ButtonEditFace, 1, 0)
        PanelActionsToolbar.Controls.Add(ButtonEditBody, 2, 0)
        PanelActionsToolbar.Controls.Add(ButtonEditOutfit, 3, 0)
        PanelActionsToolbar.Controls.Add(SeparatorActions1, 4, 0)
        PanelActionsToolbar.Controls.Add(LabelLooksMenu, 5, 0)
        PanelActionsToolbar.Controls.Add(ButtonLoadLooksmenu, 6, 0)
        PanelActionsToolbar.Controls.Add(ButtonSaveLooksmenu, 7, 0)
        PanelActionsToolbar.Controls.Add(SeparatorActions2, 8, 0)
        PanelActionsToolbar.Controls.Add(LabelLook, 9, 0)
        PanelActionsToolbar.Controls.Add(ButtonCopyLook, 10, 0)
        PanelActionsToolbar.Controls.Add(ButtonPasteLook, 11, 0)
        PanelActionsToolbar.Controls.Add(SeparatorActions3, 12, 0)
        PanelActionsToolbar.Controls.Add(ButtonSavePlugin, 13, 0)
        PanelActionsToolbar.Dock = DockStyle.Top
        PanelActionsToolbar.Location = New Point(8, 108)
        PanelActionsToolbar.Name = "PanelActionsToolbar"
        PanelActionsToolbar.RowCount = 1
        PanelActionsToolbar.RowStyles.Add(New RowStyle())
        PanelActionsToolbar.Size = New Size(1184, 32)
        PanelActionsToolbar.TabIndex = 1
        ' 
        ' LabelEdit
        ' 
        LabelEdit.Anchor = AnchorStyles.Left
        LabelEdit.AutoSize = True
        LabelEdit.Location = New Point(2, 12)
        LabelEdit.Margin = New Padding(2, 8, 6, 0)
        LabelEdit.Name = "LabelEdit"
        LabelEdit.Size = New Size(30, 15)
        LabelEdit.TabIndex = 0
        LabelEdit.Text = "Edit:"
        ' 
        ' ButtonEditFace
        ' 
        ButtonEditFace.AutoSize = True
        ButtonEditFace.Enabled = False
        ButtonEditFace.Location = New Point(40, 2)
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
        ButtonEditBody.Location = New Point(124, 2)
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
        ButtonEditOutfit.Location = New Point(208, 2)
        ButtonEditOutfit.Margin = New Padding(2)
        ButtonEditOutfit.MinimumSize = New Size(80, 28)
        ButtonEditOutfit.Name = "ButtonEditOutfit"
        ButtonEditOutfit.Size = New Size(80, 28)
        ButtonEditOutfit.TabIndex = 3
        ButtonEditOutfit.Text = "Outfit"
        ButtonEditOutfit.UseVisualStyleBackColor = True
        ' 
        ' SeparatorActions1
        ' 
        SeparatorActions1.BorderStyle = BorderStyle.Fixed3D
        SeparatorActions1.Location = New Point(298, 4)
        SeparatorActions1.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions1.Name = "SeparatorActions1"
        SeparatorActions1.Size = New Size(2, 24)
        SeparatorActions1.TabIndex = 4
        ' 
        ' LabelLooksMenu
        ' 
        LabelLooksMenu.Anchor = AnchorStyles.Left
        LabelLooksMenu.AutoSize = True
        LabelLooksMenu.Location = New Point(310, 12)
        LabelLooksMenu.Margin = New Padding(2, 8, 6, 0)
        LabelLooksMenu.Name = "LabelLooksMenu"
        LabelLooksMenu.Size = New Size(72, 15)
        LabelLooksMenu.TabIndex = 5
        LabelLooksMenu.Text = "LooksMenu:"
        ' 
        ' ButtonLoadLooksmenu
        ' 
        ButtonLoadLooksmenu.AutoSize = True
        ButtonLoadLooksmenu.Enabled = False
        ButtonLoadLooksmenu.Location = New Point(390, 2)
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
        ButtonSaveLooksmenu.Location = New Point(474, 2)
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
        SeparatorActions2.Location = New Point(564, 4)
        SeparatorActions2.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions2.Name = "SeparatorActions2"
        SeparatorActions2.Size = New Size(2, 24)
        SeparatorActions2.TabIndex = 8
        ' 
        ' LabelLook
        ' 
        LabelLook.Anchor = AnchorStyles.Left
        LabelLook.AutoSize = True
        LabelLook.Location = New Point(576, 12)
        LabelLook.Margin = New Padding(2, 8, 6, 0)
        LabelLook.Name = "LabelLook"
        LabelLook.Size = New Size(36, 15)
        LabelLook.TabIndex = 9
        LabelLook.Text = "Look:"
        ' 
        ' ButtonCopyLook
        ' 
        ButtonCopyLook.AutoSize = True
        ButtonCopyLook.Enabled = False
        ButtonCopyLook.Location = New Point(620, 2)
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
        ButtonPasteLook.Location = New Point(704, 2)
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
        SeparatorActions3.Location = New Point(794, 4)
        SeparatorActions3.Margin = New Padding(8, 4, 8, 4)
        SeparatorActions3.Name = "SeparatorActions3"
        SeparatorActions3.Size = New Size(2, 24)
        SeparatorActions3.TabIndex = 12
        ' 
        ' ButtonSavePlugin
        ' 
        ButtonSavePlugin.AutoSize = True
        ButtonSavePlugin.Enabled = False
        ButtonSavePlugin.Location = New Point(806, 2)
        ButtonSavePlugin.Margin = New Padding(2)
        ButtonSavePlugin.MinimumSize = New Size(110, 28)
        ButtonSavePlugin.Name = "ButtonSavePlugin"
        ButtonSavePlugin.Size = New Size(110, 28)
        ButtonSavePlugin.TabIndex = 13
        ButtonSavePlugin.Text = "Save ESP/ESM"
        ButtonSavePlugin.UseVisualStyleBackColor = True
        ' 
        ' PanelPreviewToolbar
        ' 
        PanelPreviewToolbar.AutoSize = True
        PanelPreviewToolbar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelPreviewToolbar.ColumnCount = 6
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 25F))
        PanelPreviewToolbar.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 30F))
        PanelPreviewToolbar.Controls.Add(LabelPreviewMode, 0, 0)
        PanelPreviewToolbar.Controls.Add(ComboBoxPreviewMode, 1, 0)
        PanelPreviewToolbar.Controls.Add(ComboBoxGender, 2, 0)
        PanelPreviewToolbar.Controls.Add(LabelOutfit, 0, 1)
        PanelPreviewToolbar.Controls.Add(ComboBoxOutfit, 1, 1)
        PanelPreviewToolbar.Controls.Add(LabelMorphs, 0, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyBoneMorphs, 1, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyVertexMorphs, 2, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplyBodyWeight, 3, 2)
        PanelPreviewToolbar.Controls.Add(CheckBoxApplySculpt, 4, 2)
        PanelPreviewToolbar.Controls.Add(LabelRenders, 0, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderBody, 1, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderUnderarmor, 2, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderArmor, 3, 3)
        PanelPreviewToolbar.Controls.Add(CheckBoxRenderHeadwear, 4, 3)
        PanelPreviewToolbar.Controls.Add(ButtonRandomNPC, 5, 0)
        PanelPreviewToolbar.Controls.Add(ButtonReroll, 5, 1)
        PanelPreviewToolbar.Dock = DockStyle.Top
        PanelPreviewToolbar.Location = New Point(8, 6)
        PanelPreviewToolbar.Name = "PanelPreviewToolbar"
        PanelPreviewToolbar.RowCount = 4
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.RowStyles.Add(New RowStyle())
        PanelPreviewToolbar.Size = New Size(1184, 102)
        PanelPreviewToolbar.TabIndex = 0
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
        ComboBoxPreviewMode.Size = New Size(265, 23)
        ComboBoxPreviewMode.TabIndex = 1
        ' 
        ' ComboBoxGender
        ' 
        PanelPreviewToolbar.SetColumnSpan(ComboBoxGender, 3)
        ComboBoxGender.Dock = DockStyle.Fill
        ComboBoxGender.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxGender.Items.AddRange(New Object() {"Random", "Male", "Female"})
        ComboBoxGender.Location = New Point(343, 2)
        ComboBoxGender.Margin = New Padding(2, 2, 4, 2)
        ComboBoxGender.Name = "ComboBoxGender"
        ComboBoxGender.Size = New Size(807, 23)
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
        PanelPreviewToolbar.SetColumnSpan(ComboBoxOutfit, 4)
        ComboBoxOutfit.Dock = DockStyle.Fill
        ComboBoxOutfit.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxOutfit.Location = New Point(72, 32)
        ComboBoxOutfit.Margin = New Padding(2, 2, 4, 2)
        ComboBoxOutfit.Name = "ComboBoxOutfit"
        ComboBoxOutfit.Size = New Size(1078, 23)
        ComboBoxOutfit.TabIndex = 5
        ' 
        ' LabelMorphs
        ' 
        LabelMorphs.Anchor = AnchorStyles.Left
        LabelMorphs.AutoSize = True
        LabelMorphs.Location = New Point(2, 65)
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
        CheckBoxApplyVertexMorphs.Location = New Point(343, 61)
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
        CheckBoxApplyBodyWeight.Location = New Point(614, 61)
        CheckBoxApplyBodyWeight.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplyBodyWeight.Name = "CheckBoxApplyBodyWeight"
        CheckBoxApplyBodyWeight.Size = New Size(140, 19)
        CheckBoxApplyBodyWeight.TabIndex = 9
        CheckBoxApplyBodyWeight.Text = "Body weight (MWGT)"
        ' 
        ' CheckBoxApplySculpt
        ' 
        CheckBoxApplySculpt.AutoSize = True
        CheckBoxApplySculpt.Checked = True
        CheckBoxApplySculpt.CheckState = CheckState.Checked
        CheckBoxApplySculpt.Location = New Point(885, 61)
        CheckBoxApplySculpt.Margin = New Padding(2, 1, 8, 1)
        CheckBoxApplySculpt.Name = "CheckBoxApplySculpt"
        CheckBoxApplySculpt.Size = New Size(134, 19)
        CheckBoxApplySculpt.TabIndex = 10
        CheckBoxApplySculpt.Text = "Sculpt (ARMA SCLP)"
        ' 
        ' LabelRenders
        ' 
        LabelRenders.Anchor = AnchorStyles.Left
        LabelRenders.AutoSize = True
        LabelRenders.Location = New Point(2, 86)
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
        CheckBoxRenderBody.Location = New Point(72, 82)
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
        CheckBoxRenderUnderarmor.Location = New Point(343, 82)
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
        CheckBoxRenderArmor.Location = New Point(614, 82)
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
        CheckBoxRenderHeadwear.Location = New Point(885, 82)
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
        ButtonRandomNPC.Location = New Point(1156, 2)
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
        ButtonReroll.Location = New Point(1156, 32)
        ButtonReroll.Margin = New Padding(2)
        ButtonReroll.Name = "ButtonReroll"
        ButtonReroll.Size = New Size(26, 26)
        ButtonReroll.TabIndex = 6
        ButtonReroll.Text = "↻"
        ' 
        ' LabelStatus
        ' 
        LabelStatus.Dock = DockStyle.Fill
        LabelStatus.Font = New Font("Segoe UI", 11F)
        LabelStatus.ForeColor = SystemColors.GrayText
        LabelStatus.Location = New Point(8, 6)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(1184, 148)
        LabelStatus.TabIndex = 1
        LabelStatus.Text = "Loading..."
        LabelStatus.TextAlign = ContentAlignment.MiddleCenter
        ' 
        ' PanelPreviewHost
        ' 
        PanelPreviewHost.BackColor = Color.FromArgb(CByte(40), CByte(40), CByte(44))
        PanelPreviewHost.Dock = DockStyle.Fill
        PanelPreviewHost.Location = New Point(0, 0)
        PanelPreviewHost.Name = "PanelPreviewHost"
        PanelPreviewHost.Size = New Size(1200, 855)
        PanelPreviewHost.TabIndex = 0
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
        ClientSize = New Size(1904, 1041)
        Controls.Add(SplitContainer1)
        Controls.Add(StatusStrip1)
        Name = "MainForm"
        StartPosition = FormStartPosition.CenterScreen
        Text = "FO4 NPC Manager"
        WindowState = FormWindowState.Maximized
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
        SplitContainerPreview.Panel1.ResumeLayout(False)
        SplitContainerPreview.Panel2.ResumeLayout(False)
        CType(SplitContainerPreview, ComponentModel.ISupportInitialize).EndInit()
        SplitContainerPreview.ResumeLayout(False)
        PanelPreviewControls.ResumeLayout(False)
        PanelPreviewControls.PerformLayout()
        PanelActionsToolbar.ResumeLayout(False)
        PanelActionsToolbar.PerformLayout()
        PanelPreviewToolbar.ResumeLayout(False)
        PanelPreviewToolbar.PerformLayout()
        StatusStrip1.ResumeLayout(False)
        StatusStrip1.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents SplitContainer1 As System.Windows.Forms.SplitContainer
    Friend WithEvents SplitContainerLeft As System.Windows.Forms.SplitContainer
    Friend WithEvents SplitContainerPreview As System.Windows.Forms.SplitContainer
    Friend WithEvents PanelNpcList As System.Windows.Forms.Panel
    Friend WithEvents PanelRecordDetails As System.Windows.Forms.Panel
    Friend WithEvents PanelPreviewControls As System.Windows.Forms.Panel
    Friend WithEvents PanelPreviewHost As System.Windows.Forms.Panel
    Friend WithEvents TreeViewNPCs As System.Windows.Forms.TreeView
    Friend WithEvents TextBoxSearch As System.Windows.Forms.TextBox
    Friend WithEvents LabelSearch As System.Windows.Forms.Label
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
    Friend WithEvents PanelActionsToolbar As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelEdit As System.Windows.Forms.Label
    Friend WithEvents ButtonEditFace As System.Windows.Forms.Button
    Friend WithEvents ButtonEditBody As System.Windows.Forms.Button
    Friend WithEvents ButtonEditOutfit As System.Windows.Forms.Button
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
    Friend WithEvents StatusStrip1 As System.Windows.Forms.StatusStrip
    Friend WithEvents ToolStripStatusLabel1 As System.Windows.Forms.ToolStripStatusLabel
    Friend WithEvents ToolStripProgressBar1 As System.Windows.Forms.ToolStripProgressBar
    Friend WithEvents SplitContainer2 As SplitContainer
End Class
