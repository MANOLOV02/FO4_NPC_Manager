Imports System.IO
Imports FO4_Base_Library

''' <summary><b>Instala o borra el plugin de F4SE <c>NPC_Manager_FO4_LoadBake.dll</c> segun el toggle
''' <see cref="NPC_Config.ForceEngineBakeOnOverrides"/>.</b> Es la UNICA sede de NPC Manager que toca ese archivo.
''' <para><b>La LEY de instalacion vive en la libreria</b> (<see cref="EmbeddedNativePlugin"/>, SafeScrap fase C, 26-sep:
''' la comparten las apps que llevan un plugin embebido). Esta clase es la FACHADA de NPC Manager con la misma API de
''' siempre: arma el plugin con el assembly de ESTA app (el recurso vive aca, no en la libreria), Fallout 4 como juego y
''' el tag de log <c>LOADBAKE</c>, y <see cref="Reconcile"/> le pasa el toggle.</para>
''' <para><b>Que hace ese DLL.</b> El motor descarta el FaceGeom horneado cuando al NPC lo gana un plugin
''' sin pedigri: el predicado <c>0x1406DF3A0</c> (FO4 1.11.240) exige extension <c>.esm</c>/<c>.esl</c> o
''' flag ESM, o indice de carga 0, o estar en la lista de 8 DLC + las lineas de <c>Fallout4.ccc</c>. Un
''' <c>.esp</c> normal falla, asi que TODO lo que esta app hornea se ignora y la cabeza se rearma en
''' runtime desde los head parts. El DLL redirige tres <c>call</c> de ese predicado y, cuando el motor
''' dice que no, contesta que si SOLO si el horneado EXISTE (lo pregunta con las propias funciones del
''' motor, <c>0x140658E60</c> + <c>0x1402FC0C0</c>, que ven tambien adentro de los <c>.ba2</c>). Fuente y
''' RE completo en <c>FO4_NPC_Manager\Native\NPC_Manager_FO4_LoadBake\</c>.</para>
''' <para><b>Solo FO4.</b> El gate no existe en Skyrim (su predicado equivalente, <c>0x1403C3B70</c>,
''' llama al loader sin mirar el plugin), y ademas <c>Data\F4SE\</c> no es una carpeta de Skyrim. Con
''' Skyrim activo NO se toca NADA: ni instala ni borra (la guarda es el <c>TargetGame</c> del plugin). El valor
''' persistido hace round-trip intacto, igual que el resto de los toggles por juego de la solapa Fixes.</para>
''' <para><b>El chequeo de version es una comparacion de BYTES</b> contra el recurso embebido (ver la sede).</para>
''' <para><b>Escribe FUERA de la app.</b> La confirmacion Yes/No la pide la UI (CharGenOptionsForm) ANTES
''' de que el usuario cambie el toggle; esta clase no pregunta nada porque tambien corre al arrancar,
''' donde no hay a quien preguntarle: ahi se limita a hacer valer lo que el usuario ya eligio.</para>
''' </summary>
Public NotInheritable Class NativePluginInstaller

    Private Sub New()
    End Sub

    ''' <summary>LogicalName fijado en el .vbproj. Lo vigila <c>Native\PluginNativoEmbebido.targets</c>.</summary>
    Public Const ResourceName As String = "NpcManager.Native.NPC_Manager_FO4_LoadBake.dll"

    ''' <summary>Ruta relativa a <c>Data\</c>. Es tambien la del FOMOD (ver FomodExporter) y la del texto de
    ''' confirmacion de CharGenOptionsForm.</summary>
    Public Const DataRelativePath As String = "F4SE\Plugins\NPC_Manager_FO4_LoadBake.dll"

    ''' <summary>El plugin, con el assembly de ESTA app: el recurso embebido vive aca.</summary>
    Private Shared ReadOnly Plugin As New EmbeddedNativePlugin(GetType(NativePluginInstaller).Assembly, ResourceName, DataRelativePath,
                                                               Config_App.Game_Enum.Fallout4, "LOADBAKE")

    ''' <summary>Los bytes del DLL que trae ESTE build, o Nothing si el recurso no esta embebido (o sea
    ''' si el proyecto nativo no se compilo antes de compilar la app: lo avisa el .targets).</summary>
    Public Shared Function EmbeddedBytes() As Byte()
        Return Plugin.EmbeddedBytes()
    End Function

    ''' <summary>Ruta absoluta del archivo instalado, o "" si no hay Data.</summary>
    Public Shared Function InstalledPath(dataPath As String) As String
        Return Plugin.InstalledPath(dataPath)
    End Function

    ''' <summary>True si el archivo esta en Data (sin mirar si es el nuestro ni si esta al dia).</summary>
    Public Shared Function IsInstalled(dataPath As String) As Boolean
        Return Plugin.IsInstalled(dataPath)
    End Function

    ''' <summary>True si el archivo instalado es EXACTAMENTE el de este build.</summary>
    Public Shared Function IsUpToDate(dataPath As String) As Boolean
        Return Plugin.IsUpToDate(dataPath)
    End Function

    ''' <summary><b>Hace valer el toggle contra el disco.</b> Idempotente y sin UI: se llama al arrancar
    ''' la app, al fijar el juego en el Preflight y al aceptar CharGen Options.
    ''' <para>ON  → escribe el archivo si falta o si difiere del de este build (el nuestro SIEMPRE gana).</para>
    ''' <para>OFF → lo borra AUNQUE NO SEA EL NUESTRO (ver <see cref="EmbeddedNativePlugin.Remove"/>).</para>
    ''' <para>Devuelve un texto corto con lo que hizo (para el log), o "" si no hizo nada.</para></summary>
    Public Shared Function Reconcile() As String
        If NPC_Config.Current Is Nothing Then Return ""
        Return Plugin.Reconcile(NPC_Config.Current.ForceEngineBakeOnOverrides)
    End Function

End Class
