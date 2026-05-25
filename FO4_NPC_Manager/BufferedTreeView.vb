Imports System
Imports System.Runtime.InteropServices
Imports System.Windows.Forms

''' <summary>TreeView with double buffering enabled, to kill the flicker the owner-drawn NPC tree
''' shows on the frequent Invalidate() calls (multi-select highlight, dirty bold, etc.). Enables BOTH
''' the managed double buffer (for the .NET OwnerDrawText paint) and the native common-control
''' TVS_EX_DOUBLEBUFFER extended style (the reliable one for TreeView). Drop-in replacement for
''' System.Windows.Forms.TreeView — the Designer instantiates this instead.</summary>
Public Class BufferedTreeView
    Inherits System.Windows.Forms.TreeView

    Private Const TV_FIRST As Integer = &H1100
    Private Const TVM_SETEXTENDEDSTYLE As Integer = TV_FIRST + 44
    Private Const TVS_EX_DOUBLEBUFFER As Integer = &H4

    <DllImport("user32.dll")>
    Private Shared Function SendMessage(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    Public Sub New()
        ' Managed double buffering for the OwnerDrawText paint cycle.
        Me.DoubleBuffered = True
    End Sub

    Protected Overrides Sub OnHandleCreated(e As EventArgs)
        MyBase.OnHandleCreated(e)
        ' Native common-control double buffering — the recommended flicker fix for TreeView.
        SendMessage(Me.Handle, TVM_SETEXTENDEDSTYLE, New IntPtr(TVS_EX_DOUBLEBUFFER), New IntPtr(TVS_EX_DOUBLEBUFFER))
    End Sub
End Class
