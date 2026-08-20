' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PaintListPicker_Form
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
        TextBoxFilter = New TextBox()
        ListBoxEntries = New ListBox()
        LabelPath = New Label()
        FlowButtons = New FlowLayoutPanel()
        ButtonCancel = New Button()
        ButtonOk = New Button()
        RootLayout.SuspendLayout()
        FlowButtons.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        RootLayout.Controls.Add(TextBoxFilter, 0, 0)
        RootLayout.Controls.Add(ListBoxEntries, 0, 1)
        RootLayout.Controls.Add(LabelPath, 0, 2)
        RootLayout.Controls.Add(FlowButtons, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle())                          ' filtro
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))  ' lista
        RootLayout.RowStyles.Add(New RowStyle())                          ' path de la entrada elegida
        RootLayout.RowStyles.Add(New RowStyle())                          ' botones
        RootLayout.Size = New Size(460, 460)
        RootLayout.TabIndex = 0
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Dock = DockStyle.Fill
        TextBoxFilter.Location = New Point(8, 8)
        TextBoxFilter.Margin = New Padding(0, 0, 0, 6)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter…"
        TextBoxFilter.Size = New Size(444, 23)
        TextBoxFilter.TabIndex = 0
        '
        ' ListBoxEntries
        '
        ListBoxEntries.Dock = DockStyle.Fill
        ListBoxEntries.IntegralHeight = False
        ListBoxEntries.Location = New Point(8, 37)
        ListBoxEntries.Margin = New Padding(0)
        ListBoxEntries.Name = "ListBoxEntries"
        ListBoxEntries.Size = New Size(444, 335)
        ListBoxEntries.TabIndex = 1
        '
        ' LabelPath
        '
        LabelPath.AutoEllipsis = True
        LabelPath.Dock = DockStyle.Fill
        LabelPath.ForeColor = SystemColors.GrayText
        LabelPath.Location = New Point(8, 376)
        LabelPath.Margin = New Padding(0, 4, 0, 4)
        LabelPath.Name = "LabelPath"
        LabelPath.Size = New Size(444, 32)
        LabelPath.TabIndex = 2
        '
        ' FlowButtons
        '
        ' Con FlowDirection = RightToLeft el PRIMERO agregado queda a la derecha: Cancel a la derecha y
        ' OK a su izquierda, igual que cuando esto se armaba por codigo.
        FlowButtons.Controls.Add(ButtonCancel)
        FlowButtons.Controls.Add(ButtonOk)
        FlowButtons.AutoSize = True
        FlowButtons.Dock = DockStyle.Fill
        FlowButtons.FlowDirection = FlowDirection.RightToLeft
        FlowButtons.Location = New Point(8, 412)
        FlowButtons.Margin = New Padding(0)
        FlowButtons.Name = "FlowButtons"
        FlowButtons.Size = New Size(444, 40)
        FlowButtons.TabIndex = 3
        '
        ' ButtonCancel
        '
        ButtonCancel.AutoSize = True
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(366, 3)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 25)
        ButtonCancel.TabIndex = 0
        ButtonCancel.Text = "Cancel"
        '
        ' ButtonOk
        '
        ' Arranca deshabilitado a proposito: sin fila seleccionada no hay nada que devolver.
        ButtonOk.AutoSize = True
        ButtonOk.Enabled = False
        ButtonOk.Location = New Point(285, 3)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(75, 25)
        ButtonOk.TabIndex = 1
        ButtonOk.Text = "OK"
        '
        ' PaintListPicker_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(460, 460)
        Controls.Add(RootLayout)
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        MaximizeBox = False
        MinimizeBox = False
        MinimumSize = New Size(360, 320)
        Name = "PaintListPicker_Form"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        RootLayout.ResumeLayout(False)
        FlowButtons.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents TextBoxFilter As TextBox
    Friend WithEvents ListBoxEntries As ListBox
    Friend WithEvents LabelPath As Label
    Friend WithEvents FlowButtons As FlowLayoutPanel
    Friend WithEvents ButtonCancel As Button
    Friend WithEvents ButtonOk As Button
End Class
