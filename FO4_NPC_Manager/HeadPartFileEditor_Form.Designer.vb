' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY: el combo de
' tipos de parte se puebla en code-behind, porque su contenido sale del enum DEL JUEGO de la sesión.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class HeadPartFileEditor_Form
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
        LabelPartType = New Label()
        ComboPartType = New ComboBox()
        LabelPartTypeRaw = New Label()
        LabelFile = New Label()
        TextBoxFile = New TextBox()
        ButtonBrowse = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle())
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle())
        RootLayout.Controls.Add(LabelHint, 0, 0)
        RootLayout.SetColumnSpan(LabelHint, 3)
        RootLayout.Controls.Add(LabelPartType, 0, 1)
        RootLayout.Controls.Add(ComboPartType, 1, 1)
        RootLayout.Controls.Add(LabelPartTypeRaw, 2, 1)
        RootLayout.Controls.Add(LabelFile, 0, 2)
        RootLayout.Controls.Add(TextBoxFile, 1, 2)
        RootLayout.Controls.Add(ButtonBrowse, 2, 2)
        RootLayout.Controls.Add(BottomLayout, 1, 3)
        RootLayout.SetColumnSpan(BottomLayout, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.TabIndex = 0
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.MaximumSize = New Size(560, 0)
        LabelHint.Name = "LabelHint"
        LabelHint.TabIndex = 0
        '
        LabelPartType.Anchor = AnchorStyles.Left
        LabelPartType.AutoSize = True
        LabelPartType.Name = "LabelPartType"
        LabelPartType.TabIndex = 1
        LabelPartType.Text = "Part type (NAM0):"
        '
        ' DropDown y no DropDownList, por lo mismo que el PNAM del editor: un valor que el enum no
        ' nombra se muestra crudo y se conserva.
        ' Lista cerrada + entrada extra, igual que el PNAM y el operador de una condicion: medido,
        ' CERO NAM0 fuera del enum en los dos corpus, asi que tipear a mano solo habilita basura.
        ComboPartType.DropDownStyle = ComboBoxStyle.DropDownList
        ComboPartType.Dock = DockStyle.Fill
        ComboPartType.Name = "ComboPartType"
        ComboPartType.TabIndex = 2
        '
        LabelPartTypeRaw.Anchor = AnchorStyles.Left
        LabelPartTypeRaw.AutoSize = True
        LabelPartTypeRaw.ForeColor = SystemColors.GrayText
        LabelPartTypeRaw.Name = "LabelPartTypeRaw"
        LabelPartTypeRaw.TabIndex = 3
        '
        LabelFile.Anchor = AnchorStyles.Left
        LabelFile.AutoSize = True
        LabelFile.Name = "LabelFile"
        LabelFile.TabIndex = 4
        LabelFile.Text = "File (NAM1):"
        '
        TextBoxFile.Dock = DockStyle.Fill
        TextBoxFile.Name = "TextBoxFile"
        TextBoxFile.TabIndex = 5
        '
        ButtonBrowse.AutoSize = True
        ButtonBrowse.Name = "ButtonBrowse"
        ButtonBrowse.TabIndex = 6
        ButtonBrowse.Text = "Browse…"
        '
        BottomLayout.AutoSize = True
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 7
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
        ClientSize = New Size(600, 190)
        Controls.Add(RootLayout)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "HeadPartFileEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Head part file (NAM0 / NAM1)"
        RootLayout.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents LabelHint As Label
    Friend WithEvents LabelPartType As Label
    Friend WithEvents ComboPartType As ComboBox
    Friend WithEvents LabelPartTypeRaw As Label
    Friend WithEvents LabelFile As Label
    Friend WithEvents TextBoxFile As TextBox
    Friend WithEvents ButtonBrowse As Button
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
