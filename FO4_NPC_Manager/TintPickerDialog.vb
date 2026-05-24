Imports FO4_Base_Library

''' <summary>Modal that lets the user pick one (group, option) from RACE.{Female|Male}TintTemplateGroups
''' to add as a new face tint layer in EditFace_Form. Two filters apply:
'''  • Mask entries (EntryType=Mask) are stripped — they are spatial-region declarations consumed
'''    by the REGION-SWAP render path (Forehead/Cheeks/etc. region masks), never painted directly.
'''  • Indices already present in the active layer list are stripped via <c>excludeIndices</c>,
'''    so the user only sees additions still available to add (caller pre-filters).
'''
''' What Mask options actually do (verified empirically in npc_preview.log REGION-SWAP entries
''' and in MainForm.BuildFaceRegionSwaps): the render iterates RACE.MorphGroups
''' (Forehead/Eyes/Nose/Ears/Cheeks/Mouth/Neck), each with an MPPK that maps to a TintSlot 0..6.
''' For each slot it looks up THE Mask option for that slot via FindTintOptionsBySlot and uses
''' its TTET[0] DDS (e.g. EarsMask.dds, CheeksMask.dds, MouthMask.dds) as the spatial region
''' for any active morph preset's MPPT TXST swap. So a Mask option declares "this is the
''' geometry of the cheek region" — it is never painted directly with a TEND colour. The
''' user-paintable layers are Palette options (LipColor, CheekColor, Eyeliner, etc.) which
''' carry their own TTET[0] mask plus a TemplateColors palette.
'''
''' Output: <see cref="SelectedOption"/> = the chosen RACE_TintTemplateOption, or Nothing on Cancel.
''' </summary>
Partial Public Class TintPickerDialog

    Public Property SelectedOption As RACE_TintTemplateOption

    ''' <summary>Every (option, ListViewItem) pair built in the constructor. The TextBoxFilter
    ''' TextChanged handler clears + repopulates <see cref="TintList"/> from this snapshot,
    ''' showing only entries whose Group/Option/Slot/Type contain the filter substring
    ''' (case-insensitive). Empty filter shows all.</summary>
    Private ReadOnly _allRows As New List(Of ListViewItem)

    Public Sub New(groups As List(Of RACE_TintTemplateGroup), excludeIndices As ICollection(Of UShort))
        InitializeComponent()
        Dim exclude = If(excludeIndices, CType(New HashSet(Of UShort)(), ICollection(Of UShort)))
        ' Mask entries filtered out: they are spatial-region declarations consumed by the
        ' REGION-SWAP path, never painted directly. See class docstring for full rationale.
        ' Already-active indices filtered out via excludeIndices: the user shouldn't see options
        ' they can't add (vanilla NPCs carry one layer per option Index — duplicates would
        ' over-saturate the compositor with no way to disambiguate them in the detail panel).
        For Each grp In groups
            For Each opt In grp.Options
                If opt.EntryType = RACE_TintEntryType.Mask Then Continue For
                If exclude.Contains(opt.Index) Then Continue For
                Dim row As New ListViewItem(If(grp.GroupName, ""))
                row.SubItems.Add(If(opt.Name, ""))
                row.SubItems.Add(opt.Slot.ToString())
                row.SubItems.Add(opt.EntryType.ToString())
                row.SubItems.Add(opt.Index.ToString())
                row.Tag = opt
                _allRows.Add(row)
                TintList.Items.Add(row)
            Next
        Next

        AddHandler ButtonOk.Click, AddressOf OnOk
        AddHandler TintList.DoubleClick, AddressOf OnOk
        AddHandler TextBoxFilter.TextChanged, AddressOf OnFilterChanged
        SortableListView.Attach(TintList)
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If TintList.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        SelectedOption = TryCast(TintList.SelectedItems(0).Tag, RACE_TintTemplateOption)
        DialogResult = DialogResult.OK
        Close()
    End Sub

    ''' <summary>Substring match across Group + Option + Slot + Type columns, case-insensitive.
    ''' Empty filter shows everything. Repopulates the ListView from <see cref="_allRows"/> on
    ''' every keystroke — the candidate set is small (~30-50 rows for vanilla races) so a
    ''' Clear+Add loop is faster + simpler than per-row visibility tracking.</summary>
    Private Sub OnFilterChanged(sender As Object, e As EventArgs)
        Dim filter = TextBoxFilter.Text.Trim()
        TintList.BeginUpdate()
        Try
            TintList.Items.Clear()
            If filter.Length = 0 Then
                For Each row In _allRows
                    TintList.Items.Add(row)
                Next
                Return
            End If
            For Each row In _allRows
                Dim grp = If(row.SubItems.Count > 0, row.Text, "")
                Dim optName = If(row.SubItems.Count > 1, row.SubItems(1).Text, "")
                Dim slot = If(row.SubItems.Count > 2, row.SubItems(2).Text, "")
                Dim kind = If(row.SubItems.Count > 3, row.SubItems(3).Text, "")
                If grp.Contains(filter, StringComparison.OrdinalIgnoreCase) _
                   OrElse optName.Contains(filter, StringComparison.OrdinalIgnoreCase) _
                   OrElse slot.Contains(filter, StringComparison.OrdinalIgnoreCase) _
                   OrElse kind.Contains(filter, StringComparison.OrdinalIgnoreCase) Then
                    TintList.Items.Add(row)
                End If
            Next
        Finally
            TintList.EndUpdate()
        End Try
    End Sub
End Class

