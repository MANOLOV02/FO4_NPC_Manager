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
        LabelGame = New Label()
        ComboBoxGame = New ComboBox()
        LabelExePath = New Label()
        TextBoxExePath = New TextBox()
        ButtonBrowse = New Button()
        LabelPluginsTxt = New Label()
        TextBoxPluginsTxt = New TextBox()
        ButtonBrowsePluginsTxt = New Button()
        ButtonAutoPluginsTxt = New Button()
        LabelIniDir = New Label()
        TextBoxIniDir = New TextBox()
        ButtonBrowseIniDir = New Button()
        ButtonAutoIniDir = New Button()
        LabelPathsStatus = New Label()
        LabelPlugins = New Label()
        TextBoxFilter = New TextBox()
        ButtonSelectActives = New Button()
        ButtonMarkAll = New Button()
        ButtonUnmarkAll = New Button()
        ListViewPlugins = New ListView()
        ColumnHeaderPlugin = New ColumnHeader()
        ColumnHeaderState = New ColumnHeader()
        ButtonCheckMasters = New Button()
        CheckBoxPersistSelection = New CheckBox()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        LabelStatus = New Label()
        ProgressBarOverall = New ProgressBar()
        ProgressBarDetail = New ProgressBar()
        LabelProgress = New Label()
        SuspendLayout()
        '
        ' LabelGame
        '
        LabelGame.AutoSize = True
        LabelGame.Location = New Point(12, 15)
        LabelGame.Name = "LabelGame"
        LabelGame.Size = New Size(41, 15)
        LabelGame.TabIndex = 0
        LabelGame.Text = "Game:"
        '
        ' ComboBoxGame
        '
        ComboBoxGame.DropDownStyle = ComboBoxStyle.DropDownList
        ComboBoxGame.Items.AddRange(New Object() {"Fallout 4", "Skyrim SE"})
        ComboBoxGame.Location = New Point(57, 12)
        ComboBoxGame.Name = "ComboBoxGame"
        ComboBoxGame.Size = New Size(120, 23)
        ComboBoxGame.TabIndex = 1
        '
        ' LabelExePath
        '
        LabelExePath.AutoSize = True
        LabelExePath.Location = New Point(183, 15)
        LabelExePath.Name = "LabelExePath"
        LabelExePath.Size = New Size(30, 15)
        LabelExePath.TabIndex = 2
        LabelExePath.Text = "Exe:"
        '
        ' TextBoxExePath
        '
        TextBoxExePath.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxExePath.Location = New Point(220, 12)
        TextBoxExePath.Name = "TextBoxExePath"
        TextBoxExePath.ReadOnly = True
        TextBoxExePath.Size = New Size(376, 23)
        TextBoxExePath.TabIndex = 3
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
        ' LabelPluginsTxt
        '
        LabelPluginsTxt.AutoSize = True
        LabelPluginsTxt.Location = New Point(12, 44)
        LabelPluginsTxt.Name = "LabelPluginsTxt"
        LabelPluginsTxt.Size = New Size(70, 15)
        LabelPluginsTxt.TabIndex = 20
        LabelPluginsTxt.Text = "Plugins.txt:"
        '
        ' TextBoxPluginsTxt
        '
        TextBoxPluginsTxt.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxPluginsTxt.Location = New Point(100, 41)
        TextBoxPluginsTxt.Name = "TextBoxPluginsTxt"
        TextBoxPluginsTxt.ReadOnly = True
        TextBoxPluginsTxt.Size = New Size(474, 23)
        TextBoxPluginsTxt.TabIndex = 21
        '
        ' ButtonBrowsePluginsTxt
        '
        ButtonBrowsePluginsTxt.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonBrowsePluginsTxt.Location = New Point(580, 40)
        ButtonBrowsePluginsTxt.Name = "ButtonBrowsePluginsTxt"
        ButtonBrowsePluginsTxt.Size = New Size(90, 25)
        ButtonBrowsePluginsTxt.TabIndex = 22
        ButtonBrowsePluginsTxt.Text = "Browse..."
        ButtonBrowsePluginsTxt.UseVisualStyleBackColor = True
        '
        ' ButtonAutoPluginsTxt
        '
        ButtonAutoPluginsTxt.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonAutoPluginsTxt.Location = New Point(676, 40)
        ButtonAutoPluginsTxt.Name = "ButtonAutoPluginsTxt"
        ButtonAutoPluginsTxt.Size = New Size(60, 25)
        ButtonAutoPluginsTxt.TabIndex = 23
        ButtonAutoPluginsTxt.Text = "Auto"
        ButtonAutoPluginsTxt.UseVisualStyleBackColor = True
        '
        ' LabelIniDir
        '
        LabelIniDir.AutoSize = True
        LabelIniDir.Location = New Point(12, 73)
        LabelIniDir.Name = "LabelIniDir"
        LabelIniDir.Size = New Size(66, 15)
        LabelIniDir.TabIndex = 24
        LabelIniDir.Text = "Game INIs:"
        '
        ' TextBoxIniDir
        '
        TextBoxIniDir.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxIniDir.Location = New Point(100, 70)
        TextBoxIniDir.Name = "TextBoxIniDir"
        TextBoxIniDir.ReadOnly = True
        TextBoxIniDir.Size = New Size(474, 23)
        TextBoxIniDir.TabIndex = 25
        '
        ' ButtonBrowseIniDir
        '
        ButtonBrowseIniDir.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonBrowseIniDir.Location = New Point(580, 69)
        ButtonBrowseIniDir.Name = "ButtonBrowseIniDir"
        ButtonBrowseIniDir.Size = New Size(90, 25)
        ButtonBrowseIniDir.TabIndex = 26
        ButtonBrowseIniDir.Text = "Browse..."
        ButtonBrowseIniDir.UseVisualStyleBackColor = True
        '
        ' ButtonAutoIniDir
        '
        ButtonAutoIniDir.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonAutoIniDir.Location = New Point(676, 69)
        ButtonAutoIniDir.Name = "ButtonAutoIniDir"
        ButtonAutoIniDir.Size = New Size(60, 25)
        ButtonAutoIniDir.TabIndex = 27
        ButtonAutoIniDir.Text = "Auto"
        ButtonAutoIniDir.UseVisualStyleBackColor = True
        '
        ' LabelPathsStatus
        '
        LabelPathsStatus.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelPathsStatus.AutoEllipsis = True
        LabelPathsStatus.Location = New Point(100, 98)
        LabelPathsStatus.Name = "LabelPathsStatus"
        LabelPathsStatus.Size = New Size(636, 17)
        LabelPathsStatus.TabIndex = 28
        '
        ' LabelPlugins
        '
        LabelPlugins.AutoSize = True
        LabelPlugins.Location = New Point(12, 126)
        LabelPlugins.Name = "LabelPlugins"
        LabelPlugins.Size = New Size(89, 15)
        LabelPlugins.TabIndex = 3
        LabelPlugins.Text = "Plugins to load:"
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFilter.Location = New Point(107, 123)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Filter by name..."
        TextBoxFilter.Size = New Size(243, 23)
        TextBoxFilter.TabIndex = 4
        '
        ' ButtonSelectActives
        '
        ButtonSelectActives.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonSelectActives.Location = New Point(358, 121)
        ButtonSelectActives.Name = "ButtonSelectActives"
        ButtonSelectActives.Size = New Size(110, 25)
        ButtonSelectActives.TabIndex = 5
        ButtonSelectActives.Text = "Only actives"
        ButtonSelectActives.UseVisualStyleBackColor = True
        '
        ' ButtonMarkAll
        '
        ButtonMarkAll.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonMarkAll.Location = New Point(476, 121)
        ButtonMarkAll.Name = "ButtonMarkAll"
        ButtonMarkAll.Size = New Size(120, 25)
        ButtonMarkAll.TabIndex = 6
        ButtonMarkAll.Text = "Mark all visible"
        ButtonMarkAll.UseVisualStyleBackColor = True
        '
        ' ButtonUnmarkAll
        '
        ButtonUnmarkAll.Anchor = AnchorStyles.Top Or AnchorStyles.Right
        ButtonUnmarkAll.Location = New Point(604, 121)
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
        ListViewPlugins.Location = New Point(12, 156)
        ListViewPlugins.Name = "ListViewPlugins"
        ListViewPlugins.Size = New Size(724, 465)
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
        ButtonCheckMasters.Location = New Point(414, 627)
        ButtonCheckMasters.Name = "ButtonCheckMasters"
        ButtonCheckMasters.Size = New Size(130, 28)
        ButtonCheckMasters.TabIndex = 14
        ButtonCheckMasters.Text = "Check Masters"
        ButtonCheckMasters.UseVisualStyleBackColor = True
        ButtonCheckMasters.Visible = False
        '
        ' CheckBoxPersistSelection
        '
        CheckBoxPersistSelection.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left
        CheckBoxPersistSelection.AutoSize = True
        CheckBoxPersistSelection.Location = New Point(12, 632)
        CheckBoxPersistSelection.Name = "CheckBoxPersistSelection"
        CheckBoxPersistSelection.Size = New Size(215, 19)
        CheckBoxPersistSelection.TabIndex = 15
        CheckBoxPersistSelection.Text = "Remember this selection for this game"
        CheckBoxPersistSelection.UseVisualStyleBackColor = True
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Location = New Point(550, 627)
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
        ButtonCancel.Location = New Point(646, 627)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(90, 28)
        ButtonCancel.TabIndex = 10
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' LabelStatus
        '
        LabelStatus.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelStatus.Location = New Point(12, 679)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(724, 21)
        LabelStatus.TabIndex = 11
        '
        ' ProgressBarOverall
        '
        ProgressBarOverall.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarOverall.Location = New Point(12, 679)
        ProgressBarOverall.Name = "ProgressBarOverall"
        ProgressBarOverall.Size = New Size(724, 14)
        ProgressBarOverall.Style = ProgressBarStyle.Continuous
        ProgressBarOverall.TabIndex = 13
        ProgressBarOverall.Visible = False
        '
        ' ProgressBarDetail
        '
        ProgressBarDetail.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarDetail.Location = New Point(12, 697)
        ProgressBarDetail.Name = "ProgressBarDetail"
        ProgressBarDetail.Size = New Size(724, 14)
        ProgressBarDetail.Style = ProgressBarStyle.Continuous
        ProgressBarDetail.TabIndex = 14
        ProgressBarDetail.Visible = False
        '
        ' LabelProgress
        '
        LabelProgress.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelProgress.Location = New Point(12, 658)
        LabelProgress.Name = "LabelProgress"
        LabelProgress.Size = New Size(724, 18)
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
        ClientSize = New Size(748, 729)
        Controls.Add(ProgressBarOverall)
        Controls.Add(ProgressBarDetail)
        Controls.Add(LabelProgress)
        Controls.Add(LabelStatus)
        Controls.Add(ButtonCancel)
        Controls.Add(ButtonOk)
        Controls.Add(CheckBoxPersistSelection)
        Controls.Add(ButtonCheckMasters)
        Controls.Add(ListViewPlugins)
        Controls.Add(ButtonUnmarkAll)
        Controls.Add(ButtonMarkAll)
        Controls.Add(ButtonSelectActives)
        Controls.Add(TextBoxFilter)
        Controls.Add(LabelPlugins)
        Controls.Add(LabelPathsStatus)
        Controls.Add(ButtonAutoIniDir)
        Controls.Add(ButtonBrowseIniDir)
        Controls.Add(TextBoxIniDir)
        Controls.Add(LabelIniDir)
        Controls.Add(ButtonAutoPluginsTxt)
        Controls.Add(ButtonBrowsePluginsTxt)
        Controls.Add(TextBoxPluginsTxt)
        Controls.Add(LabelPluginsTxt)
        Controls.Add(ButtonBrowse)
        Controls.Add(TextBoxExePath)
        Controls.Add(LabelExePath)
        Controls.Add(ComboBoxGame)
        Controls.Add(LabelGame)
        MinimumSize = New Size(640, 556)
        Name = "Preflight_Form"
        StartPosition = FormStartPosition.CenterScreen
        Text = "FO4 NPC Manager — Setup"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelGame As Label
    Friend WithEvents ComboBoxGame As ComboBox
    Friend WithEvents LabelExePath As Label
    Friend WithEvents TextBoxExePath As TextBox
    Friend WithEvents ButtonBrowse As Button
    Friend WithEvents LabelPluginsTxt As Label
    Friend WithEvents TextBoxPluginsTxt As TextBox
    Friend WithEvents ButtonBrowsePluginsTxt As Button
    Friend WithEvents ButtonAutoPluginsTxt As Button
    Friend WithEvents LabelIniDir As Label
    Friend WithEvents TextBoxIniDir As TextBox
    Friend WithEvents ButtonBrowseIniDir As Button
    Friend WithEvents ButtonAutoIniDir As Button
    Friend WithEvents LabelPathsStatus As Label
    Friend WithEvents LabelPlugins As Label
    Friend WithEvents TextBoxFilter As TextBox
    Friend WithEvents ButtonSelectActives As Button
    Friend WithEvents ButtonMarkAll As Button
    Friend WithEvents ButtonUnmarkAll As Button
    Friend WithEvents ListViewPlugins As ListView
    Friend WithEvents ColumnHeaderPlugin As ColumnHeader
    Friend WithEvents ColumnHeaderState As ColumnHeader
    Friend WithEvents ButtonCheckMasters As Button
    Friend WithEvents CheckBoxPersistSelection As CheckBox
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
    Friend WithEvents LabelStatus As Label
    Friend WithEvents ProgressBarOverall As ProgressBar
    Friend WithEvents ProgressBarDetail As ProgressBar
    Friend WithEvents LabelProgress As Label
End Class
