' UI built in Designer per 00-reglas-ui-y-vb.md (companion to ArmaEditor_Form / ArmoEditor_Form).
' InitializeComponent is declarative ONLY: el combo de PNAM, las grillas de Extra Parts / Parts /
' Conditions y el panel de validez se POBLAN en code-behind, porque su contenido depende del juego de
' la sesión y del NPC de contexto.
'
' ⛔ TODA LA UI EN INGLÉS, como el resto de la app. Medido antes de escribir esto: 1.017 cadenas
' `.Text` en los *.Designer.vb y 46 literales de MessageBox/MsgBox, CERO en castellano. Donde ya
' existía el literal se REUSA el que hay ("NEW record", "Preview", "Include Body", "Browse…",
' "New from template…", "Override existing…", "Edit mine…"), y los títulos de grupo nombran el
' subrecord, como "Meshes (MOD2 male / MOD3 female / MOD4–5 first-person)" del editor de ARMA.
'
' ⛔⛔ TRES REGLAS DE ARMADO, cada una por un defecto que el usuario vio en pantalla (20-sep):
'
'   1. TODO `GroupBox` que vive en una fila AutoSize lleva `AutoSize = True` +
'      `AutoSizeMode = GrowAndShrink`, y su `TableLayoutPanel` de adentro también. Sin eso el grupo
'      conserva el alto de diseño y la ÚLTIMA fila de adentro queda TAPADA por el grupo de abajo: así
'      se cortaba la fila «Material swap (MODS)» del tab Model. Un `Dock = Fill` dentro de una fila
'      AutoSize no arregla nada — la fila mide al control, y el control no mide a su contenido.
'
'   2. Los botones de una fila van en un `FlowLayoutPanel` en UNA columna, no en una columna por
'      botón. Con una columna por botón el ancho mínimo de la tabla es la suma de todos, y el panel
'      izquierdo —que ahora es angosto a propósito— recortaba el último («New / Edit MSWP…»).
'
'   3. El banner de NEW/OVERRIDE va en su PROPIA fila del layout raíz, en negrita y con el color de
'      texto por defecto, como en `ArmaEditor_Form` / `ArmoEditor_Form`. Antes compartía la barra de
'      arriba con el EditorID —se superponían— y estaba en `GrayText`, que lo hacía leer como
'      deshabilitado justamente en el dato más importante de la ventana.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class HeadPartEditor_Form
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
        TopBar = New FlowLayoutPanel()
        ButtonNewBlank = New Button()
        ButtonNewFromTemplate = New Button()
        ButtonOverrideExisting = New Button()
        ButtonEditMine = New Button()
        LabelEdid = New Label()
        TextBoxEdid = New TextBox()
        LabelStatusBanner = New Label()
        MainSplit = New SplitContainer()
        Tabs = New TabControl()
        TabGeneral = New TabPage()
        GeneralLayout = New TableLayoutPanel()
        GroupIdentity = New GroupBox()
        IdentityLayout = New TableLayoutPanel()
        LabelName = New Label()
        TextBoxName = New TextBox()
        LabelType = New Label()
        ComboType = New ComboBox()
        LabelTypeRaw = New Label()
        CheckNonPlayable = New CheckBox()
        GroupFlags = New GroupBox()
        FlagsLayout = New FlowLayoutPanel()
        CheckPlayable = New CheckBox()
        CheckMale = New CheckBox()
        CheckFemale = New CheckBox()
        CheckIsExtraPart = New CheckBox()
        CheckUseSolidTint = New CheckBox()
        CheckUsesBodyTexture = New CheckBox()
        TabModel = New TabPage()
        ModelLayout = New TableLayoutPanel()
        GroupModel = New GroupBox()
        ModelInnerLayout = New TableLayoutPanel()
        LabelModl = New Label()
        TextBoxModl = New TextBox()
        ButtonBrowseModl = New Button()
        LabelModtStatus = New Label()
        LabelModc = New Label()
        CheckModcPresente = New CheckBox()
        NumericModc = New NumericUpDown()
        LabelModcHint = New Label()
        LabelFullPrec = New Label()
        LabelMods = New Label()
        TextBoxMods = New TextBox()
        ModcLayout = New FlowLayoutPanel()
        ModsButtons = New FlowLayoutPanel()
        ButtonPickMods = New Button()
        ButtonNewMods = New Button()
        GroupModelFlags = New GroupBox()
        GroupAltTex = New GroupBox()
        AltTexInner = New TableLayoutPanel()
        ListViewAltTex = New ListView()
        AltTexButtons = New FlowLayoutPanel()
        ButtonAddAltTex = New Button()
        ButtonEditAltTex = New Button()
        ButtonRemoveAltTex = New Button()
        ModelFlagsLayout = New FlowLayoutPanel()
        CheckHasFaceBones = New CheckBox()
        CheckHas1stPerson = New CheckBox()
        TabExtras = New TabPage()
        ExtrasLayout = New TableLayoutPanel()
        GroupExtras = New GroupBox()
        ExtrasInner = New TableLayoutPanel()
        ListViewExtras = New ListView()
        ExtrasButtons = New FlowLayoutPanel()
        ButtonAddExtra = New Button()
        ButtonNewExtra = New Button()
        ButtonEditExtra = New Button()
        ButtonRemoveExtra = New Button()
        ButtonExtraUp = New Button()
        ButtonExtraDown = New Button()
        GroupParts = New GroupBox()
        PartsInner = New TableLayoutPanel()
        ListViewParts = New ListView()
        PartsButtons = New FlowLayoutPanel()
        ButtonAddPart = New Button()
        ButtonEditPart = New Button()
        ButtonRemovePart = New Button()
        TabRefs = New TabPage()
        RefsLayout = New TableLayoutPanel()
        GroupRefs = New GroupBox()
        RefsInner = New TableLayoutPanel()
        LabelTnam = New Label()
        TextBoxTnam = New TextBox()
        TnamButtons = New FlowLayoutPanel()
        ButtonPickTnam = New Button()
        ButtonNewTnam = New Button()
        LabelCnam = New Label()
        TextBoxCnam = New TextBox()
        CnamButtons = New FlowLayoutPanel()
        ButtonPickCnam = New Button()
        LabelRnam = New Label()
        TextBoxRnam = New TextBox()
        RnamButtons = New FlowLayoutPanel()
        ButtonPickRnam = New Button()
        ButtonNewRnam = New Button()
        GroupRnamRaces = New GroupBox()
        ListBoxRnamRaces = New ListBox()
        TabConditions = New TabPage()
        ConditionsLayout = New TableLayoutPanel()
        LabelConditionsHint = New Label()
        ListViewConditions = New ListView()
        ConditionsButtons = New FlowLayoutPanel()
        ButtonAddCondition = New Button()
        ButtonEditCondition = New Button()
        ButtonRemoveCondition = New Button()
        ButtonConditionUp = New Button()
        ButtonConditionDown = New Button()
        RightLayout = New TableLayoutPanel()
        PreviewHeader = New FlowLayoutPanel()
        LabelPreviewHint = New Label()
        RadioPartOnly = New RadioButton()
        RadioOverNpc = New RadioButton()
        CheckIncludeBody = New CheckBox()
        PreviewControlPanel = New Panel()
        LabelPreviewSubject = New Label()
        GroupValidity = New GroupBox()
        ValidityLayout = New TableLayoutPanel()
        LabelValidType = New Label()
        LabelValidGender = New Label()
        LabelValidNotExtra = New Label()
        LabelValidRace = New Label()
        LabelValidVerdict = New Label()
        ButtonFitToNpc = New Button()
        BottomLayout = New FlowLayoutPanel()
        ButtonOk = New Button()
        ButtonCancel = New Button()

        RootLayout.SuspendLayout()
        TopBar.SuspendLayout()
        CType(MainSplit, ComponentModel.ISupportInitialize).BeginInit()
        MainSplit.SuspendLayout()
        Tabs.SuspendLayout()
        SuspendLayout()
        '
        ' RootLayout — barra de objetivo, banner, split, OK/Cancel. CUATRO filas: el banner tiene la
        ' suya (regla 3 de la cabecera), así que ya no puede superponerse con el EditorID.
        '
        RootLayout.ColumnCount = 1
        RootLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RootLayout.Controls.Add(TopBar, 0, 0)
        RootLayout.Controls.Add(LabelStatusBanner, 0, 1)
        RootLayout.Controls.Add(MainSplit, 0, 2)
        RootLayout.Controls.Add(BottomLayout, 0, 3)
        RootLayout.Dock = DockStyle.Fill
        RootLayout.Name = "RootLayout"
        RootLayout.Padding = New Padding(8)
        RootLayout.RowCount = 4
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RootLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RootLayout.TabIndex = 0
        '
        ' TopBar — las cuatro puertas de objetivo y el EditorID. El banner NO va acá.
        '
        TopBar.AutoSize = True
        TopBar.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TopBar.Controls.Add(ButtonNewBlank)
        TopBar.Controls.Add(ButtonNewFromTemplate)
        TopBar.Controls.Add(ButtonOverrideExisting)
        TopBar.Controls.Add(ButtonEditMine)
        TopBar.Controls.Add(LabelEdid)
        TopBar.Controls.Add(TextBoxEdid)
        TopBar.Dock = DockStyle.Fill
        TopBar.Margin = New Padding(0)
        TopBar.Name = "TopBar"
        TopBar.TabIndex = 0
        TopBar.WrapContents = True
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
        LabelEdid.Margin = New Padding(16, 9, 3, 0)
        LabelEdid.Name = "LabelEdid"
        LabelEdid.TabIndex = 4
        LabelEdid.Text = "EditorID:"
        '
        TextBoxEdid.Margin = New Padding(3, 5, 3, 3)
        TextBoxEdid.Name = "TextBoxEdid"
        TextBoxEdid.PlaceholderText = "name"
        TextBoxEdid.Size = New Size(260, 23)
        TextBoxEdid.TabIndex = 5
        '
        ' LabelStatusBanner — NEGRITA y color de texto POR DEFECTO, gemelo del de ARMA/ARMO. Dice qué
        ' va a hacer Save: OVERRIDE (con el plugin de origen), Editing draft, o NEW record.
        '
        LabelStatusBanner.AutoSize = True
        LabelStatusBanner.Dock = DockStyle.Fill
        LabelStatusBanner.Font = New Font("Segoe UI", 9.75F, FontStyle.Bold)
        LabelStatusBanner.Margin = New Padding(3, 6, 3, 8)
        LabelStatusBanner.Name = "LabelStatusBanner"
        LabelStatusBanner.TabIndex = 1
        LabelStatusBanner.Text = "NEW record"
        '
        ' MainSplit — izquierda las pestañas, derecha el preview y la validez.
        ' ⛔ `FixedPanel = Panel1`: al agrandar la ventana crece el PREVIEW, no el formulario. Y el
        ' divisor arranca angosto a la izquierda (pedido del usuario) con los dos mínimos puestos, así
        ' que ningún panel puede quedar en un ancho donde sus controles no entren.
        '
        MainSplit.Dock = DockStyle.Fill
        MainSplit.FixedPanel = FixedPanel.Panel1
        MainSplit.Name = "MainSplit"
        MainSplit.Panel1.Controls.Add(Tabs)
        MainSplit.Panel2.Controls.Add(RightLayout)
        ' ⛔⛔ EL ORDEN DE ESTAS CUATRO LINEAS ES LA LEY, y me costo un crash de arranque:
        ' `SplitterDistance` VALIDA contra el ancho ACTUAL del control («debe estar entre Panel1MinSize y
        ' Ancho - Panel2MinSize») y al construirse el control mide el default de 150 px, asi que cualquier
        ' minimo mayor que eso hace TIRAR al setter — `InvalidOperationException` antes de que el
        ' formulario exista. Se le da el `Size` PRIMERO, y los minimos y el divisor DESPUES; el `Dock` de
        ' arriba lo re-dimensiona igual cuando entra al layout, asi que este tamano es solo para que la
        ' validacion tenga con que comparar.
        MainSplit.Size = New Size(1470, 700)
        MainSplit.Panel1MinSize = 420
        MainSplit.Panel2MinSize = 440
        MainSplit.SplitterDistance = 560
        MainSplit.SplitterWidth = 6
        MainSplit.TabIndex = 2
        '
        Tabs.Controls.Add(TabGeneral)
        Tabs.Controls.Add(TabModel)
        Tabs.Controls.Add(TabExtras)
        Tabs.Controls.Add(TabRefs)
        Tabs.Controls.Add(TabConditions)
        Tabs.Dock = DockStyle.Fill
        Tabs.Name = "Tabs"
        Tabs.SelectedIndex = 0
        Tabs.TabIndex = 0
        '
        ' ===================== General =====================
        '
        TabGeneral.Controls.Add(GeneralLayout)
        TabGeneral.Name = "TabGeneral"
        TabGeneral.Padding = New Padding(8)
        TabGeneral.TabIndex = 0
        TabGeneral.Text = "General"
        TabGeneral.UseVisualStyleBackColor = True
        '
        GeneralLayout.ColumnCount = 1
        GeneralLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        GeneralLayout.Controls.Add(GroupIdentity, 0, 0)
        GeneralLayout.Controls.Add(GroupFlags, 0, 1)
        GeneralLayout.Dock = DockStyle.Fill
        GeneralLayout.Name = "GeneralLayout"
        GeneralLayout.RowCount = 3
        GeneralLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GeneralLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        GeneralLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        GeneralLayout.TabIndex = 0
        '
        GroupIdentity.AutoSize = True
        GroupIdentity.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupIdentity.Controls.Add(IdentityLayout)
        GroupIdentity.Dock = DockStyle.Fill
        GroupIdentity.Name = "GroupIdentity"
        GroupIdentity.Padding = New Padding(8, 4, 8, 8)
        GroupIdentity.TabIndex = 0
        GroupIdentity.TabStop = False
        GroupIdentity.Text = "Identity (FULL name + PNAM type)"
        '
        IdentityLayout.AutoSize = True
        IdentityLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        IdentityLayout.ColumnCount = 2
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        IdentityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        IdentityLayout.Controls.Add(LabelName, 0, 0)
        IdentityLayout.Controls.Add(TextBoxName, 1, 0)
        IdentityLayout.Controls.Add(LabelType, 0, 1)
        IdentityLayout.Controls.Add(ComboType, 1, 1)
        IdentityLayout.Controls.Add(LabelTypeRaw, 1, 2)
        IdentityLayout.Controls.Add(CheckNonPlayable, 1, 3)
        IdentityLayout.Dock = DockStyle.Fill
        IdentityLayout.Name = "IdentityLayout"
        IdentityLayout.RowCount = 4
        IdentityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        IdentityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        IdentityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        IdentityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        IdentityLayout.TabIndex = 0
        '
        LabelName.Anchor = AnchorStyles.Left
        LabelName.AutoSize = True
        LabelName.Name = "LabelName"
        LabelName.TabIndex = 0
        LabelName.Text = "Name (FULL):"
        '
        TextBoxName.Dock = DockStyle.Fill
        TextBoxName.Name = "TextBoxName"
        TextBoxName.TabIndex = 1
        '
        LabelType.Anchor = AnchorStyles.Left
        LabelType.AutoSize = True
        LabelType.Name = "LabelType"
        LabelType.TabIndex = 2
        LabelType.Text = "Type (PNAM):"
        '
        ' ⛔⛔ `DropDownList`, NO `DropDown`. El PNAM es una lista CERRADA del esquema, así que tipear a
        ' mano no puede producir nada válido que la lista no tenga: lo único que habilita es escribir
        ' basura. Los cinco HDPT con 69/71 de `UBE_AllRace.esp` se conservan con la MISMA ley que el
        ' operador de una condición en `ConditionsEditor_Form`: cuando el record trae un valor que el
        ' enum no nombra, el combo recibe UNA entrada extra con ese valor crudo y queda elegida.
        ' Cerrado y sin normalizar a la vez; la versión editable resolvía lo segundo rompiendo lo primero.
        ComboType.DropDownStyle = ComboBoxStyle.DropDownList
        ComboType.Dock = DockStyle.Fill
        ComboType.Name = "ComboType"
        ComboType.TabIndex = 3
        '
        LabelTypeRaw.Anchor = AnchorStyles.Left
        LabelTypeRaw.AutoSize = True
        LabelTypeRaw.ForeColor = SystemColors.GrayText
        LabelTypeRaw.Name = "LabelTypeRaw"
        LabelTypeRaw.TabIndex = 4
        '
        CheckNonPlayable.Anchor = AnchorStyles.Left
        CheckNonPlayable.AutoSize = True
        CheckNonPlayable.Name = "CheckNonPlayable"
        CheckNonPlayable.TabIndex = 5
        CheckNonPlayable.Text = "Non-Playable (record header)"
        '
        GroupFlags.AutoSize = True
        GroupFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupFlags.Controls.Add(FlagsLayout)
        GroupFlags.Dock = DockStyle.Fill
        GroupFlags.Name = "GroupFlags"
        GroupFlags.Padding = New Padding(8, 4, 8, 8)
        GroupFlags.TabIndex = 1
        GroupFlags.TabStop = False
        GroupFlags.Text = "Flags (DATA)"
        '
        FlagsLayout.AutoSize = True
        FlagsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FlagsLayout.Controls.Add(CheckPlayable)
        FlagsLayout.Controls.Add(CheckMale)
        FlagsLayout.Controls.Add(CheckFemale)
        FlagsLayout.Controls.Add(CheckIsExtraPart)
        FlagsLayout.Controls.Add(CheckUseSolidTint)
        FlagsLayout.Controls.Add(CheckUsesBodyTexture)
        FlagsLayout.Dock = DockStyle.Fill
        FlagsLayout.Name = "FlagsLayout"
        FlagsLayout.TabIndex = 0
        FlagsLayout.WrapContents = True
        '
        CheckPlayable.AutoSize = True
        CheckPlayable.Name = "CheckPlayable"
        CheckPlayable.TabIndex = 0
        CheckPlayable.Text = "Playable"
        '
        CheckMale.AutoSize = True
        CheckMale.Name = "CheckMale"
        CheckMale.TabIndex = 1
        CheckMale.Text = "Male"
        '
        CheckFemale.AutoSize = True
        CheckFemale.Name = "CheckFemale"
        CheckFemale.TabIndex = 2
        CheckFemale.Text = "Female"
        '
        CheckIsExtraPart.AutoSize = True
        CheckIsExtraPart.Name = "CheckIsExtraPart"
        CheckIsExtraPart.TabIndex = 3
        CheckIsExtraPart.Text = "Is Extra Part"
        '
        CheckUseSolidTint.AutoSize = True
        CheckUseSolidTint.Name = "CheckUseSolidTint"
        CheckUseSolidTint.TabIndex = 4
        CheckUseSolidTint.Text = "Use Solid Tint"
        '
        CheckUsesBodyTexture.AutoSize = True
        CheckUsesBodyTexture.Name = "CheckUsesBodyTexture"
        CheckUsesBodyTexture.TabIndex = 5
        CheckUsesBodyTexture.Text = "Uses Body Texture"
        '
        ' ===================== Model =====================
        '
        TabModel.Controls.Add(ModelLayout)
        TabModel.Name = "TabModel"
        TabModel.Padding = New Padding(8)
        TabModel.TabIndex = 1
        TabModel.Text = "Model"
        TabModel.UseVisualStyleBackColor = True
        '
        ModelLayout.ColumnCount = 1
        ModelLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ModelLayout.Controls.Add(GroupModel, 0, 0)
        ModelLayout.Controls.Add(GroupModelFlags, 0, 1)
        ModelLayout.Controls.Add(GroupAltTex, 0, 2)
        ModelLayout.Controls.Add(LabelModtStatus, 0, 3)
        ModelLayout.Controls.Add(LabelModcHint, 0, 4)
        ' La fila 5 ya estaba declarada y vacía (RowCount = 6, seis RowStyles): el aviso de precisión
        ' entra sin tocar el layout. Va DEBAJO de los grupos, con los otros dos rótulos de estado.
        ModelLayout.Controls.Add(LabelFullPrec, 0, 5)
        ModelLayout.Dock = DockStyle.Fill
        ModelLayout.Name = "ModelLayout"
        ModelLayout.RowCount = 6
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelLayout.TabIndex = 0
        '
        GroupModel.AutoSize = True
        GroupModel.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupModel.Controls.Add(ModelInnerLayout)
        GroupModel.Dock = DockStyle.Fill
        GroupModel.Name = "GroupModel"
        GroupModel.Padding = New Padding(8, 4, 8, 8)
        GroupModel.TabIndex = 0
        GroupModel.TabStop = False
        GroupModel.Text = "Model (MODL mesh / MODT model info / MODC colour remap / MODS)"
        '
        ModelInnerLayout.AutoSize = True
        ModelInnerLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModelInnerLayout.ColumnCount = 4
        ModelInnerLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        ModelInnerLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ModelInnerLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        ModelInnerLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        ' ⛔ LOS DOS BOTONES DEL MATERIAL SWAP VAN EN SU PROPIA FILA, y no es cosmético: la columna 2
        ' es AutoSize, o sea que mide el MÁS ANCHO de lo que haya en ella. Con «…» +
        ' «New / Edit MSWP…» ahí dentro medía ~150 px, y esos 150 px se los comía a las DOS cajas de
        ' ruta — la del mesh se veía cortada a la mitad. Sola, la columna mide sólo «Browse…» (~70 px)
        ' y las rutas ganan la diferencia. Pedido del usuario: «lleva browse más a la derecha, así el
        ' path es más visible».
        ' ⛔ EL REPARTO DE COLUMNAS, que es lo que decide cuánto se ve del path. La columna 2 es AutoSize:
        ' mide lo MÁS ANCHO que haya en ella. Con los dos botones del material swap ahí dentro medía
        ' ~150 px y se los comía a la caja del mesh — la ruta se veía cortada a la mitad.
        '   La solución NO es bajar los botones a otra fila (se probó y el usuario lo rechazó: los quiere
        ' al lado de su campo). Es darle a la caja del MESH dos columnas y a «Browse…» una propia: así la
        ' ruta ocupa c1+c2, los botones del MODS siguen en su fila ocupando c2+c3, y la caja del MODS se
        ' acorta — que es exactamente lo que el usuario pidió («ahí sí acortás el textbox»).
        ModelInnerLayout.Controls.Add(LabelModl, 0, 0)
        ModelInnerLayout.Controls.Add(TextBoxModl, 1, 0)
        ModelInnerLayout.SetColumnSpan(TextBoxModl, 2)
        ModelInnerLayout.Controls.Add(ButtonBrowseModl, 3, 0)
        ModelInnerLayout.Controls.Add(LabelModc, 0, 1)
        ModelInnerLayout.Controls.Add(ModcLayout, 1, 1)
        ModelInnerLayout.SetColumnSpan(ModcLayout, 3)
        ModelInnerLayout.Controls.Add(LabelMods, 0, 2)
        ModelInnerLayout.Controls.Add(TextBoxMods, 1, 2)
        ModelInnerLayout.Controls.Add(ModsButtons, 2, 2)
        ModelInnerLayout.SetColumnSpan(ModsButtons, 2)
        ModelInnerLayout.Dock = DockStyle.Fill
        ModelInnerLayout.Name = "ModelInnerLayout"
        ModelInnerLayout.RowCount = 3
        ModelInnerLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelInnerLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelInnerLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ModelInnerLayout.TabIndex = 0
        '
        LabelModl.Anchor = AnchorStyles.Left
        LabelModl.AutoSize = True
        LabelModl.Name = "LabelModl"
        LabelModl.TabIndex = 0
        LabelModl.Text = "Mesh (MODL):"
        '
        TextBoxModl.Dock = DockStyle.Fill
        TextBoxModl.Name = "TextBoxModl"
        TextBoxModl.TabIndex = 1
        '
        ' A la DERECHA de su columna: pegado al borde, con la caja de la ruta ocupando todo lo demás.
        ButtonBrowseModl.Anchor = AnchorStyles.Right
        ButtonBrowseModl.AutoSize = True
        ButtonBrowseModl.Name = "ButtonBrowseModl"
        ButtonBrowseModl.TabIndex = 2
        ButtonBrowseModl.Text = "Browse…"
        '
        ' ⛔ VA FUERA DEL GRUPO, en una fila propia del layout de la pestaña y a TODO SU ANCHO. Dentro
        ' del grupo ocupaba la columna del CAMPO, y como es un texto largo que envuelve, empujaba el
        ' mínimo de esa columna y le comía el ancho a la caja del MODL: la ruta de la malla se veía
        ' cortada a la mitad. Un rótulo explicativo no compite por la columna de un campo.
        LabelModtStatus.AutoSize = True
        LabelModtStatus.Dock = DockStyle.Fill
        LabelModtStatus.ForeColor = SystemColors.GrayText
        LabelModtStatus.Margin = New Padding(3, 8, 3, 2)
        LabelModtStatus.Name = "LabelModtStatus"
        LabelModtStatus.TabIndex = 3
        '
        LabelModc.Anchor = AnchorStyles.Left
        LabelModc.AutoSize = True
        LabelModc.Name = "LabelModc"
        LabelModc.TabIndex = 4
        LabelModc.Text = "Colour remap (MODC):"
        '
        ' ⛔⛔ EL CAMPO ES UN FLOAT DEL ESQUEMA (`Wb.Flt` sobre `Color Remapping Index`), asi que el
        ' control es un `NumericUpDown` y no una caja de texto: con texto libre el usuario podia tipear
        ' cualquier cosa y el commit la descartaba en silencio.
        '
        ' Y va con un CHECK DE PRESENCIA al lado porque un numerico NO PUEDE representar (ausente),
        ' que es un estado distinto de cero: el esquema declara MODC como subrecord opcional y el arbol
        ' tiene `ModelColorRemappingIndexPresente` aparte del valor. Sin el check, abrir un record sin
        ' MODC y aceptar le escribiria un MODC = 0 que no tenia. Destildado = el subrecord NO ESTA.
        '
        ' El rango 0-1 no es inventado: los 406 records de Fallout 4 que traen MODC (STAT 321, MSTT 55,
        ' SCOL 30) tienen TODOS sus valores entre 0.0 y 1.0, con 90 valores distintos.
        ModcLayout.AutoSize = True
        ModcLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModcLayout.Controls.Add(CheckModcPresente)
        ModcLayout.Controls.Add(NumericModc)
        ModcLayout.Dock = DockStyle.Fill
        ModcLayout.Margin = New Padding(0)
        ModcLayout.Name = "ModcLayout"
        ModcLayout.TabIndex = 5
        ModcLayout.WrapContents = False
        '
        CheckModcPresente.Anchor = AnchorStyles.Left
        CheckModcPresente.AutoSize = True
        CheckModcPresente.Name = "CheckModcPresente"
        CheckModcPresente.TabIndex = 0
        CheckModcPresente.Text = "present"
        '
        ' El texto a la DERECHA, que es como se lee un número (pedido del usuario).
        NumericModc.Anchor = AnchorStyles.Left
        NumericModc.TextAlign = HorizontalAlignment.Right
        NumericModc.DecimalPlaces = 4
        NumericModc.Enabled = False
        NumericModc.Increment = New Decimal(New Integer() {5, 0, 0, 131072})
        NumericModc.Maximum = New Decimal(New Integer() {1, 0, 0, 0})
        NumericModc.Minimum = New Decimal(New Integer() {0, 0, 0, 0})
        NumericModc.Name = "NumericModc"
        NumericModc.Size = New Size(96, 23)
        NumericModc.TabIndex = 1
        '
        ' El campo es un FLOAT del esquema ("Color Remapping Index", `Wb.Flt`) y el rotulo lo dice, porque
        ' una caja de texto vacia al lado de "Colour remap (MODC)" no le dice a nadie ni el tipo ni el
        ' rango ni si hace falta. Medido sobre los 2.546 HDPT de Fallout 4 (por FormID ganador, `Tools\censo_hdpt.py`): NINGUNO trae MODC. El campo
        ' existe en HDPT porque el bloque Model del esquema es COMPARTIDO -- los 406 records que si lo
        ' usan son STAT (321), MSTT (55) y SCOL (30), todos con valores entre 0.0 y 1.0.
        LabelModcHint.AutoSize = True
        LabelModcHint.Dock = DockStyle.Fill
        LabelModcHint.ForeColor = SystemColors.GrayText
        LabelModcHint.Margin = New Padding(3, 2, 3, 2)
        LabelModcHint.Name = "LabelModcHint"
        LabelModcHint.TabIndex = 9
        LabelModcHint.Text = "MODC: float 0-1. Leave it empty — no vanilla head part uses it."
        '
        ' Aviso de precisión completa. Arranca VACÍO y lo llena RefrescarEstadoDelModelo: un rótulo
        ' con texto de fábrica mentiría hasta que se resuelva la primera malla. `Visible` se maneja
        ' por texto (una fila AutoSize con un label vacío no ocupa alto), no con un Hide, para que el
        ' único lugar que decide sea el que hizo la medición.
        LabelFullPrec.AutoSize = True
        LabelFullPrec.Dock = DockStyle.Fill
        LabelFullPrec.ForeColor = Color.Firebrick
        LabelFullPrec.Margin = New Padding(3, 8, 3, 2)
        LabelFullPrec.Name = "LabelFullPrec"
        LabelFullPrec.TabIndex = 10
        LabelFullPrec.Text = ""
        '
        LabelMods.Anchor = AnchorStyles.Left
        LabelMods.AutoSize = True
        LabelMods.Name = "LabelMods"
        LabelMods.TabIndex = 6
        LabelMods.Text = "Material swap (MODS):"
        '
        TextBoxMods.Dock = DockStyle.Fill
        TextBoxMods.Name = "TextBoxMods"
        TextBoxMods.ReadOnly = True
        TextBoxMods.TabIndex = 7
        '
        ModsButtons.AutoSize = True
        ModsButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModsButtons.Controls.Add(ButtonPickMods)
        ModsButtons.Controls.Add(ButtonNewMods)
        ModsButtons.Margin = New Padding(0)
        ModsButtons.Name = "ModsButtons"
        ModsButtons.TabIndex = 8
        ModsButtons.WrapContents = False
        '
        ButtonPickMods.AutoSize = True
        ButtonPickMods.Name = "ButtonPickMods"
        ButtonPickMods.TabIndex = 0
        ButtonPickMods.Text = "…"
        '
        ButtonNewMods.AutoSize = True
        ButtonNewMods.Name = "ButtonNewMods"
        ButtonNewMods.TabIndex = 1
        ButtonNewMods.Text = "New / Edit MSWP…"
        '
        GroupModelFlags.AutoSize = True
        GroupModelFlags.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupModelFlags.Controls.Add(ModelFlagsLayout)
        GroupModelFlags.Dock = DockStyle.Fill
        GroupModelFlags.Name = "GroupModelFlags"
        GroupModelFlags.Padding = New Padding(8, 4, 8, 8)
        GroupModelFlags.TabIndex = 1
        GroupModelFlags.TabStop = False
        GroupModelFlags.Text = "Model flags (MODF)"
        '
        ModelFlagsLayout.AutoSize = True
        ModelFlagsLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ModelFlagsLayout.Controls.Add(CheckHasFaceBones)
        ModelFlagsLayout.Controls.Add(CheckHas1stPerson)
        ModelFlagsLayout.Dock = DockStyle.Fill
        ModelFlagsLayout.Name = "ModelFlagsLayout"
        ModelFlagsLayout.TabIndex = 0
        ModelFlagsLayout.WrapContents = True
        '
        CheckHasFaceBones.AutoSize = True
        CheckHasFaceBones.Name = "CheckHasFaceBones"
        CheckHasFaceBones.TabIndex = 0
        CheckHasFaceBones.Text = "Has FaceBones model"
        '
        CheckHas1stPerson.AutoSize = True
        CheckHas1stPerson.Name = "CheckHas1stPerson"
        CheckHas1stPerson.TabIndex = 1
        CheckHas1stPerson.Text = "Has 1st-person model"
        '
        ' GroupAltTex -- el MODS de SKYRIM. En Fallout 4 el grupo se SACA (ver ConfigurarPorJuego):
        ' ahi MODS es un FormID de MSWP y su fila vive arriba, en el grupo del modelo.
        '
        GroupAltTex.Controls.Add(AltTexInner)
        GroupAltTex.Dock = DockStyle.Fill
        GroupAltTex.Name = "GroupAltTex"
        GroupAltTex.Padding = New Padding(8, 4, 8, 8)
        GroupAltTex.TabIndex = 2
        GroupAltTex.TabStop = False
        GroupAltTex.Text = "Alternate Textures (MODS) -- replaces the texture set of one named shape"
        '
        AltTexInner.ColumnCount = 1
        AltTexInner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        AltTexInner.Controls.Add(ListViewAltTex, 0, 0)
        AltTexInner.Controls.Add(AltTexButtons, 0, 1)
        AltTexInner.Dock = DockStyle.Fill
        AltTexInner.Name = "AltTexInner"
        AltTexInner.RowCount = 2
        AltTexInner.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        AltTexInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        AltTexInner.TabIndex = 0
        '
        ListViewAltTex.Dock = DockStyle.Fill
        ListViewAltTex.FullRowSelect = True
        ListViewAltTex.HideSelection = False
        ListViewAltTex.MultiSelect = False
        ListViewAltTex.Name = "ListViewAltTex"
        ListViewAltTex.TabIndex = 0
        ListViewAltTex.UseCompatibleStateImageBehavior = False
        ListViewAltTex.View = View.Details
        '
        AltTexButtons.AutoSize = True
        AltTexButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        AltTexButtons.Controls.Add(ButtonAddAltTex)
        AltTexButtons.Controls.Add(ButtonEditAltTex)
        AltTexButtons.Controls.Add(ButtonRemoveAltTex)
        AltTexButtons.Dock = DockStyle.Fill
        AltTexButtons.Margin = New Padding(0, 4, 0, 0)
        AltTexButtons.Name = "AltTexButtons"
        AltTexButtons.TabIndex = 1
        AltTexButtons.WrapContents = True
        '
        ButtonAddAltTex.AutoSize = True
        ButtonAddAltTex.Name = "ButtonAddAltTex"
        ButtonAddAltTex.TabIndex = 0
        ButtonAddAltTex.Text = "Add..."
        '
        ButtonEditAltTex.AutoSize = True
        ButtonEditAltTex.Name = "ButtonEditAltTex"
        ButtonEditAltTex.TabIndex = 1
        ButtonEditAltTex.Text = "Edit..."
        '
        ButtonRemoveAltTex.AutoSize = True
        ButtonRemoveAltTex.Name = "ButtonRemoveAltTex"
        ButtonRemoveAltTex.TabIndex = 2
        ButtonRemoveAltTex.Text = "Remove"
        '
        ' ===================== Extras && Parts =====================
        '
        TabExtras.Controls.Add(ExtrasLayout)
        TabExtras.Name = "TabExtras"
        TabExtras.Padding = New Padding(8)
        TabExtras.TabIndex = 2
        TabExtras.Text = "Extras && Parts"
        TabExtras.UseVisualStyleBackColor = True
        '
        ' Las dos grillas se reparten el alto en PORCENTAJE (no AutoSize): una lista tiene que estirarse
        ' con la ventana, y las filas AutoSize de los otros tabs existen porque ahí hay campos, no listas.
        '
        ExtrasLayout.ColumnCount = 1
        ExtrasLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ExtrasLayout.Controls.Add(GroupExtras, 0, 0)
        ExtrasLayout.Controls.Add(GroupParts, 0, 1)
        ExtrasLayout.Dock = DockStyle.Fill
        ExtrasLayout.Name = "ExtrasLayout"
        ExtrasLayout.RowCount = 2
        ExtrasLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 58F))
        ExtrasLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 42F))
        ExtrasLayout.TabIndex = 0
        '
        GroupExtras.Controls.Add(ExtrasInner)
        GroupExtras.Dock = DockStyle.Fill
        GroupExtras.Name = "GroupExtras"
        GroupExtras.Padding = New Padding(8, 4, 8, 8)
        GroupExtras.TabIndex = 0
        GroupExtras.TabStop = False
        GroupExtras.Text = "Extra Parts (HNAM → HDPT) — the engine pulls these with the parent"
        '
        ExtrasInner.ColumnCount = 1
        ExtrasInner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ExtrasInner.Controls.Add(ListViewExtras, 0, 0)
        ExtrasInner.Controls.Add(ExtrasButtons, 0, 1)
        ExtrasInner.Dock = DockStyle.Fill
        ExtrasInner.Name = "ExtrasInner"
        ExtrasInner.RowCount = 2
        ExtrasInner.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ExtrasInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ExtrasInner.TabIndex = 0
        '
        ListViewExtras.Dock = DockStyle.Fill
        ListViewExtras.FullRowSelect = True
        ListViewExtras.HideSelection = False
        ListViewExtras.MultiSelect = False
        ListViewExtras.Name = "ListViewExtras"
        ListViewExtras.TabIndex = 0
        ListViewExtras.UseCompatibleStateImageBehavior = False
        ListViewExtras.View = View.Details
        '
        ExtrasButtons.AutoSize = True
        ExtrasButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ExtrasButtons.Controls.Add(ButtonAddExtra)
        ExtrasButtons.Controls.Add(ButtonNewExtra)
        ExtrasButtons.Controls.Add(ButtonEditExtra)
        ExtrasButtons.Controls.Add(ButtonRemoveExtra)
        ExtrasButtons.Controls.Add(ButtonExtraUp)
        ExtrasButtons.Controls.Add(ButtonExtraDown)
        ExtrasButtons.Dock = DockStyle.Fill
        ExtrasButtons.Margin = New Padding(0, 4, 0, 0)
        ExtrasButtons.Name = "ExtrasButtons"
        ExtrasButtons.TabIndex = 1
        ExtrasButtons.WrapContents = True
        '
        ButtonAddExtra.AutoSize = True
        ButtonAddExtra.Name = "ButtonAddExtra"
        ButtonAddExtra.TabIndex = 0
        ButtonAddExtra.Text = "Add HDPT…"
        '
        ButtonNewExtra.AutoSize = True
        ButtonNewExtra.Name = "ButtonNewExtra"
        ButtonNewExtra.TabIndex = 1
        ButtonNewExtra.Text = "New HDPT…"
        '
        ButtonEditExtra.AutoSize = True
        ButtonEditExtra.Name = "ButtonEditExtra"
        ButtonEditExtra.TabIndex = 2
        ButtonEditExtra.Text = "Edit…"
        '
        ButtonRemoveExtra.AutoSize = True
        ButtonRemoveExtra.Name = "ButtonRemoveExtra"
        ButtonRemoveExtra.TabIndex = 3
        ButtonRemoveExtra.Text = "Remove"
        '
        ButtonExtraUp.AutoSize = True
        ButtonExtraUp.Name = "ButtonExtraUp"
        ButtonExtraUp.TabIndex = 4
        ButtonExtraUp.Text = "Move Up"
        '
        ButtonExtraDown.AutoSize = True
        ButtonExtraDown.Name = "ButtonExtraDown"
        ButtonExtraDown.TabIndex = 5
        ButtonExtraDown.Text = "Move Down"
        '
        GroupParts.Controls.Add(PartsInner)
        GroupParts.Dock = DockStyle.Fill
        GroupParts.Name = "GroupParts"
        GroupParts.Padding = New Padding(8, 4, 8, 8)
        GroupParts.TabIndex = 1
        GroupParts.TabStop = False
        GroupParts.Text = "Parts (NAM0 part type + NAM1 file)"
        '
        PartsInner.ColumnCount = 1
        PartsInner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        PartsInner.Controls.Add(ListViewParts, 0, 0)
        PartsInner.Controls.Add(PartsButtons, 0, 1)
        PartsInner.Dock = DockStyle.Fill
        PartsInner.Name = "PartsInner"
        PartsInner.RowCount = 2
        PartsInner.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        PartsInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        PartsInner.TabIndex = 0
        '
        ListViewParts.Dock = DockStyle.Fill
        ListViewParts.FullRowSelect = True
        ListViewParts.HideSelection = False
        ListViewParts.MultiSelect = False
        ListViewParts.Name = "ListViewParts"
        ListViewParts.TabIndex = 0
        ListViewParts.UseCompatibleStateImageBehavior = False
        ListViewParts.View = View.Details
        '
        PartsButtons.AutoSize = True
        PartsButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PartsButtons.Controls.Add(ButtonAddPart)
        PartsButtons.Controls.Add(ButtonEditPart)
        PartsButtons.Controls.Add(ButtonRemovePart)
        PartsButtons.Dock = DockStyle.Fill
        PartsButtons.Margin = New Padding(0, 4, 0, 0)
        PartsButtons.Name = "PartsButtons"
        PartsButtons.TabIndex = 1
        PartsButtons.WrapContents = True
        '
        ButtonAddPart.AutoSize = True
        ButtonAddPart.Name = "ButtonAddPart"
        ButtonAddPart.TabIndex = 0
        ButtonAddPart.Text = "Add…"
        '
        ButtonEditPart.AutoSize = True
        ButtonEditPart.Name = "ButtonEditPart"
        ButtonEditPart.TabIndex = 1
        ButtonEditPart.Text = "Edit…"
        '
        ButtonRemovePart.AutoSize = True
        ButtonRemovePart.Name = "ButtonRemovePart"
        ButtonRemovePart.TabIndex = 2
        ButtonRemovePart.Text = "Remove"
        '
        ' ===================== Textures && Races =====================
        '
        TabRefs.Controls.Add(RefsLayout)
        TabRefs.Name = "TabRefs"
        TabRefs.Padding = New Padding(8)
        TabRefs.TabIndex = 3
        TabRefs.Text = "Textures && Races"
        TabRefs.UseVisualStyleBackColor = True
        '
        RefsLayout.ColumnCount = 1
        RefsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RefsLayout.Controls.Add(GroupRefs, 0, 0)
        RefsLayout.Controls.Add(GroupRnamRaces, 0, 1)
        RefsLayout.Dock = DockStyle.Fill
        RefsLayout.Name = "RefsLayout"
        RefsLayout.RowCount = 2
        RefsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RefsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RefsLayout.TabIndex = 0
        '
        GroupRefs.AutoSize = True
        GroupRefs.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupRefs.Controls.Add(RefsInner)
        GroupRefs.Dock = DockStyle.Fill
        GroupRefs.Name = "GroupRefs"
        GroupRefs.Padding = New Padding(8, 4, 8, 8)
        GroupRefs.TabIndex = 0
        GroupRefs.TabStop = False
        GroupRefs.Text = "References (TNAM texture set / CNAM colour / RNAM valid races)"
        '
        RefsInner.AutoSize = True
        RefsInner.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RefsInner.ColumnCount = 3
        RefsInner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        RefsInner.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RefsInner.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        RefsInner.Controls.Add(LabelTnam, 0, 0)
        RefsInner.Controls.Add(TextBoxTnam, 1, 0)
        RefsInner.Controls.Add(TnamButtons, 2, 0)
        RefsInner.Controls.Add(LabelCnam, 0, 1)
        RefsInner.Controls.Add(TextBoxCnam, 1, 1)
        RefsInner.Controls.Add(CnamButtons, 2, 1)
        RefsInner.Controls.Add(LabelRnam, 0, 2)
        RefsInner.Controls.Add(TextBoxRnam, 1, 2)
        RefsInner.Controls.Add(RnamButtons, 2, 2)
        RefsInner.Dock = DockStyle.Fill
        RefsInner.Name = "RefsInner"
        RefsInner.RowCount = 3
        RefsInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RefsInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RefsInner.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RefsInner.TabIndex = 0
        '
        LabelTnam.Anchor = AnchorStyles.Left
        LabelTnam.AutoSize = True
        LabelTnam.Name = "LabelTnam"
        LabelTnam.TabIndex = 0
        LabelTnam.Text = "Texture set (TNAM):"
        '
        TextBoxTnam.Dock = DockStyle.Fill
        TextBoxTnam.Name = "TextBoxTnam"
        TextBoxTnam.ReadOnly = True
        TextBoxTnam.TabIndex = 1
        '
        TnamButtons.AutoSize = True
        TnamButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        TnamButtons.Controls.Add(ButtonPickTnam)
        TnamButtons.Controls.Add(ButtonNewTnam)
        TnamButtons.Margin = New Padding(0)
        TnamButtons.Name = "TnamButtons"
        TnamButtons.TabIndex = 2
        TnamButtons.WrapContents = False
        '
        ButtonPickTnam.AutoSize = True
        ButtonPickTnam.Name = "ButtonPickTnam"
        ButtonPickTnam.TabIndex = 0
        ButtonPickTnam.Text = "…"
        '
        ButtonNewTnam.AutoSize = True
        ButtonNewTnam.Name = "ButtonNewTnam"
        ButtonNewTnam.TabIndex = 1
        ButtonNewTnam.Text = "New / Edit TXST…"
        '
        LabelCnam.Anchor = AnchorStyles.Left
        LabelCnam.AutoSize = True
        LabelCnam.Name = "LabelCnam"
        LabelCnam.TabIndex = 3
        LabelCnam.Text = "Colour (CNAM):"
        '
        TextBoxCnam.Dock = DockStyle.Fill
        TextBoxCnam.Name = "TextBoxCnam"
        TextBoxCnam.ReadOnly = True
        TextBoxCnam.TabIndex = 4
        '
        CnamButtons.AutoSize = True
        CnamButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        CnamButtons.Controls.Add(ButtonPickCnam)
        CnamButtons.Margin = New Padding(0)
        CnamButtons.Name = "CnamButtons"
        CnamButtons.TabIndex = 5
        CnamButtons.WrapContents = False
        '
        ButtonPickCnam.AutoSize = True
        ButtonPickCnam.Name = "ButtonPickCnam"
        ButtonPickCnam.TabIndex = 0
        ButtonPickCnam.Text = "…"
        '
        LabelRnam.Anchor = AnchorStyles.Left
        LabelRnam.AutoSize = True
        LabelRnam.Name = "LabelRnam"
        LabelRnam.TabIndex = 6
        LabelRnam.Text = "Valid races (RNAM):"
        '
        TextBoxRnam.Dock = DockStyle.Fill
        TextBoxRnam.Name = "TextBoxRnam"
        TextBoxRnam.ReadOnly = True
        TextBoxRnam.TabIndex = 7
        '
        RnamButtons.AutoSize = True
        RnamButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        RnamButtons.Controls.Add(ButtonPickRnam)
        RnamButtons.Controls.Add(ButtonNewRnam)
        RnamButtons.Margin = New Padding(0)
        RnamButtons.Name = "RnamButtons"
        RnamButtons.TabIndex = 8
        RnamButtons.WrapContents = False
        '
        ButtonPickRnam.AutoSize = True
        ButtonPickRnam.Name = "ButtonPickRnam"
        ButtonPickRnam.TabIndex = 0
        ButtonPickRnam.Text = "…"
        '
        ButtonNewRnam.AutoSize = True
        ButtonNewRnam.Name = "ButtonNewRnam"
        ButtonNewRnam.TabIndex = 1
        ButtonNewRnam.Text = "New / Edit FLST…"
        '
        GroupRnamRaces.Controls.Add(ListBoxRnamRaces)
        GroupRnamRaces.Dock = DockStyle.Fill
        GroupRnamRaces.Name = "GroupRnamRaces"
        GroupRnamRaces.Padding = New Padding(8, 4, 8, 8)
        GroupRnamRaces.TabIndex = 1
        GroupRnamRaces.TabStop = False
        GroupRnamRaces.Text = "Races in that FLST (read-only)"
        '
        ListBoxRnamRaces.Dock = DockStyle.Fill
        ListBoxRnamRaces.IntegralHeight = False
        ListBoxRnamRaces.Name = "ListBoxRnamRaces"
        ListBoxRnamRaces.TabIndex = 0
        '
        ' ===================== Conditions =====================
        '
        TabConditions.Controls.Add(ConditionsLayout)
        TabConditions.Name = "TabConditions"
        TabConditions.Padding = New Padding(8)
        TabConditions.TabIndex = 4
        TabConditions.Text = "Conditions"
        TabConditions.UseVisualStyleBackColor = True
        '
        ConditionsLayout.ColumnCount = 1
        ConditionsLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ConditionsLayout.Controls.Add(LabelConditionsHint, 0, 0)
        ConditionsLayout.Controls.Add(ListViewConditions, 0, 1)
        ConditionsLayout.Controls.Add(ConditionsButtons, 0, 2)
        ConditionsLayout.Dock = DockStyle.Fill
        ConditionsLayout.Name = "ConditionsLayout"
        ConditionsLayout.RowCount = 3
        ConditionsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ConditionsLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        ConditionsLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ConditionsLayout.TabIndex = 0
        '
        LabelConditionsHint.AutoSize = True
        LabelConditionsHint.Dock = DockStyle.Fill
        LabelConditionsHint.ForeColor = Color.DimGray
        LabelConditionsHint.Margin = New Padding(3, 0, 3, 6)
        LabelConditionsHint.Name = "LabelConditionsHint"
        LabelConditionsHint.TabIndex = 0
        '
        ListViewConditions.Dock = DockStyle.Fill
        ListViewConditions.FullRowSelect = True
        ListViewConditions.HideSelection = False
        ListViewConditions.MultiSelect = False
        ListViewConditions.Name = "ListViewConditions"
        ListViewConditions.TabIndex = 1
        ListViewConditions.UseCompatibleStateImageBehavior = False
        ListViewConditions.View = View.Details
        '
        ConditionsButtons.AutoSize = True
        ConditionsButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ConditionsButtons.Controls.Add(ButtonAddCondition)
        ConditionsButtons.Controls.Add(ButtonEditCondition)
        ConditionsButtons.Controls.Add(ButtonRemoveCondition)
        ConditionsButtons.Controls.Add(ButtonConditionUp)
        ConditionsButtons.Controls.Add(ButtonConditionDown)
        ConditionsButtons.Dock = DockStyle.Fill
        ConditionsButtons.Margin = New Padding(0, 4, 0, 0)
        ConditionsButtons.Name = "ConditionsButtons"
        ConditionsButtons.TabIndex = 2
        ConditionsButtons.WrapContents = True
        '
        ButtonAddCondition.AutoSize = True
        ButtonAddCondition.Name = "ButtonAddCondition"
        ButtonAddCondition.TabIndex = 0
        ButtonAddCondition.Text = "Add…"
        '
        ButtonEditCondition.AutoSize = True
        ButtonEditCondition.Name = "ButtonEditCondition"
        ButtonEditCondition.TabIndex = 1
        ButtonEditCondition.Text = "Edit…"
        '
        ButtonRemoveCondition.AutoSize = True
        ButtonRemoveCondition.Name = "ButtonRemoveCondition"
        ButtonRemoveCondition.TabIndex = 2
        ButtonRemoveCondition.Text = "Remove"
        '
        ButtonConditionUp.AutoSize = True
        ButtonConditionUp.Name = "ButtonConditionUp"
        ButtonConditionUp.TabIndex = 3
        ButtonConditionUp.Text = "Move Up"
        '
        ButtonConditionDown.AutoSize = True
        ButtonConditionDown.Name = "ButtonConditionDown"
        ButtonConditionDown.TabIndex = 4
        ButtonConditionDown.Text = "Move Down"
        '
        ' ===================== Panel derecho: preview + validez =====================
        '
        RightLayout.ColumnCount = 1
        RightLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        RightLayout.Controls.Add(PreviewHeader, 0, 0)
        RightLayout.Controls.Add(PreviewControlPanel, 0, 1)
        RightLayout.Controls.Add(LabelPreviewSubject, 0, 2)
        RightLayout.Controls.Add(GroupValidity, 0, 3)
        RightLayout.Dock = DockStyle.Fill
        RightLayout.Name = "RightLayout"
        RightLayout.Padding = New Padding(6, 0, 0, 0)
        RightLayout.RowCount = 4
        RightLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RightLayout.RowStyles.Add(New RowStyle(SizeType.Percent, 100F))
        RightLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RightLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        RightLayout.TabIndex = 0
        '
        ' PreviewHeader — el selector de alcance del preview. «Over the NPC» queda DESHABILITADO cuando
        ' el head part no es compatible con el NPC de contexto (el mismo gateo que
        ' `ArmaEditor_Form.UpdatePreviewScopeGating`): si el motor no lo pondría, el preview no miente y
        ' sólo queda la pieza. «Include Body» vive del mismo lado porque sólo tiene sentido sobre el actor.
        '
        PreviewHeader.AutoSize = True
        PreviewHeader.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PreviewHeader.Controls.Add(LabelPreviewHint)
        PreviewHeader.Controls.Add(RadioPartOnly)
        PreviewHeader.Controls.Add(RadioOverNpc)
        PreviewHeader.Controls.Add(CheckIncludeBody)
        PreviewHeader.Dock = DockStyle.Fill
        PreviewHeader.Margin = New Padding(0)
        PreviewHeader.Name = "PreviewHeader"
        PreviewHeader.TabIndex = 0
        PreviewHeader.WrapContents = True
        '
        LabelPreviewHint.Anchor = AnchorStyles.Left
        LabelPreviewHint.AutoSize = True
        LabelPreviewHint.Margin = New Padding(3, 6, 12, 3)
        LabelPreviewHint.Name = "LabelPreviewHint"
        LabelPreviewHint.TabIndex = 0
        LabelPreviewHint.Text = "Preview"
        '
        RadioPartOnly.Anchor = AnchorStyles.Left
        RadioPartOnly.AutoSize = True
        RadioPartOnly.Checked = True
        RadioPartOnly.Name = "RadioPartOnly"
        RadioPartOnly.TabIndex = 1
        RadioPartOnly.TabStop = True
        RadioPartOnly.Text = "Part only"
        '
        RadioOverNpc.Anchor = AnchorStyles.Left
        RadioOverNpc.AutoSize = True
        RadioOverNpc.Name = "RadioOverNpc"
        RadioOverNpc.TabIndex = 2
        RadioOverNpc.Text = "Over the NPC"
        '
        CheckIncludeBody.Anchor = AnchorStyles.Left
        CheckIncludeBody.AutoSize = True
        CheckIncludeBody.Margin = New Padding(12, 3, 3, 3)
        CheckIncludeBody.Name = "CheckIncludeBody"
        CheckIncludeBody.TabIndex = 3
        CheckIncludeBody.Text = "Include Body"
        '
        PreviewControlPanel.Dock = DockStyle.Fill
        PreviewControlPanel.Margin = New Padding(3, 3, 3, 3)
        PreviewControlPanel.MinimumSize = New Size(0, 320)
        PreviewControlPanel.Name = "PreviewControlPanel"
        PreviewControlPanel.TabIndex = 1
        '
        LabelPreviewSubject.AutoSize = True
        LabelPreviewSubject.Dock = DockStyle.Fill
        LabelPreviewSubject.ForeColor = SystemColors.GrayText
        LabelPreviewSubject.Name = "LabelPreviewSubject"
        LabelPreviewSubject.TabIndex = 2
        '
        GroupValidity.AutoSize = True
        GroupValidity.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupValidity.Controls.Add(ValidityLayout)
        GroupValidity.Dock = DockStyle.Fill
        GroupValidity.Name = "GroupValidity"
        GroupValidity.Padding = New Padding(8, 4, 8, 8)
        GroupValidity.TabIndex = 3
        GroupValidity.TabStop = False
        GroupValidity.Text = "Valid for this NPC?"
        '
        ValidityLayout.AutoSize = True
        ValidityLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ValidityLayout.ColumnCount = 1
        ValidityLayout.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100F))
        ValidityLayout.Controls.Add(LabelValidType, 0, 0)
        ValidityLayout.Controls.Add(LabelValidGender, 0, 1)
        ValidityLayout.Controls.Add(LabelValidNotExtra, 0, 2)
        ValidityLayout.Controls.Add(LabelValidRace, 0, 3)
        ValidityLayout.Controls.Add(LabelValidVerdict, 0, 4)
        ValidityLayout.Controls.Add(ButtonFitToNpc, 0, 5)
        ValidityLayout.Dock = DockStyle.Fill
        ValidityLayout.Name = "ValidityLayout"
        ValidityLayout.RowCount = 6
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        ValidityLayout.TabIndex = 0
        '
        LabelValidType.AutoSize = True
        LabelValidType.Name = "LabelValidType"
        LabelValidType.TabIndex = 0
        '
        LabelValidGender.AutoSize = True
        LabelValidGender.Name = "LabelValidGender"
        LabelValidGender.TabIndex = 1
        '
        LabelValidNotExtra.AutoSize = True
        LabelValidNotExtra.Name = "LabelValidNotExtra"
        LabelValidNotExtra.TabIndex = 2
        '
        LabelValidRace.AutoSize = True
        LabelValidRace.Name = "LabelValidRace"
        LabelValidRace.TabIndex = 3
        '
        ' Sin `MaximumSize`: el tope de 300 px forzaba un ancho mínimo en la columna y peleaba con el
        ' panel. `Dock = Fill` + `AutoSize` lo hace envolver en el ancho que haya y crecer en alto.
        LabelValidVerdict.AutoSize = True
        LabelValidVerdict.Dock = DockStyle.Fill
        LabelValidVerdict.ForeColor = Color.DimGray
        LabelValidVerdict.Margin = New Padding(3, 6, 3, 6)
        LabelValidVerdict.Name = "LabelValidVerdict"
        LabelValidVerdict.TabIndex = 4
        '
        ButtonFitToNpc.Anchor = AnchorStyles.Left
        ButtonFitToNpc.AutoSize = True
        ButtonFitToNpc.Name = "ButtonFitToNpc"
        ButtonFitToNpc.TabIndex = 5
        ButtonFitToNpc.Text = "Fit to this NPC"
        '
        ' ===================== OK / Cancel =====================
        '
        BottomLayout.AutoSize = True
        BottomLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        BottomLayout.Controls.Add(ButtonOk)
        BottomLayout.Controls.Add(ButtonCancel)
        BottomLayout.Dock = DockStyle.Fill
        BottomLayout.FlowDirection = FlowDirection.RightToLeft
        BottomLayout.Margin = New Padding(0, 6, 0, 0)
        BottomLayout.Name = "BottomLayout"
        BottomLayout.TabIndex = 3
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
        ' HeadPartEditor_Form
        '
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New Size(1500, 840)
        Controls.Add(RootLayout)
        MinimumSize = New Size(1180, 720)
        Name = "HeadPartEditor_Form"
        StartPosition = FormStartPosition.CenterParent
        Text = "Head Part Editor"
        RootLayout.ResumeLayout(False)
        TopBar.ResumeLayout(False)
        CType(MainSplit, ComponentModel.ISupportInitialize).EndInit()
        MainSplit.ResumeLayout(False)
        Tabs.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents RootLayout As TableLayoutPanel
    Friend WithEvents TopBar As FlowLayoutPanel
    Friend WithEvents ButtonNewBlank As Button
    Friend WithEvents ButtonNewFromTemplate As Button
    Friend WithEvents ButtonOverrideExisting As Button
    Friend WithEvents ButtonEditMine As Button
    Friend WithEvents LabelEdid As Label
    Friend WithEvents TextBoxEdid As TextBox
    Friend WithEvents LabelStatusBanner As Label
    Friend WithEvents MainSplit As SplitContainer
    Friend WithEvents Tabs As TabControl
    Friend WithEvents TabGeneral As TabPage
    Friend WithEvents GeneralLayout As TableLayoutPanel
    Friend WithEvents GroupIdentity As GroupBox
    Friend WithEvents IdentityLayout As TableLayoutPanel
    Friend WithEvents LabelName As Label
    Friend WithEvents TextBoxName As TextBox
    Friend WithEvents LabelType As Label
    Friend WithEvents ComboType As ComboBox
    Friend WithEvents LabelTypeRaw As Label
    Friend WithEvents CheckNonPlayable As CheckBox
    Friend WithEvents GroupFlags As GroupBox
    Friend WithEvents FlagsLayout As FlowLayoutPanel
    Friend WithEvents CheckPlayable As CheckBox
    Friend WithEvents CheckMale As CheckBox
    Friend WithEvents CheckFemale As CheckBox
    Friend WithEvents CheckIsExtraPart As CheckBox
    Friend WithEvents CheckUseSolidTint As CheckBox
    Friend WithEvents CheckUsesBodyTexture As CheckBox
    Friend WithEvents TabModel As TabPage
    Friend WithEvents ModelLayout As TableLayoutPanel
    Friend WithEvents GroupModel As GroupBox
    Friend WithEvents ModelInnerLayout As TableLayoutPanel
    Friend WithEvents LabelModl As Label
    Friend WithEvents TextBoxModl As TextBox
    Friend WithEvents ButtonBrowseModl As Button
    Friend WithEvents LabelModtStatus As Label
    Friend WithEvents LabelModc As Label
    Friend WithEvents ModcLayout As FlowLayoutPanel
    Friend WithEvents CheckModcPresente As CheckBox
    Friend WithEvents NumericModc As NumericUpDown
    Friend WithEvents LabelModcHint As Label
    Friend WithEvents LabelFullPrec As Label
    Friend WithEvents LabelMods As Label
    Friend WithEvents TextBoxMods As TextBox
    Friend WithEvents ModsButtons As FlowLayoutPanel
    Friend WithEvents ButtonPickMods As Button
    Friend WithEvents ButtonNewMods As Button
    Friend WithEvents GroupModelFlags As GroupBox
    Friend WithEvents GroupAltTex As GroupBox
    Friend WithEvents AltTexInner As TableLayoutPanel
    Friend WithEvents ListViewAltTex As ListView
    Friend WithEvents AltTexButtons As FlowLayoutPanel
    Friend WithEvents ButtonAddAltTex As Button
    Friend WithEvents ButtonEditAltTex As Button
    Friend WithEvents ButtonRemoveAltTex As Button
    Friend WithEvents ModelFlagsLayout As FlowLayoutPanel
    Friend WithEvents CheckHasFaceBones As CheckBox
    Friend WithEvents CheckHas1stPerson As CheckBox
    Friend WithEvents TabExtras As TabPage
    Friend WithEvents ExtrasLayout As TableLayoutPanel
    Friend WithEvents GroupExtras As GroupBox
    Friend WithEvents ExtrasInner As TableLayoutPanel
    Friend WithEvents ListViewExtras As ListView
    Friend WithEvents ExtrasButtons As FlowLayoutPanel
    Friend WithEvents ButtonAddExtra As Button
    Friend WithEvents ButtonNewExtra As Button
    Friend WithEvents ButtonEditExtra As Button
    Friend WithEvents ButtonRemoveExtra As Button
    Friend WithEvents ButtonExtraUp As Button
    Friend WithEvents ButtonExtraDown As Button
    Friend WithEvents GroupParts As GroupBox
    Friend WithEvents PartsInner As TableLayoutPanel
    Friend WithEvents ListViewParts As ListView
    Friend WithEvents PartsButtons As FlowLayoutPanel
    Friend WithEvents ButtonAddPart As Button
    Friend WithEvents ButtonEditPart As Button
    Friend WithEvents ButtonRemovePart As Button
    Friend WithEvents TabRefs As TabPage
    Friend WithEvents RefsLayout As TableLayoutPanel
    Friend WithEvents GroupRefs As GroupBox
    Friend WithEvents RefsInner As TableLayoutPanel
    Friend WithEvents LabelTnam As Label
    Friend WithEvents TextBoxTnam As TextBox
    Friend WithEvents TnamButtons As FlowLayoutPanel
    Friend WithEvents ButtonPickTnam As Button
    Friend WithEvents ButtonNewTnam As Button
    Friend WithEvents LabelCnam As Label
    Friend WithEvents TextBoxCnam As TextBox
    Friend WithEvents CnamButtons As FlowLayoutPanel
    Friend WithEvents ButtonPickCnam As Button
    Friend WithEvents LabelRnam As Label
    Friend WithEvents TextBoxRnam As TextBox
    Friend WithEvents RnamButtons As FlowLayoutPanel
    Friend WithEvents ButtonPickRnam As Button
    Friend WithEvents ButtonNewRnam As Button
    Friend WithEvents GroupRnamRaces As GroupBox
    Friend WithEvents ListBoxRnamRaces As ListBox
    Friend WithEvents TabConditions As TabPage
    Friend WithEvents ConditionsLayout As TableLayoutPanel
    Friend WithEvents LabelConditionsHint As Label
    Friend WithEvents ListViewConditions As ListView
    Friend WithEvents ConditionsButtons As FlowLayoutPanel
    Friend WithEvents ButtonAddCondition As Button
    Friend WithEvents ButtonEditCondition As Button
    Friend WithEvents ButtonRemoveCondition As Button
    Friend WithEvents ButtonConditionUp As Button
    Friend WithEvents ButtonConditionDown As Button
    Friend WithEvents RightLayout As TableLayoutPanel
    Friend WithEvents PreviewHeader As FlowLayoutPanel
    Friend WithEvents LabelPreviewHint As Label
    Friend WithEvents RadioPartOnly As RadioButton
    Friend WithEvents RadioOverNpc As RadioButton
    Friend WithEvents CheckIncludeBody As CheckBox
    Friend WithEvents PreviewControlPanel As Panel
    Friend WithEvents LabelPreviewSubject As Label
    Friend WithEvents GroupValidity As GroupBox
    Friend WithEvents ValidityLayout As TableLayoutPanel
    Friend WithEvents LabelValidType As Label
    Friend WithEvents LabelValidGender As Label
    Friend WithEvents LabelValidNotExtra As Label
    Friend WithEvents LabelValidRace As Label
    Friend WithEvents LabelValidVerdict As Label
    Friend WithEvents ButtonFitToNpc As Button
    Friend WithEvents BottomLayout As FlowLayoutPanel
    Friend WithEvents ButtonOk As Button
    Friend WithEvents ButtonCancel As Button
End Class
