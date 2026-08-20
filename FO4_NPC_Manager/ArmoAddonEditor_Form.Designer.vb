' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE ARMO_AddonEntry (INDX + ARMA reference) of an ARMO — replaces the old
' inline-editable INDX cell in GridAddons so the addons grid can be pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ArmoAddonEditor_Form
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
        LabelIndex = New Label()
        NumIndex = New NumericUpDown()
        LabelArma = New Label()
        ArmaPanel = New Panel()
        LabelArmaValue = New Label()
        ButtonPickArma = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        ButtonEditArma = New Button()
        RootLayout.SuspendLayout()
        CType(NumIndex, ComponentModel.ISupportInitialize).BeginInit()
        ArmaPanel.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 2
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelIndex, 0, 0)
        RootLayout.Controls.Add(NumIndex, 1, 0)
        RootLayout.Controls.Add(LabelArma, 0, 1)
        RootLayout.Controls.Add(ArmaPanel, 1, 1)
        RootLayout.Controls.Add(BottomLayout, 1, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 150)
        RootLayout.TabIndex = 0
        '
        ' LabelIndex
        '
        LabelIndex.Anchor = AnchorStyles.Left
        LabelIndex.AutoSize = True
        LabelIndex.Location = New Point(13, 18)
        LabelIndex.Name = "LabelIndex"
        LabelIndex.Size = New Size(150, 15)
        LabelIndex.TabIndex = 0
        LabelIndex.Text = "Addon Index (INDX 0-65535):"
        '
        ' NumIndex
        '
        NumIndex.Anchor = AnchorStyles.Left
        NumIndex.Location = New Point(185, 14)
        NumIndex.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumIndex.Name = "NumIndex"
        NumIndex.Size = New Size(110, 23)
        NumIndex.TabIndex = 1
        NumIndex.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelArma
        '
        LabelArma.Anchor = AnchorStyles.Left
        LabelArma.AutoSize = True
        LabelArma.Location = New Point(13, 49)
        LabelArma.Name = "LabelArma"
        LabelArma.Size = New Size(140, 15)
        LabelArma.TabIndex = 2
        LabelArma.Text = "Armor Addon (ARMA):"
        '
        ' ArmaPanel
        '
        ArmaPanel.Controls.Add(LabelArmaValue)
        ArmaPanel.Controls.Add(ButtonPickArma)
        ArmaPanel.Dock = DockStyle.Fill
        ArmaPanel.Location = New Point(185, 41)
        ArmaPanel.Margin = New Padding(0)
        ArmaPanel.Name = "ArmaPanel"
        ArmaPanel.Size = New Size(325, 30)
        ArmaPanel.TabIndex = 3
        '
        ' LabelArmaValue
        '
        LabelArmaValue.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        LabelArmaValue.AutoEllipsis = True
        LabelArmaValue.BorderStyle = BorderStyle.FixedSingle
        LabelArmaValue.Location = New Point(3, 4)
        LabelArmaValue.Name = "LabelArmaValue"
        LabelArmaValue.Size = New Size(225, 21)
        LabelArmaValue.TabIndex = 0
        LabelArmaValue.Text = "(none)"
        LabelArmaValue.TextAlign = ContentAlignment.MiddleLeft
        '
        ' ButtonPickArma
        '
        ButtonPickArma.Anchor = AnchorStyles.Right
        ButtonPickArma.Location = New Point(234, 3)
        ButtonPickArma.Name = "ButtonPickArma"
        ButtonPickArma.Size = New Size(84, 24)
        ButtonPickArma.TabIndex = 1
        ButtonPickArma.Text = "Choose…"
        ButtonPickArma.UseVisualStyleBackColor = True
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonEditArma)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(185, 115)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(325, 25)
        BottomLayout.TabIndex = 4
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(242, 0)
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
        ButtonCancel.Location = New Point(156, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ButtonEditArma
        '
        ButtonEditArma.AutoSize = True
        ButtonEditArma.Enabled = False
        ButtonEditArma.Location = New Point(40, 0)
        ButtonEditArma.Margin = New Padding(3, 0, 3, 0)
        ButtonEditArma.Name = "ButtonEditArma"
        ButtonEditArma.Size = New Size(110, 25)
        ButtonEditArma.TabIndex = 2
        ButtonEditArma.Text = "Edit ARMA…"
        ButtonEditArma.UseVisualStyleBackColor = True
        '
        ' ArmoAddonEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 150)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ArmoAddonEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Armor Addon (ARMA)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumIndex, ComponentModel.ISupportInitialize).EndInit()
        ArmaPanel.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelIndex As System.Windows.Forms.Label
    Friend WithEvents NumIndex As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelArma As System.Windows.Forms.Label
    Friend WithEvents ArmaPanel As System.Windows.Forms.Panel
    Friend WithEvents LabelArmaValue As System.Windows.Forms.Label
    Friend WithEvents ButtonPickArma As System.Windows.Forms.Button
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonEditArma As System.Windows.Forms.Button
End Class
