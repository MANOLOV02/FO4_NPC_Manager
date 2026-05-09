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
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4

        Using preflight As New Preflight_Form()
            If preflight.ShowDialog() <> DialogResult.OK Then Return
            Application.Run(New MainForm(preflight.LoadedPluginManager,
                                         preflight.LoadedDataPath,
                                         preflight.LoadedAutoGenPlugins))
        End Using
    End Sub
End Module
