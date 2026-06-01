<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class CharGenOptionsForm
    Inherits System.Windows.Forms.Form

    'Form overrides dispose to clean up the component list.
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

    Private components As System.ComponentModel.IContainer

    'NOTE: The following procedure is required by the Windows Form Designer
    'It can be modified using the Windows Form Designer.
    'Do not modify it using the code editor.
    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        GroupBoxSize = New System.Windows.Forms.GroupBox()
        RadioAll = New System.Windows.Forms.RadioButton()
        RadioPerLayer = New System.Windows.Forms.RadioButton()
        LabelDiffuse = New System.Windows.Forms.Label()
        ComboDiffuse = New System.Windows.Forms.ComboBox()
        LabelNormal = New System.Windows.Forms.Label()
        ComboNormal = New System.Windows.Forms.ComboBox()
        LabelSpecular = New System.Windows.Forms.Label()
        ComboSpecular = New System.Windows.Forms.ComboBox()
        LabelFormat = New System.Windows.Forms.Label()
        ComboFormat = New System.Windows.Forms.ComboBox()
        ButtonOK = New System.Windows.Forms.Button()
        ButtonCancel = New System.Windows.Forms.Button()
        GroupBoxSize.SuspendLayout()
        SuspendLayout()
        '
        'GroupBoxSize
        '
        GroupBoxSize.Controls.Add(RadioAll)
        GroupBoxSize.Controls.Add(RadioPerLayer)
        GroupBoxSize.Controls.Add(LabelDiffuse)
        GroupBoxSize.Controls.Add(ComboDiffuse)
        GroupBoxSize.Controls.Add(LabelNormal)
        GroupBoxSize.Controls.Add(ComboNormal)
        GroupBoxSize.Controls.Add(LabelSpecular)
        GroupBoxSize.Controls.Add(ComboSpecular)
        GroupBoxSize.Location = New System.Drawing.Point(12, 12)
        GroupBoxSize.Name = "GroupBoxSize"
        GroupBoxSize.Size = New System.Drawing.Size(312, 152)
        GroupBoxSize.TabIndex = 0
        GroupBoxSize.TabStop = False
        GroupBoxSize.Text = "Texture size (per channel)"
        '
        'RadioAll
        '
        RadioAll.AutoSize = True
        RadioAll.Location = New System.Drawing.Point(12, 24)
        RadioAll.Name = "RadioAll"
        RadioAll.Size = New System.Drawing.Size(98, 19)
        RadioAll.TabIndex = 0
        RadioAll.TabStop = True
        RadioAll.Text = "All (uniform)"
        RadioAll.UseVisualStyleBackColor = True
        '
        'RadioPerLayer
        '
        RadioPerLayer.AutoSize = True
        RadioPerLayer.Location = New System.Drawing.Point(150, 24)
        RadioPerLayer.Name = "RadioPerLayer"
        RadioPerLayer.Size = New System.Drawing.Size(78, 19)
        RadioPerLayer.TabIndex = 1
        RadioPerLayer.Text = "Per layer"
        RadioPerLayer.UseVisualStyleBackColor = True
        '
        'LabelDiffuse
        '
        LabelDiffuse.AutoSize = True
        LabelDiffuse.Location = New System.Drawing.Point(12, 58)
        LabelDiffuse.Name = "LabelDiffuse"
        LabelDiffuse.Size = New System.Drawing.Size(48, 15)
        LabelDiffuse.TabIndex = 2
        LabelDiffuse.Text = "Diffuse"
        '
        'ComboDiffuse
        '
        ComboDiffuse.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        ComboDiffuse.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboDiffuse.Location = New System.Drawing.Point(100, 55)
        ComboDiffuse.Name = "ComboDiffuse"
        ComboDiffuse.Size = New System.Drawing.Size(190, 23)
        ComboDiffuse.TabIndex = 3
        '
        'LabelNormal
        '
        LabelNormal.AutoSize = True
        LabelNormal.Location = New System.Drawing.Point(12, 90)
        LabelNormal.Name = "LabelNormal"
        LabelNormal.Size = New System.Drawing.Size(48, 15)
        LabelNormal.TabIndex = 4
        LabelNormal.Text = "Normal"
        '
        'ComboNormal
        '
        ComboNormal.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        ComboNormal.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboNormal.Location = New System.Drawing.Point(100, 87)
        ComboNormal.Name = "ComboNormal"
        ComboNormal.Size = New System.Drawing.Size(190, 23)
        ComboNormal.TabIndex = 5
        '
        'LabelSpecular
        '
        LabelSpecular.AutoSize = True
        LabelSpecular.Location = New System.Drawing.Point(12, 122)
        LabelSpecular.Name = "LabelSpecular"
        LabelSpecular.Size = New System.Drawing.Size(56, 15)
        LabelSpecular.TabIndex = 6
        LabelSpecular.Text = "Specular"
        '
        'ComboSpecular
        '
        ComboSpecular.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        ComboSpecular.Items.AddRange(New Object() {"Inherit (native)", "512", "1024", "2048", "4096", "8192"})
        ComboSpecular.Location = New System.Drawing.Point(100, 119)
        ComboSpecular.Name = "ComboSpecular"
        ComboSpecular.Size = New System.Drawing.Size(190, 23)
        ComboSpecular.TabIndex = 7
        '
        'LabelFormat
        '
        LabelFormat.AutoSize = True
        LabelFormat.Location = New System.Drawing.Point(12, 178)
        LabelFormat.Name = "LabelFormat"
        LabelFormat.Size = New System.Drawing.Size(89, 15)
        LabelFormat.TabIndex = 1
        LabelFormat.Text = "Diffuse format"
        '
        'ComboFormat
        '
        ComboFormat.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList
        ComboFormat.Items.AddRange(New Object() {"BC3 (default)", "BC7"})
        ComboFormat.Location = New System.Drawing.Point(112, 175)
        ComboFormat.Name = "ComboFormat"
        ComboFormat.Size = New System.Drawing.Size(130, 23)
        ComboFormat.TabIndex = 2
        '
        'ButtonOK
        '
        ButtonOK.Location = New System.Drawing.Point(158, 218)
        ButtonOK.Name = "ButtonOK"
        ButtonOK.Size = New System.Drawing.Size(78, 26)
        ButtonOK.TabIndex = 3
        ButtonOK.Text = "OK"
        ButtonOK.UseVisualStyleBackColor = True
        '
        'ButtonCancel
        '
        ButtonCancel.DialogResult = System.Windows.Forms.DialogResult.Cancel
        ButtonCancel.Location = New System.Drawing.Point(246, 218)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New System.Drawing.Size(78, 26)
        ButtonCancel.TabIndex = 4
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        'CharGenOptionsForm
        '
        AcceptButton = ButtonOK
        AutoScaleDimensions = New System.Drawing.SizeF(7.0F, 15.0F)
        AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        CancelButton = ButtonCancel
        ClientSize = New System.Drawing.Size(336, 256)
        Controls.Add(GroupBoxSize)
        Controls.Add(LabelFormat)
        Controls.Add(ComboFormat)
        Controls.Add(ButtonOK)
        Controls.Add(ButtonCancel)
        FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "CharGenOptionsForm"
        StartPosition = System.Windows.Forms.FormStartPosition.CenterParent
        Text = "CharGen Options"
        GroupBoxSize.ResumeLayout(False)
        GroupBoxSize.PerformLayout()
        ResumeLayout(False)
        PerformLayout()
    End Sub

    Friend WithEvents GroupBoxSize As System.Windows.Forms.GroupBox
    Friend WithEvents RadioAll As System.Windows.Forms.RadioButton
    Friend WithEvents RadioPerLayer As System.Windows.Forms.RadioButton
    Friend WithEvents LabelDiffuse As System.Windows.Forms.Label
    Friend WithEvents ComboDiffuse As System.Windows.Forms.ComboBox
    Friend WithEvents LabelNormal As System.Windows.Forms.Label
    Friend WithEvents ComboNormal As System.Windows.Forms.ComboBox
    Friend WithEvents LabelSpecular As System.Windows.Forms.Label
    Friend WithEvents ComboSpecular As System.Windows.Forms.ComboBox
    Friend WithEvents LabelFormat As System.Windows.Forms.Label
    Friend WithEvents ComboFormat As System.Windows.Forms.ComboBox
    Friend WithEvents ButtonOK As System.Windows.Forms.Button
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
