Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Modal dialog that lets the user pick one HDPT for a given (RACE, gender, partType)
''' triple. Filters the master plugin HDPT enumeration by:
'''   - HDPT.PartType matches the requested type (Face / Hair / Eyes / etc).
'''   - HDPT.Flags &amp; IsExtra == 0 (extras are HNAM addons, regenerated automatically by the engine
'''     from the parent main HDPT — never selected directly).
'''   - HDPT.Flags gender-bit matches the NPC's gender. The bit layout of HDPT.DATA in this
'''     codebase is by-position (bit 1 = Male, bit 2 = Female) per the comment block at
'''     MainForm.vb:78-87. An HDPT with NEITHER bit set is treated as gender-neutral (uncommon
'''     but exists in mods); the user sees it regardless.
'''   - HDPT.RNAM (ValidRacesFormID) either 0 (no restriction) OR points to a FLST that contains
'''     the NPC's race FormID. Vanilla HDPTs typically restrict to HumanRace / GhoulRace via FLST.
'''
''' Returns the chosen FormID via <see cref="SelectedFormID"/> when DialogResult is OK.
''' </summary>
Public Class HeadPartPicker_Form

    ' HDPT.DATA flag bits — match the constants documented at MainForm.vb:78-87 (by-position
    ' interpretation of the flags byte, validated empirically against Cait's HDPT byte 0x35).
    ' ⛔ Los bits de `DATA` se fueron a `HeadPartResolver` (BitMacho / BitHembra / BitEsExtra):
    ' acá eran constantes PRIVADAS de un formulario, o sea que ningún testigo podía leerlas y el
    ' panel de validez del editor tenía su propia copia de los mismos números.

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _pluginManager As PluginManager
    ''' <summary>La sede de resolución: los cuatro filtros y el preview salen de acá, así que un
    ''' borrador se filtra y se previsualiza con la misma ley que un record real.</summary>
    Private ReadOnly _res As ResolucionDeHeadParts
    Private ReadOnly _candidates As New List(Of Candidate)
    Private _filtered As List(Of Candidate)

    ''' <summary>El (raza, género, tipo) con el que abrió: los tres botones re-construyen la lista
    ''' después de editar, y para eso hace falta recordar por qué se filtró.</summary>
    ''' <summary>El NPC de contexto, para que el editor abra con sujeto: su panel de validez y su
    ''' botón «Fit to this NPC» son lo que hace que un record nuevo pueda entrar en ESTA lista. 0 = sin
    ''' sujeto, y el editor lo dice en vez de quedarse mudo.</summary>
    Private ReadOnly _npcFormID As UInteger
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean
    Private ReadOnly _partType As Integer
    Private ReadOnly _raceDefaults As New HashSet(Of UInteger)

    Public Property SelectedFormID As UInteger

    ''' <summary>GLControl that previews the selected HDPT's NIF. Created in code-behind because
    ''' GLControl needs an OpenGL context that the Visual Studio Designer can't provide. The
    ''' preview is intentionally minimal: just the NIF shapes, no skinning resolver, no morphs,
    ''' no tints. The user only needs to see what they're picking.</summary>
    Private _preview As PreviewControl
    Private _previewLoadInProgress As Boolean
    Private _lastPreviewFormID As UInteger

    Private Class Candidate
        Public FormID As UInteger
        Public EditorID As String
        Public FullName As String
        Public Plugin As String
        ''' <summary>True para un borrador de esta sesión: gobierna «Edit selected…» y la columna Plugin.</summary>
        Public EsBorrador As Boolean
    End Class

    ''' <summary>Build the picker for a (race, gender, partType) tuple.</summary>
    ''' <param name="pluginManager">Master plugin manager — we walk all loaded HDPTs from it.</param>
    ''' <param name="raceFormID">FormID of the NPC's race; used both for the header label and
    ''' for filtering by HDPT.RNAM FLST.</param>
    ''' <param name="raceEditorID">Display name of the race for the header.</param>
    ''' <param name="isFemale">Gender filter applied to HDPT.DATA flags.</param>
    ''' <param name="partType">PNAM type to filter by (1=Face, 2=Eyes, 3=Hair, 4=Facial Hair,
    ''' 5=Scar, 6=Eyebrows, 7=Meatcaps, 8=Teeth, 9=Head Rear).</param>
    ''' <param name="partTypeLabel">Human-readable label of the part type for the header.</param>
    ''' <param name="res">La sede de resolución de head parts. ⛔ OBLIGATORIA: sin ella el picker
    ''' enumeraría sólo <c>GetRecordsOfType("HDPT")</c>, o sea los records del ORDEN DE CARGA, y un head
    ''' part propio recién creado no aparecería en la lista de la que salió.</param>
    ''' <param name="mainForm">⛔ OBLIGATORIO, y no <c>Optional</c>. Dos cosas lo necesitan y ninguna
    ''' es cosmética: (a) los borradores NUEVOS no tienen record en ningún plugin, así que la única forma
    ''' de ENUMERARLOS es preguntarle a quien los tiene —la sede resuelve por FormID pero no enumera—, y
    ''' sin eso un head part propio recién creado no aparece en esta lista; (b) los botones New / Override
    ''' / Edit crean y registran borradores. Un <c>Optional … = Nothing</c> acá sería el mismo centinela
    ''' que esta ola vino a matar, con la misma consecuencia silenciosa.</param>
    Public Sub New(mainForm As MainForm,
                   pluginManager As PluginManager,
                   res As ResolucionDeHeadParts,
                   npcFormID As UInteger,
                   raceFormID As UInteger,
                   raceEditorID As String,
                   isFemale As Boolean,
                   partType As Integer,
                   partTypeLabel As String,
                   Optional raceDefaultHeadPartFormIDs As IEnumerable(Of UInteger) = Nothing)
        InitializeComponent()
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm),
                "Sin el formulario principal este selector no puede ENUMERAR los borradores (no tienen " &
                "record en ningún plugin) ni crear uno: el head part propio del usuario no aparecería " &
                "en la lista de la que salió.")
        _mainForm = mainForm
        _pluginManager = pluginManager
        ' ⛔ TIRA, no sustituye. Acá había `If(res, SinBorradores(pluginManager))`, o sea
        ' «Nothing = resolvé sin borradores» — el centinela exacto que esta ola vino a matar, y en el
        ' único sitio donde la consecuencia es la que el propio parámetro documenta: el head part que el
        ' usuario acaba de crear no aparecería en la lista de la que salió. El parámetro NO es Optional,
        ' así que sólo se llega pasando Nothing a propósito.
        If res Is Nothing Then Throw New ArgumentNullException(NameOf(res),
                "La sede de resolución de head parts es OBLIGATORIA: sin ella este formulario enumeraría " &
                "sólo los records del ORDEN DE CARGA y un head part propio no aparecería en la lista " &
                "de la que salió. En la app es MainForm.HeadPartsResolution; en un camino sin " &
                "editores, ResolucionDeHeadParts.SinBorradores(plugins) — que lo DECLARA.")
        _res = res
        Text = $"Add {partTypeLabel}"
        LabelHeader.Text = $"{partTypeLabel} for race '{raceEditorID}' ({If(isFemale, "Female", "Male")}). Choose one:"

        Dim raceDefaultsSet As New HashSet(Of UInteger)
        If raceDefaultHeadPartFormIDs IsNot Nothing Then
            For Each fid In raceDefaultHeadPartFormIDs
                raceDefaultsSet.Add(fid)
            Next
        End If

        _npcFormID = npcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale
        _partType = partType
        For Each fid In raceDefaultsSet
            _raceDefaults.Add(fid)
        Next

        BuildCandidates(raceFormID, isFemale, partType, raceDefaultsSet)
        _filtered = New List(Of Candidate)(_candidates)
        RefreshList()
        ActualizarBotonesDeMisRecords()

        AddHandler ButtonNewHdpt.Click, AddressOf OnNuevoHdpt
        AddHandler ButtonOverrideHdpt.Click, AddressOf OnOverrideDelSeleccionado
        AddHandler ButtonEditHdpt.Click, AddressOf OnEditarElSeleccionado
        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        AddHandler ListViewParts.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ListViewParts.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler ButtonOk.Click, AddressOf OnOk
        SortableListView.Attach(ListViewParts)
    End Sub

    Private Sub BuildCandidates(raceFormID As UInteger, isFemale As Boolean, partType As Integer, raceDefaults As HashSet(Of UInteger))
        If _pluginManager Is Nothing Then Return
        Dim hdptRecords = _pluginManager.GetRecordsOfType("HDPT")
        If hdptRecords Is Nothing Then Return


        Dim totalScanned As Integer = 0
        Dim filteredPartType As Integer = 0
        Dim filteredIsExtra As Integer = 0
        Dim filteredGender As Integer = 0
        Dim filteredRace As Integer = 0
        Dim accepted As Integer = 0

        For Each rec In hdptRecords
            totalScanned += 1
            Dim hdpt = _res.Hdpt(rec.Header.FormID)
            If hdpt Is Nothing Then Continue For

            ' ⛔⛔ LOS CUATRO FILTROS SALEN DE LA SEDE. Acá estaban escritos a mano, y el panel de
            ' validez del editor los volvía a escribir DISTINTO — sin el del tipo, y con el de «es
            ' un extra» sin la excepción de Misc —, o sea que el panel prometia cosas que este
            ' selector no cumplía. El porqué de cada filtro (y por qué el de extra no aplica a
            ' Misc) vive en `HeadPartResolver.FiltrarParaPicker`, que es donde se puede medir.
            '   Los contadores se conservan: son el instrumento de este formulario — sin ellos, una
            ' lista vacía no distingue «no hay candidatos» de «los filtré todos».
            Dim filtros = HeadPartResolver.FiltrarParaPicker(hdpt, hdpt.FormID, partType,
                                                            raceFormID, isFemale, _res, raceDefaults)
            If Not filtros.TipoOk Then
                filteredPartType += 1
                Continue For
            End If
            If Not filtros.NoEsExtraOk Then
                filteredIsExtra += 1
                Continue For
            End If
            If Not filtros.GeneroOk Then
                filteredGender += 1
                Continue For
            End If
            If Not filtros.RazaOk Then
                filteredRace += 1
                Continue For
            End If

            accepted += 1
            _candidates.Add(New Candidate With {
                .FormID = hdpt.FormID,
                .EditorID = If(hdpt.EditorID, ""),
                .FullName = If(hdpt.Name, ""),
                .Plugin = ResolvePluginName(rec),
                .EsBorrador = Borradores.EsFormIdDeBorrador(hdpt.FormID)
            })
        Next

        ' ⛔⛔ LOS BORRADORES NUEVOS SE AGREGAN APARTE, y ésta es la razón por la que este formulario
        ' recibe el MainForm. La vuelta de arriba recorre `GetRecordsOfType("HDPT")`, o sea el ORDEN DE
        ' CARGA: un borrador OVERRIDE aparece igual —su FormID es real y la sede lo resuelve a la vista
        ' editada—, pero un borrador NUEVO no tiene record en ningún plugin, así que NO ESTÁ EN ESA
        ' ENUMERACIÓN Y NO PODÍA APARECER NUNCA. El head part propio existía, se resolvía, se
        ' previsualizaba… y era invisible justo en el formulario donde el usuario va a elegirlo.
        ' La sede resuelve por FormID y no enumera, así que no podía taparlo.
        For Each d In _mainForm.HdptDrafts()
            If d?.Record Is Nothing Then Continue For
            If Not d.IsNew Then Continue For          ' el override ya entró por la enumeración
            If _candidates.Any(Function(c) c.FormID = d.FormID) Then Continue For
            Dim v = _res.Hdpt(d.FormID)
            If v Is Nothing Then Continue For
            ' LOS MISMOS CUATRO FILTROS, Y AHORA LITERALMENTE LOS MISMOS: la sede. No se le regala la
            ' entrada a un borrador — si no le entra al NPC, el editor lo dice en su panel de validez
            ' y ahí se arregla —, y el comentario «los mismos» dejaba de ser verdad en cuanto alguien
            ' tocaba una de las dos copias.
            '   ⛔ El FormID que se le pasa es el del BORRADOR, no `v.FormID`: el encabezado del record
            ' de un borrador nuevo es CERO y la sede lo indexa por la identidad del borrador.
            If Not HeadPartResolver.FiltrarParaPicker(v, d.FormID, partType, raceFormID,
                                                      isFemale, _res, raceDefaults).Pasa Then Continue For
            accepted += 1
            _candidates.Add(New Candidate With {
                .FormID = d.FormID,
                .EditorID = If(v.EditorID, ""),
                .FullName = If(v.Name, ""),
                .Plugin = "(new — not saved yet)",
                .EsBorrador = True
            })
        Next

        _candidates.Sort(Function(a, b) String.Compare(a.EditorID, b.EditorID, StringComparison.OrdinalIgnoreCase))
    End Sub

    ''' <summary>Re-construye la lista entera después de crear o editar un head part, y vuelve a dejar
    ''' seleccionado el FormID pedido si sigue pasando los filtros. ⛔ Si NO pasa —el caso normal de un
    ''' record nuevo en blanco, que todavía no tiene tipo— se dice por qué en vez de dejar la lista igual
    ''' y que parezca que el botón no hizo nada.</summary>
    Private Sub ReconstruirYSeleccionar(fid As UInteger)
        _candidates.Clear()
        BuildCandidates(_raceFormID, _isFemale, _partType, _raceDefaults)
        OnFilterChanged(Me, EventArgs.Empty)
        ActualizarBotonesDeMisRecords()
        If fid = 0UI Then Return
        For Each it As ListViewItem In ListViewParts.Items
            Dim c = TryCast(it.Tag, Candidate)
            If c IsNot Nothing AndAlso c.FormID = fid Then
                it.Selected = True
                it.EnsureVisible()
                ListViewParts.Focus()
                ActualizarBotonesDeMisRecords()
                Return
            End If
        Next
        MessageBox.Show(Me,
            "The head part was saved as a draft, but it does not pass this list's filters yet " &
            "(part type, gender bit, or valid-races list), so it is not shown here. Open it again with " &
            "'New...' and use 'Fit to this NPC' in the editor.",
            "Head part", MessageBoxButtons.OK, MessageBoxIcon.Information)
    End Sub

    Private Sub ActualizarBotonesDeMisRecords()
        Dim sel = SeleccionActual()
        ButtonOverrideHdpt.Enabled = (sel IsNot Nothing AndAlso Not sel.EsBorrador)
        ButtonEditHdpt.Enabled = (sel IsNot Nothing)
    End Sub

    Private Function SeleccionActual() As Candidate
        If ListViewParts.SelectedItems.Count = 0 Then Return Nothing
        Return TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
    End Function

    ''' <summary>«New…» — un HDPT propio en blanco. Abre el editor con el NPC de contexto puesto, así que
    ''' su panel de validez y su botón «Fit to this NPC» pueden hacer que el record nuevo entre en ESTA
    ''' lista sin que el usuario tenga que adivinar qué filtro le falta.</summary>
    Private Sub OnNuevoHdpt(sender As Object, e As EventArgs)
        AbrirEditor(0UI, esOverride:=False)
    End Sub

    ''' <summary>«Override selected…» — el record REAL elegido, con su FormID, editado por mi plugin. Sólo
    ''' se habilita sobre una fila que no sea ya un borrador: sobre un borrador el gesto es «Edit».</summary>
    Private Sub OnOverrideDelSeleccionado(sender As Object, e As EventArgs)
        Dim c = SeleccionActual()
        If c Is Nothing OrElse c.EsBorrador Then Return
        AbrirEditor(c.FormID, esOverride:=True)
    End Sub

    ''' <summary>«Edit selected…» — si la fila YA es un borrador propio lo reabre; si es un record real
    ''' del orden de carga, editarlo ES un override, así que entra por la misma puerta. No hay un tercer
    ''' camino: el editor decide por el FormID, que es la única fuente de verdad sobre qué es la fila.</summary>
    Private Sub OnEditarElSeleccionado(sender As Object, e As EventArgs)
        Dim c = SeleccionActual()
        If c Is Nothing Then Return
        AbrirEditor(c.FormID, esOverride:=Not c.EsBorrador)
    End Sub

    ''' <summary>Abre el editor. El FormID es la ÚNICA fuente de verdad sobre qué se abre: si ya tiene
    ''' borrador propio se edita ése, si es un record real se abre como override, y 0 es uno nuevo en
    ''' blanco. Así los tres botones entran por la misma puerta y no hay un modo por botón.</summary>
    Private Sub AbrirEditor(fid As UInteger, esOverride As Boolean)
        Dim borradorPrevio = If(fid = 0UI, Nothing, _mainForm.HdptDraftPorFormId(fid))
        Dim resultado As UInteger = 0UI
        Using dlg As New HeadPartEditor_Form(_mainForm, _npcFormID, _raceFormID, _isFemale,
                                             editDraft:=borradorPrevio,
                                             initialOverrideFormID:=If(borradorPrevio Is Nothing AndAlso esOverride, fid, 0UI))
            dlg.ShowDialog(Me)
            resultado = dlg.ResultHdptFormID
        End Using
        ' El editor commitea VIVO, así que al cerrar el borrador ya está registrado pase lo que pase con
        ' el DialogResult: se reconstruye igual.
        ReconstruirYSeleccionar(resultado)
    End Sub

    Private Function ResolvePluginName(rec As PluginRecord) As String
        ' PluginRecord.SourcePluginName is set by PluginReader.vb:119 at parse time. Empty when
        ' the parser couldn't attribute the record (defensive fallback only).
        If rec Is Nothing OrElse String.IsNullOrEmpty(rec.SourcePluginName) Then Return "?"
        Return rec.SourcePluginName
    End Function

    Private Sub RefreshList()
        ListViewParts.BeginUpdate()
        Try
            ListViewParts.Items.Clear()
            For Each c In _filtered
                Dim row As New ListViewItem(c.EditorID)
                row.SubItems.Add(c.FullName)
                row.SubItems.Add(c.Plugin)
                row.SubItems.Add($"{c.FormID:X8}")
                row.Tag = c
                ListViewParts.Items.Add(row)
            Next
        Finally
            ListViewParts.EndUpdate()
        End Try
    End Sub

    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        Dim text = TextBoxFilter.Text.Trim()
        If text.Length = 0 Then
            _filtered = New List(Of Candidate)(_candidates)
        Else
            _filtered = _candidates.Where(Function(c) _
                c.EditorID.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
                c.FullName.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList()
        End If
        RefreshList()
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Re-load the selected HDPT's NIF into the right-pane preview. Cheapest pipeline:
    ''' read bytes from FilesDictionary, parse via NifContent_Class, hand the shapes to
    ''' PreviewControl.RenderShapes(shapes) which sets up Intent and runs the pipeline. No
    ''' skinning resolver / morphs / tints — the user only needs to see the geometry.</summary>
    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        ActualizarBotonesDeMisRecords()
        If _preview Is Nothing OrElse _preview.IsDisposed Then Return
        If _previewLoadInProgress Then Return
        If ListViewParts.SelectedItems.Count = 0 Then
            ClearPreview("(no head part selected)")
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        ' Avoid re-loading the same NIF when the user clicks the already-selected row.
        If c.FormID = _lastPreviewFormID Then Return

        _previewLoadInProgress = True
        Try
            ' ⛔ La construcción de las formas se MUDÓ a `PreviewDeHeadPart.FormasDeLaCadena`, que es
            ' la casa compartida con el editor de head parts. Lo que hacía acá —recorrer la cadena de
            ' HNAM, cargar cada malla por el FilesDictionary y aplicar el TNAM de cada nodo a SUS
            ' formas— sigue siendo exactamente eso, y el porqué de cada paso está allá. Se mudó porque
            ' el editor necesita lo mismo y dos copias divergen en lo que menos se nota (el TNAM de los
            ' ojos: sin aplicarlo, todos los colores salen marrones).
            Dim armado = PreviewDeHeadPart.FormasDeLaCadena(c.FormID, _res)
            Dim allShapes = armado.Formas
            Dim chainCount = armado.NodosDeLaCadena
            If allShapes.Count = 0 Then
                ClearPreview("(no renderable shapes resolved for this HDPT chain)")
                Return
            End If

            ' Synchronous, single-call entry point: PreviewControl applies sane defaults
            ' (no skinning resolver, no morph resolver, identity pose) and runs the pipeline.
            _preview.RenderShapes(allShapes)
            _lastPreviewFormID = c.FormID
        Finally
            _previewLoadInProgress = False
        End Try
    End Sub

    Private Sub ClearPreview(statusMessage As String)
        _lastPreviewFormID = 0UI
        ' Render an empty shape list — the pipeline detects empty shapes and clears the model
        ' (Render.vb:502-512), so the preview goes blank without leaking the previous mesh.
        Try
            _preview?.RenderShapes(New List(Of IRenderableShape))
        Catch
        End Try
    End Sub

    ' ⛔ Acá vivía `NormalizeMeshKey`. Se fue con el preview: su única llamada era de ahí, y la
    ' normalización de la clave del FilesDictionary vive ahora en `PreviewDeHeadPart`, al lado de quien
    ' la usa. Una función privada sin llamadores es una ley sin sede.


    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewParts.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        Dim c = TryCast(ListViewParts.SelectedItems(0).Tag, Candidate)
        If c Is Nothing Then Return
        SelectedFormID = c.FormID
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub HeadPartPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        If _preview Is Nothing OrElse _preview.IsDisposed Then
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        End If

        If ListViewParts.SelectedItems.Count > 0 Then
            OnListSelectionChanged(Me, EventArgs.Empty)
        Else
            ClearPreview("(no head part selected)")
        End If
    End Sub

    Private Sub HeadPartPicker_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            _preview.Clean()
            _preview.Dispose()
        End If
    End Sub
End Class
