Imports System.Globalization
Imports System.Linq
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Sub-editor modal de UNA combinación del Object Template (OBTS). Se abre desde la pestaña
''' "Object Template" del editor de ARMO y de la del editor de NPC_ (Add / Duplicate / doble clic en una fila).
'''
''' Edita los campos sueltos (nombre, Default, Editor Only, Parent Combination Index, los cuatro bytes de
''' nivel) y las tres listas: Keywords (KYWD), Includes (referencias a OMOD con su Attach Point Index y las
''' banderas Optional/DontUseAll) y Properties.
'''
''' <para>La combinación que recibe es la de TRABAJO: una copia independiente que el que llama arma sobre su
''' propio record antes de abrir el diálogo. Por eso se edita en el lugar y no hay nada que devolver —
''' cancelar deja la copia sin usar. Las tres listas se rehacen enteras al aceptar, así el orden y las bajas
''' quedan como los dejó el usuario.</para></summary>
Public Class ObtsCombinationEditor_Form

    Private ReadOnly _mainForm As MainForm

    ' La combinación de trabajo y las listas que el usuario reordena encima de ella. Los elementos de las
    ' dos listas son vistas sobre la propia combinación de trabajo: lo que el diálogo ordena y da de baja
    ' vive acá, y al aceptar se vuelca de una vez.
    Private ReadOnly _combo As Canon.IBloque_Combinations
    Private ReadOnly _keywords As New List(Of UInteger)
    Private ReadOnly _includes As New List(Of Canon.IBloque_Includes)
    Private ReadOnly _properties As New List(Of Canon.IBloque_Properties4)

    ''' <param name="mainForm">Owner — supplies the PluginManager for the FormID pickers and display-name lookups.</param>
    ''' <param name="combo">La combinación DE TRABAJO, que el que llama ya creó como copia aparte.</param>
    Public Sub New(mainForm As MainForm, combo As Canon.IBloque_Combinations)
        InitializeComponent()
        _mainForm = mainForm
        _combo = combo

        BuildIncludesGridColumns()
        BuildPropertiesGridColumns()

        LoadCombinationIntoPanels()

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
    ''' FunctionType / PropertyIndex are raw numbers (no named enum ported into the model).
    ''' No editable columns → no reentrant cell edits.</summary>
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

    Private Sub LoadCombinationIntoPanels()
        If _combo Is Nothing Then Return
        TextBoxName.Text = _combo.CombinationName
        CheckIsDefault.Checked = _combo.ObjectModTemplateItemDefault
        CheckIsEditorOnly.Checked = _combo.CombinationEditorOnly
        NumParent.Value = ClampDec(_combo.ObjectModTemplateItemParentCombinationIndex, NumParent)
        NumLevelMin.Value = ClampDec(_combo.ObjectModTemplateItemLevelMin, NumLevelMin)
        NumLevelMax.Value = ClampDec(_combo.ObjectModTemplateItemLevelMax, NumLevelMax)
        NumMinLevelForRanks.Value = ClampDec(_combo.ObjectModTemplateItemMinLevelForRanks, NumMinLevelForRanks)
        NumAltLevelsPerTier.Value = ClampDec(_combo.ObjectModTemplateItemAltLevelsPerTier, NumAltLevelsPerTier)

        _keywords.Clear()
        For Each kw In _combo.Keywords
            _keywords.Add(kw.Keyword)
        Next
        RefreshKeywordsList()

        _includes.Clear()
        _includes.AddRange(_combo.Includes)
        RefreshIncludesGrid()

        _properties.Clear()
        _properties.AddRange(_combo.Properties)
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
            GridIncludes.Rows.Add($"{DisplayFor(inc.IncludeMod)} [0x{inc.IncludeMod:X8}]",
                                  inc.IncludeAttachPointIndex.ToString(CultureInfo.InvariantCulture),
                                  BoolText(inc.IncludeOptional), BoolText(inc.IncludeDonTUseAll))
        Next
        SelectGridRow(GridIncludes, selIdx)
    End Sub

    ''' <summary>Agregar: el Include vacío se crea en la combinación de trabajo y se lo edita ahí. Si el
    ''' usuario cancela no entra a la lista, y al aceptar el diálogo grande la combinación se rehace desde la
    ''' lista, así que el nodo que quedó suelto se va con la reescritura.</summary>
    Private Sub OnAddInclude(sender As Object, e As EventArgs)
        Dim nuevo = _combo.AgregarIncludeDeCombinacion()
        If nuevo Is Nothing Then Return
        Using dlg As New ObtsIncludeEditor_Form(_mainForm, nuevo)
            If dlg.ShowDialog(Me) = DialogResult.OK Then
                _includes.Add(nuevo)
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
            If dlg.ShowDialog(Me) = DialogResult.OK Then RefreshIncludesGrid()
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
        For Each vista In _properties
            Dim p = vista.LeerPropiedad()
            GridProperties.Rows.Add(p.ValueType.ToString(),
                                    p.FunctionType.ToString(CultureInfo.InvariantCulture),
                                    p.PropertyIndex.ToString(CultureInfo.InvariantCulture),
                                    Value1Display(p),
                                    Value2Display(p),
                                    FloatText(p.StepValue))
        Next
        SelectGridRow(GridProperties, selIdx)
    End Sub

    ''' <summary>Agregar: el diálogo de una propiedad trabaja con el valor plano; lo que devuelve se escribe
    ''' en una Property nueva de la combinación de trabajo, por la rama de la union que le corresponde.</summary>
    Private Sub OnAddProp(sender As Object, e As EventArgs)
        Using dlg As New ObtsPropertyEditor_Form(_mainForm, New OMOD_Property With {.ValueType = OMOD_ValueType.IntType})
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultProperty IsNot Nothing Then
                Dim nueva = _combo.AgregarPropiedadDeCombinacion()
                If nueva Is Nothing Then Return
                nueva.EscribirPropiedad(dlg.ResultProperty)
                _properties.Add(nueva)
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
        Using dlg As New ObtsPropertyEditor_Form(_mainForm, _properties(i).LeerPropiedad())
            If dlg.ShowDialog(Me) = DialogResult.OK AndAlso dlg.ResultProperty IsNot Nothing Then
                _properties(i).EscribirPropiedad(dlg.ResultProperty)
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

    ''' <summary>Value2 shown per its ValueType: float for
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
    ' Aceptar — volcar lo editado a la combinación de trabajo
    ' =====================================================================

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _combo Is Nothing Then Return
        ' Las grillas son de sólo lectura: lo que muestran ya está en la combinación de trabajo o en las
        ' listas de acá, así que no hay nada que vaciar antes.
        _combo.CombinationName = TextBoxName.Text.Trim()
        _combo.ObjectModTemplateItemDefault = CheckIsDefault.Checked
        _combo.CombinationEditorOnly = CheckIsEditorOnly.Checked
        _combo.ObjectModTemplateItemParentCombinationIndex = CShort(NumParent.Value)
        _combo.ObjectModTemplateItemLevelMin = CByte(NumLevelMin.Value)
        _combo.ObjectModTemplateItemLevelMax = CByte(NumLevelMax.Value)
        _combo.ObjectModTemplateItemMinLevelForRanks = CByte(NumMinLevelForRanks.Value)
        _combo.ObjectModTemplateItemAltLevelsPerTier = CByte(NumAltLevelsPerTier.Value)

        _combo.ReemplazarKeywordsDeCombinacion(_keywords)
        _combo.ReemplazarIncludesDeCombinacion(_includes)
        _combo.ReemplazarPropiedadesDeCombinacion(_properties)

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
