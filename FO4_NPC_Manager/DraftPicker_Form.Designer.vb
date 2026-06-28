' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class DraftPicker_Form
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
        ListViewItems = New ListView()
        ColItemName = New ColumnHeader()
        ColItemFormID = New ColumnHeader()
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
        RootLayout.Controls.Add(ListViewItems, 0, 0)
        RootLayout.Controls.Add(BottomLayout, 0, 1)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 2
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.Size = New Size(540, 461)
        RootLayout.TabIndex = 0
        '
        ' ListViewItems
        '
        ListViewItems.Columns.AddRange(New ColumnHeader() {ColItemName, ColItemFormID})
        ListViewItems.Dock = DockStyle.Fill
        ListViewItems.FullRowSelect = True
        ListViewItems.Location = New Point(11, 11)
        ListViewItems.MultiSelect = False
        ListViewItems.Name = "ListViewItems"
        ListViewItems.Size = New Size(518, 402)
        ListViewItems.TabIndex = 0
        ListViewItems.UseCompatibleStateImageBehavior = False
        ListViewItems.View = View.Details
        '
        ' ColItemName
        '
        ColItemName.Text = "Draft"
        ColItemName.Width = 410
        '
        ' ColItemFormID
        '
        ColItemFormID.Text = "FormID"
        ColItemFormID.Width = 90
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 419)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(518, 35)
        BottomLayout.TabIndex = 1
        '
        ' ButtonOk
        '
        ButtonOk.DialogResult = DialogResult.OK
        ButtonOk.Location = New Point(435, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(349, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        '
        ' DraftPicker_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(540, 461)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "DraftPicker_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Pick draft"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents ListViewItems As System.Windows.Forms.ListView
    Friend WithEvents ColItemName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColItemFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
