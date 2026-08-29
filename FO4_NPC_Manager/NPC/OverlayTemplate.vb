Imports System.IO
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>F4SE LooksMenu body-overlay template ("tattoo" definition) — id + display name +
''' gender + sort + playable/transformable/tintable flags + a per-biped-slot material map. Mirrors
''' the C++ struct <c>OverlayTemplate</c> in <c>Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/OverlayInterface.h</c> and the
''' loader in <c>OverlayInterface::LoadOverlayTemplates</c> (OverlayInterface.cpp:1055-1136).
'''
''' Templates are NOT records: they are loaded from on-disk JSON files under
''' <c>Data\F4SE\Plugins\F4EE\Overlays\</c> (per-plugin <c>overlays.json</c> + a Loose folder; see
''' OverlayInterface.cpp:1041-1051). At runtime LooksMenu maps an applied <c>OverlayEntry.TemplateId</c>
''' → OverlayTemplate, then renders the template's slot materials onto the actor's body. We only
''' carry the data here (Phase 1); render wiring is a later phase, so the <c>.bgem</c> effect-material
''' detection done at OverlayInterface.cpp:1115-1121 is deferred — we store the raw material path.</summary>
Public Class OverlayTemplate
    ''' <summary>Stable identifier from the JSON (the key an applied OverlayEntry references).</summary>
    Public Id As String = ""
    ''' <summary>Display name (JSON "name"). May start with "$" = a LooksMenu translation key; we keep
    ''' the leading $ verbatim — translation resolution is a later concern. OverlayInterface.cpp:1093-1094.</summary>
    Public DisplayName As String = ""
    ''' <summary>0 = male, 1 = female. Clamped to 0..1 on load (OverlayInterface.cpp:1080).</summary>
    Public Gender As Byte = 0
    ''' <summary>Sort order from the JSON (OverlayInterface.cpp:1099-1100). Used to order combo entries.</summary>
    Public Sort As Integer = 0
    ''' <summary>"playable" flag (OverlayInterface.cpp:1096-1097). Default false (struct default).</summary>
    Public Playable As Boolean = False
    ''' <summary>"transformable" flag — overlay accepts offsetUV/scaleUV (OverlayInterface.cpp:1102-1103).</summary>
    Public Transformable As Boolean = False
    ''' <summary>"tintable" flag — overlay accepts a tint color (OverlayInterface.cpp:1105-1106).</summary>
    Public Tintable As Boolean = False

    ''' <summary>biped slot index → material path. JSON "slots" is an array of {slot:uint, material:string}
    ''' (OverlayInterface.cpp:1108-1125). We store the raw material path; <c>.bgem</c> effect-material
    ''' detection (engine :1115-1121) is a render-phase concern, deferred here.</summary>
    Public SlotMaterials As New Dictionary(Of Integer, String)

    Public Overrides Function ToString() As String
        Return If(String.IsNullOrEmpty(DisplayName), Id, DisplayName)
    End Function
End Class

''' <summary>Loader for LM overlay-template JSONs. Parser shape mirrors
''' <c>OverlayInterface::LoadOverlayTemplates</c> (OverlayInterface.cpp:1055-1136): a top-level array of
''' objects with id/name/gender/sort/playable/transformable/tintable/slots. Each item is parsed inside
''' its own try/catch so one malformed entry doesn't kill the file (engine :1077-1131 wraps every item
''' the same way). Gender is clamped to 0..1 (engine :1080). Returns an empty list (never Nothing) on an
''' unreadable / invalid file.</summary>
Public Module OverlayTemplateLoader

    ''' <summary>Parse one overlays.json file into a list of templates. Returns an empty list when the
    ''' file is missing, unreadable, or not a JSON array — never Nothing. Each successfully-parsed item
    ''' is appended; malformed items are skipped silently (mirrors the engine's per-item try/catch).</summary>
    Public Function LoadFromFile(filePath As String) As List(Of OverlayTemplate)
        Dim result As New List(Of OverlayTemplate)
        If String.IsNullOrEmpty(filePath) OrElse Not File.Exists(filePath) Then Return result

        Dim raw As String
        Try
            raw = File.ReadAllText(filePath)
        Catch
            Return result
        End Try

        ' jsoncpp (the C++ parser LM uses) accepts // and /* */ comments and trailing commas by
        ' default; .NET's JsonDocument rejects both unless told otherwise. Match the skin-template
        ' loader, which hit real-world files shipping // annotations.
        Dim opts As New JsonDocumentOptions With {
            .CommentHandling = JsonCommentHandling.Skip,
            .AllowTrailingCommas = True
        }

        Dim doc As JsonDocument
        Try
            doc = JsonDocument.Parse(raw, opts)
        Catch
            Return result
        End Try

        Using doc
            If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return result
            For Each item In doc.RootElement.EnumerateArray()
                ' Per-item try/catch: one bad entry shouldn't drop the rest (engine :1077-1131).
                Try
                    Dim tpl = ParseTemplate(item)
                    If tpl IsNot Nothing Then result.Add(tpl)
                Catch
                End Try
            Next
        End Using

        Return result
    End Function

    Private Function ParseTemplate(item As JsonElement) As OverlayTemplate
        If item.ValueKind <> JsonValueKind.Object Then Return Nothing

        ' id — required. The engine reads item["id"] unconditionally (OverlayInterface.cpp:1082) and
        ' keys the template map by it; an entry without an id can't be referenced by an OverlayEntry,
        ' so we skip it (same shape as LmSkinTemplateLoader.ParseTemplate).
        Dim idEl As JsonElement
        If Not item.TryGetProperty("id", idEl) OrElse idEl.ValueKind <> JsonValueKind.String Then
            Return Nothing
        End If
        Dim id = idEl.GetString()
        If String.IsNullOrEmpty(id) Then Return Nothing

        Dim tpl As New OverlayTemplate With {.Id = id, .DisplayName = id}

        Dim el As JsonElement

        ' name (OverlayInterface.cpp:1093-1094) — keep leading $ verbatim (translation key).
        If item.TryGetProperty("name", el) AndAlso el.ValueKind = JsonValueKind.String Then
            tpl.DisplayName = el.GetString()
        End If

        ' gender — clamp 0..1 (OverlayInterface.cpp:1080).
        If item.TryGetProperty("gender", el) AndAlso el.ValueKind = JsonValueKind.Number Then
            Dim g As Integer
            If el.TryGetInt32(g) Then tpl.Gender = CByte(Math.Min(1, Math.Max(0, g)))
        End If

        ' sort (OverlayInterface.cpp:1099-1100).
        If item.TryGetProperty("sort", el) AndAlso el.ValueKind = JsonValueKind.Number Then
            Dim s As Integer
            If el.TryGetInt32(s) Then tpl.Sort = s
        End If

        ' playable / transformable / tintable (OverlayInterface.cpp:1096-1106).
        tpl.Playable = ReadBool(item, "playable")
        tpl.Transformable = ReadBool(item, "transformable")
        tpl.Tintable = ReadBool(item, "tintable")

        ' slots — array of {slot:uint, material:string} (OverlayInterface.cpp:1108-1125). Store the raw
        ' material path; .bgem effect detection (engine :1115-1121) is render-phase, deferred here.
        Dim slotsEl As JsonElement
        If item.TryGetProperty("slots", slotsEl) AndAlso slotsEl.ValueKind = JsonValueKind.Array Then
            For Each slot In slotsEl.EnumerateArray()
                If slot.ValueKind <> JsonValueKind.Object Then Continue For
                Dim slotIdxEl As JsonElement
                Dim matEl As JsonElement
                If Not slot.TryGetProperty("slot", slotIdxEl) OrElse slotIdxEl.ValueKind <> JsonValueKind.Number Then Continue For
                If Not slot.TryGetProperty("material", matEl) OrElse matEl.ValueKind <> JsonValueKind.String Then Continue For
                Dim slotIdx As Integer
                If Not slotIdxEl.TryGetInt32(slotIdx) Then Continue For
                ' Last write wins on duplicate slot index (engine uses emplace which keeps first, but
                ' duplicate slots in a single template are not expected; keep it simple).
                tpl.SlotMaterials(slotIdx) = If(matEl.GetString(), "")
            Next
        End If

        Return tpl
    End Function

    Private Function ReadBool(item As JsonElement, propName As String) As Boolean
        Dim el As JsonElement
        If Not item.TryGetProperty(propName, el) Then Return False
        Return el.ValueKind = JsonValueKind.True
    End Function

End Module
