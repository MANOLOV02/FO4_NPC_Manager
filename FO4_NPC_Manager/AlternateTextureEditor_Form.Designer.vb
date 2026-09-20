' UI built in Designer per 00-reglas-ui-y-vb.md. Modal chico: tres campos y OK/Cancel.
' TODA LA UI EN INGLES, como el resto de la app.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class AlternateTextureEditor_Form
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
        LabelHint = New Label()
        LabelName3D = New Label()
        TextBoxName3D = New TextBox()
        LabelTxst = New Label()
        TextBoxTxst = New TextBox()
        TxstButtons = New FlowLayoutPanel()
        ButtonPickTxst = New Button()
        ButtonClearTxst = New Button()
        LabelIndex3D = New Label()
        NumericIndex3D = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        RootLayout.Controls.Add(LabelHint, 0, 0)
        RootLayout.SetColumnSpan(LabelHint, 3)
        RootLayout.Controls.Add(LabelName3D, 0, 1)
        RootLayout.Controls.Add(TextBoxName3D, 1, 1)
        RootLayout.Controls.Add(LabelTxst, 0, 2)
        RootLayout.Controls.Add(TextBoxTxst, 1, 2)
        RootLayout.Controls.Add(TxstButtons, 2, 2)
        RootLayout.Controls.Add(LabelIndex3D, 0, 3)
        RootLayout.Controls.Add(NumericIndex3D, 1, 3)
        RootLayout.Controls.Add(BottomLayout, 0, 4)
        RootLayout.SetColumnSpan(BottomLayout, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 5
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.TabIndex = 0
        '
        LabelHint.AutoSize = True
        LabelHint.Dock = DockStyle.Fill
        LabelHint.ForeColor = SystemColors.GrayText
        LabelHint.Margin = New Padding(3, 0, 3, 8)
        LabelHint.Name = "LabelHint"
        LabelHint.TabIndex = 0
        LabelHint.Text = "Skyrim's MODS on a head part is an array of alternate textures, not a material swap: " &
                         "each entry replaces the texture set of ONE named shape inside the NIF."
        '
        LabelName3D.Anchor = AnchorStyles.Left
        LabelName3D.AutoSize = True
        LabelName3D.Name = "LabelName3D"
        LabelName3D.TabIndex = 1
        LabelName3D.Text = "3D name:"
        '
        TextBoxName3D.Dock = DockStyle.Fill
        TextBoxName3D.Name = "TextBoxName3D"
        TextBoxName3D.PlaceholderText = "the shape's name inside the NIF"
        TextBoxName3D.TabIndex = 2
        '
        LabelTxst.Anchor = AnchorStyles.Left
        LabelTxst.AutoSize = True
        LabelTxst.Name = "LabelTxst"
        LabelTxst.TabIndex = 3
        LabelTxst.Text = "New texture (TXST):"
        '
        TextBoxTxst.Dock = DockStyle.Fill
        TextBoxTxst.Name = "TextBoxTxst"
        TextBoxTxst.ReadOnly = True
        TextBoxTxst.TabIndex = 4
        '
        TxstButtons.AutoSize = True
        TxstButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TxstButtons.Controls.Add(ButtonPickTxst)
        TxstButtons.Controls.Add(ButtonClearTxst)
        TxstButtons.Margin = New Padding(0)
        TxstButtons.Name = "TxstButtons"
        TxstButtons.TabIndex = 5
        TxstButtons.WrapContents = False
        '
        ButtonPickTxst.AutoSize = True
        ButtonPickTxst.Name = "ButtonPickTxst"
        ButtonPickTxst.TabIndex = 0
        ButtonPickTxst.Text = "…"
        '
        ButtonClearTxst.AutoSize = True
        ButtonClearTxst.Name = "ButtonClearTxst"
        ButtonClearTxst.TabIndex = 1
        ButtonClearTxst.Text = "Clear"
        '
        LabelIndex3D.Anchor = AnchorStyles.Left
        LabelIndex3D.AutoSize = True
        LabelIndex3D.Name = "LabelIndex3D"
        LabelIndex3D.TabIndex = 6
        LabelIndex3D.Text = "3D index:"
        '
        NumericIndex3D.Anchor = AnchorStyles.Left
        NumericIndex3D.Name = "NumericIndex3D"
        NumericIndex3D.Size = New Size(110, 23)
        NumericIndex3D.TabIndex = 7
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Margin = New Padding(0, 8, 0, 0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 8
        BottomLayout.WrapContents = False
        '
        ButtonOk.AutoSize = True
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Name = "ButtonOk"
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        '
        ButtonCancel.AutoSize = True
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(620, 230)
        Controls.Add(RootLayout)
        MaximizeBox = False
        MinimizeBox = False
        MinimumSize = New Size(520, 230)
        Name = "AlternateTextureEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Alternate texture"
        RootLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents LabelHint As Label
    Friend WithEvents LabelName3D As Label
    Friend WithEvents TextBoxName3D As TextBox
    Friend WithEvents LabelTxst As Label
    Friend WithEvents TextBoxTxst As TextBox
    Friend WithEvents TxstButtons As FlowLayoutPanel
    Friend WithEvents ButtonPickTxst As Button
    Friend WithEvents ButtonClearTxst As Button
    Friend WithEvents LabelIndex3D As Label
    Friend WithEvents NumericIndex3D As NumericUpDown
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
