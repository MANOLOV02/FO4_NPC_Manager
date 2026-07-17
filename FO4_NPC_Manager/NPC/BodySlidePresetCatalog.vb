Imports System.IO
Imports System.Xml.Linq

' ==========================================================================
' BodySlide slider-preset catalog (<BodySlide dir>\SliderPresets\*.xml).
'
' Faithful port of Wardrobe_Manager's preset handling — kept local because
' NPC_Manager doesn't reference the WM project:
'   • XML parsing        = SliderPresetCollection.LoadFromXml (WM OSP_Clases.vb:230)
'   • value resolution   = SliderSet_Class.SetPreset          (WM OSP_Clases.vb:2486)
'
' Values are BodySlide percent (0-100, extremes allowed), matching both the preset
' XML convention and the EditBody BodySlide tab's TinySliderTextBox scale (the
' LooksMenu overlay model stores value/100).
' ==========================================================================
Public Class BodySlidePresetCatalog

    ''' <summary>Size variant of a preset slider entry. Mirrors WM_Config.SliderSize
    ''' (Wardrobe_Manager WM_Config.vb:18) — same order so a persisted combo index maps 1:1.</summary>
    Public Enum PresetSliderSize
        [Default] = 0
        Big = 1
        Small = 2
    End Enum

    Public Class PresetSlider
        Public Property Name As String = ""
        Public Property Size As PresetSliderSize = PresetSliderSize.Default
        Public Property Value As Single = 0
    End Class

    Public Class PresetDef
        Public Property Name As String = ""
        Public Property SetName As String = ""
        Public Property Filename As String = ""
        Public Property Sliders As New List(Of PresetSlider)
    End Class

    ''' <summary>All loaded presets, display-name keyed. SortedDictionary with the default
    ''' comparer = the same alphabetical order + case-sensitive duplicate handling WM uses.</summary>
    Public ReadOnly Property Presets As New SortedDictionary(Of String, PresetDef)

    ''' <summary>Load every *.xml under the SliderPresets folder. Unreadable files are logged
    ''' and skipped (WM MsgBoxes instead, but a modal per bad XML inside the Edit Body dialog
    ''' would be hostile — the empty combo plus the log tells the same story).</summary>
    Public Sub LoadFolder(sliderPresetsDir As String)
        Presets.Clear()
        If Not Directory.Exists(sliderPresetsDir) Then Return
        For Each xmlPath In FilesDictionary_class.EnumerateFilesWithSymlinkSupport(sliderPresetsDir, "*.xml", False)
            LoadFromXml(xmlPath)
        Next
    End Sub

    ''' <summary>Parse one preset XML — port of WM's SliderPresetCollection.LoadFromXml
    ''' (OSP_Clases.vb:230): &lt;Preset name= set=&gt; / &lt;SetSlider name= value= size=&gt;,
    ''' size "small"/"big" → enum (anything else = Default), duplicate preset names get a
    ''' "_1"/"_2"… suffix.</summary>
    Private Sub LoadFromXml(path As String)
        Try
            Dim doc = XDocument.Load(path)
            For Each xp In doc.Root.Elements("Preset")
                Dim nameAttr = xp.Attribute("name")?.Value
                Dim setAttr = xp.Attribute("set")?.Value
                If String.IsNullOrEmpty(nameAttr) Then
                    Throw New InvalidDataException($"<Preset> missing required 'name' in '{path}'")
                End If

                Dim p As New PresetDef With {
                    .Name = nameAttr,
                    .SetName = If(setAttr, ""),
                    .Filename = path
                }

                For Each ss In xp.Elements("SetSlider")
                    Dim sliderName = ss.Attribute("name")?.Value
                    Dim valText = ss.Attribute("value")?.Value
                    If String.IsNullOrEmpty(sliderName) Then
                        Throw New InvalidDataException($"<SetSlider> missing 'name' in preset '{nameAttr}' of '{path}'")
                    End If
                    Dim valueFloat As Single
                    If Not Single.TryParse(valText, Globalization.NumberStyles.Float, Globalization.CultureInfo.InvariantCulture, valueFloat) Then
                        Throw New InvalidDataException($"<SetSlider name=""{sliderName}""> has invalid or missing 'value' in preset '{nameAttr}' of '{path}'")
                    End If

                    Dim sizeRaw = ss.Attribute("size")?.Value?.ToLowerInvariant()
                    Dim sz As PresetSliderSize = If(sizeRaw = "small", PresetSliderSize.Small,
                                            If(sizeRaw = "big", PresetSliderSize.Big,
                                                                PresetSliderSize.Default))

                    p.Sliders.Add(New PresetSlider With {
                        .Name = sliderName,
                        .Size = sz,
                        .Value = valueFloat
                    })
                Next

                Dim nombre As String = p.Name
                Dim subs As Integer = 1
                While Presets.ContainsKey(nombre)
                    nombre = p.Name + "_" + subs.ToString()
                    subs += 1
                End While
                Presets.Add(nombre, p)
            Next
        Catch ex As Exception
            Logger.LogLazy(Function() $"[BS-PRESET] Error reading preset file '{path}': {ex.GetType().Name}: {ex.Message}")
        End Try
    End Sub

    ''' <summary>Resolve a preset's effective slider values (percent, name-keyed case-insensitive)
    ''' for a size, replicating the game-aware match rules of WM's SliderSet_Class.SetPreset
    ''' (OSP_Clases.vb:2486):
    '''   • FO4 ignores the size — a slider takes the preset's Default entry, falling back to Big.
    '''   • SSE applies Small entries only when size = Small; Default/Big entries otherwise
    '''     (so size Default ≡ Big — SSE presets don't carry Default entries).
    ''' Matches iterate in Size order so the last applicable entry wins, exactly as in WM.
    ''' Sliders the preset doesn't name simply aren't in the result — the caller zeroes them
    ''' (the EditBody tab's rows have no slider-set XML default; the baseline is 0).</summary>
    Public Shared Function ResolveValues(preset As PresetDef,
                                         size As PresetSliderSize,
                                         game As Config_App.Game_Enum) As Dictionary(Of String, Single)
        Dim result As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        If preset Is Nothing Then Return result
        For Each grp In preset.Sliders.GroupBy(Function(s) s.Name, StringComparer.OrdinalIgnoreCase)
            Dim matches = grp.OrderBy(Function(pf) pf.Size).ToList()
            If game = Config_App.Game_Enum.Fallout4 Then
                Dim presetDefault = matches.FirstOrDefault(Function(pf) pf.Size = PresetSliderSize.Default)
                Dim presetBig = matches.FirstOrDefault(Function(pf) pf.Size = PresetSliderSize.Big)
                If presetDefault IsNot Nothing Then
                    result(grp.Key) = presetDefault.Value
                ElseIf presetBig IsNot Nothing Then
                    result(grp.Key) = presetBig.Value
                End If
            Else
                For Each sli In matches
                    If sli.Size = PresetSliderSize.Small Then
                        If size = PresetSliderSize.Small Then result(grp.Key) = sli.Value
                    Else
                        If size <> PresetSliderSize.Small Then result(grp.Key) = sli.Value
                    End If
                Next
            End If
        Next
        Return result
    End Function

    ''' <summary>Resolve a BodySlide-suite executable inside a folder — port of
    ''' WM_Config.ResolveBsSuiteExePath (WM_Config.vb:199): prefer the legacy "name x64.exe"
    ''' on a 64-bit OS, fall back to the unified suffix-less "name.exe".</summary>
    Public Shared Function ResolveBsSuiteExePath(bsDir As String, baseName As String) As String
        Dim plain = IO.Path.Combine(bsDir, baseName & ".exe")
        If Environment.Is64BitOperatingSystem Then
            Dim suffixed = IO.Path.Combine(bsDir, baseName & " x64.exe")
            If File.Exists(suffixed) Then Return suffixed
        End If
        Return plain
    End Function

End Class
