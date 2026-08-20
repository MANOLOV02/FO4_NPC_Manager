<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class BuildProgress_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24): los iconos viven UNA sola vez, en el resx de ese formulario base.
    ' El formulario base NO tiene controles y no fija Size/Text/Icon/AutoScale, asi que heredar de
    ' el no cambia el aspecto de nada. Ver el remarks de IconFormBase.vb.
    ' Los iconos se eligen SIEMPRE por ImageKey, nunca por ImageIndex: el orden del ImageList
    ' compartido se corre solo con agregar un PNG a Resources\Icons.
    Inherits FO4_Base_Library.IconFormBase

    Private components As System.ComponentModel.IContainer

    Protected Overrides Sub Dispose(ByVal disposing As Boolean)
        Try
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private Sub InitializeComponent()
        LabelStatus = New Label()
        ProgressBarMain = New ProgressBar()
        ButtonCancel = New Button()
        SuspendLayout()
        '
        ' LabelStatus
        '
        LabelStatus.AutoEllipsis = True
        LabelStatus.Location = New Point(12, 12)
        LabelStatus.Name = "LabelStatus"
        LabelStatus.Size = New Size(400, 20)
        LabelStatus.TabIndex = 0
        LabelStatus.Text = "Working…"
        '
        ' ProgressBarMain
        '
        ProgressBarMain.Location = New Point(12, 38)
        ProgressBarMain.Name = "ProgressBarMain"
        ProgressBarMain.Size = New Size(400, 22)
        ProgressBarMain.Style = ProgressBarStyle.Continuous
        ProgressBarMain.TabIndex = 1
        '
        ' ButtonCancel
        '
        ButtonCancel.Anchor = AnchorStyles.Bottom Or AnchorStyles.Right
        ButtonCancel.Location = New Point(337, 68)
        ButtonCancel.Name = "ButtonCancel"
        ButtonCancel.Size = New Size(75, 26)
        ButtonCancel.TabIndex = 2
        ButtonCancel.Text = "Cancel"
        ButtonCancel.UseVisualStyleBackColor = True
        '
        ' BuildProgress_Form
        '
        AutoScaleDimensions = New SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Size(424, 104)
        ControlBox = False
        Controls.Add(ButtonCancel)
        Controls.Add(ProgressBarMain)
        Controls.Add(LabelStatus)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "BuildProgress_Form"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterParent
        Text = "Build CharGen (loose)"
        ResumeLayout(False)
    End Sub

    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents ProgressBarMain As System.Windows.Forms.ProgressBar
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
End Class
