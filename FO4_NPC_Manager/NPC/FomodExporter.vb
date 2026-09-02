Option Strict On
Imports System.IO
Imports System.IO.Compression
Imports System.Xml.Linq
Imports BSA_BA2_Library_DLL.BethesdaArchive.Core
Imports FO4_Base_Library

''' <summary>UI-free logic behind "Export FOMOD": builds the game-aware file manifest for an
''' app-authored plugin, validates it, generates <c>fomod\info.xml</c> + <c>fomod\ModuleConfig.xml</c>
''' and streams everything into a distributable ZIP.
'''
''' Package layout: the ZIP root IS the Data root (plugin, archives, .bssliders, Scripts\*.pex,
''' BodyGen inis, extra assets keep their Data-relative paths) plus the <c>fomod\</c> folder with the
''' two XMLs, so MO2/Vortex install it without a wizard. The manifest mirrors exactly what Save ESP
''' writes to disk (see NpcOverrideSaver): the export never re-generates content, it packages the
''' last-saved state.</summary>
Public Module FomodExporter

    ''' <summary>Credit line ALWAYS appended to the info.xml description and shown on the wizard
    ''' page — the FOMOD must make it clear the mod was authored with this app, in addition to
    ''' whatever the mod author wrote.</summary>
    Public Const CreditLine As String = "Created with NPC_Manager by ManoloV02"

    ''' <summary>NPC_Manager's own Nexus page, game-aware — shown next to the credit line.</summary>
    Public Function NexusUrlFor(game As Config_App.Game_Enum) As String
        Return If(game = Config_App.Game_Enum.Skyrim,
                  "https://www.nexusmods.com/skyrimspecialedition/mods/185193",
                  "https://www.nexusmods.com/fallout4/mods/105008")
    End Function

    ''' <summary>The optional wizard screenshot's package path — captured from the main preview
    ''' when the export dialog opens, written by ExportToZip only when the author keeps
    ''' "Include screenshot" checked. XML form (backslash) and ZIP-entry form (slash).</summary>
    Public Const ScreenshotEntryXmlPath As String = "fomod\screenshot.png"
    Public Const ScreenshotEntryZipPath As String = "fomod/screenshot.png"

    Public Enum ItemKind
        Plugin
        Archive
        PresetSidecar
        ApplyScript
        BodyGenIni
        FaceGenLoose
        ExtraAsset
    End Enum

    ''' <summary>One file of the FOMOD package. <see cref="SourceFullPath"/> is the on-disk source
    ''' (streamed into the ZIP); the apply-script instead carries <see cref="SourceBytes"/> because
    ''' its canonical source is the .pex EMBEDDED in this assembly (NpcApplyScriptEmitter.PexBytes),
    ''' never a possibly-stale loose copy. <see cref="Required"/> + <see cref="Exists"/> drive
    ''' validation: a Required item that does not exist blocks the export.</summary>
    Public Class ManifestItem
        Public Property Kind As ItemKind
        ''' <summary>Data-relative path ("\" separators) — doubles as the ZIP entry name (with "/")
        ''' and as the source/destination of the ModuleConfig &lt;file&gt; element.</summary>
        Public Property DataRelativePath As String = ""
        Public Property SourceFullPath As String = Nothing
        Public Property SourceBytes As Byte() = Nothing
        Public Property Required As Boolean
        Public Property Exists As Boolean
        Public Property SizeBytes As Long
        ''' <summary>Short human-readable annotation for the dialog grid (e.g. why an optional
        ''' item is absent). Not exported.</summary>
        Public Property Note As String = ""
    End Class

    ''' <summary>Build the game-aware manifest for one app-authored plugin. Mandatory content
    ''' (user requirement): the plugin, its BA2/BSA set (or the loose FaceGen files when the app is
    ''' configured loose-only), the .bssliders preset sidecar (when present on disk — a plugin whose
    ''' NPCs carry no body data legitimately has none), and the game's apply-script .pex with its
    ''' correct <c>Scripts\</c> directory stub. Optional content: BodyGen inis (when emitted) and
    ''' the author's extra assets (Required — a listed asset missing on disk blocks the export
    ''' until the author removes it).</summary>
    Public Function BuildManifest(dataPath As String, pluginFileName As String,
                                  game As Config_App.Game_Enum,
                                  npcFormIDs As IReadOnlyList(Of UInteger),
                                  pluginManager As PluginManager,
                                  extraAssets As IReadOnlyList(Of String)) As List(Of ManifestItem)
        Dim manifest As New List(Of ManifestItem)
        Dim seen As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

        ' ⛔ `addDisk` YA RESUELVE EL AUSENTE: calcula `onDisk` y lo guarda en `.Exists`, que es lo que la
        ' grilla pinta y lo que `ExportToZip` filtra. Por eso un llamador NUNCA debe envolver la llamada en
        ' un `If File.Exists(...)`: ese guard no evita empaquetar un archivo inexistente —de eso ya se
        ' encarga el filtro del ZIP— sino que BORRA la fila, y entonces el faltante no existe para nadie.
        ' `noteSiFalta` deja que el llamador nombre el motivo sin repetir el File.Exists; `Nothing` = usar
        ' `note` siempre. NO es `Optional`: un lambda de VB no admite parametros opcionales (BC33010), asi
        ' que los cinco van explicitos y hay UNA sola version del helper.
        Dim addDisk = Sub(kind As ItemKind, relPath As String, required As Boolean, note As String,
                          noteSiFalta As String)
                          If String.IsNullOrEmpty(relPath) OrElse Not seen.Add(relPath) Then Return
                          Dim full = Path.Combine(dataPath, relPath)
                          Dim onDisk = File.Exists(full)
                          manifest.Add(New ManifestItem With {
                              .Kind = kind, .DataRelativePath = relPath, .SourceFullPath = full,
                              .Required = required, .Exists = onDisk,
                              .SizeBytes = If(onDisk, New FileInfo(full).Length, 0L),
                              .Note = If(onDisk OrElse noteSiFalta Is Nothing, note, noteSiFalta)})
                      End Sub

        ' 1. The plugin itself.
        addDisk(ItemKind.Plugin, pluginFileName, True, "", Nothing)

        Dim baseName = Path.GetFileNameWithoutExtension(pluginFileName)

        ' 2. Archives (normal mode) or loose FaceGen files (loose-only mode). Same game-aware
        '    naming ArchivePackager.Pack used when Save ESP packed the bake: FO4 = Main + Textures
        '    BA2 pair, SSE = a single BSA.
        If Not NPC_Config.IsLooseOnly(game) Then
            If game = Config_App.Game_Enum.Skyrim Then
                addDisk(ItemKind.Archive, baseName & ArchivePackager.EXT_BSA, True, "", Nothing)
            Else
                addDisk(ItemKind.Archive, baseName & ArchivePackager.SUFFIX_BA2_MAIN, True, "", Nothing)
                addDisk(ItemKind.Archive, baseName & ArchivePackager.SUFFIX_BA2_TEXTURES, True, "", Nothing)
            End If
            ' Companion slots the packer may have produced ("<base>2.esp" + its archives). NPC_Manager
            ' packs SingleAnchorOnly so none are expected today, but if they exist they are part of the
            ' mod — without them a numbered archive would not load on the end user's machine.
            Try
                Dim setInfo = ArchivePackager.DiscoverArchiveSet(dataPath, baseName)
                For Each archivePath In setInfo.Archives
                    addDisk(ItemKind.Archive, Path.GetFileName(archivePath), False, "companion of the archive set", Nothing)
                Next
                For Each pluginPath In setInfo.Plugins
                    addDisk(ItemKind.Plugin, Path.GetFileName(pluginPath), False, "companion of the archive set", Nothing)
                Next
            Catch
                ' Discovery is best-effort sugar; the required set above already covers the anchor.
            End Try
        ElseIf npcFormIDs IsNot Nothing AndAlso pluginManager IsNot Nothing Then
            ' Loose-only: include every FaceGen file that EXISTS on disk for the plugin's NPCs.
            ' Paths are per ORIGIN plugin + local FormID (game-aware, ESL-aware) — the exact same
            ' source of truth the packer/delete flows use.
            ' Las INVENTADAS se juntan aparte y se agregan UNA vez: son compartidas entre NPC (las
            ' mismas tres texturas para todas las gules de la raza), asi que recorrerlas por NPC las
            ' agregaria repetidas al manifiesto.
            ' ⛔⛔ SIN GUARD DE EXISTENCIA, Y ESA ES LA LEY. Antes las dos listas se filtraban por
            ' `File.Exists` ANTES de `addDisk`, asi que un FaceGen REFERENCIADO pero ausente —un NPC cuyo
            ' bake no dejo su NIF o alguno de sus DDS— no aparecia en ninguna parte: ni fila en la grilla,
            ' ni nota, ni error de `Validate`. El export terminaba "correcto" y entregaba un paquete
            ' incompleto. `addDisk` marca `.Exists = False` y `ExportToZip` ya filtra por ese flag, asi que
            ' nombrarlo no empaqueta nada roto: solo lo hace VISIBLE para que el usuario decida.
            ' El camino BA2 hermano ya hacia lo correcto para el MISMO activo
            ' (`NpcFaceGenPacker.PackBatch` → `result.MissingSources`); esta rama era la unica que callaba.
            Dim inventadas As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each fid In npcFormIDs
                For Each entry In NpcFaceGenPacker.CanonicalFaceGenEntryPathsForNpc(fid, pluginManager, game)
                    addDisk(ItemKind.FaceGenLoose, entry, False, "",
                            "FALTA en disco: el NIF/DDS de FaceGen de este NPC no esta horneado")
                Next
                For Each extra In NpcFaceGenPacker.InventedLooseFilesForNpc(fid, pluginManager, dataPath)
                    inventadas.Add(extra)
                Next
            Next
            For Each extra In inventadas
                addDisk(ItemKind.FaceGenLoose, extra, False, "textura que inventa el bake: no la trae ningun mod",
                        "FALTA en disco: el NIF la referencia y no la trae ningun mod — esa nuca queda sin textura")
            Next
        End If

        ' 3. Preset sidecar (.bssliders). Required when present; a plugin with no body data has no
        '    sidecar on disk (BssliderSidecar.Write drops empty files) — that is NOT an error, the
        '    grid just shows it as absent.
        Dim sidecarRel = baseName & BssliderSidecar.Extension
        If File.Exists(Path.Combine(dataPath, sidecarRel)) Then
            addDisk(ItemKind.PresetSidecar, sidecarRel, True, "character preset sidecar", Nothing)
        Else
            manifest.Add(New ManifestItem With {
                .Kind = ItemKind.PresetSidecar, .DataRelativePath = sidecarRel,
                .SourceFullPath = Path.Combine(dataPath, sidecarRel),
                .Required = False, .Exists = False,
                .Note = "not present — no NPC in this plugin carries body sliders/overlays"})
        End If

        ' 4. Apply-script .pex, game-aware, from the EMBEDDED resource (canonical source — a loose
        '    Data\Scripts copy could be stale; see NpcApplyScriptEmitter). Ships under Scripts\ for
        '    both games (LooksMenu/F4SE on FO4, RaceMenu/SKSE on SSE).
        ' EL .pex TIENE QUE SER EL MISMO QUE ESCRIBE EL SAVE ESP: nombre por plugin y MISMA generacion, que
        ' sale del sidecar igual que en el guardado. Si el paquete llevara la plantilla SIN parchear, su .pex
        ' declararia otros nombres de property que los que el VMAD del ESP emite, y el script leeria None.
        Dim pexGeneration = NpcApplyScriptEmitter.BaselineGeneration
        ' La SAL sale del sidecar igual que la generacion. Si se sorteara una nueva aca, el .pex del paquete
        ' declararia nombres de property distintos a los que emite el VMAD del ESP que va en el MISMO paquete,
        ' y el script leeria None en todo, sin un solo error.
        Dim pexSalt = NpcApplyScriptEmitter.BaselineSalt
        Dim pexSidecar = BssliderSidecar.Read(BssliderSidecar.BuildPath(IO.Path.Combine(dataPath, pluginFileName)))
        If pexSidecar IsNot Nothing AndAlso pexSidecar.PayloadGeneration > 0 Then
            pexGeneration = pexSidecar.PayloadGeneration
            If Not String.IsNullOrEmpty(pexSidecar.PayloadSalt) Then pexSalt = pexSidecar.PayloadSalt
        End If
        Dim pexBytes = NpcApplyScriptEmitter.PatchedPexBytes(game, pluginFileName, pexGeneration, pexSalt)
        manifest.Add(New ManifestItem With {
            .Kind = ItemKind.ApplyScript,
            .DataRelativePath = "Scripts\" & NpcApplyScriptEmitter.ScriptNameFor(game, pluginFileName) & ".pex",
            .SourceBytes = pexBytes, .Required = True,
            .Exists = (pexBytes IsNot Nothing AndAlso pexBytes.Length > 0),
            .SizeBytes = If(pexBytes IsNot Nothing, CLng(pexBytes.Length), 0L),
            .Note = "embedded helper script (" & If(game = Config_App.Game_Enum.Skyrim, "RaceMenu", "LooksMenu") & ")"})

        ' El .pex del nombre LEGADO va TAMBIEN, sin parchear. Si falta, los saves del jugador que ya tenian
        ' la version publicada anterior quedan con instancias de un tipo que no resuelve, y ese actor pierde la
        ' tabla de metodos PARA TODOS LOS SCRIPTS (medido: RaceMenu fallando 17 veces sobre un NPC nuestro).
        ' Es inerte: el .psc corta con el guard de instancia huerfana.
        ' LOS DOS JUEGOS. Estaba gateado a Skyrim y era una ASIMETRIA sin razon: el argumento de arriba
        ' (un save con instancias de un tipo que no resuelve deja al actor sin tabla de metodos PARA TODOS los
        ' scripts) no tiene nada de especifico de SSE, y de hecho se vieron instancias legadas de FO4 corriendo
        ' in-game. Ademas el Save ESP local ya lo instalaba para los dos (InstallLegacyPex no gatea por juego),
        ' asi que el paquete quedaba INCONSISTENTE con lo que la app deja en Data\Scripts.
        If pexBytes IsNot Nothing AndAlso pexBytes.Length > 0 Then
            Dim legacyBytes = NpcApplyScriptEmitter.PexBytes(game)
            manifest.Add(New ManifestItem With {
                .Kind = ItemKind.ApplyScript,
                .DataRelativePath = "Scripts\" & NpcApplyScriptEmitter.LegacyScriptFor(game) & ".pex",
                .SourceBytes = legacyBytes, .Required = True,
                .Exists = (legacyBytes IsNot Nothing AndAlso legacyBytes.Length > 0),
                .SizeBytes = If(legacyBytes IsNot Nothing, CLng(legacyBytes.Length), 0L),
                .Note = "compatibility: resolves the type in saves from the previous version (inert)"})
        End If

        ' 5. BodyGen inis, when Save ESP emitted them. Folder name uses the plugin file name WITH
        '    extension (engine convention — see BodyGenIniWriter/SseBodyGenIniWriter).
        Dim bodyGenDir = If(game = Config_App.Game_Enum.Skyrim,
                            "Meshes\actors\character\BodyGenData\" & pluginFileName,
                            "F4SE\Plugins\F4EE\BodyGen\" & pluginFileName)
        For Each ini In {"templates.ini", "morphs.ini"}
            Dim rel = bodyGenDir & "\" & ini
            If File.Exists(Path.Combine(dataPath, rel)) Then
                addDisk(ItemKind.BodyGenIni, rel, False, "BodyGen morphs", Nothing)
            End If
        Next

        ' 6. Author's extra assets (Data-relative). Required: a listed-but-missing asset must be
        '    fixed or removed by the author, never silently dropped from the package. Re-validated
        '    HERE (not only at Add time): sidecar entries can be hand-edited or stale, and a rooted
        '    or "..\"-escaping path must never reach the ZIP (its entry name would escape Data on
        '    install). An invalid entry surfaces as Required+missing so it BLOCKS with a clear
        '    message until the author removes it.
        If extraAssets IsNot Nothing Then
            For Each asset In extraAssets
                If IsSafeDataRelative(dataPath, asset) Then
                    addDisk(ItemKind.ExtraAsset, asset, True, "added by author", Nothing)
                ElseIf Not String.IsNullOrWhiteSpace(asset) AndAlso seen.Add(asset) Then
                    manifest.Add(New ManifestItem With {
                        .Kind = ItemKind.ExtraAsset, .DataRelativePath = asset,
                        .Required = True, .Exists = False,
                        .Note = "invalid path — escapes the Data folder"})
                End If
            Next
        End If

        Return manifest
    End Function

    ''' <summary>True when <paramref name="relPath"/> is a well-formed Data-RELATIVE path that
    ''' stays INSIDE the Data folder once resolved: not empty, not rooted (no drive/UNC/leading
    ''' slash) and its canonical full path still lives under dataPath (kills "..\" escapes).
    ''' Single gate for author-provided asset paths — the dialog's Add button AND the sidecar
    ''' re-load both funnel through the manifest, so this covers hand-edited JSON too.</summary>
    Public Function IsSafeDataRelative(dataPath As String, relPath As String) As Boolean
        If String.IsNullOrWhiteSpace(relPath) Then Return False
        Try
            If Path.IsPathRooted(relPath) Then Return False
            Dim dataRoot = Path.GetFullPath(dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) &
                           Path.DirectorySeparatorChar
            Dim full = Path.GetFullPath(Path.Combine(dataPath, relPath))
            Return full.StartsWith(dataRoot, StringComparison.OrdinalIgnoreCase)
        Catch
            Return False   ' Malformed path (invalid chars, too long) — never let it through.
        End Try
    End Function

    ''' <summary>Actionable error message per Required-but-missing manifest item. An empty list
    ''' means the package is exportable.</summary>
    Public Function Validate(manifest As IReadOnlyList(Of ManifestItem)) As List(Of String)
        Dim errors As New List(Of String)
        If manifest Is Nothing Then Return errors
        For Each item In manifest
            If Not item.Required OrElse item.Exists Then Continue For
            Select Case item.Kind
                Case ItemKind.Plugin
                    errors.Add($"Plugin file not found on disk: {item.DataRelativePath}.")
                Case ItemKind.Archive
                    errors.Add($"Missing archive '{item.DataRelativePath}' — re-save the plugin with archive packing enabled (CharGen Options).")
                Case ItemKind.ApplyScript
                    errors.Add("The embedded helper script (.pex) is missing from this build — see Papyrus\README.md.")
                Case ItemKind.ExtraAsset
                    If item.Note.StartsWith("invalid path", StringComparison.OrdinalIgnoreCase) Then
                        errors.Add($"Extra asset '{item.DataRelativePath}' escapes the Data folder — remove it from the list.")
                    Else
                        errors.Add($"Extra asset not found under Data: {item.DataRelativePath} — restore the file or remove it from the list.")
                    End If
                Case Else
                    errors.Add($"Missing required file: {item.DataRelativePath}.")
            End Select
        Next
        Return errors
    End Function

    ''' <summary>fomod\info.xml — the installer-visible metadata (conventional shape read by
    ''' MO2/Vortex/Nexus). The credit line + NPC_Manager's game-aware Nexus URL are ALWAYS appended
    ''' to the description; MachineVersion is emitted only when the author's version parses as a
    ''' System.Version (some installers choke on a non-numeric MachineVersion, the text form is
    ''' free-form).</summary>
    Public Function BuildInfoXml(meta As FomodMetaSidecar.MetaFile, game As Config_App.Game_Enum) As XDocument
        Dim desc = SanitizeXmlText(If(meta.Description, "")).Trim()
        If desc.Length > 0 Then desc &= vbCrLf & vbCrLf
        desc &= CreditLine & vbCrLf & "NPC_Manager on Nexus: " & NexusUrlFor(game)

        Dim versionText = SanitizeXmlText(If(meta.ModVersion, "")).Trim()
        Dim versionEl As New XElement("Version", versionText)
        Dim parsedVersion As Version = Nothing
        If Version.TryParse(versionText, parsedVersion) Then
            versionEl.Add(New XAttribute("MachineVersion", versionText))
        End If

        Dim root As New XElement("fomod",
            New XElement("Name", SanitizeXmlText(If(meta.ModName, ""))),
            New XElement("Author", SanitizeXmlText(If(meta.Author, ""))),
            versionEl,
            New XElement("Description", desc))
        If Not String.IsNullOrWhiteSpace(meta.Website) Then
            root.Add(New XElement("Website", SanitizeXmlText(meta.Website)))
        End If
        Return New XDocument(New XDeclaration("1.0", "utf-8", Nothing), root)
    End Function

    ''' <summary>fomod\ModuleConfig.xml (schema ModConfig5.0 — the NMM standard, rendered
    ''' identically by NMM, MO2 and Vortex; nothing manager-specific). ONE visible install step
    ''' with TWO groups so the page reads like a real installer instead of a wall of text:
    '''
    '''   Group "About" (SelectExactlyOne): a single Required info option — the main page. Its
    '''   description is the intro (<see cref="BuildAboutText"/>: name/version/author, the
    '''   author's description, website, credit + NPC_Manager's game-aware Nexus URL) and it
    '''   carries the screenshot when available. No files.
    '''
    '''   Group "Included components" (SelectAll): one Required option PER COMPONENT (plugin,
    '''   archives, preset sidecar, apply script, BodyGen, loose FaceGen, extra assets), each
    '''   carrying its own files and a short hover description. All forced-selected — the page
    '''   is presentation, the install set is fixed.
    '''
    ''' Descriptions are PLAIN TEXT with newlines: MO2 (QTextEdit), Vortex (pre-wrap label) and
    ''' NMM all preserve them; HTML/BBCode deliberately NOT used (Vortex/NMM would show raw
    ''' tags). source == destination because the ZIP root is already Data-relative; backslash
    ''' separators (canonical FOMOD form). <paramref name="includeScreenshot"/>: when True the
    ''' config references <c>fomod\screenshot.png</c> as module banner + About image; when False
    ''' NO image element is emitted at all — never a dangling reference or an empty box.</summary>
    Public Function BuildModuleConfigXml(meta As FomodMetaSidecar.MetaFile,
                                         manifest As IReadOnlyList(Of ManifestItem),
                                         game As Config_App.Game_Enum,
                                         npcCount As Integer,
                                         includeScreenshot As Boolean) As XDocument
        Dim xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance")

        ' ---- Group 1: About (the presentation page) --------------------------------------
        Dim aboutName = SanitizeXmlText($"{If(meta.ModName, "")} v{If(meta.ModVersion, "")}")
        Dim aboutOption As New XElement("plugin",
            New XAttribute("name", aboutName),
            New XElement("description", BuildAboutText(meta, game, npcCount)))
        If includeScreenshot Then
            aboutOption.Add(New XElement("image", New XAttribute("path", ScreenshotEntryXmlPath)))
        End If
        aboutOption.Add(New XElement("typeDescriptor",
            New XElement("type", New XAttribute("name", "Required"))))

        Dim aboutGroup As New XElement("group",
            New XAttribute("name", "About"),
            New XAttribute("type", "SelectExactlyOne"),
            New XElement("plugins", New XAttribute("order", "Explicit"), aboutOption))

        ' ---- Group 2: Included components (one entry per component, files attached) ------
        Dim componentsGroup As New XElement("group",
            New XAttribute("name", "Included components (installed automatically)"),
            New XAttribute("type", "SelectAll"))
        Dim componentPlugins As New XElement("plugins", New XAttribute("order", "Explicit"))
        For Each comp In BuildComponents(manifest, game, npcCount)
            Dim filesEl As New XElement("files")
            For Each rel In comp.Files
                filesEl.Add(New XElement("file",
                    New XAttribute("source", rel),
                    New XAttribute("destination", rel),
                    New XAttribute("priority", "0")))
            Next
            componentPlugins.Add(New XElement("plugin",
                New XAttribute("name", SanitizeXmlText(comp.Title)),
                New XElement("description", SanitizeXmlText(comp.Description)),
                filesEl,
                New XElement("typeDescriptor",
                    New XElement("type", New XAttribute("name", "Required")))))
        Next
        componentsGroup.Add(componentPlugins)

        Dim steps As New XElement("installSteps", New XAttribute("order", "Explicit"),
            New XElement("installStep", New XAttribute("name", SanitizeXmlText(If(meta.ModName, ""))),
                New XElement("optionalFileGroups", New XAttribute("order", "Explicit"),
                    aboutGroup, componentsGroup)))

        Dim root As New XElement("config",
            New XAttribute(XNamespace.Xmlns + "xsi", xsi.NamespaceName),
            New XAttribute(xsi + "noNamespaceSchemaLocation", "http://qconsulting.ca/fo3/ModConfig5.0.xsd"),
            New XElement("moduleName", SanitizeXmlText(If(meta.ModName, ""))))
        ' moduleImage right after moduleName (schema order) — the installer's header banner.
        If includeScreenshot Then
            root.Add(New XElement("moduleImage", New XAttribute("path", ScreenshotEntryXmlPath)))
        End If
        root.Add(steps)
        Return New XDocument(New XDeclaration("1.0", "utf-8", Nothing), root)
    End Function

    ''' <summary>One "Included components" wizard entry: display title, hover description, and
    ''' the manifest files it installs.</summary>
    Public Class WizardComponent
        Public Property Title As String = ""
        Public Property Description As String = ""
        Public Property Files As New List(Of String)
    End Class

    ''' <summary>Bucket the existing manifest items into wizard components, in a fixed
    ''' presentation order. Every existing manifest item lands in EXACTLY one component so the
    ''' union of component files == the full install set (nothing installs twice, nothing is
    ''' dropped). Empty buckets are omitted.</summary>
    Public Function BuildComponents(manifest As IReadOnlyList(Of ManifestItem),
                                    game As Config_App.Game_Enum,
                                    npcCount As Integer) As List(Of WizardComponent)
        Dim sse = (game = Config_App.Game_Enum.Skyrim)
        Dim scriptHost = If(sse, "RaceMenu", "LooksMenu")
        Dim scriptLoader = If(sse, "SKSE", "F4SE")

        Dim byKind = Function(kind As ItemKind) manifest.
            Where(Function(i) i.Exists AndAlso i.Kind = kind).
            Select(Function(i) i.DataRelativePath).ToList()

        Dim result As New List(Of WizardComponent)

        Dim plugins = byKind(ItemKind.Plugin)
        If plugins.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = $"Plugin - {plugins(0)}",
                .Description = $"The plugin with {npcCount} NPC record{If(npcCount = 1, "", "s")}." & vbCrLf &
                               "Enable it in your load order like any other plugin.",
                .Files = plugins})
        End If

        Dim archives = byKind(ItemKind.Archive)
        If archives.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = If(archives.Count = 1, "Packed archive - " & archives(0), $"Packed archives ({archives.Count})"),
                .Description = "Bethesda archive(s) with the baked FaceGen: head meshes and face textures for every NPC in the plugin." & vbCrLf &
                               "The game loads them automatically next to the plugin.",
                .Files = archives})
        End If

        Dim loose = byKind(ItemKind.FaceGenLoose)
        If loose.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = $"FaceGen loose files ({loose.Count})",
                .Description = "Baked FaceGen as loose files (head meshes + face textures), one set per NPC.",
                .Files = loose})
        End If

        Dim sidecar = byKind(ItemKind.PresetSidecar)
        If sidecar.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = "Character preset data - " & sidecar(0),
                .Description = "NPC_Manager preset sidecar (body sliders, overlays, skin options)." & vbCrLf &
                               "Not read by the game itself — it lets anyone with NPC_Manager re-open and edit these characters.",
                .Files = sidecar})
        End If

        Dim script = byKind(ItemKind.ApplyScript)
        If script.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = $"{scriptHost} helper script",
                .Description = $"Papyrus script referenced by the NPCs' records: on each actor's first spawn it applies the {scriptHost} options that have no plugin equivalent (overlays, skin overrides{If(sse, ", node transforms", "")})." & vbCrLf &
                               $"Soft dependency: without {scriptLoader} + {scriptHost} it simply does nothing.",
                .Files = script})
        End If

        Dim bodyGen = byKind(ItemKind.BodyGenIni)
        If bodyGen.Count > 0 Then
            result.Add(New WizardComponent With {
                .Title = "BodyGen body morphs",
                .Description = $"BodyGen configuration ({scriptHost}): applies each NPC's body slider values in-game on first load.",
                .Files = bodyGen})
        End If

        Dim extras = byKind(ItemKind.ExtraAsset)
        If extras.Count > 0 Then
            Dim listing = String.Join(vbCrLf, extras.Take(12).Select(Function(p) "- " & p))
            If extras.Count > 12 Then listing &= vbCrLf & $"... and {extras.Count - 12} more"
            result.Add(New WizardComponent With {
                .Title = $"Extra assets ({extras.Count})",
                .Description = "Additional files included by the author:" & vbCrLf & listing,
                .Files = extras})
        End If

        Return result
    End Function

    ''' <summary>The About page text: name/version/author header, the author's description,
    ''' website, and ALWAYS the NPC_Manager credit + game-aware Nexus URL, separated by a plain
    ''' Unicode rule (portable: NMM/MO2/Vortex all render plain text + newlines; no HTML/BBCode
    ''' — Vortex and NMM would show raw tags).</summary>
    Public Function BuildAboutText(meta As FomodMetaSidecar.MetaFile,
                                   game As Config_App.Game_Enum,
                                   npcCount As Integer) As String
        Const Rule As String = "----------------------------"
        Dim sb As New Text.StringBuilder()
        sb.AppendLine($"{If(meta.ModName, "")}")
        sb.AppendLine($"v{If(meta.ModVersion, "")}{If(String.IsNullOrWhiteSpace(meta.Author), "", "  -  by " & meta.Author.Trim())}")
        sb.AppendLine($"{npcCount} NPC{If(npcCount = 1, "", "s")}")
        sb.AppendLine(Rule)
        sb.AppendLine()

        Dim desc = If(meta.Description, "").Trim()
        If desc.Length > 0 Then
            sb.AppendLine(desc)
            sb.AppendLine()
        End If

        If Not String.IsNullOrWhiteSpace(meta.Website) Then
            sb.AppendLine("Website: " & meta.Website.Trim())
            sb.AppendLine()
        End If

        sb.AppendLine(Rule)
        sb.AppendLine(CreditLine)
        Dim gameName = If(game = Config_App.Game_Enum.Skyrim, "Skyrim SE", "Fallout 4")
        sb.Append($"Get NPC_Manager for {gameName}:{vbCrLf}{NexusUrlFor(game)}")
        Return SanitizeXmlText(sb.ToString())
    End Function

    ''' <summary>⛔ UNA sola ley de nombres para los rescates: la usan el que APARTA y el que LIMPIA.
    ''' Escritas por separado, la limpieza dejaba vivos justo los nombres que el otro sabe crear. n=1 es
    ''' el primero y va SIN sufijo, que es como ya estan nombrados los que hay en disco.</summary>
    Private Function NombreDeRescate(zipPath As String, n As Integer) As String
        Return zipPath & ".recovered" & If(n <= 1, "", n.ToString())
    End Function

    ''' <summary>Cuantos rescates puede haber. Es el CONJUNTO CERRADO que este archivo sabe crear, y por
    ''' eso la limpieza lo puede barrer entero SIN comodines: un `.recovered*` a lo ancho se llevaria
    ''' puesto un archivo del usuario que este codigo nunca escribio.</summary>
    Private Const MaxRescates As Integer = 99

    ''' <summary>Aparta el paquete YA TERMINADO cuando el volcado sobre el destino se corto. Devuelve la
    ''' ruta donde quedo, o "" si no se pudo apartar — y ahi el .tmp SE QUEDA donde esta, que es mejor
    ''' que borrarlo. ⛔ Eso ultimo lo tiene que respetar el Finally de ExportToZip: cuando no lo
    ''' respetaba, el usuario perdia el zip viejo (truncado por el volcado) Y el nuevo (borrado aca).
    ''' <para>⛔ No pisa un rescate anterior: cada uno puede ser de una exportacion distinta. Esa es la
    ''' MISMA definicion que usa <see cref="LimpiarRescates"/> para decidir a quien puede borrar — un
    ''' rescate es un ejemplar unico salvo que se PRUEBE lo contrario. Las dos rutinas la consultan y
    ''' ninguna la re-redacta: cuando estaban escritas por separado se contradecian (esta decia que NO
    ''' son intercambiables y la otra los borraba a todos, que es decir que SI lo son).</para></summary>
    Private Function ApartarPaqueteSano(tmp As String, zipPath As String) As String
        Try
            If Not File.Exists(tmp) Then Return ""
            For n = 1 To MaxRescates
                Dim rescate = NombreDeRescate(zipPath, n)
                If Not File.Exists(rescate) Then
                    ' Puede tirar (MAX_PATH, permisos, un DIRECTORIO con ese nombre): lo agarra el Catch
                    ' y sale "", que es la senal de "no se pudo apartar".
                    File.Move(tmp, rescate)
                    Return rescate
                End If
            Next
            Return ""
        Catch
            Return ""
        End Try
    End Function

    ''' <summary>Un export que SALE BIEN borra de los rescates de ESE zip SOLO los que esta corrida
    ''' PROBO byte a byte iguales al paquete recien escrito. El que difiere SOBREVIVE.
    '''
    ''' <para>⛔ LA LEY NO VIVE ACA. Vive en <c>EscrituraEnElLugar.GuardarConCopia</c>
    ''' (EscrituraEnElLugar.vb:129-132) y esto la CONSULTA, con su mismo predicado
    ''' (<c>MismoContenido</c>). Antes estaba RE-REDACTADA aca, y por eso pudo divergir: este docstring
    ''' citaba «un guardado que sale bien limpia las dos» como fundamento, que es exactamente la version
    ''' que el mismo delta DEROGO POR DESTRUCTIVA (EscrituraEnElLugar.vb:133-137, «eso BORRABA EL ULTIMO
    ''' EJEMPLAR», con la medicion al lado). Con la ley muerta puesta, esto borraba TODOS los
    ''' `.recovered` del zip en cualquier export exitoso.</para>
    '''
    ''' <para>⛔ QUE SE PERDIA, en concreto. Un `.recovered` es un paquete TERMINADO que quedo de una
    ''' corrida cortada, y la app le dijo al usuario «rename it to X.zip to use it» (ver el mensaje del
    ''' estado (c) en <see cref="ExportToZip"/>). Si en vez de renombrarlo cambia la seleccion de NPCs y
    ''' exporta de nuevo con exito, ese paquete —cientos de MB, contenido IRREPETIBLE de OTRA
    ''' seleccion— desaparecia sin aviso.</para>
    '''
    ''' <para>⛔ Y ESTE ARCHIVO SE CONTRADECIA A SI MISMO sobre QUE ES un rescate:
    ''' <see cref="ApartarPaqueteSano"/> no pisa un rescate anterior «porque cada uno puede ser de una
    ''' exportacion distinta» —o sea: NO son intercambiables— y esto los borraba todos —o sea: SI lo
    ''' son—. Las dos afirmaciones no pueden ser verdad a la vez. Queda UNA sola definicion, y las dos
    ''' rutinas la consultan: <b>un rescate es un ejemplar unico del dato del usuario salvo que esta
    ''' corrida PRUEBE que no lo es.</b></para>
    '''
    ''' <para>⚠️ EL COSTO, dicho y no escondido: un rescate que difiere QUEDA EN DISCO hasta que el
    ''' usuario lo borre o lo renombre. Es el mismo costo que EscrituraEnElLugar.vb:138-141 ya declara y
    ''' acepta para su copia heredada — es el dato del usuario, y es el unico que lo tiene. La
    ''' alternativa (borrarlo igual) es el defecto que este parrafo documenta.</para>
    '''
    ''' <para>El BORRADO es best-effort: que no se pueda borrar uno NO convierte un export exitoso en un
    ''' error. La COMPARACION no lo es: si no se pudo comparar, <c>MismoContenido</c> devuelve False y el
    ''' rescate se queda, porque el fallo del comparador no puede costarle el dato al usuario.</para>
    ''' <para>Gate: <c>Tools\FomodVolcadoGate</c> (V-4c el que difiere sobrevive, V-4d el vecino que no
    ''' es un rescate sobrevive, V-4e el probado identico SI se borra).</para></summary>
    Private Sub LimpiarRescates(zipPath As String)
        For n = 1 To MaxRescates
            Try
                Dim r = NombreDeRescate(zipPath, n)
                ' El destino ya es el paquete bueno de ESTA corrida. Si un rescate es byte por byte igual
                ' a el, no guarda nada que el destino no guarde y es descartable; si difiere, es un
                ' ejemplar unico y se queda. Misma implicacion cerrada que GuardarConCopia, y es incluso
                ' mas conservadora: alla la referencia es descartable por construccion, aca es el
                ' entregable vivo.
                If File.Exists(r) AndAlso
                   BSA_BA2_Library_DLL.EscrituraEnElLugar.MismoContenido(r, zipPath) Then
                    File.Delete(r)
                End If
            Catch
                ' Best-effort, igual que EscrituraEnElLugar.Borrar: un huerfano no rompe nada.
            End Try
        Next
    End Sub

    ''' <summary>Stream the package into <paramref name="zipPath"/>. The package is built in a .tmp
    ''' sibling and only then dumped over the target, so cancelling or failing WHILE BUILDING never
    ''' touches the previous ZIP. ⚠️ The dump itself is NOT atomic: if it is cut after opening the
    ''' target, the previous ZIP is left truncated and the .tmp is the only sane copy — that case keeps
    ''' the finished package and the error says where it is (see the Catch below). Sources are
    ''' streamed (never materialized in RAM — BA2s can be multi-GB); archives get
    ''' CompressionLevel.Fastest (their payload is already
    ''' compressed), everything else Optimal. <paramref name="screenshotPng"/>: PNG bytes of the
    ''' preview capture, written as <see cref="ScreenshotEntryZipPath"/> when non-Nothing (the
    ''' caller passes Nothing when the checkbox is off or no preview was available — the
    ''' ModuleConfig then carries no image reference either). <paramref name="isCancelled"/> is
    ''' polled between files and aborts via OperationCanceledException.</summary>
    Public Sub ExportToZip(zipPath As String, manifest As IReadOnlyList(Of ManifestItem),
                           infoXml As XDocument, moduleConfigXml As XDocument,
                           screenshotPng As Byte(),
                           progress As Action(Of Integer, Integer, String),
                           isCancelled As Func(Of Boolean))
        Dim items = manifest.Where(Function(i) i.Exists).ToList()
        Dim hasShot = screenshotPng IsNot Nothing AndAlso screenshotPng.Length > 0
        Dim total = items.Count + 2 + If(hasShot, 1, 0)
        ' CUATRO estados, no tres. Los tres de antes seguian juntos los dos que mas se parecen y que
        ' piden respuestas OPUESTAS:
        '   a) fallo ARMANDO el zip (o el usuario cancelo)     -> el .tmp es basura y se borra;
        '   b) el volcado NO LLEGO A ABRIR el destino          -> el destino esta INTACTO: no se aparta
        '      nada y NO se le dice que quedo a medias, que lo mandaria a pisar un zip BUENO;
        '   c) el volcado se corto DESPUES de abrir el destino -> el destino quedo truncado y el .tmp es
        '      la UNICA copia sana: se APARTA con nombre propio (el reintento abre el mismo .tmp con
        '      File.Create y lo truncaria) y el error DICE DONDE QUEDO;
        '   d) salio todo bien                                 -> se borra el .tmp y se limpian los
        '      rescates que hayan quedado de un corte anterior.
        ' (b) de (c) las separa `destinoTocado`. Es la misma distincion que EscrituraEnElLugar hace con su
        ' `seToco` interno en GuardarConCopia, y por el mismo motivo: sin ella, o se avisa de una perdida
        ' que no paso, o no se recupera cuando si hacia falta.
        Dim volcando = False
        Dim volcado = False
        ' ⛔ CONTRATO de VolcarEncima: si este flag quedo en False y el volcado tiro, el destino esta
        ' BYTE-IDENTICO. El callback corre despues de abrir Y truncar, antes del primer byte copiado.
        Dim destinoTocado = False
        ' Si el apartado no se pudo hacer, el .tmp SE QUEDA: es lo que promete ApartarPaqueteSano y lo que
        ' el Finally borraba igual, dejando al usuario sin el zip viejo Y sin el nuevo.
        Dim conservarTmp = False
        Dim tmp = zipPath & ".tmp"
        Try
            Using zip As New ZipArchive(File.Create(tmp), ZipArchiveMode.Create)
                WriteXmlEntry(zip, "fomod/info.xml", infoXml)
                progress?.Invoke(1, total, "fomod/info.xml")
                WriteXmlEntry(zip, "fomod/ModuleConfig.xml", moduleConfigXml)
                progress?.Invoke(2, total, "fomod/ModuleConfig.xml")

                Dim done = 2
                If hasShot Then
                    ' PNG is already compressed — Fastest, same rationale as the archives.
                    Dim shotEntry = zip.CreateEntry(ScreenshotEntryZipPath, CompressionLevel.Fastest)
                    Using dst = shotEntry.Open()
                        dst.Write(screenshotPng, 0, screenshotPng.Length)
                    End Using
                    done += 1
                    progress?.Invoke(done, total, ScreenshotEntryZipPath)
                End If
                For Each item In items
                    If isCancelled IsNot Nothing AndAlso isCancelled() Then
                        Throw New OperationCanceledException()
                    End If
                    Dim entryName = item.DataRelativePath.Replace("\"c, "/"c)
                    Dim level = If(IsAlreadyCompressed(item.DataRelativePath),
                                   CompressionLevel.Fastest, CompressionLevel.Optimal)
                    Dim entry = zip.CreateEntry(entryName, level)
                    Using dst = entry.Open()
                        If item.SourceBytes IsNot Nothing Then
                            dst.Write(item.SourceBytes, 0, item.SourceBytes.Length)
                        Else
                            Using src = File.OpenRead(item.SourceFullPath)
                                src.CopyTo(dst)
                            End Using
                        End If
                    End Using
                    done += 1
                    progress?.Invoke(done, total, item.DataRelativePath)
                Next
            End Using
            ' El zip se arma en un .tmp para que cancelar a mitad no rompa el anterior, y despues se
            ' vuelca ENCIMA del destino (no un rename: el destino puede caer adentro de un mod, y ahi
            ' renombrar encima lo saca del mod bajo MO2 y corta el hardlink en Vortex).
            ' ⚠️ El volcado NO es atomico: abre el origen primero, asi que un fallo ANTES de abrir el
            ' destino lo deja intacto — pero si se corta a mitad de la copia, el zip anterior queda
            ' parcial y el .tmp es la UNICA copia sana. Por eso el .tmp se borra solo si el volcado
            ' termino bien.
            volcando = True
            ' ⛔ `Sub() destinoTocado = True` es una ASIGNACION: el cuerpo de un lambda `Sub()` es una
            ' sentencia. En un `Function()` la MISMA linea seria una comparacion, el flag no cambiaria
            ' nunca y el mensaje volveria a mentir — trampa de VB, anotada porque acá se paga cara.
            BSA_BA2_Library_DLL.EscrituraEnElLugar.VolcarEncima(
                tmp, zipPath, alTocarElDestino:=Sub() destinoTocado = True)
            volcado = True
            ' Estado (d): el destino ya es el paquete bueno de esta corrida, asi que un rescate que sea
            ' BYTE POR BYTE igual a el dejo de ser una copia unica y se puede descartar. El que difiere
            ' NO: es el paquete terminado de otra seleccion y no existe en ningun otro lado. La ley y su
            ' precio estan en el docstring de LimpiarRescates, que apunta a EscrituraEnElLugar.
            LimpiarRescates(zipPath)
        Catch ex As Exception When volcando AndAlso Not volcado
            If Not destinoTocado Then
                ' Estado (b): el volcado ni llego a abrir el destino, asi que esta INTACTO. No se aparta
                ' el paquete —seria un .recovered muerto de cientos de MB por cada intento, y no los borra
                ' nadie— y sobre todo NO se le dice que quedo a medias: eso lo mandaba a pisar su zip
                ' BUENO con uno viejo. Misma decision que GuardarConCopia toma con `Not seToco`.
                ' El ex.Message va EMBEBIDO porque el dialogo muestra solo .Message, no el inner
                ' (FomodExport_Form.vb:355), y sin la causa el usuario no sabe que cerrar.
                Throw New IOException(
                    $"'{Path.GetFileName(zipPath)}' was NOT modified: the package could not be written " &
                    "over it. Nothing on disk was changed — close whatever is holding that file, or fix " &
                    "the cause below, and export again." & Environment.NewLine & ex.Message, ex)
            End If
            ' Estado (c): el destino quedo truncado y el .tmp es el paquete TERMINADO. Se aparta con
            ' nombre propio y el error dice donde quedo — si no, el usuario no tiene forma de saber que
            ' su export esta ahi y el proximo intento lo pisaria.
            Dim rescate = ApartarPaqueteSano(tmp, zipPath)
            If rescate <> "" Then
                Throw New IOException(
                    $"'{Path.GetFileName(zipPath)}' was left incomplete. The finished package was saved " &
                    $"next to it as '{Path.GetFileName(rescate)}' — rename it to " &
                    $"'{Path.GetFileName(zipPath)}' to use it.", ex)
            End If
            ' Ni apartar se pudo: el .tmp SE QUEDA —contrato de ApartarPaqueteSano— y el mensaje dice
            ' donde esta. Antes salia el error crudo del volcado y el Finally borraba el paquete: el
            ' usuario se quedaba sin el zip viejo (truncado) y sin el nuevo, y sin enterarse de ninguna
            ' de las dos cosas.
            conservarTmp = True
            Throw New IOException(
                $"'{Path.GetFileName(zipPath)}' was left incomplete and the finished package could not " &
                $"be renamed. It is still next to it as '{Path.GetFileName(tmp)}' — rename that file to " &
                $"'{Path.GetFileName(zipPath)}' to use it." & Environment.NewLine & ex.Message, ex)
        Finally
            ' Lo que quede del .tmp en cualquier otro camino (cancelado, fallo armando el zip, destino
            ' INTACTO, exito) es basura. Si el Catch lo aparto, aca ya no esta; y si el Catch NO lo pudo
            ' apartar, `conservarTmp` lo protege: es la unica copia del paquete que existe.
            If Not conservarTmp Then
                Try
                    If File.Exists(tmp) Then File.Delete(tmp)
                Catch
                    ' Best-effort cleanup — a leftover .tmp is harmless and overwritten next run.
                End Try
            End If
        End Try
    End Sub

    Private Sub WriteXmlEntry(zip As ZipArchive, entryName As String, doc As XDocument)
        ' UTF-8 WITHOUT a BOM: the conventional form for FOMOD info.xml/ModuleConfig.xml (Vortex,
        ' the FOMOD Creation Tool and hand-authored files all omit it). Plain doc.Save(stream)
        ' would emit a UTF-8 BOM, which some older/stricter FOMOD readers mishandle. The XML
        ' declaration still carries encoding="utf-8" so parsers are unambiguous.
        Dim entry = zip.CreateEntry(entryName, CompressionLevel.Optimal)
        Using dst = entry.Open()
            Dim settings As New Xml.XmlWriterSettings With {
                .Encoding = New Text.UTF8Encoding(encoderShouldEmitUTF8Identifier:=False),
                .Indent = True
            }
            Using writer = Xml.XmlWriter.Create(dst, settings)
                doc.Save(writer)
            End Using
        End Using
    End Sub

    ''' <summary>Bethesda archives carry their own zlib/LZ4 payload — deflating them again wastes
    ''' CPU for ~0 gain, so they go in at Fastest.</summary>
    Private Function IsAlreadyCompressed(relPath As String) As Boolean
        Return relPath.EndsWith(".ba2", StringComparison.OrdinalIgnoreCase) OrElse
               relPath.EndsWith(".bsa", StringComparison.OrdinalIgnoreCase)
    End Function

    ''' <summary>Strip control characters XML 1.0 cannot represent (XDocument would throw on
    ''' them). Tab/CR/LF are kept — the description is multiline.</summary>
    Public Function SanitizeXmlText(text As String) As String
        If String.IsNullOrEmpty(text) Then Return ""
        Dim sb As New Text.StringBuilder(text.Length)
        For Each ch In text
            If ch >= " "c OrElse ch = ChrW(9) OrElse ch = ChrW(10) OrElse ch = ChrW(13) Then sb.Append(ch)
        Next
        Return sb.ToString()
    End Function

End Module
