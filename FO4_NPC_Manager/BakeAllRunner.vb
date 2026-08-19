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
        ''' <summary>True = IGNORAR la seleccion de plugins guardada en Preflight (npc_config.json
        ''' PreflightSelection_FO4/_SSE) y usar SOLO el load order activo.
        ''' <para>Existe porque la seleccion guardada es una dependencia SILENCIOSA: alguien que la fijo hace
        ''' meses hornea contra un set distinto del que tiene activo y nada se lo recuerda salvo una linea del
        ''' log. Con esto una tarea automatizada puede decir "lo que este activo AHORA" sin tener que editar
        ''' npc_config.json (que es estado de la UI) ni Plugins.txt (que es del juego).</para>
        ''' <para>No modifica nada: es por corrida.</para></summary>
        Public SkipCustomList As Boolean = False
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
            NPC_Config.ApplyDownsizeFromMip0Setting()

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
            NPC_Config.ApplyDownsizeFromMip0Setting()

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
            If opt IsNot Nothing AndAlso opt.SkipCustomList Then
                log("Selection:   --skipcustomlist — the saved Preflight selection is IGNORED; using the active load order")
            Else
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
            End If
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
            ' ⛔ Los excluidos por master faltante se reportan APARTE y se descuentan de la cuenta de arriba:
            ' meterlos en el mismo saco los presentaría como "Creation Club no instalado", que es una causa
            ' distinta y benigna. Un plugin que el usuario tiene activo y NO se cargó tiene que decirse con
            ' su nombre y su razón.
            Dim excluded = pm.LastExcludedForMissingMasters
            If excluded IsNot Nothing AndAlso excluded.Count > 0 Then
                log($"  ⚠ {excluded.Count} plugin(s) NOT loaded — a master they need is missing, so their")
                log("    FormIDs can't be resolved. Anything they add or override is absent from this bake:")
                For Each nm In excluded
                    log($"      - {nm}")
                Next
            End If
            Dim missingFiles = effectiveLoadList.Count - pm.Plugins.Count - If(excluded Is Nothing, 0, excluded.Count)
            If missingFiles > 0 Then
                log($"  ({missingFiles} load-order entries have no file in Data\ — normally Creation Club content that isn't installed — skipped)")
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

            ' ⛔⛔ LOS CATÁLOGOS DE SESIÓN, QUE ANTES SÓLO POBLABA LA GUI. `RaceCompatCatalog` y
            ' `SliderCatalog` se construían dentro de `MainForm.EnsureAssetDictionaryAsync`, y este runner
            ' nunca ejecuta MainForm: en Skyrim el barrido corría con `RaceCompatCatalog = Nothing` ⇒
            ' `IsHeadPartValidForRace` daba False para todo el pelo vanilla en razas COtR y el bake
            ' headless producía head-parts DISTINTOS de los de la GUI para el mismo NPC. Va DESPUÉS de
            ' montar el diccionario: el catálogo de sliders lee su config a través de FilesDictionary.
            NpcSessionCatalogs.EnsureLoaded(pm)
            log($"Catalogs:    raceCompat={If(HeadPartResolver.RaceCompatCatalog IsNot Nothing, "loaded", "none")}, " &
                $"raceMenuSliders={If(NpcMorphResolver.SliderCatalog IsNot Nothing, "loaded", "none")}")

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

            ' ⛔ ACÁ, antes del Parallel.ForEach de más abajo. El registro de LUTs de pelo es perezoso, y si
            ' lo despierta el fan-out lo despiertan N hilos a la vez: el primero hace el IO mientras los
            ' demás bloquean, y el resultado del lote pasa a depender del scheduling. Cargarlo en serie acá
            ' deja el camino perezoso como red y el bake determinista.
            LmHairColorLutLoader.EnsureLoaded(pm, dataPath)
            log($"LM hair LUTs: {LmHairColorLutLoader.RegisteredColorCount} colour(s) with a custom palette")

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
            Dim parseFailures As Integer = 0
            Dim parseFirstFailure As String = ""
            For Each rec In allNpcRecords
                If espTarget <> "" AndAlso Not String.Equals(rec.SourcePluginName, espTarget, StringComparison.OrdinalIgnoreCase) Then Continue For
                Try
                    Dim npc = RecordParsers.ParseNPC(rec, If(rec.SourcePluginName <> "", rec.SourcePluginName, "Unknown"), pm)
                    If npc Is Nothing Then Continue For
                    targets.Add((npc.FormID, npc.ToString(), npc.RaceFormID, npc.IsFemale))
                Catch ex As Exception
                    ' Un record que no parsea no se hornea. El comentario viejo decia "la GUI tambien se los
                    ' traga": ya no. La GUI los cuenta, los nombra y saca un aviso que dice literalmente que
                    ' faltarian de cualquier bake — asi que callarlos ACA seria dejar la consecuencia del lado
                    ' silencioso y que el usuario distribuya un FaceGen incompleto sin senal.
                    parseFailures += 1
                    If parseFirstFailure = "" Then _
                        parseFirstFailure = $"{rec.SourcePluginName}:{rec.Header.FormID:X8} — {ex.GetType().Name}: {ex.Message}"
                End Try
            Next
            If parseFailures > 0 Then
                log($"  ⚠ WARNING: {parseFailures} NPC_ record(s) could not be parsed and will NOT be baked.")
                log($"    First: {parseFirstFailure}")
            End If

            ' ⛔ FGBAKE_GROUP_BY_RACE SE ELIMINO (decision del usuario). Agrupaba por (raza,sexo) y evictaba el
            ' cache en cada borde para acotar memoria. Es INCOMPATIBLE con el loop paralelo: la evicción hacía
            ' `EndBatchDecodeCache`, que pone el diccionario en Nothing mientras otros hilos lo estan usando.
            ' Lo que acota la memoria ahora es el TECHO (ResolveDecodeCacheBudgetFromEnvironment), que no
            ' necesita ningun orden particular. Estaba OFF en todas las mediciones.
            log("Order:       natural record order")
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
            ' ⛔ CONTADORES COMPARTIDOS: el loop es PARALELO, asi que se tocan con Interlocked y las listas
            ' bajo lock. Un `baked += 1` desde N hilos pierde incrementos en silencio.
            Dim baked As Integer = 0, skipped As Integer = 0, failed As Integer = 0
            Dim failures As New List(Of String)
            Dim tally As New Object()
            ' Fallos de TEXTURA: no cuentan como NPC fallado (el NIF salio) pero tienen que verse. Ver el
            ' comentario en el ElseIf r.Success de abajo.
            Dim texFailures As New List(Of String)
            Dim texFailedNpcs As Integer = 0, texFailedSlots As Integer = 0
            Dim sampleLimit As Integer = 0
            Integer.TryParse(If(Environment.GetEnvironmentVariable("FGBAKE_LIMIT"), "").Trim(), sampleLimit)
            If sampleLimit > 0 Then log($"SAMPLE: FGBAKE_LIMIT={sampleLimit} — only the first {sampleLimit} NPC(s) will be processed.")
            ' FGBAKE_SKIP_DDS=1 — barrido de NIF sin el trabajo de IMAGEN (compose + BCn + mips + escritura),
            ' que es el costo DOMINANTE: FO4 compone y encodea 3 canales a resolución nativa por NPC.
            ' Misma convención que FGBAKE_LIMIT / _GPU_PARITY / _STATS, y los MISMOS dos interruptores que
            ' el barrido de NIF del CLI ya usa (FO4_FaceTint_CLI: SkipDdsEncode + SkipPixelCompose), donde
            ' está documentado que ninguno cambia lo que el bake escribe en el NIF — validado por byte-diff.
            ' El slot del NIF se escribe igual porque su path es determinista, no depende del encode.
            ' ⛔ Deja los DDS SIN escribir: sirve para comparar NIF contra NIF, NO para mirar píxeles.
            ' ⛔ SE GUARDAN PARA DEVOLVERLOS EN EL Finally. Son `Shared` de la librería y se prendían sin
            ' restaurar nunca. `Run` no es sólo consola: `BakeAllProgress_Form` lo corre con `Await
            ' Task.Run(...)` desde la GUI, así que con la env var puesta un "Bake All" dejaba los dos flags
            ' en True EL RESTO DE LA SESIÓN — y `SkipPixelCompose` hace que el compose devuelva un buffer
            ' NEGRO. Doce líneas más abajo este mismo bloque resetea PhaseReset/ParityReset/etc.: el estado
            ' latcheado ya se sabía que había que devolverlo, a estos dos se les escapó.
            FaceGenBuilder.PhaseReset()
            FaceGenBuilder.ParityReset()
            SseFoldLayerStack.ResetSseParity()
            FaceTintConvention.ResetConventionWarnings()   ' o el aviso latcheado sobrevive al barrido anterior
            Dim sw = Diagnostics.Stopwatch.StartNew()
            Dim cancelled As Boolean = False

            ' Techo del cache de decode. OPT-IN por env var y APAGADO por default: el cache no tenia limite
            ' y el barrido FO4 completo llego a ~9,5 GB de working set. No se cambia el default sin medir —
            ' un techo que fuerce re-decodes puede costar tiempo, y eso hay que verlo, no suponerlo.
            ' Desde que se elimino la agrupacion por (raza,sexo), el techo es el UNICO mecanismo que acota el
            ' conjunto vivo — y tiene que serlo, porque con el loop paralelo no hay ningun punto del barrido
            ' en el que sea seguro tirar el cache entero.
            ' ⛔ CORREGIDO: este comentario decia que el default "se DERIVA DE LA RAM (25 % acotado a
            ' [512 MB, 4 GB])". Eso YA NO ES ASI y hace rato: la derivacion se saco a proposito y el
            ' argumento esta escrito en ResolveDecodeCacheBudgetFromEnvironment (un techo inventado que
            ' fuerza re-decodes cuesta tiempo invisible). El default HOY es SIN TECHO, opt-in por env var.
            '   env ausente -> SIN techo
            '   env = "0"   -> SIN techo (explicito; sirve de baseline para medir)
            '   env > 0     -> ese valor en MB (reproducible entre maquinas, para comparar corridas)
            ' ⭐ La derivacion vive en FaceTintCpuCompositor.ResolveDecodeCacheBudgetFromEnvironment, UNA sola
            ' vez: duplicada en dos archivos los numeros se habrian separado en silencio.
            ' El techo cubre los DOS niveles del cache —nivel 1 DecodedTex (bytes crudos) y nivel 2 Single()
            ' ya resampleado, 4 B por elemento— y por eso alcanza tambien al camino de SSE, que desde el
            ' colapso de `_texCache` pasa por el nivel 2 en vez de por un cache propio sin techo.
            ' El techo lo resuelve el punto de APERTURA del lote, no cada llamador: ver
            ' BeginBatchDecodeCacheConMotivo. Aca solo se loguea el motivo, sin re-derivar nada.

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

            ' ⭐⭐ GATE SIMD, UNA VEZ Y ACA — antes del loop, no adentro.
            ' ⛔ Adentro NO alcanza con que el Lazy sea thread-safe: CUATRO de los self-tests corren
            ' Parallel.ForEach por dentro, asi que el primer hilo se queda con la publicacion mientras los
            ' demas esperan (stall de arranque). Corriendolo aca, cuando el loop arranca ya esta resuelto.
            FaceGenBuilder.EnsureSimdParityGate()

            ' ⭐ FGBAKE_LIMIT se aplica RECORTANDO la lista, no con un Exit For. Con el loop paralelo "los
            ' primeros N procesados" deja de estar definido: N hilos terminan en orden arbitrario. Recortando,
            ' la muestra vuelve a ser EXACTAMENTE los N primeros del orden determinista de `targets`.
            If sampleLimit > 0 AndAlso sampleLimit < targets.Count Then
                targets = targets.Take(sampleLimit).ToList()
                log($"SAMPLE: FGBAKE_LIMIT={sampleLimit} — the target list was trimmed to the first {targets.Count} NPC(s).")
            End If

            ' ⭐⭐ CUANTOS NPCs A LA VEZ = LOS CORES DE LA MAQUINA. Derivado en runtime, sin constante propia:
            ' la app se distribuye y cualquier numero calibrado a un equipo es basura en otro.
            ' ⛔ ACA HUBO UN `\ 4` Y ESTABA MAL. El argumento era "el compose interno ya satura, un NPC por
            ' core solo agrega contencion". Eso vale para FO4, donde el compose es el 91 % del wall — NO para
            ' SSE, donde es el 18,9 % y la maquina queda ociosa. MEDIDO sobre el corpus SSE entero (4460 NPCs,
            ' 12 cores): N=1 5:16 · N=3 (lo que daba el \4) 3:46 · N=8 3:27 · N=12 2:55 = x1,81. El `\ 4`
            ' dejaba el 35 % de la ganancia sin tomar, y el usuario lo vio como "CPU al 40 %".
            ' ⚠️ La curva ya esta APLANANDO y se sabe por que: `NifWrite` y `other` se inflan ~x7 con 8 hilos
            ' mientras `Textures` (el compose, que si escala) apenas x1,6 ⇒ el techo no es CPU, es I/O y
            ' contencion en esas dos fases. Subir N mas alla de los cores gastaria energia sin ganar wall:
            ' lo que falta atacar son esas fases, no este numero.
            ' ⛔ SIN CONSTANTES PROPIAS. Todo lo de abajo sale de la MAQUINA que corre o del RUNTIME; no hay un
            ' solo numero calibrado sobre un equipo concreto. La app se distribuye como mod: el que la corre
            ' puede tener 4 cores y 8 GB o 24 y 64, con vanilla o con 300 mods de texturas 4K, y ninguno de
            ' esos casos se puede predecir desde acá.
            Dim hardCap As Integer = Math.Max(1, Environment.ProcessorCount)
            ' N FIJO sólo si alguien lo pide explícitamente. ⛔ El override APAGA el controlador: si no, dos
            ' corridas nunca serían comparables y se pierde la capacidad de hacer un A/B (que es como se
            ' probó que el paralelismo no mueve bytes).
            Dim fixedDop As Integer = 0
            Dim envThreads As Integer = 0
            If Integer.TryParse(If(Environment.GetEnvironmentVariable("FGBAKE_NPC_THREADS"), "").Trim(), envThreads) AndAlso envThreads > 0 Then
                fixedDop = Math.Max(1, Math.Min(32, envThreads))
                If fixedDop <> envThreads Then
                    log($"Parallelism: FGBAKE_NPC_THREADS={envThreads} clamped to {fixedDop} (allowed range 1..32)")
                Else
                    ' ⛔ NO dice "adaptive controller OFF" a secas: lo que esta apagado es el TREPE. La guarda
                    ' de memoria corre igual y puede bajar por debajo de este numero (y despues volver). Si el
                    ' log promete un N fijo y el barrido corre con otro, un A/B contra este arnes compara dos
                    ' cosas distintas creyendo que compara una — ver 63-arnes-de-medicion-wm.
                    log($"Parallelism: FIXED at {fixedDop} NPC(s) (FGBAKE_NPC_THREADS) — no climb; the memory guard can still lower and restore it")
                End If
            End If
            ' ⛔ CON PARIDAD GPU NO SE PARALELIZA, y se DICE. El contexto GL vive atado a UN hilo y
            ' FaceGenBuilder hace EnsureContextCurrent por NPC: con N hilos el contexto deja de ser el current
            ' del hilo que compone y la medicion de paridad seria basura. Degradar en silencio seria peor.
            If gpuParity Then
                fixedDop = 1
                log("Parallelism: FORCED TO 1 because FGBAKE_GPU_PARITY=1 — the GL context is per-thread.")
            End If

            ' ⛔ DIAGNOSTICO: Logger.Enabled es False en Release, asi que gatear SOLO por el dejaba tambien
            ' sin numeros al arnes de medicion, que corre Release. `FGBAKE_STATS=1` los reactiva —
            ' misma convencion que FGBAKE_LIMIT / FGBAKE_GPU_PARITY / FGBAKE_DECODE_CACHE_MB.
            Dim wantStats As Boolean = Logger.Enabled OrElse
                                       If(Environment.GetEnvironmentVariable("FGBAKE_STATS"), "").Trim() = "1"
            ' Declaradas ACA y no dentro del Try: las lee el resumen final, que esta fuera de ese bloque.
            Dim traj As New List(Of String)
            Dim peakPermits As Integer = If(fixedDop > 0, fixedDop, 1)


            ' ⛔ LOS DOS INTERRUPTORES SE PRENDEN PEGADO AL Try QUE LOS DEVUELVE. Estaban ~215 líneas más
            ' arriba, y entre medio se crea el CONTEXTO GL (con FGBAKE_GPU_PARITY=1), se enumeran los targets
            ' y se hidratan los overlays: cualquier excepción ahí salía sin restaurar y dejaba
            ' `SkipPixelCompose = True` para el resto de la sesión de la GUI ⇒ todo compose posterior devuelve
            ' un buffer NEGRO. Es el mismo modo de falla que el comentario de abajo describe, movido al camino
            ' de error — y muerde justo con la env var del arnés puesta, o sea envenenando el instrumento.
            ' Nada entre el punto viejo y éste LEE los flags: se consumen dentro del loop, en compose/encode.
            Dim prevSkipDds = FaceGenBuilder.SkipDdsEncode
            Dim prevSkipCompose = FaceTintCpuCompositor.SkipPixelCompose

            ' ⛔ EL SETEO VA ADENTRO DEL Try. Quedó AFUERA cinco líneas, y su `log("SAMPLE: ...")`
            ' corría con los dos flags YA PRENDIDOS: si ese log tiraba —o si tiraba
            ' `BeginBatchDecodeCacheConMotivo`— el Finally no corría y `SkipPixelCompose` quedaba en True
            ' para el resto de la sesión de la GUI ⇒ todo compose posterior devuelve un buffer NEGRO.
            ' Es el mismo modo de falla que este arreglo dice cerrar, desplazado. Las CAPTURAS sí quedan
            ' afuera: se leen antes de tocar nada y el Finally las necesita.
            Try
                If If(Environment.GetEnvironmentVariable("FGBAKE_SKIP_DDS"), "").Trim() = "1" Then
                    FaceGenBuilder.SkipDdsEncode = True
                    FaceTintCpuCompositor.SkipPixelCompose = True
                    log("SAMPLE: FGBAKE_SKIP_DDS=1 — image work skipped (NIF-only sweep); DDS are NOT written.")
                End If

                ' ⛔ EL Begin VA PEGADO AL Try QUE LO CIERRA. Al mover el bloque de FGBAKE_SKIP_DDS quedo
                ' EN EL MEDIO, con dos `log()` entre la apertura del lote y el Try: `log` lo provee el
                ' llamador (la consola o el form de progreso) y ninguno declara ser thread-safe ni
                ' resistente a un form cerrandose. Si uno tiraba, `EndBatchDecodeCache` no corria y el lote
                ' quedaba abierto con sus texturas 4K por el resto de la sesion de la GUI — el mismo patron
                ' que este arreglo perseguia, reintroducido por el arreglo.
                Dim budgetReason = FaceTintCpuCompositor.BeginBatchDecodeCacheConMotivo()
                log(budgetReason)   ' ⛔ DENTRO del Try: `log` es del llamador y puede tirar.
                Dim done As Integer = 0

                ' ⛔ `log` y `progress` los provee el CALLER (la consola, o el form de progreso de la GUI) y
                ' NINGUNO declara ser thread-safe. Con N hilos se serializan: al lado de hornear un NPC cuestan
                ' nada, y asi no dependemos de una garantia que el sink nunca dio.
                Dim logSync = Sub(s As String)
                                  SyncLock tally
                                      log(s)
                                  End SyncLock
                              End Sub
                ' ═══════════════════════════════════════════════════════════════════════════════════════
                ' ⭐⭐ CONCURRENCIA ADAPTATIVA — el N lo DESCUBRE la corrida, no lo fija una constante.
                ' ═══════════════════════════════════════════════════════════════════════════════════════
                ' POR QUE no una formula: el cuello NO es CPU. Medido sobre el corpus SSE entero: al pasar de
                ' 1 a 8 NPCs en vuelo, `Textures` (el compose) crece x1,6 pero `NifWrite` y `other` crecen
                ' x7,4 — o sea que los hilos ESPERAN. El N optimo lo fijan el disco y la memoria del equipo,
                ' que es justo lo que ProcessorCount no sabe. Y con mods de texturas 4K cada NPC pesa otra
                ' cosa, asi que tampoco se puede tabular por juego.
                '
                ' ⛔ LICENCIA PARA VARIARLO EN CALIENTE: esta MEDIDO que el orden no mueve la salida — dos
                ' corridas paralelas con N distinto (8 vs 12) dieron 0 bytes de diferencia sobre 8920
                ' archivos y 6.235.526.000 bytes de pixel. Sin ese resultado esto no seria aceptable.
                '
                ' DE DONDE SALE CADA COTA (ninguna es mia):
                '   arranque = 1                     el comportamiento serial de referencia
                '   techo    = ProcessorCount        la maquina
                '   memoria  = GC.GetGCMemoryInfo()  el UMBRAL LO CALCULA EL PROPIO GC para ese equipo
                '   frenar   = el throughput dejo de mejorar, medido EN ESTA corrida
                '   muestras = la concurrencia actual (lo que tarda en llenarse el pipeline)
                '
                ' POLITICA ASIMETRICA a proposito: sube de a UNO y solo si mejoro; baja EN EL ACTO ante
                ' presion de memoria. Quedarse corto cuesta minutos; pasarse cuesta un OutOfMemory a la mitad
                ' de un barrido de una hora, con NPCs a medio escribir.
                ' El ajuste ocurre SOLO en el borde de NPC, nunca dentro de un compose.
                ' ⛔ NO MEDIR THROUGHPUT. Se probó y dio 6:18 contra 3:58 del N fijo — 59 % PEOR, con
                ' trayectoria `1 → 2 → 3 → 2(peak)`. El throughput por-NPC es una señal sucia: mezcla CPU,
                ' esperas de I/O, warm-up (caché frío/JIT) y sobre todo la varianza entre NPCs (uno de 2
                ' shapes contra uno de 8, y 2088 que se saltean). Con pocas muestras el ruido gana y el
                ' hill-climbing toma el primer bajón por el óptimo. Promediarlo exigiria fijar CUANTAS
                ' muestras = la constante arbitraria que se queria evitar.
                '
                ' ⭐ LA SEÑAL CORRECTA ES EL RECURSO: ¿el worker que agregué TRABAJA o ESPERA?
                '     cpuBusy = Δ TotalProcessorTime / (Δ wall × ProcessorCount)   ∈ [0,1]
                ' Es el cociente de dos ACUMULADORES, no un conteo de eventos: estable en ventana corta, sin
                ' necesidad de promediar N muestras. Ahí desaparece la constante.
                '
                ' REGLA, sin un solo número inventado:
                '   SUBIR   mientras quede AL MENOS UN CORE OCIOSO (cpuBusy < 1 − 1/cores) y la memoria esté
                '           bajo el umbral del GC. "Un core" no es una constante elegida: es la unidad.
                '   BAJAR   en el acto si la memoria cruza el umbral que el GC calcula para esta máquina.
                '   TECHO   ProcessorCount.
                ' Si el bake es I/O-bound, cpuBusy se queda bajo y trepa hasta el techo — que es justo el N
                ' que midió mejor (3:58). Si la máquina satura CPU antes, frena antes. Si le falta RAM, baja.
                ' Ninguno de esos tres casos se puede predecir desde acá: los descubre el equipo que corre.
                Dim permits As Integer = If(fixedDop > 0, fixedDop, 1)
                Dim sem As New Threading.SemaphoreSlim(permits, Math.Max(hardCap, permits))
                Dim ctl As New Object()
                Dim lvlDone As Integer = 0
                Dim climbing As Boolean = (fixedDop = 0 AndAlso hardCap > 1)
                ' ⛔ EL NIVEL AL QUE HAY QUE VOLVER cuando la presion de memoria se va. Sin esto la guarda de
                ' abajo es un TRINQUETE: resta un permiso por cada NPC terminado bajo presion y no devuelve
                ' ninguno nunca. `MemoryLoadBytes` es una señal DE MAQUINA, no del proceso (el umbral del GC
                ' ronda el 90 % de la RAM fisica), asi que alcanza con que el usuario abra el navegador o
                ' tenga el juego cargado para que doce NPCs seguidos dejen el barrido en serie por el resto
                ' de la corrida — y no se entera nadie, porque queda solo en `traj`.
                Dim nivelObjetivo As Integer = permits
                Dim limpiosSeguidos As Integer = 0
                Dim proc = Process.GetCurrentProcess()
                Dim lastCpu As TimeSpan = proc.TotalProcessorTime
                Dim lastWall As Long = Stopwatch.GetTimestamp()
                traj.Add(permits.ToString())
                peakPermits = permits

                ' Devuelve el permiso y, si corresponde, ajusta el nivel. Llamarlo UNA vez por NPC terminado.
                Dim releaseAndTune =
                    Sub()
                        SyncLock ctl
                            ' ⛔⛔ LA GUARDA DE MEMORIA VA PRIMERO Y CORRE SIEMPRE.
                            ' Estaba DESPUES del `If Not climbing Then Return`, y ella misma ponia
                            ' `climbing = False` al bajar. O sea: bajaba de N a N-1 UNA sola vez y a partir
                            ' de ahi toda llamada salia por el early-return y no volvia a mirar la memoria
                            ' nunca mas. Si la presion seguia subiendo —y sube, porque el cache de decode no
                            ' tiene techo por default— el controlador ya no podia hacer nada.
                            ' Peor: `climbing` arranca en False cuando el usuario fija FGBAKE_NPC_THREADS,
                            ' asi que con DOP fijo la guarda no corria NI UNA VEZ.
                            ' El comentario de arriba promete "Si le falta RAM, baja" — recien ahora puede.
                            ' `climbing = False` se conserva: bajar por memoria SI tiene que cortar el TREPE,
                            ' porque volver a subir es volver contra la misma pared. Lo que NO corta es el
                            ' REGRESO a `nivelObjetivo`, que es otra cosa: recuperar lo que la presion se
                            ' llevo no es explorar hacia arriba.
                            Dim mi = GC.GetGCMemoryInfo()
                            Dim presion = mi.HighMemoryLoadThresholdBytes > 0 AndAlso
                                          mi.MemoryLoadBytes >= mi.HighMemoryLoadThresholdBytes
                            If presion Then
                                ' ⛔ SE DESCUENTA UNO, NO SE PONE EN CERO. Ponerlo en cero hacia que en una
                                ' maquina donde la presion es casi PERMANENTE —los 8 GB que son el caso de uso
                                ' declarado, con el umbral del GC rondando el 90 % de la RAM fisica— el
                                ' contador no llegara nunca al techo y el barrido se quedara en serie para
                                ' siempre: exactamente lo que esta guarda dice haber arreglado.
                                If limpiosSeguidos > 0 Then limpiosSeguidos -= 1
                                If permits > 1 Then
                                    permits -= 1
                                    climbing = False
                                    traj.Add($"{permits}(mem)")
                                    Return                  ' NO se devuelve el permiso ⇒ baja la concurrencia
                                End If
                                ' ⛔⛔ CON permits = 1 NO SE PUEDE BAJAR, PERO TAMPOCO SE TREPA.
                                ' Antes esto caia al camino normal y, si `climbing` seguia en True (DOP
                                ' adaptativo que nunca llego a bajar porque nunca pudo), terminaba en
                                ' `permits += 1` + `sem.Release(2)`: SUBIENDO la concurrencia con la maquina
                                ' por encima del umbral del GC. Es exactamente el caso de los 8 GB, donde la
                                ' presion es casi permanente y `permits` vive en 1 — o sea que el comentario
                                ' de arriba ("la guarda va primero y corre siempre") prometia lo contrario de
                                ' lo que hacia justo donde mas importa.
                                sem.Release()
                                Return
                            ElseIf permits < nivelObjetivo Then
                                ' ⛔ EL UMBRAL ES EL NIVEL ACTUAL, NO EL OBJETIVO. Con `nivelObjetivo` volver
                                ' de 1 a N costaba N·(N−1) NPC limpios SEGUIDOS —240 con hardCap 16— porque el
                                ' contador se reinicia despues de cada +1. Con el nivel actual el costo total
                                ' es N·(N+1)/2 y, sobre todo, el PRIMER escalon cuesta 1: se sale de la serie
                                ' apenas la presion afloja, que es cuando importa.
                                ' ⚠️ NO ESTA MEDIDO. Es aritmetica sobre el peor caso, no un barrido: la
                                ' recuperacion necesita una maquina que entre y salga de presion, y este equipo
                                ' no la reproduce. Ver 10-stack-arnes-de-medicion antes de tocar el ritmo.
                                limpiosSeguidos += 1
                                If limpiosSeguidos >= permits Then
                                    permits += 1
                                    limpiosSeguidos = 0
                                    traj.Add($"{permits}(mem+)")
                                    ' ⛔ INVARIANTE, hoy se cumple por accidente: acá se hace UN Release y
                                    ' abajo el camino `Not climbing` hace el otro ⇒ 2 en total, que es lo
                                    ' que corresponde a `permits + 1`. Eso vale SÓLO porque `climbing`
                                    ' está garantizado en False (el único decremento de `permits` siempre
                                    ' lo apaga). Si alguien vuelve a encender `climbing` después de una
                                    ' baja por memoria, esto pasa a 3 Releases y el `sem.Release(2)` del
                                    ' trepe tira SemaphoreFullException. No romper esa relación.
                                    sem.Release()           ' el permiso RETENIDO vuelve al pozo
                                    ' y abajo se devuelve ademas el propio ⇒ +1 de concurrencia real
                                End If
                            End If

                            If Not climbing Then
                                sem.Release()
                                Return
                            End If
                            lvlDone += 1
                            ' Se re-evalua cuando el nivel actual ya tuvo tiempo de llenarse (un NPC por
                            ' permiso). El disparador puede ser ruidoso: NO importa, porque lo que se mide
                            ' abajo es un cociente de acumuladores, no una tasa de eventos.
                            If lvlDone < permits Then
                                sem.Release()
                                Return
                            End If
                            lvlDone = 0
                            Dim nowCpu = proc.TotalProcessorTime
                            Dim nowWall = Stopwatch.GetTimestamp()
                            Dim dWall = (nowWall - lastWall) / CDbl(Stopwatch.Frequency)
                            Dim dCpu = (nowCpu - lastCpu).TotalSeconds
                            lastCpu = nowCpu : lastWall = nowWall
                            Dim cpuBusy = If(dWall > 0.0, dCpu / (dWall * hardCap), 1.0)
                            ' ¿Queda al menos UN core ocioso? Esa es la condicion de "hay espacio".
                            If cpuBusy < 1.0 - (1.0 / hardCap) AndAlso permits < hardCap Then
                                permits += 1
                                ' El trepe MUEVE el piso al que vuelve la guarda de memoria: lo que se gano
                                ' midiendo cores ociosos no se pierde por una presion pasajera.
                                nivelObjetivo = Math.Max(nivelObjetivo, permits)
                                peakPermits = Math.Max(peakPermits, permits)
                                traj.Add($"{permits}@{cpuBusy:P0}")
                                sem.Release(2)              ' el suyo + uno mas ⇒ sube la concurrencia
                            Else
                                climbing = False            ' CPU saturada o techo de la maquina
                                traj.Add($"{permits}(stop@{cpuBusy:P0})")
                                sem.Release()
                            End If
                        End SyncLock
                    End Sub

                ' ⛔⛔ CON PARIDAD GPU EL DOP REAL TIENE QUE SER 1 — y hasta acá NO LO ERA. El `fixedDop = 1`
                ' de arriba alimentaba SOLO al controlador de permisos; este ParallelOptions seguía recibiendo
                ' `hardCap`, así que Parallel.ForEach despachaba el cuerpo en hilos del ThreadPool mientras el
                ' contexto GL vive atado al hilo STA que lo creó (ver "CONTEXTO GL", cuyo comentario todavía
                ' decía "el loop de abajo es un For SINCRONICO" — dejó de serlo al paralelizar por NPC).
                ' La PRIMERA llamada GL desde un hilo del pool tira AccessViolationException y MATA el proceso.
                ' MEDIDO 2026-08-01: crash en `GL.GenTexture()` al NPC 7/40 con FGBAKE_GPU_PARITY=1.
                ' El log ya imprimía "Parallelism: FORCED TO 1": decía la verdad del CONTROLADOR y una mentira
                ' del DESPACHO. Con DOP=1 el cuerpo corre en el hilo LLAMADOR, que es el dueño del contexto.
                ' ⭐ Esto es lo que dejaba MUERTO al único instrumento que ve el compositor GL: el barrido
                ' normal corre con parity=0 (ciego al GLSL) y el modo que sí lo mira crasheaba.
                Dim dop As Integer = If(gpuParity, 1, hardCap)
                Dim popt As New System.Threading.Tasks.ParallelOptions With {.MaxDegreeOfParallelism = dop}
                System.Threading.Tasks.Parallel.ForEach(targets, popt,
                 Sub(t, state)
                    ' Cancelacion ANTES de tomar el permiso: si sale por aca no hay nada que devolver.
                    If isCancelled() Then
                        cancelled = True
                        state.Stop()
                        Return
                    End If
                    sem.Wait()          ' ⇦ la concurrencia real la gobierna el controlador, no MaxDegreeOfParallelism
                    Try
                    ' ⛔⛔ EL CONTADOR CUENTA NPCs TERMINADOS, NO ARRANCADOS. Estaba incrementandose ACA
                    ' —antes de hornear— y eso daba dos defectos a la vez: con N NPCs en vuelo la barra
                    ' saltaba a N sin que terminara ninguno, y el ORDEN en que los hilos reportaban no era el
                    ' de sus numeros, asi que la barra RETROCEDIA (57 → 56). Windows usa ese valor para la
                    ' barra de la taskbar, y ahi el retroceso se ve.
                    ' Contando al TERMINAR, cada NPC suma exactamente +1 y el valor es monotonico POR
                    ' CONSTRUCCION — no hace falta ninguna guarda ni depende del orden.
                    Dim tNpc = Stopwatch.GetTimestamp()
                    Try
                        ' host = Nothing en el bake normal (100 % CPU). Con FGBAKE_GPU_PARITY=1 se pasa el host
                        ' del contexto GL de arriba y BakeFaceTextures corre TAMBIEN el compositor GPU.
                        Dim r As FaceGenBuilder.BuildResult = Nothing
                        Dim buildErr As Exception = Nothing
                        Try
                            r = FaceGenBuilder.BuildCharGen(t.Fid, pm, appliedPresets,
                                                            host:=glHost,
                                                            applyMaterialOverrides:=AddressOf materialResolver.ApplyShapeMaterialOverrides,
                                                            willBePacked:=False,
                                                            lmSkinTemplateResolver:=resolveLmSkin)
                        Catch bex As Exception
                            buildErr = bex
                        End Try
                        ' Recien ACA se cuenta: el NPC termino (bien o mal).
                        Dim i = Threading.Interlocked.Increment(done)
                        Dim pct = CInt(Math.Floor(i * 100.0 / targets.Count))
                        Dim head = $"[{i,5}/{targets.Count} {pct,3}%] 0x{t.Fid:X8} {t.Name}"
                        SyncLock tally
                            progress(i, targets.Count, $"{i}/{targets.Count} — {t.Name}")
                        End SyncLock
                        If buildErr IsNot Nothing Then
                            SyncLock tally
                                failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {buildErr.GetType().Name}: {buildErr.Message}")
                            End SyncLock
                            logSync($"{head} — FAIL: {buildErr.GetType().Name}: {buildErr.Message}")
                        ElseIf r Is Nothing Then
                            SyncLock tally
                                failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: BuildCharGen returned nothing")
                            End SyncLock
                            logSync($"{head} — FAIL: BuildCharGen returned nothing")
                        ElseIf r.Skipped Then
                            Threading.Interlocked.Increment(skipped)
                            logSync($"{head} — skip: {r.Summary}")
                        ElseIf r.Success Then
                            Threading.Interlocked.Increment(baked)
                            ' ⛔ AGUJERO DE OBSERVABILIDAD (medido): un NPC cuyo bake de TEXTURAS falla igual
                            ' devuelve Success=True (el NIF se escribio), asi que el batch lo contaba como
                            ' "baked" y NO decia una palabra. Concretamente: una corrida SSE reporto
                            ' "4460 baked / 0 failed" habiendo escrito CERO facetint .dds — el fallo estaba en
                            ' r.TextureSlotsFailed, que solo miraba la GUI (MainForm). Un bake sin texturas es
                            ' un bake ROTO; que salga por consola.
                            If r.TextureSlotsFailed > 0 Then
                                SyncLock tally
                                    texFailedNpcs += 1
                                    texFailedSlots += r.TextureSlotsFailed
                                    If texFailures.Count < 40 Then texFailures.Add($"0x{t.Fid:X8} {t.Name}: {r.TextureFailureDetail}")
                                End SyncLock
                                logSync($"{head} — baked: {r.ShapesKept} shape(s) ⚠ {r.TextureSlotsFailed} TEXTURE(S) FAILED: {r.TextureFailureDetail}")
                            Else
                                logSync($"{head} — baked: {r.ShapesKept} shape(s) → {r.OutputPath}")
                            End If
                        Else
                            SyncLock tally
                                failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {r.Summary}")
                            End SyncLock
                            logSync($"{head} — FAIL: {r.Summary}")
                        End If
                    Catch ex As Exception
                        ' Red de seguridad: BuildCharGen ya tiene su propio Catch arriba, asi que aca solo
                        ' caeria un fallo del REPORTE (log/progress). El NPC igual conto al terminar.
                        SyncLock tally
                            failed += 1 : failures.Add($"0x{t.Fid:X8} {t.Name}: {ex.GetType().Name}: {ex.Message}")
                        End SyncLock
                        logSync($"0x{t.Fid:X8} {t.Name} — FAIL (reporting): {ex.GetType().Name}: {ex.Message}")
                    End Try
                    ' PhaseAdd es Interlocked por dentro (verificado) ⇒ seguro con N hilos. ⚠️ Pero ojo al
                    ' LEERLO: con el loop paralelo el TOTAL acumulado deja de aproximar el wall clock, que es
                    ' justamente lo que probaba que el loop era serial. Por eso el resumen imprime los dos.
                    FaceGenBuilder.PhaseAdd(FaceGenBuilder.BakePhase.Total, tNpc)
                    Finally
                        ' ⛔ EN Finally: si BuildCharGen tira, el permiso TIENE que volver igual o el barrido
                        ' se queda sin concurrencia de a poco hasta trabarse del todo.
                        releaseAndTune()
                    End Try
                 End Sub)
            Finally
                ' ⛔ LOS FLAGS PRIMERO. Son dos asignaciones que no pueden tirar, asi que si algo de abajo
                ' revienta igual quedan restaurados. `SkipPixelCompose = True` colgado deja todo compose
                ' posterior de la sesion de la GUI devolviendo un buffer NEGRO. Nada de lo que sigue en este
                ' Finally los lee.
                FaceGenBuilder.SkipDdsEncode = prevSkipDds
                FaceTintCpuCompositor.SkipPixelCompose = prevSkipCompose
                ' ⛔ BLOQUE DE DIAGNOSTICO, GATEADO. A un usuario que hornea su load order no le sirve NADA de
                ' esto: hits/misses de cache, bytes por nivel y contadores del izado son para MEDIR, no para
                ' operar. Va todo bajo Logger.Enabled (= FaceGenBuilder.DebugMode), que es lo que prenden el
                ' arnes y las corridas de medicion, y queda fuera de una corrida normal.
                ' ⛔ Las llamadas a *Stats() tambien van adentro del If: son lecturas Interlocked baratas, pero
                ' la regla del proyecto es gatear el CALCULO, no solo el log.
                ' ⛔⛔ EL DIAGNOSTICO VA EN SU PROPIO Try. Son CUATRO `log(...)` con el sink del llamador,
                ' que este archivo declara no confiable: si uno tira, el Finally se corta ahi, se saltea
                ' `EndBatchDecodeCache` y el teardown GL de abajo, y la excepcion ademas REEMPLAZA a la
                ' original. Los flags ya quedaron restaurados arriba, al tope del Finally, justamente para
                ' que esto no pueda dejarlos colgados. Y `wantStats` es Logger.Enabled o FGBAKE_STATS=1, o
                ' sea que el fallo apuntaria justo al arnes de medicion.
                Try
                If wantStats Then
                Dim cst = FaceTintCpuCompositor.BatchDecodeCacheStats()
                log($"Decode cache TOTAL (both levels, one shared cap): {cst.Bytes \ (1024L * 1024L)} MB retained at the end, " &
                    $"{cst.Rejected} entries rejected by the cap")
                ' ⭐ DESGLOSE POR NIVEL. Los dos niveles guardan cosas DISTINTAS (nivel 1 = textura decodificada,
                ' 1 B por elemento; nivel 2 = buffer ya resampleado a w×h, 4 B por elemento) y comparten UN techo.
                ' Sin hits/misses y bytes de los DOS no se puede contestar si el nivel 2 paga su 4x: el total solo
                ' dice cuanto pesa el conjunto. Este es el dato que decide si conviene tocar el storage.
                Dim dst = FaceTintCpuCompositor.DecodeCacheStats()
                log($"  level 1 (decoded DDS, 1 B/elem): {dst.Hits} hits / {dst.Misses} misses, " &
                    $"{dst.Bytes \ (1024L * 1024L)} MB, {dst.Rejected} rejected")
                ' ⭐ El NIVEL 2 con su contador de RESAMPLES. No es cosmético: dice si esta corrida ejercitó o no
                ' la ley del bilineal. Con `resampled=0` el corpus salió TODO por el atajo de identidad, y
                ' entonces un A/B en 0 bytes NO dice nada sobre un cambio en esa ley — el gate de eso es el
                ' self-test `bilinear`. Sin este número, eso es una suposición y no un dato.
                ' ⭐ IZADO DEL RESAMPLE. Con 0 texturas izadas, esta corrida NO ejercito ese camino y un
                ' A/B de bytes en 0 no dice nada sobre el — igual que el `resampled` del nivel 2.
                Dim hst = FaceTintCpuCompositor.HoistStats()
                log($"  resample HOIST: {hst.Textures} texture(s) materialized to SoA planes, {hst.Pixels} px" &
                    If(hst.Textures = 0, "  ⚠ THIS RUN DID NOT EXERCISE THE HOISTED PATH", ""))
                Dim ust = FaceTintCpuCompositor.UnitCacheStats()
                log($"  level 2 (resampled to w×h, 4 B/elem): {ust.Hits} hits / {ust.Misses} misses, " &
                    $"{ust.Bytes \ (1024L * 1024L)} MB, {ust.Rejected} rejected — {ust.Resampled} of the misses went " &
                    $"through the BILINEAR (the rest hit the identity shortcut: source already at the accumulator size)")
                ' ⛔ Los hits/misses/rechazos son ACUMULADOS de toda la corrida; los MB son los del cache vivo
                ' al cerrar.
                End If   ' wantStats — fin del bloque de diagnostico
                Catch exStats As Exception
                    ' Perder el diagnostico es barato; perder el restore de los flags no.
                    Dim ms = exStats.Message
                    Logger.LogLazy(Function() $"[BAKE] el bloque de estadisticas fallo: {ms}")
                End Try
                ' ⛔ `End` CON SU PROPIO Try, no el de estadisticas: su Catch dice "el bloque de estadisticas
                ' fallo", que seria mentira. Y si tira sin red se saltea el teardown GL de abajo, que este
                ' mismo archivo justifica con "este exe con --windowless a veces NO SALE al terminar".
                ' ⛔ El ORDEN es obligatorio: `EndBatchDecodeCache` pone los dos caches en Nothing y los
                ' vacia, y las estadisticas de arriba reportan los MB del cache VIVO — despues del End
                ' darian todas cero.
                Try
                    FaceTintCpuCompositor.EndBatchDecodeCache()
                Catch exEnd As Exception
                    Dim me2 = exEnd.Message
                    Logger.LogLazy(Function() $"[BAKE] EndBatchDecodeCache falló: {me2}")
                End Try
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
            ' ⛔⛔ COMO LEER EL PhaseReport CON EL LOOP PARALELO. Su `TOTAL` es la suma del tiempo POR NPC, o
            ' sea trabajo de CPU acumulado; con N hilos ya NO aproxima el wall clock. Antes coincidian, y esa
            ' coincidencia era justamente la evidencia de que el loop era serial. El cociente TOTAL/wall son
            ' los hilos EFECTIVOS. Todo esto es para MEDIR: el PhaseReport entero va gateado.
            If wantStats Then
                ' ⭐ La TRAYECTORIA, no solo el valor final: si un usuario reporta que el bake se le arrastro,
                ' esto dice si el controlador se quedo en 1, si trepo y freno por memoria, o si toco el techo.
                ' Sin la trayectoria ese reporte no es diagnosticable.
                log($"Wall clock: {sw.Elapsed.TotalSeconds:F1} s · concurrency {If(fixedDop > 0, $"FIXED {fixedDop}", $"adaptive, peak {peakPermits} of {hardCap}: " & String.Join(" → ", traj))}")
                log("Phase TOTAL below is ACCUMULATED CPU, not wall: TOTAL/wall = effective threads.")
                log(FaceGenBuilder.PhaseReport())
            End If
            ' Paridad CPU-vs-GPU: SOLO en corridas de MEDICION.
            ' ⛔ Antes se imprimia SIEMPRE. El motivo declarado era bueno —que nadie leyera un barrido CPU-only
            ' como si hubiera validado el compositor del render— pero la conclusion era la equivocada: en un
            ' bake de PRODUCCION nadie pidio medir paridad, asi que escupir dos bloques de "NOT MEASURED — this
            ' run says NOTHING about the GPU path" es ruido de diagnostico en la cara del usuario. Y encima
            ' induce a error: sugiere que falto algo, cuando en realidad no se pidio nada.
            ' El aviso hace falta donde el malentendido es POSIBLE: cuando alguien esta midiendo. Eso es
            ' `gpuParity` (pidio el instrumento ⇒ se le reporta el resultado, medido o no) o `wantStats`
            ' (FGBAKE_STATS=1 / Logger encendido ⇒ pidio diagnostico). Sin ninguno de los dos, silencio.
            ' ⛔ NO alcanzaba con gatear por `Logger.Enabled`: el arnes corre en RELEASE, donde Logger esta duro
            ' en False (Logger.Enabled = value AndAlso AllowInReleaseBuilds, y ninguna app lo prende) — por eso
            ' existe `wantStats`, que es la valvula pensada justamente para eso.
            If gpuParity OrElse wantStats Then
                log("")
                log(FaceGenBuilder.ParityReport())
                log("")
                log(SseFoldLayerStack.SseParityReport())
            End If
            ' El aviso de "el bucket Swap no gobierna el acumulador" se latchea en la libreria y hasta ahora
            ' NADIE lo leia: la propiedad y su reset existian sin un solo consumidor, mientras tres comentarios
            ' afirmaban que este runner los usaba. Se imprime por `log()` (sale tambien en release, que es el
            ' punto: `Logger` esta apagado ahi) y solo cuando hubo caso.
            Dim swapWarn = FaceTintConvention.SwapAccumWarning
            If Not String.IsNullOrEmpty(swapWarn) Then
                log("")
                log("⚠ CONVENTION: " & swapWarn)
            End If
            ' Mismo criterio para la guarda de uniforms del compositor GL: escribir en una location -1 es un
            ' no-op MUDO, así que si alguna falta hay que VERLO. Latcheado en la librería, impreso acá.
            Dim uniWarn = FaceTintCompositor.UniformsMissingWarning
            If Not String.IsNullOrEmpty(uniWarn) Then
                log("")
                log("⚠ SHADER: " & uniWarn)
            End If
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
