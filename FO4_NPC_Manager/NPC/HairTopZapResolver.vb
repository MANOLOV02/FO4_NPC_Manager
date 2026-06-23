Imports FO4_Base_Library
Imports NiflySharp.Blocks
Imports OpenTK.Mathematics

''' <summary>Which FaceGen-hair partitions of a {30,31} piece get ZAPPED this render. Engine-faithful,
''' RACE-driven, UNIFORM for the main hair AND each hairline (HNAM-extra) — they carry the same slot tags so
''' they follow the IDENTICAL rule (NO inverse complement; the old inverse-hairline model was retired):
'''   una partición se zapea ⟺ su slot está cubierto por el worn set Y cae dentro del canal de pelo de la
'''   raza (RaceUtil.RaceHairMask; HumanRace = {30,31}). top→zap si slot 30 cubierto; long→zap si slot 31.
'''   FULL-FACE CULL (face-cull slot, HumanRace 32): la pieza entera se oculta vía IsOccludedByHeadwear.
''' Bitmask: Top = v30−v31 (BSTriShapeGeometry.GetTopOnlyVertexIndices), Long = v31−v30
''' (BSTriShapeGeometry.GetLongOnlyVertexIndices). Both = unión de ambos = la pieza entera salvo el ring
''' compartido; SelectWinningCandidates NO emite Both (cuando ambas particiones están cubiertas la pieza ya
''' cae en IsOccludedByHeadwear), pero el resolver lo soporta por completitud. Consumido por
''' HairTopZapResolver (emite el/los canal(es) de zap) y ButtonSaveSceneNif_Click (compacta los verts con
''' VertexMask=-1, agnóstico a la partición). La decisión de qué partición(es) zapear se toma en
''' MainForm.SelectWinningCandidates (NpcMeshCollector) por la regla per-partición de arriba.</summary>
<Flags>
Public Enum HairZapParts
    None = 0
    Top = 1
    ' [Long] escapado con corchetes: 'Long' es palabra reservada de VB (tipo). El identificador del
    ' miembro es Long; los consumidores lo escriben HairZapParts.Long (sin corchetes, ya cualificado).
    [Long] = 2
    Both = Top Or [Long]
End Enum

' ==========================================================================
' Hair partition zap resolver for FO4_NPC_Manager.
'
' A FaceGen hair {30,31} mesh has two partitions: the TOP (biped 30 = crown) and the LONG (biped 31).
' Each piece (the main hair AND each hairline HNAM-extra) carries a per-shape HairZapParts telling this
' resolver WHICH partition(s) to zap this render (decided in MainForm.SelectWinningCandidates per the
' RACE-driven, uniform per-partition rule — main and hairline identical). The resolver emits a single zap
' MorphChannel whose Deltas carry the UNION of the requested partitions' vertex indices:
'   Top  → v30 − v31 (GetTopOnlyVertexIndices)
'   Long → v31 − v30 (GetLongOnlyVertexIndices)
'   Both → union of the two.
'
' Mechanism: emit a zap MorphChannel (IsZap=True, weight=1) whose Deltas carry the partition vertex
' indices. MorphEngine.ApplyMorphPlan turns those into VertexMask(i) = -1, and the renderer skips them
' when shape.ApplyZaps = True (Render.vb:1118 / shader bApplyZap discard). Because the zap is a CHANNEL of
' the per-frame morph plan, it is re-applied AFTER ApplyMorphPlan clears the mask every frame, so it
' survives the face/body vertex morphs that run in the same plan. SUMMARY: zap survival is by
' construction — the resolver re-injects the channel on every render update. A plan that carries ONLY this
' zap channel (the hairline case: HNAM-extra has no chargen-TRI morph) still applies: ApplyMorphPlan gates
' the zap loop on MorphPlan.HasZaps, not on position deltas.
'
' Render-headwear OFF: the composite (MainForm.BuildCompositeMorphResolver) drops this resolver, so on the
' next morph pass the zap channel is gone and the mesh is revealed whole — same toggle semantics as the
' rest of the head-part occlusion.
'
' The partition vertex sets are a stable property of the mesh segmentation (do not depend on pose/morph),
' so each set is cached per (shape, partition-flag) — the helpers' own docs tell callers to do this.
'
' App-local (eje A), same scope decision as MultiMorphResolver: only NPC_Manager needs the FaceGen hair
' top/long split. Promote to FO4_Base_Library only if a second consuming app appears.
' ==========================================================================
Public Class HairTopZapResolver
    Implements IMorphResolver

    ''' <summary>Shapes whose hair partition(s) must be zapped this render, mapped to WHICH partition(s)
    ''' (Top / Long / Both). Held by reference; the caller (MainForm) builds a fresh map per render from
    ''' PreviewResolutionResult.ShapeZapHairParts (only non-None entries). Empty map ⇒ resolver is inert.</summary>
    Private ReadOnly _zapShapes As Dictionary(Of IRenderableShape, HairZapParts)

    ''' <summary>Per-shape cache of the emitted zap vertex index list, keyed by (shape, parts). Both the
    ''' shape references (stable for the resolver's lifetime) and the partition vertex sets (a stable
    ''' property of the mesh segmentation — see Get{Top,Long}OnlyVertexIndices docs) are stable, so the
    ''' built list is cached. Keyed on parts too so a shape whose ZapParts changed across resolver
    ''' instances never reuses a stale union (each render builds a fresh resolver, but this is cheap
    ''' insurance and keeps the cache correct if a shape ever appears with two different parts).</summary>
    Private ReadOnly _deltaCache As New Dictionary(Of (IRenderableShape, HairZapParts), List(Of MorphData))

    Public Sub New(zapShapes As Dictionary(Of IRenderableShape, HairZapParts))
        _zapShapes = If(zapShapes, New Dictionary(Of IRenderableShape, HairZapParts)())
    End Sub

    ''' <summary>True when no shape is flagged for zap — lets the composite skip wiring this resolver.</summary>
    Public ReadOnly Property IsEmpty As Boolean
        Get
            Return _zapShapes.Count = 0
        End Get
    End Property

    Public Function ResolveMorphPlan(shape As IRenderableShape, geom As SkinnedGeometry) As MorphPlan _
            Implements IMorphResolver.ResolveMorphPlan
        Dim plan As New MorphPlan()
        Dim parts As HairZapParts
        If shape Is Nothing OrElse Not _zapShapes.TryGetValue(shape, parts) OrElse parts = HairZapParts.None Then Return plan

        Dim deltas = GetZapDeltas(shape, parts)
        ' [HAIRZAP-DIAG] per shape that reached the resolver: the NifShape concrete type, the requested
        ' partition(s), and the emitted vertex count. A shape in _zapShapes but with 0 deltas means its
        ' rendered NifShape is not a BSSubIndexTriShape (or lost its 30/31 split) at render time.
        If Logger.Enabled Then
            Dim shName = If(shape.ShapeName, "?")
            Dim nifTypeName = If(shape.NifShape Is Nothing, "<null>", shape.NifShape.GetType().Name)
            Dim deltaCount = If(deltas Is Nothing, 0, deltas.Count)
            Logger.LogLazy(Function() $"[HAIRZAP-DIAG] ResolveMorphPlan shape='{shName}' nifType={nifTypeName} parts={parts} zapDeltas={deltaCount} ApplyZaps={shape.ApplyZaps}")
        End If
        If deltas Is Nothing OrElse deltas.Count = 0 Then Return plan

        ' weight = 1 → ApplyMorphPlan sets VertexMask(i) = -1 for each carried index (MorphEngine.vb).
        ' PosDiff is unused for zap channels (skipped by ApplyChannelsToVertexArray) so it stays zero.
        plan.Channels.Add(New MorphChannel("__HairZap__", 1.0F, deltas, isZap:=True))
        Return plan
    End Function

    ''' <summary>Union of the requested partitions' vertex indices for the shape, packaged as zap MorphData
    ''' (PosDiff unused). Cached per (shape, parts). Returns an empty list when the shape is not a
    ''' BSSubIndexTriShape or has no matching 30/31 partition.</summary>
    Private Function GetZapDeltas(shape As IRenderableShape, parts As HairZapParts) As List(Of MorphData)
        Dim key = (shape, parts)
        Dim cached As List(Of MorphData) = Nothing
        SyncLock _deltaCache
            If _deltaCache.TryGetValue(key, cached) Then Return cached
        End SyncLock

        Dim built As New List(Of MorphData)()
        Dim subIdx = TryCast(shape.NifShape, BSSubIndexTriShape)
        If subIdx IsNot Nothing Then
            ' Union of the requested partition vertex sets. HashSet dedups the (degenerate) overlap
            ' that cannot happen for Top/Long disjoint sets but keeps the union correct for Both.
            Dim verts As New HashSet(Of Integer)()
            If (parts And HairZapParts.Top) <> 0 Then verts.UnionWith(BSTriShapeGeometry.GetTopOnlyVertexIndices(subIdx))
            If (parts And HairZapParts.Long) <> 0 Then verts.UnionWith(BSTriShapeGeometry.GetLongOnlyVertexIndices(subIdx))
            For Each vi In verts
                If vi >= 0 Then built.Add(New MorphData With {.index = CUInt(vi), .PosDiff = Vector3.Zero})
            Next
        End If

        SyncLock _deltaCache
            _deltaCache(key) = built
        End SyncLock
        Return built
    End Function
End Class
