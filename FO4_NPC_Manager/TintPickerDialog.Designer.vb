' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class TintPickerDialog
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
        Root = New System.Windows.Forms.TableLayoutPanel()
        TextBoxFilter = New System.Windows.Forms.TextBox()
        TintList = New System.Windows.Forms.ListView()
        ColGroup = New System.Windows.Forms.ColumnHeader()
        ColOption = New System.Windows.Forms.ColumnHeader()
        ColSlot = New System.Windows.Forms.ColumnHeader()
        ColType = New System.Windows.Forms.ColumnHeader()
        ColIndex = New System.Windows.Forms.ColumnHeader()
        ButtonRow = New System.Windows.Forms.FlowLayoutPanel()
        ButtonOk = New System.Windows.Forms.Button()
        ButtonCancel = New System.Windows.Forms.Button()
        Root.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        Root.Dock = System.Windows.Forms.DockStyle.Fill
        Root.ColumnCount = 1
        Root.RowCount = 3
        Root.Padding = New System.Windows.Forms.Padding(8)
        Root.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100.0F))
        Root.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.AutoSize))
        Root.Controls.Add(TextBoxFilter, 0, 0)
        Root.Controls.Add(TintList, 0, 1)
        Root.Controls.Add(ButtonRow, 0, 2)
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Dock = System.Windows.Forms.DockStyle.Fill
        TextBoxFilter.Margin = New System.Windows.Forms.Padding(0, 0, 0, 6)
        TextBoxFilter.PlaceholderText = "Filter by group, option name, slot or type…"
        '
        ' TintList
        '
        TintList.Dock = System.Windows.Forms.DockStyle.Fill
        TintList.View = System.Windows.Forms.View.Details
        TintList.FullRowSelect = True
        TintList.MultiSelect = False
        TintList.HideSelection = False
        TintList.GridLines = False
        TintList.Columns.AddRange(New System.Windows.Forms.ColumnHeader() {ColGroup, ColOption, ColSlot, ColType, ColIndex})
        '
        ColGroup.Text = "Group"
        ColGroup.Width = 140
        ColOption.Text = "Option"
        ColOption.Width = 220
        ColSlot.Text = "Slot"
        ColSlot.Width = 50
        ColType.Text = "Type"
        ColType.Width = 90
        ColIndex.Text = "Index"
        ColIndex.Width = 60
        '
        ' ButtonRow
        '
        ButtonRow.Dock = System.Windows.Forms.DockStyle.Fill
        ButtonRow.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink
        ButtonRow.Padding = New System.Windows.Forms.Padding(0, 6, 0, 0)
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Controls.Add(ButtonCancel)
        '
        ' ButtonOk
        '
        ButtonOk.Text = "OK"
        ButtonOk.Width = 80
        ButtonOk.DialogResult = System.Windows.Forms.DialogResult.OK
        '
        ' ButtonCancel
        '
        ButtonCancel.Text = "Cancel"
        ButtonCancel.Width = 80
        ButtonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel
        '
        ' TintPickerDialog
        '
        Text = "Add Face Tint"
        ClientSize = New System.Drawing.Size(560, 480)
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        MinimizeBox = False
        MaximizeBox = False
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        Controls.Add(Root)
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
