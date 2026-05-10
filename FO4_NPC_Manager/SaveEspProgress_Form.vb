''' <summary>
''' Modal progress dialog shown during Save ESP + optional CharGen bake + BA2 pack. The
''' UI thread runs the work synchronously while pumping messages via Application.DoEvents
''' between phases, so the dialog repaints without needing a worker thread. Phases are
''' driven by the caller via <see cref="ReportPhase"/> + <see cref="ReportDetail"/>.
'''
''' Determinate vs marquee: Phases that emit byte-counted progress (texture compression,
''' archive write) call <see cref="SetDeterminate"/> with a max; phases without a clean
''' bound (NIF bake, plugin write) leave the bar in marquee.
''' </summary>
Public Class SaveEspProgress_Form

    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>Update the bold stage line. Pumps DoEvents so the label paints even though
    ''' the work runs on the UI thread.</summary>
    Public Sub ReportPhase(text As String)
        LabelStage.Text = If(text, "")
        LabelDetail.Text = ""
        Application.DoEvents()
    End Sub

    ''' <summary>Update the secondary detail line under the stage label. Use for sub-step
    ''' info ("compressing 3/4: head_d.dds", "writing archive…").</summary>
    Public Sub ReportDetail(text As String)
        LabelDetail.Text = If(text, "")
        Application.DoEvents()
    End Sub

    ''' <summary>Switch the bar to indeterminate (marquee). Default state at form open.</summary>
    Public Sub SetMarquee()
        ProgressBarMain.Style = ProgressBarStyle.Marquee
        Application.DoEvents()
    End Sub

    ''' <summary>Switch the bar to a 0..max determinate range and reset value to 0.</summary>
    Public Sub SetDeterminate(max As Integer)
        ProgressBarMain.Style = ProgressBarStyle.Continuous
        ProgressBarMain.Maximum = Math.Max(1, max)
        ProgressBarMain.Value = 0
        Application.DoEvents()
    End Sub

    ''' <summary>Advance the determinate bar. Clamped to [0, Maximum].</summary>
    Public Sub SetValue(v As Integer)
        Dim clamped = Math.Max(0, Math.Min(v, ProgressBarMain.Maximum))
        ProgressBarMain.Value = clamped
        Application.DoEvents()
    End Sub

End Class
