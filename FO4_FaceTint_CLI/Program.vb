Imports System.IO
Imports System.Linq
Imports System.Text.Json
Imports FO4_Base_Library

''' <summary>
''' Headless FaceTint texture baker. Given an ESP name + NPC EditorID (or a --list of them), composes
''' the D/N/S face-tint textures through the SAME library compositor the app uses (FaceTintInputBuilder
''' + FaceTintCpuCompositor) and writes them as uncompressed TGA with a `_3` suffix next to CK's `_` and
''' the app's `_2` (in Data\Textures\Actors\Character\FaceCustomization\&lt;plugin&gt;).
'''
''' References ONLY FO4_Base_Library. The NPC is baked PRISTINE from the ESP record (no LooksMenu overlay
''' — that is app-specific and lives in FO4_NPC_Manager). Convention + order come from the app config.json
''' (--config) optionally overridden per-run via --convention / --sort.
'''
''' BATCH: with --list, plugins + archives mount ONCE and the library's BatchDecodeCache + a shared
''' tintBytesCache make every DDS decode/read happen once across the whole list (face source, tints,
''' swaps, hair LUTs — most are shared between NPCs of the same race).
'''
''' Usage: FO4_FaceTint_CLI (--esp &lt;plugin&gt; --edid &lt;EditorID&gt; | --list &lt;file&gt;) [--config c.json] [--data Data\] [--out dir]
''' </summary>
Module Program

    Private Class CliArgs
        Public Esp As String = ""
        Public Edid As String = ""
        Public ListPath As String = ""
        Public ConfigPath As String = ""
        Public ConventionPath As String = ""
        Public SortPath As String = ""
        Public DataPath As String = ""
        Public OutDir As String = ""
        Public SweepDir As String = ""
        Public DumpDir As String = ""
        Public Info As Boolean = False
        Public Tints As Boolean = False
        Public TtedScan As Boolean = False
        Public RankBy As String = "n"   ' canal por el que rankea el sweep: n (default) / d / s
    End Class

    ' TETI.Slot enum (xEdit wbDefinitionsFO4.pas:3465-3491) — nombre por valor, para --tints.
    Private ReadOnly SlotNames As String() = {
        "ForeheadMask", "EyesMask", "NoseMask", "EarsMask", "CheeksMask", "MouthMask", "NeckMask",
        "LipColor", "CheekColor", "Eyeliner", "EyeSocketUpper", "EyeSocketLower", "SkinTone", "Paint",
        "LaughLines", "CheekColorLower", "Nose", "Chin", "Neck", "Forehead", "Dirt", "Scars",
        "FaceDetail", "Brows", "Wrinkles", "Beards"}
    Private Function SlotName(s As Integer) As String
        Return If(s >= 0 AndAlso s < SlotNames.Length, SlotNames(s), $"Slot{s}")
    End Function

    Sub Main(args As String())
        Try
            Run(args)
        Catch ex As Exception
            Console.Error.WriteLine("FATAL: " & ex.ToString())
            Environment.ExitCode = 1
        End Try
    End Sub

    Private Sub Run(args As String())
        Dim opt = ParseArgs(args)
        If opt Is Nothing Then Return

        ' --- 1. Config (app config.json local) ---
        Config_App.LoadConfig()
        Config_App.Current.Game = Config_App.Game_Enum.Fallout4
        Dim dataPath = If(opt.DataPath <> "", opt.DataPath, Config_App.Current.FO4EDataPath)
        If String.IsNullOrEmpty(dataPath) OrElse Not Directory.Exists(dataPath) Then
            Console.Error.WriteLine($"Data path invalido: '{dataPath}'. Usa --data <ruta a Data\> o configura config.json.")
            Environment.ExitCode = 1 : Return
        End If

        ' --- 2. Config base: secciones FaceTint del config.json del APP (sin copiarlo a la bin) ---
        If opt.ConfigPath <> "" Then
            Using doc = JsonDocument.Parse(File.ReadAllText(opt.ConfigPath))
                Dim el As JsonElement
                If doc.RootElement.TryGetProperty("Setting_FaceTintConvention", el) Then
                    Config_App.Current.Setting_FaceTintConvention =
                        JsonSerializer.Deserialize(Of FaceTintConvention.FaceTintConventionSettings)(el.GetRawText())
                End If
                If doc.RootElement.TryGetProperty("Setting_FaceTintSort", el) Then
                    Config_App.Current.Setting_FaceTintSort =
                        JsonSerializer.Deserialize(Of FaceTintSortSettings)(el.GetRawText())
                End If
            End Using
            Console.WriteLine($"[cfg] FaceTint settings <- {opt.ConfigPath}")
        End If

        ' --- 2b. Override granular de convencion / orden (opcional, pisa lo anterior) ---
        If opt.ConventionPath <> "" Then
            Config_App.Current.Setting_FaceTintConvention =
                JsonSerializer.Deserialize(Of FaceTintConvention.FaceTintConventionSettings)(File.ReadAllText(opt.ConventionPath))
            Console.WriteLine($"[cfg] convencion <- {opt.ConventionPath}")
        End If
        If opt.SortPath <> "" Then
            Config_App.Current.Setting_FaceTintSort =
                JsonSerializer.Deserialize(Of FaceTintSortSettings)(File.ReadAllText(opt.SortPath))
            Console.WriteLine($"[cfg] orden <- {opt.SortPath}")
        End If

        ' --- 3. Lista de trabajo (esp, edid) ---
        Dim work = BuildWorkList(opt)
        If work.Count = 0 AndAlso Not opt.TtedScan Then
            Console.Error.WriteLine("No hay NPCs para procesar (revisa --edid / --list).") : Environment.ExitCode = 1 : Return
        End If

        ' --- 4. Encoding (mismo orden que el exe: antes de cargar plugins) ---
        PluginEncodingSettings.InitializeForGame(Config_App.Current.Game)
        PluginEncodingSettings.SetLanguage(PluginEncodingSettings.ReadLanguageFromIni())

        ' --- 5. Bootstrap headless: plugins (load order activa + TODOS los esps de la lista, aunque NO
        '        esten activos, + sus masters) + archivos (BA2+loose). Se monta UNA sola vez. ---
        Console.WriteLine("[load] plugins...")
        Dim pm As New PluginManager()
        Dim loadList = PluginManager.ReadActiveLoadOrder()
        For Each esp In work.Select(Function(w) w.Esp).Distinct(StringComparer.OrdinalIgnoreCase)
            EnsureEspInLoadList(loadList, esp, dataPath)
        Next
        pm.LoadAllPlugins(dataPath, loadList)

        Console.WriteLine("[load] montando archivos...")
        Dim cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Caches")
        Directory.CreateDirectory(cacheDir)
        FilesDictionary_class.CacheDirectory = cacheDir
        FilesDictionary_class.RegisterExtensions(".ssf", ".sclp")
        ' progress NO puede ser Nothing: Fill_DictionaryAsync hace progress.Report(...) sin guard
        ' (FilesDictionary_class.vb:1010) -> NRE tragado por su Try -> Dictionary vacio. Pasamos un no-op.
        Dim noProg As New Progress(Of (Stepn As String, Value As Integer, Max As Integer))()
        FilesDictionary_class.Fill_DictionaryAsync(dataPath, noProg).GetAwaiter().GetResult()

        ' --- 6''. INFO: vuelca la cadena de resolucion de base (FTST/RACE/HDPT/NIF) sin componer ---
        If opt.Info Then
            For Each w In work
                InfoNpc(pm, w.Esp, w.Edid)
            Next
            Return
        End If

        ' --- 6'''. TINTS: vuelca tints del NPC + grupos/opciones de RACE (slot, TTED float+raw, alpha) + merge ---
        If opt.Tints Then
            For Each w In work
                TintsNpc(pm, w.Esp, w.Edid)
            Next
            Return
        End If

        ' --- 6''''. TTEDSCAN: recorre TODAS las RACE, junta los TTED, reporta distintos + no-enteros ---
        If opt.TtedScan Then
            TtedScan(pm)
            Return
        End If

        ' --- 6'. SWEEP: barre convenciones (carpeta de config jsons) contra CK con UNA carga ---
        If opt.SweepDir <> "" Then
            RunSweep(pm, work, dataPath, opt.SweepDir, opt.RankBy)
            Return
        End If

        ' --- 6. Batch: cache de decode persistente entre NPCs + cache de bytes crudos de layers/swaps.
        '        Cada DDS se decodifica/lee UNA vez en todo el batch (caras de la misma raza comparten). ---
        FaceTintCpuCompositor.BeginBatchDecodeCache()
        Dim tintBytesCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Dim ok As Integer = 0, fail As Integer = 0
        Try
            For Each w In work
                If BakeNpc(pm, w.Esp, w.Edid, dataPath, opt.OutDir, tintBytesCache, opt.DumpDir) Then ok += 1 Else fail += 1
            Next
        Finally
            FaceTintCpuCompositor.EndBatchDecodeCache()
        End Try
        Console.WriteLine($"[done] {ok} ok / {fail} fail de {work.Count}")
        If ok = 0 Then Environment.ExitCode = 1
    End Sub

    ''' <summary>Compone + escribe los TGA `_3` de UN NPC. Devuelve True si escribio. tintBytesCache es
    ''' compartido por todo el batch (bytes crudos de las texturas de layers/swaps leidos una sola vez);
    ''' el decode cacheado entre NPCs lo maneja FaceTintCpuCompositor.BatchDecodeCache via las keys.</summary>
    Private Function BakeNpc(pm As PluginManager, espName As String, edid As String, dataPath As String,
                             outOverride As String, tintBytesCache As Dictionary(Of String, Byte()),
                             dumpDir As String) As Boolean
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then
            Console.Error.WriteLine($"[skip] EDID='{edid}' no provisto por '{espName}'.") : Return False
        End If
        Dim originPlugin = pm.GetOriginatingPluginName(npcFormID)

        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pm)
        If npcData Is Nothing Then Console.Error.WriteLine($"[skip] {edid}: ParseNPC fallo.") : Return False
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then
            Console.Error.WriteLine($"[skip] {edid}: RACE 0x{npcData.RaceFormID:X8} no resuelta.") : Return False
        End If
        Dim race = RecordParsers.ParseRACE(raceRec, pm)

        Dim dPath As String = "", nPath As String = "", sPath As String = ""
        ResolveFaceSkin(npcData, race, pm, dPath, nPath, sPath)
        If String.IsNullOrEmpty(dPath) Then Console.Error.WriteLine($"[skip] {edid}: sin textura diffuse de cara.") : Return False
        Dim dKey = FO4UnifiedMaterial_Class.CorrectTexturePath(dPath)
        Dim nKey = FO4UnifiedMaterial_Class.CorrectTexturePath(nPath)
        Dim sKey = FO4UnifiedMaterial_Class.CorrectTexturePath(sPath)
        Dim dBytes = FilesDictionary_class.GetBytes(dKey)
        Dim nBytes = FilesDictionary_class.GetBytes(nKey)
        Dim sBytes = FilesDictionary_class.GetBytes(sKey)
        If dBytes Is Nothing OrElse dBytes.Length = 0 Then Console.Error.WriteLine($"[skip] {edid}: diffuse bytes vacios (key='{dKey}').") : Return False

        Dim hairLut = ResolveHairLut(npcData, race, pm)
        ' Texture lighting (QNAM) leido del record, NO hardcodeado: el app inyecta la capa slot-12
        ' SkinTone sintetica desde el QNAM (FaceTintInputBuilder.InjectSyntheticSkinToneLayer) y el
        ' bake debe hacer lo mismo o la cara diverge (la capa SoftLight pisa D y R/G de N/S).
        Dim built = FaceTintInputBuilder.Build(npcData, race, npcData.IsFemale, pm, tintBytesCache,
                                               hairLut, npcData.HairColorFormID,
                                               npcData.HasTextureLighting, npcData.TextureLightingColor.ToArgb())
        Dim cpu = FaceTintCpuCompositor.ComposeCpuPipeline(dBytes, nBytes, sBytes, built.Layers, built.RegionSwaps,
                                                           resolution:=Nothing, diffuseKey:=dKey, normalKey:=nKey, specKey:=sKey)

        Dim outDir = If(outOverride <> "", outOverride,
                        Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin))
        Directory.CreateDirectory(outDir)
        Dim localId = FaceGenLocalId(npcFormID)
        WriteChannel(outDir, localId, "d", cpu.Diffuse)
        WriteChannel(outDir, localId, "msn", cpu.Normal)
        WriteChannel(outDir, localId, "s", cpu.Specular)
        If dumpDir <> "" Then DumpMasks(built, dKey, nKey, sKey, Path.Combine(dumpDir, $"{localId:X8}"))
        Console.WriteLine($"[ok] {edid} 0x{npcFormID:X8} -> {localId:X8}_*_3.tga (layers={built.Layers.Count} swaps={built.RegionSwaps.Count} hairLut='{hairLut}')")
        Return True
    End Function

    ''' <summary>Dumpea SOLO los MASKS (inputs) del compositor a un dir: BASEIN (source D/N/S) + por capa
    ''' (textura por canal + diffmask + hair-LUT) + por swap (textura por canal + regionmask). NO dumpea
    ''' intermedios de composicion (per-stage). Reusa WritePristineTga (decode CPU/DirectXTex by key).</summary>
    Private Sub DumpMasks(built As FaceTintInputBuilder.TintBuildResult, dKey As String, nKey As String, sKey As String, outDir As String)
        Directory.CreateDirectory(outDir)
        DumpKey(dKey, Path.Combine(outDir, "BASEIN_Diffuse.tga"))
        DumpKey(nKey, Path.Combine(outDir, "BASEIN_Normal.tga"))
        DumpKey(sKey, Path.Combine(outDir, "BASEIN_Specular.tga"))
        Dim chans = {(FaceTintChannel.Diffuse, "Diffuse"), (FaceTintChannel.Normal, "Normal"), (FaceTintChannel.Specular, "Specular")}
        Dim i As Integer = 0
        For Each L In built.Layers
            Dim nm = San(L.DebugName)
            For Each c In chans
                DumpKey(L.GetChannelCacheKey(c.Item1), Path.Combine(outDir, $"L{i:00}_{nm}_{c.Item2}_layer.tga"))
            Next
            DumpKey(L.LayerCacheKey, Path.Combine(outDir, $"L{i:00}_{nm}_diffmask.tga"))
            DumpKey(L.HairLutCacheKey, Path.Combine(outDir, $"L{i:00}_{nm}_HAIRLUT.tga"))
            i += 1
        Next
        i = 0
        For Each S In built.RegionSwaps
            Dim nm = San(S.DebugName)
            For Each c In chans
                DumpKey(S.GetSwapCacheKey(c.Item1), Path.Combine(outDir, $"S{i:00}_{nm}_{c.Item2}_swap.tga"))
            Next
            DumpKey(S.RegionMaskCacheKey, Path.Combine(outDir, $"S{i:00}_{nm}_regionmask.tga"))
            i += 1
        Next
        Console.WriteLine($"[dump] masks -> {outDir}")
    End Sub

    Private Sub DumpKey(key As String, outPath As String)
        If String.IsNullOrEmpty(key) Then Return
        Try : FaceTintCompositor.WritePristineTga(key, outPath) : Catch : End Try
    End Sub

    Private Function San(s As String) As String
        If String.IsNullOrEmpty(s) Then Return "x"
        For Each ch In IO.Path.GetInvalidFileNameChars() : s = s.Replace(ch, "_"c) : Next
        Return s.Replace(" "c, "_"c)
    End Function

    ' ===================== SWEEP DE CONVENCIONES =====================

    Private Class ChRef
        Public W As Integer, H As Integer
        Public Rgb As Byte()        ' w*h*3 (R,G,B)
    End Class

    Private Class NpcSweepCtx
        Public Edid As String
        Public NpcData As NPC_Data
        Public Race As RACE_Data
        Public IsFemale As Boolean
        Public HairColorFormID As UInteger
        Public HairLut As String
        Public HasTextureLighting As Boolean
        Public TextureLightingArgb As Integer
        Public DKey As String, NKey As String, SKey As String
        Public DBytes As Byte(), NBytes As Byte(), SBytes As Byte()
        Public CkD As ChRef, CkN As ChRef, CkS As ChRef
    End Class

    ''' <summary>Lee un TGA truecolor uncompressed (24/32 bpp) a (W,H,RGB). Maneja origen top/bottom-left.</summary>
    Private Function LoadTgaRgb(path As String) As ChRef
        If Not File.Exists(path) Then Return Nothing
        Dim b = File.ReadAllBytes(path)
        Dim idlen = CInt(b(0))
        Dim w = CInt(b(12)) Or (CInt(b(13)) << 8)
        Dim h = CInt(b(14)) Or (CInt(b(15)) << 8)
        Dim nch = CInt(b(16)) \ 8
        Dim topLeft = (b(17) And &H20) <> 0
        Dim off = 18 + idlen
        Dim rgb(w * h * 3 - 1) As Byte
        For y = 0 To h - 1
            Dim srcY = If(topLeft, y, h - 1 - y)
            Dim rowOff = off + srcY * w * nch
            Dim dstOff = y * w * 3
            For x = 0 To w - 1
                Dim si = rowOff + x * nch
                Dim di = dstOff + x * 3
                rgb(di) = b(si + 2) : rgb(di + 1) = b(si + 1) : rgb(di + 2) = b(si)   ' BGR(A) -> RGB
            Next
        Next
        Return New ChRef With {.W = w, .H = h, .Rgb = rgb}
    End Function

    ''' <summary>Distancia del canal compuesto (BGRA) vs CK (RGB) en el footprint de cara (CK no-negro):
    ''' mean y peor-byte del max-por-pixel sobre R/G/B. NaN si no comparable.</summary>
    Private Function DiffVsCk(ch As FaceTintCpuCompositor.CpuChannelResult, ck As ChRef) As (Mean As Double, Max As Integer)
        If ch Is Nothing OrElse ch.Bgra Is Nothing OrElse ck Is Nothing Then Return (Double.NaN, -1)
        If ch.Width <> ck.W OrElse ch.Height <> ck.H Then Return (Double.NaN, -1)
        Dim n = ck.W * ck.H
        Dim bgra = ch.Bgra, rgb = ck.Rgb
        Dim sum As Double = 0 : Dim cnt As Long = 0 : Dim mx As Integer = 0
        For i = 0 To n - 1
            Dim r = CInt(rgb(i * 3)), g = CInt(rgb(i * 3 + 1)), bl = CInt(rgb(i * 3 + 2))
            If (r Or g Or bl) = 0 Then Continue For
            Dim cr = CInt(bgra(i * 4 + 2)), cg = CInt(bgra(i * 4 + 1)), cb = CInt(bgra(i * 4))
            Dim d = Math.Max(Math.Abs(cr - r), Math.Max(Math.Abs(cg - g), Math.Abs(cb - bl)))
            sum += d : cnt += 1
            If d > mx Then mx = d
        Next
        Return (If(cnt > 0, sum / cnt, Double.NaN), mx)
    End Function

    ''' <summary>Resuelve un NPC para el sweep (npcData/race/skin-bytes/hairLut) + carga sus refs CK.</summary>
    Private Function ResolveSweepNpc(pm As PluginManager, esp As String, edid As String, dataPath As String) As NpcSweepCtx
        Dim fid = ResolveEdid(pm, esp, edid)
        If fid = 0UI Then Return Nothing
        Dim originPlugin = pm.GetOriginatingPluginName(fid)
        Dim npcRec = pm.GetRecord(fid)
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pm)
        If npcData Is Nothing Then Return Nothing
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        If raceRec Is Nothing OrElse raceRec.Header.Signature <> "RACE" Then Return Nothing
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Dim dP As String = "", nP As String = "", sP As String = ""
        ResolveFaceSkin(npcData, race, pm, dP, nP, sP)
        If String.IsNullOrEmpty(dP) Then Return Nothing
        Dim dk = FO4UnifiedMaterial_Class.CorrectTexturePath(dP)
        Dim nk = FO4UnifiedMaterial_Class.CorrectTexturePath(nP)
        Dim sk = FO4UnifiedMaterial_Class.CorrectTexturePath(sP)
        Dim ctx As New NpcSweepCtx With {
            .Edid = edid, .NpcData = npcData, .Race = race, .IsFemale = npcData.IsFemale,
            .HairColorFormID = npcData.HairColorFormID, .HairLut = ResolveHairLut(npcData, race, pm),
            .HasTextureLighting = npcData.HasTextureLighting, .TextureLightingArgb = npcData.TextureLightingColor.ToArgb(),
            .DKey = dk, .NKey = nk, .SKey = sk,
            .DBytes = FilesDictionary_class.GetBytes(dk), .NBytes = FilesDictionary_class.GetBytes(nk), .SBytes = FilesDictionary_class.GetBytes(sk)}
        Dim localId = FaceGenLocalId(fid)
        Dim ckDir = Path.Combine(dataPath, "Textures", "Actors", "Character", "FaceCustomization", originPlugin)
        ctx.CkD = LoadTgaRgb(Path.Combine(ckDir, $"{localId:X8}_d.tga"))
        ctx.CkN = LoadTgaRgb(Path.Combine(ckDir, $"{localId:X8}_msn.tga"))
        ctx.CkS = LoadTgaRgb(Path.Combine(ckDir, $"{localId:X8}_s.tga"))
        Return ctx
    End Function

    ''' <summary>Setea las secciones FaceTint de Config_App.Current desde un config json (convencion + orden).</summary>
    Private Sub ApplyConfigJson(path As String)
        Using doc = JsonDocument.Parse(File.ReadAllText(path))
            Dim el As JsonElement
            If doc.RootElement.TryGetProperty("Setting_FaceTintConvention", el) Then
                Config_App.Current.Setting_FaceTintConvention =
                    JsonSerializer.Deserialize(Of FaceTintConvention.FaceTintConventionSettings)(el.GetRawText())
            End If
            If doc.RootElement.TryGetProperty("Setting_FaceTintSort", el) Then
                Config_App.Current.Setting_FaceTintSort =
                    JsonSerializer.Deserialize(Of FaceTintSortSettings)(el.GetRawText())
            End If
        End Using
    End Sub

    Private Function AvgOr(l As List(Of Double)) As Double
        Return If(l.Count > 0, l.Average(), Double.NaN)
    End Function

    ''' <summary>Barre cada config .json de sweepDir contra CK con UNA sola carga. Resuelve los NPCs +
    ''' carga refs CK una vez; el BatchDecodeCache + tintBytesCache persisten entre TODAS las convenciones
    ''' (los inputs no cambian, solo la math/orden) -> cada DDS se decodifica una sola vez en todo el sweep.
    ''' Reporta el ranking por NORMAL (mean vs CK).</summary>
    Private Sub RunSweep(pm As PluginManager, work As List(Of (Esp As String, Edid As String)),
                         dataPath As String, sweepDir As String, Optional rankBy As String = "n")
        Console.WriteLine("[sweep] resolviendo NPCs + cargando refs CK...")
        Dim ctxs As New List(Of NpcSweepCtx)
        For Each w In work
            Dim c = ResolveSweepNpc(pm, w.Esp, w.Edid, dataPath)
            If c IsNot Nothing AndAlso c.CkN IsNot Nothing Then
                ctxs.Add(c)
            Else
                Console.Error.WriteLine($"[skip] {w.Edid}: sin resolver o sin ref CK _msn")
            End If
        Next
        If ctxs.Count = 0 Then Console.Error.WriteLine("Ningun NPC con ref CK.") : Environment.ExitCode = 1 : Return
        Dim configs = Directory.GetFiles(sweepDir, "*.json").OrderBy(Function(p) p).ToList()
        If configs.Count = 0 Then Console.Error.WriteLine($"No hay *.json en {sweepDir}") : Environment.ExitCode = 1 : Return
        Console.WriteLine($"[sweep] {ctxs.Count} NPCs x {configs.Count} convenciones (decode cacheado entre todas)")

        FaceTintCpuCompositor.BeginBatchDecodeCache()
        Dim tintCache As New Dictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)
        Dim rows As New List(Of (Name As String, Dn As Double, Dx As Integer, Nn As Double, Nx As Integer, Sn As Double, Sx As Integer))
        Try
            For Each cfg In configs
                ApplyConfigJson(cfg)
                Dim dd As New List(Of Double), nn As New List(Of Double), ss As New List(Of Double)
                Dim dmx As Integer = 0, nmx As Integer = 0, smx As Integer = 0
                For Each ctx In ctxs
                    Dim built = FaceTintInputBuilder.Build(ctx.NpcData, ctx.Race, ctx.IsFemale, pm, tintCache,
                                                           ctx.HairLut, ctx.HairColorFormID,
                                                           ctx.HasTextureLighting, ctx.TextureLightingArgb)
                    Dim cpu = FaceTintCpuCompositor.ComposeCpuPipeline(ctx.DBytes, ctx.NBytes, ctx.SBytes, built.Layers, built.RegionSwaps,
                                                                       resolution:=Nothing, diffuseKey:=ctx.DKey, normalKey:=ctx.NKey, specKey:=ctx.SKey)
                    Dim rD = DiffVsCk(cpu.Diffuse, ctx.CkD)
                    Dim rN = DiffVsCk(cpu.Normal, ctx.CkN)
                    Dim rS = DiffVsCk(cpu.Specular, ctx.CkS)
                    If Not Double.IsNaN(rD.Mean) Then dd.Add(rD.Mean) : dmx = Math.Max(dmx, rD.Max)
                    If Not Double.IsNaN(rN.Mean) Then nn.Add(rN.Mean) : nmx = Math.Max(nmx, rN.Max)
                    If Not Double.IsNaN(rS.Mean) Then ss.Add(rS.Mean) : smx = Math.Max(smx, rS.Max)
                Next
                rows.Add((Path.GetFileNameWithoutExtension(cfg), AvgOr(dd), dmx, AvgOr(nn), nmx, AvgOr(ss), smx))
                Dim shown = If(rankBy = "d", AvgOr(dd), If(rankBy = "s", AvgOr(ss), AvgOr(nn)))
                Console.WriteLine($"  [done] {Path.GetFileNameWithoutExtension(cfg)}  {rankBy.ToUpperInvariant()}={shown:F3}")
            Next
        Finally
            FaceTintCpuCompositor.EndBatchDecodeCache()
        End Try

        Dim sel = Function(x As (Name As String, Dn As Double, Dx As Integer, Nn As Double, Nx As Integer, Sn As Double, Sx As Integer)) _
                      If(rankBy = "d", x.Dn, If(rankBy = "s", x.Sn, x.Nn))
        Console.WriteLine($"=== RANKING por {rankBy.ToUpperInvariant()} (mean vs CK, asc) | {ctxs.Count} NPCs ===")
        For Each r In rows.OrderBy(Function(x) If(Double.IsNaN(sel(x)), Double.MaxValue, sel(x)))
            Console.WriteLine($"  {r.Name,-34} N mean={r.Nn,7:F3} max={r.Nx,3}  |  D mean={r.Dn,7:F3} max={r.Dx,3}  S mean={r.Sn,7:F3} max={r.Sx,3}")
        Next
    End Sub

    ''' <summary>Construye la lista de (esp, edid). --list: una linea por NPC, formato 'esp|edid' (o ','),
    ''' o solo 'edid' usando --esp como default. '#' y lineas vacias se ignoran. Sin --list: el par
    ''' (--esp, --edid) unico.</summary>
    Private Function BuildWorkList(opt As CliArgs) As List(Of (Esp As String, Edid As String))
        Dim w As New List(Of (Esp As String, Edid As String))
        Dim defEsp = Path.GetFileName(opt.Esp)
        If opt.ListPath <> "" Then
            For Each lineRaw In File.ReadAllLines(opt.ListPath)
                Dim line = lineRaw.Trim()
                If line = "" OrElse line.StartsWith("#") Then Continue For
                Dim parts = line.Split({"|"c, ","c}, StringSplitOptions.RemoveEmptyEntries)
                If parts.Length >= 2 Then
                    w.Add((Path.GetFileName(parts(0).Trim()), parts(1).Trim()))
                ElseIf parts.Length = 1 AndAlso defEsp <> "" Then
                    w.Add((defEsp, parts(0).Trim()))
                Else
                    Console.Error.WriteLine($"[warn] linea sin esp ni --esp default: '{line}'")
                End If
            Next
        ElseIf opt.Edid <> "" AndAlso defEsp <> "" Then
            w.Add((defEsp, opt.Edid))
        End If
        Return w
    End Function

    ''' <summary>Asegura que el esp este en la load list (si NO esta en el load order activo): lee sus
    ''' masters (TES4 MAST) y los agrega antes, luego agrega el esp. Se carga ULTIMO -> su override gana.</summary>
    Private Sub EnsureEspInLoadList(loadList As List(Of String), espName As String, dataPath As String)
        If loadList.Any(Function(p) String.Equals(p, espName, StringComparison.OrdinalIgnoreCase)) Then Return
        Dim espFull = Path.Combine(dataPath, espName)
        If Not File.Exists(espFull) Then
            Console.Error.WriteLine($"[warn] esp '{espName}' no existe en {dataPath}; se saltea.") : Return
        End If
        Dim probe As New PluginReader() : probe.Load(espFull)
        For Each m In probe.Masters
            If Not loadList.Any(Function(p) String.Equals(p, m, StringComparison.OrdinalIgnoreCase)) Then loadList.Add(m)
        Next
        loadList.Add(espName)
        Console.WriteLine($"[load] +esp NO-activo '{espName}' (masters: {String.Join(", ", probe.Masters)})")
    End Sub

    ''' <summary>Itera AllRecords (key = FormID global), filtra NPC_ por EditorID (case-insensitive) y
    ''' confirma que el RECORD GANADOR provenga del esp dado (rec.SourcePluginName) -- cubre tanto "el esp
    ''' ORIGINA el record" como "el esp lo OVERRIDEA" (cargado ultimo -> gana). 0 si no hay match (con
    ''' fallback a un unico match de EDID en otro plugin).</summary>
    Private Function ResolveEdid(pm As PluginManager, esp As String, edid As String) As UInteger
        ' Si 'edid' es un FormID hex (0x... o 8 digitos), resolver por FaceGenLocalId + plugin que
        ' PROVEE el record ganador (SourcePluginName), igual que el path EDID. Asi --esp <override.esp>
        ' selecciona la version overridden (ej. NPC_BAKETEST.esp sobre Alana de Fallout4.esm), no el originante.
        Dim hexId As UInteger
        If TryHexId(edid, hexId) Then
            Dim want = hexId And &HFFFFFFUI
            Dim hexFallback As UInteger = 0UI, hexFallbackCount As Integer = 0
            For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
                Dim r = kv.Value
                If r Is Nothing OrElse r.Header.Signature <> "NPC_" Then Continue For
                If FaceGenLocalId(kv.Key) <> want Then Continue For
                If String.Equals(r.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then Return kv.Key
                hexFallback = kv.Key : hexFallbackCount += 1
            Next
            If hexFallbackCount = 1 Then
                Console.WriteLine($"[warn] FormID '{edid}' no provisto por '{esp}' pero unico match en otro plugin; usando 0x{hexFallback:X8}.")
                Return hexFallback
            End If
            Return 0UI
        End If
        Dim fallback As UInteger = 0UI, fallbackCount As Integer = 0
        For Each kv As KeyValuePair(Of UInteger, PluginRecord) In pm.AllRecords
            Dim rec = kv.Value
            If rec Is Nothing OrElse rec.Header.Signature <> "NPC_" Then Continue For
            If Not String.Equals(rec.EditorID, edid, StringComparison.OrdinalIgnoreCase) Then Continue For
            If String.Equals(rec.SourcePluginName, esp, StringComparison.OrdinalIgnoreCase) Then Return kv.Key
            fallback = kv.Key : fallbackCount += 1
        Next
        If fallbackCount = 1 Then
            Console.WriteLine($"[warn] EDID '{edid}' no provisto por '{esp}' pero unico match en otro plugin; usando 0x{fallback:X8}.")
            Return fallback
        End If
        Return 0UI
    End Function

    ''' <summary>True si s es un FormID hex (prefijo 0x o exactamente 8 digitos hex) -> val = el FormID.
    ''' Asi un EDID alfanumerico (AlanaSecord, BATCH01_OPT01) NO se confunde con un FormID.</summary>
    Private Function TryHexId(s As String, ByRef val As UInteger) As Boolean
        Dim t = If(s, "").Trim()
        Dim prefixed = t.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
        If prefixed Then t = t.Substring(2)
        If Not (prefixed OrElse t.Length = 8) Then Return False
        If t.Length = 0 OrElse t.Length > 8 Then Return False
        For Each c In t
            If Not Uri.IsHexDigit(c) Then Return False
        Next
        val = Convert.ToUInt32(t, 16)
        Return True
    End Function

    ''' <summary>DIAGNOSTICO (--tints): vuelca tints autorados del NPC + grupos/opciones de tint de la RACE
    ''' (Slot, Index, Flags, EntryType, TTED Default float Y raw u32/int para ver si es float o entero,
    ''' TemplateColors con alpha/templateIndex/CLFM) + el merge. Para crackear la herencia de defaults vs CK.</summary>
    Private Sub TintsNpc(pm As PluginManager, espName As String, edid As String)
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then Console.WriteLine($"[tints] {edid}: no resuelto en {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pm)
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Dim isFemale = npcData.IsFemale
        Console.WriteLine($"=== TINTS {edid} 0x{npcFormID:X8} race=0x{npcData.RaceFormID:X8} female={isFemale} ===")

        Dim npcIdx As New HashSet(Of UShort)()
        Console.WriteLine("-- NPC authored FaceTintLayers --")
        If npcData.FaceTintLayers IsNot Nothing AndAlso npcData.FaceTintLayers.Count > 0 Then
            For Each tl In npcData.FaceTintLayers
                npcIdx.Add(tl.Index)
                Console.WriteLine($"  idx={tl.Index} value={tl.Value} disc={tl.Discriminator} tplColIdx={tl.TemplateColorIndex} color=ARGB(0x{tl.Color.ToArgb():X8})")
            Next
        Else
            Console.WriteLine("  (ninguno)")
        End If

        Dim groups = If(isFemale, race.FemaleTintTemplateGroups, race.MaleTintTemplateGroups)
        Console.WriteLine($"-- RACE tint groups ({If(isFemale, "female", "male")}): {If(groups Is Nothing, 0, groups.Count)} --")
        Dim nTted As Integer = 0, nDenorm As Integer = 0
        If groups IsNot Nothing Then
            For Each grp In groups
                Dim covered = grp.Options IsNot Nothing AndAlso grp.Options.Any(Function(o) npcIdx.Contains(o.Index))
                Console.WriteLine($"  GROUP '{grp.GroupName}' cat={grp.CategoryIndex} opts={If(grp.Options Is Nothing, 0, grp.Options.Count)} covered={covered}")
                If grp.Options Is Nothing Then Continue For
                For Each o In grp.Options
                    Dim raw = BitConverter.ToUInt32(BitConverter.GetBytes(o.DefaultValue), 0)
                    Dim denorm = o.HasDefaultValue AndAlso raw <> 0UI AndAlso Math.Abs(o.DefaultValue) < 0.000001F  ' denormal/0-tiny => era entero
                    If o.HasDefaultValue Then nTted += 1 : If denorm Then nDenorm += 1
                    Dim mark = If(npcIdx.Contains(o.Index), " <NPC>", "")
                    Dim tted = If(o.HasDefaultValue, $"float={o.DefaultValue:G6} raw=0x{raw:X8}(int={CInt(raw)}){If(denorm, " DENORM!", "")}", "<none>")
                    Console.WriteLine($"    opt slot={o.Slot}({SlotName(o.Slot)}) idx={o.Index} '{o.Name}' flags=0x{o.Flags:X4} type={o.EntryType} tex={If(o.Textures Is Nothing, 0, o.Textures.Count)} TTED={tted}{mark}")
                    If o.TemplateColors IsNot Nothing Then
                        For Each tc In o.TemplateColors
                            Dim col = "?"
                            Dim crec = pm.GetRecord(tc.ColorFormID)
                            If crec IsNot Nothing AndAlso crec.Header.Signature = "CLFM" Then
                                Dim clfm = RecordParsers.ParseCLFM(crec, pm)
                                If clfm IsNot Nothing AndAlso clfm.HasColor Then col = $"ARGB(0x{clfm.Color.ToArgb():X8})"
                            End If
                            Console.WriteLine($"        tplCol tplIdx={tc.TemplateIndex} alpha={tc.Alpha:G6} clfm=0x{tc.ColorFormID:X8} {col}")
                        Next
                    End If
                Next
            Next
        End If
        Console.WriteLine($"-- TTED summary: con-TTED={nTted}, denormales(=entero leido como float)={nDenorm} -> {If(nDenorm = 0, "TODOS floats sanos", "HAY enteros/denormales")} --")

        Dim merged = FaceTintInputBuilder.MergeTintLayersWithRaceDefaults(npcData.FaceTintLayers, race, isFemale, pm)
        Console.WriteLine($"-- MERGED ({merged.Count}) --")
        For Each m In merged
            Console.WriteLine($"  idx={m.Layer.Index} value={m.Layer.Value} tplColIdx={m.Layer.TemplateColorIndex} color=ARGB(0x{m.Layer.Color.ToArgb():X8}) raceDefault={m.IsRaceDefault}")
        Next
    End Sub

    ''' <summary>DIAGNOSTICO (--ttedscan): recorre TODAS las RACE, junta el TTED Default de cada opcion de
    ''' tint (male+female), reporta el conjunto de valores distintos (histograma) y cualquier NO-entero. Si
    ''' TODOS son enteros (0,1,2,3...) => TTED es probablemente un INDICE (no una intensidad float). Test de
    ''' la hipotesis del usuario: TTED-como-indice apunta a la opcion default y se aplica con su alpha.</summary>
    Private Sub TtedScan(pm As PluginManager)
        ' Por raw-u32 de TTED: cuenta por EntryType + ejemplos. Asi se ve si TextureSet usa int-indice o
        ' float y si Palette alguna vez usa float. raw normal (>=0x00800000) = float real; raw chico = int.
        Dim byRaw As New Dictionary(Of UInteger, Dictionary(Of String, Integer))
        Dim examples As New Dictionary(Of UInteger, String)
        Dim nonPaletteTted As New List(Of String)   ' TextureSet/Mask (tc=0) con TTED -> listar todas
        Dim total As Integer = 0, races As Integer = 0, withTted As Integer = 0
        For Each rec In pm.GetRecordsOfType("RACE")
            Dim race As RACE_Data = Nothing
            Try
                race = RecordParsers.ParseRACE(rec, pm)
            Catch
                Continue For
            End Try
            If race Is Nothing Then Continue For
            races += 1
            For Each gs In {race.MaleTintTemplateGroups, race.FemaleTintTemplateGroups}
                If gs Is Nothing Then Continue For
                For Each grp In gs
                    If grp.Options Is Nothing Then Continue For
                    For Each o In grp.Options
                        total += 1
                        If Not o.HasDefaultValue Then Continue For
                        withTted += 1
                        Dim raw = BitConverter.ToUInt32(BitConverter.GetBytes(o.DefaultValue), 0)
                        Dim et = $"{o.EntryType}(tc={If(o.TemplateColors Is Nothing, 0, o.TemplateColors.Count)})"
                        If Not byRaw.ContainsKey(raw) Then byRaw(raw) = New Dictionary(Of String, Integer)
                        byRaw(raw)(et) = If(byRaw(raw).ContainsKey(et), byRaw(raw)(et) + 1, 1)
                        If Not examples.ContainsKey(raw) Then examples(raw) = $"{rec.EditorID} slot={o.Slot}({SlotName(o.Slot)}) idx={o.Index} '{o.Name}'"
                        If o.TemplateColors Is Nothing OrElse o.TemplateColors.Count = 0 Then
                            nonPaletteTted.Add($"{rec.EditorID} slot={o.Slot}({SlotName(o.Slot)}) idx={o.Index} '{o.Name}' type={o.EntryType} tex={If(o.Textures Is Nothing, 0, o.Textures.Count)} TTED=0x{raw:X8}(int={raw} float={BitConverter.ToSingle(BitConverter.GetBytes(raw), 0):G6})")
                        End If
                    Next
                Next
            Next
        Next
        Console.WriteLine($"=== TTED SCAN: {races} RACE, {total} opciones, {withTted} con TTED ===")
        Console.WriteLine("por raw-u32 (float / int / denormal=int-index) -> EntryType breakdown:")
        For Each kv In byRaw.OrderBy(Function(x) x.Key)
            Dim raw = kv.Key
            Dim asFloat = BitConverter.ToSingle(BitConverter.GetBytes(raw), 0)
            Dim isNormalFloat = raw >= &H800000UI   ' >= smallest normal float -> es float real
            Dim kind = If(raw = 0UI, "cero", If(isNormalFloat, $"FLOAT={asFloat:G6}", $"INT-index={raw}(denormal)"))
            Dim ets = String.Join(", ", kv.Value.OrderByDescending(Function(x) x.Value).Select(Function(x) $"{x.Key}:{x.Value}"))
            Console.WriteLine($"  raw=0x{raw:X8}  {kind,-22}  [{ets}]   ej: {examples(raw)}")
        Next
        Console.WriteLine($"-- TextureSet/Mask (tc=0) CON TTED ({nonPaletteTted.Count}) --")
        For Each s In nonPaletteTted : Console.WriteLine("  " & s) : Next
    End Sub

    ''' <summary>DIAGNOSTICO (--info): vuelca la cadena de resolucion de base D/N/S de la cara para UN NPC
    ''' (NPC.HeadTexture/FTST, RACE default por genero, lo que devuelve §3 HOY, y por cada HeadPart su
    ''' PartType + HDPT.TextureSet + el material inline del NIF) con dims de cada DDS. Permite ver con keys
    ''' exactos que usa el app vs el CLI sin componer. No escribe nada.</summary>
    Private Sub InfoNpc(pm As PluginManager, espName As String, edid As String)
        Dim npcFormID = ResolveEdid(pm, espName, edid)
        If npcFormID = 0UI Then Console.WriteLine($"[info] {edid}: no resuelto en {espName}") : Return
        Dim npcRec = pm.GetRecord(npcFormID)
        Dim npcData = RecordParsers.ParseNPC(npcRec, npcRec.SourcePluginName, pm)
        Dim raceRec = pm.GetRecord(npcData.RaceFormID)
        Dim race = RecordParsers.ParseRACE(raceRec, pm)
        Console.WriteLine($"=== INFO {edid} 0x{npcFormID:X8} src='{npcRec.SourcePluginName}' race=0x{npcData.RaceFormID:X8} female={npcData.IsFemale} ===")

        Console.WriteLine("-- NPC.HeadTexture (FTST nivel-NPC, field 418) --")
        PrintTxst(pm, "NPC.HeadTexture", npcData.HeadTextureFormID)
        Console.WriteLine("-- RACE default face TXST (por genero) --")
        PrintTxst(pm, "RACE.default", If(npcData.IsFemale, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID))

        Console.WriteLine("-- §3 ResolveFaceSkin (lo que el CLI usa HOY) --")
        Dim d3 As String = "", n3 As String = "", s3 As String = ""
        ResolveFaceSkin(npcData, race, pm, d3, n3, s3)
        Console.WriteLine($"      D={DdsInfo(d3)}")
        Console.WriteLine($"      N={DdsInfo(n3)}")
        Console.WriteLine($"      S={DdsInfo(s3)}")

        Console.WriteLine("-- HeadParts (HDPT: PartType, HDPT.TextureSet, material inline del NIF) --")
        If npcData.HeadPartFormIDs IsNot Nothing Then
            For Each hpId In npcData.HeadPartFormIDs
                Dim rec = pm.GetRecord(hpId)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Console.WriteLine($"  HDPT 0x{hpId:X8} no resuelto") : Continue For
                Dim hdpt = RecordParsers.ParseHDPT(rec, pm)
                Console.WriteLine($"  HDPT 0x{hpId:X8} '{rec.EditorID}' partType={hdpt.PartType} src='{rec.SourcePluginName}' mesh='{hdpt.MeshPath}'")
                PrintTxst(pm, "    HDPT.TextureSet", hdpt.TextureSetFormID)
                If String.IsNullOrWhiteSpace(hdpt.MeshPath) Then Continue For
                Dim mp = hdpt.MeshPath.Replace("/"c, "\"c).TrimStart("\"c)
                If Not mp.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) Then mp = "Meshes\" & mp
                Dim nifBytes = FilesDictionary_class.GetBytes(mp)
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then Console.WriteLine($"    NIF sin bytes (key='{mp}')") : Continue For
                Try
                    Dim nif As New Nifcontent_Class_Manolo() : nif.Load_Manolo(nifBytes)
                    For Each kv In nif.BaseMaterials
                        Dim rm = kv.Value
                        If rm Is Nothing OrElse rm.material Is Nothing Then Continue For
                        Dim m = rm.material
                        Console.WriteLine($"    NIF mat '{kv.Key}' shader={m.NifShaderType} hair={m.Hair}")
                        Console.WriteLine($"        D={DdsInfo(m.Diffuse_or_Base_Texture)}")
                        Console.WriteLine($"        N={DdsInfo(m.NormalTexture)}")
                        Console.WriteLine($"        S={DdsInfo(m.SmoothSpecTexture)}")
                    Next
                Catch ex As Exception
                    Console.WriteLine($"    NIF load fallo: {ex.Message}")
                End Try
            Next
        End If
    End Sub

    ''' <summary>Imprime un TXST (D/N/S + dims + MNAM/bgsm) para --info.</summary>
    Private Sub PrintTxst(pm As PluginManager, label As String, formId As UInteger)
        If formId = 0UI Then Console.WriteLine($"  {label}: 0 (none)") : Return
        Dim rec = pm.GetRecord(formId)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then
            Console.WriteLine($"  {label}: 0x{formId:X8} no es TXST (sig={rec?.Header.Signature})") : Return
        End If
        Dim t = RecordParsers.ParseTXST(rec, pm)
        Console.WriteLine($"  {label}: 0x{formId:X8} src='{rec.SourcePluginName}'")
        Console.WriteLine($"      D={DdsInfo(t.DiffuseTexture)}")
        Console.WriteLine($"      N={DdsInfo(t.NormalTexture)}")
        Console.WriteLine($"      S={DdsInfo(t.SmoothSpecTexture)}")
        If Not String.IsNullOrEmpty(t.MaterialPath) Then Console.WriteLine($"      MNAM(bgsm)='{t.MaterialPath}'")
    End Sub

    ''' <summary>Path + dims (WxH) + tamaño del DDS para --info (sin decodificar full; lee el header).</summary>
    Private Function DdsInfo(rawPath As String) As String
        If String.IsNullOrEmpty(rawPath) Then Return "<vacio>"
        Dim key = FO4UnifiedMaterial_Class.CorrectTexturePath(rawPath)
        Dim b = FilesDictionary_class.GetBytes(key)
        If b Is Nothing OrElse b.Length < 20 Then Return $"'{rawPath}' (sin bytes)"
        Dim h = BitConverter.ToInt32(b, 12), w = BitConverter.ToInt32(b, 16)
        Return $"'{rawPath}' {w}x{h} {b.Length}b"
    End Function

    ''' <summary>Diffuse/Normal/SmoothSpec de la cara: NPC.HeadTexture (FTST) o, si 0, el default
    ''' de la RACE por genero. Mismo source que el fallback Face del render (record-puro).</summary>
    Private Sub ResolveFaceSkin(npcData As NPC_Data, race As RACE_Data, pm As PluginManager,
                                ByRef d As String, ByRef n As String, ByRef s As String)
        Dim txstId = npcData.HeadTextureFormID
        If txstId = 0UI AndAlso race IsNot Nothing Then
            txstId = If(npcData.IsFemale, race.FemaleDefaultFaceTextureFormID, race.MaleDefaultFaceTextureFormID)
        End If
        If txstId = 0UI Then Return
        Dim rec = pm.GetRecord(txstId)
        If rec Is Nothing OrElse rec.Header.Signature <> "TXST" Then Return
        Dim txst = RecordParsers.ParseTXST(rec, pm)
        d = txst.DiffuseTexture
        n = txst.NormalTexture
        s = txst.SmoothSpecTexture
    End Sub

    ''' <summary>Hair-LUT AUTORITATIVO headless: GreyscaleTexture del material BGSM (flag Hair) del NIF del
    ''' HeadPart de pelo del NPC. Carga el NIF desde bytes (Nifcontent_Class_Manolo.Load_Manolo, sin GL) y
    ''' lee el material YA PARSEADO (BaseMaterials). Espeja MainForm.ResolveHairPaletteTexture (BGSM gana
    ''' sobre RACE.HNAM) pero sin el modelo renderizado. Si ningun HeadPart resuelve un material Hair con
    ''' GreyscaleTexture, cae al lookup de la RACE (record-puro).</summary>
    Private Function ResolveHairLut(npcData As NPC_Data, race As RACE_Data, pm As PluginManager) As String
        If npcData.HeadPartFormIDs IsNot Nothing Then
            For Each hpId In npcData.HeadPartFormIDs
                Dim rec = pm.GetRecord(hpId)
                If rec Is Nothing OrElse rec.Header.Signature <> "HDPT" Then Continue For
                Dim hdpt = RecordParsers.ParseHDPT(rec, pm)
                If hdpt Is Nothing OrElse String.IsNullOrWhiteSpace(hdpt.MeshPath) Then Continue For
                Dim mp = hdpt.MeshPath.Replace("/"c, "\"c).TrimStart("\"c)
                If Not mp.StartsWith("meshes\", StringComparison.OrdinalIgnoreCase) Then mp = "Meshes\" & mp
                Dim nifBytes = FilesDictionary_class.GetBytes(mp)
                If nifBytes Is Nothing OrElse nifBytes.Length = 0 Then Continue For
                Try
                    Dim nif As New Nifcontent_Class_Manolo()
                    nif.Load_Manolo(nifBytes)
                    For Each kv In nif.BaseMaterials
                        Dim rm = kv.Value
                        If rm IsNot Nothing AndAlso rm.material IsNot Nothing AndAlso rm.material.Hair _
                           AndAlso Not String.IsNullOrWhiteSpace(rm.material.GreyscaleTexture) Then
                            Return rm.material.GreyscaleTexture
                        End If
                    Next
                Catch ex As Exception
                    ' NIF no soportado / sin material Hair -> seguir con el siguiente HeadPart / fallback RACE
                End Try
            Next
        End If
        Return ResolveHairLutRaceFallback(race)
    End Function

    ''' <summary>Fallback: hair-LUT = lookup de la RACE (HairColorLookupTexture / extended), record-puro.</summary>
    Private Function ResolveHairLutRaceFallback(race As RACE_Data) As String
        If race Is Nothing Then Return ""
        For Each t In {race.HairColorLookupTexture, race.HairColorExtendedLookupTexture}
            If Not String.IsNullOrWhiteSpace(t) Then Return t
        Next
        Return ""
    End Function

    ''' <summary>FormID local del FaceGen segun CK: full plugins &amp; 0xFFFFFF; ESL (high byte 0xFE)
    ''' &amp; 0xFFF (record de 12 bits). Espejo de FaceGenBuilder.FaceGenLocalId.</summary>
    Private Function FaceGenLocalId(npcFormID As UInteger) As UInteger
        If (npcFormID >> 24) = &HFEUI Then Return npcFormID And &HFFFUI
        Return npcFormID And &HFFFFFFUI
    End Function

    Private Sub WriteChannel(dir As String, localId As UInteger, suffix As String, ch As FaceTintCpuCompositor.CpuChannelResult)
        If ch Is Nothing OrElse ch.Bgra Is Nothing Then
            Console.Error.WriteLine($"[warn] {localId:X8} canal '{suffix}' vacio, skip") : Return
        End If
        FaceTintCompositor.WriteBgraToTga(Path.Combine(dir, $"{localId:X8}_{suffix}_3.tga"), ch.Bgra, ch.Width, ch.Height)
    End Sub

    Private Function ParseArgs(args As String()) As CliArgs
        Dim a As New CliArgs()
        Dim i As Integer = 0
        While i < args.Length
            Dim k = args(i).ToLowerInvariant()
            Dim v = If(i + 1 < args.Length, args(i + 1), "")
            Select Case k
                Case "--esp" : a.Esp = v : i += 2
                Case "--edid" : a.Edid = v : i += 2
                Case "--list" : a.ListPath = v : i += 2
                Case "--config" : a.ConfigPath = v : i += 2
                Case "--convention" : a.ConventionPath = v : i += 2
                Case "--sort" : a.SortPath = v : i += 2
                Case "--data" : a.DataPath = v : i += 2
                Case "--out" : a.OutDir = v : i += 2
                Case "--sweep" : a.SweepDir = v : i += 2
                Case "--dump" : a.DumpDir = v : i += 2
                Case "--rankby" : a.RankBy = v.ToLowerInvariant() : i += 2
                Case "--info" : a.Info = True : i += 1
                Case "--tints" : a.Tints = True : i += 1
                Case "--ttedscan" : a.TtedScan = True : i += 1
                Case "-h", "--help" : PrintUsage() : Return Nothing
                Case Else
                    Console.Error.WriteLine($"Arg desconocido: {args(i)}") : PrintUsage() : Return Nothing
            End Select
        End While
        If a.ListPath = "" AndAlso (a.Esp = "" OrElse a.Edid = "") AndAlso Not a.TtedScan Then
            Console.Error.WriteLine("Faltan --esp y --edid (o usa --list).") : PrintUsage() : Return Nothing
        End If
        Return a
    End Function

    Private Sub PrintUsage()
        Console.WriteLine("FO4_FaceTint_CLI (--esp <plugin> --edid <EditorID> | --list <file>) [opciones]")
        Console.WriteLine("  --list <file>          una linea por NPC: 'esp|edid' o solo 'edid' (usa --esp). '#'=comentario")
        Console.WriteLine("  --esp <plugin>         plugin del NPC (default para las lineas de --list sin esp)")
        Console.WriteLine("  --config <config.json> leer las secciones FaceTint del config.json del app")
        Console.WriteLine("  --convention <f.json>  override de Setting_FaceTintConvention")
        Console.WriteLine("  --sort <f.json>        override de Setting_FaceTintSort")
        Console.WriteLine("  --data <ruta Data\>    Data path (default: config.json)")
        Console.WriteLine("  --out <dir>            carpeta de salida (default: FaceCustomization\<plugin>)")
        Console.WriteLine("  --sweep <dir-configs>  barre cada config .json del dir vs CK (UNA carga) y rankea por Normal")
        Console.WriteLine("  --dump <dir>           ademas escribe los MASKS (inputs: BASEIN + layers + swaps + regionmasks + LUT) a <dir>\<localId>")
        Console.WriteLine("Salida por NPC: <localId>_d_3.tga / _msn_3.tga / _s_3.tga")
        Console.WriteLine("Batch (--list): monta plugins+archivos UNA vez; cada DDS se decodifica una sola vez.")
    End Sub

End Module
