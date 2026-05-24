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
        TextBoxFilter = New TextBox()
        ButtonSelectActives = New Button()
        ButtonMarkAll = New Button()
        ButtonUnmarkAll = New Button()
        ListViewPlugins = New ListView()
        ColumnHeaderPlugin = New ColumnHeader()
        ColumnHeaderState = New ColumnHeader()
        ButtonCheckMasters = New Button()
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
        LabelExePath.Location = New Point(12, 15)
        LabelExePath.Name = "LabelExePath"
        LabelExePath.Size = New Size(72, 15)
        LabelExePath.TabIndex = 0
        LabelExePath.Text = "Fallout4.exe:"
        ' 
        ' TextBoxExePath
        ' 
        TextBoxExePath.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxExePath.Location = New Point(107, 12)
        TextBoxExePath.Name = "TextBoxExePath"
        TextBoxExePath.ReadOnly = True
        TextBoxExePath.Size = New Size(489, 23)
        TextBoxExePath.TabIndex = 1
        ' 
        ' ButtonBrowse
        ' 
        ButtonBrowse.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonBrowse.Location = New Point(604, 11)
        ButtonBrowse.Name = "ButtonBrowse"
        ButtonBrowse.Size = New Size(132, 25)
        ButtonBrowse.TabIndex = 2
        ButtonBrowse.Text = "Browse..."
        ButtonBrowse.UseVisualStyleBackColor = True
        ' 
        ' LabelPlugins
        ' 
        LabelPlugins.AutoSize = True
        LabelPlugins.Location = New Point(12, 50)
        LabelPlugins.Name = "LabelPlugins"
        LabelPlugins.Size = New Size(89, 15)
        LabelPlugins.TabIndex = 3
        LabelPlugins.Text = "Plugins to load:"
        ' 
        ' TextBoxFilter
        ' 
        TextBoxFilter.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFilter.Location = New Point(107, 47)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name..."
        TextBoxFilter.Size = New Size(243, 23)
        TextBoxFilter.TabIndex = 4
        ' 
        ' ButtonSelectActives
        ' 
        ButtonSelectActives.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonSelectActives.Location = New Point(358, 45)
        ButtonSelectActives.Name = "ButtonSelectActives"
        ButtonSelectActives.Size = New Size(110, 25)
        ButtonSelectActives.TabIndex = 5
        ButtonSelectActives.Text = "Only actives"
        ButtonSelectActives.UseVisualStyleBackColor = True
        ' 
        ' ButtonMarkAll
        ' 
        ButtonMarkAll.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonMarkAll.Location = New Point(476, 45)
        ButtonMarkAll.Name = "ButtonMarkAll"
        ButtonMarkAll.Size = New Size(120, 25)
        ButtonMarkAll.TabIndex = 6
        ButtonMarkAll.Text = "Mark all visible"
        ButtonMarkAll.UseVisualStyleBackColor = True
        ' 
        ' ButtonUnmarkAll
        ' 
        ButtonUnmarkAll.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonUnmarkAll.Location = New Point(604, 45)
        ButtonUnmarkAll.Name = "ButtonUnmarkAll"
        ButtonUnmarkAll.Size = New Size(132, 25)
        ButtonUnmarkAll.TabIndex = 7
        ButtonUnmarkAll.Text = "Unmark all visible"
        ButtonUnmarkAll.UseVisualStyleBackColor = True
        ' 
        ' ListViewPlugins
        ' 
        ListViewPlugins.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ListViewPlugins.CheckBoxes = True
        ListViewPlugins.Columns.AddRange(New ColumnHeader() {ColumnHeaderPlugin, ColumnHeaderState})
        ListViewPlugins.FullRowSelect = True
        ListViewPlugins.GridLines = True
        ListViewPlugins.Location = New Point(12, 80)
        ListViewPlugins.Name = "ListViewPlugins"
        ListViewPlugins.Size = New Size(724, 415)
        ListViewPlugins.TabIndex = 8
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
        ' ButtonCheckMasters
        '
        ButtonCheckMasters.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCheckMasters.Location = New Point(414, 505)
        ButtonCheckMasters.Name = "ButtonCheckMasters"
        ButtonCheckMasters.Size = New Size(130, 28)
        ButtonCheckMasters.TabIndex = 14
        ButtonCheckMasters.Text = "Check Masters"
        ButtonCheckMasters.UseVisualStyleBackColor = True
        ButtonCheckMasters.Visible = False
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Point(550, 505)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(90, 28)
        ButtonOk.TabIndex = 9
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        ' 
        ' ButtonCancel
        ' 
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(646, 505)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(90, 28)
        ButtonCancel.TabIndex = 10
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        ' 
        ' LabelStatus
        ' 
        LabelStatus.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelStatus.Location = New Point(12, 545)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(724, 21)
        LabelStatus.TabIndex = 11
        ' 
        ' ProgressBarLoad
        ' 
        ProgressBarLoad.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarLoad.Location = New Point(12, 568)
        ProgressBarLoad.Name = "ProgressBarLoad"
        ProgressBarLoad.Size = New Size(724, 16)
        ProgressBarLoad.Style = ProgressBarStyle.Continuous
        ProgressBarLoad.TabIndex = 13
        ProgressBarLoad.Visible = False
        ' 
        ' LabelProgress
        ' 
        LabelProgress.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelProgress.Location = New Point(12, 545)
        LabelProgress.Name = "LabelProgress"
        LabelProgress.Size = New Size(724, 20)
        LabelProgress.TabIndex = 12
        LabelProgress.Visible = False
        ' 
        ' Preflight_Form
        ' 
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoScroll = True
        CancelButton = ButtonCancel
        ClientSize = New Size(748, 595)
        Controls.Add(ProgressBarLoad)
        Controls.Add(LabelProgress)
        Controls.Add(LabelStatus)
        Controls.Add(ButtonCancel)
        Controls.Add(ButtonOk)
        Controls.Add(ButtonCheckMasters)
        Controls.Add(ListViewPlugins)
        Controls.Add(ButtonUnmarkAll)
        Controls.Add(ButtonMarkAll)
        Controls.Add(ButtonSelectActives)
        Controls.Add(TextBoxFilter)
        Controls.Add(LabelPlugins)
        Controls.Add(ButtonBrowse)
        Controls.Add(TextBoxExePath)
        Controls.Add(LabelExePath)
        MinimumSize = New Size(640, 480)
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
    Friend WithEvents TextBoxFilter As TextBox
    Friend WithEvents ButtonSelectActives As Button
    Friend WithEvents ButtonMarkAll As Button
    Friend WithEvents ButtonUnmarkAll As Button
    Friend WithEvents ListViewPlugins As ListView
    Friend WithEvents ColumnHeaderPlugin As ColumnHeader
    Friend WithEvents ColumnHeaderState As ColumnHeader
    Friend WithEvents ButtonCheckMasters As Button
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
    Friend WithEvents LabelStatus As Label
    Friend WithEvents ProgressBarLoad As ProgressBar
    Friend WithEvents LabelProgress As Label
End Class
