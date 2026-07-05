Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Modal sub-editor for a SINGLE Object Template (OBTS) combination of an ARMO — opened from the
''' ARMO Editor's "Object Template" tab (Add / Duplicate / double-click a row). Mirror of
''' <see cref="MswpSubEditor_Form"/>: Designer-built UI, working buffers edited in place, deep-copy at the
''' borders so a Cancel never mutates the caller's list.
'''
''' Edits the combination's scalar fields (DisplayName, IsDefault, IsEditorOnly, ParentCombinationIndex,
''' the four level bytes) plus three list sections: Keywords (KYWD FormIDs), Includes (OMOD references with
''' Attach Point Index + Optional/DontUseAll flags) and Properties (direct 24-byte OMOD property overrides,
''' <see cref="OMOD_Property"/>). On OK it produces a fresh <see cref="ARMO_Combination"/> (deep-copied via
''' <see cref="ArmoDraft.CloneCombinations"/>) exposed through <see cref="ResultCombination"/>; the caller
''' reads it only when DialogResult = OK.
'''
''' Value model faithfulness: an OMOD_Property's <c>Value1</c> is stored as the raw 4-byte value reinterpreted
''' as a Single (matching the parser <see cref="CraftingRecordParsers.ParseObjectModProperty"/> and the writer
''' <c>WriteObtsProperty</c>). For FormID-typed properties (<see cref="OMOD_ValueType.FormIDInt"/> /
''' <see cref="OMOD_ValueType.FormIDFloat"/>) the FormID lives in <c>Value1FormID</c> and Value1 mirrors its
''' bits; for FloatType Value1 is the float directly; for the integer buckets Value1 holds the Int32 bits. The
''' editor keeps all three in sync so both the round-trip parser check and the new-record writer agree.</summary>
Public Class ObtsCombinationEditor_Form

    Private ReadOnly _mainForm As MainForm

    ' Working buffers (the editor's source of truth). Flushed from the grids/list on demand and copied
    ' out into ResultCombination on OK. Never aliased to the caller's combination (deep-copied in the ctor).
    Private ReadOnly _keywords As New List(Of UInteger)
    Private ReadOnly _includes As New List(Of ARMO_CombinationInclude)
    Private ReadOnly _properties As New List(Of OMOD_Property)

    ''' <summary>The edited combination, valid only after <c>DialogResult.OK</c>. A fresh deep-copy — the caller
    ''' owns it outright.</summary>
    Public ReadOnly Property ResultCombination As ARMO_Combination
        Get
            Return _result
        End Get
    End Property
    Private _result As ARMO_Combination

    ''' <param name="mainForm">Owner — supplies the PluginManager for the FormID pickers and display-name lookups.</param>
    ''' <param name="combo">The combination to edit. DEEP-COPIED in (never aliased); a null combo starts empty.</param>
    Public Sub New(mainForm As MainForm, combo As ARMO_Combination)
        InitializeComponent()
        _mainForm = mainForm

        BuildIncludesGridColumns()
        BuildPropertiesGridColumns()

        ' Deep-copy the incoming combination into the working buffers so Cancel leaves the caller's list intact.
        Dim src = If(combo, New ARMO_Combination())
        Dim copy = ArmoDraft.CloneCombinations(New List(Of ARMO_Combination) From {src})(0)
        LoadCombinationIntoPanels(copy)

        ' Keywords.
        AddHandler ButtonAddKeyword.Click, AddressOf OnAddKeyword
        AddHandler ButtonRemoveKeyword.Click, AddressOf OnRemoveKeyword
        ' Includes — grid is read-only; every mutation goes through a button / the double-click modal.
        AddHandler ButtonAddInclude.Click, AddressOf OnAddInclude
        AddHandler ButtonEditInclude.Click, AddressOf OnEditInclude
        AddHandler ButtonRemoveInclude.Click, AddressOf OnRemoveInclude
        AddHandler ButtonIncludeUp.Click, Sub() MoveInclude(-1)
        AddHandler ButtonIncludeDown.Click, Sub() MoveInclude(1)
        AddHandler GridIncludes.CellDoubleClick, AddressOf OnIncludeDoubleClick
        ' Properties — grid is read-only; every mutation goes through a button / the double-click modal.
        AddHandler ButtonAddProp.Click, AddressOf OnAddProp
        AddHandler ButtonEditProp.Click, AddressOf OnEditProp
        AddHandler ButtonRemoveProp.Click, AddressOf OnRemoveProp
        AddHandler GridProperties.CellDoubleClick, AddressOf OnPropCellDoubleClick
        ' Bottom.
        AddHandler ButtonOk.Click, AddressOf OnOk
    End Sub

    ' =====================================================================
    ' Grid column construction (Designer rule: typed/variable columns in code)
    ' =====================================================================

    ''' <summary>Includes grid = pure READ-ONLY summary. Bools render as text; the row is edited in the modal
    ''' <see cref="ObtsIncludeEditor_Form"/>. No editable (combo/checkbox) columns → no reentrant cell edits.</summary>
    Private Sub BuildIncludesGridColumns()
        GridIncludes.AutoGenerateColumns = False
        GridIncludes.Columns.Clear()
        GridIncludes.Columns.Add(NewReadOnlyCol("OMOD", 55))
        GridIncludes.Columns.Add(NewReadOnlyCol("Attach Point Index", 20))
        GridIncludes.Columns.Add(NewReadOnlyCol("Optional", 12))
        GridIncludes.Columns.Add(NewReadOnlyCol("Don't Use All", 13))
    End Sub

    ''' <summary>Properties grid = pure READ-ONLY summary; the row is edited in the modal
    ''' <see cref="ObtsPropertyEditor_Form"/>. ValueType shows its enum name, Value1 its ValueType-aware display.
    ''' FunctionType / PropertyIndex are raw numbers (no named enum in the model — the xEdit name tables aren't
    ''' ported). No editable columns → no reentrant cell edits.</summary>
    Private Sub BuildPropertiesGridColumns()
        GridProperties.AutoGenerateColumns = False
        GridProperties.Columns.Clear()
        GridProperties.Columns.Add(NewReadOnlyCol("ValueType", 22))
        GridProperties.Columns.Add(NewReadOnlyCol("FunctionType (raw)", 16))
        GridProperties.Columns.Add(NewReadOnlyCol("Property Index (raw)", 16))
        GridProperties.Columns.Add(NewReadOnlyCol("Value1", 24))
        GridProperties.Columns.Add(NewReadOnlyCol("Value2", 11))
        GridProperties.Columns.Add(NewReadOnlyCol("Step", 11))
    End Sub

    Private Shared Function NewReadOnlyCol(header As String, weight As Single) As DataGridViewTextBoxColumn
        Return New DataGridViewTextBoxColumn With {
            .HeaderText = header, .FillWeight = weight, .AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, .ReadOnly = True}
    End Function

    ' =====================================================================
    ' Combination → panels
    ' =====================================================================

    Private Sub LoadCombinationIntoPanels(c As ARMO_Combination)
        TextBoxName.Text = c.DisplayName
        CheckIsDefault.Checked = c.IsDefault
        CheckIsEditorOnly.Checked = c.IsEditorOnly
        NumParent.Value = ClampDec(c.ParentCombinationIndex, NumParent)
        NumLevelMin.Value = ClampDec(c.LevelMin, NumLevelMin)
        NumLevelMax.Value = ClampDec(c.LevelMax, NumLevelMax)
        NumMinLevelForRanks.Value = ClampDec(c.MinLevelForRanks, NumMinLevelForRanks)
        NumAltLevelsPerTier.Value = ClampDec(c.AltLevelsPerTier, NumAltLevelsPerTier)

        _keywords.Clear()
        _keywords.AddRange(c.Keywords)
        RefreshKeywordsList()

        _includes.Clear()
        For Each inc In c.Includes
            _includes.Add(New ARMO_CombinationInclude With {
                .ModFormID = inc.ModFormID, .AttachPointIndex = inc.AttachPointIndex,
                .IsOptional = inc.IsOptional, .DontUseAll = inc.DontUseAll})
        Next
        RefreshIncludesGrid()

        _properties.Clear()
        For Each p In c.Properties
            _properties.Add(New OMOD_Property With {
                .ValueType = p.ValueType, .FunctionType = p.FunctionType, .PropertyIndex = p.PropertyIndex,
                .Value1 = p.Value1, .Value1FormID = p.Value1FormID, .Value2 = p.Value2, .StepValue = p.StepValue})
        Next
        RefreshPropertiesGrid()
    End Sub

    ' =====================================================================
    ' Keywords
    ' =====================================================================

    Private Sub OnAddKeyword(sender As Object, e As EventArgs)
        ' Combination match keywords are general → EXCLUDE attach-point keywords (KYWD.TNAM type, not a name
        ' heuristic); the picker's "Show all" escapes the filter.
        Using dlg As New FormIdPicker_Form(_mainForm.PluginManagerForEditor, {"KYWD"},
                                           "Add combination keyword (KYWD)", 0UI, allowNull:=False,
                                           formIdFilter:=Function(fid) Not _mainForm.IsAttachPointKeyword(fid))
            If dlg.ShowDialog(Me) <> DialogResult.OK OrElse dlg.SelectedFormID = 0UI Then Return
            If Not _keywords.Contains(dlg.SelectedFormID) Then _keywords.Add(dlg.SelectedFormID)
            RefreshKeywordsList()
        End Using
    End Sub

    Private Sub OnRemoveKeyword(sender As Object, e As EventArgs)
        If ListKeywords.SelectedItems.Count = 0 Then Return
        Dim fid = CUInt(ListKeywords.SelectedItems(0).Tag)
        _keywords.Remove(fid)
        RefreshKeywordsList()
    End Sub

    Private Sub RefreshKeywordsList()
        ListKeywords.BeginUpdate()
        Try
            ListKeywords.Items.Clear()
            For Each fid In _keywords
                Dim row As New ListViewItem($"{DisplayFor(fid)} [0x{fid:X8}]")
                row.Tag = fid
                ListKeywords.Items.Add(row)
            Next
        Finally
            ListKeywords.EndUpdate()
        End Try
    End Sub

    ' =====================================================================
    ' Includes (OMOD references)
    ' =====================================================================

    ''' <summary>Repaint the includes grid from <see cref="_includes"/> (read-only summary rows; bools as text).
    ''' Called only from load / button handlers — NEVER from a cell event, so no reentrant Rows.Clear.</summary>
    Private Sub RefreshIncludesGrid()
        Dim selIdx = If(GridIncludes.CurrentRow IsNot Nothing, GridIncludes.CurrentRow.Index, -1)
        GridIncludes.Rows.Clear()
        For Each inc In _includes
            GridIncludes.Rows.Add($"{DisplayFor(inc.ModFormID)} [0x{inc.ModFormID:X8}]",
                                  inc.AttachPointIndex.ToString(CultureInfo.InvariantCulture),
                                  BoolText(inc.IsOptional), BoolText(inc.DontUseAll))
        Next
        SelectGridRow(GridIncludes, selIdx)
    End Sub

    ''' <summary>Add → open the modal on a fresh include; on OK append the returned (deep-copied) result.</summary>
    Private Sub OnAddInclude(sender As Object, e As EventArgs)
        Using dlg As New ObtsIncludeEditor_Form(_mainForm, New ARMO_CombinationInclude())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultInclude IsNot Nothing Then
                _includes.Add(dlg.ResultInclude)
                RefreshIncludesGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditInclude(sender As Object, e As EventArgs)
        EditIncludeAt(SelectedIncludeIndex())
    End Sub

    ''' <summary>Double-click a row → edit that include in the modal. Safe: the grid is read-only, so there is no
    ''' cell in edit mode ⇒ no reentrant <c>SetCurrentCellAddressCore</c>.</summary>
    Private Sub OnIncludeDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditIncludeAt(e.RowIndex)
    End Sub

    Private Sub EditIncludeAt(i As Integer)
        If i < 0 OrElse i >= _includes.Count Then Return
        Using dlg As New ObtsIncludeEditor_Form(_mainForm, _includes(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultInclude IsNot Nothing Then
                _includes(i) = dlg.ResultInclude
                RefreshIncludesGrid()
            End If
        End Using
    End Sub

    Private Sub OnRemoveInclude(sender As Object, e As EventArgs)
        Dim i = SelectedIncludeIndex()
        If i < 0 Then Return
        _includes.RemoveAt(i)
        RefreshIncludesGrid()
    End Sub

    Private Sub MoveInclude(delta As Integer)
        Dim i = SelectedIncludeIndex()
        If i < 0 Then Return
        Dim j = i + delta
        If j < 0 OrElse j >= _includes.Count Then Return
        Dim tmp = _includes(i)
        _includes(i) = _includes(j)
        _includes(j) = tmp
        RefreshIncludesGrid()
        SelectGridRow(GridIncludes, j)
    End Sub

    Private Function SelectedIncludeIndex() As Integer
        If GridIncludes.CurrentRow Is Nothing Then Return -1
        Dim i = GridIncludes.CurrentRow.Index
        If i < 0 OrElse i >= _includes.Count Then Return -1
        Return i
    End Function

    ' =====================================================================
    ' Properties (direct OMOD property overrides)
    ' =====================================================================

    ''' <summary>Repaint the properties grid from <see cref="_properties"/> (read-only summary rows; a row is
    ''' edited in the modal). Called only from load / button handlers — NEVER from a cell event.</summary>
    Private Sub RefreshPropertiesGrid()
        Dim selIdx = If(GridProperties.CurrentRow IsNot Nothing, GridProperties.CurrentRow.Index, -1)
        GridProperties.Rows.Clear()
        For Each p In _properties
            GridProperties.Rows.Add(p.ValueType.ToString(),
                                    p.FunctionType.ToString(CultureInfo.InvariantCulture),
                                    p.PropertyIndex.ToString(CultureInfo.InvariantCulture),
                                    Value1Display(p),
                                    Value2Display(p),
                                    FloatText(p.StepValue))
        Next
        SelectGridRow(GridProperties, selIdx)
    End Sub

    ''' <summary>Add → open the modal on a fresh IntType property; on OK append the returned copy.</summary>
    Private Sub OnAddProp(sender As Object, e As EventArgs)
        Using dlg As New ObtsPropertyEditor_Form(_mainForm, New OMOD_Property With {.ValueType = OMOD_ValueType.IntType})
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultProperty IsNot Nothing Then
                _properties.Add(dlg.ResultProperty)
                RefreshPropertiesGrid()
            End If
        End Using
    End Sub

    Private Sub OnEditProp(sender As Object, e As EventArgs)
        EditPropAt(SelectedPropIndex())
    End Sub

    ''' <summary>Double-click a row → edit that property in the modal. Safe: the grid is read-only ⇒ no cell in
    ''' edit mode ⇒ no reentrant <c>SetCurrentCellAddressCore</c>.</summary>
    Private Sub OnPropCellDoubleClick(sender As Object, e As DataGridViewCellEventArgs)
        If e.RowIndex < 0 Then Return
        EditPropAt(e.RowIndex)
    End Sub

    ''' <summary>Open the modal on the property at <paramref name="i"/> (deep-copied in/out by the modal, with
    ''' the SAME bit-exact Value1 semantics the old inline editor used); on OK replace the row's property. An
    ''' existing FormID property's Value1FormID is shown as-is — never re-resolved or overwritten on open.</summary>
    Private Sub EditPropAt(i As Integer)
        If i < 0 OrElse i >= _properties.Count Then Return
        Using dlg As New ObtsPropertyEditor_Form(_mainForm, _properties(i))
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultProperty IsNot Nothing Then
                _properties(i) = dlg.ResultProperty
                RefreshPropertiesGrid()
            End If
        End Using
    End Sub

    Private Sub OnRemoveProp(sender As Object, e As EventArgs)
        Dim i = SelectedPropIndex()
        If i < 0 Then Return
        _properties.RemoveAt(i)
        RefreshPropertiesGrid()
    End Sub

    Private Function SelectedPropIndex() As Integer
        If GridProperties.CurrentRow Is Nothing Then Return -1
        Dim i = GridProperties.CurrentRow.Index
        If i < 0 OrElse i >= _properties.Count Then Return -1
        Return i
    End Function

    ''' <summary>Display text for a property's Value1: the FormID (Name + hex) for FormID types, the float for
    ''' FloatType, else the Int32 reinterpretation of the raw bits.</summary>
    Private Function Value1Display(p As OMOD_Property) As String
        Select Case p.ValueType
            Case OMOD_ValueType.FormIDInt, OMOD_ValueType.FormIDFloat
                If p.Value1FormID = 0UI Then Return "(none)"
                Return $"{DisplayFor(p.Value1FormID)} [0x{p.Value1FormID:X8}]"
            Case OMOD_ValueType.FloatType
                Return FloatText(p.Value1)
            Case Else
                Return BitConverter.ToInt32(BitConverter.GetBytes(p.Value1), 0).ToString(CultureInfo.InvariantCulture)
        End Select
    End Function

    ''' <summary>Value2 shown per its ValueType (xEdit <c>wbOMODDataPropertyValue2Decider</c>): float for
    ''' Float/FormIDFloat, the Int32 reinterpretation of the raw bits for Int/FormIDInt/Bool, "(unused)" for
    ''' String/Enum. Mirror of <see cref="ObtsPropertyEditor_Form"/>'s RenderValue2 — Value2 is NOT always a float
    ''' (showing an int's bits as a float gives a garbage denormal like 7.16E-43).</summary>
    Private Shared Function Value2Display(p As OMOD_Property) As String
        Select Case p.ValueType
            Case OMOD_ValueType.FloatType, OMOD_ValueType.FormIDFloat
                Return FloatText(p.Value2)
            Case OMOD_ValueType.StringType, OMOD_ValueType.EnumType
                Return "(unused)"
            Case Else   ' Int, Bool, FormIDInt → the Int32 bits
                Return BitConverter.ToInt32(BitConverter.GetBytes(p.Value2), 0).ToString(CultureInfo.InvariantCulture)
        End Select
    End Function

    ' =====================================================================
    ' OK — build the result combination (deep-copied out)
    ' =====================================================================

    Private Sub OnOk(sender As Object, e As EventArgs)
        ' No grid flush needed — the grids are read-only; the working buffers are already authoritative.
        Dim built As New ARMO_Combination With {
            .DisplayName = TextBoxName.Text.Trim(),
            .IsDefault = CheckIsDefault.Checked,
            .IsEditorOnly = CheckIsEditorOnly.Checked,
            .ParentCombinationIndex = CInt(NumParent.Value),
            .LevelMin = CByte(NumLevelMin.Value),
            .LevelMax = CByte(NumLevelMax.Value),
            .MinLevelForRanks = CByte(NumMinLevelForRanks.Value),
            .AltLevelsPerTier = CByte(NumAltLevelsPerTier.Value)
        }
        built.Keywords.AddRange(_keywords)
        built.Includes.AddRange(_includes)
        built.Properties.AddRange(_properties)

        ' Deep-copy out so the returned combination never aliases the working buffers (single source of truth).
        _result = ArmoDraft.CloneCombinations(New List(Of ARMO_Combination) From {built})(0)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ' =====================================================================
    ' Helpers
    ' =====================================================================

    Private Function DisplayFor(fid As UInteger) As String
        If fid = 0UI Then Return "(none)"
        Return _mainForm.GetRecordDisplayNameForEditor(fid)
    End Function

    Private Shared Function FloatText(v As Single) As String
        Return v.ToString(CultureInfo.InvariantCulture)
    End Function

    Private Shared Function BoolText(b As Boolean) As String
        Return If(b, "✓", "")
    End Function

    ''' <summary>Restore a row selection after a Rows.Clear + re-add. FullRowSelect + read-only, so selecting
    ''' the row is enough; CurrentCell lands on column 0 (never an editable cell — there are none).</summary>
    Private Shared Sub SelectGridRow(grid As DataGridView, idx As Integer)
        If idx < 0 OrElse idx >= grid.Rows.Count Then Return
        grid.Rows(idx).Selected = True
        grid.CurrentCell = grid.Rows(idx).Cells(0)
    End Sub

    Private Shared Function ClampDec(v As Integer, num As NumericUpDown) As Decimal
        Dim d As Decimal = CDec(v)
        If d < num.Minimum Then Return num.Minimum
        If d > num.Maximum Then Return num.Maximum
        Return d
    End Function

End Class
