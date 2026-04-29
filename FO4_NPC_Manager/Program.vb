Imports System.Windows.Forms

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
        Application.Run(New MainForm())
    End Sub
End Module
