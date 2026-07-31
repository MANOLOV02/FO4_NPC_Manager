' UI built in Designer per 00-reglas-ui-y-vb.md (companion to ArmaEditor_Form).
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
        LabelEdidPreview = New Label()
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
        LabelInnr = New Label()
        TextBoxInnr = New TextBox()
        ButtonPickInnr = New Button()
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
        TabSlots = New TabPage()
        SlotsLayout = New TableLayoutPanel()
        ButtonRecalcSlots = New Button()
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
        TabObts = New TabPage()
        ObtsLayout = New TableLayoutPanel()
        LabelObts = New Label()
        GridCombinations = New DataGridView()
        ObtsButtons = New FlowLayoutPanel()
        ButtonAddCombo = New Button()
        ButtonRemoveCombo = New Button()
        ButtonDuplicateCombo = New Button()
        ButtonComboUp = New Button()
        ButtonComboDown = New Button()
        ButtonEditCombo = New Button()
        LabelObtsHint = New Label()
        PreviewLayout = New TableLayoutPanel()
        PreviewControlPanel = New Panel()
        LabelPreviewHint = New Label()
        PreviewModePanel = New FlowLayoutPanel()
        RadioOnlyArmor = New RadioButton()
        RadioFullOutfit = New RadioButton()
        CheckIncludeBody = New CheckBox()
        CheckShowOtherGender = New CheckBox()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        LabelEitm = New Label()
        TextBoxEitm = New TextBox()
        ButtonPickEitm = New Button()
        LabelPtrn = New Label()
        TextBoxPtrn = New TextBox()
        ButtonPickPtrn = New Button()
        CheckBoxNonPlayable = New CheckBox()
        LabelDesc = New Label()
        TextBoxDesc = New TextBox()
        LabelBaseAddonIndex = New Label()
        NumBaseAddonIndex = New NumericUpDown()
        LabelStaggerRating = New Label()
        NumStaggerRating = New NumericUpDown()
        TabMisc = New TabPage()
        MiscLayout = New TableLayoutPanel()
        LabelYnam = New Label()
        TextBoxYnam = New TextBox()
        ButtonPickYnam = New Button()
        LabelZnam = New Label()
        TextBoxZnam = New TextBox()
        ButtonPickZnam = New Button()
        LabelEtyp = New Label()
        TextBoxEtyp = New TextBox()
        ButtonPickEtyp = New Button()
        LabelBamt = New Label()
        TextBoxBamt = New TextBox()
        ButtonPickBamt = New Button()
        LabelObnd = New Label()
        FlowObnd = New FlowLayoutPanel()
        NumObndX1 = New NumericUpDown()
        NumObndY1 = New NumericUpDown()
        NumObndZ1 = New NumericUpDown()
        NumObndX2 = New NumericUpDown()
        NumObndY2 = New NumericUpDown()
        NumObndZ2 = New NumericUpDown()
        ButtonRecomputeObnd = New Button()
        LabelObndHint = New Label()
        TabDamage = New TabPage()
        DamageLayout = New TableLayoutPanel()
        LabelDamage = New Label()
        GridDamage = New DataGridView()
        DamageButtons = New FlowLayoutPanel()
        ButtonAddDamage = New Button()
        ButtonEditDamage = New Button()
        ButtonRemoveDamage = New Button()
        RootLayout.SuspendLayout()
        TopBar.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.Panel1.SuspendLayout()
        MainSplit.Panel2.SuspendLayout()
        MainSplit.SuspendLayout()
        Tabs.SuspendLayout()
        TabGeneral.SuspendLayout()
        GeneralLayout.SuspendLayout()
        TabSlots.SuspendLayout()
        SlotsLayout.SuspendLayout()
        GroupIdentity.SuspendLayout()
        IdentityLayout.SuspendLayout()
        GroupData.SuspendLayout()
        DataLayout.SuspendLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumWeight, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumHealth, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmorRating, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumBaseAddonIndex, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumStaggerRating, ComponentModel.ISupportInitialize).BeginInit()
        TabMisc.SuspendLayout()
        MiscLayout.SuspendLayout()
        FlowObnd.SuspendLayout()
        CType(NumObndX1, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumObndY1, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumObndZ1, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumObndX2, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumObndY2, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumObndZ2, ComponentModel.ISupportInitialize).BeginInit()
        TabDamage.SuspendLayout()
        DamageLayout.SuspendLayout()
        CType(GridDamage, ComponentModel.ISupportInitialize).BeginInit()
        DamageButtons.SuspendLayout()
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
        TabObts.SuspendLayout()
        ObtsLayout.SuspendLayout()
        CType(GridCombinations, ComponentModel.ISupportInitialize).BeginInit()
        ObtsButtons.SuspendLayout()
        PreviewLayout.SuspendLayout()
        PreviewModePanel.SuspendLayout()
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
        TopBar.Controls.Add(LabelEdidPreview)
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
        ButtonEditDraft.Text = "Edit mine…"
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
        TextBoxEdid.PlaceholderText = "name"
        TextBoxEdid.Size = New Size(320, 23)
        TextBoxEdid.TabIndex = 5
        '
        ' LabelEdidPreview
        '
        LabelEdidPreview.Anchor = AnchorStyles.Left
        LabelEdidPreview.AutoSize = True
        LabelEdidPreview.ForeColor = System.Drawing.SystemColors.GrayText
        LabelEdidPreview.Location = New Point(828, 7)
        LabelEdidPreview.Margin = New Padding(8, 0, 3, 0)
        LabelEdidPreview.Name = "LabelEdidPreview"
        LabelEdidPreview.Size = New Size(0, 15)
        LabelEdidPreview.TabIndex = 6
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
        Tabs.Controls.Add(TabSlots)
        Tabs.Controls.Add(TabKeywords)
        Tabs.Controls.Add(TabWorld)
        Tabs.Controls.Add(TabMisc)
        Tabs.Controls.Add(TabDamage)
        Tabs.Controls.Add(TabObts)
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
        GeneralLayout.Dock = DockStyle.Fill
        GeneralLayout.Location = New Point(6, 6)
        GeneralLayout.Name = "GeneralLayout"
        GeneralLayout.RowCount = 3
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
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 50F))
        IdentityLayout.Controls.Add(LabelFull, 0, 0)
        IdentityLayout.Controls.Add(TextBoxFull, 1, 0)
        IdentityLayout.Controls.Add(LabelRace, 0, 1)
        IdentityLayout.Controls.Add(TextBoxRace, 1, 1)
        IdentityLayout.Controls.Add(ButtonPickRace, 2, 1)
        IdentityLayout.Controls.Add(LabelInnr, 0, 2)
        IdentityLayout.Controls.Add(TextBoxInnr, 1, 2)
        IdentityLayout.Controls.Add(ButtonPickInnr, 2, 2)
        IdentityLayout.Controls.Add(LabelEitm, 0, 3)
        IdentityLayout.Controls.Add(TextBoxEitm, 1, 3)
        IdentityLayout.Controls.Add(ButtonPickEitm, 2, 3)
        IdentityLayout.Controls.Add(LabelPtrn, 0, 4)
        IdentityLayout.Controls.Add(TextBoxPtrn, 1, 4)
        IdentityLayout.Controls.Add(ButtonPickPtrn, 2, 4)
        IdentityLayout.Controls.Add(CheckBoxNonPlayable, 1, 5)
        IdentityLayout.Controls.Add(LabelDesc, 0, 6)
        IdentityLayout.Controls.Add(TextBoxDesc, 1, 6)
        IdentityLayout.AutoSize = True
        IdentityLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        IdentityLayout.Dock = DockStyle.Fill
        IdentityLayout.Location = New Point(4, 20)
        IdentityLayout.Name = "IdentityLayout"
        IdentityLayout.RowCount = 7
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.RowStyles.Add(New RowStyle())
        IdentityLayout.RowStyles.Add(New RowStyle())
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
        ' LabelInnr
        '
        LabelInnr.Anchor = AnchorStyles.Left
        LabelInnr.AutoSize = True
        LabelInnr.Location = New Point(3, 70)
        LabelInnr.Name = "LabelInnr"
        LabelInnr.Size = New Size(105, 15)
        LabelInnr.TabIndex = 5
        LabelInnr.Text = "Instance Naming:"
        '
        ' TextBoxInnr
        '
        TextBoxInnr.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxInnr.Location = New Point(113, 66)
        TextBoxInnr.Name = "TextBoxInnr"
        TextBoxInnr.ReadOnly = True
        TextBoxInnr.Size = New Size(544, 23)
        TextBoxInnr.TabIndex = 6
        '
        ' ButtonPickInnr
        '
        ButtonPickInnr.Anchor = AnchorStyles.Left
        ButtonPickInnr.Location = New Point(663, 65)
        ButtonPickInnr.Name = "ButtonPickInnr"
        ButtonPickInnr.Size = New Size(34, 24)
        ButtonPickInnr.TabIndex = 7
        ButtonPickInnr.Text = "…"
        ButtonPickInnr.UseVisualStyleBackColor = True
        '
        ' LabelEitm
        '
        LabelEitm.Anchor = AnchorStyles.Left
        LabelEitm.AutoSize = True
        LabelEitm.Name = "LabelEitm"
        LabelEitm.Text = "Object Effect:"
        '
        ' TextBoxEitm
        '
        TextBoxEitm.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxEitm.Name = "TextBoxEitm"
        TextBoxEitm.ReadOnly = True
        TextBoxEitm.Size = New Size(544, 23)
        TextBoxEitm.TabIndex = 8
        '
        ' ButtonPickEitm
        '
        ButtonPickEitm.Anchor = AnchorStyles.Left
        ButtonPickEitm.Name = "ButtonPickEitm"
        ButtonPickEitm.Size = New Size(34, 24)
        ButtonPickEitm.TabIndex = 9
        ButtonPickEitm.Text = "…"
        ButtonPickEitm.UseVisualStyleBackColor = True
        '
        ' LabelPtrn
        '
        LabelPtrn.Anchor = AnchorStyles.Left
        LabelPtrn.AutoSize = True
        LabelPtrn.Name = "LabelPtrn"
        LabelPtrn.Text = "Transform (PTRN):"
        '
        ' TextBoxPtrn
        '
        TextBoxPtrn.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxPtrn.Name = "TextBoxPtrn"
        TextBoxPtrn.ReadOnly = True
        TextBoxPtrn.Size = New Size(544, 23)
        TextBoxPtrn.TabIndex = 10
        '
        ' ButtonPickPtrn
        '
        ButtonPickPtrn.Anchor = AnchorStyles.Left
        ButtonPickPtrn.Name = "ButtonPickPtrn"
        ButtonPickPtrn.Size = New Size(34, 24)
        ButtonPickPtrn.TabIndex = 11
        ButtonPickPtrn.Text = "…"
        ButtonPickPtrn.UseVisualStyleBackColor = True
        '
        ' CheckBoxNonPlayable
        '
        CheckBoxNonPlayable.Anchor = AnchorStyles.Left
        CheckBoxNonPlayable.AutoSize = True
        CheckBoxNonPlayable.Name = "CheckBoxNonPlayable"
        CheckBoxNonPlayable.TabIndex = 12
        CheckBoxNonPlayable.Text = "Non-Playable"
        CheckBoxNonPlayable.UseVisualStyleBackColor = True
        '
        ' LabelDesc
        '
        LabelDesc.Anchor = AnchorStyles.Left
        LabelDesc.AutoSize = True
        LabelDesc.Name = "LabelDesc"
        LabelDesc.Text = "Description (DESC):"
        '
        ' TextBoxDesc
        '
        TextBoxDesc.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxDesc.Name = "TextBoxDesc"
        TextBoxDesc.Size = New Size(469, 23)
        TextBoxDesc.TabIndex = 13
        '
        ' GroupData
        '
        GroupData.AutoSize = True
        GroupData.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupData.Controls.Add(DataLayout)
        GroupData.Dock = DockStyle.Fill
        GroupData.Location = New Point(3, 64)
        GroupData.Name = "GroupData"
        GroupData.Size = New Size(694, 130)
        GroupData.TabIndex = 5
        GroupData.TabStop = False
        GroupData.Text = "DATA / FNAM"
        '
        ' DataLayout
        '
        DataLayout.ColumnCount = 5
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        DataLayout.Controls.Add(LabelValue, 0, 0)
        DataLayout.Controls.Add(NumValue, 1, 0)
        DataLayout.Controls.Add(LabelWeight, 2, 0)
        DataLayout.Controls.Add(NumWeight, 3, 0)
        DataLayout.Controls.Add(LabelHealth, 0, 1)
        DataLayout.Controls.Add(NumHealth, 1, 1)
        DataLayout.Controls.Add(LabelArmorRating, 2, 1)
        DataLayout.Controls.Add(NumArmorRating, 3, 1)
        DataLayout.Controls.Add(LabelBaseAddonIndex, 0, 2)
        DataLayout.Controls.Add(NumBaseAddonIndex, 1, 2)
        DataLayout.Controls.Add(LabelStaggerRating, 2, 2)
        DataLayout.Controls.Add(NumStaggerRating, 3, 2)
        DataLayout.AutoSize = True
        DataLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        DataLayout.Dock = DockStyle.Fill
        DataLayout.Location = New Point(3, 19)
        DataLayout.Name = "DataLayout"
        DataLayout.RowCount = 3
        DataLayout.RowStyles.Add(New RowStyle())
        DataLayout.RowStyles.Add(New RowStyle())
        DataLayout.RowStyles.Add(New RowStyle())
        DataLayout.Size = New Size(688, 100)
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
        NumValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
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
        NumWeight.Size = New Size(80, 23)
        NumWeight.TabIndex = 3
        NumWeight.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
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
        NumHealth.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
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
        NumArmorRating.Size = New Size(80, 23)
        NumArmorRating.TabIndex = 7
        NumArmorRating.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelBaseAddonIndex
        '
        LabelBaseAddonIndex.Anchor = AnchorStyles.Left
        LabelBaseAddonIndex.AutoSize = True
        LabelBaseAddonIndex.Name = "LabelBaseAddonIndex"
        LabelBaseAddonIndex.Text = "Base Addon Index:"
        '
        ' NumBaseAddonIndex
        '
        NumBaseAddonIndex.Anchor = AnchorStyles.Left
        NumBaseAddonIndex.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumBaseAddonIndex.Name = "NumBaseAddonIndex"
        NumBaseAddonIndex.Size = New Size(80, 23)
        NumBaseAddonIndex.TabIndex = 8
        NumBaseAddonIndex.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelStaggerRating
        '
        LabelStaggerRating.Anchor = AnchorStyles.Left
        LabelStaggerRating.AutoSize = True
        LabelStaggerRating.Name = "LabelStaggerRating"
        LabelStaggerRating.Text = "Stagger Rating:"
        '
        ' NumStaggerRating
        '
        NumStaggerRating.Anchor = AnchorStyles.Left
        NumStaggerRating.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumStaggerRating.Name = "NumStaggerRating"
        NumStaggerRating.Size = New Size(80, 23)
        NumStaggerRating.TabIndex = 9
        NumStaggerRating.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
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
        ' TabSlots
        '
        TabSlots.AutoScroll = True
        TabSlots.Controls.Add(SlotsLayout)
        TabSlots.Location = New Point(4, 24)
        TabSlots.Name = "TabSlots"
        TabSlots.Padding = New Padding(6)
        TabSlots.Size = New Size(712, 522)
        TabSlots.TabIndex = 5
        TabSlots.Text = "Slots"
        TabSlots.UseVisualStyleBackColor = True
        '
        ' SlotsLayout
        '
        SlotsLayout.ColumnCount = 2
        SlotsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SlotsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 240F))
        SlotsLayout.Controls.Add(LabelSlots, 0, 0)
        SlotsLayout.Controls.Add(ButtonRecalcSlots, 1, 0)
        SlotsLayout.Controls.Add(FlowSlots, 0, 1)
        SlotsLayout.SetColumnSpan(FlowSlots, 2)
        SlotsLayout.Dock = DockStyle.Fill
        SlotsLayout.Location = New Point(6, 6)
        SlotsLayout.Name = "SlotsLayout"
        SlotsLayout.RowCount = 2
        SlotsLayout.RowStyles.Add(New RowStyle())
        SlotsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SlotsLayout.Size = New Size(700, 510)
        SlotsLayout.TabIndex = 0
        '
        ' ButtonRecalcSlots
        '
        ButtonRecalcSlots.Anchor = AnchorStyles.Right
        ButtonRecalcSlots.AutoSize = True
        ButtonRecalcSlots.Location = New Point(477, 3)
        ButtonRecalcSlots.Name = "ButtonRecalcSlots"
        ButtonRecalcSlots.Size = New Size(220, 26)
        ButtonRecalcSlots.TabIndex = 1
        ButtonRecalcSlots.Text = "Recalculate from ARMA addons"
        ButtonRecalcSlots.UseVisualStyleBackColor = True
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
        GridAddons.EditMode = DataGridViewEditMode.EditProgrammatically
        GridAddons.MultiSelect = False
        GridAddons.Name = "GridAddons"
        GridAddons.ReadOnly = True
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
        ButtonEditIndx.Text = "Edit…"
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
        ' TabObts
        '
        TabObts.Controls.Add(ObtsLayout)
        TabObts.Location = New Point(4, 24)
        TabObts.Name = "TabObts"
        TabObts.Padding = New Padding(6)
        TabObts.Size = New Size(712, 522)
        TabObts.TabIndex = 4
        TabObts.Text = "Object Template"
        TabObts.UseVisualStyleBackColor = True
        '
        ' ObtsLayout
        '
        ObtsLayout.ColumnCount = 2
        ObtsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ObtsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120F))
        ObtsLayout.Controls.Add(LabelObts, 0, 0)
        ObtsLayout.Controls.Add(GridCombinations, 0, 1)
        ObtsLayout.Controls.Add(ObtsButtons, 1, 1)
        ObtsLayout.Controls.Add(LabelObtsHint, 0, 2)
        ObtsLayout.Dock = DockStyle.Fill
        ObtsLayout.Location = New Point(6, 6)
        ObtsLayout.Name = "ObtsLayout"
        ObtsLayout.RowCount = 3
        ObtsLayout.RowStyles.Add(New RowStyle())
        ObtsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ObtsLayout.RowStyles.Add(New RowStyle())
        ObtsLayout.Size = New Size(700, 510)
        ObtsLayout.TabIndex = 0
        '
        ' LabelObts
        '
        LabelObts.AutoSize = True
        LabelObts.Location = New Point(3, 0)
        LabelObts.Name = "LabelObts"
        LabelObts.Size = New Size(280, 15)
        LabelObts.TabIndex = 0
        LabelObts.Text = "Object Template (OBTE/OBTS) — combinations, order matters:"
        '
        ' GridCombinations
        '
        GridCombinations.AllowUserToAddRows = False
        GridCombinations.AllowUserToDeleteRows = False
        GridCombinations.AllowUserToResizeRows = False
        GridCombinations.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridCombinations.Dock = DockStyle.Fill
        GridCombinations.EditMode = DataGridViewEditMode.EditProgrammatically
        GridCombinations.Location = New Point(3, 18)
        GridCombinations.MultiSelect = False
        GridCombinations.Name = "GridCombinations"
        GridCombinations.ReadOnly = True
        GridCombinations.RowHeadersWidth = 25
        GridCombinations.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridCombinations.Size = New Size(574, 471)
        GridCombinations.TabIndex = 1
        '
        ' ObtsButtons
        '
        ObtsButtons.Controls.Add(ButtonAddCombo)
        ObtsButtons.Controls.Add(ButtonRemoveCombo)
        ObtsButtons.Controls.Add(ButtonDuplicateCombo)
        ObtsButtons.Controls.Add(ButtonComboUp)
        ObtsButtons.Controls.Add(ButtonComboDown)
        ObtsButtons.Controls.Add(ButtonEditCombo)
        ObtsButtons.Dock = DockStyle.Fill
        ObtsButtons.FlowDirection = FlowDirection.TopDown
        ObtsButtons.Location = New Point(580, 18)
        ObtsButtons.Margin = New Padding(0)
        ObtsButtons.Name = "ObtsButtons"
        ObtsButtons.Size = New Size(120, 471)
        ObtsButtons.TabIndex = 2
        '
        ' ButtonAddCombo
        '
        ButtonAddCombo.Location = New Point(3, 3)
        ButtonAddCombo.Name = "ButtonAddCombo"
        ButtonAddCombo.Size = New Size(110, 26)
        ButtonAddCombo.TabIndex = 0
        ButtonAddCombo.Text = "Add…"
        ButtonAddCombo.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveCombo
        '
        ButtonRemoveCombo.Location = New Point(3, 35)
        ButtonRemoveCombo.Name = "ButtonRemoveCombo"
        ButtonRemoveCombo.Size = New Size(110, 26)
        ButtonRemoveCombo.TabIndex = 1
        ButtonRemoveCombo.Text = "Remove"
        ButtonRemoveCombo.UseVisualStyleBackColor = True
        '
        ' ButtonDuplicateCombo
        '
        ButtonDuplicateCombo.Location = New Point(3, 67)
        ButtonDuplicateCombo.Name = "ButtonDuplicateCombo"
        ButtonDuplicateCombo.Size = New Size(110, 26)
        ButtonDuplicateCombo.TabIndex = 2
        ButtonDuplicateCombo.Text = "Duplicate"
        ButtonDuplicateCombo.UseVisualStyleBackColor = True
        '
        ' ButtonComboUp
        '
        ButtonComboUp.Location = New Point(3, 99)
        ButtonComboUp.Name = "ButtonComboUp"
        ButtonComboUp.Size = New Size(110, 26)
        ButtonComboUp.TabIndex = 3
        ButtonComboUp.Text = "Move Up"
        ButtonComboUp.UseVisualStyleBackColor = True
        '
        ' ButtonComboDown
        '
        ButtonComboDown.Location = New Point(3, 131)
        ButtonComboDown.Name = "ButtonComboDown"
        ButtonComboDown.Size = New Size(110, 26)
        ButtonComboDown.TabIndex = 4
        ButtonComboDown.Text = "Move Down"
        ButtonComboDown.UseVisualStyleBackColor = True
        '
        ' ButtonEditCombo
        '
        ButtonEditCombo.Location = New Point(3, 163)
        ButtonEditCombo.Name = "ButtonEditCombo"
        ButtonEditCombo.Size = New Size(110, 26)
        ButtonEditCombo.TabIndex = 5
        ButtonEditCombo.Text = "Edit…"
        ButtonEditCombo.UseVisualStyleBackColor = True
        '
        ' LabelObtsHint
        '
        LabelObtsHint.AutoSize = True
        LabelObtsHint.ForeColor = Color.DimGray
        LabelObtsHint.Location = New Point(3, 492)
        LabelObtsHint.Name = "LabelObtsHint"
        LabelObtsHint.Size = New Size(360, 15)
        LabelObtsHint.TabIndex = 3
        LabelObtsHint.Text = "Double-click or Edit… to open a combination; Add/Duplicate open the sub-editor."
        '
        ' TabMisc
        '
        TabMisc.AutoScroll = True
        TabMisc.Controls.Add(MiscLayout)
        TabMisc.Location = New Point(4, 24)
        TabMisc.Name = "TabMisc"
        TabMisc.Padding = New Padding(6)
        TabMisc.Size = New Size(712, 522)
        TabMisc.TabIndex = 6
        TabMisc.Text = "Misc & Sounds"
        TabMisc.UseVisualStyleBackColor = True
        '
        ' MiscLayout
        '
        MiscLayout.ColumnCount = 3
        MiscLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        MiscLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        MiscLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 50F))
        MiscLayout.Controls.Add(LabelYnam, 0, 0)
        MiscLayout.Controls.Add(TextBoxYnam, 1, 0)
        MiscLayout.Controls.Add(ButtonPickYnam, 2, 0)
        MiscLayout.Controls.Add(LabelZnam, 0, 1)
        MiscLayout.Controls.Add(TextBoxZnam, 1, 1)
        MiscLayout.Controls.Add(ButtonPickZnam, 2, 1)
        MiscLayout.Controls.Add(LabelEtyp, 0, 2)
        MiscLayout.Controls.Add(TextBoxEtyp, 1, 2)
        MiscLayout.Controls.Add(ButtonPickEtyp, 2, 2)
        MiscLayout.Controls.Add(LabelBamt, 0, 3)
        MiscLayout.Controls.Add(TextBoxBamt, 1, 3)
        MiscLayout.Controls.Add(ButtonPickBamt, 2, 3)
        MiscLayout.Controls.Add(LabelObnd, 0, 4)
        MiscLayout.Controls.Add(FlowObnd, 0, 5)
        MiscLayout.Controls.Add(ButtonRecomputeObnd, 0, 6)
        MiscLayout.Controls.Add(LabelObndHint, 1, 6)
        MiscLayout.SetColumnSpan(LabelObnd, 3)
        MiscLayout.SetColumnSpan(FlowObnd, 3)
        MiscLayout.SetColumnSpan(LabelObndHint, 2)
        MiscLayout.Dock = DockStyle.Fill
        MiscLayout.Location = New Point(6, 6)
        MiscLayout.Name = "MiscLayout"
        MiscLayout.RowCount = 8
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle())
        MiscLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        MiscLayout.Size = New Size(700, 510)
        MiscLayout.TabIndex = 0
        '
        ' LabelYnam
        '
        LabelYnam.Anchor = AnchorStyles.Left
        LabelYnam.AutoSize = True
        LabelYnam.Name = "LabelYnam"
        LabelYnam.Text = "Pickup Sound (YNAM):"
        '
        ' TextBoxYnam
        '
        TextBoxYnam.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxYnam.Name = "TextBoxYnam"
        TextBoxYnam.ReadOnly = True
        TextBoxYnam.Size = New Size(469, 23)
        TextBoxYnam.TabIndex = 0
        '
        ' ButtonPickYnam
        '
        ButtonPickYnam.Anchor = AnchorStyles.Left
        ButtonPickYnam.Name = "ButtonPickYnam"
        ButtonPickYnam.Size = New Size(34, 24)
        ButtonPickYnam.TabIndex = 1
        ButtonPickYnam.Text = "…"
        ButtonPickYnam.UseVisualStyleBackColor = True
        '
        ' LabelZnam
        '
        LabelZnam.Anchor = AnchorStyles.Left
        LabelZnam.AutoSize = True
        LabelZnam.Name = "LabelZnam"
        LabelZnam.Text = "Drop Sound (ZNAM):"
        '
        ' TextBoxZnam
        '
        TextBoxZnam.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxZnam.Name = "TextBoxZnam"
        TextBoxZnam.ReadOnly = True
        TextBoxZnam.Size = New Size(469, 23)
        TextBoxZnam.TabIndex = 2
        '
        ' ButtonPickZnam
        '
        ButtonPickZnam.Anchor = AnchorStyles.Left
        ButtonPickZnam.Name = "ButtonPickZnam"
        ButtonPickZnam.Size = New Size(34, 24)
        ButtonPickZnam.TabIndex = 3
        ButtonPickZnam.Text = "…"
        ButtonPickZnam.UseVisualStyleBackColor = True
        '
        ' LabelEtyp
        '
        LabelEtyp.Anchor = AnchorStyles.Left
        LabelEtyp.AutoSize = True
        LabelEtyp.Name = "LabelEtyp"
        LabelEtyp.Text = "Equip Type (ETYP):"
        '
        ' TextBoxEtyp
        '
        TextBoxEtyp.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxEtyp.Name = "TextBoxEtyp"
        TextBoxEtyp.ReadOnly = True
        TextBoxEtyp.Size = New Size(469, 23)
        TextBoxEtyp.TabIndex = 4
        '
        ' ButtonPickEtyp
        '
        ButtonPickEtyp.Anchor = AnchorStyles.Left
        ButtonPickEtyp.Name = "ButtonPickEtyp"
        ButtonPickEtyp.Size = New Size(34, 24)
        ButtonPickEtyp.TabIndex = 5
        ButtonPickEtyp.Text = "…"
        ButtonPickEtyp.UseVisualStyleBackColor = True
        '
        ' LabelBamt
        '
        LabelBamt.Anchor = AnchorStyles.Left
        LabelBamt.AutoSize = True
        LabelBamt.Name = "LabelBamt"
        LabelBamt.Text = "Block Material (BAMT):"
        '
        ' TextBoxBamt
        '
        TextBoxBamt.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxBamt.Name = "TextBoxBamt"
        TextBoxBamt.ReadOnly = True
        TextBoxBamt.Size = New Size(469, 23)
        TextBoxBamt.TabIndex = 6
        '
        ' ButtonPickBamt
        '
        ButtonPickBamt.Anchor = AnchorStyles.Left
        ButtonPickBamt.Name = "ButtonPickBamt"
        ButtonPickBamt.Size = New Size(34, 24)
        ButtonPickBamt.TabIndex = 7
        ButtonPickBamt.Text = "…"
        ButtonPickBamt.UseVisualStyleBackColor = True
        '
        ' LabelObnd
        '
        LabelObnd.Anchor = AnchorStyles.Left
        LabelObnd.AutoSize = True
        LabelObnd.Margin = New Padding(3, 12, 3, 3)
        LabelObnd.Name = "LabelObnd"
        LabelObnd.Text = "Object Bounds (min/max X, Y, Z):"
        '
        ' FlowObnd
        '
        FlowObnd.AutoSize = True
        FlowObnd.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FlowObnd.Controls.Add(NumObndX1)
        FlowObnd.Controls.Add(NumObndY1)
        FlowObnd.Controls.Add(NumObndZ1)
        FlowObnd.Controls.Add(NumObndX2)
        FlowObnd.Controls.Add(NumObndY2)
        FlowObnd.Controls.Add(NumObndZ2)
        FlowObnd.FlowDirection = FlowDirection.LeftToRight
        FlowObnd.Location = New Point(3, 3)
        FlowObnd.Name = "FlowObnd"
        FlowObnd.Size = New Size(450, 30)
        FlowObnd.TabIndex = 8
        FlowObnd.WrapContents = False
        '
        ' NumObndX1
        '
        NumObndX1.Maximum = New Decimal(32767)
        NumObndX1.Minimum = New Decimal(-32768)
        NumObndX1.Name = "NumObndX1"
        NumObndX1.Size = New Size(70, 23)
        NumObndX1.TabIndex = 0
        NumObndX1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' NumObndY1
        '
        NumObndY1.Maximum = New Decimal(32767)
        NumObndY1.Minimum = New Decimal(-32768)
        NumObndY1.Name = "NumObndY1"
        NumObndY1.Size = New Size(70, 23)
        NumObndY1.TabIndex = 1
        NumObndY1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' NumObndZ1
        '
        NumObndZ1.Maximum = New Decimal(32767)
        NumObndZ1.Minimum = New Decimal(-32768)
        NumObndZ1.Name = "NumObndZ1"
        NumObndZ1.Size = New Size(70, 23)
        NumObndZ1.TabIndex = 2
        NumObndZ1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' NumObndX2
        '
        NumObndX2.Maximum = New Decimal(32767)
        NumObndX2.Minimum = New Decimal(-32768)
        NumObndX2.Name = "NumObndX2"
        NumObndX2.Size = New Size(70, 23)
        NumObndX2.TabIndex = 3
        NumObndX2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' NumObndY2
        '
        NumObndY2.Maximum = New Decimal(32767)
        NumObndY2.Minimum = New Decimal(-32768)
        NumObndY2.Name = "NumObndY2"
        NumObndY2.Size = New Size(70, 23)
        NumObndY2.TabIndex = 4
        NumObndY2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' NumObndZ2
        '
        NumObndZ2.Maximum = New Decimal(32767)
        NumObndZ2.Minimum = New Decimal(-32768)
        NumObndZ2.Name = "NumObndZ2"
        NumObndZ2.Size = New Size(70, 23)
        NumObndZ2.TabIndex = 5
        NumObndZ2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' ButtonRecomputeObnd
        '
        ButtonRecomputeObnd.Anchor = AnchorStyles.Left
        ButtonRecomputeObnd.AutoSize = True
        ButtonRecomputeObnd.Name = "ButtonRecomputeObnd"
        ButtonRecomputeObnd.Size = New Size(160, 26)
        ButtonRecomputeObnd.TabIndex = 9
        ButtonRecomputeObnd.Text = "Recompute from mesh"
        ButtonRecomputeObnd.UseVisualStyleBackColor = True
        '
        ' LabelObndHint
        '
        LabelObndHint.Anchor = AnchorStyles.Left
        LabelObndHint.AutoSize = True
        LabelObndHint.ForeColor = Color.DimGray
        LabelObndHint.Name = "LabelObndHint"
        LabelObndHint.Text = "Approximate AABB from mesh vertices; not identical to the CK's value — editable afterwards."
        '
        ' TabDamage
        '
        TabDamage.Controls.Add(DamageLayout)
        TabDamage.Location = New Point(4, 24)
        TabDamage.Name = "TabDamage"
        TabDamage.Padding = New Padding(6)
        TabDamage.Size = New Size(712, 522)
        TabDamage.TabIndex = 7
        TabDamage.Text = "Damage Resist"
        TabDamage.UseVisualStyleBackColor = True
        '
        ' DamageLayout
        '
        DamageLayout.ColumnCount = 2
        DamageLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        DamageLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120F))
        DamageLayout.Controls.Add(LabelDamage, 0, 0)
        DamageLayout.Controls.Add(GridDamage, 0, 1)
        DamageLayout.Controls.Add(DamageButtons, 1, 1)
        DamageLayout.Dock = DockStyle.Fill
        DamageLayout.Location = New Point(6, 6)
        DamageLayout.Name = "DamageLayout"
        DamageLayout.RowCount = 2
        DamageLayout.RowStyles.Add(New RowStyle())
        DamageLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        DamageLayout.Size = New Size(700, 510)
        DamageLayout.TabIndex = 0
        '
        ' LabelDamage
        '
        LabelDamage.AutoSize = True
        LabelDamage.Location = New Point(3, 0)
        LabelDamage.Name = "LabelDamage"
        LabelDamage.Size = New Size(280, 15)
        LabelDamage.TabIndex = 0
        LabelDamage.Text = "Damage resistances (DAMA) — DMGT + Value:"
        '
        ' GridDamage
        '
        GridDamage.AllowUserToAddRows = False
        GridDamage.AllowUserToDeleteRows = False
        GridDamage.AllowUserToResizeRows = False
        GridDamage.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridDamage.Dock = DockStyle.Fill
        GridDamage.EditMode = DataGridViewEditMode.EditProgrammatically
        GridDamage.MultiSelect = False
        GridDamage.Name = "GridDamage"
        GridDamage.ReadOnly = True
        GridDamage.RowHeadersWidth = 25
        GridDamage.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridDamage.Location = New Point(3, 18)
        GridDamage.Size = New Size(574, 489)
        GridDamage.TabIndex = 1
        '
        ' DamageButtons
        '
        DamageButtons.Controls.Add(ButtonAddDamage)
        DamageButtons.Controls.Add(ButtonEditDamage)
        DamageButtons.Controls.Add(ButtonRemoveDamage)
        DamageButtons.Dock = DockStyle.Fill
        DamageButtons.FlowDirection = FlowDirection.TopDown
        DamageButtons.Location = New Point(580, 18)
        DamageButtons.Margin = New Padding(0)
        DamageButtons.Name = "DamageButtons"
        DamageButtons.Size = New Size(120, 489)
        DamageButtons.TabIndex = 2
        '
        ' ButtonAddDamage
        '
        ButtonAddDamage.Location = New Point(3, 3)
        ButtonAddDamage.Name = "ButtonAddDamage"
        ButtonAddDamage.Size = New Size(110, 26)
        ButtonAddDamage.TabIndex = 0
        ButtonAddDamage.Text = "Add…"
        ButtonAddDamage.UseVisualStyleBackColor = True
        '
        ' ButtonEditDamage
        '
        ButtonEditDamage.Location = New Point(3, 35)
        ButtonEditDamage.Name = "ButtonEditDamage"
        ButtonEditDamage.Size = New Size(110, 26)
        ButtonEditDamage.TabIndex = 1
        ButtonEditDamage.Text = "Edit…"
        ButtonEditDamage.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveDamage
        '
        ButtonRemoveDamage.Location = New Point(3, 67)
        ButtonRemoveDamage.Name = "ButtonRemoveDamage"
        ButtonRemoveDamage.Size = New Size(110, 26)
        ButtonRemoveDamage.TabIndex = 2
        ButtonRemoveDamage.Text = "Remove"
        ButtonRemoveDamage.UseVisualStyleBackColor = True
        '
        ' PreviewLayout
        '
        PreviewLayout.ColumnCount = 1
        PreviewLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PreviewLayout.Controls.Add(LabelPreviewHint, 0, 0)
        PreviewLayout.Controls.Add(PreviewControlPanel, 0, 1)
        PreviewLayout.Controls.Add(PreviewModePanel, 0, 2)
        PreviewLayout.Dock = DockStyle.Fill
        PreviewLayout.Location = New Point(0, 0)
        PreviewLayout.Name = "PreviewLayout"
        PreviewLayout.RowCount = 3
        PreviewLayout.RowStyles.Add(New RowStyle())
        PreviewLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PreviewLayout.RowStyles.Add(New RowStyle())
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
        ' PreviewModePanel
        '
        PreviewModePanel.AutoSize = True
        PreviewModePanel.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PreviewModePanel.Controls.Add(RadioOnlyArmor)
        PreviewModePanel.Controls.Add(RadioFullOutfit)
        PreviewModePanel.Controls.Add(CheckIncludeBody)
        PreviewModePanel.Controls.Add(CheckShowOtherGender)
        PreviewModePanel.Dock = DockStyle.Fill
        PreviewModePanel.Location = New Point(0, 553)
        PreviewModePanel.Margin = New Padding(0)
        PreviewModePanel.Name = "PreviewModePanel"
        PreviewModePanel.Size = New Size(498, 25)
        PreviewModePanel.TabIndex = 2
        PreviewModePanel.WrapContents = True
        '
        ' RadioOnlyArmor
        '
        RadioOnlyArmor.AutoSize = True
        RadioOnlyArmor.Checked = True
        RadioOnlyArmor.Margin = New Padding(3, 3, 8, 3)
        RadioOnlyArmor.Name = "RadioOnlyArmor"
        RadioOnlyArmor.Size = New Size(84, 19)
        RadioOnlyArmor.TabIndex = 0
        RadioOnlyArmor.TabStop = True
        RadioOnlyArmor.Text = "Only Armor"
        RadioOnlyArmor.UseVisualStyleBackColor = True
        '
        ' RadioFullOutfit
        '
        RadioFullOutfit.AutoSize = True
        RadioFullOutfit.Margin = New Padding(3, 3, 12, 3)
        RadioFullOutfit.Name = "RadioFullOutfit"
        RadioFullOutfit.Size = New Size(80, 19)
        RadioFullOutfit.TabIndex = 1
        RadioFullOutfit.Text = "Full Outfit"
        RadioFullOutfit.UseVisualStyleBackColor = True
        '
        ' CheckIncludeBody
        '
        CheckIncludeBody.AutoSize = True
        CheckIncludeBody.Checked = True
        CheckIncludeBody.CheckState = CheckState.Checked
        CheckIncludeBody.Margin = New Padding(3, 3, 12, 3)
        CheckIncludeBody.Name = "CheckIncludeBody"
        CheckIncludeBody.Size = New Size(97, 19)
        CheckIncludeBody.TabIndex = 2
        CheckIncludeBody.Text = "Include Body"
        CheckIncludeBody.UseVisualStyleBackColor = True
        '
        ' CheckShowOtherGender
        '
        CheckShowOtherGender.AutoSize = True
        CheckShowOtherGender.Margin = New Padding(3, 3, 3, 3)
        CheckShowOtherGender.Name = "CheckShowOtherGender"
        CheckShowOtherGender.Size = New Size(127, 19)
        CheckShowOtherGender.TabIndex = 3
        CheckShowOtherGender.Text = "Switch gender"
        CheckShowOtherGender.UseVisualStyleBackColor = True
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
        ClientSize = New Size(1264, 681)
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
        TabSlots.ResumeLayout(False)
        SlotsLayout.ResumeLayout(False)
        SlotsLayout.PerformLayout()
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
        CType(NumBaseAddonIndex, ComponentModel.ISupportInitialize).EndInit()
        CType(NumStaggerRating, ComponentModel.ISupportInitialize).EndInit()
        TabMisc.ResumeLayout(False)
        TabMisc.PerformLayout()
        MiscLayout.ResumeLayout(False)
        MiscLayout.PerformLayout()
        FlowObnd.ResumeLayout(False)
        CType(NumObndX1, ComponentModel.ISupportInitialize).EndInit()
        CType(NumObndY1, ComponentModel.ISupportInitialize).EndInit()
        CType(NumObndZ1, ComponentModel.ISupportInitialize).EndInit()
        CType(NumObndX2, ComponentModel.ISupportInitialize).EndInit()
        CType(NumObndY2, ComponentModel.ISupportInitialize).EndInit()
        CType(NumObndZ2, ComponentModel.ISupportInitialize).EndInit()
        TabDamage.ResumeLayout(False)
        TabDamage.PerformLayout()
        DamageLayout.ResumeLayout(False)
        DamageLayout.PerformLayout()
        CType(GridDamage, ComponentModel.ISupportInitialize).EndInit()
        DamageButtons.ResumeLayout(False)
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
        TabObts.ResumeLayout(False)
        ObtsLayout.ResumeLayout(False)
        ObtsLayout.PerformLayout()
        CType(GridCombinations, ComponentModel.ISupportInitialize).EndInit()
        ObtsButtons.ResumeLayout(False)
        PreviewModePanel.ResumeLayout(False)
        PreviewModePanel.PerformLayout()
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
    Friend WithEvents LabelEdidPreview As System.Windows.Forms.Label
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
    Friend WithEvents LabelInnr As System.Windows.Forms.Label
    Friend WithEvents TextBoxInnr As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickInnr As System.Windows.Forms.Button
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
    Friend WithEvents TabSlots As System.Windows.Forms.TabPage
    Friend WithEvents SlotsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonRecalcSlots As System.Windows.Forms.Button
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
    Friend WithEvents TabObts As System.Windows.Forms.TabPage
    Friend WithEvents ObtsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelObts As System.Windows.Forms.Label
    Friend WithEvents GridCombinations As System.Windows.Forms.DataGridView
    Friend WithEvents ObtsButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonDuplicateCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonComboUp As System.Windows.Forms.Button
    Friend WithEvents ButtonComboDown As System.Windows.Forms.Button
    Friend WithEvents ButtonEditCombo As System.Windows.Forms.Button
    Friend WithEvents LabelObtsHint As System.Windows.Forms.Label
    Friend WithEvents PreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents PreviewModePanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents RadioOnlyArmor As System.Windows.Forms.RadioButton
    Friend WithEvents RadioFullOutfit As System.Windows.Forms.RadioButton
    Friend WithEvents CheckIncludeBody As System.Windows.Forms.CheckBox
    Friend WithEvents CheckShowOtherGender As System.Windows.Forms.CheckBox
    Friend WithEvents LabelPreviewHint As System.Windows.Forms.Label
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents LabelEitm As System.Windows.Forms.Label
    Friend WithEvents TextBoxEitm As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickEitm As System.Windows.Forms.Button
    Friend WithEvents LabelPtrn As System.Windows.Forms.Label
    Friend WithEvents TextBoxPtrn As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickPtrn As System.Windows.Forms.Button
    Friend WithEvents CheckBoxNonPlayable As System.Windows.Forms.CheckBox
    Friend WithEvents LabelDesc As System.Windows.Forms.Label
    Friend WithEvents TextBoxDesc As System.Windows.Forms.TextBox
    Friend WithEvents LabelBaseAddonIndex As System.Windows.Forms.Label
    Friend WithEvents NumBaseAddonIndex As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelStaggerRating As System.Windows.Forms.Label
    Friend WithEvents NumStaggerRating As System.Windows.Forms.NumericUpDown
    Friend WithEvents TabMisc As System.Windows.Forms.TabPage
    Friend WithEvents MiscLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelYnam As System.Windows.Forms.Label
    Friend WithEvents TextBoxYnam As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickYnam As System.Windows.Forms.Button
    Friend WithEvents LabelZnam As System.Windows.Forms.Label
    Friend WithEvents TextBoxZnam As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickZnam As System.Windows.Forms.Button
    Friend WithEvents LabelEtyp As System.Windows.Forms.Label
    Friend WithEvents TextBoxEtyp As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickEtyp As System.Windows.Forms.Button
    Friend WithEvents LabelBamt As System.Windows.Forms.Label
    Friend WithEvents TextBoxBamt As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickBamt As System.Windows.Forms.Button
    Friend WithEvents LabelObnd As System.Windows.Forms.Label
    Friend WithEvents FlowObnd As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents NumObndX1 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumObndY1 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumObndZ1 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumObndX2 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumObndY2 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumObndZ2 As System.Windows.Forms.NumericUpDown
    Friend WithEvents ButtonRecomputeObnd As System.Windows.Forms.Button
    Friend WithEvents LabelObndHint As System.Windows.Forms.Label
    Friend WithEvents TabDamage As System.Windows.Forms.TabPage
    Friend WithEvents DamageLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelDamage As System.Windows.Forms.Label
    Friend WithEvents GridDamage As System.Windows.Forms.DataGridView
    Friend WithEvents DamageButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddDamage As System.Windows.Forms.Button
    Friend WithEvents ButtonEditDamage As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveDamage As System.Windows.Forms.Button
End Class
