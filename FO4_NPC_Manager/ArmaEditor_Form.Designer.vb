' UI built in Designer per 00-reglas-ui-y-vb.md.
' InitializeComponent is declarative ONLY (no For/If/lambda). The many repeated slot checkboxes are
' declared via their CONTAINER (FlowSlots) here and built in code-behind (OnLoad); the sculpt grid +
' MSWP grid columns are added in code-behind too (variable/repeated content).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ArmaEditor_Form
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
        TabModels = New TabPage()
        ModelsLayout = New TableLayoutPanel()
        GroupMeshes = New GroupBox()
        MeshesLayout = New TableLayoutPanel()
        LabelMod2 = New Label()
        TextBoxMod2 = New TextBox()
        ButtonBrowseMod2 = New Button()
        LabelMod3 = New Label()
        TextBoxMod3 = New TextBox()
        ButtonBrowseMod3 = New Button()
        LabelMod4 = New Label()
        TextBoxMod4 = New TextBox()
        ButtonBrowseMod4 = New Button()
        LabelMod5 = New Label()
        TextBoxMod5 = New TextBox()
        ButtonBrowseMod5 = New Button()
        GroupModelFlags = New GroupBox()
        ModelFlagsLayout = New TableLayoutPanel()
        CheckMo2fFaceBones = New CheckBox()
        CheckMo2f1stPerson = New CheckBox()
        CheckMo3fFaceBones = New CheckBox()
        CheckMo3f1stPerson = New CheckBox()
        LabelMo2f = New Label()
        LabelMo3f = New Label()
        GroupModelExtras = New GroupBox()
        ModelExtrasLayout = New TableLayoutPanel()
        TabSlots = New TabPage()
        SlotsLayout = New TableLayoutPanel()
        LabelSlots = New Label()
        FlowSlots = New FlowLayoutPanel()
        TabSkin = New TabPage()
        SkinLayout = New TableLayoutPanel()
        GroupRace = New GroupBox()
        RaceLayout = New TableLayoutPanel()
        GroupSkinTextures = New GroupBox()
        SkinTexturesLayout = New TableLayoutPanel()
        LabelRace = New Label()
        TextBoxRace = New TextBox()
        ButtonPickRace = New Button()
        LabelAddRaces = New Label()
        ListAddRaces = New ListView()
        ColAddRace = New ColumnHeader()
        AddRacesButtons = New FlowLayoutPanel()
        ButtonAddRace = New Button()
        ButtonRemoveRace = New Button()
        LabelNam0 = New Label()
        TextBoxNam0 = New TextBox()
        ButtonPickNam0 = New Button()
        LabelNam1 = New Label()
        TextBoxNam1 = New TextBox()
        ButtonPickNam1 = New Button()
        LabelNam2 = New Label()
        TextBoxNam2 = New TextBox()
        ButtonPickNam2 = New Button()
        LabelNam3 = New Label()
        TextBoxNam3 = New TextBox()
        ButtonPickNam3 = New Button()
        LabelMo2s = New Label()
        TextBoxMo2s = New TextBox()
        ButtonPickMo2s = New Button()
        ButtonEditMo2s = New Button()
        LabelMo3s = New Label()
        TextBoxMo3s = New TextBox()
        ButtonPickMo3s = New Button()
        ButtonEditMo3s = New Button()
        LabelSndd = New Label()
        TextBoxSndd = New TextBox()
        ButtonPickSndd = New Button()
        LabelOnam = New Label()
        TextBoxOnam = New TextBox()
        ButtonPickOnam = New Button()
        LabelMo4s = New Label()
        TextBoxMo4s = New TextBox()
        ButtonPickMo4s = New Button()
        LabelMo5s = New Label()
        TextBoxMo5s = New TextBox()
        ButtonPickMo5s = New Button()
        GroupPriorities = New GroupBox()
        PrioritiesLayout = New TableLayoutPanel()
        LabelMalePrio = New Label()
        NumMalePrio = New NumericUpDown()
        CheckMaleWeight = New CheckBox()
        LabelFemalePrio = New Label()
        NumFemalePrio = New NumericUpDown()
        CheckFemaleWeight = New CheckBox()
        LabelDetectionSound = New Label()
        NumDetectionSound = New NumericUpDown()
        LabelWeaponAdjust = New Label()
        NumWeaponAdjust = New NumericUpDown()
        TabData = New TabPage()
        DataLayout = New TableLayoutPanel()
        TabSculpt = New TabPage()
        SculptLayout = New TableLayoutPanel()
        SculptTopRow = New FlowLayoutPanel()
        LabelSculptGender = New Label()
        RadioSculptMale = New RadioButton()
        RadioSculptFemale = New RadioButton()
        SculptHeaderRow = New TableLayoutPanel()
        LabelSculptColBone = New Label()
        LabelSculptColX = New Label()
        LabelSculptColY = New Label()
        LabelSculptColZ = New Label()
        LabelSculptColRemove = New Label()
        SculptPanel = New FlowLayoutPanel()
        SculptButtons = New FlowLayoutPanel()
        LabelSculptAddBone = New Label()
        ComboSculptAddBone = New ComboBox()
        ButtonSculptAddRow = New Button()
        ButtonSculptLoad = New Button()
        ButtonSculptEstimate = New Button()
        ButtonSculptSave = New Button()
        TabFlags = New TabPage()
        FlagsLayout = New TableLayoutPanel()
        CheckNoUnderarmorScaling = New CheckBox()
        CheckHasSculptData = New CheckBox()
        PreviewLayout = New TableLayoutPanel()
        PreviewControlPanel = New Panel()
        LabelPreviewHint = New Label()
        PreviewModePanel = New FlowLayoutPanel()
        RadioOnlyModel = New RadioButton()
        RadioFullArmor = New RadioButton()
        RadioFullOutfit = New RadioButton()
        CheckIncludeBody = New CheckBox()
        CheckShowOtherGender = New CheckBox()
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
        TabModels.SuspendLayout()
        ModelsLayout.SuspendLayout()
        GroupMeshes.SuspendLayout()
        MeshesLayout.SuspendLayout()
        GroupModelFlags.SuspendLayout()
        ModelFlagsLayout.SuspendLayout()
        GroupModelExtras.SuspendLayout()
        ModelExtrasLayout.SuspendLayout()
        TabSlots.SuspendLayout()
        SlotsLayout.SuspendLayout()
        TabSkin.SuspendLayout()
        SkinLayout.SuspendLayout()
        GroupRace.SuspendLayout()
        RaceLayout.SuspendLayout()
        GroupSkinTextures.SuspendLayout()
        SkinTexturesLayout.SuspendLayout()
        AddRacesButtons.SuspendLayout()
        GroupPriorities.SuspendLayout()
        PrioritiesLayout.SuspendLayout()
        CType(NumMalePrio, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumFemalePrio, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumDetectionSound, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumWeaponAdjust, ComponentModel.ISupportInitialize).BeginInit()
        TabData.SuspendLayout()
        DataLayout.SuspendLayout()
        TabSculpt.SuspendLayout()
        SculptLayout.SuspendLayout()
        SculptTopRow.SuspendLayout()
        SculptHeaderRow.SuspendLayout()
        SculptButtons.SuspendLayout()
        TabFlags.SuspendLayout()
        FlagsLayout.SuspendLayout()
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
        Tabs.Controls.Add(TabModels)
        Tabs.Controls.Add(TabSlots)
        Tabs.Controls.Add(TabSkin)
        Tabs.Controls.Add(TabData)
        Tabs.Controls.Add(TabSculpt)
        Tabs.Controls.Add(TabFlags)
        Tabs.Dock = DockStyle.Fill
        Tabs.Location = New Point(0, 0)
        Tabs.Name = "Tabs"
        Tabs.SelectedIndex = 0
        Tabs.Size = New Size(720, 550)
        Tabs.TabIndex = 0
        '
        ' TabModels
        '
        TabModels.Controls.Add(ModelsLayout)
        TabModels.Location = New Point(4, 24)
        TabModels.Name = "TabModels"
        TabModels.Padding = New Padding(6)
        TabModels.Size = New Size(712, 522)
        TabModels.TabIndex = 0
        TabModels.Text = "Models"
        TabModels.UseVisualStyleBackColor = True
        '
        ' ModelsLayout
        '
        ModelsLayout.ColumnCount = 1
        ModelsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ModelsLayout.Controls.Add(GroupMeshes, 0, 0)
        ModelsLayout.Controls.Add(GroupModelFlags, 0, 1)
        ModelsLayout.Controls.Add(GroupModelExtras, 0, 2)
        ModelsLayout.Dock = DockStyle.Fill
        ModelsLayout.Location = New Point(6, 6)
        ModelsLayout.Name = "ModelsLayout"
        ModelsLayout.RowCount = 4
        ModelsLayout.RowStyles.Add(New RowStyle())
        ModelsLayout.RowStyles.Add(New RowStyle())
        ModelsLayout.RowStyles.Add(New RowStyle())
        ModelsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ModelsLayout.Size = New Size(700, 510)
        ModelsLayout.TabIndex = 0
        '
        ' GroupMeshes
        '
        GroupMeshes.AutoSize = True
        GroupMeshes.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupMeshes.Controls.Add(MeshesLayout)
        GroupMeshes.Dock = DockStyle.Fill
        GroupMeshes.Location = New Point(3, 3)
        GroupMeshes.Name = "GroupMeshes"
        GroupMeshes.Padding = New Padding(4)
        GroupMeshes.Size = New Size(694, 130)
        GroupMeshes.TabIndex = 0
        GroupMeshes.TabStop = False
        GroupMeshes.Text = "Meshes (MOD2 male / MOD3 female / MOD4–5 first-person)"
        '
        ' MeshesLayout
        '
        MeshesLayout.ColumnCount = 3
        MeshesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        MeshesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        MeshesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        MeshesLayout.Controls.Add(LabelMod2, 0, 0)
        MeshesLayout.Controls.Add(TextBoxMod2, 1, 0)
        MeshesLayout.Controls.Add(ButtonBrowseMod2, 2, 0)
        MeshesLayout.Controls.Add(LabelMod3, 0, 1)
        MeshesLayout.Controls.Add(TextBoxMod3, 1, 1)
        MeshesLayout.Controls.Add(ButtonBrowseMod3, 2, 1)
        MeshesLayout.Controls.Add(LabelMod4, 0, 2)
        MeshesLayout.Controls.Add(TextBoxMod4, 1, 2)
        MeshesLayout.Controls.Add(ButtonBrowseMod4, 2, 2)
        MeshesLayout.Controls.Add(LabelMod5, 0, 3)
        MeshesLayout.Controls.Add(TextBoxMod5, 1, 3)
        MeshesLayout.Controls.Add(ButtonBrowseMod5, 2, 3)
        MeshesLayout.AutoSize = True
        MeshesLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        MeshesLayout.Dock = DockStyle.Fill
        MeshesLayout.Location = New Point(4, 20)
        MeshesLayout.Name = "MeshesLayout"
        MeshesLayout.RowCount = 4
        MeshesLayout.RowStyles.Add(New RowStyle())
        MeshesLayout.RowStyles.Add(New RowStyle())
        MeshesLayout.RowStyles.Add(New RowStyle())
        MeshesLayout.RowStyles.Add(New RowStyle())
        MeshesLayout.Size = New Size(686, 106)
        MeshesLayout.TabIndex = 0
        '
        ' LabelMod2
        '
        LabelMod2.Anchor = AnchorStyles.Left
        LabelMod2.AutoSize = True
        LabelMod2.Location = New Point(3, 8)
        LabelMod2.Name = "LabelMod2"
        LabelMod2.Size = New Size(110, 15)
        LabelMod2.TabIndex = 0
        LabelMod2.Text = "Male mesh (MOD2):"
        '
        ' TextBoxMod2
        '
        TextBoxMod2.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod2.Location = New Point(153, 4)
        TextBoxMod2.Name = "TextBoxMod2"
        TextBoxMod2.PlaceholderText = "Meshes\..."
        TextBoxMod2.Size = New Size(454, 23)
        TextBoxMod2.TabIndex = 1
        '
        ' ButtonBrowseMod2
        '
        ButtonBrowseMod2.Anchor = AnchorStyles.Left
        ButtonBrowseMod2.Location = New Point(613, 3)
        ButtonBrowseMod2.Name = "ButtonBrowseMod2"
        ButtonBrowseMod2.Size = New Size(80, 24)
        ButtonBrowseMod2.TabIndex = 2
        ButtonBrowseMod2.Text = "Browse…"
        ButtonBrowseMod2.UseVisualStyleBackColor = True
        '
        ' LabelMod3
        '
        LabelMod3.Anchor = AnchorStyles.Left
        LabelMod3.AutoSize = True
        LabelMod3.Location = New Point(3, 39)
        LabelMod3.Name = "LabelMod3"
        LabelMod3.Size = New Size(120, 15)
        LabelMod3.TabIndex = 3
        LabelMod3.Text = "Female mesh (MOD3):"
        '
        ' TextBoxMod3
        '
        TextBoxMod3.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod3.Location = New Point(153, 35)
        TextBoxMod3.Name = "TextBoxMod3"
        TextBoxMod3.PlaceholderText = "Meshes\..."
        TextBoxMod3.Size = New Size(454, 23)
        TextBoxMod3.TabIndex = 4
        '
        ' ButtonBrowseMod3
        '
        ButtonBrowseMod3.Anchor = AnchorStyles.Left
        ButtonBrowseMod3.Location = New Point(613, 34)
        ButtonBrowseMod3.Name = "ButtonBrowseMod3"
        ButtonBrowseMod3.Size = New Size(80, 24)
        ButtonBrowseMod3.TabIndex = 5
        ButtonBrowseMod3.Text = "Browse…"
        ButtonBrowseMod3.UseVisualStyleBackColor = True
        '
        ' LabelMod4
        '
        LabelMod4.Anchor = AnchorStyles.Left
        LabelMod4.AutoSize = True
        LabelMod4.Location = New Point(3, 70)
        LabelMod4.Name = "LabelMod4"
        LabelMod4.Size = New Size(140, 15)
        LabelMod4.TabIndex = 6
        LabelMod4.Text = "Male 1st-person (MOD4):"
        '
        ' TextBoxMod4
        '
        TextBoxMod4.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod4.Location = New Point(153, 66)
        TextBoxMod4.Name = "TextBoxMod4"
        TextBoxMod4.PlaceholderText = "Meshes\... (optional)"
        TextBoxMod4.Size = New Size(454, 23)
        TextBoxMod4.TabIndex = 7
        '
        ' ButtonBrowseMod4
        '
        ButtonBrowseMod4.Anchor = AnchorStyles.Left
        ButtonBrowseMod4.Location = New Point(613, 65)
        ButtonBrowseMod4.Name = "ButtonBrowseMod4"
        ButtonBrowseMod4.Size = New Size(80, 24)
        ButtonBrowseMod4.TabIndex = 8
        ButtonBrowseMod4.Text = "Browse…"
        ButtonBrowseMod4.UseVisualStyleBackColor = True
        '
        ' LabelMod5
        '
        LabelMod5.Anchor = AnchorStyles.Left
        LabelMod5.AutoSize = True
        LabelMod5.Location = New Point(3, 101)
        LabelMod5.Name = "LabelMod5"
        LabelMod5.Size = New Size(150, 15)
        LabelMod5.TabIndex = 9
        LabelMod5.Text = "Female 1st-person (MOD5):"
        '
        ' TextBoxMod5
        '
        TextBoxMod5.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMod5.Location = New Point(153, 97)
        TextBoxMod5.Name = "TextBoxMod5"
        TextBoxMod5.PlaceholderText = "Meshes\... (optional)"
        TextBoxMod5.Size = New Size(454, 23)
        TextBoxMod5.TabIndex = 10
        '
        ' ButtonBrowseMod5
        '
        ButtonBrowseMod5.Anchor = AnchorStyles.Left
        ButtonBrowseMod5.Location = New Point(613, 96)
        ButtonBrowseMod5.Name = "ButtonBrowseMod5"
        ButtonBrowseMod5.Size = New Size(80, 24)
        ButtonBrowseMod5.TabIndex = 11
        ButtonBrowseMod5.Text = "Browse…"
        ButtonBrowseMod5.UseVisualStyleBackColor = True
        '
        ' GroupModelFlags
        '
        GroupModelFlags.AutoSize = True
        GroupModelFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupModelFlags.Controls.Add(ModelFlagsLayout)
        GroupModelFlags.Dock = DockStyle.Fill
        GroupModelFlags.Location = New Point(3, 131)
        GroupModelFlags.Name = "GroupModelFlags"
        GroupModelFlags.Size = New Size(694, 90)
        GroupModelFlags.TabIndex = 12
        GroupModelFlags.TabStop = False
        GroupModelFlags.Text = "Model flags (MO2F male / MO3F female)"
        '
        ' ModelFlagsLayout
        '
        ModelFlagsLayout.ColumnCount = 3
        ModelFlagsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 80F))
        ModelFlagsLayout.ColumnStyles.Add(New ColumnStyle())
        ModelFlagsLayout.ColumnStyles.Add(New ColumnStyle())
        ModelFlagsLayout.Controls.Add(LabelMo2f, 0, 0)
        ModelFlagsLayout.Controls.Add(CheckMo2fFaceBones, 1, 0)
        ModelFlagsLayout.Controls.Add(CheckMo2f1stPerson, 2, 0)
        ModelFlagsLayout.Controls.Add(LabelMo3f, 0, 1)
        ModelFlagsLayout.Controls.Add(CheckMo3fFaceBones, 1, 1)
        ModelFlagsLayout.Controls.Add(CheckMo3f1stPerson, 2, 1)
        ModelFlagsLayout.AutoSize = True
        ModelFlagsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModelFlagsLayout.Dock = DockStyle.Fill
        ModelFlagsLayout.Location = New Point(3, 19)
        ModelFlagsLayout.Name = "ModelFlagsLayout"
        ModelFlagsLayout.RowCount = 2
        ModelFlagsLayout.RowStyles.Add(New RowStyle())
        ModelFlagsLayout.RowStyles.Add(New RowStyle())
        ModelFlagsLayout.Size = New Size(688, 60)
        ModelFlagsLayout.TabIndex = 0
        '
        ' LabelMo2f
        '
        LabelMo2f.Anchor = AnchorStyles.Left
        LabelMo2f.AutoSize = True
        LabelMo2f.Location = New Point(3, 6)
        LabelMo2f.Name = "LabelMo2f"
        LabelMo2f.Size = New Size(38, 15)
        LabelMo2f.TabIndex = 0
        LabelMo2f.Text = "Male:"
        '
        ' CheckMo2fFaceBones
        '
        CheckMo2fFaceBones.Anchor = AnchorStyles.Left
        CheckMo2fFaceBones.AutoSize = True
        CheckMo2fFaceBones.Location = New Point(83, 4)
        CheckMo2fFaceBones.Name = "CheckMo2fFaceBones"
        CheckMo2fFaceBones.Size = New Size(165, 19)
        CheckMo2fFaceBones.TabIndex = 1
        CheckMo2fFaceBones.Text = "Has FaceBones Model (0x01)"
        CheckMo2fFaceBones.UseVisualStyleBackColor = True
        '
        ' CheckMo2f1stPerson
        '
        CheckMo2f1stPerson.Anchor = AnchorStyles.Left
        CheckMo2f1stPerson.AutoSize = True
        CheckMo2f1stPerson.Location = New Point(254, 4)
        CheckMo2f1stPerson.Name = "CheckMo2f1stPerson"
        CheckMo2f1stPerson.Size = New Size(175, 19)
        CheckMo2f1stPerson.TabIndex = 2
        CheckMo2f1stPerson.Text = "Has 1st-Person Model (0x02)"
        CheckMo2f1stPerson.UseVisualStyleBackColor = True
        '
        ' LabelMo3f
        '
        LabelMo3f.Anchor = AnchorStyles.Left
        LabelMo3f.AutoSize = True
        LabelMo3f.Location = New Point(3, 36)
        LabelMo3f.Name = "LabelMo3f"
        LabelMo3f.Size = New Size(50, 15)
        LabelMo3f.TabIndex = 3
        LabelMo3f.Text = "Female:"
        '
        ' CheckMo3fFaceBones
        '
        CheckMo3fFaceBones.Anchor = AnchorStyles.Left
        CheckMo3fFaceBones.AutoSize = True
        CheckMo3fFaceBones.Location = New Point(83, 34)
        CheckMo3fFaceBones.Name = "CheckMo3fFaceBones"
        CheckMo3fFaceBones.Size = New Size(165, 19)
        CheckMo3fFaceBones.TabIndex = 4
        CheckMo3fFaceBones.Text = "Has FaceBones Model (0x01)"
        CheckMo3fFaceBones.UseVisualStyleBackColor = True
        '
        ' CheckMo3f1stPerson
        '
        CheckMo3f1stPerson.Anchor = AnchorStyles.Left
        CheckMo3f1stPerson.AutoSize = True
        CheckMo3f1stPerson.Location = New Point(254, 34)
        CheckMo3f1stPerson.Name = "CheckMo3f1stPerson"
        CheckMo3f1stPerson.Size = New Size(175, 19)
        CheckMo3f1stPerson.TabIndex = 5
        CheckMo3f1stPerson.Text = "Has 1st-Person Model (0x02)"
        CheckMo3f1stPerson.UseVisualStyleBackColor = True
        '
        ' GroupModelExtras
        '
        GroupModelExtras.AutoSize = True
        GroupModelExtras.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupModelExtras.Controls.Add(ModelExtrasLayout)
        GroupModelExtras.Dock = DockStyle.Fill
        GroupModelExtras.Location = New Point(3, 227)
        GroupModelExtras.Name = "GroupModelExtras"
        GroupModelExtras.Padding = New Padding(4)
        GroupModelExtras.Size = New Size(694, 90)
        GroupModelExtras.TabIndex = 13
        GroupModelExtras.TabStop = False
        GroupModelExtras.Text = "Sounds & art (Footstep FSTS + Art Object ONAM)"
        '
        ' ModelExtrasLayout
        '
        ModelExtrasLayout.ColumnCount = 3
        ModelExtrasLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        ModelExtrasLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ModelExtrasLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        ModelExtrasLayout.Controls.Add(LabelSndd, 0, 0)
        ModelExtrasLayout.Controls.Add(TextBoxSndd, 1, 0)
        ModelExtrasLayout.Controls.Add(ButtonPickSndd, 2, 0)
        ModelExtrasLayout.Controls.Add(LabelOnam, 0, 1)
        ModelExtrasLayout.Controls.Add(TextBoxOnam, 1, 1)
        ModelExtrasLayout.Controls.Add(ButtonPickOnam, 2, 1)
        ModelExtrasLayout.AutoSize = True
        ModelExtrasLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModelExtrasLayout.Dock = DockStyle.Fill
        ModelExtrasLayout.Location = New Point(4, 20)
        ModelExtrasLayout.Name = "ModelExtrasLayout"
        ModelExtrasLayout.RowCount = 2
        ModelExtrasLayout.RowStyles.Add(New RowStyle())
        ModelExtrasLayout.RowStyles.Add(New RowStyle())
        ModelExtrasLayout.Size = New Size(686, 66)
        ModelExtrasLayout.TabIndex = 0
        '
        ' TabSlots
        '
        TabSlots.Controls.Add(SlotsLayout)
        TabSlots.Location = New Point(4, 24)
        TabSlots.Name = "TabSlots"
        TabSlots.Padding = New Padding(6)
        TabSlots.Size = New Size(712, 522)
        TabSlots.TabIndex = 1
        TabSlots.Text = "Slots"
        TabSlots.UseVisualStyleBackColor = True
        '
        ' SlotsLayout
        '
        SlotsLayout.ColumnCount = 1
        SlotsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SlotsLayout.Controls.Add(LabelSlots, 0, 0)
        SlotsLayout.Controls.Add(FlowSlots, 0, 1)
        SlotsLayout.Dock = DockStyle.Fill
        SlotsLayout.Location = New Point(6, 6)
        SlotsLayout.Name = "SlotsLayout"
        SlotsLayout.RowCount = 2
        SlotsLayout.RowStyles.Add(New RowStyle())
        SlotsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SlotsLayout.Size = New Size(700, 510)
        SlotsLayout.TabIndex = 0
        '
        ' LabelSlots
        '
        LabelSlots.AutoSize = True
        LabelSlots.Location = New Point(3, 0)
        LabelSlots.Name = "LabelSlots"
        LabelSlots.Size = New Size(280, 15)
        LabelSlots.TabIndex = 0
        LabelSlots.Text = "Biped slots (BOD2) — check the slots this addon occupies:"
        '
        ' FlowSlots
        '
        FlowSlots.AutoScroll = True
        FlowSlots.Dock = DockStyle.Fill
        FlowSlots.Location = New Point(3, 18)
        FlowSlots.Name = "FlowSlots"
        FlowSlots.Size = New Size(694, 489)
        FlowSlots.TabIndex = 1
        '
        ' TabSkin
        '
        TabSkin.Controls.Add(SkinLayout)
        TabSkin.Location = New Point(4, 24)
        TabSkin.Name = "TabSkin"
        TabSkin.Padding = New Padding(6)
        TabSkin.Size = New Size(712, 522)
        TabSkin.TabIndex = 2
        TabSkin.Text = "Skin & Material"
        TabSkin.UseVisualStyleBackColor = True
        '
        ' SkinLayout
        '
        SkinLayout.ColumnCount = 1
        SkinLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinLayout.Controls.Add(GroupRace, 0, 0)
        SkinLayout.Controls.Add(GroupSkinTextures, 0, 1)
        SkinLayout.Dock = DockStyle.Fill
        SkinLayout.Location = New Point(6, 6)
        SkinLayout.Name = "SkinLayout"
        SkinLayout.RowCount = 3
        SkinLayout.RowStyles.Add(New RowStyle())
        SkinLayout.RowStyles.Add(New RowStyle())
        SkinLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SkinLayout.Size = New Size(700, 510)
        SkinLayout.TabIndex = 0
        '
        ' GroupRace
        '
        GroupRace.AutoSize = True
        GroupRace.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupRace.Controls.Add(RaceLayout)
        GroupRace.Dock = DockStyle.Fill
        GroupRace.Location = New Point(3, 3)
        GroupRace.Name = "GroupRace"
        GroupRace.Padding = New Padding(4)
        GroupRace.Size = New Size(694, 160)
        GroupRace.TabIndex = 0
        GroupRace.TabStop = False
        GroupRace.Text = "Race (RNAM + additional races MODL)"
        '
        ' RaceLayout
        '
        RaceLayout.ColumnCount = 3
        RaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        RaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 50F))
        RaceLayout.Controls.Add(LabelRace, 0, 0)
        RaceLayout.Controls.Add(TextBoxRace, 1, 0)
        RaceLayout.Controls.Add(ButtonPickRace, 2, 0)
        RaceLayout.Controls.Add(LabelAddRaces, 0, 1)
        RaceLayout.Controls.Add(ListAddRaces, 1, 1)
        RaceLayout.Controls.Add(AddRacesButtons, 2, 1)
        RaceLayout.AutoSize = True
        RaceLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RaceLayout.Dock = DockStyle.Fill
        RaceLayout.Location = New Point(4, 20)
        RaceLayout.Name = "RaceLayout"
        RaceLayout.RowCount = 2
        RaceLayout.RowStyles.Add(New RowStyle())
        RaceLayout.RowStyles.Add(New RowStyle(SizeType.Absolute, 110F))
        RaceLayout.Size = New Size(686, 136)
        RaceLayout.TabIndex = 0
        '
        ' GroupSkinTextures
        '
        GroupSkinTextures.AutoSize = True
        GroupSkinTextures.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupSkinTextures.Controls.Add(SkinTexturesLayout)
        GroupSkinTextures.Dock = DockStyle.Fill
        GroupSkinTextures.Location = New Point(3, 169)
        GroupSkinTextures.Name = "GroupSkinTextures"
        GroupSkinTextures.Padding = New Padding(4)
        GroupSkinTextures.Size = New Size(694, 150)
        GroupSkinTextures.TabIndex = 1
        GroupSkinTextures.TabStop = False
        GroupSkinTextures.Text = "Skin textures & material swaps (NAM0–3, MO2S–5S)"
        '
        ' SkinTexturesLayout
        '
        SkinTexturesLayout.ColumnCount = 4
        SkinTexturesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 210F))
        SkinTexturesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinTexturesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        SkinTexturesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 110F))
        SkinTexturesLayout.Controls.Add(LabelNam0, 0, 0)
        SkinTexturesLayout.Controls.Add(TextBoxNam0, 1, 0)
        SkinTexturesLayout.Controls.Add(ButtonPickNam0, 2, 0)
        SkinTexturesLayout.Controls.Add(LabelNam1, 0, 1)
        SkinTexturesLayout.Controls.Add(TextBoxNam1, 1, 1)
        SkinTexturesLayout.Controls.Add(ButtonPickNam1, 2, 1)
        SkinTexturesLayout.Controls.Add(LabelNam2, 0, 2)
        SkinTexturesLayout.Controls.Add(TextBoxNam2, 1, 2)
        SkinTexturesLayout.Controls.Add(ButtonPickNam2, 2, 2)
        SkinTexturesLayout.Controls.Add(LabelNam3, 0, 3)
        SkinTexturesLayout.Controls.Add(TextBoxNam3, 1, 3)
        SkinTexturesLayout.Controls.Add(ButtonPickNam3, 2, 3)
        SkinTexturesLayout.Controls.Add(LabelMo2s, 0, 4)
        SkinTexturesLayout.Controls.Add(TextBoxMo2s, 1, 4)
        SkinTexturesLayout.Controls.Add(ButtonPickMo2s, 2, 4)
        SkinTexturesLayout.Controls.Add(ButtonEditMo2s, 3, 4)
        SkinTexturesLayout.Controls.Add(LabelMo3s, 0, 5)
        SkinTexturesLayout.Controls.Add(TextBoxMo3s, 1, 5)
        SkinTexturesLayout.Controls.Add(ButtonPickMo3s, 2, 5)
        SkinTexturesLayout.Controls.Add(ButtonEditMo3s, 3, 5)
        SkinTexturesLayout.Controls.Add(LabelMo4s, 0, 6)
        SkinTexturesLayout.Controls.Add(TextBoxMo4s, 1, 6)
        SkinTexturesLayout.Controls.Add(ButtonPickMo4s, 2, 6)
        SkinTexturesLayout.Controls.Add(LabelMo5s, 0, 7)
        SkinTexturesLayout.Controls.Add(TextBoxMo5s, 1, 7)
        SkinTexturesLayout.Controls.Add(ButtonPickMo5s, 2, 7)
        SkinTexturesLayout.AutoSize = True
        SkinTexturesLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SkinTexturesLayout.Dock = DockStyle.Fill
        SkinTexturesLayout.Location = New Point(4, 20)
        SkinTexturesLayout.Name = "SkinTexturesLayout"
        SkinTexturesLayout.RowCount = 8
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.RowStyles.Add(New RowStyle())
        SkinTexturesLayout.Size = New Size(686, 126)
        SkinTexturesLayout.TabIndex = 0
        '
        ' LabelRace
        '
        LabelRace.Anchor = AnchorStyles.Left
        LabelRace.AutoSize = True
        LabelRace.Location = New Point(3, 8)
        LabelRace.Name = "LabelRace"
        LabelRace.Size = New Size(80, 15)
        LabelRace.TabIndex = 0
        LabelRace.Text = "Race (RNAM):"
        '
        ' TextBoxRace
        '
        TextBoxRace.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxRace.Location = New Point(178, 4)
        TextBoxRace.Name = "TextBoxRace"
        TextBoxRace.ReadOnly = True
        TextBoxRace.Size = New Size(469, 23)
        TextBoxRace.TabIndex = 1
        '
        ' ButtonPickRace
        '
        ButtonPickRace.Anchor = AnchorStyles.Left
        ButtonPickRace.Location = New Point(653, 3)
        ButtonPickRace.Name = "ButtonPickRace"
        ButtonPickRace.Size = New Size(34, 24)
        ButtonPickRace.TabIndex = 2
        ButtonPickRace.Text = "…"
        ButtonPickRace.UseVisualStyleBackColor = True
        '
        ' LabelAddRaces
        '
        LabelAddRaces.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        LabelAddRaces.AutoSize = True
        LabelAddRaces.Location = New Point(3, 34)
        LabelAddRaces.Name = "LabelAddRaces"
        LabelAddRaces.Size = New Size(150, 15)
        LabelAddRaces.TabIndex = 3
        LabelAddRaces.Text = "Additional races (MODL):"
        '
        ' ListAddRaces
        '
        ListAddRaces.Anchor = AnchorStyles.Left Or AnchorStyles.Right Or AnchorStyles.Top Or AnchorStyles.Bottom
        ListAddRaces.Columns.AddRange(New ColumnHeader() {ColAddRace})
        ListAddRaces.FullRowSelect = True
        ListAddRaces.Location = New Point(178, 34)
        ListAddRaces.MultiSelect = False
        ListAddRaces.Name = "ListAddRaces"
        ListAddRaces.Size = New Size(469, 102)
        ListAddRaces.TabIndex = 4
        ListAddRaces.UseCompatibleStateImageBehavior = False
        ListAddRaces.View = View.Details
        '
        ' ColAddRace
        '
        ColAddRace.Text = "Race"
        ColAddRace.Width = 440
        '
        ' AddRacesButtons
        '
        AddRacesButtons.Anchor = AnchorStyles.Left Or AnchorStyles.Top
        AddRacesButtons.AutoSize = True
        AddRacesButtons.Controls.Add(ButtonAddRace)
        AddRacesButtons.Controls.Add(ButtonRemoveRace)
        AddRacesButtons.FlowDirection = FlowDirection.TopDown
        AddRacesButtons.Location = New Point(653, 37)
        AddRacesButtons.Name = "AddRacesButtons"
        AddRacesButtons.Size = New Size(44, 60)
        AddRacesButtons.TabIndex = 5
        '
        ' ButtonAddRace
        '
        ButtonAddRace.Location = New Point(3, 3)
        ButtonAddRace.Name = "ButtonAddRace"
        ButtonAddRace.Size = New Size(38, 24)
        ButtonAddRace.TabIndex = 0
        ButtonAddRace.Text = "Add"
        ButtonAddRace.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveRace
        '
        ButtonRemoveRace.Location = New Point(3, 33)
        ButtonRemoveRace.Name = "ButtonRemoveRace"
        ButtonRemoveRace.Size = New Size(38, 24)
        ButtonRemoveRace.TabIndex = 1
        ButtonRemoveRace.Text = "Del"
        ButtonRemoveRace.UseVisualStyleBackColor = True
        '
        ' LabelNam0
        '
        LabelNam0.Anchor = AnchorStyles.Left
        LabelNam0.AutoSize = True
        LabelNam0.Location = New Point(3, 147)
        LabelNam0.Name = "LabelNam0"
        LabelNam0.Size = New Size(150, 15)
        LabelNam0.TabIndex = 6
        LabelNam0.Text = "Male skin TXST (NAM0):"
        '
        ' TextBoxNam0
        '
        TextBoxNam0.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxNam0.Location = New Point(178, 143)
        TextBoxNam0.Name = "TextBoxNam0"
        TextBoxNam0.ReadOnly = True
        TextBoxNam0.Size = New Size(469, 23)
        TextBoxNam0.TabIndex = 7
        '
        ' ButtonPickNam0
        '
        ButtonPickNam0.Anchor = AnchorStyles.Left
        ButtonPickNam0.Location = New Point(653, 142)
        ButtonPickNam0.Name = "ButtonPickNam0"
        ButtonPickNam0.Size = New Size(34, 24)
        ButtonPickNam0.TabIndex = 8
        ButtonPickNam0.Text = "…"
        ButtonPickNam0.UseVisualStyleBackColor = True
        '
        ' LabelNam1
        '
        LabelNam1.Anchor = AnchorStyles.Left
        LabelNam1.AutoSize = True
        LabelNam1.Location = New Point(3, 177)
        LabelNam1.Name = "LabelNam1"
        LabelNam1.Size = New Size(160, 15)
        LabelNam1.TabIndex = 9
        LabelNam1.Text = "Female skin TXST (NAM1):"
        '
        ' TextBoxNam1
        '
        TextBoxNam1.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxNam1.Location = New Point(178, 173)
        TextBoxNam1.Name = "TextBoxNam1"
        TextBoxNam1.ReadOnly = True
        TextBoxNam1.Size = New Size(469, 23)
        TextBoxNam1.TabIndex = 10
        '
        ' ButtonPickNam1
        '
        ButtonPickNam1.Anchor = AnchorStyles.Left
        ButtonPickNam1.Location = New Point(653, 172)
        ButtonPickNam1.Name = "ButtonPickNam1"
        ButtonPickNam1.Size = New Size(34, 24)
        ButtonPickNam1.TabIndex = 11
        ButtonPickNam1.Text = "…"
        ButtonPickNam1.UseVisualStyleBackColor = True
        '
        ' LabelNam2
        '
        LabelNam2.Anchor = AnchorStyles.Left
        LabelNam2.AutoSize = True
        LabelNam2.Location = New Point(3, 207)
        LabelNam2.Name = "LabelNam2"
        LabelNam2.Size = New Size(170, 15)
        LabelNam2.TabIndex = 12
        LabelNam2.Text = "Male skin-swap FLST (NAM2):"
        '
        ' TextBoxNam2
        '
        TextBoxNam2.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxNam2.Location = New Point(178, 203)
        TextBoxNam2.Name = "TextBoxNam2"
        TextBoxNam2.ReadOnly = True
        TextBoxNam2.Size = New Size(469, 23)
        TextBoxNam2.TabIndex = 13
        '
        ' ButtonPickNam2
        '
        ButtonPickNam2.Anchor = AnchorStyles.Left
        ButtonPickNam2.Location = New Point(653, 202)
        ButtonPickNam2.Name = "ButtonPickNam2"
        ButtonPickNam2.Size = New Size(34, 24)
        ButtonPickNam2.TabIndex = 14
        ButtonPickNam2.Text = "…"
        ButtonPickNam2.UseVisualStyleBackColor = True
        '
        ' LabelNam3
        '
        LabelNam3.Anchor = AnchorStyles.Left
        LabelNam3.AutoSize = True
        LabelNam3.Location = New Point(3, 237)
        LabelNam3.Name = "LabelNam3"
        LabelNam3.Size = New Size(180, 15)
        LabelNam3.TabIndex = 15
        LabelNam3.Text = "Female skin-swap FLST (NAM3):"
        '
        ' TextBoxNam3
        '
        TextBoxNam3.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxNam3.Location = New Point(178, 233)
        TextBoxNam3.Name = "TextBoxNam3"
        TextBoxNam3.ReadOnly = True
        TextBoxNam3.Size = New Size(469, 23)
        TextBoxNam3.TabIndex = 16
        '
        ' ButtonPickNam3
        '
        ButtonPickNam3.Anchor = AnchorStyles.Left
        ButtonPickNam3.Location = New Point(653, 232)
        ButtonPickNam3.Name = "ButtonPickNam3"
        ButtonPickNam3.Size = New Size(34, 24)
        ButtonPickNam3.TabIndex = 17
        ButtonPickNam3.Text = "…"
        ButtonPickNam3.UseVisualStyleBackColor = True
        '
        ' LabelMo2s
        '
        LabelMo2s.Anchor = AnchorStyles.Left
        LabelMo2s.AutoSize = True
        LabelMo2s.Location = New Point(3, 267)
        LabelMo2s.Name = "LabelMo2s"
        LabelMo2s.Size = New Size(175, 15)
        LabelMo2s.TabIndex = 18
        LabelMo2s.Text = "Male material swap (MO2S):"
        '
        ' TextBoxMo2s
        '
        TextBoxMo2s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo2s.Location = New Point(178, 263)
        TextBoxMo2s.Name = "TextBoxMo2s"
        TextBoxMo2s.ReadOnly = True
        TextBoxMo2s.Size = New Size(469, 23)
        TextBoxMo2s.TabIndex = 19
        '
        ' ButtonPickMo2s
        '
        ButtonPickMo2s.Anchor = AnchorStyles.Left
        ButtonPickMo2s.Location = New Point(653, 262)
        ButtonPickMo2s.Name = "ButtonPickMo2s"
        ButtonPickMo2s.Size = New Size(34, 24)
        ButtonPickMo2s.TabIndex = 20
        ButtonPickMo2s.Text = "…"
        ButtonPickMo2s.UseVisualStyleBackColor = True
        '
        ' ButtonEditMo2s
        '
        ButtonEditMo2s.Anchor = AnchorStyles.Left
        ButtonEditMo2s.Location = New Point(693, 262)
        ButtonEditMo2s.Name = "ButtonEditMo2s"
        ButtonEditMo2s.Size = New Size(104, 24)
        ButtonEditMo2s.TabIndex = 21
        ButtonEditMo2s.Text = "New / Edit MSWP…"
        ButtonEditMo2s.UseVisualStyleBackColor = True
        '
        ' LabelMo3s
        '
        LabelMo3s.Anchor = AnchorStyles.Left
        LabelMo3s.AutoSize = True
        LabelMo3s.Location = New Point(3, 297)
        LabelMo3s.Name = "LabelMo3s"
        LabelMo3s.Size = New Size(185, 15)
        LabelMo3s.TabIndex = 22
        LabelMo3s.Text = "Female material swap (MO3S):"
        '
        ' TextBoxMo3s
        '
        TextBoxMo3s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo3s.Location = New Point(178, 293)
        TextBoxMo3s.Name = "TextBoxMo3s"
        TextBoxMo3s.ReadOnly = True
        TextBoxMo3s.Size = New Size(469, 23)
        TextBoxMo3s.TabIndex = 23
        '
        ' ButtonPickMo3s
        '
        ButtonPickMo3s.Anchor = AnchorStyles.Left
        ButtonPickMo3s.Location = New Point(653, 292)
        ButtonPickMo3s.Name = "ButtonPickMo3s"
        ButtonPickMo3s.Size = New Size(34, 24)
        ButtonPickMo3s.TabIndex = 24
        ButtonPickMo3s.Text = "…"
        ButtonPickMo3s.UseVisualStyleBackColor = True
        '
        ' ButtonEditMo3s
        '
        ButtonEditMo3s.Anchor = AnchorStyles.Left
        ButtonEditMo3s.Location = New Point(693, 292)
        ButtonEditMo3s.Name = "ButtonEditMo3s"
        ButtonEditMo3s.Size = New Size(104, 24)
        ButtonEditMo3s.TabIndex = 25
        ButtonEditMo3s.Text = "New / Edit MSWP…"
        ButtonEditMo3s.UseVisualStyleBackColor = True
        '
        ' LabelSndd
        '
        LabelSndd.Anchor = AnchorStyles.Left
        LabelSndd.AutoSize = True
        LabelSndd.Location = New Point(3, 327)
        LabelSndd.Name = "LabelSndd"
        LabelSndd.Size = New Size(105, 15)
        LabelSndd.TabIndex = 26
        LabelSndd.Text = "Footstep (FSTS):"
        '
        ' TextBoxSndd
        '
        TextBoxSndd.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxSndd.Location = New Point(178, 323)
        TextBoxSndd.Name = "TextBoxSndd"
        TextBoxSndd.ReadOnly = True
        TextBoxSndd.Size = New Size(469, 23)
        TextBoxSndd.TabIndex = 27
        '
        ' ButtonPickSndd
        '
        ButtonPickSndd.Anchor = AnchorStyles.Left
        ButtonPickSndd.Location = New Point(653, 322)
        ButtonPickSndd.Name = "ButtonPickSndd"
        ButtonPickSndd.Size = New Size(34, 24)
        ButtonPickSndd.TabIndex = 28
        ButtonPickSndd.Text = "…"
        ButtonPickSndd.UseVisualStyleBackColor = True
        '
        ' LabelOnam
        '
        LabelOnam.Anchor = AnchorStyles.Left
        LabelOnam.AutoSize = True
        LabelOnam.Location = New Point(3, 357)
        LabelOnam.Name = "LabelOnam"
        LabelOnam.Size = New Size(110, 15)
        LabelOnam.TabIndex = 29
        LabelOnam.Text = "Art Object (ONAM):"
        '
        ' TextBoxOnam
        '
        TextBoxOnam.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxOnam.Location = New Point(178, 353)
        TextBoxOnam.Name = "TextBoxOnam"
        TextBoxOnam.ReadOnly = True
        TextBoxOnam.Size = New Size(469, 23)
        TextBoxOnam.TabIndex = 30
        '
        ' ButtonPickOnam
        '
        ButtonPickOnam.Anchor = AnchorStyles.Left
        ButtonPickOnam.Location = New Point(653, 352)
        ButtonPickOnam.Name = "ButtonPickOnam"
        ButtonPickOnam.Size = New Size(34, 24)
        ButtonPickOnam.TabIndex = 31
        ButtonPickOnam.Text = "…"
        ButtonPickOnam.UseVisualStyleBackColor = True
        '
        ' LabelMo4s
        '
        LabelMo4s.Anchor = AnchorStyles.Left
        LabelMo4s.AutoSize = True
        LabelMo4s.Location = New Point(3, 387)
        LabelMo4s.Name = "LabelMo4s"
        LabelMo4s.Size = New Size(175, 15)
        LabelMo4s.TabIndex = 32
        LabelMo4s.Text = "Male 1st-p material swap (MO4S):"
        '
        ' TextBoxMo4s
        '
        TextBoxMo4s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo4s.Location = New Point(178, 383)
        TextBoxMo4s.Name = "TextBoxMo4s"
        TextBoxMo4s.ReadOnly = True
        TextBoxMo4s.Size = New Size(469, 23)
        TextBoxMo4s.TabIndex = 33
        '
        ' ButtonPickMo4s
        '
        ButtonPickMo4s.Anchor = AnchorStyles.Left
        ButtonPickMo4s.Location = New Point(653, 382)
        ButtonPickMo4s.Name = "ButtonPickMo4s"
        ButtonPickMo4s.Size = New Size(34, 24)
        ButtonPickMo4s.TabIndex = 34
        ButtonPickMo4s.Text = "…"
        ButtonPickMo4s.UseVisualStyleBackColor = True
        '
        ' LabelMo5s
        '
        LabelMo5s.Anchor = AnchorStyles.Left
        LabelMo5s.AutoSize = True
        LabelMo5s.Location = New Point(3, 417)
        LabelMo5s.Name = "LabelMo5s"
        LabelMo5s.Size = New Size(185, 15)
        LabelMo5s.TabIndex = 35
        LabelMo5s.Text = "Female 1st-p material swap (MO5S):"
        '
        ' TextBoxMo5s
        '
        TextBoxMo5s.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxMo5s.Location = New Point(178, 413)
        TextBoxMo5s.Name = "TextBoxMo5s"
        TextBoxMo5s.ReadOnly = True
        TextBoxMo5s.Size = New Size(469, 23)
        TextBoxMo5s.TabIndex = 36
        '
        ' ButtonPickMo5s
        '
        ButtonPickMo5s.Anchor = AnchorStyles.Left
        ButtonPickMo5s.Location = New Point(653, 412)
        ButtonPickMo5s.Name = "ButtonPickMo5s"
        ButtonPickMo5s.Size = New Size(34, 24)
        ButtonPickMo5s.TabIndex = 37
        ButtonPickMo5s.Text = "…"
        ButtonPickMo5s.UseVisualStyleBackColor = True
        '
        ' GroupPriorities
        '
        GroupPriorities.AutoSize = True
        GroupPriorities.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupPriorities.Controls.Add(PrioritiesLayout)
        GroupPriorities.Dock = DockStyle.Fill
        GroupPriorities.Location = New Point(3, 322)
        GroupPriorities.Name = "GroupPriorities"
        GroupPriorities.Size = New Size(694, 160)
        GroupPriorities.TabIndex = 26
        GroupPriorities.TabStop = False
        GroupPriorities.Text = "Priorities, weight slider & detection/weapon (DNAM)"
        '
        ' PrioritiesLayout
        '
        PrioritiesLayout.ColumnCount = 3
        PrioritiesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 170F))
        PrioritiesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 90F))
        PrioritiesLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PrioritiesLayout.Controls.Add(LabelMalePrio, 0, 0)
        PrioritiesLayout.Controls.Add(NumMalePrio, 1, 0)
        PrioritiesLayout.Controls.Add(CheckMaleWeight, 2, 0)
        PrioritiesLayout.Controls.Add(LabelFemalePrio, 0, 1)
        PrioritiesLayout.Controls.Add(NumFemalePrio, 1, 1)
        PrioritiesLayout.Controls.Add(CheckFemaleWeight, 2, 1)
        PrioritiesLayout.Controls.Add(LabelDetectionSound, 0, 2)
        PrioritiesLayout.Controls.Add(NumDetectionSound, 1, 2)
        PrioritiesLayout.Controls.Add(LabelWeaponAdjust, 0, 3)
        PrioritiesLayout.Controls.Add(NumWeaponAdjust, 1, 3)
        PrioritiesLayout.AutoSize = True
        PrioritiesLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PrioritiesLayout.Dock = DockStyle.Fill
        PrioritiesLayout.Location = New Point(3, 19)
        PrioritiesLayout.Name = "PrioritiesLayout"
        PrioritiesLayout.RowCount = 4
        PrioritiesLayout.RowStyles.Add(New RowStyle())
        PrioritiesLayout.RowStyles.Add(New RowStyle())
        PrioritiesLayout.RowStyles.Add(New RowStyle())
        PrioritiesLayout.RowStyles.Add(New RowStyle())
        PrioritiesLayout.Size = New Size(688, 120)
        PrioritiesLayout.TabIndex = 0
        '
        ' LabelMalePrio
        '
        LabelMalePrio.Anchor = AnchorStyles.Left
        LabelMalePrio.AutoSize = True
        LabelMalePrio.Location = New Point(3, 6)
        LabelMalePrio.Name = "LabelMalePrio"
        LabelMalePrio.Size = New Size(130, 15)
        LabelMalePrio.TabIndex = 0
        LabelMalePrio.Text = "Male priority (0-255):"
        '
        ' NumMalePrio
        '
        NumMalePrio.Anchor = AnchorStyles.Left
        NumMalePrio.Location = New Point(173, 3)
        NumMalePrio.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumMalePrio.Name = "NumMalePrio"
        NumMalePrio.Size = New Size(80, 23)
        NumMalePrio.TabIndex = 1
        NumMalePrio.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' CheckMaleWeight
        '
        CheckMaleWeight.Anchor = AnchorStyles.Left
        CheckMaleWeight.AutoSize = True
        CheckMaleWeight.Location = New Point(269, 4)
        CheckMaleWeight.Name = "CheckMaleWeight"
        CheckMaleWeight.Size = New Size(180, 19)
        CheckMaleWeight.TabIndex = 2
        CheckMaleWeight.Text = "Weight slider enabled (0x02)"
        CheckMaleWeight.UseVisualStyleBackColor = True
        '
        ' LabelFemalePrio
        '
        LabelFemalePrio.Anchor = AnchorStyles.Left
        LabelFemalePrio.AutoSize = True
        LabelFemalePrio.Location = New Point(3, 36)
        LabelFemalePrio.Name = "LabelFemalePrio"
        LabelFemalePrio.Size = New Size(140, 15)
        LabelFemalePrio.TabIndex = 3
        LabelFemalePrio.Text = "Female priority (0-255):"
        '
        ' NumFemalePrio
        '
        NumFemalePrio.Anchor = AnchorStyles.Left
        NumFemalePrio.Location = New Point(173, 33)
        NumFemalePrio.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumFemalePrio.Name = "NumFemalePrio"
        NumFemalePrio.Size = New Size(80, 23)
        NumFemalePrio.TabIndex = 4
        NumFemalePrio.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' CheckFemaleWeight
        '
        CheckFemaleWeight.Anchor = AnchorStyles.Left
        CheckFemaleWeight.AutoSize = True
        CheckFemaleWeight.Location = New Point(269, 34)
        CheckFemaleWeight.Name = "CheckFemaleWeight"
        CheckFemaleWeight.Size = New Size(180, 19)
        CheckFemaleWeight.TabIndex = 5
        CheckFemaleWeight.Text = "Weight slider enabled (0x02)"
        CheckFemaleWeight.UseVisualStyleBackColor = True
        '
        ' LabelDetectionSound
        '
        LabelDetectionSound.Anchor = AnchorStyles.Left
        LabelDetectionSound.AutoSize = True
        LabelDetectionSound.Location = New Point(3, 66)
        LabelDetectionSound.Name = "LabelDetectionSound"
        LabelDetectionSound.Size = New Size(140, 15)
        LabelDetectionSound.TabIndex = 6
        LabelDetectionSound.Text = "Detection Sound Value:"
        '
        ' NumDetectionSound
        '
        NumDetectionSound.Anchor = AnchorStyles.Left
        NumDetectionSound.Location = New Point(173, 63)
        NumDetectionSound.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumDetectionSound.Name = "NumDetectionSound"
        NumDetectionSound.Size = New Size(80, 23)
        NumDetectionSound.TabIndex = 7
        NumDetectionSound.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelWeaponAdjust
        '
        LabelWeaponAdjust.Anchor = AnchorStyles.Left
        LabelWeaponAdjust.AutoSize = True
        LabelWeaponAdjust.Location = New Point(3, 96)
        LabelWeaponAdjust.Name = "LabelWeaponAdjust"
        LabelWeaponAdjust.Size = New Size(140, 15)
        LabelWeaponAdjust.TabIndex = 8
        LabelWeaponAdjust.Text = "Weapon Adjust:"
        '
        ' NumWeaponAdjust
        '
        NumWeaponAdjust.Anchor = AnchorStyles.Left
        NumWeaponAdjust.DecimalPlaces = 2
        NumWeaponAdjust.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        NumWeaponAdjust.Location = New Point(173, 93)
        NumWeaponAdjust.Maximum = New Decimal(New Integer() {100000, 0, 0, 0})
        NumWeaponAdjust.Minimum = New Decimal(New Integer() {100000, 0, 0, -2147483648})
        NumWeaponAdjust.Name = "NumWeaponAdjust"
        NumWeaponAdjust.Size = New Size(100, 23)
        NumWeaponAdjust.TabIndex = 9
        NumWeaponAdjust.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' TabData
        '
        TabData.Controls.Add(DataLayout)
        TabData.Location = New Point(4, 24)
        TabData.Name = "TabData"
        TabData.Padding = New Padding(6)
        TabData.Size = New Size(712, 522)
        TabData.TabIndex = 5
        TabData.Text = "Data (DNAM)"
        TabData.UseVisualStyleBackColor = True
        '
        ' DataLayout
        '
        DataLayout.ColumnCount = 1
        DataLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        DataLayout.Controls.Add(GroupPriorities, 0, 0)
        DataLayout.Dock = DockStyle.Fill
        DataLayout.Location = New Point(6, 6)
        DataLayout.Name = "DataLayout"
        DataLayout.RowCount = 2
        DataLayout.RowStyles.Add(New RowStyle())
        DataLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        DataLayout.Size = New Size(700, 510)
        DataLayout.TabIndex = 0
        '
        ' TabSculpt
        '
        TabSculpt.Controls.Add(SculptLayout)
        TabSculpt.Location = New Point(4, 24)
        TabSculpt.Name = "TabSculpt"
        TabSculpt.Padding = New Padding(6)
        TabSculpt.Size = New Size(712, 522)
        TabSculpt.TabIndex = 3
        TabSculpt.Text = "Sculpt"
        TabSculpt.UseVisualStyleBackColor = True
        '
        ' SculptLayout
        '
        SculptLayout.ColumnCount = 1
        SculptLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SculptLayout.Controls.Add(SculptTopRow, 0, 0)
        SculptLayout.Controls.Add(SculptHeaderRow, 0, 1)
        SculptLayout.Controls.Add(SculptPanel, 0, 2)
        SculptLayout.Controls.Add(SculptButtons, 0, 3)
        SculptLayout.Dock = DockStyle.Fill
        SculptLayout.Location = New Point(6, 6)
        SculptLayout.Name = "SculptLayout"
        SculptLayout.RowCount = 4
        SculptLayout.RowStyles.Add(New RowStyle())
        SculptLayout.RowStyles.Add(New RowStyle())
        SculptLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SculptLayout.RowStyles.Add(New RowStyle())
        SculptLayout.Size = New Size(700, 510)
        SculptLayout.TabIndex = 0
        '
        ' SculptTopRow
        '
        SculptTopRow.AutoSize = True
        SculptTopRow.Controls.Add(LabelSculptGender)
        SculptTopRow.Controls.Add(RadioSculptMale)
        SculptTopRow.Controls.Add(RadioSculptFemale)
        SculptTopRow.Dock = DockStyle.Fill
        SculptTopRow.Location = New Point(0, 0)
        SculptTopRow.Margin = New Padding(0)
        SculptTopRow.Name = "SculptTopRow"
        SculptTopRow.Size = New Size(700, 29)
        SculptTopRow.TabIndex = 0
        SculptTopRow.WrapContents = False
        '
        ' LabelSculptGender
        '
        LabelSculptGender.Anchor = AnchorStyles.Left
        LabelSculptGender.AutoSize = True
        LabelSculptGender.Location = New Point(3, 7)
        LabelSculptGender.Name = "LabelSculptGender"
        LabelSculptGender.Size = New Size(120, 15)
        LabelSculptGender.TabIndex = 0
        LabelSculptGender.Text = "Per-bone scale for:"
        '
        ' RadioSculptMale
        '
        RadioSculptMale.Anchor = AnchorStyles.Left
        RadioSculptMale.AutoSize = True
        RadioSculptMale.Checked = True
        RadioSculptMale.Location = New Point(129, 5)
        RadioSculptMale.Name = "RadioSculptMale"
        RadioSculptMale.Size = New Size(51, 19)
        RadioSculptMale.TabIndex = 1
        RadioSculptMale.TabStop = True
        RadioSculptMale.Text = "Male"
        RadioSculptMale.UseVisualStyleBackColor = True
        '
        ' RadioSculptFemale
        '
        RadioSculptFemale.Anchor = AnchorStyles.Left
        RadioSculptFemale.AutoSize = True
        RadioSculptFemale.Location = New Point(186, 5)
        RadioSculptFemale.Name = "RadioSculptFemale"
        RadioSculptFemale.Size = New Size(63, 19)
        RadioSculptFemale.TabIndex = 2
        RadioSculptFemale.Text = "Female"
        RadioSculptFemale.UseVisualStyleBackColor = True
        '
        ' SculptHeaderRow
        '
        SculptHeaderRow.ColumnCount = 8
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 150F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 16F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 130F))
        SculptHeaderRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SculptHeaderRow.Controls.Add(LabelSculptColBone, 0, 0)
        SculptHeaderRow.Controls.Add(LabelSculptColX, 2, 0)
        SculptHeaderRow.Controls.Add(LabelSculptColY, 4, 0)
        SculptHeaderRow.Controls.Add(LabelSculptColZ, 6, 0)
        SculptHeaderRow.Controls.Add(LabelSculptColRemove, 7, 0)
        SculptHeaderRow.Dock = DockStyle.Fill
        SculptHeaderRow.Location = New Point(0, 29)
        SculptHeaderRow.Margin = New Padding(0)
        SculptHeaderRow.Name = "SculptHeaderRow"
        SculptHeaderRow.RowCount = 1
        SculptHeaderRow.RowStyles.Add(New RowStyle())
        SculptHeaderRow.Size = New Size(700, 22)
        SculptHeaderRow.TabIndex = 1
        '
        ' LabelSculptColBone
        '
        LabelSculptColBone.Anchor = AnchorStyles.Left
        LabelSculptColBone.AutoSize = True
        LabelSculptColBone.ForeColor = Color.DimGray
        LabelSculptColBone.Location = New Point(3, 3)
        LabelSculptColBone.Name = "LabelSculptColBone"
        LabelSculptColBone.Size = New Size(36, 15)
        LabelSculptColBone.TabIndex = 0
        LabelSculptColBone.Text = "Bone"
        '
        ' LabelSculptColX
        '
        LabelSculptColX.Anchor = AnchorStyles.None
        LabelSculptColX.AutoSize = True
        LabelSculptColX.ForeColor = Color.DimGray
        LabelSculptColX.Location = New Point(258, 3)
        LabelSculptColX.Name = "LabelSculptColX"
        LabelSculptColX.Size = New Size(14, 15)
        LabelSculptColX.TabIndex = 1
        LabelSculptColX.Text = "X"
        '
        ' LabelSculptColY
        '
        LabelSculptColY.Anchor = AnchorStyles.None
        LabelSculptColY.AutoSize = True
        LabelSculptColY.ForeColor = Color.DimGray
        LabelSculptColY.Location = New Point(374, 3)
        LabelSculptColY.Name = "LabelSculptColY"
        LabelSculptColY.Size = New Size(14, 15)
        LabelSculptColY.TabIndex = 2
        LabelSculptColY.Text = "Y"
        '
        ' LabelSculptColZ
        '
        LabelSculptColZ.Anchor = AnchorStyles.None
        LabelSculptColZ.AutoSize = True
        LabelSculptColZ.ForeColor = Color.DimGray
        LabelSculptColZ.Location = New Point(490, 3)
        LabelSculptColZ.Name = "LabelSculptColZ"
        LabelSculptColZ.Size = New Size(14, 15)
        LabelSculptColZ.TabIndex = 3
        LabelSculptColZ.Text = "Z"
        '
        ' LabelSculptColRemove
        '
        LabelSculptColRemove.Anchor = AnchorStyles.None
        LabelSculptColRemove.AutoSize = True
        LabelSculptColRemove.ForeColor = Color.DimGray
        LabelSculptColRemove.Location = New Point(566, 3)
        LabelSculptColRemove.Name = "LabelSculptColRemove"
        LabelSculptColRemove.Size = New Size(0, 15)
        LabelSculptColRemove.TabIndex = 4
        LabelSculptColRemove.Text = ""
        '
        ' SculptPanel
        '
        SculptPanel.AutoScroll = True
        SculptPanel.Dock = DockStyle.Fill
        SculptPanel.FlowDirection = FlowDirection.TopDown
        SculptPanel.Location = New Point(3, 54)
        SculptPanel.Name = "SculptPanel"
        SculptPanel.Size = New Size(694, 421)
        SculptPanel.TabIndex = 2
        SculptPanel.WrapContents = False
        '
        ' SculptButtons
        '
        SculptButtons.AutoSize = True
        SculptButtons.Controls.Add(LabelSculptAddBone)
        SculptButtons.Controls.Add(ComboSculptAddBone)
        SculptButtons.Controls.Add(ButtonSculptAddRow)
        SculptButtons.Controls.Add(ButtonSculptLoad)
        SculptButtons.Controls.Add(ButtonSculptEstimate)
        SculptButtons.Controls.Add(ButtonSculptSave)
        SculptButtons.Dock = DockStyle.Fill
        SculptButtons.Location = New Point(0, 478)
        SculptButtons.Margin = New Padding(0)
        SculptButtons.Name = "SculptButtons"
        SculptButtons.Size = New Size(700, 32)
        SculptButtons.TabIndex = 3
        SculptButtons.WrapContents = False
        '
        ' LabelSculptAddBone
        '
        LabelSculptAddBone.Anchor = AnchorStyles.Left
        LabelSculptAddBone.AutoSize = True
        LabelSculptAddBone.Location = New Point(3, 8)
        LabelSculptAddBone.Margin = New Padding(3, 8, 3, 0)
        LabelSculptAddBone.Name = "LabelSculptAddBone"
        LabelSculptAddBone.Size = New Size(34, 15)
        LabelSculptAddBone.TabIndex = 0
        LabelSculptAddBone.Text = "Bone:"
        '
        ' ComboSculptAddBone
        '
        ComboSculptAddBone.Anchor = AnchorStyles.Left
        ComboSculptAddBone.DropDownStyle = ComboBoxStyle.DropDown
        ComboSculptAddBone.Location = New Point(43, 4)
        ComboSculptAddBone.Margin = New Padding(3, 4, 3, 3)
        ComboSculptAddBone.Name = "ComboSculptAddBone"
        ComboSculptAddBone.Size = New Size(200, 23)
        ComboSculptAddBone.TabIndex = 1
        '
        ' ButtonSculptAddRow
        '
        ButtonSculptAddRow.Location = New Point(249, 3)
        ButtonSculptAddRow.Name = "ButtonSculptAddRow"
        ButtonSculptAddRow.Size = New Size(90, 25)
        ButtonSculptAddRow.TabIndex = 2
        ButtonSculptAddRow.Text = "Add bone"
        ButtonSculptAddRow.UseVisualStyleBackColor = True
        '
        ' ButtonSculptLoad
        '
        ButtonSculptLoad.Location = New Point(345, 3)
        ButtonSculptLoad.Margin = New Padding(20, 3, 3, 3)
        ButtonSculptLoad.Name = "ButtonSculptLoad"
        ButtonSculptLoad.Size = New Size(90, 25)
        ButtonSculptLoad.TabIndex = 3
        ButtonSculptLoad.Text = "Load .sclp…"
        ButtonSculptLoad.UseVisualStyleBackColor = True
        '
        ' ButtonSculptEstimate
        '
        ButtonSculptEstimate.Location = New Point(441, 3)
        ButtonSculptEstimate.Name = "ButtonSculptEstimate"
        ButtonSculptEstimate.Size = New Size(90, 25)
        ButtonSculptEstimate.TabIndex = 4
        ButtonSculptEstimate.Text = "Estimate"
        ButtonSculptEstimate.UseVisualStyleBackColor = True
        '
        ' ButtonSculptSave
        '
        ButtonSculptSave.Location = New Point(537, 3)
        ButtonSculptSave.Name = "ButtonSculptSave"
        ButtonSculptSave.Size = New Size(90, 25)
        ButtonSculptSave.TabIndex = 5
        ButtonSculptSave.Text = "Save .sclp…"
        ButtonSculptSave.UseVisualStyleBackColor = True
        '
        ' TabFlags
        '
        TabFlags.Controls.Add(FlagsLayout)
        TabFlags.Location = New Point(4, 24)
        TabFlags.Name = "TabFlags"
        TabFlags.Padding = New Padding(6)
        TabFlags.Size = New Size(712, 522)
        TabFlags.TabIndex = 4
        TabFlags.Text = "Flags"
        TabFlags.UseVisualStyleBackColor = True
        '
        ' FlagsLayout
        '
        FlagsLayout.ColumnCount = 1
        FlagsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FlagsLayout.Controls.Add(CheckNoUnderarmorScaling, 0, 0)
        FlagsLayout.Controls.Add(CheckHasSculptData, 0, 1)
        FlagsLayout.Dock = DockStyle.Fill
        FlagsLayout.Location = New Point(6, 6)
        FlagsLayout.Name = "FlagsLayout"
        FlagsLayout.RowCount = 3
        FlagsLayout.RowStyles.Add(New RowStyle())
        FlagsLayout.RowStyles.Add(New RowStyle())
        FlagsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        FlagsLayout.Size = New Size(700, 510)
        FlagsLayout.TabIndex = 0
        '
        ' CheckNoUnderarmorScaling
        '
        CheckNoUnderarmorScaling.AutoSize = True
        CheckNoUnderarmorScaling.Location = New Point(3, 3)
        CheckNoUnderarmorScaling.Name = "CheckNoUnderarmorScaling"
        CheckNoUnderarmorScaling.Size = New Size(220, 19)
        CheckNoUnderarmorScaling.TabIndex = 0
        CheckNoUnderarmorScaling.Text = "No Underarmor Scaling (header bit 6)"
        CheckNoUnderarmorScaling.UseVisualStyleBackColor = True
        '
        ' CheckHasSculptData
        '
        CheckHasSculptData.AutoSize = True
        CheckHasSculptData.Location = New Point(3, 28)
        CheckHasSculptData.Name = "CheckHasSculptData"
        CheckHasSculptData.Size = New Size(180, 19)
        CheckHasSculptData.TabIndex = 1
        CheckHasSculptData.Text = "Has Sculpt Data (header bit 9)"
        CheckHasSculptData.UseVisualStyleBackColor = True
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
        PreviewModePanel.Controls.Add(RadioOnlyModel)
        PreviewModePanel.Controls.Add(RadioFullArmor)
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
        ' RadioOnlyModel
        '
        RadioOnlyModel.AutoSize = True
        RadioOnlyModel.Checked = True
        RadioOnlyModel.Margin = New Padding(3, 3, 8, 3)
        RadioOnlyModel.Name = "RadioOnlyModel"
        RadioOnlyModel.Size = New Size(84, 19)
        RadioOnlyModel.TabIndex = 0
        RadioOnlyModel.TabStop = True
        RadioOnlyModel.Text = "Only Model"
        RadioOnlyModel.UseVisualStyleBackColor = True
        '
        ' RadioFullArmor
        '
        RadioFullArmor.AutoSize = True
        RadioFullArmor.Margin = New Padding(3, 3, 8, 3)
        RadioFullArmor.Name = "RadioFullArmor"
        RadioFullArmor.Size = New Size(82, 19)
        RadioFullArmor.TabIndex = 1
        RadioFullArmor.Text = "Full armor"
        RadioFullArmor.UseVisualStyleBackColor = True
        '
        ' RadioFullOutfit
        '
        RadioFullOutfit.AutoSize = True
        RadioFullOutfit.Margin = New Padding(3, 3, 12, 3)
        RadioFullOutfit.Name = "RadioFullOutfit"
        RadioFullOutfit.Size = New Size(80, 19)
        RadioFullOutfit.TabIndex = 2
        RadioFullOutfit.Text = "Full Outfit"
        RadioFullOutfit.UseVisualStyleBackColor = True
        '
        ' CheckIncludeBody
        '
        CheckIncludeBody.AutoSize = True
        CheckIncludeBody.Margin = New Padding(3, 3, 12, 3)
        CheckIncludeBody.Name = "CheckIncludeBody"
        CheckIncludeBody.Size = New Size(97, 19)
        CheckIncludeBody.TabIndex = 3
        CheckIncludeBody.Text = "Include Body"
        CheckIncludeBody.UseVisualStyleBackColor = True
        '
        ' CheckShowOtherGender
        '
        CheckShowOtherGender.AutoSize = True
        CheckShowOtherGender.Margin = New Padding(3, 3, 3, 3)
        CheckShowOtherGender.Name = "CheckShowOtherGender"
        CheckShowOtherGender.Size = New Size(127, 19)
        CheckShowOtherGender.TabIndex = 4
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
        ' ArmaEditor_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1264, 681)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ArmaEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "ARMA Editor"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TopBar.ResumeLayout(False)
        TopBar.PerformLayout()
        MainSplit.Panel1.ResumeLayout(False)
        MainSplit.Panel2.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        Tabs.ResumeLayout(False)
        TabModels.ResumeLayout(False)
        ModelsLayout.ResumeLayout(False)
        ModelsLayout.PerformLayout()
        GroupMeshes.ResumeLayout(False)
        MeshesLayout.ResumeLayout(False)
        MeshesLayout.PerformLayout()
        GroupModelFlags.ResumeLayout(False)
        ModelFlagsLayout.ResumeLayout(False)
        ModelFlagsLayout.PerformLayout()
        GroupModelExtras.ResumeLayout(False)
        ModelExtrasLayout.ResumeLayout(False)
        ModelExtrasLayout.PerformLayout()
        TabSlots.ResumeLayout(False)
        TabSlots.PerformLayout()
        SlotsLayout.ResumeLayout(False)
        SlotsLayout.PerformLayout()
        TabSkin.ResumeLayout(False)
        SkinLayout.ResumeLayout(False)
        SkinLayout.PerformLayout()
        GroupRace.ResumeLayout(False)
        RaceLayout.ResumeLayout(False)
        RaceLayout.PerformLayout()
        GroupSkinTextures.ResumeLayout(False)
        SkinTexturesLayout.ResumeLayout(False)
        SkinTexturesLayout.PerformLayout()
        AddRacesButtons.ResumeLayout(False)
        GroupPriorities.ResumeLayout(False)
        PrioritiesLayout.ResumeLayout(False)
        PrioritiesLayout.PerformLayout()
        CType(NumMalePrio, ComponentModel.ISupportInitialize).EndInit()
        CType(NumFemalePrio, ComponentModel.ISupportInitialize).EndInit()
        CType(NumDetectionSound, ComponentModel.ISupportInitialize).EndInit()
        CType(NumWeaponAdjust, ComponentModel.ISupportInitialize).EndInit()
        TabData.ResumeLayout(False)
        DataLayout.ResumeLayout(False)
        DataLayout.PerformLayout()
        TabSculpt.ResumeLayout(False)
        SculptLayout.ResumeLayout(False)
        SculptLayout.PerformLayout()
        SculptTopRow.ResumeLayout(False)
        SculptTopRow.PerformLayout()
        SculptHeaderRow.ResumeLayout(False)
        SculptHeaderRow.PerformLayout()
        SculptButtons.ResumeLayout(False)
        SculptButtons.PerformLayout()
        TabFlags.ResumeLayout(False)
        TabFlags.PerformLayout()
        FlagsLayout.ResumeLayout(False)
        FlagsLayout.PerformLayout()
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
    Friend WithEvents TabModels As System.Windows.Forms.TabPage
    Friend WithEvents ModelsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupMeshes As System.Windows.Forms.GroupBox
    Friend WithEvents MeshesLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMod2 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod2 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod2 As System.Windows.Forms.Button
    Friend WithEvents LabelMod3 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod3 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod3 As System.Windows.Forms.Button
    Friend WithEvents LabelMod4 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod4 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod4 As System.Windows.Forms.Button
    Friend WithEvents LabelMod5 As System.Windows.Forms.Label
    Friend WithEvents TextBoxMod5 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseMod5 As System.Windows.Forms.Button
    Friend WithEvents GroupModelFlags As System.Windows.Forms.GroupBox
    Friend WithEvents ModelFlagsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMo2f As System.Windows.Forms.Label
    Friend WithEvents CheckMo2fFaceBones As System.Windows.Forms.CheckBox
    Friend WithEvents CheckMo2f1stPerson As System.Windows.Forms.CheckBox
    Friend WithEvents LabelMo3f As System.Windows.Forms.Label
    Friend WithEvents CheckMo3fFaceBones As System.Windows.Forms.CheckBox
    Friend WithEvents CheckMo3f1stPerson As System.Windows.Forms.CheckBox
    Friend WithEvents GroupModelExtras As System.Windows.Forms.GroupBox
    Friend WithEvents ModelExtrasLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TabSlots As System.Windows.Forms.TabPage
    Friend WithEvents SlotsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSlots As System.Windows.Forms.Label
    Friend WithEvents FlowSlots As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents TabSkin As System.Windows.Forms.TabPage
    Friend WithEvents SkinLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupRace As System.Windows.Forms.GroupBox
    Friend WithEvents RaceLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupSkinTextures As System.Windows.Forms.GroupBox
    Friend WithEvents SkinTexturesLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelRace As System.Windows.Forms.Label
    Friend WithEvents TextBoxRace As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickRace As System.Windows.Forms.Button
    Friend WithEvents LabelAddRaces As System.Windows.Forms.Label
    Friend WithEvents ListAddRaces As System.Windows.Forms.ListView
    Friend WithEvents ColAddRace As System.Windows.Forms.ColumnHeader
    Friend WithEvents AddRacesButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddRace As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveRace As System.Windows.Forms.Button
    Friend WithEvents LabelNam0 As System.Windows.Forms.Label
    Friend WithEvents TextBoxNam0 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickNam0 As System.Windows.Forms.Button
    Friend WithEvents LabelNam1 As System.Windows.Forms.Label
    Friend WithEvents TextBoxNam1 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickNam1 As System.Windows.Forms.Button
    Friend WithEvents LabelNam2 As System.Windows.Forms.Label
    Friend WithEvents TextBoxNam2 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickNam2 As System.Windows.Forms.Button
    Friend WithEvents LabelNam3 As System.Windows.Forms.Label
    Friend WithEvents TextBoxNam3 As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickNam3 As System.Windows.Forms.Button
    Friend WithEvents LabelMo2s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo2s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo2s As System.Windows.Forms.Button
    Friend WithEvents ButtonEditMo2s As System.Windows.Forms.Button
    Friend WithEvents LabelSndd As System.Windows.Forms.Label
    Friend WithEvents TextBoxSndd As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickSndd As System.Windows.Forms.Button
    Friend WithEvents LabelOnam As System.Windows.Forms.Label
    Friend WithEvents TextBoxOnam As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickOnam As System.Windows.Forms.Button
    Friend WithEvents LabelMo4s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo4s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo4s As System.Windows.Forms.Button
    Friend WithEvents LabelMo5s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo5s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo5s As System.Windows.Forms.Button
    Friend WithEvents LabelMo3s As System.Windows.Forms.Label
    Friend WithEvents TextBoxMo3s As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickMo3s As System.Windows.Forms.Button
    Friend WithEvents ButtonEditMo3s As System.Windows.Forms.Button
    Friend WithEvents GroupPriorities As System.Windows.Forms.GroupBox
    Friend WithEvents PrioritiesLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelMalePrio As System.Windows.Forms.Label
    Friend WithEvents NumMalePrio As System.Windows.Forms.NumericUpDown
    Friend WithEvents CheckMaleWeight As System.Windows.Forms.CheckBox
    Friend WithEvents LabelFemalePrio As System.Windows.Forms.Label
    Friend WithEvents NumFemalePrio As System.Windows.Forms.NumericUpDown
    Friend WithEvents CheckFemaleWeight As System.Windows.Forms.CheckBox
    Friend WithEvents LabelDetectionSound As System.Windows.Forms.Label
    Friend WithEvents NumDetectionSound As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelWeaponAdjust As System.Windows.Forms.Label
    Friend WithEvents NumWeaponAdjust As System.Windows.Forms.NumericUpDown
    Friend WithEvents TabData As System.Windows.Forms.TabPage
    Friend WithEvents DataLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TabSculpt As System.Windows.Forms.TabPage
    Friend WithEvents SculptLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents SculptTopRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelSculptGender As System.Windows.Forms.Label
    Friend WithEvents RadioSculptMale As System.Windows.Forms.RadioButton
    Friend WithEvents RadioSculptFemale As System.Windows.Forms.RadioButton
    Friend WithEvents SculptHeaderRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSculptColBone As System.Windows.Forms.Label
    Friend WithEvents LabelSculptColX As System.Windows.Forms.Label
    Friend WithEvents LabelSculptColY As System.Windows.Forms.Label
    Friend WithEvents LabelSculptColZ As System.Windows.Forms.Label
    Friend WithEvents LabelSculptColRemove As System.Windows.Forms.Label
    Friend WithEvents SculptPanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents SculptButtons As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelSculptAddBone As System.Windows.Forms.Label
    Friend WithEvents ComboSculptAddBone As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonSculptAddRow As System.Windows.Forms.Button
    Friend WithEvents ButtonSculptLoad As System.Windows.Forms.Button
    Friend WithEvents ButtonSculptEstimate As System.Windows.Forms.Button
    Friend WithEvents ButtonSculptSave As System.Windows.Forms.Button
    Friend WithEvents TabFlags As System.Windows.Forms.TabPage
    Friend WithEvents FlagsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckNoUnderarmorScaling As System.Windows.Forms.CheckBox
    Friend WithEvents CheckHasSculptData As System.Windows.Forms.CheckBox
    Friend WithEvents PreviewLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelPreviewHint As System.Windows.Forms.Label
    Friend WithEvents PreviewControlPanel As System.Windows.Forms.Panel
    Friend WithEvents PreviewModePanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents RadioOnlyModel As System.Windows.Forms.RadioButton
    Friend WithEvents RadioFullArmor As System.Windows.Forms.RadioButton
    Friend WithEvents RadioFullOutfit As System.Windows.Forms.RadioButton
    Friend WithEvents CheckIncludeBody As System.Windows.Forms.CheckBox
    Friend WithEvents CheckShowOtherGender As System.Windows.Forms.CheckBox
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
