' UI built in Designer per feedback_ui_in_designer.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class OverridePicker_Form
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
        TypeRow = New FlowLayoutPanel()
        RadioArmo = New RadioButton()
        RadioArma = New RadioButton()
        TextBoxFilter = New TextBox()
        ListViewRecords = New ListView()
        ColRecName = New ColumnHeader()
        ColRecFormID = New ColumnHeader()
        ColRecPlugin = New ColumnHeader()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        TypeRow.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TypeRow, 0, 0)
        RootLayout.Controls.Add(TextBoxFilter, 0, 1)
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
        RootLayout.Size = New Size(684, 521)
        RootLayout.TabIndex = 0
        '
        ' TypeRow
        '
        TypeRow.AutoSize = True
        TypeRow.Controls.Add(RadioArmo)
        TypeRow.Controls.Add(RadioArma)
        TypeRow.Dock = DockStyle.Fill
        TypeRow.Location = New Point(11, 11)
        TypeRow.Margin = New Padding(0)
        TypeRow.Name = "TypeRow"
        TypeRow.Size = New Size(662, 25)
        TypeRow.TabIndex = 0
        TypeRow.WrapContents = False
        '
        ' RadioArmo
        '
        RadioArmo.AutoSize = True
        RadioArmo.Location = New Point(3, 3)
        RadioArmo.Name = "RadioArmo"
        RadioArmo.Size = New Size(110, 19)
        RadioArmo.TabIndex = 0
        RadioArmo.TabStop = True
        RadioArmo.Text = "ARMO (armor)"
        RadioArmo.UseVisualStyleBackColor = True
        '
        ' RadioArma
        '
        RadioArma.AutoSize = True
        RadioArma.Location = New Point(119, 3)
        RadioArma.Name = "RadioArma"
        RadioArma.Size = New Size(140, 19)
        RadioArma.TabIndex = 1
        RadioArma.TabStop = True
        RadioArma.Text = "ARMA (addon)"
        RadioArma.UseVisualStyleBackColor = True
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Dock = DockStyle.Top
        TextBoxFilter.Location = New Point(11, 39)
        TextBoxFilter.Margin = New Padding(0, 0, 0, 6)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name, plugin or FormID..."
        TextBoxFilter.Size = New Size(662, 23)
        TextBoxFilter.TabIndex = 1
        '
        ' ListViewRecords
        '
        ListViewRecords.Columns.AddRange(New ColumnHeader() {ColRecName, ColRecFormID, ColRecPlugin})
        ListViewRecords.Dock = DockStyle.Fill
        ListViewRecords.FullRowSelect = True
        ListViewRecords.Location = New Point(11, 68)
        ListViewRecords.MultiSelect = False
        ListViewRecords.Name = "ListViewRecords"
        ListViewRecords.Size = New Size(662, 405)
        ListViewRecords.TabIndex = 2
        ListViewRecords.UseCompatibleStateImageBehavior = False
        ListViewRecords.View = View.Details
        '
        ' ColRecName
        '
        ColRecName.Text = "Record"
        ColRecName.Width = 400
        '
        ' ColRecFormID
        '
        ColRecFormID.Text = "FormID"
        ColRecFormID.Width = 90
        '
        ' ColRecPlugin
        '
        ColRecPlugin.Text = "Plugin"
        ColRecPlugin.Width = 160
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(11, 479)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(662, 35)
        BottomLayout.TabIndex = 3
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
        ' OverridePicker_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(684, 521)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "OverridePicker_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Override existing"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TypeRow.ResumeLayout(False)
        TypeRow.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents TypeRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents RadioArmo As System.Windows.Forms.RadioButton
    Friend WithEvents RadioArma As System.Windows.Forms.RadioButton
    Friend WithEvents TextBoxFilter As System.Windows.Forms.TextBox
    Friend WithEvents ListViewRecords As System.Windows.Forms.ListView
    Friend WithEvents ColRecName As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColRecFormID As System.Windows.Forms.ColumnHeader
    Friend WithEvents ColRecPlugin As System.Windows.Forms.ColumnHeader
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
