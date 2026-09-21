' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY: el combo de
' funciones (479 en Fallout 4), el de operadores (SEIS), el de Run On y los rótulos de los dos
' parámetros se pueblan en code-behind, porque salen de las tablas generadas del juego de la sesión.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ConditionsEditor_Form
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
        LabelHint = New Label()
        LabelFunction = New Label()
        ComboFunction = New ComboBox()
        LabelParam1 = New Label()
        PanelParam1 = New Panel()
        TextBoxParam1 = New TextBox()
        ComboParam1 = New ComboBox()
        ButtonPickParam1 = New Button()
        LabelParam2 = New Label()
        PanelParam2 = New Panel()
        TextBoxParam2 = New TextBox()
        ComboParam2 = New ComboBox()
        ButtonPickParam2 = New Button()
        LabelParam3 = New Label()
        TextBoxParam3 = New TextBox()
        LabelReference = New Label()
        TextBoxReference = New TextBox()
        ButtonPickReference = New Button()
        LabelOperator = New Label()
        ComboOperator = New ComboBox()
        LabelValue = New Label()
        TextBoxValue = New TextBox()
        ButtonPickGlobal = New Button()
        LabelRunOn = New Label()
        ComboRunOn = New ComboBox()
        GroupFlags = New GroupBox()
        FlagsLayout = New FlowLayoutPanel()
        CheckOr = New CheckBox()
        CheckUseAliases = New CheckBox()
        CheckUseGlobal = New CheckBox()
        CheckUsePackdata = New CheckBox()
        CheckSwapSubject = New CheckBox()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        PanelParam1.SuspendLayout()
        PanelParam2.SuspendLayout()
        GroupFlags.SuspendLayout()
        FlagsLayout.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 3
        RootLayout.ColumnStyles.Add(New ColumnStyle())
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.ColumnStyles.Add(New ColumnStyle())
        RootLayout.Controls.Add(LabelHint, 0, 0)
        RootLayout.SetColumnSpan(LabelHint, 3)
        RootLayout.Controls.Add(LabelFunction, 0, 1)
        RootLayout.Controls.Add(ComboFunction, 1, 1)
        RootLayout.Controls.Add(LabelParam1, 0, 2)
        RootLayout.Controls.Add(PanelParam1, 1, 2)
        RootLayout.Controls.Add(ButtonPickParam1, 2, 2)
        RootLayout.Controls.Add(LabelParam2, 0, 3)
        RootLayout.Controls.Add(PanelParam2, 1, 3)
        RootLayout.Controls.Add(ButtonPickParam2, 2, 3)
        RootLayout.Controls.Add(LabelParam3, 0, 4)
        RootLayout.Controls.Add(TextBoxParam3, 1, 4)
        RootLayout.Controls.Add(LabelOperator, 0, 5)
        RootLayout.Controls.Add(ComboOperator, 1, 5)
        RootLayout.Controls.Add(LabelValue, 0, 6)
        RootLayout.Controls.Add(TextBoxValue, 1, 6)
        RootLayout.Controls.Add(ButtonPickGlobal, 2, 6)
        RootLayout.Controls.Add(LabelRunOn, 0, 7)
        RootLayout.Controls.Add(ComboRunOn, 1, 7)
        RootLayout.Controls.Add(LabelReference, 0, 8)
        RootLayout.Controls.Add(TextBoxReference, 1, 8)
        RootLayout.Controls.Add(ButtonPickReference, 2, 8)
        RootLayout.Controls.Add(GroupFlags, 1, 9)
        RootLayout.Controls.Add(BottomLayout, 1, 10)
        RootLayout.SetColumnSpan(BottomLayout, 2)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 11
        RootLayout.TabIndex = 0
        '
        LabelHint.AutoSize = True
        LabelHint.ForeColor = Color.DimGray
        LabelHint.MaximumSize = New Size(700, 0)
        LabelHint.Name = "LabelHint"
        LabelHint.TabIndex = 0
        '
        LabelFunction.Anchor = AnchorStyles.Left
        LabelFunction.AutoSize = True
        LabelFunction.Name = "LabelFunction"
        LabelFunction.TabIndex = 1
        LabelFunction.Text = "Function:"
        '
        ' Lista cerrada: las 479 (402 en Skyrim) son TODAS las que la tabla generada nombra, y el
        ' valor se lee por INDICE. Tipear a mano solo podia producir un nombre que no existe -y el
        ' parser del rotulo caia al valor viejo en silencio-. El indice que la tabla no nombra entra
        ' como entrada extra, igual que el operador invalido y el Run On fuera de tabla.
        ComboFunction.DropDownStyle = ComboBoxStyle.DropDownList
        ComboFunction.Dock = DockStyle.Fill
        ComboFunction.Name = "ComboFunction"
        ComboFunction.TabIndex = 2
        '
        LabelParam1.Anchor = AnchorStyles.Left
        LabelParam1.AutoSize = True
        LabelParam1.Name = "LabelParam1"
        LabelParam1.TabIndex = 3
        LabelParam1.Text = "Param 1:"
        '
        PanelParam1.AutoSize = True
        PanelParam1.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelParam1.Dock = DockStyle.Fill
        PanelParam1.Margin = New Padding(0)
        PanelParam1.Name = "PanelParam1"
        PanelParam1.TabIndex = 4
        PanelParam1.Controls.Add(TextBoxParam1)
        PanelParam1.Controls.Add(ComboParam1)
        '
        TextBoxParam1.Dock = DockStyle.Fill
        TextBoxParam1.Name = "TextBoxParam1"
        TextBoxParam1.TabIndex = 0
        '
        ' DropDownList: los valores de un enumerado de condicion son los que el .pas declara y
        ' nada mas. A diferencia del combo de FUNCIONES —donde el record puede traer un indice
        ' que la tabla no nombra— aca el dominio del campo ES el enumerado, y si el record trae
        ' un valor de fuera, el code-behind le agrega su propia entrada cruda.
        ComboParam1.DropDownStyle = ComboBoxStyle.DropDownList
        ComboParam1.Dock = DockStyle.Fill
        ComboParam1.Name = "ComboParam1"
        ComboParam1.TabIndex = 1
        ComboParam1.Visible = False
        '
        ButtonPickParam1.AutoSize = True
        ButtonPickParam1.Name = "ButtonPickParam1"
        ButtonPickParam1.TabIndex = 5
        ButtonPickParam1.Text = "…"
        '
        LabelParam2.Anchor = AnchorStyles.Left
        LabelParam2.AutoSize = True
        LabelParam2.Name = "LabelParam2"
        LabelParam2.TabIndex = 6
        LabelParam2.Text = "Param 2:"
        '
        PanelParam2.AutoSize = True
        PanelParam2.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelParam2.Dock = DockStyle.Fill
        PanelParam2.Margin = New Padding(0)
        PanelParam2.Name = "PanelParam2"
        PanelParam2.TabIndex = 7
        PanelParam2.Controls.Add(TextBoxParam2)
        PanelParam2.Controls.Add(ComboParam2)
        '
        TextBoxParam2.Dock = DockStyle.Fill
        TextBoxParam2.Name = "TextBoxParam2"
        TextBoxParam2.TabIndex = 0
        '
        ComboParam2.DropDownStyle = ComboBoxStyle.DropDownList
        ComboParam2.Dock = DockStyle.Fill
        ComboParam2.Name = "ComboParam2"
        ComboParam2.TabIndex = 1
        ComboParam2.Visible = False
        '
        ButtonPickParam2.AutoSize = True
        ButtonPickParam2.Name = "ButtonPickParam2"
        ButtonPickParam2.TabIndex = 8
        ButtonPickParam2.Text = "…"
        '
        LabelParam3.Anchor = AnchorStyles.Left
        LabelParam3.AutoSize = True
        LabelParam3.Name = "LabelParam3"
        LabelParam3.TabIndex = 9
        LabelParam3.Text = "Param 3:"
        '
        ' El `Parameter #3` lo decide el `Run On`, no la funcion: con Run On = 5 es un indice de
        ' alias de quest y con 7 un Event Data; en los otros nueve valores es un entero con signo.
        ' Por eso no lleva selector de record: ninguna de sus once ramas es un FormID.
        TextBoxParam3.Dock = DockStyle.Fill
        TextBoxParam3.Name = "TextBoxParam3"
        TextBoxParam3.TabIndex = 10
        '
        LabelReference.Anchor = AnchorStyles.Left
        LabelReference.AutoSize = True
        LabelReference.Name = "LabelReference"
        LabelReference.TabIndex = 11
        LabelReference.Text = "Reference:"
        '
        ' El `Reference` es una referencia SOLO con Run On = 2 (`ConditionReference` devuelve 1 nada
        ' mas en ese caso); con cualquier otro la rama es `Int("Unused", u32)` y el campo no es un
        ' FormID. El code-behind habilita o apaga el selector por eso.
        TextBoxReference.Dock = DockStyle.Fill
        TextBoxReference.Name = "TextBoxReference"
        TextBoxReference.TabIndex = 12
        '
        ButtonPickReference.AutoSize = True
        ButtonPickReference.Name = "ButtonPickReference"
        ButtonPickReference.TabIndex = 13
        ButtonPickReference.Text = "…"
        '
        LabelOperator.Anchor = AnchorStyles.Left
        LabelOperator.AutoSize = True
        LabelOperator.Name = "LabelOperator"
        LabelOperator.TabIndex = 8
        LabelOperator.Text = "Comparison:"
        '
        ' DropDownList acá SÍ: los seis valores son TODOS los que existen —el propio validador de xEdit
        ' rechaza 192 y 224—, así que un combo cerrado no puede perder un valor legítimo. Y el
        ' code-behind AGREGA una entrada extra si el record trae uno de los dos inválidos, para no
        ' reescribirlo en silencio.
        ComboOperator.DropDownStyle = ComboBoxStyle.DropDownList
        ComboOperator.Dock = DockStyle.Fill
        ComboOperator.Name = "ComboOperator"
        ComboOperator.TabIndex = 9
        '
        LabelValue.Anchor = AnchorStyles.Left
        LabelValue.AutoSize = True
        LabelValue.Name = "LabelValue"
        LabelValue.TabIndex = 10
        LabelValue.Text = "Value:"
        '
        TextBoxValue.Dock = DockStyle.Fill
        TextBoxValue.Name = "TextBoxValue"
        TextBoxValue.TabIndex = 11
        '
        ButtonPickGlobal.AutoSize = True
        ButtonPickGlobal.Name = "ButtonPickGlobal"
        ButtonPickGlobal.TabIndex = 12
        ButtonPickGlobal.Text = "Pick GLOB…"
        '
        LabelRunOn.Anchor = AnchorStyles.Left
        LabelRunOn.AutoSize = True
        LabelRunOn.Name = "LabelRunOn"
        LabelRunOn.TabIndex = 13
        LabelRunOn.Text = "Run On:"
        '
        ComboRunOn.DropDownStyle = ComboBoxStyle.DropDownList
        ComboRunOn.Dock = DockStyle.Fill
        ComboRunOn.Name = "ComboRunOn"
        ComboRunOn.TabIndex = 14
        '
        GroupFlags.AutoSize = True
        GroupFlags.Controls.Add(FlagsLayout)
        GroupFlags.Dock = DockStyle.Fill
        GroupFlags.Name = "GroupFlags"
        GroupFlags.TabIndex = 15
        GroupFlags.TabStop = False
        GroupFlags.Text = "Flags"
        '
        FlagsLayout.AutoSize = True
        FlagsLayout.Controls.Add(CheckOr)
        FlagsLayout.Controls.Add(CheckUseAliases)
        FlagsLayout.Controls.Add(CheckUseGlobal)
        FlagsLayout.Controls.Add(CheckUsePackdata)
        FlagsLayout.Controls.Add(CheckSwapSubject)
        FlagsLayout.Dock = DockStyle.Fill
        FlagsLayout.Name = "FlagsLayout"
        FlagsLayout.TabIndex = 0
        '
        CheckOr.AutoSize = True
        CheckOr.Name = "CheckOr"
        CheckOr.TabIndex = 0
        CheckOr.Text = "Or"
        '
        CheckUseAliases.AutoSize = True
        CheckUseAliases.Name = "CheckUseAliases"
        CheckUseAliases.TabIndex = 1
        CheckUseAliases.Text = "Use Aliases"
        '
        CheckUseGlobal.AutoSize = True
        CheckUseGlobal.Name = "CheckUseGlobal"
        CheckUseGlobal.TabIndex = 2
        CheckUseGlobal.Text = "Use Global"
        '
        CheckUsePackdata.AutoSize = True
        CheckUsePackdata.Name = "CheckUsePackdata"
        CheckUsePackdata.TabIndex = 3
        CheckUsePackdata.Text = "Use Packdata"
        '
        CheckSwapSubject.AutoSize = True
        CheckSwapSubject.Name = "CheckSwapSubject"
        CheckSwapSubject.TabIndex = 4
        CheckSwapSubject.Text = "Swap Subject and Target"
        '
        BottomLayout.AutoSize = True
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 16
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
        ClientSize = New Size(760, 400)
        Controls.Add(RootLayout)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "ConditionsEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Condition (CTDA)"
        PanelParam1.ResumeLayout(False)
        PanelParam2.ResumeLayout(False)
        RootLayout.ResumeLayout(False)
        GroupFlags.ResumeLayout(False)
        FlagsLayout.ResumeLayout(False)
        BottomLayout.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents LabelHint As Label
    Friend WithEvents LabelFunction As Label
    Friend WithEvents ComboFunction As ComboBox
    Friend WithEvents LabelParam1 As Label
    Friend WithEvents PanelParam1 As Panel
    Friend WithEvents TextBoxParam1 As TextBox
    Friend WithEvents ComboParam1 As ComboBox
    Friend WithEvents ButtonPickParam1 As Button
    Friend WithEvents LabelParam2 As Label
    Friend WithEvents PanelParam2 As Panel
    Friend WithEvents TextBoxParam2 As TextBox
    Friend WithEvents ComboParam2 As ComboBox
    Friend WithEvents ButtonPickParam2 As Button
    Friend WithEvents LabelParam3 As Label
    Friend WithEvents TextBoxParam3 As TextBox
    Friend WithEvents LabelReference As Label
    Friend WithEvents TextBoxReference As TextBox
    Friend WithEvents ButtonPickReference As Button
    Friend WithEvents LabelOperator As Label
    Friend WithEvents ComboOperator As ComboBox
    Friend WithEvents LabelValue As Label
    Friend WithEvents TextBoxValue As TextBox
    Friend WithEvents ButtonPickGlobal As Button
    Friend WithEvents LabelRunOn As Label
    Friend WithEvents ComboRunOn As ComboBox
    Friend WithEvents GroupFlags As GroupBox
    Friend WithEvents FlagsLayout As FlowLayoutPanel
    Friend WithEvents CheckOr As CheckBox
    Friend WithEvents CheckUseAliases As CheckBox
    Friend WithEvents CheckUseGlobal As CheckBox
    Friend WithEvents CheckUsePackdata As CheckBox
    Friend WithEvents CheckSwapSubject As CheckBox
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
