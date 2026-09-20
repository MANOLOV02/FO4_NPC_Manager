' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class FormListEditor_Form
    Inherits FO4_Base_Library.IconFormBase

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
        TopRow = New FlowLayoutPanel()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelName = New Label()
        TextBoxName = New TextBox()
        LabelBanner = New Label()
        LabelHint = New Label()
        ListViewMembers = New ListView()
        ButtonsRow = New FlowLayoutPanel()
        ButtonCloneFrom = New Button()
        ButtonAdd = New Button()
        ButtonRemove = New Button()
        ButtonUp = New Button()
        ButtonDown = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        TopRow.SuspendLayout()
        ButtonsRow.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopRow, 0, 0)
        RootLayout.Controls.Add(LabelHint, 0, 1)
        RootLayout.Controls.Add(ListViewMembers, 0, 2)
        RootLayout.Controls.Add(ButtonsRow, 0, 3)
        RootLayout.Controls.Add(BottomLayout, 0, 4)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 5
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.TabIndex = 0
        '
        TopRow.AutoSize = True
        TopRow.Controls.Add(LabelEdid)
        TopRow.Controls.Add(TextBoxEdid)
        TopRow.Controls.Add(LabelName)
        TopRow.Controls.Add(TextBoxName)
        TopRow.Controls.Add(LabelBanner)
        TopRow.Dock = DockStyle.Fill
        TopRow.Name = "TopRow"
        TopRow.TabIndex = 0
        TopRow.WrapContents = False
        '
        LabelEdid.Anchor = AnchorStyles.Left
        LabelEdid.AutoSize = True
        LabelEdid.Margin = New Padding(3, 9, 3, 0)
        LabelEdid.Name = "LabelEdid"
        LabelEdid.TabIndex = 0
        LabelEdid.Text = "EditorID:"
        '
        TextBoxEdid.Margin = New Padding(3, 5, 3, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.Size = New Size(220, 23)
        TextBoxEdid.TabIndex = 1
        '
        LabelName.Anchor = AnchorStyles.Left
        LabelName.AutoSize = True
        LabelName.Margin = New Padding(12, 9, 3, 0)
        LabelName.Name = "LabelName"
        LabelName.TabIndex = 2
        LabelName.Text = "Name (FULL):"
        '
        TextBoxName.Margin = New Padding(3, 5, 3, 3)
        TextBoxName.Name = "TextBoxName"
        TextBoxName.Size = New Size(200, 23)
        TextBoxName.TabIndex = 3
        '
        LabelBanner.Anchor = AnchorStyles.Left
        LabelBanner.AutoSize = True
        LabelBanner.ForeColor = SystemColors.GrayText
        LabelBanner.Margin = New Padding(12, 9, 3, 0)
        LabelBanner.Name = "LabelBanner"
        LabelBanner.TabIndex = 4
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.MaximumSize = New Size(820, 0)
        LabelHint.Name = "LabelHint"
        LabelHint.TabIndex = 1
        '
        ListViewMembers.Dock = DockStyle.Fill
        ListViewMembers.FullRowSelect = True
        ListViewMembers.HideSelection = False
        ListViewMembers.MultiSelect = False
        ListViewMembers.Name = "ListViewMembers"
        ListViewMembers.TabIndex = 2
        ListViewMembers.UseCompatibleStateImageBehavior = False
        ListViewMembers.View = View.Details
        '
        ButtonsRow.AutoSize = True
        ButtonsRow.Controls.Add(ButtonCloneFrom)
        ButtonsRow.Controls.Add(ButtonAdd)
        ButtonsRow.Controls.Add(ButtonRemove)
        ButtonsRow.Controls.Add(ButtonUp)
        ButtonsRow.Controls.Add(ButtonDown)
        ButtonsRow.Dock = DockStyle.Fill
        ButtonsRow.Name = "ButtonsRow"
        ButtonsRow.TabIndex = 3
        ButtonsRow.WrapContents = False
        '
        ButtonCloneFrom.AutoSize = True
        ButtonCloneFrom.Name = "ButtonCloneFrom"
        ButtonCloneFrom.TabIndex = 0
        ButtonCloneFrom.Text = "Start from an existing list…"
        '
        ButtonAdd.AutoSize = True
        ButtonAdd.Name = "ButtonAdd"
        ButtonAdd.TabIndex = 1
        ButtonAdd.Text = "Add…"
        '
        ButtonRemove.AutoSize = True
        ButtonRemove.Name = "ButtonRemove"
        ButtonRemove.TabIndex = 2
        ButtonRemove.Text = "Remove"
        '
        ButtonUp.AutoSize = True
        ButtonUp.Name = "ButtonUp"
        ButtonUp.TabIndex = 3
        ButtonUp.Text = "Move Up"
        '
        ButtonDown.AutoSize = True
        ButtonDown.Name = "ButtonDown"
        ButtonDown.TabIndex = 4
        ButtonDown.Text = "Move Down"
        '
        BottomLayout.AutoSize = True
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 4
        BottomLayout.WrapContents = False
        '
        ButtonOk.AutoSize = True
        ButtonOk.Name = "ButtonOk"
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        '
        ButtonCancel.AutoSize = True
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        '
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(860, 460)
        Controls.Add(RootLayout)
        MinimumSize = New Size(700, 400)
        Name = "FormListEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Form List Editor"
        RootLayout.ResumeLayout(False)
        TopRow.ResumeLayout(False)
        ButtonsRow.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents TopRow As FlowLayoutPanel
    Friend WithEvents LabelEdid As Label
    Friend WithEvents TextBoxEdid As TextBox
    Friend WithEvents LabelName As Label
    Friend WithEvents TextBoxName As TextBox
    Friend WithEvents LabelBanner As Label
    Friend WithEvents LabelHint As Label
    Friend WithEvents ListViewMembers As ListView
    Friend WithEvents ButtonsRow As FlowLayoutPanel
    Friend WithEvents ButtonCloneFrom As Button
    Friend WithEvents ButtonAdd As Button
    Friend WithEvents ButtonRemove As Button
    Friend WithEvents ButtonUp As Button
    Friend WithEvents ButtonDown As Button
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
