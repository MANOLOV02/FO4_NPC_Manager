Imports System.IO
Imports System.Text.Json

''' <summary>Per-plugin JSON sidecar storing F4SE-only fields that have no ESP record
''' equivalent — currently BodySlide morph sliders, the LM SkinTemplate id, and the LM body
''' overlays (tattoos). One file per
''' plugin: <c>&lt;plugin&gt;.bssliders</c> next to <c>&lt;plugin&gt;.esp</c>. Read at preflight
''' when the plugin is selected; merged + re-written by NpcOverrideSaver on Save ESP.
'''
''' Schema (version 1):
''' <code>
''' {
'''   "version": 1,
'''   "plugin": "NPC_Manager.esp",
'''   "npcs": {
'''     "ABCDEF": { "editorId": "Cait",
'''                 "bodyMorphs": { "BigBelly": 0.45 },
'''                 "skinTemplateId": "Vanilla CBBE",
'''                 "overlays": [ { "template": "Tattoo01", "priority": 0,
'''                                 "tint": [1,0,0,1], "offsetUV": [0,0], "scaleUV": [1,1] } ] }
'''   }
''' }
''' </code>
''' Key of <c>npcs</c> = LooksMenu-style form identifier <c>"Master.esp|HEX6"</c> (master
''' plugin name + local 24-bit FormID in uppercase 6-digit hex). Same convention LM uses for
''' its preset JSONs, so <see cref="LooksmenuLoader.ResolveFormIdentifier"/> resolves it to a
''' global FormID when the master is loaded. This makes the sidecar robust to overrides of
''' NPCs from multiple masters in the same override plugin (e.g. an ESP overriding both
''' Fallout4.esm and DLCRobot.esm NPCs would otherwise collide on bare 6-digit hex).
'''
''' Only NPCs with a non-empty bodyMorphs dict OR a non-empty skinTemplateId OR a non-empty
''' overlays list are persisted; everything else is dropped to keep the file small and avoid
''' leaving zero-NPC sidecars on disk.</summary>
Public Module BssliderSidecar

    Public Const Extension As String = ".bssliders"
    Public Const SchemaVersion As Integer = 1

    Public Class SidecarFile
        Public Version As Integer = SchemaVersion
        Public Plugin As String = ""
        Public Npcs As New Dictionary(Of String, NpcEntry)(StringComparer.OrdinalIgnoreCase)
    End Class

    Public Class NpcEntry
        Public EditorId As String = ""
        Public BodyMorphs As New Dictionary(Of String, Single)(StringComparer.OrdinalIgnoreCase)
        Public SkinTemplateId As String = ""
        ''' <summary>LM body overlays (tattoos) — NPC_Manager-internal persistence only (there is
        ''' no BodyGen/in-game file mechanism for overlays). Reuses the public
        ''' <see cref="LooksmenuLoader.OverlayEntry"/> so the in-memory overlay and the sidecar
        ''' share one type. Same on-disk shape as the LM preset overlay format (template +
        ''' priority always, optional tint[r,g,b,a]/offsetUV[x,y]/scaleUV[x,y]).</summary>
        Public Overlays As New List(Of LooksmenuLoader.OverlayEntry)
        ''' <summary>Optional gender hint: <c>"male"</c>, <c>"female"</c>, or empty (unknown).
        ''' Persisted alongside the sliders because the BodyGen emitter needs the gender to
        ''' filter <c>morphs.ini</c> rows, and at re-emit time the NPC's master plugin may not
        ''' be in the current load order (so we cannot re-derive it from the record). Empty =
        ''' BodyGen row written without a gender filter (engine applies to both).</summary>
        Public Gender As String = ""

        ''' <summary>True when this entry carries at least one slider or a non-empty template id.
        ''' Write() drops entries that don't satisfy this so the on-disk file never contains
        ''' rows that would be no-ops if re-applied.</summary>
        Public ReadOnly Property HasAnything As Boolean
            Get
                If BodyMorphs IsNot Nothing AndAlso BodyMorphs.Count > 0 Then Return True
                If Not String.IsNullOrEmpty(SkinTemplateId) Then Return True
                If Overlays IsNot Nothing AndAlso Overlays.Count > 0 Then Return True
                Return False
            End Get
        End Property
    End Class

    ''' <summary>Build the sidecar path for an ESP/ESM/ESL path: same directory, same basename,
    ''' <see cref="Extension"/> in place of the plugin extension.</summary>
    Public Function BuildPath(espPath As String) As String
        If String.IsNullOrEmpty(espPath) Then Return ""
        Return Path.ChangeExtension(espPath, Extension)
    End Function

    ''' <summary>Read sidecar JSON from disk. Returns Nothing when the file is missing,
    ''' unreadable, or not valid JSON. Logs nothing — caller decides whether/how to surface.
    ''' Schema-mismatch fields are silently ignored (forward-compat).</summary>
    Public Function Read(path As String) As SidecarFile
        If String.IsNullOrEmpty(path) OrElse Not File.Exists(path) Then Return Nothing
        Dim raw As String
        Try
            raw = File.ReadAllText(path)
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
            Dim result As New SidecarFile

            Dim el As JsonElement
            If root.TryGetProperty("version", el) AndAlso el.ValueKind = JsonValueKind.Number Then
                result.Version = el.GetInt32()
            End If
            If root.TryGetProperty("plugin", el) AndAlso el.ValueKind = JsonValueKind.String Then
                result.Plugin = el.GetString()
            End If
            If root.TryGetProperty("npcs", el) AndAlso el.ValueKind = JsonValueKind.Object Then
                For Each prop In el.EnumerateObject()
                    Dim entry = ParseNpcEntry(prop.Value)
                    If entry IsNot Nothing Then result.Npcs(prop.Name) = entry
                Next
            End If
            Return result
        End Using
    End Function

    Private Function ParseNpcEntry(el As JsonElement) As NpcEntry
        If el.ValueKind <> JsonValueKind.Object Then Return Nothing
        Dim entry As New NpcEntry
        Dim child As JsonElement
        If el.TryGetProperty("editorId", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.EditorId = child.GetString()
        End If
        If el.TryGetProperty("bodyMorphs", child) AndAlso child.ValueKind = JsonValueKind.Object Then
            For Each prop In child.EnumerateObject()
                If prop.Value.ValueKind = JsonValueKind.Number Then
                    entry.BodyMorphs(prop.Name) = prop.Value.GetSingle()
                End If
            Next
        End If
        If el.TryGetProperty("skinTemplateId", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.SkinTemplateId = child.GetString()
        End If
        If el.TryGetProperty("gender", child) AndAlso child.ValueKind = JsonValueKind.String Then
            entry.Gender = child.GetString()
        End If
        ' overlays — optional array of LM body overlays. Same element shape as the LM preset
        ' overlay format (see LooksmenuLoader.ParseFile's Overlays block): template required,
        ' priority default 0, optional tint[r,g,b,a]/offsetUV[x,y]/scaleUV[x,y] left Nothing when
        ' absent. An element without a template id can't reference a template, so it's skipped.
        If el.TryGetProperty("overlays", child) AndAlso child.ValueKind = JsonValueKind.Array Then
            For Each ov In child.EnumerateArray()
                If ov.ValueKind <> JsonValueKind.Object Then Continue For
                Dim tplEl As JsonElement
                If Not ov.TryGetProperty("template", tplEl) OrElse tplEl.ValueKind <> JsonValueKind.String Then Continue For
                Dim tplId = tplEl.GetString()
                If String.IsNullOrEmpty(tplId) Then Continue For

                Dim ovEntry As New LooksmenuLoader.OverlayEntry With {.TemplateId = tplId}

                Dim prEl As JsonElement
                If ov.TryGetProperty("priority", prEl) AndAlso prEl.ValueKind = JsonValueKind.Number Then
                    ovEntry.Priority = prEl.GetInt32()
                End If

                Dim tintEl As JsonElement
                If ov.TryGetProperty("tint", tintEl) AndAlso tintEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.Tint = ReadFloatArray(tintEl, 4)
                End If

                Dim offEl As JsonElement
                If ov.TryGetProperty("offsetUV", offEl) AndAlso offEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.OffsetUV = ReadFloatArray(offEl, 2)
                End If

                Dim sclEl As JsonElement
                If ov.TryGetProperty("scaleUV", sclEl) AndAlso sclEl.ValueKind = JsonValueKind.Array Then
                    ovEntry.ScaleUV = ReadFloatArray(sclEl, 2)
                End If

                entry.Overlays.Add(ovEntry)
            Next
        End If
        Return entry
    End Function

    ''' <summary>Read up to <paramref name="count"/> floats from a JSON array element into a fixed
    ''' Single() (missing/non-number slots stay 0). Mirror of LooksmenuLoader.ReadFloatArray —
    ''' duplicated here because that helper is Private to the loader.</summary>
    Private Function ReadFloatArray(arrEl As JsonElement, count As Integer) As Single()
        Dim result(count - 1) As Single
        Dim i As Integer = 0
        For Each v In arrEl.EnumerateArray()
            If i >= count Then Exit For
            If v.ValueKind = JsonValueKind.Number Then result(i) = v.GetSingle()
            i += 1
        Next
        Return result
    End Function

    ''' <summary>Write the sidecar JSON to disk atomically (.tmp + rename). Filters out NPC
    ''' entries that have neither sliders nor a skin template id. If nothing remains after
    ''' filtering, the existing sidecar (if any) is deleted instead of writing an empty file.
    ''' Indented output, npcs keys sorted ascending so diffs across saves stay readable.</summary>
    Public Sub Write(path As String, sidecar As SidecarFile)
        If String.IsNullOrEmpty(path) OrElse sidecar Is Nothing Then Return

        Dim kept = sidecar.Npcs.
            Where(Function(kv) kv.Value IsNot Nothing AndAlso kv.Value.HasAnything).
            OrderBy(Function(kv) kv.Key, StringComparer.OrdinalIgnoreCase).
            ToList()

        If kept.Count = 0 Then
            Try
                If File.Exists(path) Then File.Delete(path)
            Catch
                ' Best-effort cleanup; a leftover empty sidecar is harmless on next read.
            End Try
            Return
        End If

        Dim opts As New JsonWriterOptions With {.Indented = True}
        Dim bytes() As Byte
        Using ms As New MemoryStream()
            Using w As New Utf8JsonWriter(ms, opts)
                w.WriteStartObject()
                w.WriteNumber("version", SchemaVersion)
                w.WriteString("plugin", If(sidecar.Plugin, ""))
                w.WriteStartObject("npcs")
                For Each kv In kept
                    w.WriteStartObject(kv.Key)
                    w.WriteString("editorId", If(kv.Value.EditorId, ""))
                    If kv.Value.BodyMorphs IsNot Nothing AndAlso kv.Value.BodyMorphs.Count > 0 Then
                        w.WriteStartObject("bodyMorphs")
                        For Each bm In kv.Value.BodyMorphs.OrderBy(Function(p) p.Key, StringComparer.Ordinal)
                            w.WriteNumber(bm.Key, bm.Value)
                        Next
                        w.WriteEndObject()
                    End If
                    If Not String.IsNullOrEmpty(kv.Value.SkinTemplateId) Then
                        w.WriteString("skinTemplateId", kv.Value.SkinTemplateId)
                    End If
                    If Not String.IsNullOrEmpty(kv.Value.Gender) Then
                        w.WriteString("gender", kv.Value.Gender)
                    End If
                    ' overlays — emitted when non-empty. template + priority always; tint/offsetUV/
                    ' scaleUV only when non-Nothing. Mirrors the LM serializer's float-array idiom
                    ' (LooksmenuLoader.SerializePreset Overlays block) but with the sidecar's
                    ' insertion order preserved (priority drives render order independently).
                    If kv.Value.Overlays IsNot Nothing AndAlso kv.Value.Overlays.Count > 0 Then
                        w.WriteStartArray("overlays")
                        For Each ov In kv.Value.Overlays
                            w.WriteStartObject()
                            w.WriteString("template", ov.TemplateId)
                            w.WriteNumber("priority", ov.Priority)
                            If ov.Tint IsNot Nothing Then
                                w.WriteStartArray("tint")
                                For Each f In ov.Tint : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            If ov.OffsetUV IsNot Nothing Then
                                w.WriteStartArray("offsetUV")
                                For Each f In ov.OffsetUV : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            If ov.ScaleUV IsNot Nothing Then
                                w.WriteStartArray("scaleUV")
                                For Each f In ov.ScaleUV : w.WriteNumberValue(f) : Next
                                w.WriteEndArray()
                            End If
                            w.WriteEndObject()
                        Next
                        w.WriteEndArray()
                    End If
                    w.WriteEndObject()
                Next
                w.WriteEndObject()
                w.WriteEndObject()
                w.Flush()
            End Using
            bytes = ms.ToArray()
        End Using

        Dim tmp = path & ".tmp"
        File.WriteAllBytes(tmp, bytes)
        If File.Exists(path) Then File.Delete(path)
        File.Move(tmp, path)
    End Sub

    ''' <summary>Build a LM-style form identifier <c>"Master.esp|HEX6"</c> from a master plugin
    ''' filename and a (global) FormID. Only the low 24 bits of the FormID are used — the high
    ''' byte (load-order index) is intentionally dropped so the identifier stays stable across
    ''' different load orders.</summary>
    Public Function BuildIdentifier(masterPluginName As String, globalFormID As UInteger) As String
        Return $"{If(masterPluginName, "")}|{(globalFormID And &HFFFFFFUI):X6}"
    End Function

    ''' <summary>Reverse of <see cref="BuildIdentifier"/>: split <c>"Master.esp|HEX6"</c> into
    ''' the master filename and the local 24-bit FormID. Returns Nothing if the identifier is
    ''' malformed (no pipe, hex unparseable, empty master). Caller resolves the master to a
    ''' load-order index via <see cref="LooksmenuLoader.ResolveFormIdentifier"/> to compose the
    ''' global FormID.</summary>
    Public Function TryParseIdentifier(identifier As String,
                                       ByRef masterPluginName As String,
                                       ByRef localFormID As UInteger) As Boolean
        masterPluginName = ""
        localFormID = 0UI
        If String.IsNullOrEmpty(identifier) Then Return False
        Dim pipeIdx = identifier.IndexOf("|"c)
        If pipeIdx <= 0 OrElse pipeIdx >= identifier.Length - 1 Then Return False
        Dim master = identifier.Substring(0, pipeIdx).Trim()
        If String.IsNullOrEmpty(master) Then Return False
        Dim hex = identifier.Substring(pipeIdx + 1).Trim()
        Dim parsed As UInteger
        If Not UInteger.TryParse(hex, Globalization.NumberStyles.HexNumber,
                                 Globalization.CultureInfo.InvariantCulture, parsed) Then Return False
        masterPluginName = master
        localFormID = parsed And &HFFFFFFUI
        Return True
    End Function

End Module
