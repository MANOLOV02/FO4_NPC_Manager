<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class AnimationPicker_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        RootLayout = New System.Windows.Forms.TableLayoutPanel()
        FilterRow = New System.Windows.Forms.FlowLayoutPanel()
        LabelFilter = New System.Windows.Forms.Label()
        TextFilter = New System.Windows.Forms.TextBox()
        LabelCount = New System.Windows.Forms.Label()
        CheckFilterGender = New System.Windows.Forms.CheckBox()
        CheckShow1stPerson = New System.Windows.Forms.CheckBox()
        TreeClips = New System.Windows.Forms.TreeView()
        ButtonRow = New System.Windows.Forms.FlowLayoutPanel()
        ButtonCancel = New System.Windows.Forms.Button()
        ButtonOk = New System.Windows.Forms.Button()
        RootLayout.SuspendLayout()
        FilterRow.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F))
        RootLayout.Controls.Add(FilterRow, 0, 0)
        RootLayout.Controls.Add(TreeClips, 0, 1)
        RootLayout.Controls.Add(ButtonRow, 0, 2)
        RootLayout.Dock = System.Windows.Forms.DockStyle.Fill
        RootLayout.Location = New System.Drawing.Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle())
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle(System.Windows.Forms.SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New System.Windows.Forms.RowStyle())
        RootLayout.Size = New System.Drawing.Size(640, 460)
        RootLayout.TabIndex = 0
        '
        ' FilterRow
        '
        FilterRow.Controls.Add(LabelFilter)
        FilterRow.Controls.Add(TextFilter)
        FilterRow.Controls.Add(CheckFilterGender)
        FilterRow.Controls.Add(CheckShow1stPerson)
        FilterRow.Controls.Add(LabelCount)
        FilterRow.Dock = System.Windows.Forms.DockStyle.Fill
        FilterRow.Location = New System.Drawing.Point(3, 3)
        FilterRow.Name = "FilterRow"
        FilterRow.Padding = New System.Windows.Forms.Padding(2)
        FilterRow.Size = New System.Drawing.Size(634, 30)
        FilterRow.TabIndex = 0
        FilterRow.WrapContents = False
        '
        ' LabelFilter
        '
        LabelFilter.Anchor = System.Windows.Forms.AnchorStyles.Left
        LabelFilter.AutoSize = True
        LabelFilter.Margin = New System.Windows.Forms.Padding(3, 6, 3, 0)
        LabelFilter.Name = "LabelFilter"
        LabelFilter.Text = "Filter:"
        '
        ' TextFilter
        '
        TextFilter.Margin = New System.Windows.Forms.Padding(3, 3, 12, 3)
        TextFilter.Name = "TextFilter"
        TextFilter.Size = New System.Drawing.Size(280, 23)
        TextFilter.TabIndex = 0
        '
        ' LabelCount
        '
        LabelCount.Anchor = System.Windows.Forms.AnchorStyles.Left
        LabelCount.AutoSize = True
        LabelCount.Margin = New System.Windows.Forms.Padding(3, 6, 3, 0)
        LabelCount.Name = "LabelCount"
        LabelCount.Text = "0 clips"
        '
        ' CheckFilterGender
        '
        CheckFilterGender.Anchor = System.Windows.Forms.AnchorStyles.Left
        CheckFilterGender.AutoSize = True
        CheckFilterGender.Checked = True
        CheckFilterGender.CheckState = System.Windows.Forms.CheckState.Checked
        CheckFilterGender.Margin = New System.Windows.Forms.Padding(3, 5, 12, 0)
        CheckFilterGender.Name = "CheckFilterGender"
        CheckFilterGender.Text = "Filter by gender"
        CheckFilterGender.UseVisualStyleBackColor = True
        '
        ' CheckShow1stPerson
        '
        CheckShow1stPerson.Anchor = System.Windows.Forms.AnchorStyles.Left
        CheckShow1stPerson.AutoSize = True
        CheckShow1stPerson.Checked = False
        CheckShow1stPerson.CheckState = System.Windows.Forms.CheckState.Unchecked
        CheckShow1stPerson.Margin = New System.Windows.Forms.Padding(3, 5, 12, 0)
        CheckShow1stPerson.Name = "CheckShow1stPerson"
        CheckShow1stPerson.Text = "Show 1st-person/camera"
        CheckShow1stPerson.UseVisualStyleBackColor = True
        '
        ' TreeClips
        '
        TreeClips.Dock = System.Windows.Forms.DockStyle.Fill
        TreeClips.HideSelection = False
        TreeClips.Location = New System.Drawing.Point(3, 39)
        TreeClips.Name = "TreeClips"
        TreeClips.Size = New System.Drawing.Size(634, 382)
        TreeClips.TabIndex = 1
        '
        ' ButtonRow
        '
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Controls.Add(ButtonOk)
        ButtonRow.Dock = System.Windows.Forms.DockStyle.Fill
        ButtonRow.FlowDirection = System.Windows.Forms.FlowDirection.RightToLeft
        ButtonRow.Location = New System.Drawing.Point(3, 427)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New System.Windows.Forms.Padding(2)
        ButtonRow.Size = New System.Drawing.Size(634, 30)
        ButtonRow.TabIndex = 2
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New System.Drawing.Size(90, 26)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ButtonOk
        '
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New System.Drawing.Size(90, 26)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "Select"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' AnimationPicker_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New System.Drawing.SizeF(7F, 15F)
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New System.Drawing.Size(640, 460)
        Controls.Add(RootLayout)
        MinimumSize = New System.Drawing.Size(520, 320)
        Name = "AnimationPicker_Form"
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Text = "Select Animation"
        RootLayout.ResumeLayout(False)
        FilterRow.ResumeLayout(False)
        FilterRow.PerformLayout()
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents FilterRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents LabelFilter As System.Windows.Forms.Label
    Friend WithEvents TextFilter As System.Windows.Forms.TextBox
    Friend WithEvents LabelCount As System.Windows.Forms.Label
    Friend WithEvents CheckFilterGender As System.Windows.Forms.CheckBox
    Friend WithEvents CheckShow1stPerson As System.Windows.Forms.CheckBox
    Friend WithEvents TreeClips As System.Windows.Forms.TreeView
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
End Class
