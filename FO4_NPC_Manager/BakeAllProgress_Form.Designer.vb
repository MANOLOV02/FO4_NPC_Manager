<Global.Microsoft.VisualBasic.CompilerServices.DesignerGenerated()>
Partial Class BakeAllProgress_Form
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
            If disposing AndAlso components IsNot Nothing Then
                components.Dispose()
            End If
        Finally
            MyBase.Dispose(disposing)
        End Try
    End Sub

    Private components As System.ComponentModel.IContainer

    <System.Diagnostics.DebuggerStepThrough()>
    Private Sub InitializeComponent()
        Me.components = New System.ComponentModel.Container()
        Me.FlushTimer = New System.Windows.Forms.Timer(Me.components)
        Me.PanelTop = New System.Windows.Forms.Panel()
        Me.LabelStatus = New System.Windows.Forms.Label()
        Me.ProgressBarMain = New System.Windows.Forms.ProgressBar()
        Me.PanelBottom = New System.Windows.Forms.Panel()
        Me.LabelSummary = New System.Windows.Forms.Label()
        Me.ButtonCancel = New System.Windows.Forms.Button()
        Me.ButtonClose = New System.Windows.Forms.Button()
        Me.TextBoxLog = New System.Windows.Forms.TextBox()
        Me.PanelTop.SuspendLayout()
        Me.PanelBottom.SuspendLayout()
        Me.SuspendLayout()
        '
        'FlushTimer
        '
        Me.FlushTimer.Interval = 200
        '
        'PanelTop
        '
        Me.PanelTop.Controls.Add(Me.ProgressBarMain)
        Me.PanelTop.Controls.Add(Me.LabelStatus)
        Me.PanelTop.Dock = System.Windows.Forms.DockStyle.Top
        Me.PanelTop.Location = New System.Drawing.Point(0, 0)
        Me.PanelTop.Name = "PanelTop"
        Me.PanelTop.Padding = New System.Windows.Forms.Padding(10, 8, 10, 6)
        Me.PanelTop.Size = New System.Drawing.Size(900, 62)
        Me.PanelTop.TabIndex = 0
        '
        'LabelStatus
        '
        Me.LabelStatus.AutoEllipsis = True
        Me.LabelStatus.Dock = System.Windows.Forms.DockStyle.Top
        Me.LabelStatus.Location = New System.Drawing.Point(10, 8)
        Me.LabelStatus.Name = "LabelStatus"
        Me.LabelStatus.Size = New System.Drawing.Size(880, 20)
        Me.LabelStatus.TabIndex = 0
        Me.LabelStatus.Text = "Starting…"
        Me.LabelStatus.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'ProgressBarMain
        '
        Me.ProgressBarMain.Dock = System.Windows.Forms.DockStyle.Top
        Me.ProgressBarMain.Location = New System.Drawing.Point(10, 28)
        Me.ProgressBarMain.Name = "ProgressBarMain"
        Me.ProgressBarMain.Size = New System.Drawing.Size(880, 22)
        Me.ProgressBarMain.Style = System.Windows.Forms.ProgressBarStyle.Marquee
        Me.ProgressBarMain.TabIndex = 1
        '
        'PanelBottom
        '
        Me.PanelBottom.Controls.Add(Me.LabelSummary)
        Me.PanelBottom.Controls.Add(Me.ButtonCancel)
        Me.PanelBottom.Controls.Add(Me.ButtonClose)
        Me.PanelBottom.Dock = System.Windows.Forms.DockStyle.Bottom
        Me.PanelBottom.Location = New System.Drawing.Point(0, 458)
        Me.PanelBottom.Name = "PanelBottom"
        Me.PanelBottom.Size = New System.Drawing.Size(900, 44)
        Me.PanelBottom.TabIndex = 2
        '
        'LabelSummary
        '
        Me.LabelSummary.AutoEllipsis = True
        Me.LabelSummary.Location = New System.Drawing.Point(10, 12)
        Me.LabelSummary.Name = "LabelSummary"
        Me.LabelSummary.Size = New System.Drawing.Size(620, 20)
        Me.LabelSummary.TabIndex = 0
        Me.LabelSummary.Text = ""
        Me.LabelSummary.TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        '
        'ButtonCancel
        '
        Me.ButtonCancel.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.ButtonCancel.Location = New System.Drawing.Point(660, 8)
        Me.ButtonCancel.Name = "ButtonCancel"
        Me.ButtonCancel.Size = New System.Drawing.Size(110, 28)
        Me.ButtonCancel.TabIndex = 1
        Me.ButtonCancel.Text = "Cancel"
        Me.ButtonCancel.UseVisualStyleBackColor = True
        '
        'ButtonClose
        '
        Me.ButtonClose.Anchor = CType((System.Windows.Forms.AnchorStyles.Bottom Or System.Windows.Forms.AnchorStyles.Right), System.Windows.Forms.AnchorStyles)
        Me.ButtonClose.Enabled = False
        Me.ButtonClose.Location = New System.Drawing.Point(778, 8)
        Me.ButtonClose.Name = "ButtonClose"
        Me.ButtonClose.Size = New System.Drawing.Size(110, 28)
        Me.ButtonClose.TabIndex = 2
        Me.ButtonClose.Text = "Close"
        Me.ButtonClose.UseVisualStyleBackColor = True
        '
        'TextBoxLog
        '
        Me.TextBoxLog.BackColor = System.Drawing.SystemColors.Window
        Me.TextBoxLog.Dock = System.Windows.Forms.DockStyle.Fill
        Me.TextBoxLog.Font = New System.Drawing.Font("Consolas", 8.5!)
        Me.TextBoxLog.Location = New System.Drawing.Point(0, 62)
        Me.TextBoxLog.Multiline = True
        Me.TextBoxLog.Name = "TextBoxLog"
        Me.TextBoxLog.ReadOnly = True
        Me.TextBoxLog.ScrollBars = System.Windows.Forms.ScrollBars.Both
        Me.TextBoxLog.Size = New System.Drawing.Size(900, 396)
        Me.TextBoxLog.TabIndex = 1
        Me.TextBoxLog.WordWrap = False
        '
        'BakeAllProgress_Form
        '
        Me.AutoScaleDimensions = New System.Drawing.SizeF(7.0!, 15.0!)
        Me.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font
        Me.ClientSize = New System.Drawing.Size(900, 502)
        Me.Controls.Add(Me.TextBoxLog)
        Me.Controls.Add(Me.PanelBottom)
        Me.Controls.Add(Me.PanelTop)
        Me.MinimumSize = New System.Drawing.Size(640, 320)
        Me.Name = "BakeAllProgress_Form"
        Me.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen
        Me.Text = "Build CharGen (loose) — all NPCs in the load order"
        Me.PanelTop.ResumeLayout(False)
        Me.PanelBottom.ResumeLayout(False)
        Me.ResumeLayout(False)
        Me.PerformLayout()
    End Sub

    Friend WithEvents FlushTimer As System.Windows.Forms.Timer
    Friend WithEvents PanelTop As System.Windows.Forms.Panel
    Friend WithEvents LabelStatus As System.Windows.Forms.Label
    Friend WithEvents ProgressBarMain As System.Windows.Forms.ProgressBar
    Friend WithEvents PanelBottom As System.Windows.Forms.Panel
    Friend WithEvents LabelSummary As System.Windows.Forms.Label
    Friend WithEvents ButtonCancel As System.Windows.Forms.Button
    Friend WithEvents ButtonClose As System.Windows.Forms.Button
    Friend WithEvents TextBoxLog As System.Windows.Forms.TextBox
End Class
