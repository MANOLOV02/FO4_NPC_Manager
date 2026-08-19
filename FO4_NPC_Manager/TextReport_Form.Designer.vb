' UI built in Designer per 00-reglas-ui-y-vb.md.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class TextReport_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
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
        TextBoxReport = New TextBox()
        PanelButtons = New Panel()
        ButtonApply = New Button()
        ButtonClose = New Button()
        ButtonCopy = New Button()
        PanelButtons.SuspendLayout()
        SuspendLayout()
        '
        ' TextBoxReport
        '
        TextBoxReport.Dock = DockStyle.Fill
        TextBoxReport.Font = New Font(FontFamily.GenericMonospace, 8.5F)
        TextBoxReport.Location = New Point(0, 0)
        ' ⛔ SIN ESTO EL REPORTE SE CORTA EN SILENCIO: el MaxLength por default de un TextBox son 32767
        ' caracteres y se aplica tambien a la asignacion POR CODIGO, sin ninguna senal de que corto. Uno
        ' de los dos modales que este formulario reemplaza NO tenia este fix; ahora lo tienen los dos.
        TextBoxReport.MaxLength = Integer.MaxValue
        TextBoxReport.Multiline = True
        TextBoxReport.Name = "TextBoxReport"
        TextBoxReport.ReadOnly = True
        TextBoxReport.ScrollBars = ScrollBars.Both
        TextBoxReport.Size = New Size(860, 580)
        ' TabStop=False para que el foco inicial vaya al boton y no al TextBox: un TextBox que recibe el
        ' foco autoselecciona TODO su contenido (default de WinForms), que es lo que hacia aparecer el
        ' informe entero resaltado al abrir.
        TextBoxReport.TabIndex = 1
        TextBoxReport.TabStop = False
        TextBoxReport.WordWrap = False
        '
        ' PanelButtons
        '
        ' ⭐ ORDEN DE Controls.Add MEDIDO, no supuesto (Tools\DesignerCostProbe, Q1): con Dock=Right el
        ' borde derecho se lo lleva el ULTIMO agregado, y un boton oculto NO deja hueco. Con este unico
        ' orden [Apply, Close, Copy] salen las DOS disposiciones que tenian los modales originales:
        '   Copy oculto      -> [Apply][Close]  (Close a la derecha)   = el informe de "Regenerate morphs"
        '   Apply oculto     -> [Close][Copy]   (Copy a la derecha)    = el informe de compatibilidad
        PanelButtons.Controls.Add(ButtonApply)
        PanelButtons.Controls.Add(ButtonClose)
        PanelButtons.Controls.Add(ButtonCopy)
        PanelButtons.Dock = DockStyle.Bottom
        PanelButtons.Location = New Point(0, 580)
        PanelButtons.Name = "PanelButtons"
        PanelButtons.Padding = New Padding(6)
        PanelButtons.Size = New Size(860, 40)
        PanelButtons.TabIndex = 0
        '
        ' ButtonApply
        '
        ButtonApply.DialogResult = DialogResult.OK
        ButtonApply.Dock = DockStyle.Right
        ButtonApply.Location = New Point(628, 6)
        ButtonApply.Name = "ButtonApply"
        ButtonApply.Size = New Size(110, 28)
        ButtonApply.TabIndex = 0
        ButtonApply.Text = "Apply"
        ButtonApply.Visible = False
        '
        ' ButtonClose
        '
        ButtonClose.DialogResult = DialogResult.Cancel
        ButtonClose.Dock = DockStyle.Right
        ButtonClose.Location = New Point(738, 6)
        ButtonClose.Name = "ButtonClose"
        ButtonClose.Size = New Size(110, 28)
        ButtonClose.TabIndex = 1
        ButtonClose.Text = "Close"
        '
        ' ButtonCopy
        '
        ButtonCopy.Dock = DockStyle.Right
        ButtonCopy.Location = New Point(848, 6)
        ButtonCopy.Name = "ButtonCopy"
        ButtonCopy.Size = New Size(110, 28)
        ButtonCopy.TabIndex = 2
        ButtonCopy.Text = "Copy"
        ButtonCopy.Visible = False
        '
        ' TextReport_Form
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonClose
        ClientSize = New Size(860, 620)
        ' ⭐ El de Dock=Fill va PRIMERO para quedar al frente del z-order: el motor de layout resuelve los
        ' hijos desde el ultimo indice hacia el primero, asi que el Bottom se ubica antes y el Fill se
        ' queda con lo que sobra. El modal original conseguia lo mismo con un BringToFront() extra.
        Controls.Add(TextBoxReport)
        Controls.Add(PanelButtons)
        MinimizeBox = False
        MinimumSize = New Size(420, 260)
        Name = "TextReport_Form"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Report"
        PanelButtons.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents TextBoxReport As TextBox
    Friend WithEvents PanelButtons As Panel
    Friend WithEvents ButtonApply As Button
    Friend WithEvents ButtonClose As Button
    Friend WithEvents ButtonCopy As Button
End Class
