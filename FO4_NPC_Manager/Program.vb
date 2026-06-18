Imports System.IO
Imports System.Linq
Imports System.Windows.Forms
Imports FO4_Base_Library

Module Program
    <STAThread>
    Sub Main(args As String())
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
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4

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
                Console.Error.WriteLine("Uso: NPC_Manager_FO4.exe --bake-geom <espName> <edidOrFormId> [--data <DataDir>] [--out <nifPath>]")
                Environment.ExitCode = 1 : Return
            End If

            ' --- 1. Config. Game = FO4. Data path from --data else config.json (FO4EDataPath). ---
            Config_App.LoadConfig()
            NPC_Config.LoadConfig()
            Config_App.Current.Game = Config_App.Game_Enum.Fallout4

            Dim dataPath As String = If(dataOverride <> "", dataOverride, Config_App.Current.FO4EDataPath)
            If String.IsNullOrEmpty(dataPath) OrElse Not Directory.Exists(dataPath) Then
                Console.Error.WriteLine($"Data path invalido: '{dataPath}'. Usa --data <ruta a Data\> o configura config.json.")
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
                Dim guessedExe = Path.Combine(Directory.GetParent(dataPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).FullName, "Fallout4.exe")
                If File.Exists(guessedExe) Then
                    Config_App.Current.FO4ExePath = guessedExe
                    Console.WriteLine($"[cfg] FO4ExePath -> {guessedExe} (para que DataPath = {dataPath})")
                Else
                    Console.WriteLine($"[warn] --data dado pero no hay Fallout4.exe junto a el ({guessedExe}); el NIF se escribira bajo Config_App.DataPath='{Config_App.Current.DataPath}'.")
                End If
            End If
            Console.WriteLine($"[cfg] dataPath(load)={dataPath}")
            Console.WriteLine($"[cfg] DataPath(write)={Config_App.Current.DataPath}")

            ' --- 2. Encoding (mismo orden que el exe / la FaceTint CLI: antes de cargar plugins). ---
            PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
            PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())

            ' --- 3. Plugins: load order activa + el esp pedido (y sus masters) si no esta activo.
            '        sigFilter = Nothing -> carga TODAS las firmas (el geometry bake necesita
            '        HDPT/RACE/ARMO/ARMA/OTFT/etc.). ---
            Console.WriteLine("[load] plugins...")
            Dim pm As New PluginManager()
            Dim loadList = PluginManager.ReadActiveLoadOrder()
            EnsureEspInLoadList(loadList, espName, dataPath)
            pm.LoadAllPlugins(dataPath, loadList, Nothing, Nothing)

            ' --- 4. Archivos (BA2 + loose). Cache dir bajo el exe. ---
            Console.WriteLine("[load] montando archivos...")
            Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
            Directory.CreateDirectory(cacheDir)
            FilesDictionary_class.CacheDirectory = cacheDir
            FilesDictionary_class.RegisterExtensions(".ssf", ".sclp", ".hkx", ".hkt")
            ' progress NO puede ser Nothing: Fill_DictionaryAsync hace progress.Report(...) sin guard.
            Dim noProg As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))()
            FilesDictionary_class.Fill_DictionaryAsync(dataPath, noProg).GetAwaiter().GetResult()

            ' --- 5. Resolver el FormID del NPC desde <espName> + <edidOrFormId>. ---
            Dim npcFormID = ResolveEdid(pm, espName, edidOrId)
            If npcFormID = 0UI Then
                Console.Error.WriteLine($"No se pudo resolver NPC '{edidOrId}' provisto por '{espName}'.")
                Environment.ExitCode = 1 : Return
            End If
            Console.WriteLine($"[npc] resuelto {edidOrId} -> 0x{npcFormID:X8} (origin='{pm.GetOriginatingPluginName(npcFormID)}')")

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
                Console.WriteLine("[bake] NPC sin head parts FaceGen — nada que hornear (skip).")
                Environment.ExitCode = 1 : Return
            End If
            If Not result.Success OrElse String.IsNullOrEmpty(result.OutputPath) OrElse Not File.Exists(result.OutputPath) Then
                Console.Error.WriteLine("[bake] BuildCharGen no escribio el NIF.")
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
                    Console.WriteLine($"[ok] copia -> {outPath}")
                Catch ex As Exception
                    Console.Error.WriteLine($"[warn] no se pudo copiar a --out '{outPath}': {ex.GetType().Name}: {ex.Message}")
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
            Console.Error.WriteLine($"[warn] esp '{espName}' no existe en {dataPath}; se saltea.") : Return
        End If
        Dim probe As New PluginReader() : probe.Load(espFull)
        For Each m In probe.Masters
            If Not loadList.Any(Function(p) String.Equals(p, m, StringComparison.OrdinalIgnoreCase)) Then loadList.Add(m)
        Next
        loadList.Add(espName)
        Console.WriteLine($"[load] +esp NO-activo '{espName}' (masters: {String.Join(", ", probe.Masters)})")
    End Sub

    ''' <summary>Itera AllRecords (key = FormID global), filtra NPC_ por EditorID (case-insensitive) o
    ''' por FormID hex y confirma que el RECORD GANADOR provenga del esp dado (rec.SourcePluginName) --
    ''' cubre tanto "el esp ORIGINA el record" como "el esp lo OVERRIDEA" (cargado ultimo -> gana). 0 si
    ''' no hay match (con fallback a un unico match en otro plugin). (Copia del helper de FO4_FaceTint_CLI.)</summary>
    Private Function ResolveEdid(pm As PluginManager, esp As String, edid As String) As UInteger
        Dim hexId As UInteger
        If TryHexId(edid, hexId) Then
            Dim want = hexId And &HFFFFFFUI
            Dim hexFallback As UInteger = 0UI, hexFallbackCount As Integer = 0
            For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
                Dim r = kv.Value
                If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
                If FaceGenLocalId(kv.Key) <> want Then Continue For
                If String.Equals(r.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then Return kv.Key
                hexFallback = kv.Key : hexFallbackCount += 1
            Next
            If hexFallbackCount = 1 Then
                Console.WriteLine($"[warn] FormID '{edid}' no provisto por '{esp}' pero unico match en otro plugin; usando 0x{hexFallback:X8}.")
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
            Console.WriteLine($"[warn] EDID '{edid}' no provisto por '{esp}' pero unico match en otro plugin; usando 0x{fallback:X8}.")
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

    ''' <summary>FormID local del FaceGen segun convencion CK: full plugins & 0xFFFFFF; ESL (high byte
    ''' 0xFE) & 0xFFF. (Copia del helper de FaceGenBuilder / FO4_FaceTint_CLI.)</summary>
    Private Function FaceGenLocalId(npcFormID As UInteger) As UInteger
        If (npcFormID >> 24) = &HFEUI Then Return npcFormID And &HFFFUI
        Return npcFormID And &HFFFFFFUI
    End Function
End Module
