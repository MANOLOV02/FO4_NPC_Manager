Imports System.Linq
Imports System.Windows.Forms

''' <summary>Modal editor for the advanced part of the NPC search box.
'''
''' <para>It holds NO filter state of its own. It is opened with the current box text, parses it, and
''' hands back a rebuilt string — so typing `hair:braid` by hand and picking it here converge on the
''' same query, and closing the dialog can never leave a criterion active that the box does not show.
''' Everything, including the "follow templates" mode, round-trips through that one string.</para>
'''
''' <para>Cost: opening this dialog reads NOTHING from the load order. It is pure text editing; the
''' facets only resolve records later, lazily, while the tree is being filtered (NpcFilterIndex).</para></summary>
Public Class NpcFilterAdvanced_Form

    ''' <summary>In: the search box text as it stands. Out (on DialogResult.OK): the rebuilt query.</summary>
    Public Property QueryText As String = ""

    Private _loading As Boolean = False

    Private Sub NpcFilterAdvanced_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        LoadFromQuery(NpcFilterQuery.Parse(QueryText))
        UpdatePreview()
        FitToContent()
    End Sub

    ''' <summary>Size the dialog to what the layout actually needs instead of trusting a hardcoded
    ''' ClientSize. The Designer number cannot be right for every font/DPI/language — and when it is
    ''' short the bottom row is what gets sliced, which is exactly the button row. Measured AFTER the
    ''' fields are filled because the flags row and the hint wrap, so the height is not known until
    ''' then.</summary>
    Private Sub FitToContent()
        PanelRoot.PerformLayout()
        ' ⛔ PreferredSize is NOT enough on its own: the laid-out children can end BELOW it (a Dock=Fill
        ' child in an AutoSize row measures short — measured 388 vs a preferred 383, i.e. 5 px of button
        ' hanging outside). So take the deepest actual bottom as well and size to whichever is larger.
        Dim lowest As Integer = 0
        For Each c As Control In PanelRoot.Controls
            lowest = Math.Max(lowest, c.Bottom + c.Margin.Bottom)
        Next
        Dim needed = Math.Max(PanelRoot.PreferredSize.Height, lowest + PanelRoot.Padding.Bottom)
        If needed <= 0 Then Return

        Dim chrome = Height - ClientSize.Height
        ' Grow AND shrink: the Designer height is only a starting guess, and leaving it larger than the
        ' content would just park an empty band under the buttons.
        ClientSize = New Size(ClientSize.Width, needed)
        ' Keep the user from dragging the dialog shorter than its content — same clipping, by hand.
        MinimumSize = New Size(MinimumSize.Width, chrome + needed)
    End Sub

    ''' <summary>Headless layout probe for `--filter-selftest`. Runs the REAL Load sequence (fill the
    ''' fields, then FitToContent) on the REAL Designer layout and reports where the lowest control
    ''' ends up, so "the buttons are sliced in half" becomes something a build can catch instead of
    ''' something the user has to notice. Never shows the window.</summary>
    Friend Function ProbeLayout(query As String) As (ClientHeight As Integer, Needed As Integer, LowestBottom As Integer, LowestName As String)
        QueryText = If(query, "")
        CreateControl()
        LoadFromQuery(NpcFilterQuery.Parse(QueryText))
        UpdatePreview()
        FitToContent()
        PanelRoot.PerformLayout()
        PerformLayout()

        Dim lowest As Integer = 0
        Dim lowestName As String = "(none)"
        For Each c As Control In PanelRoot.Controls
            Dim bottom = PanelRoot.Top + c.Bottom + c.Margin.Bottom
            If bottom > lowest Then
                lowest = bottom
                lowestName = c.Name
            End If
        Next
        Return (ClientSize.Height, PanelRoot.PreferredSize.Height, lowest, lowestName)
    End Function

    Private Sub LoadFromQuery(query As NpcFilterQuery)
        _loading = True
        Try
            TextBoxFreeText.Text = query.FreeText
            TextBoxFacetHair.Text = FacetValueText(query, NpcFilterFacet.Hair)
            TextBoxFacetEyes.Text = FacetValueText(query, NpcFilterFacet.Eyes)
            TextBoxFacetFace.Text = FacetValueText(query, NpcFilterFacet.Face)
            TextBoxFacetHeadTex.Text = FacetValueText(query, NpcFilterFacet.HeadTex)
            TextBoxFacetSkin.Text = FacetValueText(query, NpcFilterFacet.Skin)
            TextBoxFacetOutfit.Text = FacetValueText(query, NpcFilterFacet.Outfit)
            TextBoxFacetRace.Text = FacetValueText(query, NpcFilterFacet.Race)
            TextBoxFacetHairColor.Text = FacetValueText(query, NpcFilterFacet.HairColor)
            TextBoxFacetTplt.Text = FacetValueText(query, NpcFilterFacet.Tplt)
            TextBoxFacetOmod.Text = FacetValueText(query, NpcFilterFacet.Omod)

            Dim flags = FacetValueText(query, NpcFilterFacet.Flags).
                        Split("|"c).Select(Function(s) s.Trim().ToLowerInvariant()).ToList()
            CheckBoxFlagFemale.Checked = flags.Contains("female")
            CheckBoxFlagMale.Checked = flags.Contains("male")
            CheckBoxFlagPreset.Checked = flags.Contains("chargenpreset") OrElse flags.Contains("preset")
            CheckBoxFlagRobot.Checked = flags.Contains("robot")
            CheckBoxFlagInherited.Checked = flags.Contains("inherited")

            CheckBoxFollowTemplates.Checked = query.FollowTemplates
        Finally
            _loading = False
        End Try
    End Sub

    ''' <summary>Every value the query carries for one facet, rendered the way the dialog shows it.
    ''' <para>NEGATED terms are deliberately left out: there is no '!' control here, so round-tripping
    ''' them through the dialog would quietly drop the negation from the user's query. They stay in the
    ''' box untouched — see ComposeQuery, which preserves them.</para></summary>
    Private Shared Function FacetValueText(query As NpcFilterQuery, facet As NpcFilterFacet) As String
        Dim parts As New List(Of String)
        For Each t In query.Terms
            If t.Facet <> facet OrElse t.Negate Then Continue For
            parts.AddRange(t.TextValues)
            parts.AddRange(t.IdValues.Select(Function(v) "0x" & v))
            If t.MatchNone Then parts.Add("none")
        Next
        Return String.Join("|", parts)
    End Function

    ''' <summary>Rebuild the whole query from the controls. Negated terms of the ORIGINAL query are
    ''' re-appended verbatim so opening and OK-ing the dialog never destroys a `!plugin:…` the user
    ''' typed by hand.</summary>
    Private Function ComposeQuery() As String
        Dim values As New List(Of KeyValuePair(Of NpcFilterFacet, String)) From {
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Hair, TextBoxFacetHair.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Eyes, TextBoxFacetEyes.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Face, TextBoxFacetFace.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.HeadTex, TextBoxFacetHeadTex.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Skin, TextBoxFacetSkin.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Outfit, TextBoxFacetOutfit.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Race, TextBoxFacetRace.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.HairColor, TextBoxFacetHairColor.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Tplt, TextBoxFacetTplt.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Omod, TextBoxFacetOmod.Text),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Flags, ComposeFlagValue())
        }

        Dim negated = NpcFilterQuery.Parse(QueryText).Terms.Where(Function(t) t.Negate).Select(Function(t) t.Raw).ToArray()
        Dim free = TextBoxFreeText.Text.Trim()
        If negated.Length > 0 Then free = (free & " " & String.Join(" ", negated)).Trim()

        Return NpcFilterQuery.Compose(free, values, CheckBoxFollowTemplates.Checked)
    End Function

    ''' <summary>The flag checkboxes collapse into ONE `flags:a|b` token — they are alternatives (OR),
    ''' not independent filters: Female + Robot means "female OR robot".</summary>
    Private Function ComposeFlagValue() As String
        Dim flags As New List(Of String)
        If CheckBoxFlagFemale.Checked Then flags.Add("female")
        If CheckBoxFlagMale.Checked Then flags.Add("male")
        If CheckBoxFlagPreset.Checked Then flags.Add("chargenpreset")
        If CheckBoxFlagRobot.Checked Then flags.Add("robot")
        If CheckBoxFlagInherited.Checked Then flags.Add("inherited")
        Return String.Join("|", flags)
    End Function

    ''' <summary>Live echo of the query the dialog will produce. It is also the only place the token
    ''' syntax is ever taught: the user sees `hair:braid` appear as they type in the Hair box.</summary>
    Private Sub UpdatePreview()
        If _loading Then Return
        Dim q = ComposeQuery()
        LabelPreviewValue.Text = If(q.Length = 0, "(no filter — shows every NPC)", q)
    End Sub

    Private Sub AnyField_Changed(sender As Object, e As EventArgs) _
            Handles TextBoxFreeText.TextChanged, TextBoxFacetHair.TextChanged, TextBoxFacetEyes.TextChanged,
                    TextBoxFacetFace.TextChanged, TextBoxFacetHeadTex.TextChanged, TextBoxFacetSkin.TextChanged,
                    TextBoxFacetOutfit.TextChanged, TextBoxFacetRace.TextChanged, TextBoxFacetHairColor.TextChanged,
                    TextBoxFacetTplt.TextChanged, TextBoxFacetOmod.TextChanged,
                    CheckBoxFlagFemale.CheckedChanged, CheckBoxFlagMale.CheckedChanged,
                    CheckBoxFlagPreset.CheckedChanged, CheckBoxFlagRobot.CheckedChanged,
                    CheckBoxFlagInherited.CheckedChanged, CheckBoxFollowTemplates.CheckedChanged
        UpdatePreview()
    End Sub

    Private Sub ButtonResetFields_Click(sender As Object, e As EventArgs) Handles ButtonResetFields.Click
        ' Empties the advanced criteria but keeps the free text — same rule as "Clear advanced" in the
        ' main window, so the two never disagree about what "clear" means.
        LoadFromQuery(NpcFilterQuery.Parse(TextBoxFreeText.Text))
        UpdatePreview()
    End Sub

    Private Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        QueryText = ComposeQuery()
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub ButtonCancel_Click(sender As Object, e As EventArgs) Handles ButtonCancelDialog.Click
        DialogResult = DialogResult.Cancel
        Close()
    End Sub
End Class
