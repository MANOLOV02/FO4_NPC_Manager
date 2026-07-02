<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Public Class MeshPicker_Form
    Inherits System.Windows.Forms.Form

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
        SplitMain.Size = New Size(1041, 589)
        SplitMain.TabIndex = 0
        '
        ' DictionaryPicker_Control1
        '
        DictionaryPicker_Control1.Dock = DockStyle.Fill
        DictionaryPicker_Control1.Location = New Point(0, 0)
        DictionaryPicker_Control1.Name = "DictionaryPicker_Control1"
        DictionaryPicker_Control1.Size = New Size(468, 589)
        DictionaryPicker_Control1.TabIndex = 0
        '
        ' PreviewHostPanel
        '
        PreviewHostPanel.BorderStyle = BorderStyle.FixedSingle
        PreviewHostPanel.Dock = DockStyle.Fill
        PreviewHostPanel.Location = New Point(0, 0)
        PreviewHostPanel.Name = "PreviewHostPanel"
        PreviewHostPanel.Size = New Size(569, 589)
        PreviewHostPanel.TabIndex = 0
        '
        ' MeshPicker_Form
        '
        AutoScaleDimensions = New SizeF(7F, 15F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(1041, 589)
        Controls.Add(SplitMain)
        MaximizeBox = False
        MinimizeBox = False
        MinimumSize = New Size(700, 350)
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
