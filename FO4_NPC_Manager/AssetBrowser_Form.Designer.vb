' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class AssetBrowser_Form
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
        LabelSearch = New Label()
        TextBoxSearch = New TextBox()
        ListViewFiles = New ListView()
        ColumnPath = New ColumnHeader()
        LabelStatus = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelSearch, 0, 0)
        RootLayout.Controls.Add(TextBoxSearch, 0, 1)
        RootLayout.Controls.Add(ListViewFiles, 0, 2)
        RootLayout.Controls.Add(LabelStatus, 0, 3)
        RootLayout.Controls.Add(BottomLayout, 0, 4)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 5
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(684, 561)
        RootLayout.TabIndex = 0
        '
        ' LabelSearch
        '
        LabelSearch.AutoSize = True
        LabelSearch.Location = New Point(11, 8)
        LabelSearch.Margin = New Padding(3, 0, 3, 4)
        LabelSearch.Name = "LabelSearch"
        LabelSearch.Size = New Size(99, 15)
        LabelSearch.TabIndex = 0
        LabelSearch.Text = "Search (or type a path):"
        '
        ' TextBoxSearch
        '
        TextBoxSearch.Dock = DockStyle.Top
        TextBoxSearch.Location = New Point(11, 27)
        TextBoxSearch.Margin = New Padding(3, 0, 3, 6)
        TextBoxSearch.Name = "TextBoxSearch"
        TextBoxSearch.PlaceholderText = "Filter by substring, or type a relative path..."
        TextBoxSearch.Size = New Size(662, 23)
        TextBoxSearch.TabIndex = 1
        '
        ' ListViewFiles
        '
        ListViewFiles.Columns.AddRange(New ColumnHeader() {ColumnPath})
        ListViewFiles.Dock = DockStyle.Fill
        ListViewFiles.FullRowSelect = True
        ListViewFiles.Location = New Point(11, 59)
        ListViewFiles.MultiSelect = False
        ListViewFiles.Name = "ListViewFiles"
        ListViewFiles.Size = New Size(662, 446)
        ListViewFiles.TabIndex = 2
        ListViewFiles.UseCompatibleStateImageBehavior = False
        ListViewFiles.View = View.Details
        '
        ' ColumnPath
        '
        ColumnPath.Text = "Path"
        ColumnPath.Width = 630
        '
        ' LabelStatus
        '
        LabelStatus.AutoSize = True
        LabelStatus.ForeColor = Color.DimGray
        LabelStatus.Location = New Point(11, 508)
        LabelStatus.Margin = New Padding(3, 0, 3, 2)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(0, 15)
        LabelStatus.TabIndex = 3
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 525)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(662, 35)
        BottomLayout.TabIndex = 4
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(579, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(493, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        '
        ' AssetBrowser_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(684, 561)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "AssetBrowser_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Browse assets"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelSearch As System.Windows.Forms.Label
    Friend WithEvents TextBoxSearch As System.Windows.Forms.TextBox
    Friend WithEvents ListViewFiles As System.Windows.Forms.ListView
    Friend WithEvents ColumnPath As System.Windows.Forms.ColumnHeader
    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
