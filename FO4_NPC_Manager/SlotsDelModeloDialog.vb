Imports System.Windows.Forms
Imports System.Drawing
Imports FO4_Base_Library

''' <summary>Modal del botón «Slots declared by the models…» del editor de ARMA. Muestra qué declaró
''' cada modelo y ofrece, UNO POR UNO, los biped slots que la malla declara y el BOD2 todavía no tiene.
''' <para>⛔ Abre con TODO DESTILDADO, a propósito. Tildar un slot hace que este addon sea su DUEÑO y
''' desaloje a quien lo tenía, y hay casos vanilla donde eso es exactamente lo que no se quiere: unas
''' botas cuya malla declara el slot de pelo, un efecto de escupida que declara el del cuerpo. El
''' diálogo informa; los bytes los decide el usuario.</para>
''' <para>Construido enteramente en code-behind (sin Designer): son N filas variables y cuatro
''' etiquetas, la misma razón por la que los checkboxes de slots se arman en código.</para></summary>
Friend Class SlotsDelModeloDialog
    Inherits Form

    Private ReadOnly _lista As New CheckedListBox()
    Private ReadOnly _slotDeFila As New List(Of Integer)()

    ''' <summary>Los bits que el usuario tildó. 0 si canceló o no tildó ninguno.</summary>
    Friend ReadOnly Property SlotsElegidos As UInteger
        Get
            Dim m As UInteger = 0UI
            For i = 0 To _slotDeFila.Count - 1
                If _lista.GetItemChecked(i) Then m = m Or (1UI << (_slotDeFila(i) - 30))
            Next
            Return m
        End Get
    End Property

    ''' <summary>Arma el modal.</summary>
    ''' <param name="p">La propuesta ya calculada.</param>
    ''' <param name="avisoHerencia">Aviso sobre el BOD2 vacío y la herencia del ARMO, ya redactado por el
    ''' editor (que es quien conoce al ARMO padre). "" si no hay nada que decir.</param>
    Friend Sub New(p As SlotsDelModelo.Propuesta, avisoHerencia As String)
        Text = "Slots declared by the models"
        FormBorderStyle = FormBorderStyle.FixedDialog
        MinimizeBox = False
        MaximizeBox = False
        StartPosition = FormStartPosition.CenterParent
        ClientSize = New Size(620, 470)

        Dim raiz As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 5, .Padding = New Padding(10)}
        raiz.RowStyles.Add(New RowStyle())
        raiz.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        raiz.RowStyles.Add(New RowStyle())
        raiz.RowStyles.Add(New RowStyle())
        raiz.RowStyles.Add(New RowStyle())
        Controls.Add(raiz)

        raiz.Controls.Add(New Label() With {
            .AutoSize = True, .MaximumSize = New Size(590, 0), .Margin = New Padding(3, 3, 3, 8),
            .Text = TextoDeLecturas(p)}, 0, 0)

        _lista.Dock = DockStyle.Fill
        _lista.CheckOnClick = True
        _lista.IntegralHeight = False
        For bit = 0 To 31
            If (p.Faltan And (1UI << bit)) = 0UI Then Continue For
            Dim slot = bit + 30
            _slotDeFila.Add(slot)
            _lista.Items.Add($"{BipedSlotCheckboxes.SlotName(slot)}   ({TextoDeAtribucion(p, bit)})", False)
        Next
        raiz.Controls.Add(_lista, 0, 1)

        raiz.Controls.Add(New Label() With {
            .AutoSize = True, .MaximumSize = New Size(590, 0), .Margin = New Padding(3, 8, 3, 3),
            .Text = "Ticking a slot makes THIS addon the owner of it: the engine evicts whatever occupied " &
                    "that slot, and geometry whose slot this addon does NOT own is hidden. Slots you already " &
                    "declare without geometry are how an item hides what is underneath — this never removes them." &
                    If(p.Sobran <> 0UI, Environment.NewLine & "Declared without geometry (left untouched): " & Lista(p.Sobran), "")}, 0, 2)

        If Not String.IsNullOrEmpty(avisoHerencia) Then
            raiz.Controls.Add(New Label() With {
                .AutoSize = True, .MaximumSize = New Size(590, 0), .Margin = New Padding(3, 8, 3, 3),
                .Text = avisoHerencia}, 0, 3)
        End If

        Dim botones As New FlowLayoutPanel() With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True}
        Dim bCancel As New Button() With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
        Dim bOk As New Button() With {.Text = "Apply ticked slots", .DialogResult = DialogResult.OK, .AutoSize = True}
        botones.Controls.Add(bCancel)
        botones.Controls.Add(bOk)
        raiz.Controls.Add(botones, 0, 4)
        AcceptButton = bOk
        CancelButton = bCancel
    End Sub

    ''' <summary>Las dos líneas de cabecera: qué se leyó de cada modelo. Los tres estados dicen cosas
    ''' distintas y ninguno se puede confundir con otro.</summary>
    ''' <param name="p">La propuesta.</param>
    Private Shared Function TextoDeLecturas(p As SlotsDelModelo.Propuesta) As String
        Return "Male model:   " & UnaLectura(p.Male, p.MaleNeto) & Environment.NewLine &
               "Female model: " & UnaLectura(p.Female, p.FemaleNeto)
    End Function

    ''' <summary>Una línea de cabecera.</summary>
    ''' <param name="g">La lectura de ese género.</param>
    ''' <param name="neto">Su máscara ya sin el tag del Pip-Boy.</param>
    Private Shared Function UnaLectura(g As SlotsDelModelo.LecturaGenero, neto As UInteger) As String
        Select Case g.Estado
            Case SlotsDelModelo.EstadoDeLectura.SinPath
                Return "(this addon declares no model for this gender)"
            Case SlotsDelModelo.EstadoDeLectura.Ilegible
                Return $"'{g.Ruta}' — could NOT be read (not found as a loose file or inside a game archive)"
            Case Else
                If neto = 0UI Then Return $"'{g.Ruta}' — read; declares no biped slot"
                Return $"'{g.Ruta}' — read; declares {Lista(neto)}"
        End Select
    End Function

    ''' <summary>El TEXTO de la etiqueta de la fila. ⛔ La decisión de QUIÉN declara el slot no se toma
    ''' acá: sale de <see cref="SlotsDelModelo.AtribucionDe"/>, que es la sede y la que ejerce el gate.
    ''' Acá sólo se traduce a palabras.</summary>
    ''' <param name="p">La propuesta.</param>
    ''' <param name="bit">Bit del slot (slot menos 30).</param>
    Private Shared Function TextoDeAtribucion(p As SlotsDelModelo.Propuesta, bit As Integer) As String
        Select Case SlotsDelModelo.AtribucionDe(p, bit)
            Case SlotsDelModelo.Atribucion.Ambos : Return "declared by both models"
            Case SlotsDelModelo.Atribucion.SoloMale : Return "declared by the MALE model only"
            Case SlotsDelModelo.Atribucion.SoloFemale : Return "declared by the FEMALE model only"
            Case SlotsDelModelo.Atribucion.MaleYNoHayFemenino : Return "declared by the male model; this addon has no female model"
            Case SlotsDelModelo.Atribucion.MaleYFemeninoIlegible : Return "declared by the male model; the female model could not be read"
            Case SlotsDelModelo.Atribucion.FemaleYNoHayMasculino : Return "declared by the female model; this addon has no male model"
            Case SlotsDelModelo.Atribucion.FemaleYMasculinoIlegible : Return "declared by the female model; the male model could not be read"
            Case Else : Return "declared by neither model"
        End Select
    End Function

    ''' <summary>"37, 38" a partir de una máscara.</summary>
    ''' <param name="mask">Máscara con bit i = slot 30+i.</param>
    Private Shared Function Lista(mask As UInteger) As String
        Dim l As New List(Of String)()
        For i = 0 To 31
            If (mask And (1UI << i)) <> 0UI Then l.Add((30 + i).ToString())
        Next
        Return String.Join(", ", l)
    End Function

End Class
