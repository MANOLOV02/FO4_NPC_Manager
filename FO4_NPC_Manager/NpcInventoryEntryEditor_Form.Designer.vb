' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE NPC_InventoryItem (Item FormID + s32 Count) of an NPC's CNTO inventory list —
' mirror of ArmoDamageResistEditor_Form so the "Inventory" grid stays pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class NpcInventoryEntryEditor_Form
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
        LabelItem = New Label()
        TextBoxItem = New TextBox()
        ButtonPickItem = New Button()
        LabelCount = New Label()
        NumCount = New NumericUpDown()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumCount, ComponentModel.ISupportInitialize).BeginInit()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 140F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 40F))
        RootLayout.Controls.Add(LabelItem, 0, 0)
        RootLayout.Controls.Add(TextBoxItem, 1, 0)
        RootLayout.Controls.Add(ButtonPickItem, 2, 0)
        RootLayout.Controls.Add(LabelCount, 0, 1)
        RootLayout.Controls.Add(NumCount, 1, 1)
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
        ' LabelItem
        '
        LabelItem.Anchor = AnchorStyles.Left
        LabelItem.AutoSize = True
        LabelItem.Location = New Point(13, 18)
        LabelItem.Name = "LabelItem"
        LabelItem.Size = New Size(90, 15)
        LabelItem.TabIndex = 0
        LabelItem.Text = "Item (CNTO):"
        '
        ' TextBoxItem
        '
        TextBoxItem.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxItem.Location = New Point(153, 14)
        TextBoxItem.Name = "TextBoxItem"
        TextBoxItem.ReadOnly = True
        TextBoxItem.Size = New Size(314, 23)
        TextBoxItem.TabIndex = 1
        '
        ' ButtonPickItem
        '
        ButtonPickItem.Anchor = AnchorStyles.Left
        ButtonPickItem.Location = New Point(473, 13)
        ButtonPickItem.Name = "ButtonPickItem"
        ButtonPickItem.Size = New Size(34, 24)
        ButtonPickItem.TabIndex = 2
        ButtonPickItem.Text = "…"
        ButtonPickItem.UseVisualStyleBackColor = True
        '
        ' LabelCount
        '
        LabelCount.Anchor = AnchorStyles.Left
        LabelCount.AutoSize = True
        LabelCount.Location = New Point(13, 49)
        LabelCount.Name = "LabelCount"
        LabelCount.Size = New Size(45, 15)
        LabelCount.TabIndex = 3
        LabelCount.Text = "Count:"
        '
        ' NumCount
        '
        NumCount.Anchor = AnchorStyles.Left
        NumCount.Location = New Point(153, 45)
        NumCount.Maximum = New Decimal(Integer.MaxValue)
        NumCount.Minimum = New Decimal(Integer.MinValue)
        NumCount.Name = "NumCount"
        NumCount.Size = New Size(130, 23)
        NumCount.TabIndex = 4
        NumCount.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        NumCount.Value = New Decimal(1)
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(153, 102)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(354, 25)
        BottomLayout.TabIndex = 5
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(271, 0)
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
        ButtonCancel.Location = New Point(185, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' NpcInventoryEntryEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 130)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "NpcInventoryEntryEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Inventory Item (CNTO)"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumCount, ComponentModel.ISupportInitialize).EndInit()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelItem As System.Windows.Forms.Label
    Friend WithEvents TextBoxItem As System.Windows.Forms.TextBox
    Friend WithEvents ButtonPickItem As System.Windows.Forms.Button
    Friend WithEvents LabelCount As System.Windows.Forms.Label
    Friend WithEvents NumCount As System.Windows.Forms.NumericUpDown
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
