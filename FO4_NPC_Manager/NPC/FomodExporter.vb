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

        Dim addDisk = Sub(kind As ItemKind, relPath As String, required As Boolean, note As String)
                          If String.IsNullOrEmpty(relPath) OrElse Not seen.Add(relPath) Then Return
                          Dim full = Path.Combine(dataPath, relPath)
                          Dim onDisk = File.Exists(full)
                          manifest.Add(New ManifestItem With {
                              .Kind = kind, .DataRelativePath = relPath, .SourceFullPath = full,
                              .Required = required, .Exists = onDisk,
                              .SizeBytes = If(onDisk, New FileInfo(full).Length, 0L),
                              .Note = note})
                      End Sub

        ' 1. The plugin itself.
        addDisk(ItemKind.Plugin, pluginFileName, True, "")

        Dim baseName = Path.GetFileNameWithoutExtension(pluginFileName)

        ' 2. Archives (normal mode) or loose FaceGen files (loose-only mode). Same game-aware
        '    naming ArchivePackager.Pack used when Save ESP packed the bake: FO4 = Main + Textures
        '    BA2 pair, SSE = a single BSA.
        If Not NPC_Config.IsLooseOnly(game) Then
            If game = Config_App.Game_Enum.Skyrim Then
                addDisk(ItemKind.Archive, baseName & ArchivePackager.EXT_BSA, True, "")
            Else
                addDisk(ItemKind.Archive, baseName & ArchivePackager.SUFFIX_BA2_MAIN, True, "")
                addDisk(ItemKind.Archive, baseName & ArchivePackager.SUFFIX_BA2_TEXTURES, True, "")
            End If
            ' Companion slots the packer may have produced ("<base>2.esp" + its archives). NPC_Manager
            ' packs SingleAnchorOnly so none are expected today, but if they exist they are part of the
            ' mod — without them a numbered archive would not load on the end user's machine.
            Try
                Dim setInfo = ArchivePackager.DiscoverArchiveSet(dataPath, baseName)
                For Each archivePath In setInfo.Archives
                    addDisk(ItemKind.Archive, Path.GetFileName(archivePath), False, "companion of the archive set")
                Next
                For Each pluginPath In setInfo.Plugins
                    addDisk(ItemKind.Plugin, Path.GetFileName(pluginPath), False, "companion of the archive set")
                Next
            Catch
                ' Discovery is best-effort sugar; the required set above already covers the anchor.
            End Try
        ElseIf npcFormIDs IsNot Nothing AndAlso pluginManager IsNot Nothing Then
            ' Loose-only: include every FaceGen file that EXISTS on disk for the plugin's NPCs.
            ' Paths are per ORIGIN plugin + local FormID (game-aware, ESL-aware) — the exact same
            ' source of truth the packer/delete flows use.
            For Each fid In npcFormIDs
                For Each entry In NpcFaceGenPacker.CanonicalFaceGenEntryPathsForNpc(fid, pluginManager, game)
                    If File.Exists(Path.Combine(dataPath, entry)) Then
                        addDisk(ItemKind.FaceGenLoose, entry, False, "")
                    End If
                Next
            Next
        End If

        ' 3. Preset sidecar (.bssliders). Required when present; a plugin with no body data has no
        '    sidecar on disk (BssliderSidecar.Write drops empty files) — that is NOT an error, the
        '    grid just shows it as absent.
        Dim sidecarRel = baseName & BssliderSidecar.Extension
        If File.Exists(Path.Combine(dataPath, sidecarRel)) Then
            addDisk(ItemKind.PresetSidecar, sidecarRel, True, "character preset sidecar")
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
        Dim pexBytes = NpcApplyScriptEmitter.PexBytes(game)
        manifest.Add(New ManifestItem With {
            .Kind = ItemKind.ApplyScript,
            .DataRelativePath = "Scripts\" & NpcApplyScriptEmitter.ScriptNameFor(game) & ".pex",
            .SourceBytes = pexBytes, .Required = True,
            .Exists = (pexBytes IsNot Nothing AndAlso pexBytes.Length > 0),
            .SizeBytes = If(pexBytes IsNot Nothing, CLng(pexBytes.Length), 0L),
            .Note = "embedded apply-script (" & If(game = Config_App.Game_Enum.Skyrim, "RaceMenu", "LooksMenu") & ")"})

        ' 5. BodyGen inis, when Save ESP emitted them. Folder name uses the plugin file name WITH
        '    extension (engine convention — see BodyGenIniWriter/SseBodyGenIniWriter).
        Dim bodyGenDir = If(game = Config_App.Game_Enum.Skyrim,
                            "Meshes\actors\character\BodyGenData\" & pluginFileName,
                            "F4SE\Plugins\F4EE\BodyGen\" & pluginFileName)
        For Each ini In {"templates.ini", "morphs.ini"}
            Dim rel = bodyGenDir & "\" & ini
            If File.Exists(Path.Combine(dataPath, rel)) Then
                addDisk(ItemKind.BodyGenIni, rel, False, "BodyGen morphs")
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
                    addDisk(ItemKind.ExtraAsset, asset, True, "added by author")
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
                    errors.Add("The embedded apply-script (.pex) is missing from this build — see Papyrus\README.md.")
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
                .Title = $"{scriptHost} apply script",
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

    ''' <summary>Stream the package into <paramref name="zipPath"/>. Atomic: writes to a .tmp
    ''' sibling and moves over the target only on success, so a failure/cancel never leaves a
    ''' half-written ZIP under the final name. Sources are streamed (never materialized in RAM —
    ''' BA2s can be multi-GB); archives get CompressionLevel.Fastest (their payload is already
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
        Dim tmp = zipPath & ".tmp"
        Dim moved = False
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
            File.Move(tmp, zipPath, overwrite:=True)
            moved = True
        Finally
            If Not moved Then
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
