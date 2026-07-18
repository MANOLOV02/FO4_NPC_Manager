Imports System
Imports System.Windows.Forms

''' <summary>Owner-drawn ListBox with managed double buffering enabled, to kill the flicker owner-draw
''' lists show when a single row is invalidated during a slider drag (the SSE tint layer list). Drop-in
''' replacement for System.Windows.Forms.ListBox.</summary>
Public Class DoubleBufferedListBox
    Inherits System.Windows.Forms.ListBox

    Public Sub New()
        ' Managed double buffering for the owner-draw paint cycle (no visible half-drawn rows on Invalidate).
        ' DoubleBuffered internally sets OptimizedDoubleBuffer + AllPaintingInWmPaint. We do NOT set UserPaint:
        ' that would suppress the ListBox's native owner-draw (DrawItem) mechanism.
        Me.DoubleBuffered = True
    End Sub
End Class
