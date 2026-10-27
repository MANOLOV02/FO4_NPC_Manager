Imports System.IO
Imports FO4_Base_Library

''' <summary>F4SE LooksMenu skin template — bundle of (skin ARMO + per-gender face TXST + per-gender
''' head HDPT + per-gender rear HDPT + sort + gender). Mirrors the C++ struct
''' <c>SkinTemplate</c> in <c>Script extenders, Racemenu y Looksmenu/F4SEPlugins/f4ee/SkinInterface.h:18-44</c>.
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

''' <summary>Loader for LM skin template JSONs, as <c>SkinInterface::LoadSkinTemplates</c> (SkinInterface.cpp:490-621) reads them:
''' f4ee's Json::Reader (Jsoncpp.LeerF4ee) and its conversions on the tree (<see cref="LoadFromFile"/>). "PluginFile|FORMID"
''' identifiers are resolved against the active load order via LooksmenuLoader.ResolveFormIdentifier (Utilities.cpp:133-155); an
''' unresolvable one, or a form of another type, leaves the field as it was (the <c>if(form)</c> / DYNAMIC_CAST guards,
''' SkinInterface.cpp:534-608).</summary>
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
        ' Loose templates: Skin\Loose\*.json, in f4ee's order - a std::set<std::string> of the full paths (SkinInterface.cpp:476-487;
        ' its std::transform to lower case runs over an empty range, path.begin()..path.begin(), so the paths keep their case): byte
        ' order (strcmp). The order counts: a later file MODIFIES a template an earlier one registered.
        Dim looseDir = Path.Combine(baseSkinDir, "Loose")
        If Directory.Exists(looseDir) Then
            Dim sueltos = Directory.EnumerateFiles(looseDir, "*.json", SearchOption.TopDirectoryOnly).ToList()
            sueltos.Sort(Jsoncpp.ComparadorStrcmp.Instancia)
            For Each p In sueltos
                LoadFromFile(p, pluginManager, result)
            Next
        End If
        Return result
    End Function

    ''' <summary>⛔ El resolvedor de plantillas de piel para el juego ACTIVO, armado sobre la carpeta
    ''' Data del juego. Existe para que ningun llamador --y sobre todo ningun gate-- tenga que saber
    ''' donde viven las plantillas: los seis delegados de guardado pasaban `Nothing` y con eso el camino
    ''' de la plantilla de piel quedaba sin medir.
    ''' <para>⛔ Recorre el disco: se arma UNA vez por contexto, no por NPC.</para></summary>
    Private ReadOnly _memoDelJuego As New Dictionary(Of String, NpcRecordOverlay.ResolveLmSkinTemplateDelegate)(StringComparer.Ordinal)
    Private _memoDelJuegoPm As PluginManager = Nothing

    Public Function ResolvedorDelJuego(pluginManager As PluginManager) As NpcRecordOverlay.ResolveLmSkinTemplateDelegate
        Dim data = If(Config_App.Current Is Nothing, "", Config_App.Current.DataPath)
        ' ⛔ MEMO. `BuildCache` RECORRE EL DISCO --un `skin.json` por plugin cargado mas la carpeta
        ' suelta-- y los llamadores de gate lo pedian por SUJETO, no por contexto: la doc decia "una vez
        ' por contexto" y el codigo la desmentia. El memo va ACA y no en cada gate, por lo mismo que la
        ' busqueda: una linea de cache copiada seis veces son seis caches que se desincronizan.
        ' ⛔ La clave incluye el gestor de plugins por IDENTIDAD: los gates cambian de juego creando
        ' uno nuevo, y con la ruta sola el catalogo del juego anterior quedaria pegado.
        SyncLock _memoDelJuego
            If Not ReferenceEquals(_memoDelJuegoPm, pluginManager) Then
                _memoDelJuego.Clear()
                _memoDelJuegoPm = pluginManager
            End If
            Dim hecho As NpcRecordOverlay.ResolveLmSkinTemplateDelegate = Nothing
            If _memoDelJuego.TryGetValue(data, hecho) Then Return hecho
            hecho = Resolvedor(BuildCache(data, pluginManager))
            _memoDelJuego(data) = hecho
            Return hecho
        End SyncLock
    End Function

    ''' <summary>⛔ El resolvedor de id -> plantilla que consume el overlay, sobre la lista dada.
    ''' <para>La busqueda estaba escrita a mano en `MainForm` y otra vez en `BakeAllRunner`, y los seis
    ''' delegados de guardado de los gates no tenian ninguna: pasaban `Nothing`, con lo cual
    ''' `MaterializeLmTemplateBundleToPreset` se iba de largo y el camino entero de la plantilla de piel
    ''' quedaba sin medir, en verde. Con el catalogo se arma aca:
    ''' `Resolvedor(BuildCache(dataPath, pm))`.</para>
    ''' <para>Ordinal, como el mapa de cadenas de f4ee.</para></summary>
    Public Function Resolvedor(lista As IEnumerable(Of LmSkinTemplate)) As NpcRecordOverlay.ResolveLmSkinTemplateDelegate
        Dim copia As New List(Of LmSkinTemplate)
        If lista IsNot Nothing Then copia.AddRange(lista)
        Return Function(templateId As String) As LmSkinTemplate
                   If String.IsNullOrEmpty(templateId) Then Return Nothing
                   For Each tpl In copia
                       If String.Equals(tpl.Id, templateId, StringComparison.Ordinal) Then Return tpl
                   Next
                   Return Nothing
               End Function
    End Function

    ''' <summary>One skin template file as <c>SkinInterface::LoadSkinTemplates</c> (SkinInterface.cpp:490-621) reads it: the file's bytes
    ''' (BSReadAll) through f4ee's <c>Json::Reader</c> (<see cref="Jsoncpp.LeerF4ee"/>) - a file that does not parse (a trailing comma, a
    ''' BOM) loads nothing (:507-510) -, then <c>for(auto &amp; item : root)</c> (an array's elements, an object's values), each item in its
    ''' own try (:515/:614): <c>id</c> asCString (a non-string throws: the item is skipped); an id already registered - by an earlier
    ''' file or item - is MODIFIED (m_skinTemplates.find :519-524), a new one is registered right away (emplace :526-527), so a
    ''' conversion that throws later leaves the fields set before it. Fields, in f4ee's order: name (asCString), gender (asUInt into a
    ''' UInt8), maleFace / femaleFace (TXST), maleHead / femaleHead / maleHeadRear / femaleHeadRear (HDPT), skin (ARMO) - each only when a
    ''' member and not null, asString, GetFormFromIdentifier, set only when the form is of that type -, sort (asInt). Returns the number
    ''' of templates this file registered.</summary>
    Public Function LoadFromFile(filePath As String, pluginManager As PluginManager,
                                  sink As List(Of LmSkinTemplate)) As Integer
        If Not File.Exists(filePath) Then Return 0
        Dim raiz As Jsoncpp.JsonValor
        Try
            Dim transcodificado As Boolean
            raiz = Jsoncpp.LeerF4ee(Jsoncpp.BytesComoLosVeJsoncpp(File.ReadAllBytes(filePath), transcodificado))
        Catch
            Return 0
        End Try
        If raiz Is Nothing Then Return 0
        Dim added As Integer = 0
        For Each item In Jsoncpp.Valores(raiz)
            If LeerPlantilla(item, pluginManager, sink) Then added += 1
        Next
        Return added
    End Function

    ''' <summary>One item of the file (the body of SkinInterface.cpp:515-613). True when it registered a NEW template. A conversion that
    ''' throws ends the item where it is: what was set before stays (the template is already in the map).</summary>
    Private Function LeerPlantilla(item As Jsoncpp.JsonValor, pluginManager As PluginManager, sink As List(Of LmSkinTemplate)) As Boolean
        Dim ok As Boolean
        ' item["id"].asCString() (:519): on an item that is not an object (or null) operator[] throws; a value that is not a string throws.
        Dim id = Jsoncpp.AsCString(Jsoncpp.Miembro(item, "id", ok), ok)
        If Not ok Then Return False
        Dim nuevo = False
        Dim tpl As LmSkinTemplate = Nothing
        For Each existing In sink
            If String.Equals(existing.Id, id, StringComparison.Ordinal) Then tpl = existing : Exit For
        Next
        If tpl Is Nothing Then
            ' DisplayName starts as the id: the app's combo shows it when the file gives no name (f4ee's name starts empty).
            tpl = New LmSkinTemplate With {.Id = id, .DisplayName = id}
            sink.Add(tpl)
            nuevo = True
        End If
        If Jsoncpp.EsMiembro(item, "name", ok) Then
            Dim nombre = Jsoncpp.AsCString(Jsoncpp.Miembro(item, "name", ok), ok)
            If Not ok Then Return nuevo
            tpl.DisplayName = nombre
        End If
        If Jsoncpp.EsMiembro(item, "gender", ok) Then
            Dim g = Jsoncpp.AsUInt(Jsoncpp.Miembro(item, "gender", ok), ok)
            If Not ok Then Return nuevo
            tpl.Gender = CByte(g And &HFFUI)                                  ' UInt8 gender (SkinInterface.h:41)
        End If
        For Each campo In {(Clave:="maleFace", Firma:="TXST", Poner:=Sub(f As UInteger) tpl.FaceTxstFormID(0) = f),
                           (Clave:="femaleFace", Firma:="TXST", Poner:=Sub(f As UInteger) tpl.FaceTxstFormID(1) = f),
                           (Clave:="maleHead", Firma:="HDPT", Poner:=Sub(f As UInteger) tpl.HeadHdptFormID(0) = f),
                           (Clave:="femaleHead", Firma:="HDPT", Poner:=Sub(f As UInteger) tpl.HeadHdptFormID(1) = f),
                           (Clave:="maleHeadRear", Firma:="HDPT", Poner:=Sub(f As UInteger) tpl.HeadRearHdptFormID(0) = f),
                           (Clave:="femaleHeadRear", Firma:="HDPT", Poner:=Sub(f As UInteger) tpl.HeadRearHdptFormID(1) = f),
                           (Clave:="skin", Firma:="ARMO", Poner:=Sub(f As UInteger) tpl.SkinArmoFormID = f)}
            Dim v = Jsoncpp.Miembro(item, campo.Clave, ok)
            If v Is Nothing OrElse v.Tipo = Jsoncpp.JsonTipo.Nulo Then Continue For   ' isMember && !isNull
            Dim identificador = Jsoncpp.AsString(v, ok)
            If Not ok Then Return nuevo
            ' GetFormFromIdentifier (Utilities.cpp:133-155) and the DYNAMIC_CAST to the field's type: a form of another type is not set.
            Dim fid = LooksmenuLoader.ResolveFormIdentifier(identificador, pluginManager)
            If fid = 0UI Then Continue For
            Dim rec = pluginManager.GetRecord(fid)
            If rec Is Nothing OrElse rec.Header.Signature <> campo.Firma Then Continue For
            campo.Poner(fid)
        Next
        If Jsoncpp.EsMiembro(item, "sort", ok) Then
            Dim s = Jsoncpp.AsInt(Jsoncpp.Miembro(item, "sort", ok), ok)
            If Not ok Then Return nuevo
            tpl.Sort = s
        End If
        Return nuevo
    End Function

End Module
