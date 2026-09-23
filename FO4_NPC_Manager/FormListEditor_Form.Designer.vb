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
        ButtonNewBlank = New Button()
        ButtonNewFromTemplate = New Button()
        ButtonOverrideExisting = New Button()
        ButtonEditMine = New Button()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelName = New Label()
        TextBoxName = New TextBox()
        LabelBanner = New Label()
        LabelHint = New Label()
        ListViewMembers = New ListView()
        ButtonsRow = New FlowLayoutPanel()
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
        RootLayout.Controls.Add(LabelBanner, 0, 1)
        RootLayout.Controls.Add(LabelHint, 0, 2)
        RootLayout.Controls.Add(ListViewMembers, 0, 3)
        RootLayout.Controls.Add(ButtonsRow, 0, 4)
        RootLayout.Controls.Add(BottomLayout, 0, 5)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 6
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.TabIndex = 0
        '
        ' Las cuatro puertas de objetivo y los campos de identidad, en el orden del molde de
        ' HDPT/ARMO/ARMA. ⛔ `WrapContents = True` y UN solo FlowLayoutPanel: con una columna por
        ' botón el ancho mínimo de la tabla es la suma de todos y el último se recorta.
        TopRow.AutoSize = True
        TopRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TopRow.Controls.Add(ButtonNewBlank)
        TopRow.Controls.Add(ButtonNewFromTemplate)
        TopRow.Controls.Add(ButtonOverrideExisting)
        TopRow.Controls.Add(ButtonEditMine)
        TopRow.Controls.Add(LabelEdid)
        TopRow.Controls.Add(TextBoxEdid)
        TopRow.Controls.Add(LabelName)
        TopRow.Controls.Add(TextBoxName)
        TopRow.Dock = DockStyle.Fill
        TopRow.Margin = New Padding(0)
        TopRow.Name = "TopRow"
        TopRow.TabIndex = 0
        TopRow.WrapContents = True
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
        ' ⛔ EL BANNER VA EN SU PROPIA FILA, EN NEGRITA Y CON EL COLOR POR DEFECTO: es el dato más
        ' importante de la ventana y en `GrayText` se leía como deshabilitado. Ver el gemelo de TXST.
        LabelBanner.AutoSize = True
        LabelBanner.Font = New Font(Font, FontStyle.Bold)
        LabelBanner.Margin = New Padding(3, 6, 3, 6)
        LabelBanner.Name = "LabelBanner"
        LabelBanner.TabIndex = 1
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
        ButtonsRow.Controls.Add(ButtonAdd)
        ButtonsRow.Controls.Add(ButtonRemove)
        ButtonsRow.Controls.Add(ButtonUp)
        ButtonsRow.Controls.Add(ButtonDown)
        ButtonsRow.Dock = DockStyle.Fill
        ButtonsRow.Name = "ButtonsRow"
        ButtonsRow.TabIndex = 3
        ButtonsRow.WrapContents = False
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
                ' ⛔⛔ +40 px DE ALTO, Y ES POR LA FILA DE BOTONES QUE ESTA OLA AGREGO. `RootLayout` paso de 5
        ' filas a 6 —la barra de «New / New from template… / Override existing… / Edit mine…»— y el
        ' `ClientSize` se quedo como estaba, asi que esos pixeles se los saco al contenido.
        '    MEDIDO por `TxstEditorLayoutGate`: la ultima fila de `GridTextures` quedo en **-13 px** (las 9
        ' filas de textura piden 34 cada una y la grilla ya no las cubre). Sin sobrante que repartir, el
        ' mutante del propio gate dejo de mover el desfase y el gate se acuso a si mismo:
        ' «este gate no puede ver el defecto que dice vigilar». O sea que el instrumento detecto que la
        ' ventana se habia quedado corta antes de que lo viera un humano.
        '    El numero sale del deficit medido (13) mas el sobrante que la ventana tenia antes (~20),
        ' redondeado al alto real de la barra. Se verifica volviendo a correr el gate: la ultima fila tiene
        ' que dar POSITIVA.
        ClientSize = New Size(860, 500)
        Controls.Add(RootLayout)
        MinimumSize = New Size(700, 440)
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
    Friend WithEvents ButtonNewBlank As Button
    Friend WithEvents ButtonNewFromTemplate As Button
    Friend WithEvents ButtonOverrideExisting As Button
    Friend WithEvents ButtonEditMine As Button
    Friend WithEvents LabelEdid As Label
    Friend WithEvents TextBoxEdid As TextBox
    Friend WithEvents LabelName As Label
    Friend WithEvents TextBoxName As TextBox
    Friend WithEvents LabelBanner As Label
    Friend WithEvents LabelHint As Label
    Friend WithEvents ListViewMembers As ListView
    Friend WithEvents ButtonsRow As FlowLayoutPanel
    Friend WithEvents ButtonAdd As Button
    Friend WithEvents ButtonRemove As Button
    Friend WithEvents ButtonUp As Button
    Friend WithEvents ButtonDown As Button
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
