' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE ARMO_DamageResist (DMGT FormID + Value) of an ARMO's DAMA block —
' mirror of ArmoAddonEditor_Form so the "Damage Resist" grid stays pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ArmoDamageResistEditor_Form
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
        LabelDamageType = New Label()
        TextBoxDamageType = New TextBox()
        ButtonPickDamageType = New Button()
        LabelValue = New Label()
        NumValue = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 175F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        RootLayout.Controls.Add(LabelDamageType, 0, 0)
        RootLayout.Controls.Add(TextBoxDamageType, 1, 0)
        RootLayout.Controls.Add(ButtonPickDamageType, 2, 0)
        RootLayout.Controls.Add(LabelValue, 0, 1)
        RootLayout.Controls.Add(NumValue, 1, 1)
        RootLayout.Controls.Add(BottomLayout, 1, 2)
        RootLayout.SetColumnSpan(BottomLayout, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 3
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 130)
        RootLayout.TabIndex = 0
        '
        ' LabelDamageType
        '
        LabelDamageType.Anchor = AnchorStyles.Left
        LabelDamageType.AutoSize = True
        LabelDamageType.Location = New Point(13, 18)
        LabelDamageType.Name = "LabelDamageType"
        LabelDamageType.Size = New Size(140, 15)
        LabelDamageType.TabIndex = 0
        LabelDamageType.Text = "Damage Type (DMGT):"
        '
        ' TextBoxDamageType
        '
        TextBoxDamageType.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxDamageType.Location = New Point(188, 14)
        TextBoxDamageType.Name = "TextBoxDamageType"
        TextBoxDamageType.ReadOnly = True
        TextBoxDamageType.Size = New Size(279, 23)
        TextBoxDamageType.TabIndex = 1
        '
        ' ButtonPickDamageType
        '
        ButtonPickDamageType.Anchor = AnchorStyles.Left
        ButtonPickDamageType.Location = New Point(473, 13)
        ButtonPickDamageType.Name = "ButtonPickDamageType"
        ButtonPickDamageType.Size = New Size(34, 24)
        ButtonPickDamageType.TabIndex = 2
        ButtonPickDamageType.Text = "…"
        ButtonPickDamageType.UseVisualStyleBackColor = True
        '
        ' LabelValue
        '
        LabelValue.Anchor = AnchorStyles.Left
        LabelValue.AutoSize = True
        LabelValue.Location = New Point(13, 49)
        LabelValue.Name = "LabelValue"
        LabelValue.Size = New Size(40, 15)
        LabelValue.TabIndex = 3
        LabelValue.Text = "Value:"
        '
        ' NumValue
        '
        NumValue.Anchor = AnchorStyles.Left
        NumValue.Location = New Point(188, 45)
        NumValue.Maximum = New Decimal(UInteger.MaxValue)
        NumValue.Name = "NumValue"
        NumValue.Size = New Size(130, 23)
        NumValue.TabIndex = 4
        NumValue.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(188, 102)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(319, 25)
        BottomLayout.TabIndex = 5
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(236, 0)
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
        ButtonCancel.Location = New Point(150, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ArmoDamageResistEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 130)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ArmoDamageResistEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Damage Resistance (DAMA)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumValue, ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelDamageType As System.Windows.Forms.Label
    Friend WithEvents TextBoxDamageType As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickDamageType As System.Windows.Forms.Button
    Friend WithEvents LabelValue As System.Windows.Forms.Label
    Friend WithEvents NumValue As System.Windows.Forms.NumericUpDown
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
