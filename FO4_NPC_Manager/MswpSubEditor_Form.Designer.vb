' UI built in Designer per feedback_ui_in_designer.md.
' InitializeComponent is declarative ONLY. The substitutions DataGridView's columns (one of which is a
' COMBO column populated at runtime from the gender mesh NIF's materials) are added in code-behind, since
' the combo item set depends on the mesh passed to the constructor.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class MswpSubEditor_Form
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
        EdidRow = New FlowLayoutPanel()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelEdidPreview = New Label()
        LabelHint = New Label()
        GridSubs = New DataGridView()
        ButtonsRow = New FlowLayoutPanel()
        ButtonAddRow = New Button()
        ButtonEditRow = New Button()
        ButtonRemoveRow = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        EdidRow.SuspendLayout()
        CType(GridSubs, ComponentModel.ISupportInitialize).BeginInit()
        ButtonsRow.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(EdidRow, 0, 0)
        RootLayout.Controls.Add(LabelHint, 0, 1)
        RootLayout.Controls.Add(GridSubs, 0, 2)
        RootLayout.Controls.Add(ButtonsRow, 0, 3)
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
        RootLayout.Size = New Size(820, 480)
        RootLayout.TabIndex = 0
        '
        ' EdidRow
        '
        EdidRow.AutoSize = True
        EdidRow.Controls.Add(LabelEdid)
        EdidRow.Controls.Add(TextBoxEdid)
        EdidRow.Controls.Add(LabelEdidPreview)
        EdidRow.Dock = DockStyle.Fill
        EdidRow.Location = New Point(8, 8)
        EdidRow.Margin = New Padding(0)
        EdidRow.Name = "EdidRow"
        EdidRow.Size = New Size(804, 29)
        EdidRow.TabIndex = 0
        EdidRow.WrapContents = False
        '
        ' LabelEdid
        '
        LabelEdid.Anchor = AnchorStyles.Left
        LabelEdid.AutoSize = True
        LabelEdid.Location = New Point(3, 7)
        LabelEdid.Name = "LabelEdid"
        LabelEdid.Size = New Size(48, 15)
        LabelEdid.TabIndex = 0
        LabelEdid.Text = "EditorID:"
        '
        ' TextBoxEdid
        '
        TextBoxEdid.Location = New Point(57, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.PlaceholderText = "name"
        TextBoxEdid.Size = New Size(360, 23)
        TextBoxEdid.TabIndex = 1
        '
        ' LabelEdidPreview
        '
        LabelEdidPreview.Anchor = AnchorStyles.Left
        LabelEdidPreview.AutoSize = True
        LabelEdidPreview.ForeColor = SystemColors.GrayText
        LabelEdidPreview.Location = New Point(423, 7)
        LabelEdidPreview.Name = "LabelEdidPreview"
        LabelEdidPreview.Size = New Size(0, 15)
        LabelEdidPreview.TabIndex = 2
        '
        ' LabelHint
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.Location = New Point(11, 40)
        LabelHint.Name = "LabelHint"
        LabelHint.Size = New Size(560, 15)
        LabelHint.TabIndex = 1
        LabelHint.Text = "The grid is read-only. Use Add / Edit (or double-click a row) to edit a substitution in a dialog. Original = a mesh material; Replacement = the swapped-in material."
        '
        ' GridSubs
        '
        GridSubs.AllowUserToAddRows = False
        GridSubs.AllowUserToDeleteRows = False
        GridSubs.AllowUserToResizeRows = False
        GridSubs.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize
        GridSubs.Dock = DockStyle.Fill
        GridSubs.EditMode = DataGridViewEditMode.EditProgrammatically
        GridSubs.Location = New Point(11, 58)
        GridSubs.MultiSelect = False
        GridSubs.Name = "GridSubs"
        GridSubs.ReadOnly = True
        GridSubs.RowHeadersWidth = 25
        GridSubs.SelectionMode = DataGridViewSelectionMode.FullRowSelect
        GridSubs.Size = New Size(798, 340)
        GridSubs.TabIndex = 2
        '
        ' ButtonsRow
        '
        ButtonsRow.AutoSize = True
        ButtonsRow.Controls.Add(ButtonAddRow)
        ButtonsRow.Controls.Add(ButtonEditRow)
        ButtonsRow.Controls.Add(ButtonRemoveRow)
        ButtonsRow.Dock = DockStyle.Fill
        ButtonsRow.Location = New Point(8, 401)
        ButtonsRow.Margin = New Padding(0)
        ButtonsRow.Name = "ButtonsRow"
        ButtonsRow.Size = New Size(804, 32)
        ButtonsRow.TabIndex = 3
        ButtonsRow.WrapContents = False
        '
        ' ButtonAddRow
        '
        ButtonAddRow.Location = New Point(3, 3)
        ButtonAddRow.Name = "ButtonAddRow"
        ButtonAddRow.Size = New Size(90, 25)
        ButtonAddRow.TabIndex = 0
        ButtonAddRow.Text = "Add…"
        ButtonAddRow.UseVisualStyleBackColor = True
        '
        ' ButtonEditRow
        '
        ButtonEditRow.Location = New Point(99, 3)
        ButtonEditRow.Name = "ButtonEditRow"
        ButtonEditRow.Size = New Size(90, 25)
        ButtonEditRow.TabIndex = 1
        ButtonEditRow.Text = "Edit…"
        ButtonEditRow.UseVisualStyleBackColor = True
        '
        ' ButtonRemoveRow
        '
        ButtonRemoveRow.Location = New Point(195, 3)
        ButtonRemoveRow.Name = "ButtonRemoveRow"
        ButtonRemoveRow.Size = New Size(90, 25)
        ButtonRemoveRow.TabIndex = 2
        ButtonRemoveRow.Text = "Remove"
        ButtonRemoveRow.UseVisualStyleBackColor = True
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(8, 436)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Padding = New Padding(0, 6, 0, 0)
        BottomLayout.Size = New Size(804, 36)
        BottomLayout.TabIndex = 4
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(721, 9)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 23)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(635, 9)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' MswpSubEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(820, 480)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "MswpSubEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Material Swap (MSWP)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        EdidRow.ResumeLayout(False)
        EdidRow.PerformLayout()
        CType(GridSubs, ComponentModel.ISupportInitialize).EndInit()
        ButtonsRow.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents EdidRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelEdid As System.Windows.Forms.Label
    Friend WithEvents TextBoxEdid As System.Windows.Forms.TextBox
    Friend WithEvents LabelEdidPreview As System.Windows.Forms.Label
    Friend WithEvents LabelHint As System.Windows.Forms.Label
    Friend WithEvents GridSubs As System.Windows.Forms.DataGridView
    Friend WithEvents ButtonsRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonAddRow As System.Windows.Forms.Button
    Friend WithEvents ButtonEditRow As System.Windows.Forms.Button
    Friend WithEvents ButtonRemoveRow As System.Windows.Forms.Button
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
