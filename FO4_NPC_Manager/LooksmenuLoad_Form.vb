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
'''     categories it carries nothing for; <see cref="LabelInfo"/> shows provenance + warnings.
'''     Under SSE the host mapper composes the .jslot onto a clone of the pre-dialog overlay, so there the
'''     amounts describe what the row WOULD APPLY (the .jslot's value where it has one, the NPC's current
'''     value otherwise) rather than the raw file contents.
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
    Private ReadOnly _sseMapper As Func(Of String, LooksmenuLoader.LooksmenuPreset)

    ''' <summary>True when no shape of the NPC's body NIF carries BODYTRI extra-data: BodySlide sliders can
    ''' still be loaded, they just won't show in-game. Surfaced as a note instead of forcing the category off.</summary>
    Private ReadOnly _npcHasBodyTri As Boolean

    ' Race-compatibility filter inputs (optional — Nothing means race info wasn't supplied
    ' and the checkbox stays disabled because we can't compute compatibility).
    Private ReadOnly _raceFormID As UInteger
    Private ReadOnly _race As RACE_Data
    Private ReadOnly _raceDefaults As HashSet(Of UInteger)
    ' FLST cache reused across IsHdptValidForRace calls so each FLST is parsed once per session.
    Private ReadOnly _flstCache As New Dictionary(Of UInteger, FLST_Data)
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
                   Optional sseMapper As Func(Of String, LooksmenuLoader.LooksmenuPreset) = Nothing)
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
        _raceDefaults = New HashSet(Of UInteger)
        If raceDefaultHeadPartFormIDs IsNot Nothing Then
            For Each fid In raceDefaultHeadPartFormIDs
                _raceDefaults.Add(fid)
            Next
        End If

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
        LoadPresetList()
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
                    Dim mapped = _sseMapper(fp)
                    If mapped Is Nothing Then
                        Dim fpLocal = fp
                        Logger.LogLazy(Function() $"[LMLoad] DROP '{Path.GetFileName(fpLocal)}': RaceMenu mapper returned Nothing (jslot failed to load/map).")
                        Continue For
                    End If
                    _allPresets.Add(mapped)
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
        Dim result = HeadPartResolver.IsPresetCompatibleWithRace(
            preset, _raceFormID, _gender = 1, _pluginManager, _race, _flstCache, _raceDefaults)
        _compatibilityCache(preset) = result
        Return result
    End Function

    Private Sub ListBoxPresets_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBoxPresets.SelectedIndexChanged
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        ButtonOk.Enabled = item IsNot Nothing
        ' Refresh the per-category amounts BEFORE previewing: the panel decides which categories are
        ' selectable for this preset, and Options (read by RaisePreview) depends on that.
        CategoryPanel.SetPreset(item?.Preset)
        UpdateInfo(item?.Preset)
        RaisePreview(item?.Preset)
    End Sub

    Private Sub RaisePreview(preset As LooksmenuLoader.LooksmenuPreset)
        RaiseEvent PreviewRequested(Me, New PreviewRequestArgs(preset, CategoryPanel.Options))
    End Sub

    ''' <summary>Provenance + warnings for the selected preset. The per-category amounts live in the panel
    ''' on the right, so this line carries what the panel can't: where the file came from, head parts whose
    ''' owning plugin isn't loaded, and fields we knowingly don't apply.</summary>
    Private Sub UpdateInfo(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then
            Dim emptyFolder = If(_isSse, "Data\SKSE\Plugins\CharGen\Presets\", "Data\F4SE\Plugins\F4EE\Presets\")
            LabelInfo.Text = If(_allPresets.Count = 0,
                                If(_isSse, $"No RaceMenu (.jslot) presets found in {emptyFolder}.",
                                           $"No {If(_gender = 1, "female", "male")} presets found in {emptyFolder}."),
                                "Select a preset to see what it carries.")
            LabelInfo.ForeColor = SystemColors.GrayText
            Return
        End If

        Dim warnings As New List(Of String)
        If preset.UnresolvedHeadParts.Count > 0 Then
            warnings.Add($"{preset.UnresolvedHeadParts.Count} head part(s) reference a plugin that isn't loaded")
        End If
        ' Overlays are supported (parse + render + round-trip). Only the F4SE skin override remains
        ' unsupported on load, and only under FO4.
        If Not _isSse AndAlso preset.UnsupportedCounts.HasSkinOverride Then
            warnings.Add("F4SE skin override will be skipped")
        End If
        If Not _npcHasBodyTri AndAlso preset.BodyMorphSliders.Count > 0 Then
            warnings.Add("this NPC's body has no BODYTRI, so BodySlide sliders won't show in-game")
        End If

        Dim src = preset.SourcePath
        Try
            If Not String.IsNullOrEmpty(_dataPath) AndAlso src.StartsWith(_dataPath, StringComparison.OrdinalIgnoreCase) Then
                src = src.Substring(_dataPath.Length).TrimStart(Path.DirectorySeparatorChar)
            End If
        Catch
        End Try

        If warnings.Count > 0 Then
            LabelInfo.Text = src & vbCrLf & "Note: " & String.Join("; ", warnings) & "."
            LabelInfo.ForeColor = Drawing.Color.DarkGoldenrod
        Else
            LabelInfo.Text = src
            LabelInfo.ForeColor = SystemColors.ControlText
        End If
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
