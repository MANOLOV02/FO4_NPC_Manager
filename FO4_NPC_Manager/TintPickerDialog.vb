Imports FO4_Base_Library

''' <summary>Modal that lets the user pick one (group, option) from RACE.{Female|Male}TintTemplateGroups
''' to add as a new face tint layer in EditFace_Form. Mask entries (EntryType=Mask) are filtered
''' out because they are not user-editable colour layers.
'''
''' What Mask options actually do (verified empirically in npc_preview.log REGION-SWAP entries
''' and in MainForm.BuildFaceRegionSwaps:2920-2922): the render iterates RACE.MorphGroups
''' (Forehead/Eyes/Nose/Ears/Cheeks/Mouth/Neck), each with an MPPK that maps to a TintSlot 0..6.
''' For each slot it looks up THE Mask option for that slot via FindTintOptionsBySlot and uses
''' its TTET[0] DDS (e.g. EarsMask.dds, CheeksMask.dds, MouthMask.dds) as the spatial region
''' for any active morph preset's MPPT TXST swap. So a Mask option declares "this is the
''' geometry of the cheek region" — it is never painted directly with a TEND colour. The
''' user-paintable layers are Palette options (LipColor, CheekColor, Eyeliner, etc.) which
''' carry their own TTET[0] mask plus a TemplateColors palette.
'''
''' Output: <see cref="SelectedOption"/> = the chosen RACE_TintTemplateOption, or Nothing on Cancel.
'''
''' Built code-only because the dialog is trivial (single ListView + OK/Cancel) and replicating
''' the Designer infrastructure for one disposable form is overhead. The host EditFace_Form is
''' Designer-built per the project rule, small one-off pickers like this can be inline.</summary>
Public Class TintPickerDialog
    Inherits Form

    Public Property SelectedOption As RACE_TintTemplateOption

    Private ReadOnly _list As ListView

    Public Sub New(groups As List(Of RACE_TintTemplateGroup))
        Text = "Add Face Tint"
        ClientSize = New Drawing.Size(560, 480)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        MaximizeBox = False

        _list = New ListView() With {
            .Dock = DockStyle.Fill, .View = View.Details,
            .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .GridLines = False}
        _list.Columns.Add("Group", 140)
        _list.Columns.Add("Option", 220)
        _list.Columns.Add("Slot", 50)
        _list.Columns.Add("Type", 90)
        _list.Columns.Add("Index", 60)

        ' Mask entries filtered out: they are spatial-region declarations consumed by the
        ' REGION-SWAP path, never painted directly. See class docstring for full rationale.
        For Each grp In groups
            For Each opt In grp.Options
                If opt.EntryType = RACE_TintEntryType.Mask Then Continue For
                Dim row As New ListViewItem(If(grp.GroupName, ""))
                row.SubItems.Add(If(opt.Name, ""))
                row.SubItems.Add(opt.Slot.ToString())
                row.SubItems.Add(opt.EntryType.ToString())
                row.SubItems.Add(opt.Index.ToString())
                row.Tag = opt
                _list.Items.Add(row)
            Next
        Next

        Dim ok As New Button() With {.Text = "OK", .DialogResult = DialogResult.OK, .Width = 80}
        Dim cancel As New Button() With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Width = 80}
        Dim btnRow As New FlowLayoutPanel() With {
            .Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(0, 6, 0, 0)}
        btnRow.Controls.Add(ok)
        btnRow.Controls.Add(cancel)

        Dim root As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Padding = New Padding(8)}
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        root.Controls.Add(_list, 0, 0)
        root.Controls.Add(btnRow, 0, 1)
        Controls.Add(root)

        AcceptButton = ok
        CancelButton = cancel

        AddHandler ok.Click, AddressOf OnOk
        AddHandler _list.DoubleClick, AddressOf OnOk
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _list.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        SelectedOption = TryCast(_list.SelectedItems(0).Tag, RACE_TintTemplateOption)
        DialogResult = DialogResult.OK
        Close()
    End Sub
End Class

''' <summary>Modal that lets the user pick one face bone region from a candidate list (RACE
''' face morph defs not yet present in the preset). Output: <see cref="SelectedRegionIndex"/>.</summary>
Public Class RegionPickerDialog
    Inherits Form

    Public Property SelectedRegionIndex As UInteger?

    Private ReadOnly _list As ListView

    Public Sub New(candidates As List(Of FacialBoneRegion))
        Text = "Add Face Bone Region"
        ClientSize = New Drawing.Size(440, 480)
        StartPosition = FormStartPosition.CenterParent
        MinimizeBox = False
        MaximizeBox = False

        _list = New ListView() With {
            .Dock = DockStyle.Fill, .View = View.Details,
            .FullRowSelect = True, .MultiSelect = False, .HideSelection = False, .GridLines = False}
        _list.Columns.Add("Region", 280)
        _list.Columns.Add("Index (hex)", 100)

        For Each def In candidates
            Dim row As New ListViewItem(If(def.Name, "(unnamed)"))
            row.SubItems.Add($"0x{def.ID:X8}")
            row.Tag = def
            _list.Items.Add(row)
        Next

        Dim ok As New Button() With {.Text = "OK", .DialogResult = DialogResult.OK, .Width = 80}
        Dim cancel As New Button() With {.Text = "Cancel", .DialogResult = DialogResult.Cancel, .Width = 80}
        Dim btnRow As New FlowLayoutPanel() With {
            .Dock = DockStyle.Bottom, .FlowDirection = FlowDirection.RightToLeft,
            .AutoSize = True, .AutoSizeMode = AutoSizeMode.GrowAndShrink,
            .Padding = New Padding(0, 6, 0, 0)}
        btnRow.Controls.Add(ok)
        btnRow.Controls.Add(cancel)

        Dim root As New TableLayoutPanel() With {.Dock = DockStyle.Fill, .ColumnCount = 1, .RowCount = 2,
            .Padding = New Padding(8)}
        root.RowStyles.Add(New RowStyle(SizeType.Percent, 100.0F))
        root.RowStyles.Add(New RowStyle(SizeType.AutoSize))
        root.ColumnStyles.Add(New ColumnStyle(SizeType.Percent, 100.0F))
        root.Controls.Add(_list, 0, 0)
        root.Controls.Add(btnRow, 0, 1)
        Controls.Add(root)

        AcceptButton = ok
        CancelButton = cancel

        AddHandler ok.Click, AddressOf OnOk
        AddHandler _list.DoubleClick, AddressOf OnOk
    End Sub

    Private Sub OnOk(sender As Object, e As EventArgs)
        If _list.SelectedItems.Count = 0 Then
            DialogResult = DialogResult.None
            Return
        End If
        Dim def = TryCast(_list.SelectedItems(0).Tag, FacialBoneRegion)
        If def Is Nothing Then Return
        SelectedRegionIndex = def.ID
        DialogResult = DialogResult.OK
        Close()
    End Sub
End Class
