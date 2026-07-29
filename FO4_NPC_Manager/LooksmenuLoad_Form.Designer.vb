' UI built in Designer per feedback_ch_ui_winforms. The right-hand category checkboxes live in the shared
' PresetCategoryPanel — the same control Paste Look hosts — so both features offer the same categories.
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
        Root = New TableLayoutPanel()
        LabelHeader = New Label()
        FilterRow = New TableLayoutPanel()
        LabelFilter = New Label()
        TextBoxFilter = New TextBox()
        CheckBoxRaceCompatible = New CheckBox()
        ListBoxPresets = New ListBox()
        InfoRow = New TableLayoutPanel()
        LabelInfo = New Label()
        ButtonShowIncompatible = New Button()
        CategoriesGroup = New GroupBox()
        CategoryPanel = New PresetCategoryPanel()
        ButtonRow = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        Root.SuspendLayout()
        FilterRow.SuspendLayout()
        InfoRow.SuspendLayout()
        CategoriesGroup.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        Root.ColumnCount = 2
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 486F))
        Root.Controls.Add(LabelHeader, 0, 0)
        Root.Controls.Add(FilterRow, 0, 1)
        Root.Controls.Add(CheckBoxRaceCompatible, 0, 2)
        Root.Controls.Add(ListBoxPresets, 0, 3)
        Root.Controls.Add(InfoRow, 0, 4)
        Root.Controls.Add(CategoriesGroup, 1, 1)
        Root.Controls.Add(ButtonRow, 0, 5)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(12)
        Root.RowCount = 6
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 40F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 32F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 26F))
        Root.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ' 29 = exactly one 23px button plus its 3px breathing room top and bottom. It used to be 42, and those
        ' 13px were dead space between the list and a two-line label that no longer exists — the list (the
        ' Percent row above) takes them now, so it reaches further down.
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 29F))
        Root.RowStyles.Add(New RowStyle(SizeType.Absolute, 40F))
        Root.SetColumnSpan(LabelHeader, 2)
        Root.SetColumnSpan(ButtonRow, 2)
        Root.SetRowSpan(CategoriesGroup, 4)
        Root.Size = New Size(884, 581)
        Root.TabIndex = 0
        '
        ' LabelHeader
        '
        LabelHeader.Dock = DockStyle.Fill
        LabelHeader.Location = New Point(12, 12)
        LabelHeader.Margin = New Padding(0)
        LabelHeader.Name = "LabelHeader"
        LabelHeader.Size = New Size(1056, 40)
        LabelHeader.TabIndex = 0
        LabelHeader.Text = ""
        '
        ' FilterRow
        '
        FilterRow.ColumnCount = 2
        FilterRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 46F))
        FilterRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        FilterRow.Controls.Add(LabelFilter, 0, 0)
        FilterRow.Controls.Add(TextBoxFilter, 1, 0)
        FilterRow.Dock = DockStyle.Fill
        FilterRow.Location = New Point(12, 52)
        FilterRow.Margin = New Padding(0)
        FilterRow.Name = "FilterRow"
        FilterRow.RowCount = 1
        FilterRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        FilterRow.Size = New Size(570, 32)
        FilterRow.TabIndex = 1
        '
        ' LabelFilter
        '
        LabelFilter.AutoSize = True
        LabelFilter.Dock = DockStyle.Fill
        LabelFilter.Location = New Point(3, 0)
        LabelFilter.Name = "LabelFilter"
        LabelFilter.Size = New Size(40, 32)
        LabelFilter.TabIndex = 0
        LabelFilter.Text = "Filter:"
        LabelFilter.TextAlign = ContentAlignment.MiddleLeft
        '
        ' TextBoxFilter
        '
        TextBoxFilter.Dock = DockStyle.Fill
        TextBoxFilter.Location = New Point(49, 3)
        TextBoxFilter.Name = "TextBoxFilter"
        TextBoxFilter.PlaceholderText = "Type to filter presets..."
        TextBoxFilter.Size = New Size(518, 23)
        TextBoxFilter.TabIndex = 1
        '
        ' CheckBoxRaceCompatible — hides presets whose HeadParts/Tints don't apply to this NPC's race.
        ' ON by default; user can disable to see everything.
        '
        CheckBoxRaceCompatible.AutoSize = True
        CheckBoxRaceCompatible.Checked = True
        CheckBoxRaceCompatible.CheckState = CheckState.Checked
        CheckBoxRaceCompatible.Dock = DockStyle.Fill
        CheckBoxRaceCompatible.Location = New Point(12, 84)
        CheckBoxRaceCompatible.Margin = New Padding(0)
        CheckBoxRaceCompatible.Name = "CheckBoxRaceCompatible"
        CheckBoxRaceCompatible.Size = New Size(570, 26)
        CheckBoxRaceCompatible.TabIndex = 2
        CheckBoxRaceCompatible.Text = "Show only race-compatible presets"
        CheckBoxRaceCompatible.UseVisualStyleBackColor = True
        '
        ' ListBoxPresets
        '
        ListBoxPresets.Dock = DockStyle.Fill
        ListBoxPresets.IntegralHeight = False
        ListBoxPresets.Location = New Point(12, 110)
        ListBoxPresets.Margin = New Padding(0, 0, 6, 0)
        ListBoxPresets.Name = "ListBoxPresets"
        ListBoxPresets.Size = New Size(564, 356)
        ListBoxPresets.TabIndex = 3
        '
        ' InfoRow — provenance/warning label on the left, "Show incompatible" on the right. The label only
        ' states THAT something is missing; the button opens the exhaustive per-item report.
        '
        InfoRow.ColumnCount = 2
        InfoRow.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        InfoRow.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 160F))
        InfoRow.Controls.Add(LabelInfo, 0, 0)
        InfoRow.Controls.Add(ButtonShowIncompatible, 1, 0)
        InfoRow.Dock = DockStyle.Fill
        InfoRow.Location = New Point(12, 466)
        ' 6px off the list above (so the row doesn't touch its border) and the same 6px right margin the list
        ' uses, so the button's right edge lines up with the list's instead of overhanging it.
        InfoRow.Margin = New Padding(0, 6, 6, 0)
        InfoRow.Name = "InfoRow"
        InfoRow.RowCount = 1
        InfoRow.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        InfoRow.Size = New Size(570, 29)
        InfoRow.TabIndex = 4
        '
        ' LabelInfo
        '
        ' Anchored Left|Right with NO vertical anchor: WinForms then keeps it vertically centred in the cell
        ' and stretches it with the dialog. Same rule as the button beside it, so both share one centre line
        ' at every window size (a vertical anchor would pin it to an edge and they'd drift apart on resize).
        LabelInfo.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        LabelInfo.ForeColor = SystemColors.GrayText
        LabelInfo.Location = New Point(0, 3)
        LabelInfo.Margin = New Padding(0)
        LabelInfo.Name = "LabelInfo"
        LabelInfo.Size = New Size(407, 23)
        LabelInfo.TabIndex = 0
        LabelInfo.Text = ""
        LabelInfo.TextAlign = ContentAlignment.MiddleLeft
        '
        ' ButtonShowIncompatible
        '
        ' Anchored Right only (no vertical anchor) so it stays vertically centred in the row and hugs the
        ' right edge of the list column as the dialog is resized.
        ButtonShowIncompatible.Anchor = AnchorStyles.Right
        ButtonShowIncompatible.Enabled = False
        ButtonShowIncompatible.Location = New Point(413, 3)
        ButtonShowIncompatible.Margin = New Padding(3, 0, 0, 0)
        ButtonShowIncompatible.Name = "ButtonShowIncompatible"
        ButtonShowIncompatible.Size = New Size(157, 23)
        ButtonShowIncompatible.TabIndex = 1
        ButtonShowIncompatible.Text = "Show incompatible"
        ButtonShowIncompatible.UseVisualStyleBackColor = True
        '
        ' CategoriesGroup
        '
        CategoriesGroup.Controls.Add(CategoryPanel)
        CategoriesGroup.Dock = DockStyle.Fill
        CategoriesGroup.Location = New Point(582, 52)
        CategoriesGroup.Margin = New Padding(0)
        CategoriesGroup.Name = "CategoriesGroup"
        CategoriesGroup.Padding = New Padding(8, 4, 8, 8)
        CategoriesGroup.Size = New Size(486, 456)
        CategoriesGroup.TabIndex = 5
        CategoriesGroup.TabStop = False
        CategoriesGroup.Text = "Load these categories"
        '
        ' CategoryPanel
        '
        CategoryPanel.Dock = DockStyle.Fill
        CategoryPanel.Location = New Point(8, 20)
        CategoryPanel.Margin = New Padding(0)
        CategoryPanel.Name = "CategoryPanel"
        CategoryPanel.Size = New Size(470, 428)
        CategoryPanel.TabIndex = 0
        '
        ' ButtonRow
        '
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Dock = DockStyle.Fill
        ButtonRow.FlowDirection = FlowDirection.RightToLeft
        ButtonRow.Location = New Point(12, 508)
        ButtonRow.Margin = New Padding(0)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New Padding(0, 6, 0, 0)
        ButtonRow.Size = New Size(1056, 40)
        ButtonRow.TabIndex = 6
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(963, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(90, 28)
        ButtonCancel.TabIndex = 0
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ButtonOk
        '
        ButtonOk.Enabled = False
        ButtonOk.Location = New Point(867, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(90, 28)
        ButtonOk.TabIndex = 1
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' LooksmenuLoad_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(880, 560)
        Controls.Add(Root)
        MinimumSize = New Size(900, 620)
        Name = "LooksmenuLoad_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Load LooksMenu Preset"
        Root.ResumeLayout(False)
        Root.PerformLayout()
        FilterRow.ResumeLayout(False)
        FilterRow.PerformLayout()
        InfoRow.ResumeLayout(False)
        CategoriesGroup.ResumeLayout(False)
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents Root As TableLayoutPanel
    Friend WithEvents LabelHeader As Label
    Friend WithEvents FilterRow As TableLayoutPanel
    Friend WithEvents LabelFilter As Label
    Friend WithEvents TextBoxFilter As TextBox
    Friend WithEvents CheckBoxRaceCompatible As CheckBox
    Friend WithEvents ListBoxPresets As ListBox
    Friend WithEvents InfoRow As TableLayoutPanel
    Friend WithEvents LabelInfo As Label
    Friend WithEvents ButtonShowIncompatible As Button
    Friend WithEvents CategoriesGroup As GroupBox
    Friend WithEvents CategoryPanel As PresetCategoryPanel
    Friend WithEvents ButtonRow As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
