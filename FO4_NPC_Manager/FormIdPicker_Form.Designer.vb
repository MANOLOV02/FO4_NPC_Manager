' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormIdPicker_Form
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
        LabelHeader = New Label()
        FilterRow = New TableLayoutPanel()
        LabelFilter = New Label()
        TextBoxFilter = New TextBox()
        CheckBoxShowAll = New CheckBox()
        ListViewRecords = New ListView()
        ColumnName = New ColumnHeader()
        ColumnEditorID = New ColumnHeader()
        ColumnFormID = New ColumnHeader()
        ColumnPlugin = New ColumnHeader()
        ColumnSignature = New ColumnHeader()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        ButtonDeleteEntry = New Button()
        RootLayout.SuspendLayout()
        FilterRow.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelHeader, 0, 0)
        RootLayout.Controls.Add(FilterRow, 0, 1)
        RootLayout.Controls.Add(ListViewRecords, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(860, 560)
        RootLayout.TabIndex = 0
        '
        ' LabelHeader
        '
        LabelHeader.AutoSize = True
        LabelHeader.Location = New Point(8, 8)
        LabelHeader.Margin = New Padding(0, 0, 0, 4)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(78, 15)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = "Pick a record"
        '
        ' FilterRow
        '
        FilterRow.AutoSize = True
        FilterRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FilterRow.ColumnCount = 3
        FilterRow.ColumnStyles.Add(New ColumnStyle())
        FilterRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FilterRow.ColumnStyles.Add(New ColumnStyle())
        FilterRow.Controls.Add(LabelFilter, 0, 0)
        FilterRow.Controls.Add(TextBoxFilter, 1, 0)
        FilterRow.Controls.Add(CheckBoxShowAll, 2, 0)
        FilterRow.Dock = DockStyle.Fill
        FilterRow.Location = New Point(8, 30)
        FilterRow.Margin = New Padding(0, 0, 0, 6)
        FilterRow.Name = "FilterRow"
        FilterRow.RowCount = 1
        FilterRow.RowStyles.Add(New RowStyle())
        FilterRow.Size = New Size(844, 29)
        FilterRow.TabIndex = 1
        '
        ' LabelFilter
        '
        LabelFilter.Anchor = AnchorStyles.Left
        LabelFilter.AutoSize = True
        LabelFilter.Location = New Point(0, 7)
        LabelFilter.Margin = New Padding(0, 0, 6, 0)
        LabelFilter.Name = "LabelFilter"
        LabelFilter.Size = New Size(36, 15)
        LabelFilter.TabIndex = 0
        LabelFilter.Text = "Filter:"
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Dock = DockStyle.Fill
        TextBoxFilter.Location = New Point(42, 3)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name, editor ID or FormID..."
        TextBoxFilter.Size = New Size(799, 23)
        TextBoxFilter.TabIndex = 0
        '
        ' CheckBoxShowAll
        '
        CheckBoxShowAll.Anchor = AnchorStyles.Left
        CheckBoxShowAll.AutoSize = True
        CheckBoxShowAll.Margin = New Padding(8, 0, 0, 0)
        CheckBoxShowAll.Name = "CheckBoxShowAll"
        CheckBoxShowAll.TabIndex = 1
        CheckBoxShowAll.Text = "Show all"
        CheckBoxShowAll.Visible = False
        '
        ' ListViewRecords
        '
        ListViewRecords.Columns.AddRange(New ColumnHeader() {ColumnName, ColumnEditorID, ColumnFormID, ColumnPlugin, ColumnSignature})
        ListViewRecords.Dock = DockStyle.Fill
        ListViewRecords.FullRowSelect = True
        ListViewRecords.HideSelection = False
        ListViewRecords.Location = New Point(11, 65)
        ListViewRecords.Margin = New Padding(3, 0, 3, 0)
        ListViewRecords.MultiSelect = False
        ListViewRecords.Name = "ListViewRecords"
        ListViewRecords.Size = New Size(838, 432)
        ListViewRecords.TabIndex = 2
        ListViewRecords.UseCompatibleStateImageBehavior = False
        ListViewRecords.View = View.Details
        '
        ' ColumnName
        '
        ColumnName.Text = "Name"
        ColumnName.Width = 240
        '
        ' ColumnEditorID
        '
        ColumnEditorID.Text = "EditorID"
        ColumnEditorID.Width = 240
        '
        ' ColumnFormID
        '
        ColumnFormID.Text = "FormID"
        ColumnFormID.Width = 100
        '
        ' ColumnPlugin
        '
        ColumnPlugin.Text = "Plugin"
        ColumnPlugin.Width = 160
        '
        ' ColumnSignature
        '
        ColumnSignature.Text = "Signature"
        ColumnSignature.Width = 80
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonDeleteEntry)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 500)
        BottomLayout.Margin = New Padding(3, 3, 3, 0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(838, 41)
        BottomLayout.TabIndex = 3
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(755, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(669, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        '
        ' ButtonDeleteEntry
        '
        ButtonDeleteEntry.AutoSize = True
        ButtonDeleteEntry.Location = New Point(3, 9)
        ButtonDeleteEntry.Margin = New Padding(3, 3, 3, 3)
        ButtonDeleteEntry.Name = "ButtonDeleteEntry"
        ButtonDeleteEntry.Size = New Size(110, 23)
        ButtonDeleteEntry.TabIndex = 2
        ButtonDeleteEntry.Text = "Delete / Revert…"
        ButtonDeleteEntry.Visible = False
        '
        ' FormIdPicker_Form
        '
        AcceptButton = ButtonOk
        CancelButton = ButtonCancel
        ClientSize = New Size(860, 560)
        Controls.Add(RootLayout)
        MinimizeBox = False
        MaximizeBox = False
        MinimumSize = New Size(560, 360)
        Name = "FormIdPicker_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Select record"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        FilterRow.ResumeLayout(False)
        FilterRow.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeader As System.Windows.Forms.Label
    Friend WithEvents FilterRow As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelFilter As System.Windows.Forms.Label
    Friend WithEvents TextBoxFilter As System.Windows.Forms.TextBox
    Friend WithEvents CheckBoxShowAll As System.Windows.Forms.CheckBox
    Friend WithEvents ListViewRecords As System.Windows.Forms.ListView
    Friend WithEvents ColumnName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnEditorID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColumnSignature As System.Windows.Forms.ColumnHeader
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonDeleteEntry As System.Windows.Forms.Button
End Class
