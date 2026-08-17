' UI built in Designer per 00-reglas-ui-y-vb (Designer + English). Fixed frame: the dialog has a
' fixed set of options, so resizing could only add dead space. The face-overlay sub-group is hidden
' outside SSE — FO4 bakes the tint INTO the diffuse and has nothing to fold — and the layout is
' AutoSize so the form shrinks by exactly that group's height when it is hidden.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ExportModelOptions_Form
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
        Root = New TableLayoutPanel()
        LeftStack = New TableLayoutPanel()
        GroupGeometry = New GroupBox()
        RadioSkinned = New RadioButton()
        RadioUnskinned = New RadioButton()
        GroupFaceTextures = New GroupBox()
        FaceLayout = New TableLayoutPanel()
        CheckUseFaceGenBake = New CheckBox()
        LabelFaceOff = New Label()
        PanelOverlays = New FlowLayoutPanel()
        RadioWithOverlays = New RadioButton()
        RadioWithoutOverlays = New RadioButton()
        GroupSkinTone = New GroupBox()
        SkinToneLayout = New TableLayoutPanel()
        CheckWriteSkinTone = New CheckBox()
        CheckExportHelperShapes = New CheckBox()
        LabelSkinTone = New Label()
        GroupLoadScreen = New GroupBox()
        LoadScreenLayout = New TableLayoutPanel()
        CheckAddLoadScreenNode = New CheckBox()
        LabelLoadScreen = New Label()
        LoadScreenValues = New TableLayoutPanel()
        LabelHeight = New Label()
        SliderHeight = New TinySlider()
        NumHeightPct = New NumericUpDown()
        LabelPosition = New Label()
        LabelPosX = New Label()
        NumPosX = New NumericUpDown()
        LabelPosY = New Label()
        NumPosY = New NumericUpDown()
        LabelPosZ = New Label()
        NumPosZ = New NumericUpDown()
        LabelRotation = New Label()
        LabelRotX = New Label()
        NumRotX = New NumericUpDown()
        LabelRotY = New Label()
        NumRotY = New NumericUpDown()
        LabelRotZ = New Label()
        NumRotZ = New NumericUpDown()
        LabelScale = New Label()
        NumScale = New NumericUpDown()
        ButtonResetPlacement = New Button()
        LabelBounds = New Label()
        ButtonRow = New FlowLayoutPanel()
        ButtonExport = New Button()
        ButtonCancel = New Button()
        Root.SuspendLayout()
        LeftStack.SuspendLayout()
        GroupGeometry.SuspendLayout()
        GroupFaceTextures.SuspendLayout()
        FaceLayout.SuspendLayout()
        PanelOverlays.SuspendLayout()
        GroupSkinTone.SuspendLayout()
        SkinToneLayout.SuspendLayout()
        GroupLoadScreen.SuspendLayout()
        LoadScreenLayout.SuspendLayout()
        LoadScreenValues.SuspendLayout()
        CType(NumHeightPct, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumPosX, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumPosY, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumPosZ, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumRotX, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumRotY, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumRotZ, System.ComponentModel.ISupportInitialize).BeginInit()
        CType(NumScale, System.ComponentModel.ISupportInitialize).BeginInit()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        ' DOS COLUMNAS: a la izquierda lo que aplica a los dos juegos, a la derecha el bloque del
        ' loading screen (FO4 only). Apilar los cuatro grupos a lo largo daba 785 px de alto, que NO entra
        ' en una pantalla de 768 — y la app se distribuye, así que "en mi equipo entra" no alcanza.
        ' La columna derecha es AutoSize a propósito: cuando GroupLoadScreen se oculta en SSE, colapsa a
        ' ancho 0 y el diálogo queda como estaba antes de esta feature. Con una columna Absolute quedaría
        ' un hueco muerto.
        Root.AutoSize = True
        Root.AutoSizeMode = AutoSizeMode.GrowAndShrink
        Root.ColumnCount = 2
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Root.Controls.Add(LeftStack, 0, 0)
        Root.Controls.Add(GroupLoadScreen, 1, 0)
        Root.Controls.Add(ButtonRow, 0, 1)
        Root.SetColumnSpan(ButtonRow, 2)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(12, 12, 12, 6)
        Root.RowCount = 2
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.TabIndex = 0
        '
        ' LeftStack
        '
        LeftStack.AutoSize = True
        LeftStack.AutoSizeMode = AutoSizeMode.GrowAndShrink
        LeftStack.ColumnCount = 1
        LeftStack.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LeftStack.Controls.Add(GroupGeometry, 0, 0)
        LeftStack.Controls.Add(GroupFaceTextures, 0, 1)
        LeftStack.Controls.Add(GroupSkinTone, 0, 2)
        LeftStack.Margin = New Padding(0)
        LeftStack.Name = "LeftStack"
        LeftStack.RowCount = 3
        LeftStack.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LeftStack.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LeftStack.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LeftStack.TabIndex = 0
        '
        ' GroupGeometry
        '
        GroupGeometry.AutoSize = True
        GroupGeometry.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupGeometry.Controls.Add(CheckExportHelperShapes)
        GroupGeometry.Controls.Add(RadioSkinned)
        GroupGeometry.Controls.Add(RadioUnskinned)
        GroupGeometry.Location = New Point(15, 15)
        GroupGeometry.Margin = New Padding(3, 3, 3, 8)
        GroupGeometry.Name = "GroupGeometry"
        GroupGeometry.Padding = New Padding(10, 6, 10, 10)
        GroupGeometry.MinimumSize = New Size(430, 0)
        GroupGeometry.MaximumSize = New Size(430, 0)
        GroupGeometry.TabIndex = 0
        GroupGeometry.TabStop = False
        GroupGeometry.Text = "Geometry"
        '
        ' RadioSkinned
        '
        RadioSkinned.AutoSize = True
        RadioSkinned.Checked = True
        RadioSkinned.Location = New Point(13, 24)
        RadioSkinned.Name = "RadioSkinned"
        RadioSkinned.Size = New Size(300, 19)
        RadioSkinned.TabIndex = 0
        RadioSkinned.TabStop = True
        RadioSkinned.Text = "Skinned — keep the skeleton and the vertex weights"
        RadioSkinned.UseVisualStyleBackColor = True
        '
        ' RadioUnskinned
        '
        RadioUnskinned.AutoSize = True
        RadioUnskinned.Location = New Point(13, 47)
        RadioUnskinned.Name = "RadioUnskinned"
        RadioUnskinned.Size = New Size(300, 19)
        RadioUnskinned.TabIndex = 1
        RadioUnskinned.Text = "Unskinned — bake the current pose into the vertices"
        RadioUnskinned.UseVisualStyleBackColor = True
        '
        ' CheckExportHelperShapes
        '
        CheckExportHelperShapes.AutoSize = True
        CheckExportHelperShapes.Checked = True
        CheckExportHelperShapes.CheckState = CheckState.Checked
        CheckExportHelperShapes.Location = New Point(13, 74)
        CheckExportHelperShapes.Name = "CheckExportHelperShapes"
        CheckExportHelperShapes.Size = New Size(300, 19)
        CheckExportHelperShapes.TabIndex = 2
        CheckExportHelperShapes.Text = "Export hidden shapes (collisions, markers)"
        CheckExportHelperShapes.UseVisualStyleBackColor = True
        '
        ' GroupFaceTextures
        '
        GroupFaceTextures.AutoSize = True
        GroupFaceTextures.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupFaceTextures.Controls.Add(FaceLayout)
        GroupFaceTextures.Location = New Point(15, 101)
        GroupFaceTextures.Margin = New Padding(3, 3, 3, 8)
        GroupFaceTextures.Name = "GroupFaceTextures"
        GroupFaceTextures.Padding = New Padding(10, 6, 10, 10)
        GroupFaceTextures.MinimumSize = New Size(430, 0)
        GroupFaceTextures.MaximumSize = New Size(430, 0)
        GroupFaceTextures.TabIndex = 1
        GroupFaceTextures.TabStop = False
        GroupFaceTextures.Text = "Face textures"
        '
        ' FaceLayout
        '
        ' ⛔ Filas AutoSize y NO posiciones fijas: el label de abajo envuelve en 2 o 3 renglones según
        ' la fuente/DPI del sistema, y con Y fijas los radios le quedaban ENCIMA. La app se distribuye,
        ' así que "en mi equipo entra" no es un criterio.
        FaceLayout.AutoSize = True
        FaceLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        FaceLayout.ColumnCount = 1
        FaceLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        FaceLayout.Controls.Add(CheckUseFaceGenBake, 0, 0)
        FaceLayout.Controls.Add(LabelFaceOff, 0, 1)
        FaceLayout.Controls.Add(PanelOverlays, 0, 2)
        FaceLayout.Dock = DockStyle.Fill
        FaceLayout.Location = New Point(10, 22)
        FaceLayout.Margin = New Padding(0)
        FaceLayout.Name = "FaceLayout"
        FaceLayout.RowCount = 3
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        FaceLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        FaceLayout.TabIndex = 0
        '
        ' CheckUseFaceGenBake
        '
        CheckUseFaceGenBake.AutoSize = True
        CheckUseFaceGenBake.Checked = True
        CheckUseFaceGenBake.CheckState = CheckState.Checked
        CheckUseFaceGenBake.Margin = New Padding(3, 3, 3, 2)
        CheckUseFaceGenBake.Name = "CheckUseFaceGenBake"
        CheckUseFaceGenBake.TabIndex = 0
        CheckUseFaceGenBake.Text = "Point the face at the FaceGen bake output"
        CheckUseFaceGenBake.UseVisualStyleBackColor = True
        '
        ' LabelFaceOff
        '
        LabelFaceOff.AutoSize = True
        LabelFaceOff.ForeColor = SystemColors.GrayText
        LabelFaceOff.Margin = New Padding(20, 0, 3, 6)
        LabelFaceOff.MaximumSize = New Size(360, 0)
        LabelFaceOff.Name = "LabelFaceOff"
        LabelFaceOff.TabIndex = 1
        LabelFaceOff.Text = "Only rewrites the texture paths — nothing is baked here. Unchecked, the face keeps the paths of the source NIF (vanilla, untinted)."
        '
        ' PanelOverlays
        '
        PanelOverlays.AutoSize = True
        PanelOverlays.AutoSizeMode = AutoSizeMode.GrowAndShrink
        PanelOverlays.Controls.Add(RadioWithOverlays)
        PanelOverlays.Controls.Add(RadioWithoutOverlays)
        PanelOverlays.FlowDirection = FlowDirection.TopDown
        PanelOverlays.Margin = New Padding(20, 0, 3, 3)
        PanelOverlays.Name = "PanelOverlays"
        PanelOverlays.TabIndex = 2
        PanelOverlays.WrapContents = False
        '
        ' RadioWithOverlays
        '
        RadioWithOverlays.AutoSize = True
        RadioWithOverlays.Checked = True
        RadioWithOverlays.Margin = New Padding(3, 2, 3, 2)
        RadioWithOverlays.Name = "RadioWithOverlays"
        RadioWithOverlays.TabIndex = 0
        RadioWithOverlays.TabStop = True
        RadioWithOverlays.Text = "With RaceMenu overlays (folded face diffuse)"
        RadioWithOverlays.UseVisualStyleBackColor = True
        '
        ' RadioWithoutOverlays
        '
        RadioWithoutOverlays.AutoSize = True
        RadioWithoutOverlays.Margin = New Padding(3, 2, 3, 2)
        RadioWithoutOverlays.Name = "RadioWithoutOverlays"
        RadioWithoutOverlays.TabIndex = 1
        RadioWithoutOverlays.Text = "Without overlays (face tint slot, engine path)"
        RadioWithoutOverlays.UseVisualStyleBackColor = True
        '
        ' GroupSkinTone
        '
        GroupSkinTone.AutoSize = True
        GroupSkinTone.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupSkinTone.Controls.Add(SkinToneLayout)
        GroupSkinTone.Margin = New Padding(3, 3, 3, 8)
        GroupSkinTone.Name = "GroupSkinTone"
        GroupSkinTone.Padding = New Padding(10, 6, 10, 10)
        GroupSkinTone.MinimumSize = New Size(430, 0)
        GroupSkinTone.MaximumSize = New Size(430, 0)
        GroupSkinTone.TabIndex = 2
        GroupSkinTone.TabStop = False
        GroupSkinTone.Text = "Skin tone"
        '
        ' SkinToneLayout
        '
        ' Mismo criterio que FaceLayout: filas AutoSize, nunca posiciones fijas — el label envuelve en 2 o
        ' 3 renglones según fuente/DPI, y encima su TEXTO CAMBIA por juego (Prepare), así que la altura no
        ' se puede fijar acá.
        SkinToneLayout.AutoSize = True
        SkinToneLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        SkinToneLayout.ColumnCount = 1
        SkinToneLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        SkinToneLayout.Controls.Add(CheckWriteSkinTone, 0, 0)
        SkinToneLayout.Controls.Add(LabelSkinTone, 0, 1)
        SkinToneLayout.Dock = DockStyle.Fill
        SkinToneLayout.Location = New Point(10, 22)
        SkinToneLayout.Margin = New Padding(0)
        SkinToneLayout.Name = "SkinToneLayout"
        SkinToneLayout.RowCount = 2
        SkinToneLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        SkinToneLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        SkinToneLayout.TabIndex = 0
        '
        ' CheckWriteSkinTone
        '
        CheckWriteSkinTone.AutoSize = True
        CheckWriteSkinTone.Checked = True
        CheckWriteSkinTone.CheckState = CheckState.Checked
        CheckWriteSkinTone.Margin = New Padding(3, 3, 3, 2)
        CheckWriteSkinTone.Name = "CheckWriteSkinTone"
        CheckWriteSkinTone.TabIndex = 0
        CheckWriteSkinTone.Text = "Write the NPC's skin tone into the skin shapes"
        CheckWriteSkinTone.UseVisualStyleBackColor = True
        '
        ' LabelSkinTone
        '
        LabelSkinTone.AutoSize = True
        LabelSkinTone.ForeColor = SystemColors.GrayText
        LabelSkinTone.Margin = New Padding(20, 0, 3, 6)
        LabelSkinTone.MaximumSize = New Size(360, 0)
        LabelSkinTone.Name = "LabelSkinTone"
        LabelSkinTone.TabIndex = 1
        ' El texto lo pone Prepare: la implicación de FO4 (corte del link al material) no aplica en Skyrim.
        LabelSkinTone.Text = ""
        '
        ' GroupLoadScreen
        '
        ' Oculto fuera de FO4 (Prepare): en SSE el nodo no existe en ningún loadscreen vanilla — el
        ' encuadre viaja en el LSCR. Mismo criterio que PanelOverlays, que es SSE-only al revés.
        GroupLoadScreen.AutoSize = True
        GroupLoadScreen.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupLoadScreen.Controls.Add(LoadScreenLayout)
        GroupLoadScreen.Anchor = AnchorStyles.Top Or AnchorStyles.Left
        GroupLoadScreen.Margin = New Padding(6, 3, 3, 8)
        GroupLoadScreen.Name = "GroupLoadScreen"
        GroupLoadScreen.Padding = New Padding(10, 6, 10, 10)
        GroupLoadScreen.MinimumSize = New Size(430, 0)
        GroupLoadScreen.MaximumSize = New Size(430, 0)
        GroupLoadScreen.TabIndex = 3
        GroupLoadScreen.TabStop = False
        GroupLoadScreen.Text = "Loading screen"
        '
        ' LoadScreenLayout
        '
        ' Mismo criterio que FaceLayout/SkinToneLayout: filas AutoSize, nunca posiciones fijas — el
        ' label envuelve en 2 o 3 renglones según fuente/DPI y la app se distribuye.
        LoadScreenLayout.AutoSize = True
        LoadScreenLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink
        LoadScreenLayout.ColumnCount = 1
        LoadScreenLayout.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LoadScreenLayout.Controls.Add(CheckAddLoadScreenNode, 0, 0)
        LoadScreenLayout.Controls.Add(LabelLoadScreen, 0, 1)
        LoadScreenLayout.Controls.Add(LoadScreenValues, 0, 2)
        LoadScreenLayout.Dock = DockStyle.Fill
        LoadScreenLayout.Location = New Point(10, 22)
        LoadScreenLayout.Margin = New Padding(0)
        LoadScreenLayout.Name = "LoadScreenLayout"
        LoadScreenLayout.RowCount = 3
        LoadScreenLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenLayout.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenLayout.TabIndex = 0
        '
        ' LoadScreenValues
        '
        ' GRILLA de 7 columnas, no FlowLayoutPanel. Con flow, la fila de Position y la de Rotation
        ' arrancaban en x distintos y las cajas no caian en columna: se veia desprolijo. Aca las tres
        ' columnas de numeros son Absolute e identicas, asi que X/Y/Z quedan alineados entre filas
        ' pase lo que pase con el ancho de las etiquetas.
        LoadScreenValues.AutoSize = True
        LoadScreenValues.AutoSizeMode = AutoSizeMode.GrowAndShrink
        LoadScreenValues.ColumnCount = 7
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        LoadScreenValues.ColumnStyles.Add(New ColumnStyle(SizeType.Absolute, 70F))
        LoadScreenValues.Controls.Add(LabelHeight, 0, 0)
        LoadScreenValues.Controls.Add(SliderHeight, 1, 0)
        LoadScreenValues.SetColumnSpan(SliderHeight, 5)
        LoadScreenValues.Controls.Add(NumHeightPct, 6, 0)
        LoadScreenValues.Controls.Add(LabelPosition, 0, 1)
        LoadScreenValues.Controls.Add(LabelPosX, 1, 1)
        LoadScreenValues.Controls.Add(NumPosX, 2, 1)
        LoadScreenValues.Controls.Add(LabelPosY, 3, 1)
        LoadScreenValues.Controls.Add(NumPosY, 4, 1)
        LoadScreenValues.Controls.Add(LabelPosZ, 5, 1)
        LoadScreenValues.Controls.Add(NumPosZ, 6, 1)
        LoadScreenValues.Controls.Add(LabelRotation, 0, 2)
        LoadScreenValues.Controls.Add(LabelRotX, 1, 2)
        LoadScreenValues.Controls.Add(NumRotX, 2, 2)
        LoadScreenValues.Controls.Add(LabelRotY, 3, 2)
        LoadScreenValues.Controls.Add(NumRotY, 4, 2)
        LoadScreenValues.Controls.Add(LabelRotZ, 5, 2)
        LoadScreenValues.Controls.Add(NumRotZ, 6, 2)
        LoadScreenValues.Controls.Add(LabelScale, 0, 3)
        LoadScreenValues.Controls.Add(NumScale, 2, 3)
        LoadScreenValues.Controls.Add(ButtonResetPlacement, 6, 3)
        LoadScreenValues.Controls.Add(LabelBounds, 0, 4)
        LoadScreenValues.SetColumnSpan(LabelBounds, 7)
        LoadScreenValues.Margin = New Padding(20, 4, 3, 3)
        LoadScreenValues.Name = "LoadScreenValues"
        LoadScreenValues.RowCount = 5
        LoadScreenValues.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenValues.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenValues.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenValues.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenValues.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        LoadScreenValues.TabIndex = 2
        '
        ' LabelHeight
        '
        LabelHeight.Anchor = AnchorStyles.Left
        LabelHeight.AutoSize = True
        LabelHeight.Margin = New Padding(0, 3, 10, 3)
        LabelHeight.Name = "LabelHeight"
        LabelHeight.TabIndex = 0
        LabelHeight.Text = "Z as % of height"
        '
        ' SliderHeight
        '
        ' No es un campo del NIF: mueve Position Z, expresada como % del alto del modelo (0 = base
        ' del bounding box, 100 = tope). Reemplaza a un TrackBar, que reservaba lugar para las marcas
        ' y hacia crecer la fila a ~28 px sin alinear con las cajas de al lado.
        SliderHeight.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        SliderHeight.Margin = New Padding(0, 2, 10, 2)
        SliderHeight.Maximum = 100
        SliderHeight.Minimum = 0
        SliderHeight.Name = "SliderHeight"
        SliderHeight.Size = New Size(200, 20)
        SliderHeight.TabIndex = 1
        SliderHeight.Value = 85
        '
        ' NumHeightPct
        '
        NumHeightPct.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumHeightPct.DecimalPlaces = 0
        NumHeightPct.Margin = New Padding(0, 2, 10, 2)
        NumHeightPct.Maximum = 100D
        NumHeightPct.Minimum = 0D
        NumHeightPct.Name = "NumHeightPct"
        NumHeightPct.TabIndex = 2
        NumHeightPct.TextAlign = HorizontalAlignment.Right
        NumHeightPct.Value = 85D
        '
        ' LabelPosition
        '
        LabelPosition.Anchor = AnchorStyles.Left
        LabelPosition.AutoSize = True
        LabelPosition.Margin = New Padding(0, 3, 10, 3)
        LabelPosition.Name = "LabelPosition"
        LabelPosition.TabIndex = 3
        LabelPosition.Text = "Position"
        '
        ' LabelPosX
        '
        LabelPosX.Anchor = AnchorStyles.Right
        LabelPosX.AutoSize = True
        LabelPosX.Margin = New Padding(0, 3, 4, 3)
        LabelPosX.Name = "LabelPosX"
        LabelPosX.TabIndex = 4
        LabelPosX.Text = "X"
        '
        ' NumPosX
        '
        NumPosX.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumPosX.DecimalPlaces = 2
        NumPosX.Margin = New Padding(0, 2, 10, 2)
        NumPosX.Maximum = 100000D
        NumPosX.Minimum = -100000D
        NumPosX.Name = "NumPosX"
        NumPosX.TabIndex = 5
        NumPosX.TextAlign = HorizontalAlignment.Right
        '
        ' LabelPosY
        '
        LabelPosY.Anchor = AnchorStyles.Right
        LabelPosY.AutoSize = True
        LabelPosY.Margin = New Padding(0, 3, 4, 3)
        LabelPosY.Name = "LabelPosY"
        LabelPosY.TabIndex = 6
        LabelPosY.Text = "Y"
        '
        ' NumPosY
        '
        NumPosY.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumPosY.DecimalPlaces = 2
        NumPosY.Margin = New Padding(0, 2, 10, 2)
        NumPosY.Maximum = 100000D
        NumPosY.Minimum = -100000D
        NumPosY.Name = "NumPosY"
        NumPosY.TabIndex = 7
        NumPosY.TextAlign = HorizontalAlignment.Right
        '
        ' LabelPosZ
        '
        LabelPosZ.Anchor = AnchorStyles.Right
        LabelPosZ.AutoSize = True
        LabelPosZ.Margin = New Padding(0, 3, 4, 3)
        LabelPosZ.Name = "LabelPosZ"
        LabelPosZ.TabIndex = 8
        LabelPosZ.Text = "Z"
        '
        ' NumPosZ
        '
        NumPosZ.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumPosZ.DecimalPlaces = 2
        NumPosZ.Margin = New Padding(0, 2, 10, 2)
        NumPosZ.Maximum = 100000D
        NumPosZ.Minimum = -100000D
        NumPosZ.Name = "NumPosZ"
        NumPosZ.TabIndex = 9
        NumPosZ.TextAlign = HorizontalAlignment.Right
        '
        ' LabelRotation
        '
        LabelRotation.Anchor = AnchorStyles.Left
        LabelRotation.AutoSize = True
        LabelRotation.Margin = New Padding(0, 3, 10, 3)
        LabelRotation.Name = "LabelRotation"
        LabelRotation.TabIndex = 10
        LabelRotation.Text = "Rotation (deg)"
        '
        ' LabelRotX
        '
        LabelRotX.Anchor = AnchorStyles.Right
        LabelRotX.AutoSize = True
        LabelRotX.Margin = New Padding(0, 3, 4, 3)
        LabelRotX.Name = "LabelRotX"
        LabelRotX.TabIndex = 11
        LabelRotX.Text = "X"
        '
        ' NumRotX
        '
        NumRotX.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumRotX.DecimalPlaces = 1
        NumRotX.Margin = New Padding(0, 2, 10, 2)
        NumRotX.Maximum = 360D
        NumRotX.Minimum = -360D
        NumRotX.Name = "NumRotX"
        NumRotX.TabIndex = 12
        NumRotX.TextAlign = HorizontalAlignment.Right
        '
        ' LabelRotY
        '
        LabelRotY.Anchor = AnchorStyles.Right
        LabelRotY.AutoSize = True
        LabelRotY.Margin = New Padding(0, 3, 4, 3)
        LabelRotY.Name = "LabelRotY"
        LabelRotY.TabIndex = 13
        LabelRotY.Text = "Y"
        '
        ' NumRotY
        '
        NumRotY.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumRotY.DecimalPlaces = 1
        NumRotY.Margin = New Padding(0, 2, 10, 2)
        NumRotY.Maximum = 360D
        NumRotY.Minimum = -360D
        NumRotY.Name = "NumRotY"
        NumRotY.TabIndex = 14
        NumRotY.TextAlign = HorizontalAlignment.Right
        '
        ' LabelRotZ
        '
        LabelRotZ.Anchor = AnchorStyles.Right
        LabelRotZ.AutoSize = True
        LabelRotZ.Margin = New Padding(0, 3, 4, 3)
        LabelRotZ.Name = "LabelRotZ"
        LabelRotZ.TabIndex = 15
        LabelRotZ.Text = "Z"
        '
        ' NumRotZ
        '
        NumRotZ.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumRotZ.DecimalPlaces = 1
        NumRotZ.Margin = New Padding(0, 2, 10, 2)
        NumRotZ.Maximum = 360D
        NumRotZ.Minimum = -360D
        NumRotZ.Name = "NumRotZ"
        NumRotZ.TabIndex = 16
        NumRotZ.TextAlign = HorizontalAlignment.Right
        '
        ' LabelScale
        '
        LabelScale.Anchor = AnchorStyles.Left
        LabelScale.AutoSize = True
        LabelScale.Margin = New Padding(0, 3, 10, 3)
        LabelScale.Name = "LabelScale"
        LabelScale.TabIndex = 17
        LabelScale.Text = "Scale"
        '
        ' NumScale
        '
        ' Minimo > 0: escala 0 colapsa el transform y el nodo deja de ser representable como pivote.
        NumScale.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        NumScale.DecimalPlaces = 3
        NumScale.Increment = 0.1D
        NumScale.Margin = New Padding(0, 2, 10, 2)
        NumScale.Maximum = 1000D
        NumScale.Minimum = 0.001D
        NumScale.Name = "NumScale"
        NumScale.TabIndex = 18
        NumScale.TextAlign = HorizontalAlignment.Right
        NumScale.Value = 1D
        '
        ' ButtonResetPlacement
        '
        ButtonResetPlacement.Anchor = AnchorStyles.Left Or AnchorStyles.Right
        ButtonResetPlacement.Margin = New Padding(0, 1, 10, 1)
        ButtonResetPlacement.Name = "ButtonResetPlacement"
        ButtonResetPlacement.TabIndex = 19
        ButtonResetPlacement.Text = "Reset"
        ButtonResetPlacement.UseVisualStyleBackColor = True
        '
        ' LabelBounds
        '
        LabelBounds.AutoSize = True
        LabelBounds.ForeColor = SystemColors.GrayText
        LabelBounds.Margin = New Padding(0, 8, 3, 0)
        LabelBounds.MaximumSize = New Size(390, 0)
        LabelBounds.Name = "LabelBounds"
        LabelBounds.TabIndex = 20
        LabelBounds.Text = ""
        '
        ' CheckAddLoadScreenNode
        '
        CheckAddLoadScreenNode.AutoSize = True
        CheckAddLoadScreenNode.Margin = New Padding(3, 3, 3, 2)
        CheckAddLoadScreenNode.Name = "CheckAddLoadScreenNode"
        CheckAddLoadScreenNode.TabIndex = 0
        CheckAddLoadScreenNode.Text = "Add the LoadingMenuZoomTarget node"
        CheckAddLoadScreenNode.UseVisualStyleBackColor = True
        '
        ' LabelLoadScreen
        '
        ' El texto lo pone Prepare/UpdateLoadScreenGate: la geometría elegida le agrega (o no) la
        ' salvedad del skin, así que fijarlo acá sería mentir en uno de los dos estados.
        LabelLoadScreen.AutoSize = True
        LabelLoadScreen.ForeColor = SystemColors.GrayText
        LabelLoadScreen.Margin = New Padding(20, 0, 3, 6)
        LabelLoadScreen.MaximumSize = New Size(390, 0)
        LabelLoadScreen.Name = "LabelLoadScreen"
        LabelLoadScreen.TabIndex = 1
        LabelLoadScreen.Text = ""
        '
        ' ButtonRow
        '
        ButtonRow.AutoSize = True
        ButtonRow.AutoSizeMode = AutoSizeMode.GrowAndShrink
        ButtonRow.Controls.Add(ButtonExport)
        ButtonRow.Controls.Add(ButtonCancel)
        ButtonRow.Dock = DockStyle.Fill
        ButtonRow.FlowDirection = FlowDirection.RightToLeft
        ButtonRow.Location = New Point(15, 254)
        ButtonRow.Margin = New Padding(3, 3, 3, 0)
        ButtonRow.Name = "ButtonRow"
        ButtonRow.Padding = New Padding(0, 4, 0, 0)
        ButtonRow.Size = New Size(400, 33)
        ButtonRow.TabIndex = 4
        '
        ' ButtonExport
        '
        ButtonExport.DialogResult = DialogResult.OK
        ButtonExport.Location = New Point(317, 7)
        ButtonExport.Name = "ButtonExport"
        ButtonExport.Size = New Size(80, 23)
        ButtonExport.TabIndex = 0
        ButtonExport.Text = "Export"
        ButtonExport.UseVisualStyleBackColor = True
        '
        ' ButtonCancel
        '
        ButtonCancel.DialogResult = DialogResult.Cancel
        ButtonCancel.Location = New Point(231, 7)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(80, 23)
        ButtonCancel.TabIndex = 1
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' ExportModelOptions_Form
        '
        AcceptButton = ButtonExport
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        AutoSize = True
        AutoSizeMode = AutoSizeMode.GrowAndShrink
        CancelButton = ButtonCancel
        ClientSize = New Size(430, 300)
        Controls.Add(Root)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "ExportModelOptions_Form"
        ShowIcon = False
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Export NPC Model to NIF"
        Root.ResumeLayout(False)
        Root.PerformLayout()
        LeftStack.ResumeLayout(False)
        LeftStack.PerformLayout()
        GroupGeometry.ResumeLayout(False)
        GroupGeometry.PerformLayout()
        GroupFaceTextures.ResumeLayout(False)
        GroupFaceTextures.PerformLayout()
        FaceLayout.ResumeLayout(False)
        FaceLayout.PerformLayout()
        PanelOverlays.ResumeLayout(False)
        PanelOverlays.PerformLayout()
        GroupSkinTone.ResumeLayout(False)
        GroupSkinTone.PerformLayout()
        SkinToneLayout.ResumeLayout(False)
        SkinToneLayout.PerformLayout()
        GroupLoadScreen.ResumeLayout(False)
        GroupLoadScreen.PerformLayout()
        LoadScreenLayout.ResumeLayout(False)
        LoadScreenLayout.PerformLayout()
        CType(NumHeightPct, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumPosX, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumPosY, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumPosZ, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumRotX, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumRotY, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumRotZ, System.ComponentModel.ISupportInitialize).EndInit()
        CType(NumScale, System.ComponentModel.ISupportInitialize).EndInit()
        LoadScreenValues.ResumeLayout(False)
        LoadScreenValues.PerformLayout()
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LeftStack As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents GroupGeometry As System.Windows.Forms.GroupBox
    Friend WithEvents RadioSkinned As System.Windows.Forms.RadioButton
    Friend WithEvents RadioUnskinned As System.Windows.Forms.RadioButton
    Friend WithEvents GroupFaceTextures As System.Windows.Forms.GroupBox
    Friend WithEvents FaceLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckUseFaceGenBake As System.Windows.Forms.CheckBox
    Friend WithEvents LabelFaceOff As System.Windows.Forms.Label
    Friend WithEvents PanelOverlays As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents RadioWithOverlays As System.Windows.Forms.RadioButton
    Friend WithEvents RadioWithoutOverlays As System.Windows.Forms.RadioButton
    Friend WithEvents GroupSkinTone As System.Windows.Forms.GroupBox
    Friend WithEvents SkinToneLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckWriteSkinTone As System.Windows.Forms.CheckBox
    Friend WithEvents CheckExportHelperShapes As System.Windows.Forms.CheckBox
    Friend WithEvents LabelSkinTone As System.Windows.Forms.Label
    Friend WithEvents GroupLoadScreen As System.Windows.Forms.GroupBox
    Friend WithEvents LoadScreenLayout As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents CheckAddLoadScreenNode As System.Windows.Forms.CheckBox
    Friend WithEvents LabelLoadScreen As System.Windows.Forms.Label
    Friend WithEvents LoadScreenValues As System.Windows.Forms.TableLayoutPanel
    Friend WithEvents LabelHeight As System.Windows.Forms.Label
    Friend WithEvents SliderHeight As TinySlider
    Friend WithEvents NumHeightPct As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelPosition As System.Windows.Forms.Label
    Friend WithEvents LabelPosX As System.Windows.Forms.Label
    Friend WithEvents NumPosX As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelPosY As System.Windows.Forms.Label
    Friend WithEvents NumPosY As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelPosZ As System.Windows.Forms.Label
    Friend WithEvents NumPosZ As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelRotation As System.Windows.Forms.Label
    Friend WithEvents LabelRotX As System.Windows.Forms.Label
    Friend WithEvents NumRotX As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelRotY As System.Windows.Forms.Label
    Friend WithEvents NumRotY As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelRotZ As System.Windows.Forms.Label
    Friend WithEvents NumRotZ As System.Windows.Forms.NumericUpDown
    Friend WithEvents LabelScale As System.Windows.Forms.Label
    Friend WithEvents NumScale As System.Windows.Forms.NumericUpDown
    Friend WithEvents ButtonResetPlacement As System.Windows.Forms.Button
    Friend WithEvents LabelBounds As System.Windows.Forms.Label
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonExport As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
