Option Strict On
Option Explicit On

Imports System.Collections.Generic
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Picker de animaciones con filtro de texto + columnas ordenables (Clip/Role/Speed/File).
''' Lo abre el botón "Select Animation" de la barra de animación de MainForm para buscar fácil entre
''' las (cientos de) animaciones de la raza. Al aceptar, MainForm setea el combo con el clip elegido.</summary>
Public Class AnimationPicker_Form

    Private ReadOnly _all As List(Of ResolvedAnimationClip)
    Private ReadOnly _initialFile As String
    Private _sortColumn As Integer = 0
    Private _sortAsc As Boolean = True

    ''' <summary>Clip elegido (Nothing si se canceló).</summary>
    Public ReadOnly Property SelectedClip As ResolvedAnimationClip
        Get
            If ListClips.SelectedItems.Count = 0 Then Return Nothing
            Return TryCast(ListClips.SelectedItems(0).Tag, ResolvedAnimationClip)
        End Get
    End Property

    Public Sub New(clips As IEnumerable(Of ResolvedAnimationClip), Optional currentFile As String = Nothing)
        InitializeComponent()
        _all = If(clips, Enumerable.Empty(Of ResolvedAnimationClip)()).Where(Function(c) c IsNot Nothing).ToList()
        _initialFile = If(currentFile, "")
    End Sub

    Private Sub AnimationPicker_Form_Shown(sender As Object, e As EventArgs) Handles Me.Shown
        Rebuild()
        TextFilter.Focus()
    End Sub

    Private Sub TextFilter_TextChanged(sender As Object, e As EventArgs) Handles TextFilter.TextChanged
        Rebuild()
    End Sub

    ' Orden por columna (toggle asc/desc al re-clickear la misma).
    Private Sub ListClips_ColumnClick(sender As Object, e As ColumnClickEventArgs) Handles ListClips.ColumnClick
        If e.Column = _sortColumn Then
            _sortAsc = Not _sortAsc
        Else
            _sortColumn = e.Column
            _sortAsc = True
        End If
        Rebuild()
    End Sub

    Private Sub ListClips_DoubleClick(sender As Object, e As EventArgs) Handles ListClips.DoubleClick
        If SelectedClip IsNot Nothing Then
            DialogResult = DialogResult.OK
            Close()
        End If
    End Sub

    Private Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        If SelectedClip Is Nothing Then
            MsgBox("Select an animation from the list.", vbInformation Or vbOKOnly, "Select Animation")
            Return
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' Aplica filtro (términos separados por espacio, AND, contra Clip+Role+File) + orden, y repuebla.
    Private Sub Rebuild()
        Dim terms = TextFilter.Text.Split(New Char() {" "c}, StringSplitOptions.RemoveEmptyEntries)
        Dim filtered = _all.Where(Function(c) MatchesAll(c, terms))

        Select Case _sortColumn
            Case 1 : filtered = OrderBoolDir(filtered, Function(c) RolesText(c))
            Case 2 : filtered = If(_sortAsc, filtered.OrderBy(Function(c) c.PlaybackSpeed), filtered.OrderByDescending(Function(c) c.PlaybackSpeed))
            Case 3 : filtered = OrderBoolDir(filtered, Function(c) c.AnimationFile)
            Case Else : filtered = OrderBoolDir(filtered, Function(c) ClipDisplayName(c))
        End Select

        ListClips.BeginUpdate()
        Try
            ListClips.Items.Clear()
            Dim toSelect As ListViewItem = Nothing
            For Each c In filtered
                Dim it As New ListViewItem(ClipDisplayName(c)) With {.Tag = c}
                it.SubItems.Add(RolesText(c))
                it.SubItems.Add(c.PlaybackSpeed.ToString("0.##"))
                it.SubItems.Add(c.AnimationFile)
                ListClips.Items.Add(it)
                If toSelect Is Nothing AndAlso _initialFile <> "" AndAlso String.Equals(c.AnimationFile, _initialFile, StringComparison.OrdinalIgnoreCase) Then toSelect = it
            Next
            If toSelect IsNot Nothing Then
                toSelect.Selected = True
                toSelect.EnsureVisible()
            End If
        Finally
            ListClips.EndUpdate()
        End Try
        LabelCount.Text = $"{ListClips.Items.Count} / {_all.Count} clips"
    End Sub

    Private Function OrderBoolDir(src As IEnumerable(Of ResolvedAnimationClip), key As Func(Of ResolvedAnimationClip, String)) As IEnumerable(Of ResolvedAnimationClip)
        Return If(_sortAsc, src.OrderBy(key, StringComparer.OrdinalIgnoreCase), src.OrderByDescending(key, StringComparer.OrdinalIgnoreCase))
    End Function

    Private Shared Function MatchesAll(c As ResolvedAnimationClip, terms As String()) As Boolean
        If terms Is Nothing OrElse terms.Length = 0 Then Return True
        Dim hay = ClipDisplayName(c) & " " & RolesText(c) & " " & c.AnimationFile
        For Each t In terms
            If hay.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0 Then Return False
        Next
        Return True
    End Function

    Private Shared Function ClipDisplayName(c As ResolvedAnimationClip) As String
        If Not String.IsNullOrWhiteSpace(c.ClipName) Then Return c.ClipName
        Return System.IO.Path.GetFileNameWithoutExtension(c.AnimationFile)
    End Function

    Private Shared Function RolesText(c As ResolvedAnimationClip) As String
        Return String.Join(",", c.Roles)
    End Function
End Class
