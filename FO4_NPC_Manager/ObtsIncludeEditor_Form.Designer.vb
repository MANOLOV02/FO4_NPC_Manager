' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE ARMO_CombinationInclude (OMOD include) of an OBTS combination — replaces the old
' inline-editable GridIncludes cells (AttachPointIndex text + Optional/DontUseAll checkboxes + the OMOD
' double-click re-pick), so the Includes grid can be pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ObtsIncludeEditor_Form
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
        LabelOmod = New Label()
        OmodPanel = New Panel()
        LabelOmodValue = New Label()
        ButtonPickOmod = New Button()
        LabelAttach = New Label()
        NumAttach = New NumericUpDown()
        CheckOptional = New CheckBox()
        CheckDontUseAll = New CheckBox()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumAttach, ComponentModel.ISupportInitialize).BeginInit()
        OmodPanel.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 2
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelOmod, 0, 0)
        RootLayout.Controls.Add(OmodPanel, 1, 0)
        RootLayout.Controls.Add(LabelAttach, 0, 1)
        RootLayout.Controls.Add(NumAttach, 1, 1)
        RootLayout.Controls.Add(CheckOptional, 1, 2)
        RootLayout.Controls.Add(CheckDontUseAll, 1, 3)
        RootLayout.Controls.Add(BottomLayout, 1, 4)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 5
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 210)
        RootLayout.TabIndex = 0
        '
        ' LabelOmod
        '
        LabelOmod.Anchor = AnchorStyles.Left
        LabelOmod.AutoSize = True
        LabelOmod.Location = New Point(13, 18)
        LabelOmod.Name = "LabelOmod"
        LabelOmod.Size = New Size(120, 15)
        LabelOmod.TabIndex = 0
        LabelOmod.Text = "OMOD (Mod FormID):"
        '
        ' OmodPanel
        '
        OmodPanel.Controls.Add(LabelOmodValue)
        OmodPanel.Controls.Add(ButtonPickOmod)
        OmodPanel.Dock = DockStyle.Fill
        OmodPanel.Location = New Point(185, 10)
        OmodPanel.Margin = New Padding(0)
        OmodPanel.Name = "OmodPanel"
        OmodPanel.Size = New Size(325, 30)
        OmodPanel.TabIndex = 1
        '
        ' LabelOmodValue
        '
        LabelOmodValue.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        LabelOmodValue.AutoEllipsis = True
        LabelOmodValue.BorderStyle = BorderStyle.FixedSingle
        LabelOmodValue.Location = New Point(3, 4)
        LabelOmodValue.Name = "LabelOmodValue"
        LabelOmodValue.Size = New Size(225, 21)
        LabelOmodValue.TabIndex = 0
        LabelOmodValue.Text = "(none)"
        LabelOmodValue.TextAlign = ContentAlignment.MiddleLeft
        '
        ' ButtonPickOmod
        '
        ButtonPickOmod.Anchor = AnchorStyles.Right
        ButtonPickOmod.Location = New Point(234, 3)
        ButtonPickOmod.Name = "ButtonPickOmod"
        ButtonPickOmod.Size = New Size(84, 24)
        ButtonPickOmod.TabIndex = 1
        ButtonPickOmod.Text = "Choose…"
        ButtonPickOmod.UseVisualStyleBackColor = True
        '
        ' LabelAttach
        '
        LabelAttach.Anchor = AnchorStyles.Left
        LabelAttach.AutoSize = True
        LabelAttach.Location = New Point(13, 49)
        LabelAttach.Name = "LabelAttach"
        LabelAttach.Size = New Size(165, 15)
        LabelAttach.TabIndex = 2
        LabelAttach.Text = "Attach Point Index:"
        '
        ' NumAttach
        '
        NumAttach.Anchor = AnchorStyles.Left
        NumAttach.Location = New Point(185, 45)
        NumAttach.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumAttach.Minimum = New Decimal(New Integer() {0, 0, 0, 0})
        NumAttach.Name = "NumAttach"
        NumAttach.Size = New Size(110, 23)
        NumAttach.TabIndex = 3
        NumAttach.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' CheckOptional
        '
        CheckOptional.Anchor = AnchorStyles.Left
        CheckOptional.AutoSize = True
        CheckOptional.Location = New Point(185, 77)
        CheckOptional.Name = "CheckOptional"
        CheckOptional.Size = New Size(75, 19)
        CheckOptional.TabIndex = 4
        CheckOptional.Text = "Optional"
        CheckOptional.UseVisualStyleBackColor = True
        '
        ' CheckDontUseAll
        '
        CheckDontUseAll.Anchor = AnchorStyles.Left
        CheckDontUseAll.AutoSize = True
        CheckDontUseAll.Location = New Point(185, 102)
        CheckDontUseAll.Name = "CheckDontUseAll"
        CheckDontUseAll.Size = New Size(95, 19)
        CheckDontUseAll.TabIndex = 5
        CheckDontUseAll.Text = "Don't Use All"
        CheckDontUseAll.UseVisualStyleBackColor = True
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(185, 175)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(325, 25)
        BottomLayout.TabIndex = 6
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(242, 0)
        ButtonOk.Margin = New Padding(3, 0, 3, 0)
        ButtonOk.Name = "ButtonOk"
        ButtonOk.Size = New Size(80, 25)
        ButtonOk.TabIndex = 0
        ButtonOk.Text = "OK"
        ButtonOk.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(156, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ObtsIncludeEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 210)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ObtsIncludeEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "OBTS Include (OMOD)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumAttach, ComponentModel.ISupportInitialize).EndInit()
        OmodPanel.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelOmod As System.Windows.Forms.Label
    Friend WithEvents OmodPanel As System.Windows.Forms.Panel
    Friend WithEvents LabelOmodValue As System.Windows.Forms.Label
    Friend WithEvents ButtonPickOmod As System.Windows.Forms.Button
    Friend WithEvents LabelAttach As System.Windows.Forms.Label
    Friend WithEvents NumAttach As System.Windows.Forms.NumericUpDown
    Friend WithEvents CheckOptional As System.Windows.Forms.CheckBox
    Friend WithEvents CheckDontUseAll As System.Windows.Forms.CheckBox
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
