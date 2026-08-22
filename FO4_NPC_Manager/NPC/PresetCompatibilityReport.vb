Imports System.IO
Imports System.Text
Imports FO4_Base_Library
Imports FO4_Base_Library.Canon.CanonInterpretacion

''' <summary>Exhaustive "what of this preset will NOT apply to this NPC" audit, shown by the preset browser
''' (<see cref="LooksmenuLoad_Form"/>) behind the "Show incompatible" button.
'''
''' <para>The dialog's info label only says THAT something is missing; this module says WHAT — every head part
''' whose plugin isn't loaded or whose race gate rejects it, every tint layer index the RACE doesn't declare,
''' every unresolved FormID (hair colour, head TXST, skin ARMO, outfits), every RaceMenu slider/overlay/mask
''' texture that isn't installed, and every field we knowingly don't apply. Each finding carries the CONCRETE
''' reason and the consequence, so the user can decide with the evidence at hand instead of a bare count.</para>
'''
''' <para>Every rule replicated here is the one the render/bake path already obeys, cited per section:
''' head parts → <see cref="HeadPartResolver.IsHdptValidForRace"/> (engine RNAM/FLST + race-default gate);
''' FO4 tints → <c>CanonInterpretacion.BuscarOpcion</c> sobre la lista fusionada (the compositor skips a
''' layer whose index doesn't resolve);
''' SSE tints → <see cref="SseFaceTintComposer.GetRaceLayersOrdered"/> (the composer iterates RACE layers, so
''' an authored index the RACE doesn't declare is never visited); FO4 chargen morphs → RACE MSID/MPPI defs
''' (<see cref="NpcMorphResolver"/>); SSE custom morphs → <see cref="NpcMorphResolver.SliderCatalog"/>.</para>
'''
''' <para>Never mutates the preset: it only parses records and probes the files dictionary. Los tints
''' custom de LooksMenu se leen vía <see cref="LmCustomTintLoader.Fusionar"/>, que arma una lista aparte
''' del RACE (cacheada por raza+género, no muta el record) — sin esto un preset usando tints custom de
''' LooksMenu se reportaría como roto cuando no lo está. Safe to call on every list selection; the
''' browser caches the result per preset.</para></summary>
Public Module PresetCompatibilityReport

    ''' <summary>Severity/nature of one finding. Drives grouping and the leading glyph in the text report.</summary>
    Public Enum PresetIssueKind
        ''' <summary>The owning plugin isn't in the active load order — the reference can't even be resolved.</summary>
        MissingMaster
        ''' <summary>The FormID resolves to nothing (or to the wrong record type) in the current load order.</summary>
        MissingRecord
        ''' <summary>Resolved fine, but this NPC's RACE rejects it — the engine drops it silently.</summary>
        RaceIncompatible
        ''' <summary>A file the entry points at (texture, …) isn't present in loose files or the archives.</summary>
        MissingAsset
        ''' <summary>Carried by the preset, knowingly not applied by this app (or by the engine here).</summary>
        NotApplied
        ''' <summary>Informational: applies, but with a caveat worth seeing.</summary>
        Note
    End Enum

    ''' <summary>One finding: a category, a one-line title, and the concrete reason/consequence.</summary>
    Public Class PresetIssue
        Public Kind As PresetIssueKind
        Public Category As String = ""
        Public Title As String = ""
        Public Detail As String = ""
        Public Sub New(kind As PresetIssueKind, category As String, title As String, Optional detail As String = "")
            Me.Kind = kind
            Me.Category = category
            Me.Title = title
            Me.Detail = If(detail, "")
        End Sub
    End Class

    ''' <summary>Result of <see cref="Build"/>: the findings plus the counters the label needs.</summary>
    Public Class PresetAuditReport
        Public ReadOnly Issues As New List(Of PresetIssue)
        ''' <summary>Per-category "resolved OK" tallies, shown as the closing summary so an empty findings
        ''' list reads as "checked and clean" rather than "nothing was checked".</summary>
        Public ReadOnly Resolved As New List(Of String)
        ''' <summary>Header lines (preset path, race, gender, mode caveats).</summary>
        Public ReadOnly Header As New List(Of String)

        ''' <summary>Findings that mean content will NOT reach the NPC (everything but Note).</summary>
        Public ReadOnly Property MissingCount As Integer
            Get
                Dim n = 0
                For Each i In Issues
                    If i.Kind <> PresetIssueKind.Note Then n += 1
                Next
                Return n
            End Get
        End Property

        Public ReadOnly Property Count As Integer
            Get
                Return Issues.Count
            End Get
        End Property

        ''' <summary>True when at least one finding is a real loss (not just a note).</summary>
        Public ReadOnly Property HasMissing As Boolean
            Get
                Return MissingCount > 0
            End Get
        End Property
    End Class

    ''' <summary>Everything the audit needs. The browser fills it once per dialog (fixed race/gender/NPC) and
    ''' swaps only <see cref="Preset"/> per list selection.</summary>
    Public Class PresetAuditContext
        Public Preset As LooksmenuLoader.LooksmenuPreset
        Public IsSse As Boolean
        Public PluginManager As PluginManager
        Public DataPath As String = ""
        Public RaceFormID As UInteger
        Public Race As Canon.IRace
        Public RaceDisplayName As String = ""
        Public IsFemale As Boolean
        Public RaceDefaults As HashSet(Of UInteger)
        Public FlstCache As Dictionary(Of UInteger, Canon.IFlst)
        Public NpcHasBodyTri As Boolean = True
        ''' <summary>Ids of the F4SE overlay templates loaded for this NPC's gender (FO4). Nothing = the caller
        ''' couldn't supply the catalog, so overlay ids are reported as "not checked" instead of "missing".</summary>
        Public KnownOverlayTemplateIds As HashSet(Of String)
        ''' <summary>Ids of the F4SE LM skin templates loaded for this NPC's gender (FO4). Same Nothing rule.</summary>
        Public KnownLmSkinTemplateIds As HashSet(Of String)
    End Class

    Private ReadOnly HeadPartTypeNames As String() =
        {"Misc", "Face", "Eyes", "Hair", "Facial hair", "Scar", "Eyebrows", "Meatcaps", "Teeth", "Head rear"}

    ''' <summary>Audit one preset against one NPC. Never throws on bad data — an entry we can't parse becomes a
    ''' finding, not an exception.</summary>
    Public Function Build(ctx As PresetAuditContext) As PresetAuditReport
        Dim r As New PresetAuditReport
        If ctx Is Nothing OrElse ctx.Preset Is Nothing Then Return r
        Dim p = ctx.Preset
        Dim pm = ctx.PluginManager
        ' IsHdptValidForRace indexes the FLST cache unconditionally — never hand it Nothing.
        If ctx.FlstCache Is Nothing Then ctx.FlstCache = New Dictionary(Of UInteger, Canon.IFlst)

        r.Header.Add("Preset : " & DisplaySourcePath(p.SourcePath, ctx.DataPath))
        r.Header.Add($"NPC    : race {If(String.IsNullOrEmpty(ctx.RaceDisplayName), $"0x{ctx.RaceFormID:X8}", ctx.RaceDisplayName)} (0x{ctx.RaceFormID:X8})  •  {If(ctx.IsFemale, "Female", "Male")}")
        r.Header.Add("Engine : " & If(ctx.IsSse, "Skyrim SE (RaceMenu .jslot)", "Fallout 4 (LooksMenu .json)"))
        ' Audited object = the FILE's own content in BOTH games (the browser hands over the file-only view; see
        ' LooksmenuLoad_Form.FileView). So every finding below is about this preset, never about what the NPC
        ' already carries.

        AuditMissingMasters(ctx, r)
        AuditHeadParts(ctx, r)
        AuditFaceTints(ctx, r)
        AuditHairColor(ctx, r)
        AuditFormIdFields(ctx, r)
        AuditFaceMorphs(ctx, r)
        AuditBodySliders(ctx, r)
        AuditOverlays(ctx, r)
        AuditLmSkinTemplate(ctx, r)
        AuditSculptAndTransforms(ctx, r)

        Return r
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' 1) Missing masters — head-part identifiers whose plugin isn't in the load order (or is, but no
    '    longer carries that FormID). This is the single most common cause of "the hair didn't apply".
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditMissingMasters(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        ' Both games funnel into the same diagnostic string list ("Plugin.esp|HEX"); SSE additionally keeps the
        ' verbatim jslot entry so a load→save round-trip re-emits it (LooksmenuPreset.SseUnresolvedHeadParts).
        Dim ids As New List(Of String)
        If p.UnresolvedHeadParts IsNot Nothing Then ids.AddRange(p.UnresolvedHeadParts)
        If ids.Count = 0 AndAlso ctx.IsSse AndAlso p.SseUnresolvedHeadParts IsNot Nothing Then
            For Each hp In p.SseUnresolvedHeadParts
                If hp Is Nothing Then Continue For
                ids.Add(If(String.IsNullOrEmpty(hp.FormIdentifier), $"?|{hp.FormId:X6}", hp.FormIdentifier))
            Next
        End If
        If ids.Count = 0 Then Return

        ' Group by plugin so the user sees "install/enable THIS mod", not 9 separate lines.
        Dim byPlugin As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        For Each id In ids
            Dim pipeIdx = If(id Is Nothing, -1, id.IndexOf("|"c))
            Dim plug = If(pipeIdx > 0, id.Substring(0, pipeIdx).Trim(), "(unknown plugin)")
            Dim hex = If(pipeIdx > 0 AndAlso pipeIdx < id.Length - 1, id.Substring(pipeIdx + 1).Trim(), id)
            If Not byPlugin.ContainsKey(plug) Then byPlugin(plug) = New List(Of String)
            byPlugin(plug).Add(hex)
        Next

        For Each kv In byPlugin
            Dim loaded = IsPluginLoaded(ctx.PluginManager, kv.Key)
            Dim title = $"{kv.Value.Count} head part(s) from '{kv.Key}'"
            Dim detail As String
            If Not loaded Then
                detail = $"'{kv.Key}' is not in the active load order — install/enable it, or these parts (FormIDs {String.Join(", ", kv.Value)}) simply won't apply."
            Else
                ' Plugin present but the identifier didn't resolve: the FormID isn't in it any more (different
                ' version of the mod), or the JSON hex is malformed.
                detail = $"'{kv.Key}' IS loaded but FormID(s) {String.Join(", ", kv.Value)} don't exist in it — the preset was made with a different version of that mod."
            End If
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingMaster, "Masters / head parts", title, detail))
        Next
    End Sub

    Private Function IsPluginLoaded(pm As PluginManager, fileName As String) As Boolean
        If pm Is Nothing OrElse pm.Plugins Is Nothing OrElse String.IsNullOrEmpty(fileName) Then Return False
        For Each pl In pm.Plugins
            If String.Equals(pl.FileName, fileName, StringComparison.OrdinalIgnoreCase) Then Return True
        Next
        Return False
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' 2) Head parts that DID resolve — record present? valid for this race?
    '    Same gate the browser's "Show only race-compatible" checkbox and the render use
    '    (HeadPartResolver.IsHdptValidForRace): HDPT.RNAM=0 + humanoid race, or RNAM's FLST lists the
    '    race (incl. the RaceCompatibility runtime insertion), or the RACE declares it as a default.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditHeadParts(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If p.HeadPartFormIDs Is Nothing OrElse p.HeadPartFormIDs.Count = 0 Then
            If p.HasHeadPartFormIDs Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Head parts", "The preset declares an EMPTY head-part list",
                                       "That is an authoritative wipe, not an absence: applying it leaves the NPC with only its RACE defaults."))
            End If
            Return
        End If

        Dim pm = ctx.PluginManager
        Dim noRaceInfo As Boolean = (ctx.RaceFormID = 0UI OrElse ctx.Race Is Nothing)
        If noRaceInfo Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Head parts", "Race gate not checked",
                                   "This dialog didn't receive the NPC's RACE record, so head parts are only checked for existence, not for race validity."))
        End If
        Dim okCount As Integer = 0
        For Each fid In p.HeadPartFormIDs
            If fid = 0UI Then Continue For
            Dim rec = pm?.GetRecord(fid)
            If rec Is Nothing Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Head parts", $"HDPT 0x{fid:X8} not found",
                                       "No record with this FormID in the current load order — the part is dropped."))
                Continue For
            End If
            If rec.Header.Signature <> "HDPT" Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Head parts", $"0x{fid:X8} is a {rec.Header.Signature}, not a HDPT",
                                       "The FormID resolves to a different record type — the part is dropped."))
                Continue For
            End If

            Dim hd = Canon.CanonRecords.Hdpt(rec, pm)
            Dim label = DescribeHdpt(fid, hd, pm)
            If noRaceInfo Then
                ' Existence is all we can assert here — the note above says so.
                okCount += 1
                Continue For
            End If

            If HeadPartResolver.IsHdptValidForRace(fid, ctx.RaceFormID, ctx.IsFemale, pm, ctx.FlstCache, ctx.RaceDefaults) Then
                okCount += 1
                Continue For
            End If

            ' Concrete reason, mirroring the three pass-paths of IsHdptValidForRace.
            Dim reason As String
            If hd Is Nothing Then
                reason = "the HDPT record couldn't be parsed."
            ElseIf hd.ValidRaces = 0UI Then
                reason = "it declares no Valid Races (RNAM=0) and this RACE declares no head parts at all (non-humanoid race), so the engine drops it."
            Else
                reason = $"its Valid Races list (FLST 0x{hd.ValidRaces:X8}) doesn't include this race, and this RACE doesn't declare it as a gender default."
            End If
            Dim consequence As String = ""
            If hd IsNot Nothing AndAlso hd.TipoDeParte() = 1 Then
                consequence = "  ⚠ This is the BASE HEAD (Face): applying this preset would leave the NPC with NO head mesh."
            End If
            r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Head parts", $"{label} is not valid for this race",
                                   reason & consequence))
        Next

        If okCount > 0 Then r.Resolved.Add($"{okCount} head part(s) resolve and pass the race gate")
    End Sub

    Private Function DescribeHdpt(fid As UInteger, hd As Canon.IHdpt, pm As PluginManager) As String
        Dim name As String = ""
        Dim typeName As String = ""
        If hd IsNot Nothing Then
            name = If(Not String.IsNullOrEmpty(hd.EditorID), hd.EditorID, hd.Name)
            If hd.TipoDeParte() >= 0 AndAlso hd.TipoDeParte() < HeadPartTypeNames.Length Then
                typeName = HeadPartTypeNames(hd.TipoDeParte())
            ElseIf hd.TipoDeParte() >= 0 Then
                typeName = $"type {hd.TipoDeParte()}"
            End If
        End If
        If String.IsNullOrEmpty(name) Then name = $"0x{fid:X8}"
        Dim origin As String = ""
        Try
            If pm IsNot Nothing Then origin = pm.GetOriginatingPluginName(fid)
        Catch
        End Try
        Dim sb As New StringBuilder(name)
        If typeName.Length > 0 Then sb.Append($" [{typeName}]")
        sb.Append($" (0x{fid:X8}")
        If Not String.IsNullOrEmpty(origin) Then sb.Append(" — " & origin)
        sb.Append(")"c)
        Return sb.ToString()
    End Function

    ' ---------------------------------------------------------------------------------------------
    ' 3) Face tints. FO4: a layer whose TETI index isn't among the RACE's tint template options is
    '    carried verbatim but INERT (the compositor's FindTintOption returns Nothing and skips it, and
    '    the Face editor hides the row). SSE: the composer walks the RACE's tint layers, so an authored
    '    TINI index the RACE doesn't declare is never visited.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditFaceTints(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If ctx.IsSse Then
            AuditSseTints(ctx, r)
            Return
        End If
        If p.FaceTintLayers Is Nothing OrElse p.FaceTintLayers.Count = 0 Then
            If p.HasFaceTintLayers Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Face tints", "The preset declares an EMPTY tint list",
                                       "Authoritative wipe: applying it removes every tint layer this NPC has."))
            End If
            Return
        End If
        If ctx.Race Is Nothing Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Face tints", $"{p.FaceTintLayers.Count} layer(s) not checked",
                                   "No RACE record was supplied to this dialog, so tint indices couldn't be validated."))
            Return
        End If

        ' LooksMenu CUSTOM tint templates (Data\F4SE\Plugins\F4EE\Tints\...) se funden con las Options del
        ' RACE en una lista aparte; sin esto un preset que usa un tint custom se reportaria como roto
        ' cuando no lo esta.
        Dim tintGroups As List(Of GrupoDeTinteEfectivo) = Nothing
        Try
            tintGroups = LmCustomTintLoader.Fusionar(ctx.Race, ctx.IsFemale, ctx.PluginManager, ctx.DataPath)
        Catch
            tintGroups = New List(Of GrupoDeTinteEfectivo)
        End Try

        Dim okCount As Integer = 0
        For Each tl In p.FaceTintLayers
            If tl Is Nothing Then Continue For
            Dim opt = tintGroups.BuscarOpcion(tl.Index)
            If opt IsNot Nothing Then
                okCount += 1
                Continue For
            End If
            r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Face tints", $"Tint layer index {tl.Index} isn't declared by this race",
                                   $"value {tl.Value}, colour {ColorText(tl)}. The layer is preserved verbatim (it round-trips on Save) but is INERT: the compositor skips it and the Face editor hides its row. Typical cause: the preset was made on another race, or with a LooksMenu custom tint pack that isn't installed."))
        Next
        If okCount > 0 Then r.Resolved.Add($"{okCount} tint layer(s) resolve against this race")
    End Sub

    Private Function ColorText(tl As LooksmenuLoader.CapaDeTintePreset) As String
        If tl.Color.IsEmpty Then Return "(texture-set layer)"
        Return $"#{tl.Color.R:X2}{tl.Color.G:X2}{tl.Color.B:X2}"
    End Function

    Private Sub AuditSseTints(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If Not p.HasSseTints OrElse p.SseTintLayers Is Nothing OrElse p.SseTintLayers.Count = 0 Then Return
        If ctx.RaceFormID = 0UI OrElse ctx.PluginManager Is Nothing Then Return

        ' The RACE's declared layer indices, in the same order the composer uses.
        Dim raceIdx As New HashSet(Of Integer)
        Try
            For Each lay In SseFaceTintComposer.GetRaceLayersOrdered(ctx.PluginManager, ctx.RaceFormID, ctx.IsFemale)
                raceIdx.Add(lay.Index)
            Next
        Catch
        End Try
        If raceIdx.Count = 0 Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Face tints", "This race declares no tint layers",
                                   "Nothing the preset carries in the tint category can be composed onto this NPC."))
            Return
        End If

        Dim okCount As Integer = 0
        For Each sr In p.SseTintLayers
            If sr Is Nothing OrElse Not sr.Indice.HasValue Then Continue For
            Dim idx As Integer = CInt(sr.Indice.Value)
            If raceIdx.Contains(idx) Then
                okCount += 1
            Else
                r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Face tints", $"Authored tint layer index {idx} isn't declared by this race",
                                       "The composer iterates the RACE's own layers, so this layer is never painted (it is still carried and saved). Usually a preset authored on a different race, or one that needs a mod adding extra tint layers."))
            End If
        Next
        If okCount > 0 Then r.Resolved.Add($"{okCount} tint layer(s) match a layer this race declares")

        ' RaceMenu per-layer CUSTOM mask textures: the index must exist AND the .dds must be installed.
        If p.SseTintTexOverride IsNot Nothing Then
            For Each kv In p.SseTintTexOverride
                If String.IsNullOrWhiteSpace(kv.Value) Then Continue For
                If Not raceIdx.Contains(kv.Key) Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Face tints", $"Custom mask texture for layer {kv.Key} targets a layer this race doesn't declare",
                                           kv.Value))
                ElseIf Not SseCatalogs.TextureResolves(kv.Value) Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.MissingAsset, "Face tints", $"Custom mask texture for layer {kv.Key} isn't installed",
                                           $"'{kv.Value}' is not present in loose files or the archives — the layer falls back to the RACE's own mask."))
                End If
            Next
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 4) Hair colour (CLFM / RaceMenu RGB).
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditHairColor(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset

        ' EL CASO FRECUENTE, y el que faltaba: el identificador NO resolvió porque el mod que trae el color
        ' no está instalado. El loader deja HairColorFormID=0, y el `Return` de abajo lo hacía indistinguible
        ' de "el preset no declara color de pelo" ⇒ cero hallazgos. La rama MissingRecord de más abajo sólo
        ' cubre el caso raro (el plugin SÍ está pero el form no existe).
        If p.HairColorFormID = 0UI AndAlso Not String.IsNullOrWhiteSpace(p.UnresolvedHairColor) Then
            ' NO asumir la causa. ResolveFormIdentifier devuelve 0 por TRES motivos distintos y cada uno
            ' necesita otra acción del usuario: identificador mal formado (el mod lo escribió mal), parte hex
            ' ilegible, o plugin ausente. Decir siempre "el plugin no está instalado" mandaba a buscar un mod
            ' que en dos de los tres casos YA está — y con un identificador sin '|' el mensaje llegaba a
            ' nombrar como plugin al identificador entero.
            Dim ident = p.UnresolvedHairColor.Trim()
            Dim bar = ident.IndexOf("|"c)
            If bar <= 0 OrElse bar >= ident.Length - 1 Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Hair colour",
                                       $"Malformed hair colour identifier '{ident}'",
                                       "LooksMenu writes ""Plugin.esp|FORMID""; this value doesn't have that shape, so no colour can be resolved from it — the NPC keeps its current hair colour. The preset file is at fault, not your load order."))
                Return
            End If
            Dim plug = ident.Substring(0, bar).Trim()
            Dim hexPart = ident.Substring(bar + 1).Trim()
            Dim pluginLoaded = ctx.PluginManager IsNot Nothing AndAlso
                               ctx.PluginManager.Plugins.Any(Function(pl) String.Equals(pl.FileName, plug, StringComparison.OrdinalIgnoreCase))
            If Not pluginLoaded Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingMaster, "Hair colour",
                                       $"'{ident}' isn't installed",
                                       $"The plugin '{plug}' isn't in the load order, so the colour can't be resolved — the NPC keeps its current hair colour. Install that mod, or pick a colour by hand after applying the preset."))
            Else
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Hair colour",
                                       $"'{ident}' doesn't resolve",
                                       $"'{plug}' IS loaded, but '{hexPart}' isn't a FormID it provides (or isn't valid hex) — the NPC keeps its current hair colour. Usually means the preset was made against a different version of that mod."))
            End If
            Return
        End If

        If p.HairColorFormID = 0UI Then Return
        Dim rec = ctx.PluginManager?.GetRecord(p.HairColorFormID)
        If rec Is Nothing Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Hair colour", $"CLFM 0x{p.HairColorFormID:X8} not found",
                                   "The colour record isn't in the load order (its mod isn't installed) — the NPC keeps its current hair colour."))
            Return
        End If
        If rec.Header.Signature <> "CLFM" Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, "Hair colour", $"0x{p.HairColorFormID:X8} is a {rec.Header.Signature}, not a CLFM",
                                   "The hair colour won't resolve."))
            Return
        End If
        ' A CLFM outside the RACE's own AHCM/AHCF list is NOT reported: the engine applies the NPC's HCLF
        ' regardless of the race palette, so it is neither missing nor incompatible — and this report only
        ' carries findings the user has to act on.

        ' El color resuelve, pero si es de PALETA (FNAM 0x2 RemappingIndex, sólo FO4) el resultado depende de
        ' una TEXTURA: la LUT del RACE, o la que le ate un LUTs\<plugin>\haircolors.json de LooksMenu. Si esa
        ' textura no está instalada, el color "aplica" y se ve mal — que es justo el tipo de hallazgo que este
        ' reporte existe para dar. Caso real: los 4 materiales de KSHairdos que apuntan a
        ' 'vhaircolor_lgrad_d.dds', que el mod nunca empaquetó.
        If ctx.IsSse Then Return
        Dim clfm = Canon.CanonRecords.Clfm(rec, ctx.PluginManager)
        If clfm Is Nothing OrElse Not clfm.TieneIndiceDePaleta() Then Return

        LmHairColorLutLoader.EnsureLoaded(ctx.PluginManager, ctx.DataPath)
        ' Las DOS cosas de la MISMA lectura del snapshot: pedirlas por separado deja una ventana en la que un
        ' Invalidate() entre medio devuelve `lut` del registro viejo y la custom del nuevo.
        ' Es la LUT custom APLICADA, no la que el registro tenga para ese color. Con una raza cuyo HNAM no
        ' es la gradient vanilla, ProcessEyebrowPath NO aplica la custom aunque el registro la tenga: leer
        ' "la que tiene" hacía que el reporte afirmara que la ceja usa una paleta que no usa, y que el aviso
        ' de textura faltante le atribuyera al haircolors.json el path del HNAM de la raza.
        Dim appliedCustom As String = Nothing
        Dim lut = LmHairColorLutLoader.ResolveBrowPaletteTexture(ctx.Race, p.HairColorFormID, appliedCustom)
        ' If(a, b) devuelve b sólo si a es Nothing, NO si es "". Canon.IClfm.FullName arranca en "" y sólo se
        ' asigna si hay subrecord FULL, así que un CLFM sin FULL —común en packs generados— imprimía comillas
        ' vacías: "'' is a palette colour but…".
        Dim colourName = If(String.IsNullOrEmpty(clfm.Name),
                            If(String.IsNullOrEmpty(clfm.EditorID), $"0x{p.HairColorFormID:X8}", clfm.EditorID),
                            clfm.Name)

        If String.IsNullOrEmpty(lut) Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Hair colour",
                                   $"'{colourName}' is a palette colour but {ctx.RaceDisplayName} declares no hair LUT",
                                   $"The colour selects row {clfm.IndiceDePaleta():F4} of a palette texture the RACE doesn't name (no HNAM), so the eyebrow tint has nothing to sample — the engine skips it too. The hair MESH still tints from its own material."))
            Return
        End If

        Dim key = FO4UnifiedMaterial_Class.CorrectTexturePath(lut)
        If key = "" OrElse Not FilesDictionary_class.Dictionary.ContainsKey(key) Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingAsset, "Hair colour",
                                   $"The palette texture for '{colourName}' isn't installed",
                                   $"'{lut}' isn't in loose files or the archives{If(Not String.IsNullOrEmpty(appliedCustom), " (registered by a LooksMenu haircolors.json)", "")}, so the eyebrow tint can't sample it and falls back to the layer's authored colour."))
        ElseIf Not String.IsNullOrEmpty(appliedCustom) Then
            r.Resolved.Add($"Hair colour '{colourName}' uses the LooksMenu custom palette '{appliedCustom}'")
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 5) Every other FormID the preset carries: SSE head TXST, skin ARMO (WNAM), outfits (DOFT/SOFT).
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditFormIdFields(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        ' `.Value` explícito: el guard garantiza HasValue, pero pasar el nullable pelado a un parámetro UInteger
        ' compila por Option Strict Off y tiraría InvalidOperationException si alguien afloja el guard.
        ' El estado clear (Some(0)) no se audita acá: no referencia ningún FormID que pueda faltar.
        If ctx.IsSse AndAlso p.SseHeadTextureFormIDOverride.HasValue AndAlso p.SseHeadTextureFormIDOverride.Value <> 0UI Then
            CheckFormId(ctx, r, "Head texture", p.SseHeadTextureFormIDOverride.Value, "TXST",
                        "the NPC's face TextureSet override (FTST) — the head falls back to the RACE/skin texture.")
        End If
        If p.SkinFormIDOverride.HasValue AndAlso p.SkinFormIDOverride.Value <> 0UI Then
            CheckFormId(ctx, r, "Skin (WNAM)", p.SkinFormIDOverride.Value, "ARMO",
                        "the NPC's skin ARMO override — the engine falls back to the RACE's skin.")
        End If
        If p.DefaultOutfitFormIDOverride.HasValue AndAlso p.DefaultOutfitFormIDOverride.Value <> 0UI Then
            CheckFormId(ctx, r, "Outfit (DOFT)", p.DefaultOutfitFormIDOverride.Value, "OTFT",
                        "the default outfit — the NPC keeps its current one.")
        End If
        If p.SleepOutfitFormIDOverride.HasValue AndAlso p.SleepOutfitFormIDOverride.Value <> 0UI Then
            CheckFormId(ctx, r, "Outfit (SOFT)", p.SleepOutfitFormIDOverride.Value, "OTFT",
                        "the sleep outfit — the NPC keeps its current one.")
        End If

        ' F4SE skin override: parsed, deliberately not applied on load (FO4 only).
        If Not ctx.IsSse AndAlso p.UnsupportedCounts IsNot Nothing AndAlso p.UnsupportedCounts.HasSkinOverride Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.NotApplied, "Skin", "F4SE skin override present in the file",
                                   "This app doesn't apply the LooksMenu skin override on load; every other field of the preset still applies."))
        End If
    End Sub

    Private Sub CheckFormId(ctx As PresetAuditContext, r As PresetAuditReport, category As String, fid As UInteger, expectedSig As String, consequence As String)
        Dim rec = ctx.PluginManager?.GetRecord(fid)
        If rec Is Nothing Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, category, $"{expectedSig} 0x{fid:X8} not found",
                                   "The record isn't in the current load order — its plugin isn't installed/enabled. Consequence: " & consequence))
        ElseIf rec.Header.Signature <> expectedSig Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingRecord, category, $"0x{fid:X8} is a {rec.Header.Signature}, not a {expectedSig}",
                                   "Consequence: " & consequence))
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 6) Face morphs.
    '    FO4: MSDK keys are resolved against the RACE's MSID sliders (MorphValues) and MPPI presets
    '         (gendered MorphPresets) — a key in neither maps to no morph name and does nothing.
    '    SSE: NAM9/NAMA are a FIXED engine map (no race table, nothing to miss); the RaceMenu custom
    '         morphs, however, are resolved through the per-race .slider catalog.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditFaceMorphs(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If Not ctx.IsSse Then
            ' MorphValues/MorphPresets son exclusivos de Fallout 4 — Skyrim no los declara en RACE.
            Dim raceFo4 = TryCast(ctx.Race, Canon.RaceFO4)
            If p.ChargenFaceMorphs IsNot Nothing AndAlso p.ChargenFaceMorphs.Count > 0 AndAlso raceFo4 IsNot Nothing Then
                Dim known As New HashSet(Of UInteger)
                For Each mv In raceFo4.MorphValues
                    known.Add(mv.ValueIndex)
                Next
                Dim presets = raceFo4.ReadMorphPresetsFlat(ctx.IsFemale)
                For Each mp In presets
                    known.Add(mp.Index)
                Next
                Dim unknown As New List(Of UInteger)
                For Each kv In p.ChargenFaceMorphs
                    If Not known.Contains(kv.Key) Then unknown.Add(kv.Key)
                Next
                If unknown.Count > 0 Then
                    unknown.Sort()
                    Dim shown = unknown.Take(12).Select(Function(k) $"0x{k:X8}")
                    r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Face morphs",
                                           $"{unknown.Count} of {p.ChargenFaceMorphs.Count} chargen slider key(s) aren't defined by this race",
                                           $"MSDK keys {String.Join(", ", shown)}{If(unknown.Count > 12, ", …", "")} match no MSID slider and no MPPI preset of this RACE, so they resolve to no morph name and apply nothing. Typical cause: the preset was made on another race (or with a race mod that adds sliders)."))
                End If
                If unknown.Count < p.ChargenFaceMorphs.Count Then
                    r.Resolved.Add($"{p.ChargenFaceMorphs.Count - unknown.Count} chargen slider key(s) map to this race's morphs")
                End If
            End If

            ' FMRS face-bone regions: applied verbatim from the record; the region table lives in a per-race
            ' asset file, not in the RACE record, so there is nothing to validate here — report the count only
            ' when the intensity would flatten them.
            If p.HasFaceBoneRegions AndAlso p.FaceBoneRegions IsNot Nothing AndAlso p.FaceBoneRegions.Count > 0 AndAlso p.FacialMorphIntensity = 0.0F Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Face bone regions", $"{p.FaceBoneRegions.Count} region(s) carried at intensity 0",
                                       "FMIN = 0 flattens every face-bone region, so these values have no visible effect."))
            End If

            ' MRSV body regions are positional against the RACE's MorphValues definitions.
            Dim raceMorphValueCount = If(raceFo4 Is Nothing, 0, raceFo4.MorphValues.Count)
            If p.BodyMorphValues IsNot Nothing AndAlso p.BodyMorphValues.Count > 0 AndAlso raceMorphValueCount > 0 AndAlso
               p.BodyMorphValues.Count > raceMorphValueCount Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Body regions",
                                       $"{p.BodyMorphValues.Count} MRSV values vs {raceMorphValueCount} defined by this race",
                                       "The extra positional values have no definition on this race and are ignored."))
            End If
            Return
        End If

        ' SSE RaceMenu custom morphs (NiOverride) — resolved by NAME through the per-race .slider catalog.
        If p.SseCustomMorphs Is Nothing OrElse p.SseCustomMorphs.Count = 0 Then Return
        Dim catalog = NpcMorphResolver.SliderCatalog
        If catalog Is Nothing Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Custom morphs", $"{p.SseCustomMorphs.Count} RaceMenu morph(s) not checked",
                                   "No RaceMenu slider catalog is loaded in this session, so each name is applied directly against the chargen .tri (best effort) instead of through its slider definition."))
            Return
        End If
        Dim raceEditorId = If(ctx.Race IsNot Nothing, ctx.Race.EditorID, "")
        Dim unknownNames As New List(Of String)
        Dim okMorphs As Integer = 0
        For Each cm In p.SseCustomMorphs
            If cm Is Nothing OrElse String.IsNullOrEmpty(cm.Name) Then Continue For
            Dim def = catalog.GetSlider(raceEditorId, ctx.IsFemale, cm.Name)
            If def Is Nothing Then unknownNames.Add(cm.Name) Else okMorphs += 1
        Next
        If unknownNames.Count > 0 Then
            Dim shown = unknownNames.Take(15)
            r.Issues.Add(New PresetIssue(PresetIssueKind.RaceIncompatible, "Custom morphs",
                                   $"{unknownNames.Count} RaceMenu slider(s) aren't registered for this race",
                                   $"{String.Join(", ", shown)}{If(unknownNames.Count > 15, ", …", "")} — no .slider definition for race '{raceEditorId}' ({If(ctx.IsFemale, "female", "male")}). The name is still tried directly against the chargen .tri, so it applies only if a morph of exactly that name exists there. Typical cause: the mod that adds those sliders isn't installed."))
        End If
        If okMorphs > 0 Then r.Resolved.Add($"{okMorphs} RaceMenu custom morph(s) resolve through this race's slider catalog")
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 7) BodySlide sliders — they load fine, but without BODYTRI on the body NIF the engine has nothing
    '    to morph in-game.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditBodySliders(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        Dim n As Integer = If(p.BodyMorphSliders Is Nothing, 0, p.BodyMorphSliders.Count)
        If ctx.IsSse AndAlso p.BodyMorphsKeyed IsNot Nothing Then n = Math.Max(n, p.BodyMorphsKeyed.Count)
        If n = 0 Then Return
        If Not ctx.NpcHasBodyTri Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.NotApplied, "Body sliders", $"{n} BodySlide slider(s) won't show in-game",
                                   "No shape of this NPC's body NIF carries BODYTRI extra-data, so the engine has no morph data to drive. The values are still stored and the app's preview applies them."))
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 8) Overlays. FO4: an OverlayEntry references a template id registered by an overlays.json; an id
    '    that isn't loaded for this gender contributes nothing (engine parity: GetTemplateByName → null,
    '    ForEachOverlayBySlot skips it). SSE: overlays are path-based — the node name must be one skee
    '    registers and the textures must actually be installed.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditOverlays(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If Not ctx.IsSse Then
            If p.Overlays Is Nothing OrElse p.Overlays.Count = 0 Then Return
            If ctx.KnownOverlayTemplateIds Is Nothing Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Overlays", $"{p.Overlays.Count} overlay(s) not checked",
                                       "The overlay template catalog wasn't available to this dialog."))
                Return
            End If
            Dim missing As New List(Of String)
            Dim ok As Integer = 0
            For Each ov In p.Overlays
                If ov Is Nothing OrElse String.IsNullOrEmpty(ov.TemplateId) Then Continue For
                If ctx.KnownOverlayTemplateIds.Contains(ov.TemplateId) Then ok += 1 Else missing.Add(ov.TemplateId)
            Next
            If missing.Count > 0 Then
                r.Issues.Add(New PresetIssue(PresetIssueKind.MissingMaster, "Overlays", $"{missing.Count} overlay template(s) aren't installed",
                                       $"{String.Join(", ", missing.Take(15))}{If(missing.Count > 15, ", …", "")} — no overlays.json loaded for this {If(ctx.IsFemale, "female", "male")} NPC registers these ids, so they paint nothing. Install the mod that ships them (Data\F4SE\Plugins\F4EE\Overlays\)."))
            End If
            If ok > 0 Then r.Resolved.Add($"{ok} overlay template(s) are installed")
            Return
        End If

        ' SSE body overlays (RaceMenu, path-based).
        If p.SseBodyOverlays IsNot Nothing AndAlso p.SseBodyOverlays.Count > 0 Then
            Dim okOv As Integer = 0
            Dim okMagic As Integer = 0
            ' Una sola lectura del ini para todo el reporte (OverlayCount statea el archivo en cada llamada).
            Dim slotLimits = {SseCatalogs.OverlayCount(SseCatalogs.OverlayZone.Body), SseCatalogs.OverlayCount(SseCatalogs.OverlayZone.Hands),
                              SseCatalogs.OverlayCount(SseCatalogs.OverlayZone.Feet), SseCatalogs.OverlayCount(SseCatalogs.OverlayZone.Face)}
            ' Y LOS DEL POOL MAGIC, QUE SON OTRA KEY. `ZoneOfNode` ahora reclama también los nodos [SOvl{n}]
            ' (son autorables), así que sin esto un `Body [SOvl2]` se validaba contra el iNumOverlays del pool
            ' NORMAL: con 6/1 daba "resuelve OK" para un nodo que el motor no crea, y con 0/2 daba un issue falso
            ' que mandaba a subir iNumOverlays — la key que NO gobierna ese nodo. Es exactamente el defecto que el
            ' aviso de sesión documenta como el pecado original, reintroducido en el reporte que SOBREVIVE a la
            ' sesión (o sea el único lugar donde el que abre un preset ajeno se enteraría).
            Dim spellLimits = {SseCatalogs.SpellOverlayCount(SseCatalogs.OverlayZone.Body), SseCatalogs.SpellOverlayCount(SseCatalogs.OverlayZone.Hands),
                               SseCatalogs.SpellOverlayCount(SseCatalogs.OverlayZone.Feet), SseCatalogs.SpellOverlayCount(SseCatalogs.OverlayZone.Face)}
            ' La fuente TAMBIÉN se saca una vez. Pedirla adentro del loop anulaba el hoist de arriba —vuelve a
            ' statear por cada overlay— y encima abría una ventana por fila para que el número impreso y el
            ' archivo nombrado dejen de corresponderse.
            Dim countSource = SseCatalogs.OverlayCountSource()
            For Each ov In p.SseBodyOverlays
                If ov Is Nothing Then Continue For
                Dim zone = SseCatalogs.ZoneOfNode(ov.NodeName)
                If Not zone.HasValue Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.NotApplied, "Overlays", $"Unknown overlay node '{ov.NodeName}'",
                                           "This node isn't one of the overlay slots skee registers, so nothing is painted through it."))
                    Continue For
                End If
                Dim bad As Boolean = False
                ' El nodo puede ser válido y aun así NO EXISTIR: skee sólo instancia iNumOverlays nodos por zona
                ' y el editor deja autorar más (avisa una vez por sesión, que se pierde al cerrar). Este reporte es
                ' la superficie que SOBREVIVE, así que el que abre un preset ajeno se entera acá. No es MissingAsset:
                ' la textura está, el que falta es el nodo.
                Dim slot = SseCatalogs.IndexOfNode(ov.NodeName)
                ' El pool decide CUÁL contador y CUÁL key nombrar. Los dos son independientes en el motor.
                Dim isSpell = SseCatalogs.IsSpellNode(ov.NodeName)
                Dim slotLimit = If(isSpell, spellLimits(CInt(zone.Value)), slotLimits(CInt(zone.Value)))
                Dim tag = If(isSpell, "SOvl", "Ovl")
                Dim keyName = If(isSpell, "iSpellOverlays", "iNumOverlays")
                ' ACÁ HABÍA UNA RAMA PARA UN SEGUNDO TOPE DEL POOL MAGIC ("la app no escribe un magic con índice
                ' ≥ 8"). Ese tope se fue entero: estaba apoyado en que Papyrus no exponía el contador del pool magic,
                ' y SÍ lo expone (GetNumSpell*Overlays, PapyrusNiOverride.cpp:1844-1853). Hoy el pool magic tiene UN
                ' solo límite —el del motor, igual que el normal— y es el que evalúa la rama de abajo.
                If slot >= slotLimit Then
                    ' Con bEnableFaceOverlays=0 el contador de cara es 0 pase lo que pase con iNumOverlays:
                    ' mandar a subir esa key sería mandarlo a una que su archivo ya tiene puesta. Vale para los DOS
                    ' pools de la cara: el flag apaga g_numFaceOverlays y g_numSpellFaceOverlays (main.cpp:833-836).
                    Dim byFlag = zone.Value = SseCatalogs.OverlayZone.Face AndAlso slotLimit = 0 AndAlso SseCatalogs.FaceOverlaysDisabledByIni()
                    ' EL TÍTULO DECÍA SÓLO "is past the N slot(s)", y el reporte agrupa esto bajo "will NOT reach the
                    ' NPC". Eso es IMPRECISO y hace dudar de la herramienta: el override SÍ se escribe (va al ESP y al
                    ' co-save, y empieza a pintar el día que suban la key). Lo que NO pasa es que PINTE, porque el juego
                    ' no construye ese nodo. Encontrado probando de verdad: "el compatibility me dice que no va a llegar
                    ' pero sí llega". Las dos cosas eran ciertas y el texto no las distinguía.
                    r.Issues.Add(New PresetIssue(PresetIssueKind.NotApplied, "Overlays",
                                           $"Overlay '{ov.NodeName}' IS written, but it will not paint: it is past the {slotLimit} {If(isSpell, "magic ", "")}slot(s) this install creates for {zone.Value}",
                                           If(byFlag,
                                              $"[Features] bEnableFaceOverlays=0 turns face overlays off entirely — both pools — ({countSource}), so this node is never built and " &
                                              $"paints nothing whatever {keyName} says. Set bEnableFaceOverlays=1, or drop the overlay.",
                                              $"{keyName} gives {If(slotLimit > 0, $"[{tag}0]…[{tag}{slotLimit - 1}]", "no slot at all")} ({countSource}), " &
                                              $"so this game never builds that node. The override is still saved and still travels to the NPC — it " &
                                              $"simply has nothing to paint on, and it starts painting the day {keyName} is raised. " &
                                              $"Raise {keyName} in skee64.ini, or move the overlay into a free slot.")))
                    bad = True
                End If
                If Not String.IsNullOrWhiteSpace(ov.DiffusePath) AndAlso Not SseCatalogs.TextureResolves(ov.DiffusePath) Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.MissingAsset, "Overlays", $"Overlay '{ov.NodeName}': diffuse texture not installed", ov.DiffusePath))
                    bad = True
                End If
                If Not String.IsNullOrWhiteSpace(ov.NormalPath) AndAlso Not SseCatalogs.TextureResolves(ov.NormalPath) Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.MissingAsset, "Overlays", $"Overlay '{ov.NodeName}': normal map not installed", ov.NormalPath))
                    bad = True
                End If
                If Not bad Then
                    okOv += 1
                    If isSpell Then okMagic += 1
                End If
            Next
            If okOv > 0 Then r.Resolved.Add($"{okOv} RaceMenu overlay(s) resolve (node + textures installed)")
            ' El pool MAGIC merece su propia línea: no es "un overlay más". Se entrega por otra vía (el
            ' apply-script, nunca el bake) y su opacidad la ANIMA el motor, así que ni el preview ni el bake pueden
            ' mostrar "cómo se va a ver" — y eso es justo lo que el usuario viene a preguntarle a este reporte.
            If okMagic > 0 Then
                ' "nunca se hornean" NO es una propiedad del pool magic fuera de la CARA: ningún overlay de
                ' cuerpo/manos/pies se hornea (el fold es sólo de la cara), así que decirlo así presentaba como
                ' diferencia algo que comparte con el pool normal. Lo que sí es propio del magic en TODAS las zonas
                ' es la alpha animada por el motor; lo del bake se aclara sólo para la cara.
                r.Resolved.Add($"{okMagic} of them are MAGIC ([SOvl]) overlays: the engine animates their alpha " &
                               "so they fade in and out instead of sitting still. A magic FACE overlay is also " &
                               "never baked into the head texture — the helper script delivers it instead")
            End If
        End If

        ' SSE skin overrides (body paint per biped slot) — same texture-presence rule.
        If p.SseSkinOverrides IsNot Nothing AndAlso p.SseSkinOverrides.Count > 0 Then
            For Each so In p.SseSkinOverrides
                If so Is Nothing OrElse so.Slots Is Nothing Then Continue For
                For Each kv In so.Slots
                    If String.IsNullOrWhiteSpace(kv.Value) Then Continue For
                    If Not SseCatalogs.TextureResolves(kv.Value) Then
                        r.Issues.Add(New PresetIssue(PresetIssueKind.MissingAsset, "Skin overrides",
                                               $"Slot mask 0x{so.SlotMask:X}, texture slot {kv.Key}: texture not installed", kv.Value))
                    End If
                Next
            Next
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 9) F4SE LM skin template (FO4-only) — an id no loaded SkinInterface template declares applies nothing.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditLmSkinTemplate(ctx As PresetAuditContext, r As PresetAuditReport)
        Dim p = ctx.Preset
        If ctx.IsSse OrElse String.IsNullOrEmpty(p.SkinTemplateId) Then Return
        If ctx.KnownLmSkinTemplateIds Is Nothing Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "LM skin template", $"'{p.SkinTemplateId}' not checked",
                                   "The LM skin template catalog wasn't available to this dialog."))
            Return
        End If
        If Not ctx.KnownLmSkinTemplateIds.Contains(p.SkinTemplateId) Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.MissingMaster, "LM skin template", $"Template '{p.SkinTemplateId}' isn't installed",
                                   $"No LooksMenu skin template with this id is registered for a {If(ctx.IsFemale, "female", "male")} NPC (Data\F4SE\Plugins\F4EE\Skin\), so the skin override applies nothing."))
        Else
            r.Resolved.Add($"LM skin template '{p.SkinTemplateId}' is installed")
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' 10) SSE sculpt + node transforms.
    '     * sculpt: carried per-shape by "host" chargen .tri; a block whose host is empty can't be routed.
    '     * node transforms: nothing here is "missing" — the note exists because the value the app shows is the
    '       EFFECTIVE one (it composed every contributor's layer on import) and it travels to the game as ONE
    '       contribution under our own label. The script neutralises (writes full identity to) exactly the layer
    '       NAMES the preset itself carried, so its own contributions cannot be counted twice; anything under a
    '       name we never saw — the engine's high-heel offset, another mod — is left alone and composes with ours.
    ' ---------------------------------------------------------------------------------------------
    Private Sub AuditSculptAndTransforms(ctx As PresetAuditContext, r As PresetAuditReport)
        If Not ctx.IsSse Then Return
        Dim p = ctx.Preset
        If p.SseSculptParts IsNot Nothing Then
            For Each sp In p.SseSculptParts
                If sp Is Nothing Then Continue For
                If String.IsNullOrWhiteSpace(sp.Host) Then
                    r.Issues.Add(New PresetIssue(PresetIssueKind.NotApplied, "Sculpt", $"A sculpt block ({If(sp.Verts Is Nothing, 0, sp.Verts.Count)} verts) declares no host shape",
                                           "Without a host chargen .tri the block can't be routed to a rendered shape, so it is skipped."))
                End If
            Next
        End If

        ' --- node transforms. The predicate MUST agree with NpcApplyScriptEmitter's (name + at least one of
        ' scale/position/rotation): a node the emitter skips is not authored, and claiming it would be a lie.
        If p.SseNodeTransforms Is Nothing Then Return
        Dim authored As New List(Of String)
        Dim weapons As New List(Of String)
        For Each nt In p.SseNodeTransforms
            If nt Is Nothing OrElse String.IsNullOrEmpty(nt.NodeName) Then Continue For
            If Not (nt.HasScale OrElse nt.HasPosition OrElse nt.HasRotation) Then Continue For
            authored.Add(nt.NodeName)
            If SseCatalogs.IsWeaponNode(nt.NodeName) Then weapons.Add(nt.NodeName)
        Next
        If authored.Count = 0 Then Return

        r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Node transforms",
            $"{authored.Count} bone(s) authored under this tool's own override label ({RaceMenuJslot.AppOverrideKey})",
            "The value shown for these bones is the EFFECTIVE one: a preset that carried several " &
            "contributors on one bone was composed into the single value the engine would produce, and that " &
            "is what the app displays and what it writes — as one contribution, under its own label." &
            ControlChars.Lf &
            "So that its own contributions cannot be counted twice, the script also writes a neutral value to " &
            "exactly the contributor names this preset carried — it matters when some other mod has already " &
            "applied this same preset to this NPC. Anything under a name the preset did not carry is left " &
            "alone: the engine's high-heel offset, or another mod that transforms this NPC, still composes with " &
            "the value shown here, so in that case the game shows more than the app does. That is the correct " &
            "outcome for the high heels and a mod conflict in the other." & ControlChars.Lf &
            "And nothing of this reaches the game unless the NPC is saved with the helper script attached " &
            "(the ""Attach the helper script"" box in the Save dialog, ticked by default)." &
            ControlChars.Lf & "Bones: " & String.Join(", ", authored)))

        If weapons.Count > 0 Then
            r.Issues.Add(New PresetIssue(PresetIssueKind.Note, "Node transforms",
                $"{weapons.Count} of them are weapon nodes — the value is NOT final there",
                "XPMSE owns weapon placement and re-applies it on every weapon change, so its layer comes " &
                "back after the script removed it. It also places weapons by RE-PARENTING the node, which " &
                "this tool deliberately never removes — so on these bones treat the value as a starting " &
                "position, not as authored state. (The appearance preview does not render equipped gear " &
                "either, which is why the editor hides these behind 'show all'.)" &
                ControlChars.Lf & "Bones: " & String.Join(", ", weapons)))
        End If
    End Sub

    ' ---------------------------------------------------------------------------------------------
    ' Text rendering — monospace report for the modal viewer.
    ' ---------------------------------------------------------------------------------------------
    Public Function BuildText(rep As PresetAuditReport) As String
        Dim sb As New StringBuilder
        If rep Is Nothing Then Return ""
        For Each h In rep.Header
            sb.AppendLine(h)
        Next
        sb.AppendLine(New String("="c, 96))
        sb.AppendLine()

        If rep.Issues.Count = 0 Then
            sb.AppendLine("No missing or incompatible content found for this NPC.")
            sb.AppendLine()
        Else
            sb.AppendLine($"{rep.MissingCount} finding(s) that will NOT reach the NPC, {rep.Count - rep.MissingCount} note(s).")
            sb.AppendLine()
            ' Group by category, keeping first-seen order, and order the kinds hardest-first inside it.
            Dim categories As New List(Of String)
            For Each i In rep.Issues
                If Not categories.Contains(i.Category) Then categories.Add(i.Category)
            Next
            For Each cat In categories
                sb.AppendLine(cat)
                sb.AppendLine(New String("-"c, cat.Length))
                For Each i In rep.Issues
                    If i.Category <> cat Then Continue For
                    sb.AppendLine($"  {KindGlyph(i.Kind)} {i.Title}")
                    If i.Detail.Length > 0 Then
                        For Each line In WrapText(i.Detail, 88)
                            sb.AppendLine("        " & line)
                        Next
                    End If
                Next
                sb.AppendLine()
            Next
        End If

        If rep.Resolved.Count > 0 Then
            sb.AppendLine("Checked and OK")
            sb.AppendLine(New String("-"c, 14))
            For Each ok In rep.Resolved
                sb.AppendLine("  + " & ok)
            Next
            sb.AppendLine()
        End If

        sb.AppendLine("Legend: [MASTER] owning plugin/mod not installed   [RECORD] FormID doesn't resolve")
        sb.AppendLine("        [RACE]   rejected by this NPC's race        [FILE]   texture/asset not installed")
        sb.AppendLine("        [SKIP]   carried but not applied            [note]   informational")
        Return sb.ToString()
    End Function

    Private Function KindGlyph(k As PresetIssueKind) As String
        Select Case k
            Case PresetIssueKind.MissingMaster : Return "[MASTER]"
            Case PresetIssueKind.MissingRecord : Return "[RECORD]"
            Case PresetIssueKind.RaceIncompatible : Return "[RACE]  "
            Case PresetIssueKind.MissingAsset : Return "[FILE]  "
            Case PresetIssueKind.NotApplied : Return "[SKIP]  "
            Case Else : Return "[note]  "
        End Select
    End Function

    ''' <summary>Word-wrap for the fixed-width report body. Long unbreakable tokens (paths, FormID lists)
    ''' are emitted on their own line rather than truncated — the viewer scrolls horizontally.</summary>
    Private Function WrapText(s As String, width As Integer) As List(Of String)
        Dim result As New List(Of String)
        If String.IsNullOrEmpty(s) Then Return result
        For Each rawLine In s.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim line As String = ""
            For Each word In rawLine.Split(" "c)
                If line.Length = 0 Then
                    line = word
                ElseIf line.Length + 1 + word.Length <= width Then
                    line &= " " & word
                Else
                    result.Add(line)
                    line = word
                End If
            Next
            result.Add(line)
        Next
        Return result
    End Function

    Private Function DisplaySourcePath(src As String, dataPath As String) As String
        If String.IsNullOrEmpty(src) Then Return "(unknown)"
        Try
            If Not String.IsNullOrEmpty(dataPath) AndAlso src.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase) Then
                Return src.Substring(dataPath.Length).TrimStart(Path.DirectorySeparatorChar)
            End If
        Catch
        End Try
        Return src
    End Function

End Module
