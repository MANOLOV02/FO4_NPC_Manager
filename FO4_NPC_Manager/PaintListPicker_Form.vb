Imports FO4_Base_Library

''' <summary>
''' A filterable modal picker over a RaceMenu paint list (<see cref="RaceMenuPaintCatalog"/>): the user sees the
''' registered name and the path is what gets stored. Used where a single value is being chosen against a fixed
''' target — currently the warpaint mask override for a RACE tint layer (the body/face overlay editors use a
''' two-list add/remove layout instead). Never a file browser: RaceMenu offers only the (name;;path) entries a mod
''' registered via <c>Add*Paint</c>.
'''
''' Code-built (no Designer) to match the other code-built SSE editor surfaces. On OK, <see cref="ChosenPath"/> is
''' the path to store (empty string when the "(None — clear)" row is chosen) and <see cref="ChosenEntry"/> carries
''' the full entry, including any <c>*Ex</c> texture-set slots.
''' </summary>
Friend Class PaintListPicker_Form
    ' Hereda de FO4_Base_Library.IconFormBase, que aporta los ImageList compartidos IconsSmall (16x16)
    ' e IconsLarge (24x24). Este formulario se arma por codigo y no tiene .Designer.vb, asi que su
    ' unico Inherits vive aca. Ver el remarks de IconFormBase.vb.
    Inherits FO4_Base_Library.IconFormBase

    Private ReadOnly _entries As List(Of RaceMenuPaintCatalog.Entry)
    Private ReadOnly _allowNone As Boolean
    Private ReadOnly _filter As TextBox
    Private ReadOnly _list As ListBox
    Private ReadOnly _pathLabel As Label
    Private ReadOnly _btnOk As Button

    ''' <summary>Path to store; "" means the user chose "(None — clear)". Only valid when ShowDialog = OK.</summary>
    Public ReadOnly Property ChosenPath As String
    ''' <summary>The picked catalog entry, or Nothing for the "(None — clear)" row.</summary>
    Public ReadOnly Property ChosenEntry As RaceMenuPaintCatalog.Entry?

    Public Sub New(title As String, entries As IReadOnlyList(Of RaceMenuPaintCatalog.Entry),
                   currentPath As String, allowNone As Boolean)
        _entries = If(entries Is Nothing, New List(Of RaceMenuPaintCatalog.Entry)(), entries.ToList())
        _allowNone = allowNone

        Text = title
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        MaximizeBox = False
        ShowInTaskbar = False
        FormBorderStyle = FormBorderStyle.SizableToolWindow
        ClientSize = New Drawing.Size(460, 460)
        MinimumSize = New Drawing.Size(360, 320)

        Dim root As New TableLayoutPanel With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 4, .Padding = New Padding(8)}
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))   ' filter
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100))  ' list
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))   ' path label
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))   ' buttons
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100))

        _filter = New TextBox With {.Dock = DockStyle.Fill, .Margin = New Padding(0, 0, 0, 6)}
        _filter.PlaceholderText = "Filter…"
        AddHandler _filter.TextChanged, Sub(s, e) ApplyFilter()
        root.Controls.Add(_filter, 0, 0)

        _list = New ListBox With {.Dock = DockStyle.Fill, .IntegralHeight = False}
        AddHandler _list.SelectedIndexChanged, Sub(s, e) OnSelectionChanged()
        AddHandler _list.DoubleClick, Sub(s, e) TryAccept()
        root.Controls.Add(_list, 0, 1)

        _pathLabel = New Label With {.Dock = DockStyle.Fill, .AutoEllipsis = True, .Height = 32,
                                     .ForeColor = Drawing.SystemColors.GrayText, .Margin = New Padding(0, 4, 0, 4)}
        root.Controls.Add(_pathLabel, 0, 2)

        Dim buttons As New FlowLayoutPanel With {.Dock = DockStyle.Fill, .FlowDirection = FlowDirection.RightToLeft, .AutoSize = True}
        Dim btnCancel As New Button With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .AutoSize = True}
        _btnOk = New Button With {.Text = "OK", .AutoSize = True, .Enabled = False}
        AddHandler _btnOk.Click, Sub(s, e) TryAccept()
        buttons.Controls.Add(btnCancel)
        buttons.Controls.Add(_btnOk)
        root.Controls.Add(buttons, 0, 3)

        Controls.Add(root)
        AcceptButton = _btnOk
        CancelButton = btnCancel

        PopulateList(currentPath)
    End Sub

    ' The rows currently shown, parallel to _list.Items. Nothing = the "(None — clear)" row.
    Private ReadOnly _shown As New List(Of RaceMenuPaintCatalog.Entry?)

    Private Sub PopulateList(currentPath As String)
        ApplyFilter()
        ' Preselect the row matching the current path.
        If Not String.IsNullOrWhiteSpace(currentPath) Then
            For i = 0 To _shown.Count - 1
                Dim en = _shown(i)
                If en.HasValue AndAlso String.Equals(en.Value.Path, currentPath, StringComparison.OrdinalIgnoreCase) Then
                    _list.SelectedIndex = i
                    Exit For
                End If
            Next
        End If
    End Sub

    Private Sub ApplyFilter()
        Dim q = If(_filter.Text, "").Trim()
        _list.BeginUpdate()
        _list.Items.Clear()
        _shown.Clear()
        If _allowNone Then
            _shown.Add(Nothing)
            _list.Items.Add("(None — clear)")
        End If
        For Each en In _entries
            If q.Length = 0 OrElse en.DisplayName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 _
               OrElse (en.Path IsNot Nothing AndAlso en.Path.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) Then
                _shown.Add(en)
                _list.Items.Add(en.DisplayName)
            End If
        Next
        _list.EndUpdate()
        OnSelectionChanged()
    End Sub

    Private Sub OnSelectionChanged()
        Dim i = _list.SelectedIndex
        If i < 0 OrElse i >= _shown.Count Then
            _btnOk.Enabled = False
            _pathLabel.Text = ""
            Return
        End If
        _btnOk.Enabled = True
        Dim en = _shown(i)
        _pathLabel.Text = If(en.HasValue, en.Value.Path, "")
    End Sub

    Private Sub TryAccept()
        Dim i = _list.SelectedIndex
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
