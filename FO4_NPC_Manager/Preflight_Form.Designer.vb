<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class Preflight_Form
    Inherits System.Windows.Forms.Form

    Private components As System.ComponentModel.IContainer

    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Sub InitializeComponent()
        LabelExePath = New Label()
        TextBoxExePath = New TextBox()
        ButtonBrowse = New Button()
        LabelPlugins = New Label()
        ListViewPlugins = New ListView()
        ColumnHeaderPlugin = New ColumnHeader()
        ColumnHeaderState = New ColumnHeader()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        LabelStatus = New Label()
        ProgressBarLoad = New ProgressBar()
        LabelProgress = New Label()
        SuspendLayout()
        '
        ' LabelExePath
        '
        LabelExePath.AutoSize = True
        LabelExePath.Location = New Drawing.Point(12, 15)
        LabelExePath.Name = "LabelExePath"
        LabelExePath.Size = New Drawing.Size(82, 15)
        LabelExePath.TabIndex = 0
        LabelExePath.Text = "Fallout4.exe:"
        '
        ' TextBoxExePath
        '
        TextBoxExePath.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxExePath.Location = New Drawing.Point(100, 12)
        TextBoxExePath.Name = "TextBoxExePath"
        TextBoxExePath.ReadOnly = True
        TextBoxExePath.Size = New Drawing.Size(540, 23)
        TextBoxExePath.TabIndex = 1
        '
        ' ButtonBrowse
        '
        ButtonBrowse.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonBrowse.Location = New Drawing.Point(646, 11)
        ButtonBrowse.Name = "ButtonBrowse"
        ButtonBrowse.Size = New Drawing.Size(90, 25)
        ButtonBrowse.TabIndex = 2
        ButtonBrowse.Text = "Browse..."
        ButtonBrowse.UseVisualStyleBackColor = True
        '
        ' LabelPlugins
        '
        LabelPlugins.AutoSize = True
        LabelPlugins.Location = New Drawing.Point(12, 50)
        LabelPlugins.Name = "LabelPlugins"
        LabelPlugins.Size = New Drawing.Size(150, 15)
        LabelPlugins.TabIndex = 3
        LabelPlugins.Text = "Plugins to load:"
        '
        ' ListViewPlugins
        '
        ListViewPlugins.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ListViewPlugins.CheckBoxes = True
        ListViewPlugins.Columns.AddRange(New ColumnHeader() {ColumnHeaderPlugin, ColumnHeaderState})
        ListViewPlugins.FullRowSelect = True
        ListViewPlugins.GridLines = True
        ListViewPlugins.Location = New Drawing.Point(12, 70)
        ListViewPlugins.Name = "ListViewPlugins"
        ListViewPlugins.Size = New Drawing.Size(724, 425)
        ListViewPlugins.TabIndex = 4
        ListViewPlugins.UseCompatibleStateImageBehavior = False
        ListViewPlugins.View = View.Details
        '
        ' ColumnHeaderPlugin
        '
        ColumnHeaderPlugin.Text = "Plugin"
        ColumnHeaderPlugin.Width = 520
        '
        ' ColumnHeaderState
        '
        ColumnHeaderState.Text = "State"
        ColumnHeaderState.Width = 180
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Drawing.Point(550, 505)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Drawing.Size(90, 28)
        ButtonOk.TabIndex = 5
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Drawing.Point(646, 505)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Drawing.Size(90, 28)
        ButtonCancel.TabIndex = 6
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' LabelStatus
        '
        LabelStatus.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelStatus.Location = New Drawing.Point(12, 545)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Drawing.Size(724, 21)
        LabelStatus.TabIndex = 7
        LabelStatus.Text = ""
        '
        ' LabelProgress
        '
        LabelProgress.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelProgress.Location = New Drawing.Point(12, 545)
        LabelProgress.Name = "LabelProgress"
        LabelProgress.Size = New Drawing.Size(724, 20)
        LabelProgress.TabIndex = 8
        LabelProgress.Text = ""
        LabelProgress.Visible = False
        '
        ' ProgressBarLoad
        '
        ProgressBarLoad.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarLoad.Location = New Drawing.Point(12, 568)
        ProgressBarLoad.Name = "ProgressBarLoad"
        ProgressBarLoad.Size = New Drawing.Size(724, 16)
        ProgressBarLoad.Style = ProgressBarStyle.Continuous
        ProgressBarLoad.TabIndex = 9
        ProgressBarLoad.Visible = False
        '
        ' Preflight_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoScroll = True
        CancelButton = ButtonCancel
        ClientSize = New Drawing.Size(748, 595)
        Controls.Add(ProgressBarLoad)
        Controls.Add(LabelProgress)
        Controls.Add(LabelStatus)
        Controls.Add(ButtonCancel)
        Controls.Add(ButtonOk)
        Controls.Add(ListViewPlugins)
        Controls.Add(LabelPlugins)
        Controls.Add(ButtonBrowse)
        Controls.Add(TextBoxExePath)
        Controls.Add(LabelExePath)
        MinimumSize = New Drawing.Size(640, 480)
        Name = "Preflight_Form"
        StartPosition = FormStartPosition.CenterScreen
        Text = "FO4 NPC Manager — Setup"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelExePath As Label
    Friend WithEvents TextBoxExePath As TextBox
    Friend WithEvents ButtonBrowse As Button
    Friend WithEvents LabelPlugins As Label
    Friend WithEvents ListViewPlugins As ListView
    Friend WithEvents ColumnHeaderPlugin As ColumnHeader
    Friend WithEvents ColumnHeaderState As ColumnHeader
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
    Friend WithEvents LabelStatus As Label
    Friend WithEvents ProgressBarLoad As ProgressBar
    Friend WithEvents LabelProgress As Label
End Class
