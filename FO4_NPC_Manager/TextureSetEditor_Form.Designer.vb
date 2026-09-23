' UI built in Designer per 00-reglas-ui-y-vb.md. InitializeComponent is declarative ONLY: los ROTULOS
' de las ocho ranuras de textura se ponen en code-behind, porque se llaman distinto en cada juego
' (TX02 es Wrinkles en Fallout 4 y Environment Mask/Subsurface Tint en Skyrim; idem TX03 y TX07).
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class TextureSetEditor_Form
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
        LabelBanner = New Label()
        GridTextures = New TableLayoutPanel()
        LabelObnd = New Label()
        LabelDodt = New Label()
        LabelMnam = New Label()
        TextBoxMnam = New TextBox()
        ButtonBrowseMnam = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()
        RootLayout.SuspendLayout()
        TopRow.SuspendLayout()
        GridTextures.SuspendLayout()
        BottomLayout.SuspendLayout()
        SuspendLayout()
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopRow, 0, 0)
        RootLayout.Controls.Add(LabelBanner, 0, 1)
        RootLayout.Controls.Add(GridTextures, 0, 2)
        RootLayout.Controls.Add(LabelObnd, 0, 3)
        RootLayout.Controls.Add(LabelDodt, 0, 4)
        RootLayout.Controls.Add(BottomLayout, 0, 5)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 6
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.RowStyles.Add(New RowStyle())
        RootLayout.TabIndex = 0
        '
        ' Las cuatro puertas de objetivo y el EditorID, en el orden del molde de HDPT/ARMO/ARMA.
        ' ⛔ `WrapContents = True`: los botones van en UN solo FlowLayoutPanel y la barra envuelve si
        ' no entran. Con una columna por botón el ancho mínimo de la tabla es la suma de todos y el
        ' último se recorta — el defecto que el editor de head parts documenta.
        TopRow.AutoSize = True
        TopRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TopRow.Controls.Add(ButtonNewBlank)
        TopRow.Controls.Add(ButtonNewFromTemplate)
        TopRow.Controls.Add(ButtonOverrideExisting)
        TopRow.Controls.Add(ButtonEditMine)
        TopRow.Controls.Add(LabelEdid)
        TopRow.Controls.Add(TextBoxEdid)
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
        TextBoxEdid.Size = New Size(280, 23)
        TextBoxEdid.TabIndex = 1
        '
        ' ⛔ EL BANNER VA EN SU PROPIA FILA, EN NEGRITA Y CON EL COLOR POR DEFECTO. Antes compartía la
        ' barra de arriba con el EditorID —se superponían— y estaba en `GrayText`, que lo hacía leer
        ' como deshabilitado justamente en el dato más importante de la ventana. Misma ley que en
        ' `ArmaEditor_Form` / `ArmoEditor_Form` / `HeadPartEditor_Form`.
        LabelBanner.AutoSize = True
        LabelBanner.Font = New Font(Font, FontStyle.Bold)
        LabelBanner.Margin = New Padding(3, 6, 3, 6)
        LabelBanner.Name = "LabelBanner"
        LabelBanner.TabIndex = 1
        '
        ' Las ocho filas (rótulo + caja + Browse) las agrega el code-behind con los nombres del juego.
        GridTextures.ColumnCount = 3
        GridTextures.ColumnStyles.Add(New ColumnStyle())
        GridTextures.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GridTextures.ColumnStyles.Add(New ColumnStyle())
        GridTextures.Dock = DockStyle.Fill
        GridTextures.Name = "GridTextures"
        ' ⛔ LAS NUEVE FILAS CON CONTENIDO VAN `AutoSize` Y HAY UNA DÉCIMA, VACÍA, QUE SE COME EL
        ' SOBRANTE. Sin `RowStyles` el sobrante vertical de un `Dock.Fill` se lo come la ÚLTIMA fila
        ' CON CONTENIDO, y ahí el rótulo y la caja del MNAM dejan de estar alineados aunque compartan
        ' la celda: la caja es de una línea, con `Dock.Fill` no puede crecer y se pega ARRIBA, y el
        ' rótulo con `Anchor = Left` se CENTRA. Medido con un repro del mismo layout: filas 0..7 de
        ' 30 px, fila 8 de 172 px, caja en Y=243 y rótulo en Y=317 — 74 px de desfase.
        GridTextures.RowCount = 10
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GridTextures.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        GridTextures.TabIndex = 1
        '
        LabelObnd.AutoSize = True
        LabelObnd.ForeColor = SystemColors.GrayText
        LabelObnd.Name = "LabelObnd"
        LabelObnd.TabIndex = 2
        '
        LabelDodt.AutoSize = True
        LabelDodt.ForeColor = SystemColors.GrayText
        LabelDodt.Name = "LabelDodt"
        LabelDodt.TabIndex = 3
        '
        LabelMnam.Anchor = AnchorStyles.Left
        LabelMnam.AutoSize = True
        LabelMnam.Name = "LabelMnam"
        LabelMnam.TabIndex = 0
        LabelMnam.Text = "Material (MNAM):"
        '
        TextBoxMnam.Dock = DockStyle.Fill
        TextBoxMnam.Name = "TextBoxMnam"
        TextBoxMnam.TabIndex = 1
        '
        ' El MNAM es un archivo de material del juego, así que tiene el mismo selector que las ocho
        ' ranuras de textura. Lo agrega a la grilla el code-behind, junto con su rótulo y su caja:
        ' la fila entera es de Fallout 4 — Skyrim no declara MNAM en TXST (0 de 1.430 del corpus).
        ButtonBrowseMnam.AutoSize = True
        ButtonBrowseMnam.Name = "ButtonBrowseMnam"
        ButtonBrowseMnam.TabIndex = 2
        ButtonBrowseMnam.Text = "Browse…"
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
        ClientSize = New Size(900, 520)
        Controls.Add(RootLayout)
        MinimumSize = New Size(700, 460)
        Name = "TextureSetEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Texture Set Editor"
        RootLayout.ResumeLayout(False)
        TopRow.ResumeLayout(False)
        GridTextures.ResumeLayout(False)
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
    Friend WithEvents LabelBanner As Label
    Friend WithEvents GridTextures As TableLayoutPanel
    Friend WithEvents LabelObnd As Label
    Friend WithEvents LabelDodt As Label
    Friend WithEvents LabelMnam As Label
    Friend WithEvents TextBoxMnam As TextBox
    Friend WithEvents ButtonBrowseMnam As Button
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
