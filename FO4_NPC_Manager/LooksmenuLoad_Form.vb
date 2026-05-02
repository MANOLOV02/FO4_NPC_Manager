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
'''   - OK: <see cref="SelectedPreset"/> is the parsed object; the caller applies it.
'''   - Cancel: caller restores its pre-dialog snapshot.
''' </summary>
Public Class LooksmenuLoad_Form

    Private ReadOnly _pluginManager As PluginManager
    Private ReadOnly _dataPath As String
    Private ReadOnly _gender As Byte
    Private ReadOnly _allPresets As New List(Of LooksmenuLoader.LooksmenuPreset)

    ''' <summary>The preset the user picked. Nothing if the dialog was cancelled.</summary>
    Public Property SelectedPreset As LooksmenuLoader.LooksmenuPreset

    Public Sub New(pluginManager As PluginManager,
                   dataPath As String,
                   gender As Byte,
                   raceDisplayName As String)
        InitializeComponent()
        _pluginManager = pluginManager
        _dataPath = dataPath
        _gender = gender

        ' Informational header. Presets live in a single flat folder and are race-agnostic — the
        ' display name is shown so the user knows which NPC will receive the preset; the gender
        ' is the only field LooksMenu actually filters on (CharGenInterface.cpp:301).
        LabelHeader.Text = $"Target NPC race: {raceDisplayName}  •  Gender: {If(gender = 1, "Female", "Male")}" & vbCrLf &
                           "Listing all presets from Data\F4SE\Plugins\F4EE\Presets\ (filtered by gender)."
    End Sub

    Private Sub LooksmenuLoad_Form_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        LoadPresetList()
    End Sub

    Private Sub LoadPresetList()
        _allPresets.Clear()
        ListBoxPresets.Items.Clear()

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

        _allPresets.Sort(Function(a, b) String.Compare(Path.GetFileName(a.SourcePath), Path.GetFileName(b.SourcePath), StringComparison.OrdinalIgnoreCase))
        ApplyFilter()
    End Sub

    Private Sub TextBoxFilter_TextChanged(sender As Object, e As EventArgs) Handles TextBoxFilter.TextChanged
        ApplyFilter()
    End Sub

    Private Sub ApplyFilter()
        ListBoxPresets.BeginUpdate()
        Try
            ListBoxPresets.Items.Clear()
            Dim needle = TextBoxFilter.Text.Trim()
            For Each preset In _allPresets
                Dim displayName = Path.GetFileNameWithoutExtension(preset.SourcePath)
                If needle.Length = 0 OrElse displayName.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0 Then
                    ListBoxPresets.Items.Add(New PresetItem(preset, displayName))
                End If
            Next
        Finally
            ListBoxPresets.EndUpdate()
        End Try

        If ListBoxPresets.Items.Count > 0 Then
            ListBoxPresets.SelectedIndex = 0
        Else
            UpdateInfo(Nothing)
            ButtonOk.Enabled = False
        End If
    End Sub

    Private Sub ListBoxPresets_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBoxPresets.SelectedIndexChanged
        Dim item = TryCast(ListBoxPresets.SelectedItem, PresetItem)
        ButtonOk.Enabled = item IsNot Nothing
        UpdateInfo(item?.Preset)
    End Sub

    Private Sub UpdateInfo(preset As LooksmenuLoader.LooksmenuPreset)
        If preset Is Nothing Then
            LabelInfo.Text = If(_allPresets.Count = 0,
                                $"No {If(_gender = 1, "female", "male")} presets found in Data\F4SE\Plugins\F4EE\Presets\.",
                                "Select a preset to see details.")
            LabelInfo.ForeColor = SystemColors.GrayText
            Return
        End If

        Dim hasUnsupported =
            preset.UnsupportedCounts.Overlays > 0 OrElse
            preset.UnsupportedCounts.BodyMorphSliders > 0 OrElse
            preset.UnsupportedCounts.HasSkinOverride

        Dim summary = $"HeadParts: {preset.HeadPartFormIDs.Count}  •  Tints: {preset.FaceTintLayers.Count}  •  " &
                      $"Face morphs: {preset.ChargenFaceMorphs.Count}  •  Body regions: {preset.BodyMorphValues.Count}  •  " &
                      $"Face bone regions: {preset.FaceBoneRegions.Count}"

        If hasUnsupported Then
            Dim warnings As New List(Of String)
            If preset.UnsupportedCounts.Overlays > 0 Then warnings.Add($"{preset.UnsupportedCounts.Overlays} overlays")
            If preset.UnsupportedCounts.BodyMorphSliders > 0 Then warnings.Add($"{preset.UnsupportedCounts.BodyMorphSliders} body morph sliders")
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
