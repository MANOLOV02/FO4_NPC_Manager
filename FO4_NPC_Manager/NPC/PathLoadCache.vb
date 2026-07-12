' ==========================================================================
' Path-keyed load-once cache for parsed FilesDictionary assets (.tri PIRT, TriHead, ...).
'
' Why this exists: the morph resolvers run under Parallel.ForEach (PipelineStep_Morphs),
' so several shapes of the same actor resolve the SAME .tri path concurrently. The old
' shape — a positive Dictionary plus a separate "already attempted" HashSet — marked the
' path as attempted BEFORE the (slow) BA2 read+parse, so a second thread arriving inside
' that window saw "attempted" with nothing in the positive cache and gave up with Nothing.
' Whoever lost the race rendered that shape unmorphed, non-deterministically.
'
' Contract here: exactly one thread loads a given key; every other thread waiting on the
' same key blocks on its gate and receives the same result. A failed load is cached as
' Nothing (so an absent/invalid path is not re-decompressed from the BA2 every frame),
' but it is only recorded AFTER the attempt resolves — never before.
' ==========================================================================

''' <summary>Thread-safe "load at most once per path" cache. A cached Nothing means the load was
''' attempted and failed (missing file, wrong magic, unparseable) — it is not retried until Clear.</summary>
Friend Class PathLoadCache(Of T As Class)

    ' Value Nothing = attempted and failed. Presence of the key (not the value) is what marks
    ' the attempt, so TryGetValue distinguishes "never tried" from "tried, no result".
    Private ReadOnly _entries As New Dictionary(Of String, T)(StringComparer.OrdinalIgnoreCase)
    ' One gate object per key: concurrent callers for the SAME path serialize, callers for
    ' different paths still load in parallel.
    Private ReadOnly _gates As New Dictionary(Of String, Object)(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Return the cached parse for <paramref name="key"/>, loading it via
    ''' <paramref name="loader"/> exactly once. Concurrent callers for the same key wait for the
    ''' in-flight load instead of short-circuiting to Nothing. A loader that throws is treated as
    ''' a failed load.</summary>
    Public Function GetOrLoad(key As String, loader As Func(Of T)) As T
        If String.IsNullOrEmpty(key) OrElse loader Is Nothing Then Return Nothing

        Dim cached As T = Nothing
        SyncLock _entries
            If _entries.TryGetValue(key, cached) Then Return cached
        End SyncLock

        Dim gate As Object = Nothing
        SyncLock _gates
            If Not _gates.TryGetValue(key, gate) Then
                gate = New Object()
                _gates(key) = gate
            End If
        End SyncLock

        SyncLock gate
            ' Re-check: another thread may have completed the load while we waited on the gate.
            SyncLock _entries
                If _entries.TryGetValue(key, cached) Then Return cached
            End SyncLock

            Dim loaded As T = Nothing
            Try
                loaded = loader()
            Catch
                loaded = Nothing
            End Try

            SyncLock _entries
                _entries(key) = loaded
            End SyncLock
            Return loaded
        End SyncLock
    End Function

    ''' <summary>Drop every cached parse (and the gates). Call on load-order change, when a path can
    ''' resolve to different bytes and a previously failed path may now load.</summary>
    Public Sub Clear()
        SyncLock _entries
            _entries.Clear()
        End SyncLock
        SyncLock _gates
            _gates.Clear()
        End SyncLock
    End Sub

End Class
