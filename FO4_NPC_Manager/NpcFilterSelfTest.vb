Imports System.Linq

''' <summary>Headless gate for the search-box query parser (`--filter-selftest`).
'''
''' <para>WHY A SELF-TEST AND NOT A CORPUS RUN: the claim that matters here is not "the advanced
''' filter finds the right NPCs" — it is "a query WITHOUT facets behaves exactly as it did before the
''' feature existed". No load order can exhibit that: the observable is the identity between two code
''' paths over arbitrary user text, which is a property of the parser, not of the data. So the gate is
''' an exhaustive check on the parser itself, and it is the thing that has to stay green when anyone
''' "tidies up" NpcFilterQuery.Parse.</para>
'''
''' <para>Exit code 0 = every check passed, 4 = at least one divergence (same convention as
''' --skinregion-diag).</para></summary>
Friend Module NpcFilterSelfTest

    Private _failures As Integer = 0
    Private _checks As Integer = 0

    Public Function Run() As Integer
        _failures = 0
        _checks = 0

        Console.WriteLine("NPC filter — query parser self-test")
        Console.WriteLine("===================================")
        Console.WriteLine("")

        CheckNoOpContract()
        CheckLiteralEscapes()
        CheckFacetParsing()
        CheckIdVsText()
        CheckTemplatesMode()
        CheckRewrites()
        CheckDialogLayout()

        Console.WriteLine("")
        Console.WriteLine($"checks = {_checks} · failures = {_failures}")
        If _failures = 0 Then
            Console.WriteLine("OK — a query with no facet token is handed through VERBATIM, so the plain")
            Console.WriteLine("     search box runs the exact code (and cost) it ran before this feature.")
            Return 0
        End If
        Console.WriteLine("DIVERGENCE — see the FAIL lines above.")
        Return 4
    End Function

    ''' <summary>⛔ THE contract: no facet token ⇒ FreeText is the input string, character for
    ''' character, and there are no terms. Anything that trims, collapses spaces or rebuilds the
    ''' string from tokens breaks the quick filter's current results (it matches the raw substring,
    ''' spaces included) and shows up here.</summary>
    Private Sub CheckNoOpContract()
        Console.WriteLine("-- no-op contract (no facet token → verbatim) --")
        Dim plain = {
            "", " ", "cait", "  cait  ", "cait raider", "Cait", "0007A1B2", "piper wright",
            "a  b", "Mr. Handy", "hair", ":", "::", "a:", ":b", "!", "!cait", """", """cait""",
            "señora", "コンパニオン", "npc_-3", "http://x", "C:\Data\Meshes", "50%", "a|b"
        }
        For Each q In plain
            Dim parsed = NpcFilterQuery.Parse(q)
            Expect(parsed.Terms.Length = 0, $"no terms for {Quote(q)}")
            Expect(String.Equals(parsed.FreeText, q, StringComparison.Ordinal),
                   $"verbatim FreeText for {Quote(q)} (got {Quote(parsed.FreeText)})")
            Expect(Not parsed.HasFacets, $"HasFacets=False for {Quote(q)}")
        Next
        ' Nothing must behave like "" and never throw.
        Dim nul = NpcFilterQuery.Parse(Nothing)
        Expect(nul.FreeText = "" AndAlso nul.Terms.Length = 0, "Parse(Nothing) → empty query")
    End Sub

    ''' <summary>An unknown prefix is NOT a facet, and a quoted token is never one. Both are the
    ''' escape hatches that keep colon-bearing text searchable.</summary>
    Private Sub CheckLiteralEscapes()
        Console.WriteLine("-- literal escapes --")
        For Each q In {"foo:bar", "note:something", "C:\path", "12:30"}
            Dim parsed = NpcFilterQuery.Parse(q)
            Expect(parsed.Terms.Length = 0, $"unknown prefix stays literal: {Quote(q)}")
            Expect(String.Equals(parsed.FreeText, q, StringComparison.Ordinal), $"verbatim for {Quote(q)}")
        Next
        Dim quoted = NpcFilterQuery.Parse("""hair:braid""")
        Expect(quoted.Terms.Length = 0, "quoted token is literal")
        Expect(quoted.FreeText = """hair:braid""", "quoted token kept verbatim (no facets ⇒ verbatim)")
        ' A facet with an empty value is not a filter — otherwise the tree empties out while the user
        ' is still typing the value.
        Dim typing = NpcFilterQuery.Parse("hair:")
        Expect(typing.Terms.Length = 0, "`hair:` with no value is not a term")
    End Sub

    Private Sub CheckFacetParsing()
        Console.WriteLine("-- facet parsing --")
        Dim q = NpcFilterQuery.Parse("cait hair:braid !plugin:Fallout4.esm skin:none eyes:blue|green")
        Expect(q.Terms.Length = 4, $"4 terms (got {q.Terms.Length})")
        Expect(q.FreeText = "cait", $"free text is 'cait' (got {Quote(q.FreeText)})")

        Dim hair = q.Terms.FirstOrDefault(Function(t) t.Facet = NpcFilterFacet.Hair)
        Expect(hair IsNot Nothing AndAlso hair.TextValues.SequenceEqual({"braid"}), "hair:braid → one text value")
        Expect(hair IsNot Nothing AndAlso hair.NeedsLabels, "a text value needs labels resolved")

        Dim plug = q.Terms.FirstOrDefault(Function(t) t.Facet = NpcFilterFacet.Plugin)
        Expect(plug IsNot Nothing AndAlso plug.Negate, "!plugin → negated")
        Expect(plug IsNot Nothing AndAlso plug.TextValues.SequenceEqual({"fallout4.esm"}), "values are lowercased")

        Dim skin = q.Terms.FirstOrDefault(Function(t) t.Facet = NpcFilterFacet.Skin)
        Expect(skin IsNot Nothing AndAlso skin.MatchNone, "skin:none → MatchNone")
        Expect(skin IsNot Nothing AndAlso Not skin.NeedsLabels, "`none` resolves without labels")

        Dim eyes = q.Terms.FirstOrDefault(Function(t) t.Facet = NpcFilterFacet.Eyes)
        Expect(eyes IsNot Nothing AndAlso eyes.TextValues.SequenceEqual({"blue", "green"}), "'|' is OR")

        ' Aliases and quoted values.
        Expect(NpcFilterQuery.Parse("template:x").Terms(0).Facet = NpcFilterFacet.Tplt, "template: aliases tplt:")
        Expect(NpcFilterQuery.Parse("formid:0x1").Terms(0).Facet = NpcFilterFacet.Fid, "formid: aliases fid:")
        Expect(NpcFilterQuery.Parse("HAIR:X").Terms.Length = 1, "prefix is case-insensitive")
        Dim spaced = NpcFilterQuery.Parse("hair:""long braid""")
        Expect(spaced.Terms.Length = 1 AndAlso spaced.Terms(0).TextValues.SequenceEqual({"long braid"}),
               "quoted value keeps its space")
    End Sub

    ''' <summary>Text vs FormID split. It decides COST, not only semantics: a term with no text value
    ''' never resolves a single record label. The rule mirrors Program.TryHexId on purpose — 0x-prefixed
    ''' or exactly 8 hex digits — so a 3-letter EDID fragment can never be mistaken for an id.</summary>
    Private Sub CheckIdVsText()
        Console.WriteLine("-- id vs text --")
        Dim byId = NpcFilterQuery.Parse("hair:0x1A2B hair:0007A1B2").Terms
        Expect(byId.All(Function(t) Not t.NeedsLabels), "FormID values need NO labels (the free path)")
        Expect(byId(0).IdValues.SequenceEqual({"1A2B"}), "0x prefix stripped, uppercased")
        Expect(byId(1).IdValues.SequenceEqual({"0007A1B2"}), "bare 8 hex digits count as an id")

        Dim asText = NpcFilterQuery.Parse("hair:cab hair:1a2b hair:abcdefghi").Terms
        Expect(asText.All(Function(t) t.NeedsLabels), "short/over-long hex-looking values stay TEXT")
        Expect(asText(0).IdValues.Length = 0, "'cab' is not a FormID")
        Expect(asText(1).IdValues.Length = 0, "'1a2b' without 0x is not a FormID")

        Dim mixed = NpcFilterQuery.Parse("hair:braid|0x1A").Terms(0)
        Expect(mixed.TextValues.SequenceEqual({"braid"}) AndAlso mixed.IdValues.SequenceEqual({"1A"}),
               "a term can carry both kinds at once")
    End Sub

    ''' <summary>The `templates:` mode. It is NOT a facet — it selects nothing, it changes how the
    ''' facets resolve — but it travels in the query string anyway so the search box remains the whole
    ''' visible state of the filter. It must never reach the term list, or it would filter everyone
    ''' out.</summary>
    Private Sub CheckTemplatesMode()
        Console.WriteLine("-- templates: mode --")
        Expect(NpcFilterQuery.Parse("cait").FollowTemplates, "default is FOLLOW")
        Dim own = NpcFilterQuery.Parse("hair:braid templates:own")
        Expect(Not own.FollowTemplates, "templates:own turns following off")
        Expect(own.Terms.Length = 1, $"the mode is NOT a term (got {own.Terms.Length} terms)")
        Expect(own.HasFacets, "the mode counts as 'something advanced is on'")
        Expect(NpcFilterQuery.Parse("templates:follow").FollowTemplates, "templates:follow turns it back on")
        Expect(NpcFilterQuery.Parse("templates:banana").Terms.Length = 0 AndAlso
               NpcFilterQuery.Parse("templates:banana").FreeText = "templates:banana",
               "an unknown value falls through to literal instead of being swallowed")
        ' `templates:own` alone still counts as recognised, so FreeText drops it.
        Expect(NpcFilterQuery.Parse("cait templates:own").FreeText = "cait", "the mode token leaves the free text")
    End Sub

    Private Sub CheckRewrites()
        Console.WriteLine("-- rewrites (Clear advanced / dialog compose) --")
        Dim q = NpcFilterQuery.Parse("cait hair:braid race:ghoul")
        Expect(q.WithoutFacets() = "cait", $"Clear advanced keeps the free text (got {Quote(q.WithoutFacets())})")
        Expect(NpcFilterQuery.Parse("cait templates:own").WithoutFacets() = "cait",
               "Clear advanced also drops the templates: mode")
        Expect(NpcFilterQuery.Parse("cait").WithoutFacets() = "cait", "Clear advanced on a plain query is a no-op")
        ' Negated terms survive Clear? No — they ARE advanced criteria, so they go too.
        Expect(NpcFilterQuery.Parse("cait !plugin:x").WithoutFacets() = "cait", "Clear advanced drops negated terms too")

        Dim composed = NpcFilterQuery.Compose("cait", {
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Hair, "braid"),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Eyes, ""),
            New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Race, "ghoul")})
        Expect(composed = "cait hair:braid race:ghoul", $"dialog → box (got {Quote(composed)})")

        Dim withMode = NpcFilterQuery.Compose("", {New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Hair, "braid")},
                                              followTemplates:=False)
        Expect(withMode = "hair:braid templates:own", $"the non-default mode is emitted (got {Quote(withMode)})")
        Expect(NpcFilterQuery.Compose("", {New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Hair, "b")}) = "hair:b",
               "the DEFAULT mode emits no token")

        ' Round trip: what the dialog writes has to parse back into the same thing, or opening and
        ' OK-ing it twice would keep rewriting the user's query.
        Dim reparsed = NpcFilterQuery.Parse(composed)
        Expect(reparsed.Terms.Length = 2 AndAlso reparsed.FreeText = "cait", "box → dialog → box is stable")
        Dim spacey = NpcFilterQuery.Compose("", {New KeyValuePair(Of NpcFilterFacet, String)(NpcFilterFacet.Hair, "long braid")})
        Expect(NpcFilterQuery.Parse(spacey).Terms(0).TextValues.SequenceEqual({"long braid"}),
               "a value with a space survives the round trip (it gets quoted)")
        Dim modeRoundTrip = NpcFilterQuery.Parse(withMode)
        Expect(Not modeRoundTrip.FollowTemplates AndAlso modeRoundTrip.Terms.Length = 1, "the mode survives the round trip")
    End Sub

    ''' <summary>The Advanced dialog has to FIT. A hardcoded ClientSize cannot be right for every
    ''' font/DPI/language, and when it is short it is the bottom row — the buttons — that gets sliced.
    ''' This runs the real Designer layout and the real Load sequence headless and checks that the
    ''' lowest control ends inside the client area, for queries of different shapes (an empty one, and
    ''' one that fills every field so the wrapping rows are at their tallest).</summary>
    Private Sub CheckDialogLayout()
        Console.WriteLine("-- Advanced dialog layout --")
        ' Mirror what Program.Main sets before any form exists: visual styles change button metrics,
        ' so measuring without them would measure a dialog nobody ever sees.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Dim cases = {
            ("empty", ""),
            ("typical", "cait hair:braid"),
            ("everything", "cait hair:braid|pony eyes:blue face:scar headtex:x skin:none outfit:x race:ghoul " &
                           "haircolor:black tplt:x omod:x flags:female|male|chargenpreset|robot|inherited templates:own")
        }
        For Each c In cases
            Using f As New NpcFilterAdvanced_Form()
                Dim r = f.ProbeLayout(c.Item2)
                Console.WriteLine($"  {c.Item1,-11} client={r.ClientHeight,4}  needed={r.Needed,4}  lowest={r.LowestBottom,4} ({r.LowestName})")
                Expect(r.LowestBottom <= r.ClientHeight,
                       $"[{c.Item1}] nothing is clipped: lowest control ends at {r.LowestBottom}, client is {r.ClientHeight}")
                Expect(r.Needed <= r.ClientHeight,
                       $"[{c.Item1}] the form grew to what the layout needs ({r.Needed} vs {r.ClientHeight})")
            End Using
        Next
    End Sub

    Private Sub Expect(condition As Boolean, what As String)
        _checks += 1
        If condition Then Return
        _failures += 1
        Console.WriteLine($"  FAIL  {what}")
    End Sub

    Private Function Quote(s As String) As String
        If s Is Nothing Then Return "<Nothing>"
        Return "«" & s & "»"
    End Function

End Module
