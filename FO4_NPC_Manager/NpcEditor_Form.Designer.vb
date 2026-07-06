' UI built in Designer per feedback_ui_in_designer.md (companion to ArmoEditor_Form). InitializeComponent
' is declarative ONLY (no For/If/lambda). The read-only DataGridView columns are added in code-behind
' (variable/repeated content), mirroring ObtsCombinationEditor_Form.BuildIncludesGridColumns.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcEditor_Form
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
        Tabs = New TabControl()
        TabGeneral = New TabPage()
        GeneralLayout = New TableLayoutPanel()
        LabelFull = New Label()
        TextBoxFull = New TextBox()
        LabelShort = New Label()
        TextBoxShort = New TextBox()
        LabelRace = New Label()
        TextBoxRace = New TextBox()
        ButtonPickRace = New Button()
        LabelVoice = New Label()
        TextBoxVoice = New TextBox()
        ButtonPickVoice = New Button()
        LabelClass = New Label()
        TextBoxClass = New TextBox()
        ButtonPickClass = New Button()
        LabelZnam = New Label()
        TextBoxZnam = New TextBox()
        ButtonPickZnam = New Button()
        LabelLevel = New Label()
        NumLevel = New NumericUpDown()
        LabelXp = New Label()
        NumXp = New NumericUpDown()
        LabelCalcMin = New Label()
        NumCalcMin = New NumericUpDown()
        LabelCalcMax = New Label()
        NumCalcMax = New NumericUpDown()
        LabelDisp = New Label()
        NumDisp = New NumericUpDown()
        LabelFlags = New Label()
        FlowFlags = New FlowLayoutPanel()
        ChkFemale = New CheckBox()
        ChkEssential = New CheckBox()
        ChkRespawn = New CheckBox()
        ChkAutoCalc = New CheckBox()
        ChkUnique = New CheckBox()
        ChkNoStealth = New CheckBox()
        ChkPCLevelMult = New CheckBox()
        ChkProtected = New CheckBox()
        ChkSummonable = New CheckBox()
        ChkDoesntBleed = New CheckBox()
        ChkOppositeGender = New CheckBox()
        ChkSimpleActor = New CheckBox()
        ChkNoActHellos = New CheckBox()
        ChkGhost = New CheckBox()
        ChkInvulnerable = New CheckBox()
        TabObts = New TabPage()
        ObtsLayout = New TableLayoutPanel()
        GridCombos = New DataGridView()
        ObtsButtons = New FlowLayoutPanel()
        ButtonAddCombo = New Button()
        ButtonDupCombo = New Button()
        ButtonRemoveCombo = New Button()
        ButtonComboUp = New Button()
        ButtonComboDown = New Button()
        ButtonEditCombo = New Button()
        TabKeywords = New TabPage()
        KeywordsLayout = New TableLayoutPanel()
        LabelKwda = New Label()
        ListKeywords = New ListView()
        ColKeyword = New ColumnHeader()
        KeywordButtons = New FlowLayoutPanel()
        ButtonAddKeyword = New Button()
        ButtonRemoveKeyword = New Button()
        LabelAppr = New Label()
        ListAppr = New ListView()
        ColAppr = New ColumnHeader()
        ApprButtons = New FlowLayoutPanel()
        ButtonAddAppr = New Button()
        ButtonRemoveAppr = New Button()
        TabFactions = New TabPage()
        FactionsLayout = New TableLayoutPanel()
        GridFactions = New DataGridView()
        FactionButtons = New FlowLayoutPanel()
        ButtonAddFaction = New Button()
        ButtonEditFaction = New Button()
        ButtonRemoveFaction = New Button()
        TabInventory = New TabPage()
        InventoryLayout = New TableLayoutPanel()
        GridInventory = New DataGridView()
        InventoryButtons = New FlowLayoutPanel()
        ButtonAddItem = New Button()
        ButtonEditItem = New Button()
        ButtonRemoveItem = New Button()
        OutfitPanel = New TableLayoutPanel()
        LabelDefaultOutfit = New Label()
        TextBoxDefaultOutfit = New TextBox()
        ButtonPickDefaultOutfit = New Button()
        LabelSleepOutfit = New Label()
        TextBoxSleepOutfit = New TextBox()
        ButtonPickSleepOutfit = New Button()
        TabPerks = New TabPage()
        PerksLayout = New TableLayoutPanel()
        GridPerks = New DataGridView()
        PerksButtons = New FlowLayoutPanel()
        ButtonAddPerk = New Button()
        ButtonEditPerk = New Button()
        ButtonRemovePerk = New Button()
        TabSpells = New TabPage()
        SpellsLayout = New TableLayoutPanel()
        ListSpells = New ListView()
        ColSpell = New ColumnHeader()
        SpellButtons = New FlowLayoutPanel()
        ButtonAddSpell = New Button()
        ButtonRemoveSpell = New Button()
        TabProps = New TabPage()
        PropsLayout = New TableLayoutPanel()
        GridProps = New DataGridView()
        PropsButtons = New FlowLayoutPanel()
        ButtonAddProp = New Button()
        ButtonEditProp = New Button()
        ButtonRemoveProp = New Button()
        LabelPersistNote = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        Tabs.SuspendLayout()
        TabGeneral.SuspendLayout()
        GeneralLayout.SuspendLayout()
        CType(NumLevel, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumXp, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumCalcMin, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumCalcMax, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumDisp, ComponentModel.ISupportInitialize).BeginInit()
        FlowFlags.SuspendLayout()
        TabObts.SuspendLayout()
        ObtsLayout.SuspendLayout()
        CType(GridCombos, ComponentModel.ISupportInitialize).BeginInit()
        ObtsButtons.SuspendLayout()
        TabKeywords.SuspendLayout()
        KeywordsLayout.SuspendLayout()
        KeywordButtons.SuspendLayout()
        ApprButtons.SuspendLayout()
        TabFactions.SuspendLayout()
        FactionsLayout.SuspendLayout()
        CType(GridFactions, ComponentModel.ISupportInitialize).BeginInit()
        FactionButtons.SuspendLayout()
        TabInventory.SuspendLayout()
        InventoryLayout.SuspendLayout()
        CType(GridInventory, ComponentModel.ISupportInitialize).BeginInit()
        InventoryButtons.SuspendLayout()
        OutfitPanel.SuspendLayout()
        TabPerks.SuspendLayout()
        PerksLayout.SuspendLayout()
        CType(GridPerks, ComponentModel.ISupportInitialize).BeginInit()
        PerksButtons.SuspendLayout()
        TabSpells.SuspendLayout()
        SpellsLayout.SuspendLayout()
        SpellButtons.SuspendLayout()
        TabProps.SuspendLayout()
        PropsLayout.SuspendLayout()
        CType(GridProps, ComponentModel.ISupportInitialize).BeginInit()
        PropsButtons.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(Tabs, 0, 0)
        RootLayout.Controls.Add(LabelPersistNote, 0, 1)
        RootLayout.Controls.Add(BottomLayout, 0, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(720, 560)
        RootLayout.TabIndex = 0
        '
        ' Tabs
        '
        Tabs.Controls.Add(TabGeneral)
        Tabs.Controls.Add(TabObts)
        Tabs.Controls.Add(TabKeywords)
        Tabs.Controls.Add(TabFactions)
        Tabs.Controls.Add(TabInventory)
        Tabs.Controls.Add(TabPerks)
        Tabs.Controls.Add(TabSpells)
        Tabs.Controls.Add(TabProps)
        Tabs.Dock = DockStyle.Fill
        Tabs.Location = New Point(11, 11)
        Tabs.Name = "Tabs"
        Tabs.SelectedIndex = 0
        Tabs.Size = New Size(698, 480)
        Tabs.TabIndex = 0
        '
        ' TabGeneral
        '
        TabGeneral.Controls.Add(GeneralLayout)
        TabGeneral.Location = New Point(4, 24)
        TabGeneral.Name = "TabGeneral"
        TabGeneral.Padding = New Padding(3)
        TabGeneral.Size = New Size(690, 452)
        TabGeneral.TabIndex = 0
        TabGeneral.Text = "General"
        TabGeneral.UseVisualStyleBackColor = True
        '
        ' GeneralLayout
        '
        GeneralLayout.ColumnCount = 3
        GeneralLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        GeneralLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GeneralLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        GeneralLayout.Controls.Add(LabelFull, 0, 0)
        GeneralLayout.Controls.Add(TextBoxFull, 1, 0)
        GeneralLayout.Controls.Add(LabelShort, 0, 1)
        GeneralLayout.Controls.Add(TextBoxShort, 1, 1)
        GeneralLayout.Controls.Add(LabelRace, 0, 2)
        GeneralLayout.Controls.Add(TextBoxRace, 1, 2)
        GeneralLayout.Controls.Add(ButtonPickRace, 2, 2)
        GeneralLayout.Controls.Add(LabelVoice, 0, 3)
        GeneralLayout.Controls.Add(TextBoxVoice, 1, 3)
        GeneralLayout.Controls.Add(ButtonPickVoice, 2, 3)
        GeneralLayout.Controls.Add(LabelClass, 0, 4)
        GeneralLayout.Controls.Add(TextBoxClass, 1, 4)
        GeneralLayout.Controls.Add(ButtonPickClass, 2, 4)
        GeneralLayout.Controls.Add(LabelZnam, 0, 5)
        GeneralLayout.Controls.Add(TextBoxZnam, 1, 5)
        GeneralLayout.Controls.Add(ButtonPickZnam, 2, 5)
        GeneralLayout.Controls.Add(LabelLevel, 0, 6)
        GeneralLayout.Controls.Add(NumLevel, 1, 6)
        GeneralLayout.Controls.Add(LabelXp, 0, 7)
        GeneralLayout.Controls.Add(NumXp, 1, 7)
        GeneralLayout.Controls.Add(LabelCalcMin, 0, 8)
        GeneralLayout.Controls.Add(NumCalcMin, 1, 8)
        GeneralLayout.Controls.Add(LabelCalcMax, 0, 9)
        GeneralLayout.Controls.Add(NumCalcMax, 1, 9)
        GeneralLayout.Controls.Add(LabelDisp, 0, 10)
        GeneralLayout.Controls.Add(NumDisp, 1, 10)
        GeneralLayout.Controls.Add(LabelFlags, 0, 11)
        GeneralLayout.Controls.Add(FlowFlags, 1, 11)
        GeneralLayout.SetColumnSpan(TextBoxFull, 2)
        GeneralLayout.SetColumnSpan(TextBoxShort, 2)
        GeneralLayout.SetColumnSpan(FlowFlags, 2)
        GeneralLayout.Dock = DockStyle.Fill
        GeneralLayout.Location = New Point(3, 3)
        GeneralLayout.Name = "GeneralLayout"
        GeneralLayout.Padding = New Padding(6)
        GeneralLayout.RowCount = 12
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle())
        GeneralLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        GeneralLayout.Size = New Size(684, 446)
        GeneralLayout.TabIndex = 0
        '
        ' LabelFull
        '
        LabelFull.Anchor = AnchorStyles.Left
        LabelFull.AutoSize = True
        LabelFull.Name = "LabelFull"
        LabelFull.Text = "Name (FULL):"
        '
        ' TextBoxFull
        '
        TextBoxFull.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFull.Name = "TextBoxFull"
        TextBoxFull.Size = New Size(470, 23)
        TextBoxFull.TabIndex = 0
        '
        ' LabelShort
        '
        LabelShort.Anchor = AnchorStyles.Left
        LabelShort.AutoSize = True
        LabelShort.Name = "LabelShort"
        LabelShort.Text = "Short Name (SHRT):"
        '
        ' TextBoxShort
        '
        TextBoxShort.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxShort.Name = "TextBoxShort"
        TextBoxShort.Size = New Size(470, 23)
        TextBoxShort.TabIndex = 1
        '
        ' LabelRace
        '
        LabelRace.Anchor = AnchorStyles.Left
        LabelRace.AutoSize = True
        LabelRace.Name = "LabelRace"
        LabelRace.Text = "Race (RNAM):"
        '
        ' TextBoxRace
        '
        TextBoxRace.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxRace.Name = "TextBoxRace"
        TextBoxRace.ReadOnly = True
        TextBoxRace.Size = New Size(470, 23)
        TextBoxRace.TabIndex = 2
        '
        ' ButtonPickRace
        '
        ButtonPickRace.Anchor = AnchorStyles.Left
        ButtonPickRace.Name = "ButtonPickRace"
        ButtonPickRace.Size = New Size(34, 24)
        ButtonPickRace.TabIndex = 3
        ButtonPickRace.Text = "…"
        ButtonPickRace.UseVisualStyleBackColor = True
        '
        ' LabelVoice
        '
        LabelVoice.Anchor = AnchorStyles.Left
        LabelVoice.AutoSize = True
        LabelVoice.Name = "LabelVoice"
        LabelVoice.Text = "Voice (VTCK):"
        '
        ' TextBoxVoice
        '
        TextBoxVoice.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxVoice.Name = "TextBoxVoice"
        TextBoxVoice.ReadOnly = True
        TextBoxVoice.Size = New Size(470, 23)
        TextBoxVoice.TabIndex = 4
        '
        ' ButtonPickVoice
        '
        ButtonPickVoice.Anchor = AnchorStyles.Left
        ButtonPickVoice.Name = "ButtonPickVoice"
        ButtonPickVoice.Size = New Size(34, 24)
        ButtonPickVoice.TabIndex = 5
        ButtonPickVoice.Text = "…"
        ButtonPickVoice.UseVisualStyleBackColor = True
        '
        ' LabelClass
        '
        LabelClass.Anchor = AnchorStyles.Left
        LabelClass.AutoSize = True
        LabelClass.Name = "LabelClass"
        LabelClass.Text = "Class (CNAM):"
        '
        ' TextBoxClass
        '
        TextBoxClass.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxClass.Name = "TextBoxClass"
        TextBoxClass.ReadOnly = True
        TextBoxClass.Size = New Size(470, 23)
        TextBoxClass.TabIndex = 6
        '
        ' ButtonPickClass
        '
        ButtonPickClass.Anchor = AnchorStyles.Left
        ButtonPickClass.Name = "ButtonPickClass"
        ButtonPickClass.Size = New Size(34, 24)
        ButtonPickClass.TabIndex = 7
        ButtonPickClass.Text = "…"
        ButtonPickClass.UseVisualStyleBackColor = True
        '
        ' LabelZnam
        '
        LabelZnam.Anchor = AnchorStyles.Left
        LabelZnam.AutoSize = True
        LabelZnam.Name = "LabelZnam"
        LabelZnam.Text = "Combat Style (ZNAM):"
        '
        ' TextBoxZnam
        '
        TextBoxZnam.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxZnam.Name = "TextBoxZnam"
        TextBoxZnam.ReadOnly = True
        TextBoxZnam.Size = New Size(470, 23)
        TextBoxZnam.TabIndex = 8
        '
        ' ButtonPickZnam
        '
        ButtonPickZnam.Anchor = AnchorStyles.Left
        ButtonPickZnam.Name = "ButtonPickZnam"
        ButtonPickZnam.Size = New Size(34, 24)
        ButtonPickZnam.TabIndex = 9
        ButtonPickZnam.Text = "…"
        ButtonPickZnam.UseVisualStyleBackColor = True
        '
        ' LabelLevel
        '
        LabelLevel.Anchor = AnchorStyles.Left
        LabelLevel.AutoSize = True
        LabelLevel.Name = "LabelLevel"
        LabelLevel.Text = "Level (ACBS):"
        '
        ' NumLevel
        '
        NumLevel.Anchor = AnchorStyles.Left
        NumLevel.Maximum = New Decimal(65535)
        NumLevel.Name = "NumLevel"
        NumLevel.Size = New Size(120, 23)
        NumLevel.TabIndex = 10
        NumLevel.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelXp
        '
        LabelXp.Anchor = AnchorStyles.Left
        LabelXp.AutoSize = True
        LabelXp.Name = "LabelXp"
        LabelXp.Text = "XP Value Offset:"
        '
        ' NumXp
        '
        NumXp.Anchor = AnchorStyles.Left
        NumXp.Maximum = New Decimal(32767)
        NumXp.Minimum = New Decimal(New Integer() {32768, 0, 0, -2147483648})
        NumXp.Name = "NumXp"
        NumXp.Size = New Size(120, 23)
        NumXp.TabIndex = 11
        NumXp.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelCalcMin
        '
        LabelCalcMin.Anchor = AnchorStyles.Left
        LabelCalcMin.AutoSize = True
        LabelCalcMin.Name = "LabelCalcMin"
        LabelCalcMin.Text = "Calc Min Level:"
        '
        ' NumCalcMin
        '
        NumCalcMin.Anchor = AnchorStyles.Left
        NumCalcMin.Maximum = New Decimal(65535)
        NumCalcMin.Name = "NumCalcMin"
        NumCalcMin.Size = New Size(120, 23)
        NumCalcMin.TabIndex = 11
        NumCalcMin.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelCalcMax
        '
        LabelCalcMax.Anchor = AnchorStyles.Left
        LabelCalcMax.AutoSize = True
        LabelCalcMax.Name = "LabelCalcMax"
        LabelCalcMax.Text = "Calc Max Level:"
        '
        ' NumCalcMax
        '
        NumCalcMax.Anchor = AnchorStyles.Left
        NumCalcMax.Maximum = New Decimal(65535)
        NumCalcMax.Name = "NumCalcMax"
        NumCalcMax.Size = New Size(120, 23)
        NumCalcMax.TabIndex = 12
        NumCalcMax.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelDisp
        '
        LabelDisp.Anchor = AnchorStyles.Left
        LabelDisp.AutoSize = True
        LabelDisp.Name = "LabelDisp"
        LabelDisp.Text = "Disposition Base:"
        '
        ' NumDisp
        '
        NumDisp.Anchor = AnchorStyles.Left
        NumDisp.Maximum = New Decimal(32767)
        NumDisp.Minimum = New Decimal(New Integer() {32768, 0, 0, -2147483648})
        NumDisp.Name = "NumDisp"
        NumDisp.Size = New Size(120, 23)
        NumDisp.TabIndex = 13
        NumDisp.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelFlags
        '
        LabelFlags.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        LabelFlags.AutoSize = True
        LabelFlags.Margin = New Padding(3, 6, 3, 0)
        LabelFlags.Name = "LabelFlags"
        LabelFlags.Text = "ACBS Flags:"
        '
        ' FlowFlags
        '
        FlowFlags.Controls.Add(ChkFemale)
        FlowFlags.Controls.Add(ChkEssential)
        FlowFlags.Controls.Add(ChkRespawn)
        FlowFlags.Controls.Add(ChkAutoCalc)
        FlowFlags.Controls.Add(ChkUnique)
        FlowFlags.Controls.Add(ChkNoStealth)
        FlowFlags.Controls.Add(ChkPCLevelMult)
        FlowFlags.Controls.Add(ChkProtected)
        FlowFlags.Controls.Add(ChkSummonable)
        FlowFlags.Controls.Add(ChkDoesntBleed)
        FlowFlags.Controls.Add(ChkOppositeGender)
        FlowFlags.Controls.Add(ChkSimpleActor)
        FlowFlags.Controls.Add(ChkNoActHellos)
        FlowFlags.Controls.Add(ChkGhost)
        FlowFlags.Controls.Add(ChkInvulnerable)
        FlowFlags.Dock = DockStyle.Fill
        FlowFlags.FlowDirection = FlowDirection.TopDown
        FlowFlags.Name = "FlowFlags"
        FlowFlags.TabIndex = 14
        FlowFlags.WrapContents = True
        '
        ' ChkFemale
        '
        ChkFemale.AutoSize = True
        ChkFemale.Name = "ChkFemale"
        ChkFemale.Text = "Female"
        '
        ' ChkEssential
        '
        ChkEssential.AutoSize = True
        ChkEssential.Name = "ChkEssential"
        ChkEssential.Text = "Essential"
        '
        ' ChkRespawn
        '
        ChkRespawn.AutoSize = True
        ChkRespawn.Name = "ChkRespawn"
        ChkRespawn.Text = "Respawn"
        '
        ' ChkAutoCalc
        '
        ChkAutoCalc.AutoSize = True
        ChkAutoCalc.Name = "ChkAutoCalc"
        ChkAutoCalc.Text = "Auto Calc Stats"
        '
        ' ChkUnique
        '
        ChkUnique.AutoSize = True
        ChkUnique.Name = "ChkUnique"
        ChkUnique.Text = "Unique"
        '
        ' ChkNoStealth
        '
        ChkNoStealth.AutoSize = True
        ChkNoStealth.Name = "ChkNoStealth"
        ChkNoStealth.Text = "Doesn't Affect Stealth Meter"
        '
        ' ChkPCLevelMult
        '
        ChkPCLevelMult.AutoSize = True
        ChkPCLevelMult.Name = "ChkPCLevelMult"
        ChkPCLevelMult.Text = "PC Level Mult"
        '
        ' ChkProtected
        '
        ChkProtected.AutoSize = True
        ChkProtected.Name = "ChkProtected"
        ChkProtected.Text = "Protected"
        '
        ' ChkSummonable
        '
        ChkSummonable.AutoSize = True
        ChkSummonable.Name = "ChkSummonable"
        ChkSummonable.Text = "Summonable"
        '
        ' ChkDoesntBleed
        '
        ChkDoesntBleed.AutoSize = True
        ChkDoesntBleed.Name = "ChkDoesntBleed"
        ChkDoesntBleed.Text = "Doesn't Bleed"
        '
        ' ChkOppositeGender
        '
        ChkOppositeGender.AutoSize = True
        ChkOppositeGender.Name = "ChkOppositeGender"
        ChkOppositeGender.Text = "Opposite Gender Anims"
        '
        ' ChkSimpleActor
        '
        ChkSimpleActor.AutoSize = True
        ChkSimpleActor.Name = "ChkSimpleActor"
        ChkSimpleActor.Text = "Simple Actor"
        '
        ' ChkNoActHellos
        '
        ChkNoActHellos.AutoSize = True
        ChkNoActHellos.Name = "ChkNoActHellos"
        ChkNoActHellos.Text = "No Activation / Hellos"
        '
        ' ChkGhost
        '
        ChkGhost.AutoSize = True
        ChkGhost.Name = "ChkGhost"
        ChkGhost.Text = "Is Ghost"
        '
        ' ChkInvulnerable
        '
        ChkInvulnerable.AutoSize = True
        ChkInvulnerable.Name = "ChkInvulnerable"
        ChkInvulnerable.Text = "Invulnerable"
        '
        ' TabObts
        '
        TabObts.Controls.Add(ObtsLayout)
        TabObts.Location = New Point(4, 24)
        TabObts.Name = "TabObts"
        TabObts.Padding = New Padding(3)
        TabObts.Size = New Size(690, 452)
        TabObts.TabIndex = 1
        TabObts.Text = "Object Template"
        TabObts.UseVisualStyleBackColor = True
        '
        ' ObtsLayout
        '
        ObtsLayout.ColumnCount = 1
        ObtsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ObtsLayout.Controls.Add(GridCombos, 0, 0)
        ObtsLayout.Controls.Add(ObtsButtons, 0, 1)
        ObtsLayout.Dock = DockStyle.Fill
        ObtsLayout.Location = New Point(3, 3)
        ObtsLayout.Name = "ObtsLayout"
        ObtsLayout.RowCount = 2
        ObtsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ObtsLayout.RowStyles.Add(New RowStyle())
        ObtsLayout.Size = New Size(684, 446)
        ObtsLayout.TabIndex = 0
        '
        ' GridCombos
        '
        GridCombos.AllowUserToAddRows = False
        GridCombos.AllowUserToDeleteRows = False
        GridCombos.AllowUserToResizeRows = False
        GridCombos.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridCombos.Dock = DockStyle.Fill
        GridCombos.EditMode = DataGridViewEditMode.EditProgrammatically
        GridCombos.MultiSelect = False
        GridCombos.Name = "GridCombos"
        GridCombos.ReadOnly = True
        GridCombos.RowHeadersWidth = 25
        GridCombos.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridCombos.TabIndex = 0
        '
        ' ObtsButtons
        '
        ObtsButtons.AutoSize = True
        ObtsButtons.Controls.Add(ButtonAddCombo)
        ObtsButtons.Controls.Add(ButtonDupCombo)
        ObtsButtons.Controls.Add(ButtonRemoveCombo)
        ObtsButtons.Controls.Add(ButtonComboUp)
        ObtsButtons.Controls.Add(ButtonComboDown)
        ObtsButtons.Controls.Add(ButtonEditCombo)
        ObtsButtons.Dock = DockStyle.Fill
        ObtsButtons.Name = "ObtsButtons"
        ObtsButtons.TabIndex = 1
        '
        ' ButtonAddCombo
        '
        ButtonAddCombo.AutoSize = True
        ButtonAddCombo.Name = "ButtonAddCombo"
        ButtonAddCombo.Size = New Size(75, 25)
        ButtonAddCombo.TabIndex = 0
        ButtonAddCombo.Text = "Add"
        ButtonAddCombo.UseVisualStyleBackColor = True
        '
        ' ButtonDupCombo
        '
        ButtonDupCombo.AutoSize = True
        ButtonDupCombo.Name = "ButtonDupCombo"
        ButtonDupCombo.Size = New Size(75, 25)
        ButtonDupCombo.TabIndex = 1
        ButtonDupCombo.Text = "Duplicate"
        ButtonDupCombo.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveCombo
        '
        ButtonRemoveCombo.AutoSize = True
        ButtonRemoveCombo.Name = "ButtonRemoveCombo"
        ButtonRemoveCombo.Size = New Size(75, 25)
        ButtonRemoveCombo.TabIndex = 2
        ButtonRemoveCombo.Text = "Remove"
        ButtonRemoveCombo.UseVisualStyleBackColor = True
        '
        ' ButtonComboUp
        '
        ButtonComboUp.AutoSize = True
        ButtonComboUp.Name = "ButtonComboUp"
        ButtonComboUp.Size = New Size(50, 25)
        ButtonComboUp.TabIndex = 3
        ButtonComboUp.Text = "Up"
        ButtonComboUp.UseVisualStyleBackColor = True
        '
        ' ButtonComboDown
        '
        ButtonComboDown.AutoSize = True
        ButtonComboDown.Name = "ButtonComboDown"
        ButtonComboDown.Size = New Size(50, 25)
        ButtonComboDown.TabIndex = 4
        ButtonComboDown.Text = "Down"
        ButtonComboDown.UseVisualStyleBackColor = True
        '
        ' ButtonEditCombo
        '
        ButtonEditCombo.AutoSize = True
        ButtonEditCombo.Name = "ButtonEditCombo"
        ButtonEditCombo.Size = New Size(75, 25)
        ButtonEditCombo.TabIndex = 5
        ButtonEditCombo.Text = "Edit…"
        ButtonEditCombo.UseVisualStyleBackColor = True
        '
        ' TabKeywords
        '
        TabKeywords.Controls.Add(KeywordsLayout)
        TabKeywords.Location = New Point(4, 24)
        TabKeywords.Name = "TabKeywords"
        TabKeywords.Padding = New Padding(3)
        TabKeywords.Size = New Size(690, 452)
        TabKeywords.TabIndex = 2
        TabKeywords.Text = "Keywords"
        TabKeywords.UseVisualStyleBackColor = True
        '
        ' KeywordsLayout — hosts BOTH KWDA (general keywords) and APPR (attach-parent-slots) splits, mirror of
        ' ArmoEditor_Form's "Keywords" tab: two ListViews + Add/Remove, filtered by IsAttachPointKeyword.
        '
        KeywordsLayout.ColumnCount = 1
        KeywordsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        KeywordsLayout.Controls.Add(LabelKwda, 0, 0)
        KeywordsLayout.Controls.Add(ListKeywords, 0, 1)
        KeywordsLayout.Controls.Add(KeywordButtons, 0, 2)
        KeywordsLayout.Controls.Add(LabelAppr, 0, 3)
        KeywordsLayout.Controls.Add(ListAppr, 0, 4)
        KeywordsLayout.Controls.Add(ApprButtons, 0, 5)
        KeywordsLayout.Dock = DockStyle.Fill
        KeywordsLayout.Location = New Point(3, 3)
        KeywordsLayout.Name = "KeywordsLayout"
        KeywordsLayout.RowCount = 6
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 50F))
        KeywordsLayout.RowStyles.Add(New RowStyle())
        KeywordsLayout.Size = New Size(684, 446)
        KeywordsLayout.TabIndex = 0
        '
        ' LabelKwda
        '
        LabelKwda.AutoSize = True
        LabelKwda.Margin = New Padding(3, 4, 3, 2)
        LabelKwda.Name = "LabelKwda"
        LabelKwda.Text = "Keywords (KWDA):"
        '
        ' ListKeywords
        '
        ListKeywords.Columns.AddRange(New ColumnHeader() {ColKeyword})
        ListKeywords.Dock = DockStyle.Fill
        ListKeywords.FullRowSelect = True
        ListKeywords.MultiSelect = False
        ListKeywords.Name = "ListKeywords"
        ListKeywords.TabIndex = 1
        ListKeywords.UseCompatibleStateImageBehavior = False
        ListKeywords.View = View.Details
        '
        ' ColKeyword
        '
        ColKeyword.Text = "Keyword (KWDA)"
        ColKeyword.Width = 620
        '
        ' KeywordButtons
        '
        KeywordButtons.AutoSize = True
        KeywordButtons.Controls.Add(ButtonAddKeyword)
        KeywordButtons.Controls.Add(ButtonRemoveKeyword)
        KeywordButtons.Dock = DockStyle.Fill
        KeywordButtons.Name = "KeywordButtons"
        KeywordButtons.TabIndex = 2
        '
        ' ButtonAddKeyword
        '
        ButtonAddKeyword.AutoSize = True
        ButtonAddKeyword.Name = "ButtonAddKeyword"
        ButtonAddKeyword.Size = New Size(75, 25)
        ButtonAddKeyword.TabIndex = 0
        ButtonAddKeyword.Text = "Add…"
        ButtonAddKeyword.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveKeyword
        '
        ButtonRemoveKeyword.AutoSize = True
        ButtonRemoveKeyword.Name = "ButtonRemoveKeyword"
        ButtonRemoveKeyword.Size = New Size(75, 25)
        ButtonRemoveKeyword.TabIndex = 1
        ButtonRemoveKeyword.Text = "Remove"
        ButtonRemoveKeyword.UseVisualStyleBackColor = True
        '
        ' LabelAppr
        '
        LabelAppr.AutoSize = True
        LabelAppr.Margin = New Padding(3, 8, 3, 2)
        LabelAppr.Name = "LabelAppr"
        LabelAppr.Text = "Attach Parent Slots (APPR):"
        '
        ' ListAppr
        '
        ListAppr.Columns.AddRange(New ColumnHeader() {ColAppr})
        ListAppr.Dock = DockStyle.Fill
        ListAppr.FullRowSelect = True
        ListAppr.MultiSelect = False
        ListAppr.Name = "ListAppr"
        ListAppr.TabIndex = 3
        ListAppr.UseCompatibleStateImageBehavior = False
        ListAppr.View = View.Details
        '
        ' ColAppr
        '
        ColAppr.Text = "Attach-Parent Slot (APPR)"
        ColAppr.Width = 620
        '
        ' ApprButtons
        '
        ApprButtons.AutoSize = True
        ApprButtons.Controls.Add(ButtonAddAppr)
        ApprButtons.Controls.Add(ButtonRemoveAppr)
        ApprButtons.Dock = DockStyle.Fill
        ApprButtons.Name = "ApprButtons"
        ApprButtons.TabIndex = 4
        '
        ' ButtonAddAppr
        '
        ButtonAddAppr.AutoSize = True
        ButtonAddAppr.Name = "ButtonAddAppr"
        ButtonAddAppr.Size = New Size(75, 25)
        ButtonAddAppr.TabIndex = 0
        ButtonAddAppr.Text = "Add…"
        ButtonAddAppr.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveAppr
        '
        ButtonRemoveAppr.AutoSize = True
        ButtonRemoveAppr.Name = "ButtonRemoveAppr"
        ButtonRemoveAppr.Size = New Size(75, 25)
        ButtonRemoveAppr.TabIndex = 1
        ButtonRemoveAppr.Text = "Remove"
        ButtonRemoveAppr.UseVisualStyleBackColor = True
        '
        ' TabFactions
        '
        TabFactions.Controls.Add(FactionsLayout)
        TabFactions.Location = New Point(4, 24)
        TabFactions.Name = "TabFactions"
        TabFactions.Padding = New Padding(3)
        TabFactions.Size = New Size(690, 452)
        TabFactions.TabIndex = 3
        TabFactions.Text = "Factions"
        TabFactions.UseVisualStyleBackColor = True
        '
        ' FactionsLayout
        '
        FactionsLayout.ColumnCount = 1
        FactionsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FactionsLayout.Controls.Add(GridFactions, 0, 0)
        FactionsLayout.Controls.Add(FactionButtons, 0, 1)
        FactionsLayout.Dock = DockStyle.Fill
        FactionsLayout.Location = New Point(3, 3)
        FactionsLayout.Name = "FactionsLayout"
        FactionsLayout.RowCount = 2
        FactionsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        FactionsLayout.RowStyles.Add(New RowStyle())
        FactionsLayout.Size = New Size(684, 446)
        FactionsLayout.TabIndex = 0
        '
        ' GridFactions
        '
        GridFactions.AllowUserToAddRows = False
        GridFactions.AllowUserToDeleteRows = False
        GridFactions.AllowUserToResizeRows = False
        GridFactions.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridFactions.Dock = DockStyle.Fill
        GridFactions.EditMode = DataGridViewEditMode.EditProgrammatically
        GridFactions.MultiSelect = False
        GridFactions.Name = "GridFactions"
        GridFactions.ReadOnly = True
        GridFactions.RowHeadersWidth = 25
        GridFactions.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridFactions.TabIndex = 0
        '
        ' FactionButtons
        '
        FactionButtons.AutoSize = True
        FactionButtons.Controls.Add(ButtonAddFaction)
        FactionButtons.Controls.Add(ButtonEditFaction)
        FactionButtons.Controls.Add(ButtonRemoveFaction)
        FactionButtons.Dock = DockStyle.Fill
        FactionButtons.Name = "FactionButtons"
        FactionButtons.TabIndex = 1
        '
        ' ButtonAddFaction
        '
        ButtonAddFaction.AutoSize = True
        ButtonAddFaction.Name = "ButtonAddFaction"
        ButtonAddFaction.Size = New Size(75, 25)
        ButtonAddFaction.TabIndex = 0
        ButtonAddFaction.Text = "Add…"
        ButtonAddFaction.UseVisualStyleBackColor = True
        '
        ' ButtonEditFaction
        '
        ButtonEditFaction.AutoSize = True
        ButtonEditFaction.Name = "ButtonEditFaction"
        ButtonEditFaction.Size = New Size(75, 25)
        ButtonEditFaction.TabIndex = 1
        ButtonEditFaction.Text = "Edit…"
        ButtonEditFaction.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveFaction
        '
        ButtonRemoveFaction.AutoSize = True
        ButtonRemoveFaction.Name = "ButtonRemoveFaction"
        ButtonRemoveFaction.Size = New Size(75, 25)
        ButtonRemoveFaction.TabIndex = 2
        ButtonRemoveFaction.Text = "Remove"
        ButtonRemoveFaction.UseVisualStyleBackColor = True
        '
        ' TabInventory
        '
        TabInventory.Controls.Add(InventoryLayout)
        TabInventory.Location = New Point(4, 24)
        TabInventory.Name = "TabInventory"
        TabInventory.Padding = New Padding(3)
        TabInventory.Size = New Size(690, 452)
        TabInventory.TabIndex = 4
        TabInventory.Text = "Inventory"
        TabInventory.UseVisualStyleBackColor = True
        '
        ' InventoryLayout
        '
        InventoryLayout.ColumnCount = 1
        InventoryLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        InventoryLayout.Controls.Add(GridInventory, 0, 0)
        InventoryLayout.Controls.Add(InventoryButtons, 0, 1)
        InventoryLayout.Controls.Add(OutfitPanel, 0, 2)
        InventoryLayout.Dock = DockStyle.Fill
        InventoryLayout.Location = New Point(3, 3)
        InventoryLayout.Name = "InventoryLayout"
        InventoryLayout.RowCount = 3
        InventoryLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        InventoryLayout.RowStyles.Add(New RowStyle())
        InventoryLayout.RowStyles.Add(New RowStyle())
        InventoryLayout.Size = New Size(684, 446)
        InventoryLayout.TabIndex = 0
        '
        ' GridInventory
        '
        GridInventory.AllowUserToAddRows = False
        GridInventory.AllowUserToDeleteRows = False
        GridInventory.AllowUserToResizeRows = False
        GridInventory.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridInventory.Dock = DockStyle.Fill
        GridInventory.EditMode = DataGridViewEditMode.EditProgrammatically
        GridInventory.MultiSelect = False
        GridInventory.Name = "GridInventory"
        GridInventory.ReadOnly = True
        GridInventory.RowHeadersWidth = 25
        GridInventory.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridInventory.TabIndex = 0
        '
        ' InventoryButtons
        '
        InventoryButtons.AutoSize = True
        InventoryButtons.Controls.Add(ButtonAddItem)
        InventoryButtons.Controls.Add(ButtonEditItem)
        InventoryButtons.Controls.Add(ButtonRemoveItem)
        InventoryButtons.Dock = DockStyle.Fill
        InventoryButtons.Name = "InventoryButtons"
        InventoryButtons.TabIndex = 1
        '
        ' ButtonAddItem
        '
        ButtonAddItem.AutoSize = True
        ButtonAddItem.Name = "ButtonAddItem"
        ButtonAddItem.Size = New Size(75, 25)
        ButtonAddItem.TabIndex = 0
        ButtonAddItem.Text = "Add…"
        ButtonAddItem.UseVisualStyleBackColor = True
        '
        ' ButtonEditItem
        '
        ButtonEditItem.AutoSize = True
        ButtonEditItem.Name = "ButtonEditItem"
        ButtonEditItem.Size = New Size(75, 25)
        ButtonEditItem.TabIndex = 1
        ButtonEditItem.Text = "Edit…"
        ButtonEditItem.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveItem
        '
        ButtonRemoveItem.AutoSize = True
        ButtonRemoveItem.Name = "ButtonRemoveItem"
        ButtonRemoveItem.Size = New Size(75, 25)
        ButtonRemoveItem.TabIndex = 2
        ButtonRemoveItem.Text = "Remove"
        ButtonRemoveItem.UseVisualStyleBackColor = True
        '
        ' OutfitPanel — Default (DOFT) + Sleep (SOFT) outfit pickers, below the CNTO item grid.
        '
        OutfitPanel.AutoSize = True
        OutfitPanel.ColumnCount = 3
        OutfitPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        OutfitPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        OutfitPanel.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        OutfitPanel.Controls.Add(LabelDefaultOutfit, 0, 0)
        OutfitPanel.Controls.Add(TextBoxDefaultOutfit, 1, 0)
        OutfitPanel.Controls.Add(ButtonPickDefaultOutfit, 2, 0)
        OutfitPanel.Controls.Add(LabelSleepOutfit, 0, 1)
        OutfitPanel.Controls.Add(TextBoxSleepOutfit, 1, 1)
        OutfitPanel.Controls.Add(ButtonPickSleepOutfit, 2, 1)
        OutfitPanel.Dock = DockStyle.Fill
        OutfitPanel.Margin = New Padding(3, 6, 3, 0)
        OutfitPanel.Name = "OutfitPanel"
        OutfitPanel.RowCount = 2
        OutfitPanel.RowStyles.Add(New RowStyle())
        OutfitPanel.RowStyles.Add(New RowStyle())
        OutfitPanel.TabIndex = 2
        '
        ' LabelDefaultOutfit
        '
        LabelDefaultOutfit.Anchor = AnchorStyles.Left
        LabelDefaultOutfit.AutoSize = True
        LabelDefaultOutfit.Name = "LabelDefaultOutfit"
        LabelDefaultOutfit.Text = "Default Outfit (DOFT):"
        '
        ' TextBoxDefaultOutfit
        '
        TextBoxDefaultOutfit.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxDefaultOutfit.Name = "TextBoxDefaultOutfit"
        TextBoxDefaultOutfit.ReadOnly = True
        TextBoxDefaultOutfit.Size = New Size(470, 23)
        TextBoxDefaultOutfit.TabIndex = 0
        '
        ' ButtonPickDefaultOutfit
        '
        ButtonPickDefaultOutfit.Anchor = AnchorStyles.Left
        ButtonPickDefaultOutfit.Name = "ButtonPickDefaultOutfit"
        ButtonPickDefaultOutfit.Size = New Size(34, 24)
        ButtonPickDefaultOutfit.TabIndex = 1
        ButtonPickDefaultOutfit.Text = "…"
        ButtonPickDefaultOutfit.UseVisualStyleBackColor = True
        '
        ' LabelSleepOutfit
        '
        LabelSleepOutfit.Anchor = AnchorStyles.Left
        LabelSleepOutfit.AutoSize = True
        LabelSleepOutfit.Name = "LabelSleepOutfit"
        LabelSleepOutfit.Text = "Sleep Outfit (SOFT):"
        '
        ' TextBoxSleepOutfit
        '
        TextBoxSleepOutfit.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxSleepOutfit.Name = "TextBoxSleepOutfit"
        TextBoxSleepOutfit.ReadOnly = True
        TextBoxSleepOutfit.Size = New Size(470, 23)
        TextBoxSleepOutfit.TabIndex = 2
        '
        ' ButtonPickSleepOutfit
        '
        ButtonPickSleepOutfit.Anchor = AnchorStyles.Left
        ButtonPickSleepOutfit.Name = "ButtonPickSleepOutfit"
        ButtonPickSleepOutfit.Size = New Size(34, 24)
        ButtonPickSleepOutfit.TabIndex = 3
        ButtonPickSleepOutfit.Text = "…"
        ButtonPickSleepOutfit.UseVisualStyleBackColor = True
        '
        ' TabPerks
        '
        TabPerks.Controls.Add(PerksLayout)
        TabPerks.Location = New Point(4, 24)
        TabPerks.Name = "TabPerks"
        TabPerks.Padding = New Padding(3)
        TabPerks.Size = New Size(690, 452)
        TabPerks.TabIndex = 5
        TabPerks.Text = "Perks"
        TabPerks.UseVisualStyleBackColor = True
        '
        ' PerksLayout
        '
        PerksLayout.ColumnCount = 1
        PerksLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PerksLayout.Controls.Add(GridPerks, 0, 0)
        PerksLayout.Controls.Add(PerksButtons, 0, 1)
        PerksLayout.Dock = DockStyle.Fill
        PerksLayout.Location = New Point(3, 3)
        PerksLayout.Name = "PerksLayout"
        PerksLayout.RowCount = 2
        PerksLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PerksLayout.RowStyles.Add(New RowStyle())
        PerksLayout.Size = New Size(684, 446)
        PerksLayout.TabIndex = 0
        '
        ' GridPerks
        '
        GridPerks.AllowUserToAddRows = False
        GridPerks.AllowUserToDeleteRows = False
        GridPerks.AllowUserToResizeRows = False
        GridPerks.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridPerks.Dock = DockStyle.Fill
        GridPerks.EditMode = DataGridViewEditMode.EditProgrammatically
        GridPerks.MultiSelect = False
        GridPerks.Name = "GridPerks"
        GridPerks.ReadOnly = True
        GridPerks.RowHeadersWidth = 25
        GridPerks.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridPerks.TabIndex = 0
        '
        ' PerksButtons
        '
        PerksButtons.AutoSize = True
        PerksButtons.Controls.Add(ButtonAddPerk)
        PerksButtons.Controls.Add(ButtonEditPerk)
        PerksButtons.Controls.Add(ButtonRemovePerk)
        PerksButtons.Dock = DockStyle.Fill
        PerksButtons.Name = "PerksButtons"
        PerksButtons.TabIndex = 1
        '
        ' ButtonAddPerk
        '
        ButtonAddPerk.AutoSize = True
        ButtonAddPerk.Name = "ButtonAddPerk"
        ButtonAddPerk.Size = New Size(75, 25)
        ButtonAddPerk.TabIndex = 0
        ButtonAddPerk.Text = "Add…"
        ButtonAddPerk.UseVisualStyleBackColor = True
        '
        ' ButtonEditPerk
        '
        ButtonEditPerk.AutoSize = True
        ButtonEditPerk.Name = "ButtonEditPerk"
        ButtonEditPerk.Size = New Size(75, 25)
        ButtonEditPerk.TabIndex = 1
        ButtonEditPerk.Text = "Edit…"
        ButtonEditPerk.UseVisualStyleBackColor = True
        '
        ' ButtonRemovePerk
        '
        ButtonRemovePerk.AutoSize = True
        ButtonRemovePerk.Name = "ButtonRemovePerk"
        ButtonRemovePerk.Size = New Size(75, 25)
        ButtonRemovePerk.TabIndex = 2
        ButtonRemovePerk.Text = "Remove"
        ButtonRemovePerk.UseVisualStyleBackColor = True
        '
        ' TabSpells
        '
        TabSpells.Controls.Add(SpellsLayout)
        TabSpells.Location = New Point(4, 24)
        TabSpells.Name = "TabSpells"
        TabSpells.Padding = New Padding(3)
        TabSpells.Size = New Size(690, 452)
        TabSpells.TabIndex = 6
        TabSpells.Text = "Actor Effects"
        TabSpells.UseVisualStyleBackColor = True
        '
        ' SpellsLayout
        '
        SpellsLayout.ColumnCount = 1
        SpellsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SpellsLayout.Controls.Add(ListSpells, 0, 0)
        SpellsLayout.Controls.Add(SpellButtons, 0, 1)
        SpellsLayout.Dock = DockStyle.Fill
        SpellsLayout.Location = New Point(3, 3)
        SpellsLayout.Name = "SpellsLayout"
        SpellsLayout.RowCount = 2
        SpellsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SpellsLayout.RowStyles.Add(New RowStyle())
        SpellsLayout.Size = New Size(684, 446)
        SpellsLayout.TabIndex = 0
        '
        ' ListSpells
        '
        ListSpells.Columns.AddRange(New ColumnHeader() {ColSpell})
        ListSpells.Dock = DockStyle.Fill
        ListSpells.FullRowSelect = True
        ListSpells.MultiSelect = False
        ListSpells.Name = "ListSpells"
        ListSpells.TabIndex = 0
        ListSpells.UseCompatibleStateImageBehavior = False
        ListSpells.View = View.Details
        '
        ' ColSpell
        '
        ColSpell.Text = "Actor Effect / Spell (SPLO)"
        ColSpell.Width = 620
        '
        ' SpellButtons
        '
        SpellButtons.AutoSize = True
        SpellButtons.Controls.Add(ButtonAddSpell)
        SpellButtons.Controls.Add(ButtonRemoveSpell)
        SpellButtons.Dock = DockStyle.Fill
        SpellButtons.Name = "SpellButtons"
        SpellButtons.TabIndex = 1
        '
        ' ButtonAddSpell
        '
        ButtonAddSpell.AutoSize = True
        ButtonAddSpell.Name = "ButtonAddSpell"
        ButtonAddSpell.Size = New Size(75, 25)
        ButtonAddSpell.TabIndex = 0
        ButtonAddSpell.Text = "Add…"
        ButtonAddSpell.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveSpell
        '
        ButtonRemoveSpell.AutoSize = True
        ButtonRemoveSpell.Name = "ButtonRemoveSpell"
        ButtonRemoveSpell.Size = New Size(75, 25)
        ButtonRemoveSpell.TabIndex = 1
        ButtonRemoveSpell.Text = "Remove"
        ButtonRemoveSpell.UseVisualStyleBackColor = True
        '
        ' TabProps
        '
        TabProps.Controls.Add(PropsLayout)
        TabProps.Location = New Point(4, 24)
        TabProps.Name = "TabProps"
        TabProps.Padding = New Padding(3)
        TabProps.Size = New Size(690, 452)
        TabProps.TabIndex = 7
        TabProps.Text = "Properties"
        TabProps.UseVisualStyleBackColor = True
        '
        ' PropsLayout
        '
        PropsLayout.ColumnCount = 1
        PropsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PropsLayout.Controls.Add(GridProps, 0, 0)
        PropsLayout.Controls.Add(PropsButtons, 0, 1)
        PropsLayout.Dock = DockStyle.Fill
        PropsLayout.Location = New Point(3, 3)
        PropsLayout.Name = "PropsLayout"
        PropsLayout.RowCount = 2
        PropsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PropsLayout.RowStyles.Add(New RowStyle())
        PropsLayout.Size = New Size(684, 446)
        PropsLayout.TabIndex = 0
        '
        ' GridProps
        '
        GridProps.AllowUserToAddRows = False
        GridProps.AllowUserToDeleteRows = False
        GridProps.AllowUserToResizeRows = False
        GridProps.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridProps.Dock = DockStyle.Fill
        GridProps.EditMode = DataGridViewEditMode.EditProgrammatically
        GridProps.MultiSelect = False
        GridProps.Name = "GridProps"
        GridProps.ReadOnly = True
        GridProps.RowHeadersWidth = 25
        GridProps.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridProps.TabIndex = 0
        '
        ' PropsButtons
        '
        PropsButtons.AutoSize = True
        PropsButtons.Controls.Add(ButtonAddProp)
        PropsButtons.Controls.Add(ButtonEditProp)
        PropsButtons.Controls.Add(ButtonRemoveProp)
        PropsButtons.Dock = DockStyle.Fill
        PropsButtons.Name = "PropsButtons"
        PropsButtons.TabIndex = 1
        '
        ' ButtonAddProp
        '
        ButtonAddProp.AutoSize = True
        ButtonAddProp.Name = "ButtonAddProp"
        ButtonAddProp.Size = New Size(75, 25)
        ButtonAddProp.TabIndex = 0
        ButtonAddProp.Text = "Add…"
        ButtonAddProp.UseVisualStyleBackColor = True
        '
        ' ButtonEditProp
        '
        ButtonEditProp.AutoSize = True
        ButtonEditProp.Name = "ButtonEditProp"
        ButtonEditProp.Size = New Size(75, 25)
        ButtonEditProp.TabIndex = 1
        ButtonEditProp.Text = "Edit…"
        ButtonEditProp.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveProp
        '
        ButtonRemoveProp.AutoSize = True
        ButtonRemoveProp.Name = "ButtonRemoveProp"
        ButtonRemoveProp.Size = New Size(75, 25)
        ButtonRemoveProp.TabIndex = 2
        ButtonRemoveProp.Text = "Remove"
        ButtonRemoveProp.UseVisualStyleBackColor = True
        '
        ' LabelPersistNote
        '
        LabelPersistNote.AutoSize = True
        LabelPersistNote.ForeColor = Color.DimGray
        LabelPersistNote.Margin = New Padding(3, 4, 3, 4)
        LabelPersistNote.Name = "LabelPersistNote"
        LabelPersistNote.TabIndex = 1
        LabelPersistNote.Text = "Edits apply to the NPC preview and persist to the plugin on Save (as an NPC-record override)."
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 2
        '
        ' ButtonOk
        '
        ButtonOk.Margin = New Padding(3, 3, 3, 3)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(90, 27)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Margin = New Padding(3, 3, 3, 3)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(90, 27)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' NpcEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(1264, 681)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "NpcEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "NPC Editor"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        Tabs.ResumeLayout(False)
        TabGeneral.ResumeLayout(False)
        GeneralLayout.ResumeLayout(False)
        GeneralLayout.PerformLayout()
        CType(NumLevel, ComponentModel.ISupportInitialize).EndInit()
        CType(NumXp, ComponentModel.ISupportInitialize).EndInit()
        CType(NumCalcMin, ComponentModel.ISupportInitialize).EndInit()
        CType(NumCalcMax, ComponentModel.ISupportInitialize).EndInit()
        CType(NumDisp, ComponentModel.ISupportInitialize).EndInit()
        FlowFlags.ResumeLayout(False)
        FlowFlags.PerformLayout()
        TabObts.ResumeLayout(False)
        ObtsLayout.ResumeLayout(False)
        ObtsLayout.PerformLayout()
        CType(GridCombos, ComponentModel.ISupportInitialize).EndInit()
        ObtsButtons.ResumeLayout(False)
        ObtsButtons.PerformLayout()
        TabKeywords.ResumeLayout(False)
        KeywordsLayout.ResumeLayout(False)
        KeywordsLayout.PerformLayout()
        KeywordButtons.ResumeLayout(False)
        KeywordButtons.PerformLayout()
        ApprButtons.ResumeLayout(False)
        ApprButtons.PerformLayout()
        TabFactions.ResumeLayout(False)
        FactionsLayout.ResumeLayout(False)
        FactionsLayout.PerformLayout()
        CType(GridFactions, ComponentModel.ISupportInitialize).EndInit()
        FactionButtons.ResumeLayout(False)
        FactionButtons.PerformLayout()
        TabInventory.ResumeLayout(False)
        InventoryLayout.ResumeLayout(False)
        InventoryLayout.PerformLayout()
        CType(GridInventory, ComponentModel.ISupportInitialize).EndInit()
        InventoryButtons.ResumeLayout(False)
        InventoryButtons.PerformLayout()
        OutfitPanel.ResumeLayout(False)
        OutfitPanel.PerformLayout()
        TabPerks.ResumeLayout(False)
        PerksLayout.ResumeLayout(False)
        PerksLayout.PerformLayout()
        CType(GridPerks, ComponentModel.ISupportInitialize).EndInit()
        PerksButtons.ResumeLayout(False)
        PerksButtons.PerformLayout()
        TabSpells.ResumeLayout(False)
        SpellsLayout.ResumeLayout(False)
        SpellsLayout.PerformLayout()
        SpellButtons.ResumeLayout(False)
        SpellButtons.PerformLayout()
        TabProps.ResumeLayout(False)
        PropsLayout.ResumeLayout(False)
        PropsLayout.PerformLayout()
        CType(GridProps, ComponentModel.ISupportInitialize).EndInit()
        PropsButtons.ResumeLayout(False)
        PropsButtons.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents Tabs As System.Windows.Forms.TabControl
    Friend WithEvents TabGeneral As System.Windows.Forms.TabPage
    Friend WithEvents GeneralLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFull As System.Windows.Forms.Label
    Friend WithEvents TextBoxFull As System.Windows.Forms.TextBox
    Friend WithEvents LabelShort As System.Windows.Forms.Label
    Friend WithEvents TextBoxShort As System.Windows.Forms.TextBox
    Friend WithEvents LabelRace As System.Windows.Forms.Label
    Friend WithEvents TextBoxRace As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickRace As System.Windows.Forms.Button
    Friend WithEvents LabelVoice As System.Windows.Forms.Label
    Friend WithEvents TextBoxVoice As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickVoice As System.Windows.Forms.Button
    Friend WithEvents LabelClass As System.Windows.Forms.Label
    Friend WithEvents TextBoxClass As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickClass As System.Windows.Forms.Button
    Friend WithEvents LabelZnam As System.Windows.Forms.Label
    Friend WithEvents TextBoxZnam As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickZnam As System.Windows.Forms.Button
    Friend WithEvents LabelLevel As System.Windows.Forms.Label
    Friend WithEvents NumLevel As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelXp As System.Windows.Forms.Label
    Friend WithEvents NumXp As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelCalcMin As System.Windows.Forms.Label
    Friend WithEvents NumCalcMin As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelCalcMax As System.Windows.Forms.Label
    Friend WithEvents NumCalcMax As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelDisp As System.Windows.Forms.Label
    Friend WithEvents NumDisp As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelFlags As System.Windows.Forms.Label
    Friend WithEvents FlowFlags As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ChkFemale As System.Windows.Forms.CheckBox
    Friend WithEvents ChkEssential As System.Windows.Forms.CheckBox
    Friend WithEvents ChkRespawn As System.Windows.Forms.CheckBox
    Friend WithEvents ChkAutoCalc As System.Windows.Forms.CheckBox
    Friend WithEvents ChkUnique As System.Windows.Forms.CheckBox
    Friend WithEvents ChkNoStealth As System.Windows.Forms.CheckBox
    Friend WithEvents ChkPCLevelMult As System.Windows.Forms.CheckBox
    Friend WithEvents ChkProtected As System.Windows.Forms.CheckBox
    Friend WithEvents ChkSummonable As System.Windows.Forms.CheckBox
    Friend WithEvents ChkDoesntBleed As System.Windows.Forms.CheckBox
    Friend WithEvents ChkOppositeGender As System.Windows.Forms.CheckBox
    Friend WithEvents ChkSimpleActor As System.Windows.Forms.CheckBox
    Friend WithEvents ChkNoActHellos As System.Windows.Forms.CheckBox
    Friend WithEvents ChkGhost As System.Windows.Forms.CheckBox
    Friend WithEvents ChkInvulnerable As System.Windows.Forms.CheckBox
    Friend WithEvents TabObts As System.Windows.Forms.TabPage
    Friend WithEvents ObtsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GridCombos As System.Windows.Forms.DataGridView
    Friend WithEvents ObtsButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonDupCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveCombo As System.Windows.Forms.Button
    Friend WithEvents ButtonComboUp As System.Windows.Forms.Button
    Friend WithEvents ButtonComboDown As System.Windows.Forms.Button
    Friend WithEvents ButtonEditCombo As System.Windows.Forms.Button
    Friend WithEvents TabKeywords As System.Windows.Forms.TabPage
    Friend WithEvents KeywordsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelKwda As System.Windows.Forms.Label
    Friend WithEvents ListKeywords As System.Windows.Forms.ListView
    Friend WithEvents ColKeyword As System.Windows.Forms.ColumnHeader
    Friend WithEvents KeywordButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddKeyword As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveKeyword As System.Windows.Forms.Button
    Friend WithEvents LabelAppr As System.Windows.Forms.Label
    Friend WithEvents ListAppr As System.Windows.Forms.ListView
    Friend WithEvents ColAppr As System.Windows.Forms.ColumnHeader
    Friend WithEvents ApprButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddAppr As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveAppr As System.Windows.Forms.Button
    Friend WithEvents TabFactions As System.Windows.Forms.TabPage
    Friend WithEvents FactionsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GridFactions As System.Windows.Forms.DataGridView
    Friend WithEvents FactionButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddFaction As System.Windows.Forms.Button
    Friend WithEvents ButtonEditFaction As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveFaction As System.Windows.Forms.Button
    Friend WithEvents TabInventory As System.Windows.Forms.TabPage
    Friend WithEvents InventoryLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GridInventory As System.Windows.Forms.DataGridView
    Friend WithEvents InventoryButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddItem As System.Windows.Forms.Button
    Friend WithEvents ButtonEditItem As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveItem As System.Windows.Forms.Button
    Friend WithEvents OutfitPanel As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelDefaultOutfit As System.Windows.Forms.Label
    Friend WithEvents TextBoxDefaultOutfit As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickDefaultOutfit As System.Windows.Forms.Button
    Friend WithEvents LabelSleepOutfit As System.Windows.Forms.Label
    Friend WithEvents TextBoxSleepOutfit As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickSleepOutfit As System.Windows.Forms.Button
    Friend WithEvents TabPerks As System.Windows.Forms.TabPage
    Friend WithEvents PerksLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GridPerks As System.Windows.Forms.DataGridView
    Friend WithEvents PerksButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddPerk As System.Windows.Forms.Button
    Friend WithEvents ButtonEditPerk As System.Windows.Forms.Button
    Friend WithEvents ButtonRemovePerk As System.Windows.Forms.Button
    Friend WithEvents TabSpells As System.Windows.Forms.TabPage
    Friend WithEvents SpellsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListSpells As System.Windows.Forms.ListView
    Friend WithEvents ColSpell As System.Windows.Forms.ColumnHeader
    Friend WithEvents SpellButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddSpell As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveSpell As System.Windows.Forms.Button
    Friend WithEvents TabProps As System.Windows.Forms.TabPage
    Friend WithEvents PropsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GridProps As System.Windows.Forms.DataGridView
    Friend WithEvents PropsButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddProp As System.Windows.Forms.Button
    Friend WithEvents ButtonEditProp As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveProp As System.Windows.Forms.Button
    Friend WithEvents LabelPersistNote As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
