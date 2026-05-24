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

        ' Plugin text encoding MUST be configured BEFORE any plugin is loaded — mirror of xEdit's
        ' order: xeInit configures wbEncodingTrans (from sLanguage) before TwbFile loads. The
        ' preflight below loads + scans all plugins; even though FULL/EDID parsing is lazy, doing
        ' this here guarantees every decode (eager or lazy, preflight or later) uses the correct
        ' encoding from the start. Process model = xEdit: configure → load all → edit.
        PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
        PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())

        Using preflight As New Preflight_Form()
            If preflight.ShowDialog() <> DialogResult.OK Then Return
            Application.Run(New MainForm(preflight.LoadedPluginManager,
                                         preflight.LoadedDataPath,
                                         preflight.LoadedAutoGenPlugins,
                                         preflight.LoadedSidecars))
        End Using
    End Sub
End Module
