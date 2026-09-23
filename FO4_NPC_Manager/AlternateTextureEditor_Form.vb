Imports FO4_Base_Library

''' <summary>Una entrada de <c>Model\MODS\Alternate Textures</c> de un head part de Skyrim: el nombre
''' del nodo 3D, el <c>TXST</c> nuevo y el índice del nodo. Modal chico, al molde de
''' <see cref="HeadPartFileEditor_Form"/>.
'''
''' <para>⛔⛔ <b>Existe porque el censo del esquema me refutó.</b> Yo tenía el editor de head parts
''' sacando la fila de <c>MODS</c> entera en Skyrim, con el comentario «el material swap del modelo no
''' existe en el esquema de Skyrim». La primera mitad es cierta y la conclusión era falsa: <c>MODS</c>
''' está declarado en los DOS juegos y lo que cambia es su FORMA —en Fallout 4 es
''' <c>Wb.Fid("Material Swap", "MSWP")</c>, un FormID solo, y en Skyrim es
''' <c>Wb.ArrayV("Alternate Textures", …)</c>, un arreglo de <c>{3D Name, New Texture, 3D Index}</c>—.
''' Sacar la fila no perdía datos (el borrador ES el record, así que un arreglo que nadie toca
''' sobrevive), pero dejaba un campo que el juego SÍ tiene sin forma de verlo ni editarlo, y el
''' comentario afirmaba que no existía. Censar los subrecords que cada juego declara —17 en Fallout 4,
''' 12 en Skyrim, con <c>CIS1 CIS2 CTDA MODC MODF</c> como los únicos cinco que son de Fallout 4 y
''' ninguno que sea sólo de Skyrim— es lo que lo mostró.</para></summary>
Public Class AlternateTextureEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    Private ReadOnly _mainForm As MainForm

    ''' <summary>El nombre del nodo 3D. Sólo válido con <c>DialogResult.OK</c>.</summary>
    Public ReadOnly Property Name3D As String
        Get
            Return _name3D
        End Get
    End Property
    Private _name3D As String = ""

    ''' <summary>El <c>TXST</c> elegido. 0 = ninguno.</summary>
    Public ReadOnly Property NewTexture As UInteger
        Get
            Return _newTexture
        End Get
    End Property
    Private _newTexture As UInteger = 0UI

    ''' <summary>El índice del nodo 3D.</summary>
    Public ReadOnly Property Index3D As Integer
        Get
            Return _index3D
        End Get
    End Property
    Private _index3D As Integer = 0

    Public Sub New(mainForm As MainForm, nombre As String, txst As UInteger, indice As Integer)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        InitializeComponent()
        _mainForm = mainForm
        _name3D = If(nombre, "")
        _newTexture = txst
        _index3D = indice

        TextBoxName3D.Text = _name3D
        PonerTxst(_newTexture)
        ' El índice es un s32 del esquema, así que el rango del control es el del TIPO y no un tope
        ' elegido: acotarlo a mano sería una regla de la app sobre un campo que el formato no acota.
        NumericIndex3D.Minimum = Integer.MinValue
        NumericIndex3D.Maximum = Integer.MaxValue
        NumericIndex3D.Value = _index3D

        AddHandler ButtonPickTxst.Click, AddressOf OnElegirTxst
        AddHandler ButtonClearTxst.Click, Sub() PonerTxst(0UI)
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    Private Sub PonerTxst(fid As UInteger)
        _newTexture = fid
        TextBoxTxst.Tag = fid
        TextBoxTxst.Text = If(fid = 0UI, "(none)", _mainForm.GetRecordDisplayNameForEditor(fid))
    End Sub

    '==============================================================================================
    ' EL CONTRATO CON LA SEDE DE BAJA — `BorradoDeBorradores.IDuenoDeBorradores`
    '==============================================================================================

    ''' <summary>Este modal no TOMA ningún borrador: edita una fila del arreglo del editor de arriba.</summary>
    Private Function FormIdTomado() As UInteger Implements BorradoDeBorradores.IDuenoDeBorradores.FormIdTomado
        Return 0UI
    End Function

    ''' <summary>⛔⛔ <c>_newTexture</c> NO SALE DE ESTA VENTANA HASTA EL OK: el editor de head parts lo
    ''' lee por <see cref="NewTexture"/> recién cuando el modal devuelve <c>DialogResult.OK</c>. Mientras
    ''' tanto ese TXST no está en ningún record registrado, así que <c>GetDraftReferrers</c> no lo ve y
    ''' «Delete / Revert…» diría que no lo apunta nadie. Borrarlo dejaría la fila apuntando a un 0xFF
    ''' muerto. Es la MISMA forma que <c>ArmoAddonEditor_Form._armaFormID</c>, y el segundo sujeto de
    ''' este miembro del contrato.</summary>
    Private Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String) _
            Implements BorradoDeBorradores.IDuenoDeBorradores.ReferenciasNoVolcadas
        If formID <> 0UI AndAlso formID = _newTexture Then Return New String() {"alternate texture (not accepted yet)"}
        Return Enumerable.Empty(Of String)()
    End Function

    ''' <summary>POSTCONDICIÓN: si dieron de baja el TXST elegido, se suelta — aceptar con un FormID
    ''' recién borrado escribiría una referencia muerta.</summary>
    Private Sub TrasLaBaja(formID As UInteger) Implements BorradoDeBorradores.IDuenoDeBorradores.TrasLaBaja
        If formID = 0UI OrElse formID <> _newTexture Then Return
        PonerTxst(0UI)
    End Sub

    ''' <summary>⛔ OFRECE LOS BORRADORES DE TXST: es la segunda mitad de F3. Hasta esta ola la baja de
    ''' un conjunto de texturas vivía en UN solo campo de OTRO editor, así que uno propio no se podía
    ''' elegir acá ni sacar desde acá.
    ''' <para>⚠️ Este camino es de SKYRIM ÚNICAMENTE — <c>Alternate Textures</c> no existe en el esquema
    ''' de Fallout 4 (medido: cero declaraciones en las vistas de FO4). El eje del gate que lo mide tiene
    ''' que decir «no aplica» en FO4 en vez de salir SIN SUJETO.</para></summary>
    Private Sub OnElegirTxst(sender As Object, e As EventArgs)
        Dim entradas = _mainForm.TxstDrafts().Select(Function(d) New FormIdPickerEntry With {
            .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
            .Signature = "TXST", .PluginName = If(d.IsOverride, "(override)", "(new)")}).ToList()
        entradas.AddRange(BorradoDeBorradores.EntradasPropias(_mainForm, "TXST", entradas))
        Using dlg As FormIdPicker_Form = If(entradas.Count = 0,
                New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"TXST"},
                                      "Select the replacement texture set (TXST)", _newTexture, True),
                FormIdPicker_Form.ParaBorradores(_mainForm, Me, {"TXST"},
                                                 "Select the replacement texture set (TXST)",
                                                 _newTexture, True, entradas))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            PonerTxst(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' ⛔ El nombre del nodo 3D es lo que ata la entrada a una forma del NIF: sin él, la entrada no
        ' puede aplicarse a nada y el motor la ignora. Se rechaza en vez de escribir una entrada muerta.
        If TextBoxName3D.Text.Trim().Length = 0 Then
            MessageBox.Show(Me, "The 3D name is what ties this entry to a shape in the NIF. Without it the " &
                               "entry cannot apply to anything.",
                            "Alternate texture", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If
        _name3D = TextBoxName3D.Text.Trim()
        _index3D = CInt(NumericIndex3D.Value)
    End Sub

End Class
