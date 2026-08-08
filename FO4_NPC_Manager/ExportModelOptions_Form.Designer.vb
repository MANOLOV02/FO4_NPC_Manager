' UI built in Designer per 00-reglas-ui-y-vb (Designer + English). Fixed frame: the dialog has a
' fixed set of options, so resizing could only add dead space. The face-overlay sub-group is hidden
' outside SSE — FO4 bakes the tint INTO the diffuse and has nothing to fold — and the layout is
' AutoSize so the form shrinks by exactly that group's height when it is hidden.
<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class ExportModelOptions_Form
    Inherits System.Windows.Forms.Form

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
        LabelSkinTone = New Label()
        ButtonRow = New FlowLayoutPanel()
        ButtonExport = New Button()
        ButtonCancel = New Button()
        Root.SuspendLayout()
        GroupGeometry.SuspendLayout()
        GroupFaceTextures.SuspendLayout()
        FaceLayout.SuspendLayout()
        PanelOverlays.SuspendLayout()
        GroupSkinTone.SuspendLayout()
        SkinToneLayout.SuspendLayout()
        ButtonRow.SuspendLayout()
        SuspendLayout()
        '
        ' Root
        '
        Root.AutoSize = True
        Root.AutoSizeMode = AutoSizeMode.GrowAndShrink
        Root.ColumnCount = 1
        Root.ColumnStyles.Add(New ColumnStyle(SizeType.AutoSize))
        Root.Controls.Add(GroupGeometry, 0, 0)
        Root.Controls.Add(GroupFaceTextures, 0, 1)
        Root.Controls.Add(GroupSkinTone, 0, 2)
        Root.Controls.Add(ButtonRow, 0, 3)
        Root.Dock = DockStyle.Fill
        Root.Location = New Point(0, 0)
        Root.Name = "Root"
        Root.Padding = New Padding(12, 12, 12, 6)
        Root.RowCount = 4
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        Root.TabIndex = 0
        '
        ' GroupGeometry
        '
        GroupGeometry.AutoSize = True
        GroupGeometry.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupGeometry.Controls.Add(RadioSkinned)
        GroupGeometry.Controls.Add(RadioUnskinned)
        GroupGeometry.Location = New Point(15, 15)
        GroupGeometry.Margin = New Padding(3, 3, 3, 8)
        GroupGeometry.Name = "GroupGeometry"
        GroupGeometry.Padding = New Padding(10, 6, 10, 10)
        GroupGeometry.Size = New Size(400, 78)
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
        ' GroupFaceTextures
        '
        GroupFaceTextures.AutoSize = True
        GroupFaceTextures.AutoSizeMode = AutoSizeMode.GrowAndShrink
        GroupFaceTextures.Controls.Add(FaceLayout)
        GroupFaceTextures.Location = New Point(15, 101)
        GroupFaceTextures.Margin = New Padding(3, 3, 3, 8)
        GroupFaceTextures.Name = "GroupFaceTextures"
        GroupFaceTextures.Padding = New Padding(10, 6, 10, 10)
        GroupFaceTextures.Size = New Size(400, 145)
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
        GroupSkinTone.Size = New Size(400, 90)
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
        ButtonRow.TabIndex = 3
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
        ButtonRow.ResumeLayout(False)
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents Root As System.Windows.Forms.TableLayoutPanel
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
    Friend WithEvents LabelSkinTone As System.Windows.Forms.Label
    Friend WithEvents ButtonRow As System.Windows.Forms.FlowLayoutPanel
    Friend WithEvents ButtonExport As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
