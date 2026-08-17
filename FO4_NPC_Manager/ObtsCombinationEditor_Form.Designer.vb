' UI built in Designer per 00-reglas-ui-y-vb.md (sub-editor for a single OBTS combination,
' mirror of MswpSubEditor_Form). InitializeComponent is declarative ONLY — the Keywords/Includes/
' Properties DataGridView columns (variable/typed content, one of them a ValueType combo) are added
' in code-behind, exactly like MswpSubEditor's substitutions grid and ArmoEditor's addons grid.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ObtsCombinationEditor_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

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
        GroupScalars = New GroupBox()
        ScalarLayout = New TableLayoutPanel()
        LabelName = New Label()
        TextBoxName = New TextBox()
        CheckIsDefault = New CheckBox()
        CheckIsEditorOnly = New CheckBox()
        LabelParent = New Label()
        NumParent = New NumericUpDown()
        LabelLevelMin = New Label()
        NumLevelMin = New NumericUpDown()
        LabelLevelMax = New Label()
        NumLevelMax = New NumericUpDown()
        LabelMinLevelForRanks = New Label()
        NumMinLevelForRanks = New NumericUpDown()
        LabelAltLevelsPerTier = New Label()
        NumAltLevelsPerTier = New NumericUpDown()
        Tabs = New TabControl()
        TabKeywords = New TabPage()
        KeywordsLayout = New TableLayoutPanel()
        LabelKeywords = New Label()
        ListKeywords = New ListView()
        ColKeyword = New ColumnHeader()
        KeywordButtons = New FlowLayoutPanel()
        ButtonAddKeyword = New Button()
        ButtonRemoveKeyword = New Button()
        TabIncludes = New TabPage()
        IncludesLayout = New TableLayoutPanel()
        LabelIncludes = New Label()
        GridIncludes = New DataGridView()
        IncludeButtons = New FlowLayoutPanel()
        ButtonAddInclude = New Button()
        ButtonEditInclude = New Button()
        ButtonRemoveInclude = New Button()
        ButtonIncludeUp = New Button()
        ButtonIncludeDown = New Button()
        TabProperties = New TabPage()
        PropsLayout = New TableLayoutPanel()
        LabelProps = New Label()
        GridProperties = New DataGridView()
        PropButtons = New FlowLayoutPanel()
        ButtonAddProp = New Button()
        ButtonEditProp = New Button()
        ButtonRemoveProp = New Button()
        LabelPropsHint = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        GroupScalars.SuspendLayout()
        ScalarLayout.SuspendLayout()
        CType(NumParent, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumLevelMin, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumLevelMax, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumMinLevelForRanks, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumAltLevelsPerTier, ComponentModel.ISupportInitialize).BeginInit()
        Tabs.SuspendLayout()
        TabKeywords.SuspendLayout()
        KeywordsLayout.SuspendLayout()
        KeywordButtons.SuspendLayout()
        TabIncludes.SuspendLayout()
        IncludesLayout.SuspendLayout()
        CType(GridIncludes, ComponentModel.ISupportInitialize).BeginInit()
        IncludeButtons.SuspendLayout()
        TabProperties.SuspendLayout()
        PropsLayout.SuspendLayout()
        CType(GridProperties, ComponentModel.ISupportInitialize).BeginInit()
        PropButtons.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(GroupScalars, 0, 0)
        RootLayout.Controls.Add(Tabs, 0, 1)
        RootLayout.Controls.Add(BottomLayout, 0, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(900, 620)
        RootLayout.TabIndex = 0
        '
        ' GroupScalars
        '
        GroupScalars.AutoSize = True
        GroupScalars.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupScalars.Controls.Add(ScalarLayout)
        GroupScalars.Dock = DockStyle.Fill
        GroupScalars.Location = New Point(11, 11)
        GroupScalars.Name = "GroupScalars"
        GroupScalars.Padding = New Padding(4)
        GroupScalars.Size = New Size(878, 150)
        GroupScalars.TabIndex = 0
        GroupScalars.TabStop = False
        GroupScalars.Text = "Combination (name, flags, parent/addon index, level range)"
        '
        ' ScalarLayout
        '
        ScalarLayout.ColumnCount = 4
        ScalarLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        ScalarLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        ScalarLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        ScalarLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ScalarLayout.Controls.Add(LabelName, 0, 0)
        ScalarLayout.Controls.Add(TextBoxName, 1, 0)
        ScalarLayout.Controls.Add(CheckIsDefault, 1, 1)
        ScalarLayout.Controls.Add(CheckIsEditorOnly, 2, 1)
        ScalarLayout.Controls.Add(LabelParent, 0, 2)
        ScalarLayout.Controls.Add(NumParent, 1, 2)
        ScalarLayout.Controls.Add(LabelLevelMin, 0, 3)
        ScalarLayout.Controls.Add(NumLevelMin, 1, 3)
        ScalarLayout.Controls.Add(LabelLevelMax, 2, 3)
        ScalarLayout.Controls.Add(NumLevelMax, 3, 3)
        ScalarLayout.Controls.Add(LabelMinLevelForRanks, 0, 4)
        ScalarLayout.Controls.Add(NumMinLevelForRanks, 1, 4)
        ScalarLayout.Controls.Add(LabelAltLevelsPerTier, 2, 4)
        ScalarLayout.Controls.Add(NumAltLevelsPerTier, 3, 4)
        ScalarLayout.SetColumnSpan(TextBoxName, 3)
        ScalarLayout.AutoSize = True
        ScalarLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ScalarLayout.Dock = DockStyle.Fill
        ScalarLayout.Location = New Point(4, 20)
        ScalarLayout.Name = "ScalarLayout"
        ScalarLayout.RowCount = 5
        ScalarLayout.RowStyles.Add(New RowStyle())
        ScalarLayout.RowStyles.Add(New RowStyle())
        ScalarLayout.RowStyles.Add(New RowStyle())
        ScalarLayout.RowStyles.Add(New RowStyle())
        ScalarLayout.RowStyles.Add(New RowStyle())
        ScalarLayout.Size = New Size(870, 126)
        ScalarLayout.TabIndex = 0
        '
        ' LabelName
        '
        LabelName.Anchor = AnchorStyles.Left
        LabelName.AutoSize = True
        LabelName.Location = New Point(3, 8)
        LabelName.Name = "LabelName"
        LabelName.Size = New Size(120, 15)
        LabelName.TabIndex = 0
        LabelName.Text = "Display name (FULL):"
        '
        ' TextBoxName
        '
        TextBoxName.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxName.Location = New Point(178, 4)
        TextBoxName.Name = "TextBoxName"
        TextBoxName.PlaceholderText = "Combination name (optional)"
        TextBoxName.Size = New Size(689, 23)
        TextBoxName.TabIndex = 1
        '
        ' CheckIsDefault
        '
        CheckIsDefault.Anchor = AnchorStyles.Left
        CheckIsDefault.AutoSize = True
        CheckIsDefault.Location = New Point(178, 35)
        CheckIsDefault.Name = "CheckIsDefault"
        CheckIsDefault.Size = New Size(140, 19)
        CheckIsDefault.TabIndex = 2
        CheckIsDefault.Text = "Default combination"
        CheckIsDefault.UseVisualStyleBackColor = True
        '
        ' CheckIsEditorOnly
        '
        CheckIsEditorOnly.Anchor = AnchorStyles.Left
        CheckIsEditorOnly.AutoSize = True
        CheckIsEditorOnly.Location = New Point(353, 35)
        CheckIsEditorOnly.Name = "CheckIsEditorOnly"
        CheckIsEditorOnly.Size = New Size(120, 19)
        CheckIsEditorOnly.TabIndex = 3
        CheckIsEditorOnly.Text = "Editor only (OBTF)"
        CheckIsEditorOnly.UseVisualStyleBackColor = True
        '
        ' LabelParent
        '
        LabelParent.Anchor = AnchorStyles.Left
        LabelParent.AutoSize = True
        LabelParent.Location = New Point(3, 65)
        LabelParent.Name = "LabelParent"
        LabelParent.Size = New Size(165, 15)
        LabelParent.TabIndex = 4
        LabelParent.Text = "Parent / Addon Index (-1..):"
        '
        ' NumParent
        '
        NumParent.Anchor = AnchorStyles.Left
        NumParent.Location = New Point(178, 61)
        NumParent.Maximum = New Decimal(New Integer() {32767, 0, 0, 0})
        NumParent.Minimum = New Decimal(New Integer() {1, 0, 0, Integer.MinValue})
        NumParent.Name = "NumParent"
        NumParent.Size = New Size(90, 23)
        NumParent.TabIndex = 5
        NumParent.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelLevelMin
        '
        LabelLevelMin.Anchor = AnchorStyles.Left
        LabelLevelMin.AutoSize = True
        LabelLevelMin.Location = New Point(3, 96)
        LabelLevelMin.Name = "LabelLevelMin"
        LabelLevelMin.Size = New Size(120, 15)
        LabelLevelMin.TabIndex = 6
        LabelLevelMin.Text = "Level Min (0-255):"
        '
        ' NumLevelMin
        '
        NumLevelMin.Anchor = AnchorStyles.Left
        NumLevelMin.Location = New Point(178, 92)
        NumLevelMin.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumLevelMin.Name = "NumLevelMin"
        NumLevelMin.Size = New Size(90, 23)
        NumLevelMin.TabIndex = 7
        NumLevelMin.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelLevelMax
        '
        LabelLevelMax.Anchor = AnchorStyles.Left
        LabelLevelMax.AutoSize = True
        LabelLevelMax.Location = New Point(353, 96)
        LabelLevelMax.Name = "LabelLevelMax"
        LabelLevelMax.Size = New Size(120, 15)
        LabelLevelMax.TabIndex = 8
        LabelLevelMax.Text = "Level Max (0-255):"
        '
        ' NumLevelMax
        '
        NumLevelMax.Anchor = AnchorStyles.Left
        NumLevelMax.Location = New Point(503, 92)
        NumLevelMax.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumLevelMax.Name = "NumLevelMax"
        NumLevelMax.Size = New Size(90, 23)
        NumLevelMax.TabIndex = 9
        NumLevelMax.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelMinLevelForRanks
        '
        LabelMinLevelForRanks.Anchor = AnchorStyles.Left
        LabelMinLevelForRanks.AutoSize = True
        LabelMinLevelForRanks.Location = New Point(3, 127)
        LabelMinLevelForRanks.Name = "LabelMinLevelForRanks"
        LabelMinLevelForRanks.Size = New Size(160, 15)
        LabelMinLevelForRanks.TabIndex = 10
        LabelMinLevelForRanks.Text = "Min Level For Ranks (0-255):"
        '
        ' NumMinLevelForRanks
        '
        NumMinLevelForRanks.Anchor = AnchorStyles.Left
        NumMinLevelForRanks.Location = New Point(178, 123)
        NumMinLevelForRanks.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumMinLevelForRanks.Name = "NumMinLevelForRanks"
        NumMinLevelForRanks.Size = New Size(90, 23)
        NumMinLevelForRanks.TabIndex = 11
        NumMinLevelForRanks.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelAltLevelsPerTier
        '
        LabelAltLevelsPerTier.Anchor = AnchorStyles.Left
        LabelAltLevelsPerTier.AutoSize = True
        LabelAltLevelsPerTier.Location = New Point(353, 127)
        LabelAltLevelsPerTier.Name = "LabelAltLevelsPerTier"
        LabelAltLevelsPerTier.Size = New Size(160, 15)
        LabelAltLevelsPerTier.TabIndex = 12
        LabelAltLevelsPerTier.Text = "Alt Levels Per Tier (0-255):"
        '
        ' NumAltLevelsPerTier
        '
        NumAltLevelsPerTier.Anchor = AnchorStyles.Left
        NumAltLevelsPerTier.Location = New Point(503, 123)
        NumAltLevelsPerTier.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumAltLevelsPerTier.Name = "NumAltLevelsPerTier"
        NumAltLevelsPerTier.Size = New Size(90, 23)
        NumAltLevelsPerTier.TabIndex = 13
        NumAltLevelsPerTier.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' Tabs
        '
        Tabs.Controls.Add(TabKeywords)
        Tabs.Controls.Add(TabIncludes)
        Tabs.Controls.Add(TabProperties)
        Tabs.Dock = DockStyle.Fill
        Tabs.Location = New Point(11, 167)
        Tabs.Name = "Tabs"
        Tabs.SelectedIndex = 0
        Tabs.Size = New Size(878, 400)
        Tabs.TabIndex = 1
        '
        ' TabKeywords
        '
        TabKeywords.Controls.Add(KeywordsLayout)
        TabKeywords.Location = New Point(4, 24)
        TabKeywords.Name = "TabKeywords"
        TabKeywords.Padding = New Padding(6)
        TabKeywords.Size = New Size(870, 372)
        TabKeywords.TabIndex = 0
        TabKeywords.Text = "Keywords"
        TabKeywords.UseVisualStyleBackColor = True
        '
        ' KeywordsLayout
        '
        KeywordsLayout.ColumnCount = 2
        KeywordsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        KeywordsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        KeywordsLayout.Controls.Add(LabelKeywords, 0, 0)
        KeywordsLayout.Controls.Add(ListKeywords, 0, 1)
        KeywordsLayout.Controls.Add(KeywordButtons, 1, 1)
        KeywordsLayout.Dock = DockStyle.Fill
        KeywordsLayout.Location = New Point(6, 6)
        KeywordsLayout.Name = "KeywordsLayout"
        KeywordsLayout.RowCount = 2
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        KeywordsLayout.Size = New Size(858, 360)
        KeywordsLayout.TabIndex = 0
        '
        ' LabelKeywords
        '
        LabelKeywords.AutoSize = True
        LabelKeywords.Location = New Point(3, 0)
        LabelKeywords.Name = "LabelKeywords"
        LabelKeywords.Size = New Size(280, 15)
        LabelKeywords.TabIndex = 0
        LabelKeywords.Text = "Filter keywords (KYWD) — the engine matches these:"
        '
        ' ListKeywords
        '
        ListKeywords.Columns.AddRange(New ColumnHeader() {ColKeyword})
        ListKeywords.Dock = DockStyle.Fill
        ListKeywords.FullRowSelect = True
        ListKeywords.Location = New Point(3, 18)
        ListKeywords.MultiSelect = False
        ListKeywords.Name = "ListKeywords"
        ListKeywords.Size = New Size(762, 339)
        ListKeywords.TabIndex = 1
        ListKeywords.UseCompatibleStateImageBehavior = False
        ListKeywords.View = View.Details
        '
        ' ColKeyword
        '
        ColKeyword.Text = "Keyword"
        ColKeyword.Width = 730
        '
        ' KeywordButtons
        '
        KeywordButtons.Controls.Add(ButtonAddKeyword)
        KeywordButtons.Controls.Add(ButtonRemoveKeyword)
        KeywordButtons.Dock = DockStyle.Fill
        KeywordButtons.FlowDirection = FlowDirection.TopDown
        KeywordButtons.Location = New Point(768, 18)
        KeywordButtons.Margin = New Padding(0)
        KeywordButtons.Name = "KeywordButtons"
        KeywordButtons.Size = New Size(90, 342)
        KeywordButtons.TabIndex = 2
        '
        ' ButtonAddKeyword
        '
        ButtonAddKeyword.Location = New Point(3, 3)
        ButtonAddKeyword.Name = "ButtonAddKeyword"
        ButtonAddKeyword.Size = New Size(84, 26)
        ButtonAddKeyword.TabIndex = 0
        ButtonAddKeyword.Text = "Add…"
        ButtonAddKeyword.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveKeyword
        '
        ButtonRemoveKeyword.Location = New Point(3, 35)
        ButtonRemoveKeyword.Name = "ButtonRemoveKeyword"
        ButtonRemoveKeyword.Size = New Size(84, 26)
        ButtonRemoveKeyword.TabIndex = 1
        ButtonRemoveKeyword.Text = "Remove"
        ButtonRemoveKeyword.UseVisualStyleBackColor = True
        '
        ' TabIncludes
        '
        TabIncludes.Controls.Add(IncludesLayout)
        TabIncludes.Location = New Point(4, 24)
        TabIncludes.Name = "TabIncludes"
        TabIncludes.Padding = New Padding(6)
        TabIncludes.Size = New Size(870, 372)
        TabIncludes.TabIndex = 1
        TabIncludes.Text = "Includes (OMODs)"
        TabIncludes.UseVisualStyleBackColor = True
        '
        ' IncludesLayout
        '
        IncludesLayout.ColumnCount = 2
        IncludesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        IncludesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120F))
        IncludesLayout.Controls.Add(LabelIncludes, 0, 0)
        IncludesLayout.Controls.Add(GridIncludes, 0, 1)
        IncludesLayout.Controls.Add(IncludeButtons, 1, 1)
        IncludesLayout.Dock = DockStyle.Fill
        IncludesLayout.Location = New Point(6, 6)
        IncludesLayout.Name = "IncludesLayout"
        IncludesLayout.RowCount = 2
        IncludesLayout.RowStyles.Add(New RowStyle())
        IncludesLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        IncludesLayout.Size = New Size(858, 360)
        IncludesLayout.TabIndex = 0
        '
        ' LabelIncludes
        '
        LabelIncludes.AutoSize = True
        LabelIncludes.Location = New Point(3, 0)
        LabelIncludes.Name = "LabelIncludes"
        LabelIncludes.Size = New Size(280, 15)
        LabelIncludes.TabIndex = 0
        LabelIncludes.Text = "OMOD includes — Mod + Attach Point Index + flags:"
        '
        ' GridIncludes
        '
        GridIncludes.AllowUserToAddRows = False
        GridIncludes.AllowUserToDeleteRows = False
        GridIncludes.AllowUserToResizeRows = False
        GridIncludes.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridIncludes.Dock = DockStyle.Fill
        GridIncludes.EditMode = DataGridViewEditMode.EditProgrammatically
        GridIncludes.Location = New Point(3, 18)
        GridIncludes.MultiSelect = False
        GridIncludes.Name = "GridIncludes"
        GridIncludes.ReadOnly = True
        GridIncludes.RowHeadersWidth = 25
        GridIncludes.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridIncludes.Size = New Size(732, 339)
        GridIncludes.TabIndex = 1
        '
        ' IncludeButtons
        '
        IncludeButtons.Controls.Add(ButtonAddInclude)
        IncludeButtons.Controls.Add(ButtonEditInclude)
        IncludeButtons.Controls.Add(ButtonRemoveInclude)
        IncludeButtons.Controls.Add(ButtonIncludeUp)
        IncludeButtons.Controls.Add(ButtonIncludeDown)
        IncludeButtons.Dock = DockStyle.Fill
        IncludeButtons.FlowDirection = FlowDirection.TopDown
        IncludeButtons.Location = New Point(738, 18)
        IncludeButtons.Margin = New Padding(0)
        IncludeButtons.Name = "IncludeButtons"
        IncludeButtons.Size = New Size(120, 342)
        IncludeButtons.TabIndex = 2
        '
        ' ButtonAddInclude
        '
        ButtonAddInclude.Location = New Point(3, 3)
        ButtonAddInclude.Name = "ButtonAddInclude"
        ButtonAddInclude.Size = New Size(110, 26)
        ButtonAddInclude.TabIndex = 0
        ButtonAddInclude.Text = "Add OMOD…"
        ButtonAddInclude.UseVisualStyleBackColor = True
        '
        ' ButtonEditInclude
        '
        ButtonEditInclude.Location = New Point(3, 35)
        ButtonEditInclude.Name = "ButtonEditInclude"
        ButtonEditInclude.Size = New Size(110, 26)
        ButtonEditInclude.TabIndex = 1
        ButtonEditInclude.Text = "Edit…"
        ButtonEditInclude.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveInclude
        '
        ButtonRemoveInclude.Location = New Point(3, 67)
        ButtonRemoveInclude.Name = "ButtonRemoveInclude"
        ButtonRemoveInclude.Size = New Size(110, 26)
        ButtonRemoveInclude.TabIndex = 2
        ButtonRemoveInclude.Text = "Remove"
        ButtonRemoveInclude.UseVisualStyleBackColor = True
        '
        ' ButtonIncludeUp
        '
        ButtonIncludeUp.Location = New Point(3, 99)
        ButtonIncludeUp.Name = "ButtonIncludeUp"
        ButtonIncludeUp.Size = New Size(110, 26)
        ButtonIncludeUp.TabIndex = 3
        ButtonIncludeUp.Text = "Move Up"
        ButtonIncludeUp.UseVisualStyleBackColor = True
        '
        ' ButtonIncludeDown
        '
        ButtonIncludeDown.Location = New Point(3, 131)
        ButtonIncludeDown.Name = "ButtonIncludeDown"
        ButtonIncludeDown.Size = New Size(110, 26)
        ButtonIncludeDown.TabIndex = 4
        ButtonIncludeDown.Text = "Move Down"
        ButtonIncludeDown.UseVisualStyleBackColor = True
        '
        ' TabProperties
        '
        TabProperties.Controls.Add(PropsLayout)
        TabProperties.Location = New Point(4, 24)
        TabProperties.Name = "TabProperties"
        TabProperties.Padding = New Padding(6)
        TabProperties.Size = New Size(870, 372)
        TabProperties.TabIndex = 2
        TabProperties.Text = "Properties"
        TabProperties.UseVisualStyleBackColor = True
        '
        ' PropsLayout
        '
        PropsLayout.ColumnCount = 2
        PropsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PropsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130F))
        PropsLayout.Controls.Add(LabelProps, 0, 0)
        PropsLayout.Controls.Add(GridProperties, 0, 1)
        PropsLayout.Controls.Add(PropButtons, 1, 1)
        PropsLayout.Controls.Add(LabelPropsHint, 0, 2)
        PropsLayout.Dock = DockStyle.Fill
        PropsLayout.Location = New Point(6, 6)
        PropsLayout.Name = "PropsLayout"
        PropsLayout.RowCount = 3
        PropsLayout.RowStyles.Add(New RowStyle())
        PropsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PropsLayout.RowStyles.Add(New RowStyle())
        PropsLayout.Size = New Size(858, 360)
        PropsLayout.TabIndex = 0
        '
        ' LabelProps
        '
        LabelProps.AutoSize = True
        LabelProps.Location = New Point(3, 0)
        LabelProps.Name = "LabelProps"
        LabelProps.Size = New Size(280, 15)
        LabelProps.TabIndex = 0
        LabelProps.Text = "Direct property overrides on this combination:"
        '
        ' GridProperties
        '
        GridProperties.AllowUserToAddRows = False
        GridProperties.AllowUserToDeleteRows = False
        GridProperties.AllowUserToResizeRows = False
        GridProperties.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridProperties.Dock = DockStyle.Fill
        GridProperties.EditMode = DataGridViewEditMode.EditProgrammatically
        GridProperties.Location = New Point(3, 18)
        GridProperties.MultiSelect = False
        GridProperties.Name = "GridProperties"
        GridProperties.ReadOnly = True
        GridProperties.RowHeadersWidth = 25
        GridProperties.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridProperties.Size = New Size(722, 306)
        GridProperties.TabIndex = 1
        '
        ' PropButtons
        '
        PropButtons.Controls.Add(ButtonAddProp)
        PropButtons.Controls.Add(ButtonEditProp)
        PropButtons.Controls.Add(ButtonRemoveProp)
        PropButtons.Dock = DockStyle.Fill
        PropButtons.FlowDirection = FlowDirection.TopDown
        PropButtons.Location = New Point(728, 18)
        PropButtons.Margin = New Padding(0)
        PropButtons.Name = "PropButtons"
        PropButtons.Size = New Size(130, 306)
        PropButtons.TabIndex = 2
        '
        ' ButtonAddProp
        '
        ButtonAddProp.Location = New Point(3, 3)
        ButtonAddProp.Name = "ButtonAddProp"
        ButtonAddProp.Size = New Size(120, 26)
        ButtonAddProp.TabIndex = 0
        ButtonAddProp.Text = "Add property"
        ButtonAddProp.UseVisualStyleBackColor = True
        '
        ' ButtonEditProp
        '
        ButtonEditProp.Location = New Point(3, 35)
        ButtonEditProp.Name = "ButtonEditProp"
        ButtonEditProp.Size = New Size(120, 26)
        ButtonEditProp.TabIndex = 1
        ButtonEditProp.Text = "Edit…"
        ButtonEditProp.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveProp
        '
        ButtonRemoveProp.Location = New Point(3, 67)
        ButtonRemoveProp.Name = "ButtonRemoveProp"
        ButtonRemoveProp.Size = New Size(120, 26)
        ButtonRemoveProp.TabIndex = 2
        ButtonRemoveProp.Text = "Remove"
        ButtonRemoveProp.UseVisualStyleBackColor = True
        '
        ' LabelPropsHint
        '
        LabelPropsHint.AutoSize = True
        LabelPropsHint.ForeColor = Color.DimGray
        LabelPropsHint.Location = New Point(3, 342)
        LabelPropsHint.Name = "LabelPropsHint"
        LabelPropsHint.Size = New Size(560, 15)
        LabelPropsHint.TabIndex = 3
        LabelPropsHint.Text = "The grid is read-only. Use Add / Edit (or double-click a row) to edit a property in a dedicated dialog; Value1 is FormID-picked or numeric per ValueType."
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 573)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(878, 36)
        BottomLayout.TabIndex = 2
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(712, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 1
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(798, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 0
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ObtsCombinationEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(900, 620)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ObtsCombinationEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Object Template Combination"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        GroupScalars.ResumeLayout(False)
        GroupScalars.PerformLayout()
        ScalarLayout.ResumeLayout(False)
        ScalarLayout.PerformLayout()
        CType(NumParent, ComponentModel.ISupportInitialize).EndInit()
        CType(NumLevelMin, ComponentModel.ISupportInitialize).EndInit()
        CType(NumLevelMax, ComponentModel.ISupportInitialize).EndInit()
        CType(NumMinLevelForRanks, ComponentModel.ISupportInitialize).EndInit()
        CType(NumAltLevelsPerTier, ComponentModel.ISupportInitialize).EndInit()
        Tabs.ResumeLayout(False)
        TabKeywords.ResumeLayout(False)
        KeywordsLayout.ResumeLayout(False)
        KeywordsLayout.PerformLayout()
        KeywordButtons.ResumeLayout(False)
        TabIncludes.ResumeLayout(False)
        IncludesLayout.ResumeLayout(False)
        IncludesLayout.PerformLayout()
        CType(GridIncludes, ComponentModel.ISupportInitialize).EndInit()
        IncludeButtons.ResumeLayout(False)
        TabProperties.ResumeLayout(False)
        PropsLayout.ResumeLayout(False)
        PropsLayout.PerformLayout()
        CType(GridProperties, ComponentModel.ISupportInitialize).EndInit()
        PropButtons.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        BottomLayout.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupScalars As System.Windows.Forms.GroupBox
    Friend WithEvents ScalarLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelName As System.Windows.Forms.Label
    Friend WithEvents TextBoxName As System.Windows.Forms.TextBox
    Friend WithEvents CheckIsDefault As System.Windows.Forms.CheckBox
    Friend WithEvents CheckIsEditorOnly As System.Windows.Forms.CheckBox
    Friend WithEvents LabelParent As System.Windows.Forms.Label
    Friend WithEvents NumParent As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelLevelMin As System.Windows.Forms.Label
    Friend WithEvents NumLevelMin As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelLevelMax As System.Windows.Forms.Label
    Friend WithEvents NumLevelMax As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelMinLevelForRanks As System.Windows.Forms.Label
    Friend WithEvents NumMinLevelForRanks As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelAltLevelsPerTier As System.Windows.Forms.Label
    Friend WithEvents NumAltLevelsPerTier As System.Windows.Forms.NumericUpDown
    Friend WithEvents Tabs As System.Windows.Forms.TabControl
    Friend WithEvents TabKeywords As System.Windows.Forms.TabPage
    Friend WithEvents KeywordsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelKeywords As System.Windows.Forms.Label
    Friend WithEvents ListKeywords As System.Windows.Forms.ListView
    Friend WithEvents ColKeyword As System.Windows.Forms.ColumnHeader
    Friend WithEvents KeywordButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddKeyword As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveKeyword As System.Windows.Forms.Button
    Friend WithEvents TabIncludes As System.Windows.Forms.TabPage
    Friend WithEvents IncludesLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelIncludes As System.Windows.Forms.Label
    Friend WithEvents GridIncludes As System.Windows.Forms.DataGridView
    Friend WithEvents IncludeButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddInclude As System.Windows.Forms.Button
    Friend WithEvents ButtonEditInclude As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveInclude As System.Windows.Forms.Button
    Friend WithEvents ButtonIncludeUp As System.Windows.Forms.Button
    Friend WithEvents ButtonIncludeDown As System.Windows.Forms.Button
    Friend WithEvents TabProperties As System.Windows.Forms.TabPage
    Friend WithEvents PropsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelProps As System.Windows.Forms.Label
    Friend WithEvents GridProperties As System.Windows.Forms.DataGridView
    Friend WithEvents PropButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddProp As System.Windows.Forms.Button
    Friend WithEvents ButtonEditProp As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveProp As System.Windows.Forms.Button
    Friend WithEvents LabelPropsHint As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
