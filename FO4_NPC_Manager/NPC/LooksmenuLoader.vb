Imports System.IO
Imports System.Text
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>Parser of the LooksMenu CharGen preset JSON format. Schema verified against
''' F4SEPlugins-master/f4ee/CharGenInterface.cpp:60-256 (SavePreset) and 259-620 (LoadPreset).
'''
''' Maps every JSON field that has a vanilla NPC_ subrecord equivalent. Three F4SE-only fields
''' (Overlays / BodyMorphs / Skin) are surfaced as raw counts via <see cref="LooksmenuPreset.UnsupportedCounts"/>
''' so the caller can warn — no attempt is made to apply them. See
''' memory/project_npc_looksmenu_pending.md for the deferral rationale.</summary>
Public Module LooksmenuLoader

    ''' <summary>Output of <see cref="ParseFile"/>. All vanilla-mappable fields are pre-resolved to
    ''' global FormIDs (via <see cref="PluginManager.ResolveReferencedFormID"/>) so the caller can
    ''' just assign them onto an NPC_Data without any string-parsing logic.</summary>
    Public Class LooksmenuPreset
        Public SourcePath As String = ""
        Public Gender As Byte = 0   ' 0=Male, 1=Female
        Public HeadPartFormIDs As New List(Of UInteger)
        ''' <summary>HeadPart entries from the JSON that ResolveFormIdentifier couldn't resolve to a
        ''' loaded plugin (returned 0). Kept as raw "Plugin.esp|HEX" strings so the caller can log
        ''' which plugins the preset depends on but the user doesn't have active. Almost always
        ''' the cause when a preset's pelo/ojos visually don't apply: the HDPT lives in a
        ''' presets-mod ESP that isn't in Plugins.txt.</summary>
        Public UnresolvedHeadParts As New List(Of String)
        Public HairColorFormID As UInteger
        Public WeightThin As Single?
        Public WeightMuscular As Single?
        Public WeightFat As Single?
        ''' <summary>Chargen face vertex morphs — Morphs.Presets in JSON, MSDK/MSDV in NPC_.
        ''' Key = MSDK hash (the JSON serializes it as hex string, parsed to UInt32 here).</summary>
        Public ChargenFaceMorphs As New Dictionary(Of UInteger, Single)
        ''' <summary>Body region morph values — Morphs.Values[] in JSON (positional array),
        ''' MRSV in NPC_. Index = position in RACE.MorphValues definitions.</summary>
        Public BodyMorphValues As New List(Of Single)
        ''' <summary>Face bone morph regions — Morphs.Regions in JSON, FMRI/FMRS in NPC_.
        ''' Key = FMRI region index, Value = 8 floats (the FMRS values for that region).</summary>
        Public FaceBoneRegions As New Dictionary(Of UInteger, Single())
        ''' <summary>Always present after parse. CharGenInterface.cpp asymmetrically skips the field
        ''' on Save when intensity == 1.0 (line 161 `if(intensity != 1.0f)`), but on Load (lines 456-458)
        ''' it interprets "absent" as "use 1.0" and ALWAYS calls SetFacialBoneMorphIntensity. So
        ''' missing-from-JSON is semantically equivalent to "1.0 explicit", not "preserve previous".
        ''' We replicate that: default 1.0F at parse time, override only when the JSON has the field.</summary>
        Public FacialMorphIntensity As Single = 1.0F
        ''' <summary>Tint layers reordered by TintOrder[] if the JSON provided one. Each entry
        ''' is a parsed NPC_FaceTintLayerData with Discriminator/Index/Value/Color/TemplateColorIndex
        ''' filled. RawTetiBytes/RawTendBytes are NOT populated (the JSON doesn't carry them) —
        ''' callers that need byte-perfect round-trip must re-emit them from the parsed fields.</summary>
        Public FaceTintLayers As New List(Of NPC_FaceTintLayerData)

        ''' <summary>Counts of F4SE-only fields the preset contains. Non-zero = the preset has
        ''' content the editor will not apply (Overlays/BodyMorphs sliders/Skin override).</summary>
        Public UnsupportedCounts As New UnsupportedFieldCounts
    End Class

    Public Class UnsupportedFieldCounts
        Public Overlays As Integer
        Public BodyMorphSliders As Integer
        Public HasSkinOverride As Boolean
    End Class

    ''' <summary>Parse a LooksMenu preset JSON file. Returns Nothing if the file is unreadable
    ''' or not valid JSON. Form-identifier strings ("Plugin.esp|XXXXXX") are resolved against
    ''' <paramref name="pluginManager"/> at parse time — entries from plugins not in the active
    ''' load order resolve to 0 and the caller will see HeadParts entries missing.</summary>
    Public Function ParseFile(filePath As String, pluginManager As PluginManager) As LooksmenuPreset
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then Return Nothing

        Dim raw As String
        Try
            raw = File.ReadAllText(filePath)
        Catch
            Return Nothing
        End Try

        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(raw)
        Catch
            Return Nothing
        End Try

        Using doc
            Dim root = doc.RootElement
            If root.ValueKind <> JsonValueKind.Object Then Return Nothing

            Dim preset As New LooksmenuPreset With {.SourcePath = filePath}

            ' Gender
            Dim genderEl As JsonElement
            If root.TryGetProperty("Gender", genderEl) AndAlso genderEl.ValueKind = JsonValueKind.Number Then
                preset.Gender = CByte(Math.Min(255, Math.Max(0, genderEl.GetInt32())))
            End If

            ' HeadParts: array of "Plugin.esp|FormIDhex" strings
            Dim hpEl As JsonElement
            If root.TryGetProperty("HeadParts", hpEl) AndAlso hpEl.ValueKind = JsonValueKind.Array Then
                For Each entry In hpEl.EnumerateArray()
                    If entry.ValueKind = JsonValueKind.String Then
                        Dim hpStr = entry.GetString()
                        Dim resolved = ResolveFormIdentifier(hpStr, pluginManager)
                        If resolved <> 0UI Then
                            preset.HeadPartFormIDs.Add(resolved)
                        Else
                            preset.UnresolvedHeadParts.Add(hpStr)
                        End If
                    End If
                Next
            End If

            ' HairColor
            Dim hcEl As JsonElement
            If root.TryGetProperty("HairColor", hcEl) AndAlso hcEl.ValueKind = JsonValueKind.String Then
                preset.HairColorFormID = ResolveFormIdentifier(hcEl.GetString(), pluginManager)
            End If

            ' Weight: 3 floats [thin, muscular, large]
            Dim wEl As JsonElement
            If root.TryGetProperty("Weight", wEl) AndAlso wEl.ValueKind = JsonValueKind.Array Then
                Dim arr = wEl.EnumerateArray().ToArray()
                If arr.Length >= 1 AndAlso arr(0).ValueKind = JsonValueKind.Number Then preset.WeightThin = arr(0).GetSingle()
                If arr.Length >= 2 AndAlso arr(1).ValueKind = JsonValueKind.Number Then preset.WeightMuscular = arr(1).GetSingle()
                If arr.Length >= 3 AndAlso arr(2).ValueKind = JsonValueKind.Number Then preset.WeightFat = arr(2).GetSingle()
            End If

            ' Morphs.{Values, Presets, Regions, Intensity}
            Dim morphsEl As JsonElement
            If root.TryGetProperty("Morphs", morphsEl) AndAlso morphsEl.ValueKind = JsonValueKind.Object Then
                Dim valuesEl As JsonElement
                If morphsEl.TryGetProperty("Values", valuesEl) AndAlso valuesEl.ValueKind = JsonValueKind.Array Then
                    For Each v In valuesEl.EnumerateArray()
                        If v.ValueKind = JsonValueKind.Number Then preset.BodyMorphValues.Add(v.GetSingle())
                    Next
                End If

                Dim presetsEl As JsonElement
                If morphsEl.TryGetProperty("Presets", presetsEl) AndAlso presetsEl.ValueKind = JsonValueKind.Object Then
                    For Each prop In presetsEl.EnumerateObject()
                        Dim hash As UInteger
                        If UInteger.TryParse(prop.Name, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, hash) AndAlso prop.Value.ValueKind = JsonValueKind.Number Then
                            preset.ChargenFaceMorphs(hash) = prop.Value.GetSingle()
                        End If
                    Next
                End If

                Dim regionsEl As JsonElement
                If morphsEl.TryGetProperty("Regions", regionsEl) AndAlso regionsEl.ValueKind = JsonValueKind.Object Then
                    For Each prop In regionsEl.EnumerateObject()
                        Dim idx As UInteger
                        If UInteger.TryParse(prop.Name, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, idx) AndAlso prop.Value.ValueKind = JsonValueKind.Array Then
                            Dim vals As New List(Of Single)
                            For Each v In prop.Value.EnumerateArray()
                                If v.ValueKind = JsonValueKind.Number Then vals.Add(v.GetSingle())
                            Next
                            preset.FaceBoneRegions(idx) = vals.ToArray()
                        End If
                    Next
                End If

                Dim intEl As JsonElement
                If morphsEl.TryGetProperty("Intensity", intEl) AndAlso intEl.ValueKind = JsonValueKind.Number Then
                    preset.FacialMorphIntensity = intEl.GetSingle()
                End If
            End If

            ' Tints + TintOrder. CharGenInterface.cpp:165-201 saves Tints as a dict keyed by tint
            ' index (hex), each entry having Type/Percent/Color/ColorID. TintOrder is a parallel
            ' array dictating render order. We build the layers in TintOrder order if provided;
            ' fallback = enumeration order of the Tints object.
            Dim tintsEl As JsonElement
            Dim tintOrderEl As JsonElement
            Dim hasTints = root.TryGetProperty("Tints", tintsEl) AndAlso tintsEl.ValueKind = JsonValueKind.Object
            Dim hasOrder = root.TryGetProperty("TintOrder", tintOrderEl) AndAlso tintOrderEl.ValueKind = JsonValueKind.Array

            If hasTints Then
                Dim orderedKeys As New List(Of String)
                If hasOrder Then
                    For Each k In tintOrderEl.EnumerateArray()
                        If k.ValueKind = JsonValueKind.String Then orderedKeys.Add(k.GetString())
                    Next
                Else
                    For Each prop In tintsEl.EnumerateObject()
                        orderedKeys.Add(prop.Name)
                    Next
                End If

                For Each keyName In orderedKeys
                    Dim entryEl As JsonElement
                    If Not tintsEl.TryGetProperty(keyName, entryEl) Then Continue For
                    If entryEl.ValueKind <> JsonValueKind.Object Then Continue For

                    Dim layer As New NPC_FaceTintLayerData()
                    Dim idxParsed As UInteger
                    If UInteger.TryParse(keyName, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, idxParsed) Then
                        layer.Index = CUShort(idxParsed And &HFFFFUI)
                    End If

                    Dim typeEl As JsonElement
                    If entryEl.TryGetProperty("Type", typeEl) AndAlso typeEl.ValueKind = JsonValueKind.Number Then
                        layer.Discriminator = CUShort(typeEl.GetInt32())
                    End If

                    Dim pctEl As JsonElement
                    If entryEl.TryGetProperty("Percent", pctEl) AndAlso pctEl.ValueKind = JsonValueKind.Number Then
                        layer.Value = CByte(Math.Min(255, Math.Max(0, pctEl.GetInt32())))
                    End If

                    ' Palette-only: Type=1 (BGSCharacterTint::Entry::kTypePalette in f4se).
                    ' Color is stored as bgra UInt32. CharGenInterface.cpp:193 writes
                    '   tintData[k]["Color"] = (Json::Int)palette->color.bgra
                    ' which is signed-int-with-bit-pattern. We read via GetInt32 + bit cast.
                    If layer.Discriminator = 1US Then
                        Dim colorEl As JsonElement
                        If entryEl.TryGetProperty("Color", colorEl) AndAlso colorEl.ValueKind = JsonValueKind.Number Then
                            Dim bgra = CUInt(colorEl.GetInt64() And &HFFFFFFFFL)
                            ' bgra layout: bytes [B G R A] little-endian → A R G B for Color.FromArgb.
                            Dim b = CInt((bgra >> 0) And &HFFUI)
                            Dim g = CInt((bgra >> 8) And &HFFUI)
                            Dim r = CInt((bgra >> 16) And &HFFUI)
                            Dim a = CInt((bgra >> 24) And &HFFUI)
                            layer.Color = Drawing.Color.FromArgb(a, r, g, b)
                        End If
                        Dim cidEl As JsonElement
                        If entryEl.TryGetProperty("ColorID", cidEl) AndAlso cidEl.ValueKind = JsonValueKind.Number Then
                            layer.TemplateColorIndex = cidEl.GetInt32()
                        End If
                    End If

                    preset.FaceTintLayers.Add(layer)
                Next
            End If

            ' F4SE-only fields: count and skip. See project_npc_looksmenu_pending.md.
            Dim ovEl As JsonElement
            If root.TryGetProperty("Overlays", ovEl) AndAlso ovEl.ValueKind = JsonValueKind.Array Then
                preset.UnsupportedCounts.Overlays = ovEl.GetArrayLength()
            End If
            Dim bmEl As JsonElement
            If root.TryGetProperty("BodyMorphs", bmEl) AndAlso bmEl.ValueKind = JsonValueKind.Object Then
                Dim n = 0
                For Each prop In bmEl.EnumerateObject() : n += 1 : Next
                preset.UnsupportedCounts.BodyMorphSliders = n
            End If
            Dim skEl As JsonElement
            If root.TryGetProperty("Skin", skEl) AndAlso skEl.ValueKind = JsonValueKind.String Then
                preset.UnsupportedCounts.HasSkinOverride = Not String.IsNullOrEmpty(skEl.GetString())
            End If

            Return preset
        End Using
    End Function

    ''' <summary>Resolve a "Plugin.esp|FormIDhex" identifier (LooksMenu's serialization format —
    ''' Utilities.cpp:108-130 GetFormIdentifier emits "%s|%06X" with the LOCAL FormID, no master
    ''' index in the high bits) to a global FormID. Returns 0 when the named plugin isn't in the
    ''' active load order (caller falls back to "skip this entry") or when the string is malformed.
    '''
    ''' We can't delegate to <see cref="PluginManager.ResolveReferencedFormID"/> directly: that
    ''' helper returns the input localFormID unchanged when the plugin isn't loaded, which would
    ''' look like a successful resolution (and then GetRecord fails downstream with "not found").
    ''' Doing the lookup ourselves lets us cleanly distinguish "plugin not loaded" from "resolved
    ''' to a global ID that happens to have low bytes".</summary>
    Private Function ResolveFormIdentifier(identifier As String, pluginManager As PluginManager) As UInteger
        If String.IsNullOrEmpty(identifier) Then Return 0UI
        Dim pipeIdx = identifier.IndexOf("|"c)
        If pipeIdx <= 0 OrElse pipeIdx >= identifier.Length - 1 Then Return 0UI

        Dim pluginName = identifier.Substring(0, pipeIdx).Trim()
        Dim hex = identifier.Substring(pipeIdx + 1).Trim()
        Dim localFormID As UInteger
        If Not UInteger.TryParse(hex, Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, localFormID) Then
            Return 0UI
        End If

        ' Find the named plugin in the active load order. If not loaded, signal "unresolved" with
        ' 0 — caller will route the raw identifier into UnresolvedHeadParts for diagnostics.
        Dim loadOrderIdx As Integer = -1
        For i = 0 To pluginManager.Plugins.Count - 1
            If String.Equals(pluginManager.Plugins(i).FileName, pluginName, StringComparison.OrdinalIgnoreCase) Then
                loadOrderIdx = i
                Exit For
            End If
        Next
        If loadOrderIdx < 0 Then Return 0UI

        ' LooksMenu always serializes the bare 24-bit local FormID (Utilities.cpp:112
        ' `modForm = formID & 0xFFFFFF`), so combine with the load-order index to get the global ID.
        Return (CUInt(loadOrderIdx) << 24) Or (localFormID And &HFFFFFFUI)
    End Function

    ''' <summary>Serialize a preset to a LooksMenu-canonical JSON string. Schema replicates
    ''' CharGenInterface.cpp SavePreset (lines 49-256) field-by-field. Three F4SE-only fields
    ''' (BodyMorphs / Overlays / Skin) are intentionally NOT emitted — see
    ''' memory/project_npc_looksmenu_pending.md for the rationale.
    '''
    ''' Per-field semantics (matches CharGenInterface.cpp behaviour):
    '''   • Gender (line 90): always emitted as UInt.
    '''   • HeadParts (line 92-103): array of "Plugin|HEX" strings. IsExtraPart filter is the
    '''     caller's responsibility (BuildPresetFromState filters before reaching here).
    '''   • HairColor (line 105-111): only when non-zero.
    '''   • Weight (line 113-115): array of 3 floats, always emitted.
    '''   • Morphs.Values (line 117-126): emitted ONLY when present. LoadPreset.Allocate(5) means
    '''     the engine works with exactly 5 slots — we pad/truncate to 5 to match.
    '''   • Morphs.Presets (line 128-139): dict hex→float, only when non-empty.
    '''   • Morphs.Regions (line 142-158): dict hex→[8 floats], only when non-empty.
    '''   • Morphs.Intensity (line 160-163): only emitted when != 1.0F.
    '''   • Tints + TintOrder (line 165-202): emitted only when there's at least one layer with
    '''     Value &gt; 0 (LooksMenu skips Value=0 entries at line 180-181 and only writes the
    '''     Tints object when it built at least one entry).
    '''   • Hex format throughout: "%X" uppercase, no zero-padding. Verified against actual
    '''     LooksMenu-saved JSON files (e.g. "4D7", "72A", "525").
    ''' </summary>
    Public Function SerializePreset(preset As LooksmenuPreset, pluginManager As PluginManager) As String
        If preset Is Nothing Then Return ""

        Using ms As New MemoryStream()
            ' StyledWriter in jsoncpp uses 3-space indentation. .NET's JsonWriter doesn't expose
            ' a knob for indent size; we let it write with default (2) and post-process below to
            ' match LooksMenu byte-for-byte readability. Using a raw MemoryStream + JsonWriter
            ' (rather than serializing to object then JsonSerializer) so we can preserve the
            ' field order LooksMenu emits — the engine doesn't depend on order but human diffs
            ' between presets do.
            ' UnsafeRelaxedJsonEscaping: emit UTF-8 raw (eñes, tildes, etc.) instead of \u escapes,
            ' so a preset with a non-ASCII EditorID or plugin name diffs cleanly against one
            ' written by jsoncpp's StyledWriter. The "Unsafe" name is misleading — it's safe for
            ' file output, just not for embedding inside HTML/JS where < > & need escaping.
            Dim writerOpts As New JsonWriterOptions() With {
                .Indented = True,
                .SkipValidation = False,
                .Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }
            Using w As New Utf8JsonWriter(ms, writerOpts)
                w.WriteStartObject()

                ' Gender
                w.WriteNumber("Gender", CUInt(preset.Gender))

                ' HeadParts
                w.WriteStartArray("HeadParts")
                For Each fid In preset.HeadPartFormIDs
                    Dim ident = FormatFormIdentifier(fid, pluginManager)
                    If Not String.IsNullOrEmpty(ident) Then w.WriteStringValue(ident)
                Next
                w.WriteEndArray()

                ' HairColor — only when non-zero (CharGenInterface.cpp:106-110)
                If preset.HairColorFormID <> 0UI Then
                    Dim hc = FormatFormIdentifier(preset.HairColorFormID, pluginManager)
                    If Not String.IsNullOrEmpty(hc) Then w.WriteString("HairColor", hc)
                End If

                ' Weight — always 3 floats (CharGenInterface.cpp:113-115). Missing slot = 0.
                w.WriteStartArray("Weight")
                w.WriteNumberValue(preset.WeightThin.GetValueOrDefault(0.0F))
                w.WriteNumberValue(preset.WeightMuscular.GetValueOrDefault(0.0F))
                w.WriteNumberValue(preset.WeightFat.GetValueOrDefault(0.0F))
                w.WriteEndArray()

                ' Morphs container — only emit when at least one sub-field has data.
                Dim hasValues = preset.BodyMorphValues.Count > 0
                Dim hasPresets = preset.ChargenFaceMorphs.Count > 0
                Dim hasRegions = preset.FaceBoneRegions.Count > 0
                Dim hasIntensity = (preset.FacialMorphIntensity <> 1.0F)
                If hasValues OrElse hasPresets OrElse hasRegions OrElse hasIntensity Then
                    w.WriteStartObject("Morphs")

                    If hasValues Then
                        ' LoadPreset.Allocate(5) hardcodes the array size — pad/truncate to match.
                        w.WriteStartArray("Values")
                        For i = 0 To 4
                            Dim v As Single = If(i < preset.BodyMorphValues.Count, preset.BodyMorphValues(i), 0.0F)
                            w.WriteNumberValue(v)
                        Next
                        w.WriteEndArray()
                    End If

                    If hasPresets Then
                        w.WriteStartObject("Presets")
                        For Each kv In preset.ChargenFaceMorphs
                            w.WriteNumber(kv.Key.ToString("X", Globalization.CultureInfo.InvariantCulture), kv.Value)
                        Next
                        w.WriteEndObject()
                    End If

                    If hasRegions Then
                        w.WriteStartObject("Regions")
                        For Each kv In preset.FaceBoneRegions
                            w.WriteStartArray(kv.Key.ToString("X", Globalization.CultureInfo.InvariantCulture))
                            For Each f In kv.Value
                                w.WriteNumberValue(f)
                            Next
                            w.WriteEndArray()
                        Next
                        w.WriteEndObject()
                    End If

                    If hasIntensity Then
                        w.WriteNumber("Intensity", preset.FacialMorphIntensity)
                    End If

                    w.WriteEndObject()
                End If

                ' Tints + TintOrder. Skip Value=0 entries (CharGenInterface.cpp:180-181). Both keys
                ' only emitted when at least one layer survives the filter. LooksMenu writes the
                ' Tints object once at the end (line 201) but TintOrder is appended per-entry as
                ' the loop runs (line 198). For our writer we precompute the surviving list once.
                Dim emittedTints = preset.FaceTintLayers.Where(Function(tl) tl.Value > 0).ToList()
                If emittedTints.Count > 0 Then
                    w.WriteStartObject("Tints")
                    For Each tl In emittedTints
                        Dim keyName = (CUInt(tl.Index) And &HFFFFUI).ToString("X", Globalization.CultureInfo.InvariantCulture)
                        w.WriteStartObject(keyName)
                        w.WriteNumber("Type", CInt(tl.Discriminator))
                        w.WriteNumber("Percent", CInt(tl.Value))
                        ' Palette-only color fields (CharGenInterface.cpp:191-195). Color is the
                        ' BGRA UInt32 written as Json::Int (signed bitcast). For Discriminator=2
                        ' (TextureSet) the engine writes neither Color nor ColorID.
                        If tl.Discriminator = 1US Then
                            ' Reconstruct BGRA from Color.FromArgb(A,R,G,B) → bytes [B,G,R,A] LE.
                            Dim bgra As UInteger =
                                (CUInt(tl.Color.A) << 24) Or
                                (CUInt(tl.Color.R) << 16) Or
                                (CUInt(tl.Color.G) << 8) Or
                                CUInt(tl.Color.B)
                            ' Cast to signed int32 (same bit pattern) to match Json::Int output.
                            w.WriteNumber("Color", BitConverter.ToInt32(BitConverter.GetBytes(bgra), 0))
                            w.WriteNumber("ColorID", tl.TemplateColorIndex)
                        End If
                        w.WriteEndObject()
                    Next
                    w.WriteEndObject()

                    w.WriteStartArray("TintOrder")
                    For Each tl In emittedTints
                        w.WriteStringValue((CUInt(tl.Index) And &HFFFFUI).ToString("X", Globalization.CultureInfo.InvariantCulture))
                    Next
                    w.WriteEndArray()
                End If

                w.WriteEndObject()
                w.Flush()
            End Using

            Dim json = Encoding.UTF8.GetString(ms.ToArray())
            ' Re-indent from 2 spaces (.NET default) to 3 (LooksMenu StyledWriter) so the file
            ' diffs cleanly against ones written in-game. Cheap line-by-line conversion.
            Return ConvertIndentationFromTwoToThree(json)
        End Using
    End Function

    Private Function ConvertIndentationFromTwoToThree(json As String) As String
        Dim sb As New System.Text.StringBuilder(json.Length + json.Length \ 8)
        Dim lines = json.Split({vbCrLf, vbLf}, StringSplitOptions.None)
        For i = 0 To lines.Length - 1
            Dim line = lines(i)
            Dim leading = 0
            While leading < line.Length AndAlso line(leading) = " "c
                leading += 1
            End While
            ' Each 2-space indent becomes 3 spaces. Odd remainders pass through (shouldn't happen).
            Dim depth = leading \ 2
            Dim extra = leading Mod 2
            sb.Append(New String(" "c, depth * 3 + extra))
            sb.Append(line, leading, line.Length - leading)
            If i < lines.Length - 1 Then sb.Append(vbLf)
        Next
        Return sb.ToString()
    End Function

    ''' <summary>Inverse of ResolveFormIdentifier: take a global FormID, find its owning plugin
    ''' in the load order, and emit "Plugin.esp|HEX" with the local 24-bit FormID.</summary>
    Private Function FormatFormIdentifier(globalFormID As UInteger, pluginManager As PluginManager) As String
        If globalFormID = 0UI Then Return ""
        Dim loadOrderIdx = CInt((globalFormID >> 24) And &HFFUI)
        Dim pluginName = pluginManager.GetPluginNameByLoadOrderIndex(loadOrderIdx)
        If String.IsNullOrEmpty(pluginName) Then Return ""
        Dim localFormID = globalFormID And &HFFFFFFUI
        ' LooksMenu uses %06X (6-digit zero-padded hex) per Utilities.cpp:127.
        Return pluginName & "|" & localFormID.ToString("X6", Globalization.CultureInfo.InvariantCulture)
    End Function

    ''' <summary>List preset JSON files. LooksMenu's actual convention (verified empirically and
    ''' against CharGenInterface.cpp:259-620 LoadPreset) is a FLAT directory: all presets live
    ''' directly in Data\F4SE\Plugins\F4EE\Presets\, no per-race subfolder. The UI compiled into
    ''' the LooksMenu .swf builds the path; the C++ side (ScaleformNatives.cpp:85-90) just receives
    ''' the file path string. The JSON itself does not store a race — only Gender — and LoadPreset
    ''' applies the preset to the current actor's race regardless of where the preset originated.
    '''
    ''' Returns absolute paths in alphabetical order, recursing one level so user-organized
    ''' subfolders (some users group presets by race or author) are still found.</summary>
    Public Function EnumeratePresetFiles(dataPath As String) As List(Of String)
        Dim result As New List(Of String)
        If String.IsNullOrEmpty(dataPath) Then Return result
        Dim dir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "Presets")
        If Not Directory.Exists(dir) Then Return result
        result.AddRange(Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
        Return result
    End Function
End Module
