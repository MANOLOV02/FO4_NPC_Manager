Imports System.IO
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>F4SE LooksMenu body-overlay template ("tattoo" definition) — id + display name +
''' gender + sort + playable/transformable/tintable flags + a per-biped-slot material map. Mirrors
''' the C++ class <c>OverlayTemplate</c> in <c>Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/OverlayInterface.h</c>
''' (:141-155) as <c>OverlayInterface::LoadOverlayTemplates</c> fills it (OverlayInterface.cpp:1055-1136).
'''
''' Templates are NOT records: they are loaded from on-disk JSON files under
''' <c>Data\F4SE\Plugins\F4EE\Overlays\</c> (per-plugin <c>overlays.json</c> + a Loose folder; see
''' <see cref="OverlayTemplateCache"/>). At runtime LooksMenu maps an applied <c>OverlayEntry.TemplateId</c>
''' → OverlayTemplate, then renders the template's slot materials onto the actor's body.</summary>
Public Class OverlayTemplate
    ''' <summary>Identifier from the JSON (the key an applied OverlayEntry references). Compared like the engine's
    ''' <c>F4EEFixedString</c>: ASCII case-insensitive (<see cref="F4eeFixedStringComparer"/>).</summary>
    Public Id As String = ""
    ''' <summary>Display name (JSON "name"). Engine default: empty (<c>F4EEFixedString</c> default, OverlayInterface.h:148).
    ''' May start with "$" = a LooksMenu translation key, kept verbatim. OverlayInterface.cpp:1093-1094.</summary>
    Public DisplayName As String = ""
    ''' <summary>0 = male, 1 = female: the map the template lives in (OverlayInterface.cpp:1080, :1084).</summary>
    Public Gender As Byte = 0
    ''' <summary>Sort order (OverlayInterface.cpp:1099-1100). Default 0 (ctor, OverlayInterface.h:144).</summary>
    Public Sort As Integer = 0
    ''' <summary>"playable" (OverlayInterface.cpp:1096-1097). Default false (ctor, OverlayInterface.h:144).</summary>
    Public Playable As Boolean = False
    ''' <summary>"transformable" — overlay accepts offsetUV/scaleUV (OverlayInterface.cpp:1102-1103).</summary>
    Public Transformable As Boolean = False
    ''' <summary>"tintable" — overlay accepts a tint color (OverlayInterface.cpp:1105-1106).</summary>
    Public Tintable As Boolean = False

    ''' <summary>biped object index (0..30 are applied: OverlayInterface.cpp:897) → material path. Filled with
    ''' <c>slotMaterial.emplace(slot["slot"].asUInt(), ...)</c> (OverlayInterface.cpp:1124): the FIRST material given for an
    ''' index stays, in the same template and across files that share the id (<see cref="OverlayTemplateCache"/>).
    ''' The <c>.bgem</c> effect-material flag (:1115-1121) is derived from the path at render time.</summary>
    Public SlotMaterials As New Dictionary(Of UInteger, String)

    Public Overrides Function ToString() As String
        Return If(String.IsNullOrEmpty(DisplayName), Id, DisplayName)
    End Function
End Class

''' <summary>The engine's two template maps, <c>m_overlayTemplates[2]</c> (<c>unordered_map&lt;F4EEFixedString,
''' OverlayTemplatePtr&gt;</c>, OverlayInterface.h:207), and the two routines that fill them:
''' <list type="bullet">
''' <item><see cref="LoadOverlayMods"/> = <c>OverlayInterface::LoadOverlayMods</c> (OverlayInterface.cpp:1025-1052):
''' per loaded plugin (<c>ForEachMod</c>: full plugins in load order, then light plugins — Utilities.cpp:288-299)
''' <c>F4SE\Plugins\F4EE\Overlays\&lt;plugin&gt;\overlays.json</c> through the resource system (loose OR inside a BA2);
''' then every <c>Data\F4SE\Plugins\F4EE\Overlays\Loose\*.json</c> from the disk only (<c>IDirectoryIterator</c>), in
''' <c>std::set&lt;std::string&gt;</c> order of the full path. The <c>std::transform</c> meant to lower-case the path (:1043)
''' is given the empty range <c>[begin, begin)</c> and changes nothing, so the order is case-sensitive byte order.</item>
''' <item><see cref="LoadTemplates"/> = <c>OverlayInterface::LoadOverlayTemplates</c> (:1055-1136): an id already in the
''' gender's map is REUSED — the later file overwrites the fields it has (<c>isMember</c>) and adds only the slots the
''' template lacks (<c>emplace</c>) — instead of being dropped.</item>
''' </list></summary>
Public NotInheritable Class OverlayTemplateCache

    Public Const OverlaysDir As String = "F4SE\Plugins\F4EE\Overlays"

    ' Per gender: id → template (engine map, F4EEFixedString equality) and first-insertion order (the engine map is
    ' unordered; the order only makes enumeration deterministic, every consumer sorts or looks up by id).
    Private ReadOnly _byId() As Dictionary(Of String, OverlayTemplate) = {
        New Dictionary(Of String, OverlayTemplate)(F4eeFixedStringComparer.Instancia),
        New Dictionary(Of String, OverlayTemplate)(F4eeFixedStringComparer.Instancia)}
    Private ReadOnly _order() As List(Of OverlayTemplate) = {New List(Of OverlayTemplate)(), New List(Of OverlayTemplate)()}

    ''' <summary><c>ClearMods</c> for the templates (OverlayInterface.h:160-165).</summary>
    Public Sub Clear()
        For g = 0 To 1
            _byId(g).Clear()
            _order(g).Clear()
        Next
    End Sub

    ''' <summary><c>LoadOverlayMods</c> (OverlayInterface.cpp:1025-1052). <paramref name="pluginsInEngineOrder"/>: the
    ''' loaded plugins' file names in <c>ForEachMod</c> order (full, then light). <paramref name="dataPath"/>: the Data
    ''' folder, for the Loose pass (filesystem only).</summary>
    Public Sub LoadOverlayMods(pluginsInEngineOrder As IEnumerable(Of String), dataPath As String)
        Clear()
        If pluginsInEngineOrder IsNot Nothing Then
            For Each pluginName In pluginsInEngineOrder
                If String.IsNullOrEmpty(pluginName) Then Continue For
                Dim rel = $"{OverlaysDir}\{pluginName}\overlays.json"
                Dim bytes As Byte() = Nothing
                Try
                    bytes = FilesDictionary_class.GetBytes(rel)
                Catch
                End Try
                If bytes IsNot Nothing AndAlso bytes.Length > 0 Then LoadTemplates(bytes, rel)
            Next
        End If
        If String.IsNullOrEmpty(dataPath) Then Return
        Dim looseDir = Path.Combine(dataPath, OverlaysDir, "Loose")
        If Not Directory.Exists(looseDir) Then Return
        Dim files = Directory.EnumerateFiles(looseDir, "*.json", SearchOption.TopDirectoryOnly).ToList()
        files.Sort(Jsoncpp.ComparadorStrcmp.Instancia)
        For Each f In files
            Dim bytes As Byte()
            Try
                bytes = File.ReadAllBytes(f)
            Catch
                Continue For
            End Try
            LoadTemplates(bytes, f)
        Next
    End Sub

    ''' <summary><c>LoadOverlayTemplates</c> (OverlayInterface.cpp:1055-1136) over the bytes <c>BSReadAll</c> reads
    ''' (Utilities.cpp:78-86: every byte, nothing cut) and <c>Json::Reader::parse</c> (json_reader.cpp:104-141): a BOM is
    ''' not skipped (the file is rejected), comments are allowed, a trailing comma is an error, trailing text after the
    ''' first value is ignored (<see cref="JsonPrimerValor"/>), bytes are taken raw (<see cref="Jsoncpp.BytesComoLosVeJsoncpp"/>).
    ''' Then <c>for(auto &amp; item : root)</c>, each item in its own try/catch (:1077-1131): an item that throws keeps what
    ''' it already applied.</summary>
    Public Sub LoadTemplates(bytes As Byte(), source As String)
        If bytes Is Nothing OrElse bytes.Length = 0 Then Return
        If bytes.Length >= 3 AndAlso bytes(0) = &HEF AndAlso bytes(1) = &HBB AndAlso bytes(2) = &HBF Then
            Logger.LogLazy(Function() $"[LM-OVERLAYS] '{source}': BOM al inicio; LooksMenu lo rechaza (json_reader.cpp:83-143). Se saltea.")
            Return
        End If
        Dim doc As JsonDocument
        Try
            Dim transcodificado As Boolean
            Dim bytesJsoncpp = Jsoncpp.BytesComoLosVeJsoncpp(bytes, transcodificado)
            If transcodificado Then
                Logger.LogLazy(Function() $"[LM-OVERLAYS] '{source}': no es UTF-8 válido; se lee con la encoding General ({PluginEncodingSettings.General.WebName}) (json_reader.cpp:390-400 no valida nada).")
            End If
            doc = JsonPrimerValor.Documento(bytesJsoncpp)
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-OVERLAYS] '{source}': JSON invalido para LooksMenu ({ex.Message}). Se saltea (OverlayInterface.cpp:1070-1073).")
            Return
        End Try
        Using doc
            For Each item In Jsoncpp.Valores(doc.RootElement)
                LoadItem(item)
            Next
        End Using
    End Sub

    ''' <summary>One item of <c>LoadOverlayTemplates</c> (OverlayInterface.cpp:1077-1125), in the engine's order. Every
    ''' <c>ok = False</c> is a jsoncpp throw caught at :1127-1130: the item stops there and what it already wrote stays.</summary>
    Private Sub LoadItem(item As JsonElement)
        Dim ok As Boolean
        ' gender = max(0, min(item["gender"].asUInt(), 1)) (:1080)
        Dim genderEl = Jsoncpp.Miembro(item, "gender", ok) : If Not ok Then Return
        Dim genderU = Jsoncpp.AsUInt(genderEl, ok) : If Not ok Then Return
        Dim gender = If(genderU >= 1UI, 1, 0)
        ' id = item["id"].asCString() (:1082)
        Dim idEl = Jsoncpp.Miembro(item, "id", ok) : If Not ok Then Return
        Dim id = Jsoncpp.AsCString(idEl, ok) : If Not ok Then Return
        ' find or create-and-insert (:1084-1090)
        Dim tpl As OverlayTemplate = Nothing
        If Not _byId(gender).TryGetValue(id, tpl) Then
            tpl = New OverlayTemplate With {.Id = id, .Gender = CByte(gender)}
            _byId(gender)(id) = tpl
            _order(gender).Add(tpl)
        End If
        ' name / playable / sort / transformable / tintable: only when the member is present (:1092-1106)
        If Jsoncpp.IsMember(item, "name", ok) Then
            Dim v = Jsoncpp.AsCString(Jsoncpp.Miembro(item, "name", ok), ok) : If Not ok Then Return
            tpl.DisplayName = v
        End If
        If Not ok Then Return
        If Not SetBool(item, "playable", Sub(b) tpl.Playable = b) Then Return
        If Jsoncpp.IsMember(item, "sort", ok) Then
            Dim v = Jsoncpp.AsInt(Jsoncpp.Miembro(item, "sort", ok), ok) : If Not ok Then Return
            tpl.Sort = v
        End If
        If Not ok Then Return
        If Not SetBool(item, "transformable", Sub(b) tpl.Transformable = b) Then Return
        If Not SetBool(item, "tintable", Sub(b) tpl.Tintable = b) Then Return
        ' slots (:1108-1125): range-for over item["slots"] (array ⇒ elements, object ⇒ values in strcmp key order, else
        ' nothing); per slot material = asString (:1113), then emplace(slot["slot"].asUInt(), material.asCString())
        If Jsoncpp.IsMember(item, "slots", ok) Then
            For Each slot In Jsoncpp.Valores(Jsoncpp.Miembro(item, "slots", ok))
                Dim matEl = Jsoncpp.Miembro(slot, "material", ok) : If Not ok Then Return
                Jsoncpp.AsString(matEl, ok) : If Not ok Then Return
                Dim idx = Jsoncpp.AsUInt(Jsoncpp.Miembro(slot, "slot", ok), ok) : If Not ok Then Return
                Dim mat = Jsoncpp.AsCString(matEl, ok) : If Not ok Then Return
                If Not tpl.SlotMaterials.ContainsKey(idx) Then tpl.SlotMaterials(idx) = mat
            Next
        End If
    End Sub

    ''' <summary><c>if(item.isMember(key)) field = item[key].asBool()</c>. False = jsoncpp threw (the item stops).</summary>
    Private Shared Function SetBool(item As JsonElement, key As String, setter As Action(Of Boolean)) As Boolean
        Dim ok As Boolean
        If Not Jsoncpp.IsMember(item, key, ok) Then Return ok
        Dim v = Jsoncpp.AsBool(Jsoncpp.Miembro(item, key, ok), ok)
        If Not ok Then Return False
        setter(v)
        Return True
    End Function

    ''' <summary><c>GetTemplateByName</c>: the template with this id in the gender's map (F4EEFixedString equality), or
    ''' Nothing — the overlay then contributes no layer (OverlayInterface.cpp:443-448).</summary>
    Public Function Resolve(id As String, isFemale As Boolean) As OverlayTemplate
        If String.IsNullOrEmpty(id) Then Return Nothing
        Dim tpl As OverlayTemplate = Nothing
        Return If(_byId(If(isFemale, 1, 0)).TryGetValue(id, tpl), tpl, Nothing)
    End Function

    ''' <summary>Every template of the gender, sorted like LooksMenu's list (ScaleformNatives.cpp:388-394): by sort, then
    ''' by display name as <c>std::string operator&lt;</c> (byte order, case-sensitive).</summary>
    Public Function Candidates(isFemale As Boolean) As List(Of OverlayTemplate)
        Dim r = _order(If(isFemale, 1, 0)).ToList()
        r.Sort(Function(a, b)
                   If a.Sort <> b.Sort Then Return a.Sort.CompareTo(b.Sort)
                   Return Jsoncpp.ComparadorStrcmp.Instancia.Compare(a.DisplayName, b.DisplayName)
               End Function)
        Return r
    End Function

    ''' <summary>The ids of every template of the gender, in a set with the engine's id equality.</summary>
    Public Function KnownIds(isFemale As Boolean) As HashSet(Of String)
        Return New HashSet(Of String)(_byId(If(isFemale, 1, 0)).Keys, F4eeFixedStringComparer.Instancia)
    End Function
End Class
