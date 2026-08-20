Imports System.IO
Imports FO4_Base_Library

''' <summary>Dialog for picking a LooksMenu (FO4) / RaceMenu (SSE) chargen preset to apply to the currently
''' selected NPC, WITH a per-category control panel next to the list.
'''
''' Behaviour:
'''   - On Load: scans Data\F4SE\Plugins\F4EE\Presets\*.json (FO4) or Data\SKSE\Plugins\CharGen\Presets\
'''     *.jslot (SSE), parses each and populates the list filtered by the NPC's gender (FO4 only — RaceMenu
'''     presets carry no gender flag). Presets that fail to parse are skipped and logged.
'''   - User types in the filter box: live-filter list by filename substring (case-insensitive).
'''   - User selects an entry: the shared <see cref="PresetCategoryPanel"/> shows what that preset carries
'''     per category (head parts, tints, morphs, BodySlide sliders, overlays, …) and greys out the
'''     categories it carries nothing for; <see cref="LabelInfo"/> states whether anything of it is missing
'''     or incompatible, and "What this preset does..." opens the per-item report.
'''     Everything the user READS goes through <see cref="FileView"/> = the FILE's own content, in both
'''     games. That matters under SSE: the preset the list holds is the .jslot mapped onto a clone of the
'''     pre-dialog overlay (mandatory — see <see cref="SsePresetMapping"/>), so without this the amounts and
'''     findings would attribute the NPC's own look to the preset. Preview and OK use the merged one.
'''   - Selecting an entry OR toggling any category fires <see cref="PreviewRequested"/> so the caller can
'''     apply the filtered overlay live to the preview. Unticked categories keep whatever the NPC shows
'''     today — the merge is <see cref="PresetCategoryFilter.BuildFiltered"/>, the same one Paste Look uses.
'''   - OK: <see cref="SelectedPreset"/> is the parsed object and <see cref="SelectedOptions"/> the category
'''     selection; the last-previewed overlay stays.
'''   - Cancel: caller restores its pre-dialog snapshot (the live preview is already on the wrong preset, so
'''     the caller must explicitly roll back).
''' </summary>
Public Class LooksmenuLoad_Form

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _dataPath As String
    Private ReadOnly _gender As Byte
    Private ReadOnly _allPresets As New List(Of LooksmenuLoader.LooksmenuPreset)

    ' SSE (RaceMenu) mode: instead of scanning F4EE\Presets\*.json, scan the RaceMenu preset dir for *.jslot
    ' and map each to a LooksmenuPreset via the host-supplied mapper (which loads the .jslot + maps it onto a
    ' clone of the pre-dialog overlay so prior NPC fields survive). Everything else — list, filter, race-compat,
    ' category panel, live preview, OK — is shared with the FO4 path. Game-aware, NOT a separate window.
    Private ReadOnly _isSse As Boolean
    Private ReadOnly _ssePresetsDir As String
    Private ReadOnly _sseMapper As Func(Of String, SsePresetMapping)

    ''' <summary>The two readings of one <c>.jslot</c> the browser needs, produced from a SINGLE load of the
    ''' file (the parse + the per-vertex sculpt conversion is the expensive part; re-mapping the parsed jslot
    ''' onto a second base is in-memory work).</summary>
    Public Class SsePresetMapping
        ''' <summary>Mapped onto a clone of the pre-dialog overlay: what the NPC ENDS UP with. This is the
        ''' object the list holds, the preview applies and OK returns. The clone is not optional — several
        ''' .jslot fields can't express "absent" (NAM9 is a fixed 18-slot vector where 0 is a real value), so
        ''' the mapper seeds from the previous value and overwrites only what the file declares.</summary>
        Public ReadOnly Applied As LooksmenuLoader.LooksmenuPreset
        ''' <summary>Mapped onto an EMPTY preset: what the FILE itself carries. Everything the user READS goes
        ''' through this one (category amounts, race-compat filter, "Show incompatible"), so a count or a
        ''' finding is never the NPC's own content misattributed to the preset — which is what the FO4 path
        ''' gets for free by parsing the .json and nothing else.</summary>
        Public ReadOnly FileOnly As LooksmenuLoader.LooksmenuPreset
        Public Sub New(applied As LooksmenuLoader.LooksmenuPreset, fileOnly As LooksmenuLoader.LooksmenuPreset)
            Me.Applied = applied
            Me.FileOnly = fileOnly
        End Sub
    End Class

    ''' <summary>Applied preset → its file-only twin. Populated at list-build time; <see cref="FileView"/> is
    ''' the only reader. Empty under FO4 (there the preset already IS the file).</summary>
    Private ReadOnly _fileOnlyView As New Dictionary(Of LooksmenuLoader.LooksmenuPreset, LooksmenuLoader.LooksmenuPreset)

    ''' <summary>True when no shape of the NPC's body NIF carries BODYTRI extra-data: BodySlide sliders can
    ''' still be loaded, they just won't show in-game. Surfaced as a note instead of forcing the category off.</summary>
    Private ReadOnly _npcHasBodyTri As Boolean

    ' F4SE catalogs (FO4) the host supplies so the compatibility audit can tell "this overlay/skin template
    ' isn't installed" apart from "not checked". Nothing = the host couldn't supply them; the report says so
    ' instead of accusing a template of being missing.
    Private ReadOnly _knownOverlayTemplateIds As HashSet(Of String)
    Private ReadOnly _knownLmSkinTemplateIds As HashSet(Of String)

    ''' <summary>Per-preset compatibility audit (<see cref="PresetCompatibilityReport"/>), memoized: the
    ''' selection handler runs it on every click and the result is invariant for the dialog's lifetime
    ''' (race + gender + NPC are fixed at construction).</summary>
    Private ReadOnly _auditCache As New Dictionary(Of LooksmenuLoader.LooksmenuPreset, PresetCompatibilityReport.PresetAuditReport)

    ' Race-compatibility filter inputs (optional — Nothing means race info wasn't supplied
    ' and the checkbox stays disabled because we can't compute compatibility).
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _race As RACE_Data
    Private ReadOnly _raceDisplayName As String = ""
    Private ReadOnly _raceDefaults As HashSet(Of UInteger)
    ' FLST cache reused across IsHdptValidForRace calls so each FLST is parsed once per session.
    Private ReadOnly _flstCache As New Dictionary(Of UInteger, Canon.FormListRecord)
    ' Compatibility memoization — preset → bool. Each preset is checked once even if the
    ' user toggles the checkbox / re-runs ApplyFilter via the text-filter handler.
    Private ReadOnly _compatibilityCache As New Dictionary(Of LooksmenuLoader.LooksmenuPreset, Boolean)

    ''' <summary>The preset the user picked. Nothing if the dialog was cancelled.</summary>
    Public Property SelectedPreset As LooksmenuLoader.LooksmenuPreset

    ''' <summary>Which categories of the picked preset the user wants applied. Categories the preset doesn't
    ''' carry (or that don't exist in this game) come back False, so the merge preserves the NPC's value.</summary>
    Public ReadOnly Property SelectedOptions As PresetCategoryOptions
        Get
            Return CategoryPanel.Options
        End Get
    End Property

    ''' <summary>Fired on every list selection change OR category toggle so the host form can apply the
    ''' preset live as a preview. Preset is Nothing when nothing is selected. The host should snapshot any
    ''' prior overlay state before showing the dialog so it can restore on Cancel — and must use that same
    ''' snapshot as the preserve baseline, NOT the current overlay (which this preview keeps rewriting).</summary>
    Public Event PreviewRequested As EventHandler(Of PreviewRequestArgs)

    Public Class PreviewRequestArgs
        Public ReadOnly Preset As LooksmenuLoader.LooksmenuPreset
        Public ReadOnly Options As PresetCategoryOptions
        Public Sub New(p As LooksmenuLoader.LooksmenuPreset, opts As PresetCategoryOptions)
            Preset = p
            Options = opts
        End Sub
    End Class

    Public Sub New(pluginManager As PluginManager,
                   dataPath As String,
                   gender As Byte,
                   raceDisplayName As String,
                   npcHasBodyTri As Boolean,
                   Optional raceFormID As UInteger = 0UI,
                   Optional race As RACE_Data = Nothing,
                   Optional raceDefaultHeadPartFormIDs As IEnumerable(Of UInteger) = Nothing,
                   Optional isSse As Boolean = False,
                   Optional ssePresetsDir As String = Nothing,
                   Optional sseMapper As Func(Of String, SsePresetMapping) = Nothing,
                   Optional knownOverlayTemplateIds As IEnumerable(Of String) = Nothing,
                   Optional knownLmSkinTemplateIds As IEnumerable(Of String) = Nothing)
        InitializeComponent()
        _pluginManager = pluginManager
        _dataPath = dataPath
        _gender = gender
        _isSse = isSse
        _ssePresetsDir = ssePresetsDir
        _sseMapper = sseMapper
        _npcHasBodyTri = npcHasBodyTri
        _raceFormID = raceFormID
        _race = race
        _raceDisplayName = If(raceDisplayName, "")
        _raceDefaults = New HashSet(Of UInteger)
        If raceDefaultHeadPartFormIDs IsNot Nothing Then
            For Each fid In raceDefaultHeadPartFormIDs
                _raceDefaults.Add(fid)
            Next
        End If
        ' Ordinal comparison on purpose: both catalogs key by an id the engine matches with a plain
        ' string compare (OverlayInterface / SkinInterface), so case matters here too.
        If knownOverlayTemplateIds IsNot Nothing Then _knownOverlayTemplateIds = New HashSet(Of String)(knownOverlayTemplateIds, StringComparer.Ordinal)
        If knownLmSkinTemplateIds IsNot Nothing Then _knownLmSkinTemplateIds = New HashSet(Of String)(knownLmSkinTemplateIds, StringComparer.Ordinal)

        Text = If(_isSse, "Load RaceMenu Preset", "Load LooksMenu Preset")

        ' Informational header. Presets live in a single flat folder and are race-agnostic at the
        ' file-system level, but the engine silently drops HDPTs / tints whose RACE doesn't accept
        ' them. The "Show only race-compatible" checkbox lets the user hide presets that would
        ' partially-apply for this NPC.
        Dim presetsFolderText As String = If(_isSse, "Listing all RaceMenu presets from Data\SKSE\Plugins\CharGen\Presets\.",
                                                       "Listing all presets from Data\F4SE\Plugins\F4EE\Presets\.")
        LabelHeader.Text = $"Target NPC race: {raceDisplayName}  •  Gender: {If(gender = 1, "Female", "Male")}" & vbCrLf &
                           presetsFolderText & "  Tick on the right what to take from the selected preset; unticked categories keep this NPC's current look."

        ' Category panel: same control Paste Look hosts. Configure the game once, before any SetPreset call,
        ' so the FO4-only / SSE-only rows collapse. Every toggle re-fires the preview.
        CategoryPanel.ConfigureGame(_isSse)
        AddHandler CategoryPanel.OptionsChanged, AddressOf OnCategoriesChanged

        ' Race-compatibility checkbox: only meaningful when caller supplied race data. Without
        ' RACE_Data we can't validate tints, and without raceFormID we can't validate HDPTs —
        ' so disable + uncheck rather than pretending to filter.
        If _raceFormID = 0UI OrElse _race Is Nothing Then
            CheckBoxRaceCompatible.Enabled = False
            CheckBoxRaceCompatible.Checked = False
        End If

        AddHandler CheckBoxRaceCompatible.CheckedChanged, AddressOf OnRaceCompatibleToggled
    End Sub

    Private Sub OnRaceCompatibleToggled(sender As Object, e As EventArgs)
        ApplyFilter()
    End Sub

    ''' <summary>A category toggle must re-fire the preview so the host rebuilds the overlay with the new
    ''' selection and re-renders — otherwise the checkboxes would have no visible effect until OK.</summary>
    Private Sub OnCategoriesChanged(sender As Object, e As EventArgs)
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        RaisePreview(item?.Preset)
    End Sub

    Private Sub LooksmenuLoad_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        EnsureRoomForCategoryPanel()
        LoadPresetList()
    End Sub

    ''' <summary>Grow the dialog's minimum height until the category panel actually fits.
    '''
    ''' <para>The panel's last row (Select all / Deselect all) is a percent-sized row: when the GroupBox is
    ''' shorter than the panel's content, that row is squeezed to zero and the FlowLayoutPanel — AutoSize —
    ''' still paints its buttons, OUTSIDE the panel and over the GroupBox's bottom border, which is what
    ''' "something below the right-hand buttons is erasing the group frame" looks like. Measured: the panel
    ''' wants ~494px (its <see cref="PresetCategoryPanel.PreferredPanelHeight"/> + the GroupBox chrome) and the
    ''' old 620px minimum left it 477. Both games were short — FO4 by the same 17px, just less visibly.</para>
    '''
    ''' <para>Derived from the panel instead of hard-coded so a new category row can't silently reintroduce it.
    ''' Width is untouched: the 900px minimum is a deliberate request (the list starts narrow).</para></summary>
    Private Sub EnsureRoomForCategoryPanel()
        Root.PerformLayout()
        ' Both "chrome" terms are MEASURED, not assumed: the GroupBox's is caption + borders + padding, which
        ' depends on the running font, and the window's is the title bar + borders. Guessing either is how the
        ' panel ends up a few pixels short — and a few pixels short is exactly when its action row overflows.
        Dim groupChrome As Integer = CategoriesGroup.Height - CategoriesGroup.DisplayRectangle.Height
        Dim chrome As Integer = Height - ClientSize.Height
        ' Rows 1..4 are the GroupBox's span; rows 0 (header) and 5 (OK/Cancel) are what it does NOT get.
        Dim rowsOutsideGroup As Integer = CInt(Root.RowStyles(0).Height + Root.RowStyles(5).Height)
        Dim needed As Integer = rowsOutsideGroup + groupChrome + CategoryPanel.PreferredPanelHeight +
                                Root.Padding.Vertical + chrome
        If MinimumSize.Height < needed Then MinimumSize = New Size(MinimumSize.Width, needed)
        If Height < MinimumSize.Height Then Height = MinimumSize.Height
    End Sub

    Private Sub LoadPresetList()
        _allPresets.Clear()
        ListBoxPresets.Items.Clear()

        If _isSse Then
            ' RaceMenu: scan Data\SKSE\Plugins\CharGen\Presets\*.jslot, map each via the host mapper (which
            ' loads the .jslot + maps it onto a clone of the pre-dialog overlay + sets SourcePath/Gender). RaceMenu
            ' presets carry no gender flag → no gender filter (the mapper stamps Gender = the NPC's so any later
            ' gender logic stays consistent); race-compat filtering still applies (headParts vs RACE).
            If Not String.IsNullOrEmpty(_ssePresetsDir) AndAlso Directory.Exists(_ssePresetsDir) AndAlso _sseMapper IsNot Nothing Then
                ' Recurse into subfolders (AllDirectories): users group .jslot presets by race/author, same as
                ' the LooksMenu path (LooksmenuLoader.EnumeratePresetFiles). RaceMenu's own loader takes a full
                ' path regardless of nesting, so subfoldered presets apply cleanly.
                For Each fp In Directory.GetFiles(_ssePresetsDir, "*.jslot", SearchOption.AllDirectories)
                    Dim mapping = _sseMapper(fp)
                    If mapping Is Nothing OrElse mapping.Applied Is Nothing Then
                        Dim fpLocal = fp
                        Logger.LogLazy(Function() $"[LMLoad] DROP '{Path.GetFileName(fpLocal)}': RaceMenu mapper returned Nothing (jslot failed to load/map).")
                        Continue For
                    End If
                    ' Register the file-only twin here (not lazily on selection) because the race-compat filter
                    ' runs over every preset before any selection exists.
                    If mapping.FileOnly IsNot Nothing Then _fileOnlyView(mapping.Applied) = mapping.FileOnly
                    _allPresets.Add(mapping.Applied)
                Next
            End If
        Else
            Dim files = LooksmenuLoader.EnumeratePresetFiles(_dataPath)
            For Each fp In files
                Dim parsed = LooksmenuLoader.ParseFile(fp, _pluginManager)
                If parsed Is Nothing Then
                    Dim fpLocal = fp
                    Logger.LogLazy(Function() $"[LMLoad] DROP '{Path.GetFileName(fpLocal)}': failed to parse (invalid/unsupported JSON).")
                    Continue For
                End If
                ' Skip presets whose declared gender doesn't match the NPC. CharGenInterface.cpp:301
                ' rejects mismatched gender at LoadPreset time — same rule here so the user only sees
                ' presets that will actually apply cleanly.
                If parsed.Gender <> _gender Then
                    Dim fpLocal = fp
                    Dim g = parsed.Gender
                    Logger.LogLazy(Function() $"[LMLoad] DROP '{Path.GetFileName(fpLocal)}': gender mismatch (preset gender={If(g = 1, "Female", "Male")} ({g}), target NPC gender={If(_gender = 1, "Female", "Male")} ({_gender})).")
                    Continue For
                End If
                _allPresets.Add(parsed)
            Next
        End If

        _allPresets.Sort(Function(a, b) String.Compare(Path.GetFileName(a.SourcePath), Path.GetFileName(b.SourcePath), StringComparison.OrdinalIgnoreCase))
        ApplyFilter()
    End Sub

    Private Sub TextBoxFilter_TextChanged(sender As Object, e As EventArgs) Handles TextBoxFilter.TextChanged
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        Dim raceFilterOn As Boolean = CheckBoxRaceCompatible.Enabled AndAlso CheckBoxRaceCompatible.Checked
        ListBoxPresets.BeginUpdate()
        Try
            ListBoxPresets.Items.Clear()
            Dim needle = TextBoxFilter.Text.Trim()
            For Each preset In _allPresets
                Dim displayName = Path.GetFileNameWithoutExtension(preset.SourcePath)
                If needle.Length > 0 AndAlso displayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0 Then Continue For
                If raceFilterOn AndAlso Not IsCompatibleWithTargetRace(preset) Then Continue For
                ListBoxPresets.Items.Add(New PresetItem(preset, displayName))
            Next
        Finally
            ListBoxPresets.EndUpdate()
        End Try

        ListBoxPresets.ClearSelected()
        UpdateInfo(Nothing)
        ButtonOk.Enabled = False
    End Sub

    ''' <summary>What this preset SAYS — the file's own content. Under FO4 that is the preset itself (the
    ''' parser reads the .json and nothing else); under SSE it is the file-only mapping, because the preset the
    ''' list holds has the NPC's current look underneath it. Everything the user reads goes through here;
    ''' the preview and OK deliberately do NOT (they need the merged one).</summary>
    Private Function FileView(preset As LooksmenuLoader.LooksmenuPreset) As LooksmenuLoader.LooksmenuPreset
        If preset Is Nothing Then Return Nothing
        Dim v As LooksmenuLoader.LooksmenuPreset = Nothing
        If _fileOnlyView.TryGetValue(preset, v) Then Return v
        Return preset
    End Function

    ''' <summary>Memoized wrapper so the strict HeadPart + FaceTint check runs at most once per
    ''' preset per session. Each preset's compatibility is invariant for the lifetime of this
    ''' dialog (race + gender are fixed at construction).</summary>
    Private Function IsCompatibleWithTargetRace(preset As LooksmenuLoader.LooksmenuPreset) As Boolean
        Dim cached As Boolean
        If _compatibilityCache.TryGetValue(preset, cached) Then Return cached
        ' The base head (Face, PartType=1) gates like every other part — no exemption. skee applies the
        ' SAME race check to every head part, the face included (PresetInterface.cpp ApplyPresetData:165-175:
        ' gender flag, then `if (part->validRaces) if (part->validRaces->Visit(ValidRaceFinder(race)))
        ' ChangeHeadPart(part)`), so a preset carrying FemaleHeadBreton simply never applies it to a Nord.
        ' The old ignoreFaceBaseHeadPart:=_isSse exemption let that cross-race head through the browser and
        ' into HeadPartFormIDs, where the render's own race filter dropped it — leaving the NPC with NO head
        ' at all (measured: 18 of 25 vanilla-adjacent presets carry a foreign base head).
        ' Unticking "Show only race-compatible" still lists every preset — the filter is the user's choice.
        ' Judged on the FILE's head parts: under SSE the merged preset falls back to the NPC's own parts for
        ' every slot the .jslot doesn't fill, and hiding a preset over parts the NPC already wears would be a
        ' verdict about the NPC, not about the preset. (Same list whenever the .jslot does declare parts — the
        ' mapper replaces the whole list then — so this doesn't change which presets are listed today.)
        Dim result = HeadPartResolver.IsPresetCompatibleWithRace(
            FileView(preset), _raceFormID, _gender = 1, _pluginManager, _race, _flstCache, _raceDefaults)
        _compatibilityCache(preset) = result
        Return result
    End Function

    Private Sub ListBoxPresets_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBoxPresets.SelectedIndexChanged
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        ButtonOk.Enabled = item IsNot Nothing
        ' Refresh the per-category amounts BEFORE previewing: the panel decides which categories are
        ' selectable for this preset, and Options (read by RaisePreview) depends on that.
        ' Fed the FILE view: the amounts must describe what this preset brings. A category the file doesn't
        ' carry is therefore greyed out ⇒ Options reports False ⇒ the merge preserves the NPC's current value —
        ' which is the same value the merged preset was carrying for it anyway, so the applied result is
        ' unchanged; only the number the user reads is now honest.
        CategoryPanel.SetPreset(FileView(item?.Preset))
        UpdateInfo(item?.Preset)
        RaisePreview(item?.Preset)
    End Sub

    Private Sub RaisePreview(preset As LooksmenuLoader.LooksmenuPreset)
        RaiseEvent PreviewRequested(Me, New PreviewRequestArgs(preset, CategoryPanel.Options))
    End Sub

    ''' <summary>Provenance + the SHORT verdict for the selected preset. The per-category amounts live in the
    ''' panel on the right; the exhaustive per-item breakdown lives behind "Show incompatible"
    ''' (<see cref="PresetCompatibilityReport"/>), so this line only states THAT something is missing —
    ''' enumerating it here never fit in two lines and hid most of it.</summary>
    Private Sub UpdateInfo(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then
            Dim emptyFolder = If(_isSse, "Data\SKSE\Plugins\CharGen\Presets\", "Data\F4SE\Plugins\F4EE\Presets\")
            LabelInfo.Text = If(_allPresets.Count = 0,
                                If(_isSse, $"No RaceMenu (.jslot) presets found in {emptyFolder}.",
                                           $"No {If(_gender = 1, "female", "male")} presets found in {emptyFolder}."),
                                "Select a preset to see what it carries.")
            LabelInfo.ForeColor = SystemColors.GrayText
            ButtonShowIncompatible.Enabled = False
            Return
        End If

        ' The file path was dropped from this line: the list already shows the preset name and the full path is
        ' in the report header, so all this line has to carry is the verdict — which the button then expands.
        ' ⛔⛔ ESTO GATEABA EL CARTEL CON `audit.Count`, QUE INCLUYE LAS NOTE. `MissingCount`/`HasMissing` existen
        ' justo para esta distinción y no se usaban acá. Con la nota nueva de huesos —que dispara en casi todo preset
        ' SSE— un preset PERFECTAMENTE compatible mostraba un "Incompatibility found" ámbar, mientras el cuerpo del
        ' reporte, a un clic, decía "0 findings that will NOT reach the NPC, 1 note". El usuario le cree al cartel y
        ' se saltea un preset bueno. Tres estados, no dos.
        Dim audit = GetAudit(preset)
        ' ⛔ SE DESHABILITABA CUANDO NO HABÍA HALLAZGOS. Con el rótulo viejo ("Show incompatible") tenía sentido; con
        ' "What this preset does..." un botón gris significa "no podés preguntar qué hace". Y el reporte ya sabe
        ' manejar el caso vacío ("No missing or incompatible content found for this NPC.").
        ButtonShowIncompatible.Enabled = True
        If audit.HasMissing Then
            LabelInfo.Text = If(audit.MissingCount = 1, "1 thing won't reach this NPC", $"{audit.MissingCount} things won't reach this NPC")
            LabelInfo.ForeColor = Drawing.Color.DarkGoldenrod
        ElseIf audit.Count > 0 Then
            LabelInfo.Text = If(audit.Count = 1, "Fully compatible — 1 thing worth reading", $"Fully compatible — {audit.Count} things worth reading")
            LabelInfo.ForeColor = SystemColors.ControlText
        Else
            LabelInfo.Text = "Fully compatible"
            LabelInfo.ForeColor = SystemColors.ControlText
        End If
    End Sub

    ''' <summary>Memoized compatibility audit for one preset. Computed lazily on selection (the record parsing
    ''' + FLST walks are the same ones the race filter already does, so this is cheap and reuses
    ''' <see cref="_flstCache"/>).</summary>
    Private Function GetAudit(preset As LooksmenuLoader.LooksmenuPreset) As PresetCompatibilityReport.PresetAuditReport
        Dim cached As PresetCompatibilityReport.PresetAuditReport = Nothing
        If _auditCache.TryGetValue(preset, cached) Then Return cached
        Dim ctx As New PresetCompatibilityReport.PresetAuditContext With {
            .Preset = FileView(preset),
            .IsSse = _isSse,
            .PluginManager = _pluginManager,
            .DataPath = _dataPath,
            .RaceFormID = _raceFormID,
            .Race = _race,
            .RaceDisplayName = _raceDisplayName,
            .IsFemale = (_gender = 1),
            .RaceDefaults = _raceDefaults,
            .FlstCache = _flstCache,
            .NpcHasBodyTri = _npcHasBodyTri,
            .KnownOverlayTemplateIds = _knownOverlayTemplateIds,
            .KnownLmSkinTemplateIds = _knownLmSkinTemplateIds}
        Dim built As PresetCompatibilityReport.PresetAuditReport
        Try
            built = PresetCompatibilityReport.Build(ctx)
        Catch ex As Exception
            ' A malformed preset must never break the browser: degrade to an empty audit and log.
            Logger.LogLazy(Function() $"[LMLoad] compatibility audit failed for '{IO.Path.GetFileName(preset.SourcePath)}': {ex}")
            built = New PresetCompatibilityReport.PresetAuditReport()
        End Try
        _auditCache(preset) = built
        Return built
    End Function

    ''' <summary>"Show incompatible": the exhaustive breakdown, in the shared read-only monospace report modal
    ''' (<see cref="TextReport_Form"/>) — the content is a fixed-width table of findings, and a label/tooltip
    ''' can't hold it.</summary>
    Private Sub ButtonShowIncompatible_Click(sender As Object, e As EventArgs) Handles ButtonShowIncompatible.Click
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        If item Is Nothing Then Return
        Dim text = PresetCompatibilityReport.BuildText(GetAudit(item.Preset))

        ' ⛔ EL TÍTULO DECÍA "Incompatible / missing content" Y ESO DESHACÍA EL CARTEL: el usuario lee "Fully
        ' compatible", abre el reporte, y la ventana lo recibe con un título que dice lo contrario. La falsa alarma no
        ' se eliminaba, se movía un clic más adentro.
        ' El modal en sí ya no se arma acá: era el MISMO formulario que el preview de "Regenerate morphs" de
        ' EditFace, escrito dos veces — y una de las dos copias no tenía el fix del MaxLength, así que ahí el
        ' reporte largo se truncaba a 32767 caracteres en silencio. Ahora los dos usan TextReport_Form.
        Using f As New TextReport_Form(
            $"What this preset does — {IO.Path.GetFileNameWithoutExtension(item.Preset.SourcePath)}",
            text, showCopy:=True)
            f.ShowDialog(Me)
        End Using
    End Sub

    Private Sub ButtonOk_Click(sender As Object, e As EventArgs) Handles ButtonOk.Click
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        If item Is Nothing Then Return
        SelectedPreset = item.Preset
        DialogResult = DialogResult.OK
        Close()
    End Sub

    Private Sub ListBoxPresets_DoubleClick(sender As Object, e As EventArgs) Handles ListBoxPresets.DoubleClick
        If ButtonOk.Enabled Then ButtonOk_Click(sender, e)
    End Sub

    Private Class PresetItem
        Public ReadOnly Preset As LooksmenuLoader.LooksmenuPreset
        Public ReadOnly Display As String
        Public Sub New(p As LooksmenuLoader.LooksmenuPreset, displayName As String)
            Preset = p
            Display = displayName
        End Sub
        Public Overrides Function ToString() As String
            Return Display
        End Function
    End Class
End Class
