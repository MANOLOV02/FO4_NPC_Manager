Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion
Imports MaterialLib

''' <summary>EL EDITOR DE HEAD PARTS (<c>HDPT</c>): uno nuevo, o un override de uno que existe.
'''
''' <para>Es el gemelo de <see cref="ArmaEditor_Form"/> / <see cref="ArmoEditor_Form"/> y comparte su
''' esqueleto entero: barra de objetivo (nuevo en blanco / desde plantilla / override / editar el mío),
''' banner de estado, volcado y commit, pickers de FormID, y el protocolo de toma-y-abandono sobre el
''' registro (<see cref="TomaDeBorrador(Of TD)"/>). El porqué de cada una de esas piezas está escrito
''' allá y acá no se repite.</para>
'''
''' <para>⛔ <b>EL BORRADOR ES EL RECORD.</b> Cada gesto del usuario se vuelca al árbol del borrador y
''' se REGISTRA en el momento (commit vivo), que es lo que hace que el preview, el guardado y los
''' pickers vean lo mismo. Y por eso vale la otra mitad de la ley: <b>QUIEN MUTA, PUBLICA</b> — cada
''' mutación vuelve a publicar la foto (<c>MainForm.RegisterHdptDraft</c> lo hace por dentro), porque el
''' render y el bake leen la FOTO y no el árbol vivo.</para>
'''
''' <para>⛔ <b>Lo que este editor NO normaliza.</b> Dos campos pueden traer valores que la lista
''' cerrada del esquema no nombra, y en los dos casos el control los MUESTRA y los CONSERVA:
''' <list type="bullet">
''' <item><c>PNAM</c> (tipo): el combo es <c>DropDownList</c> —la lista del esquema es cerrada, y tipear
''' a mano no puede producir nada válido que la lista no tenga— y el valor fuera del enum se conserva
''' con una <b>entrada extra</b>. Medido: cinco HDPT del orden de carga del usuario traen 69 y 71
''' (<c>UBE_AllRace.esp</c>), que el enum de Skyrim (0–6) no nombra; al abrir uno de ésos el combo recibe
''' una entrada con el valor crudo y queda elegida, así que aceptar el record no lo reescribe. Es la
''' misma ley que el operador de una condición, y por eso el mismo control.</item>
''' <item>el operador de una condición: son SEIS y no cinco — ver <see cref="ConditionsEditor_Form"/>.</item>
''' </list></para>
'''
''' <para>⛔ <b>Y opera por <c>TryCast</c> a la vista concreta de Fallout 4</b> para los cinco campos que
''' <c>IHdpt</c> —que es la intersección de los dos juegos— no expone: <c>MODC</c>, <c>MODF</c>, el
''' <c>MODS</c> que en FO4 es un material swap, el bit 5 de <c>DATA</c> y las condiciones. Un editor
''' escrito sólo contra la interfaz compila, anda, y los pierde.</para></summary>
Public Class HeadPartEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _res As ResolucionDeHeadParts
    Private ReadOnly _plugins As PluginManager
    Private ReadOnly _game As Canon.WbGame
    Private ReadOnly _esFo4 As Boolean

    ' Contexto del NPC: para el panel de validez y para «Fit to this NPC». 0 ⇒ no hay sujeto, y el panel
    ' lo dice en vez de quedarse mudo.
    Private ReadOnly _npcFormID As UInteger
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _isFemale As Boolean

    ''' <summary>Las claves del enum de <c>PNAM</c>, en el MISMO orden que los items del combo: el valor
    ''' se lee por índice y no parseando el rótulo. Si el record trae un valor que el enum no nombra, el
    ''' combo lleva UNA entrada más que esta lista y <see cref="_tipoCrudoFueraDelEnum"/> la respalda.</summary>
    Private ReadOnly _clavesDeTipo As New List(Of UInteger)
    Private _tipoCrudoFueraDelEnum As UInteger = 0UI
    Private _hayEntradaDeTipoCruda As Boolean = False

    Private _draft As HdptDraft
    Private _openSnapshot As HdptDraft
    Private _toma As TomaDeBorrador(Of HdptDraft)
    Private _cargando As Boolean

    Private _preview As PreviewControl
    Private _lastPreviewKey As String = Nothing

    ''' <summary>El anfitrion de render del modo «Over the NPC». Nothing cuando no hay NPC de contexto —
    ''' el modo entero queda deshabilitado y sobra el anfitrion. Gemelo del de <c>ArmaEditor_Form</c>.</summary>
    Private _host As NpcRenderHost
    Private _previewDebounce As Timer
    Private _previewEnCurso As Boolean = False
    ''' <summary>Se apaga el reintento despues de un fallo: este metodo corre en CADA tecla, asi que sin
    ''' esto un render que tira vuelve a tirar por cada caracter. Misma guarda que
    ''' <c>ArmaEditor_Form._previewCommitFallado</c>.</summary>
    Private _previewFallado As Boolean = False
    ''' <summary>True mientras el gateo del selector esta moviendo los controles, para que el
    ''' <c>CheckedChanged</c> que el propio gateo dispara no pida un render de mas.</summary>
    Private _gateandoPreview As Boolean = False

    ''' <summary>True cuando el objetivo actual se ADOPTÓ del registro de borradores («Edit mine…», o
    ''' un «Override…» sobre un FormID que ya tenía borrador) y no se creó en este gesto. Lo necesita el
    ''' banner para poder decir «Editing your draft» en vez de «NEW record». Gemelo de
    ''' <c>ArmaEditor_Form._editingExistingDraft</c>.</summary>
    Private _editandoBorradorExistente As Boolean = False

    ''' <summary>El FormID que este editor dejó commiteado: el centinela provisional si es nuevo, el real
    ''' si es un override. 0 hasta el primer commit válido — el llamador lo usa para saber si tiene que
    ''' re-apuntar la fila desde la que lo abrió.</summary>
    ''' <summary>El editor TOCÓ el registro de borradores — registró, editó o dio de baja alguno.
    ''' <para>⛔ Es lo que decide si el preview PRINCIPAL se recarga al salir, y es la misma ley que
    ''' <c>EditBody_Form.HasUncommittedChanges</c>: el preview de atrás NO se toca durante el modal
    ''' — este editor dibuja en su propio host —, así que si no cambió ningún borrador sigue siendo
    ''' correcto y recargarlo es un re-render de más.</para>
    ''' <para>⛔ NO alcanza con mirar <see cref="ResultHdptFormID"/>: dar de baja un borrador desde el
    ''' selector cambia lo que el render resuelve y ese gesto puede terminar en Cancel.</para></summary>
    Public ReadOnly Property HuboCambios As Boolean
        Get
            Return _huboCambios
        End Get
    End Property
    Private _huboCambios As Boolean

    Public ReadOnly Property ResultHdptFormID As UInteger
        Get
            Return _resultHdptFormID
        End Get
    End Property
    Private _resultHdptFormID As UInteger = 0UI

    '==============================================================================================
    ' Construcción
    '==============================================================================================

    ''' <param name="mainForm">Dueño: aporta los registradores de borrador, el PluginManager de los
    ''' pickers y LA SEDE de resolución (la misma instancia que el render y el bake).</param>
    ''' <param name="npcFormID">El NPC seleccionado, como sujeto del panel de validez. 0 = sin sujeto.</param>
    ''' <param name="editDraft">Cuando viene, edita ESE borrador directo en vez de arrancar en blanco.</param>
    ''' <param name="initialOverrideFormID">Cuando viene (y no hay <paramref name="editDraft"/>), abre
    ''' como OVERRIDE de ese record real — es la puerta desde la fila de un head part que el NPC ya tiene.</param>
    ''' <summary>La toma se rechazó porque ese borrador ya está abierto en otro editor. Lo mira el
    ''' llamador: cerrar un formulario a medio construir dejaría al <c>ShowDialog</c> devolviendo un
    ''' resultado que nadie decidió.</summary>
    Friend ReadOnly Property TomaRechazada As Boolean
        Get
            Return _tomaRechazada
        End Get
    End Property
    Private _tomaRechazada As Boolean

    Public Sub New(mainForm As MainForm,
                   npcFormID As UInteger,
                   raceFormID As UInteger,
                   isFemale As Boolean,
                   Optional editDraft As HdptDraft = Nothing,
                   Optional initialOverrideFormID As UInteger = 0UI)
        If mainForm Is Nothing Then Throw New ArgumentNullException(NameOf(mainForm))
        InitializeComponent()
        _mainForm = mainForm
        _plugins = mainForm.PluginManagerForEditor
        _res = mainForm.HeadPartsResolution
        If _res Is Nothing Then
            Throw New InvalidOperationException(
                "El editor de head parts necesita la sede de resolución de MainForm: sin ella no vería " &
                "los borradores ni en los pickers ni en el preview.")
        End If
        _npcFormID = npcFormID
        _raceFormID = raceFormID
        _isFemale = isFemale
        _game = If(Config_App.Current IsNot Nothing AndAlso
                   Config_App.Current.Game = Config_App.Game_Enum.Skyrim,
                   Canon.WbGame.Skyrim, Canon.WbGame.Fallout4)
        _esFo4 = (_game = Canon.WbGame.Fallout4)

        _toma = New TomaDeBorrador(Of HdptDraft)(
            buscar:=AddressOf BuscarBorrador,
            registrar:=Sub(d) _mainForm.RegisterHdptDraft(d),
            bajar:=Sub(fid) _mainForm.UnregisterHdptDraft(fid),
            idDe:=Function(d) d.FormID,
            construirBase:=AddressOf ConstruirBaseDeDisco)

        ConfigurarPorJuego()
        SembrarComboDeTipos()
        ArmarColumnas()
        ArmarColumnasDeAltTex()
        CrearPreview()

        If editDraft IsNot Nothing Then
            AdoptarBorrador(editDraft)
        ElseIf initialOverrideFormID <> 0UI Then
            CargarComoOverride(initialOverrideFormID)
        Else
            EmpezarEnBlanco()
        End If

        CablearEventos()
    End Sub

    ''' <summary>Lo que el juego de la sesión NO declara se SACA, no se deshabilita: un control gris
    ''' dice «esto existe y no lo podés tocar», que es falso.
    '''
    ''' <para>⛔⛔ <b>LA LISTA SALE DEL CENSO DEL ESQUEMA, no de lo que me acordaba.</b> Subrecords que
    ''' cada juego declara para <c>HDPT</c>: Fallout 4 <b>17</b>, Skyrim <b>12</b>, y los únicos CINCO
    ''' que son de Fallout 4 y no de Skyrim son <c>CIS1 CIS2 CTDA MODC MODF</c> — o sea las condiciones
    ''' (con sus dos subrecords de comentario), el remapeo de color y las banderas del modelo.
    ''' <b>Skyrim no tiene NINGUNO propio.</b> Y en <c>DATA</c> la diferencia es un bit: la tabla de
    ''' Fallout 4 nombra SEIS banderas y la de Skyrim CINCO — la sexta es <c>Uses Body Texture</c>.</para>
    '''
    ''' <para>⛔⛔ <b>Y <c>MODS</c> NO entra en esa lista: existe en los DOS juegos.</b> Acá se sacaba
    ''' su fila entera en Skyrim, con un comentario que decía que el campo no existía; lo que cambia es
    ''' su FORMA —en Fallout 4 es un FormID de <c>MSWP</c> y en Skyrim un ARREGLO de
    ''' <c>Alternate Textures</c>—. No se perdían datos (el borrador ES el record, así que un arreglo
    ''' que nadie toca sobrevive), pero el usuario de Skyrim no tenía forma de verlo ni de editarlo.
    ''' Por eso cada juego se lleva SU control: Fallout 4 la fila del material swap, Skyrim la grilla.
    ''' Lo encontró el censo, no la lectura del editor.</para></summary>
    Private Sub ConfigurarPorJuego()
        If _esFo4 Then
            ' MODS en Fallout 4 es el FormID del material swap: la grilla de Skyrim no aplica.
            ModelLayout.Controls.Remove(GroupAltTex)
            Return
        End If
        FlagsLayout.Controls.Remove(CheckUsesBodyTexture)
        ModelInnerLayout.Controls.Remove(LabelModc)
        ModelInnerLayout.Controls.Remove(ModcLayout)
        ModelLayout.Controls.Remove(LabelModcHint)
        ' La fila del MSWP se va, la GRILLA se queda: las dos son MODS, en formas distintas.
        ModelInnerLayout.Controls.Remove(LabelMods)
        ModelInnerLayout.Controls.Remove(TextBoxMods)
        ModelInnerLayout.Controls.Remove(ModsButtons)
        ModelLayout.Controls.Remove(GroupModelFlags)
        Tabs.TabPages.Remove(TabConditions)
    End Sub

    ''' <summary>El combo de <c>PNAM</c> con los nombres del enum DEL JUEGO DE LA SESIÓN. Es
    ''' <c>DropDownList</c>: ver el ⛔ de la clase sobre los valores fuera del enum.
    ''' <para>Se siembra ORDENADO POR CLAVE: la tabla es un <c>IReadOnlyDictionary</c> y el orden de
    ''' recorrido de un diccionario no es parte de su contrato, así que sin esto el orden de la lista
    ''' que ve el usuario queda a merced de la implementación.</para></summary>
    Private Sub SembrarComboDeTipos()
        ComboType.Items.Clear()
        _clavesDeTipo.Clear()
        _hayEntradaDeTipoCruda = False
        For Each kv In Canon.CanonInterpretacion.NombresDeTipoDeHeadPart(_game).OrderBy(Function(x) x.Key)
            ComboType.Items.Add($"{kv.Key} — {kv.Value}")
            _clavesDeTipo.Add(CUInt(kv.Key))
        Next
    End Sub

    Private Sub ArmarColumnasDeAltTex()
        ListViewAltTex.Columns.Clear()
        ListViewAltTex.Columns.Add("3D name", 220)
        ListViewAltTex.Columns.Add("New texture (TXST)", 260)
        ListViewAltTex.Columns.Add("3D index", 80)
    End Sub

    ''' <summary>La grilla del <c>MODS</c> de Skyrim. Vacía en Fallout 4, donde el grupo ni existe.</summary>
    Private Sub RefrescarAltTex()
        If _esFo4 Then Return
        ListViewAltTex.Items.Clear()
        Dim sse = TryCast(_draft?.Record, Canon.HdptSSE)
        If sse Is Nothing Then Return
        For Each a In sse.AlternateTextures
            Dim it As New ListViewItem(If(a.AlternateTexture3DName, ""))
            it.SubItems.Add(If(a.AlternateTextureNewTexture = 0UI, "(none)",
                               _mainForm.GetRecordDisplayNameForEditor(a.AlternateTextureNewTexture)))
            it.SubItems.Add(a.AlternateTexture3DIndex.ToString())
            ListViewAltTex.Items.Add(it)
        Next
    End Sub

    Private Sub OnAgregarAltTex(sender As Object, e As EventArgs)
        Dim sse = TryCast(_draft?.Record, Canon.HdptSSE)
        If sse Is Nothing Then Return
        Using dlg As New AlternateTextureEditor_Form(_mainForm, "", 0UI, 0)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim nuevo = sse.AgregarAlternateTextures()
            If nuevo Is Nothing Then Return
            nuevo.AlternateTexture3DName = dlg.Name3D
            nuevo.AlternateTextureNewTexture = dlg.NewTexture
            nuevo.AlternateTexture3DIndex = dlg.Index3D
        End Using
        RefrescarAltTex()
        CommitVivo()
    End Sub

    Private Sub OnEditarAltTex(sender As Object, e As EventArgs)
        Dim sse = TryCast(_draft?.Record, Canon.HdptSSE)
        If sse Is Nothing OrElse ListViewAltTex.SelectedIndices.Count = 0 Then Return
        Dim i = ListViewAltTex.SelectedIndices(0)
        Dim lista = sse.AlternateTextures
        If i < 0 OrElse i >= lista.Count Then Return
        Dim a = lista(i)
        Using dlg As New AlternateTextureEditor_Form(_mainForm, a.AlternateTexture3DName,
                                                    a.AlternateTextureNewTexture, a.AlternateTexture3DIndex)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            a.AlternateTexture3DName = dlg.Name3D
            a.AlternateTextureNewTexture = dlg.NewTexture
            a.AlternateTexture3DIndex = dlg.Index3D
        End Using
        RefrescarAltTex()
        CommitVivo()
    End Sub

    Private Sub OnQuitarAltTex(sender As Object, e As EventArgs)
        Dim sse = TryCast(_draft?.Record, Canon.HdptSSE)
        If sse Is Nothing OrElse ListViewAltTex.SelectedIndices.Count = 0 Then Return
        sse.QuitarAlternateTextures(ListViewAltTex.SelectedIndices(0))
        RefrescarAltTex()
        CommitVivo()
    End Sub

    Private Sub ArmarColumnas()
        ListViewExtras.Columns.Clear()
        ListViewExtras.Columns.Add("Extra part", 300)
        ListViewExtras.Columns.Add("Type", 110)
        ListViewExtras.Columns.Add("Source", 180)
        ListViewParts.Columns.Clear()
        ListViewParts.Columns.Add("Part type", 140)
        ListViewParts.Columns.Add("File (NAM1)", 420)
        ListViewConditions.Columns.Clear()
        ListViewConditions.Columns.Add("#", 36)
        ListViewConditions.Columns.Add("Or", 36)
        ListViewConditions.Columns.Add("Function", 200)
        ListViewConditions.Columns.Add("Param 1", 150)
        ListViewConditions.Columns.Add("Param 2", 110)
        ListViewConditions.Columns.Add("Comparison", 130)
        ListViewConditions.Columns.Add("Run On", 90)
    End Sub

    ''' <summary>El preview tiene DOS modos y el mismo control para los dos:
    ''' <list type="bullet">
    ''' <item><b>Part only</b> — las formas del NIF por la casa compartida
    ''' (<see cref="PreviewDeHeadPart"/>), sin skinning ni morphs ni tintes. Es el mismo preview mínimo
    ''' del selector, y anda SIN NPC de contexto (crear un record no necesita sujeto).</item>
    ''' <item><b>Over the NPC</b> — el NPC entero renderizado con este head part puesto, por
    ''' <see cref="MainForm.RenderInHostAsync"/>, que es el mismo camino del preview principal y del de
    ''' Edit Face.</item>
    ''' </list>
    ''' <para>⛔ Los dos existen porque ninguno reemplaza al otro. «Part only» es el único que puede
    ''' mostrar un head part que el motor NO le pondría a este NPC —y es el caso de trabajo normal
    ''' mientras se le acomoda el género o la lista de razas—; «Over the NPC» es el único que ejercita la
    ''' <b>composición</b>, o sea la ley que decide qué entra al slot, que es donde el descarte silencioso
    ''' de <c>CanonInterpretacion:1007</c> vive. Un preview aislado en verde convivió con dos defectos de
    ''' cableado en la vuelta 1, así que no alcanza como testigo por sí solo.</para></summary>
    Private Sub CrearPreview()
        ' GLControl se crea en code-behind: necesita un contexto OpenGL que el diseñador no puede dar.
        Try
            _preview = New PreviewControl() With {.Dock = DockStyle.Fill}
            PreviewControlPanel.Controls.Add(_preview)
            _preview.BringToFront()
            _preview.ApplyResize(True)
        Catch ex As Exception
            _preview = Nothing
            LabelPreviewHint.Text = $"Preview unavailable: {ex.GetType().Name}"
            RadioOverNpc.Enabled = False
            Return
        End Try
        ' El anfitrión del modo «Over the NPC» sólo tiene sentido con sujeto.
        If _npcFormID <> 0UI Then
            Try
                ' ⛔⛔ LOS TOGGLES SON LOS DE CARA, NO LOS DE ATUENDO. Acá estaba
                ' `BuildOutfitPickerToggles()`, que es la línea base del selector de ATUENDOS, y con
                ' (Include Body) apagado el preview sobre el actor salía VACÍO: ese conjunto no
                ' enciende lo que hace falta para dibujar la cabeza sola. La línea base correcta es la
                ' de Edit Face -`RenderToggles.OnlyFace`, que prende todo y deja el gore afuera-, que
                ' es el formulario que hace exactamente este preview.
                ' ⛔⛔ `AppliedPresets` VA, y es el diccionario REAL de MainForm por referencia — lo mismo
                ' que hacen `EditFace_Form` (:5219) y `EditBody_Form` (:3684). Lo había dejado en Nothing
                ' al sacar la copia, y con `OnlyFaceCollect = True` el preview salía COMPLETAMENTE VACÍO:
                ' sin la lista de presets el host no puede resolver el overlay del NPC, y con el cuerpo
                ' apagado no queda nada más que dibujar. Con el cuerpo prendido se veía igual porque el
                ' cuerpo y el atuendo no salen de ahí — de ahí el síntoma raro de «vacío sin body, bien
                ' con body», que reporté mal cuatro veces.
                '   El head part del editor NO entra por acá: entra por `PreviewExtraHeadPartFormIDs`,
                ' que es la perilla del host y no toca el overlay compartido.
                _host = New NpcRenderHost(_preview) With {
                    .AppliedPresets = _mainForm.AppliedPresetsForEditor,
                    .Toggles = RenderToggles.OnlyFace(False)}
                _mainForm?.HookSkinningToggleRefresh(_preview, _host)
            Catch ex As Exception
                _host = Nothing
                Logger.LogLazy(Function() $"[HDPT-EDITOR] sin anfitrión de render: {ex.GetType().Name}: {ex.Message}")
            End Try
        End If
        _previewDebounce = New Timer() With {.Interval = 250}
        AddHandler _previewDebounce.Tick, AddressOf OnTicDelPreview
    End Sub

    ''' <summary>Habilita o deshabilita «Over the NPC» según si el motor le pondría este head part a este
    ''' NPC. Decisión del usuario (20-sep): <i>«si es compatible se puede mostrar sobre npc, sino queda
    ''' deshabilitado y solo se puede mostrar la parte»</i>.
    ''' <para>⛔ La compatibilidad NO se re-decide acá: es el mismo veredicto que ya pinta el panel de
    ''' validez (los cuatro filtros de <c>HeadPartPicker_Form</c> + <c>IsHdptValidForRace</c>), y llega por
    ''' parámetro desde <see cref="RefrescarValidez"/>. Dos cálculos del mismo «¿le entra?» es exactamente
    ''' lo que pasó con el label y la validez del reporte de compatibilidad, que decían cosas distintas.</para>
    ''' <para>Y si el modo deshabilitado era el elegido, se vuelve a «Part only» — un radio deshabilitado
    ''' y tildado deja el preview mostrando lo que ya no puede refrescar. Misma forma que
    ''' <c>ArmaEditor_Form.UpdatePreviewScopeGating</c>.</para></summary>
    Private Sub GatearModoDePreview(compatible As Boolean)
        Dim volvioAlaPieza As Boolean = False
        Dim hayAnfitrion As Boolean = (_host IsNot Nothing AndAlso _npcFormID <> 0UI)
        Dim puedeSobreElActor As Boolean = hayAnfitrion AndAlso compatible
        _gateandoPreview = True
        Try
            RadioOverNpc.Enabled = puedeSobreElActor
            CheckIncludeBody.Enabled = puedeSobreElActor AndAlso RadioOverNpc.Checked
            If Not puedeSobreElActor AndAlso RadioOverNpc.Checked Then
                RadioPartOnly.Checked = True
                volvioAlaPieza = True
            End If
        Finally
            _gateandoPreview = False
        End Try
        ' ⛔ Y SE REDIBUJA. Con el flag `_gateandoPreview` puesto, el `CheckedChanged` que este cambio
        ' dispara retorna temprano, así que el radio decía «Part only» y el lienzo seguía mostrando el
        ' render del actor: dos estados contradiciéndose en pantalla.
        If volvioAlaPieza Then
            _lastPreviewKey = Nothing
            PedirPreview()
        End If
        ' ⛔ EL RÓTULO NO EXPLICA POR QUÉ. Pedido del usuario: «saca el label explicando que no se muestra
        ' en el npc, simplemente con que se deshabilite es suficiente». El radio deshabilitado ya lo dice,
        ' y el panel de validez de abajo dice el motivo exacto. Un párrafo arriba del preview lo repetía.
        LabelPreviewHint.Text = "Preview"
    End Sub

    Private Sub OnModcPresenteCambiado(sender As Object, e As EventArgs)
        ' ⛔ POR EL REFRESCO, no por la casilla sola: desde [rev-80] la condición del numérico es DOS
        ' cosas — la presencia Y que haya malla —, y había TRES sitios escribiendo `.Enabled`. El que
        ' se olvide de la segunda deja el control habilitado sin malla por la puerta de al lado.
        RefrescarEstadoDelModelo()
        If _cargando Then Return
        OnCampoEditado(sender, e)
    End Sub

    Private Sub OnModoDePreviewCambiado(sender As Object, e As EventArgs)
        If _cargando OrElse _gateandoPreview Then Return
        CheckIncludeBody.Enabled = RadioOverNpc.Enabled AndAlso RadioOverNpc.Checked
        ' El modo cambia LO QUE se dibuja, no un campo: se fuerza el refresco aunque la clave no cambie.
        _lastPreviewKey = Nothing
        PedirPreview()
    End Sub

    Private Sub CablearEventos()
        AddHandler ButtonNewBlank.Click, Sub() EmpezarEnBlanco()
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnNuevoDesdePlantilla
        AddHandler ButtonOverrideExisting.Click, AddressOf OnOverrideExistente
        AddHandler ButtonEditMine.Click, AddressOf OnEditarElMio
        AddHandler TextBoxEdid.TextChanged, AddressOf OnCampoEditado
        AddHandler TextBoxName.TextChanged, AddressOf OnCampoEditado
        AddHandler ComboType.SelectedIndexChanged, AddressOf OnCampoEditado
        AddHandler CheckNonPlayable.CheckedChanged, AddressOf OnCampoEditado
        For Each cb In {CheckPlayable, CheckMale, CheckFemale, CheckIsExtraPart, CheckUseSolidTint, CheckUsesBodyTexture}
            AddHandler cb.CheckedChanged, AddressOf OnCampoEditado
        Next
        AddHandler RadioPartOnly.CheckedChanged, AddressOf OnModoDePreviewCambiado
        AddHandler RadioOverNpc.CheckedChanged, AddressOf OnModoDePreviewCambiado
        AddHandler CheckIncludeBody.CheckedChanged, AddressOf OnModoDePreviewCambiado
        AddHandler TextBoxModl.TextChanged, AddressOf OnMallaEditada
        AddHandler ButtonBrowseModl.Click, AddressOf OnBuscarMalla
        AddHandler NumericModc.ValueChanged, AddressOf OnCampoEditado
        AddHandler CheckModcPresente.CheckedChanged, AddressOf OnModcPresenteCambiado
        AddHandler CheckHasFaceBones.CheckedChanged, AddressOf OnCampoEditado
        AddHandler CheckHas1stPerson.CheckedChanged, AddressOf OnCampoEditado
        AddHandler ButtonPickMods.Click, Sub() ElegirFidEn(TextBoxMods, {"MSWP"}, "Select material swap (MSWP)", conBorradoresMswp:=True)
        AddHandler ButtonNewMods.Click, AddressOf OnNuevoOEditarMswp
        AddHandler ButtonAddAltTex.Click, AddressOf OnAgregarAltTex
        AddHandler ButtonEditAltTex.Click, AddressOf OnEditarAltTex
        AddHandler ButtonRemoveAltTex.Click, AddressOf OnQuitarAltTex
        AddHandler ListViewAltTex.DoubleClick, AddressOf OnEditarAltTex
        AddHandler ButtonPickTnam.Click, Sub() ElegirFidEn(TextBoxTnam, {"TXST"}, "Select texture set (TXST)", conBorradoresTxst:=True)
        AddHandler ButtonNewTnam.Click, AddressOf OnNuevoOEditarTxst
        AddHandler ButtonPickCnam.Click, Sub() ElegirFidEn(TextBoxCnam, {"CLFM"}, "Select colour (CLFM)")
        AddHandler ButtonPickRnam.Click, Sub() ElegirFidEn(TextBoxRnam, {"FLST"}, "Select valid-races list (FLST)", conBorradoresFlst:=True)
        AddHandler ButtonNewRnam.Click, AddressOf OnNuevoOEditarFlst
        AddHandler ButtonFitToNpc.Click, AddressOf OnAjustarParaEsteNpc
        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler ButtonCancel.Click, AddressOf OnCancel
        AddHandler ButtonAddExtra.Click, AddressOf OnAgregarExtra
        AddHandler ButtonNewExtra.Click, AddressOf OnNuevoExtra
        AddHandler ButtonEditExtra.Click, AddressOf OnEditarExtra
        AddHandler ListViewExtras.DoubleClick, AddressOf OnEditarExtra
        AddHandler ButtonRemoveExtra.Click, AddressOf OnQuitarExtra
        AddHandler ButtonExtraUp.Click, Sub() MoverExtra(-1)
        AddHandler ButtonExtraDown.Click, Sub() MoverExtra(1)
        AddHandler ButtonAddPart.Click, AddressOf OnAgregarParte
        AddHandler ButtonEditPart.Click, AddressOf OnEditarParte
        AddHandler ListViewParts.DoubleClick, AddressOf OnEditarParte
        AddHandler ButtonRemovePart.Click, AddressOf OnQuitarParte
        AddHandler ButtonAddCondition.Click, AddressOf OnAgregarCondicion
        AddHandler ButtonEditCondition.Click, AddressOf OnEditarCondicion
        AddHandler ListViewConditions.DoubleClick, AddressOf OnEditarCondicion
        AddHandler ButtonRemoveCondition.Click, AddressOf OnQuitarCondicion
        AddHandler ButtonConditionUp.Click, Sub() MoverCondicion(-1)
        AddHandler ButtonConditionDown.Click, Sub() MoverCondicion(1)
    End Sub

    '==============================================================================================
    ' Las cuatro puertas del objetivo
    '==============================================================================================

    Private Function BuscarBorrador(fid As UInteger) As HdptDraft
        For Each d In _mainForm.HdptDrafts()
            If d IsNot Nothing AndAlso d.FormID = fid Then Return d
        Next
        Return Nothing
    End Function

    ''' <summary>La línea de base PRISTINA de un FormID: el record tal como está en el archivo, por la
    ''' MISMA fábrica que arma el borrador override. Con ella la suciedad se decide contra el ARCHIVO y
    ''' no contra el estado de apertura.</summary>
    ''' <summary>EL BORRADOR OVERRIDE PRISTINO de un HDPT real: el record del archivo, copiado, con
    ''' el EditorID sintetizado si no traía. Es LA FÁBRICA, y por eso la usan los DOS lados — el que
    ''' arma lo que el usuario edita Y <see cref="TomaDeBorrador(Of TD)"/> para la LÍNEA DE BASE.
    ''' <para>⛔⛔ TIENE QUE SER LA MISMA FUNCIÓN PARA LOS DOS, y acá no lo era: los dos lados
    ''' llamaban a `Edicion` pelado, y `CommitVivo` estampa un EditorID por defecto cuando el campo
    ''' está vacío ⇒ el borrador lo traía sintetizado y la base no, así que el editor marcaba SUCIO un
    ''' record que el usuario NO TOCÓ, desde el primer volcado. Es la misma ley que
    ''' `ArmoEditor_Form.OverridePristino` documenta con el caso medido; este editor se escribió sin
    ''' transcribirla.</para></summary>
    Friend Shared Function OverridePristino(fid As UInteger, plugins As PluginManager) As HdptDraft
        If plugins Is Nothing OrElse fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Dim rec = plugins.GetRecord(fid)
        If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Return Nothing
        Dim d = HdptDraft.Edicion(rec, plugins)
        If d Is Nothing OrElse d.Record Is Nothing Then Return Nothing
        ' El MISMO EditorID que estampa `CommitVivo`, o la base y el borrador difieren por él.
        If String.IsNullOrEmpty(d.Record.EditorID) Then
            d.Record.EditorID = HdptDraft.EditorIdPrefix & $"{fid And &HFFFUI:X3}"
        End If
        d.IsModified = False
        Return d
    End Function

    Private Function ConstruirBaseDeDisco(fid As UInteger) As HdptDraft
        Return OverridePristino(fid, _plugins)
    End Function

    ''' <summary>Un HDPT propio en blanco.
    ''' <para>⛔⛔ NO SE REGISTRA HASTA QUE EL USUARIO ESCRIBA ALGO. El editor llama a esto al abrir sin
    ''' objetivo, y registrar ahí mismo un record vacío tenía tres consecuencias, las tres reportadas o
    ''' medibles: (1) aparecía en la lista de «Edit mine…» antes de que el usuario eligiera nada, que es
    ''' la duplicación que se veía; (2) aparecía como candidato en el selector de head parts; y (3) —la
    ''' peor— un borrador nuevo nace <c>IsNew</c> ⇒ <c>IsDirty</c>, así que el guardado lo EMITÍA: abrir
    ''' el editor y cerrarlo dejaba un HDPT basura en el .esp.</para>
    ''' <para>El primer <c>OnCampoEditado</c> lo commitea y ahí queda registrado, que es cuando el
    ''' usuario de verdad empezó a hacer uno. La toma se hace igual, así que el abandono y la reversión
    ''' siguen funcionando.</para></summary>
    Private Sub EmpezarEnBlanco()
        Dim fid = _mainForm.AllocateDraftFormID()
        Dim d = HdptDraft.Nuevo(fid, _game)
        If d Is Nothing Then
            MessageBox.Show(Me, $"This game's format does not declare HDPT.", "Head Part Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        _editandoBorradorExistente = False
        TomarYVolcar(d, registrar:=False)
    End Sub

    Private Sub OnNuevoDesdePlantilla(sender As Object, e As EventArgs)
        Dim fid = ElegirHdptReal("Pick the HDPT to copy")
        If fid = 0UI Then Return
        Dim rec = _plugins.GetRecord(fid)
        If rec Is Nothing Then Return
        Dim nuevoFid = _mainForm.AllocateDraftFormID()
        ' ⛔ Si el FormID elegido YA es un borrador propio, la copia sale del BORRADOR y no del disco:
        ' «copiar» tiene que copiar lo que el usuario VE. Misma ley que `ArmoDraft.ClonDeBorrador`.
        Dim yaBorrador = BuscarBorrador(fid)
        Dim d = If(yaBorrador IsNot Nothing,
                   HdptDraft.ClonDeBorrador(yaBorrador, nuevoFid),
                   HdptDraft.Clon(rec, _plugins, nuevoFid))
        If d Is Nothing Then Return
        TomarYVolcar(d)
    End Sub

    Private Sub OnOverrideExistente(sender As Object, e As EventArgs)
        Dim fid = ElegirHdptReal("Pick the HDPT to override")
        If fid = 0UI Then Return
        CargarComoOverride(fid)
    End Sub

    Private Sub CargarComoOverride(fid As UInteger)
        ' Para un FormID que YA tiene borrador, el borrador MANDA sobre el disco.
        Dim ya = BuscarBorrador(fid)
        If ya IsNot Nothing Then
            AdoptarBorrador(ya)
            Return
        End If
        ' ⛔ POR LA FÁBRICA, no por `Edicion` pelado: la base de la suciedad sale de la MISMA, y con
        ' dos construcciones distintas el record abría SUCIO sin que el usuario lo tocara.
        Dim d = OverridePristino(fid, _plugins)
        If d Is Nothing Then Return
        TomarYVolcar(d)
    End Sub

    ''' <summary>«Edit mine…»: los head parts PROPIOS del usuario — los borradores de esta sesión
    ''' <b>y los que ya están GUARDADOS en su .esp</b>.
    '''
    ''' <para>⛔⛔ LA SEGUNDA MITAD FALTABA, y el usuario vio las dos respuestas contradiciendose:
    ''' «Edit mine…» decía «no hay ninguno» sobre un .esp que YA tenía head parts suyos, y al crear uno
    ''' con ese mismo nombre el editor lo rechazaba por EditorID repetido. Eran DOS PREGUNTAS
    ''' DISTINTAS sobre el mismo hecho: esta lista miraba sólo `HdptDrafts()` — la sesión — y
    ''' `IsRecordEditorIdAvailable` mira también los records del plugin. Reabrir la app vacía la
    ''' sesión; los records siguen en el archivo.</para>
    '''
    ''' <para>⛔ El gemelo ya lo hacía bien y el código estaba a la vista:
    ''' <c>ArmaEditor_Form.OnActionEditDraft</c> junta los borradores con
    ''' <c>GetAuthoredRecords("ARMA")</c> — los records de un plugin del NPC Manager, menos los que el
    ''' usuario marcó para borrar — y los muestra como <c>(saved)</c>. Este editor se escribió sin esa
    ''' mitad.</para>
    '''
    ''' <para>Un guardado se abre como OVERRIDE por <see cref="CargarComoOverride"/>, que ya trata el
    ''' caso «ya hay borrador bajo ese FormID» — el borrador manda sobre el disco —, así que elegir el
    ''' mismo record dos veces no crea un segundo borrador.</para></summary>
    Private Sub OnEditarElMio(sender As Object, e As EventArgs)
        Dim entradas As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.HdptDrafts()
            If d?.Record Is Nothing Then Continue For
            entradas.Add(New FormIdPickerEntry With {
                .FormID = d.FormID,
                .EditorID = d.Record.EditorID,
                .DisplayName = If(String.IsNullOrEmpty(d.Record.Name), d.Record.EditorID, d.Record.Name),
                .Signature = "HDPT",
                .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        ' ⛔ Y LOS YA GUARDADOS. Sin dedup contra los borradores: un record guardado que el usuario
        ' está editando en esta sesión aparece UNA vez, como borrador, que es el estado más nuevo.
        Dim yaEnLista As New HashSet(Of UInteger)(entradas.Select(Function(x) x.FormID))
        For Each r In _mainForm.GetAuthoredRecords("HDPT")
            If yaEnLista.Contains(r.FormID) Then Continue For
            entradas.Add(New FormIdPickerEntry With {
                .FormID = r.FormID,
                .EditorID = r.EditorID,
                .DisplayName = r.DisplayName,
                .Signature = "HDPT",
                .PluginName = "(saved)"})
        Next
        If entradas.Count = 0 Then
            MessageBox.Show(Me,
                            "No head parts of yours yet — neither drafts in this session nor saved ones " &
                            "in your plugin. Use New, New from template… or Override existing… first.",
                            "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As New FormIdPicker_Form(_plugins, {"HDPT"}, "Edit my head part (drafts + saved)",
                                           0UI, False, entradas,
                                           Function(fid) entradas.Any(Function(x) x.FormID = fid),
                                           AddressOf OnBorrarEntradaDeBorrador)
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            ' ⛔⛔ UN BORRADOR ABIERTO EN OTRO EDITOR NO SE ABRE DOS VECES. Este editor es RECURSIVO — el
            ' extra de un head part es un head part — y el borrador ES el record, compartido por REFERENCIA:
            ' con dos editores sobre el mismo objeto, el anidado muta el árbol que el padre muestra, su
            ' Cancel queda en un no-op mudo (el padre lo re-registra con la próxima tecla) y su OK se pierde
            ' (el `CommitVivo` del padre vuelca su formulario viejo encima). Las dos direcciones destruyen
            ' trabajo sin aviso. La marca la lleva `TomaDeBorrador`; acá sólo se pregunta.
            ' ⛔ La marca la pone también ESTE editor con su propia toma, así que el objetivo actual
            ' está «tomado» por nosotros: ese caso se distingue, o el aviso mentiría.
            If _draft IsNot Nothing AndAlso _draft.FormID = dlg.SelectedFormID Then
                MessageBox.Show(Me, "That is the head part you are already editing in this window.",
                                "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            If Borradores.EstaTomado(dlg.SelectedFormID) Then
                MessageBox.Show(Me, "That head part is already open in another editor window. Close it first.",
                                "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            ' ⛔ Por `CargarComoOverride` y NO por `BuscarBorrador`: un GUARDADO no tiene borrador
            ' todavía, y con el `If d IsNot Nothing` de antes el botón no hacía NADA para esas filas.
            ' Esa función ya resuelve las dos: con borrador lo adopta, sin borrador lo abre como
            ' override del record del archivo.
            CargarComoOverride(dlg.SelectedFormID)
        End Using
    End Sub

    Private Sub AdoptarBorrador(d As HdptDraft)
        _editandoBorradorExistente = True
        TomarYVolcar(d)
    End Sub

    ''' <summary>Toma un borrador como objetivo del editor.
    ''' <para>⛔⛔ ABANDONA LA TOMA ANTERIOR PRIMERO. Sin esto, cambiar de objetivo dejaba el
    ''' borrador anterior REGISTRADO: el editor crea uno en blanco al abrir sin objetivo, así que cada
    ''' vuelta por «New from template…» / «Override existing…» / «Edit mine…» agregaba un borrador
    ''' vacío más, y «Edit mine…» los iba acumulando. Lo reportó el usuario con dos en la lista, uno
    ''' con EditorID y uno vacío. Era el ÚNICO editor de la casa sin este paso.</para></summary>
    ''' <param name="registrar">False para un NUEVO EN BLANCO: se toma y se vuelca, pero NO se
    ''' commitea, así que NO queda registrado hasta que el usuario escriba algo. Ver el ⛔ de
    ''' <see cref="EmpezarEnBlanco"/>.</param>
    Private Sub TomarYVolcar(d As HdptDraft, Optional registrar As Boolean = True)
        ' ⛔ LA MISMA LEY QUE ARMO: `_toma.Abandonar()` aplica `Borradores.QueHacerAlAbandonar` —da de
        ' baja lo nuevo, restaura lo que había bajo un override, y NO TOCA lo que ya estaba registrado
        ' por otro—. `ArmoEditor_Form` la llama en sus tres cambios de objetivo.
        '
        ' ⛔⛔ PERO ANTES SE PREGUNTA SI ALGUIEN LO REFERENCIA, y esto lo agregué después de romperlo:
        ' la rama «dar de baja» de esa ley saca el borrador del registro SIN mirar quién le apunta, y un
        ' HDPT puede estar colgado del `HNAM` de OTRO head part. Secuencia real, reportada por el
        ' usuario: creó un head part, lo agregó como extra de un override, cambió de objetivo en el
        ' editor ⇒ el borrador se dio de baja ⇒ el override quedó con una REFERENCIA COLGADA ⇒ el
        ' guardado se negó, con razón, porque el .esp habría salido corrupto.
        '   El camino de `Delete / Revert…` ya hacía esta pregunta (`GetDraftReferrers`, la sede única
        ' que mira las ocho clases) y se niega a borrar lo referenciado; el cambio de objetivo se la
        ' salteaba. Si algo lo apunta, se SUELTA en vez de abandonar: el borrador sobrevive y la
        ' referencia sigue siendo válida. Es el mismo par de gestos que el `Delete/Revert` usa.
        If _draft IsNot Nothing AndAlso Not ReferenceEquals(_draft, d) Then
            Dim loApuntanOtros As Boolean = False
            Try
                loApuntanOtros = _mainForm.GetDraftReferrers(_draft.FormID).Count > 0
            Catch
            End Try
            If loApuntanOtros Then _toma.Soltar() Else _toma.Abandonar()
        End If
        Dim snapNuevo As HdptDraft = Nothing
        Try
            snapNuevo = d.Clone()
        Catch
            ' Sin snapshot la reversión no puede reponer: se marca y el abandono sólo da de baja.
        End Try
        ' ⛔⛔ LA TOMA VA PRIMERO, Y SI SE RECHAZA EL EDITOR NO ABRE. Acá había un `_tomaRechazada =
        ' True` que anotaba y SEGUÍA, y eso era PEOR que no tener la ley: el editor quedaba con
        ' `_draft` apuntando a un borrador cuya toma viva es de OTRO editor, y `CommitVivo()` lo
        ' REGISTRABA — o sea mutaba el objeto compartido y lo reponía en el mapa, que es exactamente el
        ' daño que la ley existe para evitar.
        ' ⛔ Y el comentario que había acá decía «no rompe bytes»: es FALSO. Un borrador registrado y
        ' sucio lo emite la fase 2l —«todo borrador SUCIO se emite, referenciado o no»—, así que
        ' llegaba al `.esp`. Es el mismo razonamiento con el que se cerró el OK sobre un borrador en
        ' blanco: registrar NO es inocuo.
        ' ⛔ Y además `Tomar` sale por su `Return False` ANTES de fijar `_actual`, con lo cual
        ' `Abandonar`/`Soltar` al cerrar quedaban en no-ops: no había limpieza Y el FormID quedaba SIN
        ' MARCA, así que un tercer editor también lo podía abrir.
        If Not _toma.Tomar(d, snapNuevo) Then
            _tomaRechazada = True
            Return
        End If
        _draft = d
        _openSnapshot = snapNuevo
        VolcarAlFormulario()
        If registrar Then CommitVivo()
        ' ⛔⛔ Y SE PIDE EL PREVIEW Y LA VALIDEZ. Esto FALTABA, y es lo que el usuario vio: cambiar de
        ' objetivo por cualquiera de las cuatro puertas —New (blank), New from template…, Override
        ' existing…, Edit mine…— volcaba los campos y commiteaba, pero NADIE pedía un redibujo, así que
        ' el preview quedaba con lo anterior o en blanco. El preview sólo se pedía desde los manejadores
        ' de campo, o sea que recien aparecía cuando el usuario tocaba algo.
        '   La clave se borra primero porque `PedirPreview` no redibuja si la clave no cambió, y el
        ' punto de este llamado es forzar el redibujo del objetivo NUEVO.
        _lastPreviewKey = Nothing
        RefrescarValidez()
        PedirPreview()
    End Sub

    '==============================================================================================
    ' Volcado y commit
    '==============================================================================================

    Private Sub VolcarAlFormulario()
        If _draft?.Record Is Nothing Then Return
        _cargando = True
        Try
            Dim r = _draft.Record
            TextBoxEdid.Text = If(r.EditorID, "")
            TextBoxName.Text = If(r.NamePresente, If(r.Name, ""), "")
            PonerTipoEnElCombo(r.Type)
            CheckNonPlayable.Checked = r.NonPlayable
            CheckPlayable.Checked = r.FlagsPlayable
            CheckMale.Checked = r.FlagsMale
            CheckFemale.Checked = r.FlagsFemale
            CheckIsExtraPart.Checked = r.FlagsIsExtraPart
            CheckUseSolidTint.Checked = r.FlagsUseSolidTint
            TextBoxModl.Text = If(r.ModelFileNamePresente, If(r.ModelFileName, ""), "")
            PonerFidEn(TextBoxTnam, r.TextureSet)
            PonerFidEn(TextBoxCnam, r.Color)
            PonerFidEn(TextBoxRnam, r.ValidRaces)
            Dim fo4 = TryCast(r, Canon.HdptFO4)
            If fo4 IsNot Nothing Then
                CheckUsesBodyTexture.Checked = fo4.FlagsUsesBodyTexture
                ' Presencia y valor son DOS datos: el check lleva la presencia, el numerico el valor.
                CheckModcPresente.Checked = fo4.ModelColorRemappingIndexPresente
                ' El estado del numérico lo pone `RefrescarEstadoDelModelo`, que es la única sede: la
                ' condición es presencia Y malla, no sólo presencia.
                If fo4.ModelColorRemappingIndexPresente Then
                    Dim vModc = CDec(fo4.ModelColorRemappingIndex)
                    ' Un valor del archivo puede caer fuera del rango que el corpus mide: se PINZA para
                    ' el control, y el del record queda intacto hasta que el usuario lo toque.
                    If vModc < NumericModc.Minimum Then vModc = NumericModc.Minimum
                    If vModc > NumericModc.Maximum Then vModc = NumericModc.Maximum
                    NumericModc.Value = vModc
                End If
                CheckHasFaceBones.Checked = fo4.ModelFlagsHasFaceBonesModel
                CheckHas1stPerson.Checked = fo4.ModelFlagsHas1stPersonModel
                PonerFidEn(TextBoxMods, fo4.ModelMaterialSwap)
            End If
            RefrescarExtras()
            RefrescarAltTex()
            RefrescarPartes()
            RefrescarCondiciones()
            RefrescarRazasDeLaFlst()
            RefrescarEstadoDelModelo()
            ActualizarBanner()
        Finally
            _cargando = False
        End Try
        RefrescarValidez()
        PedirPreview()
    End Sub

    ''' <summary>Vuelca el formulario al árbol del borrador y lo REGISTRA. Corre en cada gesto: el
    ''' borrador ES el record, así que no hay un «guardar» aparte — lo que hay es OK (aceptar) y Cancel
    ''' (revertir).</summary>
    Private Function CommitVivo() As Boolean
        If _draft?.Record Is Nothing OrElse _cargando Then Return False
        Dim r = _draft.Record
        Try
            ' ⛔⛔ UN BORRADOR NUNCA QUEDA REGISTRADO CON EditorID VACÍO, y esto NO es cosmético: el mapa
            ' de shapes permitidos del horneado indexa por EditorID y SALTEA la entrada vacía
            ' (`FaceGenBuilder.BuildAllowedShapeMap`: `If String.IsNullOrEmpty(entry.Hdpt.EditorID) Then
            ' Continue For`). O sea que un head part propio sin EDID SE DIBUJA EN EL RENDER Y NO ENTRA AL
            ' HORNEADO: render != bake, en silencio, y el usuario lo descubre en el juego.
            ' El EDID por defecto se estampaba sólo al ACEPTAR, y el commit vivo REGISTRA antes de eso.
            Dim edidEscrito = TextBoxEdid.Text.Trim()
            If edidEscrito.Length = 0 Then edidEscrito = HdptDraft.EditorIdPrefix & $"{_draft.FormID And &HFFFUI:X3}"
            r.EditorID = edidEscrito
            ' ⛔ EL NOMBRE SE PUEDE VACIAR. Acá el caso «vacío y estaba» escribía `«»`, o sea que
            ' CONSERVABA el subrecord vacío: borrar el nombre no lo borraba. El argumento viejo era
            ' que un `FULL` vacío puede ser deliberado — cierto, pero entonces el usuario no tenía
            ' forma de SACARLO, que es peor. Un `FULL` presente y vacío pide su propio control de
            ' presencia (como el del `MODC`), no una asignación que no se puede deshacer.
            Borradores.EscribirCampoOQuitar(TextBoxName.Text.Length > 0,
                                            Sub() r.Name = TextBoxName.Text,
                                            Sub() r.NamePresente = False)
            r.NonPlayable = CheckNonPlayable.Checked
            Dim tipo As UInteger = 0UI
            If TipoDelCombo(tipo) Then r.Type = tipo
            r.FlagsPlayable = CheckPlayable.Checked
            r.FlagsMale = CheckMale.Checked
            r.FlagsFemale = CheckFemale.Checked
            r.FlagsIsExtraPart = CheckIsExtraPart.Checked
            r.FlagsUseSolidTint = CheckUseSolidTint.Checked
            ' ⛔⛔ VACÍO NO ES CERO, Y ESCRIBIR CERO CREA EL SUBRECORD. Acá estaba
            '     r.TextureSet = FidDe(TextBoxTnam)
            ' sin condición, y `FidDe` devuelve 0 cuando el campo está vacío. Resultado, visto por el
            ' usuario en xEdit sobre un override de `FemaleHeadHuman` de Fallout4.esm: el record escrito
            ' tenía `TNAM - Null Reference [00000000]` y `CNAM - Null Reference [00000000]` y el ORIGINAL
            ' no trae ninguno de los dos. O sea que el editor le AGREGA subrecords al record de todos los
            ' NPC que usan ese head part, con una referencia nula.
            '   La ley: si el campo está vacío y el subrecord NO estaba, no se crea; si estaba y el
            ' usuario lo vació, se QUITA. Cero con el subrecord presente sigue siendo un valor legal y
            ' distinto de ausente — la misma ley que el `MODC` de más abajo.
            EscribirFidOQuitar(TextBoxTnam, Sub(v) r.TextureSet = v,
                               Sub() r.TextureSetPresente = False)
            EscribirFidOQuitar(TextBoxCnam, Sub(v) r.Color = v,
                               Sub() r.ColorPresente = False)
            EscribirFidOQuitar(TextBoxRnam, Sub(v) r.ValidRaces = v,
                               Sub() r.ValidRacesPresente = False)
            ' ⛔⛔ EL MODL Y EL MODT VIAJAN JUNTOS, Y ESTO ESTÁ MEDIDO, no argumentado. Acá decía
            ' «el MODL sigue por asignación directa», así que la caja vacía escribía un `MODL`
            ' presente y vacío. Censo de los dos corpus, por record:
            '     ⛔ EL MÉTODO, que es lo que faltaba declarar: un record por **FormID GANADOR** del
            '     orden de carga. Contar apariciones mezcla el mismo head part una vez por plugin y da
            '     denominadores distintos según cuántos overrides haya — que es como estas cifras
            '     llegaron a contradecirse entre sí. Corrida ÚNICA: `Tools\censo_hdpt.py`.
            '     Fallout 4 — 2.546 HDPT: `MODL` ausente 1 · presente y VACÍO **0** · con malla 2.545
            '     Skyrim SE — 4.943 HDPT: `MODL` ausente 27 · presente y VACÍO **0** · con malla 4.916
            '     `MODT` con `MODL` ausente: **0** en los dos. `MODT` con `MODL` vacío: **0** en los dos.
            ' O sea que las tres opciones son: dejarlo como estaba escribe una forma que no existe en
            ' ninguno de los 7.489 head parts; sacar sólo el `MODL` deja un `MODT` huérfano, que
            ' tampoco existe; y sacar los DOS es exactamente la forma de los 30 que SÍ existen sin
            ' malla. Se hace lo tercero.
            '   El `MODT` no se recalcula nunca en este editor — se conserva el de la malla anterior,
            ' que es lo que dice su propia etiqueta —, así que sin malla no hay hash que conservar.
            '   ⛔ Y QUE EL CONTENEDOR `Model` VACÍO NO EMITE NADA ESTÁ MEDIDO, no leído del escritor:
            ' `HdptTxstFlstRoundtripProbe --sinmodelo` reporta la sub-población sin `MODL` ni `MODT` — 1
            ' de 396 en Fallout 4 (`001F9AD7 BoyHairNone`) y 15 de 766 en Skyrim (`HumanBeard00NoBeard`,
            ' `HairKhajiit00`, `MarksMaleHumanoid00NoScar`…) — y los tres RESULT de HDPT dan byte-exacto,
            ' o sea que esos records se re-emiten idénticos con el `RStruct` vacío. Si emitiera un
            ' contenedor huérfano, esos 16 saldrían como mismatch.
            ' ⛔⛔ RONDA 3 [rev-80]: SON LOS CINCO MIEMBROS DEL BLOQUE `Model`, NO DOS. El
            ' `RStruct(«Model», …)` declara `MODL MODT MODC MODS MODF`, y sacar sólo los dos primeros
            ' dejaba `MODC`, `MODS` y `MODF` describiendo un modelo que ya no se nombra — exactamente la
            ' objeción del `MODT`, un nivel más arriba. Censo de los dos corpus sobre los HDPT con
            ' `MODL` AUSENTE (1 en Fallout 4, 27 en Skyrim): de ésos, **0 traen `MODT`, 0 `MODC`,
            ' 0 `MODS` y 0 `MODF`**, en los DOS juegos. Y `MODC`/`MODS`/`MODF` aparecen en **0 de los
            ' 2.546** HDPT de Fallout 4 en total. O sea que «sin malla» es «sin bloque Model», medido.
            Dim modl = TextBoxModl.Text.Trim()
            Dim hayMalla As Boolean = (modl.Length > 0)
            Borradores.EscribirCampoOQuitar(hayMalla,
                                            Sub() r.ModelFileName = modl,
                                            Sub() r.ModelFileNamePresente = False)
            If Not hayMalla Then r.ModelInformationERRORPresente = False
            Dim fo4 = TryCast(r, Canon.HdptFO4)
            If fo4 IsNot Nothing Then
                fo4.FlagsUsesBodyTexture = CheckUsesBodyTexture.Checked
                ' ⛔ SIN MALLA NO HAY BLOQUE `Model`: los tres que siguen se QUITAN y no se escriben
                ' desde los controles, o el commit los volvería a crear sobre un modelo inexistente. Ver el
                ' ⛔⛔ del `MODL` con el censo. Los controles quedan deshabilitados por
                ' `RefrescarEstadoDelModelo`, así que lo que el usuario ve coincide con lo que se escribe.
                If Not hayMalla Then
                    fo4.ModelColorRemappingIndexPresente = False
                    fo4.ModelMaterialSwapPresente = False
                    fo4.ModelFlagsPresente = False
                Else
                    ' ⛔ AUSENTE NO ES CERO. Destildado el subrecord se QUITA; tildado se escribe el
                    ' valor del numérico. Cero es un valor legítimo y distinto de «no está».
                    ' ⛔ Y EL QUITADO ERA UN NO-OP MUDO: acá estaba `QuitarSubrecord(fo4, «MODC»)`, que
                    ' recorre sólo los hijos DIRECTOS de la raíz, y el `MODC` cuelga del
                    ' `RStruct(«Model»)`. Destildar la presencia no borraba nada.
                    Borradores.EscribirCampoOQuitar(
                        CheckModcPresente.Checked,
                        Sub() fo4.ModelColorRemappingIndex = CSng(NumericModc.Value),
                        Sub() fo4.ModelColorRemappingIndexPresente = False)
                    fo4.ModelFlagsHasFaceBonesModel = CheckHasFaceBones.Checked
                    fo4.ModelFlagsHas1stPersonModel = CheckHas1stPerson.Checked
                    ' ⛔ MISMA LEY QUE EL TNAM/CNAM, y estaba a 20 líneas del arreglo de esos dos: con
                    ' la caja vacía esto escribía 0 y CREABA `MODS - Null Reference [00000000]`. El
                    ' usuario lo vio en xEdit después del arreglo de TNAM/CNAM — «MODS (Material SWAP)
                    ' tambien quedo en null» — y la revisión lo había levantado en paralelo.
                    EscribirFidOQuitar(TextBoxMods, Sub(v) fo4.ModelMaterialSwap = v,
                                       Sub() fo4.ModelMaterialSwapPresente = False)
                End If
            End If
        Catch ex As Exception
            ' Con Try propio, igual que `ArmoEditor_Form.CommitProtegido` y por lo mismo: esto corre desde
            ' manejadores sin Try y la app usa ThrowException, así que un throw acá CIERRA la app.
            Logger.LogLazy(Function() $"[HDPT-EDITOR] commit falló: {ex.GetType().Name}: {ex.Message}")
            Return False
        End Try
        ' La suciedad se decide contra la BASE del archivo, no contra el estado de apertura.
        Dim igualALaBase As Boolean = False
        If _toma.Base IsNot Nothing Then igualALaBase = _draft.ContentEquals(_toma.Base)
        _draft.IsModified = _toma.Sucio(igualALaBase)
        _mainForm.RegisterHdptDraft(_draft)
        _huboCambios = True
        _resultHdptFormID = _draft.FormID
        ActualizarBanner()
        Return True
    End Function

    ''' <summary>Cualquier campo de identidad o de banderas. ⛔ PIDE EL PREVIEW: el tipo, el género y
    ''' el RNAM cambian LO QUE SE DIBUJA en el modo «sobre el actor» — deciden si el motor lo pondría y
    ''' qué slot toma —, y acá sólo se commiteaba y se refrescaba la validez.</summary>
    Private Sub OnCampoEditado(sender As Object, e As EventArgs)
        If _cargando Then Return
        CommitVivo()
        RefrescarValidez()
        PedirPreview()
    End Sub

    Private Sub OnMallaEditada(sender As Object, e As EventArgs)
        If _cargando Then Return
        CommitVivo()
        RefrescarEstadoDelModelo()
        PedirPreview()
    End Sub

    '==============================================================================================
    ' MODT — el bloque de model info
    '==============================================================================================

    ''' <summary>Dice en qué estado quedó el <c>MODT</c> respecto del mesh. NO lo recalcula: el hash de
    ''' Bethesda no está transcrito del binario y esta app no inventa valores. Lo que sí hace es AVISAR,
    ''' porque un MODT del mesh anterior es un dato viejo escondido.
    ''' <para>Medido sobre el corpus: la forma más común del <c>MODT</c> de un HDPT es el bloque VACÍO
    ''' —1.674 de los 2.544 que traen MODT en Fallout 4, y CERO en Skyrim—, así
    ''' que «vacío» no es un caso degenerado: es la mayoría.</para></summary>
    ''' <summary>⛔ Y GATEA LOS CONTROLES DEL BLOQUE <c>Model</c>. Sin malla el bloque entero no
    ''' existe — medido: de los 30 HDPT del corpus con <c>MODL</c> ausente, <b>0</b> traen
    ''' <c>MODT</c>, <c>MODC</c>, <c>MODS</c> o <c>MODF</c>, en los dos juegos — y el commit los quita.
    ''' Si los controles siguieran habilitados, el usuario tildaría un <c>MODC</c> que el commit
    ''' descarta: la pantalla diría una cosa y el record tendría otra. Deshabilitarlos es lo que hace
    ''' que las dos coincidan, sin un cartel que haya que leer.</summary>
    Private Sub RefrescarEstadoDelModelo()
        If _draft?.Record Is Nothing Then Return
        Dim r = _draft.Record
        Dim hayMalla As Boolean = (TextBoxModl.Text.Trim().Length > 0)
        CheckModcPresente.Enabled = hayMalla
        NumericModc.Enabled = hayMalla AndAlso CheckModcPresente.Checked
        TextBoxMods.Enabled = hayMalla
        ButtonPickMods.Enabled = hayMalla
        ButtonNewMods.Enabled = hayMalla
        CheckHasFaceBones.Enabled = hayMalla
        CheckHas1stPerson.Enabled = hayMalla
        Dim tieneBloque As Boolean = (r.Counters IsNot Nothing AndAlso r.Counters.Count > 0) OrElse
                                     (r.Textures IsNot Nothing AndAlso r.Textures.Count > 0) OrElse
                                     r.ModelInformationERRORPresente
        Dim conTexturas As Boolean = (r.Textures IsNot Nothing AndAlso r.Textures.Count > 0)
        ' ⛔ EL RÓTULO DICE LO QUE EL USUARIO NECESITA, NO EL PORQUÉ. La justificación —la medición del
        ' corpus y que el hash no está transcrito del binario— vive en los comentarios del código, que es
        ' donde sirve. En pantalla un párrafo sólo ocupa lugar: el usuario lo que necesita saber es si el
        ' campo está y si quedó viejo al cambiar la malla.
        ' ⛔⛔ MEDICIÓN CORREGIDA DOS VECES, y la segunda corrección también era mía. El rótulo
        ' decía primero «none — same shape as 1.676 of 2.991 FO4 head parts», que era falso: esos no
        ' son los que NO tienen MODT sino los que lo tienen de 20 bytes con CERO entradas. Lo corregí
        ' a «el 100 % de los head parts con malla trae MODT» — y eso también es falso para Fallout 4.
        '   Corrida ÚNICA, por FormID ganador (`Tools\censo_hdpt.py`):
        '     Fallout 4 — con malla **2.545**, con MODT **2.544** ⇒ hay UNO con malla y sin MODT.
        '     Skyrim SE — con malla **4.916**, con MODT **4.916** ⇒ ahí sí es el 100 %.
        ' Y de los 2.544 de Fallout 4, **1.674** son de 20 bytes con cero entradas de textura.
        ' O sea que «sin MODT» es una forma que existe, pero es UNA en 2.545: el caso normal de un
        ' record propio al que todavía no le pusieron malla, no una forma del juego.
        If Not tieneBloque Then
            LabelModtStatus.Text = "MODT: none (no vanilla head part with a mesh is like this)."
        ElseIf conTexturas Then
            LabelModtStatus.Text = $"MODT: {r.Textures.Count} texture entries — kept from the previous mesh, not recomputed."
        Else
            LabelModtStatus.Text = "MODT: present, empty."
        End If
    End Sub

    '==============================================================================================
    ' Validez para el NPC de contexto
    '==============================================================================================

    ''' <summary>Los CUATRO filtros que el selector de head parts ya aplica, mostrados como resultado en
    ''' vivo. ⛔ No son inventados: son los de <c>HeadPartPicker_Form</c> y
    ''' <c>HeadPartResolver.IsHdptValidForRace</c>, y existe porque hoy son SILENCIOSOS — un head part
    ''' con el bit de género equivocado simplemente no aparece en la lista y no hay forma de saber por qué.
    ''' <para>⚠️ Y dice la verdad sobre cuál falla: un record NUEVO en blanco falla UNO SOLO, el tipo.
    ''' Sin bits de género PASA («an HDPT with NEITHER bit set is treated as gender-neutral … the user
    ''' sees it regardless», <c>HeadPartPicker_Form</c>) y con <c>RNAM = 0</c> PASA también («no race
    ''' restriction declared», <c>HeadPartResolver.IsHdptValidForRace</c>).</para></summary>
    Private Sub RefrescarValidez()
        If _draft?.Record Is Nothing Then Return
        If _npcFormID = 0UI OrElse _raceFormID = 0UI Then
            LabelValidType.Text = ""
            LabelValidGender.Text = ""
            LabelValidNotExtra.Text = ""
            LabelValidRace.Text = ""
            LabelValidVerdict.Text = "No NPC selected: there is no subject to check this head part against."
            ButtonFitToNpc.Enabled = False
            LabelPreviewSubject.Text = ""
            GatearModoDePreview(False)
            Return
        End If
        Dim r = _draft.Record
        Dim tipo = r.TipoDeParte()
        ' ⛔⛔ LOS CUATRO FILTROS SALEN DE LA SEDE, LOS MISMOS QUE APLICA EL SELECTOR. Acá estaban
        ' escritos a mano y DIFERÍAN en dos cosas: el género se calculaba igual, pero el de «es un
        ' extra» se aplicaba SIEMPRE — sin la excepción de Misc, que es justo el balde donde los
        ' extras SÍ se eligen a mano —, y el del TIPO no existía como filtro sino como un texto
        ' aparte. O sea que este panel podía prometer «va a aparecer en el selector» sobre un Misc
        ' marcado como extra, que el selector SÍ acepta, y negarlo sobre otros que también acepta.
        ' Con una sola cuenta, lo que el panel dice y lo que el selector hace no pueden separarse.
        '   Se pide con el TIPO DEL PROPIO RECORD como tipo pedido: la pregunta del panel es «si el
        ' usuario abre el selector DE ESTE TIPO, va a estar?».
        Dim filtros = HeadPartResolver.FiltrarParaPicker(r, _draft.FormID, CInt(tipo),
                                                        _raceFormID, _isFemale, _res)
        Dim generoOk As Boolean = filtros.GeneroOk
        Dim noEsExtra As Boolean = filtros.NoEsExtraOk
        ' ⛔⛔ LA RAZA SE PREGUNTA CON EL RECORD EN LA MANO, no por FormID — y ahora se pregunta una
        ' sola vez, adentro de la sede de los cuatro filtros. Acá estaba
        '     HeadPartResolver.IsHdptValidForRace(_draft.FormID, …, _res)
        ' y para un «New (blank)» el borrador todavía NO está registrado (a propósito: ver el ⛔ de
        ' `EmpezarEnBlanco`), así que la sede devolvía Nothing y la función daba False. El panel
        ' mostraba «RNAM includes race ....... NO» y el veredicto negativo, y `GatearModoDePreview`
        ' deshabilitaba «Over the NPC» — las tres cosas FALSAS, y contradiciendo el doc de este mismo
        ' panel, que dice que un record nuevo en blanco falla UNO SOLO (el tipo) porque con `RNAM = 0`
        ' pasa. Se curaba sola al escribir el primer carácter, que es peor: intermitente.
        '   El editor TIENE la vista del record, así que la pregunta se contesta con ella. Ver la
        ' sobrecarga de `IsHdptValidForRace` que recibe la `IHdpt`. Lo levantó [rev-50].
        Dim razaOk As Boolean = filtros.RazaOk

        LabelValidType.Text = $"Part type {tipo} ({r.TypeNombre}) ....... {If(tipo > 0, "OK", "Misc — it will not fill a slot")}"
        LabelValidGender.Text = $"Gender ({If(_isFemale, "Female", "Male")}) .......... {If(generoOk, "OK", "NO")}"
        LabelValidNotExtra.Text = $"Not an extra ............. {If(noEsExtra, "OK", "it is an extra (HNAM addon)")}"
        LabelValidRace.Text = $"RNAM includes race ....... {If(razaOk, "OK", "NO")}"
        ' ⛔⛔ DOS PREGUNTAS DISTINTAS, y estaban mezcladas en una lista sola.
        '   «El motor se lo pondría a este NPC?»  ⇒ el género y la RNAM, y NADA MÁS.
        '   «Aparece en el selector de ESTE tipo de slot?» ⇒ además hace falta que el tipo sea de slot.
        ' El tipo 0 (Misc) NO es un head part inválido: es ADITIVO —la ley de composición dice, textual,
        ' «Misc y Otros no disputan un slot: se UNEN entre fuentes, deduplicados»— y es donde viven los
        ' aros, las pestañas y el AO. Medido: 233 de tipo 0 en Fallout 4 y 1.769 en Skyrim. Un Misc se
        ' dibuja sobre el NPC perfectamente; lo único que no hace es llenar un slot, y para elegirlo
        ' está el botón +Misc, no el selector de un tipo.
        Dim leEntraAlNpc As Boolean = generoOk AndAlso razaOk
        Dim faltan As New List(Of String)
        If Not generoOk Then faltan.Add("the gender bit")
        If Not razaOk Then faltan.Add("a valid-races list that includes this race")
        If Not leEntraAlNpc Then
            LabelValidVerdict.Text = "The engine would NOT put this on the NPC: missing " &
                                     String.Join(", ", faltan) + "."
        ElseIf tipo = 0 Then
            ' Verdad medida, y lo que el texto anterior negaba: un Misc SÍ aparece — en la lista de Misc.
            LabelValidVerdict.Text = "It is valid for this NPC. Misc parts are additive: they do not " &
                                     "fill a slot, so they appear under «+Misc» and not in a slot's picker."
        ElseIf Not noEsExtra Then
            ' ⛔ EL TERCER FILTRO CUENTA PARA EL SELECTOR. El veredicto prometía «va a aparecer» con el
            ' bit de EXTRA tildado, y el selector filtra los extras para cualquier tipo distinto de 0
            ' (el usuario elige el PADRE y el motor arrastra sus `HNAM`). El panel ya calculaba
            ' `noEsExtra` y lo pintaba en su propia línea; faltaba meterlo en la CONCLUSIÓN.
            LabelValidVerdict.Text = "It is valid for this NPC, but it will NOT show in a slot's picker: " &
                                     "it is flagged as an extra part, and the engine pulls those with their parent."
        Else
            LabelValidVerdict.Text = "It will show in this NPC's picker for that type."
        End If
        ' ⛔ «Fit to this NPC» NO es la compensación de un record NUEVO —ése sólo necesita elegir el
        ' tipo— sino de los modos «New from template…» y «Override existing…», donde el record HEREDA el
        ' género y la RNAM restrictiva de su fuente y ésos sí pueden excluir al NPC de delante.
        ButtonFitToNpc.Enabled = (Not generoOk) OrElse (Not razaOk)
        LabelPreviewSubject.Text = $"NPC: 0x{_npcFormID:X8} · race 0x{_raceFormID:X8} · {If(_isFemale, "Female", "Male")}"
        ' El MISMO veredicto que acaba de pintar el panel gatea el modo «Over the NPC»: una sola cuenta.
        GatearModoDePreview(leEntraAlNpc)
    End Sub

    Private Sub OnAjustarParaEsteNpc(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing OrElse _raceFormID = 0UI Then Return
        If _isFemale Then CheckFemale.Checked = True Else CheckMale.Checked = True
        ' La RNAM: se OFRECEN las listas que ya incluyen a esta raza. No se inventa una — en Fallout 4
        ' son SEIS en todo el juego (medido), así que la respuesta casi siempre es una de ésas.
        Dim candidatas = FlstQueIncluyenLaRaza()
        If candidatas.Count = 0 Then
            MessageBox.Show(Me,
                "No FLST in this load order lists this race, so there is nothing to offer. Leaving RNAM at 0 " &
                "(no restriction) also passes the filter for a race that has head parts.",
                "Fit to this NPC", MessageBoxButtons.OK, MessageBoxIcon.Information)
            CommitVivo() : RefrescarValidez() : Return
        End If
        ' ⛔ LAS BORRADOR VAN COMO ENTRADAS DEL SELECTOR, no sólo en el filtro. El filtro dice «qué
        ' de lo listado se muestra», y el selector lista lo que sale del `PluginManager`: una FLST
        ' borrador no está ahí y no va a estar hasta el guardado, así que sin esto el diálogo salía
        ' vacío aunque la candidata estuviera en el conjunto.
        ' ⛔ CON SU CAMINO DE BAJA. Este selector pasó a ofrecer FLST BORRADOR y quedó con `Nothing`
        ' en `onDeleteEntry`: el doc de `OnBorrarEntradaDeBorrador` dice que ofrecer un borrador sin su
        ' baja lo deja SIN SALIDA — el botón «Delete / Revert…» ni se ve.
        Dim entradasFit = EntradasDeBorradorFlst()
        entradasFit.AddRange(EntradasGuardadas("FLST", entradasFit))
        Using dlg As New FormIdPicker_Form(_plugins, {"FLST"}, "Valid-races lists that include this race",
                                           FidDe(TextBoxRnam), True,
                                           entradasFit,
                                           Function(fid) candidatas.Contains(fid),
                                           AddressOf OnBorrarEntradaDeBorrador)
            If dlg.ShowDialog(Me) = DialogResult.OK Then PonerFidEn(TextBoxRnam, dlg.SelectedFormID)
        End Using
        CommitVivo()
        RefrescarRazasDeLaFlst()
        RefrescarValidez()
    End Sub

    ''' <summary>Las FLST que YA incluyen a esta raza: las del orden de carga <b>y las BORRADOR</b>.
    ''' <para>⛔ Las borrador faltaban, y son justo las que el editor de FLST existe para hacer — su
    ''' propio texto dice que el gesto por defecto es «clonar una de las seis y agregarle mi raza». El
    ''' usuario la hacía, volvía a «Fit to this NPC» y el cartel le decía «No FLST in this load order
    ''' lists this race», que era FALSO: la tenía hecha y en pantalla.</para>
    ''' <para>⛔ Enumerarlas es la MITAD del arreglo; la otra es pasárselas al selector — ver
    ''' <see cref="OnAjustarParaEsteNpc"/>. Sin las dos, el filtro acepta un FormID que el selector no
    ''' lista y el diálogo sale vacío igual.</para></summary>
    Private Function FlstQueIncluyenLaRaza() As HashSet(Of UInteger)
        Dim res As New HashSet(Of UInteger)
        Dim todas = _plugins.GetRecordsOfType("FLST")
        If todas IsNot Nothing Then
            For Each rec In todas
                Dim f = _res.Flst(rec.Header.FormID)
                If f Is Nothing Then Continue For
                If f.Miembros().Contains(_raceFormID) Then res.Add(rec.Header.FormID)
            Next
        End If
        For Each d In _mainForm.FlstDrafts()
            If d?.Record Is Nothing Then Continue For
            If res.Contains(d.FormID) Then Continue For
            If d.Record.Miembros().Contains(_raceFormID) Then res.Add(d.FormID)
        Next
        Return res
    End Function

    Private Sub RefrescarRazasDeLaFlst()
        ListBoxRnamRaces.Items.Clear()
        Dim fid = FidDe(TextBoxRnam)
        If fid = 0UI Then
            ListBoxRnamRaces.Items.Add("(RNAM = 0 — no race restriction)")
            Return
        End If
        Dim f = _res.Flst(fid)
        If f Is Nothing Then
            ListBoxRnamRaces.Items.Add("(that FLST does not resolve in this load order)")
            Return
        End If
        For Each m In f.Miembros()
            ListBoxRnamRaces.Items.Add(_mainForm.GetRecordDisplayNameForEditor(m))
        Next
        If ListBoxRnamRaces.Items.Count = 0 Then ListBoxRnamRaces.Items.Add("(empty list)")
    End Sub

    '==============================================================================================
    ' Preview
    '==============================================================================================

    ''' <summary>Pide un refresco del preview. La clave incluye EL MODO, así que cambiar de «Part only»
    ''' a «Over the NPC» refresca aunque no se haya tocado ningún campo.
    ''' <para>El modo sobre el actor pasa por un temporizador —el render del NPC entero cuesta, y esto
    ''' corre en cada tecla del <c>MODL</c>—; el modo de la pieza sola dibuja en el acto, que es lo que
    ''' venía haciendo.</para></summary>
    Private Sub PedirPreview()
        If _preview Is Nothing OrElse _draft Is Nothing Then Return
        Dim modo = If(RadioOverNpc.Checked AndAlso RadioOverNpc.Enabled, "actor", "pieza")
        ' ⛔⛔ LA CLAVE LLEVA TODO LO QUE CAMBIA EL DIBUJO. Antes era modo|body|FormID|MODL|TNAM, o sea
        ' que NO llevaba el TIPO, ni los bits de género, ni el RNAM, ni la lista de extras — y el modo
        ' «Over the NPC» existe justamente para ejercitar la COMPOSICIÓN, cuya entrada ES el tipo.
        ' Cambiar el tipo no redibujaba nada: `PedirPreview` cortaba en `clave = _lastPreviewKey`.
        '   ⛔ Los extras van unidos por `-` y NO por `,`: la clave usa `|` de separador de campos y
        ' una coma adentro de un campo se lee igual que cualquier otro carácter, pero deja la clave
        ' ilegible en el log justo cuando hay que leerla para entender por qué no redibujó.
        '   ⛔ Y LO QUE NO ENTRA, ESCRITO: el `MODS` (material swap) y las Alternate Textures de
        ' Skyrim NO están en la clave A PROPÓSITO. El preview dibuja la malla con el material que
        ' resuelve el `TNAM`; el swap se aplica en la cadena del NPC, no en la pieza suelta, así que
        ' meterlos forzaría un redibujo que da EL MISMO píxel. Si alguna vez el preview los aplica,
        ' entran acá en la misma línea — y entonces este párrafo es el que hay que borrar.
        Dim tipoParaClave As UInteger = 0UI
        TipoDelCombo(tipoParaClave)
        Dim extrasParaClave = String.Join("-", _draft.Record.PartesExtra().Select(Function(x) x.ToString("X8")))
        Dim clave = $"{modo}|{If(CheckIncludeBody.Checked, "1", "0")}|{_draft.FormID:X8}|" &
                    $"{TextBoxModl.Text.Trim().ToLowerInvariant()}|{FidDe(TextBoxTnam):X8}|{tipoParaClave}|" &
                    $"{If(CheckMale.Checked, 1, 0)}{If(CheckFemale.Checked, 1, 0)}{If(CheckIsExtraPart.Checked, 1, 0)}|" &
                    $"{FidDe(TextBoxRnam):X8}|{extrasParaClave}"
        If clave = _lastPreviewKey Then Return
        _lastPreviewKey = clave
        If modo = "actor" Then
            If _previewFallado OrElse _previewDebounce Is Nothing Then Return
            _previewDebounce.Stop()
            _previewDebounce.Start()
            Return
        End If
        DibujarLaPiezaSola()
    End Sub

    ''' <summary>El head part SOLO, por la MISMA casa que el selector (<see cref="PreviewDeHeadPart"/>) y
    ''' por la MISMA sede de resolución. ⛔ Es el testigo más corto del cableado: si un borrador no se ve
    ''' acá, la sede no está bien conectada — porque lo único que distingue a un borrador de un record real
    ''' en este camino es que la sede lo resuelva. No es testigo de la COMPOSICIÓN: para eso está el otro
    ''' modo y, sobre todo, el gate RENDER==BAKE.</summary>
    Private Sub DibujarLaPiezaSola()
        Try
            Dim armado = PreviewDeHeadPart.FormasDeLaCadena(_draft.FormID, _res)
            _preview.RenderShapes(armado.Formas)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[HDPT-EDITOR] preview falló: {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    Private Async Sub OnTicDelPreview(sender As Object, e As EventArgs)
        _previewDebounce.Stop()
        ' Un render en vuelo: se re-arma en vez de descartar esta edición.
        If _previewEnCurso Then
            _previewDebounce.Start()
            Return
        End If
        Await DibujarSobreElActorAsync()
    End Sub

    ''' <summary>El NPC entero con ESTE head part puesto, por <see cref="MainForm.RenderInHostAsync"/>.
    '''
    ''' <para>⛔⛔ <b>La lista de partes la compone LA LEY DEL MOTOR, no este editor.</b> El borrador entra
    ''' como una fuente MÁS —la de mayor prioridad— y <see cref="Canon.ResolverPartesDeCabeza"/> decide el
    ''' slot: así salen gratis el caso del tipo que ACUMULA (hoy sólo Scar, tipo 5), el de Misc que es
    ''' aditivo, y el <c>ganador(0)</c> cuando una fuente aporta dos del mismo tipo. Escribir acá un
    ''' «buscar el de este tipo y reemplazarlo» sería la CUARTA copia de esa ley, y el comentario de la ley
    ''' dice textualmente que antes estaba escrita tres veces con tres precedencias distintas.</para>
    '''
    ''' <para>⛔ Y el diccionario de presets que recibe el anfitrión es una COPIA con el preset del NPC
    ''' clonado por la sede canónica (<c>LooksmenuLoader.ClonePreset</c>): el editor no puede tocar el
    ''' overlay real de la app para dibujar un preview. Si lo hiciera, cancelar el editor dejaría al NPC
    ''' con el head part puesto.</para></summary>
    Private Async Function DibujarSobreElActorAsync() As Task
        If _host Is Nothing OrElse _npcFormID = 0UI OrElse _draft?.Record Is Nothing Then Return
        _previewEnCurso = True
        Try
            CommitVivo()
            ' ⛔⛔ POR LA PERILLA DEL HOST, no por una copia del diccionario de presets. Acá había un
            ' `_host.AppliedPresets = <copia con el borrador metido>`, y no hacía NADA: el
            ' `NpcStateResolver` recibe el diccionario de MainForm POR REFERENCIA en su constructor y
            ' resuelve con ese. Por eso «Over the NPC» salía vacío y, con el cuerpo, salía el NPC sin el
            ' head part. La perilla se aplica al final del estado y no toca el overlay compartido.
            _host.PreviewExtraHeadPartFormIDs = New List(Of UInteger) From {_draft.FormID}
            ' ⛔ Los toggles se RE-ARMAN en cada render desde la línea base de cara, y sólo se mueve
            ' `RenderBody`. Mutar `_host.Toggles` en el lugar dejaba el conjunto anterior mezclado con
            ' el nuevo, que es cómo un preview queda a medio filtrar sin que nada lo diga.
            ' ⛔⛔ `RenderBody` NO SE TOCA, Y ÉSTA ERA LA CAUSA DEL PREVIEW VACÍO.
            '
            ' `NpcRenderHost.ApplyRenderToggleVisibility` (:627) dice, literal:
            '     If Not renderBody AndAlso (cat = BodySkin OrElse cat = NakedHands
            '                                OrElse cat = ShapeRenderCategory.HeadPart) Then hide = True
            ' o sea que apagar `RenderBody` ESCONDE LAS HEAD PARTS. Y la categoría lo declara en su propio
            ' doc: «Head part (Kind=HeadPart). Forma parte del NPC desnudo — controlado por "Render body"».
            '
            ' Yo tenía `tg.RenderBody = CheckIncludeBody.Checked`, así que al destildar «Include Body» el
            ' host ocultaba TODOS los head parts y el preview salía COMPLETAMENTE VACÍO. El usuario lo
            ' reportó SIETE veces con el mismo síntoma exacto —«con body se ve, sin body no»— y yo le
            ' busqué la causa en el colector (que está bien: medido, devuelve 12 mallas con el borrador
            ' incluido), en el diccionario de presets del host y en la sede de resolución. Estaba acá.
            '
            ' `EditFace_Form` no tiene el problema porque NUNCA toca `RenderBody`: deja el True que le pone
            ' `RenderToggles.OnlyFace`. «Include Body» acá controla UNA sola cosa —`OnlyFaceCollect`, o sea
            ' si la piel y el atuendo se COLECTAN—: con el cuerpo apagado no hay nada de eso que esconder.
            Dim tg = RenderToggles.OnlyFace(False)
            _host.Toggles = tg
            ' Sólo la cabeza salvo que el usuario pida el cuerpo: el mismo gate del preview de Edit Face.
            _host.OnlyFaceCollect = Not CheckIncludeBody.Checked
            Await _mainForm.RenderInHostAsync(_host, _npcFormID)
        Catch ex As Exception
            _previewFallado = True
            LabelPreviewHint.Text = $"Preview over the NPC failed: {ex.GetType().Name} — showing the part only."
            RadioPartOnly.Checked = True
            Logger.LogLazy(Function() $"[HDPT-EDITOR] preview sobre el actor falló: {ex.GetType().Name}: {ex.Message}")
        Finally
            _previewEnCurso = False
        End Try
    End Function

    Private Function PartesDeLaRaza() As List(Of UInteger)
        Dim vacia As New List(Of UInteger)
        If _raceFormID = 0UI Then Return vacia
        Try
            Dim rec = _plugins.GetRecord(_raceFormID)
            If rec Is Nothing Then Return vacia
            Dim race = Canon.CanonRecords.Race(rec, _plugins)
            If race Is Nothing Then Return vacia
            Return New List(Of UInteger)(race.HeadPartsDe(_isFemale))
        Catch
            Return vacia
        End Try
    End Function

    Private Function PartesCrudasDelNpc() As List(Of UInteger)
        Dim vacia As New List(Of UInteger)
        If _npcFormID = 0UI Then Return vacia
        Try
            Dim rec = _plugins.GetRecord(_npcFormID)
            If rec Is Nothing Then Return vacia
            Dim npc = Canon.CanonRecords.Npc(rec, _plugins)
            If npc Is Nothing Then Return vacia
            Return New List(Of UInteger)(npc.PartesDeCabeza())
        Catch
            Return vacia
        End Try
    End Function

    '==============================================================================================
    ' Extras (HNAM)
    '==============================================================================================

    Private Sub RefrescarExtras()
        ListViewExtras.Items.Clear()
        If _draft?.Record Is Nothing Then Return
        For Each fid In _draft.Record.PartesExtra()
            Dim hd = _res.Hdpt(fid)
            Dim it As New ListViewItem(_mainForm.GetRecordDisplayNameForEditor(fid))
            it.SubItems.Add(If(hd Is Nothing, "(unresolved)", $"{hd.TipoDeParte()} — {hd.TypeNombre}"))
            it.SubItems.Add(If(Borradores.EsFormIdDeBorrador(fid), "(new)", NombreDelPlugin(fid)))
            it.Tag = fid
            ListViewExtras.Items.Add(it)
        Next
    End Sub

    Private Function FidSeleccionado(lv As ListView) As UInteger
        If lv.SelectedItems.Count = 0 Then Return 0UI
        Dim t = lv.SelectedItems(0).Tag
        If t Is Nothing Then Return 0UI
        Return CUInt(t)
    End Function

    Private Sub OnAgregarExtra(sender As Object, e As EventArgs)
        Dim fid = ElegirHdptReal("Add an extra part (HNAM)")
        If fid = 0UI Then Return
        AgregarExtra(fid)
    End Sub

    Private Sub OnNuevoExtra(sender As Object, e As EventArgs)
        ' ⛔ RECURSIVO: el extra de un head part es un head part, así que se edita con ESTE editor. La
        ' recursión es la del record, no una comodidad.
        Using dlg As New HeadPartEditor_Form(_mainForm, _npcFormID, _raceFormID, _isFemale)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.ResultHdptFormID <> 0UI Then AgregarExtra(dlg.ResultHdptFormID)
        End Using
    End Sub

    Private Sub AgregarExtra(fid As UInteger)
        If _draft?.Record Is Nothing OrElse fid = 0UI Then Return
        If fid = _draft.FormID Then
            MessageBox.Show(Me, "A head part cannot be its own extra part.", "Head Part Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        If _draft.Record.PartesExtra().Contains(fid) Then Return
        Dim nuevo = _draft.Record.AgregarExtraParts()
        If nuevo Is Nothing Then Return
        nuevo.Part = fid
        RefrescarExtras()
        CommitVivo()
        _lastPreviewKey = Nothing
        PedirPreview()
    End Sub

    Private Sub OnEditarExtra(sender As Object, e As EventArgs)
        Dim fid = FidSeleccionado(ListViewExtras)
        If fid = 0UI Then Return
        ' ⛔⛔ UN BORRADOR ABIERTO EN OTRO EDITOR NO SE ABRE DOS VECES. Este editor es RECURSIVO — el
        ' extra de un head part es un head part — y el borrador ES el record, compartido por REFERENCIA:
        ' con dos editores sobre el mismo objeto, el anidado muta el árbol que el padre muestra, su
        ' Cancel queda en un no-op mudo (el padre lo re-registra con la próxima tecla) y su OK se pierde
        ' (el `CommitVivo` del padre vuelca su formulario viejo encima). Las dos direcciones destruyen
        ' trabajo sin aviso. La marca la lleva `TomaDeBorrador`; acá sólo se pregunta.
        ' ⛔ La marca la pone también ESTE editor con su propia toma, así que el objetivo actual
        ' está «tomado» por nosotros: ese caso se distingue, o el aviso mentiría.
        If _draft IsNot Nothing AndAlso _draft.FormID = fid Then
            MessageBox.Show(Me, "That is the head part you are already editing in this window.",
                            "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        If Borradores.EstaTomado(fid) Then
            MessageBox.Show(Me, "That head part is already open in another editor window. Close it first.",
                            "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim ya = BuscarBorrador(fid)
        Using dlg As New HeadPartEditor_Form(_mainForm, _npcFormID, _raceFormID, _isFemale,
                                             editDraft:=ya,
                                             initialOverrideFormID:=If(ya Is Nothing, fid, 0UI))
            dlg.ShowDialog(Me)
        End Using
        RefrescarExtras()
        _lastPreviewKey = Nothing
        PedirPreview()
    End Sub

    ''' <summary>⛔ QUITAR UN EXTRA RECHAZA si algún NPC lo lleva PUESTO en su propio <c>PNAM</c>.
    ''' Decisión del usuario (20-sep), y el número que la sostiene: <b>2.272 de los 2.369</b> NPC de
    ''' Fallout 4 que tienen head parts llevan al menos un <c>IsExtraPart</c> directo en su lista. O sea
    ''' que sacar el extra del RECORD no lo saca del NPC: el head part se sigue viendo, y el usuario
    ''' creería que lo quitó.
    ''' <para>El rechazo NO es un callejón: dice cuántos son, nombra los primeros y dice qué hacer.</para></summary>
    ''' <summary>Quitar un extra (<c>HNAM</c>). ⛔⛔ AVISA Y PREGUNTA; NO RECHAZA.
    ''' <para>La decisión E original del usuario (20-sep) era RECHAZAR. El mismo día, viéndolo en
    ''' pantalla, la cambió: <i>«el cartel solo warning con "yes" lo hago igual "no" no lo borro»</i>.
    ''' Así que el gesto queda en sus manos y el aviso sólo dice a quién afecta, con el botón NO por
    ''' defecto. Lo que NO cambió es CUÁNDO aparece — ver abajo.</para>
    ''' <para>(texto anterior, conservado porque explica la pregunta que el aviso contesta) ⛔ LA
    ''' GESTO LE SACA EL EXTRA A ALGUIEN, y la pregunta que contesta eso NO es «quien lleva el extra».
    '''
    ''' <para>Aca se contaban los NPC que traen el EXTRA en su propio <c>PNAM</c> y se rechazaba si
    ''' habia alguno. Estaba mal por dos motivos, y los dos se veian en pantalla a la vez (el usuario
    ''' lo reporto sobre un «New from template…»):</para>
    ''' <list type="number">
    ''' <item>esos NPC son justamente los que NO se ven afectados —el propio texto del cartel lo decia,
    ''' «removing it here would NOT remove it from them»—, asi que era un motivo para PERMITIR;</item>
    ''' <item>no se miraba si alguien referencia AL PADRE que se esta editando. En un record NUEVO el
    ''' FormID es provisional y ningun NPC del orden de carga puede apuntarle, asi que el gesto no le
    ''' puede sacar el extra a nadie — y el cartel salia igual, bloqueando una edicion inofensiva.</item>
    ''' </list>
    ''' <para>La pregunta correcta: le quita el extra a un NPC que (a) usa ESTE head part como padre y
    ''' (b) NO lleva el extra por su cuenta. Los que lo llevan directo se informan y no bloquean.</para></summary>
    Private Sub OnQuitarExtra(sender As Object, e As EventArgs)
        Dim fid = FidSeleccionado(ListViewExtras)
        If fid = 0UI OrElse _draft?.Record Is Nothing Then Return
        ' ⛔⛔ AVISO GENÉRICO, SIN CONTAR NADA. Orden del usuario: «NO CUENTES Y LISTO», «avisa en
        ' forma genérica» — y el motivo que dio es correcto: la versión anterior recorría TODOS los
        ' <c>NPC_</c> del orden de carga y PARSEABA cada uno por el árbol canónico para leerle el PNAM,
        ' en cada clic de Remove. Yo lo había justificado con «no hay índice inverso», que es una excusa
        ' y no un motivo: el aviso no necesita el número para decir lo que importa.
        '
        ' ⛔ Y SIGUE APARECIENDO SÓLO EN UN OVERRIDE. Sobre un record propio no hay tercero a quien
        ' proteger — su FormID es provisional, así que nada del orden de carga puede apuntarle —, y
        ' preguntar ahí era bloquear el gesto normal de sacarle un extra a algo tuyo.
        If _draft.IsOverride Then
            Dim r = MessageBox.Show(Me,
                "This head part already exists in your load order, so this change also reaches the NPCs " &
                "that use it — not just the one you are editing for. Any NPC that gets this extra part " &
                "through this head part, and does not carry it in its own head-part list, will lose it." &
                vbCrLf & vbCrLf & "Remove it anyway?",
                "This head part is shared", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2)
            If r <> DialogResult.Yes Then Return
        End If
        For Each ex In _draft.Record.ExtraParts.ToList()
            If ex.Part = fid Then _draft.Record.QuitarExtraParts(ex) : Exit For
        Next
        RefrescarExtras()
        CommitVivo()
        _lastPreviewKey = Nothing
        PedirPreview()
    End Sub

    Private Sub MoverExtra(delta As Integer)
        If _draft?.Record Is Nothing Then Return
        Dim idx = If(ListViewExtras.SelectedIndices.Count = 0, -1, ListViewExtras.SelectedIndices(0))
        If idx < 0 Then Return
        Dim destino = idx + delta
        If destino < 0 OrElse destino >= ListViewExtras.Items.Count Then Return
        Dim orden = Enumerable.Range(0, ListViewExtras.Items.Count).ToList()
        orden(idx) = destino : orden(destino) = idx
        _draft.Record.ReordenarExtraParts(orden)
        RefrescarExtras()
        If destino < ListViewExtras.Items.Count Then ListViewExtras.Items(destino).Selected = True
        CommitVivo()
    End Sub

    '==============================================================================================
    ' Parts (NAM0 + NAM1)
    '==============================================================================================

    Private Sub RefrescarPartes()
        ListViewParts.Items.Clear()
        If _draft?.Record Is Nothing Then Return
        For i = 0 To _draft.Record.Parts.Count - 1
            Dim p = _draft.Record.Parts(i)
            Dim it As New ListViewItem($"{p.PartType} — {p.PartTypeNombre}")
            it.SubItems.Add(If(p.PartFilename, ""))
            it.Tag = i
            ListViewParts.Items.Add(it)
        Next
    End Sub

    ''' <summary>Agrega una entrada de <c>Parts</c> (un <c>.tri</c>). ⛔ PREGUNTA PRIMERO Y AGREGA
    ''' DESPUÉS, y el orden no es cosmético.
    ''' <para>Acá se agregaba primero y se abría el diálogo después. El <c>Part</c> del esquema declara
    ''' el <c>NAM1</c> como <c>.AsRequired()</c> y el <c>NAM0</c> no, así que
    ''' <c>WbRStructDef.CreateRequired</c> creaba un <c>NAM1</c> VACÍO y NINGÚN <c>NAM0</c> — y si el
    ''' usuario cancelaba el diálogo, eso quedaba en el record.</para>
    ''' <para>⛔ MEDIDO sobre los dos corpus, y es la forma que NO EXISTE: <c>NAM1</c> vacío aparece en
    ''' <b>0 de 2.262</b> pares de Fallout 4 y <b>0 de 6.938</b> de Skyrim, y los dos subrecords
    ''' vienen SIEMPRE en pares — cero <c>NAM0</c> sin <c>NAM1</c> y cero <c>NAM1</c> sin <c>NAM0</c>.
    ''' Preguntando primero, la entrada nace con el par completo o no nace.</para></summary>
    Private Sub OnAgregarParte(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing Then Return
        Using dlg As New HeadPartFileEditor_Form(_game, 0UI, "")
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If String.IsNullOrWhiteSpace(dlg.FileName) Then Return
            Dim nuevo = _draft.Record.AgregarParts()
            If nuevo Is Nothing Then Return
            ' El `NAM0` se escribe SIEMPRE, aunque el tipo sea 0: escribirlo lo CREA, y sin él la
            ' entrada sale con `NAM1` suelto — una forma que el corpus no tiene.
            nuevo.PartType = dlg.PartType
            nuevo.PartFilename = dlg.FileName
        End Using
        RefrescarPartes()
        CommitVivo()
        If ListViewParts.Items.Count > 0 Then
            ListViewParts.Items(ListViewParts.Items.Count - 1).Selected = True
        End If
    End Sub

    Private Sub OnEditarParte(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing OrElse ListViewParts.SelectedItems.Count = 0 Then Return
        Dim idx = CInt(ListViewParts.SelectedItems(0).Tag)
        If idx < 0 OrElse idx >= _draft.Record.Parts.Count Then Return
        Dim parte = _draft.Record.Parts(idx)
        Using dlg As New HeadPartFileEditor_Form(_game, parte.PartType, If(parte.PartFilename, ""))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            ' ⛔ El nombre VACÍO no se escribe: `NAM1` vacío no existe en el corpus (0 de 2.262 pares en
            ' Fallout 4, 0 de 6.938 en Skyrim). Si el usuario borró la caja, lo que quiere es SACAR la
            ' entrada, y para eso está el botón de quitar — no dejarla a medias.
            If String.IsNullOrWhiteSpace(dlg.FileName) Then
                MessageBox.Show(Me, "A head part file entry needs a filename. Use Remove to delete it.",
                                "Head part files", MessageBoxButtons.OK, MessageBoxIcon.Warning)
                Return
            End If
            parte.PartType = dlg.PartType
            parte.PartFilename = dlg.FileName
        End Using
        RefrescarPartes()
        CommitVivo()
    End Sub

    Private Sub OnQuitarParte(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing OrElse ListViewParts.SelectedItems.Count = 0 Then Return
        Dim idx = CInt(ListViewParts.SelectedItems(0).Tag)
        _draft.Record.QuitarParts(idx)
        RefrescarPartes()
        CommitVivo()
    End Sub

    '==============================================================================================
    ' Conditions (sólo Fallout 4)
    '==============================================================================================

    Private Sub RefrescarCondiciones()
        ListViewConditions.ShowItemToolTips = True
        ListViewConditions.Items.Clear()
        Dim fo4 = TryCast(_draft?.Record, Canon.HdptFO4)
        If fo4 Is Nothing Then Return
        LabelConditionsHint.Text =
            "40 of Fallout 4's 2.546 head parts carry conditions, and all of them are one GetStageDone. " &
            "The operator has SIX values (Equal / Not Equal / Greater / Greater-or-Equal / Less / Less-or-Equal)."
        For i = 0 To fo4.Conditions.Count - 1
            Dim c = fo4.Conditions(i)
            Dim it As New ListViewItem((i + 1).ToString())
            it.SubItems.Add(If(CondicionesDeHeadPart.TieneBanderaOr(c), "Or", ""))
            it.SubItems.Add(CondicionesDeHeadPart.NombreDeFuncion(c, _game))
            it.SubItems.Add(CondicionesDeHeadPart.TextoDeParametro(c, 1, _mainForm))
            it.SubItems.Add(CondicionesDeHeadPart.TextoDeParametro(c, 2, _mainForm))
            it.SubItems.Add(CondicionesDeHeadPart.TextoDeComparacion(c))
            it.SubItems.Add(CondicionesDeHeadPart.NombreDeRunOn(c, _game))
            it.Tag = i
            ' ⛔ LA MARCA DE «RECÉN CERADO». El cero que el sub-editor pone al cambiar la función es un
            ' RESET reproducible, NO el «ninguno» del formato: para un índice de alias o para un
            ' enumerado (`Sex`, `Axis`, `Form Type`…) el 0 es un valor NOMBRADO y con significado. Sin
            ' la marca, la fila muestra «0» y el usuario no distingue «elegí cero» de «quedó cero».
            ' Se va sola en cuanto el usuario acepta el sub-editor sin cambiar la función. [rev-77]
            If _condicionesCeradas.Contains(i) Then
                it.Text = "⚠ " & it.Text
                it.ForeColor = Drawing.Color.DarkOrange
                it.ToolTipText = "The function changed, so the parameters were reset to 0. Zero is a " &
                                 "real value for an index or an enum — check them."
            End If
            ListViewConditions.Items.Add(it)
        Next
    End Sub

    Private Sub OnAgregarCondicion(sender As Object, e As EventArgs)
        Dim fo4 = TryCast(_draft?.Record, Canon.HdptFO4)
        If fo4 Is Nothing Then Return
        Dim nueva = fo4.AgregarConditions()
        If nueva Is Nothing Then Return
        ' ⛔⛔ Y SE MATERIALIZA EL `CTDA`, O ESTE BOTÓN NO HACE NADA. `AgregarConditions` llega a
        ' `WbRStructDef.CreateRequired`, que agrega SÓLO los miembros con `Required = True`, y el
        ' `Condition` del HDPT no tiene ninguno: ni el `CTDA` ni los `CIS1`/`CIS2` llevan
        ' `.AsRequired()` en el esquema. O sea que el elemento nacía CON CERO HIJOS: la fila aparecía
        ' en la lista, el diálogo abría con todos los `*Presente` en False — nada editable, y el OK
        ' no podía escribir un solo campo —, y al emitir el `CITC` contaba una condición SIN bytes
        ' de `CTDA`. Un botón que agrega una fila fantasma y corrompe la cuenta.
        '   Escribirle un campo LO CREA con el valor por defecto del esquema — es el contrato del
        ' árbol, textual: «Escribir un campo que no está LO CREA, cualquiera sea el valor». El cero
        ' no es un valor elegido por mí: es el default de la declaración, y el usuario elige la
        ' función en el diálogo que se abre tres líneas más abajo.
        nueva.ConditionType = 0
        RefrescarCondiciones()
        CommitVivo()
        ' ⛔⛔ Y SI EL USUARIO CANCELA, LA CONDICIÓN SE VA. Con el `CTDA` materializado en ceros la
        ' condición es `GetWantBlocking == 0` — la función 0 está NOMBRADA en la tabla
        ' (`WbConditions_FO4.vb:575`) y declara CERO parámetros (`:36`) —, o sea LEGAL y EVALUABLE.
        ' Eso la vuelve peor que la fila fantasma de antes: el motor la TESTEA, y un head part con
        ' una condición que no se cumple NO SE DIBUJA. Un gesto abandonado no puede cambiar la
        ' conducta del record.
        '   Es la misma forma que el «Add» de `Parts`: el diálogo decide si la entrada existe.
        Dim idxNueva = fo4.Conditions.Count - 1
        If idxNueva < 0 Then Return
        If Not EditarCondicionEnIndice(idxNueva) Then
            fo4.QuitarConditions(idxNueva)
            RefrescarCondiciones()
            CommitVivo()
            Return
        End If
        If ListViewConditions.Items.Count > 0 Then
            ListViewConditions.Items(ListViewConditions.Items.Count - 1).Selected = True
        End If
    End Sub

    Private Sub OnEditarCondicion(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing OrElse ListViewConditions.SelectedItems.Count = 0 Then Return
        EditarCondicionEnIndice(CInt(ListViewConditions.SelectedItems(0).Tag))
    End Sub

    ''' <summary>Abre el sub-editor sobre la condición de ese índice. Devuelve True si el usuario
    ''' aceptó. ⛔ Es UNA función y no dos porque el «Add» necesita el mismo gesto y su RESULTADO:
    ''' con Cancel tiene que poder deshacer lo que agregó.</summary>
    Private Function EditarCondicionEnIndice(idx As Integer) As Boolean
        Dim fo4 = TryCast(_draft?.Record, Canon.HdptFO4)
        If fo4 Is Nothing Then Return False
        If idx < 0 OrElse idx >= fo4.Conditions.Count Then Return False
        Dim acepto As Boolean
        Using dlg As New ConditionsEditor_Form(_mainForm, _game, fo4.Conditions(idx))
            acepto = (dlg.ShowDialog(Me) = DialogResult.OK)
            ' ⛔ EL ESTADO «RECÉN CERADO» SE RECUERDA Y SE MARCA. El cero que el sub-editor pone al
            ' cambiar la función es un RESET, no un «ninguno» universal: para un índice de alias o para
            ' un enumerado (`Sex`, `Axis`, `Form Type`…) el 0 es un valor con significado. Si la fila
            ' mostrara «0» sin más, el usuario no distingue «elegí cero» de «quedó cero». [rev-77]
            If acepto AndAlso dlg.ParametrosCerados Then
                _condicionesCeradas.Add(idx)
            ElseIf acepto Then
                _condicionesCeradas.Remove(idx)
            End If
        End Using
        RefrescarCondiciones()
        CommitVivo()
        Return acepto
    End Function

    ''' <summary>Índices de condición cuyos parámetros el sub-editor acaba de poner en CERO.
    ''' <para>⛔ Se VACÍA al quitar o al mover una condición, y eso no es pereza: los índices se
    ''' corren, y una marca que se queda apunta a OTRA fila — sería peor que no marcar, porque dice
    ''' «revisá esto» sobre algo que el usuario sí eligió. Es la misma trampa que la etiqueta de gate
    ''' repetida, que atribuye mal.</para></summary>
    Private ReadOnly _condicionesCeradas As New HashSet(Of Integer)

    Private Sub OnQuitarCondicion(sender As Object, e As EventArgs)
        Dim fo4 = TryCast(_draft?.Record, Canon.HdptFO4)
        If fo4 Is Nothing OrElse ListViewConditions.SelectedItems.Count = 0 Then Return
        fo4.QuitarConditions(CInt(ListViewConditions.SelectedItems(0).Tag))
        ' Los índices se corrieron: ver el ⛔ de `_condicionesCeradas`.
        _condicionesCeradas.Clear()
        RefrescarCondiciones()
        CommitVivo()
    End Sub

    Private Sub MoverCondicion(delta As Integer)
        Dim fo4 = TryCast(_draft?.Record, Canon.HdptFO4)
        If fo4 Is Nothing OrElse ListViewConditions.SelectedIndices.Count = 0 Then Return
        Dim idx = ListViewConditions.SelectedIndices(0)
        Dim destino = idx + delta
        If destino < 0 OrElse destino >= ListViewConditions.Items.Count Then Return
        Dim orden = Enumerable.Range(0, ListViewConditions.Items.Count).ToList()
        orden(idx) = destino : orden(destino) = idx
        fo4.ReordenarConditions(orden)
        ' Los índices se corrieron: ver el ⛔ de `_condicionesCeradas`.
        _condicionesCeradas.Clear()
        RefrescarCondiciones()
        If destino < ListViewConditions.Items.Count Then ListViewConditions.Items(destino).Selected = True
        CommitVivo()
    End Sub

    '==============================================================================================
    ' Pickers y sub-editores
    '==============================================================================================

    Private Function ElegirHdptReal(titulo As String) As UInteger
        Dim entradas As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.HdptDrafts()
            If d?.Record Is Nothing OrElse d.FormID = _draft?.FormID Then Continue For
            entradas.Add(New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID,
                .DisplayName = If(String.IsNullOrEmpty(d.Record.Name), d.Record.EditorID, d.Record.Name),
                .Signature = "HDPT", .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        ' ⛔ Y los ya guardados, por lo mismo: ver `EntradasGuardadas`.
        entradas.AddRange(EntradasGuardadas("HDPT", entradas))
        Using dlg As New FormIdPicker_Form(_plugins, {"HDPT"}, titulo, 0UI, False, entradas,
                                           Nothing, AddressOf OnBorrarEntradaDeBorrador)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>Los TXST borrador como entradas de un selector. ⛔ Está afuera del diálogo porque hay
    ''' MÁS DE UN selector que los necesita: el `TNAM` de acá y el del sub-editor de Alternate
    ''' Textures de Skyrim, que elige un TXST por entrada del `MODS`. Lo que se comparte es la LISTA,
    ''' no el diálogo — compartir el diálogo obligaría al sub-editor a fingir que tiene un TextBox
    ''' destino, que es lo que no tiene.</summary>
    Friend Function EntradasDeBorradorTxst() As List(Of FormIdPickerEntry)
        Dim r As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.TxstDrafts()
            If d?.Record Is Nothing Then Continue For
            r.Add(New FormIdPickerEntry With {.FormID = d.FormID, .EditorID = d.Record.EditorID,
                                              .DisplayName = d.Record.EditorID, .Signature = "TXST",
                                              .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        Return r
    End Function

    ''' <summary>Las FLST borrador como entradas de un selector. Dos llamadores: el `RNAM` por
    ''' selección directa y «Fit to this NPC».</summary>
    Private Function EntradasDeBorradorFlst() As List(Of FormIdPickerEntry)
        Dim r As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.FlstDrafts()
            If d?.Record Is Nothing Then Continue For
            r.Add(New FormIdPickerEntry With {.FormID = d.FormID, .EditorID = d.Record.EditorID,
                                              .DisplayName = d.Record.EditorID, .Signature = "FLST",
                                              .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        Return r
    End Function

    ''' <summary>Los records PROPIOS YA GUARDADOS de esa clase, como entradas de un selector.
    ''' <para>⛔⛔ LA MITAD QUE FALTABA EN TODOS LOS SELECTORES. Un selector que ofrece borradores
    ''' tiene que ofrecer también lo que el usuario YA GUARDÓ en su plugin: si no, guardar y reabrir
    ''' la app hace desaparecer sus propios records de la lista — los ve en xEdit y no acá. El usuario
    ''' lo reportó por la puerta de «Edit mine…» («me dice que NO HAY!! pero si creo uno con el nombre
    ''' del que ya estaba me da error») y pasaba igual en el `TNAM`, el `RNAM` y el `MODS`.
    ''' <c>ArmaEditor_Form</c> ya lo hacía así; este editor se escribió sin esa mitad.</para></summary>
    Private Function EntradasGuardadas(sig As String, yaEstan As IEnumerable(Of FormIdPickerEntry)) As List(Of FormIdPickerEntry)
        Dim r As New List(Of FormIdPickerEntry)
        Dim vistos As New HashSet(Of UInteger)(yaEstan.Select(Function(x) x.FormID))
        For Each g In _mainForm.GetAuthoredRecords(sig)
            If vistos.Contains(g.FormID) Then Continue For
            r.Add(New FormIdPickerEntry With {.FormID = g.FormID, .EditorID = g.EditorID,
                                              .DisplayName = g.DisplayName, .Signature = sig,
                                              .PluginName = "(saved)"})
        Next
        Return r
    End Function

    ''' <summary>Los MSWP borrador como entradas de un selector (el `MODS` de Fallout 4).</summary>
    Private Function EntradasDeBorradorMswp() As List(Of FormIdPickerEntry)
        Dim r As New List(Of FormIdPickerEntry)
        For Each d In _mainForm.MswpDrafts()
            If d?.Record Is Nothing Then Continue For
            r.Add(New FormIdPickerEntry With {.FormID = d.FormID, .EditorID = d.Record.EditorID,
                                              .DisplayName = d.Record.EditorID, .Signature = "MSWP",
                                              .PluginName = If(d.IsOverride, "(override)", "(new)")})
        Next
        Return r
    End Function

    Private Sub ElegirFidEn(destino As TextBox, sigs As String(), titulo As String,
                            Optional conBorradoresTxst As Boolean = False,
                            Optional conBorradoresFlst As Boolean = False,
                            Optional conBorradoresMswp As Boolean = False)
        Dim entradas As New List(Of FormIdPickerEntry)
        If conBorradoresTxst Then entradas.AddRange(EntradasDeBorradorTxst())
        If conBorradoresFlst Then entradas.AddRange(EntradasDeBorradorFlst())
        If conBorradoresMswp Then entradas.AddRange(EntradasDeBorradorMswp())
        ' ⛔ Y LOS YA GUARDADOS de la misma clase: ver `EntradasGuardadas`.
        If conBorradoresTxst Then entradas.AddRange(EntradasGuardadas("TXST", entradas))
        If conBorradoresFlst Then entradas.AddRange(EntradasGuardadas("FLST", entradas))
        If conBorradoresMswp Then entradas.AddRange(EntradasGuardadas("MSWP", entradas))
        Using dlg As New FormIdPicker_Form(_plugins, sigs, titulo, FidDe(destino), True,
                                           If(entradas.Count = 0, Nothing, entradas),
                                           Nothing, AddressOf OnBorrarEntradaDeBorrador)
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            PonerFidEn(destino, dlg.SelectedFormID)
        End Using
        CommitVivo()
        If destino Is TextBoxRnam Then RefrescarRazasDeLaFlst()
        If destino Is TextBoxTnam Then _lastPreviewKey = Nothing : PedirPreview()
        RefrescarValidez()
    End Sub

    ''' <summary>El camino de BAJA de un borrador ofrecido en un picker. ⛔ Es la mitad obligatoria de
    ''' ofrecerlo: sin esto el botón «Delete / Revert…» ni se ve y el borrador queda sin salida después
    ''' del OK. Es la misma ley que <c>BorradoDeMswp</c> aplica para los material swap.</summary>
    ''' <summary>El «Delete / Revert…» del selector de «Edit mine…».
    ''' <para>⛔⛔ SI LO BORRADO ERA EL OBJETIVO ACTUAL, HAY QUE SOLTAR LA TOMA. Sin esto, el borrador
    ''' se daba de baja del registro y el editor seguía con <c>_draft</c> apuntándole y la toma tomada:
    ''' el commit siguiente —cualquier tecla, o el propio cambio de objetivo— lo RE-REGISTRABA, así que
    ''' el borrado se deshacía solo y, con el objetivo nuevo tomado, quedaban DOS. Lo reportó el
    ''' usuario: «si hago delete/revert se duplica».</para>
    ''' <para>⛔ Y se llama <see cref="TomaDeBorrador(Of TD).Soltar"/>, NO <c>Abandonar</c>. Su propio
    ''' doc lo dice: abandonar acá REPONDRÍA lo que el usuario acaba de borrar, porque la toma todavía
    ''' tiene su snapshot y la ley diría «restaurar» — por encima del <c>MarkRecordForRemoval</c> que el
    ''' mismo gesto acaba de poner. Los dos métodos existen por esta diferencia.</para>
    ''' <para>El editor queda SIN OBJETIVO y el banner lo dice, en vez de crear otro borrador en blanco
    ''' —que es justo lo que acumulaba de más—.</para></summary>
    Private Function OnBorrarEntradaDeBorrador(entry As FormIdPickerEntry) As Boolean
        If entry Is Nothing OrElse entry.FormID = 0UI Then Return False
        Dim eraElObjetivo As Boolean = (_draft IsNot Nothing AndAlso _draft.FormID = entry.FormID)
        Dim ok = BorradoDeHeadParts.BorrarORevertir(_mainForm, Me, entry)
        If ok Then _huboCambios = True
        If ok AndAlso eraElObjetivo Then
            _toma.Soltar()
            _draft = Nothing
            _openSnapshot = Nothing
            _resultHdptFormID = 0UI
            _lastPreviewKey = Nothing
            ActualizarBanner()
        End If
        Return ok
    End Function

    Private Sub OnNuevoOEditarTxst(sender As Object, e As EventArgs)
        Dim fid = FidDe(TextBoxTnam)
        Dim d = _mainForm.TxstDraftPorFormId(fid)
        If d Is Nothing AndAlso fid <> 0UI Then d = _mainForm.BuildTxstOverrideDraftFromReal(fid)
        Using dlg As New TextureSetEditor_Form(_mainForm, d)
            ' ⛔⛔ ACÁ HABÍA UN `UnregisterTxstDraft` Y ERA UN SEGUNDO DUEÑO QUE DESTRUÍA TRABAJO.
            ' El sub-editor YA resuelve el abandono: `_toma.Abandonar()` en Cancel y en `FormClosing`, y
            ' para un borrador que YA estaba registrado `Borradores.QueHacerAlAbandonar` toma la rama
            ' `Restaurar`. La línea del llamador borraba justo lo que `Abandonar` acababa de reponer:
            ' abrías el sub-editor por segunda vez, dabas Cancel, y tu TXST desaparecía. Y como el HDPT
            ' seguía apuntando a su FormID provisional, el guardado siguiente se RECHAZABA entero con
            ' «provisional reference … with no draft». Lo levantó la revisión adversarial [rev-52].
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.ResultTxstFormID <> 0UI Then PonerFidEn(TextBoxTnam, dlg.ResultTxstFormID)
        End Using
        CommitVivo()
        _lastPreviewKey = Nothing
        PedirPreview()
    End Sub

    Private Sub OnNuevoOEditarFlst(sender As Object, e As EventArgs)
        Dim fid = FidDe(TextBoxRnam)
        Dim d = _mainForm.FlstDraftPorFormId(fid)
        If d Is Nothing AndAlso fid <> 0UI Then d = _mainForm.BuildFlstOverrideDraftFromReal(fid)
        Using dlg As New FormListEditor_Form(_mainForm, d, soloRazas:=True)
            ' ⛔⛔ ACÁ HABÍA UN `UnregisterFlstDraft` Y ERA UN SEGUNDO DUEÑO QUE DESTRUÍA TRABAJO.
            ' El sub-editor YA resuelve el abandono: `_toma.Abandonar()` en Cancel y en `FormClosing`, y
            ' para un borrador que YA estaba registrado `Borradores.QueHacerAlAbandonar` toma la rama
            ' `Restaurar`. La línea del llamador borraba justo lo que `Abandonar` acababa de reponer:
            ' abrías el sub-editor por segunda vez, dabas Cancel, y tu FLST desaparecía. Y como el HDPT
            ' seguía apuntando a su FormID provisional, el guardado siguiente se RECHAZABA entero con
            ' «provisional reference … with no draft». Lo levantó la revisión adversarial [rev-52].
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            If dlg.ResultFlstFormID <> 0UI Then PonerFidEn(TextBoxRnam, dlg.ResultFlstFormID)
        End Using
        CommitVivo()
        RefrescarRazasDeLaFlst()
        RefrescarValidez()
    End Sub

    ''' <summary>El sub-editor de material swap del bloque `Model`. ⛔⛔ EL BORRADOR SE BUSCA
    ''' PRIMERO, igual que en los dos gemelos (`ArmaEditor_Form.OnNewEditMswp`, `ArmoEditor_Form`).
    ''' <para>Acá faltaba ese primer paso y el efecto era que el editor PERDÍA el trabajo:
    ''' `BuildMswpOverrideDraftFromReal` devuelve Nothing para un FormID provisional
    ''' (`MainForm.vb`, `If … EsFormIdDeBorrador(fid) Then Return Nothing`), así que al reabrir un
    ''' swap recién hecho se construía y REGISTRABA otro EN BLANCO, el diálogo abría vacío, y el OK
    ''' re-apuntaba el `MODS` al nuevo. El primero quedaba registrado y `IsNew ⇒ IsDirty`, así que la
    ''' fase 2g lo emitía igual: un MSWP huérfano en el .esp y las sustituciones del usuario
    ''' invisibles. Es el mismo patrón que esta ola persiguió diez veces — preguntarle al disco por
    ''' un FormID que sólo existe como borrador — y se me coló en un sitio nuevo.</para>
    ''' <para>⛔ Y `esNuevo` se captura ANTES de construir, no después: `BuildMswpOverrideDraftFromReal`
    ''' REGISTRA lo que devuelve, así que calculándolo después daba False sobre un MSWP real y el
    ''' Cancel no daba de baja nada — abrir y cancelar dejaba un override registrado que el usuario
    ''' nunca aceptó.</para></summary>
    Private Sub OnNuevoOEditarMswp(sender As Object, e As EventArgs)
        Dim fid = FidDe(TextBoxMods)
        Dim d = _mainForm.TryGetMswpDraft(fid)
        Dim esNuevo As Boolean = (d Is Nothing)
        If d Is Nothing Then
            ' El campo apunta a un MSWP REAL ⇒ se edita como OVERRIDE sembrado con sus sustituciones.
            d = _mainForm.BuildMswpOverrideDraftFromReal(fid)
            If d Is Nothing Then
                d = MswpDraft.Nuevo(_mainForm.AllocateDraftFormID(), _game)
                If d Is Nothing Then Return
                d.Record.EditorID = MswpDraft.EditorIdPrefix & "new"
                _mainForm.RegisterMswpDraft(d)
            End If
        End If
        Using dlg As New MswpSubEditor_Form(_mainForm, d, TextBoxModl.Text.Trim(), TextBoxModl.Text.Trim())
            If dlg.ShowDialog(Me) <> DialogResult.OK Then
                If esNuevo Then _mainForm.UnregisterMswpDraft(d.FormID)
                Return
            End If
        End Using
        PonerFidEn(TextBoxMods, d.FormID)
        CommitVivo()
    End Sub

    ''' <summary>El picker de NIF de la casa (<see cref="MeshPicker_Form"/>), el MISMO que usan los
    ''' editores de ARMA y ARMO: árbol del diccionario de archivos —o sea que ve lo que está DENTRO de los
    ''' BA2, que un diálogo de archivos del sistema no puede ver— y preview GL de la malla elegida.
    ''' <para>⛔ Se siembra con la ruta que el campo YA tiene, así que abre en la carpeta de la malla
    ''' actual y no en la raíz. La clave del picker lleva el prefijo <c>Meshes\</c> y el record lo guarda
    ''' SIN él, así que se agrega para sembrar y se saca al volver.</para></summary>
    Private Sub OnBuscarMalla(sender As Object, e As EventArgs)
        Dim exts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".nif"}
        Dim keys = FilesDictionary_class.GetFilteredKeys(MeshesPrefix, exts)
        Using dlg As New MeshPicker_Form(keys, MeshesPrefix, exts, TextBoxModl.Text.Trim())
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return
            Dim sel = dlg.SelectedKey
            If String.IsNullOrEmpty(sel) Then Return
            TextBoxModl.Text = sel.StripPrefix(MeshesPrefix)
        End Using
    End Sub

    '==============================================================================================
    ' OK / Cancel
    '==============================================================================================

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _draft?.Record Is Nothing Then
            DialogResult = DialogResult.Cancel : Close() : Return
        End If
        ' ⛔⛔ OK SOBRE UN BORRADOR NUEVO Y SIN TOCAR NO REGISTRA NADA. `CommitVivo` REGISTRA, y un
        ' borrador nuevo nace `IsNew` ⇒ `IsDirty` ⇒ la fase 2l lo EMITE al .esp — «todo borrador sucio
        ' se emite, referenciado o no». Así que abrir el editor y apretar OK escribía un HDPT VACÍO,
        ' con EditorID automático, sin malla y con PNAM 0. Es el record `npcm_<esp>_HDPT_802` que el
        ' usuario encontró en xEdit y no sabía de dónde salía. Yo había arreglado esto para Cancel y
        ' para la X (no se registra al abrir) y dejé la puerta de OK abierta. Lo levantó [rev-51].
        ' ⛔⛔ EL PREDICADO ES «¿SIGUE SIENDO IDÉNTICO A UNO EN BLANCO?», y NO necesita el snapshot.
        ' La primera versión comparó contra `_openSnapshot`, que sale de un `Clone()` dentro de un
        ' `Try`: si el clon falla, `_openSnapshot` queda Nothing, la guarda NO CORRE y vuelve el
        ' defecto. Argumenté que eso «falla hacia registrar» y que registrar no pierde trabajo — y
        ' estaba INVERTIDO: un borrador basura registrado hace TIRAR a `BuildHdptEntry` al guardar,
        ' o sea que mata el guardado ENTERO. Fallar hacia registrar es la dirección que rompe.
        '   Un record en blanco se construye en el acto y `ContentEquals` no pasa por `Copia()`, así
        ' que la guarda no depende de ningún clon. Y responde mejor la pregunta: un clon de
        ' plantilla NUNCA está en blanco, aunque el usuario no lo haya tocado.
        If _draft.IsNew Then
            Dim intacto As Boolean = False
            Try
                Dim enBlanco = HdptDraft.Nuevo(_draft.FormID, _game)
                intacto = enBlanco IsNot Nothing AndAlso _draft.ContentEquals(enBlanco)
            Catch ex As Exception
                ' No queda mudo: si esto falla, el HDPT vacío vuelve a colarse al .esp y hay que saber
                ' por qué. Es el único camino en que la guarda no puede decidir.
                Logger.LogLazy(Function() $"[HDPT-EDITOR] guarda del OK: no se pudo construir el record en blanco: {ex.GetType().Name}: {ex.Message}")
            End Try
            If intacto Then
                ' Se trata como un abandono: el usuario no hizo un record, apretó OK sobre nada.
                _toma.Abandonar()
                _resultHdptFormID = 0UI
                DialogResult = DialogResult.Cancel : Close() : Return
            End If
        End If
        If Not CommitVivo() Then
            MessageBox.Show(Me, "The edit could not be written to the record — nothing was accepted.",
                            "Head Part Editor", MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        Dim edid = TextBoxEdid.Text.Trim()
        If edid.Length = 0 Then
            edid = HdptDraft.EditorIdPrefix & $"{_draft.FormID And &HFFFUI:X3}"
            TextBoxEdid.Text = edid
            CommitVivo()
        End If
        If Not _draft.IsOverride AndAlso Not _mainForm.IsRecordEditorIdAvailable(edid, _draft) Then
            MessageBox.Show(Me, $"The EditorID '{edid}' is already taken by another record or draft. " &
                                "EditorIDs are globally unique.", "Head Part Editor",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            Return
        End If
        _toma.Soltar()
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub OnCancel(sender As Object, e As EventArgs)
        _toma.Abandonar()
        DialogResult = DialogResult.Cancel
        Close()
    End Sub

    ''' <summary>EL CIERRE, Y EL ORDEN DEL APAGADO DEL PREVIEW ES LA LEY — la misma de
    ''' <c>ArmaEditor_Form</c>, <c>OutfitPicker_Form</c> y <c>EditBody_Form</c>:
    ''' <b>apagar el bucle → tirar el host → limpiar el control → tirarlo</b>.
    '''
    ''' <para>⛔⛔ ACÁ FALTABAN TRES DE LOS CUATRO PASOS, y es lo que el usuario reportó cuatro veces
    ''' como «mezclas contextos al salir» y «el loading se ve chiquito». Este formulario sólo hacía
    ''' <c>_host.Dispose()</c>, y encima en <c>FormClosed</c> — o sea: el <b>bucle de render seguía
    ''' corriendo</b> cuando se tiraba el host que ese bucle usa, y el <c>PreviewControl</c> con su
    ''' contexto de GL quedaba VIVO y sin limpiar después de que la ventana se fue. El preview
    ''' principal se rearma sobre eso.</para>
    '''
    ''' <para>⛔ Y va en <c>FormClosing</c>, no en <c>FormClosed</c>: en <c>FormClosed</c> la ventana ya
    ''' no está y el control puede haberse destruido antes de que nadie lo limpie.</para></summary>
    Private Sub HeadPartEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        ' Va primero, por lo mismo que en ArmoEditor_Form: la reversión no debe depender de por qué se
        ' cerró. Cerrar con la X o con Alt+F4 es un abandono.
        If DialogResult <> DialogResult.OK Then _toma.Abandonar()

        ' El temporizador es de ESTE formulario y se para ANTES que nada: un tic después del apagado
        ' pediría un render sobre un host que ya no está.
        Try
            If _previewDebounce IsNot Nothing Then
                RemoveHandler _previewDebounce.Tick, AddressOf OnTicDelPreview
                _previewDebounce.Stop()
                _previewDebounce.Dispose()
                _previewDebounce = Nothing
            End If
        Catch
        End Try

        ' Los CUATRO pasos, en este orden. Ver el ⛔⛔ de arriba.
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.BeginTeardown()
            Catch
            End Try
        End If
        If _host IsNot Nothing Then
            Try
                _host.Dispose()
            Catch
            End Try
            _host = Nothing
        End If
        If _preview IsNot Nothing AndAlso Not _preview.IsDisposed Then
            Try
                _preview.Clean()
            Catch
            End Try
            Try
                _preview.Dispose()
            Catch
            End Try
        End If
    End Sub

    '==============================================================================================
    ' Helpers de presentación
    '==============================================================================================

    Private Sub ActualizarBanner()
        If _draft Is Nothing Then
            LabelStatusBanner.Text = "No target — pick one above, or create a new record."
            Return
        End If
        ' ⛔ TRES estados, como el de ARMO/ARMA. Acá había DOS y decía «NEW record (source: —)»
        ' mientras el usuario REEDITABA un borrador que ya había hecho — que es justo la pregunta que
        ' este banner existe para contestar: ¿estoy creando uno nuevo o tocando el mío?
        Dim edid = If(String.IsNullOrEmpty(_draft.Record?.EditorID), "(no EditorID yet)", _draft.Record.EditorID)
        If _draft.IsOverride Then
            Dim plug = NombreDelPlugin(_draft.FormID)
            Dim cola = If(String.IsNullOrEmpty(plug), "", $" · {plug} → your plugin replaces it")
            LabelStatusBanner.Text = If(_editandoBorradorExistente,
                $"Editing your draft — {edid} (override of 0x{_draft.FormID:X8}){cola}",
                $"OVERRIDE — {edid} [0x{_draft.FormID:X8}]{cola}")
        Else
            LabelStatusBanner.Text = If(_editandoBorradorExistente,
                $"Editing your draft — {edid} (new record, not saved yet)",
                $"NEW record — {edid} (gets a real FormID when you save)")
        End If
    End Sub

    Private Function NombreDelPlugin(fid As UInteger) As String
        If fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return "(new)"
        Dim rec = _plugins.GetRecord(fid)
        Return If(rec Is Nothing, "(not in load order)", If(rec.SourcePluginName, ""))
    End Function

    Private Sub PonerFidEn(tb As TextBox, fid As UInteger)
        tb.Tag = fid
        tb.Text = If(fid = 0UI, "(none)", $"{_mainForm.GetRecordDisplayNameForEditor(fid)}  [0x{fid:X8}]")
    End Sub

    ''' <summary>Escribe un campo de FormID, o QUITA su subrecord si el campo quedó vacío.
    ''' <para>⛔ Los tres casos, y los tres importan: vacío y no estaba ⇒ no se toca (no se crea un
    ''' subrecord con referencia nula); vacío y estaba ⇒ se QUITA; con valor ⇒ se escribe. Escribir 0
    ''' sin más es lo que le metió `TNAM`/`CNAM` nulos a un override de un record vanilla.</para></summary>
    ''' <summary>La ley de <c>Borradores.EscribirCampoOQuitar</c> para una caja de FormID. Vive
    ''' acá sólo para no repetir el <c>FidDe(tb)</c> en los cuatro llamadores; la DECISIÓN es de la
    ''' sede.</summary>
    Private Sub EscribirFidOQuitar(tb As TextBox, escribir As Action(Of UInteger), quitar As Action)
        Dim v = FidDe(tb)
        Borradores.EscribirCampoOQuitar(v <> 0UI, Sub() escribir(v), quitar)
    End Sub

    Private Function FidDe(tb As TextBox) As UInteger
        If tb.Tag Is Nothing Then Return 0UI
        Return CUInt(tb.Tag)
    End Function

    ''' <summary>Pone el tipo en el combo SIN normalizarlo. Si el valor no está en el enum, el combo
    ''' recibe UNA entrada extra con el valor crudo y queda elegida — la lista sigue cerrada y el record
    ''' no se reescribe. Ver el ⛔ de la clase (los cinco HDPT con 69/71).
    ''' <para>La entrada extra se RETIRA antes de agregar otra: sin eso, abrir un 69 y después un 71 en
    ''' la misma sesión del editor —que es lo que pasa recorriendo <c>UBE_AllRace.esp</c>— deja las dos en
    ''' la lista y la de arriba deja de corresponder a ningún record.</para></summary>
    Private Sub PonerTipoEnElCombo(valor As UInteger)
        If _hayEntradaDeTipoCruda AndAlso ComboType.Items.Count > _clavesDeTipo.Count Then
            ComboType.Items.RemoveAt(ComboType.Items.Count - 1)
            _hayEntradaDeTipoCruda = False
        End If
        Dim idx = _clavesDeTipo.IndexOf(valor)
        If idx >= 0 Then
            ComboType.SelectedIndex = idx
            LabelTypeRaw.Text = ""
            Return
        End If
        _tipoCrudoFueraDelEnum = valor
        ComboType.Items.Add($"{valor} — (not named by this game's enum; kept as-is)")
        _hayEntradaDeTipoCruda = True
        ComboType.SelectedIndex = ComboType.Items.Count - 1
        LabelTypeRaw.Text = $"(raw {valor} — not named by this game's enum; kept as-is)"
    End Sub

    ''' <summary>El valor sale del ÍNDICE elegido, no del rótulo: la entrada extra no tiene por qué
    ''' empezar con su número, y parsear texto de la UI para recuperar un byte del record es una sede de
    ''' más. <c>False</c> sólo si no hay nada elegido.</summary>
    Private Function TipoDelCombo(ByRef valor As UInteger) As Boolean
        Dim i = ComboType.SelectedIndex
        If i < 0 Then Return False
        If i < _clavesDeTipo.Count Then
            valor = _clavesDeTipo(i)
            Return True
        End If
        valor = _tipoCrudoFueraDelEnum
        Return True
    End Function

End Class
