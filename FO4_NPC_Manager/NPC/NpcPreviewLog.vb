Imports System.Globalization
Imports System.IO
Imports System.Threading

''' <summary>
''' Simple file logger for NPC preview pipeline debugging.
''' Writes to npc_preview.log in the application directory.
'''
''' Disabled by default. Enable at runtime via <see cref="Enabled"/> when diagnosing
''' a specific issue. While disabled, every <see cref="Log"/> call short-circuits at
''' the entry guard so the file I/O cost is zero. Call sites that build expensive
''' interpolated strings should prefer <see cref="LogLazy"/>, which only invokes the
''' producer lambda when logging is enabled — eliminating the formatting cost too.
'''
''' Numeric formatting: messages are formatted under <see cref="CultureInfo.InvariantCulture"/>
''' so floats appear with `.` as decimal separator regardless of the user's OS locale.
''' This is critical for log analysis (Python/awk/grep parsing assumes invariant); without
''' this, a Spanish/German/French OS produces commas and downstream tools mis-parse.
''' </summary>
Public Module NpcPreviewLog

    Private ReadOnly _lock As New Object()
    Private _logPath As String = ""
    Private _enabled As Boolean = False
    Private _initialized As Boolean = False

    ''' <summary>Toggle on/off at runtime. Default OFF. When set to True the log file is
    ''' (re)initialized lazily on the first Log call so no I/O happens until needed.</summary>
    Public Property Enabled As Boolean
        Get
            Return _enabled
        End Get
        Set(value As Boolean)
            _enabled = value
        End Set
    End Property

    ''' <summary>Compute the log file path. Called once. Does NOT write anything to disk —
    ''' the actual file header is written lazily on the first Log call when enabled, so a
    ''' disabled run never touches the filesystem.</summary>
    Public Sub Initialize()
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "npc_preview.log")
        _initialized = False
    End Sub

    Public Sub Log(message As String)
        If Not _enabled OrElse _logPath = "" Then Return
        Try
            SyncLock _lock
                If Not _initialized Then
                    File.WriteAllText(_logPath, $"=== NPC Preview Log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={vbCrLf}")
                    _initialized = True
                End If
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{vbCrLf}")
            End SyncLock
        Catch
        End Try
    End Sub

    ''' <summary>Lazy variant. The producer is only invoked when logging is enabled, so
    ''' hot paths can avoid building expensive interpolated strings while disabled.
    ''' Use for any log line whose construction touches loops, hex dumps, or many fields.
    '''
    ''' Producer runs under <see cref="CultureInfo.InvariantCulture"/> so any VB
    ''' interpolated `$"..."` strings inside emit floats with `.` decimal separator.
    ''' This makes the log machine-parsable independently of the OS locale.</summary>
    Public Sub LogLazy(producer As Func(Of String))
        If Not _enabled OrElse _logPath = "" OrElse producer Is Nothing Then Return
        Dim msg As String
        Dim prevCulture = Thread.CurrentThread.CurrentCulture
        Try
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture
            Try
                msg = producer()
            Catch
                Return
            End Try
        Finally
            Thread.CurrentThread.CurrentCulture = prevCulture
        End Try
        Log(msg)
    End Sub

    Public Sub LogSeparator(title As String)
        If Not _enabled Then Return
        Log($"--- {title} ---")
    End Sub

End Module
