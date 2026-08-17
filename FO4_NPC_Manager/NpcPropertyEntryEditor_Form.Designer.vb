' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE NPC_PropertyEntry (AVIF FormID + f32 Value) — mirror of NpcFactionEntryEditor_Form.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcPropertyEntryEditor_Form
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
        LabelAv = New Label()
        TextBoxAv = New TextBox()
        ButtonPickAv = New Button()
        LabelValue = New Label()
        NumValue = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 160F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        RootLayout.Controls.Add(LabelAv, 0, 0)
        RootLayout.Controls.Add(TextBoxAv, 1, 0)
        RootLayout.Controls.Add(ButtonPickAv, 2, 0)
        RootLayout.Controls.Add(LabelValue, 0, 1)
        RootLayout.Controls.Add(NumValue, 1, 1)
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
        ' LabelAv
        '
        LabelAv.Anchor = AnchorStyles.Left
        LabelAv.AutoSize = True
        LabelAv.Location = New Point(13, 18)
        LabelAv.Name = "LabelAv"
        LabelAv.Size = New Size(120, 15)
        LabelAv.TabIndex = 0
        LabelAv.Text = "Actor Value (PRPS):"
        '
        ' TextBoxAv
        '
        TextBoxAv.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxAv.Location = New Point(173, 14)
        TextBoxAv.Name = "TextBoxAv"
        TextBoxAv.ReadOnly = True
        TextBoxAv.Size = New Size(294, 23)
        TextBoxAv.TabIndex = 1
        '
        ' ButtonPickAv
        '
        ButtonPickAv.Anchor = AnchorStyles.Left
        ButtonPickAv.Location = New Point(473, 13)
        ButtonPickAv.Name = "ButtonPickAv"
        ButtonPickAv.Size = New Size(34, 24)
        ButtonPickAv.TabIndex = 2
        ButtonPickAv.Text = "…"
        ButtonPickAv.UseVisualStyleBackColor = True
        '
        ' LabelValue
        '
        LabelValue.Anchor = AnchorStyles.Left
        LabelValue.AutoSize = True
        LabelValue.Location = New Point(13, 49)
        LabelValue.Name = "LabelValue"
        LabelValue.Size = New Size(40, 15)
        LabelValue.TabIndex = 3
        LabelValue.Text = "Value:"
        '
        ' NumValue
        '
        NumValue.Anchor = AnchorStyles.Left
        NumValue.DecimalPlaces = 4
        NumValue.Increment = New Decimal(New Integer() {1, 0, 0, 65536})
        NumValue.Location = New Point(173, 45)
        NumValue.Maximum = New Decimal(1000000000)
        NumValue.Minimum = New Decimal(New Integer() {1000000000, 0, 0, -2147483648})
        NumValue.Name = "NumValue"
        NumValue.Size = New Size(150, 23)
        NumValue.TabIndex = 4
        NumValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(173, 102)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(334, 25)
        BottomLayout.TabIndex = 5
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(251, 0)
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
        ButtonCancel.Location = New Point(165, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' NpcPropertyEntryEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 130)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "NpcPropertyEntryEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Property (PRPS)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelAv As System.Windows.Forms.Label
    Friend WithEvents TextBoxAv As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickAv As System.Windows.Forms.Button
    Friend WithEvents LabelValue As System.Windows.Forms.Label
    Friend WithEvents NumValue As System.Windows.Forms.NumericUpDown
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
