Imports System.Linq
Imports System.Text
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Resolves advanced filter facets (<see cref="NpcFilterFacet"/>) for an NPC.
'''
''' <para>COST MODEL — the whole point of this class. There is NO bulk pass and no warm-up: every
''' cache below fills on first touch and only for what a query actually asked about.
''' <list type="bullet">
''' <item>A query with no facet terms never constructs this at all (PopulateNPCTree short-circuits).</item>
''' <item>`race:`/`skin:`/`outfit:`/… with a FORMID value resolves straight off NPC_Data — zero
''' PluginManager reads, zero parses.</item>
''' <item>The same facets with a TEXT value resolve one label per REFERENCED RECORD (memoized in
''' <see cref="_recordLabels"/>), never one per NPC: thousands of NPCs sharing a hair pay for that
''' HDPT once. PluginRecord.EditorID walks the subrecord list on every call, which is why it is
''' called once per record here and never inside the per-NPC path.</item>
''' <item>`hair:`/`eyes:`/`face:` additionally need HDPT.PartType to tell the three apart, so the
''' referenced HDPTs get parsed — again once each, globally, lazily (<see cref="_headParts"/>).
''' No GetRecordsOfType("HDPT") sweep: only the parts the evaluated NPCs actually reference.</item>
''' <item>Per-NPC facet results are memoized too, so holding a key down costs one dictionary hit per
''' NPC per keystroke after the first.</item>
''' </list>
''' Nothing here touches disk, BA2/loose resolution, meshes, morphs or tints — those are excluded
''' from the filter by design; the filter must never be able to stall a keystroke on I/O.</para>
'''
''' <para>THREADING: UI thread only (PopulateNPCTree marshals itself with Invoke). Plain
''' Dictionary on purpose — do not call from the render/bake threads.</para>
'''
''' <para>Template inheritance is resolved with the SAME bucket rule the render path uses
''' (NpcStateResolver: Traits carries race/skin/headparts/headtex/haircolor/OBTS, Inventory carries
''' DOFT/SOFT) but WITHOUT the race fallbacks, LooksMenu overlays or LVLN picks that the render
''' applies afterwards. So a bald NPC whose hair comes from RACE defaults is NOT matched by `hair:`.
''' That is deliberate: the filter answers "what does this record say", not "what would render".</para></summary>
Friend NotInheritable Class NpcFilterIndex
    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _npcLookup As Func(Of UInteger, NPC_Data)

    ' Record-level caches: keyed by the REFERENCED record's FormID, shared by every NPC that points
    ' at it. Survive an NPC edit (an NPC_ save cannot change an HDPT/ARMO/RACE), cleared only on a
    ' load-order change.
    Private ReadOnly _recordLabels As New Dictionary(Of UInteger, String)()
    Private ReadOnly _headParts As New Dictionary(Of UInteger, Canon.IHdpt)()

    ' Per-NPC caches: dropped wholesale whenever any NPC record changes (an edit to a template source
    ' changes the effective values of everything downstream of it, so per-FormID eviction would be
    ' wrong).
    Private ReadOnly _traitsSource As New Dictionary(Of UInteger, UInteger)()
    Private ReadOnly _inventorySource As New Dictionary(Of UInteger, UInteger)()
    Private ReadOnly _facetIds As New Dictionary(Of NpcFilterFacet, Dictionary(Of UInteger, UInteger()))()
    Private ReadOnly _facetText As New Dictionary(Of NpcFilterFacet, Dictionary(Of UInteger, String))()

    Private _followTemplates As Boolean = True

    Public Sub New(pluginManager As PluginManager, npcLookup As Func(Of UInteger, NPC_Data))
        _pluginManager = pluginManager
        _npcLookup = npcLookup
    End Sub

    ''' <summary>When True (default) a facet resolves through the template chain, so the 40 raiders
    ''' that inherit their look from one TPLT all match `hair:`. When False only the NPC's own
    ''' subrecords count. Flipping it invalidates the per-NPC caches, not the record-level ones.</summary>
    Public Property FollowTemplates As Boolean
        Get
            Return _followTemplates
        End Get
        Set(value As Boolean)
            If _followTemplates = value Then Return
            _followTemplates = value
            InvalidateNpcState()
        End Set
    End Property

    ''' <summary>Load order changed: everything goes, including the record-level labels.</summary>
    Public Sub InvalidateAll()
        _recordLabels.Clear()
        _headParts.Clear()
        InvalidateNpcState()
    End Sub

    ''' <summary>An NPC_ record changed (edit / save / overlay). Drops every per-NPC result: the
    ''' edited NPC may be the template source of any number of others.</summary>
    Public Sub InvalidateNpcState()
        _traitsSource.Clear()
        _inventorySource.Clear()
        _facetIds.Clear()
        _facetText.Clear()
    End Sub

    ''' <summary>True when this NPC satisfies the term (negation included).</summary>
    Public Function Matches(npc As NPC_Data, term As NpcFilterTerm) As Boolean
        If npc Is Nothing OrElse term Is Nothing Then Return False
        Dim hit = MatchesPositive(npc, term)
        Return If(term.Negate, Not hit, hit)
    End Function

    ''' <summary>True when the NPC satisfies EVERY term. Terms are evaluated cheapest-first so a
    ''' FormID-only or flags term can reject before any label is resolved.</summary>
    Public Function MatchesAll(npc As NPC_Data, terms As NpcFilterTerm()) As Boolean
        If terms Is Nothing OrElse terms.Length = 0 Then Return True
        For Each t In terms
            If Not t.NeedsLabels Then
                If Not Matches(npc, t) Then Return False
            End If
        Next
        For Each t In terms
            If t.NeedsLabels Then
                If Not Matches(npc, t) Then Return False
            End If
        Next
        Return True
    End Function

    ' ============================================================================================
    ' Matching
    ' ============================================================================================

    Private Function MatchesPositive(npc As NPC_Data, term As NpcFilterTerm) As Boolean
        Select Case term.Facet
            Case NpcFilterFacet.Flags
                Return MatchesFlags(npc, term)

            Case NpcFilterFacet.Plugin
                Return ContainsAnyText(term, If(npc.PluginName, ""))

            Case NpcFilterFacet.Edid
                Dim edid = If(npc.EditorID, "")
                If term.MatchNone AndAlso edid.Length = 0 Then Return True
                Return ContainsAnyText(term, edid)

            Case NpcFilterFacet.Fid
                ' Both halves of the term match against the same hex text: `fid:0007A1B2` and the
                ' partial `fid:7a1b` have to behave the same way the quick filter already does.
                Dim hex = npc.FormID.ToString("X8")
                If term.IdValues.Any(Function(v) hex.Contains(v, StringComparison.OrdinalIgnoreCase)) Then Return True
                Return ContainsAnyText(term, hex)

            Case Else
                Dim ids = FacetIds(npc, term.Facet)
                If ids.Length = 0 Then Return term.MatchNone
                If term.IdValues.Length > 0 Then
                    For Each id In ids
                        Dim hex = id.ToString("X8")
                        For Each v In term.IdValues
                            If hex.Contains(v, StringComparison.OrdinalIgnoreCase) Then Return True
                        Next
                    Next
                End If
                If term.NeedsLabels Then
                    Dim text = FacetText(npc, term.Facet)
                    For Each v In term.TextValues
                        If text.Contains(v, StringComparison.Ordinal) Then Return True
                    Next
                End If
                Return False
        End Select
    End Function

    ''' <summary>Flags match by whole token, never by substring: "male" must not match "female".</summary>
    Private Function MatchesFlags(npc As NPC_Data, term As NpcFilterTerm) As Boolean
        For Each v In term.TextValues
            Select Case v
                Case "female" : If npc.Record.ConfigurationFlagsFemale Then Return True
                Case "male" : If Not npc.Record.ConfigurationFlagsFemale Then Return True
                Case "chargenpreset", "preset" : If (npc.Record.ConfigurationFlags And &H4UI) <> 0UI Then Return True
                Case "robot" : If npc.Record.TieneCombinaciones() OrElse npc.Record.OmodsDeLaPrimeraCombinacion().Count > 0 Then Return True
                Case "template", "templated" : If npc.Record.ConfigurationTemplateFlags <> 0US Then Return True
                Case "inherited" : If NpcTemplateHelpers.NpcInheritsVisualAppearance(npc) Then Return True
                Case "own" : If Not NpcTemplateHelpers.NpcInheritsVisualAppearance(npc) Then Return True
            End Select
        Next
        Return False
    End Function

    Private Shared Function ContainsAnyText(term As NpcFilterTerm, haystack As String) As Boolean
        For Each v In term.TextValues
            If haystack.Contains(v, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ' ============================================================================================
    ' Facet resolution
    ' ============================================================================================

    Private Function FacetIds(npc As NPC_Data, facet As NpcFilterFacet) As UInteger()
        Dim byNpc As Dictionary(Of UInteger, UInteger()) = Nothing
        If Not _facetIds.TryGetValue(facet, byNpc) Then
            byNpc = New Dictionary(Of UInteger, UInteger())()
            _facetIds(facet) = byNpc
        End If
        Dim cached As UInteger() = Nothing
        If byNpc.TryGetValue(npc.FormID, cached) Then Return cached

        Dim ids = ComputeFacetIds(npc, facet)
        byNpc(npc.FormID) = ids
        Return ids
    End Function

    Private Function ComputeFacetIds(npc As NPC_Data, facet As NpcFilterFacet) As UInteger()
        Select Case facet
            Case NpcFilterFacet.Hair, NpcFilterFacet.Eyes, NpcFilterFacet.Face
                Dim src = TraitsNpc(npc)
                Dim outIds As New List(Of UInteger)
                For Each fid In src.Record.PartesDeCabeza()
                    If fid = 0UI Then Continue For
                    If HeadPartFacet(fid) = facet Then outIds.Add(fid)
                Next
                Return outIds.ToArray()

            Case NpcFilterFacet.Race
                Return NonZero(TraitsNpc(npc).Record.Race)

            Case NpcFilterFacet.Skin
                Return NonZero(TraitsNpc(npc).Record.Skin)

            Case NpcFilterFacet.HeadTex
                Return NonZero(TraitsNpc(npc).Record.HeadTexture)

            Case NpcFilterFacet.HairColor
                Dim src = TraitsNpc(npc)
                Return NonZero(src.Record.HairColor, src.Record.ColorDeBarba())

            Case NpcFilterFacet.Omod
                Dim src = TraitsNpc(npc)
                Return src.Record.OmodsDeLaPrimeraCombinacion().Where(Function(f) f <> 0UI).Distinct().ToArray()

            Case NpcFilterFacet.Outfit
                Dim src = InventoryNpc(npc)
                Return NonZero(src.Record.DefaultOutfit, src.Record.SleepingOutfit)

            Case NpcFilterFacet.Tplt
                ' The template link itself is never resolved THROUGH the chain — it IS the chain.
                Dim outIds As New List(Of UInteger)
                If npc.Record.Plantilla() <> 0UI Then outIds.Add(npc.Record.Plantilla())
                For Each cat As NPC_TemplateCategory In [Enum].GetValues(GetType(NPC_TemplateCategory))
                    Dim actor = npc.Record.ActorDePlantilla(cat)
                    If actor <> 0UI AndAlso Not outIds.Contains(actor) Then outIds.Add(actor)
                Next
                Return outIds.ToArray()
        End Select
        Return Array.Empty(Of UInteger)()
    End Function

    Private Function FacetText(npc As NPC_Data, facet As NpcFilterFacet) As String
        Dim byNpc As Dictionary(Of UInteger, String) = Nothing
        If Not _facetText.TryGetValue(facet, byNpc) Then
            byNpc = New Dictionary(Of UInteger, String)()
            _facetText(facet) = byNpc
        End If
        Dim cached As String = Nothing
        If byNpc.TryGetValue(npc.FormID, cached) Then Return cached

        Dim ids = FacetIds(npc, facet)
        Dim text As String
        If ids.Length = 0 Then
            text = ""
        ElseIf ids.Length = 1 Then
            text = RecordLabel(ids(0)).ToLowerInvariant()
        Else
            Dim sb As New StringBuilder()
            For Each id In ids
                If sb.Length > 0 Then sb.Append(" "c)
                sb.Append(RecordLabel(id))
            Next
            text = sb.ToString().ToLowerInvariant()
        End If
        byNpc(npc.FormID) = text
        Return text
    End Function

    ''' <summary>Which of the three head-part facets an HDPT belongs to. Types per
    ''' FaceGenBuilder.PartType*: everything that is not Hair or Eyes lands in `face` —
    ''' including Misc, which is where addon-style parts live.</summary>
    Private Function HeadPartFacet(hdptFormID As UInteger) As NpcFilterFacet
        Dim hd = HeadPart(hdptFormID)
        If hd Is Nothing Then Return NpcFilterFacet.Face
        Select Case hd.TipoDeParte()
            Case FaceGenBuilder.PartTypeHair : Return NpcFilterFacet.Hair
            Case FaceGenBuilder.PartTypeEyes : Return NpcFilterFacet.Eyes
        End Select
        Return NpcFilterFacet.Face
    End Function

    Private Function HeadPart(hdptFormID As UInteger) As Canon.IHdpt
        Dim hd As Canon.IHdpt = Nothing
        If _headParts.TryGetValue(hdptFormID, hd) Then Return hd
        Dim rec = _pluginManager.GetRecord(hdptFormID)
        If rec IsNot Nothing AndAlso rec.Header.Signature = "HDPT" Then
            Try
                hd = Canon.CanonRecords.Hdpt(rec, _pluginManager)
            Catch
                hd = Nothing
            End Try
        End If
        _headParts(hdptFormID) = hd
        Return hd
    End Function

    ''' <summary>EDID + FULL (+ mesh path for head parts) of a referenced record, resolved ONCE per
    ''' record. The mesh path is in there because it is the only thing that identifies most modded
    ''' hair — the EDID is frequently a meaningless serial.</summary>
    Private Function RecordLabel(formID As UInteger) As String
        If formID = 0UI Then Return ""
        Dim cached As String = Nothing
        If _recordLabels.TryGetValue(formID, cached) Then Return cached

        Dim label As String = ""
        Dim rec = _pluginManager.GetRecord(formID)
        If rec IsNot Nothing Then
            Dim sb As New StringBuilder()
            sb.Append(rec.EditorID)
            Dim full = rec.GetSubrecord("FULL")
            If full.HasValue Then
                Dim fullText = _pluginManager.ResolveFieldString(rec, full.Value)
                If Not String.IsNullOrEmpty(fullText) Then sb.Append(" "c).Append(fullText)
            End If
            If rec.Header.Signature = "HDPT" Then
                Dim hd = HeadPart(formID)
                If hd IsNot Nothing AndAlso hd.ModelFileName <> "" Then sb.Append(" "c).Append(hd.ModelFileName)
            End If
            label = sb.ToString()
        End If
        _recordLabels(formID) = label
        Return label
    End Function

    ' ============================================================================================
    ' Template chain
    ' ============================================================================================

    Private Function TraitsNpc(npc As NPC_Data) As NPC_Data
        If Not _followTemplates Then Return npc
        Dim srcFid = ResolveSource(npc, NPC_TemplateCategory.Traits, _traitsSource)
        If srcFid = npc.FormID Then Return npc
        Dim src = _npcLookup(srcFid)
        Return If(src, npc)
    End Function

    Private Function InventoryNpc(npc As NPC_Data) As NPC_Data
        If Not _followTemplates Then Return npc
        Dim srcFid = ResolveSource(npc, NPC_TemplateCategory.Inventory, _inventorySource)
        If srcFid = npc.FormID Then Return npc
        Dim src = _npcLookup(srcFid)
        Return If(src, npc)
    End Function

    ''' <summary>Walk the template chain for one bucket and return the FormID of the NPC whose OWN
    ''' subrecords are the effective ones. Mirrors NpcStateResolver.ResolveTraitsStateFromNPC's rule:
    ''' follow while the bucket's flag is set; stop at the first NPC that does not inherit it.
    ''' <para>An LVLN source stops the walk (the lookup returns Nothing because only NPC_ records are
    ''' in the cache): a leveled template yields a DIFFERENT actor per spawn, so there is no single
    ''' honest answer and the filter reports the last readable NPC instead of inventing one.</para></summary>
    Private Function ResolveSource(npc As NPC_Data, category As NPC_TemplateCategory,
                                   cache As Dictionary(Of UInteger, UInteger)) As UInteger
        Dim cached As UInteger
        If cache.TryGetValue(npc.FormID, cached) Then Return cached

        Dim visited As New HashSet(Of UInteger)()
        Dim cur = npc
        Dim result = npc.FormID
        While cur IsNot Nothing AndAlso visited.Add(cur.FormID)
            result = cur.FormID
            If Not NpcTemplateHelpers.HasTemplateFlag(cur.Record.ConfigurationTemplateFlags, category) Then Exit While
            Dim nextFid = NpcTemplateHelpers.ResolveTemplateSourceFormID(cur, category)
            If nextFid = 0UI Then Exit While
            Dim nxt = _npcLookup(nextFid)
            If nxt Is Nothing Then Exit While
            cur = nxt
        End While

        cache(npc.FormID) = result
        Return result
    End Function

    Private Shared Function NonZero(ParamArray ids As UInteger()) As UInteger()
        Dim outIds As New List(Of UInteger)
        For Each id In ids
            If id <> 0UI AndAlso Not outIds.Contains(id) Then outIds.Add(id)
        Next
        Return outIds.ToArray()
    End Function
End Class
