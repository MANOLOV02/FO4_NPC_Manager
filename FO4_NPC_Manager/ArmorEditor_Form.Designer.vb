' UI built in Designer per feedback_ui_in_designer.md.
' Repeated controls that are variable/many (the granular slot checkboxes, the keyword/addon/race
' lists' rows, the MSWP substitutions grid columns) are NOT enumerated here — per the Designer rule
' (InitializeComponent stays declarative, no loops): the CONTAINER controls (the FlowLayoutPanels /
' the DataGridView) are declared here and their children/columns are added in code-behind after
' InitializeComponent (see ArmorEditor_Form.vb BuildSlotCheckBoxes / grid setup).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ArmorEditor_Form
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
        LeftLayout = New TableLayoutPanel()
        GroupBoxDrafts = New GroupBox()
        ListViewDrafts = New ListView()
        ColDraftName = New ColumnHeader()
        ColDraftType = New ColumnHeader()
        ColDraftStatus = New ColumnHeader()
        DraftButtonsRow = New FlowLayoutPanel()
        ButtonNewArmo = New Button()
        ButtonNewArma = New Button()
        ButtonNewMswp = New Button()
        ButtonOverrideExisting = New Button()
        ButtonDeleteDraft = New Button()
        ButtonApply = New Button()
        EditorHostPanel = New Panel()
        GroupBoxArmo = New GroupBox()
        ArmoLayout = New TableLayoutPanel()
        LabelArmoEdid = New Label()
        TextBoxArmoEdid = New TextBox()
        LabelArmoFull = New Label()
        TextBoxArmoFull = New TextBox()
        LabelArmoRace = New Label()
        ComboArmoRace = New ComboBox()
        LabelArmoSlots = New Label()
        FlowArmoSlots = New FlowLayoutPanel()
        LabelArmoKeywords = New Label()
        ArmoKeywordsRow = New FlowLayoutPanel()
        ComboArmoKeyword = New ComboBox()
        ButtonArmoAddKeyword = New Button()
        ButtonArmoRemoveKeyword = New Button()
        ListViewArmoKeywords = New ListView()
        ColArmoKwName = New ColumnHeader()
        LabelArmoAddons = New Label()
        ArmoAddonsRow = New FlowLayoutPanel()
        ButtonArmoAddAddonExisting = New Button()
        ButtonArmoAddAddonDraft = New Button()
        ButtonArmoRemoveAddon = New Button()
        ButtonArmoAddonUp = New Button()
        ButtonArmoAddonDown = New Button()
        LabelArmoAddonIndx = New Label()
        NumArmoAddonIndx = New NumericUpDown()
        ListViewArmoAddons = New ListView()
        ColArmoAddonIndx = New ColumnHeader()
        ColArmoAddonArma = New ColumnHeader()
        ArmoDataRow = New FlowLayoutPanel()
        LabelArmoValue = New Label()
        NumArmoValue = New NumericUpDown()
        LabelArmoWeight = New Label()
        NumArmoWeight = New NumericUpDown()
        LabelArmoHealth = New Label()
        NumArmoHealth = New NumericUpDown()
        ArmoFnamRow = New FlowLayoutPanel()
        LabelArmoRating = New Label()
        NumArmoRating = New NumericUpDown()
        LabelArmoBaseAddon = New Label()
        NumArmoBaseAddon = New NumericUpDown()
        LabelArmoStagger = New Label()
        NumArmoStagger = New NumericUpDown()
        ArmoMswpRow = New FlowLayoutPanel()
        LabelArmoMswp = New Label()
        ComboArmoMswp = New ComboBox()
        ButtonArmoNewMswp = New Button()
        ArmoTnamRow = New FlowLayoutPanel()
        LabelArmoTnam = New Label()
        ComboArmoTnam = New ComboBox()
        GroupBoxArma = New GroupBox()
        ArmaLayout = New TableLayoutPanel()
        LabelArmaEdid = New Label()
        TextBoxArmaEdid = New TextBox()
        LabelArmaRace = New Label()
        ComboArmaRace = New ComboBox()
        LabelArmaSlots = New Label()
        FlowArmaSlots = New FlowLayoutPanel()
        LabelArmaAddRaces = New Label()
        ArmaAddRacesRow = New FlowLayoutPanel()
        ComboArmaAddRace = New ComboBox()
        ButtonArmaAddRace = New Button()
        ButtonArmaRemoveRace = New Button()
        ListViewArmaAddRaces = New ListView()
        ColArmaRaceName = New ColumnHeader()
        ArmaMeshMaleRow = New FlowLayoutPanel()
        LabelArmaMeshMale = New Label()
        TextBoxArmaMeshMale = New TextBox()
        ButtonArmaBrowseMeshMale = New Button()
        ArmaMeshFemaleRow = New FlowLayoutPanel()
        LabelArmaMeshFemale = New Label()
        TextBoxArmaMeshFemale = New TextBox()
        ButtonArmaBrowseMeshFemale = New Button()
        ArmaMeshMaleFpRow = New FlowLayoutPanel()
        LabelArmaMeshMaleFp = New Label()
        TextBoxArmaMeshMaleFp = New TextBox()
        ButtonArmaBrowseMeshMaleFp = New Button()
        ArmaMeshFemaleFpRow = New FlowLayoutPanel()
        LabelArmaMeshFemaleFp = New Label()
        TextBoxArmaMeshFemaleFp = New TextBox()
        ButtonArmaBrowseMeshFemaleFp = New Button()
        ArmaFlagsRow = New FlowLayoutPanel()
        CheckArmaMaleFaceBones = New CheckBox()
        CheckArmaMale1stPerson = New CheckBox()
        CheckArmaFemaleFaceBones = New CheckBox()
        CheckArmaFemale1stPerson = New CheckBox()
        ArmaTxstRow = New FlowLayoutPanel()
        LabelArmaTxstMale = New Label()
        ComboArmaTxstMale = New ComboBox()
        LabelArmaTxstFemale = New Label()
        ComboArmaTxstFemale = New ComboBox()
        ArmaMswpRow = New FlowLayoutPanel()
        LabelArmaMswpMale = New Label()
        ComboArmaMswpMale = New ComboBox()
        LabelArmaMswpFemale = New Label()
        ComboArmaMswpFemale = New ComboBox()
        ButtonArmaNewMswp = New Button()
        ArmaDnamRow = New FlowLayoutPanel()
        LabelArmaMalePrio = New Label()
        NumArmaMalePrio = New NumericUpDown()
        LabelArmaFemalePrio = New Label()
        NumArmaFemalePrio = New NumericUpDown()
        CheckArmaMaleWeightEnabled = New CheckBox()
        CheckArmaFemaleWeightEnabled = New CheckBox()
        ArmaDnam2Row = New FlowLayoutPanel()
        LabelArmaDetSound = New Label()
        NumArmaDetSound = New NumericUpDown()
        LabelArmaWeaponAdjust = New Label()
        NumArmaWeaponAdjust = New NumericUpDown()
        LabelArmaBoneScaleTodo = New Label()
        GroupBoxMswp = New GroupBox()
        MswpLayout = New TableLayoutPanel()
        LabelMswpEdid = New Label()
        TextBoxMswpEdid = New TextBox()
        LabelMswpTreeFolder = New Label()
        TextBoxMswpTreeFolder = New TextBox()
        MswpGridButtonsRow = New FlowLayoutPanel()
        ButtonMswpAddRow = New Button()
        ButtonMswpRemoveRow = New Button()
        ButtonMswpBrowseOriginal = New Button()
        ButtonMswpBrowseReplacement = New Button()
        GridMswp = New DataGridView()
        LabelNoSelection = New Label()
        PreviewLayout = New TableLayoutPanel()
        PreviewControlPanel = New Panel()
        LabelPreviewHint = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonClose = New Button()
        RootLayout.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.Panel1.SuspendLayout()
        MainSplit.Panel2.SuspendLayout()
        MainSplit.SuspendLayout()
        LeftLayout.SuspendLayout()
        GroupBoxDrafts.SuspendLayout()
        DraftButtonsRow.SuspendLayout()
        EditorHostPanel.SuspendLayout()
        GroupBoxArmo.SuspendLayout()
        ArmoLayout.SuspendLayout()
        ArmoKeywordsRow.SuspendLayout()
        ArmoAddonsRow.SuspendLayout()
        CType(NumArmoAddonIndx, ComponentModel.ISupportInitialize).BeginInit()
        ArmoDataRow.SuspendLayout()
        CType(NumArmoValue, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmoWeight, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmoHealth, ComponentModel.ISupportInitialize).BeginInit()
        ArmoFnamRow.SuspendLayout()
        CType(NumArmoRating, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmoBaseAddon, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmoStagger, ComponentModel.ISupportInitialize).BeginInit()
        ArmoMswpRow.SuspendLayout()
        ArmoTnamRow.SuspendLayout()
        GroupBoxArma.SuspendLayout()
        ArmaLayout.SuspendLayout()
        ArmaAddRacesRow.SuspendLayout()
        ArmaMeshMaleRow.SuspendLayout()
        ArmaMeshFemaleRow.SuspendLayout()
        ArmaMeshMaleFpRow.SuspendLayout()
        ArmaMeshFemaleFpRow.SuspendLayout()
        ArmaFlagsRow.SuspendLayout()
        ArmaTxstRow.SuspendLayout()
        ArmaMswpRow.SuspendLayout()
        ArmaDnamRow.SuspendLayout()
        CType(NumArmaMalePrio, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmaFemalePrio, ComponentModel.ISupportInitialize).BeginInit()
        ArmaDnam2Row.SuspendLayout()
        CType(NumArmaDetSound, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumArmaWeaponAdjust, ComponentModel.ISupportInitialize).BeginInit()
        GroupBoxMswp.SuspendLayout()
        MswpLayout.SuspendLayout()
        MswpGridButtonsRow.SuspendLayout()
        CType(GridMswp, ComponentModel.ISupportInitialize).BeginInit()
        PreviewLayout.SuspendLayout()
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
        RootLayout.Size = New Size(1244, 741)
        RootLayout.TabIndex = 0
        '
        ' MainSplit
        '
        MainSplit.Dock = DockStyle.Fill
        MainSplit.FixedPanel = FixedPanel.Panel2
        MainSplit.Location = New Point(11, 11)
        MainSplit.Name = "MainSplit"
        '
        ' MainSplit.Panel1
        '
        MainSplit.Panel1.Controls.Add(LeftLayout)
        '
        ' MainSplit.Panel2
        '
        MainSplit.Panel2.Controls.Add(PreviewLayout)
        MainSplit.Size = New Size(1222, 678)
        MainSplit.SplitterDistance = 760
        MainSplit.TabIndex = 0
        '
        ' LeftLayout
        '
        LeftLayout.ColumnCount = 1
        LeftLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        LeftLayout.Controls.Add(GroupBoxDrafts, 0, 0)
        LeftLayout.Controls.Add(DraftButtonsRow, 0, 1)
        LeftLayout.Controls.Add(EditorHostPanel, 0, 2)
        LeftLayout.Dock = DockStyle.Fill
        LeftLayout.Location = New Point(0, 0)
        LeftLayout.Name = "LeftLayout"
        LeftLayout.RowCount = 3
        LeftLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 32F))
        LeftLayout.RowStyles.Add(New RowStyle())
        LeftLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 68F))
        LeftLayout.Size = New Size(760, 678)
        LeftLayout.TabIndex = 0
        '
        ' GroupBoxDrafts
        '
        GroupBoxDrafts.Controls.Add(ListViewDrafts)
        GroupBoxDrafts.Dock = DockStyle.Fill
        GroupBoxDrafts.Location = New Point(3, 3)
        GroupBoxDrafts.Name = "GroupBoxDrafts"
        GroupBoxDrafts.Padding = New Padding(6)
        GroupBoxDrafts.Size = New Size(754, 211)
        GroupBoxDrafts.TabIndex = 0
        GroupBoxDrafts.TabStop = False
        GroupBoxDrafts.Text = "Drafts (ARMO / ARMA / MSWP)"
        '
        ' ListViewDrafts
        '
        ListViewDrafts.Columns.AddRange(New ColumnHeader() {ColDraftName, ColDraftType, ColDraftStatus})
        ListViewDrafts.Dock = DockStyle.Fill
        ListViewDrafts.FullRowSelect = True
        ListViewDrafts.Location = New Point(6, 22)
        ListViewDrafts.MultiSelect = False
        ListViewDrafts.Name = "ListViewDrafts"
        ListViewDrafts.Size = New Size(742, 183)
        ListViewDrafts.TabIndex = 0
        ListViewDrafts.UseCompatibleStateImageBehavior = False
        ListViewDrafts.View = View.Details
        '
        ' ColDraftName
        '
        ColDraftName.Text = "Name"
        ColDraftName.Width = 460
        '
        ' ColDraftType
        '
        ColDraftType.Text = "Type"
        ColDraftType.Width = 90
        '
        ' ColDraftStatus
        '
        ColDraftStatus.Text = "Status"
        ColDraftStatus.Width = 170
        '
        ' DraftButtonsRow
        '
        DraftButtonsRow.AutoSize = True
        DraftButtonsRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        DraftButtonsRow.Controls.Add(ButtonNewArmo)
        DraftButtonsRow.Controls.Add(ButtonNewArma)
        DraftButtonsRow.Controls.Add(ButtonNewMswp)
        DraftButtonsRow.Controls.Add(ButtonOverrideExisting)
        DraftButtonsRow.Controls.Add(ButtonDeleteDraft)
        DraftButtonsRow.Controls.Add(ButtonApply)
        DraftButtonsRow.Dock = DockStyle.Fill
        DraftButtonsRow.Location = New Point(0, 217)
        DraftButtonsRow.Margin = New Padding(0)
        DraftButtonsRow.Name = "DraftButtonsRow"
        DraftButtonsRow.Size = New Size(760, 34)
        DraftButtonsRow.TabIndex = 1
        DraftButtonsRow.WrapContents = True
        '
        ' ButtonNewArmo
        '
        ButtonNewArmo.AutoSize = True
        ButtonNewArmo.Location = New Point(3, 3)
        ButtonNewArmo.Name = "ButtonNewArmo"
        ButtonNewArmo.Size = New Size(80, 25)
        ButtonNewArmo.TabIndex = 0
        ButtonNewArmo.Text = "New ARMO"
        ButtonNewArmo.UseVisualStyleBackColor = True
        '
        ' ButtonNewArma
        '
        ButtonNewArma.AutoSize = True
        ButtonNewArma.Location = New Point(89, 3)
        ButtonNewArma.Name = "ButtonNewArma"
        ButtonNewArma.Size = New Size(80, 25)
        ButtonNewArma.TabIndex = 1
        ButtonNewArma.Text = "New ARMA"
        ButtonNewArma.UseVisualStyleBackColor = True
        '
        ' ButtonNewMswp
        '
        ButtonNewMswp.AutoSize = True
        ButtonNewMswp.Location = New Point(175, 3)
        ButtonNewMswp.Name = "ButtonNewMswp"
        ButtonNewMswp.Size = New Size(80, 25)
        ButtonNewMswp.TabIndex = 2
        ButtonNewMswp.Text = "New MSWP"
        ButtonNewMswp.UseVisualStyleBackColor = True
        '
        ' ButtonOverrideExisting
        '
        ButtonOverrideExisting.AutoSize = True
        ButtonOverrideExisting.Location = New Point(261, 3)
        ButtonOverrideExisting.Name = "ButtonOverrideExisting"
        ButtonOverrideExisting.Size = New Size(120, 25)
        ButtonOverrideExisting.TabIndex = 3
        ButtonOverrideExisting.Text = "Override existing…"
        ButtonOverrideExisting.UseVisualStyleBackColor = True
        '
        ' ButtonDeleteDraft
        '
        ButtonDeleteDraft.AutoSize = True
        ButtonDeleteDraft.Location = New Point(387, 3)
        ButtonDeleteDraft.Name = "ButtonDeleteDraft"
        ButtonDeleteDraft.Size = New Size(90, 25)
        ButtonDeleteDraft.TabIndex = 4
        ButtonDeleteDraft.Text = "Delete draft"
        ButtonDeleteDraft.UseVisualStyleBackColor = True
        '
        ' ButtonApply
        '
        ButtonApply.AutoSize = True
        ButtonApply.Location = New Point(483, 3)
        ButtonApply.Name = "ButtonApply"
        ButtonApply.Size = New Size(110, 25)
        ButtonApply.TabIndex = 5
        ButtonApply.Text = "Apply (to draft)"
        ButtonApply.UseVisualStyleBackColor = True
        '
        ' EditorHostPanel
        '
        EditorHostPanel.Controls.Add(GroupBoxArmo)
        EditorHostPanel.Controls.Add(GroupBoxArma)
        EditorHostPanel.Controls.Add(GroupBoxMswp)
        EditorHostPanel.Controls.Add(LabelNoSelection)
        EditorHostPanel.Dock = DockStyle.Fill
        EditorHostPanel.Location = New Point(3, 254)
        EditorHostPanel.Name = "EditorHostPanel"
        EditorHostPanel.Size = New Size(754, 421)
        EditorHostPanel.TabIndex = 2
        '
        ' GroupBoxArmo
        '
        GroupBoxArmo.Controls.Add(ArmoLayout)
        GroupBoxArmo.Dock = DockStyle.Fill
        GroupBoxArmo.Location = New Point(0, 0)
        GroupBoxArmo.Name = "GroupBoxArmo"
        GroupBoxArmo.Padding = New Padding(6)
        GroupBoxArmo.Size = New Size(754, 421)
        GroupBoxArmo.TabIndex = 0
        GroupBoxArmo.TabStop = False
        GroupBoxArmo.Text = "ARMO (armor)"
        GroupBoxArmo.Visible = False
        '
        ' ArmoLayout
        '
        ArmoLayout.AutoScroll = True
        ArmoLayout.ColumnCount = 2
        ArmoLayout.ColumnStyles.Add(New ColumnStyle())
        ArmoLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ArmoLayout.Controls.Add(LabelArmoEdid, 0, 0)
        ArmoLayout.Controls.Add(TextBoxArmoEdid, 1, 0)
        ArmoLayout.Controls.Add(LabelArmoFull, 0, 1)
        ArmoLayout.Controls.Add(TextBoxArmoFull, 1, 1)
        ArmoLayout.Controls.Add(LabelArmoRace, 0, 2)
        ArmoLayout.Controls.Add(ComboArmoRace, 1, 2)
        ArmoLayout.Controls.Add(LabelArmoSlots, 0, 3)
        ArmoLayout.Controls.Add(FlowArmoSlots, 1, 3)
        ArmoLayout.Controls.Add(LabelArmoKeywords, 0, 4)
        ArmoLayout.Controls.Add(ArmoKeywordsRow, 1, 4)
        ArmoLayout.Controls.Add(ListViewArmoKeywords, 1, 5)
        ArmoLayout.Controls.Add(LabelArmoAddons, 0, 6)
        ArmoLayout.Controls.Add(ArmoAddonsRow, 1, 6)
        ArmoLayout.Controls.Add(ListViewArmoAddons, 1, 7)
        ArmoLayout.Controls.Add(ArmoDataRow, 1, 8)
        ArmoLayout.Controls.Add(ArmoFnamRow, 1, 9)
        ArmoLayout.Controls.Add(ArmoMswpRow, 1, 10)
        ArmoLayout.Controls.Add(ArmoTnamRow, 1, 11)
        ArmoLayout.Dock = DockStyle.Fill
        ArmoLayout.Location = New Point(6, 22)
        ArmoLayout.Name = "ArmoLayout"
        ArmoLayout.RowCount = 12
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.RowStyles.Add(New RowStyle())
        ArmoLayout.Size = New Size(742, 393)
        ArmoLayout.TabIndex = 0
        '
        ' LabelArmoEdid
        '
        LabelArmoEdid.Anchor = AnchorStyles.Left
        LabelArmoEdid.AutoSize = True
        LabelArmoEdid.Location = New Point(3, 7)
        LabelArmoEdid.Name = "LabelArmoEdid"
        LabelArmoEdid.Size = New Size(46, 15)
        LabelArmoEdid.TabIndex = 0
        LabelArmoEdid.Text = "EditorID"
        '
        ' TextBoxArmoEdid
        '
        TextBoxArmoEdid.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        TextBoxArmoEdid.Location = New Point(110, 3)
        TextBoxArmoEdid.Name = "TextBoxArmoEdid"
        TextBoxArmoEdid.Size = New Size(629, 23)
        TextBoxArmoEdid.TabIndex = 1
        '
        ' LabelArmoFull
        '
        LabelArmoFull.Anchor = AnchorStyles.Left
        LabelArmoFull.AutoSize = True
        LabelArmoFull.Location = New Point(3, 36)
        LabelArmoFull.Name = "LabelArmoFull"
        LabelArmoFull.Size = New Size(71, 15)
        LabelArmoFull.TabIndex = 2
        LabelArmoFull.Text = "Name (FULL)"
        '
        ' TextBoxArmoFull
        '
        TextBoxArmoFull.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        TextBoxArmoFull.Location = New Point(110, 32)
        TextBoxArmoFull.Name = "TextBoxArmoFull"
        TextBoxArmoFull.Size = New Size(629, 23)
        TextBoxArmoFull.TabIndex = 3
        '
        ' LabelArmoRace
        '
        LabelArmoRace.Anchor = AnchorStyles.Left
        LabelArmoRace.AutoSize = True
        LabelArmoRace.Location = New Point(3, 65)
        LabelArmoRace.Name = "LabelArmoRace"
        LabelArmoRace.Size = New Size(37, 15)
        LabelArmoRace.TabIndex = 4
        LabelArmoRace.Text = "Race"
        '
        ' ComboArmoRace
        '
        ComboArmoRace.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ComboArmoRace.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmoRace.Location = New Point(110, 61)
        ComboArmoRace.Name = "ComboArmoRace"
        ComboArmoRace.Size = New Size(629, 23)
        ComboArmoRace.TabIndex = 5
        '
        ' LabelArmoSlots
        '
        LabelArmoSlots.Anchor = AnchorStyles.Left
        LabelArmoSlots.AutoSize = True
        LabelArmoSlots.Location = New Point(3, 90)
        LabelArmoSlots.Name = "LabelArmoSlots"
        LabelArmoSlots.Size = New Size(64, 15)
        LabelArmoSlots.TabIndex = 6
        LabelArmoSlots.Text = "Slots (BOD2)"
        '
        ' FlowArmoSlots
        '
        FlowArmoSlots.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        FlowArmoSlots.AutoSize = True
        FlowArmoSlots.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FlowArmoSlots.Location = New Point(110, 90)
        FlowArmoSlots.Name = "FlowArmoSlots"
        FlowArmoSlots.Size = New Size(629, 10)
        FlowArmoSlots.TabIndex = 7
        '
        ' LabelArmoKeywords
        '
        LabelArmoKeywords.Anchor = AnchorStyles.Left
        LabelArmoKeywords.AutoSize = True
        LabelArmoKeywords.Location = New Point(3, 113)
        LabelArmoKeywords.Name = "LabelArmoKeywords"
        LabelArmoKeywords.Size = New Size(62, 15)
        LabelArmoKeywords.TabIndex = 8
        LabelArmoKeywords.Text = "Keywords"
        '
        ' ArmoKeywordsRow
        '
        ArmoKeywordsRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoKeywordsRow.AutoSize = True
        ArmoKeywordsRow.Controls.Add(ComboArmoKeyword)
        ArmoKeywordsRow.Controls.Add(ButtonArmoAddKeyword)
        ArmoKeywordsRow.Controls.Add(ButtonArmoRemoveKeyword)
        ArmoKeywordsRow.Location = New Point(110, 110)
        ArmoKeywordsRow.Margin = New Padding(3)
        ArmoKeywordsRow.Name = "ArmoKeywordsRow"
        ArmoKeywordsRow.Size = New Size(629, 31)
        ArmoKeywordsRow.TabIndex = 9
        ArmoKeywordsRow.WrapContents = False
        '
        ' ComboArmoKeyword
        '
        ComboArmoKeyword.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmoKeyword.Location = New Point(3, 3)
        ComboArmoKeyword.Name = "ComboArmoKeyword"
        ComboArmoKeyword.Size = New Size(360, 23)
        ComboArmoKeyword.TabIndex = 0
        '
        ' ButtonArmoAddKeyword
        '
        ButtonArmoAddKeyword.Location = New Point(369, 3)
        ButtonArmoAddKeyword.Name = "ButtonArmoAddKeyword"
        ButtonArmoAddKeyword.Size = New Size(60, 23)
        ButtonArmoAddKeyword.TabIndex = 1
        ButtonArmoAddKeyword.Text = "Add"
        ButtonArmoAddKeyword.UseVisualStyleBackColor = True
        '
        ' ButtonArmoRemoveKeyword
        '
        ButtonArmoRemoveKeyword.Location = New Point(435, 3)
        ButtonArmoRemoveKeyword.Name = "ButtonArmoRemoveKeyword"
        ButtonArmoRemoveKeyword.Size = New Size(70, 23)
        ButtonArmoRemoveKeyword.TabIndex = 2
        ButtonArmoRemoveKeyword.Text = "Remove"
        ButtonArmoRemoveKeyword.UseVisualStyleBackColor = True
        '
        ' ListViewArmoKeywords
        '
        ListViewArmoKeywords.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ListViewArmoKeywords.Columns.AddRange(New ColumnHeader() {ColArmoKwName})
        ListViewArmoKeywords.FullRowSelect = True
        ListViewArmoKeywords.Location = New Point(110, 147)
        ListViewArmoKeywords.MultiSelect = False
        ListViewArmoKeywords.Name = "ListViewArmoKeywords"
        ListViewArmoKeywords.Size = New Size(629, 60)
        ListViewArmoKeywords.TabIndex = 10
        ListViewArmoKeywords.UseCompatibleStateImageBehavior = False
        ListViewArmoKeywords.View = View.Details
        '
        ' ColArmoKwName
        '
        ColArmoKwName.Text = "Keyword"
        ColArmoKwName.Width = 600
        '
        ' LabelArmoAddons
        '
        LabelArmoAddons.Anchor = AnchorStyles.Left
        LabelArmoAddons.AutoSize = True
        LabelArmoAddons.Location = New Point(3, 213)
        LabelArmoAddons.Name = "LabelArmoAddons"
        LabelArmoAddons.Size = New Size(96, 15)
        LabelArmoAddons.TabIndex = 11
        LabelArmoAddons.Text = "Addons (ARMA)"
        '
        ' ArmoAddonsRow
        '
        ArmoAddonsRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoAddonsRow.AutoSize = True
        ArmoAddonsRow.Controls.Add(ButtonArmoAddAddonExisting)
        ArmoAddonsRow.Controls.Add(ButtonArmoAddAddonDraft)
        ArmoAddonsRow.Controls.Add(ButtonArmoRemoveAddon)
        ArmoAddonsRow.Controls.Add(ButtonArmoAddonUp)
        ArmoAddonsRow.Controls.Add(ButtonArmoAddonDown)
        ArmoAddonsRow.Controls.Add(LabelArmoAddonIndx)
        ArmoAddonsRow.Controls.Add(NumArmoAddonIndx)
        ArmoAddonsRow.Location = New Point(110, 210)
        ArmoAddonsRow.Margin = New Padding(3)
        ArmoAddonsRow.Name = "ArmoAddonsRow"
        ArmoAddonsRow.Size = New Size(629, 31)
        ArmoAddonsRow.TabIndex = 12
        ArmoAddonsRow.WrapContents = False
        '
        ' ButtonArmoAddAddonExisting
        '
        ButtonArmoAddAddonExisting.AutoSize = True
        ButtonArmoAddAddonExisting.Location = New Point(3, 3)
        ButtonArmoAddAddonExisting.Name = "ButtonArmoAddAddonExisting"
        ButtonArmoAddAddonExisting.Size = New Size(110, 25)
        ButtonArmoAddAddonExisting.TabIndex = 0
        ButtonArmoAddAddonExisting.Text = "Add ARMA…"
        ButtonArmoAddAddonExisting.UseVisualStyleBackColor = True
        '
        ' ButtonArmoAddAddonDraft
        '
        ButtonArmoAddAddonDraft.AutoSize = True
        ButtonArmoAddAddonDraft.Location = New Point(119, 3)
        ButtonArmoAddAddonDraft.Name = "ButtonArmoAddAddonDraft"
        ButtonArmoAddAddonDraft.Size = New Size(120, 25)
        ButtonArmoAddAddonDraft.TabIndex = 1
        ButtonArmoAddAddonDraft.Text = "Add ARMA draft…"
        ButtonArmoAddAddonDraft.UseVisualStyleBackColor = True
        '
        ' ButtonArmoRemoveAddon
        '
        ButtonArmoRemoveAddon.AutoSize = True
        ButtonArmoRemoveAddon.Location = New Point(245, 3)
        ButtonArmoRemoveAddon.Name = "ButtonArmoRemoveAddon"
        ButtonArmoRemoveAddon.Size = New Size(70, 25)
        ButtonArmoRemoveAddon.TabIndex = 2
        ButtonArmoRemoveAddon.Text = "Remove"
        ButtonArmoRemoveAddon.UseVisualStyleBackColor = True
        '
        ' ButtonArmoAddonUp
        '
        ButtonArmoAddonUp.Location = New Point(321, 3)
        ButtonArmoAddonUp.Name = "ButtonArmoAddonUp"
        ButtonArmoAddonUp.Size = New Size(40, 25)
        ButtonArmoAddonUp.TabIndex = 3
        ButtonArmoAddonUp.Text = "▲"
        ButtonArmoAddonUp.UseVisualStyleBackColor = True
        '
        ' ButtonArmoAddonDown
        '
        ButtonArmoAddonDown.Location = New Point(367, 3)
        ButtonArmoAddonDown.Name = "ButtonArmoAddonDown"
        ButtonArmoAddonDown.Size = New Size(40, 25)
        ButtonArmoAddonDown.TabIndex = 4
        ButtonArmoAddonDown.Text = "▼"
        ButtonArmoAddonDown.UseVisualStyleBackColor = True
        '
        ' LabelArmoAddonIndx
        '
        LabelArmoAddonIndx.Anchor = AnchorStyles.Left
        LabelArmoAddonIndx.AutoSize = True
        LabelArmoAddonIndx.Location = New Point(413, 7)
        LabelArmoAddonIndx.Name = "LabelArmoAddonIndx"
        LabelArmoAddonIndx.Size = New Size(36, 15)
        LabelArmoAddonIndx.TabIndex = 5
        LabelArmoAddonIndx.Text = "INDX"
        '
        ' NumArmoAddonIndx
        '
        NumArmoAddonIndx.Location = New Point(455, 4)
        NumArmoAddonIndx.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumArmoAddonIndx.Name = "NumArmoAddonIndx"
        NumArmoAddonIndx.Size = New Size(70, 23)
        NumArmoAddonIndx.TabIndex = 6
        '
        ' ListViewArmoAddons
        '
        ListViewArmoAddons.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ListViewArmoAddons.Columns.AddRange(New ColumnHeader() {ColArmoAddonIndx, ColArmoAddonArma})
        ListViewArmoAddons.FullRowSelect = True
        ListViewArmoAddons.Location = New Point(110, 247)
        ListViewArmoAddons.MultiSelect = False
        ListViewArmoAddons.Name = "ListViewArmoAddons"
        ListViewArmoAddons.Size = New Size(629, 70)
        ListViewArmoAddons.TabIndex = 13
        ListViewArmoAddons.UseCompatibleStateImageBehavior = False
        ListViewArmoAddons.View = View.Details
        '
        ' ColArmoAddonIndx
        '
        ColArmoAddonIndx.Text = "INDX"
        ColArmoAddonIndx.Width = 60
        '
        ' ColArmoAddonArma
        '
        ColArmoAddonArma.Text = "ARMA"
        ColArmoAddonArma.Width = 540
        '
        ' ArmoDataRow
        '
        ArmoDataRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoDataRow.AutoSize = True
        ArmoDataRow.Controls.Add(LabelArmoValue)
        ArmoDataRow.Controls.Add(NumArmoValue)
        ArmoDataRow.Controls.Add(LabelArmoWeight)
        ArmoDataRow.Controls.Add(NumArmoWeight)
        ArmoDataRow.Controls.Add(LabelArmoHealth)
        ArmoDataRow.Controls.Add(NumArmoHealth)
        ArmoDataRow.Location = New Point(110, 323)
        ArmoDataRow.Margin = New Padding(3)
        ArmoDataRow.Name = "ArmoDataRow"
        ArmoDataRow.Size = New Size(629, 31)
        ArmoDataRow.TabIndex = 14
        ArmoDataRow.WrapContents = False
        '
        ' LabelArmoValue
        '
        LabelArmoValue.Anchor = AnchorStyles.Left
        LabelArmoValue.AutoSize = True
        LabelArmoValue.Location = New Point(3, 7)
        LabelArmoValue.Name = "LabelArmoValue"
        LabelArmoValue.Size = New Size(35, 15)
        LabelArmoValue.TabIndex = 0
        LabelArmoValue.Text = "Value"
        '
        ' NumArmoValue
        '
        NumArmoValue.Location = New Point(44, 3)
        NumArmoValue.Maximum = New Decimal(New Integer() {2000000000, 0, 0, 0})
        NumArmoValue.Name = "NumArmoValue"
        NumArmoValue.Size = New Size(90, 23)
        NumArmoValue.TabIndex = 1
        '
        ' LabelArmoWeight
        '
        LabelArmoWeight.Anchor = AnchorStyles.Left
        LabelArmoWeight.AutoSize = True
        LabelArmoWeight.Location = New Point(140, 7)
        LabelArmoWeight.Name = "LabelArmoWeight"
        LabelArmoWeight.Size = New Size(45, 15)
        LabelArmoWeight.TabIndex = 2
        LabelArmoWeight.Text = "Weight"
        '
        ' NumArmoWeight
        '
        NumArmoWeight.DecimalPlaces = 2
        NumArmoWeight.Increment = New Decimal(New Integer() {1, 0, 0, 131072})
        NumArmoWeight.Location = New Point(191, 3)
        NumArmoWeight.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        NumArmoWeight.Name = "NumArmoWeight"
        NumArmoWeight.Size = New Size(90, 23)
        NumArmoWeight.TabIndex = 3
        '
        ' LabelArmoHealth
        '
        LabelArmoHealth.Anchor = AnchorStyles.Left
        LabelArmoHealth.AutoSize = True
        LabelArmoHealth.Location = New Point(287, 7)
        LabelArmoHealth.Name = "LabelArmoHealth"
        LabelArmoHealth.Size = New Size(42, 15)
        LabelArmoHealth.TabIndex = 4
        LabelArmoHealth.Text = "Health"
        '
        ' NumArmoHealth
        '
        NumArmoHealth.Location = New Point(335, 3)
        NumArmoHealth.Maximum = New Decimal(New Integer() {2000000000, 0, 0, 0})
        NumArmoHealth.Name = "NumArmoHealth"
        NumArmoHealth.Size = New Size(90, 23)
        NumArmoHealth.TabIndex = 5
        '
        ' ArmoFnamRow
        '
        ArmoFnamRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoFnamRow.AutoSize = True
        ArmoFnamRow.Controls.Add(LabelArmoRating)
        ArmoFnamRow.Controls.Add(NumArmoRating)
        ArmoFnamRow.Controls.Add(LabelArmoBaseAddon)
        ArmoFnamRow.Controls.Add(NumArmoBaseAddon)
        ArmoFnamRow.Controls.Add(LabelArmoStagger)
        ArmoFnamRow.Controls.Add(NumArmoStagger)
        ArmoFnamRow.Location = New Point(110, 360)
        ArmoFnamRow.Margin = New Padding(3)
        ArmoFnamRow.Name = "ArmoFnamRow"
        ArmoFnamRow.Size = New Size(629, 31)
        ArmoFnamRow.TabIndex = 15
        ArmoFnamRow.WrapContents = False
        '
        ' LabelArmoRating
        '
        LabelArmoRating.Anchor = AnchorStyles.Left
        LabelArmoRating.AutoSize = True
        LabelArmoRating.Location = New Point(3, 7)
        LabelArmoRating.Name = "LabelArmoRating"
        LabelArmoRating.Size = New Size(70, 15)
        LabelArmoRating.TabIndex = 0
        LabelArmoRating.Text = "Armor Rating"
        '
        ' NumArmoRating
        '
        NumArmoRating.Location = New Point(79, 3)
        NumArmoRating.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumArmoRating.Name = "NumArmoRating"
        NumArmoRating.Size = New Size(80, 23)
        NumArmoRating.TabIndex = 1
        '
        ' LabelArmoBaseAddon
        '
        LabelArmoBaseAddon.Anchor = AnchorStyles.Left
        LabelArmoBaseAddon.AutoSize = True
        LabelArmoBaseAddon.Location = New Point(165, 7)
        LabelArmoBaseAddon.Name = "LabelArmoBaseAddon"
        LabelArmoBaseAddon.Size = New Size(95, 15)
        LabelArmoBaseAddon.TabIndex = 2
        LabelArmoBaseAddon.Text = "Base Addon Idx"
        '
        ' NumArmoBaseAddon
        '
        NumArmoBaseAddon.Location = New Point(266, 3)
        NumArmoBaseAddon.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumArmoBaseAddon.Name = "NumArmoBaseAddon"
        NumArmoBaseAddon.Size = New Size(80, 23)
        NumArmoBaseAddon.TabIndex = 3
        '
        ' LabelArmoStagger
        '
        LabelArmoStagger.Anchor = AnchorStyles.Left
        LabelArmoStagger.AutoSize = True
        LabelArmoStagger.Location = New Point(352, 7)
        LabelArmoStagger.Name = "LabelArmoStagger"
        LabelArmoStagger.Size = New Size(49, 15)
        LabelArmoStagger.TabIndex = 4
        LabelArmoStagger.Text = "Stagger"
        '
        ' NumArmoStagger
        '
        NumArmoStagger.Location = New Point(407, 3)
        NumArmoStagger.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumArmoStagger.Name = "NumArmoStagger"
        NumArmoStagger.Size = New Size(70, 23)
        NumArmoStagger.TabIndex = 5
        '
        ' ArmoMswpRow
        '
        ArmoMswpRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoMswpRow.AutoSize = True
        ArmoMswpRow.Controls.Add(LabelArmoMswp)
        ArmoMswpRow.Controls.Add(ComboArmoMswp)
        ArmoMswpRow.Controls.Add(ButtonArmoNewMswp)
        ArmoMswpRow.Location = New Point(110, 397)
        ArmoMswpRow.Margin = New Padding(3)
        ArmoMswpRow.Name = "ArmoMswpRow"
        ArmoMswpRow.Size = New Size(629, 31)
        ArmoMswpRow.TabIndex = 16
        ArmoMswpRow.WrapContents = False
        '
        ' LabelArmoMswp
        '
        LabelArmoMswp.Anchor = AnchorStyles.Left
        LabelArmoMswp.AutoSize = True
        LabelArmoMswp.Location = New Point(3, 7)
        LabelArmoMswp.Name = "LabelArmoMswp"
        LabelArmoMswp.Size = New Size(120, 15)
        LabelArmoMswp.TabIndex = 0
        LabelArmoMswp.Text = "Material Swap (world)"
        '
        ' ComboArmoMswp
        '
        ComboArmoMswp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmoMswp.Location = New Point(129, 3)
        ComboArmoMswp.Name = "ComboArmoMswp"
        ComboArmoMswp.Size = New Size(360, 23)
        ComboArmoMswp.TabIndex = 1
        '
        ' ButtonArmoNewMswp
        '
        ButtonArmoNewMswp.Location = New Point(495, 3)
        ButtonArmoNewMswp.Name = "ButtonArmoNewMswp"
        ButtonArmoNewMswp.Size = New Size(110, 25)
        ButtonArmoNewMswp.TabIndex = 2
        ButtonArmoNewMswp.Text = "New MSWP…"
        ButtonArmoNewMswp.UseVisualStyleBackColor = True
        '
        ' ArmoTnamRow
        '
        ArmoTnamRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmoTnamRow.AutoSize = True
        ArmoTnamRow.Controls.Add(LabelArmoTnam)
        ArmoTnamRow.Controls.Add(ComboArmoTnam)
        ArmoTnamRow.Location = New Point(110, 434)
        ArmoTnamRow.Margin = New Padding(3)
        ArmoTnamRow.Name = "ArmoTnamRow"
        ArmoTnamRow.Size = New Size(629, 31)
        ArmoTnamRow.TabIndex = 17
        ArmoTnamRow.WrapContents = False
        '
        ' LabelArmoTnam
        '
        LabelArmoTnam.Anchor = AnchorStyles.Left
        LabelArmoTnam.AutoSize = True
        LabelArmoTnam.Location = New Point(3, 7)
        LabelArmoTnam.Name = "LabelArmoTnam"
        LabelArmoTnam.Size = New Size(120, 15)
        LabelArmoTnam.TabIndex = 0
        LabelArmoTnam.Text = "Template (TNAM)"
        '
        ' ComboArmoTnam
        '
        ComboArmoTnam.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmoTnam.Location = New Point(129, 3)
        ComboArmoTnam.Name = "ComboArmoTnam"
        ComboArmoTnam.Size = New Size(360, 23)
        ComboArmoTnam.TabIndex = 1
        '
        ' GroupBoxArma
        '
        GroupBoxArma.Controls.Add(ArmaLayout)
        GroupBoxArma.Dock = DockStyle.Fill
        GroupBoxArma.Location = New Point(0, 0)
        GroupBoxArma.Name = "GroupBoxArma"
        GroupBoxArma.Padding = New Padding(6)
        GroupBoxArma.Size = New Size(754, 421)
        GroupBoxArma.TabIndex = 1
        GroupBoxArma.TabStop = False
        GroupBoxArma.Text = "ARMA (armor addon)"
        GroupBoxArma.Visible = False
        '
        ' ArmaLayout
        '
        ArmaLayout.AutoScroll = True
        ArmaLayout.ColumnCount = 2
        ArmaLayout.ColumnStyles.Add(New ColumnStyle())
        ArmaLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ArmaLayout.Controls.Add(LabelArmaEdid, 0, 0)
        ArmaLayout.Controls.Add(TextBoxArmaEdid, 1, 0)
        ArmaLayout.Controls.Add(LabelArmaRace, 0, 1)
        ArmaLayout.Controls.Add(ComboArmaRace, 1, 1)
        ArmaLayout.Controls.Add(LabelArmaSlots, 0, 2)
        ArmaLayout.Controls.Add(FlowArmaSlots, 1, 2)
        ArmaLayout.Controls.Add(LabelArmaAddRaces, 0, 3)
        ArmaLayout.Controls.Add(ArmaAddRacesRow, 1, 3)
        ArmaLayout.Controls.Add(ListViewArmaAddRaces, 1, 4)
        ArmaLayout.Controls.Add(ArmaMeshMaleRow, 1, 5)
        ArmaLayout.Controls.Add(ArmaMeshFemaleRow, 1, 6)
        ArmaLayout.Controls.Add(ArmaMeshMaleFpRow, 1, 7)
        ArmaLayout.Controls.Add(ArmaMeshFemaleFpRow, 1, 8)
        ArmaLayout.Controls.Add(ArmaFlagsRow, 1, 9)
        ArmaLayout.Controls.Add(ArmaTxstRow, 1, 10)
        ArmaLayout.Controls.Add(ArmaMswpRow, 1, 11)
        ArmaLayout.Controls.Add(ArmaDnamRow, 1, 12)
        ArmaLayout.Controls.Add(ArmaDnam2Row, 1, 13)
        ArmaLayout.Controls.Add(LabelArmaBoneScaleTodo, 1, 14)
        ArmaLayout.Dock = DockStyle.Fill
        ArmaLayout.Location = New Point(6, 22)
        ArmaLayout.Name = "ArmaLayout"
        ArmaLayout.RowCount = 15
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.RowStyles.Add(New RowStyle())
        ArmaLayout.Size = New Size(742, 393)
        ArmaLayout.TabIndex = 0
        '
        ' LabelArmaEdid
        '
        LabelArmaEdid.Anchor = AnchorStyles.Left
        LabelArmaEdid.AutoSize = True
        LabelArmaEdid.Location = New Point(3, 7)
        LabelArmaEdid.Name = "LabelArmaEdid"
        LabelArmaEdid.Size = New Size(46, 15)
        LabelArmaEdid.TabIndex = 0
        LabelArmaEdid.Text = "EditorID"
        '
        ' TextBoxArmaEdid
        '
        TextBoxArmaEdid.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        TextBoxArmaEdid.Location = New Point(120, 3)
        TextBoxArmaEdid.Name = "TextBoxArmaEdid"
        TextBoxArmaEdid.Size = New Size(619, 23)
        TextBoxArmaEdid.TabIndex = 1
        '
        ' LabelArmaRace
        '
        LabelArmaRace.Anchor = AnchorStyles.Left
        LabelArmaRace.AutoSize = True
        LabelArmaRace.Location = New Point(3, 36)
        LabelArmaRace.Name = "LabelArmaRace"
        LabelArmaRace.Size = New Size(37, 15)
        LabelArmaRace.TabIndex = 2
        LabelArmaRace.Text = "Race"
        '
        ' ComboArmaRace
        '
        ComboArmaRace.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ComboArmaRace.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaRace.Location = New Point(120, 32)
        ComboArmaRace.Name = "ComboArmaRace"
        ComboArmaRace.Size = New Size(619, 23)
        ComboArmaRace.TabIndex = 3
        '
        ' LabelArmaSlots
        '
        LabelArmaSlots.Anchor = AnchorStyles.Left
        LabelArmaSlots.AutoSize = True
        LabelArmaSlots.Location = New Point(3, 61)
        LabelArmaSlots.Name = "LabelArmaSlots"
        LabelArmaSlots.Size = New Size(64, 15)
        LabelArmaSlots.TabIndex = 4
        LabelArmaSlots.Text = "Slots (BOD2)"
        '
        ' FlowArmaSlots
        '
        FlowArmaSlots.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        FlowArmaSlots.AutoSize = True
        FlowArmaSlots.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FlowArmaSlots.Location = New Point(120, 61)
        FlowArmaSlots.Name = "FlowArmaSlots"
        FlowArmaSlots.Size = New Size(619, 10)
        FlowArmaSlots.TabIndex = 5
        '
        ' LabelArmaAddRaces
        '
        LabelArmaAddRaces.Anchor = AnchorStyles.Left
        LabelArmaAddRaces.AutoSize = True
        LabelArmaAddRaces.Location = New Point(3, 84)
        LabelArmaAddRaces.Name = "LabelArmaAddRaces"
        LabelArmaAddRaces.Size = New Size(102, 15)
        LabelArmaAddRaces.TabIndex = 6
        LabelArmaAddRaces.Text = "Additional Races"
        '
        ' ArmaAddRacesRow
        '
        ArmaAddRacesRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaAddRacesRow.AutoSize = True
        ArmaAddRacesRow.Controls.Add(ComboArmaAddRace)
        ArmaAddRacesRow.Controls.Add(ButtonArmaAddRace)
        ArmaAddRacesRow.Controls.Add(ButtonArmaRemoveRace)
        ArmaAddRacesRow.Location = New Point(120, 81)
        ArmaAddRacesRow.Margin = New Padding(3)
        ArmaAddRacesRow.Name = "ArmaAddRacesRow"
        ArmaAddRacesRow.Size = New Size(619, 31)
        ArmaAddRacesRow.TabIndex = 7
        ArmaAddRacesRow.WrapContents = False
        '
        ' ComboArmaAddRace
        '
        ComboArmaAddRace.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaAddRace.Location = New Point(3, 3)
        ComboArmaAddRace.Name = "ComboArmaAddRace"
        ComboArmaAddRace.Size = New Size(360, 23)
        ComboArmaAddRace.TabIndex = 0
        '
        ' ButtonArmaAddRace
        '
        ButtonArmaAddRace.Location = New Point(369, 3)
        ButtonArmaAddRace.Name = "ButtonArmaAddRace"
        ButtonArmaAddRace.Size = New Size(60, 23)
        ButtonArmaAddRace.TabIndex = 1
        ButtonArmaAddRace.Text = "Add"
        ButtonArmaAddRace.UseVisualStyleBackColor = True
        '
        ' ButtonArmaRemoveRace
        '
        ButtonArmaRemoveRace.Location = New Point(435, 3)
        ButtonArmaRemoveRace.Name = "ButtonArmaRemoveRace"
        ButtonArmaRemoveRace.Size = New Size(70, 23)
        ButtonArmaRemoveRace.TabIndex = 2
        ButtonArmaRemoveRace.Text = "Remove"
        ButtonArmaRemoveRace.UseVisualStyleBackColor = True
        '
        ' ListViewArmaAddRaces
        '
        ListViewArmaAddRaces.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ListViewArmaAddRaces.Columns.AddRange(New ColumnHeader() {ColArmaRaceName})
        ListViewArmaAddRaces.FullRowSelect = True
        ListViewArmaAddRaces.Location = New Point(120, 118)
        ListViewArmaAddRaces.MultiSelect = False
        ListViewArmaAddRaces.Name = "ListViewArmaAddRaces"
        ListViewArmaAddRaces.Size = New Size(619, 55)
        ListViewArmaAddRaces.TabIndex = 8
        ListViewArmaAddRaces.UseCompatibleStateImageBehavior = False
        ListViewArmaAddRaces.View = View.Details
        '
        ' ColArmaRaceName
        '
        ColArmaRaceName.Text = "Race"
        ColArmaRaceName.Width = 590
        '
        ' ArmaMeshMaleRow
        '
        ArmaMeshMaleRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaMeshMaleRow.Controls.Add(LabelArmaMeshMale)
        ArmaMeshMaleRow.Controls.Add(TextBoxArmaMeshMale)
        ArmaMeshMaleRow.Controls.Add(ButtonArmaBrowseMeshMale)
        ArmaMeshMaleRow.Dock = DockStyle.Fill
        ArmaMeshMaleRow.Location = New Point(120, 179)
        ArmaMeshMaleRow.Margin = New Padding(3)
        ArmaMeshMaleRow.Name = "ArmaMeshMaleRow"
        ArmaMeshMaleRow.Size = New Size(619, 29)
        ArmaMeshMaleRow.TabIndex = 9
        ArmaMeshMaleRow.WrapContents = False
        '
        ' LabelArmaMeshMale
        '
        LabelArmaMeshMale.Anchor = AnchorStyles.Left
        LabelArmaMeshMale.AutoSize = True
        LabelArmaMeshMale.Location = New Point(3, 7)
        LabelArmaMeshMale.Name = "LabelArmaMeshMale"
        LabelArmaMeshMale.Size = New Size(110, 15)
        LabelArmaMeshMale.TabIndex = 0
        LabelArmaMeshMale.Text = "Male mesh (MOD2)"
        '
        ' TextBoxArmaMeshMale
        '
        TextBoxArmaMeshMale.Location = New Point(119, 3)
        TextBoxArmaMeshMale.Name = "TextBoxArmaMeshMale"
        TextBoxArmaMeshMale.Size = New Size(410, 23)
        TextBoxArmaMeshMale.TabIndex = 1
        '
        ' ButtonArmaBrowseMeshMale
        '
        ButtonArmaBrowseMeshMale.Location = New Point(535, 3)
        ButtonArmaBrowseMeshMale.Name = "ButtonArmaBrowseMeshMale"
        ButtonArmaBrowseMeshMale.Size = New Size(75, 23)
        ButtonArmaBrowseMeshMale.TabIndex = 2
        ButtonArmaBrowseMeshMale.Text = "Browse…"
        ButtonArmaBrowseMeshMale.UseVisualStyleBackColor = True
        '
        ' ArmaMeshFemaleRow
        '
        ArmaMeshFemaleRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaMeshFemaleRow.Controls.Add(LabelArmaMeshFemale)
        ArmaMeshFemaleRow.Controls.Add(TextBoxArmaMeshFemale)
        ArmaMeshFemaleRow.Controls.Add(ButtonArmaBrowseMeshFemale)
        ArmaMeshFemaleRow.Dock = DockStyle.Fill
        ArmaMeshFemaleRow.Location = New Point(120, 214)
        ArmaMeshFemaleRow.Margin = New Padding(3)
        ArmaMeshFemaleRow.Name = "ArmaMeshFemaleRow"
        ArmaMeshFemaleRow.Size = New Size(619, 29)
        ArmaMeshFemaleRow.TabIndex = 10
        ArmaMeshFemaleRow.WrapContents = False
        '
        ' LabelArmaMeshFemale
        '
        LabelArmaMeshFemale.Anchor = AnchorStyles.Left
        LabelArmaMeshFemale.AutoSize = True
        LabelArmaMeshFemale.Location = New Point(3, 7)
        LabelArmaMeshFemale.Name = "LabelArmaMeshFemale"
        LabelArmaMeshFemale.Size = New Size(110, 15)
        LabelArmaMeshFemale.TabIndex = 0
        LabelArmaMeshFemale.Text = "Female mesh (MOD3)"
        '
        ' TextBoxArmaMeshFemale
        '
        TextBoxArmaMeshFemale.Location = New Point(119, 3)
        TextBoxArmaMeshFemale.Name = "TextBoxArmaMeshFemale"
        TextBoxArmaMeshFemale.Size = New Size(410, 23)
        TextBoxArmaMeshFemale.TabIndex = 1
        '
        ' ButtonArmaBrowseMeshFemale
        '
        ButtonArmaBrowseMeshFemale.Location = New Point(535, 3)
        ButtonArmaBrowseMeshFemale.Name = "ButtonArmaBrowseMeshFemale"
        ButtonArmaBrowseMeshFemale.Size = New Size(75, 23)
        ButtonArmaBrowseMeshFemale.TabIndex = 2
        ButtonArmaBrowseMeshFemale.Text = "Browse…"
        ButtonArmaBrowseMeshFemale.UseVisualStyleBackColor = True
        '
        ' ArmaMeshMaleFpRow
        '
        ArmaMeshMaleFpRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaMeshMaleFpRow.Controls.Add(LabelArmaMeshMaleFp)
        ArmaMeshMaleFpRow.Controls.Add(TextBoxArmaMeshMaleFp)
        ArmaMeshMaleFpRow.Controls.Add(ButtonArmaBrowseMeshMaleFp)
        ArmaMeshMaleFpRow.Dock = DockStyle.Fill
        ArmaMeshMaleFpRow.Location = New Point(120, 249)
        ArmaMeshMaleFpRow.Margin = New Padding(3)
        ArmaMeshMaleFpRow.Name = "ArmaMeshMaleFpRow"
        ArmaMeshMaleFpRow.Size = New Size(619, 29)
        ArmaMeshMaleFpRow.TabIndex = 11
        ArmaMeshMaleFpRow.WrapContents = False
        '
        ' LabelArmaMeshMaleFp
        '
        LabelArmaMeshMaleFp.Anchor = AnchorStyles.Left
        LabelArmaMeshMaleFp.AutoSize = True
        LabelArmaMeshMaleFp.Location = New Point(3, 7)
        LabelArmaMeshMaleFp.Name = "LabelArmaMeshMaleFp"
        LabelArmaMeshMaleFp.Size = New Size(110, 15)
        LabelArmaMeshMaleFp.TabIndex = 0
        LabelArmaMeshMaleFp.Text = "Male 1st-p (MOD4)"
        '
        ' TextBoxArmaMeshMaleFp
        '
        TextBoxArmaMeshMaleFp.Location = New Point(119, 3)
        TextBoxArmaMeshMaleFp.Name = "TextBoxArmaMeshMaleFp"
        TextBoxArmaMeshMaleFp.Size = New Size(410, 23)
        TextBoxArmaMeshMaleFp.TabIndex = 1
        '
        ' ButtonArmaBrowseMeshMaleFp
        '
        ButtonArmaBrowseMeshMaleFp.Location = New Point(535, 3)
        ButtonArmaBrowseMeshMaleFp.Name = "ButtonArmaBrowseMeshMaleFp"
        ButtonArmaBrowseMeshMaleFp.Size = New Size(75, 23)
        ButtonArmaBrowseMeshMaleFp.TabIndex = 2
        ButtonArmaBrowseMeshMaleFp.Text = "Browse…"
        ButtonArmaBrowseMeshMaleFp.UseVisualStyleBackColor = True
        '
        ' ArmaMeshFemaleFpRow
        '
        ArmaMeshFemaleFpRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaMeshFemaleFpRow.Controls.Add(LabelArmaMeshFemaleFp)
        ArmaMeshFemaleFpRow.Controls.Add(TextBoxArmaMeshFemaleFp)
        ArmaMeshFemaleFpRow.Controls.Add(ButtonArmaBrowseMeshFemaleFp)
        ArmaMeshFemaleFpRow.Dock = DockStyle.Fill
        ArmaMeshFemaleFpRow.Location = New Point(120, 284)
        ArmaMeshFemaleFpRow.Margin = New Padding(3)
        ArmaMeshFemaleFpRow.Name = "ArmaMeshFemaleFpRow"
        ArmaMeshFemaleFpRow.Size = New Size(619, 29)
        ArmaMeshFemaleFpRow.TabIndex = 12
        ArmaMeshFemaleFpRow.WrapContents = False
        '
        ' LabelArmaMeshFemaleFp
        '
        LabelArmaMeshFemaleFp.Anchor = AnchorStyles.Left
        LabelArmaMeshFemaleFp.AutoSize = True
        LabelArmaMeshFemaleFp.Location = New Point(3, 7)
        LabelArmaMeshFemaleFp.Name = "LabelArmaMeshFemaleFp"
        LabelArmaMeshFemaleFp.Size = New Size(110, 15)
        LabelArmaMeshFemaleFp.TabIndex = 0
        LabelArmaMeshFemaleFp.Text = "Female 1st-p (MOD5)"
        '
        ' TextBoxArmaMeshFemaleFp
        '
        TextBoxArmaMeshFemaleFp.Location = New Point(119, 3)
        TextBoxArmaMeshFemaleFp.Name = "TextBoxArmaMeshFemaleFp"
        TextBoxArmaMeshFemaleFp.Size = New Size(410, 23)
        TextBoxArmaMeshFemaleFp.TabIndex = 1
        '
        ' ButtonArmaBrowseMeshFemaleFp
        '
        ButtonArmaBrowseMeshFemaleFp.Location = New Point(535, 3)
        ButtonArmaBrowseMeshFemaleFp.Name = "ButtonArmaBrowseMeshFemaleFp"
        ButtonArmaBrowseMeshFemaleFp.Size = New Size(75, 23)
        ButtonArmaBrowseMeshFemaleFp.TabIndex = 2
        ButtonArmaBrowseMeshFemaleFp.Text = "Browse…"
        ButtonArmaBrowseMeshFemaleFp.UseVisualStyleBackColor = True
        '
        ' ArmaFlagsRow
        '
        ArmaFlagsRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaFlagsRow.AutoSize = True
        ArmaFlagsRow.Controls.Add(CheckArmaMaleFaceBones)
        ArmaFlagsRow.Controls.Add(CheckArmaMale1stPerson)
        ArmaFlagsRow.Controls.Add(CheckArmaFemaleFaceBones)
        ArmaFlagsRow.Controls.Add(CheckArmaFemale1stPerson)
        ArmaFlagsRow.Location = New Point(120, 319)
        ArmaFlagsRow.Margin = New Padding(3)
        ArmaFlagsRow.Name = "ArmaFlagsRow"
        ArmaFlagsRow.Size = New Size(619, 25)
        ArmaFlagsRow.TabIndex = 13
        ArmaFlagsRow.WrapContents = True
        '
        ' CheckArmaMaleFaceBones
        '
        CheckArmaMaleFaceBones.AutoSize = True
        CheckArmaMaleFaceBones.Location = New Point(3, 3)
        CheckArmaMaleFaceBones.Name = "CheckArmaMaleFaceBones"
        CheckArmaMaleFaceBones.Size = New Size(135, 19)
        CheckArmaMaleFaceBones.TabIndex = 0
        CheckArmaMaleFaceBones.Text = "Male FaceBones"
        CheckArmaMaleFaceBones.UseVisualStyleBackColor = True
        '
        ' CheckArmaMale1stPerson
        '
        CheckArmaMale1stPerson.AutoSize = True
        CheckArmaMale1stPerson.Location = New Point(144, 3)
        CheckArmaMale1stPerson.Name = "CheckArmaMale1stPerson"
        CheckArmaMale1stPerson.Size = New Size(135, 19)
        CheckArmaMale1stPerson.TabIndex = 1
        CheckArmaMale1stPerson.Text = "Male 1stPerson"
        CheckArmaMale1stPerson.UseVisualStyleBackColor = True
        '
        ' CheckArmaFemaleFaceBones
        '
        CheckArmaFemaleFaceBones.AutoSize = True
        CheckArmaFemaleFaceBones.Location = New Point(285, 3)
        CheckArmaFemaleFaceBones.Name = "CheckArmaFemaleFaceBones"
        CheckArmaFemaleFaceBones.Size = New Size(145, 19)
        CheckArmaFemaleFaceBones.TabIndex = 2
        CheckArmaFemaleFaceBones.Text = "Female FaceBones"
        CheckArmaFemaleFaceBones.UseVisualStyleBackColor = True
        '
        ' CheckArmaFemale1stPerson
        '
        CheckArmaFemale1stPerson.AutoSize = True
        CheckArmaFemale1stPerson.Location = New Point(436, 3)
        CheckArmaFemale1stPerson.Name = "CheckArmaFemale1stPerson"
        CheckArmaFemale1stPerson.Size = New Size(145, 19)
        CheckArmaFemale1stPerson.TabIndex = 3
        CheckArmaFemale1stPerson.Text = "Female 1stPerson"
        CheckArmaFemale1stPerson.UseVisualStyleBackColor = True
        '
        ' ArmaTxstRow
        '
        ArmaTxstRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaTxstRow.AutoSize = True
        ArmaTxstRow.Controls.Add(LabelArmaTxstMale)
        ArmaTxstRow.Controls.Add(ComboArmaTxstMale)
        ArmaTxstRow.Controls.Add(LabelArmaTxstFemale)
        ArmaTxstRow.Controls.Add(ComboArmaTxstFemale)
        ArmaTxstRow.Location = New Point(120, 350)
        ArmaTxstRow.Margin = New Padding(3)
        ArmaTxstRow.Name = "ArmaTxstRow"
        ArmaTxstRow.Size = New Size(619, 31)
        ArmaTxstRow.TabIndex = 14
        ArmaTxstRow.WrapContents = False
        '
        ' LabelArmaTxstMale
        '
        LabelArmaTxstMale.Anchor = AnchorStyles.Left
        LabelArmaTxstMale.AutoSize = True
        LabelArmaTxstMale.Location = New Point(3, 7)
        LabelArmaTxstMale.Name = "LabelArmaTxstMale"
        LabelArmaTxstMale.Size = New Size(110, 15)
        LabelArmaTxstMale.TabIndex = 0
        LabelArmaTxstMale.Text = "Male skin TXST"
        '
        ' ComboArmaTxstMale
        '
        ComboArmaTxstMale.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaTxstMale.Location = New Point(119, 3)
        ComboArmaTxstMale.Name = "ComboArmaTxstMale"
        ComboArmaTxstMale.Size = New Size(170, 23)
        ComboArmaTxstMale.TabIndex = 1
        '
        ' LabelArmaTxstFemale
        '
        LabelArmaTxstFemale.Anchor = AnchorStyles.Left
        LabelArmaTxstFemale.AutoSize = True
        LabelArmaTxstFemale.Location = New Point(295, 7)
        LabelArmaTxstFemale.Name = "LabelArmaTxstFemale"
        LabelArmaTxstFemale.Size = New Size(110, 15)
        LabelArmaTxstFemale.TabIndex = 2
        LabelArmaTxstFemale.Text = "Female skin TXST"
        '
        ' ComboArmaTxstFemale
        '
        ComboArmaTxstFemale.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaTxstFemale.Location = New Point(411, 3)
        ComboArmaTxstFemale.Name = "ComboArmaTxstFemale"
        ComboArmaTxstFemale.Size = New Size(170, 23)
        ComboArmaTxstFemale.TabIndex = 3
        '
        ' ArmaMswpRow
        '
        ArmaMswpRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaMswpRow.AutoSize = True
        ArmaMswpRow.Controls.Add(LabelArmaMswpMale)
        ArmaMswpRow.Controls.Add(ComboArmaMswpMale)
        ArmaMswpRow.Controls.Add(LabelArmaMswpFemale)
        ArmaMswpRow.Controls.Add(ComboArmaMswpFemale)
        ArmaMswpRow.Controls.Add(ButtonArmaNewMswp)
        ArmaMswpRow.Location = New Point(120, 387)
        ArmaMswpRow.Margin = New Padding(3)
        ArmaMswpRow.Name = "ArmaMswpRow"
        ArmaMswpRow.Size = New Size(619, 31)
        ArmaMswpRow.TabIndex = 15
        ArmaMswpRow.WrapContents = False
        '
        ' LabelArmaMswpMale
        '
        LabelArmaMswpMale.Anchor = AnchorStyles.Left
        LabelArmaMswpMale.AutoSize = True
        LabelArmaMswpMale.Location = New Point(3, 7)
        LabelArmaMswpMale.Name = "LabelArmaMswpMale"
        LabelArmaMswpMale.Size = New Size(80, 15)
        LabelArmaMswpMale.TabIndex = 0
        LabelArmaMswpMale.Text = "Male MSWP"
        '
        ' ComboArmaMswpMale
        '
        ComboArmaMswpMale.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaMswpMale.Location = New Point(89, 3)
        ComboArmaMswpMale.Name = "ComboArmaMswpMale"
        ComboArmaMswpMale.Size = New Size(180, 23)
        ComboArmaMswpMale.TabIndex = 1
        '
        ' LabelArmaMswpFemale
        '
        LabelArmaMswpFemale.Anchor = AnchorStyles.Left
        LabelArmaMswpFemale.AutoSize = True
        LabelArmaMswpFemale.Location = New Point(275, 7)
        LabelArmaMswpFemale.Name = "LabelArmaMswpFemale"
        LabelArmaMswpFemale.Size = New Size(90, 15)
        LabelArmaMswpFemale.TabIndex = 2
        LabelArmaMswpFemale.Text = "Female MSWP"
        '
        ' ComboArmaMswpFemale
        '
        ComboArmaMswpFemale.DropDownStyle = ComboBoxStyle.DropDownList
        ComboArmaMswpFemale.Location = New Point(371, 3)
        ComboArmaMswpFemale.Name = "ComboArmaMswpFemale"
        ComboArmaMswpFemale.Size = New Size(180, 23)
        ComboArmaMswpFemale.TabIndex = 3
        '
        ' ButtonArmaNewMswp
        '
        ButtonArmaNewMswp.Location = New Point(557, 3)
        ButtonArmaNewMswp.Name = "ButtonArmaNewMswp"
        ButtonArmaNewMswp.Size = New Size(55, 25)
        ButtonArmaNewMswp.TabIndex = 4
        ButtonArmaNewMswp.Text = "New…"
        ButtonArmaNewMswp.UseVisualStyleBackColor = True
        '
        ' ArmaDnamRow
        '
        ArmaDnamRow.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaDnamRow.AutoSize = True
        ArmaDnamRow.Controls.Add(LabelArmaMalePrio)
        ArmaDnamRow.Controls.Add(NumArmaMalePrio)
        ArmaDnamRow.Controls.Add(LabelArmaFemalePrio)
        ArmaDnamRow.Controls.Add(NumArmaFemalePrio)
        ArmaDnamRow.Controls.Add(CheckArmaMaleWeightEnabled)
        ArmaDnamRow.Controls.Add(CheckArmaFemaleWeightEnabled)
        ArmaDnamRow.Location = New Point(120, 424)
        ArmaDnamRow.Margin = New Padding(3)
        ArmaDnamRow.Name = "ArmaDnamRow"
        ArmaDnamRow.Size = New Size(619, 31)
        ArmaDnamRow.TabIndex = 16
        ArmaDnamRow.WrapContents = False
        '
        ' LabelArmaMalePrio
        '
        LabelArmaMalePrio.Anchor = AnchorStyles.Left
        LabelArmaMalePrio.AutoSize = True
        LabelArmaMalePrio.Location = New Point(3, 7)
        LabelArmaMalePrio.Name = "LabelArmaMalePrio"
        LabelArmaMalePrio.Size = New Size(70, 15)
        LabelArmaMalePrio.TabIndex = 0
        LabelArmaMalePrio.Text = "Male prio"
        '
        ' NumArmaMalePrio
        '
        NumArmaMalePrio.Location = New Point(79, 3)
        NumArmaMalePrio.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumArmaMalePrio.Name = "NumArmaMalePrio"
        NumArmaMalePrio.Size = New Size(60, 23)
        NumArmaMalePrio.TabIndex = 1
        '
        ' LabelArmaFemalePrio
        '
        LabelArmaFemalePrio.Anchor = AnchorStyles.Left
        LabelArmaFemalePrio.AutoSize = True
        LabelArmaFemalePrio.Location = New Point(145, 7)
        LabelArmaFemalePrio.Name = "LabelArmaFemalePrio"
        LabelArmaFemalePrio.Size = New Size(80, 15)
        LabelArmaFemalePrio.TabIndex = 2
        LabelArmaFemalePrio.Text = "Female prio"
        '
        ' NumArmaFemalePrio
        '
        NumArmaFemalePrio.Location = New Point(231, 3)
        NumArmaFemalePrio.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumArmaFemalePrio.Name = "NumArmaFemalePrio"
        NumArmaFemalePrio.Size = New Size(60, 23)
        NumArmaFemalePrio.TabIndex = 3
        '
        ' CheckArmaMaleWeightEnabled
        '
        CheckArmaMaleWeightEnabled.AutoSize = True
        CheckArmaMaleWeightEnabled.Location = New Point(297, 5)
        CheckArmaMaleWeightEnabled.Name = "CheckArmaMaleWeightEnabled"
        CheckArmaMaleWeightEnabled.Size = New Size(120, 19)
        CheckArmaMaleWeightEnabled.TabIndex = 4
        CheckArmaMaleWeightEnabled.Text = "Male weight slider"
        CheckArmaMaleWeightEnabled.UseVisualStyleBackColor = True
        '
        ' CheckArmaFemaleWeightEnabled
        '
        CheckArmaFemaleWeightEnabled.AutoSize = True
        CheckArmaFemaleWeightEnabled.Location = New Point(423, 5)
        CheckArmaFemaleWeightEnabled.Name = "CheckArmaFemaleWeightEnabled"
        CheckArmaFemaleWeightEnabled.Size = New Size(130, 19)
        CheckArmaFemaleWeightEnabled.TabIndex = 5
        CheckArmaFemaleWeightEnabled.Text = "Female weight slider"
        CheckArmaFemaleWeightEnabled.UseVisualStyleBackColor = True
        '
        ' ArmaDnam2Row
        '
        ArmaDnam2Row.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        ArmaDnam2Row.AutoSize = True
        ArmaDnam2Row.Controls.Add(LabelArmaDetSound)
        ArmaDnam2Row.Controls.Add(NumArmaDetSound)
        ArmaDnam2Row.Controls.Add(LabelArmaWeaponAdjust)
        ArmaDnam2Row.Controls.Add(NumArmaWeaponAdjust)
        ArmaDnam2Row.Location = New Point(120, 461)
        ArmaDnam2Row.Margin = New Padding(3)
        ArmaDnam2Row.Name = "ArmaDnam2Row"
        ArmaDnam2Row.Size = New Size(619, 31)
        ArmaDnam2Row.TabIndex = 17
        ArmaDnam2Row.WrapContents = False
        '
        ' LabelArmaDetSound
        '
        LabelArmaDetSound.Anchor = AnchorStyles.Left
        LabelArmaDetSound.AutoSize = True
        LabelArmaDetSound.Location = New Point(3, 7)
        LabelArmaDetSound.Name = "LabelArmaDetSound"
        LabelArmaDetSound.Size = New Size(95, 15)
        LabelArmaDetSound.TabIndex = 0
        LabelArmaDetSound.Text = "Detection sound"
        '
        ' NumArmaDetSound
        '
        NumArmaDetSound.Location = New Point(104, 3)
        NumArmaDetSound.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumArmaDetSound.Name = "NumArmaDetSound"
        NumArmaDetSound.Size = New Size(60, 23)
        NumArmaDetSound.TabIndex = 1
        '
        ' LabelArmaWeaponAdjust
        '
        LabelArmaWeaponAdjust.Anchor = AnchorStyles.Left
        LabelArmaWeaponAdjust.AutoSize = True
        LabelArmaWeaponAdjust.Location = New Point(170, 7)
        LabelArmaWeaponAdjust.Name = "LabelArmaWeaponAdjust"
        LabelArmaWeaponAdjust.Size = New Size(90, 15)
        LabelArmaWeaponAdjust.TabIndex = 2
        LabelArmaWeaponAdjust.Text = "Weapon adjust"
        '
        ' NumArmaWeaponAdjust
        '
        NumArmaWeaponAdjust.DecimalPlaces = 3
        NumArmaWeaponAdjust.Location = New Point(266, 3)
        NumArmaWeaponAdjust.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        NumArmaWeaponAdjust.Minimum = New Decimal(New Integer() {100000, 0, 0, -2147483648})
        NumArmaWeaponAdjust.Name = "NumArmaWeaponAdjust"
        NumArmaWeaponAdjust.Size = New Size(90, 23)
        NumArmaWeaponAdjust.TabIndex = 3
        '
        ' LabelArmaBoneScaleTodo
        '
        LabelArmaBoneScaleTodo.Anchor = AnchorStyles.Left
        LabelArmaBoneScaleTodo.AutoSize = True
        LabelArmaBoneScaleTodo.ForeColor = Color.DimGray
        LabelArmaBoneScaleTodo.Location = New Point(120, 495)
        LabelArmaBoneScaleTodo.Name = "LabelArmaBoneScaleTodo"
        LabelArmaBoneScaleTodo.Size = New Size(300, 15)
        LabelArmaBoneScaleTodo.TabIndex = 18
        LabelArmaBoneScaleTodo.Text = "Bone-scale (BSMP/BSMB/BSMS): editing TODO — existing values preserved on override."
        '
        ' GroupBoxMswp
        '
        GroupBoxMswp.Controls.Add(MswpLayout)
        GroupBoxMswp.Dock = DockStyle.Fill
        GroupBoxMswp.Location = New Point(0, 0)
        GroupBoxMswp.Name = "GroupBoxMswp"
        GroupBoxMswp.Padding = New Padding(6)
        GroupBoxMswp.Size = New Size(754, 421)
        GroupBoxMswp.TabIndex = 2
        GroupBoxMswp.TabStop = False
        GroupBoxMswp.Text = "MSWP (material swap)"
        GroupBoxMswp.Visible = False
        '
        ' MswpLayout
        '
        MswpLayout.ColumnCount = 2
        MswpLayout.ColumnStyles.Add(New ColumnStyle())
        MswpLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        MswpLayout.Controls.Add(LabelMswpEdid, 0, 0)
        MswpLayout.Controls.Add(TextBoxMswpEdid, 1, 0)
        MswpLayout.Controls.Add(LabelMswpTreeFolder, 0, 1)
        MswpLayout.Controls.Add(TextBoxMswpTreeFolder, 1, 1)
        MswpLayout.Controls.Add(MswpGridButtonsRow, 1, 2)
        MswpLayout.Controls.Add(GridMswp, 1, 3)
        MswpLayout.Dock = DockStyle.Fill
        MswpLayout.Location = New Point(6, 22)
        MswpLayout.Name = "MswpLayout"
        MswpLayout.RowCount = 4
        MswpLayout.RowStyles.Add(New RowStyle())
        MswpLayout.RowStyles.Add(New RowStyle())
        MswpLayout.RowStyles.Add(New RowStyle())
        MswpLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        MswpLayout.Size = New Size(742, 393)
        MswpLayout.TabIndex = 0
        '
        ' LabelMswpEdid
        '
        LabelMswpEdid.Anchor = AnchorStyles.Left
        LabelMswpEdid.AutoSize = True
        LabelMswpEdid.Location = New Point(3, 7)
        LabelMswpEdid.Name = "LabelMswpEdid"
        LabelMswpEdid.Size = New Size(46, 15)
        LabelMswpEdid.TabIndex = 0
        LabelMswpEdid.Text = "EditorID"
        '
        ' TextBoxMswpEdid
        '
        TextBoxMswpEdid.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        TextBoxMswpEdid.Location = New Point(90, 3)
        TextBoxMswpEdid.Name = "TextBoxMswpEdid"
        TextBoxMswpEdid.Size = New Size(649, 23)
        TextBoxMswpEdid.TabIndex = 1
        '
        ' LabelMswpTreeFolder
        '
        LabelMswpTreeFolder.Anchor = AnchorStyles.Left
        LabelMswpTreeFolder.AutoSize = True
        LabelMswpTreeFolder.Location = New Point(3, 36)
        LabelMswpTreeFolder.Name = "LabelMswpTreeFolder"
        LabelMswpTreeFolder.Size = New Size(67, 15)
        LabelMswpTreeFolder.TabIndex = 2
        LabelMswpTreeFolder.Text = "Tree Folder"
        '
        ' TextBoxMswpTreeFolder
        '
        TextBoxMswpTreeFolder.Anchor = CType(AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles)
        TextBoxMswpTreeFolder.Location = New Point(90, 32)
        TextBoxMswpTreeFolder.Name = "TextBoxMswpTreeFolder"
        TextBoxMswpTreeFolder.Size = New Size(649, 23)
        TextBoxMswpTreeFolder.TabIndex = 3
        '
        ' MswpGridButtonsRow
        '
        MswpGridButtonsRow.AutoSize = True
        MswpGridButtonsRow.Controls.Add(ButtonMswpAddRow)
        MswpGridButtonsRow.Controls.Add(ButtonMswpRemoveRow)
        MswpGridButtonsRow.Controls.Add(ButtonMswpBrowseOriginal)
        MswpGridButtonsRow.Controls.Add(ButtonMswpBrowseReplacement)
        MswpGridButtonsRow.Location = New Point(90, 61)
        MswpGridButtonsRow.Margin = New Padding(3)
        MswpGridButtonsRow.Name = "MswpGridButtonsRow"
        MswpGridButtonsRow.Size = New Size(649, 31)
        MswpGridButtonsRow.TabIndex = 4
        MswpGridButtonsRow.WrapContents = True
        '
        ' ButtonMswpAddRow
        '
        ButtonMswpAddRow.AutoSize = True
        ButtonMswpAddRow.Location = New Point(3, 3)
        ButtonMswpAddRow.Name = "ButtonMswpAddRow"
        ButtonMswpAddRow.Size = New Size(80, 25)
        ButtonMswpAddRow.TabIndex = 0
        ButtonMswpAddRow.Text = "Add row"
        ButtonMswpAddRow.UseVisualStyleBackColor = True
        '
        ' ButtonMswpRemoveRow
        '
        ButtonMswpRemoveRow.AutoSize = True
        ButtonMswpRemoveRow.Location = New Point(89, 3)
        ButtonMswpRemoveRow.Name = "ButtonMswpRemoveRow"
        ButtonMswpRemoveRow.Size = New Size(90, 25)
        ButtonMswpRemoveRow.TabIndex = 1
        ButtonMswpRemoveRow.Text = "Remove row"
        ButtonMswpRemoveRow.UseVisualStyleBackColor = True
        '
        ' ButtonMswpBrowseOriginal
        '
        ButtonMswpBrowseOriginal.AutoSize = True
        ButtonMswpBrowseOriginal.Location = New Point(185, 3)
        ButtonMswpBrowseOriginal.Name = "ButtonMswpBrowseOriginal"
        ButtonMswpBrowseOriginal.Size = New Size(150, 25)
        ButtonMswpBrowseOriginal.TabIndex = 2
        ButtonMswpBrowseOriginal.Text = "Browse Original…"
        ButtonMswpBrowseOriginal.UseVisualStyleBackColor = True
        '
        ' ButtonMswpBrowseReplacement
        '
        ButtonMswpBrowseReplacement.AutoSize = True
        ButtonMswpBrowseReplacement.Location = New Point(341, 3)
        ButtonMswpBrowseReplacement.Name = "ButtonMswpBrowseReplacement"
        ButtonMswpBrowseReplacement.Size = New Size(170, 25)
        ButtonMswpBrowseReplacement.TabIndex = 3
        ButtonMswpBrowseReplacement.Text = "Browse Replacement…"
        ButtonMswpBrowseReplacement.UseVisualStyleBackColor = True
        '
        ' GridMswp
        '
        GridMswp.AllowUserToAddRows = False
        GridMswp.AllowUserToDeleteRows = False
        GridMswp.Anchor = CType(CType(AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right, AnchorStyles), AnchorStyles)
        GridMswp.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridMswp.Location = New Point(90, 98)
        GridMswp.MultiSelect = False
        GridMswp.Name = "GridMswp"
        GridMswp.RowHeadersWidth = 30
        GridMswp.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridMswp.Size = New Size(649, 292)
        GridMswp.TabIndex = 5
        '
        ' LabelNoSelection
        '
        LabelNoSelection.AutoSize = True
        LabelNoSelection.ForeColor = Color.DimGray
        LabelNoSelection.Location = New Point(8, 8)
        LabelNoSelection.Name = "LabelNoSelection"
        LabelNoSelection.Size = New Size(400, 15)
        LabelNoSelection.TabIndex = 3
        LabelNoSelection.Text = "Select a draft, or use New ARMO / New ARMA / New MSWP / Override existing…"
        '
        ' PreviewLayout
        '
        PreviewLayout.ColumnCount = 1
        PreviewLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PreviewLayout.Controls.Add(PreviewControlPanel, 0, 0)
        PreviewLayout.Controls.Add(LabelPreviewHint, 0, 1)
        PreviewLayout.Dock = DockStyle.Fill
        PreviewLayout.Location = New Point(0, 0)
        PreviewLayout.Name = "PreviewLayout"
        PreviewLayout.RowCount = 2
        PreviewLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PreviewLayout.RowStyles.Add(New RowStyle())
        PreviewLayout.Size = New Size(458, 678)
        PreviewLayout.TabIndex = 0
        '
        ' PreviewControlPanel
        '
        PreviewControlPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewControlPanel.Dock = DockStyle.Fill
        PreviewControlPanel.Location = New Point(0, 0)
        PreviewControlPanel.Margin = New Padding(0)
        PreviewControlPanel.Name = "PreviewControlPanel"
        PreviewControlPanel.Size = New Size(458, 655)
        PreviewControlPanel.TabIndex = 0
        '
        ' LabelPreviewHint
        '
        LabelPreviewHint.AutoSize = True
        LabelPreviewHint.ForeColor = Color.DimGray
        LabelPreviewHint.Location = New Point(3, 660)
        LabelPreviewHint.Margin = New Padding(3, 5, 3, 0)
        LabelPreviewHint.Name = "LabelPreviewHint"
        LabelPreviewHint.Size = New Size(0, 15)
        LabelPreviewHint.TabIndex = 1
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonClose)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 695)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(1222, 35)
        BottomLayout.TabIndex = 1
        '
        ' ButtonClose
        '
        ButtonClose.DialogResult = DialogResult.OK
        ButtonClose.Location = New Point(1139, 9)
        ButtonClose.Name = "ButtonClose"
        ButtonClose.Size = New Size(80, 23)
        ButtonClose.TabIndex = 0
        ButtonClose.Text = "Close"
        '
        ' ArmorEditor_Form
        '
        AcceptButton = ButtonClose
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1244, 741)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ArmorEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Armor Editor"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        MainSplit.Panel1.ResumeLayout(False)
        MainSplit.Panel2.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        LeftLayout.ResumeLayout(False)
        LeftLayout.PerformLayout()
        GroupBoxDrafts.ResumeLayout(False)
        DraftButtonsRow.ResumeLayout(False)
        DraftButtonsRow.PerformLayout()
        EditorHostPanel.ResumeLayout(False)
        EditorHostPanel.PerformLayout()
        GroupBoxArmo.ResumeLayout(False)
        ArmoLayout.ResumeLayout(False)
        ArmoLayout.PerformLayout()
        ArmoKeywordsRow.ResumeLayout(False)
        ArmoAddonsRow.ResumeLayout(False)
        ArmoAddonsRow.PerformLayout()
        CType(NumArmoAddonIndx, ComponentModel.ISupportInitialize).EndInit()
        ArmoDataRow.ResumeLayout(False)
        ArmoDataRow.PerformLayout()
        CType(NumArmoValue, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmoWeight, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmoHealth, ComponentModel.ISupportInitialize).EndInit()
        ArmoFnamRow.ResumeLayout(False)
        ArmoFnamRow.PerformLayout()
        CType(NumArmoRating, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmoBaseAddon, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmoStagger, ComponentModel.ISupportInitialize).EndInit()
        ArmoMswpRow.ResumeLayout(False)
        ArmoMswpRow.PerformLayout()
        ArmoTnamRow.ResumeLayout(False)
        ArmoTnamRow.PerformLayout()
        GroupBoxArma.ResumeLayout(False)
        ArmaLayout.ResumeLayout(False)
        ArmaLayout.PerformLayout()
        ArmaAddRacesRow.ResumeLayout(False)
        ArmaMeshMaleRow.ResumeLayout(False)
        ArmaMeshMaleRow.PerformLayout()
        ArmaMeshFemaleRow.ResumeLayout(False)
        ArmaMeshFemaleRow.PerformLayout()
        ArmaMeshMaleFpRow.ResumeLayout(False)
        ArmaMeshMaleFpRow.PerformLayout()
        ArmaMeshFemaleFpRow.ResumeLayout(False)
        ArmaMeshFemaleFpRow.PerformLayout()
        ArmaFlagsRow.ResumeLayout(False)
        ArmaFlagsRow.PerformLayout()
        ArmaTxstRow.ResumeLayout(False)
        ArmaTxstRow.PerformLayout()
        ArmaMswpRow.ResumeLayout(False)
        ArmaMswpRow.PerformLayout()
        ArmaDnamRow.ResumeLayout(False)
        ArmaDnamRow.PerformLayout()
        CType(NumArmaMalePrio, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmaFemalePrio, ComponentModel.ISupportInitialize).EndInit()
        ArmaDnam2Row.ResumeLayout(False)
        ArmaDnam2Row.PerformLayout()
        CType(NumArmaDetSound, ComponentModel.ISupportInitialize).EndInit()
        CType(NumArmaWeaponAdjust, ComponentModel.ISupportInitialize).EndInit()
        GroupBoxMswp.ResumeLayout(False)
        MswpLayout.ResumeLayout(False)
        MswpLayout.PerformLayout()
        MswpGridButtonsRow.ResumeLayout(False)
        MswpGridButtonsRow.PerformLayout()
        CType(GridMswp, ComponentModel.ISupportInitialize).EndInit()
        PreviewLayout.ResumeLayout(False)
        PreviewLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents MainSplit As System.Windows.Forms.SplitContainer
    Friend WithEvents LeftLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupBoxDrafts As System.Windows.Forms.GroupBox
    Friend WithEvents ListViewDrafts As System.Windows.Forms.ListView
    Friend WithEvents ColDraftName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColDraftType As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColDraftStatus As System.Windows.Forms.ColumnHeader
    Friend WithEvents DraftButtonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonNewArmo As System.Windows.Forms.Button
    Friend WithEvents ButtonNewArma As System.Windows.Forms.Button
    Friend WithEvents ButtonNewMswp As System.Windows.Forms.Button
    Friend WithEvents ButtonOverrideExisting As System.Windows.Forms.Button
    Friend WithEvents ButtonDeleteDraft As System.Windows.Forms.Button
    Friend WithEvents ButtonApply As System.Windows.Forms.Button
    Friend WithEvents EditorHostPanel As System.Windows.Forms.Panel
    Friend WithEvents GroupBoxArmo As System.Windows.Forms.GroupBox
    Friend WithEvents ArmoLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelArmoEdid As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmoEdid As System.Windows.Forms.TextBox
    Friend WithEvents LabelArmoFull As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmoFull As System.Windows.Forms.TextBox
    Friend WithEvents LabelArmoRace As System.Windows.Forms.Label
    Friend WithEvents ComboArmoRace As System.Windows.Forms.ComboBox
    Friend WithEvents LabelArmoSlots As System.Windows.Forms.Label
    Friend WithEvents FlowArmoSlots As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmoKeywords As System.Windows.Forms.Label
    Friend WithEvents ArmoKeywordsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ComboArmoKeyword As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonArmoAddKeyword As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoRemoveKeyword As System.Windows.Forms.Button
    Friend WithEvents ListViewArmoKeywords As System.Windows.Forms.ListView
    Friend WithEvents ColArmoKwName As System.Windows.Forms.ColumnHeader
    Friend WithEvents LabelArmoAddons As System.Windows.Forms.Label
    Friend WithEvents ArmoAddonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonArmoAddAddonExisting As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoAddAddonDraft As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoRemoveAddon As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoAddonUp As System.Windows.Forms.Button
    Friend WithEvents ButtonArmoAddonDown As System.Windows.Forms.Button
    Friend WithEvents LabelArmoAddonIndx As System.Windows.Forms.Label
    Friend WithEvents NumArmoAddonIndx As System.Windows.Forms.NumericUpDown
    Friend WithEvents ListViewArmoAddons As System.Windows.Forms.ListView
    Friend WithEvents ColArmoAddonIndx As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColArmoAddonArma As System.Windows.Forms.ColumnHeader
    Friend WithEvents ArmoDataRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmoValue As System.Windows.Forms.Label
    Friend WithEvents NumArmoValue As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmoWeight As System.Windows.Forms.Label
    Friend WithEvents NumArmoWeight As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmoHealth As System.Windows.Forms.Label
    Friend WithEvents NumArmoHealth As System.Windows.Forms.NumericUpDown
    Friend WithEvents ArmoFnamRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmoRating As System.Windows.Forms.Label
    Friend WithEvents NumArmoRating As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmoBaseAddon As System.Windows.Forms.Label
    Friend WithEvents NumArmoBaseAddon As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmoStagger As System.Windows.Forms.Label
    Friend WithEvents NumArmoStagger As System.Windows.Forms.NumericUpDown
    Friend WithEvents ArmoMswpRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmoMswp As System.Windows.Forms.Label
    Friend WithEvents ComboArmoMswp As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonArmoNewMswp As System.Windows.Forms.Button
    Friend WithEvents ArmoTnamRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmoTnam As System.Windows.Forms.Label
    Friend WithEvents ComboArmoTnam As System.Windows.Forms.ComboBox
    Friend WithEvents GroupBoxArma As System.Windows.Forms.GroupBox
    Friend WithEvents ArmaLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelArmaEdid As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmaEdid As System.Windows.Forms.TextBox
    Friend WithEvents LabelArmaRace As System.Windows.Forms.Label
    Friend WithEvents ComboArmaRace As System.Windows.Forms.ComboBox
    Friend WithEvents LabelArmaSlots As System.Windows.Forms.Label
    Friend WithEvents FlowArmaSlots As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaAddRaces As System.Windows.Forms.Label
    Friend WithEvents ArmaAddRacesRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ComboArmaAddRace As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonArmaAddRace As System.Windows.Forms.Button
    Friend WithEvents ButtonArmaRemoveRace As System.Windows.Forms.Button
    Friend WithEvents ListViewArmaAddRaces As System.Windows.Forms.ListView
    Friend WithEvents ColArmaRaceName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ArmaMeshMaleRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMeshMale As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmaMeshMale As System.Windows.Forms.TextBox
    Friend WithEvents ButtonArmaBrowseMeshMale As System.Windows.Forms.Button
    Friend WithEvents ArmaMeshFemaleRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMeshFemale As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmaMeshFemale As System.Windows.Forms.TextBox
    Friend WithEvents ButtonArmaBrowseMeshFemale As System.Windows.Forms.Button
    Friend WithEvents ArmaMeshMaleFpRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMeshMaleFp As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmaMeshMaleFp As System.Windows.Forms.TextBox
    Friend WithEvents ButtonArmaBrowseMeshMaleFp As System.Windows.Forms.Button
    Friend WithEvents ArmaMeshFemaleFpRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMeshFemaleFp As System.Windows.Forms.Label
    Friend WithEvents TextBoxArmaMeshFemaleFp As System.Windows.Forms.TextBox
    Friend WithEvents ButtonArmaBrowseMeshFemaleFp As System.Windows.Forms.Button
    Friend WithEvents ArmaFlagsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckArmaMaleFaceBones As System.Windows.Forms.CheckBox
    Friend WithEvents CheckArmaMale1stPerson As System.Windows.Forms.CheckBox
    Friend WithEvents CheckArmaFemaleFaceBones As System.Windows.Forms.CheckBox
    Friend WithEvents CheckArmaFemale1stPerson As System.Windows.Forms.CheckBox
    Friend WithEvents ArmaTxstRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaTxstMale As System.Windows.Forms.Label
    Friend WithEvents ComboArmaTxstMale As System.Windows.Forms.ComboBox
    Friend WithEvents LabelArmaTxstFemale As System.Windows.Forms.Label
    Friend WithEvents ComboArmaTxstFemale As System.Windows.Forms.ComboBox
    Friend WithEvents ArmaMswpRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMswpMale As System.Windows.Forms.Label
    Friend WithEvents ComboArmaMswpMale As System.Windows.Forms.ComboBox
    Friend WithEvents LabelArmaMswpFemale As System.Windows.Forms.Label
    Friend WithEvents ComboArmaMswpFemale As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonArmaNewMswp As System.Windows.Forms.Button
    Friend WithEvents ArmaDnamRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaMalePrio As System.Windows.Forms.Label
    Friend WithEvents NumArmaMalePrio As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmaFemalePrio As System.Windows.Forms.Label
    Friend WithEvents NumArmaFemalePrio As System.Windows.Forms.NumericUpDown
    Friend WithEvents CheckArmaMaleWeightEnabled As System.Windows.Forms.CheckBox
    Friend WithEvents CheckArmaFemaleWeightEnabled As System.Windows.Forms.CheckBox
    Friend WithEvents ArmaDnam2Row As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelArmaDetSound As System.Windows.Forms.Label
    Friend WithEvents NumArmaDetSound As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmaWeaponAdjust As System.Windows.Forms.Label
    Friend WithEvents NumArmaWeaponAdjust As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArmaBoneScaleTodo As System.Windows.Forms.Label
    Friend WithEvents GroupBoxMswp As System.Windows.Forms.GroupBox
    Friend WithEvents MswpLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMswpEdid As System.Windows.Forms.Label
    Friend WithEvents TextBoxMswpEdid As System.Windows.Forms.TextBox
    Friend WithEvents LabelMswpTreeFolder As System.Windows.Forms.Label
    Friend WithEvents TextBoxMswpTreeFolder As System.Windows.Forms.TextBox
    Friend WithEvents MswpGridButtonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonMswpAddRow As System.Windows.Forms.Button
    Friend WithEvents ButtonMswpRemoveRow As System.Windows.Forms.Button
    Friend WithEvents ButtonMswpBrowseOriginal As System.Windows.Forms.Button
    Friend WithEvents ButtonMswpBrowseReplacement As System.Windows.Forms.Button
    Friend WithEvents GridMswp As System.Windows.Forms.DataGridView
    Friend WithEvents LabelNoSelection As System.Windows.Forms.Label
    Friend WithEvents PreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents LabelPreviewHint As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonClose As System.Windows.Forms.Button
End Class
