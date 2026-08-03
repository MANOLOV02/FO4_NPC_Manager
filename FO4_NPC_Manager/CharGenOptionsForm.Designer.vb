<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class CharGenOptionsForm
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        TabMain = New TabControl()
        TabPageSize = New TabPage()
        GroupBoxSize = New GroupBox()
        RadioAll = New RadioButton()
        LabelFormat = New Label()
        RadioPerLayer = New RadioButton()
        ComboFormat = New ComboBox()
        LabelDiffuse = New Label()
        ComboDiffuse = New ComboBox()
        LabelNormal = New Label()
        ComboNormal = New ComboBox()
        LabelSpecular = New Label()
        ComboSpecular = New ComboBox()
        ComboFormatN = New ComboBox()
        ComboFormatS = New ComboBox()
        CheckGenerateTga = New CheckBox()
        CheckDownsizeFromMip0 = New CheckBox()
        CheckBoxUseHardwareBcDecode = New CheckBox()
        CheckBoxAccumInComposite = New CheckBox()
        ButtonResetSize = New Button()
        TabPageConv = New TabPage()
        GroupConvDiffuse = New GroupBox()
        LblDWork = New Label()
        ComboDWork = New ComboBox()
        LblDComp = New Label()
        ComboDComp = New ComboBox()
        LblDSrc = New Label()
        ComboDSrc = New ComboBox()
        LblDTexSrc = New Label()
        ComboDTexSrc = New ComboBox()
        LblDOut = New Label()
        ComboDOut = New ComboBox()
        LblDMask = New Label()
        ComboDMask = New ComboBox()
        LblDFw = New Label()
        ComboDFw = New ComboBox()
        LblDSoft = New Label()
        ComboDSoft = New ComboBox()
        LblDBlend = New Label()
        ComboDBlend = New ComboBox()
        CheckDSeedG22 = New CheckBox()
        GroupConvNormal = New GroupBox()
        LblNWork = New Label()
        ComboNWork = New ComboBox()
        LblNComp = New Label()
        ComboNComp = New ComboBox()
        LblNSrc = New Label()
        ComboNSrc = New ComboBox()
        LblNOut = New Label()
        ComboNOut = New ComboBox()
        LblNMask = New Label()
        ComboNMask = New ComboBox()
        LblNFw = New Label()
        ComboNFw = New ComboBox()
        LblNSoft = New Label()
        ComboNSoft = New ComboBox()
        LblNBlend = New Label()
        ComboNBlend = New ComboBox()
        GroupConvFold = New GroupBox()
        LblFoldWork = New Label()
        ComboFoldWork = New ComboBox()
        LblFoldComp = New Label()
        ComboFoldComp = New ComboBox()
        LblFoldSrc = New Label()
        ComboFoldSrc = New ComboBox()
        LblFoldOut = New Label()
        ComboFoldOut = New ComboBox()
        LblFoldMask = New Label()
        ComboFoldMask = New ComboBox()
        LblFoldFw = New Label()
        ComboFoldFw = New ComboBox()
        LblFoldSoft = New Label()
        ComboFoldSoft = New ComboBox()
        GroupConvOverlay = New GroupBox()
        LblOvlWork = New Label()
        ComboOvlWork = New ComboBox()
        LblOvlComp = New Label()
        ComboOvlComp = New ComboBox()
        LblOvlSrc = New Label()
        ComboOvlSrc = New ComboBox()
        LblOvlOut = New Label()
        ComboOvlOut = New ComboBox()
        LblOvlMask = New Label()
        ComboOvlMask = New ComboBox()
        LblOvlFw = New Label()
        ComboOvlFw = New ComboBox()
        LblOvlSoft = New Label()
        ComboOvlSoft = New ComboBox()
        GroupConvSeed = New GroupBox()
        LblSeedMode = New Label()
        ComboSeedMode = New ComboBox()
        LblSeedRgb = New Label()
        NumSeedR = New NumericUpDown()
        NumSeedG = New NumericUpDown()
        NumSeedB = New NumericUpDown()
        GroupConvSwap = New GroupBox()
        LblSWork = New Label()
        ComboSWork = New ComboBox()
        LblSComp = New Label()
        ComboSComp = New ComboBox()
        LblSSrc = New Label()
        ComboSSrc = New ComboBox()
        LblSOut = New Label()
        ComboSOut = New ComboBox()
        LblSMask = New Label()
        ComboSMask = New ComboBox()
        LblSFw = New Label()
        ComboSFw = New ComboBox()
        LblSSoft = New Label()
        ComboSSoft = New ComboBox()
        LblSBlend = New Label()
        ComboSBlend = New ComboBox()
        GroupConvDWsByOp = New GroupBox()
        LblDWsReplace = New Label()
        ComboDWsReplace = New ComboBox()
        LblDWsMultiply = New Label()
        ComboDWsMultiply = New ComboBox()
        LblDWsOverlay = New Label()
        ComboDWsOverlay = New ComboBox()
        LblDWsSoftLight = New Label()
        ComboDWsSoftLight = New ComboBox()
        LblDWsHardLight = New Label()
        ComboDWsHardLight = New ComboBox()
        ButtonResetConv = New Button()
        TabPageOrder = New TabPage()
        GroupTintOrder = New GroupBox()
        ListTintRules = New ListBox()
        ComboTintKey = New ComboBox()
        ChkTintDesc = New CheckBox()
        BtnTintAdd = New Button()
        BtnTintRemove = New Button()
        BtnTintUp = New Button()
        BtnTintDown = New Button()
        GroupSwapOrder = New GroupBox()
        ListSwapRules = New ListBox()
        ComboSwapKey = New ComboBox()
        ChkSwapDesc = New CheckBox()
        BtnSwapAdd = New Button()
        BtnSwapRemove = New Button()
        BtnSwapUp = New Button()
        BtnSwapDown = New Button()
        LblSkinPlacement = New Label()
        ComboSkinPlacement = New ComboBox()
        BtnSortRevert = New Button()
        TabPageFixes = New TabPage()
        CheckBoxApplyGhoulHeadRearFix = New CheckBox()
        CheckBoxApplyEyebrowsFixedColor = New CheckBox()
        CheckBoxApplyMouthVanillaFix = New CheckBox()
        CheckBoxBakeSseRaceMenuOverlays = New CheckBox()
        CheckBoxResolveHphHeadTri = New CheckBox()
        CheckBoxReplicateEngineSkinNorm = New CheckBox()
        CheckBoxRecalcTangentSpace = New CheckBox()
        CheckBoxMatchSubsurfaceFlag = New CheckBox()
        BtnFixesRevert = New Button()
        ButtonOK = New Button()
        ButtonCancel = New Button()
        TabMain.SuspendLayout()
        TabPageSize.SuspendLayout()
        GroupBoxSize.SuspendLayout()
        TabPageConv.SuspendLayout()
        GroupConvDiffuse.SuspendLayout()
        GroupConvNormal.SuspendLayout()
        GroupConvSwap.SuspendLayout()
        GroupConvDWsByOp.SuspendLayout()
        TabPageOrder.SuspendLayout()
        GroupTintOrder.SuspendLayout()
        GroupSwapOrder.SuspendLayout()
        TabPageFixes.SuspendLayout()
        SuspendLayout()
        ' 
        ' TabMain
        ' 
        TabMain.Controls.Add(TabPageSize)
        TabMain.Controls.Add(TabPageConv)
        TabMain.Controls.Add(TabPageOrder)
        TabMain.Controls.Add(TabPageFixes)
        TabMain.Location = New Point(12, 12)
        TabMain.Name = "TabMain"
        TabMain.SelectedIndex = 0
        TabMain.Size = New Size(640, 488)
        TabMain.TabIndex = 0
        ' 
        ' TabPageSize
        ' 
        TabPageSize.Controls.Add(GroupBoxSize)
        TabPageSize.Controls.Add(ButtonResetSize)
        TabPageSize.Location = New Point(4, 24)
        TabPageSize.Name = "TabPageSize"
        TabPageSize.Padding = New Padding(3)
        TabPageSize.Size = New Size(632, 460)
        TabPageSize.TabIndex = 0
        TabPageSize.Text = "Texture Size"
        TabPageSize.UseVisualStyleBackColor = True
        ' 
        ' GroupBoxSize
        ' 
        GroupBoxSize.Controls.Add(RadioAll)
        GroupBoxSize.Controls.Add(LabelFormat)
        GroupBoxSize.Controls.Add(RadioPerLayer)
        GroupBoxSize.Controls.Add(ComboFormat)
        GroupBoxSize.Controls.Add(LabelDiffuse)
        GroupBoxSize.Controls.Add(ComboDiffuse)
        GroupBoxSize.Controls.Add(LabelNormal)
        GroupBoxSize.Controls.Add(ComboNormal)
        GroupBoxSize.Controls.Add(LabelSpecular)
        GroupBoxSize.Controls.Add(ComboSpecular)
        GroupBoxSize.Controls.Add(ComboFormatN)
        GroupBoxSize.Controls.Add(ComboFormatS)
        GroupBoxSize.Controls.Add(CheckGenerateTga)
        GroupBoxSize.Controls.Add(CheckDownsizeFromMip0)
        GroupBoxSize.Controls.Add(CheckBoxUseHardwareBcDecode)
        GroupBoxSize.Controls.Add(CheckBoxAccumInComposite)
        GroupBoxSize.Location = New Point(6, 6)
        GroupBoxSize.Name = "GroupBoxSize"
        GroupBoxSize.Size = New Size(606, 222)
        GroupBoxSize.TabIndex = 0
        GroupBoxSize.TabStop = False
        GroupBoxSize.Text = "Texture size (per channel)"
        ' 
        ' RadioAll
        ' 
        RadioAll.AutoSize = True
        RadioAll.Location = New Point(12, 24)
        RadioAll.Name = "RadioAll"
        RadioAll.Size = New Size(93, 19)
        RadioAll.TabIndex = 0
        RadioAll.TabStop = True
        RadioAll.Text = "All (uniform)"
        RadioAll.UseVisualStyleBackColor = True
        ' 
        ' LabelFormat
        ' 
        LabelFormat.AutoSize = True
        LabelFormat.Location = New Point(310, 58)
        LabelFormat.Name = "LabelFormat"
        LabelFormat.Size = New Size(45, 15)
        LabelFormat.TabIndex = 1
        LabelFormat.Text = "Format"
        ' 
        ' RadioPerLayer
        ' 
        RadioPerLayer.AutoSize = True
        RadioPerLayer.Location = New Point(150, 24)
        RadioPerLayer.Name = "RadioPerLayer"
        RadioPerLayer.Size = New Size(70, 19)
        RadioPerLayer.TabIndex = 1
        RadioPerLayer.Text = "Per layer"
        RadioPerLayer.UseVisualStyleBackColor = True
        ' 
        ' ComboFormat
        ' 
        ComboFormat.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFormat.Items.AddRange(New Object() {"BC3 (default)", "BC7", "Uncompressed"})
        ComboFormat.Location = New Point(410, 55)
        ComboFormat.Name = "ComboFormat"
        ComboFormat.Size = New Size(190, 23)
        ComboFormat.TabIndex = 2
        ' 
        ' LabelDiffuse
        ' 
        LabelDiffuse.AutoSize = True
        LabelDiffuse.Location = New Point(12, 58)
        LabelDiffuse.Name = "LabelDiffuse"
        LabelDiffuse.Size = New Size(44, 15)
        LabelDiffuse.TabIndex = 2
        LabelDiffuse.Text = "Diffuse"
        ' 
        ' ComboDiffuse
        ' 
        ComboDiffuse.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDiffuse.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboDiffuse.Location = New Point(100, 55)
        ComboDiffuse.Name = "ComboDiffuse"
        ComboDiffuse.Size = New Size(190, 23)
        ComboDiffuse.TabIndex = 3
        ' 
        ' LabelNormal
        ' 
        LabelNormal.AutoSize = True
        LabelNormal.Location = New Point(12, 90)
        LabelNormal.Name = "LabelNormal"
        LabelNormal.Size = New Size(47, 15)
        LabelNormal.TabIndex = 4
        LabelNormal.Text = "Normal"
        ' 
        ' ComboNormal
        ' 
        ComboNormal.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNormal.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboNormal.Location = New Point(100, 87)
        ComboNormal.Name = "ComboNormal"
        ComboNormal.Size = New Size(190, 23)
        ComboNormal.TabIndex = 5
        ' 
        ' LabelSpecular
        ' 
        LabelSpecular.AutoSize = True
        LabelSpecular.Location = New Point(12, 122)
        LabelSpecular.Name = "LabelSpecular"
        LabelSpecular.Size = New Size(52, 15)
        LabelSpecular.TabIndex = 6
        LabelSpecular.Text = "Specular"
        ' 
        ' ComboSpecular
        ' 
        ComboSpecular.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSpecular.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboSpecular.Location = New Point(100, 119)
        ComboSpecular.Name = "ComboSpecular"
        ComboSpecular.Size = New Size(190, 23)
        ComboSpecular.TabIndex = 7
        ' 
        ' ComboFormatN
        ' 
        ComboFormatN.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFormatN.Items.AddRange(New Object() {"BC5", "Uncompressed", "BC7", "BC3 (default)"})
        ComboFormatN.Location = New Point(410, 87)
        ComboFormatN.Name = "ComboFormatN"
        ComboFormatN.Size = New Size(190, 23)
        ComboFormatN.TabIndex = 4
        ' 
        ' ComboFormatS
        ' 
        ComboFormatS.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFormatS.Items.AddRange(New Object() {"BC5 (default)", "Uncompressed", "BC7", "BC3"})
        ComboFormatS.Location = New Point(410, 119)
        ComboFormatS.Name = "ComboFormatS"
        ComboFormatS.Size = New Size(190, 23)
        ComboFormatS.TabIndex = 6
        ' 
        ' CheckGenerateTga
        ' 
        CheckGenerateTga.AutoSize = True
        CheckGenerateTga.Location = New Point(12, 158)
        CheckGenerateTga.Name = "CheckGenerateTga"
        CheckGenerateTga.Size = New Size(187, 19)
        CheckGenerateTga.TabIndex = 8
        CheckGenerateTga.Text = "Generate TGA (uncompressed)"
        CheckGenerateTga.UseVisualStyleBackColor = True
        ' 
        ' CheckDownsizeFromMip0
        ' 
        CheckDownsizeFromMip0.AutoSize = True
        CheckDownsizeFromMip0.Location = New Point(323, 158)
        CheckDownsizeFromMip0.Name = "CheckDownsizeFromMip0"
        CheckDownsizeFromMip0.Size = New Size(230, 19)
        CheckDownsizeFromMip0.TabIndex = 9
        CheckDownsizeFromMip0.Text = "Downsize from mip 0 (slower)"
        CheckDownsizeFromMip0.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxUseHardwareBcDecode
        ' 
        CheckBoxUseHardwareBcDecode.AutoSize = True
        CheckBoxUseHardwareBcDecode.Location = New Point(323, 177)
        CheckBoxUseHardwareBcDecode.Name = "CheckBoxUseHardwareBcDecode"
        CheckBoxUseHardwareBcDecode.Size = New Size(277, 19)
        CheckBoxUseHardwareBcDecode.TabIndex = 9
        CheckBoxUseHardwareBcDecode.Text = "Decode source textures on the GPU (less VRAM)"
        CheckBoxUseHardwareBcDecode.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxAccumInComposite
        ' 
        CheckBoxAccumInComposite.AutoSize = True
        CheckBoxAccumInComposite.Location = New Point(12, 177)
        CheckBoxAccumInComposite.Name = "CheckBoxAccumInComposite"
        CheckBoxAccumInComposite.Size = New Size(270, 19)
        CheckBoxAccumInComposite.TabIndex = 10
        CheckBoxAccumInComposite.Text = "Keep accumulator in Composite Space (faster)"
        CheckBoxAccumInComposite.UseVisualStyleBackColor = True
        ' 
        ' ButtonResetSize
        ' 
        ButtonResetSize.Location = New Point(472, 428)
        ButtonResetSize.Name = "ButtonResetSize"
        ButtonResetSize.Size = New Size(154, 26)
        ButtonResetSize.TabIndex = 1
        ButtonResetSize.Text = "Revert to default"
        ButtonResetSize.UseVisualStyleBackColor = True
        ' 
        ' TabPageConv
        ' 
        TabPageConv.Controls.Add(GroupConvDiffuse)
        TabPageConv.Controls.Add(GroupConvNormal)
        TabPageConv.Controls.Add(GroupConvSwap)
        TabPageConv.Controls.Add(GroupConvFold)
        TabPageConv.Controls.Add(GroupConvOverlay)
        TabPageConv.Controls.Add(GroupConvSeed)
        TabPageConv.Controls.Add(GroupConvDWsByOp)
        TabPageConv.Controls.Add(ButtonResetConv)
        TabPageConv.Location = New Point(4, 24)
        TabPageConv.Name = "TabPageConv"
        TabPageConv.Padding = New Padding(3)
        TabPageConv.Size = New Size(632, 460)
        TabPageConv.TabIndex = 1
        TabPageConv.Text = "FaceTint Conventions"
        TabPageConv.UseVisualStyleBackColor = True
        ' 
        ' GroupConvDiffuse
        ' 
        GroupConvDiffuse.Controls.Add(LblDWork)
        GroupConvDiffuse.Controls.Add(ComboDWork)
        GroupConvDiffuse.Controls.Add(LblDComp)
        GroupConvDiffuse.Controls.Add(ComboDComp)
        GroupConvDiffuse.Controls.Add(LblDSrc)
        GroupConvDiffuse.Controls.Add(ComboDSrc)
        GroupConvDiffuse.Controls.Add(LblDTexSrc)
        GroupConvDiffuse.Controls.Add(ComboDTexSrc)
        GroupConvDiffuse.Controls.Add(LblDOut)
        GroupConvDiffuse.Controls.Add(ComboDOut)
        GroupConvDiffuse.Controls.Add(LblDMask)
        GroupConvDiffuse.Controls.Add(ComboDMask)
        GroupConvDiffuse.Controls.Add(LblDFw)
        GroupConvDiffuse.Controls.Add(ComboDFw)
        GroupConvDiffuse.Controls.Add(LblDSoft)
        GroupConvDiffuse.Controls.Add(ComboDSoft)
        GroupConvDiffuse.Controls.Add(LblDBlend)
        GroupConvDiffuse.Controls.Add(ComboDBlend)
        GroupConvDiffuse.Controls.Add(CheckDSeedG22)
        GroupConvDiffuse.Location = New Point(8, 8)
        GroupConvDiffuse.Name = "GroupConvDiffuse"
        GroupConvDiffuse.Size = New Size(200, 315)
        GroupConvDiffuse.TabIndex = 0
        GroupConvDiffuse.TabStop = False
        GroupConvDiffuse.Text = "Diffuse"
        ' 
        ' LblDWork
        ' 
        LblDWork.AutoSize = True
        LblDWork.Location = New Point(8, 24)
        LblDWork.Name = "LblDWork"
        LblDWork.Size = New Size(61, 15)
        LblDWork.TabIndex = 0
        LblDWork.Text = "Work (ext)"
        ' 
        ' ComboDWork
        ' 
        ComboDWork.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWork.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWork.Location = New Point(84, 21)
        ComboDWork.Name = "ComboDWork"
        ComboDWork.Size = New Size(108, 23)
        ComboDWork.TabIndex = 1
        ' 
        ' LblDComp
        ' 
        LblDComp.AutoSize = True
        LblDComp.Location = New Point(8, 54)
        LblDComp.Name = "LblDComp"
        LblDComp.Size = New Size(65, 15)
        LblDComp.TabIndex = 2
        LblDComp.Text = "Composite"
        ' 
        ' ComboDComp
        ' 
        ComboDComp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDComp.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDComp.Location = New Point(84, 51)
        ComboDComp.Name = "ComboDComp"
        ComboDComp.Size = New Size(108, 23)
        ComboDComp.TabIndex = 3
        ' 
        ' LblDSrc
        ' 
        LblDSrc.AutoSize = True
        LblDSrc.Location = New Point(8, 84)
        LblDSrc.Name = "LblDSrc"
        LblDSrc.Size = New Size(59, 15)
        LblDSrc.TabIndex = 4
        LblDSrc.Text = "Src (solid)"
        ' 
        ' ComboDSrc
        ' 
        ComboDSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDSrc.Location = New Point(84, 81)
        ComboDSrc.Name = "ComboDSrc"
        ComboDSrc.Size = New Size(108, 23)
        ComboDSrc.TabIndex = 5
        ' 
        ' LblDTexSrc
        ' 
        LblDTexSrc.AutoSize = True
        LblDTexSrc.Location = New Point(8, 114)
        LblDTexSrc.Name = "LblDTexSrc"
        LblDTexSrc.Size = New Size(70, 15)
        LblDTexSrc.TabIndex = 6
        LblDTexSrc.Text = "Src (texture)"
        ' 
        ' ComboDTexSrc
        ' 
        ComboDTexSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDTexSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDTexSrc.Location = New Point(84, 111)
        ComboDTexSrc.Name = "ComboDTexSrc"
        ComboDTexSrc.Size = New Size(108, 23)
        ComboDTexSrc.TabIndex = 7
        ' 
        ' LblDOut
        ' 
        LblDOut.AutoSize = True
        LblDOut.Location = New Point(8, 144)
        LblDOut.Name = "LblDOut"
        LblDOut.Size = New Size(45, 15)
        LblDOut.TabIndex = 6
        LblDOut.Text = "Output"
        ' 
        ' ComboDOut
        ' 
        ComboDOut.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDOut.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDOut.Location = New Point(84, 141)
        ComboDOut.Name = "ComboDOut"
        ComboDOut.Size = New Size(108, 23)
        ComboDOut.TabIndex = 7
        ' 
        ' LblDMask
        ' 
        LblDMask.AutoSize = True
        LblDMask.Location = New Point(8, 174)
        LblDMask.Name = "LblDMask"
        LblDMask.Size = New Size(35, 15)
        LblDMask.TabIndex = 8
        LblDMask.Text = "Mask"
        ' 
        ' ComboDMask
        ' 
        ComboDMask.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDMask.Items.AddRange(New Object() {"Raw", "SrgbEncode", "SrgbDecode", "G22Encode", "G22Decode", "G24Encode", "G24Decode"})
        ComboDMask.Location = New Point(84, 171)
        ComboDMask.Name = "ComboDMask"
        ComboDMask.Size = New Size(108, 23)
        ComboDMask.TabIndex = 9
        ' 
        ' LblDFw
        ' 
        LblDFw.AutoSize = True
        LblDFw.Location = New Point(8, 204)
        LblDFw.Name = "LblDFw"
        LblDFw.Size = New Size(66, 15)
        LblDFw.TabIndex = 10
        LblDFw.Text = "Framework"
        ' 
        ' ComboDFw
        ' 
        ComboDFw.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDFw.Items.AddRange(New Object() {"OverPrev", "OverBase", "AddBase", "ModSrc"})
        ComboDFw.Location = New Point(84, 201)
        ComboDFw.Name = "ComboDFw"
        ComboDFw.Size = New Size(108, 23)
        ComboDFw.TabIndex = 11
        ' 
        ' LblDSoft
        ' 
        LblDSoft.AutoSize = True
        LblDSoft.Location = New Point(8, 234)
        LblDSoft.Name = "LblDSoft"
        LblDSoft.Size = New Size(55, 15)
        LblDSoft.TabIndex = 12
        LblDSoft.Text = "SoftLight"
        ' 
        ' ComboDSoft
        ' 
        ComboDSoft.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDSoft.Items.AddRange(New Object() {"W3C", "Gimp", "Illusions", "Pegtop"})
        ComboDSoft.Location = New Point(84, 231)
        ComboDSoft.Name = "ComboDSoft"
        ComboDSoft.Size = New Size(108, 23)
        ComboDSoft.TabIndex = 13
        ' 
        ' LblDBlend
        ' 
        LblDBlend.AutoSize = True
        LblDBlend.Location = New Point(8, 264)
        LblDBlend.Name = "LblDBlend"
        LblDBlend.Size = New Size(37, 15)
        LblDBlend.TabIndex = 14
        LblDBlend.Text = "Blend"
        ' 
        ' ComboDBlend
        ' 
        ComboDBlend.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDBlend.Enabled = False
        ComboDBlend.Items.AddRange(New Object() {"From record"})
        ComboDBlend.Location = New Point(84, 261)
        ComboDBlend.Name = "ComboDBlend"
        ComboDBlend.Size = New Size(108, 23)
        ComboDBlend.TabIndex = 15
        ' 
        ' CheckDSeedG22
        ' 
        CheckDSeedG22.AutoSize = True
        CheckDSeedG22.Location = New Point(8, 294)
        CheckDSeedG22.Name = "CheckDSeedG22"
        CheckDSeedG22.Size = New Size(126, 19)
        CheckDSeedG22.TabIndex = 16
        CheckDSeedG22.Text = "Seed diffuse → G22"
        CheckDSeedG22.UseVisualStyleBackColor = True
        ' 
        ' GroupConvNormal
        ' 
        GroupConvNormal.Controls.Add(LblNWork)
        GroupConvNormal.Controls.Add(ComboNWork)
        GroupConvNormal.Controls.Add(LblNComp)
        GroupConvNormal.Controls.Add(ComboNComp)
        GroupConvNormal.Controls.Add(LblNSrc)
        GroupConvNormal.Controls.Add(ComboNSrc)
        GroupConvNormal.Controls.Add(LblNOut)
        GroupConvNormal.Controls.Add(ComboNOut)
        GroupConvNormal.Controls.Add(LblNMask)
        GroupConvNormal.Controls.Add(ComboNMask)
        GroupConvNormal.Controls.Add(LblNFw)
        GroupConvNormal.Controls.Add(ComboNFw)
        GroupConvNormal.Controls.Add(LblNSoft)
        GroupConvNormal.Controls.Add(ComboNSoft)
        GroupConvNormal.Controls.Add(LblNBlend)
        GroupConvNormal.Controls.Add(ComboNBlend)
        GroupConvNormal.Location = New Point(216, 8)
        GroupConvNormal.Name = "GroupConvNormal"
        GroupConvNormal.Size = New Size(200, 313)
        GroupConvNormal.TabIndex = 1
        GroupConvNormal.TabStop = False
        GroupConvNormal.Text = "Normal + Specular"
        ' 
        ' LblNWork
        ' 
        LblNWork.AutoSize = True
        LblNWork.Location = New Point(8, 24)
        LblNWork.Name = "LblNWork"
        LblNWork.Size = New Size(52, 15)
        LblNWork.TabIndex = 0
        LblNWork.Text = "Working"
        ' 
        ' ComboNWork
        ' 
        ComboNWork.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNWork.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboNWork.Location = New Point(84, 21)
        ComboNWork.Name = "ComboNWork"
        ComboNWork.Size = New Size(108, 23)
        ComboNWork.TabIndex = 1
        ' 
        ' LblNComp
        ' 
        LblNComp.AutoSize = True
        LblNComp.Location = New Point(8, 54)
        LblNComp.Name = "LblNComp"
        LblNComp.Size = New Size(65, 15)
        LblNComp.TabIndex = 2
        LblNComp.Text = "Composite"
        ' 
        ' ComboNComp
        ' 
        ComboNComp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNComp.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboNComp.Location = New Point(84, 51)
        ComboNComp.Name = "ComboNComp"
        ComboNComp.Size = New Size(108, 23)
        ComboNComp.TabIndex = 3
        ' 
        ' LblNSrc
        ' 
        LblNSrc.AutoSize = True
        LblNSrc.Location = New Point(8, 84)
        LblNSrc.Name = "LblNSrc"
        LblNSrc.Size = New Size(23, 15)
        LblNSrc.TabIndex = 4
        LblNSrc.Text = "Src"
        ' 
        ' ComboNSrc
        ' 
        ComboNSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboNSrc.Location = New Point(84, 81)
        ComboNSrc.Name = "ComboNSrc"
        ComboNSrc.Size = New Size(108, 23)
        ComboNSrc.TabIndex = 5
        ' 
        ' LblNOut
        ' 
        LblNOut.AutoSize = True
        LblNOut.Location = New Point(8, 114)
        LblNOut.Name = "LblNOut"
        LblNOut.Size = New Size(45, 15)
        LblNOut.TabIndex = 6
        LblNOut.Text = "Output"
        ' 
        ' ComboNOut
        ' 
        ComboNOut.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNOut.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboNOut.Location = New Point(84, 111)
        ComboNOut.Name = "ComboNOut"
        ComboNOut.Size = New Size(108, 23)
        ComboNOut.TabIndex = 7
        ' 
        ' LblNMask
        ' 
        LblNMask.AutoSize = True
        LblNMask.Location = New Point(8, 144)
        LblNMask.Name = "LblNMask"
        LblNMask.Size = New Size(35, 15)
        LblNMask.TabIndex = 8
        LblNMask.Text = "Mask"
        ' 
        ' ComboNMask
        ' 
        ComboNMask.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNMask.Items.AddRange(New Object() {"Raw", "SrgbEncode", "SrgbDecode", "G22Encode", "G22Decode", "G24Encode", "G24Decode"})
        ComboNMask.Location = New Point(84, 141)
        ComboNMask.Name = "ComboNMask"
        ComboNMask.Size = New Size(108, 23)
        ComboNMask.TabIndex = 9
        ' 
        ' LblNFw
        ' 
        LblNFw.AutoSize = True
        LblNFw.Location = New Point(8, 174)
        LblNFw.Name = "LblNFw"
        LblNFw.Size = New Size(66, 15)
        LblNFw.TabIndex = 10
        LblNFw.Text = "Framework"
        ' 
        ' ComboNFw
        ' 
        ComboNFw.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNFw.Items.AddRange(New Object() {"OverPrev", "OverBase", "AddBase", "ModSrc"})
        ComboNFw.Location = New Point(84, 171)
        ComboNFw.Name = "ComboNFw"
        ComboNFw.Size = New Size(108, 23)
        ComboNFw.TabIndex = 11
        ' 
        ' LblNSoft
        ' 
        LblNSoft.AutoSize = True
        LblNSoft.Location = New Point(8, 204)
        LblNSoft.Name = "LblNSoft"
        LblNSoft.Size = New Size(55, 15)
        LblNSoft.TabIndex = 12
        LblNSoft.Text = "SoftLight"
        ' 
        ' ComboNSoft
        ' 
        ComboNSoft.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNSoft.Items.AddRange(New Object() {"W3C", "Gimp", "Illusions", "Pegtop"})
        ComboNSoft.Location = New Point(84, 201)
        ComboNSoft.Name = "ComboNSoft"
        ComboNSoft.Size = New Size(108, 23)
        ComboNSoft.TabIndex = 13
        ' 
        ' LblNBlend
        ' 
        LblNBlend.AutoSize = True
        LblNBlend.Location = New Point(8, 234)
        LblNBlend.Name = "LblNBlend"
        LblNBlend.Size = New Size(37, 15)
        LblNBlend.TabIndex = 14
        LblNBlend.Text = "Blend"
        ' 
        ' ComboNBlend
        ' 
        ComboNBlend.DropDownStyle = ComboBoxStyle.DropDownList
        ComboNBlend.Enabled = False
        ComboNBlend.Items.AddRange(New Object() {"Replace"})
        ComboNBlend.Location = New Point(84, 231)
        ComboNBlend.Name = "ComboNBlend"
        ComboNBlend.Size = New Size(108, 23)
        ComboNBlend.TabIndex = 15
        ' 
        ' GroupConvFold
        ' 
        GroupConvFold.Controls.Add(LblFoldWork)
        GroupConvFold.Controls.Add(ComboFoldWork)
        GroupConvFold.Controls.Add(LblFoldComp)
        GroupConvFold.Controls.Add(ComboFoldComp)
        GroupConvFold.Controls.Add(LblFoldSrc)
        GroupConvFold.Controls.Add(ComboFoldSrc)
        GroupConvFold.Controls.Add(LblFoldOut)
        GroupConvFold.Controls.Add(ComboFoldOut)
        GroupConvFold.Controls.Add(LblFoldMask)
        GroupConvFold.Controls.Add(ComboFoldMask)
        GroupConvFold.Controls.Add(LblFoldFw)
        GroupConvFold.Controls.Add(ComboFoldFw)
        GroupConvFold.Controls.Add(LblFoldSoft)
        GroupConvFold.Controls.Add(ComboFoldSoft)
        GroupConvFold.Location = New Point(216, 8)
        GroupConvFold.Name = "GroupConvFold"
        GroupConvFold.Size = New Size(200, 250)
        GroupConvFold.TabIndex = 3
        GroupConvFold.TabStop = False
        GroupConvFold.Text = "Fold (SSE albedo)"
        ' 
        ' LblFoldWork
        ' 
        LblFoldWork.AutoSize = True
        LblFoldWork.Location = New Point(8, 24)
        LblFoldWork.Name = "LblFoldWork"
        LblFoldWork.Size = New Size(60, 15)
        LblFoldWork.TabIndex = 60
        LblFoldWork.Text = "Working"
        ' 
        ' ComboFoldWork
        ' 
        ComboFoldWork.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldWork.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboFoldWork.Location = New Point(84, 21)
        ComboFoldWork.Name = "ComboFoldWork"
        ComboFoldWork.Size = New Size(108, 23)
        ComboFoldWork.TabIndex = 61
        ' 
        ' LblFoldComp
        ' 
        LblFoldComp.AutoSize = True
        LblFoldComp.Location = New Point(8, 54)
        LblFoldComp.Name = "LblFoldComp"
        LblFoldComp.Size = New Size(60, 15)
        LblFoldComp.TabIndex = 62
        LblFoldComp.Text = "Composite"
        ' 
        ' ComboFoldComp
        ' 
        ComboFoldComp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldComp.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboFoldComp.Location = New Point(84, 51)
        ComboFoldComp.Name = "ComboFoldComp"
        ComboFoldComp.Size = New Size(108, 23)
        ComboFoldComp.TabIndex = 63
        ' 
        ' LblFoldSrc
        ' 
        LblFoldSrc.AutoSize = True
        LblFoldSrc.Location = New Point(8, 84)
        LblFoldSrc.Name = "LblFoldSrc"
        LblFoldSrc.Size = New Size(60, 15)
        LblFoldSrc.TabIndex = 64
        LblFoldSrc.Text = "Src"
        ' 
        ' ComboFoldSrc
        ' 
        ComboFoldSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboFoldSrc.Location = New Point(84, 81)
        ComboFoldSrc.Name = "ComboFoldSrc"
        ComboFoldSrc.Size = New Size(108, 23)
        ComboFoldSrc.TabIndex = 65
        ' 
        ' LblFoldOut
        ' 
        LblFoldOut.AutoSize = True
        LblFoldOut.Location = New Point(8, 114)
        LblFoldOut.Name = "LblFoldOut"
        LblFoldOut.Size = New Size(60, 15)
        LblFoldOut.TabIndex = 66
        LblFoldOut.Text = "Output"
        ' 
        ' ComboFoldOut
        ' 
        ComboFoldOut.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldOut.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboFoldOut.Location = New Point(84, 111)
        ComboFoldOut.Name = "ComboFoldOut"
        ComboFoldOut.Size = New Size(108, 23)
        ComboFoldOut.TabIndex = 67
        ' 
        ' LblFoldMask
        ' 
        LblFoldMask.AutoSize = True
        LblFoldMask.Location = New Point(8, 144)
        LblFoldMask.Name = "LblFoldMask"
        LblFoldMask.Size = New Size(60, 15)
        LblFoldMask.TabIndex = 68
        LblFoldMask.Text = "Mask conv"
        ' 
        ' ComboFoldMask
        ' 
        ComboFoldMask.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldMask.Items.AddRange(New Object() {"Raw", "SrgbEncode", "SrgbDecode", "G22Encode", "G22Decode", "G24Encode", "G24Decode"})
        ComboFoldMask.Location = New Point(84, 141)
        ComboFoldMask.Name = "ComboFoldMask"
        ComboFoldMask.Size = New Size(108, 23)
        ComboFoldMask.TabIndex = 69
        ' 
        ' LblFoldFw
        ' 
        LblFoldFw.AutoSize = True
        LblFoldFw.Location = New Point(8, 174)
        LblFoldFw.Name = "LblFoldFw"
        LblFoldFw.Size = New Size(60, 15)
        LblFoldFw.TabIndex = 70
        LblFoldFw.Text = "Framework"
        ' 
        ' ComboFoldFw
        ' 
        ComboFoldFw.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldFw.Items.AddRange(New Object() {"OverPrev", "OverBase", "AddBase", "ModSrc"})
        ComboFoldFw.Location = New Point(84, 171)
        ComboFoldFw.Name = "ComboFoldFw"
        ComboFoldFw.Size = New Size(108, 23)
        ComboFoldFw.TabIndex = 71
        ' 
        ' LblFoldSoft
        ' 
        LblFoldSoft.AutoSize = True
        LblFoldSoft.Location = New Point(8, 204)
        LblFoldSoft.Name = "LblFoldSoft"
        LblFoldSoft.Size = New Size(60, 15)
        LblFoldSoft.TabIndex = 72
        LblFoldSoft.Text = "Soft-light"
        ' 
        ' ComboFoldSoft
        ' 
        ComboFoldSoft.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFoldSoft.Items.AddRange(New Object() {"W3C", "Gimp", "Illusions", "Pegtop"})
        ComboFoldSoft.Location = New Point(84, 201)
        ComboFoldSoft.Name = "ComboFoldSoft"
        ComboFoldSoft.Size = New Size(108, 23)
        ComboFoldSoft.TabIndex = 73
        ' 
        ' GroupConvOverlay
        ' 
        GroupConvOverlay.Controls.Add(LblOvlWork)
        GroupConvOverlay.Controls.Add(ComboOvlWork)
        GroupConvOverlay.Controls.Add(LblOvlComp)
        GroupConvOverlay.Controls.Add(ComboOvlComp)
        GroupConvOverlay.Controls.Add(LblOvlSrc)
        GroupConvOverlay.Controls.Add(ComboOvlSrc)
        GroupConvOverlay.Controls.Add(LblOvlOut)
        GroupConvOverlay.Controls.Add(ComboOvlOut)
        GroupConvOverlay.Controls.Add(LblOvlMask)
        GroupConvOverlay.Controls.Add(ComboOvlMask)
        GroupConvOverlay.Controls.Add(LblOvlFw)
        GroupConvOverlay.Controls.Add(ComboOvlFw)
        GroupConvOverlay.Controls.Add(LblOvlSoft)
        GroupConvOverlay.Controls.Add(ComboOvlSoft)
        GroupConvOverlay.Location = New Point(424, 8)
        GroupConvOverlay.Name = "GroupConvOverlay"
        GroupConvOverlay.Size = New Size(200, 250)
        GroupConvOverlay.TabIndex = 3
        GroupConvOverlay.TabStop = False
        GroupConvOverlay.Text = "Overlays (RaceMenu)"
        ' 
        ' LblOvlWork
        ' 
        LblOvlWork.AutoSize = True
        LblOvlWork.Location = New Point(8, 24)
        LblOvlWork.Name = "LblOvlWork"
        LblOvlWork.Size = New Size(60, 15)
        LblOvlWork.TabIndex = 80
        LblOvlWork.Text = "Working"
        ' 
        ' ComboOvlWork
        ' 
        ComboOvlWork.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlWork.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboOvlWork.Location = New Point(84, 21)
        ComboOvlWork.Name = "ComboOvlWork"
        ComboOvlWork.Size = New Size(108, 23)
        ComboOvlWork.TabIndex = 81
        ' 
        ' LblOvlComp
        ' 
        LblOvlComp.AutoSize = True
        LblOvlComp.Location = New Point(8, 54)
        LblOvlComp.Name = "LblOvlComp"
        LblOvlComp.Size = New Size(60, 15)
        LblOvlComp.TabIndex = 82
        LblOvlComp.Text = "Composite"
        ' 
        ' ComboOvlComp
        ' 
        ComboOvlComp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlComp.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboOvlComp.Location = New Point(84, 51)
        ComboOvlComp.Name = "ComboOvlComp"
        ComboOvlComp.Size = New Size(108, 23)
        ComboOvlComp.TabIndex = 83
        ' 
        ' LblOvlSrc
        ' 
        LblOvlSrc.AutoSize = True
        LblOvlSrc.Location = New Point(8, 84)
        LblOvlSrc.Name = "LblOvlSrc"
        LblOvlSrc.Size = New Size(60, 15)
        LblOvlSrc.TabIndex = 84
        LblOvlSrc.Text = "Src"
        ' 
        ' ComboOvlSrc
        ' 
        ComboOvlSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboOvlSrc.Location = New Point(84, 81)
        ComboOvlSrc.Name = "ComboOvlSrc"
        ComboOvlSrc.Size = New Size(108, 23)
        ComboOvlSrc.TabIndex = 85
        ' 
        ' LblOvlOut
        ' 
        LblOvlOut.AutoSize = True
        LblOvlOut.Location = New Point(8, 114)
        LblOvlOut.Name = "LblOvlOut"
        LblOvlOut.Size = New Size(60, 15)
        LblOvlOut.TabIndex = 86
        LblOvlOut.Text = "Output"
        ' 
        ' ComboOvlOut
        ' 
        ComboOvlOut.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlOut.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboOvlOut.Location = New Point(84, 111)
        ComboOvlOut.Name = "ComboOvlOut"
        ComboOvlOut.Size = New Size(108, 23)
        ComboOvlOut.TabIndex = 87
        ' 
        ' LblOvlMask
        ' 
        LblOvlMask.AutoSize = True
        LblOvlMask.Location = New Point(8, 144)
        LblOvlMask.Name = "LblOvlMask"
        LblOvlMask.Size = New Size(60, 15)
        LblOvlMask.TabIndex = 88
        LblOvlMask.Text = "Mask conv"
        ' 
        ' ComboOvlMask
        ' 
        ComboOvlMask.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlMask.Items.AddRange(New Object() {"Raw", "SrgbEncode", "SrgbDecode", "G22Encode", "G22Decode", "G24Encode", "G24Decode"})
        ComboOvlMask.Location = New Point(84, 141)
        ComboOvlMask.Name = "ComboOvlMask"
        ComboOvlMask.Size = New Size(108, 23)
        ComboOvlMask.TabIndex = 89
        ' 
        ' LblOvlFw
        ' 
        LblOvlFw.AutoSize = True
        LblOvlFw.Location = New Point(8, 174)
        LblOvlFw.Name = "LblOvlFw"
        LblOvlFw.Size = New Size(60, 15)
        LblOvlFw.TabIndex = 90
        LblOvlFw.Text = "Framework"
        ' 
        ' ComboOvlFw
        ' 
        ComboOvlFw.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlFw.Items.AddRange(New Object() {"OverPrev", "OverBase", "AddBase", "ModSrc"})
        ComboOvlFw.Location = New Point(84, 171)
        ComboOvlFw.Name = "ComboOvlFw"
        ComboOvlFw.Size = New Size(108, 23)
        ComboOvlFw.TabIndex = 91
        ' 
        ' LblOvlSoft
        ' 
        LblOvlSoft.AutoSize = True
        LblOvlSoft.Location = New Point(8, 204)
        LblOvlSoft.Name = "LblOvlSoft"
        LblOvlSoft.Size = New Size(60, 15)
        LblOvlSoft.TabIndex = 92
        LblOvlSoft.Text = "Soft-light"
        ' 
        ' ComboOvlSoft
        ' 
        ComboOvlSoft.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOvlSoft.Items.AddRange(New Object() {"W3C", "Gimp", "Illusions", "Pegtop"})
        ComboOvlSoft.Location = New Point(84, 201)
        ComboOvlSoft.Name = "ComboOvlSoft"
        ComboOvlSoft.Size = New Size(108, 23)
        ComboOvlSoft.TabIndex = 93
        ' 
        ' GroupConvSeed
        ' 
        GroupConvSeed.Controls.Add(LblSeedMode)
        GroupConvSeed.Controls.Add(ComboSeedMode)
        GroupConvSeed.Controls.Add(LblSeedRgb)
        GroupConvSeed.Controls.Add(NumSeedR)
        GroupConvSeed.Controls.Add(NumSeedG)
        GroupConvSeed.Controls.Add(NumSeedB)
        GroupConvSeed.Location = New Point(216, 262)
        GroupConvSeed.Name = "GroupConvSeed"
        GroupConvSeed.Size = New Size(408, 59)
        GroupConvSeed.TabIndex = 5
        GroupConvSeed.TabStop = False
        GroupConvSeed.Text = "Accumulator seed"
        ' 
        ' LblSeedMode
        ' 
        LblSeedMode.AutoSize = True
        LblSeedMode.Location = New Point(8, 24)
        LblSeedMode.Name = "LblSeedMode"
        LblSeedMode.Size = New Size(42, 15)
        LblSeedMode.TabIndex = 0
        LblSeedMode.Text = "Source"
        ' 
        ' ComboSeedMode
        ' 
        ComboSeedMode.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSeedMode.Items.AddRange(New Object() {"Base texture", "Constant colour"})
        ComboSeedMode.Location = New Point(84, 21)
        ComboSeedMode.Name = "ComboSeedMode"
        ComboSeedMode.Size = New Size(120, 23)
        ComboSeedMode.TabIndex = 1
        ' 
        ' LblSeedRgb
        ' 
        LblSeedRgb.AutoSize = True
        LblSeedRgb.Location = New Point(216, 24)
        LblSeedRgb.Name = "LblSeedRgb"
        LblSeedRgb.Size = New Size(31, 15)
        LblSeedRgb.TabIndex = 2
        LblSeedRgb.Text = "RGB"
        ' 
        ' NumSeedR
        ' 
        NumSeedR.DecimalPlaces = 4
        NumSeedR.Increment = New Decimal(New Integer() {1, 0, 0, 262144})
        NumSeedR.Location = New Point(252, 21)
        NumSeedR.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        NumSeedR.Name = "NumSeedR"
        NumSeedR.Size = New Size(48, 23)
        NumSeedR.TabIndex = 3
        ' 
        ' NumSeedG
        ' 
        NumSeedG.DecimalPlaces = 4
        NumSeedG.Increment = New Decimal(New Integer() {1, 0, 0, 262144})
        NumSeedG.Location = New Point(304, 21)
        NumSeedG.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        NumSeedG.Name = "NumSeedG"
        NumSeedG.Size = New Size(48, 23)
        NumSeedG.TabIndex = 4
        ' 
        ' NumSeedB
        ' 
        NumSeedB.DecimalPlaces = 4
        NumSeedB.Increment = New Decimal(New Integer() {1, 0, 0, 262144})
        NumSeedB.Location = New Point(356, 21)
        NumSeedB.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        NumSeedB.Name = "NumSeedB"
        NumSeedB.Size = New Size(48, 23)
        NumSeedB.TabIndex = 5
        ' 
        ' GroupConvSwap
        ' 
        GroupConvSwap.Controls.Add(LblSWork)
        GroupConvSwap.Controls.Add(ComboSWork)
        GroupConvSwap.Controls.Add(LblSComp)
        GroupConvSwap.Controls.Add(ComboSComp)
        GroupConvSwap.Controls.Add(LblSSrc)
        GroupConvSwap.Controls.Add(ComboSSrc)
        GroupConvSwap.Controls.Add(LblSOut)
        GroupConvSwap.Controls.Add(ComboSOut)
        GroupConvSwap.Controls.Add(LblSMask)
        GroupConvSwap.Controls.Add(ComboSMask)
        GroupConvSwap.Controls.Add(LblSFw)
        GroupConvSwap.Controls.Add(ComboSFw)
        GroupConvSwap.Controls.Add(LblSSoft)
        GroupConvSwap.Controls.Add(ComboSSoft)
        GroupConvSwap.Controls.Add(LblSBlend)
        GroupConvSwap.Controls.Add(ComboSBlend)
        GroupConvSwap.Location = New Point(424, 8)
        GroupConvSwap.Name = "GroupConvSwap"
        GroupConvSwap.Size = New Size(200, 313)
        GroupConvSwap.TabIndex = 2
        GroupConvSwap.TabStop = False
        GroupConvSwap.Text = "Swaps (Diffuse)"
        ' 
        ' LblSWork
        ' 
        LblSWork.AutoSize = True
        LblSWork.Location = New Point(8, 24)
        LblSWork.Name = "LblSWork"
        LblSWork.Size = New Size(52, 15)
        LblSWork.TabIndex = 0
        LblSWork.Text = "Working"
        ' 
        ' ComboSWork
        ' 
        ComboSWork.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSWork.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboSWork.Location = New Point(84, 21)
        ComboSWork.Name = "ComboSWork"
        ComboSWork.Size = New Size(108, 23)
        ComboSWork.TabIndex = 1
        ' 
        ' LblSComp
        ' 
        LblSComp.AutoSize = True
        LblSComp.Location = New Point(8, 54)
        LblSComp.Name = "LblSComp"
        LblSComp.Size = New Size(65, 15)
        LblSComp.TabIndex = 2
        LblSComp.Text = "Composite"
        ' 
        ' ComboSComp
        ' 
        ComboSComp.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSComp.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboSComp.Location = New Point(84, 51)
        ComboSComp.Name = "ComboSComp"
        ComboSComp.Size = New Size(108, 23)
        ComboSComp.TabIndex = 3
        ' 
        ' LblSSrc
        ' 
        LblSSrc.AutoSize = True
        LblSSrc.Location = New Point(8, 84)
        LblSSrc.Name = "LblSSrc"
        LblSSrc.Size = New Size(23, 15)
        LblSSrc.TabIndex = 4
        LblSSrc.Text = "Src"
        ' 
        ' ComboSSrc
        ' 
        ComboSSrc.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSSrc.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboSSrc.Location = New Point(84, 81)
        ComboSSrc.Name = "ComboSSrc"
        ComboSSrc.Size = New Size(108, 23)
        ComboSSrc.TabIndex = 5
        ' 
        ' LblSOut
        ' 
        LblSOut.AutoSize = True
        LblSOut.Location = New Point(8, 114)
        LblSOut.Name = "LblSOut"
        LblSOut.Size = New Size(45, 15)
        LblSOut.TabIndex = 6
        LblSOut.Text = "Output"
        ' 
        ' ComboSOut
        ' 
        ComboSOut.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSOut.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboSOut.Location = New Point(84, 111)
        ComboSOut.Name = "ComboSOut"
        ComboSOut.Size = New Size(108, 23)
        ComboSOut.TabIndex = 7
        ' 
        ' LblSMask
        ' 
        LblSMask.AutoSize = True
        LblSMask.Location = New Point(8, 144)
        LblSMask.Name = "LblSMask"
        LblSMask.Size = New Size(35, 15)
        LblSMask.TabIndex = 8
        LblSMask.Text = "Mask"
        ' 
        ' ComboSMask
        ' 
        ComboSMask.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSMask.Items.AddRange(New Object() {"Raw", "SrgbEncode", "SrgbDecode", "G22Encode", "G22Decode", "G24Encode", "G24Decode"})
        ComboSMask.Location = New Point(84, 141)
        ComboSMask.Name = "ComboSMask"
        ComboSMask.Size = New Size(108, 23)
        ComboSMask.TabIndex = 9
        ' 
        ' LblSFw
        ' 
        LblSFw.AutoSize = True
        LblSFw.Location = New Point(8, 174)
        LblSFw.Name = "LblSFw"
        LblSFw.Size = New Size(66, 15)
        LblSFw.TabIndex = 10
        LblSFw.Text = "Framework"
        ' 
        ' ComboSFw
        ' 
        ComboSFw.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSFw.Items.AddRange(New Object() {"OverPrev", "OverBase", "AddBase", "ModSrc"})
        ComboSFw.Location = New Point(84, 171)
        ComboSFw.Name = "ComboSFw"
        ComboSFw.Size = New Size(108, 23)
        ComboSFw.TabIndex = 11
        ' 
        ' LblSSoft
        ' 
        LblSSoft.AutoSize = True
        LblSSoft.Location = New Point(8, 204)
        LblSSoft.Name = "LblSSoft"
        LblSSoft.Size = New Size(55, 15)
        LblSSoft.TabIndex = 12
        LblSSoft.Text = "SoftLight"
        ' 
        ' ComboSSoft
        ' 
        ComboSSoft.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSSoft.Items.AddRange(New Object() {"W3C", "Gimp", "Illusions", "Pegtop"})
        ComboSSoft.Location = New Point(84, 201)
        ComboSSoft.Name = "ComboSSoft"
        ComboSSoft.Size = New Size(108, 23)
        ComboSSoft.TabIndex = 13
        ' 
        ' LblSBlend
        ' 
        LblSBlend.AutoSize = True
        LblSBlend.Location = New Point(8, 234)
        LblSBlend.Name = "LblSBlend"
        LblSBlend.Size = New Size(37, 15)
        LblSBlend.TabIndex = 14
        LblSBlend.Text = "Blend"
        ' 
        ' ComboSBlend
        ' 
        ComboSBlend.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSBlend.Enabled = False
        ComboSBlend.Items.AddRange(New Object() {"Replace"})
        ComboSBlend.Location = New Point(84, 231)
        ComboSBlend.Name = "ComboSBlend"
        ComboSBlend.Size = New Size(108, 23)
        ComboSBlend.TabIndex = 15
        ' 
        ' GroupConvDWsByOp
        ' 
        GroupConvDWsByOp.Controls.Add(LblDWsReplace)
        GroupConvDWsByOp.Controls.Add(ComboDWsReplace)
        GroupConvDWsByOp.Controls.Add(LblDWsMultiply)
        GroupConvDWsByOp.Controls.Add(ComboDWsMultiply)
        GroupConvDWsByOp.Controls.Add(LblDWsOverlay)
        GroupConvDWsByOp.Controls.Add(ComboDWsOverlay)
        GroupConvDWsByOp.Controls.Add(LblDWsSoftLight)
        GroupConvDWsByOp.Controls.Add(ComboDWsSoftLight)
        GroupConvDWsByOp.Controls.Add(LblDWsHardLight)
        GroupConvDWsByOp.Controls.Add(ComboDWsHardLight)
        GroupConvDWsByOp.Location = New Point(8, 329)
        GroupConvDWsByOp.Name = "GroupConvDWsByOp"
        GroupConvDWsByOp.Size = New Size(616, 84)
        GroupConvDWsByOp.TabIndex = 4
        GroupConvDWsByOp.TabStop = False
        GroupConvDWsByOp.Text = "Diffuse - Working Space by BlendOp (engine-faithful: SoftLight=G22, rest=Linear)"
        ' 
        ' LblDWsReplace
        ' 
        LblDWsReplace.AutoSize = True
        LblDWsReplace.Location = New Point(8, 22)
        LblDWsReplace.Name = "LblDWsReplace"
        LblDWsReplace.Size = New Size(48, 15)
        LblDWsReplace.TabIndex = 0
        LblDWsReplace.Text = "Replace"
        ' 
        ' ComboDWsReplace
        ' 
        ComboDWsReplace.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWsReplace.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWsReplace.Location = New Point(8, 42)
        ComboDWsReplace.Name = "ComboDWsReplace"
        ComboDWsReplace.Size = New Size(110, 23)
        ComboDWsReplace.TabIndex = 1
        ' 
        ' LblDWsMultiply
        ' 
        LblDWsMultiply.AutoSize = True
        LblDWsMultiply.Location = New Point(130, 22)
        LblDWsMultiply.Name = "LblDWsMultiply"
        LblDWsMultiply.Size = New Size(51, 15)
        LblDWsMultiply.TabIndex = 2
        LblDWsMultiply.Text = "Multiply"
        ' 
        ' ComboDWsMultiply
        ' 
        ComboDWsMultiply.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWsMultiply.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWsMultiply.Location = New Point(130, 42)
        ComboDWsMultiply.Name = "ComboDWsMultiply"
        ComboDWsMultiply.Size = New Size(110, 23)
        ComboDWsMultiply.TabIndex = 3
        ' 
        ' LblDWsOverlay
        ' 
        LblDWsOverlay.AutoSize = True
        LblDWsOverlay.Location = New Point(252, 22)
        LblDWsOverlay.Name = "LblDWsOverlay"
        LblDWsOverlay.Size = New Size(47, 15)
        LblDWsOverlay.TabIndex = 4
        LblDWsOverlay.Text = "Overlay"
        ' 
        ' ComboDWsOverlay
        ' 
        ComboDWsOverlay.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWsOverlay.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWsOverlay.Location = New Point(252, 42)
        ComboDWsOverlay.Name = "ComboDWsOverlay"
        ComboDWsOverlay.Size = New Size(110, 23)
        ComboDWsOverlay.TabIndex = 5
        ' 
        ' LblDWsSoftLight
        ' 
        LblDWsSoftLight.AutoSize = True
        LblDWsSoftLight.Location = New Point(374, 22)
        LblDWsSoftLight.Name = "LblDWsSoftLight"
        LblDWsSoftLight.Size = New Size(55, 15)
        LblDWsSoftLight.TabIndex = 6
        LblDWsSoftLight.Text = "SoftLight"
        ' 
        ' ComboDWsSoftLight
        ' 
        ComboDWsSoftLight.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWsSoftLight.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWsSoftLight.Location = New Point(374, 42)
        ComboDWsSoftLight.Name = "ComboDWsSoftLight"
        ComboDWsSoftLight.Size = New Size(110, 23)
        ComboDWsSoftLight.TabIndex = 7
        ' 
        ' LblDWsHardLight
        ' 
        LblDWsHardLight.AutoSize = True
        LblDWsHardLight.Location = New Point(496, 22)
        LblDWsHardLight.Name = "LblDWsHardLight"
        LblDWsHardLight.Size = New Size(60, 15)
        LblDWsHardLight.TabIndex = 8
        LblDWsHardLight.Text = "HardLight"
        ' 
        ' ComboDWsHardLight
        ' 
        ComboDWsHardLight.DropDownStyle = ComboBoxStyle.DropDownList
        ComboDWsHardLight.Items.AddRange(New Object() {"Linear", "Srgb", "G22", "G24"})
        ComboDWsHardLight.Location = New Point(496, 42)
        ComboDWsHardLight.Name = "ComboDWsHardLight"
        ComboDWsHardLight.Size = New Size(110, 23)
        ComboDWsHardLight.TabIndex = 9
        ' 
        ' ButtonResetConv
        ' 
        ButtonResetConv.Location = New Point(472, 428)
        ButtonResetConv.Name = "ButtonResetConv"
        ButtonResetConv.Size = New Size(154, 26)
        ButtonResetConv.TabIndex = 3
        ButtonResetConv.Text = "Revert to default"
        ButtonResetConv.UseVisualStyleBackColor = True
        ' 
        ' TabPageOrder
        ' 
        TabPageOrder.Controls.Add(GroupTintOrder)
        TabPageOrder.Controls.Add(GroupSwapOrder)
        TabPageOrder.Controls.Add(LblSkinPlacement)
        TabPageOrder.Controls.Add(ComboSkinPlacement)
        TabPageOrder.Controls.Add(BtnSortRevert)
        TabPageOrder.Location = New Point(4, 24)
        TabPageOrder.Name = "TabPageOrder"
        TabPageOrder.Padding = New Padding(3)
        TabPageOrder.Size = New Size(632, 460)
        TabPageOrder.TabIndex = 2
        TabPageOrder.Text = "Tint Order"
        TabPageOrder.UseVisualStyleBackColor = True
        ' 
        ' GroupTintOrder
        ' 
        GroupTintOrder.Controls.Add(ListTintRules)
        GroupTintOrder.Controls.Add(ComboTintKey)
        GroupTintOrder.Controls.Add(ChkTintDesc)
        GroupTintOrder.Controls.Add(BtnTintAdd)
        GroupTintOrder.Controls.Add(BtnTintRemove)
        GroupTintOrder.Controls.Add(BtnTintUp)
        GroupTintOrder.Controls.Add(BtnTintDown)
        GroupTintOrder.Location = New Point(8, 8)
        GroupTintOrder.Name = "GroupTintOrder"
        GroupTintOrder.Size = New Size(300, 250)
        GroupTintOrder.TabIndex = 0
        GroupTintOrder.TabStop = False
        GroupTintOrder.Text = "Tint layer order (1st = bottom)"
        ' 
        ' ListTintRules
        ' 
        ListTintRules.FormattingEnabled = True
        ListTintRules.ItemHeight = 15
        ListTintRules.Location = New Point(8, 22)
        ListTintRules.Name = "ListTintRules"
        ListTintRules.Size = New Size(284, 139)
        ListTintRules.TabIndex = 0
        ' 
        ' ComboTintKey
        ' 
        ComboTintKey.DropDownStyle = ComboBoxStyle.DropDownList
        ComboTintKey.Location = New Point(8, 168)
        ComboTintKey.Name = "ComboTintKey"
        ComboTintKey.Size = New Size(180, 23)
        ComboTintKey.TabIndex = 1
        ' 
        ' ChkTintDesc
        ' 
        ChkTintDesc.AutoSize = True
        ChkTintDesc.Location = New Point(196, 170)
        ChkTintDesc.Name = "ChkTintDesc"
        ChkTintDesc.Size = New Size(88, 19)
        ChkTintDesc.TabIndex = 2
        ChkTintDesc.Text = "Descending"
        ChkTintDesc.UseVisualStyleBackColor = True
        ' 
        ' BtnTintAdd
        ' 
        BtnTintAdd.Location = New Point(8, 196)
        BtnTintAdd.Name = "BtnTintAdd"
        BtnTintAdd.Size = New Size(66, 26)
        BtnTintAdd.TabIndex = 3
        BtnTintAdd.Text = "Add"
        BtnTintAdd.UseVisualStyleBackColor = True
        ' 
        ' BtnTintRemove
        ' 
        BtnTintRemove.Location = New Point(78, 196)
        BtnTintRemove.Name = "BtnTintRemove"
        BtnTintRemove.Size = New Size(66, 26)
        BtnTintRemove.TabIndex = 4
        BtnTintRemove.Text = "Remove"
        BtnTintRemove.UseVisualStyleBackColor = True
        ' 
        ' BtnTintUp
        ' 
        BtnTintUp.Location = New Point(148, 196)
        BtnTintUp.Name = "BtnTintUp"
        BtnTintUp.Size = New Size(66, 26)
        BtnTintUp.TabIndex = 5
        BtnTintUp.Text = "Up"
        BtnTintUp.UseVisualStyleBackColor = True
        ' 
        ' BtnTintDown
        ' 
        BtnTintDown.Location = New Point(218, 196)
        BtnTintDown.Name = "BtnTintDown"
        BtnTintDown.Size = New Size(66, 26)
        BtnTintDown.TabIndex = 6
        BtnTintDown.Text = "Down"
        BtnTintDown.UseVisualStyleBackColor = True
        ' 
        ' GroupSwapOrder
        ' 
        GroupSwapOrder.Controls.Add(ListSwapRules)
        GroupSwapOrder.Controls.Add(ComboSwapKey)
        GroupSwapOrder.Controls.Add(ChkSwapDesc)
        GroupSwapOrder.Controls.Add(BtnSwapAdd)
        GroupSwapOrder.Controls.Add(BtnSwapRemove)
        GroupSwapOrder.Controls.Add(BtnSwapUp)
        GroupSwapOrder.Controls.Add(BtnSwapDown)
        GroupSwapOrder.Location = New Point(316, 8)
        GroupSwapOrder.Name = "GroupSwapOrder"
        GroupSwapOrder.Size = New Size(300, 250)
        GroupSwapOrder.TabIndex = 1
        GroupSwapOrder.TabStop = False
        GroupSwapOrder.Text = "Swap order (1st = first applied)"
        ' 
        ' ListSwapRules
        ' 
        ListSwapRules.FormattingEnabled = True
        ListSwapRules.ItemHeight = 15
        ListSwapRules.Location = New Point(8, 22)
        ListSwapRules.Name = "ListSwapRules"
        ListSwapRules.Size = New Size(284, 139)
        ListSwapRules.TabIndex = 0
        ' 
        ' ComboSwapKey
        ' 
        ComboSwapKey.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSwapKey.Location = New Point(8, 168)
        ComboSwapKey.Name = "ComboSwapKey"
        ComboSwapKey.Size = New Size(180, 23)
        ComboSwapKey.TabIndex = 1
        ' 
        ' ChkSwapDesc
        ' 
        ChkSwapDesc.AutoSize = True
        ChkSwapDesc.Location = New Point(196, 170)
        ChkSwapDesc.Name = "ChkSwapDesc"
        ChkSwapDesc.Size = New Size(88, 19)
        ChkSwapDesc.TabIndex = 2
        ChkSwapDesc.Text = "Descending"
        ChkSwapDesc.UseVisualStyleBackColor = True
        ' 
        ' BtnSwapAdd
        ' 
        BtnSwapAdd.Location = New Point(8, 196)
        BtnSwapAdd.Name = "BtnSwapAdd"
        BtnSwapAdd.Size = New Size(66, 26)
        BtnSwapAdd.TabIndex = 3
        BtnSwapAdd.Text = "Add"
        BtnSwapAdd.UseVisualStyleBackColor = True
        ' 
        ' BtnSwapRemove
        ' 
        BtnSwapRemove.Location = New Point(78, 196)
        BtnSwapRemove.Name = "BtnSwapRemove"
        BtnSwapRemove.Size = New Size(66, 26)
        BtnSwapRemove.TabIndex = 4
        BtnSwapRemove.Text = "Remove"
        BtnSwapRemove.UseVisualStyleBackColor = True
        ' 
        ' BtnSwapUp
        ' 
        BtnSwapUp.Location = New Point(148, 196)
        BtnSwapUp.Name = "BtnSwapUp"
        BtnSwapUp.Size = New Size(66, 26)
        BtnSwapUp.TabIndex = 5
        BtnSwapUp.Text = "Up"
        BtnSwapUp.UseVisualStyleBackColor = True
        ' 
        ' BtnSwapDown
        ' 
        BtnSwapDown.Location = New Point(218, 196)
        BtnSwapDown.Name = "BtnSwapDown"
        BtnSwapDown.Size = New Size(66, 26)
        BtnSwapDown.TabIndex = 6
        BtnSwapDown.Text = "Down"
        BtnSwapDown.UseVisualStyleBackColor = True
        ' 
        ' LblSkinPlacement
        ' 
        LblSkinPlacement.AutoSize = True
        LblSkinPlacement.Location = New Point(8, 268)
        LblSkinPlacement.Name = "LblSkinPlacement"
        LblSkinPlacement.Size = New Size(117, 15)
        LblSkinPlacement.TabIndex = 1
        LblSkinPlacement.Text = "SkinTone placement:"
        ' 
        ' ComboSkinPlacement
        ' 
        ComboSkinPlacement.DropDownStyle = ComboBoxStyle.DropDownList
        ComboSkinPlacement.Items.AddRange(New Object() {"Positional", "FirstOfAll", "LastOfAll"})
        ComboSkinPlacement.Location = New Point(160, 265)
        ComboSkinPlacement.Name = "ComboSkinPlacement"
        ComboSkinPlacement.Size = New Size(180, 23)
        ComboSkinPlacement.TabIndex = 2
        ' 
        ' BtnSortRevert
        ' 
        BtnSortRevert.Location = New Point(472, 428)
        BtnSortRevert.Name = "BtnSortRevert"
        BtnSortRevert.Size = New Size(154, 26)
        BtnSortRevert.TabIndex = 3
        BtnSortRevert.Text = "Revert to default"
        BtnSortRevert.UseVisualStyleBackColor = True
        ' 
        ' TabPageFixes
        ' 
        TabPageFixes.Controls.Add(CheckBoxApplyGhoulHeadRearFix)
        TabPageFixes.Controls.Add(CheckBoxApplyEyebrowsFixedColor)
        TabPageFixes.Controls.Add(CheckBoxApplyMouthVanillaFix)
        TabPageFixes.Controls.Add(CheckBoxBakeSseRaceMenuOverlays)
        TabPageFixes.Controls.Add(CheckBoxResolveHphHeadTri)
        TabPageFixes.Controls.Add(CheckBoxReplicateEngineSkinNorm)
        TabPageFixes.Controls.Add(CheckBoxRecalcTangentSpace)
        TabPageFixes.Controls.Add(CheckBoxMatchSubsurfaceFlag)
        TabPageFixes.Controls.Add(BtnFixesRevert)
        TabPageFixes.Location = New Point(4, 24)
        TabPageFixes.Name = "TabPageFixes"
        TabPageFixes.Padding = New Padding(3)
        TabPageFixes.Size = New Size(632, 460)
        TabPageFixes.TabIndex = 3
        TabPageFixes.Text = "Fixes"
        TabPageFixes.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxApplyGhoulHeadRearFix
        ' 
        CheckBoxApplyGhoulHeadRearFix.AutoSize = True
        CheckBoxApplyGhoulHeadRearFix.Location = New Point(12, 16)
        CheckBoxApplyGhoulHeadRearFix.Name = "CheckBoxApplyGhoulHeadRearFix"
        CheckBoxApplyGhoulHeadRearFix.Size = New Size(169, 19)
        CheckBoxApplyGhoulHeadRearFix.TabIndex = 0
        CheckBoxApplyGhoulHeadRearFix.Text = "Apply fix to ghoul headrear"
        CheckBoxApplyGhoulHeadRearFix.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxApplyEyebrowsFixedColor
        ' 
        CheckBoxApplyEyebrowsFixedColor.AutoSize = True
        CheckBoxApplyEyebrowsFixedColor.Location = New Point(12, 41)
        CheckBoxApplyEyebrowsFixedColor.Name = "CheckBoxApplyEyebrowsFixedColor"
        CheckBoxApplyEyebrowsFixedColor.Size = New Size(307, 19)
        CheckBoxApplyEyebrowsFixedColor.TabIndex = 1
        CheckBoxApplyEyebrowsFixedColor.Text = "Apply fixed color to eyebrows (SkipEyebrowsTone.ini)"
        CheckBoxApplyEyebrowsFixedColor.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxApplyMouthVanillaFix
        ' 
        CheckBoxApplyMouthVanillaFix.AutoSize = True
        CheckBoxApplyMouthVanillaFix.Location = New Point(12, 66)
        CheckBoxApplyMouthVanillaFix.Name = "CheckBoxApplyMouthVanillaFix"
        CheckBoxApplyMouthVanillaFix.Size = New Size(304, 19)
        CheckBoxApplyMouthVanillaFix.TabIndex = 2
        CheckBoxApplyMouthVanillaFix.Text = "Fix mouth vanilla error (BaseFemaleHeadChargen.tri)"
        CheckBoxApplyMouthVanillaFix.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxBakeSseRaceMenuOverlays
        ' 
        CheckBoxBakeSseRaceMenuOverlays.AutoSize = True
        CheckBoxBakeSseRaceMenuOverlays.Location = New Point(12, 96)
        CheckBoxBakeSseRaceMenuOverlays.Name = "CheckBoxBakeSseRaceMenuOverlays"
        CheckBoxBakeSseRaceMenuOverlays.Size = New Size(293, 19)
        CheckBoxBakeSseRaceMenuOverlays.TabIndex = 3
        CheckBoxBakeSseRaceMenuOverlays.Text = "Bake RaceMenu face overlays into the diffuse (SSE)"
        CheckBoxBakeSseRaceMenuOverlays.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxResolveHphHeadTri
        ' 
        CheckBoxResolveHphHeadTri.AutoSize = True
        CheckBoxResolveHphHeadTri.Location = New Point(12, 126)
        CheckBoxResolveHphHeadTri.Name = "CheckBoxResolveHphHeadTri"
        CheckBoxResolveHphHeadTri.Size = New Size(371, 19)
        CheckBoxResolveHphHeadTri.TabIndex = 4
        CheckBoxResolveHphHeadTri.Text = "Resolve missing/mismatched head .tri from High Poly Head (SSE)"
        CheckBoxResolveHphHeadTri.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxReplicateEngineSkinNorm
        ' 
        CheckBoxReplicateEngineSkinNorm.AutoSize = True
        CheckBoxReplicateEngineSkinNorm.Location = New Point(12, 151)
        CheckBoxReplicateEngineSkinNorm.Name = "CheckBoxReplicateEngineSkinNorm"
        CheckBoxReplicateEngineSkinNorm.Size = New Size(392, 19)
        CheckBoxReplicateEngineSkinNorm.TabIndex = 5
        CheckBoxReplicateEngineSkinNorm.Text = "Replicate engine skin-weight normalization (non-renormalized) (FO4)"
        CheckBoxReplicateEngineSkinNorm.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxRecalcTangentSpace
        ' 
        CheckBoxRecalcTangentSpace.AutoSize = True
        CheckBoxRecalcTangentSpace.Location = New Point(12, 201)
        CheckBoxRecalcTangentSpace.Name = "CheckBoxRecalcTangentSpace"
        CheckBoxRecalcTangentSpace.Size = New Size(392, 19)
        CheckBoxRecalcTangentSpace.TabIndex = 9
        CheckBoxRecalcTangentSpace.Text = "Recalculate normals and tangent space in the preview (all shapes)"
        CheckBoxRecalcTangentSpace.UseVisualStyleBackColor = True
        ' 
        ' CheckBoxMatchSubsurfaceFlag
        ' 
        CheckBoxMatchSubsurfaceFlag.AutoSize = True
        CheckBoxMatchSubsurfaceFlag.Location = New Point(12, 176)
        CheckBoxMatchSubsurfaceFlag.Name = "CheckBoxMatchSubsurfaceFlag"
        CheckBoxMatchSubsurfaceFlag.Size = New Size(260, 19)
        CheckBoxMatchSubsurfaceFlag.TabIndex = 6
        CheckBoxMatchSubsurfaceFlag.Text = "Match head subsurface lighting flag to body"
        CheckBoxMatchSubsurfaceFlag.UseVisualStyleBackColor = True
        ' 
        ' BtnFixesRevert
        ' 
        BtnFixesRevert.Location = New Point(472, 428)
        BtnFixesRevert.Name = "BtnFixesRevert"
        BtnFixesRevert.Size = New Size(154, 26)
        BtnFixesRevert.TabIndex = 6
        BtnFixesRevert.Text = "Revert to default"
        BtnFixesRevert.UseVisualStyleBackColor = True
        ' 
        ' ButtonOK
        ' 
        ButtonOK.Location = New Point(488, 510)
        ButtonOK.Name = "ButtonOK"
        ButtonOK.Size = New Size(78, 26)
        ButtonOK.TabIndex = 1
        ButtonOK.Text = "OK"
        ButtonOK.UseVisualStyleBackColor = True
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(574, 510)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(78, 26)
        ButtonCancel.TabIndex = 2
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        ' 
        ' CharGenOptionsForm
        ' 
        AcceptButton = ButtonOK
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(664, 548)
        Controls.Add(TabMain)
        Controls.Add(ButtonOK)
        Controls.Add(ButtonCancel)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "CharGenOptionsForm"
        StartPosition = FormStartPosition.CenterParent
        Text = "CharGen Options"
        TabMain.ResumeLayout(False)
        TabPageSize.ResumeLayout(False)
        GroupBoxSize.ResumeLayout(False)
        GroupBoxSize.PerformLayout()
        TabPageConv.ResumeLayout(False)
        GroupConvDiffuse.ResumeLayout(False)
        GroupConvDiffuse.PerformLayout()
        GroupConvNormal.ResumeLayout(False)
        GroupConvNormal.PerformLayout()
        GroupConvSwap.ResumeLayout(False)
        GroupConvSwap.PerformLayout()
        GroupConvDWsByOp.ResumeLayout(False)
        GroupConvDWsByOp.PerformLayout()
        TabPageOrder.ResumeLayout(False)
        TabPageOrder.PerformLayout()
        GroupTintOrder.ResumeLayout(False)
        GroupTintOrder.PerformLayout()
        GroupSwapOrder.ResumeLayout(False)
        GroupSwapOrder.PerformLayout()
        TabPageFixes.ResumeLayout(False)
        TabPageFixes.PerformLayout()
        ResumeLayout(False)
    End Sub

    Friend WithEvents TabMain As System.Windows.Forms.TabControl
    Friend WithEvents TabPageSize As System.Windows.Forms.TabPage
    Friend WithEvents TabPageConv As System.Windows.Forms.TabPage
    Friend WithEvents GroupBoxSize As System.Windows.Forms.GroupBox
    Friend WithEvents RadioAll As System.Windows.Forms.RadioButton
    Friend WithEvents RadioPerLayer As System.Windows.Forms.RadioButton
    Friend WithEvents LabelDiffuse As System.Windows.Forms.Label
    Friend WithEvents ComboDiffuse As System.Windows.Forms.ComboBox
    Friend WithEvents LabelNormal As System.Windows.Forms.Label
    Friend WithEvents ComboNormal As System.Windows.Forms.ComboBox
    Friend WithEvents LabelSpecular As System.Windows.Forms.Label
    Friend WithEvents ComboSpecular As System.Windows.Forms.ComboBox
    Friend WithEvents LabelFormat As System.Windows.Forms.Label
    Friend WithEvents ComboFormat As System.Windows.Forms.ComboBox
    Friend WithEvents ComboFormatN As System.Windows.Forms.ComboBox
    Friend WithEvents ComboFormatS As System.Windows.Forms.ComboBox
    Friend WithEvents CheckGenerateTga As System.Windows.Forms.CheckBox
    Friend WithEvents CheckDownsizeFromMip0 As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxUseHardwareBcDecode As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxAccumInComposite As System.Windows.Forms.CheckBox
    Friend WithEvents ButtonResetSize As System.Windows.Forms.Button
    Friend WithEvents GroupConvDiffuse As System.Windows.Forms.GroupBox
    Friend WithEvents LblDWork As System.Windows.Forms.Label
    Friend WithEvents ComboDWork As System.Windows.Forms.ComboBox
    Friend WithEvents LblDComp As System.Windows.Forms.Label
    Friend WithEvents ComboDComp As System.Windows.Forms.ComboBox
    Friend WithEvents LblDSrc As System.Windows.Forms.Label
    Friend WithEvents ComboDSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblDTexSrc As System.Windows.Forms.Label
    Friend WithEvents ComboDTexSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblDOut As System.Windows.Forms.Label
    Friend WithEvents ComboDOut As System.Windows.Forms.ComboBox
    Friend WithEvents LblDMask As System.Windows.Forms.Label
    Friend WithEvents ComboDMask As System.Windows.Forms.ComboBox
    Friend WithEvents LblDFw As System.Windows.Forms.Label
    Friend WithEvents ComboDFw As System.Windows.Forms.ComboBox
    Friend WithEvents LblDSoft As System.Windows.Forms.Label
    Friend WithEvents ComboDSoft As System.Windows.Forms.ComboBox
    Friend WithEvents LblDBlend As System.Windows.Forms.Label
    Friend WithEvents ComboDBlend As System.Windows.Forms.ComboBox
    Friend WithEvents CheckDSeedG22 As System.Windows.Forms.CheckBox
    Friend WithEvents GroupConvDWsByOp As System.Windows.Forms.GroupBox
    Friend WithEvents LblDWsReplace As System.Windows.Forms.Label
    Friend WithEvents ComboDWsReplace As System.Windows.Forms.ComboBox
    Friend WithEvents LblDWsMultiply As System.Windows.Forms.Label
    Friend WithEvents ComboDWsMultiply As System.Windows.Forms.ComboBox
    Friend WithEvents LblDWsOverlay As System.Windows.Forms.Label
    Friend WithEvents ComboDWsOverlay As System.Windows.Forms.ComboBox
    Friend WithEvents LblDWsSoftLight As System.Windows.Forms.Label
    Friend WithEvents ComboDWsSoftLight As System.Windows.Forms.ComboBox
    Friend WithEvents LblDWsHardLight As System.Windows.Forms.Label
    Friend WithEvents ComboDWsHardLight As System.Windows.Forms.ComboBox
    Friend WithEvents GroupConvNormal As System.Windows.Forms.GroupBox
    Friend WithEvents LblNWork As System.Windows.Forms.Label
    Friend WithEvents ComboNWork As System.Windows.Forms.ComboBox
    Friend WithEvents LblNComp As System.Windows.Forms.Label
    Friend WithEvents ComboNComp As System.Windows.Forms.ComboBox
    Friend WithEvents LblNSrc As System.Windows.Forms.Label
    Friend WithEvents ComboNSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblNOut As System.Windows.Forms.Label
    Friend WithEvents ComboNOut As System.Windows.Forms.ComboBox
    Friend WithEvents LblNMask As System.Windows.Forms.Label
    Friend WithEvents ComboNMask As System.Windows.Forms.ComboBox
    Friend WithEvents LblNFw As System.Windows.Forms.Label
    Friend WithEvents ComboNFw As System.Windows.Forms.ComboBox
    Friend WithEvents LblNSoft As System.Windows.Forms.Label
    Friend WithEvents ComboNSoft As System.Windows.Forms.ComboBox
    Friend WithEvents LblNBlend As System.Windows.Forms.Label
    Friend WithEvents ComboNBlend As System.Windows.Forms.ComboBox
    Friend WithEvents GroupConvFold As System.Windows.Forms.GroupBox
    Friend WithEvents LblFoldWork As System.Windows.Forms.Label
    Friend WithEvents ComboFoldWork As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldComp As System.Windows.Forms.Label
    Friend WithEvents ComboFoldComp As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldSrc As System.Windows.Forms.Label
    Friend WithEvents ComboFoldSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldOut As System.Windows.Forms.Label
    Friend WithEvents ComboFoldOut As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldMask As System.Windows.Forms.Label
    Friend WithEvents ComboFoldMask As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldFw As System.Windows.Forms.Label
    Friend WithEvents ComboFoldFw As System.Windows.Forms.ComboBox
    Friend WithEvents LblFoldSoft As System.Windows.Forms.Label
    Friend WithEvents ComboFoldSoft As System.Windows.Forms.ComboBox
    Friend WithEvents GroupConvOverlay As System.Windows.Forms.GroupBox
    Friend WithEvents LblOvlWork As System.Windows.Forms.Label
    Friend WithEvents ComboOvlWork As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlComp As System.Windows.Forms.Label
    Friend WithEvents ComboOvlComp As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlSrc As System.Windows.Forms.Label
    Friend WithEvents ComboOvlSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlOut As System.Windows.Forms.Label
    Friend WithEvents ComboOvlOut As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlMask As System.Windows.Forms.Label
    Friend WithEvents ComboOvlMask As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlFw As System.Windows.Forms.Label
    Friend WithEvents ComboOvlFw As System.Windows.Forms.ComboBox
    Friend WithEvents LblOvlSoft As System.Windows.Forms.Label
    Friend WithEvents ComboOvlSoft As System.Windows.Forms.ComboBox
    Friend WithEvents GroupConvSeed As System.Windows.Forms.GroupBox
    Friend WithEvents LblSeedMode As System.Windows.Forms.Label
    Friend WithEvents ComboSeedMode As System.Windows.Forms.ComboBox
    Friend WithEvents LblSeedRgb As System.Windows.Forms.Label
    Friend WithEvents NumSeedR As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSeedG As System.Windows.Forms.NumericUpDown
    Friend WithEvents NumSeedB As System.Windows.Forms.NumericUpDown
    Friend WithEvents GroupConvSwap As System.Windows.Forms.GroupBox
    Friend WithEvents LblSWork As System.Windows.Forms.Label
    Friend WithEvents ComboSWork As System.Windows.Forms.ComboBox
    Friend WithEvents LblSComp As System.Windows.Forms.Label
    Friend WithEvents ComboSComp As System.Windows.Forms.ComboBox
    Friend WithEvents LblSSrc As System.Windows.Forms.Label
    Friend WithEvents ComboSSrc As System.Windows.Forms.ComboBox
    Friend WithEvents LblSOut As System.Windows.Forms.Label
    Friend WithEvents ComboSOut As System.Windows.Forms.ComboBox
    Friend WithEvents LblSMask As System.Windows.Forms.Label
    Friend WithEvents ComboSMask As System.Windows.Forms.ComboBox
    Friend WithEvents LblSFw As System.Windows.Forms.Label
    Friend WithEvents ComboSFw As System.Windows.Forms.ComboBox
    Friend WithEvents LblSSoft As System.Windows.Forms.Label
    Friend WithEvents ComboSSoft As System.Windows.Forms.ComboBox
    Friend WithEvents LblSBlend As System.Windows.Forms.Label
    Friend WithEvents ComboSBlend As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonResetConv As System.Windows.Forms.Button
    Friend WithEvents ButtonOK As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents TabPageOrder As System.Windows.Forms.TabPage
    Friend WithEvents GroupTintOrder As System.Windows.Forms.GroupBox
    Friend WithEvents ListTintRules As System.Windows.Forms.ListBox
    Friend WithEvents ComboTintKey As System.Windows.Forms.ComboBox
    Friend WithEvents ChkTintDesc As System.Windows.Forms.CheckBox
    Friend WithEvents BtnTintAdd As System.Windows.Forms.Button
    Friend WithEvents BtnTintRemove As System.Windows.Forms.Button
    Friend WithEvents BtnTintUp As System.Windows.Forms.Button
    Friend WithEvents BtnTintDown As System.Windows.Forms.Button
    Friend WithEvents GroupSwapOrder As System.Windows.Forms.GroupBox
    Friend WithEvents ListSwapRules As System.Windows.Forms.ListBox
    Friend WithEvents ComboSwapKey As System.Windows.Forms.ComboBox
    Friend WithEvents ChkSwapDesc As System.Windows.Forms.CheckBox
    Friend WithEvents BtnSwapAdd As System.Windows.Forms.Button
    Friend WithEvents BtnSwapRemove As System.Windows.Forms.Button
    Friend WithEvents BtnSwapUp As System.Windows.Forms.Button
    Friend WithEvents BtnSwapDown As System.Windows.Forms.Button
    Friend WithEvents LblSkinPlacement As System.Windows.Forms.Label
    Friend WithEvents ComboSkinPlacement As System.Windows.Forms.ComboBox
    Friend WithEvents BtnSortRevert As System.Windows.Forms.Button
    Friend WithEvents TabPageFixes As System.Windows.Forms.TabPage
    Friend WithEvents CheckBoxApplyGhoulHeadRearFix As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyEyebrowsFixedColor As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxApplyMouthVanillaFix As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxBakeSseRaceMenuOverlays As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxResolveHphHeadTri As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxReplicateEngineSkinNorm As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxRecalcTangentSpace As System.Windows.Forms.CheckBox
    Friend WithEvents CheckBoxMatchSubsurfaceFlag As System.Windows.Forms.CheckBox
    Friend WithEvents BtnFixesRevert As System.Windows.Forms.Button
End Class
