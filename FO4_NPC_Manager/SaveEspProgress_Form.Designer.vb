<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class SaveEspProgress_Form
    Inherits System.Windows.Forms.Form

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
        LabelStage = New Label()
        LabelDetail = New Label()
        ProgressBarMain = New ProgressBar()
        SuspendLayout()
        '
        ' LabelStage
        '
        LabelStage.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelStage.Font = New Drawing.Font("Segoe UI", 9.0F, Drawing.FontStyle.Bold)
        LabelStage.Location = New Drawing.Point(12, 12)
        LabelStage.Name = "LabelStage"
        LabelStage.Size = New Drawing.Size(440, 22)
        LabelStage.TabIndex = 0
        LabelStage.Text = "Starting…"
        '
        ' LabelDetail
        '
        LabelDetail.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        LabelDetail.AutoEllipsis = True
        LabelDetail.ForeColor = SystemColors.GrayText
        LabelDetail.Location = New Drawing.Point(12, 38)
        LabelDetail.Name = "LabelDetail"
        LabelDetail.Size = New Drawing.Size(440, 18)
        LabelDetail.TabIndex = 1
        LabelDetail.Text = ""
        '
        ' ProgressBarMain
        '
        ProgressBarMain.Anchor = AnchorStyles.Top Or AnchorStyles.Left Or AnchorStyles.Right
        ProgressBarMain.Location = New Drawing.Point(12, 64)
        ProgressBarMain.Name = "ProgressBarMain"
        ProgressBarMain.Size = New Drawing.Size(440, 18)
        ProgressBarMain.Style = ProgressBarStyle.Marquee
        ProgressBarMain.TabIndex = 2
        '
        ' SaveEspProgress_Form
        '
        AutoScaleDimensions = New Drawing.SizeF(7.0F, 15.0F)
        AutoScaleMode = AutoScaleMode.Font
        ClientSize = New Drawing.Size(464, 100)
        ControlBox = False
        Controls.Add(LabelStage)
        Controls.Add(LabelDetail)
        Controls.Add(ProgressBarMain)
        FormBorderStyle = FormBorderStyle.FixedDialog
        MaximizeBox = False
        MinimizeBox = False
        Name = "SaveEspProgress_Form"
        ShowInTaskbar = False
        StartPosition = FormStartPosition.CenterScreen
        Text = "Saving NPC override…"
        ResumeLayout(False)
    End Sub

    Friend WithEvents LabelStage As Label
    Friend WithEvents LabelDetail As Label
    Friend WithEvents ProgressBarMain As ProgressBar

End Class
