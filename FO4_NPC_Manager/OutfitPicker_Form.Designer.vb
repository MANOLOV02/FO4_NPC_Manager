' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class OutfitPicker_Form
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
        RootLayout = New TableLayoutPanel()
        MainSplit = New SplitContainer()
        TabsMain = New TabControl()
        TabPageBrowse = New TabPage()
        BrowseLayout = New TableLayoutPanel()
        LabelHeader = New Label()
        TextBoxFilter = New TextBox()
        ListViewParts = New ListView()
        ColumnName = New ColumnHeader()
        ColumnFormID = New ColumnHeader()
        ColumnPlugin = New ColumnHeader()
        TabPageCreate = New TabPage()
        CreateLayout = New TableLayoutPanel()
        LabelItems = New Label()
        TextBoxItemFilter = New TextBox()
        ListViewItems = New ListView()
        ColItemName = New ColumnHeader()
        ColItemSlots = New ColumnHeader()
        ColItemFormID = New ColumnHeader()
        ColItemPlugin = New ColumnHeader()
        AddButtonsRow = New FlowLayoutPanel()
        ButtonAddItem = New Button()
        ButtonAddToLvl = New Button()
        ButtonReroll = New Button()
        LabelPieces = New Label()
        ListViewPieces = New ListView()
        ColPieceName = New ColumnHeader()
        ColPieceSlots = New ColumnHeader()
        ColPieceStatus = New ColumnHeader()
        Plugin = New ColumnHeader()
        PiecesButtonsRow = New FlowLayoutPanel()
        ButtonNewLvl = New Button()
        ButtonRemovePiece = New Button()
        EdidRow = New FlowLayoutPanel()
        LabelEdidPrefix = New Label()
        TextBoxEdid = New TextBox()
        ModeRow = New FlowLayoutPanel()
        ButtonNewOutfit = New Button()
        ButtonOverrideOutfit = New Button()
        LabelCreateStatus = New Label()
        LabelCreateBanner = New Label()
        PreviewLayout = New TableLayoutPanel()
        PreviewControlPanel = New Panel()
        PreviewModeRow = New FlowLayoutPanel()
        RadioButtonRenderOutfit = New RadioButton()
        RadioButtonRenderPiece = New RadioButton()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.Panel1.SuspendLayout()
        MainSplit.Panel2.SuspendLayout()
        MainSplit.SuspendLayout()
        TabsMain.SuspendLayout()
        TabPageBrowse.SuspendLayout()
        BrowseLayout.SuspendLayout()
        TabPageCreate.SuspendLayout()
        CreateLayout.SuspendLayout()
        AddButtonsRow.SuspendLayout()
        PiecesButtonsRow.SuspendLayout()
        EdidRow.SuspendLayout()
        ModeRow.SuspendLayout()
        PreviewLayout.SuspendLayout()
        PreviewModeRow.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        ' 
        ' RootLayout
        ' 
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(MainSplit, 0, 0)
        RootLayout.Controls.Add(BottomLayout, 0, 1)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 2
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(1244, 581)
        RootLayout.TabIndex = 0
        ' 
        ' MainSplit
        ' 
        MainSplit.Dock = DockStyle.Fill
        MainSplit.Location = New Point(11, 11)
        MainSplit.Name = "MainSplit"
        ' 
        ' MainSplit.Panel1
        ' 
        MainSplit.Panel1.Controls.Add(TabsMain)
        ' 
        ' MainSplit.Panel2
        ' 
        MainSplit.Panel2.Controls.Add(PreviewLayout)
        MainSplit.Size = New Size(1222, 518)
        MainSplit.SplitterDistance = 739
        MainSplit.TabIndex = 0
        ' 
        ' TabsMain
        ' 
        TabsMain.Controls.Add(TabPageBrowse)
        TabsMain.Controls.Add(TabPageCreate)
        TabsMain.Dock = DockStyle.Fill
        TabsMain.Location = New Point(0, 0)
        TabsMain.Name = "TabsMain"
        TabsMain.SelectedIndex = 0
        TabsMain.Size = New Size(739, 518)
        TabsMain.TabIndex = 0
        ' 
        ' TabPageBrowse
        ' 
        TabPageBrowse.Controls.Add(BrowseLayout)
        TabPageBrowse.Location = New Point(4, 24)
        TabPageBrowse.Name = "TabPageBrowse"
        TabPageBrowse.Padding = New Padding(6)
        TabPageBrowse.Size = New Size(731, 490)
        TabPageBrowse.TabIndex = 0
        TabPageBrowse.Text = "Browse"
        TabPageBrowse.UseVisualStyleBackColor = True
        ' 
        ' BrowseLayout
        ' 
        BrowseLayout.ColumnCount = 1
        BrowseLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        BrowseLayout.Controls.Add(LabelHeader, 0, 0)
        BrowseLayout.Controls.Add(TextBoxFilter, 0, 1)
        BrowseLayout.Controls.Add(ListViewParts, 0, 2)
        BrowseLayout.Dock = DockStyle.Fill
        BrowseLayout.Location = New Point(6, 6)
        BrowseLayout.Name = "BrowseLayout"
        BrowseLayout.RowCount = 3
        BrowseLayout.RowStyles.Add(New RowStyle())
        BrowseLayout.RowStyles.Add(New RowStyle())
        BrowseLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        BrowseLayout.Size = New Size(719, 478)
        BrowseLayout.TabIndex = 0
        ' 
        ' LabelHeader
        ' 
        LabelHeader.AutoSize = True
        LabelHeader.Location = New Point(0, 0)
        LabelHeader.Margin = New Padding(0, 0, 0, 4)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(70, 15)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Select outfit"
        ' 
        ' TextBoxFilter
        ' 
        TextBoxFilter.Dock = DockStyle.Top
        TextBoxFilter.Location = New Point(0, 19)
        TextBoxFilter.Margin = New Padding(0, 0, 0, 6)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name, plugin or FormID..."
        TextBoxFilter.Size = New Size(719, 23)
        TextBoxFilter.TabIndex = 1
        ' 
        ' ListViewParts
        ' 
        ListViewParts.Columns.AddRange(New ColumnHeader() {ColumnName, ColumnFormID, ColumnPlugin})
        ListViewParts.Dock = DockStyle.Fill
        ListViewParts.FullRowSelect = True
        ListViewParts.Location = New Point(3, 51)
        ListViewParts.MultiSelect = False
        ListViewParts.Name = "ListViewParts"
        ListViewParts.Size = New Size(713, 424)
        ListViewParts.TabIndex = 2
        ListViewParts.UseCompatibleStateImageBehavior = False
        ListViewParts.View = View.Details
        ' 
        ' ColumnName
        ' 
        ColumnName.Text = "Outfit"
        ColumnName.Width = 420
        ' 
        ' ColumnFormID
        ' 
        ColumnFormID.Text = "FormID"
        ColumnFormID.Width = 90
        ' 
        ' ColumnPlugin
        ' 
        ColumnPlugin.Text = "Plugin"
        ColumnPlugin.Width = 183
        ' 
        ' TabPageCreate
        ' 
        TabPageCreate.Controls.Add(CreateLayout)
        TabPageCreate.Location = New Point(4, 24)
        TabPageCreate.Name = "TabPageCreate"
        TabPageCreate.Padding = New Padding(6)
        TabPageCreate.Size = New Size(731, 490)
        TabPageCreate.TabIndex = 1
        TabPageCreate.Text = "Create"
        TabPageCreate.UseVisualStyleBackColor = True
        ' 
        ' CreateLayout
        ' 
        CreateLayout.ColumnCount = 1
        CreateLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        CreateLayout.Controls.Add(LabelCreateBanner, 0, 0)
        CreateLayout.Controls.Add(LabelItems, 0, 1)
        CreateLayout.Controls.Add(TextBoxItemFilter, 0, 2)
        CreateLayout.Controls.Add(ListViewItems, 0, 3)
        CreateLayout.Controls.Add(AddButtonsRow, 0, 4)
        CreateLayout.Controls.Add(LabelPieces, 0, 5)
        CreateLayout.Controls.Add(ListViewPieces, 0, 6)
        CreateLayout.Controls.Add(PiecesButtonsRow, 0, 7)
        CreateLayout.Controls.Add(EdidRow, 0, 8)
        CreateLayout.Controls.Add(ModeRow, 0, 9)
        CreateLayout.Controls.Add(LabelCreateStatus, 0, 10)
        CreateLayout.Dock = DockStyle.Fill
        CreateLayout.Location = New Point(6, 6)
        CreateLayout.Name = "CreateLayout"
        CreateLayout.RowCount = 11
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.RowStyles.Add(New RowStyle())
        CreateLayout.Size = New Size(719, 478)
        CreateLayout.TabIndex = 0
        ' 
        ' LabelItems
        ' 
        LabelItems.AutoSize = True
        LabelItems.Location = New Point(0, 0)
        LabelItems.Margin = New Padding(0, 0, 0, 2)
        LabelItems.Name = "LabelItems"
        LabelItems.Size = New Size(114, 15)
        LabelItems.TabIndex = 0
        LabelItems.Text = "Items (race/gender):"
        ' 
        ' TextBoxItemFilter
        ' 
        TextBoxItemFilter.Dock = DockStyle.Top
        TextBoxItemFilter.Location = New Point(0, 17)
        TextBoxItemFilter.Margin = New Padding(0, 0, 0, 4)
        TextBoxItemFilter.Name = "TextBoxItemFilter"
        TextBoxItemFilter.PlaceholderText = "Filter by name, slot, plugin or FormID..."
        TextBoxItemFilter.Size = New Size(719, 23)
        TextBoxItemFilter.TabIndex = 1
        ' 
        ' ListViewItems
        ' 
        ListViewItems.Columns.AddRange(New ColumnHeader() {ColItemName, ColItemSlots, ColItemFormID, ColItemPlugin})
        ListViewItems.Dock = DockStyle.Fill
        ListViewItems.FullRowSelect = True
        ListViewItems.Location = New Point(3, 47)
        ListViewItems.MultiSelect = False
        ListViewItems.Name = "ListViewItems"
        ListViewItems.Size = New Size(713, 133)
        ListViewItems.TabIndex = 2
        ListViewItems.UseCompatibleStateImageBehavior = False
        ListViewItems.View = View.Details
        ' 
        ' ColItemName
        ' 
        ColItemName.Text = "Item"
        ColItemName.Width = 255
        ' 
        ' ColItemSlots
        ' 
        ColItemSlots.Text = "Slots"
        ColItemSlots.Width = 215
        ' 
        ' ColItemFormID
        ' 
        ColItemFormID.Text = "FormID"
        ColItemFormID.Width = 100
        ' 
        ' ColItemPlugin
        ' 
        ColItemPlugin.Text = "Plugin"
        ColItemPlugin.Width = 120
        ' 
        ' AddButtonsRow
        ' 
        AddButtonsRow.AutoSize = True
        AddButtonsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        AddButtonsRow.Controls.Add(ButtonAddItem)
        AddButtonsRow.Controls.Add(ButtonAddToLvl)
        AddButtonsRow.Controls.Add(ButtonReroll)
        AddButtonsRow.Location = New Point(0, 183)
        AddButtonsRow.Margin = New Padding(0)
        AddButtonsRow.Name = "AddButtonsRow"
        AddButtonsRow.Size = New Size(341, 34)
        AddButtonsRow.TabIndex = 3
        AddButtonsRow.WrapContents = False
        ' 
        ' ButtonAddItem
        ' 
        ButtonAddItem.Location = New Point(0, 3)
        ButtonAddItem.Margin = New Padding(0, 3, 0, 6)
        ButtonAddItem.Name = "ButtonAddItem"
        ButtonAddItem.Size = New Size(130, 25)
        ButtonAddItem.TabIndex = 3
        ButtonAddItem.Text = "Add to outfit ▼"
        ButtonAddItem.UseVisualStyleBackColor = True
        ' 
        ' ButtonAddToLvl
        ' 
        ButtonAddToLvl.Enabled = False
        ButtonAddToLvl.Location = New Point(138, 3)
        ButtonAddToLvl.Margin = New Padding(8, 3, 0, 6)
        ButtonAddToLvl.Name = "ButtonAddToLvl"
        ButtonAddToLvl.Size = New Size(95, 25)
        ButtonAddToLvl.TabIndex = 5
        ButtonAddToLvl.Text = "Add to lvl ▼"
        ButtonAddToLvl.UseVisualStyleBackColor = True
        ' 
        ' ButtonReroll
        ' 
        ButtonReroll.Enabled = False
        ButtonReroll.Location = New Point(241, 3)
        ButtonReroll.Margin = New Padding(8, 3, 0, 6)
        ButtonReroll.Name = "ButtonReroll"
        ButtonReroll.Size = New Size(100, 25)
        ButtonReroll.TabIndex = 6
        ButtonReroll.Text = "🎲 Reroll LVL"
        ButtonReroll.UseVisualStyleBackColor = True
        ' 
        ' LabelPieces
        ' 
        LabelPieces.AutoSize = True
        LabelPieces.Location = New Point(0, 217)
        LabelPieces.Margin = New Padding(0, 0, 0, 2)
        LabelPieces.Name = "LabelPieces"
        LabelPieces.Size = New Size(77, 15)
        LabelPieces.TabIndex = 4
        LabelPieces.Text = "Outfit pieces:"
        ' 
        ' ListViewPieces
        ' 
        ListViewPieces.Columns.AddRange(New ColumnHeader() {ColPieceName, ColPieceSlots, ColPieceStatus, Plugin})
        ListViewPieces.Dock = DockStyle.Fill
        ListViewPieces.FullRowSelect = True
        ListViewPieces.Location = New Point(3, 237)
        ListViewPieces.MultiSelect = False
        ListViewPieces.Name = "ListViewPieces"
        ListViewPieces.Size = New Size(713, 133)
        ListViewPieces.TabIndex = 5
        ListViewPieces.UseCompatibleStateImageBehavior = False
        ListViewPieces.View = View.Details
        ' 
        ' ColPieceName
        ' 
        ColPieceName.Text = "Piece"
        ColPieceName.Width = 255
        ' 
        ' ColPieceSlots
        ' 
        ColPieceSlots.Text = "Slots"
        ColPieceSlots.Width = 215
        ' 
        ' ColPieceStatus
        ' 
        ColPieceStatus.Text = "Status"
        ColPieceStatus.Width = 100
        ' 
        ' Plugin
        ' 
        Plugin.Text = "Plugin"
        Plugin.Width = 120
        ' 
        ' PiecesButtonsRow
        ' 
        PiecesButtonsRow.AutoSize = True
        PiecesButtonsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PiecesButtonsRow.Controls.Add(ButtonNewLvl)
        PiecesButtonsRow.Controls.Add(ButtonRemovePiece)
        PiecesButtonsRow.Location = New Point(0, 373)
        PiecesButtonsRow.Margin = New Padding(0)
        PiecesButtonsRow.Name = "PiecesButtonsRow"
        PiecesButtonsRow.Size = New Size(228, 34)
        PiecesButtonsRow.TabIndex = 6
        PiecesButtonsRow.WrapContents = False
        ' 
        ' ButtonNewLvl
        ' 
        ButtonNewLvl.Location = New Point(0, 3)
        ButtonNewLvl.Margin = New Padding(0, 3, 0, 6)
        ButtonNewLvl.Name = "ButtonNewLvl"
        ButtonNewLvl.Size = New Size(90, 25)
        ButtonNewLvl.TabIndex = 0
        ButtonNewLvl.Text = "New LVL…"
        ButtonNewLvl.UseVisualStyleBackColor = True
        ' 
        ' ButtonRemovePiece
        ' 
        ButtonRemovePiece.Location = New Point(98, 3)
        ButtonRemovePiece.Margin = New Padding(8, 3, 0, 6)
        ButtonRemovePiece.Name = "ButtonRemovePiece"
        ButtonRemovePiece.Size = New Size(130, 25)
        ButtonRemovePiece.TabIndex = 1
        ButtonRemovePiece.Text = "Remove piece"
        ButtonRemovePiece.UseVisualStyleBackColor = True
        ' 
        ' EdidRow
        ' 
        EdidRow.AutoSize = True
        EdidRow.Controls.Add(LabelEdidPrefix)
        EdidRow.Controls.Add(TextBoxEdid)
        EdidRow.Dock = DockStyle.Fill
        EdidRow.Location = New Point(0, 407)
        EdidRow.Margin = New Padding(0)
        EdidRow.Name = "EdidRow"
        EdidRow.Size = New Size(719, 29)
        EdidRow.TabIndex = 7
        EdidRow.WrapContents = False
        ' 
        ' LabelEdidPrefix
        ' 
        LabelEdidPrefix.Anchor = AnchorStyles.Left
        LabelEdidPrefix.AutoSize = True
        LabelEdidPrefix.Location = New Point(3, 7)
        LabelEdidPrefix.Name = "LabelEdidPrefix"
        LabelEdidPrefix.Size = New Size(110, 15)
        LabelEdidPrefix.TabIndex = 0
        LabelEdidPrefix.Text = "EDID: npcm_<esp>_Outfit_"
        ' 
        ' TextBoxEdid
        ' 
        TextBoxEdid.Location = New Point(119, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.PlaceholderText = "name"
        TextBoxEdid.Size = New Size(220, 23)
        TextBoxEdid.TabIndex = 1
        ' 
        ' ModeRow
        ' 
        ModeRow.AutoSize = True
        ModeRow.Controls.Add(ButtonNewOutfit)
        ModeRow.Controls.Add(ButtonOverrideOutfit)
        ModeRow.Dock = DockStyle.Fill
        ModeRow.Location = New Point(0, 436)
        ModeRow.Margin = New Padding(0)
        ModeRow.Name = "ModeRow"
        ModeRow.Size = New Size(719, 31)
        ModeRow.TabIndex = 8
        ModeRow.WrapContents = False
        '
        ' ButtonNewOutfit
        '
        ButtonNewOutfit.AutoSize = True
        ButtonNewOutfit.Location = New Point(3, 3)
        ButtonNewOutfit.Name = "ButtonNewOutfit"
        ButtonNewOutfit.Size = New Size(110, 25)
        ButtonNewOutfit.TabIndex = 0
        ButtonNewOutfit.Text = "New outfit"
        ButtonNewOutfit.UseVisualStyleBackColor = True
        '
        ' ButtonOverrideOutfit
        '
        ButtonOverrideOutfit.AutoSize = True
        ButtonOverrideOutfit.Location = New Point(119, 3)
        ButtonOverrideOutfit.Name = "ButtonOverrideOutfit"
        ButtonOverrideOutfit.Size = New Size(200, 25)
        ButtonOverrideOutfit.Text = "Override selected/loaded outfit…"
        ButtonOverrideOutfit.TabIndex = 1
        ButtonOverrideOutfit.UseVisualStyleBackColor = True
        '
        ' LabelCreateBanner
        '
        LabelCreateBanner.AutoSize = True
        LabelCreateBanner.Dock = DockStyle.Fill
        LabelCreateBanner.Font = New Font("Segoe UI", 9.75F, FontStyle.Bold)
        LabelCreateBanner.Location = New Point(3, 0)
        LabelCreateBanner.Margin = New Padding(3, 2, 3, 6)
        LabelCreateBanner.Name = "LabelCreateBanner"
        LabelCreateBanner.Size = New Size(713, 19)
        LabelCreateBanner.TabIndex = 0
        LabelCreateBanner.Text = "NEW outfit"
        '
        ' LabelCreateStatus
        '
        LabelCreateStatus.AutoSize = True
        LabelCreateStatus.ForeColor = Color.DimGray
        LabelCreateStatus.Location = New Point(0, 463)
        LabelCreateStatus.Margin = New Padding(0, 2, 0, 0)
        LabelCreateStatus.Name = "LabelCreateStatus"
        LabelCreateStatus.Size = New Size(0, 15)
        LabelCreateStatus.TabIndex = 9
        ' 
        ' PreviewLayout
        ' 
        PreviewLayout.ColumnCount = 1
        PreviewLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PreviewLayout.Controls.Add(PreviewControlPanel, 0, 0)
        PreviewLayout.Controls.Add(PreviewModeRow, 0, 1)
        PreviewLayout.Dock = DockStyle.Fill
        PreviewLayout.Location = New Point(0, 0)
        PreviewLayout.Name = "PreviewLayout"
        PreviewLayout.RowCount = 2
        PreviewLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PreviewLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 34F))
        PreviewLayout.Size = New Size(479, 518)
        PreviewLayout.TabIndex = 0
        ' 
        ' PreviewControlPanel
        ' 
        PreviewControlPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewControlPanel.Dock = DockStyle.Fill
        PreviewControlPanel.Location = New Point(0, 0)
        PreviewControlPanel.Margin = New Padding(0)
        PreviewControlPanel.Name = "PreviewControlPanel"
        PreviewControlPanel.Size = New Size(479, 484)
        PreviewControlPanel.TabIndex = 0
        ' 
        ' PreviewModeRow
        ' 
        PreviewModeRow.Controls.Add(RadioButtonRenderOutfit)
        PreviewModeRow.Controls.Add(RadioButtonRenderPiece)
        PreviewModeRow.Dock = DockStyle.Fill
        PreviewModeRow.Location = New Point(0, 484)
        PreviewModeRow.Margin = New Padding(0)
        PreviewModeRow.Name = "PreviewModeRow"
        PreviewModeRow.Size = New Size(479, 34)
        PreviewModeRow.TabIndex = 1
        PreviewModeRow.WrapContents = False
        ' 
        ' RadioButtonRenderOutfit
        ' 
        RadioButtonRenderOutfit.AutoSize = True
        RadioButtonRenderOutfit.Checked = True
        RadioButtonRenderOutfit.Location = New Point(3, 3)
        RadioButtonRenderOutfit.Name = "RadioButtonRenderOutfit"
        RadioButtonRenderOutfit.Size = New Size(133, 19)
        RadioButtonRenderOutfit.TabIndex = 0
        RadioButtonRenderOutfit.TabStop = True
        RadioButtonRenderOutfit.Text = "Render Whole Outfit"
        RadioButtonRenderOutfit.UseVisualStyleBackColor = True
        ' 
        ' RadioButtonRenderPiece
        ' 
        RadioButtonRenderPiece.AutoSize = True
        RadioButtonRenderPiece.Location = New Point(142, 3)
        RadioButtonRenderPiece.Name = "RadioButtonRenderPiece"
        RadioButtonRenderPiece.Size = New Size(168, 19)
        RadioButtonRenderPiece.TabIndex = 1
        RadioButtonRenderPiece.Text = "Render Selected Piece Only"
        RadioButtonRenderPiece.UseVisualStyleBackColor = True
        ' 
        ' BottomLayout
        ' 
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 535)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(1222, 35)
        BottomLayout.TabIndex = 1
        ' 
        ' ButtonOk
        ' 
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(1139, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(1053, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' OutfitPicker_Form
        ' 
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(1244, 581)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MaximizeBox = False
        MinimizeBox = False
        Name = "OutfitPicker_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Change Outfit"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        MainSplit.Panel1.ResumeLayout(False)
        MainSplit.Panel2.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        TabsMain.ResumeLayout(False)
        TabPageBrowse.ResumeLayout(False)
        BrowseLayout.ResumeLayout(False)
        BrowseLayout.PerformLayout()
        TabPageCreate.ResumeLayout(False)
        CreateLayout.ResumeLayout(False)
        CreateLayout.PerformLayout()
        AddButtonsRow.ResumeLayout(False)
        PiecesButtonsRow.ResumeLayout(False)
        EdidRow.ResumeLayout(False)
        EdidRow.PerformLayout()
        ModeRow.ResumeLayout(False)
        ModeRow.PerformLayout()
        PreviewLayout.ResumeLayout(False)
        PreviewModeRow.ResumeLayout(False)
        PreviewModeRow.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents MainSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents TabsMain As System.Windows.Forms.TabControl
    Friend WithEvents TabPageBrowse As System.Windows.Forms.TabPage
    Friend WithEvents BrowseLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents TextBoxFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListViewParts As System.Windows.Forms.ListView
    Friend WithEvents ColumnName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents TabPageCreate As System.Windows.Forms.TabPage
    Friend WithEvents CreateLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelItems As System.Windows.Forms.Label
    Friend WithEvents TextBoxItemFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListViewItems As System.Windows.Forms.ListView
    Friend WithEvents ColItemName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColItemSlots As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColItemFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColItemPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents ButtonAddItem As System.Windows.Forms.Button
    Friend WithEvents AddButtonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonReroll As System.Windows.Forms.Button
    Friend WithEvents ButtonNewLvl As System.Windows.Forms.Button
    Friend WithEvents ButtonAddToLvl As System.Windows.Forms.Button
    Friend WithEvents LabelPieces As System.Windows.Forms.Label
    Friend WithEvents ListViewPieces As System.Windows.Forms.ListView
    Friend WithEvents ColPieceName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColPieceSlots As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColPieceStatus As System.Windows.Forms.ColumnHeader
    Friend WithEvents ButtonRemovePiece As System.Windows.Forms.Button
    Friend WithEvents PiecesButtonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents EdidRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelEdidPrefix As System.Windows.Forms.Label
    Friend WithEvents TextBoxEdid As System.Windows.Forms.TextBox
    Friend WithEvents ModeRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonNewOutfit As System.Windows.Forms.Button
    Friend WithEvents ButtonOverrideOutfit As System.Windows.Forms.Button
    Friend WithEvents LabelCreateBanner As System.Windows.Forms.Label
    Friend WithEvents PreviewModeRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents RadioButtonRenderOutfit As System.Windows.Forms.RadioButton
    Friend WithEvents RadioButtonRenderPiece As System.Windows.Forms.RadioButton
    Friend WithEvents LabelCreateStatus As System.Windows.Forms.Label
    Friend WithEvents PreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents Plugin As ColumnHeader
End Class
