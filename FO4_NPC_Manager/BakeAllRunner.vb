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
        ''' <summary>Plugin file name (e.g. "MyFollower.esp"). When set, only NPCs whose WINNING record comes
        ''' from that plugin are baked; every other NPC is left alone. Matching on the winner means the
        ''' plugin's own NPCs *and* the vanilla NPCs it overrides both count — which is what "bake this mod"
        ''' means. Empty = every NPC in the load order. The whole load order is still PARSED (a follower's
        ''' race/head parts/armor usually live in the masters), only the bake list is narrowed.</summary>
        Public EspTarget As String = ""
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
            ' El gate va PEGADO al LoadConfig: EngineSkinWeightNormalization.Enabled arranca en False y solo lo
            ' enciende ApplyEngineSkinWeightNormalizationGate. Sin esta linea el bake-all corria SIEMPRE con la
            ' ley apagada aunque el usuario la tuviera guardada en True (el valor persistia y se mostraba en la
            ' UI, pero no se APLICABA en este camino). Se re-aplica mas abajo si --executable cambia el juego.
            NPC_Config.ApplyEngineSkinWeightNormalizationGate(Config_App.Current.Game)
            NPC_Config.ApplyGlDecodeSetting()

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

            ' Estado del CONTEXTO GL opcional (FGBAKE_GPU_PARITY=1). Declarado acá arriba porque se crea antes
            ' del loop y se destruye en el Finally que lo cierra.
            Dim gpuParity As Boolean = False
            Dim glForm As System.Windows.Forms.Form = Nothing
            Dim glCtl As PreviewControl = Nothing
            Dim glHost As NpcRenderHost = Nothing

            ' Re-aplicar el gate con el juego DEFINITIVO: --executable puede haber cambiado Current.Game arriba,
            ' y la ley esta verificada por RE solo en FO4. Sin esta segunda llamada, un override a SkyrimSE.exe
            ' dejaria encendida una ley que en Skyrim no esta verificada.
            NPC_Config.ApplyEngineSkinWeightNormalizationGate(game)
            NPC_Config.ApplyGlDecodeSetting()

            ' NPC render/bake relies on per-segment occlusion (head-part hiding); the shared toggle
            ' defaults True for WM inspection, so force it off exactly like Program.Main does.
            Config_App.Current.Setting_DrawHiddenSegments = False

            ' The bake must be a REAL bake: canonical file names, pure CPU, no GL context. Both flags
            ' derive from Logger.Enabled by default, which a Debug build turns on — pin the GL one off
            ' explicitly so a Debug build doesn't try to MakeCurrent a context that doesn't exist.
            '
            ' ⭐⭐ EXCEPCION OPT-IN: FGBAKE_GPU_PARITY=1 levanta un contexto GL propio y corre TAMBIEN el
            ' compositor GPU, para poder MEDIR la paridad CPU-vs-GPU (ver mas abajo, "CONTEXTO GL").
            ' ⛔ POR QUE HACE FALTA: con needGl=False el compositor GL —el que usa el RENDER— no se ejecuta
            ' NI UNA VEZ en todo el barrido. Toda la byte-parity historica del bake es CIEGA a el, y por eso
            ' un cambio en el camino compartido pudo romper el render sin que ninguna prueba lo detectara.
            gpuParity = (If(Environment.GetEnvironmentVariable("FGBAKE_GPU_PARITY"), "").Trim() = "1")
            FaceGenBuilder.WriteGPUSandboxOutput = gpuParity

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

            ' The PERSISTED plugin selection (npc_config.json PreflightSelection_FO4/_SSE) wins over the
            ' actives, exactly as it does in the GUI (Preflight_Form.RefreshPluginList ~:199). Headless used
            ' to ignore it entirely and always run Plugins.txt, so "bake the same set the app is showing me"
            ' was impossible without editing Plugins.txt — which is the game's file, not ours to rewrite.
            ' Same three rules as the dialog: filter the stored list against what is actually in Data\ (a
            ' plugin saved last run and since uninstalled just drops out), keep the dialog's row ORDER
            ' (actives in load order first, then the rest alphabetically), and fall back to the actives when
            ' nothing survives.
            Dim effectiveLoadList = loadList
            Try
                Dim presentFiles = FilesDictionary_class.
                    EnumerateFilesWithSymlinkSupport(dataPath, "*.esp;*.esm;*.esl", False).
                    Select(Function(p) Path.GetFileName(p)).
                    ToList()
                Dim presentSet As New HashSet(Of String)(presentFiles, StringComparer.OrdinalIgnoreCase)
                Dim saved = NPC_Config.GetPreflightSelection(game).
                    Where(Function(n) presentSet.Contains(n)).
                    ToList()
                If saved.Count > 0 Then
                    Dim savedSet As New HashSet(Of String)(saved, StringComparer.OrdinalIgnoreCase)
                    ' Mirror of Preflight_Form._allRows: actives in engine order, then inactives A-Z.
                    Dim rows As New List(Of String)
                    Dim rendered As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                    For Each p In loadList
                        If presentSet.Contains(p) AndAlso rendered.Add(p) Then rows.Add(p)
                    Next
                    For Each p In presentFiles.Where(Function(x) Not rendered.Contains(x)).
                                               OrderBy(Function(x) x, StringComparer.OrdinalIgnoreCase)
                        If rendered.Add(p) Then rows.Add(p)
                    Next
                    effectiveLoadList = rows.Where(Function(p) savedSet.Contains(p)).ToList()
                    log($"Selection:   npc_config.json PreflightSelection_{If(game = Config_App.Game_Enum.Skyrim, "SSE", "FO4")} " &
                        $"-> {effectiveLoadList.Count} plugin(s) (of {loadList.Count} active); Plugins.txt NOT modified")
                Else
                    log("Selection:   none stored in npc_config.json — using the active load order")
                End If
            Catch ex As Exception
                ' Never degrade to a DIFFERENT corpus in silence: say so and keep the actives.
                log($"WARNING: could not apply the stored plugin selection ({ex.GetType().Name}: {ex.Message}) — using the active load order")
                effectiveLoadList = loadList
            End Try
            If effectiveLoadList Is Nothing OrElse effectiveLoadList.Count = 0 Then
                log("FATAL: the effective plugin list is empty.")
                Return ExitFatal
            End If

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
            pm.LoadAllPlugins(dataPath, effectiveLoadList, pluginProgress)
            log($"  → {pm.Plugins.Count} plugin(s) parsed")
            ' ReadActiveLoadOrder = implicit masters + EVERY entry of Fallout4.ccc/Skyrim.ccc + the actives
            ' from Plugins.txt. The .ccc lists all Creation Club content that EXISTS, not what is installed,
            ' so on a normal setup most of it has no file in Data\ and LoadAllPlugins skips it. That is
            ' expected, not an error — report it as information so the count doesn't read as data loss.
            If pm.Plugins.Count < effectiveLoadList.Count Then
                log($"  ({effectiveLoadList.Count - pm.Plugins.Count} load-order entries have no file in Data\ — normally Creation Club content that isn't installed — skipped)")
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
            ' loadedPlugins:=effectiveLoadList — ONE notion of "what is loaded", shared by records and
            ' assets (same rule as the GUI, Preflight_Form ~:656). Without it the archives were keyed off
            ' Plugins.txt while the records came from the selection: a deselected mod kept its BA2/BSA
            ' mounted and could still shadow a vanilla asset during the bake.
            FilesDictionary_class.Fill_DictionaryAsync(dataPath, archiveProgress,
                                                       loadedPlugins:=effectiveLoadList).GetAwaiter().GetResult()
            log("  → archives mounted")

            ' ---------------------------------------------------------------------------------
            ' 5. Overlay state — identical to what MainForm starts with: the .bssliders sidecars of
            '    the load order, hydrated into the applied-presets dict (BodyMorphs, LM skin template,
            '    overlays, and the SSE RaceMenu carriers).
            ' ---------------------------------------------------------------------------------
            Dim sidecars = Preflight_Form.ScanSidecarsForPlugins(dataPath, effectiveLoadList)
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
            ' --esptarget: restrict the bake to one plugin. Validate it is actually loaded FIRST — a typo
            ' would otherwise just bake nothing and report a cheerful "0 failed".
            Dim espTarget = If(opt Is Nothing, "", If(opt.EspTarget, "")).Trim().Trim(""""c)
            If espTarget <> "" Then
                If Not pm.Plugins.Any(Function(p) String.Equals(p.FileName, espTarget, StringComparison.OrdinalIgnoreCase)) Then
                    log($"FATAL: --esptarget '{espTarget}' is not among the {pm.Plugins.Count} loaded plugins.")
                    log("       It must be an active plugin present in Data\ (name with extension, e.g. MyMod.esp).")
                    Return ExitFatal
                End If
                log($"Target:      {espTarget} (only NPCs whose winning record comes from this plugin)")
            End If

            Dim allNpcRecords = pm.GetNPCs()
            Dim targets As New List(Of (Fid As UInteger, Name As String, Race As UInteger, Female As Boolean))
            For Each rec In allNpcRecords
                If espTarget <> "" AndAlso Not String.Equals(rec.SourcePluginName, espTarget, StringComparison.OrdinalIgnoreCase) Then Continue For
                Try
                    Dim npc = RecordParsers.ParseNPC(rec, If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown"), pm)
                    If npc Is Nothing Then Continue For
                    targets.Add((npc.FormID, npc.ToString(), npc.RaceFormID, npc.IsFemale))
                Catch
                    ' Unparseable record — nothing to bake; the GUI's ParseAllNPCs swallows these too.
                End Try
            Next

            ' ---------------------------------------------------------------------------------
            ' AGRUPAR POR (RAZA, SEXO) — opt-in, FGBAKE_GROUP_BY_RACE=1
            ' ---------------------------------------------------------------------------------
            ' Las texturas fuente pesadas del bake (head diffuse/normal/specular, 1024²-2048²) son por
            ' RAZA y por SEXO. Con el orden natural de records, los NPCs de una misma raza aparecen
            ' salteados a lo largo de todo el corpus, asi que el cache de decode tiene que retener TODAS
            ' las razas a la vez o pagar re-decodes constantes: por eso crecia sin techo (~9,5 GB medidos).
            ' Agrupando, en cada momento solo hace falta el juego de texturas de UNA (raza,sexo) — y el
            ' borde de grupo es un punto de evicción NATURAL y seguro, en vez de un LRU a ciegas.
            ' ⛔ El orden NO puede cambiar la salida: cada NPC se hornea de forma independiente y el cache
            ' es funcion pura de sus claves. Se valida igual con byte-diff del corpus completo.
            Dim groupByRace = (If(Environment.GetEnvironmentVariable("FGBAKE_GROUP_BY_RACE"), "").Trim() = "1")
            If groupByRace Then
                ' El Fid como ultimo criterio deja el orden TOTALMENTE determinista (dos corridas iguales).
                targets = targets.OrderBy(Function(t) t.Race).ThenBy(Function(t) t.Female).ThenBy(Function(t) t.Fid).ToList()
                Dim grupos = targets.Select(Function(t) (t.Race, t.Female)).Distinct().Count()
                log($"Order:       grouped by (race, gender) — {grupos} group(s); the decode cache is evicted at every boundary")
            Else
                log("Order:       natural record order (historical)")
            End If
            If targets.Count = 0 Then
                If espTarget <> "" Then
                    log($"FATAL: '{espTarget}' is loaded but wins no NPC_ record — nothing to bake.")
                    log("       (Its NPC edits may be overridden by a plugin later in the load order.)")
                Else
                    log("FATAL: no NPC_ records found in the load order.")
                End If
                Return ExitFatal
            End If
            If espTarget <> "" Then
                log($"NPCs:        {targets.Count} NPC_ record(s) to bake (of {allNpcRecords.Count} in the load order)")
            Else
                log($"NPCs:        {targets.Count} NPC_ record(s) to bake")
            End If
            log("")

            ' ---------------------------------------------------------------------------------
            ' 8. The bake loop. Sequential, exactly like the GUI batch: the CPU compositor's batch
            '    decode cache and FaceGenBuilder's shared-neutral scratch both assume one bake at a
            '    time. BeginBatchDecodeCache makes every source DDS decode ONCE for the whole run.
            ' ---------------------------------------------------------------------------------
            Dim evictions As Integer = 0, evictedMb As Long = 0
            Dim baked As Integer = 0, skipped As Integer = 0, failed As Integer = 0
            Dim failures As New List(Of String)
            ' Fallos de TEXTURA: no cuentan como NPC fallado (el NIF salio) pero tienen que verse. Ver el
            ' comentario en el ElseIf r.Success de abajo.
            Dim texFailures As New List(Of String)
            Dim texFailedNpcs As Integer = 0, texFailedSlots As Integer = 0
            Dim sampleLimit As Integer = 0
            Integer.TryParse(If(Environment.GetEnvironmentVariable("FGBAKE_LIMIT"), "").Trim(), sampleLimit)
            If sampleLimit > 0 Then log($"SAMPLE: FGBAKE_LIMIT={sampleLimit} — only the first {sampleLimit} NPC(s) will be processed.")
            FaceGenBuilder.PhaseReset()
            FaceGenBuilder.ParityReset()
            SseFoldLayerStack.ResetSseParity()
            Dim sw = Diagnostics.Stopwatch.StartNew()
            Dim cancelled As Boolean = False

            ' Techo del cache de decode. OPT-IN por env var y APAGADO por default: el cache no tenia limite
            ' y el barrido FO4 completo llego a ~9,5 GB de working set. No se cambia el default sin medir —
            ' un techo que fuerce re-decodes puede costar tiempo, y eso hay que verlo, no suponerlo.
            ' El techo es un RESPALDO, no el mecanismo. El mecanismo real es agrupar por (raza,sexo) y
            ' liberar en el borde (mas abajo): eso acota el conjunto vivo SIN forzar un solo re-decode.
            ' El techo cubre los dos casos que la agrupacion NO cubre:
            '   (a) el bake por lotes de la GUI, que usa este mismo cache y no pasa por aca;
            '   (b) un grupo (raza,sexo) patologicamente grande.
            ' Por default se DERIVA DE LA RAM: un absoluto fijo es inofensivo en 32 GB y letal en 8.
            '   env ausente -> 25 % de la memoria disponible, acotado a [512 MB, 4 GB]
            '   env = "0"   -> SIN techo (comportamiento historico; sirve de baseline para medir)
            '   env > 0     -> ese valor en MB (reproducible entre maquinas, para comparar corridas)
            ' ⭐ La derivacion se MUDO a FaceTintCpuCompositor.ResolveDecodeCacheBudgetFromEnvironment: el
            ' mismo techo lo obedecen ahora tambien los caches de SseFaceTintComposer, y con la politica
            ' duplicada en dos archivos los numeros se habrian separado en silencio. Mismos valores, mismo
            ' contrato de la env, mismo texto de log — solo cambia DONDE vive.
            Dim budget = FaceTintCpuCompositor.ResolveDecodeCacheBudgetFromEnvironment()
            FaceTintCpuCompositor.BatchDecodeCacheBudgetBytes = budget.Bytes
            log(budget.Reason)

            ' ---------------------------------------------------------------------------------
            ' CONTEXTO GL (opt-in, FGBAKE_GPU_PARITY=1) — para medir paridad CPU-vs-GPU en el batch.
            ' ---------------------------------------------------------------------------------
            ' COMO: `PreviewControl` (FO4_Base_Library.Render) ES un OpenTK GLControl que ya pide GL 4.3 Core
            ' en su ctor sin parametros, y `NpcRenderHost` se construye sobre uno — es el MISMO patron que usan
            ' EditFace/EditBody/ArmaEditor/ArmoEditor. Un GLControl necesita un HANDLE de ventana, no una bomba
            ' de mensajes: alcanza con crear un Form y realizar el handle SIN mostrarlo.
            ' ⭐ AFINIDAD DE HILO: un contexto GL vive atado a UN hilo. El loop de abajo es un `For` SINCRONICO,
            ' asi que el contexto que se hace current acá sigue siendo el current en cada BuildCharGen —
            ' PERO solo mientras `Run` no se llame desde un hilo distinto del que crea la ventana.
            ' ⛔ CUIDADO: `--bake-all` SIN `--windowless` muestra la ventana de progreso, y ese camino invoca
            ' `Await Task.Run(Function() BakeAllRunner.Run(...))` (BakeAllProgress_Form) ⇒ TODO esto correria en
            ' un hilo del ThreadPool (MTA, sin bomba de mensajes). Crear ventanas WinForms y hacer
            ' wglMakeCurrent ahi NO esta verificado. Por eso GPU PARITY exige --windowless (chequeo abajo).
            ' ⭐ Sin `Application.Run` no hay pump, asi que el RenderTimer del control NUNCA dispara: no se cuela
            ' ni un render espurio. Se para igual, explicitamente, para no depender de esa propiedad.
            ' ⛔ COSTO: el bake pasa a hacer el compose DOS VECES (CPU + GPU) ⇒ es para MUESTRAS
            ' (combinar con FGBAKE_LIMIT), no para el corpus entero, y sus TIEMPOS no son comparables contra un
            ' baseline CPU-only.
            If gpuParity AndAlso Not opt.Windowless Then
                ' ⛔ ABORTA en vez de intentarlo: el camino con ventana corre este Run dentro de
                ' `Await Task.Run(...)` (BakeAllProgress_Form), o sea en un hilo del ThreadPool sin STA ni
                ' bomba de mensajes. Crear el GLControl ahi puede fallar — o peor, "funcionar" y devolver
                ' readbacks vacios, que es exactamente la medicion fabricada que este instrumento existe para
                ' evitar. El modo verificado es --windowless (hilo STA del Main).
                log("FATAL: FGBAKE_GPU_PARITY=1 requires --windowless.")
                log("       Without --windowless the bake runs on a ThreadPool thread (the progress window")
                log("       launches it with Task.Run) and the GL context has no affinity or pump guarantee.")
                Return ExitFatal
            End If
            If gpuParity Then
                Try
                    glForm = New System.Windows.Forms.Form With {.Width = 64, .Height = 64,
                                                          .FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedToolWindow,
                                                          .ShowInTaskbar = False, .StartPosition = System.Windows.Forms.FormStartPosition.Manual,
                                                          .Location = New Drawing.Point(-4000, -4000)}
                    ' ⛔ WinForms NO esta inicializado en --windowless (ese camino nunca llama a
                    ' Application.EnableVisualStyles ni a Application.Run — ver Program.HeadlessBakeAll). El
                    ' GLControl de OpenTK crea su contexto en OnHandleCreated y necesita el entorno armado; sin
                    ' esto tira "Failed to create GLControl ... before its containing form has been fully
                    ' created". Se inicializa igual que el camino con ventana. Idempotente si ya corrio.
                    Try
                        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.DpiUnaware)
                        System.Windows.Forms.Application.EnableVisualStyles()
                        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(False)
                    Catch
                        ' Ya inicializado (el camino con ventana ya paso por aca): no es un error.
                    End Try
                    ' ⭐ SE MUESTRA DE VERDAD, pero FUERA DE PANTALLA (-4000,-4000) y sin barra de tareas.
                    ' ⛔ POR QUE NO "oculto": el contexto GL se ata al HWND y el compositor dibuja a FBOs, asi que
                    ' EN TEORIA alcanza con realizar el handle. Pero eso depende del driver, y el modo de fallo
                    ' es el peor posible: el contexto se crea "bien" y el readback vuelve en CERO — que no se
                    ' distingue de "el compose dio negro". Una ventana WS_VISIBLE fuera de pantalla es el camino
                    ' probado y no le cuesta nada al usuario. Igual NO se confia en esto: el auto-test de abajo
                    ' es el que decide.
                    '
                    ' ⛔⛔ EL ORDEN DE ESTAS LINEAS ES LOAD-BEARING (medido, con stack): el Form se muestra y se
                    ' realiza ANTES de crear y colgar el PreviewControl. Al reves fallaba asi:
                    '   Controls.Add -> layout -> Control.OnSizeChanged -> PreviewControl.OnResize ->
                    '   ApplyResize -> EnsureContextCurrent -> GLControl.MakeCurrent -> EnsureCreated -> THROW
                    '   "Failed to create GLControl ... before its containing form has been fully created".
                    ' O sea: el control intenta hacer current su contexto DURANTE el layout, cuando todavia no
                    ' hay handle de padre. Y `.Dock = Fill` lo garantizaba, porque fuerza un resize inmediato.
                    ' Con el padre ya realizado, el hijo crea handle y contexto al agregarse y el resize es sano.
                    glForm.Show()
                    ' Sin Application.Run no hay bomba de mensajes; estos DoEvents son el pump minimo para que
                    ' se procesen los mensajes de creacion de la ventana.
                    For pumpN = 1 To 8
                        System.Windows.Forms.Application.DoEvents()
                        If glForm.IsHandleCreated Then Exit For
                    Next
                    If Not glForm.IsHandleCreated Then glForm.CreateControl()
                    ' Tamaño EXPLICITO y sin Dock: asi el bounds no cambia al agregarlo y no se dispara un
                    ' OnResize antes de tiempo.
                    glCtl = New PreviewControl() With {.Left = 0, .Top = 0, .Width = 64, .Height = 64}
                    glForm.Controls.Add(glCtl)
                    For pumpN = 1 To 8
                        System.Windows.Forms.Application.DoEvents()
                        If glCtl.IsHandleCreated Then Exit For
                    Next
                    If Not glCtl.IsHandleCreated Then glCtl.CreateControl()
                    System.Windows.Forms.Application.DoEvents()
                    Dim h = glForm.Handle : Dim h2 = glCtl.Handle
                    glCtl.RenderTimer?.Stop()   ' no queremos renders del control; el compose dibuja a FBOs
                    glCtl.MakeCurrent()
                    glHost = New NpcRenderHost(glCtl)
                    ' ⛔⛔ AUTO-TEST OBLIGATORIO. Sube un patron conocido, lo pasa por el MISMO pase del
                    ' compositor y verifica el readback. Sin esto, un contexto que no dibuja produciria 200 NPCs
                    ' de "paridad" fabricada. Es la regla del arnes: una condicion anomala ABORTA, no se degrada.
                    Dim selfTest = FaceGenBuilder.GlSelfTest(glHost)
                    If selfTest IsNot Nothing Then
                        log($"FATAL: the GL context was created but DOES NOT DRAW: {selfTest}")
                        log("       With a GL that does not draw, CPU-vs-GPU parity would be a made-up number.")
                        Try : glHost.TintGpuCache?.Clear() : Catch : End Try
                        Try : glCtl.Dispose() : Catch : End Try
                        Try : glForm.Dispose() : Catch : End Try
                        Return ExitFatal
                    End If
                    log($"GPU PARITY: GL context created and VERIFIED (handle 0x{h.ToInt64():X}/0x{h2.ToInt64():X}; draw+readback self-test OK).")
                    log("            The bake now runs CPU **and** GPU and compares them in memory, before the BCn encode.")
                    log("            Use with FGBAKE_LIMIT: it doubles the compose work.")
                    ' ⛔ AVISO OBLIGATORIO: con el GPU encendido, BakeFaceTextures escribe TAMBIEN <id>_*_2b.dds
                    ' (la salida GPU) al lado de cada canal ⇒ TRIPLICA los archivos en el arbol de juego. Es la
                    ' misma trampa ya registrada para --ssecomparebatch ("escribe sueltos: apartar entre
                    ' corridas"): esos archivos quedan en Data y una corrida posterior los puede leer como si
                    ' fueran salida propia. Se avisa en vez de que aparezcan solos.
                    log("            ⚠ It ALSO writes the GPU output as <id>_d_2b.dds / _msn_2b.dds / _s_2b.dds")
                    log("              next to each channel — 3 extra files per NPC in the game tree. Move them")
                    log("              aside before any run that reads the bake output back.")
                Catch ex As Exception
                    ' ⛔ NO se degrada a CPU-only en silencio: la corrida se pidio para medir paridad y sin GL no
                    ' mide NADA. Abortar es la unica respuesta honesta (regla del arnes: una condicion anomala
                    ' aborta o se marca, jamas cae a un default).
                    log($"FATAL: FGBAKE_GPU_PARITY=1 but the GL context could not be created: {ex.GetType().Name}: {ex.Message}")
                    log("       STACK: " & ex.ToString())
                    log("       Without a GL context this run would measure only the CPU and report parity that was never tested.")
                    Try : glCtl?.Dispose() : Catch : End Try
                    Try : glForm?.Dispose() : Catch : End Try
                    Return ExitFatal
                End Try
            End If

            FaceTintCpuCompositor.BeginBatchDecodeCache()
            Try
                For i = 0 To targets.Count - 1
                    If isCancelled() Then
                        cancelled = True
                        Exit For
                    End If
                    Dim t = targets(i)
                    ' Evicción en el BORDE de (raza,sexo): a partir de aca ninguna textura del grupo
                    ' anterior se vuelve a pedir, asi que soltarlas no cuesta un solo re-decode.
                    If groupByRace AndAlso i > 0 Then
                        Dim prev = targets(i - 1)
                        If prev.Race <> t.Race OrElse prev.Female <> t.Female Then
                            Dim cs0 = FaceTintCpuCompositor.BatchDecodeCacheStats()
                            FaceTintCpuCompositor.EndBatchDecodeCache()
                            FaceTintCpuCompositor.BeginBatchDecodeCache()
                            evictions += 1
                            evictedMb += cs0.Bytes \ (1024L * 1024L)
                        End If
                    End If
                    Dim pct = CInt(Math.Floor((i + 1) * 100.0 / targets.Count))
                    progress(i + 1, targets.Count, $"{i + 1}/{targets.Count} — {t.Name}")

                    Dim head = $"[{i + 1,5}/{targets.Count} {pct,3}%] 0x{t.Fid:X8} {t.Name}"
                    Dim tNpc = Stopwatch.GetTimestamp()
                    Try
                        ' host = Nothing en el bake normal (100 % CPU). Con FGBAKE_GPU_PARITY=1 se pasa el host
                        ' del contexto GL de arriba y BakeFaceTextures corre TAMBIEN el compositor GPU.
                        Dim r = FaceGenBuilder.BuildCharGen(t.Fid, pm, appliedPresets,
                                                            host:=glHost,
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
                            ' ⛔ AGUJERO DE OBSERVABILIDAD (medido): un NPC cuyo bake de TEXTURAS falla igual
                            ' devuelve Success=True (el NIF se escribio), asi que el batch lo contaba como
                            ' "baked" y NO decia una palabra. Concretamente: una corrida SSE reporto
                            ' "4460 baked / 0 failed" habiendo escrito CERO facetint .dds — el fallo estaba en
                            ' r.TextureSlotsFailed, que solo miraba la GUI (MainForm). Un bake sin texturas es
                            ' un bake ROTO; que salga por consola.
                            If r.TextureSlotsFailed > 0 Then
                                texFailedNpcs += 1
                                texFailedSlots += r.TextureSlotsFailed
                                If texFailures.Count < 40 Then texFailures.Add($"0x{t.Fid:X8} {t.Name}: {r.TextureFailureDetail}")
                                log($"{head} — baked: {r.ShapesKept} shape(s) ⚠ {r.TextureSlotsFailed} TEXTURE(S) FAILED: {r.TextureFailureDetail}")
                            Else
                                log($"{head} — baked: {r.ShapesKept} shape(s) → {r.OutputPath}")
                            End If
                        Else
                            failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {r.Summary}")
                            log($"{head} — FAIL: {r.Summary}")
                        End If
                    Catch ex As Exception
                        failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {ex.GetType().Name}: {ex.Message}")
                        log($"{head} — FAIL: {ex.GetType().Name}: {ex.Message}")
                    End Try
                    FaceGenBuilder.PhaseAdd(FaceGenBuilder.BakePhase.Total, tNpc)
                    ' ⭐ MUESTRA: FGBAKE_LIMIT=N corta despues de N NPCs PROCESADOS. Existe para poder medir el
                    ' desglose de fases (PhaseReport) en ~5 min en vez de las 2 h del corpus entero. El orden de
                    ' `targets` es determinista, asi que la muestra es reproducible.
                    If sampleLimit > 0 AndAlso (i + 1) >= sampleLimit Then
                        log($"SAMPLE: stopped at {i + 1} NPC(s) because of FGBAKE_LIMIT.")
                        Exit For
                    End If
                Next
            Finally
                Dim cst = FaceTintCpuCompositor.BatchDecodeCacheStats()
                log($"Decode cache: {cst.Bytes \ (1024L * 1024L)} MB retained at the end, {cst.Rejected} entries rejected by the cap, " &
                    $"{evictions} boundary evictions ({evictedMb} MB freed in total)")
                ' ⛔ El cache de mascaras de SseFaceTintComposer NO entra en este resumen: no obedece el techo
                ' (es per-NPC, igual que los caches per-host de FO4 — ver el comentario en SseFaceTintComposer).
                ' Su memoria la acota la VIDA, no el presupuesto, asi que no hay bytes "retenidos por techo" ni
                ' rechazos que reportar.
                FaceTintCpuCompositor.EndBatchDecodeCache()
                ' Teardown del contexto GL. Se libera el cache de texturas ANTES de destruir el contexto (si no,
                ' se filtran los handles GL que el cache tiene vivos — contrato de FaceTintTextureCache).
                ' ⛔ Ademas: este exe con --windowless a veces NO SALE al terminar. Un Form vivo empeora eso, asi
                ' que se destruye SIEMPRE, incluso si el loop murio por excepcion.
                If glHost IsNot Nothing OrElse glCtl IsNot Nothing OrElse glForm IsNot Nothing Then
                    Try : glHost?.TintGpuCache?.Clear() : Catch : End Try
                    Try : glCtl?.RenderTimer?.Stop() : Catch : End Try
                    Try : glCtl?.Dispose() : Catch : End Try
                    Try : glForm?.Dispose() : Catch : End Try
                    glHost = Nothing : glCtl = Nothing : glForm = Nothing
                    log("GPU PARITY: GL context destroyed.")
                End If
            End Try
            sw.Stop()

            ' ---------------------------------------------------------------------------------
            ' 9. Summary.
            ' ---------------------------------------------------------------------------------
            Dim processed = baked + skipped + failed
            log("")
            If cancelled Then log($"CANCELLED — {targets.Count - processed} NPC(s) not processed.")
            log($"Done in {sw.Elapsed:hh\:mm\:ss} — {baked} baked / {skipped} skipped / {failed} failed (of {targets.Count}).")
            log("")
            log(FaceGenBuilder.PhaseReport())
            ' Paridad CPU-vs-GPU. Se imprime SIEMPRE, tambien cuando no corrio el GL: en ese caso dice
            ' explicitamente "NO MEDIDA" en vez de callarse, para que nadie lea un barrido CPU-only como si
            ' hubiera validado el compositor del render.
            log("")
            log(FaceGenBuilder.ParityReport())
            log("")
            log(SseFoldLayerStack.SseParityReport())
            If texFailedNpcs > 0 Then
                log("")
                log($"⚠ TEXTURES: {texFailedSlots} slot(s) failed on {texFailedNpcs} NPC(s) that were still counted as 'baked'.")
                For Each f In texFailures
                    log($"  {f}")
                Next
                If texFailedNpcs > texFailures.Count Then log($"  ... and {texFailedNpcs - texFailures.Count} more NPC(s)")
            End If
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
