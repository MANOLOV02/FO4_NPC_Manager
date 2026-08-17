' UI built in Designer per 00-reglas-ui-y-vb. The category checkboxes themselves live in the shared
' PresetCategoryPanel (same control the LooksMenu/RaceMenu loader hosts) — this dialog is just its frame.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class PasteOptionsDialog
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
        Root = New TableLayoutPanel()
        LabelHeader = New Label()
        CategoryPanel = New PresetCategoryPanel()
        ButtonRow = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        Root.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        Root.ColumnCount = 1
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        Root.Controls.Add(LabelHeader, 0, 0)
        Root.Controls.Add(CategoryPanel, 0, 1)
        Root.Controls.Add(ButtonRow, 0, 2)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(12)
        Root.RowCount = 3
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 44F))
        Root.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 40F))
        Root.Size = New Size(500, 600)
        Root.TabIndex = 0
        '
        ' LabelHeader
        '
        LabelHeader.Dock = DockStyle.Fill
        LabelHeader.Location = New Point(12, 12)
        LabelHeader.Margin = New Padding(0, 0, 0, 8)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(476, 36)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Choose which parts of the copied look to paste. Unchecked categories keep the target NPC's current values. The number on the right is what the copied look carries."
        LabelHeader.TextAlign = ContentAlignment.MiddleLeft
        '
        ' CategoryPanel
        '
        CategoryPanel.Dock = DockStyle.Fill
        CategoryPanel.Location = New Point(0, 44)
        CategoryPanel.Margin = New Padding(0)
        CategoryPanel.Name = "CategoryPanel"
        CategoryPanel.Size = New Size(476, 504)
        CategoryPanel.TabIndex = 1
        '
        ' ButtonRow
        '
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Dock = DockStyle.Fill
        ButtonRow.FlowDirection = FlowDirection.RightToLeft
        ButtonRow.Location = New Point(12, 548)
        ButtonRow.Margin = New Padding(0)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New Padding(0, 6, 0, 0)
        ButtonRow.Size = New Size(476, 40)
        ButtonRow.TabIndex = 2
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(393, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(307, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' PasteOptionsDialog
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(500, 600)
        Controls.Add(Root)
        ' Fixed frame: this is a checklist with a fixed number of rows, so resizing can only add dead space
        ' under the buttons or squeeze the panel's own Select all row over the group frame. The exact height
        ' is set at Load from the panel (it depends on which categories the running game shows) — hence no
        ' MinimumSize either: it would clamp that computed height.
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "PasteOptionsDialog"
        StartPosition = FormStartPosition.CenterParent
        Text = "Paste Look — choose categories"
        Root.ResumeLayout(False)
        Root.PerformLayout()
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents CategoryPanel As PresetCategoryPanel
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
