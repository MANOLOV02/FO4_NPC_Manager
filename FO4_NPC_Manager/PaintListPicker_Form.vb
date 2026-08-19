Imports FO4_Base_Library

''' <summary>
''' A filterable modal picker over a RaceMenu paint list (<see cref="RaceMenuPaintCatalog"/>): the user sees the
''' registered name and the path is what gets stored. Used where a single value is being chosen against a fixed
''' target — currently the warpaint mask override for a RACE tint layer (the body/face overlay editors use a
''' two-list add/remove layout instead). Never a file browser: RaceMenu offers only the (name;;path) entries a mod
''' registered via <c>Add*Paint</c>.
'''
''' On OK, <see cref="ChosenPath"/> is the path to store (empty string when the "(None — clear)" row is chosen)
''' and <see cref="ChosenEntry"/> carries the full entry, including any <c>*Ex</c> texture-set slots.
''' </summary>
''' <remarks>
''' La UI vive en <c>PaintListPicker_Form.Designer.vb</c>. Antes se armaba por código, y el remarks de entonces
''' lo justificaba diciendo que así "combinaba con las otras superficies SSE armadas por código" — o sea que la
''' violación de la regla se justificaba copiando la de al lado. Las otras también se migraron.
''' </remarks>
Friend Class PaintListPicker_Form

    Private ReadOnly _entries As List(Of RaceMenuPaintCatalog.Entry)
    Private ReadOnly _allowNone As Boolean

    ''' <summary>Path to store; "" means the user chose "(None — clear)". Only valid when ShowDialog = OK.</summary>
    Public ReadOnly Property ChosenPath As String
    ''' <summary>The picked catalog entry, or Nothing for the "(None — clear)" row.</summary>
    Public ReadOnly Property ChosenEntry As RaceMenuPaintCatalog.Entry?

    Public Sub New(title As String, entries As IReadOnlyList(Of RaceMenuPaintCatalog.Entry),
                   currentPath As String, allowNone As Boolean)
        InitializeComponent()
        _entries = If(entries Is Nothing, New List(Of RaceMenuPaintCatalog.Entry)(), entries.ToList())
        _allowNone = allowNone
        Text = title
        PopulateList(currentPath)
    End Sub

    ' The rows currently shown, parallel to ListBoxEntries.Items. Nothing = the "(None — clear)" row.
    Private ReadOnly _shown As New List(Of RaceMenuPaintCatalog.Entry?)

    Private Sub PopulateList(currentPath As String)
        ApplyFilter()
        ' Preselect the row matching the current path.
        If Not String.IsNullOrWhiteSpace(currentPath) Then
            For i = 0 To _shown.Count - 1
                Dim en = _shown(i)
                If en.HasValue AndAlso String.Equals(en.Value.Path, currentPath, StringComparison.OrdinalIgnoreCase) Then
                    ListBoxEntries.SelectedIndex = i
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Sub TextBoxFilter_TextChanged(sender As Object, e As EventArgs) Handles TextBoxFilter.TextChanged
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        Dim q = If(TextBoxFilter.Text, "").Trim()
        ListBoxEntries.BeginUpdate()
        ListBoxEntries.Items.Clear()
        _shown.Clear()
        If _allowNone Then
            _shown.Add(Nothing)
            ListBoxEntries.Items.Add("(None — clear)")
        End If
        For Each en In _entries
            If q.Length = 0 OrElse en.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 _
               OrElse (en.Path IsNot Nothing AndAlso en.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) Then
                _shown.Add(en)
                ListBoxEntries.Items.Add(en.DisplayName)
            End If
        Next
        ListBoxEntries.EndUpdate()
        OnSelectionChanged()
    End Sub

    Private Sub ListBoxEntries_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBoxEntries.SelectedIndexChanged
        OnSelectionChanged()
    End Sub

    Private Sub OnSelectionChanged()
        Dim i = ListBoxEntries.SelectedIndex
        If i < 0 OrElse i >= _shown.Count Then
            ButtonOk.Enabled = False
            LabelPath.Text = ""
            Return
        End If
        ButtonOk.Enabled = True
        Dim en = _shown(i)
        LabelPath.Text = If(en.HasValue, en.Value.Path, "")
    End Sub

    Private Sub ListBoxEntries_DoubleClick(sender As Object, e As EventArgs) Handles ListBoxEntries.DoubleClick
        TryAccept()
    End Sub

    Private Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        TryAccept()
    End Sub

    Private Sub TryAccept()
        Dim i = ListBoxEntries.SelectedIndex
        If i < 0 OrElse i >= _shown.Count Then Return
        Dim en = _shown(i)
        If en.HasValue Then
            _ChosenEntry = en
            _ChosenPath = en.Value.Path
        Else
            _ChosenEntry = Nothing
            _ChosenPath = ""   ' explicit clear
        End If
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
