' UI built in Designer per feedback_ui_in_designer.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE NPC_PerkEntry (PERK FormID + u8 Rank) — mirror of NpcFactionEntryEditor_Form.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcPerkEntryEditor_Form
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
        LabelPerk = New Label()
        TextBoxPerk = New TextBox()
        ButtonPickPerk = New Button()
        LabelRank = New Label()
        NumRank = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumRank, ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        RootLayout.Controls.Add(LabelPerk, 0, 0)
        RootLayout.Controls.Add(TextBoxPerk, 1, 0)
        RootLayout.Controls.Add(ButtonPickPerk, 2, 0)
        RootLayout.Controls.Add(LabelRank, 0, 1)
        RootLayout.Controls.Add(NumRank, 1, 1)
        RootLayout.Controls.Add(BottomLayout, 1, 2)
        RootLayout.SetColumnSpan(BottomLayout, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 130)
        RootLayout.TabIndex = 0
        '
        ' LabelPerk
        '
        LabelPerk.Anchor = AnchorStyles.Left
        LabelPerk.AutoSize = True
        LabelPerk.Location = New Point(13, 18)
        LabelPerk.Name = "LabelPerk"
        LabelPerk.Size = New Size(90, 15)
        LabelPerk.TabIndex = 0
        LabelPerk.Text = "Perk (PRKR):"
        '
        ' TextBoxPerk
        '
        TextBoxPerk.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxPerk.Location = New Point(153, 14)
        TextBoxPerk.Name = "TextBoxPerk"
        TextBoxPerk.ReadOnly = True
        TextBoxPerk.Size = New Size(314, 23)
        TextBoxPerk.TabIndex = 1
        '
        ' ButtonPickPerk
        '
        ButtonPickPerk.Anchor = AnchorStyles.Left
        ButtonPickPerk.Location = New Point(473, 13)
        ButtonPickPerk.Name = "ButtonPickPerk"
        ButtonPickPerk.Size = New Size(34, 24)
        ButtonPickPerk.TabIndex = 2
        ButtonPickPerk.Text = "…"
        ButtonPickPerk.UseVisualStyleBackColor = True
        '
        ' LabelRank
        '
        LabelRank.Anchor = AnchorStyles.Left
        LabelRank.AutoSize = True
        LabelRank.Location = New Point(13, 49)
        LabelRank.Name = "LabelRank"
        LabelRank.Size = New Size(40, 15)
        LabelRank.TabIndex = 3
        LabelRank.Text = "Rank:"
        '
        ' NumRank
        '
        NumRank.Anchor = AnchorStyles.Left
        NumRank.Location = New Point(153, 45)
        NumRank.Maximum = New Decimal(255)
        NumRank.Name = "NumRank"
        NumRank.Size = New Size(130, 23)
        NumRank.TabIndex = 4
        NumRank.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(153, 102)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(354, 25)
        BottomLayout.TabIndex = 5
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(271, 0)
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
        ButtonCancel.Location = New Point(185, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' NpcPerkEntryEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 130)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "NpcPerkEntryEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Perk (PRKR)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumRank, ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelPerk As System.Windows.Forms.Label
    Friend WithEvents TextBoxPerk As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickPerk As System.Windows.Forms.Button
    Friend WithEvents LabelRank As System.Windows.Forms.Label
    Friend WithEvents NumRank As System.Windows.Forms.NumericUpDown
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
