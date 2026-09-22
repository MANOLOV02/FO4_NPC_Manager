Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Una entrada del array <c>Parts</c> de un head part: el tipo (<c>NAM0</c>) y el archivo
''' (<c>NAM1</c>). Modal chico, al molde de los sub-editores de OBTS.
'''
''' <para>Los tres valores del enum son los mismos en los dos juegos —0 Race Morph, 1 Tri, 2 Chargen
''' Morph— y salen del esquema, no de una lista a mano. Medido sobre el corpus: Fallout 4 usa 1 y 2
''' (119 y 2.447 entradas) y Skyrim los tres (707 / 4.874 / 1.749).</para>
'''
''' <para>⛔ El combo es <c>DropDown</c>: un valor fuera del enum se muestra crudo y se conserva. Misma
''' ley que el <c>PNAM</c> del editor, por el mismo motivo medido.</para></summary>
Public Class HeadPartFileEditor_Form

    Private ReadOnly _game As Canon.WbGame

    ''' <summary>El tipo elegido. Sólo válido con <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property PartType As UInteger
        Get
            Return _partType
        End Get
    End Property
    ''' <summary>Las claves del enum en el orden de los items del combo: el valor sale del índice y no
    ''' de parsear el rótulo. Ver <see cref="PonerTipo"/>.</summary>
    Private ReadOnly _clavesDeTipo As New List(Of UInteger)
    Private _tipoCrudoFueraDelEnum As UInteger = 0UI

    Private _partType As UInteger

    ''' <summary>El archivo elegido. Sólo válido con <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property FileName As String
        Get
            Return _fileName
        End Get
    End Property
    Private _fileName As String = ""

    Public Sub New(game As Canon.WbGame, partTypeActual As UInteger, archivoActual As String)
        InitializeComponent()
        _game = game
        _partType = partTypeActual
        _fileName = If(archivoActual, "")
        LabelHint.Text = "Race Morph = the .tri the race uses for morphs · Tri = the part's own .tri · " &
                         "Chargen Morph = the chargen .tri. The path is relative to Data\Meshes."
        For Each kv In Canon.CanonInterpretacion.NombresDeTipoDeParteDeHeadPart(_game).OrderBy(Function(x) x.Key)
            ComboPartType.Items.Add($"{kv.Key} — {kv.Value}")
            _clavesDeTipo.Add(CUInt(kv.Key))
        Next
        PonerTipo(partTypeActual)
        TextBoxFile.Text = _fileName
        AddHandler ButtonBrowse.Click, AddressOf OnBuscar
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ''' <summary>Elige por ÍNDICE en la lista cerrada. Un valor que el enum no nombra entra como una
    ''' entrada extra propia y queda elegido, así que aceptar no lo reescribe. Medido: el corpus no trae
    ''' ninguno hoy, pero el campo es <c>u32</c> y el orden de carga del usuario cambia.</summary>
    Private Sub PonerTipo(valor As UInteger)
        Dim idx = _clavesDeTipo.IndexOf(valor)
        If idx >= 0 Then
            ComboPartType.SelectedIndex = idx
            LabelPartTypeRaw.Text = ""
            Return
        End If
        _tipoCrudoFueraDelEnum = valor
        ComboPartType.Items.Add($"{valor} — (not named by this game's enum; kept as-is)")
        ComboPartType.SelectedIndex = ComboPartType.Items.Count - 1
        LabelPartTypeRaw.Text = $"(raw {valor} — not named by this game's enum; kept as-is)"
    End Sub

    ''' <summary>El picker del DICCIONARIO DE ARCHIVOS, no un diálogo del sistema.
    ''' <para>⛔ Un <c>OpenFileDialog</c> ve el disco y NADA MAS: los <c>.tri</c> vanilla viven DENTRO
    ''' de los BA2 (Fallout 4) y los BSA (Skyrim), así que con el diálogo del sistema el usuario no
    ''' puede elegir ninguno de los que el juego trae —sólo los que algún mod dejó sueltos en Data—.
    ''' El diccionario de archivos ya indexa los dos mundos y es el mismo camino que usa el render
    ''' para abrirlos, así que lo que se elige acá es exactamente lo que el render va a encontrar.</para>
    ''' <para>La clave del picker lleva el prefijo <c>Meshes\</c> y el record lo guarda SIN él: se
    ''' agrega para sembrar y se saca al volver, igual que en el selector de mallas.</para>
    ''' <para>⛔⛔ Y ESO ES LO QUE ACÁ NO PASABA, aunque el párrafo de arriba lo prometía: la semilla se
    ''' pasaba PELADA (<c>TextBoxFile.Text.Trim()</c>), o sea sin el <c>Meshes\</c>, y
    ''' <c>SelectFileByKey</c> exige la clave COMPLETA para encontrarla en su conjunto — así que este
    ''' picker NO preseleccionaba NUNCA y siempre abría en la raíz del árbol. Un comentario que
    ''' describe lo que el código debería hacer es peor que ninguno: lo leí como cumplido y escribí la
    ''' versión correcta al lado en vez de arreglar ésta. Ahora las dos son LA MISMA
    ''' (<see cref="PickerDeAssets"/>), que es la única forma de que no vuelvan a divergir.</para></summary>
    Private Sub OnBuscar(sender As Object, e As EventArgs)
        PickerDeAssets.ElegirEnCaja(Me, TextBoxFile, FilesDictionary_class.TriDictionary_Filter)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        Dim i = ComboPartType.SelectedIndex
        If i < 0 Then
            MessageBox.Show(Me, "Pick a part type.", "Head part file",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
        _partType = If(i < _clavesDeTipo.Count, _clavesDeTipo(i), _tipoCrudoFueraDelEnum)
        _fileName = TextBoxFile.Text.Trim()
    End Sub

End Class
