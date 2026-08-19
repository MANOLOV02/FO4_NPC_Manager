''' <summary>
''' Modal de REPORTE de sólo lectura, monoespaciado: una tabla de ancho fijo o un informe de varios
''' párrafos que no cabe en un label ni en un tooltip.
''' <para>Reemplaza los DOS modales que se armaban inline —el preview de "Regenerate morphs"
''' (<c>EditFace_Form.ShowRegenReport</c>) y el informe de compatibilidad de presets
''' (<c>LooksmenuLoad_Form.ButtonShowIncompatible_Click</c>)—, que eran el mismo formulario escrito dos
''' veces, hasta en el comentario de por qué el TextBox lleva <c>TabStop = False</c>. Uno de los dos
''' además tenía el fix del <c>MaxLength</c> y el otro no, así que el informe largo se truncaba en
''' silencio en una de las dos ventanas: exactamente lo que una duplicación produce.</para>
''' <para><b>Botones.</b> Los tres viven en el Designer y el ctor elige cuáles se ven. El ORDEN en que
''' están declarados no es cosmético: con <c>Dock = Right</c> el borde derecho se lo lleva el último
''' agregado y un botón oculto no deja hueco (medido en <c>Tools\DesignerCostProbe</c>, Q1), así que un
''' único orden <c>[Apply, Close, Copy]</c> reproduce las dos disposiciones que tenían los originales.</para>
''' </summary>
Friend Class TextReport_Form

    ''' <param name="title">Título de la ventana. Dice de qué es el informe; no repite el veredicto —
    ''' un título que contradiga lo que el usuario acaba de leer en el cartel deshace el cartel.</param>
    ''' <param name="body">Texto del informe. Se normalizan los saltos de línea a CRLF.</param>
    ''' <param name="showApply">True para el modal de decisión (Apply / Cancel): <c>ShowDialog</c>
    ''' devuelve <see cref="DialogResult.OK"/> si el usuario aplica.</param>
    ''' <param name="showCopy">True para ofrecer "Copy" (el informe al portapapeles).</param>
    Public Sub New(title As String, body As String,
                   Optional showApply As Boolean = False,
                   Optional showCopy As Boolean = False)
        InitializeComponent()
        Text = title
        TextBoxReport.Text = NormalizeEol(body)
        ButtonApply.Visible = showApply
        ButtonCopy.Visible = showCopy
        ' Un botón que cierra sin hacer nada se llama "Close"; el mismo botón, cuando hay un "Apply" al
        ' lado, es la mitad negativa de una decisión y se llama "Cancel".
        ButtonClose.Text = If(showApply, "Cancel", "Close")
        AcceptButton = If(showApply, CType(ButtonApply, IButtonControl), ButtonClose)
    End Sub

    ''' <summary>Aunque el foco inicial no va al TextBox (TabStop=False), colapsar la selección al abrir
    ''' garantiza que el informe se vea limpio también si el usuario lo clica después.</summary>
    Private Sub TextReport_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        TextBoxReport.Select(0, 0)
    End Sub

    Private Sub ButtonCopy_Click(sender As Object, e As EventArgs) Handles ButtonCopy.Click
        Try
            If TextBoxReport.TextLength > 0 Then Clipboard.SetText(TextBoxReport.Text)
        Catch
            ' El portapapeles lo puede tener tomado otro proceso. No hay nada que informar: el usuario
            ' ve el texto en pantalla y puede seleccionarlo a mano.
        End Try
    End Sub

    ''' <summary>Normaliza saltos de línea a CRLF SIN duplicarlos. <c>StringBuilder.AppendLine</c> ya emite
    ''' <c>Environment.NewLine</c> (= CRLF en Windows), así que un <c>Replace(vbLf, vbCrLf)</c> directo
    ''' convertía cada CRLF en CR+CRLF y metía líneas en blanco de más en el TextBox.</summary>
    Private Shared Function NormalizeEol(s As String) As String
        If String.IsNullOrEmpty(s) Then Return ""
        Return s.Replace(vbCrLf, vbLf).Replace(vbCr, vbLf).Replace(vbLf, vbCrLf)
    End Function

End Class
