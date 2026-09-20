' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY: los ROTULOS
' de las ocho ranuras de textura se ponen en code-behind, porque se llaman distinto en cada juego
' (TX02 es Wrinkles en Fallout 4 y Environment Mask/Subsurface Tint en Skyrim; idem TX03 y TX07).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class TextureSetEditor_Form
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
        TopRow = New FlowLayoutPanel()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelBanner = New Label()
        GridTextures = New TableLayoutPanel()
        LabelObnd = New Label()
        LabelDodt = New Label()
        LabelMnam = New Label()
        TextBoxMnam = New TextBox()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        TopRow.SuspendLayout()
        GridTextures.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopRow, 0, 0)
        RootLayout.Controls.Add(GridTextures, 0, 1)
        RootLayout.Controls.Add(LabelObnd, 0, 2)
        RootLayout.Controls.Add(LabelDodt, 0, 3)
        RootLayout.Controls.Add(BottomLayout, 0, 4)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 5
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.TabIndex = 0
        '
        TopRow.AutoSize = True
        TopRow.Controls.Add(LabelEdid)
        TopRow.Controls.Add(TextBoxEdid)
        TopRow.Controls.Add(LabelBanner)
        TopRow.Dock = DockStyle.Fill
        TopRow.Name = "TopRow"
        TopRow.TabIndex = 0
        TopRow.WrapContents = False
        '
        LabelEdid.Anchor = AnchorStyles.Left
        LabelEdid.AutoSize = True
        LabelEdid.Margin = New Padding(3, 9, 3, 0)
        LabelEdid.Name = "LabelEdid"
        LabelEdid.TabIndex = 0
        LabelEdid.Text = "EditorID:"
        '
        TextBoxEdid.Margin = New Padding(3, 5, 3, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.Size = New Size(280, 23)
        TextBoxEdid.TabIndex = 1
        '
        LabelBanner.Anchor = AnchorStyles.Left
        LabelBanner.AutoSize = True
        LabelBanner.ForeColor = SystemColors.GrayText
        LabelBanner.Margin = New Padding(12, 9, 3, 0)
        LabelBanner.Name = "LabelBanner"
        LabelBanner.TabIndex = 2
        '
        ' Las ocho filas (rótulo + caja + Browse) las agrega el code-behind con los nombres del juego.
        GridTextures.ColumnCount = 3
        GridTextures.ColumnStyles.Add(New ColumnStyle())
        GridTextures.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GridTextures.ColumnStyles.Add(New ColumnStyle())
        GridTextures.Dock = DockStyle.Fill
        GridTextures.Name = "GridTextures"
        GridTextures.RowCount = 9
        GridTextures.TabIndex = 1
        '
        LabelObnd.AutoSize = True
        LabelObnd.ForeColor = SystemColors.GrayText
        LabelObnd.Name = "LabelObnd"
        LabelObnd.TabIndex = 2
        '
        LabelDodt.AutoSize = True
        LabelDodt.ForeColor = SystemColors.GrayText
        LabelDodt.Name = "LabelDodt"
        LabelDodt.TabIndex = 3
        '
        LabelMnam.Anchor = AnchorStyles.Left
        LabelMnam.AutoSize = True
        LabelMnam.Name = "LabelMnam"
        LabelMnam.TabIndex = 0
        LabelMnam.Text = "Material (MNAM):"
        '
        TextBoxMnam.Dock = DockStyle.Fill
        TextBoxMnam.Name = "TextBoxMnam"
        TextBoxMnam.TabIndex = 1
        '
        BottomLayout.AutoSize = True
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 4
        BottomLayout.WrapContents = False
        '
        ButtonOk.AutoSize = True
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
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(900, 480)
        Controls.Add(RootLayout)
        MinimumSize = New Size(700, 420)
        Name = "TextureSetEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Texture Set Editor"
        RootLayout.ResumeLayout(False)
        TopRow.ResumeLayout(False)
        GridTextures.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents TopRow As FlowLayoutPanel
    Friend WithEvents LabelEdid As Label
    Friend WithEvents TextBoxEdid As TextBox
    Friend WithEvents LabelBanner As Label
    Friend WithEvents GridTextures As TableLayoutPanel
    Friend WithEvents LabelObnd As Label
    Friend WithEvents LabelDodt As Label
    Friend WithEvents LabelMnam As Label
    Friend WithEvents TextBoxMnam As TextBox
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
