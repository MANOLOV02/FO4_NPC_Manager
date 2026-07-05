Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Inline sub-editor for a Material Swap (MSWP) draft, opened from the ARMA/ARMO Editor's
''' "New / Edit MSWP…" button for a given gender. The substitutions grid is now pure READ-ONLY: each row is
''' authored in the modal <see cref="MswpSubEntryEditor_Form"/> (Original from THAT GENDER'S mesh NIF materials
''' + free-typed fallback, Replacement typed or picked, optional Color Remap). This kills the reentrant
''' <c>SetCurrentCellAddressCore</c> crash the old inline combo/text cells caused.
'''
''' A working list (<see cref="_subs"/>) is the source of truth: populated from the draft on open, mutated by
''' the Add / Edit / Remove buttons (and the double-click modal), and flushed back into the passed-in
''' <see cref="MswpDraft"/> on OK. The editor does NOT touch the ESP; the MswpDraft is persisted by the
''' existing Save flow when an ARMA/ARMO draft references it.</summary>
Public Class MswpSubEditor_Form

    Private ReadOnly _mainForm As MainForm
    Private ReadOnly _draft As MswpDraft
    ''' <summary>Deep clone of the draft at open time — the baseline for the "dirty only on real change" rule
    ''' (mirror of the ArmA/ArmO editors' _openSnapshot). Nothing when the passed-in draft is Nothing.</summary>
    Private ReadOnly _openSnapshot As MswpDraft
    ''' <summary>Material paths the gender mesh NIF references (BaseMaterials). Seeds the Original combo in the
    ''' per-substitution modal. Empty when no mesh path was supplied or the mesh couldn't be loaded.</summary>
    Private ReadOnly _meshMaterials As New List(Of String)
    ''' <summary>Working list of substitutions (source of truth). Loaded from the draft, mutated by the buttons/
    ''' modal, flushed back into the draft on OK. Never aliased to the draft's own list (copied in and out).</summary>
    Private ReadOnly _subs As New List(Of MSWP_Substitution)
    ''' <summary>Fixed type prefix for MSWP base EditorIDs ("npcm_MSWP_"). Save injects the &lt;plugin&gt; segment.</summary>
    Private ReadOnly _edidPrefix As String = MswpDraft.EditorIdPrefix

    ''' <param name="mainForm">Owner — used for EditorID uniqueness checks.</param>
    ''' <param name="draft">The MSWP draft being authored (already registered on MainForm). Flushed on OK.</param>
    ''' <param name="genderMeshPath">The gender's MOD2 (male) / MOD3 (female) mesh path. Its NIF materials
    ''' seed the Original combo in the modal. Empty → free-text Original only.</param>
    ''' <param name="genderLabel">"Male"/"Female", shown in the caption.</param>
    ''' <param name="extraMeshPaths">Optional additional mesh paths whose NIF materials are ALSO merged into the
    ''' Original-Material list (deduped by material path). Used by the ARMO editor to seed the list from every
    ''' included ARMA addon mesh in addition to the ARMO's own gender world-model mesh. Null → gender mesh only.</param>
    Public Sub New(mainForm As MainForm, draft As MswpDraft, genderMeshPath As String, genderLabel As String,
                   Optional extraMeshPaths As IEnumerable(Of String) = Nothing)
        InitializeComponent()
        _mainForm = mainForm
        _draft = draft
        _openSnapshot = draft?.Clone()

        Text = $"Material Swap (MSWP) — {genderLabel}"
        ' Original-Material list = the gender mesh's NIF materials PLUS any supplied extra meshes' materials
        ' (LoadMeshMaterials merges into the shared _meshMaterials list, dedups by material path, and tolerates
        ' null/empty/unloadable paths — so repeated calls are safe).
        LoadMeshMaterials(genderMeshPath)
        If extraMeshPaths IsNot Nothing Then
            For Each p In extraMeshPaths
                LoadMeshMaterials(p)
            Next
        End If
        BuildGridColumns()

        RefreshEditorIdField()
        LoadSubsFromDraft()
        RefreshGrid()

        AddHandler TextBoxEdid.TextChanged, AddressOf OnEdidChanged
        AddHandler ButtonAddRow.Click, AddressOf OnAddSub
        AddHandler ButtonEditRow.Click, AddressOf OnEditSub
        AddHandler ButtonRemoveRow.Click, AddressOf OnRemoveSub
        AddHandler GridSubs.CellDoubleClick, AddressOf OnSubDoubleClick
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ''' <summary>Drive the shared EditorID field: a NEW draft edits only the &lt;name&gt; (fixed prefix + live
    ''' "Saves as:" preview); an OVERRIDE draft keeps its record EDID read-only. A null draft behaves as NEW/empty.</summary>
    Private Sub RefreshEditorIdField()
        If _draft Is Nothing Then
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview, _edidPrefix, "")
        ElseIf _draft.IsNew Then
            EditorIdField.ConfigureNew(LabelEdid, TextBoxEdid, LabelEdidPreview, _edidPrefix, _draft.EditorID)
        Else
            EditorIdField.ConfigureOverride(LabelEdid, TextBoxEdid, LabelEdidPreview, _draft.EditorID)
        End If
    End Sub

    ''' <summary>Keep the live "Saves as:" preview in sync with the name box (only while the box is editable, i.e.
    ''' a NEW draft; an OVERRIDE keeps the box disabled and the preview hidden).</summary>
    Private Sub OnEdidChanged(sender As Object, e As EventArgs)
        If TextBoxEdid.Enabled Then EditorIdField.UpdatePreview(LabelEdidPreview, _edidPrefix, TextBoxEdid.Text)
    End Sub

    ''' <summary>Load the BaseMaterials (referenced material paths) of the gender mesh into
    ''' <see cref="_meshMaterials"/>. Resolves the mesh via FilesDictionary (loose &gt; BA2). Tolerant of a
    ''' missing/unparseable mesh (leaves the list empty → free-text Original in the modal).</summary>
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

        If _meshMaterials.Count = 0 Then
            Logger.LogLazy(Function() $"[MSWP-MAT] no NIF materials for '{genderMeshPath}' (resolved key '{key}') — Original-Material list empty (free-text fallback).")
        End If
    End Sub

    ''' <summary>Build the 3 READ-ONLY grid columns. No combo/text editable cells — the row is edited in the
    ''' modal <see cref="MswpSubEntryEditor_Form"/>, so a not-listed / empty Original can never surface the
    ''' default DataGridView error dialog.</summary>
    Private Sub BuildGridColumns()
        GridSubs.AutoGenerateColumns = False
        GridSubs.Columns.Clear()
        GridSubs.Columns.Add(NewReadOnlyCol("Original Material (BNAM)", 42))
        GridSubs.Columns.Add(NewReadOnlyCol("Replacement Material (SNAM)", 42))
        GridSubs.Columns.Add(NewReadOnlyCol("Color Remap", 16))
    End Sub

    Private Shared Function NewReadOnlyCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    Private Sub LoadSubsFromDraft()
        _subs.Clear()
        If _draft Is Nothing Then Return
        For Each s In _draft.Substitutions
            _subs.Add(New MSWP_Substitution With {
                .OriginalMaterial = s.OriginalMaterial, .ReplacementMaterial = s.ReplacementMaterial,
                .TreeFolder = s.TreeFolder, .HasColorRemapIndex = s.HasColorRemapIndex, .ColorRemapIndex = s.ColorRemapIndex})
        Next
    End Sub

    ''' <summary>Repaint the grid from <see cref="_subs"/> (read-only summary rows). Called only from load /
    ''' button handlers — NEVER from a cell event, so no reentrant Rows.Clear.</summary>
    Private Sub RefreshGrid()
        Dim selIdx = If(GridSubs.CurrentRow IsNot Nothing, GridSubs.CurrentRow.Index, -1)
        GridSubs.Rows.Clear()
        For Each s In _subs
            Dim remap = If(s.HasColorRemapIndex, s.ColorRemapIndex.ToString(CultureInfo.InvariantCulture), "")
            GridSubs.Rows.Add(If(s.OriginalMaterial, ""), If(s.ReplacementMaterial, ""), remap)
        Next
        If selIdx >= 0 AndAlso selIdx < GridSubs.Rows.Count Then
            GridSubs.Rows(selIdx).Selected = True
            GridSubs.CurrentCell = GridSubs.Rows(selIdx).Cells(0)
        End If
    End Sub

    ''' <summary>Add → open the modal on a fresh substitution; on OK append the returned copy.</summary>
    Private Sub OnAddSub(sender As Object, e As EventArgs)
        Using dlg As New MswpSubEntryEditor_Form(_meshMaterials, New MSWP_Substitution())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultSub IsNot Nothing Then
                _subs.Add(dlg.ResultSub)
                RefreshGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditSub(sender As Object, e As EventArgs)
        EditSubAt(SelectedSubIndex())
    End Sub

    ''' <summary>Double-click a row → edit that substitution in the modal. Safe: the grid is read-only ⇒ no cell
    ''' in edit mode ⇒ no reentrant <c>SetCurrentCellAddressCore</c>.</summary>
    Private Sub OnSubDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditSubAt(e.RowIndex)
    End Sub

    Private Sub EditSubAt(i As Integer)
        If i < 0 OrElse i >= _subs.Count Then Return
        Using dlg As New MswpSubEntryEditor_Form(_meshMaterials, _subs(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultSub IsNot Nothing Then
                _subs(i) = dlg.ResultSub
                RefreshGrid()
            End If
        End Using
    End Sub

    Private Sub OnRemoveSub(sender As Object, e As EventArgs)
        Dim i = SelectedSubIndex()
        If i < 0 Then Return
        _subs.RemoveAt(i)
        RefreshGrid()
    End Sub

    Private Function SelectedSubIndex() As Integer
        If GridSubs.CurrentRow Is Nothing Then Return -1
        Dim i = GridSubs.CurrentRow.Index
        If i < 0 OrElse i >= _subs.Count Then Return -1
        Return i
    End Function

    ''' <summary>Commit the EditorID + working list into the draft. Validates the EditorID (non-empty + unique,
    ''' unless unchanged on the same draft) and that at least one usable substitution exists. Vetoes the close
    ''' (DialogResult.None) on a validation failure.</summary>
    Private Sub OnOk(sender As Object, e As EventArgs)
        If _draft Is Nothing Then
            DialogResult = DialogResult.OK
            Close()
            Return
        End If

        ' NEW draft: the box holds only the <name>; compose the stored base EDID (Save injects <plugin>). OVERRIDE:
        ' the box holds the kept EDID verbatim (read-only). A NEW draft must still supply a non-empty name.
        Dim edid = If(_draft IsNot Nothing AndAlso Not _draft.IsNew,
                      TextBoxEdid.Text.Trim(),
                      EditorIdField.Compose(_edidPrefix, TextBoxEdid.Text))
        If TextBoxEdid.Text.Trim().Length = 0 Then
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

        ' Drop any content-less rows (no Original AND no Replacement) defensively — the modal already rejects them.
        Dim subs = _subs.Where(Function(s) Not (String.IsNullOrEmpty(s.OriginalMaterial) AndAlso
                                                String.IsNullOrEmpty(s.ReplacementMaterial))).ToList()
        If subs.Count = 0 Then
            MessageBox.Show(Me, "Add at least one material substitution (Original + Replacement) before saving.",
                            "MSWP", MessageBoxButtons.OK, MessageBoxIcon.Information)
            DialogResult = DialogResult.None
            Return
        End If

        _draft.EditorID = edid
        _draft.Substitutions.Clear()
        _draft.Substitutions.AddRange(subs)
        ' Dirty only on a REAL change (mirror of ArmA/ArmO): an OVERRIDE opened and OK'd without edits is not
        ' marked modified, so the saver won't re-emit an identical MSWP override. NEW drafts are always dirty.
        If Not _draft.IsNew Then
            _draft.IsModified = (_openSnapshot Is Nothing) OrElse Not _draft.ContentEquals(_openSnapshot)
        End If

        DialogResult = DialogResult.OK
        Close()
    End Sub

End Class
