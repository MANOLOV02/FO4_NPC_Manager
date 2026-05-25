''' <summary>Modal determinate-progress dialog for batch operations (e.g. Build CharGen loose over
''' a multi-selection). The caller assigns <see cref="WorkAsync"/> (the batch loop) and calls
''' ShowDialog: the dialog runs the work once on its Shown event and closes itself when done. Because
''' it's modal, the main window is blocked automatically (no need to disable it) and the dialog runs
''' its OWN message loop, so the Cancel button stays responsive and repaints while the GL-bound work
''' yields between items on the UI thread. Cancel sets <see cref="Cancelled"/>, which the work checks
''' between items.</summary>
Public Class BuildProgress_Form

    ''' <summary>Set True when the user clicks Cancel. The batch loop checks it between items.</summary>
    Public Property Cancelled As Boolean = False

    ''' <summary>The batch work to run while the dialog is shown. Invoked once on Shown; when it
    ''' returns the dialog closes itself. Runs on the UI thread — the modal message loop keeps the
    ''' form responsive as the work yields between items.</summary>
    Public Property WorkAsync As Func(Of BuildProgress_Form, Task)

    Public Sub New()
        InitializeComponent()
    End Sub

    ''' <summary>Update the determinate bar + status line and force an immediate repaint, so progress
    ''' is visible even though the (GL-bound) work between updates runs synchronously on this thread.</summary>
    Public Sub SetProgress(current As Integer, maximum As Integer, status As String)
        ProgressBarMain.Maximum = Math.Max(1, maximum)
        ProgressBarMain.Value = Math.Max(0, Math.Min(current, ProgressBarMain.Maximum))
        LabelStatus.Text = status
        ProgressBarMain.Refresh()
        LabelStatus.Refresh()
    End Sub

    Private Async Sub BuildProgress_Form_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        If WorkAsync Is Nothing Then
            Close()
            Return
        End If
        Try
            Await WorkAsync(Me)
        Catch
            ' Swallow — the caller's loop already records per-item failures; never leave the dialog open.
        Finally
            Close()
        End Try
    End Sub

    Private Sub ButtonCancel_Click(sender As Object, e As EventArgs) Handles ButtonCancel.Click
        Cancelled = True
        ButtonCancel.Enabled = False
        ButtonCancel.Text = "Cancelling…"
        ButtonCancel.Refresh()
    End Sub
End Class
