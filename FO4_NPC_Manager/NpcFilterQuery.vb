Imports System.Linq
Imports System.Text

''' <summary>Facets an advanced filter term can target. Everything except <see cref="Hair"/>,
''' <see cref="Eyes"/> and <see cref="Face"/> resolves from data already held in NPC_Data; those
''' three additionally need the referenced HDPT parsed to know its PartType. See NpcFilterIndex
''' for the cost model.</summary>
Friend Enum NpcFilterFacet
    Hair
    Eyes
    Face
    Race
    Skin
    Outfit
    HairColor
    HeadTex
    Omod
    Tplt
    Plugin
    Edid
    Fid
    Flags
End Enum

''' <summary>One `facet:value` term of an advanced query. Values separated by '|' are OR'd; a term
''' prefixed with '!' is negated; terms are AND'd by the caller.
''' <para>A value is a FORMID pattern when it looks like one by the app-wide convention (0x-prefixed,
''' or exactly 8 hex digits — same rule as Program.TryHexId), and TEXT otherwise. The split matters
''' for cost, not only for semantics: a term with no text values never needs a single record label
''' resolved, so it filters straight off the FormIDs already in RAM.</para></summary>
Friend NotInheritable Class NpcFilterTerm
    Public ReadOnly Facet As NpcFilterFacet
    Public ReadOnly Negate As Boolean
    ''' <summary>The token exactly as it appeared in the query, so a chip can remove itself without
    ''' re-rendering (and thus re-normalizing) the rest of what the user typed.</summary>
    Public ReadOnly Raw As String
    ''' <summary>Lowercased substrings matched against the facet's resolved labels.</summary>
    Public ReadOnly TextValues As String()
    ''' <summary>Uppercase hex fragments matched against the referenced FormID rendered as X8
    ''' (Contains, so a partial id works exactly like it does in the quick filter today).</summary>
    Public ReadOnly IdValues As String()
    ''' <summary>The "none" sentinel: matches NPCs for which the facet resolves to nothing at all
    ''' (no skin, no outfit, bald, no template…).</summary>
    Public ReadOnly MatchNone As Boolean

    Private Sub New(facet As NpcFilterFacet, negate As Boolean, raw As String,
                    textValues As String(), idValues As String(), matchNone As Boolean)
        Me.Facet = facet
        Me.Negate = negate
        Me.Raw = raw
        Me.TextValues = textValues
        Me.IdValues = idValues
        Me.MatchNone = matchNone
    End Sub

    ''' <summary>True when this term can only be decided by resolving the facet's LABELS (EDID /
    ''' FULL / mesh path of the referenced record). False = FormID-only or "none", which the index
    ''' answers without touching PluginManager at all.</summary>
    Public ReadOnly Property NeedsLabels As Boolean
        Get
            Return TextValues.Length > 0
        End Get
    End Property

    Friend Shared Function Create(facet As NpcFilterFacet, value As String, negate As Boolean, raw As String) As NpcFilterTerm
        Dim texts As New List(Of String)
        Dim ids As New List(Of String)
        Dim none As Boolean = False
        For Each part In value.Split("|"c)
            Dim v = part.Trim()
            If v.Length = 0 Then Continue For
            If String.Equals(v, "none", StringComparison.OrdinalIgnoreCase) Then
                none = True
                Continue For
            End If
            Dim hex As String = Nothing
            If TryHexFragment(v, hex) Then
                ids.Add(hex)
            Else
                texts.Add(v.ToLowerInvariant())
            End If
        Next
        Return New NpcFilterTerm(facet, negate, raw, texts.ToArray(), ids.ToArray(), none)
    End Function

    ''' <summary>Mirror of Program.TryHexId: a value counts as a FormID only when it is 0x-prefixed
    ''' or exactly 8 hex digits. Deliberately strict — otherwise an EDID fragment like "cab" would
    ''' silently stop matching text and start matching FormIDs.</summary>
    Private Shared Function TryHexFragment(s As String, ByRef hex As String) As Boolean
        Dim t = s
        Dim prefixed = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        If prefixed Then t = t.Substring(2)
        If Not (prefixed OrElse t.Length = 8) Then Return False
        If t.Length = 0 OrElse t.Length > 8 Then Return False
        For Each c In t
            If Not Uri.IsHexDigit(c) Then Return False
        Next
        hex = t.ToUpperInvariant()
        Return True
    End Function
End Class

''' <summary>Parsed contents of the NPC search box: the free text (what the quick filter has always
''' matched) plus zero or more advanced `facet:value` terms.
'''
''' <para>⛔ NO-OP CONTRACT — the reason this class exists at all. When the text carries NO recognised
''' facet token, <see cref="FreeText"/> is the input string VERBATIM (not trimmed, not rebuilt, not
''' normalized) and <see cref="Terms"/> is empty. The caller then runs exactly the code it ran before
''' this feature existed, so a plain search costs what it always cost and cannot change results. The
''' Tools/NpcFilterGate is the gate on that claim; do not "clean up" the verbatim return.</para>
'''
''' <para>An unknown prefix (`foo:bar`) is NOT a facet — it stays literal, so searching for an EDID
''' that happens to contain a colon keeps working. A fully quoted token is always literal, which is
''' the escape hatch for literally searching "hair:something".</para></summary>
Friend NotInheritable Class NpcFilterQuery
    Private Shared ReadOnly _facetNames As Dictionary(Of String, NpcFilterFacet) = BuildFacetNames()

    ''' <summary>The `templates:` prefix. NOT a facet: it does not select NPCs, it changes how every
    ''' other facet resolves. It still lives in the query string (rather than in a checkbox somewhere)
    ''' so the search box remains the COMPLETE state of the filter — no hidden mode can shrink the
    ''' tree with nothing on screen to explain it.</summary>
    Private Const TemplatesPrefix As String = "templates"

    ''' <summary>The query text as the user typed it.</summary>
    Public ReadOnly RawText As String
    ''' <summary>What the legacy quick filter matches. Identical to <see cref="RawText"/> whenever
    ''' there are no facet terms (see the no-op contract above); otherwise the leftover literal
    ''' tokens joined by single spaces.</summary>
    Public ReadOnly FreeText As String
    Public ReadOnly Terms As NpcFilterTerm()
    ''' <summary>False when the query carries `templates:own`: facets then read the NPC's OWN
    ''' subrecords instead of resolving through the TPLT chain. Default True.</summary>
    Public ReadOnly FollowTemplates As Boolean

    Private ReadOnly _literalRaws As String()

    Private Sub New(rawText As String, freeText As String, terms As NpcFilterTerm(),
                    literalRaws As String(), followTemplates As Boolean)
        Me.RawText = rawText
        Me.FreeText = freeText
        Me.Terms = terms
        Me.FollowTemplates = followTemplates
        _literalRaws = literalRaws
    End Sub

    ''' <summary>True when the query carries anything the advanced editor owns — a facet term or the
    ''' `templates:` mode. That is exactly what "Clear advanced" removes.</summary>
    Public ReadOnly Property HasFacets As Boolean
        Get
            Return Terms.Length > 0 OrElse Not FollowTemplates
        End Get
    End Property

    ''' <summary>True when at least one term can only be answered with resolved labels. Used to keep
    ''' "this query needs records read" observable in one place.</summary>
    Public ReadOnly Property NeedsLabels As Boolean
        Get
            Return Terms.Any(Function(t) t.NeedsLabels)
        End Get
    End Property

    Public Shared Function Parse(text As String) As NpcFilterQuery
        Dim raw = If(text, "")
        Dim tokens = Tokenize(raw)
        Dim terms As New List(Of NpcFilterTerm)
        Dim literals As New List(Of String)
        Dim followTemplates As Boolean = True
        Dim sawMode As Boolean = False
        For Each tok In tokens
            Dim term As NpcFilterTerm = Nothing
            Dim follow As Boolean
            If TryParseTemplatesMode(tok, follow) Then
                followTemplates = follow
                sawMode = True
            ElseIf TryParseTerm(tok, term) Then
                terms.Add(term)
            Else
                literals.Add(tok)
            End If
        Next

        ' ⛔ Nothing recognised → hand back the ORIGINAL string. Rebuilding it from tokens would
        ' already be a behaviour change (it collapses runs of spaces, which the current filter matches
        ' literally). This is the no-op contract; Tools/NpcFilterGate is the gate on it.
        If terms.Count = 0 AndAlso Not sawMode Then
            Return New NpcFilterQuery(raw, raw, Array.Empty(Of NpcFilterTerm)(), literals.ToArray(), True)
        End If

        Dim free = String.Join(" ", literals.Select(AddressOf StripQuotes))
        Return New NpcFilterQuery(raw, free, terms.ToArray(), literals.ToArray(), followTemplates)
    End Function

    ''' <summary>The query with every facet term and the `templates:` mode dropped — what "Clear
    ''' advanced" writes back into the box. Keeps the free text: clearing the advanced part must never
    ''' wipe what the user typed.</summary>
    Public Function WithoutFacets() As String
        Return String.Join(" ", _literalRaws)
    End Function

    ''' <summary>Rebuild a query string from the free text plus one token per non-empty facet value.
    ''' This is how the Advanced dialog writes into the box — the box stays the single source of truth,
    ''' the dialog is only an editor for it, so typing `hair:braid` by hand and picking it in the
    ''' dialog converge on the same string.</summary>
    Public Shared Function Compose(freeText As String,
                                   facetValues As IEnumerable(Of KeyValuePair(Of NpcFilterFacet, String)),
                                   Optional followTemplates As Boolean = True) As String
        Dim parts As New List(Of String)
        Dim free = If(freeText, "").Trim()
        If free.Length > 0 Then parts.Add(free)
        For Each kv In facetValues
            Dim v = If(kv.Value, "").Trim()
            If v.Length = 0 Then Continue For
            parts.Add(BuildToken(kv.Key, v))
        Next
        ' Only the NON-default is emitted: a query that follows templates carries no token at all.
        If Not followTemplates Then parts.Add($"{TemplatesPrefix}:own")
        Return String.Join(" ", parts)
    End Function

    Public Shared Function BuildToken(facet As NpcFilterFacet, value As String) As String
        Dim v = If(value, "").Trim()
        If v.IndexOf(" "c) >= 0 Then v = """" & v & """"
        Return $"{FacetDisplayName(facet)}:{v}"
    End Function

    Public Shared Function FacetDisplayName(facet As NpcFilterFacet) As String
        Select Case facet
            Case NpcFilterFacet.Hair : Return "hair"
            Case NpcFilterFacet.Eyes : Return "eyes"
            Case NpcFilterFacet.Face : Return "face"
            Case NpcFilterFacet.Race : Return "race"
            Case NpcFilterFacet.Skin : Return "skin"
            Case NpcFilterFacet.Outfit : Return "outfit"
            Case NpcFilterFacet.HairColor : Return "haircolor"
            Case NpcFilterFacet.HeadTex : Return "headtex"
            Case NpcFilterFacet.Omod : Return "omod"
            Case NpcFilterFacet.Tplt : Return "tplt"
            Case NpcFilterFacet.Plugin : Return "plugin"
            Case NpcFilterFacet.Edid : Return "edid"
            Case NpcFilterFacet.Fid : Return "fid"
            Case NpcFilterFacet.Flags : Return "flags"
        End Select
        Return facet.ToString().ToLowerInvariant()
    End Function

    Private Shared Function BuildFacetNames() As Dictionary(Of String, NpcFilterFacet)
        Dim d As New Dictionary(Of String, NpcFilterFacet)(StringComparer.OrdinalIgnoreCase) From {
            {"hair", NpcFilterFacet.Hair},
            {"eyes", NpcFilterFacet.Eyes},
            {"eye", NpcFilterFacet.Eyes},
            {"face", NpcFilterFacet.Face},
            {"race", NpcFilterFacet.Race},
            {"skin", NpcFilterFacet.Skin},
            {"outfit", NpcFilterFacet.Outfit},
            {"haircolor", NpcFilterFacet.HairColor},
            {"headtex", NpcFilterFacet.HeadTex},
            {"omod", NpcFilterFacet.Omod},
            {"tplt", NpcFilterFacet.Tplt},
            {"template", NpcFilterFacet.Tplt},
            {"plugin", NpcFilterFacet.Plugin},
            {"edid", NpcFilterFacet.Edid},
            {"fid", NpcFilterFacet.Fid},
            {"formid", NpcFilterFacet.Fid},
            {"flags", NpcFilterFacet.Flags},
            {"flag", NpcFilterFacet.Flags}
        }
        Return d
    End Function

    ''' <summary>Split on whitespace, honouring double quotes so `hair:"long braid"` is one token.
    ''' Quotes are kept in the token; they are stripped where the value is consumed.</summary>
    Private Shared Function Tokenize(text As String) As List(Of String)
        Dim tokens As New List(Of String)
        If String.IsNullOrEmpty(text) Then Return tokens
        Dim sb As New StringBuilder()
        Dim inQuotes As Boolean = False
        Dim started As Boolean = False
        For Each ch In text
            If ch = """"c Then
                inQuotes = Not inQuotes
                started = True
                sb.Append(ch)
            ElseIf Not inQuotes AndAlso Char.IsWhiteSpace(ch) Then
                If started Then
                    tokens.Add(sb.ToString())
                    sb.Clear()
                    started = False
                End If
            Else
                sb.Append(ch)
                started = True
            End If
        Next
        If started Then tokens.Add(sb.ToString())
        Return tokens
    End Function

    ''' <summary>`templates:own` (also literal/no/false) turns template following OFF; `templates:follow`
    ''' turns it back on. Any other value is not the mode, so it falls through to the literal path
    ''' instead of silently swallowing the token.</summary>
    Private Shared Function TryParseTemplatesMode(token As String, ByRef followTemplates As Boolean) As Boolean
        If token.StartsWith("""", StringComparison.Ordinal) Then Return False
        Dim colon = token.IndexOf(":"c)
        If colon <= 0 Then Return False
        If Not String.Equals(token.Substring(0, colon), TemplatesPrefix, StringComparison.OrdinalIgnoreCase) Then Return False
        Select Case StripQuotes(token.Substring(colon + 1)).Trim().ToLowerInvariant()
            Case "own", "literal", "no", "false"
                followTemplates = False
                Return True
            Case "follow", "yes", "true"
                followTemplates = True
                Return True
        End Select
        Return False
    End Function

    Private Shared Function TryParseTerm(token As String, ByRef term As NpcFilterTerm) As Boolean
        Dim t = token
        Dim negate As Boolean = False
        If t.StartsWith("!", StringComparison.Ordinal) Then
            negate = True
            t = t.Substring(1)
        End If
        ' A token that opens with a quote is literal by construction — the escape hatch for searching
        ' text that contains a colon.
        If t.StartsWith("""", StringComparison.Ordinal) Then Return False
        Dim colon = t.IndexOf(":"c)
        If colon <= 0 Then Return False
        Dim facet As NpcFilterFacet
        If Not _facetNames.TryGetValue(t.Substring(0, colon), facet) Then Return False
        Dim value = StripQuotes(t.Substring(colon + 1))
        ' `hair:` with nothing after it is not a filter — treat the token as literal so the tree does
        ' not empty out while the user is still typing the value.
        If value.Trim().Length = 0 Then Return False
        term = NpcFilterTerm.Create(facet, value, negate, token)
        Return True
    End Function

    Private Shared Function StripQuotes(s As String) As String
        Dim t = If(s, "")
        If t.Length >= 2 AndAlso t.StartsWith("""", StringComparison.Ordinal) AndAlso t.EndsWith("""", StringComparison.Ordinal) Then
            Return t.Substring(1, t.Length - 2)
        End If
        Return t.Replace("""", "")
    End Function
End Class
