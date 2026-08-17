<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Public Class MeshPicker_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' ⛔ Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

    'Descartar overrides de Dispose para limpiar la lista de componentes.
    <System.Diagnostics.DebuggerNonUserCode()>
    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    'Requerido por el Diseñador de Windows Forms
    Private components As System.ComponentModel.IContainer

    'NOTA: el Diseñador necesita el siguiente procedimiento
    'Se puede modificar usando el Diseñador de Windows Forms.
    'No lo modifiques con el editor de código.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        SplitMain = New SplitContainer()
        DictionaryPicker_Control1 = New DictionaryPicker_Control()
        PreviewHostPanel = New Panel()
        CType(SplitMain, System.ComponentModel.ISupportInitialize).BeginInit()
        SplitMain.Panel1.SuspendLayout()
        SplitMain.Panel2.SuspendLayout()
        SplitMain.SuspendLayout()
        SuspendLayout()
        '
        ' SplitMain
        '
        SplitMain.Dock = DockStyle.Fill
        SplitMain.Location = New Point(0, 0)
        SplitMain.Name = "SplitMain"
        '
        ' SplitMain.Panel1
        '
        SplitMain.Panel1.Controls.Add(DictionaryPicker_Control1)
        '
        ' SplitMain.Panel2
        '
        SplitMain.Panel2.Controls.Add(PreviewHostPanel)
        SplitMain.Size = New Size(1264, 681)
        ' SplitterDistance AFTER Size so the SplitContainer validates against the final width (else it throws /
        ' clamps against a transient small size). Tree+files (Panel1) get ~800px; the preview (Panel2) keeps ~460.
        SplitMain.SplitterDistance = 800
        SplitMain.TabIndex = 0
        '
        ' DictionaryPicker_Control1
        '
        DictionaryPicker_Control1.Dock = DockStyle.Fill
        DictionaryPicker_Control1.Location = New Point(0, 0)
        DictionaryPicker_Control1.Name = "DictionaryPicker_Control1"
        DictionaryPicker_Control1.Size = New Size(800, 681)
        DictionaryPicker_Control1.TabIndex = 0
        '
        ' PreviewHostPanel
        '
        PreviewHostPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewHostPanel.Dock = DockStyle.Fill
        PreviewHostPanel.Location = New Point(0, 0)
        PreviewHostPanel.Name = "PreviewHostPanel"
        PreviewHostPanel.Size = New Size(460, 681)
        PreviewHostPanel.TabIndex = 0
        '
        ' MeshPicker_Form
        '
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1264, 681)
        Controls.Add(SplitMain)
        MaximizeBox = True
        MinimizeBox = False
        MinimumSize = New Size(900, 500)
        Name = "MeshPicker_Form"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Select mesh (.nif) — preview"
        SplitMain.Panel1.ResumeLayout(False)
        SplitMain.Panel2.ResumeLayout(False)
        CType(SplitMain, System.ComponentModel.ISupportInitialize).EndInit()
        SplitMain.ResumeLayout(False)
        ResumeLayout(False)
    End Sub

    Friend WithEvents SplitMain As System.Windows.Forms.SplitContainer
    Public WithEvents DictionaryPicker_Control1 As DictionaryPicker_Control
    Friend WithEvents PreviewHostPanel As System.Windows.Forms.Panel
End Class
