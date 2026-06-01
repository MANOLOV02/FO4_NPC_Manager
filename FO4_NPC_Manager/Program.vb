Imports System.Windows.Forms
Imports FO4_Base_Library

Module Program
    <STAThread>
    Sub Main()
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

        ' Derivación FaceTint (POR AHORA): forzar el sufijo sandbox `_2` (+ TGA) AUN en Release. Permite
        ' correr el batch en Release (Logger.Enabled=False -> sin overhead de LogLazy, CPU paralelo) y seguir
        ' escribiendo `{id}_d_2.dds`/`.tga` al lado del CK para comparar, sin pisar `{id}_d.dds`. Corre fuera
        ' del #If DEBUG (también en Release). Comentar esta línea para bakes de PRODUCCIÓN (escriben `_d.dds`).
        FaceGenBuilder.SandboxOutput = True

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
End Module
