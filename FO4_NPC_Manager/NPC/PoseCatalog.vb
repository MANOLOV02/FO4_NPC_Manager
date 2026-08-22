Imports System.Globalization
Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization
Imports System.Xml.Linq
Imports FO4_Base_Library

' ==========================================================================
' Static-pose catalog for the animation bar's pose combo.
'
' Faithful port of Wardrobe_Manager's pose loading — kept local because
' NPC_Manager doesn't reference the WM project (same criterion as
' BodySlidePresetCatalog):
'   • "None" entry        = SliderPresetCollection.LoadDefaultPose (WM OSP_Clases.vb:150)
'   • BodySlide/WM XML    = SliderPresetCollection.LoadPosesBS     (WM OSP_Clases.vb:102)
'   • SAM (ScreenArcher)  = SliderPresetCollection.LoadPosesSAM    (WM OSP_Clases.vb:160)
'
' Roots are resolved exactly like WM's Wardrobe_Manager_Form.Directorios
' (PosesBSRoot / PosesSAMRoot); the two inputs those need — the game's Data
' folder and the BodySlide install — are already configured in this app
' (Config_App.FO4EDataPath and NPC_Config.BodySlideExePath_*). See
' ResolveBodySlideDir for the sibling-install fallback.
'
' The produced Poses_class objects go straight to SkeletonInstance.ApplyPose,
' which is the SAME Delta layer the HKX animation player writes per frame —
' that is why a pose and a clip can never be active at once (the anim bar
' forces the pose back to "None" while a clip is selected).
' ==========================================================================
Public Class PoseCatalog

    ''' <summary>Loaded poses keyed by <c>Poses_class.ToString</c> (name + " (… pose)"), the same
    ''' key WM's combo shows. SortedDictionary = alphabetical, like WM's <c>Relee_Poses</c>
    ''' (which orders the keys before filling the combo).</summary>
    Public ReadOnly Property Poses As New SortedDictionary(Of String, Poses_class)

    ''' <summary>Key of the identity entry. WM builds it from the None source, so it reads
    ''' "None (Wardrobe Manager pose)" — kept identical so both apps label it the same.</summary>
    Public Shared ReadOnly Property NoneKey As String
        Get
            Return Poses_class.KeyName("None", Poses_class.Pose_Source_Enum.None)
        End Get
    End Property

    Private Shared ReadOnly _samJsonOpts As New JsonSerializerOptions With {
        .PropertyNameCaseInsensitive = True,
        .NumberHandling = JsonNumberHandling.AllowReadingFromString
    }

    ''' <summary>Rebuild the catalog: identity + SAM exports + BodySlide/WM poses, in WM's own
    ''' load order (LoadDefaultPose → LoadPosesSAM → LoadPosesBS, Wardrobe_Manager_Form.vb:456).
    ''' Both folders are optional — a missing one just contributes nothing.</summary>
    Public Sub Load(samPosesDir As String, bodySlidePoseDataDir As String)
        Poses.Clear()
        LoadDefaultPose()
        LoadPosesSAM(samPosesDir)
        LoadPosesBS(bodySlidePoseDataDir)
    End Sub

    ''' <summary>The identity pose: empty transform set, so ApplyPose clears the Delta layer and
    ''' leaves the morph/mount layers alone. Port of WM's LoadDefaultPose.</summary>
    Private Sub LoadDefaultPose()
        Dim pos As New Poses_class With {
            .Source = Poses_class.Pose_Source_Enum.None,
            .Name = "None",
            .Version = 1,
            .Skeleton = "CBBE",
            .Transforms = New Dictionary(Of String, PoseTransformData)
        }
        Poses(pos.ToString()) = pos
    End Sub

    ''' <summary>BodySlide/Wardrobe Manager poses: every *.xml under &lt;BodySlide dir&gt;\PoseData,
    ''' &lt;Pose name= [WMPose=]&gt; / &lt;Bone name= rotX= rotY= rotZ= transX= transY= transZ= [scale=]&gt;.
    ''' Port of WM's LoadPosesBS, including the rotX→Yaw / rotY→Pitch / rotZ→Roll mapping and the
    ''' last-wins on duplicate keys. Unreadable files are logged and skipped (WM MsgBoxes; a modal
    ''' per bad file while just populating a combo would be hostile — same call as the preset
    ''' catalog).</summary>
    Private Sub LoadPosesBS(posesPath As String)
        If String.IsNullOrEmpty(posesPath) OrElse Not Directory.Exists(posesPath) Then Return
        For Each xmlPath In FilesDictionary_class.EnumerateFilesWithSymlinkSupport(posesPath, "*.xml", False)
            Try
                Dim doc As XDocument = XDocument.Parse(File.ReadAllText(xmlPath))
                For Each poseEl As XElement In doc.Root.Elements("Pose")
                    Dim nameAttr = poseEl.Attribute("name")?.Value
                    If String.IsNullOrEmpty(nameAttr) Then
                        Throw New InvalidDataException($"<Pose> missing required 'name' in '{xmlPath}'")
                    End If
                    Dim pos As New Poses_class With {
                        .Source = Poses_class.Pose_Source_Enum.BodySlide,
                        .Name = nameAttr,
                        .Version = 1,
                        .Skeleton = "CBBE",
                        .Transforms = New Dictionary(Of String, PoseTransformData),
                        .Filename = xmlPath
                    }
                    If String.Equals(poseEl.Attribute("WMPose")?.Value, "true", StringComparison.OrdinalIgnoreCase) Then
                        pos.Source = Poses_class.Pose_Source_Enum.WardrobeManager
                    End If
                    For Each boneEl As XElement In poseEl.Elements("Bone")
                        Dim boneName = boneEl.Attribute("name")?.Value
                        If String.IsNullOrEmpty(boneName) Then
                            Throw New InvalidDataException($"<Bone> missing 'name' in pose '{nameAttr}' of '{xmlPath}'")
                        End If
                        Dim tr As New PoseTransformData With {
                            .Yaw = ParseFloat(boneEl, "rotX", xmlPath, nameAttr),
                            .Pitch = ParseFloat(boneEl, "rotY", xmlPath, nameAttr),
                            .Roll = ParseFloat(boneEl, "rotZ", xmlPath, nameAttr),
                            .X = ParseFloat(boneEl, "transX", xmlPath, nameAttr),
                            .Y = ParseFloat(boneEl, "transY", xmlPath, nameAttr),
                            .Z = ParseFloat(boneEl, "transZ", xmlPath, nameAttr)
                        }
                        If boneEl.Attribute("scale") IsNot Nothing Then
                            tr.Scale = ParseFloat(boneEl, "scale", xmlPath, nameAttr)
                        End If
                        tr.LeerPerEje(boneEl)
                        pos.Transforms(boneName) = tr
                    Next
                    Poses(pos.ToString()) = pos
                Next
            Catch ex As Exception
                Logger.LogLazy(Function() $"[POSE-CAT] Error reading pose file '{xmlPath}': {ex}")
            End Try
        Next
    End Sub

    ''' <summary>SAM (ScreenArcher Menu) exports: every *.json under
    ''' &lt;Data&gt;\F4SE\Plugins\SAF\Poses\Exports. Port of WM's LoadPosesSAM. SAM is a Fallout 4
    ''' mod (WM HkxPoseImport_Form.vb:57), so under Skyrim the folder simply doesn't exist and this
    ''' contributes nothing — same as WM, which also calls it unconditionally.
    ''' <para>These carry Source=ScreenArcher (the deserializer's default), which ApplyPose reads as
    ''' "delta relative to bind" — see SkeletonInstance.ResolvePoseTransform.</para></summary>
    Private Sub LoadPosesSAM(posesPath As String)
        If String.IsNullOrEmpty(posesPath) OrElse Not Directory.Exists(posesPath) Then Return
        For Each jsonPath In FilesDictionary_class.EnumerateFilesWithSymlinkSupport(posesPath, "*.json", False)
            Try
                Dim model As Poses_class = JsonSerializer.Deserialize(Of Poses_class)(File.ReadAllText(jsonPath), _samJsonOpts)
                ' Deserialize returns Nothing for an empty/"null" document — guard before dereferencing
                ' so an empty file is diagnosed as such and not as a read failure (WM has the same guard).
                If model Is Nothing OrElse model.Transforms Is Nothing Then
                    Logger.LogLazy(Function() $"[POSE-CAT] SAM pose file produced no pose (empty/null JSON): '{jsonPath}'.")
                    Continue For
                End If
                model.Filename = jsonPath
                Poses(model.ToString()) = model
            Catch ex As Exception
                Logger.LogLazy(Function() $"[POSE-CAT] Error reading pose file '{jsonPath}': {ex}")
            End Try
        Next
    End Sub

    Private Shared Function ParseFloat(el As XElement, attr As String, path As String, poseName As String) As Single
        Dim raw = el.Attribute(attr)?.Value
        Dim value As Single
        If raw Is Nothing OrElse Not Single.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, value) Then
            Throw New InvalidDataException($"<Bone> attribute '{attr}'='{If(raw, "<missing>")}' is not a number in pose '{poseName}' of '{path}'")
        End If
        Return value
    End Function

    ' ── Export (mirrors WM's Wardrobe_Manager_Form.SaveImportedHkxPoseXml) ──

    ''' <summary>Name of the shared file WM writes its imported/authored poses into, inside
    ''' <see cref="ResolveBsPosesDir"/>. Same file so both apps read and write the same catalog.</summary>
    Public Const WmPosesFileName As String = "WardrobeManagerPoses.xml"

    ''' <summary>Look up a pose BY NAME under either key spelling (WardrobeManager or BodySlide) —
    ''' the two labels the same name can carry depending on how the XML declared it. Port of the
    ''' keyName/bodySlideKeyName probe at the top of WM's SaveImportedHkxPoseXml. Returns Nothing when
    ''' the name is free; otherwise the entry and, via <paramref name="foundKey"/>, its catalog key.</summary>
    Public Function FindByName(name As String, ByRef foundKey As String) As Poses_class
        foundKey = Nothing
        If String.IsNullOrWhiteSpace(name) Then Return Nothing
        For Each src In {Poses_class.Pose_Source_Enum.WardrobeManager, Poses_class.Pose_Source_Enum.BodySlide}
            Dim k = Poses_class.KeyName(name, src)
            Dim hit As Poses_class = Nothing
            If Poses.TryGetValue(k, hit) Then
                foundKey = k
                Return hit
            End If
        Next
        Return Nothing
    End Function

    ''' <summary>Write one pose into the WM pose XML, byte-for-byte in WM's format — port of
    ''' <c>Wardrobe_Manager_Form.SaveImportedHkxPoseXml</c> minus its MsgBox conflict prompts (those
    ''' are the caller's, so this stays a pure writer):
    ''' <list type="bullet">
    ''' <item>creates the folder and an empty &lt;PoseData&gt; document if missing,</item>
    ''' <item>reuses the &lt;Pose name=…&gt; element if the name is already there (case-insensitive)
    ''' and forces <c>WMPose="true"</c>, otherwise appends a new one,</item>
    ''' <item>drops every previous &lt;Bone&gt; of that pose and rewrites the NON-identity transforms —
    ''' <c>rotX=Yaw / rotY=Pitch / rotZ=Roll</c>, invariant culture, same as the reader expects.</item>
    ''' </list>
    ''' On success stamps the pose's Filename/Source and registers it in this catalog under the WM key.</summary>
    Public Sub WriteWmPoseXml(xmlPath As String, pose As Poses_class)
        If pose Is Nothing OrElse String.IsNullOrWhiteSpace(pose.Name) Then
            Throw New ArgumentException("Pose is missing a name.", NameOf(pose))
        End If

        Dim folder = Path.GetDirectoryName(xmlPath)
        If Not String.IsNullOrEmpty(folder) AndAlso Not Directory.Exists(folder) Then Directory.CreateDirectory(folder)

        If Not File.Exists(xmlPath) Then
            Dim newDoc As New XDocument(New XDeclaration("1.0", "UTF-8", Nothing), New XElement("PoseData"))
            newDoc.Save(xmlPath)
        End If

        Dim doc = XDocument.Load(xmlPath)
        If doc.Root Is Nothing Then doc.Add(New XElement("PoseData"))

        Dim selected = doc.Root.Elements("Pose").
            FirstOrDefault(Function(pf) String.Equals(pf.Attribute("name")?.Value, pose.Name, StringComparison.OrdinalIgnoreCase))

        If selected Is Nothing Then
            selected = New XElement("Pose", New XAttribute("name", pose.Name), New XAttribute("WMPose", "true"))
            doc.Root.Add(selected)
        ElseIf selected.Attribute("WMPose") Is Nothing Then
            selected.Add(New XAttribute("WMPose", "true"))
        Else
            selected.Attribute("WMPose").Value = "true"
        End If

        For Each boneElement In selected.Elements("Bone").ToList()
            boneElement.Remove()
        Next

        For Each tr In pose.Transforms.Where(Function(pf) pf.Value.Isidentity = False)
            selected.Add(New XElement("Bone",
                                      New XAttribute("name", tr.Key),
                                      New XAttribute("rotX", tr.Value.Yaw.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("rotY", tr.Value.Pitch.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("rotZ", tr.Value.Roll.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("transX", tr.Value.X.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("transY", tr.Value.Y.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("transZ", tr.Value.Z.ToString(CultureInfo.InvariantCulture)),
                                      New XAttribute("scale", tr.Value.Scale.ToString(CultureInfo.InvariantCulture)), tr.Value.AtributosPerEje()))
        Next

        doc.Save(xmlPath)
        pose.Filename = xmlPath
        pose.Source = Poses_class.Pose_Source_Enum.WardrobeManager
        Poses(pose.ToString()) = pose
    End Sub

    ''' <summary>Deletes one catalog pose using Wardrobe Manager's canonical storage rules:
    ''' SAM poses own their JSON file; BodySlide/WM poses own one named element in a shared XML.</summary>
    Public Sub DeletePose(pose As Poses_class)
        If pose Is Nothing OrElse pose.Source = Poses_class.Pose_Source_Enum.None Then Return
        Dim path = If(pose.Filename, "")
        If String.IsNullOrWhiteSpace(path) Then Throw New InvalidDataException("Pose has no source file.")

        If pose.Source = Poses_class.Pose_Source_Enum.ScreenArcher Then
            If File.Exists(path) Then File.Delete(path)
            Return
        End If

        If Not File.Exists(path) Then Throw New FileNotFoundException("Pose source file was not found.", path)
        Dim doc = XDocument.Load(path)
        If doc.Root Is Nothing Then Throw New InvalidDataException("Pose XML has no root element.")
        Dim selected = doc.Root.Elements("Pose").
            FirstOrDefault(Function(el) String.Equals(el.Attribute("name")?.Value, pose.Name,
                                                       StringComparison.OrdinalIgnoreCase))
        If selected Is Nothing Then Throw New InvalidDataException($"Pose '{pose.Name}' was not found in its source XML.")
        selected.Remove()
        If doc.Root.Elements("Pose").Any() Then
            doc.Save(path)
        Else
            File.Delete(path)
        End If
    End Sub

    ' ── Root resolution (mirrors WM's Wardrobe_Manager_Form.Directorios) ──

    ''' <summary>Returns the SAM pose export directory under the active game's Data folder.</summary>
    Public Shared Function ResolveSamPosesDir() As String
        Dim data = If(Config_App.Current Is Nothing, "", Config_App.Current.FO4EDataPath)
        If String.IsNullOrEmpty(data) Then Return ""
        Return Path.Combine(data, "F4SE\Plugins\SAF\Poses\Exports")
    End Function

    ''' <summary>&lt;BodySlide dir&gt;\PoseData — WM's Directorios.PosesBSRoot
    ''' (Wardrobe_Manager_Form.vb:73), verbatim on top of <see cref="ResolveBodySlideDir"/>.</summary>
    Public Shared Function ResolveBsPosesDir(isSse As Boolean) As String
        Dim bsDir = ResolveBodySlideDir(isSse)
        If String.IsNullOrEmpty(bsDir) Then Return ""
        Return Path.Combine(bsDir, "PoseData")
    End Function

    ''' <summary>The BodySlide install folder. Primary source is this app's own per-game selection
    ''' (NPC_Config.BodySlideExePath_*, set from Edit Body → BodySlide tab). Fallback, for a first
    ''' run where that was never picked: a Wardrobe Manager install in a SIBLING folder of this exe
    ''' (both tools shipped side by side under one parent) — its wm_config.json holds WM_Config.BSExePath,
    ''' which points at the very same BodySlide. Read-only; nothing is written to WM's config.</summary>
    Public Shared Function ResolveBodySlideDir(isSse As Boolean) As String
        Dim exePath = If(isSse, NPC_Config.Current.BodySlideExePath_SSE, NPC_Config.Current.BodySlideExePath_FO4)
        If Not String.IsNullOrEmpty(exePath) AndAlso File.Exists(exePath) Then
            Return Path.GetDirectoryName(exePath)
        End If
        Dim sibling = ResolveBsExeFromSiblingWm(isSse)
        If Not String.IsNullOrEmpty(sibling) Then Return Path.GetDirectoryName(sibling)
        Return ""
    End Function

    ''' <summary>¿Este BodySlide es del OTRO juego? Sólo devuelve True con EVIDENCIA POSITIVA.
    '''
    ''' <para>Fallout 4 instala BodySlide bajo <c>Tools\BodySlide</c> y Skyrim SE bajo
    ''' <c>CalienteTools\BodySlide</c>. Esa carpeta abuela es lo único que distingue los dos, y viaja
    ''' con la instalación esté donde esté.</para>
    '''
    ''' <para>ANTES ESTO EXIGÍA QUE EL EXE ESTUVIERA BAJO <c>&lt;Data&gt;</c>, Y ROMPÍA LA INSTALACIÓN
    ''' MÁS COMÚN. Con Mod Organizer 2 —que es como se instala esto en la práctica— BodySlide vive en
    ''' <c>&lt;MO2&gt;\mods\BodySlide and Outfit Studio\...</c>: el <c>Data</c> "virtual" sólo existe dentro
    ''' del proceso del juego, vía USVFS. Un BodySlide perfectamente válido del juego activo daba False y
    ''' se descartaba. Lo mismo con cualquier instalación standalone o alcanzada por junction, porque
    ''' <c>Path.GetFullPath</c> no resuelve enlaces. Y la propia app se contradecía: la ruta PRIMARIA
    ''' (<c>NPC_Config.BodySlideExePath_*</c>) se acepta sin ninguna comprobación de estar bajo Data.</para>
    '''
    ''' <para>Por eso la carga de la prueba está invertida: se descarta SÓLO si la carpeta dice
    ''' explícitamente que es del otro juego. Si no se puede determinar, se acepta — que era el
    ''' comportamiento previo. El daño de rechazar de más (el combo de poses vacío, sin mensaje visible
    ''' en Release) resultó peor que el de aceptar de más.</para></summary>
    Private Shared Function EsDelOtroJuego(exe As String, isSse As Boolean) As Boolean
        Try
            ' <...>\Tools\BodySlide\BodySlide x64.exe  ->  la abuela es "Tools" (FO4) o "CalienteTools" (SSE)
            Dim carpeta = Path.GetDirectoryName(Path.GetFullPath(exe))
            If String.IsNullOrEmpty(carpeta) Then Return False
            Dim abuela = Path.GetFileName(Path.GetDirectoryName(carpeta))
            If String.IsNullOrEmpty(abuela) Then Return False
            If String.Equals(abuela, "CalienteTools", StringComparison.OrdinalIgnoreCase) Then Return Not isSse
            If String.Equals(abuela, "Tools", StringComparison.OrdinalIgnoreCase) Then Return isSse
            Return False   ' no se puede determinar: no es evidencia de nada
        Catch
            Return False
        End Try
    End Function

    ''' <summary>Olvida el escaneo memoizado de carpetas hermanas. Lo llama <c>EnsurePoseCatalog</c>
    ''' cuando el usuario pide una relectura explícita (abrir el combo, exportar una pose).</summary>
    Public Shared Sub OlvidarEscaneoDeHermanos()
        _escaneoHermanos = Nothing
    End Sub

    ''' <summary>Resultado memoizado de <see cref="ResolveBsExeFromSiblingWm"/>. Es lo ÚNICO caro de
    ''' resolver la ruta —<c>EnumerateDirectories</c> sobre la carpeta padre del exe más un
    ''' <c>ReadAllText</c> + <c>JsonDocument.Parse</c> por cada hermana con <c>wm_config.json</c>— y
    ''' <c>EnsurePoseCatalog</c> lo pide en CADA render del preview. Memoizar acá deja el corte por caché
    ''' del catálogo mirando las rutas de verdad, que es lo que tiene que mirar.</summary>
    Private Shared _escaneoHermanos As String = Nothing

    ''' <summary>Momento del último escaneo, para NO congelar un resultado VACIO. Memoizar el "no
    ''' encontré nada" para siempre reintroducía, por la rama del WM hermano, el mismo defecto que se
    ''' acababa de cerrar en la rama primaria: el usuario abre Wardrobe Manager, configura BodySlide ahí,
    ''' vuelve, y las poses no aparecen NUNCA hasta reiniciar — porque ningún camino alcanzable invalida
    ''' la memo (el combo queda deshabilitado con un solo ítem, así que su DropDown no dispara, y el botón
    ''' de export sale por el MsgBox antes de forzar la relectura).
    ''' <para>Un éxito sí se puede cachear indefinidamente: la ruta ya no va a dejar de existir sola. Un
    ''' fracaso se reintenta pasada la ventana. <c>TickCount64</c> y no <c>Date.Now</c>: monótono, inmune
    ''' a que el usuario cambie la hora del sistema.</para></summary>
    Private Shared _escaneoHermanosMs As Long = 0
    Private Const MsEntreReintentosDeEscaneo As Long = 5000

    ''' <summary>Scan the sibling folders of this exe for a Wardrobe Manager install (wm_config.json)
    ''' and read its BSExePath. Only the folders next to ours, one level — no recursive walk. Returns
    ''' "" when there is no sibling WM, its config is unreadable, or the exe it names is gone.</summary>
    Private Shared Function ResolveBsExeFromSiblingWm(isSse As Boolean) As String
        ' Un EXITO se cachea para siempre; un FRACASO sólo por una ventana corta. Ver _escaneoHermanosMs.
        If Not String.IsNullOrEmpty(_escaneoHermanos) Then Return _escaneoHermanos
        If _escaneoHermanos IsNot Nothing AndAlso
           Environment.TickCount64 - _escaneoHermanosMs < MsEntreReintentosDeEscaneo Then Return _escaneoHermanos
        _escaneoHermanos = EscanearHermanos(isSse)
        _escaneoHermanosMs = Environment.TickCount64
        Return _escaneoHermanos
    End Function

    Private Shared Function EscanearHermanos(isSse As Boolean) As String
        Try
            ' TrimEnd first: on .NET Core+ Application.StartupPath comes back WITH a trailing
            ' separator, and GetParent of "…\NpcManager\" is "…\NpcManager", not its parent.
            Dim ours = Path.GetFullPath(Application.StartupPath).TrimEnd(Path.DirectorySeparatorChar)
            Dim parent = Directory.GetParent(ours)
            If parent Is Nothing Then Return ""
            For Each siblingDir In Directory.EnumerateDirectories(parent.FullName)
                If String.Equals(Path.GetFullPath(siblingDir).TrimEnd(Path.DirectorySeparatorChar), ours, StringComparison.OrdinalIgnoreCase) Then Continue For
                Dim cfg = Path.Combine(siblingDir, "wm_config.json")
                If Not File.Exists(cfg) Then Continue For
                Try
                    Using doc = JsonDocument.Parse(File.ReadAllText(cfg),
                                                   New JsonDocumentOptions With {.CommentHandling = JsonCommentHandling.Skip, .AllowTrailingCommas = True})
                        Dim el As JsonElement
                        If Not doc.RootElement.TryGetProperty("BSExePath", el) Then Continue For
                        If el.ValueKind <> JsonValueKind.String Then Continue For
                        Dim exe = el.GetString()
                        If Not String.IsNullOrEmpty(exe) AndAlso File.Exists(exe) Then
                            ' HAY QUE CONFIRMAR QUE ESE BodySlide NO ES EL DEL OTRO JUEGO. `BSExePath` de
                            ' Wardrobe Manager es UNA SOLA propiedad, sin juego (WM_Config.vb): apunta al
                            ' BodySlide del último juego con el que se configuró WM, que no tiene por qué ser
                            ' el que este NPC Manager tiene abierto. Sin comprobar nada, con Skyrim activo y
                            ' un WM configurado para Fallout 4 se listaban las poses del BodySlide de FO4 y
                            ' —peor— "Export pose…" ESCRIBÍA en el PoseData de FO4, sin que nada lo dijera.
                            ' El criterio NO es "estar bajo <Data>": eso se probó y rompía Mod Organizer 2,
                            ' que es la instalación normal. Ver el doc de EsDelOtroJuego.
                            If EsDelOtroJuego(exe, isSse) Then
                                Logger.LogLazy(Function() $"[POSE-CAT] El BodySlide del Wardrobe Manager hermano ('{cfg}' -> '{exe}') " &
                                                          "esta bajo la carpeta del OTRO juego (Tools = FO4 / CalienteTools = SSE). " &
                                                          "Se ignora para no listar ni ESCRIBIR poses en el PoseData equivocado. " &
                                                          "Configurar la ruta en Edit Body -> BodySlide para este juego.")
                                Continue For
                            End If
                            Logger.LogLazy(Function() $"[POSE-CAT] BodySlide resolved from sibling Wardrobe Manager config '{cfg}'.")
                            Return exe
                        End If
                    End Using
                Catch ex As Exception
                    Logger.LogLazy(Function() $"[POSE-CAT] Unreadable sibling WM config '{cfg}': {ex.GetType().Name}: {ex.Message}")
                End Try
            Next
        Catch ex As Exception
            Logger.LogLazy(Function() $"[POSE-CAT] Sibling WM scan failed: {ex.GetType().Name}: {ex.Message}")
        End Try
        Return ""
    End Function

End Class
