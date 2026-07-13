Imports System.Text
Imports System.Threading
Imports System.Threading.Tasks

''' <summary>Windowed front-end for <c>--bake-all</c> (the <c>--windowed</c> flag): determinate bar +
''' scrolling log + Cancel, over the very same <see cref="BakeAllRunner.Run"/> the console mode uses.
'''
''' <para>The bake runs on a background thread — legitimate here (unlike the GUI's GL-bound batch)
''' because this path is 100% CPU: no OpenGL context exists in this process, and BakeAllRunner pins
''' <see cref="FaceGenBuilder.WriteGPUSandboxOutput"/> off so nothing tries to make one current.</para>
'''
''' <para>Log lines are BUFFERED and flushed on a timer rather than appended per line: a full load
''' order is thousands of NPCs, and one Invoke + TextBox.AppendText per line would spend more time
''' repainting than baking.</para></summary>
Friend Class BakeAllProgress_Form

    Private ReadOnly _options As BakeAllRunner.Options
    Private ReadOnly _pending As New StringBuilder()
    Private ReadOnly _pendingLock As New Object()

    Private _cancelRequested As Integer = 0     ' Interlocked flag — read from the worker thread
    Private _finished As Boolean = False

    ''' <summary>Exit code to hand back to the process (see BakeAllRunner's Exit* constants).</summary>
    Public Property ExitCode As Integer = BakeAllRunner.ExitFatal

    Public Sub New(options As BakeAllRunner.Options)
        InitializeComponent()
        _options = If(options, New BakeAllRunner.Options())
    End Sub

    Private Async Sub BakeAllProgress_Form_Shown(sender As Object, e As EventArgs) Handles MyBase.Shown
        FlushTimer.Start()

        ' Marshal-free sinks: the worker appends to a buffer / stores the latest progress tuple, and the
        ' UI timer picks both up. No Invoke per line (thousands of lines) and no cross-thread control touch.
        Dim logSink As Action(Of String) = Sub(line) AppendLog(line)
        Dim progressSink As Action(Of Integer, Integer, String) = Sub(done, total, label) SetProgressSafe(done, total, label)
        Dim cancelSink As Func(Of Boolean) = Function() Volatile.Read(_cancelRequested) <> 0

        Dim code = Await Task.Run(Function() BakeAllRunner.Run(_options, logSink, progressSink, cancelSink))

        ExitCode = code
        _finished = True
        FlushTimer.Stop()
        FlushPending()   ' drain whatever the last tick missed

        ProgressBarMain.Style = ProgressBarStyle.Continuous
        ProgressBarMain.Value = ProgressBarMain.Maximum
        LabelStatus.Text = "Finished."
        LabelSummary.Text = SummaryFor(code)
        ButtonCancel.Enabled = False
        ButtonClose.Enabled = True
        ButtonClose.Focus()
    End Sub

    Private Function SummaryFor(code As Integer) As String
        Select Case code
            Case BakeAllRunner.ExitOk : Return "All NPCs baked."
            Case BakeAllRunner.ExitSomeFailed : Return "Finished with failures — see the log."
            Case BakeAllRunner.ExitCancelled : Return "Cancelled."
            Case Else : Return "Failed — see the log."
        End Select
    End Function

    ''' <summary>Called from the worker thread. Buffer only; the timer does the UI work.</summary>
    Private Sub AppendLog(line As String)
        SyncLock _pendingLock
            _pending.AppendLine(If(line, ""))
        End SyncLock
    End Sub

    ''' <summary>Called from the worker thread. Stores the latest progress; the timer applies it.
    ''' Losing intermediate values is fine — only the newest one is worth painting.</summary>
    Private _progDone As Integer = 0
    Private _progTotal As Integer = 0
    Private _progLabel As String = ""
    Private Sub SetProgressSafe(done As Integer, total As Integer, label As String)
        SyncLock _pendingLock
            _progDone = done
            _progTotal = total
            _progLabel = If(label, "")
        End SyncLock
    End Sub

    Private Sub FlushTimer_Tick(sender As Object, e As EventArgs) Handles FlushTimer.Tick
        FlushPending()
    End Sub

    Private Sub FlushPending()
        Dim chunk As String = Nothing
        Dim done, total As Integer
        Dim label As String
        SyncLock _pendingLock
            If _pending.Length > 0 Then
                chunk = _pending.ToString()
                _pending.Clear()
            End If
            done = _progDone : total = _progTotal : label = _progLabel
        End SyncLock

        If total > 0 Then
            ' The bootstrap reports total = 0 (indeterminate); the bake loop reports real counts.
            If ProgressBarMain.Style <> ProgressBarStyle.Continuous Then ProgressBarMain.Style = ProgressBarStyle.Continuous
            ProgressBarMain.Maximum = total
            ProgressBarMain.Value = Math.Max(0, Math.Min(done, total))
        End If
        If Not _finished AndAlso Not String.IsNullOrEmpty(label) Then LabelStatus.Text = label

        If chunk IsNot Nothing Then
            TextBoxLog.AppendText(chunk)   ' AppendText scrolls to the caret — the log follows itself
        End If
    End Sub

    Private Sub ButtonCancel_Click(sender As Object, e As EventArgs) Handles ButtonCancel.Click
        Interlocked.Exchange(_cancelRequested, 1)
        ButtonCancel.Enabled = False
        ButtonCancel.Text = "Cancelling…"
        LabelStatus.Text = "Cancelling — finishing the current NPC…"
    End Sub

    Private Sub ButtonClose_Click(sender As Object, e As EventArgs) Handles ButtonClose.Click
        Close()
    End Sub

    Private Sub BakeAllProgress_Form_FormClosing(sender As Object, e As FormClosingEventArgs) Handles MyBase.FormClosing
        ' Never let the window close mid-bake: the worker writes NIF/DDS files, and tearing the process
        ' down between the two would leave a half-written FaceGen on disk. Turn the X into a Cancel.
        If Not _finished Then
            e.Cancel = True
            If Volatile.Read(_cancelRequested) = 0 Then ButtonCancel.PerformClick()
            Return
        End If
        FlushTimer.Stop()
    End Sub

End Class
