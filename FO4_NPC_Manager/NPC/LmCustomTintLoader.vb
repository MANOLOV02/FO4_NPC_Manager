Imports System.IO
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>
''' Loads LooksMenu CUSTOM face-tint templates from
''' <c>Data\F4SE\Plugins\F4EE\Tints\&lt;pluginFileName&gt;\categories.json</c> + <c>templates.json</c>
''' and merges them into a parsed <see cref="RACE_Data"/>'s tint template groups, so tints an NPC
''' applies against a mod-added template (stored in the LM preset as a "Tints"/"TintOrder" index that
''' has no vanilla RACE option) resolve to real textures/colours and compose + bake like any other.
'''
''' Faithful to f4ee (F4SEPlugins-master):
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
''' Additive &amp; idempotent: with no <c>Tints\</c> files (or none matching a given race) EnsureMerged is
''' a strict no-op over today's behaviour. Merges once per <see cref="RACE_Data"/> instance
''' (guarded by <see cref="RACE_Data.CustomLmTintsMerged"/>); the disk scan runs once per session until
''' <see cref="Invalidate"/> is called on a load-order reparse.
''' </summary>
Public Module LmCustomTintLoader

    ' Gender buckets. LooksMenu "Gender": 0=male, 1=female, 2=both (CharGenTint::ForeEachGender uses
    ' race->chargenData[gender]; FO4 chargenData[0]=male, [1]=female).
    Private Class GenderCustomTints
        Public Groups As New List(Of RACE_TintTemplateGroup)
        Public ByName As New Dictionary(Of String, RACE_TintTemplateGroup)(StringComparer.OrdinalIgnoreCase)
    End Class

    Private Class RaceCustomTints
        Public Male As New GenderCustomTints
        Public Female As New GenderCustomTints
    End Class

    ' name → engine slot value (CharGenTint.cpp:23-48 g_slotMap). Kept explicit so LM's singular "Scar"
    ' maps to our TintSlot.Scars=21; unknown → FaceDetail=22 (CharGenTint.cpp:72).
    Private ReadOnly _slotMap As New Dictionary(Of String, UShort)(StringComparer.OrdinalIgnoreCase) From {
        {"ForeheadMask", 0US}, {"EyesMask", 1US}, {"NoseMask", 2US}, {"EarsMask", 3US},
        {"CheeksMask", 4US}, {"MouthMask", 5US}, {"NeckMask", 6US}, {"LipColor", 7US},
        {"CheekColor", 8US}, {"Eyeliner", 9US}, {"EyeSocketUpper", 10US}, {"EyeSocketLower", 11US},
        {"SkinTone", 12US}, {"Paint", 13US}, {"LaughLines", 14US}, {"CheekColorLower", 15US},
        {"Nose", 16US}, {"Chin", 17US}, {"Neck", 18US}, {"Forehead", 19US},
        {"Dirt", 20US}, {"Scar", 21US}, {"FaceDetail", 22US}, {"Brows", 23US}}

    ' name → engine blend-op value (CharGenTint.cpp:51-58 g_blendMap + BGSCharacterTint enum). Unknown →
    ' Default(0)=Normal. These are the SAME values the compositor's blend map consumes.
    Private ReadOnly _blendMap As New Dictionary(Of String, UInteger)(StringComparer.OrdinalIgnoreCase) From {
        {"Default", 0UI}, {"Multiply", 1UI}, {"Overlay", 2UI}, {"SoftLight", 3UI}, {"HardLight", 4UI}}

    ' name → engine flag bit (CharGenTint.cpp:16-21 g_flagMap + BGSCharacterTint flags).
    Private ReadOnly _flagMap As New Dictionary(Of String, UShort)(StringComparer.OrdinalIgnoreCase) From {
        {"OnOff", 1US}, {"ChargenDetail", 2US}, {"TakesSkinTone", 4US}}

    Private ReadOnly _lock As New Object()
    Private _loaded As Boolean = False
    Private _index As Dictionary(Of String, RaceCustomTints) = Nothing   ' key = race EditorID

    ''' <summary>Drop the cached disk scan so the next EnsureMerged re-reads Tints\. Call on a
    ''' load-order reparse (when the RACE cache is also cleared).</summary>
    Public Sub Invalidate()
        SyncLock _lock
            _loaded = False
            _index = Nothing
        End SyncLock
    End Sub

    ''' <summary>Idempotently append this race's LooksMenu custom tint templates into its tint groups.
    ''' No-op when the race has no custom tints, when it was already merged, or when there are no
    ''' Tints\ files on disk. Safe to call from multiple threads on the shared cached instance.
    ''' App overload — resolves the Data\ path from the global <see cref="Config_App"/>.</summary>
    Public Sub EnsureMerged(race As RACE_Data, pluginManager As PluginManager)
        EnsureMerged(race, pluginManager, Config_App.Current?.DataPath)
    End Sub

    ''' <summary>Explicit-<paramref name="dataPath"/> overload — used by the headless FO4_FaceTint_CLI,
    ''' which threads its own Data\ path (honouring the <c>--data</c> flag) instead of relying on the
    ''' app-only <see cref="Config_App"/> global, which the CLI does not populate the same way.</summary>
    Public Sub EnsureMerged(race As RACE_Data, pluginManager As PluginManager, dataPath As String)
        If race Is Nothing OrElse pluginManager Is Nothing Then Return
        If race.CustomLmTintsMerged Then Return

        EnsureLoaded(pluginManager, dataPath)   ' guarded internally; does file IO OUTSIDE the per-race lock

        SyncLock race
            If race.CustomLmTintsMerged Then Return
            Dim rt As RaceCustomTints = Nothing
            If _index IsNot Nothing AndAlso Not String.IsNullOrEmpty(race.EditorID) Then
                _index.TryGetValue(race.EditorID, rt)
            End If
            If rt IsNot Nothing Then
                MergeGender(race.FemaleTintTemplateGroups, rt.Female)
                MergeGender(race.MaleTintTemplateGroups, rt.Male)
            End If
            race.CustomLmTintsMerged = True
        End SyncLock
    End Sub

    ''' <summary>Append custom groups into a gender's race group list. New categories are added as new
    ''' groups; options whose category name matches an existing (vanilla or already-added) group are
    ''' appended into it — mirroring the engine adding template Entries to an existing category vs
    ''' Pushing a new one (CharGenTint.cpp:236-371). Options whose Index already exists in the gender are
    ''' skipped (the engine treats a duplicate index as "modify existing"; we keep the vanilla one).</summary>
    Private Sub MergeGender(raceGroups As List(Of RACE_TintTemplateGroup), custom As GenderCustomTints)
        If custom Is Nothing OrElse custom.Groups.Count = 0 Then Return

        Dim existingIndices As New HashSet(Of UShort)
        For Each g In raceGroups
            For Each o In g.Options
                existingIndices.Add(o.Index)
            Next
        Next

        For Each cg In custom.Groups
            Dim target As RACE_TintTemplateGroup = Nothing
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
                        ' Pass 1 — categories.json (all plugins, load order). categoryId → name per race.
                        Dim catNames As New Dictionary(Of String, Dictionary(Of UInteger, String))(StringComparer.OrdinalIgnoreCase)
                        For Each plugin In pluginManager.Plugins
                            Dim cp = Path.Combine(baseDir, plugin.FileName, "categories.json")
                            If File.Exists(cp) Then LoadCategoriesFile(cp, catNames)
                        Next
                        ' Pass 2 — templates.json (all plugins, load order).
                        For Each plugin In pluginManager.Plugins
                            Dim tp = Path.Combine(baseDir, plugin.FileName, "templates.json")
                            If File.Exists(tp) Then LoadTemplatesFile(tp, plugin.FileName, pluginManager, catNames, idx)
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

    Private ReadOnly _jsonOpts As New JsonDocumentOptions With {
        .CommentHandling = JsonCommentHandling.Skip,
        .AllowTrailingCommas = True}

    ''' <summary>Parse a categories.json: array of { Race, Entries:[{ Type:"Category", Gender, Id, Name }] }.
    ''' Records categoryId → name per race so templates that reference a category by numeric id can be
    ''' bucketed into the right (named) group. Mirrors CharGenInterface::LoadTintCategories.</summary>
    Private Sub LoadCategoriesFile(path As String, catNames As Dictionary(Of String, Dictionary(Of UInteger, String)))
        Try
            Using doc = JsonDocument.Parse(File.ReadAllText(path), _jsonOpts)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return
                For Each item In doc.RootElement.EnumerateArray()
                    Dim raceName = GetStr(item, "Race")
                    If String.IsNullOrEmpty(raceName) Then Continue For
                    Dim entries As JsonElement
                    If Not item.TryGetProperty("Entries", entries) OrElse entries.ValueKind <> JsonValueKind.Array Then Continue For
                    Dim map As Dictionary(Of UInteger, String) = Nothing
                    If Not catNames.TryGetValue(raceName, map) Then
                        map = New Dictionary(Of UInteger, String)()
                        catNames(raceName) = map
                    End If
                    For Each entry In entries.EnumerateArray()
                        If Not String.Equals(GetStr(entry, "Type"), "Category", StringComparison.OrdinalIgnoreCase) Then Continue For
                        Dim id = GetUInt(entry, "Id")
                        Dim nm = GetStr(entry, "Name")
                        If Not String.IsNullOrEmpty(nm) Then map(id) = nm
                    Next
                Next
            End Using
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-TINT] categories.json parse failed '{path}': {ex.Message}")
        End Try
    End Sub

    ''' <summary>Parse a templates.json: array of { Race, Entries:[{ Type, Gender, Id, Slot, Flags,
    ''' BlendOp, Category|FixedCategory, + Mask:Texture / Palette:Texture+Colors / TextureSet:Diffuse/
    ''' Normal/Specular }] } into custom groups keyed by (race, gender, categoryName). Mirrors
    ''' CharGenInterface::LoadTintTemplates + CharGenTint::*::Parse.</summary>
    Private Sub LoadTemplatesFile(path As String, pluginFileName As String, pluginManager As PluginManager,
                                  catNames As Dictionary(Of String, Dictionary(Of UInteger, String)),
                                  idx As Dictionary(Of String, RaceCustomTints))
        Try
            Using doc = JsonDocument.Parse(File.ReadAllText(path), _jsonOpts)
                If doc.RootElement.ValueKind <> JsonValueKind.Array Then Return
                For Each item In doc.RootElement.EnumerateArray()
                    Dim raceName = GetStr(item, "Race")
                    If String.IsNullOrEmpty(raceName) Then Continue For
                    Dim entries As JsonElement
                    If Not item.TryGetProperty("Entries", entries) OrElse entries.ValueKind <> JsonValueKind.Array Then Continue For

                    For Each entry In entries.EnumerateArray()
                        Dim typeStr = GetStr(entry, "Type")
                        Dim opt = BuildOption(entry, typeStr, pluginManager)
                        If opt Is Nothing Then Continue For

                        ' Resolve the owning category NAME: FixedCategory (name) wins; else Category id
                        ' looked up in categories.json for this race. No resolution → drop (engine drops
                        ' a template whose category is absent, CharGenTint.cpp:345 guards on tintData).
                        Dim catName = GetStr(entry, "FixedCategory")
                        If String.IsNullOrEmpty(catName) Then
                            Dim catId As UInteger = 0UI
                            Dim hasCat As Boolean = TryGetUInt(entry, "Category", catId)
                            Dim raceMap As Dictionary(Of UInteger, String) = Nothing
                            If hasCat AndAlso catNames.TryGetValue(raceName, raceMap) Then
                                raceMap.TryGetValue(catId, catName)
                            End If
                        End If
                        If String.IsNullOrEmpty(catName) Then
                            Logger.LogLazy(Function() $"[LM-TINT] '{pluginFileName}' template Id={opt.Index} has no resolvable category — skipped")
                            Continue For
                        End If

                        Dim gender = GetUInt(entry, "Gender")
                        Dim rct As RaceCustomTints = Nothing
                        If Not idx.TryGetValue(raceName, rct) Then
                            rct = New RaceCustomTints()
                            idx(raceName) = rct
                        End If
                        If gender = 0UI OrElse gender = 2UI Then AddOptionToGender(rct.Male, catName, opt)
                        If gender = 1UI OrElse gender = 2UI Then AddOptionToGender(rct.Female, catName, opt)
                    Next
                Next
            End Using
        Catch ex As Exception
            Logger.LogLazy(Function() $"[LM-TINT] templates.json parse failed '{path}': {ex.Message}")
        End Try
    End Sub

    Private Sub AddOptionToGender(g As GenderCustomTints, catName As String, opt As RACE_TintTemplateOption)
        Dim grp As RACE_TintTemplateGroup = Nothing
        If Not g.ByName.TryGetValue(catName, grp) Then
            grp = New RACE_TintTemplateGroup With {.GroupName = catName, .IsCustomLm = True}
            g.ByName(catName) = grp
            g.Groups.Add(grp)
        End If
        ' A given Id appears once per gender in a well-formed mod; guard anyway.
        If Not grp.Options.Any(Function(o) o.Index = opt.Index) Then grp.Options.Add(CloneOption(opt))
    End Sub

    ''' <summary>Build one RACE_TintTemplateOption from a template entry. Type drives which fields are
    ''' read + the DeclaredEntryType override (so a Diffuse-only TextureSet is not mis-read as a Mask).
    ''' Returns Nothing for a malformed entry (no Id, no usable texture).</summary>
    Private Function BuildOption(entry As JsonElement, typeStr As String, pluginManager As PluginManager) As RACE_TintTemplateOption
        Dim id As UInteger = 0UI
        If Not TryGetUInt(entry, "Id", id) Then Return Nothing

        Dim opt As New RACE_TintTemplateOption With {
            .Index = CUShort(id And &HFFFFUI),
            .Name = GetStr(entry, "Name"),
            .Slot = ParseSlot(GetStr(entry, "Slot")),
            .Flags = ParseFlags(entry),
            .IsCustomLm = True}

        Dim blendStr = GetStr(entry, "BlendOp")
        If Not String.IsNullOrEmpty(blendStr) Then
            opt.BlendOperation = ParseBlend(blendStr)
            opt.HasBlendOperation = True
        End If

        Dim defVal As Single
        If TryGetSingle(entry, "Default", defVal) Then
            opt.DefaultValue = defVal
            opt.HasDefaultValue = True
        End If

        If String.Equals(typeStr, "Palette", StringComparison.OrdinalIgnoreCase) Then
            opt.DeclaredEntryType = RACE_TintEntryType.Palette
            Dim tex = GetStr(entry, "Texture")
            If Not String.IsNullOrEmpty(tex) Then opt.Textures.Add(tex)
            Dim colors As JsonElement
            If entry.TryGetProperty("Colors", colors) AndAlso colors.ValueKind = JsonValueKind.Array Then
                For Each c In colors.EnumerateArray()
                    Dim cd As New RACE_TintTemplateColor With {
                        .TemplateIndex = CUShort(GetUInt(c, "Id") And &HFFFFUI),
                        .Alpha = GetSingle(c, "Alpha"),
                        .ColorFormID = LooksmenuLoader.ResolveFormIdentifier(GetStr(c, "Form"), pluginManager)}
                    Dim cBlend = GetStr(c, "BlendOp")
                    cd.BlendOperation = If(String.IsNullOrEmpty(cBlend), 0UI, ParseBlend(cBlend))
                    opt.TemplateColors.Add(cd)
                Next
            End If
            If opt.Textures.Count = 0 Then Return Nothing

        ElseIf String.Equals(typeStr, "TextureSet", StringComparison.OrdinalIgnoreCase) Then
            opt.DeclaredEntryType = RACE_TintEntryType.TextureSet
            ' Diffuse always; Normal/Specular optional (many custom sets ship diffuse-only).
            Dim d = GetStr(entry, "Diffuse")
            If String.IsNullOrEmpty(d) Then Return Nothing
            opt.Textures.Add(d)
            opt.Textures.Add(GetStr(entry, "Normal"))     ' "" placeholder keeps triple layout; loader skips empties
            opt.Textures.Add(GetStr(entry, "Specular"))
            TrimTrailingEmptyTextures(opt.Textures)

        Else
            ' Mask (default): single grayscale texture tinted by the applied TEND colour.
            opt.DeclaredEntryType = RACE_TintEntryType.Mask
            Dim tex = GetStr(entry, "Texture")
            If String.IsNullOrEmpty(tex) Then Return Nothing
            opt.Textures.Add(tex)
        End If

        Return opt
    End Function

    ''' <summary>Drop trailing "" texture slots (absent Normal/Specular) but keep a real one that has a
    ''' later real one after it (never happens in practice, but keeps indices honest).</summary>
    Private Sub TrimTrailingEmptyTextures(tex As List(Of String))
        While tex.Count > 1 AndAlso String.IsNullOrEmpty(tex(tex.Count - 1))
            tex.RemoveAt(tex.Count - 1)
        End While
    End Sub

    Private Function CloneOption(o As RACE_TintTemplateOption) As RACE_TintTemplateOption
        Dim c As New RACE_TintTemplateOption With {
            .Slot = o.Slot, .Index = o.Index, .Name = o.Name, .Flags = o.Flags,
            .BlendOperation = o.BlendOperation, .HasBlendOperation = o.HasBlendOperation,
            .DefaultValue = o.DefaultValue, .HasDefaultValue = o.HasDefaultValue,
            .DeclaredEntryType = o.DeclaredEntryType, .IsCustomLm = o.IsCustomLm}
        c.Textures.AddRange(o.Textures)
        For Each tc In o.TemplateColors
            c.TemplateColors.Add(New RACE_TintTemplateColor With {
                .ColorFormID = tc.ColorFormID, .Alpha = tc.Alpha,
                .TemplateIndex = tc.TemplateIndex, .BlendOperation = tc.BlendOperation})
        Next
        Return c
    End Function

    Private Function ParseSlot(name As String) As UShort
        If String.IsNullOrEmpty(name) Then Return 22US   ' FaceDetail default (CharGenTint.cpp:72)
        Dim v As UShort
        If _slotMap.TryGetValue(name, v) Then Return v
        Return 22US
    End Function

    Private Function ParseBlend(name As String) As UInteger
        Dim v As UInteger
        If _blendMap.TryGetValue(name, v) Then Return v
        Return 0UI
    End Function

    Private Function ParseFlags(entry As JsonElement) As UShort
        Dim flags As UShort = 0US
        Dim arr As JsonElement
        If entry.TryGetProperty("Flags", arr) AndAlso arr.ValueKind = JsonValueKind.Array Then
            For Each f In arr.EnumerateArray()
                If f.ValueKind = JsonValueKind.String Then
                    Dim bit As UShort
                    If _flagMap.TryGetValue(f.GetString(), bit) Then flags = flags Or bit
                End If
            Next
        End If
        Return flags
    End Function

    ' --- small JSON helpers (System.Text.Json) ---

    Private Function GetStr(e As JsonElement, name As String) As String
        Dim p As JsonElement
        If e.ValueKind = JsonValueKind.Object AndAlso e.TryGetProperty(name, p) AndAlso p.ValueKind = JsonValueKind.String Then
            Return p.GetString()
        End If
        Return ""
    End Function

    Private Function TryGetUInt(e As JsonElement, name As String, ByRef value As UInteger) As Boolean
        value = 0UI
        Dim p As JsonElement
        If e.ValueKind = JsonValueKind.Object AndAlso e.TryGetProperty(name, p) AndAlso p.ValueKind = JsonValueKind.Number Then
            Dim l As Long
            If p.TryGetInt64(l) AndAlso l >= 0 AndAlso l <= UInteger.MaxValue Then
                value = CUInt(l)
                Return True
            End If
        End If
        Return False
    End Function

    Private Function GetUInt(e As JsonElement, name As String) As UInteger
        Dim v As UInteger
        TryGetUInt(e, name, v)
        Return v
    End Function

    Private Function TryGetSingle(e As JsonElement, name As String, ByRef value As Single) As Boolean
        value = 0.0F
        Dim p As JsonElement
        If e.ValueKind = JsonValueKind.Object AndAlso e.TryGetProperty(name, p) AndAlso p.ValueKind = JsonValueKind.Number Then
            Dim d As Double
            If p.TryGetDouble(d) Then
                value = CSng(d)
                Return True
            End If
        End If
        Return False
    End Function

    Private Function GetSingle(e As JsonElement, name As String) As Single
        Dim v As Single
        TryGetSingle(e, name, v)
        Return v
    End Function

End Module
