<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class LooksmenuLoad_Form
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
        LabelHeader = New Label()
        LabelFilter = New Label()
        TextBoxFilter = New TextBox()
        ListBoxPresets = New ListBox()
        LabelInfo = New Label()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        SuspendLayout()
        '
        ' LabelHeader
        '
        LabelHeader.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelHeader.Location = New Drawing.Point(12, 9)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Drawing.Size(560, 32)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = ""
        '
        ' LabelFilter
        '
        LabelFilter.AutoSize = True
        LabelFilter.Location = New Drawing.Point(12, 50)
        LabelFilter.Name = "LabelFilter"
        LabelFilter.Size = New Drawing.Size(40, 15)
        LabelFilter.TabIndex = 1
        LabelFilter.Text = "Filter:"
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        TextBoxFilter.Location = New Drawing.Point(58, 47)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Type to filter presets..."
        TextBoxFilter.Size = New Drawing.Size(514, 23)
        TextBoxFilter.TabIndex = 2
        '
        ' ListBoxPresets
        '
        ListBoxPresets.Anchor = AnchorStyles.Top Or AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        ListBoxPresets.IntegralHeight = False
        ListBoxPresets.Location = New Drawing.Point(12, 80)
        ListBoxPresets.Name = "ListBoxPresets"
        ListBoxPresets.Size = New Drawing.Size(560, 280)
        ListBoxPresets.TabIndex = 3
        '
        ' LabelInfo
        '
        LabelInfo.Anchor = AnchorStyles.Bottom Or AnchorStyles.Left Or AnchorStyles.Right
        LabelInfo.ForeColor = SystemColors.GrayText
        LabelInfo.Location = New Drawing.Point(12, 370)
        LabelInfo.Name = "LabelInfo"
        LabelInfo.Size = New Drawing.Size(560, 36)
        LabelInfo.TabIndex = 4
        LabelInfo.Text = ""
        '
        ' ButtonOk
        '
        ButtonOk.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonOk.Enabled = False
        ButtonOk.Location = New Drawing.Point(388, 415)
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
        ButtonCancel.Location = New Drawing.Point(484, 415)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Drawing.Size(90, 28)
        ButtonCancel.TabIndex = 6
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' LooksmenuLoad_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoScroll = True
        CancelButton = ButtonCancel
        ClientSize = New Drawing.Size(584, 455)
        Controls.Add(ButtonCancel)
        Controls.Add(ButtonOk)
        Controls.Add(LabelInfo)
        Controls.Add(ListBoxPresets)
        Controls.Add(TextBoxFilter)
        Controls.Add(LabelFilter)
        Controls.Add(LabelHeader)
        MinimumSize = New Drawing.Size(420, 320)
        Name = "LooksmenuLoad_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Load LooksMenu Preset"
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents LabelHeader As Label
    Friend WithEvents LabelFilter As Label
    Friend WithEvents TextBoxFilter As TextBox
    Friend WithEvents ListBoxPresets As ListBox
    Friend WithEvents LabelInfo As Label
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
