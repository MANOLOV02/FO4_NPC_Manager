Imports System.IO
Imports System.Linq
Imports System.Runtime.InteropServices
Imports System.Windows.Forms
Imports FO4_Base_Library

Module Program

    ' ============================================================================================
    ' Console attach (WinExe)
    ' ============================================================================================
    ' The project is <OutputType>WinExe</OutputType>, so Windows gives it NO console: every
    ' Console.WriteLine from a headless mode goes nowhere when launched from a terminal (it only
    ' materialises if the caller redirects stdout to a file). AttachConsole(-1) binds us to the
    ' PARENT console (the cmd/PowerShell that launched us) so the output shows up where the user is
    ' looking. If there's no parent console (double-clicked, or launched detached), AllocConsole
    ' gives us our own window so a headless run is never silent.
    Private Const ATTACH_PARENT_PROCESS As Integer = -1

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function AttachConsole(dwProcessId As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function AllocConsole() As Boolean
    End Function

    ''' <summary>Bind stdout/stderr to a real console for the headless modes. Reopens the streams
    ''' AFTER attaching: the BCL caches Console.Out on first touch, and a WinExe's cached handle is a
    ''' null device, so writing without this re-open stays invisible even once attached.</summary>
    Private Sub EnsureConsole()
        If Not AttachConsole(ATTACH_PARENT_PROCESS) Then AllocConsole()
        Try
            ' The log carries non-ASCII (…, →, —) and NPC names carry accents. A fresh Windows console
            ' is on a legacy OEM codepage (437/850), which renders UTF-8 bytes as mojibake ("ÔÇª"). Switch
            ' the console itself to UTF-8 (this calls SetConsoleOutputCP) and write UTF-8 without a BOM —
            ' both halves are needed: the codepage alone still leaves the writer on the default encoding.
            Dim utf8 As New Text.UTF8Encoding(encoderShouldEmitUTF8Identifier:=False)
            Try
                Console.OutputEncoding = utf8
            Catch
                ' Some hosts refuse the codepage switch (redirected pipes, odd terminals) — keep going;
                ' the writer below still emits valid UTF-8 bytes for anything that reads the stream.
            End Try
            Dim stdout = Console.OpenStandardOutput()
            If stdout IsNot Stream.Null Then
                Dim w As New StreamWriter(stdout, utf8) With {.AutoFlush = True}
                Console.SetOut(w)
            End If
            Dim stderr = Console.OpenStandardError()
            If stderr IsNot Stream.Null Then
                Dim w As New StreamWriter(stderr, utf8) With {.AutoFlush = True}
                Console.SetError(w)
            End If
        Catch
            ' No console could be bound (rare: service/session-0 context). Headless still runs; the
            ' caller just sees no output. Never fail the bake over the log sink.
        End Try
    End Sub

    <STAThread>
    Sub Main(args As String())
        ' --- HEADLESS bake-ALL mode ----------------------------------------------------------------
        ' NPC_Manager_FO4.exe --bake-all [--game fo4|sse] [--windowed]
        ' Bakes loose FaceGen (NIF + face textures) for EVERY NPC_ in the active load order — the exact
        ' equivalent of selecting every NPC in the tree and pressing "Build CharGen (loose)". Uses the
        ' PERSISTED config (game, Data path, resolutions, DDS codecs, FaceTint convention/sort, fixes):
        ' if that config can't be resolved, it fails loudly instead of guessing. See BakeAllRunner.
        If HasFlag(args, "--bake-all") Then
            HeadlessBakeAll(args)
            Return
        End If

        If HasFlag(args, "--help") OrElse HasFlag(args, "-h") OrElse HasFlag(args, "/?") Then
            EnsureConsole()
            PrintUsage()
            Return
        End If

        ' --- HEADLESS FaceGeom geometry-bake mode -------------------------------------------------
        ' NPC_Manager_FO4.exe --bake-geom <espName> <edidOrFormId> [--data <DataDir>] [--out <nifPath>]
        ' Detected BEFORE any WinForms / Preflight code: when present we run the bake on the CPU and
        ' Return — the GUI never opens, Application.Run is never called. The geometry bake is 100% CPU
        ' when Logger.Enabled stays False (forces FaceGenBuilder.DebugMode = False) and BuildCharGen is
        ' handed host:=Nothing (skips the GL face-texture readback). See HeadlessBakeGeom below.
        If args IsNot Nothing AndAlso args.Any(Function(a) String.Equals(a, "--bake-geom", StringComparison.OrdinalIgnoreCase)) Then
            HeadlessBakeGeom(args)
            Return
        End If

        ' --- HEADLESS slot-classification diagnostic (instrument-before-code, no render needed) ------
        ' NPC_Manager_FO4.exe --slot-diag  → runs the REAL NpcMeshCollector.ClassifyShapeCategory over
        ' synthetic per-slot masks for BOTH games, printing the resulting render category. Proves whether
        ' the FO4-hardcoded slot semantics misclassify SSE slots (e.g. Skyrim body slot 32 → Headwear).
        If args IsNot Nothing AndAlso args.Any(Function(a) String.Equals(a, "--slot-diag", StringComparison.OrdinalIgnoreCase)) Then
            RunSlotDiag()
            Return
        End If

        ' --- HEADLESS skin-region equivalence check --------------------------------------------------
        ' NPC_Manager_FO4.exe --skinregion-diag  → prueba EXHAUSTIVA de que la vieja heurística por
        ' CATEGORÍA del fast-path de piel (NpcSkinLivePreview) coincide con la LEY del render completo
        ' (NpcMaterialResolver.ResolveSkinRegionForOutfit). Exit 0 = equivalentes, 4 = hay divergencia.
        ' Existe porque ese fast-path SÓLO lo instancia MainForm ⇒ el arnés de NIFs del CLI NO lo toca.
        If args IsNot Nothing AndAlso args.Any(Function(a) String.Equals(a, "--skinregion-diag", StringComparison.OrdinalIgnoreCase)) Then
            Environment.ExitCode = RunSkinRegionDiag()
            Return
        End If

        ' HighDpiMode = DpiUnaware: Windows hace bitmap-scaling de la ventana
        ' al DPI del monitor. UI luce algo blurry a >100% pero el LAYOUT es
        ' idéntico a cualquier DPI — fonts/controles no se reescalan, así
        ' las proporciones del header vs preview no cambian. Para usar
        ' PerMonitorV2 hay que primero hacer que el GLControl cree
        ' backbuffer en pixels físicos (no soportado en la versión actual
        ' de OpenTK).
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Config_App.LoadConfig()
        NPC_Config.LoadConfig()
        ' Ley opt-in del CK (blend de skin NO normalizado) — gate por juego. Se re-aplica al finalizar
        ' el selector de juego del Preflight y al aceptar CharGen Options. Ver NPC_Config.ApplyEngineSkinWeightNormalizationGate.
        NPC_Config.ApplyEngineSkinWeightNormalizationGate(Config_App.Current.Game)
        ' Game is NO LONGER pinned to Fallout4 here — it comes from the persisted config (last session's
        ' choice) and is finalized by the user in Preflight_Form's game selector. The encoding init below
        ' uses this persisted default; Preflight re-initializes encoding for the chosen game before it
        ' loads any plugin (see Preflight_Form.ButtonOk_Click), so a mid-dialog game switch stays correct.
        ' NPC render RELIES on per-segment occlusion (Pip-Boy 60/160 swap, head-part hiding). The shared
        ' "draw hidden segments" toggle defaults True (WM inspection default) — force it OFF here so NPC
        ' always occludes. NPC has no UI for it; this is unconditional.
        Config_App.Current.Setting_DrawHiddenSegments = False

        ' Logger live BEFORE encoding init / preflight so every startup-time LogLazy is captured
        ' (encoding override INI, TES4 SNAM parse, plugin scan). Was in MainForm_Load — moved
        ' here because MainForm_Load runs AFTER the preflight has already loaded plugins.
        ' Logger habilitado SOLO en Debug builds. En Release: Logger.Enabled stays default (False)
        ' y todos los Logger.Log/LogLazy retornan early sin allocar — sin overhead. Si necesitás
        ' diagnóstico en Release, descomentar manualmente y rebuild.
#If DEBUG Then
        Logger.Enabled = True
        Logger.Initialize(IO.Path.Combine(Application.StartupPath, "fo4lib.log"))
#End If

        ' Plugin text encoding MUST be configured BEFORE any plugin is loaded — mirror of xEdit's
        ' order: xeInit configures wbEncodingTrans (from sLanguage) before TwbFile loads. The
        ' preflight below loads + scans all plugins; even though FULL/EDID parsing is lazy, doing
        ' this here guarantees every decode (eager or lazy, preflight or later) uses the correct
        ' encoding from the start. Process model = xEdit: configure → load all → edit.
        PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
        PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())
        ' OverridePluginEncoding.ini (optional, appdir): user escape hatch for cases where the
        ' game language and the plugin encoding diverge — canonical case is Korean FO4
        ' (sLanguage=en + fan-translated UTF-8 plugins). File-based mirror of xEdit's -cp-trans.
        PluginEncodingSettings.ApplyOverrideIni(AppDomain.CurrentDomain.BaseDirectory)

        Using preflight As New Preflight_Form()
            If preflight.ShowDialog() <> DialogResult.OK Then Return
            Application.Run(New MainForm(preflight.LoadedPluginManager,
                                         preflight.LoadedDataPath,
                                         preflight.LoadedAutoGenPlugins,
                                         preflight.LoadedSidecars))
        End Using
    End Sub

    ' ============================================================================================
    ' HEADLESS bake-all (--bake-all)
    ' ============================================================================================

    ''' <summary>Entry for <c>--bake-all</c>. Shows the progress window by DEFAULT; <c>--windowless</c>
    ''' runs it on the console instead. Everything that shapes the output comes from the persisted config;
    ''' the sole override is <c>--executable</c>, which moves exe + Data + game together (they are one
    ''' setting — see BakeAllRunner.Options). Sets <see cref="Environment.ExitCode"/> from
    ''' <see cref="BakeAllRunner"/>: 0 = all baked (skips are not failures), 1 = config/load could not
    ''' be resolved (nothing baked), 2 = finished with per-NPC failures, 3 = cancelled.</summary>
    Private Sub HeadlessBakeAll(args As String())
        Dim opt As New BakeAllRunner.Options()
        opt.Windowless = HasFlag(args, "--windowless")
        opt.ExecutablePath = GetFlagValue(args, "--executable")
        opt.EspTarget = GetFlagValue(args, "--esptarget")

        If Not opt.Windowless Then
            Application.SetHighDpiMode(HighDpiMode.DpiUnaware)
            Application.EnableVisualStyles()
            Application.SetCompatibleTextRenderingDefault(False)
            Using f As New BakeAllProgress_Form(opt)
                Application.Run(f)
                Environment.ExitCode = f.ExitCode
            End Using
            Return
        End If

        EnsureConsole()
        Console.WriteLine("NPC Manager — headless bake of every NPC in the load order (loose FaceGen)")
        Console.WriteLine("=========================================================================")
        Console.WriteLine("(Ctrl-C = stop after the current NPC. Press it twice to abort immediately.)")
        Console.WriteLine("")

        ' GRACEFUL Ctrl-C. The default handler kills the process on the spot, which can land in the middle
        ' of writing a NIF/DDS and leave a truncated FaceGen on disk. Cancel the kill, raise the same flag
        ' the windowed Cancel button raises, and let the bake loop finish the NPC it is on and print its
        ' summary. A SECOND Ctrl-C is left unhandled — that's the escape hatch for a wedged run.
        Dim cancelRequested As Integer = 0
        AddHandler Console.CancelKeyPress,
            Sub(sender As Object, e As ConsoleCancelEventArgs)
                If Threading.Interlocked.Exchange(cancelRequested, 1) = 0 Then
                    e.Cancel = True   ' first press: don't kill; the loop stops at the next NPC boundary
                    Console.WriteLine("")
                    Console.WriteLine("Cancelling — finishing the current NPC… (Ctrl-C again to abort now)")
                End If
            End Sub

        Environment.ExitCode = BakeAllRunner.Run(
            opt,
            log:=Sub(line) Console.WriteLine(line),
            progress:=Nothing,       ' console shows progress inline in the per-NPC lines
            isCancelled:=Function() Threading.Volatile.Read(cancelRequested) <> 0)
    End Sub

    ''' <summary>True if <paramref name="name"/> appears in <paramref name="args"/> (case-insensitive).</summary>
    Private Function HasFlag(args As String(), name As String) As Boolean
        If args Is Nothing Then Return False
        Return args.Any(Function(a) String.Equals(a, name, StringComparison.OrdinalIgnoreCase))
    End Function

    ''' <summary>Value token following <paramref name="name"/>, or "" when absent / last.</summary>
    Private Function GetFlagValue(args As String(), name As String) As String
        If args Is Nothing Then Return ""
        For i = 0 To args.Length - 2
            If String.Equals(args(i), name, StringComparison.OrdinalIgnoreCase) Then Return args(i + 1)
        Next
        Return ""
    End Function

    Private Sub PrintUsage()
        Console.WriteLine("NPC_Manager_FO4.exe — command line modes")
        Console.WriteLine("")
        Console.WriteLine("  --bake-all [--windowless] [--executable <path to game .exe>] [--esptarget <plugin>]")
        Console.WriteLine("      Bake loose FaceGen (NIF + face textures) for EVERY NPC in the active load")
        Console.WriteLine("      order — same as selecting them all and pressing 'Build CharGen (loose)'.")
        Console.WriteLine("      Shows a progress window with a live log by default.")
        Console.WriteLine("      Everything comes from the saved config: game, Data path, resolutions, DDS")
        Console.WriteLine("      formats, FaceTint convention and sort order, fixes. Fails loudly if the")
        Console.WriteLine("      config can't be resolved.")
        Console.WriteLine("      --windowless   no window: log to the console (Ctrl-C stops after the")
        Console.WriteLine("                     current NPC; press twice to abort immediately)")
        Console.WriteLine("      --executable   full path to a game exe (Fallout4.exe / SkyrimSE.exe). Runs")
        Console.WriteLine("                     against THAT install: it sets the Data folder and the game")
        Console.WriteLine("                     together, for this run only. config.json is not modified.")
        Console.WriteLine("      --esptarget    bake ONLY the NPCs of this plugin (e.g. MyFollower.esp) and")
        Console.WriteLine("                     skip every other NPC. Counts the NPCs the plugin adds AND the")
        Console.WriteLine("                     ones it overrides. The rest of the load order is still loaded.")
        Console.WriteLine("      Exit: 0 = all baked, 1 = config/load failed, 2 = some NPCs failed, 3 = cancelled")
        Console.WriteLine("")
        Console.WriteLine("  --bake-geom <espName> <edidOrFormId> [--data <Dir>] [--out <nif>]")
        Console.WriteLine("      Bake ONE NPC's head geometry (no textures). Diagnostic.")
        Console.WriteLine("")
        Console.WriteLine("  --slot-diag")
        Console.WriteLine("      Print how each body slot classifies in both games. Diagnostic.")
        Console.WriteLine("")
        Console.WriteLine("  --skinregion-diag")
        Console.WriteLine("      Exhaustively check that the skin fast-path's category heuristic agrees with")
        Console.WriteLine("      ResolveSkinRegionForOutfit, both games. Exit: 0 = equivalent, 4 = divergence.")
        Console.WriteLine("")
        Console.WriteLine("  (no flags)  Launch the GUI.")
    End Sub

    ''' <summary>Headless classification diagnostic. For each game, runs the REAL
    ''' <see cref="NpcMeshCollector.ClassifyShapeCategory"/> over one-bit slot masks (and a realistic
    ''' multi-slot cuirass) so the FO4-vs-SSE slot-semantic mismatch is observable without a render.
    ''' Expected SSE (xEdit): 30=Head,31=Hair,32=Body,33=Hands,34=Forearms,37=Feet,41=LongHair,42=Circlet.</summary>
    Private Sub RunSlotDiag()
        EnsureConsole()
        Config_App.LoadConfig()
        NPC_Config.LoadConfig()
        ' Skyrim: los slots 39/40 y los "unnamed" 44+ son el grupo ACCESORIOS (→ ArmorOver, rotulado
        ' "Render accessories"); en FO4 esos mismos bits son [U] R Leg / [A] Torso / [A] L Leg → otra cosa.
        Dim cases = New (Name As String, SlotN As Integer)() {
            ("head", 30), ("hair", 31), ("body", 32), ("hands", 33), ("forearms", 34),
            ("amulet", 35), ("ring", 36), ("feet", 37), ("calves", 38), ("shield", 39), ("tail", 40),
            ("longhair", 41), ("circlet", 42), ("ears", 43), ("mod44", 44), ("mod60", 60)
        }
        For Each game In {Config_App.Game_Enum.Fallout4, Config_App.Game_Enum.Skyrim}
            Config_App.Current.Game = game
            Console.WriteLine($"===================== {game} =====================")
            For Each cse In cases
                Dim mask As UInteger = 1UI << (cse.SlotN - 30)
                Dim outfitCat = NpcMeshCollector.ClassifyShapeCategory(New MainForm.MeshCandidate With {.SlotMask = mask, .Kind = MainForm.MeshCandidateKind.Outfit})
                Dim skinCat = NpcMeshCollector.ClassifyShapeCategory(New MainForm.MeshCandidate With {.SlotMask = mask, .Kind = MainForm.MeshCandidateKind.Skin})
                Console.WriteLine($"  slot {cse.SlotN,2} ({cse.Name,-9}) bit{cse.SlotN - 30,2}  Outfit->{outfitCat,-11}  Skin->{skinCat}")
            Next
            ' Realistic Skyrim full-body cuirass: covers body(32)+hands(33)+forearms(34) = bits 2,3,4.
            Dim cuirass As UInteger = (1UI << 2) Or (1UI << 3) Or (1UI << 4)
            Console.WriteLine($"  Skyrim cuirass 32+33+34   Outfit->{NpcMeshCollector.ClassifyShapeCategory(New MainForm.MeshCandidate With {.SlotMask = cuirass, .Kind = MainForm.MeshCandidateKind.Outfit})}")
            ' Body skin (naked body) SSE = slot 32; naked hands SSE = slot 33.
            Console.WriteLine($"  SSE naked body   (32)     Skin->{NpcMeshCollector.ClassifyShapeCategory(New MainForm.MeshCandidate With {.SlotMask = 1UI << 2, .Kind = MainForm.MeshCandidateKind.Skin})}")
            Console.WriteLine($"  SSE naked hands  (33)     Skin->{NpcMeshCollector.ClassifyShapeCategory(New MainForm.MeshCandidate With {.SlotMask = 1UI << 3, .Kind = MainForm.MeshCandidateKind.Skin})}")
        Next
    End Sub

    ''' <summary>Chequeo EXHAUSTIVO y reproducible de la equivalencia entre las dos formas de derivar
    ''' la región de piel (Body vs Hand) de un shape de outfit:
    '''   • LEY   = <see cref="NpcMaterialResolver.ResolveSkinRegionForOutfit"/>, lo que usa el render
    '''             completo (y ahora también el fast-path del picker de piel).
    '''   • VIEJA = la heurística por categoría que tenía NpcSkinLivePreview antes
    '''             (GloveOutfit → Hand, cualquier otra categoría de outfit → Body).
    '''
    ''' POR QUÉ ESTE CHECK Y NO EL BARRIDO: el fast-path vive en NpcSkinLivePreview, que SÓLO instancia
    ''' MainForm ⇒ es inalcanzable desde FO4_FaceTint_CLI y su efecto es sólo-render. Un barrido verde
    ''' de NIFs no dice NADA sobre este código. Lo que sí se puede medir es que las dos derivaciones
    ''' coinciden, porque ambas son funciones PURAS de (SlotMask, Kind).
    '''
    ''' POR QUÉ ES EXHAUSTIVO Y NO UN MUESTREO DE OUTFITS: las dos funciones NO miran bits sueltos.
    ''' Sólo consultan (a) si el mask intersecta cada máscara de región (BipedSlots.RegionMask), (b) el
    ''' bit Pipboy, y (c) si el mask es 0. Entonces su comportamiento depende ÚNICAMENTE del vector de
    ''' "toca / no toca" por región + Pipboy + non-zero. Enumerando todos los subconjuntos de un bit
    ''' REPRESENTATIVO por región (extraído de RegionMask en runtime — NO hardcodeado) × los 4 Kind ×
    ''' los 2 juegos se cubre TODO ese espacio, no una muestra sesgada del corpus vanilla.
    ''' Las regiones sin slots en un juego (p.ej. Under/[U] no existe en SSE) se saltean solas: su
    ''' RegionMask da 0 y no aportan bit representativo.</summary>
    ''' <returns>0 si las dos derivaciones coinciden en todos los casos; 4 si hay alguna divergencia.</returns>
    Private Function RunSkinRegionDiag() As Integer
        EnsureConsole()
        Config_App.LoadConfig()
        NPC_Config.LoadConfig()

        ' Categorías de outfit sobre las que el fast-path itera (ApplyOutfitSkinTintRefreshAfterBodySkinChange).
        ' Las de body skin (BodySkin/NakedHands) y HeadPart las manejan los otros dos pases y NO entran acá.
        Dim outfitCats = {MainForm.ShapeRenderCategory.Underarmor, MainForm.ShapeRenderCategory.ArmorOver,
                          MainForm.ShapeRenderCategory.GloveOutfit, MainForm.ShapeRenderCategory.Headwear}
        Dim regions = {BipedSlots.BipedRegion.Headwear, BipedSlots.BipedRegion.Body, BipedSlots.BipedRegion.Hands,
                       BipedSlots.BipedRegion.Under, BipedSlots.BipedRegion.Over, BipedSlots.BipedRegion.Accessory}
        Dim kinds = {MainForm.MeshCandidateKind.Skin, MainForm.MeshCandidateKind.Outfit,
                     MainForm.MeshCandidateKind.HeadPart, MainForm.MeshCandidateKind.Attachment}

        Dim totalChecked As Integer = 0, totalReachable As Integer = 0, mismatches As Integer = 0
        Dim savedGame = Config_App.Current.Game
        For Each game In {Config_App.Game_Enum.Fallout4, Config_App.Game_Enum.Skyrim}
            Config_App.Current.Game = game
            Console.WriteLine($"===================== {game} =====================")

            ' Un bit representativo por región, LEÍDO de la tabla autoritativa (bit más bajo de la
            ' máscara). Si la región no existe en este juego su máscara es 0 → no aporta bit.
            Dim repBits As New List(Of (Region As BipedSlots.BipedRegion, Bit As UInteger))
            For Each rg In regions
                Dim m = BipedSlots.RegionMask(rg)
                If m = 0UI Then
                    Console.WriteLine($"  (región {rg} no tiene slots en {game} — omitida)")
                    Continue For
                End If
                ' Bit más bajo prendido de la máscara. Loop explícito a propósito: en VB el operador
                ' Not tiene MENOS precedencia que la aritmética, así que el idiom `m And (Not m + 1)`
                ' se parsea como `m And Not (m + 1)` y da cualquier cosa.
                Dim low As UInteger = 0UI
                For bi As Integer = 0 To 31
                    If (m And (1UI << bi)) <> 0UI Then
                        low = 1UI << bi
                        Exit For
                    End If
                Next
                repBits.Add((rg, low))
            Next
            ' El bit Pipboy es el único slot que ClassifyShapeCategory consulta INDIVIDUALMENTE
            ' (regla FO4 slot 60), así que entra como dimensión propia del producto cartesiano.
            repBits.Add((BipedSlots.BipedRegion.None, BipedSlots.SlotBitPipboy))

            Dim combos As Integer = 1 << repBits.Count
            For subset As Integer = 0 To combos - 1
                Dim mask As UInteger = 0UI
                For b As Integer = 0 To repBits.Count - 1
                    If (subset And (1 << b)) <> 0 Then mask = mask Or repBits(b).Bit
                Next
                For Each kind In kinds
                    Dim cand = New MainForm.MeshCandidate With {.SlotMask = mask, .Kind = kind}
                    Dim cat = NpcMeshCollector.ClassifyShapeCategory(cand)
                    totalChecked += 1
                    ' Sólo son ALCANZABLES por el fast-path los shapes cuya categoría está en el filtro.
                    If Not outfitCats.Contains(cat) Then Continue For
                    totalReachable += 1

                    Dim viejaHeuristica = If(cat = MainForm.ShapeRenderCategory.GloveOutfit,
                                             MainForm.SkinRegion.Hand, MainForm.SkinRegion.Body)
                    Dim ley = NpcMaterialResolver.ResolveSkinRegionForOutfit(cand)
                    If viejaHeuristica <> ley Then
                        mismatches += 1
                        Console.WriteLine($"  MISMATCH  mask=0x{mask:X8} kind={kind,-10} cat={cat,-11} " &
                                          $"heuristica={viejaHeuristica} LEY={ley}")
                    End If
                Next
            Next
            Console.WriteLine($"  combos de región = {combos} × {kinds.Length} kinds")
        Next
        Config_App.Current.Game = savedGame

        Console.WriteLine("")
        Console.WriteLine($"casos evaluados = {totalChecked} · alcanzables por el fast-path = {totalReachable} · divergencias = {mismatches}")
        If mismatches = 0 Then
            Console.WriteLine("OK — la heurística por categoría y ResolveSkinRegionForOutfit son EQUIVALENTES")
            Console.WriteLine("     sobre todo el espacio alcanzable. El cambio en NpcSkinLivePreview es un")
            Console.WriteLine("     no-op de comportamiento: elimina la ley duplicada, no arregla un defecto.")
            Return 0
        End If
        Console.WriteLine("DIVERGENCIA — la heurística por categoría NO reproduce la ley. Ver líneas MISMATCH.")
        Return 4
    End Function

    ' ============================================================================================
    ' HEADLESS FaceGeom geometry bake
    ' ============================================================================================

    ''' <summary>Headless entry for `--bake-geom`. Bootstraps the library exactly like
    ''' FO4_FaceTint_CLI (config → encoding → plugins → archives) but then calls the SAME
    ''' FaceGenBuilder.BuildCharGen the GUI uses — with host:=Nothing and Logger.Enabled left
    ''' False, so it takes the pure-CPU geometry path (no GL context). BuildCharGen writes the
    ''' baked head .nif to the canonical FaceGeom path itself; with --out we additionally copy it
    ''' there for side-by-side comparison without clobbering. Never opens a window.</summary>
    Private Sub HeadlessBakeGeom(args As String())
        ' Keep Logger disabled: FaceGenBuilder.DebugMode = Logger.Enabled. False = pure-CPU bake
        ' (canonical <id>.NIF, no GL readback, no comparator). Enabling it would force the GL path
        ' which needs a live OpenGL context we don't have headless. Do NOT enable it here.
        Try
            EnsureConsole()

            ' --- 0. Parse args: positional <espName> <edidOrFormId> after --bake-geom, plus --data / --out.
            Dim espName As String = ""
            Dim edidOrId As String = ""
            Dim dataOverride As String = ""
            Dim outPath As String = ""
            Dim positionals As New List(Of String)
            Dim i As Integer = 0
            While i < args.Length
                Dim a = args(i)
                Select Case a.ToLowerInvariant()
                    Case "--bake-geom"
                        ' positionals consumed below from the two following non-flag tokens
                    Case "--data"
                        i += 1 : If i < args.Length Then dataOverride = args(i)
                    Case "--out"
                        i += 1 : If i < args.Length Then outPath = args(i)
                    Case Else
                        If Not a.StartsWith("--") Then positionals.Add(a)
                End Select
                i += 1
            End While
            If positionals.Count >= 1 Then espName = positionals(0)
            If positionals.Count >= 2 Then edidOrId = positionals(1)
            If String.IsNullOrWhiteSpace(espName) OrElse String.IsNullOrWhiteSpace(edidOrId) Then
                Console.Error.WriteLine("Usage: NPC_Manager_FO4.exe --bake-geom <espName> <edidOrFormId> [--data <DataDir>] [--out <nifPath>]")
                Environment.ExitCode = 1 : Return
            End If

            ' --- 1. Config. Game = the PERSISTED selector (it used to be hard-forced to Fallout4 here,
            '        which silently mis-read a Skyrim Data folder: NPC_/RACE byte layouts differ). There
            '        is no --game override on purpose — the game and the exe/Data path are ONE setting
            '        (Config_App.FO4ExePath), so overriding the game alone would just produce a
            '        game/exe mismatch. Data path from --data else config.json (FO4EDataPath). ---
            Config_App.LoadConfig()
            NPC_Config.LoadConfig()
            Console.WriteLine($"[cfg] game={Config_App.Current.Game}")
            Config_App.Current.Setting_DrawHiddenSegments = False ' NPC needs per-segment occlusion (see Main path)

            Dim dataPath As String = If(dataOverride <> "", dataOverride, Config_App.Current.FO4EDataPath)
            If String.IsNullOrEmpty(dataPath) OrElse Not Directory.Exists(dataPath) Then
                Console.Error.WriteLine($"FATAL: invalid Data path: '{dataPath}'. Pass --data <path to Data\> or fix config.json.")
                Environment.ExitCode = 1 : Return
            End If

            ' BuildCharGen writes the .nif under Config_App.Current.DataPath (derived from FO4ExePath).
            ' When --data overrides the load path, point FO4ExePath at the Fallout4.exe next to that
            ' Data dir so DataPath tracks the requested location. Best-effort: if that exe isn't there
            ' Check_FOFolder() fails and DataPath returns "" → BuildCharGen would bail "DataPath unset".
            ' In that case we keep the config's FO4ExePath (DataPath stays the configured path); the
            ' bake still lands under the configured Data\FaceGeom, and --out gives the caller the file
            ' wherever they want it.
            If dataOverride <> "" Then
                Dim exeName = If(Config_App.Current.Game = Config_App.Game_Enum.Skyrim, "SkyrimSE.exe", "Fallout4.exe")
                Dim guessedExe = Path.Combine(Directory.GetParent(dataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).FullName, exeName)
                If File.Exists(guessedExe) Then
                    Config_App.Current.FO4ExePath = guessedExe
                    Console.WriteLine($"[cfg] FO4ExePath -> {guessedExe} (so that DataPath = {dataPath})")
                Else
                    Console.WriteLine($"[warn] --data was given but there is no {exeName} next to it ({guessedExe}); the NIF will be written under Config_App.DataPath='{Config_App.Current.DataPath}'.")
                End If
            End If
            Console.WriteLine($"[cfg] dataPath(load)={dataPath}")
            Console.WriteLine($"[cfg] DataPath(write)={Config_App.Current.DataPath}")

            ' --- 2. Encoding (mismo orden que el exe / la FaceTint CLI: antes de cargar plugins).
            '        Incluye el escape hatch OverridePluginEncoding.ini que el path GUI aplica. ---
            PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
            PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())
            PluginEncodingSettings.ApplyOverrideIni(AppDomain.CurrentDomain.BaseDirectory)

            ' --- 3. Plugins: load order activa + el esp pedido (y sus masters) si no esta activo.
            '        sigFilter = Nothing -> carga TODAS las firmas (el geometry bake necesita
            '        HDPT/RACE/ARMO/ARMA/OTFT/etc.). ---
            Console.WriteLine("[load] parsing plugins…")
            Dim pm As New PluginManager()
            Dim loadList = PluginManager.ReadActiveLoadOrder()
            EnsureEspInLoadList(loadList, espName, dataPath)
            pm.LoadAllPlugins(dataPath, loadList, Nothing, Nothing)

            ' --- 4. Archivos (BA2 + loose). Cache dir bajo el exe. ---
            Console.WriteLine("[load] mounting archives (BA2/BSA + loose)…")
            Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
            Directory.CreateDirectory(cacheDir)
            FilesDictionary_class.CacheDirectory = cacheDir
            ' progress NO puede ser Nothing: Fill_DictionaryAsync hace progress.Report(...) sin guard.
            Dim noProg As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))()
            FilesDictionary_class.Fill_DictionaryAsync(dataPath, noProg).GetAwaiter().GetResult()

            ' --- 5. Resolver el FormID del NPC desde <espName> + <edidOrFormId>. ---
            Dim npcFormID = ResolveEdid(pm, espName, edidOrId)
            If npcFormID = 0UI Then
                Console.Error.WriteLine($"FATAL: could not resolve NPC '{edidOrId}' provided by '{espName}'.")
                Environment.ExitCode = 1 : Return
            End If
            Console.WriteLine($"[npc] resolved {edidOrId} -> 0x{npcFormID:X8} (origin='{pm.GetOriginatingPluginName(npcFormID)}')")

            ' --- 6. Bake. SAME BuildCharGen the GUI uses. host:=Nothing -> sin GL face-texture bake
            '        (la geometria es identica; solo se omite la composicion D/N/S de la cara, que es
            '        el TEXTURE bake, no el GEOMETRY bake). applyMaterialOverrides:=Nothing -> el
            '        resolver de material por-shape se saltea (devuelve Nothing). appliedPresets vacio.
            '        willBePacked:=False. lmSkinTemplateResolver:=Nothing. DebugMode=False (Logger off)
            '        -> 100% CPU, escribe el <id>.NIF canonico. ---
            Console.WriteLine("[bake] BuildCharGen (CPU)...")
            Dim emptyPresets As New Dictionary(Of UInteger, LooksmenuLoader.LooksmenuPreset)()
            Dim result = FaceGenBuilder.BuildCharGen(npcFormID,
                                                     pm,
                                                     emptyPresets,
                                                     host:=Nothing,
                                                     applyMaterialOverrides:=Nothing,
                                                     willBePacked:=False,
                                                     lmSkinTemplateResolver:=Nothing)

            Console.WriteLine($"[bake] Success={result.Success} Skipped={result.Skipped}")
            Console.WriteLine($"[bake] {result.Summary}")

            If result.Skipped Then
                Console.WriteLine("[bake] NPC has no FaceGen head parts — nothing to bake (skip).")
                Environment.ExitCode = 1 : Return
            End If
            If Not result.Success OrElse String.IsNullOrEmpty(result.OutputPath) OrElse Not File.Exists(result.OutputPath) Then
                Console.Error.WriteLine("[bake] BuildCharGen did not write the NIF.")
                Environment.ExitCode = 1 : Return
            End If

            Dim fi As New FileInfo(result.OutputPath)
            Console.WriteLine($"[ok] NIF -> {result.OutputPath}")
            Console.WriteLine($"[ok]   size={fi.Length} bytes  lastWrite={fi.LastWriteTime:yyyy-MM-dd HH:mm:ss.fff}")

            ' --- 7. --out: copiar el NIF producido a la ruta pedida (sin pisar el canonico). ---
            If outPath <> "" Then
                Try
                    Dim outDir = Path.GetDirectoryName(Path.GetFullPath(outPath))
                    If Not String.IsNullOrEmpty(outDir) Then Directory.CreateDirectory(outDir)
                    File.Copy(result.OutputPath, outPath, overwrite:=True)
                    Console.WriteLine($"[ok] copy -> {outPath}")
                Catch ex As Exception
                    Console.Error.WriteLine($"[warn] could not copy to --out '{outPath}': {ex.GetType().Name}: {ex.Message}")
                End Try
            End If

        Catch ex As Exception
            Console.Error.WriteLine("FATAL: " & ex.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    ''' <summary>Asegura que el esp este en la load list (si NO esta en el load order activo): lee sus
    ''' masters (TES4 MAST) y los agrega antes, luego agrega el esp. Se carga ULTIMO -> su override gana.
    ''' (Copia del helper de FO4_FaceTint_CLI.)</summary>
    Private Sub EnsureEspInLoadList(loadList As List(Of String), espName As String, dataPath As String)
        If loadList.Any(Function(p) String.Equals(p, espName, StringComparison.OrdinalIgnoreCase)) Then Return
        Dim espFull = Path.Combine(dataPath, espName)
        If Not File.Exists(espFull) Then
            Console.Error.WriteLine($"[warn] esp '{espName}' does not exist in {dataPath}; skipped.") : Return
        End If
        Dim probe As New PluginReader() : probe.Load(espFull)
        For Each m In probe.Masters
            If Not loadList.Any(Function(p) String.Equals(p, m, StringComparison.OrdinalIgnoreCase)) Then loadList.Add(m)
        Next
        loadList.Add(espName)
        Console.WriteLine($"[load] +INACTIVE esp '{espName}' (masters: {String.Join(", ", probe.Masters)})")
    End Sub

    ''' <summary>Itera AllRecords (key = FormID global), filtra NPC_ por EditorID (case-insensitive) o
    ''' por FormID hex y confirma que el RECORD GANADOR provenga del esp dado (rec.SourcePluginName) --
    ''' cubre tanto "el esp ORIGINA el record" como "el esp lo OVERRIDEA" (cargado ultimo -> gana). 0 si
    ''' no hay match (con fallback a un unico match en otro plugin). (Copia del helper de FO4_FaceTint_CLI.)</summary>
    Private Function ResolveEdid(pm As PluginManager, esp As String, edid As String) As UInteger
        Dim hexId As UInteger
        If TryHexId(edid, hexId) Then
            ' Match on the FaceGen-local FormID of BOTH sides so ESL (0xFE) FormIDs compare correctly:
            ' the record key is reduced via ToFaceGenLocalFormID, so the wanted hex must be too (a plain
            ' 24-bit mask never matches an ESL record, whose local id is the 12-bit object id).
            Dim want = PluginManager.ToFaceGenLocalFormID(hexId)
            Dim hexFallback As UInteger = 0UI, hexFallbackCount As Integer = 0
            For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
                Dim r = kv.Value
                If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
                If PluginManager.ToFaceGenLocalFormID(kv.Key) <> want Then Continue For
                If String.Equals(r.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then Return kv.Key
                hexFallback = kv.Key : hexFallbackCount += 1
            Next
            If hexFallbackCount = 1 Then
                Console.WriteLine($"[warn] FormID '{edid}' is not provided by '{esp}' but matched exactly one record in another plugin; using 0x{hexFallback:X8}.")
                Return hexFallback
            End If
            Return 0UI
        End If
        Dim fallback As UInteger = 0UI, fallbackCount As Integer = 0
        For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
            Dim rec = kv.Value
            If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Continue For
            If Not String.Equals(rec.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Continue For
            If String.Equals(rec.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then Return kv.Key
            fallback = kv.Key : fallbackCount += 1
        Next
        If fallbackCount = 1 Then
            Console.WriteLine($"[warn] EDID '{edid}' is not provided by '{esp}' but matched exactly one record in another plugin; using 0x{fallback:X8}.")
            Return fallback
        End If
        Return 0UI
    End Function

    ''' <summary>True si s es un FormID hex (prefijo 0x o exactamente 8 digitos hex) -> val = el FormID.
    ''' Asi un EDID alfanumerico NO se confunde con un FormID. (Copia del helper de FO4_FaceTint_CLI.)</summary>
    Private Function TryHexId(s As String, ByRef val As UInteger) As Boolean
        Dim t = If(s, "").Trim()
        Dim prefixed = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        If prefixed Then t = t.Substring(2)
        If Not (prefixed OrElse t.Length = 8) Then Return False
        If t.Length = 0 OrElse t.Length > 8 Then Return False
        For Each c In t
            If Not Uri.IsHexDigit(c) Then Return False
        Next
        val = Convert.ToUInt32(t, 16)
        Return True
    End Function

End Module
