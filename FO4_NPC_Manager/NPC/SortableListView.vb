Imports System.Windows.Forms
Imports System.Collections

''' <summary>Click-to-sort helper for any <see cref="ListView"/> in details view. Wire-up
''' contract: call <see cref="Attach"/> once per ListView (typically right after the dialog
''' has populated its rows). The helper subscribes to <c>ColumnClick</c>, toggles
''' Asc/Desc when re-clicking the same column, defaults to Asc when clicking a new column,
''' suffixes the active column header with a small arrow indicator, and drives
''' <c>ListView.Sort</c> via a numeric-aware <see cref="IComparer"/>.
'''
''' Numeric-aware: tries <c>Decimal.TryParse</c> on each cell first (so columns like Index,
''' FormID-as-decimal, Slot, Percent sort numerically not lexicographically). Falls back to
''' case-insensitive string compare. Hex columns (e.g. "0x000A0439") sort lexicographically
''' which preserves the visually-grouped order that hex strings already provide.
'''
''' Pattern adapted from Wardrobe_Manager_Form.ListViewSources_ColumnClick / ListViewItemComparer
''' (Wardrobe_Manager_Form.vb:2857-2933) — same UX, refactored to a shared helper so every
''' ListView in NPC_Manager (EditFace ListViewHeadParts/Tints, TintPickerDialog.TintList,
''' HeadPartPicker.ListViewParts) uses ONE implementation instead of replicating the comparer
''' + click handler in each form.</summary>
Public NotInheritable Class SortableListView

    ''' <summary>Attach click-to-sort behavior to the given ListView. Idempotent — calling
    ''' twice on the same ListView replaces the prior wiring (the sort instance becomes the
    ''' new owner of the ColumnClick handler + ListViewItemSorter). Caller does not need to
    ''' hold a reference to the returned instance; the ListView keeps it alive via its event
    ''' subscription.</summary>
    Public Shared Function Attach(lv As ListView) As SortableListView
        ArgumentNullException.ThrowIfNull(lv)
        Dim instance As New SortableListView(lv)
        Return instance
    End Function

    Private ReadOnly _lv As ListView
    Private ReadOnly _originalHeaders As String()
    Private _activeColumn As Integer = -1

    Private Sub New(lv As ListView)
        _lv = lv
        ' Snapshot the original column headers so the indicator suffix can be removed/replaced
        ' on every click without losing the user's authored caption.
        ReDim _originalHeaders(lv.Columns.Count - 1)
        For i = 0 To lv.Columns.Count - 1
            _originalHeaders(i) = lv.Columns(i).Text
        Next
        AddHandler lv.ColumnClick, AddressOf OnColumnClick
    End Sub

    Private Sub OnColumnClick(sender As Object, e As ColumnClickEventArgs)
        If e.Column = _activeColumn Then
            ' Same column re-clicked → flip direction.
            _lv.Sorting = If(_lv.Sorting = SortOrder.Ascending, SortOrder.Descending, SortOrder.Ascending)
        Else
            _activeColumn = e.Column
            _lv.Sorting = SortOrder.Ascending
        End If

        ' Refresh column headers: clear any prior indicator, then suffix the active column
        ' with an Asc/Desc arrow. Falling-back to original captions on every click keeps the
        ' state honest if the caller mutated headers between clicks.
        For i = 0 To _lv.Columns.Count - 1
            _lv.Columns(i).Text = _originalHeaders(i)
        Next
        _lv.Columns(_activeColumn).Text &= If(_lv.Sorting = SortOrder.Ascending, "  ▲", "  ▼")

        _lv.ListViewItemSorter = New NumericAwareComparer(_activeColumn, _lv.Sorting)
        _lv.SuspendLayout()
        Try
            _lv.Sort()
        Finally
            _lv.ResumeLayout()
        End Try
    End Sub

    ''' <summary>Compares two ListViewItems by a single sub-item column. Numeric-aware:
    ''' tries Decimal.TryParse (invariant culture) first so columns of integers/floats sort
    ''' as numbers; otherwise falls back to case-insensitive string compare.</summary>
    Private NotInheritable Class NumericAwareComparer
        Implements IComparer

        Private ReadOnly _column As Integer
        Private ReadOnly _order As SortOrder

        Public Sub New(column As Integer, order As SortOrder)
            _column = column
            _order = order
        End Sub

        Public Function Compare(x As Object, y As Object) As Integer Implements IComparer.Compare
            Dim ix = TryCast(x, ListViewItem)
            Dim iy = TryCast(y, ListViewItem)
            If ix Is Nothing OrElse iy Is Nothing Then Return 0

            Dim sx As String = If(_column < ix.SubItems.Count, ix.SubItems(_column).Text, "")
            Dim sy As String = If(_column < iy.SubItems.Count, iy.SubItems(_column).Text, "")

            Dim cmp As Integer
            Dim nx As Decimal, ny As Decimal
            If Decimal.TryParse(sx, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, nx) _
               AndAlso Decimal.TryParse(sy, Globalization.NumberStyles.Any, Globalization.CultureInfo.InvariantCulture, ny) Then
                cmp = nx.CompareTo(ny)
            Else
                cmp = String.Compare(sx, sy, StringComparison.OrdinalIgnoreCase)
            End If

            If _order = SortOrder.Descending Then cmp = -cmp
            Return cmp
        End Function
    End Class
End Class
