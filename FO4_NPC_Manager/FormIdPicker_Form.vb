Imports System.Linq
Imports FO4_Base_Library

''' <summary>A reusable, signature-filtered FormID PICKER dialog. Replaces the per-field FormID
''' comboboxes in the upcoming ARMA/ARMO editors with a proper modal list: a live filter, sortable
''' columns (Name / EditorID / FormID-hex / Plugin / Signature), and an optional pinned "(none / NULL)"
''' row. The CALLER decides which record signatures are valid for a given field (per the xEdit
''' <c>wbFormIDCk</c> rule for that field — e.g. ARMA RNAM → {"RACE"}, ARMO KWDA → {"KYWD"},
''' material swap → {"MSWP"}, skin texture → {"TXST"}, NAM2 → {"FLST"}, ARMO addon → {"ARMA"}); the
''' picker itself contains NO field-specific logic — it only honors <c>allowedSignatures</c> +
''' <c>allowNull</c>.
'''
''' Single-select for v1: the chosen FormID (0 = NULL/none) is read from <see cref="SelectedFormID"/>
''' after <c>DialogResult.OK</c>. The internal row model (<see cref="Entry"/>) carries everything a
''' future multi-select variant would need, so adding multi-select later is a ListView flag + a
''' SelectedFormIDs accessor, not a rewrite.
'''
''' Data: for each allowed signature the picker enumerates <see cref="PluginManager.GetRecordsOfType"/>
''' once (per open), reading EditorID via the cheap <see cref="PluginRecord.EditorID"/> property, FULL
''' via <see cref="PluginManager.ResolveFieldString"/> (localized-string + encoding aware, same as the
''' record parsers' ResolveDisplayString), and the originating plugin via
''' <see cref="PluginRecord.SourcePluginName"/>. Records are keyed by GLOBAL FormID in the manager
''' (<c>AllRecords</c>/<c>RecordsByType</c>), so <c>rec.Header.FormID</c> is already global — no
''' per-reference resolution needed for the record's own id. The built list is cached on this instance
''' (built once in the constructor); the filter operates on the cache, never re-parsing on a keystroke.
''' In-memory drafts (<paramref name="extraDraftEntries"/>) are appended after the real records, shown
''' with Plugin="(new)".</summary>
Public Class FormIdPicker_Form

    ''' <summary>Plugin="(new)" marker for draft (not-yet-saved) entries.</summary>
    Public Const NewDraftPluginLabel As String = "(new)"

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _allowNull As Boolean
    Private ReadOnly _entries As New List(Of Entry)
    Private _filtered As List(Of Entry)

    ''' <summary>Optional per-row predicate the CALLER supplies (e.g. "is this ARMA race-compatible with the
    ''' preview NPC's race"). Nothing → no row gating (every record passes). When set, the display filter ALSO
    ''' gates each non-NULL row by this predicate UNLESS "Show all" is checked. The predicate runs in the
    ''' display filter over the cached <see cref="_entries"/> — toggling "Show all" or typing only re-filters
    ''' the cache, it never re-enumerates records.</summary>
    Private ReadOnly _formIdFilter As Func(Of UInteger, Boolean)

    ''' <summary>The chosen record's GLOBAL FormID after <c>DialogResult.OK</c>. 0 = the pinned
    ''' "(none / NULL)" row was chosen (only possible when <c>allowNull</c> was True).</summary>
    Public ReadOnly Property SelectedFormID As UInteger
        Get
            Return _selectedFormID
        End Get
    End Property
    Private _selectedFormID As UInteger

    ''' <summary>Internal row model. One per pickable record (plus one pinned NULL row when allowed).
    ''' Carries the full set of display fields so the ListView, the filter and a future multi-select
    ''' accessor all read from the same shape.</summary>
    Private NotInheritable Class Entry
        Public FormID As UInteger
        Public EditorID As String = ""
        Public DisplayName As String = ""
        Public Signature As String = ""
        Public PluginName As String = ""
        ''' <summary>The pinned "(none / NULL)" row: always shown regardless of filter text, returns 0.</summary>
        Public IsNullRow As Boolean

        ''' <summary>8-hex FormID for the FormID column + the filter ("0x0001A2B3"). Blank for the NULL row.</summary>
        Public ReadOnly Property FormIDText As String
            Get
                If IsNullRow Then Return ""
                Return "0x" & FormID.ToString("X8")
            End Get
        End Property
    End Class

    ''' <param name="pluginManager">Master plugin manager — the record source for every allowed signature.</param>
    ''' <param name="allowedSignatures">Record signatures the field accepts, per the caller's xEdit
    ''' <c>wbFormIDCk</c> rule (e.g. {"RACE"}, {"KYWD"}, {"TXST"}, {"FLST"}, {"MSWP"}, {"ARMA"}, {"ARMO"}).</param>
    ''' <param name="title">Optional window caption; a sensible default is derived from the signatures.</param>
    ''' <param name="currentFormID">The field's current value — preselected in the list when present.</param>
    ''' <param name="allowNull">True → include a pinned "(none / NULL)" row that returns FormID 0.</param>
    ''' <param name="extraDraftEntries">In-memory drafts (ARMA/ARMO/MSWP not yet saved) to append after the
    ''' real records, each shown with Plugin="(new)".</param>
    ''' <param name="formIdFilter">Optional per-FormID predicate (e.g. race-compatibility for ARMA/ARMO pickers).
    ''' When supplied, the "Show all" checkbox is shown/enabled and each non-NULL row is gated by this predicate
    ''' unless "Show all" is checked. Nothing → no gating and the checkbox stays hidden/disabled.</param>
    Public Sub New(pluginManager As PluginManager,
                   allowedSignatures As IEnumerable(Of String),
                   Optional title As String = Nothing,
                   Optional currentFormID As UInteger = 0UI,
                   Optional allowNull As Boolean = True,
                   Optional extraDraftEntries As IEnumerable(Of FormIdPickerEntry) = Nothing,
                   Optional formIdFilter As Func(Of UInteger, Boolean) = Nothing)
        InitializeComponent()
        _pluginManager = pluginManager
        _allowNull = allowNull
        _formIdFilter = formIdFilter

        ' "Show all" only makes sense when a filter was supplied — otherwise nothing to override.
        ' Hidden (not just disabled) when no filter, so an unfiltered picker shows no stray control.
        CheckBoxShowAll.Visible = (_formIdFilter IsNot Nothing)
        CheckBoxShowAll.Enabled = (_formIdFilter IsNot Nothing)
        CheckBoxShowAll.Checked = False

        ' De-dup + normalize the allowed signatures (ordinal — record signatures are 4-char ASCII).
        Dim sigs = If(allowedSignatures, Enumerable.Empty(Of String)()) _
                       .Where(Function(s) Not String.IsNullOrWhiteSpace(s)) _
                       .Select(Function(s) s.Trim()) _
                       .Distinct(StringComparer.Ordinal) _
                       .ToList()

        Text = If(String.IsNullOrEmpty(title),
                  If(sigs.Count = 0, "Select record", $"Select {String.Join(" / ", sigs)}"),
                  title)
        LabelHeader.Text = If(sigs.Count = 0,
                              "No signatures supplied.",
                              $"Pick a record ({String.Join(", ", sigs)}). Type to filter; double-click or Enter to choose.")

        BuildEntries(sigs, extraDraftEntries)
        ' Initial population goes through the SAME display filter (empty text + "Show all" unchecked) so the
        ' race gate is applied from the start when a filter was supplied — no separate unfiltered first paint.
        OnFilterChanged(Me, EventArgs.Empty)
        PreselectCurrent(currentFormID)

        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        AddHandler CheckBoxShowAll.CheckedChanged, AddressOf OnFilterChanged
        AddHandler ListViewRecords.DoubleClick, AddressOf OnListDoubleClick
        AddHandler ListViewRecords.KeyDown, AddressOf OnListKeyDown
        AddHandler ButtonOk.Click, AddressOf OnOk

        ' Click-to-sort on every column (same shared helper EditFace / OutfitPicker / HeadPartPicker use).
        ' Numeric-aware comparer sorts the hex FormID column lexicographically, which keeps "0x000xxxxx"
        ' visually grouped. The sorter survives list re-population (filter changes), so a chosen sort sticks.
        SortableListView.Attach(ListViewRecords)
    End Sub

    ''' <summary>Build the per-signature entry list ONCE (per open). Optionally prepends the pinned
    ''' NULL row, then for each allowed signature enumerates <see cref="PluginManager.GetRecordsOfType"/>
    ''' and produces one <see cref="Entry"/> per record, then appends the supplied drafts. The result is
    ''' cached in <see cref="_entries"/>; the live filter runs against this cache (no re-parse per keystroke).</summary>
    Private Sub BuildEntries(sigs As List(Of String), extraDraftEntries As IEnumerable(Of FormIdPickerEntry))
        ' Pinned NULL row first so it sits at the top (and survives the filter — see OnFilterChanged).
        If _allowNull Then
            _entries.Add(New Entry With {.FormID = 0UI, .DisplayName = "(none / NULL)", .IsNullRow = True})
        End If

        If _pluginManager IsNot Nothing Then
            For Each sig In sigs
                Dim recs = _pluginManager.GetRecordsOfType(sig)
                If recs Is Nothing Then Continue For
                For Each rec In recs
                    If rec Is Nothing Then Continue For
                    _entries.Add(New Entry With {
                        .FormID = rec.Header.FormID,
                        .EditorID = If(rec.EditorID, ""),
                        .DisplayName = ResolveDisplayName(rec),
                        .Signature = sig,
                        .PluginName = ResolvePluginName(rec)
                    })
                Next
            Next
        End If

        ' Drafts AFTER the real records, marked Plugin="(new)" unless the caller set a name.
        If extraDraftEntries IsNot Nothing Then
            For Each d In extraDraftEntries
                If d Is Nothing Then Continue For
                _entries.Add(New Entry With {
                    .FormID = d.FormID,
                    .EditorID = If(d.EditorID, ""),
                    .DisplayName = If(String.IsNullOrEmpty(d.DisplayName), If(d.EditorID, ""), d.DisplayName),
                    .Signature = If(d.Signature, ""),
                    .PluginName = If(String.IsNullOrEmpty(d.PluginName), NewDraftPluginLabel, d.PluginName)
                })
            Next
        End If
    End Sub

    ''' <summary>Cheap display name: the record's FULL (localized-string + encoding aware, via the same
    ''' <see cref="PluginManager.ResolveFieldString"/> the record parsers' ResolveDisplayString uses),
    ''' falling back to EditorID when the record has no FULL (TXST / FLST / MSWP / many KYWD).</summary>
    Private Function ResolveDisplayName(rec As PluginRecord) As String
        Dim full = rec.GetSubrecord("FULL")
        If full.HasValue AndAlso _pluginManager IsNot Nothing Then
            Dim s = _pluginManager.ResolveFieldString(rec, full.Value)
            If Not String.IsNullOrEmpty(s) Then Return s
        End If
        Return If(rec.EditorID, "")
    End Function

    ''' <summary>Originating plugin (esp/esm). Prefer the parse-time <c>SourcePluginName</c> (set by
    ''' PluginReader); fall back to the ESL-aware <see cref="PluginManager.GetOriginatingPluginName"/>
    ''' for the record's global FormID, then "?" if neither attributes it.</summary>
    Private Function ResolvePluginName(rec As PluginRecord) As String
        If Not String.IsNullOrEmpty(rec.SourcePluginName) Then Return rec.SourcePluginName
        If _pluginManager IsNot Nothing Then
            Dim nm = _pluginManager.GetOriginatingPluginName(rec.Header.FormID)
            If Not String.IsNullOrEmpty(nm) Then Return nm
        End If
        Return "?"
    End Function

    ''' <summary>Repaint the ListView from <see cref="_filtered"/>. Column order matches the Designer:
    ''' Name / EditorID / FormID / Plugin / Signature.</summary>
    Private Sub RefreshList()
        ListViewRecords.BeginUpdate()
        Try
            ListViewRecords.Items.Clear()
            For Each e In _filtered
                Dim row As New ListViewItem(e.DisplayName)
                row.SubItems.Add(e.EditorID)
                row.SubItems.Add(e.FormIDText)
                row.SubItems.Add(e.PluginName)
                row.SubItems.Add(e.Signature)
                row.Tag = e
                ListViewRecords.Items.Add(row)
            Next
        Finally
            ListViewRecords.EndUpdate()
        End Try
    End Sub

    ''' <summary>Select the row matching <paramref name="currentFormID"/> (the field's current value);
    ''' else the pinned NULL row when present; else the first row. Runs against the freshly-populated
    ''' (unfiltered) list.</summary>
    Private Sub PreselectCurrent(currentFormID As UInteger)
        Dim idx As Integer = -1
        For i = 0 To _filtered.Count - 1
            Dim e = _filtered(i)
            If Not e.IsNullRow AndAlso e.FormID = currentFormID AndAlso currentFormID <> 0UI Then
                idx = i : Exit For
            End If
        Next
        If idx < 0 Then
            ' currentFormID is 0 / not found → land on the NULL row if there is one. Otherwise leave NOTHING
            ' selected: an Add picker (allowNull=False, currentFormID=0) must force a deliberate choice — auto-
            ' selecting the first (arbitrary) record would let a stray OK/Enter add the wrong record.
            idx = If(_allowNull AndAlso _filtered.Count > 0 AndAlso _filtered(0).IsNullRow, 0, -1)
        End If
        If idx >= 0 AndAlso idx < ListViewRecords.Items.Count Then
            ListViewRecords.Items(idx).Selected = True
            ListViewRecords.Items(idx).EnsureVisible()
        End If
    End Sub

    ''' <summary>Live display filter, run on every keystroke AND on a "Show all" toggle. A row passes iff it
    ''' clears BOTH gates: the race/FormID predicate gate (<see cref="PassesRaceGate"/>) AND the text gate
    ''' (<see cref="PassesTextGate"/>). The pinned NULL row always passes both. Operates on the cached
    ''' <see cref="_entries"/> (real records AND appended drafts) — never re-parses; toggling "Show all" or
    ''' typing just re-filters the cache.</summary>
    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        Dim text = TextBoxFilter.Text.Trim()
        _filtered = _entries.Where(Function(en) PassesRaceGate(en) AndAlso PassesTextGate(en, text)).ToList()
        RefreshList()
    End Sub

    ''' <summary>Race/FormID gate: a row passes iff it is the pinned NULL row, OR "Show all" is checked, OR no
    ''' <see cref="_formIdFilter"/> was supplied, OR the predicate returns True for the row's FormID. Applies to
    ''' both real records and the appended draft entries.</summary>
    Private Function PassesRaceGate(en As Entry) As Boolean
        If en.IsNullRow Then Return True
        If CheckBoxShowAll.Checked Then Return True
        If _formIdFilter Is Nothing Then Return True
        Return _formIdFilter(en.FormID)
    End Function

    ''' <summary>Text gate: case-insensitive substring across Name + EditorID + FormID-hex. Empty filter passes
    ''' everything; the pinned NULL row always passes.</summary>
    Private Function PassesTextGate(en As Entry, text As String) As Boolean
        If text.Length = 0 Then Return True
        If en.IsNullRow Then Return True
        Return en.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
               en.EditorID.Contains(text, StringComparison.OrdinalIgnoreCase) OrElse
               en.FormIDText.Contains(text, StringComparison.OrdinalIgnoreCase)
    End Function

    Private Sub OnListDoubleClick(sender As Object, e As EventArgs)
        If ListViewRecords.SelectedItems.Count = 0 Then Return
        OnOk(sender, e)
    End Sub

    ''' <summary>Enter on the focused list = OK on the selected row (matches the double-click affordance).</summary>
    Private Sub OnListKeyDown(sender As Object, e As KeyEventArgs)
        If e.KeyCode = Keys.Enter Then
            If ListViewRecords.SelectedItems.Count > 0 Then
                e.Handled = True
                e.SuppressKeyPress = True
                OnOk(sender, EventArgs.Empty)
            End If
        End If
    End Sub

    ''' <summary>Commit: read the selected row's FormID (0 for the NULL row) into
    ''' <see cref="SelectedFormID"/> and close with OK. ButtonOk carries DialogResult.OK in the Designer,
    ''' so setting DialogResult.None here VETOES the auto-close (no selection).</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If ListViewRecords.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        Dim en = TryCast(ListViewRecords.SelectedItems(0).Tag, Entry)
        If en Is Nothing Then
            DialogResult = DialogResult.None
            Return
        End If
        _selectedFormID = If(en.IsNullRow, 0UI, en.FormID)
        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class

''' <summary>A pre-built picker row for an in-memory draft (an ARMA/ARMO/MSWP record the user is
''' authoring but hasn't saved). Passed to <see cref="FormIdPicker_Form"/> via
''' <c>extraDraftEntries</c>; each is appended after the real records and shown with
''' Plugin="(new)" unless <see cref="PluginName"/> is set. Kept a plain public class (not a tuple)
''' so callers across the app construct it the same way and a future multi-select picker can reuse it.</summary>
Public Class FormIdPickerEntry
    Public Property FormID As UInteger
    Public Property EditorID As String = ""
    Public Property DisplayName As String = ""
    Public Property Signature As String = ""
    Public Property PluginName As String = ""
End Class
