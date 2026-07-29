
''' <summary>Shared "what do I take from this preset" control: one checkbox per appearance category,
''' with the amount the current preset carries for each. Hosted by BOTH consumers so the two can never
''' offer different category sets:
''' <list type="bullet">
''' <item><see cref="PasteOptionsDialog"/> — Paste Look (source = the clipboard preset).</item>
''' <item><see cref="LooksmenuLoad_Form"/> — the LooksMenu/RaceMenu preset browser, where it sits next to
''' the list and re-fires the live preview on every toggle.</item>
''' </list>
'''
''' <para>Rows for categories the running game doesn't have are collapsed (their layout row is zeroed, not
''' just hidden, so there is no gap). Rows the SELECTED preset doesn't carry stay visible but greyed and
''' unticked — the panel doubles as an inventory of the preset, which is why the counts are here and not
''' only in a summary line.</para>
'''
''' <para>Declared-but-empty is NOT "unavailable": a preset that declares an empty head-part list is an
''' authoritative wipe, so <see cref="PresetCategories.Describe"/> reports it available with count 0 and
''' the row stays selectable.</para></summary>
Public Class PresetCategoryPanel

    ''' <summary>One category row: its checkbox, its amount label, and where it lives in the layout so the
    ''' row can be collapsed for the other game.</summary>
    Private Class CategoryRow
        Public Cat As PresetCategory
        Public Chk As CheckBox
        Public Amount As Label
        Public Layout As TableLayoutPanel
        Public RowIndex As Integer
        Public BaseText As String = ""
        ''' <summary>Whether this row exists in the running game. Tracked HERE and never read back from
        ''' <c>Chk.Visible</c>: WinForms' Visible getter reports EFFECTIVE visibility, so while the hosting
        ''' form is still being constructed it returns False for every control — which silently collapsed
        ''' every group and made <see cref="Options"/> report all-False.</summary>
        Public Applies As Boolean = True
    End Class

    Private ReadOnly _rows As New List(Of CategoryRow)
    ''' <summary>What the USER last asked for per category, independent of whether the currently selected
    ''' preset happens to carry it. Without this, clicking through presets would silently forget the user's
    ''' choices every time a category momentarily goes unavailable.</summary>
    Private ReadOnly _userWanted As New Dictionary(Of PresetCategory, Boolean)
    Private _isSse As Boolean
    Private _suppress As Boolean

    ''' <summary>Raised whenever the effective selection changes (user toggle, Select all, Deselect all).
    ''' NOT raised by <see cref="SetPreset"/> — the caller is already reacting to that selection change.</summary>
    Public Event OptionsChanged As EventHandler

    Public Sub New()
        InitializeComponent()
        RegisterRow(PresetCategory.BodyWeight, CheckBoxBodyWeight, LabelCountBodyWeight, BodyLayout, 0)
        RegisterRow(PresetCategory.BodyRegions, CheckBoxBodyRegions, LabelCountBodyRegions, BodyLayout, 1)
        RegisterRow(PresetCategory.BodySliders, CheckBoxBodySliders, LabelCountBodySliders, BodyLayout, 2)
        RegisterRow(PresetCategory.BodyScale, CheckBoxBodyScale, LabelCountBodyScale, BodyLayout, 3)
        RegisterRow(PresetCategory.Overlays, CheckBoxOverlays, LabelCountOverlays, BodyLayout, 4)
        RegisterRow(PresetCategory.SkinOverride, CheckBoxSkinOverride, LabelCountSkinOverride, BodyLayout, 5)
        RegisterRow(PresetCategory.LmSkinTemplate, CheckBoxLmSkinTemplate, LabelCountLmSkinTemplate, BodyLayout, 6)
        RegisterRow(PresetCategory.Outfit, CheckBoxOutfit, LabelCountOutfit, BodyLayout, 7)
        RegisterRow(PresetCategory.FaceParts, CheckBoxFaceParts, LabelCountFaceParts, FaceLayout, 0)
        RegisterRow(PresetCategory.HairColor, CheckBoxHairColor, LabelCountHairColor, FaceLayout, 1)
        RegisterRow(PresetCategory.FaceTints, CheckBoxFaceTints, LabelCountFaceTints, FaceLayout, 2)
        RegisterRow(PresetCategory.FaceVertexMorphs, CheckBoxFaceVertexMorphs, LabelCountFaceVertexMorphs, FaceLayout, 3)
        RegisterRow(PresetCategory.CustomMorphs, CheckBoxCustomMorphs, LabelCountCustomMorphs, FaceLayout, 4)
        RegisterRow(PresetCategory.FaceBoneRegions, CheckBoxFaceBoneRegions, LabelCountFaceBoneRegions, FaceLayout, 5)
        RegisterRow(PresetCategory.Sculpt, CheckBoxSculpt, LabelCountSculpt, FaceLayout, 6)
        RegisterRow(PresetCategory.IsCharGenPreset, CheckBoxIsCharGenPreset, LabelCountIsCharGenPreset, FlagsLayout, 0)

        AddHandler ButtonSelectAll.Click, Sub(s, e) SetAll(True)
        AddHandler ButtonDeselectAll.Click, Sub(s, e) SetAll(False)
    End Sub

    Private Sub RegisterRow(cat As PresetCategory, chk As CheckBox, amount As Label, layout As TableLayoutPanel, rowIndex As Integer)
        Dim r As New CategoryRow With {
            .Cat = cat, .Chk = chk, .Amount = amount, .Layout = layout, .RowIndex = rowIndex, .BaseText = chk.Text
        }
        _rows.Add(r)
        _userWanted(cat) = chk.Checked
        AddHandler chk.CheckedChanged, AddressOf OnRowToggled
    End Sub

    Private Sub OnRowToggled(sender As Object, e As EventArgs)
        If _suppress Then Return
        Dim chk = TryCast(sender, CheckBox)
        For Each r In _rows
            If r.Chk Is chk Then
                _userWanted(r.Cat) = chk.Checked
                Exit For
            End If
        Next
        RaiseEvent OptionsChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Set the game once, before the first <see cref="SetPreset"/>. Collapses the rows that don't
    ''' exist in this engine and relabels the ones whose carrier differs (the FO4 wording would be wrong
    ''' under SSE: chargen MSDV vs NAM9, F4SE overlay templates vs RaceMenu overlay nodes).</summary>
    Public Sub ConfigureGame(isSse As Boolean)
        _isSse = isSse
        _suppress = True
        Try
            For Each r In _rows
                r.Applies = AppliesToGame(r.Cat, isSse)
                r.Chk.Visible = r.Applies
                r.Amount.Visible = r.Applies
                ' Zero the layout row too — hiding alone would leave a 24px hole in the group.
                r.Layout.RowStyles(r.RowIndex).Height = If(r.Applies, 24.0F, 0.0F)
                If Not r.Applies Then r.Chk.Checked = False
            Next

            If isSse Then
                CheckBoxBodyWeight.Text = "Body weight  (NAM7)"
                CheckBoxBodySliders.Text = "Body sliders  (BodySlide / BodyMorph)"
                CheckBoxOverlays.Text = "Overlays  (tattoos, body paint, skin overrides)"
                CheckBoxFaceVertexMorphs.Text = "Face morphs  (NAM9 sliders)"
                CheckBoxFaceTints.Text = "Face tints  (skin tone, warpaint, scars, …)"
            End If
            ' Keep BaseText in sync with whatever the game-specific relabel left, so the row tooltip and
            ' any future text decoration start from the right string.
            For Each r In _rows
                r.BaseText = r.Chk.Text
            Next

            ResizeGroups()
        Finally
            _suppress = False
        End Try
    End Sub

    ''' <summary>Shrink each group (and the layout row that holds it) to the rows that remain visible, so
    ''' collapsing the other game's categories doesn't leave empty space inside the group box.</summary>
    Private Sub ResizeGroups()
        Const RowHeight As Integer = 24
        Const GroupChrome As Integer = 34   ' caption + top/bottom padding of the GroupBox
        Dim bodyRows = VisibleRowCount(BodyLayout)
        Dim faceRows = VisibleRowCount(FaceLayout)
        Dim flagRows = VisibleRowCount(FlagsLayout)
        ' + Margin.Vertical: the GroupBox's margin is spent INSIDE its cell, so a row of exactly
        ' rows*RowHeight + GroupChrome leaves the box that many pixels short of its own content. Left out, the
        ' three groups were 6px short each — 18px the panel needs that no row declared, which is what made a
        ' host that shrink-wrapped to these numbers clip its own buttons.
        Root.RowStyles(0).Height = bodyRows * RowHeight + GroupChrome + GroupBoxBody.Margin.Vertical
        Root.RowStyles(1).Height = faceRows * RowHeight + GroupChrome + GroupBoxFace.Margin.Vertical
        Root.RowStyles(2).Height = flagRows * RowHeight + GroupChrome + GroupBoxFlags.Margin.Vertical
    End Sub

    ''' <summary>How many rows of one group survive the game gate.</summary>
    Private Function VisibleRowCount(layout As TableLayoutPanel) As Integer
        Dim n As Integer = 0
        For Each r In _rows
            If r.Layout Is layout AndAlso r.Applies Then n += 1
        Next
        Return n
    End Function

    ''' <summary>Total height the panel wants for the current game — lets a host size itself around the
    ''' panel instead of hard-coding a per-game number.
    ''' <para>The action row is measured by <see cref="Control.PreferredSize"/>, NOT by its current
    ''' <c>Height</c>: it is AutoSize inside the layout's last row, so its live height is whatever the host
    ''' happens to be giving it. Asking for that would answer "as much as you already gave me" and a host that
    ''' sized itself from the answer would keep its own dead space forever.</para></summary>
    Public ReadOnly Property PreferredPanelHeight As Integer
        Get
            Return CInt(Root.RowStyles(0).Height + Root.RowStyles(1).Height + Root.RowStyles(2).Height) +
                   QuickRow.PreferredSize.Height
        End Get
    End Property

    ''' <summary>Show what <paramref name="preset"/> carries: per-category amount, tooltip breakdown, and
    ''' grey/disable for the categories it doesn't carry. Categories that come back available are restored
    ''' to the user's last intent, not blindly re-ticked. Does NOT raise <see cref="OptionsChanged"/>.</summary>
    Public Sub SetPreset(preset As LooksmenuLoader.LooksmenuPreset)
        Dim info = Describe(preset, _isSse)
        ' No preset to inspect (nothing selected yet, or a caller that doesn't have one): show no amounts
        ' but leave every row selectable — "unknown source" must not silently disable everything, which
        ' would turn an OK into a no-op.
        Dim unknownSource As Boolean = (preset Is Nothing)
        _suppress = True
        Try
            For Each r In _rows
                If Not r.Applies Then Continue For
                Dim ci = If(unknownSource, New CategoryInfo With {.Available = True, .Text = "—"}, info(r.Cat))
                r.Amount.Text = If(ci.Available, ci.Text, "—")
                r.Chk.Enabled = ci.Available
                r.Chk.ForeColor = If(ci.Available, SystemColors.ControlText, SystemColors.GrayText)
                r.Chk.Checked = ci.Available AndAlso _userWanted(r.Cat)
                Dim tip As String = If(ci.Available,
                                       If(ci.Detail.Length > 0, ci.Detail, ""),
                                       "This preset carries nothing for this category.")
                Tips.SetToolTip(r.Chk, tip)
                Tips.SetToolTip(r.Amount, tip)
            Next
        Finally
            _suppress = False
        End Try
    End Sub

    ''' <summary>Tick / untick everything that is currently selectable.</summary>
    Public Sub SetAll(state As Boolean)
        _suppress = True
        Try
            For Each r In _rows
                _userWanted(r.Cat) = state
                If r.Applies AndAlso r.Chk.Enabled Then r.Chk.Checked = state
            Next
        Finally
            _suppress = False
        End Try
        RaiseEvent OptionsChanged(Me, EventArgs.Empty)
    End Sub

    ''' <summary>Current selection. A category that is hidden (other game) or disabled (the preset carries
    ''' nothing) reports False, so the filter preserves the target NPC's value for it.</summary>
    Public ReadOnly Property Options As PresetCategoryOptions
        Get
            Dim o As New PresetCategoryOptions
            For Each r In _rows
                o.SetValue(r.Cat, r.Applies AndAlso r.Chk.Enabled AndAlso r.Chk.Checked)
            Next
            Return o
        End Get
    End Property

End Class
