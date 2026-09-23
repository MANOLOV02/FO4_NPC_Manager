' UI built in Designer per 00-reglas-ui-y-vb.md.
'
' ⛔ MIGRADO DE POSICIONES A PIXEL A `TableLayoutPanel`. Este formulario era el único de los ocho
' editores de borrador con `FormBorderStyle.FixedDialog` y `Location = New Point(...)` en cada control:
' no se podía redimensionar, y al ganar la barra de las cuatro puertas (que ENVUELVE) el layout a pixel
' no tenía forma de acomodarse. Ahora comparte el esqueleto de los otros siete —RootLayout / TopBar /
' banner / campos / BottomLayout— y la consistencia es del LAYOUT, no sólo de los textos.
'
' ⛔ LAS FILAS CON CONTENIDO VAN `AutoSize` Y HAY UNA ÚLTIMA, VACÍA, QUE SE COME EL SOBRANTE. Sin eso,
' el sobrante vertical de un `Dock.Fill` se lo lleva la ÚLTIMA fila CON CONTENIDO y sus controles dejan
' de estar alineados aunque compartan la celda. Es la misma trampa que documenta el editor de TXST.
'
' ⛔ NINGUNA propiedad que levante evento (`.Value`, `.Checked`, `.Text` de una caja con handler) se
' fija acá: los controles se enganchan con `AddHandler` en code-behind y se siembran desde `Volcar`.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class LeveledListEditor_Form
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
        TopBar = New FlowLayoutPanel()
        ButtonNewBlank = New Button()
        ButtonNewFromTemplate = New Button()
        ButtonOverrideExisting = New Button()
        ButtonEditMine = New Button()
        LabelBanner = New Label()
        GridCampos = New TableLayoutPanel()
        LabelName = New Label()
        TextBoxName = New TextBox()
        CheckBoxCalcAllLevels = New CheckBox()
        CheckBoxCalcEachInCount = New CheckBox()
        CheckBoxUseAll = New CheckBox()
        LabelChanceNone = New Label()
        NumericChanceNone = New NumericUpDown()
        LabelMaxCount = New Label()
        NumericMaxCount = New NumericUpDown()
        LabelEntries = New Label()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).BeginInit()
        CType(NumericMaxCount, ComponentModel.ISupportInitialize).BeginInit()
        RootLayout.SuspendLayout()
        TopBar.SuspendLayout()
        GridCampos.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopBar, 0, 0)
        RootLayout.Controls.Add(LabelBanner, 0, 1)
        RootLayout.Controls.Add(GridCampos, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.TabIndex = 0
        '
        ' TopBar — las cuatro puertas de objetivo, en el orden del molde de HDPT/ARMO/ARMA.
        ' ⛔ UN solo FlowLayoutPanel que ENVUELVE: con una columna por botón el ancho mínimo de la tabla
        ' es la suma de todos y el último se recorta.
        '
        TopBar.AutoSize = True
        TopBar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TopBar.Controls.Add(ButtonNewBlank)
        TopBar.Controls.Add(ButtonNewFromTemplate)
        TopBar.Controls.Add(ButtonOverrideExisting)
        TopBar.Controls.Add(ButtonEditMine)
        TopBar.Dock = DockStyle.Fill
        TopBar.Margin = New Padding(0)
        TopBar.Name = "TopBar"
        TopBar.TabIndex = 0
        TopBar.WrapContents = True
        '
        ButtonNewBlank.AutoSize = True
        ButtonNewBlank.Name = "ButtonNewBlank"
        ButtonNewBlank.TabIndex = 0
        ButtonNewBlank.Text = "New (blank)"
        '
        ButtonNewFromTemplate.AutoSize = True
        ButtonNewFromTemplate.Name = "ButtonNewFromTemplate"
        ButtonNewFromTemplate.TabIndex = 1
        ButtonNewFromTemplate.Text = "New from template…"
        '
        ButtonOverrideExisting.AutoSize = True
        ButtonOverrideExisting.Name = "ButtonOverrideExisting"
        ButtonOverrideExisting.TabIndex = 2
        ButtonOverrideExisting.Text = "Override existing…"
        '
        ButtonEditMine.AutoSize = True
        ButtonEditMine.Name = "ButtonEditMine"
        ButtonEditMine.TabIndex = 3
        ButtonEditMine.Text = "Edit mine…"
        '
        ' ⛔ El banner va en su PROPIA fila, en negrita y con el color por defecto: es el dato más
        ' importante de la ventana y en `GrayText` se leería como deshabilitado.
        '
        LabelBanner.AutoSize = True
        LabelBanner.Font = New Font(Font, FontStyle.Bold)
        LabelBanner.Margin = New Padding(3, 6, 3, 6)
        LabelBanner.Name = "LabelBanner"
        LabelBanner.TabIndex = 1
        '
        ' GridCampos — dos columnas: rótulo y valor. Las seis filas con contenido van AutoSize y la
        ' séptima, vacía, se come el sobrante.
        '
        GridCampos.ColumnCount = 2
        GridCampos.ColumnStyles.Add(New ColumnStyle())
        GridCampos.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GridCampos.Controls.Add(LabelName, 0, 0)
        GridCampos.Controls.Add(TextBoxName, 1, 0)
        GridCampos.Controls.Add(CheckBoxCalcAllLevels, 1, 1)
        GridCampos.Controls.Add(CheckBoxCalcEachInCount, 1, 2)
        GridCampos.Controls.Add(CheckBoxUseAll, 1, 3)
        GridCampos.Controls.Add(LabelChanceNone, 0, 4)
        GridCampos.Controls.Add(NumericChanceNone, 1, 4)
        GridCampos.Controls.Add(LabelMaxCount, 0, 5)
        GridCampos.Controls.Add(NumericMaxCount, 1, 5)
        GridCampos.Controls.Add(LabelEntries, 1, 6)
        GridCampos.Dock = DockStyle.Fill
        GridCampos.Name = "GridCampos"
        GridCampos.RowCount = 8
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridCampos.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        GridCampos.TabIndex = 2
        '
        LabelName.Anchor = AnchorStyles.Left
        LabelName.AutoSize = True
        LabelName.Name = "LabelName"
        LabelName.TabIndex = 0
        LabelName.Text = "EDID: npcm_<esp>_LVLI_"
        '
        TextBoxName.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        TextBoxName.Name = "TextBoxName"
        TextBoxName.PlaceholderText = "name"
        TextBoxName.TabIndex = 1
        '
        CheckBoxCalcAllLevels.AutoSize = True
        CheckBoxCalcAllLevels.Name = "CheckBoxCalcAllLevels"
        CheckBoxCalcAllLevels.TabIndex = 2
        CheckBoxCalcAllLevels.Text = "Calculate from all levels <= player's level (0x01)"
        '
        CheckBoxCalcEachInCount.AutoSize = True
        CheckBoxCalcEachInCount.Name = "CheckBoxCalcEachInCount"
        CheckBoxCalcEachInCount.TabIndex = 3
        CheckBoxCalcEachInCount.Text = "Calculate for each item in count (0x02)"
        '
        CheckBoxUseAll.AutoSize = True
        CheckBoxUseAll.Name = "CheckBoxUseAll"
        CheckBoxUseAll.TabIndex = 4
        CheckBoxUseAll.Text = "Use All (0x04)"
        '
        LabelChanceNone.Anchor = AnchorStyles.Left
        LabelChanceNone.AutoSize = True
        LabelChanceNone.Name = "LabelChanceNone"
        LabelChanceNone.TabIndex = 5
        LabelChanceNone.Text = "Chance None (%):"
        '
        ' ⛔ Alto FIJO con `Anchor = Left`, nunca `Dock = Fill`: en una celda AutoSize un control docked
        ' sin `GetPreferredSize` hace que la fila tome su alto SERIALIZADO.
        NumericChanceNone.Anchor = AnchorStyles.Left
        NumericChanceNone.Maximum = New Decimal(New Integer() {100, 0, 0, 0})
        NumericChanceNone.Name = "NumericChanceNone"
        NumericChanceNone.Size = New Size(56, 23)
        NumericChanceNone.TabIndex = 6
        NumericChanceNone.TextAlign = HorizontalAlignment.Right
        '
        ' La fila del Max Count (LVLM) es de Fallout 4: el code-behind la SACA en Skyrim, donde el
        ' subrecord no está en el formato.
        LabelMaxCount.Anchor = AnchorStyles.Left
        LabelMaxCount.AutoSize = True
        LabelMaxCount.Name = "LabelMaxCount"
        LabelMaxCount.TabIndex = 7
        LabelMaxCount.Text = "Max Count:"
        '
        NumericMaxCount.Anchor = AnchorStyles.Left
        NumericMaxCount.Maximum = New Decimal(New Integer() {255, 0, 0, 0})
        NumericMaxCount.Name = "NumericMaxCount"
        NumericMaxCount.Size = New Size(56, 23)
        NumericMaxCount.TabIndex = 8
        NumericMaxCount.TextAlign = HorizontalAlignment.Right
        '
        LabelEntries.AutoSize = True
        LabelEntries.ForeColor = SystemColors.GrayText
        LabelEntries.Margin = New Padding(3, 12, 3, 3)
        LabelEntries.Name = "LabelEntries"
        LabelEntries.TabIndex = 9
        '
        BottomLayout.AutoSize = True
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 3
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
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(620, 340)
        Controls.Add(RootLayout)
        Font = New Font("Segoe UI", 9.0F)
        MinimumSize = New Size(560, 320)
        Name = "LeveledListEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Leveled list"
        CType(NumericChanceNone, ComponentModel.ISupportInitialize).EndInit()
        CType(NumericMaxCount, ComponentModel.ISupportInitialize).EndInit()
        RootLayout.ResumeLayout(False)
        RootLayout.PerformLayout()
        TopBar.ResumeLayout(False)
        GridCampos.ResumeLayout(False)
        GridCampos.PerformLayout()
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents TopBar As FlowLayoutPanel
    Friend WithEvents ButtonNewBlank As Button
    Friend WithEvents ButtonNewFromTemplate As Button
    Friend WithEvents ButtonOverrideExisting As Button
    Friend WithEvents ButtonEditMine As Button
    Friend WithEvents LabelBanner As Label
    Friend WithEvents GridCampos As TableLayoutPanel
    Friend WithEvents LabelName As Label
    Friend WithEvents TextBoxName As TextBox
    Friend WithEvents CheckBoxCalcAllLevels As CheckBox
    Friend WithEvents CheckBoxCalcEachInCount As CheckBox
    Friend WithEvents CheckBoxUseAll As CheckBox
    Friend WithEvents LabelChanceNone As Label
    Friend WithEvents NumericChanceNone As NumericUpDown
    Friend WithEvents LabelMaxCount As Label
    Friend WithEvents NumericMaxCount As NumericUpDown
    Friend WithEvents LabelEntries As Label
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
