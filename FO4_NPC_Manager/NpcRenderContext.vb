Imports System.Globalization
Imports System.IO
Imports System.Drawing
Imports System.Linq
Imports System.Threading
Imports System.Threading.Tasks
Imports FO4_Base_Library
Imports MaterialLib
Imports NiflySharp
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Phase 2 of the MainForm split: the single owner of per-load-order record-parse
''' services. Memoizes parsed ARMO/ARMA/RACE/HDPT/NPC_ records (thousands of records re-parsed
''' many times per render) behind FormID-keyed ConcurrentDictionary caches (background renders run
''' on Task.Run, so reads/writes can overlap). Injected by constructor (DI) into MainForm and the
''' extracted render subsystems so none of them re-implement parsing or reach into MainForm.
''' Real separate class, NOT a partial. See project_mainform_split.
'''
''' Lifetime: the PluginManager is immutable for this context's life, so a parsed result is stable
''' and the caches naturally die with the load order. InvalidateParseCaches clears ALL of them
''' together (owner-clears-its-own semantics); today ParseAllNPCs is the only caller and runs once
''' on still-empty caches, so it is defensive, not load-bearing.</summary>
Friend NotInheritable Class NpcRenderContext
    Public ReadOnly PluginManager As PluginManager

    Private ReadOnly _armoCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, ARMO_Data)()
    Private ReadOnly _armaCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, ARMA_Data)()
    Private ReadOnly _raceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, RACE_Data)()
    Private ReadOnly _hdptCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, HDPT_Data)()
    Private ReadOnly _armorRaceCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, HashSet(Of UInteger))()

    ''' <summary>Optional draft-resolver hook (set by MainForm). Given a FormID, returns a synthesized
    ''' <see cref="ARMO_Data"/> when the FormID is an in-memory ARMO draft (provisional 0xFF sentinel or
    ''' an override draft), or Nothing when it is not a draft (the real-record cache path then runs).
    ''' Same injection contract as <c>OutfitResolver.LeveledListResolver</c>: Nothing ⇒ library/real
    ''' behavior, a non-Nothing return ⇒ the app's live draft view. The returned object is NEVER cached
    ''' (drafts mutate live), so a draft edit is reflected on the next render.</summary>
    Public ArmoDraftResolver As Func(Of UInteger, ARMO_Data) = Nothing
    ''' <summary>Optional draft-resolver hook for ARMA drafts. See <see cref="ArmoDraftResolver"/>.</summary>
    Public ArmaDraftResolver As Func(Of UInteger, ARMA_Data) = Nothing

    Public Sub New(pluginManager As PluginManager)
        Me.PluginManager = pluginManager
    End Sub

    ''' <summary>O(1) record lookup, delegated to the PluginManager. Thin convenience so subsystems
    ''' depend on the context rather than capturing the PluginManager separately.</summary>
    Public Function GetRecord(formID As UInteger) As PluginRecord
        Return PluginManager.GetRecord(formID)
    End Function

    ''' <summary>The parsed NPC_ universe (shared instance). Public ReadOnly field — callers index/
    ''' mutate the contents (bulk-parse populate, GetParsedNpc memoize, tree iterate) while the
    ''' reference itself stays fixed. Not ReadOnly: VB rejects indexer-set (cache(k)=v) on a
    ''' ReadOnly field of an indexed type — same shape as the original _npcByIdCache field.</summary>
    Public NpcCache As New System.Collections.Concurrent.ConcurrentDictionary(Of UInteger, NPC_Data)()

    ''' <summary>Parsed NPC_ by FormID. Cache hit avoids re-parsing the record 5+ times per frame;
    ''' a miss (FormID outside the placed-NPC universe, e.g. a TPLT model source) parses via the
    ''' shared helper and memoizes.</summary>
    Public Function GetParsedNpc(formID As UInteger) As NPC_Data
        Dim cached As NPC_Data = Nothing
        If NpcCache.TryGetValue(formID, cached) AndAlso cached IsNot Nothing Then Return cached
        Dim parsed = NpcRecordOverlay.GetParsedNpc(formID, PluginManager)
        If parsed IsNot Nothing Then NpcCache(formID) = parsed
        Return parsed
    End Function

    ''' <summary>Parse (and cache) an ARMO by FormID. Nothing if the FormID does not resolve to an
    ''' ARMO. Does NOT swallow parse exceptions — callers that must tolerate a malformed record keep
    ''' their own Try/Catch.</summary>
    Public Function GetParsedArmo(formID As UInteger) As ARMO_Data
        If formID = 0UI Then Return Nothing
        ' Draft-aware resolution: an ARMO draft (MainForm._armoDrafts, provisional 0xFF FormID or an override
        ' draft keeping its real FormID) is NOT a real record and is NOT in this cache. Consult the app's draft
        ' resolver FIRST (mirror of OutfitResolver.LeveledListResolver / the OTFT-draft render path). A non-Nothing
        ' return is the live draft view — RETURN IT DIRECTLY, never store it in _armoCache: drafts mutate live and
        ' caching would stale them. Only real (non-draft) FormIDs fall through to the cached real-record path.
        If ArmoDraftResolver IsNot Nothing Then
            Dim draftView = ArmoDraftResolver(formID)
            If draftView IsNot Nothing Then Return draftView
        End If
        Return _armoCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = PluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMO" Then Return Nothing
                Return RecordParsers.ParseARMO(rec, PluginManager)
            End Function)
    End Function

    ''' <summary>Parse (and cache) an ARMA by FormID. Nothing if the FormID does not resolve to an ARMA.</summary>
    Public Function GetParsedArma(formID As UInteger) As ARMA_Data
        If formID = 0UI Then Return Nothing
        ' Draft-aware resolution — same rule as GetParsedArmo for ARMA drafts (MainForm._armaDrafts): consult the
        ' app's ArmaDraftResolver FIRST; a non-Nothing return is the live draft view and is RETURNED DIRECTLY,
        ' never stored in _armaCache (drafts mutate live). Real FormIDs fall through to the cached real-record path.
        If ArmaDraftResolver IsNot Nothing Then
            Dim draftView = ArmaDraftResolver(formID)
            If draftView IsNot Nothing Then Return draftView
        End If
        Return _armaCache.GetOrAdd(formID,
            Function(fid)
                Dim rec = PluginManager.GetRecord(fid)
                If rec Is Nothing OrElse rec.Header.Signature <> "ARMA" Then Return Nothing
                Return RecordParsers.ParseARMA(rec, PluginManager)
            End Function)
    End Function

    ''' <summary>Parse (and cache) a RACE from an already-fetched record, keyed by its FormID. The
    ''' NPC's race record is re-parsed ~20x/render (skeleton, body-weight, skin resolution) — this
    ''' collapses it to one parse.</summary>
    Public Function ParseRaceCached(rRec As PluginRecord) As RACE_Data
        If rRec Is Nothing Then Return Nothing
        Return _raceCache.GetOrAdd(rRec.Header.FormID, Function(fid) RecordParsers.ParseRACE(rRec, PluginManager))
    End Function

    ''' <summary>The set of races an armature (ARMA) may be authored for and still fit an actor of
    ''' <paramref name="raceFID"/>: the race itself PLUS every race reached by following the RACE.RNAM
    ''' "Armor Race" redirect chain. Copy-races (e.g. the CC Enclave turret) reuse a base race's armatures;
    ''' the engine matches the ARMA against the Armor Race, not the actor's own race. Cycle-guarded via the
    ''' HashSet (Add returns False on a revisit). Cached per race; the returned set is SHARED — callers must
    ''' not mutate it. Empty when raceFID = 0. See [[arch_armor_race_redirect]].</summary>
    Public Function GetEffectiveArmorRaces(raceFID As UInteger) As HashSet(Of UInteger)
        If raceFID = 0UI Then Return New HashSet(Of UInteger)()
        Return _armorRaceCache.GetOrAdd(raceFID,
            Function(fid)
                Dim races As New HashSet(Of UInteger)()
                Dim cur = fid
                While cur <> 0UI AndAlso races.Add(cur)
                    Dim rec = GetRecord(cur)
                    If rec Is Nothing OrElse rec.Header.Signature <> "RACE" Then Exit While
                    Dim race = ParseRaceCached(rec)
                    If race Is Nothing Then Exit While
                    cur = race.ArmorRaceFormID
                End While
                Return races
            End Function)
    End Function

    ''' <summary>Parse (and cache) an HDPT from an already-fetched record, keyed by its FormID.</summary>
    Public Function ParseHdptCached(hRec As PluginRecord) As HDPT_Data
        If hRec Is Nothing Then Return Nothing
        Return _hdptCache.GetOrAdd(hRec.Header.FormID, Function(fid) RecordParsers.ParseHDPT(hRec, PluginManager))
    End Function

    ''' <summary>Clear every parse cache. Call on load-order change (MainForm.ParseAllNPCs). Clears
    ''' ALL caches the context owns — consistent owner semantics (the old MainForm cleared RACE/HDPT/
    ''' NPC_ but not ARMO/ARMA; harmless given the immutable PluginManager, unified here).</summary>
    Public Sub InvalidateParseCaches()
        NpcCache.Clear()
        _armoCache.Clear()
        _armaCache.Clear()
        _raceCache.Clear()
        _hdptCache.Clear()
        _armorRaceCache.Clear()
    End Sub
End Class
