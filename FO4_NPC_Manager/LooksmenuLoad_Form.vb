Imports System.IO
Imports FO4_Base_Library

''' <summary>Dialog for picking a LooksMenu chargen preset to apply to the currently selected NPC.
'''
''' Behaviour:
'''   - On Load: scans Data\F4SE\Plugins\F4EE\Presets\&lt;RaceEditorID&gt;\*.json, parses each (so we
'''     can show file metadata + filter unsupported-only entries later), and populates the list
'''     filtered by the NPC's gender. Presets that fail to parse are skipped silently.
'''   - User types in the filter box: live-filter list by filename substring (case-insensitive).
'''   - User selects an entry: <see cref="LabelInfo"/> shows what the preset contains
'''     (HeadParts/morphs/tints counts) plus a warning if it has F4SE-only fields we won't apply.
'''     The dialog also fires <see cref="PreviewRequested"/> so the caller can apply the overlay
'''     live to the preview. The caller is responsible for snapshotting any prior overlay before
'''     showing the dialog and restoring it on Cancel.
'''   - OK: <see cref="SelectedPreset"/> is the parsed object; the last-previewed overlay stays.
'''   - Cancel: caller restores its pre-dialog snapshot (the live preview is already on the wrong
'''     preset, so the caller must explicitly roll back).
''' </summary>
Public Class LooksmenuLoad_Form

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _dataPath As String
    Private ReadOnly _gender As Byte
    Private ReadOnly _allPresets As New List(Of LooksmenuLoader.LooksmenuPreset)

    ' SSE (RaceMenu) mode: instead of scanning F4EE\Presets\*.json, scan the RaceMenu preset dir for *.jslot
    ' and map each to a LooksmenuPreset via the host-supplied mapper (which loads the .jslot + maps it onto a
    ' clone of the pre-dialog overlay so prior NPC fields survive). Everything else — list, filter, race-compat,
    ' live preview, OK — is shared with the FO4 path. Game-aware, NOT a separate window (user: reuse FO4 UI).
    Private ReadOnly _isSse As Boolean
    Private ReadOnly _ssePresetsDir As String
    Private ReadOnly _sseMapper As Func(Of String, LooksmenuLoader.LooksmenuPreset)

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

    ''' <summary>True if the user wants the preset's BodySlide sliders applied to the NPC. Default
    ''' = whether the NPC's NIF root carries BODYTRI extra-data (so we'd actually be able to apply
    ''' them in-game). User can override by clicking the checkbox.</summary>
    Public ReadOnly Property ApplyBodySliders As Boolean
        Get
            Return CheckBoxApplyBodySliders.Checked
        End Get
    End Property

    ''' <summary>Fired on every list selection change OR checkbox toggle so the host form can apply
    ''' the preset live as a preview. The bool is the current ApplyBodySliders state — host should
    ''' strip BodyMorphSliders from the overlay when False. Preset is Nothing when nothing is
    ''' selected. The host should snapshot any prior overlay state before showing the dialog so it
    ''' can restore on Cancel.</summary>
    Public Event PreviewRequested As EventHandler(Of PreviewRequestArgs)

    Public Class PreviewRequestArgs
        Public ReadOnly Preset As LooksmenuLoader.LooksmenuPreset
        Public ReadOnly ApplyBodySliders As Boolean
        Public Sub New(p As LooksmenuLoader.LooksmenuPreset, applyBody As Boolean)
            Preset = p
            ApplyBodySliders = applyBody
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
        _raceFormID = raceFormID
        _race = race
        _raceDefaults = New HashSet(Of UInteger)
        If raceDefaultHeadPartFormIDs IsNot Nothing Then
            For Each fid In raceDefaultHeadPartFormIDs
                _raceDefaults.Add(fid)
            Next
        End If

        ' Informational header. Presets live in a single flat folder and are race-agnostic at the
        ' file-system level, but the engine silently drops HDPTs / tints whose RACE doesn't accept
        ' them. The "Show only race-compatible" checkbox lets the user hide presets that would
        ' partially-apply for this NPC.
        Dim presetsFolderText As String = If(_isSse, "Listing all RaceMenu presets from Data\SKSE\Plugins\CharGen\Presets\.",
                                                       "Listing all presets from Data\F4SE\Plugins\F4EE\Presets\.")
        LabelHeader.Text = $"Target NPC race: {raceDisplayName}  •  Gender: {If(gender = 1, "Female", "Male")}" & vbCrLf & presetsFolderText

        ' Default the checkbox to whether the NPC's NIF can actually consume BodySlide sliders.
        ' If there's no BODYTRI on the root NiNode the engine wouldn't apply them in-game either,
        ' so we default to unchecked — but leave the choice to the user.
        CheckBoxApplyBodySliders.Checked = npcHasBodyTri

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
                For Each fp In Directory.GetFiles(_ssePresetsDir, "*.jslot")
                    Dim mapped = _sseMapper(fp)
                    If mapped Is Nothing Then Continue For
                    _allPresets.Add(mapped)
                Next
            End If
        Else
            Dim files = LooksmenuLoader.EnumeratePresetFiles(_dataPath)
            For Each fp In files
                Dim parsed = LooksmenuLoader.ParseFile(fp, _pluginManager)
                If parsed Is Nothing Then Continue For
                ' Skip presets whose declared gender doesn't match the NPC. CharGenInterface.cpp:301
                ' rejects mismatched gender at LoadPreset time — same rule here so the user only sees
                ' presets that will actually apply cleanly.
                If parsed.Gender <> _gender Then Continue For
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
        Dim result = HeadPartResolver.IsPresetCompatibleWithRace(
            preset, _raceFormID, _gender = 1, _pluginManager, _race, _flstCache, _raceDefaults)
        _compatibilityCache(preset) = result
        Return result
    End Function

    Private Sub ListBoxPresets_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBoxPresets.SelectedIndexChanged
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        ButtonOk.Enabled = item IsNot Nothing
        UpdateInfo(item?.Preset)
        RaisePreview(item?.Preset)
    End Sub

    ''' <summary>Toggling the checkbox needs to re-fire the preview so the host can rebuild the
    ''' overlay (with or without BodyMorphSliders) and re-render — without this the checkbox
    ''' wouldn't have visible effect until OK.</summary>
    Private Sub CheckBoxApplyBodySliders_CheckedChanged(sender As Object, e As EventArgs) Handles CheckBoxApplyBodySliders.CheckedChanged
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        RaisePreview(item?.Preset)
    End Sub

    Private Sub RaisePreview(preset As LooksmenuLoader.LooksmenuPreset)
        RaiseEvent PreviewRequested(Me, New PreviewRequestArgs(preset, CheckBoxApplyBodySliders.Checked))
    End Sub

    Private Sub UpdateInfo(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then
            Dim emptyFolder = If(_isSse, "Data\SKSE\Plugins\CharGen\Presets\", "Data\F4SE\Plugins\F4EE\Presets\")
            LabelInfo.Text = If(_allPresets.Count = 0,
                                If(_isSse, $"No RaceMenu (.jslot) presets found in {emptyFolder}.",
                                           $"No {If(_gender = 1, "female", "male")} presets found in {emptyFolder}."),
                                "Select a preset to see details.")
            LabelInfo.ForeColor = SystemColors.GrayText
            Return
        End If

        If _isSse Then
            ' RaceMenu summary: everything the SSE overlay carries (all supported — full round-trip via .jslot +
            ' sidecar). Body weight, head/face morphs, body morphs (BodySlide), overlays, node scales, skin overrides.
            Dim nodeScales = If(preset.SseNodeTransforms IsNot Nothing, preset.SseNodeTransforms.Count, 0)
            Dim skinOv = If(preset.SseSkinOverrides IsNot Nothing, preset.SseSkinOverrides.Count, 0)
            Dim bodyOv = If(preset.SseBodyOverlays IsNot Nothing, preset.SseBodyOverlays.Count, 0)
            Dim weightTxt = If(preset.SseWeight.HasValue, $"{preset.SseWeight.Value:0}", "—")
            LabelInfo.Text = $"HeadParts: {preset.HeadPartFormIDs.Count}  •  Tints: {preset.FaceTintLayers.Count}  •  " &
                             $"Face morphs: {preset.ChargenFaceMorphs.Count}  •  Weight: {weightTxt}  •  " &
                             $"BodySlide: {preset.BodyMorphSliders.Count}  •  Overlays: {bodyOv}  •  " &
                             $"Body scale: {nodeScales}  •  Skin overrides: {skinOv}"
            LabelInfo.ForeColor = SystemColors.ControlText
            Return
        End If

        ' Overlays are now supported (parse + render + round-trip), so they go in the summary line
        ' next to BodySlide — NOT in the "will be skipped" warning. Only skin override remains F4SE
        ' unsupported on load.
        Dim hasUnsupported = preset.UnsupportedCounts.HasSkinOverride

        Dim summary = $"HeadParts: {preset.HeadPartFormIDs.Count}  •  Tints: {preset.FaceTintLayers.Count}  •  " &
                      $"Face morphs: {preset.ChargenFaceMorphs.Count}  •  Body regions: {preset.BodyMorphValues.Count}  •  " &
                      $"Face bone regions: {preset.FaceBoneRegions.Count}  •  BodySlide: {preset.BodyMorphSliders.Count}  •  " &
                      $"Overlays: {preset.Overlays.Count}"

        If hasUnsupported Then
            Dim warnings As New List(Of String)
            If preset.UnsupportedCounts.HasSkinOverride Then warnings.Add("skin override")
            LabelInfo.Text = summary & vbCrLf & "Note: F4SE-only fields will be skipped (" & String.Join(", ", warnings) & ")."
            LabelInfo.ForeColor = Drawing.Color.DarkGoldenrod
        Else
            LabelInfo.Text = summary
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
