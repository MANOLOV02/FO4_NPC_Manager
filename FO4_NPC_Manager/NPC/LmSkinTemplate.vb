Imports System.IO
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>F4SE LooksMenu skin template — bundle of (skin ARMO + per-gender face TXST + per-gender
''' head HDPT + per-gender rear HDPT + sort + gender). Mirrors the C++ struct
''' <c>SkinTemplate</c> in <c>F4SEPlugins-master/f4ee/SkinInterface.h:18-44</c>.
'''
''' Templates are NOT records: they are loaded from on-disk JSON files at
''' <c>Data\F4SE\Plugins\F4EE\Skin\&lt;plugin&gt;\skin.json</c> and
''' <c>Data\F4SE\Plugins\F4EE\Skin\Loose\*.json</c> (see SkinInterface.cpp:461-488).
'''
''' At runtime LooksMenu maps an NPC's preset string id → SkinTemplate, then
''' <c>ApplyOverride</c> (SkinInterface.cpp:250-332) overwrites <c>npc-&gt;skinForm.skin</c> with
''' <c>template.skin</c> and (if <c>doFace</c>) overlays face textures + head/headRear parts. We
''' only consume the <c>skin</c> ARMO for body preview here; head/face overrides are layered into
''' the existing render path in MainForm.</summary>
Public Class LmSkinTemplate
    ''' <summary>Stable identifier from the JSON (the key LM persists in the actor preset).</summary>
    Public Id As String = ""
    ''' <summary>Display name. Falls back to <see cref="Id"/> if the JSON omits "name".</summary>
    Public DisplayName As String = ""
    ''' <summary>0 = male, 1 = female, 2 = unisex. SkinInterface.h:38.</summary>
    Public Gender As Byte = 2
    ''' <summary>Sort order from the JSON. Used to order combo entries (ascending).</summary>
    Public Sort As Integer = 0

    ''' <summary>Index 0 = male, 1 = female. ARMO override that replaces npc.skinForm.skin.</summary>
    Public SkinArmoFormID As UInteger = 0UI
    ''' <summary>face[0]=male, face[1]=female. TXST overrides for face textures (skin tint).</summary>
    Public FaceTxstFormID() As UInteger = New UInteger() {0UI, 0UI}
    ''' <summary>head[0]=male, head[1]=female. HDPT replaces the actor's Face headpart.</summary>
    Public HeadHdptFormID() As UInteger = New UInteger() {0UI, 0UI}
    ''' <summary>rear[0]=male, rear[1]=female. HDPT replaces the actor's HeadRear headpart.</summary>
    Public HeadRearHdptFormID() As UInteger = New UInteger() {0UI, 0UI}

    Public Overrides Function ToString() As String
        Return If(String.IsNullOrEmpty(DisplayName), Id, DisplayName)
    End Function
End Class

''' <summary>Loader for LM skin template JSONs. Parser shape mirrors
''' <c>SkinInterface::LoadSkinTemplates</c> (SkinInterface.cpp:490-621): top-level array of objects
''' with optional id/name/gender/sort/maleFace/femaleFace/maleHead/femaleHead/maleHeadRear/
''' femaleHeadRear/skin. "PluginFile|FORMID" identifiers are resolved against the active load
''' order via LooksmenuLoader.ResolveFormIdentifier (same convention LM uses for actor presets).
''' Unresolved templates / unresolvable identifiers are skipped silently (LM does the same:
''' <c>if(form)</c> guards in SkinInterface.cpp:534-608).</summary>
Public Module LmSkinTemplateLoader

    ''' <summary>Discover and parse every F4SE LooksMenu skin template reachable from
    ''' <paramref name="dataPath"/>. Mirrors f4ee/SkinInterface.cpp:461-488 (LoadSkinMods): each loaded
    ''' plugin's <c>Data\F4SE\Plugins\F4EE\Skin\&lt;pluginName&gt;\skin.json</c> plus the loose
    ''' <c>Skin\Loose\*.json</c> folder. Never Nothing — an absent Skin dir yields an empty list.
    '''
    ''' <para>Shared by MainForm (GUI) and the headless bake (Program.HeadlessBakeAll) so both resolve
    ''' LM SkinTemplate ids against the identical template set.</para></summary>
    Public Function BuildCache(dataPath As String, pluginManager As PluginManager) As List(Of LmSkinTemplate)
        Dim result As New List(Of LmSkinTemplate)
        If String.IsNullOrEmpty(dataPath) OrElse pluginManager Is Nothing Then Return result
        Dim baseSkinDir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "Skin")
        If Not Directory.Exists(baseSkinDir) Then Return result
        ' Per-plugin templates: Skin\<pluginName>\skin.json
        For Each plugin In pluginManager.Plugins
            Dim p = Path.Combine(baseSkinDir, plugin.FileName, "skin.json")
            If File.Exists(p) Then LoadFromFile(p, pluginManager, result)
        Next
        ' Loose templates: Skin\Loose\*.json
        Dim looseDir = Path.Combine(baseSkinDir, "Loose")
        If Directory.Exists(looseDir) Then
            For Each p In Directory.EnumerateFiles(looseDir, "*.json", SearchOption.TopDirectoryOnly)
                LoadFromFile(p, pluginManager, result)
            Next
        End If
        Return result
    End Function

    ''' <summary>Parse one JSON file and append every successfully-resolved template to <paramref name="sink"/>.
    ''' Templates with the same Id from later files DO NOT replace earlier entries (we keep first-loaded
    ''' to mirror C++ <c>m_skinTemplates.emplace</c> which only inserts when the key is missing —
    ''' SkinInterface.cpp:518-525). Returns the number of templates appended in this call.</summary>
    Public Function LoadFromFile(filePath As String, pluginManager As PluginManager,
                                  sink As List(Of LmSkinTemplate)) As Integer
        If Not File.Exists(filePath) Then Return 0
        Dim added As Integer = 0
        Try
            Dim raw = File.ReadAllText(filePath)
            ' jsoncpp (the C++ parser LM uses) accepts // and /* */ comments and trailing
            ' commas by default; .NET's JsonDocument rejects both unless told otherwise.
            ' Real-world skin.json files (e.g. RB_basicskins) ship with // field annotations,
            ' so without these flags the parse throws and we silently end up with 0 templates.
            Dim opts As New JsonDocumentOptions With {
                .CommentHandling = JsonCommentHandling.Skip,
                .AllowTrailingCommas = True
            }
            Using doc = JsonDocument.Parse(raw, opts)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return 0
                For Each item In doc.RootElement.EnumerateArray()
                    Dim tpl = ParseTemplate(item, pluginManager)
                    If tpl Is Nothing Then Continue For
                    ' First-loaded wins (C++ parity). Skip if a template with the same Id is already in the sink.
                    Dim duplicate As Boolean = False
                    For Each existing In sink
                        If String.Equals(existing.Id, tpl.Id, StringComparison.Ordinal) Then
                            duplicate = True
                            Exit For
                        End If
                    Next
                    If duplicate Then Continue For
                    sink.Add(tpl)
                    added += 1
                Next
            End Using
        Catch
        End Try
        Return added
    End Function

    Private Function ParseTemplate(item As JsonElement, pluginManager As PluginManager) As LmSkinTemplate
        If item.ValueKind <> JsonValueKind.Object Then Return Nothing

        Dim idEl As JsonElement
        If Not item.TryGetProperty("id", idEl) OrElse idEl.ValueKind <> JsonValueKind.String Then
            Return Nothing
        End If
        Dim id = idEl.GetString()
        If String.IsNullOrEmpty(id) Then Return Nothing

        Dim tpl As New LmSkinTemplate With {.Id = id, .DisplayName = id}

        Dim el As JsonElement
        If item.TryGetProperty("name", el) AndAlso el.ValueKind = JsonValueKind.String Then
            tpl.DisplayName = el.GetString()
        End If
        If item.TryGetProperty("gender", el) AndAlso el.ValueKind = JsonValueKind.Number Then
            Dim g As Integer
            If el.TryGetInt32(g) AndAlso g >= 0 AndAlso g <= 255 Then tpl.Gender = CByte(g)
        End If
        If item.TryGetProperty("sort", el) AndAlso el.ValueKind = JsonValueKind.Number Then
            Dim s As Integer
            If el.TryGetInt32(s) Then tpl.Sort = s
        End If

        ' "maleFace" / "femaleFace" → TXST FormID. C++ DYNAMIC_CAST to BGSTextureSet, but we
        ' don't validate the record signature here — the consumer can guard before use.
        tpl.FaceTxstFormID(0) = ResolveOptionalIdentifier(item, "maleFace", pluginManager)
        tpl.FaceTxstFormID(1) = ResolveOptionalIdentifier(item, "femaleFace", pluginManager)
        tpl.HeadHdptFormID(0) = ResolveOptionalIdentifier(item, "maleHead", pluginManager)
        tpl.HeadHdptFormID(1) = ResolveOptionalIdentifier(item, "femaleHead", pluginManager)
        tpl.HeadRearHdptFormID(0) = ResolveOptionalIdentifier(item, "maleHeadRear", pluginManager)
        tpl.HeadRearHdptFormID(1) = ResolveOptionalIdentifier(item, "femaleHeadRear", pluginManager)
        tpl.SkinArmoFormID = ResolveOptionalIdentifier(item, "skin", pluginManager)

        Return tpl
    End Function

    Private Function ResolveOptionalIdentifier(item As JsonElement, propName As String,
                                                pluginManager As PluginManager) As UInteger
        Dim el As JsonElement
        If Not item.TryGetProperty(propName, el) Then Return 0UI
        If el.ValueKind <> JsonValueKind.String Then Return 0UI
        Dim s = el.GetString()
        If String.IsNullOrEmpty(s) Then Return 0UI
        Return LooksmenuLoader.ResolveFormIdentifier(s, pluginManager)
    End Function

End Module
