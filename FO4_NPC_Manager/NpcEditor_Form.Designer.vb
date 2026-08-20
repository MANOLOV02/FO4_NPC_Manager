' UI built in Designer per 00-reglas-ui-y-vb.md (companion to ArmoEditor_Form). InitializeComponent
' is declarative ONLY (no For/If/lambda). The read-only DataGridView columns are added in code-behind
' (variable/repeated content), mirroring ObtsCombinationEditor_Form.BuildIncludesGridColumns.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcEditor_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
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
        LabelMagickaOff = New Label()
        NumMagickaOff = New NumericUpDown()
        LabelStaminaOff = New Label()
        NumStaminaOff = New NumericUpDown()
        LabelSpeedMult = New Label()
        NumSpeedMult = New NumericUpDown()
        LabelHealthOff = New Label()
        NumHealthOff = New NumericUpDown()
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
        TabStats = New TabPage()
        StatsLayout = New TableLayoutPanel()
        LabelSkillHdrA = New Label()
        LabelValueHdrA = New Label()
        LabelOffsetHdrA = New Label()
        LabelSkillHdrB = New Label()
        LabelValueHdrB = New Label()
        LabelOffsetHdrB = New Label()
        LabelSkill0 = New Label()
        NumSkillVal0 = New NumericUpDown()
        NumSkillOff0 = New NumericUpDown()
        LabelSkill1 = New Label()
        NumSkillVal1 = New NumericUpDown()
        NumSkillOff1 = New NumericUpDown()
        LabelSkill2 = New Label()
        NumSkillVal2 = New NumericUpDown()
        NumSkillOff2 = New NumericUpDown()
        LabelSkill3 = New Label()
        NumSkillVal3 = New NumericUpDown()
        NumSkillOff3 = New NumericUpDown()
        LabelSkill4 = New Label()
        NumSkillVal4 = New NumericUpDown()
        NumSkillOff4 = New NumericUpDown()
        LabelSkill5 = New Label()
        NumSkillVal5 = New NumericUpDown()
        NumSkillOff5 = New NumericUpDown()
        LabelSkill6 = New Label()
        NumSkillVal6 = New NumericUpDown()
        NumSkillOff6 = New NumericUpDown()
        LabelSkill7 = New Label()
        NumSkillVal7 = New NumericUpDown()
        NumSkillOff7 = New NumericUpDown()
        LabelSkill8 = New Label()
        NumSkillVal8 = New NumericUpDown()
        NumSkillOff8 = New NumericUpDown()
        LabelSkill9 = New Label()
        NumSkillVal9 = New NumericUpDown()
        NumSkillOff9 = New NumericUpDown()
        LabelSkill10 = New Label()
        NumSkillVal10 = New NumericUpDown()
        NumSkillOff10 = New NumericUpDown()
        LabelSkill11 = New Label()
        NumSkillVal11 = New NumericUpDown()
        NumSkillOff11 = New NumericUpDown()
        LabelSkill12 = New Label()
        NumSkillVal12 = New NumericUpDown()
        NumSkillOff12 = New NumericUpDown()
        LabelSkill13 = New Label()
        NumSkillVal13 = New NumericUpDown()
        NumSkillOff13 = New NumericUpDown()
        LabelSkill14 = New Label()
        NumSkillVal14 = New NumericUpDown()
        NumSkillOff14 = New NumericUpDown()
        LabelSkill15 = New Label()
        NumSkillVal15 = New NumericUpDown()
        NumSkillOff15 = New NumericUpDown()
        LabelSkill16 = New Label()
        NumSkillVal16 = New NumericUpDown()
        NumSkillOff16 = New NumericUpDown()
        LabelSkill17 = New Label()
        NumSkillVal17 = New NumericUpDown()
        NumSkillOff17 = New NumericUpDown()
        AttrLayout = New TableLayoutPanel()
        LabelHealth = New Label()
        NumHealth = New NumericUpDown()
        LabelMagicka = New Label()
        NumMagicka = New NumericUpDown()
        LabelStamina = New Label()
        NumStamina = New NumericUpDown()
        LabelFarModel = New Label()
        NumFarModel = New NumericUpDown()
        LabelGeared = New Label()
        NumGeared = New NumericUpDown()
        LabelStatsNote = New Label()
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
        CType(NumMagickaOff, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumStaminaOff, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSpeedMult, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumHealthOff, ComponentModel.ISupportInitialize).BeginInit()
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
        TabStats.SuspendLayout()
        StatsLayout.SuspendLayout()
        CType(NumSkillVal0, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff0, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal1, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff1, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal2, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff2, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal3, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff3, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal4, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff4, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal5, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff5, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal6, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff6, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal7, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff7, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal8, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff8, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal9, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff9, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal10, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff10, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal11, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff11, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal12, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff12, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal13, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff13, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal14, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff14, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal15, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff15, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal16, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff16, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillVal17, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumSkillOff17, ComponentModel.ISupportInitialize).BeginInit()
        AttrLayout.SuspendLayout()
        CType(NumHealth, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumMagicka, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumStamina, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumFarModel, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumGeared, ComponentModel.ISupportInitialize).BeginInit()
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
        Tabs.Controls.Add(TabStats)
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
        GeneralLayout.Controls.Add(LabelMagickaOff, 0, 11)
        GeneralLayout.Controls.Add(NumMagickaOff, 1, 11)
        GeneralLayout.Controls.Add(LabelStaminaOff, 0, 12)
        GeneralLayout.Controls.Add(NumStaminaOff, 1, 12)
        GeneralLayout.Controls.Add(LabelHealthOff, 0, 13)
        GeneralLayout.Controls.Add(NumHealthOff, 1, 13)
        GeneralLayout.Controls.Add(LabelSpeedMult, 0, 14)
        GeneralLayout.Controls.Add(NumSpeedMult, 1, 14)
        GeneralLayout.Controls.Add(LabelFlags, 0, 15)
        GeneralLayout.Controls.Add(FlowFlags, 1, 15)
        GeneralLayout.SetColumnSpan(TextBoxFull, 2)
        GeneralLayout.SetColumnSpan(TextBoxShort, 2)
        GeneralLayout.SetColumnSpan(FlowFlags, 2)
        GeneralLayout.Dock = DockStyle.Fill
        GeneralLayout.Location = New Point(3, 3)
        GeneralLayout.Name = "GeneralLayout"
        GeneralLayout.Padding = New Padding(6)
        GeneralLayout.RowCount = 16
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
        ' LabelMagickaOff — ACBS +4 s16, SKYRIM ONLY (hidden on FO4, whose 20-byte ACBS has XP Value there).
        '
        LabelMagickaOff.Anchor = AnchorStyles.Left
        LabelMagickaOff.AutoSize = True
        LabelMagickaOff.Name = "LabelMagickaOff"
        LabelMagickaOff.Text = "Magicka Offset:"
        '
        ' NumMagickaOff
        '
        NumMagickaOff.Anchor = AnchorStyles.Left
        NumMagickaOff.Maximum = New Decimal(32767)
        NumMagickaOff.Minimum = New Decimal(New Integer() {32768, 0, 0, -2147483648})
        NumMagickaOff.Name = "NumMagickaOff"
        NumMagickaOff.Size = New Size(120, 23)
        NumMagickaOff.TabIndex = 14
        NumMagickaOff.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelStaminaOff — ACBS +6 s16, SKYRIM ONLY.
        '
        LabelStaminaOff.Anchor = AnchorStyles.Left
        LabelStaminaOff.AutoSize = True
        LabelStaminaOff.Name = "LabelStaminaOff"
        LabelStaminaOff.Text = "Stamina Offset:"
        '
        ' NumStaminaOff
        '
        NumStaminaOff.Anchor = AnchorStyles.Left
        NumStaminaOff.Maximum = New Decimal(32767)
        NumStaminaOff.Minimum = New Decimal(New Integer() {32768, 0, 0, -2147483648})
        NumStaminaOff.Name = "NumStaminaOff"
        NumStaminaOff.Size = New Size(120, 23)
        NumStaminaOff.TabIndex = 15
        NumStaminaOff.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelHealthOff — ACBS +20 s16, SKYRIM ONLY.
        '
        LabelHealthOff.Anchor = AnchorStyles.Left
        LabelHealthOff.AutoSize = True
        LabelHealthOff.Name = "LabelHealthOff"
        LabelHealthOff.Text = "Health Offset:"
        '
        ' NumHealthOff
        '
        NumHealthOff.Anchor = AnchorStyles.Left
        NumHealthOff.Maximum = New Decimal(32767)
        NumHealthOff.Minimum = New Decimal(New Integer() {32768, 0, 0, -2147483648})
        NumHealthOff.Name = "NumHealthOff"
        NumHealthOff.Size = New Size(120, 23)
        NumHealthOff.TabIndex = 16
        NumHealthOff.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelSpeedMult — ACBS +14 u16, SKYRIM ONLY (FO4's ACBS has no speed field).
        '
        LabelSpeedMult.Anchor = AnchorStyles.Left
        LabelSpeedMult.AutoSize = True
        LabelSpeedMult.Name = "LabelSpeedMult"
        LabelSpeedMult.Text = "Speed Multiplier:"
        '
        ' NumSpeedMult
        '
        NumSpeedMult.Anchor = AnchorStyles.Left
        NumSpeedMult.Maximum = New Decimal(65535)
        NumSpeedMult.Name = "NumSpeedMult"
        NumSpeedMult.Size = New Size(120, 23)
        NumSpeedMult.TabIndex = 17
        NumSpeedMult.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
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
        ' TabStats — DNAM "Player Skills", SKYRIM ONLY (removed from the TabControl on Fallout 4, whose DNAM
        ' is an unrelated 8-byte Calculated-Stats struct the engine recomputes). Layout follows the schema
        ' order of the 18 Skyrim skills, each with a Value and an Offset, then the derived attributes.
        '
        TabStats.Controls.Add(StatsLayout)
        TabStats.Location = New Point(4, 24)
        TabStats.Name = "TabStats"
        TabStats.Padding = New Padding(3)
        TabStats.Size = New Size(690, 452)
        TabStats.TabIndex = 8
        TabStats.Text = "Stats"
        TabStats.UseVisualStyleBackColor = True
        '
        ' StatsLayout
        '
        StatsLayout.ColumnCount = 6
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 75F))
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 95F))
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 75F))
        StatsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        StatsLayout.Controls.Add(LabelSkillHdrA, 0, 0)
        StatsLayout.Controls.Add(LabelValueHdrA, 1, 0)
        StatsLayout.Controls.Add(LabelOffsetHdrA, 2, 0)
        StatsLayout.Controls.Add(LabelSkillHdrB, 3, 0)
        StatsLayout.Controls.Add(LabelValueHdrB, 4, 0)
        StatsLayout.Controls.Add(LabelOffsetHdrB, 5, 0)
        StatsLayout.Controls.Add(LabelSkill0, 0, 1)
        StatsLayout.Controls.Add(NumSkillVal0, 1, 1)
        StatsLayout.Controls.Add(NumSkillOff0, 2, 1)
        StatsLayout.Controls.Add(LabelSkill1, 0, 2)
        StatsLayout.Controls.Add(NumSkillVal1, 1, 2)
        StatsLayout.Controls.Add(NumSkillOff1, 2, 2)
        StatsLayout.Controls.Add(LabelSkill2, 0, 3)
        StatsLayout.Controls.Add(NumSkillVal2, 1, 3)
        StatsLayout.Controls.Add(NumSkillOff2, 2, 3)
        StatsLayout.Controls.Add(LabelSkill3, 0, 4)
        StatsLayout.Controls.Add(NumSkillVal3, 1, 4)
        StatsLayout.Controls.Add(NumSkillOff3, 2, 4)
        StatsLayout.Controls.Add(LabelSkill4, 0, 5)
        StatsLayout.Controls.Add(NumSkillVal4, 1, 5)
        StatsLayout.Controls.Add(NumSkillOff4, 2, 5)
        StatsLayout.Controls.Add(LabelSkill5, 0, 6)
        StatsLayout.Controls.Add(NumSkillVal5, 1, 6)
        StatsLayout.Controls.Add(NumSkillOff5, 2, 6)
        StatsLayout.Controls.Add(LabelSkill6, 0, 7)
        StatsLayout.Controls.Add(NumSkillVal6, 1, 7)
        StatsLayout.Controls.Add(NumSkillOff6, 2, 7)
        StatsLayout.Controls.Add(LabelSkill7, 0, 8)
        StatsLayout.Controls.Add(NumSkillVal7, 1, 8)
        StatsLayout.Controls.Add(NumSkillOff7, 2, 8)
        StatsLayout.Controls.Add(LabelSkill8, 0, 9)
        StatsLayout.Controls.Add(NumSkillVal8, 1, 9)
        StatsLayout.Controls.Add(NumSkillOff8, 2, 9)
        StatsLayout.Controls.Add(LabelSkill9, 3, 1)
        StatsLayout.Controls.Add(NumSkillVal9, 4, 1)
        StatsLayout.Controls.Add(NumSkillOff9, 5, 1)
        StatsLayout.Controls.Add(LabelSkill10, 3, 2)
        StatsLayout.Controls.Add(NumSkillVal10, 4, 2)
        StatsLayout.Controls.Add(NumSkillOff10, 5, 2)
        StatsLayout.Controls.Add(LabelSkill11, 3, 3)
        StatsLayout.Controls.Add(NumSkillVal11, 4, 3)
        StatsLayout.Controls.Add(NumSkillOff11, 5, 3)
        StatsLayout.Controls.Add(LabelSkill12, 3, 4)
        StatsLayout.Controls.Add(NumSkillVal12, 4, 4)
        StatsLayout.Controls.Add(NumSkillOff12, 5, 4)
        StatsLayout.Controls.Add(LabelSkill13, 3, 5)
        StatsLayout.Controls.Add(NumSkillVal13, 4, 5)
        StatsLayout.Controls.Add(NumSkillOff13, 5, 5)
        StatsLayout.Controls.Add(LabelSkill14, 3, 6)
        StatsLayout.Controls.Add(NumSkillVal14, 4, 6)
        StatsLayout.Controls.Add(NumSkillOff14, 5, 6)
        StatsLayout.Controls.Add(LabelSkill15, 3, 7)
        StatsLayout.Controls.Add(NumSkillVal15, 4, 7)
        StatsLayout.Controls.Add(NumSkillOff15, 5, 7)
        StatsLayout.Controls.Add(LabelSkill16, 3, 8)
        StatsLayout.Controls.Add(NumSkillVal16, 4, 8)
        StatsLayout.Controls.Add(NumSkillOff16, 5, 8)
        StatsLayout.Controls.Add(LabelSkill17, 3, 9)
        StatsLayout.Controls.Add(NumSkillVal17, 4, 9)
        StatsLayout.Controls.Add(NumSkillOff17, 5, 9)
        StatsLayout.Controls.Add(AttrLayout, 0, 10)
        StatsLayout.Controls.Add(LabelStatsNote, 0, 11)
        StatsLayout.SetColumnSpan(AttrLayout, 6)
        StatsLayout.SetColumnSpan(LabelStatsNote, 6)
        StatsLayout.Dock = DockStyle.Fill
        StatsLayout.Location = New Point(3, 3)
        StatsLayout.Name = "StatsLayout"
        StatsLayout.Padding = New Padding(6)
        StatsLayout.RowCount = 12
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle())
        StatsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        StatsLayout.Size = New Size(684, 446)
        StatsLayout.TabIndex = 0
        '
        ' Skill column headers
        '
        LabelSkillHdrA.Anchor = AnchorStyles.Left
        LabelSkillHdrA.AutoSize = True
        LabelSkillHdrA.Name = "LabelSkillHdrA"
        LabelSkillHdrA.Text = "Skill"
        LabelValueHdrA.Anchor = AnchorStyles.Left
        LabelValueHdrA.AutoSize = True
        LabelValueHdrA.Name = "LabelValueHdrA"
        LabelValueHdrA.Text = "Value"
        LabelOffsetHdrA.Anchor = AnchorStyles.Left
        LabelOffsetHdrA.AutoSize = True
        LabelOffsetHdrA.Name = "LabelOffsetHdrA"
        LabelOffsetHdrA.Text = "Offset"
        LabelSkillHdrB.Anchor = AnchorStyles.Left
        LabelSkillHdrB.AutoSize = True
        LabelSkillHdrB.Name = "LabelSkillHdrB"
        LabelSkillHdrB.Text = "Skill"
        LabelValueHdrB.Anchor = AnchorStyles.Left
        LabelValueHdrB.AutoSize = True
        LabelValueHdrB.Name = "LabelValueHdrB"
        LabelValueHdrB.Text = "Value"
        LabelOffsetHdrB.Anchor = AnchorStyles.Left
        LabelOffsetHdrB.AutoSize = True
        LabelOffsetHdrB.Name = "LabelOffsetHdrB"
        LabelOffsetHdrB.Text = "Offset"
        '
        ' Skill rows (labels carry the schema skill names; the code-behind asserts they match)
        '
        LabelSkill0.Anchor = AnchorStyles.Left
        LabelSkill0.AutoSize = True
        LabelSkill0.Name = "LabelSkill0"
        LabelSkill0.Text = "One-Handed"
        NumSkillVal0.Anchor = AnchorStyles.Left
        NumSkillVal0.Maximum = New Decimal(255)
        NumSkillVal0.Name = "NumSkillVal0"
        NumSkillVal0.Size = New Size(65, 23)
        NumSkillVal0.TabIndex = 0
        NumSkillVal0.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff0.Anchor = AnchorStyles.Left
        NumSkillOff0.Maximum = New Decimal(255)
        NumSkillOff0.Name = "NumSkillOff0"
        NumSkillOff0.Size = New Size(65, 23)
        NumSkillOff0.TabIndex = 1
        NumSkillOff0.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill1.Anchor = AnchorStyles.Left
        LabelSkill1.AutoSize = True
        LabelSkill1.Name = "LabelSkill1"
        LabelSkill1.Text = "Two-Handed"
        NumSkillVal1.Anchor = AnchorStyles.Left
        NumSkillVal1.Maximum = New Decimal(255)
        NumSkillVal1.Name = "NumSkillVal1"
        NumSkillVal1.Size = New Size(65, 23)
        NumSkillVal1.TabIndex = 2
        NumSkillVal1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff1.Anchor = AnchorStyles.Left
        NumSkillOff1.Maximum = New Decimal(255)
        NumSkillOff1.Name = "NumSkillOff1"
        NumSkillOff1.Size = New Size(65, 23)
        NumSkillOff1.TabIndex = 3
        NumSkillOff1.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill2.Anchor = AnchorStyles.Left
        LabelSkill2.AutoSize = True
        LabelSkill2.Name = "LabelSkill2"
        LabelSkill2.Text = "Marksman"
        NumSkillVal2.Anchor = AnchorStyles.Left
        NumSkillVal2.Maximum = New Decimal(255)
        NumSkillVal2.Name = "NumSkillVal2"
        NumSkillVal2.Size = New Size(65, 23)
        NumSkillVal2.TabIndex = 4
        NumSkillVal2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff2.Anchor = AnchorStyles.Left
        NumSkillOff2.Maximum = New Decimal(255)
        NumSkillOff2.Name = "NumSkillOff2"
        NumSkillOff2.Size = New Size(65, 23)
        NumSkillOff2.TabIndex = 5
        NumSkillOff2.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill3.Anchor = AnchorStyles.Left
        LabelSkill3.AutoSize = True
        LabelSkill3.Name = "LabelSkill3"
        LabelSkill3.Text = "Block"
        NumSkillVal3.Anchor = AnchorStyles.Left
        NumSkillVal3.Maximum = New Decimal(255)
        NumSkillVal3.Name = "NumSkillVal3"
        NumSkillVal3.Size = New Size(65, 23)
        NumSkillVal3.TabIndex = 6
        NumSkillVal3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff3.Anchor = AnchorStyles.Left
        NumSkillOff3.Maximum = New Decimal(255)
        NumSkillOff3.Name = "NumSkillOff3"
        NumSkillOff3.Size = New Size(65, 23)
        NumSkillOff3.TabIndex = 7
        NumSkillOff3.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill4.Anchor = AnchorStyles.Left
        LabelSkill4.AutoSize = True
        LabelSkill4.Name = "LabelSkill4"
        LabelSkill4.Text = "Smithing"
        NumSkillVal4.Anchor = AnchorStyles.Left
        NumSkillVal4.Maximum = New Decimal(255)
        NumSkillVal4.Name = "NumSkillVal4"
        NumSkillVal4.Size = New Size(65, 23)
        NumSkillVal4.TabIndex = 8
        NumSkillVal4.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff4.Anchor = AnchorStyles.Left
        NumSkillOff4.Maximum = New Decimal(255)
        NumSkillOff4.Name = "NumSkillOff4"
        NumSkillOff4.Size = New Size(65, 23)
        NumSkillOff4.TabIndex = 9
        NumSkillOff4.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill5.Anchor = AnchorStyles.Left
        LabelSkill5.AutoSize = True
        LabelSkill5.Name = "LabelSkill5"
        LabelSkill5.Text = "Heavy Armor"
        NumSkillVal5.Anchor = AnchorStyles.Left
        NumSkillVal5.Maximum = New Decimal(255)
        NumSkillVal5.Name = "NumSkillVal5"
        NumSkillVal5.Size = New Size(65, 23)
        NumSkillVal5.TabIndex = 10
        NumSkillVal5.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff5.Anchor = AnchorStyles.Left
        NumSkillOff5.Maximum = New Decimal(255)
        NumSkillOff5.Name = "NumSkillOff5"
        NumSkillOff5.Size = New Size(65, 23)
        NumSkillOff5.TabIndex = 11
        NumSkillOff5.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill6.Anchor = AnchorStyles.Left
        LabelSkill6.AutoSize = True
        LabelSkill6.Name = "LabelSkill6"
        LabelSkill6.Text = "Light Armor"
        NumSkillVal6.Anchor = AnchorStyles.Left
        NumSkillVal6.Maximum = New Decimal(255)
        NumSkillVal6.Name = "NumSkillVal6"
        NumSkillVal6.Size = New Size(65, 23)
        NumSkillVal6.TabIndex = 12
        NumSkillVal6.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff6.Anchor = AnchorStyles.Left
        NumSkillOff6.Maximum = New Decimal(255)
        NumSkillOff6.Name = "NumSkillOff6"
        NumSkillOff6.Size = New Size(65, 23)
        NumSkillOff6.TabIndex = 13
        NumSkillOff6.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill7.Anchor = AnchorStyles.Left
        LabelSkill7.AutoSize = True
        LabelSkill7.Name = "LabelSkill7"
        LabelSkill7.Text = "Pickpocket"
        NumSkillVal7.Anchor = AnchorStyles.Left
        NumSkillVal7.Maximum = New Decimal(255)
        NumSkillVal7.Name = "NumSkillVal7"
        NumSkillVal7.Size = New Size(65, 23)
        NumSkillVal7.TabIndex = 14
        NumSkillVal7.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff7.Anchor = AnchorStyles.Left
        NumSkillOff7.Maximum = New Decimal(255)
        NumSkillOff7.Name = "NumSkillOff7"
        NumSkillOff7.Size = New Size(65, 23)
        NumSkillOff7.TabIndex = 15
        NumSkillOff7.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill8.Anchor = AnchorStyles.Left
        LabelSkill8.AutoSize = True
        LabelSkill8.Name = "LabelSkill8"
        LabelSkill8.Text = "Lockpicking"
        NumSkillVal8.Anchor = AnchorStyles.Left
        NumSkillVal8.Maximum = New Decimal(255)
        NumSkillVal8.Name = "NumSkillVal8"
        NumSkillVal8.Size = New Size(65, 23)
        NumSkillVal8.TabIndex = 16
        NumSkillVal8.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff8.Anchor = AnchorStyles.Left
        NumSkillOff8.Maximum = New Decimal(255)
        NumSkillOff8.Name = "NumSkillOff8"
        NumSkillOff8.Size = New Size(65, 23)
        NumSkillOff8.TabIndex = 17
        NumSkillOff8.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill9.Anchor = AnchorStyles.Left
        LabelSkill9.AutoSize = True
        LabelSkill9.Name = "LabelSkill9"
        LabelSkill9.Text = "Sneak"
        NumSkillVal9.Anchor = AnchorStyles.Left
        NumSkillVal9.Maximum = New Decimal(255)
        NumSkillVal9.Name = "NumSkillVal9"
        NumSkillVal9.Size = New Size(65, 23)
        NumSkillVal9.TabIndex = 18
        NumSkillVal9.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff9.Anchor = AnchorStyles.Left
        NumSkillOff9.Maximum = New Decimal(255)
        NumSkillOff9.Name = "NumSkillOff9"
        NumSkillOff9.Size = New Size(65, 23)
        NumSkillOff9.TabIndex = 19
        NumSkillOff9.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill10.Anchor = AnchorStyles.Left
        LabelSkill10.AutoSize = True
        LabelSkill10.Name = "LabelSkill10"
        LabelSkill10.Text = "Alchemy"
        NumSkillVal10.Anchor = AnchorStyles.Left
        NumSkillVal10.Maximum = New Decimal(255)
        NumSkillVal10.Name = "NumSkillVal10"
        NumSkillVal10.Size = New Size(65, 23)
        NumSkillVal10.TabIndex = 20
        NumSkillVal10.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff10.Anchor = AnchorStyles.Left
        NumSkillOff10.Maximum = New Decimal(255)
        NumSkillOff10.Name = "NumSkillOff10"
        NumSkillOff10.Size = New Size(65, 23)
        NumSkillOff10.TabIndex = 21
        NumSkillOff10.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill11.Anchor = AnchorStyles.Left
        LabelSkill11.AutoSize = True
        LabelSkill11.Name = "LabelSkill11"
        LabelSkill11.Text = "Speechcraft"
        NumSkillVal11.Anchor = AnchorStyles.Left
        NumSkillVal11.Maximum = New Decimal(255)
        NumSkillVal11.Name = "NumSkillVal11"
        NumSkillVal11.Size = New Size(65, 23)
        NumSkillVal11.TabIndex = 22
        NumSkillVal11.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff11.Anchor = AnchorStyles.Left
        NumSkillOff11.Maximum = New Decimal(255)
        NumSkillOff11.Name = "NumSkillOff11"
        NumSkillOff11.Size = New Size(65, 23)
        NumSkillOff11.TabIndex = 23
        NumSkillOff11.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill12.Anchor = AnchorStyles.Left
        LabelSkill12.AutoSize = True
        LabelSkill12.Name = "LabelSkill12"
        LabelSkill12.Text = "Alteration"
        NumSkillVal12.Anchor = AnchorStyles.Left
        NumSkillVal12.Maximum = New Decimal(255)
        NumSkillVal12.Name = "NumSkillVal12"
        NumSkillVal12.Size = New Size(65, 23)
        NumSkillVal12.TabIndex = 24
        NumSkillVal12.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff12.Anchor = AnchorStyles.Left
        NumSkillOff12.Maximum = New Decimal(255)
        NumSkillOff12.Name = "NumSkillOff12"
        NumSkillOff12.Size = New Size(65, 23)
        NumSkillOff12.TabIndex = 25
        NumSkillOff12.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill13.Anchor = AnchorStyles.Left
        LabelSkill13.AutoSize = True
        LabelSkill13.Name = "LabelSkill13"
        LabelSkill13.Text = "Conjuration"
        NumSkillVal13.Anchor = AnchorStyles.Left
        NumSkillVal13.Maximum = New Decimal(255)
        NumSkillVal13.Name = "NumSkillVal13"
        NumSkillVal13.Size = New Size(65, 23)
        NumSkillVal13.TabIndex = 26
        NumSkillVal13.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff13.Anchor = AnchorStyles.Left
        NumSkillOff13.Maximum = New Decimal(255)
        NumSkillOff13.Name = "NumSkillOff13"
        NumSkillOff13.Size = New Size(65, 23)
        NumSkillOff13.TabIndex = 27
        NumSkillOff13.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill14.Anchor = AnchorStyles.Left
        LabelSkill14.AutoSize = True
        LabelSkill14.Name = "LabelSkill14"
        LabelSkill14.Text = "Destruction"
        NumSkillVal14.Anchor = AnchorStyles.Left
        NumSkillVal14.Maximum = New Decimal(255)
        NumSkillVal14.Name = "NumSkillVal14"
        NumSkillVal14.Size = New Size(65, 23)
        NumSkillVal14.TabIndex = 28
        NumSkillVal14.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff14.Anchor = AnchorStyles.Left
        NumSkillOff14.Maximum = New Decimal(255)
        NumSkillOff14.Name = "NumSkillOff14"
        NumSkillOff14.Size = New Size(65, 23)
        NumSkillOff14.TabIndex = 29
        NumSkillOff14.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill15.Anchor = AnchorStyles.Left
        LabelSkill15.AutoSize = True
        LabelSkill15.Name = "LabelSkill15"
        LabelSkill15.Text = "Illusion"
        NumSkillVal15.Anchor = AnchorStyles.Left
        NumSkillVal15.Maximum = New Decimal(255)
        NumSkillVal15.Name = "NumSkillVal15"
        NumSkillVal15.Size = New Size(65, 23)
        NumSkillVal15.TabIndex = 30
        NumSkillVal15.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff15.Anchor = AnchorStyles.Left
        NumSkillOff15.Maximum = New Decimal(255)
        NumSkillOff15.Name = "NumSkillOff15"
        NumSkillOff15.Size = New Size(65, 23)
        NumSkillOff15.TabIndex = 31
        NumSkillOff15.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill16.Anchor = AnchorStyles.Left
        LabelSkill16.AutoSize = True
        LabelSkill16.Name = "LabelSkill16"
        LabelSkill16.Text = "Restoration"
        NumSkillVal16.Anchor = AnchorStyles.Left
        NumSkillVal16.Maximum = New Decimal(255)
        NumSkillVal16.Name = "NumSkillVal16"
        NumSkillVal16.Size = New Size(65, 23)
        NumSkillVal16.TabIndex = 32
        NumSkillVal16.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff16.Anchor = AnchorStyles.Left
        NumSkillOff16.Maximum = New Decimal(255)
        NumSkillOff16.Name = "NumSkillOff16"
        NumSkillOff16.Size = New Size(65, 23)
        NumSkillOff16.TabIndex = 33
        NumSkillOff16.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelSkill17.Anchor = AnchorStyles.Left
        LabelSkill17.AutoSize = True
        LabelSkill17.Name = "LabelSkill17"
        LabelSkill17.Text = "Enchanting"
        NumSkillVal17.Anchor = AnchorStyles.Left
        NumSkillVal17.Maximum = New Decimal(255)
        NumSkillVal17.Name = "NumSkillVal17"
        NumSkillVal17.Size = New Size(65, 23)
        NumSkillVal17.TabIndex = 34
        NumSkillVal17.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumSkillOff17.Anchor = AnchorStyles.Left
        NumSkillOff17.Maximum = New Decimal(255)
        NumSkillOff17.Name = "NumSkillOff17"
        NumSkillOff17.Size = New Size(65, 23)
        NumSkillOff17.TabIndex = 35
        NumSkillOff17.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' AttrLayout — the DNAM tail (Health/Magicka/Stamina u16, far-away model distance f32, geared-up u8)
        '
        AttrLayout.AutoSize = True
        AttrLayout.ColumnCount = 4
        AttrLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 165F))
        AttrLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 120F))
        AttrLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 165F))
        AttrLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        AttrLayout.Controls.Add(LabelHealth, 0, 0)
        AttrLayout.Controls.Add(NumHealth, 1, 0)
        AttrLayout.Controls.Add(LabelMagicka, 0, 1)
        AttrLayout.Controls.Add(NumMagicka, 1, 1)
        AttrLayout.Controls.Add(LabelStamina, 0, 2)
        AttrLayout.Controls.Add(NumStamina, 1, 2)
        AttrLayout.Controls.Add(LabelFarModel, 2, 0)
        AttrLayout.Controls.Add(NumFarModel, 3, 0)
        AttrLayout.Controls.Add(LabelGeared, 2, 1)
        AttrLayout.Controls.Add(NumGeared, 3, 1)
        AttrLayout.Dock = DockStyle.Fill
        AttrLayout.Margin = New Padding(0, 10, 0, 0)
        AttrLayout.Name = "AttrLayout"
        AttrLayout.RowCount = 3
        AttrLayout.RowStyles.Add(New RowStyle())
        AttrLayout.RowStyles.Add(New RowStyle())
        AttrLayout.RowStyles.Add(New RowStyle())
        AttrLayout.TabIndex = 36
        '
        LabelHealth.Anchor = AnchorStyles.Left
        LabelHealth.AutoSize = True
        LabelHealth.Name = "LabelHealth"
        LabelHealth.Text = "Health:"
        NumHealth.Anchor = AnchorStyles.Left
        NumHealth.Maximum = New Decimal(65535)
        NumHealth.Name = "NumHealth"
        NumHealth.Size = New Size(100, 23)
        NumHealth.TabIndex = 0
        NumHealth.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelMagicka.Anchor = AnchorStyles.Left
        LabelMagicka.AutoSize = True
        LabelMagicka.Name = "LabelMagicka"
        LabelMagicka.Text = "Magicka:"
        NumMagicka.Anchor = AnchorStyles.Left
        NumMagicka.Maximum = New Decimal(65535)
        NumMagicka.Name = "NumMagicka"
        NumMagicka.Size = New Size(100, 23)
        NumMagicka.TabIndex = 1
        NumMagicka.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelStamina.Anchor = AnchorStyles.Left
        LabelStamina.AutoSize = True
        LabelStamina.Name = "LabelStamina"
        LabelStamina.Text = "Stamina:"
        NumStamina.Anchor = AnchorStyles.Left
        NumStamina.Maximum = New Decimal(65535)
        NumStamina.Name = "NumStamina"
        NumStamina.Size = New Size(100, 23)
        NumStamina.TabIndex = 2
        NumStamina.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelFarModel.Anchor = AnchorStyles.Left
        LabelFarModel.AutoSize = True
        LabelFarModel.Name = "LabelFarModel"
        LabelFarModel.Text = "Far away model distance:"
        NumFarModel.Anchor = AnchorStyles.Left
        NumFarModel.DecimalPlaces = 6
        NumFarModel.Increment = New Decimal(New Integer() {1, 0, 0, 131072})
        NumFarModel.Maximum = New Decimal(1000000)
        NumFarModel.Minimum = New Decimal(New Integer() {1000000, 0, 0, -2147483648})
        NumFarModel.Name = "NumFarModel"
        NumFarModel.Size = New Size(100, 23)
        NumFarModel.TabIndex = 3
        NumFarModel.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        LabelGeared.Anchor = AnchorStyles.Left
        LabelGeared.AutoSize = True
        LabelGeared.Name = "LabelGeared"
        LabelGeared.Text = "Geared up weapons:"
        NumGeared.Anchor = AnchorStyles.Left
        NumGeared.Maximum = New Decimal(255)
        NumGeared.Name = "NumGeared"
        NumGeared.Size = New Size(100, 23)
        NumGeared.TabIndex = 4
        NumGeared.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelStatsNote
        '
        LabelStatsNote.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        LabelStatsNote.AutoSize = True
        LabelStatsNote.ForeColor = Color.DimGray
        LabelStatsNote.Margin = New Padding(3, 10, 3, 3)
        LabelStatsNote.Name = "LabelStatsNote"
        LabelStatsNote.TabIndex = 37
        LabelStatsNote.Text = "DNAM (Player Skills) — Skyrim only. Editing any value makes the NPC own the Stats template category."
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
        CType(NumMagickaOff, ComponentModel.ISupportInitialize).EndInit()
        CType(NumStaminaOff, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSpeedMult, ComponentModel.ISupportInitialize).EndInit()
        CType(NumHealthOff, ComponentModel.ISupportInitialize).EndInit()
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
        TabStats.ResumeLayout(False)
        StatsLayout.ResumeLayout(False)
        StatsLayout.PerformLayout()
        CType(NumSkillVal0, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff0, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal1, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff1, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal2, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff2, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal3, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff3, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal4, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff4, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal5, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff5, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal6, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff6, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal7, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff7, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal8, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff8, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal9, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff9, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal10, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff10, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal11, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff11, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal12, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff12, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal13, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff13, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal14, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff14, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal15, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff15, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal16, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff16, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillVal17, ComponentModel.ISupportInitialize).EndInit()
        CType(NumSkillOff17, ComponentModel.ISupportInitialize).EndInit()
        AttrLayout.ResumeLayout(False)
        AttrLayout.PerformLayout()
        CType(NumHealth, ComponentModel.ISupportInitialize).EndInit()
        CType(NumMagicka, ComponentModel.ISupportInitialize).EndInit()
        CType(NumStamina, ComponentModel.ISupportInitialize).EndInit()
        CType(NumFarModel, ComponentModel.ISupportInitialize).EndInit()
        CType(NumGeared, ComponentModel.ISupportInitialize).EndInit()
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
    ' --- Stats tab (DNAM Player Skills) + the SSE-only ACBS offsets on the General tab. Skyrim only. ---
    Friend WithEvents TabStats As System.Windows.Forms.TabPage
    Friend WithEvents StatsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSkillHdrA As System.Windows.Forms.Label
    Friend WithEvents LabelValueHdrA As System.Windows.Forms.Label
    Friend WithEvents LabelOffsetHdrA As System.Windows.Forms.Label
    Friend WithEvents LabelSkillHdrB As System.Windows.Forms.Label
    Friend WithEvents LabelValueHdrB As System.Windows.Forms.Label
    Friend WithEvents LabelOffsetHdrB As System.Windows.Forms.Label
    Friend WithEvents LabelSkill0 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal0 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff0 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill1 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal1 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff1 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill2 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal2 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff2 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill3 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal3 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff3 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill4 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal4 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff4 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill5 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal5 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff5 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill6 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal6 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff6 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill7 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal7 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff7 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill8 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal8 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff8 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill9 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal9 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff9 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill10 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal10 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff10 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill11 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal11 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff11 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill12 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal12 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff12 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill13 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal13 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff13 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill14 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal14 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff14 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill15 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal15 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff15 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill16 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal16 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff16 As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSkill17 As System.Windows.Forms.Label
    Friend WithEvents NumSkillVal17 As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSkillOff17 As System.Windows.Forms.NumericUpDown
    Friend WithEvents AttrLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHealth As System.Windows.Forms.Label
    Friend WithEvents NumHealth As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelMagicka As System.Windows.Forms.Label
    Friend WithEvents NumMagicka As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelStamina As System.Windows.Forms.Label
    Friend WithEvents NumStamina As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelFarModel As System.Windows.Forms.Label
    Friend WithEvents NumFarModel As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelGeared As System.Windows.Forms.Label
    Friend WithEvents NumGeared As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelStatsNote As System.Windows.Forms.Label
    Friend WithEvents LabelMagickaOff As System.Windows.Forms.Label
    Friend WithEvents NumMagickaOff As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelStaminaOff As System.Windows.Forms.Label
    Friend WithEvents NumStaminaOff As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelSpeedMult As System.Windows.Forms.Label
    Friend WithEvents NumSpeedMult As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelHealthOff As System.Windows.Forms.Label
    Friend WithEvents NumHealthOff As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelPersistNote As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
