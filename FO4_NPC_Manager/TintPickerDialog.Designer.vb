' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class TintPickerDialog
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
        Root = New TableLayoutPanel()
        TextBoxFilter = New TextBox()
        TintList = New ListView()
        ColGroup = New ColumnHeader()
        ColOption = New ColumnHeader()
        ColSlot = New ColumnHeader()
        ColType = New ColumnHeader()
        ColIndex = New ColumnHeader()
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
        Root.Controls.Add(TextBoxFilter, 0, 0)
        Root.Controls.Add(TintList, 0, 1)
        Root.Controls.Add(ButtonRow, 0, 2)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(8)
        Root.RowCount = 3
        Root.RowStyles.Add(New RowStyle())
        Root.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        Root.RowStyles.Add(New RowStyle())
        Root.Size = New Size(610, 480)
        Root.TabIndex = 0
        ' 
        ' TextBoxFilter
        ' 
        TextBoxFilter.Dock = DockStyle.Fill
        TextBoxFilter.Location = New Point(8, 8)
        TextBoxFilter.Margin = New Padding(0, 0, 0, 6)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by group, option name, slot or type…"
        TextBoxFilter.Size = New Size(594, 23)
        TextBoxFilter.TabIndex = 0
        ' 
        ' TintList
        ' 
        TintList.Columns.AddRange(New ColumnHeader() {ColGroup, ColOption, ColSlot, ColType, ColIndex})
        TintList.Dock = DockStyle.Fill
        TintList.FullRowSelect = True
        TintList.Location = New Point(11, 40)
        TintList.MultiSelect = False
        TintList.Name = "TintList"
        TintList.Size = New Size(588, 388)
        TintList.TabIndex = 1
        TintList.UseCompatibleStateImageBehavior = False
        TintList.View = View.Details
        ' 
        ' ColGroup
        ' 
        ColGroup.Text = "Group"
        ColGroup.Width = 140
        ' 
        ' ColOption
        ' 
        ColOption.Text = "Option"
        ColOption.Width = 220
        ' 
        ' ColSlot
        ' 
        ColSlot.Text = "Slot"
        ColSlot.Width = 50
        ' 
        ' ColType
        ' 
        ColType.Text = "Type"
        ColType.Width = 90
        ' 
        ' ColIndex
        ' 
        ColIndex.Text = "Index"
        ' 
        ' ButtonRow
        ' 
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Dock = DockStyle.Fill
        ButtonRow.FlowDirection = FlowDirection.RightToLeft
        ButtonRow.Location = New Point(11, 434)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New Padding(0, 6, 0, 0)
        ButtonRow.Size = New Size(588, 35)
        ButtonRow.TabIndex = 2
        ' 
        ' ButtonOk
        ' 
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(505, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(419, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ' 
        ' TintPickerDialog
        ' 
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(610, 480)
        Controls.Add(Root)
        MaximizeBox = False
        MinimizeBox = False
        Name = "TintPickerDialog"
        StartPosition = FormStartPosition.CenterParent
        Text = "Add Face Tint"
        Root.ResumeLayout(False)
        Root.PerformLayout()
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TextBoxFilter As System.Windows.Forms.TextBox
    Friend WithEvents TintList As System.Windows.Forms.ListView
    Friend WithEvents ColGroup As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColOption As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColSlot As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColType As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColIndex As System.Windows.Forms.ColumnHeader
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
