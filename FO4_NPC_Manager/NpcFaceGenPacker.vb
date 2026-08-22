Option Strict On
Imports System.IO
Imports System.Threading
Imports BSA_BA2_Library_DLL.BethesdaArchive.Core
Imports FO4_Base_Library
Imports FO4_Base_Library.Archives

''' <summary>
''' Batches the FaceGen loose files produced by <c>FaceGenBuilder.BuildCharGen</c> (FO4: 1 NIF + 3
''' FaceCustomization DDS per NPC; SSE: 1 NIF + 1 FaceTint DDS — see <see cref="FaceGenFileSpecs"/>)
''' into the archive set anchored to the Save ESP plugin.
'''
''' Pattern mirrors Wardrobe_Manager.WM_PackUnpack.Pack (the proven shape):
'''   1) Walk the bundles → flat list of <see cref="LooseFileRef"/> (sourcePath + canonical entryPath
'''      + isTexture + debugSandbox). No bytes loaded yet.
'''   2) Micro-batch parallel compress (<see cref="MICRO_BATCH"/> entries per pass via Parallel.For).
'''      Transient peak RAM is bounded by MICRO_BATCH × max-file-size; workers free their slot as
'''      soon as the compressed VirtualEntry is folded into the main accumulator.
'''   3) Accumulator (chunkEntries) collects pre-compressed entries until the cumulative compressed
'''      size would exceed <see cref="MEMORY_CAP_BYTES"/>. At that point the buffer is flushed via
'''      a SINGLE <see cref="ArchivePackager.Pack"/> call (anchored to the same plugin, never split),
'''      then PreCompressedBytes are nulled out so the next chunk starts with clean memory.
'''   4) Final flush after the walk. Loose sources of committed refs are deleted (including
'''      the _2 sandbox files when <c>DebugSandbox</c> is True — BA2 is the source of truth
'''      once a bundle commits, regardless of which suffix the disk file carried).
'''
''' Override semantics preserved by ArchivePackager.Pack on every flush:
'''   - Existing BA2 entries not in the bundle: stream-copy.
'''   - Bundle entries that overlap with the existing archive: ComputeDiff CRC32 → stream-copy
'''     unchanged or rewrite changed.
'''   - Re-saving the same NPC: the 4 paths overlap → stream-copy / rewrite per ComputeDiff.
'''
''' Single archive anchor (Overflow=ThrowOnExceed). Unlike WM, NPC_Manager never splits across
''' numbered plugins — the Save ESP IS the anchor and the user expects one set of BA2s next to it.
''' If the accumulated archive (existing + new) exceeds the FO4 3 GB cap inside the packager,
''' Pack throws — caller surfaces the error in the save summary.
''' </summary>
Public Module NpcFaceGenPacker

    ''' <summary>One baked NPC's identity. The packer derives the loose paths from these three
    ''' fields via <see cref="FaceGenFileSpecs"/>, the same naming FaceGenBuilder used to write them.
    ''' Public (not Friend) so it can be exposed through the Public SaveContext delegates
    ''' (NpcOverrideSaver.SaveContext.RunChargenBake / RunChargenPackBatch).</summary>
    Public Class BakedNpcBundle
        ''' <summary>Plugin name segment in the FaceGen path (NPC's source master, e.g.
        ''' "Fallout4.esm" or the auto-gen plugin that owns the override).</summary>
        Public Property OriginPlugin As String = ""
        ''' <summary>NPC FormID with the master-index high byte cleared (matches the
        ''' FormID8hex naming FaceGenBuilder used for the loose files).</summary>
        Public Property FormIdLow As UInteger
        ''' <summary>True when the bake ran with FaceGenBuilder.DebugMode=True (loose written
        ''' with a _2 suffix). The packer reads the _2 files but stores entries under canonical
        ''' names. The _2 sources are deleted after a successful pack just like canonical ones
        ''' (2026-05-26 change — BA2 is the post-pack source of truth).</summary>
        Public Property DebugSandbox As Boolean
    End Class

    ''' <summary>One file of an NPC's FaceGen bake, as Data-relative paths. <c>Source</c> carries the
    ''' _2 debug suffix when the bake ran in DebugMode; <c>Entry</c> is always the canonical name the
    ''' engine (and the archive) expects.</summary>
    Friend Structure FaceGenFileSpec
        Public Source As String
        Public Entry As String
        Public IsTexture As Boolean
        ''' <summary>True = the bake emits this file only conditionally (e.g. the SSE per-NPC diffuse, written
        ''' only for NPCs with RaceMenu face overlays). When the source is absent on disk it is SILENTLY skipped
        ''' — not counted as a missing source and not counted toward the bundle's expected file count — so a
        ''' vanilla NPC (no diffuse) still commits fully. Required files (False) that are missing = a bake bug.</summary>
        Public IsOptional As Boolean
    End Structure

    ''' <summary>Data-relative file specs for one NPC's FaceGen bake, GAME-AWARE — the single source of
    ''' truth for what <c>FaceGenBuilder</c> wrote and what the packer / delete flow must look for:
    '''   FO4: FaceGeom NIF + 3 FaceCustomization DDS (_d/_msn/_s), all with a _2 debug variant.
    '''   SSE: FaceGeom NIF + 1 FaceGenData\FaceTint DDS, also with a _2 debug variant. Matches the vanilla
    '''        CK layout in the BSA (textures\actors\character\facegendata\facetint\&lt;plugin&gt;\&lt;id&gt;.dds,
    '''        512² DXT5, _d only) and what <c>FaceGenBuilder.WriteSseFacetintDds</c> writes.
    ''' The _2b GPU-sandbox dumps are deliberately absent: they never enter an archive and are not deleted
    ''' with the bake — they exist for CPU-vs-GPU inspection.</summary>
    Friend Function FaceGenFileSpecs(game As Config_App.Game_Enum, originPlugin As String,
                                     formIdLow As UInteger, debugSandbox As Boolean) As List(Of FaceGenFileSpec)
        Dim specs As New List(Of FaceGenFileSpec)
        If String.IsNullOrEmpty(originPlugin) Then Return specs
        Dim hex = formIdLow.ToString("X8")

        Dim geomDir = FaceGenPaths.GeomDir(originPlugin)
        specs.Add(New FaceGenFileSpec With {
            .Source = geomDir & hex & If(debugSandbox, "_2.nif", ".nif"),
            .Entry = geomDir & hex & ".nif",
            .IsTexture = False})

        If game = Config_App.Game_Enum.Skyrim Then
            Dim tintDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalTint, originPlugin)
            ' Facetint: SIEMPRE per-NPC <id>.dds canónico. El engine ARMA `FaceTint\<plugin>\<id>.dds` él mismo
            ' (BuildFaceTintPath, RE SkyrimSE.exe) e IGNORA el slot 6 del NIF ⇒ NO se puede compartir ni omitir; si
            ' falta, el tint del material queda NULL → cara brown. Para NPCs plegados es un gris neutral (fgTint≈1).
            specs.Add(New FaceGenFileSpec With {
                .Source = tintDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = tintDir & hex & ".dds",
                .IsTexture = True})
            ' (Eliminado el spec del `facedetailneutral.dds` COMPARTIDO por plugin: el fold ya no neutraliza el
            '  slot 3 — deja el detail REAL y pre-compensa el amplify en el diffuse. Ver
            '  SseFaceGenBaker.PreCompensateEngineChain. Era el único artefacto compartido entre NPCs/ESPs.)
            ' OPTIONAL per-NPC head diffuse — emitted only when the NPC has RaceMenu face overlays/skee masks baked
            ' in (FaceGenBuilder.WriteSseFaceDiffuseWithOverlays). Absent for vanilla NPCs → silently skipped.
            Dim diffDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalDiffuse, originPlugin)
            specs.Add(New FaceGenFileSpec With {
                .Source = diffDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = diffDir & hex & ".dds",
                .IsTexture = True,
                .IsOptional = True})
            ' OPTIONAL per-NPC head normal (_msn) — emitted only when a face overlay carries a normal map.
            Dim normDir = FaceGenPaths.TexturaDir(FaceGenPaths.CanalNormal, originPlugin)
            specs.Add(New FaceGenFileSpec With {
                .Source = normDir & hex & If(debugSandbox, "_2.dds", ".dds"),
                .Entry = normDir & hex & ".dds",
                .IsTexture = True,
                .IsOptional = True})
        Else
            Dim texDir = "Textures\Actors\Character\FaceCustomization\" & originPlugin & "\"
            For Each suffix In {"_d", "_msn", "_s"}
                specs.Add(New FaceGenFileSpec With {
                    .Source = texDir & hex & suffix & If(debugSandbox, "_2.dds", ".dds"),
                    .Entry = texDir & hex & suffix & ".dds",
                    .IsTexture = True})
            Next
        End If
        Return specs
    End Function

    ''' <summary>The CANONICAL archive entry paths (as stored inside the archive — no _2 debug suffix) for one
    ''' NPC's FaceGen bake: the FaceGeom NIF (→ Main archive) + the texture(s) (→ Textures archive). Count and
    ''' texture layout are game-aware — see <see cref="FaceGenFileSpecs"/>, the same source <see cref="PackBatch"/>
    ''' uses for its bundle spec. Used by the "mark to delete" flow to build the ExcludePaths passed to
    ''' <see cref="PackBatch"/>. Empty when the origin plugin can't be resolved.</summary>
    Public Function CanonicalFaceGenEntryPathsForNpc(npcFormID As UInteger, pluginManager As PluginManager,
                                                     game As Config_App.Game_Enum) As List(Of String)
        Dim origin = pluginManager.GetOriginatingPluginName(npcFormID)
        If String.IsNullOrEmpty(origin) Then Return New List(Of String)()
        Dim local = PluginManager.ToFaceGenLocalFormID(npcFormID)
        Return FaceGenFileSpecs(game, origin, local, debugSandbox:=False).
               Select(Function(s) s.Entry).ToList()
    End Function

    ''' <summary>Aggregate result of <see cref="PackBatch"/> across one or more flushes.</summary>
    Friend Class PackResult
        Public Property Success As Boolean
        ''' <summary>Archive paths that were (re)written across all flushes.</summary>
        Public ReadOnly WrittenArchives As New List(Of String)
        ''' <summary>Archive paths the packer reported as skipped (byte-identical) on any flush.</summary>
        Public ReadOnly SkippedArchives As New List(Of String)
        ''' <summary>Loose files removed from disk after their containing flush succeeded.</summary>
        Public ReadOnly DeletedLoose As New List(Of String)
        ''' <summary>Now-empty directories pruned (up to, but excluding, Data) after deletions.</summary>
        Public ReadOnly RemovedDirs As New List(Of String)
        ''' <summary>How many flushes were committed to disk (one ArchivePackager.Pack call each).</summary>
        Public Property FlushesCommitted As Integer
        ''' <summary>How many input bundles produced at least one VirtualEntry that landed in a flush.
        ''' A bundle is "committed" iff every loose file its game's layout calls for existed on disk
        ''' AND all of its entries were part of a successful flush.</summary>
        Public Property BundlesCommitted As Integer
        ''' <summary>Loose source paths that the packer expected (from bundles) but did not find
        ''' on disk. Each missing source = one of the bake outputs (see FaceGenFileSpecs) for some NPC
        ''' that <c>FaceGenBuilder.BuildCharGen</c> reported as Success but did not actually produce.
        ''' Surfacing the count in the summary helps the user see how many bundles were dropped
        ''' before flush.</summary>
        Public ReadOnly MissingSources As New List(Of String)
        ''' <summary>Bundles descartados ENTEROS por faltarles un archivo REQUERIDO, ya formateados
        ''' "&lt;plugin&gt; 0x&lt;formId&gt;: faltan N archivo(s), p.ej. '&lt;nombre&gt;'". Existe porque el mensaje al usuario
        ''' era "N NPCs failed to pack (M files unaccounted for)" — sin decir QUÉ NPC ni QUÉ archivo — y el
        ''' desglose por path sólo vivía en el log, que en Release no se escribe (Logger.Enabled=False). Con esto
        ''' el MessageBox del save puede nombrarlos, como ya hace el batch loose.</summary>
        Public ReadOnly FailedBundles As New List(Of String)
        ''' <summary>Free-form failure message when Success = False.</summary>
        Public Property ErrorMessage As String = ""
    End Class

    ''' <summary>Progress phases reported through the optional progress callback.</summary>
    Friend Enum PackPhase
        BuildingBundle
        WritingArchive
        DeletingLoose
        Done
    End Enum

    ''' <summary>Lightweight progress envelope.</summary>
    Friend Class PackProgress
        Public Phase As PackPhase
        Public Detail As String = ""
        Public Current As Integer
        Public Max As Integer
    End Class

    ' --- Tunables -------------------------------------------------------------------------------
    ' Compressed-bytes ceiling for the accumulator. When folding the next compressed entry would
    ' cross this, the buffer is flushed to disk first. 500 MB lands ~300–3000 NPC bundles in a
    ' single flush (typical NPC compressed total: 150 KB–1.5 MB), which keeps almost every
    ' real-world save to one ArchivePackager.Pack call. Mass batches that exceed it incur
    ' K flushes (K = ceil(totalCompressedBytes / MEMORY_CAP_BYTES)) but never per-NPC.
    Private Const MEMORY_CAP_BYTES As Long = 500L << 20

    ' Per-pass parallel-compress micro-batch. Bounds transient RAM during compression to
    ' MICRO_BATCH × max-file-size across all worker threads. Lower than WM_PackUnpack's 64 because
    ' FaceGen DDS are small and compression overhead dominates — 32 is the sweet spot for typical
    ' face textures (256×256 to 1024×1024 BC1/BC3 mipped).
    Private Const MICRO_BATCH As Integer = 32

    ' FO4 BA2 hard cap inside the packager. Engine is unstable past 4 GB; 3 GB leaves headroom.
    ' This bounds a SINGLE archive (existing + new bundle), independent of MEMORY_CAP_BYTES which
    ' bounds the per-flush working set.
    ' El tope es del FORMATO, no de esta app: vive en PackagerRequest.MaxArchiveBytesDefault.
    Private ReadOnly MAX_ARCHIVE_BYTES As Long = BSA_BA2_Library_DLL.BethesdaArchive.Core.PackagerRequest.MaxArchiveBytesDefault

    ''' <summary>One loose file in the bundle, with its canonical BA2 entry name. SourcePath is
    ''' the actual on-disk file (carries _2 suffix when bake ran in DebugMode); EntryPath is the
    ''' canonical name the entry takes inside the BA2 (never _2). The DebugSandbox flag is kept
    ''' on the ref for traceability but no longer gates deletion (post 2026-05-26).</summary>
    Private Class LooseFileRef
        Public Property SourcePath As String            ' actual file on disk (may carry _2 suffix)
        Public Property EntryPath As String             ' canonical path inside the BA2 (never _2)
        Public Property IsTexture As Boolean
        Public Property DebugSandbox As Boolean
    End Class

    ''' <summary>Pack the FaceGen loose for <paramref name="bundles"/> into the BA2 set anchored
    ''' to <paramref name="anchorPluginPath"/>. Loose files are read from disk and compressed in
    ''' bounded-memory micro-batches; the accumulator flushes whenever the running compressed
    ''' total would exceed <see cref="MEMORY_CAP_BYTES"/>. Per-flush ArchivePackager.Pack call
    ''' preserves override semantics (CRC32 diff against existing archive).
    ''' </summary>
    ''' <param name="anchorPluginPath">Full path to the Save ESP plugin written this same
    ''' transaction. Its file name (without extension) becomes the BA2 ModBaseName, so the
    ''' engine auto-loads "&lt;name&gt; - Main.ba2" + "&lt;name&gt; - Textures.ba2" alongside the plugin.</param>
    ''' <param name="dataDir">FO4 Data folder.</param>
    ''' <param name="game">Game variant. Only Fallout4 is exercised today; Skyrim path is provided
    ''' for parity with WM_PackUnpack and uses BSA / LZ4 frame.</param>
    ''' <param name="ba2Version">Header version for the FO4 BA2 writer. Caller must NOT pass 0
    ''' (the loose-only sentinel); that case is decided at the orchestrator level (no PackBatch call).</param>
    ''' <param name="bundles">One entry per baked NPC. The packer derives the loose paths from
    ''' (game, OriginPlugin, FormIdLow, DebugSandbox) via <see cref="FaceGenFileSpecs"/> — the same
    ''' naming FaceGenBuilder applied at bake.</param>
    ''' <param name="progress">Optional progress callback. Invoked synchronously on the worker
    ''' thread; the caller's IProgress(Of T) wrapper marshals back to the UI thread.</param>
    ''' <param name="ct">Cancellation token. Checked at safe checkpoints (between micro-batches
    ''' and before each flush) — never mid-flush, so the on-disk archive stays consistent.</param>
    ''' <param name="excludeEntries">Optional canonical archive entry paths (as stored inside the BA2, e.g.
    ''' "Meshes\Actors\Character\FaceGenData\FaceGeom\&lt;plugin&gt;\&lt;id&gt;.nif") to REMOVE from the app's
    ''' target archive set — the "mark to delete" flow strips a removed NPC's stale FaceGen bake. Applied on
    ''' the same Pack calls that write new bakes; when there are NO new bakes (delete-only) a single removal-only
    ''' Pack runs so the drop still happens. Nothing/empty = no removals (existing callers unaffected).</param>
    Friend Function PackBatch(anchorPluginPath As String,
                              dataDir As String,
                              game As Config_App.Game_Enum,
                              ba2Version As UInteger,
                              bundles As IReadOnlyList(Of BakedNpcBundle),
                              Optional progress As Action(Of PackProgress) = Nothing,
                              Optional ct As CancellationToken = Nothing,
                              Optional excludeEntries As IEnumerable(Of String) = Nothing) As PackResult
        Dim result As New PackResult()

        ' Canonical entry paths to strip from the target archive set (mark-to-delete). Empty = no removals.
        Dim excludeSet As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
        If excludeEntries IsNot Nothing Then
            For Each e In excludeEntries
                If Not String.IsNullOrEmpty(e) Then excludeSet.Add(e)
            Next
        End If

        If String.IsNullOrEmpty(anchorPluginPath) OrElse Not File.Exists(anchorPluginPath) Then
            result.ErrorMessage = $"Anchor plugin not found: '{anchorPluginPath}'."
            Return result
        End If
        If String.IsNullOrEmpty(dataDir) OrElse Not Directory.Exists(dataDir) Then
            result.ErrorMessage = $"Data folder not found: '{dataDir}'."
            Return result
        End If
        ' Nothing to pack AND nothing to remove → true no-op. With exclusions we still proceed (delete-only).
        If (bundles Is Nothing OrElse bundles.Count = 0) AndAlso excludeSet.Count = 0 Then
            result.Success = True
            Report(progress, PackPhase.Done, "No bundles to pack.", 0, 0)
            Return result
        End If
        If bundles Is Nothing Then bundles = New List(Of BakedNpcBundle)()

        ' --- Step 1: walk bundles → flat LooseFileRef list -----------------------------------
        ' One bundle = the game's FaceGenFileSpecs refs (NIF first, then the DDS in stable order).
        ' Refs whose source is missing on disk are dropped with a warning into the result; missing
        ' sources are a bake-phase bug (FaceGenBuilder should always produce every file), surfaced
        ' here so the user sees it but the rest of the batch still ships.
        Dim allRefs As New List(Of LooseFileRef)
        Dim refToBundleIdx As New List(Of Integer)
        Dim bundleRefCounts(bundles.Count - 1) As Integer   ' how many refs per bundle made it in
        Dim bundleExpected(bundles.Count - 1) As Integer    ' how many the game's layout calls for
        Dim missingSources As New List(Of String)

        ' B1 — EL BUNDLE ES ATÓMICO: o entran TODOS sus archivos requeridos, o NO ENTRA NINGUNO.
        ' EL BUG: el `Continue For` de un spec requerido faltante era POR SPEC, así que los demás archivos del
        ' MISMO NPC se empaquetaban igual. El bundle no contaba como committed, pero (a) PackResult.Success seguía
        ' True, (b) sus refs SÍ llegaban a `committedRefs` y por lo tanto (c) sus sueltos se BORRABAN en el paso 5.
        ' Caso concreto y medido en SSE: si WriteSseFacetintDds bailó, falta `FaceTint\<id>.dds` (spec REQUERIDO) y
        ' el NIF entraba al BSA SIN su facetint ⇒ ApplyFaceTintToHeadMaterial (0x1403BC400) hace LoadTexture NULL,
        ' [mat+0xA0]=NULL, CARA MARRÓN in-game. Y como los sueltos ya se borraron, tampoco se puede re-empacar.
        ' Ahora se resuelve la lista COMPLETA de specs primero y, si falta alguno requerido, el bundle entero se
        ' descarta ANTES de agregar un solo ref: el archive nunca queda a medias y los sueltos sobreviven para
        ' reintentar. El save lo reporta con nombre y archivo (ver failedBundles).
        For bi = 0 To bundles.Count - 1
            Dim b = bundles(bi)
            Dim bundleSpec = FaceGenFileSpecs(game, b.OriginPlugin, b.FormIdLow, b.DebugSandbox)

            Dim pending As New List(Of LooseFileRef)
            Dim missingForThisBundle As New List(Of String)
            For Each spec In bundleSpec
                Dim sourcePath = Path.Combine(dataDir, spec.Source)
                If Not File.Exists(sourcePath) Then
                    ' Optional (per-NPC diffuse/normal): absent = the NPC has no face overlays → silently skip.
                    ' Required: absent = a bake bug → tumba el bundle ENTERO (ver la nota de arriba).
                    If Not spec.IsOptional Then missingForThisBundle.Add(sourcePath)
                    Continue For
                End If
                pending.Add(New LooseFileRef With {
                    .SourcePath = sourcePath,
                    .EntryPath = Path.Combine(dataDir, spec.Entry),
                    .IsTexture = spec.IsTexture,
                    .DebugSandbox = b.DebugSandbox
                })
            Next

            If missingForThisBundle.Count > 0 Then
                ' Bundle DESCARTADO: no se agrega NINGUNO de sus refs ⇒ no se empaqueta nada suyo y, al no llegar
                ' a committedRefs, sus sueltos NO se borran. bundleExpected queda > bundleRefCounts ⇒ tampoco
                ' cuenta como committed en el resumen.
                missingSources.AddRange(missingForThisBundle)
                bundleExpected(bi) = pending.Count + missingForThisBundle.Count
                Dim fidF = b.FormIdLow, opF = b.OriginPlugin
                Dim firstF = Path.GetFileName(missingForThisBundle(0))
                Dim nF = missingForThisBundle.Count
                result.FailedBundles.Add($"{opF} 0x{fidF:X8}: {nF} bake file(s) missing, e.g. '{firstF}'")
                Logger.LogLazy(Function() $"[PACK-BATCH] bundle DESCARTADO ENTERO {opF} 0x{fidF:X8}: {nF} archivo(s) requeridos ausentes (primero '{firstF}'). Sus sueltos NO se borran.")
                Continue For
            End If

            For Each rf In pending
                bundleExpected(bi) += 1
                allRefs.Add(rf)
                refToBundleIdx.Add(bi)
                bundleRefCounts(bi) += 1
            Next
        Next

        If missingSources.Count > 0 Then
            result.MissingSources.AddRange(missingSources)
            Logger.LogLazy(Function() $"[PACK-BATCH] {missingSources.Count} expected loose file(s) missing on disk before pack — first: '{missingSources(0)}'")
        End If

        ' No bake outputs to pack. If there is also nothing to remove, that's the "bake reported Success but
        ' produced no files" error. With exclusions we proceed to the removal-only pack below (delete-only save).
        If allRefs.Count = 0 AndAlso excludeSet.Count = 0 Then
            Dim msg = "No bake outputs found on disk for any of the requested bundles."
            If missingSources.Count > 0 Then msg &= $" First missing: '{missingSources(0)}'."
            result.ErrorMessage = msg
            Return result
        End If

        ' --- Step 2: unregister current archives once ----------------------------------------
        ' NO "frees pooled FileStreams" en el sentido que hacía falta: un reader ALQUILADO ya salió del
        ' pool y su FileStream vive todo el ExtractToMemory, así que esto vuelve con ese handle abierto.
        ' Lo que permite que el packager renombre/borre el .ba2 con lectores en vuelo es el FileShare.Delete
        ' de FilesDictionary_class.AbrirArchiveParaLectura. Este loop sigue siendo necesario por la otra
        ' razón: dejar de SERVIR entradas cuyos índices dejan de valer apenas se reescribe el archive.
        Dim modBaseName = Path.GetFileNameWithoutExtension(anchorPluginPath)
        Dim preSet = ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName)
        For Each archivePath In preSet.Archives
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch
            End Try
        Next

        ' --- Step 3: walk allRefs, parallel-compress in micro-batches, flush on cap ----------
        Dim totalEntries = allRefs.Count
        Dim entriesDone As Integer = 0

        Dim chunkEntries As New List(Of VirtualEntry)
        Dim chunkRefs As New List(Of LooseFileRef)
        Dim chunkCompBytes As Long = 0
        Dim committedRefs As New List(Of LooseFileRef)
        Dim cancelled As Boolean = False
        Dim packFailureMessage As String = ""

        Dim parOpts As New ParallelOptions With {
            .MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
            .CancellationToken = ct
        }

        Dim idx As Integer = 0
        While idx < allRefs.Count
            If ct.IsCancellationRequested Then
                cancelled = True
                Exit While
            End If

            Dim batchSize = Math.Min(MICRO_BATCH, allRefs.Count - idx)
            Dim micro(batchSize - 1) As VirtualEntry
            ' Per-ref compress errors are captured (instead of swallowed) so the user sees WHICH
            ' loose failed and why, rather than a silent "X NPCs dropped" with no breadcrumb.
            Dim microErrors(batchSize - 1) As String

            Try
                Parallel.For(0, batchSize, parOpts,
                    Sub(j)
                        Dim rf = allRefs(idx + j)
                        Try
                            micro(j) = If(rf.IsTexture,
                                          MakeTextureEntry(dataDir, rf.SourcePath, rf.EntryPath, game),
                                          MakeMaterialEntry(dataDir, rf.SourcePath, rf.EntryPath, game))
                        Catch ex As Exception
                            micro(j) = Nothing
                            microErrors(j) = $"{ex.GetType().Name}: {ex.Message}"
                        End Try
                    End Sub)
            Catch ex As OperationCanceledException
                cancelled = True
                Exit While
            End Try

            ' Surface every per-ref compress failure to the log + result so the post-pack summary
            ' can name them. Without this, a corrupt DDS or unreadable NIF disappeared into a
            ' silent count gap ("2 NPCs dropped"); now [PACK-BATCH-ERR] makes it debuggable.
            For j = 0 To batchSize - 1
                If microErrors(j) IsNot Nothing Then
                    Dim rf = allRefs(idx + j)
                    Dim msg = microErrors(j)
                    Dim src = rf.SourcePath
                    Logger.LogLazy(Function() $"[PACK-BATCH-ERR] compress failed for '{src}': {msg}")
                    result.MissingSources.Add(src)
                End If
            Next

            If ct.IsCancellationRequested Then
                cancelled = True
                Exit While
            End If

            ' Fold compressed entries into the accumulator, flushing on cap.
            For j = 0 To batchSize - 1
                Dim ve = micro(j)
                Dim rf = allRefs(idx + j)
                If ve Is Nothing Then Continue For

                Dim veCompSize As Long = If(ve.PreCompressedCompSize > 0UI,
                                            CLng(ve.PreCompressedCompSize),
                                            CLng(ve.PreCompressedDecompSize))

                If chunkEntries.Count > 0 AndAlso chunkCompBytes + veCompSize > MEMORY_CAP_BYTES Then
                    Try
                        FlushChunk(dataDir, modBaseName, game, ba2Version,
                                   chunkEntries, chunkRefs, chunkCompBytes,
                                   result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
                    Catch ex As Exception
                        packFailureMessage = $"BA2 packer failed mid-flush: {ex.GetType().Name}: {ex.Message}"
                        Exit For
                    End Try
                    entriesDone += chunkEntries.Count
                    chunkEntries = New List(Of VirtualEntry)
                    chunkRefs = New List(Of LooseFileRef)
                    chunkCompBytes = 0
                    If ct.IsCancellationRequested Then
                        cancelled = True
                        Exit For
                    End If
                End If

                chunkEntries.Add(ve)
                chunkRefs.Add(rf)
                chunkCompBytes += veCompSize
            Next

            If packFailureMessage <> "" Then Exit While

            idx += batchSize
            Report(progress, PackPhase.BuildingBundle,
                   $"Compressed {idx:N0}/{totalEntries:N0} (buffer {chunkCompBytes / (1024.0 * 1024.0):N0} MB / {MEMORY_CAP_BYTES / (1024.0 * 1024.0):N0} MB)",
                   idx, totalEntries)
        End While

        ' Final flush of whatever survived (carries the exclusions so both buckets get stripped even if the
        ' surviving entries only touch one).
        If packFailureMessage = "" AndAlso Not cancelled AndAlso chunkEntries.Count > 0 Then
            Try
                FlushChunk(dataDir, modBaseName, game, ba2Version,
                           chunkEntries, chunkRefs, chunkCompBytes,
                           result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
                entriesDone += chunkEntries.Count
            Catch ex As Exception
                packFailureMessage = $"BA2 packer failed on final flush: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        ' Delete-only (or all-excluded) case: no flush ran the exclusions yet → do one removal-only Pack so the
        ' removed NPCs' bake entries are stripped from the target archive set while every other entry is preserved.
        If packFailureMessage = "" AndAlso Not cancelled AndAlso excludeSet.Count > 0 AndAlso result.FlushesCommitted = 0 Then
            Try
                FlushChunk(dataDir, modBaseName, game, ba2Version,
                           New List(Of VirtualEntry)(), New List(Of LooseFileRef)(), 0L,
                           result, committedRefs, progress, entriesDone, totalEntries, ct, excludeSet)
            Catch ex As Exception
                packFailureMessage = $"BA2 packer failed on removal flush: {ex.GetType().Name}: {ex.Message}"
            End Try
        End If

        ' --- Step 4: re-mount EVERY archive in the set (skipped ones too — we unregistered them) -
        Dim postSet = ArchivePackager.DiscoverArchiveSet(dataDir, modBaseName)
        For Each archivePath In postSet.Archives
            Try
                FilesDictionary_class.UnregisterArchive(archivePath)
            Catch
            End Try
            FilesDictionary_class.RegisterArchive(archivePath)
        Next

        ' --- Step 5: delete loose for every committed ref ------------------------------------
        ' DebugSandbox refs (the _2.xxx files) are deleted too — per user 2026-05-26: if the
        ' bundle landed in a BA2 (committed), the disk loose is redundant regardless of which
        ' suffix it carried. Old behavior preserved them for inspection; new behavior trusts the
        ' BA2 as the source of truth and lets the user inspect via Unpack if needed.
        If committedRefs.Count > 0 Then
            Report(progress, PackPhase.DeletingLoose, "Removing loose files…", 0, committedRefs.Count)
            Dim deletedAt As Integer = 0
            Dim affectedDirs As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each rf In committedRefs
                deletedAt += 1
                Try
                    If File.Exists(rf.SourcePath) Then
                        File.Delete(rf.SourcePath)
                        result.DeletedLoose.Add(rf.SourcePath)
                    End If
                    Dim relUnderData = Path.GetRelativePath(dataDir, rf.SourcePath).Correct_Path_Separator
                    FilesDictionary_class.RemoveDictionaryEntry(relUnderData)
                    Dim d = Path.GetDirectoryName(rf.SourcePath)
                    If Not String.IsNullOrEmpty(d) Then affectedDirs.Add(d)
                Catch ex As Exception
                    Dim srcL = rf.SourcePath
                    Dim msgL = ex.Message
                    Dim typeL = ex.GetType().Name
                    Logger.LogLazy(Function() $"[PACK-BATCH-DEL] delete failed '{srcL}': {typeL}: {msgL}")
                End Try
                If (deletedAt And &H1F) = 0 OrElse deletedAt = committedRefs.Count Then
                    Report(progress, PackPhase.DeletingLoose,
                           $"Removed {deletedAt}/{committedRefs.Count}", deletedAt, committedRefs.Count)
                End If
            Next
            For Each leaf In affectedDirs
                RemoveEmptyDirsUpTo(leaf, dataDir, result.RemovedDirs)
            Next
        End If

        ' --- Step 6: count fully-committed bundles for the summary -------------------------------
        ' Single pass over allRefs accumulating per-bundle committed-hit counts (was O(bundles×refs):
        ' for each bundle it re-scanned every ref). refToBundleIdx is parallel to allRefs.
        Dim committedSet As New HashSet(Of String)(committedRefs.Select(Function(r) r.SourcePath),
                                                   StringComparer.OrdinalIgnoreCase)
        Dim bundleHitCounts(bundles.Count - 1) As Integer
        For ri = 0 To allRefs.Count - 1
            If committedSet.Contains(allRefs(ri).SourcePath) Then
                bundleHitCounts(refToBundleIdx(ri)) += 1
            End If
        Next
        For bi = 0 To bundles.Count - 1
            ' A bundle is fully committed when every file the game's layout calls for (4 on FO4,
            ' 2 on SSE — see FaceGenFileSpecs) was authored AND committed.
            If bundleExpected(bi) > 0 AndAlso bundleRefCounts(bi) = bundleExpected(bi) AndAlso
               bundleHitCounts(bi) = bundleExpected(bi) Then
                result.BundlesCommitted += 1
            End If
        Next

        If packFailureMessage <> "" Then
            result.ErrorMessage = packFailureMessage
            Report(progress, PackPhase.Done, "Pack failed (some bundles may have committed).", entriesDone, totalEntries)
            Return result
        End If

        If cancelled Then
            result.ErrorMessage = "Cancelled before all bundles committed."
            Report(progress, PackPhase.Done, "Cancelled.", entriesDone, totalEntries)
            Return result
        End If

        result.Success = True
        Report(progress, PackPhase.Done,
               $"Done. {result.BundlesCommitted}/{bundles.Count} bundles committed in {result.FlushesCommitted} flush(es).",
               totalEntries, totalEntries)
        Return result
    End Function

    ''' <summary>Hand the current accumulator to ArchivePackager.Pack as a single batch.
    ''' Mirror of WM_PackUnpack.FlushChunk: single Pack call, free PreCompressedBytes from the
    ''' flushed entries so the next chunk has clean memory, append committed refs to the global
    ''' list so the caller can delete their loose sources after every flush has run.</summary>
    Private Sub FlushChunk(dataDir As String,
                           modBaseName As String,
                           game As Config_App.Game_Enum,
                           ba2Version As UInteger,
                           chunkEntries As List(Of VirtualEntry),
                           chunkRefs As List(Of LooseFileRef),
                           chunkCompBytes As Long,
                           result As PackResult,
                           committedRefs As List(Of LooseFileRef),
                           progress As Action(Of PackProgress),
                           entriesDone As Integer,
                           totalEntries As Integer,
                           ct As CancellationToken,
                           Optional excludeSet As HashSet(Of String) = Nothing)
        ' Proceed with 0 entries only when there are exclusions to apply (delete-only removal pass).
        Dim hasExclusions = excludeSet IsNot Nothing AndAlso excludeSet.Count > 0
        If chunkEntries.Count = 0 AndAlso Not hasExclusions Then Return
        If ct.IsCancellationRequested Then Return

        Report(progress, PackPhase.WritingArchive,
               $"Writing BA2 (flush {result.FlushesCommitted + 1}: {chunkEntries.Count:N0} entries, {chunkCompBytes / (1024.0 * 1024.0):N0} MB)…",
               entriesDone, totalEntries)

        Dim req As New PackagerRequest With {
            .Game = MapGame(game),
            .Ba2Version = ba2Version,
            .ModBaseName = modBaseName,
            .OutputDir = dataDir,
            .Entries = chunkEntries,
            .BundleAlreadyCompressed = True,
            .MaxArchiveBytes = MAX_ARCHIVE_BYTES,
            .Overflow = ArchiveOverflowPolicy.ThrowOnExceed,
            .SingleAnchorOnly = True,
            .ExcludePaths = If(hasExclusions, excludeSet, Nothing),
            .PluginWriter = Sub(p As String, g As GameKind)
                                ' Anchor plugin already exists (Save ESP wrote it before we got
                                ' called). This callback would only fire if the packer split into
                                ' a numbered slot — but we set Overflow=ThrowOnExceed so we never
                                ' reach split. Emit a dummy as a defensive fallback in case the
                                ' policy changes; mirrors WM behavior so existing tests stay valid.
                                PluginWriter.WriteLightMasterDummy(p, MapGameBack(g), PluginWriter.NPC_MANAGER_AUTHOR_CNAM)
                            End Sub
        }

        ' [AUDIT-BA2] valida que el writer suelta cada payload comprimido al escribirlo. Se mide el
        ' pico de heap ADMINISTRADO del pack: antes los N payloads vivian a la vez hasta terminar el
        ' archivo, ahora sólo hasta que cada uno se escribe.
        Dim heapAntes As Long = 0
        If Logger.Enabled Then heapAntes = GC.GetTotalMemory(False)
        Dim chunkResult = ArchivePackager.Pack(req)
        If Logger.Enabled Then
            Dim hA = heapAntes, hD = GC.GetTotalMemory(False)
            Dim mi = GC.GetGCMemoryInfo()
            Dim entradas = req.Entries.Count
            Logger.LogLazy(Function() $"[AUDIT-BA2] pack de {entradas} entradas: heap {hA \ (1024 * 1024)} MB -> {hD \ (1024 * 1024)} MB, heapTotalDelGC={mi.HeapSizeBytes \ (1024 * 1024)} MB")
        End If

        result.WrittenArchives.AddRange(chunkResult.Archives)
        result.SkippedArchives.AddRange(chunkResult.Skipped)
        result.FlushesCommitted += 1
        committedRefs.AddRange(chunkRefs)

        ' Free compressed bytes so the next chunk starts with clean memory. WM does the same.
        For Each ve In chunkEntries
            ve.Data = Nothing
            ve.PreCompressedBytes = Nothing
        Next
    End Sub

    ''' <summary>Delete <paramref name="leafDir"/> and each empty ancestor, walking UP until
    ''' (and excluding) <paramref name="stopDir"/>. Stops at the first non-empty ancestor. Never
    ''' deletes stopDir itself nor anything outside it (StartsWith guard). Best-effort: IO failure
    ''' aborts the walk silently — a leftover empty dir is harmless.</summary>
    Private Sub RemoveEmptyDirsUpTo(leafDir As String, stopDir As String, removed As List(Of String))
        Try
            Dim sep = Path.DirectorySeparatorChar
            Dim stopFull = Path.GetFullPath(stopDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            Dim current = Path.GetFullPath(leafDir).TrimEnd(sep, Path.AltDirectorySeparatorChar)
            While True
                If String.Equals(current, stopFull, StringComparison.OrdinalIgnoreCase) Then Exit While
                If Not current.StartsWith(stopFull & sep, StringComparison.OrdinalIgnoreCase) Then Exit While

                If Directory.Exists(current) Then
                    Dim hasAny As Boolean = False
                    For Each e In Directory.EnumerateFileSystemEntries(current)
                        hasAny = True
                        Exit For
                    Next
                    If hasAny Then Exit While
                    Directory.Delete(current, recursive:=False)
                    If removed IsNot Nothing Then removed.Add(current)
                End If
                Dim parent = Path.GetDirectoryName(current)
                If String.IsNullOrEmpty(parent) Then Exit While
                current = parent
            End While
        Catch
        End Try
    End Sub

    ' ============================================================================
    ' Entry builders — same shape as the previous per-NPC version. sourcePath may
    ' carry the _2 suffix (DebugSandbox); entryPath is always the canonical name
    ' the BA2 entry takes inside the archive, so a release-built archive is byte-
    ' compatible regardless of whether the bake ran in debug mode.
    ' ============================================================================





    Private Sub Report(progress As Action(Of PackProgress),
                       phase As PackPhase, detail As String,
                       current As Integer, max As Integer)
        If progress Is Nothing Then Return
        progress(New PackProgress With {
            .Phase = phase,
            .Detail = detail,
            .Current = current,
            .Max = max
        })
    End Sub

End Module
