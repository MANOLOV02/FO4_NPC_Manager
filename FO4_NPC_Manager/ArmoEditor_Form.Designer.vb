' UI built in Designer per feedback_ui_in_designer.md (companion to ArmaEditor_Form).
' InitializeComponent is declarative ONLY (no For/If/lambda). The many repeated slot checkboxes are
' declared via their CONTAINER (FlowSlots) here and built in code-behind; the Addons DataGridView's
' columns are added in code-behind too (variable/repeated content).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ArmoEditor_Form
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
        TopBar = New FlowLayoutPanel()
        ButtonNewBlank = New Button()
        ButtonNewFromTemplate = New Button()
        ButtonOverrideExisting = New Button()
        ButtonEditDraft = New Button()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelStatusBanner = New Label()
        MainSplit = New SplitContainer()
        Tabs = New TabControl()
        TabGeneral = New TabPage()
        GeneralLayout = New TableLayoutPanel()
        GroupIdentity = New GroupBox()
        IdentityLayout = New TableLayoutPanel()
        LabelFull = New Label()
        TextBoxFull = New TextBox()
        LabelRace = New Label()
        TextBoxRace = New TextBox()
        ButtonPickRace = New Button()
        GroupData = New GroupBox()
        DataLayout = New TableLayoutPanel()
        LabelValue = New Label()
        NumValue = New NumericUpDown()
        LabelWeight = New Label()
        NumWeight = New NumericUpDown()
        LabelHealth = New Label()
        NumHealth = New NumericUpDown()
        LabelArmorRating = New Label()
        NumArmorRating = New NumericUpDown()
        LabelSlots = New Label()
        FlowSlots = New FlowLayoutPanel()
        TabAddons = New TabPage()
        AddonsLayout = New TableLayoutPanel()
        LabelAddons = New Label()
        GridAddons = New DataGridView()
        AddonsButtons = New FlowLayoutPanel()
        ButtonAddArma = New Button()
        ButtonEditIndx = New Button()
        ButtonRemoveAddon = New Button()
        ButtonAddonUp = New Button()
        ButtonAddonDown = New Button()
        LabelAddonsHint = New Label()
        TabKeywords = New TabPage()
        KeywordsLayout = New TableLayoutPanel()
        LabelKwda = New Label()
        ListKwda = New ListView()
        ColKwda = New ColumnHeader()
        KwdaButtons = New FlowLayoutPanel()
        ButtonAddKwda = New Button()
        ButtonRemoveKwda = New Button()
        LabelAppr = New Label()
        ListAppr = New ListView()
        ColAppr = New ColumnHeader()
        ApprButtons = New FlowLayoutPanel()
        ButtonAddAppr = New Button()
        ButtonRemoveAppr = New Button()
        TabWorld = New TabPage()
        WorldLayout = New TableLayoutPanel()
        GroupWorld = New GroupBox()
        WorldFieldsLayout = New TableLayoutPanel()
        LabelMod2 = New Label()
        TextBoxMod2 = New TextBox()
        ButtonBrowseMod2 = New Button()
        LabelMod4 = New Label()
        TextBoxMod4 = New TextBox()
        ButtonBrowseMod4 = New Button()
        LabelMo2s = New Label()
        TextBoxMo2s = New TextBox()
        ButtonPickMo2s = New Button()
        ButtonEditMo2s = New Button()
        LabelMo4s = New Label()
        TextBoxMo4s = New TextBox()
        ButtonPickMo4s = New Button()
        ButtonEditMo4s = New Button()
        PreviewLayout = New TableLayoutPanel()
        PreviewControlPanel = New Panel()
        LabelPreviewHint = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        TopBar.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.Panel1.SuspendLayout()
        MainSplit.Panel2.SuspendLayout()
        MainSplit.SuspendLayout()
        Tabs.SuspendLayout()
        TabGeneral.SuspendLayout()
        GeneralLayout.SuspendLayout()
        GroupIdentity.SuspendLayout()
        IdentityLayout.SuspendLayout()
        GroupData.SuspendLayout()
        DataLayout.SuspendLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumWeight, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumHealth, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmorRating, ComponentModel.ISupportInitialize).BeginInit()
        TabAddons.SuspendLayout()
        AddonsLayout.SuspendLayout()
        CType(GridAddons, ComponentModel.ISupportInitialize).BeginInit()
        AddonsButtons.SuspendLayout()
        TabKeywords.SuspendLayout()
        KeywordsLayout.SuspendLayout()
        KwdaButtons.SuspendLayout()
        ApprButtons.SuspendLayout()
        TabWorld.SuspendLayout()
        WorldLayout.SuspendLayout()
        GroupWorld.SuspendLayout()
        WorldFieldsLayout.SuspendLayout()
        PreviewLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopBar, 0, 0)
        RootLayout.Controls.Add(LabelStatusBanner, 0, 1)
        RootLayout.Controls.Add(MainSplit, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(1244, 640)
        RootLayout.TabIndex = 0
        '
        ' TopBar
        '
        TopBar.AutoSize = True
        TopBar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TopBar.Controls.Add(ButtonNewBlank)
        TopBar.Controls.Add(ButtonNewFromTemplate)
        TopBar.Controls.Add(ButtonOverrideExisting)
        TopBar.Controls.Add(ButtonEditDraft)
        TopBar.Controls.Add(LabelEdid)
        TopBar.Controls.Add(TextBoxEdid)
        TopBar.Dock = DockStyle.Fill
        TopBar.Location = New Point(11, 11)
        TopBar.Margin = New Padding(0)
        TopBar.Name = "TopBar"
        TopBar.Size = New Size(1222, 30)
        TopBar.TabIndex = 0
        TopBar.WrapContents = False
        '
        ' ButtonNewBlank
        '
        ButtonNewBlank.AutoSize = True
        ButtonNewBlank.Location = New Point(3, 3)
        ButtonNewBlank.Name = "ButtonNewBlank"
        ButtonNewBlank.Size = New Size(90, 24)
        ButtonNewBlank.TabIndex = 0
        ButtonNewBlank.Text = "New (blank)"
        ButtonNewBlank.UseVisualStyleBackColor = True
        '
        ' ButtonNewFromTemplate
        '
        ButtonNewFromTemplate.AutoSize = True
        ButtonNewFromTemplate.Location = New Point(99, 3)
        ButtonNewFromTemplate.Name = "ButtonNewFromTemplate"
        ButtonNewFromTemplate.Size = New Size(140, 24)
        ButtonNewFromTemplate.TabIndex = 1
        ButtonNewFromTemplate.Text = "New from template…"
        ButtonNewFromTemplate.UseVisualStyleBackColor = True
        '
        ' ButtonOverrideExisting
        '
        ButtonOverrideExisting.AutoSize = True
        ButtonOverrideExisting.Location = New Point(245, 3)
        ButtonOverrideExisting.Name = "ButtonOverrideExisting"
        ButtonOverrideExisting.Size = New Size(130, 24)
        ButtonOverrideExisting.TabIndex = 2
        ButtonOverrideExisting.Text = "Override existing…"
        ButtonOverrideExisting.UseVisualStyleBackColor = True
        '
        ' ButtonEditDraft
        '
        ButtonEditDraft.AutoSize = True
        ButtonEditDraft.Location = New Point(381, 3)
        ButtonEditDraft.Name = "ButtonEditDraft"
        ButtonEditDraft.Size = New Size(90, 24)
        ButtonEditDraft.TabIndex = 3
        ButtonEditDraft.Text = "Edit draft…"
        ButtonEditDraft.UseVisualStyleBackColor = True
        '
        ' LabelEdid
        '
        LabelEdid.Anchor = AnchorStyles.Left
        LabelEdid.AutoSize = True
        LabelEdid.Location = New Point(448, 7)
        LabelEdid.Name = "LabelEdid"
        LabelEdid.Size = New Size(48, 15)
        LabelEdid.TabIndex = 4
        LabelEdid.Text = "EditorID:"
        '
        ' TextBoxEdid
        '
        TextBoxEdid.Location = New Point(502, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.PlaceholderText = "npcm_ARMO_<name>"
        TextBoxEdid.Size = New Size(320, 23)
        TextBoxEdid.TabIndex = 5
        '
        ' LabelStatusBanner
        '
        LabelStatusBanner.AutoSize = True
        LabelStatusBanner.Dock = DockStyle.Fill
        LabelStatusBanner.Font = New Font("Segoe UI", 9.75F, FontStyle.Bold)
        LabelStatusBanner.Location = New Point(11, 44)
        LabelStatusBanner.Margin = New Padding(0, 4, 0, 6)
        LabelStatusBanner.Name = "LabelStatusBanner"
        LabelStatusBanner.Size = New Size(1222, 19)
        LabelStatusBanner.TabIndex = 1
        LabelStatusBanner.Text = "NEW record"
        '
        ' MainSplit
        '
        MainSplit.Dock = DockStyle.Fill
        MainSplit.Location = New Point(11, 44)
        MainSplit.Name = "MainSplit"
        '
        ' MainSplit.Panel1
        '
        MainSplit.Panel1.Controls.Add(Tabs)
        '
        ' MainSplit.Panel2
        '
        MainSplit.Panel2.Controls.Add(PreviewLayout)
        MainSplit.Size = New Size(1222, 550)
        MainSplit.SplitterDistance = 720
        MainSplit.TabIndex = 1
        '
        ' Tabs
        '
        Tabs.Controls.Add(TabGeneral)
        Tabs.Controls.Add(TabAddons)
        Tabs.Controls.Add(TabKeywords)
        Tabs.Controls.Add(TabWorld)
        Tabs.Dock = DockStyle.Fill
        Tabs.Location = New Point(0, 0)
        Tabs.Name = "Tabs"
        Tabs.SelectedIndex = 0
        Tabs.Size = New Size(720, 550)
        Tabs.TabIndex = 0
        '
        ' TabGeneral
        '
        TabGeneral.AutoScroll = True
        TabGeneral.Controls.Add(GeneralLayout)
        TabGeneral.Location = New Point(4, 24)
        TabGeneral.Name = "TabGeneral"
        TabGeneral.Padding = New Padding(6)
        TabGeneral.Size = New Size(712, 522)
        TabGeneral.TabIndex = 0
        TabGeneral.Text = "General"
        TabGeneral.UseVisualStyleBackColor = True
        '
        ' GeneralLayout
        '
        GeneralLayout.ColumnCount = 1
        GeneralLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GeneralLayout.Controls.Add(GroupIdentity, 0, 0)
        GeneralLayout.Controls.Add(GroupData, 0, 1)
        GeneralLayout.Controls.Add(LabelSlots, 0, 2)
        GeneralLayout.Controls.Add(FlowSlots, 0, 3)
        GeneralLayout.Dock = DockStyle.Fill
        GeneralLayout.Location = New Point(6, 6)
        GeneralLayout.Name = "GeneralLayout"
        GeneralLayout.RowCount = 4
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        GeneralLayout.Size = New Size(700, 510)
        GeneralLayout.TabIndex = 0
        '
        ' GroupIdentity
        '
        GroupIdentity.AutoSize = True
        GroupIdentity.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupIdentity.Controls.Add(IdentityLayout)
        GroupIdentity.Dock = DockStyle.Fill
        GroupIdentity.Location = New Point(3, 3)
        GroupIdentity.Name = "GroupIdentity"
        GroupIdentity.Padding = New Padding(4)
        GroupIdentity.Size = New Size(694, 90)
        GroupIdentity.TabIndex = 0
        GroupIdentity.TabStop = False
        GroupIdentity.Text = "Identity (FULL name + RNAM race)"
        '
        ' IdentityLayout
        '
        IdentityLayout.ColumnCount = 3
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 50F))
        IdentityLayout.Controls.Add(LabelFull, 0, 0)
        IdentityLayout.Controls.Add(TextBoxFull, 1, 0)
        IdentityLayout.Controls.Add(LabelRace, 0, 1)
        IdentityLayout.Controls.Add(TextBoxRace, 1, 1)
        IdentityLayout.Controls.Add(ButtonPickRace, 2, 1)
        IdentityLayout.AutoSize = True
        IdentityLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        IdentityLayout.Dock = DockStyle.Fill
        IdentityLayout.Location = New Point(4, 20)
        IdentityLayout.Name = "IdentityLayout"
        IdentityLayout.RowCount = 2
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.Size = New Size(686, 66)
        IdentityLayout.TabIndex = 0
        '
        ' LabelFull
        '
        LabelFull.Anchor = AnchorStyles.Left
        LabelFull.AutoSize = True
        LabelFull.Location = New Point(3, 8)
        LabelFull.Name = "LabelFull"
        LabelFull.Size = New Size(85, 15)
        LabelFull.TabIndex = 0
        LabelFull.Text = "Name (FULL):"
        '
        ' TextBoxFull
        '
        TextBoxFull.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFull.Location = New Point(113, 4)
        TextBoxFull.Name = "TextBoxFull"
        TextBoxFull.PlaceholderText = "Display name (optional)"
        TextBoxFull.Size = New Size(544, 23)
        TextBoxFull.TabIndex = 1
        '
        ' LabelRace
        '
        LabelRace.Anchor = AnchorStyles.Left
        LabelRace.AutoSize = True
        LabelRace.Location = New Point(3, 39)
        LabelRace.Name = "LabelRace"
        LabelRace.Size = New Size(80, 15)
        LabelRace.TabIndex = 2
        LabelRace.Text = "Race (RNAM):"
        '
        ' TextBoxRace
        '
        TextBoxRace.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxRace.Location = New Point(113, 35)
        TextBoxRace.Name = "TextBoxRace"
        TextBoxRace.ReadOnly = True
        TextBoxRace.Size = New Size(544, 23)
        TextBoxRace.TabIndex = 3
        '
        ' ButtonPickRace
        '
        ButtonPickRace.Anchor = AnchorStyles.Left
        ButtonPickRace.Location = New Point(663, 34)
        ButtonPickRace.Name = "ButtonPickRace"
        ButtonPickRace.Size = New Size(34, 24)
        ButtonPickRace.TabIndex = 4
        ButtonPickRace.Text = "…"
        ButtonPickRace.UseVisualStyleBackColor = True
        '
        ' GroupData
        '
        GroupData.Controls.Add(DataLayout)
        GroupData.Dock = DockStyle.Top
        GroupData.Location = New Point(3, 64)
        GroupData.Name = "GroupData"
        GroupData.Size = New Size(694, 90)
        GroupData.TabIndex = 5
        GroupData.TabStop = False
        GroupData.Text = "DATA / FNAM"
        '
        ' DataLayout
        '
        DataLayout.ColumnCount = 8
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        DataLayout.Controls.Add(LabelValue, 0, 0)
        DataLayout.Controls.Add(NumValue, 1, 0)
        DataLayout.Controls.Add(LabelWeight, 2, 0)
        DataLayout.Controls.Add(NumWeight, 3, 0)
        DataLayout.Controls.Add(LabelHealth, 4, 0)
        DataLayout.Controls.Add(NumHealth, 5, 0)
        DataLayout.Controls.Add(LabelArmorRating, 6, 0)
        DataLayout.Controls.Add(NumArmorRating, 7, 0)
        DataLayout.Dock = DockStyle.Top
        DataLayout.Location = New Point(3, 19)
        DataLayout.Name = "DataLayout"
        DataLayout.RowCount = 1
        DataLayout.RowStyles.Add(New RowStyle())
        DataLayout.Size = New Size(688, 35)
        DataLayout.TabIndex = 0
        '
        ' LabelValue
        '
        LabelValue.Anchor = AnchorStyles.Left
        LabelValue.AutoSize = True
        LabelValue.Location = New Point(3, 8)
        LabelValue.Name = "LabelValue"
        LabelValue.Size = New Size(70, 15)
        LabelValue.TabIndex = 0
        LabelValue.Text = "Value (s32):"
        '
        ' NumValue
        '
        NumValue.Anchor = AnchorStyles.Left
        NumValue.Location = New Point(93, 6)
        NumValue.Maximum = New Decimal(Integer.MaxValue)
        NumValue.Minimum = New Decimal(Integer.MinValue)
        NumValue.Name = "NumValue"
        NumValue.Size = New Size(80, 23)
        NumValue.TabIndex = 1
        '
        ' LabelWeight
        '
        LabelWeight.Anchor = AnchorStyles.Left
        LabelWeight.AutoSize = True
        LabelWeight.Location = New Point(183, 8)
        LabelWeight.Name = "LabelWeight"
        LabelWeight.Size = New Size(50, 15)
        LabelWeight.TabIndex = 2
        LabelWeight.Text = "Weight:"
        '
        ' NumWeight
        '
        NumWeight.Anchor = AnchorStyles.Left
        NumWeight.DecimalPlaces = 2
        NumWeight.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        NumWeight.Location = New Point(253, 6)
        NumWeight.Maximum = New Decimal(New Integer() {1000000, 0, 0, 0})
        NumWeight.Name = "NumWeight"
        NumWeight.Size = New Size(70, 23)
        NumWeight.TabIndex = 3
        '
        ' LabelHealth
        '
        LabelHealth.Anchor = AnchorStyles.Left
        LabelHealth.AutoSize = True
        LabelHealth.Location = New Point(333, 8)
        LabelHealth.Name = "LabelHealth"
        LabelHealth.Size = New Size(50, 15)
        LabelHealth.TabIndex = 4
        LabelHealth.Text = "Health:"
        '
        ' NumHealth
        '
        NumHealth.Anchor = AnchorStyles.Left
        NumHealth.Location = New Point(423, 6)
        NumHealth.Maximum = New Decimal(New Integer() {-1, 0, 0, 0})
        NumHealth.Name = "NumHealth"
        NumHealth.Size = New Size(80, 23)
        NumHealth.TabIndex = 5
        '
        ' LabelArmorRating
        '
        LabelArmorRating.Anchor = AnchorStyles.Left
        LabelArmorRating.AutoSize = True
        LabelArmorRating.Location = New Point(513, 8)
        LabelArmorRating.Name = "LabelArmorRating"
        LabelArmorRating.Size = New Size(110, 15)
        LabelArmorRating.TabIndex = 6
        LabelArmorRating.Text = "Armor Rating (FNAM):"
        '
        ' NumArmorRating
        '
        NumArmorRating.Anchor = AnchorStyles.Left
        NumArmorRating.Location = New Point(623, 6)
        NumArmorRating.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumArmorRating.Name = "NumArmorRating"
        NumArmorRating.Size = New Size(60, 23)
        NumArmorRating.TabIndex = 7
        '
        ' LabelSlots
        '
        LabelSlots.Anchor = AnchorStyles.Left
        LabelSlots.AutoSize = True
        LabelSlots.Location = New Point(3, 160)
        LabelSlots.Name = "LabelSlots"
        LabelSlots.Size = New Size(280, 15)
        LabelSlots.TabIndex = 6
        LabelSlots.Text = "Biped slots (BOD2) — check the slots this armor occupies:"
        '
        ' FlowSlots
        '
        FlowSlots.AutoScroll = True
        FlowSlots.Dock = DockStyle.Fill
        FlowSlots.Location = New Point(3, 178)
        FlowSlots.Name = "FlowSlots"
        FlowSlots.Size = New Size(694, 329)
        FlowSlots.TabIndex = 7
        '
        ' TabAddons
        '
        TabAddons.Controls.Add(AddonsLayout)
        TabAddons.Location = New Point(4, 24)
        TabAddons.Name = "TabAddons"
        TabAddons.Padding = New Padding(6)
        TabAddons.Size = New Size(712, 522)
        TabAddons.TabIndex = 1
        TabAddons.Text = "Addons"
        TabAddons.UseVisualStyleBackColor = True
        '
        ' AddonsLayout
        '
        AddonsLayout.ColumnCount = 2
        AddonsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        AddonsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120F))
        AddonsLayout.Controls.Add(LabelAddons, 0, 0)
        AddonsLayout.Controls.Add(GridAddons, 0, 1)
        AddonsLayout.Controls.Add(AddonsButtons, 1, 1)
        AddonsLayout.Controls.Add(LabelAddonsHint, 0, 2)
        AddonsLayout.Dock = DockStyle.Fill
        AddonsLayout.Location = New Point(6, 6)
        AddonsLayout.Name = "AddonsLayout"
        AddonsLayout.RowCount = 3
        AddonsLayout.RowStyles.Add(New RowStyle())
        AddonsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        AddonsLayout.RowStyles.Add(New RowStyle())
        AddonsLayout.Size = New Size(700, 510)
        AddonsLayout.TabIndex = 0
        '
        ' LabelAddons
        '
        LabelAddons.AutoSize = True
        LabelAddons.Location = New Point(3, 0)
        LabelAddons.Name = "LabelAddons"
        LabelAddons.Size = New Size(280, 15)
        LabelAddons.TabIndex = 0
        LabelAddons.Text = "Armor Addons (Models) — INDX + ARMA, order matters:"
        '
        ' GridAddons
        '
        GridAddons.AllowUserToAddRows = False
        GridAddons.AllowUserToDeleteRows = False
        GridAddons.AllowUserToResizeRows = False
        GridAddons.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridAddons.Dock = DockStyle.Fill
        GridAddons.MultiSelect = False
        GridAddons.Name = "GridAddons"
        GridAddons.RowHeadersWidth = 25
        GridAddons.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridAddons.Location = New Point(3, 18)
        GridAddons.Size = New Size(574, 471)
        GridAddons.TabIndex = 1
        '
        ' AddonsButtons
        '
        AddonsButtons.Controls.Add(ButtonAddArma)
        AddonsButtons.Controls.Add(ButtonEditIndx)
        AddonsButtons.Controls.Add(ButtonRemoveAddon)
        AddonsButtons.Controls.Add(ButtonAddonUp)
        AddonsButtons.Controls.Add(ButtonAddonDown)
        AddonsButtons.Dock = DockStyle.Fill
        AddonsButtons.FlowDirection = FlowDirection.TopDown
        AddonsButtons.Location = New Point(580, 18)
        AddonsButtons.Margin = New Padding(0)
        AddonsButtons.Name = "AddonsButtons"
        AddonsButtons.Size = New Size(120, 471)
        AddonsButtons.TabIndex = 2
        '
        ' ButtonAddArma
        '
        ButtonAddArma.Location = New Point(3, 3)
        ButtonAddArma.Name = "ButtonAddArma"
        ButtonAddArma.Size = New Size(110, 26)
        ButtonAddArma.TabIndex = 0
        ButtonAddArma.Text = "Add ARMA…"
        ButtonAddArma.UseVisualStyleBackColor = True
        '
        ' ButtonEditIndx
        '
        ButtonEditIndx.Location = New Point(3, 35)
        ButtonEditIndx.Name = "ButtonEditIndx"
        ButtonEditIndx.Size = New Size(110, 26)
        ButtonEditIndx.TabIndex = 1
        ButtonEditIndx.Text = "Edit INDX…"
        ButtonEditIndx.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveAddon
        '
        ButtonRemoveAddon.Location = New Point(3, 67)
        ButtonRemoveAddon.Name = "ButtonRemoveAddon"
        ButtonRemoveAddon.Size = New Size(110, 26)
        ButtonRemoveAddon.TabIndex = 2
        ButtonRemoveAddon.Text = "Remove"
        ButtonRemoveAddon.UseVisualStyleBackColor = True
        '
        ' ButtonAddonUp
        '
        ButtonAddonUp.Location = New Point(3, 99)
        ButtonAddonUp.Name = "ButtonAddonUp"
        ButtonAddonUp.Size = New Size(110, 26)
        ButtonAddonUp.TabIndex = 3
        ButtonAddonUp.Text = "Move Up"
        ButtonAddonUp.UseVisualStyleBackColor = True
        '
        ' ButtonAddonDown
        '
        ButtonAddonDown.Location = New Point(3, 131)
        ButtonAddonDown.Name = "ButtonAddonDown"
        ButtonAddonDown.Size = New Size(110, 26)
        ButtonAddonDown.TabIndex = 4
        ButtonAddonDown.Text = "Move Down"
        ButtonAddonDown.UseVisualStyleBackColor = True
        '
        ' LabelAddonsHint
        '
        LabelAddonsHint.AutoSize = True
        LabelAddonsHint.ForeColor = Color.DimGray
        LabelAddonsHint.Location = New Point(3, 492)
        LabelAddonsHint.Name = "LabelAddonsHint"
        LabelAddonsHint.Size = New Size(360, 15)
        LabelAddonsHint.TabIndex = 3
        LabelAddonsHint.Text = "Double-click a row to open the ARMA Editor for that addon."
        '
        ' TabKeywords
        '
        TabKeywords.Controls.Add(KeywordsLayout)
        TabKeywords.Location = New Point(4, 24)
        TabKeywords.Name = "TabKeywords"
        TabKeywords.Padding = New Padding(6)
        TabKeywords.Size = New Size(712, 522)
        TabKeywords.TabIndex = 2
        TabKeywords.Text = "Keywords"
        TabKeywords.UseVisualStyleBackColor = True
        '
        ' KeywordsLayout
        '
        KeywordsLayout.ColumnCount = 2
        KeywordsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        KeywordsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        KeywordsLayout.Controls.Add(LabelKwda, 0, 0)
        KeywordsLayout.Controls.Add(ListKwda, 0, 1)
        KeywordsLayout.Controls.Add(KwdaButtons, 1, 1)
        KeywordsLayout.Controls.Add(LabelAppr, 0, 2)
        KeywordsLayout.Controls.Add(ListAppr, 0, 3)
        KeywordsLayout.Controls.Add(ApprButtons, 1, 3)
        KeywordsLayout.Dock = DockStyle.Fill
        KeywordsLayout.Location = New Point(6, 6)
        KeywordsLayout.Name = "KeywordsLayout"
        KeywordsLayout.RowCount = 4
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        KeywordsLayout.Size = New Size(700, 510)
        KeywordsLayout.TabIndex = 0
        '
        ' LabelKwda
        '
        LabelKwda.AutoSize = True
        LabelKwda.Location = New Point(3, 0)
        LabelKwda.Name = "LabelKwda"
        LabelKwda.Size = New Size(180, 15)
        LabelKwda.TabIndex = 0
        LabelKwda.Text = "Keywords (KWDA) — KYWD:"
        '
        ' ListKwda
        '
        ListKwda.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        ListKwda.Columns.AddRange(New ColumnHeader() {ColKwda})
        ListKwda.FullRowSelect = True
        ListKwda.Location = New Point(3, 18)
        ListKwda.MultiSelect = False
        ListKwda.Name = "ListKwda"
        ListKwda.Size = New Size(604, 220)
        ListKwda.TabIndex = 1
        ListKwda.UseCompatibleStateImageBehavior = False
        ListKwda.View = View.Details
        '
        ' ColKwda
        '
        ColKwda.Text = "Keyword"
        ColKwda.Width = 580
        '
        ' KwdaButtons
        '
        KwdaButtons.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        KwdaButtons.AutoSize = True
        KwdaButtons.Controls.Add(ButtonAddKwda)
        KwdaButtons.Controls.Add(ButtonRemoveKwda)
        KwdaButtons.FlowDirection = FlowDirection.TopDown
        KwdaButtons.Location = New Point(613, 21)
        KwdaButtons.Name = "KwdaButtons"
        KwdaButtons.Size = New Size(84, 62)
        KwdaButtons.TabIndex = 2
        '
        ' ButtonAddKwda
        '
        ButtonAddKwda.Location = New Point(3, 3)
        ButtonAddKwda.Name = "ButtonAddKwda"
        ButtonAddKwda.Size = New Size(78, 24)
        ButtonAddKwda.TabIndex = 0
        ButtonAddKwda.Text = "Add…"
        ButtonAddKwda.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveKwda
        '
        ButtonRemoveKwda.Location = New Point(3, 33)
        ButtonRemoveKwda.Name = "ButtonRemoveKwda"
        ButtonRemoveKwda.Size = New Size(78, 24)
        ButtonRemoveKwda.TabIndex = 1
        ButtonRemoveKwda.Text = "Remove"
        ButtonRemoveKwda.UseVisualStyleBackColor = True
        '
        ' LabelAppr
        '
        LabelAppr.AutoSize = True
        LabelAppr.Location = New Point(3, 256)
        LabelAppr.Name = "LabelAppr"
        LabelAppr.Size = New Size(280, 15)
        LabelAppr.TabIndex = 3
        LabelAppr.Text = "Attach Parent Slots (APPR) — KYWD:"
        '
        ' ListAppr
        '
        ListAppr.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        ListAppr.Columns.AddRange(New ColumnHeader() {ColAppr})
        ListAppr.FullRowSelect = True
        ListAppr.Location = New Point(3, 274)
        ListAppr.MultiSelect = False
        ListAppr.Name = "ListAppr"
        ListAppr.Size = New Size(604, 233)
        ListAppr.TabIndex = 4
        ListAppr.UseCompatibleStateImageBehavior = False
        ListAppr.View = View.Details
        '
        ' ColAppr
        '
        ColAppr.Text = "Attach parent slot keyword"
        ColAppr.Width = 580
        '
        ' ApprButtons
        '
        ApprButtons.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        ApprButtons.AutoSize = True
        ApprButtons.Controls.Add(ButtonAddAppr)
        ApprButtons.Controls.Add(ButtonRemoveAppr)
        ApprButtons.FlowDirection = FlowDirection.TopDown
        ApprButtons.Location = New Point(613, 277)
        ApprButtons.Name = "ApprButtons"
        ApprButtons.Size = New Size(84, 62)
        ApprButtons.TabIndex = 5
        '
        ' ButtonAddAppr
        '
        ButtonAddAppr.Location = New Point(3, 3)
        ButtonAddAppr.Name = "ButtonAddAppr"
        ButtonAddAppr.Size = New Size(78, 24)
        ButtonAddAppr.TabIndex = 0
        ButtonAddAppr.Text = "Add…"
        ButtonAddAppr.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveAppr
        '
        ButtonRemoveAppr.Location = New Point(3, 33)
        ButtonRemoveAppr.Name = "ButtonRemoveAppr"
        ButtonRemoveAppr.Size = New Size(78, 24)
        ButtonRemoveAppr.TabIndex = 1
        ButtonRemoveAppr.Text = "Remove"
        ButtonRemoveAppr.UseVisualStyleBackColor = True
        '
        ' TabWorld
        '
        TabWorld.AutoScroll = True
        TabWorld.Controls.Add(WorldLayout)
        TabWorld.Location = New Point(4, 24)
        TabWorld.Name = "TabWorld"
        TabWorld.Padding = New Padding(6)
        TabWorld.Size = New Size(712, 522)
        TabWorld.TabIndex = 3
        TabWorld.Text = "World Model & Material"
        TabWorld.UseVisualStyleBackColor = True
        '
        ' WorldLayout
        '
        WorldLayout.ColumnCount = 1
        WorldLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        WorldLayout.Controls.Add(GroupWorld, 0, 0)
        WorldLayout.Dock = DockStyle.Fill
        WorldLayout.Location = New Point(6, 6)
        WorldLayout.Name = "WorldLayout"
        WorldLayout.RowCount = 2
        WorldLayout.RowStyles.Add(New RowStyle())
        WorldLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        WorldLayout.Size = New Size(700, 510)
        WorldLayout.TabIndex = 0
        '
        ' GroupWorld
        '
        GroupWorld.AutoSize = True
        GroupWorld.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupWorld.Controls.Add(WorldFieldsLayout)
        GroupWorld.Dock = DockStyle.Fill
        GroupWorld.Location = New Point(3, 3)
        GroupWorld.Name = "GroupWorld"
        GroupWorld.Padding = New Padding(4)
        GroupWorld.Size = New Size(694, 130)
        GroupWorld.TabIndex = 0
        GroupWorld.TabStop = False
        GroupWorld.Text = "World models & material swaps (MOD2 male / MOD4 female)"
        '
        ' WorldFieldsLayout
        '
        WorldFieldsLayout.ColumnCount = 4
        WorldFieldsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        WorldFieldsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        WorldFieldsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        WorldFieldsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        WorldFieldsLayout.Controls.Add(LabelMod2, 0, 0)
        WorldFieldsLayout.Controls.Add(TextBoxMod2, 1, 0)
        WorldFieldsLayout.Controls.Add(ButtonBrowseMod2, 2, 0)
        WorldFieldsLayout.Controls.Add(LabelMod4, 0, 1)
        WorldFieldsLayout.Controls.Add(TextBoxMod4, 1, 1)
        WorldFieldsLayout.Controls.Add(ButtonBrowseMod4, 2, 1)
        WorldFieldsLayout.Controls.Add(LabelMo2s, 0, 2)
        WorldFieldsLayout.Controls.Add(TextBoxMo2s, 1, 2)
        WorldFieldsLayout.Controls.Add(ButtonPickMo2s, 2, 2)
        WorldFieldsLayout.Controls.Add(ButtonEditMo2s, 3, 2)
        WorldFieldsLayout.Controls.Add(LabelMo4s, 0, 3)
        WorldFieldsLayout.Controls.Add(TextBoxMo4s, 1, 3)
        WorldFieldsLayout.Controls.Add(ButtonPickMo4s, 2, 3)
        WorldFieldsLayout.Controls.Add(ButtonEditMo4s, 3, 3)
        WorldFieldsLayout.AutoSize = True
        WorldFieldsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        WorldFieldsLayout.Dock = DockStyle.Fill
        WorldFieldsLayout.Location = New Point(4, 20)
        WorldFieldsLayout.Name = "WorldFieldsLayout"
        WorldFieldsLayout.RowCount = 4
        WorldFieldsLayout.RowStyles.Add(New RowStyle())
        WorldFieldsLayout.RowStyles.Add(New RowStyle())
        WorldFieldsLayout.RowStyles.Add(New RowStyle())
        WorldFieldsLayout.RowStyles.Add(New RowStyle())
        WorldFieldsLayout.Size = New Size(686, 106)
        WorldFieldsLayout.TabIndex = 0
        '
        ' LabelMod2
        '
        LabelMod2.Anchor = AnchorStyles.Left
        LabelMod2.AutoSize = True
        LabelMod2.Location = New Point(3, 8)
        LabelMod2.Name = "LabelMod2"
        LabelMod2.Size = New Size(160, 15)
        LabelMod2.TabIndex = 0
        LabelMod2.Text = "Male world model (MOD2):"
        '
        ' TextBoxMod2
        '
        TextBoxMod2.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod2.Location = New Point(178, 4)
        TextBoxMod2.Name = "TextBoxMod2"
        TextBoxMod2.PlaceholderText = "Meshes\... (optional — robots/special armors)"
        TextBoxMod2.Size = New Size(469, 23)
        TextBoxMod2.TabIndex = 1
        '
        ' ButtonBrowseMod2
        '
        ButtonBrowseMod2.Anchor = AnchorStyles.Left
        ButtonBrowseMod2.Location = New Point(653, 3)
        ButtonBrowseMod2.Name = "ButtonBrowseMod2"
        ButtonBrowseMod2.Size = New Size(34, 24)
        ButtonBrowseMod2.TabIndex = 2
        ButtonBrowseMod2.Text = "…"
        ButtonBrowseMod2.UseVisualStyleBackColor = True
        '
        ' LabelMod4
        '
        LabelMod4.Anchor = AnchorStyles.Left
        LabelMod4.AutoSize = True
        LabelMod4.Location = New Point(3, 39)
        LabelMod4.Name = "LabelMod4"
        LabelMod4.Size = New Size(170, 15)
        LabelMod4.TabIndex = 3
        LabelMod4.Text = "Female world model (MOD4):"
        '
        ' TextBoxMod4
        '
        TextBoxMod4.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod4.Location = New Point(178, 35)
        TextBoxMod4.Name = "TextBoxMod4"
        TextBoxMod4.PlaceholderText = "Meshes\... (optional — robots/special armors)"
        TextBoxMod4.Size = New Size(469, 23)
        TextBoxMod4.TabIndex = 4
        '
        ' ButtonBrowseMod4
        '
        ButtonBrowseMod4.Anchor = AnchorStyles.Left
        ButtonBrowseMod4.Location = New Point(653, 34)
        ButtonBrowseMod4.Name = "ButtonBrowseMod4"
        ButtonBrowseMod4.Size = New Size(34, 24)
        ButtonBrowseMod4.TabIndex = 5
        ButtonBrowseMod4.Text = "…"
        ButtonBrowseMod4.UseVisualStyleBackColor = True
        '
        ' LabelMo2s
        '
        LabelMo2s.Anchor = AnchorStyles.Left
        LabelMo2s.AutoSize = True
        LabelMo2s.Location = New Point(3, 70)
        LabelMo2s.Name = "LabelMo2s"
        LabelMo2s.Size = New Size(175, 15)
        LabelMo2s.TabIndex = 6
        LabelMo2s.Text = "Male material swap (MO2S):"
        '
        ' TextBoxMo2s
        '
        TextBoxMo2s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo2s.Location = New Point(178, 66)
        TextBoxMo2s.Name = "TextBoxMo2s"
        TextBoxMo2s.ReadOnly = True
        TextBoxMo2s.Size = New Size(469, 23)
        TextBoxMo2s.TabIndex = 7
        '
        ' ButtonPickMo2s
        '
        ButtonPickMo2s.Anchor = AnchorStyles.Left
        ButtonPickMo2s.Location = New Point(653, 65)
        ButtonPickMo2s.Name = "ButtonPickMo2s"
        ButtonPickMo2s.Size = New Size(34, 24)
        ButtonPickMo2s.TabIndex = 8
        ButtonPickMo2s.Text = "…"
        ButtonPickMo2s.UseVisualStyleBackColor = True
        '
        ' ButtonEditMo2s
        '
        ButtonEditMo2s.Anchor = AnchorStyles.Left
        ButtonEditMo2s.Location = New Point(693, 65)
        ButtonEditMo2s.Name = "ButtonEditMo2s"
        ButtonEditMo2s.Size = New Size(104, 24)
        ButtonEditMo2s.TabIndex = 9
        ButtonEditMo2s.Text = "New / Edit MSWP…"
        ButtonEditMo2s.UseVisualStyleBackColor = True
        '
        ' LabelMo4s
        '
        LabelMo4s.Anchor = AnchorStyles.Left
        LabelMo4s.AutoSize = True
        LabelMo4s.Location = New Point(3, 100)
        LabelMo4s.Name = "LabelMo4s"
        LabelMo4s.Size = New Size(185, 15)
        LabelMo4s.TabIndex = 10
        LabelMo4s.Text = "Female material swap (MO4S):"
        '
        ' TextBoxMo4s
        '
        TextBoxMo4s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo4s.Location = New Point(178, 96)
        TextBoxMo4s.Name = "TextBoxMo4s"
        TextBoxMo4s.ReadOnly = True
        TextBoxMo4s.Size = New Size(469, 23)
        TextBoxMo4s.TabIndex = 11
        '
        ' ButtonPickMo4s
        '
        ButtonPickMo4s.Anchor = AnchorStyles.Left
        ButtonPickMo4s.Location = New Point(653, 95)
        ButtonPickMo4s.Name = "ButtonPickMo4s"
        ButtonPickMo4s.Size = New Size(34, 24)
        ButtonPickMo4s.TabIndex = 12
        ButtonPickMo4s.Text = "…"
        ButtonPickMo4s.UseVisualStyleBackColor = True
        '
        ' ButtonEditMo4s
        '
        ButtonEditMo4s.Anchor = AnchorStyles.Left
        ButtonEditMo4s.Location = New Point(693, 95)
        ButtonEditMo4s.Name = "ButtonEditMo4s"
        ButtonEditMo4s.Size = New Size(104, 24)
        ButtonEditMo4s.TabIndex = 13
        ButtonEditMo4s.Text = "New / Edit MSWP…"
        ButtonEditMo4s.UseVisualStyleBackColor = True
        '
        ' PreviewLayout
        '
        PreviewLayout.ColumnCount = 1
        PreviewLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PreviewLayout.Controls.Add(LabelPreviewHint, 0, 0)
        PreviewLayout.Controls.Add(PreviewControlPanel, 0, 1)
        PreviewLayout.Dock = DockStyle.Fill
        PreviewLayout.Location = New Point(0, 0)
        PreviewLayout.Name = "PreviewLayout"
        PreviewLayout.RowCount = 2
        PreviewLayout.RowStyles.Add(New RowStyle())
        PreviewLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PreviewLayout.Size = New Size(498, 550)
        PreviewLayout.TabIndex = 0
        '
        ' LabelPreviewHint
        '
        LabelPreviewHint.AutoSize = True
        LabelPreviewHint.ForeColor = Color.DimGray
        LabelPreviewHint.Location = New Point(3, 0)
        LabelPreviewHint.Margin = New Padding(3, 0, 3, 4)
        LabelPreviewHint.Name = "LabelPreviewHint"
        LabelPreviewHint.Size = New Size(85, 15)
        LabelPreviewHint.TabIndex = 0
        LabelPreviewHint.Text = "Preview"
        '
        ' PreviewControlPanel
        '
        PreviewControlPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewControlPanel.Dock = DockStyle.Fill
        PreviewControlPanel.Location = New Point(0, 19)
        PreviewControlPanel.Margin = New Padding(0)
        PreviewControlPanel.Name = "PreviewControlPanel"
        PreviewControlPanel.Size = New Size(498, 531)
        PreviewControlPanel.TabIndex = 1
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 597)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(1222, 35)
        BottomLayout.TabIndex = 2
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(1056, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 1
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(1142, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 0
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ArmoEditor_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1244, 640)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ArmoEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "ARMO Editor"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TopBar.ResumeLayout(False)
        TopBar.PerformLayout()
        MainSplit.Panel1.ResumeLayout(False)
        MainSplit.Panel2.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        Tabs.ResumeLayout(False)
        TabGeneral.ResumeLayout(False)
        TabGeneral.PerformLayout()
        GeneralLayout.ResumeLayout(False)
        GeneralLayout.PerformLayout()
        GroupIdentity.ResumeLayout(False)
        IdentityLayout.ResumeLayout(False)
        IdentityLayout.PerformLayout()
        GroupData.ResumeLayout(False)
        DataLayout.ResumeLayout(False)
        DataLayout.PerformLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).EndInit()
        CType(NumWeight, ComponentModel.ISupportInitialize).EndInit()
        CType(NumHealth, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmorRating, ComponentModel.ISupportInitialize).EndInit()
        TabAddons.ResumeLayout(False)
        TabAddons.PerformLayout()
        AddonsLayout.ResumeLayout(False)
        AddonsLayout.PerformLayout()
        CType(GridAddons, ComponentModel.ISupportInitialize).EndInit()
        AddonsButtons.ResumeLayout(False)
        TabKeywords.ResumeLayout(False)
        KeywordsLayout.ResumeLayout(False)
        KeywordsLayout.PerformLayout()
        KwdaButtons.ResumeLayout(False)
        KwdaButtons.PerformLayout()
        ApprButtons.ResumeLayout(False)
        ApprButtons.PerformLayout()
        TabWorld.ResumeLayout(False)
        TabWorld.PerformLayout()
        WorldLayout.ResumeLayout(False)
        WorldLayout.PerformLayout()
        GroupWorld.ResumeLayout(False)
        WorldFieldsLayout.ResumeLayout(False)
        WorldFieldsLayout.PerformLayout()
        PreviewLayout.ResumeLayout(False)
        PreviewLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TopBar As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonNewBlank As System.Windows.Forms.Button
    Friend WithEvents ButtonNewFromTemplate As System.Windows.Forms.Button
    Friend WithEvents ButtonOverrideExisting As System.Windows.Forms.Button
    Friend WithEvents ButtonEditDraft As System.Windows.Forms.Button
    Friend WithEvents LabelEdid As System.Windows.Forms.Label
    Friend WithEvents TextBoxEdid As System.Windows.Forms.TextBox
    Friend WithEvents LabelStatusBanner As System.Windows.Forms.Label
    Friend WithEvents MainSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents Tabs As System.Windows.Forms.TabControl
    Friend WithEvents TabGeneral As System.Windows.Forms.TabPage
    Friend WithEvents GeneralLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupIdentity As System.Windows.Forms.GroupBox
    Friend WithEvents IdentityLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFull As System.Windows.Forms.Label
    Friend WithEvents TextBoxFull As System.Windows.Forms.TextBox
    Friend WithEvents LabelRace As System.Windows.Forms.Label
    Friend WithEvents TextBoxRace As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickRace As System.Windows.Forms.Button
    Friend WithEvents GroupData As System.Windows.Forms.GroupBox
    Friend WithEvents DataLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelValue As System.Windows.Forms.Label
    Friend WithEvents NumValue As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelWeight As System.Windows.Forms.Label
    Friend WithEvents NumWeight As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelHealth As System.Windows.Forms.Label
    Friend WithEvents NumHealth As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmorRating As System.Windows.Forms.Label
    Friend WithEvents NumArmorRating As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSlots As System.Windows.Forms.Label
    Friend WithEvents FlowSlots As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents TabAddons As System.Windows.Forms.TabPage
    Friend WithEvents AddonsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelAddons As System.Windows.Forms.Label
    Friend WithEvents GridAddons As System.Windows.Forms.DataGridView
    Friend WithEvents AddonsButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddArma As System.Windows.Forms.Button
    Friend WithEvents ButtonEditIndx As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveAddon As System.Windows.Forms.Button
    Friend WithEvents ButtonAddonUp As System.Windows.Forms.Button
    Friend WithEvents ButtonAddonDown As System.Windows.Forms.Button
    Friend WithEvents LabelAddonsHint As System.Windows.Forms.Label
    Friend WithEvents TabKeywords As System.Windows.Forms.TabPage
    Friend WithEvents KeywordsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelKwda As System.Windows.Forms.Label
    Friend WithEvents ListKwda As System.Windows.Forms.ListView
    Friend WithEvents ColKwda As System.Windows.Forms.ColumnHeader
    Friend WithEvents KwdaButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddKwda As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveKwda As System.Windows.Forms.Button
    Friend WithEvents LabelAppr As System.Windows.Forms.Label
    Friend WithEvents ListAppr As System.Windows.Forms.ListView
    Friend WithEvents ColAppr As System.Windows.Forms.ColumnHeader
    Friend WithEvents ApprButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddAppr As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveAppr As System.Windows.Forms.Button
    Friend WithEvents TabWorld As System.Windows.Forms.TabPage
    Friend WithEvents WorldLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupWorld As System.Windows.Forms.GroupBox
    Friend WithEvents WorldFieldsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMod2 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod2 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod2 As System.Windows.Forms.Button
    Friend WithEvents LabelMod4 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod4 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod4 As System.Windows.Forms.Button
    Friend WithEvents LabelMo2s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo2s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo2s As System.Windows.Forms.Button
    Friend WithEvents ButtonEditMo2s As System.Windows.Forms.Button
    Friend WithEvents LabelMo4s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo4s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo4s As System.Windows.Forms.Button
    Friend WithEvents ButtonEditMo4s As System.Windows.Forms.Button
    Friend WithEvents PreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelPreviewHint As System.Windows.Forms.Label
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
