Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Inline sub-editor for a Material Swap (MSWP) draft, opened from the ARMA Editor's
''' "New / Edit MSWP…" button for a given gender. The substitutions grid's <b>Original Material</b> column
''' is a DROPDOWN populated from THAT GENDER'S mesh NIF materials (the MOD2/MOD3 mesh's
''' <see cref="Nifcontent_Class_Manolo.BaseMaterials"/> paths) so the user matches the swap against the
''' material slots the mesh actually references; the <b>Replacement Material</b> is typed or picked via the
''' library's <see cref="DictionaryFilePicker_Form"/> (Materials\ + {.bgsm,.bgem}); <b>Color Remap</b> is an
''' optional float (CNAM). On OK the grid is written back into the passed-in <see cref="MswpDraft"/> (the
''' caller has already allocated/registered it) and DialogResult.OK is returned.
'''
''' The editor does NOT touch the ESP; the MswpDraft it edits is persisted by the existing Save flow when an
''' ARMA/ARMO draft references it.</summary>
Public Class MswpSubEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _draft As MswpDraft
    ''' <summary>Material paths the gender mesh NIF references (BaseMaterials). Drives the Original combo.
    ''' Empty when no mesh path was supplied or the mesh couldn't be loaded — the combo then falls back to a
    ''' free-text column so the user can still author swaps.</summary>
    Private ReadOnly _meshMaterials As New List(Of String)

    Private _colOriginal As DataGridViewColumn
    Private _colReplacement As DataGridViewTextBoxColumn
    Private _colRemap As DataGridViewTextBoxColumn

    ''' <param name="mainForm">Owner — used for EditorID uniqueness checks.</param>
    ''' <param name="draft">The MSWP draft being authored (already registered on MainForm). Edited in place.</param>
    ''' <param name="genderMeshPath">The gender's MOD2 (male) / MOD3 (female) mesh path. Its NIF materials
    ''' source the Original-Material dropdown. Empty → free-text Original column.</param>
    ''' <param name="genderLabel">"Male"/"Female", shown in the caption.</param>
    Public Sub New(mainForm As MainForm, draft As MswpDraft, genderMeshPath As String, genderLabel As String)
        InitializeComponent()
        _mainForm = mainForm
        _draft = draft

        Text = $"Material Swap (MSWP) — {genderLabel}"
        LoadMeshMaterials(genderMeshPath)
        BuildGridColumns()

        TextBoxEdid.Text = If(_draft IsNot Nothing, _draft.EditorID, "")
        LoadGridFromDraft()

        AddHandler ButtonAddRow.Click, AddressOf OnAddRow
        AddHandler ButtonRemoveRow.Click, AddressOf OnRemoveRow
        AddHandler ButtonBrowseReplacement.Click, AddressOf OnBrowseReplacement
        AddHandler ButtonOk.Click, AddressOf OnOk

        ' Original-material combo (when the mesh exposed materials) must be FREE-TEXT editable so a swap
        ' whose Original isn't referenced by the chosen mesh can still be authored, and an out-of-list /
        ' empty value must never surface the default DataGridView error dialog.
        AddHandler GridSubs.EditingControlShowing, AddressOf OnGridEditingControlShowing
        AddHandler GridSubs.CellValidating, AddressOf OnGridCellValidating
        AddHandler GridSubs.DataError, AddressOf OnGridDataError
    End Sub

    ''' <summary>Make the Original combo editable (ComboBoxStyle.DropDown) so a not-listed material can be
    ''' typed — the column otherwise behaves as a DropDownList. No-op for the free-text column.</summary>
    Private Sub OnGridEditingControlShowing(sender As Object, e As DataGridViewEditingControlShowingEventArgs)
        If GridSubs.CurrentCell IsNot Nothing AndAlso GridSubs.CurrentCell.ColumnIndex = 0 Then
            Dim combo = TryCast(e.Control, ComboBox)
            If combo IsNot Nothing Then combo.DropDownStyle = ComboBoxStyle.DropDown
        End If
    End Sub

    ''' <summary>A free-typed Original material not yet in the combo's item list would raise a DataError on
    ''' commit; register it as an item first so the typed value is accepted (and persisted by OnOk).</summary>
    Private Sub OnGridCellValidating(sender As Object, e As DataGridViewCellValidatingEventArgs)
        If e.ColumnIndex <> 0 Then Return
        Dim combo = TryCast(_colOriginal, DataGridViewComboBoxColumn)
        If combo Is Nothing Then Return
        Dim v = TryCast(e.FormattedValue, String)
        If Not String.IsNullOrEmpty(v) AndAlso Not combo.Items.Contains(v) Then combo.Items.Add(v)
    End Sub

    ''' <summary>Belt-and-suspenders: never let a combo value-not-in-list bubble up as the default
    ''' DataGridView error dialog (e.g. the empty placeholder/new row).</summary>
    Private Sub OnGridDataError(sender As Object, e As DataGridViewDataErrorEventArgs)
        e.ThrowException = False
    End Sub

    ''' <summary>Load the BaseMaterials (referenced material paths) of the gender mesh into
    ''' <see cref="_meshMaterials"/>. Resolves the mesh via FilesDictionary (loose &gt; BA2). Tolerant of a
    ''' missing/unparseable mesh (leaves the list empty → free-text Original column).</summary>
    Private Sub LoadMeshMaterials(genderMeshPath As String)
        If String.IsNullOrWhiteSpace(genderMeshPath) Then Return
        ' Records store mesh paths RELATIVE to Meshes\ (prefix-free); NormalizeMeshKey re-adds the lowercase
        ' "meshes\" prefix + strips build-machine absolute prefixes so TryLoadMeshBytes (loose > BA2) resolves.
        Dim key As String = MeshPathHelpers.NormalizeMeshKey(genderMeshPath)
        Try
            Dim bytes = MeshPathHelpers.TryLoadMeshBytes(key)
            If bytes Is Nothing Then
                Logger.LogLazy(Function() $"[MSWP-MAT] mesh not found for '{genderMeshPath}' (resolved key '{key}') — Original-Material list will be empty (free-text fallback).")
                Return
            End If
            Dim nif As New Nifcontent_Class_Manolo()
            nif.Load_Manolo(bytes)
            For Each m In nif.BaseMaterials.Values
                If m IsNot Nothing AndAlso Not String.IsNullOrEmpty(m.path) AndAlso Not _meshMaterials.Contains(m.path) Then
                    _meshMaterials.Add(m.path)
                End If
            Next
        Catch ex As Exception
            Logger.LogLazy(Function() $"[MSWP-MAT] mesh material load failed for '{genderMeshPath}' (resolved key '{key}'): {ex.GetType().Name}: {ex.Message}")
        End Try

        ' Empty list despite a resolvable mesh (NIF parsed but exposed no BaseMaterials) — still diagnosable.
        If _meshMaterials.Count = 0 Then
            Logger.LogLazy(Function() $"[MSWP-MAT] no NIF materials for '{genderMeshPath}' (resolved key '{key}') — Original-Material list empty (free-text fallback).")
        End If
    End Sub

    ''' <summary>Build the 3 grid columns. Original is a combo when the mesh exposed materials (the dropdown
    ''' items), else a plain text column. The combo column allows arbitrary text (DataGridViewComboBox is set
    ''' editable) so a not-listed material can still be typed.</summary>
    Private Sub BuildGridColumns()
        GridSubs.AutoGenerateColumns = False
        GridSubs.Columns.Clear()

        If _meshMaterials.Count > 0 Then
            Dim combo As New DataGridViewComboBoxColumn With {
                .HeaderText = "Original Material (BNAM)",
                .FillWeight = 42, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                .FlatStyle = FlatStyle.Flat, .DropDownWidth = 420}
            For Each p In _meshMaterials
                combo.Items.Add(p)
            Next
            _colOriginal = combo
        Else
            _colOriginal = New DataGridViewTextBoxColumn With {
                .HeaderText = "Original Material (BNAM)",
                .FillWeight = 42, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}
        End If
        _colReplacement = New DataGridViewTextBoxColumn With {
            .HeaderText = "Replacement Material (SNAM)",
            .FillWeight = 42, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}
        _colRemap = New DataGridViewTextBoxColumn With {
            .HeaderText = "Color Remap (optional)",
            .FillWeight = 16, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill}

        GridSubs.Columns.Add(_colOriginal)
        GridSubs.Columns.Add(_colReplacement)
        GridSubs.Columns.Add(_colRemap)
    End Sub

    Private Sub LoadGridFromDraft()
        GridSubs.Rows.Clear()
        If _draft Is Nothing Then Return
        For Each s In _draft.Substitutions
            Dim remap = If(s.HasColorRemapIndex, s.ColorRemapIndex.ToString(CultureInfo.InvariantCulture), "")
            AddRow(s.OriginalMaterial, s.ReplacementMaterial, remap)
        Next
    End Sub

    ''' <summary>Add a grid row. For a combo Original column, an out-of-list value would throw on assignment,
    ''' so the item is appended to the combo's value list first.</summary>
    Private Sub AddRow(original As String, replacement As String, remap As String)
        Dim idx = GridSubs.Rows.Add()
        Dim row = GridSubs.Rows(idx)
        Dim combo = TryCast(_colOriginal, DataGridViewComboBoxColumn)
        If combo IsNot Nothing AndAlso Not String.IsNullOrEmpty(original) AndAlso Not combo.Items.Contains(original) Then
            combo.Items.Add(original)
        End If
        ' For a combo Original column an out-of-list value throws; "" is not a valid item, so an empty
        ' Original must be the null "no selection" value (Nothing), never "". (Non-empty out-of-list
        ' values were registered as items just above.)
        If combo IsNot Nothing AndAlso String.IsNullOrEmpty(original) Then
            row.Cells(0).Value = Nothing
        Else
            row.Cells(0).Value = If(original, "")
        End If
        row.Cells(1).Value = If(replacement, "")
        row.Cells(2).Value = If(remap, "")
    End Sub

    Private Sub OnAddRow(sender As Object, e As EventArgs)
        AddRow("", "", "")
    End Sub

    Private Sub OnRemoveRow(sender As Object, e As EventArgs)
        If GridSubs.SelectedRows.Count > 0 Then
            For Each row As DataGridViewRow In GridSubs.SelectedRows
                If Not row.IsNewRow Then GridSubs.Rows.Remove(row)
            Next
        ElseIf GridSubs.CurrentRow IsNot Nothing AndAlso Not GridSubs.CurrentRow.IsNewRow Then
            GridSubs.Rows.Remove(GridSubs.CurrentRow)
        End If
    End Sub

    ''' <summary>Pick a Materials\ file (loose+BA2, ext-filtered) via the library tree picker into the
    ''' current row's Replacement cell.</summary>
    Private Sub OnBrowseReplacement(sender As Object, e As EventArgs)
        If GridSubs.CurrentRow Is Nothing OrElse GridSubs.CurrentRow.IsNewRow Then
            MessageBox.Show(Me, "Select a row first (Add row if empty).", "Browse material",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            Return
        End If
        Dim current = CStr(If(GridSubs.CurrentRow.Cells(1).Value, "")).Trim()
        Dim exts As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {".bgsm", ".bgem"}
        Dim keys = FilesDictionary_class.GetFilteredKeys(MaterialsPrefix, exts)
        Using dlg As New DictionaryFilePicker_Form(keys, MaterialsPrefix, exts, current)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                Dim sel = dlg.DictionaryPicker_Control1.SelectedKey
                If Not String.IsNullOrEmpty(sel) Then GridSubs.CurrentRow.Cells(1).Value = sel
            End If
        End Using
    End Sub

    ''' <summary>Commit the EditorID + grid into the draft. Validates the EditorID (non-empty + unique, unless
    ''' unchanged on the same draft) and that at least one usable substitution exists. Vetoes the close
    ''' (DialogResult.None) on a validation failure.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        ' Flush any in-progress grid cell edit into the model before reading the rows.
        GridSubs.EndEdit()

        If _draft Is Nothing Then
            DialogResult = DialogResult.OK
            Close()
            Return
        End If

        Dim edid = TextBoxEdid.Text.Trim()
        If edid.Length = 0 Then
            MessageBox.Show(Me, "Enter an EditorID for the material swap.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If
        ' Unchanged EditorID on the same draft is fine; a CHANGED one must be free.
        If Not String.Equals(edid, _draft.EditorID, StringComparison.OrdinalIgnoreCase) _
           AndAlso Not _mainForm.IsRecordEditorIdAvailable(edid) Then
            MessageBox.Show(Me, $"EditorID '{edid}' is already in use. Choose another.", "MSWP",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning)
            DialogResult = DialogResult.None
            Return
        End If

        Dim subs As New List(Of MSWP_Substitution)
        For Each row As DataGridViewRow In GridSubs.Rows
            If row.IsNewRow Then Continue For
            Dim orig = CStr(If(row.Cells(0).Value, "")).Trim()
            Dim repl = CStr(If(row.Cells(1).Value, "")).Trim()
            If orig.Length = 0 AndAlso repl.Length = 0 Then Continue For
            Dim sub_ As New MSWP_Substitution With {.OriginalMaterial = orig, .ReplacementMaterial = repl}
            Dim remapText = CStr(If(row.Cells(2).Value, "")).Trim()
            Dim remapVal As Single
            If remapText.Length > 0 AndAlso Single.TryParse(remapText, NumberStyles.Float, CultureInfo.InvariantCulture, remapVal) Then
                sub_.HasColorRemapIndex = True
                sub_.ColorRemapIndex = remapVal
            End If
            subs.Add(sub_)
        Next

        ' A material swap with no usable substitution is a content-less record — veto the close (the
        ' summary contract requires at least one substitution).
        If subs.Count = 0 Then
            MessageBox.Show(Me, "Add at least one material substitution (Original + Replacement) before saving.",
                            "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        _draft.EditorID = edid
        _draft.Substitutions.Clear()
        _draft.Substitutions.AddRange(subs)
        If Not _draft.IsNew Then _draft.IsModified = True

        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
