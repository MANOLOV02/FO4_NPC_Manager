Imports System.Windows.Forms
Imports FO4_Base_Library

Module Program
    <STAThread>
    Sub Main()
        ' Match WM (Wardrobe_Manager Application.Designer.vb:31): HighDpiMode = DpiUnaware.
        ' Default en .NET 8 es SystemAware/PerMonitorV2, que hace que el framebuffer del
        ' GLControl no coincida con el Width/Height reportado al CenterCamera/UpdateProjection
        ' cuando Windows está en escalado >100%, generando frame visualmente desbalanceado.
        ' DpiUnaware desactiva el escalado y deja Width/Height en píxeles físicos directos.
        Application.SetHighDpiMode(HighDpiMode.DpiUnaware)
        Application.EnableVisualStyles()
        Application.SetCompatibleTextRenderingDefault(False)

        Config_App.LoadConfig()
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4

        Using preflight As New Preflight_Form()
            If preflight.ShowDialog() <> DialogResult.OK Then Return
            Application.Run(New MainForm(preflight.LoadedPluginManager, preflight.LoadedDataPath))
        End Using
    End Sub
End Module
