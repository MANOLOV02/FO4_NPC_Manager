Imports System.IO
Imports System.Linq
Imports FO4_Base_Library

''' <summary>Headless "bake every NPC in the load order to loose FaceGen" — the engine behind the
''' <c>--bake-all</c> flag (console) and its <c>--windowed</c> variant (progress dialog).
'''
''' <para><b>What it is.</b> The exact equivalent of selecting every NPC in the tree and pressing
''' "Build CharGen (loose)": it reuses <see cref="FaceGenBuilder.BuildCharGen"/> with
''' <c>willBePacked:=False</c>, the render's material resolver, the sidecar-hydrated presets and the
''' LM skin-template resolver — the same arguments MainForm.BuildCharGenForSelectionAsync passes
''' (MainForm.vb ~:9842). No ESP is written, nothing is packed, no loose file is deleted: this ONLY
''' bakes (NIF + face textures) exactly like the GUI button does.</para>
'''
''' <para><b>Why it needs no OpenGL.</b> The texture bake is GL-bound only when
''' <see cref="FaceGenBuilder.WriteGPUSandboxOutput"/> is on (it gates <c>needGl</c> in
''' FaceGenBuilder.BakeFaceTextures); with it off the whole composite runs through
''' FaceTintCpuCompositor on the CPU. We force it off below, so this path never touches a GL context
''' and never needs a window — and, because DebugMode stays off too, it writes the CANONICAL
''' <c>&lt;id&gt;.NIF</c> / <c>_d|_msn|_s.dds</c>, not the <c>_2</c> sandbox names.</para>
'''
''' <para><b>Bootstrap.</b> <see cref="Run"/> replays the preflight's own order (config → encoding →
''' plugins → archives → sidecars), because that order is load-bearing: plugin text encoding must be
''' configured BEFORE any plugin is parsed (xEdit's "configure → load → edit"). Every step that the
''' preflight validates interactively is validated here as a hard failure instead.</para></summary>
Friend Module BakeAllRunner

    ''' <summary>Exit codes. Distinguish "the run never started" from "the run finished but some NPCs
    ''' failed", so a script can tell a broken config apart from a broken NPC.</summary>
    Friend Const ExitOk As Integer = 0
    Friend Const ExitFatal As Integer = 1          ' config/load could not be resolved — nothing was baked
    Friend Const ExitSomeFailed As Integer = 2     ' ran to completion, but ≥1 NPC failed to bake
    Friend Const ExitCancelled As Integer = 3      ' user cancelled (windowed mode)

    ''' <summary>Per-NPC outcome, mirroring FaceGenBuilder.BuildResult's three states.</summary>
    Friend Enum Outcome
        Baked
        Skipped
        Failed
    End Enum

    ''' <summary>Options parsed off the command line. Everything that shapes the OUTPUT comes from the
    ''' persisted config (config.json + npc_config.json) — resolutions, DDS codecs, FaceTint convention +
    ''' sort order, mouth fix, eyebrow LUT, ghoul head-rear fix, Generate-TGA. Same law as the GUI.
    '''
    ''' <para>The one exception is <see cref="ExecutablePath"/>. The game selector and the exe/Data path
    ''' are a SINGLE setting in this app (Config_App.FO4ExePath → FO4EDataPath), which is why there is no
    ''' bare "--game" flag: it could only ever produce a game/exe mismatch. Pointing at an executable moves
    ''' both at once — the Data folder to load AND the game whose record layout is used.</para></summary>
    Friend Class Options
        ''' <summary>True = console output, no window. False (default) = the progress window.</summary>
        Public Windowless As Boolean = False
        ''' <summary>Full path to a game executable (Fallout4.exe / SkyrimSE.exe). When set it overrides the
        ''' persisted exe — and with it the Data folder AND the game — for this run only (never saved to
        ''' config.json). Empty = use the config as-is.</summary>
        Public ExecutablePath As String = ""
    End Class

    ''' <summary>Infer the game from an executable's file name — the same signal the preflight uses to warn
    ''' about a game/exe mismatch. Nothing when the name is neither game's: the caller must then REFUSE
    ''' rather than guess, because choosing wrong silently mis-parses every NPC_/RACE record.</summary>
    Private Function InferGameFromExe(exePath As String) As Config_App.Game_Enum?
        Dim name = Path.GetFileNameWithoutExtension(If(exePath, "")).ToLowerInvariant()
        If name.Contains("skyrim") OrElse name.Contains("sse") Then Return Config_App.Game_Enum.Skyrim
        If name.Contains("fallout") Then Return Config_App.Game_Enum.Fallout4
        Return Nothing
    End Function

    ''' <summary>Sinks the two front-ends provide. <paramref name="log"/> gets every human-readable
    ''' line; <paramref name="progress"/> gets (done, total, currentLabel) — total is 0 while the
    ''' bootstrap runs (indeterminate). <paramref name="isCancelled"/> is polled between NPCs.</summary>
    Friend Function Run(opt As Options,
                        log As Action(Of String),
                        progress As Action(Of Integer, Integer, String),
                        isCancelled As Func(Of Boolean)) As Integer
        ' Null sinks are legal (the console front-end passes no progress/cancel channel): swap in no-ops
        ' so the body below never has to null-check.
        If log Is Nothing Then
            log = Sub(s As String)
                  End Sub
        End If
        If progress Is Nothing Then
            progress = Sub(done As Integer, total As Integer, label As String)
                       End Sub
        End If
        If isCancelled Is Nothing Then isCancelled = Function() False

        Try
            ' ---------------------------------------------------------------------------------
            ' 1. Config. Persisted — the CLI never invents values, it only reports what it found.
            ' ---------------------------------------------------------------------------------
            Config_App.LoadConfig()
            NPC_Config.LoadConfig()

            ' --executable: point the whole run at another install. It rewrites Config_App.Current.FO4ExePath
            ' IN MEMORY ONLY (no SaveConfig), which moves FO4EDataPath with it, and re-derives the game from
            ' the exe name — the two are one setting. Refuse an exe we can't classify: guessing the game wrong
            ' mis-parses every record silently.
            If opt IsNot Nothing AndAlso Not String.IsNullOrWhiteSpace(opt.ExecutablePath) Then
                Dim exeOverride = opt.ExecutablePath.Trim().Trim(""""c)
                If Not File.Exists(exeOverride) Then
                    log($"FATAL: --executable '{exeOverride}' does not exist.")
                    Return ExitFatal
                End If
                Dim inferred = InferGameFromExe(exeOverride)
                If Not inferred.HasValue Then
                    log($"FATAL: cannot tell which game '{Path.GetFileName(exeOverride)}' is.")
                    log("       --executable expects the game exe (e.g. Fallout4.exe or SkyrimSE.exe).")
                    Return ExitFatal
                End If
                Config_App.Current.FO4ExePath = exeOverride
                Config_App.Current.Game = inferred.Value
                log($"--executable override: {exeOverride}  →  game = {inferred.Value} (this run only, config.json untouched)")
            End If

            Dim game = Config_App.Current.Game

            ' NPC render/bake relies on per-segment occlusion (head-part hiding); the shared toggle
            ' defaults True for WM inspection, so force it off exactly like Program.Main does.
            Config_App.Current.Setting_DrawHiddenSegments = False

            ' The bake must be a REAL bake: canonical file names, pure CPU, no GL context. Both flags
            ' derive from Logger.Enabled by default, which a Debug build turns on — pin the GL one off
            ' explicitly so a Debug build doesn't try to MakeCurrent a context that doesn't exist.
            FaceGenBuilder.WriteGPUSandboxOutput = False

            log($"Game:        {game}")

            If Not Config_App.Check_FOFolder() Then
                log($"FATAL: the configured game executable is not valid: '{Config_App.Current.FO4ExePath}'")
                log("       Open the app once and pick a valid game .exe in the preflight, then retry.")
                Return ExitFatal
            End If
            Dim dataPath = Config_App.Current.FO4EDataPath
            If String.IsNullOrEmpty(dataPath) OrElse Not Directory.Exists(dataPath) Then
                log($"FATAL: Data folder does not exist: '{dataPath}'")
                Return ExitFatal
            End If
            log($"Exe:         {Config_App.Current.FO4ExePath}")
            log($"Data:        {dataPath}")

            ' Guard the silent-corruption trap the preflight guards interactively: NPC_/RACE byte
            ' layouts differ between games, so a Skyrim Data dir parsed as Fallout 4 mis-reads records.
            ' Headless has nobody to ask — refuse instead of baking garbage.
            Dim exeLower = Path.GetFileName(Config_App.Current.FO4ExePath).ToLowerInvariant()
            Dim wantSkyrim = (game = Config_App.Game_Enum.Skyrim)
            Dim looksSkyrim = exeLower.Contains("skyrim") OrElse exeLower.Contains("sse")
            Dim looksFo4 = exeLower.Contains("fallout4")
            If (wantSkyrim AndAlso looksFo4) OrElse (Not wantSkyrim AndAlso looksSkyrim) Then
                log($"FATAL: game/executable mismatch — game is {game} but the exe is '{Path.GetFileName(Config_App.Current.FO4ExePath)}'.")
                log("       This corrupts record parsing. Open the app, fix the game selector + exe in the")
                log("       preflight (they are one setting), and retry.")
                Return ExitFatal
            End If

            ' ---------------------------------------------------------------------------------
            ' 2. Plugin text encoding — BEFORE any plugin is parsed (xEdit order). Includes the
            '    OverridePluginEncoding.ini escape hatch, which the GUI applies and --bake-geom
            '    historically forgot.
            ' ---------------------------------------------------------------------------------
            PluginEncodingSettings.InitializeForGame(game)
            PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())
            PluginEncodingSettings.ApplyOverrideIni(AppDomain.CurrentDomain.BaseDirectory)

            ' ---------------------------------------------------------------------------------
            ' 3. Plugins — the ACTIVE load order (Plugins.txt), i.e. what the game itself would load.
            ' ---------------------------------------------------------------------------------
            Dim loadList = PluginManager.ReadActiveLoadOrder()
            If loadList Is Nothing OrElse loadList.Count = 0 Then
                log("FATAL: the active load order is empty (could not read Plugins.txt / loadorder).")
                Return ExitFatal
            End If
            log($"Load order:  {loadList.Count} active plugin(s)")

            progress(0, 0, "Parsing plugins…")
            ' The parse is PARALLEL: reports from different plugins interleave, so "log whenever the
            ' name changes" would print the same few .esm names hundreds of times. Log each plugin the
            ' FIRST time it's seen instead — one line per plugin, in the order they actually start.
            Dim loggedPlugins As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim pluginProgress As New InlineProgress(Of PluginLoadProgress)(
                Sub(p)
                    If String.IsNullOrEmpty(p.CurrentName) Then Return
                    progress(0, 0, $"Parsing plugins — {p.CurrentName} ({p.FilesDone}/{p.FilesTotal})")
                    SyncLock loggedPlugins
                        If Not loggedPlugins.Add(p.CurrentName) Then Return
                    End SyncLock
                    log($"  [ESP] {p.CurrentName}")
                End Sub)

            Dim pm As New PluginManager()
            log("Parsing plugins…")
            pm.LoadAllPlugins(dataPath, loadList, pluginProgress)
            log($"  → {pm.Plugins.Count} plugin(s) parsed")
            ' ReadActiveLoadOrder = implicit masters + EVERY entry of Fallout4.ccc/Skyrim.ccc + the actives
            ' from Plugins.txt. The .ccc lists all Creation Club content that EXISTS, not what is installed,
            ' so on a normal setup most of it has no file in Data\ and LoadAllPlugins skips it. That is
            ' expected, not an error — report it as information so the count doesn't read as data loss.
            If pm.Plugins.Count < loadList.Count Then
                log($"  ({loadList.Count - pm.Plugins.Count} load-order entries have no file in Data\ — normally Creation Club content that isn't installed — skipped)")
            End If

            ' ---------------------------------------------------------------------------------
            ' 4. Archives (BA2/BSA) + loose files. The bake reads every source texture and TRI/NIF
            '    through this dictionary, so it must be mounted before the first BuildCharGen.
            ' ---------------------------------------------------------------------------------
            Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
            Directory.CreateDirectory(cacheDir)
            FilesDictionary_class.CacheDirectory = cacheDir
            log("Mounting archives (BA2/BSA + loose)…")
            progress(0, 0, "Mounting archives…")
            ' The dictionary reports once PER indexed file — thousands of loose files — and reports from its
            ' parallel workers arrive out of order. Mirror every report to the status line (cheap), but only
            ' LOG on each new 10% high-water mark: without the monotonic guard the deciles go backwards
            ' (…10%, 0%, 10%…) and without the throttle the log is a wall of file names.
            Dim highestDecile As Integer = -1
            Dim decileLock As New Object()
            Dim archiveProgress As New InlineProgress(Of (Stepn As String, Value As Integer, Max As Integer))(
                Sub(info)
                    If info.Max <= 0 Then Return
                    progress(0, 0, $"Mounting archives — {info.Value}/{info.Max}")
                    Dim decile = CInt(Math.Floor(info.Value * 10.0 / info.Max))
                    SyncLock decileLock
                        If decile <= highestDecile Then Return
                        highestDecile = decile
                    End SyncLock
                    log($"  [ARC] {info.Value}/{info.Max} files indexed ({decile * 10}%)")
                End Sub)
            FilesDictionary_class.Fill_DictionaryAsync(dataPath, archiveProgress).GetAwaiter().GetResult()
            log("  → archives mounted")

            ' ---------------------------------------------------------------------------------
            ' 5. Overlay state — identical to what MainForm starts with: the .bssliders sidecars of
            '    the load order, hydrated into the applied-presets dict (BodyMorphs, LM skin template,
            '    overlays, and the SSE RaceMenu carriers).
            ' ---------------------------------------------------------------------------------
            Dim sidecars = Preflight_Form.ScanSidecarsForPlugins(dataPath, loadList)
            Dim appliedPresets As New Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)()
            BssliderSidecar.HydratePresets(sidecars, pm, appliedPresets)
            log($"Sidecars:    {If(sidecars Is Nothing, 0, sidecars.Count)} .bssliders file(s) → {appliedPresets.Count} NPC overlay(s)")

            Dim lmTemplates = LmSkinTemplateLoader.BuildCache(dataPath, pm)
            log($"LM skins:    {lmTemplates.Count} skin template(s)")

            ' ---------------------------------------------------------------------------------
            ' 6. Resolvers — the SAME objects the render/GUI bake wires up (NpcRenderContext +
            '    NpcMaterialResolver over the preset overlay), so per-shape materials, texture sets,
            '    hair palettes and skin overrides resolve exactly as they do in the app.
            ' ---------------------------------------------------------------------------------
            Dim ctx As New NpcRenderContext(pm)
            Dim resolveLmSkin As NpcRecordOverlay.ResolveLmSkinTemplateDelegate =
                Function(templateId As String) As LmSkinTemplate
                    If String.IsNullOrEmpty(templateId) Then Return Nothing
                    Return lmTemplates.FirstOrDefault(Function(t) String.Equals(t.Id, templateId, StringComparison.Ordinal))
                End Function
            Dim overlayResolver As Func(Of NPC_Data, UInteger, NPC_Data) =
                Function(raw As NPC_Data, fid As UInteger) NpcRecordOverlay.ApplyPresetOverlayToNpcData(
                    raw, fid, appliedPresets, pm, resolveLmSkin, AddressOf ctx.ParseRaceCached)
            Dim materialResolver As New NpcMaterialResolver(ctx, overlayResolver, appliedPresets)

            ' ---------------------------------------------------------------------------------
            ' 7. The NPC universe — every NPC_ winner in the load order. No category filter: the two
            '    skip gates inside BuildCharGen (race without FaceGen / no FaceGen head parts) decide
            '    what is bakeable, and each skip is reported with its reason.
            ' ---------------------------------------------------------------------------------
            Dim targets As New List(Of (Fid As UInteger, Name As String))
            For Each rec In pm.GetNPCs()
                Try
                    Dim npc = RecordParsers.ParseNPC(rec, If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown"), pm)
                    If npc Is Nothing Then Continue For
                    targets.Add((npc.FormID, npc.ToString()))
                Catch
                    ' Unparseable record — nothing to bake; the GUI's ParseAllNPCs swallows these too.
                End Try
            Next
            If targets.Count = 0 Then
                log("FATAL: no NPC_ records found in the load order.")
                Return ExitFatal
            End If
            log($"NPCs:        {targets.Count} NPC_ record(s) to bake")
            log("")

            ' ---------------------------------------------------------------------------------
            ' 8. The bake loop. Sequential, exactly like the GUI batch: the CPU compositor's batch
            '    decode cache and FaceGenBuilder's shared-neutral scratch both assume one bake at a
            '    time. BeginBatchDecodeCache makes every source DDS decode ONCE for the whole run.
            ' ---------------------------------------------------------------------------------
            Dim baked As Integer = 0, skipped As Integer = 0, failed As Integer = 0
            Dim failures As New List(Of String)
            Dim sw = Diagnostics.Stopwatch.StartNew()
            Dim cancelled As Boolean = False

            FaceTintCpuCompositor.BeginBatchDecodeCache()
            Try
                For i = 0 To targets.Count - 1
                    If isCancelled() Then
                        cancelled = True
                        Exit For
                    End If
                    Dim t = targets(i)
                    Dim pct = CInt(Math.Floor((i + 1) * 100.0 / targets.Count))
                    progress(i + 1, targets.Count, $"{i + 1}/{targets.Count} — {t.Name}")

                    Dim head = $"[{i + 1,5}/{targets.Count} {pct,3}%] 0x{t.Fid:X8} {t.Name}"
                    Try
                        Dim r = FaceGenBuilder.BuildCharGen(t.Fid, pm, appliedPresets,
                                                            host:=Nothing,
                                                            applyMaterialOverrides:=AddressOf materialResolver.ApplyShapeMaterialOverrides,
                                                            willBePacked:=False,
                                                            lmSkinTemplateResolver:=resolveLmSkin)
                        If r Is Nothing Then
                            failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: BuildCharGen returned nothing")
                            log($"{head} — FAIL: BuildCharGen returned nothing")
                        ElseIf r.Skipped Then
                            skipped += 1
                            log($"{head} — skip: {r.Summary}")
                        ElseIf r.Success Then
                            baked += 1
                            log($"{head} — baked: {r.ShapesKept} shape(s) → {r.OutputPath}")
                        Else
                            failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {r.Summary}")
                            log($"{head} — FAIL: {r.Summary}")
                        End If
                    Catch ex As Exception
                        failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {ex.GetType().Name}: {ex.Message}")
                        log($"{head} — FAIL: {ex.GetType().Name}: {ex.Message}")
                    End Try
                Next
            Finally
                FaceTintCpuCompositor.EndBatchDecodeCache()
            End Try
            sw.Stop()

            ' ---------------------------------------------------------------------------------
            ' 9. Summary.
            ' ---------------------------------------------------------------------------------
            Dim processed = baked + skipped + failed
            log("")
            If cancelled Then log($"CANCELLED — {targets.Count - processed} NPC(s) not processed.")
            log($"Done in {sw.Elapsed:hh\:mm\:ss} — {baked} baked / {skipped} skipped / {failed} failed (of {targets.Count}).")
            If failures.Count > 0 Then
                log("")
                log($"Failures ({failures.Count}):")
                For Each f In failures
                    log($"  {f}")
                Next
            End If
            progress(processed, targets.Count, "Done.")

            If cancelled Then Return ExitCancelled
            If failed > 0 Then Return ExitSomeFailed
            Return ExitOk

        Catch ex As Exception
            log("FATAL: " & ex.ToString())
            Return ExitFatal
        End Try
    End Function

    ''' <summary>IProgress that invokes the handler ON THE REPORTING THREAD. The BCL's Progress(Of T)
    ''' posts to the captured SynchronizationContext (none here → thread pool), which would interleave
    ''' our console lines out of order. This one keeps the log strictly sequential.</summary>
    Private NotInheritable Class InlineProgress(Of T)
        Implements IProgress(Of T)

        Private ReadOnly _handler As Action(Of T)

        Public Sub New(handler As Action(Of T))
            _handler = handler
        End Sub

        Public Sub Report(value As T) Implements IProgress(Of T).Report
            _handler?.Invoke(value)
        End Sub
    End Class

End Module
