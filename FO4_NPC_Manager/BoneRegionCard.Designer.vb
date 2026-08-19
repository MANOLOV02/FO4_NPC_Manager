' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class BoneRegionCard
    Inherits System.Windows.Forms.UserControl

    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then components.Dispose()
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    ' ⛔ LAS MEDIDAS NO SE REDONDEAN: son las mismas que calculaba BuildBoneCard.
    '   RowH = 26  >= TinySliderTextBox.MinimumSize.Height (24). Una fila mas baja clava el slider en su
    '          minimo mientras el boton se encoge, y dejan de coincidir.
    '   HdrH = 18  para los tres encabezados de seccion.
    '   Alto de la tarjeta = 3*18 + 7*26 + 34 = 270, que es el `contentH + 34` del codigo anterior.
    '   Ancho 230.
    ' El boton mide 22 de alto (= alto del TextBox interno del slider) para que los dos queden alineados
    ' arriba.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        components = New System.ComponentModel.Container()
        GroupBoxCard = New GroupBox()
        LayoutCard = New TableLayoutPanel()
        LabelTranslation = New Label()
        ButtonResetPosX = New Button()
        SliderPosX = New FO4_Base_Library.TinySliderTextBox()
        ButtonResetPosY = New Button()
        SliderPosY = New FO4_Base_Library.TinySliderTextBox()
        ButtonResetPosZ = New Button()
        SliderPosZ = New FO4_Base_Library.TinySliderTextBox()
        LabelRotation = New Label()
        ButtonResetRotX = New Button()
        SliderRotX = New FO4_Base_Library.TinySliderTextBox()
        ButtonResetRotY = New Button()
        SliderRotY = New FO4_Base_Library.TinySliderTextBox()
        ButtonResetRotZ = New Button()
        SliderRotZ = New FO4_Base_Library.TinySliderTextBox()
        LabelScale = New Label()
        ButtonResetScale = New Button()
        SliderScale = New FO4_Base_Library.TinySliderTextBox()
        ToolTipCard = New ToolTip(components)
        GroupBoxCard.SuspendLayout()
        LayoutCard.SuspendLayout()
        SuspendLayout()
        '
        ' GroupBoxCard
        '
        GroupBoxCard.Controls.Add(LayoutCard)
        GroupBoxCard.Dock = DockStyle.Fill
        GroupBoxCard.Location = New Point(0, 0)
        GroupBoxCard.Name = "GroupBoxCard"
        GroupBoxCard.Padding = New Padding(6, 4, 6, 4)
        GroupBoxCard.Size = New Size(230, 270)
        GroupBoxCard.TabIndex = 0
        GroupBoxCard.TabStop = False
        '
        ' LayoutCard
        '
        LayoutCard.AutoSize = False
        LayoutCard.ColumnCount = 2
        LayoutCard.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 26.0F))
        LayoutCard.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        LayoutCard.Controls.Add(LabelTranslation, 0, 0)
        LayoutCard.Controls.Add(ButtonResetPosX, 0, 1)
        LayoutCard.Controls.Add(SliderPosX, 1, 1)
        LayoutCard.Controls.Add(ButtonResetPosY, 0, 2)
        LayoutCard.Controls.Add(SliderPosY, 1, 2)
        LayoutCard.Controls.Add(ButtonResetPosZ, 0, 3)
        LayoutCard.Controls.Add(SliderPosZ, 1, 3)
        LayoutCard.Controls.Add(LabelRotation, 0, 4)
        LayoutCard.Controls.Add(ButtonResetRotX, 0, 5)
        LayoutCard.Controls.Add(SliderRotX, 1, 5)
        LayoutCard.Controls.Add(ButtonResetRotY, 0, 6)
        LayoutCard.Controls.Add(SliderRotY, 1, 6)
        LayoutCard.Controls.Add(ButtonResetRotZ, 0, 7)
        LayoutCard.Controls.Add(SliderRotZ, 1, 7)
        LayoutCard.Controls.Add(LabelScale, 0, 8)
        LayoutCard.Controls.Add(ButtonResetScale, 0, 9)
        LayoutCard.Controls.Add(SliderScale, 1, 9)
        LayoutCard.Dock = DockStyle.Fill
        LayoutCard.Location = New Point(6, 19)
        LayoutCard.Margin = New Padding(0)
        LayoutCard.Name = "LayoutCard"
        LayoutCard.RowCount = 11
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 18.0F))
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Absolute, 26.0F))
        ' ⛔ Ultima fila FLEXIBLE, no AutoSize: absorbe el alto que sobra en una tarjeta de tamano fijo para
        ' que las filas de arriba conserven su alto exacto (los botones siguen alineados con los sliders)
        ' en vez de estirarse la ultima. Una fila AutoSize vacia mide 0 y no absorberia nada.
        LayoutCard.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        LayoutCard.Size = New Size(218, 247)
        LayoutCard.TabIndex = 0
        '
        ' LabelTranslation
        '
        LabelTranslation.AutoSize = False
        LabelTranslation.Dock = DockStyle.Fill
        LabelTranslation.Font = New Font(Font, FontStyle.Bold)
        LabelTranslation.Location = New Point(0, 0)
        LabelTranslation.Margin = New Padding(0)
        LabelTranslation.Name = "LabelTranslation"
        LabelTranslation.Size = New Size(218, 18)
        LabelTranslation.TabIndex = 0
        LabelTranslation.Text = "Translation"
        LabelTranslation.TextAlign = ContentAlignment.MiddleCenter
        LayoutCard.SetColumnSpan(LabelTranslation, 2)
        '
        ' ButtonResetPosX
        '
        ButtonResetPosX.Dock = DockStyle.Top
        ButtonResetPosX.Location = New Point(0, 19)
        ButtonResetPosX.Margin = New Padding(0, 1, 2, 1)
        ButtonResetPosX.Name = "ButtonResetPosX"
        ButtonResetPosX.Padding = New Padding(0)
        ButtonResetPosX.Size = New Size(24, 22)
        ButtonResetPosX.TabIndex = 1
        ButtonResetPosX.TabStop = False
        ButtonResetPosX.Text = "X"
        '
        ' SliderPosX
        '
        SliderPosX.Dock = DockStyle.Fill
        SliderPosX.DisplayFormat = "0.00%"
        SliderPosX.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderPosX.InputScale = 0.01R
        SliderPosX.LargeChange = 0.1R
        SliderPosX.Location = New Point(26, 19)
        SliderPosX.Margin = New Padding(0, 1, 0, 1)
        SliderPosX.Maximum = 1.0R
        SliderPosX.Minimum = -1.0R
        SliderPosX.Name = "SliderPosX"
        SliderPosX.Size = New Size(192, 24)
        SliderPosX.SmallChange = 0.01R
        SliderPosX.TabIndex = 2
        '
        ' ButtonResetPosY
        '
        ButtonResetPosY.Dock = DockStyle.Top
        ButtonResetPosY.Location = New Point(0, 45)
        ButtonResetPosY.Margin = New Padding(0, 1, 2, 1)
        ButtonResetPosY.Name = "ButtonResetPosY"
        ButtonResetPosY.Padding = New Padding(0)
        ButtonResetPosY.Size = New Size(24, 22)
        ButtonResetPosY.TabIndex = 3
        ButtonResetPosY.TabStop = False
        ButtonResetPosY.Text = "Y"
        '
        ' SliderPosY
        '
        SliderPosY.Dock = DockStyle.Fill
        SliderPosY.DisplayFormat = "0.00%"
        SliderPosY.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderPosY.InputScale = 0.01R
        SliderPosY.LargeChange = 0.1R
        SliderPosY.Location = New Point(26, 45)
        SliderPosY.Margin = New Padding(0, 1, 0, 1)
        SliderPosY.Maximum = 1.0R
        SliderPosY.Minimum = -1.0R
        SliderPosY.Name = "SliderPosY"
        SliderPosY.Size = New Size(192, 24)
        SliderPosY.SmallChange = 0.01R
        SliderPosY.TabIndex = 4
        '
        ' ButtonResetPosZ
        '
        ButtonResetPosZ.Dock = DockStyle.Top
        ButtonResetPosZ.Location = New Point(0, 71)
        ButtonResetPosZ.Margin = New Padding(0, 1, 2, 1)
        ButtonResetPosZ.Name = "ButtonResetPosZ"
        ButtonResetPosZ.Padding = New Padding(0)
        ButtonResetPosZ.Size = New Size(24, 22)
        ButtonResetPosZ.TabIndex = 5
        ButtonResetPosZ.TabStop = False
        ButtonResetPosZ.Text = "Z"
        '
        ' SliderPosZ
        '
        SliderPosZ.Dock = DockStyle.Fill
        SliderPosZ.DisplayFormat = "0.00%"
        SliderPosZ.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderPosZ.InputScale = 0.01R
        SliderPosZ.LargeChange = 0.1R
        SliderPosZ.Location = New Point(26, 71)
        SliderPosZ.Margin = New Padding(0, 1, 0, 1)
        SliderPosZ.Maximum = 1.0R
        SliderPosZ.Minimum = -1.0R
        SliderPosZ.Name = "SliderPosZ"
        SliderPosZ.Size = New Size(192, 24)
        SliderPosZ.SmallChange = 0.01R
        SliderPosZ.TabIndex = 6
        '
        ' LabelRotation
        '
        LabelRotation.AutoSize = False
        LabelRotation.Dock = DockStyle.Fill
        LabelRotation.Font = New Font(Font, FontStyle.Bold)
        LabelRotation.Location = New Point(0, 96)
        LabelRotation.Margin = New Padding(0)
        LabelRotation.Name = "LabelRotation"
        LabelRotation.Size = New Size(218, 18)
        LabelRotation.TabIndex = 7
        LabelRotation.Text = "Rotation"
        LabelRotation.TextAlign = ContentAlignment.MiddleCenter
        LayoutCard.SetColumnSpan(LabelRotation, 2)
        '
        ' ButtonResetRotX
        '
        ButtonResetRotX.Dock = DockStyle.Top
        ButtonResetRotX.Location = New Point(0, 115)
        ButtonResetRotX.Margin = New Padding(0, 1, 2, 1)
        ButtonResetRotX.Name = "ButtonResetRotX"
        ButtonResetRotX.Padding = New Padding(0)
        ButtonResetRotX.Size = New Size(24, 22)
        ButtonResetRotX.TabIndex = 8
        ButtonResetRotX.TabStop = False
        ButtonResetRotX.Text = "X"
        '
        ' SliderRotX
        '
        SliderRotX.Dock = DockStyle.Fill
        SliderRotX.DisplayFormat = "0.00%"
        SliderRotX.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderRotX.InputScale = 0.01R
        SliderRotX.LargeChange = 0.1R
        SliderRotX.Location = New Point(26, 115)
        SliderRotX.Margin = New Padding(0, 1, 0, 1)
        SliderRotX.Maximum = 1.0R
        SliderRotX.Minimum = -1.0R
        SliderRotX.Name = "SliderRotX"
        SliderRotX.Size = New Size(192, 24)
        SliderRotX.SmallChange = 0.01R
        SliderRotX.TabIndex = 9
        '
        ' ButtonResetRotY
        '
        ButtonResetRotY.Dock = DockStyle.Top
        ButtonResetRotY.Location = New Point(0, 141)
        ButtonResetRotY.Margin = New Padding(0, 1, 2, 1)
        ButtonResetRotY.Name = "ButtonResetRotY"
        ButtonResetRotY.Padding = New Padding(0)
        ButtonResetRotY.Size = New Size(24, 22)
        ButtonResetRotY.TabIndex = 10
        ButtonResetRotY.TabStop = False
        ButtonResetRotY.Text = "Y"
        '
        ' SliderRotY
        '
        SliderRotY.Dock = DockStyle.Fill
        SliderRotY.DisplayFormat = "0.00%"
        SliderRotY.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderRotY.InputScale = 0.01R
        SliderRotY.LargeChange = 0.1R
        SliderRotY.Location = New Point(26, 141)
        SliderRotY.Margin = New Padding(0, 1, 0, 1)
        SliderRotY.Maximum = 1.0R
        SliderRotY.Minimum = -1.0R
        SliderRotY.Name = "SliderRotY"
        SliderRotY.Size = New Size(192, 24)
        SliderRotY.SmallChange = 0.01R
        SliderRotY.TabIndex = 11
        '
        ' ButtonResetRotZ
        '
        ButtonResetRotZ.Dock = DockStyle.Top
        ButtonResetRotZ.Location = New Point(0, 167)
        ButtonResetRotZ.Margin = New Padding(0, 1, 2, 1)
        ButtonResetRotZ.Name = "ButtonResetRotZ"
        ButtonResetRotZ.Padding = New Padding(0)
        ButtonResetRotZ.Size = New Size(24, 22)
        ButtonResetRotZ.TabIndex = 12
        ButtonResetRotZ.TabStop = False
        ButtonResetRotZ.Text = "Z"
        '
        ' SliderRotZ
        '
        SliderRotZ.Dock = DockStyle.Fill
        SliderRotZ.DisplayFormat = "0.00%"
        SliderRotZ.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderRotZ.InputScale = 0.01R
        SliderRotZ.LargeChange = 0.1R
        SliderRotZ.Location = New Point(26, 167)
        SliderRotZ.Margin = New Padding(0, 1, 0, 1)
        SliderRotZ.Maximum = 1.0R
        SliderRotZ.Minimum = -1.0R
        SliderRotZ.Name = "SliderRotZ"
        SliderRotZ.Size = New Size(192, 24)
        SliderRotZ.SmallChange = 0.01R
        SliderRotZ.TabIndex = 13
        '
        ' LabelScale
        '
        LabelScale.AutoSize = False
        LabelScale.Dock = DockStyle.Fill
        LabelScale.Font = New Font(Font, FontStyle.Bold)
        LabelScale.Location = New Point(0, 192)
        LabelScale.Margin = New Padding(0)
        LabelScale.Name = "LabelScale"
        LabelScale.Size = New Size(218, 18)
        LabelScale.TabIndex = 14
        LabelScale.Text = "Scale"
        LabelScale.TextAlign = ContentAlignment.MiddleCenter
        LayoutCard.SetColumnSpan(LabelScale, 2)
        '
        ' ButtonResetScale
        '
        ButtonResetScale.Dock = DockStyle.Top
        ButtonResetScale.Location = New Point(0, 211)
        ButtonResetScale.Margin = New Padding(0, 1, 2, 1)
        ButtonResetScale.Name = "ButtonResetScale"
        ButtonResetScale.Padding = New Padding(0)
        ButtonResetScale.Size = New Size(24, 22)
        ButtonResetScale.TabIndex = 15
        ButtonResetScale.TabStop = False
        ButtonResetScale.Text = "S"
        '
        ' SliderScale
        '
        SliderScale.Dock = DockStyle.Fill
        SliderScale.DisplayFormat = "0.00%"
        SliderScale.FillMode = FO4_Base_Library.TinySliderFillMode.Center
        SliderScale.InputScale = 0.01R
        SliderScale.LargeChange = 0.1R
        SliderScale.Location = New Point(26, 211)
        SliderScale.Margin = New Padding(0, 1, 0, 1)
        SliderScale.Maximum = 1.0R
        SliderScale.Minimum = -1.0R
        SliderScale.Name = "SliderScale"
        SliderScale.Size = New Size(192, 24)
        SliderScale.SmallChange = 0.01R
        SliderScale.TabIndex = 16
        '
        ' BoneRegionCard
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        Controls.Add(GroupBoxCard)
        Margin = New Padding(4)
        Name = "BoneRegionCard"
        Size = New Size(230, 270)
        GroupBoxCard.ResumeLayout(False)
        LayoutCard.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents GroupBoxCard As GroupBox
    Friend WithEvents LayoutCard As TableLayoutPanel
    Friend WithEvents LabelTranslation As Label
    Friend WithEvents LabelRotation As Label
    Friend WithEvents LabelScale As Label
    Friend WithEvents ButtonResetPosX As Button
    Friend WithEvents ButtonResetPosY As Button
    Friend WithEvents ButtonResetPosZ As Button
    Friend WithEvents ButtonResetRotX As Button
    Friend WithEvents ButtonResetRotY As Button
    Friend WithEvents ButtonResetRotZ As Button
    Friend WithEvents ButtonResetScale As Button
    Friend WithEvents SliderPosX As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderPosY As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderPosZ As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderRotX As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderRotY As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderRotZ As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents SliderScale As FO4_Base_Library.TinySliderTextBox
    Friend WithEvents ToolTipCard As ToolTip
End Class
