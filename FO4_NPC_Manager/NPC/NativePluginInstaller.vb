Imports System.IO
Imports FO4_Base_Library

''' <summary><b>Instala o borra el plugin de F4SE <c>NPC_Manager_FO4_LoadBake.dll</c> segun el toggle
''' <see cref="NPC_Config.ForceEngineBakeOnOverrides"/>.</b> Es la UNICA sede que toca ese archivo.
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
''' Skyrim activo esta clase no toca NADA: ni instala ni borra. El valor persistido hace round-trip
''' intacto, igual que el resto de los toggles por juego de la solapa Fixes.</para>
''' <para><b>El chequeo de version es una comparacion de BYTES</b> contra el recurso embebido, no un
''' numero: si el archivo instalado difiere en un solo byte del que trae este build, se pisa. Cubre de
''' una las dos cosas que hay que cubrir —una version vieja nuestra y una copia ajena en esa ruta— sin
''' depender de que alguien se acuerde de subir un contador. El DLL no expone su version a la app de
''' ninguna otra forma que no sea leerle el bloque de recursos, que seria mas codigo para responder
''' MENOS (un build con el mismo numero y distinto contenido pasaria).</para>
''' <para><b>Escribe FUERA de la app.</b> La confirmacion Yes/No la pide la UI (CharGenOptionsForm) ANTES
''' de que el usuario cambie el toggle; esta clase no pregunta nada porque tambien corre al arrancar,
''' donde no hay a quien preguntarle: ahi se limita a hacer valer lo que el usuario ya eligio.</para>
''' </summary>
Public NotInheritable Class NativePluginInstaller

    Private Sub New()
    End Sub

    ''' <summary>LogicalName fijado en el .vbproj. Lo vigila <c>Native\PluginNativoEmbebido.targets</c>.</summary>
    Public Const ResourceName As String = "NpcManager.Native.NPC_Manager_FO4_LoadBake.dll"

    ''' <summary>Ruta relativa a <c>Data\</c>. Es tambien la del FOMOD (ver FomodExporter).</summary>
    Public Const DataRelativePath As String = "F4SE\Plugins\NPC_Manager_FO4_LoadBake.dll"

    ''' <summary>Los bytes del DLL que trae ESTE build, o Nothing si el recurso no esta embebido (o sea
    ''' si el proyecto nativo no se compilo antes de compilar la app: lo avisa el .targets).</summary>
    Public Shared Function EmbeddedBytes() As Byte()
        Using s = Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName)
            If s Is Nothing Then Return Nothing
            Using ms As New MemoryStream()
                s.CopyTo(ms)
                Return ms.ToArray()
            End Using
        End Using
    End Function

    ''' <summary>Ruta absoluta del archivo instalado, o "" si no hay Data.</summary>
    Public Shared Function InstalledPath(dataPath As String) As String
        If String.IsNullOrEmpty(dataPath) Then Return ""
        Return Path.Combine(dataPath, DataRelativePath)
    End Function

    ''' <summary>True si el archivo esta en Data (sin mirar si es el nuestro ni si esta al dia).</summary>
    Public Shared Function IsInstalled(dataPath As String) As Boolean
        Dim p = InstalledPath(dataPath)
        Return p <> "" AndAlso File.Exists(p)
    End Function

    ''' <summary>True si el archivo instalado es EXACTAMENTE el de este build.</summary>
    Public Shared Function IsUpToDate(dataPath As String) As Boolean
        Dim p = InstalledPath(dataPath)
        If p = "" OrElse Not File.Exists(p) Then Return False
        Dim ours = EmbeddedBytes()
        If ours Is Nothing OrElse ours.Length = 0 Then Return False
        Try
            Dim onDisk = File.ReadAllBytes(p)
            Return onDisk.Length = ours.Length AndAlso onDisk.SequenceEqual(ours)
        Catch
            ' Ilegible (bloqueado / permisos): no se puede afirmar que este al dia.
            Return False
        End Try
    End Function

    ''' <summary><b>Hace valer el toggle contra el disco.</b> Idempotente y sin UI: se llama al arrancar
    ''' la app, al fijar el juego en el Preflight y al aceptar CharGen Options.
    ''' <para>ON  → escribe el archivo si falta o si difiere del de este build (el nuestro SIEMPRE gana).</para>
    ''' <para>OFF → lo borra AUNQUE NO SEA EL NUESTRO. Es lo correcto y lo pedido: en esa ruta, con ese
    ''' nombre, el archivo lo puso esta app; dejar una copia distinta "porque no la reconozco" es dejar
    ''' enganchado justo lo que el usuario acaba de pedir que se saque.</para>
    ''' <para>Devuelve un texto corto con lo que hizo (para el log), o "" si no hizo nada.</para></summary>
    Public Shared Function Reconcile() As String
        Dim cfg = Config_App.Current
        If cfg Is Nothing Then Return ""
        ' ⛔ Solo FO4: ver el docstring de la clase. Con Skyrim activo NO se borra tampoco — el Data de
        ' Skyrim no es donde vive este archivo, y borrar "por las dudas" en la carpeta equivocada seria
        ' tocar los mods de otro juego.
        If cfg.Game <> Config_App.Game_Enum.Fallout4 Then Return ""

        Dim dataPath = cfg.DataPath
        If String.IsNullOrEmpty(dataPath) Then Return ""
        Dim dest = InstalledPath(dataPath)

        If NPC_Config.Current Is Nothing Then Return ""
        If Not NPC_Config.Current.ForceEngineBakeOnOverrides Then
            If Not File.Exists(dest) Then Return ""
            Try
                File.Delete(dest)
                Logger.LogLazy(Function() $"[LOADBAKE] borrado '{dest}' (opcion OFF)")
                Return "removed"
            Catch ex As Exception
                Dim m = ex.Message
                Logger.LogLazy(Function() $"[LOADBAKE] NO se pudo borrar '{dest}': {m}")
                Return "remove-failed: " & m
            End Try
        End If

        Dim bytes = EmbeddedBytes()
        If bytes Is Nothing OrElse bytes.Length = 0 Then
            Logger.LogLazy(Function() "[LOADBAKE] el DLL no esta embebido en este build: no hay nada que instalar")
            Return "missing-resource"
        End If
        If IsUpToDate(dataPath) Then Return ""

        Try
            Directory.CreateDirectory(Path.GetDirectoryName(dest))
            ' ⛔ NO `File.WriteAllBytes`: pide CREATE_ALWAYS, da ACCESS_DENIED sobre un destino OCULTO y
            ' rompe el VFS de MO2 / el hardlink de Vortex al crear un archivo nuevo. Misma ley que el
            ' `.pex` (ver NpcApplyScriptEmitter.InstallPex).
            BSA_BA2_Library_DLL.EscrituraEnElLugar.Escribir(dest, Sub(fs) fs.Write(bytes, 0, bytes.Length))
            Logger.LogLazy(Function() $"[LOADBAKE] instalado '{dest}' ({bytes.Length} bytes)")
            Return "installed"
        Catch ex As Exception
            Dim m = ex.Message
            Logger.LogLazy(Function() $"[LOADBAKE] NO se pudo instalar '{dest}': {m}")
            Return "install-failed: " & m
        End Try
    End Function

End Class
