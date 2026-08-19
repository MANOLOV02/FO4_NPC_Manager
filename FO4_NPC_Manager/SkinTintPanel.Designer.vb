' UI built in Designer per 00-reglas-ui-y-vb.md.
'
' Este control es la mitad VISUAL del tab "Skin tint match". Antes vivia dentro de EditBody_Form.Designer.vb
' y su logica en un PARCIAL de EditBody_Form (EditBody_SkinTint.vb). Eso rompia el build con MSB3577: VB deriva
' el nombre del manifiesto de un .resx desde la CLASE declarada en el .vb hermano, no desde el nombre del
' archivo, asi que el .resx que Visual Studio le generaba al parcial resolvia a EditBody_Form.resources y
' chocaba con el del formulario. Un UserControl tiene clase propia => resx propio => no hay choque.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class SkinTintPanel
    Inherits System.Windows.Forms.UserControl

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
        Dim resources As System.ComponentModel.ComponentResourceManager = New System.ComponentModel.ComponentResourceManager(GetType(SkinTintPanel))
        SkinTintTabLayout = New TableLayoutPanel()
        LabelSkinTintLegend = New Label()
        LabelSkinTintIntensityMeaning = New Label()
        GroupBoxSkinTintMatch = New GroupBox()
        SkinTintMatchLayout = New TableLayoutPanel()
        ButtonSkinTintPickSource = New Button()
        PanelSkinTintSourceSwatch = New Panel()
        LabelSkinTintSource = New Label()
        ButtonSkinTintPickTarget = New Button()
        PanelSkinTintTargetSwatch = New Panel()
        LabelSkinTintTarget = New Label()
        ButtonSkinTintAuto = New Button()
        LabelSkinTintAutoHint = New Label()
        LabelSkinTintQuality = New Label()
        SliderSkinTintQuality = New TinySliderTextBox()
        LabelSkinTintSampleSize = New Label()
        SliderSkinTintSampleSize = New TinySliderTextBox()
        GroupBoxSkinTintOffsets = New GroupBox()
        SkinTintOffsetsLayout = New TableLayoutPanel()
        LabelSkinTintR = New Label()
        SliderSkinTintR = New TinySliderTextBox()
        LabelSkinTintG = New Label()
        SliderSkinTintG = New TinySliderTextBox()
        LabelSkinTintB = New Label()
        SliderSkinTintB = New TinySliderTextBox()
        LabelSkinTintIntensity = New Label()
        SliderSkinTintIntensity = New TinySliderTextBox()
        ButtonSkinTintReset = New Button()
        LabelSkinTintStatus = New Label()
        LabelSkinTintGate = New Label()
        SkinTintTabLayout.SuspendLayout()
        GroupBoxSkinTintMatch.SuspendLayout()
        SkinTintMatchLayout.SuspendLayout()
        GroupBoxSkinTintOffsets.SuspendLayout()
        SkinTintOffsetsLayout.SuspendLayout()
        SuspendLayout()
        ' 
        ' SkinTintTabLayout
        ' 
        SkinTintTabLayout.AutoScroll = True
        SkinTintTabLayout.ColumnCount = 1
        SkinTintTabLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinTintTabLayout.Controls.Add(LabelSkinTintLegend, 0, 0)
        SkinTintTabLayout.Controls.Add(LabelSkinTintIntensityMeaning, 0, 1)
        SkinTintTabLayout.Controls.Add(GroupBoxSkinTintMatch, 0, 2)
        SkinTintTabLayout.Controls.Add(GroupBoxSkinTintOffsets, 0, 3)
        SkinTintTabLayout.Controls.Add(LabelSkinTintStatus, 0, 4)
        SkinTintTabLayout.Controls.Add(LabelSkinTintGate, 0, 5)
        SkinTintTabLayout.Dock = DockStyle.Fill
        SkinTintTabLayout.Location = New Point(6, 6)
        SkinTintTabLayout.Name = "SkinTintTabLayout"
        SkinTintTabLayout.RowCount = 7
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle())
        SkinTintTabLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        SkinTintTabLayout.Size = New Size(818, 680)
        SkinTintTabLayout.TabIndex = 0
        ' 
        ' LabelSkinTintLegend
        ' 
        LabelSkinTintLegend.AutoSize = True
        LabelSkinTintLegend.Location = New Point(2, 2)
        LabelSkinTintLegend.Margin = New Padding(2)
        LabelSkinTintLegend.Name = "LabelSkinTintLegend"
        LabelSkinTintLegend.Size = New Size(799, 45)
        LabelSkinTintLegend.TabIndex = 0
        LabelSkinTintLegend.Text = resources.GetString("LabelSkinTintLegend.Text")
        ' 
        ' LabelSkinTintIntensityMeaning
        ' 
        LabelSkinTintIntensityMeaning.AutoSize = True
        LabelSkinTintIntensityMeaning.Location = New Point(2, 51)
        LabelSkinTintIntensityMeaning.Margin = New Padding(2, 2, 2, 8)
        LabelSkinTintIntensityMeaning.Name = "LabelSkinTintIntensityMeaning"
        LabelSkinTintIntensityMeaning.Size = New Size(310, 15)
        LabelSkinTintIntensityMeaning.TabIndex = 1
        LabelSkinTintIntensityMeaning.Text = "Intensity is the QNAM alpha (the body soft-light opacity)."
        ' 
        ' GroupBoxSkinTintMatch
        ' 
        GroupBoxSkinTintMatch.AutoSize = True
        GroupBoxSkinTintMatch.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSkinTintMatch.Controls.Add(SkinTintMatchLayout)
        GroupBoxSkinTintMatch.Dock = DockStyle.Fill
        GroupBoxSkinTintMatch.Location = New Point(2, 76)
        GroupBoxSkinTintMatch.Margin = New Padding(2, 2, 2, 8)
        GroupBoxSkinTintMatch.Name = "GroupBoxSkinTintMatch"
        GroupBoxSkinTintMatch.Size = New Size(814, 181)
        GroupBoxSkinTintMatch.TabIndex = 2
        GroupBoxSkinTintMatch.TabStop = False
        GroupBoxSkinTintMatch.Text = "Match"
        ' 
        ' SkinTintMatchLayout
        ' 
        SkinTintMatchLayout.AutoSize = True
        SkinTintMatchLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SkinTintMatchLayout.ColumnCount = 3
        SkinTintMatchLayout.ColumnStyles.Add(New ColumnStyle())
        SkinTintMatchLayout.ColumnStyles.Add(New ColumnStyle())
        SkinTintMatchLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinTintMatchLayout.Controls.Add(ButtonSkinTintPickSource, 0, 0)
        SkinTintMatchLayout.Controls.Add(PanelSkinTintSourceSwatch, 1, 0)
        SkinTintMatchLayout.Controls.Add(LabelSkinTintSource, 2, 0)
        SkinTintMatchLayout.Controls.Add(ButtonSkinTintPickTarget, 0, 1)
        SkinTintMatchLayout.Controls.Add(PanelSkinTintTargetSwatch, 1, 1)
        SkinTintMatchLayout.Controls.Add(LabelSkinTintTarget, 2, 1)
        SkinTintMatchLayout.Controls.Add(ButtonSkinTintAuto, 0, 2)
        SkinTintMatchLayout.Controls.Add(LabelSkinTintAutoHint, 1, 2)
        SkinTintMatchLayout.Controls.Add(LabelSkinTintQuality, 0, 3)
        SkinTintMatchLayout.Controls.Add(SliderSkinTintQuality, 1, 3)
        SkinTintMatchLayout.Controls.Add(LabelSkinTintSampleSize, 0, 4)
        SkinTintMatchLayout.Controls.Add(SliderSkinTintSampleSize, 1, 4)
        SkinTintMatchLayout.Dock = DockStyle.Fill
        SkinTintMatchLayout.Location = New Point(3, 19)
        SkinTintMatchLayout.Name = "SkinTintMatchLayout"
        SkinTintMatchLayout.Padding = New Padding(4)
        SkinTintMatchLayout.RowCount = 5
        SkinTintMatchLayout.RowStyles.Add(New RowStyle())
        SkinTintMatchLayout.RowStyles.Add(New RowStyle())
        SkinTintMatchLayout.RowStyles.Add(New RowStyle())
        SkinTintMatchLayout.RowStyles.Add(New RowStyle())
        SkinTintMatchLayout.RowStyles.Add(New RowStyle())
        SkinTintMatchLayout.Size = New Size(808, 159)
        SkinTintMatchLayout.TabIndex = 0
        ' 
        ' ButtonSkinTintPickSource
        ' 
        ButtonSkinTintPickSource.Location = New Point(6, 6)
        ButtonSkinTintPickSource.Margin = New Padding(2)
        ButtonSkinTintPickSource.Name = "ButtonSkinTintPickSource"
        ButtonSkinTintPickSource.Size = New Size(170, 25)
        ButtonSkinTintPickSource.TabIndex = 0
        ButtonSkinTintPickSource.Text = "Pick source (face)..."
        ButtonSkinTintPickSource.UseVisualStyleBackColor = True
        ' 
        ' PanelSkinTintSourceSwatch
        ' 
        PanelSkinTintSourceSwatch.BackColor = SystemColors.Control
        PanelSkinTintSourceSwatch.BorderStyle = BorderStyle.FixedSingle
        PanelSkinTintSourceSwatch.Location = New Point(180, 8)
        PanelSkinTintSourceSwatch.Margin = New Padding(2, 4, 2, 2)
        PanelSkinTintSourceSwatch.Name = "PanelSkinTintSourceSwatch"
        PanelSkinTintSourceSwatch.Size = New Size(28, 20)
        PanelSkinTintSourceSwatch.TabIndex = 1
        ' 
        ' LabelSkinTintSource
        ' 
        LabelSkinTintSource.AutoSize = True
        LabelSkinTintSource.Location = New Point(212, 10)
        LabelSkinTintSource.Margin = New Padding(2, 6, 2, 2)
        LabelSkinTintSource.Name = "LabelSkinTintSource"
        LabelSkinTintSource.Size = New Size(63, 15)
        LabelSkinTintSource.TabIndex = 2
        LabelSkinTintSource.Text = "not picked"
        ' 
        ' ButtonSkinTintPickTarget
        ' 
        ButtonSkinTintPickTarget.Location = New Point(6, 35)
        ButtonSkinTintPickTarget.Margin = New Padding(2)
        ButtonSkinTintPickTarget.Name = "ButtonSkinTintPickTarget"
        ButtonSkinTintPickTarget.Size = New Size(170, 25)
        ButtonSkinTintPickTarget.TabIndex = 3
        ButtonSkinTintPickTarget.Text = "Pick target (body)..."
        ButtonSkinTintPickTarget.UseVisualStyleBackColor = True
        ' 
        ' PanelSkinTintTargetSwatch
        ' 
        PanelSkinTintTargetSwatch.BackColor = SystemColors.Control
        PanelSkinTintTargetSwatch.BorderStyle = BorderStyle.FixedSingle
        PanelSkinTintTargetSwatch.Location = New Point(180, 37)
        PanelSkinTintTargetSwatch.Margin = New Padding(2, 4, 2, 2)
        PanelSkinTintTargetSwatch.Name = "PanelSkinTintTargetSwatch"
        PanelSkinTintTargetSwatch.Size = New Size(28, 20)
        PanelSkinTintTargetSwatch.TabIndex = 4
        ' 
        ' LabelSkinTintTarget
        ' 
        LabelSkinTintTarget.AutoSize = True
        LabelSkinTintTarget.Location = New Point(212, 39)
        LabelSkinTintTarget.Margin = New Padding(2, 6, 2, 2)
        LabelSkinTintTarget.Name = "LabelSkinTintTarget"
        LabelSkinTintTarget.Size = New Size(421, 15)
        LabelSkinTintTarget.TabIndex = 5
        LabelSkinTintTarget.Text = "not picked (only its POSITION is kept: its colour is what the adjustment moves)"
        ' 
        ' ButtonSkinTintAuto
        ' 
        ButtonSkinTintAuto.Enabled = False
        ButtonSkinTintAuto.Location = New Point(6, 64)
        ButtonSkinTintAuto.Margin = New Padding(2)
        ButtonSkinTintAuto.Name = "ButtonSkinTintAuto"
        ButtonSkinTintAuto.Size = New Size(170, 25)
        ButtonSkinTintAuto.TabIndex = 6
        ButtonSkinTintAuto.Text = "Auto-calc"
        ButtonSkinTintAuto.UseVisualStyleBackColor = True
        ' 
        ' LabelSkinTintAutoHint
        ' 
        LabelSkinTintAutoHint.AutoSize = True
        SkinTintMatchLayout.SetColumnSpan(LabelSkinTintAutoHint, 2)
        LabelSkinTintAutoHint.Location = New Point(180, 68)
        LabelSkinTintAutoHint.Margin = New Padding(2, 6, 2, 2)
        LabelSkinTintAutoHint.Name = "LabelSkinTintAutoHint"
        LabelSkinTintAutoHint.Size = New Size(621, 30)
        LabelSkinTintAutoHint.TabIndex = 7
        LabelSkinTintAutoHint.Text = "Searches the R/G/B offsets that bring the body pixel closest to the source colour, re-rendering at every step. Intensity is only raised when the tone hits its limit."
        ' 
        ' LabelSkinTintQuality
        ' 
        LabelSkinTintQuality.Anchor = AnchorStyles.Left
        LabelSkinTintQuality.AutoSize = True
        LabelSkinTintQuality.Location = New Point(6, 110)
        LabelSkinTintQuality.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintQuality.Name = "LabelSkinTintQuality"
        LabelSkinTintQuality.Size = New Size(90, 15)
        LabelSkinTintQuality.TabIndex = 8
        LabelSkinTintQuality.Text = "Quality (passes)"
        LabelSkinTintQuality.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintQuality
        ' 
        SliderSkinTintQuality.AccentColor = SystemColors.HotTrack
        SliderSkinTintQuality.BackColor = SystemColors.Control
        SkinTintMatchLayout.SetColumnSpan(SliderSkinTintQuality, 2)
        SliderSkinTintQuality.DisplayFormat = "0"
        SliderSkinTintQuality.Dock = DockStyle.Fill
        SliderSkinTintQuality.LargeChange = 1R
        SliderSkinTintQuality.Location = New Point(180, 102)
        SliderSkinTintQuality.Margin = New Padding(2)
        SliderSkinTintQuality.Maximum = 6R
        SliderSkinTintQuality.Minimum = 1R
        SliderSkinTintQuality.MinimumSize = New Size(120, 22)
        SliderSkinTintQuality.Name = "SliderSkinTintQuality"
        SliderSkinTintQuality.ShowTicks = True
        SliderSkinTintQuality.Size = New Size(622, 28)
        SliderSkinTintQuality.TabIndex = 9
        SliderSkinTintQuality.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintQuality.ThumbColor = SystemColors.HotTrack
        SliderSkinTintQuality.ThumbRadius = 4F
        SliderSkinTintQuality.TickFrequency = 1R
        SliderSkinTintQuality.TrackColor = SystemColors.ControlDark
        SliderSkinTintQuality.Value = 3R
        ' 
        ' LabelSkinTintSampleSize
        ' 
        LabelSkinTintSampleSize.Anchor = AnchorStyles.Left
        LabelSkinTintSampleSize.AutoSize = True
        LabelSkinTintSampleSize.Location = New Point(6, 142)
        LabelSkinTintSampleSize.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintSampleSize.Name = "LabelSkinTintSampleSize"
        LabelSkinTintSampleSize.Size = New Size(91, 15)
        LabelSkinTintSampleSize.TabIndex = 10
        LabelSkinTintSampleSize.Text = "Sample size (px)"
        LabelSkinTintSampleSize.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintSampleSize
        ' 
        SliderSkinTintSampleSize.AccentColor = SystemColors.HotTrack
        SliderSkinTintSampleSize.BackColor = SystemColors.Control
        SkinTintMatchLayout.SetColumnSpan(SliderSkinTintSampleSize, 2)
        SliderSkinTintSampleSize.DisplayFormat = "0"
        SliderSkinTintSampleSize.Dock = DockStyle.Fill
        SliderSkinTintSampleSize.LargeChange = 2R
        SliderSkinTintSampleSize.Location = New Point(180, 134)
        SliderSkinTintSampleSize.Margin = New Padding(2)
        SliderSkinTintSampleSize.Maximum = 16R
        SliderSkinTintSampleSize.Minimum = 2R
        SliderSkinTintSampleSize.MinimumSize = New Size(120, 22)
        SliderSkinTintSampleSize.Name = "SliderSkinTintSampleSize"
        SliderSkinTintSampleSize.ShowTicks = True
        SliderSkinTintSampleSize.Size = New Size(622, 28)
        SliderSkinTintSampleSize.SmallChange = 2R
        SliderSkinTintSampleSize.TabIndex = 11
        SliderSkinTintSampleSize.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintSampleSize.ThumbColor = SystemColors.HotTrack
        SliderSkinTintSampleSize.ThumbRadius = 4F
        SliderSkinTintSampleSize.TickFrequency = 2R
        SliderSkinTintSampleSize.TrackColor = SystemColors.ControlDark
        SliderSkinTintSampleSize.Value = 8R
        ' 
        ' GroupBoxSkinTintOffsets
        ' 
        GroupBoxSkinTintOffsets.AutoSize = True
        GroupBoxSkinTintOffsets.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupBoxSkinTintOffsets.Controls.Add(SkinTintOffsetsLayout)
        GroupBoxSkinTintOffsets.Dock = DockStyle.Fill
        GroupBoxSkinTintOffsets.Location = New Point(2, 267)
        GroupBoxSkinTintOffsets.Margin = New Padding(2, 2, 2, 8)
        GroupBoxSkinTintOffsets.Name = "GroupBoxSkinTintOffsets"
        GroupBoxSkinTintOffsets.Size = New Size(814, 191)
        GroupBoxSkinTintOffsets.TabIndex = 3
        GroupBoxSkinTintOffsets.TabStop = False
        GroupBoxSkinTintOffsets.Text = "Offsets (QNAM)"
        ' 
        ' SkinTintOffsetsLayout
        ' 
        SkinTintOffsetsLayout.AutoSize = True
        SkinTintOffsetsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SkinTintOffsetsLayout.ColumnCount = 2
        SkinTintOffsetsLayout.ColumnStyles.Add(New ColumnStyle())
        SkinTintOffsetsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        SkinTintOffsetsLayout.Controls.Add(LabelSkinTintR, 0, 0)
        SkinTintOffsetsLayout.Controls.Add(SliderSkinTintR, 1, 0)
        SkinTintOffsetsLayout.Controls.Add(LabelSkinTintG, 0, 1)
        SkinTintOffsetsLayout.Controls.Add(SliderSkinTintG, 1, 1)
        SkinTintOffsetsLayout.Controls.Add(LabelSkinTintB, 0, 2)
        SkinTintOffsetsLayout.Controls.Add(SliderSkinTintB, 1, 2)
        SkinTintOffsetsLayout.Controls.Add(LabelSkinTintIntensity, 0, 3)
        SkinTintOffsetsLayout.Controls.Add(SliderSkinTintIntensity, 1, 3)
        SkinTintOffsetsLayout.Controls.Add(ButtonSkinTintReset, 1, 4)
        SkinTintOffsetsLayout.Dock = DockStyle.Fill
        SkinTintOffsetsLayout.Location = New Point(3, 19)
        SkinTintOffsetsLayout.Name = "SkinTintOffsetsLayout"
        SkinTintOffsetsLayout.Padding = New Padding(4)
        SkinTintOffsetsLayout.RowCount = 5
        SkinTintOffsetsLayout.RowStyles.Add(New RowStyle())
        SkinTintOffsetsLayout.RowStyles.Add(New RowStyle())
        SkinTintOffsetsLayout.RowStyles.Add(New RowStyle())
        SkinTintOffsetsLayout.RowStyles.Add(New RowStyle())
        SkinTintOffsetsLayout.RowStyles.Add(New RowStyle())
        SkinTintOffsetsLayout.Size = New Size(808, 169)
        SkinTintOffsetsLayout.TabIndex = 0
        ' 
        ' LabelSkinTintR
        ' 
        LabelSkinTintR.Anchor = AnchorStyles.Left
        LabelSkinTintR.AutoSize = True
        LabelSkinTintR.Location = New Point(6, 14)
        LabelSkinTintR.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintR.Name = "LabelSkinTintR"
        LabelSkinTintR.Size = New Size(14, 15)
        LabelSkinTintR.TabIndex = 0
        LabelSkinTintR.Text = "R"
        LabelSkinTintR.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintR
        ' 
        SliderSkinTintR.AccentColor = SystemColors.HotTrack
        SliderSkinTintR.BackColor = SystemColors.Control
        SliderSkinTintR.DisplayFormat = "0"
        SliderSkinTintR.Dock = DockStyle.Fill
        SliderSkinTintR.Location = New Point(68, 6)
        SliderSkinTintR.Margin = New Padding(2)
        SliderSkinTintR.Maximum = 255R
        SliderSkinTintR.Minimum = -255R
        SliderSkinTintR.MinimumSize = New Size(120, 22)
        SliderSkinTintR.Name = "SliderSkinTintR"
        SliderSkinTintR.Size = New Size(734, 28)
        SliderSkinTintR.TabIndex = 1
        SliderSkinTintR.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintR.ThumbColor = SystemColors.HotTrack
        SliderSkinTintR.ThumbRadius = 4F
        SliderSkinTintR.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSkinTintG
        ' 
        LabelSkinTintG.Anchor = AnchorStyles.Left
        LabelSkinTintG.AutoSize = True
        LabelSkinTintG.Location = New Point(6, 46)
        LabelSkinTintG.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintG.Name = "LabelSkinTintG"
        LabelSkinTintG.Size = New Size(15, 15)
        LabelSkinTintG.TabIndex = 2
        LabelSkinTintG.Text = "G"
        LabelSkinTintG.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintG
        ' 
        SliderSkinTintG.AccentColor = SystemColors.HotTrack
        SliderSkinTintG.BackColor = SystemColors.Control
        SliderSkinTintG.DisplayFormat = "0"
        SliderSkinTintG.Dock = DockStyle.Fill
        SliderSkinTintG.Location = New Point(68, 38)
        SliderSkinTintG.Margin = New Padding(2)
        SliderSkinTintG.Maximum = 255R
        SliderSkinTintG.Minimum = -255R
        SliderSkinTintG.MinimumSize = New Size(120, 22)
        SliderSkinTintG.Name = "SliderSkinTintG"
        SliderSkinTintG.Size = New Size(734, 28)
        SliderSkinTintG.TabIndex = 3
        SliderSkinTintG.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintG.ThumbColor = SystemColors.HotTrack
        SliderSkinTintG.ThumbRadius = 4F
        SliderSkinTintG.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSkinTintB
        ' 
        LabelSkinTintB.Anchor = AnchorStyles.Left
        LabelSkinTintB.AutoSize = True
        LabelSkinTintB.Location = New Point(6, 78)
        LabelSkinTintB.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintB.Name = "LabelSkinTintB"
        LabelSkinTintB.Size = New Size(14, 15)
        LabelSkinTintB.TabIndex = 4
        LabelSkinTintB.Text = "B"
        LabelSkinTintB.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintB
        ' 
        SliderSkinTintB.AccentColor = SystemColors.HotTrack
        SliderSkinTintB.BackColor = SystemColors.Control
        SliderSkinTintB.DisplayFormat = "0"
        SliderSkinTintB.Dock = DockStyle.Fill
        SliderSkinTintB.Location = New Point(68, 70)
        SliderSkinTintB.Margin = New Padding(2)
        SliderSkinTintB.Maximum = 255R
        SliderSkinTintB.Minimum = -255R
        SliderSkinTintB.MinimumSize = New Size(120, 22)
        SliderSkinTintB.Name = "SliderSkinTintB"
        SliderSkinTintB.Size = New Size(734, 28)
        SliderSkinTintB.TabIndex = 5
        SliderSkinTintB.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintB.ThumbColor = SystemColors.HotTrack
        SliderSkinTintB.ThumbRadius = 4F
        SliderSkinTintB.TrackColor = SystemColors.ControlDark
        ' 
        ' LabelSkinTintIntensity
        ' 
        LabelSkinTintIntensity.Anchor = AnchorStyles.Left
        LabelSkinTintIntensity.AutoSize = True
        LabelSkinTintIntensity.Location = New Point(6, 110)
        LabelSkinTintIntensity.Margin = New Padding(2, 6, 8, 2)
        LabelSkinTintIntensity.Name = "LabelSkinTintIntensity"
        LabelSkinTintIntensity.Size = New Size(52, 15)
        LabelSkinTintIntensity.TabIndex = 6
        LabelSkinTintIntensity.Text = "Intensity"
        LabelSkinTintIntensity.TextAlign = ContentAlignment.MiddleLeft
        ' 
        ' SliderSkinTintIntensity
        ' 
        SliderSkinTintIntensity.AccentColor = SystemColors.HotTrack
        SliderSkinTintIntensity.BackColor = SystemColors.Control
        SliderSkinTintIntensity.DisplayFormat = "0\%"
        SliderSkinTintIntensity.Dock = DockStyle.Fill
        SliderSkinTintIntensity.Location = New Point(68, 102)
        SliderSkinTintIntensity.Margin = New Padding(2)
        SliderSkinTintIntensity.Minimum = -100R
        SliderSkinTintIntensity.MinimumSize = New Size(120, 22)
        SliderSkinTintIntensity.Name = "SliderSkinTintIntensity"
        SliderSkinTintIntensity.Size = New Size(734, 28)
        SliderSkinTintIntensity.TabIndex = 7
        SliderSkinTintIntensity.TextBoxTextAlign = HorizontalAlignment.Right
        SliderSkinTintIntensity.ThumbColor = SystemColors.HotTrack
        SliderSkinTintIntensity.ThumbRadius = 4F
        SliderSkinTintIntensity.TrackColor = SystemColors.ControlDark
        ' 
        ' ButtonSkinTintReset
        ' 
        ButtonSkinTintReset.Location = New Point(68, 138)
        ButtonSkinTintReset.Margin = New Padding(2, 6, 2, 2)
        ButtonSkinTintReset.Name = "ButtonSkinTintReset"
        ButtonSkinTintReset.Size = New Size(170, 25)
        ButtonSkinTintReset.TabIndex = 8
        ButtonSkinTintReset.Text = "Reset to 0, 0, 0, 0"
        ButtonSkinTintReset.UseVisualStyleBackColor = True
        ' 
        ' LabelSkinTintStatus
        ' 
        LabelSkinTintStatus.AutoSize = True
        LabelSkinTintStatus.Location = New Point(2, 468)
        LabelSkinTintStatus.Margin = New Padding(2)
        LabelSkinTintStatus.Name = "LabelSkinTintStatus"
        LabelSkinTintStatus.Size = New Size(0, 15)
        LabelSkinTintStatus.TabIndex = 4
        ' 
        ' LabelSkinTintGate
        ' 
        LabelSkinTintGate.AutoSize = True
        LabelSkinTintGate.ForeColor = Color.Firebrick
        LabelSkinTintGate.Location = New Point(2, 487)
        LabelSkinTintGate.Margin = New Padding(2)
        LabelSkinTintGate.Name = "LabelSkinTintGate"
        LabelSkinTintGate.Size = New Size(0, 15)
        LabelSkinTintGate.TabIndex = 5
        ' 
        ' SkinTintPanel
        ' 
        Controls.Add(SkinTintTabLayout)
        Name = "SkinTintPanel"
        Size = New Size(818, 680)
        SkinTintTabLayout.ResumeLayout(False)
        SkinTintTabLayout.PerformLayout()
        GroupBoxSkinTintMatch.ResumeLayout(False)
        GroupBoxSkinTintMatch.PerformLayout()
        SkinTintMatchLayout.ResumeLayout(False)
        SkinTintMatchLayout.PerformLayout()
        GroupBoxSkinTintOffsets.ResumeLayout(False)
        GroupBoxSkinTintOffsets.PerformLayout()
        SkinTintOffsetsLayout.ResumeLayout(False)
        SkinTintOffsetsLayout.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents SkinTintTabLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSkinTintLegend As System.Windows.Forms.Label
    Friend WithEvents LabelSkinTintIntensityMeaning As System.Windows.Forms.Label
    Friend WithEvents GroupBoxSkinTintMatch As System.Windows.Forms.GroupBox
    Friend WithEvents SkinTintMatchLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ButtonSkinTintPickSource As System.Windows.Forms.Button
    Friend WithEvents PanelSkinTintSourceSwatch As System.Windows.Forms.Panel
    Friend WithEvents LabelSkinTintSource As System.Windows.Forms.Label
    Friend WithEvents ButtonSkinTintPickTarget As System.Windows.Forms.Button
    Friend WithEvents PanelSkinTintTargetSwatch As System.Windows.Forms.Panel
    Friend WithEvents LabelSkinTintTarget As System.Windows.Forms.Label
    Friend WithEvents ButtonSkinTintAuto As System.Windows.Forms.Button
    Friend WithEvents LabelSkinTintAutoHint As System.Windows.Forms.Label
    Friend WithEvents LabelSkinTintQuality As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintQuality As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSkinTintSampleSize As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintSampleSize As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents GroupBoxSkinTintOffsets As System.Windows.Forms.GroupBox
    Friend WithEvents SkinTintOffsetsLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSkinTintR As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintR As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSkinTintG As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintG As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSkinTintB As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintB As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents LabelSkinTintIntensity As System.Windows.Forms.Label
    Friend WithEvents SliderSkinTintIntensity As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents ButtonSkinTintReset As System.Windows.Forms.Button
    Friend WithEvents LabelSkinTintStatus As System.Windows.Forms.Label
    Friend WithEvents LabelSkinTintGate As System.Windows.Forms.Label
End Class
