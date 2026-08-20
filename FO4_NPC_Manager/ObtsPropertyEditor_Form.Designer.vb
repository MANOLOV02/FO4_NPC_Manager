' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY.
' Modal editor for a SINGLE OMOD_Property row of an OBTS combination — replaces the old inline-editable
' GridProperties cells (ValueType combo + FunctionType/PropertyIndex/Value2/Step text cells + the
' out-of-band Value1 prompt) with dedicated controls, so the grid can be pure read-only.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ObtsPropertyEditor_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
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
        LabelValueType = New Label()
        ComboValueType = New ComboBox()
        LabelFunction = New Label()
        ComboFunction = New ComboBox()
        LabelIndex = New Label()
        NumIndex = New NumericUpDown()
        LabelValue1 = New Label()
        Value1Panel = New Panel()
        TextBoxValue1 = New TextBox()
        LabelValue1FormID = New Label()
        ButtonPickValue1 = New Button()
        LabelValue2 = New Label()
        TextBoxValue2 = New TextBox()
        LabelStep = New Label()
        TextBoxStep = New TextBox()
        LabelHint = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        CType(NumIndex, ComponentModel.ISupportInitialize).BeginInit()
        Value1Panel.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout
        '
        RootLayout.ColumnCount = 2
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 160F))
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(LabelValueType, 0, 0)
        RootLayout.Controls.Add(ComboValueType, 1, 0)
        RootLayout.Controls.Add(LabelFunction, 0, 1)
        RootLayout.Controls.Add(ComboFunction, 1, 1)
        RootLayout.Controls.Add(LabelIndex, 0, 2)
        RootLayout.Controls.Add(NumIndex, 1, 2)
        RootLayout.Controls.Add(LabelValue1, 0, 3)
        RootLayout.Controls.Add(Value1Panel, 1, 3)
        RootLayout.Controls.Add(LabelValue2, 0, 4)
        RootLayout.Controls.Add(TextBoxValue2, 1, 4)
        RootLayout.Controls.Add(LabelStep, 0, 5)
        RootLayout.Controls.Add(TextBoxStep, 1, 5)
        RootLayout.Controls.Add(LabelHint, 1, 6)
        RootLayout.Controls.Add(BottomLayout, 1, 7)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Location = New Point(0, 0)
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(10)
        RootLayout.RowCount = 8
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.Size = New Size(520, 320)
        RootLayout.TabIndex = 0
        '
        ' LabelValueType
        '
        LabelValueType.Anchor = AnchorStyles.Left
        LabelValueType.AutoSize = True
        LabelValueType.Location = New Point(13, 17)
        LabelValueType.Name = "LabelValueType"
        LabelValueType.Size = New Size(65, 15)
        LabelValueType.TabIndex = 0
        LabelValueType.Text = "ValueType:"
        '
        ' ComboValueType
        '
        ComboValueType.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ComboValueType.DropDownStyle = ComboBoxStyle.DropDownList
        ComboValueType.Location = New Point(173, 13)
        ComboValueType.Name = "ComboValueType"
        ComboValueType.Size = New Size(330, 23)
        ComboValueType.TabIndex = 1
        '
        ' LabelFunction
        '
        LabelFunction.Anchor = AnchorStyles.Left
        LabelFunction.AutoSize = True
        LabelFunction.Location = New Point(13, 48)
        LabelFunction.Name = "LabelFunction"
        LabelFunction.Size = New Size(130, 15)
        LabelFunction.TabIndex = 2
        LabelFunction.Text = "FunctionType:"
        '
        ' ComboFunction
        '
        ComboFunction.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ComboFunction.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFunction.Location = New Point(173, 44)
        ComboFunction.Name = "ComboFunction"
        ComboFunction.Size = New Size(330, 23)
        ComboFunction.TabIndex = 3
        '
        ' LabelIndex
        '
        LabelIndex.Anchor = AnchorStyles.Left
        LabelIndex.AutoSize = True
        LabelIndex.Location = New Point(13, 79)
        LabelIndex.Name = "LabelIndex"
        LabelIndex.Size = New Size(150, 15)
        LabelIndex.TabIndex = 4
        LabelIndex.Text = "Property Index (raw 0-65535):"
        '
        ' NumIndex
        '
        NumIndex.Anchor = AnchorStyles.Left
        NumIndex.Location = New Point(173, 75)
        NumIndex.Maximum = New Decimal(New Integer() {65535, 0, 0, 0})
        NumIndex.Name = "NumIndex"
        NumIndex.Size = New Size(110, 23)
        NumIndex.TabIndex = 5
        NumIndex.TextAlign = System.Windows.Forms.HorizontalAlignment.Right
        '
        ' LabelValue1
        '
        LabelValue1.Anchor = AnchorStyles.Left
        LabelValue1.AutoSize = True
        LabelValue1.Location = New Point(13, 110)
        LabelValue1.Name = "LabelValue1"
        LabelValue1.Size = New Size(48, 15)
        LabelValue1.TabIndex = 6
        LabelValue1.Text = "Value1:"
        '
        ' Value1Panel
        '
        Value1Panel.Controls.Add(TextBoxValue1)
        Value1Panel.Controls.Add(LabelValue1FormID)
        Value1Panel.Controls.Add(ButtonPickValue1)
        Value1Panel.Dock = DockStyle.Fill
        Value1Panel.Location = New Point(170, 107)
        Value1Panel.Margin = New Padding(0)
        Value1Panel.Name = "Value1Panel"
        Value1Panel.Size = New Size(340, 30)
        Value1Panel.TabIndex = 7
        '
        ' TextBoxValue1
        '
        TextBoxValue1.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxValue1.Location = New Point(3, 3)
        TextBoxValue1.Name = "TextBoxValue1"
        TextBoxValue1.Size = New Size(330, 23)
        TextBoxValue1.TabIndex = 0
        '
        ' LabelValue1FormID
        '
        LabelValue1FormID.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        LabelValue1FormID.AutoEllipsis = True
        LabelValue1FormID.BorderStyle = BorderStyle.FixedSingle
        LabelValue1FormID.Location = New Point(3, 4)
        LabelValue1FormID.Name = "LabelValue1FormID"
        LabelValue1FormID.Size = New Size(240, 21)
        LabelValue1FormID.TabIndex = 1
        LabelValue1FormID.Text = "(none)"
        LabelValue1FormID.TextAlign = ContentAlignment.MiddleLeft
        '
        ' ButtonPickValue1
        '
        ButtonPickValue1.Anchor = AnchorStyles.Right
        ButtonPickValue1.Location = New Point(249, 3)
        ButtonPickValue1.Name = "ButtonPickValue1"
        ButtonPickValue1.Size = New Size(84, 24)
        ButtonPickValue1.TabIndex = 2
        ButtonPickValue1.Text = "Choose…"
        ButtonPickValue1.UseVisualStyleBackColor = True
        '
        ' LabelValue2
        '
        LabelValue2.Anchor = AnchorStyles.Left
        LabelValue2.AutoSize = True
        LabelValue2.Location = New Point(13, 147)
        LabelValue2.Name = "LabelValue2"
        LabelValue2.Size = New Size(90, 15)
        LabelValue2.TabIndex = 8
        LabelValue2.Text = "Value2:"
        '
        ' TextBoxValue2
        '
        TextBoxValue2.Anchor = AnchorStyles.Left
        TextBoxValue2.Location = New Point(173, 143)
        TextBoxValue2.Name = "TextBoxValue2"
        TextBoxValue2.Size = New Size(150, 23)
        TextBoxValue2.TabIndex = 9
        '
        ' LabelStep
        '
        LabelStep.Anchor = AnchorStyles.Left
        LabelStep.AutoSize = True
        LabelStep.Location = New Point(13, 178)
        LabelStep.Name = "LabelStep"
        LabelStep.Size = New Size(75, 15)
        LabelStep.TabIndex = 10
        LabelStep.Text = "Step (float):"
        '
        ' TextBoxStep
        '
        TextBoxStep.Anchor = AnchorStyles.Left
        TextBoxStep.Location = New Point(173, 174)
        TextBoxStep.Name = "TextBoxStep"
        TextBoxStep.Size = New Size(150, 23)
        TextBoxStep.TabIndex = 11
        '
        ' LabelHint
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.Location = New Point(170, 205)
        LabelHint.Margin = New Padding(0, 6, 3, 0)
        LabelHint.MaximumSize = New Size(330, 0)
        LabelHint.Name = "LabelHint"
        LabelHint.Size = New Size(320, 30)
        LabelHint.TabIndex = 12
        LabelHint.Text = "Value1 is FormID-picked for FormID Int/Float; a float for FloatType; else an integer (stored as raw 4-byte bits)."
        '
        ' BottomLayout
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Dock = DockStyle.Bottom
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Location = New Point(170, 285)
        BottomLayout.Margin = New Padding(0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.Size = New Size(340, 25)
        BottomLayout.TabIndex = 13
        '
        ' ButtonOk
        '
        ButtonOk.Location = New Point(257, 0)
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
        ButtonCancel.Location = New Point(171, 0)
        ButtonCancel.Margin = New Padding(3, 0, 3, 0)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 25)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ObtsPropertyEditor_Form
        '
        AcceptButton = ButtonOk
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(520, 320)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9F)
        MinimizeBox = False
        Name = "ObtsPropertyEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "OBTS Property"
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        CType(NumIndex, ComponentModel.ISupportInitialize).EndInit()
        Value1Panel.ResumeLayout(False)
        Value1Panel.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelValueType As System.Windows.Forms.Label
    Friend WithEvents ComboValueType As System.Windows.Forms.ComboBox
    Friend WithEvents LabelFunction As System.Windows.Forms.Label
    Friend WithEvents ComboFunction As System.Windows.Forms.ComboBox
    Friend WithEvents LabelIndex As System.Windows.Forms.Label
    Friend WithEvents NumIndex As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelValue1 As System.Windows.Forms.Label
    Friend WithEvents Value1Panel As System.Windows.Forms.Panel
    Friend WithEvents TextBoxValue1 As System.Windows.Forms.TextBox
    Friend WithEvents LabelValue1FormID As System.Windows.Forms.Label
    Friend WithEvents ButtonPickValue1 As System.Windows.Forms.Button
    Friend WithEvents LabelValue2 As System.Windows.Forms.Label
    Friend WithEvents TextBoxValue2 As System.Windows.Forms.TextBox
    Friend WithEvents LabelStep As System.Windows.Forms.Label
    Friend WithEvents TextBoxStep As System.Windows.Forms.TextBox
    Friend WithEvents LabelHint As System.Windows.Forms.Label
    Friend WithEvents BottomLayout As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonOk As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
