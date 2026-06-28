Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal picker over the game's merged file universe (loose + BA2, via
''' <see cref="FilesDictionary_class"/>). Used by the Armor Editor's mesh / material "Browse…" buttons:
''' it lists the available relative asset paths filtered by an extension set (e.g. ".nif" for meshes,
''' ".bgsm"/".bgem" for materials) and returns the selected relative path in <see cref="SelectedPath"/>.
'''
''' Enumeration source: <see cref="FilesDictionary_class.GetFilteredKeys"/> (root-prefix + extension
''' filtered, accelerated by the dictionary's per-directory-extension index). The full filtered set can
''' be large (thousands), so the ListView is (re)populated on demand against the live search text — a
''' simple case-insensitive Contains over the pre-filtered key list, capped at <see cref="MaxRows"/> rows
''' so a too-broad search never floods the control. The text field in the editor stays editable as a
''' fallback (free-text), per the user's choice (browser over loose+BA2, not free-text only).</summary>
Public Class AssetBrowser_Form

    ''' <summary>Cap on rows shown at once — a guard so an empty / very broad search doesn't try to add
    ''' tens of thousands of ListView items. The status label tells the user when results are truncated.</summary>
    Private Const MaxRows As Integer = 2000

    ''' <summary>The relative asset path the user picked (e.g. "Meshes\armor\foo.nif"), or "" on cancel.</summary>
    Public Property SelectedPath As String = ""

    ''' <summary>The full (pre-)filtered key universe for this browser's extension set, computed once in the
    ''' ctor. The live search filters THIS list (not the whole dictionary) on each keystroke.</summary>
    Private ReadOnly _allKeys As List(Of String)

    ''' <param name="title">Dialog title (e.g. "Pick mesh" / "Pick material").</param>
    ''' <param name="rootPrefix">Restrict to a subtree (e.g. "Meshes\" / "Materials\") — "" = no root filter.</param>
    ''' <param name="extensions">Allowed extensions (with or without leading dot), e.g. {".nif"} or {".bgsm",".bgem"}.</param>
    ''' <param name="initialPath">Pre-fill the search box + preselect this path if present.</param>
    Public Sub New(title As String, rootPrefix As String, extensions As IEnumerable(Of String),
                   Optional initialPath As String = "")
        InitializeComponent()
        Text = title

        ' One-shot pull of the filtered universe (sorted). FilesDictionary normalizes keys to backslash,
        ' case-insensitive, prefix-included (e.g. "Meshes\..."). Empty when no extensions / nothing loaded.
        Dim keys As List(Of String)
        Try
            keys = FilesDictionary_class.GetFilteredKeys(If(rootPrefix, ""), extensions)
        Catch
            keys = New List(Of String)
        End Try
        keys.Sort(StringComparer.OrdinalIgnoreCase)
        _allKeys = keys

        AddHandler TextBoxSearch.TextChanged, AddressOf OnSearchChanged
        AddHandler ListViewFiles.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ListViewFiles.SelectedIndexChanged, AddressOf OnListSelectionChanged
        AddHandler ButtonOk.Click, AddressOf OnOk

        If Not String.IsNullOrEmpty(initialPath) Then TextBoxSearch.Text = initialPath
        RefreshList()
        PreselectPath(initialPath)
    End Sub

    ''' <summary>(Re)populate the ListView with the keys matching the current search text (case-insensitive
    ''' Contains), capped at <see cref="MaxRows"/>. An empty search shows the first MaxRows of the whole set.</summary>
    Private Sub RefreshList()
        Dim search = TextBoxSearch.Text.Trim()
        Dim matches As IEnumerable(Of String)
        If search.Length = 0 Then
            matches = _allKeys
        Else
            matches = _allKeys.Where(Function(k) k.Contains(search, StringComparison.OrdinalIgnoreCase))
        End If

        ListViewFiles.BeginUpdate()
        Try
            ListViewFiles.Items.Clear()
            Dim shown As Integer = 0
            Dim total As Integer = 0
            For Each k In matches
                total += 1
                If shown < MaxRows Then
                    Dim row As New ListViewItem(k)
                    row.Tag = k
                    ListViewFiles.Items.Add(row)
                    shown += 1
                End If
            Next
            If total > shown Then
                LabelStatus.Text = $"Showing {shown:N0} of {total:N0} matches — refine the search to narrow."
            Else
                LabelStatus.Text = $"{shown:N0} match(es)."
            End If
        Finally
            ListViewFiles.EndUpdate()
        End Try
    End Sub

    Private Sub PreselectPath(path As String)
        If String.IsNullOrEmpty(path) Then Return
        For Each row As ListViewItem In ListViewFiles.Items
            If String.Equals(CStr(row.Tag), path, StringComparison.OrdinalIgnoreCase) Then
                row.Selected = True
                row.EnsureVisible()
                Return
            End If
        Next
    End Sub

    Private Sub OnSearchChanged(sender As Object, e As EventArgs)
        RefreshList()
    End Sub

    Private Sub OnListSelectionChanged(sender As Object, e As EventArgs)
        If ListViewFiles.SelectedItems.Count = 0 Then Return
        ' Mirror the chosen path into the search box so the user sees the full selected path.
        Dim k = CStr(ListViewFiles.SelectedItems(0).Tag)
        SelectedPath = k
    End Sub

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewFiles.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Commit the picked path. A ListView selection wins; otherwise the raw search-box text is
    ''' returned verbatim (free-text fallback — the user may type a path the browser didn't surface).</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewFiles.SelectedItems.Count > 0 Then
            SelectedPath = CStr(ListViewFiles.SelectedItems(0).Tag)
        Else
            SelectedPath = TextBoxSearch.Text.Trim()
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
