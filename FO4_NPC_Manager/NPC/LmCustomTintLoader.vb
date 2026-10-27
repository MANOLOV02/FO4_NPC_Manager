Imports System.IO
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>
''' Loads LooksMenu CUSTOM face-tint templates from
''' <c>Data\F4SE\Plugins\F4EE\Tints\&lt;pluginFileName&gt;\categories.json</c> + <c>templates.json</c>
''' and superimposes them on the ones the RACE declares, so tints an NPC
''' applies against a mod-added template (stored in the LM preset as a "Tints"/"TintOrder" index that
''' has no vanilla RACE option) resolve to real textures/colours and compose + bake like any other.
'''
''' Faithful to f4ee (Script extenders, Racemenu y Looksmenu/F4SEPlugins):
'''  - Disk layout + two-pass load order = <c>CharGenInterface::LoadTintTemplateMods</c>
'''    (CharGenInterface.cpp:647-660): ALL <c>categories.json</c> first (every plugin, load order),
'''    THEN all <c>templates.json</c>, because a template can reference (by <c>Category</c> id or
'''    <c>FixedCategory</c> name) a category defined by an earlier plugin. No <c>Loose\</c> folder —
'''    the engine's Tints loader has none (unlike Overlays/Skin).
'''  - Category / template model + field names = <c>CharGenTint.cpp</c> (Parse/Apply). Slot, BlendOp and
'''    Flags carry the SAME enum values the engine uses (CharGenTint.cpp:16-58), so the existing FaceTint
'''    pipeline composes them with zero special-casing. LooksMenu itself defines NO face-tint compositing:
'''    it only injects the template (with the mod author's authored BlendOp) into
'''    <c>race-&gt;chargenData[gender]-&gt;tintData</c>; the engine's chargen tint baker does the blending.
'''  - Injection = append (mirrors the engine <c>Push()</c>), so custom templates land at the END of the
'''    physical tint order — which the compositor already honours via its PHYS-desc ordering. No new
'''    ordering rule and no change to the vanilla ordering.
'''
''' Additive &amp; idempotent: with no <c>Tints\</c> files (or none matching a given race) Fusionar
''' returns the base list untouched. La lista fusionada vive APARTE del record — el record nunca
''' se muta — y se cachea por (EditorID, género) para no rearmarla en cada consulta; la caché se limpia
''' junto con el índice de disco en <see cref="Invalidate"/> (load-order reparse).
'''
''' <para>Estas plantillas NO tienen record detrás: no hay dónde colgarlas en el árbol. Por eso lo que
''' arma este cargador es el modelo del COMPOSITOR (<see cref="GrupoDeTinteEfectivo"/>), no un modelo del
''' formato — el que declara el tipo es el que puede tener entradas que ningún ESP declara. La superposición
''' es una OPERACIÓN: la mitad de fábrica sale del record por <c>TintesEfectivos.TintesDelRecord</c>, ésta
''' sale del .json, y las dos terminan en el mismo modelo, marcadas por <c>EsDeLooksMenu</c>.</para>
''' </summary>
Public Module LmCustomTintLoader

    ' Gender buckets. LooksMenu "Gender": 0=male, 1=female, 2=both (CharGenTint::ForeEachGender uses
    ' race->chargenData[gender]; FO4 chargenData[0]=male, [1]=female).
    Private Class GenderCustomTints
        Public Groups As New List(Of GrupoDeTinteEfectivo)
        Public ByName As New Dictionary(Of String, GrupoDeTinteEfectivo)(StringComparer.OrdinalIgnoreCase)
    End Class

    Private Class RaceCustomTints
        Public Male As New GenderCustomTints
        Public Female As New GenderCustomTints
    End Class

    ' name → engine slot value (CharGenTint.cpp:23-48 g_slotMap). Kept explicit so LM's singular "Scar"
    ' maps to our TintSlot.Scars=21; unknown → FaceDetail=22 (CharGenTint.cpp:72).
    ' std::map<std::string, UInt32>::find (CharGenTint.cpp:66 / :93 / :120): exact, case-sensitive.
    Private ReadOnly _slotMap As New Dictionary(Of String, UShort)(StringComparer.Ordinal) From {
        {"ForeheadMask", 0US}, {"EyesMask", 1US}, {"NoseMask", 2US}, {"EarsMask", 3US},
        {"CheeksMask", 4US}, {"MouthMask", 5US}, {"NeckMask", 6US}, {"LipColor", 7US},
        {"CheekColor", 8US}, {"Eyeliner", 9US}, {"EyeSocketUpper", 10US}, {"EyeSocketLower", 11US},
        {"SkinTone", 12US}, {"Paint", 13US}, {"LaughLines", 14US}, {"CheekColorLower", 15US},
        {"Nose", 16US}, {"Chin", 17US}, {"Neck", 18US}, {"Forehead", 19US},
        {"Dirt", 20US}, {"Scar", 21US}, {"FaceDetail", 22US}, {"Brows", 23US}}

    ' name → engine blend-op value (CharGenTint.cpp:51-58 g_blendMap + BGSCharacterTint enum). Unknown →
    ' Default(0)=Normal. These are the SAME values the compositor's blend map consumes.
    Private ReadOnly _blendMap As New Dictionary(Of String, UInteger)(StringComparer.Ordinal) From {
        {"Default", 0UI}, {"Multiply", 1UI}, {"Overlay", 2UI}, {"SoftLight", 3UI}, {"HardLight", 4UI}}

    ' name → engine flag bit (CharGenTint.cpp:16-21 g_flagMap + BGSCharacterTint flags).
    Private ReadOnly _flagMap As New Dictionary(Of String, UShort)(StringComparer.Ordinal) From {
        {"OnOff", 1US}, {"ChargenDetail", 2US}, {"TakesSkinTone", 4US}}

    Private ReadOnly _lock As New Object()
    Private _loaded As Boolean = False
    Private _index As Dictionary(Of String, RaceCustomTints) = Nothing   ' key = race EditorID

    ''' <summary>Resultado YA fusionado, cacheado por "EditorID|género". Reemplaza al latch de estado
    ''' interno de antes: lo que se cachea ahora es el RESULTADO (una lista aparte),
    ''' nunca una marca sobre el record — el record no se vuelve a tocar.</summary>
    Private ReadOnly _cacheLock As New Object()
    Private ReadOnly _cache As New Dictionary(Of String, List(Of GrupoDeTinteEfectivo))(StringComparer.OrdinalIgnoreCase)

    ''' <summary>Drop the cached disk scan AND the cache de listas fusionadas, así la próxima consulta
    ''' vuelve a leer Tints\ y a rearmar. Call on a load-order reparse (when the RACE cache is also
    ''' cleared).</summary>
    Public Sub Invalidate()
        SyncLock _lock
            _loaded = False
            _index = Nothing
        End SyncLock
        SyncLock _cacheLock
            _cache.Clear()
        End SyncLock
    End Sub

    ''' <summary>Fusiona los grupos de tinte de esta raza+género (los que trae el record) con los tints
    ''' custom de LooksMenu de esa misma raza+género. NO muta <paramref name="race"/>: devuelve una lista
    ''' aparte, cacheada por (EditorID, género) para no rearmarla en cada consulta. Safe to call from
    ''' multiple threads. App overload — resolves the Data\ path from the global
    ''' <see cref="Config_App"/>.</summary>
    Public Function Fusionar(race As Canon.IRace, isFemale As Boolean, pluginManager As PluginManager) As List(Of GrupoDeTinteEfectivo)
        Return Fusionar(race, isFemale, pluginManager, Config_App.Current?.DataPath)
    End Function

    ''' <summary>Explicit-<paramref name="dataPath"/> overload — used by the headless FO4_FaceTint_CLI,
    ''' which threads its own Data\ path (honouring the <c>--data</c> flag) instead of relying on the
    ''' app-only <see cref="Config_App"/> global, which the CLI does not populate the same way.</summary>
    Public Function Fusionar(race As Canon.IRace, isFemale As Boolean, pluginManager As PluginManager,
                             dataPath As String) As List(Of GrupoDeTinteEfectivo)
        If race Is Nothing Then Return New List(Of GrupoDeTinteEfectivo)
        ' Tint Layers son exclusivos de Fallout 4 — Skyrim no los declara en RACE.
        Dim raceFo4 = TryCast(race, Canon.RaceFO4)
        Dim baseGroups = raceFo4.TintesDelRecord(isFemale)
        Return Fusionar(baseGroups, race.EditorID, isFemale, pluginManager, dataPath)
    End Function

    ''' <summary>Versión de lista pelada: fusiona GRUPOS (los que trae el record, armados por
    ''' <c>CanonInterpretacion.TintesDelRecord</c>) con los tints custom de LooksMenu de
    ''' <paramref name="editorId"/>+género. App overload.</summary>
    Public Function Fusionar(grupos As List(Of GrupoDeTinteEfectivo), editorId As String, isFemale As Boolean,
                             pluginManager As PluginManager) As List(Of GrupoDeTinteEfectivo)
        Return Fusionar(grupos, editorId, isFemale, pluginManager, Config_App.Current?.DataPath)
    End Function

    ''' <summary>Fusiona <paramref name="grupos"/> (los que trae el record) con los tints custom de
    ''' LooksMenu de <paramref name="editorId"/>+género y cachea el resultado por (editorId, género):
    ''' la fusión corre una sola vez por raza, no en cada consulta. Sin plugin manager o sin EditorID no
    ''' hay con qué buscar en el índice de disco — se devuelve <paramref name="grupos"/> tal cual.</summary>
    Public Function Fusionar(grupos As List(Of GrupoDeTinteEfectivo), editorId As String, isFemale As Boolean,
                             pluginManager As PluginManager, dataPath As String) As List(Of GrupoDeTinteEfectivo)
        Dim baseGroups = If(grupos, New List(Of GrupoDeTinteEfectivo))
        If pluginManager Is Nothing OrElse String.IsNullOrEmpty(editorId) Then Return baseGroups

        Dim clave = editorId & "|" & isFemale.ToString()
        SyncLock _cacheLock
            Dim cacheado As List(Of GrupoDeTinteEfectivo) = Nothing
            If _cache.TryGetValue(clave, cacheado) Then Return cacheado
        End SyncLock

        EnsureLoaded(pluginManager, dataPath)   ' guarded internally; does file IO OUTSIDE the cache lock

        Dim rt As RaceCustomTints = Nothing
        If _index IsNot Nothing Then _index.TryGetValue(editorId, rt)

        Dim salida = ClonarGrupos(baseGroups)
        If rt IsNot Nothing Then MergeGender(salida, If(isFemale, rt.Female, rt.Male))

        SyncLock _cacheLock
            _cache(clave) = salida
        End SyncLock
        Return salida
    End Function

    ''' <summary>Copia la lista y cada grupo (no las Options: MergeGender sólo AGREGA elementos, nunca
    ''' edita uno existente, así que compartir las Options no es un problema). Evita que superponer dos
    ''' veces la misma raza pise la lista base que salió del record.</summary>
    Private Function ClonarGrupos(grupos As List(Of GrupoDeTinteEfectivo)) As List(Of GrupoDeTinteEfectivo)
        Dim salida As New List(Of GrupoDeTinteEfectivo)
        For Each g In grupos
            Dim copia As New GrupoDeTinteEfectivo With {
                .GroupName = g.GroupName, .CategoryIndex = g.CategoryIndex}
            copia.Options.AddRange(g.Options)
            salida.Add(copia)
        Next
        Return salida
    End Function

    ''' <summary>Append custom groups into a gender's race group list. New categories are added as new
    ''' groups; options whose category name matches an existing (vanilla or already-added) group are
    ''' appended into it — mirroring the engine adding template Entries to an existing category vs
    ''' Pushing a new one (CharGenTint.cpp:236-371). Options whose Index already exists in the gender are
    ''' skipped (the engine treats a duplicate index as "modify existing"; we keep the vanilla one).</summary>
    Private Sub MergeGender(raceGroups As List(Of GrupoDeTinteEfectivo), custom As GenderCustomTints)
        If custom Is Nothing OrElse custom.Groups.Count = 0 Then Return

        Dim existingIndices As New HashSet(Of UShort)
        For Each g In raceGroups
            For Each o In g.Options
                existingIndices.Add(o.Index)
            Next
        Next

        For Each cg In custom.Groups
            Dim target As GrupoDeTinteEfectivo = Nothing
            If Not String.IsNullOrEmpty(cg.GroupName) Then
                For Each g In raceGroups
                    If String.Equals(g.GroupName, cg.GroupName, StringComparison.OrdinalIgnoreCase) Then
                        target = g
                        Exit For
                    End If
                Next
            End If

            If target Is Nothing Then
                ' New category: append the whole custom group.
                raceGroups.Add(cg)
                For Each o In cg.Options
                    existingIndices.Add(o.Index)
                Next
            Else
                ' Existing category (vanilla or already added): append only new-index options into it.
                For Each o In cg.Options
                    If existingIndices.Add(o.Index) Then target.Options.Add(o)
                Next
            End If
        Next
    End Sub

    Private Sub EnsureLoaded(pluginManager As PluginManager, dataPath As String)
        If _loaded Then Return
        SyncLock _lock
            If _loaded Then Return

            Dim idx As New Dictionary(Of String, RaceCustomTints)(StringComparer.OrdinalIgnoreCase)
            Try
                If Not String.IsNullOrEmpty(dataPath) Then
                    Dim baseDir = Path.Combine(dataPath, "F4SE", "Plugins", "F4EE", "Tints")
                    If Directory.Exists(baseDir) Then
                        ' GetRaceByName (Utilities.cpp:169-185): a race by EditorID, case-insensitive (F4EEFixedString).
                        Dim razas = LmHairColorLutLoader.BuildRaceEditorIdIndex(pluginManager)
                        ' Pass 1 — categories.json (all plugins, load order). categoryId → name per race.
                        Dim catNames As New Dictionary(Of String, Dictionary(Of UInteger, String))(StringComparer.OrdinalIgnoreCase)
                        For Each plugin In pluginManager.Plugins
                            Dim cp = Path.Combine(baseDir, plugin.FileName, "categories.json")
                            If File.Exists(cp) Then LoadCategoriesFile(cp, razas, catNames)
                        Next
                        ' Pass 2 — templates.json (all plugins, load order).
                        For Each plugin In pluginManager.Plugins
                            Dim tp = Path.Combine(baseDir, plugin.FileName, "templates.json")
                            If File.Exists(tp) Then LoadTemplatesFile(tp, plugin.FileName, pluginManager, razas, catNames, idx)
                        Next
                    End If
                End If
            Catch ex As Exception
                Logger.LogLazy(Function() $"[LM-TINT] EnsureLoaded failed: {ex.Message}")
            End Try

            _index = idx
            _loaded = True
        End SyncLock
    End Sub

    ''' <summary>A Tints file's bytes (BSReadAll) through f4ee's Json::Reader (Jsoncpp.LeerF4ee). Nothing when it does not parse: f4ee
    ''' returns without loading anything of the file (CharGenInterface.cpp:701-703 / :758-760) - a trailing comma, a BOM.</summary>
    Private Function LeerArchivoDeTintes(path As String) As Jsoncpp.JsonValor
        Try
            Dim transcodificado As Boolean, motivo As String = Nothing
            Dim raiz = Jsoncpp.LeerF4ee(Jsoncpp.BytesComoLosVeJsoncpp(File.ReadAllBytes(path), transcodificado), motivo)
            If raiz Is Nothing Then Logger.LogLazy(Function() $"[LM-TINT] '{path}': Json::Reader rejects it ({motivo}); f4ee loads nothing of the file.")
            Return raiz
        Catch ex As Exception
            Dim msg = ex.Message
            Logger.LogLazy(Function() $"[LM-TINT] '{path}' could not be read: {msg}")
            Return Nothing
        End Try
    End Function

    ''' <summary>_strnicmp(text, prefix, Len(prefix)) = 0 (CharGenInterface.cpp:731 / :792 / :801 / :810): the first Len(prefix)
    ''' characters equal, ignoring ASCII case ("Masks" is a Mask).</summary>
    Private Function EmpiezaCon(texto As String, prefijo As String) As Boolean
        Return texto.Length >= prefijo.Length AndAlso String.Compare(texto, 0, prefijo, 0, prefijo.Length, StringComparison.OrdinalIgnoreCase) = 0
    End Function

    ''' <summary>The head of every entry of both files (CharGenInterface.cpp:712-726 / :775-789): entry["Type"].asString(),
    ''' entry["Gender"].asUInt() into a UInt8, entry["Id"].asUInt(), and a gender outside 0..2 throws. False = a throw: f4ee's ONE try
    ''' around the whole traversal (:704/:738, :767/:825) ends the file there - what was applied before stays.</summary>
    Private Function CabeceraDeEntrada(entry As Jsoncpp.JsonValor, ByRef tipo As String, ByRef genero As UInteger, ByRef id As UInteger) As Boolean
        Dim ok As Boolean
        tipo = Jsoncpp.AsString(Jsoncpp.Miembro(entry, "Type", ok), ok) : If Not ok Then Return False
        genero = Jsoncpp.AsUInt(Jsoncpp.Miembro(entry, "Gender", ok), ok) And &HFFUI : If Not ok Then Return False
        id = Jsoncpp.AsUInt(Jsoncpp.Miembro(entry, "Id", ok), ok) : If Not ok Then Return False
        Return genero <= 2UI                                   ' throw std::exception("Invalid gender specified")
    End Function

    ''' <summary>A categories.json as CharGenInterface::LoadTintCategories (:686-743) reads it: for each item (the root's elements or
    ''' values) its "Race" (asString; a race the load order does not have is skipped without reading its entries), then each of its
    ''' "Entries"; a "Category" entry registers its Id under its "Name" (CharGenTintObject::Parse, CharGenTint.cpp:270-285: Name
    ''' asString when a member - a throw there drops only that category; Apply needs a name for an id the race lacks, :245-251).
    ''' Records categoryId → name per race so templates that reference a category by numeric id can be bucketed into the right
    ''' (named) group.</summary>
    Private Sub LoadCategoriesFile(path As String, razas As Dictionary(Of String, Boolean()), catNames As Dictionary(Of String, Dictionary(Of UInteger, String)))
        Dim raiz = LeerArchivoDeTintes(path)
        If raiz Is Nothing Then Return
        Dim ok As Boolean
        For Each item In Jsoncpp.Valores(raiz)
            Dim raceName = Jsoncpp.AsString(Jsoncpp.Miembro(item, "Race", ok), ok)
            If Not ok Then Return
            If Not razas.ContainsKey(raceName) Then Continue For
            Dim entries = Jsoncpp.Miembro(item, "Entries", ok)
            If Not ok Then Return
            Dim map As Dictionary(Of UInteger, String) = Nothing
            If Not catNames.TryGetValue(raceName, map) Then
                map = New Dictionary(Of UInteger, String)()
                catNames(raceName) = map
            End If
            For Each entry In Jsoncpp.Valores(entries)
                Dim tipo As String = Nothing, genero, id As UInteger
                If Not CabeceraDeEntrada(entry, tipo, genero, id) Then Return
                If Not EmpiezaCon(tipo, "Category") Then Continue For
                Dim nombre = ""
                If Jsoncpp.EsMiembro(entry, "Name", ok) Then
                    nombre = Jsoncpp.AsString(Jsoncpp.Miembro(entry, "Name", ok), ok)
                    If Not ok Then Continue For
                End If
                If Not String.IsNullOrEmpty(nombre) Then map(id) = nombre
            Next
        Next
    End Sub

    ''' <summary>A templates.json as CharGenInterface::LoadTintTemplates (:745-830) reads it: the same head as categories.json, then by
    ''' "Type" prefix a Mask (4), a Palette (7) or a TextureSet (10) - any other type is not loaded - parsed as CharGenTint.cpp parses
    ''' it (<see cref="BuildOption"/>), into custom groups keyed by (race, gender, categoryName). The category, as Apply looks it up
    ''' (CharGenTint.cpp:330-334): by id when "Category" is not 0, else by the "FixedCategory" name.</summary>
    Private Sub LoadTemplatesFile(path As String, pluginFileName As String, pluginManager As PluginManager,
                                  razas As Dictionary(Of String, Boolean()),
                                  catNames As Dictionary(Of String, Dictionary(Of UInteger, String)),
                                  idx As Dictionary(Of String, RaceCustomTints))
        Dim raiz = LeerArchivoDeTintes(path)
        If raiz Is Nothing Then Return
        Dim ok As Boolean
        For Each item In Jsoncpp.Valores(raiz)
            Dim raceName = Jsoncpp.AsString(Jsoncpp.Miembro(item, "Race", ok), ok)
            If Not ok Then Return
            If Not razas.ContainsKey(raceName) Then Continue For
            Dim entries = Jsoncpp.Miembro(item, "Entries", ok)
            If Not ok Then Return
            For Each entry In Jsoncpp.Valores(entries)
                Dim typeStr As String = Nothing, gender, id As UInteger
                If Not CabeceraDeEntrada(entry, typeStr, gender, id) Then Return
                Dim clase As ClaseDeTinte
                If EmpiezaCon(typeStr, "Mask") Then
                    clase = ClaseDeTinte.Mask
                ElseIf EmpiezaCon(typeStr, "Palette") Then
                    clase = ClaseDeTinte.Palette
                ElseIf EmpiezaCon(typeStr, "TextureSet") Then
                    clase = ClaseDeTinte.TextureSet
                Else
                    Continue For
                End If
                Dim categoryId As UInteger = 0UI, fixedCategory As String = ""
                Dim opt = BuildOption(entry, clase, id, pluginManager, categoryId, fixedCategory)
                If opt Is Nothing Then Continue For

                Dim catName As String = Nothing
                If categoryId <> 0UI Then
                    Dim raceMap As Dictionary(Of UInteger, String) = Nothing
                    If catNames.TryGetValue(raceName, raceMap) Then raceMap.TryGetValue(categoryId, catName)
                Else
                    catName = fixedCategory
                End If
                If String.IsNullOrEmpty(catName) Then
                    Logger.LogLazy(Function() $"[LM-TINT] '{pluginFileName}' template Id={opt.Index} has no resolvable category — skipped")
                    Continue For
                End If

                Dim rct As RaceCustomTints = Nothing
                If Not idx.TryGetValue(raceName, rct) Then
                    rct = New RaceCustomTints()
                    idx(raceName) = rct
                End If
                If gender = 0UI OrElse gender = 2UI Then AddOptionToGender(rct.Male, catName, opt)
                If gender = 1UI OrElse gender = 2UI Then AddOptionToGender(rct.Female, catName, opt)
            Next
        Next
    End Sub

    Private Sub AddOptionToGender(g As GenderCustomTints, catName As String, opt As OpcionDeTinteEfectiva)
        Dim grp As GrupoDeTinteEfectivo = Nothing
        If Not g.ByName.TryGetValue(catName, grp) Then
            grp = New GrupoDeTinteEfectivo With {.GroupName = catName}
            g.ByName(catName) = grp
            g.Groups.Add(grp)
        End If
        ' A given Id appears once per gender in a well-formed mod; guard anyway.
        If Not grp.Options.Any(Function(o) o.Index = opt.Index) Then grp.Options.Add(CloneOption(opt))
    End Sub

    ''' <summary>One template entry as CharGenTint.cpp parses it, into the compositor's option. The base fields
    ''' (CharGenTintObject::Parse :270-285 and CharGenTintBase::Parse :287-317): Name, Category (asUInt), FixedCategory, Slot (asString
    ''' through the slot map; unknown → FaceDetail, :62-75) and Flags (each element asString through the flag map) - each Parse in
    ''' its own try: a throw ends THAT Parse, the fields set before stay, and the class's Parse runs anyway (it ignores
    ''' __super's result). The class's own fields: Mask (:375-397) Texture and BlendOp; Palette (:428-461) Texture and the Colors
    ''' (Id asUInt, Form asString → a CLFM or the colour is dropped, Alpha asFloat, BlendOp); TextureSet (:503-541) Diffuse,
    ''' Normal, Specular, BlendOp and Default (asFloat). A throw in the class's Parse makes it return false: the template is not
    ''' applied (Nothing). Nothing too when the option has no texture to draw (the app's model: an entry without one is not
    ''' composed).</summary>
    Private Function BuildOption(entry As Jsoncpp.JsonValor, clase As ClaseDeTinte, id As UInteger, pluginManager As PluginManager,
                                 ByRef categoryId As UInteger, ByRef fixedCategory As String) As OpcionDeTinteEfectiva
        Dim ok As Boolean
        Dim opt As New OpcionDeTinteEfectiva With {.Index = CUShort(id And &HFFFFUI), .Name = "", .Slot = 22US, .EntryType = clase, .EsDeLooksMenu = True}

        ' CharGenTintObject::Parse: Name.
        Dim nombre As String = ""
        If Cadena(entry, "Name", nombre) Then opt.Name = nombre
        ' CharGenTintBase::Parse: Category, FixedCategory, Slot, Flags - in this order, a throw ends it.
        Do
            If Jsoncpp.EsMiembro(entry, "Category", ok) Then
                categoryId = Jsoncpp.AsUInt(Jsoncpp.Miembro(entry, "Category", ok), ok)
                If Not ok Then Exit Do
            End If
            If Not Cadena(entry, "FixedCategory", fixedCategory) Then Exit Do
            Dim slot As String = Nothing
            If Jsoncpp.EsMiembro(entry, "Slot", ok) Then
                If Not Cadena(entry, "Slot", slot) Then Exit Do
                opt.Slot = ParseSlot(slot)
            End If
            If Jsoncpp.EsMiembro(entry, "Flags", ok) Then
                Dim flags As UShort = 0US
                Dim lanzo = False
                For Each f In Jsoncpp.Valores(Jsoncpp.Miembro(entry, "Flags", ok))
                    Dim s = Jsoncpp.AsString(f, ok)
                    If Not ok Then lanzo = True : Exit For
                    Dim bit As UShort
                    If _flagMap.TryGetValue(s, bit) Then flags = flags Or bit
                Next
                If lanzo Then Exit Do
                opt.Flags = flags
            End If
        Loop While False

        ' The class's Parse.
        Dim blend As String = Nothing
        Select Case clase
            Case ClaseDeTinte.Mask
                Dim tex As String = Nothing
                If Not Cadena(entry, "Texture", tex) Then Return Nothing
                If Not Cadena(entry, "BlendOp", blend) Then Return Nothing
                If blend IsNot Nothing Then opt.BlendOperation = ParseBlend(blend) : opt.HasBlendOperation = True
                If String.IsNullOrEmpty(tex) Then Return Nothing
                opt.Textures.Add(tex)
            Case ClaseDeTinte.Palette
                Dim tex As String = Nothing
                If Not Cadena(entry, "Texture", tex) Then Return Nothing
                ' auto entries = entry["Colors"] (:436) on a const Value: a non-object entry throws; each colour's conversions in the same try.
                Dim colores = Jsoncpp.Miembro(entry, "Colors", ok)
                If Not ok Then Return Nothing
                For Each c In Jsoncpp.Valores(colores)
                    Dim cId = Jsoncpp.AsUInt(Jsoncpp.Miembro(c, "Id", ok), ok) : If Not ok Then Return Nothing
                    Dim forma = Jsoncpp.AsString(Jsoncpp.Miembro(c, "Form", ok), ok) : If Not ok Then Return Nothing
                    Dim alfa = Jsoncpp.AsFloat(Jsoncpp.Miembro(c, "Alpha", ok), Jsoncpp.Dialecto.F4eeSkee64, ok) : If Not ok Then Return Nothing
                    Dim cBlend As UInteger = 0UI
                    If Jsoncpp.EsMiembro(c, "BlendOp", ok) Then
                        Dim b = Jsoncpp.AsString(Jsoncpp.Miembro(c, "BlendOp", ok), ok) : If Not ok Then Return Nothing
                        cBlend = ParseBlend(b)
                    End If
                    ' DYNAMIC_CAST(GetFormFromIdentifier(...), TESForm, BGSColorForm): only a colour form is kept (:452-455).
                    Dim fid = LooksmenuLoader.ResolveFormIdentifier(forma, pluginManager)
                    Dim rec = If(fid = 0UI, Nothing, pluginManager.GetRecord(fid))
                    If rec Is Nothing OrElse rec.Header.Signature <> "CLFM" Then Continue For
                    opt.TemplateColors.Add(New ColorDeTinteEfectivo With {
                        .TemplateIndex = CUShort(cId And &HFFFFUI), .Alpha = alfa, .ColorFormID = fid, .BlendOperation = cBlend})
                Next
                If String.IsNullOrEmpty(tex) Then Return Nothing
                opt.Textures.Add(tex)
            Case ClaseDeTinte.TextureSet
                Dim d As String = Nothing, n As String = Nothing, s As String = Nothing
                If Not Cadena(entry, "Diffuse", d) Then Return Nothing
                If Not Cadena(entry, "Normal", n) Then Return Nothing
                If Not Cadena(entry, "Specular", s) Then Return Nothing
                If Not Cadena(entry, "BlendOp", blend) Then Return Nothing
                If blend IsNot Nothing Then opt.BlendOperation = ParseBlend(blend) : opt.HasBlendOperation = True
                If Jsoncpp.EsMiembro(entry, "Default", ok) Then
                    Dim dv = Jsoncpp.AsFloat(Jsoncpp.Miembro(entry, "Default", ok), Jsoncpp.Dialecto.F4eeSkee64, ok)
                    If Not ok Then Return Nothing
                    opt.DefaultValue = dv : opt.HasDefaultValue = True
                End If
                ' Diffuse always; Normal/Specular optional (many custom sets ship diffuse-only).
                If String.IsNullOrEmpty(d) Then Return Nothing
                opt.Textures.Add(d)
                opt.Textures.Add(If(n, ""))     ' "" placeholder keeps triple layout; loader skips empties
                opt.Textures.Add(If(s, ""))
                TrimTrailingEmptyTextures(opt.Textures)
        End Select
        Return opt
    End Function

    ''' <summary>Drop trailing "" texture slots (absent Normal/Specular) but keep a real one that has a
    ''' later real one after it (never happens in practice, but keeps indices honest).</summary>
    Private Sub TrimTrailingEmptyTextures(tex As List(Of String))
        While tex.Count > 1 AndAlso String.IsNullOrEmpty(tex(tex.Count - 1))
            tex.RemoveAt(tex.Count - 1)
        End While
    End Sub

    Private Function CloneOption(o As OpcionDeTinteEfectiva) As OpcionDeTinteEfectiva
        Dim c As New OpcionDeTinteEfectiva With {
            .Slot = o.Slot, .Index = o.Index, .Name = o.Name, .Flags = o.Flags,
            .BlendOperation = o.BlendOperation, .HasBlendOperation = o.HasBlendOperation,
            .DefaultValue = o.DefaultValue, .HasDefaultValue = o.HasDefaultValue,
            .EntryType = o.EntryType, .EsDeLooksMenu = o.EsDeLooksMenu}
        c.Textures.AddRange(o.Textures)
        For Each tc In o.TemplateColors
            c.TemplateColors.Add(New ColorDeTinteEfectivo With {
                .ColorFormID = tc.ColorFormID, .Alpha = tc.Alpha,
                .TemplateIndex = tc.TemplateIndex, .BlendOperation = tc.BlendOperation})
        Next
        Return c
    End Function

    ''' <summary>if(entry.isMember(key)) value = entry[key].asString() - CharGenTint.cpp's pattern for every string field. False = asString
    ''' threw (an array or an object); <paramref name="valor"/> untouched when the key is not a member.</summary>
    Private Function Cadena(entry As Jsoncpp.JsonValor, clave As String, ByRef valor As String) As Boolean
        Dim ok As Boolean
        If Not Jsoncpp.EsMiembro(entry, clave, ok) Then Return True
        valor = Jsoncpp.AsString(Jsoncpp.Miembro(entry, clave, ok), ok)
        Return ok
    End Function

    Private Function ParseSlot(name As String) As UShort
        Dim v As UShort
        If name IsNot Nothing AndAlso _slotMap.TryGetValue(name, v) Then Return v
        Return 22US   ' FaceDetail (CharGenTint.cpp:74)
    End Function

    Private Function ParseBlend(name As String) As UInteger
        Dim v As UInteger
        If name IsNot Nothing AndAlso _blendMap.TryGetValue(name, v) Then Return v
        Return 0UI    ' Default (CharGenTint.cpp:101)
    End Function

End Module
