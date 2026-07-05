' UI built in Designer per feedback_ui_in_designer.md. InitializeComponent is declarative ONLY. The Original
' ComboBox items (mesh NIF materials) are added in code-behind since they depend on the ctor-supplied list.
' Modal editor for a SINGLE MSWP substitution (Original/Replacement/Color-Remap) — replaces the old
' inline-editable GridSubs (combo Original cell + text cells) so that grid can be pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MswpSubEntryEditor_Form
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
        LabelOriginal = New Label()
        ComboOriginal = New ComboBox()
        LabelReplacement = New Label()
        ReplacementPanel = New Panel()
        TextBoxReplacement = New TextBox()
        ButtonBrowseReplacement = New Button()
        LabelRemap = New Label()
        RemapPanel = New FlowLayoutPanel()
        CheckRemap = New CheckBox()
        SliderRemap = New FO4_Base_Library.TinySliderTextBox()
        PicRemapGradient = New PictureBox()
        LabelHint = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        ReplacementPanel.SuspendLayout()
        RemapPanel.SuspendLayout()
        CType(PicRemapGradient, System.ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 2
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 190F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelOriginal, 0, 0)
        RootLayout.Controls.Add(ComboOriginal, 1, 0)
        RootLayout.Controls.Add(LabelReplacement, 0, 1)
        RootLayout.Controls.Add(ReplacementPanel, 1, 1)
        RootLayout.Controls.Add(LabelRemap, 0, 2)
        RootLayout.Controls.Add(RemapPanel, 1, 2)
        RootLayout.Controls.Add(PicRemapGradient, 1, 3)
        RootLayout.Controls.Add(LabelHint, 1, 4)
        RootLayout.Controls.Add(BottomLayout, 1, 5)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 6
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(640, 255)
        RootLayout.TabIndex = 0
        '
        ' LabelOriginal
        '
        LabelOriginal.Anchor = AnchorStyles.Left
        LabelOriginal.AutoSize = True
        LabelOriginal.Location = New Point(13, 18)
        LabelOriginal.Name = "LabelOriginal"
        LabelOriginal.Size = New Size(155, 15)
        LabelOriginal.TabIndex = 0
        LabelOriginal.Text = "Original Material (BNAM):"
        '
        ' ComboOriginal
        '
        ComboOriginal.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ComboOriginal.DropDownStyle = ComboBoxStyle.DropDown
        ComboOriginal.Location = New Point(203, 14)
        ComboOriginal.Name = "ComboOriginal"
        ComboOriginal.Size = New Size(414, 23)
        ComboOriginal.TabIndex = 1
        '
        ' LabelReplacement
        '
        LabelReplacement.Anchor = AnchorStyles.Left
        LabelReplacement.AutoSize = True
        LabelReplacement.Location = New Point(13, 49)
        LabelReplacement.Name = "LabelReplacement"
        LabelReplacement.Size = New Size(180, 15)
        LabelReplacement.TabIndex = 2
        LabelReplacement.Text = "Replacement Material (SNAM):"
        '
        ' ReplacementPanel
        '
        ReplacementPanel.Controls.Add(TextBoxReplacement)
        ReplacementPanel.Controls.Add(ButtonBrowseReplacement)
        ReplacementPanel.Dock = DockStyle.Fill
        ReplacementPanel.Location = New Point(203, 41)
        ReplacementPanel.Margin = New Padding(0)
        ReplacementPanel.Name = "ReplacementPanel"
        ReplacementPanel.Size = New Size(414, 30)
        ReplacementPanel.TabIndex = 3
        '
        ' TextBoxReplacement
        '
        TextBoxReplacement.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxReplacement.Location = New Point(3, 4)
        TextBoxReplacement.Name = "TextBoxReplacement"
        TextBoxReplacement.Size = New Size(300, 23)
        TextBoxReplacement.TabIndex = 0
        '
        ' ButtonBrowseReplacement
        '
        ButtonBrowseReplacement.Anchor = AnchorStyles.Right
        ButtonBrowseReplacement.Location = New Point(309, 3)
        ButtonBrowseReplacement.Name = "ButtonBrowseReplacement"
        ButtonBrowseReplacement.Size = New Size(100, 24)
        ButtonBrowseReplacement.TabIndex = 1
        ButtonBrowseReplacement.Text = "Browse…"
        ButtonBrowseReplacement.UseVisualStyleBackColor = True
        '
        ' LabelRemap
        '
        LabelRemap.Anchor = AnchorStyles.Left
        LabelRemap.AutoSize = True
        LabelRemap.Location = New Point(13, 80)
        LabelRemap.Name = "LabelRemap"
        LabelRemap.Size = New Size(160, 15)
        LabelRemap.TabIndex = 4
        LabelRemap.Text = "Color Remap (0–1):"
        '
        ' RemapPanel
        '
        RemapPanel.Anchor = AnchorStyles.Left
        RemapPanel.AutoSize = True
        RemapPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RemapPanel.Controls.Add(CheckRemap)
        RemapPanel.Controls.Add(SliderRemap)
        RemapPanel.Location = New Point(203, 74)
        RemapPanel.Margin = New Padding(0)
        RemapPanel.Name = "RemapPanel"
        RemapPanel.Size = New Size(300, 28)
        RemapPanel.TabIndex = 5
        RemapPanel.WrapContents = False
        '
        ' CheckRemap
        '
        CheckRemap.Anchor = AnchorStyles.Left
        CheckRemap.AutoSize = True
        CheckRemap.Margin = New Padding(3, 4, 8, 0)
        CheckRemap.Name = "CheckRemap"
        CheckRemap.Size = New Size(72, 19)
        CheckRemap.TabIndex = 0
        CheckRemap.Text = "Present"
        CheckRemap.UseVisualStyleBackColor = True
        '
        ' SliderRemap
        '
        SliderRemap.DisplayFormat = "0.0000"
        SliderRemap.Location = New Point(83, 0)
        SliderRemap.Margin = New Padding(0)
        SliderRemap.Maximum = 1.0R
        SliderRemap.Minimum = 0.0R
        SliderRemap.Name = "SliderRemap"
        SliderRemap.SmallChange = 0.0001R
        SliderRemap.Size = New Size(200, 28)
        SliderRemap.TabIndex = 1
        SliderRemap.Value = 0.0R
        '
        ' PicRemapGradient
        '
        PicRemapGradient.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        PicRemapGradient.BorderStyle = BorderStyle.FixedSingle
        PicRemapGradient.Location = New Point(203, 105)
        PicRemapGradient.Margin = New Padding(0, 3, 3, 6)
        PicRemapGradient.Name = "PicRemapGradient"
        PicRemapGradient.Size = New Size(414, 28)
        PicRemapGradient.SizeMode = PictureBoxSizeMode.StretchImage
        PicRemapGradient.TabIndex = 2
        PicRemapGradient.TabStop = False
        '
        ' LabelHint
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.Location = New Point(203, 108)
        LabelHint.Margin = New Padding(0, 6, 3, 0)
        LabelHint.MaximumSize = New Size(410, 0)
        LabelHint.Name = "LabelHint"
        LabelHint.Size = New Size(400, 30)
        LabelHint.TabIndex = 6
        LabelHint.Text = "Original = a material referenced by the mesh (pick from the list or type another); Replacement = the swapped-in material."
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(203, 185)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(414, 25)
        BottomLayout.TabIndex = 7
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(331, 0)
        ButtonOk.Margin = New Padding(3, 0, 3, 0)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 25)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(245, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' MswpSubEntryEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(640, 255)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "MswpSubEntryEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Material Substitution"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        ReplacementPanel.ResumeLayout(False)
        ReplacementPanel.PerformLayout()
        RemapPanel.ResumeLayout(False)
        RemapPanel.PerformLayout()
        CType(PicRemapGradient, System.ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelOriginal As System.Windows.Forms.Label
    Friend WithEvents ComboOriginal As System.Windows.Forms.ComboBox
    Friend WithEvents LabelReplacement As System.Windows.Forms.Label
    Friend WithEvents ReplacementPanel As System.Windows.Forms.Panel
    Friend WithEvents TextBoxReplacement As System.Windows.Forms.TextBox
    Friend WithEvents ButtonBrowseReplacement As System.Windows.Forms.Button
    Friend WithEvents LabelRemap As System.Windows.Forms.Label
    Friend WithEvents RemapPanel As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents CheckRemap As System.Windows.Forms.CheckBox
    Friend WithEvents SliderRemap As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents PicRemapGradient As System.Windows.Forms.PictureBox
    Friend WithEvents LabelHint As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
