Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Inline sub-editor for a Material Swap (MSWP) draft, opened from the ARMA/ARMO Editor's
''' "New / Edit MSWP…" button for a given gender. The substitutions grid is now pure READ-ONLY: each row is
''' authored in the modal <see cref="MswpSubEntryEditor_Form"/> (Original from THAT GENDER'S mesh NIF materials
''' + free-typed fallback, Replacement typed or picked, optional Color Remap). This kills the reentrant
''' <c>SetCurrentCellAddressCore</c> crash the old inline combo/text cells caused.
'''
''' A working list (<see cref="_subs"/>) is the source of truth: populated from the draft on open, mutated by
''' the Add / Edit / Remove buttons (and the double-click modal), and flushed back into the passed-in
''' <see cref="MswpDraft"/> on OK. The editor does NOT touch the ESP; the MswpDraft is persisted by the
''' existing Save flow when an ARMA/ARMO draft references it.</summary>
Public Class MswpSubEditor_Form
    Implements BorradoDeBorradores.IDuenoDeBorradores

    Private ReadOnly _mainForm As MainForm
    ''' <summary>El borrador que se está editando. ⛔ DEJÓ DE SER `ReadOnly` porque ahora el editor
    ''' tiene sus cuatro puertas de objetivo y puede CAMBIARLO. Por eso también existe
    ''' <see cref="ResultMswpFormID"/>: los llamadores leían el FormID del objeto que habían pasado, y
    ''' con cambio de objetivo eso escribiría el FormID VIEJO en su campo.</summary>
    Private _draft As MswpDraft
    ''' <summary>La LÍNEA DE BASE PRISTINA del swap: el record tal como está en el archivo. Es contra ESTO
    ''' que se decide si el borrador quedó sucio — ver <see cref="Borradores.SucioContraLaBase"/>.
    ''' <para>⛔ Antes acá había un clon del borrador AL ABRIR, y esa pregunta —«¿cambió algo desde que
    ''' abrí?»— dejaba LIMPIO un swap que ya venía editado de una sesión anterior: aceptarlo sin retocar
    ''' apagaba <c>IsModified</c>, el saver lo salteaba y las sustituciones del usuario no llegaban al
    ''' .esp mientras el render las seguía aplicando.</para>
    ''' <para>Nothing para un borrador NUEVO (su FormID es provisional, no hay record que leer) o si
    ''' construirla falla ⇒ SUCIO, que es la dirección segura.</para></summary>
    Private _base As MswpDraft

    ''' <summary>LA TOMA. ⛔⛔ ESTE EDITOR ERA EL ÚNICO DE LOS CUATRO SIN ELLA, y esa ausencia
    ''' costaba DOS defectos de bytes en cuanto ganó las puertas de creación:
    ''' <list type="number">
    ''' <item>el objetivo nuevo no quedaba REGISTRADO — «New (blank)» y «New from template…»
    ''' construían un borrador que el registro no tenía, así que el llamador hacía
    ''' <c>TryGetMswpDraft</c> → Nothing, re-registraba el VIEJO y escribía el fid NUEVO en su campo:
    ''' el swap recién compuesto se perdía entero y el <c>0xFF</c> sin dueño llegaba al .esp;</item>
    ''' <item>y el borrador que el llamador crea ANTES de abrir el modal quedaba registrado al
    ''' aceptar sobre OTRO objetivo — <c>IsNew ⇒ IsDirty</c> y la fase 2g emite TODO borrador sucio,
    ''' referenciado o no.</item></list>
    ''' <para>⛔ Y no alcanzaba con agregar un <c>Register</c>: eso canjea un defecto por otro — abrir,
    ''' apretar «New (blank)» tres veces y CANCELAR dejaría tres borradores vivos, y el llamador no
    ''' puede bajarlos (su rama de Cancel baja el que creó ÉL, y su comentario explica por qué no
    ''' puede bajar otro). La ley ya estaba escrita en el árbol: <b>registrar NO es inocuo</b>.</para>
    ''' <para>Con la toma, <c>QueHacerAlAbandonar</c> da de baja lo que ESTE editor registró y NO toca
    ''' lo ajeno, que es exactamente la distinción que hacía falta.</para></summary>
    Private _toma As TomaDeBorrador(Of MswpDraft)
    ''' <summary>Material paths the gender mesh NIF references (BaseMaterials). Seeds the Original combo in the
    ''' per-substitution modal. Empty when no mesh path was supplied or the mesh couldn't be loaded.</summary>
    Private ReadOnly _meshMaterials As New List(Of String)
    ''' <summary>Working list of substitutions (source of truth). Loaded from the draft, mutated by the buttons/
    ''' modal, flushed back into the draft on OK. Never aliased to the draft's own list (copied in and out).</summary>
    Private ReadOnly _subs As New List(Of Canon.SustitucionEditable)
    ''' <summary>Fixed type prefix for MSWP base EditorIDs ("npcm_MSWP_"). Save injects the &lt;plugin&gt; segment.</summary>
    Private ReadOnly _edidPrefix As String = MswpDraft.EditorIdPrefix

    ''' <summary>EL FormID QUE EL LLAMADOR TIENE QUE ESCRIBIR EN SU CAMPO tras un OK.
    ''' <para>⛔⛔ EXISTE PORQUE LOS TRES LLAMADORES LEÍAN `draft.FormID` DEL OBJETO QUE HABÍAN PASADO
    ''' (<c>ArmoEditor_Form</c>, <c>ArmaEditor_Form</c>, <c>HeadPartEditor_Form</c>). Mientras este editor
    ''' no podía cambiar de objetivo eso era correcto; con las cuatro puertas puestas, el usuario puede
    ''' entrar con un swap y salir con OTRO, y el llamador escribiría el FormID VIEJO — un campo
    ''' apuntando a un record que no es el que el usuario aceptó. Es la misma forma que
    ''' <c>TextureSetEditor_Form.ResultTxstFormID</c> y <c>FormListEditor_Form.ResultFlstFormID</c> ya
    ''' tenían; este editor era el único de los tres sin ella.</para></summary>
    Public ReadOnly Property ResultMswpFormID As UInteger
        Get
            Return If(_draft Is Nothing, 0UI, _draft.FormID)
        End Get
    End Property

    ''' <param name="mainForm">Owner — used for EditorID uniqueness checks.</param>
    ''' <param name="draft">The MSWP draft being authored (already registered on MainForm). Flushed on OK.</param>
    ''' <param name="genderMeshPath">The gender's MOD2 (male) / MOD3 (female) mesh path. Its NIF materials
    ''' seed the Original combo in the modal. Empty → free-text Original only.</param>
    ''' <param name="genderLabel">"Male"/"Female", shown in the caption.</param>
    ''' <param name="extraMeshPaths">Optional additional mesh paths whose NIF materials are ALSO merged into the
    ''' Original-Material list (deduped by material path). Used by the ARMO editor to seed the list from every
    ''' included ARMA addon mesh in addition to the ARMO's own gender world-model mesh. Null → gender mesh only.</param>
    Public Sub New(mainForm As MainForm, draft As MswpDraft, genderMeshPath As String, genderLabel As String,
                   Optional extraMeshPaths As IEnumerable(Of String) = Nothing)
        InitializeComponent()
        _mainForm = mainForm
        _toma = New TomaDeBorrador(Of MswpDraft)(
            buscar:=AddressOf _mainForm.TryGetMswpDraft,
            registrar:=Sub(d) _mainForm.RegisterMswpDraft(d),
            bajar:=Sub(fid) _mainForm.UnregisterMswpDraft(fid),
            idDe:=Function(d) d.FormID,
            construirBase:=AddressOf ConstruirBaseDeDisco)
        Text = $"Material Swap (MSWP) — {genderLabel}"
        ' Original-Material list = the gender mesh's NIF materials PLUS any supplied extra meshes' materials
        ' (LoadMeshMaterials merges into the shared _meshMaterials list, dedups by material path, and tolerates
        ' null/empty/unloadable paths — so repeated calls are safe).
        LoadMeshMaterials(genderMeshPath)
        If extraMeshPaths IsNot Nothing Then
            For Each p In extraMeshPaths
                LoadMeshMaterials(p)
            Next
        End If
        BuildGridColumns()

        ' ⛔ EL OBJETIVO INICIAL ENTRA POR LA PUERTA ÚNICA, como en los otros tres editores: el ctor no
        ' arma `_draft`/`_base` a mano. `registrar:=False` porque el llamador YA lo registró.
        '
        ' ⛔⛔ ACÁ DECÍA QUE `QueHacerAlAbandonar` «tiene que ver que este borrador ya estaba y NO
        ' tocarlo». ES FALSO, y por creerlo se dio por cubierto un defecto de bytes. `Tomar` guarda
        ' `_registroPrevio = _buscar(fid)`, que es EL MISMO OBJETO que el llamador pasó, y la ley
        ' (`Borradores.QueHacerAlAbandonar`) devuelve `NoTocar` sólo cuando NO hay snapshot — acá lo hay,
        ' así que devuelve `Restaurar` y lo REPONE. El blanco del llamador sobrevive al cambio de
        ' objetivo, y quien lo tiene que bajar es el llamador, que es el único que sabe que es suyo.
        TomarYVolcar(draft, registrar:=False)

        AddHandler ButtonNewBlank.Click, AddressOf OnNuevoEnBlanco
        AddHandler ButtonNewFromTemplate.Click, AddressOf OnNuevoDesdePlantilla
        AddHandler ButtonOverrideExisting.Click, AddressOf OnOverrideExistente
        AddHandler ButtonEditMine.Click, AddressOf OnEditarElMio
        AddHandler TextBoxEdid.TextChanged, AddressOf OnEdidChanged
        AddHandler ButtonAddRow.Click, AddressOf OnAddSub
        AddHandler ButtonEditRow.Click, AddressOf OnEditSub
        AddHandler ButtonRemoveRow.Click, AddressOf OnRemoveSub
        AddHandler GridSubs.CellDoubleClick, AddressOf OnSubDoubleClick
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    '==============================================================================================
    ' LAS CUATRO PUERTAS DE OBJETIVO — el molde de HDPT / ARMO / ARMA, transcrito
    '
    ' ⛔ ANTES ESTE EDITOR NO TENÍA NINGUNA: el modo se INFERÍA de si el campo del llamador traía
    ' FormID cuando se apretó «New / Edit MSWP…». Vacío ⇒ nuevo, con valor ⇒ override. El usuario no
    ' podía pedir un override, ni arrancar de un swap que ya existe, ni volver a abrir uno propio sin
    ' pasar por el campo de otro editor.
    '
    ' ⛔ Y ESTE EDITOR YA USA `TomaDeBorrador`, como sus tres hermanos. No la tenía, y esa ausencia
    ' costaba dos defectos de BYTES en cuanto ganó las puertas: el objetivo nuevo sin registrar y el
    ' blanco del llamador filtrándose al .esp. Ver el ⛔ de `_toma`.
    '==============================================================================================

    ''' <summary>«New (blank)»: un MSWP nuevo y vacío.</summary>
    Private Sub OnNuevoEnBlanco(sender As Object, e As EventArgs)
        Dim d = MswpDraft.Nuevo(_mainForm.AllocateDraftFormID(), Canon.CanonBridge.SessionGame())
        If d Is Nothing Then Return
        d.Record.EditorID = MswpDraft.EditorIdPrefix & "new"
        ' ⛔ `registrar:=False`: un borrador NUEVO no se commitea al abrir. Registrar NO es inocuo — la
        ' fase 2g emite TODO borrador sucio —, así que probar esta puerta y cancelar no debe dejar nada.
        TomarYVolcar(d, registrar:=False)
    End Sub

    ''' <summary>«New from template…»: un MSWP NUEVO con las sustituciones de uno que ya existe.</summary>
    Private Sub OnNuevoDesdePlantilla(sender As Object, e As EventArgs)
        Dim fid = ElegirMswpReal("Pick the material swap to copy")
        If fid = 0UI Then Return
        Dim rec = _mainForm.PluginManagerForEditor.GetRecord(fid)
        If rec Is Nothing Then Return
        Dim d = MswpDraft.Clon(rec, _mainForm.PluginManagerForEditor, _mainForm.AllocateDraftFormID())
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that material swap.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d)
    End Sub

    ''' <summary>«Override existing…»: se edita un MSWP que YA existe, conservando su identidad.</summary>
    Private Sub OnOverrideExistente(sender As Object, e As EventArgs)
        Dim fid = ElegirMswpReal("Pick the material swap to override")
        If fid = 0UI Then Return
        CargarComoOverride(fid)
    End Sub

    ''' <summary>«Edit mine…»: los MSWP PROPIOS — los borradores de esta sesión Y los ya GUARDADOS.
    ''' <para>⛔ Los guardados salen de <see cref="BorradoDeBorradores.EntradasPropias"/>: sin ellos,
    ''' después de un Save el swap propio DESAPARECE de la lista, porque el remapeo de la promoción lo
    ''' saca del registro de borradores.</para></summary>
    Private Sub OnEditarElMio(sender As Object, e As EventArgs)
        Dim entradas = _mainForm.MswpDrafts().Where(Function(d) d?.Record IsNot Nothing).
            Select(Function(d) New FormIdPickerEntry With {
                .FormID = d.FormID, .EditorID = d.Record.EditorID, .DisplayName = d.Record.EditorID,
                .Signature = "MSWP", .PluginName = If(d.IsOverride, "(override)", "(new)")}).ToList()
        entradas.AddRange(BorradoDeBorradores.EntradasPropias(_mainForm, "MSWP", entradas))
        If entradas.Count = 0 Then
            MessageBox.Show(Me,
                            "No material swaps of yours yet — neither drafts in this session nor saved " &
                            "ones in your plugin. Use New, New from template… or Override existing… first.",
                            "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Using dlg As FormIdPicker_Form = FormIdPicker_Form.ParaBorradores(
                _mainForm, Me, {"MSWP"}, "Edit my material swap (drafts + saved)",
                0UI, False, entradas,
                formIdFilter:=Function(fid) entradas.Any(Function(x) x.FormID = fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If _draft IsNot Nothing AndAlso _draft.FormID = dlg.SelectedFormID Then
                MessageBox.Show(Me, "That is the material swap you are already editing in this window.",
                                "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
                Return
            End If
            CargarComoOverride(dlg.SelectedFormID)
        End Using
    End Sub

    ''' <summary>El selector de un MSWP REAL, que comparten «New from template…» y «Override
    ''' existing…». Devuelve 0 si se canceló.</summary>
    ''' <summary>Elegir un record REAL para «New from template…» / «Override existing…».
    ''' <para>⛔⛔ Y LOS PROPIOS YA GUARDADOS TAMBIÉN. Los cuatro <c>ElegirXxxReal</c> usaban el ctor
    ''' PLANO, que sólo lista el orden de carga: un record que el usuario creó con esta app y ya guardó
    ''' NO aparecía, así que no podía partir de su propio trabajo — ni para clonarlo ni para
    ''' override-arlo. El código que esta ola borró decía textualmente que eso era el defecto.</para>
    ''' <para>⛔ Van SIN dueño y ésa es la decisión: este diálogo no toma ningún borrador —devuelve un
    ''' FormID y se cierra—, así que no hay precondición ni postcondición que cumplir. Las filas son
    ''' records REALES, no borradores, pero igual pasan por la fábrica: el botón «Delete / Revert…» se
    ''' habilita sólo sobre las filas que el llamador pasa, y un record propio guardado SÍ tiene salida
    ''' (<c>Gesto.QuitarGuardado</c>).</para></summary>
    Private Function ElegirMswpReal(titulo As String) As UInteger
        Dim propias = BorradoDeBorradores.EntradasPropias(_mainForm, "MSWP", Nothing)
        ' ⛔⛔ CON DUEÑO. Este editor TIENE `_toma` y ya implementa el contrato, así que la condición de
        ' `ParaBorradoresSinDueno` —«el sitio no tiene ningún borrador tomado»— NO se cumple. Es el mismo
        ' error que esta ola le corrigió a `ArmaEditor_Form.PickTxstInto`, y acá pesa MÁS porque las clases
        ' COINCIDEN: `EntradasPropias` puede listar el record que este editor tiene tomado, y sin dueño
        ' `Planear` no sabe que lo tomó ESTA ventana ⇒ contesta «already open in another editor window»
        ' —mentira, es ésta— y niega un gesto que la ley concede («tomado por MÍ se permite»).
        Using dlg As FormIdPicker_Form = If(propias.Count = 0,
                New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"MSWP"}, titulo, 0UI, allowNull:=False),
                FormIdPicker_Form.ParaBorradores(_mainForm, Me, {"MSWP"}, titulo, 0UI, False, propias))
            If dlg.ShowDialog(Me) <> DialogResult.OK Then Return 0UI
            Return dlg.SelectedFormID
        End Using
    End Function

    ''' <summary>Tomar <paramref name="fid"/> como override. ⛔ Si YA hay borrador bajo ese FormID, el
    ''' BORRADOR MANDA sobre el disco: reconstruirlo tiraría lo que el usuario editó en esta sesión.</summary>
    Private Sub CargarComoOverride(fid As UInteger)
        Dim ya = _mainForm.TryGetMswpDraft(fid)
        If ya IsNot Nothing Then
            TomarYVolcar(ya)
            Return
        End If
        Dim d = _mainForm.BuildMswpOverrideDraftFromReal(fid)
        If d Is Nothing Then
            MessageBox.Show(Me, "Could not parse that material swap.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Error)
            Return
        End If
        TomarYVolcar(d)
    End Sub

    ''' <summary>LA PUERTA ÚNICA de cambio de objetivo — la misma que tienen los otros tres editores.
    ''' <para>⛔⛔ ABANDONA LA TOMA ANTERIOR, y eso es lo que cierra los dos defectos de bytes que la
    ''' ausencia de este protocolo causaba: <c>QueHacerAlAbandonar</c> da de baja lo que ESTE editor
    ''' registró (el blanco que se probó y se dejó) y NO toca lo ajeno (el que el llamador creó, o uno
    ''' que ya existía). Sin esto, cada vuelta por una puerta dejaba un borrador vivo y sucio, y la
    ''' fase 2g emite TODO borrador sucio, referenciado o no.</para>
    ''' <para>⛔ LA GUARDA VA ANTES DEL ABANDONO. <c>TomaDeBorrador.Tomar</c> lo declara: soltar primero
    ''' y rechazar después deja al editor sin su propio borrador, ya dado de baja.</para>
    ''' <para>⛔ LA BASE SE RECALCULA, no se arrastra: es contra ELLA que se decide si el borrador quedó
    ''' sucio, y la del objetivo anterior marcaría sucio un swap que el usuario no tocó — o peor, limpio
    ''' uno que sí.</para></summary>
    ''' <param name="registrar">False para un NUEVO EN BLANCO: se toma y se vuelca, pero NO se
    ''' commitea, así que no queda registrado hasta que el usuario escriba algo.</param>
    Private Sub TomarYVolcar(d As MswpDraft, Optional registrar As Boolean = True)
        If d Is Nothing OrElse d.Record Is Nothing Then Return
        ' ⛔ ACÁ HABÍA UNA COPIA DE LA GUARDA, y sobra desde que `CambiarObjetivo` pregunta ANTES de
        ' abandonar. Era la ÚNICA de los cinco editores que la tenía —o sea, la ley tenía dos dueños y
        ' cuatro ventanas sin protección—, y se escribió como parche porque la puerta única preguntaba
        ' tarde. Arreglada la puerta, la copia es la que se va.
        Dim snap As MswpDraft = Nothing
        Try
            snap = d.Clone()
        Catch
        End Try
        ' ⛔⛔ rev-37 · EL CAMBIO DE OBJETIVO ES DESTRUCTIVO Y ANTES NO AVISABA: la ley y su
        ' porqué están en `TomaDeBorrador.CambiarDestruyeTrabajo`. El predicado vive allá para que
        ' un testigo lo corra; el cartel, en `BorradoDeBorradores`, para que los cinco editores
        ' digan lo mismo.
        If _toma.CambiarDestruyeTrabajo(d, _draft IsNot Nothing AndAlso _draft.IsDirty) AndAlso
           Not BorradoDeBorradores.ConfirmarCambioDeObjetivo(Me, "material swap", "MSWP") Then Return
        ' ⛔ LA PUERTA ÚNICA, Y ES MEDIBLE: abandonar + tomar + registrar viven en
        ' `TomaDeBorrador.CambiarObjetivo`, afuera del formulario, porque adentro ningún testigo
        ' puede correrla — cada OK abre validaciones con `MessageBox` y un modal cuelga al gate.
        ' Mismo movimiento que `PlanDeCierreDeListas` y `FilasPropiasDeValue1`.
        If Not _toma.CambiarObjetivo(d, snap, registrar) Then
            MessageBox.Show(Me, "That material swap is already open in another editor window. Close it first.",
                            "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        _draft = d
        _base = ConstruirBaseDeDisco(If(d.IsNew, 0UI, d.FormID))
        RefreshEditorIdField()
        LoadSubsFromDraft()
        RefreshGrid()
        ActualizarBanner()
    End Sub

    ''' <summary>El banner dice el MODO, que es el dato más importante de la ventana.</summary>
    Private Sub ActualizarBanner()
        If _draft Is Nothing Then
            LabelBanner.Text = "No target — use New, New from template… or Override existing…"
        Else
            LabelBanner.Text = If(_draft.IsOverride, $"OVERRIDE of 0x{_draft.FormID:X8}", "NEW record")
        End If
    End Sub

    '==============================================================================================
    ' EL CONTRATO CON LA SEDE DE BAJA — `BorradoDeBorradores.IDuenoDeBorradores`
    '==============================================================================================

    ''' <summary>El FormID que este editor tiene TOMADO. Alimenta la precondición de `Planear`.</summary>
    Private Function FormIdTomado() As UInteger Implements BorradoDeBorradores.IDuenoDeBorradores.FormIdTomado
        Return If(_draft Is Nothing, 0UI, _draft.FormID)
    End Function

    ''' <summary>El buffer de sustituciones (<c>_subs</c>) NO lleva referencias a otros borradores: son
    ''' rutas de material, texto. No hay nada que declarar acá.</summary>
    Private Function ReferenciasNoVolcadas(formID As UInteger) As IEnumerable(Of String) _
            Implements BorradoDeBorradores.IDuenoDeBorradores.ReferenciasNoVolcadas
        Return Enumerable.Empty(Of String)()
    End Function

    ''' <summary>POSTCONDICIÓN: si dieron de baja el swap que esta ventana tiene abierto, el editor se
    ''' queda SIN OBJETIVO y el banner lo dice. Aceptar con un borrador recién borrado lo re-registraría
    ''' — el borrado se desharía solo, que es el defecto que el editor de head parts documenta con el
    ''' reporte del usuario «si hago delete/revert se duplica».</summary>
    Private Sub TrasLaBaja(formID As UInteger) Implements BorradoDeBorradores.IDuenoDeBorradores.TrasLaBaja
        If _draft Is Nothing OrElse _draft.FormID <> formID Then Return
        ' ⛔ SOLTAR, no abandonar: abandonar REPONDRÍA lo que el usuario acaba de borrar, por encima
        ' del `MarkRecordForRemoval` del mismo gesto. Los dos métodos existen por esa diferencia.
        _toma.Soltar()
        _draft = Nothing
        _base = Nothing
        _subs.Clear()
        RefreshEditorIdField()
        RefreshGrid()
        ActualizarBanner()
    End Sub

    ''' <summary>LA FÁBRICA de la línea de base: el record del archivo, copiado. La usan los DOS
    ''' lados — el volcado y <see cref="TomaDeBorrador(Of TD)"/> — porque con dos construcciones
    ''' distintas el record abre SUCIO sin que el usuario lo toque.</summary>
    Private Function ConstruirBaseDeDisco(fid As UInteger) As MswpDraft
        If fid = 0UI OrElse Borradores.EsFormIdDeBorrador(fid) Then Return Nothing
        Try
            Return MswpDraft.Edicion(_mainForm.PluginManagerForEditor?.GetRecord(fid),
                                     _mainForm.PluginManagerForEditor)
        Catch ex As Exception
            ' ⛔ Con Try: construir la base parsea y copia un record del disco y eso puede tirar; esto
            ' corre desde un manejador de clic sin Try y la app usa `UnhandledExceptionMode.ThrowException`.
            ' Sin base, el volcado marca SUCIO — la dirección SEGURA: un override de más es ruido; un
            ' cambio perdido es daño.
            Logger.Log("MswpSubEditor (línea de base): " & ex.ToString())
            Return Nothing
        End Try
    End Function

    ''' <summary>Drive the shared EditorID field: a NEW draft edits only the &lt;name&gt; (fixed prefix + live
    ''' "Saves as:" preview); an OVERRIDE draft keeps its record EDID read-only. A null draft behaves as NEW/empty.</summary>
    Private Sub RefreshEditorIdField()
        If _draft Is Nothing Then
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview, _edidPrefix, "")
        ElseIf _draft.IsNew Then
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview, _edidPrefix, _draft.Record.EditorID)
        Else
            EditorIdField.ConfigureOverride(LabelEdid, TextBoxEdid, LabelEdidPreview, _draft.Record.EditorID)
        End If
    End Sub

    ''' <summary>Keep the live "Saves as:" preview in sync with the name box (only while the box is editable, i.e.
    ''' a NEW draft; an OVERRIDE keeps the box disabled and the preview hidden).</summary>
    Private Sub OnEdidChanged(sender As Object, e As EventArgs)
        If TextBoxEdid.Enabled Then EditorIdField.UpdatePreview(LabelEdidPreview, _edidPrefix, TextBoxEdid.Text)
        ' ⛔ Y SE VUELCA, como en los otros cuatro editores: el nombre es parte del record y sin esto
        ' `IsDirty` no lo ve. Ver el ⛔ de `CommitVivo`.
        CommitVivo()
    End Sub

    ''' <summary>Load the BaseMaterials (referenced material paths) of the gender mesh into
    ''' <see cref="_meshMaterials"/>. Resolves the mesh via FilesDictionary (loose &gt; BA2). Tolerant of a
    ''' missing/unparseable mesh (leaves the list empty → free-text Original in the modal).</summary>
    Private Sub LoadMeshMaterials(genderMeshPath As String)
        If String.IsNullOrWhiteSpace(genderMeshPath) Then Return
        ' Records store mesh paths RELATIVE to Meshes\ (prefix-free); NormalizeMeshKey re-adds the lowercase
        ' "meshes\" prefix + strips build-machine absolute prefixes so TryLoadMeshBytes (loose > BA2) resolves.
        Dim key As String = MeshPathHelpers.NormalizeMeshKey(genderMeshPath)
        Try
            Dim bytes = MeshPathHelpers.TryLoadMeshBytes(key)
            If bytes Is Nothing Then
                Logger.LogLazy(Function() $"[MSWP-MAT] mesh not found for '{genderMeshPath}' (resolved key '{key}') — Original-Material list will be empty (free-text fallback).")
                Return
            End If
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            For Each m In nif.BaseMaterials.Values
                If m IsNot Nothing AndAlso Not String.IsNullOrEmpty(m.path) AndAlso Not _meshMaterials.Contains(m.path) Then
                    _meshMaterials.Add(m.path)
                End If
            Next
        Catch ex As Exception
            Logger.LogLazy(Function() $"[MSWP-MAT] mesh material load failed for '{genderMeshPath}' (resolved key '{key}'): {ex.GetType().Name}: {ex.Message}")
        End Try

        If _meshMaterials.Count = 0 Then
            Logger.LogLazy(Function() $"[MSWP-MAT] no NIF materials for '{genderMeshPath}' (resolved key '{key}') — Original-Material list empty (free-text fallback).")
        End If
    End Sub

    ''' <summary>Build the 3 READ-ONLY grid columns. No combo/text editable cells — the row is edited in the
    ''' modal <see cref="MswpSubEntryEditor_Form"/>, so a not-listed / empty Original can never surface the
    ''' default DataGridView error dialog.</summary>
    Private Sub BuildGridColumns()
        GridSubs.AutoGenerateColumns = False
        GridSubs.Columns.Clear()
        GridSubs.Columns.Add(NewReadOnlyCol("Original Material (BNAM)", 42))
        GridSubs.Columns.Add(NewReadOnlyCol("Replacement Material (SNAM)", 42))
        GridSubs.Columns.Add(NewReadOnlyCol("Color Remap", 16))
    End Sub

    Private Shared Function NewReadOnlyCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    Private Sub LoadSubsFromDraft()
        _subs.Clear()
        If _draft Is Nothing Then Return
        ' Buffer de edicion: el usuario reordena, agrega y borra sobre esta lista, y al aceptar el
        ' record se rehace desde ella. El record sigue siendo lo unico que se guarda.
        For Each e In _draft.Record.MaterialSubstitutions
            _subs.Add(New Canon.SustitucionEditable(e))
        Next
    End Sub

    ''' <summary>Repaint the grid from <see cref="_subs"/> (read-only summary rows). Called only from load /
    ''' button handlers — NEVER from a cell event, so no reentrant Rows.Clear.</summary>
    Private Sub RefreshGrid()
        Dim selIdx = If(GridSubs.CurrentRow IsNot Nothing, GridSubs.CurrentRow.Index, -1)
        GridSubs.Rows.Clear()
        For Each s In _subs
            Dim remap = If(s.TieneIndiceDeColor, s.IndiceDeColor.ToString(CultureInfo.InvariantCulture), "")
            GridSubs.Rows.Add(If(s.MaterialOriginal, ""), If(s.MaterialReemplazo, ""), remap)
        Next
        If selIdx >= 0 AndAlso selIdx < GridSubs.Rows.Count Then
            GridSubs.Rows(selIdx).Selected = True
            GridSubs.CurrentCell = GridSubs.Rows(selIdx).Cells(0)
        End If
    End Sub

    ''' <summary>Add → open the modal on a fresh substitution; on OK append the returned copy.</summary>
    Private Sub OnAddSub(sender As Object, e As EventArgs)
        Using dlg As New MswpSubEntryEditor_Form(_meshMaterials, New Canon.SustitucionEditable())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultSub IsNot Nothing Then
                _subs.Add(dlg.ResultSub)
                RefreshGrid()
                CommitVivo()
            End If
        End Using
    End Sub

    Private Sub OnEditSub(sender As Object, e As EventArgs)
        EditSubAt(SelectedSubIndex())
    End Sub

    ''' <summary>Double-click a row → edit that substitution in the modal. Safe: the grid is read-only ⇒ no cell
    ''' in edit mode ⇒ no reentrant <c>SetCurrentCellAddressCore</c>.</summary>
    Private Sub OnSubDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditSubAt(e.RowIndex)
    End Sub

    Private Sub EditSubAt(i As Integer)
        If i < 0 OrElse i >= _subs.Count Then Return
        Using dlg As New MswpSubEntryEditor_Form(_meshMaterials, _subs(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultSub IsNot Nothing Then
                _subs(i) = dlg.ResultSub
                RefreshGrid()
                CommitVivo()
            End If
        End Using
    End Sub

    Private Sub OnRemoveSub(sender As Object, e As EventArgs)
        Dim i = SelectedSubIndex()
        If i < 0 Then Return
        _subs.RemoveAt(i)
        RefreshGrid()
        CommitVivo()
    End Sub

    Private Function SelectedSubIndex() As Integer
        If GridSubs.CurrentRow Is Nothing Then Return -1
        Dim i = GridSubs.CurrentRow.Index
        If i < 0 OrElse i >= _subs.Count Then Return -1
        Return i
    End Function

    ''' <summary>Commit the EditorID + working list into the draft. Validates the EditorID (non-empty + unique,
    ''' unless unchanged on the same draft) and that at least one usable substitution exists. Vetoes the close
    ''' (DialogResult.None) on a validation failure.</summary>
    ''' <summary>VOLCAR EL BUFFER AL RECORD Y DEJAR LA SUCIEDAD ESCRITA — el <c>Commit</c> que este
    ''' editor no tenía.
    ''' <para>⛔⛔ Los otros cuatro editores corren el suyo en CADA cambio, y por eso su estado de
    ''' sucio se puede leer desde afuera. Acá el cálculo vivía SÓLO adentro de <c>OnOk</c>, así que
    ''' durante toda la sesión un OVERRIDE valía <c>IsDirty = False</c> por más sustituciones que el
    ''' usuario compusiera: el cartel de «vas a perder lo que editaste» era un NO-OP justo en el editor
    ''' cuyo trabajo vive en un buffer (<c>_subs</c>), y cambiar de objetivo se lo llevaba en silencio.</para>
    ''' <para>⛔ La ley es la MISMA —<c>Borradores.SucioContraLaBase</c> contra la BASE— y el cálculo es
    ''' el que ya estaba escrito en <c>OnOk</c>: se saca de ahí, no se inventa otro.</para></summary>
    Private Sub CommitVivo()
        If _draft Is Nothing OrElse _draft.Record Is Nothing Then Return
        Dim subs = _subs.Where(Function(x) Not (String.IsNullOrEmpty(x.MaterialOriginal) AndAlso
                                                String.IsNullOrEmpty(x.MaterialReemplazo))).ToList()
        Try
            _draft.Record.ReemplazarSustituciones(subs)
            If Not _draft.IsNew Then
                _draft.IsModified = Borradores.SucioContraLaBase(_base IsNot Nothing,
                                                                 _draft.ContentEquals(_base))
            End If
        Catch ex As Exception
            ' Dirección SEGURA, la misma que el ctor: un override de más es ruido; un cambio
            ' perdido es daño.
            Logger.Log("MswpSubEditor.CommitVivo: " & ex.ToString())
            _draft.IsModified = True
        End Try
    End Sub

    ''' <summary>QUÉ HACE EL LLAMADOR CON EL RESULTADO: qué FormID va al campo (<c>Poner = 0</c> ⇒ no
    ''' se toca nada) y cuál hay que DAR DE BAJA (<c>Bajar = 0</c> ⇒ ninguno).
    ''' <para>⛔⛔ ES LA MITAD DEL LLAMADOR DONDE VIVÍAN LOS DOS DEFECTOS DE BYTES, y por eso está acá
    ''' afuera y no adentro del <c>Using</c> de cada editor — mismo movimiento que
    ''' <c>TomaDeBorrador.CambiarObjetivo</c>: adentro del formulario ningún testigo la corre, porque
    ''' construir un `ArmoEditor_Form` pide un NPC del corpus y levanta el host de render.</para>
    ''' <list type="number">
    ''' <item><b>Resultado 0 NO es «usá el que te pasé».</b> Es «no hay objetivo», y sólo pasa cuando el
    ''' usuario BORRÓ el swap desde el selector (<c>TrasLaBaja</c> deja <c>_draft = Nothing</c>). El
    ''' centinela viejo caía al FormID recién borrado, lo re-registraba y se lo apuntaba al campo: el
    ''' «Delete / Revert…» se deshacía solo.</item>
    ''' <item><b>El blanco que el llamador creó se BAJA si el usuario salió con otro.</b> El protocolo
    ''' de <c>TomaDeBorrador</c> no lo cubre: <c>Tomar</c> guardó <c>_registroPrevio = _buscar(fid)</c>,
    ''' que es EL MISMO OBJETO, así que <c>QueHacerAlAbandonar</c> cae en <c>Restaurar</c> y lo REPONE.
    ''' Y todo borrador <c>IsNew</c> es <c>IsDirty</c> ⇒ la fase 2g lo emite.</item>
    ''' <item><b>Y NUNCA se baja el que el usuario ACEPTÓ</b>, aunque sea el mismo que le pasamos.</item>
    ''' </list></summary>
    Friend Shared Function ResultadoDelModal(resultado As UInteger, borradorPropio As UInteger,
                                             loCreamosNosotros As Boolean) As (Poner As UInteger, Bajar As UInteger)
        If resultado = 0UI Then Return (0UI, 0UI)
        If loCreamosNosotros AndAlso resultado <> borradorPropio Then Return (resultado, borradorPropio)
        Return (resultado, 0UI)
    End Function

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' ⛔⛔ SIN OBJETIVO, EL OK ES UN CANCEL, y acá devolvía OK — era el ÚNICO de los cuatro
        ' editores que lo hacía (`TextureSetEditor_Form.OnOk`, `FormListEditor_Form.OnOk` y
        ' `LeveledListEditor_Form.OnOk` ya ponen Cancel).
        '    `_draft` sólo llega a Nothing por `TrasLaBaja`, o sea porque el usuario acaba de BORRAR
        ' ese swap desde «Delete / Revert…». Con OK, `ResultMswpFormID` devuelve 0, el llamador caía a
        ' «usá el FormID que te pasé» —el que se acaba de borrar—, lo RE-REGISTRABA y se lo apuntaba al
        ' campo: el borrado se deshacía solo. Es el defecto «si hago delete/revert se duplica» que esta
        ' casa documenta tres veces como motivo de `Soltar` vs `Abandonar`, entrando por la otra puerta.
        '    ⛔ Y peor en la rama override: `Ejecutar` ya corrió `MarkRecordForRemoval` +
        ' `RevertAppOverrideInMemory`, así que el mismo FormID quedaba A LA VEZ en `_recordsToRemove` y
        ' como borrador sucio registrado — la fase 2a lo deja caer y la 2g lo emite.
        If _draft Is Nothing Then
            DialogResult = DialogResult.Cancel
            Close()
            Return
        End If

        ' NEW draft: the box holds only the <name>; compose the stored base EDID (Save injects <plugin>). OVERRIDE:
        ' the box holds the kept EDID verbatim (read-only). A NEW draft must still supply a non-empty name.
        Dim edid = If(_draft IsNot Nothing AndAlso Not _draft.IsNew,
                      TextBoxEdid.Text.Trim(),
                      EditorIdField.Compose(_edidPrefix, TextBoxEdid.Text))
        If TextBoxEdid.Text.Trim().Length = 0 Then
            MessageBox.Show(Me, "Enter an EditorID for the material swap.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If
        ' El EditorID propio no cuenta como tomado: se exceptúa por IDENTIDAD, no por texto (ver
        ' `MainForm.IsRecordEditorIdAvailable`).
        ' ⛔ Y SÓLO PARA LOS NUEVOS. A diferencia de ARMO/ARMA, acá el chequeo también alcanzaba a los
        ' OVERRIDE, cuya caja está deshabilitada y cuyo EditorID ES el del record REAL —que
        ' `IsOutfitEditorIdAvailable` reporta como tomado, porque está en `AllRecords`—. Hasta ahora los
        ' salvaba el atajo por texto; con la identidad sola, TODO override de MSWP se rechazaría al
        ' aceptar. Un override no elige nombre: no hay nada que validar.
        If _draft.IsNew AndAlso Not _mainForm.IsRecordEditorIdAvailable(edid, _draft) Then
            MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If

        ' Drop any content-less rows (no Original AND no Replacement) defensively — the modal already rejects them.
        Dim subs = _subs.Where(Function(s) Not (String.IsNullOrEmpty(s.MaterialOriginal) AndAlso
                                                String.IsNullOrEmpty(s.MaterialReemplazo))).ToList()
        If subs.Count = 0 Then
            MessageBox.Show(Me, "Add at least one material substitution (Original + Replacement) before saving.",
                            "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        ' ⛔ Bajo Try, y no por precaución: `ContentEquals` termina en `WbWriter.EmitBody`, que TIRA con
        ' un subrecord que el esquema no supo ubicar. Y este editor es el ÚNICO de los tres cuyo
        ' `Edicion` ya está cableado (MainForm.BuildMswpOverrideDraftFromReal), o sea el único que ya
        ' trabaja sobre un árbol COPIADO de un record del disco — la precondición exacta de ese throw.
        ' Sin esto, un MSWP de un mod de terceros con un subrecord raro mataba el proceso AL APRETAR OK:
        ' es un manejador de clic sin Try y la app corre con UnhandledExceptionMode.ThrowException.
        ' ⛔ DENTRO del Try. `Clone()` ganó una precondición que puede tirar, y acá arriba no la
        ' atrapa nadie: el Catch que revierte el borrador empieza una línea más abajo, y la app corre
        ' con `UnhandledExceptionMode.ThrowException`, o sea que tirar en esta línea CIERRA la app y
        ' deja el borrador registrado a medias — lo contrario de lo que este Try existe para hacer.
        Dim antes As MswpDraft = Nothing
        Try
            antes = _draft?.Clone()
            _draft.Record.EditorID = edid
            _draft.Record.ReemplazarSustituciones(subs)
            ' Sucio sólo ante un cambio REAL y contra la LÍNEA DE BASE PRISTINA (espejo de ARMA/ARMO): un
            ' OVERRIDE abierto y aceptado sin editar nada no se marca modificado, así que el saver no
            ' re-emite un MSWP idéntico — pero uno que YA venía editado sí queda sucio y sí se emite, que
            ' es lo que antes se perdía. Los NUEVOS son siempre sucios. Ver `Borradores.SucioContraLaBase`.
            If Not _draft.IsNew Then
                _draft.IsModified = Borradores.SucioContraLaBase(_base IsNot Nothing,
                                                                 _draft.ContentEquals(_base))
            End If
        Catch ex As Exception
            ' Primero deshacer, después avisar, y NO cerrar: el borrador vive en MainForm y el árbol es
            ' el mismo objeto, así que lo que quedó a medio escribir ya sería visible para el guardado.
            If antes IsNot Nothing Then
                _draft.Record = antes.Record
                _draft.IsModified = antes.IsModified
            End If
            Logger.Log("MswpSubEditor.OnOk: " & ex.ToString())
            MessageBox.Show(Me,
                "Could not build this material substitution:" & vbCrLf & vbCrLf &
                ex.Message & vbCrLf & vbCrLf &
                "The last change was rolled back. The details went to the log.",
                "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End Try

        ' ⛔ SOLTAR, no abandonar: el objetivo aceptado tiene que quedar REGISTRADO para que el
        ' llamador lo encuentre por `ResultMswpFormID`.
        _mainForm.RegisterMswpDraft(_draft)
        _toma.Soltar()
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>⛔ ABANDONAR AL CERRAR SIN OK. Es la mitad que faltaba: sin ella, lo que el editor
    ''' registró al probar las puertas queda vivo y sucio, y el guardado lo emite.</summary>
    Private Sub MswpSubEditor_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles Me.FormClosing
        If DialogResult <> DialogResult.OK Then _toma.Abandonar()
    End Sub

End Class
