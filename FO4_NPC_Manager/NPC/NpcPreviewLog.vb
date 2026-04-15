Imports System.IO

''' <summary>
''' Simple file logger for NPC preview pipeline debugging.
''' Writes to npc_preview.log in the application directory.
''' </summary>
Public Module NpcPreviewLog

    Private ReadOnly _lock As New Object()
    Private _logPath As String = ""
    Private _enabled As Boolean = True

    Public Sub Initialize()
        _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "npc_preview.log")
        Try
            File.WriteAllText(_logPath, $"=== NPC Preview Log started {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==={vbCrLf}")
        Catch
        End Try
    End Sub

    Public Sub Log(message As String)
        If Not _enabled OrElse _logPath = "" Then Return
        Try
            SyncLock _lock
                File.AppendAllText(_logPath, $"[{DateTime.Now:HH:mm:ss.fff}] {message}{vbCrLf}")
            End SyncLock
        Catch
        End Try
    End Sub

    Public Sub LogSeparator(title As String)
        Log($"--- {title} ---")
    End Sub

End Module
